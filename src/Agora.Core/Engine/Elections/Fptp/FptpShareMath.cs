using System;
using System.Collections.Generic;
using Agora.Core.Determinism;

namespace Agora.Core.Engine.Elections.Fptp
{
    /// <summary>
    /// Share arithmetic for the first-past-the-post packet. Every routine here is pure and operates
    /// on a parallel array indexed by ballot position, which is why nothing in this file needs a
    /// dictionary: ballot position is already the contractual party-id ordinal ordering, so no result
    /// can depend on hash iteration order.
    /// </summary>
    /// <remarks>
    /// Deliberately internal. The election packet's public surface is
    /// <see cref="FptpElection.Run"/>, <see cref="FptpSeatMath"/> and <see cref="FptpCalendar"/>;
    /// widening it would let other packets couple to arithmetic that is free to change.
    /// </remarks>
    internal static class FptpShareMath
    {
        /// <summary>netstandard2.0 has no <c>Math.Clamp</c>.</summary>
        internal static double Clamp(double v, double min, double max) =>
            v < min ? min : (v > max ? max : v);

        /// <summary>
        /// Rescales in place so the entries are non-negative and sum to 1. NaN, infinity and negative
        /// entries are treated as zero rather than propagating — a single NaN share would otherwise
        /// poison the whole result and turn an election into a silent no-op.
        /// </summary>
        internal static void Normalize(double[] shares)
        {
            int n = shares.Length;
            if (n == 0) return;

            double sum = 0.0;
            for (int i = 0; i < n; i++)
            {
                double v = shares[i];
                if (double.IsNaN(v) || double.IsInfinity(v) || v < 0.0) v = 0.0;
                shares[i] = v;
                sum += v;
            }

            if (sum <= 0.0)
            {
                double even = 1.0 / n;
                for (int i = 0; i < n; i++) shares[i] = even;
                return;
            }

            for (int i = 0; i < n; i++) shares[i] /= sum;
        }

        /// <summary>
        /// Turns a bloc's affinity scores into vote shares with a Boltzmann softmax at
        /// <c>affinity.softmaxTemperature</c>. The maximum is subtracted before exponentiating so a
        /// large affinity cannot overflow to infinity and collapse the whole bloc to NaN.
        /// </summary>
        /// <remarks>
        /// A non-positive or non-finite temperature degenerates to winner-take-all with ties split
        /// evenly, rather than dividing by zero. That keeps a mis-tuned file from crashing an
        /// election; the caller sees a plausible landslide and the tuning warning explains why.
        /// </remarks>
        internal static double[] Softmax(double[] scores, double temperature)
        {
            int n = scores.Length;
            var result = new double[n];
            if (n == 0) return result;

            if (!(temperature > 0.0) || double.IsNaN(temperature) || double.IsInfinity(temperature))
            {
                double best = double.NegativeInfinity;
                for (int i = 0; i < n; i++)
                    if (!double.IsNaN(scores[i]) && scores[i] > best) best = scores[i];

                int ties = 0;
                for (int i = 0; i < n; i++)
                    if (scores[i] >= best) ties++;

                if (ties == 0)
                {
                    double even = 1.0 / n;
                    for (int i = 0; i < n; i++) result[i] = even;
                    return result;
                }

                for (int i = 0; i < n; i++) result[i] = scores[i] >= best ? 1.0 / ties : 0.0;
                return result;
            }

            double max = double.NegativeInfinity;
            for (int i = 0; i < n; i++)
                if (!double.IsNaN(scores[i]) && scores[i] > max) max = scores[i];

            double sum = 0.0;
            for (int i = 0; i < n; i++)
            {
                double e = Math.Exp((scores[i] - max) / temperature);
                result[i] = e;
                sum += e;
            }

            if (sum <= 0.0 || double.IsNaN(sum) || double.IsInfinity(sum))
            {
                double even = 1.0 / n;
                for (int i = 0; i < n; i++) result[i] = even;
                return result;
            }

            for (int i = 0; i < n; i++) result[i] /= sum;
            return result;
        }

        /// <summary>
        /// Ballot indices ordered by share descending, ties broken by ballot position ascending.
        /// </summary>
        /// <remarks>
        /// The comparison is a total order — ballot positions are unique — which matters because
        /// <see cref="Array.Sort{T}(T[], Comparison{T})"/> is an unstable introsort. A comparison that
        /// can return 0 for two distinct entries would let the result depend on the sort's internal
        /// pivot choice, which is exactly the kind of quiet non-determinism §2.3 forbids.
        /// </remarks>
        internal static int[] RankOrder(double[] shares)
        {
            int n = shares.Length;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;

            Array.Sort(order, (a, b) =>
            {
                int c = shares[b].CompareTo(shares[a]);
                return c != 0 ? c : a.CompareTo(b);
            });

            return order;
        }

