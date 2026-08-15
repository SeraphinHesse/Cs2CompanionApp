using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Stories;
using Agora.Core.Tuning;
using Agora.Mod.Persistence;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Drafting: pool in, weighted seeded draw, stories out — and every degradation branch.
    ///
    /// <para>
    /// <b>Degradations are valid outcomes, never errors.</b> No major left in the pool promotes a
    /// minor; too few events left drafts a shorter story; an empty pool drafts nothing. All three are
    /// asserted below as outcomes with a shape, because the alternative — an exception, or a silently
    /// skipped cycle — is what a player experiences as the story layer switching itself off.
    /// </para>
    ///
    /// <para>
    /// Every catalog entry here carries a <c>Manual</c> trigger, which never fires from the city, so
    /// the pool refresh adds nothing and the pool each test states is the pool the draw sees. That is
    /// deliberate: these are tests of the <i>draw</i>, and routing them through the evaluator would
    /// make them fail for lane 2a's reasons rather than lane 2b's.
    /// </para>
    /// </summary>
    public class StoryAssemblerTests
    {
        private static readonly SimDate March1994 = StoryTestFixtures.March1994;
        private static readonly EngineTuning Tuning = EngineTuning.Default;

        private static StoryReadContext Context() =>
            StoryTestFixtures.Context(StoryTestFixtures.City(March1994));

        /// <summary>
        /// A catalog and a pool built from the same ids, so a test states its pool once. The severity
        /// of each id decides its tier through <c>StoryTiers</c>; nothing here stores one.
        /// </summary>
        private static void Build(IEnumerable<CivicEvent> events,
                                  out List<CivicEvent> catalog, out List<EventPoolEntry> pool)
        {
            catalog = new List<CivicEvent>(events);
            catalog.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            pool = new List<EventPoolEntry>();
            foreach (CivicEvent civicEvent in catalog) pool.Add(StoryTestFixtures.Pooled(civicEvent.Id));
        }

        private static StoryDraftResult Draft(List<CivicEvent> catalog, List<EventPoolEntry> pool,
                                              EngineTuning? tuning = null, Guid? save = null)
        {
            PoliticalState prior = StoryTestFixtures.State(March1994, pool.ToArray());
            return StoryAssembler.Draft(prior, catalog, Context(), save ?? StoryTestFixtures.Save,
                                        March1994, tuning ?? Tuning);
        }

        /// <summary>A pool with enough majors and minors for a full cycle, and then some.</summary>
        private static void FullPool(out List<CivicEvent> catalog, out List<EventPoolEntry> pool)
        {
            var events = new List<CivicEvent>();
            for (int i = 0; i < Tuning.Stories.StoriesPerCycle + 2; i++)
            {
                events.Add(StoryTestFixtures.Major("evt-major-" + i.ToString("00")));
            }

            int minors = (Tuning.Stories.StoriesPerCycle * Tuning.Stories.EventsPerStory) + 4;
            for (int i = 0; i < minors; i++)
            {
                events.Add(StoryTestFixtures.Minor("evt-minor-" + i.ToString("00")));
            }

            Build(events, out catalog, out pool);
        }

        private static IEnumerable<Story> Ordinary(StoryDraftResult result)
        {
            foreach (Story story in result.DraftedStories)
            {
                if (!story.IsMandatory) yield return story;
            }
        }

        private static int Count(IEnumerable<Story> stories)
        {
            int count = 0;
            foreach (Story ignored in stories) count++;
            return count;
        }

        // --- the ordinary draw --------------------------------------------------------------------

        [Fact]
        public void Draft_ProducesTheTunedNumberOfStories()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            Assert.Equal(Tuning.Stories.StoriesPerCycle, Count(Ordinary(Draft(catalog, pool))));
        }

        /// <summary>One major and the rest minors — the shape the whole design is named after.</summary>
        [Fact]
        public void Draft_GivesEachStoryOneMajorAndTheRestMinors()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            foreach (Story story in Ordinary(Draft(catalog, pool)))
            {
                Assert.Equal(Tuning.Stories.EventsPerStory, story.Slots.Count);

                int majors = 0;
                foreach (StorySlot slot in story.Slots)
                {
                    if (slot.Role == SlotRole.Major) majors++;
                }

                Assert.Equal(1, majors);
            }
        }

        /// <summary>
        /// Slots are sorted major first, then by <c>EventId</c> ordinal. "Which minor is first" left to
        /// collection order is the determinism bug the contract calls the most common one.
        /// </summary>
        [Fact]
        public void Draft_SortsSlotsMajorFirstThenByEventIdOrdinal()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            foreach (Story story in Draft(catalog, pool).DraftedStories)
            {
                for (int i = 1; i < story.Slots.Count; i++)
                {
                    StorySlot previous = story.Slots[i - 1];
                    StorySlot current = story.Slots[i];

                    if (previous.Role != current.Role)
                    {
                        Assert.Equal(SlotRole.Major, previous.Role);
                        continue;
                    }

                    Assert.True(string.CompareOrdinal(previous.EventId, current.EventId) < 0,
                        "Slots '" + previous.EventId + "' and '" + current.EventId +
                        "' are out of ordinal order within one role.");
                }
            }
        }

        [Fact]
        public void Draft_SortsTheDraftedStoriesById()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            List<Story> stories = Draft(catalog, pool).DraftedStories;
            for (int i = 1; i < stories.Count; i++)
            {
                Assert.True(string.CompareOrdinal(stories[i - 1].Id, stories[i].Id) < 0,
                            "DraftedStories is not sorted by Id ordinal.");
            }
        }

        /// <summary>No event may appear in two slots, in one story or across the cycle.</summary>
        [Fact]
        public void Draft_NeverDrawsOneEventTwice()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Story story in Draft(catalog, pool).DraftedStories)
            {
                foreach (StorySlot slot in story.Slots)
                {
                    Assert.True(seen.Add(slot.EventId),
                                "Event '" + slot.EventId + "' was drawn into two slots.");
                }
            }
        }

        /// <summary>
        /// The story's dates come from the clock and the cadence, never from a day count: there is no
        /// day 15 because a sim "day" is a calendar month.
        /// </summary>
        [Fact]
        public void Draft_OpensOnTodayAndResolvesOneMonthBeforeTheCycleEnds()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            foreach (Story story in Draft(catalog, pool).DraftedStories)
            {
                Assert.Equal(March1994.TotalMonths, story.OpenedDate.TotalMonths);

                // CycleMonths is the PERIOD, not the draft-to-resolution gap: a cycle of 2 drafts on
                // M, resolves on M+1 and drafts again at M+2. The worked example on
                // StoriesTuning.CycleMonths is the authority, and the field's summary used to
                // contradict it by exactly this one month.
                Assert.Equal(March1994.AddMonths(Tuning.Stories.CycleMonths - 1).TotalMonths,
                             story.ResolvesDate.TotalMonths);
                Assert.Equal(StoryOutcome.Pending, story.Outcome);
                Assert.Equal(-1, story.ResolvedMonth);
            }
        }

        /// <summary>
        /// The headline fallback is the major event's name — what the UI shows before any LLM prose
        /// exists, which on a fail-closed LLM is forever.
        /// </summary>
        [Fact]
        public void Draft_FallsBackToTheMajorEventsNameForTheHeadline()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            foreach (Story story in Ordinary(Draft(catalog, pool)))
            {
                string majorId = "";
                foreach (StorySlot slot in story.Slots)
                {
                    if (slot.Role == SlotRole.Major) majorId = slot.EventId;
                }

                Assert.Equal("Event " + majorId, story.HeadlineFallback);
            }
        }

        // --- the pool afterwards ------------------------------------------------------------------

        /// <summary>The ids drawn into any story this cycle.</summary>
        private static HashSet<string> DrawnIds(StoryDraftResult result)
        {
            var drawn = new HashSet<string>(StringComparer.Ordinal);
            foreach (Story story in result.DraftedStories)
            {
                foreach (StorySlot slot in story.Slots) drawn.Add(slot.EventId);
            }

            return drawn;
        }

        private static EventPoolEntry? Find(List<EventPoolEntry> pool, string eventId)
        {
            foreach (EventPoolEntry entry in pool)
            {
                if (string.Equals(entry.EventId, eventId, StringComparison.Ordinal)) return entry;
            }

            return null;
        }

        /// <summary>
        /// <b>A drawn entry STAYS in the pool</b>, retained with its <c>MissStreak</c> reset and its
        /// <c>LastDraftedMonth</c> stamped. This reverses the original "clear the drawn entries"
        /// instruction, and the reversal is load-bearing.
        /// </summary>
        /// <remarks>
        /// The re-use cooldown lives on the entry, so the entry has to survive the months it is
        /// counting. Drop it and it is re-admitted from the catalog next cycle at
        /// <c>LastDraftedMonth = -1</c>, at which point the cooldown does nothing whatsoever — the
        /// same class of bug as the archive-based rule it replaced, reached in one cycle instead of
        /// fourteen.
        /// </remarks>
        [Fact]
        public void Draft_RetainsEveryDrawnEntryWithItsStreakResetAndItsDraftMonthStamped()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            StoryDraftResult result = Draft(catalog, pool);
            HashSet<string> drawn = DrawnIds(result);

            Assert.NotEmpty(drawn);

            foreach (string id in drawn)
            {
                EventPoolEntry? entry = Find(result.UpdatedPool, id);

                Assert.True(entry != null,
                    "Drawn event '" + id + "' left the pool. Its cooldown stamp goes with it, so it " +
                    "is re-admitted next cycle at LastDraftedMonth = -1 and the cooldown does nothing.");

                Assert.Equal(0, entry!.MissStreak);
                Assert.Equal(March1994.TotalMonths, entry.LastDraftedMonth);
            }

            // Nothing left the pool at all: the whole set is still there.
            Assert.Equal(pool.Count, result.UpdatedPool.Count);
        }

        /// <summary>
        /// Everything not drawn ages by one. That ageing is what the pity weighting reads, so a cycle
        /// that forgot it would leave the pool permanently flat.
        /// </summary>
        [Fact]
        public void Draft_AgesEveryEntryItPassedOver()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            StoryDraftResult result = Draft(catalog, pool);
            HashSet<string> drawn = DrawnIds(result);

            int aged = 0;
            foreach (EventPoolEntry entry in result.UpdatedPool)
            {
                if (drawn.Contains(entry.EventId)) continue;

                Assert.Equal(1, entry.MissStreak);
                aged++;
            }

            Assert.True(aged > 0, "Nothing was passed over, so this proves nothing about ageing.");
        }

        /// <summary>
        /// <b>An entry sitting out its cooldown is not aged.</b> It was never offered, so it was never
        /// passed over, and ageing it would hand it a pity bonus for time it did not spend waiting —
        /// which on release would put it straight to the front of the draw.
        /// </summary>
        [Fact]
        public void Draft_DoesNotAgeAnEntryServingItsCooldown()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            // Stamped this month, so it is mid-cooldown at every draft until the cooldown elapses.
            EventPoolEntry cooling = StoryTestFixtures.Pooled("evt-cooling", missStreak: 0);
            cooling.LastDraftedMonth = March1994.TotalMonths;

            catalog.Add(StoryTestFixtures.Minor("evt-cooling"));
            catalog.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            pool.Add(cooling);

            StoryDraftResult result = Draft(catalog, pool);

            EventPoolEntry? after = Find(result.UpdatedPool, "evt-cooling");
            Assert.True(after != null, "The cooling entry left the pool.");
            Assert.Equal(0, after!.MissStreak);
        }

        /// <summary>
        /// An entry inside its cooldown is not drawn at all.
        /// </summary>
        [Fact]
        public void Draft_DoesNotDrawAnEntryInsideItsCooldown()
        {
            var events = new List<CivicEvent> { StoryTestFixtures.Major("evt-major-00") };
            for (int i = 0; i < 6; i++) events.Add(StoryTestFixtures.Minor("evt-minor-" + i.ToString("00")));

            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            Build(events, out catalog, out pool);

            // Every minor was told last month, so only the major is drawable and the cycle degrades
            // rather than re-telling something the player just read.
            foreach (EventPoolEntry entry in pool)
            {
                if (entry.EventId.StartsWith("evt-minor", StringComparison.Ordinal))
                {
                    entry.LastDraftedMonth = March1994.TotalMonths - 1;
                }
            }

            StoryDraftResult result = Draft(catalog, pool);

            foreach (Story story in result.DraftedStories)
            {
                foreach (StorySlot slot in story.Slots)
                {
                    Assert.False(slot.EventId.StartsWith("evt-minor", StringComparison.Ordinal),
                        "'" + slot.EventId + "' was re-told one month after its last outing, well " +
                        "inside stories.reuseCooldownMonths of " + Tuning.Stories.ReuseCooldownMonths + ".");
                }
            }
        }

        /// <summary>
        /// A mandatory event ignores the cooldown entirely. A mandatory trigger is a statement about
        /// the city right now; suppressing it because the same event was told two years ago would drop
        /// a genuine crisis silently — no story, no power movement, no prose.
        /// </summary>
        [Fact]
        public void Draft_DeliversAMandatoryEventEvenInsideItsCooldown()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            EventPoolEntry mandatory = StoryTestFixtures.Pooled("evt-mandatory-00");
            mandatory.LastDraftedMonth = March1994.TotalMonths - 1;

            catalog.Add(StoryTestFixtures.Mandatory("evt-mandatory-00"));
            catalog.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            pool.Add(mandatory);

            StoryDraftResult result = Draft(catalog, pool);

            bool delivered = false;
            foreach (Story story in result.DraftedStories)
            {
                foreach (StorySlot slot in story.Slots)
                {
                    if (slot.EventId == "evt-mandatory-00") delivered = true;
                }
            }

            Assert.True(delivered,
                "A mandatory event was suppressed by the re-use cooldown. A mandatory trigger is a " +
                "statement about the city right now.");
        }

        /// <summary>
        /// <b>The cooling set survives the <c>poolMaxSize</c> trim, and this is the test that catches
        /// the trap.</b>
        /// </summary>
        /// <remarks>
        /// A cooling entry has <c>MissStreak == 0</c> by construction, so it sits in the
        /// minimum-weight class and is the <i>first</i> thing a weight-ordered trim discards — which
        /// destroys exactly the stamps that make the cooldown work, re-admits those events at -1, and
        /// silently reduces the cooldown to nothing. Relying on <c>poolMaxSize</c> being set above the
        /// catalog size delegates a correctness property to a dial and a data file, which is how the
        /// archive coupling this replaced went wrong one level up.
        /// </remarks>
        [Fact]
        public void Draft_KeepsACoolingEntrysStampWhenThePoolOverflows()
        {
            EngineTuning tiny = StoryTestFixtures.Tuned("{\"stories\":{\"poolMaxSize\":8}}");

            var events = new List<CivicEvent>();
            for (int i = 0; i < 3; i++) events.Add(StoryTestFixtures.Major("evt-major-" + i.ToString("00")));
            for (int i = 0; i < 20; i++) events.Add(StoryTestFixtures.Minor("evt-minor-" + i.ToString("00")));

            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            Build(events, out catalog, out pool);

            Assert.True(pool.Count > tiny.Stories.PoolMaxSize,
                        "The pool must overflow for this to test the trim at all.");

            // One cooling entry among many well-aged ones. Every other entry outweighs it, so a naive
            // weight-ordered trim drops this one first.
            EventPoolEntry cooling = Find(pool, "evt-minor-00")!;
            cooling.MissStreak = 0;
            cooling.LastDraftedMonth = March1994.TotalMonths - 1;

            foreach (EventPoolEntry entry in pool)
            {
                if (entry.EventId != "evt-minor-00") entry.MissStreak = Tuning.Stories.MaxMissStreak;
            }

            StoryDraftResult result = Draft(catalog, pool, tiny);

            EventPoolEntry? after = Find(result.UpdatedPool, "evt-minor-00");

            Assert.True(after != null,
                "The poolMaxSize trim discarded a cooling entry. It has MissStreak 0 by construction, " +
                "so it is the lowest-weighted thing in the pool and a weight-ordered trim takes it " +
                "first — which loses the stamp, re-admits the event at LastDraftedMonth = -1, and " +
                "reduces the re-use cooldown to nothing.");

            Assert.Equal(March1994.TotalMonths - 1, after!.LastDraftedMonth);
        }

        [Fact]
        public void Draft_SortsTheUpdatedPoolByEventIdOrdinal()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            List<EventPoolEntry> updated = Draft(catalog, pool).UpdatedPool;
            for (int i = 1; i < updated.Count; i++)
            {
                Assert.True(string.CompareOrdinal(updated[i - 1].EventId, updated[i].EventId) < 0,
                            "UpdatedPool is not sorted by EventId ordinal.");
            }
        }

        /// <summary>
        /// The prior state is an input, not a scratch pad. A speculative advance that aged the
        /// caller's own pool would leave the runtime holding a state it never asked for — the same
        /// hazard <c>Story.Clone</c> deep-copies its slots for.
        /// </summary>
        [Fact]
        public void Draft_DoesNotMutateThePriorState()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            PoliticalState prior = StoryTestFixtures.State(March1994, pool.ToArray());
            string before = AgoraJson.Fingerprint(prior);

            StoryAssembler.Draft(prior, catalog, Context(), StoryTestFixtures.Save, March1994, Tuning);

            Assert.Equal(before, AgoraJson.Fingerprint(prior));
        }

        // --- degradations -------------------------------------------------------------------------

        /// <summary>
        /// <b>No major left, promotion on.</b> The highest-ordered minor is promoted, so the story is
        /// still a full one — a degradation is a shape, not a shortfall — and the fact is recorded for
        /// the log.
        /// </summary>
        [Fact]
        public void Draft_PromotesAMinorWhenNoMajorIsLeftInThePool()
        {
            var events = new List<CivicEvent>();
            for (int i = 0; i < Tuning.Stories.EventsPerStory; i++)
            {
                events.Add(StoryTestFixtures.Minor("evt-minor-" + i.ToString("00")));
            }

            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            Build(events, out catalog, out pool);

            StoryDraftResult result = Draft(catalog, pool);

            // A story was drafted at all, it is led by a minor wearing the Major role, and the fact
            // was recorded for the log. Promotion is a shape, not a shortfall.
            Assert.NotEmpty(Ordinary(result));
            Assert.NotEmpty(result.Degradations);

            foreach (Story story in Ordinary(result))
            {
                int majors = 0;
                foreach (StorySlot slot in story.Slots)
                {
                    if (slot.Role == SlotRole.Major) majors++;
                }

                Assert.Equal(1, majors);
                Assert.True(story.Slots.Count > 0);
            }

            // How the scarce events are spread ACROSS the cycle's stories is asserted separately, in
            // Draft_FillsOneStoryBeforeOpeningAnother — it is a distinct claim and it is currently in
            // dispute, so folding it in here would make this test fail for a reason it is not about.
        }

        /// <summary>
        /// The promotion goes through the declared total order like every other selection. With every
        /// candidate tied on weight and on <c>MissStreak</c>, the id-ordinal key decides — leaving it
        /// to collection order is the bug the order exists to prevent.
        /// </summary>
        [Fact]
        public void Draft_PromotesTheHighestOrderedMinorRatherThanTheFirstInTheCollection()
        {
            var events = new List<CivicEvent>();
            for (int i = 0; i < Tuning.Stories.EventsPerStory; i++)
            {
                events.Add(StoryTestFixtures.Minor("evt-minor-" + i.ToString("00")));
            }

            List<CivicEvent> forward;
            List<EventPoolEntry> pool;
            Build(events, out forward, out pool);

            var backward = new List<CivicEvent>(forward);
            backward.Reverse();

            var reversedPool = new List<EventPoolEntry>(pool);
            reversedPool.Reverse();

            Assert.Equal(AgoraJson.Fingerprint(Draft(forward, pool)),
                         AgoraJson.Fingerprint(Draft(backward, reversedPool)));
        }

        /// <summary>
        /// <b>No major left, promotion off.</b> The cycle drafts fewer stories instead — the switch is
        /// what it says it is, and turning it off must not produce a story with no major in it.
        /// </summary>
        [Fact]
        public void Draft_DraftsNoStoryWhenNoMajorIsLeftAndPromotionIsOff()
        {
            var events = new List<CivicEvent>();
            for (int i = 0; i < Tuning.Stories.EventsPerStory; i++)
            {
                events.Add(StoryTestFixtures.Minor("evt-minor-" + i.ToString("00")));
            }

            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            Build(events, out catalog, out pool);

            EngineTuning noPromotion =
                StoryTestFixtures.Tuned("{\"stories\":{\"minorPromotionEnabled\":false}}");

            StoryDraftResult result = Draft(catalog, pool, noPromotion);

            Assert.Equal(0, Count(Ordinary(result)));

            // And the pool is left intact for the next cycle rather than consumed by a draw that
            // produced nothing.
            Assert.Equal(pool.Count, result.UpdatedPool.Count);
        }

        /// <summary>
        /// <b>Too few events left for a full story.</b> A shorter story is a story — the cycle must not
        /// throw the major away because it could not find two minors to go with it.
        /// </summary>
        [Fact]
        public void Draft_DraftsAShorterStoryWhenThePoolCannotFillOne()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            Build(new[] { StoryTestFixtures.Major("evt-major-00"), StoryTestFixtures.Minor("evt-minor-00") },
                  out catalog, out pool);

            StoryDraftResult result = Draft(catalog, pool);

            // The cycle did not throw the major away for want of two minors to go with it: something
            // was drafted, every event available was used, and the shortfall was logged.
            Assert.NotEmpty(Ordinary(result));
            Assert.NotEmpty(result.Degradations);

            HashSet<string> drawn = DrawnIds(result);
            Assert.Contains("evt-major-00", drawn);
            Assert.Contains("evt-minor-00", drawn);

            foreach (Story story in Ordinary(result))
            {
                Assert.True(story.Slots.Count > 0, "A story was opened with no slots in it at all.");
                Assert.True(story.Slots.Count < Tuning.Stories.EventsPerStory);
            }
        }

        /// <summary>
        /// <b>A minor must not be promoted while a real major is still available.</b> Promotion exists
        /// for "no major left"; here one was left, and it was only unavailable because the draft had
        /// already given it to a different story.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>This currently fails, and I believe the implementation is wrong rather than the
        /// assertion.</b> With exactly one major and one minor in the pool, the draft opens two
        /// stories: the first led by the real major, the second led by the minor promoted into the
        /// Major role — and then neither has anything left to fill it, so both come out one slot long.
        /// The alternative was available and strictly better: one story of <c>[major, minor]</c>, no
        /// promotion, one degradation instead of three.
        /// </para>
        /// <para>
        /// The cause is that leads are allocated greedily — one per story for the whole cycle, before
        /// any story is filled — so story 1 finds the majors list already empty and takes the
        /// promotion branch. The promotion is manufactured by the allocation order rather than by the
        /// pool's actual contents, which is what makes this a defect rather than a policy choice: the
        /// degradation the log reports did not happen.
        /// </para>
        /// <para>
        /// It is not merely cosmetic. A one-slot story is decided by a single slot, and a story of
        /// fewer than three needs <i>all</i> its scored slots — so splitting one two-slot story into
        /// two one-slot stories doubles the number of all-or-nothing verdicts the player faces and
        /// changes what the cycle pays out.
        /// </para>
        /// </remarks>
        [Fact]
        public void Draft_DoesNotPromoteAMinorWhileARealMajorIsAvailable()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            Build(new[] { StoryTestFixtures.Major("evt-major-00"), StoryTestFixtures.Minor("evt-minor-00") },
                  out catalog, out pool);

            StoryDraftResult result = Draft(catalog, pool);

            foreach (string degradation in result.Degradations)
            {
                Assert.False(degradation.StartsWith("minor-promoted", StringComparison.Ordinal),
                    "A minor was promoted although the pool held a real major: " + degradation +
                    ". Promotion is for 'no major left', and one was left — it had already been " +
                    "given to another story by the greedy lead allocation.");
            }
        }

        /// <summary>
        /// <b>Scarce events fill one story before another is opened.</b> Spreading them thin maximises
        /// exactly the degradations the draft is supposed to minimise.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>This currently fails, and is the general form of the defect above.</b> Three minors and
        /// no major produce two stories — <c>[promoted, minor]</c> and <c>[promoted]</c> — and so two
        /// promotions and two short-story degradations. Concentrating them gives one full three-slot
        /// story with a single promotion: the same events, a better story, and a quarter of the
        /// degradations.
        /// </para>
        /// <para>
        /// Asserted as "no story is opened while an earlier one is still short" rather than as a story
        /// count, because the count depends on <c>storiesPerCycle</c> and on how many events happen to
        /// be available. The invariant is what matters and it holds at every pool size: an unfilled
        /// story means there was nothing left to fill it with, not that the leftovers went elsewhere.
        /// </para>
        /// <para>
        /// A one-slot ordinary story is also the shape the design reserves for a <i>mandatory</i>
        /// event — the bare single-slot story — so producing them by accident blurs a distinction the
        /// UI and the power economy both read.
        /// </para>
        /// </remarks>
        [Fact]
        public void Draft_FillsOneStoryBeforeOpeningAnother()
        {
            var events = new List<CivicEvent>();
            for (int i = 0; i < Tuning.Stories.EventsPerStory; i++)
            {
                events.Add(StoryTestFixtures.Minor("evt-minor-" + i.ToString("00")));
            }

            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            Build(events, out catalog, out pool);

            var drafted = new List<Story>(Ordinary(Draft(catalog, pool)));

            for (int i = 0; i < drafted.Count - 1; i++)
            {
                Assert.True(drafted[i].Slots.Count >= Tuning.Stories.EventsPerStory,
                    "Story '" + drafted[i].Id + "' holds " + drafted[i].Slots.Count + " of " +
                    Tuning.Stories.EventsPerStory + " slots, yet '" + drafted[i + 1].Id +
                    "' was opened as well. The events that would have filled the first were spread " +
                    "into the second, which manufactures a promotion and two short stories where " +
                    "one full story was available from the same pool.");
            }
        }

        /// <summary>
        /// <b>An empty pool.</b> Nothing to draw is not an error state: the cycle produces no stories,
        /// an empty pool and no exception, and the next cycle tries again.
        /// </summary>
        [Fact]
        public void Draft_OnAnEmptyPoolProducesNothingAndDoesNotThrow()
        {
            StoryDraftResult result = Draft(new List<CivicEvent>(), new List<EventPoolEntry>());

            Assert.Empty(result.DraftedStories);
            Assert.Empty(result.UpdatedPool);
        }

        /// <summary>
        /// A pool entry naming an event the catalog no longer carries — a save loaded against a
        /// narrower content set — is dropped rather than drafted into a story with no event behind it.
        /// </summary>
        [Fact]
        public void Draft_IgnoresAPoolEntryWithNoCatalogEntry()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);
            pool.Add(StoryTestFixtures.Pooled("evt-retired-content"));

            StoryDraftResult result = Draft(catalog, pool);

            foreach (Story story in result.DraftedStories)
            {
                foreach (StorySlot slot in story.Slots)
                {
                    Assert.NotEqual("evt-retired-content", slot.EventId);
                }
            }
        }

        // --- Manual-triggered events are not pool members -----------------------------------------

        /// <summary>
        /// <b>The refresh does not adopt a <see cref="TriggerKind.Manual"/> event.</b> It never fires
        /// from the city, so nothing about the city can put it in the pool.
        /// </summary>
        /// <remarks>
        /// Ruling 11, and it is worth its own test rather than being left implied. This behaviour is
        /// invisible from the engine's side — the skip is one <c>continue</c> before eligibility is
        /// considered — and the whole wave-2 fixture set was built on the opposite reading of the same
        /// sentence, which is how it came to be ruled on at all.
        /// </remarks>
        [Fact]
        public void Draft_NeverAdoptsAManualTriggeredEvent()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            catalog.Add(StoryTestFixtures.ManualTriggered("evt-introduced-elsewhere",
                                                          Tuning.Stories.MajorSeverityThreshold));
            catalog.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            StoryDraftResult result = Draft(catalog, pool);

            Assert.Null(Find(result.UpdatedPool, "evt-introduced-elsewhere"));
            Assert.DoesNotContain("evt-introduced-elsewhere", DrawnIds(result));
        }

        /// <summary>
        /// <b>And it drops one already carried in the pool</b> — the skip is before eligibility, so a
        /// Manual event is not merely un-admitted but actively released.
        /// </summary>
        /// <remarks>
        /// The distinction matters on a save whose catalog changed: an event that was pooled under a
        /// metric trigger and is re-authored as Manual must leave rather than sit there forever,
        /// eligible for a draw the refresh will never re-confirm.
        /// </remarks>
        [Fact]
        public void Draft_ReleasesAManualTriggeredEventAlreadyInThePool()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            catalog.Add(StoryTestFixtures.ManualTriggered("evt-rewritten-as-manual",
                                                          Tuning.Stories.MajorSeverityThreshold));
            catalog.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            pool.Add(StoryTestFixtures.Pooled("evt-rewritten-as-manual", missStreak: 3));

            StoryDraftResult result = Draft(catalog, pool);

            Assert.Null(Find(result.UpdatedPool, "evt-rewritten-as-manual"));
            Assert.DoesNotContain("evt-rewritten-as-manual", DrawnIds(result));
        }

        /// <summary>
        /// A mandatory-severity event with a Manual trigger is excluded too, and this is the pairing
        /// ruling 11 was written to prevent anyone assuming their way past. <b>Manual is a trigger
        /// kind; mandatory is a tier derived from severity.</b> They are orthogonal, so a Manual
        /// trigger removes even a severity-5 event from the pool entirely and it will never produce a
        /// story.
        /// </summary>
        [Fact]
        public void Draft_ExcludesAManualTriggeredEventEvenAtMandatorySeverity()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            catalog.Add(StoryTestFixtures.ManualTriggered("evt-manual-mandatory",
                                                          Tuning.Stories.MandatorySeverityThreshold));
            catalog.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            pool.Add(StoryTestFixtures.Pooled("evt-manual-mandatory"));

            StoryDraftResult result = Draft(catalog, pool);

            Assert.Null(Find(result.UpdatedPool, "evt-manual-mandatory"));

            foreach (Story story in result.DraftedStories)
            {
                Assert.False(story.IsMandatory && story.Slots.Count == 1 &&
                             story.Slots[0].EventId == "evt-manual-mandatory",
                    "A Manual-triggered event was delivered as a mandatory story. Manual is a " +
                    "trigger kind and mandatory is a tier; a Manual trigger takes the event out of " +
                    "the pool whatever its severity.");
            }
        }

        // --- mandatory events ---------------------------------------------------------------------

        /// <summary>
        /// A mandatory event gets its own bare single-slot story, <b>over and above</b>
        /// <c>stories.storiesPerCycle</c>. It is not drawn, it is delivered — so it is neither
        /// weighted nor degraded, and it does not consume one of the cycle's stories.
        /// </summary>
        [Fact]
        public void Draft_GivesAMandatoryEventItsOwnBareStoryOnTopOfTheCycle()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            catalog.Add(StoryTestFixtures.Mandatory("evt-mandatory-00"));
            catalog.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            pool.Add(StoryTestFixtures.Pooled("evt-mandatory-00"));

            StoryDraftResult result = Draft(catalog, pool);

            Assert.Equal(Tuning.Stories.StoriesPerCycle, Count(Ordinary(result)));

            var mandatory = new List<Story>();
            foreach (Story story in result.DraftedStories)
            {
                if (story.IsMandatory) mandatory.Add(story);
            }

            Story only = Assert.Single(mandatory);
            StorySlot slot = Assert.Single(only.Slots);
            Assert.Equal("evt-mandatory-00", slot.EventId);
        }

        // --- determinism --------------------------------------------------------------------------

        /// <summary>
        /// The canonical pattern from <c>tests/CLAUDE.md</c>: run twice from identical seeds and
        /// compare a serialized hash rather than field by field, because a hash catches the fields a
        /// hand-written assertion forgets.
        /// </summary>
        [Fact]
        public void Draft_IsByteIdenticalFromTheSameSeed()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            Assert.Equal(AgoraJson.Fingerprint(Draft(catalog, pool)),
                         AgoraJson.Fingerprint(Draft(catalog, pool)));
        }

        /// <summary>
        /// The draw is seeded from the save, so two saves at the same date with the same pool do not
        /// have to see the same stories. Asserted as "the seed reaches the result" rather than as
        /// inequality of the whole draft: with a small pool two saves may legitimately land on the
        /// same set, and a test that forbade that would be flaky by construction.
        /// </summary>
        [Fact]
        public void Draft_SeedsFromTheSaveGuid()
        {
            var events = new List<CivicEvent>();
            events.Add(StoryTestFixtures.Major("evt-major-00"));
            events.Add(StoryTestFixtures.Major("evt-major-01"));
            for (int i = 0; i < 12; i++) events.Add(StoryTestFixtures.Minor("evt-minor-" + i.ToString("00")));

            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            Build(events, out catalog, out pool);

            string a = AgoraJson.Fingerprint(Draft(catalog, pool, save: StoryTestFixtures.Save));
            string b = AgoraJson.Fingerprint(Draft(catalog, pool, save: StoryTestFixtures.Save));
            string other = AgoraJson.Fingerprint(Draft(catalog, pool, save: StoryTestFixtures.OtherSave));

            Assert.Equal(a, b);
            Assert.NotEqual(a, other);
        }

        /// <summary>
        /// Catalog order must not reach the result. The catalog is loaded from files whose enumeration
        /// order is the filesystem's, which is not engine state.
        /// </summary>
        [Fact]
        public void Draft_IsIndependentOfCatalogAndPoolCollectionOrder()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            var shuffledCatalog = new List<CivicEvent>(catalog);
            shuffledCatalog.Reverse();

            var shuffledPool = new List<EventPoolEntry>(pool);
            shuffledPool.Reverse();

            Assert.Equal(AgoraJson.Fingerprint(Draft(catalog, pool)),
                         AgoraJson.Fingerprint(Draft(shuffledCatalog, shuffledPool)));
        }
    }
}
