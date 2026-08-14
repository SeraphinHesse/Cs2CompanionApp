using System;
using Agora.Core.Contracts;
using Agora.Mod.Core;
using Agora.Mod.Time;
using Colossal.Logging;
using Colossal.Serialization.Entities;
using Game;
using Game.City;
using Game.Serialization;
using Unity.Entities;

namespace Agora.Mod.Persistence
{
    /// <summary>
    /// Adapts the game's logger to <see cref="ISidecarLog"/>, which is what keeps
    /// <see cref="SidecarStore"/> free of <c>Colossal.*</c>.
    /// </summary>
    public sealed class ColossalSidecarLog : ISidecarLog
    {
        private readonly ILog _log;

        public ColossalSidecarLog(ILog log)
        {
            _log = log;
        }

        public void Info(string message)
        {
            if (_log != null) _log.Info(message);
        }

        public void Warn(string message)
        {
            if (_log != null) _log.Warn(message);
        }

        public void Error(string message, Exception error)
        {
            if (_log == null) return;

            if (error != null)
            {
                _log.Error(error, message);
            }
            else
            {
                _log.Error(message);
            }
        }
    }

    /// <summary>
    /// The bridge between the game's save file and Agora's sidecar.
    ///
    /// <para>
    /// It does two things. First, it <b>owns Agora's save identity</b>: a guid written into the save
    /// itself through the serialization hooks, exactly as <c>politicsmodplan.md</c> §5 requires —
    /// <i>"Do not assume the engine exposes a stable save GUID… it survives renames and copies, it
    /// cannot collide with a filename, and it retires risk §13.1 outright."</i> Second, it
    /// <b>flushes and reloads the sidecar</b> around the game's own save and load.
    /// </para>
    ///
    /// <para>
    /// <b>How the game finds it.</b> <c>SystemSerializerLibrary.Initialize</c> walks every system in
    /// the world and gives any that implements <see cref="IDefaultSerializable"/> a block inside the
    /// save file, keyed by type name. Mod systems are included — this was read out of
    /// <c>Colossal.Core</c>, not assumed. <see cref="IDefaultSerializable"/> rather than bare
    /// <see cref="ISerializable"/> is deliberate: the game logs an error for the latter, and, more
    /// importantly, <see cref="SetDefaults"/> is the hook that fires when a save has <i>no</i> Agora
    /// block, which is precisely the "installed into an existing city" case.
    /// </para>
    ///
    /// <para>
    /// <b>Timing.</b> The identity is minted in <see cref="PostDeserialize"/>, which needs nothing
    /// but the serialization context. The sidecar is loaded later, in
    /// <see cref="OnGameLoaded(Context)"/>, which the game raises after the whole Deserialize phase
    /// has finished — by then <c>TimeSystem</c> has been deserialized and the sim clock is readable,
    /// which it is not during the phase itself. That ordering matters and is not incidental:
    /// reconciliation is a comparison against the current sim date (#8), so a clock that is still
    /// showing the previous session's date would pick the wrong snapshot.
    /// </para>
    ///
    /// <para>
    /// Writing happens in <see cref="PreSerialize"/>. §5 allows this explicitly — <i>"If no post-save
    /// hook exists, serialize at save-start with the sim paused"</i> — and the game's own
    /// <c>ClimateSystem</c> uses the same hook for the same reason.
    /// </para>
    ///
    /// <para>
    /// <b>Registration</b> (a serial gate applies this to <c>Mod.cs</c>; this system does not
    /// register itself):
    /// </para>
    /// <code>
    /// updateSystem.UpdateBefore&lt;PreSerialize&lt;AgoraSidecarSystem&gt;&gt;(SystemUpdatePhase.Serialize);
    /// updateSystem.UpdateAfter&lt;PostDeserialize&lt;AgoraSidecarSystem&gt;&gt;(SystemUpdatePhase.Deserialize);
    /// </code>
    /// </summary>
    public sealed partial class AgoraSidecarSystem : GameSystemBase,
        IDefaultSerializable, ISerializable, IPreSerialize, IPostDeserialize
    {
        /// <summary>
        /// Version of the block this system writes into the save file — distinct from the sidecar's
        /// own <c>schemaVersion</c>, which versions the JSON on disk.
        /// </summary>
        /// <remarks>
        /// Bumping this means writing more fields, and every future reader must consume exactly as
        /// many as its writer produced. The game may hand all systems one shared buffer
        /// (<c>m_SeparateSystemBuffers == false</c>), in which case under-reading does not merely
        /// lose Agora's data — it misaligns the stream for every system deserialized after us.
        /// </remarks>
        public const int IdentityFormatVersion = 1;

        private Guid _saveGuid;
        private int _loadedIdentityFormatVersion;

        private SidecarStore _store;
        private AgoraTimeService _time;
        private CityConfigurationSystem _cityConfiguration;

        /// <summary>Agora's save identity, or <see cref="Guid.Empty"/> before one has been minted.</summary>
        public Guid SaveGuid
        {
            get { return _saveGuid; }
        }

        /// <summary>The sidecar, or null if the user-data path could not be resolved.</summary>
        public SidecarStore Store
        {
            get { return _store; }
        }

        /// <summary>
        /// Supplies the state to write at save time. Left unset until the engine tick exists; when it
        /// is null this system still maintains the save identity and still loads, it simply has
        /// nothing to write.
        /// </summary>
        public Func<PoliticalState> StateProvider { get; set; }

        /// <summary>
        /// Supplies the sensor layer's rent and land-value memory to write at save time. Separate
        /// from <see cref="StateProvider"/> because the two are independent: a city with no politics
        /// yet is still accumulating the history, and a session whose engine failed to attach should
        /// not also lose a year of measurements.
        /// </summary>
        public Func<MetricHistoryFile> MetricHistoryProvider { get; set; }

        /// <summary>
        /// The most recent load result, for a consumer that starts after <c>OnGameLoaded</c> has
        /// already fired. Null before the first load.
        /// </summary>
        public SidecarLoadResult PendingLoad { get; private set; }

        /// <summary>
        /// Raised as soon as the sidecar has been read, with the reconciliation plan attached.
        /// </summary>
        public Action<SidecarLoadResult> LoadHandler { get; set; }

        protected override void OnCreate()
        {
            base.OnCreate();

            // Never registered in an update phase: PreSerialize<T> / PostDeserialize<T> call this
            // system's methods directly, and OnGameLoaded arrives as an event. An empty OnUpdate
            // running every frame would be pure waste.
            Enabled = false;

            try
            {
                _time = new AgoraTimeService(World);
                _cityConfiguration = World.GetOrCreateSystemManaged<CityConfigurationSystem>();
                _store = new SidecarStore(SidecarPaths.Root(ResolveUserDataPath()),
                                          new ColossalSidecarLog(AgoraMod.Log));
            }
            catch (Exception ex)
            {
                // A mod that cannot find its data directory must still let the city load.
                _store = null;
                AgoraMod.Log.Error(ex, "Agora persistence could not initialise; the sidecar is disabled " +
                                       "for this session.");
            }
        }

        protected override void OnUpdate()
        {
            // Intentionally empty; see OnCreate.
        }

        /// <summary>
        /// Forgets the previous save's identity so the incoming one mints or reads its own.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="EnsureIdentity"/> early-returns on a non-empty guid, and this system instance
        /// survives "quit to menu, load another city" — so without this, city B whose save carries no
        /// Agora block (a save made before the mod was installed, where <see cref="SetDefaults"/>
        /// fires but the guid was already set) would keep city A's guid and write into city A's
        /// sidecar directory.
        /// </para>
        /// <para>
        /// Safe to do unconditionally: <c>GameManager</c> raises <c>onGamePreload</c> only from
        /// <c>LoadSimulationData</c>, never from the save path, so this cannot orphan a sidecar by
        /// clearing the guid out from under <see cref="PreSerialize"/>. <see cref="Deserialize"/> and
        /// <see cref="SetDefaults"/> both run afterwards.
        /// </para>
        /// <para>
        /// The runtime reset belongs here rather than only in <see cref="OnGameLoaded"/>'s handler
        /// call: that call is skipped whenever <c>_store.Load</c> throws, and city A's political state
        /// surviving that skip would be written into city B's sidecar directory by the next
        /// <see cref="PreSerialize"/>. It is idempotent with the reset
        /// <c>AgoraRuntime.OnSidecarLoaded</c> does first.
        /// </para>
        /// </remarks>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);

            _saveGuid = Guid.Empty;
            _loadedIdentityFormatVersion = IdentityFormatVersion;
            PendingLoad = null;

            // Guarded: a throw out of OnGamePreload disables the system for the rest of the process,
            // which would silently stop sidecar writing entirely.
            try
            {
                // SaveBoundary, not ModShutdown: the outgoing city's entities are still alive here —
                // ClearSystem destroys them later, in the deserialize phase this callback precedes —
                // so the slot table is live buffers, and a revert would work. It is skipped because
                // nothing would ever see it: that city is discarded moments later, and its save file
                // was written before this ran. See AgoraRuntime.ResetCause.
                AgoraRuntime.ResetForNewSave(ResetCause.SaveBoundary);
            }
            catch (Exception ex)
            {
                AgoraMod.Log.Error(ex, "Agora could not reset its per-save state before load; the " +
                                       "political layer may carry state from the previous city.");
            }
        }

