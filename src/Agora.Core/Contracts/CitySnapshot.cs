using System.Collections.Generic;

namespace Agora.Core.Contracts
{
    /// <summary>
    /// Share of the population in each wealth tier. The three shares sum to 1 within rounding.
    /// </summary>
    public readonly struct WealthDistribution
    {
        public double LowShare { get; }
        public double MiddleShare { get; }
        public double HighShare { get; }

        public WealthDistribution(double lowShare, double middleShare, double highShare)
        {
            LowShare = lowShare;
            MiddleShare = middleShare;
            HighShare = highShare;
        }

        public double this[WealthTier tier]
        {
            get
            {
                switch (tier)
                {
                    case WealthTier.Low: return LowShare;
                    case WealthTier.Middle: return MiddleShare;
                    case WealthTier.High: return HighShare;
                    default: throw new System.ArgumentOutOfRangeException(nameof(tier), tier, "Unknown wealth tier.");
                }
            }
        }
    }

    /// <summary>
    /// Share of the population at each education level. Mirrors
    /// <c>Game.Citizens.CitizenEducationLevel</c>; the five shares sum to 1 within rounding.
    /// </summary>
    public readonly struct EducationDistribution
    {
        public double UneducatedShare { get; }
        public double PoorlyEducatedShare { get; }
        public double EducatedShare { get; }
        public double WellEducatedShare { get; }
        public double HighlyEducatedShare { get; }

        public EducationDistribution(double uneducated, double poorlyEducated, double educated,
                                     double wellEducated, double highlyEducated)
        {
            UneducatedShare = uneducated;
            PoorlyEducatedShare = poorlyEducated;
            EducatedShare = educated;
            WellEducatedShare = wellEducated;
            HighlyEducatedShare = highlyEducated;
        }

        public double this[EducationTier tier]
        {
            get
            {
                switch (tier)
                {
                    case EducationTier.Uneducated: return UneducatedShare;
                    case EducationTier.PoorlyEducated: return PoorlyEducatedShare;
                    case EducationTier.Educated: return EducatedShare;
                    case EducationTier.WellEducated: return WellEducatedShare;
                    case EducationTier.HighlyEducated: return HighlyEducatedShare;
                    default: throw new System.ArgumentOutOfRangeException(nameof(tier), tier, "Unknown education tier.");
                }
            }
        }

        /// <summary>
        /// Mean education on <c>[0, 1]</c>, weighting the five tiers evenly. This is the figure the
        /// turnout and polling packets mean when they say "education index".
        /// </summary>
        public double Index() =>
            (UneducatedShare * 0.0 + PoorlyEducatedShare * 0.25 + EducatedShare * 0.5 +
             WellEducatedShare * 0.75 + HighlyEducatedShare * 1.0);
    }

    /// <summary>
    /// Share of the population in each age band. Mirrors <c>Game.Citizens.CitizenAge</c>; the four
    /// shares sum to 1 within rounding.
    /// </summary>
    public readonly struct AgeDistribution
    {
        public double ChildShare { get; }
        public double TeenShare { get; }
        public double AdultShare { get; }
        public double ElderlyShare { get; }

        public AgeDistribution(double child, double teen, double adult, double elderly)
        {
            ChildShare = child;
            TeenShare = teen;
            AdultShare = adult;
            ElderlyShare = elderly;
        }

        public double this[AgeBand band]
        {
            get
            {
                switch (band)
                {
                    case AgeBand.Child: return ChildShare;
                    case AgeBand.Teen: return TeenShare;
                    case AgeBand.Adult: return AdultShare;
                    case AgeBand.Elderly: return ElderlyShare;
                    default: throw new System.ArgumentOutOfRangeException(nameof(band), band, "Unknown age band.");
                }
            }
        }
    }

