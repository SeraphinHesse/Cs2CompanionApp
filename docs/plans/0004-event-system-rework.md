# AGORA — News → Story/Event System Rework

## Context

AGORA's news layer today is **derived, not decided**. `AgoraUiProjection.BuildFeed` reassembles a
feed from scratch on every publish out of `ActiveEvents`, `ElectionHistory`, `CoalitionHistory` and
LLM prose; nothing news-shaped is persisted, nothing the player reads can be acted on, and the whole
surface is a rear-view mirror. Month to month the city's politics moves smoothly and quietly, which
is the opposite of what a political layer is for.

`CS2 Mod Planning_ Events.md` reworks this into a **Story/Event system**: each month the city draws
two stories from a pool of triggered events, each story bundles one major and two minor events into
a single narrative, each event carries live gameplay effects, and the player must *tackle* each one
— ignore it, meet its goal, buy it off with political power, or define their own solution. Halfway
through the month the story resolves, a resolution article is written, and success or failure lands
as effects and as voter movement. A political-power currency gates the override and punishes debt.

The outcome: real month-to-month and block-to-block political variance, driven by the city the player
actually built, with prose that describes decisions instead of narrating history.

**Owner decisions taken 2026-08-15, binding on this plan:**

1. **A two-month story cycle, plus a manual early resolve.** Stories draft on month M's tick and
   resolve on month M+1's tick; the next batch drafts at M+2. **This supersedes the "day 15" design
   in the source document**, and the reason is not stylistic — see "Why not half a month" below. The
   player is not made to wait it out: a **Resolve now** button closes a story early, and because that
   is a player command its reading is *recorded* rather than re-measured, which keeps it deterministic.
2. **Sensors first.** Wave 1 builds the missing sensors, sourced from the data the game already shows
   the player on the **city statistics screen** (`CityStatisticsSystem.GetStatisticValueLong` is
   public — no Harmony).
3. **Timeline events: adapt, prune, promote.** Fired timeline events auto-wrap into mandatory civic
   events. The **most boring 25% are dropped outright**; the **most significant ~25–33% are
   hand-authored** with real resolution checks and full prose. The middle keeps the generic wrapper.
4. **Full replacement of the news feed.** Stories become the only prose surface. Because far fewer
   articles are written per month, **article limits triple**: headline 90 → 270, body 420 → 1260.
5. **Severity tiers are derived, never a new enum.** Mandatory / Major / Minor is a projection of the
   existing 1–5 `Severity` integer through two tuning keys, so there stays exactly one number per
   concept and the shipped alert lane keeps working.
6. **One modal per story, not per event**, on the story lane's own queue.

### Why not half a month

The source document says stories resolve "halfway through the month". They cannot, and the reason is
structural rather than an implementation difficulty:

- **There is no day 15.** CS2 ships `TimeSettingsData.m_DaysPerYear = 12`, so *one in-game "day" is
  one calendar month* (`src/Agora.Mod/Time/SimClockMath.cs:14-20`). `SimClockMath.ToSimDate` returns
  `new SimDate(year, month, 1)` — `Day` is a literal `1`. `AgoraHeartbeatSystem`'s "day change"
  detector therefore already fires exactly twelve times a sim year; there is no daily call site.
- **Nothing would have changed anyway.** `AgoraSnapshotSystem.Capture()` calls `EnsureSampled(today)`
  keyed on that same month-pinned date, so a mid-month read hands back the **byte-identical snapshot
  taken at month start**. Every `metric` and `delta` goal check would be provably unmeasurable: the
  number cannot have moved between draft and resolution.
- **Forcing a fresh mid-month sample trades one problem for a worse one.** The reading would then
  depend on exactly which 128-frame tick crossed the threshold, which varies with sim speed and frame
  timing — a non-deterministic input, which is precisely what non-negotiable #3 forbids.
- **And a real intra-month tick would break every existing save.** `SeedStreams.Derive` folds
  `date.Day` into the seed (`SeedStreams.cs:61`), so making `Day` meaningful rewrites every seed in
  every save. `SidecarPaths.StateFileName` is `(year, month)` only, so two states in one month would
  collide on one file; `LoadReconciliation` and `TickPlanner.CatchUpDates` are month-granular
  throughout.

**The two-month cycle gets the design's intent for free.** Drafting at M and resolving at M+1 is a
genuinely later measurement, so `windowMonths` and `delta` mean something; it needs no new cadence,
no new seed input, no schema break; and the story arc still reads as "the city reacts, then the
verdict lands". The manual **Resolve now** button covers the pacing complaint that motivated the
half-month rule in the first place, and covers it better, because the player chooses when.

---

# Part I — Execution model

## Waves of parallel subagents

Eight waves, numbered 0–7. **Every wave has the same shape**, enforced by the `/nextwave` and
`/commitpushpr` skills specified in Part III:

```
EventSystemRefresh
      │
      ├── event-system/wave-N          umbrella branch · one orchestrator session
      │        │
      │        ├── [SPINE]  orchestrator lands cross-cutting files itself, alone
      │        │
      │        ├── event-system/wN-a  ─┐
      │        ├── event-system/wN-b   ├─ worktrees off the spine, disjoint files, parallel
      │        ├── event-system/wN-c   │
      │        └── event-system/wN-d  ─┘
      │        │
      │        └── merge in declared order → build → test → PR → handoff
      │
      └──◄── PR: umbrella → EventSystemRefresh
```

**The conflict rule, and it is the whole reason this works:** every file more than one lane would
need is landed by the orchestrator in the **spine commit**, before any worktree exists. Lanes then
own strictly disjoint path sets, declared here and restated in the handoff. Two lanes never open the
same file. Merges are therefore trivially reviewable, and a merge conflict is a *bug in the wave
plan*, not something to resolve by hand.

**Three structural moves that buy most of the conflict-freedom:**

- `AgoraRuntime` (3013 lines, `src/Agora.Mod/Core/AgoraRuntime.cs`) is the single hottest file in the
  repo and every wave wants it. Wave 2's spine makes it `public static **partial** class` — a
  one-word change — so later lanes add `AgoraRuntime.Stories.cs`, `AgoraRuntime.Power.cs`,
  `AgoraRuntime.StoryCommands.cs` as **new files** instead of queueing on one.
- Same move for `AgoraUiProjection` (1757 lines) → `AgoraUiProjection.Stories.cs`.
- All new engine code lands in a **new folder**, `src/Agora.Core/Stories/`. The existing
  `src/Agora.Core/Events/` timeline subsystem is left untouched until Wave 3's adapter, which only
  *reads* it.

**Worktree hygiene**, both learned the hard way (`docs/status.md` § Known toolchain quirks):

- Run `npm install` *inside* each worktree's `ui/`. **Never junction `ui/node_modules`** to another
  checkout — deleting the junction later follows the link and empties the target, silently disarming
  `tsc` for every other lane.
- **`npm run build` deploys to the player's live `…\Mods\Agora.Mod`.** Lanes verify with
  `dotnet build src/Agora.Mod/Agora.Mod.csproj -p:UseCsiiToolchain=false` and `npx tsc --noEmit`.
  Only the orchestrator, once per wave, runs a real deploying build.
- `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj` — project path, never the solution.
- **`refsrc/` does not exist inside a worktree.** It is gitignored, so it lives only in the main
  checkout at `C:\Users\serap\OneDrive\Documents\GitHub\Cs2CompanionApp\refsrc`. Any lane that needs
  the decompiled game source — Wave 1's scout above all — must be handed that absolute path, and must
  **grep it, never read it in full** (it is hundreds of MB). A lane that greps `./refsrc` inside its
  worktree gets zero hits and will quietly conclude the API does not exist.

## Wave map

