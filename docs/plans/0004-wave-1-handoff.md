# Wave 1 → Wave 2 handoff

Wave 1 (sensors and city statistics) is code complete, reviewed and merged into `event-system/wave-1`.
This file is written for a session that was not here and has none of the context.

---

## Ready-to-paste prompt for the next orchestrator

> You are the orchestrator for **Wave 2 — the story engine core** of the AGORA event-system rework.
> **Begin with `/nextwave`.** Read `docs/plans/0004-event-system-rework.md` (the plan),
> `docs/plans/0004-wave-1-handoff.md` (this file — it **outranks the plan** wherever the two
> disagree, because it was written against the code) and `docs/status.md`.
>
> Wave 1 built the sensors. Confirm wave 1's PR is merged into `EventSystemRefresh` before you cut
> anything; `/nextwave` step 2 then has you prove the base builds and tests green and record the
> count **yourself**.
>
> Wave 2 is pure `Agora.Core` — no game types, no IO, fully testable, and the first wave whose whole
> output is covered by the suite. Before you design the spine, read **"Contradictions with the
> plan"** below: six of the plan's assumptions about what the game exposes are wrong, two of its
> Part IV migration rows are struck, and there is one unresolved question (Q1) that
> `CheckResult.Unmeasurable` **must not be built on top of**.

---

## State of the world, in one paragraph

AGORA's engine sees the city through one `CitySnapshot`, assembled once per sim day from a family of
ECS sensors. Until this wave it could see population, happiness, money, pollution, services, rent and
commuting — but nothing the game's own **city statistics screen** shows, which is where most of the
rework's event triggers were meant to read from. Wave 1 added three sensor systems against
`CityStatisticsSystem.GetStatisticValueLong` (already proven in the shipped mobility sensor, so the
central API premise was never at risk) and widened `CitySnapshot` to **schemaVersion 4**:
homelessness, migration, births, deaths, garbage production, uncollected garbage, tourists,
attractiveness, lodging, milestone level, lifetime XP, unlocked features and per-resource tax rates.
The pure half — merge, assembly, metric history and rehydration — was widened in step, so the new
scalars survive a reload rather than reading as fabricated zeros on the first tick after every load.
A scout report (`docs/scout/0004-city-statistics.md`) records exactly what the game exposes, with
file and line numbers, and **anything not marked CONFIRMED there does not get a trigger in wave 3.**

## PR

**PR:** https://github.com/SeraphinHesse/Cs2CompanionApp/pull/4
**Merge status: NOT merged.** The owner reviews. Wave 2 must not open its umbrella until it is in.

Note **wave 0's PR #3 is merged**; #4 is the only one outstanding.

---

## What actually shipped

23 commits on `event-system/wave-1`, 19 files, +4269/−12. **Zero merge conflicts across five lanes**,
the second wave running to prove the spine-first law.

### The spine (`573d675`, plus corrections in `9755dd4` and `376d0b5`)

| File | Change |
|---|---|
| `docs/scout/0004-city-statistics.md` | **New.** The authority on what the game exposes. Corrected once mid-wave (see below). |
| `src/Agora.Core/Contracts/CitySnapshot.cs` | `schemaVersion` 3 → 4. New `CityStatistics`, `TourismLevels`, `ProgressionState`, `ResourceTaxRate`, `TaxArea`. `UnlockedFeatureIds` and `IndustryTaxRates` as sorted lists. Three counts on **both** snapshots. |
| `data/schemas/snapshot.schema.json` | Same shape, `const` 4. |
| `src/Agora.Mod/Sensors/SensorReadings.cs` | The seam vocabulary the three sensor lanes write into. |
| `src/Agora.Mod/Sensors/Agora{Statistics,Progression,Tourism}SensorSystem.cs` | **New**, landed as compiling `AGORA-SEAM(wave-1/…)` stubs so lane 1d built from commit one. |
| `tests/…/HouseholdBudgetTests.cs` | The snapshot-version assertion made version-relative. |

### The lanes

