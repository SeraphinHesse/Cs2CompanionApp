using System;
using System.Collections.Generic;

namespace Agora.Core.Contracts
{
    /// <summary>Which political system the save runs (<c>politicsmodplan.md</c> §3).</summary>
    public enum ElectoralSystem
    {
        /// <summary>EU theme: proportional list seats, 4–7 parties, coalitions, 1-year terms.</summary>
        Proportional = 0,

        /// <summary>NA theme: FPTP district races plus a directly elected mayor, 1-year terms.</summary>
        FirstPastThePost = 1
    }

    /// <summary>
    /// The regional flavour of the save. Selects which timeline catalogs load, which naming
    /// vocabulary the prose draws from, and — through <see cref="RegionThemeRules"/> — which
    /// electoral system the save runs. Chosen by the player at first run (§3).
    /// </summary>
    public enum RegionTheme
    {
        Eu = 0,
        Na = 1
    }

    /// <summary>
    /// What a <see cref="RegionTheme"/> implies. One function, so that the theme → system mapping
    /// cannot be spelled twice and come to disagree with itself.
    /// </summary>
    /// <remarks>
    /// It lives beside the two enums rather than in the engine because both sides of the boundary
    /// need it: <c>PoliticalEngine</c> derives <see cref="AgoraSettings.System"/> from it at save
    /// creation and at a retheme, and it is pure — no tuning, no seeds, no state.
    /// </remarks>
    public static class RegionThemeRules
    {
        /// <summary>
        /// <see cref="RegionTheme.Na"/> runs first-past-the-post district races with a directly
        /// elected mayor; everything else runs proportional list seats. There is no override: the
        /// system is a property of the theme, not a separate choice (fixplan W3).
        /// </summary>
        public static ElectoralSystem SystemFor(RegionTheme theme)
        {
            return theme == RegionTheme.Na
                ? ElectoralSystem.FirstPastThePost
                : ElectoralSystem.Proportional;
        }
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
    /// Party fields the player has taken ownership of. A locked field is never rewritten by flavor:
    /// <see cref="IFlavorProvider"/> output for it is discarded, not merged.
    ///
    /// <para>A flag set rather than loose booleans on <see cref="Party"/>: it is one string on the
    /// wire, it adds no <c>$defs</c> block to the state schema, and it matches
    /// <see cref="LlmWakeCadence"/>, the flags enum already in this contract.</para>
    ///
    /// <para><b>Field mapping — this is the specification the enforcement point is written against.</b>
    /// <see cref="NameLocked"/> covers <see cref="Party.Name"/> AND <see cref="Party.ShortName"/>;
    /// <see cref="DescriptionLocked"/> covers <see cref="Party.Description"/> AND
    /// <see cref="Party.Slogan"/>; <see cref="ColorLocked"/> covers <see cref="Party.ColorHex"/>.
    /// Every flavor-owned string on <see cref="Party"/> is accounted for by exactly one flag.</para>
    /// </summary>
    [Flags]
    public enum PartyOverrides
    {
        None = 0,
        NameLocked = 1,
        DescriptionLocked = 2,
        ColorLocked = 4
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

        /// <summary>
        /// True for one of the two dominant parties in the NA theme; false for everything else,
        /// including every party in the EU theme.
        ///
        /// <para>Engine-owned. Until this field existed, major-versus-minor
        /// was encoded only as position in <c>PartyArchetypes.NaArray</c> — the majors come first so
        /// <c>PartyRegistry.GenerateInitial</c> can take a prefix — which meant nothing downstream
        /// could ask the question. The <c>fringe</c> packet needs to, so the ordering convention is
        /// promoted to a field here.</para>
        ///
        /// <para>A brand that splits off a major is <b>not</b> a major: new parties keep the default
        /// false. In the EU theme nothing reads it — the fringe ceiling is FPTP-only — and it stays
        /// false rather than true so that a stray reader cannot change proportional behaviour.</para>
        ///
        /// <para>There are two writers, not one. <c>GenerateInitial</c> sets it at generation, and
        /// <c>NaMajorParties.Repair</c> reconciles it on every load. The second exists because
        /// generation is not the only way a registry arrives: a save restored from a sidecar written
        /// before this field existed has it reconstructed from <see cref="ArchetypeId"/>, and a save
        /// whose flags were written by a build that guessed wrong has no other route back — a
        /// migration fires at one version boundary and never again. The repair is idempotent and
        /// leaves off-ballot brands alone, so it moves the state fingerprint at most once per save.</para>
        /// </summary>
        public bool IsMajor { get; set; }

        /// <summary>
        /// Which of this party's flavor-owned fields the player has taken over. Player-owned, not
        /// engine-owned and not flavor-owned: nothing in Agora.Core writes it, and flavor must not
        /// overwrite a field whose flag is set.
        /// </summary>
        public PartyOverrides PlayerOverrides { get; set; } = PartyOverrides.None;
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
