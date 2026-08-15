# Wave 0 → Wave 1 handoff

Wave 0 (tick correctness prerequisites) is code complete, reviewed and merged into
`event-system/wave-0`. This file is written for a session that was not here and has none of the
context.

---

## Ready-to-paste prompt for the next orchestrator

> You are the orchestrator for **Wave 1 — sensors and city statistics** of the AGORA event-system
> rework. **Begin with `/nextwave`.** Read `docs/plans/0004-event-system-rework.md` (the plan),
> `docs/plans/0004-wave-0-handoff.md` (this file — it **outranks the plan** wherever the two
> disagree, because it was written against the code) and `docs/status.md`.
>
> Wave 0 closed two pre-existing tick defects and is merged. Before you start, confirm wave 0's PR
> is merged into `EventSystemRefresh`; `/nextwave` step 2 then has you prove the base builds and
> tests green and record the count yourself — do it, because wave 0 opened on a base that did
> neither while the plan claimed otherwise.
>
> Wave 1's spine is a scout report, `CitySnapshot` v4, `snapshot.schema.json`, and the metric-history
> migration — **but read "Contradictions with the plan" in the handoff first: the metric-history
> bump may no longer be needed, and one wave-1 lane's premise has changed.**

---

## State of the world, in one paragraph

AGORA's political engine ticks once per sim month. Two defects made that tick unreliable in ways that
were cosmetic before this rework and become severe the moment a tick carries a score. First, the
runtime decided "the month changed" from session-local fields that a reload cleared, so **every
reload re-ran a month it had already advanced through** — a duplicated poll and a double-counted
`FringeWatch.MonthsObserved` today, unbounded political-power farming by save-scumming once the story
system lands. Second, the engine's snapshot history was session-static and **died at every save
boundary**, so every `delta` and `windowMonths` trend read empty on the first tick after a load. Wave
0 closed both: a persisted `LastCompletedTickMonth` watermark gates the month, and the already-
existing `MetricHistory` ring was widened and taught to rebuild past snapshots from disk. Wave 0 also
had to repair a red base branch before it could start. Nothing in wave 0 is story-specific; it stands
on its own merits.

## PR

**PR:** *(link added on creation — see the wave-0 PR into `EventSystemRefresh`)*
**Merge status: NOT merged.** The owner reviews. Wave 1 must not open its umbrella until it is in.

## What actually shipped

Thirteen commits on `event-system/wave-0`. The two that are not spine or lane:

| Commit | Why it exists |
|---|---|
| `59dff44` **Repair the red baseline** | `EventSystemRefresh` did not build (`AnchoredBrandRepair.Apply` passed `IList<Party>` where `IReadOnlyList<Party>` was required) and five tests were red. All were stale expectations from the previous three commits landing unverified, not engine defects. |
| `bd6ff89` **Teach `/nextwave` what wave 0 learned** | Process fixes, listed at the end of this file. |

### The spine (`efaa330`, plus fixup `6d7f7f2`)

| File | Change |
|---|---|
| `src/Agora.Core/Contracts/PoliticalState.cs` | `LastCompletedTickMonth` (`int`, default `-1`); `SchemaVersion` default reconciled 3 → 5 |
| `src/Agora.Mod/Persistence/SidecarSchema.cs` | `CurrentStateVersion` 4 → 5; `MigrateStateV4ToV5` seeding from the document's own `date`; `TryReadTotalMonths` |
| `src/Agora.Core/Engine/PoliticalEngine.cs` | `CloneState` carries the watermark (the `6d7f7f2` fixup — see "What nearly went wrong") |
| `src/Agora.Core/Tuning/EngineTuning.cs` | `PollTickIntervalDays` → `PollTickIntervalMonths`, default 1 |
| `src/Agora.Core/Events/Scheduler/TickPlanner.cs` | `IsPollTick` becomes a month cadence gated on `engineTick` |
| `data/engine_tuning.json`, `data/schemas/engine_tuning.schema.json` | key renamed; `schemaVersion` 4 → 5 (`4564822`) |
| `src/Agora.Mod/Sensors/SnapshotRehydration.cs` | seam stub, filled in by lane 0b |

### The lanes

| Lane | Delivered | Review |
|---|---|---|
| **0a** `AgoraRuntime.cs` | The gate (`today.TotalMonths > watermark`), the watermark write in `OnMonth` and per replayed month in `Replay`, `_hasTicked`/`_lastTick` demoted to a logging latch, `_snapshotHistory` seeded on load, and a clamp stopping a state dated ahead of the clock from freezing the layer. | **Blocked twice**, approved on the third pass |
| **0b** `MetricHistory.cs`, `AgoraSnapshotSystem.cs`, `SnapshotRehydration.cs` | Widened the recorded series and implemented rehydration. `RecordSnapshot(CitySnapshot)` lives on `MetricHistory`, deliberately, so tests drive the real recorder. | Approved first pass |
| **0c** `tests/**` | +27 tests: the v4→v5 migration and its idempotency, the poll cadence, the golden rehydration test, and a reflective `CloneState` guard. | Approved first pass |

**Zero merge conflicts across three lanes.** The spine-first, disjoint-paths law worked.

---

## Contradictions with `docs/plans/0004-event-system-rework.md`

**These outrank the plan. Wave 1 plans against two of them.**

1. **`metric_ring.json` was never built, and should not be.** The plan has wave 0 create a new
   persisted metric ring as its own `SidecarDocument`. `src/Agora.Mod/Sensors/MetricHistory.cs` +
   `MetricHistoryFile.cs` already *were* one — bounded, sorted, migrated through
   `SidecarSchema.Migrate`, reload-surviving, and already `<Compile Link>`ed into the test project.
   They recorded two series. Wave 0 widened them instead. **Strike the `metric_ring.json` row from
   Part IV.**

