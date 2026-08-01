using System.Collections.Generic;
using Agora.Core.Contracts;
using Colossal.Entities;
using Game.Areas;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// The environment sensor: air, ground and noise pollution, plus crime, for the city and for
    /// each district.
    ///
    /// <para>
    /// Pollution is stored as three cell maps covering the whole map, with no per-district rollup
    /// anywhere in the game. So it is sampled where people actually are — at each building's
    /// position — and averaged by that building's district. Averaging the raw map instead would let
    /// an empty forest in the corner of the map dominate a dense district's reading.
    /// </para>
    ///
    /// <para>
    /// <b>Water pollution is not measured in this pass.</b> The game keeps it in the GPU-side water
    /// simulation with no cheap CPU accessor, so <c>PollutionLevels.Water</c> reports 0 everywhere.
    /// That is a known gap, recorded in the packet report rather than papered over with a proxy: a
    /// fabricated water figure would be indistinguishable from a measured one downstream.
    /// </para>
    /// </summary>
    public sealed partial class AgoraEnvironmentSensorSystem : AgoraSensorSystemBase
    {
        private EntityQuery _buildingQuery;

        private AgoraDistrictSensorSystem _districtSensor;
        private CityStatisticsSystem _statistics;
        private GroundPollutionSystem _groundPollution;
        private AirPollutionSystem _airPollution;
        private NoisePollutionSystem _noisePollution;

        private readonly CityReading _city = new CityReading();
        private readonly Dictionary<string, DistrictReading> _byDistrictId =
            new Dictionary<string, DistrictReading>();

        /// <summary>City-wide environment metrics from the most recent sample.</summary>
        public CityReading City => _city;

        /// <summary>Per-district environment metrics from the most recent sample, keyed by district id.</summary>
        public IReadOnlyDictionary<string, DistrictReading> Districts => _byDistrictId;

        protected override void CreateQueries()
        {
            _districtSensor = World.GetOrCreateSystemManaged<AgoraDistrictSensorSystem>();
            _statistics = World.GetOrCreateSystemManaged<CityStatisticsSystem>();
            _groundPollution = World.GetOrCreateSystemManaged<GroundPollutionSystem>();
            _airPollution = World.GetOrCreateSystemManaged<AirPollutionSystem>();
            _noisePollution = World.GetOrCreateSystemManaged<NoisePollutionSystem>();

            _buildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Buildings.Building>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                },
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

            var cityTally = new EnvironmentTally();
            var byDistrict = new Dictionary<Entity, EnvironmentTally>();
            for (int i = 0; i < districts.Count; i++)
            {
                byDistrict[districts[i].Entity] = new EnvironmentTally();
            }

            JobHandle groundDeps, airDeps, noiseDeps;
            NativeArray<GroundPollution> groundMap = _groundPollution.GetMap(true, out groundDeps);
            NativeArray<AirPollution> airMap = _airPollution.GetMap(true, out airDeps);
            NativeArray<NoisePollution> noiseMap = _noisePollution.GetMap(true, out noiseDeps);

            // The maps are written by jobs. Reading them on the simulation thread without completing
            // those jobs first is a race that shows up as a torn value, not as an exception.
            groundDeps.Complete();
            airDeps.Complete();
            noiseDeps.Complete();

            NativeArray<Entity> buildings = _buildingQuery.ToEntityArray(Allocator.TempJob);
            try
            {
                for (int i = 0; i < buildings.Length; i++)
                {
                    Entity building = buildings[i];

                    Game.Objects.Transform transform;
                    if (!EntityManager.TryGetComponent(building, out transform)) continue;

                    float3 position = transform.m_Position;

                    // GetPollution bounds-checks internally and returns zero off-map, so no guard is
                    // needed here — unlike LandValueSystem.GetCellIndex, which does not.
                    double ground = GroundPollutionSystem.GetPollution(position, groundMap).m_Pollution;
                    double air = AirPollutionSystem.GetPollution(position, airMap).m_Pollution;
                    double noise = NoisePollutionSystem.GetPollution(position, noiseMap).m_Pollution;

                    double? crime = null;
                    CrimeProducer crimeProducer;
                    if (EntityManager.TryGetComponent(building, out crimeProducer))
                    {
                        crime = crimeProducer.m_Crime;
                    }

                    cityTally.Add(ground, air, noise, crime);

                    CurrentDistrict currentDistrict;
                    if (!EntityManager.TryGetComponent(building, out currentDistrict)) continue;

                    EnvironmentTally districtTally;
                    if (byDistrict.TryGetValue(currentDistrict.m_District, out districtTally))
                    {
                        districtTally.Add(ground, air, noise, crime);
                    }
                }
            }
            finally
            {
                buildings.Dispose();
            }

            SensorCalibration calibration = Calibration;

            _city.Pollution = cityTally.Pollution(calibration);

            // The city crime rate has a first-class statistic; districts do not, so they fall back to
            // averaging their buildings' crime producers. The two are not the same measurement, which
            // is why the district figure is derived rather than scaled from the city one.
            _city.CrimeRate = ReadCityCrimeRate(calibration);

            _byDistrictId.Clear();
            for (int i = 0; i < districts.Count; i++)
            {
                DistrictEntry entry = districts[i];

                EnvironmentTally tally;
                if (!byDistrict.TryGetValue(entry.Entity, out tally)) continue;

                _byDistrictId[entry.Id] = new DistrictReading
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    Pollution = tally.Pollution(calibration),
                    CrimeRate = tally.CrimeRate(calibration),
                };
            }
        }

        private double? ReadCityCrimeRate(SensorCalibration calibration)
        {
            long raw = _statistics.GetStatisticValueLong(StatisticType.CrimeRate);
            if (raw < 0) return null;
            return SensorMath.Normalize(raw, calibration.CityCrimeStatisticReferenceMax);
        }

        /// <summary>
        /// Running sums for one scope. Reports null rather than zero when nothing was sampled: an
        /// empty district has no pollution reading, which is a different claim from "the air here
        /// is clean".
        /// </summary>
        private sealed class EnvironmentTally
        {
            private double _ground;
            private double _air;
            private double _noise;
            private long _samples;

            private double _crime;
            private long _crimeSamples;

            public void Add(double ground, double air, double noise, double? crime)
            {
                _ground += ground;
                _air += air;
                _noise += noise;
                _samples++;

                if (crime.HasValue)
                {
                    _crime += crime.Value;
                    _crimeSamples++;
                }
            }

            public PollutionLevels? Pollution(SensorCalibration calibration)
            {
                if (_samples <= 0) return null;

                return new PollutionLevels(
                    SensorMath.Normalize(_air / _samples, calibration.AirPollutionReferenceMax),
                    SensorMath.Normalize(_ground / _samples, calibration.GroundPollutionReferenceMax),
                    SensorMath.Normalize(_noise / _samples, calibration.NoisePollutionReferenceMax),
                    // Water: see the class remarks. Not measurable from the CPU in this pass.
                    0.0);
            }

            public double? CrimeRate(SensorCalibration calibration)
            {
                if (_crimeSamples <= 0) return null;
                return SensorMath.Normalize(_crime / _crimeSamples, calibration.CrimeReferenceMax);
            }
        }
    }
}
