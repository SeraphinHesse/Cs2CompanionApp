using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Agora.Core.Contracts;

namespace Agora.Core.Stories
{
    /// <summary>
    /// The single sorted registry mapping metric ids onto <see cref="CitySnapshot"/> and
    /// <see cref="DistrictSnapshot"/> accessors.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The ids are a contract shared with a file this assembly cannot see.</b> The vocabulary wave 1
    /// shipped is declared as <c>const string</c> members on <c>Agora.Mod.Sensors.MetricHistory</c>
    /// (the wave-2 lane document calls that class <c>MetricNames</c>; the constants are on
    /// <c>MetricHistory</c> itself, and the strings are what the contract is about), and
    /// <c>MetricHistory</c> keys its series on those exact strings. <c>Agora.Core</c> may never
    /// reference <c>Agora.Mod</c>, so this registry necessarily holds a second copy of them, and two
    /// copies drift. The pin is a test: the suite compile-links <c>MetricHistory.cs</c>, so it can
    /// compare the two lists directly, and lane 2e owns that test. The constants below therefore carry
    /// the <b>same identifier names</b> as their counterparts there, so the comparison can be made
    /// member by member rather than by eye. A name may be <b>added but never renamed</b> without a
    /// migration — the sidecar fingerprint is taken over these strings sorted, the same rule that
    /// governs a seed stream name.
    /// </para>
    /// <para>
    /// <b>A <c>null</c> reading means unmeasurable and never zero.</b> That distinction is the whole
    /// reason <see cref="CheckResult.Unmeasurable"/> exists, and it cannot be recovered downstream
    /// once it has been flattened to a number.
    /// </para>
    /// <para>
    /// <b>Scope is part of the id's meaning, which is why <see cref="IsKnown"/> takes one.</b> The
    /// split is the game's rather than ours: <c>CityStatisticsSystem</c> is keyed by
    /// <c>(StatisticType, parameter)</c> with no district dimension, so homelessness, migration,
    /// births, deaths, tourism and progression exist at city scope only. Commute and congestion are
    /// city-only for a different reason — the mobility family reports nothing district-scoped, so a
    /// district's copy of them is the city's number under a local name. The three counts whose
    /// buildings carry <c>CurrentDistrict</c> (uncollected garbage, attractions, signature buildings)
    /// are the only part of the v4 pass that reads at both scopes.
    /// </para>
    /// </remarks>
    public static class MetricRegistry
    {
        // ------------------------------------------------------------- the vocabulary, second copy
        //
        // Identifier names and values both mirror Agora.Mod.Sensors.MetricHistory. Do not "tidy" a
        // name here: the pin test compares the strings, and the sidecar fingerprint is taken over them.

        /// <summary>Mean land value. Both scopes.</summary>
        public const string LandValue = "landValue";

        /// <summary>Mean rent. Both scopes.</summary>
        public const string Rent = "rent";

        /// <summary>Head count. Both scopes.</summary>
        public const string Population = "population";

        /// <summary>Share of residents at each education tier. The five sum to 1 within rounding.</summary>
        public const string EducationUneducated = "education.uneducated";
        public const string EducationPoorlyEducated = "education.poorlyEducated";
        public const string EducationEducated = "education.educated";
        public const string EducationWellEducated = "education.wellEducated";
        public const string EducationHighlyEducated = "education.highlyEducated";

        /// <summary>Share of residents in each wealth tier.</summary>
        public const string WealthLow = "wealth.low";
        public const string WealthMiddle = "wealth.middle";
        public const string WealthHigh = "wealth.high";

        /// <summary>0–100.</summary>
        public const string Happiness = "happiness";

        /// <summary>0–1.</summary>
        public const string Unemployment = "unemployment";

        /// <summary>0–1.</summary>
        public const string CrimeRate = "crimeRate";

        /// <summary>Unweighted mean of the four pollution channels, 0–1.</summary>
        public const string PollutionMean = "pollution";

        /// <summary>Unweighted mean of the nine service coverages, 0–1.</summary>
        public const string ServiceCoverageMean = "serviceCoverage";

        /// <summary>Mean one-way commute in minutes. City scope only.</summary>
        public const string CommuteMinutes = "commuteMinutes";

        /// <summary>0–1. City scope only.</summary>
        public const string TrafficCongestion = "trafficCongestion";

        /// <summary>Homeless residents. A count. City scope only.</summary>
        public const string Homeless = "homeless";

        /// <summary>Homeless share, 0–1. Not the game's 0–100 percentage. City scope only.</summary>
        public const string HomelessShare = "homelessShare";

        /// <summary>Citizens who moved in. A count, not a rate. City scope only.</summary>
        public const string CitizensMovedIn = "citizensMovedIn";

        /// <summary>Citizens who moved away. A count. City scope only.</summary>
        public const string CitizensMovedAway = "citizensMovedAway";

