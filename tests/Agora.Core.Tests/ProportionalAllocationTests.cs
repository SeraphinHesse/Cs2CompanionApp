using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Engine.Elections.Proportional;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Packet 7 — EU proportional seat allocation.
    ///
    /// <para>
    /// The golden seat counts below were derived independently of the implementation (by hand, then
    /// checked against a separate model of modified Sainte-Lague / d'Hondt / Hare). They are not
    /// "whatever the code printed": if one fails, the allocator changed, and the question is whether
    /// that was intended — not whether the constant needs updating.
    /// </para>
    /// </summary>
    public class ProportionalAllocationTests
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static readonly Guid SaveA = new Guid("11111111-2222-3333-4444-555555555555");
        private static readonly Guid SaveB = new Guid("99999999-8888-7777-6666-555555555555");
        private static readonly SimDate May1994 = new SimDate(1994, 5, 8);
        private static readonly SimDate May1997 = new SimDate(1997, 5, 8);
        private const string ElectionId = "election-1994-05";

        // -----------------------------------------------------------------------------------------
        // Fixtures
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// A tuning document with only the keys this packet reads. Built as JSON rather than by
        /// mutating <see cref="EngineTuning.Default"/> because the section setters are internal — and
        /// because going through the real parser means these tests would catch a key being renamed.
        /// </summary>
        private static EngineTuning Tuning(
            double thresholdShare = 0.05,
            string method = "sainte-lague",
            double firstDivisor = 1.4,
            int minSeatsForRepresentation = 0,
            int totalSeats = 60,
            double seatsPerPopulation = 0.0,
            int minSeats = 21,
            int maxSeats = 120,
            double districtSeatShare = 0.0)
        {
            var sb = new StringBuilder();
            sb.Append("{\"electionsPr\":{");
            sb.Append("\"termYears\":3,");
            sb.Append("\"totalSeats\":").Append(totalSeats.ToString(Inv)).Append(',');
            sb.Append("\"seatsPerPopulation\":").Append(seatsPerPopulation.ToString("R", Inv)).Append(',');
            sb.Append("\"minSeats\":").Append(minSeats.ToString(Inv)).Append(',');
            sb.Append("\"maxSeats\":").Append(maxSeats.ToString(Inv)).Append(',');
            sb.Append("\"thresholdShare\":").Append(thresholdShare.ToString("R", Inv)).Append(',');
            sb.Append("\"method\":\"").Append(method).Append("\",");
            sb.Append("\"firstDivisor\":").Append(firstDivisor.ToString("R", Inv)).Append(',');
            sb.Append("\"districtSeatShare\":").Append(districtSeatShare.ToString("R", Inv)).Append(',');
            sb.Append("\"minSeatsForRepresentation\":").Append(minSeatsForRepresentation.ToString(Inv));
            sb.Append("}}");
            return EngineTuning.FromJson(sb.ToString());
        }

        private static List<PartyVoteCount> Votes(params object[] pairs)
        {
            var list = new List<PartyVoteCount>();
            for (int i = 0; i + 1 < pairs.Length; i += 2)
                list.Add(new PartyVoteCount((string)pairs[i], (int)pairs[i + 1]));
            return list;
        }

        private static int Sum(SeatAllocationResult r)
        {
            int total = 0;
            foreach (SeatAllocation a in r.Seats) total += a.Seats;
            return total;
        }

        private static SeatAllocation Find(SeatAllocationResult r, string partyId)
        {
            foreach (SeatAllocation a in r.Seats)
            {
                if (string.Equals(a.PartyId, partyId, StringComparison.Ordinal)) return a;
            }
            throw new InvalidOperationException("No allocation for " + partyId);
        }

        /// <summary>SHA-256 of the canonical rendering. Hashing catches the field an assertion forgot.</summary>
        private static string Hash(SeatAllocationResult r)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(r.ToCanonicalString()));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes) sb.Append(b.ToString("x2", Inv));
                return sb.ToString();
            }
        }

        // -----------------------------------------------------------------------------------------
        // Determinism
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The canonical pattern. Run over a fixture that *contains an exact tie*, so the seeded
        /// stream is genuinely exercised — a determinism test on a tie-free fixture would pass even
        /// if the tie-break used <c>System.Random</c>.
        /// </summary>
        [Fact]
        public void Allocate_IsByteIdenticalAcrossRuns()
        {
            EngineTuning t = Tuning(thresholdShare: 0.0, minSeatsForRepresentation: 0);
            List<PartyVoteCount> v = Votes("p-alpha", 100, "p-beta", 100);

            string first = Hash(ProportionalAllocator.Allocate(v, 1, t, SaveA, May1994, ElectionId));
            string second = Hash(ProportionalAllocator.Allocate(v, 1, t, SaveA, May1994, ElectionId));

            Assert.Equal(first, second);
        }

        /// <summary>
        /// The negative half. Without it, an allocator that returned a constant would pass the
        /// determinism test perfectly.
        /// </summary>
        [Fact]
        public void Allocate_DiffersWhenVotesDiffer()
        {
            EngineTuning t = Tuning(thresholdShare: 0.0, minSeatsForRepresentation: 0);

            string a = Hash(ProportionalAllocator.Allocate(
                Votes("p-alpha", 100, "p-beta", 100), 5, t, SaveA, May1994, ElectionId));
            string b = Hash(ProportionalAllocator.Allocate(
                Votes("p-alpha", 180, "p-beta", 20), 5, t, SaveA, May1994, ElectionId));

            Assert.NotEqual(a, b);
        }

        /// <summary>Input order must not reach the output: the result sorts by party id ordinal.</summary>
        [Fact]
        public void Allocate_IsIndependentOfInputOrder()
        {
            EngineTuning t = Tuning(thresholdShare: 0.0, minSeatsForRepresentation: 0);

            string forward = Hash(ProportionalAllocator.Allocate(
                Votes("p-a", 100, "p-b", 80, "p-c", 30), 5, t, SaveA, May1994, ElectionId));
            string reversed = Hash(ProportionalAllocator.Allocate(
                Votes("p-c", 30, "p-b", 80, "p-a", 100), 5, t, SaveA, May1994, ElectionId));

            Assert.Equal(forward, reversed);
        }

        [Fact]
        public void Allocate_SortsSeatsByPartyIdOrdinal()
        {
            EngineTuning t = Tuning(thresholdShare: 0.0, minSeatsForRepresentation: 0);
            SeatAllocationResult r = ProportionalAllocator.Allocate(
                Votes("p-zeta", 10, "p-alpha", 100, "p-mu", 50), 5, t, SaveA, May1994, ElectionId);

            Assert.Equal(new[] { "p-alpha", "p-mu", "p-zeta" },
                         new[] { r.Seats[0].PartyId, r.Seats[1].PartyId, r.Seats[2].PartyId });
        }

        [Fact]
        public void Allocate_SumsDuplicatePartyIds()
        {
            EngineTuning t = Tuning(thresholdShare: 0.0, minSeatsForRepresentation: 0);
            SeatAllocationResult r = ProportionalAllocator.Allocate(
                Votes("p-a", 60, "p-b", 80, "p-a", 40), 5, t, SaveA, May1994, ElectionId);

            Assert.Equal(2, r.Seats.Count); // three ballot lines collapse to two parties
            Assert.Equal(180, r.TotalVotes);
            Assert.Equal(100.0, Find(r, "p-a").VoteShare * r.TotalVotes, 6);
            Assert.Equal(3, r.SeatsFor("p-a"));
            Assert.Equal(2, r.SeatsFor("p-b"));
        }

        // -----------------------------------------------------------------------------------------
        // The ratified method: modified Sainte-Lague, first divisor 1.4
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Golden. Votes 100 / 80 / 30 over 5 seats, divisors 1.4, 3, 5:
        /// a(71.43) b(57.14) a(33.33) b(26.67) c(21.43) → 2 / 2 / 1.
        /// </summary>
        [Fact]
        public void SainteLague_MatchesGoldenAllocation()
        {
            EngineTuning t = Tuning(thresholdShare: 0.0, minSeatsForRepresentation: 0);
            SeatAllocationResult r = ProportionalAllocator.Allocate(
                Votes("p-a", 100, "p-b", 80, "p-c", 30), 5, t, SaveA, May1994, ElectionId);

            Assert.Equal(ProportionalAllocator.MethodSainteLague, r.Method);
            Assert.Equal(2, r.SeatsFor("p-a"));
            Assert.Equal(2, r.SeatsFor("p-b"));
            Assert.Equal(1, r.SeatsFor("p-c"));
            Assert.Empty(r.TieBreaks);
        }

        /// <summary>
        /// D'Hondt on the same votes gives 3 / 2 / 0. That the two methods disagree is the point:
        /// it proves <c>electionsPr.method</c> is actually consulted rather than decorative.
        /// </summary>
        [Fact]
        public void DHondt_FavoursTheLargestParty()
        {
            EngineTuning t = Tuning(thresholdShare: 0.0, minSeatsForRepresentation: 0, method: "d-hondt");
            SeatAllocationResult r = ProportionalAllocator.Allocate(
                Votes("p-a", 100, "p-b", 80, "p-c", 30), 5, t, SaveA, May1994, ElectionId);

            Assert.Equal(ProportionalAllocator.MethodDHondt, r.Method);
            Assert.Equal(3, r.SeatsFor("p-a"));
            Assert.Equal(2, r.SeatsFor("p-b"));
            Assert.Equal(0, r.SeatsFor("p-c"));
        }

        /// <summary>Hare quota 210/5 = 42 → floors 2/1/0, remainders .381/.905/.714 → b then c.</summary>
        [Fact]
        public void LargestRemainder_MatchesGoldenAllocation()
        {
            EngineTuning t = Tuning(thresholdShare: 0.0, minSeatsForRepresentation: 0,
                                    method: "largest-remainder");
            SeatAllocationResult r = ProportionalAllocator.Allocate(
                Votes("p-a", 100, "p-b", 80, "p-c", 30), 5, t, SaveA, May1994, ElectionId);

            Assert.Equal(ProportionalAllocator.MethodLargestRemainder, r.Method);
            Assert.Equal(2, r.SeatsFor("p-a"));
            Assert.Equal(2, r.SeatsFor("p-b"));
            Assert.Equal(1, r.SeatsFor("p-c"));
        }

        [Fact]
        public void UnknownMethod_FallsBackToSainteLague()
        {
            EngineTuning weird = Tuning(thresholdShare: 0.0, minSeatsForRepresentation: 0,
                                        method: "borda-count");
            EngineTuning sl = Tuning(thresholdShare: 0.0, minSeatsForRepresentation: 0);
            List<PartyVoteCount> v = Votes("p-a", 100, "p-b", 80, "p-c", 30);

            SeatAllocationResult r = ProportionalAllocator.Allocate(v, 5, weird, SaveA, May1994, ElectionId);

            Assert.Equal(ProportionalAllocator.MethodSainteLague, r.Method);
            Assert.Equal(Hash(ProportionalAllocator.Allocate(v, 5, sl, SaveA, May1994, ElectionId)), Hash(r));
        }

        /// <summary>
        /// The whole reason the first divisor is 1.4 rather than 1. With 100 vs 25 over three seats,
        /// an unmodified sequence hands the small party the last seat (100/5 = 20 &lt; 25/1); the
        /// modified sequence prices its first seat at 25/1.4 = 17.9 and it gets nothing. If this test
        /// stops failing under firstDivisor = 1.0, the coefficient has stopped being read.
        /// </summary>
        [Fact]
        public void FirstDivisor_RaisesThePriceOfASmallPartysFirstSeat()
        {
            List<PartyVoteCount> v = Votes("p-a", 100, "p-b", 25);

            SeatAllocationResult modified = ProportionalAllocator.Allocate(
                v, 3, Tuning(thresholdShare: 0.0, minSeatsForRepresentation: 0, firstDivisor: 1.4),
                SaveA, May1994, ElectionId);
            SeatAllocationResult unmodified = ProportionalAllocator.Allocate(
                v, 3, Tuning(thresholdShare: 0.0, minSeatsForRepresentation: 0, firstDivisor: 1.0),
                SaveA, May1994, ElectionId);

            Assert.Equal(3, modified.SeatsFor("p-a"));
            Assert.Equal(0, modified.SeatsFor("p-b"));

            Assert.Equal(2, unmodified.SeatsFor("p-a"));
            Assert.Equal(1, unmodified.SeatsFor("p-b"));
        }

        // -----------------------------------------------------------------------------------------
        // Electoral threshold
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// 4.9% is out and 5.0% is in, on otherwise identical ballots. The boundary is inclusive:
        /// a party that polls exactly the threshold clears it.
        /// </summary>
        [Fact]
        public void Threshold_IsInclusiveAtExactlyFivePercent()
        {
            EngineTuning t = Tuning(thresholdShare: 0.05, minSeatsForRepresentation: 0);

            SeatAllocationResult at = ProportionalAllocator.Allocate(
                Votes("p-a", 600, "p-b", 350, "p-c", 50), 20, t, SaveA, May1994, ElectionId);
            SeatAllocationResult below = ProportionalAllocator.Allocate(
                Votes("p-a", 600, "p-b", 351, "p-c", 49), 20, t, SaveA, May1994, ElectionId);

            Assert.True(Find(at, "p-c").PassedThreshold);
            Assert.Equal(1, at.SeatsFor("p-c"));
            Assert.Equal(12, at.SeatsFor("p-a"));
            Assert.Equal(7, at.SeatsFor("p-b"));

            Assert.False(Find(below, "p-c").PassedThreshold);
            Assert.Equal(0, below.SeatsFor("p-c"));
            Assert.Equal(13, below.SeatsFor("p-a"));
            Assert.Equal(7, below.SeatsFor("p-b"));
        }

        /// <summary>An excluded party still appears in the result, with zero seats and its real vote share.</summary>
        [Fact]
        public void Threshold_KeepsExcludedPartiesInTheResult()
        {
            EngineTuning t = Tuning(thresholdShare: 0.05, minSeatsForRepresentation: 0);
            SeatAllocationResult r = ProportionalAllocator.Allocate(
                Votes("p-a", 700, "p-b", 260, "p-c", 40), 10, t, SaveA, May1994, ElectionId);

            SeatAllocation c = Find(r, "p-c");
            Assert.Equal(0, c.Seats);
            Assert.False(c.PassedThreshold);
            Assert.Equal(0.04, c.VoteShare, 10);
            Assert.Equal(new List<string> { "p-c" }, r.ExcludedPartyIds);
            Assert.Equal(960, r.QualifyingVotes);
            Assert.Equal(1000, r.TotalVotes);
            Assert.Equal(10, Sum(r));
        }

        /// <summary>
        /// If literally nobody clears the bar the allocator fails open rather than seating an empty
        /// chamber — government formation downstream has to have something to form from.
        /// </summary>
        [Fact]
        public void Threshold_IsWaivedWhenNoPartyClearsIt()
        {
            EngineTuning t = Tuning(thresholdShare: 0.99, minSeatsForRepresentation: 0);
            SeatAllocationResult r = ProportionalAllocator.Allocate(
                Votes("p-a", 900, "p-b", 50, "p-c", 50), 6, t, SaveA, May1994, ElectionId);

            Assert.True(r.ThresholdWaived);
            Assert.Equal(6, Sum(r));
            Assert.Equal(6, r.SeatsFor("p-a"));
            // The waiver seats them; it does not pretend they passed.
            Assert.False(Find(r, "p-a").PassedThreshold);
        }

        // -----------------------------------------------------------------------------------------
        // Guaranteed representation
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// <c>minSeatsForRepresentation</c> is what stops a party that cleared the threshold from
        /// being rounded out of the chamber entirely. With it off, the 2% party gets nothing.
        /// </summary>
        [Fact]
        public void MinSeatsForRepresentation_SeatsAQualifyingMinnow()
        {
            List<PartyVoteCount> v = Votes("p-a", 970, "p-b", 20, "p-c", 10);

            SeatAllocationResult on = ProportionalAllocator.Allocate(
                v, 3, Tuning(thresholdShare: 0.02, minSeatsForRepresentation: 1),
                SaveA, May1994, ElectionId);
            SeatAllocationResult off = ProportionalAllocator.Allocate(
                v, 3, Tuning(thresholdShare: 0.02, minSeatsForRepresentation: 0),
                SaveA, May1994, ElectionId);

            Assert.Equal(2, on.SeatsFor("p-a"));
            Assert.Equal(1, on.SeatsFor("p-b"));
            Assert.Equal(0, on.SeatsFor("p-c")); // 1% — below the threshold, so no guarantee

            Assert.Equal(3, off.SeatsFor("p-a"));
            Assert.Equal(0, off.SeatsFor("p-b"));
            Assert.Equal(3, Sum(on));
            Assert.Equal(3, Sum(off));
        }

        /// <summary>The guarantee can never overfill the chamber, however many parties qualify.</summary>
        [Fact]
        public void MinSeatsForRepresentation_NeverOverfillsTheChamber()
        {
            EngineTuning t = Tuning(thresholdShare: 0.0, minSeatsForRepresentation: 4);
            SeatAllocationResult r = ProportionalAllocator.Allocate(
                Votes("p-a", 50, "p-b", 40, "p-c", 30, "p-d", 20, "p-e", 10), 3,
                t, SaveA, May1994, ElectionId);

            // Five qualifying parties, a guarantee of four seats each, and a chamber of three: the
            // guarantee declines rather than overfilling, and the divisor method still fills exactly
            // three seats — strongest first, not alphabetically first.
            Assert.Equal(3, Sum(r));
            Assert.Equal(1, r.SeatsFor("p-a"));
            Assert.Equal(1, r.SeatsFor("p-b"));
            Assert.Equal(1, r.SeatsFor("p-c"));
            Assert.Equal(0, r.SeatsFor("p-d"));
            Assert.Equal(0, r.SeatsFor("p-e"));
        }

        // -----------------------------------------------------------------------------------------
        // Tie-breaking
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Golden. Pins the tie-break entity id and therefore the whole seed derivation for this
        /// packet: stream <c>election.tiebreak</c>, entity <c>election-1994-05#seat1</c>. If someone
        /// "harmlessly" renames the context format, every existing save's contested seats change
        /// hands — that is what this literal exists to catch.
        /// </summary>
        [Fact]
        public void TieBreak_MatchesGoldenDraw()
        {
            EngineTuning t = Tuning(thresholdShare: 0.0, minSeatsForRepresentation: 0);
            SeatAllocationResult r = ProportionalAllocator.Allocate(
                Votes("p-alpha", 100, "p-beta", 100), 1, t, SaveA, May1994, ElectionId);

            Assert.Single(r.TieBreaks);
            SeatTieBreak tie = r.TieBreaks[0];
            Assert.Equal(1, tie.SeatNumber);
            Assert.Equal("election-1994-05#seat1", tie.StreamContext);
            Assert.Equal(new[] { "p-alpha", "p-beta" }, new List<string>(tie.CandidatePartyIds));
            Assert.Equal("p-alpha", tie.WinningPartyId);
            Assert.Equal(1, r.SeatsFor("p-alpha"));
            Assert.Equal(0, r.SeatsFor("p-beta"));
        }

        /// <summary>
        /// The tie is decided by the seeded stream, not by list order. Sweeping the save guid must
        /// flip the winner — if it never does, the "coin flip" is really just <c>tied[0]</c>.
        /// </summary>
        [Fact]
        public void TieBreak_DependsOnTheSaveGuidNotOnListOrder()
        {
            EngineTuning t = Tuning(thresholdShare: 0.0, minSeatsForRepresentation: 0);
            List<PartyVoteCount> v = Votes("p-alpha", 100, "p-beta", 100);

            int alphaWins = 0, betaWins = 0;
            for (int i = 1; i <= 24; i++)
            {
                var guid = new Guid("00000000-0000-0000-0000-0000000000" + i.ToString("x2", Inv));
                SeatAllocationResult r = ProportionalAllocator.Allocate(v, 1, t, guid, May1994, ElectionId);
                if (r.SeatsFor("p-alpha") == 1) alphaWins++; else betaWins++;
            }

            Assert.True(alphaWins > 0, "the tie-break never picked p-alpha across 24 saves");
            Assert.True(betaWins > 0, "the tie-break never picked p-beta across 24 saves — it is not random");
            Assert.Equal(24, alphaWins + betaWins);
        }

        /// <summary>Same save, same date, same ballot → same winner. Save-scumming converges (§3).</summary>
        [Fact]
        public void TieBreak_IsStableForOneSaveAndDate()
        {
            EngineTuning t = Tuning(thresholdShare: 0.0, minSeatsForRepresentation: 0);
            List<PartyVoteCount> v = Votes("p-alpha", 100, "p-beta", 100);

            string first = ProportionalAllocator.Allocate(v, 1, t, SaveB, May1994, ElectionId)
                .TieBreaks[0].WinningPartyId;
            string second = ProportionalAllocator.Allocate(v, 1, t, SaveB, May1994, ElectionId)
                .TieBreaks[0].WinningPartyId;
            string later = ProportionalAllocator.Allocate(v, 1, t, SaveB, May1997, "election-1997-05")
                .TieBreaks[0].WinningPartyId;

            Assert.Equal(first, second);
            Assert.Contains(later, new[] { "p-alpha", "p-beta" });
        }

        /// <summary>
        /// Four parties on identical votes over four seats: three consecutive ties, and every party
        /// still ends on one seat whichever way the coin lands. Perfect proportionality, so the
        /// Gallagher index is exactly zero.
        /// </summary>
        [Fact]
        public void TieBreak_DoesNotChangeAPerfectlyProportionalOutcome()
        {
            EngineTuning t = Tuning(thresholdShare: 0.0, minSeatsForRepresentation: 0);
            SeatAllocationResult r = ProportionalAllocator.Allocate(
                Votes("p-a", 100, "p-b", 100, "p-c", 100, "p-d", 100), 4, t, SaveA, May1994, ElectionId);

            Assert.Equal(3, r.TieBreaks.Count);
            Assert.Equal(4, r.TieBreaks[0].CandidatePartyIds.Count);
            Assert.Equal(3, r.TieBreaks[1].CandidatePartyIds.Count);
            Assert.Equal(2, r.TieBreaks[2].CandidatePartyIds.Count);

            foreach (SeatAllocation a in r.Seats) Assert.Equal(1, a.Seats);
            Assert.Equal(0.0, r.Disproportionality, 12);
        }

        // -----------------------------------------------------------------------------------------
        // The shipped configuration
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Golden against the real <c>electionsPr</c> defaults — 60 seats, 5% threshold, modified
        /// Sainte-Lague at 1.4, one guaranteed seat. A six-party ballot where one party polls 3.995%.
        /// Vote totals are deliberately non-round so no quotient ties occur and the result is
        /// independent of the seed.
        /// </summary>
        [Fact]
        public void ShippedDefaults_ProduceAWholeChamber()
        {
            EngineTuning t = EngineTuning.Default;
            SeatAllocationResult r = ProportionalAllocator.Allocate(
                Votes("p-a", 38017, "p-b", 24983, "p-c", 15011,
                      "p-d", 11987, "p-e", 6007, "p-f", 3995),
                ProportionalAllocator.ChamberSize(0, t.ElectionsPr),
                t, SaveA, May1994, ElectionId);

            Assert.Equal(60, r.TotalSeats);
            Assert.Equal(60, Sum(r));
            Assert.Empty(r.TieBreaks);
            Assert.False(r.ThresholdWaived);

            Assert.Equal(24, r.SeatsFor("p-a"));
            Assert.Equal(16, r.SeatsFor("p-b"));
            Assert.Equal(9, r.SeatsFor("p-c"));
            Assert.Equal(7, r.SeatsFor("p-d"));
            Assert.Equal(4, r.SeatsFor("p-e"));
            Assert.Equal(0, r.SeatsFor("p-f"));

            // Every seated party is a list seat under the shipped pure-list configuration.
            foreach (SeatAllocation a in r.Seats)
            {
                Assert.Equal(0, a.DistrictSeats);
                Assert.Equal(a.Seats, a.ListSeats);
            }
        }

        [Theory]
        [InlineData("sainte-lague")]
        [InlineData("d-hondt")]
        [InlineData("largest-remainder")]
        public void Allocate_AlwaysFillsTheChamberExactly(string method)
        {
            EngineTuning t = Tuning(method: method, thresholdShare: 0.05, minSeatsForRepresentation: 1);

            int[][] ballots =
            {
                new[] { 5000, 3000, 1200, 800 },
                new[] { 9000, 500, 300, 200 },
                new[] { 2500, 2500, 2500, 2500 },
                new[] { 4000, 3000, 2000, 1000 },
                new[] { 10000, 0, 0, 0 },
            };

            foreach (int[] ballot in ballots)
            {
                for (int seats = 21; seats <= 60; seats += 13)
                {
                    SeatAllocationResult r = ProportionalAllocator.Allocate(
                        Votes("p-a", ballot[0], "p-b", ballot[1], "p-c", ballot[2], "p-d", ballot[3]),
                        seats, t, SaveA, May1994, ElectionId);

                    Assert.Equal(seats, Sum(r));
                }
            }
        }

        // -----------------------------------------------------------------------------------------
        // Chamber size
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void ChamberSize_UsesTotalSeatsWhenSeatsPerPopulationIsZero()
        {
            Assert.Equal(60, ProportionalAllocator.ChamberSize(250000, EngineTuning.Default.ElectionsPr));
        }

        [Fact]
        public void ChamberSize_ScalesWithPopulationAndClampsBothWays()
        {
            ElectionsPrTuning t = Tuning(seatsPerPopulation: 0.001, minSeats: 21, maxSeats: 120).ElectionsPr;

            Assert.Equal(21, ProportionalAllocator.ChamberSize(10000, t));   // 10 → clamped up
            Assert.Equal(50, ProportionalAllocator.ChamberSize(50000, t));   // 50 → in range
            Assert.Equal(120, ProportionalAllocator.ChamberSize(1000000, t)); // 1000 → clamped down
        }

        [Fact]
        public void DistrictSeatCount_IsZeroUnderTheShippedPureListConfiguration()
        {
            Assert.Equal(0, ProportionalAllocator.DistrictSeatCount(60, EngineTuning.Default.ElectionsPr));
            Assert.Equal(30, ProportionalAllocator.DistrictSeatCount(
                60, Tuning(districtSeatShare: 0.5).ElectionsPr));
        }

        // -----------------------------------------------------------------------------------------
        // Mixed-member top-up (districtSeatShare > 0)
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// District wins consume proportional entitlement rather than adding to it, and overhang is
        /// absorbed: p-c holds two district seats on a 14% vote, keeps both, and the chamber still
        /// totals five.
        /// </summary>
        [Fact]
        public void DistrictTopUp_AbsorbsOverhangWithoutGrowingTheChamber()
        {
            EngineTuning t = Tuning(thresholdShare: 0.0, minSeatsForRepresentation: 0,
                                    districtSeatShare: 0.4);
            var district = new List<PartySeatCount> { new PartySeatCount("p-c", 2) };

            SeatAllocationResult r = ProportionalAllocator.Allocate(
                Votes("p-a", 100, "p-b", 80, "p-c", 30), 5, district, t, SaveA, May1994, ElectionId);

            Assert.Equal(5, Sum(r));
            Assert.Equal(2, r.SeatsFor("p-a"));
            Assert.Equal(1, r.SeatsFor("p-b"));
            Assert.Equal(2, r.SeatsFor("p-c"));

            SeatAllocation c = Find(r, "p-c");
            Assert.Equal(2, c.DistrictSeats);
            Assert.Equal(0, c.ListSeats);
        }

        /// <summary>A district winner sits even though its list share missed the threshold.</summary>
        [Fact]
        public void DistrictTopUp_SeatsABelowThresholdDistrictWinner()
        {
            EngineTuning t = Tuning(thresholdShare: 0.5, minSeatsForRepresentation: 0,
                                    districtSeatShare: 0.25);
            var district = new List<PartySeatCount> { new PartySeatCount("p-b", 1) };

            SeatAllocationResult r = ProportionalAllocator.Allocate(
                Votes("p-a", 900, "p-b", 80, "p-c", 20), 4, district, t, SaveA, May1994, ElectionId);

            Assert.Equal(4, Sum(r));
            Assert.Equal(3, r.SeatsFor("p-a"));
            Assert.Equal(1, r.SeatsFor("p-b"));
            Assert.Equal(0, r.SeatsFor("p-c"));

            SeatAllocation b = Find(r, "p-b");
            Assert.False(b.PassedThreshold);
            Assert.Equal(1, b.DistrictSeats);
            Assert.Contains("p-b", r.QualifiedPartyIds);
        }

        // -----------------------------------------------------------------------------------------
        // Degenerate input — none of these may throw or hang
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void Allocate_HandlesNoBallot()
        {
            EngineTuning t = EngineTuning.Default;
            SeatAllocationResult r = ProportionalAllocator.Allocate(null, 60, t, SaveA, May1994, ElectionId);

            Assert.Empty(r.Seats);
            Assert.Equal(60, r.TotalSeats);
            Assert.Equal(0, r.TotalVotes);
        }

        [Fact]
        public void Allocate_HandlesZeroVotesAndZeroSeats()
        {
            EngineTuning t = EngineTuning.Default;

            SeatAllocationResult noVotes = ProportionalAllocator.Allocate(
                Votes("p-a", 0, "p-b", 0), 60, t, SaveA, May1994, ElectionId);
            Assert.Equal(0, Sum(noVotes));
            Assert.False(noVotes.ThresholdWaived);

            SeatAllocationResult noSeats = ProportionalAllocator.Allocate(
                Votes("p-a", 100, "p-b", 80), 0, t, SaveA, May1994, ElectionId);
            Assert.Equal(0, Sum(noSeats));
            Assert.Equal(0, noSeats.TotalSeats);
        }

        [Fact]
        public void Allocate_TreatsNegativeVotesAsZero()
        {
            EngineTuning t = Tuning(thresholdShare: 0.0, minSeatsForRepresentation: 0);
            SeatAllocationResult r = ProportionalAllocator.Allocate(
                Votes("p-a", 100, "p-b", -50), 3, t, SaveA, May1994, ElectionId);

            Assert.Equal(100, r.TotalVotes);
            Assert.Equal(3, r.SeatsFor("p-a"));
            Assert.Equal(0, r.SeatsFor("p-b"));
        }

        // -----------------------------------------------------------------------------------------
        // Shares → whole votes
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The voter model speaks in shares and the allocator counts ballots. The conversion must not
        /// lose or invent a vote — a one-vote margin has to survive it.
        /// </summary>
        [Fact]
        public void FromShares_SumsExactlyToTheBallotTotal()
        {
            var shares = new List<PartyVoteShare>
            {
                new PartyVoteShare("p-c", 1.0 / 3.0),
                new PartyVoteShare("p-a", 1.0 / 3.0),
                new PartyVoteShare("p-b", 1.0 / 3.0),
            };

            List<PartyVoteCount> counts = VoteCounts.FromShares(shares, 100);

            int total = 0;
            foreach (PartyVoteCount c in counts) total += c.Votes;
            Assert.Equal(100, total);

            Assert.Equal(new[] { "p-a", "p-b", "p-c" },
                         new[] { counts[0].PartyId, counts[1].PartyId, counts[2].PartyId });
            Assert.Equal(34, counts[0].Votes); // the leftover vote goes to the first id, not at random
            Assert.Equal(33, counts[1].Votes);
            Assert.Equal(33, counts[2].Votes);
        }

        /// <summary>
        /// Shares that do not sum to 1 are rescaled, not truncated. Exact binary fractions are used
        /// so the expected counts are not hostage to a floating-point rounding decision.
        /// </summary>
        [Fact]
        public void FromShares_NormalisesSharesThatDoNotSumToOne()
        {
            var shares = new List<PartyVoteShare>
            {
                new PartyVoteShare("p-a", 1.5),
                new PartyVoteShare("p-b", 0.5),
            };

            List<PartyVoteCount> counts = VoteCounts.FromShares(shares, 1000);

            Assert.Equal(750, counts[0].Votes);
            Assert.Equal(250, counts[1].Votes);
        }

        [Fact]
        public void FromShares_HandlesEmptyAndDegenerateInput()
        {
            Assert.Empty(VoteCounts.FromShares(null, 1000));

            List<PartyVoteCount> zeroTotal = VoteCounts.FromShares(
                new List<PartyVoteShare> { new PartyVoteShare("p-a", 0.0) }, 1000);
            Assert.Single(zeroTotal);
            Assert.Equal(0, zeroTotal[0].Votes);
        }

        /// <summary>Shares in, seats out, end to end — the path the election packet actually walks.</summary>
        [Fact]
        public void FromShares_FeedsTheAllocatorWithoutLosingVotes()
        {
            var shares = new List<PartyVoteShare>
            {
                new PartyVoteShare("p-a", 0.38),
                new PartyVoteShare("p-b", 0.25),
                new PartyVoteShare("p-c", 0.15),
                new PartyVoteShare("p-d", 0.12),
                new PartyVoteShare("p-e", 0.06),
                new PartyVoteShare("p-f", 0.04),
            };

            List<PartyVoteCount> counts = VoteCounts.FromShares(shares, 100000);
            SeatAllocationResult r = ProportionalAllocator.Allocate(
                counts, 60, EngineTuning.Default, SaveA, May1994, ElectionId);

            Assert.Equal(100000, r.TotalVotes);
            Assert.Equal(60, Sum(r));
            Assert.Equal(0, r.SeatsFor("p-f")); // 4% — below the 5% threshold
            Assert.True(r.SeatsFor("p-a") > r.SeatsFor("p-b"));
            Assert.True(r.SeatsFor("p-b") > r.SeatsFor("p-c"));
        }
    }
}
