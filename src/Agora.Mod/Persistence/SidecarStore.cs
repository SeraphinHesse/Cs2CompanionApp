// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Agora.Core.Contracts;
using Newtonsoft.Json.Linq;

namespace Agora.Mod.Persistence
{
    /// <summary>
    /// Logging seam, so the store stays free of <c>Colossal.*</c> and can be exercised without the
    /// game. <see cref="SidecarStore"/> logs a lot on purpose: every recovery path in §5 is required
    /// to be visible in the log rather than silent.
    /// </summary>
    public interface ISidecarLog
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message, Exception error);
    }

    /// <summary>Discards everything. The default when no logger is supplied.</summary>
    public sealed class NullSidecarLog : ISidecarLog
    {
        public static readonly NullSidecarLog Instance = new NullSidecarLog();

        private NullSidecarLog()
        {
        }

        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception error)
        {
        }
    }

    /// <summary>
    /// <c>timeline_progress.json</c>. A mirror of <see cref="PoliticalState.FiredEventIds"/>, kept as
    /// its own file because §5 lists it as one and because the scheduler wants to know what has
    /// already fired without deserializing a thirty-year political state to find out.
    /// </summary>
    public sealed class TimelineProgressFile
    {
        public int SchemaVersion { get; set; }

        /// <summary>Fired event ids, sorted ascending — the same order the state contract requires.</summary>
        public List<string> FiredEventIds { get; set; }

        public TimelineProgressFile()
        {
            SchemaVersion = SidecarSchema.CurrentTimelineProgressVersion;
            FiredEventIds = new List<string>();
        }
    }

    /// <summary>What a load produced, and everything worth saying about how it went.</summary>
    public sealed class SidecarLoadResult
    {
        public SidecarLoadResult()
        {
            Outcome = ReconciliationOutcome.FreshStart;
            Warnings = new List<string>();
        }

        public ReconciliationOutcome Outcome { get; set; }

        /// <summary>The loaded state, or null when the engine must cold-start.</summary>
        public PoliticalState State { get; set; }

        /// <summary>
        /// Per-save settings (non-negotiable #10). Always populated: from the loaded state, else from
        /// <c>settings.json</c>, else defaults. Never null, so no caller has to invent one.
        /// </summary>
        public AgoraSettings Settings { get; set; }

        /// <summary>
        /// True when <see cref="Settings"/> is <c>new AgoraSettings()</c> rather than something read
        /// from disk — no state file carried a settings block and there was no <c>settings.json</c>
        /// either.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Runtime only. Not a persisted contract, and no schema version depends on it</b> — it
        /// describes this load, not the save, and there is nowhere on disk it could be written that
        /// would not immediately contradict itself.
        /// </para>
        /// <para>
        /// It exists because "no prior state" is not the same question as "the player has never
        /// chosen a theme": <see cref="Load"/> falls back to <c>settings.json</c> when there is no
        /// state file, so a save that answered the first-run prompt and then crashed before its first
        /// monthly tick has settings but no state. Asking only <see cref="HasState"/> would show that
        /// player the prompt a second time and offer to throw their choice away.
        /// </para>
        /// </remarks>
        public bool SettingsAreDefaults { get; set; }

        /// <summary>Sim date of <see cref="State"/>, or <c>default</c> when there is none.</summary>
        public SimDate SnapshotDate { get; set; }

        /// <summary>Whole months the engine must replay to reach the current sim date.</summary>
        public int MonthsToReplay { get; set; }

        /// <summary>The file that was loaded, or null.</summary>
        public string SourcePath { get; set; }

        /// <summary>One sentence describing the reconciliation decision.</summary>
        public string Explanation { get; set; }

        /// <summary>Everything that went wrong but did not stop the load.</summary>
        public List<string> Warnings { get; private set; }

        public bool HasState
        {
            get { return State != null; }
        }
    }

    /// <summary>
    /// Sidecar IO: reading and writing <c>ModsData/Agora/&lt;saveGuid&gt;/</c>
    /// (<c>politicsmodplan.md</c> §5).
    ///
    /// <para>
    /// Two rules shape everything here. <b>Non-negotiable #6</b> — every write is atomic and load
    /// must never desync — which is why nothing writes in place and why a file that will not parse is
    /// moved aside rather than deleted or overwritten. And <b>§5's recovery rule</b> — <i>log a
    /// warning; never crash, never reset politics</i> — which is why no public method on this type
    /// throws. A save that Agora cannot read is a save that loads without Agora's opinions for one
    /// session, not a save that fails to load.
    /// </para>
    ///
    /// <para>
    /// The type has no game dependency: it takes a root directory and a log seam. That is deliberate,
    /// since <c>tests/Agora.Core.Tests</c> must run on a machine with no copy of the game and this is
    /// the part of the packet worth exercising.
    /// </para>
    /// </summary>
    public sealed class SidecarStore
    {
        private readonly string _root;
        private readonly ISidecarLog _log;

        public SidecarStore(string root, ISidecarLog log)
        {
            if (string.IsNullOrEmpty(root)) throw new ArgumentException("Root must not be empty.", "root");

            _root = root;
            _log = log ?? NullSidecarLog.Instance;
        }

        /// <summary><c>&lt;userData&gt;/ModsData/Agora</c>.</summary>
        public string Root
        {
            get { return _root; }
        }

        public string DirectoryFor(Guid saveGuid)
        {
            return SidecarPaths.SaveDirectory(_root, saveGuid);
        }

        // ---------------------------------------------------------------- load

        /// <summary>
        /// Reconciles what is on disk against the current sim date and loads the best snapshot it
        /// can. Never throws.
        /// </summary>
        public SidecarLoadResult Load(Guid saveGuid, SimDate currentDate)
        {
            var result = new SidecarLoadResult();

            try
            {
                if (saveGuid == Guid.Empty)
                {
                    result.Outcome = ReconciliationOutcome.FreshStart;
                    result.Explanation = "No save identity yet; nothing to load.";
                    result.Settings = new AgoraSettings();
                    result.SettingsAreDefaults = true;
                    _log.Info("Sidecar: " + result.Explanation);
                    return result;
                }

                string directory = DirectoryFor(saveGuid);

                int staleTemps = AtomicFile.CleanStaleTemps(directory);
                if (staleTemps > 0)
                {
                    // A leftover temp means a previous session died mid-write. The real file is
                    // whatever it was before that write, which is exactly the guarantee #6 buys.
                    _log.Warn("Sidecar: removed " + staleTemps.ToString(CultureInfo.InvariantCulture) +
                              " incomplete write(s) from a previous session.");
                }

                IList<StateFileRef> candidates = SidecarPaths.EnumerateStateFiles(directory);
                bool damaged = false;

                while (true)
                {
                    ReconciliationPlan plan = LoadReconciliation.Plan(candidates, currentDate, damaged);

                    result.Outcome = plan.Outcome;
                    result.MonthsToReplay = plan.MonthsToReplay;
                    result.Explanation = plan.Explanation;

                    if (plan.Chosen == null)
                    {
                        break;
                    }

                    PoliticalState state;
                    string failure;

                    if (TryLoadState(plan.Chosen.Path, saveGuid, out state, out failure))
                    {
                        result.State = state;
                        result.SnapshotDate = state.Date;
                        result.SourcePath = plan.Chosen.Path;
                        break;
                    }

                    damaged = true;
                    result.Warnings.Add(failure);
                    _log.Warn("Sidecar: " + failure);

                    candidates = LoadReconciliation.Without(candidates, plan.Chosen);
                }

                bool settingsAreDefaults;
                result.Settings = ResolveSettings(result.State, directory, result.Warnings,
                                                  out settingsAreDefaults);
                result.SettingsAreDefaults = settingsAreDefaults;

                string summary = "Sidecar: " + (result.Explanation ?? "loaded.");

                bool routine = result.Outcome == ReconciliationOutcome.ExactMatch
                            || (result.Outcome == ReconciliationOutcome.FreshStart && result.Warnings.Count == 0);

                if (routine)
                {
                    // An exact match, or a city that has simply never had politics before.
                    _log.Info(summary);
                }
                else
                {
                    // Everything else is a reconciliation, and §5 asks for it to be warned about
                    // rather than absorbed quietly.
                    _log.Warn(summary);
                }

                return result;
            }
            catch (Exception ex)
            {
                // The whole point of this catch: a load failure must degrade to "no politics this
                // session", never to a failed city load.
                result.Outcome = ReconciliationOutcome.FreshStart;
                result.State = null;
                result.MonthsToReplay = 0;
                result.Explanation = "Sidecar load failed outright; starting fresh.";
                result.Warnings.Add(ex.Message);

                if (result.Settings == null)
                {
                    result.Settings = new AgoraSettings();
                    result.SettingsAreDefaults = true;
                }

                _log.Error("Sidecar: load failed; the political layer starts fresh for this session.", ex);
                return result;
            }
        }

        private bool TryLoadState(string path, Guid expectedSaveGuid, out PoliticalState state, out string failure)
        {
            state = null;
            failure = null;

            string json;
            Exception readError;

            if (!AtomicFile.TryReadAllText(path, out json, out readError))
            {
                failure = "Could not read " + Path.GetFileName(path) +
                          (readError == null ? " (missing)." : ": " + readError.Message);
                return false;
            }

            JObject root;
            try
            {
                root = AgoraJson.ParseObject(json);
            }
            catch (Exception ex)
            {
                string moved = AtomicFile.Quarantine(path);
                failure = Path.GetFileName(path) + " is not valid JSON (" + ex.Message + "); moved to " +
                          (moved == null ? "nowhere — it could not be renamed" : Path.GetFileName(moved)) + ".";
                return false;
            }

            MigrationResult migration = SidecarSchema.Migrate(root, SidecarDocument.State);

            if (!migration.IsLoadable)
            {
                // Deliberately NOT quarantined. A file from a newer Agora, or one this build has no
                // migration path for, is intact and valuable — quarantining it would look like
                // damage recovery while actually hiding a downgrade.
                failure = Path.GetFileName(path) + ": " + migration.Message;
                return false;
            }

            if (migration.Outcome != MigrationOutcome.Current)
            {
                _log.Info("Sidecar: " + Path.GetFileName(path) + ": " + migration.Message);
            }

            PoliticalState parsed;
            try
            {
                parsed = AgoraJson.ToObject<PoliticalState>(root);
            }
            catch (Exception ex)
            {
                string moved = AtomicFile.Quarantine(path);
                failure = Path.GetFileName(path) + " did not match the state contract (" + ex.Message +
                          "); moved to " +
                          (moved == null ? "nowhere — it could not be renamed" : Path.GetFileName(moved)) + ".";
                return false;
            }

            if (parsed == null)
            {
                failure = Path.GetFileName(path) + " deserialized to nothing.";
                return false;
            }

            if (parsed.SaveGuid == Guid.Empty)
            {
                // Recoverable: the directory name is the identity, and it is the same value.
                parsed.SaveGuid = expectedSaveGuid;
                _log.Warn("Sidecar: " + Path.GetFileName(path) +
                          " carried no save guid; adopting the one from its directory.");
            }
            else if (parsed.SaveGuid != expectedSaveGuid)
            {
                // Not recoverable, and not safe to paper over: every seeded stream starts from this
                // guid (non-negotiable #2), so loading it would run this city's politics on another
                // city's random sequence.
                failure = Path.GetFileName(path) + " belongs to save " +
                          SidecarPaths.FormatGuid(parsed.SaveGuid) + ", not " +
                          SidecarPaths.FormatGuid(expectedSaveGuid) + "; refusing to load it.";
                return false;
            }

            if (parsed.Settings == null) parsed.Settings = new AgoraSettings();

            state = parsed;
            return true;
        }

        /// <param name="areDefaults">
        /// True only on the last branch, where nothing on disk had anything to say. See
        /// <see cref="SidecarLoadResult.SettingsAreDefaults"/> for who asks and why.
        /// </param>
        private AgoraSettings ResolveSettings(PoliticalState state, string directory,
                                              List<string> warnings, out bool areDefaults)
        {
            areDefaults = false;

            if (state != null && state.Settings != null)
            {
                return state.Settings;
            }

            AgoraSettings fromFile = ReadDocument<AgoraSettings>(
                SidecarPaths.SettingsPath(directory), SidecarDocument.Settings, warnings);

            if (fromFile != null) return fromFile;

            // Defaults, not a crash: a save with no settings file is a save that has never been
            // through a full Agora session, and StartYear 1990 / Proportional is the documented
            // default set (§3).
            areDefaults = true;
            return new AgoraSettings();
        }

        // ---------------------------------------------------------------- save

        /// <summary>
        /// Writes the full political state plus its two mirrors, then prunes. Returns false and logs
        /// on any failure — a save that Agora could not write must not take the city's save with it.
        /// </summary>
        public bool SaveState(PoliticalState state)
        {
            try
            {
                if (state == null)
                {
                    _log.Error("Sidecar: refusing to write a null political state.", null);
                    return false;
                }

                if (state.SaveGuid == Guid.Empty)
                {
                    _log.Error("Sidecar: refusing to write political state with no save identity — " +
                               "it would be unreachable on the next load.", null);
                    return false;
                }

                string directory = DirectoryFor(state.SaveGuid);
                Directory.CreateDirectory(directory);

                state.SchemaVersion = SidecarSchema.CurrentStateVersion;
                if (state.Settings == null) state.Settings = new AgoraSettings();
                state.Settings.SchemaVersion = SidecarSchema.CurrentSettingsVersion;

                string statePath = SidecarPaths.StatePath(directory, state.Date);

                // Order matters. The state file is the authority; the two mirrors are conveniences
                // derived from it. Writing the authority first means an interrupted save leaves
                // stale mirrors beside good state, which the next load repairs, rather than fresh
                // mirrors beside stale state, which it would not notice.
                AtomicFile.WriteAllText(statePath, AgoraJson.Serialize(state));

                WriteSettings(directory, state.Settings);
                WriteTimelineProgress(directory, state.FiredEventIds);

                Prune(state.SaveGuid, state.Settings.SnapshotRetention, statePath);

                _log.Info("Sidecar: wrote " + Path.GetFileName(statePath) + " for save " +
                          SidecarPaths.FormatGuid(state.SaveGuid) + ".");
                return true;
            }
            catch (Exception ex)
            {
                _log.Error("Sidecar: could not write political state; the previous snapshot is intact.", ex);
                return false;
            }
        }

        /// <summary>Per-save settings (non-negotiable #10). Never global config.</summary>
        public bool SaveSettings(Guid saveGuid, AgoraSettings settings)
        {
            try
            {
                if (saveGuid == Guid.Empty || settings == null) return false;

                string directory = DirectoryFor(saveGuid);
                Directory.CreateDirectory(directory);
                WriteSettings(directory, settings);
                return true;
            }
            catch (Exception ex)
            {
                _log.Error("Sidecar: could not write settings.json.", ex);
                return false;
            }
        }

        public AgoraSettings LoadSettings(Guid saveGuid)
        {
            if (saveGuid == Guid.Empty) return new AgoraSettings();

            var warnings = new List<string>();
            AgoraSettings settings = ReadDocument<AgoraSettings>(
                SidecarPaths.SettingsPath(DirectoryFor(saveGuid)), SidecarDocument.Settings, warnings);

            return settings ?? new AgoraSettings();
        }

        /// <summary>Fired event ids, or an empty list. An event never fires twice.</summary>
        public List<string> LoadTimelineProgress(Guid saveGuid)
        {
            if (saveGuid == Guid.Empty) return new List<string>();

            var warnings = new List<string>();
            TimelineProgressFile file = ReadDocument<TimelineProgressFile>(
                SidecarPaths.TimelineProgressPath(DirectoryFor(saveGuid)),
                SidecarDocument.TimelineProgress, warnings);

            if (file == null || file.FiredEventIds == null) return new List<string>();

            return file.FiredEventIds;
        }

        /// <summary>
        /// Creates the save's sidecar directory if it does not exist and returns it.
        /// </summary>
        /// <remarks>
        /// This is the handoff for <c>flavor_cache.json</c>, the fourth file in §5's layout.
        /// <c>Agora.Mod/Llm/FileFlavorCache</c> owns that file end to end — it stores the <i>raw
        /// validated JSON</i> rather than a re-serialisation, so that the load path can re-run the
        /// same validator over the same bytes. Duplicating it here would put two writers, with two
        /// different notions of the wire format, on one path. So this packet supplies the directory
        /// and stays out of the file.
        /// </remarks>
        public string EnsureDirectory(Guid saveGuid)
        {
            if (saveGuid == Guid.Empty) return null;

            string directory = DirectoryFor(saveGuid);

            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception ex)
            {
                _log.Warn("Sidecar: could not create " + directory + ": " + ex.Message);
                return null;
            }

            return directory;
        }

        // ---------------------------------------------------------------- retention

        /// <summary>
        /// Keeps the newest <paramref name="retention"/> snapshots and deletes the rest. Returns how
        /// many were removed.
        /// </summary>
        /// <remarks>
        /// AGORA-SEAM(§14.3): the retention policy is an open decision — the proposal is a default of
        /// 25 and nothing cleverer than "keep the newest N". No thinning of old years, no keeping one
        /// per election cycle, no size budget. When §14.3 closes, this is the only method that
        /// changes.
        /// </remarks>
        public int Prune(Guid saveGuid, int retention)
        {
            return Prune(saveGuid, retention, null);
        }

        private int Prune(Guid saveGuid, int retention, string protectedPath)
        {
            int removed = 0;

            try
            {
                if (saveGuid == Guid.Empty) return 0;

                if (retention <= 0)
                {
                    _log.Warn("Sidecar: snapshot retention is " +
                              retention.ToString(CultureInfo.InvariantCulture) +
                              "; keeping everything rather than deleting the player's history.");
                    return 0;
                }

                string directory = DirectoryFor(saveGuid);
                List<StateFileRef> files = SidecarPaths.EnumerateStateFiles(directory);

                if (files.Count <= retention) return 0;

                int excess = files.Count - retention;

                // EnumerateStateFiles returns oldest first, so the excess is the head of the list.
                for (int i = 0; i < excess; i++)
                {
                    StateFileRef candidate = files[i];

                    if (protectedPath != null &&
                        string.Equals(candidate.Path, protectedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        File.Delete(candidate.Path);
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        _log.Warn("Sidecar: could not prune " + Path.GetFileName(candidate.Path) + ": " +
                                  ex.Message);
                    }
                }

                if (removed > 0)
                {
                    _log.Info("Sidecar: pruned " + removed.ToString(CultureInfo.InvariantCulture) +
                              " snapshot(s), keeping the newest " +
                              retention.ToString(CultureInfo.InvariantCulture) + ".");
                }
            }
            catch (Exception ex)
            {
                _log.Warn("Sidecar: pruning failed: " + ex.Message);
            }

            return removed;
        }

        // ---------------------------------------------------------------- helpers

        private void WriteSettings(string directory, AgoraSettings settings)
        {
            settings.SchemaVersion = SidecarSchema.CurrentSettingsVersion;
            AtomicFile.WriteAllText(SidecarPaths.SettingsPath(directory), AgoraJson.Serialize(settings));
        }

        private void WriteTimelineProgress(string directory, List<string> firedEventIds)
        {
            var file = new TimelineProgressFile
            {
                SchemaVersion = SidecarSchema.CurrentTimelineProgressVersion,
                FiredEventIds = firedEventIds ?? new List<string>()
            };

            AtomicFile.WriteAllText(SidecarPaths.TimelineProgressPath(directory), AgoraJson.Serialize(file));
        }

        /// <summary>
        /// Read + migrate + materialise one small document. Returns null on any failure, after
        /// logging — none of these files is load-bearing enough to fail a city load over.
        /// </summary>
        private T ReadDocument<T>(string path, SidecarDocument document, List<string> warnings) where T : class
        {
            string json;
            Exception readError;

            if (!AtomicFile.TryReadAllText(path, out json, out readError))
            {
                if (readError != null)
                {
                    string message = "Could not read " + Path.GetFileName(path) + ": " + readError.Message;
                    if (warnings != null) warnings.Add(message);
                    _log.Warn("Sidecar: " + message);
                }

                return null;
            }

            try
            {
                JObject root = AgoraJson.ParseObject(json);
                MigrationResult migration = SidecarSchema.Migrate(root, document);

                if (!migration.IsLoadable)
                {
                    string message = Path.GetFileName(path) + ": " + migration.Message;
                    if (warnings != null) warnings.Add(message);
                    _log.Warn("Sidecar: " + message);
                    return null;
                }

                return AgoraJson.ToObject<T>(root);
            }
            catch (Exception ex)
            {
                string moved = AtomicFile.Quarantine(path);
                string message = Path.GetFileName(path) + " was unreadable (" + ex.Message + "); moved to " +
                                 (moved == null ? "nowhere — it could not be renamed" : Path.GetFileName(moved)) +
                                 ".";

                if (warnings != null) warnings.Add(message);
                _log.Warn("Sidecar: " + message);
                return null;
            }
        }
    }
}
