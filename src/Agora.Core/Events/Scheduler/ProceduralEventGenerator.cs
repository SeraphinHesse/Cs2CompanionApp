using System;
using System.Collections.Generic;
using System.Globalization;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Agora.Core.Tuning;

namespace Agora.Core.Events.Scheduler
{
    /// <summary>
    /// Generates timeline events once the curated catalogs run out — the post mid-2020s world.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every draw comes from <see cref="StreamNames.EventProcedural"/>, keyed on the save GUID and the
    /// sim date, so a given save produces the same 2031 as often as you replay it, and two saves of the
    /// same city produce different ones.
    /// </para>
    /// <para>
    /// AGORA-SEAM(§14.4): the post-2026 authorship split is an open decision. This implements the
    /// proposed shape exactly — the engine picks archetype, date, severity and effects; the prose is
    /// written later by the flavor provider into <c>LocalAngle</c>. Nothing here reads LLM output, and
    /// no generated number may ever originate from one (non-negotiable #1).
    /// </para>
    /// </remarks>
    public static class ProceduralEventGenerator
    {
        /// <summary>
        /// The events generated for one date. Empty before <c>catalog.proceduralStartYear</c>, when
        /// <c>catalog.proceduralEnabled</c> is false, or when the monthly draw comes up short.
        /// </summary>
        /// <param name="saveGuid">Save identity — Agora's own GUID, not a filename (§5).</param>
        /// <param name="date">The tick date. Generated events are dated exactly here.</param>
        /// <param name="theme">The save's region theme, used to filter archetypes.</param>
        /// <param name="pool">Archetype pool, or null for <see cref="ProceduralArchetypes.CreateDefaultPool"/>.</param>
        /// <param name="firedEventIds">Ids already fired; a regenerated id is skipped, making re-ticks idempotent.</param>
        /// <param name="tuning">Engine tuning. Never null — pass <see cref="EngineTuning.Default"/>.</param>
        public static List<TimelineEvent> Generate(Guid saveGuid, SimDate date, RegionTheme theme,
                                                   IReadOnlyList<ProceduralArchetype>? pool,
                                                   IReadOnlyList<string>? firedEventIds,
                                                   EngineTuning tuning)
        {
            var fired = new HashSet<string>(StringComparer.Ordinal);
            if (firedEventIds != null)
            {
                for (int i = 0; i < firedEventIds.Count; i++)
                {
                    if (!string.IsNullOrEmpty(firedEventIds[i])) fired.Add(firedEventIds[i]);
                }
            }

            return GenerateInternal(saveGuid, date, theme, pool, fired, tuning);
        }

        internal static List<TimelineEvent> GenerateInternal(Guid saveGuid, SimDate date, RegionTheme theme,
                                                             IReadOnlyList<ProceduralArchetype>? pool,
                                                             HashSet<string>? firedEventIds, EngineTuning tuning)
        {
            var generated = new List<TimelineEvent>();

            if (tuning == null) tuning = EngineTuning.Default;
            CatalogTuning catalog = tuning.Catalog;

            if (!catalog.ProceduralEnabled) return generated;
            if (date.Year < catalog.ProceduralStartYear) return generated;

            List<ProceduralArchetype> eligible = EligiblePool(pool, theme, catalog);
            if (eligible.Count == 0) return generated;

            int count = MonthlyEventCount(saveGuid, date, tuning);
            if (count <= 0) return generated;

            for (int i = 0; i < count; i++)
            {
                // A sub-stream per slot: slot 1's archetype does not move when slot 0's draw changes.
                DeterministicRng rng = SeedStreams.RngFor(saveGuid, date, StreamNames.EventProcedural,
                                                          "gen:" + Int(i));

                ProceduralArchetype archetype = PickArchetype(eligible, catalog, rng);
                int severity = DrawSeverity(rng, catalog);

                string id = "proc-" + date.Year.ToString("D4", CultureInfo.InvariantCulture)
                          + "-" + date.Month.ToString("D2", CultureInfo.InvariantCulture)
                          + "-" + archetype.Id + "-" + Int(i + 1);

                // Already fired means this tick is a replay of one that already happened. Skipping
                // keeps re-ticking idempotent rather than duplicating history.
                if (firedEventIds != null && firedEventIds.Contains(id)) continue;

                var ev = new TimelineEvent
                {
                    Id = id,
                    Date = date,
                    Region = archetype.Region,
                    Origin = EventOrigin.Procedural,
                    Title = Truncate(archetype.Title, 120),
                    Severity = severity,
                    DurationMonths = EffectResolution.ClampMonths(archetype.DurationMonths,
                                                                  catalog.EffectDurationCapMonths),
                    HeadlineBrief = Truncate(archetype.HeadlineBrief, 300),
                    IssuePressure = archetype.IssuePressure.Clamped(),
                    ArchetypeId = archetype.Id
                };

                for (int tagIndex = 0; tagIndex < archetype.Tags.Count; tagIndex++)
                {
                    ev.Tags.Add(archetype.Tags[tagIndex]);
                }

                // Authored order, district ids still null — the scheduler resolves and targets them,
                // on exactly the same code path as a catalog event.
                for (int effectIndex = 0; effectIndex < archetype.Effects.Count; effectIndex++)
                {
                    ev.Effects.Add(archetype.Effects[effectIndex]);
                }

                generated.Add(ev);
            }

            return generated;
        }

