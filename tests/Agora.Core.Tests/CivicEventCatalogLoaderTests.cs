using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Events.Catalog;
using Agora.Core.Stories;
using Agora.Core.Stories.Catalog;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Wave 3 — the negative path through <see cref="CivicEventCatalogLoader"/>.
    ///
    /// <para>
    /// <c>ShippedCivicEventCatalogTests</c> proves the shipped catalogs load. This suite proves the
    /// complement: that the loader <b>rejects what it claims to reject</b>, and degrades the way it
    /// promises to when it does.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the pedantic-looking rules are worth a test each.</b> <c>TriggerKind.Absent</c> negates
    /// whatever its spec resolves to, so an id that resolves to <i>nothing</i> — a typo, a policy id,
    /// an undeclared feature name — does not fail. It reads as "not present" and fires <c>Met</c> on
    /// every city, forever, silently. Several of the loader's rules exist solely to turn that outcome
    /// into a load-time error, and each of them looks like something a later simplification would
    /// delete. This suite is what stops that.
    /// </para>
    /// <para>
    /// Every assertion is on a <see cref="CatalogIssueCode"/>, never on message text, which is free to
    /// be reworded. Bounds come from tuning — the delta window ceiling is
    /// <c>scheduler.snapshotRetention - 1</c>, read rather than memorised, so a retention change
    /// re-checks the rule instead of turning this file red for an unrelated reason.
    /// </para>
    /// </remarks>
    public class CivicEventCatalogLoaderTests
    {
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

        /// <summary>
        /// The tuning the game will actually run on, matching <c>ShippedCivicEventCatalogTests</c>.
        /// The severity ceiling, the effect palette and the snapshot retention every bound below is
        /// derived from all live in that file.
        /// </summary>
        private static EngineTuning ShippedTuning() =>
            EngineTuning.FromJson(File.ReadAllText(Path.Combine(RepoRoot(), "data", "engine_tuning.json")));

        private static readonly EngineTuning Tuning = ShippedTuning();

        /// <summary>The widest delta window that can ever be answered, read from tuning.</summary>
        private static int MaxDeltaWindow =>
            Tuning.Scheduler.SnapshotRetention > 1 ? Tuning.Scheduler.SnapshotRetention - 1 : 1;

        private const string ValidSpec =
            "{\"kind\":\"metric\",\"metricId\":\"happiness\",\"comparison\":\"lt\",\"threshold\":0.4}";

        private static readonly string[] ProseFields =
        {
            "name", "description", "ignoreText", "goalText", "powerOverrideText", "successText", "failText"
        };

        // ================================================================== 100 MalformedSpec

        [Fact]
        public void MalformedSpec_WhenTheTriggerIsAbsent()
        {
            AssertRejected(LoadOne(EventJson(trigger: null, omitTrigger: true)), CatalogIssueCode.MalformedSpec);
        }

        [Fact]
        public void MalformedSpec_WhenTheTriggerIsNotAnObject()
        {
            AssertRejected(LoadOne(EventJson(trigger: "\"happiness < 0.4\"")), CatalogIssueCode.MalformedSpec);
        }

        [Fact]
        public void MalformedSpec_WhenTheCheckIsAbsent()
        {
            // A check is what a Goal response is scored against; without one the response has no verdict.
            AssertRejected(LoadOne(EventJson(check: null, omitCheck: true)), CatalogIssueCode.MalformedSpec);
        }

        [Fact]
        public void MalformedSpec_WhenTheCheckCarriesNoSpec()
        {
            AssertRejected(LoadOne(EventJson(check: "{}")), CatalogIssueCode.MalformedSpec);
        }

        [Fact]
        public void MalformedSpec_WhenRelativeToBaselineIsNotABoolean()
        {
            AssertRejected(LoadOne(EventJson(
                check: "{\"spec\":" + ValidSpec + ",\"relativeToBaseline\":1}")),
                CatalogIssueCode.MalformedSpec);
        }

        // ================================================================== 101 UnknownTriggerKind

        [Theory]
        [InlineData("\"sudden\"")]   // not a kind
        [InlineData("\"Metric\"")]   // the vocabulary is ordinal, not case-insensitive
        [InlineData("5")]            // the enum's numeric value is not the wire format
        [InlineData(null)]           // absent
        public void UnknownTriggerKind_ForAnythingOutsideTheDeclaredVocabulary(string? kind)
        {
            AssertRejected(LoadOne(EventJson(trigger: SpecJson(kind: kind))),
                CatalogIssueCode.UnknownTriggerKind);
        }

        // ================================================================== 102 UnknownComparison

        [Theory]
        [InlineData("\"eq\"")]
        [InlineData("\">=\"")]
        [InlineData("\"GTE\"")]
        [InlineData("3")]
        public void UnknownComparison_ForAnythingOutsideTheFourDeclaredComparisons(string comparison)
        {
            AssertRejected(LoadOne(EventJson(trigger: SpecJson(comparison: comparison))),
                CatalogIssueCode.UnknownComparison);
        }

        /// <summary>
        /// An omitted comparison is not an error — it defaults to <c>gte</c>. Paired with the theory
        /// above so "reject everything" could not pass both.
        /// </summary>
        [Fact]
        public void UnknownComparison_IsNotRaisedWhenComparisonIsSimplyOmitted()
        {
            CivicEventCatalogLoadResult result = LoadOne(EventJson(trigger: SpecJson(comparison: null)));

            AssertClean(result);
            Assert.Equal(Comparison.GreaterThanOrEqual, Assert.Single(result.Catalog.Events).Trigger.Comparison);
        }

        // ================================================================== 103 UnknownTriggerScope

        [Theory]
        [InlineData("\"borough\"")]
        [InlineData("\"district\"")]      // the effect vocabulary, not the trigger one
        [InlineData("\"anydistrict\"")]
        [InlineData("0")]
        public void UnknownTriggerScope_ForAnythingOutsideTheThreeDeclaredScopes(string scope)
        {
            AssertRejected(LoadOne(EventJson(trigger: SpecJson(scope: scope))),
                CatalogIssueCode.UnknownTriggerScope);
        }

        // ================================================================== 104 UnknownMetricId

        [Theory]
        [InlineData("\"notAMetric\"")]
        [InlineData("\"Happiness\"")]     // registry ids are ordinal
        [InlineData("\"\"")]
        [InlineData(null)]
        public void UnknownMetricId_ForAnIdTheRegistryCannotRead(string? metricId)
        {
            AssertRejected(LoadOne(EventJson(trigger: SpecJson(metricId: metricId))),
                CatalogIssueCode.UnknownMetricId);
        }

        /// <summary>
        /// <b>Scope discrimination.</b> <c>commuteMinutes</c> is real at city scope and does not exist
        /// at district scope (wave 1 ruling 3: no <c>CityStatistics</c> scalar is per-district), so the
        /// same id must be accepted under <c>city</c> and refused under both district scopes. A loader
        /// that consulted only the city list would pass the accept and fail here.
        /// </summary>
        [Theory]
        [InlineData("\"anyDistrict\"")]
        [InlineData("\"allDistricts\"")]
        public void UnknownMetricId_ForACityOnlyMetricReadAtDistrictScope(string scope)
        {
            Assert.True(MetricRegistry.IsKnown(MetricRegistry.CommuteMinutes, TriggerScope.City),
                "the fixture assumes commuteMinutes is a real city metric");

            AssertRejected(
                LoadOne(EventJson(trigger: SpecJson(metricId: "\"" + MetricRegistry.CommuteMinutes + "\"",
                                                    scope: scope))),
                CatalogIssueCode.UnknownMetricId);
        }

        [Fact]
        public void CityOnlyMetric_LoadsAtCityScope()
        {
            CivicEventCatalogLoadResult result = LoadOne(EventJson(
                trigger: SpecJson(metricId: "\"" + MetricRegistry.CommuteMinutes + "\"", scope: "\"city\"")));

            AssertClean(result);
            Assert.Equal(TriggerScope.City, Assert.Single(result.Catalog.Events).Trigger.Scope);
        }

        /// <summary>
        /// A district-scope metric loads at district scope — otherwise the theory above would pass on a
        /// loader that simply refused every district trigger.
        /// </summary>
        [Fact]
        public void DistrictMetric_LoadsAtDistrictScope()
        {
            AssertClean(LoadOne(EventJson(
                trigger: SpecJson(metricId: "\"" + MetricRegistry.CrimeRate + "\"", scope: "\"anyDistrict\""))));
        }

        // ================================================================== 105 ThresholdNotFinite

        [Theory]
        [InlineData("\"0.4\"")]   // a string, not a number
        [InlineData(null)]        // absent: 0.0 is a real threshold, not an obviously-missing one
        public void ThresholdNotFinite_OnAMetricSpec(string? threshold)
        {
            AssertRejected(LoadOne(EventJson(trigger: SpecJson(threshold: threshold))),
                CatalogIssueCode.ThresholdNotFinite);
        }

        /// <summary>
        /// A delta spec compares a number too, so it needs a threshold on the same terms.
        /// </summary>
        [Fact]
        public void ThresholdNotFinite_OnADeltaSpec()
        {
            AssertRejected(LoadOne(EventJson(trigger: SpecJson(
                kind: "\"delta\"", threshold: null, windowMonths: "3"))),
                CatalogIssueCode.ThresholdNotFinite);
        }

        /// <summary>
        /// An <c>absent</c> spec that resolves to a registry metric is a negated threshold, so it is
        /// held to the same requirement — this is the branch a "feature ids need no threshold"
        /// simplification would quietly drop.
        /// </summary>
        [Fact]
        public void ThresholdNotFinite_OnAnAbsentSpecThatResolvesToAMetric()
        {
            AssertRejected(LoadOne(EventJson(trigger: SpecJson(kind: "\"absent\"", threshold: null))),
                CatalogIssueCode.ThresholdNotFinite);
        }

        // ================================================================== 106 WindowMonthsOutOfRange

        /// <summary>
        /// The bound is <c>scheduler.snapshotRetention - 1</c>: a delta over W months needs a sample W
        /// months back, and anything at or beyond retention evaluates Unmeasurable for the life of
        /// every save. Read from tuning, never pinned to a literal.
        /// </summary>
        [Fact]
        public void WindowMonthsOutOfRange_AtAndBeyondTheRetentionBound()
        {
            int max = MaxDeltaWindow;

            AssertClean(LoadOne(EventJson(trigger: DeltaSpec(max))));
            AssertClean(LoadOne(EventJson(trigger: DeltaSpec(1))));

            AssertRejected(LoadOne(EventJson(trigger: DeltaSpec(max + 1))),
                CatalogIssueCode.WindowMonthsOutOfRange);
            AssertRejected(LoadOne(EventJson(trigger: DeltaSpec(0))),
                CatalogIssueCode.WindowMonthsOutOfRange);
            AssertRejected(LoadOne(EventJson(trigger: DeltaSpec(-1))),
                CatalogIssueCode.WindowMonthsOutOfRange);
        }

        [Theory]
        [InlineData("2.5")]     // fractional months are not a window
        [InlineData("\"3\"")]
        [InlineData(null)]      // absent
        public void WindowMonthsOutOfRange_ForANonIntegerWindow(string? windowMonths)
        {
            AssertRejected(LoadOne(EventJson(trigger: SpecJson(kind: "\"delta\"", windowMonths: windowMonths))),
                CatalogIssueCode.WindowMonthsOutOfRange);
        }

        // ================================================================== 107 CensusGatedMetricNeedsDelta

        /// <summary>
        /// All five census-gated ids, swept from the loader's own list rather than retyped: an absolute
        /// threshold is refused and a delta over the same id loads. Their units are unresolved until
        /// wave 1's <c>AGORA-STATCOLLECTION</c> gate is walked, and a delta survives that ambiguity in
        /// direction where an absolute threshold does not survive it at all.
        /// </summary>
        [Fact]
        public void CensusGatedMetricNeedsDelta_ForEveryGatedIdAndForNoOther()
        {
            Assert.NotEmpty(CivicEventCatalogLoader.CensusGatedMetricIds);

            foreach (string metricId in CivicEventCatalogLoader.CensusGatedMetricIds)
            {
                string quoted = "\"" + metricId + "\"";

                AssertRejected(LoadOne(EventJson(trigger: SpecJson(metricId: quoted))),
                    CatalogIssueCode.CensusGatedMetricNeedsDelta);

                AssertClean(LoadOne(EventJson(trigger: SpecJson(
                    kind: "\"delta\"", metricId: quoted, windowMonths: "3"))));
            }

            // An ungated metric is untouched by the rule.
            AssertClean(LoadOne(EventJson(trigger: SpecJson(
                metricId: "\"" + MetricRegistry.Happiness + "\""))));
        }

        /// <summary>
        /// The gate applies to the <c>absent</c> branch that resolves to a registry metric too —
        /// <c>absent</c> is a negated absolute reading, and negating an unknown unit is no better.
        /// </summary>
        [Fact]
        public void CensusGatedMetricNeedsDelta_UnderAnAbsentSpec()
        {
            AssertRejected(LoadOne(EventJson(trigger: SpecJson(
                kind: "\"absent\"", metricId: "\"" + MetricRegistry.Births + "\""))),
                CatalogIssueCode.CensusGatedMetricNeedsDelta);
        }

        // ================================================================== 108 PolicyTriggerUnsupported

        /// <summary>
        /// <c>policy</c> parses as a kind and is then refused by name, so the finding states the real
        /// reason rather than "unknown kind". Nothing writes <c>CitySnapshot.ActivePolicyIds</c>: a
        /// policy spec is permanently <c>NotMet</c> and an absent one permanently <c>Met</c>.
        /// </summary>
        [Fact]
        public void PolicyTriggerUnsupported_InsteadOfUnknownTriggerKind()
        {
            CivicEventCatalogLoadResult result = LoadOne(EventJson(
                trigger: SpecJson(kind: "\"policy\"", metricId: "\"anything\"")));

            AssertRejected(result, CatalogIssueCode.PolicyTriggerUnsupported);
            Assert.DoesNotContain(result.Errors, e => e.Code == CatalogIssueCode.UnknownTriggerKind);
        }

        /// <summary>The refusal holds on the check side as well as the trigger side.</summary>
        [Fact]
        public void PolicyTriggerUnsupported_OnACheckSpec()
        {
            AssertRejected(
                LoadOne(EventJson(check: "{\"spec\":" + SpecJson(kind: "\"policy\"", metricId: "\"anything\"") + "}")),
                CatalogIssueCode.PolicyTriggerUnsupported);
        }

        // ================================================================== 109 UnlockIdNotDeclared

        [Theory]
        [InlineData("\"unlock\"")]
        [InlineData("\"absent\"")]
        public void UnlockIdNotDeclared_ForAFeatureNameNoDocumentDeclared(string kind)
        {
            AssertRejected(
                LoadOne(EventJson(trigger: SpecJson(kind: kind, metricId: "\"Metro\"", threshold: null))),
                CatalogIssueCode.UnlockIdNotDeclared);
        }

        /// <summary>
        /// <b>The misspelling trap, stated as itself.</b> <c>homlessShare</c> is not a registry metric,
        /// so an <c>absent</c> spec falls through to feature membership, matches nothing, negates
        /// nothing and reads <c>Met</c> on every city forever. The allow-list is the only thing that
        /// makes it a load error.
        /// </summary>
        [Fact]
        public void UnlockIdNotDeclared_ForATypoedMetricIdUnderAbsent()
        {
            Assert.False(MetricRegistry.IsKnown("homlessShare", TriggerScope.City));

            AssertRejected(
                LoadOne(EventJson(trigger: SpecJson(kind: "\"absent\"", metricId: "\"homlessShare\"",
                                                    threshold: null))),
                CatalogIssueCode.UnlockIdNotDeclared);
        }

        /// <summary>
        /// A real metric read at a scope that does not carry it falls through to the feature branch
        /// under <c>absent</c> — and is caught there. Both halves of the fall-through are closed.
        /// </summary>
        [Fact]
        public void UnlockIdNotDeclared_WhenAnAbsentSpecNamesACityOnlyMetricAtDistrictScope()
        {
            AssertRejected(
                LoadOne(EventJson(trigger: SpecJson(kind: "\"absent\"",
                                                    metricId: "\"" + MetricRegistry.CommuteMinutes + "\"",
                                                    scope: "\"anyDistrict\"", threshold: null))),
                CatalogIssueCode.UnlockIdNotDeclared);
        }

        [Fact]
        public void UnlockIdNotDeclared_WhenAnUnlockSpecCarriesNoMetricIdAtAll()
        {
            AssertRejected(
                LoadOne(EventJson(trigger: SpecJson(kind: "\"unlock\"", metricId: null, threshold: null))),
                CatalogIssueCode.UnlockIdNotDeclared);
        }

        [Fact]
        public void UnlockSpec_LoadsWhenItsFeatureIdIsDeclared()
        {
            string json = Doc(featureIds: "[\"Metro\"]",
                events: EventJson(trigger: SpecJson(kind: "\"unlock\"", metricId: "\"Metro\"", threshold: null)));

            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load("events_global.json", json, Tuning);

            AssertClean(result);
            Assert.Equal(new[] { "Metro" }, result.Catalog.DeclaredFeatureIds);
        }

        // ================================================================== 110 MalformedFeatureIds

        [Theory]
        [InlineData("\"Metro\"")]           // a bare string, not an array
        [InlineData("{\"a\":\"Metro\"}")]
        [InlineData("7")]
        public void MalformedFeatureIds_WhenTheAllowListIsNotAnArray(string featureIds)
        {
            CivicEventCatalogLoadResult result =
                CivicEventCatalogLoader.Load("events_global.json", Doc(featureIds: featureIds), Tuning);

            Assert.Contains(result.Errors, e => e.Code == CatalogIssueCode.MalformedFeatureIds);
        }

        [Theory]
        [InlineData("[\"\"]")]
        [InlineData("[42]")]
        [InlineData("[null]")]
        [InlineData("[\"Metro\", \"\"]")]
        public void MalformedFeatureIds_WhenAnEntryIsNotANonEmptyString(string featureIds)
        {
            CivicEventCatalogLoadResult result =
                CivicEventCatalogLoader.Load("events_global.json", Doc(featureIds: featureIds), Tuning);

            Assert.Contains(result.Errors, e => e.Code == CatalogIssueCode.MalformedFeatureIds);
        }

        /// <summary>
        /// A broken allow-list is an error against the document, but it does not reject the document's
        /// events — an event that never names a feature id is unaffected by it. Recorded here because
        /// it is the one place in this suite where an error coexists with an accepted entry.
        /// </summary>
        [Fact]
        public void MalformedFeatureIds_DoesNotRejectEventsThatNameNoFeature()
        {
            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(
                "events_global.json", Doc(featureIds: "[\"\"]", events: EventJson()), Tuning);

            Assert.Contains(result.Errors, e => e.Code == CatalogIssueCode.MalformedFeatureIds);
            Assert.Equal(0, result.RejectedEventCount);
            Assert.Single(result.Catalog.Events);
        }

        // ================================================================== 111 MissingProse

        /// <summary>
        /// All seven prose fields are required and blank is not "author it later": each is rendered on
        /// a surface the player acts from, and an empty <c>goalText</c> is a button with no label.
        /// </summary>
        [Theory]
        [InlineData("name")]
        [InlineData("description")]
        [InlineData("ignoreText")]
        [InlineData("goalText")]
        [InlineData("powerOverrideText")]
        [InlineData("successText")]
        [InlineData("failText")]
        public void MissingProse_WhenAnyOneOfTheSevenIsAbsent(string field)
        {
            AssertRejected(LoadOne(EventJson(omitProse: field)), CatalogIssueCode.MissingProse);
        }

        [Theory]
        [InlineData("\"\"")]
        [InlineData("\"   \"")]      // whitespace is blank
        [InlineData("\"\\t\\n\"")]
        [InlineData("42")]           // not a string
        public void MissingProse_WhenAProseFieldIsBlankOrNotAString(string value)
        {
            AssertRejected(LoadOne(EventJson(proseField: "goalText", proseValue: value)),
                CatalogIssueCode.MissingProse);
        }

        // ================================================================== 112 MalformedEffectList

        [Theory]
        [InlineData("activeEffects")]
        [InlineData("successEffects")]
        [InlineData("failureEffects")]
        public void MalformedEffectList_WhenTheListIsNotAnArray(string key)
        {
            AssertRejected(LoadOne(EventJson(extra: "\"" + key + "\":\"district-wellbeing\"")),
                CatalogIssueCode.MalformedEffectList);
        }

        [Theory]
        [InlineData("[\"\"]")]
        [InlineData("[null]")]
        [InlineData("[7]")]
        [InlineData("[{\"effectId\":\"district-wellbeing\"}]")]   // the timeline shape, not this one
        public void MalformedEffectList_WhenAnEntryIsNotANonEmptyString(string effects)
        {
            AssertRejected(LoadOne(EventJson(activeEffects: effects)), CatalogIssueCode.MalformedEffectList);
        }

        // ================================================================== 113 MalformedDistrictAffinity

        [Theory]
        [InlineData("\"industrial\"")]
        [InlineData("{\"a\":1}")]
        [InlineData("[\"\"]")]
        [InlineData("[3]")]
        public void MalformedDistrictAffinity_WhenItIsNotAnArrayOfNonEmptyStrings(string affinity)
        {
            AssertRejected(LoadOne(EventJson(districtAffinity: affinity)),
                CatalogIssueCode.MalformedDistrictAffinity);
        }

        [Fact]
        public void DistrictAffinity_LoadsSortedOrdinal()
        {
            CivicEventCatalogLoadResult result =
                LoadOne(EventJson(districtAffinity: "[\"industrial\",\"affluent\",\"blue-collar\"]"));

            AssertClean(result);
            Assert.Equal(new[] { "affluent", "blue-collar", "industrial" },
                Assert.Single(result.Catalog.Events).DistrictAffinity);
        }

        // ================================================================== 114 DuplicateEffectId (warning)

        /// <summary>
        /// A warning, not an error: the entry loads and the duplicate is dropped, because two identical
        /// requests would stack against <c>maxStoryEffectsPerModifier</c> for no authored reason.
        /// </summary>
        [Fact]
        public void DuplicateEffectId_WarnsAndDropsTheDuplicateWithoutRejectingTheEvent()
        {
            CivicEventCatalogLoadResult result = LoadOne(EventJson(
                activeEffects: "[\"district-wellbeing\",\"city-attractiveness\",\"district-wellbeing\"]"));

            Assert.Empty(result.Errors);
            Assert.Equal(0, result.RejectedEventCount);
            Assert.Contains(result.Warnings, w => w.Code == CatalogIssueCode.DuplicateEffectId);
            Assert.Contains(result.Warnings, w => w.Severity == CatalogIssueSeverity.Warning &&
                                                  w.Code == CatalogIssueCode.DuplicateEffectId);

            // Sorted by id and de-duplicated, because the story effect builder walks them in order.
            Assert.Equal(new[] { "city-attractiveness", "district-wellbeing" },
                Assert.Single(result.Catalog.Events).ActiveEffects);
        }

        // ================================================================== 115 BaselineOnNonMetricCheck (warning)

        /// <summary>
        /// A baseline is a recorded metric reading, so the flag on any other kind is authoring
        /// confusion rather than a request. A warning, because ignoring it changes nothing about what
        /// the check does — the entry must still load, flag and all.
        /// </summary>
        [Theory]
        [InlineData("\"manual\"")]
        [InlineData("\"unlock\"")]
        public void BaselineOnNonMetricCheck_WarnsButKeepsTheEvent(string kind)
        {
            string spec = string.CompareOrdinal(kind, "\"unlock\"") == 0
                ? SpecJson(kind: kind, metricId: "\"Metro\"", threshold: null)
                : SpecJson(kind: kind, metricId: null, threshold: null, comparison: null);

            string json = Doc(featureIds: "[\"Metro\"]",
                events: EventJson(check: "{\"spec\":" + spec + ",\"relativeToBaseline\":true}"));

            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load("events_global.json", json, Tuning);

            Assert.Empty(result.Errors);
            Assert.Equal(0, result.RejectedEventCount);
            Assert.Contains(result.Warnings, w => w.Code == CatalogIssueCode.BaselineOnNonMetricCheck);
            Assert.True(Assert.Single(result.Catalog.Events).Check.RelativeToBaseline);
        }

        [Theory]
        [InlineData("\"metric\"")]
        [InlineData("\"delta\"")]
        public void BaselineOnNonMetricCheck_IsNotRaisedOnAReadingShapedKind(string kind)
        {
            string spec = string.CompareOrdinal(kind, "\"delta\"") == 0 ? DeltaSpec(3) : ValidSpec;

            CivicEventCatalogLoadResult result = LoadOne(EventJson(
                check: "{\"spec\":" + spec + ",\"relativeToBaseline\":true}"));

            AssertClean(result);
        }

        // ================================================================== the degradation contract

        /// <summary>
        /// <b>A bad entry rejects that entry and not the document.</b> One broken event between two
        /// good ones costs exactly itself.
        /// </summary>
        [Fact]
        public void OneBadEntry_CostsOnlyItself()
        {
            string json = Doc(events: new[]
            {
                EventJson(id: "\"glob-first\""),
                EventJson(id: "\"glob-broken\"", trigger: SpecJson(metricId: "\"notAMetric\"")),
                EventJson(id: "\"glob-third\"")
            });

            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load("events_global.json", json, Tuning);

            Assert.Equal(1, result.RejectedEventCount);
            Assert.Contains(result.Errors, e => e.Code == CatalogIssueCode.UnknownMetricId);
            Assert.Equal(new[] { "glob-first", "glob-third" }, Ids(result));

            // The finding names the offender, so a log line is actionable without opening the file.
            CatalogIssue issue = Assert.Single(result.Errors, e => e.Code == CatalogIssueCode.UnknownMetricId);
            Assert.Equal("glob-broken", issue.EventId);
            Assert.Equal("events_global.json", issue.SourceName);
        }

        /// <summary>
        /// A corrupt document contributes nothing, does not throw, and does not take its siblings with
        /// it (non-negotiable #7).
        /// </summary>
        [Fact]
        public void ACorruptDocument_ContributesNothingAndSpares_TheOthers()
        {
            var sources = new[]
            {
                new CivicEventCatalogSource("events_eu.json", "{ \"schemaVersion\": 1, \"events\": ["),
                new CivicEventCatalogSource("events_global.json", Doc(events: EventJson(id: "\"glob-survivor\"")))
            };

            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(sources, Tuning);

            Assert.Contains(result.Errors, e => e.Code == CatalogIssueCode.MalformedJson);
            Assert.Equal(new[] { "glob-survivor" }, Ids(result));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not json at all")]
        [InlineData("[1, 2, 3]")]
        [InlineData("{ \"schemaVersion\": 1, \"events\": [ { ")]
        public void ACorruptDocument_NeverThrows(string json)
        {
            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load("events_global.json", json, Tuning);

            Assert.NotEmpty(result.Errors);
            Assert.Empty(result.Catalog.Events);
        }

        /// <summary>
        /// <b>A document at the wrong schemaVersion is refused whole</b>, however good its entries are.
        /// A version is a contract, not a hint (non-negotiable #9).
        /// </summary>
        [Theory]
        [InlineData("2")]
        [InlineData("0")]
        [InlineData("\"1\"")]
        [InlineData("1.5")]
        [InlineData(null)]
        public void AWrongSchemaVersion_RefusesTheWholeDocument(string? schemaVersion)
        {
            string json = Doc(schemaVersion: schemaVersion,
                events: new[] { EventJson(id: "\"glob-first\""), EventJson(id: "\"glob-second\"") });

            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load("events_global.json", json, Tuning);

            Assert.Contains(result.Errors, e => e.Code == CatalogIssueCode.UnsupportedSchemaVersion);
            Assert.Empty(result.Catalog.Events);
        }

        [Fact]
        public void AWrongSchemaVersion_DoesNotCostTheOtherDocuments()
        {
            var sources = new[]
            {
                new CivicEventCatalogSource("events_eu.json",
                    Doc(schemaVersion: "2", events: EventJson(id: "\"eu-refused\""))),
                new CivicEventCatalogSource("events_global.json", Doc(events: EventJson(id: "\"glob-kept\"")))
            };

            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(sources, Tuning);

            Assert.Contains(result.Errors, e => e.Code == CatalogIssueCode.UnsupportedSchemaVersion);
            Assert.Equal(new[] { "glob-kept" }, Ids(result));
        }

        [Fact]
        public void ADocumentWithNoEventsArray_IsRefusedWithoutThrowing()
        {
            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(
                "events_global.json", "{ \"schemaVersion\": 1 }", Tuning);

            Assert.Contains(result.Errors, e => e.Code == CatalogIssueCode.EventsMissing);
            Assert.Empty(result.Catalog.Events);
        }

        [Fact]
        public void AnEmptyCatalog_IsValid()
        {
            AssertClean(CivicEventCatalogLoader.Load("events_eu.json", Doc(), Tuning));
        }

        /// <summary>
        /// <c>LoadFrom</c> reads the caller's reader and is otherwise the same loader — Core owns no
        /// stream and closes none.
        /// </summary>
        [Fact]
        public void LoadFrom_ReadsAReaderWithoutDisposingIt()
        {
            using var reader = new StringReader(Doc(events: EventJson(id: "\"glob-from-reader\"")));

            CivicEventCatalogLoadResult result =
                CivicEventCatalogLoader.LoadFrom("events_global.json", reader, Tuning);

            AssertClean(result);
            Assert.Equal(new[] { "glob-from-reader" }, Ids(result));

            // Still usable: a disposed StringReader would throw here.
            Assert.Null(reader.ReadLine());
        }

        // ================================================================== cross-document behaviour

        /// <summary>
        /// A duplicate id across two documents names the <b>second</b> occurrence and keeps the first,
        /// where "first" is decided by source name — not by the order a directory listing produced.
        /// Handed in both orders, and the surviving copy must be the same one both times.
        /// </summary>
        [Fact]
        public void DuplicateIdAcrossDocuments_NamesTheSecondAndKeepsTheFirstByName()
        {
            var eu = new CivicEventCatalogSource("events_eu.json",
                Doc(events: EventJson(id: "\"shared-id\"", proseField: "name", proseValue: "\"European telling\"")));
            var na = new CivicEventCatalogSource("events_na.json",
                Doc(events: EventJson(id: "\"shared-id\"", proseField: "name", proseValue: "\"North American telling\"")));

            foreach (CivicEventCatalogSource[] order in new[] { new[] { eu, na }, new[] { na, eu } })
            {
                CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(order, Tuning);

                CatalogIssue duplicate =
                    Assert.Single(result.Errors, e => e.Code == CatalogIssueCode.DuplicateEventId);
                Assert.Equal("events_na.json", duplicate.SourceName);
                Assert.Equal(1, result.RejectedEventCount);
                Assert.Equal("European telling", Assert.Single(result.Catalog.Events).Name);
            }
        }

        [Fact]
        public void DuplicateIdWithinOneDocument_RejectsTheSecondOnly()
        {
            string json = Doc(events: new[]
            {
                EventJson(id: "\"glob-twice\"", proseField: "name", proseValue: "\"The first claim\""),
                EventJson(id: "\"glob-twice\"", proseField: "name", proseValue: "\"The second claim\"")
            });

            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load("events_global.json", json, Tuning);

            Assert.Contains(result.Errors, e => e.Code == CatalogIssueCode.DuplicateEventId);
            Assert.Equal(1, result.RejectedEventCount);
            Assert.Equal("The first claim", Assert.Single(result.Catalog.Events).Name);
        }

        /// <summary>
        /// <b>The <c>featureIds</c> allow-list unions across documents.</b> An unlock spec in one file
        /// may legally name a feature declared in another, and the loader's two-pass order is the only
        /// reason that is true — a single-pass loader would make legality depend on read order. Handed
        /// in both orders, so a regression to one pass fails at least one direction.
        /// </summary>
        [Fact]
        public void FeatureIdAllowList_UnionsAcrossDocumentsInEitherOrder()
        {
            var declaring = new CivicEventCatalogSource("events_global.json", Doc(featureIds: "[\"Metro\"]"));
            var using_ = new CivicEventCatalogSource("events_eu.json",
                Doc(events: EventJson(id: "\"eu-metro-opens\"",
                    trigger: SpecJson(kind: "\"unlock\"", metricId: "\"Metro\"", threshold: null))));

            foreach (CivicEventCatalogSource[] order in
                     new[] { new[] { declaring, using_ }, new[] { using_, declaring } })
            {
                CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(order, Tuning);

                AssertClean(result);
                Assert.Equal(new[] { "eu-metro-opens" }, Ids(result));
                Assert.Equal(new[] { "Metro" }, result.Catalog.DeclaredFeatureIds);
            }
        }

        [Fact]
        public void FeatureIdAllowList_IsUnionedAndSortedWithoutDuplicates()
        {
            var a = new CivicEventCatalogSource("events_global.json", Doc(featureIds: "[\"Metro\",\"Airport\"]"));
            var b = new CivicEventCatalogSource("events_eu.json", Doc(featureIds: "[\"Metro\",\"Tram\"]"));

            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(new[] { a, b }, Tuning);

            AssertClean(result);
            Assert.Equal(new[] { "Airport", "Metro", "Tram" }, result.Catalog.DeclaredFeatureIds);
        }

        /// <summary>
        /// Two sources handed in under the same name are indistinguishable in every finding, so the
        /// loader says so — a warning, since the content itself may be perfectly valid.
        /// </summary>
        [Fact]
        public void TwoSourcesUnderOneName_WarnWithoutLosingEitherDocument()
        {
            var sources = new[]
            {
                new CivicEventCatalogSource("events_global.json", Doc(events: EventJson(id: "\"glob-one\""))),
                new CivicEventCatalogSource("events_global.json", Doc(events: EventJson(id: "\"glob-two\"")))
            };

            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(sources, Tuning);

            Assert.Empty(result.Errors);
            Assert.Contains(result.Warnings, w => w.Code == CatalogIssueCode.DuplicateSourceName);
            Assert.Equal(new[] { "glob-one", "glob-two" }, Ids(result));
        }

        [Fact]
        public void ANullSourceInTheListIsSkippedRatherThanThrowing()
        {
            var sources = new List<CivicEventCatalogSource>
            {
                null!,
                new CivicEventCatalogSource("events_global.json", Doc(events: EventJson(id: "\"glob-only\"")))
            };

            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(sources, Tuning);

            AssertClean(result);
            Assert.Equal(new[] { "glob-only" }, Ids(result));
        }

        // ================================================================== sorted, deterministic output

        /// <summary>
        /// Accepted events come back sorted by id ordinal regardless of which document or which
        /// position they were authored in. It is the order <c>EventPoolWeighting.Compare</c> breaks its
        /// last tie on, so an unsorted catalog would let a directory listing pick the story.
        /// </summary>
        [Fact]
        public void AcceptedEvents_AreSortedByIdOrdinalAcrossDocuments()
        {
            var z = new CivicEventCatalogSource("z-events.json", Doc(events: new[]
            {
                EventJson(id: "\"zulu-event\""), EventJson(id: "\"alpha-event\"")
            }));
            var a = new CivicEventCatalogSource("a-events.json", Doc(events: new[]
            {
                EventJson(id: "\"mike-event\""), EventJson(id: "\"bravo-event\"")
            }));

            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(new[] { z, a }, Tuning);

            AssertClean(result);
            Assert.Equal(new[] { "alpha-event", "bravo-event", "mike-event", "zulu-event" }, Ids(result));
        }

        /// <summary>The canonical determinism check: the same text twice, byte-identical output.</summary>
        [Fact]
        public void Load_ProducesIdenticalOutputTwice()
        {
            string json = MixedFixture();

            Assert.Equal(
                Hash(CivicEventCatalogLoader.Load("events_global.json", json, Tuning)),
                Hash(CivicEventCatalogLoader.Load("events_global.json", json, Tuning)));
        }

        /// <summary>Paired with the above, so a loader returning a constant could not pass both.</summary>
        [Fact]
        public void Load_ProducesDifferentOutputForDifferentContent()
        {
            Assert.NotEqual(
                Hash(CivicEventCatalogLoader.Load("events_global.json", MixedFixture(), Tuning)),
                Hash(CivicEventCatalogLoader.Load("events_global.json",
                    MixedFixture().Replace("alpha-event", "omega-event"), Tuning)));
        }

        [Fact]
        public void Load_IsIndependentOfTheOrderSourcesAreHandedIn()
        {
            var eu = new CivicEventCatalogSource("events_eu.json",
                Doc(events: EventJson(id: "\"eu-one\"", region: "\"eu\"")));
            var na = new CivicEventCatalogSource("events_na.json",
                Doc(events: EventJson(id: "\"na-one\"", region: "\"na\"")));
            var global = new CivicEventCatalogSource("events_global.json",
                Doc(featureIds: "[\"Metro\"]", events: EventJson(id: "\"glob-one\"")));

            Assert.Equal(
                Hash(CivicEventCatalogLoader.Load(new[] { eu, na, global }, Tuning)),
                Hash(CivicEventCatalogLoader.Load(new[] { global, na, eu }, Tuning)));
        }

        /// <summary>
        /// The warning list is part of the loader's output, so it must not vary with the order the
        /// author happened to type the properties in — that is the classic dictionary-iteration desync,
        /// stable within a run and different across runs.
        /// </summary>
        [Fact]
        public void WarningOrder_DoesNotVaryWithJsonPropertyOrder()
        {
            string first = Doc(events: EventJson(extra: "\"zebra\":1,\"aardvark\":2,\"mood\":\"anxious\""));
            string second = Doc(events: EventJson(extra: "\"mood\":\"anxious\",\"aardvark\":2,\"zebra\":1"));

            CivicEventCatalogLoadResult a = CivicEventCatalogLoader.Load("events_global.json", first, Tuning);
            CivicEventCatalogLoadResult b = CivicEventCatalogLoader.Load("events_global.json", second, Tuning);

            Assert.Equal(3, a.Warnings.Count);
            Assert.Equal(Paths(a.Warnings), Paths(b.Warnings));
            Assert.Equal(new[] { "events[0].aardvark", "events[0].mood", "events[0].zebra" }, Paths(a.Warnings));
        }

        [Fact]
        public void UnderscoreCommentKeysAreIgnoredRatherThanWarnedAbout()
        {
            CivicEventCatalogLoadResult result =
                LoadOne(EventJson(extra: "\"_comment\":\"provisional until the census gate is walked\""));

            AssertClean(result);
            Assert.Empty(result.Warnings);
        }

        // ================================================================== assertions

        private static void AssertClean(CivicEventCatalogLoadResult result)
        {
            Assert.True(result.IsClean, "expected a clean load:" + Environment.NewLine + Describe(result));
            Assert.Empty(result.Warnings);
        }

        /// <summary>
        /// The entry — and only the entry — was refused, for the stated reason. Asserting the code
        /// rather than the message is what lets the loader reword its findings freely.
        /// </summary>
        private static void AssertRejected(CivicEventCatalogLoadResult result, CatalogIssueCode expected)
        {
            Assert.False(result.IsClean, "expected a rejection but the entry loaded cleanly");
            Assert.Contains(result.Errors, e => e.Code == expected);
            Assert.Contains(result.Errors, e => e.Code == expected && e.Severity == CatalogIssueSeverity.Error);
            Assert.Equal(1, result.RejectedEventCount);
            Assert.Empty(result.Catalog.Events);
        }

        private static string[] Ids(CivicEventCatalogLoadResult result)
        {
            var ids = new string[result.Catalog.Events.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = result.Catalog.Events[i].Id;
            return ids;
        }

        private static string[] Paths(IReadOnlyList<CatalogIssue> issues)
        {
            var paths = new string[issues.Count];
            for (int i = 0; i < paths.Length; i++) paths[i] = issues[i].Path;
            return paths;
        }

        private static string Describe(CivicEventCatalogLoadResult result)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < result.Errors.Count; i++) sb.Append(result.Errors[i]).Append('\n');
            for (int i = 0; i < result.Warnings.Count; i++) sb.Append(result.Warnings[i]).Append('\n');
            return sb.ToString();
        }

        /// <summary>
        /// Hashes the serialized result rather than comparing field by field: hashing catches the field
        /// a hand-written assertion forgot, which is precisely where a desync hides.
        /// </summary>
        private static string Hash(CivicEventCatalogLoadResult result)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < result.Catalog.Events.Count; i++)
            {
                CivicEvent e = result.Catalog.Events[i];
                sb.Append(e.Id).Append('|')
                  .Append(e.Severity).Append('|')
                  .Append(e.Region).Append('|')
                  .Append(Describe(e.Trigger)).Append('|')
                  .Append(Describe(e.Check.Spec)).Append(':').Append(e.Check.RelativeToBaseline).Append('|')
                  .Append(string.Join(",", e.ActiveEffects)).Append('|')
                  .Append(string.Join(",", e.SuccessEffects)).Append('|')
                  .Append(string.Join(",", e.FailureEffects)).Append('|')
                  .Append(string.Join(",", e.DistrictAffinity)).Append('|')
                  .Append(string.Join(",", e.Tags)).Append('|')
                  .Append(e.Name).Append('|').Append(e.Description).Append('|')
                  .Append(e.IgnoreText).Append('|').Append(e.GoalText).Append('|')
                  .Append(e.PowerOverrideText).Append('|').Append(e.SuccessText).Append('|')
                  .Append(e.FailText).Append('|');

                for (int k = 0; k < Issues.All.Count; k++)
                {
                    Issue issue = Issues.All[k];
                    sb.Append(Number(e.ActivePressure[issue])).Append(',')
                      .Append(Number(e.SuccessPressure[issue])).Append(',')
                      .Append(Number(e.FailurePressure[issue])).Append(';');
                }

                sb.Append('\n');
            }

            sb.Append("--features--\n").Append(string.Join(",", result.Catalog.DeclaredFeatureIds)).Append('\n');
            sb.Append("--rejected--").Append(result.RejectedEventCount).Append('\n');
            sb.Append("--issues--\n").Append(Describe(result));

            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
            }
        }

        private static string Describe(TriggerSpec spec) =>
            spec.Kind + ":" + spec.MetricId + ":" + spec.Comparison + ":" +
            Number(spec.Threshold) + ":" + spec.WindowMonths + ":" + spec.Scope;

        private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        // ================================================================== document fixtures

        /// <summary>
        /// A document mixing accepted entries, a rejected one and a warned one — enough surface that a
        /// hash over it would notice almost any change of behaviour.
        /// </summary>
        private static string MixedFixture() => Doc(featureIds: "[\"Metro\"]", events: new[]
        {
            EventJson(id: "\"alpha-event\"", region: "\"eu\"", severity: "4",
                      activeEffects: "[\"district-wellbeing\",\"district-wellbeing\"]",
                      tags: "[\"housing\",\"budget\"]"),
            EventJson(id: "\"beta-event\"", trigger: DeltaSpec(6),
                      activePressure: "{\"services\":-0.4,\"costOfLiving\":0.25}",
                      districtAffinity: "[\"industrial\",\"affluent\"]"),
            EventJson(id: "\"broken-event\"", trigger: SpecJson(metricId: "\"notAMetric\"")),
            EventJson(id: "\"gamma-event\"", trigger: SpecJson(kind: "\"unlock\"", metricId: "\"Metro\"",
                                                               threshold: null))
        });

        private static CivicEventCatalogLoadResult LoadOne(string eventJson) =>
            CivicEventCatalogLoader.Load("events_global.json", Doc(events: eventJson), Tuning);

        private static string Doc(string? schemaVersion = "1", string? featureIds = null, string? events = null) =>
            DocCore(schemaVersion, featureIds, events == null ? new string[0] : new[] { events });

        private static string Doc(string[] events, string? schemaVersion = "1", string? featureIds = null) =>
            DocCore(schemaVersion, featureIds, events);

        private static string DocCore(string? schemaVersion, string? featureIds, string[] events)
        {
            var parts = new List<string>();
            Add(parts, "schemaVersion", schemaVersion);
            Add(parts, "featureIds", featureIds);
            parts.Add("\"events\":[" + string.Join(",", events) + "]");
            return "{" + string.Join(",", parts) + "}";
        }

        /// <summary>
        /// One <c>events[]</c> entry. Every parameter is a raw JSON fragment, and null omits the
        /// property — which is what lets one builder cover "absent", "wrong type" and "out of range".
        /// </summary>
        /// <remarks>
        /// The check defaults to a known-good spec rather than mirroring <paramref name="trigger"/>, so
        /// a fixture that breaks the trigger produces exactly one finding and the test cannot pass on
        /// the wrong one.
        /// </remarks>
        private static string EventJson(
            string? id = "\"synthetic-event\"",
            string? severity = "2",
            string? region = "\"global\"",
            string? trigger = null,
            bool omitTrigger = false,
            string? check = null,
            bool omitCheck = false,
            string? activeEffects = null,
            string? activePressure = null,
            string? districtAffinity = null,
            string? tags = null,
            string? omitProse = null,
            string? proseField = null,
            string? proseValue = null,
            string? extra = null)
        {
            var parts = new List<string>();
            Add(parts, "id", id);
            Add(parts, "severity", severity);
            Add(parts, "region", region);
            if (!omitTrigger) Add(parts, "trigger", trigger ?? ValidSpec);
            if (!omitCheck) Add(parts, "check", check ?? "{\"spec\":" + ValidSpec + "}");
            Add(parts, "activeEffects", activeEffects);
            Add(parts, "activePressure", activePressure);
            Add(parts, "districtAffinity", districtAffinity);
            Add(parts, "tags", tags);

            for (int i = 0; i < ProseFields.Length; i++)
            {
                string field = ProseFields[i];
                if (string.CompareOrdinal(field, omitProse) == 0) continue;

                Add(parts, field, string.CompareOrdinal(field, proseField) == 0
                    ? proseValue
                    : "\"Prose for " + field + ".\"");
            }

            if (extra != null) parts.Add(extra);
            return "{" + string.Join(",", parts) + "}";
        }

        /// <summary>
        /// A trigger spec. Defaults are the always-valid city metric spec, so a caller overrides only
        /// the one property under test.
        /// </summary>
        private static string SpecJson(
            string? kind = "\"metric\"",
            string? metricId = "\"happiness\"",
            string? comparison = "\"lt\"",
            string? threshold = "0.4",
            string? windowMonths = null,
            string? scope = null)
        {
            var parts = new List<string>();
            Add(parts, "kind", kind);
            Add(parts, "metricId", metricId);
            Add(parts, "comparison", comparison);
            Add(parts, "threshold", threshold);
            Add(parts, "windowMonths", windowMonths);
            Add(parts, "scope", scope);
            return "{" + string.Join(",", parts) + "}";
        }

        private static string DeltaSpec(int windowMonths) => SpecJson(
            kind: "\"delta\"", comparison: "\"lte\"", threshold: "-0.05",
            windowMonths: windowMonths.ToString(CultureInfo.InvariantCulture));

        private static void Add(List<string> parts, string key, string? value)
        {
            if (value != null) parts.Add("\"" + key + "\":" + value);
        }
    }
}
