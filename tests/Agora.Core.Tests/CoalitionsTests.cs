using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Engine.Government.Coalitions;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Packet 9 — coalition formation, stability and collapse.
    ///
    /// <para>
    /// Fixtures put every party on a single ideological axis: <see cref="Pos"/> sets all six issues to
    /// the same value, so <c>IssuePosition.Distance</c> between two parties is exactly half the gap
    /// between their axis values. That makes every expected distance in this file checkable by hand
    /// against <c>data/engine_tuning.json</c> rather than by running the code and pasting the answer.
    /// </para>
    /// </summary>
    public class CoalitionsTests
    {
        private static readonly Guid SaveA = new Guid("11111111-2222-3333-4444-555555555555");
        private static readonly SimDate ElectionDay = new SimDate(1994, 6, 5);
        private static readonly SimDate MidTerm = new SimDate(1996, 3, 10);

        // ---------------------------------------------------------------- fixtures

        /// <summary>All six issues at the same stance, so distance is half the axis gap.</summary>
        private static IssuePosition Pos(double axis) => new IssuePosition(axis, axis, axis, axis, axis, axis);

        private static Party MakeParty(string id, double axis, PartyStatus status = PartyStatus.Active) =>
            new Party { Id = id, Name = id, Platform = Pos(axis), Status = status };

        private static SeatAllocation Alloc(string id, int seats, int total) =>
            new SeatAllocation(id, seats, total <= 0 ? 0.0 : (double)seats / total, 0.0, 0, seats, true);

        /// <summary>
        /// A spread chamber of 100 seats. Axis: a −0.8 (35), b −0.4 (25), c +0.4 (25), d +0.9 (15).
        /// a↔b = 0.20, b↔c = 0.40, c↔d = 0.25 are all inside the 0.55 cap; a↔c = 0.60, b↔d = 0.65 and
        /// a↔d = 0.85 are all outside it. party-d holds 15% and so can never lead.
        /// </summary>
        private static void SpreadChamber(out List<SeatAllocation> seats, out List<Party> parties)
        {
            seats = new List<SeatAllocation>
            {
                Alloc("party-a", 35, 100),
                Alloc("party-b", 25, 100),
                Alloc("party-c", 25, 100),
                Alloc("party-d", 15, 100)
            };

            parties = new List<Party>
            {
                MakeParty("party-a", -0.8),
                MakeParty("party-b", -0.4),
                MakeParty("party-c", 0.4),
                MakeParty("party-d", 0.9)
            };
        }

        /// <summary>
        /// A consensual chamber of 100 seats where every pair is inside the cap. Axis: a −0.2 (40),
        /// b −0.1 (30), c 0.0 (20), d +0.1 (10). Used for the minimum-winning rule.
        /// </summary>
        private static void CloseChamber(out List<SeatAllocation> seats, out List<Party> parties)
        {
            seats = new List<SeatAllocation>
            {
                Alloc("party-a", 40, 100),
                Alloc("party-b", 30, 100),
                Alloc("party-c", 20, 100),
                Alloc("party-d", 10, 100)
            };

            parties = new List<Party>
            {
                MakeParty("party-a", -0.2),
                MakeParty("party-b", -0.1),
                MakeParty("party-c", 0.0),
                MakeParty("party-d", 0.1)
            };
        }

        private static EngineTuning Tuned(string coalitionOverrides) =>
            EngineTuning.FromJson("{\"coalitions\":" + coalitionOverrides + "}");

        // ---------------------------------------------------------------- hashing

        private static string Signature(CoalitionFormationResult r)
        {
            var sb = new StringBuilder();
            sb.Append("succeeded=").Append(r.Succeeded).Append('|');
            sb.Append("attempts=").Append(r.Attempts).Append('|');
            sb.Append("slack=").Append(r.UsedGrandCoalitionSlack).Append('|');
            sb.Append("snap=").Append(r.SnapElectionDate.HasValue ? r.SnapElectionDate.Value.ToString() : "-").Append('|');

            for (int i = 0; i < r.RankedCandidates.Count; i++)
            {
                CoalitionCandidate c = r.RankedCandidates[i];
                sb.Append(c.Key).Append('/').Append(c.LeadPartyId).Append('/')
                  .Append(c.Seats).Append('/').Append(F(c.Score)).Append('/')
                  .Append(c.HasMajority).Append('/').Append(c.IsMinimumWinning).Append(';');
            }

            sb.Append('|').Append(Signature(r.Government));
            return sb.ToString();
        }

        private static string Signature(Coalition? c)
        {
            if (c == null) return "none";

            var sb = new StringBuilder();
            sb.Append(c.SchemaVersion).Append('|').Append(c.Id).Append('|').Append(c.FormedDate).Append('|');
            sb.Append(c.EndedDate.HasValue ? c.EndedDate.Value.ToString() : "-").Append('|');
            sb.Append(string.Join("+", c.MemberPartyIds)).Append('|').Append(c.LeadPartyId).Append('|');
            sb.Append(string.Join("+", c.OppositionPartyIds)).Append('|');
            sb.Append(c.Seats).Append('|').Append(F(c.SeatShare)).Append('|').Append(c.HasMajority).Append('|');
            sb.Append(F(c.Cohesion)).Append('|').Append(F(c.Stability)).Append('|');
            sb.Append(c.Status).Append('|').Append(c.CollapseReason).Append('|');
            sb.Append(c.FormationAttempts).Append('|').Append(c.ElectionId);
            return sb.ToString();
        }

        private static string Signature(CoalitionTickResult r)
        {
            var sb = new StringBuilder();
            sb.Append(F(r.Stability)).Append('|').Append(F(r.StabilityDelta)).Append('|').Append(F(r.Cohesion)).Append('|');
            sb.Append(r.Seats).Append('|').Append(F(r.SeatShare)).Append('|').Append(r.HasMajority).Append('|');
            sb.Append(string.Join("+", r.MemberPartyIds)).Append('|');
            sb.Append(string.Join("+", r.OppositionPartyIds)).Append('|');
            sb.Append(string.Join("+", r.WithdrawnPartyIds)).Append('|').Append(r.LeadPartyId).Append('|');
            sb.Append(r.Status).Append('|').Append(r.CollapseReason).Append('|');
            sb.Append(r.EndedDate.HasValue ? r.EndedDate.Value.ToString() : "-").Append('|');
            sb.Append(r.SnapElectionDate.HasValue ? r.SnapElectionDate.Value.ToString() : "-").Append('|');
            sb.Append(F(r.DecayComponent)).Append('|').Append(F(r.MandateShockComponent)).Append('|');
            sb.Append(F(r.MandateRecoveryComponent)).Append('|').Append(F(r.EventShockComponent)).Append('|');
            sb.Append(F(r.MinorityTransitionComponent)).Append('|').Append(F(r.MaxPairwiseDistance));
            return sb.ToString();
        }

        private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static string Hash(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(digest.Length * 2);
                for (int i = 0; i < digest.Length; i++) sb.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        // ================================================================ formation

        [Fact]
        public void Form_ProducesIdenticalResultTwice()
        {
            List<SeatAllocation> seats;
            List<Party> parties;
            SpreadChamber(out seats, out parties);

            CoalitionFormationResult first = CoalitionFormation.Form(
                SaveA, ElectionDay, "election-1994-06", ElectoralSystem.Proportional,
                seats, parties, null, EngineTuning.Default);

            CoalitionFormationResult second = CoalitionFormation.Form(
                SaveA, ElectionDay, "election-1994-06", ElectoralSystem.Proportional,
                seats, parties, null, EngineTuning.Default);

            Assert.Equal(Hash(Signature(first)), Hash(Signature(second)));
        }

        [Fact]
        public void Form_IsIndependentOfInputOrder()
        {
            List<SeatAllocation> seats;
            List<Party> parties;
            SpreadChamber(out seats, out parties);

            var reversedSeats = new List<SeatAllocation>(seats);
            reversedSeats.Reverse();
            var reversedParties = new List<Party>(parties);
            reversedParties.Reverse();

            CoalitionFormationResult inOrder = CoalitionFormation.Form(
                SaveA, ElectionDay, "election-1994-06", ElectoralSystem.Proportional,
                seats, parties, null, EngineTuning.Default);

            CoalitionFormationResult reversed = CoalitionFormation.Form(
                SaveA, ElectionDay, "election-1994-06", ElectoralSystem.Proportional,
                reversedSeats, reversedParties, null, EngineTuning.Default);

            // The whole packet sorts explicitly; if any enumeration leaked, this is where it shows.
            Assert.Equal(Hash(Signature(inOrder)), Hash(Signature(reversed)));
        }

        [Fact]
        public void Form_DependsOnTheSaveGuid()
        {
            List<SeatAllocation> seats;
            List<Party> parties;
            SpreadChamber(out seats, out parties);

            var outcomes = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 64; i++)
            {
                var guid = new Guid(i + 1, 0, 0, new byte[8]);
                CoalitionFormationResult r = CoalitionFormation.Form(
                    guid, ElectionDay, "election-1994-06", ElectoralSystem.Proportional,
                    seats, parties, null, EngineTuning.Default);

                outcomes.Add(Signature(r.Government));
            }

            // Talks succeed with probability equal to cohesion (0.65 for the top arrangement here), so
            // different saves must reach different governments. A constant result would pass the
            // determinism test above perfectly and be worthless.
            Assert.True(outcomes.Count > 1,
                "Formation produced the same government for all 64 save guids; the seeded draw is not wired in.");
        }

        [Fact]
        public void Form_RanksIdeologicallyClosestMajorityFirst()
        {
            List<SeatAllocation> seats;
            List<Party> parties;
            SpreadChamber(out seats, out parties);

            CoalitionFormationResult r = CoalitionFormation.Form(
                SaveA, ElectionDay, "election-1994-06", ElectoralSystem.Proportional,
                seats, parties, null, EngineTuning.Default);

            // a+b: 60 seats, mean distance 0.20 → 0.6*0.80 + 0.4*0.60 = 0.72
            // b+c: 50 seats, mean distance 0.40 → 0.6*0.60 + 0.4*0.50 = 0.56
            Assert.Equal("party-a+party-b", r.RankedCandidates[0].Key);
            Assert.Equal("party-b+party-c", r.RankedCandidates[1].Key);
            Assert.Equal(0.72, r.RankedCandidates[0].Score, 10);
            Assert.Equal(0.56, r.RankedCandidates[1].Score, 10);
            Assert.Equal(0.65, r.RankedCandidates[0].Cohesion, 10); // 0.75 - 0.5 * 0.20
        }

        [Fact]
        public void Form_RejectsPartnersBeyondTheDistanceCap()
        {
            List<SeatAllocation> seats;
            List<Party> parties;
            SpreadChamber(out seats, out parties);

            CoalitionFormationResult r = CoalitionFormation.Form(
                SaveA, ElectionDay, "election-1994-06", ElectoralSystem.Proportional,
                seats, parties, null, EngineTuning.Default);

            for (int i = 0; i < r.RankedCandidates.Count; i++)
            {
                CoalitionCandidate c = r.RankedCandidates[i];
                Assert.True(c.MaxPairwiseDistance <= c.DistanceCap,
                    c.Key + " sits together at distance " + F(c.MaxPairwiseDistance) + " over cap " + F(c.DistanceCap));

                bool hasA = c.MemberPartyIds.Contains("party-a");
                Assert.False(hasA && c.MemberPartyIds.Contains("party-c"), "a↔c is 0.60, past the 0.55 cap");
                Assert.False(hasA && c.MemberPartyIds.Contains("party-d"), "a↔d is 0.85, past the 0.55 cap");
                Assert.False(c.MemberPartyIds.Contains("party-b") && c.MemberPartyIds.Contains("party-d"),
                    "b↔d is 0.65, past the 0.55 cap");
            }
        }

        [Fact]
        public void Form_RejectsALeadPartyBelowTheMinimumSeatShare()
        {
            List<SeatAllocation> seats;
            List<Party> parties;
            SpreadChamber(out seats, out parties);

            CoalitionFormationResult r = CoalitionFormation.Form(
                SaveA, ElectionDay, "election-1994-06", ElectoralSystem.Proportional,
                seats, parties, null, EngineTuning.Default);

            for (int i = 0; i < r.RankedCandidates.Count; i++)
            {
                // party-d holds 15% and leadPartyMinSeatShare is 0.20, so it can never lead — and it
                // is the only member of a one-party "party-d" arrangement, which must not exist.
                Assert.NotEqual("party-d", r.RankedCandidates[i].LeadPartyId);
                Assert.NotEqual("party-d", r.RankedCandidates[i].Key);
            }
        }

        [Fact]
        public void Form_FlagsRedundantPartnersAsNotMinimumWinning()
        {
            List<SeatAllocation> seats;
            List<Party> parties;
            CloseChamber(out seats, out parties);

            CoalitionFormationResult r = CoalitionFormation.Form(
                SaveA, ElectionDay, "election-1994-06", ElectoralSystem.Proportional,
                seats, parties, null, EngineTuning.Default);

            CoalitionCandidate ab = Find(r, "party-a+party-b");
            CoalitionCandidate abc = Find(r, "party-a+party-b+party-c");

            Assert.True(ab.HasMajority);   // 70 seats
            Assert.True(abc.HasMajority);  // 90 seats
            Assert.True(ab.IsMinimumWinning);
            Assert.False(abc.IsMinimumWinning); // drop party-c and a+b still governs

            // And the ranking honours it: every minimum-winning majority precedes every bloated one.
            bool seenBloated = false;
            for (int i = 0; i < r.RankedCandidates.Count; i++)
            {
                CoalitionCandidate c = r.RankedCandidates[i];
                if (!c.HasMajority) break;
                if (!c.IsMinimumWinning) seenBloated = true;
                else Assert.False(seenBloated, "minimum-winning " + c.Key + " ranked below a bloated arrangement");
            }

            Assert.Equal("party-a+party-b", r.RankedCandidates[0].Key);
        }

        private static CoalitionCandidate Find(CoalitionFormationResult r, string key)
        {
            for (int i = 0; i < r.RankedCandidates.Count; i++)
            {
                if (r.RankedCandidates[i].Key == key) return r.RankedCandidates[i];
            }

            throw new Xunit.Sdk.XunitException("No candidate with key " + key);
        }

        [Fact]
        public void Form_GrantsGrandCoalitionSlackOnlyWhenNothingElseWorks()
        {
            // Two-thirds needed to govern. a −0.55 (45), b +0.60 (40), c +0.65 (15).
            // a↔b = 0.575 — past the 0.55 cap, inside 0.55 + 0.10 of grand-coalition slack.
            // a↔c = 0.60 is past the cap and gets no slack (not the two largest).
            var seats = new List<SeatAllocation>
            {
                Alloc("party-a", 45, 100),
                Alloc("party-b", 40, 100),
                Alloc("party-c", 15, 100)
            };
            var parties = new List<Party>
            {
                MakeParty("party-a", -0.55),
                MakeParty("party-b", 0.60),
                MakeParty("party-c", 0.65)
            };

            EngineTuning tuning = Tuned("{\"minSeatShareToGovern\":0.6}");

            CoalitionFormationResult r = CoalitionFormation.Form(
                SaveA, ElectionDay, "election-1994-06", ElectoralSystem.Proportional,
                seats, parties, null, tuning);

            Assert.True(r.UsedGrandCoalitionSlack);

            CoalitionCandidate grand = Find(r, "party-a+party-b");
            Assert.True(grand.IsGrandCoalition);
            Assert.Equal(0.65, grand.DistanceCap, 10);
            Assert.Equal(0.575, grand.MaxPairwiseDistance, 10);
            Assert.True(grand.HasMajority);

            // Without the slack there is no majority at all, so the strict cap alone would have sent
            // the city back to the polls.
            for (int i = 0; i < r.RankedCandidates.Count; i++)
            {
                CoalitionCandidate c = r.RankedCandidates[i];
                if (c.HasMajority) Assert.Equal("party-a+party-b", c.Key);
            }
        }

        [Fact]
        public void Form_FallsBackToAMinorityGovernment()
        {
            List<SeatAllocation> seats;
            List<Party> parties;
            SpreadChamber(out seats, out parties);

            // One partner maximum: nobody reaches 50%, so the largest party governs alone.
            EngineTuning tuning = Tuned("{\"formationMaxPartners\":1}");

            CoalitionFormationResult r = CoalitionFormation.Form(
                SaveA, ElectionDay, "election-1994-06", ElectoralSystem.Proportional,
                seats, parties, null, tuning);

            Assert.True(r.Succeeded);
            Assert.True(r.IsMinority);
            Assert.NotNull(r.Government);
            Assert.Equal(CoalitionStatus.Minority, r.Government!.Status);
            Assert.Equal(new List<string> { "party-a" }, r.Government.MemberPartyIds);
            Assert.Equal(new List<string> { "party-b", "party-c", "party-d" }, r.Government.OppositionPartyIds);
            Assert.False(r.Government.HasMajority);
            Assert.Equal(35, r.Government.Seats);
            Assert.Equal(0.35, r.Government.SeatShare, 10);

            // stabilityInitial 0.80, minus the 25% minority-government penalty.
            Assert.Equal(0.60, r.Government.Stability, 10);
            Assert.Null(r.SnapElectionDate);
        }

        [Fact]
        public void Form_FailsToASnapElectionWhenNoGovernmentIsPossible()
        {
            List<SeatAllocation> seats;
            List<Party> parties;
            SpreadChamber(out seats, out parties);

            EngineTuning tuning = Tuned("{\"formationMaxPartners\":1,\"minorityGovernmentAllowed\":false}");

            CoalitionFormationResult r = CoalitionFormation.Form(
                SaveA, ElectionDay, "election-1994-06", ElectoralSystem.Proportional,
                seats, parties, null, tuning);

            Assert.False(r.Succeeded);
            Assert.Null(r.Government);
            Assert.Equal(new SimDate(1994, 9, 5), r.SnapElectionDate); // formationWindowMonths = 3
        }

        [Fact]
        public void Form_NeverExceedsTheAttemptBudget()
        {
            List<SeatAllocation> seats;
            List<Party> parties;
            CloseChamber(out seats, out parties);

            for (int i = 0; i < 64; i++)
            {
                var guid = new Guid(i + 1, 0, 0, new byte[8]);
                CoalitionFormationResult r = CoalitionFormation.Form(
                    guid, ElectionDay, "election-1994-06", ElectoralSystem.Proportional,
                    seats, parties, null, EngineTuning.Default);

                Assert.InRange(r.Attempts, 1, 3); // formationAttemptsMax = 3
                if (r.Government != null) Assert.InRange(r.Government.FormationAttempts, 1, 3);
            }
        }

        [Fact]
        public void Form_UnderFptp_GivesTheMayorsPartyASinglePartyGovernment()
        {
            List<SeatAllocation> seats;
            List<Party> parties;
            SpreadChamber(out seats, out parties);

            CoalitionFormationResult r = CoalitionFormation.Form(
                SaveA, ElectionDay, "election-1994-06", ElectoralSystem.FirstPastThePost,
                seats, parties, "party-c", EngineTuning.Default);

            Assert.True(r.Succeeded);
            Assert.NotNull(r.Government);
            Assert.Equal("party-c", r.Government!.LeadPartyId);
            Assert.Equal(new List<string> { "party-c" }, r.Government.MemberPartyIds);
            Assert.Equal(new List<string> { "party-a", "party-b", "party-d" }, r.Government.OppositionPartyIds);

            // 25 of 100 seats: a minority administration, and it pays the penalty.
            Assert.Equal(CoalitionStatus.Minority, r.Government.Status);
            Assert.Equal(0.60, r.Government.Stability, 10);
            Assert.Equal(1, r.Government.FormationAttempts);
            Assert.Equal("gov-1994-06", r.Government.Id);
        }

        [Fact]
        public void Form_UnderFptp_WithAMajorityGovernsAtFullStability()
        {
            var seats = new List<SeatAllocation>
            {
                Alloc("party-a", 60, 100),
                Alloc("party-b", 40, 100)
            };
            var parties = new List<Party> { MakeParty("party-a", -0.5), MakeParty("party-b", 0.5) };

            CoalitionFormationResult r = CoalitionFormation.Form(
                SaveA, ElectionDay, "election-1994-06", ElectoralSystem.FirstPastThePost,
                seats, parties, "party-a", EngineTuning.Default);

            Assert.NotNull(r.Government);
            Assert.Equal(CoalitionStatus.Governing, r.Government!.Status);
            Assert.True(r.Government.HasMajority);
            Assert.Equal(0.80, r.Government.Stability, 10);
            Assert.Equal(0.75, r.Government.Cohesion, 10); // one party, no partners: cohesionBase
        }

        [Fact]
        public void Form_IgnoresDissolvedAndMergedParties()
        {
            List<SeatAllocation> seats;
            List<Party> parties;
            SpreadChamber(out seats, out parties);
            parties[1] = MakeParty("party-b", -0.4, PartyStatus.Dissolved);

            CoalitionFormationResult r = CoalitionFormation.Form(
                SaveA, ElectionDay, "election-1994-06", ElectoralSystem.Proportional,
                seats, parties, null, EngineTuning.Default);

            for (int i = 0; i < r.RankedCandidates.Count; i++)
            {
                Assert.DoesNotContain("party-b", r.RankedCandidates[i].MemberPartyIds);
            }
        }

        [Fact]
        public void Form_OnAnEmptyChamber_GoesBackToTheVoters()
        {
            CoalitionFormationResult r = CoalitionFormation.Form(
                SaveA, ElectionDay, "election-1994-06", ElectoralSystem.Proportional,
                new List<SeatAllocation>(), new List<Party>(), null, EngineTuning.Default);

            Assert.False(r.Succeeded);
            Assert.Equal(new SimDate(1994, 9, 5), r.SnapElectionDate);
            Assert.Empty(r.RankedCandidates);
        }

        // ================================================================ stability

        private static Coalition Governing(double stability = 0.8)
        {
            return new Coalition
            {
                Id = "gov-1994-06",
                FormedDate = ElectionDay,
                MemberPartyIds = new List<string> { "party-a", "party-b" },
                LeadPartyId = "party-a",
                OppositionPartyIds = new List<string> { "party-c", "party-d" },
                Seats = 60,
                SeatShare = 0.60,
                HasMajority = true,
                Cohesion = 0.65,
                Stability = stability,
                Status = CoalitionStatus.Governing,
                FormationAttempts = 1,
                ElectionId = "election-1994-06"
            };
        }

        private static CoalitionTickInputs Quiet()
        {
            List<SeatAllocation> seats;
            List<Party> parties;
            SpreadChamber(out seats, out parties);
            return new CoalitionTickInputs { MonthsElapsed = 1, Seats = seats, Parties = parties };
        }

        [Fact]
        public void Advance_ProducesIdenticalResultTwice()
        {
            CoalitionTickInputs inputs = Quiet();
            inputs.FailedMandates = 1;
            inputs.FulfilledMandates = 2;
            inputs.EventSeverities = new List<int> { 2, 4, 5 };

            CoalitionTickResult first = CoalitionStability.Advance(Governing(), inputs, SaveA, MidTerm, EngineTuning.Default);
            CoalitionTickResult second = CoalitionStability.Advance(Governing(), inputs, SaveA, MidTerm, EngineTuning.Default);

            Assert.Equal(Hash(Signature(first)), Hash(Signature(second)));
        }

        [Fact]
        public void Advance_DecaysStabilityEveryMonth()
        {
            CoalitionTickResult one = CoalitionStability.Advance(Governing(), Quiet(), SaveA, MidTerm, EngineTuning.Default);
            Assert.Equal(0.79, one.Stability, 10);        // stabilityDecayPerMonth = 0.01
            Assert.Equal(-0.01, one.StabilityDelta, 10);
            Assert.Equal(CoalitionStatus.Governing, one.Status);
            Assert.False(one.Ended);

            CoalitionTickInputs six = Quiet();
            six.MonthsElapsed = 6;
            CoalitionTickResult later = CoalitionStability.Advance(Governing(), six, SaveA, MidTerm, EngineTuning.Default);
            Assert.Equal(0.74, later.Stability, 10);
        }

        [Fact]
        public void Advance_DefiedMandatesShockAndFulfilledOnesHeal()
        {
            CoalitionTickInputs failed = Quiet();
            failed.FailedMandates = 2;
            CoalitionTickResult down = CoalitionStability.Advance(Governing(), failed, SaveA, MidTerm, EngineTuning.Default);
            Assert.Equal(0.80 - 0.01 - 0.16, down.Stability, 10); // 0.08 per failure
            Assert.Equal(-0.16, down.MandateShockComponent, 10);

            CoalitionTickInputs kept = Quiet();
            kept.FulfilledMandates = 3;
            CoalitionTickResult up = CoalitionStability.Advance(Governing(0.5), kept, SaveA, MidTerm, EngineTuning.Default);
            Assert.Equal(0.50 - 0.01 + 0.15, up.Stability, 10); // 0.05 per fulfilment
            Assert.Equal(0.15, up.MandateRecoveryComponent, 10);
        }

        [Fact]
        public void Advance_OnlyMajorEventsShockTheGovernment()
        {
            CoalitionTickInputs minor = Quiet();
            minor.EventSeverities = new List<int> { 1, 2, 3, 3 };
            CoalitionTickResult ignored = CoalitionStability.Advance(Governing(), minor, SaveA, MidTerm, EngineTuning.Default);
            Assert.Equal(0.0, ignored.EventShockComponent, 10); // majorSeverityThreshold = 4

            CoalitionTickInputs major = Quiet();
            major.EventSeverities = new List<int> { 3, 4, 5 };
            CoalitionTickResult shaken = CoalitionStability.Advance(Governing(), major, SaveA, MidTerm, EngineTuning.Default);
            Assert.Equal(-0.27, shaken.EventShockComponent, 10); // (4 + 5) * 0.03

            CoalitionTickInputs absurd = Quiet();
            absurd.EventSeverities = new List<int> { 99 };
            CoalitionTickResult clamped = CoalitionStability.Advance(Governing(), absurd, SaveA, MidTerm, EngineTuning.Default);
            Assert.Equal(-0.15, clamped.EventShockComponent, 10); // severityMax = 5
        }

        [Fact]
        public void Advance_KeepsStabilityInsideTheUnitInterval()
        {
            CoalitionTickInputs flood = Quiet();
            flood.FulfilledMandates = 1000;
            CoalitionTickResult ceiling = CoalitionStability.Advance(Governing(), flood, SaveA, MidTerm, EngineTuning.Default);
            Assert.Equal(1.0, ceiling.Stability, 10);

            CoalitionTickInputs disaster = Quiet();
            disaster.FailedMandates = 1000;
            CoalitionTickResult floor = CoalitionStability.Advance(Governing(), disaster, SaveA, MidTerm, EngineTuning.Default);
            Assert.Equal(0.0, floor.Stability, 10);
            Assert.True(floor.Collapsed);
        }

        [Fact]
        public void Advance_CollapsesBelowThreshold_AndSchedulesASnapElection()
        {
            CoalitionTickInputs inputs = Quiet();
            inputs.FailedMandates = 1;

            // 0.35 − 0.01 decay − 0.08 mandate = 0.26, under the 0.30 collapse threshold.
            CoalitionTickResult r = CoalitionStability.Advance(Governing(0.35), inputs, SaveA, MidTerm, EngineTuning.Default);

            Assert.True(r.Collapsed);
            Assert.Equal(CoalitionStatus.Collapsed, r.Status);
            Assert.Equal(CoalitionCollapseReason.MandateFailure, r.CollapseReason);
            Assert.Equal(MidTerm, r.EndedDate);
            Assert.Equal(new SimDate(1996, 6, 10), r.SnapElectionDate); // snapElectionDelayMonths = 3
        }

        [Fact]
        public void Advance_AttributesCollapseToTheDominantPressure()
        {
            CoalitionTickResult decayed = CoalitionStability.Advance(
                Governing(0.305), Quiet(), SaveA, MidTerm, EngineTuning.Default);
            Assert.True(decayed.Collapsed);
            Assert.Equal(CoalitionCollapseReason.StabilityDecay, decayed.CollapseReason);

            CoalitionTickInputs shock = Quiet();
            shock.EventSeverities = new List<int> { 5, 5 };
            CoalitionTickResult shaken = CoalitionStability.Advance(
                Governing(0.5), shock, SaveA, MidTerm, EngineTuning.Default);
            Assert.True(shaken.Collapsed);
            Assert.Equal(CoalitionCollapseReason.EventShock, shaken.CollapseReason);
        }

        [Fact]
        public void Advance_HoldsAboveThreshold()
        {
            CoalitionTickResult r = CoalitionStability.Advance(
                Governing(0.32), Quiet(), SaveA, MidTerm, EngineTuning.Default);

            Assert.Equal(0.31, r.Stability, 10);
            Assert.False(r.Ended);
            Assert.Equal(CoalitionCollapseReason.None, r.CollapseReason);
            Assert.Null(r.SnapElectionDate);
        }

        [Fact]
        public void Advance_PartnerWalkout_DropsToMinorityWhenAllowed()
        {
            CoalitionTickInputs inputs = Quiet();
            inputs.WithdrawnPartyIds = new List<string> { "party-b" };

            CoalitionTickResult r = CoalitionStability.Advance(Governing(), inputs, SaveA, MidTerm, EngineTuning.Default);

            Assert.Equal(new List<string> { "party-a" }, r.MemberPartyIds);
            Assert.Equal(new List<string> { "party-b" }, r.WithdrawnPartyIds);
            Assert.Equal(new List<string> { "party-b", "party-c", "party-d" }, r.OppositionPartyIds);
            Assert.Equal(35, r.Seats);
            Assert.False(r.HasMajority);
            Assert.Equal(CoalitionStatus.Minority, r.Status);
            Assert.False(r.Collapsed);

            // 0.80 − 0.01 decay = 0.79, then the 25% minority penalty once.
            Assert.Equal(0.79 * 0.75, r.Stability, 10);
            Assert.Equal(0.79 * 0.75 - 0.79, r.MinorityTransitionComponent, 10);
        }

        [Fact]
        public void Advance_PartnerWalkout_CollapsesWhenMinorityGovernmentIsBanned()
        {
            CoalitionTickInputs inputs = Quiet();
            inputs.WithdrawnPartyIds = new List<string> { "party-b" };

            CoalitionTickResult r = CoalitionStability.Advance(
                Governing(), inputs, SaveA, MidTerm, Tuned("{\"minorityGovernmentAllowed\":false}"));

            Assert.True(r.Collapsed);
            Assert.Equal(CoalitionCollapseReason.PartnerWithdrawal, r.CollapseReason);
            Assert.Equal(new SimDate(1996, 6, 10), r.SnapElectionDate);
        }

        [Fact]
        public void Advance_CollapsesWhenTheLeadPartyWalksOut()
        {
            CoalitionTickInputs inputs = Quiet();
            inputs.WithdrawnPartyIds = new List<string> { "party-a" };

            CoalitionTickResult r = CoalitionStability.Advance(Governing(), inputs, SaveA, MidTerm, EngineTuning.Default);

            Assert.True(r.Collapsed);
            Assert.Equal(CoalitionCollapseReason.PartnerWithdrawal, r.CollapseReason);
        }

        [Fact]
        public void Advance_CollapsesOnMaximalIdeologicalDrift()
        {
            // Partners at the two extremes: distance 1.0 against a cap of 0.55 + 0.10 grand-coalition
            // slack, so the walk-out hazard saturates at 1.0 and every save collapses.
            var seats = new List<SeatAllocation> { Alloc("party-a", 60, 100), Alloc("party-b", 40, 100) };
            var parties = new List<Party> { MakeParty("party-a", -1.0), MakeParty("party-b", 1.0) };

            for (int i = 0; i < 8; i++)
            {
                var guid = new Guid(i + 1, 0, 0, new byte[8]);
                CoalitionTickResult r = CoalitionStability.Advance(
                    Governing(), new CoalitionTickInputs { Seats = seats, Parties = parties },
                    guid, MidTerm, EngineTuning.Default);

                Assert.Equal(1.0, r.MaxPairwiseDistance, 10);
                Assert.True(r.Collapsed, "drift at maximum distance must always end the government");
                Assert.Equal(CoalitionCollapseReason.IdeologicalDrift, r.CollapseReason);
                Assert.Equal(new SimDate(1996, 6, 10), r.SnapElectionDate);
            }
        }

        [Fact]
        public void Advance_DoesNotCollapseOnDriftInsideTheCap()
        {
            List<SeatAllocation> seats;
            List<Party> parties;
            SpreadChamber(out seats, out parties); // a↔b = 0.20

            for (int i = 0; i < 8; i++)
            {
                var guid = new Guid(i + 1, 0, 0, new byte[8]);
                CoalitionTickResult r = CoalitionStability.Advance(
                    Governing(), new CoalitionTickInputs { Seats = seats, Parties = parties },
                    guid, MidTerm, EngineTuning.Default);

                Assert.Equal(0.20, r.MaxPairwiseDistance, 10);
                Assert.False(r.Ended);
                Assert.Equal(CoalitionCollapseReason.None, r.CollapseReason);
            }
        }

        [Fact]
        public void Advance_RecomputesCohesionFromCurrentPlatforms()
        {
            List<SeatAllocation> seats;
            List<Party> parties;
            SpreadChamber(out seats, out parties);
            parties[1] = MakeParty("party-b", 0.2); // a −0.8 ↔ b +0.2 = 0.50, still inside the cap

            CoalitionTickResult r = CoalitionStability.Advance(
                Governing(), new CoalitionTickInputs { Seats = seats, Parties = parties },
                SaveA, MidTerm, EngineTuning.Default);

            Assert.Equal(0.50, r.MaxPairwiseDistance, 10);
            Assert.Equal(0.75 - 0.5 * 0.50, r.Cohesion, 10);
            Assert.False(r.Ended);
        }

        [Fact]
        public void Advance_ExpiresAtTermEndWithoutASnapElection()
        {
            CoalitionTickInputs inputs = Quiet();
            inputs.TermExpired = true;

            CoalitionTickResult r = CoalitionStability.Advance(Governing(0.31), inputs, SaveA, MidTerm, EngineTuning.Default);

            Assert.Equal(CoalitionStatus.Expired, r.Status);
            Assert.True(r.Ended);
            Assert.False(r.Collapsed);
            Assert.Equal(MidTerm, r.EndedDate);
            Assert.Null(r.SnapElectionDate); // the scheduled election is already on the calendar
        }

        [Fact]
        public void Advance_OnAnEndedGovernment_IsANoOp()
        {
            var ended = Governing();
            ended.Status = CoalitionStatus.Collapsed;
            ended.CollapseReason = CoalitionCollapseReason.StabilityDecay;
            ended.Stability = 0.2;

            CoalitionTickResult r = CoalitionStability.Advance(ended, Quiet(), SaveA, MidTerm, EngineTuning.Default);

            Assert.Equal(0.2, r.Stability, 10);
            Assert.Equal(0.0, r.StabilityDelta, 10);
            Assert.Equal(CoalitionStatus.Collapsed, r.Status);
            Assert.Null(r.SnapElectionDate);
        }

        [Fact]
        public void ApplyTo_WritesTheTickBackOntoTheGovernment()
        {
            var government = Governing();
            CoalitionTickInputs inputs = Quiet();
            inputs.WithdrawnPartyIds = new List<string> { "party-b" };

            CoalitionTickResult r = CoalitionStability.Advance(government, inputs, SaveA, MidTerm, EngineTuning.Default);

            // Advance itself must not mutate.
            Assert.Equal(0.8, government.Stability, 10);
            Assert.Equal(2, government.MemberPartyIds.Count);

            r.ApplyTo(government);

            Assert.Equal(r.Stability, government.Stability, 10);
            Assert.Equal(new List<string> { "party-a" }, government.MemberPartyIds);
            Assert.Equal(CoalitionStatus.Minority, government.Status);
            Assert.Equal("gov-1994-06", government.Id);              // identity untouched
            Assert.Equal("election-1994-06", government.ElectionId); // provenance untouched
        }

        [Fact]
        public void Advance_KeepsIdListsSorted()
        {
            var government = Governing();
            government.MemberPartyIds = new List<string> { "party-b", "party-a" };

            CoalitionTickResult r = CoalitionStability.Advance(government, Quiet(), SaveA, MidTerm, EngineTuning.Default);

            Assert.Equal(new List<string> { "party-a", "party-b" }, r.MemberPartyIds);
            Assert.Equal(new List<string> { "party-c", "party-d" }, r.OppositionPartyIds);
        }
    }
}
