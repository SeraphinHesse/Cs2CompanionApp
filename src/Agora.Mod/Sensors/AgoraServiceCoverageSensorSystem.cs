using System.Collections.Generic;
using Agora.Core.Contracts;
using Colossal.Entities;
using Game.Areas;
using Game.Common;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// The service-coverage sensor: how well served each district is, and the city as a whole.
    ///
    /// <para>
    /// Coverage is not stored on buildings. The game keeps a <c>Game.Net.ServiceCoverage</c> buffer
    /// on each <b>road edge</b>, indexed by <c>CoverageService</c>, and a building reads its own
    /// figure by interpolating along that edge at its <c>Building.m_CurvePosition</c>. This sensor
    /// does the same walk the happiness system does, then averages by district.
    /// </para>
    ///
    /// <para>
    /// <b>Four of the contract's nine coverage fields are not measured in this pass.</b> The game
    /// exposes exactly five coverage services — healthcare, education, police, fire and parks — and
    /// has no coverage concept at all for garbage, transit, water or electricity. Those four report
    /// 0 for now. That is a real gap, listed in the packet report; anything else would mean inventing
    /// four numbers and letting the indices packet weigh them as if they had been measured.
    /// </para>
    /// </summary>
    public sealed partial class AgoraServiceCoverageSensorSystem : AgoraSensorSystemBase
    {
        private EntityQuery _buildingQuery;
        private AgoraDistrictSensorSystem _districtSensor;

        private readonly CityReading _city = new CityReading();
        private readonly Dictionary<string, DistrictReading> _byDistrictId =
            new Dictionary<string, DistrictReading>();

        /// <summary>City-wide coverage from the most recent sample.</summary>
        public CityReading City => _city;

        /// <summary>Per-district coverage from the most recent sample, keyed by district id.</summary>
        public IReadOnlyDictionary<string, DistrictReading> Districts => _byDistrictId;

        protected override void CreateQueries()
        {
            _districtSensor = World.GetOrCreateSystemManaged<AgoraDistrictSensorSystem>();

            _buildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Buildings.Building>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
        }

        public override void Invalidate()
        {
            base.Invalidate();
            _byDistrictId.Clear();
        }

        protected override void Sample(SimDate date)
        {
            _districtSensor.EnsureSampled(date);
            IReadOnlyList<DistrictEntry> districts = _districtSensor.Districts;

            var cityTally = new CoverageTally();
            var byDistrict = new Dictionary<Entity, CoverageTally>();
            for (int i = 0; i < districts.Count; i++)
            {
                byDistrict[districts[i].Entity] = new CoverageTally();
            }

            NativeArray<Entity> buildings = _buildingQuery.ToEntityArray(Allocator.TempJob);
            try
            {
                for (int i = 0; i < buildings.Length; i++)
                {
                    Entity building = buildings[i];

                    Game.Buildings.Building buildingData;
                    if (!EntityManager.TryGetComponent(building, out buildingData)) continue;
                    if (buildingData.m_RoadEdge == Entity.Null) continue;

                    DynamicBuffer<Game.Net.ServiceCoverage> coverages;
                    if (!EntityManager.TryGetBuffer(buildingData.m_RoadEdge, true, out coverages)) continue;

                    float curve = buildingData.m_CurvePosition;

                    double health = Game.Net.NetUtils.GetServiceCoverage(coverages, Game.Net.CoverageService.Healthcare, curve);
                    double education = Game.Net.NetUtils.GetServiceCoverage(coverages, Game.Net.CoverageService.Education, curve);
                    double police = Game.Net.NetUtils.GetServiceCoverage(coverages, Game.Net.CoverageService.Police, curve);
                    double fire = Game.Net.NetUtils.GetServiceCoverage(coverages, Game.Net.CoverageService.FireRescue, curve);
                    double parks = Game.Net.NetUtils.GetServiceCoverage(coverages, Game.Net.CoverageService.Park, curve);

                    cityTally.Add(health, education, police, fire, parks);

                    CurrentDistrict currentDistrict;
                    if (!EntityManager.TryGetComponent(building, out currentDistrict)) continue;

                    CoverageTally districtTally;
                    if (byDistrict.TryGetValue(currentDistrict.m_District, out districtTally))
                    {
                        districtTally.Add(health, education, police, fire, parks);
                    }
                }
            }
            finally
            {
                buildings.Dispose();
            }

            SensorCalibration calibration = Calibration;
            _city.Services = cityTally.Coverage(calibration);

            _byDistrictId.Clear();
            for (int i = 0; i < districts.Count; i++)
            {
                DistrictEntry entry = districts[i];

                CoverageTally tally;
                if (!byDistrict.TryGetValue(entry.Entity, out tally)) continue;

                _byDistrictId[entry.Id] = new DistrictReading
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    Services = tally.Coverage(calibration),
                };
            }
        }

        /// <summary>
        /// Running sums for one scope, normalised on read. Null when nothing was sampled — an empty
        /// district is unserved in the sense of "no measurement", not "measured as zero".
        /// </summary>
        private sealed class CoverageTally
        {
            private double _health;
            private double _education;
            private double _police;
            private double _fire;
            private double _parks;
            private long _samples;

            public void Add(double health, double education, double police, double fire, double parks)
            {
                _health += health;
                _education += education;
                _police += police;
                _fire += fire;
                _parks += parks;
                _samples++;
            }

            public Agora.Core.Contracts.ServiceCoverage? Coverage(SensorCalibration calibration)
            {
                if (_samples <= 0) return null;

                double max = calibration.ServiceCoverageReferenceMax;

                // Garbage, transit, water and electricity have no coverage service in the game. See
                // the class remarks — these zeros are a documented gap, not a measurement.
                return new Agora.Core.Contracts.ServiceCoverage(
                    SensorMath.Normalize(_health / _samples, max),
                    SensorMath.Normalize(_education / _samples, max),
                    SensorMath.Normalize(_police / _samples, max),
                    SensorMath.Normalize(_fire / _samples, max),
                    0.0,
                    0.0,
                    0.0,
                    0.0,
                    SensorMath.Normalize(_parks / _samples, max));
            }
        }
    }
}
