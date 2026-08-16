using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Stories;
using Agora.Core.Stories.Catalog;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The generic wrapper: what a timeline event becomes when the adaptation policy says
    /// <c>generic</c>, and what it must never become.
    /// </summary>
    /// <remarks>
    /// The mapping assertions are the small half. The half worth reading is
    /// <see cref="Wrapped_CarriesAManualTriggerAndNeverAMetricOne"/> and
    /// <see cref="Wrapped_TierStillComesFromSeverity"/> — a wrapped event is introduced by the timeline
    /// firing, so it must not be pooled and drafted the way an authored civic event is, and it must
    /// still take its tier from severity like every other event.
    /// </remarks>
    public class TimelineEventAdapterTests
    {
        // ------------------------------------------------------------------ fixtures

        /// <summary>Field separator for the canonical form. No field the adapter writes can contain a newline.</summary>
        private const char Separator = '\n';

        private static EngineTuning Tuning() => EngineTuning.Default;

        private static TimelineEvent SampleEvent()
        {
            return new TimelineEvent
            {
                SchemaVersion = 1,
                Id = "energy-price-shock-2022",
                Date = new SimDate(2022, 8, 26),
                Region = EventRegion.Eu,
                Origin = EventOrigin.Catalog,
                Title = "European gas cut drives a global energy price shock",
                Severity = 4,
                DurationMonths = 12,
                HeadlineBrief = "Pipeline gas to Europe is cut and benchmark prices peak.",
                Tags = new List<string> { "subsidy", "energy", "industry", "energy" },
                IssuePressure = IssuePosition.Centre
                    .With(Issue.CostOfLiving, 0.6)
                    .With(Issue.Environment, -0.2)
            };
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Agora.sln"))) return dir.FullName;
                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate the repository root (no Agora.sln above " + AppContext.BaseDirectory + ").");
        }

        private static string ShippedPolicyJson() =>
            File.ReadAllText(Path.Combine(RepoRoot(), "data", "timeline_adaptation.json"));

        private static string PolicyDocument(string entries) =>
            "{ \"schemaVersion\": 1, \"defaultPolicy\": \"generic\", \"policies\": [" + entries + "] }";

        /// <summary>
        /// Every field of a civic event, flattened into one string. Two adaptations of the same event
        /// must produce the same text — the canonical shape of the determinism assertion.
        /// </summary>
        private static string Canonical(CivicEvent civic)
        {
            var sb = new StringBuilder();
            sb.Append(civic.Id).Append(Separator);
            sb.Append(civic.Severity.ToString(CultureInfo.InvariantCulture)).Append(Separator);
            sb.Append(((int)civic.Region).ToString(CultureInfo.InvariantCulture)).Append(Separator);
            AppendSpec(sb, civic.Trigger);
            AppendSpec(sb, civic.Check.Spec);
            sb.Append(civic.Check.RelativeToBaseline ? "1" : "0").Append(Separator);
            AppendList(sb, civic.ActiveEffects);
            AppendList(sb, civic.SuccessEffects);
            AppendList(sb, civic.FailureEffects);
            AppendPressure(sb, civic.ActivePressure);
            AppendPressure(sb, civic.SuccessPressure);
            AppendPressure(sb, civic.FailurePressure);
            AppendList(sb, civic.DistrictAffinity);
            AppendList(sb, civic.Tags);
            sb.Append(civic.Name).Append(Separator);
            sb.Append(civic.Description).Append(Separator);
            sb.Append(civic.IgnoreText).Append(Separator);
            sb.Append(civic.GoalText).Append(Separator);
            sb.Append(civic.PowerOverrideText).Append(Separator);
            sb.Append(civic.SuccessText).Append(Separator);
            sb.Append(civic.FailText).Append(Separator);
            return sb.ToString();
        }

        private static void AppendSpec(StringBuilder sb, TriggerSpec spec)
        {
            sb.Append(((int)spec.Kind).ToString(CultureInfo.InvariantCulture)).Append('|');
            sb.Append(spec.MetricId).Append('|');
            sb.Append(((int)spec.Comparison).ToString(CultureInfo.InvariantCulture)).Append('|');
            sb.Append(spec.Threshold.ToString("R", CultureInfo.InvariantCulture)).Append('|');
            sb.Append(spec.WindowMonths.ToString(CultureInfo.InvariantCulture)).Append('|');
            sb.Append(((int)spec.Scope).ToString(CultureInfo.InvariantCulture)).Append(Separator);
        }

        private static void AppendList(StringBuilder sb, List<string> values)
        {
            for (int i = 0; i < values.Count; i++) sb.Append(values[i]).Append('|');
            sb.Append(Separator);
        }

        private static void AppendPressure(StringBuilder sb, IssuePosition pressure)
        {
            for (int i = 0; i < Issues.All.Count; i++)
            {
                sb.Append(pressure[Issues.All[i]].ToString("R", CultureInfo.InvariantCulture)).Append('|');
            }
            sb.Append(Separator);
        }

        // ------------------------------------------------------------------ the mapping

        /// <summary>
        /// Name from title, description from brief, and severity, region and tags carried across
        /// unchanged. Tags come back sorted ordinal and de-duplicated, the order
        /// <see cref="CivicEvent.Tags"/> declares.
        /// </summary>
        [Fact]
        public void Wrapped_TakesItsNameBriefSeverityRegionAndTagsFromTheTimelineEvent()
        {
            TimelineEvent source = SampleEvent();
            CivicEvent civic = TimelineEventAdapter.Wrap(source, Tuning());

            Assert.Equal(source.Title, civic.Name);
            Assert.Equal(source.HeadlineBrief, civic.Description);
            Assert.Equal(source.Severity, civic.Severity);
            Assert.Equal(source.Region, civic.Region);
            Assert.Equal(new[] { "energy", "industry", "subsidy" }, civic.Tags);
        }

        /// <summary>The adapted id namespaces the timeline id, and the inverse recovers it.</summary>
        [Fact]
        public void Wrapped_IdIsThePrefixedTimelineId()
        {
            CivicEvent civic = TimelineEventAdapter.Wrap(SampleEvent(), Tuning());

            Assert.Equal(TimelineEventAdapter.AdaptedIdPrefix + "energy-price-shock-2022", civic.Id);
            Assert.Equal("energy-price-shock-2022", TimelineEventAdapter.TimelineIdOf(civic.Id));
            Assert.Equal("", TimelineEventAdapter.TimelineIdOf("glob-housing-squeeze"));
        }

        /// <summary>
        /// <b>The invariant this class exists for.</b> A timeline event is introduced by the timeline
        /// firing, not by a metric crossing a threshold, so the wrapper carries
        /// <see cref="TriggerKind.Manual"/> — the kind that never fires from the city and is never a
        /// pool member. Any reading-shaped kind would let a wrapped event be drafted in a month when
        /// the historical event is not live, which is a different event wearing the same prose.
        /// </summary>
        [Fact]
        public void Wrapped_CarriesAManualTriggerAndNeverAMetricOne()
        {
            CivicEvent civic = TimelineEventAdapter.Wrap(SampleEvent(), Tuning());

            Assert.Equal(TriggerKind.Manual, civic.Trigger.Kind);
            Assert.Equal("", civic.Trigger.MetricId);
            Assert.Equal(0, civic.Trigger.WindowMonths);
        }

        /// <summary>
        /// Manual is a trigger kind; mandatory is a tier. The wrapper sets the first and derives
        /// nothing about the second — the tier still comes from severity through
        /// <see cref="StoryTiers"/>, exactly as it does for an authored event.
        /// </summary>
        [Fact]
        public void Wrapped_TierStillComesFromSeverity()
        {
            EngineTuning tuning = Tuning();
            int mandatory = tuning.Stories.MandatorySeverityThreshold;
            int major = tuning.Stories.MajorSeverityThreshold;

            TimelineEvent source = SampleEvent();

            source.Severity = 5;
            Assert.Equal(StoryTier.Mandatory,
                TimelineEventAdapter.Wrap(source, tuning).TierUnder(mandatory, major));

            source.Severity = 4;
            Assert.Equal(StoryTier.Major,
                TimelineEventAdapter.Wrap(source, tuning).TierUnder(mandatory, major));

            source.Severity = 1;
            Assert.Equal(StoryTier.Minor,
                TimelineEventAdapter.Wrap(source, tuning).TierUnder(mandatory, major));
        }

        /// <summary>
        /// The check is a city happiness reading relative to the baseline captured when the story
        /// opened, and severity sets the demand in <b>happiness points on the 0–100 scale</b>: the full
        /// <c>stories.wrappedEventHappinessGoalPoints</c> at severity 1, falling linearly to a
        /// <b>nonzero floor</b> at <c>catalog.severityMax</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The five thresholds are asserted as <b>hard numbers</b> rather than by restating the
        /// formula. Restating it would have passed just as happily on the version this replaced, whose
        /// entire severity spread was 0.8 points out of 100 — narrower than the month-to-month drift of
        /// a population mean, and therefore not a curve at all. A literal here is what makes the unit
        /// error visible if it ever comes back.
        /// </para>
        /// <para>
        /// <b>The floor is the assertion that matters most.</b> Severity <c>severityMax</c> is the
        /// Mandatory tier, and a demand of exactly +0.0 under a <c>gte</c> comparison settles the
        /// highest-stakes story class on whether a population mean happened to drift — an exactly flat
        /// city passes. Zero at the top is a defect, not a tuning taste.
        /// </para>
        /// </remarks>
        [Fact]
        public void Wrapped_CheckDemandIsInHappinessPointsAndFallsToANonzeroFloor()
        {
            EngineTuning tuning = Tuning();
            Assert.Equal(2.0, tuning.Stories.WrappedEventHappinessGoalPoints, 12);
            Assert.Equal(5, tuning.Catalog.SeverityMax);

            TimelineEvent source = SampleEvent();

            source.Severity = 1;
            CheckSpec minor = TimelineEventAdapter.Wrap(source, tuning).Check;

            Assert.True(minor.RelativeToBaseline);
            Assert.Equal(TriggerKind.Metric, minor.Spec.Kind);
            Assert.Equal(MetricRegistry.Happiness, minor.Spec.MetricId);
            Assert.Equal(TriggerScope.City, minor.Spec.Scope);
            Assert.Equal(Comparison.GreaterThanOrEqual, minor.Spec.Comparison);

            // 2.0, 1.6, 1.2, 0.8, 0.4 points of happiness, at the shipped tuning.
            double[] expected = { 2.0, 1.6, 1.2, 0.8, 0.4 };
            for (int severity = 1; severity <= 5; severity++)
            {
                source.Severity = severity;
                Assert.Equal(expected[severity - 1],
                             TimelineEventAdapter.Wrap(source, tuning).Check.Spec.Threshold, 12);
            }

            // Nothing on the curve asks for nothing, and every step down is a real step.
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.True(expected[i] > 0.0, "no severity may ask for a zero gain");
                if (i > 0) Assert.True(expected[i - 1] - expected[i] > 0.0, "the curve must be strict");
            }

            // The severity-1 demand is anchored to a happiness delta the engine already spends, which
            // is the calibration stories.wrappedEventHappinessGoalPoints states for itself.
            //
            // This replaces an earlier assertion on the SPREAD, which the nonzero-floor ruling makes
            // arithmetically unsatisfiable: with the top pinned to goalPoints and the bottom held above
            // zero, the spread is necessarily under goalPoints and can never reach the 2.0 bonus.
            Assert.True(expected[0] >= tuning.Mandates.FulfilledHappinessBonus,
                "a severity-1 wrapped goal must ask for at least what fulfilling a mandate pays, or " +
                "the whole curve sits in the noise");
        }

        /// <summary>
        /// A one-point severity scale has no range to fall across, so the single tier gets the full
        /// demand — and in particular not a division by zero and not nothing.
        /// </summary>
        [Fact]
        public void Wrapped_CheckSurvivesASeverityScaleWithOnePoint()
        {
            EngineTuning tuning = EngineTuning.FromJson(
                "{ \"catalog\": { \"severityMax\": 1 }, " +
                "\"stories\": { \"wrappedEventHappinessGoalPoints\": 2.0 } }");
            Assert.Equal(1, tuning.Catalog.SeverityMax);

            TimelineEvent source = SampleEvent();
            source.Severity = 4; // clamped to the one point the scale has

            CivicEvent civic = TimelineEventAdapter.Wrap(source, tuning);
            Assert.Equal(1, civic.Severity);
            Assert.Equal(2.0, civic.Check.Spec.Threshold, 12);
        }

        /// <summary>
        /// The two rules the spine added after this class was written do not reach it — checked rather
        /// than assumed.
        /// </summary>
        /// <remarks>
        /// <c>CheckWindowOutrunsStoryLife</c> inspects only a <c>delta</c> check, and this one is a
        /// plain <c>metric</c> reading with a zero window. <c>ThresholdAboveAttainableMaximum</c> bites
        /// only the metrics whose sensor cannot reach their nominal ceiling — the two channel means —
        /// and happiness is not one of them.
        /// </remarks>
        [Fact]
        public void Wrapped_CheckIsUntouchedByTheTwoNewCatalogRules()
        {
            CheckSpec check = TimelineEventAdapter.Wrap(SampleEvent(), Tuning()).Check;

            Assert.NotEqual(TriggerKind.Delta, check.Spec.Kind);
            Assert.Equal(0, check.Spec.WindowMonths);
            Assert.Null(CivicEventCatalogLoader.AttainableMaximum(check.Spec.MetricId));
        }

        /// <summary>
        /// No effect ids, on purpose. A timeline event's own effects are requested by the timeline
        /// scheduler when it fires; copying them here would apply one historical event's capped
        /// magnitude twice.
        /// </summary>
        [Fact]
        public void Wrapped_RequestsNoEffectsOfItsOwn()
        {
            CivicEvent civic = TimelineEventAdapter.Wrap(SampleEvent(), Tuning());

            Assert.Empty(civic.ActiveEffects);
            Assert.Empty(civic.SuccessEffects);
            Assert.Empty(civic.FailureEffects);
        }

        /// <summary>
        /// Pressure is salience and never credit: all three positions carry the timeline event's own
        /// authored pressure, pointing the same way, and no outcome flips a sign.
        /// </summary>
        /// <remarks>
        /// A mirror-negated success would not release the issue. <c>AffinityEngine.EventTerm</c>
        /// dot-products the position against each party's platform, so negating it moves voters to the
        /// opposite pole and rewards the party that was against fixing the thing — see the owner ruling
        /// on <c>CivicEvent.ActivePressure</c>. Government credit is derived from the slot outcome by
        /// the story cycle's own weights, not authored into a position that has no idea who governs.
        /// </remarks>
        [Fact]
        public void Wrapped_PressureIsSalienceAndNeverFlipsSign()
        {
            TimelineEvent source = SampleEvent();
            CivicEvent civic = TimelineEventAdapter.Wrap(source, Tuning());

            for (int i = 0; i < Issues.All.Count; i++)
            {
                Issue issue = Issues.All[i];
                Assert.Equal(source.IssuePressure[issue], civic.ActivePressure[issue], 12);
                Assert.Equal(source.IssuePressure[issue], civic.SuccessPressure[issue], 12);
                Assert.Equal(source.IssuePressure[issue], civic.FailurePressure[issue], 12);

                Assert.False(civic.ActivePressure[issue] * civic.SuccessPressure[issue] < 0.0,
                    "successPressure flips the sign of activePressure on " + Issues.ToKey(issue));
                Assert.False(civic.ActivePressure[issue] * civic.FailurePressure[issue] < 0.0,
                    "failurePressure flips the sign of activePressure on " + Issues.ToKey(issue));
            }
        }

        /// <summary>An out-of-range authored pressure is clamped, like every other pressure producer.</summary>
        [Fact]
        public void Wrapped_PressureIsClamped()
        {
            TimelineEvent source = SampleEvent();
            source.IssuePressure = IssuePosition.Centre
                .With(Issue.CostOfLiving, 4.0)
                .With(Issue.Environment, -9.0);

            CivicEvent civic = TimelineEventAdapter.Wrap(source, Tuning());

            Assert.Equal(1.0, civic.ActivePressure[Issue.CostOfLiving], 12);
            Assert.Equal(-1.0, civic.FailurePressure[Issue.Environment], 12);
        }

        /// <summary>
        /// <b>The shipped catalogs make every adapted event politically inert, and that is recorded
        /// here rather than left to be discovered.</b> No timeline event authors an
        /// <c>issuePressure</c>, so a wrapped event presses no issue and — by the double-application
        /// decision — requests no effect either.
        /// </summary>
        /// <remarks>
        /// AGORA-WAVE4(timeline issuePressure). The repair is an authoring pass over
        /// <c>timeline_*.json</c>, frozen this wave, and the mapping already picks the numbers up the
        /// moment they exist. <b>This test is written to fail when that lands</b>: the assertion is
        /// that the catalogs contain no <c>issuePressure</c> at all, so adding one turns the reminder
        /// red instead of leaving it quietly true forever.
        /// </remarks>
        [Fact]
        public void ShippedTimelineEvents_AuthorNoPressure_SoWrappedEventsAreInertForNow()
        {
            string[] files = { "timeline_global.json", "timeline_eu.json", "timeline_na.json" };
            foreach (string file in files)
            {
                string json = File.ReadAllText(Path.Combine(RepoRoot(), "data", file));
                Assert.DoesNotContain("issuePressure", json, StringComparison.Ordinal);
            }

            TimelineEvent source = SampleEvent();
            source.IssuePressure = IssuePosition.Centre; // what every shipped event actually carries
            CivicEvent civic = TimelineEventAdapter.Wrap(source, Tuning());

            for (int i = 0; i < Issues.All.Count; i++)
            {
                Assert.Equal(0.0, civic.ActivePressure[Issues.All[i]], 12);
                Assert.Equal(0.0, civic.SuccessPressure[Issues.All[i]], 12);
                Assert.Equal(0.0, civic.FailurePressure[Issues.All[i]], 12);
            }

            Assert.Empty(civic.ActiveEffects);
        }

        /// <summary>Severity outside the tuned range is clamped rather than carried through.</summary>
        [Fact]
        public void Wrapped_SeverityIsClampedToTheTunedRange()
        {
            EngineTuning tuning = Tuning();
            TimelineEvent source = SampleEvent();

            source.Severity = 0;
            Assert.Equal(1, TimelineEventAdapter.Wrap(source, tuning).Severity);

            source.Severity = tuning.Catalog.SeverityMax + 3;
            Assert.Equal(tuning.Catalog.SeverityMax, TimelineEventAdapter.Wrap(source, tuning).Severity);
        }

        // ------------------------------------------------------------------ the civic event contract

        /// <summary>
        /// An adapted event must satisfy what the civic event contract asks of an authored one: all
        /// seven prose fields present, a check the metric registry can actually read at its scope and
        /// which is not census-gated, sorted tags, a kebab-case id and a severity in range.
        /// </summary>
        /// <remarks>
        /// Re-derived here rather than by handing the wrapper to <c>CivicEventCatalogLoader</c>: an
        /// adapted event never passes through the loader, so the rules it would have been held to have
        /// to be asserted against the object.
        /// </remarks>
        [Fact]
        public void Wrapped_SatisfiesTheCivicEventContract()
        {
            EngineTuning tuning = Tuning();
            CivicEvent civic = TimelineEventAdapter.Wrap(SampleEvent(), tuning);

            Assert.False(string.IsNullOrWhiteSpace(civic.Name));
            Assert.False(string.IsNullOrWhiteSpace(civic.Description));
            Assert.False(string.IsNullOrWhiteSpace(civic.IgnoreText));
            Assert.False(string.IsNullOrWhiteSpace(civic.GoalText));
            Assert.False(string.IsNullOrWhiteSpace(civic.PowerOverrideText));
            Assert.False(string.IsNullOrWhiteSpace(civic.SuccessText));
            Assert.False(string.IsNullOrWhiteSpace(civic.FailText));

            Assert.True(MetricRegistry.IsKnown(civic.Check.Spec.MetricId, civic.Check.Spec.Scope));
            Assert.DoesNotContain(civic.Check.Spec.MetricId, CivicEventCatalogLoader.CensusGatedMetricIds);

            Assert.InRange(civic.Severity, 1, tuning.Catalog.SeverityMax);
            Assert.True(IsKebabCase(civic.Id), "'" + civic.Id + "' must be lowercase kebab-case");

            for (int i = 1; i < civic.Tags.Count; i++)
            {
                Assert.True(string.CompareOrdinal(civic.Tags[i - 1], civic.Tags[i]) < 0,
                    "tags must be sorted ordinal and unique");
            }
        }

        private static bool IsKebabCase(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')) return false;
            }

            return true;
        }

        // ------------------------------------------------------------------ policy and determinism

        /// <summary>An event marked <c>none</c> becomes nothing at all — the plan's "drop", reversibly.</summary>
        [Fact]
        public void Adapt_DropsANoneEvent()
        {
            TimelineAdaptationPolicy policy;
            Assert.True(TimelineAdaptationPolicy.TryParse(
                PolicyDocument("{ \"timelineEventId\": \"energy-price-shock-2022\", \"policy\": \"none\", " +
                               "\"reason\": \"fixture\" }"), out policy));

            Assert.Equal(TimelineAdaptationKind.None, policy.KindFor("energy-price-shock-2022"));

            AdaptationOutcome outcome = new TimelineEventAdapter(policy).Adapt(SampleEvent(), Tuning());
            Assert.Equal(AdaptationOutcomeKind.Dropped, outcome.Kind);
            Assert.False(outcome.HasCivicEvent);
            Assert.Equal("", outcome.AuthoredCivicEventId);
        }

        /// <summary>
        /// An <c>authored</c> event produces no wrapper either — but it is a <i>different answer</i>,
        /// and the outcome says so and names the civic event that takes over.
        /// </summary>
        /// <remarks>
        /// This is the case a bare null would have destroyed: a caller reading "no civic event" as
        /// "drop it" would make every authored event vanish silently.
        /// </remarks>
        [Fact]
        public void Adapt_DefersAnAuthoredEventAndNamesTheCivicEvent()
        {
            TimelineAdaptationPolicy policy;
            Assert.True(TimelineAdaptationPolicy.TryParse(
                PolicyDocument("{ \"timelineEventId\": \"energy-price-shock-2022\", \"policy\": \"authored\", " +
                               "\"civicEventId\": \"eu-heating-bills\" }"), out policy));

            AdaptationOutcome outcome = new TimelineEventAdapter(policy).Adapt(SampleEvent(), Tuning());

            Assert.Equal(AdaptationOutcomeKind.Authored, outcome.Kind);
            Assert.False(outcome.HasCivicEvent);
            Assert.Equal("eu-heating-bills", outcome.AuthoredCivicEventId);
            Assert.Equal("eu-heating-bills", policy.AuthoredCivicEventIdFor("energy-price-shock-2022"));
        }

        /// <summary>An unnamed event takes the default, which is <c>generic</c>: it gets wrapped.</summary>
        [Fact]
        public void Adapt_WrapsAnEventTheFileDoesNotName()
        {
            TimelineAdaptationPolicy policy;
            Assert.True(TimelineAdaptationPolicy.TryParse(
                PolicyDocument("{ \"timelineEventId\": \"some-other-event\", \"policy\": \"none\" }"), out policy));

            Assert.Equal(TimelineAdaptationKind.Generic, policy.KindFor("energy-price-shock-2022"));

            AdaptationOutcome outcome = new TimelineEventAdapter(policy).Adapt(SampleEvent(), Tuning());
            Assert.Equal(AdaptationOutcomeKind.Wrapped, outcome.Kind);
            Assert.True(outcome.HasCivicEvent);
        }

        /// <summary>
        /// A null event is its own outcome, not a third meaning for "dropped". No policy lookup can
        /// explain it, because there is no id to look up.
        /// </summary>
        [Fact]
        public void Adapt_ReportsANullEventAsItsOwnOutcome()
        {
            AdaptationOutcome outcome = new TimelineEventAdapter().Adapt(null, Tuning());

            Assert.Equal(AdaptationOutcomeKind.NoEvent, outcome.Kind);
            Assert.False(outcome.HasCivicEvent);
        }

        /// <summary>
        /// Same input twice, same output. The adapter reads no clock, draws no random number and
        /// iterates no dictionary, so this is a statement about the whole class rather than about one
        /// field.
        /// </summary>
        [Fact]
        public void Adapt_IsDeterministic()
        {
            EngineTuning tuning = Tuning();
            var adapter = new TimelineEventAdapter();

            AdaptationOutcome first = adapter.Adapt(SampleEvent(), tuning);
            AdaptationOutcome second = adapter.Adapt(SampleEvent(), tuning);

            Assert.True(first.HasCivicEvent);
            Assert.True(second.HasCivicEvent);
            Assert.Equal(Canonical(first.CivicEvent!), Canonical(second.CivicEvent!));

            // And a second adapter built from a second parse of the same document agrees, so nothing
            // depends on the order the policy file was read in.
            TimelineAdaptationPolicy a, b;
            Assert.True(TimelineAdaptationPolicy.TryParse(ShippedPolicyJson(), out a));
            Assert.True(TimelineAdaptationPolicy.TryParse(ShippedPolicyJson(), out b));

            AdaptationOutcome viaA = new TimelineEventAdapter(a).Adapt(SampleEvent(), tuning);
            AdaptationOutcome viaB = new TimelineEventAdapter(b).Adapt(SampleEvent(), tuning);
            Assert.True(viaA.HasCivicEvent);
            Assert.Equal(Canonical(viaA.CivicEvent!), Canonical(viaB.CivicEvent!));
        }

        /// <summary>
        /// A corrupt or wrong-versioned policy document is reported rather than thrown, and degrades to
        /// wrapping everything — the recoverable direction.
        /// </summary>
        [Fact]
        public void Policy_MalformedOrWrongVersion_DegradesToWrapAll()
        {
            TimelineAdaptationPolicy policy;

            Assert.False(TimelineAdaptationPolicy.TryParse("{ not json", out policy));
            Assert.Same(TimelineAdaptationPolicy.WrapAll, policy);

            Assert.False(TimelineAdaptationPolicy.TryParse(
                "{ \"schemaVersion\": 99, \"policies\": [] }", out policy));
            Assert.Equal(TimelineAdaptationKind.Generic, policy.KindFor("anything"));
        }

        /// <summary>
        /// An entry the reader cannot understand is reported rather than skipped in silence. A misspelt
        /// policy falls through to the default — <c>generic</c> — so an event somebody meant to drop
        /// keeps being wrapped while the file claims otherwise, which is the one failure here that
        /// looks exactly like success.
        /// </summary>
        [Fact]
        public void Policy_ReportsEntriesItCannotUnderstand()
        {
            TimelineAdaptationPolicy policy;
            Assert.True(TimelineAdaptationPolicy.TryParse(PolicyDocument(
                "{ \"timelineEventId\": \"energy-price-shock-2022\", \"policy\": \"non\" }, " +
                "{ \"policy\": \"none\" }, " +
                "{ \"timelineEventId\": \"record-heat-2023\", \"policy\": \"authored\" }"), out policy));

            Assert.Equal(3, policy.Diagnostics.Count);
            Assert.Contains(policy.Diagnostics, d => d.Contains("energy-price-shock-2022"));
            Assert.Contains(policy.Diagnostics, d => d.Contains("no timelineEventId"));
            Assert.Contains(policy.Diagnostics, d => d.Contains("names no civicEventId"));

            // And the misread entry really did fall through to the default, which is what makes the
            // diagnostic the only evidence a reader would ever get.
            Assert.Equal(TimelineAdaptationKind.Generic, policy.KindFor("energy-price-shock-2022"));
        }

        /// <summary>
        /// <c>authored</c> is a per-event answer and may never be the default. A file that asks for it
        /// is reported and falls back to <c>generic</c>.
        /// </summary>
        /// <remarks>
        /// The schema's <c>defaultPolicy</c> enum is <c>["none","generic"]</c>, and the reader has to
        /// hold the same line for a hand-edited file: an accepted default of <c>authored</c> would
        /// defer every unnamed event — some ninety of them — to a <c>civicEventId</c> that cannot
        /// exist, because only an entry can carry one. It would also break the outcome's guarantee
        /// that an <c>Authored</c> result names something.
        /// </remarks>
        [Fact]
        public void Policy_RefusesAnAuthoredDefault()
        {
            TimelineAdaptationPolicy policy;
            Assert.True(TimelineAdaptationPolicy.TryParse(
                "{ \"schemaVersion\": 1, \"defaultPolicy\": \"authored\", \"policies\": [] }", out policy));

            Assert.Equal(TimelineAdaptationKind.Generic, policy.DefaultKind);
            Assert.Contains(policy.Diagnostics, d => d.Contains("defaultPolicy"));

            AdaptationOutcome outcome = new TimelineEventAdapter(policy).Adapt(SampleEvent(), Tuning());
            Assert.Equal(AdaptationOutcomeKind.Wrapped, outcome.Kind);

            // "none" is a legitimate default and still parses, so the restriction is to the one kind
            // that cannot work rather than to whatever the file happens to say.
            TimelineAdaptationPolicy dropAll;
            Assert.True(TimelineAdaptationPolicy.TryParse(
                "{ \"schemaVersion\": 1, \"defaultPolicy\": \"none\", \"policies\": [] }", out dropAll));
            Assert.Equal(TimelineAdaptationKind.None, dropAll.DefaultKind);
            Assert.Empty(dropAll.Diagnostics);
        }

        /// <summary>
        /// The zero value of the outcome struct — which nobody constructs but every array of them
        /// starts as — honours the contract: an empty id rather than a null one.
        /// </summary>
        [Fact]
        public void Outcome_DefaultValueCarriesAnEmptyAuthoredId()
        {
            AdaptationOutcome outcome = default(AdaptationOutcome);

            Assert.Equal(AdaptationOutcomeKind.NoEvent, outcome.Kind);
            Assert.False(outcome.HasCivicEvent);
            Assert.Equal("", outcome.AuthoredCivicEventId);
            Assert.Equal(0, outcome.AuthoredCivicEventId.Length); // the NRE this exists to prevent
        }

        /// <summary>
        /// A <c>Wrapped</c> outcome carrying nothing would contradict the only thing its kind asserts,
        /// so the factory refuses it rather than constructing one.
        /// </summary>
        [Fact]
        public void Outcome_WrappedRefusesANullEvent()
        {
            Assert.Throws<ArgumentNullException>(() => AdaptationOutcome.Wrapped(null!));
        }

        // ------------------------------------------------------------------ the shipped policy file

        /// <summary>
        /// The shipped file parses, and every <c>none</c> entry states a reason. "The most boring 25%"
        /// is a judgement, and a judgement with no stated reason cannot be argued with later.
        /// </summary>
        /// <remarks>
        /// That every id names a real timeline event is checked next door, in
        /// <c>ShippedCivicEventCatalogTests.AdaptationPolicy_NamesOnlyEventsThatExist</c>.
        /// </remarks>
        [Fact]
        public void ShippedPolicy_ParsesAndEveryNoneEntryStatesAReason()
        {
            TimelineAdaptationPolicy policy;
            Assert.True(TimelineAdaptationPolicy.TryParse(ShippedPolicyJson(), out policy),
                "data/timeline_adaptation.json must parse at schemaVersion " +
                TimelineAdaptationPolicy.SupportedSchemaVersion.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(TimelineAdaptationKind.Generic, policy.DefaultKind);
            Assert.Empty(policy.Diagnostics);

            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(ShippedPolicyJson());

            int noneCount = 0;
            foreach (System.Text.Json.JsonElement entry in doc.RootElement.GetProperty("policies").EnumerateArray())
            {
                string id = entry.GetProperty("timelineEventId").GetString() ?? "";
                string kind = entry.GetProperty("policy").GetString() ?? "";

                System.Text.Json.JsonElement reason;
                Assert.True(entry.TryGetProperty("reason", out reason) &&
                            !string.IsNullOrWhiteSpace(reason.GetString()),
                    "'" + id + "' is marked " + kind + " with no reason");

                if (string.CompareOrdinal(kind, "none") == 0) noneCount++;

                // No lane may name an authored civic event this wave: the authored events are being
                // written in parallel and naming one would be guessing an id.
                Assert.NotEqual("authored", kind);
            }

            // Roughly the least interesting quarter of the 120 shipped timeline events.
            Assert.InRange(noneCount, 25, 35);
            Assert.Equal(noneCount, CountOf(policy, TimelineAdaptationKind.None));
        }

        private static int CountOf(TimelineAdaptationPolicy policy, TimelineAdaptationKind kind)
        {
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(ShippedPolicyJson());

            int count = 0;
            foreach (System.Text.Json.JsonElement entry in doc.RootElement.GetProperty("policies").EnumerateArray())
            {
                string id = entry.GetProperty("timelineEventId").GetString() ?? "";
                if (policy.KindFor(id) == kind) count++;
            }

            return count;
        }
    }
}
