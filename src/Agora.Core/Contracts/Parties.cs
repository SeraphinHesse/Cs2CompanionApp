using System.Collections.Generic;

namespace Agora.Core.Contracts
{
    /// <summary>Which political system the save runs (<c>politicsmodplan.md</c> §3).</summary>
    public enum ElectoralSystem
    {
        /// <summary>EU theme: proportional list seats, 4–7 parties, coalitions, 3-year terms.</summary>
        Proportional = 0,

        /// <summary>NA theme: FPTP district races plus a directly elected mayor, 4-year terms.</summary>
        FirstPastThePost = 1
    }

    /// <summary>
    /// The regional flavour of the save. Selects which timeline catalogs load and which electoral
    /// system is the default; overridable in per-save settings (§3).
    /// </summary>
    public enum RegionTheme
    {
        Eu = 0,
        Na = 1
    }

    /// <summary>
    /// Lifecycle state of a party. EU parties split, merge, die below 3% across two consecutive
    /// elections, and may revive if their core grievance resurges (§3).
    /// </summary>
    public enum PartyStatus
    {
        /// <summary>Contesting elections.</summary>
        Active = 0,

        /// <summary>Below threshold once. One more and it dies. Still on the ballot.</summary>
        Endangered = 1,

        /// <summary>Off the ballot. The brand persists so it can revive.</summary>
        Dissolved = 2,

        /// <summary>Absorbed into <see cref="Party.SuccessorPartyId"/>.</summary>
        Merged = 3,

        /// <summary>A dissolved brand that returned. Active for all electoral purposes.</summary>
        Revived = 4
    }

    /// <summary>Lifecycle state of a faction inside a party (NA theme, §3).</summary>
    public enum FactionStatus
    {
        Active = 0,
        Endangered = 1,
        Dissolved = 2,
        Merged = 3,
        Revived = 4
    }

    /// <summary>
    /// A political party. Every number on this type is engine-computed; every string that reads as
    /// prose is flavor, supplied by <see cref="IFlavorProvider"/> and never fed back into a
    /// calculation (non-negotiable #1).
    /// </summary>
    /// <remarks>
    /// <see cref="Id"/> is engine-generated, stable for the life of the save, and the entity id used
    /// in seeded sub-streams. Renaming a party changes <see cref="Name"/>, never <see cref="Id"/>.
    /// </remarks>
    public sealed class Party
    {
        /// <summary>Stable id, e.g. <c>"party-03"</c>. Lowercase, no spaces. Engine-owned.</summary>
        public string Id { get; set; } = "";

        /// <summary>Display name. Flavor-owned — placeholder until the LLM names it (M3).</summary>
        public string Name { get; set; } = "";

        /// <summary>Short name for seat charts, ≤12 chars. Flavor-owned.</summary>
        public string ShortName { get; set; } = "";

        /// <summary>One-paragraph description. Flavor-owned.</summary>
        public string Description { get; set; } = "";

        /// <summary>Campaign slogan. Flavor-owned.</summary>
        public string Slogan { get; set; } = "";

        /// <summary>
        /// Hex colour <c>#RRGGBB</c> for charts and the map overlay. Engine-assigned from a fixed
        /// palette so the same party is the same colour across reloads.
        /// </summary>
        public string ColorHex { get; set; } = "#808080";

        /// <summary>
        /// Archetype id the party was generated from, e.g. <c>"green"</c>, <c>"labour"</c>,
        /// <c>"liberal"</c>, <c>"conservative"</c>, <c>"populist"</c>, <c>"localist"</c>. Drives the
        /// initial platform and the flavor prompt. Engine-owned.
        /// </summary>
        public string ArchetypeId { get; set; } = "";

        /// <summary>The party's current stance on each issue. Refreshed each campaign.</summary>
        public IssuePosition Platform { get; set; } = IssuePosition.Centre;

        /// <summary>The platform it ran on at the last election. Mandates are generated from this.</summary>
        public IssuePosition LastManifesto { get; set; } = IssuePosition.Centre;

        public PartyStatus Status { get; set; } = PartyStatus.Active;

        public SimDate FoundedDate { get; set; }

        /// <summary>Set when <see cref="Status"/> becomes Dissolved or Merged.</summary>
        public SimDate? DissolvedDate { get; set; }

        /// <summary>Vote share at the most recent election, 0–1. Zero before the first election.</summary>
        public double LastVoteShare { get; set; }