        /// <summary>
        /// How many events this month. <c>proceduralEventsPerYear / 12</c> whole events plus a
        /// Bernoulli draw on the remainder, capped at <c>scheduler.maxEventsPerTick</c>.
        /// </summary>
        internal static int MonthlyEventCount(Guid saveGuid, SimDate date, EngineTuning tuning)
        {
            double perYear = tuning.Catalog.ProceduralEventsPerYear;
            if (double.IsNaN(perYear) || perYear <= 0.0) return 0;

            double perMonth = perYear / 12.0;
            int guaranteed = (int)Math.Floor(perMonth);
            double remainder = perMonth - guaranteed;

            DeterministicRng rng = SeedStreams.Rng(saveGuid, date, StreamNames.EventProcedural);
            int count = guaranteed + (rng.NextBool(remainder) ? 1 : 0);

            int cap = tuning.Scheduler.MaxEventsPerTick;
            if (cap < 0) cap = 0;
            return count > cap ? cap : count;
        }

        /// <summary>
        /// Archetypes this theme can see, sorted by id and truncated to
        /// <c>catalog.proceduralArchetypeCount</c>. Sorting first means the truncation is stable
        /// whatever order the pool was built in.
        /// </summary>
        internal static List<ProceduralArchetype> EligiblePool(IReadOnlyList<ProceduralArchetype>? pool,
                                                               RegionTheme theme, CatalogTuning catalog)
        {
            IReadOnlyList<ProceduralArchetype> source = pool ?? ProceduralArchetypes.CreateDefaultPool();

            var eligible = new List<ProceduralArchetype>();
            for (int i = 0; i < source.Count; i++)
            {
                ProceduralArchetype a = source[i];
                if (a == null || string.IsNullOrEmpty(a.Id)) continue;
                if (!RegionMatches(a.Region, theme, catalog)) continue;
                eligible.Add(a);
            }

            eligible.Sort(CompareArchetypes);

            int max = catalog.ProceduralArchetypeCount;
            if (max > 0 && eligible.Count > max) eligible.RemoveRange(max, eligible.Count - max);

            return eligible;
        }

        /// <summary>Whether an event or archetype from <paramref name="region"/> can fire in this save.</summary>
        internal static bool RegionMatches(EventRegion region, RegionTheme theme, CatalogTuning catalog)
        {
            switch (region)
            {
                case EventRegion.Global: return catalog.IncludeGlobal;
                case EventRegion.Eu: return theme == RegionTheme.Eu;
                case EventRegion.Na: return theme == RegionTheme.Na;
                default: return false;
            }
        }

        private static ProceduralArchetype PickArchetype(List<ProceduralArchetype> eligible,
                                                         CatalogTuning catalog, DeterministicRng rng)
        {
            double total = 0.0;
            for (int i = 0; i < eligible.Count; i++) total += WeightOf(eligible[i], catalog);

            // Degenerate weights must not produce a null pick or a biased one. Fall back to uniform.
            if (double.IsNaN(total) || total <= 0.0) return eligible[rng.NextInt(0, eligible.Count)];

            double draw = rng.NextDouble() * total;
            double cumulative = 0.0;

            for (int i = 0; i < eligible.Count; i++)
            {
                cumulative += WeightOf(eligible[i], catalog);
                if (draw < cumulative) return eligible[i];
            }

            return eligible[eligible.Count - 1];
        }

        private static double WeightOf(ProceduralArchetype archetype, CatalogTuning catalog)
        {
            double w = archetype.Weight;
            if (double.IsNaN(w) || w <= 0.0) return 0.0;

            double regionWeight = archetype.Region == EventRegion.Global
                ? catalog.GlobalEventWeight
                : catalog.RegionalEventWeight;

            if (double.IsNaN(regionWeight) || regionWeight <= 0.0) return 0.0;
            return w * regionWeight;
        }

        private static int DrawSeverity(DeterministicRng rng, CatalogTuning catalog)
        {
            double sigma = catalog.ProceduralSeveritySigma;
            if (double.IsNaN(sigma) || sigma < 0.0) sigma = 0.0;

            double raw = catalog.ProceduralSeverityMean + sigma * rng.NextGaussian();

            // Away-from-zero rounding, not Math.Round's banker's rounding: a severity that halves in
            // frequency because 2.5 rounds to 2 is a tuning surprise nobody would look for.
            int severity = (int)Math.Floor(raw + 0.5);
            return EffectResolution.ClampSeverity(severity, catalog);
        }

        private static int CompareArchetypes(ProceduralArchetype a, ProceduralArchetype b) =>
            string.CompareOrdinal(a.Id, b.Id);

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= max ? value : value.Substring(0, max);
        }
    }
}
