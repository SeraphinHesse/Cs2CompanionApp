using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Events.Scheduler;
using Agora.Core.Stories;
using Agora.Core.Tuning;

namespace Agora.Core.Engine
{
    /// <summary>
    /// Everything one monthly tick is allowed to see. A plain input bag, so the whole engine stays a
    /// pure function of it plus <c>engine_tuning.json</c> (non-negotiable #3).
    ///
    /// <para>
    /// Note what is <em>not</em> here: no clock, no sink, no flavor provider, no filesystem. The tick
    /// computes; the caller applies. That is what lets the determinism suite replay a decade in
    /// milliseconds with no game installed, and it is why <see cref="EngineTickResult.EffectRequests"/>
    /// is a list to be dispatched rather than something already dispatched.
    /// </para>
    /// </summary>
    public sealed class EngineTickInput
    {
        private static readonly CitySnapshot[] NoSnapshots = new CitySnapshot[0];
        private static readonly TimelineEvent[] NoEvents = new TimelineEvent[0];
        private static readonly CivicEvent[] NoCivicEvents = new CivicEvent[0];

        /// <summary>Agora's save identity (§5). The first argument to every seed derivation.</summary>
        public Guid SaveGuid { get; set; }

        /// <summary>The date being ticked. From <c>AgoraTimeService</c> only (non-negotiable #8).</summary>
        public SimDate Date { get; set; }

        /// <summary>
        /// The save's first political date — the phase anchor for every cadence in
        /// <see cref="TickPlanner"/>. Constant for the life of the save.
        /// </summary>
        public SimDate StartDate { get; set; }

        /// <summary>
        /// Last tick's state. Never mutated: <see cref="PoliticalEngine.Advance"/> clones what it
        /// changes, so a caller can diff before against after, and a tick run twice from the same
        /// prior state produces two byte-identical results.
        /// </summary>
        public PoliticalState PriorState { get; set; } = new PoliticalState();

        /// <summary>The city as measured this tick. Null is tolerated and means "sensors gave us nothing".</summary>
        public CitySnapshot? Snapshot { get; set; }

        /// <summary>
        /// Earlier snapshots, oldest first and excluding <see cref="Snapshot"/>. Feeds the trend legs
        /// of the derived indices; an empty history simply zeroes those legs.
        /// </summary>
        public IReadOnlyList<CitySnapshot> SnapshotHistory { get; set; } = NoSnapshots;

        /// <summary>
        /// The loaded, validated timeline catalogs. Empty means the procedural generator is the only
        /// source of events, which is a degraded but valid save rather than an error.
        /// </summary>
        public IReadOnlyList<TimelineEvent> Catalog { get; set; } = NoEvents;

        /// <summary>Archetype pool for procedural events, or null for the built-in twelve.</summary>
        public IReadOnlyList<ProceduralArchetype>? Archetypes { get; set; }

        /// <summary>
        /// The loaded, validated civic-event catalog. Empty means no story can ever draft, which is a
        /// degraded save rather than an error — exactly as an empty <see cref="Catalog"/> is.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="Catalog"/> and not merged into it: a <see cref="TimelineEvent"/>
        /// fires on a date and a <see cref="Agora.Core.Stories.CivicEvent"/> triggers on a reading,
        /// and <c>TimelineEventAdapter</c> is the one sanctioned bridge between them. Handing the
        /// scheduler civic events, or the story assembler timeline ones, would let each subsystem
        /// silently answer the other's question.
        /// </remarks>
        public IReadOnlyList<CivicEvent> CivicCatalog { get; set; } = NoCivicEvents;

        /// <summary>
        /// True when this month is being replayed by load reconciliation or fast-forward rather than
        /// lived through.
        /// </summary>
        /// <remarks>
        /// <b>The story cycle is suspended entirely while this is set</b>, and the decision is taken
        /// here rather than discovered later — see
        /// <see cref="Agora.Core.Stories.StoryCycleInput.IsReplay"/> for the two hazards behind it.
        /// Nothing else in the tick reads it: every other subsystem was designed to be replayed and
        /// documents itself as scoring the replayed month against the present city.
        /// </remarks>
        public bool IsReplay { get; set; }

        /// <summary>True on the tick the player pressed the manual flavor button.</summary>
        public bool ManualFlavorWakeRequested { get; set; }

        /// <summary>Engine tuning. Null is read as <see cref="EngineTuning.Default"/>.</summary>
        public EngineTuning? Tuning { get; set; }
    }

