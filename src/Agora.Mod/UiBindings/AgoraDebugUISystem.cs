using System;
using Agora.Mod.Core;
using Agora.Mod.Time;
using Colossal.UI.Binding;
using Game.UI;

namespace Agora.Mod.UiBindings
{
    /// <summary>
    /// Publishes the M0 debug bindings consumed by <c>ui/src/shell/bindings.ts</c>, which the
    /// always-mounted dashboard toggle (<c>ui/src/shell/AgoraButton.tsx</c>) renders. They used to
    /// feed a panel of their own; that panel was retired and its readout folded into the button, so
    /// the liveness proof now sits on the one element that is on screen whatever else has failed.
    ///
    /// <para>
    /// Small on purpose: its value is proving the whole C# → JS pipeline works before the dashboard
    /// depends on it. A renamed binding fails at runtime with an empty panel rather than at compile
    /// time, so every binding here is registered in <c>docs/contracts/ui_bindings.md</c> first.
    /// </para>
    /// </summary>
    public sealed partial class AgoraDebugUISystem : UISystemBase
    {
        /// <summary>Binding group. The JS side addresses bindings as (group, name).</summary>
        private const string Group = "agora.debug";

        private AgoraTimeService _time;

        protected override void OnCreate()
        {
            base.OnCreate();

            // Guarded for the same reason AgoraUISystemBase guards its own: GameSystemBase.OnCreate
            // has already subscribed this system to GameManager.onGamePreload, and a throw from here
            // would leave that subscription pointing at a freed system state — which makes every
            // subsequent new game or save load fail. This system does not inherit AgoraUISystemBase,
            // so it needs its own copy of the guard.
            try
            {
                _time = new AgoraTimeService(World);

                // Getters run on every UI update tick — keep them cheap and never query the ECS world
                // inside one. Anything expensive gets computed in a simulation system and cached.
                AddUpdateBinding(new GetterValueBinding<int>(Group, "simDay", GetSimMonth));
                AddUpdateBinding(new GetterValueBinding<bool>(Group, "enabled", GetEnabled));
                AddUpdateBinding(new GetterValueBinding<string>(Group, "simDate", GetSimDate));
            }
            catch (Exception ex)
            {
                AgoraMod.Log.Error(ex, "Agora debug bindings could not be registered; the debug panel " +
                                       "stays empty for this session.");
            }
        }

        private static bool GetEnabled() => AgoraMod.Settings != null && AgoraMod.Settings.Enabled;

        /// <summary>
        /// The political month, 1–12. Named <c>simDay</c> for binding compatibility with the M0
        /// panel; the value is not a day and never was.
        /// </summary>
        /// <remarks>
        /// This used to read <c>CurrentDateTime.DayOfYear</c>. That is wrong on the shipped game and
        /// wrong in a way no panel would reveal: <c>TimeSystem.GetCurrentDateTime()</c> builds its
        /// <see cref="DateTime"/> as <c>new DateTime(0).AddYears(year-1).AddDays(day-1)</c> with
        /// <c>day = 1 + floor(daysPerYear * normalizedDate) % daysPerYear</c>, and the shipped
        /// <c>daysPerYear</c> is <b>12</b> — so the result never left January and
        /// <c>DayOfYear</c> could only ever return 1–12. It looked like a plausible day-of-year for
        /// the first twelve days of a game and then silently stopped moving. The Time packet found
        /// the underlying bug; <see cref="AgoraTimeService.Month"/> derives the month from
        /// <c>normalizedDate</c> and is correct at any <c>daysPerYear</c>.
        /// </remarks>
        private int GetSimMonth()
        {
            try
            {
                return _time.Month;
            }
            catch (Exception)
            {
                // Readable only inside a loaded game; 0 renders as "—" in the panel.
                return 0;
            }
        }

        private string GetSimDate()
        {
            try
            {
                return _time.Today.ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
