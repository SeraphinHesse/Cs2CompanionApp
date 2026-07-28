# Scout Report 0001 — API Index

**Date:** 2026-07-28
**Method:** Direct metadata inspection of shipped assemblies via `Colossal.Mono.Cecil` (no decompiler
required for type/member enumeration). Every name below was read out of the shipped DLLs, not from
documentation.
**Game build:** Unity `2022.3.71`, `Cities2.exe`
**Assembly root:** `C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II\Cities2_Data\Managed\`

---

## 1. Environment

| Item | Finding |
|---|---|
| Managed assemblies | 173 DLLs; `Game.dll` = 11.7 MB, **4398 types** |
| `Newtonsoft.Json.dll` | **Ships with the game** — no JSON package dependency needed |
| `0Harmony.dll` | **Absent** — Harmony must come from the toolchain or `Lib.Harmony` |
| Modding toolchain | Not installed at time of writing (all `CSII_*` env vars unset) |
| Unity Editor | Not required — assets/prefabs only, and Agora ships no art |

Largest namespaces in `Game.dll`: `Game.Prefabs` (1274), `Game.Simulation` (479), `Game.UI.InGame`
(224), `Game.Rendering` (155), `Game.Net` (148), `Game.UI.Widgets` (146), `Game.Buildings` (145).

---

## 2. Confirmed types

### Mod lifecycle
```
Game.Modding        IMod, ModSetting, ModManager
Game                GameSystemBase, UpdateSystem, SystemUpdatePhase, AutoSaveSystem, GameMode, Version
Game.SceneFlow      GameManager, SaveHelpers, LoadingScreen
Colossal            IDictionarySource            (localization source for the settings page)
```

### Save / load
```
Game.Serialization  IPreSerialize, IPreDeserialize, IPostDeserialize, LoadGameSystem,
                    DeserializationBarrier, PreDeserialize<T>, PostDeserialize<T>
```
`IPreSerialize` / `IPostDeserialize` are the hooks for writing a self-owned save GUID (see §5).

### Time and statistics
```
Game.Simulation     SimulationSystem, TimeSystem, ITimeSystem, ISimulationSystem,
                    CityStatisticsSystem, ICityStatisticsSystem, WealthStatisticsSystem,
                    CrimeStatisticsSystem, CitizenHappinessSystem, CityServiceStatisticsSystem,
                    CompanyEconomyStatisticSystem, WorkProviderStatisticsSystem
Game.Common         SystemOrder, TimeData, RandomSeed, PseudoRandomSeed
```
Note the game already owns `RandomSeed` / `PseudoRandomSeed`; Agora's determinism kernel uses its own
`SeedStreams` and must not be confused with these.

### Districts — **districts are real ECS entities**
```
Game.Areas          District, CurrentDistrict, CurrentDistrictSystem, BorderDistrict,
                    ServiceDistrict, ServiceDistrictSystem, DistrictModifier,
                    DistrictModifierType, DistrictOption, Area, AreaType, MapTile
```

### City-level
```
Game.City           City, CityStatistic, StatisticType, StatisticParameter, StatisticUnitType,
                    CityModifier, CityModifierType, CityOption, TaxRate, TaxRates, PlayerMoney,
                    Population, Tourism, ServiceFee, IncomeSource, ExpenseSource, CityServiceUpkeep
```

### Citizens — the voter-bloc substrate
```
Game.Citizens       Citizen, CitizenAge, CitizenEducationLevel, CitizenHappiness, CitizenFlags,
                    Household, HouseholdCitizen, HouseholdMember, HouseholdNeed, HouseholdFlags,
                    Worker, Student, Criminal, CrimeVictim, HealthProblem, CurrentBuilding,
                    CurrentTransport, TravelPurpose, CommuterHousehold, TouristHousehold
```
The plan's bloc model (wealth × education × age) maps directly: `CitizenAge` and
`CitizenEducationLevel` are enums on `Citizen`; wealth is a `Household` property. **Open:** confirm
the exact wealth field and whether `HouseholdCitizen` gives a reliable district association.

### UI
```
Game.UI             UISystemBase, UIUpdateSystem, UIUpdateState, NameSystem
Colossal.UI.Binding ValueBinding<T>, GetterValueBinding<T>, RawValueBinding,
                    TriggerBinding, TriggerBinding<T…>, CallBinding<T…>, EventBinding,
                    GetterMapBinding<K,V>, CompositeBinding, IBindingRegistry,
                    IJsonWritable, IJsonWriter, IJsonReadable, IJsonReader
