using System;
using System.Collections.Generic;
using System.Globalization;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Agora.Core.Events.Scheduler;
using Agora.Core.Tuning;

namespace Agora.Core.Stories
{
    /// <summary>
    /// Drafts one cycle's stories: pool in, weighted seeded draw, stories out.
    /// </summary>
    public static class StoryAssembler
    {
        /// <summary>
        /// Refreshes the pool from the catalog's triggers and draws this cycle's stories.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Every degradation is a valid outcome, not an error.</b> No major left in the pool ⇒
        /// promote the highest-ordered minor and take three minors; too few events ⇒ a shorter story
        /// is a story. Each mandatory event additionally gets its own bare single-slot story, over
        /// and above <c>stories.storiesPerCycle</c> — a mandatory event is not drawn, it is delivered.
        /// </para>
        /// <para>
        /// After the draw, every entry that was passed over has its <c>MissStreak</c> incremented.
        /// That aging is what the pity weighting reads. A drawn entry leaves the drawable set but
        /// <b>stays in the pool as its own cooldown record</b> — streak reset, <c>LastDraftedMonth</c>
        /// stamped — because the stamp is the only thing that remembers when the event was told, and
        /// dropping the entry would hand it straight back next cycle with a clean slate.
        /// </para>
        /// <para>
        /// <b>Seeded from <c>StreamNames.StoryDraft</c> and <c>StoryPool</c>, tie-broken through
        /// <c>StoryTiebreak</c> — never a coin flip in place.</b> Drawing twice from the same seed
        /// must produce byte-identical stories, which is what lane 2e's determinism test asserts.
        /// </para>
        /// <para>
        /// <b>Eligibility, and which way round <see cref="CheckResult.Unmeasurable"/> falls.</b> An
        /// event enters the pool only on <see cref="CheckResult.Met"/>. <c>NotMet</c> evicts it: its
        /// trigger no longer holds, so it stops waiting. <c>Unmeasurable</c> does <b>neither</b> — an
        /// entry already pooled stays pooled and keeps aging, but is not drawn this cycle, and a new
        /// event is not admitted on a reading nobody could take. That is the direction that is safe
        /// for the player: an unreadable trigger drafted into a story would almost certainly meet an
        /// unreadable check at resolution, so the player would be handed an obligation that could
        /// never be scored, while the entry keeping its streak costs them nothing.
        /// </para>
        /// <para>
        /// <b>An evicted entry's <c>MissStreak</c> is discarded, and that is a decision.</b> A
        /// trigger that stopped holding means the event stopped being pending; when the city brings
        /// it back it is a new occurrence rather than a resumed one, and it queues from the back like
        /// any other. Carrying the streak across the gap would let an event that lapsed for a decade
        /// return already at the top of the order.
        /// </para>
        /// <para>
        /// <b>"Already used" means named by a slot of a story in <c>LiveStories</c> — the archive is
        /// not consulted.</b> Live is forced: one event cannot run in two stories at once. Re-use is
        /// then gated on a <i>duration</i>, <c>stories.reuseCooldownMonths</c> measured against
        /// <c>EventPoolEntry.LastDraftedMonth</c>, and not on what the archive still remembers.
        /// Archive exclusion is only sound while
        /// <c>archiveRetention × eventsPerStory &lt; liveCatalogSize</c>; at the shipped 40 and 3 it
        /// names 120 slots over a live catalog of roughly 40, so it excludes every event several
        /// times over and the pool empties for good around month 14 — and once nothing drafts,
        /// nothing resolves, nothing archives, and the archive never releases an event back. A
        /// duration has no such coupling and cannot exhaust a finite catalog however long the save
        /// runs. The full arithmetic is on <see cref="EventPoolEntry.LastDraftedMonth"/>.
        /// </para>
        /// <para>
        /// <b>Mandatory events ignore the cooldown entirely, and are bounded by
        /// <c>stories.maxMandatoryPerCycle</c> instead.</b> A mandatory trigger is a statement about
        /// the city right now, so suppressing it because the same event was told two years ago would
        /// drop a genuine crisis with no story, no power movement and no prose. Excess mandatory
        /// events are not dropped either: they stay in the pool, age normally, and arrive next cycle.
        /// </para>
        /// </remarks>
        public static StoryDraftResult Draft(PoliticalState prior,
                                             IReadOnlyList<CivicEvent> catalog,
                                             StoryReadContext context,
                                             Guid saveGuid,
                                             SimDate today,
                                             EngineTuning tuning)
        {
            if (prior == null) throw new ArgumentNullException(nameof(prior));
            if (tuning == null) tuning = EngineTuning.Default;
            if (context == null) context = new StoryReadContext();

            StoriesTuning stories = tuning.Stories;
            AgoraSettings settings = prior.Settings ?? new AgoraSettings();

            var result = new StoryDraftResult();

            // Both halves of the master switch. Off is inert rather than empty-and-aged: nothing
            // drafts and nothing ages, so turning the layer back on resumes where it left off
            // instead of handing the player a pool that pitied itself while they were not looking.
            if (!stories.Enabled || !settings.StoriesEnabled)
            {
                result.UpdatedPool = ClonedPool(prior.EventPool);
                return result;
            }

            // --- 1. Pool refresh. Walks the catalog in id order, so the pool never inherits the
            // order the loader happened to produce.
            var eventsById = new Dictionary<string, CivicEvent>(StringComparer.Ordinal);
            List<CivicEvent> sortedCatalog = SortedCatalog(catalog, settings.Theme, tuning, eventsById);

            var carried = PoolByEventId(prior.EventPool);
            var live = LiveEventIds(prior);

            // Drawable candidates are split by the tier the severity projects onto. Tier is derived
            // here and nowhere else: StoryTiers.Of is the single definition of "is this major".
            var pool = new List<EventPoolEntry>();
            var cooling = new HashSet<string>(StringComparer.Ordinal);
            var mandatory = new List<Candidate>();
            var majors = new List<Candidate>();
            var minors = new List<Candidate>();

            for (int i = 0; i < sortedCatalog.Count; i++)
            {
                CivicEvent ev = sortedCatalog[i];

                // A Manual trigger never fires from the city — the engine or the player introduces
                // those events directly, so the refresh must not adopt them.
                if (ev.Trigger == null || ev.Trigger.Kind == TriggerKind.Manual) continue;

                EventPoolEntry existing;
                bool pooled = carried.TryGetValue(ev.Id, out existing);

                StoryTier tier = ev.TierUnder(stories.MandatorySeverityThreshold,
                                              stories.MajorSeverityThreshold);

                // The cooldown is held on the entry, so the entry has to survive the months it is
                // counting — including the cycle its story is live and any month its trigger lapses.
                // Dropping it would hand the event straight back with a clean LastDraftedMonth, which
                // is the cooldown doing nothing at all.
                if (pooled && tier != StoryTier.Mandatory && OnCooldown(existing, today, stories))
                {
                    pool.Add(existing.Clone());
                    cooling.Add(ev.Id);
                    continue;
                }

                // A live event is not waiting to be drawn — it is being told.
                if (live.Contains(ev.Id)) continue;

                CheckResult eligibility = TriggerEvaluator.Evaluate(ev.Trigger, context);
                if (eligibility == CheckResult.NotMet) continue;
                if (eligibility == CheckResult.Unmeasurable && !pooled) continue;

                EventPoolEntry entry = pooled
                    ? existing.Clone()
                    : new EventPoolEntry { EventId = ev.Id, FirstTriggeredDate = today, MissStreak = 0 };
                pool.Add(entry);

                // Only a Met entry may be drawn this cycle. An Unmeasurable one waits and ages.
                if (eligibility != CheckResult.Met) continue;

                var candidate = new Candidate
                {
                    Entry = entry,
                    Event = ev,
                    Weight = EventPoolWeighting.Weight(entry, ev, tuning)
                };

                switch (tier)
                {
                    case StoryTier.Mandatory: mandatory.Add(candidate); break;
                    case StoryTier.Major: majors.Add(candidate); break;
                    default: minors.Add(candidate); break;
                }
            }

            var drawn = new HashSet<string>(StringComparer.Ordinal);
            var drafted = new List<Story>();

            // --- 2. Mandatory events are delivered, not drawn: no weight, no seed, and each is its
            // own bare single-slot story over and above storiesPerCycle. The only thing that holds
            // one back is the per-cycle cap, and a held one waits rather than disappearing.
            int maxMandatory = stories.MaxMandatoryPerCycle < 0 ? 0 : stories.MaxMandatoryPerCycle;

            mandatory.Sort(CompareCandidates);
            if (mandatory.Count > maxMandatory)
            {
                result.Degradations.Add("mandatory-deferred: delivered " + Index(maxMandatory)
                                        + " of " + Index(mandatory.Count)
                                        + " mandatory events, the rest wait a cycle");
                mandatory.RemoveRange(maxMandatory, mandatory.Count - maxMandatory);
            }

            for (int i = 0; i < mandatory.Count; i++)
            {
                Candidate m = mandatory[i];
                Story story = NewStory(MandatoryStoryId(today, i), today, stories, true);
                story.Slots.Add(NewSlot(m.Event, SlotRole.Major, context));
                Finish(story, m.Event);

                drafted.Add(story);
                drawn.Add(m.Entry.EventId);
            }

            // --- 3. The drawn stories. Majors first, in story order; the minors are then filled in
            // a StoryDraft-seeded order, because which story gets the heaviest remaining minor is a
            // real choice and leaving it to loop order would decide it by accident.
            // Per-save settings win over the tuning key of the same name when set — story shape is a
            // per-save setting, not global config (non-negotiable #10). Same shape and same reason as
            // TickPlanner.SnapshotsToPrune. The clamps come after the resolution, not before it.
            int storiesPerCycle = settings.StoriesPerCycle > 0
                ? settings.StoriesPerCycle
                : stories.StoriesPerCycle;
            if (storiesPerCycle < 0) storiesPerCycle = 0;

            int eventsPerStory = settings.EventsPerStory > 0
                ? settings.EventsPerStory
                : stories.EventsPerStory;
            if (eventsPerStory < 1) eventsPerStory = 1;

            var open = new List<Story>();
            for (int i = 0; i < storiesPerCycle; i++)
            {
                Candidate? lead = Draw(majors, saveGuid, today, "major:" + Index(i));
                bool promoted = false;

                if (lead == null)
                {
                    if (!stories.MinorPromotionEnabled)
                    {
                        // Fewer stories, deliberately: promotion is switched off, so a story with no
                        // major is not drafted at all rather than quietly becoming three minors.
                        result.Degradations.Add("minor-promotion-disabled: no major left, drafted "
                                                + Index(i) + " of " + Index(storiesPerCycle) + " stories");
                        break;
                    }

                    // Promotion is a selection, so it goes through the declared total order rather
                    // than through the seeded draw — the highest-ordered minor, not a lucky one.
                    lead = TakeTop(minors);
                    if (lead == null)
                    {
                        result.Degradations.Add("empty-pool: no major and no minor left, drafted "
                                                + Index(i) + " of " + Index(storiesPerCycle) + " stories");
                        break;
                    }

                    promoted = true;
                }

                Story story = NewStory(DrawnStoryId(today, i), today, stories, false);
                story.Slots.Add(NewSlot(lead.Event, SlotRole.Major, context));
                drawn.Add(lead.Entry.EventId);

                if (promoted)
                {
                    result.Degradations.Add("minor-promoted: '" + lead.Entry.EventId
                                            + "' leads story " + story.Id);
                }

                open.Add(story);
            }

            var fills = new List<SlotFill>();
            for (int slot = 1; slot < eventsPerStory; slot++)
            {
                for (int i = 0; i < open.Count; i++) fills.Add(new SlotFill { Story = i, Slot = slot });
            }
            SeedStreams.Rng(saveGuid, today, StreamNames.StoryDraft).Shuffle(fills);

            for (int i = 0; i < fills.Count; i++)
            {
                SlotFill fill = fills[i];
                Candidate? minor = Draw(minors, saveGuid, today,
                                        "minor:" + Index(fill.Story) + ":" + Index(fill.Slot));
                if (minor == null) continue;

                open[fill.Story].Slots.Add(NewSlot(minor.Event, SlotRole.Minor, context));
                drawn.Add(minor.Entry.EventId);
            }

            for (int i = 0; i < open.Count; i++)
            {
                Story story = open[i];
                if (story.Slots.Count < eventsPerStory)
                {
                    // A shorter story is a story. The pool simply had nothing else to say.
                    result.Degradations.Add("short-story: " + story.Id + " filled "
                                            + Index(story.Slots.Count) + " of "
                                            + Index(eventsPerStory) + " slots");
                }

                Finish(story, LeadEvent(story, eventsById));
                drafted.Add(story);
            }

            if (drafted.Count == 0) result.Degradations.Add("empty-pool: no stories drafted");

            // --- 4. Age what was passed over. Every entry that was drawable and not taken has its
            // MissStreak go up by one — that aging is the whole of the pity weighting, read on the
            // next cycle. A drawn entry is stamped and reset instead; an entry sitting out its
            // cooldown is neither, because it was never offered and so was never passed over.
            int cap = stories.MaxMissStreak < 0 ? 0 : stories.MaxMissStreak;

            var survivors = new List<EventPoolEntry>(pool);
            for (int i = 0; i < survivors.Count; i++)
            {
                EventPoolEntry entry = survivors[i];

                if (drawn.Contains(entry.EventId))
                {
                    entry.LastDraftedMonth = today.TotalMonths;
                    entry.MissStreak = 0;
                    continue;
                }

                if (cooling.Contains(entry.EventId)) continue;

                int streak = entry.MissStreak < 0 ? 0 : entry.MissStreak;
                entry.MissStreak = streak >= cap ? cap : streak + 1;
            }

            // Over capacity, the lowest-weighted go — decided by the same total order as every other
            // choice here, so the entry that gets dropped is never the one that happened to be last.
            // Note that a poolMaxSize set below the live catalog size would start evicting cooldown
            // records, which shortens a re-use gap; it ships at 60 against a live catalog of roughly
            // 40 precisely so that it never binds.
            if (stories.PoolMaxSize > 0 && survivors.Count > stories.PoolMaxSize)
            {
                EventPoolWeighting.SortByOrder(survivors, eventsById, tuning);
                survivors.RemoveRange(stories.PoolMaxSize, survivors.Count - stories.PoolMaxSize);
            }

            survivors.Sort(CompareEntriesById);
            drafted.Sort(CompareStoriesById);

            result.DraftedStories = drafted;
            result.UpdatedPool = survivors;
            return result;
        }

