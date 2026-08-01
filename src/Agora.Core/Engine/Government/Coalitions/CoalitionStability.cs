using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Government.Coalitions
{
    /// <summary>
    /// Everything that happened to a government since its last confidence check. A plain input bag:
    /// the caller fills what it knows and leaves the rest empty.
    /// </summary>
    /// <remarks>
    /// Lists, never dictionaries. Every field here is either summed (order-independent for integers)
    /// or sorted before use, so a caller cannot change the outcome by reordering its inputs.
    /// </remarks>
    public sealed class CoalitionTickInputs
    {
        /// <summary>Months since the last check. Defaults to one; negatives read as zero.</summary>
        public int MonthsElapsed { get; set; } = 1;

        /// <summary>Mandates resolved <see cref="MandateStatus.Defied"/> since the last check.</summary>
        public int FailedMandates { get; set; }

        /// <summary>Mandates resolved <see cref="MandateStatus.Fulfilled"/> since the last check.</summary>
        public int FulfilledMandates { get; set; }

        /// <summary>
        /// Severities of timeline events that fired since the last check. Only those at or above
        /// <c>catalog.majorSeverityThreshold</c> shake a government; the rest are noise.
        /// </summary>
        public List<int> EventSeverities { get; set; } = new List<int>();

        /// <summary>Members that walked out, beyond any whose party status already says so.</summary>
        public List<string> WithdrawnPartyIds { get; set; } = new List<string>();

        /// <summary>Current parties, for platforms and status. Order-independent.</summary>
        public List<Party> Parties { get; set; } = new List<Party>();

        /// <summary>Current chamber. Order-independent. Empty keeps the government's stored seats.</summary>
        public List<SeatAllocation> Seats { get; set; } = new List<SeatAllocation>();

        /// <summary>True on the tick the term runs out. Ends the government normally, with no snap election.</summary>
        public bool TermExpired { get; set; }
    }

    /// <summary>
    /// The government's state after one confidence check. Nothing is mutated until the caller calls
    /// <see cref="ApplyTo"/> — a tick that ends in a collapse still has to be written into history in
    /// the right order, and that is the caller's business.
    /// </summary>
    public sealed class CoalitionTickResult
    {
        internal CoalitionTickResult(
            double stability, double stabilityDelta, double cohesion,
            int seats, double seatShare, bool hasMajority,
            IReadOnlyList<string> memberPartyIds, IReadOnlyList<string> oppositionPartyIds,
            IReadOnlyList<string> withdrawnPartyIds, string leadPartyId,
            CoalitionStatus status, CoalitionCollapseReason collapseReason,
            SimDate? endedDate, SimDate? snapElectionDate,
            double decayComponent, double mandateShockComponent,
            double mandateRecoveryComponent, double eventShockComponent,
            double minorityTransitionComponent, double maxPairwiseDistance)
        {
            Stability = stability;
            StabilityDelta = stabilityDelta;
            Cohesion = cohesion;
            Seats = seats;
            SeatShare = seatShare;
            HasMajority = hasMajority;
            MemberPartyIds = memberPartyIds;
            OppositionPartyIds = oppositionPartyIds;
            WithdrawnPartyIds = withdrawnPartyIds;
            LeadPartyId = leadPartyId;
            Status = status;
            CollapseReason = collapseReason;
            EndedDate = endedDate;
            SnapElectionDate = snapElectionDate;
            DecayComponent = decayComponent;
            MandateShockComponent = mandateShockComponent;
            MandateRecoveryComponent = mandateRecoveryComponent;
            EventShockComponent = eventShockComponent;
            MinorityTransitionComponent = minorityTransitionComponent;
            MaxPairwiseDistance = maxPairwiseDistance;
        }

        /// <summary>Confidence after the tick, <c>[0,1]</c>.</summary>
        public double Stability { get; }

        /// <summary>Signed change applied this tick. Negative in every quiet month.</summary>
        public double StabilityDelta { get; }

        /// <summary>Cohesion recomputed from the members' current platforms, <c>[0,1]</c>.</summary>
        public double Cohesion { get; }

        public int Seats { get; }

        public double SeatShare { get; }

        public bool HasMajority { get; }

        /// <summary>Members still in government, sorted ordinal ascending.</summary>
        public IReadOnlyList<string> MemberPartyIds { get; }

        /// <summary>Seat-holders outside government, sorted ordinal ascending.</summary>
        public IReadOnlyList<string> OppositionPartyIds { get; }

        /// <summary>Members that left this tick, sorted ordinal ascending.</summary>
        public IReadOnlyList<string> WithdrawnPartyIds { get; }

        public string LeadPartyId { get; }

        public CoalitionStatus Status { get; }

        public CoalitionCollapseReason CollapseReason { get; }

        /// <summary>Set when the government ended this tick, by collapse or by expiry.</summary>
        public SimDate? EndedDate { get; }

        /// <summary>
        /// When the fresh ballot falls, <c>coalitions.snapElectionDelayMonths</c> after the collapse.
        /// Null unless <see cref="Collapsed"/> — a term that simply expires uses the calendar.
        /// </summary>
        public SimDate? SnapElectionDate { get; }

        /// <summary>True when the government fell mid-term.</summary>
        public bool Collapsed => Status == CoalitionStatus.Collapsed;

        /// <summary>True when the government ended at all, by collapse or by expiry.</summary>
        public bool Ended => Status == CoalitionStatus.Collapsed || Status == CoalitionStatus.Expired;

        /// <summary>Stability lost to the monthly decay this tick (negative).</summary>
        public double DecayComponent { get; }

        /// <summary>Stability lost to defied mandates this tick (negative).</summary>
        public double MandateShockComponent { get; }

        /// <summary>Stability gained from fulfilled mandates this tick (positive).</summary>
        public double MandateRecoveryComponent { get; }

        /// <summary>Stability lost to major events this tick (negative).</summary>
        public double EventShockComponent { get; }

        /// <summary>Stability lost the tick a majority government became a minority one (negative).</summary>
        public double MinorityTransitionComponent { get; }

        /// <summary>Widest platform gap among the remaining members, <c>[0,1]</c>.</summary>
        public double MaxPairwiseDistance { get; }

        /// <summary>Writes this tick onto the government. Id, formation and mandates are left alone.</summary>
        public void ApplyTo(Coalition coalition)
        {
            if (coalition == null) throw new ArgumentNullException(nameof(coalition));

            coalition.MemberPartyIds = new List<string>(MemberPartyIds);
            coalition.OppositionPartyIds = new List<string>(OppositionPartyIds);
            coalition.LeadPartyId = LeadPartyId;
            coalition.Seats = Seats;
            coalition.SeatShare = SeatShare;
            coalition.HasMajority = HasMajority;
            coalition.Cohesion = Cohesion;
            coalition.Stability = Stability;
            coalition.Status = Status;
            coalition.CollapseReason = CollapseReason;
            coalition.EndedDate = EndedDate;
        }
    }

    /// <summary>
    /// A government's confidence over time, and the collapse that triggers a snap election
    /// (<c>politicsmodplan.md</c> §3).
    ///
    /// <para>
    /// Stability decays every month, drops when mandates are defied or a major event lands, and
    /// recovers when promises are kept. Below <c>coalitions.collapseThreshold</c> the government
    /// falls and the ballot is <c>coalitions.snapElectionDelayMonths</c> later.
    /// </para>
    ///
    /// <para>
    /// Pure and non-mutating: the caller decides when to write the result back with
    /// <see cref="CoalitionTickResult.ApplyTo"/>.
    /// </para>
    /// </summary>
    public static class CoalitionStability
    {
        /// <summary>
        /// Runs one confidence check, normally every <c>coalitions.collapseCheckIntervalMonths</c>.
        /// </summary>
        /// <param name="government">The sitting government. Not mutated.</param>
        /// <param name="inputs">What happened since the last check.</param>
        /// <param name="saveGuid">Save identity, for seed derivation.</param>
        /// <param name="date">The date of this check.</param>
        /// <param name="tuning">Engine tuning; the <c>coalitions</c> section is read, plus
        /// <c>catalog.majorSeverityThreshold</c> and <c>catalog.severityMax</c> for what counts as a shock.</param>
        public static CoalitionTickResult Advance(
            Coalition government,
            CoalitionTickInputs inputs,
            Guid saveGuid,
            SimDate date,
            EngineTuning tuning)
        {
            if (government == null) throw new ArgumentNullException(nameof(government));
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            CoalitionsTuning t = tuning.Coalitions;

            if (government.Status == CoalitionStatus.Collapsed || government.Status == CoalitionStatus.Expired)
                return Unchanged(government);

            // A government that was already governing in a minority is not re-judged against
            // `minorityGovernmentAllowed` every month — formation decided that once, and under FPTP a
            // mayor with a quarter of the chamber is the normal case, not a crisis.
            bool wasMinority = government.Status == CoalitionStatus.Minority;

            // --- who is still at the table --------------------------------------------------

            int totalSeats;
            List<PartySeat> chamber = CoalitionMath.BuildPool(inputs.Seats, inputs.Parties, out totalSeats);

            var storedMembers = new List<string>(government.MemberPartyIds ?? new List<string>());
            storedMembers.Sort(StringComparer.Ordinal);

            var gone = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < inputs.WithdrawnPartyIds.Count; i++) gone.Add(inputs.WithdrawnPartyIds[i]);

            // BuildPool already dropped dissolved and merged brands, so a member missing from the
            // chamber has either lost every seat or ceased to exist. Either way it is not governing.
            var stillSeated = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < chamber.Count; i++) stillSeated.Add(chamber[i].PartyId);

            var remaining = new List<string>();
            var withdrawn = new List<string>();
            bool haveChamber = chamber.Count > 0;

            for (int i = 0; i < storedMembers.Count; i++)
            {
                string memberId = storedMembers[i];
                bool left = gone.Contains(memberId) || (haveChamber && !stillSeated.Contains(memberId));
                if (left) withdrawn.Add(memberId);
                else remaining.Add(memberId);
            }

            List<PartySeat> members = haveChamber
                ? CoalitionMath.Select(chamber, remaining)
                : new List<PartySeat>();

            string leadPartyId = government.LeadPartyId ?? "";
            bool leadGone = withdrawn.Contains(leadPartyId) || remaining.Count == 0;
            if (!leadGone && members.Count > 0) leadPartyId = CoalitionMath.LeadOf(members).PartyId;

            int seats = government.Seats;
            double seatShare = government.SeatShare;
            double cohesion = government.Cohesion;
            double maxDistance = 0.0;

            if (haveChamber && members.Count > 0)
            {
                seats = CoalitionMath.SeatsOf(members);
                seatShare = CoalitionMath.ShareOf(seats, totalSeats);
                cohesion = CoalitionMath.Cohesion(CoalitionMath.MeanPairwiseDistance(members), t);
                maxDistance = CoalitionMath.MaxPairwiseDistance(members);
            }
            else if (remaining.Count == 0)
            {
                seats = 0;
                seatShare = 0.0;
            }

            bool hasMajority = seatShare >= t.MinSeatShareToGovern;
            List<string> opposition = haveChamber
                ? CoalitionMath.OppositionIds(chamber, remaining)
                : new List<string>(government.OppositionPartyIds ?? new List<string>());

            // --- confidence ------------------------------------------------------------------

            int months = inputs.MonthsElapsed < 0 ? 0 : inputs.MonthsElapsed;
            double decay = -t.StabilityDecayPerMonth * months;

            int failed = inputs.FailedMandates < 0 ? 0 : inputs.FailedMandates;
            int fulfilled = inputs.FulfilledMandates < 0 ? 0 : inputs.FulfilledMandates;
            double mandateShock = -t.StabilityShockPerFailedMandate * failed;
            double mandateRecovery = t.StabilityRecoveryPerFulfilledMandate * fulfilled;
            double eventShock = -t.StabilityShockPerSeverityPoint * MajorSeverityPoints(inputs.EventSeverities, tuning);

            double before = CoalitionMath.Clamp01(government.Stability);
            double stability = CoalitionMath.Clamp01(before + decay + mandateShock + mandateRecovery + eventShock);

            // A majority government reduced to a minority pays the penalty once, on the tick it slips.
            double minorityTransition = 0.0;
            if (!hasMajority && !wasMinority)
            {
                double after = CoalitionMath.Clamp01(stability * (1.0 - t.MinorityGovernmentPenalty));
                minorityTransition = after - stability;
                stability = after;
            }

            // --- does it survive --------------------------------------------------------------

            CoalitionStatus status = hasMajority ? CoalitionStatus.Governing : CoalitionStatus.Minority;
            CoalitionCollapseReason reason = CoalitionCollapseReason.None;
            SimDate? endedDate = null;
            SimDate? snapElection = null;

            if (inputs.TermExpired)
            {
                // A term that runs out ends normally even if confidence was low: the scheduled
                // election is already on the calendar, and a snap election would double-book it.
                status = CoalitionStatus.Expired;
                endedDate = date;
            }
            else if (leadGone || remaining.Count == 0 || (!hasMajority && !wasMinority && !t.MinorityGovernmentAllowed))
            {
                bool lostSeats = withdrawn.Count > 0 || remaining.Count == 0;
                if (lostSeats)
                {
                    status = CoalitionStatus.Collapsed;
                    reason = CoalitionCollapseReason.PartnerWithdrawal;
                    endedDate = date;
                    snapElection = CoalitionMath.SnapElectionDate(date, t);
                }
                else
                {
                    // No walkout, but the arrangement is short of the governing threshold and minority
                    // government is switched off in tuning. That is the same fall, minus the partner.
                    status = CoalitionStatus.Collapsed;
                    reason = CoalitionCollapseReason.StabilityDecay;
                    endedDate = date;
                    snapElection = CoalitionMath.SnapElectionDate(date, t);
                }
            }
            else if (DriftedApart(members, chamber, maxDistance, t, saveGuid, date, government.Id ?? ""))
            {
                status = CoalitionStatus.Collapsed;
                reason = CoalitionCollapseReason.IdeologicalDrift;
                endedDate = date;
                snapElection = CoalitionMath.SnapElectionDate(date, t);
            }
            else if (stability < t.CollapseThreshold)
            {
                status = CoalitionStatus.Collapsed;
                reason = DominantReason(decay, mandateShock, eventShock);
                endedDate = date;
                snapElection = CoalitionMath.SnapElectionDate(date, t);
            }

            return new CoalitionTickResult(
                stability, stability - before, cohesion,
                seats, seatShare, hasMajority,
                remaining, opposition, withdrawn, leadPartyId,
                status, reason, endedDate, snapElection,
                decay, mandateShock, mandateRecovery, eventShock,
                minorityTransition, maxDistance);
        }

        /// <summary>Sum of severity points from events major enough to shake a government.</summary>
        private static int MajorSeverityPoints(List<int> severities, EngineTuning tuning)
        {
            if (severities == null) return 0;

            int threshold = tuning.Catalog.MajorSeverityThreshold;
            int max = tuning.Catalog.SeverityMax;
            int points = 0;

            for (int i = 0; i < severities.Count; i++)
            {
                int severity = severities[i];
                if (severity < threshold) continue;
                if (severity > max) severity = max;
                points += severity;
            }

            return points;
        }

        /// <summary>
        /// Whether the members have drifted far enough apart to stop sitting together.
        /// </summary>
        /// <remarks>
        /// A hazard, not a cliff: the walk-out probability is the share of the remaining room past the
        /// cap, so a coalition just over the line usually holds on and one at maximum distance always
        /// falls. The draw comes from <c>StreamNames.CoalitionCollapse</c>, sub-streamed by coalition
        /// id, and the date is already in the seed — so each tick is an independent roll and none of
        /// it depends on iteration order.
        /// </remarks>
        private static bool DriftedApart(
            List<PartySeat> members, List<PartySeat> chamber, double maxDistance,
            CoalitionsTuning t, Guid saveGuid, SimDate date, string coalitionId)
        {
            if (members.Count < 2) return false;

            double cap = CoalitionMath.EffectiveDistanceCap(members, chamber, t, true);
            if (maxDistance <= cap) return false;
            if (cap >= 1.0) return true;

            double probability = CoalitionMath.Clamp01((maxDistance - cap) / (1.0 - cap));
            DeterministicRng rng = SeedStreams.RngFor(
                saveGuid, date, StreamNames.CoalitionCollapse, coalitionId);

            return rng.NextBool(probability);
        }

        /// <summary>
        /// Which pressure gets the blame. The largest negative contributor wins; an exact tie reads as
        /// ordinary decay, because "nothing in particular went wrong" is the honest answer there.
        /// </summary>
        private static CoalitionCollapseReason DominantReason(double decay, double mandateShock, double eventShock)
        {
            double d = Math.Abs(decay);
            double m = Math.Abs(mandateShock);
            double e = Math.Abs(eventShock);

            if (m > d && m >= e) return CoalitionCollapseReason.MandateFailure;
            if (e > d && e > m) return CoalitionCollapseReason.EventShock;
            return CoalitionCollapseReason.StabilityDecay;
        }

        private static CoalitionTickResult Unchanged(Coalition government)
        {
            var members = new List<string>(government.MemberPartyIds ?? new List<string>());
            members.Sort(StringComparer.Ordinal);

            var opposition = new List<string>(government.OppositionPartyIds ?? new List<string>());
            opposition.Sort(StringComparer.Ordinal);

            return new CoalitionTickResult(
                CoalitionMath.Clamp01(government.Stability), 0.0, government.Cohesion,
                government.Seats, government.SeatShare, government.HasMajority,
                members, opposition, new List<string>(), government.LeadPartyId ?? "",
                government.Status, government.CollapseReason,
                government.EndedDate, null,
                0.0, 0.0, 0.0, 0.0, 0.0, 0.0);
        }
    }
}
