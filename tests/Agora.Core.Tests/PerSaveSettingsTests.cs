using System;
using System.IO;
using Agora.Core.Contracts;
using Agora.Mod.Persistence;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Per-save settings on disk (non-negotiable #10) and the first-run signal derived from their
    /// absence (fixplan W3).
    ///
    /// <para>
    /// <see cref="SidecarStore.SaveSettings"/> had no caller anywhere in the repo until W3, and so no
    /// test either: settings reached disk only as a side effect of
    /// <see cref="SidecarStore.SaveState"/>, which runs when the player saves the game. A theme chosen
    /// and then lost to a crash would have come back as the other theme with a different set of
    /// parties — the worst possible way to lose one boolean's worth of choice. These are the first
    /// tests of that path.
    /// </para>
    /// </summary>
    public class PerSaveSettingsTests
    {
        private static readonly Guid Save = new Guid("c93a1e07-6b28-4d55-9f10-2e7c48a3b6d1");

        // --- the round trip --------------------------------------------------------------------------

        /// <summary>
        /// Every field, out and back. Field by field rather than by hash because the failure this
        /// guards is a property added to <see cref="AgoraSettings"/> and forgotten in the JSON schema
        /// or the migration, and the useful message is which one.
        /// </summary>
        [Fact]
        public void SaveSettings_RoundTripsEveryField()
        {
            string root = TempRoot("roundtrip");

            try
            {
                var store = new SidecarStore(root, NullSidecarLog.Instance);

                var written = new AgoraSettings
                {
                    StartYear = 1977,
                    Theme = RegionTheme.Na,
                    System = ElectoralSystem.FirstPastThePost,
                    WakeCadence = LlmWakeCadence.Election | LlmWakeCadence.Manual,
                    SnapshotRetention = 9,
                    Enabled = false,
                    EffectsEnabled = false,
                    ThemeLocked = true,
                    PauseOnMajorNews = false,
                    ShowAllReports = true
                };

                Assert.True(store.SaveSettings(Save, written));

                AgoraSettings read = store.LoadSettings(Save);

                Assert.Equal(1977, read.StartYear);
                Assert.Equal(RegionTheme.Na, read.Theme);
                Assert.Equal(ElectoralSystem.FirstPastThePost, read.System);
                Assert.Equal(LlmWakeCadence.Election | LlmWakeCadence.Manual, read.WakeCadence);
                Assert.Equal(9, read.SnapshotRetention);
                Assert.False(read.Enabled);
                Assert.False(read.EffectsEnabled);
                Assert.True(read.ThemeLocked);
                Assert.False(read.PauseOnMajorNews);
                Assert.True(read.ShowAllReports);
                Assert.Equal(SidecarSchema.CurrentSettingsVersion, read.SchemaVersion);
            }
            finally
            {
                Delete(root);
            }
        }

        /// <summary>
        /// The write is what W3 relies on to survive a crash, so it has to be a file rather than a
        /// promise, and writing it twice must not accumulate anything.
        /// </summary>
        [Fact]
        public void SaveSettings_WritesTheFileAndIsIdempotent()
        {
            string root = TempRoot("idempotent");

            try
            {
                var store = new SidecarStore(root, NullSidecarLog.Instance);
                var settings = new AgoraSettings { Theme = RegionTheme.Na };

                store.SaveSettings(Save, settings);
                string first = File.ReadAllText(SettingsPath(root));

                store.SaveSettings(Save, settings);
                string second = File.ReadAllText(SettingsPath(root));

                Assert.Equal(first, second);
            }
            finally
            {
                Delete(root);
            }
        }

        /// <summary>
        /// Refuses rather than throws. It is called from the UI update loop, where an exception costs
        /// far more than a setting that stays in memory for one more session.
        /// </summary>
        [Fact]
        public void SaveSettings_RefusesAnEmptyGuidWithoutThrowing()
        {
            string root = TempRoot("noguid");

            try
            {
                var store = new SidecarStore(root, NullSidecarLog.Instance);

                Assert.False(store.SaveSettings(Guid.Empty, new AgoraSettings()));
                Assert.False(store.SaveSettings(Save, null!));
            }
            finally
            {
                Delete(root);
            }
        }

        // --- the first-run signal --------------------------------------------------------------------

        /// <summary>
        /// An empty sidecar directory is the only state that means "this save has never chosen a
        /// theme". It is what the first-run dialog fires on.
        /// </summary>
        [Fact]
        public void Load_ReportsDefaultSettingsWhenNothingIsOnDisk()
        {
            string root = TempRoot("firstrun-empty");

            try
            {
                SidecarLoadResult result = new SidecarStore(root, NullSidecarLog.Instance)
                    .Load(Save, new SimDate(1990, 1, 1));

                Assert.False(result.HasState);
                Assert.True(result.SettingsAreDefaults);
            }
            finally
            {
                Delete(root);
            }
        }

        /// <summary>
        /// <b>The distinction the flag exists for.</b> A save that answered the prompt and was then
        /// closed before its first monthly tick has a <c>settings.json</c> and no state file. Asking
        /// only <c>HasState</c> would re-prompt that player and offer to discard their own choice —
        /// which, because a retheme regenerates the party registry, would actually do it.
        /// </summary>
        [Fact]
        public void Load_ReportsSettingsFromDiskEvenWithNoStateFile()
        {
            string root = TempRoot("firstrun-settingsonly");

            try
            {
                var store = new SidecarStore(root, NullSidecarLog.Instance);
                store.SaveSettings(Save, new AgoraSettings { Theme = RegionTheme.Na });

                SidecarLoadResult result = store.Load(Save, new SimDate(1990, 1, 1));

                Assert.False(result.HasState);
                Assert.False(result.SettingsAreDefaults);
                Assert.Equal(RegionTheme.Na, result.Settings.Theme);
            }
            finally
            {
                Delete(root);
            }
        }

        /// <summary>
        /// A state file carries its own settings block, and that block is the authority — so it is not
        /// a default set either, whatever it happens to contain.
        /// </summary>
        [Fact]
        public void Load_ReportsSettingsFromTheStateFile()
        {
            string root = TempRoot("firstrun-state");

            try
            {
                var store = new SidecarStore(root, NullSidecarLog.Instance);
                var date = new SimDate(1990, 1, 1);

                Assert.True(store.SaveState(new PoliticalState
                {
                    SaveGuid = Save,
                    Date = date,
                    Settings = new AgoraSettings { Theme = RegionTheme.Na }
                }));

                SidecarLoadResult result = store.Load(Save, date);

                Assert.True(result.HasState);
                Assert.False(result.SettingsAreDefaults);
                Assert.Equal(RegionTheme.Na, result.Settings.Theme);
            }
            finally
            {
                Delete(root);
            }
        }

        /// <summary>
        /// No identity yet means no directory to read, which is as first-run as it gets. Guarded
        /// because it is a separate early return in <see cref="SidecarStore.Load"/> and a flag set on
        /// only one of the two paths is a flag that is wrong half the time.
        /// </summary>
        [Fact]
        public void Load_ReportsDefaultSettingsWhenThereIsNoSaveIdentity()
        {
            string root = TempRoot("firstrun-noguid");

            try
            {
                SidecarLoadResult result = new SidecarStore(root, NullSidecarLog.Instance)
                    .Load(Guid.Empty, new SimDate(1990, 1, 1));

                Assert.True(result.SettingsAreDefaults);
            }
            finally
            {
                Delete(root);
            }
        }

        // --- temp directories ------------------------------------------------------------------------

        private static string SettingsPath(string root) =>
            SidecarPaths.SettingsPath(Path.Combine(root, SidecarPaths.FormatGuid(Save)));

        private static string TempRoot(string name)
        {
            string path = Path.Combine(Path.GetTempPath(), "agora-per-save-settings-tests", name);
            Delete(path);
            Directory.CreateDirectory(path);
            return path;
        }

        private static void Delete(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not a test failure.
            }
        }
    }
}
