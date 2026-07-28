using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace Agora.Mod.Core
{
    /// <summary>
    /// The options page.
    ///
    /// <para>
    /// Deliberately minimal. Non-negotiable #10 puts configuration in the per-save sidecar, not in
    /// global settings — start year, theme and LLM cadence are all per-save and belong there. What
    /// stays here is the small set that must work before any save exists: chiefly the master toggle.
    /// </para>
    /// </summary>
    [FileLocation("ModsSettings/Agora/Agora")]
    [SettingsUIGroupOrder(GeneralGroup, DiagnosticsGroup)]
    [SettingsUIShowGroupName(GeneralGroup, DiagnosticsGroup)]
    public sealed class AgoraSettings : ModSetting
    {
        public const string MainTab = "Main";
        public const string GeneralGroup = "General";
        public const string DiagnosticsGroup = "Diagnostics";

        public AgoraSettings(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        /// <summary>
        /// Master toggle. When off, Agora's systems must no-op cleanly rather than unregister —
        /// unregistering mid-session leaves the ECS world in a state the game does not expect.
        /// </summary>
        [SettingsUISection(MainTab, GeneralGroup)]
        public bool Enabled { get; set; }

        /// <summary>Logs one line per in-game day. Useful for the M0 gate; noisy afterwards.</summary>
        [SettingsUISection(MainTab, DiagnosticsGroup)]
        public bool LogDailyHeartbeat { get; set; }

        public override void SetDefaults()
        {
            Enabled = true;
            LogDailyHeartbeat = true;
        }
    }
}
