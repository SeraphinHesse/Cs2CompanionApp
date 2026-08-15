using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Stories
{
    /// <summary>
    /// Which way a <see cref="TriggerSpec"/> or <see cref="CheckSpec"/> reads the city.
    /// </summary>
    /// <remarks>
    /// Declarative, never code per event. Fifty authored events stay <i>content</i> only while no
    /// event needs a C# branch of its own, so a kind that cannot be expressed here is a reason to
    /// widen this enum in a reviewed commit — not a reason to special-case one catalog entry.
    /// </remarks>
    public enum TriggerKind
    {
        /// <summary>A metric compared against a threshold, as of the evaluated month.</summary>
        Metric = 0,

        /// <summary>Change in a metric across <see cref="TriggerSpec.WindowMonths"/>.</summary>
        Delta = 1,

        /// <summary>A progression feature id being unlocked. Present-tense only — see the remarks
        /// on <see cref="TriggerSpec.WindowMonths"/>.</summary>
        Unlock = 2,

        /// <summary>A policy or setting being in force.</summary>
        Policy = 3,

        /// <summary>The negation of the same spec: fires when the condition does <i>not</i> hold.</summary>
        Absent = 4,

        /// <summary>
        /// Never fires from the city. Reserved for events the engine or the player introduces
        /// directly — a wrapped timeline event, or a mandatory civic event.
        /// </summary>
        Manual = 5
    }

    /// <summary>How a measured value is compared with <see cref="TriggerSpec.Threshold"/>.</summary>
    public enum Comparison
    {
        LessThan = 0,
        LessThanOrEqual = 1,
        GreaterThan = 2,
        GreaterThanOrEqual = 3
    }

    /// <summary>Which part of the city a spec reads.</summary>
    public enum TriggerScope
    {
        /// <summary>The city-wide reading.</summary>
        City = 0,

        /// <summary>Holds when at least one district satisfies it.</summary>
        AnyDistrict = 1,

        /// <summary>Holds when every district satisfies it.</summary>
        AllDistricts = 2
    }

    /// <summary>
    /// Mandatory / Major / Minor. <b>Derived, never stored</b> — see <see cref="StoryTiers"/>.
    /// </summary>
    public enum StoryTier
    {
        Minor = 0,
        Major = 1,
        Mandatory = 2
    }

    /// <summary>
    /// The projection of the 1–5 <c>Severity</c> integer onto <see cref="StoryTier"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>There is exactly one number per concept and this is not a new one.</b> Severity is already
    /// the single definition of "how big is this", shared by <c>EventScheduler.IsMajor</c>,
    /// <c>CoalitionStability</c> and <c>AgoraRuntime.RaiseEventAlerts</c>, and
    /// <c>docs/contracts/ui_bindings.md</c> §4.5 states in bold that the UI must <b>never</b>
    /// re-derive it. A fourth vocabulary would drift on the next tuning pass, and keeping the integer
    /// is also what keeps <c>AffinityEngine.EventTerm</c>'s <c>severity/5</c> scaling honest.
    /// </para>
    /// <para>
    /// Both thresholds are inclusive lower bounds, and both live in <c>stories</c> tuning rather than
    /// in C# (<c>data/CLAUDE.md</c> rule 4).
    /// </para>
    /// </remarks>
    public static class StoryTiers
    {
        /// <summary>
        /// The tier a severity projects onto. <paramref name="mandatoryThreshold"/> wins ties with
        /// <paramref name="majorThreshold"/>, so a misconfiguration that inverts them degrades to
        /// "everything big is mandatory" rather than to an unreachable tier.
        /// </summary>
        public static StoryTier Of(int severity, int mandatoryThreshold, int majorThreshold)
        {
            if (severity >= mandatoryThreshold) return StoryTier.Mandatory;
            if (severity >= majorThreshold) return StoryTier.Major;
            return StoryTier.Minor;
        }
    }

    /// <summary>
    /// One declarative reading of the city. Serves both roles: as a <c>Trigger</c> it decides whether
    /// an event may enter the pool, and as a <c>Check</c> it decides whether a slot the player took a
    /// goal on was met.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One evaluator, two callers.</b> <c>TriggerEvaluator</c> serves both, so a threshold means
    /// the same thing at draft as at resolution. <see cref="CheckSpec"/> is a distinct type only
    /// because a check has a baseline a trigger does not.
    /// </para>
    /// <para>
    /// <b><see cref="MetricId"/> resolves through <c>MetricRegistry</c>, and that is what makes an
    /// unreachable trigger a load-time catalog error rather than a runtime surprise.</b> The
    /// vocabulary is the one wave 1 built and recorded in <c>docs/plans/0004-wave-1-lanes.md</c>: 18
    /// city-scope names and 3 district-scope ones. A name may be <b>added but never renamed</b>,
    /// exactly like a seed stream name.
    /// </para>
    /// </remarks>
    public sealed class TriggerSpec
    {
        public TriggerKind Kind { get; set; } = TriggerKind.Manual;

        /// <summary>
        /// A name from the metric registry. Empty for <see cref="TriggerKind.Manual"/>.
        /// </summary>
        public string MetricId { get; set; } = "";

        public Comparison Comparison { get; set; } = Comparison.GreaterThanOrEqual;

        public double Threshold { get; set; }

        /// <summary>
        /// Months of history a <see cref="TriggerKind.Delta"/> reads back over. Ignored by every
        /// other kind.
        /// </summary>
        /// <remarks>
        /// <b>No <see cref="TriggerKind.Delta"/> may name <c>unlockedFeatureIds</c> or
        /// <c>industryTaxRates</c>.</b> Both are lists, and <c>MetricHistory</c> stores one
        /// <c>double</c> per series per month, so no historical series stands behind either — the
        /// omission is a decision recorded in <c>MetricHistory</c> and <c>SnapshotRehydration</c>,
        /// not an oversight to be repaired by adding one. A spec may ask what is unlocked
        /// <i>today</i>; it may not ask what changed.
        /// </remarks>
        public int WindowMonths { get; set; }

        public TriggerScope Scope { get; set; } = TriggerScope.City;
    }

    /// <summary>
    /// The resolution-time reading of a slot the player took a <c>Goal</c> on.
    /// </summary>
    public sealed class CheckSpec
    {
        /// <summary>The reading itself. Same grammar and same evaluator as a trigger.</summary>
        public TriggerSpec Spec { get; set; } = new TriggerSpec();

        /// <summary>
        /// True when the threshold is relative to the reading captured at the story's open
        /// (<c>StorySlot.BaselineMetric</c>) rather than absolute.
        /// </summary>
        /// <remarks>
        /// This is what makes a two-month cycle mean something: drafting at M and resolving at M+1 is
        /// a genuinely later measurement, so a delta measured from the month the story started is a
        /// real question about what the player did.
        /// </remarks>
        public bool RelativeToBaseline { get; set; }
    }

    /// <summary>
    /// The verdict on one <see cref="CheckSpec"/>. <b>Three states, not two.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Unmeasurable"/> exists because a deleted district, a sensor that fell back to a
    /// city value, or a metric with no reading must <b>not</b> score as failure — that would cost the
    /// player political power for a sensor gap. <c>ui_bindings.md</c> §4.5 already writes this rule
    /// for the identical mandate case: <i>"held, not failing … never show it as Defied because the
    /// clock ran out while its metric was unreadable."</i>
    /// </para>
    /// <para>
    /// An <see cref="Unmeasurable"/> slot is excluded from <b>both</b> the numerator and the
    /// denominator of the 2-of-3 — see <c>StoryResolution</c>.
    /// </para>
    /// <para>
    /// <b>It must not be built on an assumption about zero-versus-absent.</b>
    /// <c>CityStatisticsSystem.GetStatisticValueLong</c> returns <c>0</c> for a genuine zero, for a
    /// statistic locked behind progression, and for a key that does not exist, with no way to tell
    /// them apart (scout 0004 §1.7, Q1 — still open). The sensor layer invents no sentinel. So
    /// unmeasurability is answered from the <i>markers</i> — a district's
    /// <c>CityFallbackFields</c> on the live snapshot, and the presence or absence of a recorded
    /// sample in <c>MetricHistory</c> for a historical month. Two surfaces, two mechanisms:
    /// <c>SnapshotRehydration</c> rebuilds a district from recorded samples alone, so its
    /// <c>HasCityFallbacks</c> comes back <b>false whatever the original month looked like</b>, and
    /// asking a rehydrated district "did you fall back?" gets the one wrong answer the arrangement
    /// exists to prevent.
    /// </para>
    /// </remarks>
    public enum CheckResult
    {
        NotMet = 0,
        Met = 1,
        Unmeasurable = 2
    }

    /// <summary>
    /// One authored civic event: the unit a story is assembled from, and the unit the player tackles.
    /// </summary>
    /// <remarks>
    /// Content, not code. Every field here is authored in <c>data/events_*.json</c> and validated at
    /// load; the engine reads this type and never branches on an event id.
    /// </remarks>
    public sealed class CivicEvent
    {
        public string Id { get; set; } = "";

        /// <summary>
        /// The existing 1–5 integer. <b>Not a new enum</b> — <see cref="StoryTiers"/> projects it.
        /// </summary>
        public int Severity { get; set; } = 1;

        public EventRegion Region { get; set; } = EventRegion.Global;

        /// <summary>What has to be true of the city for this event to enter the pool.</summary>
        public TriggerSpec Trigger { get; set; } = new TriggerSpec();

        /// <summary>What has to become true for a <c>Goal</c> response to succeed.</summary>
        public CheckSpec Check { get; set; } = new CheckSpec();

        /// <summary>Effect ids applied while the story is live, sorted by id.</summary>
        public List<string> ActiveEffects { get; set; } = new List<string>();

        /// <summary>Effect ids applied when the slot resolves met, sorted by id.</summary>
        public List<string> SuccessEffects { get; set; } = new List<string>();

        /// <summary>Effect ids applied when the slot resolves not-met, sorted by id.</summary>
        public List<string> FailureEffects { get; set; } = new List<string>();

        /// <summary>
        /// Voter pressure while live. Together with the two below this is the mechanism the whole
        /// rework exists for: a positive outcome moves voters toward the government and a negative
        /// one away.
        /// </summary>
        public IssuePosition ActivePressure { get; set; } = IssuePosition.Centre;

        public IssuePosition SuccessPressure { get; set; } = IssuePosition.Centre;

        public IssuePosition FailurePressure { get; set; } = IssuePosition.Centre;

        /// <summary>
        /// District archetypes that feel this hardest, sorted ordinal. Empty means "evenly".
        /// </summary>
        public List<string> DistrictAffinity { get; set; } = new List<string>();

        /// <summary>Free-form classification, sorted ordinal.</summary>
        public List<string> Tags { get; set; } = new List<string>();

        // ---------------------------------------------------------------- the seven prose fields
        //
        // Authored, never generated into engine state. The LLM may rewrite how these READ (wave 5);
        // no number may ever originate from them (non-negotiable #1).

        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string IgnoreText { get; set; } = "";
        public string GoalText { get; set; } = "";
        public string PowerOverrideText { get; set; } = "";
        public string SuccessText { get; set; } = "";
        public string FailText { get; set; } = "";

        /// <summary>
        /// The tier this event projects onto under the given thresholds. Convenience over
        /// <see cref="StoryTiers.Of"/> so no caller re-derives the rule.
        /// </summary>
        public StoryTier TierUnder(int mandatoryThreshold, int majorThreshold)
        {
            return StoryTiers.Of(Severity, mandatoryThreshold, majorThreshold);
        }
    }
}
