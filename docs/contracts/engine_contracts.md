# Contract — Agora.Core engine types and tuning

**schemaVersion: contracts 1 · `CitySnapshot` 2 · `engine_tuning.json` 2**

Frozen at the M2 contract pass. Every engine packet codes against these types and reads every
coefficient through `EngineTuning`. If you need a field or a key that is not here, **stop and report
it** — do not add one. Contracts and `data/engine_tuning.json` are single-owner files; a parallel
edit to either is how fourteen packets end up disagreeing about what a `Bloc` is.

---

## 1. Rules that bind every packet

1. **`Agora.Core` references nothing from the game.** No `Game.*`, `Colossal.*`, `Unity.*`.
2. **netstandard2.0.** No `Span<T>`, `MathF`, `Math.Clamp`, `HashCode.Combine`, index/range syntax,
   `record` with positional/`init` members, or default interface members. Polyfill privately.
3. **Every draw goes through `SeedStreams`** with a constant from `StreamNames`. Never
   `System.Random`, `Guid.NewGuid()`, `DateTime.Now`, or `string.GetHashCode()`.
4. **Never iterate a `Dictionary` or `HashSet` where order affects output.** Every list in these
   contracts has a documented sort key — honour it. This is the most common way determinism dies.
5. **No hardcoded coefficients.** Take an `EngineTuning` and read your section.
6. **`SimDate` only.** No `DateTime` anywhere in Core.

---

## 2. Where things live

| File | Types |
|---|---|
| `src/Agora.Core/Contracts/SimDate.cs` | `SimDate` *(pre-existing)* |
| `src/Agora.Core/Contracts/Boundary.cs` | `IClock`, `ISnapshotSource`, `IEffectSink`, `IFlavorProvider`, `EffectScope`, `EffectRequest`, `FlavorPayload`, `PartyFlavor`, `Article` *(pre-existing)* |
| `src/Agora.Core/Contracts/Issues.cs` | `Issue`, `Issues`, `IssueWeights`, `IssuePosition` |
| `src/Agora.Core/Contracts/Blocs.cs` | `WealthTier`, `EducationTier`, `AgeBand`, `BlocAxes`, `BlocKey`, `Bloc`, `BlocAffinity`, `BlocTurnout` |
| `src/Agora.Core/Contracts/Parties.cs` | `ElectoralSystem`, `RegionTheme`, `PartyStatus`, `FactionStatus`, `Party`, `Faction` |
| `src/Agora.Core/Contracts/Elections.cs` | `PartyVoteShare`, `PollResult`, `DistrictPollResult`, `SeatAllocation`, `DistrictResult`, `ElectionResult` |
| `src/Agora.Core/Contracts/Government.cs` | `CoalitionStatus`, `CoalitionCollapseReason`, `Coalition`, `MandateStatus`, `MandateDirection`, `MandateMetric`, `Mandate` |
| `src/Agora.Core/Contracts/TimelineEvent.cs` | `EventRegion`, `EventOrigin`, `TimelineEventEffect`, `TimelineEvent` |
| `src/Agora.Core/Contracts/CitySnapshot.cs` | `WealthDistribution`, `EducationDistribution`, `AgeDistribution`, `PollutionLevels`, `ServiceCoverage`, `TaxRates`, `CitySnapshot`, `DistrictSnapshot` |
| `src/Agora.Core/Contracts/PoliticalState.cs` | `DerivedIndices`, `DistrictIndices`, `LlmWakeCadence`, `AgoraSettings`, `PoliticalState` |
| `src/Agora.Core/Tuning/EngineTuning.cs` | `EngineTuning` + 14 section classes, `EffectCap` |
| `src/Agora.Core/Tuning/TuningJson.cs` | `TuningReader`, `AgeBandMultipliers`, `TuningFormatException` |

Namespaces: `Agora.Core.Contracts` and `Agora.Core.Tuning`.

---

## 3. The issue space

