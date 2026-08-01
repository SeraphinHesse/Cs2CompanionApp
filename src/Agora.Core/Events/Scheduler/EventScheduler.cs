using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Events.Scheduler
{
    /// <summary>
    /// Everything the scheduler needs to decide one tick. A plain input record — the scheduler holds no
    /// state of its own, so two calls with an equal context always produce an equal result.
    /// </summary>
    public sealed class SchedulerContext
    {
        /// <summary>Agora's own save identity (§5). Seeds every draw.</summary>
        public Guid SaveGuid { get; set; }

        /// <summary>The tick date.</summary>
        public SimDate Date { get; set; }

        /// <summary>The save's first political date. Events dated before it are history, not news.</summary>
        public SimDate StartDate { get; set; }

        /// <summary>Which regional catalog applies, from per-save settings.</summary>
        public RegionTheme Theme { get; set; } = RegionTheme.Eu;

        /// <summary>
        /// The loaded, validated catalogs (packet 11's output), in any order. Never mutated: the
        /// scheduler clones an entry when it fires it.
        /// </summary>
        public IReadOnlyList<TimelineEvent> Catalog { get; set; } = new List<TimelineEvent>();

        /// <summary>Ids already fired or written off as pre-start history. From <c>PoliticalState.FiredEventIds</c>.</summary>
        public IReadOnlyList<string> FiredEventIds { get; set; } = new List<string>();

        /// <summary>Events currently live. From <c>PoliticalState.ActiveEvents</c>.</summary>
        public IReadOnlyList<TimelineEvent> ActiveEvents { get; set; } = new List<TimelineEvent>();

        /// <summary>
        /// District ids from the current snapshot. District-scoped effects are targeted from this list;
        /// when it is empty they are dropped with a warning rather than applied city-wide.
        /// </summary>
        public IReadOnlyList<string> DistrictIds { get; set; } = new List<string>();

        /// <summary>
        /// Engine-raised events waiting to fire this tick — unrest after a defied mandate, and the like.
        /// They always fire (they are consequences, not candidates) but go through the same effect
        /// resolution and expiry machinery.
        /// </summary>
        public IReadOnlyList<TimelineEvent> PendingPolitical { get; set; } = new List<TimelineEvent>();

        /// <summary>Archetype pool for the procedural generator, or null for the built-in twelve.</summary>
        public IReadOnlyList<ProceduralArchetype>? Archetypes { get; set; }

        /// <summary>
        /// Per-save effects switch (<c>AgoraSettings.EffectsEnabled</c>). False still fires events and
        /// still resolves their effects for display — it only withholds the requests to the sink.
        /// </summary>
        public bool EffectsEnabled { get; set; } = true;
    }

    /// <summary>
    /// What the scheduler decided for one tick. Every list has a documented, stable sort order, because
    /// downstream packets fold these into engine state and a reordered list is a desync.
    /// </summary>
    public sealed class SchedulerTick
    {
        /// <summary>The tick date.</summary>
        public SimDate Date { get; set; }

        /// <summary>
        /// Events firing now, with <c>FiredDate</c>, <c>ExpiresDate</c> and resolved effects filled in.
        /// Sorted by authored date, then severity descending, then id ordinal.
        /// </summary>
        public List<TimelineEvent> Fired { get; set; } = new List<TimelineEvent>();

        /// <summary>Previously active events whose duration ran out. Sorted by id ordinal.</summary>
        public List<TimelineEvent> Expired { get; set; } = new List<TimelineEvent>();

        /// <summary>Previously active events still live. Sorted by id ordinal.</summary>
        public List<TimelineEvent> Continuing { get; set; } = new List<TimelineEvent>();

        /// <summary>
        /// <see cref="Continuing"/> ∪ <see cref="Fired"/>, sorted by id ordinal — assign straight to
        /// <c>PoliticalState.ActiveEvents</c>.
        /// </summary>
        public List<TimelineEvent> NextActiveEvents { get; set; } = new List<TimelineEvent>();

        /// <summary>
        /// Requests for the effect sink, in (event order, authored effect order). Empty when effects
        /// are switched off in tuning or in per-save settings.
        /// </summary>
        public List<EffectRequest> EffectRequests { get; set; } = new List<EffectRequest>();

        /// <summary>
        /// Ids to merge into <c>PoliticalState.FiredEventIds</c>: everything fired this tick plus every
        /// pre-start catalog entry written off as history. Sorted by id ordinal.
        /// </summary>
        public List<string> RecordedEventIds { get; set; } = new List<string>();

        /// <summary>
        /// Catalog ids held back this tick by a budget or the major-event cooldown. Purely diagnostic —
        /// they stay eligible and are re-offered next scan. Sorted by id ordinal.
        /// </summary>
        public List<string> DeferredEventIds { get; set; } = new List<string>();

        /// <summary>
        /// Catalog ids dated before the save started. They never fire; record them so the scan does not
        /// reconsider them every month. Sorted by id ordinal.
        /// </summary>
        public List<string> SkippedHistoricalIds { get; set; } = new List<string>();

        /// <summary>Non-fatal problems, in emission order. Log them; never throw on them.</summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Decides which timeline events fire on which sim date.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pure and stateless: <c>(context, tuning) → SchedulerTick</c>. Everything it needs to remember
    /// between ticks lives in <c>PoliticalState</c> — fired ids and active events — so a reload cannot
    /// desync the timeline from the politics that reacted to it (non-negotiable #6).
    /// </para>
    /// <para>
    /// AGORA-SEAM(§14.2): timeline jitter (fixed real dates vs seeded ±6 months) is an open decision.
    /// <c>catalog.jitterEnabled</c> ships false with a zero window, and the <c>event.jitter</c> stream
    /// is deliberately never drawn from here. Closing the decision means adding one date shift at the
    /// candidate-collection step and nothing else.
    /// </para>
    /// </remarks>
    public static class EventScheduler
    {
        /// <summary>
        /// Runs one tick.
        /// </summary>
        /// <param name="context">Tick inputs. Never mutated.</param>
        /// <param name="tuning">Engine tuning. Never null — pass <see cref="EngineTuning.Default"/>.</param>
        public static SchedulerTick Run(SchedulerContext context, EngineTuning tuning)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tuning == null) tuning = EngineTuning.Default;

            CatalogTuning catalog = tuning.Catalog;
            SchedulerTuning scheduler = tuning.Scheduler;

            var tick = new SchedulerTick { Date = context.Date };

            var firedIds = ToSet(context.FiredEventIds);
            List<string> districts = SortedDistricts(context.DistrictIds);

            // --- 1. Expiry, before anything fires, so a one-tick event does not block its successor.
            var active = new List<TimelineEvent>(Events(context.ActiveEvents));
            active.Sort(CompareById);

            for (int i = 0; i < active.Count; i++)
            {
                TimelineEvent a = active[i];
                if (a == null) continue;

                if (a.ExpiresDate.HasValue && context.Date >= a.ExpiresDate.Value)
                {
                    tick.Expired.Add(a);
                }
                else
                {
                    tick.Continuing.Add(a);
                }
            }

            // The most recent major event, for the cooldown. Derived rather than stored: PoliticalState
            // has no "last major event" field, and inventing one would be a contract change.
            SimDate? lastMajor = MostRecentMajor(context, firedIds, catalog);

            // --- 2. Engine-raised political events. Consequences, not candidates: they always fire.
            var political = new List<TimelineEvent>(Events(context.PendingPolitical));
            political.Sort(CompareById);

            for (int i = 0; i < political.Count; i++)
            {
                TimelineEvent p = political[i];
                if (p == null || string.IsNullOrEmpty(p.Id)) continue;
                if (firedIds.Contains(p.Id)) continue;

                TimelineEvent firedEvent = Fire(p, EventOrigin.Political, context, tuning, districts, tick.Warnings);
                tick.Fired.Add(firedEvent);
                firedIds.Add(firedEvent.Id);

                if (IsMajor(firedEvent, catalog)) lastMajor = context.Date;
            }

            // --- 3. Candidates: catalog entries that are due, then procedural generation.
            bool scanning = TickPlanner.OnInterval(context.StartDate.MonthsUntil(context.Date),
                                                   scheduler.EventScanIntervalMonths);

            if (scanning)
            {
                var candidates = new List<TimelineEvent>();
                CollectCatalogCandidates(context, tuning, firedIds, candidates, tick);
                CollectProceduralCandidates(context, tuning, firedIds, candidates);

                candidates.Sort(CompareCandidates);

                int perTick = scheduler.MaxEventsPerTick < 0 ? 0 : scheduler.MaxEventsPerTick;
                int firedFromCandidates = 0;

                for (int i = 0; i < candidates.Count; i++)
                {
                    TimelineEvent candidate = candidates[i];

                    int liveAfterThis = tick.Continuing.Count + tick.Fired.Count;
                    bool concurrencyFull = catalog.MaxConcurrentEvents >= 0
                                        && liveAfterThis >= catalog.MaxConcurrentEvents;

                    if (firedFromCandidates >= perTick || concurrencyFull)
                    {
                        Defer(tick, candidate);
                        continue;
                    }

                    if (IsMajor(candidate, catalog) && InMajorCooldown(lastMajor, context.Date, catalog))
                    {
                        Defer(tick, candidate);
                        continue;
                    }

                    TimelineEvent firedEvent = Fire(candidate, candidate.Origin, context, tuning,
                                                    districts, tick.Warnings);
                    tick.Fired.Add(firedEvent);
                    firedIds.Add(firedEvent.Id);
                    firedFromCandidates++;

                    if (IsMajor(firedEvent, catalog)) lastMajor = context.Date;
                }
            }

            // --- 4. Outputs. Sorted before they leave, every time.
            tick.Fired.Sort(CompareCandidates);

            bool effectsOn = context.EffectsEnabled && tuning.Effects.Enabled;
            if (effectsOn)
            {
                for (int i = 0; i < tick.Fired.Count; i++)
                {
                    TimelineEvent e = tick.Fired[i];
                    for (int j = 0; j < e.Effects.Count; j++)
                    {
                        tick.EffectRequests.Add(e.Effects[j].ToRequest(e.Id));
                    }
                }
            }

            tick.NextActiveEvents.AddRange(tick.Continuing);
            tick.NextActiveEvents.AddRange(tick.Fired);
            tick.NextActiveEvents.Sort(CompareById);

            for (int i = 0; i < tick.Fired.Count; i++) tick.RecordedEventIds.Add(tick.Fired[i].Id);
            tick.RecordedEventIds.AddRange(tick.SkippedHistoricalIds);
            tick.RecordedEventIds.Sort(CompareOrdinal);

            tick.DeferredEventIds.Sort(CompareOrdinal);
            tick.SkippedHistoricalIds.Sort(CompareOrdinal);

            return tick;
        }

        // --- candidate collection ------------------------------------------------------------------

        private static void CollectCatalogCandidates(SchedulerContext context, EngineTuning tuning,
                                                     HashSet<string> firedIds, List<TimelineEvent> candidates,
                                                     SchedulerTick tick)
        {
            IReadOnlyList<TimelineEvent> source = Events(context.Catalog);
            CatalogTuning catalog = tuning.Catalog;

            for (int i = 0; i < source.Count; i++)
            {
                TimelineEvent ev = source[i];
                if (ev == null || string.IsNullOrEmpty(ev.Id)) continue;
                if (firedIds.Contains(ev.Id)) continue;
                if (!ProceduralEventGenerator.RegionMatches(ev.Region, context.Theme, catalog)) continue;

                // The catalog covers 1990 → catalogEndYear. Anything dated past the end of curated
                // history is a data error, not a future event: the procedural generator owns that era.
                if (ev.Date.Year > catalog.CatalogEndYear) continue;

                // Due-ness is compared in whole months, not exact dates. The engine ticks monthly on
                // whatever day the save happens to land on, so an event authored on the 15th must fire
                // in its own month rather than slipping into the next one.
                if (ev.Date.TotalMonths < context.StartDate.TotalMonths)
                {
                    // Already history when this save began. Never fires — recorded so the scan stops
                    // reconsidering it every single month for the rest of the save.
                    tick.SkippedHistoricalIds.Add(ev.Id);
                    continue;
                }

                // AGORA-SEAM(§14.2): with jitter closed as "seeded ±6 months", the comparison date
                // would be ev.Date shifted by a StreamNames.EventJitter draw. Pinned off, so the
                // authored date is the firing date, and the jitter stream is never drawn from.
                if (ev.Date.TotalMonths > context.Date.TotalMonths) continue;

                candidates.Add(ev);
            }
        }

        private static void CollectProceduralCandidates(SchedulerContext context, EngineTuning tuning,
                                                        HashSet<string> firedIds, List<TimelineEvent> candidates)
        {
            List<TimelineEvent> generated = ProceduralEventGenerator.GenerateInternal(
                context.SaveGuid, context.Date, context.Theme, context.Archetypes, firedIds, tuning);

            for (int i = 0; i < generated.Count; i++)
            {
                if (!firedIds.Contains(generated[i].Id)) candidates.Add(generated[i]);
            }
        }

        // --- firing --------------------------------------------------------------------------------

        /// <summary>
        /// Clones the source event and fills in what only the scheduler knows: the fire date, the expiry
        /// date, and effects that are severity-scaled, capped and district-targeted.
        /// </summary>
        /// <remarks>
        /// Cloning is not politeness. The catalog list is loaded once and reused for the whole session;
        /// mutating an entry in place would make the second run of an identical tick differ from the
        /// first, which is exactly the class of bug the determinism suite exists to catch.
        /// </remarks>
        private static TimelineEvent Fire(TimelineEvent source, EventOrigin origin, SchedulerContext context,
                                          EngineTuning tuning, List<string> districts, List<string> warnings)
        {
            CatalogTuning catalog = tuning.Catalog;

            int severity = EffectResolution.ClampSeverity(source.Severity, catalog);
            int duration = EffectResolution.ClampMonths(source.DurationMonths, catalog.EffectDurationCapMonths);

            // An engine-raised event may arrive with an unset date (default(SimDate) is year 0). It
            // happened now, by definition — and the seed must not be a date that cannot exist.
            SimDate authored = source.Date.Year <= 0 ? context.Date : source.Date;

            var ev = new TimelineEvent
            {
                SchemaVersion = source.SchemaVersion,
                Id = source.Id,
                Date = authored,
                Region = source.Region,
                Origin = origin,
                Title = source.Title,
                Severity = severity,
                DurationMonths = duration,
                HeadlineBrief = source.HeadlineBrief,
                IssuePressure = source.IssuePressure.Clamped(),
                ArchetypeId = source.ArchetypeId,
                LocalAngle = source.LocalAngle,
                FiredDate = context.Date,
                ExpiresDate = context.Date.AddMonths(duration)
            };

            for (int i = 0; i < source.Tags.Count; i++) ev.Tags.Add(source.Tags[i]);

            for (int i = 0; i < source.Effects.Count; i++)
            {
                TimelineEventEffect resolved;
                if (EffectResolution.TryResolve(source.Effects[i], severity, context.SaveGuid, authored,
                                                source.Id, i, districts, tuning, warnings, out resolved))
                {
                    ev.Effects.Add(resolved);
                }
            }

            return ev;
        }

        // --- gates ---------------------------------------------------------------------------------

        private static bool IsMajor(TimelineEvent ev, CatalogTuning catalog) =>
            ev.Severity >= catalog.MajorSeverityThreshold;

        private static bool InMajorCooldown(SimDate? lastMajor, SimDate now, CatalogTuning catalog)
        {
            if (!lastMajor.HasValue) return false;
            if (catalog.MinMonthsBetweenMajorEvents <= 0) return false;
            return lastMajor.Value.MonthsUntil(now) < catalog.MinMonthsBetweenMajorEvents;
        }

        /// <summary>
        /// The date of the most recent major event, from what the state actually retains: active events
        /// carry their fire date, and a catalog entry in <c>FiredEventIds</c> fired on its authored date
        /// because jitter is off.
        /// </summary>
        private static SimDate? MostRecentMajor(SchedulerContext context, HashSet<string> firedIds,
                                                CatalogTuning catalog)
        {
            SimDate? latest = null;

            IReadOnlyList<TimelineEvent> active = Events(context.ActiveEvents);
            for (int i = 0; i < active.Count; i++)
            {
                TimelineEvent a = active[i];
                if (a == null || !a.FiredDate.HasValue) continue;
                if (!IsMajor(a, catalog)) continue;
                if (a.FiredDate.Value > context.Date) continue;
                if (!latest.HasValue || a.FiredDate.Value > latest.Value) latest = a.FiredDate.Value;
            }

            IReadOnlyList<TimelineEvent> source = Events(context.Catalog);
            for (int i = 0; i < source.Count; i++)
            {
                TimelineEvent ev = source[i];
                if (ev == null || string.IsNullOrEmpty(ev.Id)) continue;
                if (!firedIds.Contains(ev.Id)) continue;
                if (!IsMajor(ev, catalog)) continue;
                if (ev.Date.TotalMonths > context.Date.TotalMonths) continue;
                if (!latest.HasValue || ev.Date > latest.Value) latest = ev.Date;
            }

            return latest;
        }

        private static void Defer(SchedulerTick tick, TimelineEvent candidate)
        {
            // A deferred *catalog* entry is re-offered next scan: its date is still in the past and its
            // id is still unfired. A deferred *procedural* entry is simply not generated — the monthly
            // draw stands in for a queue, so the post-catalog era cannot build up a backlog.
            tick.DeferredEventIds.Add(candidate.Id);
        }

        // --- ordering ------------------------------------------------------------------------------

        /// <summary>
        /// Total order for candidates and fired events: authored date, then severity descending, then id.
        /// The id tiebreak is not decoration — <c>List.Sort</c> is unstable, so without it two events on
        /// the same date could swap between runs.
        /// </summary>
        private static int CompareCandidates(TimelineEvent a, TimelineEvent b)
        {
            int byDate = a.Date.CompareTo(b.Date);
            if (byDate != 0) return byDate;

            int bySeverity = b.Severity.CompareTo(a.Severity);
            if (bySeverity != 0) return bySeverity;

            return string.CompareOrdinal(a.Id, b.Id);
        }

        private static int CompareById(TimelineEvent a, TimelineEvent b) => string.CompareOrdinal(a.Id, b.Id);

        private static int CompareOrdinal(string a, string b) => string.CompareOrdinal(a, b);

        // --- inputs --------------------------------------------------------------------------------

        private static readonly List<TimelineEvent> NoEvents = new List<TimelineEvent>();

        /// <summary>Null-tolerant accessor. The context's list properties are settable, so a caller can
        /// null one; the scheduler treats that as empty rather than throwing mid-tick.</summary>
        private static IReadOnlyList<TimelineEvent> Events(IReadOnlyList<TimelineEvent>? list) =>
            list == null ? NoEvents : list;

        private static HashSet<string> ToSet(IReadOnlyList<string>? ids)
        {
            // Membership only. This set is never iterated — non-negotiable #3 dies quietly the day
            // someone folds a HashSet enumeration into engine output.
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (ids == null) return set;

            for (int i = 0; i < ids.Count; i++)
            {
                if (!string.IsNullOrEmpty(ids[i])) set.Add(ids[i]);
            }
            return set;
        }

        private static List<string> SortedDistricts(IReadOnlyList<string>? districtIds)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var sorted = new List<string>();

            if (districtIds != null)
            {
                for (int i = 0; i < districtIds.Count; i++)
                {
                    string id = districtIds[i];
                    if (string.IsNullOrEmpty(id)) continue;
                    if (seen.Add(id)) sorted.Add(id);
                }
            }

            sorted.Sort(CompareOrdinal);
            return sorted;
        }
    }
}