    /// <summary>
    /// Pollution levels, each normalised to <c>[0, 1]</c> against the game's own display maximum so
    /// the four are comparable and so a units change in the game does not silently retune the engine.
    /// </summary>
    public readonly struct PollutionLevels
    {
        public double Air { get; }
        public double Ground { get; }
        public double Noise { get; }
        public double Water { get; }

        public PollutionLevels(double air, double ground, double noise, double water)
        {
            Air = air;
            Ground = ground;
            Noise = noise;
            Water = water;
        }

        /// <summary>Unweighted mean of the four, <c>[0, 1]</c>.</summary>
        public double Mean() => (Air + Ground + Noise + Water) / 4.0;
    }

    /// <summary>
    /// Service coverage, each <c>[0, 1]</c> where 1 is fully served. Missing coverage for a service
    /// the city has not built yet reads as 0, not as a fallback.
    /// </summary>
    public readonly struct ServiceCoverage
    {
        public double Health { get; }
        public double Education { get; }
        public double Police { get; }
        public double Fire { get; }
        public double Garbage { get; }
        public double Transit { get; }
        public double Water { get; }
        public double Electricity { get; }
        public double Parks { get; }

        public ServiceCoverage(double health, double education, double police, double fire,
                               double garbage, double transit, double water, double electricity,
                               double parks)
        {
            Health = health;
            Education = education;
            Police = police;
            Fire = fire;
            Garbage = garbage;
            Transit = transit;
            Water = water;
            Electricity = electricity;
            Parks = parks;
        }

        /// <summary>Unweighted mean coverage, <c>[0, 1]</c>. The weighted form lives in the indices packet.</summary>
        public double Mean() =>
            (Health + Education + Police + Fire + Garbage + Transit + Water + Electricity + Parks) / 9.0;
    }

    /// <summary>Tax rates as fractions, e.g. 0.10 for 10%.</summary>
    public readonly struct TaxRates
    {
        public double Residential { get; }
        public double Commercial { get; }
        public double Industrial { get; }
        public double Office { get; }

        public TaxRates(double residential, double commercial, double industrial, double office)
        {
            Residential = residential;
            Commercial = commercial;
            Industrial = industrial;
            Office = office;
        }

        /// <summary>Unweighted mean rate. Used as the cost-of-living tax signal.</summary>
        public double Mean() => (Residential + Commercial + Industrial + Office) / 4.0;
    }

