using System;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Simulation;
using Unity.Entities;

namespace Agora.Mod.Time
{
    /// <summary>
    /// Delivers AGORA's start year to the game clock. Owns the epoch; nothing else may write it.
    ///
    /// <para>
    /// <b>No Harmony patch is involved, and none is needed.</b> Verified 2026-07-31 against the
    /// shipped assemblies (<c>tools/api-query.ps1 -Members TimeSystem -Public</c>) and the
    /// decompilation in <c>refsrc/</c>:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <c>Game.Common.TimeData.m_StartingYear</c> is the single root of every absolute year in the
    /// game. The only two year computations in <c>Game.dll</c> are
    /// <c>TimeSystem.GetYear(settings, data)</c> and its rendering-frame overload, both
    /// <c>data.m_StartingYear + floor(ticks / ticksPerYear)</c>. Downstream of them: <c>TimeSystem.year</c>,
    /// <c>GetCurrentDateTime()</c>, <c>GetDateTime(frame)</c>, the HUD clock
    /// (<c>Game.UI.InGame.TimeUISystem</c> publishes <c>epochYear = singleton.m_StartingYear</c> and the
    /// UI does its own arithmetic from there), the save-metadata date shown in the load panel
    /// (<c>Game.UI.Menu.MenuUISystem</c>) and <c>Game.Simulation.PlanetarySystem</c>'s celestial year.
    /// Achievements count elapsed seasons, not absolute years. There is no second source.
    /// </description></item>
    /// <item><description>
    /// <c>TimeSystem.startingYear</c> — the public setter Scout 0001 spotted — is read in exactly one
    /// place: <c>TimeSystem.PostDeserialize</c>, and only when <c>context.purpose == Purpose.NewGame</c>,
    /// where it is copied into <c>m_StartingYear</c>. It is therefore sufficient on its own for a new
    /// game, provided it is set before deserialization, which is what
    /// <see cref="OnGamePreload"/> does — <c>GameManager.LoadSimulationData</c> raises
    /// <c>onGamePreload</c> immediately before <c>m_DeserializationSystem.RunOnce()</c>.
    /// </description></item>
    /// <item><description>
    /// For a save that was already started at a stock year, the setter is inert (its value was
    /// consumed at that save's creation). That case needs one write to a public <c>int</c> field on a
    /// public <c>IComponentData</c> singleton — ECS, not patching. <see cref="OnGameLoadingComplete"/>
    /// does it.
    /// </description></item>
    /// </list>
    ///
    /// <para>
    /// <b>Reversibility.</b> <c>TimeData</c> is serialized into the player's vanilla save, so the
    /// rewritten epoch survives uninstalling AGORA. The stock value is therefore captured
    /// <i>before</i> the first write and exposed as <see cref="StockEpochYear"/> for the persistence
    /// layer to store in the sidecar; <see cref="StartYearDeliveryMode.Off"/> puts it back. Changing
    /// <c>m_StartingYear</c> is a pure relabelling — elapsed time is anchored by
    /// <c>m_FirstFrame</c>, which is never touched — so restoring it returns every date surface to
    /// stock exactly.
    /// </para>
    ///
    /// <para>
    /// <b>No map mutation</b> (non-negotiable #4): this system writes one integer on the time
    /// singleton and nothing else. It creates no entity, and touches no district, zone, building or
    /// terrain.
    /// </para>
    /// </summary>
    public sealed partial class AgoraStartYearSystem : GameSystemBase
    {
        /// <summary>
        /// Falls back to the ratified per-save default rather than a literal, so the number lives in
        /// exactly one place — the <c>AgoraSettings.StartYear</c> contract (§3: 1990).
        /// </summary>
        public static readonly int DefaultPoliticalStartYear =
            new global::Agora.Core.Contracts.AgoraSettings().StartYear;

        private TimeSystem _timeSystem;
        private EntityQuery _timeDataQuery;

        private StartYearDeliveryMode _mode = StartYearDeliveryMode.RewriteGameEpoch;
        private int _politicalStartYear = DefaultPoliticalStartYear;

        private int _stockEpochYear;
        private bool _hasStockEpochYear;

        private int _politicalYearOffset;
        private bool _clockReady;
        private bool _broken;

        /// <summary>
        /// Added to the game's year to get the political year. Zero once the epoch has been rewritten
        /// successfully; non-zero in offset-only mode, or if the write was refused.
        /// </summary>
        public int PoliticalYearOffset
        {
            get { return _politicalYearOffset; }
        }

        /// <summary>True once a <c>TimeData</c> singleton has been observed — false in the main menu.</summary>
        public bool IsClockReady
        {
            get { return _clockReady; }
        }

        public StartYearDeliveryMode DeliveryMode
        {
            get { return _mode; }
        }