| Lane | Delivered | Review |
|---|---|---|
| **1a** `AgoraStatisticsSensorSystem.cs` | Homelessness, migration, births, deaths, garbage production rate, uncollected garbage (city + district). The `AGORA-STATCOLLECTION` census. | **Blocked once**, two findings |
| **1b** `AgoraProgressionSensorSystem.cs` | Milestone level, lifetime XP, milestone progress, unlocked feature ids, per-resource tax rates. | **Blocked once** |
| **1c** `AgoraTourismSensorSystem.cs` | Tourists, attractiveness, lodging, attraction and signature counts (city + district). | Approved first pass |
| **1d** merge · assembly · history · rehydration · `AgoraSnapshotSystem` | The pure half, +6 tests. Two rulings implemented. | Approved, four claims re-derived by mutation |
| **1e** `FlavorPromptBuilder.cs` + contract tests | Four prompt bands, +21 tests. | **Blocked once** |

---

## Contradictions with `docs/plans/0004-event-system-rework.md`

**These outrank the plan.** Waves 2 and 3 plan against them.

### From the scout — six plan assumptions that are wrong about the game

1. **There is no landmark count.** No `Landmark` component or count exists anywhere in `Game.dll` —
   the only two occurrences of the word are DLC id lines. `TourismLevels.LandmarkCount` is struck;
   the contract ships `SignatureBuildingCount`, counting the `Game.Buildings.Signature` tag.
2. **`GarbageAccumulation` is a production rate per day, not a stockpile.** The game's own binding
   for the same value is named `productionRate`. It does not fall when collection improves. Shipped
   as `CityStatistics.GarbageProductionRate`, with the stockpile question answered separately by
   `UncollectedGarbage` (summed `GarbageProducer.m_Garbage`) — which is **not** the infoview's
   "stored garbage" either, so wave-3 prose must say *uncollected*, never *landfill*.
3. **No statistic is per-district. At all.** `CityStatisticsSystem.StatisticsKey` is
   `(StatisticType, int parameter)` — two fields — and there is no district statistics system. The
   plan's "mirror the reachable subset onto `DistrictSnapshot`" mirrors **three fields**, not a
   block. The city-only blocks are deliberately *absent* from `DistrictSnapshot` rather than
   mirrored there as a fallback that would be marked on every capture forever.
4. **`UnlockedFeatureIds` cannot be an enum or a hash.** `FeatureData` is a zero-field tag, so the
   identity is the prefab and the portable id is `PrefabSystem.GetPrefabName` — a **string**.
5. **City level and milestone index are one number**, not two. `MilestoneLevel.m_AchievedMilestone`.
6. **Birth rate is readable, and always was.** `docs/status.md` and scout 0001 §3 record it as
   unreachable; that finding was about `CityModifierType` — nothing can *modify* it, which is a
   different claim from being unable to *read* it. **Wave 3 must not skip a working trigger on the
   strength of the old note.** The same correction applies to death rate.

### Two Part IV migration rows are struck

7. **`snapshot` v3 → v4 has no migration to write.** `CitySnapshot` is not a sidecar document —
   `SidecarDocument` has five members and none is the snapshot. It is a contract, a JSON schema and
   an LLM prompt input, measured fresh every capture and never loaded back off disk. `/schema-change`
   steps 1, 3 and 4 applied; step 2 had nothing to act on.
8. **`metric_history` was not bumped, deliberately** — confirming wave 0's suspicion. It is a keyed
   series bag whose shape is `{series, samples[]}`, so ~21 new metrics are new *keys*, not a new
   shape. `SidecarSchema.cs`'s own comment warns that bumping the constant without adding a 1 → 2
   step turns every existing history into `NoPathForward`, i.e. silently discards it. **No sidecar or
   binding version moved this wave:** state 5, settings 3, timeline 1, metric history 1, flavor cache
   2, bindings unchanged.

### Rulings taken this wave that wave 2 inherits

9. **The three per-district counts fall back to ZERO, not to the city value**, and keep their
   `CityFallbackFields` marker. For an *average* the city value is a genuine estimate of a district's
   value, which is why the other twenty-three fields still use it. For a **sum** it is not an
   estimate of the part but an upper bound wrong for every district at once — and because the sensor
   lanes seed their districts at zero, this path now fires only when a sensor has gone blind, which
   is exactly when a large credible-looking number does the most damage.
