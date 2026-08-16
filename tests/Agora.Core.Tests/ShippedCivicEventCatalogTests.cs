using System;
using System.Collections.Generic;
using System.IO;
using Agora.Core.Contracts;
using Agora.Core.Events.Catalog;
using Agora.Core.Stories;
using Agora.Core.Stories.Catalog;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The build-time gate on <c>data/events_global.json</c>, <c>events_eu.json</c> and
    /// <c>events_na.json</c> — the civic events three content lanes author in parallel, unable to see
    /// each other's files and unable to run the game.
    ///
    /// <para>
    /// <b>It is landed in the wave-3 spine, before any content exists</b>, so every content lane has
    /// it from its first commit. That is the point: a catalog gate written after the catalog is a
    /// report, and a catalog gate written before it is a specification. It passes on the empty
    /// catalogs the spine ships and acquires teeth as events land.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Loads all three <b>as one merged catalog</b>, the way the story assembler will, because the two
    /// failures that parallel authoring actually produces are invisible to a per-file load: an id
    /// claimed in two files, and a feature id used in one file that only exists in another's
    /// allow-list.
    /// </remarks>
    public class ShippedCivicEventCatalogTests
    {
        private static readonly string[] CatalogFiles =
        {
            "events_global.json",
            "events_eu.json",
            "events_na.json",
        };

        // ------------------------------------------------------------------ fixtures

        private static string RepoRoot()
        {
            // AppContext.BaseDirectory, not Environment.CurrentDirectory: the runner's cwd varies,
            // the assembly's own location does not.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Agora.sln"))) return dir.FullName;
                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate the repository root (no Agora.sln above " + AppContext.BaseDirectory + ").");
        }

        private static string CatalogPath(string fileName) => Path.Combine(RepoRoot(), "data", fileName);

        /// <summary>
        /// The tuning the game will actually run on, not <see cref="EngineTuning.Default"/>. The
        /// palette these catalogs are checked against lives in that file, and
        /// <c>ShippedTuningTests.ShippedTuningFile_MatchesBuiltInDefaults</c> separately proves the
        /// two agree — so reading the file here means a future palette change is checked against the
        /// catalogs on the same run that introduces it.
        /// </summary>
        private static EngineTuning ShippedTuning() =>
            EngineTuning.FromJson(File.ReadAllText(Path.Combine(RepoRoot(), "data", "engine_tuning.json")));

        private static List<CivicEventCatalogSource> AllSources()
        {
            var sources = new List<CivicEventCatalogSource>(CatalogFiles.Length);
            foreach (string file in CatalogFiles)
            {
                string path = CatalogPath(file);
                Assert.True(File.Exists(path), file + " must ship.");
                sources.Add(new CivicEventCatalogSource(file, File.ReadAllText(path)));
            }

            return sources;
        }

        private static CivicEventCatalogLoadResult LoadAll() =>
            CivicEventCatalogLoader.Load(AllSources(), ShippedTuning());

        private static string Describe(IReadOnlyList<CatalogIssue> issues)
        {
            var lines = new List<string>(issues.Count);
            for (int i = 0; i < issues.Count; i++) lines.Add(issues[i].ToString());
            return string.Join(Environment.NewLine, lines);
        }

        // ------------------------------------------------------------------ the gate

        /// <summary>
        /// Nothing rejected, nothing in error. This is the assertion the content lanes are working
        /// against, and the failure message names the offending file, event and property.
        /// </summary>
        [Fact]
        public void ShippedCatalogs_LoadWithNothingRejected()
        {
            CivicEventCatalogLoadResult result = LoadAll();

            Assert.True(result.Errors.Count == 0,
                "the shipped civic event catalogs must load without errors:" + Environment.NewLine +
                Describe(result.Errors));
            Assert.Equal(0, result.RejectedEventCount);
            Assert.True(result.IsClean);
        }

        /// <summary>
        /// Warnings are authoring feedback, and the shipped catalogs are held to zero of them. A
        /// warning that is tolerated in the tree is a warning nobody reads.
        /// </summary>
        [Fact]
        public void ShippedCatalogs_RaiseNoWarnings()
        {
            CivicEventCatalogLoadResult result = LoadAll();

            Assert.True(result.Warnings.Count == 0,
                "the shipped civic event catalogs must load without warnings:" + Environment.NewLine +
                Describe(result.Warnings));
        }

        /// <summary>
        /// Every id unique across all three documents, and the merged list sorted by id ordinal — the
        /// order <c>EventPoolWeighting.Compare</c> breaks its last tie on.
        /// </summary>
        [Fact]
        public void ShippedCatalogs_HaveUniqueIdsInSortedOrder()
        {
            IReadOnlyList<CivicEvent> events = LoadAll().Catalog.Events;

            for (int i = 1; i < events.Count; i++)
            {
                Assert.True(string.CompareOrdinal(events[i - 1].Id, events[i].Id) < 0,
                    "ids are not unique and sorted ordinal ascending at '" + events[i].Id + "'");
            }
        }

        /// <summary>
        /// <b>The independent pass.</b> Re-derives the two rules that matter most, against the loaded
        /// catalog rather than by asking the loader whether the loader is happy. If a validation rule
        /// is ever dropped from <see cref="CivicEventCatalogLoader"/>, this is what notices.
        /// </summary>
        [Fact]
        public void ShippedCatalogs_NameOnlyReachableMetricsAndEffects()
        {
            EngineTuning tuning = ShippedTuning();
            CivicEventCatalog catalog = LoadAll().Catalog;

            foreach (CivicEvent civic in catalog.Events)
            {
                AssertSpecIsReachable(civic, civic.Trigger, "trigger", catalog);
                AssertSpecIsReachable(civic, civic.Check.Spec, "check.spec", catalog);

                AssertEffectsExist(civic, civic.ActiveEffects, "activeEffects", tuning);
                AssertEffectsExist(civic, civic.SuccessEffects, "successEffects", tuning);
                AssertEffectsExist(civic, civic.FailureEffects, "failureEffects", tuning);
            }
        }

        private static void AssertSpecIsReachable(CivicEvent civic, TriggerSpec spec, string where,
                                                  CivicEventCatalog catalog)
        {
            switch (spec.Kind)
            {
                case TriggerKind.Metric:
                case TriggerKind.Delta:
                    Assert.True(MetricRegistry.IsKnown(spec.MetricId, spec.Scope),
                        civic.Id + " " + where + " names '" + spec.MetricId +
                        "', which the metric registry cannot read at that scope");

                    // The census gate, re-derived. Until wave 1's AGORA-STATCOLLECTION gate is walked
                    // these five could be per-in-game-day rates or totals since the city was founded,
                    // and an absolute threshold cannot be authored correctly against an unknown unit.
                    if (spec.Kind != TriggerKind.Delta)
                    {
                        Assert.False(IsCensusGated(spec.MetricId),
                            civic.Id + " " + where + " puts an ABSOLUTE threshold on '" + spec.MetricId +
                            "', whose units are unresolved until the AGORA-STATCOLLECTION gate is " +
                            "walked; only a delta spec may name it");
                    }
                    break;

                case TriggerKind.Unlock:
                    Assert.Contains(spec.MetricId, catalog.DeclaredFeatureIds);
                    break;

                case TriggerKind.Policy:
                    Assert.Fail(civic.Id + " " + where + " is a policy spec, and nothing writes " +
                                "CitySnapshot.ActivePolicyIds: it can never fire, and under absent it " +
                                "would fire on every city forever");
                    break;

                case TriggerKind.Absent:
                    Assert.True(MetricRegistry.IsKnown(spec.MetricId, spec.Scope) ||
                                Contains(catalog.DeclaredFeatureIds, spec.MetricId),
                        civic.Id + " " + where + " is an ABSENT spec naming '" + spec.MetricId +
                        "', which resolves to neither a registry metric nor a declared feature id — " +
                        "so it negates nothing and evaluates Met on every city, forever");
                    break;
            }
        }

        private static void AssertEffectsExist(CivicEvent civic, IReadOnlyList<string> effects,
                                               string where, EngineTuning tuning)
        {
            for (int i = 0; i < effects.Count; i++)
            {
                EffectCap cap;
                Assert.True(tuning.Effects.TryGetEffect(effects[i], out cap),
                    civic.Id + " " + where + " names '" + effects[i] +
                    "', which is not in the closed effect palette (effects.perEffect)");
            }
        }

        private static bool IsCensusGated(string metricId) =>
            Contains(CivicEventCatalogLoader.CensusGatedMetricIds, metricId);

        private static bool Contains(IReadOnlyList<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.CompareOrdinal(values[i], value) == 0) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ the loader's own rules

        /// <summary>
        /// The census gate refuses an absolute spec and accepts a delta on the same metric. Written
        /// against a synthetic document rather than the shipped ones, so it keeps proving the rule
        /// after the content lanes have (correctly) stopped authoring anything that violates it.
        /// </summary>
        [Fact]
        public void CensusGatedMetric_RejectsAbsoluteAndAcceptsDelta()
        {
            EngineTuning tuning = ShippedTuning();

            CivicEventCatalogLoadResult absolute = CivicEventCatalogLoader.Load(
                "synthetic.json", Document(Spec("metric", MetricRegistry.Births, threshold: 10)), tuning);
            Assert.Equal(1, absolute.RejectedEventCount);
            Assert.Contains(absolute.Errors,
                issue => issue.Code == CatalogIssueCode.CensusGatedMetricNeedsDelta);

            CivicEventCatalogLoadResult delta = CivicEventCatalogLoader.Load(
                "synthetic.json",
                Document(Spec("delta", MetricRegistry.Births, threshold: 10, windowMonths: 3)), tuning);
            Assert.True(delta.IsClean, Describe(delta.Errors));
        }

        /// <summary>
        /// A policy spec is refused by name, with the reason. This is the rule that would otherwise
        /// ship an <c>absent</c> trigger which is true of every city forever.
        /// </summary>
        [Fact]
        public void PolicySpec_IsRejectedWithItsOwnReason()
        {
            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(
                "synthetic.json", Document(Spec("policy", "anything")), ShippedTuning());

            Assert.Equal(1, result.RejectedEventCount);
            Assert.Contains(result.Errors, issue => issue.Code == CatalogIssueCode.PolicyTriggerUnsupported);
        }

        /// <summary>
        /// The misspelling case, stated as the trap it is: an <c>absent</c> spec whose metric id is a
        /// typo falls through to feature membership, matches nothing, and negates to <c>Met</c>. The
        /// allow-list is what turns that into a load error.
        /// </summary>
        [Fact]
        public void AbsentSpec_WithUndeclaredId_IsRejected()
        {
            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(
                "synthetic.json", Document(Spec("absent", "homlessShare")), ShippedTuning());

            Assert.Equal(1, result.RejectedEventCount);
            Assert.Contains(result.Errors, issue => issue.Code == CatalogIssueCode.UnlockIdNotDeclared);
        }

        /// <summary>A metric id the registry does not carry is a load error, not a runtime surprise.</summary>
        [Fact]
        public void UnknownMetricId_IsRejected()
        {
            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(
                "synthetic.json", Document(Spec("metric", "notAMetric", threshold: 1)), ShippedTuning());

            Assert.Equal(1, result.RejectedEventCount);
            Assert.Contains(result.Errors, issue => issue.Code == CatalogIssueCode.UnknownMetricId);
        }

        /// <summary>
        /// A delta window wider than the retained history can never be answered, so it is refused
        /// against the value read from tuning rather than against a literal.
        /// </summary>
        [Fact]
        public void DeltaWindow_BeyondRetention_IsRejected()
        {
            EngineTuning tuning = ShippedTuning();
            int beyond = tuning.Scheduler.SnapshotRetention + 1;

            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(
                "synthetic.json",
                Document(Spec("delta", MetricRegistry.Happiness, threshold: 1, windowMonths: beyond)),
                tuning);

            Assert.Equal(1, result.RejectedEventCount);
            Assert.Contains(result.Errors, issue => issue.Code == CatalogIssueCode.WindowMonthsOutOfRange);
        }

        /// <summary>
        /// A relative check at district scope is refused, because it is provably unscoreable rather
        /// than merely unwise: <c>StoryAssembler.Baseline</c> returns null for every non-city scope.
        /// </summary>
        [Fact]
        public void RelativeCheck_AtDistrictScope_IsRejected()
        {
            string json = @"{
              ""schemaVersion"": 1,
              ""events"": [
                {
                  ""id"": ""synthetic-event"",
                  ""severity"": 2,
                  ""region"": ""global"",
                  ""trigger"": { ""kind"": ""metric"", ""metricId"": ""crimeRate"", ""comparison"": ""gte"",
                                 ""threshold"": 0.4, ""scope"": ""anyDistrict"" },
                  ""check"": {
                    ""relativeToBaseline"": true,
                    ""spec"": { ""kind"": ""metric"", ""metricId"": ""crimeRate"", ""comparison"": ""lt"",
                                ""threshold"": -0.05, ""scope"": ""anyDistrict"" }
                  },
                  ""name"": ""n"", ""description"": ""d"", ""ignoreText"": ""i"", ""goalText"": ""g"",
                  ""powerOverrideText"": ""p"", ""successText"": ""s"", ""failText"": ""f""
                }
              ]
            }";

            CivicEventCatalogLoadResult result =
                CivicEventCatalogLoader.Load("synthetic.json", json, ShippedTuning());

            Assert.Equal(1, result.RejectedEventCount);
            Assert.Contains(result.Errors,
                issue => issue.Code == CatalogIssueCode.BaselineCheckAtDistrictScope);
        }

        /// <summary>
        /// A district check reading the same metric as its district trigger is warned about, because
        /// the best district in the city answers it rather than the one the story is about. The
        /// shipped catalogs are held to zero warnings, so this stops such an event shipping.
        /// </summary>
        [Fact]
        public void DistrictCheck_OnTheTriggersOwnMetric_IsWarnedAbout()
        {
            string json = @"{
              ""schemaVersion"": 1,
              ""events"": [
                {
                  ""id"": ""synthetic-event"",
                  ""severity"": 2,
                  ""region"": ""global"",
                  ""trigger"": { ""kind"": ""metric"", ""metricId"": ""crimeRate"", ""comparison"": ""gte"",
                                 ""threshold"": 0.4, ""scope"": ""anyDistrict"" },
                  ""check"": {
                    ""spec"": { ""kind"": ""metric"", ""metricId"": ""crimeRate"", ""comparison"": ""lt"",
                                ""threshold"": 0.25, ""scope"": ""anyDistrict"" }
                  },
                  ""name"": ""n"", ""description"": ""d"", ""ignoreText"": ""i"", ""goalText"": ""g"",
                  ""powerOverrideText"": ""p"", ""successText"": ""s"", ""failText"": ""f""
                }
              ]
            }";

            CivicEventCatalogLoadResult result =
                CivicEventCatalogLoader.Load("synthetic.json", json, ShippedTuning());

            // A warning, so the entry still loads — the shape is a judgement, not an impossibility.
            Assert.Equal(0, result.RejectedEventCount);
            Assert.Single(result.Catalog.Events);
            Assert.Contains(result.Warnings,
                issue => issue.Code == CatalogIssueCode.DistrictCheckNotBoundToTrigger);
        }

        /// <summary>
        /// The paired positive case: <c>allDistricts</c> on the trigger's own metric is the repair,
        /// and must not warn. Without this, a loader that warned on every district check would pass
        /// the test above while making the rule useless.
        /// </summary>
        /// <remarks>
        /// The threshold here is the trigger's own, and that is load-bearing rather than incidental.
        /// The first version of this fixture demanded <c>0.25</c> against a trigger at <c>0.4</c> —
        /// itself a trap band, which the rule below caught the moment it was written. A positive
        /// fixture carrying the defect a sibling rule exists to find is worth more as a warning than
        /// as a passing test.
        /// </remarks>
        [Fact]
        public void AllDistrictsCheck_OnTheTriggersOwnMetric_IsClean()
        {
            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(
                "synthetic.json", DistrictPairDocument("gte", 0.4, "lt", 0.4), ShippedTuning());

            Assert.True(result.IsClean, Describe(result.Errors));
            Assert.Empty(result.Warnings);
        }

        /// <summary>
        /// A check tighter than its trigger leaves a band of districts that never contributed to the
        /// trigger and can still fail the story.
        /// </summary>
        [Theory]
        [InlineData("gte", 0.40, "lt", 0.35)]   // high-is-bad: districts in [0.35, 0.40) are trapped
        [InlineData("lte", 0.30, "gt", 0.35)]   // low-is-bad, mirrored
        public void CheckTighterThanItsTrigger_IsWarnedAbout(string triggerCmp, double triggerAt,
                                                             string checkCmp, double checkAt)
        {
            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(
                "synthetic.json", DistrictPairDocument(triggerCmp, triggerAt, checkCmp, checkAt),
                ShippedTuning());

            Assert.Equal(0, result.RejectedEventCount);
            Assert.Contains(result.Warnings,
                issue => issue.Code == CatalogIssueCode.CheckThresholdLeavesTrapBand);
        }

        /// <summary>
        /// The boundary cases: an exact complement is silent whichever way the strictness falls, and
        /// a check <i>looser</i> than its trigger is not a trap. The rule is about the band, not the
        /// boundary — without these it could degrade into "any district check warns".
        /// </summary>
        [Theory]
        [InlineData("gte", 0.40, "lt", 0.40)]   // exact complement
        [InlineData("gt", 0.40, "lte", 0.40)]   // exact complement, opposite strictness
        [InlineData("gte", 0.40, "lt", 0.50)]   // looser than the trigger: no trapped district
        public void CheckThatLeavesNoBand_IsClean(string triggerCmp, double triggerAt,
                                                  string checkCmp, double checkAt)
        {
            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(
                "synthetic.json", DistrictPairDocument(triggerCmp, triggerAt, checkCmp, checkAt),
                ShippedTuning());

            Assert.True(result.IsClean, Describe(result.Errors));
            Assert.Empty(result.Warnings);
        }

        private static string DistrictPairDocument(string triggerCmp, double triggerAt,
                                                   string checkCmp, double checkAt)
        {
            string t = triggerAt.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            string c = checkAt.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

            return @"{
              ""schemaVersion"": 1,
              ""events"": [
                {
                  ""id"": ""synthetic-event"",
                  ""severity"": 2,
                  ""region"": ""global"",
                  ""trigger"": { ""kind"": ""metric"", ""metricId"": ""crimeRate"", ""comparison"": """ +
                  triggerCmp + @""", ""threshold"": " + t + @", ""scope"": ""anyDistrict"" },
                  ""check"": {
                    ""spec"": { ""kind"": ""metric"", ""metricId"": ""crimeRate"", ""comparison"": """ +
                    checkCmp + @""", ""threshold"": " + c + @", ""scope"": ""allDistricts"" }
                  },
                  ""name"": ""n"", ""description"": ""d"", ""ignoreText"": ""i"", ""goalText"": ""g"",
                  ""powerOverrideText"": ""p"", ""successText"": ""s"", ""failText"": ""f""
                }
              ]
            }";
        }

        /// <summary>
        /// A delta check may not read back further than the story has existed. A story lives
        /// <c>cycleMonths - 1</c> months, not <c>cycleMonths</c> — the cadence and the life differ by
        /// one, which is the distinction every wave-3 content lane was handed wrongly.
        /// </summary>
        [Fact]
        public void DeltaCheck_ReadingBackFurtherThanTheStoryLives_IsWarnedAbout()
        {
            EngineTuning tuning = ShippedTuning();
            int life = tuning.Stories.CycleMonths - 1;

            CivicEventCatalogLoadResult beyond = CivicEventCatalogLoader.Load(
                "synthetic.json", DeltaCheckDocument(life + 1), tuning);

            Assert.Contains(beyond.Warnings,
                issue => issue.Code == CatalogIssueCode.CheckWindowOutrunsStoryLife);

            // The paired positive: exactly the story's life is the whole window the player owns.
            CivicEventCatalogLoadResult atLife = CivicEventCatalogLoader.Load(
                "synthetic.json", DeltaCheckDocument(life), tuning);

            Assert.True(atLife.IsClean, Describe(atLife.Errors));
            Assert.Empty(atLife.Warnings);
        }

        private static string DeltaCheckDocument(int windowMonths)
        {
            string w = windowMonths.ToString(System.Globalization.CultureInfo.InvariantCulture);

            return @"{
              ""schemaVersion"": 1,
              ""events"": [
                {
                  ""id"": ""synthetic-event"",
                  ""severity"": 2,
                  ""region"": ""global"",
                  ""trigger"": { ""kind"": ""metric"", ""metricId"": ""happiness"", ""comparison"": ""lt"",
                                 ""threshold"": 50 },
                  ""check"": {
                    ""spec"": { ""kind"": ""delta"", ""metricId"": ""happiness"", ""comparison"": ""gte"",
                                ""threshold"": 1, ""windowMonths"": " + w + @" }
                  },
                  ""name"": ""n"", ""description"": ""d"", ""ignoreText"": ""i"", ""goalText"": ""g"",
                  ""powerOverrideText"": ""p"", ""successText"": ""s"", ""failText"": ""f""
                }
              ]
            }";
        }

        /// <summary>
        /// A mirror-negated outcome pressure is warned about. Pressures are salience, not credit —
        /// flipping the sign moves voters to the opposite pole rather than releasing the issue.
        /// </summary>
        [Theory]
        [InlineData("successPressure")]
        [InlineData("failurePressure")]
        public void MirroredOutcomePressure_IsWarnedAbout(string outcomeKey)
        {
            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(
                "synthetic.json", PressureDocument(outcomeKey, -0.25), ShippedTuning());

            Assert.Equal(0, result.RejectedEventCount);
            Assert.Contains(result.Warnings, issue => issue.Code == CatalogIssueCode.PressureSignFlip);
        }

        /// <summary>
        /// The paired positive cases: same sign at any magnitude is the authored shape, and dropping
        /// the issue to zero is a legitimate way to say it stopped mattering. Without these, a loader
        /// that warned on every outcome pressure would pass the test above.
        /// </summary>
        [Theory]
        [InlineData(0.10)]  // quieter — the ordinary success shape
        [InlineData(0.45)]  // louder — the ordinary failure shape
        [InlineData(0.0)]   // dropped entirely — not a flip
        public void OutcomePressure_InTheSameDirection_IsClean(double outcomeValue)
        {
            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(
                "synthetic.json", PressureDocument("successPressure", outcomeValue), ShippedTuning());

            Assert.True(result.IsClean, Describe(result.Errors));
            Assert.Empty(result.Warnings);
        }

        private static string PressureDocument(string outcomeKey, double outcomeValue)
        {
            string value = outcomeValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

            return @"{
              ""schemaVersion"": 1,
              ""events"": [
                {
                  ""id"": ""synthetic-event"",
                  ""severity"": 2,
                  ""region"": ""global"",
                  ""trigger"": { ""kind"": ""metric"", ""metricId"": ""happiness"", ""comparison"": ""lt"",
                                 ""threshold"": 50 },
                  ""check"": { ""spec"": { ""kind"": ""metric"", ""metricId"": ""happiness"",
                                           ""comparison"": ""gte"", ""threshold"": 55 } },
                  ""activePressure"": { ""services"": 0.30 },
                  """ + outcomeKey + @""": { ""services"": " + value + @" },
                  ""name"": ""n"", ""description"": ""d"", ""ignoreText"": ""i"", ""goalText"": ""g"",
                  ""powerOverrideText"": ""p"", ""successText"": ""s"", ""failText"": ""f""
                }
              ]
            }";
        }

        /// <summary>
        /// A threshold above what a metric's sensor can ever report is warned about, and the two
        /// known ceilings are asserted against the arithmetic that produces them rather than as
        /// remembered numbers.
        /// </summary>
        /// <remarks>
        /// The ceilings exist because both metrics are means over channels the game does not all
        /// expose: <c>serviceCoverage</c> is five measured of nine, <c>pollution</c> three of four.
        /// If a sensor ever starts reporting one of the missing channels, the arithmetic here changes
        /// with it and this test says so.
        /// </remarks>
        [Fact]
        public void ThresholdAboveTheSensorCeiling_IsWarnedAbout()
        {
            Assert.Equal(5.0 / 9.0, CivicEventCatalogLoader.AttainableMaximum(MetricRegistry.ServiceCoverageMean));
            Assert.Equal(3.0 / 4.0, CivicEventCatalogLoader.AttainableMaximum(MetricRegistry.PollutionMean));
            Assert.Null(CivicEventCatalogLoader.AttainableMaximum(MetricRegistry.Happiness));

            double ceiling = CivicEventCatalogLoader.AttainableMaximum(MetricRegistry.ServiceCoverageMean)!.Value;

            CivicEventCatalogLoadResult above = CivicEventCatalogLoader.Load(
                "synthetic.json", CeilingDocument(MetricRegistry.ServiceCoverageMean, ceiling + 0.05),
                ShippedTuning());
            Assert.Contains(above.Warnings,
                issue => issue.Code == CatalogIssueCode.ThresholdAboveAttainableMaximum);

            // The paired positive: at the ceiling is attainable, and must stay silent. Demanding is
            // not the same as impossible, and this loader only claims to catch the second.
            CivicEventCatalogLoadResult atCeiling = CivicEventCatalogLoader.Load(
                "synthetic.json", CeilingDocument(MetricRegistry.ServiceCoverageMean, ceiling),
                ShippedTuning());
            Assert.True(atCeiling.IsClean, Describe(atCeiling.Errors));
            Assert.Empty(atCeiling.Warnings);
        }

        /// <summary>
        /// An <c>absent</c> spec is checked against the ceiling too, because it <b>negates</b> what it
        /// reads: an inner condition that can never be met is not inert, it is true forever.
        /// </summary>
        /// <remarks>
        /// <c>absent serviceCoverage gte 0.9</c> has an inner condition above the 5/9 ceiling, so the
        /// inside can never be <c>Met</c> — and the negation therefore fires on every city, in every
        /// month, silently. That is the outcome codes 108 and 109 exist to close, arriving through a
        /// door the first version of rule 121 left open by scoping itself to <c>metric</c> alone.
        /// Found by lane 3e, which declined to write a test pinning the hole as correct — the right
        /// instinct, since such a test would have blessed it.
        /// </remarks>
        [Fact]
        public void AbsentSpec_AboveTheSensorCeiling_IsWarnedAbout()
        {
            double ceiling = CivicEventCatalogLoader.AttainableMaximum(MetricRegistry.ServiceCoverageMean)!.Value;

            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(
                "synthetic.json", AbsentCeilingDocument(ceiling + 0.35), ShippedTuning());

            Assert.Contains(result.Warnings,
                issue => issue.Code == CatalogIssueCode.ThresholdAboveAttainableMaximum);

            // The paired positive: an absent spec inside the attainable range negates something the
            // city can actually satisfy, which is a real question and must stay silent.
            CivicEventCatalogLoadResult within = CivicEventCatalogLoader.Load(
                "synthetic.json", AbsentCeilingDocument(ceiling - 0.2), ShippedTuning());

            Assert.True(within.IsClean, Describe(within.Errors));
            Assert.Empty(within.Warnings);
        }

        private static string AbsentCeilingDocument(double threshold)
        {
            string t = threshold.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

            return @"{
              ""schemaVersion"": 1,
              ""events"": [
                {
                  ""id"": ""synthetic-event"",
                  ""severity"": 2,
                  ""region"": ""global"",
                  ""trigger"": { ""kind"": ""absent"", ""metricId"": ""serviceCoverage"",
                                 ""comparison"": ""gte"", ""threshold"": " + t + @" },
                  ""check"": { ""spec"": { ""kind"": ""metric"", ""metricId"": ""happiness"",
                                           ""comparison"": ""gte"", ""threshold"": 55 } },
                  ""name"": ""n"", ""description"": ""d"", ""ignoreText"": ""i"", ""goalText"": ""g"",
                  ""powerOverrideText"": ""p"", ""successText"": ""s"", ""failText"": ""f""
                }
              ]
            }";
        }

        private static string CeilingDocument(string metricId, double threshold)
        {
            string t = threshold.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

            return @"{
              ""schemaVersion"": 1,
              ""events"": [
                {
                  ""id"": ""synthetic-event"",
                  ""severity"": 2,
                  ""region"": ""global"",
                  ""trigger"": { ""kind"": ""metric"", ""metricId"": """ + metricId + @""",
                                 ""comparison"": ""gte"", ""threshold"": " + t + @" },
                  ""check"": { ""spec"": { ""kind"": ""metric"", ""metricId"": ""happiness"",
                                           ""comparison"": ""gte"", ""threshold"": 55 } },
                  ""name"": ""n"", ""description"": ""d"", ""ignoreText"": ""i"", ""goalText"": ""g"",
                  ""powerOverrideText"": ""p"", ""successText"": ""s"", ""failText"": ""f""
                }
              ]
            }";
        }

        /// <summary>A corrupt document contributes nothing and does not throw (non-negotiable #7).</summary>
        [Fact]
        public void MalformedJson_ReportsRatherThanThrows()
        {
            CivicEventCatalogLoadResult result =
                CivicEventCatalogLoader.Load("synthetic.json", "{ not json", ShippedTuning());

            Assert.Contains(result.Errors, issue => issue.Code == CatalogIssueCode.MalformedJson);
            Assert.Empty(result.Catalog.Events);
        }

        // ------------------------------------------------------------------ the adaptation policy

        /// <summary>
        /// Every id in <c>timeline_adaptation.json</c> names a timeline event that actually exists,
        /// and every <c>authored</c> policy names a civic event that actually exists.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the cost of the owner's non-destructive ruling, and it is worth paying. Expressing
        /// the 25/50/25 split in a side file instead of by editing the timeline catalogs keeps those
        /// catalogs intact — but it creates a second list that can drift from the first, and a
        /// <c>timelineEventId</c> that matches nothing would silently adapt nothing while reading as a
        /// deliberate decision. Deleting the entries would have had no drift to guard; this is the
        /// trade, made explicit.
        /// </para>
        /// </remarks>
        [Fact]
        public void AdaptationPolicy_NamesOnlyEventsThatExist()
        {
            string path = Path.Combine(RepoRoot(), "data", "timeline_adaptation.json");
            Assert.True(File.Exists(path), "timeline_adaptation.json must ship.");

            using System.Text.Json.JsonDocument doc =
                System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));

            HashSet<string> timelineIds = ShippedTimelineEventIds();
            var civicIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (CivicEvent civic in LoadAll().Catalog.Events) civicIds.Add(civic.Id);

            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (System.Text.Json.JsonElement entry in doc.RootElement.GetProperty("policies").EnumerateArray())
            {
                string id = entry.GetProperty("timelineEventId").GetString() ?? "";
                string policy = entry.GetProperty("policy").GetString() ?? "";

                Assert.True(timelineIds.Contains(id),
                    "timeline_adaptation.json names '" + id + "', which is not an event in any " +
                    "timeline catalog; the policy would silently adapt nothing");

                Assert.True(seen.Add(id),
                    "timeline_adaptation.json names '" + id + "' twice; which entry wins would be " +
                    "decided by read order");

                if (string.CompareOrdinal(policy, "authored") == 0)
                {
                    string civicId = entry.TryGetProperty("civicEventId", out System.Text.Json.JsonElement c)
                        ? (c.GetString() ?? "")
                        : "";

                    Assert.True(civicIds.Contains(civicId),
                        "'" + id + "' is marked authored but its civicEventId '" + civicId +
                        "' is not in any civic event catalog");
                }
            }
        }

        private static HashSet<string> ShippedTimelineEventIds()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            string[] files = { "timeline_global.json", "timeline_eu.json", "timeline_na.json" };

            foreach (string file in files)
            {
                using System.Text.Json.JsonDocument doc =
                    System.Text.Json.JsonDocument.Parse(File.ReadAllText(CatalogPath(file)));

                foreach (System.Text.Json.JsonElement e in doc.RootElement.GetProperty("events").EnumerateArray())
                {
                    ids.Add(e.GetProperty("id").GetString() ?? "");
                }
            }

            return ids;
        }

        // ------------------------------------------------------------------ synthetic fixtures

        private static string Spec(string kind, string metricId, double? threshold = null,
                                   int? windowMonths = null)
        {
            string json = "{ \"kind\": \"" + kind + "\", \"metricId\": \"" + metricId + "\"";
            if (threshold.HasValue)
            {
                json += ", \"comparison\": \"gte\", \"threshold\": " +
                        threshold.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            if (windowMonths.HasValue)
            {
                json += ", \"windowMonths\": " +
                        windowMonths.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return json + " }";
        }

        /// <summary>
        /// A single-event document whose only interesting part is the spec under test. Every other
        /// field is filled with something valid so a failure can only be about the spec.
        /// </summary>
        private static string Document(string spec)
        {
            return @"{
              ""schemaVersion"": 1,
              ""events"": [
                {
                  ""id"": ""synthetic-event"",
                  ""severity"": 2,
                  ""region"": ""global"",
                  ""trigger"": " + spec + @",
                  ""check"": { ""spec"": " + spec + @" },
                  ""name"": ""A synthetic event"",
                  ""description"": ""Exists only to exercise one validation rule."",
                  ""ignoreText"": ""Do nothing."",
                  ""goalText"": ""Fix it."",
                  ""powerOverrideText"": ""Make it go away."",
                  ""successText"": ""It was fixed."",
                  ""failText"": ""It was not fixed.""
                }
              ]
            }";
        }
    }
}
