# Wave 2 — lane ownership

The story engine core. Pure `Agora.Core`: no game types, no IO, and **the first wave whose whole
output is covered by the headless suite** — every lane below compiles into a test, which is not true
of waves 1, 4 or 6.

The spine (`ac78590`) landed every cross-cutting file before this file was written and before any
worktree existed. Lanes own strictly disjoint paths. **A merge conflict in this wave is a bug in this
document, not something to resolve by hand.**

Branch and worktree names follow waves 0 and 1: `event-system/w2-<lane>` in
`.claude/worktrees/w2-<lane>`, cut from `event-system/wave-2`.

---

## What the spine already landed — do not rewrite any of it

| File | What it now holds |
|---|---|
| `src/Agora.Core/Stories/CivicEvent.cs` | `CivicEvent`, `TriggerSpec`, `CheckSpec`, `CheckResult`, `StoryTier`/`StoryTiers`, and the `TriggerKind` / `Comparison` / `TriggerScope` grammar |
| `src/Agora.Core/Stories/Story.cs` | `Story`, `StorySlot`, `EventPoolEntry`, `MetricReading`, the four outcome enums, and their `Clone()`s |
| `src/Agora.Core/Stories/PoliticalPowerState.cs` | `PoliticalPowerState`, `PowerLedgerEntry`, `PlayerCommand` |
| `src/Agora.Core/Stories/StoryEngineTypes.cs` | `StoryReadContext`, `StoryDraftResult`, `StoryResolutionResult` |
| `src/Agora.Core/Contracts/PoliticalState.cs` | state v6 · settings v4 · the five new collections · the two watermarks |
| `src/Agora.Core/Tuning/EngineTuning.cs` | `StoriesTuning`, `PowerTuning`, `PowerTierAmounts` |
| `data/engine_tuning.json` + its schema | `stories` and `power`, schemaVersion 6 |
| `src/Agora.Mod/Persistence/SidecarSchema.cs` | state 5→6, settings 3→4, `UpgradeSettingsObjectToV4` |
| `src/Agora.Core/Determinism/SeedStreams.cs` | `StoryDraft`, `StoryPool`, `StoryTiebreak`, `StoryDistrictTarget`, `PowerAccrual` |
| `PoliticalEngine.CloneState` · `AgoraSettings.Clone` | both hand-maintained lists already carry every new field |

**No lane edits any of the above.** If your lane appears to need a change there, that is an
escalation to the orchestrator, not an edit — it is by definition a file another lane also reads.

---

## Lanes

### 2a — the evaluator and the metric registry

| | |
|---|---|
| **Branch** | `event-system/w2-2a` |
| **Worktree** | `.claude/worktrees/w2-2a` |
| **Owns (exclusive)** | `src/Agora.Core/Stories/TriggerEvaluator.cs`, `src/Agora.Core/Stories/MetricRegistry.cs` |

Replace both `AGORA-SEAM(wave-2/2a)` stubs. Evaluate every `TriggerSpec` against `CitySnapshot` +
history; the same evaluator serves `CheckSpec` at resolution — **one implementation, two callers**,
because a threshold has to mean the same thing at draft as at resolution.

**Acceptance**
- Every `TriggerKind` and every `TriggerScope` handled; sorted iteration throughout, no
  dictionary-order dependence, including in `AnyDistrict`.
- A reading that cannot be taken returns `Unmeasurable`, never `NotMet`.
- `MetricRegistry` ids match `Agora.Mod.Sensors.MetricNames` exactly — see the seam note below.
- Builds with `dotnet build src/Agora.Mod/Agora.Mod.csproj -p:UseCsiiToolchain=false`.

### 2b — drafting

| | |
|---|---|
| **Branch** | `event-system/w2-2b` |
| **Worktree** | `.claude/worktrees/w2-2b` |
| **Owns (exclusive)** | `src/Agora.Core/Stories/StoryAssembler.cs`, `src/Agora.Core/Stories/EventPoolWeighting.cs` |

