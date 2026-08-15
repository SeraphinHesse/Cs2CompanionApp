# Scout Report 0004 — City Statistics for the Event System Rework

**Date:** 2026-08-15
**Commissioned by:** `docs/plans/0004-event-system-rework.md` § "Wave 1 — Sensors and city statistics", spine item 1.
**Method:**
1. Metadata enumeration of the shipped assemblies via `Colossal.Mono.Cecil.dll` (public/static/signature/arity/enum values). Assembly root
   `C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II\Cities2_Data\Managed\`.
2. Targeted `grep` of the decompiled tree at `C:\Users\serap\OneDrive\Documents\GitHub\Cs2CompanionApp\refsrc`
   (the **main checkout** — `refsrc/` does not exist inside a worktree), followed by narrow reads of
   the located files, for method bodies, units and real call sites.
3. Cross-check against the shipped Agora sensors in `src/Agora.Mod/Sensors/`.

**Every name below was read out of the shipped DLLs or the decompiled bodies.** Nothing here comes
from community documentation.

---

## 0. Verdict summary

| # | Item | Verdict |
|---|---|---|
| 1 | Homelessness (count + share) | **CONFIRMED** — two independent sources |
| 2a | Migration in/out | **CONFIRMED** |
| 2b | Birth rate / death rate | **CONFIRMED** — *this overturns scout 0001 §3 and `docs/status.md`* |
| 3a | Tourist count | **CONFIRMED** |
| 3b | `Attractiveness` | **CONFIRMED** (city-only) |
| 3c | Landmark count | **UNREACHABLE as named** — no `Landmark` concept in code |
| 3d | Attraction / signature-building counts | **CONFIRMED via own `EntityQuery`** (no game-side counter) |
| 4a | City level (milestone) | **CONFIRMED** |
| 4b | XP / experience | **CONFIRMED** |
| 4c | Milestone index + progress | **CONFIRMED** |
| 4d | Unlocked feature ids | **CONFIRMED** — identified by prefab **name string** |
| 5 | Per-resource / per-industry tax rates | **CONFIRMED** — stable sortable int key |
| 6 | Garbage — production rate | **CONFIRMED** |
| 6b | Garbage — stored/uncollected stockpile | **PROXY, own query required** |

Two verdicts to read carefully before Wave 3 authors anything:

- **Every `StatisticType` value is city-only.** There is no district dimension in the statistics system
  at all (§1.4). Everything sourced from `CityStatisticsSystem` must be declared a
  `CityFallbackFields` value on `DistrictSnapshot`.
- **`GarbageAccumulation` is a production *rate per day*, not a stockpile** (§7). The plan's field name
  `CityStatistics.GarbageAccumulation` will read as "how much garbage the city makes", not "how much
  is piling up". Those are different events.

---

## 1. The anchor: `CityStatisticsSystem.GetStatisticValueLong`

### 1.1 Signatures — confirmed, all `public`

`Game.Simulation.CityStatisticsSystem : GameSystemBase, ICityStatisticsSystem, IDefaultSerializable,
ISerializable, IPostDeserialize` — the class itself is `public`, non-abstract.

```csharp
// instance, the one to use
public long GetStatisticValueLong(StatisticType type, int parameter = 0);
public int  GetStatisticValue    (StatisticType type, int parameter = 0);

// instance, caller supplies the buffer lookup (for use inside a job)
public long GetStatisticValueLong(BufferLookup<CityStatistic> stats, StatisticType type, int parameter = 0);
public int  GetStatisticValue    (BufferLookup<CityStatistic> stats, StatisticType type, int parameter = 0);

// static, fully job-safe
public static long GetStatisticValueLong(
    NativeParallelHashMap<CityStatisticsSystem.StatisticsKey, Entity> statisticsLookup,
    BufferLookup<CityStatistic> stats, StatisticType type, int parameter = 0);

// whole series, newest last
public NativeArray<long> GetStatisticDataArrayLong(StatisticType type, int parameter = 0);
public NativeArray<int>  GetStatisticDataArray    (StatisticType type, int parameter = 0);
public NativeArray<int>  GetStatisticArray        (StatisticType type, int parameter = 0);