2. **Wave 1's `metric_history` v1 → v2 bump is probably unnecessary.** The plan bumps it "for the new
   trend windows". The file is a **keyed series bag** — new metrics are new keys, not a new shape —
   so wave 0 added roughly sixteen new series per scope without touching the schema at all. Do not
   bump it reflexively; bump it only if you genuinely change the document's *shape*. Note
   `SidecarSchema`'s own comment: `MetricHistorySteps` is empty and reached, which is safe only while
   `CurrentMetricHistoryVersion` is 1. **Bumping the constant without adding a 1 → 2 step turns every
   existing history into `NoPathForward`, i.e. silently discards it.**

3. **`TickPlanner.cs:120` had no arithmetic slip to fix.** The plan calls it a latent bug where
   `((date.Day - 1) % pollDays) == 0` was wrong. It was `(0 % 7) == 0` — unconditionally true, for
   every setting, because `SimDate.Day` is a literal `1` on every date the clock produces. There was
   nothing to correct, only an intent to decide. **Owner decision: reinterpret as months**, shipped
   as `pollTickIntervalMonths` with default `1`, which is behaviour-identical for every existing
   save while making the dial real for the first time. Wave 7b owns any actual retune.

4. **The plan's baseline of "1319 tests, green" was wrong on both counts.** It was 1415, and the
   branch was red. It is now **1442**.

5. **Only `IndicesEngine.Compute` reads `SnapshotHistory`**, and only `Population` + `Education` off
   the city and `Education` + `Wealth[Low]` off each district. Verified by grep, twice, in review.
   That closed set is what made honest rehydration possible rather than a bag of zeros. If a later
   wave adds a historical read, the golden test in `SnapshotRehydrationTests.cs` fails — by design.

---

## What nearly went wrong, and what wave 1 should carry

Two defects reached review rather than the merge, both invisible to a clean build and a full green
suite because `AgoraRuntime` compiles into no test.

- **`PoliticalEngine.CloneState` silently dropped the new watermark.** It is a hand-maintained field
  list; a missing scalar does not fail to compile, it arrives at the type default. `Retheme` clones
  mid-month, so pressing the region button re-armed the double-tick and persisted `-1` to disk.
  **Wave 2 adds five more collections to `PoliticalState` and must not repeat this.**
  `CloneStateCoverageTests` now fails by name when a property is not carried.
- **A boundary written as `<=` where `<` was meant.** The clamp guarding against a state dated ahead
  of the clock was specified by the orchestrator with the wrong operator and implemented faithfully.
  Equality is the ordinary mid-month reload, so it would have fired on every normal save/quit/reload
  and re-run the month — a more elaborate version of the exact bug the wave existed to remove.

### Two traps aimed squarely at wave 2

- **`CloneStateCoverageTests.Properties()` filters on `CanWrite`.** If any of wave 2's five new
  `PoliticalState` members is declared get-only with an initializer, **the guard silently skips it**
  and the hole it was built to close reopens. Give every new member a setter.
- **`pollTickIntervalMonths: 0` is expressible and permanently disables polling.**
  `TuningReader.Int` does no range check, though `engine_tuning.schema.json` types the key
  `positiveInt`; `TickPlanner.OnInterval` treats non-positive as "never", and unlike
  `TickIntervalMonths` it gets no floor. `TickPlan_ANonPositivePollInterval_NeverPolls` now codifies
  "non-positive means never" as intended planner contract, so whoever closes this must clamp **in
  the reader or at the call site, not in `OnInterval`**.

---

## Manual gates opened by wave 0 and not yet walked

Nothing below has been seen in game. `AgoraRuntime` is not linkable into the headless suite by
design, so these are gate rows rather than tests, and no test was manufactured for them.

1. **The double-tick.** Save mid-month, quit to menu, reload. `Agora.log` and the sidecar must show
   the month running **once** — no duplicate poll, no double-counted `FringeWatch.MonthsObserved`.
   This is the gate the whole political-power economy later rests on.
2. **The clamp must not fire on an ordinary reload.** On the reload above, the reconciliation line
   must be **absent**. Its presence is exactly the defect review caught twice.
3. **The rewound load.** Roll a city save back past the oldest retained Agora snapshot and load it.
   The reconciliation line appears **once**, and the next month boundary actually ticks — the freeze
   would not show up in gate 1 at all.
4. **Retheme.** Change region mid-month in a month that has already ticked, then let the month turn.
   The month must not run twice.
5. **The trend window survives a reload.** Play twelve months, quit to menu, reload, and confirm from
   `Agora.log` that gentrification and brain-drain indices are non-zero on the first tick after the
   load rather than starting from nothing.

## Housekeeping

- Lane worktrees removed and branches deleted; `git worktree list` is clean.
- **`ui/node_modules` is now installed in `.claude/worktrees/EventSystem/ui`** (needed for
  `npx tsc --noEmit`, which is clean). Consequence: `dotnet build Agora.sln` in that worktree will
  now **deploy to the player's live `…\Mods\Agora.Mod`**. Wave 0 ran **no deploying build** — the
  wave changed no UI and deploying a mid-rework build was the owner's call, not the orchestrator's.
- `/nextwave` was updated with wave 0's process lessons (`bd6ff89`): prove the base green before
  cutting, land a compiling stub for every seam, update hand-maintained copy lists in the same
  commit, publish both ends of a seam, hand lanes invariants rather than literal operators, and give
  reviewers the specific failure mode to hunt.

## Verification recorded

- `dotnet build Agora.sln` — succeeded, 0 errors.
- `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj` — **1442 passed, 0 failed** (from 1415).
- `cd ui && npx tsc --noEmit` — clean. No `ui/` file changed this wave.