        /// <summary>Seats currently held. Zero outside the Proportional/FPTP council.</summary>
        public int SeatsHeld { get; set; }

        /// <summary>True while the party leads the government (coalition lead, or holds the mayoralty).</summary>
        public bool IsIncumbent { get; set; }

        /// <summary>True while the party sits in government without leading it.</summary>
        public bool IsInGovernment { get; set; }

        /// <summary>
        /// Consecutive elections below <c>parties.deathVoteShareThreshold</c>. Reaching
        /// <c>parties.deathConsecutiveElections</c> dissolves the party.
        /// </summary>
        public int ConsecutiveElectionsBelowThreshold { get; set; }

        /// <summary>Party this one split from, if any.</summary>
        public string? PredecessorPartyId { get; set; }

        /// <summary>Party this one merged into, if <see cref="Status"/> is Merged.</summary>
        public string? SuccessorPartyId { get; set; }

        /// <summary>Faction ids, sorted ascending. Populated in the NA theme; usually empty in EU.</summary>
        public List<string> FactionIds { get; set; } = new List<string>();

        /// <summary>
        /// The issue whose grievance the brand owns. A dissolved party revives when this issue's
        /// city-wide grievance passes <c>parties.revivalGrievanceThreshold</c>.
        /// </summary>
        public Issue CoreGrievance { get; set; } = Issue.Services;

        /// <summary>Number of times this brand has revived. Used for revival cooldown and prose.</summary>
        public int RevivalCount { get; set; }
    }

    /// <summary>
    /// A faction inside a party (NA theme). Factions have their own demographic support, demands and
    /// leader; the dominant faction writes the party platform each cycle (§3).
    /// </summary>
    public sealed class Faction
    {
        /// <summary>Stable id, e.g. <c>"faction-07"</c>. Engine-owned.</summary>
        public string Id { get; set; } = "";

        /// <summary>Owning party. Never empty — a faction cannot outlive its party unassigned.</summary>
        public string PartyId { get; set; } = "";

        /// <summary>Display name. Flavor-owned.</summary>
        public string Name { get; set; } = "";

        /// <summary>Short name, ≤12 chars. Flavor-owned.</summary>
        public string ShortName { get; set; } = "";

        /// <summary>Description. Flavor-owned.</summary>
        public string Description { get; set; } = "";

        /// <summary>Leader's name. Flavor-owned; the engine only tracks that a change happened.</summary>
        public string LeaderName { get; set; } = "";

        /// <summary>Archetype id the faction was generated from. Engine-owned.</summary>
        public string ArchetypeId { get; set; } = "";

        /// <summary>The faction's own stance. Blended into the party platform by dominance weight.</summary>
        public IssuePosition Platform { get; set; } = IssuePosition.Centre;

        /// <summary>Share of the party's support base, 0–1. Factions of one party sum to 1.</summary>
        public double InternalSupport { get; set; }

        /// <summary>True for the faction that writes the platform this cycle.</summary>
        public bool IsDominant { get; set; }

        /// <summary>
        /// Distance between this faction's platform and its party's, 0–1. Passing
        /// <c>factions.internalTensionThreshold</c> makes a split possible.
        /// </summary>
        public double TensionWithParty { get; set; }

        public FactionStatus Status { get; set; } = FactionStatus.Active;

        public SimDate FoundedDate { get; set; }

        public SimDate? DissolvedDate { get; set; }

        /// <summary>Faction this one split from, if any.</summary>
        public string? PredecessorFactionId { get; set; }

        /// <summary>Faction this one merged into, if <see cref="Status"/> is Merged.</summary>
        public string? SuccessorFactionId { get; set; }

        /// <summary>
        /// The issues this faction demands the party act on, in declaration order. Length is capped
        /// by <c>factions.demandCountPerFaction</c>.
        /// </summary>
        public List<Issue> Demands { get; set; } = new List<Issue>();

        /// <summary>
        /// Bloc keys this faction draws its support from, sorted by <see cref="BlocKey.Ordinal"/>.
        /// Drives which grievances raise its <see cref="InternalSupport"/>.
        /// </summary>
        public List<BlocKey> CoreBlocs { get; set; } = new List<BlocKey>();

        /// <summary>Consecutive lifecycle cycles below <c>factions.deathSupportThreshold</c>.</summary>
        public int ConsecutiveCyclesBelowThreshold { get; set; }

        /// <summary>The grievance this faction owns, for revival checks.</summary>
        public Issue CoreGrievance { get; set; } = Issue.Services;
    }
}
