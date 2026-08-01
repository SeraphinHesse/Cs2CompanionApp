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
        /// 2 as of the M2 contract freeze: v1 carried only population, happiness, unemployment and
        /// money.
        /// </summary>
        public int SchemaVersion { get; set; } = 2;

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

        /// <summary>Share of trips taken on public transit, 0–1.</summary>
        public double TransitRidership { get; set; }

        /// <summary>Mean one-way commute in minutes.</summary>
        public double AverageCommuteMinutes { get; set; }

        /// <summary>Road congestion, 0 (free-flowing) – 1 (gridlock).</summary>
        public double TrafficCongestion { get; set; }

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

        /// <summary>0–1.</summary>
        public double TransitRidership { get; set; }

        public double AverageCommuteMinutes { get; set; }

        /// <summary>0–1.</summary>
        public double TrafficCongestion { get; set; }

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
