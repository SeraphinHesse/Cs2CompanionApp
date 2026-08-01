using System;
using Agora.Core.Contracts;
using Agora.Mod.Time;
using Game;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// Shared plumbing for every sensor system: the master toggle, the once-per-sim-day cadence, and
    /// the rule that a sensor never throws into the simulation loop.
    ///
    /// <para>
    /// <b>Cadence.</b> Sensors read aggregates, and aggregates do not change within a game day. Each
    /// subclass therefore samples at most once per in-game day and caches the result; the snapshot
    /// system reads the cache. This is what keeps the §"getters run every UI tick" trap closed —
    /// nothing downstream ever runs a query.
    /// </para>
    ///
    /// <para>
    /// <b>Phase.</b> Sensors are registered at <c>SystemUpdatePhase.GameSimulation</c>. That is after
    /// the game's own simulation systems have advanced the frame, so a sensor reads settled state
    /// rather than a half-updated one — reading in the wrong phase is a silent correctness bug, not
    /// a crash, which is why the registration lines are spelled out in the packet report rather than
    /// left to whoever wires them up.
    /// </para>
    /// </summary>
    public abstract partial class AgoraSensorSystemBase : GameSystemBase
    {
        private AgoraTimeService _time;
        private SimDate _lastSampledDate;
        private bool _hasSampled;
        private bool _loggedReadFailure;
        private bool _broken;

        /// <summary>True once this sensor holds a reading for the current day.</summary>
        public bool HasReading => _hasSampled;

        /// <summary>The date of the cached reading. Meaningless while <see cref="HasReading"/> is false.</summary>
        public SimDate ReadingDate => _lastSampledDate;

        /// <summary>The calibration in force. Read through a property so a test can substitute one.</summary>
        protected SensorCalibration Calibration => SensorCalibration.Active ?? new SensorCalibration();

        /// <summary>
        /// 128 simulation frames. A game day cannot elapse in fewer, so the day-change guard below
        /// still fires exactly once per day, and the cost of the check leaves the hot loop.
        /// </summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 128;

        protected override void OnCreate()
        {
            base.OnCreate();

            // CreateQueries resolves stock game systems by type, so a member that moved between game
            // versions surfaces here as a TypeLoadException. That must not escape: GameSystemBase.OnCreate
            // has already subscribed this system to GameManager.onGamePreload, and a system whose
            // OnCreate throws is freed without ever unsubscribing — leaving a dead delegate that makes
            // every later new game or save load fail. One blind sensor is a far better outcome.
            try
            {
                _time = new AgoraTimeService(World);
                CreateQueries();
            }
            catch (Exception ex)
            {
                _broken = true;
                Enabled = false;
                AgoraMod.Log.Error(ex, GetType().Name + " could not initialise; this sensor family " +
                                       "reports nothing for the rest of the session.");
            }
        }

        /// <summary>
        /// Build every <c>EntityQuery</c> here — never in <c>OnUpdate</c>. Query construction
        /// allocates and registers with the entity manager; doing it per frame is the single most
        /// expensive mistake available in an ECS sensor.
        /// </summary>
        protected abstract void CreateQueries();

        /// <summary>Take a fresh reading for <paramref name="date"/> and cache it.</summary>
        protected abstract void Sample(SimDate date);

        /// <summary>
        /// Discards the cached reading, so the next update re-samples. Called on load, when the
        /// world underneath the cache has been replaced wholesale.
        /// </summary>
        public virtual void Invalidate()
        {
            _hasSampled = false;
        }

        /// <summary>
        /// Samples now if the cached reading is not for <paramref name="date"/>. The snapshot system
        /// calls this so a capture is never served stale data, whatever the update cadence.
        /// </summary>
        public void EnsureSampled(SimDate date)
        {
            if (_broken) return;
            if (_hasSampled && _lastSampledDate == date) return;
            TrySample(date);
        }

        protected override void OnUpdate()
        {
            if (_broken) return;

            var settings = AgoraMod.Settings;
            if (settings == null || !settings.Enabled)
            {
                // Master toggle: no-op cleanly rather than unregistering, so re-enabling mid-session
                // does not need a reload.
                return;
            }

            SimDate today;
            if (!TryGetToday(out today)) return;

            if (_hasSampled && _lastSampledDate == today) return;

            TrySample(today);
        }

        /// <summary>
        /// The current sim date, or false when no game is loaded. Never throws: outside a loaded
        /// game there is simply nothing to sense, and that is a normal state, not an error.
        /// </summary>
        protected bool TryGetToday(out SimDate today)
        {
            today = default(SimDate);
            try
            {
                today = _time.Today;
                return true;
            }
            catch (Exception ex)
            {
                // No loaded game: the clock is unreadable and there is nothing to sense. Logged once
                // per system so the main menu does not fill the log.
                if (!_loggedReadFailure)
                {
                    _loggedReadFailure = true;
                    AgoraMod.Log.Debug($"{GetType().Name}: sim clock unreadable ({ex.Message}); sensing paused.");
                }

                return false;
            }
        }

        private void TrySample(SimDate date)
        {
            try
            {
                Sample(date);
                _lastSampledDate = date;
                _hasSampled = true;
            }
            catch (Exception ex)
            {
                // Fail closed, exactly as the LLM provider does. A sensor that throws into
                // GameSimulation takes the player's game down over a metric nobody asked for; keeping
                // the previous reading and logging is always the better trade.
                AgoraMod.Log.Warn($"{GetType().Name}: sampling failed at {date} ({ex.GetType().Name}: {ex.Message}); keeping the previous reading.");
            }
        }
    }
}
