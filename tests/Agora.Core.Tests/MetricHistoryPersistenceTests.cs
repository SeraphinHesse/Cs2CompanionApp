// Requires the Persistence and Sensors <Compile Link> lines in Agora.Core.Tests.csproj. See the
// comment there for why they are linked rather than referenced.

using System;
using System.IO;
using Agora.Core.Contracts;
using Agora.Mod.Persistence;
using Agora.Mod.Sensors;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// <c>metric_history.json</c>: the sensor layer's memory of past rent and land value.
    ///
    /// <para>
    /// The bug this file guards is the reason the document exists. A <i>trend</i> has no source in
    /// the game — Cities: Skylines II stores no rent time series — so <c>CitySnapshot.RentTrend</c>
    /// can only be computed against samples Agora took itself. Those samples used to live in a
    /// private field on <c>AgoraSnapshotSystem</c> and die with the session, which meant a window
    /// measured in months could never be reached by anyone who quits the game: the trend was
    /// permanently null, and <c>SnapshotAssembly</c> correctly reported it as a city fallback in
    /// every district, forever. Nothing about that reads as broken — the fallback machinery was
    /// working exactly as designed, on an input that could never arrive.
    /// </para>
    ///
    /// <para>
    /// So the load-bearing test here is <see cref="Trend_SurvivesASaveAndReload"/>. The rest guard
    /// the ways a persisted history can be worse than none: a fabricated future after a rewind, an
    /// order-dependent serialization, a hand-edited file taken at face value.
    /// </para>
    /// </summary>
    public class MetricHistoryPersistenceTests
    {
        private static readonly Guid Save = new Guid("aaaabbbb-cccc-dddd-eeee-ffff00001111");

        private const int WindowMonths = 12;

        private static SimDate Month(int year, int month) => new SimDate(year, month, 1);

        /// <summary>Records one rising sample per month, inclusive of both ends.</summary>
        private static void FillMonthly(MetricHistory history, string series,
                                        SimDate from, int months, double start, double step)
        {
            SimDate date = from;
            for (int i = 0; i <= months; i++)
            {
                history.Record(series, date, start + step * i);
                date = date.AddMonths(1);
            }
        }

        // --- The regression ------------------------------------------------------------------------

        /// <summary>
        /// Thirteen months of rents, a save, a reload, and the trend is still there. Before
        /// <c>metric_history.json</c> the reload produced null — and null becomes the city figure with
        /// the field named in <c>cityFallbackFields</c>, which is the "rent trend is city wide"
        /// warning a player sees in the districts view.
        /// </summary>
        [Fact]
        public void Trend_SurvivesASaveAndReload()
        {
            string root = TempRoot("reload");

            try
            {
                string series = MetricHistory.DistrictKey("district-01", MetricHistory.Rent);
                SimDate start = Month(1990, 1);
                SimDate now = start.AddMonths(WindowMonths);

                var before = new MetricHistory(64);
                FillMonthly(before, series, start, WindowMonths, 1000.0, 10.0);

                double? measuredBefore = before.TrendOver(series, now, WindowMonths);
                Assert.NotNull(measuredBefore);

                var store = new SidecarStore(root, NullSidecarLog.Instance);
                Assert.True(store.SaveMetricHistory(Save, before.ToFile()));

                // A brand new instance, exactly as a fresh session gets: no samples until the file is
                // read back.
                var after = new MetricHistory(64);
                Assert.Null(after.TrendOver(series, now, WindowMonths));

                after.RestoreFrom(store.LoadMetricHistory(Save), now);

                Assert.Equal(measuredBefore!.Value, after.TrendOver(series, now, WindowMonths)!.Value, 12);

                // 1000 → 1120 over the window.
                Assert.Equal(0.12, after.TrendOver(series, now, WindowMonths)!.Value, 12);
            }
            finally
            {
                Delete(root);
            }
        }

        /// <summary>
        /// The history reaches the sensor through <c>SidecarStore.Load</c>, not only through the
        /// standalone reader — that is the path <c>AgoraRuntime.OnSidecarLoaded</c> actually takes,
        /// and a document written but never surfaced on the load result would be silently useless.
        /// </summary>
        [Fact]
        public void Load_SurfacesTheHistoryAlongsideTheState()
        {
            string root = TempRoot("load-result");

            try
            {
                var store = new SidecarStore(root, NullSidecarLog.Instance);
                string series = MetricHistory.CityKey(MetricHistory.Rent);

                var history = new MetricHistory(64);
                FillMonthly(history, series, Month(1990, 1), 3, 500.0, 5.0);
                Assert.True(store.SaveMetricHistory(Save, history.ToFile()));

                SidecarLoadResult loaded = store.Load(Save, Month(1990, 4));

                // No state file was ever written. The history is a measurement record, not political
                // state, so a city whose politics have not started still carries one.
                Assert.False(loaded.HasState);
                Assert.NotNull(loaded.MetricHistory);
                Assert.Empty(loaded.Warnings);

                Assert.Single(loaded.MetricHistory.Series);
                Assert.Equal(series, loaded.MetricHistory.Series[0].Series);
                Assert.Equal(4, loaded.MetricHistory.Series[0].Samples.Count);
            }
            finally
            {
                Delete(root);
            }
        }

        /// <summary>
        /// A save with no history at all — every save made before this document existed. Null, no
        /// warning, and a restore from null leaves the sensor collecting from scratch, which is the
        /// same position it was already in.
        /// </summary>
        [Fact]
        public void AnAbsentFile_IsNotAWarningAndNotAnEmptyHistory()
        {
            string root = TempRoot("absent");

            try
            {
                var store = new SidecarStore(root, NullSidecarLog.Instance);

                Assert.Null(store.LoadMetricHistory(Save));

                SidecarLoadResult loaded = store.Load(Save, Month(1990, 1));
                Assert.Null(loaded.MetricHistory);
                Assert.Empty(loaded.Warnings);

                var history = new MetricHistory(64);
                history.Record("city/rent", Month(1990, 1), 100.0);

                // A missing file is an absence, not an instruction to forget. Restoring from null
                // leaves what this session has already collected alone...
                history.RestoreFrom(null, Month(1990, 1));
                Assert.Equal(1, history.SampleCount("city/rent"));

                // ...whereas an empty document is a save saying it genuinely has no history, and that
                // one does replace. The two cases look identical at the call site and mean opposite
                // things, which is why they are separated here rather than in the caller.
                history.RestoreFrom(new MetricHistoryFile(), Month(1990, 1));
                Assert.Equal(0, history.SampleCount("city/rent"));
            }
            finally
            {
                Delete(root);
            }
        }

        // --- Rewind ------------------------------------------------------------------------------

        /// <summary>
        /// §5 allows a load to reconcile onto an <i>earlier</i> snapshot. The history on disk is the
        /// newest one written, so it holds months that, from the loaded save's point of view, have
        /// not happened. Keeping them would compute a trend whose "present" is the future.
        /// </summary>
        [Fact]
        public void RestoringOntoAnEarlierDate_DiscardsTheFuture()
        {
            string series = MetricHistory.CityKey(MetricHistory.LandValue);

            var written = new MetricHistory(64);
            FillMonthly(written, series, Month(1990, 1), 36, 100.0, 1.0);

            var rewound = new MetricHistory(64);
            SimDate loadedAt = Month(1992, 1);
            rewound.RestoreFrom(written.ToFile(), loadedAt);

            // 1990-01 through 1992-01 inclusive: 25 months. The 12 that follow are gone.
            Assert.Equal(25, rewound.SampleCount(series));

            // The window is measured back from the loaded month, not from the start of the file:
            // 112 at 1991-01 to 124 at 1992-01. The untrimmed history would have read 136 as the
            // present and reported a rise that has not happened yet.
            Assert.Equal(12.0 / 112.0, rewound.TrendOver(series, loadedAt, WindowMonths)!.Value, 12);

            // What the untrimmed history would have said, and why the trim is not cosmetic. TrendOver
            // reads the newest sample as "present", so with the future still in place the present is
            // 1993-01's 136 — a city loaded in 1992 told its rents had risen twice as far as they had.
            var untrimmed = new MetricHistory(64);
            untrimmed.RestoreFrom(written.ToFile(), Month(1993, 1));
            Assert.Equal(24.0 / 112.0, untrimmed.TrendOver(series, loadedAt, WindowMonths)!.Value, 12);
        }

        /// <summary>
        /// The boundary of the trim: the month being loaded into is the present, not the future, and
        /// dropping it would silently shorten every window by one.
        /// </summary>
        [Fact]
        public void TheLoadedMonthItself_IsKept()
        {
            var written = new MetricHistory(64);
            FillMonthly(written, "city/rent", Month(1990, 1), 5, 10.0, 1.0);

            var restored = new MetricHistory(64);
            restored.RestoreFrom(written.ToFile(), Month(1990, 6));

            Assert.Equal(6, restored.SampleCount("city/rent"));
        }

        // --- Determinism -------------------------------------------------------------------------

        /// <summary>
        /// The history is a dictionary, and this is the one place anything enumerates it. Two
        /// histories holding the same samples must serialize byte-identically however they were
        /// filled — non-negotiable #3 defines desync as that fingerprint changing across a reload.
        /// </summary>
        [Fact]
        public void Serialization_DoesNotDependOnInsertionOrder()
        {
            string[] forward = { "city/rent", "district-01/rent", "district-02/landValue" };

            var a = new MetricHistory(64);
            for (int i = 0; i < forward.Length; i++)
            {
                FillMonthly(a, forward[i], Month(1990, 1), 4, 100.0 + i, 1.0);
            }

            var b = new MetricHistory(64);
            for (int i = forward.Length - 1; i >= 0; i--)
            {
                FillMonthly(b, forward[i], Month(1990, 1), 4, 100.0 + i, 1.0);
            }

            Assert.Equal(AgoraJson.Fingerprint(a.ToFile()), AgoraJson.Fingerprint(b.ToFile()));

            // And the written order is the sorted one, not either insertion order.
            MetricHistoryFile file = b.ToFile();
            Assert.Equal(forward[0], file.Series[0].Series);
            Assert.Equal(forward[1], file.Series[1].Series);
            Assert.Equal(forward[2], file.Series[2].Series);
        }

        /// <summary>
        /// Save, load, save again: the second file is byte-identical to the first. A round trip that
        /// perturbed a value — a double narrowed, a sample re-ordered, a month shifted — would drift a
        /// long-running save's trends one reload at a time, which is the failure that is invisible on
        /// any single load and obvious after fifty.
        /// </summary>
        [Fact]
        public void ARoundTrip_IsIdempotent()
        {
            string root = TempRoot("idempotent");

            try
            {
                var store = new SidecarStore(root, NullSidecarLog.Instance);

                // 42 samples ending exactly on `now`, so nothing is trimmed and the comparison is of
                // the whole file. The rewind case is RestoringOntoAnEarlierDate_DiscardsTheFuture.
                SimDate start = Month(1990, 1);
                SimDate now = start.AddMonths(41);

                var original = new MetricHistory(64);
                FillMonthly(original, "city/rent", start, 41, 812.5, 3.25);
                FillMonthly(original, "district-01/landValue", start, 41, 1_000.75, -2.5);

                store.SaveMetricHistory(Save, original.ToFile());
                string first = File.ReadAllText(
                    SidecarPaths.MetricHistoryPath(store.DirectoryFor(Save)));

                var reloaded = new MetricHistory(64);
                reloaded.RestoreFrom(store.LoadMetricHistory(Save), now);
                store.SaveMetricHistory(Save, reloaded.ToFile());

                string second = File.ReadAllText(
                    SidecarPaths.MetricHistoryPath(store.DirectoryFor(Save)));

                Assert.Equal(first, second);

                // ...and the trend the whole thing exists to serve is unchanged.
                Assert.Equal(original.TrendOver("city/rent", now, WindowMonths)!.Value,
                             reloaded.TrendOver("city/rent", now, WindowMonths)!.Value, 12);
            }
            finally
            {
                Delete(root);
            }
        }

        // --- The widened series ------------------------------------------------------------------

        /// <summary>
        /// The document no longer holds only rent and land value. It now carries the closed set
        /// <c>SnapshotRehydration</c> rebuilds a past <c>CitySnapshot</c> from — city population and
        /// education, and per district education and the low-wealth share — and every one of them has
        /// to survive the disk round trip, not just the two the file was originally written for.
        /// </summary>
        /// <remarks>
        /// Asserted by fingerprint rather than by naming the series, for the same reason the golden
        /// rehydration test does not assert a field list: a hand-written list only covers what its
        /// author thought of, and a series silently dropped on the way through would be invisible to
        /// it. The count and sample-count assertions are there so the fingerprints cannot agree by
        /// both being empty.
        /// </remarks>
        [Fact]
        public void EveryRecordedSeries_SurvivesTheDiskRoundTrip()
        {
            string root = TempRoot("widened-series");

            try
            {
                const int Months = 30;
                const int Retention = 128;

                SimDate start = Month(1990, 1);
                SimDate now = start.AddMonths(Months - 1);

                var original = new MetricHistory(Retention);
                SyntheticCityHistory.RecordAll(original, SyntheticCityHistory.Months(start, Months));

                var store = new SidecarStore(root, NullSidecarLog.Instance);
                Assert.True(store.SaveMetricHistory(Save, original.ToFile()));

                var reloaded = new MetricHistory(Retention);
                reloaded.RestoreFrom(store.LoadMetricHistory(Save), now);

                Assert.Equal(AgoraJson.Fingerprint(original.ToFile()),
                             AgoraJson.Fingerprint(reloaded.ToFile()));

                // Not against a hardcoded series count, which would have to be maintained every time
                // the recorded vocabulary widens and would be wrong in the interim. One month through
                // the same recorder produces exactly the set a full history should hold, so the two
                // counts agreeing is the statement "nothing was dropped on the way through", and the
                // month counts are what stop both sides from being trivially empty.
                var oneMonth = new MetricHistory(Retention);
                oneMonth.RecordSnapshot(SyntheticCityHistory.Snapshot(start, 0));

                MetricHistoryFile file = reloaded.ToFile();
                Assert.NotEmpty(file.Series);
                Assert.Equal(oneMonth.ToFile().Series.Count, file.Series.Count);
                Assert.All(file.Series, s => Assert.Equal(Months, s.Samples.Count));
            }
            finally
            {
                Delete(root);
            }
        }

        /// <summary>
        /// The values themselves, not only the shape. A double narrowed on the way to disk would
        /// leave every trend and every rehydrated snapshot subtly wrong in a way no count assertion
        /// can see — and the rehydration path feeds <c>IndicesEngine</c>, which compares a historical
        /// education index against a present one and would report a drift the city never had.
        /// </summary>
        [Fact]
        public void EveryRecordedSample_ComesBackBitIdentical()
        {
            const int Months = 30;
            const int Retention = 128;

            SimDate start = Month(1990, 1);

            var original = new MetricHistory(Retention);
            SyntheticCityHistory.RecordAll(original, SyntheticCityHistory.Months(start, Months));

            // Through the wire form rather than the in-memory object: this is where a formatting
            // choice would cost precision, and the object copy could not show it.
            MetricHistoryFile written = AgoraJson.Deserialize<MetricHistoryFile>(
                AgoraJson.Serialize(original.ToFile()));

            MetricHistoryFile before = original.ToFile();

            Assert.Equal(before.Series.Count, written.Series.Count);

            for (int i = 0; i < before.Series.Count; i++)
            {
                Assert.Equal(before.Series[i].Series, written.Series[i].Series);
                Assert.Equal(before.Series[i].Samples.Count, written.Series[i].Samples.Count);

                for (int s = 0; s < before.Series[i].Samples.Count; s++)
                {
                    Assert.Equal(before.Series[i].Samples[s].TotalMonths,
                                 written.Series[i].Samples[s].TotalMonths);

                    // Exact, not to a tolerance. A tolerance here would accept the narrowing.
                    Assert.True(before.Series[i].Samples[s].Value == written.Series[i].Samples[s].Value,
                                "Series " + before.Series[i].Series + " lost precision on the wire.");
                }
            }
        }

        /// <summary>
        /// The whole path, end to end, for the city-statistics vocabulary: record an assembled
        /// snapshot, write the sidecar, read it back into a fresh history, rebuild the month — and get
        /// the measurements rather than the defaults.
        ///
        /// <para>
        /// The three tests above check shape, fingerprint and precision, and all three would pass on a
        /// history whose new series were filed and then never read. This one closes that: it goes
        /// through <c>SnapshotRehydration</c>, which is the only consumer that turns the file back
        /// into something the engine understands, and it asserts the values by name. Everything before
        /// this pass came back through it; a wave-3 <c>delta</c> trigger on homelessness or tourism
        /// needs the same to be true of these.
        /// </para>
        /// </summary>
        [Fact]
        public void TheWidenedSnapshot_SurvivesRecordFileRestoreAndRehydrate()
        {
            string root = TempRoot("widened-rehydrate");

            try
            {
                const int Retention = 128;

                SimDate start = Month(1990, 1);
                SimDate now = start.AddMonths(2);

                CitySnapshot latest = WidenedSnapshotFixture.Snapshot(now, 2);

                var recorded = new MetricHistory(Retention);
                recorded.RecordSnapshot(WidenedSnapshotFixture.Snapshot(start, 0));
                recorded.RecordSnapshot(WidenedSnapshotFixture.Snapshot(start.AddMonths(1), 1));
                recorded.RecordSnapshot(latest);

                var store = new SidecarStore(root, NullSidecarLog.Instance);
                Assert.True(store.SaveMetricHistory(Save, recorded.ToFile()));

                // A brand new instance, exactly as a fresh session gets.
                var reloaded = new MetricHistory(Retention);
                reloaded.RestoreFrom(store.LoadMetricHistory(Save), now);

                System.Collections.Generic.List<CitySnapshot> restored =
                    SnapshotRehydration.Restore(reloaded, now, Retention);

                Assert.Equal(3, restored.Count);

                // The newest rebuilt month is the one the fixture's `latest` describes, so the two are
                // directly comparable; the earlier two prove the month keying did not collapse them.
                Assert.Equal(now, restored[2].Date);
                WidenedSnapshotFixture.AssertCityStatisticsMatch(latest, restored[2]);

                // ...and the months either side are the months either side, not three copies of one.
                Assert.NotEqual(0.0, restored[0].Statistics.HomelessShare);
                Assert.Equal(restored[0].UncollectedGarbage, restored[2].UncollectedGarbage, 12);
                Assert.NotEqual(restored[0].Districts[0].UncollectedGarbage,
                                restored[2].Districts[0].UncollectedGarbage);
            }
            finally
            {
                Delete(root);
            }
        }

        // --- Fail soft ---------------------------------------------------------------------------

        /// <summary>
        /// A hand-edited or corrupted document is repaired sample by sample rather than refused. The
        /// sidecar's standing rule is that a file Agora cannot fully trust costs a feature, never a
        /// load — and a NaN in this one would poison every trend computed against it.
        /// </summary>
        [Fact]
        public void AMalformedDocument_IsRepairedRatherThanRefused()
        {
            var file = new MetricHistoryFile();

            file.Series.Add(null);
            file.Series.Add(new MetricSeriesFile { Series = "" });

            var good = new MetricSeriesFile { Series = "city/rent" };
            good.Samples.Add(new MetricSampleFile { TotalMonths = 100, Value = 10.0 });
            good.Samples.Add(null);
            good.Samples.Add(new MetricSampleFile { TotalMonths = 101, Value = double.NaN });
            good.Samples.Add(new MetricSampleFile { TotalMonths = 99, Value = 5.0 });   // out of order
            good.Samples.Add(new MetricSampleFile { TotalMonths = 102, Value = 12.0 });
            file.Series.Add(good);

            var history = new MetricHistory(64);
            history.RestoreFrom(file, new SimDate(1990, 1, 1).AddMonths(1000));

            // Only the two clean, ascending samples survive.
            Assert.Equal(2, history.SampleCount("city/rent"));
            Assert.Equal(0, history.SampleCount(""));
        }

        /// <summary>
        /// A file written before <c>schemaVersion</c> was mandatory still loads: <c>Migrate</c> treats
        /// an unversioned document as v1, which is the current version, and stamps it.
        /// </summary>
        [Fact]
        public void AnUnversionedFile_LoadsAsVersionOne()
        {
            string root = TempRoot("unversioned");

            try
            {
                string directory = Path.Combine(root, SidecarPaths.FormatGuid(Save));
                Directory.CreateDirectory(directory);

                File.WriteAllText(
                    Path.Combine(directory, SidecarPaths.MetricHistoryFileName),
                    "{\"series\":[{\"series\":\"city/rent\"," +
                    "\"samples\":[{\"totalMonths\":10,\"value\":1.5}]}]}");

                var store = new SidecarStore(root, NullSidecarLog.Instance);
                MetricHistoryFile loaded = store.LoadMetricHistory(Save);

                Assert.NotNull(loaded);
                Assert.Single(loaded.Series);
                Assert.Equal("city/rent", loaded.Series[0].Series);
                Assert.Equal(1.5, loaded.Series[0].Samples[0].Value, 12);
            }
            finally
            {
                Delete(root);
            }
        }

        /// <summary>
        /// The version constant and the empty step table have to stay in step. While the current
        /// version is 1 there is nothing to migrate from; bumping it without adding a step would turn
        /// every existing history into <c>NoPathForward</c>, i.e. discard it in silence.
        /// </summary>
        [Fact]
        public void TheVersionAndTheStepTable_Agree()
        {
            Assert.Equal(SidecarSchema.CurrentMetricHistoryVersion,
                         SidecarSchema.CurrentVersionOf(SidecarDocument.MetricHistory));

            Assert.Equal(1, SidecarSchema.CurrentMetricHistoryVersion);
        }

        // --- Temp directories --------------------------------------------------------------------

        private static string TempRoot(string name)
        {
            string path = Path.Combine(Path.GetTempPath(), "agora-metric-history-tests", name);
            Delete(path);
            Directory.CreateDirectory(path);
            return path;
        }

        private static void Delete(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a test over.
            }
        }
    }
}
