# Wave 4 → Wave 5 handoff

Wave 4 (tick wiring, effects and persistence) is code complete, reviewed and merged into
`event-system/wave-4`. This file is written for a session that was not here and has none of the
context.

---

## Ready-to-paste prompt for the next orchestrator

> You are the orchestrator for **Wave 5 — prose** of the AGORA event-system rework. **Begin with
> `/nextwave`.** Read `docs/plans/0004-event-system-rework.md` (the plan),
> `docs/plans/0004-wave-4-handoff.md` (this file — it **outranks the plan** wherever the two
> disagree, because it was written against the code) and `docs/status.md`.
>
> Wave 4 made the story system run. The tick now drafts stories, resolves them, dispatches capped
> effects, moves political power, and moves votes. **Nothing renders** — no player has ever seen a
> story. Wave 5 writes the prose that will be shown, wave 6 builds the surface that shows it.
>
> Confirm wave 4's PR is merged into `EventSystemRefresh` before you cut anything; `/nextwave` step 2
> then has you prove the base builds and tests green and record the count **yourself**.
>
> Before you design the spine, read **"Contradictions with the plan"** and **"Traps aimed squarely at
> wave 5"** below. Two items will cost you a lane each if found late: **the four story commands have
> no binding and no caller**, and **`politics_flavor` has two copies that a drift test pins
> together**.

---

## State of the world, in one paragraph

Before this wave the engine could decide what a city's political stories are and had 58 authored
civic events to decide from, and **nothing called any of it**. Now `TickPlanner` marks a draft phase
and a resolve phase; `PoliticalEngine` runs a story stage between the event scan and the indices;
`StoryCycle` sweeps stranded stories, resolves due ones, drafts the next batch and trims the archive;
`StoryEffects` turns authored effect ids into capped `EffectRequest`s; `StoryPressure` derives what
the voter model reads; `AffinityEngine` gained a **story term that did not previously exist**, so a
story's issues and its verdict now move votes; `PowerLedger` accrues, awards, spends and charges debt;
and `AgoraRuntime` finally **loads the civic catalog**, which nothing in the whole assembly had ever
mentioned. All 90 generically-wrapped timeline events now author an `issuePressure`, so the timeline
half of the catalog is no longer inert. **What is still true: no player has seen any of it.** There
is no story card, no modal, no prose, and the four inbound commands have no caller and no binding.

## PR

**PR:** https://github.com/SeraphinHesse/Cs2CompanionApp/pull/7
**Merge status: NOT merged.** The owner reviews. Wave 5 must not open its umbrella until it is in.

---

## What actually shipped

**Zero merge conflicts across eight lanes**, the fifth wave to prove the spine-first law and the
widest test of it so far.

### The spine

| File | Change |
|---|---|
| `Events/Scheduler/TickPlanner.cs` | `TickPlan.IsStoryDraft` / `IsStoryResolve` |
| `Engine/PoliticalEngine.cs` | stage 3b + `GoverningVoteShare` helper |
| `Engine/EngineTick.cs` | `CivicCatalog`, `IsReplay` in; `DraftedStories`, `ResolvedStories`, `PowerDelta` out |
| `Engine/Affinity/AffinityEngine.cs` + `AffinityRequest.cs` | **`StoryTerm`** — the term that did not exist |
| `Contracts/Blocs.cs` | `BlocAffinity.StoryComponent` |
| `Contracts/CommandOutcome.cs` | `InsufficientPower = 11`, `AlreadyResolved = 12`, `PowerDisabled = 13` |
| `Stories/StoryCycleTypes.cs` · `StoryPressureContribution.cs` | new spine-owned seam types |
| `Stories/PoliticalPowerState.cs` | `PlayerCommand.DeclaredMet` + **`PlayerCommandLog.Append`** |
| `Stories/Catalog/CivicEventCatalog.cs` | `CivicEventCatalog.Empty` |
| `Tuning/EngineTuning.cs` · `TuningPresets.cs` · `data/engine_tuning.json` + schema | 4 new keys · **schemaVersion 7 → 8** |
| `data/schemas/timeline.schema.json` | `issuePressure` declared — **version deliberately unchanged** |
| `src/Agora.Mod/Core/AgoraRuntime.cs` | `LoadCivicCatalog()`, `CivicCatalog`, `IsReplay` wiring, **`ClampWatermarkToClock` repairing all four watermarks** |
| `tests/…/StoryCyclePhaseTests.cs` · `PlayerCommandLogTests.cs` | the spine's own guards |

### The lanes

