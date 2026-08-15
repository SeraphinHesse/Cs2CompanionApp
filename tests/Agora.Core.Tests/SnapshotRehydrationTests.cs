// Requires the Sensors and Persistence <Compile Link> lines in Agora.Core.Tests.csproj —
// SnapshotRehydration, MetricHistory and AgoraJson. See the comment there for why they are linked
// rather than referenced.

using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Engine.Indices;
using Agora.Core.Tuning;
using Agora.Mod.Persistence;
using Agora.Mod.Sensors;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// <c>SnapshotRehydration</c>: rebuilding past <see cref="CitySnapshot"/>s off
    /// <c>metric_history.json</c>, so the engine's trend window survives a reload.
    ///
    /// <para>
    /// The bug is the same shape as the one <c>MetricHistoryPersistenceTests</c> guards, one layer
    /// up. <c>AgoraRuntime</c> held its snapshot history in a session-static list that
    /// <c>ResetForNewSave</c> cleared at every save boundary, so <c>EngineTickInput.SnapshotHistory</c>
    /// was empty on the first tick after every load. A player who played twelve months straight saw
    /// the history-dependent indices move; the same player quitting to the menu each year never did.
    /// Nothing about that reads as broken — the engine was correctly computing zero from an empty
    /// history it should never have been given.
    /// </para>
    /// </summary>
    public class SnapshotRehydrationTests
    {
        /// <summary>The shipped defaults. Effectively immutable — every setter on it is internal.</summary>
        private static readonly EngineTuning Tuning = EngineTuning.Default;

        private static readonly SimDate Start = new SimDate(1990, 1, 1);

        /// <summary>
        /// Long enough to reach past <c>indices.gentrificationWindowMonths</c> (24), which is the
        /// wider of the two windows anything reads a historical snapshot for. A shorter history would
        /// leave the gentrification leg reading zero on both sides of the golden comparison and make
        /// it agree for the wrong reason.
        /// </summary>
        private const int HistoryMonths = 30;

        /// <summary>Above any sample count this fixture produces, so nothing is silently truncated.</summary>
        private const int Retention = 128;

        // --- The golden test ---------------------------------------------------------------------

        /// <summary>
        /// <b>The load-bearing one.</b> A full multi-month history, round-tripped through the metric
        /// ring and back out as snapshots, must score exactly the same as the history it was built
        /// from.
        ///
        /// <para>
        /// Deliberately not a field list. A hand-written assertion — "the rehydrated snapshot carries
        /// population, and education, and…" — only ever covers the fields whoever wrote it thought
        /// of, and the failure this guards against is precisely a field nobody thought of: a
        /// rehydrated snapshot leaves every unstored field at <c>0</c>, and a zero is
        /// indistinguishable from a measurement. So the assertion is on the only thing that actually
        /// consumes a historical snapshot. <c>IndicesEngine.Compute</c> is the sole reader of
        /// <c>SnapshotHistory</c> in the repo; if its answer is unchanged, the stored set is
        /// sufficient for every reader there is, and the day someone adds a historical read that is
        /// not stored, this goes red rather than quietly returning a zero.
        /// </para>
        ///
        /// <para>
        /// The present snapshot is the same object on both sides, so anything that differs is
        /// necessarily something the history carried. The recording end is
        /// <c>MetricHistory.RecordSnapshot</c> — the real one the snapshot system calls, not a
        /// reimplementation of it, which is why that method lives on the pure type rather than on the
        /// game-facing sensor: a test that files the series itself agrees with itself by construction.
        /// </para>
        /// </summary>
        [Fact]
        public void RehydratedHistory_ScoresIdenticallyToTheHistoryItWasBuiltFrom()
        {
            SimDate now = Start.AddMonths(HistoryMonths);

            List<CitySnapshot> original = SyntheticCityHistory.Months(Start, HistoryMonths);
            CitySnapshot present = SyntheticCityHistory.Snapshot(now, HistoryMonths);

            var recorded = new MetricHistory(Retention);
            SyntheticCityHistory.RecordAll(recorded, original);

            // The sensor records the month it is standing in as well. It comes back out of Restore
            // and IndicesEngine ignores it — only strictly earlier snapshots are eligible — so
            // recording it here also proves the present month cannot perturb the comparison.
            recorded.RecordSnapshot(present);

            var reloaded = new MetricHistory(Retention);
            reloaded.RestoreFrom(recorded.ToFile(), now);

            List<CitySnapshot> rehydrated = SnapshotRehydration.Restore(reloaded, now, Retention);

            DerivedIndices fromOriginal = Compute(present, original);

            // The guard on the guard. Every index in this comparison would agree if the
            // history-dependent legs read zero on both sides, which is exactly what an empty history
            // produces — so the fixture has to be proved to move them first, or the assertion below
            // passes against a rehydration that returned nothing at all.
            Assert.NotEqual(AgoraJson.Fingerprint(Compute(present, new List<CitySnapshot>())),
                            AgoraJson.Fingerprint(fromOriginal));

            Assert.Equal(AgoraJson.Fingerprint(fromOriginal),
                         AgoraJson.Fingerprint(Compute(present, rehydrated)));
        }

        // --- The published seam ------------------------------------------------------------------

        /// <summary>
        /// Oldest first, each snapshot carrying its own date, and nothing dated after the date being
        /// loaded into. The trim is the same rule <c>MetricHistory.RestoreFrom</c> follows and for the
        /// same reason: §5 allows a load to reconcile onto an earlier snapshot, and a history that
        /// still held next decade's measurements would compute a trend against a present that has not
        /// happened.
        /// </summary>
        [Fact]
        public void Restore_IsOldestFirst_AndCarriesNothingAfterTheDateLoadedInto()
        {
            var recorded = new MetricHistory(Retention);
            SyntheticCityHistory.RecordAll(recorded, SyntheticCityHistory.Months(Start, HistoryMonths));

            SimDate loadedAt = Start.AddMonths(11);

            var reloaded = new MetricHistory(Retention);
            reloaded.RestoreFrom(recorded.ToFile(), loadedAt);

            List<CitySnapshot> restored = SnapshotRehydration.Restore(reloaded, loadedAt, Retention);

            Assert.NotEmpty(restored);

            for (int i = 0; i < restored.Count; i++)
            {
                Assert.True(restored[i].Date <= loadedAt,
                            "Restore returned a snapshot dated after the date being loaded into.");

                if (i > 0)
                {
                    Assert.True(restored[i - 1].Date < restored[i].Date,
                                "Restore returned snapshots out of order; the contract is oldest first.");
                }
            }

            // The month being loaded into is the present, not the future, so it is kept.
            Assert.Equal(loadedAt, restored[restored.Count - 1].Date);
        }

        /// <summary>
        /// The cap keeps the newest months, not the oldest. A window measured backwards from the
        /// present that was handed the start of the save instead would read a decades-old baseline as
        /// "last year".
        /// </summary>
        [Fact]
        public void Restore_CapsAtTheRequestedMonthCount_KeepingTheNewest()
        {
            SimDate now = Start.AddMonths(HistoryMonths - 1);

            var recorded = new MetricHistory(Retention);
            SyntheticCityHistory.RecordAll(recorded, SyntheticCityHistory.Months(Start, HistoryMonths));

            List<CitySnapshot> restored = SnapshotRehydration.Restore(recorded, now, 6);

            Assert.Equal(6, restored.Count);
            Assert.Equal(now.AddMonths(-5), restored[0].Date);
            Assert.Equal(now, restored[restored.Count - 1].Date);
        }

        /// <summary>
        /// A save with no recorded history — every save made before <c>metric_history.json</c>
        /// existed. An empty list is the correct answer and not an error, and it must not be null:
        /// the caller assigns it straight into <c>EngineTickInput.SnapshotHistory</c>.
        /// </summary>
        [Fact]
        public void Restore_OnASaveWithNoHistory_IsEmptyRatherThanNull()
        {
            List<CitySnapshot> restored =
                SnapshotRehydration.Restore(new MetricHistory(Retention), Start, Retention);

            Assert.NotNull(restored);
            Assert.Empty(restored);
        }

        // --- helpers -----------------------------------------------------------------------------

        private static DerivedIndices Compute(CitySnapshot present, IReadOnlyList<CitySnapshot> history)
        {
            return IndicesEngine.Compute(
                new IndicesInput { Snapshot = present, History = history }, Tuning);
        }
    }
}
