using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// Turns raw sensor readings into a <see cref="CitySnapshot"/>: applies the city fallback for
    /// every unmeasured district field, records which fields fell back, and sorts everything the
    /// contract says must be sorted.
    ///
    /// <para>
    /// This is where §6's best-effort rule is actually enforced, and it is pure by design. Sorting
    /// and fallback marking are the two places a sensor bug becomes an engine determinism bug —
    /// ECS chunk order is not stable, and a city number presented as a local fact would let the
    /// mandate packet score a district against a figure that was never measured there.
    /// </para>
    /// </summary>
    public static class SnapshotAssembly
    {
        // Property names on DistrictSnapshot. Written out rather than reflected: nameof() on another
        // assembly's members is checked at compile time, and a typo here would silently produce a
        // fallback marker no consumer recognises.
        private const string FieldPopulation = "Population";
        private const string FieldHouseholds = "Households";
        private const string FieldHappiness = "Happiness";
        private const string FieldUnemployment = "Unemployment";
        private const string FieldWealth = "Wealth";
        private const string FieldEducation = "Education";
        private const string FieldAge = "Age";
        private const string FieldPollution = "Pollution";
        private const string FieldServices = "Services";
        private const string FieldCrimeRate = "CrimeRate";
        private const string FieldSickRate = "SickRate";
        private const string FieldAverageLandValue = "AverageLandValue";
        private const string FieldLandValueTrend = "LandValueTrend";
        private const string FieldAverageRent = "AverageRent";
        private const string FieldRentTrend = "RentTrend";
        private const string FieldRentBurden = "RentBurden";
        private const string FieldTransitRidership = "TransitRidership";
        private const string FieldAverageCommuteMinutes = "AverageCommuteMinutes";
        private const string FieldTrafficCongestion = "TrafficCongestion";

        /// <summary>
        /// Builds the snapshot. <paramref name="city"/> and <paramref name="districts"/> may be
        /// null or empty — a capture taken before a save is loaded produces an empty but valid
        /// snapshot rather than throwing, because a sensor that can throw can take the game down.
        /// </summary>
        public static CitySnapshot Build(SimDate date, CityReading city, IList<DistrictReading> districts)
        {
            if (city == null) city = new CityReading();

            var snapshot = new CitySnapshot
            {
                Date = date,
                Population = city.Population ?? 0,
                Households = city.Households ?? 0,
                Happiness = city.Happiness ?? 0.0,
                Unemployment = city.Unemployment ?? 0.0,
                Money = city.Money ?? 0L,
                Income = city.Income ?? 0L,
                Expenses = city.Expenses ?? 0L,
                Debt = city.Debt ?? 0L,
                Wealth = city.Wealth ?? new WealthDistribution(0.0, 0.0, 0.0),
                Education = city.Education ?? new EducationDistribution(0.0, 0.0, 0.0, 0.0, 0.0),
                Age = city.Age ?? new AgeDistribution(0.0, 0.0, 0.0, 0.0),
                Pollution = city.Pollution ?? new PollutionLevels(0.0, 0.0, 0.0, 0.0),
                Services = city.Services ?? new ServiceCoverage(0, 0, 0, 0, 0, 0, 0, 0, 0),
                Taxes = city.Taxes ?? new TaxRates(0.0, 0.0, 0.0, 0.0),
                CrimeRate = city.CrimeRate ?? 0.0,
                SickRate = city.SickRate ?? 0.0,
                AverageLandValue = city.AverageLandValue ?? 0.0,
                LandValueTrend = city.LandValueTrend ?? 0.0,
                AverageRent = city.AverageRent ?? 0.0,
                RentTrend = city.RentTrend ?? 0.0,
                RentBurden = city.RentBurden ?? 0.0,
                TransitRidership = city.TransitRidership ?? 0.0,
                AverageCommuteMinutes = city.AverageCommuteMinutes ?? 0.0,
                TrafficCongestion = city.TrafficCongestion ?? 0.0,
            };

            snapshot.BudgetBalance = snapshot.Income - snapshot.Expenses;
            snapshot.ActivePolicyIds = SortedCopy(city.ActivePolicyIds);
            snapshot.RecentDisasterIds = SortedCopy(city.RecentDisasterIds);

            // InProgressMandateIds is owned by the engine, not by a sensor: the mod cannot see a
            // mandate. Left empty for the caller that does know.
            snapshot.InProgressMandateIds = new List<string>();

            // Indices are computed by the indices packet from this snapshot, not measured.
            snapshot.Indices = new DerivedIndices();

            if (districts != null)
            {
                for (int i = 0; i < districts.Count; i++)
                {
                    DistrictReading reading = districts[i];
                    if (reading == null || string.IsNullOrEmpty(reading.Id)) continue;
                    snapshot.Districts.Add(BuildDistrict(reading, snapshot));
                }
            }

            // Contractual: CitySnapshot.Districts is ordered by Id. Ordinal, not culture-aware — a
            // machine with a Turkish locale must produce the same order as everyone else's.
            snapshot.Districts.Sort(CompareDistrictById);

            return snapshot;
        }

        private static int CompareDistrictById(DistrictSnapshot a, DistrictSnapshot b) =>
            string.CompareOrdinal(a.Id, b.Id);

        private static List<string> SortedCopy(List<string> source)
        {
            var copy = source == null ? new List<string>() : new List<string>(source);
            copy.Sort(StringComparer.Ordinal);
            return copy;
        }

        private static DistrictSnapshot BuildDistrict(DistrictReading reading, CitySnapshot city)
        {
            var fallbacks = new List<string>();
            var district = new DistrictSnapshot
            {
                Id = reading.Id,
                Name = string.IsNullOrEmpty(reading.Name) ? reading.Id : reading.Name,
            };

            district.Population = Resolve(reading.Population, city.Population, FieldPopulation, fallbacks);
            district.Households = Resolve(reading.Households, city.Households, FieldHouseholds, fallbacks);
            district.Happiness = Resolve(reading.Happiness, city.Happiness, FieldHappiness, fallbacks);
            district.Unemployment = Resolve(reading.Unemployment, city.Unemployment, FieldUnemployment, fallbacks);
            district.Wealth = Resolve(reading.Wealth, city.Wealth, FieldWealth, fallbacks);
            district.Education = Resolve(reading.Education, city.Education, FieldEducation, fallbacks);
            district.Age = Resolve(reading.Age, city.Age, FieldAge, fallbacks);
            district.Pollution = Resolve(reading.Pollution, city.Pollution, FieldPollution, fallbacks);
            district.Services = Resolve(reading.Services, city.Services, FieldServices, fallbacks);
            district.CrimeRate = Resolve(reading.CrimeRate, city.CrimeRate, FieldCrimeRate, fallbacks);
            district.SickRate = Resolve(reading.SickRate, city.SickRate, FieldSickRate, fallbacks);
            district.AverageLandValue = Resolve(reading.AverageLandValue, city.AverageLandValue, FieldAverageLandValue, fallbacks);
            district.LandValueTrend = Resolve(reading.LandValueTrend, city.LandValueTrend, FieldLandValueTrend, fallbacks);
            district.AverageRent = Resolve(reading.AverageRent, city.AverageRent, FieldAverageRent, fallbacks);
            district.RentTrend = Resolve(reading.RentTrend, city.RentTrend, FieldRentTrend, fallbacks);
            district.RentBurden = Resolve(reading.RentBurden, city.RentBurden, FieldRentBurden, fallbacks);
            district.TransitRidership = Resolve(reading.TransitRidership, city.TransitRidership, FieldTransitRidership, fallbacks);
            district.AverageCommuteMinutes = Resolve(reading.AverageCommuteMinutes, city.AverageCommuteMinutes, FieldAverageCommuteMinutes, fallbacks);
            district.TrafficCongestion = Resolve(reading.TrafficCongestion, city.TrafficCongestion, FieldTrafficCongestion, fallbacks);

            fallbacks.Sort(StringComparer.Ordinal);
            district.CityFallbackFields = fallbacks;
            district.HasCityFallbacks = fallbacks.Count > 0;

            return district;
        }

        private static T Resolve<T>(T? measured, T cityValue, string fieldName, List<string> fallbacks)
            where T : struct
        {
            if (measured.HasValue) return measured.Value;
            fallbacks.Add(fieldName);
            return cityValue;
        }
    }
}