        // --- the draw ----------------------------------------------------------------------------

        /// <summary>
        /// Takes one candidate, weighted, from an ordered pool. Removes it, so a later draw in the
        /// same cycle cannot land on it twice.
        /// </summary>
        /// <param name="key">
        /// The sub-stream key. Per-slot rather than one generator walked in a loop, so inserting a
        /// story does not silently change every later slot's draw.
        /// </param>
        private static Candidate? Draw(List<Candidate> candidates, Guid saveGuid, SimDate today, string key)
        {
            if (candidates.Count == 0) return null;

            candidates.Sort(CompareCandidates);

            double total = 0.0;
            for (int i = 0; i < candidates.Count; i++) total += candidates[i].Weight;

            int index;
            if (double.IsNaN(total) || total <= 0.0)
            {
                // Degenerate weights make every candidate exactly as likely, which is an exact tie in
                // the pool draw rather than an ordering question — StoryTiebreak decides it, because
                // taking the first in order here would quietly bias the alphabet.
                index = SeedStreams.RngFor(saveGuid, today, StreamNames.StoryTiebreak, key)
                                   .NextInt(0, candidates.Count);
            }
            else
            {
                double roll = SeedStreams.RngFor(saveGuid, today, StreamNames.StoryPool, key)
                                         .NextDouble() * total;

                index = candidates.Count - 1;
                double cumulative = 0.0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    cumulative += candidates[i].Weight;
                    if (roll < cumulative) { index = i; break; }

                    // The roll landed exactly on the boundary between two candidates. Rare, but a
                    // real tie, so it is broken through the stream rather than by always rounding
                    // one way.
                    if (roll == cumulative && i + 1 < candidates.Count)
                    {
                        index = SeedStreams.RngFor(saveGuid, today, StreamNames.StoryTiebreak, key)
                                           .NextInt(0, 2) == 0 ? i : i + 1;
                        break;
                    }
                }
            }

