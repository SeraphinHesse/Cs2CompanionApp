using System;
using Agora.Core.Contracts;
using Agora.Mod.Time;
using Game;

namespace Agora.Mod.Core
{
    /// <summary>
    /// Logs one line per in-game day. This is the M0 gate's liveness proof: it shows the mod is
    /// loaded, its systems are registered in the right phase, and the sim clock is readable.
    ///
    /// <para>
    /// From M1 this becomes the monthly engine tick's trigger. The day-change detection stays —
    /// deriving cadence from the sim clock rather than from frame counts is what keeps the political
    /// calendar aligned with the game's, at any simulation speed.
    /// </para>
    /// </summary>
    public sealed partial class AgoraHeartbeatSystem : GameSystemBase
    {
        private AgoraTimeService _time;
        private SimDate _lastLoggedDay;
        private bool _hasLogged;

        /// <summary>
        /// Runs every 128th simulation frame rather than every frame. A day cannot elapse between
        /// consecutive frames even at maximum speed, so this loses nothing and avoids a per-frame
        /// date comparison in the hot loop.
        /// </summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 128;

        protected override void OnCreate()
        {
            base.OnCreate();
            _time = new AgoraTimeService(World);
        }

        protected override void OnUpdate()
        {
            // Master toggle: no-op cleanly rather than unregistering (see AgoraSettings.Enabled).
            var settings = AgoraMod.Settings;
            if (settings == null || !settings.Enabled || !settings.LogDailyHeartbeat)
            {
                return;
            }

            SimDate today;
            try
            {
                today = _time.Today;
            }
            catch (Exception ex)
            {
                // The clock is unreadable outside a loaded game. Staying quiet here keeps the main
                // menu clean; a throw would surface as a game-level error dialog.
                AgoraMod.Log.Debug($"Heartbeat could not read the sim clock: {ex.Message}");
                return;
            }

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
