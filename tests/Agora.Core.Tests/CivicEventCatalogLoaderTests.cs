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

        /// <summary>
        /// A window a fixture uses when the window is not what is under test. Clamped, so a fixture
        /// that only ever meant "some legal window" cannot start failing for a retention change it has
        /// no opinion about — that is the bound's own test's job.
        /// </summary>
        private static int SomeLegalWindow(int preferred) =>
            preferred < MaxDeltaWindow ? preferred : MaxDeltaWindow;

        /// <summary><see cref="SomeLegalWindow"/> as a raw JSON fragment.</summary>
        private static string Window(int preferred) =>
            SomeLegalWindow(preferred).ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// How long a story actually lives, and therefore the widest window a <c>check</c> may read
        /// back over. <b><c>stories.cycleMonths</c> is the cadence, not the story's life</b>, and the
        /// two differ by one: <c>StoryAssembler.NewStory</c> drafts at M and resolves at M+1. Derived
        /// the way <see cref="MaxDeltaWindow"/> is derived from retention, so moving the cadence
        /// re-checks the rule instead of rotting the fixtures.
        /// </summary>
        private static int StoryLifeMonths => Tuning.Stories.CycleMonths - 1 < 1
            ? 1
            : Tuning.Stories.CycleMonths - 1;

        /// <summary>A check window a fixture uses when the window is not what is under test.</summary>
        private static string CheckWindow(int preferred) =>
            (preferred < StoryLifeMonths ? preferred : StoryLifeMonths).ToString(CultureInfo.InvariantCulture);

        /// <summary>The highest severity the catalog admits, read from tuning rather than typed as 5.</summary>
        private static int SeverityMax => Tuning.Catalog.SeverityMax;

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
                kind: "\"delta\"", threshold: null, windowMonths: Window(3)))),
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
                    kind: "\"delta\"", metricId: quoted, windowMonths: Window(3)))));
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
        /// <b>The one error in the enum that rejects nothing.</b> A broken allow-list is document-
        /// scoped: an event that names a feature fails separately and more precisely with
        /// <c>UnlockIdNotDeclared</c>, and one that names none is unharmed.
        /// </summary>
        /// <remarks>
        /// Both halves are asserted, and the second is the reason the first is acceptable.
        /// <c>IsClean</c> going false is what fails <c>ShippedCivicEventCatalogTests</c>, so a
        /// malformed allow-list still breaks the build — it simply does so without discarding events
        /// that were never in question. Pinning only "nothing was rejected" would document a silent
        /// failure instead of a scoped one.
        /// </remarks>
        [Fact]
        public void MalformedFeatureIds_ReportsWithoutRejectingEventsThatNameNoFeature()
        {
            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load(
                "events_global.json", Doc(featureIds: "[\"\"]", events: EventJson()), Tuning);

            Assert.Contains(result.Errors, e => e.Code == CatalogIssueCode.MalformedFeatureIds);
            Assert.Equal(0, result.RejectedEventCount);
            Assert.Single(result.Catalog.Events);

            // The half that makes the half above acceptable: the build still goes red.
            Assert.False(result.IsClean,
                "a malformed featureIds allow-list must fail the shipped-catalog gate even though it " +
                "discards no event");
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
            // A check window, not a trigger window: the ceiling here is the story's life, not the
            // snapshot retention, and the two are wildly different numbers.
            string spec = string.CompareOrdinal(kind, "\"delta\"") == 0
                ? SpecJson(kind: "\"delta\"", comparison: "\"lte\"", threshold: "-0.05",
                           windowMonths: CheckWindow(3))
                : ValidSpec;

            CivicEventCatalogLoadResult result = LoadOne(EventJson(
                check: "{\"spec\":" + spec + ",\"relativeToBaseline\":true}"));

            AssertClean(result);
        }

        // ================================================================== 116 BaselineCheckAtDistrictScope

        /// <summary>
        /// <b>An error, not a warning, because it is provable rather than a judgement.</b>
        /// <c>StoryAssembler.Baseline</c> returns <c>null</c> for every non-city scope — nothing on
        /// <c>StorySlot</c> records which district the story landed on — so a relative district check
        /// resolves <c>Unmeasurable</c> on every save, in every month, forever. It scores in neither
        /// half of the 2-of-3 and moves the power balance by zero, which is the worst kind of broken:
        /// an event that reads like a working goal and contributes nothing.
        /// </summary>
        [Theory]
        [InlineData("\"anyDistrict\"")]
        [InlineData("\"allDistricts\"")]
        public void BaselineCheckAtDistrictScope_IsRejectedAtEitherDistrictScope(string scope)
        {
            string spec = SpecJson(metricId: "\"" + MetricRegistry.CrimeRate + "\"", scope: scope);

            AssertRejected(
                LoadOne(EventJson(check: "{\"spec\":" + spec + ",\"relativeToBaseline\":true}")),
                CatalogIssueCode.BaselineCheckAtDistrictScope);
        }

        /// <summary>
        /// The positive pair. A relative check at city scope is the whole point of the two-month cycle
        /// — drafting at M and resolving at M+1 is a genuinely later measurement — so the rule must
        /// bite on scope alone and not on the flag.
        /// </summary>
        [Fact]
        public void BaselineCheckAtCityScope_StaysClean()
        {
            AssertClean(LoadOne(EventJson(
                check: "{\"spec\":" + ValidSpec + ",\"relativeToBaseline\":true}")));
        }

        /// <summary>
        /// The other half of the pair: a district-scoped check is perfectly legal as long as it is not
        /// asking to be measured against a baseline nobody recorded.
        /// </summary>
        [Theory]
        [InlineData("\"anyDistrict\"")]
        [InlineData("\"allDistricts\"")]
        public void ADistrictCheckWithoutABaseline_StaysClean(string scope)
        {
            // A different metric from the trigger's, so this cannot also trip rule 117.
            string spec = SpecJson(metricId: "\"" + MetricRegistry.CrimeRate + "\"", scope: scope);

            AssertClean(LoadOne(EventJson(check: "{\"spec\":" + spec + "}")));
        }

        [Fact]
        public void BaselineCheckAtDistrictScope_IsNotSuppressedByTheNonMetricWarning()
        {
            // relativeToBaseline on a delta at district scope: reading-shaped, so rule 115 stays quiet
            // and only the provable impossibility is reported.
            string spec = SpecJson(kind: "\"delta\"", metricId: "\"" + MetricRegistry.CrimeRate + "\"",
                                   comparison: "\"lte\"", threshold: "-0.05",
                                   windowMonths: CheckWindow(3), scope: "\"allDistricts\"");

            CivicEventCatalogLoadResult result =
                LoadOne(EventJson(check: "{\"spec\":" + spec + ",\"relativeToBaseline\":true}"));

            AssertRejected(result, CatalogIssueCode.BaselineCheckAtDistrictScope);
            Assert.DoesNotContain(result.Warnings, w => w.Code == CatalogIssueCode.BaselineOnNonMetricCheck);
        }

        // ================================================================== 117 DistrictCheckNotBoundToTrigger

        /// <summary>
        /// "Some district is bad" paired with "some district is fine on the same metric" is answered by
        /// the healthiest block in the city, usually on the month the story opens — <c>AnyDistrict</c>
        /// returns <c>Met</c> on the first district that clears the bar, and no district id survives
        /// onto <c>StorySlot</c> to bind the check to the one the story is about.
        /// </summary>
        /// <remarks>
        /// A warning rather than an error, so the event must still load — but the shipped-catalog gate
        /// holds the catalogs to zero warnings, which is what stops one shipping unargued.
        /// </remarks>
        [Theory]
        [InlineData("\"anyDistrict\"")]
        [InlineData("\"allDistricts\"")]
        public void DistrictCheckNotBoundToTrigger_WarnsButKeepsTheEvent(string triggerScope)
        {
            string metric = "\"" + MetricRegistry.CrimeRate + "\"";

            // The check carries the trigger's own threshold, so this fixture isolates the scope defect
            // and cannot also trip CheckThresholdLeavesTrapBand — that is a different rule with a
            // different repair, and a fixture carrying both defects would not tell them apart.
            CivicEventCatalogLoadResult result = LoadOne(EventJson(
                trigger: SpecJson(metricId: metric, comparison: "\"gte\"", threshold: "0.6",
                                  scope: triggerScope),
                check: "{\"spec\":" + SpecJson(metricId: metric, comparison: "\"lt\"", threshold: "0.6",
                                                scope: "\"anyDistrict\"") + "}"));

            Assert.Empty(result.Errors);
            Assert.Equal(0, result.RejectedEventCount);
            Assert.Contains(result.Warnings, w => w.Code == CatalogIssueCode.DistrictCheckNotBoundToTrigger);
            Assert.DoesNotContain(result.Warnings, w => w.Code == CatalogIssueCode.CheckThresholdLeavesTrapBand);
            Assert.Single(result.Catalog.Events);
        }

        /// <summary>
        /// <b>The first positive pair, and the repair the warning names.</b> <c>allDistricts</c> is a
        /// real and rising ask — every district must clear the bar — so it must stay clean on the
        /// trigger's own metric.
        /// </summary>
        /// <remarks>
        /// The check takes the trigger's <i>own</i> threshold, and an earlier version of this fixture
        /// did not — it asked for 0.4 against a trigger firing at 0.6, which is exactly the trap band
        /// rule 119 was later written to find. A positive fixture that quietly carries the defect a
        /// neighbouring rule exists to catch is worse than no fixture: it reads as a blessing.
        /// </remarks>
        [Fact]
        public void AnAllDistrictsCheckOnTheTriggersOwnMetric_StaysClean()
        {
            string metric = "\"" + MetricRegistry.CrimeRate + "\"";

            AssertClean(LoadOne(EventJson(
                trigger: SpecJson(metricId: metric, comparison: "\"gte\"", threshold: "0.6",
                                  scope: "\"anyDistrict\""),
                check: "{\"spec\":" + SpecJson(metricId: metric, comparison: "\"lt\"", threshold: "0.6",
                                                scope: "\"allDistricts\"") + "}")));
        }

        /// <summary>
        /// The second positive pair, and the reason the rule is restricted to a matching
        /// <c>MetricId</c>: a district check on a <i>different</i> metric is a genuinely different
        /// question and may well be intended. Widening the rule to every district check would train
        /// authors to ignore it, which is the failure mode a noisy check has.
        /// </summary>
        [Fact]
        public void ADistrictCheckOnADifferentMetric_StaysClean()
        {
            AssertClean(LoadOne(EventJson(
                trigger: SpecJson(metricId: "\"" + MetricRegistry.CrimeRate + "\"",
                                  comparison: "\"gte\"", threshold: "0.6", scope: "\"anyDistrict\""),
                check: "{\"spec\":" + SpecJson(metricId: "\"" + MetricRegistry.Happiness + "\"",
                                                scope: "\"anyDistrict\"") + "}")));
        }

        /// <summary>
        /// <b>Two specs with no metric id at all are not "the same metric".</b> A <c>manual</c> trigger
        /// and a <c>manual</c> check, both at <c>anyDistrict</c> scope, carry an empty
        /// <c>MetricId</c> on both sides and sail through the equality test — so without the
        /// emptiness guard the loader emits a warning naming <c>''</c>, against an event whose specs
        /// read no metric at all.
        /// </summary>
        /// <remarks>
        /// A finding that names nothing is worse than no finding: it costs an author a search through
        /// a document for a metric that was never there, and the shipped gate holds catalogs to zero
        /// warnings, so it would also block a merge for a defect that does not exist.
        /// </remarks>
        [Fact]
        public void TwoSpecsWithNoMetricId_DoNotCountAsReadingTheSameMetric()
        {
            string manual = SpecJson(kind: "\"manual\"", metricId: null, comparison: null,
                                     threshold: null, scope: "\"anyDistrict\"");

            CivicEventCatalogLoadResult result = LoadOne(EventJson(
                trigger: manual, check: "{\"spec\":" + manual + "}"));

            Assert.DoesNotContain(result.Warnings,
                w => w.Code == CatalogIssueCode.DistrictCheckNotBoundToTrigger);
            AssertClean(result);
        }

        /// <summary>A city-scoped trigger cannot be unbound from anything; the rule must stay quiet.</summary>
        [Fact]
        public void ACityScopedTrigger_DoesNotTripTheBindingRule()
        {
            AssertClean(LoadOne(EventJson(
                trigger: SpecJson(metricId: "\"" + MetricRegistry.Happiness + "\""),
                check: "{\"spec\":" + SpecJson(metricId: "\"" + MetricRegistry.Happiness + "\"",
                                                scope: "\"anyDistrict\"") + "}")));
        }

        // ================================================================== 118 PressureSignFlip

        /// <summary>
        /// <b>Pressures are salience, not credit.</b> The only consumer of an event's
        /// <c>IssuePosition</c> is <c>AffinityEngine.EventTerm</c>, which dot-products it against each
        /// party's platform — so a mirror-negated success pressure does not release the issue, it moves
        /// voters to the <i>opposite pole</i> and rewards the party that opposed doing anything. All
        /// three wave-3 content lanes independently invented the mirroring convention, which is why
        /// this is machine-checked rather than only written down.
        /// </summary>
        [Theory]
        [InlineData("successPressure")]
        [InlineData("failurePressure")]
        public void PressureSignFlip_WarnsWhenAnOutcomeReversesTheActivePressure(string key)
        {
            CivicEventCatalogLoadResult result = LoadOne(EventJson(
                activePressure: "{\"services\":-0.4}",
                successPressure: string.CompareOrdinal(key, "successPressure") == 0 ? "{\"services\":0.4}" : null,
                failurePressure: string.CompareOrdinal(key, "failurePressure") == 0 ? "{\"services\":0.4}" : null));

            Assert.Empty(result.Errors);
            Assert.Equal(0, result.RejectedEventCount);
            CatalogIssue flip = Assert.Single(result.Warnings, w => w.Code == CatalogIssueCode.PressureSignFlip);
            Assert.Equal("events[0]." + key + ".services", flip.Path);
            Assert.Single(result.Catalog.Events);
        }

        /// <summary>
        /// The first positive pair: same sign, any magnitude. Louder on failure and quieter on success
        /// is the expected shape, and the loader deliberately polices direction only.
        /// </summary>
        [Fact]
        public void SameSignOutcomePressures_StayClean()
        {
            AssertClean(LoadOne(EventJson(
                activePressure: "{\"services\":-0.4,\"costOfLiving\":0.3}",
                successPressure: "{\"services\":-0.1,\"costOfLiving\":0.05}",
                failurePressure: "{\"services\":-0.9,\"costOfLiving\":0.6}")));
        }

        /// <summary>
        /// The second positive pair: zero on either side is not a flip. Dropping an issue at
        /// resolution is a legitimate way to say "this stopped mattering", stated both by omitting the
        /// component and by writing it as an explicit zero.
        /// </summary>
        [Fact]
        public void ZeroedOutcomePressures_StayClean()
        {
            AssertClean(LoadOne(EventJson(
                activePressure: "{\"services\":-0.4,\"environment\":0.5}",
                successPressure: "{\"services\":0.0}",       // explicitly released
                failurePressure: "{\"environment\":0.2}")));  // services simply unstated
        }

        /// <summary>
        /// The flip is reported per issue, in <c>Issues.All</c> order — the fold order that makes the
        /// output bit-stable — and not once per event.
        /// </summary>
        [Fact]
        public void PressureSignFlip_IsReportedPerIssueInIssuesAllOrder()
        {
            CivicEventCatalogLoadResult result = LoadOne(EventJson(
                activePressure: "{\"services\":-0.4,\"transit\":0.3,\"environment\":-0.2}",
                successPressure: "{\"services\":0.4,\"transit\":-0.3,\"environment\":0.2}"));

            var flipped = new List<string>();
            for (int i = 0; i < result.Warnings.Count; i++)
            {
                if (result.Warnings[i].Code == CatalogIssueCode.PressureSignFlip)
                {
                    flipped.Add(result.Warnings[i].Path);
                }
            }

            var expected = new List<string>();
            for (int i = 0; i < Issues.All.Count; i++)
            {
                string key = Issues.ToKey(Issues.All[i]);
                if (string.CompareOrdinal(key, "services") == 0 ||
                    string.CompareOrdinal(key, "transit") == 0 ||
                    string.CompareOrdinal(key, "environment") == 0)
                {
                    expected.Add("events[0].successPressure." + key);
                }
            }

            Assert.Equal(expected, flipped);
        }

        // ================================================================== 119 CheckThresholdLeavesTrapBand

        /// <summary>
        /// <b>The band between the two thresholds is districts the player was never told about.</b> An
        /// <c>allDistricts</c> check returns <c>NotMet</c> the instant one measured district fails, so
        /// a trigger firing at <c>&gt;= 0.6</c> paired with a check demanding <c>&lt; 0.35</c> loses
        /// the story over a district sitting at 0.37 — one that contributed nothing to the trigger and
        /// appears nowhere in the prose.
        /// </summary>
        [Theory]
        [InlineData("\"anyDistrict\"")]
        [InlineData("\"allDistricts\"")]
        public void CheckThresholdLeavesTrapBand_WhenTheCheckIsTighterThanTheTrigger(string checkScope)
        {
            string metric = "\"" + MetricRegistry.CrimeRate + "\"";

            CivicEventCatalogLoadResult result = LoadOne(EventJson(
                trigger: SpecJson(metricId: metric, comparison: "\"gte\"", threshold: "0.6",
                                  scope: "\"anyDistrict\""),
                check: "{\"spec\":" + SpecJson(metricId: metric, comparison: "\"lt\"", threshold: "0.35",
                                                scope: checkScope) + "}"));

            Assert.Empty(result.Errors);
            Assert.Equal(0, result.RejectedEventCount);
            Assert.Contains(result.Warnings, w => w.Code == CatalogIssueCode.CheckThresholdLeavesTrapBand);
            Assert.Single(result.Catalog.Events);
        }

        /// <summary>
        /// Mirrored, for a metric where low is the bad direction. The rule must not be written only
        /// for the "too high" shape — half a rule reads as a whole one.
        /// </summary>
        [Fact]
        public void CheckThresholdLeavesTrapBand_ForALowIsBadTrigger()
        {
            string metric = "\"" + MetricRegistry.Happiness + "\"";

            CivicEventCatalogLoadResult result = LoadOne(EventJson(
                trigger: SpecJson(metricId: metric, comparison: "\"lte\"", threshold: "0.3",
                                  scope: "\"anyDistrict\""),
                check: "{\"spec\":" + SpecJson(metricId: metric, comparison: "\"gte\"", threshold: "0.5",
                                                scope: "\"allDistricts\"") + "}"));

            Assert.Contains(result.Warnings, w => w.Code == CatalogIssueCode.CheckThresholdLeavesTrapBand);
        }

        /// <summary>
        /// The positive pair, both ways round: an exact complement leaves no band whatever the
        /// strictness of the two comparisons. The rule is about the band, not the boundary, so
        /// <c>&gt;= T</c> against <c>&lt; T</c> and <c>&gt; T</c> against <c>&lt;= T</c> are both silent.
        /// </summary>
        [Theory]
        [InlineData("\"gte\"", "\"lt\"")]
        [InlineData("\"gt\"", "\"lte\"")]
        public void AnExactComplement_LeavesNoTrapBand(string triggerComparison, string checkComparison)
        {
            string metric = "\"" + MetricRegistry.CrimeRate + "\"";

            AssertClean(LoadOne(EventJson(
                trigger: SpecJson(metricId: metric, comparison: triggerComparison, threshold: "0.6",
                                  scope: "\"anyDistrict\""),
                check: "{\"spec\":" + SpecJson(metricId: metric, comparison: checkComparison,
                                                threshold: "0.6", scope: "\"allDistricts\"") + "}")));
        }

        /// <summary>
        /// A check <i>looser</i> than its trigger traps nobody — every district that can fail the check
        /// already fired the trigger — so the rule must not fire on a gap in the harmless direction.
        /// </summary>
        [Fact]
        public void ACheckLooserThanItsTrigger_LeavesNoTrapBand()
        {
            string metric = "\"" + MetricRegistry.CrimeRate + "\"";

            AssertClean(LoadOne(EventJson(
                trigger: SpecJson(metricId: metric, comparison: "\"gte\"", threshold: "0.6",
                                  scope: "\"anyDistrict\""),
                check: "{\"spec\":" + SpecJson(metricId: metric, comparison: "\"lt\"", threshold: "0.8",
                                                scope: "\"allDistricts\"") + "}")));
        }

        /// <summary>
        /// Two specs pointing the same way are not a "fix it" pair at all, and the rule has no opinion
        /// about them.
        /// </summary>
        [Fact]
        public void ACheckPointingTheSameWayAsItsTrigger_IsNotATrapBand()
        {
            string metric = "\"" + MetricRegistry.CrimeRate + "\"";

            AssertClean(LoadOne(EventJson(
                trigger: SpecJson(metricId: metric, comparison: "\"gte\"", threshold: "0.6",
                                  scope: "\"anyDistrict\""),
                check: "{\"spec\":" + SpecJson(metricId: metric, comparison: "\"gte\"", threshold: "0.3",
                                                scope: "\"allDistricts\"") + "}")));
        }

        /// <summary>
        /// A <c>delta</c> pair is left alone: the two windows would have to be normalised before the
        /// thresholds could be compared at all, and comparing them unnormalised would produce
        /// confident nonsense. The gap here is large and deliberate, and must stay silent.
        /// </summary>
        [Fact]
        public void ADeltaPair_IsNotComparedForATrapBand()
        {
            string metric = "\"" + MetricRegistry.CrimeRate + "\"";

            AssertClean(LoadOne(EventJson(
                trigger: SpecJson(kind: "\"delta\"", metricId: metric, comparison: "\"gte\"",
                                  threshold: "0.6", windowMonths: Window(6), scope: "\"anyDistrict\""),
                check: "{\"spec\":" + SpecJson(kind: "\"delta\"", metricId: metric, comparison: "\"lte\"",
                                                threshold: "-0.35", windowMonths: CheckWindow(1),
                                                scope: "\"allDistricts\"") + "}")));
        }

        /// <summary>
        /// A relative check has no fixed band — the bar moves with the city — and the rule bows out.
        /// The entry is still refused, by rule 116, because a district baseline is never captured; the
        /// point here is that it is refused <b>once</b>, for the reason that is provable, without a
        /// second finding about a band that does not exist.
        /// </summary>
        [Fact]
        public void ARelativeCheck_IsNotJudgedForATrapBand()
        {
            string metric = "\"" + MetricRegistry.CrimeRate + "\"";

            CivicEventCatalogLoadResult result = LoadOne(EventJson(
                trigger: SpecJson(metricId: metric, comparison: "\"gte\"", threshold: "0.6",
                                  scope: "\"anyDistrict\""),
                check: "{\"spec\":" + SpecJson(metricId: metric, comparison: "\"lt\"", threshold: "0.35",
                                                scope: "\"allDistricts\"") + ",\"relativeToBaseline\":true}"));

            AssertRejected(result, CatalogIssueCode.BaselineCheckAtDistrictScope);
            Assert.DoesNotContain(result.Warnings, w => w.Code == CatalogIssueCode.CheckThresholdLeavesTrapBand);
        }

        /// <summary>
        /// <b>The twin of <see cref="TwoSpecsWithNoMetricId_DoNotCountAsReadingTheSameMetric"/>, and it
        /// needs its own fixture.</b> That one uses <c>manual</c> specs, which bail out of this rule
        /// several lines earlier; this one is <c>metric</c>-kind and district-scoped on both sides with
        /// no <c>metricId</c> at all, so it reaches the emptiness guard rule 119 keeps for itself. Two
        /// empty ids compare equal, the thresholds diverge, and without the guard the loader reports a
        /// trap band on a metric named <c>''</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The entry is rejected regardless — <c>UnknownMetricId</c> — but that is not what suppresses
        /// the warning and must not be mistaken for it: the warn hooks run unconditionally after
        /// <c>ReadSpec</c>, so a rejected entry still emits findings. The guard is the only thing
        /// standing between an author and a warning about nothing.
        /// </para>
        /// <para>
        /// A finding that names nothing is worse than no finding — the same argument as the rule-117
        /// case, and the reason both guards are worth a fixture rather than a comment.
        /// </para>
        /// </remarks>
        [Fact]
        public void TwoMetricSpecsWithNoMetricId_DoNotOpenATrapBand()
        {
            CivicEventCatalogLoadResult result = LoadOne(EventJson(
                trigger: SpecJson(metricId: null, comparison: "\"gte\"", threshold: "0.6",
                                  scope: "\"anyDistrict\""),
                check: "{\"spec\":" + SpecJson(metricId: null, comparison: "\"lt\"", threshold: "0.35",
                                                scope: "\"allDistricts\"") + "}"));

            AssertRejected(result, CatalogIssueCode.UnknownMetricId);
            Assert.DoesNotContain(result.Warnings, w => w.Code == CatalogIssueCode.CheckThresholdLeavesTrapBand);
        }

        // ================================================================== 120 CheckWindowOutrunsStoryLife

        /// <summary>
        /// <b><c>stories.cycleMonths</c> is the cadence, not the story's life.</b>
        /// <c>StoryAssembler.NewStory</c> drafts at M and resolves at M+1, so a check reading back
        /// further than <c>cycleMonths - 1</c> scores the player on months that predate their
        /// decision — and the further back it reads, the smaller their share of the verdict.
        /// </summary>
        [Fact]
        public void CheckWindowOutrunsStoryLife_WhenTheCheckReadsBackFurtherThanTheStoryLived()
        {
            int beyond = StoryLifeMonths + 1;
            Assert.True(beyond <= MaxDeltaWindow,
                "the fixture needs a window that outruns the story's life while staying inside the " +
                "retention bound, so that only rule 120 can be what fires");

            CivicEventCatalogLoadResult result = LoadOne(EventJson(
                check: "{\"spec\":" + SpecJson(kind: "\"delta\"", comparison: "\"lte\"",
                                                threshold: "-0.05",
                                                windowMonths: beyond.ToString(CultureInfo.InvariantCulture)) + "}"));

            Assert.Empty(result.Errors);
            Assert.Equal(0, result.RejectedEventCount);
            Assert.Contains(result.Warnings, w => w.Code == CatalogIssueCode.CheckWindowOutrunsStoryLife);
            Assert.DoesNotContain(result.Warnings, w => w.Code == CatalogIssueCode.WindowMonthsOutOfRange);
            Assert.Single(result.Catalog.Events);
        }

        /// <summary>The positive pair: a window of exactly the story's life is the whole of it.</summary>
        [Fact]
        public void ACheckWindowOfExactlyTheStorysLife_StaysClean()
        {
            AssertClean(LoadOne(EventJson(
                check: "{\"spec\":" + SpecJson(kind: "\"delta\"", comparison: "\"lte\"",
                                                threshold: "-0.05",
                                                windowMonths: StoryLifeMonths.ToString(CultureInfo.InvariantCulture)) + "}")));
        }

        /// <summary>
        /// <b>The rule is scoped to the check and must stay there.</b> A trigger asks what the city has
        /// been doing for the last two years, which is a fair question the player is not being scored
        /// on — it is bounded by <c>snapshotRetention</c>, not by the story's life, and the two bounds
        /// differ by more than an order of magnitude at the shipped values.
        /// </summary>
        [Fact]
        public void ALongTriggerWindow_DoesNotOutrunTheStorysLife()
        {
            Assert.True(MaxDeltaWindow > StoryLifeMonths,
                "the two bounds must differ for this fixture to prove anything");

            AssertClean(LoadOne(EventJson(trigger: DeltaSpec(MaxDeltaWindow))));
        }

        // ================================================================== 121 ThresholdAboveAttainableMaximum

        /// <summary>
        /// The two published ceilings, asserted as the arithmetic they are rather than as remembered
        /// decimals: <c>serviceCoverage</c> is a mean over nine channels of which four are hard-zeroed,
        /// and <c>pollution</c> a mean over four of which one is not measurable from the CPU.
        /// </summary>
        [Fact]
        public void AttainableMaximum_PublishesTheTwoCeilingsAsFractions()
        {
            Assert.Equal(5.0 / 9.0, CivicEventCatalogLoader.AttainableMaximum(MetricRegistry.ServiceCoverageMean));
            Assert.Equal(3.0 / 4.0, CivicEventCatalogLoader.AttainableMaximum(MetricRegistry.PollutionMean));

            // Every other metric is unbounded as far as this loader knows, and silence is the answer.
            Assert.Null(CivicEventCatalogLoader.AttainableMaximum(MetricRegistry.Happiness));
            Assert.Null(CivicEventCatalogLoader.AttainableMaximum("notAMetric"));
        }

        /// <summary>
        /// A threshold above what the sensor can ever report says nothing about the city: a <c>gte</c>
        /// can never be met, and a <c>lt</c> is met by every city always.
        /// </summary>
        [Theory]
        [InlineData("serviceCoverage")]
        [InlineData("pollution")]
        public void ThresholdAboveAttainableMaximum_WarnsAboveTheCeiling(string metricId)
        {
            double ceiling = CivicEventCatalogLoader.AttainableMaximum(metricId)!.Value;

            CivicEventCatalogLoadResult result = LoadOne(EventJson(
                trigger: SpecJson(metricId: "\"" + metricId + "\"", comparison: "\"gte\"",
                                  threshold: Number(ceiling + 0.01))));

            Assert.Empty(result.Errors);
            Assert.Equal(0, result.RejectedEventCount);
            Assert.Contains(result.Warnings, w => w.Code == CatalogIssueCode.ThresholdAboveAttainableMaximum);
            Assert.Single(result.Catalog.Events);
        }

        /// <summary>
        /// The positive pair, at and below the ceiling. Exactly at it is attainable — the boundary is
        /// the one place an off-by-one in the comparison would show, and a threshold merely demanding
        /// is a judgement this loader has no basis to make.
        /// </summary>
        [Theory]
        [InlineData("serviceCoverage")]
        [InlineData("pollution")]
        public void AThresholdAtOrBelowTheCeiling_StaysClean(string metricId)
        {
            double ceiling = CivicEventCatalogLoader.AttainableMaximum(metricId)!.Value;

            AssertClean(LoadOne(EventJson(
                trigger: SpecJson(metricId: "\"" + metricId + "\"", comparison: "\"gte\"",
                                  threshold: Number(ceiling)))));

            AssertClean(LoadOne(EventJson(
                trigger: SpecJson(metricId: "\"" + metricId + "\"", comparison: "\"gte\"",
                                  threshold: Number(ceiling - 0.01)))));
        }

        /// <summary>
        /// A metric with no published ceiling is not policed at all — the rule must not invent a 1.0
        /// bound for metrics whose sensors are complete.
        /// </summary>
        [Fact]
        public void AMetricWithNoPublishedCeiling_IsNotPoliced()
        {
            AssertClean(LoadOne(EventJson(
                trigger: SpecJson(metricId: "\"" + MetricRegistry.Happiness + "\"",
                                  comparison: "\"gte\"", threshold: "0.99"))));
        }

        /// <summary>
        /// A <c>delta</c> is a change rather than a level, and a change may legitimately exceed the
        /// level's ceiling — a swing of 0.9 on a metric that tops out at 0.5556 is impossible, but the
        /// loader deliberately does not reason about that, and this pins the scoping so the rule is not
        /// quietly widened to deltas where it would be wrong.
        /// </summary>
        [Fact]
        public void ADeltaThreshold_IsNotComparedAgainstTheLevelCeiling()
        {
            AssertClean(LoadOne(EventJson(
                trigger: SpecJson(kind: "\"delta\"", metricId: "\"" + MetricRegistry.PollutionMean + "\"",
                                  comparison: "\"gte\"", threshold: "0.9", windowMonths: Window(6)))));
        }

        // ============================================== the entry shape: codes 20-52, this loader's own

        /// <summary>
        /// <b>The closed effect palette</b> — the loader's own remarks name it and the metric registry
        /// as the two reasons this validator exists, and JSON Schema can express neither. The registry
        /// half is checked above; this is the palette half.
        /// </summary>
        /// <remarks>
        /// <c>ShippedCivicEventCatalogTests.ShippedCatalogs_NameOnlyReachableMetricsAndEffects</c> looks
        /// like it covers this and does not: it re-derives against the <i>loaded</i> catalog, so a
        /// rejected entry has already been discarded before it looks, and on near-empty catalogs it is
        /// vacuously true. This is a fixture, so it keeps its teeth whatever ships.
        /// </remarks>
        [Theory]
        [InlineData("activeEffects")]
        [InlineData("successEffects")]
        [InlineData("failureEffects")]
        public void UnknownEffectId_ForAnIdOutsideTheClosedPalette(string key)
        {
            AssertRejected(LoadOne(EventJson(extra: "\"" + key + "\":[\"district-wellbeing-plus\"]")),
                CatalogIssueCode.UnknownEffectId);
        }

        [Theory]
        [InlineData("\"loan-interest-spike\"")]        // the timeline catalog's naming, never a palette id
        [InlineData("\"District-Wellbeing\"")]         // palette ids are ordinal
        [InlineData("\"district-wellbeing \"")]        // trailing space
        [InlineData("\"Wellbeing\"")]                  // the modifier name, not the effect id
        public void UnknownEffectId_ForNearMissesOnARealPaletteId(string effectId)
        {
            AssertRejected(LoadOne(EventJson(activeEffects: "[" + effectId + "]")),
                CatalogIssueCode.UnknownEffectId);
        }

        /// <summary>
        /// The positive sweep: <b>every</b> id in the shipped palette is nameable by an authored civic
        /// event. Paired with the theories above, so neither "refuse everything" nor "accept
        /// everything" could pass both.
        /// </summary>
        /// <remarks>
        /// <b>This is not a rename tripwire, and an earlier version of this comment claimed it was.</b>
        /// The ids and the loader's palette are drawn from the same <see cref="EngineTuning"/>
        /// instance, so renaming an entry in <c>data/engine_tuning.json</c> moves both sides together
        /// and this test stays green — verified by doing it. What it actually proves is that the
        /// loader admits <i>every</i> id the registry carries, so it fails if the loader ever becomes
        /// <b>narrower</b> than the palette: an id filtered out, a scope restriction added to
        /// <c>ReadEffectList</c>, a validity rule applied to civic effects that timeline effects are
        /// spared. The rename is caught by
        /// <c>ShippedTuningTests.ShippedTuningFile_ShipsTheSameEffectPaletteAsTheBuiltInRegistry</c>
        /// and its siblings, which compare the file against the built-in defaults — two independent
        /// sources, which is what a tripwire needs and what this test does not have.
        /// </remarks>
        [Fact]
        public void EveryPaletteEntry_IsNameableByAnAuthoredEvent()
        {
            IReadOnlyList<string> ids = Tuning.Effects.EffectIds;
            Assert.NotEmpty(ids);

            for (int i = 0; i < ids.Count; i++)
            {
                CivicEventCatalogLoadResult result =
                    LoadOne(EventJson(activeEffects: "[\"" + ids[i] + "\"]"));

                Assert.True(result.IsClean, ids[i] + " should be nameable: " + Describe(result));
                Assert.Equal(new[] { ids[i] }, Assert.Single(result.Catalog.Events).ActiveEffects);
            }
        }

        // --- 28 SeverityOutOfRange ----------------------------------------------------------------

        /// <summary>
        /// The bound is <c>catalog.severityMax</c>, read from tuning rather than typed as 5.
        /// <c>AffinityEngine.EventTerm</c> scales by <c>severity/5</c>, so an unbounded severity is an
        /// uncapped effect magnitude wearing a different name.
        /// </summary>
        [Fact]
        public void SeverityOutOfRange_OutsideOneToSeverityMax()
        {
            Assert.True(SeverityMax >= 1, "catalog.severityMax must admit at least one severity");

            AssertClean(LoadOne(EventJson(severity: "1")));
            AssertClean(LoadOne(EventJson(severity: SeverityMax.ToString(CultureInfo.InvariantCulture))));

            AssertRejected(LoadOne(EventJson(severity: "0")), CatalogIssueCode.SeverityOutOfRange);
            AssertRejected(LoadOne(EventJson(severity: "-1")), CatalogIssueCode.SeverityOutOfRange);
            AssertRejected(LoadOne(EventJson(
                severity: (SeverityMax + 1).ToString(CultureInfo.InvariantCulture))),
                CatalogIssueCode.SeverityOutOfRange);
        }

        [Theory]
        [InlineData("2.5")]     // severity is an integer tier, not a dial
        [InlineData("\"3\"")]
        [InlineData(null)]      // absent
        public void SeverityOutOfRange_ForANonIntegerSeverity(string? severity)
        {
            AssertRejected(LoadOne(EventJson(severity: severity)), CatalogIssueCode.SeverityOutOfRange);
        }

        // --- 33 / 34 issue pressure ----------------------------------------------------------------

        /// <summary>
        /// The stance range is <c>[-1, +1]</c> on both sides, driven past the bound in both directions.
        /// A cap that only holds one way is not a cap.
        /// </summary>
        [Theory]
        [InlineData("{\"transit\":1.5}")]
        [InlineData("{\"transit\":-1.5}")]
        [InlineData("{\"services\":-0.2,\"growth\":42}")]
        public void IssuePressureOutOfRange_BeyondTheStanceRange(string pressure)
        {
            AssertRejected(LoadOne(EventJson(activePressure: pressure)),
                CatalogIssueCode.IssuePressureOutOfRange);
        }

        /// <summary>Exactly at the bound is inside it — the positive pair for the theory above.</summary>
        [Fact]
        public void IssuePressureAtTheStanceBounds_StaysClean()
        {
            CivicEventCatalogLoadResult result =
                LoadOne(EventJson(activePressure: "{\"transit\":1.0,\"services\":-1.0}"));

            AssertClean(result);
            CivicEvent loaded = Assert.Single(result.Catalog.Events);
            Assert.Equal(1.0, loaded.ActivePressure[Issue.Transit]);
            Assert.Equal(-1.0, loaded.ActivePressure[Issue.Services]);
        }

        [Theory]
        [InlineData("\"anxious\"")]                  // not an object
        [InlineData("[-0.4]")]
        [InlineData("{\"services\":\"-0.4\"}")]      // a string component
        [InlineData("{\"services\":null}")]
        [InlineData("{\"services\":{\"x\":1}}")]
        public void MalformedIssuePressure_WhenItIsNotAnObjectOfNumbers(string pressure)
        {
            AssertRejected(LoadOne(EventJson(activePressure: pressure)),
                CatalogIssueCode.MalformedIssuePressure);
        }

        /// <summary>
        /// An unstated issue is simply not pressed, and every issue defaults to centre — the loader
        /// must not require all six to be spelled out.
        /// </summary>
        [Fact]
        public void AnUnstatedIssueIsCentreRatherThanAnError()
        {
            CivicEventCatalogLoadResult result = LoadOne(EventJson());

            AssertClean(result);
            CivicEvent loaded = Assert.Single(result.Catalog.Events);
            for (int i = 0; i < Issues.All.Count; i++)
            {
                Assert.Equal(0.0, loaded.ActivePressure[Issues.All[i]]);
            }
        }

        // --- 20 / 21 / 22 / 26 the entry itself ----------------------------------------------------

        [Theory]
        [InlineData("42")]
        [InlineData("\"glob-one\"")]
        [InlineData("[]")]
        [InlineData("null")]
        public void EventNotObject_ForAnEventsElementThatIsNotAnObject(string element)
        {
            AssertRejected(CivicEventCatalogLoader.Load("events_global.json", Doc(events: element), Tuning),
                CatalogIssueCode.EventNotObject);
        }

        [Theory]
        [InlineData("\"\"")]
        [InlineData("42")]        // not a string
        [InlineData("null")]
        [InlineData(null)]        // absent
        public void MissingEventId_WhenThereIsNoUsableId(string? id)
        {
            AssertRejected(LoadOne(EventJson(id: id)), CatalogIssueCode.MissingEventId);
        }

        /// <summary>
        /// Ids are lowercase kebab-case, which is what makes the <c>glob-</c>/<c>eu-</c>/<c>na-</c>
        /// prefix convention a mechanical guarantee against three blind content lanes colliding.
        /// </summary>
        [Theory]
        [InlineData("\"GLOB-ONE\"")]
        [InlineData("\"glob One\"")]
        [InlineData("\"glob_one\"")]
        [InlineData("\"glob.one\"")]
        [InlineData("\"glob/one\"")]
        [InlineData("\"glöb-one\"")]
        public void MalformedEventId_ForAnythingOutsideLowercaseKebabCase(string id)
        {
            AssertRejected(LoadOne(EventJson(id: id)), CatalogIssueCode.MalformedEventId);
        }

        [Theory]
        [InlineData("\"glob-one\"")]
        [InlineData("\"eu-2\"")]
        [InlineData("\"na-housing-crisis-2\"")]
        [InlineData("\"a\"")]
        public void AKebabCaseIdIsAccepted(string id)
        {
            AssertClean(LoadOne(EventJson(id: id)));
        }

        [Theory]
        [InlineData("\"apac\"")]
        [InlineData("\"EU\"")]        // the region vocabulary is ordinal
        [InlineData("\"world\"")]
        [InlineData("0")]
        [InlineData(null)]            // absent
        public void UnknownRegion_ForAnythingOutsideTheThreeThemes(string? region)
        {
            AssertRejected(LoadOne(EventJson(region: region)), CatalogIssueCode.UnknownRegion);
        }

        [Theory]
        [InlineData("\"eu\"", EventRegion.Eu)]
        [InlineData("\"na\"", EventRegion.Na)]
        [InlineData("\"global\"", EventRegion.Global)]
        public void EachDeclaredRegionIsAccepted(string region, EventRegion expected)
        {
            CivicEventCatalogLoadResult result = LoadOne(EventJson(region: region));

            AssertClean(result);
            Assert.Equal(expected, Assert.Single(result.Catalog.Events).Region);
        }

        // --- 31 tags -------------------------------------------------------------------------------

        [Theory]
        [InlineData("\"housing\"")]
        [InlineData("{\"a\":1}")]
        [InlineData("[\"\"]")]
        [InlineData("[5]")]
        public void MalformedTags_WhenTagsAreNotAnArrayOfNonEmptyStrings(string tags)
        {
            AssertRejected(LoadOne(EventJson(tags: tags)), CatalogIssueCode.MalformedTags);
        }

        [Fact]
        public void Tags_LoadSortedOrdinal()
        {
            CivicEventCatalogLoadResult result = LoadOne(EventJson(tags: "[\"transport\",\"budget\",\"crime\"]"));

            AssertClean(result);
            Assert.Equal(new[] { "budget", "crime", "transport" },
                Assert.Single(result.Catalog.Events).Tags);
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

        /// <summary>
        /// A corrupt document reports and never throws — and it reports the <i>right</i> reason.
        /// "Unparseable" and "parsed fine but is not a catalog" are different authoring mistakes with
        /// different fixes, and a bare <c>NotEmpty(Errors)</c> would not tell them apart.
        /// </summary>
        [Theory]
        [InlineData("", CatalogIssueCode.MalformedJson)]
        [InlineData("   ", CatalogIssueCode.MalformedJson)]
        [InlineData("not json at all", CatalogIssueCode.MalformedJson)]
        [InlineData("{ \"schemaVersion\": 1, \"events\": [ { ", CatalogIssueCode.MalformedJson)]
        [InlineData("[1, 2, 3]", CatalogIssueCode.RootNotObject)]
        [InlineData("\"events\"", CatalogIssueCode.RootNotObject)]
        [InlineData("7", CatalogIssueCode.RootNotObject)]
        public void ACorruptDocument_ReportsItsOwnReasonAndNeverThrows(string json, CatalogIssueCode expected)
        {
            CivicEventCatalogLoadResult result = CivicEventCatalogLoader.Load("events_global.json", json, Tuning);

            Assert.Contains(result.Errors, e => e.Code == expected);
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

        /// <summary>
        /// The order the caller happened to enumerate the files in must not reach the output.
        /// </summary>
        /// <remarks>
        /// <b>This does not by itself carry the name-ordering claim, and must not be read as if it
        /// did.</b> With disjoint ids the accepted list is sorted by id and the outcome is
        /// order-independent whether or not <c>OrderSources</c> sorts — so this test would still pass
        /// with the name sort deleted. What the sort actually decides is <i>which copy of a duplicated
        /// id survives</i>, and <see cref="DuplicateIdAcrossDocuments_NamesTheSecondAndKeepsTheFirstByName"/>
        /// is the test that proves it. Delete that one and the guarantee is unowned, however green
        /// this hash comparison looks.
        /// </remarks>
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
            EventJson(id: "\"beta-event\"", trigger: DeltaSpec(SomeLegalWindow(6)),
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
            string? successEffects = null,
            string? failureEffects = null,
            string? activePressure = null,
            string? successPressure = null,
            string? failurePressure = null,
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
            Add(parts, "successEffects", successEffects);
            Add(parts, "failureEffects", failureEffects);
            Add(parts, "activePressure", activePressure);
            Add(parts, "successPressure", successPressure);
            Add(parts, "failurePressure", failurePressure);
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
