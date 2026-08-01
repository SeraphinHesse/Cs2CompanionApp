using System.Collections.Generic;

namespace Agora.Core.Contracts
{
    /// <summary>Lifecycle state of a coalition (EU theme).</summary>
    public enum CoalitionStatus
    {
        /// <summary>Talks are open; no government yet. Formation has a window and an attempt budget.</summary>
        Negotiating = 0,

        /// <summary>Governing with at least <c>coalitions.minSeatShareToGovern</c>.</summary>
        Governing = 1,

        /// <summary>Governing without a majority, carrying the minority-government stability penalty.</summary>
        Minority = 2,

        /// <summary>Fell below <c>coalitions.collapseThreshold</c>. A snap election follows.</summary>
        Collapsed = 3,

        /// <summary>Ended normally at the end of a term.</summary>
        Expired = 4
    }

    /// <summary>Why a coalition ended. An id, never prose — the LLM writes the story from this.</summary>
    public enum CoalitionCollapseReason
    {
        None = 0,

        /// <summary>Stability decayed past the threshold.</summary>
        StabilityDecay = 1,

        /// <summary>Defied mandates drained stability.</summary>
        MandateFailure = 2,

        /// <summary>A high-severity timeline event shocked the government.</summary>
        EventShock = 3,

        /// <summary>A partner's platform drifted past the ideological distance cap.</summary>
        IdeologicalDrift = 4,

        /// <summary>A partner walked out; seats fell below the governing threshold.</summary>
        PartnerWithdrawal = 5
    }

    /// <summary>
    /// A governing arrangement. Under the Proportional system this is a real coalition; under FPTP
    /// it is the single winning party plus the mayor, modelled with the same type so the dashboard
    /// and the mandate packet do not need two code paths.
    /// </summary>
    public sealed class Coalition
    {
        public int SchemaVersion { get; set; } = 1;

        /// <summary>Stable id, e.g. <c>"gov-1994-06"</c>.</summary>
        public string Id { get; set; } = "";

        public SimDate FormedDate { get; set; }

        public SimDate? EndedDate { get; set; }

        /// <summary>Member party ids, sorted ascending. Always contains <see cref="LeadPartyId"/>.</summary>
        public List<string> MemberPartyIds { get; set; } = new List<string>();

        /// <summary>The party that holds the leadership and writes the mandate list.</summary>
        public string LeadPartyId { get; set; } = "";

        /// <summary>Party ids not in government, sorted ascending.</summary>
        public List<string> OppositionPartyIds { get; set; } = new List<string>();

        /// <summary>Combined seats of the members.</summary>
        public int Seats { get; set; }

        /// <summary>Combined seat share, 0–1.</summary>
        public double SeatShare { get; set; }

        /// <summary>True when <see cref="SeatShare"/> is at or above <c>coalitions.minSeatShareToGovern</c>.</summary>
        public bool HasMajority { get; set; }

        /// <summary>
        /// How ideologically close the members are, 0–1 (1 = identical platforms). Set at formation
        /// and recomputed when a platform drifts.
        /// </summary>
        public double Cohesion { get; set; }

        /// <summary>
        /// Current confidence, 0–1. Decays monthly and takes shocks from failed mandates and severe
        /// events. Falling below <c>coalitions.collapseThreshold</c> collapses the government.
        /// </summary>
        public double Stability { get; set; }

        public CoalitionStatus Status { get; set; } = CoalitionStatus.Negotiating;

        public CoalitionCollapseReason CollapseReason { get; set; } = CoalitionCollapseReason.None;

        /// <summary>How many formation attempts were made before this arrangement stuck.</summary>
        public int FormationAttempts { get; set; }

        /// <summary>Election that produced this government.</summary>
        public string ElectionId { get; set; } = "";

        /// <summary>Mandate ids this government owns, sorted ascending.</summary>
        public List<string> MandateIds { get; set; } = new List<string>();
    }

    /// <summary>A measurable promise's state.</summary>
    public enum MandateStatus
    {
        /// <summary>Issued, inside its grace period; not yet scored.</summary>
        Pending = 0,

        /// <summary>Being monitored monthly against the live snapshot.</summary>
        Active = 1,

        /// <summary>Target met before the deadline.</summary>
        Fulfilled = 2,

        /// <summary>Progress passed <c>mandates.partialCreditThreshold</c> but missed the target.</summary>
        PartiallyFulfilled = 3,

        /// <summary>Deadline passed with progress below the partial-credit threshold.</summary>
        Defied = 4,

        /// <summary>Cancelled because the government fell or the district vanished. Never scored.</summary>
        Abandoned = 5
    }

