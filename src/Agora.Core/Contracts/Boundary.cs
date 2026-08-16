using System;
using System.Collections.Generic;

namespace Agora.Core.Contracts
{
    /// <summary>
    /// The boundary between Agora.Core and the game. Agora.Mod implements these; Agora.Core only ever
    /// sees the interfaces. This is the seam that keeps the engine testable without Unity.
    /// </summary>
    public interface IClock
    {
        /// <summary>The current political date. Backed by <c>AgoraTimeService</c> (non-negotiable #8).</summary>
        SimDate Today { get; }
    }

    /// <summary>Supplies the engine with the city's measured state. Implemented by the sensor layer.</summary>
    public interface ISnapshotSource
    {
        CitySnapshot Capture();
    }

    /// <summary>
    /// Applies sanctioned effects to the game. Implemented in Agora.Mod/Effects.
    /// </summary>
    /// <remarks>
    /// The sink is responsible for enforcing caps — the engine may request anything, and the sink
    /// clamps it (non-negotiable #5). An effect with no available implementation degrades to its
    /// declared fallback rather than being dropped.
    /// </remarks>
    public interface IEffectSink
    {
        void Apply(EffectRequest request);
    }

    /// <summary>
    /// Supplies prose. Never numbers (non-negotiable #1).
    /// </summary>
    /// <remarks>
    /// Behind this interface from day one so the Claude CLI provider can be swapped for a
    /// pregenerated content pool later without touching the engine. Implementations must fail
    /// closed: on timeout or malformed output, return the last good value and log (#7).
    /// </remarks>
    public interface IFlavorProvider
    {
        /// <summary>Returns null when no fresh flavor is available. Callers keep their last good state.</summary>
        FlavorPayload? TryGetFlavor(CitySnapshot snapshot, SimDate date);
    }

    /// <summary>Scope of an effect. Mirrors the game's own district/city modifier split.</summary>
    public enum EffectScope
    {
        City,
        District
    }

    /// <summary>
    /// A request to apply one sanctioned effect. Magnitude and duration are requests, not guarantees:
    /// the sink clamps both to the palette entry's declared caps.
    /// </summary>
    public readonly struct EffectRequest
    {
        public string EffectId { get; }
        public EffectScope Scope { get; }

        /// <summary>Target district, or null for city scope.</summary>
        public string? DistrictId { get; }

        public double Magnitude { get; }
        public int DurationMonths { get; }

        /// <summary>Event or mandate that caused this, for the news feed and for debugging.</summary>
        public string? SourceId { get; }

        public EffectRequest(string effectId, EffectScope scope, double magnitude,
                             int durationMonths, string? districtId = null, string? sourceId = null)
        {
            if (string.IsNullOrEmpty(effectId))
                throw new ArgumentException("Effect id must not be empty.", nameof(effectId));
            if (scope == EffectScope.District && string.IsNullOrEmpty(districtId))
                throw new ArgumentException("District-scoped effects require a district id.", nameof(districtId));

            EffectId = effectId;
            Scope = scope;
            Magnitude = magnitude;
            DurationMonths = durationMonths;
            DistrictId = districtId;
            SourceId = sourceId;
        }
    }

    /// <summary>
    /// Prose from the flavor provider. Every field is text, an id or a date — by design.
    /// </summary>
    /// <remarks>
    /// If a numeric field ever appears on this type or its members, that is a non-negotiable #1
    /// violation and the schema suite fails the build.
    /// </remarks>
    public sealed class FlavorPayload
    {
        public int SchemaVersion { get; set; } = 1;
        public SimDate GeneratedAt { get; set; }
        public List<PartyFlavor> Parties { get; set; } = new List<PartyFlavor>();
        public List<Article> Articles { get; set; } = new List<Article>();

        /// <summary>Opening prose for live stories, keyed by story id.</summary>
        public List<StoryProse> Stories { get; set; } = new List<StoryProse>();

        /// <summary>Closing prose for stories that have resolved, keyed by the same story id.</summary>
        public List<StoryProse> Resolutions { get; set; } = new List<StoryProse>();

        /// <summary>
        /// Per-event local colour, destined for <see cref="TimelineEvent.LocalAngle"/>.
        /// </summary>
        /// <remarks>
        /// On the payload rather than only on the provider's document because it was parsed,
        /// validated and cached for three milestones and then reached no surface at all — nothing
        /// copied it onto the event, so every <c>localAngle</c> the model ever wrote was discarded
        /// one step from being shown. The write-back is in <c>AgoraRuntime.CollectProse</c>; this
        /// field is what gives it something to write from.
        /// </remarks>
        public List<EventProse> EventProse { get; set; } = new List<EventProse>();
    }

    /// <summary>Which writer produced a piece of prose.</summary>
    /// <remarks>
    /// Carried so a consumer can show both. The canned pool answers every poll and the model answers
    /// only on a wake, so a consumer that kept "the latest" would have pool prose overwrite the
    /// model's within a month of it arriving — and one that kept "the best" would rewrite a headline
    /// under a player who had already read it. Neither replaces the other: the pool's text is what
    /// the card was opened with and stays, and the model's is added beside it when it lands.
    /// </remarks>
    public enum ProseSource
    {
        /// <summary>The canned static pool. Always available, deterministic, never absent.</summary>
        Pool = 0,

        /// <summary>The LLM. Present only after a wake that succeeded.</summary>
        Cli = 1
    }

    /// <summary>
    /// One story's prose. Prose and an id, and nothing else — non-negotiable #1 is why this type has
    /// no severity, no score and no count on it.
    /// </summary>
    public sealed class StoryProse
    {
        public string StoryId { get; set; } = "";
        public string Headline { get; set; } = "";
        public string Article { get; set; } = "";

        /// <summary>
        /// Who wrote it. Never round-tripped through the LLM schema — <c>politics_flavor.json</c>
        /// has no such field, and the model is not asked to identify itself. It is stamped by
        /// whichever provider produced the document.
        /// </summary>
        public ProseSource Source { get; set; } = ProseSource.Pool;
    }

    /// <summary>One event's local angle. Prose and an id.</summary>
    public sealed class EventProse
    {
        public string EventId { get; set; } = "";
        public string LocalAngle { get; set; } = "";
        public ProseSource Source { get; set; } = ProseSource.Pool;
    }

    public sealed class PartyFlavor
    {
        public string PartyId { get; set; } = "";
        public string Name { get; set; } = "";
        public string ShortName { get; set; } = "";
        public string Description { get; set; } = "";
        public string Slogan { get; set; } = "";
    }

    public sealed class Article
    {
        public string Id { get; set; } = "";
        public string Outlet { get; set; } = "";
        public string Headline { get; set; } = "";
        public string Body { get; set; } = "";
        public string Tone { get; set; } = "";

        // What the article is about. These are catalog ids echoed back by the model and checked
        // against FlavorCatalog before they reach this type, so an id naming nothing legal never
        // gets here. Identifiers, never numbers - non-negotiable #1 is untouched.
        public string EventId { get; set; } = "";
        public string DistrictId { get; set; } = "";
        public string PartyId { get; set; } = "";
    }
}