public int sampleCount { get; }
public uint GetSampleFrameIndex(int index);
public NativeParallelHashMap<StatisticsKey, Entity> GetLookup();
public void CompleteWriters();
public const int kUpdatesPerDay = 32;
public Action eventStatisticsUpdated { get; set; }
```

`parameter` is a plain `int`, default `0`. It is **not** a resource and **not** a sub-type enum — see
§1.5 for what it keys.

### 1.2 It is safe to call from a managed system

`refsrc/Game/Game.Simulation/CityStatisticsSystem.cs:559-572`:

```csharp
private double GetStatisticValueDouble(StatisticType type, int parameter = 0)
{
    StatisticsKey key = new StatisticsKey(type, parameter);
    m_Writers.Complete();                       // <-- syncs the writing jobs for us
    if (m_StatisticsLookup.ContainsKey(key)) { ... return Math.Round(buffer[buffer.Length - 1].m_TotalValue, MidpointRounding.AwayFromZero); }
    return 0.0;
}
```

The instance overload completes the writer jobs itself. **A missing key returns `0.0`, it does not
throw** — which means *a statistic that does not exist and a statistic that is genuinely zero are
indistinguishable*. See open question Q1.

### 1.3 Units and scaling of the returned `long`

The returned value is `Math.Round(buffer[last].m_TotalValue, MidpointRounding.AwayFromZero)`, clamped
to `long` range. **No unit conversion, no percentage scaling, no division.** What `m_TotalValue` means
is decided per statistic by the prefab's `StatisticsData.m_CollectionType`
(`Game.City.StatisticCollectionType`, three members, `refsrc/Game/Game.City/StatisticCollectionType.cs`):

| `m_CollectionType` | What `m_TotalValue` is (from `ResetEntityJob.Execute`, `CityStatisticsSystem.cs:308-336`) |
|---|---|
| `Daily` (0) | rolling sum of the **last 32 samples** — i.e. the last in-game day, since `kUpdatesPerDay = 32` |
| `Point` (1) | the value accumulated during the **last single sample** (1/32 day) — an instantaneous reading |
| `Cumulative` (2) | running total since the city started, never reset |

`Game.City.StatisticUnitType` — `None = 0, Money = 1, Percent = 2, Weight = 3` — is *presentation
metadata only*; nothing in `GetStatisticValueLong` consults it.

**This is discoverable at runtime, not a guess.** Both fields live on `Game.Prefabs.StatisticsData`,
an `IComponentData` on every statistic prefab entity:

```csharp
public struct StatisticsData : IComponentData, IQueryTypeParameter {
    public Entity m_Category;  public Entity m_Group;
    public StatisticType m_StatisticType;
    public StatisticCollectionType m_CollectionType;
    public StatisticUnitType m_UnitType;
    public Color m_Color;  public bool m_Stacked;
}
```

A sensor can therefore build an `EntityQuery(ComponentType.ReadOnly<StatisticsData>())` once in
`OnCreate` and log the collection type of each statistic it reads, rather than assuming. **Wave 1a
should do exactly that once at first sample and log it** — it is cheap and it converts Q2 below from a
guess into a fact on the player's own machine.

### 1.4 There is no district dimension. At all.

`CityStatisticsSystem.StatisticsKey` (`CityStatisticsSystem.cs:24-62`) is:

```csharp
public readonly struct StatisticsKey : IEquatable<StatisticsKey> {
    public StatisticType type { get; }
    public int parameter { get; }
}
```

Two fields. No district, no area, no entity. `ls refsrc/Game/Game.Simulation | grep -i district`
returns nothing — **there is no district statistics system.** Consequence, and it is load-bearing for
the `CitySnapshot` v4 contract: *every* value sourced from `CityStatisticsSystem` is city-only and must
go on `DistrictSnapshot` through `HasCityFallbacks` / `CityFallbackFields`
(`src/Agora.Core/Contracts/CitySnapshot.cs:367-449`), or not go on `DistrictSnapshot` at all.

### 1.5 What `parameter` keys

`parameter` is a discriminator whose meaning is defined per statistic by the prefab's
`ParametricStatistic` subclass (`refsrc/Game/Game.Prefabs/ParametricStatistic.cs`), which publishes its
legal values into a `DynamicBuffer<StatisticParameterData>` on the prefab entity
(`public struct StatisticParameterData : IBufferElementData { public int m_Value; public Color m_Color; }`).

Confirmed subclasses and their keying:

| Prefab class | `parameter` is |
|---|---|
| `ResourceStatistic` | `EconomyUtils.GetResourceIndex(Resource)` — a stable int |
| `MoveAwayStatistic` | `(int)Game.Agents.MoveAwayReason` |
| `LevelStatistic` | the building level integer |
| `AgeStatistic`, `EducationStatistic`, `PassengerStatistic`, `IncomeStatistic`, `ExpenseStatistic`, `CityServiceStatistic` | their own enum values |

Hard-coded parameters are also visible directly in `CityStatisticsJob.Execute`
(`CityStatisticsSystem.cs:103-219`) — e.g. `StatisticType.Age` uses `0=Children 1=Teen 2=Adult
3=Senior`, `StatisticType.EducationCount` uses `0=Uneducated … 4=HighlyEducated`.

### 1.6 Real call sites — the best evidence of correct usage

`refsrc/Game/Game.UI.InGame/PopulationInfoviewUISystem.cs:78, 130-136` — this is the game's own
population infoview, and it is precisely the pattern a sensor should copy:

```csharp
// OnCreate
m_CityStatisticsSystem = base.World.GetOrCreateSystemManaged<CityStatisticsSystem>();
...
private void UpdateStatistics()
{
    m_BirthRate.Update(m_CityStatisticsSystem.GetStatisticValue(StatisticType.BirthRate));
    m_DeathRate.Update(m_CityStatisticsSystem.GetStatisticValue(StatisticType.DeathRate));
    m_MovedIn.Update(m_CityStatisticsSystem.GetStatisticValue(StatisticType.CitizensMovedIn));
    m_MovedAway.Update(m_CityStatisticsSystem.GetStatisticValue(StatisticType.CitizensMovedAway));
}
```

The **city statistics screen** itself is `refsrc/Game/Game.UI.InGame/StatisticsUISystem.cs`; it uses
`GetStatisticDataArrayLong((StatisticType)stat.statisticType, parameter)` (`:540, :559`) for the
series, and discovers every statistic by iterating prefab entities carrying `StatisticsData` (`:405-453`).

Agora already does this correctly in three shipped sensors —
`src/Agora.Mod/Sensors/AgoraEconomySensorSystem.cs:58, 96-97`,
`AgoraEnvironmentSensorSystem.cs:58, 178`, `AgoraMobilitySensorSystem.cs:36, 65-73`. **Wave 1a should
follow those files, not invent a new access pattern.**

### 1.7 A locked statistic reads as zero

`StatisticsUISystem.cs:397` — `bool locked = base.EntityManager.HasEnabledComponent<Locked>(entity2);`.
Statistic prefabs are progression-gated like any other prefab. A locked statistic's buffer is simply
never fed, so `GetStatisticValueLong` returns `0`. A trigger written against an early-game city must
therefore tolerate a legitimate zero. See Q1.

---

## 2. `Game.City.StatisticType` — complete enumeration

Read from `Game.dll` metadata. 63 members plus `Invalid`.

```
-1 Invalid          16 Health                33 PassengerCountTrain   50 CargoCountShip
 0 Population       17 WorkerCount           34 PassengerCountTaxi    51 CargoCountAirplane
 1 Money            18 Unemployed            35 PassengerCountAirplane 52 SeniorWorkerInDemandPercentage
 2 Income           19 EducationCount        36 PassengerCountShip    53 CrimeCount
 3 Expense          20 TouristCount          37 CrimeRate             54 EscapedArrestCount
 4 Trade            21 TouristIncome         38 CityServiceWorkers    55 AdultsCount
 5 HouseholdWealth  22 LodgingUsed           39 CityServiceMaxWorkers 56 Age
 6 HouseholdCount   23 LodgingTotal          40 OfficeWealth          57 WellbeingLevel
 7 ServiceWealth    24 DeathRate             41 OfficeCount           58 HealthLevel
 8 ServiceCount     25 CollectedMail         42 OfficeWorkers         59 HomelessCount
 9 ServiceWorkers   26 DeliveredMail         43 OfficeMaxWorkers      60 PassengerCountFerry
