using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Events.Scheduler;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Packet 12 — the deterministic event scheduler and the procedural post-catalog generator.
    ///
    /// <para>
    /// Fixtures are synthetic and built in this file (see <c>/write-test</c>): a hand-written catalog
    /// diffs cleanly and does not rot when the timeline schema gains a field.
    /// </para>
    /// </summary>
    public class SchedulerTests
    {
        private static readonly Guid SaveA = new Guid("11111111-2222-3333-4444-555555555555");
        private static readonly Guid SaveB = new Guid("99999999-8888-7777-6666-555555555555");

        private static readonly SimDate Start = new SimDate(1990, 1, 1);
        private static readonly string[] Districts = { "harbour", "north", "old-town", "south" };

        // --- fixtures ------------------------------------------------------------------------------

        private static TimelineEvent Event(string id, SimDate date, int severity, int durationMonths,
                                           EventRegion region = EventRegion.Global,
                                           params TimelineEventEffect[] effects)
        {
            var ev = new TimelineEvent
            {
                Id = id,
                Date = date,
                Region = region,
                Origin = EventOrigin.Catalog,
                Title = id,
                Severity = severity,
                DurationMonths = durationMonths,
                HeadlineBrief = "brief for " + id
            };

            for (int i = 0; i < effects.Length; i++) ev.Effects.Add(effects[i]);
            return ev;
        }

        private static TimelineEventEffect CityEffect(string id, double magnitude, int months) =>
            new TimelineEventEffect(id, EffectScope.City, magnitude, months);

        private static TimelineEventEffect DistrictEffect(string id, double magnitude, int months) =>
            new TimelineEventEffect(id, EffectScope.District, magnitude, months);

        private static SchedulerContext Context(SimDate date, IEnumerable<TimelineEvent> catalog,
                                                Guid? save = null, RegionTheme theme = RegionTheme.Eu)
        {
            return new SchedulerContext
            {
                SaveGuid = save ?? SaveA,
                Date = date,
                StartDate = Start,
                Theme = theme,
                Catalog = catalog.ToList(),
                DistrictIds = Districts.ToList()
            };
        }

        /// <summary>The shipped defaults. Effectively immutable — every setter on it is internal.</summary>
        private static readonly EngineTuning Tuning = EngineTuning.Default;

        // --- catalog scheduling --------------------------------------------------------------------

        [Fact]
        public void CatalogEvent_DoesNotFireBeforeItsMonth()
        {
            var catalog = new[] { Event("gulf-war", new SimDate(1992, 3, 15), 3, 6) };

            SchedulerTick early = EventScheduler.Run(Context(new SimDate(1992, 2, 1), catalog), Tuning);

            Assert.Empty(early.Fired);
            Assert.Empty(early.EffectRequests);
        }

        [Fact]
        public void CatalogEvent_FiresInItsOwnMonth_EvenWhenAuthoredMidMonth()
        {
            var catalog = new[] { Event("gulf-war", new SimDate(1992, 3, 15), 3, 6) };

            SchedulerTick tick = EventScheduler.Run(Context(new SimDate(1992, 3, 1), catalog), Tuning);

            TimelineEvent fired = Assert.Single(tick.Fired);
            Assert.Equal("gulf-war", fired.Id);
            Assert.Equal(new SimDate(1992, 3, 1), fired.FiredDate);
            Assert.Equal(new SimDate(1992, 9, 1), fired.ExpiresDate);
            Assert.Contains("gulf-war", tick.RecordedEventIds);
        }

        [Fact]
        public void CatalogEvent_DatedBeforeTheSaveStarts_IsWrittenOffAsHistory()
        {
            var catalog = new[]
            {
                Event("reunification", new SimDate(1989, 11, 9), 5, 12),
                Event("maastricht", new SimDate(1992, 2, 7), 4, 12)
            };

            SchedulerTick tick = EventScheduler.Run(Context(new SimDate(1992, 2, 1), catalog), Tuning);

            Assert.Contains("reunification", tick.SkippedHistoricalIds);
            Assert.DoesNotContain(tick.Fired, e => e.Id == "reunification");

            // Recorded, so the scan stops reconsidering it every month for the rest of the save.
            Assert.Contains("reunification", tick.RecordedEventIds);
            Assert.Contains(tick.Fired, e => e.Id == "maastricht");
        }

        [Fact]
        public void CatalogEvent_OutsideTheThemeRegion_NeverFires()
        {
            var catalog = new[]
            {
                Event("nafta", new SimDate(1994, 1, 1), 3, 12, EventRegion.Na),
                Event("maastricht", new SimDate(1994, 1, 1), 3, 12, EventRegion.Eu),
                Event("gfc", new SimDate(1994, 1, 1), 3, 12, EventRegion.Global)
            };

            SchedulerTick eu = EventScheduler.Run(Context(new SimDate(1994, 1, 1), catalog), Tuning);
            SchedulerTick na = EventScheduler.Run(
                Context(new SimDate(1994, 1, 1), catalog, theme: RegionTheme.Na), Tuning);

            Assert.Equal(new[] { "gfc", "maastricht" }, eu.Fired.Select(e => e.Id).OrderBy(i => i, StringComparer.Ordinal));
            Assert.Equal(new[] { "gfc", "nafta" }, na.Fired.Select(e => e.Id).OrderBy(i => i, StringComparer.Ordinal));
        }

        [Fact]
        public void CatalogEvent_DatedPastTheEndOfCuratedHistory_NeverFires()
        {
            // The procedural generator owns that era; a catalog entry there is a data error.
            var catalog = new[] { Event("impossible", new SimDate(2040, 1, 1), 3, 12) };

            SchedulerTick tick = EventScheduler.Run(Context(new SimDate(2040, 1, 1), catalog), Tuning);

            Assert.DoesNotContain(tick.Fired, e => e.Id == "impossible");
            Assert.DoesNotContain("impossible", tick.SkippedHistoricalIds);
        }

        [Fact]
        public void MaxEventsPerTick_DefersTheRemainder_WhichFiresNextTick()
        {
            var catalog = new[]
            {
                Event("ev-a", new SimDate(1992, 1, 10), 2, 3),
                Event("ev-b", new SimDate(1992, 1, 10), 2, 3),
                Event("ev-c", new SimDate(1992, 1, 10), 2, 3),
                Event("ev-d", new SimDate(1992, 1, 10), 2, 3),
                Event("ev-e", new SimDate(1992, 1, 10), 2, 3)
            };

            SchedulerContext first = Context(new SimDate(1992, 1, 1), catalog);
            SchedulerTick tick1 = EventScheduler.Run(first, Tuning);

            Assert.Equal(3, tick1.Fired.Count);                                  // scheduler.maxEventsPerTick
            Assert.Equal(new[] { "ev-a", "ev-b", "ev-c" }, tick1.Fired.Select(e => e.Id));
            Assert.Equal(new[] { "ev-d", "ev-e" }, tick1.DeferredEventIds);

            SchedulerContext second = Context(new SimDate(1992, 2, 1), catalog);
            second.FiredEventIds = tick1.RecordedEventIds;
            second.ActiveEvents = tick1.NextActiveEvents;

            SchedulerTick tick2 = EventScheduler.Run(second, Tuning);

            Assert.Equal(new[] { "ev-d", "ev-e" }, tick2.Fired.Select(e => e.Id));
            Assert.Empty(tick2.DeferredEventIds);
        }

        [Fact]
        public void MaxConcurrentEvents_StopsTheTimelineFromPilingUp()
        {
            var catalog = Enumerable.Range(0, 8)
                .Select(i => Event("ev-" + (char)('a' + i), new SimDate(1992, 1, 10), 2, 24))
                .ToList();

            var state = new SchedulerState(catalog);

            SchedulerTick tick1 = state.Advance(new SimDate(1992, 1, 1));
            SchedulerTick tick2 = state.Advance(new SimDate(1992, 2, 1));
            SchedulerTick tick3 = state.Advance(new SimDate(1992, 3, 1));

            Assert.Equal(3, tick1.Fired.Count);
            Assert.Equal(3, tick2.Fired.Count);

            // Six live events is catalog.maxConcurrentEvents; the last two wait rather than stack.
            Assert.Empty(tick3.Fired);
            Assert.Equal(new[] { "ev-g", "ev-h" }, tick3.DeferredEventIds);
            Assert.Equal(6, tick3.Continuing.Count);
        }

        [Fact]
        public void MajorEvents_ObserveTheCooldown_ThenFire()
        {
            var catalog = new[]
            {
                Event("crash-one", new SimDate(1992, 1, 10), 5, 2),
                Event("crash-two", new SimDate(1992, 2, 10), 5, 2)
            };

            var state = new SchedulerState(catalog);

            Assert.Equal(new[] { "crash-one" }, state.Advance(new SimDate(1992, 1, 1)).Fired.Select(e => e.Id));

            // catalog.minMonthsBetweenMajorEvents is 3, so one and two months later are both too soon.
            SchedulerTick february = state.Advance(new SimDate(1992, 2, 1));
            Assert.Empty(february.Fired);
            Assert.Equal(new[] { "crash-two" }, february.DeferredEventIds);

            SchedulerTick march = state.Advance(new SimDate(1992, 3, 1));
            Assert.Empty(march.Fired);

            // By April the first major has already expired — the cooldown still holds, because it is
            // derived from the fired-id ledger and the catalog, not only from what is still live.
            SchedulerTick april = state.Advance(new SimDate(1992, 4, 1));
            Assert.Equal(new[] { "crash-two" }, april.Fired.Select(e => e.Id));
        }

        [Fact]
        public void ActiveEvent_ExpiresExactlyAfterItsDuration()
        {
            var catalog = new[] { Event("recession", new SimDate(1992, 1, 10), 3, 6) };
            var state = new SchedulerState(catalog);

            state.Advance(new SimDate(1992, 1, 1));

            SchedulerTick june = state.Advance(new SimDate(1992, 6, 1));
            Assert.Equal(new[] { "recession" }, june.Continuing.Select(e => e.Id));
            Assert.Empty(june.Expired);

            SchedulerTick july = state.Advance(new SimDate(1992, 7, 1));
            Assert.Equal(new[] { "recession" }, july.Expired.Select(e => e.Id));
            Assert.Empty(july.NextActiveEvents);
        }

        [Fact]
        public void Run_NeverMutatesTheCatalogItWasGiven()
        {
            var source = Event("gfc", new SimDate(1992, 1, 10), 3, 6, EventRegion.Global,
                               CityEffect("city-loan-interest", 0.10, 24),
                               DistrictEffect("district-wellbeing", -0.05, 12));

            var catalog = new[] { source };
            EventScheduler.Run(Context(new SimDate(1992, 1, 1), catalog), Tuning);

            // The catalog is loaded once and reused all session. Mutating it in place would make the
            // second run of an identical tick differ from the first.
            Assert.Null(source.FiredDate);
            Assert.Null(source.ExpiresDate);
            Assert.Equal(0.10, source.Effects[0].Magnitude, 12);
            Assert.Null(source.Effects[1].DistrictId);
        }

        [Fact]
        public void FiredEvent_IsNotOfferedAgain()
        {
            var catalog = new[] { Event("gfc", new SimDate(1992, 1, 10), 3, 6) };
            var state = new SchedulerState(catalog);

            Assert.Single(state.Advance(new SimDate(1992, 1, 1)).Fired);
            Assert.Empty(state.Advance(new SimDate(1992, 2, 1)).Fired);
            Assert.Empty(state.Advance(new SimDate(1993, 2, 1)).Fired);
        }

        // --- effects: caps, scaling, targeting ------------------------------------------------------

        [Fact]
        public void EffectMagnitude_ScalesWithSeverity()
        {
            // catalog.severityEffectScale is 0.20: severity 1 is the authored magnitude, each further
            // point adds 20% of it. 0.10 at severity 3 is 0.14, comfortably under the 0.30 cap.
            double mild = FirstMagnitude(severity: 1, authored: 0.10);
            double severe = FirstMagnitude(severity: 3, authored: 0.10);

            Assert.Equal(0.10, mild, 10);
            Assert.Equal(0.14, severe, 10);
            Assert.True(severe > mild);
        }

        [Fact]
        public void EffectMagnitude_ClampsToThePaletteCap_InBothDirections()
        {
            // city-loan-interest declares a 0.30 magnitude cap. A cap that only holds one way is not a cap.
            Assert.Equal(0.30, FirstMagnitude(severity: 5, authored: 5.0), 10);
            Assert.Equal(-0.30, FirstMagnitude(severity: 5, authored: -5.0), 10);
        }

        [Fact]
        public void EffectDuration_ClampsToThePaletteCap()
        {
            var catalog = new[]
            {
                Event("gfc", new SimDate(1992, 1, 10), 3, 6, EventRegion.Global,
                      CityEffect("city-loan-interest", 0.10, 9999))
            };

            SchedulerTick tick = EventScheduler.Run(Context(new SimDate(1992, 1, 1), catalog), Tuning);

            Assert.Equal(60, tick.Fired[0].Effects[0].DurationMonths);   // city-loan-interest: 60 months
            Assert.Equal(60, tick.EffectRequests[0].DurationMonths);
        }

        [Fact]
        public void DistrictEffect_IsTargetedDeterministically_AndNotAlwaysTheSameDistrict()
        {
            var catalog = Enumerable.Range(0, 20)
                .Select(i => Event("ev-" + i.ToString("D2", CultureInfo.InvariantCulture),
                                   new SimDate(1992, 1, 10), 2, 1, EventRegion.Global,
                                   DistrictEffect("district-wellbeing", -0.05, 6)))
                .ToList();

            var first = new SchedulerState(catalog);
            var second = new SchedulerState(catalog);

            var targetsA = new List<string>();
            var targetsB = new List<string>();

            for (int month = 1; month <= 12; month++)
            {
                var date = new SimDate(1992, month, 1);
                targetsA.AddRange(first.Advance(date).EffectRequests.Select(r => r.DistrictId!));
                targetsB.AddRange(second.Advance(date).EffectRequests.Select(r => r.DistrictId!));
            }

            Assert.Equal(20, targetsA.Count);
            Assert.Equal(targetsA, targetsB);
            Assert.All(targetsA, t => Assert.Contains(t, Districts));
            Assert.True(targetsA.Distinct().Count() > 1, "district targeting collapsed onto one district");
        }

        [Fact]
        public void DistrictEffect_IsDroppedWithAWarning_WhenTheCityHasNoDistricts()
        {
            var catalog = new[]
            {
                Event("gfc", new SimDate(1992, 1, 10), 3, 6, EventRegion.Global,
                      DistrictEffect("district-wellbeing", -0.05, 12),
                      CityEffect("city-loan-interest", 0.10, 12))
            };

            SchedulerContext context = Context(new SimDate(1992, 1, 1), catalog);
            context.DistrictIds = new List<string>();

            SchedulerTick tick = EventScheduler.Run(context, Tuning);

            EffectRequest request = Assert.Single(tick.EffectRequests);
            Assert.Equal("city-loan-interest", request.EffectId);
            Assert.Single(tick.Warnings);
        }

        [Fact]
        public void UnknownEffectId_DegradesToTheTerminalFallback()
        {
            var catalog = new[]
            {
                Event("gfc", new SimDate(1992, 1, 10), 1, 6, EventRegion.Global,
                      CityEffect("city-invented-by-nobody", 0.10, 12))
            };

            SchedulerTick tick = EventScheduler.Run(Context(new SimDate(1992, 1, 1), catalog), Tuning);

            // The palette is a closed registry: an id that is not in it does not exist.
            Assert.Equal("city-tax-happiness", tick.EffectRequests[0].EffectId);
            Assert.Single(tick.Warnings);
        }

        [Fact]
        public void EffectsDisabled_StillFiresEvents_ButRequestsNothing()
        {
            var catalog = new[]
            {
                Event("gfc", new SimDate(1992, 1, 10), 3, 6, EventRegion.Global,
                      CityEffect("city-loan-interest", 0.10, 12))
            };

            SchedulerContext context = Context(new SimDate(1992, 1, 1), catalog);
            context.EffectsEnabled = false;

            SchedulerTick tick = EventScheduler.Run(context, Tuning);

            Assert.Single(tick.Fired);
            Assert.Single(tick.Fired[0].Effects);      // still resolved, so the dashboard can show it
            Assert.Empty(tick.EffectRequests);         // but nothing reaches the sink
        }

        [Fact]
        public void PoliticalEvent_FiresImmediately_AndIgnoresThePerTickBudget()
        {
            var catalog = new[]
            {
                Event("ev-a", new SimDate(1992, 1, 10), 2, 3),
                Event("ev-b", new SimDate(1992, 1, 10), 2, 3),
                Event("ev-c", new SimDate(1992, 1, 10), 2, 3)
            };

            var unrest = new TimelineEvent
            {
                Id = "unrest-mandate-1",
                Origin = EventOrigin.Political,
                Title = "Unrest after a defied mandate",
                Severity = 2,
                DurationMonths = 3
            };
            unrest.Effects.Add(DistrictEffect("district-crime-accumulation", 0.05, 6));

            SchedulerContext context = Context(new SimDate(1992, 1, 1), catalog);
            context.PendingPolitical = new[] { unrest };

            SchedulerTick tick = EventScheduler.Run(context, Tuning);

            Assert.Equal(4, tick.Fired.Count);
            TimelineEvent fired = tick.Fired.Single(e => e.Id == "unrest-mandate-1");
            Assert.Equal(EventOrigin.Political, fired.Origin);

            // An unset date on an engine-raised event means "now", not year zero.
            Assert.Equal(new SimDate(1992, 1, 1), fired.Date);
            Assert.Equal(new SimDate(1992, 4, 1), fired.ExpiresDate);
        }

        // --- procedural generation ------------------------------------------------------------------

        [Fact]
        public void Procedural_GeneratesNothingBeforeItsStartYear()
        {
            var pool = ProceduralArchetypes.CreateDefaultPool();

            for (int year = 1990; year < 2027; year++)
            {
                for (int month = 1; month <= 12; month++)
                {
                    List<TimelineEvent> generated = ProceduralEventGenerator.Generate(
                        SaveA, new SimDate(year, month, 1), RegionTheme.Eu, pool, null, Tuning);

                    Assert.Empty(generated);
                }
            }
        }

        [Fact]
        public void Procedural_FiresAtRoughlyTheConfiguredRate()
        {
            // catalog.proceduralEventsPerYear is 2.0. Over a century that is ~200 events; the band is
            // wide enough to survive the Bernoulli variance and narrow enough to catch a broken rate.
            int count = ProceduralIds(SaveA, 2027, years: 100).Count;

            Assert.InRange(count, 140, 260);
        }

        [Fact]
        public void Procedural_EventsAreWellFormed()
        {
            var palette = new HashSet<string>(Tuning.Effects.EffectIds, StringComparer.Ordinal);
            var pool = ProceduralArchetypes.CreateDefaultPool();
            var archetypeIds = new HashSet<string>(pool.Select(a => a.Id), StringComparer.Ordinal);
            int seen = 0;

            for (int year = 2027; year < 2067; year++)
            {
                for (int month = 1; month <= 12; month++)
                {
                    foreach (TimelineEvent ev in ProceduralEventGenerator.Generate(
                                 SaveA, new SimDate(year, month, 1), RegionTheme.Eu, pool, null, Tuning))
                    {
                        seen++;

                        Assert.Equal(EventOrigin.Procedural, ev.Origin);
                        Assert.Contains(ev.ArchetypeId, archetypeIds);
                        Assert.InRange(ev.Severity, 1, Tuning.Catalog.SeverityMax);
                        Assert.Equal(new SimDate(year, month, 1), ev.Date);
                        Assert.Matches("^[a-z0-9-]+$", ev.Id);
                        Assert.NotEmpty(ev.HeadlineBrief);
                        Assert.NotEmpty(ev.Effects);

                        foreach (TimelineEventEffect effect in ev.Effects)
                        {
                            // Every archetype effect must be in the closed palette registry.
                            Assert.Contains(effect.EffectId, palette);
                        }
                    }
                }
            }

            Assert.True(seen > 0, "the generator produced nothing over forty years");
        }

        [Fact]
        public void Procedural_IsIdenticalForTheSameSaveAndDate()
        {
            Assert.Equal(ProceduralIds(SaveA, 2027, years: 20), ProceduralIds(SaveA, 2027, years: 20));
        }

        [Fact]
        public void Procedural_DiffersBetweenSaves()
        {
            Assert.NotEqual(ProceduralIds(SaveA, 2027, years: 20), ProceduralIds(SaveB, 2027, years: 20));
        }

        [Fact]
        public void Procedural_RespectsTheSeverityDistribution()
        {
            var severities = new List<int>();
            var pool = ProceduralArchetypes.CreateDefaultPool();

            for (int year = 2027; year < 2127; year++)
            {
                for (int month = 1; month <= 12; month++)
                {
                    severities.AddRange(ProceduralEventGenerator
                        .Generate(SaveA, new SimDate(year, month, 1), RegionTheme.Eu, pool, null, Tuning)
                        .Select(e => e.Severity));
                }
            }

            // proceduralSeverityMean 2.5, sigma 0.8: the mean should land near 2.5, and nothing may
            // escape [1, severityMax] however far into the tail the Gaussian wanders.
            Assert.InRange(severities.Average(), 2.0, 3.0);
            Assert.All(severities, s => Assert.InRange(s, 1, 5));
        }

        [Fact]
        public void Procedural_SkipsAnIdThatAlreadyFired()
        {
            var pool = ProceduralArchetypes.CreateDefaultPool();

            SimDate date = FirstProceduralDate(SaveA, out List<TimelineEvent> generated);
            Assert.NotEmpty(generated);

            List<TimelineEvent> replay = ProceduralEventGenerator.Generate(
                SaveA, date, RegionTheme.Eu, pool, generated.Select(e => e.Id).ToList(), Tuning);

            Assert.Empty(replay);
        }

        [Fact]
        public void Procedural_EventsFlowThroughTheSchedulerWithCappedEffects()
        {
            var state = new SchedulerState(new List<TimelineEvent>());
            var fired = new List<TimelineEvent>();

            for (int year = 2027; year < 2047; year++)
            {
                for (int month = 1; month <= 12; month++)
                {
                    SchedulerTick tick = state.Advance(new SimDate(year, month, 1));
                    fired.AddRange(tick.Fired);

                    foreach (EffectRequest request in tick.EffectRequests)
                    {
                        EffectCap cap = Tuning.Effects.CapFor(request.EffectId, request.Scope);
                        Assert.InRange(request.Magnitude, -cap.MagnitudeCap, cap.MagnitudeCap);
                        Assert.InRange(request.DurationMonths, 0, cap.DurationCapMonths);

                        if (request.Scope == EffectScope.District) Assert.NotNull(request.DistrictId);
                    }
                }
            }

            Assert.NotEmpty(fired);
            Assert.All(fired, e => Assert.Equal(EventOrigin.Procedural, e.Origin));
        }

        // --- determinism ----------------------------------------------------------------------------

        [Fact]
        public void Run_ProducesIdenticalHashTwice()
        {
            Assert.Equal(HashRun(SaveA, Start), HashRun(SaveA, Start));
        }

        [Fact]
        public void Run_DiffersAcrossSaves()
        {
            // Without this, a scheduler that returned nothing at all would pass the determinism test.
            Assert.NotEqual(HashRun(SaveA, Start), HashRun(SaveB, Start));
        }

        // --- tick calendar ---------------------------------------------------------------------------

        [Fact]
        public void TickPlan_RunsEverySubsystemOnTheAnchorMonth()
        {
            TickPlan plan = TickPlanner.Plan(Start, Start, new AgoraSettings(), null, false, false, Tuning);

            Assert.True(plan.IsEngineTick);
            Assert.True(plan.IsEventScan);
            Assert.True(plan.IsSnapshot);
            Assert.True(plan.IsLifecycle);
            Assert.True(plan.IsIndices);
            Assert.True(plan.IsMandateMonitor);
            Assert.True(plan.HasWork);
        }

        [Fact]
        public void TickPlan_RunsLifecycleYearly_NotMonthly()
        {
            var settings = new AgoraSettings();

            Assert.False(TickPlanner.Plan(Start, new SimDate(1990, 7, 1), settings, null, false, false, Tuning).IsLifecycle);
            Assert.True(TickPlanner.Plan(Start, new SimDate(1991, 1, 1), settings, null, false, false, Tuning).IsLifecycle);
            Assert.True(TickPlanner.Plan(Start, new SimDate(1990, 7, 1), settings, null, false, false, Tuning).IsIndices);
        }

        /// <summary>
        /// The default cadence, and what every existing save has always had: a poll on every
        /// political tick.
        /// </summary>
        /// <remarks>
        /// Asserted across two years rather than at one date, because the expression this replaced
        /// was <c>((date.Day - 1) % pollTickIntervalDays) == 0</c> and <c>SimDate.Day</c> is a literal
        /// <c>1</c> on every date the clock produces — so it was unconditionally true, and a
        /// single-date assertion would have passed against it just as happily. What makes this test
        /// mean something is the pair below, which prove the flag is now capable of being false.
        /// </remarks>
        [Fact]
        public void TickPlan_PollsOnEveryEngineTickByDefault()
        {
            var settings = new AgoraSettings();
            Assert.Equal(1, Tuning.Scheduler.PollTickIntervalMonths);

            for (int i = 0; i < 24; i++)
            {
                Assert.True(TickPlanner.Plan(Start, Start.AddMonths(i), settings, null, false, false, Tuning)
                                       .IsPollTick,
                            "month " + i + " should poll at the default interval of one month");
            }
        }

        /// <summary>
        /// The dial means something for the first time. Measured in whole months from the save's
        /// start date like every other cadence here, so a reload or a fast-forward cannot shift its
        /// phase.
        /// </summary>
        [Fact]
        public void TickPlan_PollFollowsTheConfiguredMonthInterval()
        {
            var settings = new AgoraSettings();
            EngineTuning quarterly = EngineTuning.FromJson("{\"scheduler\":{\"pollTickIntervalMonths\":3}}");

            var polled = new List<int>();
            for (int i = 0; i < 12; i++)
            {
                if (TickPlanner.Plan(Start, Start.AddMonths(i), settings, null, false, false, quarterly).IsPollTick)
                {
                    polled.Add(i);
                }
            }

            Assert.Equal(new[] { 0, 3, 6, 9 }, polled);
        }

        /// <summary>
        /// A poll published on a month the engine did not advance would report shares nothing had
        /// recomputed, so the flag is gated on <c>IsEngineTick</c> like every other cadence — even
        /// when the poll interval on its own would say yes.
        /// </summary>
        [Fact]
        public void TickPlan_DoesNotPollOnAMonthTheEngineDidNotTick()
        {
            var settings = new AgoraSettings();
            EngineTuning slow = EngineTuning.FromJson(
                "{\"scheduler\":{\"tickIntervalMonths\":3,\"pollTickIntervalMonths\":1}}");

            for (int i = 0; i < 9; i++)
            {
                TickPlan plan = TickPlanner.Plan(Start, Start.AddMonths(i), settings, null, false, false, slow);

                Assert.Equal(i % 3 == 0, plan.IsEngineTick);
                Assert.Equal(plan.IsEngineTick, plan.IsPollTick);
            }

            // And a date before the save started is not a tick of anything. The clock belongs to the
            // Mod, so the planner answers rather than throws.
            Assert.False(TickPlanner.Plan(Start, Start.AddMonths(-1), settings, null, false, false, slow).IsPollTick);
        }

        /// <summary>
        /// A non-positive interval means never, which is the convention every cadence in this planner
        /// follows. The one exception is the master tick interval, which is floored at 1 because a
        /// zero there would freeze the engine rather than configure it.
        /// </summary>
        [Fact]
        public void TickPlan_ANonPositivePollInterval_NeverPolls()
        {
            var settings = new AgoraSettings();
            EngineTuning off = EngineTuning.FromJson("{\"scheduler\":{\"pollTickIntervalMonths\":0}}");

            for (int i = 0; i < 12; i++)
            {
                TickPlan plan = TickPlanner.Plan(Start, Start.AddMonths(i), settings, null, false, false, off);

                Assert.True(plan.IsEngineTick);
                Assert.False(plan.IsPollTick);
            }
        }

        [Fact]
        public void TickPlan_WarmupCompletesAfterTheConfiguredMonths()
        {
            var settings = new AgoraSettings();

            Assert.False(TickPlanner.Plan(Start, new SimDate(1990, 6, 1), settings, null, false, false, Tuning).IsWarmupComplete);
            Assert.True(TickPlanner.Plan(Start, new SimDate(1990, 7, 1), settings, null, false, false, Tuning).IsWarmupComplete);
        }

        [Fact]
        public void TickPlan_CampaignSeasonOpensTwoMonthsBeforeTheElection()
        {
            var settings = new AgoraSettings();
            var election = new SimDate(1994, 6, 1);

            Assert.False(TickPlanner.Plan(Start, new SimDate(1994, 3, 1), settings, election, false, false, Tuning).IsCampaignSeason);
            Assert.True(TickPlanner.Plan(Start, new SimDate(1994, 4, 1), settings, election, false, false, Tuning).IsCampaignSeason);
            Assert.True(TickPlanner.Plan(Start, new SimDate(1994, 6, 1), settings, election, false, false, Tuning).IsCampaignSeason);
            Assert.False(TickPlanner.Plan(Start, new SimDate(1994, 7, 1), settings, election, false, false, Tuning).IsCampaignSeason);
        }

        [Fact]
        public void TickPlan_LlmWakesYearlyAndOnElections_AndObeysPerSaveSettings()
        {
            var settings = new AgoraSettings();

            Assert.Equal(LlmWakeCadence.Yearly,
                TickPlanner.Plan(Start, new SimDate(1991, 1, 1), settings, null, false, false, Tuning).LlmWake);

            Assert.Equal(LlmWakeCadence.None,
                TickPlanner.Plan(Start, new SimDate(1991, 5, 1), settings, null, false, false, Tuning).LlmWake);

            Assert.Equal(LlmWakeCadence.Election,
                TickPlanner.Plan(Start, new SimDate(1991, 5, 1), settings, null, true, false, Tuning).LlmWake);

            // Per-save settings win over the tuning switch (non-negotiable #10).
            var quiet = new AgoraSettings { WakeCadence = LlmWakeCadence.None };
            Assert.Equal(LlmWakeCadence.None,
                TickPlanner.Plan(Start, new SimDate(1991, 1, 1), quiet, null, true, true, Tuning).LlmWake);
        }

        [Fact]
        public void CatchUpDates_AreClampedToTheCap_KeepingTheRecentPast()
        {
            List<SimDate> shortGap = TickPlanner.CatchUpDates(Start, new SimDate(1990, 6, 1), Tuning, out bool truncatedShort);

            Assert.False(truncatedShort);
            Assert.Equal(5, shortGap.Count);
            Assert.Equal(new SimDate(1990, 2, 1), shortGap[0]);
            Assert.Equal(new SimDate(1990, 6, 1), shortGap[shortGap.Count - 1]);

            List<SimDate> longGap = TickPlanner.CatchUpDates(Start, new SimDate(2010, 1, 1), Tuning, out bool truncatedLong);

            Assert.True(truncatedLong);
            Assert.Equal(120, longGap.Count);                             // scheduler.catchUpMaxMonths
            Assert.Equal(new SimDate(2010, 1, 1), longGap[longGap.Count - 1]);
            Assert.Equal(new SimDate(2000, 2, 1), longGap[0]);
        }

        [Fact]
        public void SnapshotsToPrune_KeepsTheNewestN_AndNothingCleverer()
        {
            var existing = new List<SimDate>();
            for (int i = 0; i < 30; i++) existing.Add(Start.AddMonths(i));

            // Deliberately out of order: the caller's directory listing order must not matter.
            existing.Reverse();

            List<SimDate> prune = TickPlanner.SnapshotsToPrune(existing, Tuning);

            Assert.Equal(5, prune.Count);                                 // 30 - scheduler.snapshotRetention
            Assert.Equal(Start, prune[0]);
            Assert.Equal(Start.AddMonths(4), prune[prune.Count - 1]);
        }

        [Fact]
        public void SnapshotsToPrune_IsEmptyBelowTheRetentionLimit()
        {
            var existing = new List<SimDate>();
            for (int i = 0; i < 25; i++) existing.Add(Start.AddMonths(i));

            Assert.Empty(TickPlanner.SnapshotsToPrune(existing, Tuning));
        }

        [Fact]
        public void SnapshotsToPrune_PrefersThePerSaveSetting()
        {
            var existing = new List<SimDate>();
            for (int i = 0; i < 30; i++) existing.Add(Start.AddMonths(i));

            var settings = new AgoraSettings { SnapshotRetention = 10 };

            Assert.Equal(20, TickPlanner.SnapshotsToPrune(existing, Tuning, settings).Count);
        }

        // --- helpers ---------------------------------------------------------------------------------

        /// <summary>
        /// Carries the two pieces of state the scheduler needs between ticks — the fired-id ledger and
        /// the live event list — exactly as <c>PoliticalState</c> would.
        /// </summary>
        private sealed class SchedulerState
        {
            private readonly List<TimelineEvent> _catalog;
            private readonly List<string> _fired = new List<string>();
            private readonly Guid _save;
            private List<TimelineEvent> _active = new List<TimelineEvent>();

            public SchedulerState(IEnumerable<TimelineEvent> catalog, Guid? save = null)
            {
                _catalog = catalog.ToList();
                _save = save ?? SaveA;
            }

            public SchedulerTick Advance(SimDate date)
            {
                var context = new SchedulerContext
                {
                    SaveGuid = _save,
                    Date = date,
                    StartDate = Start,
                    Theme = RegionTheme.Eu,
                    Catalog = _catalog,
                    FiredEventIds = _fired,
                    ActiveEvents = _active,
                    DistrictIds = Districts.ToList()
                };

                SchedulerTick tick = EventScheduler.Run(context, Tuning);

                foreach (string id in tick.RecordedEventIds)
                {
                    if (!_fired.Contains(id)) _fired.Add(id);
                }
                _fired.Sort(StringComparer.Ordinal);
                _active = tick.NextActiveEvents;

                return tick;
            }
        }

        private static double FirstMagnitude(int severity, double authored)
        {
            var catalog = new[]
            {
                Event("gfc", new SimDate(1992, 1, 10), severity, 6, EventRegion.Global,
                      CityEffect("city-loan-interest", authored, 12))
            };

            return EventScheduler.Run(Context(new SimDate(1992, 1, 1), catalog), Tuning)
                                 .EffectRequests[0].Magnitude;
        }

        private static List<string> ProceduralIds(Guid save, int startYear, int years)
        {
            var pool = ProceduralArchetypes.CreateDefaultPool();
            var ids = new List<string>();

            for (int year = startYear; year < startYear + years; year++)
            {
                for (int month = 1; month <= 12; month++)
                {
                    ids.AddRange(ProceduralEventGenerator
                        .Generate(save, new SimDate(year, month, 1), RegionTheme.Eu, pool, null, Tuning)
                        .Select(e => e.Id));
                }
            }

            return ids;
        }

        private static SimDate FirstProceduralDate(Guid save, out List<TimelineEvent> generated)
        {
            var pool = ProceduralArchetypes.CreateDefaultPool();

            for (int year = 2027; year < 2047; year++)
            {
                for (int month = 1; month <= 12; month++)
                {
                    var date = new SimDate(year, month, 1);
                    generated = ProceduralEventGenerator.Generate(save, date, RegionTheme.Eu, pool, null, Tuning);
                    if (generated.Count > 0) return date;
                }
            }

            generated = new List<TimelineEvent>();
            return new SimDate(2027, 1, 1);
        }

        /// <summary>
        /// Forty-five simulated years through both eras — curated catalog, then procedural — hashed as
        /// one string. Hashing catches the field a hand-written assertion forgot, which is where
        /// desyncs hide.
        /// </summary>
        private static string HashRun(Guid save, SimDate start)
        {
            var catalog = new List<TimelineEvent>
            {
                Event("gulf-war", new SimDate(1990, 8, 2), 4, 9, EventRegion.Global,
                      CityEffect("city-import-cost", 0.12, 12)),
                Event("maastricht", new SimDate(1992, 2, 7), 3, 24, EventRegion.Eu,
                      CityEffect("city-attractiveness", 0.06, 24)),
                Event("nafta", new SimDate(1994, 1, 1), 3, 24, EventRegion.Na,
                      CityEffect("city-export-cost", -0.06, 24)),
                Event("dot-com-bust", new SimDate(2000, 3, 10), 4, 18, EventRegion.Global,
                      CityEffect("city-office-efficiency", -0.09, 18),
                      DistrictEffect("district-wellbeing", -0.04, 12)),
                Event("gfc", new SimDate(2008, 9, 15), 5, 36, EventRegion.Global,
                      CityEffect("city-loan-interest", 0.20, 36),
                      DistrictEffect("district-building-upkeep", 0.07, 24)),
                Event("covid", new SimDate(2020, 3, 11), 5, 24, EventRegion.Global,
                      CityEffect("city-disease-probability", 0.15, 24),
                      DistrictEffect("district-wellbeing", -0.08, 18))
            };

            var state = new SchedulerState(catalog, save);
            var output = new StringBuilder();
            SimDate date = start;

            for (int month = 0; month < 45 * 12; month++)
            {
                SchedulerTick tick = state.Advance(date);

                output.Append(date).Append('|');
                foreach (TimelineEvent ev in tick.Fired)
                {
                    output.Append(ev.Id).Append('@').Append(ev.FiredDate).Append('>').Append(ev.ExpiresDate)
                          .Append('s').Append(ev.Severity).Append('{');

                    foreach (TimelineEventEffect effect in ev.Effects)
                    {
                        output.Append(effect.EffectId).Append(':').Append(effect.Scope).Append(':')
                              .Append(effect.Magnitude.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                              .Append(effect.DurationMonths).Append(':').Append(effect.DistrictId ?? "-")
                              .Append(';');
                    }

                    output.Append('}');
                }

                output.Append("|exp=").Append(string.Join(",", tick.Expired.Select(e => e.Id)));
                output.Append("|def=").Append(string.Join(",", tick.DeferredEventIds));
                output.Append("|req=").Append(tick.EffectRequests.Count);
                output.Append('\n');

                date = date.AddMonths(1);
            }

            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(output.ToString()))).Replace("-", "");
        }
    }
}
