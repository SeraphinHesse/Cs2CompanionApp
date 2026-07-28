using Agora.Mod.Core;
using Agora.Mod.UiBindings;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace Agora.Mod
{
    /// <summary>
    /// Mod entry point. The game's loader finds this by reflection — it is the only type here the
    /// engine calls directly.
    ///
    /// <para>
    /// M0 scope: register settings, log one line per in-game day, and publish one UI binding. That is
    /// deliberately small — its job is to prove the toolchain end to end (C# build → deploy → load →
    /// settings page → ECS system → UI binding → React panel) before any political logic exists.
    /// </para>
    /// </summary>
    public sealed class AgoraMod : IMod
    {
        public const string Id = "Agora";

        /// <summary>Single logger for the whole mod. Writes to the game's Player.log.</summary>
        public static readonly ILog Log = LogManager.GetLogger(Id);

        /// <summary>
        /// Per-save settings live in the sidecar (non-negotiable #10). These are the few genuinely
        /// global ones — chiefly the master toggle, which must work before any save is loaded.
        /// </summary>
        public static AgoraSettings Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info($"{Id} loading.");

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                Log.Info($"{Id} asset: {asset.path}");
            }

            Settings = new AgoraSettings(this);
            Settings.RegisterInOptionsUI();

            // Without a localization source the options page renders raw locale keys.
            GameManager.instance.localizationManager.AddSource("en-US", new AgoraLocaleSource(Settings));

            AssetDatabase.global.LoadSettings(Id, Settings, new AgoraSettings(this));

            // GameSimulation: the heartbeat reads the sim clock, so it must run where the clock is
            // already advanced for this frame. UIUpdate: bindings are read by the UI on that phase,
            // and publishing anywhere else means the panel lags a frame behind.
            updateSystem.UpdateAt<AgoraHeartbeatSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<AgoraDebugUISystem>(SystemUpdatePhase.UIUpdate);

            Log.Info($"{Id} loaded.");
        }

        public void OnDispose()
        {
            Log.Info($"{Id} unloading.");

            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }
    }
}
