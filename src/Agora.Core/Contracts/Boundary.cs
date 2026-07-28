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
    }
}
