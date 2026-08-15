# Wave 1 — lane ownership

Umbrella: `event-system/wave-1`, cut from `EventSystemRefresh` at `dc9b0a9`.
Spine: `573d675` — `wave-1 spine`.
Base measured before cutting: `dotnet build Agora.sln` 0 errors, **1442 tests passed, 0 failed**.

**The one law.** Every file more than one lane would touch was landed in the spine, before any
worktree existed. Lanes own strictly disjoint paths. **A merge conflict in this wave is a bug in
this table, not something to resolve by hand.**

---

## What the spine already settled

Do not re-litigate these, and do not re-derive them from the rework plan — the plan was written
before most of this code was read, and `docs/scout/0004-city-statistics.md` outranks it.

| Question | Settled |
|---|---|
| The API to read statistics through | `CityStatisticsSystem.GetStatisticValueLong(StatisticType, int parameter = 0)`. Public, instance, completes its own writer jobs. **Already in production use** in `AgoraMobilitySensorSystem.cs:65-73` — copy that, do not invent an access pattern. |
| `CitySnapshot` shape | Landed at v4 in the spine. **No lane edits `CitySnapshot.cs`, `snapshot.schema.json` or `SensorReadings.cs`.** |
| Whether `metric_history.json` bumps | **No.** It is a keyed series bag: new metrics are new keys, not a new shape. `SidecarSchema.cs:587` warns that bumping the constant without a 1→2 step turns every existing history into `NoPathForward`, i.e. silently discards it. |
| Whether the snapshot bump needs a migration | **No.** `CitySnapshot` is not a sidecar document — `SidecarDocument` has five members and none is the snapshot. `/schema-change` steps 1, 3, 4 apply; step 2 has nothing to act on. |
| Whether `Mod.cs` needs a registration line per sensor | **No.** Sensors other than `AgoraSnapshotSystem` are never registered — they are created by `World.GetOrCreateSystemManaged<T>()` inside `AgoraSnapshotSystem.CreateQueries()`, and only `AgoraSnapshotSystem` is registered at `GameSimulation`. `Mod.cs` is frozen this wave. |
| Landmark count | **Struck.** No landmark concept exists in `Game.dll`. `SignatureBuildingCount` replaces it. |
| Garbage naming | Production rate and uncollected stockpile are two different numbers and the contract carries both under names that say which is which. |

---

## Lanes

Branch and worktree names follow wave 0: `event-system/w1-<lane>` in `.claude/worktrees/w1-<lane>`.

### 1a — the city-statistics sensor

| | |
|---|---|
| **Branch** | `event-system/w1-1a` |
| **Worktree** | `.claude/worktrees/w1-1a` |
| **Owns, exclusively** | `src/Agora.Mod/Sensors/AgoraStatisticsSensorSystem.cs` |

Fill in the spine stub: homelessness, migration, births, deaths, the garbage production rate, and
uncollected garbage — the last both city-wide and per district.

Acceptance: every field of `CityReading.Statistics` populated from the sources named in scout 0004
§3, §4 and §7.1; `UncollectedGarbage` summed city-wide and per district; queries built in
`CreateQueries`; the collection-type log described below emitted once.

**The homeless share is a fraction, and the game hands you a percentage.**
`CountHouseholdDataSystem.HomelessnessRate` returns 0–100. The contract's `HomelessShare` is 0–1, on
the same convention that already makes `TaxRates` fractions. Get this wrong and every homelessness
threshold wave 3 authors fires at a hundred times the intended level.

**A zero you cannot explain is still a zero — report it.** `GetStatisticValueLong` returns `0` for a
genuine zero, for a statistic that is locked behind progression, and for a key that does not exist,
with no way to tell them apart (scout 0004 §1.7, Q1). **Do not invent an "unmeasurable" signal from
a zero**, and do not leave a field null because a number looked implausible. Leave a reading null
only when the whole source is unavailable — that is what null means here, and assembly turns it into
the city fallback rather than into a fabricated measurement. Wave 2 has a real `Unmeasurable` state
for this and it must not be built on a guess made here.

**Log the collection types once, on the first sample.** Build one
`EntityQuery(ComponentType.ReadOnly<StatisticsData>())` in `CreateQueries` and, on the first sample
only, log each `(m_StatisticType, m_CollectionType, m_UnitType)` triple. `StatisticCollectionType` is
set in prefab asset data, so it is not greppable and nobody knows today whether `BirthRate` means
"births in the last day" or "births ever" — and wave 3 cannot author a threshold against a number
whose period is unknown. This is the cheapest possible way to convert that from a guess into a fact
on the player's own machine. Scout 0004 §1.3 has the exact struct.

