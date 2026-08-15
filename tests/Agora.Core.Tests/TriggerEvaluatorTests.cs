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
        /// <b>Over a registry metric, <see cref="TriggerKind.Absent"/> negates the threshold read</b>,
        /// so it swaps <see cref="CheckResult.Met"/> and <see cref="CheckResult.NotMet"/>.
        /// </summary>
        [Theory]
        [InlineData(50.0, CheckResult.NotMet)]  // happiness 60 is >= 50, so "absent" does not hold
        [InlineData(70.0, CheckResult.Met)]     // happiness 60 is not >= 70, so "absent" holds
        public void Evaluate_Absent_NegatesTheThresholdReadForARegistryMetric(
            double threshold, CheckResult expected)
        {
            StoryReadContext context = StoryTestFixtures.Context(
                StoryTestFixtures.City(March1994, happiness: 60.0));

            TriggerSpec spec = StoryTestFixtures.Metric(
                MetricHistory.Happiness, Comparison.GreaterThanOrEqual, threshold);
            spec.Kind = TriggerKind.Absent;

            Assert.Equal(expected, TriggerEvaluator.Evaluate(spec, context));
        }

        /// <summary>
        /// <b>Over a name the registry does not know, it negates set membership</b> — the union of the
        /// unlocked features and the active policies.
        /// </summary>
        /// <remarks>
        /// This is what earns the kind its place in the grammar. The four comparisons are already
        /// closed under negation, so a spec that only ever negated a threshold read would be pure
        /// redundancy: <c>Absent(happiness &gt;= 70)</c> is exactly <c>happiness &lt; 70</c> written
        /// twice. Membership has no such inverse, and that is the case worth a kind.
        /// </remarks>
        [Fact]
        public void Evaluate_Absent_NegatesSetMembershipForANonMetricId()
        {
            StoryReadContext context = StoryTestFixtures.Context(StoryTestFixtures.City(March1994,
                unlockedFeatureIds: new[] { "feature-transit" },
                activePolicyIds: new[] { "policy-heavy-traffic-ban" }));

            // Present in either set, so "absent" does not hold. Both sets, because the union is the
            // claim: a spec naming a policy must not read as absent merely because it is not a feature.
            Assert.Equal(CheckResult.NotMet, TriggerEvaluator.Evaluate(
                StoryTestFixtures.OfKind(TriggerKind.Absent, "feature-transit"), context));
            Assert.Equal(CheckResult.NotMet, TriggerEvaluator.Evaluate(
                StoryTestFixtures.OfKind(TriggerKind.Absent, "policy-heavy-traffic-ban"), context));

            // In neither, so it holds.
            Assert.Equal(CheckResult.Met, TriggerEvaluator.Evaluate(
                StoryTestFixtures.OfKind(TriggerKind.Absent, "feature-parks"), context));
        }

        /// <summary>
        /// <b>Negation must not manufacture a reading.</b> Unmeasurable has no opposite — "we cannot
        /// see" negates to "we still cannot see", and turning it into <see cref="CheckResult.Met"/>
        /// would let an unreadable metric fire an event.
        /// </summary>
        /// <remarks>
        /// The fixture is a registry metric the city cannot answer for — a district-scope read on a
        /// city with no districts — rather than an unknown name, because an unknown name is now the
        /// membership case and would legitimately answer <see cref="CheckResult.Met"/>.
        /// </remarks>
        [Fact]
        public void Evaluate_Absent_LeavesAnUnmeasurableReadingUnmeasurable()
        {
            StoryReadContext context = StoryTestFixtures.Context(
                StoryTestFixtures.City(March1994, districts: new List<DistrictSnapshot>()));

            TriggerSpec spec = StoryTestFixtures.Metric(MetricHistory.UncollectedGarbage,
                Comparison.GreaterThan, 500.0, TriggerScope.AnyDistrict);
            spec.Kind = TriggerKind.Absent;

            Assert.Equal(CheckResult.Unmeasurable, TriggerEvaluator.Evaluate(spec, context));
        }

        // --- Manual -------------------------------------------------------------------------------

        /// <summary>
        /// <see cref="TriggerKind.Manual"/> never fires from the city — it is reserved for events the
        /// engine or the player introduces directly — and it answers
        /// <see cref="CheckResult.Unmeasurable"/> rather than <see cref="CheckResult.NotMet"/>.
        /// </summary>
        /// <remarks>
        /// Both answers keep the event out of the pool, so the choice looks free at the draft end. It
        /// is not: the same evaluator serves resolution, and if a catalog ever carried a
        /// <c>Manual</c> check then <see cref="CheckResult.NotMet"/> would charge the player power for
        /// an event the engine was never going to read. Unmeasurable is the honest answer at both ends.
        /// </remarks>
        [Fact]
        public void Evaluate_Manual_IsUnmeasurableAndNeverFiresFromTheCity()
        {
            StoryReadContext rich = StoryTestFixtures.Context(StoryTestFixtures.City(March1994,
                happiness: 99.0, unlockedFeatureIds: new[] { "feature-transit" },
                activePolicyIds: new[] { "policy-heavy-traffic-ban" }));

            Assert.Equal(CheckResult.Unmeasurable,
                         TriggerEvaluator.Evaluate(StoryTestFixtures.OfKind(TriggerKind.Manual, ""), rich));

            Assert.Equal(CheckResult.Unmeasurable, TriggerEvaluator.Evaluate(
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
        /// <b>Refuting a universal needs one real witness.</b> A district that genuinely measured the
        /// metric and fails it settles <c>AllDistricts</c> as not-met, whatever the district beside it
        /// could not read — the same asymmetry the 2-of-3 rule uses, and for the same reason: asserting
        /// "everywhere" means having looked everywhere, but refuting it needs only one counterexample.
        /// </summary>
        [Fact]
        public void Evaluate_AllDistricts_IsRefutedByASingleMeasuredCounterexample()
        {
            StoryReadContext context = WithDistricts(
                StoryTestFixtures.District("d00000001", uncollectedGarbage: 900.0,
                    fellBackOn: new[] { SnapshotAssembly.FieldUncollectedGarbage }),
                StoryTestFixtures.District("d00000002", uncollectedGarbage: 10.0));

            Assert.Equal(CheckResult.NotMet, TriggerEvaluator.Evaluate(
                StoryTestFixtures.Metric(MetricHistory.UncollectedGarbage, Comparison.GreaterThan, 500.0,
                                         TriggerScope.AllDistricts), context));
        }

        /// <summary>
        /// The mirror image on the other quantifier: nothing measured satisfies it and something was
        /// dark, so the honest answer is that we cannot tell — the missing district might have been
        /// the one that qualified.
        /// </summary>
        [Fact]
        public void Evaluate_AnyDistrict_IsUnmeasurableWhenNothingMeasuredQualifiesAndSomethingWasDark()
        {
            StoryReadContext context = WithDistricts(
                StoryTestFixtures.District("d00000001", uncollectedGarbage: 900.0,
                    fellBackOn: new[] { SnapshotAssembly.FieldUncollectedGarbage }),
                StoryTestFixtures.District("d00000002", uncollectedGarbage: 10.0));

            Assert.Equal(CheckResult.Unmeasurable, TriggerEvaluator.Evaluate(
                StoryTestFixtures.Metric(MetricHistory.UncollectedGarbage, Comparison.GreaterThan, 500.0,
                                         TriggerScope.AnyDistrict), context));
        }

        /// <summary>
        /// <b>The same city, the same dark district, two quantifiers, two different answers.</b> Stated
        /// as one test because the asymmetry <i>is</i> the ruling, and two tests that happened to agree
        /// would not show that it had been thought about.
        /// </summary>
        [Fact]
        public void Evaluate_TheTwoQuantifiersTreatADarkDistrictOppositely()
        {
            StoryReadContext context = WithDistricts(
                StoryTestFixtures.District("d00000001", uncollectedGarbage: 900.0,
                    fellBackOn: new[] { SnapshotAssembly.FieldUncollectedGarbage }),
                StoryTestFixtures.District("d00000002", uncollectedGarbage: 10.0));

            CheckResult any = TriggerEvaluator.Evaluate(
                StoryTestFixtures.Metric(MetricHistory.UncollectedGarbage, Comparison.GreaterThan, 500.0,
                                         TriggerScope.AnyDistrict), context);

            CheckResult all = TriggerEvaluator.Evaluate(
                StoryTestFixtures.Metric(MetricHistory.UncollectedGarbage, Comparison.GreaterThan, 500.0,
                                         TriggerScope.AllDistricts), context);

            Assert.Equal(CheckResult.Unmeasurable, any);
            Assert.Equal(CheckResult.NotMet, all);
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

        /// <summary>
        /// A city with no districts has nothing to read at district scope — <b>including
        /// <c>AllDistricts</c>, which does not get to be vacuously true.</b> Letting "every district"
        /// hold over an empty set would make a district story succeed on a city that has no districts
        /// drawn, which is the one city where the player provably did nothing.
        /// </summary>
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
        /// <b>A relative check is the ABSOLUTE difference from the story's open</b>, implemented by
        /// shifting the threshold: the spec holds when <c>today &gt;= baseline + Threshold</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Pinned exactly on the boundary rather than with values far either side, which is the whole
        /// point of this version: a fractional reading — "10% better than it was" — would answer
        /// differently at every row below, so this is the test that decides between the two rather
        /// than straddling them.
        /// </para>
        /// <para>
        /// A fractional form would be a new <see cref="TriggerKind"/> in a reviewed commit, not a
        /// reinterpretation of this one. Several metrics in the vocabulary are legitimately zero in a
        /// given month — <c>homeless</c>, <c>births</c>, <c>attractionCount</c> — and a fraction over
        /// a zero baseline is an infinity, which would serialise the evidence as invalid JSON.
        /// </para>
        /// </remarks>
        [Theory]
        [InlineData(59.0, CheckResult.Met)]     // 60 >= 59 + 1, exactly on the boundary
        [InlineData(58.0, CheckResult.Met)]
        [InlineData(60.0, CheckResult.NotMet)]  // 60 >= 61 is false, one short
        [InlineData(0.0, CheckResult.Met)]      // a zero baseline is arithmetic, not a special case
        public void EvaluateCheck_RelativeToBaseline_ShiftsTheThresholdByTheBaseline(
            double baseline, CheckResult expected)
        {
            StoryReadContext context = StoryTestFixtures.Context(
                StoryTestFixtures.City(March1994, happiness: 60.0));

            CheckSpec check = StoryTestFixtures.Check(
                StoryTestFixtures.Metric(MetricHistory.Happiness, Comparison.GreaterThanOrEqual, 1.0),
                relativeToBaseline: true);

            Assert.Equal(expected, TriggerEvaluator.EvaluateCheck(check, baseline, context));
        }

        /// <summary>
        /// A negative threshold is how an event asks the player to hold a line rather than improve it:
        /// "no worse than five below where you started".
        /// </summary>
        [Theory]
        [InlineData(64.0, CheckResult.Met)]     // 60 >= 64 - 5
        [InlineData(66.0, CheckResult.NotMet)]  // 60 >= 61 is false
        public void EvaluateCheck_RelativeToBaseline_HandlesANegativeThreshold(
            double baseline, CheckResult expected)
        {
            StoryReadContext context = StoryTestFixtures.Context(
                StoryTestFixtures.City(March1994, happiness: 60.0));

            CheckSpec check = StoryTestFixtures.Check(
                StoryTestFixtures.Metric(MetricHistory.Happiness, Comparison.GreaterThanOrEqual, -5.0),
                relativeToBaseline: true);

            Assert.Equal(expected, TriggerEvaluator.EvaluateCheck(check, baseline, context));
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

        // --- recorded evidence is keyed by metric AND district ------------------------------------

        private static CheckSpec DistrictGarbageCheck(double threshold, TriggerScope scope) =>
            StoryTestFixtures.Check(StoryTestFixtures.Metric(
                MetricHistory.UncollectedGarbage, Comparison.GreaterThan, threshold, scope));

        /// <summary>
        /// <b>A reading recorded for district A must not answer a check on district B.</b> This is the
        /// specific wrong answer <c>MetricReading.DistrictId</c> was added to prevent, and it is the
        /// one case in the suite that a metric-only implementation would pass — matching on the metric
        /// alone, the north side's recorded garbage answers confidently for the south side's, replay
        /// believes it permanently, and nothing downstream can tell.
        /// </summary>
        /// <remarks>
        /// Asserted through <c>AllDistricts</c> with the two districts' recorded values on opposite
        /// sides of the threshold. A metric-only lookup would find whichever reading it reached first
        /// and give both districts the same answer, so this fails whichever one it picks: it either
        /// reports Met for a district recorded at 10, or NotMet for one recorded at 900.
        /// </remarks>
        [Fact]
        public void EvaluateCheck_DoesNotLetOneDistrictsRecordedReadingAnswerForAnother()
        {
            StoryReadContext context = StoryTestFixtures.WithEvidence(
                WithDistricts(
                    StoryTestFixtures.District("d00000001", uncollectedGarbage: 0.0),
                    StoryTestFixtures.District("d00000002", uncollectedGarbage: 0.0)),
                StoryTestFixtures.Reading(MetricHistory.UncollectedGarbage, 900.0, "d00000001"),
                StoryTestFixtures.Reading(MetricHistory.UncollectedGarbage, 10.0, "d00000002"));

            // Only d00000001 clears 500, so "any" holds and "all" does not. A lookup that ignored the
            // district id would have to answer both the same way.
            Assert.Equal(CheckResult.Met, TriggerEvaluator.EvaluateCheck(
                DistrictGarbageCheck(500.0, TriggerScope.AnyDistrict), null, context));

            Assert.Equal(CheckResult.NotMet, TriggerEvaluator.EvaluateCheck(
                DistrictGarbageCheck(500.0, TriggerScope.AllDistricts), null, context));
        }

        /// <summary>
        /// A city-wide recorded reading must not answer a district-scoped check either. The empty
        /// district id is a positive claim — "this reading is the city's" — not a wildcard.
        /// </summary>
        [Fact]
        public void EvaluateCheck_DoesNotLetACityReadingAnswerADistrictScopedCheck()
        {
            StoryReadContext context = StoryTestFixtures.WithEvidence(
                WithDistricts(StoryTestFixtures.District("d00000001", uncollectedGarbage: 900.0,
                    fellBackOn: new[] { SnapshotAssembly.FieldUncollectedGarbage })),
                StoryTestFixtures.Reading(MetricHistory.UncollectedGarbage, 900.0));

            // The one district is dark and no reading was recorded FOR it, so there is nothing to
            // score against — the city's 900 is not a measurement of this district.
            Assert.Equal(CheckResult.Unmeasurable, TriggerEvaluator.EvaluateCheck(
                DistrictGarbageCheck(500.0, TriggerScope.AnyDistrict), null, context));
        }

        /// <summary>
        /// <b>A null <c>DistrictId</c> reads as the empty one.</b> Both mean "no district", and a
        /// reading deserialised from a sidecar written before the field existed carries null where a
        /// freshly built one carries <c>""</c>.
        /// </summary>
        /// <remarks>
        /// Without the equivalence every recorded reading in every existing save silently stops
        /// matching and falls through to a fresh measurement — which is exactly the determinism hole
        /// the field was added to close, reintroduced by the migration rather than by the design.
        /// </remarks>
        [Fact]
        public void EvaluateCheck_TreatsANullDistrictIdAsTheCityReading()
        {
            var cityReading = new MetricReading
            {
                MetricId = MetricHistory.Happiness,
                DistrictId = null!,
                Value = 90.0
            };

            var context = new StoryReadContext
            {
                Today = StoryTestFixtures.City(March1994, happiness: 10.0),
                RecordedEvidence = new List<MetricReading> { cityReading }
            };

            CheckSpec check = StoryTestFixtures.Check(
                StoryTestFixtures.Metric(MetricHistory.Happiness, Comparison.GreaterThanOrEqual, 80.0));

            Assert.Equal(CheckResult.Met, TriggerEvaluator.EvaluateCheck(check, null, context));
        }

        /// <summary>
        /// The equivalence runs one way only: null and empty are both "the city", but a district's
        /// reading is still not the city's. Leniency about null must not become leniency about which
        /// district a number came from.
        /// </summary>
        [Fact]
        public void EvaluateCheck_DoesNotLetADistrictReadingAnswerACityScopedCheck()
        {
            StoryReadContext context = StoryTestFixtures.WithEvidence(
                StoryTestFixtures.Context(StoryTestFixtures.City(March1994, happiness: 10.0)),
                StoryTestFixtures.Reading(MetricHistory.Happiness, 90.0, "d00000001"));

            CheckSpec check = StoryTestFixtures.Check(
                StoryTestFixtures.Metric(MetricHistory.Happiness, Comparison.GreaterThanOrEqual, 80.0));

            // No city reading was recorded, so the live snapshot's 10 decides it — the district's 90
            // is a different measurement and must not stand in.
            Assert.Equal(CheckResult.NotMet, TriggerEvaluator.EvaluateCheck(check, null, context));
        }

        /// <summary>
        /// A recorded null at district scope short-circuits to unreadable rather than sending the
        /// evaluator back to the district for a second opinion. The record says the metric could not
        /// be read when the evidence was taken; re-measuring would answer a different month's question.
        /// </summary>
        [Fact]
        public void EvaluateCheck_TreatsARecordedNullDistrictReadingAsUnmeasurable()
        {
            StoryReadContext context = StoryTestFixtures.WithEvidence(
                WithDistricts(StoryTestFixtures.District("d00000001", uncollectedGarbage: 900.0)),
                StoryTestFixtures.Reading(MetricHistory.UncollectedGarbage, null, "d00000001"));

            Assert.Equal(CheckResult.Unmeasurable, TriggerEvaluator.EvaluateCheck(
                DistrictGarbageCheck(500.0, TriggerScope.AnyDistrict), null, context));
        }

        /// <summary>
        /// And on both legs of a delta: a recorded null at either end makes the whole delta unreadable,
        /// rather than letting the evaluator fill the missing end from the live city and report a
        /// change measured against two different months.
        /// </summary>
        [Fact]
        public void EvaluateCheck_TreatsARecordedNullAsUnmeasurableOnBothLegsOfADelta()
        {
            CheckSpec delta = StoryTestFixtures.Check(StoryTestFixtures.Delta(
                MetricHistory.UncollectedGarbage, Comparison.GreaterThan, 0.0, 1,
                TriggerScope.AnyDistrict));

            StoryReadContext today = StoryTestFixtures.Context(
                StoryTestFixtures.City(March1994, districts: new[]
                {
                    StoryTestFixtures.District("d00000001", uncollectedGarbage: 900.0)
                }),
                StoryTestFixtures.City(March1994.AddMonths(-1), districts: new[]
                {
                    StoryTestFixtures.District("d00000001", uncollectedGarbage: 10.0)
                }));

            StoryReadContext recorded = StoryTestFixtures.WithEvidence(today,
                StoryTestFixtures.Reading(MetricHistory.UncollectedGarbage, null, "d00000001"));

            Assert.Equal(CheckResult.Unmeasurable, TriggerEvaluator.EvaluateCheck(delta, null, recorded));
        }
    }
}