| Lane | Delivered | Review |
|---|---|---|
| **4a** `StoryCycle.cs` | The cycle, the stranded sweep, the forward-dated reap, the district choice | **Blocked once** — half the block was the orchestrator's |
| **4b** `AgoraRuntime.StoryCommands.cs` | Four commands, 14 gate rows | **Blocked once**, 5 findings; 2 were spine defects |
| **4c** `StoryEffects.cs` + `StoryPressure.cs` | Effects, breadth cap, salience/credit split | **Blocked once**, 3 findings, all measured |
| **4d** `PowerLedger.cs` | Accrual guard, awards, `TrySpend`, debt penalty | **Blocked once**, 2 findings |
| **4e** two test files | 34 tests: reload matrix, sweep, replay, evidence | **Blocked once** — over-broad assertions |
| **4f/4g/4h** `timeline_*.json` | 90 wrapped events given a pressure | 4f **blocked once**; 4g/4h approved first pass |

**The suite went 1978 → 2109 (+131).**

---

## Contradictions with `docs/plans/0004-event-system-rework.md`

**These outrank the plan.** Wave 5 plans against them.

1. **The story cycle lives in `Agora.Core`, not `AgoraRuntime.Stories.cs`.** The plan put it on the
   Mod side. It is `Stories/StoryCycle.cs`, called from `PoliticalEngine` stage 3b, because
   `AgoraRuntime` compiles into no test and the idempotence guards are exactly what must be provable.
   `AgoraRuntime.Stories.cs` **does not exist and should not be created**.
2. **`AgoraTreasurySystem` was never built and must not be.** There is no `kind: "money"` effect and
   no `PlayerMoney` debit; the debt penalty ships as the capped palette entry
   `city-service-building-upkeep`. The plan's "primary route" is struck.
3. **`EngineTickInput` did not gain `PlayerCommands` / `ResolveEarlyRequests`.** The command log is
   engine state on `PoliticalState`, and `Story.ResolveEarlyRequested` is on the story. Putting them
   on the tick input would have made the log an argument instead of state.
4. **The plan's §605 table lists three inbound bindings; four are needed.** It assumed the panel
   would send `PowerOverride` through `setResponse`. Wave 4 **refuses** that — it is an
   unconditional-`Met` response with no ledger entry, i.e. free — so `spendPowerOverride` needs a
   call binding of its own. Also, **none of the four is in `docs/contracts/ui_bindings.md` yet**, and
   the method is named `declareManual`, not `declareOutcome`.
5. **`timeline` schemaVersion did not bump and must not.**
   `TimelineCatalogLoader.SupportedSchemaVersion` is a hard equality, so a bump is a rejection rather
   than a migration. `issuePressure` is optional and always was readable. Pinned by
   `StoryCyclePhaseTests.TheTimelineSchemaVersionDidNotMove`.
6. **`stories.maxStoryEffectsPerModifier` bounds one story, never the cycle.** It is applied per
   `ForResolution` call. What bounds concurrency is `stories.resolutionEffectMonths` (new this wave),
   which decides how many cycles overlap.

---

## Traps aimed squarely at wave 5

- **`politics_flavor` has two copies and a test pins them.** The plan's §Wave 5 spine says
  `data/schemas/politics_flavor.schema.json` → **schemaVersion 3** *and* the verbatim duplicate in
  `src/Agora.Mod/Llm/FlavorSchema.cs` `EmbeddedJson` — because `data/` is not deployed for that path.
  `FlavorSchemaDriftTests` guards the copy and will go red if you move one side only.
  `FlavorSchema.SupportedSchemaVersion` is **2** today and `CurrentFlavorCacheVersion` is **2**; both
  are sidecar-adjacent, so this is the first wave since 1 that **does** owe a real migration and a
  fixture test at the old version.
- **Non-negotiable #1 is the whole risk of this wave.** No number entering engine state may originate
  from Claude output. The flavor validator already enforces it; wave 5 is where the temptation to let
  prose carry a figure is highest. A numeric field anywhere in `politics_flavor.json` is a
  review-blocking defect.
- **Article limits triple** (headline 90 → 270, body 420 → 1260) per owner decision 4. Confirm both
  the schema and the C# copy move together.
- **The story surface has no caller.** `AgoraRuntime.StoryCommands.cs` exists and compiles and is
  reachable from nothing. If wave 5 wants a prose surface to point at a story, the binding work is
  wave 6's — do not quietly do it here without amending the contract.
