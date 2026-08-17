using System;
using System.IO;
using Agora.Mod.Core;
using Agora.Mod.Effects;
using Agora.Mod.Persistence;
using Agora.Mod.Sensors;
using Agora.Mod.Time;
using Agora.Mod.UiBindings;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Game.Serialization;

namespace Agora.Mod
{
    /// <summary>
    /// Mod entry point. The game's loader finds this by reflection — it is the only type here the
    /// engine calls directly.
    ///
    /// <para>
    /// <c>OnLoad</c> does three things and no more: register the options page and its localization,
    /// record where the mod was deployed from so <c>data/</c> can be found, and register every ECS
    /// system. It deliberately builds nothing — there is no ECS world yet at this point, so the
    /// composition root (<see cref="AgoraRuntime"/>) is attached later, from the first system the
    /// world creates.
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

                // Where data/engine_tuning.json and the timeline catalogs are deployed alongside the
                // assembly. Captured here because this is the only place the game tells us.
                AgoraRuntime.ModDirectory = Path.GetDirectoryName(asset.path);
            }
            else
            {
                Log.Warn($"{Id} could not resolve its own executable asset, so data/ cannot be located. " +
                         "Tuning and catalogs fall back to the compiled-in defaults.");
            }

            Settings = new AgoraSettings(this);
            Settings.RegisterInOptionsUI();

            // Without a localization source the options page renders raw locale keys.
            GameManager.instance.localizationManager.AddSource("en-US", new AgoraLocaleSource(Settings));

            AssetDatabase.global.LoadSettings(Id, Settings, new AgoraSettings(this));

            RegisterSystems(updateSystem);

            Log.Info($"{Id} loaded.");
        }

        /// <summary>
        /// Every Agora system, in load order. The order of these calls does not by itself decide
        /// execution order — <c>UpdateSystem</c> appends within a phase and the game creates systems
        /// when it builds the world — so each registration below says what it actually depends on.
        /// </summary>
        private static void RegisterSystems(UpdateSystem updateSystem)
        {
            // Each registration is isolated. UpdateSystem.UpdateAt<T> does not merely record a type —
            // it calls World.GetOrCreateSystemManaged<T>(), so the system is constructed here and now
            // and any exception from its OnCreate would otherwise propagate out of OnLoad and abandon
            // every registration below it. That is exactly how a single bad UI binding once left the
            // dashboard half-registered. One system failing must cost only that system.
            Action<Action> register = registration =>
            {
                try
                {
                    registration();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"{Id}: a system failed to register and has been skipped; the rest " +
                                  "of the mod continues to load.");
                }
            };

            // --- clock (non-negotiable #8) -------------------------------------------------------
            // First, because every other system reads dates through AgoraTimeService, which reads the
            // political-year offset from this one. The real work happens in OnGamePreload /
            // OnGameLoadingComplete, which GameSystemBase dispatches to any CREATED system regardless
            // of phase — but a system that is never registered is never created, so it would never
            // receive those callbacks at all. GameSimulation is chosen for the periodic drift
            // re-assert, which needs a frame where the sim clock is coherent.
            register(() => updateSystem.UpdateAt<AgoraStartYearSystem>(SystemUpdatePhase.GameSimulation));

            // --- persistence (§5) ----------------------------------------------------------------
            // Never registered in an update phase of its own: it sets Enabled = false in OnCreate and
            // is driven entirely by the serialization hooks. PreSerialize<T> / PostDeserialize<T> are
            // the game's own wrappers — this mirrors how ClimateSystem and TimeSystem register
            // (SystemOrder.cs:731, :860). UpdateBefore on Serialize means Agora's sidecar is written
            // before the game writes the save that carries Agora's identity; UpdateAfter on
            // Deserialize means the identity has been read before anything asks for it.
            register(() => updateSystem.UpdateBefore<PreSerialize<AgoraSidecarSystem>>(SystemUpdatePhase.Serialize));
            register(() => updateSystem.UpdateAfter<PostDeserialize<AgoraSidecarSystem>>(SystemUpdatePhase.Deserialize));

            // --- sensors -------------------------------------------------------------------------
            // Only the aggregator is registered. The six sensor families are created on demand by
            // AgoraSnapshotSystem and sampled through EnsureSampled, which is idempotent per sim day
            // — registering them as well would add six no-op OnUpdate calls per 128 frames and, worse,
            // would let a family sample on a frame the aggregator has not reached, so the snapshot
            // could mix two days' readings. GameSimulation: sensors must read state the game's own
            // simulation systems have already settled for this frame.
            register(() => updateSystem.UpdateAt<AgoraSnapshotSystem>(SystemUpdatePhase.GameSimulation));

            // --- effects (§7) --------------------------------------------------------------------
            // GameSimulation, the same phase as the game's own CityModifierUpdateSystem. The
            // reconciler is written so correctness does not depend on running after it — worst case
            // is one pass of latency after the game rebuilds a modifier buffer.
            register(() => updateSystem.UpdateAt<AgoraEffectApplicationSystem>(SystemUpdatePhase.GameSimulation));

            // --- cadence -------------------------------------------------------------------------
            // Last of the simulation systems: it drives AgoraRuntime.Tick, which reads the sensors,
            // so it wants them sampled for this frame first. Also the system that calls
            // AgoraRuntime.Attach, because that needs a World and OnLoad does not have one.
            register(() => updateSystem.UpdateAt<AgoraHeartbeatSystem>(SystemUpdatePhase.GameSimulation));

            // --- UI ------------------------------------------------------------------------------
            // UIUpdate: bindings are read by the UI on that phase, and publishing anywhere else means
            // the panel lags a frame behind.
            register(() => updateSystem.UpdateAt<AgoraDebugUISystem>(SystemUpdatePhase.UIUpdate));

            // The dashboard publishers. State goes first so that a panel which renders in the same
            // frame can resolve a party id to a name and a colour from agora.parties before the seat,
            // district and news payloads that reference it arrive — within a frame this is cosmetic,
            // but it costs nothing to register them in dependency order and it documents the one.
            //
            // None of these poll the ECS world: each republishes only when AgoraRuntime.StateVersion
            // moves, which is the engine's monthly cadence rather than the renderer's.
            register(() => updateSystem.UpdateAt<AgoraStateUISystem>(SystemUpdatePhase.UIUpdate));
            register(() => updateSystem.UpdateAt<AgoraSeatsUISystem>(SystemUpdatePhase.UIUpdate));
            register(() => updateSystem.UpdateAt<AgoraDistrictsUISystem>(SystemUpdatePhase.UIUpdate));
            register(() => updateSystem.UpdateAt<AgoraNewsUISystem>(SystemUpdatePhase.UIUpdate));

            // Stories last, and it is the only publisher that also carries a write surface the player
            // reaches every month. Its five inbound CallBindings run on this same phase, which is what
            // lets a player answer a story while the sim is held paused behind a card — GameSimulation
            // does not tick then, so a command deferred to the engine's own phase would appear to do
            // nothing at all. Same reasoning as agora.state.setSetting.
            register(() => updateSystem.UpdateAt<AgoraStoriesUISystem>(SystemUpdatePhase.UIUpdate));
        }

        public void OnDispose()
        {
            Log.Info($"{Id} unloading.");

            // Drops the flavor provider (which owns a background thread and possibly a child
            // process), clears the effect ledger and releases the per-save references. It also hands
            // the city's modifier buffers back, here rather than in AgoraEffectApplicationSystem's
            // OnDestroy: the mod can be unloaded with the city still open, and this is the last point
            // at which those buffers are reachable. See AgoraRuntime.ResetCause.ModShutdown.
            AgoraRuntime.Detach();

            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }
    }
}
