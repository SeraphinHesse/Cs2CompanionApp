using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Agora.Core.Contracts;
using Agora.Core.Engine;
using Agora.Core.Engine.Parties;
using Agora.Core.Events.Catalog;
using Agora.Core.Events.Scheduler;
using Agora.Core.Stories;
using Agora.Core.Stories.Catalog;
using Agora.Core.Tuning;
using Agora.Mod.Effects;
using Agora.Mod.Llm;
using Agora.Mod.Persistence;
using Agora.Mod.Sensors;
using Agora.Mod.Time;
using Unity.Entities;
using CoreSettings = Agora.Core.Contracts.AgoraSettings;

namespace Agora.Mod.Core
{
    /// <summary>
    /// Which world <see cref="AgoraRuntime.ResetForNewSave"/> is running against. The two cases clear
    /// the same fields and differ in exactly one respect — whether the city holding Agora's modifier
    /// buffers is one the player will still be looking at afterwards, and so whether handing those
    /// buffers back is observable or merely work done on a world about to be thrown away.
    /// </summary>
    /// <remarks>
    /// This is an argument rather than something the reset infers because it cannot be inferred. Both
    /// cases run against a live world with live entities — the difference is what happens to that
    /// world next, which nothing reachable from here reports. It has no default value for the same
    /// reason: a new call site must answer the question rather than inherit whichever answer happened
    /// to be written first.
    /// </remarks>
    public enum ResetCause
    {
        /// <summary>
        /// A different city is loading, or the open one is being closed. The outgoing city's entities
        /// are <i>still alive</i> at this point — <c>GameManager.LoadSimulationData</c> raises
        /// <c>onGamePreload</c> and only then starts the deserialize phase that <c>ClearSystem</c>
        /// destroys them in (<c>SystemOrder</c> registers it
        /// <c>UpdateBefore&lt;ClearSystem&gt;(SystemUpdatePhase.Deserialize)</c>) — so a revert here
        /// would succeed. It is skipped because it would be unobservable: everything it wrote would go
        /// into a world destroyed moments later, and the outgoing save on disk was already written
        /// before this ran (<c>onGamePreload</c> is raised only from the load path, never the save
        /// path), so the revert cannot change what that save carries. What is baked into it is
        /// reconciled on the next load by <c>ModifierAggregate.IsCarriedOver</c>, either way.
        /// </summary>
        SaveBoundary,

        /// <summary>
        /// Agora is going away while the city stays open — mod unload, master toggle off. Every
        /// tracked slot is still a live modifier buffer holding our contribution, and it has to be
        /// given back before the table is dropped: what is left behind here is what the player is
        /// left with.
        /// </summary>
        ModShutdown
    }

    /// <summary>
    /// The composition root: the one place the five layers written by separate packets — time,
    /// sensors, persistence, effects, flavor — are joined to each other.
    ///
    /// <para>
    /// Each layer was built to be wired from outside rather than to find its collaborators itself,
    /// and each left a named seam for it: <see cref="AgoraSidecarSystem.LoadHandler"/>,
    /// <see cref="AgoraEffects.DistrictResolver"/>, <see cref="AgoraEffects.Initialize"/>,
    /// <see cref="AgoraStartYearSystem.Configure"/>. Filling those in one type — rather than letting
    /// each system reach for the others in its own <c>OnCreate</c> — is what keeps the load order
    /// legible and keeps a per-save reset from having to be remembered in six places.
    /// </para>
    ///
    /// <para>
    /// <b>Where the politics are.</b> Not here. This type owns the <i>cadence</i> — day and month
    /// boundaries derived from <see cref="AgoraTimeService"/> and nothing else (non-negotiable #8) —
    /// and it owns the call into <see cref="PoliticalEngine.Advance"/> on each month boundary. Every
    /// decision that call makes is taken inside <c>Agora.Core</c>. What happens on this side of the
    /// boundary is assembling the input, storing the returned state, handing the effect requests to
    /// the sink and waking the prose: glue, not computation, because
    /// <c>src/Agora.Mod/CLAUDE.md</c> forbids political logic in this assembly and because anything
    /// decided here would be untestable without the game installed.
    /// </para>
    ///
    /// <para>
    /// Static because there is exactly one game world and one loaded save at a time, and because the
    /// systems that need it are created by the ECS world rather than by us, so there is nowhere to
    /// inject an instance. Every method is defensive: this runs inside <c>GameSimulation</c>, and a
    /// throw here takes the player's game down.
    /// </para>
    /// </summary>
    // `partial` so later waves add AgoraRuntime.Stories.cs, .StoryCommands.cs and .Power.cs as NEW
    // files rather than queueing on this one. At 3000+ lines this is the hottest file in the repo and
    // every wave wants it; splitting is what keeps parallel lanes from colliding here.
    public static partial class AgoraRuntime
    {
        /// <summary>
        /// Guards the lifecycle entry points — <see cref="Attach"/>, <see cref="Detach"/>,
        /// <see cref="ResetForNewSave"/>, <see cref="SetSetting"/>. Deliberately <b>not</b> taken by
        /// <see cref="Tick"/>, which is the largest writer of this type's fields.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>That is safe, and the reason is that every one of those callers is the Unity main
        /// thread.</b> <c>GameManager</c> is a <c>MonoBehaviour</c>: its <c>Update</c> runs
        /// <c>UpdateWorld</c> → <c>UpdateSystem.Update(SystemUpdatePhase.MainLoop)</c> → <c>UIUpdateSystem</c>
        /// → <c>Update(SystemUpdatePhase.UIUpdate)</c>, and then <c>UpdateUI</c> →
        /// <c>UIManager.Update</c> → <c>UIView.Update</c> → <c>View.Advance</c>, which is where a
        /// <c>CallBinding</c> from the dashboard is dispatched. Its <c>LateUpdate</c> runs
        /// <c>LateUpdateWorld</c> → <c>Update(SystemUpdatePhase.LateUpdate)</c> → <c>SimulationSystem</c>
        /// → <c>Update(SystemUpdatePhase.GameSimulation)</c>, where <see cref="Tick"/> is reached.
        /// Unity calls both on the main thread, in that order, in the same frame. So <c>UIUpdate</c>,
        /// the binding dispatch and <c>GameSimulation</c> are one thread, and this lock is decorative
        /// with respect to <c>_state</c>, <c>_flavorPayload</c> and <c>_pendingWake</c>.
        /// </para>
        /// <para>
        /// It is kept because it costs an uncontended monitor and it is the honest place to start if
        /// that ever stops being true. The one field that genuinely crosses threads is inside
        /// <see cref="ClaudeCliProvider"/>, which has its own lock and is not reached through this one.
        /// Anything added here that is written off the main thread needs saying so out loud, because
        /// nothing else in this file expects it.
        /// </para>
        /// </remarks>
        private static readonly object Gate = new object();

        private static World _world;
        private static AgoraTimeService _time;
        private static AgoraSnapshotSystem _snapshots;
        private static AgoraDistrictSensorSystem _districts;
        private static AgoraSidecarSystem _sidecar;
        private static AgoraStartYearSystem _startYear;
        private static AgoraEffectApplicationSystem _effectApplication;

        private static EngineTuning _tuning;
        private static LayeredFlavorProvider _flavor;
        private static CoreSettings _saveSettings;

        private static TimelineCatalog _catalog = TimelineCatalog.Empty;
        private static CivicEventCatalog _civicCatalog = CivicEventCatalog.Empty;

        /// <summary>
        /// The prose each writer has produced for each story. See <see cref="StoryProseLedger"/> for
        /// why both are kept rather than one replacing the other.
        /// </summary>
        /// <remarks>
        /// AGORA-SEAM(wave-5/spine): wave 6's story panel reads this through
        /// <see cref="StoryProse"/> to render a card. It is deliberately not persisted — pool prose
        /// rebuilds identically and CLI prose returns from <c>flavor_cache.json</c>.
        /// </remarks>
        private static readonly StoryProseLedger _storyProse = new StoryProseLedger();

        /// <summary>The story prose ledger, for the UI publishers.</summary>
        public static StoryProseLedger StoryProse
        {
            get { return _storyProse; }
        }

        private static PoliticalState _state;
        private static SimDate _startDate;
        private static readonly List<CitySnapshot> _snapshotHistory = new List<CitySnapshot>();
        private static bool _manualWakeRequested;

        private static CitySnapshot _lastSnapshot;
        private static int _stateVersion;

        /// <summary>
        /// Alerts the player has not answered yet, oldest first. Published as
        /// <c>agora.news.alerts</c>; the modal shows the head of it and acks its way down.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>In memory only, and never persisted.</b> That is what makes "an alert does not replay
        /// after a reload" structural rather than a rule somebody has to remember: a resumed save
        /// starts with an empty list because the list is fresh. Same reasoning as
        /// <see cref="_provisionalNamePartyIds"/>, whose comment is worth reading before touching
        /// this.
        /// </para>
        /// <para>
        /// Filled by <see cref="RaiseAlerts"/> once per sim month — and by nothing else, since v10
        /// retired the article alert — bounded by <see cref="AlertQueueMax"/>, de-duplicated through
        /// <see cref="_raisedAlertIds"/>, and cleared with the prose block in
        /// <see cref="ResetForNewSave"/> (<c>docs/plans/0003-w5-popup-lane.md</c> §5.3).
        /// </para>
        /// </remarks>
        private static readonly List<NewsAlert> _alerts = new List<NewsAlert>();

        /// <summary>
        /// Keys of the alerts already raised this session, <c>Kind + "|" + Id</c>. Membership only —
        /// never enumerated, so nothing downstream can depend on a hash order (non-negotiable #3).
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is what stops the same thing interrupting twice. Every raise path re-reads persisted
        /// state rather than a diff — a party's founding date, a coalition's formed date, an event's
        /// fired date — so a replayed catch-up or a re-publish would otherwise offer the same card
        /// again. The set is the mechanism; it is session-scoped for the same reason
        /// <see cref="_alerts"/> is, and an alert the player already answered before a reload simply
        /// never comes back because the ring behind it is empty too.
        /// </para>
        /// <para>
        /// <b>Why the key is compound.</b> It was the article alert that made it so: every other kind
        /// carries an engine-written prefix, but an article's id was the bare, model-authored
        /// <c>Article.Id</c>, and a model returning <c>"event:flood-2031"</c> would have landed on the
        /// real event alert's key and suppressed whichever came second. That alert is gone with the
        /// feed (v10), so nothing model-authored reaches this set today — but the kind stays in the
        /// key, because it costs one concatenation and it is what keeps the namespaces separate the
        /// next time a raise path is added.
        /// </para>
        /// </remarks>
        private static readonly HashSet<string> _raisedAlertIds =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// How many unanswered alerts the ring will hold before it starts dropping the oldest.
        /// </summary>
        /// <remarks>
        /// Not a tuning knob: it is a bound on a UI queue, not a political quantity, and
        /// <c>data/engine_tuning.json</c> is for numbers the engine reasons with. A player who leaves
        /// the game running through a decade at speed three must not come back to an unbounded stack
        /// of modals, and eight is already more than anyone will read.
        /// </remarks>
        private const int AlertQueueMax = 8;

        /// <summary>
        /// Story cards the player has not answered yet, oldest first. Published as
        /// <c>agora.stories.alerts</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>A separate queue from <see cref="_alerts"/>, and separate on purpose</b> — the reasoning
        /// is in the rework plan's wave 6 section and is worth restating where the field is, because
        /// "just reuse the news lane" is the obvious cheap move and it breaks three ways. A news
        /// alert's body is fetched from <c>agora.news.article</c> under the alert's own id; a story id
        /// is not a key that map holds, and <c>AgoraUiProjection.BuildArticle</c> answers an unknown
        /// key with an empty payload rather than throwing, so the failure would be a blank masthead
        /// with nothing logged. <c>ArticleModal</c> renders <c>alerts[0]</c> or nothing, so two lanes
        /// sharing it would serialise behind each other. And <see cref="AlertQueueMax"/> drops the
        /// oldest when it overflows — on the news lane that is a missed announcement about something
        /// that happened anyway, on this one it is <b>a decision the player never got to make</b>.
        /// </para>
        /// <para>
        /// Session-scoped and never persisted, exactly like <see cref="_alerts"/>: a card that was
        /// never answered before a reload does not come back, because the story itself is persisted
        /// and the panel is where it is answered. The queue is the interruption, not the record.
        /// </para>
        /// </remarks>
        private static readonly List<StoryAlert> _storyAlerts = new List<StoryAlert>();

        /// <summary>
        /// Story ids already offered as a card this session. Membership only — never enumerated, so
        /// nothing downstream can depend on a hash order (non-negotiable #3).
        /// </summary>
        /// <remarks>
        /// Story ids are engine-authored, so this needs no compound key. Neither, strictly, does
        /// <see cref="_raisedAlertIds"/> any more: it prefixes with a kind because an article id was
        /// bare and model-authored and could collide with an engine-written one, and that alert
        /// retired with the feed in v10. It keeps the compound key against the next raise path
        /// regardless; this set has never had a reason to want one.
        /// </remarks>
        private static readonly HashSet<string> _raisedStoryAlertIds =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// How many unanswered story cards the ring holds before it drops the oldest.
        /// </summary>
        /// <remarks>
        /// <b>Deliberately larger in proportion than <see cref="AlertQueueMax"/> is for news, because
        /// a dropped story card is a lost decision rather than a missed headline.</b> At the shipped
        /// cadence of two stories per two-month cycle, four is two full cycles of not opening the
        /// dashboard — and the story itself survives in <c>LiveStories</c> whatever this queue does,
        /// so a drop costs the interruption, never the story. It is a bound on a UI queue and not a
        /// political quantity, so it is not a tuning key, for the same reason as
        /// <see cref="AlertQueueMax"/>.
        /// </remarks>
        private const int StoryAlertQueueMax = 4;

        private static FlavorPayload _flavorPayload;
        private static SimDate? _lastFlavorDate;
        private static SimDate? _lastAttemptDate;
        private static bool _pendingWake;
        private static FlavorProviderState _lastFlavorState = FlavorProviderState.Idle;

        /// <summary>
        /// Parties whose current name came from the canned pool and may still be improved on once by
        /// a real CLI document. In memory only, deliberately: nothing about it is worth persisting.
        ///
        /// <para>
        /// It needs no persisted flag and no provenance field on the payload because a second
        /// pool-sourced write rewrites the identical string and cannot be observed — but that holds
        /// only while every pool caller generates over the <b>full roster</b>. The pool's name for a
        /// party is a function of (save GUID, founding date, party ID) <i>and of the other parties in
        /// the same <c>Generate</c> call</i>, because de-duplication is per call; see
        /// <see cref="StaticPoolProvider"/>. Narrow the request to the unnamed parties and the pool
        /// hands a newcomer a name an incumbent already holds, which the next full-roster document
        /// then resolves by moving one of them. So <see cref="EnsureEveryPartyNamed"/> passes every
        /// party and applies only the empty ones.
        /// </para>
        ///
        /// <para>
        /// Only a CLI document can actually change a name, and the set is what lets it do so exactly
        /// once. Both doors that can write a canned name — <see cref="EnsureEveryPartyNamed"/> and a
        /// pool-sourced payload arriving through <see cref="CollectProse"/> — put the party in here,
        /// so a splinter born on a month the CLI was late is not stuck with a stopgap for the save.
        /// After a reload the set is empty and every name arrives non-empty from the sidecar,
        /// so <see cref="ApplyProseNames"/> declines every rename from then on. It is not a cache and
        /// it is not a leak; an empty set is the correct state for a resumed save.
        /// </para>
        /// </summary>
        private static readonly HashSet<string> _provisionalNamePartyIds =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// The last date <see cref="Tick"/> saw, and whether it has seen one at all, <b>this session</b>.
        /// A logging latch and nothing more.
        /// </summary>
        /// <remarks>
        /// <para>
        /// These two used to decide whether a month ran, and that is precisely why every reload ran a
        /// month twice. They are session-local and <see cref="ResetForNewSave"/> clears them at every
        /// save boundary; the only other writer is <see cref="Replay"/>, which runs solely when
        /// reconciliation reports months to replay — and a mid-month save, quit and reload reports
        /// none. So the first heartbeat of every resumed session saw "no tick yet" and re-ran a month
        /// the save had already advanced through, with <c>PoliticalEngine.Advance</c> carrying no
        /// same-month guard of its own.
        /// </para>
        /// <para>
        /// The question is now answered by <see cref="PoliticalState.LastCompletedTickMonth"/>, which
        /// is persisted and therefore survives the session boundary that defeated this pair. What is
        /// left here is one log line: the first heartbeat after a load reports whether the watermark
        /// disagreed with what the old latch would have done, which is what the reload gate is read
        /// off in <c>Agora.log</c>.
        /// </para>
        /// </remarks>
        private static SimDate _lastTick;
        private static bool _hasTicked;

        private static bool _attached;
        private static bool _saveActive;
        private static bool _isFirstRun;

        /// <summary>
        /// How many past snapshots the engine is handed for its trend legs. The widest window the
        /// indices packet reads is measured in months, so a couple of years is comfortably enough;
        /// keeping the whole save's history in memory is not.
        /// </summary>
        private const int SnapshotHistoryMonths = 36;

        /// <summary>
        /// Directory the mod was loaded from, captured in <see cref="AgoraMod.OnLoad"/>. Null when
        /// the executable asset could not be resolved, in which case every on-disk catalog and
        /// tuning file falls back to the compiled-in default.
        /// </summary>
        public static string ModDirectory { get; set; }

        /// <summary>True once <see cref="Attach"/> has run against a world.</summary>
        public static bool IsAttached
        {
            get { return _attached; }
        }

        /// <summary>True between a successful sidecar load and the next save being loaded.</summary>
        public static bool IsSaveActive
        {
            get { return _saveActive; }
        }

        /// <summary>
        /// True when this save has never chosen a region theme: it arrived with no political state
        /// <i>and</i> with settings the store had to invent. Published as
        /// <c>agora.state.isFirstRun</c>, and the first-run dialog is the only thing that reads it.
        /// </summary>
        /// <remarks>
        /// One-shot and in-memory. It is cleared the moment the theme is chosen or the dialog is
        /// dismissed, so a player who answered the prompt is not asked again in the same session —
        /// and after a reload the settings on disk answer for them. Both halves of the condition are
        /// needed: state alone would re-prompt a save whose choice was written but whose first
        /// monthly tick never ran.
        /// </remarks>
        public static bool IsFirstRun
        {
            get { return _isFirstRun; }
        }

        /// <summary>The tuning in force. Never null once <see cref="Attach"/> has run.</summary>
        public static EngineTuning Tuning
        {
            get { return _tuning ?? EngineTuning.Default; }
        }

        /// <summary>
        /// The save's first political date — January of the per-save start year — or the default date
        /// before a sidecar has been read.
        /// </summary>
        /// <remarks>
        /// Not a clock read and not a second calendar (non-negotiable #8): the start year is a
        /// persisted setting and this is the value derived from it once, at load, in
        /// <see cref="OnSidecarLoaded"/>. Exposed rather than re-derived because the news publisher
        /// needs the same date for the opening-roster exclusion on party rows, and two derivations of
        /// one fact is how they come to disagree.
        /// </remarks>
        public static SimDate StartDate
        {
            get { return _startDate; }
        }

        /// <summary>The engine's view of the city. Null before <see cref="Attach"/>.</summary>
        public static ISnapshotSource Snapshots
        {
            get { return _snapshots; }
        }

        /// <summary>Prose provider for the current save, or null when no save is loaded.</summary>
        public static IFlavorProvider Flavor
        {
            get { return _flavor; }
        }

        /// <summary>The per-save settings that came out of the sidecar. Never null after attach.</summary>
        public static CoreSettings SaveSettings
        {
            get { return _saveSettings ?? (_saveSettings = new CoreSettings()); }
        }

        /// <summary>Agora's save identity, or <see cref="Guid.Empty"/> before one is minted.</summary>
        public static Guid SaveGuid
        {
            get { return _sidecar != null ? _sidecar.SaveGuid : Guid.Empty; }
        }

        /// <summary>
        /// The political state as of the last engine tick, or null before a save is loaded. This is
        /// what the sidecar persists and what the dashboard reads; it is never edited from outside.
        /// </summary>
        public static PoliticalState State
        {
            get { return _state; }
        }

