using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Stories;
using Agora.Core.Tuning;
using Agora.Mod.Sensors;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Synthetic fixtures for the story engine — snapshots, events, pools and stories built by hand
    /// rather than recorded.
    ///
    /// <para>
    /// <c>tests/CLAUDE.md</c> asks for synthetic <see cref="CitySnapshot"/>s over recorded ones, and
    /// the story lanes are the sharpest case for it: a recorded snapshot answers "what did the city
    /// look like" when the question every test below actually asks is "what does one number do to one
    /// trigger". A builder with one named parameter per fixture makes the difference between two
    /// cases a single line of the diff.
    /// </para>
    ///
    /// <para>
    /// <b>Metric ids come from <see cref="MetricHistory"/>, never typed as literals.</b> That file is
    /// the vocabulary's one implementation, it is compile-linked into this project, and a test that
    /// spelled <c>"happiness"</c> by hand would keep passing through a rename that broke every save.
    /// </para>
    ///
    /// <para>
    /// <b>Tuning is read, never memorised.</b> Every number these fixtures need comes off
    /// <see cref="EngineTuning.Default"/> or off a variant built with <see cref="Tuned"/>; a literal
    /// <c>0.25</c> or <c>2</c> here would go red on the next balance pass for a reason that has
    /// nothing to do with what the test guards.
    /// </para>
    /// </summary>
    internal static class StoryTestFixtures
    {
        internal static readonly Guid Save = new Guid("5b1e6f30-2c44-4a9e-8f61-73d0c9a1e224");
        internal static readonly Guid OtherSave = new Guid("aa11bb22-cc33-dd44-ee55-ff6677889900");
        internal static readonly SimDate March1994 = new SimDate(1994, 3, 1);

        // --- tuning ---------------------------------------------------------------------------

        /// <summary>
        /// <see cref="EngineTuning.Default"/> with the given JSON overlaid. Every key the document is
        /// silent about keeps its built-in default, which is what makes a one-key overlay a legible
        /// statement of the single thing a test is varying.
        /// </summary>
        internal static EngineTuning Tuned(string json) => EngineTuning.FromJson(json);

        internal static StoriesTuning Stories => EngineTuning.Default.Stories;

        internal static PowerTuning Power => EngineTuning.Default.Power;

        // --- snapshots ------------------------------------------------------------------------

        /// <summary>
        /// A city with nothing in it but the fields a fixture names. Every default here is a value no
        /// threshold in this file compares against, so a test that forgets to set what it cares about
        /// fails rather than passing on a coincidence.
        /// </summary>
        internal static CitySnapshot City(SimDate date,
                                          double happiness = 50.0,
                                          double unemployment = 0.10,
                                          double crimeRate = 0.10,
                                          int population = 10000,
                                          int homeless = 0,
                                          IEnumerable<string>? unlockedFeatureIds = null,
                                          IEnumerable<string>? activePolicyIds = null,
                                          IEnumerable<DistrictSnapshot>? districts = null)
        {
            var snapshot = new CitySnapshot
            {
                Date = date,
                Happiness = happiness,
                Unemployment = unemployment,
                CrimeRate = crimeRate,
                Population = population,
                Statistics = new CityStatistics(homeless, 0.0, 0, 0, 0, 0, 0, 0.0),
                Tourism = new TourismLevels(0, 0, 0, 0),
                Progression = new ProgressionState(0, 0, 0.0)
            };

            if (unlockedFeatureIds != null) snapshot.UnlockedFeatureIds = new List<string>(unlockedFeatureIds);
            if (activePolicyIds != null) snapshot.ActivePolicyIds = new List<string>(activePolicyIds);
            if (districts != null) snapshot.Districts = new List<DistrictSnapshot>(districts);

            return snapshot;
        }

        /// <summary>
        /// One district. <paramref name="fellBackOn"/> names the fields this district could not
        /// measure for itself — the marker <c>MetricRegistry.ReadDistrict</c> must answer null on,
        /// because a value copied down from the city is not a measurement of the district.
        /// </summary>
        internal static DistrictSnapshot District(string id,
                                                  double uncollectedGarbage = 0.0,
                                                  int attractionCount = 0,
                                                  int signatureBuildingCount = 0,
                                                  double happiness = 50.0,
                                                  params string[] fellBackOn)
        {
            var district = new DistrictSnapshot
            {
                Id = id,
                Name = id,
                Population = 1000,
                Happiness = happiness,
                UncollectedGarbage = uncollectedGarbage,
                AttractionCount = attractionCount,
                SignatureBuildingCount = signatureBuildingCount
            };

            if (fellBackOn != null && fellBackOn.Length > 0)
            {
                district.CityFallbackFields = new List<string>(fellBackOn);
                district.CityFallbackFields.Sort(StringComparer.Ordinal);
                district.HasCityFallbacks = true;
            }

            return district;
        }

        // --- read contexts --------------------------------------------------------------------

        internal static StoryReadContext Context(CitySnapshot today, params CitySnapshot[] history)
        {
            return new StoryReadContext
            {
                Today = today,
                History = new List<CitySnapshot>(history ?? new CitySnapshot[0])
            };
        }

        internal static StoryReadContext WithEvidence(StoryReadContext context,
                                                      params MetricReading[] evidence)
        {
            var readings = new List<MetricReading>(evidence);
            readings.Sort(CompareReadings);

            return new StoryReadContext
            {
                Today = context.Today,
                History = context.History,
                RecordedEvidence = readings
            };
        }

        /// <summary>
        /// A reading is identified by metric <b>and</b> district together, so the sort is over both.
        /// Ordering on the metric alone would leave two districts' readings of one metric in whatever
        /// order they happened to be appended.
        /// </summary>
        internal static int CompareReadings(MetricReading a, MetricReading b)
        {
            int byMetric = string.CompareOrdinal(a.MetricId, b.MetricId);
            return byMetric != 0 ? byMetric : string.CompareOrdinal(a.DistrictId, b.DistrictId);
        }

        /// <summary>
        /// One recorded reading. <paramref name="districtId"/> is empty for a city-wide one, and it is
        /// <b>part of the identity</b> — a lookup matching on the metric alone would let one
        /// district's record answer for another's, which is worse than having no record at all.
        /// </summary>
        internal static MetricReading Reading(string metricId, double? value, string districtId = "") =>
            new MetricReading { MetricId = metricId, DistrictId = districtId, Value = value };

        // --- specs ----------------------------------------------------------------------------

        internal static TriggerSpec Metric(string metricId, Comparison comparison, double threshold,
                                           TriggerScope scope = TriggerScope.City)
        {
            return new TriggerSpec
            {
                Kind = TriggerKind.Metric,
                MetricId = metricId,
                Comparison = comparison,
                Threshold = threshold,
                Scope = scope
            };
        }

        internal static TriggerSpec Delta(string metricId, Comparison comparison, double threshold,
                                          int windowMonths, TriggerScope scope = TriggerScope.City)
        {
            return new TriggerSpec
            {
                Kind = TriggerKind.Delta,
                MetricId = metricId,
                Comparison = comparison,
                Threshold = threshold,
                WindowMonths = windowMonths,
                Scope = scope
            };
        }

        internal static TriggerSpec OfKind(TriggerKind kind, string metricId)
        {
            return new TriggerSpec { Kind = kind, MetricId = metricId };
        }

        internal static CheckSpec Check(TriggerSpec spec, bool relativeToBaseline = false) =>
            new CheckSpec { Spec = spec, RelativeToBaseline = relativeToBaseline };

        // --- events ---------------------------------------------------------------------------

        /// <summary>
        /// An authored event with a <see cref="TriggerKind.Manual"/> trigger unless a fixture supplies
        /// one.
        /// </summary>
        /// <remarks>
        /// <b>Manual is the deliberate default, and it is what decouples the drafting tests from lane
        /// 2a.</b> A manual trigger never fires from the city, so a catalog built out of these adds
        /// nothing to the pool on a refresh — which lets a draft test state its own pool outright and
        /// assert about the draw rather than about the evaluator.
        /// </remarks>
        internal static CivicEvent Event(string id, int severity, TriggerSpec? trigger = null,
                                         CheckSpec? check = null, string? name = null)
        {
            return new CivicEvent
            {
                Id = id,
                Severity = severity,
                Region = EventRegion.Global,
                Trigger = trigger ?? OfKind(TriggerKind.Manual, ""),
                Check = check ?? Check(OfKind(TriggerKind.Manual, "")),
                Name = name ?? ("Event " + id)
            };
        }

        /// <summary>A minor event, at a severity below <c>stories.majorSeverityThreshold</c>.</summary>
        internal static CivicEvent Minor(string id, CheckSpec? check = null) =>
            Event(id, Math.Max(1, Stories.MajorSeverityThreshold - 1), check: check);

        /// <summary>A major event: at the major threshold but below the mandatory one.</summary>
        internal static CivicEvent Major(string id, CheckSpec? check = null) =>
            Event(id, Stories.MajorSeverityThreshold, check: check);

        /// <summary>A mandatory event, at the mandatory threshold.</summary>
        internal static CivicEvent Mandatory(string id, CheckSpec? check = null) =>
            Event(id, Stories.MandatorySeverityThreshold, check: check);

        // --- pools and state ------------------------------------------------------------------

        internal static EventPoolEntry Pooled(string eventId, int missStreak = 0, SimDate first = default)
        {
            return new EventPoolEntry
            {
                EventId = eventId,
                FirstTriggeredDate = first.TotalMonths == 0 ? March1994 : first,
                MissStreak = missStreak
            };
        }

        /// <summary>
        /// A political state carrying nothing but the story fields. The pool is stated outright rather
        /// than triggered into existence, for the reason given on <see cref="Event"/>.
        /// </summary>
        internal static PoliticalState State(SimDate date, params EventPoolEntry[] pool)
        {
            var state = new PoliticalState
            {
                SaveGuid = Save,
                Date = date,
                LastCompletedTickMonth = date.TotalMonths - 1
            };

            state.EventPool = new List<EventPoolEntry>(pool ?? new EventPoolEntry[0]);
            state.EventPool.Sort((a, b) => string.CompareOrdinal(a.EventId, b.EventId));
            return state;
        }

        // --- stories --------------------------------------------------------------------------

        /// <summary>
        /// A story whose slots are given directly. The slots arrive in the contract's declared order —
        /// major first, then <c>EventId</c> ordinal — because a resolution test that fed them in some
        /// other order would be asserting about a story the assembler could never have produced.
        /// </summary>
        internal static Story Story(string id, SimDate opened, params StorySlot[] slots)
        {
            var ordered = new List<StorySlot>(slots ?? new StorySlot[0]);
            ordered.Sort((a, b) =>
            {
                if (a.Role != b.Role) return b.Role.CompareTo(a.Role); // Major (1) before Minor (0)
                return string.CompareOrdinal(a.EventId, b.EventId);
            });

            return new Agora.Core.Stories.Story
            {
                Id = id,
                OpenedDate = opened,
                // CycleMonths is the PERIOD, so the draft-to-resolution gap is one month less: a
                // cycle of 2 drafts on M, resolves on M+1 and drafts again at M+2. The worked example
                // on StoriesTuning.CycleMonths is the authority, and its summary used to disagree with
                // it by exactly this one month.
                ResolvesDate = opened.AddMonths(Stories.CycleMonths - 1),
                Slots = ordered
            };
        }

        internal static StorySlot Slot(string eventId, SlotResponse response,
                                       SlotRole role = SlotRole.Minor,
                                       bool manualDeclared = false,
                                       double? baseline = null)
        {
            return new StorySlot
            {
                EventId = eventId,
                Role = role,
                Response = response,
                ManualDeclared = manualDeclared,
                BaselineMetric = baseline
            };
        }

        /// <summary>
        /// A slot that must resolve <see cref="SlotOutcome.Met"/> whatever the city says:
        /// <c>PowerOverride</c> is an automatic success the player has already paid for.
        /// </summary>
        internal static StorySlot MetSlot(string eventId, SlotRole role = SlotRole.Minor) =>
            Slot(eventId, SlotResponse.PowerOverride, role);

        /// <summary>
        /// A slot that must resolve <see cref="SlotOutcome.NotMet"/>: <c>Ignore</c> is an automatic
        /// failure, because the player decided.
        /// </summary>
        internal static StorySlot NotMetSlot(string eventId, SlotRole role = SlotRole.Minor) =>
            Slot(eventId, SlotResponse.Ignore, role);

        /// <summary>
        /// A slot the player left alone. <b>This now scores <see cref="SlotOutcome.NotMet"/></b> — see
        /// the remarks on <see cref="SlotResponse"/>. It used to be neutral, which made doing nothing
        /// strictly cheaper than every response that could fail.
        /// </summary>
        internal static StorySlot SilentSlot(string eventId, SlotRole role = SlotRole.Minor) =>
            Slot(eventId, SlotResponse.Unaddressed, role);
    }
}
