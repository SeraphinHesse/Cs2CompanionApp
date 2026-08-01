using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Factions
{
    /// <summary>
    /// One faction's demographic base: the bloc keys it wins inside its own party, and the share of
    /// the party's support that implies.
    /// </summary>
    public sealed class FactionConstituency
    {
        public string FactionId { get; set; } = "";

        /// <summary>Share of the party's internal support this base is worth, 0–1. Shares over one
        /// party's factions sum to 1.</summary>
        public double TargetShare { get; set; }

        /// <summary>Bloc keys whose nearest faction (inside this party) is this one, sorted by
        /// <see cref="BlocKey.Ordinal"/> per the contract.</summary>
        public List<BlocKey> CoreBlocs { get; set; } = new List<BlocKey>();

        /// <summary>Electoral weight behind <see cref="CoreBlocs"/>. Diagnostic only.</summary>
        public double Weight { get; set; }
    }

    /// <summary>
    /// Internal support: who backs which faction inside a party, and how that moves over time.
    ///
    /// <para>
    /// The model is a nearest-platform partition of the city's blocs. Each bloc key backs whichever of
    /// the party's factions sits closest to its ideal point, weighted by what that bloc cares about.
    /// A faction's support is the electoral weight it wins, normalised across the party — which makes
    /// "factions have their own demographic support" (§3) literally true rather than a stored number
    /// with no referent.
    /// </para>
    /// </summary>
    public static class FactionSupport
    {
        /// <summary>A faction that is on the field: it can hold support, take dominance and write platform.</summary>
        public static bool IsEligible(Faction? f) =>
            f != null && (f.Status == FactionStatus.Active
                          || f.Status == FactionStatus.Endangered
                          || f.Status == FactionStatus.Revived);

        /// <summary>
        /// Partitions the city's blocs among one party's eligible factions.
        /// </summary>
        /// <remarks>
        /// Ties (two factions equidistant from a bloc) go to the lower faction id. That is a total
        /// order, so no seeded draw is needed — and a coin flip here would be re-rolled every cycle,
        /// making support jitter for no modelled reason.
        /// </remarks>
        public static List<FactionConstituency> Constituencies(
            IReadOnlyList<Faction>? partyFactions,
            IReadOnlyList<Bloc>? blocs)
        {
            List<Faction> eligible = EligibleSortedById(partyFactions);
            var result = new List<FactionConstituency>(eligible.Count);
            for (int i = 0; i < eligible.Count; i++)
                result.Add(new FactionConstituency { FactionId = eligible[i].Id });

            if (eligible.Count == 0) return result;

            BlocDemography demography = BlocDemography.FromBlocs(blocs);

            if (demography.TotalWeight <= 0.0)
            {
                EvenShares(result);
                return result;
            }

            double total = 0.0;
            IReadOnlyList<BlocSlice> slices = demography.Slices;

            for (int s = 0; s < slices.Count; s++)
            {
                BlocSlice slice = slices[s];

                int best = -1;
                double bestAffinity = double.NegativeInfinity;
                for (int f = 0; f < eligible.Count; f++)
                {
                    double affinity = Affinity(slice, eligible[f].Platform);
                    if (affinity > bestAffinity)
                    {
                        bestAffinity = affinity;
                        best = f;   // eligible is id-sorted, so the first max wins the tie
                    }
                }

                if (best < 0) continue;

                double mass = slice.Weight * IssueVectors.Clamp01(bestAffinity);
                result[best].CoreBlocs.Add(slice.Key);
                result[best].Weight += mass;
                total += mass;
            }

            for (int i = 0; i < result.Count; i++)
                result[i].CoreBlocs.Sort((a, b) => a.Ordinal.CompareTo(b.Ordinal));

            if (total <= 0.0 || !IssueVectors.IsFinite(total))
            {
                EvenShares(result);
                return result;
            }

            double running = 0.0;
            for (int i = 0; i < result.Count; i++)
            {
                if (i == result.Count - 1)
                {
                    result[i].TargetShare = IssueVectors.Clamp01(1.0 - running);
                }
                else
                {
                    double share = IssueVectors.Clamp01(result[i].Weight / total);
                    result[i].TargetShare = share;
                    running += share;
                }
            }

            return result;
        }

        /// <summary>
        /// How well one demographic slice fits a platform, in <c>[0, 1]</c>. <c>1 − distance</c>, with
        /// the slice's own issue weights deciding what "close" means for it.
        /// </summary>
        internal static double Affinity(BlocSlice slice, IssuePosition platform) =>
            1.0 - slice.Ideal.WeightedDistance(platform, slice.Weights);

        /// <summary>
        /// Sets <c>InternalSupport</c> and <c>CoreBlocs</c> straight from the partition. Used at
        /// generation, when the party has no support history to drift from.
        /// </summary>
        public static void ApplyTargets(IReadOnlyList<Faction>? partyFactions, IReadOnlyList<Bloc>? blocs)
        {
            List<FactionConstituency> constituencies = Constituencies(partyFactions, blocs);
            Assign(partyFactions, constituencies, /*useTargetAsSupport:*/ true);
        }

        /// <summary>
        /// Moves each faction a capped step toward the support its demographic base implies, then
        /// renormalises the party to sum to exactly 1.
        /// </summary>
        /// <remarks>
        /// Grievance shifts blocs between factions; this is where a faction that owns a rising
        /// grievance grows and eventually takes the party over. The per-cycle cap is what stops one
        /// bad year handing a party to its fringe.
        /// </remarks>
        public static void ApplyDrift(IReadOnlyList<Faction>? partyFactions,
                                      IReadOnlyList<Bloc>? blocs,
                                      EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            List<Faction> eligible = EligibleSortedById(partyFactions);
            if (eligible.Count == 0) return;

            List<FactionConstituency> constituencies = Constituencies(partyFactions, blocs);

            for (int i = 0; i < eligible.Count; i++)
            {
                FactionConstituency? c = FindConstituency(constituencies, eligible[i].Id);
                double current = IssueVectors.Clamp01(
                    IssueVectors.IsFinite(eligible[i].InternalSupport) ? eligible[i].InternalSupport : 0.0);
                double target = c == null ? current : c.TargetShare;

                eligible[i].InternalSupport = IssueVectors.Clamp01(current + DriftStep(current, target, tuning));
                if (c != null) eligible[i].CoreBlocs = new List<BlocKey>(c.CoreBlocs);
            }

            Normalize(eligible);
        }

        /// <summary>
        /// The support change one cycle may produce: a fraction
        /// <c>factions.supportDriftPerCycle</c> of the gap, clamped to
        /// <c>factions.supportDriftCapPerCycle</c> in either direction.
        /// </summary>
        public static double DriftStep(double current, double target, EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            double cap = Math.Abs(tuning.Factions.SupportDriftCapPerCycle);
            double gap = target - current;
            if (!IssueVectors.IsFinite(gap)) return 0.0;

            double step = tuning.Factions.SupportDriftPerCycle * gap;
            return IssueVectors.Clamp(step, -cap, cap);
        }

        /// <summary>
        /// Rescales a party's eligible factions so their support sums to exactly 1. The residual lands
        /// on the last faction by id, so the sum is exact rather than 0.9999999999999999.
        /// </summary>
        public static void Normalize(IReadOnlyList<Faction>? partyFactions)
        {
            List<Faction> eligible = EligibleSortedById(partyFactions);
            if (eligible.Count == 0) return;

            double sum = 0.0;
            for (int i = 0; i < eligible.Count; i++)
            {
                double v = eligible[i].InternalSupport;
                if (!IssueVectors.IsFinite(v) || v < 0.0) v = 0.0;
                eligible[i].InternalSupport = v;
                sum += v;
            }

            if (sum <= 0.0)
            {
                double even = 1.0 / eligible.Count;
                double runningEven = 0.0;
                for (int i = 0; i < eligible.Count; i++)
                {
                    if (i == eligible.Count - 1) eligible[i].InternalSupport = 1.0 - runningEven;
                    else { eligible[i].InternalSupport = even; runningEven += even; }
                }
                return;
            }

            double running = 0.0;
            for (int i = 0; i < eligible.Count; i++)
            {
                if (i == eligible.Count - 1)
                {
                    eligible[i].InternalSupport = IssueVectors.Clamp01(1.0 - running);
                }
                else
                {
                    double share = IssueVectors.Clamp01(eligible[i].InternalSupport / sum);
                    eligible[i].InternalSupport = share;
                    running += share;
                }
            }
        }

        /// <summary>Eligible factions of a party, sorted by id. The canonical iteration order for
        /// everything in this packet.</summary>
        public static List<Faction> EligibleSortedById(IReadOnlyList<Faction>? factions)
        {
            var list = new List<Faction>();
            if (factions == null) return list;
            for (int i = 0; i < factions.Count; i++)
                if (IsEligible(factions[i])) list.Add(factions[i]);
            list.Sort(FactionIds.ByIdComparison);
            return list;
        }

        internal static FactionConstituency? FindConstituency(List<FactionConstituency> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.CompareOrdinal(list[i].FactionId, id) == 0) return list[i];
            return null;
        }

        private static void Assign(IReadOnlyList<Faction>? partyFactions,
                                   List<FactionConstituency> constituencies,
                                   bool useTargetAsSupport)
        {
            List<Faction> eligible = EligibleSortedById(partyFactions);
            for (int i = 0; i < eligible.Count; i++)
            {
                FactionConstituency? c = FindConstituency(constituencies, eligible[i].Id);
                if (c == null) continue;
                eligible[i].CoreBlocs = new List<BlocKey>(c.CoreBlocs);
                if (useTargetAsSupport) eligible[i].InternalSupport = c.TargetShare;
            }
            Normalize(eligible);
        }

        private static void EvenShares(List<FactionConstituency> result)
        {
            if (result.Count == 0) return;
            double even = 1.0 / result.Count;
            double running = 0.0;
            for (int i = 0; i < result.Count; i++)
            {
                if (i == result.Count - 1) result[i].TargetShare = 1.0 - running;
                else { result[i].TargetShare = even; running += even; }
            }
        }
    }
}