| Wave | Theme | Spine (orchestrator) | Lanes |
|---|---|---|---|
| **0** | **Tick correctness prerequisites** | `PoliticalState.LastCompletedTickMonth`; the `SchemaVersion` default reconciliation | 3 |
| **1** | Sensors & city statistics | scout report; `CitySnapshot` v4; `snapshot.schema.json`; state + metric-history migrations | 4 |
| **2** | Story engine core | `Stories/` contracts; `PoliticalState` v6; `AgoraSettings` v4; `stories`/`power` tuning; `partial` splits; new seed streams; concurrency retune | 5 |
| **3** | Catalog & content | `civic_events.schema.json`; catalog loader; shipped-catalog test harness; palette additions | 5 |
| **4** | Cycle wiring & effects | `TickPlanner` draft/resolve phases; `PoliticalEngine` story stage; `EngineTick` in/out fields; replay policy | 5 |
| **5** | Prose | `politics_flavor` v3 (both copies); `FlavorPayload`; tripled limits; cache v3 | 4 |
| **6** | UI | `ui_bindings.md` v9; story payloads; `bindings.d.ts` | 4 |
| **7** | Retirement, balance, gates | `ui_bindings.md` v10 (news removals) | 4 |

Waves are strictly sequential; lanes inside a wave are strictly parallel.

---

# Part II — The waves

## Wave 0 — Tick correctness prerequisites

**Why this wave exists.** Two pre-existing defects are harmless today and become severe the moment
the story system puts scoring accumulators on the monthly tick. Both must be closed **before** a
single accumulator is added, which is why they lead rather than trail.

### The reload double-tick

`AgoraRuntime.cs:1848` decides a month has changed by comparing against **session-local** `_hasTicked`
/ `_lastTick`. `_hasTicked` is cleared by `ResetForNewSave` and set only by `Tick` or `Replay` — and
`Replay` runs only when `MonthsToReplay > 0`, which a mid-month save/quit/reload never produces
(`LoadReconciliation` returns `ExactMatch` with 0). So on every reload the next heartbeat sees
`monthChanged == true` and **runs `OnMonth(M)` a second time for a month already advanced through.**
`PoliticalEngine.Advance` has no same-month guard.

Today the damage is a duplicated poll and a double-counted `FringeWatch.MonthsObserved`. Under this
rework it would mean **unbounded political-power farming by save-scumming**, inflated `MissStreak`
weights, and a story resolving — and paying out — twice.

