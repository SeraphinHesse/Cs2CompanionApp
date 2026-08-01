using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Events.Catalog;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Packet 11 — the timeline catalog loader and validator.
    ///
    /// <para>
    /// The loader is the build-time gate that keeps <c>data/timeline_*.json</c> honest: an effect id
    /// that is not in the palette registry, a magnitude or duration outside the declared cap, a
    /// malformed date or a duplicated id must be refused here rather than discovered as a silently
    /// clamped effect three in-game decades later.
    /// </para>
    ///
    /// <para>
    /// Every fixture is a synthetic document built in this file. Nothing touches the filesystem, and
    /// tuning comes from <see cref="EngineTuning.Default"/>, which is documented as identical to the
    /// shipped file — so a test never depends on the working directory.
    /// </para>
    /// </summary>
    public class TimelineCatalogTests
    {
        private static readonly EngineTuning Tuning = EngineTuning.Default;

        // --- happy path -------------------------------------------------------------------------

        [Fact]
        public void Load_ReadsAWellFormedEvent()
        {
            string json = Doc(EventJson(
                id: "\"gfc-2008\"",
                dateISO: "\"2008-09-15\"",
                region: "\"global\"",
                title: "\"Global financial crisis\"",
                severity: "3",
                durationMonths: "24",
                effects: "[" + EffectJson(effectId: "\"city-loan-interest\"", scope: "\"city\"",
                                          magnitude: "0.20", durationMonths: "18") + "]",
                tags: "[\"economy\",\"finance\"]"));

            TimelineCatalogLoadResult result = TimelineCatalogLoader.Load("timeline_global.json", json, Tuning);

            Assert.True(result.IsValid, Describe(result));
            Assert.Empty(result.Warnings);
            Assert.Equal(0, result.RejectedEventCount);

            TimelineEvent loaded = Assert.Single(result.Catalog.Events);
            Assert.Equal("gfc-2008", loaded.Id);
            Assert.Equal(new SimDate(2008, 9, 15), loaded.Date);
            Assert.Equal(EventRegion.Global, loaded.Region);
            Assert.Equal(EventOrigin.Catalog, loaded.Origin);
            Assert.Equal(1, loaded.SchemaVersion);
            Assert.Equal("Global financial crisis", loaded.Title);
            Assert.Equal(3, loaded.Severity);
            Assert.Equal(24, loaded.DurationMonths);
            Assert.Equal(new[] { "economy", "finance" }, loaded.Tags);
            Assert.Equal("", loaded.ArchetypeId);
            Assert.Null(loaded.FiredDate);
            Assert.Null(loaded.ExpiresDate);

            TimelineEventEffect effect = Assert.Single(loaded.Effects);
            Assert.Equal("city-loan-interest", effect.EffectId);
            Assert.Equal(EffectScope.City, effect.Scope);
            Assert.Equal(0.20, effect.Magnitude);
            Assert.Equal(18, effect.DurationMonths);

            // The catalog never names a district: real history does not know the player's map.
            Assert.Null(effect.DistrictId);
        }

        [Fact]
        public void Load_AcceptsAnEmptyCatalog()
        {
            // The shape the three shipped catalogs currently have.
            TimelineCatalogLoadResult result =
                TimelineCatalogLoader.Load("timeline_eu.json", "{ \"schemaVersion\": 1, \"events\": [] }", Tuning);

            Assert.True(result.IsValid, Describe(result));
            Assert.Empty(result.Catalog.Events);
        }

        [Fact]
        public void Load_LeavesIssuePressureAtCentreWhenUnstated()
        {
            TimelineCatalogLoadResult result = LoadOne(EventJson());

            TimelineEvent loaded = Assert.Single(result.Catalog.Events);
            for (int i = 0; i < Issues.All.Count; i++)
            {
                Assert.Equal(0.0, loaded.IssuePressure[Issues.All[i]]);
            }
        }

        [Fact]
        public void Load_ReadsIssuePressurePerIssue()
        {
            TimelineCatalogLoadResult result = LoadOne(EventJson(
                issuePressure: "{\"environment\":-0.5,\"costOfLiving\":0.75}"));

            Assert.True(result.IsValid, Describe(result));
            TimelineEvent loaded = Assert.Single(result.Catalog.Events);
            Assert.Equal(-0.5, loaded.IssuePressure.Environment);
            Assert.Equal(0.75, loaded.IssuePressure.CostOfLiving);
            Assert.Equal(0.0, loaded.IssuePressure.Transit);
        }

        // --- rejection: unknown effect id --------------------------------------------------------

        [Fact]
        public void Load_RejectsEffectIdOutsideThePaletteRegistry()
        {
            TimelineCatalogLoadResult result = LoadOne(EventJson(
                effects: "[" + EffectJson(effectId: "\"loan-interest-spike\"", scope: "\"city\"") + "]"));

            AssertRejected(result, CatalogIssueCode.UnknownEffectId);
        }

        [Fact]
        public void Load_RejectsAnEffectWithNoEffectIdAtAll()
        {
            TimelineCatalogLoadResult result = LoadOne(EventJson(
                effects: "[" + EffectJson(effectId: null) + "]"));

            AssertRejected(result, CatalogIssueCode.UnknownEffectId);
        }

        [Fact]
        public void Load_RejectsAScopeThatDisagreesWithThePalette()
        {
            // city-loan-interest is city-scoped in the registry.
            TimelineCatalogLoadResult result = LoadOne(EventJson(
                effects: "[" + EffectJson(effectId: "\"city-loan-interest\"", scope: "\"district\"") + "]"));

            AssertRejected(result, CatalogIssueCode.EffectScopeMismatch);
        }

        [Fact]
        public void Load_RejectsAnEffectThatNamesADistrict()
        {
            TimelineCatalogLoadResult result = LoadOne(EventJson(
                effects: "[" + EffectJson(extra: "\"districtId\":\"downtown\"") + "]"));

            AssertRejected(result, CatalogIssueCode.DistrictIdNotAllowed);
        }

        // --- rejection: magnitude and duration caps ----------------------------------------------

        [Fact]
        public void Load_AcceptsAMagnitudeExactlyAtTheDeclaredCap()
        {
            // district-wellbeing declares a magnitude cap of 0.15.
            TimelineCatalogLoadResult result = LoadOne(EventJson(
                effects: "[" + EffectJson(magnitude: "0.15") + "]"));

            Assert.True(result.IsValid, Describe(result));
            Assert.Equal(0.15, Assert.Single(Assert.Single(result.Catalog.Events).Effects).Magnitude);
        }

        [Fact]
        public void Load_RejectsAMagnitudeAboveTheDeclaredCap()
        {
            TimelineCatalogLoadResult result = LoadOne(EventJson(
                effects: "[" + EffectJson(magnitude: "0.16") + "]"));

            AssertRejected(result, CatalogIssueCode.MagnitudeOutOfCap);
        }

        [Fact]
        public void Load_RejectsAMagnitudeBelowTheNegativeCap()
        {
            // A cap that only holds in the positive direction is not a cap.
            TimelineCatalogLoadResult result = LoadOne(EventJson(
                effects: "[" + EffectJson(magnitude: "-0.16") + "]"));

            AssertRejected(result, CatalogIssueCode.MagnitudeOutOfCap);
        }

        [Theory]
        [InlineData("\"0.1\"")]      // a string, not a number
        [InlineData((string?)null)]  // absent
        public void Load_RejectsANonNumericMagnitude(string? magnitude)
        {
            TimelineCatalogLoadResult result = LoadOne(EventJson(
                effects: "[" + EffectJson(magnitude: magnitude) + "]"));

            AssertRejected(result, CatalogIssueCode.MagnitudeNotFinite);
        }

        [Fact]
        public void Load_AcceptsADurationExactlyAtTheDeclaredCap()
        {
            // district-wellbeing declares a duration cap of 60 months.
            TimelineCatalogLoadResult result = LoadOne(EventJson(
                effects: "[" + EffectJson(durationMonths: "60") + "]"));

            Assert.True(result.IsValid, Describe(result));
        }

        [Fact]
        public void Load_RejectsADurationAboveTheDeclaredCap()
        {
            TimelineCatalogLoadResult result = LoadOne(EventJson(
                effects: "[" + EffectJson(durationMonths: "61") + "]"));

            AssertRejected(result, CatalogIssueCode.EffectDurationOutOfCap);
        }

        [Fact]
        public void Load_RejectsANegativeEffectDuration()
        {
            TimelineCatalogLoadResult result = LoadOne(EventJson(
                effects: "[" + EffectJson(durationMonths: "-1") + "]"));

            AssertRejected(result, CatalogIssueCode.EffectDurationOutOfCap);
        }

        [Fact]
        public void Load_RejectsAFractionalEffectDuration()
        {
            TimelineCatalogLoadResult result = LoadOne(EventJson(
                effects: "[" + EffectJson(durationMonths: "12.5") + "]"));

            AssertRejected(result, CatalogIssueCode.EffectDurationOutOfCap);
        }

        /// <summary>
        /// Sweeps the whole shipped palette: at the declared caps every entry loads, and one step past
        /// either cap every entry is refused. This is the test that fails when a palette entry gains a
        /// cap the validator does not consult.
        /// </summary>
        [Fact]
        public void Load_EnforcesEveryPaletteEntrysDeclaredCaps()
        {
            IReadOnlyList<string> ids = Tuning.Effects.EffectIds;
            Assert.NotEmpty(ids);

            for (int i = 0; i < ids.Count; i++)
            {
                EffectCap cap;
                Assert.True(Tuning.Effects.TryGetEffect(ids[i], out cap));

                string scope = cap.Scope == EffectScope.District ? "\"district\"" : "\"city\"";
                string atCap = Number(cap.MagnitudeCap);
                string overCap = Number(cap.MagnitudeCap + 0.0001);
                string atDuration = cap.DurationCapMonths.ToString(CultureInfo.InvariantCulture);
                string overDuration = (cap.DurationCapMonths + 1).ToString(CultureInfo.InvariantCulture);

                TimelineCatalogLoadResult ok = LoadOne(EventJson(effects: "[" + EffectJson(
                    effectId: "\"" + ids[i] + "\"", scope: scope,
                    magnitude: atCap, durationMonths: atDuration) + "]"));
                Assert.True(ok.IsValid, ids[i] + " should load at its declared caps: " + Describe(ok));

                TimelineCatalogLoadResult tooBig = LoadOne(EventJson(effects: "[" + EffectJson(
                    effectId: "\"" + ids[i] + "\"", scope: scope,
                    magnitude: overCap, durationMonths: atDuration) + "]"));
                AssertRejected(tooBig, CatalogIssueCode.MagnitudeOutOfCap);

                TimelineCatalogLoadResult tooLong = LoadOne(EventJson(effects: "[" + EffectJson(
                    effectId: "\"" + ids[i] + "\"", scope: scope,
                    magnitude: atCap, durationMonths: overDuration) + "]"));
                AssertRejected(tooLong, CatalogIssueCode.EffectDurationOutOfCap);
            }
        }

        // --- rejection: dates --------------------------------------------------------------------

        [Theory]
        [InlineData("\"2008-9-15\"")]    // not zero-padded
        [InlineData("\"2008-13-01\"")]   // month 13
        [InlineData("\"2008-00-10\"")]   // month 0
        [InlineData("\"2008-02-31\"")]   // no such day
        [InlineData("\"2008-09-00\"")]   // day 0
        [InlineData("\"20080915\"")]     // no separators
        [InlineData("\"2008-09-15T00:00:00Z\"")]
        [InlineData("\"yesterday\"")]
        [InlineData("\"\"")]
        [InlineData("20080915")]         // a number, not a string
        [InlineData((string?)null)]      // absent
        public void Load_RejectsAMalformedDate(string? dateISO)
        {
            TimelineCatalogLoadResult result = LoadOne(EventJson(dateISO: dateISO));

            AssertRejected(result, CatalogIssueCode.MalformedDate);
        }

        [Fact]
        public void Load_AcceptsALeapDay()
        {
            TimelineCatalogLoadResult result = LoadOne(EventJson(dateISO: "\"2000-02-29\""));

            Assert.True(result.IsValid, Describe(result));
            Assert.Equal(new SimDate(2000, 2, 29), Assert.Single(result.Catalog.Events).Date);
        }

        // --- rejection: duplicate ids ------------------------------------------------------------

        [Fact]
        public void Load_RejectsADuplicateIdWithinOneDocument()
        {
            string json = Doc(
                EventJson(id: "\"gfc-2008\""),
                EventJson(id: "\"gfc-2008\"", title: "\"A second claim on the same id\""));

            TimelineCatalogLoadResult result = TimelineCatalogLoader.Load("timeline_global.json", json, Tuning);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Code == CatalogIssueCode.DuplicateEventId);
            Assert.Equal(1, result.RejectedEventCount);
            Assert.Single(result.Catalog.Events);
            Assert.Equal("Global financial crisis", result.Catalog.Events[0].Title);
        }

        [Fact]
        public void Load_RejectsADuplicateIdAcrossDocuments_KeepingTheOneFromTheFirstSourceByName()
        {
            var sources = new[]
            {
                // Handed in out of name order on purpose: the survivor must not depend on this.
                new TimelineCatalogSource("timeline_na.json", Doc(EventJson(title: "\"North American telling\""))),
                new TimelineCatalogSource("timeline_eu.json", Doc(EventJson(title: "\"European telling\"")))
            };

            TimelineCatalogLoadResult result = TimelineCatalogLoader.Load(sources, Tuning);

            Assert.False(result.IsValid);
            CatalogIssue duplicate = Assert.Single(result.Errors, e => e.Code == CatalogIssueCode.DuplicateEventId);
            Assert.Equal("timeline_na.json", duplicate.SourceName);
            Assert.Equal("European telling", Assert.Single(result.Catalog.Events).Title);
        }

        [Theory]
        [InlineData("\"GFC-2008\"")]
        [InlineData("\"gfc 2008\"")]
        [InlineData("\"gfc_2008\"")]
        public void Load_RejectsAnIdThatIsNotKebabCase(string id)
        {
            AssertRejected(LoadOne(EventJson(id: id)), CatalogIssueCode.MalformedEventId);
        }

        [Fact]
        public void Load_RejectsAnEventWithNoId()
        {
            AssertRejected(LoadOne(EventJson(id: null)), CatalogIssueCode.MissingEventId);
        }

        // --- rejection: the rest of the entry shape ----------------------------------------------

        [Theory]
        [InlineData("0")]
        [InlineData("6")]     // catalog.severityMax is 5
        [InlineData("2.5")]
        [InlineData("\"4\"")]
        public void Load_RejectsSeverityOutsideOneToSeverityMax(string severity)
        {
            AssertRejected(LoadOne(EventJson(severity: severity)), CatalogIssueCode.SeverityOutOfRange);
        }

        [Theory]
        [InlineData("-1")]
        [InlineData("241")]   // catalog.effectDurationCapMonths is 240
        public void Load_RejectsAnEventDurationOutsideTheCatalogCeiling(string durationMonths)
        {
            AssertRejected(LoadOne(EventJson(durationMonths: durationMonths)), CatalogIssueCode.DurationOutOfRange);
        }

        [Fact]
        public void Load_RejectsAnUnknownRegion()
        {
            AssertRejected(LoadOne(EventJson(region: "\"apac\"")), CatalogIssueCode.UnknownRegion);
        }

        [Fact]
        public void Load_RejectsABlankTitle()
        {
            AssertRejected(LoadOne(EventJson(title: "\"   \"")), CatalogIssueCode.MissingTitle);
        }

        [Fact]
        public void Load_RejectsAMissingHeadlineBrief()
        {
            AssertRejected(LoadOne(EventJson(headlineBrief: null)), CatalogIssueCode.MissingHeadlineBrief);
        }

        [Fact]
        public void Load_RejectsIssuePressureOutsideTheStanceRange()
        {
            AssertRejected(LoadOne(EventJson(issuePressure: "{\"transit\":1.5}")),
                CatalogIssueCode.IssuePressureOutOfRange);
        }

        // --- rejection: document level -----------------------------------------------------------

        [Fact]
        public void Load_ReportsMalformedJsonInsteadOfThrowing()
        {
            TimelineCatalogLoadResult result =
                TimelineCatalogLoader.Load("timeline_global.json", "{ \"schemaVersion\": 1, \"events\": [", Tuning);

            Assert.False(result.IsValid);
            Assert.Equal(CatalogIssueCode.MalformedJson, Assert.Single(result.Errors).Code);
            Assert.Empty(result.Catalog.Events);
        }

        [Fact]
        public void Load_RejectsAnUnsupportedSchemaVersion()
        {
            TimelineCatalogLoadResult result = TimelineCatalogLoader.Load(
                "timeline_global.json", "{ \"schemaVersion\": 2, \"events\": [] }", Tuning);

            Assert.Equal(CatalogIssueCode.UnsupportedSchemaVersion, Assert.Single(result.Errors).Code);
        }

        [Fact]
        public void Load_RejectsADocumentWithNoEventsArray()
        {
            TimelineCatalogLoadResult result = TimelineCatalogLoader.Load(
                "timeline_global.json", "{ \"schemaVersion\": 1 }", Tuning);

            Assert.Equal(CatalogIssueCode.EventsMissing, Assert.Single(result.Errors).Code);
        }

        /// <summary>One broken entry must not cost the rest of the document.</summary>
        [Fact]
        public void Load_KeepsTheValidEntriesAroundABrokenOne()
        {
            string json = Doc(
                EventJson(id: "\"first-event\"", dateISO: "\"1995-04-01\""),
                EventJson(id: "\"broken-event\"", dateISO: "\"1996-13-01\""),
                EventJson(id: "\"third-event\"", dateISO: "\"1997-04-01\""));

            TimelineCatalogLoadResult result = TimelineCatalogLoader.Load("timeline_global.json", json, Tuning);

            Assert.False(result.IsValid);
            Assert.Equal(1, result.RejectedEventCount);
            Assert.Equal(new[] { "first-event", "third-event" }, Ids(result));
        }

        // --- warnings (loud, but not fatal) ------------------------------------------------------

        [Fact]
        public void Load_WarnsWhenSeverityScalingWouldClampTheMagnitude()
        {
            // 0.15 is exactly district-wellbeing's cap; severity 5 scales it to 0.27.
            TimelineCatalogLoadResult result = LoadOne(EventJson(
                severity: "5",
                effects: "[" + EffectJson(magnitude: "0.15") + "]"));

            Assert.True(result.IsValid, Describe(result));
            Assert.Contains(result.Warnings, w => w.Code == CatalogIssueCode.SeverityScaledMagnitudeClamped);
        }

        [Fact]
        public void Load_WarnsAboutADateOutsideTheCuratedWindow()
        {
            TimelineCatalogLoadResult result = LoadOne(EventJson(dateISO: "\"1975-04-01\""));

            Assert.True(result.IsValid, Describe(result));
            Assert.Contains(result.Warnings, w => w.Code == CatalogIssueCode.DateOutsideCatalogWindow);
        }

        [Fact]
        public void Load_WarnsAboutAnUndeclaredPropertyButKeepsTheEvent()
        {
            TimelineCatalogLoadResult result = LoadOne(EventJson(extra: "\"mood\":\"anxious\""));

            Assert.True(result.IsValid, Describe(result));
            CatalogIssue warning = Assert.Single(result.Warnings, w => w.Code == CatalogIssueCode.UnknownProperty);
            Assert.Equal("events[0].mood", warning.Path);
        }

        [Fact]
        public void Load_IgnoresUnderscoreCommentKeys()
        {
            TimelineCatalogLoadResult result = LoadOne(EventJson(extra: "\"_comment\":\"authored from memory\""));

            Assert.True(result.IsValid, Describe(result));
            Assert.Empty(result.Warnings);
        }

        [Fact]
        public void Load_WarnsAboutAZeroMagnitudeEffect()
        {
            TimelineCatalogLoadResult result = LoadOne(EventJson(
                effects: "[" + EffectJson(magnitude: "0.0") + "]"));

            Assert.True(result.IsValid, Describe(result));
            Assert.Contains(result.Warnings, w => w.Code == CatalogIssueCode.ZeroMagnitude);
        }

        // --- ordering and determinism ------------------------------------------------------------

        [Fact]
        public void Load_SortsEventsByDateThenId()
        {
            var sources = new[]
            {
                new TimelineCatalogSource("z-source.json", Doc(
                    EventJson(id: "\"late-event\"", dateISO: "\"2010-01-01\""),
                    EventJson(id: "\"alpha-event\"", dateISO: "\"1999-06-01\""))),
                new TimelineCatalogSource("a-source.json", Doc(
                    EventJson(id: "\"omega-event\"", dateISO: "\"1999-06-01\""),
                    EventJson(id: "\"early-event\"", dateISO: "\"1991-03-01\"")))
            };

            TimelineCatalogLoadResult result = TimelineCatalogLoader.Load(sources, Tuning);

            Assert.True(result.IsValid, Describe(result));
            Assert.Equal(new[] { "early-event", "alpha-event", "omega-event", "late-event" }, Ids(result));
        }

        /// <summary>
        /// The canonical determinism check: identical inputs, byte-identical output. Paired with the
        /// negative below, so a loader that returned a constant could not pass both.
        /// </summary>
        [Fact]
        public void Load_ProducesIdenticalOutputTwice()
        {
            string json = MixedFixture();

            Assert.Equal(
                Hash(TimelineCatalogLoader.Load("timeline_global.json", json, Tuning)),
                Hash(TimelineCatalogLoader.Load("timeline_global.json", json, Tuning)));
        }

        [Fact]
        public void Load_ProducesDifferentOutputForDifferentContent()
        {
            string a = MixedFixture();
            string b = MixedFixture().Replace("1999-06-01", "2001-06-01");

            Assert.NotEqual(
                Hash(TimelineCatalogLoader.Load("timeline_global.json", a, Tuning)),
                Hash(TimelineCatalogLoader.Load("timeline_global.json", b, Tuning)));
        }

        /// <summary>
        /// The order the caller happened to enumerate the files in must not reach the output — that is
        /// exactly the kind of coupling that makes one save produce two histories.
        /// </summary>
        [Fact]
        public void Load_IsIndependentOfTheOrderSourcesAreHandedIn()
        {
            var eu = new TimelineCatalogSource("timeline_eu.json",
                Doc(EventJson(id: "\"euro-launch\"", dateISO: "\"1999-01-01\"", region: "\"eu\"")));
            var na = new TimelineCatalogSource("timeline_na.json",
                Doc(EventJson(id: "\"nafta-signed\"", dateISO: "\"1994-01-01\"", region: "\"na\"")));
            var global = new TimelineCatalogSource("timeline_global.json",
                Doc(EventJson(id: "\"gfc-2008\"", dateISO: "\"2008-09-15\"")));

            string forward = Hash(TimelineCatalogLoader.Load(new[] { eu, na, global }, Tuning));
            string reversed = Hash(TimelineCatalogLoader.Load(new[] { global, na, eu }, Tuning));

            Assert.Equal(forward, reversed);
        }

        // --- selection -----------------------------------------------------------------------------

        [Fact]
        public void ForTheme_SelectsTheRegionsOwnEventsPlusGlobalOnes()
        {
            string json = Doc(
                EventJson(id: "\"euro-launch\"", dateISO: "\"1999-01-01\"", region: "\"eu\""),
                EventJson(id: "\"nafta-signed\"", dateISO: "\"1994-01-01\"", region: "\"na\""),
                EventJson(id: "\"gfc-2008\"", dateISO: "\"2008-09-15\"", region: "\"global\""));

            TimelineCatalogLoadResult result = TimelineCatalogLoader.Load("timeline_global.json", json, Tuning);
            Assert.True(result.IsValid, Describe(result));

            IReadOnlyList<TimelineEvent> eu = result.Catalog.ForTheme(RegionTheme.Eu, Tuning);
            Assert.Equal(new[] { "euro-launch", "gfc-2008" }, Ids(eu));

            IReadOnlyList<TimelineEvent> na = result.Catalog.ForTheme(RegionTheme.Na, Tuning);
            Assert.Equal(new[] { "nafta-signed", "gfc-2008" }, Ids(na));
        }

        [Fact]
        public void TryGetById_FindsALoadedEventAndNothingElse()
        {
            TimelineCatalogLoadResult result = LoadOne(EventJson(id: "\"gfc-2008\""));

            TimelineEvent? found;
            Assert.True(result.Catalog.TryGetById("gfc-2008", out found));
            Assert.NotNull(found);
            Assert.Equal("gfc-2008", found!.Id);

            Assert.False(result.Catalog.TryGetById("no-such-event", out found));
            Assert.Null(found);
        }

        // --- fixtures ------------------------------------------------------------------------------

        /// <summary>A document mixing valid entries, a rejected one and a warned one.</summary>
        private static string MixedFixture() => Doc(
            EventJson(id: "\"alpha-event\"", dateISO: "\"1999-06-01\"", region: "\"eu\"", severity: "2"),
            EventJson(id: "\"beta-event\"", dateISO: "\"2008-09-15\"", severity: "5",
                      effects: "[" + EffectJson(magnitude: "0.15") + "," +
                               EffectJson(effectId: "\"city-loan-interest\"", scope: "\"city\"",
                                          magnitude: "0.30", durationMonths: "18") + "]",
                      tags: "[\"economy\",\"finance\"]"),
            EventJson(id: "\"broken-event\"", dateISO: "\"2011-02-30\""),
            EventJson(id: "\"gamma-event\"", dateISO: "\"1975-01-01\"", region: "\"na\""));

        private static TimelineCatalogLoadResult LoadOne(string eventJson) =>
            TimelineCatalogLoader.Load("timeline_global.json", Doc(eventJson), Tuning);

        private static string Doc(params string[] events) =>
            "{ \"schemaVersion\": 1, \"events\": [" + string.Join(",", events) + "] }";

        /// <summary>
        /// Builds one <c>events[]</c> entry. Every parameter is a raw JSON fragment, and null omits the
        /// property — which is what lets one builder cover "absent", "wrong type" and "out of range".
        /// </summary>
        private static string EventJson(
            string? id = "\"gfc-2008\"",
            string? dateISO = "\"2008-09-15\"",
            string? region = "\"global\"",
            string? title = "\"Global financial crisis\"",
            string? severity = "3",
            string? durationMonths = "24",
            string? effects = null,
            string? headlineBrief = "\"Credit markets seize; lending costs spike.\"",
            string? tags = null,
            string? issuePressure = null,
            string? extra = null)
        {
            var parts = new List<string>();
            Add(parts, "id", id);
            Add(parts, "dateISO", dateISO);
            Add(parts, "region", region);
            Add(parts, "title", title);
            Add(parts, "severity", severity);
            Add(parts, "durationMonths", durationMonths);
            Add(parts, "effects", effects);
            Add(parts, "headlineBrief", headlineBrief);
            Add(parts, "tags", tags);
            Add(parts, "issuePressure", issuePressure);
            if (extra != null) parts.Add(extra);
            return "{" + string.Join(",", parts) + "}";
        }

        private static string EffectJson(
            string? effectId = "\"district-wellbeing\"",
            string? scope = "\"district\"",
            string? magnitude = "0.10",
            string? durationMonths = "12",
            string? extra = null)
        {
            var parts = new List<string>();
            Add(parts, "effectId", effectId);
            Add(parts, "scope", scope);
            Add(parts, "magnitude", magnitude);
            Add(parts, "durationMonths", durationMonths);
            if (extra != null) parts.Add(extra);
            return "{" + string.Join(",", parts) + "}";
        }

        private static void Add(List<string> parts, string key, string? value)
        {
            if (value != null) parts.Add("\"" + key + "\":" + value);
        }

        private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        // --- assertions ----------------------------------------------------------------------------

        private static void AssertRejected(TimelineCatalogLoadResult result, CatalogIssueCode expected)
        {
            Assert.False(result.IsValid, "expected a rejection but the entry loaded cleanly");
            Assert.Contains(result.Errors, e => e.Code == expected);
            Assert.Empty(result.Catalog.Events);
            Assert.Equal(1, result.RejectedEventCount);
        }

        private static string[] Ids(TimelineCatalogLoadResult result) => Ids(result.Catalog.Events);

        private static string[] Ids(IReadOnlyList<TimelineEvent> events)
        {
            var ids = new string[events.Count];
            for (int i = 0; i < events.Count; i++) ids[i] = events[i].Id;
            return ids;
        }

        private static string Describe(TimelineCatalogLoadResult result)
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
        private static string Hash(TimelineCatalogLoadResult result)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < result.Catalog.Events.Count; i++)
            {
                TimelineEvent e = result.Catalog.Events[i];
                sb.Append(e.SchemaVersion).Append('|')
                  .Append(e.Id).Append('|')
                  .Append(e.Date).Append('|')
                  .Append(e.Region).Append('|')
                  .Append(e.Origin).Append('|')
                  .Append(e.Title).Append('|')
                  .Append(e.Severity).Append('|')
                  .Append(e.DurationMonths).Append('|')
                  .Append(e.HeadlineBrief).Append('|')
                  .Append(string.Join(",", e.Tags)).Append('|')
                  .Append(e.ArchetypeId).Append('|');

                for (int k = 0; k < Issues.All.Count; k++)
                {
                    sb.Append(Number(e.IssuePressure[Issues.All[k]])).Append(',');
                }

                sb.Append('|');

                for (int j = 0; j < e.Effects.Count; j++)
                {
                    TimelineEventEffect fx = e.Effects[j];
                    sb.Append(fx.EffectId).Append(':')
                      .Append(fx.Scope).Append(':')
                      .Append(Number(fx.Magnitude)).Append(':')
                      .Append(fx.DurationMonths).Append(':')
                      .Append(fx.DistrictId ?? "null").Append(';');
                }

                sb.Append('\n');
            }

            sb.Append("--errors--\n").Append(Describe(result));

            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
            }
        }
    }
}