Pool refresh → weighted seeded draw → one major + two minors, ×`storiesPerCycle`; one extra bare
story per mandatory event. Clear the drawn entries, increment `MissStreak` on everything left.

**Degradations are valid outcomes, never errors:** no major left ⇒ promote a minor and take three
minors; too few events ⇒ a shorter story is a story.

**Acceptance**
- Every selection and every degradation goes through the declared total order — weight desc, then
  `MissStreak` desc, then `EventId` ordinal asc. Leaving "which minor is promoted" to collection
  order is the determinism bug `Agora.Core/CLAUDE.md` calls the most common one.
- `Weight` is monotonic non-decreasing in `MissStreak`, saturating at `stories.maxMissStreak`.
- Same seed twice ⇒ byte-identical `StoryDraftResult`.

### 2c — resolution

| | |
|---|---|
| **Branch** | `event-system/w2-2c` |
| **Worktree** | `.claude/worktrees/w2-2c` |
| **Owns (exclusive)** | `src/Agora.Core/Stories/StoryResolution.cs` |

The 2-of-3 rule and its edge cases. Per-slot verdict by response mode: `Goal` runs the `CheckSpec`
through 2a's evaluator; `PowerOverride` is an automatic success already paid for; `Ignore` is an
automatic failure; `Manual` reads the player's declaration and is **neutral until declared**.

**Acceptance**
- The threshold is a ratio over **scored** slots. A full story of three needs
  `stories.successThreshold`; a story of fewer needs **all** its scored slots.
- `Unmeasurable` slots are in neither the numerator nor the denominator.
- A story with **no** scored slots resolves `Abandoned`, not `Failure`.
- Calls `TriggerEvaluator`; does not write a second comparison implementation.

### 2d — the power economy

| | |
|---|---|
| **Branch** | `event-system/w2-2d` |
| **Worktree** | `.claude/worktrees/w2-2d` |
| **Owns (exclusive)** | `src/Agora.Core/Stories/PoliticalPower.cs` |

Accrual, award, penalty, affordability, debt state. Pure arithmetic — the *consequence* of debt is
wave 4's effect and nothing here applies anything.

**Acceptance**
- Accrual ≤ `power.maxMonthlyGain`, scaled by governing vote share through `gainPopularityCurve`;
  no government ⇒ zero, never negative.
- **A manual-declared award is capped at the minor rate whatever the tier.** This is the one real
  exploit surface in the design: otherwise a one-word justification on a mandatory event mints 50.
- An `Unmeasurable` slot moves the balance by exactly zero.
- `CanAfford` tolerates an already-negative balance: debt is a state, not a bar to play.

### 2e — the tests

| | |
|---|---|
| **Branch** | `event-system/w2-2e` |
| **Worktree** | `.claude/worktrees/w2-2e` |
| **Owns (exclusive)** | `tests/Agora.Core.Tests/Story*.cs` (new), `TriggerEvaluatorTests.cs`, `MetricRegistryTests.cs`, `EventPoolWeightingTests.cs`, `PoliticalPowerTests.cs` (all new), plus **additions only** to `SidecarMigrationTests.cs` and `PerSaveSettingsTests.cs` |

**This lane's tests will FAIL in its own worktree, and that is correct rather than a defect.** They
drive lanes 2a–2d, which are still `AGORA-SEAM` stubs on this branch: the tests *compile* (the
signatures are landed) but assert against trivial return values. Write to the behaviour contract in
this document, do not weaken an assertion to make it pass locally, and report the failing list. The
lane is verified on the umbrella after 2a–2d merge, and reviewed there.

**Acceptance** — determinism (same seed twice ⇒ identical hash) across a full open → respond →
resolve arc; every degradation branch; pity-weighting monotonicity; migration from a real v5 fixture;
and the registry-vocabulary pin below.

---

## Seams — both ends, published here

A lane must not have to guess at a name another lane owns.

