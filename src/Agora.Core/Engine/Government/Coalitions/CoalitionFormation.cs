using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Government.Coalitions
{
    /// <summary>
    /// What came out of government formation: the government if one formed, the ranked arrangements
    /// that were on the table, and the snap-election date if nobody could form one.
    /// </summary>
    public sealed class CoalitionFormationResult
    {
        internal CoalitionFormationResult(
            Coalition? government,
            int attempts,
            SimDate? snapElectionDate,
            IReadOnlyList<CoalitionCandidate> rankedCandidates,
            bool usedGrandCoalitionSlack)
        {
            Government = government;
            Attempts = attempts;
            SnapElectionDate = snapElectionDate;
            RankedCandidates = rankedCandidates;
            UsedGrandCoalitionSlack = usedGrandCoalitionSlack;
        }

        /// <summary>The formed government, or null when formation failed.</summary>
        public Coalition? Government { get; }

        /// <summary>True when a government formed, majority or minority.</summary>
        public bool Succeeded => Government != null;

        /// <summary>True when the government that formed is a minority government.</summary>
        public bool IsMinority => Government != null && Government.Status == CoalitionStatus.Minority;

        /// <summary>Rounds of talks held, including the one that produced the government.</summary>
        public int Attempts { get; }

        /// <summary>
        /// When the fresh ballot falls. Set only when formation failed — the calendar gets
        /// <c>coalitions.formationWindowMonths</c> from the election before it gives up and asks the
        /// voters again.
        /// </summary>
        public SimDate? SnapElectionDate { get; }

        /// <summary>
        /// Every viable arrangement, in formation order. Reporting and tests read this; the ranking is
        /// fully deterministic even though whether talks succeed is a seeded draw.
        /// </summary>
        public IReadOnlyList<CoalitionCandidate> RankedCandidates { get; }

        /// <summary>True when nothing worked until the two-largest-parties slack was granted.</summary>
        public bool UsedGrandCoalitionSlack { get; }
    }

    /// <summary>
    /// Government formation from an election's seat allocation (<c>politicsmodplan.md</c> §3).
    ///
    /// <para>
    /// Pure: seats and parties in, a <see cref="Coalition"/> out. Nothing is mutated — in particular
    /// <see cref="Party.IsInGovernment"/> and <see cref="Party.SeatsHeld"/> belong to the party
    /// registry, and this packet only reports what the caller should write there.
    /// </para>
    ///
    /// <para>
    /// Determinism: candidate enumeration runs over a pool sorted by party id, ranking is a total
    /// order (<see cref="CoalitionCandidate.Compare"/>), and the only stochastic element — whether a
    /// round of talks succeeds — is drawn from <c>StreamNames.CoalitionFormation</c> sub-streamed by
    /// election id and attempt number, so it never depends on iteration order.
    /// </para>
    /// </summary>
    public static class CoalitionFormation
    {
        /// <summary>
        /// Forms a government. Under <see cref="ElectoralSystem.FirstPastThePost"/> the mayor's party
        /// governs alone, modelled as a one-member <see cref="Coalition"/> so the mandate packet and
        /// the dashboard have a single code path.
        /// </summary>
        /// <param name="saveGuid">Save identity, for seed derivation.</param>
        /// <param name="date">Formation date — the day after the count, in practice.</param>
        /// <param name="electionId">Election that produced these seats. Also the seed sub-stream key.</param>
        /// <param name="system">Electoral system in force.</param>
        /// <param name="seats">Seat allocation from the election. Order-independent; sorted internally.</param>
        /// <param name="parties">Parties, for platforms and status. Order-independent.</param>
        /// <param name="mayorPartyId">Winner of the mayoral race under FPTP; ignored under PR.</param>
        /// <param name="tuning">Engine tuning; the <c>coalitions</c> section is read.</param>
        /// <param name="coalitionId">
        /// Override for the generated id. The default is <c>gov-YYYY-MM</c>, which is unique in
        /// practice because two governments cannot form in the same month, but a caller that knows
        /// better can say so rather than risk a collision in <c>CoalitionHistory</c>.
        /// </param>
        public static CoalitionFormationResult Form(
            Guid saveGuid,
            SimDate date,
            string electionId,
            ElectoralSystem system,
            IReadOnlyList<SeatAllocation> seats,
            IReadOnlyList<Party> parties,
            string? mayorPartyId,
            EngineTuning tuning,
            string? coalitionId = null)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            if (seats == null) throw new ArgumentNullException(nameof(seats));

            CoalitionsTuning t = tuning.Coalitions;
            string id = string.IsNullOrEmpty(coalitionId) ? DefaultId(date) : coalitionId!;
            string election = electionId ?? "";

            int totalSeats;
            List<PartySeat> pool = CoalitionMath.BuildPool(seats, parties, out totalSeats);

            var noCandidates = new List<CoalitionCandidate>();

            if (pool.Count == 0 || totalSeats <= 0)
            {
                // An empty or unusable chamber cannot produce a government. Back to the voters.
                return new CoalitionFormationResult(
                    null, 0, date.AddMonths(Months(t.FormationWindowMonths)), noCandidates, false);
            }

            if (system == ElectoralSystem.FirstPastThePost)
                return FormSinglePartyGovernment(date, election, id, pool, totalSeats, mayorPartyId, t);

            // --- Proportional: enumerate, rank, negotiate -------------------------------------

            bool usedSlack = false;
            List<CoalitionCandidate> candidates = BuildCandidates(pool, totalSeats, t, false);
            List<CoalitionCandidate> majority = MajorityOf(candidates);

            if (majority.Count == 0 && t.GrandCoalitionDistanceBonus > 0.0)
            {
                // "Slack granted to a two-largest-parties coalition when nothing else works."
                List<CoalitionCandidate> withSlack = BuildCandidates(pool, totalSeats, t, true);
                List<CoalitionCandidate> majorityWithSlack = MajorityOf(withSlack);
                if (majorityWithSlack.Count > 0)
                {
                    usedSlack = true;
                    candidates = withSlack;
                    majority = majorityWithSlack;
                }
            }

            MarkMinimumWinning(majority);
            candidates.Sort(CoalitionCandidate.Order);
            majority.Sort(CoalitionCandidate.Order);

            int attempts = 0;
            int maxAttempts = t.FormationAttemptsMax < 1 ? 1 : t.FormationAttemptsMax;

            for (int i = 0; i < majority.Count && attempts < maxAttempts; i++)
            {
                attempts++;
                CoalitionCandidate candidate = majority[i];

                // Talks succeed with probability equal to the arrangement's cohesion: a close-knit
                // bloc closes the deal, a strained one walks out and the next arrangement gets a turn.
                // Sub-streamed by attempt so adding a candidate cannot shift an earlier round's draw.
                // The attempt number is formatted invariantly: a stream name is part of the seed, and a
                // seed must never depend on the machine's culture.
                DeterministicRng rng = SeedStreams.RngFor(
                    saveGuid, date, StreamNames.CoalitionFormation,
                    election + ":attempt-" + attempts.ToString(System.Globalization.CultureInfo.InvariantCulture));

                if (!rng.NextBool(candidate.Cohesion)) continue;

                Coalition formed = Build(candidate, pool, date, election, id, attempts, CoalitionStatus.Governing, t);
                return new CoalitionFormationResult(formed, attempts, null, candidates, usedSlack);
            }

            if (t.MinorityGovernmentAllowed)
            {
                CoalitionCandidate? minority = BestMinority(candidates);
                if (minority != null)
                {
                    Coalition formed = Build(
                        minority, pool, date, election, id,
                        attempts < 1 ? 1 : attempts, CoalitionStatus.Minority, t);
                    return new CoalitionFormationResult(formed, formed.FormationAttempts, null, candidates, usedSlack);
                }
            }

            return new CoalitionFormationResult(
                null, attempts, date.AddMonths(Months(t.FormationWindowMonths)), candidates, usedSlack);
        }

        /// <summary>Stable id from the formation month, e.g. <c>gov-1994-06</c>.</summary>
        public static string DefaultId(SimDate date) =>
            "gov-" + date.Year.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)
                   + "-" + date.Month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);

        private static int Months(int configured) => configured < 0 ? 0 : configured;

        // --- FPTP ---------------------------------------------------------------------------

        private static CoalitionFormationResult FormSinglePartyGovernment(
            SimDate date, string electionId, string id,
            List<PartySeat> pool, int totalSeats, string? mayorPartyId, CoalitionsTuning t)
        {
            // AGORA-SEAM(§14, NA primaries): `electionsFptp.primariesEnabled` is pinned false, so the
            // party's candidate is not chosen by a primary and faction dominance stands in. If that
            // decision closes, the leader selection hook belongs here, before the government is built.

            PartySeat lead = default(PartySeat);
            bool found = false;

            if (!string.IsNullOrEmpty(mayorPartyId))
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    if (string.CompareOrdinal(pool[i].PartyId, mayorPartyId) == 0)
                    {
                        lead = pool[i];
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                // No mayor, or a mayor whose party won no seats: the chamber's largest party governs.
                var bySize = new List<PartySeat>(pool);
                bySize.Sort(CoalitionMath.CompareBySeatsDescending);
                lead = bySize[0];
            }

            var members = new List<PartySeat> { lead };
            double seatShare = CoalitionMath.ShareOf(lead.Seats, totalSeats);
            bool hasMajority = seatShare >= t.MinSeatShareToGovern;

            var ids = CoalitionMath.SortedIds(members);
            var candidate = new CoalitionCandidate(
                ids, lead.PartyId, lead.Seats, seatShare, hasMajority,
                0.0, 0.0, t.IdeologicalDistanceCap,
                CoalitionMath.Cohesion(0.0, t),
                Score(0.0, seatShare, t), false);

            Coalition government = Build(
                candidate, pool, date, electionId, id, 1,
                hasMajority ? CoalitionStatus.Governing : CoalitionStatus.Minority, t);

            var ranked = new List<CoalitionCandidate> { candidate };
            return new CoalitionFormationResult(government, 1, null, ranked, false);
        }

        // --- candidate construction -----------------------------------------------------------

        private static List<CoalitionCandidate> BuildCandidates(
            List<PartySeat> pool, int totalSeats, CoalitionsTuning t, bool allowGrandSlack)
        {
            var candidates = new List<CoalitionCandidate>();
            int maxPartners = t.FormationMaxPartners < 1 ? 1 : t.FormationMaxPartners;
            if (maxPartners > pool.Count) maxPartners = pool.Count;

            var chosen = new List<PartySeat>();
            Enumerate(pool, 0, maxPartners, chosen, candidates, totalSeats, t, allowGrandSlack);
            return candidates;
        }

        /// <summary>
        /// Depth-first over the id-sorted pool, emitting every member set of size 1..maxPartners.
        /// Enumeration order is a function of the sorted pool alone, so it never depends on how the
        /// caller ordered its seat list.
        /// </summary>
        private static void Enumerate(
            List<PartySeat> pool, int start, int maxPartners, List<PartySeat> chosen,
            List<CoalitionCandidate> sink, int totalSeats, CoalitionsTuning t, bool allowGrandSlack)
        {
            for (int i = start; i < pool.Count; i++)
            {
                chosen.Add(pool[i]);

                CoalitionCandidate? candidate = Evaluate(chosen, pool, totalSeats, t, allowGrandSlack);
                if (candidate != null) sink.Add(candidate);

                if (chosen.Count < maxPartners)
                    Enumerate(pool, i + 1, maxPartners, chosen, sink, totalSeats, t, allowGrandSlack);

                chosen.RemoveAt(chosen.Count - 1);
            }
        }

        /// <summary>Null when the member set is not viable: too far apart, or no member big enough to lead.</summary>
        private static CoalitionCandidate? Evaluate(
            List<PartySeat> members, List<PartySeat> pool, int totalSeats,
            CoalitionsTuning t, bool allowGrandSlack)
        {
            PartySeat lead = CoalitionMath.LeadOf(members);
            double leadShare = CoalitionMath.ShareOf(lead.Seats, totalSeats);
            if (leadShare < t.LeadPartyMinSeatShare) return null;

            double maxDistance = CoalitionMath.MaxPairwiseDistance(members);
            double cap = CoalitionMath.EffectiveDistanceCap(members, pool, t, allowGrandSlack);
            if (maxDistance > cap) return null;

            double meanDistance = CoalitionMath.MeanPairwiseDistance(members);
            int seats = CoalitionMath.SeatsOf(members);
            double seatShare = CoalitionMath.ShareOf(seats, totalSeats);

            return new CoalitionCandidate(
                CoalitionMath.SortedIds(members),
                lead.PartyId,
                seats,
                seatShare,
                seatShare >= t.MinSeatShareToGovern,
                meanDistance,
                maxDistance,
                cap,
                CoalitionMath.Cohesion(meanDistance, t),
                Score(meanDistance, seatShare, t),
                CoalitionMath.IsTwoLargest(members, pool));
        }

        /// <summary>
        /// Tuned blend of ideological closeness and size, normalised to <c>[0,1]</c> so the weights can
        /// be retuned without moving the scale the ranking is read on.
        /// </summary>
        private static double Score(double meanDistance, double seatShare, CoalitionsTuning t)
        {
            double total = t.DistanceWeight + t.SizeWeight;
            if (total <= 0.0) return 0.0;

            double blended = t.DistanceWeight * (1.0 - meanDistance) + t.SizeWeight * seatShare;
            return CoalitionMath.Clamp01(blended / total);
        }

        private static List<CoalitionCandidate> MajorityOf(List<CoalitionCandidate> candidates)
        {
            var majority = new List<CoalitionCandidate>();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].HasMajority) majority.Add(candidates[i]);
            }
            return majority;
        }

        /// <summary>
        /// Flags candidates carrying a partner they do not need: if dropping exactly one member leaves
        /// another viable majority candidate, this one is not minimum winning and ranks below it.
        /// </summary>
        /// <remarks>
        /// Implemented as a flag rather than a filter. Filtering could empty the list — the smaller set
        /// might fail <c>leadPartyMinSeatShare</c> and so not be a candidate at all — and a chamber
        /// with no arrangement at all is a snap election, which is far too strong a consequence for a
        /// tidiness rule.
        /// </remarks>
        private static void MarkMinimumWinning(List<CoalitionCandidate> majority)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < majority.Count; i++) keys.Add(majority[i].Key);

            for (int i = 0; i < majority.Count; i++)
            {
                CoalitionCandidate c = majority[i];
                if (c.MemberPartyIds.Count < 2) continue;

                bool minimal = true;
                for (int drop = 0; drop < c.MemberPartyIds.Count && minimal; drop++)
                {
                    var reduced = new List<string>();
                    for (int k = 0; k < c.MemberPartyIds.Count; k++)
                    {
                        if (k != drop) reduced.Add(c.MemberPartyIds[k]);
                    }

                    // Membership test only — the set is never enumerated, so no order leaks.
                    if (keys.Contains(CoalitionMath.KeyOf(reduced))) minimal = false;
                }

                c.IsMinimumWinning = minimal;
            }
        }

        private static CoalitionCandidate? BestMinority(List<CoalitionCandidate> ranked)
        {
            for (int i = 0; i < ranked.Count; i++)
            {
                if (!ranked[i].HasMajority) return ranked[i];
            }
            return null;
        }

        private static Coalition Build(
            CoalitionCandidate candidate, List<PartySeat> pool, SimDate date,
            string electionId, string id, int attempts, CoalitionStatus status, CoalitionsTuning t)
        {
            double stability = CoalitionMath.Clamp01(t.StabilityInitial);
            if (status == CoalitionStatus.Minority)
                stability = CoalitionMath.Clamp01(stability * (1.0 - t.MinorityGovernmentPenalty));

            var members = new List<string>(candidate.MemberPartyIds);
            members.Sort(StringComparer.Ordinal);

            return new Coalition
            {
                Id = id,
                FormedDate = date,
                EndedDate = null,
                MemberPartyIds = members,
                LeadPartyId = candidate.LeadPartyId,
                OppositionPartyIds = CoalitionMath.OppositionIds(pool, members),
                Seats = candidate.Seats,
                SeatShare = candidate.SeatShare,
                HasMajority = candidate.HasMajority,
                Cohesion = candidate.Cohesion,
                Stability = stability,
                Status = status,
                CollapseReason = CoalitionCollapseReason.None,
                FormationAttempts = attempts,
                ElectionId = electionId,
                MandateIds = new List<string>()
            };
        }
    }
}
