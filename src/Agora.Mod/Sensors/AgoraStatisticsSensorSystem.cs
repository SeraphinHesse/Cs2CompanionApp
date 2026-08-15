using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// The city-statistics sensor: homelessness, migration, births and deaths, and garbage.
    ///
    /// <para>
    /// Everything but garbage comes from <c>Game.Simulation.CityStatisticsSystem</c> — the same
    /// source the game's own city statistics screen reads, which is the constraint this wave was
    /// given. All of it is city-only: the statistics system is keyed by
    /// <c>(StatisticType, int parameter)</c> and has no district dimension whatsoever
    /// (<c>docs/scout/0004-city-statistics.md</c> §1.4). Uncollected garbage is the exception and is
    /// measured per district, because the buildings holding it carry <c>Game.Areas.CurrentDistrict</c>.
    /// </para>
    /// </summary>
    /// <remarks>
    /// AGORA-SEAM(wave-1/1a). This is a compiling stub so that lane 1d's merge and assembly work
    /// builds from the spine commit onward. <b>The real deliverable is the whole sampling body</b> —
    /// see <c>docs/plans/0004-wave-1-lanes.md</c> row 1a and scout 0004 §3, §4, §7 and §9. Until it
    /// lands, every reading stays null, which the assembly step already treats as "not measured"
    /// rather than as zero.
    /// </remarks>
    public sealed partial class AgoraStatisticsSensorSystem : AgoraSensorSystemBase
    {
        private readonly CityReading _city = new CityReading();

        private readonly Dictionary<string, DistrictReading> _districts =
            new Dictionary<string, DistrictReading>();

        /// <summary>City-wide statistics from the most recent sample.</summary>
        public CityReading City => _city;

        /// <summary>Per-district uncollected garbage from the most recent sample, keyed by district id.</summary>
        public IReadOnlyDictionary<string, DistrictReading> Districts => _districts;

        protected override void CreateQueries()
        {
            // AGORA-SEAM(wave-1/1a): resolve CityStatisticsSystem, CountHouseholdDataSystem and
            // GarbageAccumulationSystem here, and build the GarbageProducer and StatisticsData
            // queries here — never in Sample.
        }

        protected override void Sample(SimDate date)
        {
            // AGORA-SEAM(wave-1/1a): fill _city.Statistics, _city.UncollectedGarbage and _districts.
        }
    }
}