10 ServiceMaxWorkers 27 BirthRate            44 ResidentialTaxableIncome 61 MovedAwayReason
11 ProcessingWealth 28 CitizensMovedIn       45 CommercialTaxableIncome  62 Count
12 ProcessingCount  29 CitizensMovedAway     46 IndustrialTaxableIncome
13 ProcessingWorkers 30 PassengerCountBus    47 OfficeTaxableIncome
14 ProcessingMaxWorkers 31 PassengerCountSubway 48 CargoCountTruck
15 Wellbeing        32 PassengerCountTram    49 CargoCountTrain
```

`Count` (62) is the sentinel; `ProcessStatisticsJob` skips it explicitly (`CityStatisticsSystem.cs:237`).

---

## 3. Homelessness — **CONFIRMED**, two sources

**Source A — the statistics system (matches what the player sees).**

```csharp
long homeless   = statisticsSystem.GetStatisticValueLong(StatisticType.HomelessCount); // 59
long population = statisticsSystem.GetStatisticValueLong(StatisticType.Population);    // 0
```

Fed by `CityStatisticsJob.Execute` (`CityStatisticsSystem.cs:214-218`) from
`m_HouseholdData.m_HomelessCitizenCount`. Note `StatisticType.Population` is fed from
`HouseholdData.Population()`, not from `Game.City.Population` — use it for the share so numerator and
denominator come from the same sample.

**Source B — direct, and it hands you the share already computed.**
`Game.Simulation.CountHouseholdDataSystem`, all `public` instance properties:

```csharp
public int   HomelessCitizenCount   { get; }   // :880
public int   HomelessHouseholdCount { get; }
public int   MovedInCitizenCount    { get; }
public float HomelessnessRate       { get; }   // :967
public HouseholdData GetHouseholdCountData();
public bool  IsCountDataNotReady();
```

`HomelessnessRate` body (`CountHouseholdDataSystem.cs:967-976`):

```csharp
if (m_LastHouseholdCountData.m_MovedInCitizenCount == 0) return 0f;
return 100f * (float)m_LastHouseholdCountData.m_HomelessCitizenCount / (float)m_LastHouseholdCountData.m_MovedInCitizenCount;
```

**It is a percentage 0–100, not a fraction 0–1.** Agora's contract style is fractions
(`AgoraEconomySensorSystem.cs:129-136` divides tax rates by 100 for exactly this reason), so
`HomelessShare` must be `HomelessnessRate / 100.0`. Getting this wrong yields a trigger that fires at
100× the intended threshold.

`IsCountDataNotReady()` exists and should gate the read on the first frames after load.

**Per-district: city-only.** `Game.Citizens.HomelessHousehold` carries only `Entity m_TempHome`; a
homeless household has no `PropertyRenter` and therefore no building-derived district. A per-district
count could be derived by walking `HomelessHousehold.m_TempHome → CurrentDistrict`, but `m_TempHome` is
frequently `Entity.Null` and no game surface does this. **Recommend: `HomelessShare` is a
`CityFallbackFields` entry on `DistrictSnapshot`.**

---

## 4. Migration, births and deaths — **CONFIRMED**

This **overturns the standing verdict.** `docs/status.md` "Known gaps" and scout 0001 §3 record birth
rate as unreachable. That finding was about the **effect** side — scout 0001 §3 was enumerating
`CityModifierType`, and it is still true that **no modifier can change the birth rate.** But *reading*
it was never blocked, and `StatisticType.BirthRate` has been public the whole time.

| Metric | Call | Written by |
|---|---|---|
| Births | `GetStatisticValueLong(StatisticType.BirthRate)` (27) | `BirthSystem.cs:154-158`, `m_Change = 1f` per birth |
| Deaths | `GetStatisticValueLong(StatisticType.DeathRate)` (24) | `DeathCheckSystem.cs:486-490`, `m_Change = 1f` per death |
| In-migration | `GetStatisticValueLong(StatisticType.CitizensMovedIn)` (28) | `CitizenTravelPurposeSystem.cs:369-373`, `m_Change = householdCitizens.Length` |
| Out-migration | `GetStatisticValueLong(StatisticType.CitizensMovedAway)` (29) | `HouseholdMoveAwaySystem.cs:179-183`, `m_Change = buffer.Length` |

All four are **citizen counts, not per-mille rates**, despite "Rate" in the names. Whether the returned
long is "in the last day" or "since the city began" depends on the collection type — see Q2. The
`_Rate` naming plus the game UI showing them as a plain number strongly suggests `Daily`, but I did not
find that in code and **will not assert it**.

**Bonus, and directly useful to Wave 3 content:**

```csharp
long notHappy = statisticsSystem.GetStatisticValueLong(
    StatisticType.MovedAwayReason,                       // 61
    (int)Game.Agents.MoveAwayReason.NotHappy);