Gate the household reads on `CountHouseholdDataSystem.IsCountDataNotReady()`.

For uncollected garbage, sum `Game.Buildings.GarbageProducer.m_Garbage`, grouping by
`Game.Areas.CurrentDistrict` for the district figures. You may **read** `DistrictIdentityMap` to map a
district entity to Agora's stable id; you may not edit it. Respect
`Calibration.MaxBuildingsPerCapture` the way the existing building walks do.

### 1b — the progression sensor

| | |
|---|---|
| **Branch** | `event-system/w1-1b` |
| **Worktree** | `.claude/worktrees/w1-1b` |
| **Owns, exclusively** | `src/Agora.Mod/Sensors/AgoraProgressionSensorSystem.cs` |

Milestone level, experience, milestone progress, unlocked feature ids, and the per-resource tax rates.

Acceptance: `CityReading.Progression`, `UnlockedFeatureIds` and `IndustryTaxRates` populated per scout
0004 §6 and §8. This system has **no** `Districts` property and must not grow one — a district has no
milestone, and `TaxSystem` exposes no per-district per-resource overload.

**An unlock is disabled, not removed.** `Game.Prefabs.Locked` is an `IEnableableComponent`, and
`UnlockSystem` unlocks by calling `SetComponentEnabled<Locked>(entity, false)`. So the test for "this
is unlocked" is about whether the component is *enabled*, not whether it is *present* —
`HasComponent<Locked>` answers the wrong question and would report every feature in the game as
locked forever. Both `UnlockSystem.cs:226-229` and `StatisticsUISystem.cs:397` show the right form.

**Guard the singleton the way the game does.** `MilestoneUISystem.cs:376-380` checks
`IsEmptyIgnoreFilter` before `GetSingleton<MilestoneLevel>()` — the game itself does not assume the
singleton exists, and neither may we.

**Sort before you hand anything over.** Feature names sort ordinal ascending; tax rates sort by
`(Area, ResourceIndex)`. Assembly sorts too, but a lane that hands over collection order is relying
on a sort it cannot see. Key resources by `EconomyUtils.GetResourceIndex`, never by the `Resource`
flag value, which is a bitfield up to `1 << 40`.

**Read-only, absolutely.** `TaxSystem` has matching `Set…` methods, `DevTreeSystem` has a `points`
setter and a `Purchase`, and `MilestoneSystem` has `UnlockAllMilestones()`. Writing any of them is
"targeting the player's authority" under §7's FORBIDDEN list. Sensors read.

Do not edit `AgoraEconomySensorSystem.cs`. It already reads the four `TaxAreaType` rates and it is
not yours; two systems resolving `TaxSystem` independently is fine and costs nothing.

### 1c — the tourism sensor

| | |
|---|---|
| **Branch** | `event-system/w1-1c` |
| **Worktree** | `.claude/worktrees/w1-1c` |
| **Owns, exclusively** | `src/Agora.Mod/Sensors/AgoraTourismSensorSystem.cs` |

Tourists, attractiveness, lodging, and the attraction and signature-building counts — the last two
city-wide and per district.

Acceptance: `CityReading.Tourism`, `AttractionCount`, `SignatureBuildingCount` and the district
readings populated per scout 0004 §5.

**Attractiveness is stored raw and must stay raw.** It is a dimensionless index, not a percentage,
and it is the exact quantity the shipped `city-attractiveness` effect moves — which is what makes
trigger and effect two ends of one number. Normalising it against an invented reference maximum
would break that quietly. Read it off `CitySystem.City` with `TryGetComponent<Tourism>`, exactly as
`TourismInfoviewUISystem.cs:158` does.

**Both exclusions or the counts are wrong.** Copy `AttractionSystem.cs:241`: the query carries
`Exclude<Temp>` *and* `Exclude<Deleted>`. Without them the count includes buildings the player is
still dragging out and buildings already demolished, so it moves for reasons that are not events.

**Emit no landmark count.** There is no landmark concept in the game. The field is
`SignatureBuildingCount` and it counts the `Game.Buildings.Signature` tag.

The per-district attraction figures are honest counts of real entities, but they are **not** the
game's city `Attractiveness` number and must never be labelled as it.