Six issues, closed set, `enum Issue`: `Services`, `CostOfLiving`, `Environment`, `Transit`,
`Growth`, `HeritageOrder`. Iterate `Issues.All` (`IReadOnlyList<Issue>`), never `Enum.GetValues` —
the framework's ordering is unspecified and unspecified ordering is a determinism defect.
`Issues.Count == 6`. `Issues.ToKey(issue)` gives the camelCase JSON key.

Two structurally identical, deliberately distinct readonly structs, each with six `double`
properties (`Services`, `CostOfLiving`, `Environment`, `Transit`, `Growth`, `HeritageOrder`) and a
`this[Issue]` indexer:

- **`IssueWeights`** — how much a bloc *cares*. Non-negative. `Uniform`, `With`, `Add`, `Scale`,
  `Sum()`, `Normalized()` (rescales to sum 6), `Clamped(min, max)`.
- **`IssuePosition`** — a *stance*, each component in `[-1, +1]`. `Centre`, `With`, `Add`, `Scale`,
  `Clamped()`, `Clamped(min,max)`, `WeightedDistance(other, weights)` → `[0,1]`,
  `Distance(other)` → `[0,1]`.

Sign convention (fixed — affinity depends on it): `+1` = spend/protect/restrict **more**.

---

## 4. Blocs

`WealthTier { Low, Middle, High }` · `EducationTier { Uneducated, PoorlyEducated, Educated,
WellEducated, HighlyEducated }` (mirrors `CitizenEducationLevel`) · `AgeBand { Child, Teen, Adult,
Elderly }` (mirrors `CitizenAge`). Both mirrors verified with `tools/api-query.ps1 -Enum`.

`BlocAxes` — `Wealth`/`Education`/`Age` ordered lists, `AllKeys` (60 `BlocKey`s in fixed order),
`BlocCount = 60`, and `Axis(tier)` overloads normalising each tier to `[-1, +1]`.

`BlocKey` (readonly struct): `WealthTier Wealth`, `EducationTier Education`, `AgeBand Age`,
`int Ordinal` (0–59), `string Id` (`"middle.educated.adult"`), equality, `IComparable<BlocKey>`.
**`Id` is what goes into `SeedStreams.RngFor` — never an index, never a hash code.**

`Bloc` (class): `string DistrictId`, `BlocKey Key`, `int Population`, `double PopulationShare`,
`int EligibleVoters`, `IssueWeights Weights`, `IssuePosition Ideal`, `double Happiness` (0–100),
`double Discontent` (0–1), `List<PartyVoteShare> PreviousVote`, `bool HasCityFallbacks`.

`BlocAffinity` (class): `string DistrictId`, `BlocKey Bloc`, `string PartyId`, `double Affinity`,
plus the component breakdown kept for the dashboard — `IssueComponent`, `IncumbencyComponent`,
`MandateComponent`, `EventComponent`, `LoyaltyComponent`, `NoiseComponent`.

`BlocTurnout` (class): `string DistrictId`, `BlocKey Bloc`, `double Turnout`, `int EligibleVoters`,
`int ProjectedVotes`, `double NoiseComponent`.

Minors are disenfranchised by a **turnout multiplier of 0**, not by a missing bloc.

---

## 5. Parties and factions

`ElectoralSystem { Proportional, FirstPastThePost }` · `RegionTheme { Eu, Na }` ·
`PartyStatus`/`FactionStatus` `{ Active, Endangered, Dissolved, Merged, Revived }`.

`Party`: `Id`, `Name`, `ShortName`, `Description`, `Slogan`, `ColorHex`, `ArchetypeId`,
`IssuePosition Platform`, `IssuePosition LastManifesto`, `PartyStatus Status`, `SimDate FoundedDate`,
`SimDate? DissolvedDate`, `double LastVoteShare`, `int SeatsHeld`, `bool IsIncumbent`,
`bool IsInGovernment`, `int ConsecutiveElectionsBelowThreshold`, `string? PredecessorPartyId`,
`string? SuccessorPartyId`, `List<string> FactionIds`, `Issue CoreGrievance`, `int RevivalCount`.

