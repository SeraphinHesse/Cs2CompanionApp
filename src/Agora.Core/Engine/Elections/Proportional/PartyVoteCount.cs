using System;
using System.Collections.Generic;
using System.Globalization;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Elections.Proportional
{
    /// <summary>
    /// One party's whole-vote total in a proportional contest. The allocator's only input atom.
    /// </summary>
    /// <remarks>
    /// Counts are integers on purpose (§6, <c>DistrictResult.VotesCast</c>): a one-vote margin has to
    /// be representable, and an exact tie has to be recognisable as exact rather than as two doubles
    /// that happen to be close. Every list of these is sorted by <see cref="PartyId"/> ordinal
    /// ascending before anything is computed from it.
    /// </remarks>
    public readonly struct PartyVoteCount : IEquatable<PartyVoteCount>, IComparable<PartyVoteCount>
    {
        public string PartyId { get; }

        /// <summary>Whole votes. Negative inputs are treated as zero rather than throwing.</summary>
        public int Votes { get; }

        public PartyVoteCount(string? partyId, int votes)
        {
            PartyId = partyId ?? string.Empty;
            Votes = votes < 0 ? 0 : votes;
        }

        public int CompareTo(PartyVoteCount other) => string.CompareOrdinal(PartyId, other.PartyId);

        public bool Equals(PartyVoteCount other) =>
            string.Equals(PartyId, other.PartyId, StringComparison.Ordinal) && Votes == other.Votes;

        public override bool Equals(object? obj) => obj is PartyVoteCount other && Equals(other);

        /// <summary>
        /// A hash that is stable across processes.
        /// </summary>
        /// <remarks>
        /// <see cref="string.GetHashCode()"/> — and <c>StringComparer.Ordinal.GetHashCode</c>, which
        /// delegates to it — is randomised per process on .NET Core. Nothing in this packet keys a
        /// dictionary on a vote count today, but a value type in <c>Agora.Core</c> whose hash changes
        /// between launches is a determinism trap waiting for the first caller who does.
        /// </remarks>
        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                string id = PartyId;
                if (id != null)
                {
                    for (int i = 0; i < id.Length; i++) h = h * 31 + id[i];
                }
                return h * 397 ^ Votes;
            }
        }

        public override string ToString() =>
            PartyId + "=" + Votes.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Seats a party already holds coming into the list allocation — district seats under a
    /// mixed-member configuration (<c>electionsPr.districtSeatShare</c> &gt; 0).
    /// </summary>
    /// <remarks>
    /// The shipped EU configuration is a pure list system (<c>districtSeatShare = 0</c>), so this is
    /// normally empty. When it is not, the list seats are a *top-up*: a party's divisor sequence
    /// continues from the seats it already holds, so district wins consume proportional entitlement
    /// rather than adding to it. Overhang is absorbed, not levelled — the chamber never grows.
    /// </remarks>
    public readonly struct PartySeatCount
    {
        public string PartyId { get; }

        public int Seats { get; }

        public PartySeatCount(string? partyId, int seats)
        {
            PartyId = partyId ?? string.Empty;
            Seats = seats < 0 ? 0 : seats;
        }

        public override string ToString() =>
            PartyId + ":" + Seats.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Turning modelled vote <em>shares</em> into whole vote <em>counts</em>.
    /// </summary>
    /// <remarks>
    /// The voter model produces shares; the allocator consumes counts. Rounding each share
    /// independently would lose or invent votes, so the conversion uses largest-remainder rounding
    /// against the known ballot total. That keeps the counts summing exactly to
    /// <c>totalVotesCast</c>, which is what makes <see cref="ProportionalAllocator"/>'s threshold
    /// test and tie detection meaningful.
    /// </remarks>
    public static class VoteCounts
    {
        /// <summary>
        /// Converts shares to whole votes summing exactly to <paramref name="totalVotesCast"/>.
        /// Output is sorted by party id ordinal ascending; duplicate ids are summed.
        /// </summary>
        /// <remarks>
        /// Remainder ties are broken by party id, not by a seeded stream. The leftover vote here is a
        /// rounding artefact of the share model, not a contested ballot — spending an
        /// <c>election.tiebreak</c> draw on it would couple real seat tie-breaks to the arithmetic of
        /// rounding, and make them shift whenever turnout changed by one voter.
        /// </remarks>
        public static List<PartyVoteCount> FromShares(IReadOnlyList<PartyVoteShare>? shares, int totalVotesCast)
        {
            var merged = MergeShares(shares);
            int n = merged.Count;
            var result = new List<PartyVoteCount>(n);
            if (n == 0) return result;

            if (totalVotesCast <= 0)
            {
                for (int i = 0; i < n; i++) result.Add(new PartyVoteCount(merged[i].PartyId, 0));
                return result;
            }

            double total = 0.0;
            for (int i = 0; i < n; i++) total += merged[i].Share;

            if (total <= 0.0 || double.IsNaN(total) || double.IsInfinity(total))
            {
                for (int i = 0; i < n; i++) result.Add(new PartyVoteCount(merged[i].PartyId, 0));
                return result;
            }

            var exact = new double[n];
            var counts = new int[n];
            long assigned = 0;
            for (int i = 0; i < n; i++)
            {
                exact[i] = merged[i].Share / total * totalVotesCast;
                counts[i] = (int)Math.Floor(exact[i]);
                assigned += counts[i];
            }

            int remaining = totalVotesCast - (int)assigned;
            if (remaining > 0)
            {
                var order = new List<int>(n);
                for (int i = 0; i < n; i++) order.Add(i);

                // Descending remainder, then ascending party id. Ids are unique after MergeShares, so
                // this is a total order and List.Sort's instability cannot leak in.
                order.Sort((a, b) =>
                {
                    double ra = exact[a] - counts[a];
                    double rb = exact[b] - counts[b];
                    int c = rb.CompareTo(ra);
                    return c != 0 ? c : string.CompareOrdinal(merged[a].PartyId, merged[b].PartyId);
                });

                for (int k = 0; k < remaining; k++) counts[order[k % n]]++;
            }

            for (int i = 0; i < n; i++) result.Add(new PartyVoteCount(merged[i].PartyId, counts[i]));
            return result;
        }

        /// <summary>Sums duplicate ids, drops negative/NaN shares, and sorts by party id ordinal.</summary>
        private static List<PartyVoteShare> MergeShares(IReadOnlyList<PartyVoteShare>? shares)
        {
            var merged = new List<PartyVoteShare>();
            if (shares == null) return merged;

            for (int i = 0; i < shares.Count; i++)
            {
                string id = shares[i].PartyId;
                double share = shares[i].Share;
                if (double.IsNaN(share) || double.IsInfinity(share) || share < 0.0) share = 0.0;

                int found = -1;
                for (int j = 0; j < merged.Count; j++)
                {
                    if (string.Equals(merged[j].PartyId, id, StringComparison.Ordinal)) { found = j; break; }
                }

                if (found >= 0) merged[found] = new PartyVoteShare(id, merged[found].Share + share);
                else merged.Add(new PartyVoteShare(id, share));
            }

            merged.Sort((a, b) => string.CompareOrdinal(a.PartyId, b.PartyId));
            return merged;
        }
    }
}