            Candidate picked = candidates[index];
            candidates.RemoveAt(index);
            return picked;
        }

        /// <summary>
        /// Takes the highest-ordered candidate — the promotion path. Deterministic by construction:
        /// no seed is drawn, because the declared total order already answers this.
        /// </summary>
        private static Candidate? TakeTop(List<Candidate> candidates)
        {
            if (candidates.Count == 0) return null;

            candidates.Sort(CompareCandidates);
            Candidate top = candidates[0];
            candidates.RemoveAt(0);
            return top;
        }

        // --- building stories --------------------------------------------------------------------

        private static Story NewStory(string id, SimDate today, StoriesTuning stories, bool isMandatory)
        {
            // cycleMonths 2 means "draft on M, resolve on M+1, next batch at M+2", so resolution is
            // one month short of the cadence — and never the drafting month itself, which would make
            // every check a re-read of the snapshot the story opened on.
            int months = stories.CycleMonths - 1;
            if (months < 1) months = 1;

            return new Story
            {
                Id = id,
                OpenedDate = today,
                ResolvesDate = today.AddMonths(months),
                IsMandatory = isMandatory,
                FlavorKey = "story:" + id,
                ResolutionFlavorKey = "story.resolution:" + id
            };
        }

        private static StorySlot NewSlot(CivicEvent ev, SlotRole role, StoryReadContext context)
        {
            return new StorySlot
            {
                EventId = ev.Id,
                Role = role,
                BaselineMetric = Baseline(ev, context)
            };
        }

