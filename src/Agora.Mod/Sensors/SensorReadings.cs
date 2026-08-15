// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// What the sensors measured, before assembly into a <see cref="CitySnapshot"/>. Contains no
    /// game types, so the whole assembly step — fallbacks, ordering, trend maths — is reasoned about
    /// and tested without the game.
    ///
    /// <para>
    /// Every field is nullable, and null means exactly one thing: <b>not measured</b>. It is never a
    /// zero in disguise. <see cref="SnapshotAssembly"/> turns a null district field into the city
    /// value plus an entry in <c>CityFallbackFields</c>; a null city field becomes 0, because the
    /// city snapshot has nowhere further to fall back to.
    /// </para>
    /// </summary>
    public sealed class CityReading
    {
        public int? Population;
        public int? Households;
        public double? Happiness;
        public double? Unemployment;
        public long? Money;
        public long? Income;
        public long? Expenses;
        public long? Debt;
        public WealthDistribution? Wealth;
        public EducationDistribution? Education;
        public AgeDistribution? Age;
        public PollutionLevels? Pollution;
        public ServiceCoverage? Services;
        public TaxRates? Taxes;
        public double? CrimeRate;
        public double? SickRate;
        public double? AverageLandValue;
        public double? LandValueTrend;
        public double? AverageRent;
        public double? RentTrend;
        public double? RentBurden;
        public double? AverageHouseholdUpkeep;
        public double? AverageHouseholdResourceSpend;
        public double? AverageHouseholdFees;
        public double? DisposableMargin;
        public double? TransitRidership;
        public double? AverageCommuteMinutes;
        public double? TrafficCongestion;

        // --- The city-statistics pass (snapshot v4) --------------------------------------------
        //
        // AGORA-SEAM(wave-1): these six fields plus the two lists below are the whole interface
        // between the three new sensor systems and the merge/assembly half. AgoraStatisticsSensorSystem
        // writes Statistics and UncollectedGarbage; AgoraTourismSensorSystem writes Tourism,
        // AttractionCount and SignatureBuildingCount; AgoraProgressionSensorSystem writes Progression,
        // UnlockedFeatureIds and IndustryTaxRates. No sensor writes a field another sensor owns — the
        // merge resolves a collision by source order, but a collision here would mean two sensors
        // disagreed about a measurement, which is a bug rather than something to resolve.

        /// <summary>Homelessness, migration, births, deaths and the garbage production rate.</summary>
        public CityStatistics? Statistics;

        /// <summary>Tourists, attractiveness and lodging.</summary>
        public TourismLevels? Tourism;

        /// <summary>Milestone level, experience and progress.</summary>
        public ProgressionState? Progression;

        /// <summary>Garbage waiting uncollected at producers. Also measured per district.</summary>
        public double? UncollectedGarbage;

        /// <summary>Buildings contributing attractiveness. Also measured per district.</summary>
        public int? AttractionCount;

        /// <summary>Signature buildings. Also measured per district.</summary>
        public int? SignatureBuildingCount;

        /// <summary>
        /// Unlocked feature prefab names. Assembly sorts them; the sensor need not.
        /// </summary>
        /// <remarks>
        /// Empty and "not measured" are the same value here, unlike every nullable above. That is
        /// tolerable only because the list is never a fallback source: a district does not have one,
        /// and a city with no unlocks and a city whose sensor is blind both read as "no features
        /// unlocked", which for a trigger asking whether a feature is present is the same answer.
        /// </remarks>
        public List<string> UnlockedFeatureIds = new List<string>();

        /// <summary>Per-resource tax rates. Assembly sorts them by <c>(Area, ResourceIndex)</c>.</summary>
        public List<ResourceTaxRate> IndustryTaxRates = new List<ResourceTaxRate>();

        /// <summary>Active policy ids. Assembly sorts them; the sensor need not.</summary>
        public List<string> ActivePolicyIds = new List<string>();

        /// <summary>Disaster ids seen in the last twelve months. Assembly sorts them.</summary>
        public List<string> RecentDisasterIds = new List<string>();
    }

    /// <summary>
    /// One district's measurements. See <see cref="CityReading"/> for the null convention — it
    /// carries more weight here, because null is what produces the best-effort marking §6 requires.
    /// </summary>
    public sealed class DistrictReading
    {
        /// <summary>Stable id assigned by <see cref="DistrictIdentityMap"/>. Never empty.</summary>
        public string Id = "";

        /// <summary>The player's name for the district, or the id when it has none.</summary>
        public string Name = "";

        public int? Population;
        public int? Households;
        public double? Happiness;
        public double? Unemployment;
        public WealthDistribution? Wealth;
        public EducationDistribution? Education;
        public AgeDistribution? Age;
        public PollutionLevels? Pollution;
        public ServiceCoverage? Services;
        public double? CrimeRate;
        public double? SickRate;
        public double? AverageLandValue;
        public double? LandValueTrend;
        public double? AverageRent;
        public double? RentTrend;
        public double? RentBurden;
        public double? AverageHouseholdUpkeep;
        public double? AverageHouseholdResourceSpend;
        public double? AverageHouseholdFees;
        public double? DisposableMargin;
        public double? TransitRidership;
        public double? AverageCommuteMinutes;
        public double? TrafficCongestion;

        // AGORA-SEAM(wave-1): the three v4 fields that are genuinely resolvable per district, because
        // the buildings behind them carry Game.Areas.CurrentDistrict. Nothing else from the
        // city-statistics pass appears here — CityStatisticsSystem has no district dimension at all,
        // so the rest is city-only at source rather than merely unmeasured here.

        /// <summary>Garbage waiting uncollected in this district.</summary>
        public double? UncollectedGarbage;

        /// <summary>Buildings contributing attractiveness in this district.</summary>
        public int? AttractionCount;

        /// <summary>Signature buildings in this district.</summary>
        public int? SignatureBuildingCount;
    }
}
