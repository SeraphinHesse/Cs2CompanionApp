using System;
using System.Collections.Generic;
using System.IO;
using Agora.Core.Contracts;
using Agora.Core.Engine;
using Agora.Core.Engine.Parties;
using Agora.Core.Events.Catalog;
using Agora.Core.Events.Scheduler;
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
    public static class AgoraRuntime
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
        private static PoliticalState _state;
        private static SimDate _startDate;
        private static readonly List<CitySnapshot> _snapshotHistory = new List<CitySnapshot>();
        private static bool _manualWakeRequested;

        private static CitySnapshot _lastSnapshot;
        private static int _stateVersion;

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

                // City B's party ids are not city A's, but an id that collided would arrive here
                // already marked provisional and let a stopgap name be overwritten in a save that
                // never had one. Carrying it across a save boundary is exactly the class of bug this
                // method exists for.
                _provisionalNamePartyIds.Clear();

                // Cadence. A city loaded on the month city A last ticked would otherwise be treated as
                // already ticked for that month.
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

                // Per-save kill-switch (#10). False computes all the politics and applies none of it.
                AgoraEffects.EffectsEnabled = _saveSettings.EffectsEnabled;

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

                RebuildFlavor();

                // The pool cannot see the registry through IFlavorProvider — it is handed a snapshot
                // and a date only — so the roster has to be pushed at it. Doing it here rather than
                // waiting for the first wake is what lets the very first poll name anybody.
                SeedFlavorRoster(_state.Date);

                _saveActive = true;
                _stateVersion++;

                if (result != null && !string.IsNullOrEmpty(result.Explanation))
                {
                    AgoraMod.Log.Info("Agora sidecar: " + result.Explanation);
                }

                if (result != null && result.MonthsToReplay > 0) Replay(result.MonthsToReplay);

                // Last, and after the replay: a catch-up can found parties, and a party reaching the
                // dashboard without a name is the one thing this whole path exists to prevent. The
                // canned pool answers synchronously, so there is no frame in which the UI sees a blank.
                //
                // Its own catch, not the outer one: that one clears _saveActive, and a cosmetic naming
                // step must not be able to switch the political layer off for the session. A party
                // with no name is a blemish; a save with no politics is the mod not running.
                try
                {
                    EnsureEveryPartyNamed(_state.Date);
                }
                catch (Exception nameEx)
                {
                    AgoraMod.Log.Warn("Agora flavor: naming the unnamed parties failed (" + nameEx.Message +
                                      "); they stay unnamed until the next prose collection.");
                }
            }
            catch (Exception ex)
            {
                _saveActive = false;
                AgoraMod.Log.Error(ex, "Agora could not apply per-save settings; continuing with defaults.");
            }
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
            // to say why. Four counts here turn that into a one-line diagnosis.
            AgoraMod.Log.Info("Agora flavor: cache re-validation catalog — " +
                              catalog.PartyCount + " parties, " +
                              catalog.FactionCount + " factions, " +
                              catalog.DistrictCount + " districts, " +
                              catalog.EventCount + " events.");

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
            RegionTheme theme;
            if (string.Equals(value, "Eu", StringComparison.OrdinalIgnoreCase)) theme = RegionTheme.Eu;
            else if (string.Equals(value, "Na", StringComparison.OrdinalIgnoreCase)) theme = RegionTheme.Na;
            else return CommandOutcome.BadValue;

            // Refused rather than queued. A retheme disposes the flavor provider and deletes its cache
            // file, and doing either with a claude subprocess in flight races the runner's own
            // completion path — Dispose's join is bounded at two seconds and can return with the worker
            // still alive, so the ordering below is a guard and not a proof. PendingWake stays true
            // until the worker has finished with the disk, which is later than the state says. The
            // player can press the button again in a few seconds.
            if (theme != _saveSettings.Theme && PendingWake) return CommandOutcome.Busy;

            RegionTheme previous = _saveSettings.Theme;
            RethemeResult retheme = PoliticalEngine.Retheme(_state, theme, _startDate, Tuning);

            if (!retheme.Accepted) return retheme.Outcome;

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

                AgoraMod.Log.Info("Agora: region theme changed from " + previous + " to " + theme +
                                  "; regenerated " + _state.Parties.Count + " parties at " +
                                  _startDate + ".");
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

            bool monthChanged = !_hasTicked || today.TotalMonths != _lastTick.TotalMonths;

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
                ManualFlavorWakeRequested = _manualWakeRequested,
                Tuning = Tuning
            };

            EngineTickResult tick = PoliticalEngine.Advance(input);

            _state = tick.State;
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
            // outranks yearly: the rarer the trigger, the more the prose should be about it.
            FlavorWakeReason reason;
            if ((tick.LlmWake & LlmWakeCadence.Election) != 0) reason = FlavorWakeReason.Election;
            else if ((tick.LlmWake & LlmWakeCadence.Manual) != 0) reason = FlavorWakeReason.Manual;
            else reason = FlavorWakeReason.Yearly;

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
                        Tuning = Tuning
                    });

                    _state = tick.State;
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

            return new FlavorCatalog(partyIds, factionIds, districtIds, eventIds);
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
