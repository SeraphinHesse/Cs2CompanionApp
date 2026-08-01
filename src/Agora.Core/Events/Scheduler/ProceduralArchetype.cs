using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Events.Scheduler
{
    /// <summary>
    /// A template the procedural generator instantiates once the curated catalogs run out
    /// (<c>catalog.catalogEndYear</c> → <c>catalog.proceduralStartYear</c>).
    ///
    /// <para>
    /// An archetype is <b>content</b>, not tuning: it is the same kind of thing as an entry in
    /// <c>timeline_*.json</c> — a title, a factual brief, a set of capped effect requests and an issue
    /// pressure vector. The <i>coefficients</i> that govern how often archetypes fire and how severe
    /// they get all live in <c>engine_tuning.json</c> under <c>catalog.procedural*</c>.
    /// </para>
    ///
    /// <para>
    /// AGORA-SEAM(§14.4): the post-2026 authorship split is an open decision. This type implements the
    /// proposed shape and nothing beyond it — the engine picks the archetype, the severity, the date
    /// and the effects; every string here is a <i>prompt input</i>, and the published prose is written
    /// later by <see cref="IFlavorProvider"/> into <see cref="TimelineEvent.LocalAngle"/>. No number on
    /// a generated event ever comes from the LLM (non-negotiable #1).
    /// </para>
    /// </summary>
    public sealed class ProceduralArchetype
    {
        /// <summary>Stable kebab-case id. Becomes part of the generated event id and its seed.</summary>
        public string Id { get; set; } = "";

        /// <summary>Short factual title, ≤120 chars. Authored, never generated.</summary>
        public string Title { get; set; } = "";

        /// <summary>Terse factual brief, ≤300 chars. A prompt input, not published prose.</summary>
        public string HeadlineBrief { get; set; } = "";

        /// <summary>Which themes this archetype is eligible in.</summary>
        public EventRegion Region { get; set; } = EventRegion.Global;

        /// <summary>
        /// Relative selection weight, multiplied by <c>catalog.globalEventWeight</c> or
        /// <c>catalog.regionalEventWeight</c> depending on <see cref="Region"/>.
        /// </summary>
        public double Weight { get; set; } = 1.0;

        /// <summary>How long the generated event stays politically live, before the duration cap.</summary>
        public int DurationMonths { get; set; } = 12;

        /// <summary>Free tags, lowercase kebab-case, in authored order.</summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>Which issues the generated event pushes on, in [-1, +1].</summary>
        public IssuePosition IssuePressure { get; set; } = IssuePosition.Centre;

        /// <summary>
        /// Effects in authored order, with <c>DistrictId</c> always null — exactly like a catalog
        /// entry. The scheduler scales them for severity, clamps them and picks district targets.
        /// </summary>
        public List<TimelineEventEffect> Effects { get; set; } = new List<TimelineEventEffect>();
    }

    /// <summary>
    /// The built-in archetype pool. Twelve entries, matching the default
    /// <c>catalog.proceduralArchetypeCount</c>.
    /// </summary>
    /// <remarks>
    /// Every <c>effectId</c> below is a member of the closed palette registry
    /// (<c>effects.perEffect</c>). Base magnitudes are deliberately set under the per-effect caps so
    /// that severity scaling has somewhere to go: a severity-5 instance reaches the cap and is clamped
    /// there, a severity-1 instance is a nudge.
    /// </remarks>
    public static class ProceduralArchetypes
    {
        /// <summary>
        /// A fresh pool. Returns a new list of new objects on every call on purpose — the archetype
        /// objects are mutable, and a shared static instance that some caller edited would change
        /// generated history silently.
        /// </summary>
        public static List<ProceduralArchetype> CreateDefaultPool()
        {
            var pool = new List<ProceduralArchetype>
            {
                Make("energy-price-shock",
                    "Regional energy price shock",
                    "Wholesale energy prices spike across the region; household bills and municipal utility costs follow.",
                    new[] { "energy", "economy", "cost-of-living" },
                    new IssuePosition(0.20, 0.70, 0.30, 0.10, -0.20, 0.00),
                    18,
                    new[]
                    {
                        Eff("city-import-cost", EffectScope.City, 0.10, 18),
                        Eff("district-energy-awareness", EffectScope.District, 0.08, 12)
                    }),

                Make("credit-crunch",
                    "Credit tightening hits municipal borrowing",
                    "Lending standards tighten and municipal borrowing costs rise; capital projects are repriced.",
                    new[] { "finance", "economy", "budget" },
                    new IssuePosition(-0.30, 0.50, 0.00, -0.10, -0.50, 0.10),
                    24,
                    new[]
                    {
                        Eff("city-loan-interest", EffectScope.City, 0.12, 24),
                        Eff("city-service-building-upkeep", EffectScope.City, 0.07, 18)
                    }),

                Make("industrial-downturn",
                    "Industrial downturn across the region",
                    "Regional industrial orders fall; plants run below capacity and layoffs are announced.",
                    new[] { "industry", "employment", "economy" },
                    new IssuePosition(0.30, 0.40, -0.20, 0.00, -0.60, 0.00),
                    18,
                    new[]
                    {
                        Eff("city-industrial-efficiency", EffectScope.City, -0.10, 18),
                        Eff("city-export-cost", EffectScope.City, 0.08, 12)
                    }),

                Make("tech-boom",
                    "Regional technology investment boom",
                    "A wave of technology investment reaches the region; office demand and in-migration rise.",
                    new[] { "technology", "growth", "jobs" },
                    new IssuePosition(-0.10, 0.20, -0.10, 0.20, 0.60, -0.20),
                    24,
                    new[]
                    {
                        Eff("city-office-efficiency", EffectScope.City, 0.10, 18),
                        Eff("city-attractiveness", EffectScope.City, 0.09, 24)
                    }),

                Make("public-health-scare",
                    "Regional public health scare",
                    "A communicable illness spreads through the region; clinics report queues and staff shortages.",
                    new[] { "health", "services" },
                    new IssuePosition(0.70, 0.10, 0.30, -0.20, -0.20, 0.20),
                    12,
                    new[]
                    {
                        Eff("city-disease-probability", EffectScope.City, 0.09, 12),
                        Eff("city-hospital-efficiency", EffectScope.City, -0.07, 12)
                    }),

                Make("crime-wave",
                    "Organised crime wave",
                    "Reported offences climb sharply; police report an organised network operating across neighbourhoods.",
                    new[] { "crime", "safety", "order" },
                    new IssuePosition(0.30, 0.00, 0.00, 0.00, -0.10, 0.70),
                    18,
                    new[]
                    {
                        Eff("city-crime-probability", EffectScope.City, 0.10, 18),
                        Eff("district-crime-accumulation", EffectScope.District, 0.10, 12)
                    }),

                Make("heatwave-drought",
                    "Prolonged heatwave and drought",
                    "A record heatwave and rainfall deficit strain water supply, health services and the grid.",
                    new[] { "climate", "environment", "health" },
                    new IssuePosition(0.40, 0.20, 0.70, 0.00, -0.20, 0.00),
                    9,
                    new[]
                    {
                        Eff("city-pollution-health-affect", EffectScope.City, 0.09, 9),
                        Eff("city-disaster-damage-rate", EffectScope.City, 0.08, 9),
                        Eff("district-building-fire-hazard", EffectScope.District, 0.08, 9)
                    }),

                Make("transit-strike",
                    "Regional transit dispute",
                    "A pay dispute halts scheduled services intermittently; commuters shift to cars and bikes.",
                    new[] { "transit", "labour", "commute" },
                    new IssuePosition(0.30, 0.30, 0.20, 0.70, -0.20, 0.00),
                    6,
                    new[]
                    {
                        Eff("city-taxi-starting-fee", EffectScope.City, 0.10, 6),
                        Eff("district-bike-probability", EffectScope.District, 0.09, 6)
                    }),

                Make("housing-squeeze",
                    "Housing cost squeeze",
                    "Housing costs outrun incomes across the region; maintenance backlogs and arrears both grow.",
                    new[] { "housing", "cost-of-living" },
                    new IssuePosition(0.40, 0.80, 0.00, 0.10, 0.20, -0.10),
                    24,
                    new[]
                    {
                        // AGORA-SEAM(§7 gap / §14.6): there is deliberately no rent or land-value effect
                        // in the palette. Until that gap is resolved this archetype expresses the squeeze
                        // through upkeep and tax sentiment, which are enum-backed, rather than inventing one.
                        Eff("district-building-upkeep", EffectScope.District, 0.08, 18),
                        Eff("city-tax-happiness", EffectScope.City, -0.06, 18)
                    }),

                Make("waste-crisis",
                    "Waste handling crisis",
                    "Waste contracts fail across the region; collection slips and industrial waste backs up.",
                    new[] { "waste", "environment", "services" },
                    new IssuePosition(0.60, 0.10, 0.60, 0.00, -0.10, 0.20),
                    12,
                    new[]
                    {
                        Eff("city-industrial-garbage", EffectScope.City, 0.09, 12),
                        Eff("district-garbage-production", EffectScope.District, 0.09, 12)
                    }),

                Make("university-expansion",
                    "Regional university expansion programme",
                    "A funded expansion adds places and research capacity at regional universities.",
                    new[] { "education", "growth" },
                    new IssuePosition(0.60, -0.10, 0.00, 0.10, 0.40, -0.10),
                    36,
                    new[]
                    {
                        Eff("city-university-interest", EffectScope.City, 0.09, 36),
                        Eff("city-college-graduation", EffectScope.City, 0.08, 36)
                    }),

                Make("commodity-boom",
                    "Commodity extraction boom",
                    "High commodity prices restart regional extraction; output rises and so does industrial pollution.",
                    new[] { "resources", "industry", "environment" },
                    new IssuePosition(-0.20, -0.20, 0.60, 0.00, 0.50, 0.00),
                    24,
                    new[]
                    {
                        Eff("city-ore-resource-amount", EffectScope.City, 0.09, 24),
                        Eff("city-industrial-air-pollution", EffectScope.City, 0.08, 18)
                    })
            };

            return pool;
        }

        private static ProceduralArchetype Make(string id, string title, string brief, string[] tags,
                                                IssuePosition pressure, int durationMonths,
                                                TimelineEventEffect[] effects)
        {
            var a = new ProceduralArchetype
            {
                Id = id,
                Title = title,
                HeadlineBrief = brief,
                Region = EventRegion.Global,
                Weight = 1.0,
                DurationMonths = durationMonths,
                IssuePressure = pressure.Clamped()
            };

            for (int i = 0; i < tags.Length; i++) a.Tags.Add(tags[i]);
            for (int i = 0; i < effects.Length; i++) a.Effects.Add(effects[i]);
            return a;
        }

        private static TimelineEventEffect Eff(string effectId, EffectScope scope, double magnitude, int months) =>
            new TimelineEventEffect(effectId, scope, magnitude, months);
    }
}
