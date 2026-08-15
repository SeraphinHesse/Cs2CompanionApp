using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Mod.Persistence;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// The boundary between the game and the engine: implements
    /// <see cref="ISnapshotSource"/> by merging every sensor family's reading into one
    /// <see cref="CitySnapshot"/>.
    ///
    /// <para>
    /// This is the only type in <c>Sensors/</c> that <c>Agora.Core</c> ever talks to, and it is
    /// deliberately thin. It owns three things: the order sensors are merged in, the two trend
    /// fields that need memory across captures, and the guarantee that <see cref="Capture"/> is
    /// cheap — it hands back a cached object and never runs an <c>EntityQuery</c>, so a caller on
    /// the UI tick cannot accidentally walk every building in the city.
    /// </para>
    /// </summary>
    public sealed partial class AgoraSnapshotSystem : AgoraSensorSystemBase, ISnapshotSource
    {
        private AgoraDistrictSensorSystem _districts;
        private AgoraResidentsSensorSystem _residents;
        private AgoraEconomySensorSystem _economy;
        private AgoraEnvironmentSensorSystem _environment;
        private AgoraServiceCoverageSensorSystem _services;
        private AgoraMobilitySensorSystem _mobility;
        private AgoraStatisticsSensorSystem _statistics;
        private AgoraTourismSensorSystem _tourism;
        private AgoraProgressionSensorSystem _progression;

        /// <summary>
        /// The metric history: the only state a snapshot needs beyond the current frame. Sized well
        /// past both the widest trend window the calibration allows and the snapshot retention
        /// <c>AgoraRuntime</c> asks to have rebuilt.
        /// </summary>
        /// <remarks>
        /// Persisted, via <see cref="ExportHistory"/> and <see cref="RestoreHistory"/>, to
        /// <c>metric_history.json</c>. It used to be session-scoped, which quietly made both trend
        /// fields unmeasurable for anyone who ever quit the game. It now also carries the fields
        /// <see cref="SnapshotRehydration"/> rebuilds past snapshots from, so the engine's own trend
        /// window survives the same reload.
        /// </remarks>
        private readonly MetricHistory _history = new MetricHistory(64);

        private CitySnapshot _latest;

        /// <summary>
        /// The most recent snapshot, or null before the first successful sample. Prefer
        /// <see cref="Capture"/>, which never returns null.
        /// </summary>
        public CitySnapshot Latest => _latest;

        protected override void CreateQueries()
        {
            // No queries of its own — every reading comes from a sibling sensor. Resolved here rather
            // than lazily so a missing system fails at world creation, where it is obvious, instead of
            // on the first capture months into a game.
            _districts = World.GetOrCreateSystemManaged<AgoraDistrictSensorSystem>();
            _residents = World.GetOrCreateSystemManaged<AgoraResidentsSensorSystem>();
            _economy = World.GetOrCreateSystemManaged<AgoraEconomySensorSystem>();
            _environment = World.GetOrCreateSystemManaged<AgoraEnvironmentSensorSystem>();
            _services = World.GetOrCreateSystemManaged<AgoraServiceCoverageSensorSystem>();
            _mobility = World.GetOrCreateSystemManaged<AgoraMobilitySensorSystem>();
            _statistics = World.GetOrCreateSystemManaged<AgoraStatisticsSensorSystem>();
            _tourism = World.GetOrCreateSystemManaged<AgoraTourismSensorSystem>();
            _progression = World.GetOrCreateSystemManaged<AgoraProgressionSensorSystem>();
        }

        public override void Invalidate()
        {
            base.Invalidate();
            _latest = null;
            _history.Clear();

            // A different save means a different city. Every sensor's cache and the trend history
            // must go with it, or the new game inherits the old one's rents.
            //
            // Null-guarded because Invalidate is public: a caller that reaches it before OnCreate has
            // resolved the sibling systems gets a no-op rather than a NullReferenceException thrown
            // out of a load handler.
            if (_districts != null) _districts.Invalidate();
            if (_residents != null) _residents.Invalidate();
            if (_economy != null) _economy.Invalidate();
            if (_environment != null) _environment.Invalidate();
            if (_services != null) _services.Invalidate();
            if (_mobility != null) _mobility.Invalidate();

            // The city-statistics pass. A sensor left out of this list carries one city's readings
            // into the next save and nothing shows it until someone loads a second city, which is the
            // per-save-reset bug class this method exists for.
            if (_statistics != null) _statistics.Invalidate();
            if (_tourism != null) _tourism.Invalidate();
            if (_progression != null) _progression.Invalidate();
        }

        /// <summary>
        /// The trend history as the sidecar document, for <c>AgoraSidecarSystem</c> to write at save
        /// time. Never null: a save taken before the first capture writes an empty history, which is
        /// the truth about that city.
        /// </summary>
        public MetricHistoryFile ExportHistory()
        {
            return _history.ToFile();
        }

        /// <summary>
        /// Adopts the history the sidecar just read, trimmed to the date being loaded into.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Must run <b>after</b> <see cref="Invalidate"/> and <b>before</b> the first
        /// <see cref="Capture"/> of the session — <see cref="Invalidate"/> clears the history, and a
        /// capture would otherwise record the present month against an empty series and report no
        /// trend for a save that has years of them. <c>AgoraRuntime.OnSidecarLoaded</c> owns that
        /// ordering.
        /// </para>
        /// <para>
        /// The date comes from the clock rather than from the caller, because the clock is the one
        /// that knows: on a §5 reconciliation onto an earlier snapshot, "today" IS the earlier date,
        /// and that is exactly the boundary the trim needs. A capture taken before the clock is
        /// readable trims against <c>default(SimDate)</c>, which would discard everything — so the
        /// restore is skipped entirely in that case and the next load repeats it.
        /// </para>
        /// </remarks>
        public void RestoreHistory(MetricHistoryFile file)
        {
            if (file == null) return;

            SimDate today;
            if (!TryGetToday(out today)) return;

            _history.RestoreFrom(file, today);
        }

        /// <summary>
        /// The engine's view of the city. Returns the cached snapshot, refreshing it first if the
        /// day has turned. Never null and never throws — a failed sample leaves the previous good
        /// snapshot in place, and a capture before any game is loaded returns an empty one.
        /// </summary>
        public CitySnapshot Capture()
        {
            SimDate today;
            if (TryGetToday(out today))
            {
                EnsureSampled(today);
            }

            if (_latest != null) return _latest;

            return SnapshotAssembly.Build(today, new CityReading(), new List<DistrictReading>());
        }

        protected override void Sample(SimDate date)
        {
            _districts.EnsureSampled(date);
            _residents.EnsureSampled(date);
            _economy.EnsureSampled(date);
            _environment.EnsureSampled(date);
            _services.EnsureSampled(date);
            _mobility.EnsureSampled(date);
            _statistics.EnsureSampled(date);
            _tourism.EnsureSampled(date);
            _progression.EnsureSampled(date);

            // Fixed priority order. Families own disjoint fields, so nothing actually collides —
            // pinning the order anyway means a future overlap resolves the same way every time
            // rather than by whichever system the scheduler ran last.
            var citySources = new List<CityReading>
            {
                _residents.City,
                _economy.City,
                _environment.City,
                _services.City,
                _mobility.City,
                _statistics.City,
                _tourism.City,
                _progression.City,
            };

            var districtSources = new List<IReadOnlyDictionary<string, DistrictReading>>
            {
                _residents.Districts,
                _economy.Districts,
                _environment.Districts,
                _services.Districts,
                _statistics.Districts,
                _tourism.Districts,

                // No progression entry: a district has no milestone and TaxSystem has no per-district,
                // per-resource overload, so that system has no Districts property at all. An
                // always-empty one would read as "measured nothing here" rather than "there is
                // nothing here to measure".
            };

            IReadOnlyList<DistrictEntry> entries = _districts.Districts;
            var idsAndNames = new List<KeyValuePair<string, string>>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                idsAndNames.Add(new KeyValuePair<string, string>(entries[i].Id, entries[i].Name));
            }

            CityReading city = SensorMerge.MergeCity(citySources);
            List<DistrictReading> districts = SensorMerge.MergeDistricts(idsAndNames, districtSources);

            ApplyTrends(date, city, districts);

            _latest = SnapshotAssembly.Build(date, city, districts);

            // Files the whole assembled snapshot into the history, so a reload can rebuild it. It
            // runs on the built snapshot rather than on the readings above because the city fallback
            // for an unmeasured district field is applied during Build: recording the readings would
            // rebuild a past month that disagrees with the one the engine was actually handed. Rent
            // and land value stay out of it — ApplyTrends already recorded those, and only where they
            // were genuinely measured.
            _history.RecordSnapshot(_latest);
        }

        /// <summary>
        /// Records this capture's rent and land value, then reads back the change over the trend
        /// window. A trend with no baseline old enough stays null and falls back — a city three
        /// months old has not held its rents steady, it has no rent history at all.
        /// </summary>
        private void ApplyTrends(SimDate date, CityReading city, List<DistrictReading> districts)
        {
            int window = Calibration.TrendWindowMonths;

            city.LandValueTrend = Track(MetricHistory.CityKey(MetricHistory.LandValue), date, window, city.AverageLandValue);
            city.RentTrend = Track(MetricHistory.CityKey(MetricHistory.Rent), date, window, city.AverageRent);

            for (int i = 0; i < districts.Count; i++)
            {
                DistrictReading district = districts[i];

                district.LandValueTrend = Track(
                    MetricHistory.DistrictKey(district.Id, MetricHistory.LandValue), date, window, district.AverageLandValue);
                district.RentTrend = Track(
                    MetricHistory.DistrictKey(district.Id, MetricHistory.Rent), date, window, district.AverageRent);
            }
        }

        private double? Track(string series, SimDate date, int windowMonths, double? level)
        {
            // An unmeasured level records nothing. Writing a zero here would put a fabricated point
            // in the history and poison every trend computed against it for the next two years.
            if (!level.HasValue) return null;

            _history.Record(series, date, level.Value);
            return _history.TrendOver(series, date, windowMonths);
        }
    }
}
