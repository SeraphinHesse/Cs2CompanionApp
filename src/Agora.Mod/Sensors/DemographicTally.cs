using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// Counters filled while walking one district's residents, and the conversions from those
    /// counters into the distributions <c>DistrictSnapshot</c> declares.
    ///
    /// <para>
    /// Pure by construction — it never sees an <c>Entity</c>. The ECS walk decides <i>which</i>
    /// citizens belong to a district; this decides what their composition means, which is the part
    /// worth testing.
    /// </para>
    ///
    /// <para>
    /// Counts are kept in arrays indexed by enum value, never in a dictionary. Iterating a
    /// dictionary to build a distribution is the classic way a snapshot stops being reproducible.
    /// </para>
    /// </summary>
    public sealed class DemographicTally
    {
        private readonly long[] _ageCounts = new long[4];
        private readonly long[] _educationCounts = new long[5];

        /// <summary>Residents counted. Drives <c>DistrictSnapshot.Population</c>.</summary>
        public long Residents { get; private set; }

        /// <summary>Households counted. Drives <c>DistrictSnapshot.Households</c>.</summary>
        public long Households { get; private set; }

        /// <summary>
        /// Household wealth samples, in game currency, in walk order. Sorted by the caller before
        /// any quantile is taken — see <see cref="WealthTiering"/>.
        /// </summary>
        public List<double> HouseholdWealth { get; } = new List<double>();

        private double _happinessSum;
        private long _happinessSamples;
        private long _sickCitizens;
        private long _workableCitizens;
        private long _employedCitizens;
        private double _commuteMinutesSum;
        private long _commuteSamples;
        private double _rentSum;
        private long _rentSamples;
        private double _dailySalarySum;
        private long _dailySalarySamples;

        /// <summary>
        /// Records one resident.
        /// </summary>
        /// <param name="age">Age band; out-of-range values are ignored rather than throwing.</param>
        /// <param name="education">Education tier; likewise.</param>
        /// <param name="happiness">Citizen happiness on the game's 0–100 scale.</param>
        /// <param name="isSick">True when the citizen has an unresolved health problem.</param>
        /// <param name="isWorkable">
        /// True when the citizen is of working age and not a full-time student — the denominator of
        /// the unemployment rate. A city where every adult is studying has no unemployment, not 100%.
        /// </param>
        /// <param name="isEmployed">True when the citizen holds a workplace.</param>
        /// <param name="commuteMinutes">
        /// Last one-way commute in minutes, or null when the citizen has never commuted. Null is not
        /// a zero-minute commute.
        /// </param>
        public void AddCitizen(AgeBand age, EducationTier education, double happiness, bool isSick,
                               bool isWorkable, bool isEmployed, double? commuteMinutes)
        {
            Residents++;

            int ageIndex = (int)age;
            if (ageIndex >= 0 && ageIndex < _ageCounts.Length) _ageCounts[ageIndex]++;

            int educationIndex = (int)education;
            if (educationIndex >= 0 && educationIndex < _educationCounts.Length) _educationCounts[educationIndex]++;

            if (!double.IsNaN(happiness))
            {
                _happinessSum += happiness;
                _happinessSamples++;
            }

            if (isSick) _sickCitizens++;
            if (isWorkable) _workableCitizens++;
            if (isWorkable && isEmployed) _employedCitizens++;

            if (commuteMinutes.HasValue && !double.IsNaN(commuteMinutes.Value) && commuteMinutes.Value > 0.0)
            {
                _commuteMinutesSum += commuteMinutes.Value;
                _commuteSamples++;
            }
        }

        /// <summary>
        /// Records one household.
        /// </summary>
        /// <param name="wealth">Liquid household resources, in game currency.</param>
        /// <param name="rent">Rent charged for the property, or null for a household that pays none.</param>
        /// <param name="dailySalary">Household salary for the last day, or null when unknown.</param>
        public void AddHousehold(double wealth, double? rent, double? dailySalary)
        {
            Households++;
            HouseholdWealth.Add(wealth);

            if (rent.HasValue && rent.Value > 0.0)
            {
                _rentSum += rent.Value;
                _rentSamples++;
            }

            if (dailySalary.HasValue && dailySalary.Value > 0.0)
            {
                _dailySalarySum += dailySalary.Value;
                _dailySalarySamples++;
            }
        }

        /// <summary>Age composition, or null when nobody was counted.</summary>
        public AgeDistribution? AgeShares()
        {
            if (Residents <= 0) return null;

            var shares = new double[4];
            SensorMath.SharesOf(_ageCounts, shares);
            return new AgeDistribution(shares[0], shares[1], shares[2], shares[3]);
        }

        /// <summary>Education composition, or null when nobody was counted.</summary>
        public EducationDistribution? EducationShares()
        {
            if (Residents <= 0) return null;

            var shares = new double[5];
            SensorMath.SharesOf(_educationCounts, shares);
            return new EducationDistribution(shares[0], shares[1], shares[2], shares[3], shares[4]);
        }

        /// <summary>
        /// Wealth composition against city-wide cut points, or null when no households were counted.
        /// </summary>
        /// <remarks>
        /// The cuts are deliberately city-wide. Cutting each district at its own quantiles would
        /// make every district exactly one-third low, one-third middle, one-third high, which is the
        /// opposite of what the bloc model needs — the whole point is that a rich district differs
        /// in composition from a poor one.
        /// </remarks>
        public WealthDistribution? WealthShares(WealthCuts cuts)
        {
            if (Households <= 0) return null;

            var counts = new long[3];
            for (int i = 0; i < HouseholdWealth.Count; i++)
            {
                counts[(int)cuts.TierOf(HouseholdWealth[i])]++;
            }

            var shares = new double[3];
            SensorMath.SharesOf(counts, shares);
            return new WealthDistribution(shares[0], shares[1], shares[2]);
        }

        /// <summary>Mean citizen happiness (0–100), or null when nobody was counted.</summary>
        public double? MeanHappiness() =>
            _happinessSamples <= 0 ? (double?)null : SensorMath.Clamp(_happinessSum / _happinessSamples, 0.0, 100.0);

        /// <summary>Share of residents with an unresolved health problem, or null when nobody was counted.</summary>
        public double? SickRate() =>
            Residents <= 0 ? (double?)null : SensorMath.Clamp01(SensorMath.SafeDivide(_sickCitizens, Residents));

        /// <summary>
        /// Share of workable residents without a workplace, or null when nobody is workable — an
        /// all-retiree district has no unemployment rate, rather than one of zero.
        /// </summary>
        public double? Unemployment() =>
            _workableCitizens <= 0
                ? (double?)null
                : SensorMath.Clamp01(1.0 - SensorMath.SafeDivide(_employedCitizens, _workableCitizens));

        /// <summary>Mean one-way commute in minutes, or null when nobody commuted.</summary>
        public double? MeanCommuteMinutes() =>
            _commuteSamples <= 0 ? (double?)null : _commuteMinutesSum / _commuteSamples;

        /// <summary>Mean rent in game currency, or null when no household pays rent.</summary>
        public double? MeanRent() =>
            _rentSamples <= 0 ? (double?)null : _rentSum / _rentSamples;

        /// <summary>
        /// Rent as a share of household income over the same period, or null when either side is
        /// unmeasured. Uncapped above 1: a household paying more rent than it earns is a real and
        /// politically interesting state, and clamping it would hide exactly the signal the
        /// cost-of-living issue is meant to read.
        /// </summary>
        public double? RentBurden(double rentPeriodDays)
        {
            if (_rentSamples <= 0 || _dailySalarySamples <= 0 || rentPeriodDays <= 0.0) return null;

            double meanRent = _rentSum / _rentSamples;
            double meanPeriodIncome = (_dailySalarySum / _dailySalarySamples) * rentPeriodDays;
            if (meanPeriodIncome <= 0.0) return null;

            return SensorMath.SafeDivide(meanRent, meanPeriodIncome);
        }
    }

    /// <summary>
    /// The two city-wide wealth cut points that split households into low, middle and high tiers.
    /// Built once per capture from every household in the city.
    /// </summary>
    public readonly struct WealthCuts
    {
        /// <summary>Wealth at or below this is <see cref="WealthTier.Low"/>.</summary>
        public double LowCut { get; }

        /// <summary>Wealth above <see cref="LowCut"/> and at or below this is <see cref="WealthTier.Middle"/>.</summary>
        public double MiddleCut { get; }

        public WealthCuts(double lowCut, double middleCut)
        {
            LowCut = lowCut;
            MiddleCut = Math.Max(lowCut, middleCut);
        }

        public WealthTier TierOf(double wealth)
        {
            if (wealth <= LowCut) return WealthTier.Low;
            if (wealth <= MiddleCut) return WealthTier.Middle;
            return WealthTier.High;
        }
    }

    /// <summary>
    /// Derives <see cref="WealthCuts"/> from the city's household wealth distribution, using the
    /// quantiles in <c>blocs.wealthTierThresholds</c>.
    /// </summary>
    public static class WealthTiering
    {
        /// <summary>
        /// Sorts a copy of <paramref name="citySamples"/> ascending and cuts it at the given
        /// quantiles. Fewer than two thresholds, or an empty sample, yields cuts at zero — every
        /// household then reads as high-tier, which is visibly wrong rather than quietly plausible.
        /// </summary>
        public static WealthCuts FromSamples(IList<double> citySamples, double[] quantiles)
        {
            if (citySamples == null || citySamples.Count == 0 || quantiles == null || quantiles.Length < 2)
            {
                return new WealthCuts(0.0, 0.0);
            }

            var sorted = new List<double>(citySamples);
            sorted.Sort();

            double low = SensorMath.QuantileOfSorted(sorted, quantiles[0]);
            double middle = SensorMath.QuantileOfSorted(sorted, quantiles[1]);
            return new WealthCuts(low, middle);
        }
    }
}