| Seam | Signature | Written by | Read by |
|---|---|---|---|
| Trigger evaluation | `CheckResult TriggerEvaluator.Evaluate(TriggerSpec, StoryReadContext)` | 2a | 2b, 2e |
| Check evaluation | `CheckResult TriggerEvaluator.EvaluateCheck(CheckSpec, double? baseline, StoryReadContext)` | 2a | 2c, 2e |
| Metric read (city) | `double? MetricRegistry.ReadCity(CitySnapshot, string metricId)` | 2a | 2b, 2c, 2e |
| Metric read (district) | `double? MetricRegistry.ReadDistrict(DistrictSnapshot, string metricId)` | 2a | 2b, 2c, 2e |
| Metric id validity | `bool MetricRegistry.IsKnown(string metricId, TriggerScope)` | 2a | 2b, 2e, and wave 3's catalog loader |
| Drafting | `StoryDraftResult StoryAssembler.Draft(PoliticalState, IReadOnlyList<CivicEvent>, StoryReadContext, Guid, SimDate, EngineTuning)` | 2b | 2e |
| Pool weight | `double EventPoolWeighting.Weight(EventPoolEntry, CivicEvent, EngineTuning)` | 2b | 2e |
| Pool order | `int EventPoolWeighting.Compare(EventPoolEntry, double, EventPoolEntry, double)` | 2b | 2e |
| Resolution | `StoryResolutionResult StoryResolution.Resolve(Story, IReadOnlyList<CivicEvent>, StoryReadContext, EngineTuning)` | 2c | 2e |
| Accrual | `int PoliticalPower.AccrualFor(double governingVoteShare, EngineTuning)` | 2d | 2e |
| Override cost | `int PoliticalPower.OverrideCost(StoryTier, EngineTuning)` | 2d | 2e |
| Affordability | `bool PoliticalPower.CanAfford(PoliticalPowerState, StoryTier, EngineTuning)` | 2d | 2e |
| Slot award | `int PoliticalPower.AwardFor(SlotOutcome, StoryTier, bool manualDeclared, EngineTuning)` | 2d | 2e |
| Debt state | `bool PoliticalPower.IsInDebt(PoliticalPowerState)` | 2d | 2e |

### The metric vocabulary, and why it is two copies

`MetricRegistry` (Core) and the metric-name constants in `Agora.Mod` must carry the **same strings**,
and `Agora.Core` may never reference `Agora.Mod` — so there is necessarily a second copy, and two
copies drift. **The pin is a test, and lane 2e owns it:** the suite compile-links `MetricHistory.cs`,
so it can compare the two sets directly.

> **Corrected mid-wave — the first version of this section was wrong twice**, and both errors were
> mine rather than a lane's:
>
> - **There is no `MetricNames` class.** The constants are `public const string` members on
>   **`MetricHistory` itself** (`src/Agora.Mod/Sensors/MetricHistory.cs`). Exclude
>   `CityScope = "city"`, which is a scope segment and not a metric name.
> - **"18 city-scope and 3 district-scope" is not the total.** Those count only wave 1's
>   city-statistics additions — which is what the block comment at `MetricHistory.cs:598` is
>   counting. The full vocabulary also carries the pre-existing group (`landValue`, `rent`,
>   `population`, five `education.*`, three `wealth.*`, `happiness`, `unemployment`, `crimeRate`,
>   `pollution`, `serviceCoverage`, `commuteMinutes`, `trafficCongestion`), making the registry
>   **36 city-scope / 19 district-scope**.
>
> The pin must assert **neither** figure: derive both sides reflectively and compare the sets, so a
> failure names the missing string rather than a count. A test that memorises 36/19 rots on the next
> sensor.

A name may be **added but never renamed** — the sidecar fingerprint is taken over them sorted, the
same rule that governs a seed stream name.

