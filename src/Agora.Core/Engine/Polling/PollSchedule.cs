using System;
using System.Collections.Generic;
using System.Globalization;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Polling
{
    /// <summary>
    /// The polling calendar: when polls publish during a campaign, which pollster is in the field, and
    /// how many polls the sidecar keeps.
    ///
    /// <para>
    /// Pure functions of (election date, tuning). Nothing here draws a random number — which pollster
    /// publishes on a given day is a rotation, not a lottery, so a reload cannot produce a different
    /// pollster and therefore a different house effect for the same date.
    /// </para>
    /// </summary>
    public static class PollSchedule
    {
        /// <summary>First day of the polling season, <c>polling.campaignWeeks</c> before the vote.</summary>
        public static SimDate CampaignStart(SimDate electionDate, EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            int weeks = tuning.Polling.CampaignWeeks;
            if (weeks < 0) weeks = 0;
            return PollCalendar.AddDays(electionDate, -weeks * PollCalendar.DaysPerWeek);
        }

        /// <summary>True from the first day of the polling season through election day inclusive.</summary>
        public static bool IsCampaignSeason(SimDate today, SimDate electionDate, EngineTuning tuning) =>
            today >= CampaignStart(electionDate, tuning) && today <= electionDate;

        /// <summary>
        /// Every publication date of one campaign, ascending, starting at
        /// <see cref="CampaignStart"/> and stepping by <c>polling.publishIntervalDays</c>.
        /// </summary>
        public static List<SimDate> PublishDates(SimDate electionDate, EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            int interval = tuning.Polling.PublishIntervalDays;
            if (interval < 1) interval = 1;

            int weeks = tuning.Polling.CampaignWeeks;
            if (weeks < 0) weeks = 0;

            int totalDays = weeks * PollCalendar.DaysPerWeek;
            int start = PollCalendar.ToDayNumber(CampaignStart(electionDate, tuning));

            var dates = new List<SimDate>(totalDays / interval + 1);
            for (int offset = 0; offset <= totalDays; offset += interval)
                dates.Add(PollCalendar.FromDayNumber(start + offset));

            return dates;
        }

        /// <summary>
        /// Position of <paramref name="today"/> in this campaign's publication schedule, or -1 if no
        /// poll publishes that day.
        /// </summary>
        public static int PublishIndex(SimDate today, SimDate electionDate, EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            int interval = tuning.Polling.PublishIntervalDays;
            if (interval < 1) interval = 1;

            int weeks = tuning.Polling.CampaignWeeks;
            if (weeks < 0) weeks = 0;

            int offset = PollCalendar.DaysBetween(CampaignStart(electionDate, tuning), today);
            if (offset < 0 || offset > weeks * PollCalendar.DaysPerWeek) return -1;
            return offset % interval == 0 ? offset / interval : -1;
        }

        /// <summary>True when a poll publishes on <paramref name="today"/> for this election.</summary>
        public static bool IsPublishDay(SimDate today, SimDate electionDate, EngineTuning tuning) =>
            PublishIndex(today, electionDate, tuning) >= 0;

        /// <summary>
        /// The pollster in the field at a given position in the schedule. A plain rotation over
        /// <c>polling.pollsterCount</c>: three houses publishing weekly means each is heard from every
        /// three weeks, and each carries its own stable house effect.
        /// </summary>
        /// <remarks>
        /// Ids are 1-based and zero-padded to two digits so that ordinal sorting matches numeric order
        /// for the shipped pollster count. Beyond 99 pollsters the ids stay unique but stop sorting
        /// numerically; nothing in the engine depends on that ordering.
        /// </remarks>
        public static string PollsterIdFor(int publishIndex, EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            int count = tuning.Polling.PollsterCount;
            if (count < 1) count = 1;

            int index = publishIndex % count;
            if (index < 0) index += count;

            return "pollster-" + (index + 1).ToString("D2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The pollster publishing on <paramref name="today"/>, or null if no poll publishes then.
        /// </summary>
        public static string? PollsterForDate(SimDate today, SimDate electionDate, EngineTuning tuning)
        {
            int index = PublishIndex(today, electionDate, tuning);
            return index < 0 ? null : PollsterIdFor(index, tuning);
        }

        /// <summary>
        /// Keeps the newest <c>polling.maxStoredPolls</c>, oldest first — the order
        /// <c>PoliticalState.RecentPolls</c> is contractually stored in.
        /// </summary>
        /// <remarks>
        /// Sorts by date, then by id, before trimming. The input is supposed to be ordered already;
        /// sorting anyway means a caller that appended out of order cannot make the sidecar hash
        /// depend on insertion history.
        /// </remarks>
        public static List<PollResult> Trim(IReadOnlyList<PollResult>? polls, EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            var ordered = new List<PollResult>();
            if (polls != null)
                foreach (PollResult poll in polls)
                    if (poll != null) ordered.Add(poll);

            ordered.Sort((a, b) =>
            {
                int byDate = a.Date.CompareTo(b.Date);
                return byDate != 0 ? byDate : string.CompareOrdinal(a.Id ?? "", b.Id ?? "");
            });

            int max = tuning.Polling.MaxStoredPolls;
            if (max < 0) max = 0;
            if (ordered.Count <= max) return ordered;

            return ordered.GetRange(ordered.Count - max, max);
        }
    }
}
