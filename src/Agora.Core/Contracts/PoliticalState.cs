using System;
using System.Collections.Generic;

namespace Agora.Core.Contracts
{
    /// <summary>
    /// Derived indices computed from a snapshot (<c>politicsmodplan.md</c> §6). All are normalised
    /// to <c>[0, 1]</c> unless the doc comment says otherwise, so the dashboard can render any of
    /// them on a shared scale.
    /// </summary>
    public sealed class DerivedIndices
    {
        /// <summary>Wealth inequality across households, 0 (equal) – 1 (maximally unequal).</summary>
        public double GiniCoefficient { get; set; }

        /// <summary>Net loss of highly educated residents, 0 – 1. Higher is worse.</summary>
        public double BrainDrainIndex { get; set; }

        /// <summary>Spread of service coverage between districts, 0 (even) – 1 (severely uneven).</summary>
        public double ServiceInequalityIndex { get; set; }

        /// <summary>Commute pain relative to <c>indices.commuteMiseryReferenceMinutes</c>, 0 – 1.</summary>
        public double CommuteMiseryIndex { get; set; }

        /// <summary>Dispersion of vote share across parties, 0 (one party) – 1 (fragmented).</summary>
        public double PolarizationIndex { get; set; }

        /// <summary>
        /// Confidence in the political system, 0 – 1, from turnout, mandate delivery and government
        /// stability. Low legitimacy raises the odds of unrest events.
        /// </summary>
        public double LegitimacyIndex { get; set; }

        /// <summary>City-wide discontent, 0 – 1. The turnout and affinity packets both read it.</summary>
        public double DiscontentIndex { get; set; }

        /// <summary>Per-district indices, sorted by district id.</summary>
        public List<DistrictIndices> Districts { get; set; } = new List<DistrictIndices>();
    }

    /// <summary>Per-district derived indices. Same normalisation rules as <see cref="DerivedIndices"/>.</summary>
    public sealed class DistrictIndices
    {
        public string DistrictId { get; set; } = "";

        /// <summary>Rent, education and turnover rising together, 0 – 1.</summary>
        public double GentrificationIndex { get; set; }

        public double CommuteMiseryIndex { get; set; }

        /// <summary>Mean service coverage in this district, 0 – 1. Higher is better.</summary>
        public double ServiceCoverageIndex { get; set; }

        public double DiscontentIndex { get; set; }

        /// <summary>Local wealth inequality, 0 – 1.</summary>
        public double GiniCoefficient { get; set; }

        /// <summary>True when any input to these numbers was a city-wide fallback.</summary>
        public bool HasCityFallbacks { get; set; }
    }

    /// <summary>
    /// When the flavor provider is allowed to wake (§3 Cadence). Flags, so a save can enable any
    /// combination. Stored per save, never globally (non-negotiable #10).
    /// </summary>
    [Flags]
    public enum LlmWakeCadence
    {
        None = 0,
        Yearly = 1,
        Election = 2,
        Manual = 4,
        Default = Yearly | Election | Manual
    }

    /// <summary>
    /// Per-save settings. Lives in the sidecar, not in global config (non-negotiable #10). The only
    /// exceptions are the master toggle and anything that must work before a save exists — those
    /// stay in the mod's own options page.
    /// </summary>
    public sealed class AgoraSettings
    {
        public int SchemaVersion { get; set; } = 2;

        /// <summary>Political start year. Default 1990, chosen at save creation, locked afterward (§3).</summary>
        public int StartYear { get; set; } = 1990;

        /// <summary>
        /// The save's region. Chosen by the player at first run and locked at the first election
        /// (<see cref="ThemeLocked"/>). <c>Eu</c> is the initialiser value, not a decision: a save
        /// that has never answered the first-run prompt reads as EU until it does.
        /// </summary>
        public RegionTheme Theme { get; set; } = RegionTheme.Eu;

        /// <summary>
        /// Derived from <see cref="Theme"/>, unconditionally, by
        /// <see cref="RegionThemeRules.SystemFor"/>. There is no player override: this is a mirror of
        /// the theme, kept as its own field because every election path reads it, and writing it to
        /// anything else desynchronises the ballot from the parties contesting it.
        /// </summary>
        public ElectoralSystem System { get; set; } = ElectoralSystem.Proportional;

        public LlmWakeCadence WakeCadence { get; set; } = LlmWakeCadence.Default;

        /// <summary>
        /// How many <c>state_*.json</c> snapshots to keep per save.
        /// </summary>
        /// <remarks>
        /// AGORA-SEAM(§14.3): the retention default (proposed 25) is an open decision. The value is
        /// read from <c>scheduler.snapshotRetention</c>; do not implement a pruning policy beyond
        /// "keep the newest N" until it closes.
        /// </remarks>
        public int SnapshotRetention { get; set; } = 25;

        /// <summary>Master enable for the political layer within this save.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Kill switch for the effect layer. When false the engine still computes politics and the
        /// dashboard still renders it, but nothing is applied to the city.
        /// </summary>
        public bool EffectsEnabled { get; set; } = true;

        /// <summary>
        /// True once the region theme is history. Set at the first election; before that the player
        /// may still change their mind from the settings surface.
        /// </summary>
        public bool ThemeLocked { get; set; } = false;

        /// <summary>
        /// Pause the sim and raise a modal when a major news item lands — elections, coalition
        /// formation or collapse, party founding or dissolution, timeline events at severity >= 3.
        /// Default on.
        /// </summary>
        public bool PauseOnMajorNews { get; set; } = true;

        /// <summary>
        /// Raise a modal for <i>every</i> report, not just the major ones. Default off: on a large
        /// city this interrupts constantly.
        /// </summary>
        public bool ShowAllReports { get; set; } = false;