### 1d — the merge, assembly, history and rehydration half

| | |
|---|---|
| **Branch** | `event-system/w1-1d` |
| **Worktree** | `.claude/worktrees/w1-1d` |
| **Owns, exclusively** | `src/Agora.Mod/Sensors/SensorMerge.cs`, `src/Agora.Mod/Sensors/SnapshotAssembly.cs`, `src/Agora.Mod/Sensors/MetricHistory.cs`, `src/Agora.Mod/Sensors/SnapshotRehydration.cs`, `src/Agora.Mod/Sensors/AgoraSnapshotSystem.cs`, `tests/Agora.Core.Tests/SnapshotRehydrationTests.cs`, `tests/Agora.Core.Tests/MetricHistoryPersistenceTests.cs` |

The pure half, and the one lane that is fully testable. Five jobs:

1. **Merge** the new `CityReading` and `DistrictReading` fields in `SensorMerge`, on the existing
   first-source-wins rule.
2. **Assemble** them in `SnapshotAssembly`, including the three per-district fields through the
   existing `Resolve` path so an unmeasured one is marked in `CityFallbackFields`, and the two new
   sorted lists. Add the three new field-name constants alongside the existing ones — they are
   written out rather than reflected on purpose, and the file says why.
3. **Record** the new scalars in `MetricHistory` (vocabulary below).
4. **Rehydrate** them in `SnapshotRehydration`.
5. **Wire** the three new sensors into `AgoraSnapshotSystem`: resolve them in `CreateQueries`,
   `EnsureSampled` them in `Sample`, add them to the city and district source lists, and add them to
   `Invalidate`. **`Invalidate` is not optional** — a sensor left out of it carries one city's
   readings into the next, which is the W0 per-save-reset bug class.

**The invariant that governs jobs 3 and 4, and it is the whole reason they are in this lane.**
`SnapshotRehydration` writes a field only when its series holds a sample for that month, and leaves
every other field at the contract default — where a `0` is indistinguishable from a measurement. So
*the set of metrics recorded in `MetricHistory` is exactly the set anything may trust off a
historical snapshot.* Wave 3 will author `delta` and `windowMonths` triggers against these fields; a
field that is on the snapshot but not in the history would read as a fabricated zero for every month
before the current session, which is precisely the desync wave 0 existed to close. **Record and
rehydrate the same set, and let the two go out of step for nothing.**

The wave-0 golden test in `SnapshotRehydrationTests.cs` is designed to fail when the recorded set
widens — its own comment says so. Updating it is your job, not a signal that something is wrong.

**Metric vocabulary — a contract, not an implementation detail.** Wave 2's trigger registry names
these strings and the sidecar fingerprint is taken over them sorted, so a name may be added but never
renamed without a migration, on the same rule that governs a seed stream name.

City scope (18): `homeless` · `homelessShare` · `citizensMovedIn` · `citizensMovedAway` ·
`movedAwayUnhappy` · `births` · `deaths` · `garbageProductionRate` · `tourists` · `attractiveness` ·
`lodgingUsed` · `lodgingTotal` · `milestoneLevel` · `experience` · `milestoneProgress` ·
`uncollectedGarbage` · `attractionCount` · `signatureBuildingCount`

District scope (3): `uncollectedGarbage` · `attractionCount` · `signatureBuildingCount`

**Not recorded, deliberately:** `UnlockedFeatureIds` and `IndustryTaxRates`. They are lists, not
scalars, and `MetricHistory` stores one `double` per series per month. A trigger may ask what is
unlocked *today*; there is no historical series behind either, and so no honest `delta` read. Say so
in a comment where the vocabulary is declared, so wave 2 does not go looking for a series that was
never a decision to omit.

### 1e — the prompt sync and the contract proof

| | |
|---|---|
| **Branch** | `event-system/w1-1e` |
| **Worktree** | `.claude/worktrees/w1-1e` |
| **Owns, exclusively** | `src/Agora.Mod/Llm/FlavorPromptBuilder.cs`, `tests/Agora.Core.Tests/FlavorPromptBuilderTests.cs`, `tests/Agora.Core.Tests/CityStatisticsContractTests.cs` (new) |

`/schema-change` step 3: a snapshot field the LLM cannot see is a contract break, because the model
then writes prose about a city it has no view of.

