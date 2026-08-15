using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// The tourism sensor: tourists, attractiveness, lodging, and the attraction and
    /// signature-building counts.
    ///
    /// <para>
    /// The first four are city-only — tourist and lodging counts come from
    /// <c>CityStatisticsSystem</c>, and <c>Game.City.Tourism</c> exists on the city entity and
    /// nowhere else. The two building counts are genuinely per-district, because the placed
    /// instances carry <c>Game.Areas.CurrentDistrict</c>
    /// (<c>docs/scout/0004-city-statistics.md</c> §5).
    /// </para>
    /// </summary>
    /// <remarks>
    /// AGORA-SEAM(wave-1/1c). A compiling stub; the real deliverable is the sampling body — see
    /// <c>docs/plans/0004-wave-1-lanes.md</c> row 1c and scout 0004 §5, §9. Note that this system
    /// emits <b>no</b> landmark count: the game has no landmark concept, and the field is named
    /// <c>SignatureBuildingCount</c> for what is actually counted.
    /// </remarks>
    public sealed partial class AgoraTourismSensorSystem : AgoraSensorSystemBase
    {
        private readonly CityReading _city = new CityReading();

        private readonly Dictionary<string, DistrictReading> _districts =
            new Dictionary<string, DistrictReading>();

        /// <summary>City-wide tourism levels from the most recent sample.</summary>
        public CityReading City => _city;

        /// <summary>Per-district attraction and signature counts, keyed by district id.</summary>
        public IReadOnlyDictionary<string, DistrictReading> Districts => _districts;

        protected override void CreateQueries()
        {
            // AGORA-SEAM(wave-1/1c): resolve CityStatisticsSystem and CitySystem, and build the
            // AttractivenessProvider and Signature queries here — both with Exclude<Temp> and
            // Exclude<Deleted>, copying the game's own AttractionSystem query.
        }

        protected override void Sample(SimDate date)
        {
            // AGORA-SEAM(wave-1/1c): fill _city.Tourism, the two counts, and _districts.
        }
    }
}
