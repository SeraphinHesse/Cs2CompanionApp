using System.Collections.Generic;

namespace Agora.Core.Contracts
{
    /// <summary>Which catalog an event comes from (<c>politicsmodplan.md</c> §6).</summary>
    public enum EventRegion
    {
        /// <summary>Fires only in an EU-themed save.</summary>
        Eu = 0,

        /// <summary>Fires only in an NA-themed save.</summary>
        Na = 1,

        /// <summary>Fires in every save.</summary>
        Global = 2
    }

    /// <summary>How an event entered the timeline.</summary>
    public enum EventOrigin
    {
        /// <summary>Curated real history from <c>data/timeline_*.json</c>, 1990 → mid-2020s.</summary>
        Catalog = 0,

        /// <summary>Generated after the catalog ends, from a seeded archetype.</summary>
        Procedural = 1,

        /// <summary>Raised by the engine itself — e.g. unrest after a defied mandate.</summary>
        Political = 2
    }

    /// <summary>
    /// One sanctioned consequence attached to an event. Mirrors the <c>effects[]</c> entry in
    /// <c>timeline_*.json</c>.
    /// </summary>
    /// <remarks>
    /// The magnitude here is a *request*. The effect sink clamps it to the palette entry's declared
    /// cap (non-negotiable #5), and the catalog validator additionally refuses to load a magnitude
    /// outside that cap so the problem is caught at build time rather than at runtime.
    /// </remarks>
    public readonly struct TimelineEventEffect
    {
        /// <summary>Palette id, e.g. <c>"city-loan-interest"</c>. Must exist in <c>effects.perEffect</c>.</summary>
        public string EffectId { get; }

        public EffectScope Scope { get; }

        /// <summary>Requested magnitude, before severity scaling and before the cap.</summary>
        public double Magnitude { get; }

        /// <summary>Requested duration in months, before the cap.</summary>
        public int DurationMonths { get; }

        /// <summary>
        /// Target district for a district-scoped effect. Always null in catalog entries — real
        /// history does not know the player's district names — and filled in by the scheduler, which
        /// picks a target deterministically.
        /// </summary>
        public string? DistrictId { get; }

        public TimelineEventEffect(string effectId, EffectScope scope, double magnitude,
                                   int durationMonths, string? districtId = null)
        {
            EffectId = effectId;
            Scope = scope;
            Magnitude = magnitude;
            DurationMonths = durationMonths;
            DistrictId = districtId;
        }

        /// <summary>The equivalent request for <see cref="IEffectSink"/>.</summary>
        public EffectRequest ToRequest(string? sourceId) =>
            new EffectRequest(EffectId, Scope, Magnitude, DurationMonths, DistrictId, sourceId);
    }

    /// <summary>
    /// One entry in the world timeline: a real historical event, a procedurally generated one, or an
    /// engine-raised political one. The in-memory form of a <c>timeline_*.json</c> entry.
    /// </summary>
    /// <remarks>
    /// The catalog is content, not code (§6). Nothing here computes anything: an event is a date, a
    /// severity, a list of capped effect requests, and a factual brief for the LLM to write from.
    /// </remarks>
    public sealed class TimelineEvent
    {
        public int SchemaVersion { get; set; } = 1;

        /// <summary>Catalog id, lowercase kebab-case, e.g. <c>"gfc-2008"</c>. Unique across catalogs.</summary>
        public string Id { get; set; } = "";

        /// <summary>The real-world date the entry is authored against.</summary>
        public SimDate Date { get; set; }

        public EventRegion Region { get; set; } = EventRegion.Global;

        public EventOrigin Origin { get; set; } = EventOrigin.Catalog;

        /// <summary>Short factual title, ≤120 chars. Authored, not generated.</summary>
        public string Title { get; set; } = "";

        /// <summary>1–5. Drives effect scaling through <c>catalog.severityEffectScale</c>.</summary>
        public int Severity { get; set; } = 1;

        /// <summary>How long the event is politically live, in months.</summary>
        public int DurationMonths { get; set; }

        /// <summary>Effects, in authored order. Order is preserved so scaling is reproducible.</summary>
        public List<TimelineEventEffect> Effects { get; set; } = new List<TimelineEventEffect>();

        /// <summary>
        /// Terse factual brief. A *prompt input*, not published prose — the LLM writes the article.
        /// </summary>
        public string HeadlineBrief { get; set; } = "";

        /// <summary>Free tags, lowercase kebab-case, in authored order.</summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>
        /// Which issues this event pushes on, and how hard, in <c>[-1, +1]</c>. The affinity packet
        /// folds this into the event modifier term, decaying over
        /// <c>affinity.eventModifierDecayHalfLifeMonths</c>.
        /// </summary>
        public IssuePosition IssuePressure { get; set; } = IssuePosition.Centre;

        /// <summary>Archetype id when <see cref="Origin"/> is Procedural; empty otherwise.</summary>
        public string ArchetypeId { get; set; } = "";

        /// <summary>Sim date the scheduler actually fired this on. Null until it fires.</summary>
        public SimDate? FiredDate { get; set; }

        /// <summary>Sim date its effects stop applying. Null until it fires.</summary>
        public SimDate? ExpiresDate { get; set; }

        /// <summary>Local angle written by the LLM after firing. Flavor-owned; never parsed.</summary>
        public string LocalAngle { get; set; } = "";
    }
}
