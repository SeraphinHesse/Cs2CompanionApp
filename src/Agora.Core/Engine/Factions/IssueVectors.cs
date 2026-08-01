using Agora.Core.Contracts;

namespace Agora.Core.Engine.Factions
{
    /// <summary>
    /// Array ↔ struct helpers for the two six-component issue vectors.
    /// </summary>
    /// <remarks>
    /// The array index is the position in <see cref="Issues.All"/>, which is also the constructor
    /// parameter order of both structs. Accumulating into a <c>double[6]</c> and converting once keeps
    /// the summation order fixed no matter how many terms a caller folds in.
    /// </remarks>
    internal static class IssueVectors
    {
        internal static IssueWeights Weights(double[] v) =>
            new IssueWeights(v[0], v[1], v[2], v[3], v[4], v[5]);

        internal static IssuePosition Position(double[] v) =>
            new IssuePosition(v[0], v[1], v[2], v[3], v[4], v[5]);

        /// <summary>Index of an issue inside <see cref="Issues.All"/>. Equals its enum value, but the
        /// lookup is explicit so a future reordering of the enum cannot silently transpose a vector.</summary>
        internal static int IndexOf(Issue issue)
        {
            for (int i = 0; i < Issues.All.Count; i++)
                if (Issues.All[i] == issue) return i;
            return 0;
        }

        // netstandard2.0 has no Math.Clamp.
        internal static double Clamp(double v, double min, double max) =>
            v < min ? min : (v > max ? max : v);

        internal static double Clamp01(double v) => Clamp(v, 0.0, 1.0);

        internal static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
