using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Stories
{
    /// <summary>
    /// The single sorted registry mapping metric ids onto <see cref="CitySnapshot"/> and
    /// <see cref="DistrictSnapshot"/> accessors.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AGORA-SEAM(wave-2/2a) — <b>this is a stub.</b> Lane 2a delivers the real registry: the full id
    /// list, the accessors, and the measurability rules. The signatures are landed here so lanes 2b,
    /// 2c and 2e build from commit one; a caller that compiles against this today gets the right
    /// shape and the wrong answers.
    /// </para>
    /// <para>
    /// <b>The ids are a contract shared with a file this assembly cannot see.</b>
    /// <c>Agora.Mod.Sensors.MetricNames</c> is the vocabulary wave 1 shipped — 18 city-scope names
    /// and 3 district-scope ones — and <c>MetricHistory</c> keys its series on those exact strings.
    /// <c>Agora.Core</c> may never reference <c>Agora.Mod</c>, so this registry necessarily holds a
    /// second copy of them, and two copies drift. The pin is a test: the suite compile-links
    /// <c>MetricHistory.cs</c>, so it can compare the two lists directly, and lane 2e owns that test.
    /// A name may be <b>added but never renamed</b> without a migration — the sidecar fingerprint is
    /// taken over these strings sorted, the same rule that governs a seed stream name.
    /// </para>
    /// <para>
    /// <b>A <c>null</c> reading means unmeasurable and never zero.</b> That distinction is the whole
    /// reason <see cref="CheckResult.Unmeasurable"/> exists, and it cannot be recovered downstream
    /// once it has been flattened to a number.
    /// </para>
    /// </remarks>
    public static class MetricRegistry
    {
        /// <summary>Every city-scope metric id, sorted ordinal.</summary>
        public static IReadOnlyList<string> CityMetricIds
        {
            // AGORA-SEAM(wave-2/2a): lane 2a fills this from Agora.Mod.Sensors.MetricNames.
            get { return new List<string>(); }
        }

        /// <summary>Every district-scope metric id, sorted ordinal.</summary>
        public static IReadOnlyList<string> DistrictMetricIds
        {
            // AGORA-SEAM(wave-2/2a)
            get { return new List<string>(); }
        }

        /// <summary>
        /// True when <paramref name="metricId"/> is readable at the given scope. This is what makes
        /// an unreachable trigger a <b>load-time catalog error</b> rather than a runtime surprise.
        /// </summary>
        public static bool IsKnown(string metricId, TriggerScope scope)
        {
            // AGORA-SEAM(wave-2/2a)
            return false;
        }

        /// <summary>
        /// The city-wide reading, or null when it cannot be read.
        /// </summary>
        public static double? ReadCity(CitySnapshot snapshot, string metricId)
        {
            // AGORA-SEAM(wave-2/2a)
            return null;
        }

        /// <summary>
        /// One district's reading, or null when it cannot be read.
        /// </summary>
        /// <remarks>
        /// Null is the answer for a district whose <c>CityFallbackFields</c> names this metric: a
        /// value copied down from the city is not a measurement of the district, and scoring against
        /// it would charge the player for a sensor gap. Note this marker is only trustworthy on a
        /// <i>live</i> snapshot — a rehydrated district reports no fallbacks whatever the original
        /// month looked like.
        /// </remarks>
        public static double? ReadDistrict(DistrictSnapshot district, string metricId)
        {
            // AGORA-SEAM(wave-2/2a)
            return null;
        }
    }
}