**A third string vocabulary exists and is pinned by nothing.** `DistrictSnapshot.CityFallbackFields`
holds *property* names, not metric ids (`"AverageRent"` versus `"rent"`), and the education and
wealth ids each collapse onto one marker because the sensor falls back on a whole distribution.
`MetricRegistry.FallbackFieldFor` carries that mapping. `commuteMinutes` and `trafficCongestion` are
**city-only**, per `MetricHistory`'s own recorder comment.

---

## Decisions already closed — do not re-litigate, and where they are closed in the code

| Question | Answer | Authority |
|---|---|---|
| Is `StoryTier` a stored field? | **No, derived.** | `StoryTiers` doc comment |
| Should an unreadable metric fail the player? | **No — `Unmeasurable`, scored in neither half.** | `CheckResult` doc comment |
| Do story events go in `ActiveEvents`? | **No.** | `PoliticalState.LiveStories` block comment |
| Is there a day-15 resolution? | **No. There are no days.** | `StoriesTuning.CycleMonths` doc comment |
| What order do pool draws use? | weight desc, `MissStreak` desc, `EventId` asc | `EventPoolWeighting.Compare` doc comment |
| Can a `Delta` name `unlockedFeatureIds`? | **No — no historical series exists.** | `TriggerSpec.WindowMonths` doc comment |
| Is the manual award capped? | **Yes, at the minor rate — the AWARD only.** A self-declared failure pays the real tier. | `PoliticalPowerState` remarks |
| What does silence score? | **Not-met.** `Unaddressed`, and `Manual` still undeclared at resolution, both score as failure. | `SlotResponse` remarks |
| Is silence "unmeasurable"? | **No.** `Unmeasurable` means the engine could not read the city, and nothing else. | `SlotOutcome.Unmeasurable` remarks |
| How long is a cycle? | `CycleMonths` is the **period**; draft-to-resolution is `CycleMonths - 1`. | `StoriesTuning.CycleMonths` doc comment |

### Rulings taken mid-wave, after the lanes reported

These **supersede** anything above that disagrees, and two of them reverse an instruction a lane was
originally given. Where a lane built the earlier rule faithfully, that is the brief's defect and not
the lane's.

1. **Silence scores as failure** (owner decision). An unaddressed slot, and a `Manual` slot still
   undeclared when its story resolves, both score `NotMet`. The earlier rule — score them
   `Unmeasurable` — made doing nothing strictly cheaper than every response that could fail: `Ignore`
   cost 25 on a mandatory event while never opening the story cost nothing, so the rational play on
   anything you expected to lose was to leave it alone. That inverts the premise of the whole
   feature. The accepted cost is that a player who never saw the card is charged for it, which leans
   on wave 6's story modal actually rendering.
2. **The manual cap is one-sided.** Award capped at minor; penalty at the real tier. Capping both
   handed an 80% discount on every mandatory failure to anyone who preferred the Manual button, with
   no lying required.
3. **`MetricReading` gains `DistrictId`.** A reading is identified by metric **and** district
   together. Without it, a district-scoped check resolved early recorded nothing and re-measured a
   moved city on replay — a determinism hole, closed now rather than in wave 4, which is what would
   have built on it. Lookups must match on both fields.

4. **Re-use is gated on a cooldown, not on the archive** — `EventPoolEntry.LastDraftedMonth` against
   `stories.reuseCooldownMonths` (6), with `stories.maxMandatoryPerCycle` (2) bounding the events
   that are exempt from it. Excluding anything the archive remembered emptied a ~40-event catalog by
   month 14 into an absorbing state: nothing drafted, so nothing resolved, so nothing archived, so
   nothing was ever released. Archive-based exclusion is sound only while
   `archiveRetention × eventsPerStory < liveCatalogSize`; at 40 and 3 it names 120 slots over 40
   events.