        /// <summary>Citizens who moved away because they were unhappy. A count. City scope only.</summary>
        public const string MovedAwayUnhappy = "movedAwayUnhappy";

        /// <summary>Births. A count. City scope only.</summary>
        public const string Births = "births";

        /// <summary>Deaths. A count. City scope only.</summary>
        public const string Deaths = "deaths";

        /// <summary>Garbage produced per day. A rate, not a stockpile. City scope only.</summary>
        public const string GarbageProductionRate = "garbageProductionRate";

        /// <summary>Tourists in the city. A count. City scope only.</summary>
        public const string Tourists = "tourists";

        /// <summary>The city's attractiveness index, raw. City scope only.</summary>
        public const string Attractiveness = "attractiveness";

        /// <summary>Hotel rooms occupied. City scope only.</summary>
        public const string LodgingUsed = "lodgingUsed";

        /// <summary>Hotel rooms available. City scope only.</summary>
        public const string LodgingTotal = "lodgingTotal";

        /// <summary>The achieved milestone, which is also the city level. City scope only.</summary>
        public const string MilestoneLevel = "milestoneLevel";

        /// <summary>Lifetime experience. City scope only.</summary>
        public const string Experience = "experience";

        /// <summary>Progress toward the next milestone, 0–1. City scope only.</summary>
        public const string MilestoneProgress = "milestoneProgress";

        /// <summary>Garbage sitting uncollected at producers. Both scopes.</summary>
        public const string UncollectedGarbage = "uncollectedGarbage";

        /// <summary>Buildings contributing attractiveness. Both scopes.</summary>
        public const string AttractionCount = "attractionCount";

        /// <summary>Signature buildings. Both scopes. There is no landmark count.</summary>
        public const string SignatureBuildingCount = "signatureBuildingCount";

        // ------------------------------------------------------------------------ the sorted lists
        //
        // Sorted by StringComparer.Ordinal at static initialisation rather than by hand. Sorting the
        // literal order out in code costs one comparison sort at first touch and removes the one way a
        // hand-kept "sorted" list goes wrong — an insertion in the wrong place, which would break both
        // the binary search below and the fingerprint the vocabulary is hashed into.

        private static readonly ReadOnlyCollection<string> CityIds = SortedOrdinal(new[]
        {
            LandValue, Rent, Population,
            EducationUneducated, EducationPoorlyEducated, EducationEducated, EducationWellEducated,
            EducationHighlyEducated,
            WealthLow, WealthMiddle, WealthHigh,
            Happiness, Unemployment, CrimeRate, PollutionMean, ServiceCoverageMean,
            CommuteMinutes, TrafficCongestion,
            Homeless, HomelessShare, CitizensMovedIn, CitizensMovedAway, MovedAwayUnhappy,
            Births, Deaths, GarbageProductionRate,
            Tourists, Attractiveness, LodgingUsed, LodgingTotal,
            MilestoneLevel, Experience, MilestoneProgress,
            UncollectedGarbage, AttractionCount, SignatureBuildingCount
        });

        private static readonly ReadOnlyCollection<string> DistrictIds = SortedOrdinal(new[]
        {
            LandValue, Rent, Population,
            EducationUneducated, EducationPoorlyEducated, EducationEducated, EducationWellEducated,
            EducationHighlyEducated,
            WealthLow, WealthMiddle, WealthHigh,
            Happiness, Unemployment, CrimeRate, PollutionMean, ServiceCoverageMean,
            UncollectedGarbage, AttractionCount, SignatureBuildingCount
        });

        /// <summary>Every city-scope metric id, sorted ordinal.</summary>
        public static IReadOnlyList<string> CityMetricIds
        {
            get { return CityIds; }
        }

        /// <summary>Every district-scope metric id, sorted ordinal.</summary>
        public static IReadOnlyList<string> DistrictMetricIds
        {
            get { return DistrictIds; }
        }

        /// <summary>
        /// True when <paramref name="metricId"/> is readable at the given scope. This is what makes
        /// an unreachable trigger a <b>load-time catalog error</b> rather than a runtime surprise.
        /// </summary>
        /// <remarks>
        /// <see cref="TriggerScope.AnyDistrict"/> and <see cref="TriggerScope.AllDistricts"/> answer
        /// the same question — both quantify over the district vocabulary — so the two are folded
        /// together here. An unknown scope value answers false rather than defaulting to the city
        /// list: a scope this registry does not understand is one whose readings it cannot vouch for.
        /// </remarks>
        public static bool IsKnown(string metricId, TriggerScope scope)
        {
            if (string.IsNullOrEmpty(metricId)) return false;

            switch (scope)
            {
                case TriggerScope.City:
                    return Contains(CityIds, metricId);
                case TriggerScope.AnyDistrict:
                case TriggerScope.AllDistricts:
                    return Contains(DistrictIds, metricId);
                default:
                    return false;
            }
        }