    /// <summary>
    /// City-wide statistics read from the game's own city statistics screen
    /// (<c>Game.Simulation.CityStatisticsSystem</c>), plus homelessness from
    /// <c>CountHouseholdDataSystem</c> and the garbage production rate from
    /// <c>GarbageAccumulationSystem</c>.
    ///
    /// <para>
    /// <b>Every field here is city-only, and that is a property of the game rather than of this
    /// sensor pass.</b> <c>CityStatisticsSystem.StatisticsKey</c> is <c>(StatisticType, int
    /// parameter)</c> — two fields, no district, no area, no entity — and there is no district
    /// statistics system anywhere in the game (scout 0004 §1.4). So this block is deliberately
    /// <i>not</i> mirrored onto <see cref="DistrictSnapshot"/>: a district copy would be the city
    /// number under a name claiming it was local, which is the failure
    /// <see cref="DistrictSnapshot.CityFallbackFields"/> exists to prevent.
    /// </para>
    /// </summary>
    public readonly struct CityStatistics
    {
        /// <summary>Homeless residents. <c>StatisticType.HomelessCount</c>.</summary>
        public int Homeless { get; }

        /// <summary>
        /// Homeless share of the moved-in population, <b>0–1</b>.
        /// </summary>
        /// <remarks>
        /// The game's <c>CountHouseholdDataSystem.HomelessnessRate</c> is a percentage 0–100; this is
        /// that number divided by 100, on the same convention that makes
        /// <see cref="TaxRates"/> fractions. The conversion is load-bearing — a threshold authored
        /// against the unconverted figure fires at a hundred times the intended homelessness.
        /// </remarks>
        public double HomelessShare { get; }

        /// <summary>
        /// Citizens who moved into the city. <c>StatisticType.CitizensMovedIn</c>.
        /// </summary>
        /// <remarks>
        /// A head <i>count</i>, not a rate, despite what the neighbouring game enum members are
        /// called. Named for what it is so that an event's prose cannot promise a percentage the
        /// number never was.
        /// </remarks>
        public int CitizensMovedIn { get; }

        /// <summary>Citizens who left the city. <c>StatisticType.CitizensMovedAway</c>. A count.</summary>
        public int CitizensMovedAway { get; }

        /// <summary>
        /// Citizens who left specifically because they were unhappy —
        /// <c>StatisticType.MovedAwayReason</c> keyed by <c>(int)MoveAwayReason.NotHappy</c>.
        /// </summary>
        /// <remarks>
        /// Separated from <see cref="CitizensMovedAway"/> because the two mean opposite things
        /// politically: people leaving because there is nowhere to live is a housing story, and
        /// people leaving because they are miserable is a government story.
        /// </remarks>
        public int MovedAwayUnhappy { get; }

        /// <summary>Births. <c>StatisticType.BirthRate</c>. A count, not a per-mille rate.</summary>
        /// <remarks>
        /// Readable, and always was. <c>docs/scout/0001-api-index.md</c> §3 records birth rate as
        /// unreachable, but that finding was about <c>CityModifierType</c> — nothing can *modify* the
        /// birth rate, which is a different claim from being unable to *read* it (scout 0004 §4).
        /// </remarks>
        public int Births { get; }

        /// <summary>Deaths. <c>StatisticType.DeathRate</c>. A count.</summary>
        public int Deaths { get; }

        /// <summary>
        /// Garbage <b>produced per day</b>, from <c>GarbageAccumulationSystem.garbageAccumulation</c>.
        /// </summary>
        /// <remarks>
        /// <b>Not a stockpile.</b> The game's own binding for this exact value is named
        /// <c>productionRate</c> (scout 0004 §7.1), and it does not fall when collection improves —
        /// only when the city produces less. Garbage that is piling up uncollected is
        /// <see cref="CitySnapshot.UncollectedGarbage"/>, which is a different number and the only
        /// one of the two that has a district breakdown.
        /// </remarks>
        public double GarbageProductionRate { get; }

        public CityStatistics(int homeless, double homelessShare, int citizensMovedIn,
                              int citizensMovedAway, int movedAwayUnhappy, int births, int deaths,
                              double garbageProductionRate)
        {
            Homeless = homeless;
            HomelessShare = homelessShare;
            CitizensMovedIn = citizensMovedIn;
            CitizensMovedAway = citizensMovedAway;
            MovedAwayUnhappy = movedAwayUnhappy;
            Births = births;
            Deaths = deaths;
            GarbageProductionRate = garbageProductionRate;
        }
    }

    /// <summary>
    /// Tourism and visitor pressure. City-only, for the reason given on <see cref="CityStatistics"/>;
    /// the two genuinely per-district figures live on the snapshots themselves as
    /// <see cref="CitySnapshot.AttractionCount"/> and
    /// <see cref="CitySnapshot.SignatureBuildingCount"/>.
    /// </summary>
    public readonly struct TourismLevels
    {
        /// <summary>Tourists currently in the city. <c>StatisticType.TouristCount</c>.</summary>
        public int Tourists { get; }

        /// <summary>
        /// The city's attractiveness index, from <c>Game.City.Tourism.m_Attractiveness</c> on the city
        /// entity.
        /// </summary>
        /// <remarks>
        /// <b>A dimensionless index, not a percentage</b>, and deliberately stored raw rather than
        /// normalised: it is the exact quantity the shipped <c>city-attractiveness</c> effect moves,
        /// which makes trigger and effect two ends of one number. Normalising it against an invented
        /// reference maximum would break that correspondence silently.
        /// </remarks>
        public int Attractiveness { get; }

        /// <summary>Hotel rooms occupied. <c>StatisticType.LodgingUsed</c>.</summary>
        public int LodgingUsed { get; }

        /// <summary>Hotel rooms available. <c>StatisticType.LodgingTotal</c>. Zero in a city with no hotels.</summary>
        public int LodgingTotal { get; }

        public TourismLevels(int tourists, int attractiveness, int lodgingUsed, int lodgingTotal)
        {
            Tourists = tourists;
            Attractiveness = attractiveness;
            LodgingUsed = lodgingUsed;
            LodgingTotal = lodgingTotal;
        }
    }

