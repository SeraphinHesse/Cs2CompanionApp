using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The determinism suite. Non-negotiable #3 says engine state is a pure function of its inputs;
    /// these tests are what makes that claim falsifiable.
    /// </summary>
    public class DeterminismTests
    {
        private static readonly Guid SaveA = new Guid("11111111-2222-3333-4444-555555555555");
        private static readonly Guid SaveB = new Guid("99999999-8888-7777-6666-555555555555");
        private static readonly SimDate Jan1990 = new SimDate(1990, 1, 1);

        // --- Seed derivation -------------------------------------------------------------------

        [Fact]
        public void Derive_IsStable_ForIdenticalInputs()
        {
            ulong first = SeedStreams.Derive(SaveA, Jan1990, StreamNames.PollError);
            ulong second = SeedStreams.Derive(SaveA, Jan1990, StreamNames.PollError);

            Assert.Equal(first, second);
        }

        /// <summary>
        /// Pins the hash to a known value. This is the test that catches an "innocent" refactor of
        /// SeedStreams — swapping in string.GetHashCode, reordering the mix, changing the encoding —
        /// any of which would silently rewrite every existing save's political history.
        /// </summary>
        [Fact]
        public void Derive_MatchesGoldenValue()
        {
            Assert.Equal(13577910404977656935UL, SeedStreams.Derive(SaveA, Jan1990, StreamNames.PollError));
        }

        [Fact]
        public void Derive_DiffersBySave()
        {
            Assert.NotEqual(
                SeedStreams.Derive(SaveA, Jan1990, StreamNames.PollError),
                SeedStreams.Derive(SaveB, Jan1990, StreamNames.PollError));
        }

        [Fact]
        public void Derive_DiffersByDate()
        {
            Assert.NotEqual(
                SeedStreams.Derive(SaveA, Jan1990, StreamNames.PollError),
                SeedStreams.Derive(SaveA, new SimDate(1990, 2, 1), StreamNames.PollError));
        }

        [Fact]
        public void Derive_DiffersByStream()
        {
            Assert.NotEqual(
                SeedStreams.Derive(SaveA, Jan1990, StreamNames.PollError),
                SeedStreams.Derive(SaveA, Jan1990, StreamNames.TurnoutNoise));
        }

        [Fact]
        public void RngFor_GivesIndependentSubStreamsPerEntity()
        {
            var north = SeedStreams.RngFor(SaveA, Jan1990, StreamNames.AffinityNoise, "district-north");
            var south = SeedStreams.RngFor(SaveA, Jan1990, StreamNames.AffinityNoise, "district-south");

            Assert.NotEqual(north.NextULong(), south.NextULong());
        }

        [Fact]
        public void Derive_RejectsEmptyStreamName()
        {
            Assert.Throws<ArgumentException>(() => SeedStreams.Derive(SaveA, Jan1990, ""));
        }

        // --- Generator -------------------------------------------------------------------------

        [Fact]
        public void Rng_SameSeed_ProducesIdenticalSequence()
        {
            var a = SeedStreams.Rng(SaveA, Jan1990, StreamNames.AffinityNoise);
            var b = SeedStreams.Rng(SaveA, Jan1990, StreamNames.AffinityNoise);

            ulong[] first = Enumerable.Range(0, 100).Select(_ => a.NextULong()).ToArray();
            ulong[] second = Enumerable.Range(0, 100).Select(_ => b.NextULong()).ToArray();

            Assert.Equal(first, second);
        }

        [Fact]
        public void NextDouble_StaysInUnitInterval()
        {
            var rng = SeedStreams.Rng(SaveA, Jan1990, StreamNames.PollTurnout);

            for (int i = 0; i < 10_000; i++)
            {
                double value = rng.NextDouble();
                Assert.InRange(value, 0.0, 0.9999999999999999);
            }
        }

        [Fact]
        public void NextInt_RespectsBounds()
        {
            var rng = SeedStreams.Rng(SaveA, Jan1990, StreamNames.PartyLifecycle);

            for (int i = 0; i < 10_000; i++)
            {
                int value = rng.NextInt(3, 9);
                Assert.InRange(value, 3, 8);
            }
        }

        [Fact]
        public void NextInt_RejectsEmptyRange()
        {
            var rng = SeedStreams.Rng(SaveA, Jan1990, StreamNames.PartyLifecycle);
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
        }

        [Fact]
        public void NextGaussian_IsDeterministicDespiteRejectionLoop()
        {
            var a = SeedStreams.Rng(SaveA, Jan1990, StreamNames.PollError);
            var b = SeedStreams.Rng(SaveA, Jan1990, StreamNames.PollError);

            double[] first = Enumerable.Range(0, 200).Select(_ => a.NextGaussian()).ToArray();
            double[] second = Enumerable.Range(0, 200).Select(_ => b.NextGaussian()).ToArray();

            Assert.Equal(first, second);
        }

        [Fact]
        public void Shuffle_IsDeterministic()
        {
            var a = SeedStreams.Rng(SaveA, Jan1990, StreamNames.NameSelection);
            var b = SeedStreams.Rng(SaveA, Jan1990, StreamNames.NameSelection);

            var listA = Enumerable.Range(0, 50).ToList();
            var listB = Enumerable.Range(0, 50).ToList();

            a.Shuffle(listA);
            b.Shuffle(listB);

            Assert.Equal(listA, listB);
            Assert.NotEqual(Enumerable.Range(0, 50).ToList(), listA);
        }

        // --- The canonical whole-run pattern ----------------------------------------------------

        /// <summary>
        /// The pattern every engine test should follow: run twice from identical inputs and compare
        /// a hash of the serialized result, not field by field. Hashing catches the field a
        /// hand-written assertion forgot to check — which is exactly where desyncs hide.
        /// </summary>
        [Fact]
        public void SimulatedRun_ProducesIdenticalHashTwice()
        {
            string first = HashRun(SaveA, Jan1990);
            string second = HashRun(SaveA, Jan1990);

            Assert.Equal(first, second);
        }

        [Fact]
        public void SimulatedRun_DiffersAcrossSaves()
        {
            Assert.NotEqual(HashRun(SaveA, Jan1990), HashRun(SaveB, Jan1990));
        }

        /// <summary>
        /// Stands in for a real engine tick until M2. Consumes several streams across several months
        /// so it exercises seed derivation, sub-streams and ordering together.
        /// </summary>
        private static string HashRun(Guid save, SimDate start)
        {
            var districts = new[] { "north", "south", "harbour", "old-town" };
            var output = new StringBuilder();
            SimDate date = start;

            for (int month = 0; month < 24; month++)
            {
                var pollRng = SeedStreams.Rng(save, date, StreamNames.PollError);
                output.Append(date).Append('|').Append(pollRng.NextGaussian().ToString("R")).Append('|');

                // Sub-streams per district: independent of iteration order, so adding a district
                // later cannot perturb the others.
                foreach (string district in districts.OrderBy(d => d, StringComparer.Ordinal))
                {
                    var rng = SeedStreams.RngFor(save, date, StreamNames.AffinityNoise, district);
                    output.Append(district).Append('=').Append(rng.NextDouble().ToString("R")).Append(';');
                }

                output.Append('\n');
                date = date.AddMonths(1);
            }

            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(output.ToString()));
            return BitConverter.ToString(hash).Replace("-", "");
        }
    }

    /// <summary>SimDate is contract surface — its arithmetic feeds seed derivation and the calendar.</summary>
    public class SimDateTests
    {
        [Fact]
        public void AddMonths_RollsOverYearBoundary()
        {
            var date = new SimDate(1990, 11, 15).AddMonths(3);

            Assert.Equal(1991, date.Year);
            Assert.Equal(2, date.Month);
            Assert.Equal(15, date.Day);
        }

        [Fact]
        public void AddMonths_HandlesNegativeSpans()
        {
            var date = new SimDate(1990, 2, 10).AddMonths(-3);

            Assert.Equal(1989, date.Year);
            Assert.Equal(11, date.Month);
        }

        [Fact]
        public void MonthsUntil_IsSignedAndSymmetric()
        {
            var start = new SimDate(1990, 1, 1);
            var end = new SimDate(1993, 1, 1);

            Assert.Equal(36, start.MonthsUntil(end));
            Assert.Equal(-36, end.MonthsUntil(start));
        }

        [Fact]
        public void Ordering_IsTotalAndChronological()
        {
            var dates = new List<SimDate>
            {
                new SimDate(1991, 1, 1),
                new SimDate(1990, 6, 15),
                new SimDate(1990, 6, 1),
                new SimDate(1990, 1, 1),
            };

            dates.Sort();

            Assert.Equal(new SimDate(1990, 1, 1), dates[0]);
            Assert.Equal(new SimDate(1990, 6, 1), dates[1]);
            Assert.Equal(new SimDate(1990, 6, 15), dates[2]);
            Assert.Equal(new SimDate(1991, 1, 1), dates[3]);
        }

        [Fact]
        public void ToString_IsSortableAndZeroPadded()
        {
            Assert.Equal("1990-03-07", new SimDate(1990, 3, 7).ToString());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(13)]
        public void Constructor_RejectsInvalidMonth(int month)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimDate(1990, month, 1));
        }
    }
}