```

`Game.Agents.MoveAwayReason` (`refsrc/Game/Game.Agents/MoveAwayReason.cs`):
`None=0, NoSuitableProperty=1, NotHappy=2, NoAdults=3, NoMoney=4, TouristNoTarget=5, TouristNoHotel=6,
TouristNoMoney=7, TripNeedNotMovedIn=8, Count=9`. Keying confirmed by
`Game.Prefabs.MoveAwayStatistic.GetParameters()`, which yields `(int)m_MoveAwayReasons[i]`.
This lets an event distinguish "people are leaving because they are miserable" from "people are leaving
because there is nowhere to live" — a much better trigger than raw out-migration.

**Per-district: city-only** (§1.4). All five are `CityFallbackFields`.

---

## 5. Tourism

### 5.1 Tourist count — **CONFIRMED**

```csharp
long tourists = statisticsSystem.GetStatisticValueLong(StatisticType.TouristCount);   // 20
long lodgingUsed  = statisticsSystem.GetStatisticValueLong(StatisticType.LodgingUsed);  // 22
long lodgingTotal = statisticsSystem.GetStatisticValueLong(StatisticType.LodgingTotal); // 23
long touristIncome = statisticsSystem.GetStatisticValueLong(StatisticType.TouristIncome); // 21
```

Real call site: `refsrc/Game/Game.UI.InGame/TourismInfoviewUISystem.cs:166`.
Fed from `HouseholdData.m_TouristCitizenCount` and `Tourism.m_Lodging` (`CityStatisticsSystem.cs:110-129`).
Also available as `CountHouseholdDataSystem.TouristCitizenCount` (public `int`).

### 5.2 Attractiveness — **CONFIRMED, city-only**

`Game.City.Tourism` is an `IComponentData` on the **City entity**:

```csharp
public struct Tourism : IComponentData, IQueryTypeParameter, IDefaultSerializable, ISerializable {
    public int m_CurrentTourists;
    public int m_AverageTourists;
    public int m_Attractiveness;
    public int2 m_Lodging;          // x = used, y = total
}
```

Real call site — `TourismInfoviewUISystem.cs:158`:

```csharp
if (base.EntityManager.TryGetComponent<Tourism>(m_CitySystem.City, out var component)) { ... }
```

`m_CitySystem` is `Game.Simulation.CitySystem`, with `public Entity City { get; }` (also
`public int moneyAmount { get; }`, `public int XP { get; }`). Agora already resolves `CitySystem` in
`AgoraResidentsSensorSystem.cs:68`.

`m_Attractiveness` is computed in `TourismSystem.cs:95-107` as
`sum over AttractivenessProvider of (a*a)/10000`, then `CityUtils.ApplyModifier(ref num,
modifiers, CityModifierType.Attractiveness)`. So it is a **dimensionless index, not a percentage**, and
it *is* the value Agora's shipped `city-attractiveness` effect moves — which makes it a clean
trigger/effect pair.

**Per-district attractiveness: UNREACHABLE as a game value.** `Tourism` exists only on the City entity
(`RequiredComponentSystem.cs:567, :1303` adds it to `Game.City.City` entities and nothing else).
A per-district figure could be *invented* by summing `Game.Buildings.AttractivenessProvider.m_Attractiveness`
grouped by `CurrentDistrict`, but that would be Agora's number, not the game's, and it would not match
what the player sees. Treat as `CityFallbackFields`.

### 5.3 Landmark count — **UNREACHABLE as named**

`grep -rn "Landmark" refsrc/Game --include=*.cs` returns **two hits, both DLC plumbing**:
`Game.Dlc/Dlc.cs:11` (`public static readonly DlcId LandmarkBuildings`) and
`Game.Dlc/SteamworksDlcsMapping.cs:21`. There is no `Landmark` component, no `LandmarkData`, no
landmark count anywhere in the game code. **The plan's `TourismLevels.LandmarkCount` has no source.**
Either drop the field or redefine it as the signature-building count below — but do not ship a field
called `LandmarkCount` that silently means something else.

### 5.4 Attraction and signature-building counts — **CONFIRMED via own `EntityQuery`**

No game system exposes a count. The components exist and are queryable, so a sensor can count them
itself; this is not a proxy, it is a direct count of the right entities.

| Concept | Component on the **placed instance** | Prefab-side marker |
|---|---|---|
| Attraction (anything contributing attractiveness) | `Game.Buildings.AttractivenessProvider { public int m_Attractiveness; }` | `Game.Prefabs.AttractionData { public int m_Attractiveness; }` |
| Signature building | `Game.Buildings.Signature` (empty tag) | `Game.Prefabs.SignatureBuildingData`, `Game.Prefabs.PlacedSignatureBuildingData` (both empty tags) |

The game's own query, `AttractionSystem.cs:241`, is the pattern to copy — note both exclusions, they
are what keeps preview and demolished buildings out of the count:

```csharp
GetEntityQuery(ComponentType.ReadWrite<AttractivenessProvider>(),
               ComponentType.Exclude<Temp>(),
               ComponentType.Exclude<Deleted>());
