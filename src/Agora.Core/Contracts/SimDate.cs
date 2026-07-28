using System;

namespace Agora.Core.Contracts
{
    /// <summary>
    /// A date on the political calendar. This is the only date type the engine understands.
    ///
    /// <para>
    /// <see cref="DateTime"/> is avoided on purpose: it carries a time zone story, a calendar story
    /// and a <c>Now</c> property, and any of the three leaking into the engine would break
    /// determinism. <see cref="SimDate"/> is a plain (year, month, day) triple with a total order.
    /// </para>
    ///
    /// <para>
    /// Non-negotiable #8: dates come from <c>AgoraTimeService</c> in Agora.Mod. Nothing else computes
    /// a year.
    /// </para>
    /// </summary>
    public readonly struct SimDate : IEquatable<SimDate>, IComparable<SimDate>
    {
        public int Year { get; }

        /// <summary>1–12.</summary>
        public int Month { get; }

        /// <summary>1–31. The game's calendar is regular; no leap-year handling is implied.</summary>
        public int Day { get; }

        public SimDate(int year, int month, int day)
        {
            if (month < 1 || month > 12)
                throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be 1–12.");
            if (day < 1 || day > 31)
                throw new ArgumentOutOfRangeException(nameof(day), day, "Day must be 1–31.");

            Year = year;
            Month = month;
            Day = day;
        }

        /// <summary>Months since year 0. The engine's monthly tick counts in these.</summary>
        public int TotalMonths => Year * 12 + (Month - 1);

        /// <summary>A new date this many months later. Day is preserved and never rolls over.</summary>
        public SimDate AddMonths(int months)
        {
            int total = TotalMonths + months;
            return new SimDate(total / 12, (total % 12) + 1, Day);
        }

        public SimDate AddYears(int years) => new SimDate(Year + years, Month, Day);

        /// <summary>Whole months from this date to <paramref name="other"/>; negative if it is earlier.</summary>
        public int MonthsUntil(SimDate other) => other.TotalMonths - TotalMonths;

        /// <summary>Sortable, stable, and the form used in sidecar filenames and seed derivation.</summary>
        public override string ToString() => $"{Year:D4}-{Month:D2}-{Day:D2}";

        public bool Equals(SimDate other) =>
            Year == other.Year && Month == other.Month && Day == other.Day;

        public override bool Equals(object? obj) => obj is SimDate other && Equals(other);

        public override int GetHashCode() => (Year * 397 ^ Month) * 397 ^ Day;

        public int CompareTo(SimDate other)
        {
            int y = Year.CompareTo(other.Year);
            if (y != 0) return y;
            int m = Month.CompareTo(other.Month);
            return m != 0 ? m : Day.CompareTo(other.Day);
        }

        public static bool operator ==(SimDate a, SimDate b) => a.Equals(b);
        public static bool operator !=(SimDate a, SimDate b) => !a.Equals(b);
        public static bool operator <(SimDate a, SimDate b) => a.CompareTo(b) < 0;
        public static bool operator >(SimDate a, SimDate b) => a.CompareTo(b) > 0;
        public static bool operator <=(SimDate a, SimDate b) => a.CompareTo(b) <= 0;
        public static bool operator >=(SimDate a, SimDate b) => a.CompareTo(b) >= 0;
    }
}
