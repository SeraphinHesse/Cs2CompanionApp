namespace Agora.Core.Engine.Blocs
{
    /// <summary>
    /// Small numeric helpers for the bloc packet.
    /// </summary>
    /// <remarks>
    /// <c>Math.Clamp</c> does not exist on netstandard2.0, which is Core's target and is not
    /// negotiable (the toolchain builds <c>Agora.Mod</c> as <c>net48</c>). Polyfilled privately here
    /// rather than raising the target, per <c>src/CLAUDE.md</c>.
    ///
    /// <para>
    /// The means are unweighted on purpose. A weighted blend of sub-signals would be a tuning
    /// coefficient, and coefficients live in <c>data/engine_tuning.json</c>, never in code.
    /// </para>
    /// </remarks>
    internal static class BlocMath
    {
        internal static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        internal static double Clamp01(double value)
        {
            // NaN never satisfies a comparison, so test for it explicitly rather than letting it
            // through: one NaN in a weight vector poisons every later sum in the district.
            if (double.IsNaN(value)) return 0.0;
            return Clamp(value, 0.0, 1.0);
        }

        internal static double Mean(double a, double b)
        {
            return (a + b) / 2.0;
        }

        internal static double Mean(double a, double b, double c)
        {
            return (a + b + c) / 3.0;
        }
    }
}
