using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Engine.Blocs;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Engine packet 1 — bloc construction and issue weights.
    ///
    /// <para>
    /// Fixtures are synthetic <see cref="CitySnapshot"/> objects built in this file, per
    /// <c>/write-test</c>: they diff cleanly and they do not rot when the snapshot schema gains a
    /// field. Behavioural assertions check <em>direction</em> ("a longer commute raises transit
    /// salience") rather than pinned magnitudes, so retuning <c>engine_tuning.json</c> does not turn
    /// this file into a minefield. The two exceptions are the determinism hash and the cap, which are
    /// meant to be exact.
    /// </para>
    /// </summary>
    public class BlocConstructionTests
    {
        // A bloc that the tuned sensitivities make maximally exposed to transit:
        // low wealth (-0.25 × -1), highly educated (+0.25 × +1), child (-0.15 × -1).
        private static readonly BlocKey HighTransitExposure =
            new BlocKey(WealthTier.Low, EducationTier.HighlyEducated, AgeBand.Child);

        // ...and its mirror, which the same coefficients make barely exposed at all.
        private static readonly BlocKey LowTransitExposure =
            new BlocKey(WealthTier.High, EducationTier.Uneducated, AgeBand.Elderly);

        private static readonly BlocKey MiddleBloc =
            new BlocKey(WealthTier.Middle, EducationTier.Educated, AgeBand.Adult);

        // --- determinism -------------------------------------------------------------------------

        /// <summary>
        /// The canonical pattern: run twice from identical inputs, compare a hash of the serialized
        /// result rather than field by field. Hashing catches the field a hand-written assertion
        /// forgot, which is exactly where desyncs hide.
        /// </summary>
        [Fact]
        public void Build_ProducesIdenticalOutputTwice()
        {
            CitySnapshot city = TwoDistrictCity();
            EngineTuning tuning = EngineTuning.Default;

            Assert.Equal(
                Hash(BlocBuilder.Build(city, tuning)),
                Hash(BlocBuilder.Build(city, tuning)));
        }

        /// <summary>
        /// The negative half. Without it, a builder that returned a constant would pass the
        /// determinism test perfectly.
        /// </summary>
        [Fact]
        public void Build_DiffersWhenTheCityDiffers()
        {
            EngineTuning tuning = EngineTuning.Default;

            CitySnapshot calm = TwoDistrictCity();
            CitySnapshot gridlocked = MakeCity(
                new[] { MakeDistrict("north", commuteMinutes: 75.0), MakeDistrict("south") },
                commuteMinutes: 50.0);

            Assert.NotEqual(
                Hash(BlocBuilder.Build(calm, tuning)),
                Hash(BlocBuilder.Build(gridlocked, tuning)));
        }

        /// <summary>
        /// Bloc construction draws nothing, so it cannot depend on the save or the date. If a seeded
        /// stream is ever added here this test is the one that must be revisited deliberately.
        /// </summary>
        [Fact]
        public void Build_IsIndependentOfIterationOrderOfDistricts()
        {
            EngineTuning tuning = EngineTuning.Default;

            DistrictSnapshot north = MakeDistrict("north", commuteMinutes: 55.0);
            DistrictSnapshot south = MakeDistrict("south", rentBurden: 0.6);

            CitySnapshot inOrder = MakeCity(new[] { north, south });
            CitySnapshot reversed = MakeCity(new[] { south, north });

            Assert.Equal(
                Hash(BlocBuilder.Build(inOrder, tuning)),
                Hash(BlocBuilder.Build(reversed, tuning)));
        }

        [Fact]
        public void Build_OrdersByDistrictThenBlocOrdinal()
        {
            CitySnapshot city = MakeCity(new[] { MakeDistrict("south"), MakeDistrict("north") });

            List<Bloc> blocs = BlocBuilder.Build(city, EngineTuning.Default);

            var districtOrder = blocs.Select(b => b.DistrictId).Distinct().ToList();
            Assert.Equal(new[] { "north", "south" }, districtOrder);

            foreach (string district in districtOrder)
            {
                var ordinals = blocs.Where(b => b.DistrictId == district).Select(b => b.Key.Ordinal).ToList();
                Assert.Equal(ordinals.OrderBy(o => o).ToList(), ordinals);
            }
        }

        // --- composition and head counts ---------------------------------------------------------

        /// <summary>
        /// Largest-remainder apportionment: every citizen lands in exactly one bloc. Rounding each
        /// cell independently would quietly manufacture or disenfranchise voters, and the election
        /// packet counts integers.
        /// </summary>
        [Fact]
        public void Build_ConservesDistrictPopulationExactly()
        {
            // Deliberately not divisible by 60, so the remainder distribution actually runs.
            DistrictSnapshot district = MakeDistrict("north", population: 60007);
            CitySnapshot city = MakeCity(new[] { district });

            List<Bloc> blocs = BlocBuilder.Build(city, EngineTuning.Default);

            Assert.Equal(BlocAxes.BlocCount, blocs.Count);
            Assert.Equal(60007, blocs.Sum(b => b.Population));
            Assert.Equal(1.0, blocs.Sum(b => b.PopulationShare), 9);
        }

        [Fact]
        public void Build_SkewedDemographicsPutMorePeopleInTheDominantTier()
        {
            DistrictSnapshot poor = MakeDistrict("north", wealth: new WealthDistribution(0.70, 0.15, 0.15));
            List<Bloc> blocs = BlocBuilder.Build(MakeCity(new[] { poor }), EngineTuning.Default);

            int low = blocs.Where(b => b.Key.Wealth == WealthTier.Low).Sum(b => b.Population);
            int high = blocs.Where(b => b.Key.Wealth == WealthTier.High).Sum(b => b.Population);

            Assert.True(low > high * 4, "70/15 wealth split should put roughly 4.7x as many people in the low tier.");
        }

        [Fact]
        public void Build_PrunesCellsBelowTheTunedThresholds()
        {
            // 900 people over 60 cells is 15 each — under blocs.minBlocPopulation (25).
            DistrictSnapshot hamlet = MakeDistrict("hamlet", population: 900);

            List<Bloc> blocs = BlocBuilder.Build(MakeCity(new[] { hamlet }), EngineTuning.Default);

            Assert.True(blocs.Count < BlocAxes.BlocCount, "Cells under the head-count floor must be pruned.");
        }

        /// <summary>
        /// A village must still be able to vote. Pruning that emptied a district would silently
        /// disenfranchise it, so the largest cell survives regardless — and the tie-break is the
        /// lowest ordinal, never a coin flip.
        /// </summary>
        [Fact]
        public void Build_NeverPrunesADistrictOutOfExistence()
        {
            DistrictSnapshot village = MakeDistrict("village", population: 30);

            List<Bloc> blocs = BlocBuilder.Build(MakeCity(new[] { village }), EngineTuning.Default);

            Assert.Single(blocs);
            Assert.True(blocs[0].Population > 0);
        }

        [Fact]
        public void Build_IgnoresEmptyAndUnmeasurableDistricts()
        {
            DistrictSnapshot empty = MakeDistrict("empty", population: 0);
            DistrictSnapshot unmeasured = MakeDistrict("unmeasured");
            unmeasured.Wealth = new WealthDistribution(0, 0, 0);

            List<Bloc> blocs = BlocBuilder.Build(MakeCity(new[] { empty, unmeasured }), EngineTuning.Default);

            Assert.Empty(blocs);
        }

        // --- enfranchisement ---------------------------------------------------------------------

        /// <summary>
        /// Minors are disenfranchised by <c>turnout.ageBandMultipliers</c> being zero, never by a
        /// missing bloc — the dashboard still shows the whole population, and a future setting could
        /// enfranchise 16-year-olds without a schema change.
        /// </summary>
        [Fact]
        public void Build_MinorsExistButCannotVote()
        {
            List<Bloc> blocs = BlocBuilder.Build(TwoDistrictCity(), EngineTuning.Default);

            var minors = blocs.Where(b => b.Key.Age == AgeBand.Child || b.Key.Age == AgeBand.Teen).ToList();
            var adults = blocs.Where(b => b.Key.Age == AgeBand.Adult || b.Key.Age == AgeBand.Elderly).ToList();

            Assert.NotEmpty(minors);
            Assert.All(minors, b => Assert.True(b.Population > 0));
            Assert.All(minors, b => Assert.Equal(0, b.EligibleVoters));
            Assert.All(adults, b => Assert.Equal(b.Population, b.EligibleVoters));
        }

        // --- issue weights fall out of lived metrics ---------------------------------------------

        [Fact]
        public void Weights_LongerCommuteRaisesTransitSalience()
        {
            EngineTuning tuning = EngineTuning.Default;

            DistrictSnapshot easy = MakeDistrict("north", commuteMinutes: 20.0);
            DistrictSnapshot grim = MakeDistrict("south", commuteMinutes: 70.0);
            CitySnapshot city = MakeCity(new[] { easy, grim }, commuteMinutes: 45.0);

            List<Bloc> blocs = BlocBuilder.Build(city, tuning);

            double easyTransit = Weight(blocs, "north", MiddleBloc, Issue.Transit);
            double grimTransit = Weight(blocs, "south", MiddleBloc, Issue.Transit);

            Assert.True(grimTransit > easyTransit,
                $"70-minute commute should out-weigh a 20-minute one ({grimTransit} vs {easyTransit}).");
        }

        [Fact]
        public void Weights_HigherRentBurdenRaisesCostOfLivingSalience()
        {
            DistrictSnapshot affordable = MakeDistrict("north", rentBurden: 0.15);
            DistrictSnapshot squeezed = MakeDistrict("south", rentBurden: 0.75);
            CitySnapshot city = MakeCity(new[] { affordable, squeezed }, rentBurden: 0.45);

            List<Bloc> blocs = BlocBuilder.Build(city, EngineTuning.Default);

            Assert.True(
                Weight(blocs, "south", MiddleBloc, Issue.CostOfLiving) >
                Weight(blocs, "north", MiddleBloc, Issue.CostOfLiving));
        }

        [Fact]
        public void Weights_WorseServiceCoverageRaisesServicesSalience()
        {
            DistrictSnapshot served = MakeDistrict("north", serviceLevel: 0.95);
            DistrictSnapshot neglected = MakeDistrict("south", serviceLevel: 0.20);
            CitySnapshot city = MakeCity(new[] { served, neglected }, serviceLevel: 0.575);

            List<Bloc> blocs = BlocBuilder.Build(city, EngineTuning.Default);

            Assert.True(
                Weight(blocs, "south", MiddleBloc, Issue.Services) >
                Weight(blocs, "north", MiddleBloc, Issue.Services));
        }

        /// <summary>
        /// §4.3's example, stated precisely: a long commute raises transit weight <em>for commuting
        /// blocs</em>. Exposure comes from the composition sensitivities, so the same city street
        /// moves two blocs by different amounts.
        /// </summary>
        [Fact]
        public void Weights_LivedShiftScalesWithBlocExposure()
        {
            BlocsTuning t = EngineTuning.Default.Blocs;
            // 0.4 rather than 1.0 so this measures exposure, not the cap — the cap has its own test.
            var pressure = new LivedPressure(0, 0, 0, 0.4, 0, 0);
            var calm = LivedPressure.None;

            double exposed = BlocIssueModel
                .LivedShift(BlocIssueModel.CompositionWeights(HighTransitExposure, t), pressure, calm, t)
                .Transit;
            double sheltered = BlocIssueModel
                .LivedShift(BlocIssueModel.CompositionWeights(LowTransitExposure, t), pressure, calm, t)
                .Transit;

            Assert.True(exposed > sheltered,
                $"Commuting blocs should feel the commute harder ({exposed} vs {sheltered}).");
            Assert.True(sheltered > 0.0, "Even a sheltered bloc notices, it just notices less.");
        }

        /// <summary>
        /// Half the lived signal is the district measured against the city. A district that is exactly
        /// average contributes no <em>relative</em> grievance — which is also why a district whose
        /// metrics all fell back to city values invents no false local anger.
        /// </summary>
        [Fact]
        public void Weights_RelativeComponentCancelsWhenDistrictMatchesCity()
        {
            BlocsTuning t = EngineTuning.Default.Blocs;
            var pressure = new LivedPressure(0, 0, 0, 0.6, 0, 0);

            double absoluteOnly = BlocIssueModel
                .LivedShift(BlocIssueModel.CompositionWeights(MiddleBloc, t), pressure, pressure, t)
                .Transit;
            double expected = t.LivedMetricWeightGain * 0.6
                            * BlocIssueModel.CompositionWeights(MiddleBloc, t).Transit / t.IssueWeightPriors.Transit;

            Assert.Equal(expected, absoluteOnly, 12);
        }

        [Fact]
        public void Weights_WorseThanTheCityCountsForMoreThanTheSameAbsoluteGrievance()
        {
            BlocsTuning t = EngineTuning.Default.Blocs;
            IssueWeights composition = BlocIssueModel.CompositionWeights(MiddleBloc, t);
            var districtPain = new LivedPressure(0, 0, 0, 0.6, 0, 0);

            double worseThanTown = BlocIssueModel.LivedShift(composition, districtPain, LivedPressure.None, t).Transit;
            double asBadAsTown = BlocIssueModel.LivedShift(composition, districtPain, districtPain, t).Transit;

            Assert.True(worseThanTown > asBadAsTown);
        }

        // --- caps --------------------------------------------------------------------------------

        /// <summary>
        /// The lived-metric cap, driven past the limit in both directions. A cap that only holds for
        /// positive magnitudes is not a cap.
        /// </summary>
        [Fact]
        public void LivedShift_IsCappedInBothDirections()
        {
            BlocsTuning t = EngineTuning.Default.Blocs;
            double cap = t.LivedMetricMaxShift;

            var worst = new LivedPressure(1, 1, 1, 1, 1, 1);
            var best = LivedPressure.None;

            IssueWeights composition = BlocIssueModel.CompositionWeights(HighTransitExposure, t);

            IssueWeights hellish = BlocIssueModel.LivedShift(composition, worst, best, t);
            IssueWeights blessed = BlocIssueModel.LivedShift(composition, best, worst, t);

            foreach (Issue issue in Issues.All)
            {
                Assert.InRange(hellish[issue], -cap, cap);
                Assert.InRange(blessed[issue], -cap, cap);
            }

            // And it actually binds, rather than being a cap nothing ever reaches: the transit term
            // is 0.35 gain x 2.0 signal x 1.65 exposure = 1.155, far past the 0.5 ceiling.
            Assert.Equal(cap, hellish.Transit, 12);
            Assert.Equal(-cap, blessed.Transit, 12);
        }

        [Fact]
        public void Weights_StayFiniteAndNormalisedUnderExtremeCities()
        {
            EngineTuning tuning = EngineTuning.Default;

            DistrictSnapshot hell = MakeDistrict(
                "hell", happiness: 0.0, unemployment: 1.0, commuteMinutes: 600.0, congestion: 1.0,
                rentBurden: 3.0, rentTrend: 5.0, landValueTrend: 5.0, crimeRate: 1.0, sickRate: 1.0,
                serviceLevel: 0.0, pollutionLevel: 1.0);
            DistrictSnapshot eden = MakeDistrict(
                "eden", happiness: 100.0, unemployment: 0.0, commuteMinutes: 1.0, congestion: 0.0,
                rentBurden: 0.0, rentTrend: -1.0, landValueTrend: -1.0, crimeRate: 0.0, sickRate: 0.0,
                serviceLevel: 1.0, pollutionLevel: 0.0);

            List<Bloc> blocs = BlocBuilder.Build(MakeCity(new[] { hell, eden }), tuning);

            foreach (Bloc bloc in blocs)
            {
                foreach (Issue issue in Issues.All)
                {
                    double w = bloc.Weights[issue];
                    Assert.False(double.IsNaN(w) || double.IsInfinity(w), "Weights must stay finite.");
                    Assert.True(w > 0.0, "A weight of zero would erase an issue from the affinity kernel.");
                }

                // blocs.normalizeWeights is true, so total political energy is constant per bloc.
                Assert.Equal((double)Issues.Count, bloc.Weights.Sum(), 9);
                Assert.InRange(bloc.Discontent, 0.0, 1.0);
            }
        }

        // --- ideal points ------------------------------------------------------------------------

        [Fact]
        public void Ideal_StaysInsideTheUnitCubeForEveryBloc()
        {
            BlocsTuning t = EngineTuning.Default.Blocs;

            foreach (BlocKey key in BlocAxes.AllKeys)
            {
                IssuePosition ideal = BlocIssueModel.Ideal(key, t);
                foreach (Issue issue in Issues.All) Assert.InRange(ideal[issue], -1.0, 1.0);
            }
        }

        /// <summary>
        /// Sign convention check (+1 = spend/protect/restrict more). Wealth pulls the cost-of-living
        /// stance toward revenue and away from affordability, so the rich bloc must sit lower.
        /// </summary>
        [Fact]
        public void Ideal_WealthMovesTheCostOfLivingStanceTowardRevenue()
        {
            BlocsTuning t = EngineTuning.Default.Blocs;

            double poor = BlocIssueModel.Ideal(new BlocKey(WealthTier.Low, EducationTier.Educated, AgeBand.Adult), t).CostOfLiving;
            double rich = BlocIssueModel.Ideal(new BlocKey(WealthTier.High, EducationTier.Educated, AgeBand.Adult), t).CostOfLiving;

            Assert.True(poor > rich);
        }

        [Fact]
        public void Ideal_EducationMovesTheEnvironmentStanceTowardProtection()
        {
            BlocsTuning t = EngineTuning.Default.Blocs;

            double unschooled = BlocIssueModel.Ideal(new BlocKey(WealthTier.Middle, EducationTier.Uneducated, AgeBand.Adult), t).Environment;
            double graduate = BlocIssueModel.Ideal(new BlocKey(WealthTier.Middle, EducationTier.HighlyEducated, AgeBand.Adult), t).Environment;

            Assert.True(graduate > unschooled);
        }

        /// <summary>Ideal points are composition only — the city cannot argue a bloc out of what it wants.</summary>
        [Fact]
        public void Ideal_DoesNotMoveWithLivedMetrics()
        {
            EngineTuning tuning = EngineTuning.Default;

            var calm = MakeCity(new[] { MakeDistrict("north") });
            var awful = MakeCity(new[] { MakeDistrict("north", serviceLevel: 0.0, crimeRate: 1.0, commuteMinutes: 90.0) });

            IssuePosition a = Find(BlocBuilder.Build(calm, tuning), "north", MiddleBloc).Ideal;
            IssuePosition b = Find(BlocBuilder.Build(awful, tuning), "north", MiddleBloc).Ideal;

            foreach (Issue issue in Issues.All) Assert.Equal(a[issue], b[issue], 12);
        }

        // --- discontent --------------------------------------------------------------------------

        [Fact]
        public void Discontent_RisesAsHappinessFalls()
        {
            EngineTuning tuning = EngineTuning.Default;

            var content = MakeCity(new[] { MakeDistrict("north", happiness: 85.0) });
            var miserable = MakeCity(new[] { MakeDistrict("north", happiness: 10.0) });

            double low = Find(BlocBuilder.Build(content, tuning), "north", MiddleBloc).Discontent;
            double high = Find(BlocBuilder.Build(miserable, tuning), "north", MiddleBloc).Discontent;

            Assert.True(high > low, $"Misery should read as discontent ({high} vs {low}).");
            Assert.InRange(low, 0.0, 1.0);
            Assert.InRange(high, 0.0, 1.0);
        }

        /// <summary>
        /// Discontent is grievance weighted by salience, so the same collapsed service level lands
        /// harder on the blocs that prioritise services.
        /// </summary>
        [Fact]
        public void Discontent_VariesBetweenBlocsInOneDistrict()
        {
            var city = MakeCity(new[] { MakeDistrict("north", serviceLevel: 0.1, happiness: 40.0) });

            List<Bloc> blocs = BlocBuilder.Build(city, EngineTuning.Default);
            var distinct = blocs.Select(b => Math.Round(b.Discontent, 9)).Distinct().Count();

            Assert.True(distinct > 1, "A district-uniform discontent would waste the bloc model.");
        }

        // --- smoothing and carry-forward ---------------------------------------------------------

        /// <summary>
        /// Weight smoothing is an EMA against the <em>persisted</em> bloc. That is the only place it
        /// can live: smoothing state held in a field would not survive save/load, and politics that
        /// changed on reload is the desync non-negotiable #3 forbids.
        /// </summary>
        [Fact]
        public void Weights_SmoothTowardLastTickRatherThanJumping()
        {
            EngineTuning tuning = EngineTuning.Default;

            CitySnapshot before = MakeCity(new[] { MakeDistrict("north", commuteMinutes: 20.0) }, commuteMinutes: 20.0);
            CitySnapshot after = MakeCity(new[] { MakeDistrict("north", commuteMinutes: 90.0) }, commuteMinutes: 20.0);

            List<Bloc> tick1 = BlocBuilder.Build(before, tuning);
            List<Bloc> unsmoothed = BlocBuilder.Build(after, tuning);
            List<Bloc> smoothed = BlocBuilder.Build(after, tuning, tick1);

            double a = Find(tick1, "north", MiddleBloc).Weights.Transit;
            double jump = Find(unsmoothed, "north", MiddleBloc).Weights.Transit;
            double eased = Find(smoothed, "north", MiddleBloc).Weights.Transit;

            Assert.True(jump > a);
            Assert.True(eased > a && eased < jump,
                $"Smoothed weight {eased} should sit between {a} and {jump}.");
        }

        [Fact]
        public void Composition_SmoothsTowardLastTicksDemographics()
        {
            EngineTuning tuning = EngineTuning.Default;

            CitySnapshot poorCity = MakeCity(new[] { MakeDistrict("north", wealth: new WealthDistribution(0.70, 0.15, 0.15)) });
            CitySnapshot richCity = MakeCity(new[] { MakeDistrict("north", wealth: new WealthDistribution(0.15, 0.15, 0.70)) });

            List<Bloc> tick1 = BlocBuilder.Build(poorCity, tuning);
            List<Bloc> abrupt = BlocBuilder.Build(richCity, tuning);
            List<Bloc> eased = BlocBuilder.Build(richCity, tuning, tick1);

            int abruptLow = abrupt.Where(b => b.Key.Wealth == WealthTier.Low).Sum(b => b.Population);
            int easedLow = eased.Where(b => b.Key.Wealth == WealthTier.Low).Sum(b => b.Population);

            Assert.True(easedLow > abruptLow,
                "A district cannot gentrify wholesale in one month; the EMA holds the old composition.");
            Assert.Equal(60000, eased.Sum(b => b.Population));
        }

        [Fact]
        public void Build_CarriesThePreviousVoteForward()
        {
            EngineTuning tuning = EngineTuning.Default;
            CitySnapshot city = MakeCity(new[] { MakeDistrict("north") });

            List<Bloc> tick1 = BlocBuilder.Build(city, tuning);
            foreach (Bloc bloc in tick1)
            {
                bloc.PreviousVote = new List<PartyVoteShare>
                {
                    new PartyVoteShare("party-a", 0.6),
                    new PartyVoteShare("party-b", 0.4)
                };
            }

            List<Bloc> tick2 = BlocBuilder.Build(city, tuning, tick1);

            Bloc carried = Find(tick2, "north", MiddleBloc);
            Assert.Equal(2, carried.PreviousVote.Count);
            Assert.Equal("party-a", carried.PreviousVote[0].PartyId);

            // Copied, not aliased — mutating the new bloc must not reach into persisted state.
            carried.PreviousVote.Clear();
            Assert.Equal(2, Find(tick1, "north", MiddleBloc).PreviousVote.Count);
        }

        [Fact]
        public void Build_PropagatesCityFallbackMarking()
        {
            CitySnapshot city = MakeCity(new[]
            {
                MakeDistrict("north", hasCityFallbacks: true),
                MakeDistrict("south")
            });

            List<Bloc> blocs = BlocBuilder.Build(city, EngineTuning.Default);

            Assert.All(blocs.Where(b => b.DistrictId == "north"), b => Assert.True(b.HasCityFallbacks));
            Assert.All(blocs.Where(b => b.DistrictId == "south"), b => Assert.False(b.HasCityFallbacks));
        }

        // --- guards ------------------------------------------------------------------------------

        [Fact]
        public void Build_RejectsNullArguments()
        {
            Assert.Throws<ArgumentNullException>(() => BlocBuilder.Build(null!, EngineTuning.Default));
            Assert.Throws<ArgumentNullException>(() => BlocBuilder.Build(TwoDistrictCity(), null!));
        }

        [Fact]
        public void LivedPressure_StaysInsideTheUnitIntervalOnGarbageInput()
        {
            DistrictSnapshot nonsense = MakeDistrict(
                "broken", unemployment: 12.0, rentBurden: -4.0, crimeRate: 99.0,
                serviceLevel: 3.0, pollutionLevel: -2.0, commuteMinutes: -50.0);

            LivedPressure pressure = LivedPressure.ForDistrict(nonsense, EngineTuning.Default);

            foreach (Issue issue in Issues.All) Assert.InRange(pressure[issue], 0.0, 1.0);
        }

        // --- fixtures ----------------------------------------------------------------------------

        private static CitySnapshot TwoDistrictCity()
        {
            return MakeCity(new[] { MakeDistrict("north"), MakeDistrict("south") });
        }

        /// <summary>
        /// Uniform marginals: 3 x 5 x 4 cells at 1/60 each, comfortably clear of both prune
        /// thresholds at 60,000 people, so tests about weights are not silently tests about pruning.
        /// </summary>
        private static DistrictSnapshot MakeDistrict(
            string id,
            int population = 60000,
            double happiness = 50.0,
            double unemployment = 0.08,
            double commuteMinutes = 25.0,
            double congestion = 0.30,
            double rentBurden = 0.30,
            double rentTrend = 0.0,
            double landValueTrend = 0.0,
            double crimeRate = 0.10,
            double sickRate = 0.05,
            double serviceLevel = 0.70,
            double pollutionLevel = 0.20,
            bool hasCityFallbacks = false,
            WealthDistribution? wealth = null)
        {
            return new DistrictSnapshot
            {
                Id = id,
                Name = id,
                Population = population,
                Households = population / 2,
                Happiness = happiness,
                Unemployment = unemployment,
                Wealth = wealth ?? new WealthDistribution(1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0),
                Education = new EducationDistribution(0.2, 0.2, 0.2, 0.2, 0.2),
                Age = new AgeDistribution(0.25, 0.25, 0.25, 0.25),
                Pollution = new PollutionLevels(pollutionLevel, pollutionLevel, pollutionLevel, pollutionLevel),
                Services = UniformServices(serviceLevel),
                CrimeRate = crimeRate,
                SickRate = sickRate,
                AverageLandValue = 1000.0,
                LandValueTrend = landValueTrend,
                AverageRent = 800.0,
                RentTrend = rentTrend,
                RentBurden = rentBurden,
                TransitRidership = 0.20,
                AverageCommuteMinutes = commuteMinutes,
                TrafficCongestion = congestion,
                HasCityFallbacks = hasCityFallbacks,
                CityFallbackFields = new List<string>()
            };
        }

        private static CitySnapshot MakeCity(
            DistrictSnapshot[] districts,
            double happiness = 50.0,
            double unemployment = 0.08,
            double commuteMinutes = 25.0,
            double congestion = 0.30,
            double rentBurden = 0.30,
            double rentTrend = 0.0,
            double landValueTrend = 0.0,
            double crimeRate = 0.10,
            double sickRate = 0.05,
            double serviceLevel = 0.70,
            double pollutionLevel = 0.20)
        {
            // Deliberately NOT sorted here. CitySnapshot.Districts is contractually ordered by Id,
            // but the builder must not depend on a sensor honouring that, and two tests above check
            // exactly that by handing it the districts backwards.
            var given = districts.ToList();

            return new CitySnapshot
            {
                Date = new SimDate(1990, 1, 1),
                Population = given.Sum(d => d.Population),
                Households = given.Sum(d => d.Households),
                Happiness = happiness,
                Unemployment = unemployment,
                Money = 100000,
                Income = 5000,
                Expenses = 4000,
                BudgetBalance = 1000,
                Debt = 0,
                Wealth = new WealthDistribution(1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0),
                Education = new EducationDistribution(0.2, 0.2, 0.2, 0.2, 0.2),
                Age = new AgeDistribution(0.25, 0.25, 0.25, 0.25),
                Pollution = new PollutionLevels(pollutionLevel, pollutionLevel, pollutionLevel, pollutionLevel),
                Services = UniformServices(serviceLevel),
                Taxes = new TaxRates(0.10, 0.10, 0.10, 0.10),
                CrimeRate = crimeRate,
                SickRate = sickRate,
                AverageLandValue = 1000.0,
                LandValueTrend = landValueTrend,
                AverageRent = 800.0,
                RentTrend = rentTrend,
                RentBurden = rentBurden,
                TransitRidership = 0.20,
                AverageCommuteMinutes = commuteMinutes,
                TrafficCongestion = congestion,
                Districts = given
            };
        }

        private static ServiceCoverage UniformServices(double level)
        {
            return new ServiceCoverage(level, level, level, level, level, level, level, level, level);
        }

        // --- helpers -----------------------------------------------------------------------------

        private static Bloc Find(IEnumerable<Bloc> blocs, string districtId, BlocKey key)
        {
            Bloc? found = blocs.FirstOrDefault(b => b.DistrictId == districtId && b.Key == key);
            Assert.NotNull(found);
            return found!;
        }

        private static double Weight(IEnumerable<Bloc> blocs, string districtId, BlocKey key, Issue issue)
        {
            return Find(blocs, districtId, key).Weights[issue];
        }

        private static string Hash(IEnumerable<Bloc> blocs)
        {
            var text = new StringBuilder();

            foreach (Bloc bloc in blocs)
            {
                text.Append(bloc.DistrictId).Append('|')
                    .Append(bloc.Key.Id).Append('|')
                    .Append(bloc.Population.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(bloc.PopulationShare.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(bloc.EligibleVoters.ToString(CultureInfo.InvariantCulture)).Append('|');

                foreach (Issue issue in Issues.All)
                {
                    text.Append(bloc.Weights[issue].ToString("R", CultureInfo.InvariantCulture)).Append(',')
                        .Append(bloc.Ideal[issue].ToString("R", CultureInfo.InvariantCulture)).Append(';');
                }

                text.Append(bloc.Happiness.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(bloc.Discontent.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(bloc.HasCityFallbacks ? '1' : '0').Append('\n');
            }

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
                return BitConverter.ToString(hash).Replace("-", "");
            }
        }
    }
}
