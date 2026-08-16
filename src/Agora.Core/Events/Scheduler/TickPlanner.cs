using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Events.Scheduler
{
    /// <summary>
    /// What runs on one sim date. A pure projection of the <c>scheduler</c> tuning section onto a
    /// date, with no state of its own — the caller owns the clock (non-negotiable #8) and simply asks
    /// "what is due today?".
    /// </summary>
    public readonly struct TickPlan
    {
        /// <summary>The date this plan describes.</summary>
        public SimDate Date { get; }

        /// <summary>The engine's own cadence (<c>tickIntervalMonths</c>). False means do nothing.</summary>
        public bool IsEngineTick { get; }

        /// <summary>Scan the timeline for events to fire (<c>eventScanIntervalMonths</c>).</summary>
        public bool IsEventScan { get; }

        /// <summary>Write a sidecar snapshot (<c>snapshotIntervalMonths</c>).</summary>
        public bool IsSnapshot { get; }

        /// <summary>Run party and faction lifecycle (<c>lifecycleTickMonths</c>).</summary>
        public bool IsLifecycle { get; }

        /// <summary>Recompute derived indices (<c>indicesTickMonths</c>).</summary>
        public bool IsIndices { get; }

        /// <summary>Re-measure live mandates (<c>mandateMonitorIntervalMonths</c>).</summary>
        public bool IsMandateMonitor { get; }

        /// <summary>
        /// Publish a poll (<c>pollTickIntervalMonths</c>). Deliberately <b>not</b> gated on
        /// <see cref="IsCampaignSeason"/> — polling packets that only publish during a campaign
        /// should AND the two themselves, rather than have that policy baked in here.
        /// </summary>
        /// <remarks>
        /// Months, not days, and there is no day cadence to be had: CS2 ships
        /// <c>TimeSettingsData.m_DaysPerYear = 12</c>, so one in-game day is one calendar month and
        /// <c>SimClockMath.ToSimDate</c> returns a <see cref="SimDate"/> whose <c>Day</c> is a literal
        /// <c>1</c>. The old <c>pollTickIntervalDays</c> was read as <c>((date.Day - 1) % days) == 0</c>,
        /// i.e. <c>0 % days == 0</c> — true on every date, for every setting. The dial was inert, not
        /// wrong, and it is now a month count because a month is the only unit this calendar has.
        /// </remarks>
        public bool IsPollTick { get; }

        /// <summary>
        /// Draw this cycle's stories (<c>stories.cycleMonths</c> phase 0).
        /// </summary>
        /// <remarks>
        /// <para>
        /// A phase of the same elapsed-months arithmetic as every other cadence in this file, anchored
        /// on the save's start date — <b>no new tick, no new clock, and nothing added to
        /// <c>SeedStreams</c>' inputs</b>. The design document's "halfway through the month" rule is
        /// not buildable: CS2 ships <c>TimeSettingsData.m_DaysPerYear = 12</c>, so one in-game day is
        /// one calendar month and <c>SimDate.Day</c> is a literal <c>1</c>. See "Why not half a month"
        /// in <c>docs/plans/0004-event-system-rework.md</c>.
        /// </para>
        /// <para>
        /// Gated on <see cref="IsEngineTick"/> like every other cadence: a story drawn on a month the
        /// engine did not advance would be scored against readings nothing had recomputed.
        /// </para>
        /// </remarks>
        public bool IsStoryDraft { get; }

        /// <summary>
        /// Score the stories drawn this cycle — the <b>last</b> month of it,
        /// <c>stories.cycleMonths - 1</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>It has to be the month the stories are actually due, and
        /// <c>StoryAssembler.NewStory</c> is what decides that</b>: it sets
        /// <c>ResolvesDate = OpenedDate + (cycleMonths - 1)</c>. A story drafted on phase 0 is
        /// therefore due on phase <c>cycleMonths - 1</c>, and the cycle reads draft → live → verdict →
        /// next draft.
        /// </para>
        /// <para>
        /// <b>This shipped as a literal phase 1 in the wave-4 spine and was wrong</b>, though only
        /// latently: at the shipped cadence of 2 the two expressions are the same number, which is
        /// exactly why it survived being written down twice and tested once. At any wider cadence the
        /// verdict phase arrived before the due month, so no story was ever resolved on time — every
        /// one of them fell through to the stranded sweep a month later and was abandoned, paying
        /// nothing in either direction and silently discarding the player's work. Found by lane 4a
        /// reading this against the assembler rather than trusting it.
        /// </para>
        /// <para>
        /// <b>A story still lives one month at the shipped cadence, and that is a separate fact.</b>
        /// The window a player can influence is <c>cycleMonths - 1</c> months long, which is ONE, not
        /// two — reading the cadence as the window is the wave-3 defect that cost roughly forty
        /// re-derived thresholds. The two statements agree: the window is the span, and the verdict
        /// lands at the end of it.
        /// </para>
        /// </remarks>
        public bool IsStoryResolve { get; }

        /// <summary>Enough metric history has accumulated to schedule an election (<c>warmupMonths</c>).</summary>
        public bool IsWarmupComplete { get; }

        /// <summary>Within <c>campaignStartMonthsBeforeElection</c> of the next election.</summary>
        public bool IsCampaignSeason { get; }

        /// <summary>
        /// Why the LLM should wake, as flags. <see cref="LlmWakeCadence.None"/> means it should not.
        /// Every reason is gated twice: the per-save <see cref="AgoraSettings.WakeCadence"/> and the
        /// tuning switch must both allow it.
        /// </summary>
        public LlmWakeCadence LlmWake { get; }

        internal TickPlan(SimDate date, bool isEngineTick, bool isEventScan, bool isSnapshot,
                          bool isLifecycle, bool isIndices, bool isMandateMonitor, bool isPollTick,
                          bool isStoryDraft, bool isStoryResolve,
                          bool isWarmupComplete, bool isCampaignSeason, LlmWakeCadence llmWake)
        {
            Date = date;
            IsEngineTick = isEngineTick;
            IsEventScan = isEventScan;
            IsSnapshot = isSnapshot;
            IsLifecycle = isLifecycle;
            IsIndices = isIndices;
            IsMandateMonitor = isMandateMonitor;
            IsPollTick = isPollTick;
            IsStoryDraft = isStoryDraft;
            IsStoryResolve = isStoryResolve;
            IsWarmupComplete = isWarmupComplete;
            IsCampaignSeason = isCampaignSeason;
            LlmWake = llmWake;
        }

        /// <summary>
        /// True when any subsystem is due. Cheap early-out for the caller.
        /// </summary>
        /// <remarks>
        /// <see cref="IsStoryDraft"/> and <see cref="IsStoryResolve"/> are not listed because both
        /// are already gated on <see cref="IsEngineTick"/>, which is. Naming them as well would imply
        /// they can be true independently, and the day one of them can, this expression is where that
        /// has to change.
        /// </remarks>
        public bool HasWork =>
            IsEngineTick || IsEventScan || IsSnapshot || IsLifecycle || IsIndices ||
            IsMandateMonitor || IsPollTick || LlmWake != LlmWakeCadence.None;
    }

    /// <summary>
    /// The monthly tick calendar. Pure: <c>(startDate, date, settings, tuning) → TickPlan</c>.
    /// </summary>
    /// <remarks>
    /// Every cadence is measured in whole months from the save's start date rather than from "the last
    /// time this ran", so a reload, a fast-forward or a skipped tick cannot shift the phase of the
    /// calendar. That is what makes a replayed save land on the same lifecycle years as the original.
    /// </remarks>
    public static class TickPlanner
    {
        /// <summary>
        /// Builds the plan for one date.
        /// </summary>
        /// <param name="startDate">The save's first political date. The phase anchor for every cadence.</param>
        /// <param name="date">The date being ticked.</param>
        /// <param name="settings">Per-save settings; supplies the LLM wake cadence (non-negotiable #10).</param>
        /// <param name="nextElectionDate">Next scheduled election, or null when none is scheduled.</param>
        /// <param name="electionThisTick">True when an election resolves on this date.</param>
        /// <param name="manualWakeRequested">True when the player pressed the manual flavor button.</param>
        /// <param name="tuning">Engine tuning. Never null — pass <see cref="EngineTuning.Default"/>.</param>
        public static TickPlan Plan(SimDate startDate, SimDate date, AgoraSettings settings,
                                    SimDate? nextElectionDate, bool electionThisTick,
                                    bool manualWakeRequested, EngineTuning tuning)
        {
            if (tuning == null) tuning = EngineTuning.Default;
            if (settings == null) settings = new AgoraSettings();

            SchedulerTuning s = tuning.Scheduler;

            int elapsed = startDate.MonthsUntil(date);

            // A negative elapsed month count means the caller asked about a date before the save
            // started. Nothing is due then — but never throw: the clock is the Mod's, not ours.
            bool engineTick = elapsed >= 0 && OnInterval(elapsed, s.TickIntervalMonths <= 0 ? 1 : s.TickIntervalMonths);

            bool eventScan = engineTick && OnInterval(elapsed, s.EventScanIntervalMonths);
            bool snapshot = engineTick && OnInterval(elapsed, s.SnapshotIntervalMonths);
            bool lifecycle = engineTick && OnInterval(elapsed, s.LifecycleTickMonths);
            bool indices = engineTick && OnInterval(elapsed, s.IndicesTickMonths);
            bool mandates = engineTick && OnInterval(elapsed, s.MandateMonitorIntervalMonths);

            // Gated on engineTick like every other cadence here: a poll published on a month the
            // engine did not advance would report shares nothing had recomputed.
            bool pollTick = engineTick && OnInterval(elapsed, s.PollTickIntervalMonths);

            // The story cycle. Its cadence lives in the `stories` section rather than `scheduler`,
            // because it is the story system's own dial and the two content lanes that read it look
            // there — but the phase arithmetic is identical to every cadence above.
            //
            // Floored at 2, and the floor is not defensive tidiness: at a cadence of 1 the draft and
            // resolve phases would be the same month, so a story would be scored on the tick that
            // drew it — against a city that had not moved since, making every delta and every window
            // provably unmeasurable. A hand-edited tuning file that reaches 1 gets the shipped
            // cadence rather than a story system that silently scores nothing.
            int cycle = tuning.Stories.CycleMonths;
            if (cycle < 2) cycle = 2;

            // The resolve phase is the LAST month of the cycle because that is the month the stories
            // are due: StoryAssembler.NewStory sets ResolvesDate = OpenedDate + (cycleMonths - 1).
            // Writing a literal 1 here is correct at the shipped cadence of 2 and wrong at every
            // wider one — see the remarks on TickPlan.IsStoryResolve for what that cost.
            int cyclePhase = elapsed >= 0 ? elapsed % cycle : -1;
            bool storyDraft = engineTick && cyclePhase == 0;
            bool storyResolve = engineTick && cyclePhase == cycle - 1;

            bool warmupComplete = elapsed >= (s.WarmupMonths < 0 ? 0 : s.WarmupMonths);

            bool campaign = false;
            if (nextElectionDate.HasValue)
            {
                int toElection = date.MonthsUntil(nextElectionDate.Value);
                campaign = toElection >= 0 && toElection <= s.CampaignStartMonthsBeforeElection;
            }

            LlmWakeCadence wake = LlmWakeCadence.None;
            LlmWakeCadence allowed = settings.WakeCadence;

            if (engineTick && s.LlmWakeYearly && (allowed & LlmWakeCadence.Yearly) != 0
                && date.Month == NormaliseMonth(s.LlmWakeMonth))
            {
                wake |= LlmWakeCadence.Yearly;
            }

            if (electionThisTick && s.LlmWakeOnElection && (allowed & LlmWakeCadence.Election) != 0)
            {
                wake |= LlmWakeCadence.Election;
            }

            if (manualWakeRequested && s.LlmWakeManualEnabled && (allowed & LlmWakeCadence.Manual) != 0)
            {
                wake |= LlmWakeCadence.Manual;
            }

            // On the DRAFT month, not the resolve month. The prose a story wake exists to fetch is
            // the opening article for the stories drafted on this very tick, and the resolution piece
            // for whatever resolved alongside them — both of which the draft tick has just produced.
            // Waking on the resolve phase instead would ask the model to write about stories the
            // player has already been reading for a full cycle, and at the shipped cadence of 2 the
            // two phases are adjacent months, so the mistake would look like a one-month lag rather
            // than like an error.
            //
            // Gated on the story layer being ON, and that gate is load-bearing rather than tidy.
            // IsStoryDraft is pure phase arithmetic — it stays true every cycle on a save that has
            // stories switched off, because the switch is honoured downstream in StoryCycle, which
            // returns before drafting anything. So without this clause a player who turned stories
            // off would have the mod start a subprocess every two months to fetch prose about
            // stories that will never exist, which is both the most frequent wake in the build and
            // the one they most clearly said no to.
            bool storyLayerOn = tuning.Stories.Enabled && settings.StoriesEnabled;

            if (storyDraft && storyLayerOn && s.LlmWakeOnStoryDraft && (allowed & LlmWakeCadence.Story) != 0)
            {
                wake |= LlmWakeCadence.Story;
            }

            return new TickPlan(date, engineTick, eventScan, snapshot, lifecycle, indices, mandates,
                                pollTick, storyDraft, storyResolve, warmupComplete, campaign, wake);
        }

        /// <summary>
        /// The dates to replay when the sim has moved on further than one tick — load reconciliation
        /// and fast-forward (§5). Oldest first, exclusive of <paramref name="from"/>, inclusive of
        /// <paramref name="to"/>.
        /// </summary>
        /// <remarks>
        /// Clamped to <c>scheduler.catchUpMaxMonths</c>. When the gap is longer than the cap the
        /// <i>oldest</i> months are dropped, not the newest: the political state has to end up at the
        /// current date, and the recent past is what the player will actually see referenced.
        /// </remarks>
        public static List<SimDate> CatchUpDates(SimDate from, SimDate to, EngineTuning tuning, out bool truncated)
        {
            if (tuning == null) tuning = EngineTuning.Default;

            var dates = new List<SimDate>();
            truncated = false;

            int gap = from.MonthsUntil(to);
            if (gap <= 0) return dates;

            int cap = tuning.Scheduler.CatchUpMaxMonths;
            if (cap < 0) cap = 0;

            int months = gap;
            if (months > cap)
            {
                months = cap;
                truncated = true;
            }

            // Walk back from `to` so the final entry is exactly `to` regardless of truncation.
            for (int i = months - 1; i >= 0; i--)
            {
                dates.Add(to.AddMonths(-i));
            }

            return dates;
        }

        /// <summary>
        /// Which sidecar snapshots to delete, oldest first.
        /// </summary>
        /// <remarks>
        /// AGORA-SEAM(§14.3): the retention default is proposed, not ratified. This keeps the newest N
        /// and does nothing cleverer — no thinning, no keep-one-per-year — precisely so that closing
        /// the decision is a policy change here and nowhere else.
        /// </remarks>
        /// <param name="settings">
        /// Per-save settings. When present its <see cref="AgoraSettings.SnapshotRetention"/> wins over
        /// tuning — retention is a per-save setting, not global config (non-negotiable #10).
        /// </param>
        public static List<SimDate> SnapshotsToPrune(IReadOnlyList<SimDate> existing, EngineTuning tuning,
                                                     AgoraSettings? settings = null)
        {
            if (tuning == null) tuning = EngineTuning.Default;

            var prune = new List<SimDate>();
            if (existing == null || existing.Count == 0) return prune;

            int retention = settings != null && settings.SnapshotRetention > 0
                ? settings.SnapshotRetention
                : tuning.Scheduler.SnapshotRetention;

            if (retention < 1) retention = 1;
            if (existing.Count <= retention) return prune;

            var sorted = new List<SimDate>(existing);
            sorted.Sort(CompareDates);

            int excess = sorted.Count - retention;
            for (int i = 0; i < excess; i++) prune.Add(sorted[i]);
            return prune;
        }

        private static int CompareDates(SimDate a, SimDate b) => a.CompareTo(b);

        /// <summary>
        /// True when <paramref name="elapsedMonths"/> lands on the interval. A non-positive interval
        /// means "never" — the one exception is the master tick interval, which the caller floors at 1
        /// because a zero there would freeze the engine rather than configure it.
        /// </summary>
        internal static bool OnInterval(int elapsedMonths, int intervalMonths)
        {
            if (intervalMonths <= 0) return false;
            if (elapsedMonths < 0) return false;
            return (elapsedMonths % intervalMonths) == 0;
        }

        private static int NormaliseMonth(int month)
        {
            if (month < 1) return 1;
            return month > 12 ? 12 : month;
        }
    }
}
