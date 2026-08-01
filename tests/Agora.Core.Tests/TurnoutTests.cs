using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Engine.Turnout;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Packet 5 — the turnout model. Turnout feeds both poll error and seat allocation, so these
    /// tests care about three things in this order: that it is reproducible, that the caps and the
    /// age-band multiplier hold, and that each coefficient pushes turnout in the direction its name
    /// claims.
    /// </summary>
    public class TurnoutTests
    {
        private static readonly Guid SaveA = new Guid("11111111-2222-3333-4444-555555555555");
        private static readonly Guid SaveB = new Guid("99999999-8888-7777-6666-555555555555");
        private static readonly SimDate May1994 = new SimDate(1994, 5, 1);
        private static readonly SimDate Jun1994 = new SimDate(1994, 6, 1);

        /// <summary>
        /// Shipped tuning with the noise draw switched off, for the tests that compare two different
        /// blocs. Two different bloc keys draw from two different sub-streams, so without this the
        /// noise would not cancel and a direction assertion would be measuring the RNG.
        /// </summary>
        private static EngineTuning Quiet() => EngineTuning.FromJson("{\"turnout\":{\"noiseSigma\":0.0}}");

        // =====================================================================================
        // Determinism
        // =====================================================================================

        [Fact]
        public void Project_ProducesIdenticalOutputTwice()
        {
            TurnoutInputs a = FullCityInputs(SaveA, May1994);
            TurnoutInputs b = FullCityInputs(SaveA, May1994);

            Assert.Equal(
                Hash(TurnoutModel.Project(a, EngineTuning.Default)),
                Hash(TurnoutModel.Project(b, EngineTuning.Default)));
        }

        /// <summary>
        /// The negative half of the determinism pattern. Without it, a model that returned a constant
        /// would pass the test above perfectly.
        /// </summary>
        [Fact]
        public void Project_DiffersBySave()
        {
            Assert.NotEqual(
                Hash(TurnoutModel.Project(FullCityInputs(SaveA, May1994), EngineTuning.Default)),
                Hash(TurnoutModel.Project(FullCityInputs(SaveB, May1994), EngineTuning.Default)));
        }

        [Fact]
        public void Project_DiffersByDate()
        {
            Assert.NotEqual(
                Hash(TurnoutModel.Project(FullCityInputs(SaveA, May1994), EngineTuning.Default)),
                Hash(TurnoutModel.Project(FullCityInputs(SaveA, Jun1994), EngineTuning.Default)));
        }

        /// <summary>
        /// The input list arrives from a dictionary walk or an ECS query somewhere upstream. If its
        /// order could reach the output, one save would produce different politics per launch.
        /// </summary>
        [Fact]
        public void Project_IsIndependentOfInputOrder()
        {
            var forward = new List<Bloc>(FullCity());
            var reversed = new List<Bloc>(forward);
            reversed.Reverse();

            string a = Hash(TurnoutModel.Project(WithBlocs(forward, SaveA, May1994), EngineTuning.Default));
            string b = Hash(TurnoutModel.Project(WithBlocs(reversed, SaveA, May1994), EngineTuning.Default));

            Assert.Equal(a, b);
        }

        /// <summary>
        /// Each bloc draws from its own sub-stream, so a new district must not perturb an existing
        /// one. A single generator walked in a loop would fail this.
        /// </summary>
        [Fact]
        public void Project_AddingADistrictDoesNotDisturbTheOthers()
        {
            var one = new List<Bloc>(District("alpha", 55.0, 0.30));
            var two = new List<Bloc>(one);
            two.AddRange(District("beta", 40.0, 0.60));

            TurnoutProjection before = TurnoutModel.Project(WithBlocs(one, SaveA, May1994), EngineTuning.Default);
            TurnoutProjection after = TurnoutModel.Project(WithBlocs(two, SaveA, May1994), EngineTuning.Default);

            Assert.Equal(SerializeDistrict(before.Districts[0]), SerializeDistrict(FindDistrict(after, "alpha")));
        }

        [Fact]
        public void Project_DoesNotMutateItsInputs()
        {
            List<Bloc> blocs = FullCity();
            Bloc probe = blocs[7];
            double happiness = probe.Happiness;
            double discontent = probe.Discontent;
            int eligible = probe.EligibleVoters;

            TurnoutModel.Project(WithBlocs(blocs, SaveA, May1994), EngineTuning.Default);

            Assert.Equal(happiness, probe.Happiness);
            Assert.Equal(discontent, probe.Discontent);
            Assert.Equal(eligible, probe.EligibleVoters);
        }

        // =====================================================================================
        // Golden values — the arithmetic itself
        // =====================================================================================

        /// <summary>
        /// A bloc sitting exactly on every reference point must land on <c>turnout.base</c>. Middle
        /// wealth is axis 0, Educated is index 0.5 = the reference, happiness 50 = the reference, and
        /// nothing else is engaged. Anything but 0.55 means a term is being added that should not be.
        /// </summary>
        [Fact]
        public void Project_AtEveryReferencePoint_ReturnsBase()
        {
            var bloc = MakeBloc("alpha", WealthTier.Middle, EducationTier.Educated, AgeBand.Adult,
                                happiness: 50.0, discontent: 0.0, eligible: 1000);

            BlocTurnout result = OnlyBloc(TurnoutModel.Project(WithBlocs(new List<Bloc> { bloc }, SaveA, May1994), Quiet()));

            Assert.Equal(0.55, result.Turnout, 12);
            Assert.Equal(550, result.ProjectedVotes);
        }

        /// <summary>The elderly multiplier is 1.1, so the same bloc one band older turns out 10% harder.</summary>
        [Fact]
        public void Project_AppliesTheElderlyMultiplier()
        {
            var elderly = MakeBloc("alpha", WealthTier.Middle, EducationTier.Educated, AgeBand.Elderly,
                                   happiness: 50.0, discontent: 0.0, eligible: 1000);

            BlocTurnout result = OnlyBloc(TurnoutModel.Project(WithBlocs(new List<Bloc> { elderly }, SaveA, May1994), Quiet()));

            Assert.Equal(0.605, result.Turnout, 12);
            Assert.Equal(605, result.ProjectedVotes);
        }

        // =====================================================================================
        // Disenfranchisement — the multiplier, not a missing bloc
        // =====================================================================================

        /// <summary>
        /// The sharpest test in the file. Minors are disenfranchised by an age multiplier of 0, and
        /// the turnout floor is 0.10. Apply the floor after the multiplier and every child in the city
        /// votes at 10%. The bloc here is deliberately given 1000 eligible voters so the assertion
        /// cannot pass by accident because the bloc packet zeroed eligibility.
        /// </summary>
        [Theory]
        [InlineData(AgeBand.Child)]
        [InlineData(AgeBand.Teen)]
        public void Project_MinorsCastNoVotes_EvenAtTheFloor(AgeBand band)
        {
            var minor = MakeBloc("alpha", WealthTier.Low, EducationTier.Uneducated, band,
                                 happiness: 0.0, discontent: 1.0, eligible: 1000);

            BlocTurnout result = OnlyBloc(TurnoutModel.Project(WithBlocs(new List<Bloc> { minor }, SaveA, May1994),
                                                               EngineTuning.Default));

            Assert.Equal(0.0, result.Turnout);
            Assert.Equal(0, result.ProjectedVotes);
            Assert.Equal(1000, result.EligibleVoters);
        }

        /// <summary>Minors stay in the projection — the dashboard shows the whole population (§4.3).</summary>
        [Fact]
        public void Project_KeepsMinorBlocsInTheOutput()
        {
            TurnoutProjection p = TurnoutModel.Project(WithBlocs(District("alpha", 50.0, 0.0), SaveA, May1994),
                                                       EngineTuning.Default);

            Assert.Equal(BlocAxes.BlocCount, p.Districts[0].Blocs.Count);
            Assert.True(p.Districts[0].TryGetBloc(
                new BlocKey(WealthTier.Low, EducationTier.Uneducated, AgeBand.Child), out BlocTurnout? child));
            Assert.NotNull(child);
            Assert.Equal(0.0, child!.Turnout);
        }

        // =====================================================================================
        // Caps — driven past the bound in both directions
        // =====================================================================================

        [Fact]
        public void Project_ClampsToTheCeiling_WhenEveryTermPushesUp()
        {
            var bloc = MakeBloc("alpha", WealthTier.High, EducationTier.HighlyEducated, AgeBand.Adult,
                                happiness: 100.0, discontent: 0.0, eligible: 1000);

            var inputs = WithBlocs(new List<Bloc> { bloc }, SaveA, May1994);
            inputs.CityStandings = Shares(0.5, 0.5);   // dead heat: full competitiveness bonus
            inputs.CampaignIntensity = 1.0;

            BlocTurnout result = OnlyBloc(TurnoutModel.Project(inputs, Quiet()));

            // Unclamped this sums to 0.975.
            Assert.Equal(0.95, result.Turnout, 12);
        }

        /// <summary>
        /// The ceiling has to survive the elderly multiplier too — 0.95 x 1.1 is 1.045, which is not a
        /// turnout rate. A cap that only holds before the multiplier is not a cap.
        /// </summary>
        [Fact]
        public void Project_ClampsToTheCeiling_AfterTheElderlyMultiplier()
        {
            var bloc = MakeBloc("alpha", WealthTier.High, EducationTier.HighlyEducated, AgeBand.Elderly,
                                happiness: 100.0, discontent: 0.0, eligible: 1000);

            var inputs = WithBlocs(new List<Bloc> { bloc }, SaveA, May1994);
            inputs.CityStandings = Shares(0.5, 0.5);
            inputs.CampaignIntensity = 1.0;

            BlocTurnout result = OnlyBloc(TurnoutModel.Project(inputs, Quiet()));

            Assert.Equal(0.95, result.Turnout, 12);
            Assert.Equal(950, result.ProjectedVotes);
        }

        [Fact]
        public void Project_ClampsToTheFloor_WhenEveryTermPushesDown()
        {
            var bloc = MakeBloc("alpha", WealthTier.Low, EducationTier.Uneducated, AgeBand.Adult,
                                happiness: 0.0, discontent: 1.0, eligible: 1000);

            var inputs = WithBlocs(new List<Bloc> { bloc }, SaveA, May1994);
            inputs.IsSnapElection = true;
            inputs.IncumbentConsecutiveTerms = 5;

            BlocTurnout result = OnlyBloc(TurnoutModel.Project(inputs, Quiet()));

            // Unclamped this sums to 0.015.
            Assert.Equal(0.10, result.Turnout, 12);
            Assert.Equal(100, result.ProjectedVotes);
        }

        /// <summary>Absurd inputs must still land inside the interval rather than producing 300% turnout.</summary>
        [Fact]
        public void Project_KeepsEveryRateInsideTheInterval_UnderExtremeInputs()
        {
            var blocs = new List<Bloc>();
            foreach (BlocKey key in BlocAxes.AllKeys)
            {
                blocs.Add(new Bloc
                {
                    DistrictId = "alpha",
                    Key = key,
                    Happiness = key.Ordinal % 2 == 0 ? 1000.0 : -1000.0,
                    Discontent = key.Ordinal % 3 == 0 ? 50.0 : -50.0,
                    EligibleVoters = 500
                });
            }

            var inputs = WithBlocs(blocs, SaveA, May1994);
            inputs.CampaignIntensity = 99.0;
            inputs.IncumbentConsecutiveTerms = 200;

            TurnoutProjection p = TurnoutModel.Project(inputs, EngineTuning.Default);

            foreach (BlocTurnout bt in p.Districts[0].Blocs)
            {
                Assert.InRange(bt.Turnout, 0.0, 0.95);
                Assert.InRange(bt.ProjectedVotes, 0, bt.EligibleVoters);
            }
        }

        [Fact]
        public void Project_SurvivesNaNMetrics()
        {
            var bloc = MakeBloc("alpha", WealthTier.Middle, EducationTier.Educated, AgeBand.Adult,
                                happiness: double.NaN, discontent: double.NaN, eligible: 1000);

            BlocTurnout result = OnlyBloc(TurnoutModel.Project(WithBlocs(new List<Bloc> { bloc }, SaveA, May1994), Quiet()));

            Assert.False(double.IsNaN(result.Turnout));
            Assert.Equal(0.55, result.Turnout, 12);
        }

        // =====================================================================================
        // Direction — one coefficient at a time
        // =====================================================================================

        /// <summary>
        /// Same district id and same bloc key on both runs, so the noise draw is identical and cancels.
        /// The difference measured is the happiness coefficient alone.
        /// </summary>
        [Fact]
        public void Project_TurnoutRisesWithHappiness()
        {
            double sad = SingleBlocRate(MakeBloc("alpha", WealthTier.Middle, EducationTier.Educated, AgeBand.Adult,
                                                 happiness: 20.0, discontent: 0.0, eligible: 1000), EngineTuning.Default);
            double happy = SingleBlocRate(MakeBloc("alpha", WealthTier.Middle, EducationTier.Educated, AgeBand.Adult,
                                                   happiness: 90.0, discontent: 0.0, eligible: 1000), EngineTuning.Default);

            Assert.True(happy > sad, $"happy={happy} should exceed sad={sad}");
        }

        [Fact]
        public void Project_TurnoutRisesWithEducation()
        {
            double low = SingleBlocRate(MakeBloc("alpha", WealthTier.Middle, EducationTier.Uneducated, AgeBand.Adult,
                                                 happiness: 50.0, discontent: 0.0, eligible: 1000), Quiet());
            double high = SingleBlocRate(MakeBloc("alpha", WealthTier.Middle, EducationTier.HighlyEducated, AgeBand.Adult,
                                                  happiness: 50.0, discontent: 0.0, eligible: 1000), Quiet());

            Assert.True(high > low, $"highly educated={high} should exceed uneducated={low}");
        }

        [Fact]
        public void Project_TurnoutRisesWithWealth()
        {
            double poor = SingleBlocRate(MakeBloc("alpha", WealthTier.Low, EducationTier.Educated, AgeBand.Adult,
                                                  happiness: 50.0, discontent: 0.0, eligible: 1000), Quiet());
            double rich = SingleBlocRate(MakeBloc("alpha", WealthTier.High, EducationTier.Educated, AgeBand.Adult,
                                                  happiness: 50.0, discontent: 0.0, eligible: 1000), Quiet());

            Assert.True(rich > poor, $"high wealth={rich} should exceed low wealth={poor}");
        }

        /// <summary>
        /// <c>turnout.discontentCoefficient</c> ships negative on purpose: discontent suppresses
        /// turnout more often than it mobilises it. If someone flips the sign in the tuning file this
        /// test is the one that should stop them.
        /// </summary>
        [Fact]
        public void Project_TurnoutFallsWithDiscontent()
        {
            double content = SingleBlocRate(MakeBloc("alpha", WealthTier.Middle, EducationTier.Educated, AgeBand.Adult,
                                                     happiness: 50.0, discontent: 0.0, eligible: 1000), EngineTuning.Default);
            double angry = SingleBlocRate(MakeBloc("alpha", WealthTier.Middle, EducationTier.Educated, AgeBand.Adult,
                                                   happiness: 50.0, discontent: 1.0, eligible: 1000), EngineTuning.Default);

            Assert.True(angry < content, $"discontented={angry} should fall below content={content}");
        }

        /// <summary>"Turnout ... can flip close races" (§3 Campaigns) — a close race must pull voters out.</summary>
        [Fact]
        public void Project_TurnoutRisesWhenTheRaceIsClose()
        {
            var bloc = MakeBloc("alpha", WealthTier.Middle, EducationTier.Educated, AgeBand.Adult,
                                happiness: 50.0, discontent: 0.0, eligible: 1000);

            var blowout = WithBlocs(new List<Bloc> { CopyOf(bloc) }, SaveA, May1994);
            blowout.DistrictStandings = DistrictShares("alpha", 0.80, 0.20);

            var knifeEdge = WithBlocs(new List<Bloc> { CopyOf(bloc) }, SaveA, May1994);
            knifeEdge.DistrictStandings = DistrictShares("alpha", 0.50, 0.50);

            double low = OnlyBloc(TurnoutModel.Project(blowout, EngineTuning.Default)).Turnout;
            double high = OnlyBloc(TurnoutModel.Project(knifeEdge, EngineTuning.Default)).Turnout;

            Assert.True(high > low, $"dead heat={high} should exceed blowout={low}");
        }

        [Fact]
        public void Project_SnapElectionDepressesTurnout()
        {
            var scheduled = WithBlocs(new List<Bloc> { StandardBloc() }, SaveA, May1994);
            var snap = WithBlocs(new List<Bloc> { StandardBloc() }, SaveA, May1994);
            snap.IsSnapElection = true;

            double a = OnlyBloc(TurnoutModel.Project(scheduled, EngineTuning.Default)).Turnout;
            double b = OnlyBloc(TurnoutModel.Project(snap, EngineTuning.Default)).Turnout;

            Assert.True(b < a, $"snap={b} should fall below scheduled={a}");
        }

        [Fact]
        public void Project_IncumbentFatigueDepressesTurnout()
        {
            var fresh = WithBlocs(new List<Bloc> { StandardBloc() }, SaveA, May1994);
            var tired = WithBlocs(new List<Bloc> { StandardBloc() }, SaveA, May1994);
            tired.IncumbentConsecutiveTerms = 4;

            double a = OnlyBloc(TurnoutModel.Project(fresh, EngineTuning.Default)).Turnout;
            double b = OnlyBloc(TurnoutModel.Project(tired, EngineTuning.Default)).Turnout;

            Assert.True(b < a, $"fourth-term={b} should fall below first-term={a}");
        }

        [Fact]
        public void Project_CampaignIntensityRaisesTurnout()
        {
            var quietWeek = WithBlocs(new List<Bloc> { StandardBloc() }, SaveA, May1994);
            var finalWeek = WithBlocs(new List<Bloc> { StandardBloc() }, SaveA, May1994);
            finalWeek.CampaignIntensity = 1.0;

            double a = OnlyBloc(TurnoutModel.Project(quietWeek, EngineTuning.Default)).Turnout;
            double b = OnlyBloc(TurnoutModel.Project(finalWeek, EngineTuning.Default)).Turnout;

            Assert.True(b > a, $"full campaign={b} should exceed no campaign={a}");
        }

        [Fact]
        public void Project_IgnoresCampaignIntensityAboveOne()
        {
            var one = WithBlocs(new List<Bloc> { StandardBloc() }, SaveA, May1994);
            one.CampaignIntensity = 1.0;
            var absurd = WithBlocs(new List<Bloc> { StandardBloc() }, SaveA, May1994);
            absurd.CampaignIntensity = 50.0;

            Assert.Equal(OnlyBloc(TurnoutModel.Project(one, EngineTuning.Default)).Turnout,
                         OnlyBloc(TurnoutModel.Project(absurd, EngineTuning.Default)).Turnout, 12);
        }

        // =====================================================================================
        // Competitiveness
        // =====================================================================================

        [Fact]
        public void Competitiveness_IsOneForADeadHeat() =>
            Assert.Equal(1.0, TurnoutModel.Competitiveness(Shares(0.4, 0.4)), 12);

        [Fact]
        public void Competitiveness_IsTheRunnerUpOverTheLeader() =>
            Assert.Equal(1.0 / 3.0, TurnoutModel.Competitiveness(Shares(0.6, 0.2)), 12);

        [Fact]
        public void Competitiveness_IsZeroForAnUncontestedField()
        {
            Assert.Equal(0.0, TurnoutModel.Competitiveness(Shares(1.0)));
            Assert.Equal(0.0, TurnoutModel.Competitiveness(new List<PartyVoteShare>()));
            Assert.Equal(0.0, TurnoutModel.Competitiveness(null));
        }

        /// <summary>Order-independent: the leader and runner-up are the same whichever way the list is walked.</summary>
        [Fact]
        public void Competitiveness_IgnoresListOrder()
        {
            Assert.Equal(TurnoutModel.Competitiveness(Shares(0.1, 0.5, 0.4)),
                         TurnoutModel.Competitiveness(Shares(0.5, 0.4, 0.1)), 12);
        }

        /// <summary>A district with its own standing must use it, not the city fallback.</summary>
        [Fact]
        public void Project_PrefersDistrictStandingsOverCityStandings()
        {
            var inputs = WithBlocs(District("alpha", 50.0, 0.0), SaveA, May1994);
            inputs.CityStandings = Shares(0.9, 0.1);              // competitiveness 1/9
            inputs.DistrictStandings = DistrictShares("alpha", 0.5, 0.5);  // competitiveness 1

            TurnoutProjection p = TurnoutModel.Project(inputs, EngineTuning.Default);

            Assert.Equal(1.0, p.Districts[0].Competitiveness, 12);
        }

        [Fact]
        public void Project_FallsBackToCityStandings_ForAnUnlistedDistrict()
        {
            var inputs = WithBlocs(District("beta", 50.0, 0.0), SaveA, May1994);
            inputs.CityStandings = Shares(0.6, 0.2);
            inputs.DistrictStandings = DistrictShares("alpha", 0.5, 0.5);

            TurnoutProjection p = TurnoutModel.Project(inputs, EngineTuning.Default);

            Assert.Equal(1.0 / 3.0, p.Districts[0].Competitiveness, 12);
        }

        // =====================================================================================
        // Aggregation
        // =====================================================================================

        /// <summary>
        /// The reported rate must be the integer counts divided, not a mean of the bloc rates. The
        /// election packet counts the integers, and in a close race a rounding disagreement between
        /// the two is a wrong winner.
        /// </summary>
        [Fact]
        public void Project_DistrictRateMatchesItsOwnVoteCounts()
        {
            TurnoutProjection p = TurnoutModel.Project(FullCityInputs(SaveA, May1994), EngineTuning.Default);

            foreach (DistrictTurnout d in p.Districts)
            {
                int eligible = 0;
                int votes = 0;
                foreach (BlocTurnout bt in d.Blocs)
                {
                    eligible += bt.EligibleVoters;
                    votes += bt.ProjectedVotes;
                }

                Assert.Equal(eligible, d.EligibleVoters);
                Assert.Equal(votes, d.ProjectedVotes);
                Assert.Equal(votes / (double)eligible, d.Turnout, 12);
            }
        }

        [Fact]
        public void Project_CityTotalsAreTheSumOfTheDistricts()
        {
            TurnoutProjection p = TurnoutModel.Project(FullCityInputs(SaveA, May1994), EngineTuning.Default);

            int eligible = 0;
            int votes = 0;
            foreach (DistrictTurnout d in p.Districts)
            {
                eligible += d.EligibleVoters;
                votes += d.ProjectedVotes;
            }

            Assert.Equal(eligible, p.TotalEligibleVoters);
            Assert.Equal(votes, p.TotalProjectedVotes);
            Assert.Equal(votes / (double)eligible, p.CityTurnout, 12);
        }

        [Fact]
        public void Project_SortsDistrictsByIdOrdinal()
        {
            var blocs = new List<Bloc>();
            blocs.AddRange(District("zulu", 50.0, 0.0));
            blocs.AddRange(District("alpha", 50.0, 0.0));
            blocs.AddRange(District("Mike", 50.0, 0.0));

            TurnoutProjection p = TurnoutModel.Project(WithBlocs(blocs, SaveA, May1994), EngineTuning.Default);

            Assert.Equal(new[] { "Mike", "alpha", "zulu" },
                         new[] { p.Districts[0].DistrictId, p.Districts[1].DistrictId, p.Districts[2].DistrictId });
        }

        [Fact]
        public void Project_SortsBlocsByOrdinal()
        {
            var blocs = new List<Bloc>(District("alpha", 50.0, 0.0));
            blocs.Reverse();

            TurnoutProjection p = TurnoutModel.Project(WithBlocs(blocs, SaveA, May1994), EngineTuning.Default);

            IReadOnlyList<BlocTurnout> ordered = p.Districts[0].Blocs;
            for (int i = 1; i < ordered.Count; i++)
                Assert.True(ordered[i - 1].Bloc.Ordinal < ordered[i].Bloc.Ordinal);
        }

        [Fact]
        public void Project_ReportsZeroForADistrictWithNoEligibleVoters()
        {
            var bloc = MakeBloc("alpha", WealthTier.Middle, EducationTier.Educated, AgeBand.Adult,
                                happiness: 50.0, discontent: 0.0, eligible: 0);

            TurnoutProjection p = TurnoutModel.Project(WithBlocs(new List<Bloc> { bloc }, SaveA, May1994), Quiet());

            Assert.Equal(0, p.Districts[0].ProjectedVotes);
            Assert.Equal(0.0, p.Districts[0].Turnout);
            Assert.Equal(0.0, p.CityTurnout);
            Assert.Equal(0.55, p.Districts[0].Blocs[0].Turnout, 12); // the rate still exists, nobody casts it
        }

        [Fact]
        public void Project_OnAnEmptyCity_ReturnsAnEmptyProjection()
        {
            TurnoutProjection p = TurnoutModel.Project(WithBlocs(new List<Bloc>(), SaveA, May1994), EngineTuning.Default);

            Assert.Empty(p.Districts);
            Assert.Equal(0, p.TotalEligibleVoters);
            Assert.Equal(0.0, p.CityTurnout);
            Assert.Equal(May1994, p.Date);
        }

        // =====================================================================================
        // Noise wiring
        // =====================================================================================

        /// <summary>
        /// Proves the noise is drawn per bloc rather than once per city — otherwise every bloc would
        /// share one offset and the "seeded stream" would be decorative.
        /// </summary>
        [Fact]
        public void Project_DrawsDistinctNoisePerBloc()
        {
            TurnoutProjection p = TurnoutModel.Project(WithBlocs(District("alpha", 50.0, 0.0), SaveA, May1994),
                                                       EngineTuning.Default);

            IReadOnlyList<BlocTurnout> blocs = p.Districts[0].Blocs;
            Assert.NotEqual(blocs[0].NoiseComponent, blocs[1].NoiseComponent);
            Assert.NotEqual(0.0, blocs[0].NoiseComponent);
        }

        /// <summary>The same bloc key in two districts is two different voters, so two different draws.</summary>
        [Fact]
        public void Project_DrawsDistinctNoisePerDistrict()
        {
            var blocs = new List<Bloc>();
            blocs.AddRange(District("alpha", 50.0, 0.0));
            blocs.AddRange(District("beta", 50.0, 0.0));

            TurnoutProjection p = TurnoutModel.Project(WithBlocs(blocs, SaveA, May1994), EngineTuning.Default);

            Assert.NotEqual(p.Districts[0].Blocs[12].NoiseComponent, p.Districts[1].Blocs[12].NoiseComponent);
        }

        [Fact]
        public void Project_WithZeroSigma_DrawsNoNoise()
        {
            TurnoutProjection p = TurnoutModel.Project(WithBlocs(District("alpha", 50.0, 0.0), SaveA, May1994), Quiet());

            foreach (BlocTurnout bt in p.Districts[0].Blocs)
                Assert.Equal(0.0, bt.NoiseComponent);
        }

        // =====================================================================================
        // Lookups
        // =====================================================================================

        [Fact]
        public void Projection_LookupsResolveAndFailCleanly()
        {
            TurnoutProjection p = TurnoutModel.Project(WithBlocs(District("alpha", 50.0, 0.0), SaveA, May1994), Quiet());

            Assert.True(p.TryGetDistrict("alpha", out DistrictTurnout? found));
            Assert.NotNull(found);
            Assert.False(p.TryGetDistrict("nowhere", out DistrictTurnout? missing));
            Assert.Null(missing);
            Assert.Equal(0.0, p.TurnoutFor("nowhere"));
            Assert.Equal(found!.Turnout, p.TurnoutFor("alpha"), 12);

            var key = new BlocKey(WealthTier.High, EducationTier.WellEducated, AgeBand.Adult);
            Assert.Equal(found.RateFor(key), found.Blocs[key.Ordinal].Turnout, 12);
        }

        [Fact]
        public void Project_RejectsNullArguments()
        {
            Assert.Throws<ArgumentNullException>(() => TurnoutModel.Project(null!, EngineTuning.Default));
            Assert.Throws<ArgumentNullException>(() =>
                TurnoutModel.Project(WithBlocs(new List<Bloc>(), SaveA, May1994), null!));
        }

        // =====================================================================================
        // Fixtures
        // =====================================================================================

        private static Bloc MakeBloc(string districtId, WealthTier wealth, EducationTier education, AgeBand age,
                                     double happiness, double discontent, int eligible) =>
            new Bloc
            {
                DistrictId = districtId,
                Key = new BlocKey(wealth, education, age),
                Population = eligible,
                PopulationShare = 1.0,
                EligibleVoters = eligible,
                Happiness = happiness,
                Discontent = discontent
            };

        private static Bloc StandardBloc() =>
            MakeBloc("alpha", WealthTier.Middle, EducationTier.Educated, AgeBand.Adult, 50.0, 0.0, 1000);

        private static Bloc CopyOf(Bloc b) =>
            MakeBloc(b.DistrictId, b.Key.Wealth, b.Key.Education, b.Key.Age, b.Happiness, b.Discontent, b.EligibleVoters);

        /// <summary>All 60 blocs of one district. Minors keep a non-zero head count on purpose.</summary>
        private static List<Bloc> District(string districtId, double happiness, double discontent)
        {
            var list = new List<Bloc>(BlocAxes.BlocCount);
            foreach (BlocKey key in BlocAxes.AllKeys)
            {
                list.Add(new Bloc
                {
                    DistrictId = districtId,
                    Key = key,
                    Population = 200 + key.Ordinal,
                    PopulationShare = 1.0 / BlocAxes.BlocCount,
                    EligibleVoters = 100 + key.Ordinal,
                    Happiness = happiness + (key.Ordinal % 7),
                    Discontent = discontent
                });
            }

            return list;
        }

        private static List<Bloc> FullCity()
        {
            var blocs = new List<Bloc>();
            blocs.AddRange(District("alpha", 62.0, 0.20));
            blocs.AddRange(District("beta", 41.0, 0.55));
            blocs.AddRange(District("gamma", 78.0, 0.05));
            return blocs;
        }

        private static TurnoutInputs FullCityInputs(Guid save, SimDate date)
        {
            TurnoutInputs inputs = WithBlocs(FullCity(), save, date);
            inputs.CityStandings = Shares(0.38, 0.31, 0.19, 0.12);
            inputs.DistrictStandings = new List<DistrictResult>
            {
                new DistrictResult { DistrictId = "alpha", Shares = Shares(0.45, 0.40, 0.10, 0.05) },
                new DistrictResult { DistrictId = "beta", Shares = Shares(0.62, 0.20, 0.12, 0.06) }
            };
            inputs.CampaignIntensity = 0.7;
            inputs.IncumbentConsecutiveTerms = 2;
            return inputs;
        }

        private static TurnoutInputs WithBlocs(IReadOnlyList<Bloc> blocs, Guid save, SimDate date) =>
            new TurnoutInputs { SaveGuid = save, Date = date, Blocs = blocs };

        private static List<PartyVoteShare> Shares(params double[] values)
        {
            var list = new List<PartyVoteShare>(values.Length);
            for (int i = 0; i < values.Length; i++)
                list.Add(new PartyVoteShare("p" + i.ToString(CultureInfo.InvariantCulture), values[i]));
            return list;
        }

        private static List<DistrictResult> DistrictShares(string districtId, params double[] values) =>
            new List<DistrictResult> { new DistrictResult { DistrictId = districtId, Shares = Shares(values) } };

        private static BlocTurnout OnlyBloc(TurnoutProjection p) => p.Districts[0].Blocs[0];

        private static double SingleBlocRate(Bloc bloc, EngineTuning tuning) =>
            OnlyBloc(TurnoutModel.Project(WithBlocs(new List<Bloc> { bloc }, SaveA, May1994), tuning)).Turnout;

        private static DistrictTurnout FindDistrict(TurnoutProjection p, string id)
        {
            Assert.True(p.TryGetDistrict(id, out DistrictTurnout? d));
            return d!;
        }

        // =====================================================================================
        // Hashing — compares the whole result, not the fields someone remembered to assert
        // =====================================================================================

        private static string Hash(TurnoutProjection p)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Serialize(p))));
            }
        }

        private static string Serialize(TurnoutProjection p)
        {
            var sb = new StringBuilder();
            sb.Append(p.Date.ToString()).Append('|')
              .Append(F(p.CityTurnout)).Append('|')
              .Append(p.TotalEligibleVoters).Append('|')
              .Append(p.TotalProjectedVotes).Append('\n');

            foreach (DistrictTurnout d in p.Districts)
                sb.Append(SerializeDistrict(d));

            return sb.ToString();
        }

        private static string SerializeDistrict(DistrictTurnout d)
        {
            var sb = new StringBuilder();
            sb.Append(d.DistrictId).Append('|')
              .Append(F(d.Turnout)).Append('|')
              .Append(d.EligibleVoters).Append('|')
              .Append(d.ProjectedVotes).Append('|')
              .Append(F(d.Competitiveness)).Append('\n');

            foreach (BlocTurnout bt in d.Blocs)
                sb.Append("  ").Append(bt.DistrictId).Append('|')
                  .Append(bt.Bloc.Id).Append('|')
                  .Append(F(bt.Turnout)).Append('|')
                  .Append(bt.EligibleVoters).Append('|')
                  .Append(bt.ProjectedVotes).Append('|')
                  .Append(F(bt.NoiseComponent)).Append('\n');

            return sb.ToString();
        }

        private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    }
}
