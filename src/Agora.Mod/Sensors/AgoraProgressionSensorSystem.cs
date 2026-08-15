using Agora.Core.Contracts;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// The progression sensor: milestone level, experience, milestone progress, unlocked features and
    /// the per-resource tax rates.
    ///
    /// <para>
    /// City-only in its entirety, and not merely for want of a district query — a district has no
    /// milestone, and <c>TaxSystem</c> exposes no per-district, per-resource overload
    /// (<c>docs/scout/0004-city-statistics.md</c> §6, §8). This system therefore has no
    /// <c>Districts</c> property at all, which is a more honest statement than an always-empty one.
    /// </para>
    /// </summary>
    /// <remarks>
    /// AGORA-SEAM(wave-1/1b). A compiling stub; the real deliverable is the sampling body — see
    /// <c>docs/plans/0004-wave-1-lanes.md</c> row 1b and scout 0004 §6, §8, §9.
    /// </remarks>
    public sealed partial class AgoraProgressionSensorSystem : AgoraSensorSystemBase
    {
        private readonly CityReading _city = new CityReading();

        /// <summary>City progression and tax detail from the most recent sample.</summary>
        public CityReading City => _city;

        protected override void CreateQueries()
        {
            // AGORA-SEAM(wave-1/1b): resolve MilestoneSystem, CitySystem, PrefabSystem and TaxSystem,
            // and build the MilestoneLevel and (FeatureData, PrefabData) queries here.
        }

        protected override void Sample(SimDate date)
        {
            // AGORA-SEAM(wave-1/1b): fill _city.Progression, _city.UnlockedFeatureIds and
            // _city.IndustryTaxRates.
        }
    }
}
