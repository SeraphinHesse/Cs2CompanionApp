using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Engine.Affinity;
using Agora.Core.Engine.Effects;
using Agora.Core.Stories;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Wave 4c — <see cref="StoryEffects"/> and <see cref="StoryPressure"/>.
    ///
    /// <para>
    /// The two halves guard different things. The effects half guards that the existing palette does
    /// the capping and that <c>stories.maxStoryEffectsPerModifier</c> is actually read; the pressure
    /// half guards the split the whole wave turns on — <i>salience</i> comes off the catalog and never
    /// changes sign, <i>credit</i> is derived and reaches whoever governs and nobody else.
    /// </para>
    ///
    /// <para>
    /// <b>Every expected value is read from tuning or asserted as a shape, never memorised.</b> A test
    /// that pinned a magnitude of 0.15 would go red on the next balance pass for a reason that has
    /// nothing to do with what it guards; "the request never exceeds its declared cap" and "raising
    /// the breadth key admits one more" survive any retune. Effect ids come off the palette rather
    /// than being typed, for the same reason.
    /// </para>
    /// </summary>
    public class StoryEffectsAndPressureTests
    {
        private static readonly EngineTuning Tuning = EngineTuning.Default;
        private static readonly StoriesTuning Stories = EngineTuning.Default.Stories;
        private static readonly EffectPalette Palette = EffectPalette.From(EngineTuning.Default);

        private static readonly Guid Save = new Guid("3f2a91c4-55d6-4f0b-9c1a-7e2b8d4a6f10");
        private static readonly SimDate June2001 = new SimDate(2001, 6, 1);

        /// <summary>The empty half of a call that is only about live stories, or only about resolved ones.</summary>
        private static readonly Story[] NoStories = new Story[0];

        /// <summary>Two registered city effects, which therefore drive two different modifiers.</summary>
        private static string CityEffectA => Palette.CityIds[0];
        private static string CityEffectB => Palette.CityIds[1];

        /// <summary>
        /// A registered district effect. Read off the palette rather than typed, so a retune that
        /// renames one does not leave a test asserting about an id the registry no longer has.
        /// </summary>
        private static string DistrictEffect => Palette.DistrictIds[0];

        /// <summary>
        /// The target lane 4a picks from the tick's snapshot. Every district-scoped request lands here;
        /// <see cref="NoDistrict"/> is the degraded path a city with no districts takes.
        /// </summary>
        private const string District = "downtown";

        private const string NoDistrict = "";

        private static EngineTuning Tuned(string json) => EngineTuning.FromJson(json);

        // --- fixtures -----------------------------------------------------------------------------

        private static IssuePosition Services(double value) => new IssuePosition(value, 0, 0, 0, 0, 0);

        /// <summary>
        /// An authored civic event. The three pressures default to the shape the catalogs are held to:
        /// one direction on one axis, quieter on success and louder on failure.
        /// </summary>
        private static CivicEvent Event(string id, int severity,
                                        IEnumerable<string>? active = null,
                                        IEnumerable<string>? success = null,
                                        IEnumerable<string>? failure = null,
                                        double salience = 0.0)
        {
            return new CivicEvent
            {
                Id = id,
                Severity = severity,
                Name = "Event " + id,
                ActiveEffects = new List<string>(active ?? new string[0]),
                SuccessEffects = new List<string>(success ?? new string[0]),
                FailureEffects = new List<string>(failure ?? new string[0]),
                ActivePressure = Services(salience),
                SuccessPressure = Services(salience / 3.0),
                FailurePressure = Services(salience * 1.5)
            };
        }

        private static StorySlot Slot(string eventId, SlotOutcome outcome = SlotOutcome.Pending,
                                      SlotRole role = SlotRole.Major)
        {
            return new StorySlot { EventId = eventId, Role = role, SlotOutcome = outcome };
        }

        private static Story Story(string id, params StorySlot[] slots)
        {
            return new Story
            {
                Id = id,
                OpenedDate = June2001,
                ResolvesDate = June2001.AddMonths(Stories.CycleMonths - 1),
                Slots = new List<StorySlot>(slots)
            };
        }

        private static List<SlotOutcome> Outcomes(Story story)
        {
            var outcomes = new List<SlotOutcome>();
            for (int i = 0; i < story.Slots.Count; i++) outcomes.Add(story.Slots[i].SlotOutcome);
            return outcomes;
        }

        private static EffectCap CapOf(string effectId)
        {
            EffectCap cap;
            Assert.True(Palette.TryGetCap(effectId, out cap));
            return cap;
        }

        // =========================================================================================
        // StoryEffects
        // =========================================================================================

        /// <summary>
        /// A live slot asks for its event's active list, for as long as the story itself runs. The
        /// duration is read off <c>stories.cycleMonths</c> rather than asserted as a number.
        /// </summary>
        [Fact]
        public void ForActive_RequestsTheActiveListForTheLengthOfTheStory()
        {
            var catalog = new List<CivicEvent> { Event("e1", 3, active: new[] { CityEffectA }) };
            var story = Story("s1", Slot("e1"));

            List<EffectRequest> requests = StoryEffects.ForActive(new[] { story }, catalog, District, Tuning);

            EffectRequest request = Assert.Single(requests);
            Assert.Equal(CityEffectA, request.EffectId);
            Assert.Equal(EffectScope.City, request.Scope);
            Assert.Equal(Stories.CycleMonths, request.DurationMonths);
            Assert.True(request.Magnitude > 0.0);
        }

        /// <summary>
        /// <b>The palette does the capping and nothing here re-derives it.</b> Whatever the phase scale
        /// and severity multiply out to, no request may leave above the entry's own effective cap —
        /// which is the property non-negotiable #5 actually asks for.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(5)]
        public void ForResolution_NeverExceedsTheDeclaredMagnitudeCap(int severity)
        {
            var catalog = new List<CivicEvent> { Event("e1", severity, success: new[] { CityEffectA }) };
            var story = Story("s1", Slot("e1", SlotOutcome.Met));

            List<EffectRequest> requests =
                StoryEffects.ForResolution(story, Outcomes(story), catalog, District, Tuning);

            EffectCap cap = CapOf(CityEffectA);
            EffectRequest request = Assert.Single(requests);

            Assert.True(Math.Abs(request.Magnitude) <= Palette.EffectiveMagnitudeCap(cap));
            Assert.True(request.DurationMonths <= Palette.EffectiveDurationCapMonths(cap));
        }


        /// <summary>
        /// <b>Severity has to survive to the ledger, and the cap test above cannot see whether it
        /// does.</b> That test asserts <c>magnitude &lt;= cap</c> at three severities and passed while
        /// all three clamped to the <i>same</i> number: the resolution scales shipped at 1.00, which is
        /// the full cap, and <c>ScaleForSeverity</c> multiplies by <c>1 + 0.2 × (severity - 1)</c>,
        /// which is at least 1 for every severity — so a severity-1 minor story failing did exactly as
        /// much damage as a severity-5 mandatory catastrophe. The clamp existing and severity moving
        /// anything are two different claims, and only the second one is what the player feels.
        /// </summary>
        /// <remarks>
        /// A strict ordering rather than any value, so it survives a retune. It goes red the moment a
        /// scale is raised back to a figure that leaves no headroom under the cap, which is exactly the
        /// misconfiguration it exists to catch.
        /// </remarks>
        [Fact]
        public void ForResolution_ScalesWithSeverityRatherThanPinningToTheCap()
        {
            var catalog = new List<CivicEvent>
            {
                Event("small", 1, failure: new[] { CityEffectA }),
                Event("large", 5, failure: new[] { CityEffectA })
            };

            var small = Story("s1", Slot("small", SlotOutcome.NotMet));
            var large = Story("s2", Slot("large", SlotOutcome.NotMet));

            double atOne = Assert.Single(
                StoryEffects.ForResolution(small, Outcomes(small), catalog, District, Tuning)).Magnitude;
            double atFive = Assert.Single(
                StoryEffects.ForResolution(large, Outcomes(large), catalog, District, Tuning)).Magnitude;

            Assert.True(atFive > atOne,
                "severity is inert on the resolution path: severity 5 asked for " + atFive
                + " and severity 1 asked for " + atOne + ". stories.failureEffectScale ("
                + Stories.FailureEffectScale + ") leaves no headroom under the magnitude cap, so "
                + "ScaleForSeverity's multiplier is clamped away.");
        }

        /// <summary>
        /// <b>The reason the scales ship below 1.0, as an executable check rather than a comment.</b>
        /// A phase scale is a fraction of the entry's magnitude cap, and
        /// <c>EffectResolver.ScaleForSeverity</c> then multiplies by
        /// <c>1 + effects.severityMagnitudeScale × (severity - 1)</c>. If the scale times that
        /// multiplier reaches 1.0 at a severity below the maximum, every severity from there up clamps
        /// to the same number and the top of the range goes flat. Measured on the old tuning: at 1.00
        /// severities 1 and 5 both produced 0.25.
        /// </summary>
        /// <remarks>
        /// Every term is read from tuning, so this states the constraint rather than the current
        /// answer. It fails on the retune that breaks it rather than on the one that merely moves it.
        /// </remarks>
        [Fact]
        public void ShippedEffectScalesLeaveHeadroomForSeverityUnderTheCap()
        {
            int severityMax = Tuning.Catalog.SeverityMax;
            double topMultiplier = 1.0 + (Tuning.Effects.SeverityMagnitudeScale * (severityMax - 1));

            // The scale one step below the top must still be under the cap, or the last step is flat.
            double oneBelowTop = 1.0 + (Tuning.Effects.SeverityMagnitudeScale * (severityMax - 2));

            Assert.True(Stories.SuccessEffectScale * oneBelowTop < 1.0,
                "stories.successEffectScale " + Stories.SuccessEffectScale + " × " + oneBelowTop
                + " already reaches the magnitude cap at severity " + (severityMax - 1)
                + ", so the top of the severity range is flat.");

            Assert.True(Stories.FailureEffectScale * oneBelowTop < 1.0);
            Assert.True(Stories.ActiveEffectScale * oneBelowTop < 1.0);

            // And the top of the range is meant to reach nearly all of the cap, not a fraction of it:
            // headroom that is too generous wastes the palette's declared range instead of flattening it.
            Assert.True(Stories.SuccessEffectScale * topMultiplier > 0.5);
        }

        /// <summary>
        /// The same claim on the active path, which was always correct — kept beside its sibling so a
        /// future retune that flattens one and not the other is visibly asymmetric rather than half
        /// caught.
        /// </summary>
        [Fact]
        public void ForActive_ScalesWithSeverity()
        {
            var catalog = new List<CivicEvent>
            {
                Event("small", 1, active: new[] { CityEffectA }),
                Event("large", 5, active: new[] { CityEffectA })
            };

            var small = Story("s1", Slot("small"));
            var large = Story("s2", Slot("large"));

            double atOne = Assert.Single(
                StoryEffects.ForActive(new[] { small }, catalog, District, Tuning)).Magnitude;
            double atFive = Assert.Single(
                StoryEffects.ForActive(new[] { large }, catalog, District, Tuning)).Magnitude;

            Assert.True(atFive > atOne);
        }

        /// <summary>
        /// <b>A consequence lasts <c>stories.resolutionEffectMonths</c>, not the entry's ceiling.</b>
        /// A palette entry declares 24 to 60 months, and against a two-month cadence reading that as a
        /// default let 12 to 30 cycles of consequences pile onto one modifier — which made the breadth
        /// cap a no-op against the problem it was written for, because a cap is a ceiling and not a
        /// default.
        /// </summary>
        [Fact]
        public void ForResolution_LastsTheTunedConsequenceLengthAndNotTheEntrysCap()
        {
            var catalog = new List<CivicEvent> { Event("e1", 3, success: new[] { CityEffectA }) };
            var story = Story("s1", Slot("e1", SlotOutcome.Met));

            EffectRequest request = Assert.Single(
                StoryEffects.ForResolution(story, Outcomes(story), catalog, District, Tuning));

            Assert.Equal(Stories.ResolutionEffectMonths, request.DurationMonths);
            Assert.True(request.DurationMonths < Palette.EffectiveDurationCapMonths(CapOf(CityEffectA)));
        }

        /// <summary>
        /// The entry's own duration cap still clamps, so tuning can only ever shorten a consequence.
        /// A hand-edited <c>resolutionEffectMonths</c> above every declared ceiling comes back at the
        /// ceiling rather than being obeyed.
        /// </summary>
        [Fact]
        public void ForResolution_LetsTheEntrysCapShortenTheConsequence()
        {
            EffectCap cap = CapOf(CityEffectA);
            int beyond = Palette.EffectiveDurationCapMonths(cap) + 12;

            EngineTuning longer = Tuned("{\"stories\":{\"resolutionEffectMonths\":" + beyond + "}}");

            var catalog = new List<CivicEvent> { Event("e1", 3, success: new[] { CityEffectA }) };
            var story = Story("s1", Slot("e1", SlotOutcome.Met));

            EffectRequest request = Assert.Single(
                StoryEffects.ForResolution(story, Outcomes(story), catalog, District, longer));

            Assert.Equal(Palette.EffectiveDurationCapMonths(cap), request.DurationMonths);
        }

        // --- the district target -------------------------------------------------------------------

        /// <summary>
        /// <b>A district-scoped effect reaches the city.</b> Twelve of the thirty-nine effect ids the
        /// authored civic events name are district-scoped, and before the seam carried a target every
        /// one of them was skipped: 102 of 277 authored references, and 47 of 174 non-empty effect
        /// phases went <i>entirely</i> empty — the author wrote <c>failureEffects</c>, the story failed,
        /// and nothing happened. That is the inert-wrapper defect this wave exists to fix.
        /// </summary>
        [Fact]
        public void ForResolution_LandsADistrictScopedEffectOnTheGivenDistrict()
        {
            var catalog = new List<CivicEvent> { Event("e1", 3, failure: new[] { DistrictEffect }) };
            var story = Story("s1", Slot("e1", SlotOutcome.NotMet));

            EffectRequest request = Assert.Single(
                StoryEffects.ForResolution(story, Outcomes(story), catalog, District, Tuning));

            Assert.Equal(DistrictEffect, request.EffectId);
            Assert.Equal(EffectScope.District, request.Scope);
            Assert.Equal(District, request.DistrictId);
        }

        /// <summary>The same on the live path.</summary>
        [Fact]
        public void ForActive_LandsADistrictScopedEffectOnTheGivenDistrict()
        {
            var catalog = new List<CivicEvent> { Event("e1", 3, active: new[] { DistrictEffect }) };
            var story = Story("s1", Slot("e1"));

            EffectRequest request = Assert.Single(
                StoryEffects.ForActive(new[] { story }, catalog, District, Tuning));

            Assert.Equal(EffectScope.District, request.Scope);
            Assert.Equal(District, request.DistrictId);
        }

        /// <summary>
        /// <b>The degraded path stays reachable and stays quiet.</b> With no target the district-scoped
        /// id is skipped rather than thrown on: a city with no districts is a save state. The
        /// city-scoped effect beside it still lands, so "no districts" costs exactly the district
        /// effects and nothing else.
        /// </summary>
        [Fact]
        public void ForResolution_SkipsADistrictScopedEffectWhenNoDistrictIsNamed()
        {
            var catalog = new List<CivicEvent>
            {
                Event("e1", 3, failure: new[] { CityEffectA, DistrictEffect })
            };
            var story = Story("s1", Slot("e1", SlotOutcome.NotMet));

            EffectRequest request = Assert.Single(
                StoryEffects.ForResolution(story, Outcomes(story), catalog, NoDistrict, Tuning));

            Assert.Equal(CityEffectA, request.EffectId);
        }

        /// <summary>
        /// A district effect and a city effect never compete for the same breadth slot: the count is
        /// keyed on scope, modifier and district together.
        /// </summary>
        [Fact]
        public void ForActive_CountsCityAndDistrictScopeSeparately()
        {
            var catalog = new List<CivicEvent>
            {
                Event("e1", 3, active: new[] { CityEffectA, DistrictEffect })
            };
            var story = Story("s1", Slot("e1"));

            List<EffectRequest> requests =
                StoryEffects.ForActive(new[] { story }, catalog, District, Tuning);

            Assert.Equal(2, requests.Count);
        }

        /// <summary>
        /// Each phase reads its own dial. Both are set below the ceiling here so the comparison is
        /// about the scales rather than about the clamp, and the assertion is the ordering rather than
        /// either number.
        /// </summary>
        [Fact]
        public void ForResolution_ReadsTheSuccessAndFailureScalesSeparately()
        {
            EngineTuning tuning = Tuned(
                "{\"stories\":{\"successEffectScale\":0.2,\"failureEffectScale\":0.6}}");

            var catalog = new List<CivicEvent>
            {
                Event("e1", 1, success: new[] { CityEffectA }, failure: new[] { CityEffectA })
            };

            var met = Story("s1", Slot("e1", SlotOutcome.Met));
            var notMet = Story("s2", Slot("e1", SlotOutcome.NotMet));

            double onSuccess = Assert.Single(
                StoryEffects.ForResolution(met, Outcomes(met), catalog, District, tuning)).Magnitude;
            double onFailure = Assert.Single(
                StoryEffects.ForResolution(notMet, Outcomes(notMet), catalog, District, tuning)).Magnitude;

            Assert.True(onFailure > onSuccess);
        }

        /// <summary>
        /// A live story asks for less than the same event's consequence does, because
        /// <c>activeEffectScale</c> ships below the resolution scales: the city reacting while the
        /// argument runs is quieter than the argument ending.
        /// </summary>
        [Fact]
        public void ForActive_AsksForLessThanTheResolutionDoes()
        {
            EngineTuning tuning = Tuned(
                "{\"stories\":{\"activeEffectScale\":0.2,\"failureEffectScale\":0.6}}");

            var catalog = new List<CivicEvent>
            {
                Event("e1", 1, active: new[] { CityEffectA }, failure: new[] { CityEffectA })
            };

            var live = Story("s1", Slot("e1"));
            var resolved = Story("s1", Slot("e1", SlotOutcome.NotMet));

            double active = Assert.Single(
                StoryEffects.ForActive(new[] { live }, catalog, District, tuning)).Magnitude;
            double consequence = Assert.Single(
                StoryEffects.ForResolution(resolved, Outcomes(resolved), catalog, District, tuning)).Magnitude;

            Assert.True(active < consequence);
        }

        /// <summary>
        /// <b>An unmeasurable slot requests nothing.</b> It means the engine could not read the city,
        /// and a sensor gap must not move the city any more than it moves the balance.
        /// </summary>
        [Fact]
        public void ForResolution_AnUnmeasurableSlotRequestsNothing()
        {
            var catalog = new List<CivicEvent>
            {
                Event("e1", 5, success: new[] { CityEffectA }, failure: new[] { CityEffectA })
            };
            var story = Story("s1", Slot("e1", SlotOutcome.Unmeasurable));

            Assert.Empty(StoryEffects.ForResolution(story, Outcomes(story), catalog, District, Tuning));
        }

        /// <summary>
        /// <b>The breadth cap binds, and it is read from tuning rather than compiled in.</b> Six story
        /// events a cycle sharing one modifier is exactly the case
        /// <c>effects.maxStackedPerModifier</c> would silently truncate in the ledger; the same input
        /// under a wider key admits every request, which is what proves the number is being read.
        /// </summary>
        [Fact]
        public void ForActive_BoundsRequestsPerModifierByTheTuningKey()
        {
            int cap = Stories.MaxStoryEffectsPerModifier;
            int count = cap + 3;

            var catalog = new List<CivicEvent>();
            var stories = new List<Story>();
            for (int i = 0; i < count; i++)
            {
                string id = "e" + i.ToString("00");
                catalog.Add(Event(id, 3, active: new[] { CityEffectA }));
                stories.Add(Story("s" + i.ToString("00"), Slot(id)));
            }

            Assert.Equal(cap, StoryEffects.ForActive(stories, catalog, District, Tuning).Count);

            EngineTuning wider = Tuned(
                "{\"stories\":{\"maxStoryEffectsPerModifier\":" + count + "}}");
            Assert.Equal(count, StoryEffects.ForActive(stories, catalog, District, wider).Count);
        }

        /// <summary>
        /// The cap is per modifier, not per call: two effects driving different modifiers do not
        /// compete for the same slots.
        /// </summary>
        [Fact]
        public void ForActive_CountsEachModifierSeparately()
        {
            var catalog = new List<CivicEvent>
            {
                Event("e1", 3, active: new[] { CityEffectA, CityEffectB })
            };
            var story = Story("s1", Slot("e1"));

            List<EffectRequest> requests = StoryEffects.ForActive(new[] { story }, catalog, District, Tuning);

            Assert.Equal(2, requests.Count);
            Assert.Equal(CityEffectA, requests[0].EffectId);
            Assert.Equal(CityEffectB, requests[1].EffectId);
        }

        /// <summary>
        /// The outcome list is index-aligned with the story's slots by contract. A mismatch is a caller
        /// defect and is refused rather than papered over — silently scoring slot 2's outcome against
        /// slot 1's event would apply the wrong consequence to the wrong event.
        /// </summary>
        [Fact]
        public void ForResolution_RefusesAnOutcomeListThatIsNotAligned()
        {
            var catalog = new List<CivicEvent> { Event("e1", 3, success: new[] { CityEffectA }) };
            var story = Story("s1", Slot("e1", SlotOutcome.Met));

            Assert.Throws<ArgumentException>(() =>
                StoryEffects.ForResolution(story, new List<SlotOutcome>(), catalog, District, Tuning));
        }

        /// <summary>The master switch makes the whole packet inert, which is the control in tests.</summary>
        [Fact]
        public void ForActive_RequestsNothingWhenTheStoriesPacketIsOff()
        {
            EngineTuning off = Tuned("{\"stories\":{\"enabled\":false}}");
            var catalog = new List<CivicEvent> { Event("e1", 3, active: new[] { CityEffectA }) };
            var story = Story("s1", Slot("e1"));

            Assert.Empty(StoryEffects.ForActive(new[] { story }, catalog, District, off));
        }

        /// <summary>
        /// A slot naming an event the catalog no longer holds asks for nothing. A catalog that shrank
        /// under a save is a gap on our side, and this file's posture on one is to do nothing.
        /// </summary>
        [Fact]
        public void ForActive_IgnoresASlotTheCatalogNoLongerExplains()
        {
            var catalog = new List<CivicEvent> { Event("e1", 3, active: new[] { CityEffectA }) };
            var story = Story("s1", Slot("gone"));

            Assert.Empty(StoryEffects.ForActive(new[] { story }, catalog, District, Tuning));
        }

        /// <summary>
        /// The canonical determinism pattern: the same inputs twice, compared as serialized output
        /// rather than field by field, so a field a hand-written assertion forgets still fails.
        /// </summary>
        [Fact]
        public void ForActive_IsDeterministic()
        {
            var catalog = new List<CivicEvent>
            {
                Event("e1", 3, active: new[] { CityEffectA, CityEffectB }),
                Event("e2", 5, active: new[] { CityEffectA })
            };
            var stories = new List<Story>
            {
                Story("s1", Slot("e1"), Slot("e2", role: SlotRole.Minor)),
                Story("s2", Slot("e2"))
            };

            Assert.Equal(Describe(StoryEffects.ForActive(stories, catalog, District, Tuning)),
                         Describe(StoryEffects.ForActive(stories, catalog, District, Tuning)));
        }

        private static string Describe(IReadOnlyList<EffectRequest> requests)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < requests.Count; i++)
            {
                EffectRequest r = requests[i];
                sb.Append(r.EffectId).Append('|').Append(r.Scope).Append('|')
                  .Append(r.Magnitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                  .Append('|').Append(r.DurationMonths).Append('|')
                  .Append(r.DistrictId ?? "").Append('|').Append(r.SourceId ?? "").Append('\n');
            }
            return sb.ToString();
        }

        // =========================================================================================
        // StoryPressure — salience
        // =========================================================================================

        /// <summary>
        /// A live story carries the argument and no verdict: salience in the authored direction, and
        /// credit of exactly zero, because the city has not yet learned whether the mayor delivered.
        /// </summary>
        [Fact]
        public void For_ALiveStoryCarriesSalienceAndNoCredit()
        {
            var catalog = new List<CivicEvent> { Event("e1", 3, salience: 0.3) };
            var story = Story("s1", Slot("e1"));

            StoryPressureContribution c = Assert.Single(
                StoryPressure.For(new[] { story }, NoStories, catalog, Tuning));

            Assert.Equal(catalog[0].ActivePressure.Services, c.Pressure.Services, 12);
            Assert.Equal(0.0, c.GovernmentCredit);
            Assert.Equal(story.OpenedDate, c.OpenedDate);
        }

        /// <summary>
        /// <b>The sign convention, stated directly.</b> A met outcome carries the event's
        /// <c>successPressure</c>, which points the same way as its <c>activePressure</c> and is
        /// merely quieter. Negating it would not release the argument — the only consumer is a dot
        /// product against a platform, so it would move voters to the opposite pole.
        /// </summary>
        [Fact]
        public void For_AMetOutcomeKeepsTheAuthoredDirection()
        {
            var catalog = new List<CivicEvent> { Event("e1", 3, salience: 0.3) };
            var story = Story("s1", Slot("e1", SlotOutcome.Met));

            StoryPressureContribution c = Assert.Single(
                StoryPressure.For(NoStories, new[] { story }, catalog, Tuning));

            Assert.True(catalog[0].ActivePressure.Services > 0.0);
            Assert.True(c.Pressure.Services > 0.0);
            Assert.True(c.Pressure.Services < catalog[0].ActivePressure.Services);
        }

        /// <summary>
        /// The same rule end to end, and the one all three wave-3 content lanes got wrong: <b>fixing
        /// the clinics must not reward the anti-services party</b>. Neither party governs here, so the
        /// only thing moving is salience.
        /// </summary>
        [Fact]
        public void For_AMetOutcomeDoesNotRewardThePartyThatOpposedActing()
        {
            var catalog = new List<CivicEvent> { Event("e1", 5, salience: 0.6) };
            var story = Story("s1", Slot("e1", SlotOutcome.Met));

            List<StoryPressureContribution> pressures =
                StoryPressure.For(NoStories, new[] { story }, catalog, Tuning);

            Bloc bloc = MakeBloc();
            Party pro = MakeParty("pro", Services(1.0));
            Party anti = MakeParty("anti", Services(-1.0));

            AffinityRequest request = Request(new[] { pro, anti }, pressures);

            double proStory = AffinityEngine.ComputeFor(bloc, pro, request, Tuning).StoryComponent;
            double antiStory = AffinityEngine.ComputeFor(bloc, anti, request, Tuning).StoryComponent;

            Assert.True(proStory > 0.0);
            Assert.True(antiStory < 0.0);
        }

        /// <summary>
        /// A story every slot of which went unreadable moves nothing at all, and is not reported as an
        /// inert row either. Only a sensor gap reaches this state — silence scores not-met.
        /// </summary>
        [Fact]
        public void For_AnUnmeasurableStoryMovesNothing()
        {
            var catalog = new List<CivicEvent> { Event("e1", 5, salience: 0.6) };
            var story = Story("s1", Slot("e1", SlotOutcome.Unmeasurable));

            Assert.Empty(StoryPressure.For(NoStories, new[] { story }, catalog, Tuning));
        }

        /// <summary>Contributions leave sorted by story id ordinal, whatever order the caller had.</summary>
        [Fact]
        public void For_IsSortedByStoryIdOrdinal()
        {
            var catalog = new List<CivicEvent> { Event("e1", 3, salience: 0.2) };
            var stories = new List<Story>
            {
                Story("s-zulu", Slot("e1")),
                Story("s-alpha", Slot("e1")),
                Story("s-mike", Slot("e1"))
            };

            List<StoryPressureContribution> result = StoryPressure.For(stories, NoStories, catalog, Tuning);

            Assert.Equal(3, result.Count);
            Assert.Equal("s-alpha", result[0].StoryId);
            Assert.Equal("s-mike", result[1].StoryId);
            Assert.Equal("s-zulu", result[2].StoryId);
        }

        // =========================================================================================
        // StoryPressure — credit
        // =========================================================================================

        /// <summary>
        /// Credit is derived from the verdict and points at the government: positive when the city's
        /// story was delivered on, negative when it was not. Nothing in any catalog expresses it.
        /// </summary>
        [Fact]
        public void For_CreditIsPositiveOnAMetStoryAndNegativeOnAFailedOne()
        {
            var catalog = new List<CivicEvent> { Event("e1", 3) };

            var met = Story("s1", Slot("e1", SlotOutcome.Met));
            var notMet = Story("s2", Slot("e1", SlotOutcome.NotMet));

            double onMet = Assert.Single(StoryPressure.For(NoStories, new[] { met }, catalog, Tuning))
                .GovernmentCredit;
            double onNotMet = Assert.Single(StoryPressure.For(NoStories, new[] { notMet }, catalog, Tuning))
                .GovernmentCredit;

            Assert.True(onMet > 0.0);
            Assert.True(onNotMet < 0.0);
        }

        /// <summary>
        /// The two dials are read. Doubling <c>alienationWeight</c> against an unchanged
        /// <c>enfranchisementWeight</c> deepens the blame and leaves the reward alone, which no
        /// compiled-in coefficient could reproduce.
        /// </summary>
        [Fact]
        public void For_ReadsTheEnfranchisementAndAlienationDials()
        {
            var catalog = new List<CivicEvent> { Event("e1", 3) };
            var notMet = Story("s1", Slot("e1", SlotOutcome.NotMet));

            EngineTuning harsher = Tuned(
                "{\"stories\":{\"alienationWeight\":" + (Stories.AlienationWeight * 2.0) + "}}");

            double shipped = Assert.Single(StoryPressure.For(NoStories, new[] { notMet }, catalog, Tuning))
                .GovernmentCredit;
            double doubled = Assert.Single(StoryPressure.For(NoStories, new[] { notMet }, catalog, harsher))
                .GovernmentCredit;

            Assert.True(doubled < shipped);
        }

        /// <summary>
        /// <b>Credit is scaled by what was at stake.</b> The stake is the slot's tier, which is the
        /// severity projection — so a mandatory-severity slot delivered on is worth more than a minor
        /// one, without a second magnitude being invented for it.
        /// </summary>
        [Fact]
        public void For_ScalesCreditByWhatWasAtStake()
        {
            var catalog = new List<CivicEvent>
            {
                Event("small", Math.Max(1, Stories.MajorSeverityThreshold - 2)),
                Event("large", Stories.MandatorySeverityThreshold)
            };

            var small = Story("s1", Slot("small", SlotOutcome.Met));
            var large = Story("s2", Slot("large", SlotOutcome.Met));

            double onSmall = Assert.Single(StoryPressure.For(NoStories, new[] { small }, catalog, Tuning))
                .GovernmentCredit;
            double onLarge = Assert.Single(StoryPressure.For(NoStories, new[] { large }, catalog, Tuning))
                .GovernmentCredit;

            Assert.True(onLarge > onSmall);
        }

        /// <summary>
        /// <b>Nobody governs, so nobody is credited.</b> The contribution still carries its credit
        /// figure — it is a statement about the verdict, not about a party — and the affinity term
        /// pays it to governing parties only, so a caretaker gap pays it to none of them.
        /// </summary>
        [Fact]
        public void For_CreditReachesNobodyWhenNobodyGoverns()
        {
            var catalog = new List<CivicEvent> { Event("e1", 5) };
            var story = Story("s1", Slot("e1", SlotOutcome.Met));

            List<StoryPressureContribution> pressures =
                StoryPressure.For(NoStories, new[] { story }, catalog, Tuning);

            // No salience was authored on this event, so credit is the only thing in the term.
            Assert.True(Assert.Single(pressures).GovernmentCredit > 0.0);

            Bloc bloc = MakeBloc();
            Party caretaker = MakeParty("p", Services(1.0));
            Party governing = MakeParty("p", Services(1.0), incumbent: true);

            Assert.Equal(0.0,
                AffinityEngine.ComputeFor(bloc, caretaker, Request(new[] { caretaker }, pressures), Tuning)
                    .StoryComponent);

            Assert.True(
                AffinityEngine.ComputeFor(bloc, governing, Request(new[] { governing }, pressures), Tuning)
                    .StoryComponent > 0.0);
        }

        /// <summary>
        /// A failed story pushes voters away from the government, and — the other half of the same
        /// ruling — the opposition is not paid an explicit mirror. Share normalisation already moves
        /// them; paying it here would count the movement twice.
        /// </summary>
        [Fact]
        public void For_AFailedStoryMovesTheGovernmentAndNotTheOpposition()
        {
            var catalog = new List<CivicEvent> { Event("e1", 5) };
            var story = Story("s1", Slot("e1", SlotOutcome.NotMet));

            List<StoryPressureContribution> pressures =
                StoryPressure.For(NoStories, new[] { story }, catalog, Tuning);

            Bloc bloc = MakeBloc();
            Party government = MakeParty("gov", Services(1.0), incumbent: true);
            Party opposition = MakeParty("opp", Services(-1.0));

            AffinityRequest request = Request(new[] { government, opposition }, pressures);

            Assert.True(AffinityEngine.ComputeFor(bloc, government, request, Tuning).StoryComponent < 0.0);
            Assert.Equal(0.0,
                AffinityEngine.ComputeFor(bloc, opposition, request, Tuning).StoryComponent);
        }

        /// <summary>
        /// <b>The bound holds under a busy cycle.</b> Credit is summed per story and clamped to
        /// <c>[-1, +1]</c> before it leaves, and salience is clamped componentwise, for the same reason
        /// <c>AffinityEngine.EventTerm</c> clamps before weighting: without it a busy cycle drowns
        /// every other term and the model stops discriminating between a flood and a bus-fare rise.
        /// </summary>
        [Fact]
        public void For_KeepsCreditAndSalienceInsideTheUnitRangeUnderManyStories()
        {
            var catalog = new List<CivicEvent>();
            var stories = new List<Story>();

            for (int i = 0; i < 40; i++)
            {
                string id = "e" + i.ToString("00");
                // A salience far above anything an author would write, so the componentwise clamp is
                // the only thing that can hold the result inside the contract.
                catalog.Add(Event(id, Stories.MandatorySeverityThreshold, salience: 0.9));

                bool met = i % 2 == 0;
                stories.Add(Story("s" + i.ToString("00"),
                    Slot(id, met ? SlotOutcome.Met : SlotOutcome.NotMet),
                    Slot(id, met ? SlotOutcome.Met : SlotOutcome.NotMet, SlotRole.Minor),
                    Slot(id, met ? SlotOutcome.Met : SlotOutcome.NotMet, SlotRole.Minor)));
            }

            List<StoryPressureContribution> result =
                StoryPressure.For(NoStories, stories, catalog, Tuning);

            Assert.Equal(stories.Count, result.Count);

            for (int i = 0; i < result.Count; i++)
            {
                StoryPressureContribution c = result[i];
                Assert.InRange(c.GovernmentCredit, -1.0, 1.0);

                for (int a = 0; a < Issues.All.Count; a++)
                    Assert.InRange(c.Pressure[Issues.All[a]], -1.0, 1.0);
            }
        }

        /// <summary>The master switch makes the whole packet inert.</summary>
        [Fact]
        public void For_MovesNothingWhenTheStoriesPacketIsOff()
        {
            EngineTuning off = Tuned("{\"stories\":{\"enabled\":false}}");
            var catalog = new List<CivicEvent> { Event("e1", 5, salience: 0.5) };
            var story = Story("s1", Slot("e1", SlotOutcome.Met));

            Assert.Empty(StoryPressure.For(new[] { story }, new[] { story }, catalog, off));
        }

        // --- affinity fixtures --------------------------------------------------------------------

        private static Party MakeParty(string id, IssuePosition platform, bool incumbent = false) =>
            new Party
            {
                Id = id,
                Name = "placeholder",
                ArchetypeId = "test",
                Platform = platform,
                LastManifesto = platform,
                Status = PartyStatus.Active,
                IsIncumbent = incumbent,
                FoundedDate = June2001
            };

        private static Bloc MakeBloc() =>
            new Bloc
            {
                DistrictId = "d1",
                Key = new BlocKey(WealthTier.Middle, EducationTier.Educated, AgeBand.Adult),
                Population = 1000,
                PopulationShare = 0.1,
                EligibleVoters = 800,
                Weights = IssueWeights.Uniform,
                Ideal = IssuePosition.Centre,
                Happiness = 60,
                Discontent = 0.0,
                PreviousVote = new List<PartyVoteShare>()
            };

        private static AffinityRequest Request(IReadOnlyList<Party> parties,
                                               IReadOnlyList<StoryPressureContribution> pressures) =>
            new AffinityRequest
            {
                SaveGuid = Save,
                Date = June2001,
                Blocs = new List<Bloc> { MakeBloc() },
                Parties = new List<Party>(parties),
                StoryPressures = pressures
            };
    }
}
