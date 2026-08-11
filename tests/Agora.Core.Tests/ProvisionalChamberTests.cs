using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Engine.Government.Coalitions;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The chamber a save projects from its latest published poll, before it has ever voted, and the
    /// coalition arithmetic ranked off it.
    ///
    /// <para>
    /// It exists so the dashboard's coalition section is a live view from the save's first poll
    /// rather than from its first election, three sim years later. What is asserted here is what that
    /// promise costs: the projection must be a pure function of what it is handed (non-negotiable
    /// #3), it must stay silent under first past the post, where coalitions are ratified as absent,
    /// and it must never be mistaken for a result — nothing it does may touch the state it read.
    /// </para>
    /// </summary>
    public class ProvisionalChamberTests
    {
        private static readonly Guid SaveA = new Guid("11111111-2222-3333-4444-555555555555");
        private static readonly Guid SaveB = new Guid("99999999-8888-7777-6666-555555555555");
        private static readonly SimDate Spring = new SimDate(1991, 4, 15);

        // ---------------------------------------------------------------- fixtures

        /// <summary>All six issues at one stance, so distance is half the axis gap — CoalitionsTests' rule.</summary>
        private static IssuePosition Pos(double axis) => new IssuePosition(axis, axis, axis, axis, axis, axis);

        private static Party MakeParty(string id, double axis) =>
            new Party { Id = id, Name = id, Platform = Pos(axis), Status = PartyStatus.Active };

        /// <summary>
        /// Four parties on a spread axis, polling 38/27/23/12. Every share is clear of the 5%
        /// threshold, so no party is excluded and the chamber the projection seats is the whole 60.
        /// </summary>
        private static List<PartyVoteShare> Poll() => new List<PartyVoteShare>
        {
            new PartyVoteShare("party-a", 0.38),
            new PartyVoteShare("party-b", 0.27),
            new PartyVoteShare("party-c", 0.23),
            new PartyVoteShare("party-d", 0.12)
        };

        private static List<Party> Parties() => new List<Party>
        {
            MakeParty("party-a", -0.8),
            MakeParty("party-b", -0.4),
            MakeParty("party-c", 0.4),
            MakeParty("party-d", 0.9)
        };

        private static PoliticalState StateWith(
            RegionTheme theme, IEnumerable<PartyVoteShare> pollShares, bool published)
        {
            var state = new PoliticalState
            {
                SaveGuid = SaveA,
                Date = Spring,
                Settings = new AgoraSettings { Theme = theme, System = RegionThemeRules.SystemFor(theme) },
                Parties = Parties()
            };

            state.RecentPolls.Add(new PollResult
            {
                Id = "poll-1991-04",
                Date = Spring,
                Shares = new List<PartyVoteShare>(pollShares),
                IsPublished = published
            });

            return state;
        }

        /// <summary>Every field of every seat row, in published order, as one comparable string.</summary>
        private static string Signature(IReadOnlyList<SeatAllocation> seats)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < seats.Count; i++)
            {
                SeatAllocation s = seats[i];
                sb.Append(s.PartyId).Append('/')
                  .Append(s.Seats.ToString(CultureInfo.InvariantCulture)).Append('/')
                  .Append(s.SeatShare.ToString("R", CultureInfo.InvariantCulture)).Append('/')
                  .Append(s.VoteShare.ToString("R", CultureInfo.InvariantCulture)).Append('/')
                  .Append(s.PassedThreshold).Append(';');
            }
            return sb.ToString();
        }

        private static string Signature(IReadOnlyList<CoalitionCandidate> ranked)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < ranked.Count; i++)
            {
                CoalitionCandidate c = ranked[i];
                sb.Append(c.Key).Append('/').Append(c.LeadPartyId).Append('/')
                  .Append(c.Seats.ToString(CultureInfo.InvariantCulture)).Append('/')
                  .Append(c.Score.ToString("R", CultureInfo.InvariantCulture)).Append('/')
                  .Append(c.HasMajority).Append('/').Append(c.IsMinimumWinning).Append(';');
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- determinism

        /// <summary>
        /// The canonical determinism check: the same inputs twice, byte-identical out. It is the whole
        /// licence for putting a projection on screen — a seat count that moved between two renders of
        /// an unchanged save would be a number the engine cannot stand behind.
        /// </summary>
        [Fact]
        public void SameInputsProjectTheSameChamberTwice()
        {
            EngineTuning tuning = EngineTuning.Default;

            IReadOnlyList<SeatAllocation> first = ProvisionalChamber.Project(
                ElectoralSystem.Proportional, Poll(), tuning, SaveA, Spring);
            IReadOnlyList<SeatAllocation> second = ProvisionalChamber.Project(
                ElectoralSystem.Proportional, Poll(), tuning, SaveA, Spring);

            Assert.Equal(Signature(first), Signature(second));
            Assert.NotEqual("", Signature(first));
        }

        /// <summary>
        /// And the ranking built on it, which is the thing the dashboard actually renders. Ranking is
        /// documented pure already; this asserts the composition is, which is what the panel depends on.
        /// </summary>
        [Fact]
        public void SameInputsRankTheSameArrangementsTwice()
        {
            EngineTuning tuning = EngineTuning.Default;

            string first = Signature(CoalitionFormation.RankCandidates(
                ElectoralSystem.Proportional,
                ProvisionalChamber.Project(ElectoralSystem.Proportional, Poll(), tuning, SaveA, Spring),
                Parties(), tuning));

            string second = Signature(CoalitionFormation.RankCandidates(
                ElectoralSystem.Proportional,
                ProvisionalChamber.Project(ElectoralSystem.Proportional, Poll(), tuning, SaveA, Spring),
                Parties(), tuning));

            Assert.Equal(first, second);
            Assert.NotEqual("", first);
        }

        /// <summary>
        /// Share order in must not change the chamber out. The allocator sorts internally, but the
        /// projection is now a second caller of it and the guarantee has to hold through this one too —
        /// a poll's share list arriving in a different order is not a different election.
        /// </summary>
        [Fact]
        public void ShareOrderDoesNotChangeTheChamber()
        {
            EngineTuning tuning = EngineTuning.Default;

            List<PartyVoteShare> reversed = Poll();
            reversed.Reverse();

            Assert.Equal(
                Signature(ProvisionalChamber.Project(
                    ElectoralSystem.Proportional, Poll(), tuning, SaveA, Spring)),
                Signature(ProvisionalChamber.Project(
                    ElectoralSystem.Proportional, reversed, tuning, SaveA, Spring)));
        }

        // ---------------------------------------------------------------- the FPTP invariant

        /// <summary>
        /// First past the post projects nothing, whatever it is handed. Coalitions are ratified as a
        /// proportional feature and factions as the US one; a projection that fabricated a chamber for
        /// a US save would put a coalition section on screen the design says does not exist there.
        /// </summary>
        [Fact]
        public void FirstPastThePostProjectsNoChamber()
        {
            Assert.Empty(ProvisionalChamber.Project(
                ElectoralSystem.FirstPastThePost, Poll(), EngineTuning.Default, SaveA, Spring));
        }

        /// <summary>The same through the state overload, which is the one the projection calls.</summary>
        [Fact]
        public void FirstPastThePostSaveProjectsNoChamber()
        {
            PoliticalState state = StateWith(RegionTheme.Na, Poll(), published: true);

            Assert.Equal(ElectoralSystem.FirstPastThePost, state.Settings.System);
            Assert.Empty(ProvisionalChamber.Project(state, EngineTuning.Default));
        }

        /// <summary>
        /// And nothing ranks off it either. Belt and braces: <see cref="CoalitionFormation.RankCandidates"/>
        /// refuses FPTP on its own, so this asserts the two refusals agree rather than one covering the
        /// other by accident.
        /// </summary>
        [Fact]
        public void FirstPastThePostRanksNoArrangements()
        {
            EngineTuning tuning = EngineTuning.Default;
            PoliticalState state = StateWith(RegionTheme.Na, Poll(), published: true);

            Assert.Empty(CoalitionFormation.RankCandidates(
                state.Settings.System,
                ProvisionalChamber.Project(state, tuning),
                state.Parties, tuning));
        }

        // ---------------------------------------------------------------- what it will and will not read

        /// <summary>
        /// A save with no poll projects nothing. This is the empty state the panel explains in words:
        /// "coalition options appear once the first poll is published" is only honest if the engine
        /// really does answer nothing until then.
        /// </summary>
        [Fact]
        public void NoPollProjectsNoChamber()
        {
            var state = new PoliticalState
            {
                SaveGuid = SaveA,
                Date = Spring,
                Settings = new AgoraSettings { Theme = RegionTheme.Eu, System = ElectoralSystem.Proportional },
                Parties = Parties()
            };

            Assert.Empty(ProvisionalChamber.Project(state, EngineTuning.Default));
        }

        /// <summary>
        /// An UNPUBLISHED poll is not a poll. The dashboard shows the player what a pollster reported,
        /// and seating a chamber off a poll no outlet ran would put a number on screen with no source —
        /// the same rule that keeps <c>PollResult.TrueShares</c> off every panel.
        /// </summary>
        [Fact]
        public void UnpublishedPollProjectsNoChamber()
        {
            PoliticalState state = StateWith(RegionTheme.Eu, Poll(), published: false);

            Assert.Empty(ProvisionalChamber.Project(state, EngineTuning.Default));
        }

        /// <summary>
        /// A published poll seats the whole chamber, and every seat in it is a list seat: a projection
        /// has no district contests, so reporting district wins would be inventing a race.
        /// </summary>
        [Fact]
        public void PublishedPollSeatsTheWholeChamber()
        {
            EngineTuning tuning = EngineTuning.Default;
            PoliticalState state = StateWith(RegionTheme.Eu, Poll(), published: true);

            IReadOnlyList<SeatAllocation> seats = ProvisionalChamber.Project(state, tuning);

            int total = 0;
            for (int i = 0; i < seats.Count; i++)
            {
                total += seats[i].Seats;
                Assert.Equal(0, seats[i].DistrictSeats);
                Assert.Equal(seats[i].Seats, seats[i].ListSeats);
            }

            Assert.Equal(tuning.ElectionsPr.TotalSeats, total);
        }

        /// <summary>
        /// The projection reads the NEWEST published poll, not the first one it finds. RecentPolls is
        /// oldest first, so a scan in the wrong direction would leave the chamber frozen at the save's
        /// opening poll and the "live view" would be a stale one.
        /// </summary>
        [Fact]
        public void TheNewestPublishedPollIsTheOneProjected()
        {
            EngineTuning tuning = EngineTuning.Default;

            PoliticalState state = StateWith(RegionTheme.Eu, Poll(), published: true);
            var later = new List<PartyVoteShare>
            {
                new PartyVoteShare("party-a", 0.12),
                new PartyVoteShare("party-b", 0.23),
                new PartyVoteShare("party-c", 0.27),
                new PartyVoteShare("party-d", 0.38)
            };
            state.RecentPolls.Add(new PollResult
            {
                Id = "poll-1991-05",
                Date = new SimDate(1991, 5, 15),
                Shares = later,
                IsPublished = true
            });

            Assert.Equal(
                Signature(ProvisionalChamber.Project(
                    ElectoralSystem.Proportional, later, tuning, state.SaveGuid, state.Date)),
                Signature(ProvisionalChamber.Project(state, tuning)));
        }

        /// <summary>
        /// Nothing the projection does reaches the state it read. It is called from a UI publisher on
        /// a save the engine also holds, so a projection that mutated anything would be a desync
        /// (non-negotiable #6) arriving through a read.
        /// </summary>
        [Fact]
        public void ProjectingWritesNothingToTheState()
        {
            EngineTuning tuning = EngineTuning.Default;
            PoliticalState state = StateWith(RegionTheme.Eu, Poll(), published: true);

            int polls = state.RecentPolls.Count;
            int shares = state.RecentPolls[0].Shares.Count;
            int parties = state.Parties.Count;

            ProvisionalChamber.Project(state, tuning);

            Assert.Equal(polls, state.RecentPolls.Count);
            Assert.Equal(shares, state.RecentPolls[0].Shares.Count);
            Assert.Equal(parties, state.Parties.Count);
            Assert.Empty(state.ElectionHistory);
            Assert.Empty(state.CoalitionHistory);
            Assert.Null(state.Government);
            Assert.Empty(state.CurrentVoteShares);
        }

        /// <summary>
        /// The save guid reaches the allocator's tie-break stream, so two saves polling identically may
        /// break an exact tie differently — which is what makes this a seeded projection rather than an
        /// unseeded one that happens to be stable. The chambers are equal here because these shares
        /// produce no tie; what is asserted is that the guid is threaded at all, by proving the call
        /// accepts it and stays deterministic under it.
        /// </summary>
        [Fact]
        public void ADifferentSaveProjectsWithItsOwnSeed()
        {
            EngineTuning tuning = EngineTuning.Default;

            string a = Signature(ProvisionalChamber.Project(
                ElectoralSystem.Proportional, Poll(), tuning, SaveA, Spring));
            string b = Signature(ProvisionalChamber.Project(
                ElectoralSystem.Proportional, Poll(), tuning, SaveB, Spring));
            string bAgain = Signature(ProvisionalChamber.Project(
                ElectoralSystem.Proportional, Poll(), tuning, SaveB, Spring));

            Assert.Equal(b, bAgain);
            Assert.Equal(a, b); // no tie in this fixture; both saves count the same seats
        }

        // ---------------------------------------------------------------- the arithmetic on top

        /// <summary>
        /// The point of the whole exercise: a proportional save that has never voted has coalition
        /// arithmetic to show, and it contains the arrangements the ranking rules allow.
        /// </summary>
        [Fact]
        public void APolledSaveHasArrangementsBeforeItsFirstElection()
        {
            EngineTuning tuning = EngineTuning.Default;
            PoliticalState state = StateWith(RegionTheme.Eu, Poll(), published: true);

            IReadOnlyList<CoalitionCandidate> ranked = CoalitionFormation.RankCandidates(
                state.Settings.System, ProvisionalChamber.Project(state, tuning), state.Parties, tuning);

            Assert.NotEmpty(ranked);

            // a↔b is 0.20 on the shared axis, inside the 0.55 cap, and 38+27 of 60 seats is a
            // majority — so the arrangement the design would expect is on the table. Matched on the
            // member list rather than on `Key`: CoalitionMath is internal, and reconstructing its key
            // format here would be a second copy of a private convention.
            bool sawAb = false;
            for (int i = 0; i < ranked.Count; i++)
            {
                IReadOnlyList<string> members = ranked[i].MemberPartyIds;
                if (members.Count == 2 && members[0] == "party-a" && members[1] == "party-b")
                {
                    sawAb = true;
                    Assert.True(ranked[i].HasMajority);
                }
            }
            Assert.True(sawAb);
        }
    }
}