**The prompt describes the city in words, never in figures** — `FlavorPromptBuilder`'s own opening
remark, and `NumericFieldScanner` enforces the other direction of the same rule. So the new material
arrives as bands: homelessness, whether people are arriving or leaving and whether unhappiness is why,
tourism pressure, and how far through the milestone track the city is. Add band helpers in the style
of the existing `HappinessBand` / `CoverageBand` / `RentBurdenBand`. Do not print a count.

Judgement is yours on which of the eighteen new numbers earn a line — the prompt has a cap and the
sections it must never cut are the ones that fail invisibly. Prefer a few bands that change month to
month over an exhaustive dump. `IndustryTaxRates` and `UnlockedFeatureIds` almost certainly do not
belong in the prompt at all; say so in a comment if you leave them out, so the omission reads as a
decision.

`CityStatisticsContractTests.cs` proves the contract itself: that the three per-district fields
appear in `CityFallbackFields` when unmeasured and not when measured, that the two lists come out
sorted, and that a default `CitySnapshot` reports zeros rather than throwing. Prefer synthetic
fixtures — `tests/CLAUDE.md` says so and the reason is that they do not rot when the schema gains a
field.

---

## Path disjointness — checked before any worktree was created

Every path below appears in exactly one row above.

```
1a  Sensors/AgoraStatisticsSensorSystem.cs
1b  Sensors/AgoraProgressionSensorSystem.cs
1c  Sensors/AgoraTourismSensorSystem.cs
1d  Sensors/SensorMerge.cs · SnapshotAssembly.cs · MetricHistory.cs ·
    SnapshotRehydration.cs · AgoraSnapshotSystem.cs
    tests/SnapshotRehydrationTests.cs · tests/MetricHistoryPersistenceTests.cs
1e  Llm/FlavorPromptBuilder.cs
    tests/FlavorPromptBuilderTests.cs · tests/CityStatisticsContractTests.cs (new)
```

**Frozen — no lane may edit these.** Shared, or already settled by the spine:
`src/Agora.Core/Contracts/CitySnapshot.cs` · `data/schemas/snapshot.schema.json` ·
`src/Agora.Mod/Sensors/SensorReadings.cs` · `SensorCalibration.cs` · `SensorMath.cs` ·
`DistrictIdentityMap.cs` · `AgoraDistrictSensorSystem.cs` · `AgoraEconomySensorSystem.cs` ·
`AgoraEnvironmentSensorSystem.cs` · `AgoraResidentsSensorSystem.cs` ·
`AgoraServiceCoverageSensorSystem.cs` · `AgoraMobilitySensorSystem.cs` ·
`AgoraSensorSystemBase.cs` · `src/Agora.Mod/Mod.cs` ·
`tests/Agora.Core.Tests/Agora.Core.Tests.csproj` · `tests/Agora.Core.Tests/SyntheticCityHistory.cs` ·
`tests/Agora.Core.Tests/HouseholdBudgetTests.cs`

A lane that believes it needs one of these should stop and say so rather than edit it. That is a bug
in this table, and moving the file into the spine is cheap; a hand-resolved conflict later is not.

New test files are picked up by SDK globbing — **the test csproj needs no edit**, and the three new
sensor systems must never be added to its `<Compile Link>` list: they name `Game.*` types, and that
list is what keeps the suite runnable with no copy of the game installed.

---

## Seam signatures — both ends published

Landed in the spine at `573d675`, so every lane compiles from commit one.

Written by 1a / 1b / 1c, read by 1d, on `Agora.Mod.Sensors.CityReading`:

```csharp
public CityStatistics?    Statistics;              // 1a
public double?            UncollectedGarbage;      // 1a
public TourismLevels?     Tourism;                 // 1c
public int?               AttractionCount;         // 1c
public int?               SignatureBuildingCount;  // 1c
public ProgressionState?  Progression;             // 1b
public List<string>       UnlockedFeatureIds;      // 1b
public List<ResourceTaxRate> IndustryTaxRates;     // 1b
```

On `DistrictReading` — the three the game genuinely resolves per district:

```csharp
public double? UncollectedGarbage;      // 1a
public int?    AttractionCount;         // 1c
public int?    SignatureBuildingCount;  // 1c
```

The sensor systems 1d wires up:

