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
    /// decision, and they read completely differently in the prose and in the command log.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>They do, however, score the same at resolution, and that is deliberate.</b> An earlier
    /// draft of this comment said only the explicit choice was "the player's fault" and let silence
    /// score as unmeasurable — which made doing nothing strictly cheaper than every response that
    /// could fail. Under shipped tuning, pressing <see cref="Ignore"/> on a mandatory event cost 25
    /// power while never opening the story cost nothing, so the rational play on anything you
    /// expected to lose was to leave it alone. That inverts the premise of a feature whose whole
    /// point is that the player must tackle each event, and it made the one response with its own
    /// text box the worst button on the screen.
    /// </para>
    /// <para>
    /// So an unaddressed slot, and a <see cref="Manual"/> slot still undeclared when its story
    /// resolves, both score as not-met. The story was open for a full cycle; declining to engage is
    /// a decision the city feels. <b>What it is not is a sensor gap</b> — see
    /// <see cref="SlotOutcome.Unmeasurable"/>, which keeps exactly one meaning: the engine could not
    /// read the city. Overloading it with "the player did not click" would make the engine tell a
    /// player it could not read their city about a story they simply never opened, and nothing
    /// downstream could separate an outage from disengagement again.
    /// </para>
    /// <para>
    /// The cost is real and was accepted knowingly: a player who never saw the card is charged for
    /// it. That leans on the story modal actually rendering, which is wave 6's manual gate — if that
    /// gate fails, this rule is the first thing to revisit.
    /// </para>
    /// </remarks>
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
        /// <remarks>
        /// <b>This means "the engine could not read the city", and nothing else.</b> It is not the
        /// outcome for a player who did not respond: silence scores <see cref="NotMet"/>, for the
        /// reasons on <see cref="SlotResponse"/>. Keeping the two apart is what lets a later reader
        /// tell a broken sensor from a disengaged player, and it is unrecoverable once merged.
        /// </remarks>
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

    /// <summary>One reading at one scope. Evidence, not state.</summary>
    /// <remarks>
    /// A reading is identified by <see cref="MetricId"/> <b>and</b> <see cref="DistrictId"/> together.
    /// Without the second half a district-scoped check resolved early could record nothing — there
    /// was nowhere to put a per-district value, and two districts' readings of one metric could not
    /// coexist — so on replay the evaluator found no recorded reading and re-measured a city that had
    /// since moved. That is a determinism hole rather than a cosmetic gap, and it is closed here
    /// rather than in wave 4 because wave 4 is what would have built on it.
    /// </remarks>
    public sealed class MetricReading
    {
        public string MetricId { get; set; } = "";

        /// <summary>
        /// The district this reading was taken in, or empty for a city-wide reading.
        /// </summary>
        /// <remarks>
        /// Part of the identity, not a decoration: a lookup must match on both fields. Matching on
        /// <see cref="MetricId"/> alone would let one district's recorded reading answer for another's,
        /// which is worse than having no record at all — it is a confident wrong answer.
        /// </remarks>
        public string DistrictId { get; set; } = "";

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
        /// The month this event was last drawn into a story, as <see cref="SimDate.TotalMonths"/>.
        /// <c>-1</c> means it has never been told.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>This is the re-use cooldown, and it replaces excluding everything the archive
        /// remembers.</b> That earlier rule drove the pool into an absorbing state: a live catalog of
        /// roughly forty events consumed six per cycle, so it emptied around month 14 — and then
        /// nothing drafted, so nothing resolved, so nothing archived, so the archive never evicted
        /// and never released an event back. The feature stopped for the rest of the save and logged
        /// "no stories drafted" politely once per cycle forever.
        /// </para>
        /// <para>
        /// The arithmetic that killed it is worth keeping written down, because it is the constraint
        /// any future rule has to satisfy: archive-based exclusion is only sound while
        /// <c>archiveRetention × eventsPerStory &lt; liveCatalogSize</c>. At the shipped 40 and 3 that
        /// caps retention at 12, whereas it ships at 40 — so the rule was only ever correct for an
        /// unbounded catalog.
        /// </para>
        /// <para>
        /// A cooldown has no such coupling: it is a duration, so it cannot exhaust a finite catalog
        /// however long the save runs. <c>stories.reuseCooldownMonths</c> must still be kept well
        /// under <c>liveCatalogSize ÷ (storiesPerCycle × eventsPerStory) × cycleMonths</c>, which is
        /// why it ships at 6 rather than at something that feels narratively generous.
        /// </para>
        /// </remarks>
        public int LastDraftedMonth { get; set; } = -1;

        /// <summary>
        /// A copy safe to mutate. <see cref="MissStreak"/> is incremented every cycle the entry is
        /// not drawn, so an alias would let a speculative advance age the caller's own pool.
        /// </summary>
        public EventPoolEntry Clone() => new EventPoolEntry
        {
            EventId = EventId,
            FirstTriggeredDate = FirstTriggeredDate,
            MissStreak = MissStreak,
            LastDraftedMonth = LastDraftedMonth
        };
    }
}