        /// <summary>
        /// A field-by-field copy.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This object is shared <i>by reference</i> across an engine tick —
        /// <c>PoliticalEngine.CloneState</c> copies the pointer, not the settings — because a tick
        /// never changes a setting and copying one per month would be waste. Anything that <i>does</i>
        /// change a setting therefore has to clone first, or it mutates the caller's input and breaks
        /// the purity the whole engine is tested for. <c>PoliticalEngine.Retheme</c> is the first such
        /// caller.
        /// </para>
        /// <para>
        /// Deliberately here rather than in the engine: a new property added above and forgotten here
        /// silently reverts to its default the first time a player changes their theme, and the two
        /// places are one screen apart so that the omission is visible.
        /// </para>
        /// </remarks>
        public AgoraSettings Clone()
        {
            return new AgoraSettings
            {
                SchemaVersion = SchemaVersion,
                StartYear = StartYear,
                Theme = Theme,
                System = System,
                WakeCadence = WakeCadence,
                SnapshotRetention = SnapshotRetention,
                Enabled = Enabled,
                EffectsEnabled = EffectsEnabled,
                ThemeLocked = ThemeLocked,
                PauseOnMajorNews = PauseOnMajorNews,
                ShowAllReports = ShowAllReports
            };
        }
    }

    /// <summary>
    /// The complete political state of one save at one sim date — the root of
    /// <c>ModsData/Agora/&lt;saveGuid&gt;/state_&lt;year&gt;_&lt;month&gt;.json</c> (§5).
    ///
    /// <para>
    /// Determinism contract (non-negotiable #3): this object is a pure function of (metrics history,
    /// prior state, seeds, catalogs, settings). Every list on it and on its members has a documented
    /// sort key, because "desync" is defined as the SHA-256 of this object's serialization changing
    /// across a reload — and an unsorted list changes that hash without anything actually being wrong.
    /// </para>
    /// </summary>
    public sealed class PoliticalState
    {
        public int SchemaVersion { get; set; } = 2;

        /// <summary>
        /// Agora's own save identity (§5). Written into the save via the serialization hooks, never
        /// derived from a filename, and never <c>Guid.NewGuid()</c> at load time. It is the first
        /// argument to every seed derivation.
        /// </summary>
        public Guid SaveGuid { get; set; }

        /// <summary>Sim date this state describes. From <c>AgoraTimeService</c> only (#8).</summary>
        public SimDate Date { get; set; }

        public AgoraSettings Settings { get; set; } = new AgoraSettings();

        /// <summary>Parties, sorted by <see cref="Party.Id"/>. Includes dissolved brands so they can revive.</summary>
        public List<Party> Parties { get; set; } = new List<Party>();

        /// <summary>Factions, sorted by <see cref="Faction.Id"/>. Usually empty in the EU theme.</summary>
        public List<Faction> Factions { get; set; } = new List<Faction>();

        /// <summary>
        /// Blocs, sorted by district id then <see cref="BlocKey.Ordinal"/>. Persisted so a reload can
        /// reconcile from the nearest earlier snapshot without replaying the whole history.
        /// </summary>
        public List<Bloc> Blocs { get; set; } = new List<Bloc>();

        /// <summary>Most recent city-wide vote shares, sorted by party id. Recomputed monthly.</summary>
        public List<PartyVoteShare> CurrentVoteShares { get; set; } = new List<PartyVoteShare>();

        /// <summary>Per-district current standings, sorted by district id.</summary>
        public List<DistrictResult> CurrentDistrictStandings { get; set; } = new List<DistrictResult>();

        /// <summary>
        /// Published polls, oldest first, capped at <c>polling.maxStoredPolls</c>.
        /// </summary>
        public List<PollResult> RecentPolls { get; set; } = new List<PollResult>();

        /// <summary>Completed elections, oldest first. Append-only history.</summary>
        public List<ElectionResult> ElectionHistory { get; set; } = new List<ElectionResult>();

        /// <summary>The sitting government, or null between a collapse and a new formation.</summary>
        public Coalition? Government { get; set; }

        /// <summary>Past governments, oldest first. Append-only history.</summary>
        public List<Coalition> CoalitionHistory { get; set; } = new List<Coalition>();

        /// <summary>All mandates, active and resolved, sorted by <see cref="Mandate.Id"/>.</summary>
        public List<Mandate> Mandates { get; set; } = new List<Mandate>();

        /// <summary>
        /// Events that have fired and are still politically live, sorted by
        /// <see cref="TimelineEvent.Id"/>.
        /// </summary>
        public List<TimelineEvent> ActiveEvents { get; set; } = new List<TimelineEvent>();

        /// <summary>
        /// Every event id already fired, sorted ascending. Mirrors <c>timeline_progress.json</c>;
        /// an event never fires twice.
        /// </summary>
        public List<string> FiredEventIds { get; set; } = new List<string>();

        /// <summary>Derived indices as of <see cref="Date"/>.</summary>
        public DerivedIndices Indices { get; set; } = new DerivedIndices();

        /// <summary>Sequential term number, starting at 1 before the first election.</summary>
        public int TermNumber { get; set; } = 1;

        /// <summary>Next scheduled election. Null before the first term is scheduled.</summary>
        public SimDate? NextElectionDate { get; set; }

        /// <summary>True during the final <c>polling.campaignWeeks</c> before an election (§3).</summary>
        public bool IsCampaignSeason { get; set; }

        /// <summary>Sitting mayor's party. Null under the Proportional system.</summary>
        public string? MayorPartyId { get; set; }

        /// <summary>
        /// Last sim date the flavor provider produced a usable payload. Null when the engine has
        /// never had flavor. The engine never waits on it (#7).
        /// </summary>
        public SimDate? LastFlavorDate { get; set; }
    }
}
