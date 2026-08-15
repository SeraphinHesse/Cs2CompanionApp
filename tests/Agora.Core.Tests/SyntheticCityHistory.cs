// Requires the Sensors <Compile Link> lines in Agora.Core.Tests.csproj — MetricHistory and
// SnapshotRehydration. See the comment there for why they are linked rather than referenced.

using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Mod.Sensors;

namespace Agora.Core.Tests
{
    /// <summary>
    /// A synthetic multi-month city, for the tests that need a history rather than a moment.
    ///
    /// <para>
    /// Synthetic rather than recorded (<c>tests/CLAUDE.md</c>): the numbers are readable, they diff
    /// cleanly, and they do not rot when <see cref="CitySnapshot"/> gains a field. Every trend the
    /// indices packet looks for is built in deliberately — city education and population fall, so the
    /// brain-drain leg has something to find; district education rises and the low-wealth share falls,
    /// so the gentrification leg does too. A fixture that held those flat would let the golden
    /// rehydration test agree with itself on two identically empty answers.
    /// </para>
    ///
    /// <para>
    /// <b>The fields the metric ring does not record are filled with something that varies month to
    /// month.</b> A rehydrated snapshot leaves those at zero, so if <c>IndicesEngine</c> ever starts
    /// reading one off a <i>historical</i> snapshot, the two sides of the golden test stop agreeing.
    /// That is the alarm the whole arrangement exists for, and a fixture whose unrecorded fields were
    /// already zero would silence it.
    /// </para>
    ///
    /// <para>
    /// There is deliberately no recording pass here. <c>MetricHistory.RecordSnapshot</c> is the real
    /// one and it lives on the pure type precisely so this suite can drive it; a fixture that filed
    /// the series itself would be a reimplementation of the thing under test, and it would agree with
    /// itself by construction.
    /// </para>
    /// </summary>
    internal static class SyntheticCityHistory
    {
        /// <summary>The district roster, stable across every month of the fixture.</summary>
        internal static readonly string[] DistrictIds = { "district-01", "district-02", "district-03" };

        /// <summary>
        /// One month of the synthetic city. <paramref name="monthIndex"/> is months since the start
        /// of the fixture and is the only thing any value is derived from, so a given month index
        /// always produces the same snapshot.
        /// </summary>
        internal static CitySnapshot Snapshot(SimDate date, int monthIndex)
        {
            double i = monthIndex;

            var snapshot = new CitySnapshot
            {
                Date = date,

                // --- read off a historical snapshot by IndicesEngine ---------------------------
                Population = 120_000 - 1_200 * monthIndex,
                Education = new EducationDistribution(
                    0.10 + 0.004 * i,
                    0.20 + 0.002 * i,
                    0.30,
                    0.25 - 0.003 * i,
                    0.15 - 0.003 * i),

                // --- recorded, but nothing reads them off a historical snapshot ----------------
                Happiness = 40.0 + 0.5 * i,
                Unemployment = 0.05 + 0.001 * i,
                CrimeRate = 0.03 + 0.001 * i,
                Wealth = new WealthDistribution(0.40 - 0.004 * i, 0.35, 0.25 + 0.004 * i),
                Pollution = new PollutionLevels(0.10 + 0.002 * i, 0.08, 0.12, 0.05),
                Services = Services(0.60 + 0.005 * i),
                AverageCommuteMinutes = 24.0 + 0.2 * i,
                TrafficCongestion = 0.30 + 0.004 * i,
                AverageLandValue = 900.0 + 12.0 * i,
                AverageRent = 700.0 + 9.0 * i,

                // --- not recorded at all ------------------------------------------------------
                Households = 48_000 - 400 * monthIndex,
                Money = 1_000_000 + 5_000 * monthIndex,
                Income = 90_000 + 100 * monthIndex,
                Expenses = 80_000 + 150 * monthIndex,
                Age = new AgeDistribution(0.20, 0.10, 0.55, 0.15),
                Taxes = new TaxRates(0.10, 0.11, 0.12, 0.09),
                SickRate = 0.02,
                LandValueTrend = 0.02 + 0.001 * i,
                RentTrend = 0.03 + 0.001 * i,
                RentBurden = 0.28 + 0.002 * i,
                TransitRidership = 0.22 + 0.001 * i
            };

            for (int k = 0; k < DistrictIds.Length; k++) snapshot.Districts.Add(District(k, monthIndex));

            return snapshot;
        }

