using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Colossal.Entities;
using Game.Agents;
using Game.Areas;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

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
    ///
    /// <para>
    /// <b>A zero here is reported as a zero.</b> <c>GetStatisticValueLong</c> returns 0 for a genuine
    /// zero, for a statistic locked behind progression, and for a key that does not exist, and
    /// nothing distinguishes the three (scout 0004 §1.7 and Q1). This sensor therefore never invents
    /// an "unmeasurable" signal out of a suspicious number: null means the <i>source</i> was
    /// unavailable and nothing else, and wave 2's real <c>Unmeasurable</c> state must be built on an
    /// answer to Q1 rather than on a guess made here.
    /// </para>
    /// </summary>
    public sealed partial class AgoraStatisticsSensorSystem : AgoraSensorSystemBase
    {
        private EntityQuery _garbageProducerQuery;
        private EntityQuery _statisticsDataQuery;

        private AgoraDistrictSensorSystem _districtSensor;
        private CityStatisticsSystem _statistics;
        private CountHouseholdDataSystem _households;
        private GarbageAccumulationSystem _garbageAccumulation;

        /// <summary>
        /// Set once the collection-type census has actually been written, or once it has thrown. An
        /// <i>empty</i> census does not latch it, so a first sample taken before the statistic
        /// prefabs exist retries; a throwing one does, because that will not fix itself and a daily
        /// warning would be noise. Deliberately not cleared
        /// by <see cref="Invalidate"/>: the statistic prefabs are the same for every save loaded in a
        /// session, so re-logging sixty-odd lines per load would be noise, not information.
        /// </summary>
        private bool _loggedCollectionTypes;

        private readonly CityReading _city = new CityReading();

        private readonly Dictionary<string, DistrictReading> _districts =
            new Dictionary<string, DistrictReading>();

        /// <summary>City-wide statistics from the most recent sample.</summary>
        public CityReading City => _city;

        /// <summary>Per-district uncollected garbage from the most recent sample, keyed by district id.</summary>
        public IReadOnlyDictionary<string, DistrictReading> Districts => _districts;

        protected override void CreateQueries()
        {
            _districtSensor = World.GetOrCreateSystemManaged<AgoraDistrictSensorSystem>();
            _statistics = World.GetOrCreateSystemManaged<CityStatisticsSystem>();
            _households = World.GetOrCreateSystemManaged<CountHouseholdDataSystem>();
            _garbageAccumulation = World.GetOrCreateSystemManaged<GarbageAccumulationSystem>();

            // The exclusions are copied from the game's own producer query
            // (GarbageAccumulationSystem.cs:522-536): a building being dragged out under the cursor
            // (Temp), one already demolished (Deleted) or one burnt down (Destroyed) still carries a
            // GarbageProducer, and counting them would move the metric for reasons that are not
            // events. UpdateFrame is in the game's All list only because it shards the walk across
            // frames; this sensor walks the whole set once a day and must not filter by it.
            _garbageProducerQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<GarbageProducer>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Destroyed>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });

            // Every statistic prefab carries its collection type as asset data, which is why it
            // cannot be read out of the decompiled source. See LogCollectionTypes.
            _statisticsDataQuery = GetEntityQuery(ComponentType.ReadOnly<StatisticsData>());
        }

        public override void Invalidate()
        {
            base.Invalidate();
            _districts.Clear();
        }

        protected override void Sample(SimDate date)
        {
            // A diagnostic must never be able to cost a measurement: a throw here would reach
            // AgoraSensorSystemBase.TrySample and discard the whole day's statistics and garbage
            // reading over a census nobody's city depends on.
            if (!_loggedCollectionTypes)
            {
                try
                {
                    _loggedCollectionTypes = LogCollectionTypes();
                }
                catch (Exception ex)
                {
                    _loggedCollectionTypes = true;
                    AgoraMod.Log.Warn("AGORA-STATCOLLECTION census failed (" + ex.GetType().Name +
                                      ": " + ex.Message + "); scout 0004 Q2 stays open for this " +
                                      "session. Measurements are unaffected.");
                }
            }

            _city.Statistics = ReadStatistics();
            SampleUncollectedGarbage(date);
        }

        /// <summary>
        /// The city-statistics block, or null when the household count data is not yet ready.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>CountHouseholdDataSystem.IsCountDataNotReady()</c> is true for the first frames after a
        /// load, and in that window every figure below reads zero — the homeless count and the
        /// population statistic are both fed from the same household data
        /// (<c>CityStatisticsSystem.cs:214-218</c>). A city that has not been counted yet and a city
        /// with nobody in it are indistinguishable there, so the whole block is withheld rather than
        /// published as a set of zeros: null is "not measured", and the next sample takes it again.
        /// </para>
        /// <para>
        /// Outside that window a zero is published as a zero, deliberately. See the class remarks.
        /// </para>
        /// </remarks>
        private CityStatistics? ReadStatistics()
        {
            if (_households.IsCountDataNotReady()) return null;

            // A percentage 0-100 from the game (CountHouseholdDataSystem.cs:967-976), and a fraction
            // 0-1 in the contract, on the same convention that already makes TaxRates fractions.
            // Without the /100 every homelessness threshold fires at a hundred times its intended
            // level, and the logged figure looks plausible either way.
            double homelessShare = _households.HomelessnessRate / 100.0;

            return new CityStatistics(
                ToCount(_statistics.GetStatisticValueLong(StatisticType.HomelessCount)),
                homelessShare,
                ToCount(_statistics.GetStatisticValueLong(StatisticType.CitizensMovedIn)),
                ToCount(_statistics.GetStatisticValueLong(StatisticType.CitizensMovedAway)),
                // MovedAwayReason is parametric; the parameter is the reason enum value itself,
                // per Game.Prefabs.MoveAwayStatistic.GetParameters (scout 0004 §4).
                ToCount(_statistics.GetStatisticValueLong(
                    StatisticType.MovedAwayReason, (int)MoveAwayReason.NotHappy)),
                // "Rate" in the game's enum names, counts in fact: BirthSystem and DeathCheckSystem
                // each contribute m_Change = 1f per event.
                ToCount(_statistics.GetStatisticValueLong(StatisticType.BirthRate)),
                ToCount(_statistics.GetStatisticValueLong(StatisticType.DeathRate)),
                // Garbage produced per day, already scaled by kUpdatesPerDay. The game's own binding
                // for this exact expression is named "productionRate"; it is not a stockpile.
                _garbageAccumulation.garbageAccumulation);
        }

        /// <summary>
        /// Sums <c>GarbageProducer.m_Garbage</c> — garbage waiting on the kerb — for the city and for
        /// each district.
        /// </summary>
        /// <remarks>
        /// The infoview's own "stored garbage" is computed by a private job into a private array and
        /// is not callable (scout 0004 §7.2), so this is a proxy for it and content must say
        /// "uncollected", not "landfill". It is also the only garbage figure with a district
        /// breakdown, because producers are buildings and buildings carry <c>CurrentDistrict</c>.
        /// </remarks>
        private void SampleUncollectedGarbage(SimDate date)
        {
            _districtSensor.EnsureSampled(date);
            IReadOnlyList<DistrictEntry> districts = _districtSensor.Districts;

            var byDistrict = new Dictionary<Entity, long>();
            for (int i = 0; i < districts.Count; i++)
            {
                byDistrict[districts[i].Entity] = 0L;
            }

            long cityGarbage = 0L;
            int visited = 0;
            int total;
            bool subsampled;

            NativeArray<Entity> producers = _garbageProducerQuery.ToEntityArray(Allocator.TempJob);
            try
            {
                total = producers.Length;
                int stride = SubsampleStride(total);
                subsampled = stride > 1;

                for (int i = 0; i < total; i++)
                {
                    Entity producer = producers[i];

                    // Keyed off the entity index rather than the loop counter, exactly as the
                    // residents walk does: chunk order is not stable across loads, so a positional
                    // stride would visit a different set of buildings each capture and quietly make
                    // the snapshot non-reproducible.
                    if (subsampled && (producer.Index % stride) != 0) continue;

                    GarbageProducer garbage;
                    if (!EntityManager.TryGetComponent(producer, out garbage)) continue;

                    visited++;
                    cityGarbage += garbage.m_Garbage;

                    // No district tally under a stride: the per-district figures are withheld in that
                    // case, for the reason set out where they are published below.
                    if (subsampled) continue;

                    CurrentDistrict currentDistrict;
                    if (!EntityManager.TryGetComponent(producer, out currentDistrict)) continue;

                    long districtGarbage;
                    if (byDistrict.TryGetValue(currentDistrict.m_District, out districtGarbage))
                    {
                        byDistrict[currentDistrict.m_District] = districtGarbage + garbage.m_Garbage;
                    }
                }
            }
            finally
            {
                producers.Dispose();
            }

            // Under the emergency cap this is a sum over a subset, and a sum — unlike the shares and
            // averages the other walks produce — does not survive subsampling on its own
            // (SensorCalibration.MaxBuildingsPerCapture says so in as many words). Scaling it back up
            // by the sampled fraction is an unbiased estimator over the whole city, where the
            // unscaled alternative is biased low by a factor of the stride. This deviates from
            // AgoraResidentsSensorSystem, which does not extrapolate — deliberately, because it
            // publishes shares and averages, which need no correction. With the default cap of 0 the
            // walk is exhaustive and the factor is exactly 1.
            double scale = visited > 0 && visited < total ? (double)total / visited : 1.0;

            _city.UncollectedGarbage = cityGarbage * scale;

            _districts.Clear();
            for (int i = 0; i < districts.Count; i++)
            {
                DistrictEntry entry = districts[i];

                long districtGarbage;
                if (!byDistrict.TryGetValue(entry.Entity, out districtGarbage)) continue;

                // Zero is published, not withheld, and the invariant that licenses it is that the
                // walk was exhaustive: every producer in the city was visited, so a district that
                // contributed nothing has no garbage waiting in it — a measurement, not an absence
                // of one.
                //
                // Under a stride that invariant does not hold, and the estimator that works city-wide
                // does not survive being cut into district-sized pieces. A district with forty
                // producers sampled at stride 25 expects fewer than two hits: it draws none about one
                // capture in five, which would publish a confident 0.0 over a week of uncollected
                // rubbish, and when it is hit, two producers holding 800 each scale to 40,000 against
                // a truth nearer 24,000. Alternating between those two readings is exactly the shape
                // of a garbage crisis to a delta trigger. So the district figure is withheld: null
                // means "not measured here", assembly records the CityFallbackFields marker, and the
                // marker is the honest claim.
                _districts[entry.Id] = new DistrictReading
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    UncollectedGarbage = subsampled ? (double?)null : districtGarbage,
                };
            }
        }

        private int SubsampleStride(int buildingCount)
        {
            int cap = Calibration.MaxBuildingsPerCapture;
            if (cap <= 0 || buildingCount <= cap) return 1;

            int stride = buildingCount / cap;
            return stride < 2 ? 2 : stride;
        }

        /// <summary>
        /// Writes every statistic prefab's <c>(m_StatisticType, m_CollectionType, m_UnitType)</c>
        /// triple to the log, once per session.
        /// </summary>
        /// <remarks>
        /// This answers scout 0004 Q2, and it can only be answered at runtime: the collection type
        /// lives in prefab <b>asset</b> data, so it is not in the decompiled source and cannot be
        /// grepped for. It decides whether <c>BirthRate</c> means "births in the last in-game day"
        /// (<c>Daily</c>, a rolling sum of 32 samples), "births in the last 1/32 of a day"
        /// (<c>Point</c>) or "births since the city was founded" (<c>Cumulative</c>) — and wave 3
        /// cannot author a threshold against a number whose period is unknown. The lines are prefixed
        /// and sorted so the block can be grepped out of <c>Agora.log</c> and pasted into the handoff
        /// unedited.
        /// </remarks>
        /// <returns>
        /// True once the census has actually been written. An empty result is <b>not</b> a census:
        /// if the first sample of a session lands before the statistic prefabs reach the entity
        /// manager, latching on it would log "0 statistic prefabs" and leave Q2 unanswerable from
        /// that player's log for the whole session — a round trip to a player to discover, and a
        /// blocked wave 3 in the meantime. So a zero-row result retries on the next sample.
        /// </returns>
        private bool LogCollectionTypes()
        {
            var rows = new List<StatisticsData>();

            NativeArray<StatisticsData> data =
                _statisticsDataQuery.ToComponentDataArray<StatisticsData>(Allocator.TempJob);
            try
            {
                for (int i = 0; i < data.Length; i++)
                {
                    rows.Add(data[i]);
                }
            }
            finally
            {
                data.Dispose();
            }

            if (rows.Count == 0) return false;

            // Sorted by statistic id so two players' logs are diffable line for line. Ties are broken
            // by collection type, which keeps the order total for the parametric statistics that
            // appear on more than one prefab.
            rows.Sort((a, b) =>
            {
                int byType = ((int)a.m_StatisticType).CompareTo((int)b.m_StatisticType);
                return byType != 0
                    ? byType
                    : ((int)a.m_CollectionType).CompareTo((int)b.m_CollectionType);
            });

            AgoraMod.Log.Info($"AGORA-STATCOLLECTION begin — {rows.Count} statistic prefabs " +
                              "(scout 0004 Q2; Daily = last in-game day, Point = last 1/32 day, " +
                              "Cumulative = since founding)");

            for (int i = 0; i < rows.Count; i++)
            {
                StatisticsData row = rows[i];
                AgoraMod.Log.Info(
                    $"AGORA-STATCOLLECTION type={(int)row.m_StatisticType} {row.m_StatisticType} " +
                    $"collection={row.m_CollectionType} unit={row.m_UnitType}");
            }

            AgoraMod.Log.Info("AGORA-STATCOLLECTION end");
            return true;
        }

        /// <summary>
        /// A statistic as the <c>int</c> count the contract asks for.
        /// </summary>
        /// <remarks>
        /// Clamped to the <c>int</c> range rather than cast, because an unchecked narrowing cast of a
        /// <c>Cumulative</c> statistic in a very old city would wrap into a negative count. The value
        /// is otherwise passed through untouched — including a zero, which this sensor never second-
        /// guesses.
        /// </remarks>
        private static int ToCount(long value)
        {
            if (value > int.MaxValue) return int.MaxValue;
            if (value < int.MinValue) return int.MinValue;
            return (int)value;
        }
    }
}