    /// <summary>
    /// The city's progression through the milestone track. City-only by nature — a district has no
    /// level.
    /// </summary>
    public readonly struct ProgressionState
    {
        /// <summary>
        /// The achieved milestone, from the <c>Game.City.MilestoneLevel</c> singleton.
        /// </summary>
        /// <remarks>
        /// <b>This is also "city level".</b> CS2 has no separate level counter — the two names in the
        /// rework plan are one number (scout 0004 §6.1), and carrying both would guarantee they
        /// eventually disagreed.
        /// </remarks>
        public int MilestoneLevel { get; }

        /// <summary>
        /// <b>Lifetime</b> experience, from <c>CitySystem.XP</c> (the <c>Game.City.XP.m_XP</c>
        /// accumulator).
        /// </summary>
        /// <remarks>
        /// Deliberately not <c>MilestoneSystem.currentXP</c>, which looks like the same number and is
        /// not: <c>MilestoneSystem.cs:90</c> computes it as <c>CitySystem.XP - max(0, lastRequired)</c>,
        /// so it is XP *since the last milestone* and it **drops back toward zero every time the city
        /// achieves one**. That matters here because this field is recorded into the metric history
        /// and wave 3 may author a <c>delta</c> trigger against it — and a resetting counter would fire
        /// "experience collapsed" at the exact moment the city succeeded, an event whose prose the
        /// simulation contradicts. A lifetime total is monotonic, so a delta on it always means what
        /// it says. Within-milestone position is <see cref="MilestoneProgress"/>, which is the honest
        /// place for it.
        /// </remarks>
        public int Experience { get; }

        /// <summary>Progress toward the next milestone, 0–1. <c>MilestoneSystem.progress</c>.</summary>
        /// <remarks>
        /// Sanitised by the sensor rather than trusted raw. <c>MilestoneSystem.progress</c> is
        /// <c>currentXP / requiredXP</c> with no guard of its own, and at the top of the milestone
        /// tree the denominator can reach zero — which would put an infinity or a NaN into this field
        /// and serialise <c>snapshot.json</c> as invalid JSON.
        /// </remarks>
        public double MilestoneProgress { get; }

        public ProgressionState(int milestoneLevel, int experience, double milestoneProgress)
        {
            MilestoneLevel = milestoneLevel;
            Experience = experience;
            MilestoneProgress = milestoneProgress;
        }
    }

    /// <summary>Which taxable area a per-resource tax rate belongs to. Mirrors the three area types
    /// that <c>TaxSystem</c> exposes a per-resource reader for.</summary>
    public enum TaxArea
    {
        Commercial = 0,
        Industrial = 1,
        Office = 2
    }

