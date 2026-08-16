using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Stories;
using Agora.Core.Tuning;
using Agora.Mod.Sensors;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The story cycle's boundaries: idempotence at a re-entered month, the stranded-story sweep,
    /// replay suspension, and an early resolve scored from its recorded evidence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>These are lane 4a's specification, written against the seam rather than against a body.</b>
    /// <see cref="StoryCycle.Run"/> ships as an <c>AGORA-SEAM</c> stub that returns an empty result,
    /// so every test here fails until the real cycle lands. That is the intended state — the file is
    /// merged into the wave-4 umbrella after 4a and verified there.
    /// </para>
    /// <para>
    /// <b>Nothing below is asserted against an empty answer alone.</b> A test whose only claim is
    /// "nothing happened" passes against a stub while asserting nothing, so every one of them first
    /// establishes that something <i>did</i> happen — a draft that drafted, a resolution that reached
    /// a verdict — and only then asserts that re-entering the month leaves it alone. The one
    /// exception is <see cref="ReplayDraftsNothingAndAccruesNoPower"/>, which is paired with a
    /// non-replayed control run in the same test for exactly that reason.
    /// </para>
    /// <para>
    /// Every threshold comes off <see cref="EngineTuning"/> rather than being written down. The
    /// cadence in particular: a story lives <c>stories.cycleMonths - 1</c> months — <b>one</b>, not
    /// two — and both numbers are read, never typed.
    /// </para>
    /// </remarks>
    public sealed class StoryCycleTests
    {
        private static readonly SimDate Start = StoryTestFixtures.March1994;

        private static EngineTuning Tuning => EngineTuning.Default;

        /// <summary>The cadence between draws. <b>Not</b> the window a story lives for.</summary>
        private static int CycleMonths => Tuning.Stories.CycleMonths;

        /// <summary>How long a story is open, which is one month less than the cadence.</summary>
        private static int StoryLifeMonths => CycleMonths - 1;

        // ---------------------------------------------------------------- idempotence

        /// <summary>
        /// Re-entering a draft month drafts nothing further, and the guard is the month stamp.
        /// </summary>
        /// <remarks>
        /// The common case: the player saves inside the month Agora drafted on, and reloads. Wave 0's
        /// <c>LastCompletedTickMonth</c> is the partner half; <c>LastStoryDraftMonth</c> is the
        /// cycle's own, and it has to be stamped for the second entry to be able to refuse.
        /// </remarks>
        [Fact]
        public void ReEnteringADraftMonthDoesNotDraftTwice()
        {
            PoliticalState state = Fresh(Start);

            StoryCycleResult first = Run(state, Start);
            AssertDrafted(first);

            int liveAfterFirst = state.LiveStories.Count;
            Assert.Equal(Start.TotalMonths, state.LastStoryDraftMonth);

            StoryCycleResult second = Run(state, Start);

            Assert.Empty(second.DraftedStories);
            Assert.Equal(liveAfterFirst, state.LiveStories.Count);
            Assert.Equal(Start.TotalMonths, state.LastStoryDraftMonth);
        }

        /// <summary>
        /// Re-entering a resolve month reaches no second verdict.
        /// </summary>
        /// <remarks>
        /// <c>Story.Outcome != Pending</c> is the guard and it is stamped before the effects are
        /// dispatched, so a crash between the two loses the effects rather than the record. What this
        /// asserts is the consequence: whatever the story resolved to, it stays resolved to that.
        /// </remarks>
        [Fact]
        public void ReEnteringAResolveMonthDoesNotResolveTwice()
        {
            PoliticalState state = Fresh(Start);
            AssertDrafted(Run(state, Start));

            SimDate due = Start.AddMonths(StoryLifeMonths);
            StoryCycleResult resolved = Run(state, due);
            AssertResolved(resolved);

            int archived = state.StoryArchive.Count;
            int live = state.LiveStories.Count;
            List<StoryOutcome> outcomes = Outcomes(resolved.ResolvedStories);

            StoryCycleResult again = Run(state, due);

            Assert.Empty(again.ResolvedStories);
            Assert.Equal(archived, state.StoryArchive.Count);
            Assert.Equal(live, state.LiveStories.Count);
            Assert.Equal(outcomes, Outcomes(resolved.ResolvedStories));
            Assert.Equal(due.TotalMonths, state.LastStoryResolveMonth);
        }

        /// <summary>
        /// One month pays out one accrual, however many times the month is re-entered.
        /// </summary>
        /// <remarks>
        /// <c>PoliticalPowerState.LastAccrualMonth</c> is the guard, and
        /// <see cref="PoliticalPower.AccrualFor"/> says out loud that it is a pure function with no
        /// state to check the month against — so the guard exists only at this call site. Without it,
        /// saving and reloading across a month boundary farms power without limit.
        /// </remarks>
        [Fact]
        public void ReEnteringAMonthDoesNotAccruePowerTwice()
        {
            PoliticalState state = Fresh(Start);
            state.Power.Balance = 0;

            StoryCycleResult first = Run(state, Start, governingVoteShare: 1.0);

            Assert.True(state.Power.Balance > 0,
                "A governing party on a full vote share must accrue something, or this test asserts nothing.");
            Assert.Equal(Start.TotalMonths, state.Power.LastAccrualMonth);

            int balance = state.Power.Balance;
            int ledger = state.Power.Ledger.Count;

            StoryCycleResult second = Run(state, Start, governingVoteShare: 1.0);

            Assert.Equal(balance, state.Power.Balance);
            Assert.Equal(ledger, state.Power.Ledger.Count);
            Assert.Equal(0, second.PowerDelta);
        }

        /// <summary>
        /// The accrual is bounded by <c>power.maxMonthlyGain</c> whatever the share.
        /// </summary>
        /// <remarks>
        /// The shape, not the coefficient: the cap is read from tuning, so a balance pass that moves
        /// it moves this test with it. The cycle must not compute its own accrual — the seam says
        /// <see cref="PoliticalPower.AccrualFor"/> is the only source of that figure — and the cheapest
        /// way to notice a second derivation is that it exceeds the ceiling the first one respects.
        /// </remarks>
        [Fact]
        public void OneMonthNeverAccruesMoreThanTheMonthlyCeiling()
        {
            PoliticalState state = Fresh(Start);
            state.Power.Balance = 0;

            Run(state, Start, governingVoteShare: 1.0);

            Assert.InRange(state.Power.Balance, 1, Tuning.Power.MaxMonthlyGain);
        }

        /// <summary>
        /// <b>The ordinary mid-month reload: nothing changes at all.</b>
        /// </summary>
        /// <remarks>
        /// The player saves and reloads while a story is open and not yet due. This is the common case
        /// and the one a careless sweep boundary breaks — an operator one step out closes every story
        /// the moment the player reloads, which is the double-tick class of bug wave 0 existed to
        /// remove. The story must still be live, still <see cref="StoryOutcome.Pending"/>, and the
        /// cycle must report nothing.
        /// </remarks>
        [Fact]
        public void AMidMonthReEntryLeavesALiveStoryUntouched()
        {
            PoliticalState state = Fresh(Start);
            StoryCycleResult drafted = Run(state, Start);
            AssertDrafted(drafted);

            int live = state.LiveStories.Count;

            StoryCycleResult again = Run(state, Start);

            Assert.Empty(again.DraftedStories);
            Assert.Empty(again.ResolvedStories);
            Assert.Equal(live, state.LiveStories.Count);

            for (int i = 0; i < state.LiveStories.Count; i++)
            {
                Assert.Equal(StoryOutcome.Pending, state.LiveStories[i].Outcome);
                Assert.Equal(-1, state.LiveStories[i].ResolvedMonth);
            }
        }

        // ---------------------------------------------------------------- the sweep boundary

        /// <summary>
        /// A story due <i>this</i> month resolves through the ordinary pass, with a real verdict.
        /// </summary>
        /// <remarks>
        /// One of the three cases the sweep must not reap. Reaping it would replace a verdict the
        /// player earned with <see cref="StoryOutcome.Abandoned"/>, which pays nothing — so a
        /// successful story would silently become worth nothing on the month it succeeded.
        /// </remarks>
        [Fact]
        public void AStoryDueThisMonthResolvesThroughTheOrdinaryPass()
        {
            SimDate due = Start.AddMonths(StoryLifeMonths);

            PoliticalState state = StoryTestFixtures.State(Start);
            state.LiveStories.Add(WonStory("story-due-now", Start));

            StoryCycleResult result = Run(state, due, catalog: Catalog());

            Story resolved = Assert.Single(result.ResolvedStories);
            Assert.Equal(StoryOutcome.Success, resolved.Outcome);
            Assert.Equal(due.TotalMonths, resolved.ResolvedMonth);
        }

        /// <summary>A story due in a later month is not touched at all.</summary>
        /// <remarks>
        /// The second half of the test is the control, and it is what stops the first half asserting
        /// nothing: the same story on the same state <i>does</i> resolve once its due month arrives,
        /// so "not touched" is a claim about the month rather than about a cycle that never touches
        /// anything.
        /// </remarks>
        [Fact]
        public void AStoryDueLaterIsNotTouched()
        {
            PoliticalState state = StoryTestFixtures.State(Start);
            state.LiveStories.Add(WonStory("story-due-later", Start));

            StoryCycleResult early = Run(state, Start, catalog: Catalog());

            Assert.Empty(early.ResolvedStories);
            Story live = Assert.Single(state.LiveStories);
            Assert.Equal(StoryOutcome.Pending, live.Outcome);

            StoryCycleResult onTime = Run(state, Start.AddMonths(StoryLifeMonths), catalog: Catalog());

            Assert.Single(onTime.ResolvedStories);
        }

        /// <summary>
        /// <b>Catch-up truncation leaves no stranded story.</b>
        /// </summary>
        /// <remarks>
        /// <c>TickPlanner.CatchUpDates</c> drops the <i>oldest</i> months when a gap exceeds
        /// <c>scheduler.catchUpMaxMonths</c>, so the month a story was due on can be skipped entirely
        /// and never come round again. Without the sweep it stays pending for the life of the save.
        /// The gap here is read from tuning and taken past the cap, which is the condition that
        /// produces the truncation in the first place.
        /// </remarks>
        [Fact]
        public void CatchUpTruncationLeavesNoStrandedStory()
        {
            SimDate due = Start.AddMonths(StoryLifeMonths);
            SimDate afterTheGap = due.AddMonths(Tuning.Scheduler.CatchUpMaxMonths + 1);

            PoliticalState state = StoryTestFixtures.State(Start);
            state.LiveStories.Add(WonStory("story-stranded", Start));

            StoryCycleResult result = Run(state, afterTheGap, catalog: Catalog());

            Story reaped = Assert.Single(result.ResolvedStories);
            Assert.Equal("story-stranded", reaped.Id);
            Assert.NotEqual(StoryOutcome.Pending, reaped.Outcome);
            Assert.DoesNotContain(state.LiveStories, s => s.Id == "story-stranded");
            Assert.Contains(state.StoryArchive, s => s.Id == "story-stranded");
        }

        /// <summary>
        /// The sweep is not a phase. It runs at the top of the cycle whatever the month is for.
        /// </summary>
        /// <remarks>
        /// A stranded story is stranded precisely because the phase it needed never arrived, so
        /// gating the sweep on that phase would leave it stranded a second time — and the failure
        /// would be invisible at the shipped cadence of two, where every month is one phase or the
        /// other.
        /// </remarks>
        [Fact]
        public void TheStrandedSweepDoesNotDependOnThePhase()
        {
            SimDate due = Start.AddMonths(StoryLifeMonths);
            SimDate afterTheGap = due.AddMonths(Tuning.Scheduler.CatchUpMaxMonths + 1);

            PoliticalState state = StoryTestFixtures.State(Start);
            state.LiveStories.Add(WonStory("story-stranded", Start));

            StoryCycleResult result = Run(state, afterTheGap, catalog: Catalog(),
                                          draft: false, resolve: false);

            Assert.Single(result.ResolvedStories);
            Assert.Empty(state.LiveStories);
        }

        /// <summary>
        /// <b>A swept story whose evidence is gone is <see cref="StoryOutcome.Abandoned"/>, not
        /// failed.</b>
        /// </summary>
        /// <remarks>
        /// The catalog no longer explains the slot, so nothing can be scored — every slot is
        /// unmeasurable and the scored count is zero. Calling that a failure would charge the player
        /// for the scheduler's own truncation. <see cref="StoryResolution.Resolve"/> already answers
        /// <see cref="StoryOutcome.Abandoned"/> to a story with no scored slots; what is asserted here
        /// is that the sweep routes through it rather than stamping a verdict of its own.
        /// </remarks>
        [Fact]
        public void AStrandedStoryWithNoEvidenceIsAbandonedRatherThanFailed()
        {
            SimDate due = Start.AddMonths(StoryLifeMonths);
            SimDate afterTheGap = due.AddMonths(Tuning.Scheduler.CatchUpMaxMonths + 1);

            PoliticalState state = StoryTestFixtures.State(Start);
            state.LiveStories.Add(GoalStory("story-evidenceless", Start, "event-the-catalog-forgot"));

            // An empty catalog is the evidence being gone: the slot names an event nothing explains,
            // so there is no check to run and no honest verdict to reach.
            StoryCycleResult result = Run(state, afterTheGap, catalog: new List<CivicEvent>());

            Story reaped = Assert.Single(result.ResolvedStories);
            Assert.Equal(StoryOutcome.Abandoned, reaped.Outcome);
        }

        /// <summary>An abandoned story pays nothing in either direction.</summary>
        /// <remarks>
        /// Both directions, deliberately: it is not a penalty <i>and</i> it is not an award. The
        /// balance is seeded non-zero so that "unchanged" is a claim rather than a coincidence, and
        /// the governing share is zero so no accrual can hide a movement.
        /// </remarks>
        [Fact]
        public void AnAbandonedStoryPaysNothingInEitherDirection()
        {
            SimDate due = Start.AddMonths(StoryLifeMonths);
            SimDate afterTheGap = due.AddMonths(Tuning.Scheduler.CatchUpMaxMonths + 1);

            PoliticalState state = StoryTestFixtures.State(Start);
            state.LiveStories.Add(GoalStory("story-evidenceless", Start, "event-the-catalog-forgot"));
            state.Power.Balance = 100;

            StoryCycleResult result = Run(state, afterTheGap, catalog: new List<CivicEvent>(),
                                          governingVoteShare: 0.0);

            Assert.Equal(StoryOutcome.Abandoned, Assert.Single(result.ResolvedStories).Outcome);
            Assert.Equal(100, state.Power.Balance);
            Assert.Equal(0, result.PowerDelta);
            Assert.DoesNotContain(state.Power.Ledger, e => e.StoryId == "story-evidenceless");
        }

        // ---------------------------------------------------------------- replay

        /// <summary>
        /// <b>A replayed month produces no stories and no power.</b>
        /// </summary>
        /// <remarks>
        /// See <see cref="StoryCycleInput.IsReplay"/> for the two hazards that decided it: replay does
        /// not dispatch effects, so a story drafted and resolved inside a replayed window would award
        /// power while applying none of its effects; and replay scores every replayed month against
        /// today's city, so a check in a replayed window would evaluate 2005's crime wave against
        /// 2031's crime rate. The control run in the same test is what stops this asserting nothing
        /// against a stub that returns an empty result whatever it is handed.
        /// </remarks>
        [Fact]
        public void ReplayDraftsNothingAndAccruesNoPower()
        {
            PoliticalState control = Fresh(Start);
            AssertDrafted(Run(control, Start, governingVoteShare: 1.0));
            Assert.True(control.Power.Balance > 0, "The control run must move something.");

            PoliticalState replayed = Fresh(Start);
            replayed.Power.Balance = 0;

            StoryCycleResult result = Run(replayed, Start, replay: true, governingVoteShare: 1.0);

            Assert.Empty(result.DraftedStories);
            Assert.Empty(replayed.LiveStories);
            Assert.Equal(0, result.PowerDelta);
            Assert.Equal(0, replayed.Power.Balance);
            Assert.Empty(replayed.Power.Ledger);

            Assert.NotEmpty(result.Warnings);
        }

        /// <summary>A replayed month resolves nothing either, and leaves the due story pending.</summary>
        /// <remarks>
        /// The control run comes first, on an identical state: the month is a resolve month and the
        /// story really is due on it, so the replayed branch is being denied something the lived
        /// branch is given rather than something nothing ever produces.
        /// </remarks>
        [Fact]
        public void ReplayResolvesNothingAndLeavesTheStoryDue()
        {
            SimDate due = Start.AddMonths(StoryLifeMonths);

            PoliticalState control = StoryTestFixtures.State(Start);
            control.LiveStories.Add(WonStory("story-due-now", Start));
            AssertResolved(Run(control, due, catalog: Catalog()));

            PoliticalState state = StoryTestFixtures.State(Start);
            state.LiveStories.Add(WonStory("story-due-now", Start));
            state.Power.Balance = 100;

            StoryCycleResult result = Run(state, due, catalog: Catalog(), replay: true);

            Assert.Empty(result.ResolvedStories);
            Assert.Equal(StoryOutcome.Pending, Assert.Single(state.LiveStories).Outcome);
            Assert.Equal(100, state.Power.Balance);
            Assert.Equal(0, result.PowerDelta);
        }

        /// <summary>
        /// Replay does not reap either: a stranded story survives a replayed month and is swept on
        /// the first lived one.
        /// </summary>
        /// <remarks>
        /// The suspension is of the whole cycle, not of drafting alone. Abandoning a story during
        /// replay would be the engine deciding, inside a window the player never lived, that they
        /// never got to answer it — and the sweep on the lived month reaches the same verdict anyway.
        /// </remarks>
        [Fact]
        public void ReplayDoesNotSweepAStrandedStory()
        {
            SimDate due = Start.AddMonths(StoryLifeMonths);
            SimDate afterTheGap = due.AddMonths(Tuning.Scheduler.CatchUpMaxMonths + 1);

            PoliticalState state = StoryTestFixtures.State(Start);
            state.LiveStories.Add(WonStory("story-stranded", Start));

            StoryCycleResult replayed = Run(state, afterTheGap, catalog: Catalog(), replay: true);

            Assert.Empty(replayed.ResolvedStories);
            Assert.Single(state.LiveStories);

            StoryCycleResult lived = Run(state, afterTheGap, catalog: Catalog());

            Assert.Single(lived.ResolvedStories);
            Assert.Empty(state.LiveStories);
        }

        // ---------------------------------------------------------------- recorded evidence

        /// <summary>
        /// <b>An early resolve replays its recorded snapshot rather than re-measuring.</b>
        /// </summary>
        /// <remarks>
        /// The player's <c>Resolve now</c> fires at an exogenous moment, so the sample it takes is
        /// written into <see cref="Story.ResolutionEvidence"/> and the resolution reads that through
        /// <see cref="StoryReadContext.RecordedEvidence"/>. Here the recorded reading meets the check
        /// and the city in front of us fails it by a wide margin: a cycle that re-measured would
        /// resolve this story to the opposite verdict, so the assertion cannot pass by accident.
        /// </remarks>
        [Fact]
        public void AnEarlyResolveScoresFromItsRecordedEvidenceAndNotFromTodaysCity()
        {
            SimDate due = Start.AddMonths(StoryLifeMonths);

            Story story = GoalStory("story-early", Start, HappinessEvent.Id);
            story.ResolveEarlyRequested = true;
            story.ResolutionEvidence.Add(StoryTestFixtures.Reading(MetricHistory.Happiness, 90.0));

            PoliticalState state = StoryTestFixtures.State(Start);
            state.LiveStories.Add(story);

            // The city today is nowhere near the threshold. Only the recorded reading can meet it.
            var context = StoryTestFixtures.Context(StoryTestFixtures.City(due, happiness: 5.0));

            StoryCycleResult result = Run(state, due, context: context,
                                          catalog: new List<CivicEvent> { HappinessEvent });

            Story resolved = Assert.Single(result.ResolvedStories);
            Assert.Equal(StoryOutcome.Success, resolved.Outcome);
        }

        /// <summary>
        /// <b>A recorded reading is identified by metric <i>and</i> district together.</b>
        /// </summary>
        /// <remarks>
        /// Matching on the metric alone would let one district's recorded reading answer for another's
        /// — a confident wrong answer, and worse than having no record at all. The fixture records
        /// only the first district; the second must fall through to its own live reading, which fails
        /// the check, so an <c>AllDistricts</c> verdict is not met. A cycle that keyed on the metric
        /// alone would hand the second district the first one's 90 and resolve this story
        /// <see cref="StoryOutcome.Success"/>.
        /// </remarks>
        [Fact]
        public void ARecordedReadingAnswersOnlyForItsOwnDistrict()
        {
            SimDate due = Start.AddMonths(StoryLifeMonths);

            Story story = GoalStory("story-districts", Start, DistrictHappinessEvent.Id);
            story.ResolveEarlyRequested = true;
            story.ResolutionEvidence.Add(
                StoryTestFixtures.Reading(MetricHistory.Happiness, 90.0, "district-01"));

            PoliticalState state = StoryTestFixtures.State(Start);
            state.LiveStories.Add(story);

            CitySnapshot city = StoryTestFixtures.City(due, districts: new[]
            {
                StoryTestFixtures.District("district-01", happiness: 5.0),
                StoryTestFixtures.District("district-02", happiness: 5.0)
            });

            StoryCycleResult result = Run(state, due, context: StoryTestFixtures.Context(city),
                                          catalog: new List<CivicEvent> { DistrictHappinessEvent });

            Story resolved = Assert.Single(result.ResolvedStories);
            Assert.Equal(StoryOutcome.Failure, resolved.Outcome);
        }

        /// <summary>
        /// An early-resolve request is honoured before the story's due month, and the story leaves
        /// the live list when it is.
        /// </summary>
        /// <remarks>
        /// The whole point of the button: the player does not wait. Its determinism comes from the
        /// recorded evidence, not from the timing, which is why the flag may be honoured on a month
        /// that is not the resolve phase at all.
        /// </remarks>
        [Fact]
        public void AnEarlyResolveRequestIsHonouredBeforeTheDueMonth()
        {
            Story story = GoalStory("story-early", Start, HappinessEvent.Id);
            story.ResolveEarlyRequested = true;
            story.ResolutionEvidence.Add(StoryTestFixtures.Reading(MetricHistory.Happiness, 90.0));

            PoliticalState state = StoryTestFixtures.State(Start);
            state.LiveStories.Add(story);

            // The draft month, a full cycle before this story is due.
            StoryCycleResult result = Run(state, Start,
                                          catalog: new List<CivicEvent> { HappinessEvent });

            Story resolved = Assert.Single(result.ResolvedStories);
            Assert.NotEqual(StoryOutcome.Pending, resolved.Outcome);
            Assert.DoesNotContain(state.LiveStories, s => s.Id == "story-early");
        }

        /// <summary>
        /// A story resolved early is not resolved a second time on the month it was originally due.
        /// </summary>
        /// <remarks>
        /// The idempotence guard has to survive the one path that reaches a verdict off-phase. Without
        /// it the ordinary resolve pass finds a story whose <c>ResolvesDate</c> is today and scores it
        /// again — this time against the live city, which is the very measurement the recorded
        /// evidence exists to avoid.
        /// </remarks>
        [Fact]
        public void AStoryResolvedEarlyIsNotResolvedAgainOnItsDueMonth()
        {
            Story story = GoalStory("story-early", Start, HappinessEvent.Id);
            story.ResolveEarlyRequested = true;
            story.ResolutionEvidence.Add(StoryTestFixtures.Reading(MetricHistory.Happiness, 90.0));

            PoliticalState state = StoryTestFixtures.State(Start);
            state.LiveStories.Add(story);

            var catalog = new List<CivicEvent> { HappinessEvent };
            Assert.Single(Run(state, Start, catalog: catalog).ResolvedStories);

            int archived = state.StoryArchive.Count;

            SimDate due = Start.AddMonths(StoryLifeMonths);
            StoryCycleResult onTheDueMonth = Run(state, due, catalog: catalog);

            Assert.Empty(onTheDueMonth.ResolvedStories);
            Assert.Equal(archived, state.StoryArchive.Count);
        }

        // ---------------------------------------------------------------- housekeeping

        /// <summary>
        /// The archive is bounded by <c>stories.archiveRetention</c>, trimmed where stories retire.
        /// </summary>
        /// <remarks>
        /// <c>PoliticalState.StoryArchive</c> says in its own remarks that it is intended to be
        /// bounded and that nothing enforces it, because archiving happens where a story is retired —
        /// which is the cycle. The bound is read from tuning; the pre-loaded archive is one over it so
        /// that the trim has exactly one thing to do.
        /// </remarks>
        [Fact]
        public void TheArchiveIsTrimmedToItsRetentionBound()
        {
            int retention = Tuning.Stories.ArchiveRetention;

            PoliticalState state = StoryTestFixtures.State(Start);
            for (int i = 0; i < retention; i++)
            {
                Story old = WonStory("story-old-" + i.ToString("D3"), Start.AddMonths(-24));
                old.Outcome = StoryOutcome.Success;
                old.ResolvedMonth = Start.AddMonths(-24 + StoryLifeMonths).TotalMonths;
                state.StoryArchive.Add(old);
            }

            state.LiveStories.Add(WonStory("story-due-now", Start));

            SimDate due = Start.AddMonths(StoryLifeMonths);
            Assert.Single(Run(state, due, catalog: Catalog()).ResolvedStories);

            Assert.True(state.StoryArchive.Count <= retention,
                "The archive is " + state.StoryArchive.Count + " with a retention of " + retention + ".");
            Assert.Contains(state.StoryArchive, s => s.Id == "story-due-now");
        }

        /// <summary>Every list the cycle returns leaves sorted by the key its contract declares.</summary>
        /// <remarks>
        /// An unsorted list fails the state hash while nothing is actually wrong, which is the most
        /// expensive kind of determinism failure to diagnose: it is stable within a run and different
        /// across runs.
        /// </remarks>
        [Fact]
        public void EveryListLeavesSortedByItsDeclaredKey()
        {
            PoliticalState state = Fresh(Start);
            StoryCycleResult drafted = Run(state, Start);
            AssertDrafted(drafted);

            AssertSortedById(drafted.DraftedStories);
            AssertSortedById(state.LiveStories);

            SimDate due = Start.AddMonths(StoryLifeMonths);
            StoryCycleResult resolved = Run(state, due);
            AssertResolved(resolved);

            AssertSortedById(resolved.ResolvedStories);

            for (int i = 1; i < resolved.Pressures.Count; i++)
            {
                Assert.True(
                    string.CompareOrdinal(resolved.Pressures[i - 1].StoryId,
                                          resolved.Pressures[i].StoryId) <= 0,
                    "Story pressures are not sorted by StoryId ordinal.");
            }
        }

        // ---------------------------------------------------------------- fixtures

        /// <summary>
        /// An event whose check is a city-wide happiness floor, so a recorded reading and a live one
        /// can disagree about it.
        /// </summary>
        private static readonly CivicEvent HappinessEvent = StoryTestFixtures.Major(
            "event-happiness",
            StoryTestFixtures.Check(StoryTestFixtures.Metric(
                MetricHistory.Happiness, Comparison.GreaterThanOrEqual, 50.0)));

        /// <summary>The same floor, quantified over every district.</summary>
        private static readonly CivicEvent DistrictHappinessEvent = StoryTestFixtures.Major(
            "event-district-happiness",
            StoryTestFixtures.Check(StoryTestFixtures.Metric(
                MetricHistory.Happiness, Comparison.GreaterThanOrEqual, 50.0,
                TriggerScope.AllDistricts)));

        /// <summary>
        /// A catalog with comfortably more than one cycle's worth of drawable events, so no test here
        /// is accidentally asserting about a degradation.
        /// </summary>
        private static List<CivicEvent> Catalog()
        {
            var catalog = new List<CivicEvent>();

            int needed = Tuning.Stories.StoriesPerCycle * Tuning.Stories.EventsPerStory;
            for (int i = 0; i < needed; i++) catalog.Add(StoryTestFixtures.Major("major-" + i.ToString("D2")));
            for (int i = 0; i < needed * 2; i++) catalog.Add(StoryTestFixtures.Minor("minor-" + i.ToString("D2")));

            return catalog;
        }

        /// <summary>
        /// A state whose pool already holds every event in <see cref="Catalog"/>, so a draft month
        /// has something to draw from without depending on the refresh having run first.
        /// </summary>
        private static PoliticalState Fresh(SimDate date)
        {
            List<CivicEvent> catalog = Catalog();
            var pool = new List<EventPoolEntry>();
            for (int i = 0; i < catalog.Count; i++) pool.Add(StoryTestFixtures.Pooled(catalog[i].Id));

            return StoryTestFixtures.State(date, pool.ToArray());
        }

        /// <summary>
        /// A story whose every slot is bought off, so it resolves <see cref="StoryOutcome.Success"/>
        /// whatever the city says. Used wherever the verdict must be unambiguous and the point of the
        /// test is elsewhere.
        /// </summary>
        private static Story WonStory(string id, SimDate opened)
        {
            return StoryTestFixtures.Story(id, opened,
                StoryTestFixtures.MetSlot("major-00", SlotRole.Major),
                StoryTestFixtures.MetSlot("minor-00"),
                StoryTestFixtures.MetSlot("minor-01"));
        }

        /// <summary>A single-slot story that scores by running its event's check.</summary>
        private static Story GoalStory(string id, SimDate opened, string eventId)
        {
            return StoryTestFixtures.Story(id, opened,
                StoryTestFixtures.Slot(eventId, SlotResponse.Goal, SlotRole.Major));
        }

        /// <summary>
        /// Runs one cycle. The phase is derived from the save start rather than passed, exactly as
        /// <c>TickPlanner</c> derives it, so no test can quietly claim a month is a draft month when
        /// the planner would say otherwise.
        /// </summary>
        private static StoryCycleResult Run(PoliticalState state, SimDate today,
                                            StoryReadContext? context = null,
                                            IReadOnlyList<CivicEvent>? catalog = null,
                                            bool? draft = null, bool? resolve = null,
                                            bool replay = false, double governingVoteShare = 0.0,
                                            EngineTuning? tuning = null)
        {
            EngineTuning t = tuning ?? Tuning;
            int phase = Start.MonthsUntil(today) % t.Stories.CycleMonths;

            state.Date = today;

            return StoryCycle.Run(new StoryCycleInput
            {
                State = state,
                Catalog = catalog ?? Catalog(),
                Context = context ?? StoryTestFixtures.Context(StoryTestFixtures.City(today)),
                SaveGuid = StoryTestFixtures.Save,
                Today = today,
                IsStoryDraft = draft ?? (phase == 0),
                IsStoryResolve = resolve ?? (phase == 1),
                IsReplay = replay,
                GoverningVoteShare = governingVoteShare,
                Tuning = t
            });
        }

        /// <summary>
        /// The non-vacuity guard. Every "nothing happened" assertion in this file is preceded by one
        /// of these, because a stub returns an empty result whatever it is handed.
        /// </summary>
        private static void AssertDrafted(StoryCycleResult result)
        {
            Assert.True(result.DraftedStories.Count > 0,
                "Nothing drafted, so every assertion that follows would hold vacuously. " +
                "StoryCycle.Run is an AGORA-SEAM stub until lane 4a lands.");
        }

        private static void AssertResolved(StoryCycleResult result)
        {
            Assert.True(result.ResolvedStories.Count > 0,
                "Nothing resolved, so every assertion that follows would hold vacuously. " +
                "StoryCycle.Run is an AGORA-SEAM stub until lane 4a lands.");
        }

        private static void AssertSortedById(IReadOnlyList<Story> stories)
        {
            for (int i = 1; i < stories.Count; i++)
            {
                Assert.True(string.CompareOrdinal(stories[i - 1].Id, stories[i].Id) <= 0,
                    "Stories are not sorted by Id ordinal: " + stories[i - 1].Id + " before " + stories[i].Id);
            }
        }

        private static List<StoryOutcome> Outcomes(IReadOnlyList<Story> stories)
        {
            var outcomes = new List<StoryOutcome>();
            for (int i = 0; i < stories.Count; i++) outcomes.Add(stories[i].Outcome);
            return outcomes;
        }
    }
}