- **A story lives `cycleMonths - 1` months — ONE, not two.** Repeated for a third wave because it is
  still the costliest available mistake. `IsStoryResolve` is now `phase == cycleMonths - 1`, tied to
  `StoryAssembler.NewStory`, and pinned by a theory at cadences 2, 3, 4 and 6.
- **`AgoraRuntime` is not compile-linked into the test suite.** Verified this wave: it appears
  nowhere in `Agora.Core.Tests.csproj`. Two lanes claimed otherwise. Anything you put there gets a
  gate row, never a test, and **faking the runtime to manufacture coverage is a review-blocking
  defect**.

---

## Known gaps, recorded rather than closed

- **`PoliticalPowerEnabled` is guarded at call sites, not in the seam.** `PowerLedger` and
  `PoliticalPower` take only `EngineTuning`, so the per-save setting is honoured by each caller —
  two copies today (`StoryCycle.MovePower` and `AgoraRuntime.StoryCommands`), and wave 6's cost quote
  would be a third. The fix is to pass `AgoraSettings` into the seam and delete all three; it is an
  owner-deferred decision, not an oversight, and `StoryCycle.MovePower`'s remarks say so.
- **A story's district target can drift mid-story.** The choice is per tick (most populous district,
  tie-broken by ordinal id). `EffectLedger.IdentityKey` includes the district but `SourceId` does
  not, so if the largest district changes while a story is live, the previous entry is not replaced
  and one story's district effect applies in two places at once. Bounded (active effects last
  `cycleMonths`) and self-healing. Fixing it properly needs a district on `Story`, which is persisted
  state and a sidecar migration.
- **Two severity ceilings, aligned only by coincidence.** `catalog.severityMax` (5) bounds what
  `StoryPressure` writes; `AffinityEngine.MaxEventSeverity` (a private const 5) is what divides.
  Retune either and the whole story term silently rescales. Recorded on `StoryPressure`'s remarks;
  reconciling them is a spine decision about which is the authority.
- **`ForResolution_NeverExceedsTheDeclaredMagnitudeCap` never drives past the cap.** At shipped
  tuning the worst case is 0.99 of the cap, so it asserts on values that never clamp. The behaviour
  is correct (probed), but the proof is missing — one more `InlineData` past 1/1.8 closes it.
- **`data/CLAUDE.md`'s file list still does not mention `events_*.json` or
  `timeline_adaptation.json`.** Carried from wave 3. Wave 7d owns that doc.
- **Everything wave 3 recorded is still open**: `districtAffinity` empty on every event, `rent` and
  `uncollectedGarbage` ungroundable, `commuteMinutes` unvalidated, `trafficCongestion` possibly
  sign-inverted, and three `notes` in `events_eu.json` asserting bars above drift without evidence.

---

## Manual gates opened by wave 4 — fifteen, none walked

Full text in `docs/status.md` § "Wave 4's manual gates".

**Gate 0 is the one that matters, and it has no test by construction.** The rewound load must
reconcile **every** watermark: roll a city save back past the oldest retained Agora snapshot, and
confirm within three sim months that a story drafts **and** power accrues, and that the
reconciliation line names the story and accrual watermarks — appearing **once**, and **not at all**
on an ordinary mid-month reload. Before the fix the tick gate opened correctly, so polls, elections
and failure penalties all ran, while nothing story-shaped happened and no log line said so.

Rows 1–14 cover the command surface and **cannot run until wave 6 wires a pressable modal**. Row 8 is
the three-step double-charge sequence; row 10 pins the free-override case a balance heuristic got
wrong.

**Still outstanding from earlier waves:** all five of wave 0's gates and all sixteen of wave 1's.
None has been walked. Wave 1's gate 1 (the `AGORA-STATCOLLECTION` census) and gate 11
(`unlockedFeatureIds`) still gate real authored content.

---

## Verification recorded

- `dotnet build Agora.sln` — **0 warnings, 0 errors**, toolchain mode. Run once at the end; **this
  build deploys** to the player's live `…\Mods\Agora.Mod` (15 data files).
- `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj` — **2109 passed, 0 failed**
  (from 1978; **+131**).
- `cd ui && npx tsc --noEmit` — clean. **No `ui/` file changed this wave.**
- **Schema versions: only `engine_tuning` moved, 7 → 8.** No sidecar version moved — state 6,
  settings 4, metric history 1, flavor cache 2, flavor schema 2 — and none of those files was
  touched. Every field this wave persists was landed by wave 2, so `PoliticalEngine.CloneState` and
  `AgoraSettings.Clone()` needed no change and **there is no migration to write**.
