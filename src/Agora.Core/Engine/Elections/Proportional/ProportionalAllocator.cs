using System;
using System.Collections.Generic;
using System.Globalization;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Elections.Proportional
{
    /// <summary>
    /// EU-mode proportional seat allocation: whole vote totals in, <see cref="SeatAllocation"/> out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ratified configuration (<c>data/engine_tuning.json</c> → <c>electionsPr</c>): a 60-seat chamber,
    /// a 5% electoral threshold, and <b>modified Sainte-Lague with a 1.4 first divisor</b> — the
    /// Scandinavian variant, where the first divisor is raised above 1 so a party's *first* seat costs
    /// more than its later ones. That is the whole point of the modification: it damps the splinter
    /// parties that an unmodified 1/3/5/7 sequence rewards. Nothing here is hardcoded; d'Hondt and
    /// largest-remainder are implemented too because <c>electionsPr.method</c> documents them as legal
    /// values, and a key that silently does nothing is worse than one that does not exist.
    /// </para>
    /// <para>
    /// The class is static and stateless. Every call is a pure function of
    /// (votes, seats, tuning, saveGuid, date, electionId) — non-negotiable #3. The save guid and date
    /// are taken rather than a pre-built generator so that each tie can draw from its own
    /// <c>election.tiebreak</c> sub-stream: with one shared generator, an early tie would consume a
    /// draw and shift the outcome of every later one, coupling unrelated seats together.
    /// </para>
    /// </remarks>
    public static class ProportionalAllocator
    {
        /// <summary>Modified Sainte-Lague. Divisors <c>firstDivisor</c>, 3, 5, 7, … The ratified method.</summary>
        public const string MethodSainteLague = "sainte-lague";

        /// <summary>D'Hondt / Jefferson. Divisors 1, 2, 3, … Favours large parties.</summary>
        public const string MethodDHondt = "d-hondt";

        /// <summary>Hare quota with largest remainders. Not a divisor method.</summary>
        public const string MethodLargestRemainder = "largest-remainder";

        // ---------------------------------------------------------------------------------------
        // Chamber size
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Chamber size for a city of this population, from <c>electionsPr</c>.
        /// </summary>
        /// <remarks>
        /// <c>seatsPerPopulation = 0</c> (the shipped value) pins the chamber at <c>totalSeats</c>;
        /// a positive value scales it with the city and is then clamped to
        /// <c>[minSeats, maxSeats]</c>. Callers must pass the same population on every call for a
        /// given election — the chamber must not resize between counting and allocating.
        /// </remarks>
        public static int ChamberSize(int population, ElectionsPrTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            int seats;
            if (tuning.SeatsPerPopulation > 0.0 && population > 0)
            {
                double raw = population * tuning.SeatsPerPopulation;
                if (double.IsNaN(raw) || raw < 0.0) raw = 0.0;
                if (raw > int.MaxValue) raw = int.MaxValue;
                seats = (int)Math.Floor(raw);
            }
            else
            {
                seats = tuning.TotalSeats;
            }

            int min = tuning.MinSeats < 1 ? 1 : tuning.MinSeats;
            int max = tuning.MaxSeats < min ? min : tuning.MaxSeats;
            if (seats < min) seats = min;
            if (seats > max) seats = max;
            return seats;
        }

        /// <summary>
        /// How many of the chamber's seats are decided in district contests, from
        /// <c>electionsPr.districtSeatShare</c>. Zero under the shipped pure-list configuration.
        /// </summary>
        public static int DistrictSeatCount(int totalSeats, ElectionsPrTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            if (totalSeats <= 0) return 0;

            double share = tuning.DistrictSeatShare;
            if (double.IsNaN(share) || share <= 0.0) return 0;
            if (share > 1.0) share = 1.0;

            int seats = (int)Math.Floor(totalSeats * share);
            if (seats < 0) seats = 0;
            if (seats > totalSeats) seats = totalSeats;
            return seats;
        }

        // ---------------------------------------------------------------------------------------
        // Allocation
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Allocates a pure list chamber. The normal EU call.
        /// </summary>
        /// <param name="votes">Whole votes per party. Duplicate ids are summed; order is irrelevant.</param>
        /// <param name="totalSeats">Chamber size, usually from <see cref="ChamberSize"/>.</param>
        /// <param name="tuning">Tuning accessor; only the <c>electionsPr</c> section is read.</param>
        /// <param name="saveGuid">Save identity, for the tie-break stream.</param>
        /// <param name="date">Election date, for the tie-break stream.</param>
        /// <param name="electionId">
        /// Stable id of this election, e.g. <c>"election-1994-05"</c>. It is part of the tie-break
        /// entity id, so two elections on the same date never share a coin flip.
        /// </param>
        public static SeatAllocationResult Allocate(
            IReadOnlyList<PartyVoteCount>? votes,
            int totalSeats,
            EngineTuning tuning,
            Guid saveGuid,
            SimDate date,
            string electionId)
        {
            return Allocate(votes, totalSeats, null, tuning, saveGuid, date, electionId);
        }

        /// <summary>
        /// Allocates a chamber in which some seats were already won in district contests, topping the
        /// rest up proportionally.
        /// </summary>
        /// <remarks>
        /// A party's divisor sequence continues from the district seats it already holds, so district
        /// wins consume proportional entitlement instead of adding to it, and a district winner sits
        /// even if its list share missed the threshold. Overhang is absorbed: the chamber never grows
        /// past <paramref name="totalSeats"/>.
        /// </remarks>
        public static SeatAllocationResult Allocate(
            IReadOnlyList<PartyVoteCount>? votes,
            int totalSeats,
            IReadOnlyList<PartySeatCount>? districtSeatsWon,
            EngineTuning tuning,
            Guid saveGuid,
            SimDate date,
            string electionId)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            ElectionsPrTuning t = tuning.ElectionsPr;
            string method = NormalizeMethod(t.Method);
            string context = string.IsNullOrEmpty(electionId) ? "election" : electionId;
            if (totalSeats < 0) totalSeats = 0;

            double threshold = t.ThresholdShare;
            if (double.IsNaN(threshold) || threshold < 0.0) threshold = 0.0;
            if (threshold > 1.0) threshold = 1.0;

            List<PartyVoteCount> parties = MergeAndSort(votes);
            int n = parties.Count;
            if (n == 0)
            {
                return new SeatAllocationResult(
                    new List<SeatAllocation>(), totalSeats, 0, 0, threshold, method,
                    new List<string>(), new List<string>(), false, new List<SeatTieBreak>(), 0.0);
            }

            long totalVotesLong = 0;
            for (int i = 0; i < n; i++) totalVotesLong += parties[i].Votes;
            int totalVotes = totalVotesLong > int.MaxValue ? int.MaxValue : (int)totalVotesLong;

            // --- seats already held in district contests ---------------------------------------
            var seats = new int[n];
            var districtSeats = new int[n];
            int districtTotal = 0;
            if (districtSeatsWon != null)
            {
                for (int k = 0; k < districtSeatsWon.Count; k++)
                {
                    int idx = IndexOfParty(parties, districtSeatsWon[k].PartyId);
                    if (idx >= 0) districtSeats[idx] += districtSeatsWon[k].Seats;
                }

                for (int i = 0; i < n; i++)
                {
                    int room = totalSeats - districtTotal;
                    if (room < 0) room = 0;
                    if (districtSeats[i] > room) districtSeats[i] = room;
                    districtTotal += districtSeats[i];
                    seats[i] = districtSeats[i];
                }
            }

            // --- electoral threshold -----------------------------------------------------------
            var passed = new bool[n];
            var qualified = new bool[n];
            int qualifiedCount = 0;
            long qualifyingVotesLong = 0;

            for (int i = 0; i < n; i++)
            {
                double share = totalVotes > 0 ? (double)parties[i].Votes / totalVotes : 0.0;
                passed[i] = parties[i].Votes > 0 && share >= threshold;

                // A district winner sits regardless of its list share (the basic-mandate rule). Under
                // the shipped pure-list configuration districtSeats is always zero, so this is inert.
                qualified[i] = passed[i] || districtSeats[i] > 0;
                if (qualified[i])
                {
                    qualifiedCount++;
                    qualifyingVotesLong += parties[i].Votes;
                }
            }

            bool waived = false;
            if (qualifiedCount == 0)
            {
                // Nobody cleared the bar. Fail open rather than seat an empty chamber: coalition
                // formation and mandates downstream have nothing to work with otherwise.
                for (int i = 0; i < n; i++)
                {
                    qualified[i] = parties[i].Votes > 0;
                    if (qualified[i])
                    {
                        qualifiedCount++;
                        qualifyingVotesLong += parties[i].Votes;
                    }
                }
                waived = qualifiedCount > 0;
            }

            int qualifyingVotes = qualifyingVotesLong > int.MaxValue
                ? int.MaxValue
                : (int)qualifyingVotesLong;

            // --- guaranteed representation -----------------------------------------------------
            int assigned = districtTotal;
            int guarantee = t.MinSeatsForRepresentation;
            if (guarantee < 0) guarantee = 0;

            if (guarantee > 0 && qualifiedCount > 0 && totalSeats > 0)
            {
                // Never promise more chamber than exists: the floor is capped at an equal split.
                int cap = totalSeats / qualifiedCount;
                int floorSeats = guarantee < cap ? guarantee : cap;

                // Under scarcity the strongest parties are seated first. Ordering by party id here
                // would hand the last seat to whoever is alphabetically luckiest.
                List<int> byVotesDesc = OrderByVotesDescending(parties, qualified);
                for (int k = 0; k < byVotesDesc.Count && assigned < totalSeats; k++)
                {
                    int i = byVotesDesc[k];
                    int need = floorSeats - seats[i];
                    if (need <= 0) continue;
                    if (assigned + need > totalSeats) need = totalSeats - assigned;
                    seats[i] += need;
                    assigned += need;
                }
            }

            int remaining = totalSeats - assigned;
            if (remaining < 0) remaining = 0;

            // --- the method itself -------------------------------------------------------------
            var tieBreaks = new List<SeatTieBreak>();
            if (qualifiedCount > 0 && remaining > 0)
            {
                if (string.Equals(method, MethodLargestRemainder, StringComparison.Ordinal))
                {
                    AllocateLargestRemainder(parties, qualified, seats, totalSeats, qualifyingVotes,
                                             saveGuid, date, context, tieBreaks);
                }
                else
                {
                    AllocateHighestAverages(parties, qualified, seats, remaining, assigned, method,
                                            FirstDivisor(t), saveGuid, date, context, tieBreaks);
                }
            }

            // --- assemble ----------------------------------------------------------------------
            var allocations = new List<SeatAllocation>(n);
            var qualifiedIds = new List<string>();
            var excludedIds = new List<string>();
            double lsq = 0.0;

            for (int i = 0; i < n; i++)
            {
                int partySeats = seats[i];
                double seatShare = totalSeats > 0 ? (double)partySeats / totalSeats : 0.0;
                double voteShare = totalVotes > 0 ? (double)parties[i].Votes / totalVotes : 0.0;
                int dSeats = districtSeats[i] < partySeats ? districtSeats[i] : partySeats;
                int lSeats = partySeats - dSeats;

                allocations.Add(new SeatAllocation(
                    parties[i].PartyId, partySeats, seatShare, voteShare, dSeats, lSeats, passed[i]));

                if (qualified[i]) qualifiedIds.Add(parties[i].PartyId);
                else excludedIds.Add(parties[i].PartyId);

                double d = voteShare - seatShare;
                lsq += d * d;
            }

            return new SeatAllocationResult(
                allocations, totalSeats, totalVotes, qualifyingVotes, threshold, method,
                qualifiedIds, excludedIds, waived, tieBreaks, Math.Sqrt(0.5 * lsq));
        }

        // ---------------------------------------------------------------------------------------
        // Methods
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Highest averages, one seat at a time. Sainte-Lague and d'Hondt differ only in
        /// <see cref="Divisor"/>.
        /// </summary>
        private static void AllocateHighestAverages(
            List<PartyVoteCount> parties, bool[] qualified, int[] seats, int remaining,
            int alreadyAssigned, string method, double firstDivisor,
            Guid saveGuid, SimDate date, string context, List<SeatTieBreak> tieBreaks)
        {
            int n = parties.Count;
            var tied = new List<int>();

            for (int s = 0; s < remaining; s++)
            {
                double best = double.NegativeInfinity;
                for (int i = 0; i < n; i++)
                {
                    if (!qualified[i]) continue;
                    double q = parties[i].Votes / Divisor(method, seats[i], firstDivisor);
                    if (q > best) best = q;
                }

                tied.Clear();
                for (int i = 0; i < n; i++)
                {
                    if (!qualified[i]) continue;
                    double q = parties[i].Votes / Divisor(method, seats[i], firstDivisor);
                    // Exact equality, not an epsilon band — see SeatTieBreak for why.
                    if (q == best) tied.Add(i);
                }

                if (tied.Count == 0) break; // defensive: unreachable while any party qualifies

                int seatNumber = alreadyAssigned + s + 1;
                int winner = tied.Count == 1
                    ? tied[0]
                    : BreakTie(parties, tied, saveGuid, date, context, seatNumber, "seat", tieBreaks);

                seats[winner]++;
            }
        }

        /// <summary>
        /// Hare quota, then largest remainders. Any floor already in <paramref name="seats"/>
        /// (district wins, guaranteed representation) is respected as a lower bound.
        /// </summary>
        private static void AllocateLargestRemainder(
            List<PartyVoteCount> parties, bool[] qualified, int[] seats, int totalSeats,
            int qualifyingVotes, Guid saveGuid, SimDate date, string context,
            List<SeatTieBreak> tieBreaks)
        {
            int n = parties.Count;
            if (qualifyingVotes <= 0 || totalSeats <= 0) return;

            var floorSeats = new int[n];
            for (int i = 0; i < n; i++) floorSeats[i] = seats[i];

            double quota = (double)qualifyingVotes / totalSeats;
            var exact = new double[n];
            var baseSeats = new int[n];
            int qualifiedCount = 0;

            for (int i = 0; i < n; i++)
            {
                if (!qualified[i]) continue;
                qualifiedCount++;
                exact[i] = parties[i].Votes / quota;
                baseSeats[i] = (int)Math.Floor(exact[i]);
                if (baseSeats[i] > seats[i]) seats[i] = baseSeats[i];
            }

            if (qualifiedCount == 0) return;

            int assigned = 0;
            for (int i = 0; i < n; i++) assigned += seats[i];

            if (assigned > totalSeats)
            {
                // Floors over-filled the chamber. Strip from the weakest remainders first, never
                // below a party's guaranteed floor.
                List<int> weakestFirst = OrderByRemainderAscending(parties, qualified, exact, baseSeats);
                int excess = assigned - totalSeats;
                bool progress = true;
                while (excess > 0 && progress)
                {
                    progress = false;
                    for (int k = 0; k < weakestFirst.Count && excess > 0; k++)
                    {
                        int i = weakestFirst[k];
                        if (seats[i] <= floorSeats[i]) continue;
                        seats[i]--;
                        excess--;
                        progress = true;
                    }
                }
                return;
            }

            int extra = totalSeats - assigned;
            var awarded = new bool[n];
            int awardedCount = 0;
            var tied = new List<int>();

            for (int s = 0; s < extra; s++)
            {
                if (awardedCount >= qualifiedCount)
                {
                    // More leftover seats than parties: start a second round rather than spin.
                    for (int i = 0; i < n; i++) awarded[i] = false;
                    awardedCount = 0;
                }

                double best = double.NegativeInfinity;
                for (int i = 0; i < n; i++)
                {
                    if (!qualified[i] || awarded[i]) continue;
                    double r = exact[i] - baseSeats[i];
                    if (r > best) best = r;
                }

                tied.Clear();
                for (int i = 0; i < n; i++)
                {
                    if (!qualified[i] || awarded[i]) continue;
                    if (exact[i] - baseSeats[i] == best) tied.Add(i);
                }

                if (tied.Count == 0) break; // defensive

                int seatNumber = assigned + s + 1;
                int winner = tied.Count == 1
                    ? tied[0]
                    : BreakTie(parties, tied, saveGuid, date, context, seatNumber, "remainder", tieBreaks);

                seats[winner]++;
                awarded[winner] = true;
                awardedCount++;
            }
        }

        /// <summary>
        /// The divisor a party's next seat is priced at, given the seats it already holds.
        /// </summary>
        /// <remarks>
        /// Modified Sainte-Lague: <c>firstDivisor</c>, then 3, 5, 7, … The raised first divisor is
        /// what makes a party's first seat expensive; it applies only at zero seats held, so a party
        /// that already won district seats does not get it.
        /// </remarks>
        private static double Divisor(string method, int seatsHeld, double firstDivisor)
        {
            if (string.Equals(method, MethodDHondt, StringComparison.Ordinal))
                return seatsHeld + 1.0;

            return seatsHeld == 0 ? firstDivisor : 2.0 * seatsHeld + 1.0;
        }

        private static double FirstDivisor(ElectionsPrTuning t)
        {
            double fd = t.FirstDivisor;
            if (double.IsNaN(fd) || double.IsInfinity(fd) || fd <= 0.0) return 1.0;
            return fd;
        }

        /// <summary>Maps the tuning string onto a known method; anything unrecognised is Sainte-Lague.</summary>
        private static string NormalizeMethod(string? method)
        {
            if (string.IsNullOrEmpty(method)) return MethodSainteLague;

            string m = method!.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
            if (m == "d-hondt" || m == "dhondt" || m == "d'hondt" || m == "jefferson")
                return MethodDHondt;
            if (m == "largest-remainder" || m == "hare" || m == "hare-niemeyer" || m == "quota")
                return MethodLargestRemainder;
            return MethodSainteLague;
        }

        // ---------------------------------------------------------------------------------------
        // Tie-breaking
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Resolves an exact tie from the <c>election.tiebreak</c> stream.
        /// </summary>
        /// <remarks>
        /// <paramref name="tied"/> arrives in party-id ordinal order because <paramref name="parties"/>
        /// is sorted, so the draw indexes a canonical list rather than an incidental construction
        /// order — the difference between a reproducible coin flip and a desync. The entity id
        /// includes the seat number, so each tie draws from its own sub-stream and one tie cannot
        /// shift another.
        /// </remarks>
        private static int BreakTie(
            List<PartyVoteCount> parties, List<int> tied, Guid saveGuid, SimDate date,
            string context, int seatNumber, string kind, List<SeatTieBreak> tieBreaks)
        {
            var candidates = new string[tied.Count];
            for (int k = 0; k < tied.Count; k++) candidates[k] = parties[tied[k]].PartyId;

            string entityId = context + "#" + kind + seatNumber.ToString(CultureInfo.InvariantCulture);
            DeterministicRng rng = SeedStreams.RngFor(saveGuid, date, StreamNames.ElectionTieBreak, entityId);
            int winner = tied[rng.NextInt(0, tied.Count)];

            tieBreaks.Add(new SeatTieBreak(seatNumber, parties[winner].PartyId, candidates, entityId));
            return winner;
        }

        // ---------------------------------------------------------------------------------------
        // Ordering helpers — every one of these produces a total order, so List.Sort's instability
        // can never leak into engine state.
        // ---------------------------------------------------------------------------------------

        /// <summary>Sums duplicate ids and sorts by party id ordinal ascending.</summary>
        private static List<PartyVoteCount> MergeAndSort(IReadOnlyList<PartyVoteCount>? votes)
        {
            var merged = new List<PartyVoteCount>();
            if (votes == null) return merged;

            for (int i = 0; i < votes.Count; i++)
            {
                string id = votes[i].PartyId;
                int found = IndexOfParty(merged, id);
                if (found >= 0)
                    merged[found] = new PartyVoteCount(id, merged[found].Votes + votes[i].Votes);
                else
                    merged.Add(new PartyVoteCount(id, votes[i].Votes));
            }

            merged.Sort((a, b) => string.CompareOrdinal(a.PartyId, b.PartyId));
            return merged;
        }

        private static int IndexOfParty(List<PartyVoteCount> parties, string? partyId)
        {
            for (int i = 0; i < parties.Count; i++)
            {
                if (string.Equals(parties[i].PartyId, partyId, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        private static List<int> OrderByVotesDescending(List<PartyVoteCount> parties, bool[] qualified)
        {
            var order = new List<int>();
            for (int i = 0; i < parties.Count; i++)
            {
                if (qualified[i]) order.Add(i);
            }

            order.Sort((a, b) =>
            {
                int c = parties[b].Votes.CompareTo(parties[a].Votes);
                return c != 0 ? c : string.CompareOrdinal(parties[a].PartyId, parties[b].PartyId);
            });
            return order;
        }

        private static List<int> OrderByRemainderAscending(
            List<PartyVoteCount> parties, bool[] qualified, double[] exact, int[] baseSeats)
        {
            var order = new List<int>();
            for (int i = 0; i < parties.Count; i++)
            {
                if (qualified[i]) order.Add(i);
            }

            order.Sort((a, b) =>
            {
                double ra = exact[a] - baseSeats[a];
                double rb = exact[b] - baseSeats[b];
                int c = ra.CompareTo(rb);
                return c != 0 ? c : string.CompareOrdinal(parties[a].PartyId, parties[b].PartyId);
            });
            return order;
        }
    }
}
