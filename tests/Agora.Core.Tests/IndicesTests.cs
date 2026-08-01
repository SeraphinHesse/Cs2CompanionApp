using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Engine.Indices;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Packet 13 — derived indices.
    ///
    /// <para>
    /// Every index here has a hand-computed golden value on a synthetic snapshot, so the arithmetic
    /// is pinned rather than merely "non-crashing". The fixtures are deliberately round numbers: the
    /// expected value in each golden test is worked out in the comment above it, and if the formula
    /// changes the test must be re-derived by hand, not re-baselined from the output.
    /// </para>
    /// </summary>
    public class IndicesTests
    {
        private static readonly SimDate Jun2000 = new SimDate(2000, 6, 1);

        // =====================================================================================
        // Determinism
        // =====================================================================================

        /// <summary>
        /// The canonical pattern: run twice from identical inputs, compare a hash of the whole
        /// result rather than a handful of fields, because the field a hand-written assertion
        /// forgets is exactly where a desync hides.
        /// </summary>
        [Fact]
        public void Compute_ProducesIdenticalHashTwice()
        {
            Assert.Equal(
                Hash(IndicesEngine.Compute(RichInput(), EngineTuning.Default)),
                Hash(IndicesEngine.Compute(RichInput(), EngineTuning.Default)));
        }

        /// <summary>
        /// The negative half of the determinism pair. Without it, a Compute that returned a constant
        /// would pass the test above perfectly.
        /// </summary>
        [Fact]
        public void Compute_ProducesDifferentHash_ForDifferentInput()
        {
            IndicesInput changed = RichInput();
            changed.Snapshot.Happiness = changed.Snapshot.Happiness + 10.0;

            Assert.NotEqual(
                Hash(IndicesEngine.Compute(RichInput(), EngineTuning.Default)),
                Hash(IndicesEngine.Compute(changed, EngineTuning.Default)));
        }

        /// <summary>
        /// District order in the input must not leak into the output. The engine sorts a copy, so a
        /// sensor that emitted districts in ECS chunk order still produces the same serialized state.
        /// </summary>
        [Fact]
        public void Compute_SortsDistrictsById_RegardlessOfInputOrder()
        {
            IndicesInput forward = RichInput();
            IndicesInput reversed = RichInput();
            reversed.Snapshot.Districts.Reverse();

            DerivedIndices a = IndicesEngine.Compute(forward, EngineTuning.Default);
            DerivedIndices b = IndicesEngine.Compute(reversed, EngineTuning.Default);

            Assert.Equal(new[] { "district-a", "district-b", "district-c" }, Ids(a));
            Assert.Equal(Hash(a), Hash(b));
        }

        /// <summary>Compute must not mutate the snapshot it was handed.</summary>
        [Fact]
        public void Compute_DoesNotMutateItsInput()
        {
            IndicesInput input = RichInput();
            List<string> before = Ids(input);

            IndicesEngine.Compute(input, EngineTuning.Default);

            Assert.Equal(before, Ids(input));
            Assert.Empty(input.Snapshot.Indices.Districts);
        }

        // =====================================================================================
        // Gini
        // =====================================================================================

        /// <summary>
        /// Golden value, worked by hand.
        ///
        /// Wealth 40% Low / 20% Middle / 40% High, income proxies 0 / 0.5 / 1.
        /// Mean income = 0.4·0 + 0.2·0.5 + 0.4·1 = 0.5.
        /// Lorenz breakpoints: (0.4, 0), (0.6, 0.1/0.5 = 0.2), (1.0, 1.0).
        /// Area = ½·0.4·(0+0) + ½·0.2·(0+0.2) + ½·0.4·(0.2+1.0) = 0 + 0.02 + 0.24 = 0.26.
        /// G = 1 − 2·0.26 = 0.48.
        ///
        /// Both breakpoints land on a 20-bucket edge (8/20 and 12/20), so the trapezoid sampling is
        /// exact here and the golden is the closed-form Gini, not an approximation artefact.
        /// </summary>
        [Fact]
        public void Gini_MatchesHandComputedGoldenValue()
        {
            double g = IndexFormulas.Gini(new WealthDistribution(0.4, 0.2, 0.4), 20);
            Assert.Equal(0.48, g, 9);
        }

        /// <summary>
        /// The same golden, but driven end to end through <see cref="IndicesEngine.Compute"/> on a
        /// synthetic snapshot, so the wiring from <c>CitySnapshot.Wealth</c> and
        /// <c>DistrictSnapshot.Wealth</c> into the published index is pinned too — a formula-level
        /// golden cannot catch the city and district figures being crossed.
        ///
        /// City wealth 40/20/40 → 0.48 (see the formula golden).
        /// District wealth 90/0/10 → the elite holds all measured income → 0.9.
        /// City commute 50 min against the 25-minute reference with congestion 0.5 → 0.5.
        /// District commute 75 min → σ(2) = 2/3, so 0.6·(2/3) + 0.4·0.25 = 0.4 + 0.1 = 0.5.
        /// </summary>
        [Fact]
        public void Compute_PublishesGiniAndCommuteMisery_ForCityAndDistrict()
        {
            DistrictSnapshot d = District("district-a", 1000);
            d.Wealth = new WealthDistribution(0.9, 0.0, 0.1);
            d.AverageCommuteMinutes = 75.0;
            d.TrafficCongestion = 0.25;

            CitySnapshot city = City(Jun2000, d);
            city.Wealth = new WealthDistribution(0.4, 0.2, 0.4);
            city.AverageCommuteMinutes = 50.0;
            city.TrafficCongestion = 0.5;

            DerivedIndices r = IndicesEngine.Compute(new IndicesInput { Snapshot = city }, EngineTuning.Default);

            Assert.Equal(0.48, r.GiniCoefficient, 9);
            Assert.Equal(0.5, r.CommuteMiseryIndex, 9);
            Assert.Equal(0.9, r.Districts[0].GiniCoefficient, 9);
            Assert.Equal(0.5, r.Districts[0].CommuteMiseryIndex, 9);
        }

        /// <summary>A city where everyone sits in one tier has nothing to be unequal about.</summary>
        [Theory]
        [InlineData(1.0, 0.0, 0.0)]
        [InlineData(0.0, 1.0, 0.0)]
        [InlineData(0.0, 0.0, 1.0)]
        public void Gini_IsZero_WhenEveryoneSharesOneTier(double low, double mid, double high)
        {
            Assert.Equal(0.0, IndexFormulas.Gini(new WealthDistribution(low, mid, high), 20), 9);
        }

        /// <summary>
        /// Direction: a mass of poor under a small elite is more unequal than a spread-out city.
        /// 90/0/10 works out to exactly 0.9 — the elite holds all the measured income.
        /// </summary>
        [Fact]
        public void Gini_RisesAsWealthConcentrates()
        {
            double spread = IndexFormulas.Gini(new WealthDistribution(0.4, 0.2, 0.4), 20);
            double concentrated = IndexFormulas.Gini(new WealthDistribution(0.9, 0.0, 0.1), 20);

            Assert.True(concentrated > spread, $"concentrated {concentrated} should exceed spread {spread}");
            Assert.Equal(0.9, concentrated, 9);
        }

        /// <summary>Gini never leaves its declared range, whatever nonsense the sensor hands over.</summary>
        [Theory]
        [InlineData(-5.0, 0.0, 12.0)]
        [InlineData(double.NaN, 0.5, 0.5)]
        [InlineData(0.0, 0.0, 0.0)]
        public void Gini_StaysInRange_ForDegenerateInput(double low, double mid, double high)
        {
            double g = IndexFormulas.Gini(new WealthDistribution(low, mid, high), 20);
            Assert.InRange(g, 0.0, 1.0);
        }

        // =====================================================================================
        // Gentrification
        // =====================================================================================

        /// <summary>
        /// Golden value, worked by hand, driven end-to-end through Compute so the 24-month history
        /// lookup is exercised too.
        ///
        /// rent     = σ(1.0) = 1/(1+1) = 0.5                    × 0.5 = 0.25
        /// education= σ((0.6 − 0.5)/0.5) = σ(0.2) = 0.2/1.2 = 1/6 × 0.3 = 0.05
        /// turnover = (0.5 − 0.4)/0.5 = 0.2                     × 0.2 = 0.04
        /// total    = 0.34
        /// </summary>
        [Fact]
        public void Gentrification_MatchesHandComputedGoldenValue()
        {
            DistrictSnapshot then = District("district-a", 1000);
            then.Education = EducationMix(0.5);
            then.Wealth = new WealthDistribution(0.5, 0.3, 0.2);

            DistrictSnapshot now = District("district-a", 1000);
            now.Education = EducationMix(0.6);
            now.Wealth = new WealthDistribution(0.4, 0.3, 0.3);
            now.RentTrend = 1.0;

            CitySnapshot past = City(Jun2000.AddMonths(-24), then);
            CitySnapshot present = City(Jun2000, now);

            var input = new IndicesInput { Snapshot = present, History = new[] { past } };
            DerivedIndices result = IndicesEngine.Compute(input, EngineTuning.Default);

            Assert.Equal(0.34, result.Districts[0].GentrificationIndex, 9);
        }

        /// <summary>
        /// With no history the two backward-looking legs read zero rather than being invented, so a
        /// brand-new save reports rent pressure only: σ(1.0)·0.5 = 0.25.
        /// </summary>
        [Fact]
        public void Gentrification_DropsHistoricalLegs_WhenThereIsNoHistory()
        {
            DistrictSnapshot now = District("district-a", 1000);
            now.Education = EducationMix(0.6);
            now.Wealth = new WealthDistribution(0.4, 0.3, 0.3);
            now.RentTrend = 1.0;

            var input = new IndicesInput { Snapshot = City(Jun2000, now) };
            DerivedIndices result = IndicesEngine.Compute(input, EngineTuning.Default);

            Assert.Equal(0.25, result.Districts[0].GentrificationIndex, 9);
        }

        /// <summary>Falling rents are not gentrification, and a negative trend must not go negative.</summary>
        [Fact]
        public void Gentrification_IsZero_WhenRentsFallAndNothingElseMoves()
        {
            DistrictSnapshot now = District("district-a", 1000);
            now.RentTrend = -0.5;

            var input = new IndicesInput { Snapshot = City(Jun2000, now) };
            DerivedIndices result = IndicesEngine.Compute(input, EngineTuning.Default);

            Assert.Equal(0.0, result.Districts[0].GentrificationIndex, 9);
        }

        // =====================================================================================
        // Brain drain
        // =====================================================================================

        /// <summary>
        /// Golden value, worked by hand, through Compute so the 12-month window lookup is exercised.
        ///
        /// education index 0.5 → 0.4       ⇒ relative fall (0.5−0.4)/0.5 = 0.2   × 0.6 = 0.12
        /// skilled head count 40 000 → 30 000 ⇒ relative fall 10 000/40 000 = 0.25 × 0.4 = 0.10
        /// total = 0.22
        /// </summary>
        [Fact]
        public void BrainDrain_MatchesHandComputedGoldenValue()
        {
            CitySnapshot past = City(Jun2000.AddMonths(-12));
            past.Population = 100000;
            past.Education = new EducationDistribution(0.2, 0.2, 0.2, 0.2, 0.2); // index 0.5, skilled 0.4

            CitySnapshot present = City(Jun2000);
            present.Population = 100000;
            present.Education = new EducationDistribution(0.3, 0.2, 0.2, 0.2, 0.1); // index 0.4, skilled 0.3

            var input = new IndicesInput { Snapshot = present, History = new[] { past } };
            DerivedIndices result = IndicesEngine.Compute(input, EngineTuning.Default);

            Assert.Equal(0.22, result.BrainDrainIndex, 9);
        }

        /// <summary>An unmeasured drain is not a drain. No history in the window reads zero.</summary>
        [Fact]
        public void BrainDrain_IsZero_WithoutHistory()
        {
            CitySnapshot present = City(Jun2000);
            present.Population = 100000;
            present.Education = new EducationDistribution(0.3, 0.2, 0.2, 0.2, 0.1);

            DerivedIndices result = IndicesEngine.Compute(new IndicesInput { Snapshot = present }, EngineTuning.Default);

            Assert.Equal(0.0, result.BrainDrainIndex, 9);
        }

        /// <summary>A city gaining graduates is not draining. The index floors at zero, never negative.</summary>
        [Fact]
        public void BrainDrain_IsZero_WhenTheCityGainsGraduates()
        {
            CitySnapshot past = City(Jun2000.AddMonths(-12));
            past.Population = 100000;
            past.Education = new EducationDistribution(0.3, 0.2, 0.2, 0.2, 0.1);

            CitySnapshot present = City(Jun2000);
            present.Population = 120000;
            present.Education = new EducationDistribution(0.2, 0.2, 0.2, 0.2, 0.2);

            var input = new IndicesInput { Snapshot = present, History = new[] { past } };
            DerivedIndices result = IndicesEngine.Compute(input, EngineTuning.Default);

            Assert.Equal(0.0, result.BrainDrainIndex, 9);
        }

        // =====================================================================================
        // Commute misery
        // =====================================================================================

        /// <summary>
        /// Golden value: 50 minutes against a 25-minute reference is an overrun of 1.0, so
        /// σ(1.0) = 0.5, and congestion 0.5 passes straight through.
        /// 0.6·0.5 + 0.4·0.5 = 0.5.
        /// </summary>
        [Fact]
        public void CommuteMisery_MatchesHandComputedGoldenValue()
        {
            Assert.Equal(0.5, IndexFormulas.CommuteMisery(50.0, 0.5, EngineTuning.Default.Indices), 9);
        }

        /// <summary>A commute at or under the reference with free-flowing roads is painless.</summary>
        [Theory]
        [InlineData(25.0)]
        [InlineData(10.0)]
        [InlineData(0.0)]
        public void CommuteMisery_IsZero_AtOrUnderReferenceWithNoCongestion(double minutes)
        {
            Assert.Equal(0.0, IndexFormulas.CommuteMisery(minutes, 0.0, EngineTuning.Default.Indices), 9);
        }

        /// <summary>Misery rises monotonically with commute length.</summary>
        [Fact]
        public void CommuteMisery_RisesWithCommuteLength()
        {
            IndicesTuning t = EngineTuning.Default.Indices;
            double shortRun = IndexFormulas.CommuteMisery(30.0, 0.2, t);
            double longRun = IndexFormulas.CommuteMisery(90.0, 0.2, t);

            Assert.True(longRun > shortRun, $"90 min ({longRun}) should beat 30 min ({shortRun})");
        }

        /// <summary>The declared range holds even for absurd sensor values.</summary>
        [Theory]
        [InlineData(1e9, 50.0)]
        [InlineData(double.PositiveInfinity, 1.0)]
        [InlineData(double.NaN, double.NaN)]
        [InlineData(-100.0, -100.0)]
        public void CommuteMisery_StaysInRange(double minutes, double congestion)
        {
            Assert.InRange(IndexFormulas.CommuteMisery(minutes, congestion, EngineTuning.Default.Indices), 0.0, 1.0);
        }

        // =====================================================================================
        // Service inequality
        // =====================================================================================

        /// <summary>
        /// Golden value, worked by hand. Two equally populated districts, identical on eight
        /// services and split 1.0 / 0.0 on health.
        ///
        /// Health: weighted mean 0.5, MAD = ½·0.5 + ½·0.5 = 0.5, dispersion = 2·0.5 = 1.0.
        /// Everything else: dispersion 0.
        /// Weight sum = 1 + 1 + 1 + 0.8 + 0.8 + 1 + 0.6 + 0.6 + 0.5 = 7.3.
        /// Index = (1.0 × 1.0) / 7.3 = 0.136986301369863.
        /// </summary>
        [Fact]
        public void ServiceInequality_MatchesHandComputedGoldenValue()
        {
            DistrictSnapshot a = District("district-a", 1000);
            a.Services = Coverage(health: 1.0, rest: 0.5);
            DistrictSnapshot b = District("district-b", 1000);
            b.Services = Coverage(health: 0.0, rest: 0.5);

            DerivedIndices result = IndicesEngine.Compute(
                new IndicesInput { Snapshot = City(Jun2000, a, b) }, EngineTuning.Default);

            Assert.Equal(1.0 / 7.3, result.ServiceInequalityIndex, 9);
        }

        /// <summary>Identical districts are perfectly equal, not merely "close to zero".</summary>
        [Fact]
        public void ServiceInequality_IsZero_WhenDistrictsAreIdentical()
        {
            DistrictSnapshot a = District("district-a", 1000);
            a.Services = Coverage(health: 0.7, rest: 0.7);
            DistrictSnapshot b = District("district-b", 4000);
            b.Services = Coverage(health: 0.7, rest: 0.7);

            DerivedIndices result = IndicesEngine.Compute(
                new IndicesInput { Snapshot = City(Jun2000, a, b) }, EngineTuning.Default);

            Assert.Equal(0.0, result.ServiceInequalityIndex, 9);
        }

        /// <summary>
        /// A district whose service coverage is really the city's average must not be counted: doing
        /// so measures the city against itself and drags a genuinely unequal city toward "even".
        /// The result must equal the two-real-district golden exactly.
        /// </summary>
        [Fact]
        public void ServiceInequality_ExcludesDistrictsWhoseServicesAreACityFallback()
        {
            DistrictSnapshot a = District("district-a", 1000);
            a.Services = Coverage(health: 1.0, rest: 0.5);
            DistrictSnapshot b = District("district-b", 1000);
            b.Services = Coverage(health: 0.0, rest: 0.5);

            DistrictSnapshot fallback = District("district-c", 1000);
            fallback.Services = Coverage(health: 0.5, rest: 0.5);
            fallback.HasCityFallbacks = true;
            fallback.CityFallbackFields = new List<string> { "Services" };

            DerivedIndices withFallback = IndicesEngine.Compute(
                new IndicesInput { Snapshot = City(Jun2000, a, b, fallback) }, EngineTuning.Default);

            Assert.Equal(1.0 / 7.3, withFallback.ServiceInequalityIndex, 9);
            Assert.True(withFallback.Districts[2].HasCityFallbacks);
        }

        /// <summary>
        /// The control for the test above: counted as real, the third district changes the answer.
        /// Without this, "excluded" could be indistinguishable from "the maths ignores it anyway".
        /// </summary>
        [Fact]
        public void ServiceInequality_CountsTheThirdDistrict_WhenItIsNotAFallback()
        {
            DistrictSnapshot a = District("district-a", 1000);
            a.Services = Coverage(health: 1.0, rest: 0.5);
            DistrictSnapshot b = District("district-b", 1000);
            b.Services = Coverage(health: 0.0, rest: 0.5);
            DistrictSnapshot c = District("district-c", 1000);
            c.Services = Coverage(health: 0.5, rest: 0.5);

            DerivedIndices result = IndicesEngine.Compute(
                new IndicesInput { Snapshot = City(Jun2000, a, b, c) }, EngineTuning.Default);

            // Health MAD = (0.5 + 0.5 + 0)/3 = 1/3, dispersion = 2/3, index = (2/3)/7.3.
            Assert.Equal((2.0 / 3.0) / 7.3, result.ServiceInequalityIndex, 9);
        }

        /// <summary>One district cannot be unequal with itself.</summary>
        [Fact]
        public void ServiceInequality_IsZero_ForASingleDistrict()
        {
            DistrictSnapshot a = District("district-a", 1000);
            a.Services = Coverage(health: 1.0, rest: 0.0);

            DerivedIndices result = IndicesEngine.Compute(
                new IndicesInput { Snapshot = City(Jun2000, a) }, EngineTuning.Default);

            Assert.Equal(0.0, result.ServiceInequalityIndex, 9);
        }

        // =====================================================================================
        // Discontent, polarization, legitimacy
        // =====================================================================================

        /// <summary>
        /// Golden value: happiness 50/100 → 0.5 unhappy, unemployment 0.2, coverage 0.6 → 0.4
        /// underserved. 0.5·0.5 + 0.3·0.2 + 0.2·0.4 = 0.25 + 0.06 + 0.08 = 0.39.
        /// </summary>
        [Fact]
        public void Discontent_MatchesHandComputedGoldenValue()
        {
            Assert.Equal(0.39, IndexFormulas.Discontent(50.0, 0.2, 0.6, EngineTuning.Default.Indices), 9);
        }

        /// <summary>A perfectly happy, fully employed, fully served city has nothing to be cross about.</summary>
        [Fact]
        public void Discontent_IsZero_ForAPerfectCity()
        {
            Assert.Equal(0.0, IndexFormulas.Discontent(100.0, 0.0, 1.0, EngineTuning.Default.Indices), 9);
        }

        /// <summary>And the mirror image saturates at 1.</summary>
        [Fact]
        public void Discontent_IsOne_ForAMiserableCity()
        {
            Assert.Equal(1.0, IndexFormulas.Discontent(0.0, 1.0, 0.0, EngineTuning.Default.Indices), 9);
        }

        /// <summary>
        /// Golden value: shares 0.5 / 0.3 / 0.2 give Σs² = 0.25 + 0.09 + 0.04 = 0.38, so the
        /// Herfindahl complement is 0.62 and the three-party maximum is 1 − 1/3 = 2/3.
        /// 0.62 ÷ (2/3) = 0.93.
        /// </summary>
        [Fact]
        public void Polarization_MatchesHandComputedGoldenValue()
        {
            var shares = new[]
            {
                new PartyVoteShare("party-a", 0.5),
                new PartyVoteShare("party-b", 0.3),
                new PartyVoteShare("party-c", 0.2)
            };

            Assert.Equal(0.93, IndexFormulas.Polarization(shares, EngineTuning.Default.Indices), 9);
        }

        /// <summary>n equal parties is maximum fragmentation for any n ≥ 2.</summary>
        [Fact]
        public void Polarization_IsOne_ForEquallySizedParties()
        {
            var shares = new[]
            {
                new PartyVoteShare("party-a", 0.25),
                new PartyVoteShare("party-b", 0.25),
                new PartyVoteShare("party-c", 0.25),
                new PartyVoteShare("party-d", 0.25)
            };

            Assert.Equal(1.0, IndexFormulas.Polarization(shares, EngineTuning.Default.Indices), 9);
        }

        /// <summary>One party holding everything is the definition of an unfragmented system.</summary>
        [Fact]
        public void Polarization_IsZero_ForASinglePartySystem()
        {
            var shares = new[] { new PartyVoteShare("party-a", 1.0) };
            Assert.Equal(0.0, IndexFormulas.Polarization(shares, EngineTuning.Default.Indices), 9);

            var dominant = new[]
            {
                new PartyVoteShare("party-a", 1.0),
                new PartyVoteShare("party-b", 0.0)
            };
            Assert.Equal(0.0, IndexFormulas.Polarization(dominant, EngineTuning.Default.Indices), 9);
        }

        /// <summary>
        /// Golden value: turnout 0.6, mandate delivery 0.5 (one fulfilled, one defied at zero
        /// progress), stability 0.8. 0.4·0.6 + 0.35·0.5 + 0.25·0.8 = 0.24 + 0.175 + 0.2 = 0.615.
        /// </summary>
        [Fact]
        public void Legitimacy_MatchesHandComputedGoldenValue()
        {
            var input = new IndicesInput
            {
                Snapshot = City(Jun2000),
                LastElectionTurnout = 0.6,
                Government = new Coalition { Id = "gov-1", Stability = 0.8 },
                Mandates = new[]
                {
                    new Mandate { Id = "mandate-1", Status = MandateStatus.Fulfilled, Progress = 1.0 },
                    new Mandate { Id = "mandate-2", Status = MandateStatus.Defied, Progress = 0.0 }
                }
            };

            DerivedIndices result = IndicesEngine.Compute(input, EngineTuning.Default);

            Assert.Equal(0.615, result.LegitimacyIndex, 9);
        }

        /// <summary>
        /// A brand-new save has no election, no resolved mandate and no government. Legitimacy is
        /// eroded by measured failure, so with nothing measured every leg reads full and the index is
        /// clampMax — not zero, which would fire unrest on turn one.
        /// </summary>
        [Fact]
        public void Legitimacy_IsFull_WhenNothingHasBeenMeasuredYet()
        {
            DerivedIndices result = IndicesEngine.Compute(
                new IndicesInput { Snapshot = City(Jun2000) }, EngineTuning.Default);

            Assert.Equal(EngineTuning.Default.Indices.ClampMax, result.LegitimacyIndex, 9);
        }

        /// <summary>
        /// Unresolved mandates are not evidence of anything. A save with only pending and active
        /// promises scores the mandate leg as unmeasured, so legitimacy stays full.
        /// </summary>
        [Fact]
        public void Legitimacy_TreatsUnresolvedMandatesAsUnmeasured()
        {
            var input = new IndicesInput
            {
                Snapshot = City(Jun2000),
                Mandates = new[]
                {
                    new Mandate { Id = "mandate-1", Status = MandateStatus.Pending, Progress = 0.0 },
                    new Mandate { Id = "mandate-2", Status = MandateStatus.Active, Progress = 0.1 },
                    new Mandate { Id = "mandate-3", Status = MandateStatus.Abandoned, Progress = 0.0 }
                }
            };

            DerivedIndices result = IndicesEngine.Compute(input, EngineTuning.Default);

            Assert.Equal(EngineTuning.Default.Indices.ClampMax, result.LegitimacyIndex, 9);
        }

        /// <summary>Direction: defying every promise costs legitimacy against fulfilling every promise.</summary>
        [Fact]
        public void Legitimacy_FallsWhenMandatesAreDefied()
        {
            IndicesInput kept = LegitimacyInput(MandateStatus.Fulfilled);
            IndicesInput broken = LegitimacyInput(MandateStatus.Defied);

            double keptScore = IndicesEngine.Compute(kept, EngineTuning.Default).LegitimacyIndex;
            double brokenScore = IndicesEngine.Compute(broken, EngineTuning.Default).LegitimacyIndex;

            Assert.True(brokenScore < keptScore, $"defied {brokenScore} should sit below kept {keptScore}");
            Assert.Equal(0.35, keptScore - brokenScore, 9); // exactly the mandate weight
        }

        private static IndicesInput LegitimacyInput(MandateStatus status) => new IndicesInput
        {
            Snapshot = City(Jun2000),
            LastElectionTurnout = 1.0,
            Government = new Coalition { Id = "gov-1", Stability = 1.0 },
            Mandates = new[] { new Mandate { Id = "mandate-1", Status = status, Progress = status == MandateStatus.Fulfilled ? 1.0 : 0.0 } }
        };

        // =====================================================================================
        // Smoothing, clamping and tuning
        // =====================================================================================

        /// <summary>
        /// The EMA is <c>α·raw + (1−α)·previous</c> with α = 0.3, so a jump from 0 to 0.5 shows up
        /// as 0.15 this tick. Pinned by hand, because a smoothing bug looks exactly like a tuning
        /// change from the outside.
        /// </summary>
        [Fact]
        public void Smoothing_BlendsTheRawValueWithLastTick()
        {
            IndicesTuning t = EngineTuning.Default.Indices;

            Assert.Equal(0.15, IndexFormulas.Smooth(0.5, 0.0, t), 9);
            Assert.Equal(0.5, IndexFormulas.Smooth(0.5, null, t), 9);
        }

        /// <summary>
        /// End to end: last tick's zeroes damp this tick's discontent by exactly α. The city fixture
        /// scores 0.39 raw (see the Discontent golden), so the smoothed figure is 0.3 × 0.39 = 0.117.
        /// </summary>
        [Fact]
        public void Compute_SmoothsAgainstThePreviousTick()
        {
            CitySnapshot city = City(Jun2000);
            city.Happiness = 50.0;
            city.Unemployment = 0.2;
            city.Services = Coverage(health: 0.6, rest: 0.6);

            var input = new IndicesInput
            {
                Snapshot = city,
                Previous = new DerivedIndices { DiscontentIndex = 0.0 }
            };

            DerivedIndices result = IndicesEngine.Compute(input, EngineTuning.Default);

            Assert.Equal(0.117, result.DiscontentIndex, 9);
        }

        /// <summary>
        /// A district that did not exist last tick has nothing to smooth against and must pass its
        /// raw value through rather than blending against a stranger's history.
        /// </summary>
        [Fact]
        public void Compute_DoesNotSmoothANewDistrictAgainstAnotherDistrict()
        {
            DistrictSnapshot fresh = District("district-new", 1000);
            fresh.Happiness = 50.0;
            fresh.Unemployment = 0.2;
            fresh.Services = Coverage(health: 0.6, rest: 0.6);

            var input = new IndicesInput
            {
                Snapshot = City(Jun2000, fresh),
                Previous = new DerivedIndices
                {
                    Districts = { new DistrictIndices { DistrictId = "district-old", DiscontentIndex = 1.0 } }
                }
            };

            DerivedIndices result = IndicesEngine.Compute(input, EngineTuning.Default);

            Assert.Equal(0.39, result.Districts[0].DiscontentIndex, 9);
        }

        /// <summary>
        /// Every published index stays inside its declared <c>[0, 1]</c> range even when the sensor
        /// hands over infinities, NaNs and negative populations. The dashboard renders these on a
        /// shared scale; an out-of-range value is a rendering bug and a desync risk at once.
        /// </summary>
        [Fact]
        public void AllIndices_StayInDeclaredRange_ForAbsurdInput()
        {
            DistrictSnapshot d1 = District("district-a", -100);
            d1.Happiness = double.NegativeInfinity;
            d1.Unemployment = 7.0;
            d1.RentTrend = double.PositiveInfinity;
            d1.AverageCommuteMinutes = 1e12;
            d1.Services = new ServiceCoverage(5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0);

            DistrictSnapshot d2 = District("district-b", 0);
            d2.Services = new ServiceCoverage(-5.0, -5.0, -5.0, -5.0, -5.0, -5.0, -5.0, -5.0, -5.0);

            CitySnapshot city = City(Jun2000, d1, d2);
            city.Population = -5;
            city.Happiness = 1e6;
            city.Unemployment = double.PositiveInfinity;
            city.AverageCommuteMinutes = double.NaN;
            city.TrafficCongestion = -50.0;
            city.Wealth = new WealthDistribution(double.NaN, -1.0, 1e9);
            city.Services = new ServiceCoverage(double.NaN, -2.0, 5.0, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5);

            var input = new IndicesInput
            {
                Snapshot = city,
                LastElectionTurnout = 42.0,
                Government = new Coalition { Id = "gov-1", Stability = -3.0 },
                VoteShares = new[]
                {
                    new PartyVoteShare("party-a", double.NaN),
                    new PartyVoteShare("party-b", 1e9)
                }
            };

            DerivedIndices r = IndicesEngine.Compute(input, EngineTuning.Default);

            foreach (double v in CityValues(r)) Assert.InRange(v, 0.0, 1.0);
            foreach (DistrictIndices d in r.Districts)
                foreach (double v in DistrictValues(d))
                    Assert.InRange(v, 0.0, 1.0);
        }

        /// <summary>
        /// Every coefficient comes from <c>data/engine_tuning.json</c>, never from a literal in the
        /// engine. Retuning the commute reference from 25 to 50 minutes must move the answer; if it
        /// does not, someone inlined the constant.
        /// </summary>
        [Fact]
        public void Indices_ReadTheirCoefficientsFromTuning()
        {
            EngineTuning retuned = EngineTuning.FromJson(
                "{\"indices\":{\"commuteMiseryReferenceMinutes\":50.0,\"commuteMiseryTimeWeight\":0.6,\"commuteMiseryCongestionWeight\":0.4}}");

            Assert.Equal(50.0, retuned.Indices.CommuteMiseryReferenceMinutes, 9);

            double stock = IndexFormulas.CommuteMisery(50.0, 0.5, EngineTuning.Default.Indices);
            double loose = IndexFormulas.CommuteMisery(50.0, 0.5, retuned.Indices);

            Assert.Equal(0.5, stock, 9);
            Assert.Equal(0.2, loose, 9); // overrun is now zero; only the congestion leg remains
        }

        /// <summary>
        /// The Gini bucket count is a tuning key too. One bucket collapses the Lorenz curve to a
        /// single chord and reports no inequality at all — proof the key is actually consulted.
        /// </summary>
        [Fact]
        public void Gini_ConsultsTheBucketCount()
        {
            var wealth = new WealthDistribution(0.4, 0.2, 0.4);

            Assert.Equal(0.0, IndexFormulas.Gini(wealth, 1), 9);
            Assert.Equal(0.48, IndexFormulas.Gini(wealth, 20), 9);
        }

        // =====================================================================================
        // History selection
        // =====================================================================================

        /// <summary>
        /// The window picks the snapshot nearest the target date, not merely the oldest one on hand,
        /// and never a snapshot at or after the current date.
        /// </summary>
        [Fact]
        public void History_PicksTheSnapshotNearestTheWindow()
        {
            CitySnapshot near = City(Jun2000.AddMonths(-12));
            near.Population = 100000;
            near.Education = new EducationDistribution(0.2, 0.2, 0.2, 0.2, 0.2); // index 0.5, skilled 40 000

            CitySnapshot ancient = City(Jun2000.AddMonths(-120));
            ancient.Population = 100000;
            ancient.Education = new EducationDistribution(0.0, 0.0, 0.0, 0.0, 1.0); // index 1.0, skilled 100 000

            CitySnapshot future = City(Jun2000.AddMonths(6));
            future.Population = 100000;
            future.Education = new EducationDistribution(0.0, 0.0, 0.0, 0.0, 1.0);

            CitySnapshot present = City(Jun2000);
            present.Population = 100000;
            present.Education = new EducationDistribution(0.3, 0.2, 0.2, 0.2, 0.1); // index 0.4, skilled 30 000

            var input = new IndicesInput
            {
                Snapshot = present,
                History = new[] { ancient, near, future }
            };

            // Picking `near` reproduces the brain-drain golden exactly; picking `ancient` or `future`
            // would not.
            Assert.Equal(0.22, IndicesEngine.Compute(input, EngineTuning.Default).BrainDrainIndex, 9);
        }

        /// <summary>History order must not change the answer — selection is by distance, not position.</summary>
        [Fact]
        public void History_SelectionIsIndependentOfListOrder()
        {
            CitySnapshot a = City(Jun2000.AddMonths(-12));
            a.Population = 100000;
            a.Education = new EducationDistribution(0.2, 0.2, 0.2, 0.2, 0.2);

            CitySnapshot b = City(Jun2000.AddMonths(-60));
            b.Population = 100000;
            b.Education = new EducationDistribution(0.0, 0.0, 0.0, 0.0, 1.0);

            CitySnapshot present = City(Jun2000);
            present.Population = 100000;
            present.Education = new EducationDistribution(0.3, 0.2, 0.2, 0.2, 0.1);

            double forward = IndicesEngine.Compute(
                new IndicesInput { Snapshot = present, History = new[] { b, a } }, EngineTuning.Default).BrainDrainIndex;
            double backward = IndicesEngine.Compute(
                new IndicesInput { Snapshot = present, History = new[] { a, b } }, EngineTuning.Default).BrainDrainIndex;

            Assert.Equal(forward, backward, 12);
        }

        // =====================================================================================
        // Fixtures
        // =====================================================================================

        /// <summary>
        /// A synthetic city with three districts, two years of history, a government, resolved
        /// mandates and a previous tick — enough moving parts that a determinism regression has
        /// somewhere to hide.
        /// </summary>
        private static IndicesInput RichInput()
        {
            DistrictSnapshot a = District("district-a", 12000);
            a.Happiness = 62.0;
            a.Unemployment = 0.08;
            a.Wealth = new WealthDistribution(0.2, 0.5, 0.3);
            a.Education = EducationMix(0.62);
            a.Services = Coverage(health: 0.9, rest: 0.7);
            a.RentTrend = 0.35;
            a.AverageCommuteMinutes = 31.0;
            a.TrafficCongestion = 0.42;

            DistrictSnapshot b = District("district-b", 8000);
            b.Happiness = 44.0;
            b.Unemployment = 0.17;
            b.Wealth = new WealthDistribution(0.6, 0.3, 0.1);
            b.Education = EducationMix(0.31);
            b.Services = Coverage(health: 0.3, rest: 0.45);
            b.RentTrend = -0.05;
            b.AverageCommuteMinutes = 48.0;
            b.TrafficCongestion = 0.66;

            DistrictSnapshot c = District("district-c", 5000);
            c.Happiness = 71.0;
            c.Unemployment = 0.04;
            c.Wealth = new WealthDistribution(0.1, 0.4, 0.5);
            c.Education = EducationMix(0.78);
            c.Services = Coverage(health: 0.75, rest: 0.8);
            c.RentTrend = 0.9;
            c.AverageCommuteMinutes = 22.0;
            c.TrafficCongestion = 0.18;
            c.HasCityFallbacks = true;
            c.CityFallbackFields = new List<string> { "AverageRent" };

            CitySnapshot present = City(Jun2000, a, b, c);
            present.Population = 25000;
            present.Happiness = 58.0;
            present.Unemployment = 0.11;
            present.Wealth = new WealthDistribution(0.32, 0.42, 0.26);
            present.Education = new EducationDistribution(0.15, 0.2, 0.3, 0.25, 0.1);
            present.Services = Coverage(health: 0.66, rest: 0.62);
            present.AverageCommuteMinutes = 34.0;
            present.TrafficCongestion = 0.47;

            CitySnapshot past12 = City(Jun2000.AddMonths(-12), District("district-a", 11000), District("district-b", 7800));
            past12.Population = 22000;
            past12.Education = new EducationDistribution(0.12, 0.18, 0.3, 0.28, 0.12);

            CitySnapshot past24 = City(Jun2000.AddMonths(-24), DistrictWith("district-a", 0.55, 0.5), DistrictWith("district-b", 0.4, 0.62));
            past24.Population = 19000;
            past24.Education = new EducationDistribution(0.1, 0.18, 0.32, 0.28, 0.12);

            return new IndicesInput
            {
                Snapshot = present,
                History = new[] { past24, past12 },
                Previous = new DerivedIndices
                {
                    GiniCoefficient = 0.4,
                    BrainDrainIndex = 0.1,
                    ServiceInequalityIndex = 0.2,
                    CommuteMiseryIndex = 0.3,
                    PolarizationIndex = 0.8,
                    LegitimacyIndex = 0.7,
                    DiscontentIndex = 0.35,
                    Districts =
                    {
                        new DistrictIndices { DistrictId = "district-a", GentrificationIndex = 0.2, CommuteMiseryIndex = 0.3, ServiceCoverageIndex = 0.7, DiscontentIndex = 0.3, GiniCoefficient = 0.4 },
                        new DistrictIndices { DistrictId = "district-b", GentrificationIndex = 0.1, CommuteMiseryIndex = 0.5, ServiceCoverageIndex = 0.4, DiscontentIndex = 0.5, GiniCoefficient = 0.5 }
                    }
                },
                VoteShares = new[]
                {
                    new PartyVoteShare("party-alpha", 0.41),
                    new PartyVoteShare("party-beta", 0.34),
                    new PartyVoteShare("party-gamma", 0.25)
                },
                LastElectionTurnout = 0.63,
                Government = new Coalition { Id = "gov-1998-06", Stability = 0.72 },
                Mandates = new[]
                {
                    new Mandate { Id = "mandate-1", Status = MandateStatus.Fulfilled, Progress = 1.0 },
                    new Mandate { Id = "mandate-2", Status = MandateStatus.PartiallyFulfilled, Progress = 0.62 },
                    new Mandate { Id = "mandate-3", Status = MandateStatus.Defied, Progress = 0.11 },
                    new Mandate { Id = "mandate-4", Status = MandateStatus.Active, Progress = 0.4 }
                }
            };
        }

        private static CitySnapshot City(SimDate date, params DistrictSnapshot[] districts)
        {
            var snapshot = new CitySnapshot { Date = date };
            for (int i = 0; i < districts.Length; i++) snapshot.Districts.Add(districts[i]);
            return snapshot;
        }

        private static DistrictSnapshot District(string id, int population) => new DistrictSnapshot
        {
            Id = id,
            Name = id,
            Population = population,
            Wealth = new WealthDistribution(1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0),
            Education = EducationMix(0.5),
            Age = new AgeDistribution(0.2, 0.1, 0.55, 0.15),
            Services = Coverage(health: 0.5, rest: 0.5)
        };

        private static DistrictSnapshot DistrictWith(string id, double lowWealthShare, double educationIndex)
        {
            DistrictSnapshot d = District(id, 10000);
            d.Wealth = new WealthDistribution(lowWealthShare, 1.0 - lowWealthShare, 0.0);
            d.Education = EducationMix(educationIndex);
            return d;
        }

        /// <summary>
        /// An education mix whose <see cref="EducationDistribution.Index"/> is exactly
        /// <paramref name="index"/>: put everyone at the two extremes and slide the split.
        /// index = 0·(1−x) + 1·x, so x is the HighlyEducated share.
        /// </summary>
        private static EducationDistribution EducationMix(double index) =>
            new EducationDistribution(1.0 - index, 0.0, 0.0, 0.0, index);

        private static ServiceCoverage Coverage(double health, double rest) =>
            new ServiceCoverage(health, rest, rest, rest, rest, rest, rest, rest, rest);

        private static List<string> Ids(DerivedIndices indices)
        {
            var ids = new List<string>();
            foreach (DistrictIndices d in indices.Districts) ids.Add(d.DistrictId);
            return ids;
        }

        private static List<string> Ids(IndicesInput input)
        {
            var ids = new List<string>();
            foreach (DistrictSnapshot d in input.Snapshot.Districts) ids.Add(d.Id);
            return ids;
        }

        private static IEnumerable<double> CityValues(DerivedIndices r)
        {
            yield return r.GiniCoefficient;
            yield return r.BrainDrainIndex;
            yield return r.ServiceInequalityIndex;
            yield return r.CommuteMiseryIndex;
            yield return r.PolarizationIndex;
            yield return r.LegitimacyIndex;
            yield return r.DiscontentIndex;
        }

        private static IEnumerable<double> DistrictValues(DistrictIndices d)
        {
            yield return d.GentrificationIndex;
            yield return d.CommuteMiseryIndex;
            yield return d.ServiceCoverageIndex;
            yield return d.DiscontentIndex;
            yield return d.GiniCoefficient;
        }

        // =====================================================================================
        // Hashing
        // =====================================================================================

        /// <summary>
        /// SHA-256 over a canonical, culture-invariant rendering of the whole result. Round-tripping
        /// every field — rather than asserting on a chosen few — is what makes the determinism test
        /// catch the field nobody thought to check.
        /// </summary>
        private static string Hash(DerivedIndices indices)
        {
            var sb = new StringBuilder();
            AppendNum(sb, "gini", indices.GiniCoefficient);
            AppendNum(sb, "brainDrain", indices.BrainDrainIndex);
            AppendNum(sb, "serviceInequality", indices.ServiceInequalityIndex);
            AppendNum(sb, "commuteMisery", indices.CommuteMiseryIndex);
            AppendNum(sb, "polarization", indices.PolarizationIndex);
            AppendNum(sb, "legitimacy", indices.LegitimacyIndex);
            AppendNum(sb, "discontent", indices.DiscontentIndex);

            foreach (DistrictIndices d in indices.Districts)
            {
                sb.Append('[').Append(d.DistrictId).Append(']');
                AppendNum(sb, "gentrification", d.GentrificationIndex);
                AppendNum(sb, "commuteMisery", d.CommuteMiseryIndex);
                AppendNum(sb, "serviceCoverage", d.ServiceCoverageIndex);
                AppendNum(sb, "discontent", d.DiscontentIndex);
                AppendNum(sb, "gini", d.GiniCoefficient);
                sb.Append("fallbacks=").Append(d.HasCityFallbacks ? '1' : '0').Append(';');
            }

            using (var sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(digest.Length * 2);
                foreach (byte b in digest) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        private static void AppendNum(StringBuilder sb, string key, double value) =>
            sb.Append(key).Append('=').Append(value.ToString("R", CultureInfo.InvariantCulture)).Append(';');
    }
}