        // ------------------------------------------------------------ save identity

        /// <summary>
        /// Runs when the save contains no Agora block — a new city, or Agora newly installed into an
        /// existing one. Clearing the guid is what marks the save as needing an identity;
        /// <see cref="PostDeserialize"/> mints it once the context is available.
        /// </summary>
        public void SetDefaults(Context context)
        {
            _saveGuid = Guid.Empty;
            _loadedIdentityFormatVersion = IdentityFormatVersion;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            int version = IdentityFormatVersion;
            writer.Write(version);

            string guid = SidecarPaths.FormatGuid(_saveGuid);
            writer.Write(guid);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            int version;
            reader.Read(out version);

            string guidText;
            reader.Read(out guidText);

            _loadedIdentityFormatVersion = version;

            Guid parsed;
            _saveGuid = Guid.TryParse(guidText, out parsed) ? parsed : Guid.Empty;

            // No logging here: this can run off the main thread as part of the deserialization job
            // graph. PostDeserialize reports what was found.
        }

        /// <summary>
        /// Mints the identity if the save did not carry one. Runs on the main thread, before the
        /// Deserialize phase completes.
        /// </summary>
        public void PostDeserialize(Context context)
        {
            try
            {
                if (!IsCityPurpose(context.purpose))
                {
                    // Map editor, or a cleanup pass. There is no city, so there is nothing political.
                    return;
                }

                if (_loadedIdentityFormatVersion > IdentityFormatVersion)
                {
                    AgoraMod.Log.Warn("Agora save identity was written by a newer version of the mod " +
                                      "(block format " + _loadedIdentityFormatVersion + " > " +
                                      IdentityFormatVersion + "). Continuing with the guid it carried.");
                }

                EnsureIdentity(context);
            }
            catch (Exception ex)
            {
                AgoraMod.Log.Error(ex, "Agora could not establish a save identity; the political layer " +
                                       "is inactive for this session.");
            }
        }

