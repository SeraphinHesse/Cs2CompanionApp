using System;
using Agora.Core.Contracts;
using Agora.Mod.Time;
using Colossal.Serialization.Entities;
using Game;

namespace Agora.Mod.Core
{
    /// <summary>
    /// The day boundary detector, and through it the political layer's only cadence source.
    ///
    /// <para>
    /// It began as the M0 liveness proof — one log line per in-game day, showing the mod is loaded,
    /// its systems are in the right phase and the sim clock is readable. That log is now optional
    /// (<c>AgoraSettings.LogDailyHeartbeat</c>) and the day change itself is the point: it drives
    /// <see cref="AgoraRuntime.Tick"/>, which refreshes the sensors and, on a month boundary, runs
    /// the political month.
    /// </para>
    ///
    /// <para>
    /// Cadence is derived from the sim clock, never from frame counts. That is what keeps the
    /// political calendar aligned with the game's at any simulation speed, and what stops a
    /// fast-forwarded decade from producing a different history than a slowly played one
    /// (non-negotiable #3, #8).
    /// </para>
    /// </summary>
    public sealed partial class AgoraHeartbeatSystem : GameSystemBase
    {
        private AgoraTimeService _time;
        private SimDate _lastLoggedDay;
        private bool _hasLogged;
        private SimDate _lastTickedDay;
        private bool _hasTicked;
        private bool _broken;

        /// <summary>
        /// Runs every 128th simulation frame rather than every frame. A day cannot elapse between
        /// consecutive frames even at maximum speed, so this loses nothing and avoids a per-frame
        /// date comparison in the hot loop.
        /// </summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 128;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Nothing may escape: GameSystemBase.OnCreate has already subscribed this system to
            // GameManager.onGamePreload, and a system whose OnCreate throws is freed without ever
            // unsubscribing — the resulting dead delegate makes every later new game or save load
            // fail outright. Attach has its own internal guard; this one covers the rest.
            try
            {
                _time = new AgoraTimeService(World);

                // The composition root needs a World, which Mod.OnLoad does not have. This system is
                // the first Agora system the game creates in GameSimulation, so it is the natural
                // place to do it; Attach is idempotent, so a second caller costs nothing.
                AgoraRuntime.Attach(World);
            }
            catch (Exception ex)
            {
                _broken = true;
                Enabled = false;
                AgoraMod.Log.Error(ex, "Agora heartbeat could not initialise; the political layer is " +
                                       "inactive for this session.");
            }
        }

        /// <summary>
        /// Drops the cadence latches for the save that is loading.
        /// </summary>
        /// <remarks>
        /// This system instance outlives an individual save — the world is re-used across "quit to
        /// menu, load another city" — so <see cref="_lastTickedDay"/> still holds the date city A
        /// stopped on. Load city B on that same in-game date and the gate in <see cref="OnUpdate"/>
        /// sees no change and skips the tick entirely, leaving the political layer idle until the
        /// date rolls over. <c>GameSystemBase.OnCreate</c> has already subscribed us to
        /// <c>GameManager.onGamePreload</c>, so this needs no hook of its own; it is raised from
        /// <c>LoadSimulationData</c> and therefore never during a save.
        /// </remarks>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);

            // Unconditional, and deliberately not gated on purpose or mode: a latch carried into the
            // editor or the main menu is just as wrong as one carried into another city.
            _lastTickedDay = default(SimDate);
            _hasTicked = false;
            _lastLoggedDay = default(SimDate);
            _hasLogged = false;
        }

        protected override void OnUpdate()
        {
            if (_broken) return;

            // Master toggle: no-op cleanly rather than unregistering (see AgoraSettings.Enabled).
            var settings = AgoraMod.Settings;
            if (settings == null || !settings.Enabled)
            {
                return;
            }

            // TryGetToday rather than Today: outside a loaded game the clock is not merely stale, it
            // is meaningless, and acting on a main-menu reading would tick the political calendar
            // against a city that does not exist.
            SimDate today;
            try
            {
                if (!_time.TryGetToday(out today)) return;
            }
            catch (Exception ex)
            {
                // The clock is unreadable outside a loaded game. Staying quiet here keeps the main
                // menu clean; a throw would surface as a game-level error dialog.
                AgoraMod.Log.Debug($"Heartbeat could not read the sim clock: {ex.Message}");
                return;
            }

            if (!_hasTicked || today != _lastTickedDay)
            {
                _lastTickedDay = today;
                _hasTicked = true;

                // Drives the sensors, and on a month boundary the political month. AgoraRuntime never
                // throws out of here — it fails closed and logs — so the diagnostic log below still
                // runs on a day whose tick failed, which is exactly when it is worth having.
                AgoraRuntime.Tick(today);
            }

            if (!settings.LogDailyHeartbeat) return;

            if (_hasLogged && today == _lastLoggedDay)
            {
                return;
            }

            _lastLoggedDay = today;
            _hasLogged = true;

            AgoraMod.Log.Info($"day {today}");
        }
    }
}