        /// <summary>
        /// The city-wide reading, or null when it cannot be read.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Null here means only one thing: <paramref name="metricId"/> names no reading at city scope,
        /// or there is no snapshot to read. <b>The city has no fallback marker and needs none</b> — it
        /// has nowhere to fall back to, so every value it carries was either measured or resolved to
        /// the contract default by the assembler.
        /// </para>
        /// <para>
        /// The known exception, inherited rather than introduced here:
        /// <see cref="TourismLevels.Attractiveness"/> reads <c>0</c> both for a city nobody wants to
        /// visit and for a tourism sensor that went blind, and unlike a district there is no marker to
        /// say which (see <c>SnapshotAssembly</c>'s city block). This registry reports the number it
        /// is given; it does not invent a sentinel, because the sensor layer deliberately did not
        /// (non-negotiable: no fabricated readings).
        /// </para>
        /// </remarks>
        public static double? ReadCity(CitySnapshot snapshot, string metricId)
        {
            if (snapshot == null || string.IsNullOrEmpty(metricId)) return null;

            switch (metricId)
            {
                case LandValue: return snapshot.AverageLandValue;
                case Rent: return snapshot.AverageRent;
                case Population: return snapshot.Population;

                case EducationUneducated: return snapshot.Education.UneducatedShare;
                case EducationPoorlyEducated: return snapshot.Education.PoorlyEducatedShare;
                case EducationEducated: return snapshot.Education.EducatedShare;
                case EducationWellEducated: return snapshot.Education.WellEducatedShare;
                case EducationHighlyEducated: return snapshot.Education.HighlyEducatedShare;

                case WealthLow: return snapshot.Wealth.LowShare;
                case WealthMiddle: return snapshot.Wealth.MiddleShare;
                case WealthHigh: return snapshot.Wealth.HighShare;

                case Happiness: return snapshot.Happiness;
                case Unemployment: return snapshot.Unemployment;
                case CrimeRate: return snapshot.CrimeRate;
                case PollutionMean: return snapshot.Pollution.Mean();
                case ServiceCoverageMean: return snapshot.Services.Mean();
                case CommuteMinutes: return snapshot.AverageCommuteMinutes;
                case TrafficCongestion: return snapshot.TrafficCongestion;

                case Homeless: return snapshot.Statistics.Homeless;
                case HomelessShare: return snapshot.Statistics.HomelessShare;
                case CitizensMovedIn: return snapshot.Statistics.CitizensMovedIn;
                case CitizensMovedAway: return snapshot.Statistics.CitizensMovedAway;
                case MovedAwayUnhappy: return snapshot.Statistics.MovedAwayUnhappy;
                case Births: return snapshot.Statistics.Births;
                case Deaths: return snapshot.Statistics.Deaths;
                case GarbageProductionRate: return snapshot.Statistics.GarbageProductionRate;

                case Tourists: return snapshot.Tourism.Tourists;
                case Attractiveness: return snapshot.Tourism.Attractiveness;
                case LodgingUsed: return snapshot.Tourism.LodgingUsed;
                case LodgingTotal: return snapshot.Tourism.LodgingTotal;

                case MilestoneLevel: return snapshot.Progression.MilestoneLevel;
                case Experience: return snapshot.Progression.Experience;
                case MilestoneProgress: return snapshot.Progression.MilestoneProgress;

                case UncollectedGarbage: return snapshot.UncollectedGarbage;
                case AttractionCount: return snapshot.AttractionCount;
                case SignatureBuildingCount: return snapshot.SignatureBuildingCount;

                // An id that names no reading at city scope. Not zero, not a default — nothing was
                // measured, so nothing may be reported. The caller turns this into Unmeasurable.
                default: return null;
            }
        }