        /// <summary>
        /// Fired after the entire Deserialize phase, when the sim clock is readable. This is where
        /// the sidecar is reconciled against the loaded city (§5).
        /// </summary>
        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);

            try
            {
                if (!IsCityPurpose(serializationContext.purpose)) return;
                if (!IsEnabled()) return;
                if (_store == null) return;

                EnsureIdentity(serializationContext);

                if (_saveGuid == Guid.Empty)
                {
                    AgoraMod.Log.Warn("Agora has no save identity after load; skipping sidecar reconciliation.");
                    return;
                }

                SimDate today = _time.Today;

                PendingLoad = _store.Load(_saveGuid, today);

                Action<SidecarLoadResult> handler = LoadHandler;
                if (handler != null) handler(PendingLoad);
            }
            catch (Exception ex)
            {
                // GameSystemBase would catch this itself — and then disable the system, which would
                // silently stop save writing for the rest of the session. Catch it here instead.
                PendingLoad = null;
                AgoraMod.Log.Error(ex, "Agora sidecar load failed; the political layer starts fresh for " +
                                       "this session.");
            }
        }

        /// <summary>
        /// Save-start. §5: <i>"Write <c>state_*.json</c> inside the save callback, atomically… If no
        /// post-save hook exists, serialize at save-start with the sim paused."</i>
        /// </summary>
        public void PreSerialize(Context context)
        {
            try
            {
                if (!IsEnabled()) return;
                if (_store == null) return;

                EnsureIdentity(context);

                if (_saveGuid == Guid.Empty) return;

                // Before the state, and not gated on it. The trend history is a measurement record,
                // so it is worth writing for a city whose politics have not started — and if it were
                // written after the early return below, that city would restart its rent history from
                // zero on every load, which is the failure metric_history.json exists to end.
                WriteMetricHistory();

                Func<PoliticalState> provider = StateProvider;
                if (provider == null)
                {
                    // No engine attached yet. The identity is still written into the save by
                    // Serialize, so the sidecar this save eventually gets will be the right one.
                    return;
                }

                PoliticalState state = provider();
                if (state == null) return;

                // The identity is this system's to assign, not the engine's: it is the first
                // argument to every seed derivation (#2) and must match the directory being written.
                state.SaveGuid = _saveGuid;

                _store.SaveState(state);
            }
            catch (Exception ex)
            {
                // Never let a sidecar failure propagate into the game's save path.
                AgoraMod.Log.Error(ex, "Agora could not write its sidecar during save; the previous " +
                                       "snapshot is intact and the city save is unaffected.");
            }
        }

        // ------------------------------------------------------------ helpers

        /// <summary>
        /// Writes the sensor's trend memory, if there is a provider and it produced something. Its own
        /// try/catch so that a failure here cannot cost the state file that is written next — losing
        /// a year of rent samples is a blemish, losing the political state is not.
        /// </summary>
        private void WriteMetricHistory()
        {
            try
            {
                Func<MetricHistoryFile> provider = MetricHistoryProvider;
                if (provider == null) return;

                MetricHistoryFile history = provider();
                if (history == null) return;

                _store.SaveMetricHistory(_saveGuid, history);
            }
            catch (Exception ex)
            {
                AgoraMod.Log.Error(ex, "Agora could not write its metric history during save; the " +
                                       "rent and land-value trends will rebuild from the previous file.");
            }
        }

        private void EnsureIdentity(Context context)
        {
            if (_saveGuid != Guid.Empty) return;
            if (_store == null) return;
            if (!IsEnabled()) return;

            // The instigator hash identifies the asset this session was started from — the map for a
            // new city, the save for a loaded one. It is stable, but it is NOT unique per city (two
            // cities from one map share it), which is why SaveIdentity.Mint also walks past any
            // candidate whose sidecar directory is already claimed.
            uint x = context.instigatorGuid.value.x;
            uint y = context.instigatorGuid.value.y;
            uint z = context.instigatorGuid.value.z;
            uint w = context.instigatorGuid.value.w;

            string cityName = _cityConfiguration != null ? _cityConfiguration.cityName : null;

            string explanation;
            _saveGuid = SaveIdentity.Mint(_store.Root, x, y, z, w, cityName, out explanation);

            AgoraMod.Log.Info("Agora: " + explanation);
        }

        private static bool IsCityPurpose(Purpose purpose)
        {
            return purpose == Purpose.NewGame
                || purpose == Purpose.LoadGame
                || purpose == Purpose.SaveGame;
        }

        /// <summary>
        /// The global master toggle. Per non-negotiable #10 almost everything is per-save, but the
        /// master switch has to work before a save exists, so it lives on the options page.
        /// </summary>
        /// <remarks>
        /// When Agora is off, the identity is neither minted nor cleared: a previously minted guid
        /// keeps travelling in the save, so toggling the mod off and on again does not orphan a
        /// player's political history.
        /// </remarks>
        private static bool IsEnabled()
        {
            var settings = AgoraMod.Settings;
            return settings == null || settings.Enabled;
        }

        /// <summary>
        /// The game's user-data directory, which is where <c>ModsData/</c> lives.
        /// </summary>
        /// <remarks>
        /// <c>Colossal.PSI.Environment.EnvPath.kUserDataPath</c> is the game's own name for this and
        /// would be the more idiomatic call, but <c>Colossal.PSI.Common</c> is not among
        /// <c>Agora.Mod.csproj</c>'s references and this packet does not own that file.
        /// <c>EnvPath</c> initialises <c>kUserDataPath</c> to
        /// <see cref="UnityEngine.Application.persistentDataPath"/> verbatim, so the two are the same
        /// string; if the reference is ever added, this should switch to <c>EnvPath</c>.
        /// </remarks>
        private static string ResolveUserDataPath()
        {
            string path = UnityEngine.Application.persistentDataPath;

            if (string.IsNullOrEmpty(path))
            {
                throw new InvalidOperationException(
                    "Application.persistentDataPath is empty, so Agora cannot locate ModsData.");
            }

            return path;
        }
    }
}
