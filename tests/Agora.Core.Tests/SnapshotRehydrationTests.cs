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

        // --- Record and rehydrate are one set ----------------------------------------------------

        /// <summary>
        /// <b>The guard on the invariant.</b> Record a month, rehydrate it, record the rehydrated
        /// month again: the two histories must hold the same series with the same values. A metric
        /// filed by <c>MetricHistory.RecordSnapshot</c> but not read back by
        /// <c>SnapshotRehydration</c> comes back as the contract default, files as a zero on the
        /// second pass, and fails here — loudly, and without anyone having had to remember to extend
        /// a hand-written field list.
        ///
        /// <para>
        /// That is the whole reason the two live in one lane. The recorded set is exactly the set
        /// anything may trust off a historical snapshot; a field on the snapshot but not in the
        /// history reads as a fabricated zero for every month before the current session, so a player
        /// who plays straight through sees a trend fire and the same player who quits to menu never
        /// does.
        /// </para>
        /// </summary>
        /// <remarks>
        /// Two metrics are excluded, and the exclusion is the point rather than a loophole: both are
        /// stored as a <i>mean</i> of a struct that cannot be reconstructed from it. One pollution
        /// number cannot say which of four channels moved and one coverage number cannot say which of
        /// nine services is missing, so rehydration leaves both structs at their defaults rather than
        /// spreading the mean across the channels, which would be an invention. Anything added to the
        /// recorder that is not similarly lossy has to be rehydrated or this goes red — and adding a
        /// name to this list is a deliberate act that has to be argued for in a comment.
        /// </remarks>
        [Fact]
        public void EveryRecordedMetric_ComesBackThroughRehydration()
        {
            string[] lossyByDesign = { MetricHistory.PollutionMean, MetricHistory.ServiceCoverageMean };

            var recorded = new MetricHistory(Retention);
            recorded.RecordSnapshot(WidenedSnapshotFixture.Snapshot(Start, 0));

            List<CitySnapshot> rehydrated = SnapshotRehydration.Restore(recorded, Start, 1);
            Assert.Single(rehydrated);

            var again = new MetricHistory(Retention);
            again.RecordSnapshot(rehydrated[0]);

            Dictionary<string, double> before = SingleMonthValues(recorded, lossyByDesign);
            Dictionary<string, double> after = SingleMonthValues(again, lossyByDesign);

            // Not empty on either side, or the comparison below would agree about nothing.
            Assert.NotEmpty(before);

            Assert.Equal(SortedKeys(before), SortedKeys(after));

            foreach (KeyValuePair<string, double> entry in before)
            {
                Assert.True(entry.Value != 0.0,
                            "Fixture bug: " + entry.Key + " was recorded as zero, so a rehydration " +
                            "that dropped it would look identical to one that kept it.");

                Assert.Equal(entry.Value, after[entry.Key], 12);
            }
        }

        /// <summary>
        /// The city-statistics pass, field by field, off a history that has been through
        /// <c>ToFile</c> and <c>RestoreFrom</c>. The test above proves the two ends agree; this one
        /// proves what they agree on is the measurement rather than a shared zero, and it is the
        /// assertion a wave-3 <c>delta</c> trigger on any of these names is standing on.
        /// </summary>
        [Fact]
        public void ARehydratedMonth_ReportsTheCityStatisticsPass_RatherThanSilentZeros()
        {
            CitySnapshot original = WidenedSnapshotFixture.Snapshot(Start, 0);

            var recorded = new MetricHistory(Retention);
            recorded.RecordSnapshot(original);

            var reloaded = new MetricHistory(Retention);
            reloaded.RestoreFrom(recorded.ToFile(), Start);

            List<CitySnapshot> restored = SnapshotRehydration.Restore(reloaded, Start, 1);
            Assert.Single(restored);

            WidenedSnapshotFixture.AssertCityStatisticsMatch(original, restored[0]);
        }

        // --- The assembled snapshot ---------------------------------------------------------------
        //
        // Assembly rather than rehydration, but this lane owns two test files and this is the nearer
        // of the two: what SnapshotAssembly resolves is what MetricHistory files and what the tests
        // above read back.

        /// <summary>
        /// The three per-district counts go through the same best-effort path as every other district
        /// field — measured stands, unmeasured is named in <c>CityFallbackFields</c> — but they are
        /// the only three that fall back to <b>zero</b> rather than to the city figure.
        ///
        /// <para>
        /// They are sums, not averages, and a city sum is not an estimate of a district's share of it.
        /// The path only fires when a sensor has gone blind — both sensors seed every known district
        /// at zero first, so an empty district reports a measured zero — and in exactly that case the
        /// city total would hand every district a large, entirely credible number at once. Zero is
        /// also wrong, but it cannot manufacture a citywide garbage crisis out of a sensor gap.
        /// </para>
        ///
        /// <para>
        /// The marker is the load-bearing half either way: it is what tells a consumer the figure was
        /// never measured, and wave 2's <c>CheckResult.Unmeasurable</c> reads it.
        /// </para>
        /// </summary>
        [Fact]
        public void TheNewPerDistrictCounts_AreMarkedAsFallbacksOnlyWhenUnmeasured()
        {
            var city = new CityReading
            {
                UncollectedGarbage = 5_000.0,
                AttractionCount = 40,
                SignatureBuildingCount = 7,
            };

            var measured = new DistrictReading
            {
                Id = "d00000001",
                UncollectedGarbage = 120.0,
                AttractionCount = 3,
                SignatureBuildingCount = 1,
            };

            // Nothing measured at all: the whole trio falls back.
            var blind = new DistrictReading { Id = "d00000002" };

            CitySnapshot snapshot = SnapshotAssembly.Build(
                Start, city, new List<DistrictReading> { measured, blind });

            DistrictSnapshot first = snapshot.Districts[0];
            Assert.Equal(120.0, first.UncollectedGarbage, 12);
            Assert.Equal(3, first.AttractionCount);
            Assert.Equal(1, first.SignatureBuildingCount);
            Assert.DoesNotContain("UncollectedGarbage", first.CityFallbackFields);
            Assert.DoesNotContain("AttractionCount", first.CityFallbackFields);
            Assert.DoesNotContain("SignatureBuildingCount", first.CityFallbackFields);

            // Zero, and emphatically not the city's 5,000 / 40 / 7 — a total is not an estimate of a
            // part, and this district measured nothing at all.
            DistrictSnapshot second = snapshot.Districts[1];
            Assert.Equal(0.0, second.UncollectedGarbage, 12);
            Assert.Equal(0, second.AttractionCount);
            Assert.Equal(0, second.SignatureBuildingCount);

            // The zero on its own is indistinguishable from a district that genuinely holds nothing.
            // These three assertions are what carries the difference.
            Assert.True(second.HasCityFallbacks);
            Assert.Contains("UncollectedGarbage", second.CityFallbackFields);
            Assert.Contains("AttractionCount", second.CityFallbackFields);
            Assert.Contains("SignatureBuildingCount", second.CityFallbackFields);
        }

        /// <summary>
        /// A district that fell back on one of the three counts records <b>no sample</b> for it that
        /// month — an honest absence rather than a fabricated zero.
        ///
        /// <para>
        /// The same argument that keeps rent and land value out of <c>RecordSnapshot</c> entirely: a
        /// fabricated value written into a series poisons every window computed against it afterwards,
        /// and the fallback ruling only changed which fabrication it would be. An absent sample is
        /// what <c>TryValueAt</c> already reports as "not measured", so wave 2's <c>Unmeasurable</c>
        /// can be answered off a rehydrated month and not only off the live snapshot.
        /// </para>
        ///
        /// <para>
        /// <b>The regression guard is the last two assertions.</b> Skipping a field must cost that
        /// district the field, never the month — <c>SnapshotRehydration</c> decides whether a district
        /// existed in a month by probing its <c>population</c> series, so a change that made a
        /// fallback district skip more than the three counts would delete it from history altogether
        /// and hand the gentrification leg a hole instead of a baseline.
        /// </para>
        /// </summary>
        [Fact]
        public void ADistrictThatFellBackOnACount_RecordsNoSampleForIt_ButStillJoinsTheMonth()
        {
            var city = new CityReading
            {
                Population = 90_000,
                Happiness = 55.0,
                UncollectedGarbage = 5_000.0,
                AttractionCount = 40,
                SignatureBuildingCount = 7,
            };

            var measured = new DistrictReading
            {
                Id = "d00000001",
                Population = 50_000,
                Happiness = 56.0,
                UncollectedGarbage = 120.0,
                AttractionCount = 3,
                SignatureBuildingCount = 1,
            };

            // Population and happiness measured, the three counts not — the shape a blind tourism or
            // statistics sensor actually produces, rather than a wholly unmeasured district.
            var blind = new DistrictReading
            {
                Id = "d00000002",
                Population = 40_000,
                Happiness = 54.0,
            };

            CitySnapshot snapshot = SnapshotAssembly.Build(
                Start, city, new List<DistrictReading> { measured, blind });

            var history = new MetricHistory(Retention);
            history.RecordSnapshot(snapshot);

            string[] counts =
            {
                MetricHistory.UncollectedGarbage,
                MetricHistory.AttractionCount,
                MetricHistory.SignatureBuildingCount,
            };

            for (int i = 0; i < counts.Length; i++)
            {
                Assert.Equal(1, history.SampleCount(MetricHistory.DistrictKey("d00000001", counts[i])));
                Assert.Equal(0, history.SampleCount(MetricHistory.DistrictKey("d00000002", counts[i])));

                // City scope is unaffected: the city has nowhere to fall back to and so is never
                // fallback-marked.
                Assert.Equal(1, history.SampleCount(MetricHistory.CityKey(counts[i])));
            }

            // The fallback costs that district those three fields and nothing else.
            Assert.Equal(1, history.SampleCount(
                MetricHistory.DistrictKey("d00000002", MetricHistory.Population)));
            Assert.Equal(1, history.SampleCount(
                MetricHistory.DistrictKey("d00000002", MetricHistory.Happiness)));

            List<CitySnapshot> restored = SnapshotRehydration.Restore(history, Start, 1);
            Assert.Single(restored);

            // Still two districts in the rebuilt month: the probe is population, which is recorded
            // unconditionally, so a district that lost a count did not lose its place in history.
            Assert.Equal(2, restored[0].Districts.Count);

            DistrictSnapshot rebuilt = restored[0].Districts[1];
            Assert.Equal("d00000002", rebuilt.Id);
            Assert.Equal(40_000, rebuilt.Population);

            // ...and the three it never measured come back at the contract default rather than
            // carrying a zero that was filed as though it had been measured.
            Assert.Equal(0.0, rebuilt.UncollectedGarbage, 12);
            Assert.Equal(0, rebuilt.AttractionCount);
            Assert.Equal(0, rebuilt.SignatureBuildingCount);
        }

        /// <summary>
        /// Both new lists come out of assembly in the contract's order however the sensor happened to
        /// collect them — feature names ordinal ascending, tax rates by <c>(Area, ResourceIndex)</c>.
        /// The sensor sorts too, but a consumer relying on a sort it cannot see is relying on ECS
        /// chunk order, which is the determinism bug non-negotiable #3 names.
        /// </summary>
        [Fact]
        public void TheTwoNewLists_ComeOutSorted()
        {
            var city = new CityReading();
            city.UnlockedFeatureIds.Add("Zoning");
            city.UnlockedFeatureIds.Add("Electricity");
            city.UnlockedFeatureIds.Add("Garbage");

            // Deliberately in neither area order nor resource order.
            city.IndustryTaxRates.Add(new ResourceTaxRate(TaxArea.Office, 7, "Software", 0.11));
            city.IndustryTaxRates.Add(new ResourceTaxRate(TaxArea.Commercial, 9, "Food", 0.09));
            city.IndustryTaxRates.Add(new ResourceTaxRate(TaxArea.Industrial, 2, "Grain", 0.13));
            city.IndustryTaxRates.Add(new ResourceTaxRate(TaxArea.Commercial, 4, "Textiles", 0.10));

            CitySnapshot snapshot = SnapshotAssembly.Build(Start, city, new List<DistrictReading>());

            Assert.Equal(new[] { "Electricity", "Garbage", "Zoning" }, snapshot.UnlockedFeatureIds);

            var order = new List<string>();
            for (int i = 0; i < snapshot.IndustryTaxRates.Count; i++)
            {
                ResourceTaxRate rate = snapshot.IndustryTaxRates[i];
                order.Add(rate.Area + ":" + rate.ResourceIndex);
            }

            Assert.Equal(
                new[] { "Commercial:4", "Commercial:9", "Industrial:2", "Office:7" },
                order);
        }

        // --- helpers -----------------------------------------------------------------------------

        private static DerivedIndices Compute(CitySnapshot present, IReadOnlyList<CitySnapshot> history)
        {
            return IndicesEngine.Compute(
                new IndicesInput { Snapshot = present, History = history }, Tuning);
        }

        /// <summary>
        /// Every series in <paramref name="history"/> with its single sample's value, dropping the
        /// metrics named in <paramref name="excludedMetrics"/>. Built off <c>ToFile</c> because that
        /// is the one accessor that enumerates the whole store, and it sorts before it does.
        /// </summary>
        private static Dictionary<string, double> SingleMonthValues(MetricHistory history,
                                                                    string[] excludedMetrics)
        {
            var values = new Dictionary<string, double>(System.StringComparer.Ordinal);
            MetricHistoryFile file = history.ToFile();

            for (int i = 0; i < file.Series.Count; i++)
            {
                MetricSeriesFile series = file.Series[i];
                if (IsExcluded(series.Series, excludedMetrics)) continue;

                Assert.Single(series.Samples);
                values[series.Series] = series.Samples[0].Value;
            }

            return values;
        }

        private static bool IsExcluded(string seriesKey, string[] excludedMetrics)
        {
            for (int i = 0; i < excludedMetrics.Length; i++)
            {
                if (seriesKey.EndsWith("/" + excludedMetrics[i], System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<string> SortedKeys(Dictionary<string, double> values)
        {
            var keys = new List<string>(values.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            return keys;
        }
    }

    /// <summary>
    /// A single synthetic month in which <b>every</b> metric the recorder files is non-zero and
    /// distinct.
    ///
    /// <para>
    /// Separate from <c>SyntheticCityHistory</c>, which predates the city-statistics pass and leaves
    /// its fields at the contract default. That is fine for the golden indices comparison — a zero on
    /// both sides still agrees — but it is useless for proving a metric survived the round trip,
    /// because a dropped zero and a kept zero look identical. Synthetic rather than recorded, per
    /// <c>tests/CLAUDE.md</c>: it diffs cleanly and it does not rot when the schema gains a field.
    /// </para>
    /// </summary>
    internal static class WidenedSnapshotFixture
    {
        internal static readonly string[] DistrictIds = { "d00000001", "d00000002" };

        internal static CitySnapshot Snapshot(SimDate date, int monthIndex)
        {
            double i = monthIndex;

            var snapshot = new CitySnapshot
            {
                Date = date,

                // Everything the recorder already filed before this pass, held away from zero for the
                // same reason the new material is.
                Population = 121_000 + 137 * monthIndex,
                Happiness = 61.5 + 0.4 * i,
                Unemployment = 0.071 + 0.001 * i,
                CrimeRate = 0.033 + 0.001 * i,
                Education = new EducationDistribution(0.11 + 0.001 * i, 0.22, 0.29, 0.23, 0.15),
                Wealth = new WealthDistribution(0.41 - 0.001 * i, 0.34, 0.25 + 0.001 * i),
                Pollution = new PollutionLevels(0.11, 0.12, 0.13, 0.14),
                Services = new ServiceCoverage(0.61, 0.62, 0.63, 0.64, 0.65, 0.66, 0.67, 0.68, 0.69),
                AverageCommuteMinutes = 26.5 + 0.1 * i,
                TrafficCongestion = 0.37 + 0.002 * i,

                // The city-statistics pass. Every figure distinct, so a rehydration that read one
                // series into the wrong field is a failure rather than a coincidence.
                Statistics = new CityStatistics(137, 0.0231, 412, 233, 91, 64, 39, 18_342.5),
                Tourism = new TourismLevels(1_873, 47, 512, 780),
                Progression = new ProgressionState(9, 41_250, 0.63),
                UncollectedGarbage = 91_234.75,
                AttractionCount = 27,
                SignatureBuildingCount = 6,
            };

            for (int k = 0; k < DistrictIds.Length; k++)
            {
                snapshot.Districts.Add(District(k, monthIndex));
            }

            return snapshot;
        }

        private static DistrictSnapshot District(int index, int monthIndex)
        {
            double i = monthIndex;
            double k = index;

            return new DistrictSnapshot
            {
                Id = DistrictIds[index],
                Name = DistrictIds[index],

                Population = 41_000 + 500 * index + 31 * monthIndex,
                Happiness = 57.5 + 0.3 * i + 2.0 * k,
                Unemployment = 0.062 + 0.001 * i + 0.003 * k,
                CrimeRate = 0.028 + 0.001 * i + 0.002 * k,
                Education = new EducationDistribution(0.13 + 0.01 * k, 0.24, 0.28, 0.21, 0.14),
                Wealth = new WealthDistribution(0.43 - 0.01 * k, 0.33, 0.24 + 0.01 * k),
                Pollution = new PollutionLevels(0.09, 0.10, 0.11, 0.12),
                Services = new ServiceCoverage(0.51, 0.52, 0.53, 0.54, 0.55, 0.56, 0.57, 0.58, 0.59),

                UncollectedGarbage = 4_812.25 + 100.0 * k + 3.5 * i,
                AttractionCount = 11 + 4 * index,
                SignatureBuildingCount = 2 + index,
            };
        }

        /// <summary>
        /// Asserts that <paramref name="restored"/> carries the same city-statistics pass as
        /// <paramref name="original"/>, city and district. Field by field on purpose: this is the one
        /// place in the lane where the names are checked rather than the shape, so a series wired to
        /// the wrong property has somewhere to fail.
        /// </summary>
        internal static void AssertCityStatisticsMatch(CitySnapshot original, CitySnapshot restored)
        {
            Assert.Equal(original.Statistics.Homeless, restored.Statistics.Homeless);
            Assert.Equal(original.Statistics.HomelessShare, restored.Statistics.HomelessShare, 12);
            Assert.Equal(original.Statistics.CitizensMovedIn, restored.Statistics.CitizensMovedIn);
            Assert.Equal(original.Statistics.CitizensMovedAway, restored.Statistics.CitizensMovedAway);
            Assert.Equal(original.Statistics.MovedAwayUnhappy, restored.Statistics.MovedAwayUnhappy);
            Assert.Equal(original.Statistics.Births, restored.Statistics.Births);
            Assert.Equal(original.Statistics.Deaths, restored.Statistics.Deaths);
            Assert.Equal(original.Statistics.GarbageProductionRate,
                         restored.Statistics.GarbageProductionRate, 12);

            Assert.Equal(original.Tourism.Tourists, restored.Tourism.Tourists);
            Assert.Equal(original.Tourism.Attractiveness, restored.Tourism.Attractiveness);
            Assert.Equal(original.Tourism.LodgingUsed, restored.Tourism.LodgingUsed);
            Assert.Equal(original.Tourism.LodgingTotal, restored.Tourism.LodgingTotal);

            Assert.Equal(original.Progression.MilestoneLevel, restored.Progression.MilestoneLevel);
            Assert.Equal(original.Progression.Experience, restored.Progression.Experience);
            Assert.Equal(original.Progression.MilestoneProgress,
                         restored.Progression.MilestoneProgress, 12);

            Assert.Equal(original.UncollectedGarbage, restored.UncollectedGarbage, 12);
            Assert.Equal(original.AttractionCount, restored.AttractionCount);
            Assert.Equal(original.SignatureBuildingCount, restored.SignatureBuildingCount);

            Assert.Equal(original.Districts.Count, restored.Districts.Count);

            for (int i = 0; i < original.Districts.Count; i++)
            {
                DistrictSnapshot from = original.Districts[i];
                DistrictSnapshot to = restored.Districts[i];

                Assert.Equal(from.Id, to.Id);
                Assert.Equal(from.UncollectedGarbage, to.UncollectedGarbage, 12);
                Assert.Equal(from.AttractionCount, to.AttractionCount);
                Assert.Equal(from.SignatureBuildingCount, to.SignatureBuildingCount);
            }
        }
    }
}