**Fix:** persist `LastCompletedTickMonth` on `PoliticalState` and gate `OnMonth` on
`today.TotalMonths > state.LastCompletedTickMonth`. `_hasTicked` becomes a pure logging latch. State
schema 4 → 5 here (so Wave 2's story fields land at 6), with the step seeding the field from the
state's own `Date` so an existing save does not re-run its last month on first load.

### Snapshot history is never persisted

`AgoraRuntime.cs:136` holds `_snapshotHistory` as a session-static list, capped at 36 (`:248`) and
**cleared at every save boundary** (`:639`). Nothing in `Persistence/` writes it —
`MetricHistoryFile` carries only the rent and land-value trend memory. So `EngineTickInput.SnapshotHistory`
is empty on the first tick after every load.

Every `delta` and `windowMonths` trigger reads exactly this. A player who plays twelve months straight
would see "unemployment up 3pp over six months" fire; the same player quitting to menu each year never
would. That is the literal definition of desync. (`IndicesEngine` already has this defect for its trend
legs; the rework makes it player-visible and score-bearing.)

**Fix:** a bounded, persisted metric ring as its own `SidecarDocument` with its own migration table —
not the full snapshots, just the fields the trigger registry can name, which keeps the file small and
its schema stable.

### Lanes

| Lane | Owns (exclusive) | Task |
|---|---|---|
| **0a** | `src/Agora.Mod/Core/AgoraRuntime.cs` (the `Tick`/`OnMonth` gate), `src/Agora.Core/Contracts/PoliticalState.cs` | `LastCompletedTickMonth` + the gate. Also reconcile `PoliticalState.SchemaVersion`'s default of `3` against `SidecarSchema.CurrentStateVersion = 4` — a freshly constructed state currently claims v3, so a v4→v5 step could run against an object that was never v4. |
| **0b** | `src/Agora.Mod/Persistence/MetricRing.cs` (new), `SidecarSchema.cs`, `SidecarStore.cs`, `SidecarPaths.cs` | The persisted metric ring and its document type, load/save/prune, migration table. |
| **0c** | `tests/Agora.Core.Tests/TickIdempotenceTests.cs`, `MetricRingTests.cs` (new) | Prove `OnMonth` is idempotent for a repeated month, and that a reload preserves the trend window. **Also fix the latent bug found on the way:** `TickPlanner.cs:120` computes `((date.Day - 1) % pollDays) == 0` — `Day` is always 1, so `IsPollTick` has been unconditionally true and `scheduler.pollTickIntervalDays` has never done anything. |

**Nothing in Wave 0 is story-specific.** It is a correctness pass that happens to be a prerequisite,
and it stands on its own merits if the rework were abandoned tomorrow.

---

## Wave 1 — Sensors and city statistics

**Why first:** every data trigger in the design document reads city state Agora cannot currently see.
Authoring 50+ events against metrics that turn out unreachable is the single most expensive mistake
available, so the data comes first and the catalog is written to what exists.

### Spine (orchestrator, alone)

1. **Scout pass → `docs/scout/0004-city-statistics.md`.** Enumerate, from the shipped assemblies and
   `refsrc/` (grep only, never read in full), exactly what is reachable for: homelessness, migration
   / birth / death rates, tourism and `Attractiveness`, city level / XP / milestones, progression
   unlocks, per-resource and per-industry tax rates, landmark & signature-building counts, and
   garbage accumulation. Anchor on `Game.City.CityStatisticsSystem.GetStatisticValueLong` and the
   `StatisticType` enum — this is the exact source the city statistics screen reads, which is the
   owner's stated constraint. Record concrete type and member names. **Anything not confirmed here
   does not get a trigger in Wave 3.**
2. `src/Agora.Core/Contracts/CitySnapshot.cs` → **schemaVersion 4**, additive only. New blocks,
   shaped as `readonly struct`s to match the file's existing style:
   - `CityStatistics { Homeless, HomelessShare, MigrationInRate, MigrationOutRate, BirthRate, DeathRate, GarbageAccumulation, … }`
   - `TourismLevels { Tourists, Attractiveness, LandmarkCount, AttractionCount }`
   - `ProgressionState { CityLevel, Experience, UnlockedFeatureIds (sorted), MilestoneIndex }`
   - `IndustryTaxRates` — per-resource / sub-industry rates keyed by a sorted id list, so an
     event can trigger on "office software subsidised while farming taxed".
   Mirror the reachable subset onto `DistrictSnapshot`, using the established `HasCityFallbacks` /
   `CityFallbackFields` contract for anything district-blind.
3. `data/schemas/snapshot.schema.json` — same shape, same version.
4. `src/Agora.Mod/Persistence/SidecarSchema.cs` — `CurrentMetricHistoryVersion` 1 → 2 plus its
   migration step, for the new trend windows. Follow the existing `MigrationStep` table pattern
   exactly, and copy the "frozen local constants" discipline: a step reproduces what the file was
   written with, never a live tuning read.
5. `/schema-change` checklist walked and recorded.
6. **Commit this plan into the repo** as `docs/plans/0004-event-system-rework.md`, matching the
   existing `docs/plans/000N-*.md` convention. Every later wave's handoff and lane table is a sibling
   (`0004-wave-<N>-lanes.md`, `0004-wave-<N>-handoff.md`), so the whole rework is one numbered
   family and a new orchestrator can find its inputs by pattern rather than by being told.

### Lanes

| Lane | Owns (exclusive) | Task |
|---|---|---|
| **1a** | `src/Agora.Mod/Sensors/AgoraStatisticsSensorSystem.cs` (new) | Homelessness, migration, births/deaths, garbage — all via `CityStatisticsSystem`. Derive from `AgoraSensorSystemBase`; build queries in `OnCreate`; fail closed to `_broken` and log once. |
| **1b** | `src/Agora.Mod/Sensors/AgoraProgressionSensorSystem.cs` (new) | City level, XP, milestone index, unlocked feature ids, per-industry tax rates. |
| **1c** | `src/Agora.Mod/Sensors/AgoraTourismSensorSystem.cs` (new) | Tourists, attractiveness, landmark/attraction counts. |
| **1d** | `src/Agora.Mod/Sensors/SensorReadings.cs`, `SensorMerge.cs`, `SnapshotAssembly.cs`, `MetricHistory.cs`, `Mod.cs` registration block | The pure merge half + system registration. Owns the seams the three sensor lanes write *into*, so it is the one lane that touches shared sensor files. |
| **1e** | `tests/Agora.Core.Tests/**` (all new files), `src/Agora.Mod/Llm/FlavorPromptBuilder.cs` | Snapshot-v4 migration fixtures, merge/assembly tests, and the prompt sync `/schema-change` step 3 demands — a snapshot field the LLM cannot see is a contract break. |

**Ordering inside the wave:** 1d publishes its seam signatures in the wave handoff *before* 1a–1c
start, so the three sensor lanes code against agreed names. They never edit 1d's files; they hand
back a `SensorReadings` fragment 1d already declared.

### Verification
`dotnet test` green; new fixtures prove a v3 snapshot and a v1 metric-history file upgrade in place
without loss. Manual gate: load an existing save, confirm `Agora.log` reports the new metrics with
plausible values and no `_broken` sensor.

---

## Wave 2 — The story engine core

Pure `Agora.Core`. No game types, no IO, fully testable.

### Spine

1. **New folder `src/Agora.Core/Stories/`** with the contract types:
   - `CivicEvent` — `Id`, `Severity` (**the existing 1–5 integer, not a new enum**), `Region`,
     `Trigger` (`TriggerSpec`), `Check` (`CheckSpec`), `ActiveEffects[]`, `SuccessEffects[]`,
     `FailureEffects[]`, `ActivePressure`/`SuccessPressure`/`FailurePressure` (`IssuePosition`),
     `DistrictAffinity` (which district archetypes feel it hardest), `Tags[]`, and the seven prose
     fields: `Name`, `Description`, `IgnoreText`, `GoalText`, `PowerOverrideText`, `SuccessText`,
     `FailText`.
   - **`StoryTier` is derived, not stored.** `Mandatory` / `Major` / `Minor` is a pure projection of
     `Severity` through two new tuning keys (`stories.mandatorySeverityThreshold`,
     `stories.majorSeverityThreshold`). This is deliberate: `catalog.majorSeverityThreshold` is
     already the single definition of "major", shared by `EventScheduler.IsMajor`,
     `CoalitionStability` and `AgoraRuntime.RaiseEventAlerts`, and `ui_bindings.md` §4.5 states in
     bold that the UI must **never** re-derive it. A fourth vocabulary would drift on the next tuning
     pass. Keeping the integer also keeps `AffinityEngine.EventTerm`'s `severity/5` scaling honest.
   - `TriggerSpec` / `CheckSpec` — **declarative, never code per event**, so 50+ events stay content:
     `{ Kind: Metric | Delta | Unlock | Policy | Absent | Manual, MetricId, Comparison, Threshold,
     WindowMonths, Scope: City | AnyDistrict | AllDistricts }`. `MetricId` resolves through a single
     sorted registry mapping ids → `CitySnapshot`/`DistrictSnapshot` accessors, which is also what
     makes an unreachable trigger a *load-time catalog error* rather than a runtime surprise.
   - **`CheckResult` has three states, not two: `Met`, `NotMet`, `Unmeasurable`.** A deleted district,
     a sensor that fell back to a city value (`DistrictSnapshot.CityFallbackFields`), or a metric with
     no reading must not score as failure — that would cost the player political power for a sensor
     gap. `ui_bindings.md` §4.5 already writes this rule for the identical mandate case: *"held, not
     failing … never show it as `Defied` because the clock ran out while its metric was unreadable."*
     An `Unmeasurable` slot is excluded from both the numerator and the denominator of the 2-of-3.
   - `Story` — `Id`, `OpenedDate`, `ResolvesDate`, `IsMandatory`, `Slots[]`, `Outcome`,
     `HeadlineFallback`, `ResolutionOutcome`, and the flavor keys.
   - `StorySlot` — `EventId`, `Role` (Major/Minor), `Response` (`Unaddressed | Ignore | Goal |
     PowerOverride | Manual`), `PlayerText`, `BaselineMetric` (captured at open, so a delta check is
     measured against the month it started), `SlotOutcome`, `ManualDeclared`.
   - `EventPoolEntry` — `EventId`, `FirstTriggeredDate`, `MissStreak`.
   - `PoliticalPowerState` — `Balance` (signed `int`), `LifetimeEarned`, `LifetimeSpent`,
     `LastAccrualDate`, and a bounded `Ledger[]` of recent transactions for the UI.
2. `src/Agora.Core/Contracts/PoliticalState.cs` → **schemaVersion 6** (Wave 0 took 5): adds
   `LiveStories`, `StoryArchive` (bounded by `stories.archiveRetention`), `EventPool`, `Power`,
   `PlayerCommands`, plus `LastStoryDraftMonth` / `LastStoryResolveMonth`. Every new list gets a
   documented sort key — the determinism contract is the SHA-256 of this object's serialization, and
   a `Dictionary` keyed by event id here would fail it outright. Declared orders:
   `LiveStories` by `Id` ordinal · `StoryArchive` by `(ResolvedMonth desc, Id)` ·
   `EventPool` by `EventId` · `PlayerCommands` by `(DecidedMonth, Sequence, EventId)`.
2b. **`PoliticalEngine.CloneState` must deep-copy every new collection.** It is a hand-maintained
   field list (`PoliticalEngine.cs:1034-1066`); `Fringe` is deep-cloned with a comment saying exactly
   why. Note that `ActiveEvents` is currently only a *shallow* list copy, so its elements are aliased
   between prior and clone — story records carry a mutable `MissStreak` and a chosen response, so
   aliasing them would let a speculative advance write into the caller's prior state. Miss one and it
   silently reverts on the next tick, exactly as `AgoraSettings.Clone()`'s remark warns.
3. `AgoraSettings` → **schemaVersion 4**, with the doc's "all of these numbers must be balanceable in
   the agora settings" requirement satisfied by level-style enums where the existing settings use
   them and plain values where the doc names a number: `StoriesEnabled`, `StoryResolutionDay` (1–27,
   default 15), `StoriesPerMonth` (default 2), `EventsPerStory` (default 3), `PoliticalPowerEnabled`,
   `PowerIntensity` (`Lenient | Default | Harsh`, driving the gain/cost/penalty presets), and
   `StoryDifficulty`. `StoryResolutionDay` is **not** a setting — there are no days; the tunable is
   `stories.resolutionProgress`. **`Clone()` gains a line per property, in the same screen** — the
   file says why.
4. `src/Agora.Mod/Persistence/SidecarSchema.cs` — `CurrentStateVersion` 5 → 6,
   `CurrentSettingsVersion` 3 → 4. New `StateSteps` and `SettingsSteps` entries; the settings upgrade
   goes in a shared `UpgradeSettingsObjectToV4` helper called from **both** the nested-in-state path
   and the standalone path, because the nested block never sees `SettingsSteps`. Existing saves get
   empty story lists, an empty pool and a zero power balance — **no story is ever generated
   retroactively.**
5. `data/engine_tuning.json` + `data/schemas/engine_tuning.schema.json` → new `stories` and `power`
   sections (schemaVersion 3 → 4). Everything the design doc names as a number lives here:
   ```
   stories:  storiesPerCycle 2 · eventsPerStory 3 · cycleMonths 2 ·
             successThreshold 2 · mandatorySeverityThreshold · majorSeverityThreshold ·
             missStreakWeightStep · maxMissStreak · poolMaxSize · archiveRetention ·
             minorPromotionEnabled true · maxStoryEffectsPerModifier ·
             activeEffectScale · successEffectScale · failureEffectScale ·
             alienationWeight · enfranchisementWeight · freeTextMaxLength
   power:    maxMonthlyGain 5 · gainPopularityCurve · successGain {minor 10, major 20, mandatory 50} ·
             failureLossRatio 0.5 · overrideCost {minor 50, major 100, mandatory 500} ·
             debtRevenuePenalty 0.20 · debtPenaltyCapPerMonth · ledgerRetention
   ```
6. `src/Agora.Core/Determinism/SeedStreams.cs` — new `StreamNames` constants: `StoryDraft`,
   `StoryPool`, `StoryTiebreak`, `StoryDistrictTarget`, `PowerAccrual`. **Adding streams is
   sanctioned; renaming an existing one is not** (it rewrites every save's history). Do not reuse
   `EventProcedural` or `EventJitter` — the file's own comment says borrowing a neighbouring stream
   "would couple two unrelated systems' outcomes."
7. **Concurrency retune, in the same spine.** `catalog.maxConcurrentEvents` is 6 and
   `EventScheduler` enforces it; a cycle drafting 2 stories × 3 events would sit at the cap and start
   refusing to fire *timeline* events. Worse, `AffinityEngine.EventTerm` sums over every live event
   and clamps to [-1,+1] **before** weighting, so at that volume the clamp saturates permanently and
   the event term stops discriminating between a flood and a bus-fare rise. Story events therefore
   **do not enter `state.ActiveEvents`**: they live in `LiveStories` and contribute their pressure
   through their own term with its own budget. Wave 2's tests must pin the non-saturation claim.
8. Split for conflict-freedom: `AgoraRuntime` and `AgoraUiProjection` become `partial`.

### Lanes

| Lane | Owns (exclusive) | Task |
|---|---|---|
| **2a** | `Stories/TriggerEvaluator.cs`, `Stories/MetricRegistry.cs` | Evaluate every `TriggerSpec` against `CitySnapshot` + `SnapshotHistory`. Pure, sorted iteration, no dictionary-order dependence. Same evaluator serves `CheckSpec` at resolution — one implementation, two callers. |
| **2b** | `Stories/StoryAssembler.cs`, `Stories/EventPoolWeighting.cs` | Draft the cycle's stories: pool → weighted seeded draw → 1 major + 2 minors ×2; one extra bare story per mandatory event; **degradations** — no major left ⇒ promote a minor and take 3 minors; too few events ⇒ a shorter story is valid, not an error. Clear the pool afterwards, incrementing `MissStreak` on every unchosen entry. **Every selection and every degradation needs a declared total order** — weight desc, then `MissStreak` desc, then `Id` ordinal asc — because "which minor gets promoted" left to collection order is the determinism bug `Agora.Core/CLAUDE.md` calls the most common one. |
| **2c** | `Stories/StoryResolution.cs` | The 2-of-3 rule and its edge cases: a story of 3 needs `stories.successThreshold` met; a story of fewer than 3 needs **all** slots met. Per-slot outcome by response mode — `Goal` runs the `CheckSpec`; `PowerOverride` is an automatic success already paid for; `Ignore` is an automatic failure; `Manual` reads the player's own declaration and is neutral until declared. |
| **2d** | `Stories/PoliticalPower.cs` | Accrual (≤ `maxMonthlyGain`, scaled by the governing party's / coalition's current vote share), award and penalty on resolution, affordability check for an override, and the debt state. Pure arithmetic; the *consequence* of debt is Wave 4's effect. |
| **2e** | `tests/Agora.Core.Tests/Story*.cs`, `PoliticalPowerTests.cs`, plus additions to `SidecarMigrationTests.cs`, `PerSaveSettingsTests.cs` | Determinism (same seed twice → identical hash), every degradation branch, the pity-weighting monotonicity, migration from a real v4 fixture. |

### Determinism note this wave must write down

Player choices arrive asynchronously via `CallBinding`. That does **not** break non-negotiable #3,
but the amendment has to be stated precisely — "add player choices to the input tuple" is not enough.
To be added to `politicsmodplan.md` §5:

> **Amended #3.** Engine state at date D is a pure function of *(metrics history, prior state, seeds,
> catalogs, settings, and the ordered, dated log of player commands with timestamp ≤ D)*. The command
> log **is** engine state: it is persisted in `PoliticalState`, it has a total order, and it is
> replayed, never re-solicited.

What that forces, concretely:

- **A choice is an appended, dated record, not a mutation** — `PlayerCommands` with
  `(StoryId, EventId, Kind, FreeText, DecidedMonth, Sequence)` and the sort key declared above.
- **It is persisted the moment it is recorded**, not at resolution. `AgoraSidecarSystem.PreSerialize`
  already runs on every `Purpose.SaveGame`, so a choice made in month M survives into M+1's tick.
- **Free text is prose and is treated as such**: capped at `stories.freeTextMaxLength`, rejected with
  the existing `CommandOutcome.TooLong` (W4 already added it — reuse, do not add a new code), and
  **never parsed for a number**, exactly as non-negotiable #1 requires of LLM output.
- **The manual-override path is the one real exploit surface.** A player who declares their own
  success mints `+50` power per mandatory event on a one-word justification. Wave 2d caps
  manual-declared awards at the minor rate regardless of tier, and Wave 7's balance pass revisits it.
- **The Resolve-now reading is recorded, not re-measured.** Because the button is a player command
  its firing time is already exogenous; the snapshot it resolves against is written into the story
  record, so replay reads the recorded evidence rather than sampling a different city.

---

## Wave 3 — Catalog and content

### Spine

1. `data/schemas/civic_events.schema.json` — `additionalProperties: false`, mirroring
   `timeline.schema.json`'s discipline. Note explicitly the two checks JSON Schema cannot make:
   every `effectId` exists in the palette, and every `metricId` exists in the metric registry.
2. `src/Agora.Core/Stories/Catalog/CivicEventCatalogLoader.cs` — takes **text, never a path**
   (IO stays out of Core), never throws on bad content, degrades to the valid subset and returns
   `Errors`/`Warnings`/`RejectedEventCount`, exactly as `TimelineCatalogLoader` does. `ForTheme`
   filters by region + global.
3. `tests/Agora.Core.Tests/ShippedCivicEventCatalogTests.cs` — the build-time gate that turns a bad
   catalog entry into a red test. Landed in the spine so all four content lanes have it from commit
   one.
4. Effect palette additions in `data/engine_tuning.json` `effects.perEffect`, each with scope,
   `magnitudeCap`, `durationCapMonths` and a `fallbackEffectId` (per `/add-effect`).

### Lanes

| Lane | Owns (exclusive) | Task |
|---|---|---|
| **3a** | `data/events_global.json` | ~25 events. Services, pollution, crime, housing, transport, budget. |
| **3b** | `data/events_eu.json` | ~15 EU-flavoured events. |
| **3c** | `data/events_na.json` | ~15 NA-flavoured events. |
| **3d** | `src/Agora.Core/Stories/Catalog/TimelineEventAdapter.cs`, `data/timeline_*.json` | The owner's 25/50/25 split: **drop the most boring 25%** of the 120 shipped timeline entries outright; **hand-author the top ~25–33%** into full mandatory civic events with real resolution checks and all seven prose fields; the middle keeps the generic adapter wrapper (name ← `Title`, description ← `HeadlineBrief`, a severity-derived generic check). This lane is the only one that edits the timeline catalogs. |
| **3e** | `src/Agora.Mod/Effects/*` new files, `tests/…/EffectPaletteTests.cs` additions | New palette entries and their cap tests — including the **political-power debt penalty**, see below. |

### Content rules for lanes 3a–3d

- **Every threshold must be hittable in a normal game.** The design document says so twice. Author
  against the metric registry Wave 1 actually built, and state the expected trigger frequency in a
  comment on each event.
- **Effects must alienate or enfranchise.** Each of active / success / failure carries an
  `IssuePosition` pressure, so a positive outcome moves voters toward the government and a negative
  one away — that is the mechanism that produces the swing the rework exists for.
- **District dependence is the point.** Prefer `Scope: AnyDistrict` triggers and district-scoped
  effects so the same month reads differently block to block.
- Severity discipline as `/add-event` states it: conservative, mandatory should feel rare.

### The two known palette gaps, and what to do about them

- **Political-power debt → "lose 20% of baseline revenue".** No `CityModifierType` member touches
  revenue. Both routes below were confirmed against `refsrc/`.

  **Primary — a capped recurring debit against `PlayerMoney`.** `Game.City.PlayerMoney` is an
  `IComponentData` with public `Add(int)` / `Subtract(int)` clamped to ±2e9, and
  `Game.Simulation.CityServiceBudgetSystem` exposes **public** `GetTotalIncome()`,
  `GetTotalExpenses()` and `GetTotalTaxIncome()` — no Harmony needed to read the budget. The game's
  own `GameModeGovernmentSubsidiesSystem` already computes a percentage of total expenses and feeds
  it back as a money delta, so there is precedent for exactly this arithmetic. Debit
  `min(power.debtRevenuePenalty × GetTotalIncome(), power.debtPenaltyCapPerMonth)` once per political
  month.

  Three things this needs that an ordinary palette entry does not, because it is **not** a
  `CityModifier` and so `EffectDispatcher`'s decay, stacking and `maxStackedPerModifier` machinery
  does not apply to it: its own declared scope, `magnitudeCap` (0.20) and a **bounded**
  `durationCapMonths` so the penalty expires even if the debt does not; a `kind: "money"`
  discriminator in `effects.perEffect` so `EffectPalette` still owns the closed registry; and
  `ModifierRegistry` taught to **skip** it rather than report-and-drop it. §7 FORBIDDEN check: it
  creates or modifies no district, zoning, building or terrain, it is capped, and it takes money
  rather than control — clean, but it is a new *kind* of effect and must be **ratified into
  `politicsmodplan.md` §7 in this wave, not assumed.**

  The real implementation risk: `PlayerMoney` is written by a Burst job in `BudgetApplySystem` every
  **1/1024 of a day** (`kUpdatesPerDay = 1024`, verified in `refsrc` by wave 7d — an earlier draft of
  this plan said 1/128, which was wrong by a factor of eight and had been promoted into
  `politicsmodplan.md` §7 before anyone re-checked it). A managed write from `GameSimulation` must be sequenced against it or one of the
  two writes is lost. Comment the phase choice, as `src/Agora.Mod/CLAUDE.md` requires.

  **Fallback — `city-service-building-upkeep`** (`CityServiceBuildingBaseUpkeepCost`, 38). Already in
  the palette, already capped, already applied and tested; raise it by a capped fraction. Costs the
  player money through the existing sanctioned channel with **zero** new machinery.
  `city-loan-interest` reads better narratively for a *debt* penalty but does nothing to a city with
  no loan, so it is the worse fallback, not the better one.

  **Explicitly rejected: `ServiceFee` and `TaxRates`.** Both are the player's own sliders. Writing
  them is "targeting the player's authority" in the plainest sense of §7's FORBIDDEN list, and the
  player would watch their own settings move without touching them.
- **The design doc's own sample events, mapped against the real enums** — this table is the pattern
  every authored event follows, and it is why "effects are capped" survives contact with the content:

  | Sample event wants | Reachable as |
  |---|---|
  | Lower wellbeing in urban cores | `District.Wellbeing` |
  | Tourism demand | **Proxy only.** No tourism modifier exists — `Game.City.Tourism` is a read component. Use `city-attractiveness`, `city-entertainment`, `city-park-entertainment` (all shipped) and **say "attractiveness" in the prose, not "tourism"**, or the effect and the headline disagree. |
  | Illness from pollution | `City.DiseaseProbability`, `PollutionHealthAffect`, `HospitalEfficiency` |
  | Tax-hike resentment | `City.TaxHappiness`, `District.LowCommercialTax`, `District.Wellbeing` |
  | Graduation chance | `City.CollegeGraduation`, `UniversityGraduation`, `UniversityInterest` |
  | Garbage pileup | `District.GarbageProduction`, `City.IndustrialGarbage` |
  | Crime / prison break | `District.CrimeAccumulation`, `City.CrimeProbability`, `PrisonTime`, `CrimeResponseTime`, `CriminalMonitorProbability` |
  | Strikes, sector productivity | `City.IndustrialEfficiency`, `OfficeEfficiency`, `OfficeSoftwareEfficiency` |
  | Trade balance | **Direct hit.** `city-import-cost`, `city-export-cost`, `city-service-import-cost` — all shipped. |
  | Agricultural output | **Impossible as stated.** `IndustrialEfficiency` (31) is the only production lever and it is all-industry. `IndustrialFishInputEfficiency` (35) / `FishHubEfficiency` (36) are fish-specific and not in the palette; `OreResourceAmount` (12) / `OilResourceAmount` (13) are the wrong resources. Re-specify the farmers-vs-tech-subsidies event around **taxes and trade cost**, which is where the player's lever actually is. |
  | Prison cost | **Partial, and do not mislabel it.** `city-prison-time` → `PrisonTime` (22) is sentence *length*, not cost. The cost proxy is `city-service-building-upkeep` → `CityServiceBuildingBaseUpkeepCost` (38), which is city-wide across every service building. Do not write prose calling it "the prison budget". |
  | Commute misery | `District.StreetSpeedLimit`, `StreetTrafficSafety`, `City.HighwayTrafficSafety` + `Wellbeing` (no commute modifier exists) |
  | Party polarisation on an axis | **`IssuePressure` only** — this is a voter effect, not a city effect, and needs no palette entry |
  | **"20–100 cims die"** | **Not doable and not wanted.** Killing citizens is entity mutation and sits outside §7. `DiseaseProbability` (5), `PollutionHealthAffect` (33) and `HospitalEfficiency` (34) change illness and treatment, not mortality — nothing kills. (`RecoveryFailChange` (11) is the only member that plausibly moves death outcomes; it is **not** in the palette and adding it is a `/add-effect` decision this plan does not take.) Re-specify as a `CrimeAccumulation` + `DiseaseProbability` spike carrying heavy `IssuePressure` — the political shock without the forbidden mechanism. |

  **The rule this table exists to enforce:** *an event's prose may only claim what its effect ids can
  actually do.* Wave 7d extends the `/add-event` guidance to check the claim against the palette
  entry. Without that rule the story system becomes a machine for producing lies about the city — the
  headline promises deaths, or a tourism boom, or a prison budget cut, and the simulation contradicts
  it within the month.

- **RCI demand / rent / land value / birth rate** remain unreachable (scout 0001 §3). Events wanting
  them re-specify against what exists — most land on `Wellbeing`, `Attractiveness`, `TaxHappiness`,
  `CrimeAccumulation` or the graduation members. **Do not author an event against a modifier that
  does not exist**; the catalog test will fail and the lane will have wasted its budget.

---

## Wave 4 — Tick wiring, effects and persistence

The wave that makes it run. Highest risk, so the spine is deliberately large and the lanes small.

### Spine

1. **The two-month cycle — one cadence, no new clock.** See "Why not half a month" in the Context
   section for why the source document's day-15 rule is not buildable. What ships instead:

   - `TickPlanner.Plan` gains **`IsStoryDraft`** and **`IsStoryResolve`**, both `elapsedMonths %
     stories.cycleMonths` phases measured from the save start date, exactly like every other cadence
     in that file. Draft on phase 0, resolve on phase 1. **No new tick, no `SimDate.Day`, no
     `MonthProgress`, nothing added to `SeedStreams`' inputs.**
   - A story records `DraftMonth` and `ResolveOnMonth`. Resolution reads the snapshot taken at
     `ResolveOnMonth` — a genuinely later measurement, which is what makes `delta` and `windowMonths`
     mean anything.
   - **`Story.Outcome != Pending` is the idempotence guard** and is written **before** the effect
     dispatch, so a reload never double-resolves. Wave 0's `LastCompletedTickMonth` gate is the
     partner half of that guarantee.
   - **Resolve now** sets `ResolveEarlyRequested` on the persisted story and bumps `StateVersion`.
     Because a player command's timing is already exogenous, this path may force a fresh sample — and
     **persists that snapshot into the story record as the resolution's evidence**, so replay reads
     the recorded number rather than measuring a different city. That is what keeps it deterministic,
     and it is the same trick that makes the choice log deterministic.
   - **A sweep at the top of `OnMonth` reaps anything stranded**: any story whose `ResolveOnMonth` is
     now in the past resolves immediately, or is abandoned if its evidence is gone. Without this,
     `TickPlanner.CatchUpDates` truncating a long gap (it drops the *oldest* months past
     `catchUpMaxMonths`) would leave a story pending forever.

2. **Two replay hazards, both decided here rather than discovered later.**
   - `Replay` **does not dispatch effects** (`AgoraRuntime.cs:2707-2709`). A story drafted and
     resolved inside a replayed window would award political power while applying no success effects.
     Scoring one half silently is worse than skipping both.
   - `Replay` scores every replayed month **against today's city** — it is documented as doing so. A
     `CheckSpec` in a replayed window would evaluate 2005's crime wave against 2031's crime rate:
     deterministic, and nonsense.

   **Decision: story drafting and resolution are suspended during replay.** The catch-up log says how
   many story cycles were skipped. A replayed decade produces no stories and no power, which is
   honest; inventing either would be fiction the player never got to participate in.
3. `src/Agora.Core/Engine/PoliticalEngine.cs` — insert the story stage. Stories **draft after** the
   event scan (stage 3) and **before** affinity (stage 5), so the cycle's active effects and issue
   pressures are visible to the voter model that same tick, matching how timeline events already work.
   Resolution runs in the same slot on the resolve month, before the pressures it changes are read.
4. `src/Agora.Core/Engine/EngineTick.cs` — `EngineTickInput` gains `PlayerCommands` /
   `ResolveEarlyRequests` / `IsReplay`; `EngineTickResult` gains `DraftedStories`, `ResolvedStories`,
   `PowerDelta`. Additive, documented, sorted.
5. **Effect breadth cap.** `effects` ships `stackingMode: sum` with `maxStackedPerModifier: 4`. Six
   story events per cycle, several sharing a modifier, would hit that limit and **silently drop the
   fifth**. Cap story effect breadth at draft time against `stories.maxStoryEffectsPerModifier`, so
   the constraint is enforced where it can be reasoned about rather than discovered in the ledger.

### Lanes

| Lane | Owns (exclusive) | Task |
|---|---|---|
| **4a** | `src/Agora.Mod/Core/AgoraRuntime.Stories.cs` (new) | The draft/resolve hooks on the existing monthly path, the stranded-story sweep, replay suspension, and the persist-immediately discipline. **No change to `AgoraHeartbeatSystem` — that is the point of the two-month cycle.** |
| **4b** | `src/Agora.Mod/Core/AgoraRuntime.StoryCommands.cs` (new) | Inbound commands: `SetStoryResponse(storyId, eventId, mode, text)`, `DeclareManualOutcome`, `ResolveNow`, `SpendPowerOverride`. Each returns a `CommandOutcome` from the **closed set** — extend the C# enum first if a new reason is genuinely needed. Each bumps `StateVersion` and persists. |
| **4c** | `src/Agora.Core/Stories/StoryEffects.cs`, `src/Agora.Core/Stories/StoryPressure.cs` | Turn a story's active/success/failure effect lists into capped `EffectRequest`s (clamped twice, as the existing resolution path does) and its pressures into the `IssuePressure` the affinity engine already reads. Reuse `EffectResolution` and `EffectPalette`; do not write a second clamp. |
| **4d** | `src/Agora.Mod/Core/AgoraRuntime.Power.cs` (new), `src/Agora.Mod/Effects/AgoraTreasurySystem.cs` (new) | Power accrual on the month tick and the debt penalty effect from Wave 3's palette entry. |
| **4e** | `tests/Agora.Core.Tests/StoryTickTests.cs`, `StoryPersistenceTests.cs` (new) | The reload matrix: reload before draft, between draft and resolve, and after resolve, each proving an identical state hash. Catch-up truncation leaving no stranded story. Replay producing no stories and no power. And that an early resolve replays from its **recorded** snapshot rather than re-measuring. |

---

## Wave 5 — Prose

### Spine

1. `data/schemas/politics_flavor.schema.json` → **schemaVersion 3**, and the verbatim duplicate in
   `src/Agora.Mod/Llm/FlavorSchema.cs` `EmbeddedJson` (`data/` is not deployed;
   `FlavorSchemaDriftTests` guards the copy). Additions:
   - `stories[] { storyId, headline ≤270, article ≤1260 }`
   - `resolutions[] { storyId, headline ≤270, article ≤1260 }`
   - Article `headline` 90 → **270**, `body` 420 → **1260**.
2. `FlavorPayload` / `FlavorDocument` gain the two collections. The numeric-field ban is unchanged and
   still enforced by `NumericFieldScanner` — non-negotiable #1.
3. `SidecarSchema.CurrentFlavorCacheVersion` 2 → 3 with `FlavorCacheMigration` **pruning, never
   truncating** — the same discipline that stopped the last limit change from resurrecting the
   `party-01` bug. Note the limits went *up* this time, so nothing existing becomes over-length.

### Lanes

| Lane | Owns (exclusive) | Task |
|---|---|---|
| **5a** | `src/Agora.Mod/Llm/FlavorPromptBuilder.cs`, `FlavorRequest.cs` | The story sections. **Headline** from the major event's `Name` + `Description` only. **Article** from all three events' names and descriptions, instructed to imply they are connected *and* to write the opposition's reaction — capitalising on the negative, angered by the positive. **Resolution** prompted from which slots failed. |
| **5b** | `src/Agora.Mod/Llm/FlavorValidator.cs`, `FlavorCatalog.cs`, `FlavorDocument.cs` | Validate and id-check the two new collections; drop unknown `storyId`s entry-by-entry, never the whole document. |
| **5c** | `src/Agora.Mod/Llm/StaticPoolProvider.cs`, `StaticPoolContent.cs` | **The fallback the design doc specifies exactly:** no Claude ⇒ headline = the major event's `Name`; article = each of the three events' `Name` then `Description`, in order. Resolution likewise from the success/fail description fields. This path must be good enough to play on. |
| **5d** | `tests/Agora.Core.Tests/Flavor*Tests.cs` additions | Schema drift, cache migration, the numeric ban against the new fields, and a golden test of the no-Claude fallback text. |

**Also fixed here, because it is one line from the story work and is a known live defect:**
`FlavorDocument.EventProse` is parsed, validated and cached but never written back to
`TimelineEvent.LocalAngle`, so per-event LLM prose currently reaches no surface at all.

---

## Wave 6 — UI

### Spine

1. `docs/contracts/ui_bindings.md` → **schemaVersion 9**, new group `agora.stories`, purely additive
   (nothing renamed, per the contract's own rule):
   | Binding | Kind | Direction |
   |---|---|---|
   | `agora.stories.live` | `ValueBinding<List<StoryPayload>>` | C# → UI |
   | `agora.stories.archive` | `ValueBinding<List<StoryBriefPayload>>` | C# → UI |
   | `agora.stories.article` | `GetterMapBinding<string, StoryArticlePayload>` | C# → UI |
   | `agora.stories.power` | `ValueBinding<PowerPayload>` | C# → UI |
   | `agora.stories.setResponse` | `CallBinding<SetResponseArgs, string>` | **UI → C#** |
   | `agora.stories.declareManual` | `CallBinding<…, string>` | **UI → C#** |
   | `agora.stories.resolveNow` | `CallBinding<string, string>` | **UI → C#** |
   Sort keys and payload caps declared alongside, as §4.5 does for news.
2. `src/Agora.Mod/UiBindings/AgoraUiPayloads.cs` — the payload shapes (`IJsonWritable`).
3. `ui/types/bindings.d.ts` — the mirror, and **fix its stale "schemaVersion 5" authority comment**
   at the same time (`docs/status.md` known gap 2).

### Lanes

| Lane | Owns (exclusive) | Task |
|---|---|---|
| **6a** | `src/Agora.Mod/UiBindings/AgoraStoriesUISystem.cs` (new), `AgoraUiProjection.Stories.cs` (new) | Publisher + projection. Register at `SystemUpdatePhase.UIUpdate` in `Mod.cs`; publish only on `StateVersion` change, like every other Agora UI system. |
| **6b** | `ui/src/panels/Stories/**` (new) | The panel. Live stories with headline + article; three **“Tackle <event name>”** buttons that expand into the four response options; textareas for Ignore and Manual; a **Resolve now** control; the archive below. Flexbox only — Gameface has no CSS grid. Add `"stories"` to `TAB_ORDER` **and** to `Dashboard.renderTab`'s switch — the `default:` falls through to `SeatsPanel`, so a missing case renders the wrong panel silently. |
| **6c** | `ui/src/shell/AgoraButton.tsx`, `Shell.module.scss`, `SettingsPanel.tsx` | The **political-power counter next to the mod icon, top left** — `AgoraButton` is already appended to `GameTopLeft` and already renders a dot, label and date, so the counter is an added element, not a new surface. Plus the settings rows for every new tunable. |
| **6d** | `ui/src/shell/StoryModal.tsx` (new), `storyPause.ts` (new), `src/Agora.Mod/Core/StoryAlert.cs` (new) | **A separate modal lane, not a repoint.** Story cards get their own queue and their own admission policy: **one card per story, not per event** — all three events render inside the one card. At 2 stories per cycle that is 2 interruptions, matching the current major-news cadence. `major` stays the **engine's** verdict; the UI never compares a severity to a threshold of its own. |

**Why stories get their own alert lane rather than riding `agora.news.alerts`.** Three reasons, all
from the shipped contract:

- The alert contract states that *every alert `id` is a feed row's id* and a body is fetched from
  `agora.news.article` under that same id. Once Wave 7 retires the feed, a story alert's id is not a
  feed row id — and `BuildArticle` answers an unknown key with `EMPTY_NEWS_ARTICLE` rather than
  throwing, so the failure is **a blank masthead with nothing logged**.
- `ArticleModal` renders `alerts[0]` **or nothing** — one card at a time by construction — and holds
  the sim pause barrier while it is up. Six event-cards per cycle would be six serialised forced
  pauses on the first frame of the month, each needing an `ackAlert` round trip.
- `AlertQueueMax` drops the oldest and logs when it does. On the news lane a dropped card is a missed
  headline; on the story lane it would be **a decision the player never got to make.**

**Text entry is unproven under Gameface** (`docs/status.md` known gap 0): `PartyEditor.tsx` holds the
only `<input>`/`<textarea>` in `ui/src`, they have never been rendered in game, and nothing stops key
propagation — space, digits, `b`, `p` may reach game hotkeys. This rework adds **two textareas per
event, six per story**, so lane 6b must copy `PartyEditor`'s pattern *and* add `onKeyDown` propagation
stopping, and the manual gate must test it.

---

## Wave 7 — Retirement, balance and gates

### Spine
`docs/contracts/ui_bindings.md` → **schemaVersion 10**, removing `agora.news.feed`, `.article`,
`.events`, `.alerts`, `.ackAlert`, `.wakeFlavor`. This is the "remove the old name in a **later**
change" half of the contract's never-rename-in-place rule, and Wave 6 is what makes it safe.
`agora.news.mandates` **stays** — the mandate tracker is unrelated to stories and is consumed by the
Parties tab too.

### Lanes

| Lane | Owns (exclusive) | Task |
|---|---|---|
| **7a** | `ui/src/panels/News/**` (delete), `AgoraNewsUISystem.cs`, `AgoraUiProjection.BuildFeed`/`BuildArticle` | Retire the feed. Re-home the mandate tracker into the Stories panel or its own tab — it is the only part worth keeping. |
| **7b** | `data/engine_tuning.json`, `src/Agora.Core/Tuning/TuningPresets.cs` | The balance pass: story frequency, effect magnitudes, power economy, and the presets behind `StoryDifficulty` / `PowerIntensity`. Run the headless multi-year harness and tune against it. |
| **7c** | `tests/Agora.Core.Tests/**` | The full determinism + migration sweep, including a fixture built from a **real pre-rework save** at state v4 / settings v3 / flavor cache v2, proving it loads, upgrades and ticks. |
| **7d** | `docs/status.md`, `politicsmodplan.md`, `CLAUDE.md`, `data/CLAUDE.md`, `.claude/skills/add-event/SKILL.md` | Ratify §7's new effect kind, add a §15 for the story system, update the routing table, and split `/add-event` into timeline vs civic-event guidance. |

---

# Part III — The two skills

**Both are written and in the tree** — `.claude/skills/nextwave/SKILL.md` and
`.claude/skills/commitpushpr/SKILL.md`. Nothing in `.claude/skills/` dealt with git, worktrees or PRs
before, so there was no collision. Both follow the house format: frontmatter of **only** `name` and
`description`, body opening `# /<name>`, hard-wrapped ~100 columns, numbered steps with a bolded
lead, and a `## Traps` section giving the *reason* each rule exists. The summaries below are the
record of what they encode; the files are the authority.

## `.claude/skills/nextwave/SKILL.md`

Invoked by **every wave orchestrator as its first action.** It is what keeps the structure alive
across handoffs, when the session that designed it is gone.

Steps it encodes:
1. **Read the inputs, in order** — this plan, `docs/plans/0004-wave-<N-1>-handoff.md`, and `docs/status.md`.
   Refuse to start if the previous wave's PR is not merged into `EventSystemRefresh`.
2. **Create the umbrella** — `git checkout EventSystemRefresh && git pull && git checkout -b
   event-system/wave-<N>`.
3. **Land the spine alone.** List the wave's spine files from this plan; write them; build; test;
   commit as one commit titled `wave-<N> spine`. No lane exists yet.
4. **Declare lane ownership** — write `docs/plans/0004-wave-<N>-lanes.md` with one row per lane: branch,
   worktree path, exclusive path list, acceptance criteria, and the seam signatures other lanes
   depend on. **A path may appear in exactly one row.** The skill states this as the invariant to
   check before spawning anything.
5. **Spawn worktrees** — `git worktree add .claude/worktrees/w<N>-<lane> -b event-system/w<N>-<lane>
   event-system/wave-<N>`, then `npm install` inside each `ui/` that needs it. Never junction
   `node_modules`.
6. **Dispatch coders in parallel**, one `coder` subagent per lane, each given only its own row plus
   the routing table's relevant `CLAUDE.md` files.
7. **Review each lane** with the `reviewer` agent against `/review-checklist` before merging it.
8. **Merge in the declared order**, build and test after each. A conflict means the wave plan was
   wrong — stop and fix the plan, do not hand-resolve.
9. **Hand off to `/commitpushpr`.**

Traps it names: the `node_modules` junction; `npm run build` deploying over the player's live mod;
`dotnet test` on the solution pulling in `Agora.Mod`; `npm run check` neither typechecking nor
diffing CSS class names, so `npx tsc --noEmit` is a separate obligation; `refsrc/` being absent from
every worktree, so a lane that greps it locally gets a false negative; and the rule that shared files
belong in the spine, never in a lane.

## `.claude/skills/commitpushpr/SKILL.md`

Fired at the orchestrator once all lanes are merged into the umbrella and it is ready to go back.

Steps it encodes:
1. **Prove it green** — `dotnet build Agora.sln`, `dotnet test tests\Agora.Core.Tests\…csproj`,
   `cd ui && npx tsc --noEmit`. Record the test count; a drop is a defect, not noise.
2. **Verify migrations** — every schema version this wave bumped has a step *and* a fixture test at
   the old version. The skill lists the six documents and their current versions so the check is
   mechanical, and reminds the orchestrator that `Migrate` must stay idempotent.
3. **Clean up worktrees** — `git worktree remove` each lane, `git worktree prune`. Delete merged lane
   branches.
4. **Commit and push** the umbrella, with the co-author trailer.
5. **Open the PR** — `gh pr create --base EventSystemRefresh --head event-system/wave-<N>`, body
   covering: what shipped, schema versions moved, what is *not* done, manual gates only the player
   can walk, and the test delta. Ends with the Claude Code generation footer.
6. **Write the handoff** — `docs/plans/0004-wave-<N>-handoff.md`, and this is the load-bearing step. It
   contains a **ready-to-paste prompt for the next orchestrator**: the wave number, a one-paragraph
   state of the world, the PR link, spine file list, lane table, anything the wave discovered that
   contradicts this plan, and the literal instruction to begin with `/nextwave`.
7. **Update `docs/status.md`** with the wave's row.

Traps it names: never force-push the umbrella; never merge the PR itself (the owner reviews); a green
build is not a passed gate — game-facing code (`AgoraRuntime`, `UiBindings/**`) is not linkable into
the test suite and gets manual gate items in `docs/status.md` instead of manufactured coverage.

---

# Part IV — Existing saves

The rework must be invisible to a thirty-year save except that stories start appearing. Six documents
move; each gets a step and a fixture.

| Document | From | To | Wave | Migration |
|---|---|---|---|---|
| `state_*.json` (`PoliticalState`) | 4 | 5 | 0 | `LastCompletedTickMonth` seeded from the state's own `Date`, so an existing save does not re-run its last month on first load. |
| `metric_ring.json` (new document) | — | 1 | 0 | Created empty; fills forward. Absent file is not an error. |
| `snapshot` (`CitySnapshot`) | 3 | 4 | 1 | Additive; absent statistics read as zero, not as an error. |
| `metric_history.json` | 1 | 2 | 1 | New trend windows start empty and fill forward. |
| `state_*.json` (`PoliticalState`) | 5 | 6 | 2 | Empty `LiveStories`/`StoryArchive`/`EventPool`/`PlayerCommands`; `Power.Balance = 0`. |
| `settings.json` / nested settings | 3 | 4 | 2 | Defaults for all new tunables, via a shared helper called from **both** paths. |
| `politics_flavor` + `flavor_cache.json` | 2 | 3 | 5 | Limits rose, so nothing is over-length; new collections default empty. |

**Check `SidecarSchema.Migrate` is idempotent before adding the 4→5 step.** `PoliticalState`'s own
`SchemaVersion` default is `3` while `CurrentStateVersion` is already `4`, so a freshly constructed
state claims a version it is not — meaning a 4→5 step could run against an object that was never v4.
Wave 0a reconciles this; the test project's own csproj comment names idempotency as the specific
property non-negotiable #6 depends on.

**Rules the waves must hold:**

- **Never retro-generate.** A save loaded mid-month has no live story; the first one opens on the
  next month boundary. Backfilling stories would invent history and desync the state hash.
- **Never reset politics.** Migration upgrades in memory and continues, per `/schema-change` step 2.
- **A save newer than this build is refused, file untouched** — `SidecarSchema` already does this
  (`TooNew`); do not weaken it.
- **Every step uses frozen local constants**, never live tuning reads, so a later retune cannot
  retroactively change what an old save migrates into.
- **Wave 0's fix is itself save-affecting and must ship first.** Until `LastCompletedTickMonth`
  exists, every reload re-runs a month; adding power or streak accumulators before it turns a
  cosmetic duplicate into an exploit.

---

# Verification

**Automated, per wave:**
- `dotnet build Agora.sln` — 0 warnings, 0 errors.
- `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj` — currently 1319 tests; the count must
  rise every wave and never fall.
- `cd ui && npx tsc --noEmit` — separately from `npm run check`, which does neither typechecking nor
  CSS class parity.
- Determinism: same seed twice → identical SHA-256 of serialized state, extended to cover a story's
  full open → respond → resolve arc.
- Migration fixtures at every prior version, including one built from a **real pre-rework save**.

**Manual gate — only the player can walk it.** `AgoraRuntime` and `src/Agora.Mod/UiBindings/**` are
not linkable into the headless suite by design, so these are gate items, not tests:

0. **Wave 0 first, and on its own.** Save mid-month, quit to menu, reload, and confirm from
   `Agora.log` and the sidecar that the month does **not** run twice: no duplicate poll, no
   double-counted `FringeWatch.MonthsObserved`. This is the gate the whole power economy rests on.
1. **The arc.** Load an existing save. Draft month → two stories appear with headline and article.
   Expand each event, pick a different response for each of the three. Next month's tick →
   resolution story appears, effects land, political power moves by the published amounts.
2. **Resolve now.** Press it during the draft month. The story resolves immediately and the resolve
   month's pass then does **nothing** — no second resolution, no doubled effects. Then let a full
   cycle run untouched and confirm the automatic resolution lands on its own.
3. **The reload matrix.** Save and reload before draft, between draft and resolve, and after resolve.
   The story, the responses and the power balance all survive; nothing double-resolves. An early
   resolve replayed after a reload produces the **same verdict**, because it reads its recorded
   snapshot rather than re-measuring.
3b. **Interruption budget.** Confirm exactly **one modal per story** — not one per event — and that
   two stories in a cycle mean two cards, not six.
4. **Text entry under Gameface** — the highest-risk unverified area, now multiplied by six textareas
   per story. Focus one and press space, digits, `b`, `p`: the sim must not pause, change speed or
   open bulldoze.
5. **Political power counter** renders next to the mod icon top-left, tracks the ledger, and blocks
   an override the player cannot afford with a legible reason rather than a silent no-op. **Try to
   farm it:** save, resolve a story, reload, resolve again — the balance must not move twice.
5b. **A goal whose metric is unreadable** shows as *held*, not failed, and costs no power.
6. **Debt penalty** — drive the balance negative and confirm the revenue penalty appears in the
   effects ledger, is capped, and lifts when the balance recovers.
7. **No Claude** — unset the CLI and confirm stories still get the specified fallback prose: major
   event name as headline, the three names and descriptions as the article. This path must be
   playable, not merely non-crashing.
8. **A thirty-year save** loads, upgrades, and ticks without losing parties, mandates or effects.
