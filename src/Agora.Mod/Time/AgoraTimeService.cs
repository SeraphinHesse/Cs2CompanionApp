using System;
using Agora.Core.Contracts;
using Game.Simulation;
using Unity.Entities;

namespace Agora.Mod.Time
{
    /// <summary>
    /// The single source of truth for dates (non-negotiable #8). Everything political reads from
    /// here; nothing else computes a year.
    ///
    /// <para>
    /// Wraps <see cref="TimeSystem"/> and translates the game's <see cref="DateTime"/> into
    /// <see cref="SimDate"/>, which is the only date type the engine understands.
    /// </para>
    ///
    /// <para>
    /// <b>Scout finding (0001):</b> <c>TimeSystem.startingYear</c> has a public setter. If the
    /// game's date surfaces all derive from <see cref="TimeSystem"/>, setting it may deliver the
    /// 1990 start with no Harmony patch at all — which would retire the plan's second-largest risk.
    /// M1 must verify that before writing any patch; see <c>docs/status.md</c>.
    /// </para>
    /// </summary>
    public sealed class AgoraTimeService : IClock
    {
        private readonly TimeSystem _timeSystem;

        public AgoraTimeService(World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            _timeSystem = world.GetOrCreateSystemManaged<TimeSystem>();
        }

        /// <summary>The current political date.</summary>
        public SimDate Today
        {
            get
            {
                DateTime now = _timeSystem.GetCurrentDateTime();
                return new SimDate(now.Year, now.Month, now.Day);
            }
        }

        /// <summary>
        /// The game's own starting year. M1 sets this to the save's configured start year (default
        /// 1990) rather than patching the clock, if verification confirms the display follows it.
        /// </summary>
        public int StartingYear => _timeSystem.startingYear;

        /// <summary>Raw game date, for the few call sites that must interoperate with game APIs.</summary>
        public DateTime CurrentDateTime => _timeSystem.GetCurrentDateTime();
    }
}