        /// <summary>
        /// The check's metric as read at the story's open, so a relative check is measured against
        /// the month it started.
        /// </summary>
        /// <remarks>
        /// City scope only. A district-scoped check has no single baseline to capture — nothing on
        /// <c>StorySlot</c> records which district the story landed on — so the baseline stays null
        /// and the check resolves <see cref="SlotOutcome.Unmeasurable"/>, which costs the player
        /// nothing. Inventing a city reading to stand in for a district one would be the opposite:
        /// a number the player could be scored against that was never a measurement of the thing
        /// asked about.
        /// </remarks>
        private static double? Baseline(CivicEvent ev, StoryReadContext context)
        {
            CheckSpec check = ev.Check;
            if (check == null || check.Spec == null) return null;

            TriggerSpec spec = check.Spec;
            if (spec.Scope != TriggerScope.City) return null;
            if (string.IsNullOrEmpty(spec.MetricId)) return null;

            return MetricRegistry.ReadCity(context.Today, spec.MetricId);
        }

        /// <summary>
        /// Sorts the slots into their declared order and fills in the headline fallback.
        /// </summary>
        private static void Finish(Story story, CivicEvent? lead)
        {
            // Major first, then id ordinal — Story.Slots documents that order, and "which minor is
            // first" left to insertion order is the determinism bug this file keeps guarding against.
            story.Slots.Sort(CompareSlots);

            // The fallback headline is the major event's name, per the design document.
            story.HeadlineFallback = lead == null ? "" : lead.Name;
        }

