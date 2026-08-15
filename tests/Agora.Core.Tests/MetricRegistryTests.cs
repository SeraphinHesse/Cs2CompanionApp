// Requires the Sensors <Compile Link> lines in Agora.Core.Tests.csproj — MetricHistory in
// particular. This file is the only place in the codebase that can see both copies of the metric
// vocabulary at once, which is the whole reason it exists.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Stories;
using Agora.Mod.Sensors;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The metric registry, and the pin that holds its vocabulary to the sensor layer's.
    ///
    /// <para>
    /// <b>There are two copies of the metric names and there have to be.</b>
    /// <see cref="MetricRegistry"/> lives in <c>Agora.Core</c>, the names live in
    /// <see cref="MetricHistory"/> in <c>Agora.Mod</c>, and Core may never reference Mod — so the
    /// registry necessarily holds a second copy of the same strings. Two copies drift. Nothing in
    /// either assembly can compare them, because neither can see the other; this test project
    /// compile-links <c>MetricHistory.cs</c> and project-references <c>Agora.Core</c>, so it is the
    /// only place that can. That is what makes the pin below a test rather than a comment.
    /// </para>
    ///
    /// <para>
    /// A name may be <b>added but never renamed</b> — the sidecar fingerprint is taken over these
    /// strings sorted, the same rule that governs a seed stream name. So the pin is deliberately
    /// asymmetric in one direction: the registry may not name a metric the history has never heard
    /// of (that trigger could never be read off a historical month), while the history is allowed to
    /// record a series no trigger has been written against yet.
    /// </para>
    /// </summary>
    public class MetricRegistryTests
    {
        // --- the two copies -----------------------------------------------------------------------

        /// <summary>
        /// Every metric-name constant <see cref="MetricHistory"/> declares, by reflection rather than
        /// by a hand-kept list.
        /// </summary>
        /// <remarks>
        /// Reflected on purpose. A hand-written list here would be a <i>third</i> copy of the
        /// vocabulary, and the failure this file exists to catch is precisely that two copies went out
        /// of step without anyone noticing. <c>CityScope</c> is excluded because it is a key segment
        /// meaning "the whole city", not a metric.
        /// </remarks>
        private static IReadOnlyList<string> HistoryMetricNames()
        {
            var names = new List<string>();

            foreach (FieldInfo field in typeof(MetricHistory)
                         .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (!field.IsLiteral || field.IsInitOnly) continue;
                if (field.FieldType != typeof(string)) continue;
                if (field.Name == nameof(MetricHistory.CityScope)) continue;

                var value = (string?)field.GetRawConstantValue();
                if (!string.IsNullOrEmpty(value)) names.Add(value!);
            }

            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary>
        /// <b>The vocabulary pin.</b> The city-scope registry and the sensor vocabulary are the same
        /// set of strings, compared in both directions.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Both sides are derived, and neither is a count.</b> A hand-kept list here would be a
        /// third copy of the vocabulary, and the failure this file exists to catch is exactly that two
        /// copies went out of step. A count — "eighteen city-scope names", "thirty-six" — is worse
        /// again: it rots the first time a metric is legitimately added, and when it fails it says
        /// <i>how many</i> rather than <i>which</i>, which is the one thing the reader needs.
        /// </para>
        /// <para>
        /// A registry id with no series behind it is not a harmless spare. <c>SnapshotRehydration</c>
        /// writes a field only where its series holds a sample and leaves everything else at the
        /// contract default, where a <c>0</c> is indistinguishable from a measurement — so a trigger
        /// authored against an unrecorded name reads as a fabricated zero for every month before the
        /// current session. And a recorded series no trigger can name is a metric the engine pays to
        /// store and can never ask about.
        /// </para>
        /// </remarks>
        [Fact]
        public void CityMetricIds_AreExactlyTheSensorVocabulary()
        {
            AssertSameSet(HistoryMetricNames(), MetricRegistry.CityMetricIds,
                          "Agora.Mod.Sensors.MetricHistory", "MetricRegistry.CityMetricIds");
        }

        /// <summary>
        /// The district registry is a subset of the same vocabulary — not every metric the city
        /// records has a per-district reading, but nothing may be readable per district that the
        /// sensor layer never files under a district's scope.
        /// </summary>
        [Fact]
        public void DistrictMetricIds_NameNothingOutsideTheSensorVocabulary()
        {
            IReadOnlyList<string> known = HistoryMetricNames();

            var strays = new List<string>();
            foreach (string id in MetricRegistry.DistrictMetricIds)
            {
                if (!known.Contains(id, StringComparer.Ordinal)) strays.Add(id);
            }

            Assert.True(strays.Count == 0,
                "MetricRegistry.DistrictMetricIds names metrics MetricHistory does not record, so " +
                "nothing stands behind them on a rehydrated month: " + string.Join(", ", strays) +
                Environment.NewLine +
                "Add the constant to MetricHistory, or drop the id from the registry — a name may be " +
                "added but never renamed.");
        }

        /// <summary>
        /// The mobility pair is city-only, and the reason is the game's rather than ours: nothing
        /// reports a per-district commute or congestion, so every district's figure is a copy of the
        /// city's. Offering them at district scope would let an author write "the north side's traffic"
        /// into a trigger that in fact reads the whole city, and no assertion downstream could tell.
        /// </summary>
        [Theory]
        [InlineData("commuteMinutes")]
        [InlineData("trafficCongestion")]
        public void DistrictMetricIds_ExcludeTheCityOnlyMobilityPair(string metricId)
        {
            // Spelled out rather than taken off MetricHistory on purpose: the constants are what the
            // pin above compares, so reading them here would make this test agree with a rename that
            // the pin had already accepted on both sides.
            Assert.Contains(metricId, MetricRegistry.CityMetricIds);
            Assert.DoesNotContain(metricId, MetricRegistry.DistrictMetricIds);
        }

        /// <summary>
        /// Set equality, reported as the two directions separately — "missing from" and "unknown to"
        /// are different mistakes with different fixes, and a message that only said the sets differed
        /// would leave whoever broke it to work out which.
        /// </summary>
        private static void AssertSameSet(IReadOnlyList<string> expected, IReadOnlyList<string> actual,
                                          string expectedName, string actualName)
        {
            var missing = new List<string>();
            foreach (string id in expected)
            {
                if (!actual.Contains(id, StringComparer.Ordinal)) missing.Add(id);
            }

            var unknown = new List<string>();
            foreach (string id in actual)
            {
                if (!expected.Contains(id, StringComparer.Ordinal)) unknown.Add(id);
            }

            if (missing.Count == 0 && unknown.Count == 0) return;

            var message = new StringBuilder();
            message.Append("The two copies of the metric vocabulary have drifted.")
                   .Append(Environment.NewLine);

            if (missing.Count > 0)
            {
                message.Append("In ").Append(expectedName).Append(" but missing from ")
                       .Append(actualName).Append(": ").Append(string.Join(", ", missing))
                       .Append(Environment.NewLine);
            }

            if (unknown.Count > 0)
            {
                message.Append("In ").Append(actualName).Append(" but unknown to ")
                       .Append(expectedName).Append(": ").Append(string.Join(", ", unknown))
                       .Append(Environment.NewLine);
            }

            message.Append("A name may be added to both sides, but never renamed on one — the " +
                           "sidecar fingerprint is taken over these strings sorted.");

            Assert.Fail(message.ToString());
        }

        // --- the shape of the lists ---------------------------------------------------------------

        /// <summary>
        /// Sorted ordinal and free of duplicates, on both lists. The registry's own summary says
        /// "sorted ordinal", and anything that enumerates it — a catalog validator listing the legal
        /// ids, a UI dropdown — would otherwise depend on however the list was built.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void MetricIds_AreSortedOrdinalAndDistinct(bool city)
        {
            IReadOnlyList<string> ids = city ? MetricRegistry.CityMetricIds : MetricRegistry.DistrictMetricIds;

            var sorted = new List<string>(ids);
            sorted.Sort(StringComparer.Ordinal);

            Assert.Equal(sorted, ids.ToList());
            Assert.Equal(ids.Distinct(StringComparer.Ordinal).Count(), ids.Count);
        }

        /// <summary>
        /// <see cref="MetricRegistry.IsKnown"/> is what makes an unreachable trigger a load-time
        /// catalog error, so it has to answer exactly what the published lists say — a scope where
        /// the two disagree is a trigger that validates and can never be read.
        /// </summary>
        [Fact]
        public void IsKnown_AgreesWithThePublishedListsAtEveryScope()
        {
            foreach (string id in MetricRegistry.CityMetricIds)
            {
                Assert.True(MetricRegistry.IsKnown(id, TriggerScope.City),
                            "CityMetricIds lists '" + id + "' but IsKnown refuses it at City scope.");
            }

            foreach (string id in MetricRegistry.DistrictMetricIds)
            {
                Assert.True(MetricRegistry.IsKnown(id, TriggerScope.AnyDistrict),
                            "DistrictMetricIds lists '" + id + "' but IsKnown refuses it at AnyDistrict scope.");
                Assert.True(MetricRegistry.IsKnown(id, TriggerScope.AllDistricts),
                            "DistrictMetricIds lists '" + id + "' but IsKnown refuses it at AllDistricts scope.");
            }
        }

        /// <summary>
        /// The two district scopes ask the same question of the same list — "any" and "all" differ in
        /// how the answers are combined, never in what is readable.
        /// </summary>
        [Fact]
        public void IsKnown_TreatsTheTwoDistrictScopesIdentically()
        {
            foreach (string id in HistoryMetricNames())
            {
                Assert.Equal(MetricRegistry.IsKnown(id, TriggerScope.AnyDistrict),
                             MetricRegistry.IsKnown(id, TriggerScope.AllDistricts));
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("notAMetric")]
        [InlineData("Happiness")]   // wrong case: the vocabulary is ordinal, not case-insensitive
        public void IsKnown_RefusesAnythingOutsideTheVocabulary(string id)
        {
            Assert.False(MetricRegistry.IsKnown(id, TriggerScope.City));
            Assert.False(MetricRegistry.IsKnown(id, TriggerScope.AnyDistrict));
        }

        [Fact]
        public void IsKnown_RefusesNullWithoutThrowing()
        {
            Assert.False(MetricRegistry.IsKnown(null!, TriggerScope.City));
        }

        /// <summary>
        /// The two list-valued snapshot fields are deliberately not metrics. Both are lists and
        /// <see cref="MetricHistory"/> stores one <c>double</c> per series per month, so there is no
        /// scalar to name — a decision recorded where the vocabulary is declared, not an omission to
        /// be repaired by adding one.
        /// </summary>
        [Theory]
        [InlineData("unlockedFeatureIds")]
        [InlineData("industryTaxRates")]
        public void IsKnown_RefusesTheTwoListValuedFields(string id)
        {
            Assert.False(MetricRegistry.IsKnown(id, TriggerScope.City));
            Assert.False(MetricRegistry.IsKnown(id, TriggerScope.AnyDistrict));
            Assert.False(MetricRegistry.IsKnown(id, TriggerScope.AllDistricts));
        }

        // --- the third vocabulary: the fallback markers -------------------------------------------

        /// <summary>
        /// Every marker <c>SnapshotAssembly</c> can write into
        /// <see cref="DistrictSnapshot.CityFallbackFields"/>, by reflection over its own
        /// <c>Field*</c> constants.
        /// </summary>
        /// <remarks>
        /// Most of those constants are <c>private</c>, so this reaches them with
        /// <see cref="BindingFlags.NonPublic"/> — legitimate here and nowhere else, because
        /// <c>SnapshotAssembly.cs</c> is compile-linked into this very assembly. Reflecting rather
        /// than re-typing them is the point: a hand-kept list would be a fourth copy of a vocabulary
        /// that already has three.
        /// </remarks>
        private static IReadOnlyList<string> FallbackMarkers()
        {
            var markers = new List<string>();

            foreach (FieldInfo field in typeof(SnapshotAssembly).GetFields(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (!field.IsLiteral || field.IsInitOnly) continue;
                if (field.FieldType != typeof(string)) continue;
                if (!field.Name.StartsWith("Field", StringComparison.Ordinal)) continue;

                var value = (string?)field.GetRawConstantValue();
                if (!string.IsNullOrEmpty(value)) markers.Add(value!);
            }

            markers.Sort(StringComparer.Ordinal);
            return markers;
        }

        /// <summary>
        /// Which markers suppress <paramref name="metricId"/>, found by trying each one and asking
        /// <see cref="MetricRegistry.ReadDistrict"/> whether the reading went dark.
        /// </summary>
        private static List<string> MarkersThatSuppress(string metricId)
        {
            var found = new List<string>();

            foreach (string marker in FallbackMarkers())
            {
                DistrictSnapshot district = StoryTestFixtures.District("d00000001",
                    uncollectedGarbage: 900.0, attractionCount: 7, signatureBuildingCount: 3,
                    happiness: 61.5, fellBackOn: new[] { marker });

                if (MetricRegistry.ReadDistrict(district, metricId) == null) found.Add(marker);
            }

            return found;
        }

        /// <summary>
        /// <b>The third string vocabulary, and until now it was pinned by nothing.</b>
        /// <c>CityFallbackFields</c> holds <i>property</i> names — <c>"AverageRent"</c> — where the
        /// registry says <c>"rent"</c>, so the two lists cannot be compared directly and a mismatch
        /// never throws. It simply never matches, and every fallback district then reads as having
        /// genuinely measured a number it copied down from the city.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Asserted behaviourally rather than by calling the mapping, because
        /// <c>MetricRegistry.FallbackFieldFor</c> is <c>private</c> and is not on the published seam
        /// table — reaching it would mean editing lane 2a's file. Probing every marker and asking
        /// which one darkens the reading tests the same property from outside, and needs no change to
        /// <c>src</c>.
        /// </para>
        /// <para>
        /// A metric with <i>no</i> marker is the failure mode: it can never be reported as a fallback,
        /// so a district that copied the city's figure scores the player against a number that was
        /// never its own.
        /// </para>
        /// </remarks>
        [Fact]
        public void EveryDistrictMetricIsSuppressedByAMarkerTheAssemblerActuallyWrites()
        {
            var unmapped = new List<string>();

            foreach (string metricId in MetricRegistry.DistrictMetricIds)
            {
                if (MarkersThatSuppress(metricId).Count == 0) unmapped.Add(metricId);
            }

            Assert.True(unmapped.Count == 0,
                "No CityFallbackFields marker suppresses these district-scope metrics, so a district " +
                "that fell back on one reads as having measured it: " +
                string.Join(", ", unmapped) + Environment.NewLine +
                "The marker vocabulary is PROPERTY names (\"AverageRent\"), not metric ids (\"rent\") " +
                "— a mapping that compared the wrong one would silently never match.");
        }

        /// <summary>
        /// The other end of the test above: a district that fell back on nothing reads every metric as
        /// measured. A mapping that suppressed everything would pass that one and be equally wrong.
        /// </summary>
        [Fact]
        public void ADistrictWithNoFallbacksMeasuresEveryDistrictMetric()
        {
            DistrictSnapshot district = StoryTestFixtures.District("d00000001",
                uncollectedGarbage: 900.0, attractionCount: 7, signatureBuildingCount: 3,
                happiness: 61.5);

            var dark = new List<string>();
            foreach (string metricId in MetricRegistry.DistrictMetricIds)
            {
                if (MetricRegistry.ReadDistrict(district, metricId) == null) dark.Add(metricId);
            }

            Assert.True(dark.Count == 0,
                "A district that fell back on nothing still reads as unmeasurable for: " +
                string.Join(", ", dark));
        }

        /// <summary>
        /// The five education ids share one marker and the three wealth ids another, because the
        /// sensor falls back on a whole distribution at once — a district that could not measure its
        /// education cannot measure any tier of it. Asserted as "the ids in each family agree with each
        /// other" rather than by naming the marker, which is lane 2a's to choose.
        /// </summary>
        [Theory]
        [InlineData("education.")]
        [InlineData("wealth.")]
        public void ADistributionsTiersShareOneFallbackMarker(string prefix)
        {
            var family = new List<string>();
            foreach (string metricId in MetricRegistry.DistrictMetricIds)
            {
                if (metricId.StartsWith(prefix, StringComparison.Ordinal)) family.Add(metricId);
            }

            Assert.True(family.Count > 1,
                "Expected more than one '" + prefix + "' metric at district scope; found " +
                family.Count + ". If the vocabulary really has changed, rewrite this test rather than " +
                "deleting it.");

            List<string> first = MarkersThatSuppress(family[0]);
            for (int i = 1; i < family.Count; i++)
            {
                Assert.Equal(first, MarkersThatSuppress(family[i]));
            }
        }

        // --- reading ------------------------------------------------------------------------------

        [Fact]
        public void ReadCity_ReadsTheFieldEachNameDeclares()
        {
            CitySnapshot city = StoryTestFixtures.City(StoryTestFixtures.March1994,
                happiness: 61.5, unemployment: 0.17, crimeRate: 0.09, population: 42000, homeless: 137);

            Assert.Equal(61.5, MetricRegistry.ReadCity(city, MetricHistory.Happiness));
            Assert.Equal(0.17, MetricRegistry.ReadCity(city, MetricHistory.Unemployment));
            Assert.Equal(0.09, MetricRegistry.ReadCity(city, MetricHistory.CrimeRate));
            Assert.Equal(42000.0, MetricRegistry.ReadCity(city, MetricHistory.Population));
            Assert.Equal(137.0, MetricRegistry.ReadCity(city, MetricHistory.Homeless));
        }

        /// <summary>
        /// The means, not the channels. <see cref="MetricHistory"/> stores pollution and service
        /// coverage as their means and nothing records the channels, so the registry has to read what
        /// the history can rebuild.
        /// </summary>
        [Fact]
        public void ReadCity_ReadsPollutionAndCoverageAsTheirMeans()
        {
            var city = StoryTestFixtures.City(StoryTestFixtures.March1994);
            city.Pollution = new PollutionLevels(0.1, 0.2, 0.3, 0.4);
            city.Services = new ServiceCoverage(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5);

            Assert.Equal(city.Pollution.Mean(), MetricRegistry.ReadCity(city, MetricHistory.PollutionMean)!.Value, 9);
            Assert.Equal(city.Services.Mean(), MetricRegistry.ReadCity(city, MetricHistory.ServiceCoverageMean)!.Value, 9);
        }

        /// <summary>
        /// Null means unmeasurable and never zero. Flattening the two together cannot be undone
        /// downstream, and it is the whole reason <see cref="CheckResult.Unmeasurable"/> exists.
        /// </summary>
        [Fact]
        public void ReadCity_ReturnsNullForAnUnknownName()
        {
            CitySnapshot city = StoryTestFixtures.City(StoryTestFixtures.March1994);

            Assert.Null(MetricRegistry.ReadCity(city, "notAMetric"));
            Assert.Null(MetricRegistry.ReadCity(city, ""));
        }

        [Fact]
        public void ReadCity_ReturnsNullForANullSnapshot()
        {
            Assert.Null(MetricRegistry.ReadCity(null!, MetricHistory.Happiness));
        }

        [Fact]
        public void ReadDistrict_ReadsAFieldTheDistrictMeasuredForItself()
        {
            DistrictSnapshot district = StoryTestFixtures.District("d00000001",
                uncollectedGarbage: 812.0, attractionCount: 4, signatureBuildingCount: 1);

            Assert.Equal(812.0, MetricRegistry.ReadDistrict(district, MetricHistory.UncollectedGarbage));
            Assert.Equal(4.0, MetricRegistry.ReadDistrict(district, MetricHistory.AttractionCount));
            Assert.Equal(1.0, MetricRegistry.ReadDistrict(district, MetricHistory.SignatureBuildingCount));
        }

        /// <summary>
        /// <b>The fallback marker is the measurability answer on a live snapshot.</b> A number copied
        /// down from the city is not a measurement of the district, and scoring a goal against it
        /// would charge the player political power for a sensor gap.
        /// </summary>
        [Fact]
        public void ReadDistrict_ReturnsNullForAFieldTheDistrictFellBackOn()
        {
            DistrictSnapshot district = StoryTestFixtures.District("d00000002",
                uncollectedGarbage: 900.0, attractionCount: 7,
                fellBackOn: new[] { SnapshotAssembly.FieldUncollectedGarbage });

            Assert.Null(MetricRegistry.ReadDistrict(district, MetricHistory.UncollectedGarbage));

            // Only the field that was named. A district that fell back on one metric has still
            // genuinely measured the others.
            Assert.Equal(7.0, MetricRegistry.ReadDistrict(district, MetricHistory.AttractionCount));
        }

        [Fact]
        public void ReadDistrict_ReturnsNullForAnUnknownNameOrANullDistrict()
        {
            DistrictSnapshot district = StoryTestFixtures.District("d00000003");

            Assert.Null(MetricRegistry.ReadDistrict(district, "notAMetric"));
            Assert.Null(MetricRegistry.ReadDistrict(null!, MetricHistory.UncollectedGarbage));
        }
    }
}