    /// <summary>
    /// One resource's tax rate within one area — enough for an event to trigger on "office software
    /// subsidised while farming is taxed".
    /// </summary>
    public readonly struct ResourceTaxRate
    {
        public TaxArea Area { get; }

        /// <summary>
        /// The game's own stable key, <c>EconomyUtils.GetResourceIndex</c> — a small dense integer,
        /// not the <c>Resource</c> flag value.
        /// </summary>
        /// <remarks>
        /// The flag enum is a bitfield up to <c>1 &lt;&lt; 40</c> and would be a poor sort key; the
        /// index is what the game itself lays its internal tax array out by. Sorting
        /// <see cref="CitySnapshot.IndustryTaxRates"/> by <c>(Area, ResourceIndex)</c> is what keeps
        /// this list free of the collection-order determinism bug.
        /// </remarks>
        public int ResourceIndex { get; }

        /// <summary>The resource's stable name, from <c>EconomyUtils.GetName</c>. The id content authors write.</summary>
        public string ResourceName { get; }

        /// <summary>The rate as a fraction, e.g. 0.10 for 10%. The game reports whole percentage points.</summary>
        public double Rate { get; }

        public ResourceTaxRate(TaxArea area, int resourceIndex, string resourceName, double rate)
        {
            Area = area;
            ResourceIndex = resourceIndex;
            ResourceName = resourceName ?? "";
            Rate = rate;
        }
    }

    /// <summary>
    /// The measured state of the city at one moment — the engine's only view of the game, and the
    /// body of <c>snapshot.json</c> (<c>politicsmodplan.md</c> §6).
    ///
    /// <para>
    /// Sensor passes widen this type; every widening bumps <see cref="SchemaVersion"/> and runs
    /// through <c>/schema-change</c>, because it is mirrored by <c>data/schemas/snapshot.schema.json</c>
    /// and by the LLM prompt.
    /// </para>
    ///
    /// <para>
    /// Per-district fields are best-effort by design: Scout 0001 flags that several metrics may only
    /// exist city-wide. A sensor that cannot resolve a district value falls back to the city value
    /// and records the field name in <see cref="DistrictSnapshot.CityFallbackFields"/>, rather than
    /// throwing.
    /// </para>
    /// </summary>
    public sealed class CitySnapshot
    {
        /// <summary>
        /// 4 as of the city-statistics pass. v1 carried only population, happiness, unemployment and
        /// money; v2 was the M2 contract freeze; v3 adds the four cost lines the game's own district
        /// panel shows — <see cref="AverageHouseholdUpkeep"/>,
        /// <see cref="AverageHouseholdResourceSpend"/>, <see cref="AverageHouseholdFees"/> — and the
        /// <see cref="DisposableMargin"/> they add up to. v4 adds what the game's own city statistics
        /// screen can see: <see cref="Statistics"/>, <see cref="Tourism"/>,
        /// <see cref="Progression"/>, <see cref="UnlockedFeatureIds"/>,
        /// <see cref="IndustryTaxRates"/>, and the three genuinely per-district counts.
        /// </summary>
        /// <remarks>
        /// <b>This contract is not a sidecar document and has no migration table.</b>
        /// <c>SidecarDocument</c> has five members and none of them is the snapshot: a
        /// <see cref="CitySnapshot"/> is measured afresh every capture and is never loaded back off
        /// disk, so a version bump here is a <c>/schema-change</c> steps 1, 3 and 4 change — contract,
        /// prompt and <c>data/schemas/snapshot.schema.json</c> — with no step 2 to write. An older
        /// <c>snapshot.json</c> on disk is a debugging artifact, not state. The rework plan's Part IV
        /// lists a v3 → v4 migration for this document; there is nothing for it to migrate.
        /// </remarks>
        public int SchemaVersion { get; set; } = 4;

        public SimDate Date { get; set; }

        public int Population { get; set; }

        public int Households { get; set; }

        /// <summary>0–100.</summary>
        public double Happiness { get; set; }

        /// <summary>0–1.</summary>
        public double Unemployment { get; set; }

        /// <summary>Current balance. Signed; a city in debt reads negative.</summary>
        public long Money { get; set; }

        /// <summary>Monthly income.</summary>
        public long Income { get; set; }

        /// <summary>Monthly expenses.</summary>
        public long Expenses { get; set; }

        /// <summary>Income minus expenses for the month. Signed.</summary>
        public long BudgetBalance { get; set; }

        /// <summary>Outstanding loan principal. Non-negative.</summary>
        public long Debt { get; set; }