        private static CivicEvent? LeadEvent(Story story, Dictionary<string, CivicEvent> eventsById)
        {
            for (int i = 0; i < story.Slots.Count; i++)
            {
                if (story.Slots[i].Role != SlotRole.Major) continue;

                CivicEvent lead;
                if (eventsById.TryGetValue(story.Slots[i].EventId, out lead)) return lead;
            }
            return null;
        }

        // --- ids ---------------------------------------------------------------------------------

        /// <summary>
        /// Story ids are derived from the drafting month and the story's position, never generated:
        /// <c>Guid.NewGuid()</c> would make the same seed produce a different sidecar every run.
        /// </summary>
        private static string DrawnStoryId(SimDate today, int index) =>
            "story-" + Stamp(today) + "-" + index.ToString("D2", CultureInfo.InvariantCulture);

        private static string MandatoryStoryId(SimDate today, int index) =>
            "story-" + Stamp(today) + "-m" + index.ToString("D2", CultureInfo.InvariantCulture);

        private static string Stamp(SimDate today) =>
            today.Year.ToString("D4", CultureInfo.InvariantCulture) + "-"
            + today.Month.ToString("D2", CultureInfo.InvariantCulture);

        private static string Index(int value) => value.ToString(CultureInfo.InvariantCulture);

        // --- ordering ----------------------------------------------------------------------------

        private static int CompareCandidates(Candidate a, Candidate b) =>
            EventPoolWeighting.Compare(a.Entry, a.Weight, b.Entry, b.Weight);

        private static int CompareEntriesById(EventPoolEntry a, EventPoolEntry b) =>
            string.CompareOrdinal(a.EventId, b.EventId);

        private static int CompareStoriesById(Story a, Story b) => string.CompareOrdinal(a.Id, b.Id);

        private static int CompareSlots(StorySlot a, StorySlot b)
        {
            int byRole = b.Role.CompareTo(a.Role);
            return byRole != 0 ? byRole : string.CompareOrdinal(a.EventId, b.EventId);
        }

        // --- inputs ------------------------------------------------------------------------------

