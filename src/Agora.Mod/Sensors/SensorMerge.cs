// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// Folds the separate sensor families' readings into one <see cref="CityReading"/> and one
    /// district reading per district.
    ///
    /// <para>
    /// Each family owns a disjoint set of fields, so in practice nothing collides. The merge still
    /// resolves collisions by a fixed rule — <b>the first source in the supplied order that measured
    /// the field wins</b> — rather than by whichever sensor happened to run last. "Last writer wins"
    /// would make the snapshot depend on system update order, which is exactly the kind of
    /// invisible coupling that turns a reproducible save into a drifting one.
    /// </para>
    ///
    /// <para>Pure: no game types, no ECS, no clock.</para>
    /// </summary>
    public static class SensorMerge
    {
        /// <summary>
        /// Merges city readings in priority order. Null sources are skipped; a field left null by
        /// every source stays null and is resolved to a default during assembly.
        /// </summary>
        public static CityReading MergeCity(IList<CityReading> sources)
        {
            var merged = new CityReading();
            if (sources == null) return merged;

            for (int i = 0; i < sources.Count; i++)
            {
                CityReading source = sources[i];
                if (source == null) continue;

                merged.Population = merged.Population ?? source.Population;
                merged.Households = merged.Households ?? source.Households;
                merged.Happiness = merged.Happiness ?? source.Happiness;
                merged.Unemployment = merged.Unemployment ?? source.Unemployment;
                merged.Money = merged.Money ?? source.Money;
                merged.Income = merged.Income ?? source.Income;
                merged.Expenses = merged.Expenses ?? source.Expenses;
                merged.Debt = merged.Debt ?? source.Debt;
                merged.Wealth = merged.Wealth ?? source.Wealth;
                merged.Education = merged.Education ?? source.Education;
                merged.Age = merged.Age ?? source.Age;
                merged.Pollution = merged.Pollution ?? source.Pollution;
                merged.Services = merged.Services ?? source.Services;
                merged.Taxes = merged.Taxes ?? source.Taxes;
                merged.CrimeRate = merged.CrimeRate ?? source.CrimeRate;
                merged.SickRate = merged.SickRate ?? source.SickRate;
                merged.AverageLandValue = merged.AverageLandValue ?? source.AverageLandValue;
                merged.LandValueTrend = merged.LandValueTrend ?? source.LandValueTrend;
                merged.AverageRent = merged.AverageRent ?? source.AverageRent;
                merged.RentTrend = merged.RentTrend ?? source.RentTrend;
                merged.RentBurden = merged.RentBurden ?? source.RentBurden;
                merged.AverageHouseholdUpkeep = merged.AverageHouseholdUpkeep ?? source.AverageHouseholdUpkeep;
                merged.AverageHouseholdResourceSpend =
                    merged.AverageHouseholdResourceSpend ?? source.AverageHouseholdResourceSpend;
                merged.AverageHouseholdFees = merged.AverageHouseholdFees ?? source.AverageHouseholdFees;
                merged.DisposableMargin = merged.DisposableMargin ?? source.DisposableMargin;
                merged.TransitRidership = merged.TransitRidership ?? source.TransitRidership;
                merged.AverageCommuteMinutes = merged.AverageCommuteMinutes ?? source.AverageCommuteMinutes;
                merged.TrafficCongestion = merged.TrafficCongestion ?? source.TrafficCongestion;

                // The city-statistics pass (snapshot v4). Same first-source-wins rule as everything
                // above: the three sensors that write these own disjoint fields, so nothing collides
                // today, and pinning the rule means a future overlap resolves identically every run
                // instead of by whichever ECS system the scheduler happened to run last.
                merged.Statistics = merged.Statistics ?? source.Statistics;
                merged.Tourism = merged.Tourism ?? source.Tourism;
                merged.Progression = merged.Progression ?? source.Progression;
                merged.UncollectedGarbage = merged.UncollectedGarbage ?? source.UncollectedGarbage;
                merged.AttractionCount = merged.AttractionCount ?? source.AttractionCount;
                merged.SignatureBuildingCount = merged.SignatureBuildingCount ?? source.SignatureBuildingCount;

                AppendMissing(merged.ActivePolicyIds, source.ActivePolicyIds);
                AppendMissing(merged.RecentDisasterIds, source.RecentDisasterIds);
                AppendMissing(merged.UnlockedFeatureIds, source.UnlockedFeatureIds);
                AppendMissingRates(merged.IndustryTaxRates, source.IndustryTaxRates);
            }

            return merged;
        }

        /// <summary>
        /// Merges per-district readings. <paramref name="districts"/> supplies the authoritative id
        /// and name for every district and the order of the result; a district absent from every
        /// source still appears, with everything null, so it falls back wholesale rather than
        /// vanishing from the snapshot.
        /// </summary>
        public static List<DistrictReading> MergeDistricts(
            IList<KeyValuePair<string, string>> districts,
            IList<IReadOnlyDictionary<string, DistrictReading>> sources)
        {
            var merged = new List<DistrictReading>();
            if (districts == null) return merged;

            for (int d = 0; d < districts.Count; d++)
            {
                string id = districts[d].Key;
                if (string.IsNullOrEmpty(id)) continue;

                var target = new DistrictReading { Id = id, Name = districts[d].Value };

                if (sources != null)
                {
                    for (int s = 0; s < sources.Count; s++)
                    {
                        IReadOnlyDictionary<string, DistrictReading> source = sources[s];
                        if (source == null) continue;

                        DistrictReading reading;
                        if (!source.TryGetValue(id, out reading) || reading == null) continue;

                        Fold(target, reading);
                    }
                }

                merged.Add(target);
            }

            return merged;
        }

        private static void Fold(DistrictReading target, DistrictReading source)
        {
            target.Population = target.Population ?? source.Population;
            target.Households = target.Households ?? source.Households;
            target.Happiness = target.Happiness ?? source.Happiness;
            target.Unemployment = target.Unemployment ?? source.Unemployment;
            target.Wealth = target.Wealth ?? source.Wealth;
            target.Education = target.Education ?? source.Education;
            target.Age = target.Age ?? source.Age;
            target.Pollution = target.Pollution ?? source.Pollution;
            target.Services = target.Services ?? source.Services;
            target.CrimeRate = target.CrimeRate ?? source.CrimeRate;
            target.SickRate = target.SickRate ?? source.SickRate;
            target.AverageLandValue = target.AverageLandValue ?? source.AverageLandValue;
            target.LandValueTrend = target.LandValueTrend ?? source.LandValueTrend;
            target.AverageRent = target.AverageRent ?? source.AverageRent;
            target.RentTrend = target.RentTrend ?? source.RentTrend;
            target.RentBurden = target.RentBurden ?? source.RentBurden;
            target.AverageHouseholdUpkeep = target.AverageHouseholdUpkeep ?? source.AverageHouseholdUpkeep;
            target.AverageHouseholdResourceSpend =
                target.AverageHouseholdResourceSpend ?? source.AverageHouseholdResourceSpend;
            target.AverageHouseholdFees = target.AverageHouseholdFees ?? source.AverageHouseholdFees;
            target.DisposableMargin = target.DisposableMargin ?? source.DisposableMargin;
            target.TransitRidership = target.TransitRidership ?? source.TransitRidership;
            target.AverageCommuteMinutes = target.AverageCommuteMinutes ?? source.AverageCommuteMinutes;
            target.TrafficCongestion = target.TrafficCongestion ?? source.TrafficCongestion;

            // The three v4 fields the game genuinely resolves per district; the rest of the
            // city-statistics pass has no district dimension at source and so appears only above.
            target.UncollectedGarbage = target.UncollectedGarbage ?? source.UncollectedGarbage;
            target.AttractionCount = target.AttractionCount ?? source.AttractionCount;
            target.SignatureBuildingCount = target.SignatureBuildingCount ?? source.SignatureBuildingCount;
        }

        private static void AppendMissing(List<string> target, List<string> source)
        {
            if (target == null || source == null) return;

            for (int i = 0; i < source.Count; i++)
            {
                string value = source[i];
                if (string.IsNullOrEmpty(value) || target.Contains(value)) continue;
                target.Add(value);
            }
        }

        /// <summary>
        /// Appends every rate whose <c>(Area, ResourceIndex)</c> the target does not already carry.
        /// First source wins, matching every other field here — the key is the pair rather than the
        /// whole struct so that two sources disagreeing about one resource's rate resolve to a fixed
        /// answer instead of silently shipping both.
        /// </summary>
        private static void AppendMissingRates(List<ResourceTaxRate> target, List<ResourceTaxRate> source)
        {
            if (target == null || source == null) return;

            for (int i = 0; i < source.Count; i++)
            {
                ResourceTaxRate rate = source[i];
                if (ContainsRate(target, rate)) continue;
                target.Add(rate);
            }
        }

        private static bool ContainsRate(List<ResourceTaxRate> target, ResourceTaxRate rate)
        {
            for (int i = 0; i < target.Count; i++)
            {
                if (target[i].Area == rate.Area && target[i].ResourceIndex == rate.ResourceIndex) return true;
            }

            return false;
        }
    }
}
