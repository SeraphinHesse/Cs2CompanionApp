using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Events.Scheduler;
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
    }
}
