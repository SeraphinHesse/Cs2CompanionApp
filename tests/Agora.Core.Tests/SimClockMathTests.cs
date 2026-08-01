using Agora.Core.Contracts;
using Agora.Mod.Time;
using Xunit;

namespace Agora.Mod.Time.Tests
{
    /// <summary>
    /// The clock arithmetic. These tests exist because the obvious implementation — read
    /// <c>TimeSystem.GetCurrentDateTime().Month</c> — is silently wrong: the shipped
    /// <c>daysPerYear</c> is 12, so the game's DateTime never leaves January.
    /// </summary>
    public sealed class SimClockMathTests
    {
        [Theory]
        [InlineData(0.0, 1)]
        [InlineData(0.0833, 1)]   // just under 1/12
        [InlineData(0.0834, 2)]   // just over
        [InlineData(0.5, 7)]
        [InlineData(0.9167, 12)]
        [InlineData(0.99999, 12)]
        public void MonthFromNormalizedDate_MapsTheYearFractionOntoTwelveMonths(double normalized, int expected)
        {
            Assert.Equal(expected, SimClockMath.MonthFromNormalizedDate(normalized));
        }

        [Fact]
        public void MonthFromNormalizedDate_CoversEveryMonthExactlyOnceAcrossAYear()
        {
            var seen = new bool[13];
            for (int step = 0; step < 12; step++)
            {
                // Sample the middle of each twelfth so no result sits on a boundary.
                double normalized = (step + 0.5) / 12.0;
                int month = SimClockMath.MonthFromNormalizedDate(normalized);
                Assert.InRange(month, 1, 12);
                Assert.False(seen[month], "month " + month + " produced twice");
                seen[month] = true;
            }
        }

        [Fact]
        public void MonthFromNormalizedDate_IsMonotoneAcrossTheYear()
        {
            int previous = 0;
            for (int i = 0; i < 10000; i++)
            {
                int month = SimClockMath.MonthFromNormalizedDate(i / 10000.0);
                Assert.True(month >= previous, "month went backwards at sample " + i);
                previous = month;
            }
            Assert.Equal(12, previous);
        }

        [Theory]
        [InlineData(1.0)]      // an exact year boundary
        [InlineData(2.25)]     // a value that has wrapped past the year
        [InlineData(-0.01)]    // negative: GetTimeOfYear does a signed int modulo
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void MonthFromNormalizedDate_NeverLeavesTheLegalRange(double normalized)
        {
            Assert.InRange(SimClockMath.MonthFromNormalizedDate(normalized), 1, 12);
        }

        [Fact]
        public void MonthProgress_RunsZeroToOneInsideEachMonthAndResets()
        {
            Assert.Equal(0.0, SimClockMath.MonthProgress(0.0), 6);

            double quarterThroughMonthOne = SimClockMath.MonthProgress(0.25 / 12.0);
            Assert.Equal(0.25, quarterThroughMonthOne, 6);

            // Same position inside the next month, not a continuation of the first.
            double quarterThroughMonthTwo = SimClockMath.MonthProgress(1.25 / 12.0);
            Assert.Equal(0.25, quarterThroughMonthTwo, 6);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(-3.5)]
        [InlineData(17.0)]
        public void MonthProgress_StaysInUnitRange(double normalized)
        {
            Assert.InRange(SimClockMath.MonthProgress(normalized), 0.0, 1.0);
        }

        [Theory]
        [InlineData(1990, 1990)]
        [InlineData(0, SimClockMath.MinYear)]
        [InlineData(-4000, SimClockMath.MinYear)]
        [InlineData(int.MaxValue, SimClockMath.MaxYear)]
        [InlineData(SimClockMath.MaxYear, SimClockMath.MaxYear)]
        public void ClampYear_KeepsTheYearInsideWhatTheGameCanExpress(int input, int expected)
        {
            Assert.Equal(expected, SimClockMath.ClampYear(input));
        }

        [Fact]
        public void ToSimDate_PinsTheDayToOneSoSeedsChangeOnlyOncePerMonth()
        {
            SimDate early = SimClockMath.ToSimDate(1990, 0.0);
            SimDate late = SimClockMath.ToSimDate(1990, 1.0 / 12.0 - 1e-6);

            Assert.Equal(1, early.Day);
            Assert.Equal(1, late.Day);

            // Same month, same string -> same sidecar filename, same derived seed all month.
            Assert.Equal(early.ToString(), late.ToString());
        }

        [Fact]
        public void ToSimDate_ProducesTheRatifiedStartOfHistory()
        {
            SimDate date = SimClockMath.ToSimDate(1990, 0.0);
            Assert.Equal("1990-01-01", date.ToString());
        }

        [Fact]
        public void ToSimDate_AdvancesOneMonthPerTwelfthOfTheYear()
        {
            SimDate january = SimClockMath.ToSimDate(1990, 0.0);
            SimDate december = SimClockMath.ToSimDate(1990, 11.5 / 12.0);

            Assert.Equal(11, january.MonthsUntil(december));
        }

        [Fact]
        public void ToSimDate_ClampsAnAbsurdYearRatherThanThrowing()
        {
            SimDate date = SimClockMath.ToSimDate(-99, 0.5);
            Assert.Equal(SimClockMath.MinYear, date.Year);
        }
    }
}
