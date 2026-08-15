using System;
using System.Collections.Generic;
using Agora.Core.Tuning;

namespace Agora.Core.Stories
{
    /// <summary>
    /// How likely one pooled event is to be drawn, and the total order that settles ties.
    /// </summary>
    public static class EventPoolWeighting
    {
        /// <summary>
        /// The base weight every eligible entry carries before the pity term. Not a tuning constant:
        /// it is the multiplicative identity the streak term is added to, and moving it would only
        /// rescale <c>stories.missStreakWeightStep</c>, which is the dial that already exists.
        /// </summary>
        private const double BaseWeight = 1.0;

        /// <summary>
        /// The draw weight of one pool entry.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Monotonic in <c>MissStreak</c>, and lane 2e pins that.</b> An entry that has waited
        /// longer must never weigh less than the same entry having waited less — that is the whole
        /// content of "pity weighting", and it is the property a balance pass could silently break.
        /// The streak contribution saturates at <c>stories.maxMissStreak</c> so an ancient entry
        /// cannot crowd out everything else forever.
        /// </para>
        /// <para>
        /// Monotonicity is held by three things and each is load-bearing: the streak floors at zero,
        /// it saturates at the cap rather than wrapping or decaying past it, and a <b>negative</b>
        /// <c>missStreakWeightStep</c> floors at zero rather than being honoured. A negative step
        /// would invert pity — the longer an entry waited the less it could be drawn — which is not a
        /// balance choice but the feature running backwards, so tuning cannot express it.
        /// </para>
        /// <para>
        /// Severity deliberately does not scale the weight. It already decides the entry's tier
        /// through <see cref="StoryTiers"/>, so majors and minors are drawn from separate pools;
        /// weighting by it as well would count the same number twice, and would need a coefficient
        /// that <c>stories</c> tuning does not define (<c>data/CLAUDE.md</c> rule 4).
        /// </para>
        /// </remarks>
        public static double Weight(EventPoolEntry entry, CivicEvent civicEvent, EngineTuning tuning)
        {
            // An entry whose catalog event has gone — renamed, or dropped from a data pack between
            // sessions — cannot be drawn at all, so it weighs nothing rather than defaulting to base.
            if (entry == null || civicEvent == null) return 0.0;

            StoriesTuning stories = (tuning ?? EngineTuning.Default).Stories;

            int cap = stories.MaxMissStreak < 0 ? 0 : stories.MaxMissStreak;
            int streak = entry.MissStreak < 0 ? 0 : entry.MissStreak;
            if (streak > cap) streak = cap;

            double step = stories.MissStreakWeightStep;
            if (double.IsNaN(step) || step < 0.0) step = 0.0;

            return BaseWeight + step * streak;
        }

        /// <summary>
        /// The declared total order over pool entries: <b>weight descending, then
        /// <c>MissStreak</c> descending, then <c>EventId</c> ordinal ascending.</b>
        /// </summary>
        /// <remarks>
        /// Every selection and every degradation sorts through this. Leaving "which minor gets
        /// promoted" to collection order is the determinism bug <c>Agora.Core/CLAUDE.md</c> names as
        /// the most common one, and an id-ordinal final key is what makes the order <i>total</i>
        /// rather than merely mostly-defined.
        /// </remarks>
        public static int Compare(EventPoolEntry a, double weightA, EventPoolEntry b, double weightB)
        {
            // Null sorts last, and two nulls compare equal — the caller never holds them, but a total
            // order with a hole in it is not a total order.
            if (a == null) return b == null ? 0 : 1;
            if (b == null) return -1;

            int byWeight = weightB.CompareTo(weightA);
            if (byWeight != 0) return byWeight;

            int byStreak = b.MissStreak.CompareTo(a.MissStreak);
            if (byStreak != 0) return byStreak;

            return string.CompareOrdinal(a.EventId ?? "", b.EventId ?? "");
        }

        /// <summary>
        /// Sorts entries into the declared order, weighing each against the catalog as it goes.
        /// </summary>
        /// <param name="entries">Mutated in place. Must already be in a deterministic order.</param>
        /// <param name="eventsById">
        /// The catalog, keyed by id. Read only for the weight — never iterated, because a dictionary
        /// enumeration folded into engine output is the determinism bug this order exists to prevent.
        /// </param>
        internal static void SortByOrder(List<EventPoolEntry> entries,
                                         IDictionary<string, CivicEvent> eventsById,
                                         EngineTuning tuning)
        {
            if (entries == null || entries.Count < 2) return;

            entries.Sort((a, b) => Compare(a, WeightOf(a, eventsById, tuning),
                                           b, WeightOf(b, eventsById, tuning)));
        }

        /// <summary>The weight of one entry against a catalog lookup, unknown ids weighing nothing.</summary>
        internal static double WeightOf(EventPoolEntry entry, IDictionary<string, CivicEvent> eventsById,
                                        EngineTuning tuning)
        {
            if (entry == null || eventsById == null || string.IsNullOrEmpty(entry.EventId)) return 0.0;

            CivicEvent civicEvent;
            return eventsById.TryGetValue(entry.EventId, out civicEvent)
                ? Weight(entry, civicEvent, tuning)
                : 0.0;
        }
    }
}
