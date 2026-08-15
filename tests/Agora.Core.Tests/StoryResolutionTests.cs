using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Stories;
using Agora.Core.Tuning;
using Agora.Mod.Persistence;
using Agora.Mod.Sensors;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The 2-of-3 rule and its edges.
    ///
    /// <para>
    /// <b>The property under test throughout is that an unreadable slot is excluded from both halves
    /// of the ratio.</b> Counting one as a failure in the denominator would charge the player
    /// political power for a sensor gap, and the sharpest fixture for it is
    /// <see cref="Resolve_OneMetSlotAmongTwoUnmeasurableOnesSucceeds"/>: correct scoring reaches
    /// Success and the wrong scoring reaches Failure, so the outcome alone separates them.
    /// </para>
    ///
    /// <para>
    /// Most fixtures below pin their per-slot verdicts through the response modes rather than through
    /// the city, because <c>PowerOverride</c> is an automatic success, <c>Ignore</c> is an automatic
    /// failure and an undeclared <c>Manual</c> is neutral. That makes the arithmetic of the ratio
    /// testable without routing every case through lane 2a's evaluator — the tests that <i>do</i> mean
    /// to exercise the evaluator say so.
    /// </para>
    /// </summary>
    public class StoryResolutionTests
    {
        private static readonly SimDate March1994 = StoryTestFixtures.March1994;
        private static readonly EngineTuning Tuning = EngineTuning.Default;

        /// <summary>
        /// A catalog covering every slot in <paramref name="story"/>. Checks are absolute metric reads
        /// on happiness, which the fixtures below drive from the snapshot when they mean to.
        /// </summary>
        private static List<CivicEvent> CatalogFor(Story story, double goalThreshold = 50.0)
        {
            var catalog = new List<CivicEvent>();
            foreach (StorySlot slot in story.Slots)
            {
                catalog.Add(StoryTestFixtures.Event(slot.EventId, 2, check: StoryTestFixtures.Check(
                    StoryTestFixtures.Metric(MetricHistory.Happiness,
                                             Comparison.GreaterThanOrEqual, goalThreshold))));
            }

            catalog.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return catalog;
        }

        private static StoryResolutionResult Resolve(Story story, double happiness = 50.0,
                                                     double goalThreshold = 50.0,
                                                     StoryReadContext? context = null)
        {
            return StoryResolution.Resolve(story, CatalogFor(story, goalThreshold),
                context ?? StoryTestFixtures.Context(
                    StoryTestFixtures.City(March1994, happiness: happiness)),
                Tuning);
        }

        // --- per-slot verdict by response mode ----------------------------------------------------

        /// <summary><c>PowerOverride</c> is an automatic success that was already paid for.</summary>
        [Fact]
        public void Resolve_PowerOverrideIsAnAutomaticSuccess()
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.MetSlot("evt-a", SlotRole.Major));

            StoryResolutionResult result = Resolve(story, happiness: 0.0);

            Assert.Equal(SlotOutcome.Met, Assert.Single(result.SlotOutcomes));
            Assert.Equal(1, result.MetCount);
            Assert.Equal(1, result.ScoredCount);
        }

        /// <summary><c>Ignore</c> is an automatic failure — the player decided.</summary>
        [Fact]
        public void Resolve_IgnoreIsAnAutomaticFailure()
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.NotMetSlot("evt-a", SlotRole.Major));

            StoryResolutionResult result = Resolve(story, happiness: 100.0);

            Assert.Equal(SlotOutcome.NotMet, Assert.Single(result.SlotOutcomes));
            Assert.Equal(0, result.MetCount);
            Assert.Equal(1, result.ScoredCount);
        }

        /// <summary>
        /// <b>A <c>Manual</c> slot is neutral until declared.</b> Silence is not the same as a
        /// decision: the player who has not yet written their own verdict has not failed one.
        /// </summary>
        [Fact]
        public void Resolve_ManualIsNeutralUntilDeclared()
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.UnmeasurableSlot("evt-a", SlotRole.Major));

            StoryResolutionResult result = Resolve(story);

            Assert.Equal(SlotOutcome.Unmeasurable, Assert.Single(result.SlotOutcomes));
            Assert.Equal(0, result.MetCount);
            Assert.Equal(0, result.ScoredCount);
        }

        [Fact]
        public void Resolve_ManualScoresOnceDeclared()
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.Slot("evt-a", SlotResponse.Manual, SlotRole.Major, manualDeclared: true));

            StoryResolutionResult result = Resolve(story, happiness: 0.0);

            Assert.Equal(SlotOutcome.Met, Assert.Single(result.SlotOutcomes));
            Assert.Equal(1, result.ScoredCount);
        }

        /// <summary>
        /// <c>Unaddressed</c> is silence, not a decision, and only a decision may be the player's
        /// fault. It is emphatically not the same as <c>Ignore</c>.
        /// </summary>
        [Fact]
        public void Resolve_UnaddressedIsNotScoredAsFailure()
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.Slot("evt-a", SlotResponse.Unaddressed, SlotRole.Major));

            StoryResolutionResult result = Resolve(story);

            Assert.NotEqual(SlotOutcome.NotMet, Assert.Single(result.SlotOutcomes));
            Assert.Equal(0, result.MetCount);
            Assert.Equal(0, result.ScoredCount);
        }

        /// <summary>
        /// A <c>Goal</c> runs the event's <see cref="CheckSpec"/> through
        /// <see cref="TriggerEvaluator"/> — one comparison implementation, not a second one written
        /// here.
        /// </summary>
        [Theory]
        [InlineData(80.0, SlotOutcome.Met)]
        [InlineData(20.0, SlotOutcome.NotMet)]
        public void Resolve_GoalRunsTheCheckThroughTheEvaluator(double happiness, SlotOutcome expected)
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.Slot("evt-a", SlotResponse.Goal, SlotRole.Major));

            StoryResolutionResult result = Resolve(story, happiness: happiness, goalThreshold: 50.0);

            Assert.Equal(expected, Assert.Single(result.SlotOutcomes));
            Assert.Equal(1, result.ScoredCount);
        }

        /// <summary>
        /// A goal whose metric cannot be read is unmeasurable, and it costs the player nothing. This
        /// is the case the whole three-state design exists for.
        /// </summary>
        [Fact]
        public void Resolve_GoalWithAnUnreadableCheckIsUnmeasurable()
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.Slot("evt-a", SlotResponse.Goal, SlotRole.Major));

            var catalog = new List<CivicEvent>
            {
                StoryTestFixtures.Event("evt-a", 2, check: StoryTestFixtures.Check(
                    StoryTestFixtures.Metric("notAMetric", Comparison.GreaterThanOrEqual, 1.0)))
            };

            StoryResolutionResult result = StoryResolution.Resolve(story, catalog,
                StoryTestFixtures.Context(StoryTestFixtures.City(March1994)), Tuning);

            Assert.Equal(SlotOutcome.Unmeasurable, Assert.Single(result.SlotOutcomes));
            Assert.Equal(0, result.ScoredCount);
        }

        // --- the threshold over scored slots ------------------------------------------------------

        /// <summary>
        /// A full story built from tuning: <c>eventsPerStory</c> slots of which exactly
        /// <paramref name="metSlots"/> are met and the rest not-met. Generated rather than typed out,
        /// so the fixture follows a balance pass instead of contradicting it.
        /// </summary>
        private static Story FullStory(int metSlots)
        {
            var slots = new List<StorySlot>();
            for (int i = 0; i < Tuning.Stories.EventsPerStory; i++)
            {
                string id = "evt-" + ((char)('a' + i));
                SlotRole role = i == 0 ? SlotRole.Major : SlotRole.Minor;
                slots.Add(i < metSlots
                    ? StoryTestFixtures.MetSlot(id, role)
                    : StoryTestFixtures.NotMetSlot(id, role));
            }

            return StoryTestFixtures.Story("story-01", March1994, slots.ToArray());
        }

        /// <summary>
        /// A full story needs <c>stories.successThreshold</c> of its slots met. Exactly the threshold
        /// succeeds and one below it fails, both built from tuning rather than from a memorised 2 —
        /// the shape under test is "enough of them", never the literal.
        /// </summary>
        [Fact]
        public void Resolve_AFullStoryNeedsExactlyTheTunedThreshold()
        {
            int threshold = Tuning.Stories.SuccessThreshold;

            StoryResolutionResult atThreshold = Resolve(FullStory(threshold));
            Assert.Equal(threshold, atThreshold.MetCount);
            Assert.Equal(Tuning.Stories.EventsPerStory, atThreshold.ScoredCount);
            Assert.Equal(StoryOutcome.Success, atThreshold.Outcome);

            StoryResolutionResult below = Resolve(FullStory(threshold - 1));
            Assert.Equal(threshold - 1, below.MetCount);
            Assert.Equal(Tuning.Stories.EventsPerStory, below.ScoredCount);
            Assert.Equal(StoryOutcome.Failure, below.Outcome);
        }

        /// <summary>Every slot met is a success under any threshold the tuning could carry.</summary>
        [Fact]
        public void Resolve_AFullStoryWithEverySlotMetSucceeds()
        {
            Assert.Equal(StoryOutcome.Success, Resolve(FullStory(Tuning.Stories.EventsPerStory)).Outcome);
        }

        // --- unmeasurable slots, the whole point --------------------------------------------------

        /// <summary>
        /// One unmeasurable slot among three. <b><see cref="StoryResolutionResult.ScoredCount"/> is 2,
        /// not 3</b> — the assertion that fails if the unreadable slot were counted in the
        /// denominator, even though the verdict happens to come out the same either way.
        /// </summary>
        [Fact]
        public void Resolve_ExcludesOneUnmeasurableSlotFromTheDenominator()
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.MetSlot("evt-a", SlotRole.Major),
                StoryTestFixtures.MetSlot("evt-b"),
                StoryTestFixtures.UnmeasurableSlot("evt-c"));

            StoryResolutionResult result = Resolve(story);

            Assert.Equal(2, result.ScoredCount);
            Assert.Equal(2, result.MetCount);
            Assert.Equal(StoryOutcome.Success, result.Outcome);
        }

        /// <summary>
        /// <b>Two unmeasurable among three, and this is the fixture that separates the two scorings
        /// outright.</b> Excluding them leaves one scored slot, all of which was met, so the story
        /// succeeds. Counting them as failures in the denominator leaves 1 of 3, below the threshold,
        /// so the story fails — and the player loses power because two sensors were dark.
        /// </summary>
        [Fact]
        public void Resolve_OneMetSlotAmongTwoUnmeasurableOnesSucceeds()
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.MetSlot("evt-a", SlotRole.Major),
                StoryTestFixtures.UnmeasurableSlot("evt-b"),
                StoryTestFixtures.UnmeasurableSlot("evt-c"));

            StoryResolutionResult result = Resolve(story);

            Assert.Equal(1, result.ScoredCount);
            Assert.Equal(1, result.MetCount);
            Assert.Equal(StoryOutcome.Success, result.Outcome);
        }

        /// <summary>
        /// The same shape with the one scored slot failed. Exclusion does not mean leniency: a story
        /// whose only readable slot went unmet is a failure.
        /// </summary>
        [Fact]
        public void Resolve_OneNotMetSlotAmongTwoUnmeasurableOnesFails()
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.NotMetSlot("evt-a", SlotRole.Major),
                StoryTestFixtures.UnmeasurableSlot("evt-b"),
                StoryTestFixtures.UnmeasurableSlot("evt-c"));

            StoryResolutionResult result = Resolve(story);

            Assert.Equal(1, result.ScoredCount);
            Assert.Equal(0, result.MetCount);
            Assert.Equal(StoryOutcome.Failure, result.Outcome);
        }

        /// <summary>
        /// <b>No scored slots at all resolves <see cref="StoryOutcome.Abandoned"/>, not
        /// <see cref="StoryOutcome.Failure"/>.</b> There is nothing to have a verdict about, and
        /// calling that a failure is precisely charging the player for a sensor gap.
        /// </summary>
        [Fact]
        public void Resolve_AStoryThatScoredNothingIsAbandonedRatherThanFailed()
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.UnmeasurableSlot("evt-a", SlotRole.Major),
                StoryTestFixtures.UnmeasurableSlot("evt-b"),
                StoryTestFixtures.UnmeasurableSlot("evt-c"));

            StoryResolutionResult result = Resolve(story);

            Assert.Equal(0, result.ScoredCount);
            Assert.Equal(0, result.MetCount);
            Assert.Equal(StoryOutcome.Abandoned, result.Outcome);
        }

        /// <summary>A story with no slots at all is the degenerate case of the same rule.</summary>
        [Fact]
        public void Resolve_AnEmptyStoryIsAbandoned()
        {
            Story story = StoryTestFixtures.Story("story-01", March1994);

            StoryResolutionResult result = StoryResolution.Resolve(story, new List<CivicEvent>(),
                StoryTestFixtures.Context(StoryTestFixtures.City(March1994)), Tuning);

            Assert.Empty(result.SlotOutcomes);
            Assert.Equal(0, result.ScoredCount);
            Assert.Equal(StoryOutcome.Abandoned, result.Outcome);
        }

        // --- degraded stories ---------------------------------------------------------------------

        /// <summary>
        /// <b>A degraded two-slot story needs all of its scored slots.</b> The threshold is a ratio,
        /// not a count: a story of two that met one has met half, and half is not "most of it".
        /// </summary>
        [Fact]
        public void Resolve_ATwoSlotStoryNeedsBothOfItsScoredSlots()
        {
            Story both = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.MetSlot("evt-a", SlotRole.Major),
                StoryTestFixtures.MetSlot("evt-b"));

            Story one = StoryTestFixtures.Story("story-02", March1994,
                StoryTestFixtures.MetSlot("evt-a", SlotRole.Major),
                StoryTestFixtures.NotMetSlot("evt-b"));

            Assert.Equal(StoryOutcome.Success, Resolve(both).Outcome);

            StoryResolutionResult partial = Resolve(one);
            Assert.Equal(1, partial.MetCount);
            Assert.Equal(2, partial.ScoredCount);
            Assert.Equal(StoryOutcome.Failure, partial.Outcome);
        }

        /// <summary>
        /// A degraded story with one of its two slots unreadable falls back to the same rule over what
        /// is left: one scored slot, met, so Success.
        /// </summary>
        [Fact]
        public void Resolve_ATwoSlotStoryWithOneUnmeasurableSlotScoresOnTheOther()
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.MetSlot("evt-a", SlotRole.Major),
                StoryTestFixtures.UnmeasurableSlot("evt-b"));

            StoryResolutionResult result = Resolve(story);

            Assert.Equal(1, result.ScoredCount);
            Assert.Equal(1, result.MetCount);
            Assert.Equal(StoryOutcome.Success, result.Outcome);
        }

        /// <summary>A one-slot story — what a mandatory event gets — is decided by that slot alone.</summary>
        [Theory]
        [InlineData(SlotResponse.PowerOverride, StoryOutcome.Success)]
        [InlineData(SlotResponse.Ignore, StoryOutcome.Failure)]
        [InlineData(SlotResponse.Manual, StoryOutcome.Abandoned)]
        public void Resolve_AMandatoryStorysSingleSlotDecidesIt(SlotResponse response,
                                                                StoryOutcome expected)
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.Slot("evt-a", response, SlotRole.Major));
            story.IsMandatory = true;

            Assert.Equal(expected, Resolve(story).Outcome);
        }

        // --- the result's own shape ---------------------------------------------------------------

        /// <summary>
        /// <see cref="StoryResolutionResult.SlotOutcomes"/> is in the story's own slot order so the
        /// two lists line up by index — the caller writes each verdict back onto the slot it belongs
        /// to, and an order that only usually matched would mislabel the player's own choices.
        /// </summary>
        [Fact]
        public void Resolve_ReturnsOneOutcomePerSlotInTheStorysOwnOrder()
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.MetSlot("evt-a", SlotRole.Major),
                StoryTestFixtures.NotMetSlot("evt-b"),
                StoryTestFixtures.UnmeasurableSlot("evt-c"));

            StoryResolutionResult result = Resolve(story);

            Assert.Equal(story.Slots.Count, result.SlotOutcomes.Count);
            Assert.Equal(SlotOutcome.Met, result.SlotOutcomes[0]);
            Assert.Equal(SlotOutcome.NotMet, result.SlotOutcomes[1]);
            Assert.Equal(SlotOutcome.Unmeasurable, result.SlotOutcomes[2]);
        }

        /// <summary>
        /// No slot may come back <see cref="SlotOutcome.Pending"/>: the whole point of resolving is
        /// that every slot now has a verdict, even if that verdict is "we could not see".
        /// </summary>
        [Fact]
        public void Resolve_LeavesNoSlotPending()
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.Slot("evt-a", SlotResponse.Goal, SlotRole.Major),
                StoryTestFixtures.Slot("evt-b", SlotResponse.Unaddressed),
                StoryTestFixtures.UnmeasurableSlot("evt-c"));

            foreach (SlotOutcome outcome in Resolve(story).SlotOutcomes)
            {
                Assert.NotEqual(SlotOutcome.Pending, outcome);
            }
        }

        /// <summary>
        /// The counts must agree with the verdicts they claim to summarise. Asserted separately
        /// because they are what the power economy is paid from, and a summary that drifted from the
        /// per-slot list would pay for outcomes nobody reached.
        /// </summary>
        [Fact]
        public void Resolve_CountsAgreeWithThePerSlotVerdicts()
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.MetSlot("evt-a", SlotRole.Major),
                StoryTestFixtures.NotMetSlot("evt-b"),
                StoryTestFixtures.UnmeasurableSlot("evt-c"));

            StoryResolutionResult result = Resolve(story);

            int met = 0;
            int scored = 0;
            foreach (SlotOutcome outcome in result.SlotOutcomes)
            {
                if (outcome == SlotOutcome.Met) met++;
                if (outcome == SlotOutcome.Met || outcome == SlotOutcome.NotMet) scored++;
            }

            Assert.Equal(met, result.MetCount);
            Assert.Equal(scored, result.ScoredCount);
        }

        /// <summary>Evidence is sorted by metric id, like every other collection that crosses a save.</summary>
        [Fact]
        public void Resolve_SortsTheEvidenceByMetricId()
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.Slot("evt-a", SlotResponse.Goal, SlotRole.Major),
                StoryTestFixtures.Slot("evt-b", SlotResponse.Goal),
                StoryTestFixtures.Slot("evt-c", SlotResponse.Goal));

            var catalog = new List<CivicEvent>
            {
                StoryTestFixtures.Event("evt-a", 2, check: StoryTestFixtures.Check(
                    StoryTestFixtures.Metric(MetricHistory.Unemployment, Comparison.LessThan, 1.0))),
                StoryTestFixtures.Event("evt-b", 2, check: StoryTestFixtures.Check(
                    StoryTestFixtures.Metric(MetricHistory.CrimeRate, Comparison.LessThan, 1.0))),
                StoryTestFixtures.Event("evt-c", 2, check: StoryTestFixtures.Check(
                    StoryTestFixtures.Metric(MetricHistory.Happiness, Comparison.GreaterThan, 0.0)))
            };

            StoryResolutionResult result = StoryResolution.Resolve(story, catalog,
                StoryTestFixtures.Context(StoryTestFixtures.City(March1994)), Tuning);

            for (int i = 1; i < result.Evidence.Count; i++)
            {
                Assert.True(
                    string.CompareOrdinal(result.Evidence[i - 1].MetricId, result.Evidence[i].MetricId) < 0,
                    "Evidence is not sorted by metric id ordinal.");
            }
        }

        /// <summary>
        /// Resolution reads the recorded evidence when there is any, rather than re-measuring — which
        /// is what makes a <c>Resolve now</c> command deterministic on replay. Asserted with the live
        /// snapshot saying the opposite of the record, so a re-measurement cannot pass by accident.
        /// </summary>
        [Fact]
        public void Resolve_ScoresAgainstRecordedEvidenceRatherThanReMeasuring()
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.Slot("evt-a", SlotResponse.Goal, SlotRole.Major));

            StoryReadContext recorded = StoryTestFixtures.WithEvidence(
                StoryTestFixtures.Context(StoryTestFixtures.City(March1994, happiness: 10.0)),
                StoryTestFixtures.Reading(MetricHistory.Happiness, 90.0));

            StoryResolutionResult result = Resolve(story, goalThreshold: 50.0, context: recorded);

            Assert.Equal(SlotOutcome.Met, Assert.Single(result.SlotOutcomes));
        }

        /// <summary>
        /// Resolution is pure: nothing here is applied, and the story it was handed comes back
        /// untouched. The caller writes the verdicts on — a resolver that wrote them itself would make
        /// a speculative advance permanent.
        /// </summary>
        [Fact]
        public void Resolve_DoesNotMutateTheStory()
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.MetSlot("evt-a", SlotRole.Major),
                StoryTestFixtures.NotMetSlot("evt-b"),
                StoryTestFixtures.UnmeasurableSlot("evt-c"));

            string before = AgoraJson.Fingerprint(story);
            Resolve(story);

            Assert.Equal(before, AgoraJson.Fingerprint(story));
        }

        /// <summary>
        /// Same story, same city, twice: identical results. Resolution takes no seeded draw of its
        /// own, so this is a stronger claim than it looks — it says nothing in the scoring path reads
        /// a dictionary in enumeration order.
        /// </summary>
        [Fact]
        public void Resolve_IsByteIdenticalOnASecondRun()
        {
            Story story = StoryTestFixtures.Story("story-01", March1994,
                StoryTestFixtures.Slot("evt-a", SlotResponse.Goal, SlotRole.Major),
                StoryTestFixtures.MetSlot("evt-b"),
                StoryTestFixtures.UnmeasurableSlot("evt-c"));

            Assert.Equal(AgoraJson.Fingerprint(Resolve(story)),
                         AgoraJson.Fingerprint(Resolve(story)));
        }
    }
}
