using System;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Polling
{
    /// <summary>
    /// Day-level arithmetic over <see cref="SimDate"/>.
    ///
    /// <para>
    /// <see cref="SimDate"/> only offers month and year arithmetic, but polls publish every
    /// <c>polling.publishIntervalDays</c> — a day-level cadence. Rather than reach for
    /// <see cref="DateTime"/> (banned by non-negotiable #8: <c>AgoraTimeService</c> is the only clock,
    /// and <see cref="DateTime"/> drags a time zone and a <c>Now</c> along with it), this converts a
    /// <see cref="SimDate"/> to a proleptic-Gregorian day number with pure integer arithmetic.
    /// </para>
    ///
    /// <para>
    /// The algorithm is Howard Hinnant's <c>days_from_civil</c> / <c>civil_from_days</c>: exact,
    /// branch-free of any locale or culture, and identical on every runtime. None of these numbers are
    /// tuning coefficients — they are the definition of the Gregorian calendar, so they live in code.
    /// </para>
    /// </summary>
    public static class PollCalendar
    {
        /// <summary>Days per week. A calendar fact, not a tunable.</summary>
        public const int DaysPerWeek = 7;

        private const int DaysFromCivilEpochShift = 719468; // 1970-01-01 == day 0
        private const int DaysPerEra = 146097;              // 400 Gregorian years
        private const int YearsPerEra = 400;

        /// <summary>
        /// Days since 1970-01-01, negative before it.
        /// </summary>
        /// <remarks>
        /// <see cref="SimDate"/> permits day 31 in a 30-day month (it validates only 1–31), so the day
        /// is clamped to the month's real length first. Without the clamp, "1994-02-31" would silently
        /// map onto 1994-03-03 and two distinct dates would collide.
        /// </remarks>
        public static int ToDayNumber(SimDate date)
        {
            int y = date.Year;
            int m = date.Month;
            int d = ClampDayToMonth(y, m, date.Day);

            y -= m <= 2 ? 1 : 0;

            int era = (y >= 0 ? y : y - (YearsPerEra - 1)) / YearsPerEra;
            int yoe = y - era * YearsPerEra;                             // [0, 399]
            int doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;    // [0, 365]
            int doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;             // [0, 146096]

            return era * DaysPerEra + doe - DaysFromCivilEpochShift;
        }

        /// <summary>The inverse of <see cref="ToDayNumber"/>. Round-trips for every valid civil date.</summary>
        public static SimDate FromDayNumber(int dayNumber)
        {
            int z = dayNumber + DaysFromCivilEpochShift;

            int era = (z >= 0 ? z : z - (DaysPerEra - 1)) / DaysPerEra;
            int doe = z - era * DaysPerEra;                                        // [0, 146096]
            int yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;       // [0, 399]
            int y = yoe + era * YearsPerEra;
            int doy = doe - (365 * yoe + yoe / 4 - yoe / 100);                     // [0, 365]
            int mp = (5 * doy + 2) / 153;                                          // [0, 11]
            int d = doy - (153 * mp + 2) / 5 + 1;                                  // [1, 31]
            int m = mp + (mp < 10 ? 3 : -9);                                       // [1, 12]

            return new SimDate(y + (m <= 2 ? 1 : 0), m, d);
        }

        /// <summary>A date <paramref name="days"/> later. Negative values move backwards.</summary>
        public static SimDate AddDays(SimDate date, int days) => FromDayNumber(ToDayNumber(date) + days);

        /// <summary>Whole days from <paramref name="from"/> to <paramref name="to"/>; negative if earlier.</summary>
        public static int DaysBetween(SimDate from, SimDate to) => ToDayNumber(to) - ToDayNumber(from);

        /// <summary>
        /// Whole weeks from <paramref name="from"/> to <paramref name="to"/>, truncated toward zero.
        /// Election day itself is week 0, which is what <c>PollResult.WeeksToElection</c> means.
        /// </summary>
        public static int WeeksBetween(SimDate from, SimDate to) => DaysBetween(from, to) / DaysPerWeek;

        /// <summary>Length of a month in the proleptic Gregorian calendar.</summary>
        public static int DaysInMonth(int year, int month)
        {
            switch (month)
            {
                case 1: case 3: case 5: case 7: case 8: case 10: case 12: return 31;
                case 4: case 6: case 9: case 11: return 30;
                case 2: return IsLeapYear(year) ? 29 : 28;
                default: throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be 1-12.");
            }
        }

        public static bool IsLeapYear(int year) =>
            (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;

        private static int ClampDayToMonth(int year, int month, int day)
        {
            int max = DaysInMonth(year, month);
            return day > max ? max : day < 1 ? 1 : day;
        }
    }
}
