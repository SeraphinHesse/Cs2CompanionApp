using System;
using System.Collections.Generic;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// Arithmetic shared by every sensor. Deliberately free of game types so it can be reasoned
    /// about — and eventually tested — without Cities: Skylines II running.
    ///
    /// <para>
    /// Nothing here interprets a number politically. Normalisation is a unit conversion, not a
    /// judgement: it maps a game-native quantity onto the range the snapshot contract declares.
    /// Anything that weighs one metric against another belongs in <c>Agora.Core</c>.
    /// </para>
    ///
    /// <para>
    /// <b>net48 note.</b> <c>Agora.Mod</c> compiles as <c>net48</c> under the modding toolchain, so
    /// <c>Math.Clamp</c> and <c>MathF</c> are unavailable here for the same reason they are
    /// unavailable in Core. Everything below is hand-rolled on purpose.
    /// </para>
    /// </summary>
    internal static class SensorMath
    {
        public static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value)) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static double Clamp01(double value) => Clamp(value, 0.0, 1.0);

        /// <summary>
        /// Division that yields <paramref name="fallback"/> rather than NaN or Infinity when the
        /// denominator is zero. Every ratio in a sensor goes through this: an empty district is the
        /// normal case on a fresh map, not an exceptional one.
        /// </summary>
        public static double SafeDivide(double numerator, double denominator, double fallback = 0.0)
        {
            if (denominator == 0.0 || double.IsNaN(denominator) || double.IsInfinity(denominator))
            {
                return fallback;
            }

            double result = numerator / denominator;
            return double.IsNaN(result) || double.IsInfinity(result) ? fallback : result;
        }

        /// <summary>
        /// Maps a non-negative game-native quantity onto <c>[0, 1]</c> by dividing by
        /// <paramref name="referenceMax"/> and saturating. Used for pollution and coverage, whose
        /// raw units are engine-internal and would otherwise leak into engine tuning.
        /// </summary>
        public static double Normalize(double raw, double referenceMax)
        {
            if (referenceMax <= 0.0) return 0.0;
            return Clamp01(raw / referenceMax);
        }

        /// <summary>
        /// Fractional change from <paramref name="past"/> to <paramref name="present"/>, e.g. 0.10
        /// for a 10% rise. Returns 0 when there is no baseline to compare against, which is the
        /// correct reading for a city with no history yet — not "no change detected".
        /// </summary>
        public static double FractionalChange(double present, double past)
        {
            if (past <= 0.0 || double.IsNaN(past) || double.IsNaN(present)) return 0.0;
            return SafeDivide(present - past, past);
        }

        /// <summary>
        /// Value at <paramref name="quantile"/> of an already-ascending sample, using linear
        /// interpolation between neighbours.
        /// </summary>
        /// <remarks>
        /// The caller sorts. That is deliberate: sorting is where determinism is won or lost, and
        /// hiding it inside a helper makes it easy to forget that ECS chunk order is not stable.
        /// </remarks>
        public static double QuantileOfSorted(IReadOnlyList<double> ascending, double quantile)
        {
            if (ascending == null || ascending.Count == 0) return 0.0;
            if (ascending.Count == 1) return ascending[0];

            double q = Clamp01(quantile);
            double position = q * (ascending.Count - 1);
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            if (lower == upper) return ascending[lower];

            double t = position - lower;
            return ascending[lower] * (1.0 - t) + ascending[upper] * t;
        }

        /// <summary>Arithmetic mean, 0 for an empty sample.</summary>
        public static double Mean(IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0) return 0.0;

            // Summed in index order so a re-run over the same list is bit-identical.
            double total = 0.0;
            for (int i = 0; i < values.Count; i++)
            {
                total += values[i];
            }

            return total / values.Count;
        }

        /// <summary>
        /// Rescales three raw counts into shares that sum to 1. An all-zero input yields all-zero
        /// shares rather than NaN — a district with no residents has no distribution, and inventing
        /// a uniform one would be a fabricated measurement.
        /// </summary>
        public static void SharesOf(long[] counts, double[] shares)
        {
            if (counts == null || shares == null) return;

            long total = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                total += counts[i];
            }

            for (int i = 0; i < shares.Length && i < counts.Length; i++)
            {
                shares[i] = total <= 0 ? 0.0 : (double)counts[i] / total;
            }
        }
    }
}
