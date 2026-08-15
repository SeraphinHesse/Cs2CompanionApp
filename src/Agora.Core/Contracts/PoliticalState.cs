using System;
using System.Collections.Generic;
using Agora.Core.Stories;

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
    /// How decisively a bloc converts its preference into a vote — the player-facing name for
    /// <c>affinity.softmaxTemperature</c>.
    /// </summary>
    /// <remarks>
    /// A level, not a number. The coefficient each level maps to lives in
    /// <c>affinity.softmaxTemperaturePresets</c>, because a number that affects behaviour may not
    /// live in C# (<c>data/CLAUDE.md</c> rule 4). <see cref="Default"/> deliberately carries no
    /// preset entry: it means "whatever the tuning file's own value is", so retuning the shipped
    /// coefficient reaches every save that never chose otherwise.
    /// </remarks>
    public enum VoteSharpness
    {
        Blurred = 0,
        Default = 1,
        Sharp = 2
    }

    /// <summary>
    /// How far a live event can move a bloc — the player-facing name for
    /// <c>affinity.eventModifierWeight</c>. Levels map through
    /// <c>affinity.eventModifierWeightPresets</c>.
    /// </summary>
    public enum NewsInfluence
    {
        Muted = 0,
        Default = 1,
        Loud = 2
    }

    /// <summary>
    /// How tightly a fixed brand holds its archetype at generation — the player-facing name for
    /// <c>parties.anchoredSpreadSigma</c>. Levels map through
    /// <c>parties.anchoredSpreadSigmaPresets</c>.
    /// </summary>
    /// <remarks>
    /// Read only at party generation, so changing it mid-save does nothing until the registry is
    /// regenerated — which today happens only on a theme change. The settings surface says so.
    /// </remarks>
    public enum BrandDiscipline
    {
        Loose = 0,
        Default = 1,
        Locked = 2
    }

    /// <summary>
    /// How punishing the political-power economy is — the player-facing name for the <c>power</c>
    /// gain, cost and penalty presets.
    /// </summary>
    /// <remarks>
    /// A level, not a number, for the same reason as <see cref="VoteSharpness"/>: the coefficients
    /// live in tuning because a number that affects behaviour may not live in C# (<c>data/CLAUDE.md</c>
    /// rule 4). <see cref="Default"/> carries no preset entry and means "whatever the tuning file's
    /// own values are", so retuning the shipped economy reaches every save that never chose otherwise.
    /// </remarks>
    public enum PowerIntensity
    {
        Lenient = 0,
        Default = 1,
        Harsh = 2
    }

    /// <summary>
    /// How hard a story's goals are to meet — the player-facing name for the <c>stories</c> check
    /// scaling presets. Same <see cref="PowerIntensity">Default-means-leave-tuning-alone</see> rule.
    /// </summary>
    public enum StoryDifficulty
    {
        Forgiving = 0,
        Default = 1,
        Demanding = 2
    }

    /// <summary>
    /// Per-save settings. Lives in the sidecar, not in global config (non-negotiable #10). The only
    /// exceptions are the master toggle and anything that must work before a save exists — those
    /// stay in the mod's own options page.
    /// </summary>
    public sealed class AgoraSettings
    {
        public int SchemaVersion { get; set; } = 4;

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
        /// How decisively blocs convert preference into votes. Default means "use the shipped
        /// coefficient", so a retune reaches saves that never chose otherwise.
        /// </summary>
        public VoteSharpness VoteSharpness { get; set; } = VoteSharpness.Default;

        /// <summary>How far live events can move a bloc.</summary>
        public NewsInfluence NewsInfluence { get; set; } = NewsInfluence.Default;

        /// <summary>How tightly fixed party brands hold their archetype at generation.</summary>
        public BrandDiscipline BrandDiscipline { get; set; } = BrandDiscipline.Default;

        // ------------------------------------------------------------------ the story system (v4)
        //
        // Every number the design document names is balanceable from here or from `stories`/`power`
        // tuning. There is deliberately no StoryResolutionDay: a sim "day" is a calendar month, so
        // there is no day 15 to resolve on — see the rework plan's "Why not half a month". The
        // cadence tunable is `stories.cycleMonths`.

        /// <summary>
        /// Master switch for the story layer within this save. Off leaves the rest of the political
        /// engine running exactly as before, which is what makes it a safe thing to turn off.
        /// </summary>
        public bool StoriesEnabled { get; set; } = true;

        /// <summary>
        /// Stories drafted per cycle. <b>Wins over <c>stories.storiesPerCycle</c> when set</b>
        /// (greater than zero); the tuning key is the fallback.
        /// </summary>
        /// <remarks>
        /// Per-save settings live in the sidecar, not in global config — non-negotiable #10 — so
        /// where a setting and a tuning key name the same quantity, the setting is the player's
        /// answer and tuning is the default they never overrode. The pattern to copy is
        /// <c>TickPlanner.SnapshotsToPrune</c>, which resolves <c>SnapshotRetention</c> against
        /// <c>scheduler.snapshotRetention</c> the same way and states the same reason. "Set" is
        /// <c>&gt; 0</c> rather than a nullable, matching that precedent.
        /// </remarks>
        public int StoriesPerCycle { get; set; } = 2;

        /// <summary>
        /// Events bundled into one story. <b>Wins over <c>stories.eventsPerStory</c> when set</b>,
        /// on the same rule as <see cref="StoriesPerCycle"/>.
        /// </summary>
        public int EventsPerStory { get; set; } = 3;

        /// <summary>
        /// Master switch for the political-power currency. Off means overrides are unavailable and
        /// no debt penalty can arise; stories still draft and resolve.
        /// </summary>
        public bool PoliticalPowerEnabled { get; set; } = true;

        /// <summary>How punishing the power economy is.</summary>
        public PowerIntensity PowerIntensity { get; set; } = PowerIntensity.Default;

        /// <summary>How hard story goals are to meet.</summary>
        public StoryDifficulty StoryDifficulty { get; set; } = StoryDifficulty.Default;

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
                ShowAllReports = ShowAllReports,
                VoteSharpness = VoteSharpness,
                NewsInfluence = NewsInfluence,
                BrandDiscipline = BrandDiscipline,
                StoriesEnabled = StoriesEnabled,
                StoriesPerCycle = StoriesPerCycle,
                EventsPerStory = EventsPerStory,
                PoliticalPowerEnabled = PoliticalPowerEnabled,
                PowerIntensity = PowerIntensity,
                StoryDifficulty = StoryDifficulty
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
    /// <summary>
    /// Running record of how badly the major parties are governing, used by the <c>fringe</c> packet
    /// to decide whether minor parties may poll above their ceiling.
    ///
    /// <para>Split into a closed part and an open accumulator. The closed part —
    /// <see cref="ConsecutiveFailureTerms"/>, <see cref="LastClosedTermNumber"/> and
    /// <see cref="LastTermFailureScore"/> — is what the ceiling actually reads, and only moves when a
    /// term ends at an election. The accumulator underneath it collects the current term's monthly
    /// observations and is zeroed at every close.</para>
    ///
    /// <para>Mutable and written every tick, so it must be deep-copied wherever
    /// <see cref="PoliticalState"/> is cloned — sharing the instance would let a speculative advance
    /// write into the prior state.</para>
    /// </summary>
    public sealed class FringeWatch
    {
        /// <summary>
        /// Terms in a row scored as failures. The ceiling stays shut until this reaches
        /// <c>fringe.unlockConsecutiveTerms</c>; one good term resets it to zero.
        /// </summary>
        public int ConsecutiveFailureTerms { get; set; }

        /// <summary>Term number of the most recent close, so a term cannot be scored twice.</summary>
        public int LastClosedTermNumber { get; set; }

        /// <summary>Score of the last closed term, 0–1. Scales how far the ceiling opens.</summary>
        public double LastTermFailureScore { get; set; }

        /// <summary>Term the accumulator below is collecting for.</summary>
        public int TermNumber { get; set; }

        /// <summary>Ticks observed this term. Divides <see cref="DiscontentSum"/> into a mean.</summary>
        public int MonthsObserved { get; set; }

        /// <summary>Running sum of the city discontent index over this term.</summary>
        public double DiscontentSum { get; set; }

        /// <summary>
        /// Running sum of <c>MandateResolution.OppositionSurge</c> for mandates defied by a major
        /// party this term. Salience-weighted at source, so a broken promise nobody cared about
        /// counts for less than one that mattered.
        /// </summary>
        public double DefianceSurgeSum { get; set; }

        /// <summary>Governments that collapsed this term.</summary>
        public int GovernmentChanges { get; set; }

        /// <summary>Elections this term that changed which party holds the mayoralty.</summary>
        public int MayorChanges { get; set; }

        public FringeWatch Clone() => new FringeWatch
        {
            ConsecutiveFailureTerms = ConsecutiveFailureTerms,
            LastClosedTermNumber = LastClosedTermNumber,
            LastTermFailureScore = LastTermFailureScore,
            TermNumber = TermNumber,
            MonthsObserved = MonthsObserved,
            DiscontentSum = DiscontentSum,
            DefianceSurgeSum = DefianceSurgeSum,
            GovernmentChanges = GovernmentChanges,
            MayorChanges = MayorChanges
        };
    }

    public sealed class PoliticalState
    {
        /// <summary>
        /// Must equal <c>Agora.Mod.Persistence.SidecarSchema.CurrentStateVersion</c>. Written as a
        /// literal because <c>Agora.Core</c> cannot reference <c>Agora.Mod</c>, and kept honest by
        /// <c>SidecarMigrationTests</c>.
        /// </summary>
        /// <remarks>
        /// This default was <c>3</c> while <c>CurrentStateVersion</c> was already <c>4</c>, so a
        /// freshly constructed state claimed a version it had never been. That is not cosmetic: the
        /// migration chain dispatches on this number, so a v4 -> v5 step would have run against an
        /// object that was never v4 and "upgraded" fields it had never written. Whoever bumps
        /// <c>CurrentStateVersion</c> bumps this in the same commit.
        /// </remarks>
        public int SchemaVersion { get; set; } = 6;

        /// <summary>
        /// Agora's own save identity (§5). Written into the save via the serialization hooks, never
        /// derived from a filename, and never <c>Guid.NewGuid()</c> at load time. It is the first
        /// argument to every seed derivation.
        /// </summary>
        public Guid SaveGuid { get; set; }

        /// <summary>Sim date this state describes. From <c>AgoraTimeService</c> only (#8).</summary>
        public SimDate Date { get; set; }

        /// <summary>
        /// The last month whose political tick ran to completion, as <see cref="SimDate.TotalMonths"/>.
        /// <c>-1</c> means no month has completed yet. A month may run only when
        /// <c>today.TotalMonths &gt; LastCompletedTickMonth</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Persisted, and that is the whole point.</b> The runtime used to decide "the month
        /// changed" from session-local fields that <c>ResetForNewSave</c> clears, and the replay path
        /// that would otherwise have set them runs only when reconciliation reports months to replay
        /// — which a mid-month save, quit and reload never produces. So every reload re-ran the month
        /// it had already advanced through, and <c>PoliticalEngine.Advance</c> has no same-month guard
        /// of its own. Keeping the watermark in the sidecar is what makes the guard survive the
        /// session boundary that defeated the old one.
        /// </para>
        /// <para>
        /// A month count rather than a <see cref="SimDate"/>: the political calendar is month-granular
        /// (a sim "day" is a calendar month), and storing a date would invite two values that differ
        /// only in their day to read as two distinct months.
        /// </para>
        /// <para>
        /// The damage was a duplicated poll and a double-counted
        /// <see cref="FringeWatch.MonthsObserved"/>. It stops being cosmetic the moment a tick carries
        /// a scoring accumulator, which is why this lands before the story system rather than with it.
        /// </para>
        /// </remarks>
        public int LastCompletedTickMonth { get; set; } = -1;

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

        /// <summary>
        /// Establishment-failure record driving the fringe ceiling. Meaningful only under
        /// <see cref="ElectoralSystem.FirstPastThePost"/>; still carried in EU saves so the shape of
        /// the document does not depend on the theme, and reset on a retheme.
        /// </summary>
        public FringeWatch Fringe { get; set; } = new FringeWatch();

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

        // ------------------------------------------------------------------ the story system (v6)
        //
        // Every list below carries a DOCUMENTED SORT KEY, and that is not decoration: the determinism
        // contract is the SHA-256 of this object's serialization, so a list whose order depends on
        // insertion — or a Dictionary keyed by event id — fails it outright while nothing is
        // actually wrong. Every member also has a setter, because CloneStateCoverageTests filters on
        // CanWrite and a get-only member is one the guard silently skips.
        //
        // Story events deliberately do NOT enter ActiveEvents. Two stories of three events would sit
        // at catalog.maxConcurrentEvents (6) and start refusing to fire timeline events; worse,
        // AffinityEngine.EventTerm sums over every live event and clamps to [-1,+1] BEFORE weighting,
        // so at that volume the clamp saturates permanently and the event term stops discriminating
        // between a flood and a bus-fare rise. Stories contribute pressure through their own term
        // with its own budget.

        /// <summary>Stories currently open, sorted by <c>Id</c> ordinal.</summary>
        public List<Story> LiveStories { get; set; } = new List<Story>();

        /// <summary>
        /// Resolved stories, sorted by <c>(ResolvedMonth descending, Id ordinal)</c>.
        /// </summary>
        /// <remarks>
        /// <b>Intended to be bounded by <c>stories.archiveRetention</c>, and nothing enforces that
        /// yet.</b> Wave 2 writes no story here — archiving happens where a story is retired, which
        /// lands with the tick wiring in wave 4 — so the trim is that wave's obligation and is
        /// recorded as such rather than left implied by this comment. An earlier draft simply
        /// asserted "bounded by", which is the kind of claim that goes unchecked for a year and then
        /// turns out never to have been true.
        /// <para>
        /// Do not make anything <i>depend</i> on the bound for correctness. The re-use cooldown
        /// deliberately does not: gating re-use on archive membership couples it to
        /// <c>archiveRetention × eventsPerStory</c> and empties a finite catalog permanently. See
        /// <see cref="EventPoolEntry.LastDraftedMonth"/>.
        /// </para>
        /// </remarks>
        public List<Story> StoryArchive { get; set; } = new List<Story>();

        /// <summary>Triggered events awaiting a draw, sorted by <c>EventId</c> ordinal.</summary>
        public List<EventPoolEntry> EventPool { get; set; } = new List<EventPoolEntry>();

        /// <summary>The political-power currency. Never null.</summary>
        public PoliticalPowerState Power { get; set; } = new PoliticalPowerState();

        /// <summary>
        /// The ordered, dated log of player decisions, sorted by
        /// <c>(DecidedMonth, Sequence, EventId ordinal)</c>.
        /// </summary>
        /// <remarks>
        /// This log <b>is</b> engine state, per the amendment to non-negotiable #3 recorded on
        /// <see cref="PlayerCommand"/>. It is replayed, never re-solicited, which is what lets an
        /// asynchronous player choice sit inside a deterministic engine.
        /// </remarks>
        public List<PlayerCommand> PlayerCommands { get; set; } = new List<PlayerCommand>();

        /// <summary>
        /// Last month a story draft ran, as <see cref="SimDate.TotalMonths"/>. -1 means never.
        /// The draft phase's own idempotence guard.
        /// </summary>
        public int LastStoryDraftMonth { get; set; } = -1;

        /// <summary>
        /// Last month a story resolution ran, as <see cref="SimDate.TotalMonths"/>. -1 means never.
        /// </summary>
        public int LastStoryResolveMonth { get; set; } = -1;
    }
}