        /// <summary>The loaded timeline catalog. Empty when the data files are missing or invalid.</summary>
        public static TimelineCatalog Catalog
        {
            get { return _catalog ?? TimelineCatalog.Empty; }
        }

        /// <summary>
        /// The loaded civic-event catalog — what stories are assembled from. Empty when the data
        /// files are missing or every document was rejected.
        /// </summary>
        /// <remarks>
        /// Kept beside <see cref="Catalog"/> and never merged into it. A timeline event fires on a
        /// date and a civic event triggers on a reading of the city; <c>TimelineEventAdapter</c> is
        /// the one sanctioned bridge, and handing either subsystem the other's list would let each
        /// silently answer the other's question.
        /// </remarks>
        public static CivicEventCatalog CivicCatalog
        {
            get { return _civicCatalog ?? CivicEventCatalog.Empty; }
        }

        /// <summary>
        /// Bumped every time <see cref="State"/> is replaced or the prose changes.
        /// </summary>
        /// <remarks>
        /// The dashboard publishers watch this rather than republishing from <c>OnUpdate</c>: a
        /// <c>ValueBinding.Update</c> with an unchanged payload still costs a bridge crossing, and the
        /// panels have to refresh on the engine's cadence, not the renderer's
        /// (<c>docs/contracts/ui_bindings.md</c> §7 rule 10). Comparing an <see cref="int"/> per UI
        /// tick is the cheapest possible way to ask "is what I published still current?".
        /// </remarks>
        public static int StateVersion
        {
            get { return _stateVersion; }
        }

        /// <summary>The most recent sensor reading, or null before the first tick.</summary>
        public static CitySnapshot LastSnapshot
        {
            get { return _lastSnapshot; }
        }

        /// <summary>
        /// The unanswered alert queue, oldest first. Never null, and empty for the whole of a resumed
        /// save until something is raised on it.
        /// </summary>
        public static IList<NewsAlert> Alerts
        {
            get { return _alerts; }
        }

        /// <summary>
        /// The unanswered story-card queue, oldest first. Never null, and empty for the whole of a
        /// resumed save until a story drafts.
        /// </summary>
        /// <remarks>
        /// Its own queue rather than a share of <see cref="Alerts"/> — see <c>_storyAlerts</c> for the
        /// three reasons, of which the load-bearing one is that a dropped news card is a missed
        /// headline and a dropped story card is a decision the player never got to make.
        /// </remarks>
        public static IList<StoryAlert> StoryAlerts
        {
            get { return _storyAlerts; }
        }

        /// <summary>The prose currently in force, or null when none has ever been produced.</summary>
        public static FlavorPayload Prose
        {
            get { return _flavorPayload; }
        }

        /// <summary>Date of the last <i>successful</i> generation, or null.</summary>
        public static SimDate? LastFlavorDate
        {
            get { return _lastFlavorDate; }
        }

        /// <summary>Date of the last attempt, successful or not, or null.</summary>
        public static SimDate? LastAttemptDate
        {
            get { return _lastAttemptDate; }
        }

        /// <summary>True while a generation is in flight. The wake control is disabled meanwhile.</summary>
        /// <remarks>
        /// Three terms rather than one, and the third is the one that matters.
        /// <see cref="_pendingWake"/> covers only a wake the player asked for — a scheduled yearly or
        /// election wake never sets it — and <see cref="FlavorProviderState.Running"/> is dropped by
        /// the CLI worker <i>before</i> it writes <c>flavor_cache.json</c>. Between those two moments
        /// the first two terms are both false while a file the retheme is about to delete has not been
        /// written yet, which is exactly the window <see cref="SetTheme"/> must refuse in.
        /// <see cref="LayeredFlavorProvider.IsGenerating"/> is the flag that stays up until the worker
        /// has finished with the disk.
        /// </remarks>
        public static bool PendingWake
        {
            get
            {
                if (_pendingWake) return true;
                if (_flavor == null) return false;
                return _flavor.State == FlavorProviderState.Running || _flavor.IsGenerating;
            }
        }

        /// <summary>True when a CLI provider exists and has not written the machine off.</summary>
        public static bool ProviderAvailable
        {
            get { return _flavor != null && _flavor.State != FlavorProviderState.Unavailable; }
        }

        /// <summary>
        /// The engine-authored failure code for the dashboard: <c>""</c>, <c>CliMissing</c>,
        /// <c>Timeout</c>, <c>BadJson</c>, <c>Disabled</c> or <c>Unknown</c>. Never a raw exception
        /// message and never model output — the panel switches on it.
        /// </summary>
        public static string LastFlavorError
        {
            get
            {
                if (_flavor == null) return "Disabled";
                if (_flavor.State == FlavorProviderState.Unavailable) return "CliMissing";
                if (_flavor.Cli == null) return "Disabled";
                if (_flavor.State != FlavorProviderState.Failed) return "";

                // The provider records a short reason; anything unrecognised is reported as Unknown
                // rather than passed through, because the contract fixes this vocabulary.
                string reason = _flavor.Cli.LastError ?? "";
                if (reason.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0) return "Timeout";
                if (reason.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0) return "BadJson";
                if (reason.IndexOf("schema", StringComparison.OrdinalIgnoreCase) >= 0) return "BadJson";
                if (reason.Length == 0) return "";
                return "Unknown";
            }
        }

        /// <summary>
        /// Asks for a manual flavor generation on the next month boundary. The engine decides whether
        /// it is allowed (the per-save cadence and the tuning switch must both permit it); this only
        /// records that the player pressed the button.
        /// </summary>
        public static void RequestManualFlavorWake()
        {
            _manualWakeRequested = true;
            _pendingWake = true;
            _stateVersion++;
        }

        // ------------------------------------------------------------------ attach

        /// <summary>
        /// Resolves every system and fills in the seams. Called from
        /// <see cref="AgoraHeartbeatSystem"/>'s <c>OnCreate</c> rather than from
        /// <see cref="AgoraMod.OnLoad"/>, because <c>OnLoad</c> runs before any ECS world exists —
        /// <see cref="World"/> is the argument this needs and <c>UpdateSystem</c> is not it.
        /// Idempotent, so it is safe for several systems to call.
        /// </summary>
        public static void Attach(World world)
        {
            if (world == null) return;

            lock (Gate)
            {
                if (_attached && ReferenceEquals(_world, world)) return;

                _world = world;

                try
                {
                    // Inside the try, not before it. Both fail soft internally, but "internally" is a
                    // property of today's implementations rather than of the call, and this runs from
                    // a system's OnCreate — where an escaping exception costs far more than a missing
                    // catalog (it leaves the system subscribed to the game's preload event with a
                    // freed state, which breaks every subsequent load).
                    _tuning = LoadTuning();
                    _catalog = LoadCatalog();
                    _civicCatalog = LoadCivicCatalog();

                    // The sensors read one coefficient out of tuning — blocs.wealthTierThresholds, the
                    // quantile cuts that split households into wealth tiers. Without this line they
                    // silently keep EngineTuning.Default's cuts no matter what data/engine_tuning.json
                    // says, so an edited threshold would move the bloc model but not the sensor that
                    // feeds it, and the two would disagree about where "middle income" starts.
                    SensorTuning.Active = _tuning;

                    _time = new AgoraTimeService(world);

                    // GetOrCreateSystemManaged, not GetExistingSystemManaged: the sensor families and
                    // the sidecar are deliberately NOT registered in an update phase (they are driven
                    // by AgoraSnapshotSystem and by the serialization hooks respectively), so on a
                    // cold world they may genuinely not exist yet.
                    _snapshots = world.GetOrCreateSystemManaged<AgoraSnapshotSystem>();
                    _districts = world.GetOrCreateSystemManaged<AgoraDistrictSensorSystem>();
                    _sidecar = world.GetOrCreateSystemManaged<AgoraSidecarSystem>();
                    _startYear = world.GetOrCreateSystemManaged<AgoraStartYearSystem>();

                    // Held only so ResetForNewSave can reach it. It is registered in an update phase
                    // by AgoraMod.OnLoad, so this resolves rather than creates in practice.
                    _effectApplication = world.GetOrCreateSystemManaged<AgoraEffectApplicationSystem>();

                    // Effects: build the palette from the tuning we actually loaded, not from the
                    // compiled default the application system would otherwise settle for, and give it
                    // a resolver that speaks the sensor layer's district ids. Without the second line
                    // every district-scoped effect silently lands nowhere — see SensorDistrictResolver.
                    AgoraEffects.Initialize(_tuning, _time);
                    AgoraEffects.DistrictResolver = new SensorDistrictResolver(_districts);

                    // Persistence: the sidecar raises this once the save has been read and the clock
                    // is trustworthy. Everything per-save hangs off it.
                    _sidecar.LoadHandler = OnSidecarLoaded;

                    // ...and this is what it writes. A Func rather than a stored object because the
                    // engine replaces the state wholesale each tick (it is never mutated in place),
                    // so anything captured by value here would go stale on the first month boundary.
                    _sidecar.StateProvider = GetStateForSave;

                    // The sensor layer's rent and land-value memory, written alongside the state.
                    // Same Func reasoning, and resolved through _snapshots at call time rather than
                    // captured: Detach nulls that field, and a closure holding the old system would
                    // write the previous city's rents into this one's directory.
                    _sidecar.MetricHistoryProvider = GetMetricHistoryForSave;

                    // A load that already happened before this ran (system creation order is the
                    // game's business, not ours) must not be missed.
                    if (_sidecar.PendingLoad != null) OnSidecarLoaded(_sidecar.PendingLoad);

                    _attached = true;
                    AgoraMod.Log.Info("Agora runtime attached.");
                }
                catch (Exception ex)
                {
                    _attached = false;
                    AgoraMod.Log.Error(ex, "Agora runtime failed to attach; the political layer is " +
                                           "inactive for this session.");
                }
            }
        }

        /// <summary>
        /// Drops every per-save reference. The world itself is not torn down here — the ECS systems
        /// outlive a save and are re-used when the next city loads.
        /// </summary>
        /// <remarks>
        /// Detach is <see cref="ResetForNewSave"/> plus the world-level references. It used to clear a
        /// subset of what that method clears, which is the shape that produced the three-copy bug:
        /// two reset paths, one an incomplete superset of the other, and no way to tell from either
        /// site which fields the other had forgotten. There is one list of per-save state now.
        ///
        /// <para>
        /// <b><see cref="ResetCause.ModShutdown"/>, and it is not a formality.</b> The city is still
        /// open and the player keeps looking at it, so the reset must hand those buffers back before
        /// it forgets which ones they were. The load path skips the revert not because the buffers are
        /// unreachable there — they are not — but because that city is about to be destroyed; see
        /// <see cref="ResetCause"/>. Passing the wrong value here leaves the player's city carrying
        /// Agora's last numbers with no mod left to remove them.
        /// </para>
        /// </remarks>
        public static void Detach()
        {
            lock (Gate)
            {
                ResetForNewSave(ResetCause.ModShutdown);

                if (_sidecar != null)
                {
                    _sidecar.LoadHandler = null;
                    _sidecar.StateProvider = null;
                    _sidecar.MetricHistoryProvider = null;
                }

                // No second AgoraEffects.Shutdown here: ResetForNewSave shuts the effect layer down
                // first thing and only rebuilds it on the ResetCause.SaveBoundary path, so on this one
                // it is already down and stays down. Re-initialising it just to tear it down again ran
                // LogCoverage on every teardown, which is a palette report nobody reads at exit.

                // Assigning null restores EngineTuning.Default rather than leaving the sensors holding
                // a tuning whose save has been closed.
                SensorTuning.Active = null;

                _world = null;
                _time = null;
                _snapshots = null;
                _districts = null;
                _sidecar = null;
                _startYear = null;
                _effectApplication = null;
                _attached = false;
            }
        }

        /// <summary>
        /// Drops every trace of the save that was open and rebuilds the per-save machinery for the
        /// one that is loading. The world, the systems and the tuning survive; nothing that describes
        /// a city does.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why this exists as one call.</b> CS2 re-uses the ECS world across "quit to menu, load
        /// another city", so every static and every system instance here outlives a save. Three layers
        /// held city A's state into city B before this: the prose fields below, the effect ledger
        /// (<see cref="Attach"/> early-returns on the same world, so <see cref="AgoraEffects.Initialize"/>
        /// never ran again) and the application system's slot table. Resetting them from three places
        /// is how it came to be three bugs, so there is one seam and <see cref="OnSidecarLoaded"/>
        /// calls it first.
        /// </para>
        /// <para>
        /// Distinct from <see cref="Detach"/>: that additionally drops the world-level references and
        /// leaves the runtime unattached, which is right at process exit and wrong at a save boundary.
        /// </para>
        /// <para>
        /// <b>Why <paramref name="cause"/> has no default.</b> One assumption in here — that nobody
        /// will ever see the city behind the effect layer's slot table again — is true at the load
        /// boundary and false at shutdown, and a shared path carried it silently to the second call
        /// site once already. See <see cref="ResetCause"/>. Every caller states which world it is
        /// resetting against, because neither this method nor the effect system can work it out.
        /// </para>
        /// </remarks>
        /// <param name="cause">Which of the two worlds this reset is running against.</param>
        public static void ResetForNewSave(ResetCause cause)
        {
            lock (Gate)
            {
                DisposeFlavor();

                _saveActive = false;
                _saveSettings = null;
                _isFirstRun = false;
                _state = null;
                _startDate = default(SimDate);
                _snapshotHistory.Clear();
                _manualWakeRequested = false;
                _lastSnapshot = null;

                // The prose block. Every one of these was surviving a load, and together they are what
                // showed the player city A's articles and party names inside city B — _lastFlavorState
                // most quietly of all, since a provider that happened to be in the same state in both
                // cities would suppress the transition check in Tick and drop a completed generation.
                _flavorPayload = null;
                _lastFlavorDate = null;
                _lastAttemptDate = null;
                _pendingWake = false;
                _lastFlavorState = FlavorProviderState.Idle;

                // Same block, same bug class, and it lands in the same commit as the emission rather
                // than after it: a queue of city A's alerts popping over city B is precisely the shape
                // of the three carry-overs this method exists for. The ring is never persisted, so a
                // reload is already clean; this is the quit-to-menu path, where the statics survive.
                _alerts.Clear();
                _raisedAlertIds.Clear();

                // The story ring, for identically the same reason and in the same block so the two
                // cannot drift apart. Story ids are minted per save, so a card from city A popping
                // over city B would point `agora.stories.article` at a story city B's state has never
                // heard of — and the panel would render a headline for a decision that does not exist.
                _storyAlerts.Clear();
                _raisedStoryAlertIds.Clear();

                // City B's party ids are not city A's, but an id that collided would arrive here
                // already marked provisional and let a stopgap name be overwritten in a save that
                // never had one. Carrying it across a save boundary is exactly the class of bug this
                // method exists for.
                _provisionalNamePartyIds.Clear();

                // Cadence. Only the log latch now — the cadence itself is decided by the incoming
                // save's own LastCompletedTickMonth, which arrives with its state a few lines into
                // OnSidecarLoaded and cannot be inherited from city A.
                _lastTick = default(SimDate);
                _hasTicked = false;

                // Sensors cache per sim day against a world that has just been replaced.
                if (_snapshots != null) _snapshots.Invalidate();

                // The ledger, rebuilt rather than merely cleared: Shutdown releases the palette, and
                // IsInitialised reporting false is what stops the application system writing anything
                // in the window between here and the re-Initialize below.
                AgoraEffects.Shutdown();

                // Rebuilt only for a save that is arriving. Nothing arrives after ModShutdown, and
                // Initialize logs the palette coverage report, so doing it there is a line in
                // Agora.log for a palette that is about to be released again.
                if (cause == ResetCause.SaveBoundary && _tuning != null && _time != null)
                {
                    AgoraEffects.Initialize(_tuning, _time);
                    AgoraEffects.DistrictResolver = _districts != null
                        ? new SensorDistrictResolver(_districts)
                        : null;
                }

                // The per-save kill-switch (#10). OnSidecarLoaded overwrites it a few lines later with
                // the incoming save's own setting; this line is what stops city A's choice standing in
                // for city B's on any path that reaches here without a sidecar to read.
                AgoraEffects.EffectsEnabled = true;

                if (_effectApplication != null)
                {
                    // The one branch in this method, and the reason ResetCause exists. On
                    // SaveBoundary the old city's entities are still alive — ClearSystem runs later,
                    // in the deserialize phase onGamePreload precedes — so the revert would work; it
                    // is skipped because nothing would ever see it. That world is destroyed moments
                    // later, and its save file was written before this ran, so what it carries is
                    // fixed and gets reconciled on the next load by IsCarriedOver. On ModShutdown the
                    // city stays open and every one of those slots is a live buffer still carrying
                    // our contribution, so it is handed back here, while there is still an entity
                    // manager to hand it back to.
                    //
                    // Not left to AgoraEffectApplicationSystem.OnDestroy: that revert opens with an
                    // empty-table early-return, so anything that drops the table first silently
                    // disarms it. Reverting before the drop is what makes disabling Agora leave a
                    // stock city (non-negotiable #4 in spirit, and TryRevertAll's own contract).
                    if (cause == ResetCause.ModShutdown) _effectApplication.TryRevertAll();

                    _effectApplication.ForgetTrackedSlots();
                }

                _stateVersion++;
            }
        }

        // ------------------------------------------------------------------ per-save