        public int PoliticalStartYear
        {
            get { return _politicalStartYear; }
        }

        /// <summary>
        /// The player's original epoch year, captured before the first write. Meaningful only when
        /// <see cref="HasStockEpochYear"/> is true. <b>The persistence layer must write this to the
        /// sidecar</b>: once the epoch has been overwritten it cannot be recovered from the save, and
        /// without it the kill-switch can only restore within the session that captured it.
        /// </summary>
        public int StockEpochYear
        {
            get { return _stockEpochYear; }
        }

        public bool HasStockEpochYear
        {
            get { return _hasStockEpochYear; }
        }

        /// <summary>
        /// A cheap re-assert, not a hot path. 4096 simulation frames is about 1/64 of an in-game day,
        /// and the body short-circuits on the first integer comparison unless something moved the
        /// epoch out from under us.
        /// </summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 4096;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            // Must not throw: GameSystemBase.OnCreate has already subscribed this system to
            // GameManager.onGamePreload, and a system freed mid-creation never unsubscribes — the
            // stale delegate then breaks every subsequent load. Falling back to a null _timeSystem is
            // survivable; OnGamePreload already checks for it and the epoch write path degrades to an
            // in-engine offset.
            try
            {
                _timeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
                _timeDataQuery = GetEntityQuery(ComponentType.ReadOnly<TimeData>());
            }
            catch (Exception ex)
            {
                _broken = true;
                Enabled = false;
                AgoraMod.Log.Error(ex, "start year: the clock system could not initialise; AGORA's " +
                                       "political calendar falls back to the game's own epoch.");
            }
        }

        /// <summary>
        /// Applies the settings this save actually carries. Called by the persistence layer once the
        /// sidecar has been read; until then the ratified defaults stand, which is the correct answer
        /// for a brand new city.
        /// </summary>
        /// <param name="recordedStockEpochYear">
        /// The stock epoch year this save recorded on a previous run, or null if it never has.
        /// Passing null for a save AGORA has already rewritten would record 1990 as "stock" and make
        /// the kill-switch a no-op — so the persistence layer must pass through what it stored,
        /// including on the very first call.
        /// </param>
        public void Configure(StartYearDeliveryMode mode, int politicalStartYear,
                              int? recordedStockEpochYear)
        {
            _mode = mode;
            _politicalStartYear = SimClockMath.ClampYear(politicalStartYear);

            if (recordedStockEpochYear.HasValue)
            {
                _stockEpochYear = recordedStockEpochYear.Value;
                _hasStockEpochYear = true;
            }

            Apply("settings changed");
        }

        /// <summary>
        /// Kill-switch. Puts the player's clock back and stops shifting dates. Equivalent to
        /// <c>Configure(StartYearDeliveryMode.Off, …)</c>, kept as a named call because that is what
        /// the M1 gate item ("disabling Agora mid-save reverts every date surface to stock") asks for.
        /// </summary>
        public void RestoreStockClock()
        {
            _mode = StartYearDeliveryMode.Off;
            Apply("kill-switch");
        }

        /// <summary>
        /// New games only: hand the start year to the game's own machinery and let
        /// <c>TimeSystem.PostDeserialize</c> install it. Nothing is written to any component on this
        /// path — the public setter genuinely is sufficient here.
        /// </summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);

            if (_broken) return;

            // Per-save state, and this system instance outlives an individual load: the world is
            // reused across "quit to menu, load another city". Anything remembered from the previous
            // save must be dropped here or the kill-switch would restore the WRONG stock year into
            // someone else's city.
            _hasStockEpochYear = false;
            _stockEpochYear = 0;
            _politicalYearOffset = 0;
            _clockReady = false;
            _mode = StartYearDeliveryMode.RewriteGameEpoch;
            _politicalStartYear = DefaultPoliticalStartYear;

            if (mode != GameMode.Game || purpose != Purpose.NewGame) return;
            if (_timeSystem == null) return;

            // A new city has no sidecar yet, so this is necessarily the default start year. If the
            // player picked a different one at creation, Configure() corrects it on the next line of
            // the load — and because the correction path is a component write, it still lands.
            int target = SimClockMath.ClampYear(_politicalStartYear);

            // Stock for a brand new game is whatever the map or the menu just asked for —
            // GameManager.AutoLoad and MenuUISystem both write TimeSystem.startingYear from
            // MapInfo.startingYear (falling back to DateTime.Now.Year) immediately before Load().
            _stockEpochYear = _timeSystem.startingYear;
            _hasStockEpochYear = true;

