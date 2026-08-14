// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System.Collections.Generic;

namespace Agora.Mod.Persistence
{
    /// <summary>One recorded level, at the month it was recorded in.</summary>
    /// <remarks>
    /// The month is <c>SimDate.TotalMonths</c> rather than a <c>SimDate</c>: the history is indexed by
    /// month and has no use for a day, and storing one would invite two samples that differ only in
    /// their day to look like two distinct months.
    /// </remarks>
    public sealed class MetricSampleFile
    {
        public int TotalMonths { get; set; }
        public double Value { get; set; }
    }

    /// <summary>One metric's samples, oldest first.</summary>
    public sealed class MetricSeriesFile
    {
        /// <summary>The series key — <c>city/rent</c>, <c>district-03/landValue</c>.</summary>
        public string Series { get; set; }

        public List<MetricSampleFile> Samples { get; set; }

        public MetricSeriesFile()
        {
            Series = "";
            Samples = new List<MetricSampleFile>();
        }
    }

    /// <summary>
    /// <c>metric_history.json</c> — the sensor layer's memory of past rent and land value, and the
    /// fifth file in the §5 sidecar layout.
    ///
    /// <para>
    /// It exists because a <i>trend</i> is not a game metric. Cities: Skylines II stores no rent time
    /// series anywhere, so <c>CitySnapshot.RentTrend</c> and <c>LandValueTrend</c> can only be
    /// computed against samples Agora took itself — and until this file existed those samples lived in
    /// a private field on <c>AgoraSnapshotSystem</c> and died with the session. Every reload restarted
    /// the clock on a window measured in years, which meant that in practice both trends were
    /// permanently unmeasurable and every district reported them as a city fallback.
    /// </para>
    ///
    /// <para>
    /// <b>Not political state, and deliberately not in <c>state_*.json</c>.</b> These are
    /// measurements of the city, not decisions about it; <c>Agora.Core</c> neither reads nor writes
    /// them, and folding them into <c>PoliticalState</c> would put a sensor artifact inside the engine
    /// contract and force a state-schema bump every time the sensor learned a new series.
    /// </para>
    ///
    /// <para>
    /// One file per save rather than one per snapshot: it is a rolling record, not a point-in-time
    /// one, and <c>MetricHistory.RestoreFrom</c> trims anything dated after the load it is restored
    /// into — so a player who rewinds to an earlier snapshot does not inherit the future's rents.
    /// </para>
    /// </summary>
    public sealed class MetricHistoryFile
    {
        public int SchemaVersion { get; set; }

        /// <summary>
        /// Every series held, sorted by <see cref="MetricSeriesFile.Series"/> ascending (ordinal).
        /// The order is contractual for the same reason it is everywhere else in the sidecar:
        /// non-negotiable #3 defines desync as the fingerprint of serialized state changing across a
        /// reload, and this file is written from a dictionary.
        /// </summary>
        public List<MetricSeriesFile> Series { get; set; }

        public MetricHistoryFile()
        {
            SchemaVersion = SidecarSchema.CurrentMetricHistoryVersion;
            Series = new List<MetricSeriesFile>();
        }
    }
}