10. **A district that fell back on one of those three records no sample for it** in `MetricHistory`,
    rather than recording the zero. Same reason rent and land value are not recorded from the
    assembled snapshot: a fabricated value poisons every window computed against it. The zero-fallback
    ruling changed *which* fabrication it would be, not whether it was one.
11. **`Experience` is lifetime `CitySystem.XP`, not `MilestoneSystem.currentXP`.** The latter is XP
    *since the last milestone* (`MilestoneSystem.cs:90`) and falls back toward zero every time the
    city achieves one. It is recorded into the metric history, so a resetting counter would hand a
    `delta` trigger a large negative swing at the moment of success.

---

## Traps aimed squarely at wave 2

- **`CheckResult.Unmeasurable` must not be built on an assumption about zero-versus-absent.**
  `GetStatisticValueLong` returns `0` for a genuine zero, for a statistic locked behind progression,
  and for a key that does not exist, with **no way to tell them apart** (scout §1.7, Q1 — still
  open). Lane 1a deliberately invents no sentinel: `null` there means only "the source was
  unavailable". Answering Q1 is wave 2's problem and the scout suggests a probe
  (`GetLookup().ContainsKey`) that was **not confirmed** to distinguish a locked statistic.
- **`Unmeasurable` is answerable off the live snapshot by the marker, and off a historical month
  only by probing the history.** `SnapshotRehydration` rebuilds a district from recorded samples
  alone, so its `CityFallbackFields` comes back **empty** and `HasCityFallbacks` **false** whatever
  the original month looked like. A consumer that asks a rehydrated district "did you fall back?" is
  told *"no, and the value is 0"* — the one wrong answer the arrangement exists to prevent. Two
  surfaces, two mechanisms; `MetricHistory.cs` says so at the vocabulary block.
- **No `delta` or `windowMonths` trigger may name `UnlockedFeatureIds` or `IndustryTaxRates`.** They
  are lists; `MetricHistory` stores one `double` per series per month, so no historical series stands
  behind either. A trigger may ask what is unlocked *today*. This is recorded as a decision in both
  `MetricHistory` and `SnapshotRehydration`, so wave 2 does not go looking for a series that was
  never omitted by accident.
- **The metric vocabulary is a contract.** 18 city-scope names and 3 district-scope ones, listed in
  `docs/plans/0004-wave-1-lanes.md` row 1d and implemented verbatim. Wave 2's trigger registry names
  these strings and the sidecar fingerprint is taken over them sorted, so a name may be **added but
  never renamed** without a migration — the same rule that governs a seed stream name.
- **`CloneStateCoverageTests.Properties()` filters on `CanWrite`** (carried from wave 0, still live).
  Wave 2 adds five collections to `PoliticalState`; **give every new member a setter** or the guard
  silently skips it and the hole it exists to close reopens.
- **Attractiveness at city scope has no fallback marker.** A blind tourism sensor reports
  "attractiveness 0" rather than "unknown", and unlike a district there is nothing to record which it
  was. Nothing reads it as an engine input today, so there is no defect now — but whoever writes the
  first trigger against it inherits this. Named in `SnapshotAssembly`'s city block.

---

## What nearly went wrong

Four defects reached review rather than the merge. **Two share one signature**, which is worth
naming because wave 2's scoring accumulators are exactly where it would recur: *a number that
collapses at the moment of achievement.*

- **`Experience` read a counter that resets at every milestone.** Caught by the lane, ruled by the
  orchestrator.
- **`MilestoneProgress` mapped a completed track to 0.0.** The guard against a non-finite value was
  correct in structure but wrong in the branch that fires: at the top of the tree `requiredXP`
  becomes *exactly* zero while the numerator keeps growing, so the division yields `+Infinity`, not
  `NaN`. Folding that into the NaN branch would have reported a city that finished the entire
  progression track as having made no progress — and handed `MetricHistory` a one-day fall of ~1.0,
  the largest negative delta a `[0,1]` metric can carry, at the instant of the city's biggest success.
