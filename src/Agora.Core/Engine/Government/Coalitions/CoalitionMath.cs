using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Government.Coalitions
{
    /// <summary>
    /// One seat-holding party as the coalition packet sees it: an id, a seat count and a platform.
    /// </summary>
    /// <remarks>
    /// Pools of these are always sorted by <see cref="PartyId"/> ordinal ascending before anything is
    /// summed over them. Every distance figure in this packet is a floating-point sum over pairs, and
    /// an unsorted pool would make the last bit of that sum depend on the caller's list order.
    /// </remarks>
    internal readonly struct PartySeat
    {
        public string PartyId { get; }

        public int Seats { get; }

        public IssuePosition Platform { get; }

        public PartySeat(string partyId, int seats, IssuePosition platform)
        {
            PartyId = partyId;
            Seats = seats;
            Platform = platform;
        }
    }

    /// <summary>
    /// Shared, side-effect-free arithmetic for coalition formation and coalition stability. Internal
    /// on purpose: the packet's public surface is <see cref="CoalitionFormation"/> and
    /// <see cref="CoalitionStability"/>, and nothing else.
    /// </summary>
    internal static class CoalitionMath
    {
        // netstandard2.0 has no Math.Clamp.
        internal static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);

        /// <summary>
        /// Builds the seat-holding pool, sorted by party id ordinal ascending.
        /// </summary>
        /// <remarks>
        /// <paramref name="totalSeats"/> is the whole chamber, including seats held by parties that
        /// have since dissolved or merged — those seats still count against a majority even though
        /// their holder cannot join a government.
        /// </remarks>
        internal static List<PartySeat> BuildPool(
            IReadOnlyList<SeatAllocation> seats,
            IReadOnlyList<Party> parties,
            out int totalSeats)
        {
            // Lookup only. This dictionary is never enumerated, so it cannot leak iteration order.
            var platforms = new Dictionary<string, Party>(StringComparer.Ordinal);
            if (parties != null)
            {
                for (int i = 0; i < parties.Count; i++)
                {
                    Party p = parties[i];
                    if (p == null || string.IsNullOrEmpty(p.Id)) continue;
                    if (!platforms.ContainsKey(p.Id)) platforms.Add(p.Id, p);
                }
            }

            // Sort the allocations before reading them so that a duplicate party id resolves to the
            // same winner regardless of how the caller ordered its list.
            var ordered = new List<SeatAllocation>();
            if (seats != null)
            {
                for (int i = 0; i < seats.Count; i++) ordered.Add(seats[i]);
            }
            ordered.Sort(CompareAllocations);

            int chamber = 0;
            var pool = new List<PartySeat>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < ordered.Count; i++)
            {
                SeatAllocation a = ordered[i];
                if (string.IsNullOrEmpty(a.PartyId)) continue;
                if (!seen.Add(a.PartyId)) continue;
                if (a.Seats <= 0) continue;

                chamber += a.Seats;

                Party party;
                if (platforms.TryGetValue(a.PartyId, out party))
                {
                    // A dissolved or merged brand holds no bench that can be brought into government.
                    if (party.Status == PartyStatus.Dissolved || party.Status == PartyStatus.Merged) continue;
                    pool.Add(new PartySeat(a.PartyId, a.Seats, party.Platform));
                }
                else
                {
                    // A seat-holder the caller did not describe still sits in the chamber. Treating it
                    // as centrist keeps formation total rather than throwing mid-election-night.
                    pool.Add(new PartySeat(a.PartyId, a.Seats, IssuePosition.Centre));
                }
            }

            pool.Sort(ComparePartySeats);
            totalSeats = chamber;
            return pool;
        }

        private static int CompareAllocations(SeatAllocation a, SeatAllocation b) =>
            string.CompareOrdinal(a.PartyId ?? "", b.PartyId ?? "");

        private static int ComparePartySeats(PartySeat a, PartySeat b) =>
            string.CompareOrdinal(a.PartyId, b.PartyId);

        /// <summary>Seats first, then party id ordinal — the chamber's "largest party" order.</summary>
        internal static int CompareBySeatsDescending(PartySeat a, PartySeat b)
        {
            int s = b.Seats.CompareTo(a.Seats);
            return s != 0 ? s : string.CompareOrdinal(a.PartyId, b.PartyId);
        }

        /// <summary>
        /// Mean unweighted platform distance across every member pair, in <c>[0,1]</c>. Fewer than two
        /// members is zero distance.
        /// </summary>
        internal static double MeanPairwiseDistance(IReadOnlyList<PartySeat> members)
        {
            int n = members.Count;
            if (n < 2) return 0.0;

            double sum = 0.0;
            int pairs = 0;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    sum += members[i].Platform.Distance(members[j].Platform);
                    pairs++;
                }
            }

            return sum / pairs;
        }

        /// <summary>Widest platform gap between any two members, in <c>[0,1]</c>.</summary>
        internal static double MaxPairwiseDistance(IReadOnlyList<PartySeat> members)
        {
            int n = members.Count;
            if (n < 2) return 0.0;

            double max = 0.0;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double d = members[i].Platform.Distance(members[j].Platform);
                    if (d > max) max = d;
                }
            }

            return max;
        }

        /// <summary>
        /// Cohesion from mean platform distance: <c>cohesionBase - cohesionDistancePenalty * distance</c>,
        /// clamped to <c>[0,1]</c>. A single-party government sits at <c>cohesionBase</c> — it has no
        /// partners to fall out with, but the tuning still decides how confident "no partners" is.
        /// </summary>
        internal static double Cohesion(double meanDistance, CoalitionsTuning t) =>
            Clamp01(t.CohesionBase - t.CohesionDistancePenalty * meanDistance);

        /// <summary>
        /// The distance cap this member set is judged against. The grand-coalition bonus only applies
        /// to a pair that is exactly the two largest parties in the chamber (tuning calls it slack for
        /// "when nothing else works", so formation only enables it on the second pass).
        /// </summary>
        internal static double EffectiveDistanceCap(
            IReadOnlyList<PartySeat> members,
            IReadOnlyList<PartySeat> chamber,
            CoalitionsTuning t,
            bool allowGrandSlack)
        {
            if (allowGrandSlack && IsTwoLargest(members, chamber))
                return t.IdeologicalDistanceCap + t.GrandCoalitionDistanceBonus;

            return t.IdeologicalDistanceCap;
        }

        /// <summary>True when <paramref name="members"/> is exactly the chamber's two largest parties.</summary>
        internal static bool IsTwoLargest(IReadOnlyList<PartySeat> members, IReadOnlyList<PartySeat> chamber)
        {
            if (members.Count != 2 || chamber.Count < 2) return false;

            var bySize = new List<PartySeat>(chamber);
            bySize.Sort(CompareBySeatsDescending);

            string a = bySize[0].PartyId;
            string b = bySize[1].PartyId;

            bool hasA = false, hasB = false;
            for (int i = 0; i < members.Count; i++)
            {
                if (string.CompareOrdinal(members[i].PartyId, a) == 0) hasA = true;
                else if (string.CompareOrdinal(members[i].PartyId, b) == 0) hasB = true;
            }

            return hasA && hasB;
        }

        /// <summary>The member holding the most seats; an exact tie goes to the lower party id.</summary>
        internal static PartySeat LeadOf(IReadOnlyList<PartySeat> members)
        {
            PartySeat best = members[0];
            for (int i = 1; i < members.Count; i++)
            {
                if (CompareBySeatsDescending(members[i], best) < 0) best = members[i];
            }
            return best;
        }

        /// <summary>Party ids, sorted ordinal ascending — the contract order for every id list.</summary>
        internal static List<string> SortedIds(IReadOnlyList<PartySeat> members)
        {
            var ids = new List<string>(members.Count);
            for (int i = 0; i < members.Count; i++) ids.Add(members[i].PartyId);
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        /// <summary>Stable identity for a member set: sorted ids joined with <c>+</c>.</summary>
        internal static string KeyOf(IReadOnlyList<string> sortedIds) =>
            string.Join("+", (IEnumerable<string>)sortedIds);

        internal static double ShareOf(int seats, int totalSeats) =>
            totalSeats <= 0 ? 0.0 : (double)seats / totalSeats;

        internal static int SeatsOf(IReadOnlyList<PartySeat> members)
        {
            int sum = 0;
            for (int i = 0; i < members.Count; i++) sum += members[i].Seats;
            return sum;
        }

        /// <summary>
        /// Seat-holding parties outside the government, sorted ordinal ascending. Parties with no
        /// seats are not "opposition" — they are not in the chamber at all.
        /// </summary>
        internal static List<string> OppositionIds(IReadOnlyList<PartySeat> chamber, IReadOnlyList<string> memberIds)
        {
            var members = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < memberIds.Count; i++) members.Add(memberIds[i]);

            var opposition = new List<string>();
            for (int i = 0; i < chamber.Count; i++)
            {
                if (!members.Contains(chamber[i].PartyId)) opposition.Add(chamber[i].PartyId);
            }

            opposition.Sort(StringComparer.Ordinal);
            return opposition;
        }

        /// <summary>Selects pool entries whose id is in <paramref name="ids"/>, keeping pool order.</summary>
        internal static List<PartySeat> Select(IReadOnlyList<PartySeat> pool, IReadOnlyList<string> ids)
        {
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < ids.Count; i++) wanted.Add(ids[i]);

            var selected = new List<PartySeat>();
            for (int i = 0; i < pool.Count; i++)
            {
                if (wanted.Contains(pool[i].PartyId)) selected.Add(pool[i]);
            }
            return selected;
        }

        /// <summary>The month the ballot falls on after a government collapses mid-term.</summary>
        internal static SimDate SnapElectionDate(SimDate collapseDate, CoalitionsTuning t) =>
            collapseDate.AddMonths(t.SnapElectionDelayMonths < 0 ? 0 : t.SnapElectionDelayMonths);
    }
}
