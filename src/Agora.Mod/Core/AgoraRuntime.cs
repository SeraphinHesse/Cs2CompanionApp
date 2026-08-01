using System;
using System.Collections.Generic;
using System.IO;
using Agora.Core.Contracts;
using Agora.Core.Engine;
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
        private static readonly object Gate = new object();

        private static World _world;
        private static AgoraTimeService _time;
        private static AgoraSnapshotSystem _snapshots;
        private static AgoraDistrictSensorSystem _districts;
        private static AgoraSidecarSystem _sidecar;
        private static AgoraStartYearSystem _startYear;

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

        private static SimDate _lastTick;
        private static bool _hasTicked;
        private static bool _attached;
        private static bool _saveActive;

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
        public static bool PendingWake
        {
            get { return _pendingWake || (_flavor != null && _flavor.State == FlavorProviderState.Running); }
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
        public static void Detach()
        {
            lock (Gate)
            {
                DisposeFlavor();

                _saveActive = false;
                _hasTicked = false;
                _saveSettings = null;
                _state = null;
                _manualWakeRequested = false;
                _snapshotHistory.Clear();

                if (_sidecar != null)
                {
                    _sidecar.LoadHandler = null;
                    _sidecar.StateProvider = null;
                }

                AgoraEffects.Shutdown();

                // Assigning null restores EngineTuning.Default rather than leaving the sensors holding
                // a tuning whose save has been closed.
                SensorTuning.Active = null;

                _world = null;
                _time = null;
                _snapshots = null;
                _districts = null;
                _sidecar = null;
                _startYear = null;
                _attached = false;
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
                _saveSettings = (result != null && result.Settings != null)
                    ? result.Settings
                    : new CoreSettings();

                // Sensors cache per day and the world underneath them has just been replaced. Not
                // invalidating here would let the new city inherit the previous one's rents.
                if (_snapshots != null) _snapshots.Invalidate();

                ConfigureClock();

                // Per-save kill-switch (#10). False computes all the politics and applies none of it.
                AgoraEffects.EffectsEnabled = _saveSettings.EffectsEnabled;

                // The political start date, and the phase anchor for every engine cadence. January of
                // the save's start year: the year is a per-save setting, and the day-of-month must not
                // matter to a month-granular calendar.
                _startDate = new SimDate(_saveSettings.StartYear, 1, 1);

                _snapshotHistory.Clear();
                _manualWakeRequested = false;

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

                _hasTicked = false;
                _saveActive = true;
                _stateVersion++;

                if (result != null && !string.IsNullOrEmpty(result.Explanation))
                {
                    AgoraMod.Log.Info("Agora sidecar: " + result.Explanation);
                }

                if (result != null && result.MonthsToReplay > 0) Replay(result.MonthsToReplay);
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

        private static void RebuildFlavor()
        {
            DisposeFlavor();

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
            _flavor = FlavorProviders.Create(saveGuid, _saveSettings.Theme, directory, BuildFlavorCatalog());
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
                    StatusWord = party.Status.ToString(),
                    CurrentName = party.Name
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
                    CurrentName = faction.Name
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

            _lastAttemptDate = today;

            if (payload == null)
            {
                _stateVersion++;
                return;
            }

            _flavorPayload = payload;
            _lastFlavorDate = today;
            _pendingWake = false;

            if (_state != null)
            {
                ApplyProseNames(payload);
                _state.LastFlavorDate = today;
            }

            _stateVersion++;
        }

        private static void ApplyProseNames(FlavorPayload payload)
        {
            for (int i = 0; i < payload.Parties.Count; i++)
            {
                PartyFlavor flavor = payload.Parties[i];
                if (flavor == null || string.IsNullOrEmpty(flavor.PartyId)) continue;

                for (int p = 0; p < _state.Parties.Count; p++)
                {
                    Party party = _state.Parties[p];
                    if (string.CompareOrdinal(party.Id, flavor.PartyId) != 0) continue;

                    if (!string.IsNullOrEmpty(flavor.Name)) party.Name = flavor.Name;
                    if (!string.IsNullOrEmpty(flavor.ShortName)) party.ShortName = flavor.ShortName;
                    if (!string.IsNullOrEmpty(flavor.Description)) party.Description = flavor.Description;
                    if (!string.IsNullOrEmpty(flavor.Slogan)) party.Slogan = flavor.Slogan;
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
        private static FlavorCatalog BuildFlavorCatalog()
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
            CitySnapshot snapshot = CaptureSnapshot();
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
