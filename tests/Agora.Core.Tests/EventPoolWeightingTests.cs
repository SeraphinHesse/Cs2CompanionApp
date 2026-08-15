using System;
using System.Collections.Generic;
using Agora.Core.Stories;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Pity weighting and the declared total order over the event pool.
    ///
    /// <para>
    /// Every assertion here is about the <i>shape</i> of a relationship — "more misses never weighs
    /// less", "the streak stops mattering past the cap" — and never about a coefficient. A test that
    /// pinned <c>missStreakWeightStep</c> to 0.25 would go red on the next balance pass for a reason
    /// that has nothing to do with the property it guards, and a reader would have no way to tell
    /// which had happened.
    /// </para>
    /// </summary>
    public class EventPoolWeightingTests
    {
        private static readonly EngineTuning Tuning = EngineTuning.Default;

        private static double Weigh(int missStreak, int severity = 2)
        {
            return EventPoolWeighting.Weight(
                StoryTestFixtures.Pooled("evt-" + missStreak, missStreak),
                StoryTestFixtures.Event("evt-" + missStreak, severity),
                Tuning);
        }

        // --- the pity term ------------------------------------------------------------------------

        /// <summary>
        /// <b>Monotonic non-decreasing in <c>MissStreak</c>.</b> An entry that has waited longer must
        /// never weigh less than the same entry having waited less — that is the entire content of
        /// "pity weighting", and it is exactly the property a balance pass can break without anything
        /// looking wrong.
        /// </summary>
        [Fact]
        public void Weight_NeverDecreasesAsTheMissStreakGrows()
        {
            int ceiling = Tuning.Stories.MaxMissStreak + 5;

            for (int streak = 1; streak <= ceiling; streak++)
            {
                double previous = Weigh(streak - 1);
                double current = Weigh(streak);

                Assert.True(current >= previous,
                    "Weight fell from " + previous + " at MissStreak " + (streak - 1) +
                    " to " + current + " at MissStreak " + streak +
                    ": an entry that waited longer must never weigh less.");
            }
        }

        /// <summary>
        /// The streak contribution saturates at <c>stories.maxMissStreak</c>, so an ancient entry
        /// cannot crowd out everything else forever.
        /// </summary>
        [Fact]
        public void Weight_SaturatesAtTheTuningCap()
        {
            double atCap = Weigh(Tuning.Stories.MaxMissStreak);

            for (int over = 1; over <= 10; over++)
            {
                Assert.Equal(atCap, Weigh(Tuning.Stories.MaxMissStreak + over), 9);
            }
        }

        /// <summary>
        /// Saturation is a ceiling, not a switch that turns the term off: waiting up to the cap has to
        /// have bought something, or the pity term does nothing at all.
        /// </summary>
        [Fact]
        public void Weight_RewardsWaitingUpToTheCap()
        {
            Assert.True(Tuning.Stories.MissStreakWeightStep > 0.0,
                        "This test is meaningless if the shipped step is zero.");

            Assert.True(Weigh(Tuning.Stories.MaxMissStreak) > Weigh(0),
                "An entry that waited to the cap weighs no more than one that has never been passed " +
                "over, so the pity term is inert.");
        }

        /// <summary>
        /// A weight is a draw likelihood, so it has to be finite and positive. A zero would make an
        /// entry undrawable however long it waited, which no degradation branch could recover from.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(50)]
        public void Weight_IsFiniteAndPositive(int missStreak)
        {
            double weight = Weigh(missStreak);

            Assert.False(double.IsNaN(weight));
            Assert.False(double.IsInfinity(weight));
            Assert.True(weight > 0.0, "A pool entry weighing " + weight + " could never be drawn.");
        }

        /// <summary>
        /// A negative streak cannot arise from the assembler — it only ever increments — but a
        /// hand-edited sidecar can carry one, and it must not read as a weight below the floor.
        /// </summary>
        [Fact]
        public void Weight_TreatsANegativeStreakAsNoWaitAtAll()
        {
            Assert.Equal(Weigh(0), Weigh(-3), 9);
        }

        // --- the total order ----------------------------------------------------------------------

        /// <summary>
        /// Weight descending is the first key: <see cref="EventPoolWeighting.Compare"/> orders the
        /// heavier entry first, so a negative result means "a sorts before b".
        /// </summary>
        [Fact]
        public void Compare_OrdersHeavierEntriesFirst()
        {
            EventPoolEntry a = StoryTestFixtures.Pooled("evt-a", 0);
            EventPoolEntry b = StoryTestFixtures.Pooled("evt-b", 0);

            Assert.True(EventPoolWeighting.Compare(a, 9.0, b, 1.0) < 0);
            Assert.True(EventPoolWeighting.Compare(a, 1.0, b, 9.0) > 0);
        }

        /// <summary>
        /// <b>The second key, on a fixture that ties on the first.</b> Two entries of equal weight are
        /// separated by <c>MissStreak</c> descending — the entry that has been passed over more often
        /// comes first.
        /// </summary>
        [Fact]
        public void Compare_BreaksAWeightTieOnMissStreakDescending()
        {
            EventPoolEntry patient = StoryTestFixtures.Pooled("evt-b", missStreak: 5);
            EventPoolEntry fresh = StoryTestFixtures.Pooled("evt-a", missStreak: 1);

            // The id order deliberately opposes the streak order, so a comparer that skipped straight
            // to the id would answer the other way round.
            Assert.True(EventPoolWeighting.Compare(patient, 3.0, fresh, 3.0) < 0);
            Assert.True(EventPoolWeighting.Compare(fresh, 3.0, patient, 3.0) > 0);
        }

        /// <summary>
        /// <b>The third key, on a fixture that ties on both of the others.</b> An id-ordinal final key
        /// is what makes the order <i>total</i> rather than merely mostly-defined — without it,
        /// "which minor gets promoted" falls back to collection order, which is the determinism bug
        /// <c>Agora.Core/CLAUDE.md</c> names as the most common one.
        /// </summary>
        [Fact]
        public void Compare_BreaksAFullTieOnEventIdOrdinalAscending()
        {
            EventPoolEntry first = StoryTestFixtures.Pooled("evt-aardvark", missStreak: 4);
            EventPoolEntry second = StoryTestFixtures.Pooled("evt-zebra", missStreak: 4);

            Assert.True(EventPoolWeighting.Compare(first, 3.0, second, 3.0) < 0);
            Assert.True(EventPoolWeighting.Compare(second, 3.0, first, 3.0) > 0);
        }

        /// <summary>
        /// Ordinal, not culture-aware. The ids are engine identifiers and a culture-sensitive compare
        /// would order them differently on a different machine — a desync that reproduces on nobody
        /// else's save.
        /// </summary>
        [Fact]
        public void Compare_UsesOrdinalIdComparison()
        {
            EventPoolEntry upper = StoryTestFixtures.Pooled("EVT-b", missStreak: 0);
            EventPoolEntry lower = StoryTestFixtures.Pooled("evt-a", missStreak: 0);

            // Ordinal puts every uppercase letter before every lowercase one; a linguistic compare
            // would put "evt-a" first.
            Assert.True(EventPoolWeighting.Compare(upper, 1.0, lower, 1.0) < 0);
        }

        /// <summary>An entry compared with itself is a tie, which is what makes the order reflexive.</summary>
        [Fact]
        public void Compare_ReportsATieOnlyForIdenticalEntries()
        {
            EventPoolEntry entry = StoryTestFixtures.Pooled("evt-a", missStreak: 2);

            Assert.Equal(0, EventPoolWeighting.Compare(entry, 1.0, entry, 1.0));
            Assert.NotEqual(0, EventPoolWeighting.Compare(
                entry, 1.0, StoryTestFixtures.Pooled("evt-b", missStreak: 2), 1.0));
        }

        /// <summary>
        /// The order is total, so sorting the same set from two different starting arrangements has to
        /// land on the same sequence. This is the property the assembler actually depends on: the pool
        /// arrives from a sidecar list whose order nothing guarantees.
        /// </summary>
        [Fact]
        public void Compare_SortsTwoArrangementsOfOneSetIdentically()
        {
            var entries = new List<EventPoolEntry>
            {
                StoryTestFixtures.Pooled("evt-a", 4),
                StoryTestFixtures.Pooled("evt-b", 4),
                StoryTestFixtures.Pooled("evt-c", 0),
                StoryTestFixtures.Pooled("evt-d", 4),
                StoryTestFixtures.Pooled("evt-e", 8)
            };

            var reversed = new List<EventPoolEntry>(entries);
            reversed.Reverse();

            Assert.Equal(SortedIds(entries), SortedIds(reversed));
        }

        private static List<string> SortedIds(List<EventPoolEntry> entries)
        {
            var copy = new List<EventPoolEntry>(entries);
            copy.Sort((a, b) => EventPoolWeighting.Compare(
                a, Weigh(a.MissStreak), b, Weigh(b.MissStreak)));

            var ids = new List<string>();
            foreach (EventPoolEntry entry in copy) ids.Add(entry.EventId);
            return ids;
        }

        /// <summary>
        /// Antisymmetry across every pair in a mixed set. <c>List.Sort</c> is free to throw
        /// <see cref="InvalidOperationException"/> on an inconsistent comparer, and it does so
        /// nondeterministically depending on partition order — so an asymmetric compare would surface
        /// as an intermittent failure somewhere else entirely.
        /// </summary>
        [Fact]
        public void Compare_IsAntisymmetric()
        {
            var entries = new List<EventPoolEntry>
            {
                StoryTestFixtures.Pooled("evt-a", 0),
                StoryTestFixtures.Pooled("evt-b", 4),
                StoryTestFixtures.Pooled("evt-c", 4),
                StoryTestFixtures.Pooled("evt-d", 8)
            };

            for (int i = 0; i < entries.Count; i++)
            {
                for (int j = 0; j < entries.Count; j++)
                {
                    double wi = Weigh(entries[i].MissStreak);
                    double wj = Weigh(entries[j].MissStreak);

                    int forward = EventPoolWeighting.Compare(entries[i], wi, entries[j], wj);
                    int backward = EventPoolWeighting.Compare(entries[j], wj, entries[i], wi);

                    Assert.Equal(Math.Sign(forward), -Math.Sign(backward));
                }
            }
        }
    }
}
