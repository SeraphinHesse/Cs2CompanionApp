using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Factions
{
    /// <summary>
    /// One demographic slice of the whole city: a <see cref="BlocKey"/> with the same key summed
    /// across every district.
    /// </summary>
    /// <remarks>
    /// Factions are city-wide (they live inside a party, not inside a district), so their
    /// constituency is expressed in <see cref="BlocKey"/>s — see <c>Faction.CoreBlocs</c>, which
    /// carries no district id.
    /// </remarks>
    internal readonly struct BlocSlice
    {
        internal BlocKey Key { get; }

        /// <summary>Electoral weight of the slice: eligible voters where the city has any, head count
        /// otherwise. Never negative.</summary>
        internal double Weight { get; }

        internal IssuePosition Ideal { get; }

        internal IssueWeights Weights { get; }

        internal BlocSlice(BlocKey key, double weight, IssuePosition ideal, IssueWeights weights)
        {
            Key = key;
            Weight = weight;
            Ideal = ideal;
            Weights = weights;
        }
    }

    /// <summary>
    /// The city's blocs folded down to one row per <see cref="BlocKey"/>, in a fixed order.
    /// </summary>
    internal sealed class BlocDemography
    {
        internal static readonly BlocDemography Empty = new BlocDemography(new List<BlocSlice>(), 0.0);

        private readonly List<BlocSlice> _slices;

        internal IReadOnlyList<BlocSlice> Slices => _slices;

        /// <summary>Sum of <see cref="BlocSlice.Weight"/> over every slice.</summary>
        internal double TotalWeight { get; }

        private BlocDemography(List<BlocSlice> slices, double totalWeight)
        {
            _slices = slices;
            TotalWeight = totalWeight;
        }

        /// <summary>
        /// Folds a bloc list into per-<see cref="BlocKey"/> slices.
        /// </summary>
        /// <remarks>
        /// The input is copied and sorted by (district id, bloc ordinal, population) before anything is
        /// summed. <c>PoliticalState.Blocs</c> is contractually in that order already, but a caller
        /// handing over an unsorted list would otherwise change the floating-point result without
        /// changing the data — the classic silent determinism defect.
        /// <para>
        /// Slices with zero weight are dropped: they are the disenfranchised child/teen bands, and
        /// keeping them would put non-voters into a faction's core constituency.
        /// </para>
        /// </remarks>
        internal static BlocDemography FromBlocs(IReadOnlyList<Bloc>? blocs)
        {
            if (blocs == null || blocs.Count == 0) return Empty;

            var sorted = new List<Bloc>(blocs.Count);
            for (int i = 0; i < blocs.Count; i++)
                if (blocs[i] != null) sorted.Add(blocs[i]);
            if (sorted.Count == 0) return Empty;

            sorted.Sort(CompareBlocs);

            double totalEligible = 0.0;
            for (int i = 0; i < sorted.Count; i++)
                totalEligible += Math.Max(0, sorted[i].EligibleVoters);
            bool useEligible = totalEligible > 0.0;

            // Dense accumulators indexed by BlocKey.Ordinal — never a dictionary, so the walk order
            // below is the ordinal order and nothing else.
            var weight = new double[BlocAxes.BlocCount];
            var ideal = new double[BlocAxes.BlocCount][];
            var weights = new double[BlocAxes.BlocCount][];
            for (int k = 0; k < BlocAxes.BlocCount; k++)
            {
                ideal[k] = new double[Issues.Count];
                weights[k] = new double[Issues.Count];
            }

            for (int i = 0; i < sorted.Count; i++)
            {
                Bloc b = sorted[i];
                double w = useEligible ? Math.Max(0, b.EligibleVoters) : Math.Max(0, b.Population);
                if (w <= 0.0) continue;

                int ord = b.Key.Ordinal;
                if (ord < 0 || ord >= BlocAxes.BlocCount) continue;

                weight[ord] += w;
                for (int n = 0; n < Issues.All.Count; n++)
                {
                    Issue issue = Issues.All[n];
                    ideal[ord][n] += b.Ideal[issue] * w;
                    double iw = b.Weights[issue];
                    weights[ord][n] += (iw > 0.0 ? iw : 0.0) * w;
                }
            }

            var slices = new List<BlocSlice>();
            double total = 0.0;
            for (int k = 0; k < BlocAxes.BlocCount; k++)
            {
                double w = weight[k];
                if (w <= 0.0) continue;

                var idealMean = new double[Issues.Count];
                var weightMean = new double[Issues.Count];
                for (int n = 0; n < Issues.Count; n++)
                {
                    idealMean[n] = ideal[k][n] / w;
                    weightMean[n] = weights[k][n] / w;
                }

                slices.Add(new BlocSlice(
                    BlocAxes.AllKeys[k],
                    w,
                    IssueVectors.Position(idealMean).Clamped(),
                    IssueVectors.Weights(weightMean)));
                total += w;
            }

            return slices.Count == 0 ? Empty : new BlocDemography(slices, total);
        }

        private static int CompareBlocs(Bloc a, Bloc b)
        {
            int c = string.CompareOrdinal(a.DistrictId ?? "", b.DistrictId ?? "");
            if (c != 0) return c;
            c = a.Key.Ordinal.CompareTo(b.Key.Ordinal);
            if (c != 0) return c;
            c = a.Population.CompareTo(b.Population);
            return c != 0 ? c : a.EligibleVoters.CompareTo(b.EligibleVoters);
        }
    }
}
