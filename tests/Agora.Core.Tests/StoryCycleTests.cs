using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Events.Scheduler;
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
        /// <para>
        /// The second half of the test is the control, and it is what stops the first half asserting
        /// nothing: the same story on the same state <i>does</i> resolve once its due month arrives,
        /// so "not touched" is a claim about the month rather than about a cycle that never touches
        /// anything.
        /// </para>
        /// <para>
        /// <b>Every assertion names <c>story-due-later</c>.</b> The months this runs on are draft
        /// phases with a populated catalog, so the cycle legitimately opens stories of its own on the
        /// same ticks — asserting that the live list holds exactly one entry would fail on those and
        /// say nothing whatever about the story under test.
        /// </para>
        /// </remarks>
        [Fact]
        public void AStoryDueLaterIsNotTouched()
        {
            PoliticalState state = StoryTestFixtures.State(Start);
            state.LiveStories.Add(WonStory("story-due-later", Start));

            StoryCycleResult early = Run(state, Start, catalog: Catalog());

            Assert.DoesNotContain(early.ResolvedStories, s => s.Id == "story-due-later");
            Story live = Only(state.LiveStories, "story-due-later");
            Assert.Equal(StoryOutcome.Pending, live.Outcome);

            StoryCycleResult onTime = Run(state, Start.AddMonths(StoryLifeMonths), catalog: Catalog());

            Assert.Contains(onTime.ResolvedStories, s => s.Id == "story-due-later");
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
        /// <para>
        /// The suspension is of the whole cycle, not of drafting alone. Abandoning a story during
        /// replay would be the engine deciding, inside a window the player never lived, that they
        /// never got to answer it — and the sweep on the lived month reaches the same verdict anyway.
        /// </para>
        /// <para>
        /// The lived run drafts stories of its own, which is correct, so the closing assertion names
        /// <c>story-stranded</c> rather than asking whether the live list is empty.
        /// </para>
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
            Assert.Contains(state.LiveStories, s => s.Id == "story-stranded");

            StoryCycleResult lived = Run(state, afterTheGap, catalog: Catalog());

            Assert.Contains(lived.ResolvedStories, s => s.Id == "story-stranded");
            Assert.DoesNotContain(state.LiveStories, s => s.Id == "story-stranded");
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
        /// <para>
        /// The idempotence guard has to survive the one path that reaches a verdict off-phase. Without
        /// it the ordinary resolve pass finds a story whose <c>ResolvesDate</c> is today and scores it
        /// again — this time against the live city, which is the very measurement the recorded
        /// evidence exists to avoid.
        /// </para>
        /// <para>
        /// <b>The due month is not an empty month.</b> A story lives <c>cycleMonths - 1</c> months,
        /// which is one, so anything the first run drafted at <c>Start</c> is genuinely due on the
        /// month this re-runs — and resolving it is correct. Both assertions therefore name
        /// <c>story-early</c>; asserting the resolved list was empty would fail on the draft and
        /// would have said nothing about the story under test.
        /// </para>
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
            Assert.Equal(1, Count(Run(state, Start, catalog: catalog).ResolvedStories, "story-early"));

            SimDate due = Start.AddMonths(StoryLifeMonths);
            StoryCycleResult onTheDueMonth = Run(state, due, catalog: catalog);

            Assert.DoesNotContain(onTheDueMonth.ResolvedStories, s => s.Id == "story-early");
            Assert.Equal(1, Count(state.StoryArchive, "story-early"));
        }

        // ---------------------------------------------------------------- double reachability

        /// <summary>
        /// <b>A story two passes can both reach resolves exactly once, and is paid exactly once.</b>
        /// </summary>
        /// <remarks>
        /// <para>
        /// The reachable case is a story that is stranded — its due month went past under a catch-up
        /// truncation — and <i>also</i> carries <see cref="Story.ResolveEarlyRequested"/>, so the
        /// sweep and the early-resolve path both have a claim on it on one tick. "Non-empty" is not
        /// the assertion here and would pass while the story resolved twice; the count is.
        /// </para>
        /// <para>
        /// <b>The subject carries <see cref="Story.ResolutionEvidence"/>, and that is what gives this
        /// test teeth.</b> A stranded story with no evidence is <see cref="StoryOutcome.Abandoned"/>
        /// and pays nothing, so a double payment could not show up in it — the test would pass on two
        /// zeroes. With evidence the sweep scores it for real, which is the only arrangement where
        /// paying twice is possible and therefore the only one where "paid once" is a claim.
        /// </para>
        /// <para>
        /// Every count is scoped to <c>story-contested</c>. Both runs land on months the cycle
        /// legitimately drafts on, so a whole-list count would be measuring the draft.
        /// </para>
        /// </remarks>
        [Fact]
        public void AStoryBothPassesReachResolvesExactlyOnce()
        {
            SimDate due = Start.AddMonths(StoryLifeMonths);
            SimDate afterTheGap = due.AddMonths(Tuning.Scheduler.CatchUpMaxMonths + 1);

            // Control: one pass only — due today, no early request.
            PoliticalState control = StoryTestFixtures.State(Start);
            control.LiveStories.Add(WonStory("story-contested", Start));
            StoryCycleResult once = Run(control, due, catalog: Catalog());

            Assert.Equal(1, Count(once.ResolvedStories, "story-contested"));
            int paidOnce = LedgerEntriesFor(control.Power, "story-contested");
            int effectsOnce = EffectRequestsFor(once, "story-contested");

            // Subject: stranded, asked to resolve early, and carrying the reading that lets the sweep
            // reach a real verdict rather than abandoning it.
            PoliticalState state = StoryTestFixtures.State(Start);
            Story contested = WonStory("story-contested", Start);
            contested.ResolveEarlyRequested = true;
            contested.ResolutionEvidence.Add(StoryTestFixtures.Reading(MetricHistory.Happiness, 90.0));
            state.LiveStories.Add(contested);

            StoryCycleResult twice = Run(state, afterTheGap, catalog: Catalog());

            Assert.Equal(1, Count(twice.ResolvedStories, "story-contested"));
            Assert.Equal(1, Count(state.StoryArchive, "story-contested"));
            Assert.DoesNotContain(state.LiveStories, s => s.Id == "story-contested");

            // One resolution's worth of consequence, not two. The comparison is against the control
            // rather than against a written-down number, so a balance pass moves both together.
            Assert.Equal(paidOnce, LedgerEntriesFor(state.Power, "story-contested"));
            Assert.Equal(effectsOnce, EffectRequestsFor(twice, "story-contested"));
            Assert.Equal(1, Count(twice.Pressures, "story-contested"));
        }

        /// <summary>
        /// <b>A stranded story with no evidence, reached by both passes, is
        /// <see cref="StoryOutcome.Abandoned"/> and pays nothing.</b>
        /// </summary>
        /// <remarks>
        /// <para>
        /// The other half of the sweep's rule, and it is a ruling rather than an accident: the due
        /// month has gone and the story carries no reading, so scoring it now would measure a verdict
        /// against a city that has moved on by the whole catch-up gap — the hazard replay suspension
        /// exists to prevent, where a check evaluates 2005's crime wave against 2031's crime rate.
        /// Abandoned pays nothing in either direction, because charging for months the scheduler
        /// declined to run would be the engine billing the player for its own truncation.
        /// </para>
        /// <para>
        /// <b>An earlier version of this test asserted the ledger equalled an on-time resolution's,
        /// and that was wrong.</b> It conflated "not paid twice" with "paid exactly as if it had
        /// resolved on time", which are different claims and only the first is the property the test
        /// is named for. What is asserted now is the named property and its sharp form: no payment at
        /// all here, and in no arrangement twice a single one.
        /// </para>
        /// <para>
        /// <see cref="Story.ResolveEarlyRequested"/> does not change the verdict. The button says
        /// "score this now", not "score this on evidence that was never taken" — nothing the player
        /// can press makes an unrecorded month readable.
        /// </para>
        /// </remarks>
        [Fact]
        public void AStrandedStoryWithNoEvidenceIsAbandonedEvenWhenAskedToResolveEarly()
        {
            SimDate due = Start.AddMonths(StoryLifeMonths);
            SimDate afterTheGap = due.AddMonths(Tuning.Scheduler.CatchUpMaxMonths + 1);

            // What one on-time resolution of this story costs, so "twice" below is derived.
            PoliticalState control = StoryTestFixtures.State(Start);
            control.LiveStories.Add(WonStory("story-contested", Start));
            AssertResolved(Run(control, due, catalog: Catalog()));

            int paidOnce = LedgerEntriesFor(control.Power, "story-contested");
            Assert.True(paidOnce > 0,
                "An on-time Success must move the balance, or the bound below would be zero.");

            PoliticalState state = StoryTestFixtures.State(Start);
            Story contested = WonStory("story-contested", Start);
            contested.ResolveEarlyRequested = true;   // and no ResolutionEvidence: nothing was recorded
            state.LiveStories.Add(contested);
            state.Power.Balance = 100;

            StoryCycleResult swept = Run(state, afterTheGap, catalog: Catalog(),
                                         governingVoteShare: 0.0);

            Story reaped = Only(swept.ResolvedStories, "story-contested");
            Assert.Equal(StoryOutcome.Abandoned, reaped.Outcome);

            // Pays nothing in either direction, and in particular never twice a real resolution.
            int paid = LedgerEntriesFor(state.Power, "story-contested");
            Assert.Equal(0, paid);
            Assert.True(paid < 2 * paidOnce, "The story was paid for twice.");
            Assert.Equal(100, state.Power.Balance);
        }

        // ---------------------------------------------------------------- housekeeping

        /// <summary>
        /// <b>Exactly at <c>stories.archiveRetention</c>, nothing is evicted.</b>
        /// </summary>
        /// <remarks>
        /// The boundary case, and the reason the three tests below exist separately rather than as one
        /// "the archive stays bounded" assertion: a trim that stays under the bound proves nothing,
        /// and an off-by-one that evicts at the bound rather than past it silently loses the player's
        /// oldest story on a save that never exceeded the limit. Retention is read from tuning and the
        /// archive is filled to one short of it, so the story retiring on this tick lands exactly on
        /// the boundary.
        /// </remarks>
        [Fact]
        public void AtExactlyTheRetentionBoundNothingIsEvicted()
        {
            int retention = Tuning.Stories.ArchiveRetention;

            PoliticalState state = ArchiveOf(retention - 1);
            state.LiveStories.Add(WonStory("story-due-now", Start));

            SimDate due = Start.AddMonths(StoryLifeMonths);
            AssertResolved(Run(state, due, catalog: Catalog()));

            Assert.Equal(retention, state.StoryArchive.Count);
            Assert.Contains(state.StoryArchive, s => s.Id == "story-due-now");
            Assert.Contains(state.StoryArchive, s => s.Id == OldestArchivedId);
        }

        /// <summary>
        /// One over the bound evicts exactly one, and it is the oldest by the documented sort key.
        /// </summary>
        /// <remarks>
        /// The key is <c>(ResolvedMonth descending, Id ordinal)</c>, so what goes is the entry that
        /// sorts last — the oldest resolution, and among equals the highest id. The fixture resolves
        /// each pre-loaded story a month apart precisely so that "oldest" is a fact about the data
        /// rather than about the order it was appended in.
        /// </remarks>
        [Fact]
        public void OneOverTheRetentionBoundEvictsExactlyTheOldest()
        {
            int retention = Tuning.Stories.ArchiveRetention;

            PoliticalState state = ArchiveOf(retention);
            state.LiveStories.Add(WonStory("story-due-now", Start));

            SimDate due = Start.AddMonths(StoryLifeMonths);
            AssertResolved(Run(state, due, catalog: Catalog()));

            Assert.Equal(retention, state.StoryArchive.Count);
            Assert.Contains(state.StoryArchive, s => s.Id == "story-due-now");
            Assert.DoesNotContain(state.StoryArchive, s => s.Id == OldestArchivedId);
        }

        /// <summary>
        /// A retention of zero or less is <b>unbounded</b>, not "keep nothing".
        /// </summary>
        /// <remarks>
        /// Only reachable from a hand-edited tuning file — the schema pins the key as a positive
        /// integer — which is exactly why it is worth pinning: the two readings of a non-positive
        /// bound differ by the whole archive, and the destructive one is the one a naive
        /// <c>while (count &gt; retention) RemoveLast()</c> implements. Nothing may depend on the
        /// bound for correctness anyway, so keeping everything is the safe reading.
        /// </remarks>
        [Fact]
        public void ARetentionOfZeroKeepsEverything()
        {
            EngineTuning unbounded = StoryTestFixtures.Tuned("{\"stories\":{\"archiveRetention\":0}}");
            Assert.True(unbounded.Stories.ArchiveRetention <= 0,
                "The overlay did not take, so this test would assert about the shipped bound instead.");

            PoliticalState state = ArchiveOf(4);
            state.LiveStories.Add(WonStory("story-due-now", Start));

            SimDate due = Start.AddMonths(StoryLifeMonths);
            AssertResolved(Run(state, due, catalog: Catalog(), tuning: unbounded));

            Assert.Equal(5, state.StoryArchive.Count);
            Assert.Contains(state.StoryArchive, s => s.Id == OldestArchivedId);
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

        /// <summary>
        /// The id of the entry <see cref="ArchiveOf"/> resolves first, and so the one the documented
        /// sort key <c>(ResolvedMonth descending, Id ordinal)</c> puts last.
        /// </summary>
        private const string OldestArchivedId = "story-old-000";

        /// <summary>
        /// A state whose archive already holds <paramref name="count"/> resolved stories, each a month
        /// older than the next.
        /// </summary>
        /// <remarks>
        /// The months are spread rather than shared so that "the oldest" is a property of the data and
        /// not of the order the fixture appended them in — a trim that happened to drop the first
        /// element would otherwise look correct.
        /// </remarks>
        private static PoliticalState ArchiveOf(int count)
        {
            PoliticalState state = StoryTestFixtures.State(Start);

            for (int i = 0; i < count; i++)
            {
                Story old = WonStory("story-old-" + i.ToString("D3"), Start.AddMonths(-count - 1 + i));
                old.Outcome = StoryOutcome.Success;
                old.ResolvedMonth = Start.AddMonths(-count + i).TotalMonths;
                state.StoryArchive.Add(old);
            }

            return state;
        }

        /// <summary>
        /// How many entries carry this id.
        /// </summary>
        /// <remarks>
        /// <b>Every count in this file is scoped to one story, and that is not fussiness.</b> Most of
        /// these fixtures run on months that are draft phases with a populated catalog, so the cycle
        /// legitimately opens stories of its own on the same tick it resolves the one under test.
        /// <c>Assert.Single</c> over a whole list would then fail on the draft — red for a reason
        /// unrelated to what the test guards, which is the defect family this wave keeps finding.
        /// </remarks>
        private static int Count(IReadOnlyList<Story> stories, string id)
        {
            int found = 0;
            for (int i = 0; i < stories.Count; i++)
            {
                if (string.Equals(stories[i].Id, id, StringComparison.Ordinal)) found++;
            }

            return found;
        }

        private static int Count(IReadOnlyList<StoryPressureContribution> pressures, string id)
        {
            int found = 0;
            for (int i = 0; i < pressures.Count; i++)
            {
                if (string.Equals(pressures[i].StoryId, id, StringComparison.Ordinal)) found++;
            }

            return found;
        }

        /// <summary>
        /// The one entry carrying this id, failing with the count when there is not exactly one.
        /// </summary>
        private static Story Only(IReadOnlyList<Story> stories, string id)
        {
            Story? found = null;
            int seen = 0;

            for (int i = 0; i < stories.Count; i++)
            {
                if (!string.Equals(stories[i].Id, id, StringComparison.Ordinal)) continue;
                found = stories[i];
                seen++;
            }

            Assert.True(seen == 1, "Expected exactly one " + id + ", found " + seen + ".");
            return found!;
        }

        /// <summary>
        /// Effect requests this story asked for. <c>StoryEffects</c> stamps
        /// <see cref="EffectRequest.SourceId"/> as <c>storyId/eventId</c>, so the story is the prefix.
        /// </summary>
        private static int EffectRequestsFor(StoryCycleResult result, string storyId)
        {
            string prefix = storyId + "/";
            int found = 0;

            for (int i = 0; i < result.EffectRequests.Count; i++)
            {
                string source = result.EffectRequests[i].SourceId ?? "";
                if (source.StartsWith(prefix, StringComparison.Ordinal)) found++;
            }

            return found;
        }

        private static int LedgerEntriesFor(PoliticalPowerState power, string storyId)
        {
            int found = 0;
            for (int i = 0; i < power.Ledger.Count; i++)
            {
                if (string.Equals(power.Ledger[i].StoryId, storyId, StringComparison.Ordinal)) found++;
            }

            return found;
        }

        /// <summary>A single-slot story that scores by running its event's check.</summary>
        private static Story GoalStory(string id, SimDate opened, string eventId)
        {
            return StoryTestFixtures.Story(id, opened,
                StoryTestFixtures.Slot(eventId, SlotResponse.Goal, SlotRole.Major));
        }

        /// <summary>
        /// Runs one cycle, with the two phase flags taken from <see cref="TickPlanner.Plan"/> rather
        /// than recomputed here.
        /// </summary>
        /// <remarks>
        /// <b>Asked, not restated.</b> An earlier version of this helper wrote the phase arithmetic
        /// out a second time, as <c>phase == 0</c> and <c>phase == 1</c> — which was correct at the
        /// shipped cadence of two and wrong at every wider one, and would have gone on agreeing with
        /// itself while the planner moved underneath it. The planner is the authority on when a cycle
        /// is due; this file is the authority on what it does when it is.
        /// </remarks>
        private static StoryCycleResult Run(PoliticalState state, SimDate today,
                                            StoryReadContext? context = null,
                                            IReadOnlyList<CivicEvent>? catalog = null,
                                            bool? draft = null, bool? resolve = null,
                                            bool replay = false, double governingVoteShare = 0.0,
                                            EngineTuning? tuning = null)
        {
            EngineTuning t = tuning ?? Tuning;
            TickPlan plan = TickPlanner.Plan(Start, today, new AgoraSettings(), null, false, false, t);

            state.Date = today;

            return StoryCycle.Run(new StoryCycleInput
            {
                State = state,
                Catalog = catalog ?? Catalog(),
                Context = context ?? StoryTestFixtures.Context(StoryTestFixtures.City(today)),
                SaveGuid = StoryTestFixtures.Save,
                Today = today,
                IsStoryDraft = draft ?? plan.IsStoryDraft,
                IsStoryResolve = resolve ?? plan.IsStoryResolve,
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
