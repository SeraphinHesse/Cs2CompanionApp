using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Factions
{
    /// <summary>
    /// The city's political weather, read straight off the bloc set: how aggrieved the electorate is
    /// per issue, how much that issue matters, and where the median voter sits.
    ///
    /// <para>
    /// Factions form around grievances, so this is the one input the faction packet needs beyond the
    /// parties themselves. It is a pure function of the bloc list — no snapshot, no clock, no state.
    /// </para>
    /// </summary>
    public readonly struct IssueClimate
    {
        /// <summary>
        /// Per issue, the weight-weighted mean discontent of the voters who care about it, in
        /// <c>[0, 1]</c>. Directly comparable to <c>factions.revivalGrievanceThreshold</c>.
        /// </summary>
        public IssueWeights Grievance { get; }

        /// <summary>
        /// <see cref="Grievance"/> scaled by how heavily the electorate weights the issue. Ranking
        /// key for "which issues do factions form around" — an issue nobody cares about cannot be
        /// salient however aggrieved its few partisans are.
        /// </summary>
        public IssueWeights Salience { get; }

        /// <summary>Electoral-weight-weighted mean of every bloc's ideal point.</summary>
        public IssuePosition MeanIdeal { get; }

        /// <summary>Total electoral weight the climate was computed from. Zero means "no data".</summary>
        public double TotalWeight { get; }

        public IssueClimate(IssueWeights grievance, IssueWeights salience, IssuePosition meanIdeal, double totalWeight)
        {
            Grievance = grievance;
            Salience = salience;
            MeanIdeal = meanIdeal;
            TotalWeight = totalWeight;
        }

        public bool HasData => TotalWeight > 0.0;

        /// <summary>No blocs: zero grievance everywhere, centre ideal. Never NaN.</summary>
        public static IssueClimate Neutral =>
            new IssueClimate(new IssueWeights(0, 0, 0, 0, 0, 0), new IssueWeights(0, 0, 0, 0, 0, 0),
                             IssuePosition.Centre, 0.0);

        /// <summary>
        /// Builds the climate from raw blocs, summing in (district, bloc ordinal) order.
        /// </summary>
        /// <remarks>
        /// Discontent is folded in here rather than in <see cref="BlocDemography"/>: that type is
        /// shared with the constituency partition and stays a purely demographic fold.
        /// </remarks>
        public static IssueClimate FromBlocs(IReadOnlyList<Bloc>? blocs)
        {
            if (blocs == null || blocs.Count == 0) return Neutral;

            var sorted = new List<Bloc>(blocs.Count);
            for (int i = 0; i < blocs.Count; i++)
                if (blocs[i] != null) sorted.Add(blocs[i]);
            if (sorted.Count == 0) return Neutral;

            sorted.Sort(CompareBlocs);

            double totalEligible = 0.0;
            for (int i = 0; i < sorted.Count; i++)
                totalEligible += Math.Max(0, sorted[i].EligibleVoters);
            bool useEligible = totalEligible > 0.0;

            var weightMass = new double[Issues.Count];   // Σ w_b[i] · weight_b
            var grievedMass = new double[Issues.Count];  // Σ w_b[i] · weight_b · discontent_b
            var idealMass = new double[Issues.Count];    // Σ ideal_b[i] · weight_b
            double totalWeight = 0.0;

            for (int i = 0; i < sorted.Count; i++)
            {
                Bloc b = sorted[i];
                double weight = useEligible ? Math.Max(0, b.EligibleVoters) : Math.Max(0, b.Population);
                if (weight <= 0.0) continue;

                double discontent = IssueVectors.Clamp01(
                    IssueVectors.IsFinite(b.Discontent) ? b.Discontent : 0.0);

                totalWeight += weight;
                for (int n = 0; n < Issues.All.Count; n++)
                {
                    Issue issue = Issues.All[n];
                    double iw = b.Weights[issue];
                    if (!IssueVectors.IsFinite(iw) || iw < 0.0) iw = 0.0;

                    weightMass[n] += iw * weight;
                    grievedMass[n] += iw * weight * discontent;

                    double ideal = b.Ideal[issue];
                    idealMass[n] += (IssueVectors.IsFinite(ideal) ? ideal : 0.0) * weight;
                }
            }

            if (totalWeight <= 0.0) return Neutral;

            var grievance = new double[Issues.Count];
            var salience = new double[Issues.Count];
            var meanIdeal = new double[Issues.Count];

            for (int n = 0; n < Issues.Count; n++)
            {
                grievance[n] = weightMass[n] > 0.0 ? IssueVectors.Clamp01(grievedMass[n] / weightMass[n]) : 0.0;
                double meanWeight = weightMass[n] / totalWeight;
                salience[n] = grievance[n] * meanWeight;
                meanIdeal[n] = idealMass[n] / totalWeight;
            }

            return new IssueClimate(
                IssueVectors.Weights(grievance),
                IssueVectors.Weights(salience),
                IssueVectors.Position(meanIdeal).Clamped(),
                totalWeight);
        }

        /// <summary>
        /// The six issues ordered by descending <see cref="Salience"/>, ties broken by
        /// <see cref="Issue"/> declaration order so the result is a total order.
        /// </summary>
        public IReadOnlyList<Issue> IssuesBySalience()
        {
            var order = new Issue[Issues.Count];
            for (int i = 0; i < Issues.Count; i++) order[i] = Issues.All[i];

            IssueWeights s = Salience;
            Array.Sort(order, (a, b) =>
            {
                int c = s[b].CompareTo(s[a]);
                return c != 0 ? c : ((int)a).CompareTo((int)b);
            });
            return order;
        }

        /// <summary>The single most salient issue. <see cref="Issue.Services"/> when there is no data.</summary>
        public Issue TopSalient() => IssuesBySalience()[0];

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
