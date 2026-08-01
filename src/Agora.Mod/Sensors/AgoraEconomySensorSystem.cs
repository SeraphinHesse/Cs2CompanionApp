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
using Unity.Jobs;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// The economy sensor: the city ledger (money, income, expenses, debt), the four tax rates, and
    /// land value, city-wide and per district. Levels only — the trend fields are computed once, in
    /// <see cref="AgoraSnapshotSystem"/>, so rent and land value cannot end up measured over
    /// different windows.
    ///
    /// <para>
    /// The ledger figures come from <c>CityStatisticsSystem</c>, which exposes
    /// <c>GetStatisticValueLong</c> as a plain public method — no Harmony patch is needed to read the
    /// city's books, which closes Scout 0001's open question 6 for the values Agora needs.
    /// </para>
    ///
    /// <para>
    /// Land value has no per-district aggregate in the game, so it is measured the only honest way
    /// available: sample the land-value cell map at each building's position and average by the
    /// district that building sits in. A district with no buildings yet has no land value — the
    /// reading stays null and falls back, rather than reporting a confident zero.
    /// </para>
    /// </summary>
    public sealed partial class AgoraEconomySensorSystem : AgoraSensorSystemBase
    {
        private EntityQuery _playerMoneyQuery;
        private EntityQuery _buildingQuery;

        private AgoraDistrictSensorSystem _districtSensor;
        private CityStatisticsSystem _statistics;
        private LandValueSystem _landValue;
        private TaxSystem _taxes;
        private Game.Tools.LoanSystem _loans;

        private readonly CityReading _city = new CityReading();
        private readonly Dictionary<string, DistrictReading> _byDistrictId =
            new Dictionary<string, DistrictReading>();

        /// <summary>City-wide economy metrics from the most recent sample.</summary>
        public CityReading City => _city;

        /// <summary>Per-district economy metrics from the most recent sample, keyed by district id.</summary>
        public IReadOnlyDictionary<string, DistrictReading> Districts => _byDistrictId;

        protected override void CreateQueries()
        {
            _districtSensor = World.GetOrCreateSystemManaged<AgoraDistrictSensorSystem>();
            _statistics = World.GetOrCreateSystemManaged<CityStatisticsSystem>();
            _landValue = World.GetOrCreateSystemManaged<LandValueSystem>();
            _taxes = World.GetOrCreateSystemManaged<TaxSystem>();
            _loans = World.GetOrCreateSystemManaged<Game.Tools.LoanSystem>();

            _playerMoneyQuery = GetEntityQuery(ComponentType.ReadOnly<PlayerMoney>());

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
            SampleLedger();
            SampleTaxes();
            SampleLandValue(date);
        }

        private void SampleLedger()
        {
            _city.Money = ReadPlayerMoney();
            _city.Income = _statistics.GetStatisticValueLong(StatisticType.Income);
            _city.Expenses = _statistics.GetStatisticValueLong(StatisticType.Expense);

            // Loans are held as a positive principal. The contract's Debt is likewise non-negative,
            // so the sign is asserted here rather than trusted.
            long principal = _loans.CurrentLoan.m_Amount;
            _city.Debt = principal < 0 ? -principal : principal;
        }

        private long? ReadPlayerMoney()
        {
            NativeArray<Entity> entities = _playerMoneyQuery.ToEntityArray(Allocator.TempJob);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    PlayerMoney money;
                    if (EntityManager.TryGetComponent(entities[i], out money))
                    {
                        return money.money;
                    }
                }
            }
            finally
            {
                entities.Dispose();
            }

            return null;
        }

        private void SampleTaxes()
        {
            // GetTaxRate returns whole percentage points; the contract wants fractions.
            // Fully qualified: Game.City.TaxRates is a different type with the same name, and an
            // unqualified reference here is an ambiguity, not a preference.
            _city.Taxes = new Agora.Core.Contracts.TaxRates(
                _taxes.GetTaxRate(TaxAreaType.Residential) / 100.0,
                _taxes.GetTaxRate(TaxAreaType.Commercial) / 100.0,
                _taxes.GetTaxRate(TaxAreaType.Industrial) / 100.0,
                _taxes.GetTaxRate(TaxAreaType.Office) / 100.0);
        }

        private void SampleLandValue(SimDate date)
        {
            _districtSensor.EnsureSampled(date);
            IReadOnlyList<DistrictEntry> districts = _districtSensor.Districts;

            var cityAccumulator = new Accumulator();
            var byDistrict = new Dictionary<Entity, Accumulator>();
            for (int i = 0; i < districts.Count; i++)
            {
                byDistrict[districts[i].Entity] = new Accumulator();
            }

            JobHandle dependencies;
            NativeArray<LandValueCell> map = _landValue.GetMap(true, out dependencies);
            dependencies.Complete();

            NativeArray<Entity> buildings = _buildingQuery.ToEntityArray(Allocator.TempJob);
            try
            {
                for (int i = 0; i < buildings.Length; i++)
                {
                    Entity building = buildings[i];

                    Game.Objects.Transform transform;
                    if (!EntityManager.TryGetComponent(building, out transform)) continue;

                    // LandValueSystem.GetCellIndex does no bounds checking of its own — a building
                    // outside the playable area would index past the end of the map.
                    int cell = LandValueSystem.GetCellIndex(transform.m_Position);
                    if (cell < 0 || cell >= map.Length) continue;

                    double value = map[cell].m_LandValue;
                    cityAccumulator.Add(value);

                    CurrentDistrict currentDistrict;
                    if (!EntityManager.TryGetComponent(building, out currentDistrict)) continue;

                    Accumulator districtAccumulator;
                    if (byDistrict.TryGetValue(currentDistrict.m_District, out districtAccumulator))
                    {
                        districtAccumulator.Add(value);
                    }
                }
            }
            finally
            {
                buildings.Dispose();
            }

            // Levels only. Trends need history across captures, and that lives in one place —
            // AgoraSnapshotSystem — so rent and land value cannot end up measured over different
            // windows by accident.
            _city.AverageLandValue = cityAccumulator.Mean();

            _byDistrictId.Clear();

            // Sorted district list, never the accumulator dictionary — hashing order must not reach
            // the snapshot.
            for (int i = 0; i < districts.Count; i++)
            {
                DistrictEntry entry = districts[i];

                Accumulator accumulator;
                if (!byDistrict.TryGetValue(entry.Entity, out accumulator)) continue;

                var reading = new DistrictReading { Id = entry.Id, Name = entry.Name };
                reading.AverageLandValue = accumulator.Mean();
                _byDistrictId[entry.Id] = reading;
            }
        }

        /// <summary>
        /// Running mean that reports null rather than zero when it saw nothing. "No buildings here"
        /// and "land here is worthless" are different facts and the snapshot must not confuse them.
        /// </summary>
        private sealed class Accumulator
        {
            private double _sum;
            private long _count;

            public void Add(double value)
            {
                _sum += value;
                _count++;
            }

            public double? Mean() => _count <= 0 ? (double?)null : _sum / _count;
        }
    }
}
