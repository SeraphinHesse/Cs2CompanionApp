using System;
using System.Collections.Generic;
using Agora.Core.Tuning;

namespace Agora.Core.Stories
{
    /// <summary>
    /// How likely one pooled event is to be drawn, and the total order that settles ties.
    /// </summary>
    /// <remarks>
    /// AGORA-SEAM(wave-2/2b) — <b>this is a stub.</b> Lane 2b delivers it.
    /// </remarks>
    public static class EventPoolWeighting
    {
        /// <summary>
        /// The draw weight of one pool entry.
        /// </summary>
        /// <remarks>
        /// <b>Monotonic in <c>MissStreak</c>, and lane 2e pins that.</b> An entry that has waited
        /// longer must never weigh less than the same entry having waited less — that is the whole
        /// content of "pity weighting", and it is the property a balance pass could silently break.
        /// The streak contribution saturates at <c>stories.maxMissStreak</c> so an ancient entry
        /// cannot crowd out everything else forever.
        /// </remarks>
        public static double Weight(EventPoolEntry entry, CivicEvent civicEvent, EngineTuning tuning)
        {
            // AGORA-SEAM(wave-2/2b)
            return 0.0;
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
            // AGORA-SEAM(wave-2/2b)
            return 0;
        }
    }
}
