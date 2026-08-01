using System;
using Agora.Core.Contracts;

namespace Agora.Mod.Time
{
    /// <summary>
    /// The arithmetic half of the clock. Pure, allocation-free, and deliberately free of every
    /// <c>Game.*</c>, <c>Colossal.*</c> and <c>Unity.*</c> type so it can be unit-tested on a machine
    /// with no copy of Cities: Skylines II (see <c>tests/Agora.Mod.Time.Tests</c>).
    ///
    /// <para>
    /// <b>Why this exists at all — the trap in <c>TimeSystem.GetCurrentDateTime()</c>.</b> The game
    /// builds its <see cref="DateTime"/> as
    /// <c>day = 1 + floor(daysPerYear * normalizedDate) % daysPerYear</c> and then
    /// <c>new DateTime(0).AddYears(year - 1).AddDays(day - 1)</c>. The shipped
    /// <c>TimeSettingsData.m_DaysPerYear</c> is <b>12</b> — one in-game "day" is one calendar month —
    /// so <c>day</c> only ever reaches 12 and the returned <c>DateTime</c> is always
    /// <c>1 January … 12 January</c>. Reading <c>.Month</c> off it yields <b>1, always</b>. Deriving
    /// the political month from <c>normalizedDate</c> instead is correct for any
    /// <c>daysPerYear</c>, including modded values.
    /// </para>
    ///
    /// <para>
    /// <b>Why <see cref="SimDate.Day"/> is pinned to 1.</b> AGORA's calendar is month-granular: the
    /// engine ticks monthly and <c>SimDate.ToString()</c> feeds sidecar filenames and seed
    /// derivation. A day-of-month that wobbled inside a month would change every seeded stream
    /// several times per political tick for no modelling benefit, and CS2's own HUD shows a month and
    /// a year — there is no day-of-month surface to be consistent with. Sub-month position is exposed
    /// separately as <see cref="MonthProgress"/>, for UI only; nothing in the engine may consume it.
    /// </para>
    /// </summary>
    public static class SimClockMath
    {
        public const int MonthsPerYear = 12;

        /// <summary>
        /// Lowest year the game's own <c>CreateDateTime</c> can express: it starts from
        /// <c>new DateTime(0)</c> — year 1 — and calls <c>AddYears(year - 1)</c>.
        /// </summary>
        public const int MinYear = 1;

        /// <summary>Highest year <see cref="DateTime"/> can express.</summary>
        public const int MaxYear = 9999;

        /// <summary>
        /// Political month (1–12) from the game's <c>TimeSystem.normalizedDate</c> — the fraction of
        /// the way through the current in-game year.
        /// </summary>
        /// <remarks>
        /// Values outside [0,1) are wrapped rather than rejected. <c>GetTimeOfYear</c> is
        /// <c>(ticks % ticksPerYear) / ticksPerYear</c> over a signed int, so a save whose
        /// <c>m_FirstFrame</c> sits ahead of the current frame can legitimately produce a small
        /// negative fraction. Wrapping keeps the month in range instead of throwing inside a clock
        /// read, which nothing upstream is prepared to handle.
        /// </remarks>
        public static int MonthFromNormalizedDate(double normalizedDate)
        {
            if (double.IsNaN(normalizedDate) || double.IsInfinity(normalizedDate))
            {
                return 1;
            }

            double fraction = normalizedDate - Math.Floor(normalizedDate);
            int month = 1 + (int)Math.Floor(fraction * MonthsPerYear);

            // Guards the one-ULP case where fraction rounds to exactly 1.0 after the subtraction.
            if (month < 1) return 1;
            if (month > MonthsPerYear) return MonthsPerYear;
            return month;
        }

        /// <summary>
        /// How far through the current political month we are, 0 (first instant) to 1 (last instant).
        /// <b>Presentation only.</b> No engine state may be derived from this — it moves every frame
        /// and would decouple seeded streams from the monthly tick.
        /// </summary>
        public static double MonthProgress(double normalizedDate)
        {
            if (double.IsNaN(normalizedDate) || double.IsInfinity(normalizedDate))
            {
                return 0.0;
            }

            double fraction = normalizedDate - Math.Floor(normalizedDate);
            double scaled = fraction * MonthsPerYear;
            double progress = scaled - Math.Floor(scaled);

            if (progress < 0.0) return 0.0;
            if (progress > 1.0) return 1.0;
            return progress;
        }

        /// <summary>
        /// Clamps a year to the range the game's own date construction can survive. A political start
        /// year outside this range would make <c>TimeSystem.GetCurrentDateTime()</c> throw deep inside
        /// the game's UI thread, which is a far worse failure than a clamped year.
        /// </summary>
        public static int ClampYear(int year)
        {
            if (year < MinYear) return MinYear;
            if (year > MaxYear) return MaxYear;
            return year;
        }

        /// <summary>
        /// The political date. This is the only place a <see cref="SimDate"/> is manufactured from
        /// game clock readings (non-negotiable #8).
        /// </summary>
        public static SimDate ToSimDate(int politicalYear, double normalizedDate)
        {
            return new SimDate(ClampYear(politicalYear), MonthFromNormalizedDate(normalizedDate), 1);
        }
    }
}
