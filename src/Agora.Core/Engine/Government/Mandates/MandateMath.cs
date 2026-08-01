using System;

namespace Agora.Core.Engine.Government.Mandates
{
    /// <summary>
    /// Numeric helpers shared by the mandate packet. netstandard2.0 has no <c>Math.Clamp</c>, so the
    /// polyfill lives here rather than the target framework being raised (see <c>src/CLAUDE.md</c>).
    /// </summary>
    internal static class MandateMath
    {
        /// <summary>
        /// Below this, a baseline and a target count as the same number. Not a tuning coefficient —
        /// it is the floating-point resolution guard that keeps a zero-span target from producing
        /// infinity or NaN.
        /// </summary>
        internal const double Epsilon = 1e-9;

        /// <summary>
        /// Clamps, and maps NaN to <paramref name="min"/>. Without the NaN case every comparison is
        /// false and the NaN escapes the clamp — which is how one bad sensor reading would poison
        /// every downstream sum.
        /// </summary>
        internal static double Clamp(double v, double min, double max)
        {
            if (double.IsNaN(v)) return min;
            return v < min ? min : (v > max ? max : v);
        }

        internal static double Clamp01(double v) => Clamp(v, 0.0, 1.0);

        /// <summary>Clamps to <c>[0, 1]</c>, treating a non-finite value as 0.</summary>
        internal static double SafeProgress(double v) => IsFinite(v) ? Clamp01(v) : 0.0;

        internal static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        /// <summary>
        /// Fraction of the way from baseline to target, clamped to <c>[0, 1]</c>. Works for both
        /// directions because the span carries the sign: backsliding past the baseline reads 0, and
        /// overshooting the target reads 1.
        /// </summary>
        internal static double Progress(double baseline, double target, double current)
        {
            if (!IsFinite(baseline) || !IsFinite(target) || !IsFinite(current)) return 0.0;

            double span = target - baseline;
            if (Math.Abs(span) < Epsilon)
            {
                // Degenerate promise: the target was already the baseline, so it is trivially met.
                return 1.0;
            }

            return Clamp01((current - baseline) / span);
        }
    }
}