    /// <summary>Which way the promise runs.</summary>
    public enum MandateDirection
    {
        /// <summary>Target is above baseline (e.g. raise transit ridership).</summary>
        Increase = 0,

        /// <summary>Target is below baseline (e.g. cut ground pollution 20%).</summary>
        Decrease = 1
    }

    /// <summary>
    /// The measurable quantity a mandate is scored against. Every member must be readable from
    /// <see cref="CitySnapshot"/> or <see cref="DistrictSnapshot"/> — if it cannot be measured, it
    /// cannot be a mandate.
    /// </summary>
    public enum MandateMetric
    {
        Happiness = 0,
        Unemployment = 1,
        AirPollution = 2,
        GroundPollution = 3,
        NoisePollution = 4,
        WaterPollution = 5,
        CrimeRate = 6,
        HealthCoverage = 7,
        EducationCoverage = 8,
        PoliceCoverage = 9,
        FireCoverage = 10,
        GarbageCoverage = 11,
        TransitCoverage = 12,
        AverageCommuteMinutes = 13,
        AverageRent = 14,
        AverageLandValue = 15,
        RentBurden = 16,
        Population = 17,
        BudgetBalance = 18,
        Debt = 19
    }

    /// <summary>
    /// A measurable promise generated from a real deficit — "cut District X ground pollution 20% in
    /// two years" (§3 Mandates). Monitored monthly against the live snapshot; the player is never
    /// punished beyond a sanctioned effect.
    /// </summary>
    /// <remarks>
    /// <see cref="Text"/> is flavor. Everything scored is numeric and engine-owned: the metric, the
    /// baseline, the target, the deadline. A mandate whose metric has no measurement this tick is
    /// held, not failed.
    /// </remarks>
    public sealed class Mandate
    {
        public int SchemaVersion { get; set; } = 1;

        /// <summary>Stable id, e.g. <c>"mandate-1994-06-02"</c>.</summary>
        public string Id { get; set; } = "";

        /// <summary>Party that owns the promise — the coalition lead, or the mayor's party.</summary>
        public string PartyId { get; set; } = "";

        /// <summary>Government that issued it.</summary>
        public string CoalitionId { get; set; } = "";

        /// <summary>Target district, or null for a city-wide promise.</summary>
        public string? DistrictId { get; set; }

        /// <summary>The issue this promise belongs to. Drives which blocs reward or punish it.</summary>
        /// <remarks>
        /// The property name deliberately matches its type. The initializer is fully qualified so the
        /// "identical simple names" resolution rule never has to be relied on by a future reader.
        /// </remarks>
        public Issue Issue { get; set; } = Agora.Core.Contracts.Issue.Services;

        public MandateMetric Metric { get; set; } = MandateMetric.Happiness;

        public MandateDirection Direction { get; set; } = MandateDirection.Increase;

        /// <summary>Measured value when the mandate was issued, in the metric's own units.</summary>
        public double BaselineValue { get; set; }

        /// <summary>Value that counts as fulfilment, in the metric's own units.</summary>
        public double TargetValue { get; set; }

        /// <summary>Most recent measurement. Updated every <c>mandates.monitoringIntervalMonths</c>.</summary>
        public double CurrentValue { get; set; }

        /// <summary>
        /// Fraction of the way from baseline to target, 0–1, clamped. 1 means the target is met;
        /// backsliding past the baseline reads as 0, not negative.
        /// </summary>
        public double Progress { get; set; }

        public SimDate IssuedDate { get; set; }

        /// <summary>Deadline. <c>mandates.horizonMonths</c> after issue unless overridden.</summary>
        public SimDate DeadlineDate { get; set; }

        public SimDate? ResolvedDate { get; set; }

        public MandateStatus Status { get; set; } = MandateStatus.Pending;

        /// <summary>
        /// How much voters care, 0–1, from the issue's weight among affected blocs. Scales the
        /// happiness stake at resolution.
        /// </summary>
        public double Salience { get; set; }

        /// <summary>
        /// Effect applied at resolution — the reward or the penalty. Must exist in the palette
        /// (<c>effects.perEffect</c>); the sink clamps its magnitude regardless.
        /// </summary>
        public string? ResolutionEffectId { get; set; }

        /// <summary>Human-readable promise. Flavor-owned; never parsed.</summary>
        public string Text { get; set; } = "";

        /// <summary>
        /// True while the metric could not be measured this tick (sensor gap or district removed).
        /// A held mandate does not accrue progress and does not fail on its deadline.
        /// </summary>
        public bool IsMeasurementStalled { get; set; }
    }
}