        public WealthDistribution Wealth { get; set; }

        public EducationDistribution Education { get; set; }

        public AgeDistribution Age { get; set; }

        public PollutionLevels Pollution { get; set; }

        public ServiceCoverage Services { get; set; }

        public TaxRates Taxes { get; set; }

        /// <summary>0–1. Share of the population affected by crime, normalised.</summary>
        public double CrimeRate { get; set; }

        /// <summary>0–1. Share of the population with an unresolved health problem.</summary>
        public double SickRate { get; set; }

        /// <summary>Mean land value per cell, in game currency.</summary>
        public double AverageLandValue { get; set; }

        /// <summary>Fractional change in land value over <c>indices.gentrificationWindowMonths</c>.</summary>
        public double LandValueTrend { get; set; }

        /// <summary>Mean residential rent, in game currency.</summary>
        public double AverageRent { get; set; }

        /// <summary>Fractional change in rent over the same window as <see cref="LandValueTrend"/>.</summary>
        public double RentTrend { get; set; }

        /// <summary>Rent as a share of mean household income, 0–1+. The cost-of-living signal.</summary>
        public double RentBurden { get; set; }

        /// <summary>
        /// Mean daily spend per household on keeping its home standing, in game currency. The game's
        /// district panel shows this as "upkeep".
        /// </summary>
        public double AverageHouseholdUpkeep { get; set; }

        /// <summary>
        /// Mean daily spend per household on goods, in game currency. The game's district panel shows
        /// this as "resources".
        /// </summary>
        public double AverageHouseholdResourceSpend { get; set; }

        /// <summary>
        /// Mean daily utility bill per household, in game currency: electricity, water, sewage and
        /// garbage charged at the player's own fee rates. The game's district panel shows this as
        /// "fees".
        /// </summary>
        /// <remarks>
        /// The only cost line here the player sets directly, which makes it the one that turns a
        /// budget slider into a political consequence. Billed on <i>fulfilled</i> consumption, so a
        /// district the grid never reached is not charged for the power it did not receive.
        /// </remarks>
        public double AverageHouseholdFees { get; set; }

        /// <summary>
        /// Share of daily household income left after rent, upkeep, resources and utility fees. 1 is
        /// "nothing is spent"; 0 is "every unit earned is committed"; negative means households are
        /// eating into savings.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A ratio rather than a currency figure, for the same reason <see cref="RentBurden"/> is one:
        /// game currency has no fixed scale across cities or across patches, and an engine coefficient
        /// tuned against an absolute margin would silently retune itself whenever the economy did.
        /// </para>
        /// <para>
        /// Uncapped in both directions, deliberately. Clamping at zero would hide the households the
        /// cost-of-living issue most wants to find, and clamping at one would hide a district where
        /// nobody pays rent at all.
        /// </para>
        /// <para>
        /// All four cost lines the game itself bills a household are in it, so this is the whole of
        /// measurable household pressure rather than a floor on it. What it still cannot see is
        /// anything the game does not charge per household — it is the household's ledger, not the
        /// city's.
        /// </para>
        /// </remarks>
        public double DisposableMargin { get; set; }

        /// <summary>Share of trips taken on public transit, 0–1.</summary>
        public double TransitRidership { get; set; }

        /// <summary>Mean one-way commute in minutes.</summary>
        public double AverageCommuteMinutes { get; set; }

        /// <summary>Road congestion, 0 (free-flowing) – 1 (gridlock).</summary>
        public double TrafficCongestion { get; set; }

        /// <summary>
        /// What the game's city statistics screen shows: homelessness, migration, births, deaths and
        /// the garbage production rate. City-only — see the type's own remarks.
        /// </summary>
        public CityStatistics Statistics { get; set; }

        /// <summary>Tourists, attractiveness and lodging. City-only.</summary>
        public TourismLevels Tourism { get; set; }

