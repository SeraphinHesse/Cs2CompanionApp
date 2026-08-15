using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Stories;
using Agora.Mod.Sensors;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The trigger and check evaluator — one implementation, two callers.
    ///
    /// <para>
    /// <b>The property that governs every test here is that unreadable is not the same as unmet.</b>
    /// <c>NotMet</c> costs the player political power and <c>Unmeasurable</c> costs them nothing, so
    /// an evaluator that collapses the two charges them for a sensor gap. Every negative case below
    /// therefore asserts <i>which</i> negative it is, never merely that the trigger did not fire.
    /// </para>
    /// </summary>
    public class TriggerEvaluatorTests
    {
        private static readonly SimDate March1994 = StoryTestFixtures.March1994;

        // --- Metric -------------------------------------------------------------------------------

        [Theory]
        [InlineData(Comparison.GreaterThanOrEqual, 60.0, CheckResult.Met)]
        [InlineData(Comparison.GreaterThanOrEqual, 61.0, CheckResult.NotMet)]
        [InlineData(Comparison.GreaterThan, 59.0, CheckResult.Met)]
        [InlineData(Comparison.GreaterThan, 60.0, CheckResult.NotMet)]
        [InlineData(Comparison.LessThanOrEqual, 60.0, CheckResult.Met)]
        [InlineData(Comparison.LessThanOrEqual, 59.0, CheckResult.NotMet)]
        [InlineData(Comparison.LessThan, 61.0, CheckResult.Met)]
        [InlineData(Comparison.LessThan, 60.0, CheckResult.NotMet)]
        public void Evaluate_Metric_AppliesEveryComparisonWithTheDocumentedInclusivity(
            Comparison comparison, double threshold, CheckResult expected)
        {
            StoryReadContext context = StoryTestFixtures.Context(
                StoryTestFixtures.City(March1994, happiness: 60.0));

            Assert.Equal(expected, TriggerEvaluator.Evaluate(
                StoryTestFixtures.Metric(MetricHistory.Happiness, comparison, threshold), context));
        }

        /// <summary>
        /// A name outside the vocabulary cannot be read, and "cannot be read" is never "did not
        /// happen".
        /// </summary>
        [Fact]
        public void Evaluate_Metric_IsUnmeasurableForANameTheRegistryDoesNotKnow()
        {
            StoryReadContext context = StoryTestFixtures.Context(StoryTestFixtures.City(March1994));

            Assert.Equal(CheckResult.Unmeasurable, TriggerEvaluator.Evaluate(
                StoryTestFixtures.Metric("notAMetric", Comparison.GreaterThan, 0.0), context));
        }

        // --- Delta --------------------------------------------------------------------------------

        /// <summary>
        /// A delta is a genuinely later measurement against an earlier one, which is what makes the
        /// two-month cycle mean something.
        /// </summary>
        [Fact]
        public void Evaluate_Delta_ReadsBackOverTheWindow()
        {
            CitySnapshot older = StoryTestFixtures.City(March1994.AddMonths(-2), happiness: 40.0);
            CitySnapshot earlier = StoryTestFixtures.City(March1994.AddMonths(-1), happiness: 45.0);
            CitySnapshot today = StoryTestFixtures.City(March1994, happiness: 60.0);

            StoryReadContext context = StoryTestFixtures.Context(today, older, earlier);

            // Happiness rose over the window however the change is expressed — an absolute rise of 20
            // or a fractional one of +0.5 both clear a threshold of zero, so this asserts the
            // direction rather than the arithmetic.
            Assert.Equal(CheckResult.Met, TriggerEvaluator.Evaluate(
                StoryTestFixtures.Delta(MetricHistory.Happiness, Comparison.GreaterThan, 0.0, 2), context));

            Assert.Equal(CheckResult.NotMet, TriggerEvaluator.Evaluate(
                StoryTestFixtures.Delta(MetricHistory.Happiness, Comparison.LessThan, 0.0, 2), context));
        }

        /// <summary>
        /// <b>An empty history is the normal case on a young save, not an error.</b> A delta with
        /// nothing to read back over is unmeasurable — never not-met, because only the second may cost
        /// the player anything.
        /// </summary>
        [Fact]
        public void Evaluate_Delta_IsUnmeasurableWithNoHistory()
        {
            StoryReadContext context = StoryTestFixtures.Context(
                StoryTestFixtures.City(March1994, happiness: 60.0));

            Assert.Equal(CheckResult.Unmeasurable, TriggerEvaluator.Evaluate(
                StoryTestFixtures.Delta(MetricHistory.Happiness, Comparison.GreaterThan, 0.0, 2), context));
        }

        /// <summary>A window reaching further back than the history goes has no baseline either.</summary>
        [Fact]
        public void Evaluate_Delta_IsUnmeasurableWhenTheWindowOutrunsTheHistory()
        {
            CitySnapshot earlier = StoryTestFixtures.City(March1994.AddMonths(-1), happiness: 45.0);
            StoryReadContext context = StoryTestFixtures.Context(
                StoryTestFixtures.City(March1994, happiness: 60.0), earlier);

            Assert.Equal(CheckResult.Unmeasurable, TriggerEvaluator.Evaluate(
                StoryTestFixtures.Delta(MetricHistory.Happiness, Comparison.GreaterThan, 0.0, 24), context));
        }

        /// <summary>
        /// The two list-valued snapshot fields have no historical series behind them at all — the
        /// omission is a decision recorded where the vocabulary is declared. A delta naming one is
        /// unmeasurable rather than false.
        /// </summary>
        [Theory]
        [InlineData("unlockedFeatureIds")]
        [InlineData("industryTaxRates")]
        public void Evaluate_Delta_IsUnmeasurableForTheListValuedFields(string metricId)
        {
            CitySnapshot earlier = StoryTestFixtures.City(March1994.AddMonths(-1));
            StoryReadContext context = StoryTestFixtures.Context(
                StoryTestFixtures.City(March1994, unlockedFeatureIds: new[] { "feature-transit" }), earlier);

            Assert.Equal(CheckResult.Unmeasurable, TriggerEvaluator.Evaluate(
                StoryTestFixtures.Delta(metricId, Comparison.GreaterThan, 0.0, 1), context));
        }

        // --- Unlock and Policy --------------------------------------------------------------------

        /// <summary>
        /// Present tense only. A spec may ask what is unlocked <i>today</i>; it may not ask what
        /// changed.
        /// </summary>
        [Fact]
        public void Evaluate_Unlock_ReadsTheUnlockedFeatureList()
        {
            StoryReadContext context = StoryTestFixtures.Context(StoryTestFixtures.City(March1994,
                unlockedFeatureIds: new[] { "feature-highways", "feature-transit" }));

            Assert.Equal(CheckResult.Met, TriggerEvaluator.Evaluate(
                StoryTestFixtures.OfKind(TriggerKind.Unlock, "feature-transit"), context));

            Assert.Equal(CheckResult.NotMet, TriggerEvaluator.Evaluate(
                StoryTestFixtures.OfKind(TriggerKind.Unlock, "feature-parks"), context));
        }

        [Fact]
        public void Evaluate_Policy_ReadsTheActivePolicyList()
        {
            StoryReadContext context = StoryTestFixtures.Context(StoryTestFixtures.City(March1994,
                activePolicyIds: new[] { "policy-heavy-traffic-ban" }));

            Assert.Equal(CheckResult.Met, TriggerEvaluator.Evaluate(
                StoryTestFixtures.OfKind(TriggerKind.Policy, "policy-heavy-traffic-ban"), context));

            Assert.Equal(CheckResult.NotMet, TriggerEvaluator.Evaluate(
                StoryTestFixtures.OfKind(TriggerKind.Policy, "policy-combustion-ban"), context));
        }

        // --- Absent -------------------------------------------------------------------------------

        /// <summary>
        /// <see cref="TriggerKind.Absent"/> is the negation of the same spec, so it swaps
        /// <see cref="CheckResult.Met"/> and <see cref="CheckResult.NotMet"/>.
        /// </summary>
        [Theory]
        [InlineData(50.0, CheckResult.NotMet)]  // happiness 60 is >= 50, so "absent" does not hold
        [InlineData(70.0, CheckResult.Met)]     // happiness 60 is not >= 70, so "absent" holds
        public void Evaluate_Absent_NegatesTheSpec(double threshold, CheckResult expected)
        {
            StoryReadContext context = StoryTestFixtures.Context(
                StoryTestFixtures.City(March1994, happiness: 60.0));

            TriggerSpec spec = StoryTestFixtures.Metric(
                MetricHistory.Happiness, Comparison.GreaterThanOrEqual, threshold);
            spec.Kind = TriggerKind.Absent;

            Assert.Equal(expected, TriggerEvaluator.Evaluate(spec, context));
        }

        /// <summary>
        /// <b>Negation must not manufacture a reading.</b> Unmeasurable has no opposite — "we cannot
        /// see" negates to "we still cannot see", and turning it into <see cref="CheckResult.Met"/>
        /// would let an unreadable metric fire an event.
        /// </summary>
        [Fact]
        public void Evaluate_Absent_LeavesAnUnmeasurableReadingUnmeasurable()
        {
            StoryReadContext context = StoryTestFixtures.Context(StoryTestFixtures.City(March1994));

            TriggerSpec spec = StoryTestFixtures.Metric("notAMetric", Comparison.GreaterThanOrEqual, 1.0);
            spec.Kind = TriggerKind.Absent;

            Assert.Equal(CheckResult.Unmeasurable, TriggerEvaluator.Evaluate(spec, context));
        }

        // --- Manual -------------------------------------------------------------------------------

        /// <summary>
        /// <see cref="TriggerKind.Manual"/> never fires from the city — it is reserved for events the
        /// engine or the player introduces directly. Asserted as "not Met" rather than as a specific
        /// negative, because the contract states what it must never do and leaves which negative it is
        /// to lane 2a.
        /// </summary>
        [Fact]
        public void Evaluate_Manual_NeverFiresFromTheCity()
        {
            StoryReadContext rich = StoryTestFixtures.Context(StoryTestFixtures.City(March1994,
                happiness: 99.0, unlockedFeatureIds: new[] { "feature-transit" },
                activePolicyIds: new[] { "policy-heavy-traffic-ban" }));

            Assert.NotEqual(CheckResult.Met,
                            TriggerEvaluator.Evaluate(StoryTestFixtures.OfKind(TriggerKind.Manual, ""), rich));

            Assert.NotEqual(CheckResult.Met, TriggerEvaluator.Evaluate(
                StoryTestFixtures.OfKind(TriggerKind.Manual, MetricHistory.Happiness), rich));
        }

        // --- Scope --------------------------------------------------------------------------------

        private static StoryReadContext WithDistricts(params DistrictSnapshot[] districts) =>
            StoryTestFixtures.Context(StoryTestFixtures.City(March1994, districts: districts));

        [Fact]
        public void Evaluate_AnyDistrict_HoldsWhenOneDistrictSatisfiesIt()
        {
            StoryReadContext context = WithDistricts(
                StoryTestFixtures.District("d00000001", uncollectedGarbage: 10.0),
                StoryTestFixtures.District("d00000002", uncollectedGarbage: 900.0));

            Assert.Equal(CheckResult.Met, TriggerEvaluator.Evaluate(
                StoryTestFixtures.Metric(MetricHistory.UncollectedGarbage, Comparison.GreaterThan, 500.0,
                                         TriggerScope.AnyDistrict), context));
        }

        [Fact]
        public void Evaluate_AnyDistrict_DoesNotHoldWhenNoDistrictSatisfiesIt()
        {
            StoryReadContext context = WithDistricts(
                StoryTestFixtures.District("d00000001", uncollectedGarbage: 10.0),
                StoryTestFixtures.District("d00000002", uncollectedGarbage: 20.0));

            Assert.Equal(CheckResult.NotMet, TriggerEvaluator.Evaluate(
                StoryTestFixtures.Metric(MetricHistory.UncollectedGarbage, Comparison.GreaterThan, 500.0,
                                         TriggerScope.AnyDistrict), context));
        }

        [Fact]
        public void Evaluate_AllDistricts_NeedsEveryDistrict()
        {
            StoryReadContext all = WithDistricts(
                StoryTestFixtures.District("d00000001", uncollectedGarbage: 900.0),
                StoryTestFixtures.District("d00000002", uncollectedGarbage: 800.0));

            StoryReadContext one = WithDistricts(
                StoryTestFixtures.District("d00000001", uncollectedGarbage: 900.0),
                StoryTestFixtures.District("d00000002", uncollectedGarbage: 10.0));

            TriggerSpec spec = StoryTestFixtures.Metric(MetricHistory.UncollectedGarbage,
                Comparison.GreaterThan, 500.0, TriggerScope.AllDistricts);

            Assert.Equal(CheckResult.Met, TriggerEvaluator.Evaluate(spec, all));
            Assert.Equal(CheckResult.NotMet, TriggerEvaluator.Evaluate(spec, one));
        }

        /// <summary>
        /// A positive is still a positive. One district that genuinely measured the metric and clears
        /// the threshold satisfies "any", whatever the district beside it could not read.
        /// </summary>
        [Fact]
        public void Evaluate_AnyDistrict_IgnoresAFallenBackDistrictWhenAnotherSatisfiesIt()
        {
            StoryReadContext context = WithDistricts(
                StoryTestFixtures.District("d00000001", uncollectedGarbage: 900.0,
                    fellBackOn: new[] { SnapshotAssembly.FieldUncollectedGarbage }),
                StoryTestFixtures.District("d00000002", uncollectedGarbage: 900.0));

            Assert.Equal(CheckResult.Met, TriggerEvaluator.Evaluate(
                StoryTestFixtures.Metric(MetricHistory.UncollectedGarbage, Comparison.GreaterThan, 500.0,
                                         TriggerScope.AnyDistrict), context));
        }

        /// <summary>
        /// Every district fell back, so nothing in the city measured this metric — unmeasurable, and
        /// emphatically not "no district has a garbage problem".
        /// </summary>
        [Theory]
        [InlineData(TriggerScope.AnyDistrict)]
        [InlineData(TriggerScope.AllDistricts)]
        public void Evaluate_DistrictScope_IsUnmeasurableWhenNoDistrictMeasuredIt(TriggerScope scope)
        {
            StoryReadContext context = WithDistricts(
                StoryTestFixtures.District("d00000001", uncollectedGarbage: 900.0,
                    fellBackOn: new[] { SnapshotAssembly.FieldUncollectedGarbage }),
                StoryTestFixtures.District("d00000002", uncollectedGarbage: 10.0,
                    fellBackOn: new[] { SnapshotAssembly.FieldUncollectedGarbage }));

            Assert.Equal(CheckResult.Unmeasurable, TriggerEvaluator.Evaluate(
                StoryTestFixtures.Metric(MetricHistory.UncollectedGarbage, Comparison.GreaterThan, 500.0,
                                         scope), context));
        }

        /// <summary>A city with no districts has nothing to read at district scope.</summary>
        [Theory]
        [InlineData(TriggerScope.AnyDistrict)]
        [InlineData(TriggerScope.AllDistricts)]
        public void Evaluate_DistrictScope_IsUnmeasurableWithNoDistricts(TriggerScope scope)
        {
            StoryReadContext context = StoryTestFixtures.Context(
                StoryTestFixtures.City(March1994, districts: new List<DistrictSnapshot>()));

            Assert.Equal(CheckResult.Unmeasurable, TriggerEvaluator.Evaluate(
                StoryTestFixtures.Metric(MetricHistory.UncollectedGarbage, Comparison.GreaterThan, 500.0,
                                         scope), context));
        }

        /// <summary>
        /// <b>District order must not decide anything.</b> "Any" does not care which district matched,
        /// but the same spec at draft feeds a district-targeted effect at resolution, so an evaluator
        /// that walked the collection in arrival order would make the answer depend on the sensor's
        /// enumeration — the determinism bug <c>Agora.Core/CLAUDE.md</c> names as the most common one.
        /// </summary>
        [Fact]
        public void Evaluate_DistrictScope_IsIndependentOfTheOrderTheDistrictsArriveIn()
        {
            DistrictSnapshot a = StoryTestFixtures.District("d00000001", uncollectedGarbage: 900.0);
            DistrictSnapshot b = StoryTestFixtures.District("d00000002", uncollectedGarbage: 10.0);
            DistrictSnapshot c = StoryTestFixtures.District("d00000003", uncollectedGarbage: 501.0);

            TriggerSpec any = StoryTestFixtures.Metric(MetricHistory.UncollectedGarbage,
                Comparison.GreaterThan, 500.0, TriggerScope.AnyDistrict);
            TriggerSpec all = StoryTestFixtures.Metric(MetricHistory.UncollectedGarbage,
                Comparison.GreaterThan, 500.0, TriggerScope.AllDistricts);

            Assert.Equal(TriggerEvaluator.Evaluate(any, WithDistricts(a, b, c)),
                         TriggerEvaluator.Evaluate(any, WithDistricts(c, b, a)));

            Assert.Equal(TriggerEvaluator.Evaluate(all, WithDistricts(a, b, c)),
                         TriggerEvaluator.Evaluate(all, WithDistricts(c, b, a)));
        }

        // --- EvaluateCheck ------------------------------------------------------------------------

        /// <summary>An absolute check does not consult the baseline at all.</summary>
        [Fact]
        public void EvaluateCheck_Absolute_IgnoresTheBaseline()
        {
            StoryReadContext context = StoryTestFixtures.Context(
                StoryTestFixtures.City(March1994, happiness: 60.0));

            CheckSpec check = StoryTestFixtures.Check(
                StoryTestFixtures.Metric(MetricHistory.Happiness, Comparison.GreaterThanOrEqual, 55.0));

            Assert.Equal(CheckResult.Met, TriggerEvaluator.EvaluateCheck(check, null, context));
            Assert.Equal(CheckResult.Met, TriggerEvaluator.EvaluateCheck(check, 10.0, context));
            Assert.Equal(CheckResult.Met, TriggerEvaluator.EvaluateCheck(check, 999.0, context));
        }

        /// <summary>
        /// A relative check measures against the month the story started. Asserted with the today
        /// value far above and far below the baseline, so the verdict is the same whether the
        /// difference is expressed absolutely or fractionally.
        /// </summary>
        [Fact]
        public void EvaluateCheck_RelativeToBaseline_MeasuresAgainstTheStoryOpen()
        {
            StoryReadContext context = StoryTestFixtures.Context(
                StoryTestFixtures.City(March1994, happiness: 60.0));

            CheckSpec check = StoryTestFixtures.Check(
                StoryTestFixtures.Metric(MetricHistory.Happiness, Comparison.GreaterThanOrEqual, 1.0),
                relativeToBaseline: true);

            Assert.Equal(CheckResult.Met, TriggerEvaluator.EvaluateCheck(check, 20.0, context));
            Assert.Equal(CheckResult.NotMet, TriggerEvaluator.EvaluateCheck(check, 90.0, context));
        }

        /// <summary>
        /// <b>A null baseline makes a relative check unmeasurable rather than failed.</b> There is no
        /// honest verdict to reach without the number the comparison is against, and reaching for
        /// zero instead would score the player against a city that never existed.
        /// </summary>
        [Fact]
        public void EvaluateCheck_RelativeToBaseline_IsUnmeasurableWithNoBaseline()
        {
            StoryReadContext context = StoryTestFixtures.Context(
                StoryTestFixtures.City(March1994, happiness: 60.0));

            CheckSpec check = StoryTestFixtures.Check(
                StoryTestFixtures.Metric(MetricHistory.Happiness, Comparison.GreaterThanOrEqual, 1.0),
                relativeToBaseline: true);

            Assert.Equal(CheckResult.Unmeasurable, TriggerEvaluator.EvaluateCheck(check, null, context));
        }

        /// <summary>
        /// <b>Recorded evidence beats a live reading, and that is what keeps an early resolve
        /// deterministic.</b> The player's command fires at a wall-clock moment the engine does not
        /// control, so the sample taken then is persisted into the story and replay reads it back
        /// rather than sampling a city that has since moved on.
        /// </summary>
        [Fact]
        public void EvaluateCheck_PrefersRecordedEvidenceOverTheLiveSnapshot()
        {
            StoryReadContext live = StoryTestFixtures.Context(
                StoryTestFixtures.City(March1994, happiness: 10.0));

            StoryReadContext recorded = StoryTestFixtures.WithEvidence(live,
                StoryTestFixtures.Reading(MetricHistory.Happiness, 90.0));

            CheckSpec check = StoryTestFixtures.Check(
                StoryTestFixtures.Metric(MetricHistory.Happiness, Comparison.GreaterThanOrEqual, 80.0));

            Assert.Equal(CheckResult.NotMet, TriggerEvaluator.EvaluateCheck(check, null, live));
            Assert.Equal(CheckResult.Met, TriggerEvaluator.EvaluateCheck(check, null, recorded));
        }

        /// <summary>
        /// Recorded evidence carries null for a metric that was unreadable at the moment it was taken,
        /// and null is a distinct claim from zero. Falling through to the live snapshot here would
        /// hand back a reading from a different month.
        /// </summary>
        [Fact]
        public void EvaluateCheck_TreatsRecordedNullEvidenceAsUnmeasurable()
        {
            StoryReadContext context = StoryTestFixtures.WithEvidence(
                StoryTestFixtures.Context(StoryTestFixtures.City(March1994, happiness: 90.0)),
                StoryTestFixtures.Reading(MetricHistory.Happiness, null));

            CheckSpec check = StoryTestFixtures.Check(
                StoryTestFixtures.Metric(MetricHistory.Happiness, Comparison.GreaterThanOrEqual, 80.0));

            Assert.Equal(CheckResult.Unmeasurable, TriggerEvaluator.EvaluateCheck(check, null, context));
        }
    }
}
