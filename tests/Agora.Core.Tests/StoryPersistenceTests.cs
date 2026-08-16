using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Events.Scheduler;
using Agora.Core.Stories;
using Agora.Core.Tuning;
using Agora.Mod.Persistence;
using Agora.Mod.Sensors;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The reload matrix: reload before the draft, between the draft and the resolve, and after the
    /// resolve, each proving an identical state hash.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>"Desync" has a precise definition and this file uses it.</b> <c>tests/CLAUDE.md</c>: the
    /// SHA-256 of serialized Agora state at sim date D after a reload equals the hash before it.
    /// <see cref="AgoraJson.Fingerprint"/> is that operational definition, and hashing the serialized
    /// form is what keeps the coverage honest — a hand-written field-by-field assertion silently
    /// stops covering every field added after it was written, which for a state carrying stories,
    /// pools, ledgers and a command log is most of them.
    /// </para>
    /// <para>
    /// <b>A reload here is the real one</b>, minus the file: serialize through
    /// <see cref="AgoraJson"/>, re-parse, and materialise. That is precisely what
    /// <c>SidecarStore.TryLoadState</c> does between the disk and the engine. No migration step is
    /// involved because nothing in wave 4 moves the state version — every field it writes was landed
    /// by wave 2.
    /// </para>
    /// <para>
    /// <b>Everything but <see cref="TheStoryFieldsSurviveASerializationRoundTrip"/> depends on lane
    /// 4a.</b> <see cref="StoryCycle.Run"/> is an <c>AGORA-SEAM</c> stub, so these fail here and are
    /// verified in the umbrella after 4a merges. Each one first asserts that the cycle did something,
    /// because two empty states hash identically and would agree about nothing.
    /// </para>
    /// </remarks>
    public sealed class StoryPersistenceTests
    {
        private static readonly SimDate Start = StoryTestFixtures.March1994;

        private static EngineTuning Tuning => EngineTuning.Default;

        private static int StoryLifeMonths => Tuning.Stories.CycleMonths - 1;

        // ---------------------------------------------------------------- the round trip itself

        /// <summary>
        /// Every story field survives the sidecar round trip, hash-identical.
        /// </summary>
        /// <remarks>
        /// The floor the whole matrix stands on, and the only test in this file that does not depend
        /// on lane 4a. If the serializer drops a field the reload tests below fail for a reason that
        /// has nothing to do with the cycle, and this is the test that says so first. The fixture
        /// deliberately populates every story-shaped collection on the state — live, archived, pooled,
        /// the power ledger, the command log and a story's recorded evidence — since a field nothing
        /// fills round-trips correctly by not existing.
        /// </remarks>
        [Fact]
        public void TheStoryFieldsSurviveASerializationRoundTrip()
        {
            PoliticalState state = Populated();

            Assert.Equal(Hash(state), Hash(Reload(state)));
        }

        /// <summary>
        /// Reloading twice is the same as reloading once — the round trip is idempotent.
        /// </summary>
        /// <remarks>
        /// Non-negotiable #6: load must never desync. A round trip that normalises a value on the way
        /// through would hash differently the first time and stably thereafter, which is the failure
        /// shape that looks like a one-off and is not.
        /// </remarks>
        [Fact]
        public void ReloadingTwiceIsTheSameAsReloadingOnce()
        {
            PoliticalState once = Reload(Populated());

            Assert.Equal(Hash(once), Hash(Reload(once)));
        }

        // ---------------------------------------------------------------- the reload matrix

        /// <summary>
        /// <b>Reload before the draft.</b> The player saves, quits and comes back on the month Agora
        /// is about to draft on.
        /// </summary>
        /// <remarks>
        /// The draft is a seeded draw over the pool, so this is where a reload would show up as a
        /// different set of stories rather than as a missing one — the failure that is invisible in a
        /// screenshot and obvious in a hash.
        /// </remarks>
        [Fact]
        public void ReloadBeforeTheDraftLeavesTheStateHashIdentical()
        {
            PoliticalState lived = Fresh(Start);
            PoliticalState reloaded = Reload(Fresh(Start));

            StoryCycleResult a = Run(lived, Start);
            StoryCycleResult b = Run(reloaded, Start);

            AssertDrafted(a);
            Assert.Equal(Hash(lived), Hash(reloaded));
            Assert.Equal(Hash(a.DraftedStories), Hash(b.DraftedStories));
            Assert.Equal(Hash(a.EffectRequests), Hash(b.EffectRequests));
        }

        /// <summary>
        /// <b>Reload between the draft and the resolve.</b> The story is open, the player is playing,
        /// and the save is closed and reopened before the verdict.
        /// </summary>
        [Fact]
        public void ReloadBetweenDraftAndResolveLeavesTheStateHashIdentical()
        {
            PoliticalState lived = Fresh(Start);
            AssertDrafted(Run(lived, Start));

            PoliticalState reloaded = Reload(lived);
            Assert.Equal(Hash(lived), Hash(reloaded));

            SimDate due = Start.AddMonths(StoryLifeMonths);
            StoryCycleResult a = Run(lived, due);
            StoryCycleResult b = Run(reloaded, due);

            AssertResolved(a);
            Assert.Equal(Hash(lived), Hash(reloaded));
            Assert.Equal(Hash(a.ResolvedStories), Hash(b.ResolvedStories));
            Assert.Equal(a.PowerDelta, b.PowerDelta);
        }

        /// <summary>
        /// <b>Reload after the resolve.</b> The verdict is in, and re-entering the month it landed on
        /// must not reach a second one.
        /// </summary>
        /// <remarks>
        /// The sharpest of the three: <c>Story.Outcome != Pending</c> is stamped before the effects go
        /// out, so a reload lands on a state that has already been scored. Re-entering must produce
        /// the same hash it left with — not merely the same hash as the other branch, which two
        /// equally broken branches would also satisfy.
        /// </remarks>
        [Fact]
        public void ReloadAfterTheResolveLeavesTheStateHashIdentical()
        {
            PoliticalState lived = Fresh(Start);
            AssertDrafted(Run(lived, Start));

            SimDate due = Start.AddMonths(StoryLifeMonths);
            AssertResolved(Run(lived, due));

            string settled = Hash(lived);

            PoliticalState reloaded = Reload(lived);
            Assert.Equal(settled, Hash(reloaded));

            StoryCycleResult again = Run(reloaded, due);

            Assert.Empty(again.ResolvedStories);
            Assert.Equal(0, again.PowerDelta);
            Assert.Equal(settled, Hash(reloaded));
        }

        /// <summary>
        /// <b>The ordinary mid-month reload changes nothing at all.</b>
        /// </summary>
        /// <remarks>
        /// The common case, and the one a careless idempotence guard breaks. The player saved inside
        /// the month Agora already ticked and came back to it; the state that comes out is the state
        /// that went in, byte for byte. Anything else is either a second draft, a second accrual or a
        /// story swept while it was still live.
        /// </remarks>
        [Fact]
        public void AnOrdinaryMidMonthReloadChangesNothingAtAll()
        {
            PoliticalState lived = Fresh(Start);
            AssertDrafted(Run(lived, Start, governingVoteShare: FullShare));

            string settled = Hash(lived);

            PoliticalState reloaded = Reload(lived);
            StoryCycleResult again = Run(reloaded, Start, governingVoteShare: FullShare);

            Assert.Empty(again.DraftedStories);
            Assert.Empty(again.ResolvedStories);
            Assert.Equal(0, again.PowerDelta);
            Assert.Equal(settled, Hash(reloaded));
        }

        /// <summary>
        /// A reload across the whole two-month cycle — draft, reload, resolve, reload — arrives at the
        /// same state as a save that was never closed.
        /// </summary>
        /// <remarks>
        /// The three cases above are each one boundary. This is all of them in sequence, which is what
        /// a player who saves habitually actually does, and it is the arrangement in which a guard
        /// that is correct at each boundary and wrong in combination shows up.
        /// </remarks>
        [Fact]
        public void AReloadAtEveryBoundaryArrivesWhereAnUninterruptedRunDoes()
        {
            SimDate due = Start.AddMonths(StoryLifeMonths);

            PoliticalState uninterrupted = Fresh(Start);
            AssertDrafted(Run(uninterrupted, Start, governingVoteShare: FullShare));
            AssertResolved(Run(uninterrupted, due, governingVoteShare: FullShare));

            PoliticalState interrupted = Fresh(Start);
            Run(interrupted, Start, governingVoteShare: FullShare);
            interrupted = Reload(interrupted);
            Run(interrupted, Start, governingVoteShare: FullShare);   // the reloaded month, re-entered
            interrupted = Reload(interrupted);
            Run(interrupted, due, governingVoteShare: FullShare);
            interrupted = Reload(interrupted);
            Run(interrupted, due, governingVoteShare: FullShare);     // and again on the resolve month

            Assert.Equal(Hash(uninterrupted), Hash(interrupted));
        }

        // ---------------------------------------------------------------- replay and the sweep

        /// <summary>
        /// A replayed month leaves the state hash untouched — no stories and no power.
        /// </summary>
        /// <remarks>
        /// The control run is what stops this passing against a stub: the same month lived produces a
        /// different hash, so "unchanged" is a claim about replay rather than about an engine that
        /// does nothing.
        /// </remarks>
        [Fact]
        public void AReplayedMonthLeavesTheStateHashUntouched()
        {
            PoliticalState control = Fresh(Start);
            string before = Hash(control);
            AssertDrafted(Run(control, Start, governingVoteShare: FullShare));
            Assert.NotEqual(before, Hash(control));

            PoliticalState replayed = Fresh(Start);
            string unlived = Hash(replayed);

            Run(replayed, Start, replay: true, governingVoteShare: FullShare);

            Assert.Equal(unlived, Hash(replayed));
        }

        /// <summary>
        /// The stranded-story sweep reaches the same state whether or not the save was reloaded first.
        /// </summary>
        /// <remarks>
        /// Catch-up truncation is itself a reload path — <c>TickPlanner.CatchUpDates</c> drops the
        /// oldest months of a long gap, which is how a story's due month goes missing — so the sweep
        /// that cleans up after it has to be hash-stable across the reload that caused it.
        /// </remarks>
        [Fact]
        public void TheStrandedSweepIsHashIdenticalAcrossAReload()
        {
            SimDate due = Start.AddMonths(StoryLifeMonths);
            SimDate afterTheGap = due.AddMonths(Tuning.Scheduler.CatchUpMaxMonths + 1);

            PoliticalState lived = WithAStrandedStory();
            PoliticalState reloaded = Reload(WithAStrandedStory());

            StoryCycleResult a = Run(lived, afterTheGap);
            StoryCycleResult b = Run(reloaded, afterTheGap);

            Assert.NotEmpty(a.ResolvedStories);
            Assert.Equal(Hash(lived), Hash(reloaded));
            Assert.Equal(Hash(a.ResolvedStories), Hash(b.ResolvedStories));
        }

        /// <summary>
        /// A story resolved from its recorded evidence resolves to the same verdict after a reload,
        /// even though the city it is being read against has moved.
        /// </summary>
        /// <remarks>
        /// The determinism argument for <c>Resolve now</c> in one test. The command's firing time is
        /// exogenous, so the sample it took is written into <see cref="Story.ResolutionEvidence"/>;
        /// the reloaded branch is handed a visibly different city and must still reach the recorded
        /// verdict. A cycle that re-measured would agree with itself on the lived branch and disagree
        /// here, which is exactly the failure the recorded evidence exists to prevent.
        /// </remarks>
        [Fact]
        public void AnEarlyResolveReachesTheSameVerdictAgainstADifferentCity()
        {
            SimDate due = Start.AddMonths(StoryLifeMonths);
            var catalog = new List<CivicEvent> { HappinessEvent };

            PoliticalState lived = WithAnEarlyResolveRequest();
            PoliticalState reloaded = Reload(WithAnEarlyResolveRequest());

            StoryCycleResult a = Run(lived, due, catalog: catalog,
                                     context: CityWithHappiness(due, 5.0));
            StoryCycleResult b = Run(reloaded, due, catalog: catalog,
                                     context: CityWithHappiness(due, 95.0));

            Assert.NotEmpty(a.ResolvedStories);
            Assert.Equal(Hash(a.ResolvedStories), Hash(b.ResolvedStories));
            Assert.Equal(StoryOutcome.Success, a.ResolvedStories[0].Outcome);
        }

        // ---------------------------------------------------------------- fixtures

        /// <summary>
        /// A governing share of 1 accrues the monthly ceiling. Named rather than written twice, and
        /// never used as a number — the ceiling itself is read from tuning wherever it matters.
        /// </summary>
        private const double FullShare = 1.0;

        private static readonly CivicEvent HappinessEvent = StoryTestFixtures.Major(
            "event-happiness",
            StoryTestFixtures.Check(StoryTestFixtures.Metric(
                MetricHistory.Happiness, Comparison.GreaterThanOrEqual, 50.0)));

        /// <summary>
        /// The reload, without the file. <c>SidecarStore</c> parses to a DOM, migrates, and
        /// materialises; nothing in wave 4 moves the state version, so the migration step is a no-op
        /// and is left to <c>SidecarMigrationTests</c>, which owns it.
        /// </summary>
        private static PoliticalState Reload(PoliticalState state)
        {
            return AgoraJson.ToObject<PoliticalState>(AgoraJson.ParseObject(AgoraJson.Serialize(state)));
        }

        /// <summary>SHA-256 of the serialized form — the operational definition of desync.</summary>
        private static string Hash(object value) => AgoraJson.Fingerprint(value);

        private static List<CivicEvent> Catalog()
        {
            var catalog = new List<CivicEvent>();

            int needed = Tuning.Stories.StoriesPerCycle * Tuning.Stories.EventsPerStory;
            for (int i = 0; i < needed; i++) catalog.Add(StoryTestFixtures.Major("major-" + i.ToString("D2")));
            for (int i = 0; i < needed * 2; i++) catalog.Add(StoryTestFixtures.Minor("minor-" + i.ToString("D2")));

            return catalog;
        }

        private static PoliticalState Fresh(SimDate date)
        {
            List<CivicEvent> catalog = Catalog();
            var pool = new List<EventPoolEntry>();
            for (int i = 0; i < catalog.Count; i++) pool.Add(StoryTestFixtures.Pooled(catalog[i].Id));

            return StoryTestFixtures.State(date, pool.ToArray());
        }

        /// <summary>
        /// A state with something in every story-shaped collection, so the round-trip test is asserting
        /// about fields that are actually populated.
        /// </summary>
        private static PoliticalState Populated()
        {
            PoliticalState state = Fresh(Start);

            Story live = StoryTestFixtures.Story("story-live", Start,
                StoryTestFixtures.Slot("major-00", SlotResponse.Goal, SlotRole.Major, baseline: 42.0),
                StoryTestFixtures.Slot("minor-00", SlotResponse.Manual, manualDeclared: true),
                StoryTestFixtures.SilentSlot("minor-01"));
            live.HeadlineFallback = "A headline";
            live.FlavorKey = "story-live.open";
            live.ResolutionFlavorKey = "story-live.resolution";
            live.ResolveEarlyRequested = true;
            live.ResolutionEvidence.Add(StoryTestFixtures.Reading(MetricHistory.Happiness, 61.5));
            live.ResolutionEvidence.Add(
                StoryTestFixtures.Reading(MetricHistory.Happiness, null, "district-01"));
            state.LiveStories.Add(live);

            Story archived = StoryTestFixtures.Story("story-archived", Start.AddMonths(-4),
                StoryTestFixtures.MetSlot("major-01", SlotRole.Major));
            archived.Outcome = StoryOutcome.Success;
            archived.ResolvedMonth = Start.AddMonths(-4 + StoryLifeMonths).TotalMonths;
            state.StoryArchive.Add(archived);

            state.Power.Balance = 37;
            state.Power.LifetimeEarned = 120;
            state.Power.LifetimeSpent = 83;
            state.Power.LastAccrualMonth = Start.TotalMonths;
            state.Power.Ledger.Add(new PowerLedgerEntry
            {
                Month = Start.TotalMonths,
                Sequence = 0,
                Reason = PowerLedgerReason.Accrual,
                Delta = 5
            });
            state.Power.Ledger.Add(new PowerLedgerEntry
            {
                Month = Start.TotalMonths,
                Sequence = 1,
                Reason = PowerLedgerReason.SuccessAward,
                Delta = 20,
                StoryId = "story-archived",
                EventId = "major-01"
            });

            // Through the log's own helper rather than appended by hand: Append is what assigns
            // Sequence and inserts in sort position, and a fixture that stamped its own would be
            // round-tripping a log the engine could not have produced.
            PlayerCommandLog.Append(state.PlayerCommands, new PlayerCommand
            {
                StoryId = "story-live",
                EventId = "minor-00",
                Kind = PlayerCommandKind.DeclareManualOutcome,
                DeclaredMet = true,
                FreeText = "We opened the depot early.",
                DecidedMonth = Start.TotalMonths
            });

            state.LastStoryDraftMonth = Start.TotalMonths;
            state.LastStoryResolveMonth = Start.AddMonths(-1).TotalMonths;

            return state;
        }

        /// <summary>A story whose due month the scheduler skipped, and which nothing has reaped yet.</summary>
        private static PoliticalState WithAStrandedStory()
        {
            PoliticalState state = StoryTestFixtures.State(Start);

            state.LiveStories.Add(StoryTestFixtures.Story("story-stranded", Start,
                StoryTestFixtures.MetSlot("major-00", SlotRole.Major),
                StoryTestFixtures.MetSlot("minor-00"),
                StoryTestFixtures.MetSlot("minor-01")));

            return state;
        }

        /// <summary>A story the player asked to resolve now, with the sample that request took.</summary>
        private static PoliticalState WithAnEarlyResolveRequest()
        {
            PoliticalState state = StoryTestFixtures.State(Start);

            Story story = StoryTestFixtures.Story("story-early", Start,
                StoryTestFixtures.Slot(HappinessEvent.Id, SlotResponse.Goal, SlotRole.Major));
            story.ResolveEarlyRequested = true;
            story.ResolutionEvidence.Add(StoryTestFixtures.Reading(MetricHistory.Happiness, 90.0));

            state.LiveStories.Add(story);
            return state;
        }

        private static StoryReadContext CityWithHappiness(SimDate date, double happiness) =>
            StoryTestFixtures.Context(StoryTestFixtures.City(date, happiness: happiness));

        /// <summary>
        /// Runs one cycle, with the two phase flags taken from <see cref="TickPlanner.Plan"/> rather
        /// than recomputed here — the planner is the authority on when a cycle is due.
        /// </summary>
        private static StoryCycleResult Run(PoliticalState state, SimDate today,
                                            StoryReadContext? context = null,
                                            IReadOnlyList<CivicEvent>? catalog = null,
                                            bool replay = false, double governingVoteShare = 0.0)
        {
            TickPlan plan = TickPlanner.Plan(Start, today, new AgoraSettings(), null, false, false, Tuning);

            state.Date = today;

            return StoryCycle.Run(new StoryCycleInput
            {
                State = state,
                Catalog = catalog ?? Catalog(),
                Context = context ?? StoryTestFixtures.Context(StoryTestFixtures.City(today)),
                SaveGuid = StoryTestFixtures.Save,
                Today = today,
                IsStoryDraft = plan.IsStoryDraft,
                IsStoryResolve = plan.IsStoryResolve,
                IsReplay = replay,
                GoverningVoteShare = governingVoteShare,
                Tuning = Tuning
            });
        }

        private static void AssertDrafted(StoryCycleResult result)
        {
            Assert.True(result.DraftedStories.Count > 0,
                "Nothing drafted, so the two hashes would agree about an empty state. " +
                "StoryCycle.Run is an AGORA-SEAM stub until lane 4a lands.");
        }

        private static void AssertResolved(StoryCycleResult result)
        {
            Assert.True(result.ResolvedStories.Count > 0,
                "Nothing resolved, so the two hashes would agree about an empty state. " +
                "StoryCycle.Run is an AGORA-SEAM stub until lane 4a lands.");
        }
    }
}
