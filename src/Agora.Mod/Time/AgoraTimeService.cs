using System;
using Agora.Core.Contracts;
using Game.Simulation;
using Unity.Entities;

namespace Agora.Mod.Time
{
    /// <summary>
    /// The single source of truth for dates (non-negotiable #8). Everything political reads from
    /// here; nothing else in the mod computes a year, a month, or an offset.
    ///
    /// <para>
    /// Two readings are combined. <see cref="TimeSystem"/> supplies the raw game clock, and
    /// <see cref="AgoraStartYearSystem"/> supplies the political-year offset it installed at load.
    /// The offset is normally zero, because the default delivery mode rewrites the game's own epoch
    /// so the HUD and AGORA agree; it is non-zero in offset-only mode, or when the epoch write was
    /// refused. Reading the offset here rather than assuming it is what keeps the two calendars from
    /// silently diverging.
    /// </para>
    ///
    /// <para>
    /// <b>What this type deliberately does not do:</b> it never calls
    /// <c>TimeSystem.GetCurrentDateTime()</c> to derive a month. That method builds its
    /// <see cref="DateTime"/> from <c>day = 1 + floor(daysPerYear * normalizedDate) % daysPerYear</c>
    /// with a shipped <c>daysPerYear</c> of <b>12</b>, so its <c>Month</c> is always 1 and its
    /// <c>Day</c> is 1–12 — a January-only calendar. <see cref="SimClockMath"/> derives the month
    /// from <c>normalizedDate</c> instead, which is correct at any <c>daysPerYear</c>.
    /// </para>
    ///
    /// <para>
    /// Construction is cheap and idempotent: both backing systems are fetched with
    /// <c>GetOrCreateSystemManaged</c>, so several call sites may each hold their own instance
    /// without any of them owning state.
    /// </para>
    /// </summary>
    public sealed class AgoraTimeService : IClock
    {
        private readonly TimeSystem _timeSystem;
        private readonly AgoraStartYearSystem _startYear;

        public AgoraTimeService(World world)
        {
            if (world == null) throw new ArgumentNullException("world");
            _timeSystem = world.GetOrCreateSystemManaged<TimeSystem>();
            _startYear = world.GetOrCreateSystemManaged<AgoraStartYearSystem>();
        }

        /// <summary>
        /// The current political date, month-granular with <see cref="SimDate.Day"/> pinned to 1 —
        /// see <see cref="SimClockMath"/> for why. Total function: it does not throw in the main menu,
        /// where <see cref="IsReady"/> is false and the value is meaningless rather than fatal.
        /// </summary>
        public SimDate Today
        {
            get { return SimClockMath.ToSimDate(PoliticalYear, _timeSystem.normalizedDate); }
        }

        /// <summary>
        /// False until a city is loaded. Callers that would act on a date — the engine tick, the
        /// scheduler, the sidecar writer — must check this rather than treat the main-menu reading as
        /// a real date.
        /// </summary>
        public bool IsReady
        {
            get { return _startYear.IsClockReady; }
        }

        /// <summary>The year AGORA reasons in: the game's year plus the installed offset.</summary>
        public int PoliticalYear
        {
            get { return _timeSystem.year + _startYear.PoliticalYearOffset; }
        }

        /// <summary>
        /// The year the game itself believes it is in. Equal to <see cref="PoliticalYear"/> whenever
        /// the epoch rewrite succeeded, which is the normal case. Exposed for diagnostics and for the
        /// M1 gate, which compares the two.
        /// </summary>
        public int GameYear
        {
            get { return _timeSystem.year; }
        }

        /// <summary>Political month, 1–12.</summary>
        public int Month
        {
            get { return SimClockMath.MonthFromNormalizedDate(_timeSystem.normalizedDate); }
        }

        /// <summary>
        /// Fraction of the way through the current political month, 0–1. <b>Presentation only</b> —
        /// it changes every frame, and feeding it into engine state would decouple seeded streams
        /// from the monthly tick.
        /// </summary>
        public double MonthProgress
        {
            get { return SimClockMath.MonthProgress(_timeSystem.normalizedDate); }
        }

        /// <summary>
        /// Fraction of the way through the current in-game day, 0–1. Presentation only, same caveat.
        /// </summary>
        public double DayProgress
        {
            get { return _timeSystem.normalizedTime; }
        }

        /// <summary>
        /// The epoch year currently installed in the game's <c>TimeData</c>, i.e. what every stock
        /// date surface counts from. Diagnostics and the M1 gate.
        /// </summary>
        public int GameStartingYear
        {
            get { return _timeSystem.startingYear; }
        }

        /// <summary>Added to <see cref="GameYear"/> to get <see cref="PoliticalYear"/>.</summary>
        public int PoliticalYearOffset
        {
            get { return _startYear.PoliticalYearOffset; }
        }

        /// <summary>
        /// Raw game date, for the few call sites that must interoperate with game APIs.
        /// <b>Do not read <c>.Month</c> or <c>.Day</c> off this</b> — with the shipped
        /// <c>daysPerYear</c> of 12 they are always 1 and 1–12 respectively. Use
        /// <see cref="Today"/>. Throws outside a loaded game, because the underlying call needs the
        /// <c>TimeData</c> and <c>TimeSettingsData</c> singletons.
        /// </summary>
        public DateTime CurrentDateTime
        {
            get { return _timeSystem.GetCurrentDateTime(); }
        }

        /// <summary>
        /// The clock, if a city is loaded. Preferred over <see cref="Today"/> anywhere a main-menu
        /// reading would be acted on.
        /// </summary>
        public bool TryGetToday(out SimDate date)
        {
            if (!IsReady)
            {
                date = default(SimDate);
                return false;
            }

            date = Today;
            return true;
        }
    }
}