- **A subsample stride republished a fabricated clean district.** Lane 1a correctly seeded every
  district at zero before its walk, but that is honest only while the walk is *exhaustive*. Under a
  stride a district can simply have had no producer land on its residue class, publishing a measured
  zero with no marker while sitting on a week of rubbish — and flickering between zero and a scaled
  estimate on alternate days, a shape a `delta` trigger reads as a crisis arriving and leaving.
- **A prompt band asserted a false fact about an unmeasured city.** Milestone level 0 means either
  "brand new" or "the sensor never read anything", and the band said *"a brand-new settlement with
  almost nothing unlocked"* — so a blinded sensor would put a frontier settlement two lines below a
  metropolis in every prompt for a session. **The lane had written this rule itself and applied it to
  three lines out of four.** The rule is now stated once: *the bottom band of every sensor-fed line
  must be true of an unmeasured city as well as an empty one.*

Two orchestrator errors, both caught because a lane escalated rather than coding around them: the
scout's quote of the game's attraction query was abbreviated to two exclusions where the source has
three (`Destroyed` matters — a burnt-out attraction contributes nothing to the game's own
attractiveness), and the `Experience` source above.

---

## Manual gates opened by wave 1 and not yet walked

Nothing below has been seen in game. **Lanes 1a, 1b and 1c compile into no test whatsoever** —
`GameSystemBase` is not linkable into the headless suite by design — so these are gate rows and no
test was manufactured for them. Lanes 1d and 1e are genuinely covered.

### The one that blocks wave 3

1. **The collection-type census.** `grep AGORA-STATCOLLECTION Agora.log` after a session. It must
   appear **exactly once** with a **non-zero** prefab count. **Record the `collection=` value for
   `BirthRate`, `DeathRate`, `CitizensMovedIn`, `CitizensMovedAway` and `MovedAwayReason` verbatim
   into wave 2's handoff.** This is the only thing that will ever answer scout Q2 — whether those
   five are counted per in-game day or per city lifetime — and **wave 3 cannot author a threshold on
   any of them until it is answered.** If any reads `Cumulative`, wave 3 must treat them as lifetime
   totals and trigger on deltas only.

### Units and ranges — the failures that look plausible in a log

2. **Homeless share is a fraction.** On a save with visible homelessness, confirm `homelessShare` is
   strictly between 0 and 1 — a value like `0.03`, **not** `3.0`. The game reports 0–100 and the
   contract wants 0–1; both look plausible.
3. **Tax rates are fractions.** Set the industrial slider to 20% and confirm every `Industrial` entry
   in `industryTaxRates` reads near `0.2`, not `20.0`.
4. **A completed milestone track reads as complete.** On a save with every milestone achieved,
   `milestoneProgress` must read **`1.0`**, and `metric_history.json`'s `milestoneProgress` series
   must **not** contain a ~1.0 single-month fall on the month the final milestone landed.
5. **Experience is monotonic across a milestone.** Capture before and after achieving one:
   `experience` must not fall. `milestoneProgress` *is* expected to drop, and that is what
   distinguishes the two fields at a glance.
6. **Attractiveness is raw and matches the game.** Read the number the tourism infoview shows and
   confirm the logged `attractiveness` is that same integer — not a 0–1 value, not a percentage.

### Correctness of the counts

7. **Signature isolation.** Place one signature building in district A: A's count rises by exactly 1,
   B's and C's are **unchanged**, city rises by exactly 1.
8. **Preview does not count.** Hold a placement preview over a district for a full in-game day
   without clicking; `attractionCount` must not move. (This is the `Temp` exclusion.)
9. **Destroyed, not merely bulldozed.** Let a fire destroy an attraction and leave the rubble
   standing. The count must fall at the next capture **while the entity is still on the map** — the
   only row that catches a missing `Destroyed` exclusion, since every other row passes without it.
10. **City is not a sum of districts.** Where buildings sit outside every drawn district, city
    `attractionCount` must exceed the sum of district counts, and no district may exceed the city.
11. **Features grow, and are not the whole catalogue.** `unlockedFeatureIds` on a mature save must be
    non-empty and must **grow** across a milestone. Fails on `[]` (the `HasComponent` instead of
    `HasEnabledComponent` signature) **and** fails if a brand-new city reports every feature in the
    game (the inverted-test signature). Neither failure is detectable from the other.