        /// <summary>
        /// The Duverger squeeze: under FPTP, support for a party that cannot win the seat migrates to
        /// whichever of the top two it can still influence.
        /// </summary>
        /// <param name="shares">Shares by ballot position; modified in place, not renormalised.</param>
        /// <param name="penalty"><c>electionsFptp.thirdPartyPenalty</c> — the fraction that migrates.</param>
        /// <param name="contentionThreshold">
        /// <c>affinity.tacticalVotingThresholdFptp</c> — a third party within this distance of second
        /// place is still contending and is left alone. Only support further behind than this moves.
        /// </param>
        /// <remarks>
        /// <para>
        /// Applied per contest (each district, and the mayoral race) because tactical voting is a
        /// judgement about the local field, not the national one. That is what produces the
        /// characteristic FPTP pattern of a third party polling respectably city-wide while winning
        /// nothing.
        /// </para>
        /// <para>
        /// The migrated pool splits between the top two in proportion to their own shares, not by
        /// ideological proximity. A tactical vote is a bet on who can win, so it follows viability;
        /// the second-choice question is asked instead in the mayoral runoff, which does split by
        /// proximity. The affinity packet exposes a proximity-weighted variant
        /// (<c>AffinityEngine.ApplyTacticalVoting</c>) operating on bloc-level
        /// <c>PartyVoteShare</c> lists rather than on a contest's aggregated shares; if the two are
        /// ever consolidated, that difference in level — bloc versus contest — is the thing to
        /// resolve first, because the squeeze here must run after the district swing and the
        /// mayoral coattails, which do not exist at bloc level.
        /// </para>
        /// </remarks>
        internal static void ApplyTacticalSqueeze(double[] shares, double penalty, double contentionThreshold)
        {
            int n = shares.Length;
            if (n < 3) return;                       // with two candidates there is nowhere to squeeze to
            if (!(penalty > 0.0)) return;

            int[] order = RankOrder(shares);
            int first = order[0];
            int second = order[1];
            double secondShare = shares[second];
            double cut = FptpShareMath.Clamp(penalty, 0.0, 1.0);

            double pool = 0.0;
            for (int r = 2; r < n; r++)
            {
                int i = order[r];
                if (shares[i] > secondShare - contentionThreshold) continue;  // still in contention

                double moved = shares[i] * cut;
                shares[i] -= moved;
                pool += moved;
            }

            if (pool <= 0.0) return;

            double topTwo = shares[first] + shares[second];
            if (topTwo <= 0.0)
            {
                shares[first] += pool;
                return;
            }

            shares[first] += pool * (shares[first] / topTwo);
            shares[second] += pool * (shares[second] / topTwo);
        }

        /// <summary>
        /// Zeroes shares below <c>affinity.minPartyShare</c> and renormalises, so rounding dust is
        /// never reported as a party polling 0.03%.
        /// </summary>
        internal static void ZeroTinyShares(double[] shares, double minShare)
        {
            if (!(minShare > 0.0)) return;

            bool changed = false;
            for (int i = 0; i < shares.Length; i++)
            {
                if (shares[i] > 0.0 && shares[i] < minShare)
                {
                    shares[i] = 0.0;
                    changed = true;
                }
            }

            if (changed) Normalize(shares);
        }

        /// <summary>
        /// Largest-remainder apportionment of <paramref name="total"/> whole units across
        /// <paramref name="shares"/>. Used for both vote counts and at-large seats.
        /// </summary>
        /// <param name="tieRng">
        /// Optional. When supplied, equal remainders are ordered by a seeded permutation instead of by
        /// ballot position. Without it, a genuine dead heat would hand the spare unit to whichever
        /// party sorts first alphabetically — a systematic bias that would decide real elections.
        /// The permutation is drawn once, so the draw is independent of how many ties occur.
        /// </param>
        /// <remarks>
        /// Vote counts are integers by contract (§6): a one-vote margin has to be representable, and
        /// the per-party counts must sum exactly to the district's votes cast. Largest remainder is
        /// the only cheap method that guarantees both.
        /// </remarks>
        internal static int[] Apportion(double[] shares, int total, DeterministicRng? tieRng)
        {
            int n = shares.Length;
            var units = new int[n];
            if (n == 0 || total <= 0) return units;

            var remainder = new double[n];
            int assigned = 0;

            for (int i = 0; i < n; i++)
            {
                double q = shares[i] * total;
                if (double.IsNaN(q) || q < 0.0) q = 0.0;
                if (q > total) q = total;

                int whole = (int)Math.Floor(q);
                units[i] = whole;
                assigned += whole;
                remainder[i] = q - whole;
            }

            int left = total - assigned;
            if (left <= 0) return units;

            var priority = new int[n];
            if (tieRng != null)
            {
                var permutation = new List<int>(n);
                for (int i = 0; i < n; i++) permutation.Add(i);
                tieRng.Shuffle(permutation);
                for (int rank = 0; rank < n; rank++) priority[permutation[rank]] = rank;
            }
            else
            {
                for (int i = 0; i < n; i++) priority[i] = i;
            }

            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;

            Array.Sort(order, (a, b) =>
            {
                int c = remainder[b].CompareTo(remainder[a]);
                return c != 0 ? c : priority[a].CompareTo(priority[b]);
            });

            // `left` cannot exceed n for shares that sum to 1, but wrapping keeps a mis-normalised
            // input from throwing instead of merely being slightly odd.
            for (int r = 0; r < left; r++) units[order[r % n]]++;

            return units;
        }
    }
}
