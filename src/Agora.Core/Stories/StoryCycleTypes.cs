using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Stories
{
    /// <summary>
    /// Everything one story cycle is allowed to see. A closed input, for the same reason
    /// <see cref="StoryReadContext"/> is one: the engine is a pure function of its inputs, and
    /// handing the cycle the whole tick would make "what did this draft depend on?" unanswerable.
    /// </summary>
    public sealed class StoryCycleInput
    {
        private static readonly CivicEvent[] NoEvents = new CivicEvent[0];

        /// <summary>
        /// The tick's working state — already cloned by <c>PoliticalEngine.Advance</c>, so the cycle
        /// may write to it directly. It is never the caller's own prior state.
        /// </summary>
        public PoliticalState State { get; set; } = new PoliticalState();

        /// <summary>The loaded, validated civic catalog. Empty means nothing can draft, which is a degraded save rather than an error.</summary>
        public IReadOnlyList<CivicEvent> Catalog { get; set; } = NoEvents;

        /// <summary>Today's readings plus history. Never null.</summary>
        public StoryReadContext Context { get; set; } = new StoryReadContext();

        /// <summary>Agora's save identity. The first argument to every seed derivation.</summary>
        public Guid SaveGuid { get; set; }

        /// <summary>The date being ticked. From the tick, never computed here (non-negotiable #8).</summary>
        public SimDate Today { get; set; }

        /// <summary>True on the cycle's draft phase — <c>elapsed % stories.cycleMonths == 0</c>.</summary>
        public bool IsStoryDraft { get; set; }

        /// <summary>True on the cycle's resolve phase — <c>elapsed % stories.cycleMonths == 1</c>.</summary>
        public bool IsStoryResolve { get; set; }

        /// <summary>
        /// True when this month is being replayed by catch-up rather than lived.
        /// </summary>
        /// <remarks>
        /// <b>Drafting and resolution are suspended entirely when this is set</b>, and the decision is
        /// taken here rather than discovered later. Two hazards make the alternative worse than doing
        /// nothing. Replay does not dispatch effects (<c>AgoraRuntime.cs:2855-2857</c>), so a story
        /// drafted and resolved inside a replayed window would award political power while applying
        /// none of its effects — scoring one half silently. And replay scores every replayed month
        /// against <i>today's</i> city, which it is documented as doing, so a <c>CheckSpec</c> in a
        /// replayed window would evaluate 2005's crime wave against 2031's crime rate: deterministic,
        /// and nonsense. A replayed decade producing no stories and no power is honest; inventing
        /// either would be fiction the player never got to participate in.
        /// </remarks>
        public bool IsReplay { get; set; }

        /// <summary>
        /// The governing party's or coalition's current vote share, 0–1. Zero when nobody governs,
        /// which yields no accrual rather than a debit.
        /// </summary>
        public double GoverningVoteShare { get; set; }

        /// <summary>Engine tuning. Never null at the call site — the stage substitutes the default.</summary>
        public EngineTuning Tuning { get; set; } = EngineTuning.Default;
    }

    /// <summary>
    /// What one story cycle decided. Pure: <see cref="StoryCycleInput.State"/> has been written, and
    /// everything else here is for the tick to fold into its own result.
    /// </summary>
    public sealed class StoryCycleResult
    {
        /// <summary>Stories opened this cycle, sorted by <c>Id</c> ordinal.</summary>
        public List<Story> DraftedStories { get; set; } = new List<Story>();

        /// <summary>
        /// Stories that reached a verdict this cycle, sorted by <c>Id</c> ordinal. Includes the
        /// <see cref="StoryOutcome.Abandoned"/> ones the stranded sweep reaped.
        /// </summary>
        public List<Story> ResolvedStories { get; set; } = new List<Story>();

        /// <summary>
        /// Capped effect requests, in (active, resolution, debt) order. The sink clamps them again
        /// regardless — the engine may ask for anything (non-negotiable #5).
        /// </summary>
        public List<EffectRequest> EffectRequests { get; set; } = new List<EffectRequest>();

        /// <summary>
        /// What the voter model should read this tick, sorted by <c>StoryId</c> ordinal.
        /// </summary>
        public List<StoryPressureContribution> Pressures { get; set; } =
            new List<StoryPressureContribution>();

        /// <summary>
        /// The net signed political-power movement this cycle: accrual plus awards minus penalties.
        /// A report, not an instruction — the ledger on <see cref="StoryCycleInput.State"/> is
        /// already authoritative.
        /// </summary>
        public int PowerDelta { get; set; }

        /// <summary>Non-fatal problems, in emission order. Log them; never throw on them.</summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