        /// <summary>
        /// The catalog sorted by id and filtered to this save's region, with the id lookup built as
        /// a side effect. The lookup is read by key only — never enumerated.
        /// </summary>
        private static List<CivicEvent> SortedCatalog(IReadOnlyList<CivicEvent>? catalog, RegionTheme theme,
                                                      EngineTuning tuning,
                                                      Dictionary<string, CivicEvent> eventsById)
        {
            var sorted = new List<CivicEvent>();
            if (catalog == null) return sorted;

            for (int i = 0; i < catalog.Count; i++)
            {
                CivicEvent ev = catalog[i];
                if (ev == null || string.IsNullOrEmpty(ev.Id)) continue;
                if (!ProceduralEventGenerator.RegionMatches(ev.Region, theme, tuning.Catalog)) continue;

                // A duplicate id is a catalog error the loader owns; here the first in id order wins,
                // so the draft is at least reproducible while the data is being fixed.
                if (eventsById.ContainsKey(ev.Id)) continue;

                eventsById.Add(ev.Id, ev);
                sorted.Add(ev);
            }

            sorted.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return sorted;
        }

        private static Dictionary<string, EventPoolEntry> PoolByEventId(IReadOnlyList<EventPoolEntry>? pool)
        {
            var byId = new Dictionary<string, EventPoolEntry>(StringComparer.Ordinal);
            if (pool == null) return byId;

            for (int i = 0; i < pool.Count; i++)
            {
                EventPoolEntry entry = pool[i];
                if (entry == null || string.IsNullOrEmpty(entry.EventId)) continue;
                if (!byId.ContainsKey(entry.EventId)) byId.Add(entry.EventId, entry);
            }
            return byId;
        }

        /// <summary>
        /// Whether an entry is still inside its re-use cooldown. Never true for a mandatory event —
        /// the caller checks the tier first, because a mandatory trigger describes the city now.
        /// </summary>
        private static bool OnCooldown(EventPoolEntry entry, SimDate today, StoriesTuning stories)
        {
            if (stories.ReuseCooldownMonths <= 0) return false;
            if (entry.LastDraftedMonth < 0) return false;

            // A save loaded backwards — an entry stamped in the future — reads as still cooling
            // rather than as a negative gap that silently satisfies the comparison.
            int elapsed = today.TotalMonths - entry.LastDraftedMonth;
            return elapsed < stories.ReuseCooldownMonths;
        }

        /// <summary>
        /// Event ids currently being told by an open story. Membership only — never iterated.
        /// </summary>
        /// <remarks>
        /// <c>StoryArchive</c> is deliberately not consulted: re-use is gated on a duration, for the
        /// reasons set out on <see cref="Draft"/>.
        /// </remarks>
        private static HashSet<string> LiveEventIds(PoliticalState prior)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            AddSlotIds(used, prior.LiveStories);
            return used;
        }

        private static void AddSlotIds(HashSet<string> used, List<Story>? stories)
        {
            if (stories == null) return;

            for (int i = 0; i < stories.Count; i++)
            {
                Story story = stories[i];
                if (story == null || story.Slots == null) continue;

                for (int j = 0; j < story.Slots.Count; j++)
                {
                    StorySlot slot = story.Slots[j];
                    if (slot != null && !string.IsNullOrEmpty(slot.EventId)) used.Add(slot.EventId);
                }
            }
        }

        /// <summary>
        /// The prior pool, cloned and sorted — the inert result. Cloned because the caller still holds
        /// the entries and a shared instance would let a later cycle age their copy.
        /// </summary>
        private static List<EventPoolEntry> ClonedPool(IReadOnlyList<EventPoolEntry>? pool)
        {
            var copy = new List<EventPoolEntry>();
            if (pool == null) return copy;

            for (int i = 0; i < pool.Count; i++)
            {
                EventPoolEntry entry = pool[i];
                if (entry != null && !string.IsNullOrEmpty(entry.EventId)) copy.Add(entry.Clone());
            }

            copy.Sort(CompareEntriesById);
            return copy;
        }

        /// <summary>One pooled event with the catalog entry and weight it was ordered by.</summary>
        private sealed class Candidate
        {
            public EventPoolEntry Entry = new EventPoolEntry();
            public CivicEvent Event = new CivicEvent();
            public double Weight;
        }

        /// <summary>One slot waiting to be filled, as a (story, slot) pair the fill order shuffles.</summary>
        private sealed class SlotFill
        {
            public int Story;
            public int Slot;
        }
    }
}