        /// <summary>
        /// Applies everything the sidecar carried: the clock, the effect kill-switch, the flavor
        /// provider and the sensor caches.
        /// </summary>
        private static void OnSidecarLoaded(SidecarLoadResult result)
        {
            try
            {
                // First line, before any restore work: everything below writes per-save state, and a
                // reset that ran afterwards would undo it. See ResetForNewSave for what it covers.
                // SaveBoundary: this is the load path, one step behind OnGamePreload's own reset, so
                // the slot table it would revert is already empty — and any key that did survive
                // would name an entity of the outgoing city, which ClearSystem destroyed between that
                // reset and this one.
                ResetForNewSave(ResetCause.SaveBoundary);

                _saveSettings = (result != null && result.Settings != null)
                    ? result.Settings
                    : new CoreSettings();

                // "Never chosen a theme", which is a narrower question than "has no state" — a save
                // can have answered the prompt and then been closed before its first monthly tick
                // wrote a state file. Asking both is what stops that player being re-prompted and
                // offered the chance to discard their own choice. See IsFirstRun.
                _isFirstRun = result == null || (!result.HasState && result.SettingsAreDefaults);

                ConfigureClock();

                // Before anything that can capture a snapshot — CreateInitialState below does, and a
                // capture records the present month, so a history restored afterwards would be
                // overwritten by a series one sample long. ResetForNewSave (first line above) is what
                // cleared it, so this has to sit between the two.
                RestoreMetricHistory(result);

                // And the engine's own trend window, off the same document. Same ordering constraint
                // for the same reason: RecordSnapshot appends, so a history rebuilt after the first
                // capture would sit behind a sample taken this session rather than before it.
                RestoreSnapshotHistory(result);

                // Per-save kill-switch (#10). False computes all the politics and applies none of it.
                AgoraEffects.EffectsEnabled = _saveSettings.EffectsEnabled;

                // The save's voter-model levels, onto a fresh parse of the tuning file. Both halves
                // matter: re-reading is what makes a level of Default mean "the shipped coefficient"
                // rather than "whatever the previously loaded save left in the static", and applying
                // here — before CreateInitialState below — is what lets BrandDiscipline reach party
                // generation at all, since that runs once and never again.
                _tuning = LoadTuning();
                TuningPresets.Apply(_tuning, _saveSettings);

                // The political start date, and the phase anchor for every engine cadence. January of
                // the save's start year: the year is a per-save setting, and the day-of-month must not
                // matter to a month-granular calendar.
                _startDate = new SimDate(_saveSettings.StartYear, 1, 1);

                // Restore, or mint. A save that carried state resumes from it; one that did not is
                // starting its politics now, which is also what a save created before Agora was
                // installed looks like.
                _state = (result != null && result.HasState) ? result.State : null;

                if (_state == null)
                {
                    _state = PoliticalEngine.CreateInitialState(
                        SaveGuid, _startDate, _saveSettings, CaptureSnapshot(), Tuning);

                    AgoraMod.Log.Info("Agora: no prior political state; generated a fresh registry at " +
                                      _startDate + ".");
                }
                else
                {
                    // The settings in the state are the ones the sidecar just reconciled. Keeping the
                    // stale copy embedded in the snapshot would let a changed setting be silently
                    // reverted by the next save.
                    _state.Settings = _saveSettings;
                }

                // After the if/else, so one call site covers both branches, and before anything below
                // reads the state. On the freshly-minted branch it is a provable no-op — GenerateInitial
                // flags the NaArray prefix, which is exactly what the reconstruction returns — so it
                // doubles as a live assertion that generation and repair still agree.
                RepairLoadedState();

                // Same placement and the same reason: one call site for both branches, and before
                // anything below acts on the state.
                ClampWatermarkToClock();

                // Raised HERE, the moment there is a state to serve, and deliberately before the
                // flavor and replay work below.
                //
                // Everything from this line down is prose, catch-up and cosmetics; none of it decides
                // whether this save has politics. When it all sat inside the one try, a throw in any
                // of it fell to the catch below, which clears _saveActive — and _saveActive false is
                // what `enabled` publishes to the dashboard, so a failed flavor rebuild took the
                // whole layer down for the session: no panels, and no first-run prompt either, which
                // left the save silently on the initialiser theme with no surface left to change it
                // from. That is defect A of the parties-tab report. The state was fine the whole time.
                _saveActive = true;
                _stateVersion++;

                if (result != null && !string.IsNullOrEmpty(result.Explanation))
                {
                    AgoraMod.Log.Info("Agora sidecar: " + result.Explanation);
                }

                // One catch for the three cosmetic steps, and a second for the replay, because they
                // fail differently: prose that does not arrive is a blemish, a catch-up that does not
                // run leaves the state behind the clock and the next monthly tick has to be told.
                try
                {
                    RebuildFlavor();

                    // The pool cannot see the registry through IFlavorProvider — it is handed a
                    // snapshot and a date only — so the roster has to be pushed at it. Doing it here
                    // rather than waiting for the first wake is what lets the very first poll name
                    // anybody.
                    SeedFlavorRoster(_state.Date);
                }
                catch (Exception flavorEx)
                {
                    AgoraMod.Log.Warn("Agora flavor: the provider could not be built for this save (" +
                                      flavorEx.Message + "); the political layer runs without prose " +
                                      "until the next wake.");
                }

                try
                {
                    if (result != null && result.MonthsToReplay > 0) Replay(result.MonthsToReplay);
                }
                catch (Exception replayEx)
                {
                    AgoraMod.Log.Error(replayEx, "Agora could not replay the months this save missed; " +
                                                 "the state stands at " + _state.Date + " and the next " +
                                                 "monthly tick continues from there.");
                }

                // Last, and after the replay: a catch-up can found parties, and a party reaching the
                // dashboard without a name is the one thing this whole path exists to prevent. The
                // canned pool answers synchronously, so there is no frame in which the UI sees a blank.
                try
                {
                    EnsureEveryPartyNamed(_state.Date);
                }
                catch (Exception nameEx)
                {
                    AgoraMod.Log.Warn("Agora flavor: naming the unnamed parties failed (" + nameEx.Message +
                                      "); they stay unnamed until the next prose collection.");
                }

                // Reconcile the flag against the history on every load, not only when a tick or a
                // replay moves it. LockThemeIfElectionHeld's own contract is that ElectionHistory is
                // the authority — "a flag can be lost, a held election cannot" — and settings.json is
                // exactly where it can be lost: PersistSettings swallows a failed write, so a save
                // whose lock never reached disk would come back offering a choice it has spent. Idem-
                // potent and an early return once locked, so a save that resumed correctly pays a
                // comparison.
                LockThemeIfElectionHeld();

                AgoraMod.Log.Info("Agora: save active at " + _state.Date + "; theme " +
                                  _saveSettings.Theme + " (" + _saveSettings.System + "), " +
                                  _state.Parties.Count + " parties, " + _state.Factions.Count +
                                  " factions, themeLocked=" + _saveSettings.ThemeLocked +
                                  ", firstRunPrompt=" + _isFirstRun + ".");
            }
            catch (Exception ex)
            {
                _saveActive = false;
                AgoraMod.Log.Error(ex, "Agora could not apply per-save settings; continuing with defaults.");
            }
        }

        /// <summary>
        /// Enforces the one invariant the tick gate rests on: a watermark may never stand ahead of
        /// the month the city is actually in. A state that arrives dated into the future has its
        /// <see cref="PoliticalState.LastCompletedTickMonth"/> pulled down to the month before now, so
        /// that the current month is the next one to run.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The load this exists for.</b> <c>LoadReconciliation</c> returns
        /// <c>RewindBeforeHistory</c> when every political snapshot on disk is later than the city's
        /// date — the player rolled the city save back further than any state Agora has written, which
        /// <c>TickPlanner.SnapshotsToPrune</c> makes ordinarily reachable by keeping only the newest
        /// few. It hands back the <i>earliest</i> snapshot with nothing to replay, so <see cref="Replay"/>
        /// never runs and the watermark stays years ahead of the clock. Without this the gate would
        /// then suppress every month until the sim caught up, and the suppression would outlive the
        /// session because the watermark is on disk: no polls, no elections, no event ticks, one Info
        /// line a month. A month run twice is wrong once; a month never run does not come back.
        /// </para>
        /// <para>
        /// This is not a policy decision taken here. That outcome's own contract is that "the earliest
        /// snapshot supplies identity and settings, the engine rebuilds current state from city
        /// metrics" — which is ticking, from the city's date forward. The clamp restores the behaviour
        /// that reconciliation already documents and that the runtime had before the gate existed.
        /// </para>
        /// <para>
        /// Written as an invariant on the watermark rather than as a branch on
        /// <c>RewindBeforeHistory</c>, because the property is what the gate depends on and the
        /// outcome that violates it is not guaranteed to stay the only one.
        /// </para>
        /// <para>
        /// <b>Asserted at load and again on every heartbeat.</b> The load call is where the offending
        /// state actually arrives, but it can only run when the clock is readable — and a session in
        /// which it is not at that one moment would stay frozen for the whole session, which is the
        /// direction that does not recover. <see cref="Tick"/> holds a date that is readable by
        /// construction, so it asserts the same invariant a second time for the cost of one
        /// comparison a day. One helper for both, so the two cannot drift apart.
        /// </para>
        /// </remarks>
        private static void ClampWatermarkToClock()
        {
            if (_time == null) return;

            // Only from the clock, and only when it is readable: a comparison against a main-menu
            // date would be a comparison against nothing (non-negotiable #8). The next load repeats
            // it, and Tick asserts it again from a date that cannot be unreadable.
            SimDate today;
            if (!_time.TryGetToday(out today)) return;

            ClampWatermarkToClock(today);
        }

        /// <summary>
        /// The invariant itself, against a date the caller already has. See the overload above for
        /// why it exists and why it is asserted from two places.
        /// </summary>
        private static void ClampWatermarkToClock(SimDate today)
        {
            if (_state == null) return;

            // Ahead means strictly ahead. Level is the ordinary case, not a violation: OnMonth writes
            // the watermark to today's month on the first heartbeat of month M, so from that moment to
            // the end of M the watermark equals M — which is where almost every save, quit and reload
            // happens. Clamping on equality would pull it to M-1 and hand the next heartbeat the very
            // duplicate month, duplicated poll and double-counted FringeWatch.MonthsObserved that the
            // gate exists to remove, then persist the wrong watermark. RewindBeforeHistory is still
            // fully covered: it is reached only after the exact-match and nearest-earlier branches
            // have both failed, so every snapshot on disk — and the watermark inside the earliest of
            // them — is strictly later than the city's date.
            if (today.TotalMonths >= _state.LastCompletedTickMonth) return;

            int was = _state.LastCompletedTickMonth;
            _state.LastCompletedTickMonth = today.TotalMonths - 1;

            // EVERY watermark, not just the tick's. Wave 0 wrote this repair when there was exactly
            // one; wave 4 added three more — the two story phases and the power accrual — and each of
            // them gates its own subsystem behind the same "have we already run this month" question.
            // Repairing one and leaving three is what made three separate wave-4 lanes each look like
            // they had a rewind defect of their own: the cycle stalled for every month between the
            // city's date and the stale watermark, silently, with no log line and no story panel, and
            // the accrual froze while every debit stayed live. A save rolled back further than the
            // snapshot retention is a supported path, not an abuse — TickPlanner.SnapshotsToPrune
            // keeps only the newest few, so it is reachable in ordinary play.
            //
            // Pulled to today - 1 rather than to -1, so the guards read "this month has not run" and
            // not "no month has ever run": resetting to never would re-open a first-tick path that
            // reseeds the election calendar and the pool.
            int floor = today.TotalMonths - 1;

            int draftWas = _state.LastStoryDraftMonth;
            int resolveWas = _state.LastStoryResolveMonth;
            if (_state.LastStoryDraftMonth > floor) _state.LastStoryDraftMonth = floor;
            if (_state.LastStoryResolveMonth > floor) _state.LastStoryResolveMonth = floor;

            PoliticalPowerState power = _state.Power;
            int accrualWas = power != null ? power.LastAccrualMonth : -1;
            if (power != null && power.LastAccrualMonth > floor) power.LastAccrualMonth = floor;

            AgoraMod.Log.Info("Agora: the political state is dated ahead of the city (watermark month " +
                              was + ", city is at " + today + "). Reconciling the watermark to " +
                              _state.LastCompletedTickMonth + " so this month ticks — the " +
                              "RewindBeforeHistory path keeps the party system and settings and " +
                              "rebuilds current state from city metrics. Story watermarks " +
                              draftWas + "/" + resolveWas + " and power accrual " + accrualWas +
                              " were reconciled with it.");
        }

        private static void ConfigureClock()
        {
            if (_startYear == null) return;

            // AGORA-SEAM(persistence): Agora.Core.Contracts.AgoraSettings has no field for the stock
            // epoch year, so null is the only thing there is to pass. Configure leaves a value
            // captured earlier in THIS session alone, so the kill-switch reverts correctly within a
            // session; what it cannot yet do is revert after a restart. See the gate report.
            _startYear.Configure(StartYearDeliveryMode.RewriteGameEpoch, _saveSettings.StartYear, null);
        }

        /// <summary>
        /// Drops the flavor provider and builds a new one for the current theme. The load path's
        /// entry point; the retheme runs the two halves separately so that it can delete the cache
        /// between them.
        /// </summary>
        private static void RebuildFlavor()
        {
            DisposeFlavor();
            CreateFlavor(mayCaptureSnapshot: true);
        }

        /// <summary>
        /// Constructs the provider for the current theme. Assumes any previous one has already been
        /// disposed.
        /// </summary>
        /// <param name="mayCaptureSnapshot">
        /// Whether the catalog this is validated against may run the sensors. See
        /// <see cref="BuildFlavorCatalog"/> — false on the UI update tick.
        /// </param>
        private static void CreateFlavor(bool mayCaptureSnapshot)
        {
            Guid saveGuid = SaveGuid;
            if (saveGuid == Guid.Empty)
            {
                // No identity means no seed and no sidecar directory. Canned prose keyed off an empty
                // guid would be stable but wrong the moment the identity arrives, so publish nothing.
                AgoraMod.Log.Warn("Agora has no save identity yet; flavor is disabled for now.");
                return;
            }

            string directory = null;
            if (_sidecar != null && _sidecar.Store != null)
            {
                try
                {
                    directory = _sidecar.Store.EnsureDirectory(saveGuid);
                }
                catch (Exception ex)
                {
                    AgoraMod.Log.Warn("Agora could not prepare the sidecar directory for flavor (" +
                                      ex.Message + "); the last-good cache is in memory only.");
                }
            }

            // The ids the engine currently recognises. This is the input to cache re-validation, so it
            // has to describe the state we just restored: an empty catalog means "trust nothing
            // cached" and would throw away every name the last session generated.
            FlavorCatalog catalog = BuildFlavorCatalog(mayCaptureSnapshot);

            // Logged because the failure mode is silent: a catalog that came out short drops cached
            // entries one at a time, and the player sees names quietly revert with nothing in the log
            // to say why. Five counts here turn that into a one-line diagnosis.
            //
            // Stories are the fifth, and they were nearly the counterexample this comment warns
            // about: the count existed for a wave with no call site, while story ids are the entry
            // most likely to come back empty — they are minted per cycle, and a load that rebuilds
            // state without them (a lost state_*.json beside an intact flavor_cache.json, or a
            // rewind) drops every cached story entry with nothing above Debug to say so.
            AgoraMod.Log.Info("Agora flavor: cache re-validation catalog — " +
                              catalog.PartyCount + " parties, " +
                              catalog.FactionCount + " factions, " +
                              catalog.DistrictCount + " districts, " +
                              catalog.EventCount + " events, " +
                              catalog.StoryCount + " stories.");

            _flavor = FlavorProviders.Create(saveGuid, _saveSettings.Theme, directory, catalog);
        }

        private static void DisposeFlavor()
        {
            if (_flavor == null) return;

            try
            {
                _flavor.Dispose();
            }
            catch (Exception ex)
            {
                AgoraMod.Log.Warn("Agora flavor provider did not dispose cleanly: " + ex.Message);
            }

            _flavor = null;
        }

        // ------------------------------------------------------------------ per-save settings

        /// <summary>
        /// Writes the per-save settings to <c>settings.json</c> on their own, without waiting for the
        /// player to save the game.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Until this existed, <see cref="SidecarStore.SaveSettings"/> had no caller anywhere: settings
        /// reached disk only as a side effect of <see cref="SidecarStore.SaveState"/>, i.e. only on a
        /// game save. A theme chosen and then lost to a crash would have come back as the other theme
        /// with a different set of parties, which is the worst possible way to lose one setting.
        /// </para>
        /// <para>
        /// Every failure is swallowed. A settings write is a convenience — the value is already live in
        /// memory and <see cref="SidecarStore.SaveState"/> will write it again — and a full disk must
        /// not be able to take a session down from inside the UI update loop.
        /// </para>
        /// </remarks>
        private static void PersistSettings()
        {
            try
            {
                if (_sidecar == null || _sidecar.Store == null) return;

                Guid saveGuid = SaveGuid;
                if (saveGuid == Guid.Empty) return;

                _sidecar.Store.SaveSettings(saveGuid, SaveSettings);
            }
            catch (Exception ex)
            {
                AgoraMod.Log.Warn("Agora could not write settings.json (" + ex.Message + "); the " +
                                  "setting is live for this session and will be written with the save.");
            }
        }

        /// <summary>
        /// Reconciles the two things about a loaded state that are derivable from something else it
        /// carries, and that nothing on the load path used to re-derive: the electoral system, and
        /// which parties are the NA majors.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This exists because the v2 → v3 sidecar migration is a one-shot. It fires at one version
        /// boundary and never again, so a file already stamped 3 with wrong flags — written by a build
        /// whose reconstruction guessed from party ids — had no route back. A load-time reconciliation
        /// has no such boundary: it converges every save, at whatever version, on the first load.
        /// </para>
        /// <para>
        /// The invariant is a set comparison against the reconstruction, not a count. "Exactly
        /// <c>targetCountNa</c> majors" looks equivalent and is not: <c>PartyLifecycle.ApplyDeaths</c>
        /// has no protection for a major and the NA ballot floor is satisfiable by two minors, so a
        /// save can legitimately be down to one major. A count check would warn about that on every
        /// load, forever. The set form is silent when correct and also catches identity errors — green
        /// flagged, conservative not — that a count of two would wave through.
        /// </para>
        /// <para>
        /// The share clamp is deliberately limited to parties this pass just DEMOTED. Those had their
        /// shares computed with no ceiling aimed at them, so the recorded number is meaningless. A
        /// party that was already correctly minor is left alone, because once the failure streak
        /// unlocks, <c>FringeFailureModel.CeilingFor</c> opens toward <c>maxCeiling</c> and a legitimate
        /// surge well above the base ceiling is exactly what the packet is for — an unconditional clamp
        /// here would delete it on every reload.
        /// </para>
        /// <para>
        /// In its own try, like the flavor block above it: a reconciliation that throws must cost the
        /// save its flags, not its politics.
        /// </para>
        /// </remarks>
        private static void RepairLoadedState()
        {
            if (_state == null || _saveSettings == null) return;

            try
            {
                // Re-assert the system even though ResolveSettings now derives it: _saveSettings can
                // arrive from somewhere other than the store, and this is one comparison.
                ElectoralSystem derived = RegionThemeRules.SystemFor(_saveSettings.Theme);
                if (_saveSettings.System != derived)
                {
                    AgoraMod.Log.Warn("Agora: this save recorded " + _saveSettings.System + " under the " +
                                      _saveSettings.Theme + " theme, which is not a state the engine can " +
                                      "produce; running it as " + derived + ".");
                    _saveSettings.System = derived;
                }
                if (_state.Settings != null) _state.Settings.System = derived;

                int majorCount = _saveSettings.Theme == RegionTheme.Na ? Tuning.Parties.TargetCountNa : 0;

                // Anchored brand identity, before the major/minor flags and before that block's early
                // return. A save generated before the anchored catalog landed carries the palette's
                // colours in catalog order — which handed the liberal party red and the conservative
                // party blue — and whatever names the flavor pipeline invented. Nothing else in the
                // engine ever writes a party's identity, so without this the fix would only ever
                // reach saves created after it.
                BrandRepairResult brands = AnchoredBrandRepair.Apply(
                    _state.Parties, PartyArchetypes.For(_saveSettings.Theme), Tuning);

                if (brands.Changed)
                {
                    _stateVersion++;
                    AgoraMod.Log.Warn("Agora: repaired anchored party identities on load (" +
                                      brands.Summary + "). Platforms are untouched — a party's stance " +
                                      "is the record of how it has governed, and the blocs' previous " +
                                      "votes were taken against it.");
                }

                MajorRepairResult repair = NaMajorParties.Repair(
                    _state.Parties, NaMajorParties.DefaultMajorArchetypeIds(majorCount), majorCount);

                if (!repair.Changed) return;

                for (int i = 0; i < repair.Demoted.Count; i++)
                {
                    Party demoted = PartyRegistry.Find(_state.Parties, repair.Demoted[i]);
                    if (demoted == null) continue;
                    if (demoted.LastVoteShare > Tuning.Fringe.BaseCeiling)
                    {
                        demoted.LastVoteShare = Tuning.Fringe.BaseCeiling;
                    }
                }

                _stateVersion++;

                AgoraMod.Log.Warn("Agora: repaired the major/minor party flags on load (" +
                                  repair.Summary + "). A demoted party's last recorded vote share was " +
                                  "taken with no ceiling applied, so it has been capped at " +
                                  Tuning.Fringe.BaseCeiling.ToString("F2", CultureInfo.InvariantCulture) +
                                  "; the next tick recomputes the live standings.");
            }
            catch (Exception repairEx)
            {
                AgoraMod.Log.Warn("Agora: could not reconcile the loaded political state (" +
                                  repairEx.Message + "); it runs with the flags it was saved with.");
            }
        }

        /// <summary>
        /// Locks the region theme once the save has held its first election, and says so once.
        /// </summary>
        /// <remarks>
        /// Deliberately here and not inside <c>PoliticalEngine.RunElection</c> or
        /// <c>Advance</c>: both are documented pure, and <see cref="PoliticalState.Settings"/> is
        /// shared by reference across the engine's clone, so a write there would reach back into the
        /// caller's input. <see cref="PoliticalState.ElectionHistory"/> is the authority for the same
        /// reason <c>Retheme</c> reads it — a flag can be lost, a held election cannot.
        /// </remarks>
        private static void LockThemeIfElectionHeld()
        {
            if (_state == null || _saveSettings == null) return;
            if (_saveSettings.ThemeLocked) return;
            if (_state.ElectionHistory.Count == 0) return;

            _saveSettings.ThemeLocked = true;

            // Normally the same object — the engine's clone shares the settings reference — but a
            // retheme replaces it, so the two are re-synchronised rather than assumed equal.
            if (_state.Settings != null) _state.Settings.ThemeLocked = true;

            PersistSettings();
            _stateVersion++;

            AgoraMod.Log.Info("Agora: region theme " + _saveSettings.Theme + " locked at the first " +
                              "election; it is history from here.");
        }

