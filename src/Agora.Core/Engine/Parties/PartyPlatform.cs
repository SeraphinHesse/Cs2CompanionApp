using System;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Parties
{
    /// <summary>
    /// Platform arithmetic for the party packet: instantiate an archetype, keep two platforms
    /// legibly apart, drift a platform toward the electorate's grievances, and derive a splinter's
    /// stance from the parent it broke away from.
    ///
    /// <para>
    /// Every function is pure, takes its coefficients from <see cref="EngineTuning"/>, and sums in
    /// <see cref="Issues.All"/> order so results are bit-stable.
    /// </para>
    /// </summary>
    public static class PartyPlatform
    {
        // Distance() is the mean absolute per-issue gap divided by the 2.0 span, i.e.
        // sum|delta| / (Issues.Count * 2). Inverting that is how SeparateFrom sizes its push.
        private const double DistanceDenominator = Issues.Count * 2.0;

        /// <summary>Componentwise <c>a - b</c>.</summary>
        public static IssuePosition Delta(IssuePosition a, IssuePosition b) => a.Add(b.Scale(-1.0));

        /// <summary>Sum of absolute components, in <see cref="Issues.All"/> order.</summary>
        public static double AbsSum(IssuePosition p)
        {
            double total = 0.0;
            for (int i = 0; i < Issues.All.Count; i++) total += Math.Abs(p[Issues.All[i]]);
            return total;
        }

        /// <summary>
        /// An archetype's base platform plus seeded per-issue jitter, so two saves running the same
        /// archetype do not produce the same party.
        /// </summary>
        /// <param name="rng">
        /// Must come from a per-entity sub-stream (<see cref="SeedStreams.RngFor"/> on
        /// <see cref="StreamNames.PartyGeneration"/>) — a shared stream would couple every party's
        /// platform to generation order.
        /// </param>
        public static IssuePosition Instantiate(PartyArchetype archetype, DeterministicRng rng, double spreadSigma)
        {
            if (archetype == null) throw new ArgumentNullException(nameof(archetype));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            IssuePosition p = archetype.BasePlatform;
            for (int i = 0; i < Issues.All.Count; i++)
            {
                Issue issue = Issues.All[i];
                p = p.With(issue, p[issue] + rng.NextGaussian() * spreadSigma);
            }
            return p.Clamped();
        }

        /// <summary>
        /// Pushes <paramref name="candidate"/> away from <paramref name="other"/> until they are at
        /// least <paramref name="minDistance"/> apart, so the ballot stays legible.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two directions are tried in order. First the natural one — straight away from
        /// <paramref name="other"/> along the existing gap — which keeps the party where it already
        /// leaned. Positions are bounded to [-1,+1] though, so a party sitting near a corner can have
        /// that push clipped to nothing; the second direction therefore heads for the interior, whose
        /// sign is chosen opposite <paramref name="other"/>'s own. That guarantees at least
        /// <c>Issues.Count</c> of unit travel available, i.e. a reachable distance of at least 0.5,
        /// so any sane <paramref name="minDistance"/> is always satisfiable.
        /// </para>
        /// <para>
        /// At most one random draw is consumed, and only to break an exact tie in the interior sign.
        /// </para>
        /// </remarks>
        /// <param name="fallbackAxis">
        /// The issue the brand owns. It gets double weight in the interior direction so a party
        /// pushed off its neighbour moves furthest on the issue it is actually about.
        /// </param>
        public static IssuePosition SeparateFrom(IssuePosition candidate, IssuePosition other,
                                                 double minDistance, Issue fallbackAxis, DeterministicRng? rng)
        {
            if (minDistance <= 0.0) return candidate.Clamped();

            IssuePosition best = candidate.Clamped();
            double bestDistance = best.Distance(other);
            if (bestDistance >= minDistance) return best;

            IssuePosition natural = Delta(candidate, other);
            Push(other, natural, minDistance, ref best, ref bestDistance);
            if (bestDistance >= minDistance) return best;

            Push(other, InteriorDirection(other, fallbackAxis, rng), minDistance, ref best, ref bestDistance);
            return best;
        }

        /// <summary>
        /// Walks outward from <paramref name="other"/> along <paramref name="direction"/>, doubling
        /// the step until the clamp stops eating it. Four steps is enough to saturate every axis.
        /// </summary>
        private static void Push(IssuePosition other, IssuePosition direction, double minDistance,
                                 ref IssuePosition best, ref double bestDistance)
        {
            double norm = AbsSum(direction);
            if (norm <= 1e-9) return;

            double needed = minDistance * DistanceDenominator;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                IssuePosition pushed = other.Add(direction.Scale(needed / norm)).Clamped();
                double distance = pushed.Distance(other);

                if (distance > bestDistance)
                {
                    best = pushed;
                    bestDistance = distance;
                }
                if (bestDistance >= minDistance) return;

                needed *= 2.0;
            }
        }

        /// <summary>
        /// A direction pointing away from <paramref name="from"/> and toward the interior of the
        /// position space, so the clamp never blocks it.
        /// </summary>
        private static IssuePosition InteriorDirection(IssuePosition from, Issue fallbackAxis, DeterministicRng? rng)
        {
            double signedTotal = 0.0;
            for (int i = 0; i < Issues.All.Count; i++) signedTotal += from[Issues.All[i]];

            double sign;
            if (signedTotal > 0.0) sign = -1.0;
            else if (signedTotal < 0.0) sign = 1.0;
            else sign = (rng != null && rng.NextBool(0.5)) ? 1.0 : -1.0;

            // Not a position — a direction vector, so the [-1,+1] convention does not apply to it.
            return new IssuePosition(sign, sign, sign, sign, sign, sign).With(fallbackAxis, sign * 2.0);
        }

        /// <summary>Weighted blend of two platforms. A zero or negative weight total falls back to 50/50.</summary>
        public static IssuePosition Blend(IssuePosition a, double weightA, IssuePosition b, double weightB)
        {
            double total = weightA + weightB;
            if (total <= 0.0 || double.IsNaN(total) || double.IsInfinity(total))
            {
                weightA = 0.5;
                weightB = 0.5;
                total = 1.0;
            }
            return a.Scale(weightA / total).Add(b.Scale(weightB / total)).Clamped();
        }

        /// <summary>
        /// The issue on which the leadership has moved furthest from the line it was elected on —
        /// the grievance a splinter owns. Ties break to the lowest <see cref="Issue"/> ordinal.
        /// </summary>
        public static Issue MostBetrayedIssue(Party party)
        {
            if (party == null) throw new ArgumentNullException(nameof(party));

            Issue worst = Issues.All[0];
            double worstGap = -1.0;
            for (int i = 0; i < Issues.All.Count; i++)
            {
                Issue issue = Issues.All[i];
                double gap = Math.Abs(party.Platform[issue] - party.LastManifesto[issue]);
                if (gap > worstGap)
                {
                    worstGap = gap;
                    worst = issue;
                }
            }
            return worst;
        }

        /// <summary>
        /// The stance a splinter takes: the manifesto the leadership abandoned, pushed away from the
        /// current platform until the two are at least <c>parties.minPlatformDistance</c> apart.
        /// </summary>
        public static IssuePosition SplinterPlatform(Party parent, EngineTuning tuning, DeterministicRng rng)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            return SeparateFrom(parent.LastManifesto, parent.Platform,
                                tuning.Parties.MinPlatformDistance, MostBetrayedIssue(parent), rng);
        }

        /// <summary>
        /// Rewrites a party's platform at the start of a campaign: it moves toward the issues the
        /// city is aggrieved about, plus seeded drift, and the move is capped per issue.
        ///
        /// <para>
        /// Returns a <b>new</b> <see cref="Party"/>; the input is not mutated. Both
        /// <see cref="Party.Platform"/> and <see cref="Party.LastManifesto"/> are set — the manifesto
        /// is by definition the platform it is about to run on, and the pair only diverges again as
        /// the party governs.
        /// </para>
        /// </summary>
        /// <param name="cityGrievance">
        /// Per-issue grievance, each component expected in [0,1] (clamped defensively). Higher means
        /// the electorate wants more action on that issue, which is the <c>+1</c> direction of the
        /// <see cref="IssuePosition"/> sign convention for all six issues.
        /// </param>
        public static Party RefreshManifesto(Guid saveGuid, SimDate date, Party party,
                                             IssueWeights cityGrievance, EngineTuning tuning)
        {
            if (party == null) throw new ArgumentNullException(nameof(party));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            PartiesTuning t = tuning.Parties;
            var rng = SeedStreams.RngFor(saveGuid, date, StreamNames.CampaignManifesto, party.Id);

            IssuePosition platform = party.Platform;
            for (int i = 0; i < Issues.All.Count; i++)
            {
                Issue issue = Issues.All[i];
                double grievance = Clamp(cityGrievance[issue], 0.0, 1.0);
                double current = platform[issue];

                // Saturating pull toward +1: a party already at the ceiling cannot promise more.
                double pull = t.PlatformGrievanceResponsiveness * grievance * (1.0 - current);
                double drift = rng.NextGaussian() * t.PlatformDriftPerCycle;

                double move = Clamp(pull + drift, -t.PlatformDriftCapPerCycle, t.PlatformDriftCapPerCycle);
                platform = platform.With(issue, current + move);
            }

            platform = platform.Clamped();

            Party next = PartyRegistry.Clone(party);
            next.Platform = platform;
            next.LastManifesto = platform;
            return next;
        }

        // netstandard2.0 has no Math.Clamp.
        internal static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);
    }
}
