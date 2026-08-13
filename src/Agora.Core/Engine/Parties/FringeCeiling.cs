using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Parties
{
    /// <summary>One party's maximum share of a bloc's vote, from the <c>fringe</c> packet.</summary>
    public struct PartyCeiling
    {
        public PartyCeiling(string partyId, double ceiling)
        {
            PartyId = partyId ?? "";
            Ceiling = ceiling;
        }

        public string PartyId { get; private set; }

        /// <summary>Maximum share, 0–1.</summary>
        public double Ceiling { get; private set; }
    }

    /// <summary>
    /// The ceilings in force this tick, keyed by party id. Probe-only and immutable once built, so the
    /// affinity hot loop can consult it without copying or sorting per bloc.
    /// </summary>
    /// <remarks>
    /// Entries are held sorted by party id ordinal and looked up by binary search rather than by
    /// dictionary, for the reason spelled out in <c>Agora.Core/CLAUDE.md</c>: a dictionary would work
    /// here today and would become a determinism bug the moment somebody iterated it.
    /// </remarks>
    public sealed class FringeCeilings
    {
        /// <summary>No ceilings in force. The EU path and a disabled packet both pass this.</summary>
        public static readonly FringeCeilings None = new FringeCeilings(new string[0], new double[0]);

        private readonly string[] _partyIds;
        private readonly double[] _ceilings;

        private FringeCeilings(string[] partyIds, double[] ceilings)
        {
            _partyIds = partyIds;
            _ceilings = ceilings;
        }

        public bool IsEmpty { get { return _partyIds.Length == 0; } }

        public int Count { get { return _partyIds.Length; } }

        public bool TryGet(string partyId, out double ceiling)
        {
            ceiling = 0.0;
            if (_partyIds.Length == 0 || string.IsNullOrEmpty(partyId)) return false;

            int i = Array.BinarySearch(_partyIds, partyId, StringComparer.Ordinal);
            if (i < 0) return false;

            ceiling = _ceilings[i];
            return true;
        }

        /// <summary>
        /// Builds from an unordered list. A duplicate party id keeps the lower ceiling — the stricter
        /// reading — rather than whichever entry happened to come last.
        /// </summary>
        public static FringeCeilings FromList(IReadOnlyList<PartyCeiling> ceilings)
        {
            if (ceilings == null || ceilings.Count == 0) return None;

            var ids = new List<string>(ceilings.Count);
            var values = new List<double>(ceilings.Count);

            for (int i = 0; i < ceilings.Count; i++)
            {
                string id = ceilings[i].PartyId;
                if (string.IsNullOrEmpty(id)) continue;

                double c = ceilings[i].Ceiling;
                if (double.IsNaN(c)) continue;
                if (c < 0.0) c = 0.0;
                if (c > 1.0) c = 1.0;

                int at = ids.IndexOf(id);
                if (at >= 0)
                {
                    if (c < values[at]) values[at] = c;
                    continue;
                }

                ids.Add(id);
                values.Add(c);
            }

            if (ids.Count == 0) return None;

            var order = new int[ids.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => string.CompareOrdinal(ids[a], ids[b]));

            var sortedIds = new string[ids.Count];
            var sortedValues = new double[ids.Count];
            for (int i = 0; i < order.Length; i++)
            {
                sortedIds[i] = ids[order[i]];
                sortedValues[i] = values[order[i]];
            }

            return new FringeCeilings(sortedIds, sortedValues);
        }
    }

    /// <summary>
    /// Packet 15 — enforcement of the fringe-party ceiling (<c>politicsmodplan.md</c> §3, "two dominant
    /// parties + weak third parties").
    ///
    /// <para>
    /// Applied to a finished affinity row, in <b>affinity space rather than share space</b>. That is
    /// the whole design. A bloc's shares are a softmax of its affinity row, and the election packet
    /// re-runs that softmax itself from the same affinities
    /// (<c>FptpElection</c> → <c>FptpShareMath.Softmax</c>) rather than reading the aggregated
    /// standings. So expressing the cap as an additive shift on <see cref="BlocAffinity.Affinity"/>
    /// makes one edit cover all three surfaces that report support — city standings, published polls
    /// and election day — with the election packet needing no knowledge of ceilings at all. Capping
    /// the shares instead would need the same logic in three places, and the three would drift.
    /// </para>
    ///
    /// <para>
    /// Pure and stateless: no seeds, no tuning read, no allocation beyond the working arrays. The
    /// caller decides <i>what</i> the ceilings are (<see cref="FringeFailureModel"/>); this decides
    /// only how to impose them.
    /// </para>
    /// </summary>
    public static class FringeCeiling
    {
        /// <summary>
        /// Softmax weights below this are treated as zero. Not a tuning coefficient — it is the point
        /// where <c>exp</c> has underflowed far enough that dividing by it produces noise rather than
        /// a ratio.
        /// </summary>
        private const double NegligibleWeight = 1e-300;

        /// <summary>Slack on the "is this ceiling binding" test, to keep float dust out of the loop.</summary>
        private const double Epsilon = 1e-12;

        /// <summary>
        /// Lowers capped parties' affinity so that the row's softmax puts each at or under its ceiling,
        /// and records the shift in <see cref="BlocAffinity.CeilingComponent"/>.
        /// </summary>
        /// <param name="row">
        /// One bloc's affinities, one entry per party on the ballot. Mutated in place. Order does not
        /// affect the result: the water-filling loop below picks its binding set by comparing shares,
        /// and ties are impossible because a party appears once.
        /// </param>
        /// <param name="ceilings">Ceilings in force. <see cref="FringeCeilings.None"/> is a no-op.</param>
        /// <param name="temperature">
        /// <c>affinity.softmaxTemperature</c>. Must match the temperature the row will actually be
        /// softmaxed at, or the shift will not land where it was aimed.
        /// </param>
        /// <remarks>
        /// <para>
        /// <b>Why water-filling.</b> Shrinking one party's weight raises everyone else's share, which
        /// can push a second capped party back over its own ceiling. So the binding set is grown one
        /// pass at a time until it stops changing, exactly as in water-filling power allocation. At
        /// most <c>n</c> passes, because each pass either adds a party or terminates.
        /// </para>
        /// <para>
        /// <b>Where the surplus goes.</b> Nowhere explicit — it falls out of the softmax. Shrinking
        /// only the capped weights leaves every uncapped weight untouched, so after renormalisation
        /// each uncapped party gains in proportion to what it already had and the ratios between the
        /// majors are preserved exactly. That is both the politically right answer, since a protest
        /// vote that cannot go to the protest party splits the way the rest of the field already
        /// splits, and the arithmetically safe one, since there is no separate redistribution step
        /// that could fail to sum to 1.
        /// </para>
        /// <para>
        /// <b>Failing open.</b> Every degenerate case leaves the row untouched rather than throwing or
        /// producing a nonsense distribution: ceilings that sum to 1 or more, a row where everyone is
        /// capped, weights that have underflowed. A mis-tuned ceiling must not be able to take an
        /// election down — non-negotiable #5's "every effect declares a fallback", applied to a cap.
        /// </para>
        /// </remarks>
        public static void ApplyToRow(IList<BlocAffinity> row, FringeCeilings ceilings, double temperature)
        {
            if (row == null || row.Count == 0) return;
            if (ceilings == null || ceilings.IsEmpty) return;

            int n = row.Count;

            // Which entries are capped, and at what. Collected first so the degenerate exits below can
            // be taken before any arithmetic.
            var capped = new bool[n];
            var cap = new double[n];
            int cappedCount = 0;

            for (int i = 0; i < n; i++)
            {
                BlocAffinity a = row[i];
                if (a == null) continue;

                double c;
                if (!ceilings.TryGet(a.PartyId, out c)) continue;

                capped[i] = true;
                cap[i] = c;
                cappedCount++;
            }

            if (cappedCount == 0) return;

            // Nothing to redistribute onto. A row of nothing but fringe parties is not a situation the
            // ceiling can express — someone has to hold the other 97% — so it is left alone.
            if (cappedCount == n) return;

            if (temperature <= 0.0 || double.IsNaN(temperature) || double.IsInfinity(temperature))
            {
                ApplyDegenerate(row, capped, n);
                return;
            }

            // Softmax weights, max-subtracted so a large affinity cannot overflow. These are
            // proportional to the shares, which is all the loop below needs.
            double max = double.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                if (row[i] == null) continue;
                double v = row[i].Affinity;
                if (!double.IsNaN(v) && v > max) max = v;
            }
            if (double.IsInfinity(max)) return;

            var weight = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (row[i] == null) continue;

                double e = Math.Exp((row[i].Affinity - max) / temperature);
                weight[i] = (double.IsNaN(e) || double.IsInfinity(e) || e < 0.0) ? 0.0 : e;
            }

            var binding = new bool[n];

            // Water-filling. Each pass recomputes what the uncapped parties would receive once the
            // currently-binding ceilings are honoured, and promotes any capped party still over its
            // own ceiling. Bounded by n because a party is promoted at most once.
            for (int pass = 0; pass < n; pass++)
            {
                double capMass = 0.0;
                double freeMass = 0.0;

                for (int i = 0; i < n; i++)
                {
                    if (binding[i]) capMass += cap[i];
                    else freeMass += weight[i];
                }

                double headroom = 1.0 - capMass;
                if (headroom <= 0.0 || freeMass <= NegligibleWeight) return;  // fail open

                bool promoted = false;
                for (int i = 0; i < n; i++)
                {
                    if (!capped[i] || binding[i]) continue;
                    if (weight[i] <= 0.0) continue;

                    double share = weight[i] * headroom / freeMass;
                    if (share > cap[i] + Epsilon)
                    {
                        binding[i] = true;
                        promoted = true;
                    }
                }

                if (!promoted) break;
            }

            // Recompute the final split once, then convert each binding party's target share into the
            // affinity shift that produces it.
            double finalCapMass = 0.0;
            double finalFreeMass = 0.0;
            for (int i = 0; i < n; i++)
            {
                if (binding[i]) finalCapMass += cap[i];
                else finalFreeMass += weight[i];
            }

            double finalHeadroom = 1.0 - finalCapMass;
            if (finalHeadroom <= 0.0 || finalFreeMass <= NegligibleWeight) return;

            for (int i = 0; i < n; i++)
            {
                if (!binding[i] || weight[i] <= NegligibleWeight) continue;

                // The factor this party's weight must be multiplied by to land exactly on its ceiling
                // once the uncapped parties share the remaining headroom.
                double factor = (cap[i] / finalHeadroom) * (finalFreeMass / weight[i]);
                if (factor <= 0.0 || double.IsNaN(factor) || double.IsInfinity(factor)) continue;
                if (factor >= 1.0) continue;   // already under its ceiling; nothing to do

                // exp(delta / T) == factor, so delta = T * ln(factor). Negative for factor < 1.
                double delta = temperature * Math.Log(factor);
                if (double.IsNaN(delta) || double.IsInfinity(delta)) continue;

                row[i].Affinity += delta;
                row[i].CeilingComponent += delta;
            }
        }

        /// <summary>
        /// Winner-take-all fallback, for the same non-positive temperature the softmax itself treats as
        /// "no dispersion". Every capped party is dropped to the row minimum, so it cannot be the
        /// unique maximum and cannot take the bloc — unless every party is capped, which the caller has
        /// already excluded.
        /// </summary>
        /// <remarks>
        /// Unreachable under shipped tuning; present because a tuning typo must degrade rather than
        /// divide by zero, and because "the ceiling silently stops applying" is a worse failure than a
        /// blunt one.
        /// </remarks>
        private static void ApplyDegenerate(IList<BlocAffinity> row, bool[] capped, int n)
        {
            double min = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                if (row[i] == null) continue;
                double v = row[i].Affinity;
                if (!double.IsNaN(v) && v < min) min = v;
            }

            if (double.IsInfinity(min)) return;

            for (int i = 0; i < n; i++)
            {
                if (!capped[i] || row[i] == null) continue;

                double delta = min - row[i].Affinity;
                if (delta >= 0.0) continue;

                row[i].Affinity += delta;
                row[i].CeilingComponent += delta;
            }
        }
    }
}
