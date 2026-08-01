using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Factions
{
    /// <summary>
    /// Which faction is responsible for the party's stance on one issue, and by how much.
    /// </summary>
    /// <remarks>
    /// This is the "which faction owns which issue positions" answer. The owner is the faction whose
    /// weighted stance moved the blended component furthest from centre — i.e. the one a journalist
    /// would name when asking why the party is where it is on transit.
    /// </remarks>
    public readonly struct IssueAuthorship
    {
        public Issue Issue { get; }

        /// <summary>Owning faction id. Empty only when the party has no eligible factions.</summary>
        public string FactionId { get; }

        /// <summary>That faction's own stance on the issue.</summary>
        public double Position { get; }

        /// <summary>Its signed contribution to the blended party stance (blend weight × position).</summary>
        public double Contribution { get; }

        public IssueAuthorship(Issue issue, string factionId, double position, double contribution)
        {
            Issue = issue;
            FactionId = factionId;
            Position = position;
            Contribution = contribution;
        }
    }

    /// <summary>
    /// The platform one party's factions wrote this cycle, plus the per-issue attribution.
    /// </summary>
    public sealed class PlatformAuthorship
    {
        public string PartyId { get; set; } = "";

        /// <summary>The authored platform, already clamped to <c>[-1, +1]</c>.</summary>
        public IssuePosition Platform { get; set; } = IssuePosition.Centre;

        /// <summary>Null when no faction cleared <c>factions.dominanceThreshold</c>.</summary>
        public string? DominantFactionId { get; set; }

        /// <summary>Blend weight actually applied to each eligible faction, in
        /// <c>FactionSupport.EligibleSortedById</c> order. Sums to 1.</summary>
        public List<FactionBlendWeight> Weights { get; set; } = new List<FactionBlendWeight>();

        /// <summary>One entry per issue, in <c>Issues.All</c> order.</summary>
        public List<IssueAuthorship> Issues { get; set; } = new List<IssueAuthorship>();
    }

    public readonly struct FactionBlendWeight
    {
        public string FactionId { get; }
        public double Weight { get; }

        public FactionBlendWeight(string factionId, double weight)
        {
            FactionId = factionId;
            Weight = weight;
        }
    }

    /// <summary>
    /// Faction platforms: how far a faction stands from its party, what it demands, and how the
    /// party's own platform is written from its factions.
    /// </summary>
    public static class FactionPlatform
    {
        /// <summary>
        /// Distance between a faction's platform and its party's, over the issues the faction actually
        /// demands the party act on, in <c>[0, 1]</c>.
        /// </summary>
        /// <remarks>
        /// Demand-weighted rather than flat, because a faction is not in tension with its party over
        /// issues it does not care about. A flat six-issue mean can barely exceed 0.2 in practice,
        /// which would put <c>factions.internalTensionThreshold</c> permanently out of reach and make
        /// splits impossible; weighting by demands restores the full <c>[0, 1]</c> range.
        /// A faction with no recorded demands falls back to uniform weights.
        /// </remarks>
        public static double Tension(Faction faction, IssuePosition partyPlatform)
        {
            if (faction == null) throw new ArgumentNullException(nameof(faction));
            return IssueVectors.Clamp01(
                faction.Platform.WeightedDistance(partyPlatform, DemandWeights(faction.Demands)));
        }

        /// <summary>
        /// Weights that put all the mass on a faction's demands. Uniform when the demand list is
        /// empty or unrecognisable, so the result is never NaN.
        /// </summary>
        public static IssueWeights DemandWeights(IReadOnlyList<Issue>? demands)
        {
            if (demands == null || demands.Count == 0) return IssueWeights.Uniform;

            var v = new double[Contracts.Issues.Count];
            bool any = false;
            for (int n = 0; n < Contracts.Issues.All.Count; n++)
            {
                Issue issue = Contracts.Issues.All[n];
                for (int d = 0; d < demands.Count; d++)
                {
                    if (demands[d] == issue) { v[n] = 1.0; any = true; break; }
                }
            }

            return any ? IssueVectors.Weights(v) : IssueWeights.Uniform;
        }

        /// <summary>
        /// The issues a faction presses its party on: its core grievance first, then the issues where
        /// it stands furthest from the current party line.
        /// </summary>
        /// <remarks>
        /// Length is <c>factions.demandCountPerFaction</c>, clamped to at least one and at most six.
        /// Ties break by <see cref="Issue"/> declaration order.
        /// </remarks>
        public static List<Issue> Demands(Faction faction, IssuePosition partyPlatform, EngineTuning tuning)
        {
            if (faction == null) throw new ArgumentNullException(nameof(faction));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            int count = tuning.Factions.DemandCountPerFaction;
            if (count < 1) count = 1;
            if (count > Contracts.Issues.Count) count = Contracts.Issues.Count;

            var ordered = new Issue[Contracts.Issues.Count];
            for (int i = 0; i < Contracts.Issues.Count; i++) ordered[i] = Contracts.Issues.All[i];

            IssuePosition platform = faction.Platform;
            Array.Sort(ordered, (a, b) =>
            {
                double ga = Math.Abs(platform[a] - partyPlatform[a]);
                double gb = Math.Abs(platform[b] - partyPlatform[b]);
                int c = gb.CompareTo(ga);
                return c != 0 ? c : ((int)a).CompareTo((int)b);
            });

            var demands = new List<Issue>(count) { faction.CoreGrievance };
            for (int i = 0; i < ordered.Length && demands.Count < count; i++)
            {
                if (ordered[i] == faction.CoreGrievance) continue;
                demands.Add(ordered[i]);
            }
            return demands;
        }

        /// <summary>
        /// Writes the party platform from its factions.
        /// </summary>
        /// <remarks>
        /// With a dominant faction the blend is <c>factions.platformWeightDominant</c> to the pen
        /// holder and <c>factions.platformWeightOthers</c> spread across the rest by internal support
        /// — normalised to a convex combination, so the result can never leave the hull of the
        /// factions' own platforms. With no dominant faction (nobody cleared the threshold) it is a
        /// plain support-weighted mean: the party governs by committee until someone wins the argument.
        ///
        /// <para>
        /// <c>IsDominant</c> must already be resolved — call <see cref="FactionDominance.Apply"/>
        /// first. Reading it rather than recomputing keeps a single source of truth for who holds
        /// the pen.
        /// </para>
        /// </remarks>
        public static PlatformAuthorship Author(Party party, IReadOnlyList<Faction>? partyFactions, EngineTuning tuning)
        {
            if (party == null) throw new ArgumentNullException(nameof(party));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            var result = new PlatformAuthorship { PartyId = party.Id, Platform = party.Platform };

            List<Faction> eligible = FactionSupport.EligibleSortedById(partyFactions);
            if (eligible.Count == 0) return result;

            int dominantIndex = -1;
            for (int i = 0; i < eligible.Count; i++)
            {
                if (eligible[i].IsDominant) { dominantIndex = i; break; }
            }

            double[] weights = BlendWeights(eligible, dominantIndex, tuning);

            var blended = new double[Contracts.Issues.Count];
            for (int n = 0; n < Contracts.Issues.All.Count; n++)
            {
                Issue issue = Contracts.Issues.All[n];
                double sum = 0.0;
                for (int i = 0; i < eligible.Count; i++)
                    sum += weights[i] * eligible[i].Platform[issue];
                blended[n] = sum;
            }

            result.Platform = IssueVectors.Position(blended).Clamped();
            result.DominantFactionId = dominantIndex >= 0 ? eligible[dominantIndex].Id : null;

            for (int i = 0; i < eligible.Count; i++)
                result.Weights.Add(new FactionBlendWeight(eligible[i].Id, weights[i]));

            for (int n = 0; n < Contracts.Issues.All.Count; n++)
            {
                Issue issue = Contracts.Issues.All[n];
                int owner = 0;
                double bestPull = double.NegativeInfinity;
                for (int i = 0; i < eligible.Count; i++)
                {
                    double pull = Math.Abs(weights[i] * eligible[i].Platform[issue]);
                    if (pull > bestPull) { bestPull = pull; owner = i; }
                }

                result.Issues.Add(new IssueAuthorship(
                    issue,
                    eligible[owner].Id,
                    eligible[owner].Platform[issue],
                    weights[owner] * eligible[owner].Platform[issue]));
            }

            return result;
        }

        /// <summary>
        /// The convex blend weights. Exposed so the dashboard and the tests can assert the split
        /// without re-deriving it.
        /// </summary>
        internal static double[] BlendWeights(List<Faction> eligible, int dominantIndex, EngineTuning tuning)
        {
            var weights = new double[eligible.Count];
            if (eligible.Count == 0) return weights;

            double[] support = new double[eligible.Count];
            double supportTotal = 0.0;
            for (int i = 0; i < eligible.Count; i++)
            {
                double s = eligible[i].InternalSupport;
                if (!IssueVectors.IsFinite(s) || s < 0.0) s = 0.0;
                support[i] = s;
                supportTotal += s;
            }

            double wDom = tuning.Factions.PlatformWeightDominant;
            double wOthers = tuning.Factions.PlatformWeightOthers;
            if (!IssueVectors.IsFinite(wDom) || wDom < 0.0) wDom = 0.0;
            if (!IssueVectors.IsFinite(wOthers) || wOthers < 0.0) wOthers = 0.0;
            double wTotal = wDom + wOthers;

            bool committee = dominantIndex < 0 || wTotal <= 0.0 || eligible.Count == 1;

            if (committee)
            {
                if (dominantIndex >= 0 && eligible.Count == 1)
                {
                    weights[dominantIndex] = 1.0;
                    return weights;
                }

                if (supportTotal <= 0.0)
                {
                    for (int i = 0; i < eligible.Count; i++) weights[i] = 1.0 / eligible.Count;
                    return weights;
                }

                for (int i = 0; i < eligible.Count; i++) weights[i] = support[i] / supportTotal;
                return weights;
            }

            double othersSupport = supportTotal - support[dominantIndex];
            double dominantShare = wDom / wTotal;
            double othersShare = wOthers / wTotal;

            weights[dominantIndex] = dominantShare;

            if (othersSupport <= 0.0)
            {
                // Every non-dominant faction is at zero support: split the remainder evenly rather
                // than handing it all back to the dominant faction, so the cap on dominance holds.
                int others = eligible.Count - 1;
                for (int i = 0; i < eligible.Count; i++)
                    if (i != dominantIndex) weights[i] = othersShare / others;
                return weights;
            }

            for (int i = 0; i < eligible.Count; i++)
                if (i != dominantIndex) weights[i] = othersShare * (support[i] / othersSupport);

            return weights;
        }
    }
}