    /// <summary>
    /// What one tick decided. The caller persists <see cref="State"/>, dispatches
    /// <see cref="EffectRequests"/> and logs <see cref="Warnings"/> — in that order, because a
    /// sidecar write that happens after a failed dispatch still has to describe the politics that
    /// were computed (non-negotiable #6).
    /// </summary>
    public sealed class EngineTickResult
    {
        /// <summary>
        /// The state after the tick. Always a distinct object from
        /// <see cref="EngineTickInput.PriorState"/>, even when nothing was due.
        /// </summary>
        public PoliticalState State { get; set; } = new PoliticalState();

        /// <summary>What the calendar said was due. Kept so the caller can log why little happened.</summary>
        public TickPlan Plan { get; set; }

        /// <summary>
        /// False when the tick fell between engine intervals. <see cref="State"/> is then a copy of the
        /// prior state with nothing but the date touched, and every list below is empty.
        /// </summary>
        public bool DidWork { get; set; }

        /// <summary>
        /// Capped effect requests, in (events, mandate resolutions) order. The sink clamps them again
        /// regardless — the engine may ask for anything (non-negotiable #5).
        /// </summary>
        public List<EffectRequest> EffectRequests { get; set; } = new List<EffectRequest>();

        /// <summary>Events that fired this tick, sorted as <see cref="SchedulerTick.Fired"/> is.</summary>
        public List<TimelineEvent> FiredEvents { get; set; } = new List<TimelineEvent>();

        /// <summary>The election resolved on this tick, or null — which is the case in all but one month per term.</summary>
        public ElectionResult? Election { get; set; }

        /// <summary>A poll published on this tick, or null.</summary>
        public PollResult? Poll { get; set; }

        /// <summary>True when a government formed, collapsed or expired this tick.</summary>
        public bool GovernmentChanged { get; set; }

        /// <summary>
        /// Why the flavor provider should wake, as flags — the engine's answer, gated by both the
        /// per-save cadence and the tuning switch. <see cref="LlmWakeCadence.None"/> means it should
        /// not. The engine never waits on the reply (non-negotiable #7).
        /// </summary>
        public LlmWakeCadence LlmWake { get; set; } = LlmWakeCadence.None;

        /// <summary>
        /// Ids the flavor cache may still trust: parties, active events and live mandates the engine
        /// currently recognises. Sorted ordinal ascending within each list by the assembling code.
        /// </summary>
        public List<string> KnownPartyIds { get; set; } = new List<string>();

        /// <summary>Non-fatal problems, in emission order. Log them; never throw on them.</summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// Stories opened on this tick, sorted by <c>Id</c> ordinal. Empty on every month that is not
        /// a draft phase — which is most of them, since the cadence is two months.
        /// </summary>
        /// <remarks>
        /// A report of what <see cref="State"/> already holds, not a second copy of it. The caller
        /// persists the state; these exist so prose, alerts and the log can name what is new without
        /// diffing two story lists.
        /// </remarks>
        public List<Story> DraftedStories { get; set; } = new List<Story>();

        /// <summary>
        /// Stories that reached a verdict on this tick, sorted by <c>Id</c> ordinal — including any
        /// the stranded sweep reaped as <see cref="StoryOutcome.Abandoned"/>.
        /// </summary>
        public List<Story> ResolvedStories { get; set; } = new List<Story>();

        /// <summary>
        /// Net signed political-power movement this tick: accrual plus awards minus penalties.
        /// </summary>
        /// <remarks>
        /// A summary for the log and the dashboard. <c>State.Power</c> is authoritative and its
        /// ledger is the itemisation; this is deliberately one number, so nothing downstream is
        /// tempted to reconstruct a balance by summing deltas across ticks it may not have seen.
        /// </remarks>
        public int PowerDelta { get; set; }
    }

    /// <summary>
    /// What <see cref="PoliticalEngine.Retheme"/> decided. A request, an answer and — only when the
    /// answer is yes and something actually moved — a new state.
    /// </summary>
    public sealed class RethemeResult
    {
        internal RethemeResult(CommandOutcome outcome, PoliticalState? state, bool changed)
        {
            Outcome = outcome;
            State = state;
            Changed = changed;
        }

        /// <summary>
        /// <see cref="CommandOutcome.Ok"/> when the request was honoured — including the no-op case
        /// where the save already runs the requested theme.
        /// </summary>
        public CommandOutcome Outcome { get; }

        /// <summary>
        /// The rethemed state on an accepted change; the caller's own <c>prior</c>, untouched, on a
        /// no-op or a rejection; null only when <c>prior</c> itself was null.
        /// </summary>
        public PoliticalState? State { get; }

        /// <summary>
        /// True only when <see cref="State"/> is a new object the caller must adopt. False on a
        /// rejection <i>and</i> on an accepted no-op, so one check covers "is there work to do".
        /// </summary>
        public bool Changed { get; }

        public bool Accepted
        {
            get { return Outcome == CommandOutcome.Ok; }
        }
    }
}
