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
        /// Day-of-month cadence for polling (<c>pollTickIntervalDays</c>). Deliberately <b>not</b>
        /// gated on <see cref="IsCampaignSeason"/> — polling packets that only publish during a
        /// campaign should AND the two themselves, rather than have that policy baked in here.
        /// </summary>
        public bool IsPollTick { get; }

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
            IsWarmupComplete = isWarmupComplete;
            IsCampaignSeason = isCampaignSeason;
            LlmWake = llmWake;
        }

        /// <summary>True when any subsystem is due. Cheap early-out for the caller.</summary>
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

            int pollDays = s.PollTickIntervalDays <= 0 ? 1 : s.PollTickIntervalDays;
            bool pollTick = elapsed >= 0 && ((date.Day - 1) % pollDays) == 0;

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

            return new TickPlan(date, engineTick, eventScan, snapshot, lifecycle, indices, mandates,
                                pollTick, warmupComplete, campaign, wake);
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