5. **A drawn entry STAYS in the pool. This reverses "clear the pool afterwards" in row 2b above,
   and that row is now wrong where it says otherwise.** The cooldown stamp lives on the entry, so the
   entry has to survive the months it is counting: a drawn entry is retained with `MissStreak` reset
   and `LastDraftedMonth` stamped, and it is kept through the cycle its story is live and through any
   month its trigger lapses. Drop it instead and it is re-admitted next cycle with
   `LastDraftedMonth = -1`, at which point the cooldown does nothing whatsoever.

   An entry sitting out its cooldown is **not** aged — it was never offered, so it was never passed
   over, and ageing it would hand it a pity bonus for time it did not spend waiting.

   Anything asserting that a drawn id is *absent* from `UpdatedPool` must instead assert it is
   **present, streak 0, stamped**.

6. **Per-save settings win over the tuning key of the same name** when set (`> 0`), tuning being the
   fallback — non-negotiable #10, following `TickPlanner.SnapshotsToPrune`. Applies to
   `StoriesPerCycle` and `EventsPerStory`.

### Known-unreachable, recorded so it is not mistaken for tested behaviour

`PoliticalPower.AwardFor(NotMet, tier, manualDeclared: true)` cannot be reached from the resolution
path: a *declared* `Manual` slot always yields `Met`, so a failing `Manual` slot always carries
`manualDeclared == false`. The penalty half of the one-sided cap is therefore defensive contract
rather than live behaviour today. It is kept because wave 4's `DeclareManualOutcome` is where a
self-declared *failure* would become expressible.

The residue this leaves is accepted and not closable in arithmetic: a player may always declare
success for the minor award rather than take an honest failure at the real tier. Closing it belongs
at the response layer — making `Manual` unavailable when a slot's check is measurable — not in the
award schedule.

---

## What no lane may do

- **No lane tests `AgoraRuntime` or `UiBindings/**`.** They are deliberately not linkable into the
  headless suite; faking the runtime to manufacture coverage is itself a review-blocking defect.
  Nothing in wave 2 touches either, so this should not arise — if it seems to, escalate.
- **No lane runs `npm run build` or a bare `dotnet build Agora.sln`.** Both deploy into the player's
  live `…\Mods\Agora.Mod`. Verify with
  `dotnet build src/Agora.Mod/Agora.Mod.csproj -p:UseCsiiToolchain=false`.
- **No lane runs `dotnet test Agora.sln`** — it pulls in `Agora.Mod`, which needs the game installed.
  Always `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj`.
- **No lane touches `ui/`.** No worktree needs `npm install` this wave and `npx tsc --noEmit` is not
  an obligation on any lane.
- **`refsrc/` does not exist inside a worktree.** It is gitignored and lives only at
  `C:\Users\serap\OneDrive\Documents\GitHub\Cs2CompanionApp\refsrc`. No wave-2 lane should need it —
  this is pure Core — but a lane that greps `./refsrc` locally gets zero hits and would wrongly
  conclude an API is absent.

---

## Path disjointness — checked before any worktree was created

Every path below appears in **exactly one** row.

```
src/Agora.Core/Stories/TriggerEvaluator.cs      2a
src/Agora.Core/Stories/MetricRegistry.cs        2a
src/Agora.Core/Stories/StoryAssembler.cs        2b
src/Agora.Core/Stories/EventPoolWeighting.cs    2b
src/Agora.Core/Stories/StoryResolution.cs       2c
src/Agora.Core/Stories/PoliticalPower.cs        2d
tests/Agora.Core.Tests/**                       2e   (sole owner of the test project this wave)
```

The four contract files in `src/Agora.Core/Stories/` — `CivicEvent.cs`, `Story.cs`,
`PoliticalPowerState.cs`, `StoryEngineTypes.cs` — appear in **no** row. They are spine, and frozen.

## Merge order

`2a → 2b → 2c → 2e`, building and testing after each, because 2b and 2c call 2a's evaluator and 2e
drives all of them.

**`2d` shares no file and no seam with any in-flight lane** — `PoliticalPower.cs` is called by
nothing until wave 4 — so it may merge as soon as it is reviewed, at any point in that sequence,
rather than idling. Say so in the merge commit.