        /// <summary>Milestone level, experience and progress. City-only.</summary>
        public ProgressionState Progression { get; set; }

        /// <summary>
        /// Garbage sitting uncollected at producers, summed from
        /// <c>Game.Buildings.GarbageProducer.m_Garbage</c>.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="CityStatistics.GarbageProductionRate"/>, and the distinction is
        /// the whole political point: production is what the city makes, this is what nobody has come
        /// to collect. Buildings carry <c>CurrentDistrict</c>, so unlike every statistic in
        /// <see cref="Statistics"/> this one is genuinely per-district. It is <i>not</i> the
        /// "stored garbage" figure the game's infoview shows — that comes from a private job — so
        /// prose written against it must say "uncollected", never "landfill".
        /// </remarks>
        public double UncollectedGarbage { get; set; }

        /// <summary>
        /// Buildings contributing attractiveness, counted from
        /// <c>Game.Buildings.AttractivenessProvider</c>. Genuinely per-district.
        /// </summary>
        public int AttractionCount { get; set; }

        /// <summary>
        /// Signature buildings, counted from the <c>Game.Buildings.Signature</c> tag. Genuinely
        /// per-district.
        /// </summary>
        /// <remarks>
        /// This is what the rework plan called <c>LandmarkCount</c>. <b>There is no landmark concept
        /// in the game</b> — the only two occurrences of the word in <c>Game.dll</c> are DLC id lines
        /// (scout 0004 §5.3) — so the field is named for what is actually counted rather than
        /// shipping a plausible name over a different quantity.
        /// </remarks>
        public int SignatureBuildingCount { get; set; }

        /// <summary>
        /// Unlocked feature prefab names, sorted ordinal ascending.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A feature carries no id of its own — <c>Game.Prefabs.FeatureData</c> is a zero-field tag —
        /// so the identity is the prefab and the only stable, sortable, serializable id is
        /// <c>PrefabSystem.GetPrefabName</c>. There is no <c>FeatureType</c> enum and no hash
        /// (scout 0004 §6.3).
        /// </para>
        /// <para>
        /// A list, not a scalar, and therefore <b>not recorded in the metric history</b>: a trigger
        /// may test whether a feature is present today, but there is no historical series behind it
        /// and so no honest <c>delta</c> or <c>windowMonths</c> read. Same for
        /// <see cref="IndustryTaxRates"/>.
        /// </para>
        /// </remarks>
        public List<string> UnlockedFeatureIds { get; set; } = new List<string>();

        /// <summary>
        /// Per-resource tax rates for the three areas that expose one, sorted by
        /// <c>(Area, ResourceIndex)</c>. City-only: <c>TaxSystem</c> has no per-district,
        /// per-resource overload.
        /// </summary>
        public List<ResourceTaxRate> IndustryTaxRates { get; set; } = new List<ResourceTaxRate>();

        /// <summary>Active policy ids, sorted ascending. Stable ids, not display names.</summary>
        public List<string> ActivePolicyIds { get; set; } = new List<string>();

        /// <summary>Disasters in the last 12 months, sorted ascending.</summary>
        public List<string> RecentDisasterIds { get; set; } = new List<string>();

        /// <summary>Mandate ids currently being monitored, sorted ascending.</summary>
        public List<string> InProgressMandateIds { get; set; } = new List<string>();

        /// <summary>Derived indices for this snapshot. Computed by the indices packet, not the sensor.</summary>
        public DerivedIndices Indices { get; set; } = new DerivedIndices();

        /// <summary>
        /// Ordered by <see cref="DistrictSnapshot.Id"/>. Order is part of the contract: the engine
        /// iterates this list, and an unstable order would make results depend on ECS chunk layout.
        /// </summary>
        public List<DistrictSnapshot> Districts { get; set; } = new List<DistrictSnapshot>();
    }