            _timeSystem.startingYear = target;
            AgoraMod.Log.Info(
                $"start year: new game, handing {target} to TimeSystem.startingYear (stock {_stockEpochYear}); no patch, no component write.");
        }

        /// <summary>
        /// Fires after deserialization completes (<c>GameManager</c> raises
        /// <c>onGameLoadingComplete</c> once <c>LoadSimulationData</c> has awaited), so the
        /// <c>TimeData</c> singleton exists and <c>TimeSystem.PostDeserialize</c> has already run.
        /// </summary>
        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            if (_broken) return;

            if (mode != GameMode.Game)
            {
                // Main menu or editor: no political clock, and no business touching the epoch.
                _clockReady = false;
                _politicalYearOffset = 0;
                return;
            }

            Apply("game loaded");
        }

        protected override void OnUpdate()
        {
            if (_broken) return;

            // Master toggle: no-op cleanly rather than unregistering (AgoraSettings.Enabled).
            var settings = AgoraMod.Settings;
            if (settings == null || !settings.Enabled) return;
            if (!_clockReady) return;

            int current;
            if (!TryReadEpochYear(out current)) return;

            int expected = ExpectedEpochYear();
            if (current == expected) return;

            AgoraMod.Log.Warn(
                $"start year: epoch drifted to {current}, expected {expected}; re-asserting.");
            Apply("epoch drift");
        }

        /// <summary>What the epoch should read, given the current mode.</summary>
        private int ExpectedEpochYear()
        {
            switch (_mode)
            {
                case StartYearDeliveryMode.RewriteGameEpoch:
                    return SimClockMath.ClampYear(_politicalStartYear);
                case StartYearDeliveryMode.OffsetOnly:
                case StartYearDeliveryMode.Off:
                    return _hasStockEpochYear ? _stockEpochYear : ReadEpochYearOrDefault();
                default:
                    return ReadEpochYearOrDefault();
            }
        }

        private int ReadEpochYearOrDefault()
        {
            int value;
            return TryReadEpochYear(out value) ? value : TimeData.kDefaultStartingYear;
        }

        private void Apply(string cause)
        {
            int current;
            if (!TryReadEpochYear(out current))
            {
                _clockReady = false;
                _politicalYearOffset = 0;
                AgoraMod.Log.Debug($"start year: no TimeData singleton yet ({cause}); deferring.");
                return;
            }

            _clockReady = true;

            StartYearPlan plan = StartYearPlanner.Plan(
                _mode,
                _politicalStartYear,
                current,
                _hasStockEpochYear ? (int?)_stockEpochYear : null);

            // Capture stock BEFORE the write. After it, the original is gone from the save forever.
            if (!_hasStockEpochYear)
            {
                _stockEpochYear = plan.StockEpochYear;
                _hasStockEpochYear = true;
            }

            if (plan.Action != StartYearAction.None)
            {
                if (!TryWriteEpochYear(plan.EpochYearToWrite))
                {
                    AgoraMod.Log.Warn(
                        $"start year: could not write the epoch year ({cause}); falling back to an in-engine offset.");
                }
            }

            // Re-read rather than assume. If the write was refused the offset absorbs the difference
            // and AGORA's own calendar still starts where it was configured to.
            int observed;
            if (!TryReadEpochYear(out observed)) observed = current;

            _politicalYearOffset = StartYearPlanner.PoliticalYearOffset(_mode, _politicalStartYear, observed);

            // Keeps TimeSystem's own field consistent with the installed epoch. Not the delivery
            // mechanism — the game overwrites it from MapInfo before every new game — but leaving it
            // stale would mislead anything that reads it.
            if (_timeSystem != null) _timeSystem.startingYear = observed;

            AgoraMod.Log.Info(
                $"start year ({cause}): {plan.Reason} epoch now {observed}, political offset {_politicalYearOffset}, mode {_mode}.");
        }

        private bool TryReadEpochYear(out int epochYear)
        {
            epochYear = 0;
            try
            {
                if (_timeDataQuery.IsEmptyIgnoreFilter) return false;
                epochYear = _timeDataQuery.GetSingleton<TimeData>().m_StartingYear;
                return true;
            }
            catch (Exception ex)
            {
                AgoraMod.Log.Debug($"start year: TimeData unreadable ({ex.Message}).");
                return false;
            }
        }

        private bool TryWriteEpochYear(int epochYear)
        {
            try
            {
                if (_timeDataQuery.IsEmptyIgnoreFilter) return false;

                // Exactly the pattern TimeSystem.PostDeserialize uses on this same singleton.
                TimeData data = _timeDataQuery.GetSingleton<TimeData>();
                Entity entity = _timeDataQuery.GetSingletonEntity();
                data.m_StartingYear = epochYear;
                EntityManager.SetComponentData(entity, data);
                return true;
            }
            catch (Exception ex)
            {
                AgoraMod.Log.Warn($"start year: epoch write failed ({ex.Message}).");
                return false;
            }
        }
    }
}