```

**These two, uniquely in this report, ARE available per district** — the placed instances carry
`Game.Areas.CurrentDistrict`, which `AgoraDistrictSensorSystem` already uses. `AttractivenessProvider`
also carries the per-building attractiveness value, so a per-district attraction count *and* a
per-district raw attractiveness sum are both honest counts of real entities. They are still not the
game's city `Attractiveness` number (§5.2) and must not be labelled as such.

---

## 6. Progression — **CONFIRMED**

### 6.1 City level / milestone index

`Game.City.MilestoneLevel` — an `IComponentData` **singleton**:

```csharp
public struct MilestoneLevel : IComponentData, IQueryTypeParameter, ISerializable {
    public int m_AchievedMilestone;
}
```

Real call site — `refsrc/Game/Game.UI.InGame/MilestoneUISystem.cs:164, :376-380`:

```csharp
m_MilestoneLevelQuery = GetEntityQuery(ComponentType.ReadOnly<MilestoneLevel>());   // OnCreate
...
if (m_MilestoneLevelQuery.IsEmptyIgnoreFilter) return 0;
return m_MilestoneLevelQuery.GetSingleton<MilestoneLevel>().m_AchievedMilestone;
```

Note the `IsEmptyIgnoreFilter` guard — the game itself does not assume the singleton exists. Copy it.
`Game.PSI/RichPresenceUpdateSystem.cs:203-207` does the same. It is added to the City entity in
`CitySystem.cs:79`.

**In CS2 "city level" and "milestone" are the same number.** There is no separate level counter.

### 6.2 XP and milestone progress

`Game.Simulation.MilestoneSystem` (public `GameSystemBase`, implements `IMilestoneSystem`) — six public
properties, no arguments, no jobs:

```csharp
public int   currentXP      { get; }
public int   requiredXP     { get; }
public int   lastRequiredXP { get; }
public int   nextRequiredXP { get; }
public float progress       { get; }   // 0..1 within the current milestone
public int   nextMilestone  { get; }
public void  UnlockAllMilestones();     // exists; Agora must never call it
```

Also `Game.Simulation.CitySystem.XP` (public `int`), read from
`EntityManager.GetComponentData<XP>(m_City).m_XP` (`CitySystem.cs:50`). `Game.City.XP` additionally
carries `m_MaximumPopulation`, `m_MaximumIncome` and `XPRewardFlags m_XPRewardRecord` — the last is a
bitfield of one-off XP awards already granted, which is a usable "has this city ever done X" signal.

`Game.Prefabs.MilestoneData` on the milestone prefab gives `m_Index, m_Reward, m_DevTreePoints,
m_MapTiles, m_LoanLimit, m_XpRequried` *(sic, typo is in the shipped API)*, `m_Major`, `m_IsVictory`.

Development-tree points: `Game.City.DevTreeSystem` exposes `public int points { get; set; }` and
`public void Purchase(...)`. **The setter and `Purchase` are writes to the player's progression and are
out of scope** — read `points` only, if at all.

### 6.3 Unlocked feature ids — **CONFIRMED; the id is a prefab name string**

The unlock model, confirmed from `Game.Prefabs/UnlockSystem.cs`:

- `Game.Prefabs.Locked` is an **`IEnableableComponent`** empty tag on the *prefab* entity:
  `public struct Locked : IComponentData, IQueryTypeParameter, IEnableableComponent, IEmptySerializable {}`
- Unlocking **disables** rather than removes it — `UnlockSystem.cs:208`:
  `base.EntityManager.SetComponentEnabled<Locked>(unlock, value: false);`
- So the correct test is `HasEnabledComponent<Locked>` / `IsComponentEnabled<Locked>`, **not**
  `HasComponent<Locked>`. `UnlockSystem.cs:226-229` and `StatisticsUISystem.cs:397` both do it this way.
- `public bool UnlockSystem.IsLocked(PrefabBase prefab)` exists but takes a managed `PrefabBase`.
- An unlock also raises a one-frame event entity: archetype `(Event, Unlock)` where
  `Game.Prefabs.Unlock { public Entity m_Prefab; }` (`UnlockSystem.cs:117, :211`). That is a usable
  "just unlocked this tick" signal if a Wave 3 event wants one.

**Features specifically:** `Game.Prefabs.FeaturePrefab : PrefabBase` attaches
`Game.Prefabs.FeatureData` — an *empty* struct (`Size = 1`). So a feature carries **no id field of its
own**. The identity is the prefab, and the only stable, sortable, serializable id available is the
prefab **name**:

```csharp
public string Game.Prefabs.PrefabSystem.GetPrefabName(Entity entity);   // public, confirmed in metadata
```

Recommended shape for `ProgressionState.UnlockedFeatureIds`: query
`(FeatureData, PrefabData)`, keep entities where `Locked` is absent or disabled, map through
`PrefabSystem.GetPrefabName`, sort ordinal ascending. It is a `List<string>` and it is deterministic
once sorted.

**Answering the plan's question directly: an unlock is identified by neither an enum nor a hash. It is
a prefab entity, and its portable id is a name string.** There is no `FeatureType` enum.

---

## 7. Garbage

### 7.1 City-wide garbage production rate — **CONFIRMED**

`Game.Simulation.GarbageAccumulationSystem`, `refsrc/Game/Game.Simulation/GarbageAccumulationSystem.cs:351`:

```csharp
public long garbageAccumulation => m_Accumulation * kUpdatesPerDay;
```

Real call site — `refsrc/Game/Game.UI.InGame/GarbageInfoviewUISystem.cs:246`:

```csharp
AddBinding(m_GarbageRate = new GetterValueBinding<float>(
    "garbageInfo", "productionRate", () => m_GarbageAccumulationSystem.garbageAccumulation));