    /// <summary>
    /// One district's measured state. Districts are real ECS entities (Scout 0001 §2).
    /// </summary>
    /// <remarks>
    /// Every numeric field here is best-effort. When a metric cannot be resolved per district the
    /// sensor copies the city value, sets <see cref="HasCityFallbacks"/>, and appends the property
    /// name to <see cref="CityFallbackFields"/> so the dashboard can refuse to present it as a local
    /// fact and the mandate packet can refuse to score against it.
    /// </remarks>
    public sealed class DistrictSnapshot
    {
        /// <summary>Stable identifier, used as the entity id in seeded sub-streams.</summary>
        public string Id { get; set; } = "";

        /// <summary>The player's name for the district, shown in the dashboard.</summary>
        public string Name { get; set; } = "";

        public int Population { get; set; }

        public int Households { get; set; }

        /// <summary>0–100.</summary>
        public double Happiness { get; set; }

        /// <summary>0–1.</summary>
        public double Unemployment { get; set; }

        public WealthDistribution Wealth { get; set; }

        public EducationDistribution Education { get; set; }

        public AgeDistribution Age { get; set; }

        public PollutionLevels Pollution { get; set; }

        public ServiceCoverage Services { get; set; }

        /// <summary>0–1.</summary>
        public double CrimeRate { get; set; }

        /// <summary>0–1.</summary>
        public double SickRate { get; set; }

        public double AverageLandValue { get; set; }

        public double LandValueTrend { get; set; }

        public double AverageRent { get; set; }

        public double RentTrend { get; set; }

        /// <summary>0–1+.</summary>
        public double RentBurden { get; set; }

        /// <summary>Mean daily household upkeep spend, in game currency.</summary>
        public double AverageHouseholdUpkeep { get; set; }

        /// <summary>Mean daily household spend on goods, in game currency.</summary>
        public double AverageHouseholdResourceSpend { get; set; }

        /// <summary>Mean daily household utility bill, in game currency.</summary>
        public double AverageHouseholdFees { get; set; }

        /// <summary>
        /// Share of daily household income left after rent, upkeep, resources and fees. Signed; see
        /// <see cref="CitySnapshot.DisposableMargin"/>.
        /// </summary>
        public double DisposableMargin { get; set; }

        /// <summary>0–1.</summary>
        public double TransitRidership { get; set; }

        public double AverageCommuteMinutes { get; set; }

        /// <summary>0–1.</summary>
        public double TrafficCongestion { get; set; }

        /// <summary>
        /// Garbage sitting uncollected in this district. See
        /// <see cref="CitySnapshot.UncollectedGarbage"/>.
        /// </summary>
        public double UncollectedGarbage { get; set; }

        /// <summary>Buildings contributing attractiveness in this district.</summary>
        public int AttractionCount { get; set; }

        /// <summary>Signature buildings in this district.</summary>
        public int SignatureBuildingCount { get; set; }

        // Deliberately absent: Statistics, Tourism, Progression and the two id lists. Every value in
        // them is city-only at source — CityStatisticsSystem is keyed by (StatisticType, parameter)
        // with no district dimension, Tourism exists only on the city entity, and a district has no
        // milestone (scout 0004 §1.4, §5.2, §9). Mirroring them here would mean writing the city's
        // number onto every district and marking the whole block as a fallback on every capture
        // forever, which is noise that teaches a reader nothing. The three counts above are mirrored
        // precisely because they are the ones the game really does resolve per district: their
        // buildings carry Game.Areas.CurrentDistrict.

        /// <summary>
        /// True when one or more fields on this district fell back to a city-wide value because the
        /// per-district metric was unavailable. The dashboard should not present these as local facts.
        /// </summary>
        public bool HasCityFallbacks { get; set; }

        /// <summary>
        /// Names of the properties that fell back, sorted ascending — e.g. <c>"AverageRent"</c>.
        /// Empty when <see cref="HasCityFallbacks"/> is false.
        /// </summary>
        public List<string> CityFallbackFields { get; set; } = new List<string>();
    }
}