12. **Industrial and office do not bleed.** `software` must appear under `Office` and **not** under
    `Industrial`; `grain` under `Industrial` and **not** under `Office`. They share array slot
    `51 + index`, so a missing `Contains` filter shows up here and nowhere else.
13. **Garbage: production versus backlog.** Remove one district's garbage service for several days.
    That district's `uncollectedGarbage` must rise while `garbageProductionRate` stays roughly flat.
    If the production rate tracks the backlog, the two numbers have been crossed.

### Reload and per-save reset

14. **Save A then save B without restarting.** B's first snapshot must report B's own homeless,
    tourist and milestone figures — **specifically not A's.** This is the one thing 1d's wiring can
    get wrong that no test in the suite can see.
15. **A capped capture withholds districts rather than zeroing them.** Set
    `sensors.maxBuildingsPerCapture` below the city's producer count: every district's
    `uncollectedGarbage` must read `0` **with a `uncollectedGarbage` entry in its
    `cityFallbackFields`**. A `0` with no marker means the withholding is not taking effect. City
    `uncollectedGarbage` must stay within roughly ±20% of the uncapped figure.
16. **Nothing was written.** After a session, confirm the tax sliders, dev-tree points and milestone
    level are exactly where the player left them. Every system lane 1b touches has a writer sitting
    beside the reader it calls; this is the §7 FORBIDDEN check.

### Still outstanding from wave 0

Wave 0's five gates (the double-tick, the clamp not firing on an ordinary reload, the rewound load,
retheme, and the trend window surviving a reload) **have still not been walked.** They are recorded
in `docs/plans/0004-wave-0-handoff.md` and `docs/status.md`.

---

## Known gaps, recorded rather than closed

- **The wave-0 golden rehydration test does not exercise the new material.** It stayed green through
  this wave, and that is explainable rather than lucky: widening only adds series, `IndicesEngine`
  reads none of them off a historical snapshot, and the frozen `SyntheticCityHistory` leaves the new
  fields at their defaults, so both sides of its comparison move identically. Making it exercise v4
  needs `SyntheticCityHistory` widened, which was frozen for this wave. Lane 1d flagged it rather
  than manufacturing a change to it.
- **`Households` is arguably a sum too**, and still falls back to the city value. It sits outside
  ruling 9 as written. Flagged by review, not changed — a decision for whoever next opens that rule.
- **`HomelessnessRate / 100.0` has no finite guard** in lane 1a. Benign today: lane 1e's clamp maps a
  NaN to the "no visible homelessness" band, and an infinity would need the game's own rate to be
  infinite. On record against 1a's file.
- **`sensors.maxBuildingsPerCapture` now means two different sampling fractions** — it is documented
  as a ceiling on *residential* buildings and lane 1a applies the same absolute number to the
  garbage-producer set. Worth a line in the calibration doc eventually.
- **Sensors need no `Mod.cs` registration.** Only `AgoraSnapshotSystem` is registered; every other
  sensor is created by `World.GetOrCreateSystemManaged<T>()` inside its `CreateQueries`. `Mod.cs` was
  frozen this wave and did not need to move. Do not add registration lines for the three new systems.

## Verification recorded

- `dotnet build Agora.sln` — **0 warnings, 0 errors**, toolchain mode. This matters more than usual
  here: the three new sensors are `partial` classes that need the Unity.Entities source generators,
  which run **only** in toolchain mode, so the lanes' fallback-mode builds could not have shown it.
  **This build deploys** to the player's live `…\Mods\Agora.Mod`; it was run once, at the end.
- `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj` — **1469 passed, 0 failed**
  (from 1442; **+27**).
- `cd ui && npx tsc --noEmit` — clean. No `ui/` file changed this wave.
- **No sidecar or binding schema version moved.** Only `snapshot` 3 → 4, which has no migration table
  by design; its two sides are pinned to each other by a version-relative test rather than by a
  memorised literal.

## PR link

https://github.com/SeraphinHesse/Cs2CompanionApp/pull/4 — open, awaiting the owner. Wave 2 must not
cut its umbrella until this is merged into `EventSystemRefresh`.
