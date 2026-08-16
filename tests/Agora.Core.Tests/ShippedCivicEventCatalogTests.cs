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
        [Fact]
        public void AllDistrictsCheck_OnTheTriggersOwnMetric_IsClean()
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
                                ""threshold"": 0.25, ""scope"": ""allDistricts"" }
                  },
                  ""name"": ""n"", ""description"": ""d"", ""ignoreText"": ""i"", ""goalText"": ""g"",
                  ""powerOverrideText"": ""p"", ""successText"": ""s"", ""failText"": ""f""
                }
              ]
            }";

            CivicEventCatalogLoadResult result =
                CivicEventCatalogLoader.Load("synthetic.json", json, ShippedTuning());

            Assert.True(result.IsClean, Describe(result.Errors));
            Assert.Empty(result.Warnings);
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