`Faction`: `Id`, `PartyId`, `Name`, `ShortName`, `Description`, `LeaderName`, `ArchetypeId`,
`IssuePosition Platform`, `double InternalSupport`, `bool IsDominant`, `double TensionWithParty`,
`FactionStatus Status`, `SimDate FoundedDate`, `SimDate? DissolvedDate`,
`string? PredecessorFactionId`, `string? SuccessorFactionId`, `List<Issue> Demands`,
`List<BlocKey> CoreBlocs`, `int ConsecutiveCyclesBelowThreshold`, `Issue CoreGrievance`.

**`Name`, `ShortName`, `Description`, `Slogan`, `LeaderName` are flavor-owned.** They arrive from
`IFlavorProvider` and must never be parsed or fed into a calculation (non-negotiable #1).
`Id`, `ArchetypeId` and every number are engine-owned.

---

## 6. Elections

`PartyVoteShare` (readonly struct): `string PartyId`, `double Share`. **Every list of these is
sorted by `PartyId` ordinal ascending.** That is a contract, not a style preference.

`PollResult`: `SchemaVersion=1`, `Id`, `SimDate Date`, `PollsterName` (flavor), `PollsterId`
(engine), `List<PartyVoteShare> Shares` (published), `List<PartyVoteShare> TrueShares` (model truth,
never shown), `List<DistrictPollResult> Districts`, `UndecidedShare`, `ProjectedTurnout`,
`int SampleSize`, `MarginOfError`, `int WeeksToElection`, `SimDate? ElectionDate`, `bool IsPublished`.

`DistrictPollResult`: `DistrictId`, `Shares`, `ProjectedTurnout`, `double SamplingBias` (negative =
under-sampled; the M4a gate asserts this is negative in low-education districts).

`SeatAllocation` (readonly struct): `PartyId`, `int Seats`, `double SeatShare`, `double VoteShare`,
`int DistrictSeats`, `int ListSeats`, `bool PassedThreshold`.

`DistrictResult`: `DistrictId`, `Shares`, `Turnout`, `int VotesCast`, `int EligibleVoters`,
`WinningPartyId`, `double Margin`, `int Seats`, `bool DecidedByTieBreak`.

`ElectionResult`: `SchemaVersion=1`, `Id`, `Date`, `ElectoralSystem System`, `int TermNumber`,
`bool IsSnapElection`, `List<string> PartyIdsOnBallot`, `List<PartyVoteShare> CityVoteShares`,
`List<DistrictResult> Districts`, `List<SeatAllocation> Seats`, `int TotalSeats`, `double Turnout`,
`int TotalVotesCast`, `int TotalEligibleVoters`, `string? MayorPartyId`, `string? MayorName`,
`List<PartyVoteShare> MayorVoteShares`, `double FinalPollDeviation`, `SimDate NextElectionDate`.

Vote counts are integers. A one-vote margin must be representable, and float shares cannot do that.

---

## 7. Government

`Coalition`: `SchemaVersion=1`, `Id`, `SimDate FormedDate`, `SimDate? EndedDate`,
`List<string> MemberPartyIds` (sorted), `LeadPartyId`, `List<string> OppositionPartyIds` (sorted),
`int Seats`, `double SeatShare`, `bool HasMajority`, `double Cohesion`, `double Stability`,
`CoalitionStatus Status` `{ Negotiating, Governing, Minority, Collapsed, Expired }`,
`CoalitionCollapseReason CollapseReason` `{ None, StabilityDecay, MandateFailure, EventShock,
IdeologicalDrift, PartnerWithdrawal }`, `int FormationAttempts`, `ElectionId`,
`List<string> MandateIds`.

Under FPTP the winning party plus mayor is modelled as a `Coalition` too, so the mandate packet and
the dashboard need only one code path.

`Mandate`: `SchemaVersion=1`, `Id`, `PartyId`, `CoalitionId`, `string? DistrictId`, `Issue Issue`,
`MandateMetric Metric`, `MandateDirection Direction` `{ Increase, Decrease }`, `double BaselineValue`,
`double TargetValue`, `double CurrentValue`, `double Progress` (0–1), `SimDate IssuedDate`,
`SimDate DeadlineDate`, `SimDate? ResolvedDate`, `MandateStatus Status` `{ Pending, Active,
Fulfilled, PartiallyFulfilled, Defied, Abandoned }`, `double Salience`,
`string? ResolutionEffectId`, `string Text` (flavor), `bool IsMeasurementStalled`.

`MandateMetric`: `Happiness, Unemployment, AirPollution, GroundPollution, NoisePollution,
WaterPollution, CrimeRate, HealthCoverage, EducationCoverage, PoliceCoverage, FireCoverage,
GarbageCoverage, TransitCoverage, AverageCommuteMinutes, AverageRent, AverageLandValue, RentBurden,
Population, BudgetBalance, Debt`. Every member is readable from `CitySnapshot` or
`DistrictSnapshot` — if it cannot be measured, it cannot be a mandate. A mandate whose metric is
unmeasurable is **held** (`IsMeasurementStalled`), not failed.

---

## 8. Timeline events

`EventRegion { Eu, Na, Global }` · `EventOrigin { Catalog, Procedural, Political }`.

`TimelineEventEffect` (readonly struct): `EffectId`, `EffectScope Scope`, `double Magnitude`,
`int DurationMonths`, `string? DistrictId`, plus `ToRequest(sourceId)` → `EffectRequest`.
`DistrictId` is always null in catalog files — real history does not know the player's districts —
and is filled in deterministically by the scheduler.

`TimelineEvent`: `SchemaVersion=1`, `Id`, `SimDate Date`, `EventRegion Region`, `EventOrigin Origin`,
`Title`, `int Severity` (1–5), `int DurationMonths`, `List<TimelineEventEffect> Effects`,
`HeadlineBrief`, `List<string> Tags`, `IssuePosition IssuePressure`, `ArchetypeId`,
`SimDate? FiredDate`, `SimDate? ExpiresDate`, `LocalAngle` (flavor).

---

## 9. Snapshot

`CitySnapshot` — **`SchemaVersion = 2`.** City block: `Date`, `Population`, `Households`,
`Happiness` (0–100), `Unemployment` (0–1), `Money`, `Income`, `Expenses`, `BudgetBalance`, `Debt`
(all `long`), `WealthDistribution Wealth`, `EducationDistribution Education`, `AgeDistribution Age`,
`PollutionLevels Pollution`, `ServiceCoverage Services`, `TaxRates Taxes`, `CrimeRate`, `SickRate`,
`AverageLandValue`, `LandValueTrend`, `AverageRent`, `RentTrend`, `RentBurden`, `TransitRidership`,
`AverageCommuteMinutes`, `TrafficCongestion`, `List<string> ActivePolicyIds`,
`List<string> RecentDisasterIds`, `List<string> InProgressMandateIds`, `DerivedIndices Indices`,
`List<DistrictSnapshot> Districts` (sorted by `Id`).

`DistrictSnapshot`: `Id`, `Name`, `Population`, `Households`, `Happiness`, `Unemployment`, `Wealth`,
`Education`, `Age`, `Pollution`, `Services`, `CrimeRate`, `SickRate`, `AverageLandValue`,
`LandValueTrend`, `AverageRent`, `RentTrend`, `RentBurden`, `TransitRidership`,
`AverageCommuteMinutes`, `TrafficCongestion`, `bool HasCityFallbacks`,
`List<string> CityFallbackFields`.

Value structs: `WealthDistribution(LowShare, MiddleShare, HighShare)` ·
`EducationDistribution(UneducatedShare … HighlyEducatedShare)` with `Index()` → `[0,1]` ·
`AgeDistribution(ChildShare … ElderlyShare)` · `PollutionLevels(Air, Ground, Noise, Water)` with
`Mean()` · `ServiceCoverage(Health, Education, Police, Fire, Garbage, Transit, Water, Electricity,
Parks)` with `Mean()` · `TaxRates(Residential, Commercial, Industrial, Office)` with `Mean()`.
Each has an indexer keyed by its tier enum where one applies.

**Per-district numbers are best-effort.** A sensor that cannot resolve one copies the city value,
sets `HasCityFallbacks`, and appends the property name to `CityFallbackFields`. Do not score a
mandate against a fallen-back field, and do not present one as a local fact.

---

## 10. Political state (the sidecar root)

`PoliticalState`: `SchemaVersion=1`, `Guid SaveGuid`, `SimDate Date`, `AgoraSettings Settings`,
`List<Party> Parties` (by id), `List<Faction> Factions` (by id), `List<Bloc> Blocs` (by districtId
then `BlocKey.Ordinal`), `List<PartyVoteShare> CurrentVoteShares`,
`List<DistrictResult> CurrentDistrictStandings`, `List<PollResult> RecentPolls` (oldest first),
`List<ElectionResult> ElectionHistory` (oldest first), `Coalition? Government`,
`List<Coalition> CoalitionHistory`, `List<Mandate> Mandates` (by id),
`List<TimelineEvent> ActiveEvents` (by id), `List<string> FiredEventIds` (sorted),
`DerivedIndices Indices`, `int TermNumber`, `SimDate? NextElectionDate`, `bool IsCampaignSeason`,
`string? MayorPartyId`, `SimDate? LastFlavorDate`.

`AgoraSettings`: `SchemaVersion=1`, `int StartYear` (1990), `RegionTheme Theme`,
`ElectoralSystem System`, `LlmWakeCadence WakeCadence` (`[Flags] None/Yearly/Election/Manual/Default`),
`int SnapshotRetention`, `bool Enabled`, `bool EffectsEnabled`.

`DerivedIndices`: `GiniCoefficient`, `BrainDrainIndex`, `ServiceInequalityIndex`,
`CommuteMiseryIndex`, `PolarizationIndex`, `LegitimacyIndex`, `DiscontentIndex`,
`List<DistrictIndices> Districts` (by district id). All `[0,1]`.

`DistrictIndices`: `DistrictId`, `GentrificationIndex`, `CommuteMiseryIndex`,
`ServiceCoverageIndex`, `DiscontentIndex`, `GiniCoefficient`, `HasCityFallbacks`.

**Wire conventions** for every Agora JSON file: camelCase property names, `SimDate` as the string
`"YYYY-MM-DD"`, enums as their C# member names, `Guid` in canonical `8-4-4-4-12` form. The
persistence packet supplies the converters; `Agora.Core` never serializes anything itself.

---

## 11. Tuning — `EngineTuning`

```csharp
EngineTuning t = EngineTuning.FromJson(json);      // throws TuningFormatException on bad JSON only
EngineTuning t = EngineTuning.LoadOrDefault(json); // never throws; degrades to defaults
EngineTuning t = EngineTuning.Default;             // built-in defaults, identical to the shipped file
IReadOnlyList<string> problems = t.Warnings;       // missing / wrong-shape keys, in read order
```

Missing keys never throw — they return the documented default and add a line to `Warnings`. Log the
warnings; a non-empty list means the file and the code have drifted.

Sections, one per packet: `t.Blocs`, `t.Parties`, `t.Factions`, `t.Affinity`, `t.Turnout`,
`t.Polling`, `t.ElectionsPr`, `t.ElectionsFptp`, `t.Coalitions`, `t.Mandates`, `t.Catalog`,
`t.Scheduler`, `t.Indices`, `t.Effects`. C# property names are PascalCase of the JSON key; the JSON
key list for each section is in `data/engine_tuning.json` and validated by
`data/schemas/engine_tuning.schema.json`.

Helper value types: `AgeBandMultipliers(Child, Teen, Adult, Elderly)` with a `this[AgeBand]`
indexer, used by `Turnout.AgeBandMultipliers`; `IssueWeights` for every per-issue coefficient map;
`IssuePosition` for the bloc ideal-point maps; `ServiceCoverage` for
`Indices.ServiceInequalityWeights`.

### The effect palette

`t.Effects` is the closed registry (non-negotiable #4):

```csharp
IReadOnlyList<string> ids = t.Effects.EffectIds;                     // sorted ordinal ascending
bool known  = t.Effects.TryGetEffect("city-loan-interest", out var c);
EffectCap c = t.Effects.CapFor(effectId, EffectScope.City);          // never uncapped
double m    = c.ClampMagnitude(requested);
int months  = c.ClampDuration(requestedMonths);
```

`EffectCap` (readonly struct): `EffectId`, `EffectScope Scope`, `string Modifier`,
`double MagnitudeCap`, `int DurationCapMonths`, `string FallbackEffectId` (empty = terminal),
`ClampMagnitude(double)`, `ClampDuration(int)`.

`Modifier` names a member of `Game.Areas.DistrictModifierType` or `Game.City.CityModifierType`.
Core keeps it as a string; the enum lookup happens in `Agora.Mod/Effects`. That is what lets the
palette be declared in data without Core learning that the game exists.

43 entries ship: 12 district-scoped (of the 14 available district modifiers) and 31 city-scoped.
The two terminal fallbacks are **`district-wellbeing`** and **`city-tax-happiness`** — every other
entry falls back to the one matching its scope, and both terminals carry an empty
`fallbackEffectId` so the sink cannot loop.

---

## 12. Seed streams

`StreamNames` constants, pre-existing: `PollError`, `PollTurnout`, `AffinityNoise`, `TurnoutNoise`,
`PartyLifecycle`, `FactionLifecycle`, `CoalitionFormation`, `CoalitionCollapse`, `EventJitter`,
`EventProcedural`, `MandateSelection`, `NameSelection`.

Added at this freeze: `PartyGeneration` (`party.generation`), `FactionGeneration`
(`faction.generation`), `CampaignManifesto` (`campaign.manifesto`), `PollHouseEffect`
(`poll.houseeffect`), `PollSample` (`poll.sample`), `ElectionTieBreak` (`election.tiebreak`),
`ElectionDistrictSwing` (`election.district.swing`), `MandateGeneration` (`mandate.generation`),
`UnrestTrigger` (`event.unrest`).

Use `SeedStreams.RngFor(saveGuid, date, streamName, entityId)` for anything per-district, per-party
or per-bloc. Drawing repeatedly from one stream inside a loop couples the result to iteration order,
so inserting a district silently changes every later district's outcome.

---

## 13. Open decisions — leave a seam, do not implement

Marked in code as `// AGORA-SEAM(§14.x)` and pinned in the schema:

| §14 item | Where | State |
|---|---|---|
| NA primaries as full elections | `electionsFptp.primariesEnabled` | pinned `false` (schema `const`) |
| Timeline jitter (fixed dates vs ±6 months) | `catalog.jitterEnabled` / `jitterMonths` | pinned `false` / `0`; `event.jitter` stream exists but must not be drawn from |
| Snapshot retention default | `scheduler.snapshotRetention`, `AgoraSettings.SnapshotRetention` | 25, proposed; keep newest N and nothing cleverer |
| Post-2026 authorship split | `catalog.procedural*` | keys size the proposed shape only |
| Unrest ceiling (statistical only) | `mandates.unrestEventProbabilityOnDefiance` | no visual destruction; statistical effects only |
| Effect palette gap (§7) | `effects.perEffect` | ships only enum-backed effects; no rent, land value, RCI demand or subsidy entries exist |

---

## 14. Related schemas

- `data/schemas/engine_tuning.schema.json` — the tuning file. `additionalProperties: false`
  everywhere, so a typo'd key fails the schema suite instead of silently defaulting at runtime.
- `data/schemas/snapshot.schema.json` — `CitySnapshot` on the wire.
- `data/schemas/political_state.schema.json` — the sidecar `state_*.json`.
- `data/schemas/politics_flavor.schema.json` — LLM output. No numeric field may ever appear.
- `data/schemas/timeline.schema.json` — the curated catalogs. Unchanged by this pass.
