using System.Collections.Generic;
using Agora.Core.Contracts;
using Colossal.Entities;
using Game.Areas;
using Game.City;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

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
    ///
    /// <para>
    /// <b>Attractiveness is stored raw.</b> It is a dimensionless index — a sum of squared
    /// per-building contributions, not a percentage and not a share — so it is not normalised the way
    /// the pollution and traffic readings are. It is also the exact quantity the shipped
    /// <c>city-attractiveness</c> effect moves, which makes trigger and effect two ends of one
    /// number; normalising it against an invented reference maximum would break that
    /// correspondence and nothing downstream would report it.
    /// </para>
    ///
    /// <para>
    /// <b>The per-district attraction figures are not the city's attractiveness.</b> They are honest
    /// counts of real entities in a district. The city's index is computed differently and exists
    /// only on the city entity, so the two must never be labelled or substituted for each other.
    /// </para>
    /// </summary>
    /// <remarks>
    /// This system emits <b>no</b> landmark count: the game has no landmark concept — the only two
    /// occurrences of the word in <c>Game.dll</c> are DLC id lines (scout 0004 §5.3) — and the field
    /// is named <c>SignatureBuildingCount</c> for what is actually counted, the
    /// <c>Game.Buildings.Signature</c> tag.
    /// </remarks>
    public sealed partial class AgoraTourismSensorSystem : AgoraSensorSystemBase
    {
        private CityStatisticsSystem _statistics;
        private CitySystem _citySystem;
        private AgoraDistrictSensorSystem _districtSensor;

        private EntityQuery _attractionQuery;
        private EntityQuery _signatureQuery;

        private readonly CityReading _city = new CityReading();

        private readonly Dictionary<string, DistrictReading> _districts =
            new Dictionary<string, DistrictReading>();

        /// <summary>City-wide tourism levels from the most recent sample.</summary>
        public CityReading City => _city;

        /// <summary>Per-district attraction and signature counts, keyed by district id.</summary>
        public IReadOnlyDictionary<string, DistrictReading> Districts => _districts;

        protected override void CreateQueries()
        {
            _statistics = World.GetOrCreateSystemManaged<CityStatisticsSystem>();
            _citySystem = World.GetOrCreateSystemManaged<CitySystem>();
            _districtSensor = World.GetOrCreateSystemManaged<AgoraDistrictSensorSystem>();

            // All three exclusions, copying the game's own AttractionSystem query
            // (AttractionSystem.cs:216-230). Each one keeps out a building that would otherwise move
            // the count for a reason that is not an event: Temp is a placement preview the player is
            // still dragging around, Deleted is already bulldozed, and Destroyed is burnt out or
            // collapsed — the game's own attractiveness job never visits it, so counting it here
            // would make Agora's figure disagree with the one the player sees in the tourism
            // infoview. Without Temp in particular, a trigger would fire on the player opening a
            // build menu.
            _attractionQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Buildings.AttractivenessProvider>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Destroyed>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });

            _signatureQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Buildings.Signature>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Destroyed>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
        }

        public override void Invalidate()
        {
            base.Invalidate();
            _districts.Clear();
        }

        protected override void Sample(SimDate date)
        {
            _city.Tourism = ReadTourism();
            CountBuildings(date);
        }

        /// <summary>
        /// Tourists and lodging from the city statistics, attractiveness off the city entity.
        /// </summary>
        /// <remarks>
        /// The <c>Tourism</c> component is read exactly as the game's own tourism infoview reads it
        /// (<c>TourismInfoviewUISystem.cs:158</c>). When it is absent the whole reading is left null
        /// rather than filled in with a zero attractiveness: null means "not measured" here, and a
        /// fabricated zero on the one field the <c>city-attractiveness</c> effect moves is the worst
        /// possible place to invent a number.
        /// </remarks>
        private TourismLevels? ReadTourism()
        {
            Tourism tourism;
            if (!EntityManager.TryGetComponent(_citySystem.City, out tourism)) return null;

            long tourists = _statistics.GetStatisticValueLong(StatisticType.TouristCount);
            long lodgingUsed = _statistics.GetStatisticValueLong(StatisticType.LodgingUsed);
            long lodgingTotal = _statistics.GetStatisticValueLong(StatisticType.LodgingTotal);

            return new TourismLevels(
                (int)tourists, tourism.m_Attractiveness, (int)lodgingUsed, (int)lodgingTotal);
        }

        /// <summary>
        /// Counts attractions and signature buildings once for the city and once per district.
        /// </summary>
        /// <remarks>
        /// No game system exposes either count, so this is a direct count of the right components
        /// rather than a proxy. It deliberately ignores <c>MaxBuildingsPerCapture</c>: subsampling
        /// preserves shares and averages but destroys absolute counts, which is what these are, and
        /// both queries are narrow enough that the walk is a fraction of the residents pass.
        /// </remarks>
        private void CountBuildings(SimDate date)
        {
            _districtSensor.EnsureSampled(date);
            IReadOnlyList<DistrictEntry> districts = _districtSensor.Districts;

            // Every known district starts at a measured zero. A district with no attractions has
            // genuinely zero of them — leaving it null would make assembly fall back to the city
            // count, which would report the whole city's attractions as if they stood in one district.
            var attractionsByDistrict = new Dictionary<Entity, int>();
            var signaturesByDistrict = new Dictionary<Entity, int>();
            for (int i = 0; i < districts.Count; i++)
            {
                attractionsByDistrict[districts[i].Entity] = 0;
                signaturesByDistrict[districts[i].Entity] = 0;
            }

            _city.AttractionCount = CountByDistrict(_attractionQuery, attractionsByDistrict);
            _city.SignatureBuildingCount = CountByDistrict(_signatureQuery, signaturesByDistrict);

            _districts.Clear();
            for (int i = 0; i < districts.Count; i++)
            {
                DistrictEntry entry = districts[i];

                _districts[entry.Id] = new DistrictReading
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    AttractionCount = attractionsByDistrict[entry.Entity],
                    SignatureBuildingCount = signaturesByDistrict[entry.Entity],
                };
            }
        }

        /// <summary>
        /// Walks <paramref name="query"/> once, adding each entity to its district's tally, and
        /// returns the city-wide total.
        /// </summary>
        /// <remarks>
        /// The city figure is the length of the same array the district tallies are built from, so
        /// the two can never disagree. It is a total, not a sum of the districts: a building outside
        /// every district counts for the city and for no district, which is the truth.
        /// </remarks>
        private int CountByDistrict(EntityQuery query, Dictionary<Entity, int> tally)
        {
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.TempJob);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    CurrentDistrict currentDistrict;
                    if (!EntityManager.TryGetComponent(entities[i], out currentDistrict)) continue;

                    int count;
                    if (tally.TryGetValue(currentDistrict.m_District, out count))
                    {
                        tally[currentDistrict.m_District] = count + 1;
                    }
                }

                return entities.Length;
            }
            finally
            {
                entities.Dispose();
            }
        }
    }
}
