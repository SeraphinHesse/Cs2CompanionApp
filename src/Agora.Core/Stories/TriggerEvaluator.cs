namespace Agora.Core.Stories
{
    /// <summary>
    /// Evaluates a <see cref="TriggerSpec"/> against the city, and a <see cref="CheckSpec"/> against
    /// the city plus the slot's baseline. <b>One implementation, two callers.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// AGORA-SEAM(wave-2/2a) — <b>this is a stub.</b> Lane 2a delivers it. Lanes 2b and 2c call these
    /// two methods and must not write their own comparison arithmetic: a threshold has to mean the
    /// same thing at draft as at resolution, and two implementations is exactly how that stops being
    /// true.
    /// </para>
    /// <para>
    /// <b>Sorted iteration throughout, no dictionary-order dependence.</b> An <c>AnyDistrict</c> spec
    /// walks districts in sorted id order even though "any" does not care which one matched — because
    /// <see cref="TriggerScope.AnyDistrict"/> at draft feeds a district-targeted effect at
    /// resolution, and "whichever came first" is the determinism bug <c>Agora.Core/CLAUDE.md</c>
    /// calls the most common one.
    /// </para>
    /// </remarks>
    public static class TriggerEvaluator
    {
        /// <summary>
        /// Whether the city satisfies <paramref name="spec"/> as of the context's month.
        /// </summary>
        /// <returns>
        /// <see cref="CheckResult.Met"/>, <see cref="CheckResult.NotMet"/>, or
        /// <see cref="CheckResult.Unmeasurable"/> when the reading is unavailable — which is never
        /// the same as not met.
        /// </returns>
        public static CheckResult Evaluate(TriggerSpec spec, StoryReadContext context)
        {
            // AGORA-SEAM(wave-2/2a)
            return CheckResult.Unmeasurable;
        }

        /// <summary>
        /// Whether a slot the player took a goal on was met.
        /// </summary>
        /// <param name="baseline">
        /// The slot's <c>BaselineMetric</c> — the reading captured when the story opened. Null when
        /// the metric was unreadable then, which makes a <c>RelativeToBaseline</c> check
        /// <see cref="CheckResult.Unmeasurable"/>: there is no honest verdict without the number the
        /// comparison is against.
        /// </param>
        public static CheckResult EvaluateCheck(CheckSpec check, double? baseline,
                                                StoryReadContext context)
        {
            // AGORA-SEAM(wave-2/2a)
            return CheckResult.Unmeasurable;
        }
    }
}
