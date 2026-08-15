using System.Collections.Generic;
using Agora.Core.Tuning;

namespace Agora.Core.Stories
{
    /// <summary>
    /// The 2-of-3 rule and its edge cases.
    /// </summary>
    /// <remarks>
    /// AGORA-SEAM(wave-2/2c) — <b>this is a stub.</b> Lane 2c delivers it.
    /// </remarks>
    public static class StoryResolution
    {
        /// <summary>
        /// Scores every slot, then the story.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Per-slot verdict by response mode.</b> <c>Goal</c> runs the <see cref="CheckSpec"/>
        /// through <see cref="TriggerEvaluator"/>. <c>PowerOverride</c> is an automatic success that
        /// was already paid for. <c>Ignore</c> is an automatic failure — the player decided.
        /// <c>Manual</c> reads the player's own declaration and is <b>neutral until declared</b>,
        /// which is to say <see cref="SlotOutcome.Unmeasurable"/> rather than failed.
        /// <c>Unaddressed</c> is silence, not a decision, and is likewise not scored as failure.
        /// </para>
        /// <para>
        /// <b>The story threshold is a ratio over SCORED slots, not over all of them.</b> A full
        /// story of three needs <c>stories.successThreshold</c> met; a story of fewer than three —
        /// a degraded draft, or one whose slots went unmeasurable — needs <b>all</b> its scored slots
        /// met. A story with no scored slots at all resolves <see cref="StoryOutcome.Abandoned"/>:
        /// there is nothing to have a verdict about, and calling that a failure would charge the
        /// player for a sensor gap.
        /// </para>
        /// </remarks>
        public static StoryResolutionResult Resolve(Story story,
                                                    IReadOnlyList<CivicEvent> catalog,
                                                    StoryReadContext context,
                                                    EngineTuning tuning)
        {
            // AGORA-SEAM(wave-2/2c)
            return new StoryResolutionResult();
        }
    }
}
