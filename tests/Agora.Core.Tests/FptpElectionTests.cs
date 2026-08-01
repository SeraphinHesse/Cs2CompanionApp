using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Engine.Elections.Fptp;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Packet 8 — NA first-past-the-post district races plus the directly elected mayor.
    ///
    /// <para>
    /// Fixtures are synthetic and built in this file (per <c>/write-test</c>): a small city of
    /// identical districts whose affinities come from a closed-form expression, so every expected
    /// value below can be reasoned about by hand rather than copied out of a previous run.
    /// </para>
    /// </summary>
    public class FptpElectionTests
    {
        private static readonly Guid SaveA = new Guid("11111111-2222-3333-4444-555555555555");
        private static readonly Guid SaveB = new Guid("99999999-8888-7777-6666-555555555555");
        private static readonly SimDate Nov1994 = new SimDate(1994, 11, 8);
        private static readonly SimDate Nov1998 = new SimDate(1998, 11, 8);

        private const string Alpha = "p-alpha";
        private const string Beta = "p-beta";
        private const string Gamma = "p-gamma";

        // Alpha leads everywhere, Beta is the challenger, Gamma is the squeezed third party. The gap
        // between Beta and Gamma is far wider than affinity.tacticalVotingThresholdFptp (0.05), so
        // the Duverger squeeze is guaranteed to engage.
        private static readonly string[] PartyIds = { Alpha, Beta, Gamma };
        private static readonly double[] PartyBaseAffinity = { 0.90, 0.70, 0.35 };

        private static readonly BlocKey[] VotingBlocs =
        {
            new BlocKey(WealthTier.Low, EducationTier.PoorlyEducated, AgeBand.Adult),
            new BlocKey(WealthTier.Middle, EducationTier.Educated, AgeBand.Adult),
            new BlocKey(WealthTier.High, EducationTier.WellEducated, AgeBand.Adult),
            new BlocKey(WealthTier.Middle, EducationTier.Educated, AgeBand.Elderly)
        };

        private static readonly int[] BlocProjectedVotes = { 400, 600, 700, 500 }; // 2200 per district
        private const int BlocEligibleVoters = 1000;                               // 4000 per district

        // Alpha sits a long way from the other two; Gamma is Beta's ideological twin, which is what
        // makes the runoff-transfer direction assertable.
        private static readonly IssuePosition AlphaPlatform =
            new IssuePosition(0.8, -0.8, -0.7, -0.6, 0.8, -0.7);

        private static readonly IssuePosition BetaPlatform =
            new IssuePosition(-0.6, 0.7, 0.7, 0.7, -0.6, 0.6);

        // ------------------------------------------------------------------------------------------
        // Determinism
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// The canonical pattern: identical inputs twice, compared by hash of the serialized result
        /// rather than field by field, so a field a hand-written assertion forgot still fails here.
        /// </summary>
        [Fact]
        public void Run_ProducesIdenticalHashTwice()
        {
            string first = HashOf(FptpElection.Run(BuildInput(SaveA, Nov1994, 3), EngineTuning.Default));
            string second = HashOf(FptpElection.Run(BuildInput(SaveA, Nov1994, 3), EngineTuning.Default));

            Assert.Equal(first, second);
        }

        /// <summary>
        /// The negative half of the determinism pair. Without it, a function returning a constant
        /// would pass the test above perfectly.
        /// </summary>
        [Fact]
        public void Run_DiffersBySaveAndByDate()
        {
            string baseline = HashOf(FptpElection.Run(BuildInput(SaveA, Nov1994, 3), EngineTuning.Default));
            string otherSave = HashOf(FptpElection.Run(BuildInput(SaveB, Nov1994, 3), EngineTuning.Default));
            string otherDate = HashOf(FptpElection.Run(BuildInput(SaveA, Nov1998, 3), EngineTuning.Default));

            Assert.NotEqual(baseline, otherSave);
            Assert.NotEqual(baseline, otherDate);
        }

        /// <summary>
        /// Input list order must not reach the output. The fixture hands the parties over in
        /// reverse-alphabetical order and the blocs shuffled; both results must still match.
        /// </summary>
        [Fact]
        public void Run_IsIndependentOfInputListOrder()
        {
            FptpElectionInput natural = BuildInput(SaveA, Nov1994, 3);
            FptpElectionInput reversed = BuildInput(SaveA, Nov1994, 3);
            reversed.Affinities = ReverseOf(reversed.Affinities);
            reversed.Turnouts = ReverseOf(reversed.Turnouts);
            reversed.Parties = ReverseOf(reversed.Parties);

            Assert.Equal(HashOf(FptpElection.Run(natural, EngineTuning.Default)),
                         HashOf(FptpElection.Run(reversed, EngineTuning.Default)));
        }

        // ------------------------------------------------------------------------------------------
        // Counting invariants
        // ------------------------------------------------------------------------------------------

        [Fact]
        public void Result_ReportsTheContractualShape()
        {
            ElectionResult r = FptpElection.Run(BuildInput(SaveA, Nov1994, 3), EngineTuning.Default);

            Assert.Equal(1, r.SchemaVersion);
            Assert.Equal(ElectoralSystem.FirstPastThePost, r.System);
            Assert.Equal("election-1994-11", r.Id);
            Assert.Equal(new SimDate(1998, 11, 8), r.NextElectionDate);
            Assert.Equal(new[] { Alpha, Beta, Gamma }, r.PartyIdsOnBallot);
            Assert.Equal(new[] { "d-01", "d-02", "d-03" }, DistrictIdsOf(r));
        }

        /// <summary>
        /// Vote counts are whole numbers that sum exactly to the district total (§6). Reported shares
        /// are derived from those counts, so multiplying back must land on an integer.
        /// </summary>
        [Fact]
        public void DistrictVotes_AreWholeAndSumToVotesCast()
        {
            ElectionResult r = FptpElection.Run(BuildInput(SaveA, Nov1994, 3), EngineTuning.Default);

            foreach (DistrictResult d in r.Districts)
            {
                Assert.Equal(2200, d.VotesCast);
                Assert.Equal(4000, d.EligibleVoters);

                int counted = 0;
                foreach (PartyVoteShare s in d.Shares)
                {
                    double votes = s.Share * d.VotesCast;
                    Assert.True(Math.Abs(votes - Math.Round(votes)) < 1e-6,
                        "share " + s.PartyId + " = " + s.Share + " is not a whole number of votes");
                    counted += (int)Math.Round(votes);
                }

                Assert.Equal(d.VotesCast, counted);
            }
        }

        [Fact]
        public void CityTotals_AggregateTheDistricts()
        {
            ElectionResult r = FptpElection.Run(BuildInput(SaveA, Nov1994, 3), EngineTuning.Default);

            Assert.Equal(6600, r.TotalVotesCast);
            Assert.Equal(12000, r.TotalEligibleVoters);
            Assert.Equal(0.55, r.Turnout, 10);

            double sum = 0.0;
            foreach (PartyVoteShare s in r.CityVoteShares) sum += s.Share;
            Assert.Equal(1.0, sum, 9);
        }

        /// <summary>Every list of shares is sorted by party id ordinal ascending — contractual (§6).</summary>
        [Fact]
        public void EveryShareList_IsSortedByPartyIdOrdinal()
        {
            ElectionResult r = FptpElection.Run(BuildInput(SaveA, Nov1994, 3), EngineTuning.Default);

            AssertSortedByPartyId(r.CityVoteShares);
            AssertSortedByPartyId(r.MayorVoteShares);
            foreach (DistrictResult d in r.Districts) AssertSortedByPartyId(d.Shares);

            for (int i = 1; i < r.Seats.Count; i++)
                Assert.True(string.CompareOrdinal(r.Seats[i - 1].PartyId, r.Seats[i].PartyId) < 0);
        }

        /// <summary>
        /// The seat a district awards belongs to the party with the most votes in it. The only
        /// permitted exception is a seat the tie-break stream decided.
        /// </summary>
        [Fact]
        public void DistrictWinner_HoldsTheMostVotes()
        {
            ElectionResult r = FptpElection.Run(BuildInput(SaveA, Nov1994, 3), EngineTuning.Default);

            foreach (DistrictResult d in r.Districts)
            {
                Assert.False(d.DecidedByTieBreak);   // this fixture has no dead heats
                Assert.NotEqual("", d.WinningPartyId);

                double winning = ShareOf(d.Shares, d.WinningPartyId);
                foreach (PartyVoteShare s in d.Shares)
                    Assert.True(winning >= s.Share, d.DistrictId + ": " + s.PartyId + " out-polled the winner");

                Assert.True(d.Margin > 0.0);
                Assert.Equal(1, d.Seats);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Seats
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Three districts would otherwise elect a three-member council, in which one district win is
        /// an absolute majority. <c>minCouncilSeats</c> tops it up to nine from the city-wide vote.
        /// </summary>
        [Fact]
        public void Chamber_TopsUpToTheMinimumWithAtLargeSeats()
        {
            FptpChamber chamber = FptpSeatMath.Chamber(3, EngineTuning.Default);

            Assert.Equal(1, chamber.SeatsPerDistrict);
            Assert.Equal(3, chamber.DistrictSeats);
            Assert.Equal(6, chamber.AtLargeSeats);
            Assert.Equal(9, chamber.TotalSeats);
        }

        /// <summary>
        /// <c>maxCouncilSeats</c> bites on the seats-per-district first, so no district loses its
        /// representation to satisfy a size cap.
        /// </summary>
        [Fact]
        public void Chamber_ShrinksSeatsPerDistrictBeforeExceedingTheMaximum()
        {
            EngineTuning t = TuningWith("{\"electionsFptp\":{\"councilSeatsPerDistrict\":5}}");

            FptpChamber chamber = FptpSeatMath.Chamber(20, t);   // 20 x 5 = 100 > 45

            Assert.Equal(2, chamber.SeatsPerDistrict);            // 45 / 20 = 2
            Assert.Equal(40, chamber.DistrictSeats);
            Assert.Equal(0, chamber.AtLargeSeats);
            Assert.Equal(40, chamber.TotalSeats);
        }

        /// <summary>
        /// The documented exception: more districts than the cap allows seats. Representation wins —
        /// every district still returns one member and the chamber exceeds the nominal maximum.
        /// </summary>
        [Fact]
        public void Chamber_NeverLeavesADistrictUnrepresented()
        {
            FptpChamber chamber = FptpSeatMath.Chamber(50, EngineTuning.Default);

            Assert.Equal(1, chamber.SeatsPerDistrict);
            Assert.Equal(50, chamber.TotalSeats);
            Assert.Equal(0, chamber.AtLargeSeats);
        }

        [Fact]
        public void Seats_SumToTotalSeatsAndSplitIntoDistrictAndAtLarge()
        {
            ElectionResult r = FptpElection.Run(BuildInput(SaveA, Nov1994, 3), EngineTuning.Default);

            int seats = 0, districtSeats = 0, atLarge = 0;
            foreach (SeatAllocation s in r.Seats)
            {
                Assert.Equal(s.DistrictSeats + s.ListSeats, s.Seats);
                seats += s.Seats;
                districtSeats += s.DistrictSeats;
                atLarge += s.ListSeats;
            }

            Assert.Equal(9, r.TotalSeats);
            Assert.Equal(9, seats);
            Assert.Equal(3, districtSeats);
            Assert.Equal(6, atLarge);
        }

        /// <summary>
        /// A district where nobody turned out awards nothing. Its seat is not handed to a party on a
        /// technicality, and the chamber shrinks by exactly that seat.
        /// </summary>
        [Fact]
        public void DistrictWithNoVotes_AwardsNoSeat()
        {
            ElectionResult r = FptpElection.Run(
                BuildInput(SaveA, Nov1994, 4, emptyDistricts: 1), EngineTuning.Default);

            DistrictResult empty = r.Districts[3];
            Assert.Equal("d-04", empty.DistrictId);
            Assert.Equal(0, empty.VotesCast);
            Assert.Equal("", empty.WinningPartyId);
            Assert.Equal(0, empty.Seats);
            Assert.Equal(0.0, empty.Turnout, 12);

            // 4 districts -> chamber of 4 + 5 at-large = 9 nominal, but only 3 district seats are won.
            int seats = 0;
            foreach (SeatAllocation s in r.Seats) seats += s.Seats;
            Assert.Equal(8, r.TotalSeats);
            Assert.Equal(8, seats);
        }

        // ------------------------------------------------------------------------------------------
        // Behaviour
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// The wasted-vote squeeze must actually cost the third party support, and must cost it more
        /// in the seat count than in the popular vote — that disproportionality is the whole point of
        /// modelling FPTP separately from PR.
        /// </summary>
        [Fact]
        public void ThirdPartyPenalty_SqueezesTheTrailingParty()
        {
            EngineTuning noSqueeze = TuningWith("{\"electionsFptp\":{\"thirdPartyPenalty\":0.0}}");

            ElectionResult with = FptpElection.Run(BuildInput(SaveA, Nov1994, 3), EngineTuning.Default);
            ElectionResult without = FptpElection.Run(BuildInput(SaveA, Nov1994, 3), noSqueeze);

            double squeezed = ShareOf(with.CityVoteShares, Gamma);
            double free = ShareOf(without.CityVoteShares, Gamma);

            Assert.True(squeezed < free,
                "gamma polled " + squeezed + " with the squeeze and " + free + " without it");

            // And the squeeze moves support up, not out of the count.
            Assert.True(ShareOf(with.CityVoteShares, Alpha) > ShareOf(without.CityVoteShares, Alpha));

            // The disproportionality that makes FPTP worth modelling separately: a party polling
            // several percent city-wide carries no district at all.
            Assert.True(squeezed > 0.02, "fixture must leave gamma a visible city-wide vote");
            Assert.Equal(0, DistrictSeatsOf(with.Seats, Gamma));
        }

        /// <summary>
        /// Coattails: the incumbent mayor's personal bonus has to reach the council races, or
        /// <c>straightTicketFactor</c> is decorative.
        /// </summary>
        [Fact]
        public void IncumbentMayor_LiftsHisPartysCouncilVoteThroughCoattails()
        {
            ElectionResult open = FptpElection.Run(BuildInput(SaveA, Nov1994, 3), EngineTuning.Default);
            ElectionResult defended = FptpElection.Run(
                BuildInput(SaveA, Nov1994, 3, incumbentMayor: Beta), EngineTuning.Default);

            Assert.True(ShareOf(defended.MayorVoteShares, Beta) > ShareOf(open.MayorVoteShares, Beta));
            Assert.True(ShareOf(defended.CityVoteShares, Beta) > ShareOf(open.CityVoteShares, Beta),
                "beta polled " + ShareOf(defended.CityVoteShares, Beta) + " defending the mayoralty and "
                + ShareOf(open.CityVoteShares, Beta) + " in an open race");
        }

        /// <summary>
        /// Turning coattails off must leave the council races alone even when the mayoral race moves.
        /// This is the control for the test above.
        /// </summary>
        [Fact]
        public void StraightTicketFactorOfZero_LeavesTheCouncilVoteUnmoved()
        {
            EngineTuning noCoattails = TuningWith("{\"electionsFptp\":{\"straightTicketFactor\":0.0}}");

            ElectionResult open = FptpElection.Run(BuildInput(SaveA, Nov1994, 3), noCoattails);
            ElectionResult defended = FptpElection.Run(
                BuildInput(SaveA, Nov1994, 3, incumbentMayor: Beta), noCoattails);

            Assert.True(ShareOf(defended.MayorVoteShares, Beta) > ShareOf(open.MayorVoteShares, Beta));
            Assert.Equal(ShareOf(open.CityVoteShares, Beta), ShareOf(defended.CityVoteShares, Beta), 12);
        }

        /// <summary>
        /// The mayoralty is a separate, city-wide contest, and it is the leader of that contest who
        /// takes it — not the party with the most council seats.
        /// </summary>
        [Fact]
        public void Mayor_IsTheLeaderOfTheCityWideRace()
        {
            ElectionResult r = FptpElection.Run(BuildInput(SaveA, Nov1994, 3), EngineTuning.Default);

            Assert.Equal(Alpha, r.MayorPartyId);
            Assert.Null(r.MayorName);   // flavor-owned; the engine never invents a name

            double top = ShareOf(r.MayorVoteShares, Alpha);
            foreach (PartyVoteShare s in r.MayorVoteShares) Assert.True(top >= s.Share);
        }

        /// <summary>
        /// With a runoff threshold set, the eliminated third party's support breaks toward its
        /// ideological twin rather than splitting evenly.
        /// </summary>
        [Fact]
        public void MayorRunoff_TransfersEliminatedSupportByIdeologicalProximity()
        {
            EngineTuning runoff = TuningWith("{\"electionsFptp\":{\"mayorRunoffThreshold\":0.99}}");

            ElectionResult plurality = FptpElection.Run(BuildInput(SaveA, Nov1994, 3), EngineTuning.Default);
            ElectionResult second = FptpElection.Run(BuildInput(SaveA, Nov1994, 3), runoff);

            double gammaFirstRound = ShareOf(plurality.MayorVoteShares, Gamma);
            Assert.True(gammaFirstRound > 0.0, "fixture must leave gamma something to transfer");

            Assert.Equal(0.0, ShareOf(second.MayorVoteShares, Gamma), 12);
            Assert.Equal(1.0,
                ShareOf(second.MayorVoteShares, Alpha) + ShareOf(second.MayorVoteShares, Beta), 9);

            // Gamma's platform is Beta's platform, so more than half of it must break to Beta.
            double betaGain = ShareOf(second.MayorVoteShares, Beta) - ShareOf(plurality.MayorVoteShares, Beta);
            Assert.True(betaGain > 0.5 * gammaFirstRound,
                "beta gained " + betaGain + " of gamma's " + gammaFirstRound);

            // The runoff decides the mayoralty only; the council count is untouched.
            Assert.Equal(ShareOf(plurality.CityVoteShares, Gamma), ShareOf(second.CityVoteShares, Gamma), 12);
        }

        /// <summary>
        /// A genuine dead heat goes to the <c>election.tiebreak</c> stream, is flagged as such, and
        /// resolves the same way on every replay of the save. An in-place coin flip or an
        /// alphabetical fallback would both pass a naive "someone won" assertion; neither would
        /// survive this one.
        /// </summary>
        [Fact]
        public void DeadHeat_IsResolvedByTheTieBreakStreamAndIsReproducible()
        {
            EngineTuning noSwing = TuningWith("{\"electionsFptp\":{\"districtSwingSigma\":0.0}}");

            ElectionResult first = FptpElection.Run(BuildTiedInput(SaveA, Nov1994), noSwing);
            ElectionResult again = FptpElection.Run(BuildTiedInput(SaveA, Nov1994), noSwing);

            DistrictResult d = Assert.Single(first.Districts);
            Assert.True(d.DecidedByTieBreak);
            Assert.Equal(0.5, ShareOf(d.Shares, "p-one"), 12);
            Assert.Equal(0.5, ShareOf(d.Shares, "p-two"), 12);
            Assert.Equal(0.0, d.Margin, 12);
            Assert.True(d.WinningPartyId == "p-one" || d.WinningPartyId == "p-two", d.WinningPartyId);

            Assert.Equal(d.WinningPartyId, again.Districts[0].WinningPartyId);
            Assert.Equal(first.MayorPartyId, again.MayorPartyId);

            // A different save must be free to break the same tie the other way, so the tie-break is
            // seeded rather than a constant. Assert only that it is still a valid, flagged decision.
            ElectionResult otherSave = FptpElection.Run(BuildTiedInput(SaveB, Nov1994), noSwing);
            DistrictResult elsewhere = otherSave.Districts[0];
            Assert.True(elsewhere.DecidedByTieBreak);
            Assert.True(elsewhere.WinningPartyId == "p-one" || elsewhere.WinningPartyId == "p-two",
                elsewhere.WinningPartyId);
        }

        [Fact]
        public void Ballot_ExcludesDissolvedMergedAndUnbornParties()
        {
            FptpElectionInput input = BuildInput(SaveA, Nov1994, 3);
            var parties = new List<Party>(input.Parties)
            {
                new Party { Id = "p-dead", Status = PartyStatus.Dissolved, FoundedDate = new SimDate(1990, 1, 1) },
                new Party { Id = "p-absorbed", Status = PartyStatus.Merged, FoundedDate = new SimDate(1990, 1, 1) },
                new Party { Id = "p-future", Status = PartyStatus.Active, FoundedDate = new SimDate(2000, 1, 1) },
                new Party { Id = "p-ghost", Status = PartyStatus.Active, FoundedDate = new SimDate(1990, 1, 1) }
            };
            input.Parties = parties;

            ElectionResult r = FptpElection.Run(input, EngineTuning.Default);

            // p-ghost exists and is active, but the voter model never scored it, so it cannot be
            // counted: an unscored party would otherwise inherit the neutral baseline everywhere.
            Assert.Equal(new[] { Alpha, Beta, Gamma }, r.PartyIdsOnBallot);
        }

        [Fact]
        public void FinalPollDeviation_IsTheMeanAbsoluteErrorOfTheLastPoll()
        {
            FptpElectionInput input = BuildInput(SaveA, Nov1994, 3);
            ElectionResult truth = FptpElection.Run(input, EngineTuning.Default);

            var poll = new PollResult { Shares = new List<PartyVoteShare>() };
            foreach (PartyVoteShare s in truth.CityVoteShares)
                poll.Shares.Add(new PartyVoteShare(s.PartyId, s.Share + (s.PartyId == Alpha ? 0.03 : -0.015)));

            input.FinalPoll = poll;
            ElectionResult r = FptpElection.Run(input, EngineTuning.Default);

            Assert.Equal((0.03 + 0.015 + 0.015) / 3.0, r.FinalPollDeviation, 9);
        }

        [Fact]
        public void EmptyCity_ReturnsAWellFormedEmptyResult()
        {
            var input = new FptpElectionInput { SaveGuid = SaveA, Date = Nov1994, TermNumber = 1 };

            ElectionResult r = FptpElection.Run(input, EngineTuning.Default);

            Assert.Empty(r.Districts);
            Assert.Empty(r.Seats);
            Assert.Equal(0, r.TotalSeats);
            Assert.Null(r.MayorPartyId);
            Assert.Equal(new SimDate(1998, 11, 8), r.NextElectionDate);
        }

        // ------------------------------------------------------------------------------------------
        // Calendar
        // ------------------------------------------------------------------------------------------

        [Fact]
        public void Calendar_UsesTheFourYearNaTerm()
        {
            EngineTuning t = EngineTuning.Default;

            Assert.Equal(new SimDate(1998, 11, 8), FptpCalendar.NextElection(Nov1994, t));
            Assert.Equal(new SimDate(1998, 11, 8), FptpCalendar.MayorTermEnd(Nov1994, t));
            Assert.Equal(new SimDate(1994, 5, 8), FptpCalendar.CampaignStart(Nov1994, t));

            Assert.True(FptpCalendar.IsCampaignSeason(new SimDate(1994, 6, 1), Nov1994, t));
            Assert.False(FptpCalendar.IsCampaignSeason(new SimDate(1994, 4, 30), Nov1994, t));
            Assert.False(FptpCalendar.IsCampaignSeason(new SimDate(1994, 6, 1), null, t));
        }

        // ------------------------------------------------------------------------------------------
        // Fixtures
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// A synthetic city of <paramref name="districtCount"/> identical-sized districts. Affinity is
        /// a closed-form function of (district, bloc, party) so the expected ordering is obvious:
        /// Alpha ahead everywhere, Beta second, Gamma far enough back to be squeezed.
        /// </summary>
        private static FptpElectionInput BuildInput(Guid save, SimDate date, int districtCount,
                                                    string? incumbentMayor = null, int emptyDistricts = 0)
        {
            var affinities = new List<BlocAffinity>();
            var turnouts = new List<BlocTurnout>();

            for (int d = 0; d < districtCount; d++)
            {
                string districtId = "d-" + (d + 1).ToString("D2", CultureInfo.InvariantCulture);
                bool empty = d >= districtCount - emptyDistricts;

                for (int b = 0; b < VotingBlocs.Length; b++)
                {
                    BlocKey key = VotingBlocs[b];

                    turnouts.Add(new BlocTurnout
                    {
                        DistrictId = districtId,
                        Bloc = key,
                        EligibleVoters = BlocEligibleVoters,
                        ProjectedVotes = empty ? 0 : BlocProjectedVotes[b],
                        Turnout = empty ? 0.0 : BlocProjectedVotes[b] / (double)BlocEligibleVoters
                    });

                    for (int p = 0; p < PartyIds.Length; p++)
                    {
                        affinities.Add(new BlocAffinity
                        {
                            DistrictId = districtId,
                            Bloc = key,
                            PartyId = PartyIds[p],
                            Affinity = PartyBaseAffinity[p]
                                       + (p == d % PartyIds.Length ? 0.15 : 0.0)   // local strength
                                       + 0.01 * ((key.Ordinal + p) % 5)            // bloc texture
                        });
                    }
                }
            }

            // Deliberately not in ballot order: the packet must sort, not trust the caller.
            var parties = new List<Party>
            {
                new Party { Id = Gamma, Status = PartyStatus.Active, FoundedDate = new SimDate(1990, 1, 1), Platform = BetaPlatform },
                new Party { Id = Alpha, Status = PartyStatus.Active, FoundedDate = new SimDate(1990, 1, 1), Platform = AlphaPlatform },
                new Party { Id = Beta, Status = PartyStatus.Active, FoundedDate = new SimDate(1990, 1, 1), Platform = BetaPlatform }
            };

            return new FptpElectionInput
            {
                SaveGuid = save,
                Date = date,
                TermNumber = 2,
                Parties = parties,
                Affinities = affinities,
                Turnouts = turnouts,
                IncumbentMayorPartyId = incumbentMayor
            };
        }

        /// <summary>
        /// One district, two parties, identical affinity in every bloc. With
        /// <c>districtSwingSigma</c> at zero this is an exact 50/50 dead heat — the case the
        /// <c>election.tiebreak</c> stream exists for.
        /// </summary>
        private static FptpElectionInput BuildTiedInput(Guid save, SimDate date)
        {
            var affinities = new List<BlocAffinity>();
            var turnouts = new List<BlocTurnout>();
            string[] twins = { "p-one", "p-two" };

            for (int b = 0; b < VotingBlocs.Length; b++)
            {
                BlocKey key = VotingBlocs[b];

                turnouts.Add(new BlocTurnout
                {
                    DistrictId = "d-01",
                    Bloc = key,
                    EligibleVoters = BlocEligibleVoters,
                    ProjectedVotes = BlocProjectedVotes[b]
                });

                for (int p = 0; p < twins.Length; p++)
                {
                    affinities.Add(new BlocAffinity
                    {
                        DistrictId = "d-01",
                        Bloc = key,
                        PartyId = twins[p],
                        Affinity = 0.60
                    });
                }
            }

            var parties = new List<Party>();
            foreach (string id in twins)
                parties.Add(new Party { Id = id, Status = PartyStatus.Active, FoundedDate = new SimDate(1990, 1, 1) });

            return new FptpElectionInput
            {
                SaveGuid = save,
                Date = date,
                Parties = parties,
                Affinities = affinities,
                Turnouts = turnouts
            };
        }

        // ------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------

        private static EngineTuning TuningWith(string json)
        {
            EngineTuning t = EngineTuning.FromJson(json);
            Assert.Equal(2, t.SchemaVersion);   // unspecified sections fall back to the shipped defaults
            return t;
        }

        private static List<T> ReverseOf<T>(IReadOnlyList<T> source)
        {
            var copy = new List<T>(source.Count);
            for (int i = source.Count - 1; i >= 0; i--) copy.Add(source[i]);
            return copy;
        }

        private static double ShareOf(List<PartyVoteShare> shares, string partyId)
        {
            foreach (PartyVoteShare s in shares)
                if (s.PartyId == partyId) return s.Share;
            return 0.0;
        }

        private static int DistrictSeatsOf(List<SeatAllocation> seats, string partyId)
        {
            foreach (SeatAllocation s in seats)
                if (s.PartyId == partyId) return s.DistrictSeats;
            return 0;
        }

        private static string[] DistrictIdsOf(ElectionResult r)
        {
            var ids = new string[r.Districts.Count];
            for (int i = 0; i < r.Districts.Count; i++) ids[i] = r.Districts[i].DistrictId;
            return ids;
        }

        private static void AssertSortedByPartyId(List<PartyVoteShare> shares)
        {
            for (int i = 1; i < shares.Count; i++)
                Assert.True(string.CompareOrdinal(shares[i - 1].PartyId, shares[i].PartyId) < 0,
                    "share list is not sorted by party id ordinal ascending");
        }

        /// <summary>
        /// SHA-256 over a canonical rendering of the whole result. Round-trip ("R") formatting keeps
        /// the comparison exact — a one-ulp drift in a share is a desync and must fail here.
        /// </summary>
        private static string HashOf(ElectionResult r)
        {
            var sb = new StringBuilder();
            sb.Append(r.SchemaVersion).Append('|').Append(r.Id).Append('|').Append(r.Date).Append('|')
              .Append(r.System).Append('|').Append(r.TermNumber).Append('|').Append(r.IsSnapElection)
              .Append('|').Append(r.TotalSeats).Append('|').Append(F(r.Turnout))
              .Append('|').Append(r.TotalVotesCast).Append('|').Append(r.TotalEligibleVoters)
              .Append('|').Append(r.MayorPartyId ?? "-").Append('|').Append(r.MayorName ?? "-")
              .Append('|').Append(F(r.FinalPollDeviation)).Append('|').Append(r.NextElectionDate)
              .Append('\n');

            sb.Append(string.Join(",", r.PartyIdsOnBallot)).Append('\n');
            AppendShares(sb, r.CityVoteShares);
            AppendShares(sb, r.MayorVoteShares);

            foreach (DistrictResult d in r.Districts)
            {
                sb.Append(d.DistrictId).Append('|').Append(F(d.Turnout)).Append('|').Append(d.VotesCast)
                  .Append('|').Append(d.EligibleVoters).Append('|').Append(d.WinningPartyId)
                  .Append('|').Append(F(d.Margin)).Append('|').Append(d.Seats)
                  .Append('|').Append(d.DecidedByTieBreak).Append('\n');
                AppendShares(sb, d.Shares);
            }

            foreach (SeatAllocation s in r.Seats)
            {
                sb.Append(s.PartyId).Append('|').Append(s.Seats).Append('|').Append(F(s.SeatShare))
                  .Append('|').Append(F(s.VoteShare)).Append('|').Append(s.DistrictSeats)
                  .Append('|').Append(s.ListSeats).Append('|').Append(s.PassedThreshold).Append('\n');
            }

            using (var sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(digest.Length * 2);
                foreach (byte b in digest) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        private static void AppendShares(StringBuilder sb, List<PartyVoteShare> shares)
        {
            foreach (PartyVoteShare s in shares) sb.Append(s.PartyId).Append('=').Append(F(s.Share)).Append(';');
            sb.Append('\n');
        }

        private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    }
}
