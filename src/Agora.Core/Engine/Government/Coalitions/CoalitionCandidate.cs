using System;
using System.Collections.Generic;

namespace Agora.Core.Engine.Government.Coalitions
{
    /// <summary>
    /// One arrangement that could govern: a member set, the arithmetic that decides whether it is
    /// viable, and the score it is ranked by. Produced by <see cref="CoalitionFormation"/>; useful to
    /// the dashboard ("who was talking to whom") and to the tests, which assert the ranking rather
    /// than the outcome of the talks.
    /// </summary>
    public sealed class CoalitionCandidate
    {
        internal CoalitionCandidate(
            IReadOnlyList<string> memberPartyIds,
            string leadPartyId,
            int seats,
            double seatShare,
            bool hasMajority,
            double meanPairwiseDistance,
            double maxPairwiseDistance,
            double distanceCap,
            double cohesion,
            double score,
            bool isGrandCoalition)
        {
            MemberPartyIds = memberPartyIds;
            LeadPartyId = leadPartyId;
            Seats = seats;
            SeatShare = seatShare;
            HasMajority = hasMajority;
            MeanPairwiseDistance = meanPairwiseDistance;
            MaxPairwiseDistance = maxPairwiseDistance;
            DistanceCap = distanceCap;
            Cohesion = cohesion;
            Score = score;
            IsGrandCoalition = isGrandCoalition;
            Key = CoalitionMath.KeyOf(memberPartyIds);
        }

        /// <summary>Member party ids, sorted ordinal ascending.</summary>
        public IReadOnlyList<string> MemberPartyIds { get; }

        /// <summary>Largest member; an exact seat tie resolves to the lower party id.</summary>
        public string LeadPartyId { get; }

        public int Seats { get; }

        public double SeatShare { get; }

        /// <summary>True when <see cref="SeatShare"/> reaches <c>coalitions.minSeatShareToGovern</c>.</summary>
        public bool HasMajority { get; }

        /// <summary>Mean platform distance across member pairs, <c>[0,1]</c>. Drives cohesion.</summary>
        public double MeanPairwiseDistance { get; }

        /// <summary>Widest platform gap between two members, <c>[0,1]</c>. Judged against the cap.</summary>
        public double MaxPairwiseDistance { get; }

        /// <summary>The cap this candidate was judged against, including any grand-coalition slack.</summary>
        public double DistanceCap { get; }

        /// <summary>Cohesion this arrangement would have, <c>[0,1]</c>. Also the odds talks succeed.</summary>
        public double Cohesion { get; }

        /// <summary>Ranking score, <c>[0,1]</c>: tuned blend of closeness and size.</summary>
        public double Score { get; }

        /// <summary>
        /// True when no single member can be dropped while the rest still hold a majority. Classic
        /// minimum-winning-coalition logic: parties do not hand out seats at the cabinet table for
        /// votes they do not need. Always true for a candidate without a majority.
        /// </summary>
        public bool IsMinimumWinning { get; internal set; } = true;

        /// <summary>True when this is exactly the chamber's two largest parties.</summary>
        public bool IsGrandCoalition { get; }

        /// <summary>Sorted member ids joined with <c>+</c>. Unique per candidate, so it is a total tiebreak.</summary>
        public string Key { get; }

        public override string ToString() => Key + " (" + LeadPartyId + ", " + Seats + " seats)";

        /// <summary>
        /// Formation order. Deterministic and total — <see cref="Key"/> is unique across candidates, so
        /// the comparison never falls through to the sort algorithm's own (unstable) ordering.
        /// </summary>
        /// <remarks>
        /// A majority beats no majority; a minimum-winning arrangement beats a bloated one; then the
        /// tuned score; then fewer partners; then the id key.
        /// </remarks>
        internal static int Compare(CoalitionCandidate a, CoalitionCandidate b)
        {
            if (a.HasMajority != b.HasMajority) return a.HasMajority ? -1 : 1;
            if (a.IsMinimumWinning != b.IsMinimumWinning) return a.IsMinimumWinning ? -1 : 1;

            int byScore = b.Score.CompareTo(a.Score);
            if (byScore != 0) return byScore;

            int bySize = a.MemberPartyIds.Count.CompareTo(b.MemberPartyIds.Count);
            if (bySize != 0) return bySize;

            return string.CompareOrdinal(a.Key, b.Key);
        }

        internal static readonly Comparison<CoalitionCandidate> Order = Compare;
    }
}
