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
        public void Draft_OpensOnTodayAndResolvesOneCycleLater()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            foreach (Story story in Draft(catalog, pool).DraftedStories)
            {
                Assert.Equal(March1994.TotalMonths, story.OpenedDate.TotalMonths);
                Assert.Equal(March1994.AddMonths(Tuning.Stories.CycleMonths).TotalMonths,
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

        /// <summary>
        /// Drawn entries leave the pool and everything left behind ages by one. That aging is what the
        /// pity weighting reads, so a cycle that forgot it would leave the pool permanently flat.
        /// </summary>
        [Fact]
        public void Draft_RemovesTheDrawnEntriesAndAgesEveryOneLeftBehind()
        {
            List<CivicEvent> catalog;
            List<EventPoolEntry> pool;
            FullPool(out catalog, out pool);

            StoryDraftResult result = Draft(catalog, pool);

            var drawn = new HashSet<string>(StringComparer.Ordinal);
            foreach (Story story in result.DraftedStories)
            {
                foreach (StorySlot slot in story.Slots) drawn.Add(slot.EventId);
            }

            foreach (EventPoolEntry entry in result.UpdatedPool)
            {
                Assert.False(drawn.Contains(entry.EventId),
                             "Drawn event '" + entry.EventId + "' is still in the pool.");
                Assert.Equal(1, entry.MissStreak);
            }

            Assert.Equal(pool.Count - drawn.Count, result.UpdatedPool.Count);
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

            Assert.Equal(1, Count(Ordinary(result)));
            Assert.NotEmpty(result.Degradations);

            foreach (Story story in Ordinary(result))
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

            Assert.Equal(1, Count(Ordinary(result)));
            Assert.NotEmpty(result.Degradations);

            foreach (Story story in Ordinary(result))
            {
                Assert.Equal(2, story.Slots.Count);
                Assert.True(story.Slots.Count < Tuning.Stories.EventsPerStory);
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
