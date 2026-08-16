using System.Collections.Generic;
using Agora.Core.Tuning;

namespace Agora.Core.Stories
{
    /// <summary>
    /// Turns stories into what the voter model reads: salience from the catalog, credit from the
    /// verdict.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>AGORA-SEAM(wave-4/4c).</b> The body returns an empty list, so the spine's affinity term
    /// compiles and is inert until this lands. <b>Not finished work</b> — and the second half below
    /// is the rework's central mechanism, not a detail.
    /// </para>
    ///
    /// <para><b>Salience — read, not invented.</b></para>
    /// <para>
    /// A live slot contributes its event's <see cref="CivicEvent.ActivePressure"/>; a slot that
    /// resolved met contributes <see cref="CivicEvent.SuccessPressure"/>, one that resolved not-met
    /// <see cref="CivicEvent.FailurePressure"/>. All three point the same way on each axis by
    /// construction and differ only in magnitude — that is machine-checked at load time as
    /// <c>PressureSignFlip</c>, so nothing here needs to re-check it and nothing here may negate one.
    /// An unmeasurable slot contributes nothing at all.
    /// </para>
    ///
    /// <para><b>Credit — derived, and nothing in any catalog expresses it.</b></para>
    /// <para>
    /// This is the half the wave-3 ruling left to wave 4, and the plan's sentence about effects that
    /// "alienate or enfranchise" is about exactly this. What must be true: a story the government
    /// delivered on pulls voters <i>toward</i> whoever is governing, and one it failed pushes them
    /// away — regardless of which issues the story was about, and regardless of which party happens
    /// to agree with those issues. <c>stories.enfranchisementWeight</c> and
    /// <c>stories.alienationWeight</c> are the two dials, and their own doc comments already define
    /// them as "how far a met outcome pulls voters toward the government". Both have existed since
    /// wave 2 and neither has ever been read.
    /// </para>
    /// <para>
    /// Scale credit by what was actually at stake — the slot's tier, which is the severity projection
    /// and not a second magnitude — and let it be zero while a story is still live: the city has not
    /// yet learned whether the mayor delivered. Bound the summed result to <c>[-1, +1]</c> before it
    /// leaves, for the same reason <c>AffinityEngine.EventTerm</c> clamps before weighting: without a
    /// bound a busy cycle drowns every other term and the model stops discriminating between a flood
    /// and a bus-fare rise.
    /// </para>
    /// <para>
    /// <b>Zero credit when nobody governs.</b> There is no one to reward during a caretaker gap.
    /// </para>
    ///
    /// <para><b>Determinism.</b></para>
    /// <para>
    /// Walk the story lists in the order given and the slots in the story's own order; sum in that
    /// declared order and sort the result by <c>StoryId</c> ordinal before returning. Prefer
    /// <c>double</c> and never accumulate across an unordered collection.
    /// </para>
    /// </remarks>
    public static class StoryPressure
    {
        /// <summary>
        /// One contribution per story that should move the voter model this tick — the open ones and
        /// the ones that reached a verdict on this very tick.
        /// </summary>
        /// <param name="live">Stories still open, sorted by <c>Id</c> ordinal.</param>
        /// <param name="justResolved">
        /// Stories that reached a verdict on this tick. Separate from <paramref name="live"/> because
        /// a verdict lands in the same tick the pressures it changes are read — that ordering is why
        /// the story stage sits before affinity rather than after it.
        /// </param>
        public static List<StoryPressureContribution> For(IReadOnlyList<Story> live,
                                                          IReadOnlyList<Story> justResolved,
                                                          IReadOnlyList<CivicEvent> catalog,
                                                          EngineTuning tuning)
        {
            // AGORA-SEAM(wave-4/4c): empty, so the affinity term reads nothing and moves nothing.
            return new List<StoryPressureContribution>();
        }
    }
}