        /// <summary>
        /// The one inbound write channel: applies a per-save setting the dashboard asked for, or says
        /// why it did not. Backs <c>agora.state.setSetting</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Synchronous on purpose. It runs from a <c>CallBinding</c> on the UI phase, which keeps
        /// ticking while the sim is paused — and <c>GameSimulation</c> does not, so deferring the work
        /// to the next engine tick would mean a player who chose a theme at speed zero saw nothing
        /// happen at all.
        /// </para>
        /// <para>
        /// No ECS work happens here for the same reason the getters do none (<c>ui_bindings.md</c> §7
        /// rule 6): this is the UI update tick. Where a snapshot is needed, the one the sensors
        /// already captured is used — which is why <see cref="BuildFlavorCatalog"/> takes a flag
        /// rather than deciding for itself. <c>AgoraSnapshotSystem.Capture</c> is not a getter: on a
        /// day it has not yet sampled it runs every sensor family's query, so the retheme path passes
        /// false and lives with <see cref="_lastSnapshot"/>.
        /// </para>
        /// </remarks>
        /// <returns>
        /// <see cref="CommandOutcome.Ok"/> when the value took. Every other member is a short
        /// engine-authored reason; no exception text ever reaches the panel.
        /// </returns>
        public static CommandOutcome SetSetting(string key, string value)
        {
            lock (Gate)
            {
                try
                {
                    if (!_attached || !_saveActive || _state == null || _saveSettings == null)
                        return CommandOutcome.NoActiveSave;

                    switch (key)
                    {
                        case "theme":
                            return SetTheme(value);

                        case "pauseOnMajorNews":
                            return SetFlag(value, v => _saveSettings.PauseOnMajorNews = v);

                        case "showAllReports":
                            return SetFlag(value, v => _saveSettings.ShowAllReports = v);

                        case "effectsEnabled":
                            return SetFlag(value, v =>
                            {
                                _saveSettings.EffectsEnabled = v;

                                // The same second write the load path makes: the per-save switch lives
                                // in the settings object, but the effect layer reads its own copy, and
                                // a value set in only one of the two is a kill switch that does not
                                // kill anything (non-negotiable #10, and OnSidecarLoaded).
                                AgoraEffects.EffectsEnabled = v;
                            });

                        case "voteSharpness":
                            return SetLevel<VoteSharpness>(value, v => _saveSettings.VoteSharpness = v);

                        case "newsInfluence":
                            return SetLevel<NewsInfluence>(value, v => _saveSettings.NewsInfluence = v);

                        case "brandDiscipline":
                            return SetLevel<BrandDiscipline>(value, v => _saveSettings.BrandDiscipline = v);

                        // ---- the story layer (wave 6) ----
                        //
                        // These six have been in the sidecar since wave 2 and reachable from nothing
                        // since. They are ordinary per-save settings and take the ordinary helpers;
                        // nothing here retro-generates or cancels a story, because a setting change is
                        // not a tick. Turning stories off leaves whatever is live exactly where it is
                        // and stops the next draft — the story cycle reads the flag on the draft phase
                        // and nowhere else, so a live story still resolves rather than being stranded.

                        case "storiesEnabled":
                            return SetFlag(value, v => _saveSettings.StoriesEnabled = v);

                        case "storiesPerCycle":
                            return SetCount(value, v => _saveSettings.StoriesPerCycle = v);

                        case "eventsPerStory":
                            return SetCount(value, v => _saveSettings.EventsPerStory = v);

                        case "politicalPowerEnabled":
                            return SetFlag(value, v => _saveSettings.PoliticalPowerEnabled = v);

                        // ---- the two levels that had no write key until wave 7 ----
                        //
                        // Wave 6 deliberately withheld these because `TuningPresets.Apply` read three
                        // levels and no fourth or fifth, so a control would have persisted a value and
                        // changed no number — the defect W5 closed for PauseOnMajorNews. Wave 7's
                        // spine opens the key and wave 7b lands the preset tables behind it, in the
                        // same wave and in the declared merge order, so the setting and its effect
                        // reach a player together. Do not merge 7b's row without them.

                        case "powerIntensity":
                            return SetLevel<PowerIntensity>(value, v => _saveSettings.PowerIntensity = v);

                        case "storyDifficulty":
                            return SetLevel<StoryDifficulty>(value, v => _saveSettings.StoryDifficulty = v);

                        case "pauseOnMajorStory":
                            // Governs only whether the clock stops, never whether the card appears.
                            // Separate from pauseOnMajorNews on purpose — that control's hint
                            // enumerates news categories, so neither of its positions is an answer
                            // about stories.
                            return SetFlag(value, v => _saveSettings.PauseOnMajorStory = v);

                        case "dismissFirstRun":
                            // Not a setting and not persisted — the player closed the prompt. It is
                            // here because it is a one-shot lifecycle signal on the same object, and
                            // giving it a binding of its own would be a second write channel for one
                            // boolean that is never written to disk.
                            _isFirstRun = false;
                            _stateVersion++;
                            return CommandOutcome.Ok;

                        default:
                            return CommandOutcome.UnknownKey;
                    }
                }
                catch (Exception ex)
                {
                    // Whatever it was stays in the log. An escaping exception here is inside the UI
                    // update loop, and the panel gets a code it can render rather than a stack trace.
                    AgoraMod.Log.Error(ex, "Agora: setting '" + (key ?? "(null)") + "' could not be " +
                                           "applied; the previous value stands.");
                    return CommandOutcome.Failed;
                }
            }
        }

        /// <summary>Parses a boolean, applies it, persists and republishes. Rejects anything else.</summary>
        private static CommandOutcome SetFlag(string value, Action<bool> apply)
        {
            bool parsed;
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) parsed = true;
            else if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) parsed = false;
            else return CommandOutcome.BadValue;