```csharp
public sealed partial class AgoraStatisticsSensorSystem : AgoraSensorSystemBase {
    public CityReading City { get; }
    public IReadOnlyDictionary<string, DistrictReading> Districts { get; }
}
public sealed partial class AgoraProgressionSensorSystem : AgoraSensorSystemBase {
    public CityReading City { get; }          // no Districts, by design
}
public sealed partial class AgoraTourismSensorSystem : AgoraSensorSystemBase {
    public CityReading City { get; }
    public IReadOnlyDictionary<string, DistrictReading> Districts { get; }
}
```

Contract types are in `Agora.Core.Contracts` (`CitySnapshot.cs`): `CityStatistics`, `TourismLevels`,
`ProgressionState`, `ResourceTaxRate`, `TaxArea`. All `readonly struct`s with positional constructors,
matching the file's existing style.

---

## What each lane must NOT test

`AgoraRuntime` and `src/Agora.Mod/UiBindings/**` are not the only unlinkable code. **Every
`GameSystemBase` is unlinkable**, which is the whole of lanes 1a, 1b and 1c.

- **1a, 1b, 1c write no tests.** Their files name `Game.*` types and cannot be compiled into
  `Agora.Core.Tests` — that is by design, not an omission, and it is the property that keeps the
  suite runnable without the game. **Faking the runtime to manufacture coverage is itself a
  review-blocking defect.** Verify with
  `dotnet build src/Agora.Mod/Agora.Mod.csproj -p:UseCsiiToolchain=false`, reason about the
  arithmetic in your report, and **propose the manual gate rows** your work needs — specific enough
  to fail, e.g. "confirm `Agora.log` reports a homeless share between 0 and 1 on a save with visible
  homelessness, **not** a figure in the tens", not "confirm homelessness works".
- **1d and 1e are fully testable** and are where this wave's coverage comes from. A test count that
  does not rise this wave means 1d or 1e under-delivered.

---

## Build and verification, per lane

```
dotnet build src/Agora.Mod/Agora.Mod.csproj -p:UseCsiiToolchain=false
dotnet test  tests/Agora.Core.Tests/Agora.Core.Tests.csproj
```

**Never `npm run build`, and never a bare `dotnet build Agora.sln`** — both deploy into the player's
live `…\Mods\Agora.Mod` once `node_modules` exists, and it does exist in the main worktree. Only the
orchestrator runs a deploying build, once, at the end.

**Never `dotnet test Agora.sln`** — it pulls in `Agora.Mod`, which needs the game installed.

No lane touches `ui/`, so no worktree needs `npm install` this wave and `npx tsc --noEmit` is not an
obligation for any lane.

`refsrc/` does not exist inside a worktree — it is gitignored and lives only at
`C:\Users\serap\OneDrive\Documents\GitHub\Cs2CompanionApp\refsrc`. Pass that absolute path, and
**grep it, never read it in full**. A lane that greps `./refsrc` locally gets zero hits and quietly
concludes the API is absent. Most of what the lanes need is already quoted in
`docs/scout/0004-city-statistics.md` with file and line numbers, so reach for the report first.

---

## Merge order

1. **1d** first — it is the only lane the others' output flows through, and it is the one whose tests
   prove the wave. It compiles against the spine stubs, so it stands alone.
2. **1a, 1b, 1c** in any order; they share no file and no seam with each other, so whichever is
   reviewed first merges first. Say so in the merge commit rather than idling.
3. **1e** last, because its band choices read better against the finished prompt input, and its
   contract tests are cheapest to confirm once assembly is real.

On a conflict: stop, fix this table, re-cut the affected lane. **Do not hand-resolve** — a
hand-resolved conflict silently erases one lane's half of a shared file.

---

## Carried into the handoff

Findings from this wave that later waves must not have to rediscover:

- Scout 0004 §10 lists six plan assumptions that are wrong. Wave 3 authors content against the metric
  registry, and two of them will cost it directly: **birth rate is readable** (only *modifying* it is
  impossible, which is what scout 0001 §3 actually said), and **there is no landmark count**.
- Scout 0004 Q1 — zero versus absent — is **unresolved** and wave 2's `CheckResult.Unmeasurable` must
  not be built on an assumption about it.
- Scout 0004 Q2 — the collection type of each statistic — is answered by the log lane 1a emits. **The
  answer must be recorded in the wave-1 handoff before wave 3 authors a threshold against any of
  these numbers**, because it decides whether `births` means "this month" or "ever".
- `docs/status.md`'s "Known gaps" entry recording birth rate as unreachable is now misleading and
  should be corrected when the status doc is updated at the end of the wave.
