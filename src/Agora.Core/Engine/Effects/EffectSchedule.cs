using System;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Effects
{
    /// <summary>
    /// The time half of an effect: how long it lives, how it fades, and which months it is re-applied on.
    ///
    /// <para>
    /// Stateless by design. Nothing here remembers an active effect — an effect's life is a pure
    /// function of (start date, duration, tuning, today), so a reload recomputes exactly what a
    /// continuous session would have (non-negotiable #3, #6). All dates are <see cref="SimDate"/>;
    /// nothing here computes a year (non-negotiable #8).
    /// </para>
    /// </summary>
    public static class EffectSchedule
    {
        /// <summary>Whole months from <paramref name="start"/> to <paramref name="now"/>; negative before it began.</summary>
        public static int ElapsedMonths(SimDate start, SimDate now) => start.MonthsUntil(now);

        /// <summary>The month the effect stops applying.</summary>
        public static SimDate ExpiryDate(SimDate start, int durationMonths) =>
            start.AddMonths(durationMonths < 0 ? 0 : durationMonths);

        /// <summary>
        /// True while the effect is live: on or after its start month and strictly before its expiry.
        /// A zero-month effect is never live.
        /// </summary>
        public static bool IsActive(SimDate start, int durationMonths, SimDate now)
        {
            if (durationMonths <= 0) return false;
            int elapsed = ElapsedMonths(start, now);
            return elapsed >= 0 && elapsed < durationMonths;
        }

        /// <summary>
        /// How much of the original magnitude survives, in <c>[0, 1]</c>.
        ///
        /// <list type="bullet">
        /// <item><c>linear</c> — straight ramp from 1 at the start to 0 at expiry.</item>
        /// <item><c>exponential</c> — halves every <c>effects.decayHalfLifeMonths</c>, cut to 0 at expiry.</item>
        /// <item><c>step</c> — full strength throughout, then 0 at expiry.</item>
        /// </list>
        ///
        /// An unrecognised curve name is treated as <c>linear</c>, the shipped default.
        /// </summary>
        public static double DecayFactor(EffectsTuning tuning, int elapsedMonths, int durationMonths)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            if (durationMonths <= 0) return 0.0;
            if (elapsedMonths >= durationMonths) return 0.0;
            if (elapsedMonths <= 0) return 1.0;

            string curve = tuning.DecayCurve ?? "";

            if (string.Equals(curve, "step", StringComparison.Ordinal))
                return 1.0;

            if (string.Equals(curve, "exponential", StringComparison.Ordinal))
            {
                double halfLife = tuning.DecayHalfLifeMonths;
                if (halfLife <= 0.0 || double.IsNaN(halfLife)) return 1.0; // no half-life declared: no decay
                double factor = Math.Pow(0.5, elapsedMonths / halfLife);
                return Clamp01(factor);
            }

            // linear, and anything unrecognised
            return Clamp01(1.0 - ((double)elapsedMonths / durationMonths));
        }

        /// <summary>
        /// The decayed magnitude at <paramref name="now"/>, or exactly zero once the effect has expired
        /// or before it starts. Sign is preserved: decay shrinks toward zero, it never flips.
        /// </summary>
        public static double MagnitudeAt(EffectsTuning tuning, double baseMagnitude,
                                         SimDate start, int durationMonths, SimDate now)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            if (double.IsNaN(baseMagnitude)) return 0.0;
            if (!IsActive(start, durationMonths, now)) return 0.0;
            return baseMagnitude * DecayFactor(tuning, ElapsedMonths(start, now), durationMonths);
        }

        /// <summary>
        /// Whether this month is a re-apply month. Game modifiers do not persist Agora's intent, so a
        /// live effect is re-asserted every <c>effects.reapplyIntervalMonths</c> months from its start.
        /// </summary>
        public static bool IsReapplyMonth(EffectsTuning tuning, SimDate start, int durationMonths, SimDate now)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            if (!IsActive(start, durationMonths, now)) return false;

            int interval = tuning.ReapplyIntervalMonths;
            if (interval <= 1) return true;

            return ElapsedMonths(start, now) % interval == 0;
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value)) return 0.0;
            if (value < 0.0) return 0.0;
            return value > 1.0 ? 1.0 : value;
        }
    }
}