```

---

## 3. The effect palette finding (highest impact)

The game exposes two first-class modifier enums, scoped exactly district and city. **`politicsmodplan.md`
§7 should be rebuilt on these rather than invented**, because they are already capped, already
serialized, and already respected by the simulation.

### `Game.Areas.DistrictModifierType` — only 14 members
```
0 GarbageProduction        7 Wellbeing
1 ProductConsumption       8 CrimeAccumulation
2 ParkingFee               9 StreetSpeedLimit
3 BuildingFireHazard      10 StreetTrafficSafety
4 BuildingFireResponseTime 11 EnergyConsumptionAwareness
5 BuildingUpkeep          12 CarReserveProbability
6 LowCommercialTax        13 BikeProbability
```

### `Game.City.CityModifierType` — 40 members
```
 0 Attractiveness          14 UniversityInterest        28 BuildingLevelingCost
 1 CrimeAccumulation       15 OfficeSoftwareDemand      29 ExportCost
 3 DisasterWarningTime     16 IndustrialElectronicsDemand 30 TaxiStartingFee
 4 DisasterDamageRate      17 OfficeSoftwareEfficiency  31 IndustrialEfficiency
 5 DiseaseProbability      18 IndustrialElectronicsEfficiency 32 OfficeEfficiency
 6 ParkEntertainment       19 TelecomCapacity           33 PollutionHealthAffect
 7 CriminalMonitorProbability 20 Entertainment          34 HospitalEfficiency
 8 IndustrialAirPollution  21 HighwayTrafficSafety      35 IndustrialFishInputEfficiency
 9 IndustrialGroundPollution 22 PrisonTime              36 IndustrialFishHubEfficiency
10 IndustrialGarbage       23 CrimeProbability          37 CityServiceImportCost
11 RecoveryFailChange      24 CollegeGraduation         38 CityServiceBuildingBaseUpkeepCost
12 OreResourceAmount       25 UniversityGraduation      39 CrimeResponseTime
13 OilResourceAmount       26 ImportCost                40 TaxHappiness
                           27 LoanInterest
```
(Value 2 is unused — a removed member.)

### Coverage of §7 against these enums

**Well covered.** District unrest (`District.CrimeAccumulation` + `Wellbeing`) is an almost exact fit
for the plan's intent. Also strong: crime (5 city members plus district accumulation), health
(`DiseaseProbability`, `PollutionHealthAffect`, `HospitalEfficiency`), education
(`CollegeGraduation`, `UniversityGraduation`, `UniversityInterest`), garbage, building upkeep,
loan interest, import/export cost, industrial and office efficiency (strikes), attractiveness
(immigration and tourism).

**Not covered by any enum member — needs a decision, not an assumption:**
- **RCI demand shifts.** Only `OfficeSoftwareDemand` and `IndustrialElectronicsDemand` exist, both
  narrow. There is no general residential/commercial/industrial demand modifier.
- **Rent / land value nudges.** No member.
- **Birth rate modifier.** No member.
- **One-off subsidies and fines.** No member — requires direct `PlayerMoney` manipulation.
- **Pollution decay boost.** The industrial pollution members reduce *production*, not decay rate.
- **Transit fare income.** Only `TaxiStartingFee` (city) and `ParkingFee` (district).

### The structural constraint this exposes

`DistrictModifierType` has **14 members and no pollution, no land value, no education, no happiness
beyond `Wellbeing`.** City scope has 40. Agora's entire premise is *per-district* politics, so the
effect layer is far more constrained at exactly the scope the design cares most about. This should
shape M5 scoping before any effect is written: either district effects lean heavily on `Wellbeing` +
`CrimeAccumulation`, or Harmony work is accepted for the rest.

---

## 4. Open questions for Scout 0002

1. **Save identity.** Is a stable save GUID exposed anywhere, or must we write our own via
   `IPreSerialize` / `IPostDeserialize`? The plan currently assumes the latter — confirm.
2. **Save-complete callback.** Does a post-save hook exist, or only pre-serialize? Determines whether
   §5's fallback path is needed.
3. **Household wealth field.** Exact member name and units; is it per-household or derived?
4. **District association for citizens.** Does `CurrentDistrict` cover residents reliably, or must we
   walk `Household` → building → district?
5. **Date display surfaces.** Full enumeration required before the M1 clock patch — this is the
   gating item for that milestone.
6. **`CityStatisticsSystem` query API.** How to read a `StatisticType` series without a Harmony patch.
7. **Modifier application.** How `DistrictModifier` / `CityModifier` components are written and whether
   multiple sources stack additively or multiplicatively.

---

## 5. Method note

Type enumeration needs no decompiler — `Colossal.Mono.Cecil.dll` ships with the game and reads
metadata directly. **Reading method bodies does need one**: install `ilspycmd` and decompile into
`refsrc/` (gitignored) before attempting questions 5–7 above.
