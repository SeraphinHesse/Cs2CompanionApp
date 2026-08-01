using Agora.Core.Contracts;
using Game.City;
using Game.Simulation;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// The mobility sensor: public-transit ridership and road congestion.
    ///
    /// <para>
    /// Both are city-wide only. The game aggregates passenger counts per transport type across the
    /// whole city and keeps one average traffic flow figure; neither has a district breakdown, and
    /// there is no honest way to invent one from these inputs. Districts therefore leave both fields
    /// null, and assembly marks them as city fallbacks — which is exactly the case §6's best-effort
    /// rule exists for.
    /// </para>
    ///
    /// <para>
    /// This system builds no <c>EntityQuery</c> at all: everything it reads is a system property or
    /// a city statistic. It is still a <c>GameSystemBase</c> so it inherits the master toggle, the
    /// once-per-day cadence and the fail-closed sampling every other sensor gets.
    /// </para>
    /// </summary>
    public sealed partial class AgoraMobilitySensorSystem : AgoraSensorSystemBase
    {
        private CityStatisticsSystem _statistics;
        private TrafficFlowSystem _trafficFlow;

        private readonly CityReading _city = new CityReading();

        /// <summary>City-wide mobility metrics from the most recent sample.</summary>
        public CityReading City => _city;

        protected override void CreateQueries()
        {
            _statistics = World.GetOrCreateSystemManaged<CityStatisticsSystem>();
            _trafficFlow = World.GetOrCreateSystemManaged<TrafficFlowSystem>();
        }

        protected override void Sample(SimDate date)
        {
            SensorCalibration calibration = Calibration;

            _city.TransitRidership = ReadTransitRidership(calibration);
            _city.TrafficCongestion = SensorMath.Normalize(
                _trafficFlow.cityAverageTrafficFlow, calibration.TrafficFlowReferenceMax);
        }

        /// <summary>
        /// Boardings per resident, rescaled onto the 0–1 share the contract asks for.
        /// </summary>
        /// <remarks>
        /// The contract wants a share of trips; the game only counts absolute boardings, and never
        /// counts car trips at all, so a true modal share is not computable from these inputs. This
        /// is a calibrated proxy and is named as such in the packet report — the one number here a
        /// consumer should not read as a literal percentage of journeys.
        ///
        /// <para>
        /// Taxi, airplane and ship counts are excluded deliberately: they measure tourism and freight
        /// far more than they measure a resident's commute.
        /// </para>
        /// </remarks>
        private double? ReadTransitRidership(SensorCalibration calibration)
        {
            long population = _statistics.GetStatisticValueLong(StatisticType.Population);
            if (population <= 0) return null;

            long boardings =
                _statistics.GetStatisticValueLong(StatisticType.PassengerCountBus) +
                _statistics.GetStatisticValueLong(StatisticType.PassengerCountSubway) +
                _statistics.GetStatisticValueLong(StatisticType.PassengerCountTram) +
                _statistics.GetStatisticValueLong(StatisticType.PassengerCountTrain) +
                _statistics.GetStatisticValueLong(StatisticType.PassengerCountFerry);

            if (boardings < 0) return null;

            double perCapita = SensorMath.SafeDivide(boardings, population);
            return SensorMath.Normalize(perCapita, calibration.TransitBoardingsPerCapitaAtFullRidership);
        }
    }
}
