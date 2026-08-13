using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Engine.Parties;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Packet 15 — the failure score, the streak, and the per-party ceiling those two produce.
    ///
    /// <para>
    /// The behavioural rule these are all testing is the ratified one: a minor party sits at 3% and
    /// only rises if the major parties fail to deliver, repeatedly. So the tests that matter most are
    /// the negative ones — the ceiling staying shut through two bad terms, staying shut for the party
    /// whose issue nobody is angry about, and snapping shut again after one good term.
    /// </para>
    /// </summary>
    public class FringeFailureTests
    {
        private static FringeTuning Shipped => EngineTuning.Default.Fringe;

        private static FringeTuning TuningWith(string json) =>
            EngineTuning.FromJson("{\"fringe\":" + json + "}").Fringe;

        // ------------------------------------------------------------------------------------------
        // Fixtures
        // ------------------------------------------------------------------------------------------

        private static Party Major(string id) =>
            new Party { Id = id, Status = PartyStatus.Active, IsMajor = true, CoreGrievance = Issue.Services };

        private static Party Fringe(string id, Issue grievance) =>
            new Party { Id = id, Status = PartyStatus.Active, IsMajor = false, CoreGrievance = grievance };

        /// <summary>Grievance vector that is angry about exactly one issue.</summary>
        private static IssueWeights AngryAbout(Issue issue, double level = 1.0)
        {
            return new IssueWeights(
                issue == Issue.Services ? level : 0.0,
                issue == Issue.CostOfLiving ? level : 0.0,
                issue == Issue.Environment ? level : 0.0,
                issue == Issue.Transit ? level : 0.0,
                issue == Issue.Growth ? level : 0.0,
                issue == Issue.HeritageOrder ? level : 0.0);
        }

        private static IssueWeights NoGrievance() => new IssueWeights(0, 0, 0, 0, 0, 0);

        /// <summary>Runs a term of <paramref name="months"/> ticks and closes it.</summary>
        private static void RunTerm(FringeWatch watch, int termNumber, FringeMonth month,
                                    FringeTuning tuning, int months = 12)
        {
            for (int i = 0; i < months; i++) FringeFailureModel.Observe(watch, month, tuning);
            FringeFailureModel.CloseTerm(watch, termNumber, tuning);
        }

        /// <summary>A term bad enough on every signal to clear the threshold comfortably.</summary>
        private static FringeMonth CollapsingMonth() =>
            new FringeMonth { CityDiscontent = 1.0, MajorDefianceSurge = 0.02, MayorChanges = 0 };

        /// <summary>A term with nothing wrong at all.</summary>
        private static FringeMonth CalmMonth() =>
            new FringeMonth { CityDiscontent = 0.20 };

        // ------------------------------------------------------------------------------------------
        // The score
        // ------------------------------------------------------------------------------------------

        [Fact]
        public void Score_IsZeroForAFlawlessTerm()
        {
            var watch = new FringeWatch();
            for (int i = 0; i < 12; i++) FringeFailureModel.Observe(watch, CalmMonth(), Shipped);

            Assert.Equal(0.0, FringeFailureModel.Score(watch, Shipped), 12);
        }

        [Fact]
        public void Score_IsOneForATotalCollapse()
        {
            var watch = new FringeWatch();
            for (int i = 0; i < 12; i++)
            {
                FringeFailureModel.Observe(watch, new FringeMonth
                {
                    CityDiscontent = 1.0,
                    MajorDefianceSurge = 0.05,
                    GovernmentChanges = 1,
                    MayorChanges = 1
                }, Shipped);
            }

            Assert.Equal(1.0, FringeFailureModel.Score(watch, Shipped), 12);
        }

        /// <summary>
        /// An ordinarily grumpy city is not a failing one. Discontent is measured against a floor, so
        /// only the part above <c>discontentFloor</c> counts toward the score.
        /// </summary>
        [Fact]
        public void Score_IgnoresDiscontentBelowTheFloor()
        {
            var atFloor = new FringeWatch();
            for (int i = 0; i < 12; i++)
                FringeFailureModel.Observe(atFloor, new FringeMonth { CityDiscontent = Shipped.DiscontentFloor }, Shipped);

            Assert.Equal(0.0, FringeFailureModel.Score(atFloor, Shipped), 12);

            var above = new FringeWatch();
            for (int i = 0; i < 12; i++)
                FringeFailureModel.Observe(above, new FringeMonth { CityDiscontent = 1.0 }, Shipped);

            Assert.Equal(Shipped.DiscontentWeight, FringeFailureModel.Score(above, Shipped), 12);
        }

        /// <summary>
        /// Discontent is a mean, not a sum: a long term must not read as worse governance than a short
        /// one at the same level of unhappiness.
        /// </summary>
        [Fact]
        public void Score_UsesMeanDiscontentSoTermLengthDoesNotInflateIt()
        {
            var shortTerm = new FringeWatch();
            for (int i = 0; i < 6; i++)
                FringeFailureModel.Observe(shortTerm, new FringeMonth { CityDiscontent = 0.9 }, Shipped);

            var longTerm = new FringeWatch();
            for (int i = 0; i < 48; i++)
                FringeFailureModel.Observe(longTerm, new FringeMonth { CityDiscontent = 0.9 }, Shipped);

            Assert.Equal(FringeFailureModel.Score(shortTerm, Shipped),
                         FringeFailureModel.Score(longTerm, Shipped), 12);
        }

        /// <summary>Each signal contributes exactly its own weight when saturated, and nothing more.</summary>
        [Fact]
        public void Score_WeightsTheThreeSignalsAsTuned()
        {
            var defianceOnly = new FringeWatch();
            FringeFailureModel.Observe(defianceOnly, new FringeMonth
            {
                MajorDefianceSurge = Shipped.DefianceSurgeForFullSignal,
                CityDiscontent = 0.0
            }, Shipped);
            Assert.Equal(Shipped.DefianceWeight, FringeFailureModel.Score(defianceOnly, Shipped), 12);

            var churnOnly = new FringeWatch();
            FringeFailureModel.Observe(churnOnly, new FringeMonth
            {
                GovernmentChanges = Shipped.ChurnEventsForFullSignal,
                CityDiscontent = 0.0
            }, Shipped);
            Assert.Equal(Shipped.ChurnWeight, FringeFailureModel.Score(churnOnly, Shipped), 12);
        }

        [Fact]
        public void Score_NeverExceedsOneHoweverBadThingsGet()
        {
            var watch = new FringeWatch();
            for (int i = 0; i < 60; i++)
            {
                FringeFailureModel.Observe(watch, new FringeMonth
                {
                    CityDiscontent = 1.0,
                    MajorDefianceSurge = 10.0,
                    GovernmentChanges = 20,
                    MayorChanges = 20
                }, Shipped);
            }

            Assert.Equal(1.0, FringeFailureModel.Score(watch, Shipped), 12);
        }

        // ------------------------------------------------------------------------------------------
        // The streak
        // ------------------------------------------------------------------------------------------

        [Fact]
        public void CloseTerm_ExtendsTheStreakOnAFailureAndZeroesTheAccumulator()
        {
            var watch = new FringeWatch();
            RunTerm(watch, 1, CollapsingMonth(), Shipped);

            Assert.Equal(1, watch.ConsecutiveFailureTerms);
            Assert.Equal(1, watch.LastClosedTermNumber);
            Assert.True(watch.LastTermFailureScore >= Shipped.FailureTermScoreThreshold);

            Assert.Equal(0, watch.MonthsObserved);
            Assert.Equal(0.0, watch.DiscontentSum);
            Assert.Equal(0.0, watch.DefianceSurgeSum);
            Assert.Equal(0, watch.GovernmentChanges);
            Assert.Equal(0, watch.MayorChanges);
        }

        /// <summary>
        /// One good term wipes the streak outright rather than decaying it. A decay would let a fringe
        /// party keep most of an unlock it had stopped earning.
        /// </summary>
        [Fact]
        public void CloseTerm_ResetsTheStreakAfterASingleGoodTerm()
        {
            var watch = new FringeWatch();
            RunTerm(watch, 1, CollapsingMonth(), Shipped);
            RunTerm(watch, 2, CollapsingMonth(), Shipped);
            Assert.Equal(2, watch.ConsecutiveFailureTerms);

            RunTerm(watch, 3, CalmMonth(), Shipped);
            Assert.Equal(0, watch.ConsecutiveFailureTerms);
        }

        /// <summary>
        /// A reload replaying the election month must not score the same term twice, which would hand
        /// the city a streak it never lived through.
        /// </summary>
        [Fact]
        public void CloseTerm_IsIdempotentForTheSameTermNumber()
        {
            var watch = new FringeWatch();
            RunTerm(watch, 1, CollapsingMonth(), Shipped);

            FringeFailureModel.CloseTerm(watch, 1, Shipped);
            Assert.Equal(1, watch.ConsecutiveFailureTerms);

            FringeFailureModel.CloseTerm(watch, 0, Shipped);
            Assert.Equal(1, watch.ConsecutiveFailureTerms);
        }

        // ------------------------------------------------------------------------------------------
        // The ceiling: the ratified rule
        // ------------------------------------------------------------------------------------------

        private static double CeilingAfter(int failureTerms, Issue partyIssue, Issue cityIssue)
        {
            var watch = new FringeWatch();
            for (int term = 1; term <= failureTerms; term++) RunTerm(watch, term, CollapsingMonth(), Shipped);

            return FringeFailureModel.CeilingFor(Fringe("party-03", partyIssue), watch,
                                                 AngryAbout(cityIssue), Shipped);
        }

        /// <summary>
        /// The headline rule. Two bad terms are not "repeatedly" — the ceiling does not move at all
        /// until the third.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void Ceiling_StaysAtThreePercentBelowTheUnlock(int failureTerms)
        {
            Assert.Equal(Shipped.BaseCeiling,
                         CeilingAfter(failureTerms, Issue.Environment, Issue.Environment), 12);
        }

        [Fact]
        public void Ceiling_OpensOnTheThirdConsecutiveFailureTerm()
        {
            Assert.True(CeilingAfter(3, Issue.Environment, Issue.Environment) > Shipped.BaseCeiling);
        }

        [Fact]
        public void Ceiling_RisesWithTheLengthOfTheStreak()
        {
            double third = CeilingAfter(3, Issue.Environment, Issue.Environment);
            double fourth = CeilingAfter(4, Issue.Environment, Issue.Environment);
            double sixth = CeilingAfter(6, Issue.Environment, Issue.Environment);

            Assert.True(fourth > third);
            Assert.True(sixth > fourth);
        }

        /// <summary>
        /// maxCeiling is reached only when every factor is saturated at once: a long streak, total
        /// grievance on this party's issue, AND a last term that scored a full 1.0. A term that was
        /// merely bad does not get there — CollapsingMonth has no churn, so it scores 0.75 and lands
        /// well short. That is the intended shape: 40% is what a complete collapse buys, not what
        /// persistent mediocrity buys.
        /// </summary>
        [Fact]
        public void Ceiling_ReachesMaxOnlyOnATotalCollapse()
        {
            Assert.True(CeilingAfter(30, Issue.Environment, Issue.Environment) < Shipped.MaxCeiling);

            var watch = new FringeWatch();
            for (int term = 1; term <= 30; term++)
            {
                RunTerm(watch, term, new FringeMonth
                {
                    CityDiscontent = 1.0,
                    MajorDefianceSurge = 0.05,
                    GovernmentChanges = 1,
                    MayorChanges = 1
                }, Shipped);
            }

            Assert.Equal(1.0, watch.LastTermFailureScore, 12);
            Assert.Equal(Shipped.MaxCeiling,
                         FringeFailureModel.CeilingFor(Fringe("party-03", Issue.Environment), watch,
                                                       AngryAbout(Issue.Environment), Shipped), 12);
        }

        /// <summary>No combination of inputs may push a ceiling past its tuned maximum.</summary>
        [Fact]
        public void Ceiling_NeverExceedsMaxCeiling()
        {
            var watch = new FringeWatch();
            for (int term = 1; term <= 50; term++)
            {
                RunTerm(watch, term, new FringeMonth
                {
                    CityDiscontent = 5.0,          // out of range on purpose
                    MajorDefianceSurge = 99.0,
                    GovernmentChanges = 50,
                    MayorChanges = 50
                }, Shipped);
            }

            double ceiling = FringeFailureModel.CeilingFor(Fringe("party-03", Issue.Environment), watch,
                                                          AngryAbout(Issue.Environment, 5.0), Shipped);

            Assert.True(ceiling <= Shipped.MaxCeiling + 1e-12);
            Assert.Equal(Shipped.MaxCeiling, ceiling, 12);
        }

        /// <summary>
        /// The per-party signal, and the reason a bad government is not a windfall for every minor
        /// party at once: the environmentalists rise when the environment is being neglected, not
        /// merely when the mayor is unpopular.
        /// </summary>
        [Fact]
        public void Ceiling_OnlyLiftsTheFringePartyWhoseOwnGrievanceIsUnmet()
        {
            var watch = new FringeWatch();
            for (int term = 1; term <= 6; term++) RunTerm(watch, term, CollapsingMonth(), Shipped);

            IssueWeights grievance = AngryAbout(Issue.Environment);

            double greens = FringeFailureModel.CeilingFor(Fringe("party-03", Issue.Environment), watch, grievance, Shipped);
            double populists = FringeFailureModel.CeilingFor(Fringe("party-04", Issue.HeritageOrder), watch, grievance, Shipped);

            Assert.True(greens > Shipped.BaseCeiling);
            Assert.Equal(Shipped.BaseCeiling, populists, 12);
        }

        /// <summary>Even total establishment collapse leaves the ceiling shut if nobody is aggrieved.</summary>
        [Fact]
        public void Ceiling_StaysShutWhenTheCityIsNotAggrievedAtAll()
        {
            var watch = new FringeWatch();
            for (int term = 1; term <= 6; term++) RunTerm(watch, term, CollapsingMonth(), Shipped);

            Assert.Equal(Shipped.BaseCeiling,
                         FringeFailureModel.CeilingFor(Fringe("party-03", Issue.Environment), watch,
                                                       NoGrievance(), Shipped), 12);
        }

        [Fact]
        public void Ceiling_StaysShutForGrievanceBelowTheFloor()
        {
            var watch = new FringeWatch();
            for (int term = 1; term <= 6; term++) RunTerm(watch, term, CollapsingMonth(), Shipped);

            IssueWeights justUnder = AngryAbout(Issue.Environment, Shipped.GrievanceFloor);

            Assert.Equal(Shipped.BaseCeiling,
                         FringeFailureModel.CeilingFor(Fringe("party-03", Issue.Environment), watch,
                                                       justUnder, Shipped), 12);
        }

        /// <summary>Recovery. After one good term the streak breaks and the ceiling snaps back to 3%.</summary>
        [Fact]
        public void Ceiling_SnapsShutAgainAfterOneGoodTerm()
        {
            var watch = new FringeWatch();
            for (int term = 1; term <= 6; term++) RunTerm(watch, term, CollapsingMonth(), Shipped);

            IssueWeights grievance = AngryAbout(Issue.Environment);
            Party greens = Fringe("party-03", Issue.Environment);
            Assert.True(FringeFailureModel.CeilingFor(greens, watch, grievance, Shipped) > Shipped.BaseCeiling);

            RunTerm(watch, 7, CalmMonth(), Shipped);

            Assert.Equal(Shipped.BaseCeiling,
                         FringeFailureModel.CeilingFor(greens, watch, grievance, Shipped), 12);
        }

        // ------------------------------------------------------------------------------------------
        // Who gets a ceiling at all
        // ------------------------------------------------------------------------------------------

        [Fact]
        public void Ceilings_CoverTheMinorPartiesAndNeverTheMajors()
        {
            var parties = new List<Party>
            {
                Major("party-01"), Major("party-02"),
                Fringe("party-03", Issue.Environment), Fringe("party-04", Issue.HeritageOrder)
            };

            FringeCeilings c = FringeFailureModel.Ceilings(parties, new FringeWatch(), NoGrievance(),
                                                           ElectoralSystem.FirstPastThePost, Shipped);

            double v;
            Assert.False(c.TryGet("party-01", out v));
            Assert.False(c.TryGet("party-02", out v));
            Assert.True(c.TryGet("party-03", out v));
            Assert.Equal(Shipped.BaseCeiling, v, 12);
            Assert.True(c.TryGet("party-04", out v));
        }

        /// <summary>
        /// EU inertness. Multiparty PR is supposed to have viable small parties, and it has its own 5%
        /// electoral threshold; this packet must not touch it.
        /// </summary>
        [Fact]
        public void Ceilings_AreEmptyUnderProportional()
        {
            var parties = new List<Party>
            {
                Major("party-01"), Fringe("party-02", Issue.Environment)
            };

            Assert.True(FringeFailureModel.Ceilings(parties, new FringeWatch(), AngryAbout(Issue.Environment),
                                                    ElectoralSystem.Proportional, Shipped).IsEmpty);
        }

        [Fact]
        public void Ceilings_AreEmptyWhenThePacketIsDisabled()
        {
            var parties = new List<Party> { Major("party-01"), Fringe("party-02", Issue.Environment) };

            Assert.True(FringeFailureModel.Ceilings(parties, new FringeWatch(), NoGrievance(),
                                                    ElectoralSystem.FirstPastThePost,
                                                    TuningWith("{\"enabled\":false}")).IsEmpty);
        }

        /// <summary>A brand that is off the ballot draws no affinity, so capping it means nothing.</summary>
        [Fact]
        public void Ceilings_SkipDissolvedAndMergedBrands()
        {
            var parties = new List<Party>
            {
                Major("party-01"),
                new Party { Id = "party-02", Status = PartyStatus.Dissolved, CoreGrievance = Issue.Environment },
                new Party { Id = "party-03", Status = PartyStatus.Merged, CoreGrievance = Issue.Environment },
                new Party { Id = "party-04", Status = PartyStatus.Endangered, CoreGrievance = Issue.Environment },
                new Party { Id = "party-05", Status = PartyStatus.Revived, CoreGrievance = Issue.Environment }
            };

            FringeCeilings c = FringeFailureModel.Ceilings(parties, new FringeWatch(), NoGrievance(),
                                                           ElectoralSystem.FirstPastThePost, Shipped);

            double v;
            Assert.False(c.TryGet("party-02", out v));
            Assert.False(c.TryGet("party-03", out v));
            Assert.True(c.TryGet("party-04", out v));   // endangered is still contesting
            Assert.True(c.TryGet("party-05", out v));
        }

        [Fact]
        public void Ceilings_AreDeterministic()
        {
            var parties = new List<Party>
            {
                Major("party-01"), Fringe("party-03", Issue.Environment), Fringe("party-04", Issue.Transit)
            };

            var watch = new FringeWatch();
            for (int term = 1; term <= 4; term++) RunTerm(watch, term, CollapsingMonth(), Shipped);

            FringeCeilings a = FringeFailureModel.Ceilings(parties, watch, AngryAbout(Issue.Environment),
                                                           ElectoralSystem.FirstPastThePost, Shipped);
            FringeCeilings b = FringeFailureModel.Ceilings(parties, watch, AngryAbout(Issue.Environment),
                                                           ElectoralSystem.FirstPastThePost, Shipped);

            double x, y;
            Assert.Equal(a.TryGet("party-03", out x), b.TryGet("party-03", out y));
            Assert.Equal(x, y, 12);
        }

        // ------------------------------------------------------------------------------------------
        // Degenerate tuning
        // ------------------------------------------------------------------------------------------

        /// <summary>A ceiling that cannot open is still a valid ceiling: it simply never moves.</summary>
        [Fact]
        public void Ceiling_WithMaxAtOrBelowBase_StaysAtBase()
        {
            FringeTuning t = TuningWith("{\"baseCeiling\":0.10,\"maxCeiling\":0.05}");

            var watch = new FringeWatch();
            for (int term = 1; term <= 10; term++) RunTerm(watch, term, CollapsingMonth(), t);

            Assert.Equal(0.10, FringeFailureModel.CeilingFor(Fringe("party-03", Issue.Environment), watch,
                                                             AngryAbout(Issue.Environment), t), 12);
        }

        [Fact]
        public void DisabledTuning_StopsObservingAndClosing()
        {
            FringeTuning off = TuningWith("{\"enabled\":false}");
            var watch = new FringeWatch();

            RunTerm(watch, 1, CollapsingMonth(), off);

            Assert.Equal(0, watch.MonthsObserved);
            Assert.Equal(0, watch.ConsecutiveFailureTerms);
            Assert.Equal(0, watch.LastClosedTermNumber);
        }

        [Fact]
        public void NullWatch_IsTreatedAsAFreshOne()
        {
            var parties = new List<Party> { Fringe("party-03", Issue.Environment) };

            FringeCeilings c = FringeFailureModel.Ceilings(parties, null, AngryAbout(Issue.Environment),
                                                           ElectoralSystem.FirstPastThePost, Shipped);

            double v;
            Assert.True(c.TryGet("party-03", out v));
            Assert.Equal(Shipped.BaseCeiling, v, 12);
        }
    }
}