        /// <summary>
        /// One district's reading, or null when it cannot be read.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Null is the answer for a district whose <c>CityFallbackFields</c> names this metric: a
        /// value copied down from the city is not a measurement of the district, and scoring against
        /// it would charge the player for a sensor gap. It is also the answer for an id that names no
        /// district-scope reading at all — commute, congestion and the whole city-statistics block are
        /// city-only at source, so asking a district for one is a question with no measurement behind
        /// it rather than a question answered no.
        /// </para>
        /// <para>
        /// <b>The marker is only trustworthy on a <i>live</i> snapshot.</b>
        /// <c>SnapshotRehydration</c> rebuilds a district from recorded samples alone, so its
        /// <c>CityFallbackFields</c> comes back empty and <c>HasCityFallbacks</c> false whatever the
        /// original month looked like — asking a rehydrated district "did you fall back?" is told
        /// "no, and the value is 0", the one wrong answer the arrangement exists to prevent. The
        /// honest mechanism for a historical month is probing <c>MetricHistory</c> for a recorded
        /// sample, and that store lives in <c>Agora.Mod</c> where this assembly cannot reach it. The
        /// consequence is stated where it bites, on <c>TriggerEvaluator</c>'s <c>Delta</c> path.
        /// </para>
        /// </remarks>
        public static double? ReadDistrict(DistrictSnapshot district, string metricId)
        {
            if (district == null || string.IsNullOrEmpty(metricId)) return null;
            if (FellBack(district, metricId)) return null;

            switch (metricId)
            {
                case LandValue: return district.AverageLandValue;
                case Rent: return district.AverageRent;
                case Population: return district.Population;

                case EducationUneducated: return district.Education.UneducatedShare;
                case EducationPoorlyEducated: return district.Education.PoorlyEducatedShare;
                case EducationEducated: return district.Education.EducatedShare;
                case EducationWellEducated: return district.Education.WellEducatedShare;
                case EducationHighlyEducated: return district.Education.HighlyEducatedShare;

                case WealthLow: return district.Wealth.LowShare;
                case WealthMiddle: return district.Wealth.MiddleShare;
                case WealthHigh: return district.Wealth.HighShare;

                case Happiness: return district.Happiness;
                case Unemployment: return district.Unemployment;
                case CrimeRate: return district.CrimeRate;
                case PollutionMean: return district.Pollution.Mean();
                case ServiceCoverageMean: return district.Services.Mean();

                case UncollectedGarbage: return district.UncollectedGarbage;
                case AttractionCount: return district.AttractionCount;
                case SignatureBuildingCount: return district.SignatureBuildingCount;

                default: return null;
            }
        }

        // ------------------------------------------------------------------------------- internals

        /// <summary>
        /// The <see cref="DistrictSnapshot"/> property name a metric id resolves to, or null when the
        /// id has no district-scope reading.
        /// </summary>
        /// <remarks>
        /// <b>The marker vocabulary is property names, not metric ids</b> — <c>SnapshotAssembly</c>
        /// writes <c>"AverageRent"</c> where this registry says <c>"rent"</c>, and several ids share
        /// one marker because the sensor falls back on a whole distribution at once: the five
        /// education shares are one <c>Education</c> field and the three wealth shares one
        /// <c>Wealth</c> field, so a district that could not measure its education cannot measure any
        /// tier of it. Comparing the wrong vocabulary would silently never match, and every fallback
        /// district would read as measured.
        /// </remarks>
        private static string? FallbackFieldFor(string metricId)
        {
            switch (metricId)
            {
                case LandValue: return "AverageLandValue";
                case Rent: return "AverageRent";
                case Population: return "Population";

                case EducationUneducated:
                case EducationPoorlyEducated:
                case EducationEducated:
                case EducationWellEducated:
                case EducationHighlyEducated:
                    return "Education";

                case WealthLow:
                case WealthMiddle:
                case WealthHigh:
                    return "Wealth";

                case Happiness: return "Happiness";
                case Unemployment: return "Unemployment";
                case CrimeRate: return "CrimeRate";
                case PollutionMean: return "Pollution";
                case ServiceCoverageMean: return "Services";

                case UncollectedGarbage: return "UncollectedGarbage";
                case AttractionCount: return "AttractionCount";
                case SignatureBuildingCount: return "SignatureBuildingCount";

                default: return null;
            }
        }

        /// <summary>
        /// True when <paramref name="district"/> names <paramref name="metricId"/>'s field in its
        /// <c>CityFallbackFields</c>. A linear scan over a list the contract keeps short and sorted;
        /// it is walked in its own order, which is fixed, and never a dictionary's.
        /// </summary>
        private static bool FellBack(DistrictSnapshot district, string metricId)
        {
            List<string>? fallbacks = district.CityFallbackFields;
            if (fallbacks == null || fallbacks.Count == 0) return false;

            string? field = FallbackFieldFor(metricId);
            if (field == null) return false;

            for (int i = 0; i < fallbacks.Count; i++)
            {
                if (string.Equals(fallbacks[i], field, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static ReadOnlyCollection<string> SortedOrdinal(string[] ids)
        {
            var sorted = new List<string>(ids);
            sorted.Sort(StringComparer.Ordinal);
            return new ReadOnlyCollection<string>(sorted);
        }

        private static bool Contains(ReadOnlyCollection<string> sortedIds, string metricId)
        {
            int low = 0;
            int high = sortedIds.Count - 1;

            while (low <= high)
            {
                int mid = low + ((high - low) / 2);
                int cmp = string.CompareOrdinal(sortedIds[mid], metricId);
                if (cmp == 0) return true;
                if (cmp < 0) low = mid + 1;
                else high = mid - 1;
            }

            return false;
        }
    }
}
