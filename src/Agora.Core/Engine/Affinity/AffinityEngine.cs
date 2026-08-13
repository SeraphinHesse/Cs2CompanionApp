using System;
using System.Collections.Generic;
using System.Linq;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Agora.Core.Engine.Parties;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Affinity
{
    /// <summary>
    /// Packet 4 — bloc→party affinity (<c>politicsmodplan.md</c> §4.3).
    ///
    /// <para>
    /// One bloc's score for one party is
    /// <c>base + issue proximity + incumbency + mandate performance + events + habitual loyalty +
    /// seeded noise</c>, every coefficient read from the <c>affinity</c> section of
    /// <c>data/engine_tuning.json</c>. Terms are summed in that fixed order so the floating-point
    /// result is bit-stable.
    /// </para>
    ///
    /// <para>
    /// The class is static and holds no state: the same request and tuning always produce the same
    /// result, which is what lets the electoral packets call it as a pure function of engine state.
    /// The only stochastic term is the noise draw, which comes from a per-(district, bloc, party)
    /// sub-stream of <see cref="StreamNames.AffinityNoise"/> — never from a loop counter, so
    /// inserting a district cannot shift anyone else's draw.
    /// </para>
    /// </summary>
    public static class AffinityEngine
    {
        /// <summary>
        /// <see cref="TimelineEvent.Severity"/> is 1–5 by contract; dividing by the ceiling turns it
        /// into a <c>[0.2, 1]</c> scale. This is a schema bound, not a tuning coefficient — the
        /// strength of the event term itself is <c>affinity.eventModifierWeight</c>.
        /// </summary>
        private const int MaxEventSeverity = 5;

        // ---------------------------------------------------------------------------------------
        // Public surface. Four entry points, all pure.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Scores every bloc against every party on the ballot and softmaxes each bloc's row into a
        /// preference distribution.
        /// </summary>
        public static AffinityResult Compute(AffinityRequest request, EngineTuning tuning)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            Context ctx = Context.Build(request, tuning);
            AffinityTuning t = tuning.Affinity;
            List<Bloc> blocs = OrderedBlocs(request.Blocs);
            List<Party> parties = BallotParties(request.Parties);

            var affinities = new List<BlocAffinity>(blocs.Count * parties.Count);
            var blocShares = new List<BlocVoteShares>(blocs.Count);

            for (int b = 0; b < blocs.Count; b++)
            {
                Bloc bloc = blocs[b];
                var row = new List<BlocAffinity>(parties.Count);

                for (int p = 0; p < parties.Count; p++) row.Add(Score(bloc, parties[p], ctx));

                // The fringe ceiling, applied to the finished row rather than folded into Score: a
                // ceiling is a claim about a party's share, and share does not exist until the whole
                // row does. Expressed as a shift on Affinity, so the election packet's independent
                // re-softmax of these same values reproduces the capped distribution without knowing
                // ceilings exist. A no-op when ctx.Ceilings is empty, which is the EU path.
                FringeCeiling.ApplyToRow(row, ctx.Ceilings, t.SoftmaxTemperature);

                for (int p = 0; p < row.Count; p++) affinities.Add(row[p]);

                blocShares.Add(new BlocVoteShares(bloc.DistrictId, bloc.Key, ToVoteShares(row, tuning)));
            }

            var partyIds = new List<string>(parties.Count);
            for (int p = 0; p < parties.Count; p++) partyIds.Add(parties[p].Id);

            return new AffinityResult(affinities, blocShares, partyIds);
        }

        /// <summary>
        /// One bloc against one party. Convenience for tests, the dashboard's "why" panel and any
        /// caller that needs a single cell; <see cref="Compute"/> is the cheaper path for a full tick
        /// because it sorts and filters the request once instead of per cell.
        /// </summary>
        /// <remarks>
        /// Does <b>not</b> apply the fringe ceiling, and cannot: a ceiling is a statement about a
        /// party's share of a bloc, which is only defined once every party on the ballot has been
        /// scored. A single cell returned from here is therefore the uncapped affinity, and callers
        /// comparing it against a row from <see cref="Compute"/> will see them differ for a suppressed
        /// party. That is the correct reading — the difference is exactly
        /// <see cref="BlocAffinity.CeilingComponent"/>.
        /// </remarks>
        public static BlocAffinity ComputeFor(Bloc bloc, Party party, AffinityRequest request, EngineTuning tuning)
        {
            if (bloc == null) throw new ArgumentNullException(nameof(bloc));
            if (party == null) throw new ArgumentNullException(nameof(party));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            return Score(bloc, party, Context.Build(request, tuning));
        }

        /// <summary>
        /// Turns one bloc's affinity row into vote shares with a softmax at
        /// <c>affinity.softmaxTemperature</c>, then drops parties below
        /// <c>affinity.minPartyShare</c> and renormalises so rounding noise is never reported as
        /// support.
        /// </summary>
        /// <remarks>
        /// Softmax rather than "normalise the raw scores" because affinity is signed and unbounded:
        /// dividing by a sum that can be near zero or negative produces nonsense, while
        /// <c>exp</c> is total. The result is sorted by party id ordinal ascending, per the
        /// <see cref="PartyVoteShare"/> contract.
        /// </remarks>
        public static List<PartyVoteShare> ToVoteShares(IReadOnlyList<BlocAffinity> affinities, EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            var result = new List<PartyVoteShare>();
            if (affinities == null || affinities.Count == 0) return result;

            AffinityTuning t = tuning.Affinity;

            List<BlocAffinity> ordered = affinities
                .Where(a => a != null)
                .OrderBy(a => a.PartyId, StringComparer.Ordinal)
                .ToList();

            int n = ordered.Count;
            if (n == 0) return result;

            var weights = new double[n];

            if (t.SoftmaxTemperature <= 0.0)
            {
                // A zero or negative temperature is the degenerate "no dispersion" case: all support
                // goes to the best party. Ties break to the lowest party id, because the list is
                // already sorted — never to whichever the caller happened to add first.
                int best = 0;
                for (int i = 1; i < n; i++)
                    if (ordered[i].Affinity > ordered[best].Affinity) best = i;
                weights[best] = 1.0;
            }
            else
            {
                double max = ordered[0].Affinity;
                for (int i = 1; i < n; i++)
                    if (ordered[i].Affinity > max) max = ordered[i].Affinity;

                // Subtract the max before exponentiating: mathematically a no-op, but it keeps the
                // argument non-positive so a large affinity cannot overflow to infinity.
                for (int i = 0; i < n; i++)
                {
                    double e = Math.Exp((ordered[i].Affinity - max) / t.SoftmaxTemperature);
                    weights[i] = (double.IsNaN(e) || double.IsInfinity(e) || e < 0.0) ? 0.0 : e;
                }
            }

            double[] shares = Normalize(weights);
            shares = PruneMinorShares(shares, t.MinPartyShare);

            for (int i = 0; i < n; i++)
                result.Add(new PartyVoteShare(ordered[i].PartyId, shares[i]));

            return result;
        }

        /// <summary>
        /// FPTP tactical voting: parties more than <c>affinity.tacticalVotingThresholdFptp</c> behind
        /// second place cannot win the seat, so a fraction of their support migrates to whichever of
        /// the top two is ideologically closer.
        /// </summary>
        /// <param name="shares">District shares, in any order. Returned sorted by party id.</param>
        /// <param name="parties">
        /// Parties whose <see cref="Party.Platform"/> decides where support migrates. A party with no
        /// entry here migrates to the leader, which is the safe default when the alternative is
        /// picking arbitrarily.
        /// </param>
        /// <param name="tuning">Supplies the viability threshold.</param>
        /// <param name="migrationShare">
        /// Fraction of a non-viable party's support that defects, clamped to <c>[0, 1]</c>. Passed in
        /// rather than read here because how *hard* FPTP squeezes third parties is the election
        /// packet's coefficient (<c>electionsFptp.thirdPartyPenalty</c>); this packet only owns who
        /// counts as non-viable. Splitting it that way stops the two sections double-counting.
        /// </param>
        public static List<PartyVoteShare> ApplyTacticalVoting(IReadOnlyList<PartyVoteShare> shares,
                                                               IReadOnlyList<Party> parties,
                                                               EngineTuning tuning,
                                                               double migrationShare)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            var ordered = (shares ?? new List<PartyVoteShare>())
                .OrderBy(s => s.PartyId, StringComparer.Ordinal)
                .ToList();

            if (ordered.Count < 3) return ordered; // Nobody is outside the top two.

            double migrate = Clamp(migrationShare, 0.0, 1.0);
            if (migrate <= 0.0) return ordered;

            // Rank by share descending, party id ascending — a total order, so an exact tie for
            // second place resolves the same way on every machine.
            List<int> byRank = Enumerable.Range(0, ordered.Count)
                .OrderByDescending(i => ordered[i].Share)
                .ThenBy(i => ordered[i].PartyId, StringComparer.Ordinal)
                .ToList();

            int first = byRank[0];
            int second = byRank[1];
            double cutoff = ordered[second].Share - tuning.Affinity.TacticalVotingThresholdFptp;

            var values = new double[ordered.Count];
            for (int i = 0; i < ordered.Count; i++) values[i] = ordered[i].Share;

            for (int r = 2; r < byRank.Count; r++)
            {
                int i = byRank[r];
                if (values[i] >= cutoff) continue; // Still within striking distance of second.

                double moved = values[i] * migrate;
                values[i] -= moved;
                values[NearerOf(ordered[i].PartyId, ordered[first].PartyId, ordered[second].PartyId, parties, first, second)] += moved;
            }

            double[] normalized = Normalize(values);

            var result = new List<PartyVoteShare>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
                result.Add(new PartyVoteShare(ordered[i].PartyId, normalized[i]));

            return result;
        }

        /// <summary>
        /// The distance kernel: how a weighted issue distance in <c>[0, 1]</c> becomes a proximity
        /// score in <c>[0, 1]</c>, per <c>affinity.distanceKernel</c>.
        /// </summary>
        /// <remarks>
        /// An unrecognised kernel name degrades to <c>linear</c> rather than throwing. A typo in a
        /// tuning file must not take a save down mid-election; the tuning reader already surfaces
        /// shape problems through <see cref="EngineTuning.Warnings"/>.
        /// </remarks>
        public static double IssueProximity(double distance, AffinityTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            double d = Clamp(distance, 0.0, 1.0);
            string kernel = tuning.DistanceKernel ?? "";

            if (string.Equals(kernel, "quadratic", StringComparison.OrdinalIgnoreCase))
                return 1.0 - d * d;

            if (string.Equals(kernel, "gaussian", StringComparison.OrdinalIgnoreCase))
            {
                double sigma = tuning.DistanceKernelSigma;
                if (sigma <= 0.0) return 1.0 - d; // Degenerate width: fall back to linear.
                return Math.Exp(-(d * d) / (2.0 * sigma * sigma));
            }

            return 1.0 - d;
        }

        // ---------------------------------------------------------------------------------------
        // Scoring
        // ---------------------------------------------------------------------------------------

        private static BlocAffinity Score(Bloc bloc, Party party, Context ctx)
        {
            AffinityTuning t = ctx.Tuning;

            double distance = bloc.Ideal.WeightedDistance(party.Platform, bloc.Weights);
            double issue = t.IssueWeight * IssueProximity(distance, t);
            double incumbency = IncumbencyTerm(bloc, party, ctx);
            double mandate = MandateTerm(bloc, party, ctx);
            double eventTerm = EventTerm(bloc, party, ctx);
            double loyalty = LoyaltyTerm(bloc, party, ctx);
            double noise = NoiseTerm(bloc, party, ctx);

            return new BlocAffinity
            {
                DistrictId = bloc.DistrictId,
                Bloc = bloc.Key,
                PartyId = party.Id,
                IssueComponent = issue,
                IncumbencyComponent = incumbency,
                MandateComponent = mandate,
                EventComponent = eventTerm,
                LoyaltyComponent = loyalty,
                NoiseComponent = noise,
                // Fixed summation order — see the class remarks.
                Affinity = t.BaseAffinity + issue + incumbency + mandate + eventTerm + loyalty + noise
            };
        }

        /// <summary>
        /// Governing parties carry a small structural bonus that discontent turns into a penalty.
        /// Local grievance (this bloc) and national mood (the city) are blended by their tuned
        /// weights, so a happy bloc in an angry city still feels some of the anger.
        /// </summary>
        private static double IncumbencyTerm(Bloc bloc, Party party, Context ctx)
        {
            if (!ctx.IsGoverning(party)) return 0.0;

            AffinityTuning t = ctx.Tuning;
            double local = Clamp(bloc.Discontent, 0.0, 1.0);
            double national = ctx.HasNationalDiscontent ? ctx.NationalDiscontent : local;

            double wl = t.LocalGrievanceWeight;
            double wn = t.NationalMoodWeight;
            double total = wl + wn;
            double discontent = total > 0.0 ? (wl * local + wn * national) / total : local;

            return t.IncumbencyBonus - t.IncumbencyDiscontentPenalty * discontent;
        }

        /// <summary>
        /// Delivery record of the promises this party owns, averaged rather than summed so a
        /// government that issued ten mandates is not scored ten times as hard as one that issued
        /// one. Weighted by how much this bloc cares about the mandate's issue.
        /// </summary>
        /// <remarks>
        /// <see cref="Mandate.Salience"/> is deliberately not a factor: the contract defines it as the
        /// stake at *resolution* (the happiness effect), and the per-bloc equivalent here is the
        /// bloc's own issue weight, which is finer-grained. Multiplying by both would count caring
        /// twice and let a zero-salience mandate vanish from politics entirely.
        /// </remarks>
        private static double MandateTerm(Bloc bloc, Party party, Context ctx)
        {
            AffinityTuning t = ctx.Tuning;
            double sum = 0.0;
            int count = 0;

            // ctx.Mandates is pre-sorted by id and pre-filtered to scoreable statuses.
            for (int i = 0; i < ctx.Mandates.Length; i++)
            {
                Mandate m = ctx.Mandates[i];
                if (!string.Equals(m.PartyId, party.Id, StringComparison.Ordinal)) continue;

                // A district promise is only political news inside that district.
                if (m.DistrictId != null && !string.Equals(m.DistrictId, bloc.DistrictId, StringComparison.Ordinal))
                    continue;

                double raw;
                switch (m.Status)
                {
                    case MandateStatus.Fulfilled:
                        raw = t.MandatePerformanceWeight;
                        break;
                    case MandateStatus.PartiallyFulfilled:
                    case MandateStatus.Active:
                        // Visible progress earns partial credit; missing the target only costs at
                        // the deadline, when the status becomes Defied.
                        raw = t.MandatePerformanceWeight * Clamp(m.Progress, 0.0, 1.0);
                        break;
                    case MandateStatus.Defied:
                        raw = -t.MandateFailurePenalty;
                        break;
                    default:
                        continue;
                }

                sum += raw * CareFactor(bloc, m.Issue);
                count++;
            }

            return count == 0 ? 0.0 : sum / count;
        }

        /// <summary>
        /// How live events push blocs toward or away from a platform. Alignment is the
        /// issue-weighted correlation between the event's pressure and the party's platform, decayed
        /// by <c>affinity.eventModifierDecayHalfLifeMonths</c> and scaled by severity.
        /// </summary>
        /// <remarks>
        /// The summed alignment is clamped to <c>[-1, +1]</c> before scaling, so
        /// <c>affinity.eventModifierWeight</c> is a hard bound on the total event swing no matter how
        /// many events are live at once. Without that, a busy decade would drown the issue term.
        /// </remarks>
        private static double EventTerm(Bloc bloc, Party party, Context ctx)
        {
            if (ctx.Events.Length == 0) return 0.0;

            AffinityTuning t = ctx.Tuning;
            double raw = 0.0;

            for (int i = 0; i < ctx.Events.Length; i++)
            {
                TimelineEvent e = ctx.Events[i];
                double alignment = Alignment(e.IssuePressure, party.Platform, bloc.Weights);
                if (alignment == 0.0) continue;

                raw += alignment * EventDecay(e, ctx) * SeverityScale(e.Severity);
            }

            return t.EventModifierWeight * Clamp(raw, -1.0, 1.0);
        }

        private static double EventDecay(TimelineEvent e, Context ctx)
        {
            SimDate fired = e.FiredDate ?? e.Date;
            int months = fired.MonthsUntil(ctx.Date);
            if (months <= 0) return 1.0;

            int halfLife = ctx.Tuning.EventModifierDecayHalfLifeMonths;
            if (halfLife <= 0) return 0.0; // A zero half-life means events never linger.

            return Math.Pow(0.5, months / (double)halfLife);
        }

        private static double SeverityScale(int severity)
        {
            int s = severity < 1 ? 1 : (severity > MaxEventSeverity ? MaxEventSeverity : severity);
            return s / (double)MaxEventSeverity;
        }

        /// <summary>
        /// Issue-weighted correlation of two positions, in <c>[-1, +1]</c>. Summed in
        /// <see cref="Issues.All"/> order so the result does not depend on how the caller stores its
        /// issues.
        /// </summary>
        private static double Alignment(IssuePosition pressure, IssuePosition platform, IssueWeights weights)
        {
            double num = 0.0;
            double den = 0.0;

            for (int i = 0; i < Issues.All.Count; i++)
            {
                Issue issue = Issues.All[i];
                double w = weights[issue];
                if (w < 0.0) w = 0.0;
                num += w * Clamp(pressure[issue], -1.0, 1.0) * Clamp(platform[issue], -1.0, 1.0);
                den += w;
            }

            return den > 0.0 ? num / den : 0.0;
        }

        /// <summary>
        /// Stickiness to how this bloc voted last time, decaying month by month so a party cannot
        /// coast on a decade-old result.
        /// </summary>
        private static double LoyaltyTerm(Bloc bloc, Party party, Context ctx)
        {
            double previous = PreviousShare(bloc, party.Id);
            if (previous <= 0.0) return 0.0;

            AffinityTuning t = ctx.Tuning;
            double decay = 1.0;

            if (ctx.LastElectionDate.HasValue)
            {
                int months = ctx.LastElectionDate.Value.MonthsUntil(ctx.Date);
                if (months > 0)
                {
                    decay = 1.0 - t.LoyaltyDecayPerMonth * months;
                    if (decay < 0.0) decay = 0.0;
                }
            }

            return t.HabitualLoyalty * Clamp(previous, 0.0, 1.0) * decay;
        }

        /// <summary>
        /// The seeded noise term. Each (district, bloc, party) cell draws from its own sub-stream, so
        /// the draw is independent of iteration order — adding a party cannot shift another party's
        /// noise. Clamped to <c>affinity.noiseClamp</c> so one tail draw cannot decide an election.
        /// </summary>
        private static double NoiseTerm(Bloc bloc, Party party, Context ctx)
        {
            AffinityTuning t = ctx.Tuning;
            double bound = Math.Abs(t.NoiseClamp);
            if (bound == 0.0 || t.NoiseSigma == 0.0) return 0.0;

            string entityId = bloc.DistrictId + "|" + bloc.Key.Id + "|" + party.Id;
            DeterministicRng rng = SeedStreams.RngFor(ctx.SaveGuid, ctx.Date, StreamNames.AffinityNoise, entityId);

            return Clamp(rng.NextGaussian() * t.NoiseSigma, -bound, bound);
        }

        /// <summary>
        /// How much this bloc cares about one issue relative to its own average. Weights normally sum
        /// to <see cref="Issues.Count"/>, so an indifferent bloc scores 1.0 and a single-issue bloc
        /// scores well above it.
        /// </summary>
        private static double CareFactor(Bloc bloc, Issue issue)
        {
            double sum = bloc.Weights.Sum();
            if (sum <= 0.0 || double.IsNaN(sum) || double.IsInfinity(sum)) return 1.0;

            double mean = sum / Issues.Count;
            if (mean <= 0.0) return 1.0;

            double w = bloc.Weights[issue];
            return w <= 0.0 ? 0.0 : w / mean;
        }

        private static double PreviousShare(Bloc bloc, string partyId)
        {
            List<PartyVoteShare> previous = bloc.PreviousVote;
            if (previous == null) return 0.0;

            // Linear scan of a short list: correct regardless of order, and no dictionary to iterate.
            for (int i = 0; i < previous.Count; i++)
                if (string.Equals(previous[i].PartyId, partyId, StringComparison.Ordinal))
                    return previous[i].Share;

            return 0.0;
        }

        // ---------------------------------------------------------------------------------------
        // Ordering and filtering
        // ---------------------------------------------------------------------------------------

        private static List<Bloc> OrderedBlocs(IReadOnlyList<Bloc> blocs)
        {
            if (blocs == null) return new List<Bloc>();

            return blocs
                .Where(b => b != null)
                .OrderBy(b => b.DistrictId, StringComparer.Ordinal)
                .ThenBy(b => b.Key.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Parties actually contesting. A dissolved or merged brand still exists in the registry so
        /// it can revive, but it draws no affinity while it is off the ballot.
        /// </summary>
        private static List<Party> BallotParties(IReadOnlyList<Party> parties)
        {
            if (parties == null) return new List<Party>();

            return parties
                .Where(p => p != null && !string.IsNullOrEmpty(p.Id) && IsOnBallot(p.Status))
                .OrderBy(p => p.Id, StringComparer.Ordinal)
                .ToList();
        }

        private static bool IsOnBallot(PartyStatus status) =>
            status == PartyStatus.Active || status == PartyStatus.Endangered || status == PartyStatus.Revived;

        // ---------------------------------------------------------------------------------------
        // Share arithmetic
        // ---------------------------------------------------------------------------------------

        private static double[] Normalize(double[] values)
        {
            var result = new double[values.Length];
            double total = 0.0;

            for (int i = 0; i < values.Length; i++)
            {
                double v = values[i];
                if (v < 0.0 || double.IsNaN(v)) v = 0.0;
                result[i] = v;
                total += v;
            }

            if (total <= 0.0 || double.IsInfinity(total))
            {
                // Total collapse is only reachable through degenerate tuning. Spreading the mass
                // evenly keeps the "shares sum to 1" contract instead of emitting NaN downstream.
                double even = values.Length > 0 ? 1.0 / values.Length : 0.0;
                for (int i = 0; i < result.Length; i++) result[i] = even;
                return result;
            }

            for (int i = 0; i < result.Length; i++) result[i] /= total;
            return result;
        }

        /// <summary>
        /// Zeroes shares under <c>affinity.minPartyShare</c> and renormalises, repeating because
        /// renormalising can push another party under the floor. Reverts if the floor would erase
        /// everyone — a threshold set above <c>1/parties</c> must not produce an empty ballot.
        /// </summary>
        private static double[] PruneMinorShares(double[] shares, double minShare)
        {
            if (minShare <= 0.0 || shares.Length == 0) return shares;

            var working = (double[])shares.Clone();

            for (int pass = 0; pass < shares.Length; pass++)
            {
                bool pruned = false;
                double kept = 0.0;

                for (int i = 0; i < working.Length; i++)
                {
                    if (working[i] > 0.0 && working[i] < minShare)
                    {
                        working[i] = 0.0;
                        pruned = true;
                    }
                    kept += working[i];
                }

                if (kept <= 0.0) return shares; // Floor too high for this ballot; leave it alone.
                if (!pruned) return working;

                for (int i = 0; i < working.Length; i++) working[i] /= kept;
            }

            return working;
        }

        private static int NearerOf(string moverId, string firstId, string secondId,
                                    IReadOnlyList<Party> parties, int firstIndex, int secondIndex)
        {
            Party? mover = FindParty(parties, moverId);
            Party? first = FindParty(parties, firstId);
            Party? second = FindParty(parties, secondId);

            if (mover == null || first == null || second == null) return firstIndex;

            double toFirst = mover.Platform.Distance(first.Platform);
            double toSecond = mover.Platform.Distance(second.Platform);

            // An exact tie goes to the leader: a fixed rule, not a coin flip in place.
            return toSecond < toFirst ? secondIndex : firstIndex;
        }

        private static Party? FindParty(IReadOnlyList<Party> parties, string id)
        {
            if (parties == null) return null;

            for (int i = 0; i < parties.Count; i++)
                if (parties[i] != null && string.Equals(parties[i].Id, id, StringComparison.Ordinal))
                    return parties[i];

            return null;
        }

        // netstandard2.0 has no Math.Clamp.
        private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

        // ---------------------------------------------------------------------------------------
        // Per-tick context: everything sorted and filtered once, so the hot loop is order-free.
        // ---------------------------------------------------------------------------------------

        private sealed class Context
        {
            public readonly Guid SaveGuid;
            public readonly SimDate Date;
            public readonly AffinityTuning Tuning;
            public readonly Mandate[] Mandates;
            public readonly TimelineEvent[] Events;
            public readonly SimDate? LastElectionDate;
            public readonly double NationalDiscontent;
            public readonly bool HasNationalDiscontent;
            public readonly FringeCeilings Ceilings;

            private readonly string[] _governingPartyIds;

            private Context(Guid saveGuid, SimDate date, AffinityTuning tuning, Mandate[] mandates,
                            TimelineEvent[] events, string[] governingPartyIds, SimDate? lastElectionDate,
                            double nationalDiscontent, bool hasNationalDiscontent, FringeCeilings ceilings)
            {
                Ceilings = ceilings;
                SaveGuid = saveGuid;
                Date = date;
                Tuning = tuning;
                Mandates = mandates;
                Events = events;
                _governingPartyIds = governingPartyIds;
                LastElectionDate = lastElectionDate;
                NationalDiscontent = nationalDiscontent;
                HasNationalDiscontent = hasNationalDiscontent;
            }

            public static Context Build(AffinityRequest r, EngineTuning tuning)
            {
                SimDate date = r.Date;

                Mandate[] mandates = (r.Mandates ?? new List<Mandate>())
                    .Where(m => m != null && IsScoreable(m))
                    .OrderBy(m => m.Id, StringComparer.Ordinal)
                    .ToArray();

                TimelineEvent[] events = (r.ActiveEvents ?? new List<TimelineEvent>())
                    .Where(e => e != null && IsLive(e, date))
                    .OrderBy(e => e.Id, StringComparer.Ordinal)
                    .ToArray();

                string[] governing = (r.Government?.MemberPartyIds ?? new List<string>())
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray();

                bool hasNational = r.Indices != null;
                double national = hasNational ? Clamp(r.Indices!.DiscontentIndex, 0.0, 1.0) : 0.0;

                return new Context(r.SaveGuid, date, tuning.Affinity, mandates, events, governing,
                                   r.LastElectionDate, national, hasNational,
                                   r.FringeCeilings ?? FringeCeilings.None);
            }

            /// <summary>
            /// Membership test only — the array is sorted so the lookup is a binary search and never
            /// an iteration whose order could reach the output.
            /// </summary>
            public bool IsGoverning(Party party)
            {
                if (party.IsIncumbent || party.IsInGovernment) return true;
                if (_governingPartyIds.Length == 0) return false;

                return Array.BinarySearch(_governingPartyIds, party.Id, StringComparer.Ordinal) >= 0;
            }

            /// <summary>
            /// Pending mandates are inside their grace period and Abandoned ones were never scored; a
            /// stalled mandate is held, never counted against its party (see <see cref="Mandate"/>).
            /// </summary>
            private static bool IsScoreable(Mandate m)
            {
                if (m.IsMeasurementStalled) return false;

                switch (m.Status)
                {
                    case MandateStatus.Active:
                    case MandateStatus.Fulfilled:
                    case MandateStatus.PartiallyFulfilled:
                    case MandateStatus.Defied:
                        return true;
                    default:
                        return false;
                }
            }

            private static bool IsLive(TimelineEvent e, SimDate date)
            {
                SimDate fired = e.FiredDate ?? e.Date;
                if (fired > date) return false;
                if (e.ExpiresDate.HasValue && e.ExpiresDate.Value < date) return false;
                return true;
            }
        }
    }
}