            apply(parsed);
            PersistSettings();
            _stateVersion++;
            return CommandOutcome.Ok;
        }

        /// <summary>
        /// Upper bound on the two story counts a player may set from the panel.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Not a balance number — a bound on what a settings control may ask for.</b> Wave 2's
        /// concurrency retune sized the story layer's effect budget and its non-saturation claim
        /// around the shipped 2 stories × 3 events; asking for an order of magnitude more is a
        /// rebalance, and a rebalance belongs in <c>engine_tuning.json</c> where the effect scales and
        /// the pool size move with it. Five is comfortably above anything the shipped tuning is sized
        /// for and comfortably below a number that would exhaust the pool in a cycle.
        /// </para>
        /// <para>
        /// Zero is legal and means "unset": <c>StoryAssembler</c> resolves a count of zero against
        /// <c>stories.storiesPerCycle</c> / <c>stories.eventsPerStory</c>, which is how a player hands
        /// the decision back to tuning. That is <c>TickPlanner.SnapshotsToPrune</c>'s convention, and
        /// it is why the floor here is 0 rather than 1.
        /// </para>
        /// </remarks>
        private const int StoryCountMax = 5;

        /// <summary>
        /// Parses a small non-negative count, applies it, persists and republishes. Rejects anything
        /// that is not a plain decimal integer within <c>[0, <see cref="StoryCountMax"/>]</c>.
        /// </summary>
        /// <remarks>
        /// Parsed with <see cref="CultureInfo.InvariantCulture"/> rather than the ambient culture: the
        /// value crosses the bridge as a string the panel built, and a culture that formats or parses
        /// digits differently must not change which number a save takes.
        /// </remarks>
        private static CommandOutcome SetCount(string value, Action<int> apply)
        {
            if (string.IsNullOrEmpty(value)) return CommandOutcome.BadValue;

            int parsed;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed))
                return CommandOutcome.BadValue;

            if (parsed < 0 || parsed > StoryCountMax) return CommandOutcome.BadValue;

            apply(parsed);
            PersistSettings();
            _stateVersion++;
            return CommandOutcome.Ok;
        }

        /// <summary>
        /// Parses one of the voter-model levels by enum name, applies it, re-derives the tuning,
        /// persists and republishes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The tuning is re-read from disk, not patched in place.</b>
        /// <c>TuningPresets.Apply</c> is one-way — it overwrites a coefficient and keeps no record of
        /// what was there before — so moving a level back to <c>Default</c> is only expressible as
        /// "parse the file again and apply the new levels to that". Patching the live instance
        /// instead would make Default mean "whatever the last non-default level left behind".
        /// </para>
        /// <para>
        /// Parsed by name and case-sensitively. <c>Enum.TryParse</c> also accepts a bare number, and
        /// a panel that sent "2" would silently select whichever member happens to sit at 2 today, so
        /// an all-digit value is rejected before it is parsed.
        /// </para>
        /// </remarks>
        private static CommandOutcome SetLevel<T>(string value, Action<T> apply) where T : struct
        {
            if (string.IsNullOrEmpty(value)) return CommandOutcome.BadValue;

            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] >= '0' && value[i] <= '9') return CommandOutcome.BadValue;
            }

            T parsed;
            if (!Enum.TryParse(value, /*ignoreCase:*/ false, out parsed)) return CommandOutcome.BadValue;
            if (!Enum.IsDefined(typeof(T), parsed)) return CommandOutcome.BadValue;

            apply(parsed);

            _tuning = LoadTuning();
            TuningPresets.Apply(_tuning, _saveSettings);

            PersistSettings();
            _stateVersion++;
            return CommandOutcome.Ok;
        }

        /// <summary>
        /// The player answered an alert: drops it from the queue, or drops all of them when the id is
        /// the sentinel <c>"*"</c>. Backs <c>agora.news.ackAlert</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A call rather than a trigger for the same reason <see cref="SetSetting"/> is one: if the
        /// ack did not land, the modal must not close over an alert the engine still holds, and a
        /// trigger cannot say so.
        /// </para>
        /// <para>
        /// <b>Acking an id the queue no longer holds is <see cref="CommandOutcome.Ok"/>, not
        /// <see cref="CommandOutcome.NotFound"/>.</b> A double-click, or a second dismiss racing the
        /// republish, is not something the player did wrong, and a refusal there would put a worrying
        /// sentence in front of someone who did nothing.
        /// </para>
        /// <para>
        /// Synchronous, and doing no ECS work, for the reasons set out at <see cref="SetSetting"/>:
        /// this runs on the UI phase, which keeps ticking while the sim is held paused — and it will
        /// be, because a major alert holds the pause barrier while it is up.
        /// </para>
        /// <para>
        /// Nothing is persisted. The queue is session state, so unlike <see cref="SetFlag"/> there is
        /// no <see cref="PersistSettings"/> call here; adding one would write the sidecar on every
        /// dismiss.
        /// </para>
        /// </remarks>
        public static CommandOutcome AckAlert(string id)
        {
            lock (Gate)
            {
                try
                {
                    if (string.IsNullOrEmpty(id)) return CommandOutcome.BadValue;

                    if (string.CompareOrdinal(id, "*") == 0)
                    {
                        _alerts.Clear();
                    }
                    else
                    {
                        for (int i = 0; i < _alerts.Count; i++)
                        {
                            if (_alerts[i] == null || string.CompareOrdinal(_alerts[i].Id, id) != 0)
                                continue;

                            _alerts.RemoveAt(i);
                            break;
                        }
                    }

                    // Not optional, and not tidiness. AgoraUISystemBase.OnUpdate republishes only when
                    // StateVersion has moved, so without this bump agora.news.alerts never updates and
                    // the modal stays on a card the engine has already dropped. The queue advances on
                    // player input, not on an engine tick, and this is the only thing that tells the
                    // publisher so. Do not remove it.
                    _stateVersion++;
                    return CommandOutcome.Ok;
                }
                catch (Exception ex)
                {
                    AgoraMod.Log.Error(ex, "Agora: an alert could not be dismissed; the queue is " +
                                           "unchanged and the card stays up.");
                    return CommandOutcome.Failed;
                }
            }
        }

        /// <summary>
        /// Changes the save's region theme, if the engine allows it, and rebuilds everything downstream
        /// of the party registry it just replaced.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The order below is not interchangeable, and the first step is the subtle one. The old
        /// provider is disposed <i>before</i> the cache file is deleted, because the CLI worker writes
        /// <c>flavor_cache.json</c> after it has already left
        /// <see cref="FlavorProviderState.Running"/>; deleting first would let a generation that was
        /// finishing write the old theme's prose back on top of the delete, and every id in it
        /// validates against the new catalog. <see cref="ClaudeCliProvider.Dispose"/> joins the worker,
        /// so once <see cref="DisposeFlavor"/> returns the write has landed and the delete removes it.
        /// The join is bounded, which is why <see cref="PendingWake"/> also refuses the command while
        /// the worker is on its feet — the ordering alone is not a guarantee.
        /// </para>
        /// <para>
        /// After that: the new provider is constructed against the new catalog, the roster is re-seeded
        /// because the canned pool learns the registry from nowhere else, and the synchronous namer
        /// runs last because it is what puts a name on a party before the next frame renders it.
        /// </para>
        /// </remarks>
        private static CommandOutcome SetTheme(string value)
        {
            // Logged on entry, and on every exit below, because the first question any report of
            // "the region choice does nothing" asks is whether this method ran at all — and a
            // refusal, a no-op and a prompt that never rendered are three different bugs that look
            // identical from the player's chair. One line each, on a path a player takes twice a save.
            AgoraMod.Log.Info("Agora: setTheme(\"" + value + "\") requested; current theme " +
                              _saveSettings.Theme + ", themeLocked=" + _saveSettings.ThemeLocked + ".");

            RegionTheme theme;
            if (string.Equals(value, "Eu", StringComparison.OrdinalIgnoreCase)) theme = RegionTheme.Eu;
            else if (string.Equals(value, "Na", StringComparison.OrdinalIgnoreCase)) theme = RegionTheme.Na;
            else
            {
                AgoraMod.Log.Warn("Agora: setTheme refused — \"" + value + "\" is not a region.");
                return CommandOutcome.BadValue;
            }

            // Refused rather than queued. A retheme disposes the flavor provider and deletes its cache
            // file, and doing either with a claude subprocess in flight races the runner's own
            // completion path — Dispose's join is bounded at two seconds and can return with the worker
            // still alive, so the ordering below is a guard and not a proof. PendingWake stays true
            // until the worker has finished with the disk, which is later than the state says. The
            // player can press the button again in a few seconds.
            if (theme != _saveSettings.Theme && PendingWake)
            {
                AgoraMod.Log.Info("Agora: setTheme deferred — a flavor generation is in flight. The " +
                                  "player may press again once it finishes.");
                return CommandOutcome.Busy;
            }

            RegionTheme previous = _saveSettings.Theme;
            RethemeResult retheme = PoliticalEngine.Retheme(_state, theme, _startDate, Tuning);

            if (!retheme.Accepted)
            {
                AgoraMod.Log.Info("Agora: setTheme refused by the engine (" + retheme.Outcome +
                                  "); the save stays on " + previous + ".");
                return retheme.Outcome;
            }

            if (retheme.Changed)
            {
                _state = retheme.State;
                _saveSettings = _state.Settings;

                // Every one of these is keyed to a party id whose meaning just changed. The ids
                // themselves are unchanged — party-01 exists under both themes — so nothing downstream
                // would ever reject them; they have to be dropped here or not at all.
                _provisionalNamePartyIds.Clear();
                _flavorPayload = null;
                _lastFlavorDate = null;
                _lastAttemptDate = null;
                _pendingWake = false;
                _lastFlavorState = FlavorProviderState.Idle;

                // Dispose, then delete, then create — see the remarks. Disposing first is what stops a
                // finishing generation writing the file back after the delete.
                DisposeFlavor();

                // The file, not just the provider. flavor_cache.json names party-01 and the new
                // catalog contains party-01, so the catalog filter has no reason to drop it and the
                // old theme's prose would be restored verbatim onto the new theme's parties.
                DeleteFlavorCache();

                // No fresh sensor capture: this runs on the UI update tick. The catalog's district
                // half comes from the last reading the sensors took (ui_bindings.md §7 rule 6).
                CreateFlavor(mayCaptureSnapshot: false);
                SeedFlavorRoster(_state.Date);
                EnsureEveryPartyNamed(_state.Date);

                PersistSettings();

                // The electoral system and the faction count are on this line for a reason: they are
                // the two things the player reports as "not applying", and they are derived rather
                // than chosen — System from the theme, the factions from FactionModel.AppliesTo — so
                // the log has to show the derivation landed, not merely that the theme field moved.
                AgoraMod.Log.Info("Agora: region theme changed from " + previous + " to " + theme +
                                  "; system now " + _saveSettings.System + ", regenerated " +
                                  _state.Parties.Count + " parties and " + _state.Factions.Count +
                                  " factions at " + _startDate + ".");
            }
            else
            {
                AgoraMod.Log.Info("Agora: setTheme accepted as a no-op; the save was already " +
                                  theme + ".");
            }

            // Cleared on a no-op too: the player answered the prompt either way.
            _isFirstRun = false;
            _stateVersion++;
            return CommandOutcome.Ok;
        }

        /// <summary>
        /// Removes <c>flavor_cache.json</c>. Called on an accepted retheme, and nowhere else — the
        /// cache is the only thing that survives a provider rebuild, and its contents are keyed to
        /// party ids that have just been redefined.
        /// </summary>
        private static void DeleteFlavorCache()
        {
            try
            {
                if (_sidecar == null || _sidecar.Store == null) return;

                Guid saveGuid = SaveGuid;
                if (saveGuid == Guid.Empty) return;

                string path = Path.Combine(_sidecar.Store.DirectoryFor(saveGuid), FileFlavorCache.FileName);
                if (!File.Exists(path)) return;

                File.Delete(path);
                AgoraMod.Log.Info("Agora flavor: discarded the cached prose; it described the previous " +
                                  "theme's parties under the same ids.");
            }
            catch (Exception ex)
            {
                AgoraMod.Log.Warn("Agora flavor: could not delete the stale prose cache (" + ex.Message +
                                  "); it may restore the previous theme's names on the next load.");
            }
        }

        // ------------------------------------------------------------------ party identity

        // The player's six edit channels for a party's identity. Every one of them follows the shape
        // SetSetting established, for the same reasons: the Gate, the active-save guard, and a catch
        // that logs the exception and hands back CommandOutcome.Failed. No exception text ever crosses
        // the bridge — the panel switches on the value (docs/contracts/ui_bindings.md §4.5), and a
        // stack trace rendered in a tooltip is neither actionable nor translatable.
        //
        // Validation is Agora.Core's: PartyIdentity owns the limits and PartyRegistry owns the
        // roster-wide questions. Nothing here decides what a legal name is, because that rule has to be
        // testable without the game and this assembly is not.
        //
        // WHAT PERSISTS. Not PersistSettings() — that writes settings.json, and a party's name is
        // state, not a setting. Party edits mutate the live Party objects inside _state, which is the
        // same object GetStateForSave hands to AgoraSidecarSystem.PreSerialize, so the edit reaches
        // state_*.json with the player's next game save and no sooner. That is deliberate rather than
        // merely convenient: writing the state blob out of band from the UI update tick would race the
        // save path for the same file, and an edit that survives a crash the surrounding city does not
        // is a sidecar describing a city that never existed (#6). _stateVersion++ is what makes the
        // change visible immediately — the dashboard publishers watch it and republish on the next UI
        // tick, so the panel redraws in the same frame the player let go of the field.
        //
        // RESETS ARE IDEMPOTENT. Resetting a field the player never locked is a no-op that returns Ok,
        // in all three cases. The alternative — an error code for "there was nothing to undo" — makes a
        // reset button that is safe to press twice look broken the second time, and gives the panel a
        // failure state it has nothing useful to say about.

        /// <summary>
        /// Renames a party on the player's behalf and takes the name out of flavor's hands for good.
        /// Backs <c>agora.parties.rename</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Both fields at once, because <see cref="PartyOverrides.NameLocked"/> covers both. A rename
        /// that set only <see cref="Party.Name"/> would still lock <see cref="Party.ShortName"/>,
        /// freezing whatever the generator last happened to put there with no way to change it.
        /// </para>
        /// <para>
        /// The party leaves <see cref="_provisionalNamePartyIds"/> here, and that line is not tidying.
        /// It makes <i>"provisional implies not player-owned"</i> true by construction. Without it the
        /// invariant holds only because <see cref="ApplyProseNames"/> happens to consult the lock
        /// before it consults the set — an ordering nothing states and nothing tests, and one that a
        /// future fourth call site would have no reason to preserve.
        /// </para>
        /// </remarks>
        public static CommandOutcome RenameParty(string partyId, string name, string shortName)
        {
            lock (Gate)
            {
                try
                {
                    if (!_attached || !_saveActive || _state == null) return CommandOutcome.NoActiveSave;

                    Party party = PartyRegistry.Find(_state.Parties, partyId);
                    if (party == null) return CommandOutcome.NotFound;

                    CommandOutcome valid = PartyIdentity.ValidateName(name, shortName);
                    if (valid != CommandOutcome.Ok) return valid;

                    party.Name = name;
                    party.ShortName = shortName;
                    party.PlayerOverrides |= PartyOverrides.NameLocked;

                    // See the remarks: the invariant, not housekeeping.
                    _provisionalNamePartyIds.Remove(party.Id);

                    _stateVersion++;
                    return CommandOutcome.Ok;
                }
                catch (Exception ex)
                {
                    AgoraMod.Log.Error(ex, "Agora: party '" + (partyId ?? "(null)") + "' could not be " +
                                           "renamed; the previous name stands.");
                    return CommandOutcome.Failed;
                }
            }
        }

        /// <summary>
        /// Writes the player's own description and slogan onto a party and stops flavor rewriting
        /// them. Backs <c>agora.parties.setDescription</c>.
        /// </summary>
        /// <remarks>
        /// One lock for the pair, as with <see cref="RenameParty"/>: <see cref="PartyOverrides"/> maps
        /// <see cref="PartyOverrides.DescriptionLocked"/> onto <see cref="Party.Description"/> and
        /// <see cref="Party.Slogan"/> together, so they are taken together or not at all.
        /// </remarks>
        public static CommandOutcome SetPartyDescription(string partyId, string description, string slogan)
        {
            lock (Gate)
            {
                try
                {
                    if (!_attached || !_saveActive || _state == null) return CommandOutcome.NoActiveSave;

                    Party party = PartyRegistry.Find(_state.Parties, partyId);
                    if (party == null) return CommandOutcome.NotFound;

                    CommandOutcome valid = PartyIdentity.ValidateDescription(description, slogan);
                    if (valid != CommandOutcome.Ok) return valid;

                    party.Description = description;
                    party.Slogan = slogan;
                    party.PlayerOverrides |= PartyOverrides.DescriptionLocked;

                    _stateVersion++;
                    return CommandOutcome.Ok;
                }
                catch (Exception ex)
                {
                    AgoraMod.Log.Error(ex, "Agora: the description for party '" + (partyId ?? "(null)") +
                                           "' could not be applied; the previous text stands.");
                    return CommandOutcome.Failed;
                }
            }
        }

        /// <summary>
        /// Gives a party the colour the player picked. Backs <c>agora.parties.setColor</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The <b>normalised</b> form is what gets stored, and that is the whole point of this method
        /// existing rather than a raw assignment. Duplicate detection compares ordinally against a
        /// palette that is upper case, so <c>#c0392b</c> as a player types it is byte-different from
        /// the palette's <c>#C0392B</c> and simply never registers as taken — the next splinter is
        /// handed a colour nobody can tell apart from this one on any chart.
        /// </para>
        /// <para>
        /// <see cref="CommandOutcome.OkColorInUse"/> is an <b>acceptance</b>. The colour is applied
        /// either way; the code exists so the panel can say "another party already wears this" instead
        /// of leaving the player to discover it from a seat chart with two identical slices. The party
        /// is excluded from the scan, or its own freshly written colour would flag itself every time.
        /// </para>
        /// </remarks>
        public static CommandOutcome SetPartyColor(string partyId, string colorHex)
        {
            lock (Gate)
            {
                try
                {
                    if (!_attached || !_saveActive || _state == null) return CommandOutcome.NoActiveSave;

                    Party party = PartyRegistry.Find(_state.Parties, partyId);
                    if (party == null) return CommandOutcome.NotFound;

                    CommandOutcome valid = PartyIdentity.ValidateColor(colorHex);
                    if (valid != CommandOutcome.Ok) return valid;

                    string normalised = PartyIdentity.NormalizeHex(colorHex);

                    party.ColorHex = normalised;
                    party.PlayerOverrides |= PartyOverrides.ColorLocked;

                    _stateVersion++;

                    return PartyRegistry.IsColorTaken(_state.Parties, normalised, partyId)
                        ? CommandOutcome.OkColorInUse
                        : CommandOutcome.Ok;
                }
                catch (Exception ex)
                {
                    AgoraMod.Log.Error(ex, "Agora: the colour for party '" + (partyId ?? "(null)") +
                                           "' could not be applied; the previous colour stands.");
                    return CommandOutcome.Failed;
                }
            }
        }

        /// <summary>
        /// Hands a party's name back to flavor. Backs <c>agora.parties.resetName</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Clearing the lock is only half of it. A cleared lock on a party that still carries the
        /// player's name would leave that name in place indefinitely —
        /// <see cref="ApplyProseNames"/> declines to rename any party whose name is non-empty and not
        /// provisional — so the fields are blanked and <see cref="EnsureEveryPartyNamed"/> is run
        /// immediately, which normally puts a generated name back before the panel redraws rather
        /// than showing a blank party until the next prose collection, possibly months of sim time
        /// away.
        /// </para>
        /// <para>
        /// <b>"Normally" is the honest word.</b> The namer fails closed, and none of its failures is
        /// reported here. Three are method-level and abandon the whole roster: it returns at once if
        /// the runtime has no state or the flavor provider or its canned pool is absent, it logs and
        /// returns if <c>Generate</c> throws, and it returns if <c>Generate</c> hands back no
        /// document. Two more sit inside the per-party loop and abandon one party even though a
        /// document arrived: the document carries no usable entry for that party — none at all, or
        /// only entries that are null, name no party, or name a party not on the roster — or it
        /// carries an entry for the party whose <c>Name</c> is empty, which the loop refuses rather
        /// than let a nameless entry count as a naming. (<see cref="PartyIdentity.ApplyFlavor"/>'s own
        /// name lock is a sixth door on paper only: this command clears the lock before calling, and
        /// a blank name cannot be locked in the first place, since
        /// <see cref="PartyIdentity.ValidateName"/> rejects one.) On any of those the party is left
        /// nameless and the panel shows its placeholder until the next prose wake, where
        /// <see cref="ApplyProseNames"/> renames it —
        /// the name is empty, so <c>mayRename</c> is true there — and the gap self-heals. That is the
        /// right trade: a nameless party for a wake is recoverable, a command that fails because the
        /// pool is missing is not.
        /// </para>
        /// <para>
        /// <b>The date is <see cref="PoliticalState.Date"/>, not a computed one</b> (non-negotiable
        /// #8). It is the same argument <see cref="SetTheme"/> passes from the same UI-thread command
        /// path, which is also the evidence that the call is safe here: the namer is synchronous, it
        /// reaches only the canned pool's <c>Generate</c>, and it calls nothing on this type — so
        /// there is no re-entry into any of these six entry points, and the <see cref="Gate"/> it
        /// re-enters is a monitor this thread already holds.
        /// </para>
        /// <para>
        /// <b>Asymmetric with <see cref="ResetPartyDescription"/> on purpose</b>, and the panel has to
        /// say so: this one visibly re-rolls the name on the spot, that one does not touch the text.
        /// </para>
        /// </remarks>
        public static CommandOutcome ResetPartyName(string partyId)
        {
            lock (Gate)
            {
                try
                {
                    if (!_attached || !_saveActive || _state == null) return CommandOutcome.NoActiveSave;

                    Party party = PartyRegistry.Find(_state.Parties, partyId);
                    if (party == null) return CommandOutcome.NotFound;

                    if ((party.PlayerOverrides & PartyOverrides.NameLocked) == 0) return CommandOutcome.Ok;

                    party.PlayerOverrides &= ~PartyOverrides.NameLocked;
                    party.Name = "";
                    party.ShortName = "";

                    // Synchronous, and it is what stops the party being nameless on the very next
                    // frame. It is also the path the description lock has to survive: this party's
                    // description may well still be the player's.
                    EnsureEveryPartyNamed(_state.Date);

                    _stateVersion++;
                    return CommandOutcome.Ok;
                }
                catch (Exception ex)
                {
                    AgoraMod.Log.Error(ex, "Agora: the name of party '" + (partyId ?? "(null)") +
                                           "' could not be reset.");
                    return CommandOutcome.Failed;
                }
            }
        }

        /// <summary>
        /// Hands a party's description and slogan back to flavor. Backs
        /// <c>agora.parties.resetDescription</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The text is left exactly as it stands</b>, which is the one place this differs from
        /// <see cref="ResetPartyName"/> and a difference the panel must state rather than leave the
        /// player to infer. There is no cheap regenerate-a-description path: the canned pool's namer
        /// only touches parties with no name, and asking the CLI is tens of seconds away and may never
        /// answer at all. Blanking the field and waiting would leave the party with an empty
        /// description for however long that takes — up to the next yearly wake, months of sim time.
        /// Flavor reclaims the field at that wake, because the lock is now clear and
        /// <see cref="PartyIdentity.ApplyFlavor"/> writes an unlocked description on every pass.
        /// </para>
        /// <para>
        /// So the observable effect of this command is a promise about the future, not a change now.
        /// Saying that plainly in the UI is cheaper than the support question.
        /// </para>
        /// </remarks>
        public static CommandOutcome ResetPartyDescription(string partyId)
        {
            lock (Gate)
            {
                try
                {
                    if (!_attached || !_saveActive || _state == null) return CommandOutcome.NoActiveSave;

                    Party party = PartyRegistry.Find(_state.Parties, partyId);
                    if (party == null) return CommandOutcome.NotFound;

                    if ((party.PlayerOverrides & PartyOverrides.DescriptionLocked) == 0)
                        return CommandOutcome.Ok;

                    party.PlayerOverrides &= ~PartyOverrides.DescriptionLocked;

                    _stateVersion++;
                    return CommandOutcome.Ok;
                }
                catch (Exception ex)
                {
                    AgoraMod.Log.Error(ex, "Agora: the description lock on party '" +
                                           (partyId ?? "(null)") + "' could not be cleared.");
                    return CommandOutcome.Failed;
                }
            }
        }

        /// <summary>
        /// Gives a party back an engine-allocated colour. Backs <c>agora.parties.resetColor</c>.
        /// </summary>
        /// <remarks>
        /// <b>A reassignment from today's registry, not a restore of the launch colour.</b>
        /// <see cref="PartyRegistry.RegenerateColor"/> scans the palette from the slot this party's
        /// ordinal originally drew from and takes the first entry nobody holds — so if the colour it
        /// launched with has since gone to another brand, founded while the player was wearing a
        /// custom one, the party legitimately comes back a different colour. That is the honest
        /// outcome; the alternative is two brands sharing a colour, which is worse and permanent.
        /// </remarks>
        public static CommandOutcome ResetPartyColor(string partyId)
        {
            lock (Gate)
            {
                try
                {
                    if (!_attached || !_saveActive || _state == null) return CommandOutcome.NoActiveSave;

                    Party party = PartyRegistry.Find(_state.Parties, partyId);
                    if (party == null) return CommandOutcome.NotFound;

                    if ((party.PlayerOverrides & PartyOverrides.ColorLocked) == 0) return CommandOutcome.Ok;

                    party.PlayerOverrides &= ~PartyOverrides.ColorLocked;
                    party.ColorHex = PartyRegistry.RegenerateColor(_state.Parties, party.Id, Tuning);

                    _stateVersion++;
                    return CommandOutcome.Ok;
                }
                catch (Exception ex)
                {
                    AgoraMod.Log.Error(ex, "Agora: the colour of party '" + (partyId ?? "(null)") +
                                           "' could not be reset.");
                    return CommandOutcome.Failed;
                }
            }
        }

        // ------------------------------------------------------------------ cadence

        /// <summary>
        /// The day heartbeat's entry point. Returns false when nothing was done, which is the normal
        /// case for the great majority of calls.
        /// </summary>
        /// <remarks>
        /// The caller has already established that the date changed; this decides what kind of change
        /// it was. A month boundary is <see cref="SimDate.TotalMonths"/> moving, never a day count —
        /// the political calendar is month-granular and must not care how many days a month has.
        /// </remarks>
        public static bool Tick(SimDate today)
        {
            if (!_attached || !_saveActive) return false;

            var settings = AgoraMod.Settings;
            if (settings == null || !settings.Enabled) return false;
            if (!_saveSettings.Enabled) return false;

            // Before the gate reads it, because the gate is only as good as the invariant beneath it,
            // and this date is readable by construction where the load-time assertion's is not. A
            // no-op on every heartbeat but the one after a state arrived dated ahead of the city.
            ClampWatermarkToClock(today);

            // The gate, and it is read off persisted state rather than off anything this session
            // remembers: a month may run only when it is strictly newer than the watermark the last
            // completed month wrote. Strictly newer, not merely different — a clock that moved
            // backwards (a §5 reconciliation onto an earlier snapshot) must not re-run months the
            // state has already lived through either.
            bool monthChanged = _state != null && today.TotalMonths > _state.LastCompletedTickMonth;

            // What the old session-local test would have answered. Kept only to say so in the log on
            // the one heartbeat where the two disagree — the first after a load, which is where the
            // duplicated poll and the double-counted FringeWatch.MonthsObserved used to come from.
            // See the field declarations for why that test could never be right.
            bool sessionLatchWouldHaveRun = !_hasTicked || today.TotalMonths != _lastTick.TotalMonths;

            if (sessionLatchWouldHaveRun && !monthChanged)
            {
                AgoraMod.Log.Info("Agora: " + today + " has already been ticked (watermark month " +
                                  (_state != null ? _state.LastCompletedTickMonth : -1) +
                                  "); not running it again. Last tick this session: " +
                                  (_hasTicked ? _lastTick.ToString() : "none") + ".");
            }

            _lastTick = today;
            _hasTicked = true;

            try
            {
                // Refresh the sensors' cached reading for the new day. Cheap and idempotent: the
                // snapshot system samples at most once per sim day and hands back a cached object.
                CitySnapshot snapshot = _snapshots != null ? _snapshots.Capture() : null;
                if (snapshot != null) _lastSnapshot = snapshot;

                // Watch for the background generation finishing. Polling the provider itself every
                // day would make the canned pool re-derive prose daily for nothing; watching a state
                // transition costs one enum comparison and catches the result within a sim day of it
                // arriving, rather than up to a month later.
                if (_flavor != null)
                {
                    FlavorProviderState state = _flavor.State;
                    if (state != _lastFlavorState)
                    {
                        _lastFlavorState = state;

                        if (state == FlavorProviderState.Succeeded)
                        {
                            CollectProse(today, snapshot);
                        }
                        else if (state == FlavorProviderState.Failed || state == FlavorProviderState.Unavailable)
                        {
                            // Fail closed (#7): the last good prose stands. Only the status line moves.
                            _lastAttemptDate = today;
                            _pendingWake = false;
                            _stateVersion++;
                        }
                    }
                }

                if (monthChanged)
                {
                    OnMonth(today, snapshot);
                }

                return true;
            }
            catch (Exception ex)
            {
                // Fail closed. A political layer that throws into GameSimulation takes the player's
                // city down over something nobody asked for.
                AgoraMod.Log.Warn("Agora tick at " + today + " failed (" + ex.GetType().Name + ": " +
                                  ex.Message + "); skipping this month.");
                return false;
            }
        }

        /// <summary>
        /// The monthly political tick: hand the engine the city and the prior state, take the new
        /// state back, persist it, apply what it asked for.
        /// </summary>
        /// <remarks>
        /// Every political decision in this method is made inside <c>Agora.Core</c>. What lives here
        /// is strictly glue — assembling the input, storing the output, dispatching the requests and
        /// waking the prose — because <c>src/Agora.Mod/CLAUDE.md</c> forbids political computation in
        /// this assembly, and because anything decided here would be untestable without the game.
        /// </remarks>
        private static void OnMonth(SimDate today, CitySnapshot snapshot)
        {
            if (_state == null) return;

            var input = new EngineTickInput
            {
                SaveGuid = SaveGuid,
                Date = today,
                StartDate = _startDate,
                PriorState = _state,
                Snapshot = snapshot,
                SnapshotHistory = _snapshotHistory,
                Catalog = Catalog.Events,
                CivicCatalog = CivicCatalog.Events,
                ManualFlavorWakeRequested = _manualWakeRequested,
                // Explicit, though false is the default: this is the lived month, and the one place
                // in the codebase where that has to be stated is beside the place that says otherwise.
                IsReplay = false,
                Tuning = Tuning
            };

            EngineTickResult tick = PoliticalEngine.Advance(input);

            _state = tick.State;

            // Immediately after the assignment and before anything below can throw: the watermark has
            // to be part of the object the sidecar writes, and GetStateForSave hands out exactly this
            // reference. Written whether or not the tick did work — the question it answers is "has
            // this month been run", not "did running it change anything", and a month the engine
            // declined to act on is still a month that must not come round a second time.
            _state.LastCompletedTickMonth = today.TotalMonths;

            _manualWakeRequested = false;
            _stateVersion++;

            // After the assignment, because the flag is decided by the state that just came back.
            LockThemeIfElectionHeld();

            // Refreshed every month rather than only on a wake: a splinter or a new entry founded by
            // the tick above is not in the roster the last RequestFlavor left behind, and the pool
            // writes about the roster it was handed. Cheap — it walks the registry and allocates
            // briefs, nothing more.
            SeedFlavorRoster(today);

            // The canned pool answers immediately and the CLI overwrites it later, so polling here
            // is what gets party names onto a brand-new save's very first dashboard frame.
            CollectProse(today, snapshot);

            for (int i = 0; i < tick.Warnings.Count; i++)
            {
                AgoraMod.Log.Warn("Agora engine: " + tick.Warnings[i]);
            }

            if (!tick.DidWork) return;

            RecordSnapshot(snapshot);

            // Effects. The engine has already withheld these when the per-save switch is off; the
            // dispatcher checks again, because a cap that is only enforced in one place is a cap that
            // will eventually be bypassed (non-negotiable #5).
            if (tick.EffectRequests.Count > 0 && AgoraEffects.IsInitialised)
            {
                AgoraEffects.Dispatcher.Dispatch(tick.EffectRequests, AgoraEffects.EffectsEnabled);
            }

            if (tick.Election != null)
            {
                AgoraMod.Log.Info("Agora: election " + tick.Election.Id + " counted at " + today +
                                  " — " + tick.Election.TotalSeats + " seats, turnout " +
                                  tick.Election.Turnout.ToString("P1") + ".");
            }

            MaybeWakeFlavor(today, snapshot, tick);

            // Last, and beside the wake for the same reason: both are one-shot consequences of a tick
            // that has already been stored. This is the ONLY entry point to the alert system — until
            // v10 CollectProse was a second one, raising the month's article alerts wherever a
            // background generation happened to land, and a card that never appeared could have come
            // from either. It cannot now: every alert the player sees is raised below.
            RaiseAlerts(today, tick);
        }

        /// <summary>
        /// Turns what this tick did into the interruptions the player is owed, once
        /// (<c>docs/plans/0003-w5-popup-lane.md</c> §5.1–5.2).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Here and not in the projection.</b> The projection is a view, rebuilt from scratch on
        /// every publish; an alert is an event and happens once. Deriving the queue over there would
        /// re-raise everything on every republish, which is the bug <see cref="_raisedAlertIds"/> and
        /// this method's placement exist to prevent between them.
        /// </para>
        /// <para>
        /// Each block reads a dated fact the engine has already persisted and compares it to
        /// <paramref name="today"/> — the date the tick was handed, never one computed here
        /// (non-negotiable #8). As of v10, when the article alert retired with the feed, <b>nothing
        /// model-authored enters an alert at all</b>: every headline and summary below is written in
        /// this file or by the engine (non-negotiable #1). Prose reaches a card only as a body the
        /// player opens, fetched from <c>agora.news.article</c> under the alert's own id.
        /// </para>
        /// <para>
        /// The order below is fixed: the result of the ballot, then who is governing because of it,
        /// then who joined or left the field, then what happened to the city, then the stories drafted
        /// against it. No collection with an undefined enumeration order is walked (non-negotiable #3)
        /// — <c>FiredEvents</c> arrives sorted from the scheduler and
        /// <see cref="PartyLifecycleChanges.Collect"/> returns a total order of its own.
        /// </para>
        /// </remarks>
        private static void RaiseAlerts(SimDate today, EngineTickResult tick)
        {
            if (_state == null) return;

            if (tick.Election != null)
            {
                Enqueue(new NewsAlert
                {
                    Id = "election:" + tick.Election.Id,
                    Kind = "Election",
                    Date = today,
                    Headline = tick.Election.IsSnapElection ? "Snap election held" : "Election held",
                    // Whole percent, away from zero, invariant. The invariance is load-bearing and
                    // outlived the feed row this used to have to match: "P1" would say "46,8 %"
                    // under de-DE, and this mod is English only (non-negotiable #10).
                    Summary = "Turnout " +
                              ((int)Math.Round(tick.Election.Turnout * 100.0,
                                  MidpointRounding.AwayFromZero))
                                  .ToString(CultureInfo.InvariantCulture) +
                              "% across " + tick.Election.TotalSeats + " seats.",
                    Major = true
                });
            }

            RaiseCoalitionAlerts(today);
            RaisePartyAlerts(today);
            RaiseEventAlerts(tick);
            RaiseStoryAlerts(today, tick);
        }

        /// <summary>Stories that drafted on this tick, one card each.</summary>
        /// <remarks>
        /// <para>
        /// <b>Drafted stories only, never resolved ones.</b> A card is an interruption asking for a
        /// decision, and a resolved story has no decision left in it — its verdict is news and belongs
        /// in the panel's archive, where the player reads it when they choose to. Interrupting for a
        /// verdict would double this lane's interruption budget for something nobody can answer.
        /// </para>
        /// <para>
        /// <b>The severity gate is read, never written down here</b>, on exactly the rule
        /// <see cref="RaiseEventAlerts"/> states: <c>stories.majorSeverityThreshold</c> is the same
        /// number the engine derives a tier from, and a literal here would be a second and eventually
        /// disagreeing definition of a serious story inside one build. What is compared is the
        /// <b>major slot's</b> severity, because a story's weight is its major event's — a bundle
        /// carrying two trivial minors beside a catastrophe is a catastrophe.
        /// </para>
        /// <para>
        /// A story whose major slot the catalog no longer explains still raises a card, at
        /// non-major. Losing the card entirely would cost the player the decision; losing only the
        /// pause is the smaller failure, and it is logged where the catalog gap is diagnosable.
        /// </para>
        /// </remarks>
        private static void RaiseStoryAlerts(SimDate today, EngineTickResult tick)
        {
            if (tick == null || tick.DraftedStories == null) return;

            int threshold = Tuning.Stories.MajorSeverityThreshold;

            for (int i = 0; i < tick.DraftedStories.Count; i++)
            {
                Story story = tick.DraftedStories[i];
                if (story == null || string.IsNullOrEmpty(story.Id)) continue;

                StorySlot major = MajorSlotOf(story);
                CivicEvent civicEvent = major == null ? null : FindCivicEvent(major.EventId);

                EnqueueStory(new StoryAlert
                {
                    Id = story.Id,
                    Date = story.OpenedDate == default(SimDate) ? today : story.OpenedDate,
                    Headline = story.HeadlineFallback ?? "",
                    Summary = civicEvent == null ? "" : civicEvent.Description ?? "",
                    SlotCount = story.Slots == null ? 0 : story.Slots.Count,
                    Major = civicEvent != null && civicEvent.Severity >= threshold
                });

                if (civicEvent == null)
                {
                    AgoraMod.Log.Warn("Agora: story '" + story.Id + "' drafted with a major slot the " +
                                      "loaded civic catalog does not explain; its card is raised " +
                                      "without a summary and does not hold the clock.");
                }
            }
        }

        /// <summary>
        /// A story's major slot, or its first slot when no slot carries the flag.
        /// </summary>
        /// <remarks>
        /// <b>The flag, not the position.</b> <c>Story.Slots</c> really is sorted major-first, so
        /// index 0 is right today and every fixture in the suite is built that way — which is exactly
        /// why wave 5's review found that deleting the flag check from
        /// <c>StaticPoolProvider.MajorSlot</c> left the whole suite green. Reading the flag is what
        /// keeps this correct if the sort ever changes; falling back to index 0 is what keeps a
        /// degraded all-minor story from raising a card with no summary at all.
        /// </remarks>
        private static StorySlot MajorSlotOf(Story story)
        {
            if (story == null || story.Slots == null || story.Slots.Count == 0) return null;

            for (int i = 0; i < story.Slots.Count; i++)
            {
                StorySlot slot = story.Slots[i];
                if (slot != null && slot.Role == SlotRole.Major) return slot;
            }

            return story.Slots[0];
        }

        /// <summary>
        /// A government that took office, or one that fell, on this tick's date.
        /// </summary>
        /// <remarks>
        /// The <c>":formed"</c> suffix is not decoration: it is what keeps a government's birth and its
        /// death distinct in the ack key, so one coalition cannot dedupe the other out of the ring.
        /// That half stands on its own today. The other half — that the two must not fetch each
        /// other's body from <c>agora.news.article</c> — is dormant rather than false: no coalition
        /// alert sets <see cref="NewsAlert.HasArticle"/> until the article/alert id join lands, and it
        /// becomes load-bearing again the moment it does.
        /// </remarks>
        private static void RaiseCoalitionAlerts(SimDate today)
        {
            Coalition government = _state.Government;
            if (government != null && government.Status != CoalitionStatus.Negotiating &&
                government.FormedDate == today)
            {
                Enqueue(new NewsAlert
                {
                    Id = "coalition:" + government.Id + ":formed",
                    Kind = "Coalition",
                    Date = today,
                    Headline = string.IsNullOrEmpty(government.ElectionId)
                        ? "New government formed mid-term"
                        : "New government takes office",
                    Summary = "The city has a new government. The Council tab has who is in it.",
                    PartyId = government.LeadPartyId,
                    Major = true
                });
            }

            // A list in the engine's own append order, not a dictionary. The date test does the
            // filtering, so a long history costs a comparison per entry and raises nothing.
            for (int i = 0; i < _state.CoalitionHistory.Count; i++)
            {
                Coalition ended = _state.CoalitionHistory[i];
                if (ended == null || !ended.EndedDate.HasValue) continue;
                if (ended.EndedDate.Value != today) continue;

                Enqueue(new NewsAlert
                {
                    Id = "coalition:" + ended.Id,
                    Kind = "Coalition",
                    Date = today,
                    Headline = ended.Status == CoalitionStatus.Collapsed
                        ? "Government collapsed"
                        : "Government's term ended",
                    Summary = CollapseReasonSentence(ended.CollapseReason),
                    PartyId = ended.LeadPartyId,
                    Major = true
                });
            }
        }

        /// <summary>A brand that entered or left the field on this tick's date.</summary>
        /// <remarks>
        /// <see cref="PartyLifecycleChanges.Collect"/> is in Agora.Core so the rules that matter here
        /// are testable without the game — chiefly the opening-roster exclusion, without which a new
        /// save's first tick would announce the founding of the entire field.
        /// </remarks>
        private static void RaisePartyAlerts(SimDate today)
        {
            PartyLifecycleChangeSet lifecycle = PartyLifecycleChanges.Collect(_state.Parties, _startDate);

            // Logged here rather than anywhere a view could log it: this runs once per sim month, and
            // the dedupe set narrows that to once per occurrence. The set is shared with the alerts,
            // under the same Kind + "|" + Id key every other entry uses — "suppressed-lifecycle" is a
            // kind no alert has — rather than a second set that would then need a second line in
            // ResetForNewSave to stay honest.
            for (int i = 0; i < lifecycle.SuppressedDates.Count; i++)
            {
                SimDate suppressed = lifecycle.SuppressedDates[i];
                if (!_raisedAlertIds.Add("suppressed-lifecycle|" + suppressed)) continue;

                AgoraMod.Log.Warn("Agora: every party in existence on " + suppressed +
                                  " was founded on that date, so it reads as a whole-roster " +
                                  "regeneration rather than as politics; nothing dated then raises " +
                                  "an alert.");
            }

            for (int i = 0; i < lifecycle.Records.Count; i++)
            {
                PartyLifecycleRecord change = lifecycle.Records[i];
                if (change.Date != today) continue;

                bool founded = change.Kind == PartyLifecycleKind.Founded;

                Enqueue(new NewsAlert
                {
                    // Merged and Dissolved share the ":dissolved" suffix: one thing from the reader's
                    // point of view — the brand leaving the ballot — and the headline is where the
                    // two stories part.
                    Id = "party:" + change.PartyId + (founded ? ":founded" : ":dissolved"),
                    Kind = "Party",
                    Date = today,
                    Headline = founded
                        ? "New party founded"
                        : change.Kind == PartyLifecycleKind.Merged
                            ? "Party absorbed into another"
                            : "Party dissolved",
                    Summary = founded
                        ? "A new party has entered the field."
                        : change.Kind == PartyLifecycleKind.Merged
                            ? "Its members and its seats pass to the party that took it in."
                            : "It fell below the threshold once too often and leaves the ballot.",
                    PartyId = change.PartyId,
                    Major = true
                });
            }
        }

        /// <summary>Events that fired this tick and cleared the severity gate.</summary>
        /// <remarks>
        /// <b>The threshold is read, never written down here.</b>
        /// <c>CatalogTuning.MajorSeverityThreshold</c> (<c>src/Agora.Core/Tuning/EngineTuning.cs:844</c>,
        /// loaded from <c>data/engine_tuning.json</c>) is the same number
        /// <c>EventScheduler.IsMajor</c> (<c>src/Agora.Core/Events/Scheduler/EventScheduler.cs:378</c>)
        /// and <c>CoalitionStability</c> already decide "major" by. A literal here would be a second,
        /// eventually disagreeing, definition of a serious event inside one build — so there is no
        /// literal, and moving the number moves all three at once.
        /// <para>
        /// An event below the gate raises no card and is not lost: it is still on
        /// <c>PoliticalState.ActiveEvents</c> and still applying its effects. A popup is an
        /// interruption, and interrupting for every fired event at every severity is what the gate
        /// exists to prevent.
        /// </para>
        /// </remarks>
        private static void RaiseEventAlerts(EngineTickResult tick)
        {
            int threshold = Tuning.Catalog.MajorSeverityThreshold;

            for (int i = 0; i < tick.FiredEvents.Count; i++)
            {
                TimelineEvent ev = tick.FiredEvents[i];
                if (ev == null || string.IsNullOrEmpty(ev.Id)) continue;
                if (ev.Severity < threshold) continue;

                Enqueue(new NewsAlert
                {
                    Id = "event:" + ev.Id,
                    Kind = "Event",
                    Date = ev.FiredDate ?? _state.Date,
                    Headline = ev.Title,
                    Summary = ev.HeadlineBrief ?? "",
                    Severity = ev.Severity,
                    EventId = ev.Id,
                    Major = true
                });
            }
        }

        /// <summary>
        /// Plain English for a collapse. No enum member name may reach the player, and
        /// <c>CollapseReason.ToString()</c> would put <c>"PartnerWithdrawal."</c> in front of one.
        /// </summary>
        /// <remarks>
        /// A switch with a default rather than a lookup table, so a member added to
        /// <see cref="CoalitionCollapseReason"/> cannot leak its own name while nobody is looking: the
        /// worst an unmapped reason can do is say nothing.
        /// </remarks>
        internal static string CollapseReasonSentence(CoalitionCollapseReason reason)
        {
            switch (reason)
            {
                case CoalitionCollapseReason.StabilityDecay:
                    return "It had been losing its grip for months.";
                case CoalitionCollapseReason.MandateFailure:
                    return "Too many of its promises were abandoned.";
                case CoalitionCollapseReason.EventShock:
                    return "It did not survive the crisis.";
                case CoalitionCollapseReason.IdeologicalDrift:
                    return "Its partners had drifted too far apart to govern together.";
                case CoalitionCollapseReason.PartnerWithdrawal:
                    return "A partner walked out and took the majority with it.";
                default:
                    return "";
            }
        }

        /// <summary>
        /// Puts one alert on the ring, if it is new and the ring will have it.
        /// </summary>
        /// <remarks>
        /// The dedupe set is consulted before the bound, and is never rolled back by a drop: an alert
        /// pushed off the front has already been superseded by eight newer ones and re-offering it
        /// later would be worse than losing it.
        /// </remarks>
        private static void Enqueue(NewsAlert alert)
        {
            if (alert == null || string.IsNullOrEmpty(alert.Id)) return;

            // Keyed on the kind as well as the id. See _raisedAlertIds for why the compound key stays
            // now that the model-authored id that forced it is gone. The alert's own Id is untouched —
            // it is what the ack and the agora.news.article map are keyed on.
            if (!_raisedAlertIds.Add(alert.Kind + "|" + alert.Id)) return;

            _alerts.Add(alert);

            while (_alerts.Count > AlertQueueMax)
            {
                NewsAlert dropped = _alerts[0];
                _alerts.RemoveAt(0);

                AgoraMod.Log.Info("Agora: the alert queue is full at " + AlertQueueMax +
                                  "; dropped the oldest unanswered card (" + dropped.Id +
                                  "). What it announced still stands in the political state; only " +
                                  "the interruption is gone.");
            }

            // The publishers republish on this and on nothing else, so an alert raised without it
            // would sit in the ring until some unrelated change happened to move the version.
            _stateVersion++;
        }

        /// <summary>
        /// Puts one story card on its ring, if it is new and the ring will have it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// No compound key, unlike <see cref="Enqueue"/>: every id on this ring is an engine-minted
        /// story id, so there has never been a model-authored namespace to keep apart from an
        /// engine-written one. <see cref="Enqueue"/> kept its key after v10 retired the id that
        /// forced it — see <see cref="_raisedAlertIds"/>; this ring had no such id to begin with.
        /// </para>
        /// <para>
        /// <b>The drop is logged at Warn, not Info</b> — the difference from the news lane is the
        /// whole reason the two rings are separate. A dropped news card announced something that
        /// happened regardless and is still in the political state; a dropped story card is an
        /// interruption the player never saw, and the log line has to say plainly that the story is
        /// still live and still answerable from the Stories panel, or the drop reads as the decision
        /// having been taken away.
        /// </para>
        /// </remarks>
        private static void EnqueueStory(StoryAlert alert)
        {
            if (alert == null || string.IsNullOrEmpty(alert.Id)) return;

            if (!_raisedStoryAlertIds.Add(alert.Id)) return;

            _storyAlerts.Add(alert);

            while (_storyAlerts.Count > StoryAlertQueueMax)
            {
                StoryAlert dropped = _storyAlerts[0];
                _storyAlerts.RemoveAt(0);

                AgoraMod.Log.Warn("Agora: the story card queue is full at " + StoryAlertQueueMax +
                                  "; dropped the oldest unanswered card (" + dropped.Id +
                                  "). The story itself is untouched and is still answerable from the " +
                                  "Stories panel.");
            }

            _stateVersion++;
        }

        /// <summary>
        /// The player answered a story card: drops it from the queue, or drops all of them when the id
        /// is the sentinel <c>"*"</c>. Backs <c>agora.stories.ackAlert</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Dismissing a card is not answering the story.</b> It closes the interruption and nothing
        /// else — no response is recorded, no slot moves, and the story stays live in
        /// <see cref="PoliticalState.LiveStories"/> until it resolves or the player tackles it from
        /// the panel. That separation is what lets the card be a notification rather than a modal the
        /// player must complete, and it is why this touches no engine state at all.
        /// </para>
        /// <para>
        /// Acking an id the queue no longer holds is <see cref="CommandOutcome.Ok"/>, not
        /// <see cref="CommandOutcome.NotFound"/> — a double-click, or a second dismiss racing the
        /// republish, is not something the player did wrong. Same rule as <see cref="AckAlert"/>.
        /// </para>
        /// </remarks>
        public static CommandOutcome AckStoryAlert(string id)
        {
            lock (Gate)
            {
                try
                {
                    if (string.IsNullOrEmpty(id)) return CommandOutcome.BadValue;

                    if (id == "*")
                    {
                        if (_storyAlerts.Count == 0) return CommandOutcome.Ok;

                        _storyAlerts.Clear();
                        _stateVersion++;
                        return CommandOutcome.Ok;
                    }

                    for (int i = 0; i < _storyAlerts.Count; i++)
                    {
                        StoryAlert alert = _storyAlerts[i];
                        if (alert == null || !string.Equals(alert.Id, id, StringComparison.Ordinal))
                            continue;

                        _storyAlerts.RemoveAt(i);
                        _stateVersion++;
                        return CommandOutcome.Ok;
                    }

                    // Not held any more, which a double-click reaches routinely. The dedupe set is
                    // deliberately not rolled back: re-offering a card the player already dismissed is
                    // worse than never showing it again.
                    return CommandOutcome.Ok;
                }
                catch (Exception ex)
                {
                    AgoraMod.Log.Error(ex, "Agora: story card '" + (id ?? "(null)") + "' could not be " +
                                           "dismissed; the queue is unchanged.");
                    return CommandOutcome.Failed;
                }
            }
        }

        /// <summary>
        /// The LLM wake (§3). Which reasons are permitted is the engine's answer, not ours: it gates
        /// each one on both the per-save cadence and the tuning switch, so asking here would be a
        /// second copy of that rule and eventually a disagreeing one.
        /// </summary>
        private static void MaybeWakeFlavor(SimDate today, CitySnapshot snapshot, EngineTickResult tick)
        {
            if (_flavor == null) return;
            if (tick.LlmWake == LlmWakeCadence.None) return;

            // One generation per tick even when several reasons coincide. Election outranks manual
            // outranks yearly outranks the story draft: the rarer the trigger, the more the prose
            // should be about it.
            //
            // The story draft ranks LAST despite being the most frequent, and that costs nothing:
            // the prompt's story sections key on request.Stories being non-empty, not on this label,
            // so a story that drafts in the yearly month is still written about — it is written
            // about inside a round whose emphasis is the year in review, which is the better piece.
            FlavorWakeReason reason;
            if ((tick.LlmWake & LlmWakeCadence.Election) != 0) reason = FlavorWakeReason.Election;
            else if ((tick.LlmWake & LlmWakeCadence.Manual) != 0) reason = FlavorWakeReason.Manual;
            else if ((tick.LlmWake & LlmWakeCadence.Yearly) != 0) reason = FlavorWakeReason.Yearly;
            else reason = FlavorWakeReason.StoryDraft;

            var request = new FlavorRequest
            {
                Date = today,
                Reason = reason,
                Theme = _saveSettings.Theme,
                Snapshot = snapshot
            };

            // An election round asks the prompt for a result piece, both reactions and — under EU
            // rules — a coalition outlook, so it needs the slots to put them in. It is a prompt
            // instruction to the model only: RequestFlavor gives the canned pool a RosterCopy, which
            // carries the ordinary count, so the raised one reaches the CLI and nothing else.
            if (reason == FlavorWakeReason.Election)
            {
                request.ArticleCount = FlavorRequest.ElectionArticleCount(request.Theme);
            }

            FillBriefs(request, tick.State);

            // Fire and forget: RequestFlavor starts a background generation and returns immediately,
            // and a false return (no CLI, or one already in flight) is not an error — the canned pool
            // has already answered, which is exactly what non-negotiable #7 asks for.
            if (_flavor.RequestFlavor(request))
            {
                AgoraMod.Log.Info("Agora flavor: " + reason + " wake requested at " + today + ".");
            }
        }

        /// <summary>
        /// Files this round's story prose, keeping whatever was already filed.
        /// </summary>
        /// <remarks>
        /// The merge rule itself is <see cref="StoryProseLedger"/>, which is compile-linked into the
        /// test suite precisely because "first write wins" and "latest write wins" are one branch
        /// apart and only one of them lets the model's prose survive the next canned poll. All this
        /// method does is hand the payload over and sweep the ledger of stories that no longer
        /// exist.
        /// </remarks>
        private static void AbsorbStoryProse(FlavorPayload payload)
        {
            if (payload == null) return;

            _storyProse.Absorb(payload);

            if (_state == null) return;

            // Swept here rather than when a story leaves the archive, because eviction happens deep
            // inside the engine's own trim and the ledger is a Mod-side concern. Doing it on the
            // prose path costs one pass over a few dozen ids on a tick that has just done far more
            // than that, and it cannot drift out of step with the state the way a second eviction
            // hook could.
            var live = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _state.LiveStories.Count; i++)
            {
                Story story = _state.LiveStories[i];
                if (story != null && !string.IsNullOrEmpty(story.Id)) live.Add(story.Id);
            }
            for (int i = 0; i < _state.StoryArchive.Count; i++)
            {
                Story story = _state.StoryArchive[i];
                if (story != null && !string.IsNullOrEmpty(story.Id)) live.Add(story.Id);
            }

            _storyProse.RetainOnly(live);
        }

        /// <summary>
        /// Writes each event's local angle onto the event it was written for.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>This closes a three-milestone dead end.</b> <c>eventProse</c> was prompted for,
        /// returned, schema-checked, id-checked against the catalog and cached — and then nothing
        /// copied it anywhere. <c>AgoraUiProjection</c> published <c>ev.LocalAngle</c>, which no code
        /// path had ever assigned, so every local angle the model ever wrote was discarded one step
        /// from the screen and the panel showed an empty string with no error anywhere to say so.
        /// </para>
        /// <para>
        /// Written onto <c>ActiveEvents</c> only. A local angle is colour for an event the player is
        /// currently being shown; back-filling the archive would rewrite history that has already
        /// scrolled past, on every poll, for the life of the save.
        /// </para>
        /// <para>
        /// <b>Non-negotiable #1 is untouched.</b> <c>LocalAngle</c> is a display string — nothing
        /// reads it, parses it or derives from it — which is exactly why it is the one field on a
        /// <see cref="TimelineEvent"/> that LLM text may be written into at all.
        /// </para>
        /// </remarks>
        private static void ApplyEventProse(FlavorPayload payload)
        {
            if (payload == null || _state == null) return;
            if (payload.EventProse.Count == 0) return;

            var byId = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < payload.EventProse.Count; i++)
            {
                Agora.Core.Contracts.EventProse prose = payload.EventProse[i];
                if (prose == null || string.IsNullOrEmpty(prose.EventId)) continue;
                if (string.IsNullOrEmpty(prose.LocalAngle)) continue;

                byId[prose.EventId] = prose.LocalAngle;
            }

            if (byId.Count == 0) return;

            int written = 0;
            for (int i = 0; i < _state.ActiveEvents.Count; i++)
            {
                TimelineEvent ev = _state.ActiveEvents[i];
                if (ev == null || string.IsNullOrEmpty(ev.Id)) continue;

                string angle;
                if (!byId.TryGetValue(ev.Id, out angle)) continue;

                // Same first-write-wins discipline as the story ledger, and for the same reason: the
                // canned pool answers every poll and the model answers rarely, so overwriting would
                // erase the model's angle within a month of it arriving.
                if (!string.IsNullOrEmpty(ev.LocalAngle)) continue;

                ev.LocalAngle = angle;
                written++;
            }

            if (written > 0)
            {
                AgoraMod.Log.Info("Agora flavor: wrote " + written + " local angle" +
                                  (written == 1 ? "" : "s") + " onto active events.");
            }
        }

        /// <summary>
        /// Describes the current politics to the prompt — ids, archetypes and the issue each brand
        /// exists to shout about. Deliberately no vote shares, no seats, no platform coordinates: the
        /// model must not be able to echo a number back into engine state (non-negotiable #1).
        /// </summary>
        private static void FillBriefs(FlavorRequest request, PoliticalState state)
        {
            if (state == null) return;

            for (int i = 0; i < state.Parties.Count; i++)
            {
                Party party = state.Parties[i];
                request.Parties.Add(new PartyBrief
                {
                    PartyId = party.Id,
                    ArchetypeId = party.ArchetypeId,
                    CoreGrievance = party.CoreGrievance,
                    // A governing phrase, not Status.ToString(). The lifecycle word carried no
                    // outcome at all, and the prompt's election block is written against this field
                    // — see PartyBrief.StandingWord.
                    StatusWord = PartyBrief.StandingWord(party),
                    CurrentName = party.Name,
                    FoundedDate = party.FoundedDate
                });
            }

            for (int i = 0; i < state.Factions.Count; i++)
            {
                Faction faction = state.Factions[i];
                request.Factions.Add(new FactionBrief
                {
                    FactionId = faction.Id,
                    PartyId = faction.PartyId,
                    ArchetypeId = faction.ArchetypeId,
                    CoreGrievance = faction.CoreGrievance,
                    StatusWord = faction.Status.ToString(),
                    CurrentName = faction.Name,
                    FoundedDate = faction.FoundedDate
                });
            }

            for (int i = 0; i < state.ActiveEvents.Count; i++)
            {
                TimelineEvent ev = state.ActiveEvents[i];
                var brief = new EventBrief
                {
                    EventId = ev.Id,
                    Title = ev.Title,
                    HeadlineBrief = ev.HeadlineBrief
                };

                for (int t = 0; t < ev.Tags.Count; t++) brief.Tags.Add(ev.Tags[t]);
                request.Events.Add(brief);
            }

            FillStoryBriefs(request, state);
        }

        /// <summary>
        /// Describes the live stories, and the ones that resolved in the cycle just gone, to the
        /// prompt and to the canned pool.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The archive is filtered by recency, not taken whole.</b> It retains
        /// <c>stories.archiveRetention</c> entries — forty at shipped tuning — and asking either
        /// writer for closing prose on a story that resolved three years ago would spend the round's
        /// whole budget re-telling history the player has already seen and moved past. Only stories
        /// that resolved within the current cycle can still be news.
        /// </para>
        /// <para>
        /// Titles come from the civic catalog rather than from the story, because a
        /// <see cref="StorySlot"/> carries an event <i>id</i> and nothing else. A slot whose event
        /// the catalog cannot resolve is still sent, with the id standing in for the title: the
        /// story is real and the player is looking at it, so writing about it with a thin
        /// description beats silently dropping a third of it.
        /// </para>
        /// </remarks>
        private static void FillStoryBriefs(FlavorRequest request, PoliticalState state)
        {
            if (state == null) return;

            for (int i = 0; i < state.LiveStories.Count; i++)
            {
                StoryBrief brief = BuildStoryBrief(state.LiveStories[i]);
                if (brief != null) request.Stories.Add(brief);
            }

            int cycle = _tuning != null ? _tuning.Stories.CycleMonths : 2;
            if (cycle < 2) cycle = 2;

            int nowMonths = state.Date.TotalMonths;

            for (int i = 0; i < state.StoryArchive.Count; i++)
            {
                Story story = state.StoryArchive[i];
                if (story == null) continue;

                // -1 is "never resolved", which an archived story should not be; treat it as too old
                // rather than as freshly resolved, so a malformed entry cannot flood the round.
                if (story.ResolvedMonth < 0) continue;
                if (nowMonths - story.ResolvedMonth > cycle) continue;

                StoryBrief brief = BuildStoryBrief(story);
                if (brief != null) request.Stories.Add(brief);
            }
        }

        private static StoryBrief BuildStoryBrief(Story story)
        {
            if (story == null || string.IsNullOrEmpty(story.Id)) return null;

            var brief = new StoryBrief
            {
                StoryId = story.Id,
                IsResolved = story.Outcome != StoryOutcome.Pending,
                OutcomeWord = story.Outcome == StoryOutcome.Pending ? "" : StoryOutcomeWord(story.Outcome)
            };

            List<StorySlot> slots = story.Slots;
            if (slots == null) return brief;

            for (int i = 0; i < slots.Count; i++)
            {
                StorySlot slot = slots[i];
                if (slot == null) continue;

                CivicEvent authored = _civicCatalog == null ? null : _civicCatalog.Find(slot.EventId);

                brief.Slots.Add(new StorySlotBrief
                {
                    EventId = slot.EventId,
                    IsMajor = slot.Role == SlotRole.Major,
                    Title = authored != null ? authored.Name : slot.EventId,
                    HeadlineBrief = authored != null ? authored.Description : "",
                    OutcomeWord = SlotOutcomeWord(slot.SlotOutcome)
                });
            }

            return brief;
        }

        /// <summary>
        /// The verdict as a word. Lowercase prose, not <c>ToString()</c>: these go straight into a
        /// prompt, and <c>NotMet</c> reads as a symbol where <c>not met</c> reads as English.
        /// </summary>
        private static string StoryOutcomeWord(StoryOutcome outcome)
        {
            switch (outcome)
            {
                case StoryOutcome.Success: return "success";
                case StoryOutcome.Failure: return "failure";
                case StoryOutcome.Abandoned: return "abandoned";
                default: return "";
            }
        }

        /// <inheritdoc cref="StoryOutcomeWord"/>
        private static string SlotOutcomeWord(SlotOutcome outcome)
        {
            switch (outcome)
            {
                case SlotOutcome.Met: return "met";
                case SlotOutcome.NotMet: return "not met";
                case SlotOutcome.Unmeasurable: return "unmeasurable";
                default: return "";
            }
        }

        /// <summary>
        /// Tells the canned pool who exists. <see cref="IFlavorProvider.TryGetFlavor"/> hands it only
        /// a snapshot and a date, so without this the pool has no way to learn the registry and every
        /// poll returns articles about nobody.
        /// </summary>
        /// <remarks>
        /// Deliberately not a <c>RequestFlavor</c> call: that is the CLI's trigger, and firing a
        /// subprocess on every load and every month boundary is not what "the roster moved" warrants.
        /// </remarks>
        private static void SeedFlavorRoster(SimDate date)
        {
            if (_state == null) return;
            if (_flavor == null || _flavor.Pool == null) return;

            var request = new FlavorRequest
            {
                Date = date,
                Theme = _saveSettings != null ? _saveSettings.Theme : RegionTheme.Eu,
                Snapshot = _lastSnapshot
            };

            FillBriefs(request, _state);

            // Through RosterCopy for the same reason LayeredFlavorProvider.RequestFlavor does it: one
            // rule, so a roster is never an alias of a request anyone else holds. Nothing changes value
            // here — this request is built locally, at the ordinary article count, and never handed to
            // the CLI worker — but two assignment sites with opposite treatment is how the rule rots.
            _flavor.Pool.Roster = request.RosterCopy();

            // The authored civic text, which a roster cannot carry. A resolution's canned prose reads
            // the event's SuccessText or FailText, and the only route to those is
            // CivicEventCatalog.Find — a StorySlotBrief carries the event's name and its factual
            // description and deliberately not its outcome blurbs, because the prompt has no use for
            // them and a brief carrying every authored string would be most of the catalog.
            //
            // Without this the pool holds CivicEventCatalog.Empty and every resolution degrades to
            // the slot's description: whole, valid, schema-passing prose that says what the story WAS
            // rather than how it went. Nothing errors and no count is wrong, so the only symptom is
            // that closing cards read oddly like opening ones. Note this is no longer the difference
            // between a resolution card and an open one — lane 5c's closing lead-in is keyed on the
            // story's own outcome word and needs no catalog — but it is the difference between a
            // closing card that names the outcome and one that also says what happened.
            //
            // Assigned beside the roster because the two are the pool's whole view of the world and
            // drift apart the moment they are set in different places. Safe: the pool never crosses
            // to the CLI worker thread, and CivicEventCatalog is immutable after construction.
            _flavor.Pool.CivicCatalog = _civicCatalog;
        }

        /// <summary>
        /// Gives every still-unnamed party a name, synchronously, from the canned pool.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The CLI is the preferred source, but it answers in tens of seconds and may never answer at
        /// all, and a party has to be on the ballot with a name the moment it exists. So the pool is
        /// asked directly — <c>Generate</c> is synchronous and has no once-per-date guard. If the pool
        /// is unavailable the names stay empty and the dashboard shows its placeholder; failing closed
        /// here is the same rule as #7.
        /// </para>
        /// <para>
        /// <b>The request carries the whole roster, named parties included, and that is not
        /// redundant.</b> The pool de-duplicates names per <c>Generate</c> call, so a request narrowed
        /// to the unnamed would let a splinter draw a name an incumbent already holds — invisible
        /// until the next full-roster document either renamed the splinter or, if it sorted first,
        /// left the two sharing a name for good. Generating over everyone and writing only the empty
        /// ones costs one extra pass over a handful of briefs and makes this path produce exactly the
        /// names the monthly document would. Factions and events are still dropped: this method is
        /// about the ballot, they are drawn after the parties and so cannot move a party name, and
        /// generating prose nobody
        /// reads is work done on the sim thread for nothing.
        /// </para>
        /// </remarks>
        private static void EnsureEveryPartyNamed(SimDate date)
        {
            if (_state == null) return;
            if (_flavor == null || _flavor.Pool == null) return;

            var request = new FlavorRequest
            {
                Date = date,
                Theme = _saveSettings != null ? _saveSettings.Theme : RegionTheme.Eu,
                ArticleCount = 1
            };

            FillBriefs(request, _state);
            request.Factions.Clear();
            request.Events.Clear();

            // The roster stays whole — see the remarks. All this decides is whether there is anything
            // to write, so that the common case (everybody named) costs no generation at all.
            bool anyUnnamed = false;
            for (int i = 0; i < request.Parties.Count; i++)
            {
                if (string.IsNullOrEmpty(request.Parties[i].CurrentName)) { anyUnnamed = true; break; }
            }

            if (!anyUnnamed) return;

            FlavorDocument document;
            try
            {
                document = _flavor.Pool.Generate(request);
            }
            catch (Exception ex)
            {
                AgoraMod.Log.Warn("Agora flavor: the canned pool threw while naming parties (" +
                                  ex.Message + "); they stay unnamed for now.");
                return;
            }

            if (document == null) return;

            FlavorPayload payload = document.ToPayload(date);
            int named = 0;

            for (int i = 0; i < payload.Parties.Count; i++)
            {
                PartyFlavor flavor = payload.Parties[i];
                if (flavor == null || string.IsNullOrEmpty(flavor.PartyId)) continue;

                for (int p = 0; p < _state.Parties.Count; p++)
                {
                    Party party = _state.Parties[p];
                    if (string.CompareOrdinal(party.Id, flavor.PartyId) != 0) continue;
                    // Both guards stay, and both are load-bearing. The first is what makes this method
                    // "name the unnamed" rather than "rewrite everybody from the canned pool" — drop
                    // it and a party the model named years ago has its prose replaced by a stopgap on
                    // the next load. The second stops an entry that carried no name from counting as
                    // a naming and marking the party provisional for a write that never happened.
                    if (!string.IsNullOrEmpty(party.Name)) break;
                    if (string.IsNullOrEmpty(flavor.Name)) break;

                    // mayRename is unconditionally true: the guard above already established the name
                    // is empty, which is the only case this method acts in. The locks are still
                    // honoured, inside ApplyFlavor, and the description lock is the one that matters
                    // here — this is the second pair of writes fixplan.md's "single enforcement point"
                    // framing misses entirely, and it becomes a live bug the moment "reset name"
                    // ships: the player locks the description, then resets the name, the name goes
                    // empty, this method fires on the very next pass, and without the lock check it
                    // overwrites the description the player wrote with pool prose. Nothing would be
                    // logged and nothing would look wrong.
                    bool wroteName;
                    PartyIdentity.ApplyFlavor(party, flavor, mayRename: true, wroteName: out wroteName);

                    if (wroteName)
                    {
                        _provisionalNamePartyIds.Add(party.Id);
                        named++;
                    }
                    break;
                }
            }

            if (named > 0)
            {
                AgoraMod.Log.Info("Agora flavor: named " + named + " part" + (named == 1 ? "y" : "ies") +
                                  " from the canned pool at " + date + ".");
                _stateVersion++;
            }
        }

        /// <summary>
        /// Takes whatever prose is available and applies the parts that belong on engine objects.
        /// </summary>
        /// <remarks>
        /// Names are written onto <see cref="Party"/> and <see cref="Faction"/> because those fields
        /// are flavor-owned by contract and are persisted — which is what lets a party keep its name
        /// across a reload instead of being re-christened every session. Nothing numeric is copied:
        /// the payload has no numbers on it, by design, and non-negotiable #1 is what that design is
        /// for.
        /// </remarks>
        private static void CollectProse(SimDate today, CitySnapshot snapshot)
        {
            if (_flavor == null) return;

            try
            {
                FlavorPayload payload;
                try
                {
                    payload = _flavor.TryGetFlavor(snapshot, today);
                }
                catch (Exception ex)
                {
                    AgoraMod.Log.Warn("Agora flavor poll failed (" + ex.Message + "); keeping the last good prose.");
                    return;
                }

                if (payload == null) return;

                _flavorPayload = payload;
                _lastFlavorDate = today;
                _pendingWake = false;

                // Before the party names, and outside the _state guard, because neither depends on
                // the other and story prose is the reason this method now runs at all on most ticks.
                AbsorbStoryProse(payload);
                ApplyEventProse(payload);

                if (_state != null)
                {
                    // Canned prose can name a party through this door too — a splinter founded between
                    // wakes, on a month the CLI was late. Such a name is as provisional as one written
                    // by EnsureEveryPartyNamed, and marking it so is what lets the model's document
                    // still claim it later.
                    ApplyProseNames(payload, _flavor.LastPayloadSource == FlavorPayloadSource.Pool);

                    // After, not before: a party the payload did not cover — a splinter founded this
                    // month, or one the CLI's cached document predates — still needs a name before
                    // this method's version bump republishes the panel.
                    EnsureEveryPartyNamed(today);

                    _state.LastFlavorDate = today;
                }
            }
            finally
            {
                // In a finally, not on the success path: the status line reports when prose was last
                // *attempted*, so an attempt that threw — anywhere in this method, not only inside
                // TryGetFlavor — must still move it. Leaving it behind reads to the player as "never
                // tried" and hides a provider that is failing every cycle. The version bump is here
                // for the same reason: the panel republishes on it and would otherwise keep showing
                // the previous attempt's date.
                _lastAttemptDate = today;
                _stateVersion++;
            }
        }

        /// <param name="provisional">
        /// True when the payload came from the canned pool, in which case any name it writes joins the
        /// set of names a later model document is still allowed to replace. False locks on write.
        /// </param>
        /// <remarks>
        /// <para>
        /// The merge rule itself is <see cref="PartyIdentity.ApplyFlavor"/>, in <c>Agora.Core</c>. It
        /// used to be written out inline here, which made it the one rule in the whole prose path that
        /// no test could reach — this assembly names game types and the headless suite cannot load it.
        /// What is left on this side is the part that is genuinely the mod's: matching a payload entry
        /// to a party, deciding <c>mayRename</c>, and keeping the provisional set.
        /// </para>
        /// <para>
        /// <b>This method is one of four writes across two methods, not the single enforcement
        /// point.</b> <see cref="EnsureEveryPartyNamed"/> has the other two and is reached on paths
        /// this one is not (load, retheme, a party founded between wakes). Anything that must hold for
        /// every flavor write has to hold in <see cref="PartyIdentity.ApplyFlavor"/>, which is why the
        /// player's locks are checked there and not here.
        /// </para>
        /// </remarks>
        private static void ApplyProseNames(FlavorPayload payload, bool provisional)
        {
            for (int i = 0; i < payload.Parties.Count; i++)
            {
                PartyFlavor flavor = payload.Parties[i];
                if (flavor == null || string.IsNullOrEmpty(flavor.PartyId)) continue;

                for (int p = 0; p < _state.Parties.Count; p++)
                {
                    Party party = _state.Parties[p];
                    if (string.CompareOrdinal(party.Id, flavor.PartyId) != 0) continue;

                    // The name lock. Identity is written once and then left alone: a party the player
                    // has been reading about for ten years must not be re-christened because a fresh
                    // document happened to draw a different adjective. The one exception is a name the
                    // canned pool wrote as a stopgap — that one yields to the first real document,
                    // once, and is locked from then on.
                    bool mayRename = string.IsNullOrEmpty(party.Name) ||
                                     _provisionalNamePartyIds.Contains(party.Id);

                    // Description and slogan are prose, not identity. They are allowed to move with
                    // the politics, and a party whose platform shifted saying the same thing forever
                    // is the worse failure — so ApplyFlavor writes them whenever the player has not
                    // taken them, independently of whether the rename above was allowed.
                    bool wroteName;
                    PartyIdentity.ApplyFlavor(party, flavor, mayRename, out wroteName);

                    if (wroteName)
                    {
                        // Canned prose leaves the door open; a model document closes it. Keyed off
                        // what was actually written rather than off mayRename: a document with no
                        // name for this party leaves the door exactly as it found it.
                        if (provisional) _provisionalNamePartyIds.Add(party.Id);
                        else _provisionalNamePartyIds.Remove(party.Id);
                    }

                    break;
                }
            }
        }

        // ------------------------------------------------------------------ replay

        /// <summary>
        /// Brings the political state forward to the sim's current date after a load.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The reconciler picks the nearest earlier snapshot; the months between it and now have to be
        /// run or the politics would be stuck in the past while the city moved on. Clamped to
        /// <c>scheduler.catchUpMaxMonths</c> by <see cref="TickPlanner.CatchUpDates"/>.
        /// </para>
        /// <para>
        /// <b>Known limitation.</b> Every replayed month is scored against <i>today's</i> city,
        /// because the sensors can only measure the present and the sidecar does not retain a snapshot
        /// per month. The replay is therefore deterministic — the same load replays identically — but
        /// it is not historically faithful: a city that was poor five years ago and is rich now
        /// replays as though it had always been rich. Effects are deliberately not dispatched for
        /// replayed months, since applying five years of accumulated modifiers in one frame is worse
        /// than applying none.
        /// </para>
        /// </remarks>
        private static void Replay(int monthsToReplay)
        {
            if (_state == null || _time == null) return;

            SimDate today = _time.Today;
            bool truncated;
            List<SimDate> dates = TickPlanner.CatchUpDates(_state.Date, today, Tuning, out truncated);

            if (dates.Count == 0)
            {
                AgoraMod.Log.Info("Agora: sidecar reported " + monthsToReplay + " month(s) to replay, " +
                                  "but the state is already current at " + _state.Date + ".");
                return;
            }

            CitySnapshot snapshot = CaptureSnapshot();
            int replayed = 0;

            for (int i = 0; i < dates.Count; i++)
            {
                try
                {
                    EngineTickResult tick = PoliticalEngine.Advance(new EngineTickInput
                    {
                        SaveGuid = SaveGuid,
                        Date = dates[i],
                        StartDate = _startDate,
                        PriorState = _state,
                        Snapshot = snapshot,
                        SnapshotHistory = _snapshotHistory,
                        Catalog = Catalog.Events,
                        CivicCatalog = CivicCatalog.Events,

                        // The story cycle is suspended for every replayed month, and the flag is set
                        // here because this loop is the only thing that can know. Two hazards decided
                        // it: this method deliberately does not dispatch effects, so a story drafted
                        // and resolved inside the window would award political power while applying
                        // none of its consequences; and every replayed month is scored against
                        // TODAY's city, so a resolution check would measure 2005's crime wave against
                        // 2031's crime rate. A replayed decade producing no stories is honest.
                        IsReplay = true,
                        Tuning = Tuning
                    });

                    _state = tick.State;

                    // Per replayed month, inside the loop rather than once at the end: the catch
                    // below breaks out on the first failure, and the watermark must then describe the
                    // months that actually ran, not the ones that were planned. Same rule as OnMonth
                    // — a replayed month is a completed month and the next heartbeat must not run it.
                    _state.LastCompletedTickMonth = dates[i].TotalMonths;

                    if (tick.DidWork) replayed++;
                }
                catch (Exception ex)
                {
                    // Stop where it broke rather than abandoning the save. The state is still a valid
                    // political state, just an older one, and the next month boundary will carry on.
                    AgoraMod.Log.Error(ex, "Agora replay failed at " + dates[i] + "; stopping catch-up " +
                                           "with the state at " + _state.Date + ".");
                    break;
                }
            }

            // The log latch, so the first heartbeat after a catch-up does not report a disagreement
            // there is not one of. The cadence itself is carried by the watermark written per month
            // in the loop above.
            _lastTick = _state.Date;
            _hasTicked = true;
            _stateVersion++;

            // A catch-up can run an election, and that is the moment the theme stops being a choice.
            // The monthly tick's own call would not cover it: a save loaded years late locks here or
            // not at all until the next month boundary.
            LockThemeIfElectionHeld();

            AgoraMod.Log.Info("Agora: replayed " + replayed + " month(s) up to " + _state.Date +
                              (truncated ? " (clamped to scheduler.catchUpMaxMonths)." : ".") +
                              " Replayed months were scored against the present city; effects were not applied.");
        }

        // ------------------------------------------------------------------ state and history

        /// <summary>What the sidecar writes. Null before the first load, which it handles.</summary>
        private static PoliticalState GetStateForSave()
        {
            return _state;
        }

        /// <summary>
        /// The sensor layer's rent and land-value memory, for the sidecar to write beside the state.
        /// Null before the sensors exist, which the sidecar handles by writing nothing — leaving the
        /// previous file intact rather than replacing a real history with an empty one.
        /// </summary>
        private static MetricHistoryFile GetMetricHistoryForSave()
        {
            try
            {
                return _snapshots != null ? _snapshots.ExportHistory() : null;
            }
            catch (Exception ex)
            {
                AgoraMod.Log.Warn("Agora could not export its metric history (" + ex.Message +
                                  "); the previous metric_history.json is left in place.");
                return null;
            }
        }

        /// <summary>
        /// Hands the sensors the trend history the sidecar just read, so that a rent trend measured
        /// over a year is actually reachable by a player who quits the game.
        /// </summary>
        /// <remarks>
        /// Warn rather than throw, and never rethrow: a history that will not restore costs the two
        /// trend fields until they refill, which is precisely the state every save was already in
        /// before this file existed. It must not be able to take the load down with it.
        /// </remarks>
        private static void RestoreMetricHistory(SidecarLoadResult result)
        {
            try
            {
                if (result == null || result.MetricHistory == null) return;
                if (_snapshots == null) return;

                _snapshots.RestoreHistory(result.MetricHistory);
            }
            catch (Exception ex)
            {
                AgoraMod.Log.Warn("Agora could not restore its metric history (" + ex.Message +
                                  "); the rent and land-value trends rebuild from this session.");
            }
        }

        /// <summary>
        /// Refills <see cref="_snapshotHistory"/> from the metric ring the sidecar just read, so the
        /// engine's trend legs are as long for a player who quit to the menu as for one who did not.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The ring is session-static and <see cref="ResetForNewSave"/> clears it at every save
        /// boundary, so <c>EngineTickInput.SnapshotHistory</c> was empty on the first tick after every
        /// load — and every <c>delta</c> and <c>windowMonths</c> read goes through exactly that list.
        /// Twelve months played straight fired a trend; the same twelve months with a quit in the
        /// middle did not. That is a desync in the sense of non-negotiable #6, not a cosmetic gap.
        /// </para>
        /// <para>
        /// The ring is rebuilt here rather than borrowed from <see cref="AgoraSnapshotSystem"/>,
        /// which keeps its <c>MetricHistory</c> private. The two restores read the same document
        /// against the same date, so they agree on <i>which</i> samples survive the trim — but not on
        /// how many are kept per series, which is the ring's constructor argument and is the sensor
        /// system's own literal over there. This one therefore states its depth rather than inheriting
        /// a default from a file another lane owns, and states it as
        /// <see cref="SnapshotHistoryMonths"/>: exactly the window asked for below. <c>asOf</c> comes from
        /// the clock and only when the clock is readable — a restore against <c>default(SimDate)</c>
        /// would trim away everything, and the same guard is why <c>RestoreHistory</c> skips it.
        /// </para>
        /// <para>
        /// Warn and continue, never rethrow: a history that will not rebuild costs the trend legs
        /// until they refill, which is the state every save was already in. It must not be able to
        /// take the load down with it.
        /// </para>
        /// </remarks>
        private static void RestoreSnapshotHistory(SidecarLoadResult result)
        {
            try
            {
                if (result == null || result.MetricHistory == null) return;
                if (_time == null) return;

                SimDate asOf;
                if (!_time.TryGetToday(out asOf)) return;

                // The depth is stated rather than defaulted: what this ring has to hold is exactly the
                // window asked for below, and RestoreFrom trims oldest-first, so the newest
                // SnapshotHistoryMonths months per series are what survive. Taking the constructor's
                // default would make the answer depend on a number in a file this one does not own.
                var history = new MetricHistory(SnapshotHistoryMonths);
                history.RestoreFrom(result.MetricHistory, asOf);

                List<CitySnapshot> restored =
                    SnapshotRehydration.Restore(history, asOf, SnapshotHistoryMonths);

                if (restored == null || restored.Count == 0) return;

                // Oldest first, which is the order the ring is already in and the order the trend legs
                // read it in. Trimmed anyway: Restore is bounded by its own argument, and one bound
                // enforced in one place is a bound that stops holding the day the other moves.
                _snapshotHistory.AddRange(restored);
                while (_snapshotHistory.Count > SnapshotHistoryMonths) _snapshotHistory.RemoveAt(0);

                AgoraMod.Log.Info("Agora: rebuilt " + _snapshotHistory.Count +
                                  " month(s) of snapshot history from the metric ring, up to " +
                                  asOf + ".");
            }
            catch (Exception ex)
            {
                AgoraMod.Log.Warn("Agora could not rebuild its snapshot history (" + ex.Message +
                                  "); the engine's trend legs refill from this session.");
            }
        }

        private static CitySnapshot CaptureSnapshot()
        {
            try
            {
                return _snapshots != null ? _snapshots.Capture() : null;
            }
            catch (Exception ex)
            {
                AgoraMod.Log.Warn("Agora could not capture a snapshot (" + ex.Message + "); the engine " +
                                  "will tick without one.");
                return null;
            }
        }

        /// <summary>
        /// Keeps a bounded ring of past snapshots for the indices packet's trend legs. Bounded because
        /// this is per-save memory the player never asked to spend, and the widest tuned window is a
        /// couple of years.
        /// </summary>
        private static void RecordSnapshot(CitySnapshot snapshot)
        {
            if (snapshot == null) return;

            _snapshotHistory.Add(snapshot);
            while (_snapshotHistory.Count > SnapshotHistoryMonths) _snapshotHistory.RemoveAt(0);
        }

        /// <summary>
        /// The id set the flavor provider's output is validated against: everything the engine
        /// currently knows about. An id outside this set is a hallucination and is rejected.
        /// </summary>
        /// <param name="mayCaptureSnapshot">
        /// Whether this call is allowed to ask the sensors for a fresh reading. Only the district ids
        /// come from a snapshot, and <see cref="AgoraSnapshotSystem.Capture"/> is not a getter — on a
        /// day it has not yet sampled it runs the <c>EntityQuery</c> of all six sensor families and
        /// moves the trend history. That is wanted on the deserialize side and forbidden on the UI
        /// update tick (<c>ui_bindings.md</c> §7 rule 6), so the caller states which it is rather than
        /// this deciding. False falls back on <see cref="_lastSnapshot"/> alone.
        /// </param>
        private static FlavorCatalog BuildFlavorCatalog(bool mayCaptureSnapshot)
        {
            if (_state == null) return FlavorCatalog.Empty;

            var partyIds = new List<string>();
            for (int i = 0; i < _state.Parties.Count; i++) partyIds.Add(_state.Parties[i].Id);

            var factionIds = new List<string>();
            for (int i = 0; i < _state.Factions.Count; i++) factionIds.Add(_state.Factions[i].Id);

            var eventIds = new List<string>();
            for (int i = 0; i < _state.ActiveEvents.Count; i++) eventIds.Add(_state.ActiveEvents[i].Id);

            // Live stories AND the archive, unlike the request catalog, which only names the stories
            // this round is asking about. This one decides what a CACHED document may keep on load,
            // and a cache written a year ago legitimately holds resolution prose for stories that
            // have since been archived. Narrowing it to the live set would drop exactly the prose
            // the model was woken to write, silently, on every single load — the id check reports a
            // discard, and a discard of everything reads identically to a round nobody asked for.
            var storyIds = new List<string>();
            AddStoryIds(_state.LiveStories, storyIds, eventIds);
            AddStoryIds(_state.StoryArchive, storyIds, eventIds);

            // Districts come from the sensors rather than from state: the player can rename or add one
            // between ticks, and prose about a district that exists is not a hallucination.
            var districtIds = new List<string>();

            // A load can reach here before the sensor systems have run once, and a fresh capture then
            // returns null. Falling back on the last reading rather than accepting an empty district
            // set matters: an empty set is indistinguishable from "this city has no districts", and it
            // would drop every cached article that referenced one. The retheme path takes the fallback
            // on its own: it has no capture to fall back from.
            CitySnapshot snapshot = (mayCaptureSnapshot ? CaptureSnapshot() : null) ?? _lastSnapshot;
            if (snapshot != null)
            {
                for (int i = 0; i < snapshot.Districts.Count; i++) districtIds.Add(snapshot.Districts[i].Id);
            }

            return new FlavorCatalog(partyIds, factionIds, districtIds, eventIds, storyIds);
        }

        /// <summary>
        /// Adds each story's id to <paramref name="storyIds"/> and each of its slots' event ids to
        /// <paramref name="eventIds"/>.
        /// </summary>
        /// <remarks>
        /// The slot events matter as much as the story ids. A story's events are civic events, drawn
        /// from a catalog the timeline knows nothing about, so they are absent from
        /// <c>ActiveEvents</c> — and an article the model wrote about the event a story asked it to
        /// write about would be dropped for referencing an unknown event id.
        /// </remarks>
        private static void AddStoryIds(List<Story> stories, List<string> storyIds, List<string> eventIds)
        {
            if (stories == null) return;

            for (int i = 0; i < stories.Count; i++)
            {
                Story story = stories[i];
                if (story == null || string.IsNullOrEmpty(story.Id)) continue;

                storyIds.Add(story.Id);

                List<StorySlot> slots = story.Slots;
                if (slots == null) continue;
                for (int s = 0; s < slots.Count; s++)
                {
                    if (slots[s] != null && !string.IsNullOrEmpty(slots[s].EventId))
                    {
                        eventIds.Add(slots[s].EventId);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ catalogs

        /// <summary>
        /// Loads the timeline catalogs for every region from the deployed <c>data/</c> folder: the
        /// global file plus the theme-specific ones. Both are read regardless of the save's theme —
        /// the scheduler filters by region itself, and a save whose theme is changed mid-game must not
        /// need a restart to see its own history.
        /// </summary>
        /// <remarks>
        /// Fails soft, like every other data load here. A missing or malformed catalog leaves the
        /// procedural generator as the only source of events, which is a degraded save rather than a
        /// broken one — and far better than refusing to load a city over a data file.
        /// </remarks>
        private static TimelineCatalog LoadCatalog()
        {
            var sources = new List<TimelineCatalogSource>();
            string[] fileNames = { "timeline_global.json", "timeline_eu.json", "timeline_na.json" };

            for (int i = 0; i < fileNames.Length; i++)
            {
                string path = DataFile(fileNames[i]);
                if (path == null) continue;

                try
                {
                    sources.Add(new TimelineCatalogSource(fileNames[i], File.ReadAllText(path)));
                }
                catch (Exception ex)
                {
                    AgoraMod.Log.Error(ex, "Agora could not read " + path + "; its events will not fire.");
                }
            }

            if (sources.Count == 0)
            {
                AgoraMod.Log.Warn("Agora found no timeline catalogs under the mod's data folder; only " +
                                  "procedural events will fire.");
                return TimelineCatalog.Empty;
            }

            try
            {
                TimelineCatalogLoadResult loaded = TimelineCatalogLoader.Load(sources, Tuning);

                // Single-argument form: a rejected catalog entry is a data error, not an exception,
                // and the two-argument overload is ambiguous on a null Exception.
                for (int i = 0; i < loaded.Errors.Count; i++)
                    AgoraMod.Log.Error("Agora catalog: " + loaded.Errors[i]);

                for (int i = 0; i < loaded.Warnings.Count; i++)
                    AgoraMod.Log.Warn("Agora catalog: " + loaded.Warnings[i]);

                AgoraMod.Log.Info("Agora loaded " + loaded.Catalog.Events.Count + " timeline event(s) from " +
                                  sources.Count + " file(s)" +
                                  (loaded.RejectedEventCount > 0
                                      ? "; " + loaded.RejectedEventCount + " rejected."
                                      : "."));

                return loaded.Catalog;
            }
            catch (Exception ex)
            {
                AgoraMod.Log.Error(ex, "Agora could not load the timeline catalogs; only procedural " +
                                       "events will fire.");
                return TimelineCatalog.Empty;
            }
        }

        /// <summary>
        /// Loads the civic-event catalogs for every region from the deployed <c>data/</c> folder.
        /// </summary>
        /// <remarks>
        /// <para>
        /// All three files regardless of the save's theme, exactly as <see cref="LoadCatalog"/> does
        /// and for the same reason: <c>StoryAssembler</c> filters by region itself, and a save whose
        /// theme is changed mid-game must not need a restart to see its own events.
        /// </para>
        /// <para>
        /// Fails soft. A missing or malformed catalog leaves the story layer with nothing to draft —
        /// a degraded save rather than a broken one, and far better than refusing to load a city over
        /// a data file. Rejections are logged by name and reason, because a silently shorter catalog
        /// is indistinguishable from a quiet month.
        /// </para>
        /// </remarks>
        private static CivicEventCatalog LoadCivicCatalog()
        {
            var sources = new List<CivicEventCatalogSource>();
            string[] fileNames = { "events_global.json", "events_eu.json", "events_na.json" };

            for (int i = 0; i < fileNames.Length; i++)
            {
                string path = DataFile(fileNames[i]);
                if (path == null) continue;

                try
                {
                    sources.Add(new CivicEventCatalogSource(fileNames[i], File.ReadAllText(path)));
                }
                catch (Exception ex)
                {
                    AgoraMod.Log.Error(ex, "Agora could not read " + path + "; its civic events cannot " +
                                           "become stories.");
                }
            }

            if (sources.Count == 0)
            {
                AgoraMod.Log.Warn("Agora found no civic-event catalogs under the mod's data folder; no " +
                                  "story will ever draft in this save.");
                return CivicEventCatalog.Empty;
            }

            try
            {
                CivicEventCatalogLoadResult loaded = CivicEventCatalogLoader.Load(sources, Tuning);

                // Single-argument form: a rejected entry is a data error, not an exception, and the
                // two-argument overload is ambiguous on a null Exception.
                for (int i = 0; i < loaded.Errors.Count; i++)
                    AgoraMod.Log.Error("Agora civic catalog: " + loaded.Errors[i]);

                for (int i = 0; i < loaded.Warnings.Count; i++)
                    AgoraMod.Log.Warn("Agora civic catalog: " + loaded.Warnings[i]);

                AgoraMod.Log.Info("Agora loaded " + loaded.Catalog.Events.Count + " civic event(s) from " +
                                  sources.Count + " file(s)" +
                                  (loaded.RejectedEventCount > 0
                                      ? "; " + loaded.RejectedEventCount + " rejected."
                                      : "."));

                return loaded.Catalog;
            }
            catch (Exception ex)
            {
                AgoraMod.Log.Error(ex, "Agora could not load the civic-event catalogs; no story will " +
                                       "draft in this save.");
                return CivicEventCatalog.Empty;
            }
        }

        // ------------------------------------------------------------------ tuning

        /// <summary>
        /// Reads <c>data/engine_tuning.json</c> from the deployed mod folder, falling back to the
        /// compiled-in defaults.
        /// </summary>
        /// <remarks>
        /// Non-negotiable: no coefficient is hardcoded anywhere. <see cref="EngineTuning.Default"/>
        /// is not an exception to that — it is the same numbers as the shipped file, kept in code so
        /// the engine and its tests do not need a filesystem. Reading the file when it is there is
        /// what lets a player or a tuning pass change a coefficient without a rebuild; falling back
        /// when it is not is what keeps a missing deploy from being fatal.
        /// </remarks>
        private static EngineTuning LoadTuning()
        {
            string path = DataFile("engine_tuning.json");
            if (path == null) return EngineTuning.Default;

            try
            {
                EngineTuning tuning = EngineTuning.FromJson(File.ReadAllText(path));
                AgoraMod.Log.Info("Agora tuning loaded from " + path + ".");
                return tuning;
            }
            catch (Exception ex)
            {
                AgoraMod.Log.Error(ex, "Agora could not read " + path + "; using the built-in tuning " +
                                       "defaults, which match the shipped file.");
                return EngineTuning.Default;
            }
        }

        /// <summary>Path to a file under the deployed <c>data/</c> folder, or null if absent.</summary>
        public static string DataFile(string fileName)
        {
            if (string.IsNullOrEmpty(ModDirectory) || string.IsNullOrEmpty(fileName)) return null;

            try
            {
                string path = Path.Combine(Path.Combine(ModDirectory, "data"), fileName);
                return File.Exists(path) ? path : null;
            }
            catch (Exception)
            {
                // An unreadable or malformed mod path is a deployment problem, not a reason to throw
                // out of a system's OnCreate.
                return null;
            }
        }
    }
}