        private static DistrictSnapshot District(int index, int monthIndex)
        {
            double i = monthIndex;
            double k = index;

            double lowWealth = 0.45 - 0.005 * i - 0.02 * k;

            var district = new DistrictSnapshot
            {
                Id = DistrictIds[index],
                Name = DistrictIds[index],

                // --- read off a historical snapshot by IndicesEngine ---------------------------
                Education = new EducationDistribution(
                    0.15 - 0.003 * i - 0.01 * k,
                    0.25 - 0.002 * i,
                    0.30,
                    0.18 + 0.002 * i,
                    0.12 + 0.003 * i + 0.01 * k),
                Wealth = new WealthDistribution(lowWealth, 0.35, 0.65 - lowWealth),

                // --- recorded, but nothing reads them off a historical snapshot ----------------
                Population = 40_000 - 300 * monthIndex - 1_000 * index,
                Happiness = 45.0 + 0.4 * i - 2.0 * k,
                Unemployment = 0.04 + 0.001 * i + 0.005 * k,
                CrimeRate = 0.03 + 0.001 * i + 0.004 * k,
                Pollution = new PollutionLevels(0.09 + 0.002 * i, 0.07, 0.11 + 0.01 * k, 0.04),
                Services = Services(0.55 + 0.004 * i + 0.03 * k),
                AverageLandValue = 850.0 + 11.0 * i + 40.0 * k,
                AverageRent = 680.0 + 8.0 * i + 25.0 * k,

                // --- not recorded at all ------------------------------------------------------
                Households = 16_000 - 120 * monthIndex,
                Age = new AgeDistribution(0.21, 0.09, 0.54, 0.16),
                SickRate = 0.02 + 0.001 * k,
                LandValueTrend = 0.02 + 0.001 * i,

                // The rent term of the gentrification index, and it is read off the CURRENT snapshot
                // rather than the historical one — which is exactly why a non-zero value here is
                // worth having: the day it is read historically instead, the golden test says so.
                RentTrend = 0.05 + 0.01 * k,
                RentBurden = 0.27 + 0.002 * i,
                TransitRidership = 0.20 + 0.001 * i,
                AverageCommuteMinutes = 23.0 + 0.25 * i + 1.5 * k,
                TrafficCongestion = 0.28 + 0.004 * i + 0.02 * k
            };

            // One district whose services are really the city's, so the service-inequality leg has a
            // district to exclude. A property of the assembled snapshot, not of the metric ring.
            if (index == DistrictIds.Length - 1)
            {
                district.HasCityFallbacks = true;
                district.CityFallbackFields.Add("Services");
            }

            return district;
        }

        private static ServiceCoverage Services(double baseline) =>
            new ServiceCoverage(baseline, baseline + 0.02, baseline - 0.03, baseline + 0.01,
                                baseline - 0.01, baseline + 0.04, baseline, baseline + 0.03,
                                baseline - 0.05);

        /// <summary>
        /// <paramref name="count"/> consecutive months starting at <paramref name="start"/>, oldest
        /// first, with month index 0 at <paramref name="start"/>.
        /// </summary>
        internal static List<CitySnapshot> Months(SimDate start, int count)
        {
            var months = new List<CitySnapshot>(count);
            for (int i = 0; i < count; i++) months.Add(Snapshot(start.AddMonths(i), i));
            return months;
        }

        /// <summary>
        /// Files every month of <paramref name="months"/> through the real recorder, oldest first —
        /// exactly what the snapshot system does once a month.
        /// </summary>
        internal static void RecordAll(MetricHistory history, IReadOnlyList<CitySnapshot> months)
        {
            for (int i = 0; i < months.Count; i++) history.RecordSnapshot(months[i]);
        }
    }
}