```

**Read the binding name.** The game labels this `productionRate`. It is *garbage produced per day*,
already scaled up from the per-frame accumulator by `kUpdatesPerDay`. It is **not** a stockpile and it
does not fall when collection improves — only when the city produces less.

There is no `StatisticType` for garbage. This is the only city-wide garbage number.

**Naming risk for the Wave 1 spine:** the plan's `CityStatistics.GarbageAccumulation` will be read by
every content author as "garbage piling up". **Recommend naming the field `GarbageProductionRate`.** A
mis-named metric here produces events whose prose contradicts the simulation, which is the exact
failure mode the plan's own §Wave 3 table exists to prevent.

### 7.2 Stored / uncollected garbage — **PROXY, own query required**

The infoview's "stored garbage" comes from a **private** `UpdateGarbageJob` writing into a private
`NativeArray<float> m_Results` (`GarbageInfoviewUISystem.cs:260-300`); `GetStoredGarbage()` and
`GetGarbageCapacity()` are private. **Not callable, and not patchable within this wave's no-Harmony
rule.**

Two reachable substitutes, both requiring Agora to write its own query:

- **Uncollected garbage at producers.** `Game.Buildings.GarbageProducer { public Entity
  m_CollectionRequest; public int m_Garbage; public GarbageProducerFlags m_Flags; public byte
  m_DispatchIndex; }`. Summing `m_Garbage` over buildings is "garbage waiting on the kerb", and because
  buildings carry `Game.Areas.CurrentDistrict` this is **the one garbage number available per
  district** — which is exactly where the plan wants block-to-block variance.
- **Landfill fill level.** Sum `Resource.Garbage` out of the `Game.Economy.Resources` buffer on garbage
  facilities. Reachable but multi-step; not recommended for Wave 1.

Verdict: **PROXY.** `GarbageProducer.m_Garbage` is not the same number the statistics screen shows, and
Wave 3 prose must say "uncollected", not "landfill".

---

## 8. Per-resource and per-industry tax rates — **CONFIRMED**

`Game.Simulation.TaxSystem` (public `GameSystemBase`, implements `ITaxSystem`). Agora already resolves
it in `AgoraEconomySensorSystem.cs:60` and reads the four area rates.

**Confirmed public readers** (there are matching `Set…` methods — Agora must not call them; writing the
player's tax sliders is explicitly rejected by the plan's own Wave 3 note):

```csharp
public int TaxRate { get; set; }                       // the flat city-wide base rate
public int GetTaxRate(TaxAreaType areaType);
public int GetResidentialTaxRate(int jobLevel);        // jobLevel 0..4
public int GetCommercialTaxRate(Resource resource);
public int GetIndustrialTaxRate(Resource resource);
public int GetOfficeTaxRate(Resource resource);
public int2 GetTaxRateRange(TaxAreaType areaType);
public int GetTaxRateEffect(TaxAreaType areaType, int taxRate);
public int GetModifiedTaxRate(TaxAreaType areaType, Entity district, BufferLookup<DistrictModifier> policies);
public NativeArray<int> GetTaxRates();                 // the raw 92-entry array
public TaxParameterData GetTaxParameterData();
public JobHandle Readers { get; }
public void AddReader(JobHandle);
```

`Game.Simulation.TaxAreaType : byte` — `None=0, Residential=1, Commercial=2, Industrial=3, Office=4`.
(Note the namespace: it is `Game.Simulation`, **not** `Game.City`, despite `Game.City.TaxRate` and
`Game.City.TaxRates` living next door. `AgoraEconomySensorSystem.cs:130-132` already carries a comment
about this exact name collision.)

**Units: whole percentage points.** `GetTaxRate(areaType, taxRates) => taxRates[0] + taxRates[(int)areaType]`
(`TaxSystem.cs:443-447`), and the shipped Agora sensor already divides by 100 to reach the contract's
fraction (`AgoraEconomySensorSystem.cs:129-136`). Keep that convention.

**How a resource is keyed, and whether the id is stable and sortable — yes.**
`Game.Economy.Resource : ulong` is a **flag** enum (`NoResource=0, Money=1, Grain=2, ConvenienceFood=4,
… Fish=1<<40, Last=1<<41, All=ulong.MaxValue`). The flag value itself is a poor key. The stable one is:

```csharp
public static int Game.Economy.EconomyUtils.GetResourceIndex(Resource resource);   // bit index, 0..40
public static Resource Game.Economy.EconomyUtils.GetResource(int index);           // inverse
public static string Game.Economy.EconomyUtils.GetName(Resource resource);         // stable name string
```

`GetResourceIndex` is exactly what the game uses for the internal tax array layout
(`TaxSystem.cs:312-313, :584-604`) — `[0]` base, `[1..4]` per `TaxAreaType`, `[5+jobLevel]`
residential, `[10 + resourceIndex]` commercial, `[51 + resourceIndex]` industrial *and* office,
92 entries total. **Sort `IndustryTaxRates` by resource index.** It is a small dense int, stable across
saves, and it is the game's own key.

To enumerate the resources legally taxable in an area — the game's own loop, `TaxSystem.cs:620-635`:

```csharp
ResourcePrefabs prefabs = m_ResourceSystem.GetPrefabs();
ResourceIterator iterator = ResourceIterator.GetIterator();   // Game.Economy, public struct
while (iterator.Next())
{
    Entity entity = prefabs[iterator.resource];
    if (EntityManager.TryGetComponent<TaxableResourceData>(entity, out var data) && data.Contains(areaType))
        { /* iterator.resource is taxable in areaType */ }
}
```

`Game.Prefabs.TaxableResourceData { public byte m_TaxAreas; public bool Contains(TaxAreaType); }` —
public. Industrial and office share the `51 + index` slot precisely because their taxable resource sets
are disjoint; `Contains` is what separates them. `EconomyUtils.IsOfficeResource(Resource)` and
`IsCommercialResource(Resource)` are also public static and give the same split more cheaply.

**"Office software subsidised while farming taxed" is fully expressible:**

```csharp
int software = taxSystem.GetOfficeTaxRate(Resource.Software);
int grain    = taxSystem.GetIndustrialTaxRate(Resource.Grain);
// also available: Livestock, Vegetables, Cotton, Fish, Wood, Oil, Ore, Coal, Stone …
```

**Per-district: partially available, and this is the one bright spot.**
`GetModifiedTaxRate(TaxAreaType, Entity district, BufferLookup<DistrictModifier>)` applies
`DistrictModifierType.LowCommercialTax` for a given district entity (`TaxSystem.cs:457-471`). So the
*area* rates are genuinely per-district. The **per-resource** rates are city-only — there is no
per-district, per-resource overload. `AgoraDistrictSensorSystem` already holds the district entities
needed for the area call.

---

## 9. Recommendations for lanes 1a / 1b / 1c

Matching the shipped pattern in `AgoraSensorSystemBase.cs` (queries in `OnCreate`, fail closed to
`_broken`, log once) and `AgoraResidentsSensorSystem.cs`:

**1a — `AgoraStatisticsSensorSystem`.** Resolve in `CreateQueries()`:
`CityStatisticsSystem`, `CountHouseholdDataSystem`, `GarbageAccumulationSystem`. Build one
`EntityQuery(ComponentType.ReadOnly<StatisticsData>())` and, on first sample only, log every
`(m_StatisticType, m_CollectionType, m_UnitType)` triple — that closes Q2 on the player's machine
rather than in a guess. Gate on `CountHouseholdDataSystem.IsCountDataNotReady()`.
Divide `HomelessnessRate` by 100. Name the garbage field for a **production rate**.

**1b — `AgoraProgressionSensorSystem`.** Resolve `MilestoneSystem`, `CitySystem`, `PrefabSystem`,
`TaxSystem`. Build `EntityQuery(ComponentType.ReadOnly<MilestoneLevel>())` and
`EntityQuery(ComponentType.ReadOnly<FeatureData>(), ComponentType.ReadOnly<PrefabData>())` in
`OnCreate`. Guard the singleton with `IsEmptyIgnoreFilter`. Test `Locked` with
`HasEnabledComponent`/`IsComponentEnabled`, never `HasComponent`. Sort feature name strings ordinal
ascending. For taxes, iterate `ResourceIterator` + `TaxableResourceData.Contains` and key by
`EconomyUtils.GetResourceIndex`.

**1c — `AgoraTourismSensorSystem`.** Resolve `CityStatisticsSystem` and `CitySystem`; read
`Tourism` off `CitySystem.City` with `TryGetComponent`, exactly as `TourismInfoviewUISystem:158` does.
Build the two counting queries with **both** `Exclude<Temp>` and `Exclude<Deleted>`, copying
`AttractionSystem.cs:241`. **Do not emit a `LandmarkCount` field** — see §5.3.

**Contract note for lane 1d.** Of everything in this report, only the attraction/signature counts and
uncollected `GarbageProducer.m_Garbage` are honestly per-district. Everything else —
homelessness, births, deaths, migration, tourists, attractiveness, garbage production rate, city level,
XP, features, per-resource taxes — is city-only and belongs in `CityFallbackFields` when mirrored onto
`DistrictSnapshot`. Per-district *area* tax rates are the one partial exception (§8).

---

## 10. Things the plan assumed that are not there

1. **`TourismLevels.LandmarkCount` has no source.** No `Landmark` component or count exists anywhere in
   `Game.dll`. (§5.3)
2. **`CityStatistics.GarbageAccumulation` names a stockpile but the only reachable value is a
   production rate per day.** (§7.1)
3. **No statistic is per-district.** The plan says "mirror the reachable subset onto `DistrictSnapshot`";
   the reachable subset for mirroring is nearly empty. (§1.4)
4. **`ProgressionState.UnlockedFeatureIds` cannot be an enum or a hash** — `FeatureData` is a zero-field
   tag, so the only id is `PrefabSystem.GetPrefabName(entity)`, a string. (§6.3)
5. **Wave 3's note that birth rate "remains unreachable (scout 0001 §3)" is half wrong.** Reading it is
   fully reachable; only *modifying* it is not. Wave 3's content rules should be corrected, or authors
   will skip a trigger that works. (§4)
6. **"City level" and "milestone index" are one number**, not two — `MilestoneLevel.m_AchievedMilestone`.
   The plan lists them as separate contract fields. (§6.1)

---

## 11. Open questions for the next scout

**Q1. Zero-versus-absent.** `GetStatisticValueLong` returns `0.0` for an unknown key, for a locked
statistic, and for a genuine zero, with no way to tell them apart. Wave 2's `CheckResult.Unmeasurable`
state exists precisely for this. Is `GetLookup().ContainsKey(new StatisticsKey(type, parameter))` a
sound "this statistic exists" probe? The lookup is `public` and the key struct is `public`, so it is
callable — but I did not confirm that a locked statistic is absent from the lookup rather than present
and empty. **Unresolved. Do not build the `Unmeasurable` path on an assumption here.**

**Q2. Collection type per statistic.** `Daily` / `Point` / `Cumulative` is set in prefab **asset data**,
not in code, so it is not greppable in `refsrc/`. It decides whether `BirthRate` means "births in the
last day" or "births ever". §1.3 gives the runtime discovery route (query `StatisticsData` and read
`m_CollectionType`). **Wave 1a must log this and the result must be recorded in the wave handoff before
Wave 3 authors a threshold against any of these.**

**Q3. Sample cadence versus the monthly tick.** `kUpdatesPerDay = 32` and one in-game day is one
calendar month (`SimClockMath.cs:14-20`). A `Point` statistic read at the month boundary is a 1/32-day
instantaneous reading; a `Daily` one is a full month's sum. Which side of a month boundary Agora's
sensor lands on may matter for the noisier statistics. Not investigated.

**Q4. Landfill fill level.** Whether summing `Resource.Garbage` from the `Game.Economy.Resources` buffer
on `GarbageFacilityData` entities reproduces the infoview's "stored garbage". Not attempted (§7.2).

**Q5. `XPRewardFlags`.** The bitfield on `Game.City.XP.m_XPRewardRecord` records one-off XP awards
already granted and would make an excellent "has this city ever done X" trigger. Members not
enumerated.

**Q6. `CityStatisticsSystem.eventStatisticsUpdated`.** A public `Action` raised at the end of each
sample (`CityStatisticsSystem.cs:481`). Could replace polling, but subscribing from a mod needs an
unsubscribe path proven against the `OnCreate`-throws hazard documented in
`AgoraSensorSystemBase.cs:55-70`. Not investigated.
