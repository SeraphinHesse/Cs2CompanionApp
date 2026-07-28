using System;
using Agora.Mod.Core;
using Agora.Mod.Time;
using Colossal.UI.Binding;
using Game.UI;

namespace Agora.Mod.UiBindings
{
    /// <summary>
    /// Publishes the M0 debug bindings consumed by <c>ui/src/panels/DebugPanel.tsx</c>.
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

            _time = new AgoraTimeService(World);

            // Getters run on every UI update tick — keep them cheap and never query the ECS world
            // inside one. Anything expensive gets computed in a simulation system and cached.
            AddUpdateBinding(new GetterValueBinding<int>(Group, "simDay", GetSimDay));
            AddUpdateBinding(new GetterValueBinding<bool>(Group, "enabled", GetEnabled));
            AddUpdateBinding(new GetterValueBinding<string>(Group, "simDate", GetSimDate));
        }

        private static bool GetEnabled() => AgoraMod.Settings != null && AgoraMod.Settings.Enabled;

        private int GetSimDay()
        {
            try
            {
                return _time.CurrentDateTime.DayOfYear;
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
