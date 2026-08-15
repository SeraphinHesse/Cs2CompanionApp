using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Stories
{
    /// <summary>Where a slot sits in its story's narrative.</summary>
    public enum SlotRole
    {
        Minor = 0,
        Major = 1
    }

    /// <summary>
    /// How the player chose to tackle one event. <see cref="Unaddressed"/> is the state before they
    /// chose, and is <b>not</b> the same as <see cref="Ignore"/> — one is silence, the other is a
    /// decision, and only the second is the player's fault.
    /// </summary>
    public enum SlotResponse
    {
        Unaddressed = 0,
        Ignore = 1,
        Goal = 2,
        PowerOverride = 3,
        Manual = 4
    }

    /// <summary>How one slot came out. <see cref="Pending"/> until its story resolves.</summary>
    public enum SlotOutcome
    {
        Pending = 0,
        Met = 1,
        NotMet = 2,

        /// <summary>
        /// The check could not be read. Excluded from both halves of the success ratio and costs the
        /// player nothing — see <see cref="CheckResult.Unmeasurable"/> for why this is a state rather
        /// than a failure.
        /// </summary>
        Unmeasurable = 3
    }

    /// <summary>How a whole story came out.</summary>
    public enum StoryOutcome
    {
        Pending = 0,
        Success = 1,
        Failure = 2,

        /// <summary>
        /// Resolved without a verdict because its evidence was gone — the stranded-story sweep found
        /// it after a catch-up truncation. Pays out nothing in either direction.
        /// </summary>
        Abandoned = 3
    }

    /// <summary>
    /// One event inside a story, plus the player's response to it and the verdict on that response.
    /// </summary>
    public sealed class StorySlot
    {
        public string EventId { get; set; } = "";

        public SlotRole Role { get; set; } = SlotRole.Minor;

        public SlotResponse Response { get; set; } = SlotResponse.Unaddressed;

        /// <summary>
        /// The player's own words, for <see cref="SlotResponse.Ignore"/> and
        /// <see cref="SlotResponse.Manual"/>. Capped at <c>stories.freeTextMaxLength</c>.
        /// </summary>
        /// <remarks>
        /// <b>Prose, and treated as such: never parsed for a number</b>, exactly as non-negotiable #1
        /// requires of LLM output. Over-length input is rejected with the existing
        /// <c>CommandOutcome.TooLong</c> — reuse, do not add a new code.
        /// </remarks>
        public string PlayerText { get; set; } = "";

        /// <summary>
        /// The check's metric as read at the story's open, so a relative check is measured against
        /// the month it started rather than against whatever the city looked like at resolution.
        /// </summary>
        /// <remarks>
        /// Null when the metric was unreadable at open. A null baseline makes a
        /// <c>RelativeToBaseline</c> check <see cref="SlotOutcome.Unmeasurable"/> rather than failed:
        /// there is no honest verdict to reach without the number the comparison is against.
        /// </remarks>
        public double? BaselineMetric { get; set; }

        public SlotOutcome SlotOutcome { get; set; } = SlotOutcome.Pending;

        /// <summary>
        /// True once the player has declared their own outcome on a <see cref="SlotResponse.Manual"/>
        /// slot. Until then the slot is neutral rather than failing.
        /// </summary>
        public bool ManualDeclared { get; set; }

        /// <summary>A field-by-field copy. Hand-maintained — see <c>PoliticalEngine.CloneState</c>.</summary>
        public StorySlot Clone() => new StorySlot
        {
            EventId = EventId,
            Role = Role,
            Response = Response,
            PlayerText = PlayerText,
            BaselineMetric = BaselineMetric,
            SlotOutcome = SlotOutcome,
            ManualDeclared = ManualDeclared
        };
    }

    /// <summary>
    /// One month's narrative: a major event and two minors, bundled, tackled and resolved together.
    /// </summary>
    public sealed class Story
    {
        public string Id { get; set; } = "";

        /// <summary>The month this story drafted on.</summary>
        public SimDate OpenedDate { get; set; }

        /// <summary>The month it is due to resolve on — <c>stories.cycleMonths</c> later.</summary>
        public SimDate ResolvesDate { get; set; }

        /// <summary>
        /// True for the bare single-slot story a mandatory event gets. A mandatory story is not
        /// drawn, not weighted and not degraded; it exists because the event fired.
        /// </summary>
        public bool IsMandatory { get; set; }

        /// <summary>
        /// The events in this story. Sorted <see cref="SlotRole.Major"/> first, then by
        /// <c>EventId</c> ordinal — a declared total order, because "which minor is first" left to
        /// collection order is the determinism bug <c>Agora.Core/CLAUDE.md</c> calls the most common.
        /// </summary>
        public List<StorySlot> Slots { get; set; } = new List<StorySlot>();

        public StoryOutcome Outcome { get; set; } = StoryOutcome.Pending;

        /// <summary>
        /// Set by the <c>Resolve now</c> command. The resolution then reads
        /// <see cref="ResolutionEvidence"/> rather than re-measuring.
        /// </summary>
        public bool ResolveEarlyRequested { get; set; }

        /// <summary>
        /// The metric readings the resolution was scored against, keyed by metric id and sorted
        /// ordinal.
        /// </summary>
        /// <remarks>
        /// <b>Recorded, not re-measured — and this is what keeps an early resolve deterministic.</b>
        /// A player command's firing time is already exogenous, so the <c>Resolve now</c> path may
        /// force a fresh sample; persisting that sample into the story record means replay reads the
        /// recorded evidence rather than sampling a different city. It is the same trick that makes
        /// the command log deterministic.
        /// </remarks>
        public List<MetricReading> ResolutionEvidence { get; set; } = new List<MetricReading>();

        /// <summary>
        /// The headline to show when no LLM prose exists for this story. Per the design document's
        /// fallback: the major event's <c>Name</c>.
        /// </summary>
        public string HeadlineFallback { get; set; } = "";

        /// <summary>Flavor cache key for the opening article.</summary>
        public string FlavorKey { get; set; } = "";

        /// <summary>Flavor cache key for the resolution article.</summary>
        public string ResolutionFlavorKey { get; set; } = "";

        /// <summary>Month this story resolved, as <c>SimDate.TotalMonths</c>. -1 while pending.</summary>
        public int ResolvedMonth { get; set; } = -1;

        /// <summary>
        /// A copy safe to mutate, slots included.
        /// </summary>
        /// <remarks>
        /// <b>The slots are deep-copied and that is the whole point.</b> A slot carries a mutable
        /// response and outcome, so sharing the instances would let a speculative advance write the
        /// player's choice into the prior state the caller still holds — the same hazard the
        /// <c>Fringe</c> watch is deep-cloned for, and the one <c>ActiveEvents</c> still has because
        /// it is only a shallow list copy.
        /// </remarks>
        public Story Clone()
        {
            var slots = new List<StorySlot>();
            List<StorySlot> source = Slots ?? new List<StorySlot>();
            for (int i = 0; i < source.Count; i++) slots.Add(source[i].Clone());

            return new Story
            {
                Id = Id,
                OpenedDate = OpenedDate,
                ResolvesDate = ResolvesDate,
                IsMandatory = IsMandatory,
                Slots = slots,
                Outcome = Outcome,
                ResolveEarlyRequested = ResolveEarlyRequested,
                // Evidence is written once at resolution and never edited afterwards, so the entries
                // are shared like an election result rather than copied like a slot.
                ResolutionEvidence =
                    new List<MetricReading>(ResolutionEvidence ?? new List<MetricReading>()),
                HeadlineFallback = HeadlineFallback,
                FlavorKey = FlavorKey,
                ResolutionFlavorKey = ResolutionFlavorKey,
                ResolvedMonth = ResolvedMonth
            };
        }
    }

    /// <summary>One metric id and the value read for it. Evidence, not state.</summary>
    public sealed class MetricReading
    {
        public string MetricId { get; set; } = "";

        /// <summary>Null means the metric was unreadable, which is a distinct claim from zero.</summary>
        public double? Value { get; set; }
    }

    /// <summary>
    /// An event that has triggered and is waiting to be drawn into a story.
    /// </summary>
    public sealed class EventPoolEntry
    {
        public string EventId { get; set; } = "";

        /// <summary>When it first became eligible.</summary>
        public SimDate FirstTriggeredDate { get; set; }

        /// <summary>
        /// Cycles this entry was eligible for and not drawn. Feeds the pity weighting, capped at
        /// <c>stories.maxMissStreak</c> so an ancient entry cannot crowd out everything else forever.
        /// </summary>
        public int MissStreak { get; set; }

        /// <summary>
        /// A copy safe to mutate. <see cref="MissStreak"/> is incremented every cycle the entry is
        /// not drawn, so an alias would let a speculative advance age the caller's own pool.
        /// </summary>
        public EventPoolEntry Clone() => new EventPoolEntry
        {
            EventId = EventId,
            FirstTriggeredDate = FirstTriggeredDate,
            MissStreak = MissStreak
        };
    }
}
