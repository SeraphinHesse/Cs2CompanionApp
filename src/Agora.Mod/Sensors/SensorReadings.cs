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
        public double? TransitRidership;
        public double? AverageCommuteMinutes;
        public double? TrafficCongestion;

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
        public double? TransitRidership;
        public double? AverageCommuteMinutes;
        public double? TrafficCongestion;
    }
}
