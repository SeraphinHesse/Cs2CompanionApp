// Requires the Persistence <Compile Link> lines in Agora.Core.Tests.csproj — SidecarSchema and the
// five files it loads beside. See the comment there for why they are linked rather than referenced.

using System;
using System.IO;
using Agora.Core.Contracts;
using Agora.Mod.Persistence;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The sidecar state migrations: v1 → v2 adds <c>parties[].playerOverrides</c> and the three
    /// per-save UI settings; v2 → v3 adds <c>parties[].isMajor</c> and the <c>fringe</c> watch.
    ///
    /// <para>
    /// <c>/schema-change</c> step 5 — <i>an untested migration is a guess</i> — is the reason this
    /// file exists, and non-negotiable #6 is what it is guarding. "Load must never desync" does not
    /// mean the migration leaves the fingerprint alone; bumping a version necessarily changes it
    /// once, which is what a version bump <i>is</i>. What must hold afterwards is that migrating a
    /// migrated document changes nothing further, so the fingerprint at sim date D is the same on
    /// every subsequent reload. Every helper in the step only fills properties that are absent, and
    /// the idempotency test below is what keeps it that way.
    /// </para>
    ///
    /// <para>
    /// The fixtures are hand-authored v1 JSON rather than a serialized <c>PoliticalState</c> with
    /// the new properties deleted. A fixture built from the current contract stops being a v1
    /// document the moment the contract moves again, and the migration it is meant to exercise
    /// quietly becomes a no-op no test would notice.
    /// </para>
    /// </summary>
    public class SidecarMigrationTests
    {
        private static readonly Guid Save = new Guid("11112222-3333-4444-5555-666677778888");

        // --- Fixtures ----------------------------------------------------------------------------

        /// <summary>
        /// A v1 state document. <paramref name="versionLine"/> lets a caller drop or alter the root
        /// <c>schemaVersion</c> without re-typing the rest; <paramref name="elections"/> is the
        /// <c>electionHistory</c> array, which the theme lock reads;
        /// <paramref name="withSettings"/> false omits the nested <c>settings</c> block entirely,
        /// which is the branch that synthesises one.
        /// </summary>
        private static string StateV1(string versionLine = "\"schemaVersion\": 1,",
                                      string elections = "[]",
                                      string? parties = null,
                                      bool withSettings = true)
        {
            if (parties == null)
            {
                parties = "[" +
                    PartyV1("party-01", "Green Alliance") + "," +
                    PartyV1("party-02", "Civic Union") + "," +
                    PartyV1("party-03", "Harbour Labour") +
                "]";
            }

            return "{" +
                versionLine +
                "\"saveGuid\": \"11112222-3333-4444-5555-666677778888\"," +
                "\"date\": \"1994-03-01\"," +
                (withSettings ? "\"settings\": " + SettingsV1() + "," : "") +
                "\"parties\": " + parties + "," +
                "\"factions\": []," +
                "\"electionHistory\": " + elections + "," +
                "\"firedEventIds\": [\"eu-1992-maastricht\"]," +
                "\"termNumber\": 2," +
                "\"isCampaignSeason\": false" +
            "}";
        }

        private static string PartyV1(string id, string name, string? overrides = null)
        {
            return "{" +
                "\"id\": \"" + id + "\"," +
                "\"name\": \"" + name + "\"," +
                "\"shortName\": \"" + id + "\"," +
                "\"colorHex\": \"#3366aa\"," +
                "\"status\": \"Active\"," +
                "\"foundedDate\": \"1990-01-01\"," +
                "\"revivalCount\": 0" +
                (overrides == null ? "" : ",\"playerOverrides\": \"" + overrides + "\"") +
            "}";
        }

        private static string SettingsV1()
        {
            return "{" +
                "\"schemaVersion\": 1," +
                "\"startYear\": 1990," +
                "\"theme\": \"Eu\"," +
                "\"system\": \"Proportional\"," +
                "\"wakeCadence\": \"Yearly, Election, Manual\"," +
                "\"snapshotRetention\": 25," +
                "\"enabled\": true," +
                "\"effectsEnabled\": true" +
            "}";
        }

        /// <summary>One election, enough for the theme lock to see that the save is past its first.</summary>
        private static string OneElection()
        {
            return "[{" +
                "\"schemaVersion\": 1," +
                "\"id\": \"election-01\"," +
                "\"date\": \"1993-05-12\"," +
                "\"system\": \"Proportional\"" +
            "}]";
        }

        private static JObject Migrate(string json, out MigrationResult result)
        {
            JObject root = AgoraJson.ParseObject(json);
            result = SidecarSchema.Migrate(root, SidecarDocument.State);
            return root;
        }

        // --- 1. The fall-through -------------------------------------------------------------------

        /// <summary>
        /// The defect this pass had to fix before it could add a step at all: <c>Migrate</c> used to
        /// stamp the <i>target</i> version on an unversioned document and return without running one.
        /// Harmless while every step table was empty, and silent unrepairable data loss the moment
        /// one was not — the file would claim to be current while carrying v1 content, so nothing
        /// afterwards could tell it apart from a document this build had written itself.
        /// </summary>
        [Fact]
        public void Migrate_StampsAbsentVersionAndStillRunsEveryStep()
        {
            MigrationResult result;
            JObject root = Migrate(StateV1(versionLine: ""), out result);

            Assert.Equal(MigrationOutcome.AssumedVersionOne, result.Outcome);
            Assert.True(result.IsLoadable);
            Assert.Equal(SidecarSchema.CurrentStateVersion, Int(root, SidecarSchema.VersionProperty));

            foreach (JToken party in Arr(root, "parties"))
            {
                Assert.Equal("None", Text(party, "playerOverrides"));
            }

            Assert.True(Bool(Obj(root, "settings"), "pauseOnMajorNews"));
        }

        // --- 2-4. What the step writes ---------------------------------------------------------------

        [Fact]
        public void Migrate_StateV1_AddsPlayerOverridesToEveryParty()
        {
            string parties = "[" +
                PartyV1("party-01", "Green Alliance") + "," +
                PartyV1("party-02", "Civic Union") + "," +
                PartyV1("party-03", "Harbour Labour") + "," +
                PartyV1("party-04", "Riverside Independents", "NameLocked") +
            "]";

            MigrationResult result;
            JObject root = Migrate(StateV1(parties: parties), out result);

            Assert.Equal(MigrationOutcome.Upgraded, result.Outcome);

            JArray migrated = Arr(root, "parties");
            Assert.Equal("None", Text(migrated[0], "playerOverrides"));
            Assert.Equal("None", Text(migrated[1], "playerOverrides"));
            Assert.Equal("None", Text(migrated[2], "playerOverrides"));

            // A lock the player already set is left exactly as it was.
            Assert.Equal("NameLocked", Text(migrated[3], "playerOverrides"));
        }

        [Fact]
        public void Migrate_StateV1_AddsSettingsFieldsWithTheDocumentedDefaults()
        {
            MigrationResult result;
            JObject settings = Obj(Migrate(StateV1(), out result), "settings");

            Assert.False(Bool(settings, "themeLocked"));
            Assert.True(Bool(settings, "pauseOnMajorNews"));
            Assert.False(Bool(settings, "showAllReports"));

            // The nested block carries its own version, and the root chain is the only thing that
            // will ever reach it.
            Assert.Equal(SidecarSchema.CurrentSettingsVersion, Int(settings, SidecarSchema.VersionProperty));
        }

        /// <summary>
        /// The other branch of the step: a state file with no <c>settings</c> block at all, where
        /// the migration writes a whole default object out of hand-typed JSON literals.
        ///
        /// <para>
        /// It has to be materialised, not inspected. Three of those literals — <c>"Eu"</c>,
        /// <c>"Proportional"</c>, <c>"Yearly, Election, Manual"</c> — only mean anything once
        /// <c>StringEnumConverter</c> has had a go at them, and a wrong member name is not a wrong
        /// value but a thrown <c>JsonSerializationException</c> out of <c>ToObject</c>. In
        /// <c>SidecarStore</c> that throw quarantines the state file: the player's political history
        /// is moved aside, not recovered. Asserting on the DOM would agree with the typo. (Case is
        /// the one thing this does not police — Newtonsoft matches enum members case-insensitively,
        /// so <c>"eu"</c> would survive both here and in the real loader.)
        /// </para>
        /// </summary>
        [Fact]
        public void Migrate_StateV1_SynthesisesASettingsBlockThatMaterialisesAsTheDefaults()
        {
            MigrationResult result;
            JObject root = Migrate(StateV1(withSettings: false), out result);

            Assert.Equal(MigrationOutcome.Upgraded, result.Outcome);

            AgoraSettings actual = AgoraJson.ToObject<PoliticalState>(root).Settings;
            var expected = new AgoraSettings();

            Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
            Assert.Equal(expected.StartYear, actual.StartYear);
            Assert.Equal(expected.Theme, actual.Theme);
            Assert.Equal(expected.System, actual.System);
            Assert.Equal(expected.WakeCadence, actual.WakeCadence);
            Assert.Equal(expected.SnapshotRetention, actual.SnapshotRetention);
            Assert.Equal(expected.Enabled, actual.Enabled);
            Assert.Equal(expected.EffectsEnabled, actual.EffectsEnabled);
            Assert.Equal(expected.ThemeLocked, actual.ThemeLocked);
            Assert.Equal(expected.PauseOnMajorNews, actual.PauseOnMajorNews);
            Assert.Equal(expected.ShowAllReports, actual.ShowAllReports);
        }

        /// <summary>
        /// The theme locks at the first election, so a save that has already held one is past that
        /// point and must not be offered the choice again on the next load.
        /// </summary>
        [Fact]
        public void Migrate_StateV1_LocksTheThemeWhenAnElectionHasBeenHeld()
        {
            MigrationResult ignored;
            JObject never = Migrate(StateV1(elections: "[]"), out ignored);
            JObject held = Migrate(StateV1(elections: OneElection()), out ignored);

            Assert.False(Bool(Obj(never, "settings"), "themeLocked"));
            Assert.True(Bool(Obj(held, "settings"), "themeLocked"));
        }

        // --- 5. And nothing else ---------------------------------------------------------------------

        /// <summary>
        /// <c>/schema-change</c> step 2's "never silently drop a field", asserted by deep comparison
        /// rather than by naming the fields that should have survived — a hand-written assertion
        /// only covers the fields whoever wrote it thought of.
        /// </summary>
        [Fact]
        public void Migrate_StateV1_ChangesNothingElse()
        {
            string json = StateV1(elections: OneElection());

            MigrationResult result;
            JObject after = Migrate(json, out result);
            JObject before = AgoraJson.ParseObject(json);

            Strip(before);
            Strip(after);

            Assert.True(JToken.DeepEquals(before, after),
                        "Migration changed something outside the six paths it is allowed to touch.");
        }

        /// <summary>Removes the paths v1 → v2 is permitted to change, from either side.</summary>
        private static void Strip(JObject root)
        {
            root.Remove(SidecarSchema.VersionProperty);
            root.Remove("fringe");

            JObject settings = Obj(root, "settings");
            settings.Remove(SidecarSchema.VersionProperty);
            settings.Remove("themeLocked");
            settings.Remove("pauseOnMajorNews");
            settings.Remove("showAllReports");

            foreach (JToken party in Arr(root, "parties"))
            {
                ((JObject)party).Remove("playerOverrides");
                ((JObject)party).Remove("isMajor");
            }
        }

        // --- 5b. v2 → v3: isMajor and the fringe watch -------------------------------------------

        /// <summary>
        /// A v2 state file at the version the previous step produced. Parties are given in a
        /// deliberately scrambled array order, because the migration must reconstruct majors from id
        /// order and not from however the writer happened to emit the list.
        /// </summary>
        private static string StateV2(string theme, string parties)
        {
            return "{" +
                "\"schemaVersion\": 2," +
                "\"saveGuid\": \"11112222-3333-4444-5555-666677778888\"," +
                "\"date\": \"1994-03-01\"," +
                "\"settings\": {" +
                    "\"schemaVersion\": 2," +
                    "\"startYear\": 1990," +
                    "\"theme\": \"" + theme + "\"," +
                    "\"system\": \"" + (theme == "Na" ? "FirstPastThePost" : "Proportional") + "\"," +
                    "\"wakeCadence\": \"Yearly, Election, Manual\"," +
                    "\"snapshotRetention\": 25," +
                    "\"enabled\": true," +
                    "\"effectsEnabled\": true," +
                    "\"themeLocked\": true," +
                    "\"pauseOnMajorNews\": false," +
                    "\"showAllReports\": false" +
                "}," +
                "\"parties\": " + parties + "," +
                "\"factions\": []," +
                "\"electionHistory\": []," +
                "\"firedEventIds\": []," +
                "\"termNumber\": 2," +
                "\"isCampaignSeason\": false" +
            "}";
        }

        private static string PartyV2(string id, string status = "Active")
        {
            return "{" +
                "\"id\": \"" + id + "\"," +
                "\"name\": \"\"," +
                "\"shortName\": \"" + id + "\"," +
                "\"colorHex\": \"#3366aa\"," +
                "\"status\": \"" + status + "\"," +
                "\"foundedDate\": \"1990-01-01\"," +
                "\"revivalCount\": 0," +
                "\"playerOverrides\": \"None\"" +
            "}";
        }

        private static bool IsMajor(JObject root, string partyId)
        {
            foreach (JToken token in Arr(root, "parties"))
            {
                var party = (JObject)token;
                if ((string?)party["id"] == partyId) return Bool(party, "isMajor");
            }

            throw new Xunit.Sdk.XunitException("no party " + partyId + " in the migrated document");
        }

        /// <summary>
        /// The reconstruction that matters. Defaulting <c>isMajor</c> to false would tell the fringe
        /// ceiling that an existing NA save has no majors at all, and the ceiling would then pin every
        /// party on the ballot to 3% — so the migration has to work out which two they were.
        /// </summary>
        [Fact]
        public void Migrate_StateV2_MarksTheTwoLowestNaPartyIdsAsMajors()
        {
            MigrationResult result;
            JObject root = Migrate(StateV2("Na", "[" +
                PartyV2("party-04") + "," + PartyV2("party-01") + "," +
                PartyV2("party-03") + "," + PartyV2("party-02") + "]"), out result);

            Assert.Equal(MigrationOutcome.Upgraded, result.Outcome);
            Assert.True(IsMajor(root, "party-01"));
            Assert.True(IsMajor(root, "party-02"));

            // Anything past the prefix is a splinter or an entrant, and neither is a major.
            Assert.False(IsMajor(root, "party-03"));
            Assert.False(IsMajor(root, "party-04"));
        }

        /// <summary>
        /// A dead brand must not consume a major slot that belongs to a live party — the same reason
        /// <c>NextPartyId</c> counts past dissolved ids.
        /// </summary>
        [Fact]
        public void Migrate_StateV2_SkipsDeadBrandsWhenPickingTheMajors()
        {
            MigrationResult result;
            JObject root = Migrate(StateV2("Na", "[" +
                PartyV2("party-01", "Dissolved") + "," + PartyV2("party-02") + "," +
                PartyV2("party-03") + "," + PartyV2("party-04", "Merged") + "]"), out result);

            Assert.Equal(MigrationOutcome.Upgraded, result.Outcome);
            Assert.False(IsMajor(root, "party-01"));
            Assert.True(IsMajor(root, "party-02"));
            Assert.True(IsMajor(root, "party-03"));
            Assert.False(IsMajor(root, "party-04"));
        }

        /// <summary>EU has no majors at all, and the flag stays false rather than true (§3).</summary>
        [Fact]
        public void Migrate_StateV2_LeavesEveryEuPartyMinor()
        {
            MigrationResult result;
            JObject root = Migrate(StateV2("Eu", "[" +
                PartyV2("party-01") + "," + PartyV2("party-02") + "," + PartyV2("party-03") + "]"), out result);

            Assert.Equal(MigrationOutcome.Upgraded, result.Outcome);
            Assert.False(IsMajor(root, "party-01"));
            Assert.False(IsMajor(root, "party-02"));
            Assert.False(IsMajor(root, "party-03"));
        }

        /// <summary>
        /// The watch arrives zeroed. Inventing a streak here would hand an existing save an unearned
        /// fringe surge on its very next tick.
        /// </summary>
        [Fact]
        public void Migrate_StateV2_AddsAZeroedFringeWatch()
        {
            MigrationResult ignored;
            JObject root = Migrate(StateV2("Na", "[" + PartyV2("party-01") + "]"), out ignored);

            JObject fringe = Obj(root, "fringe");
            Assert.Equal(0, (int)fringe["consecutiveFailureTerms"]!);
            Assert.Equal(0, (int)fringe["lastClosedTermNumber"]!);
            Assert.Equal(0.0, (double)fringe["lastTermFailureScore"]!);
            Assert.Equal(0, (int)fringe["monthsObserved"]!);
            Assert.Equal(0.0, (double)fringe["discontentSum"]!);
            Assert.Equal(0.0, (double)fringe["defianceSurgeSum"]!);
            Assert.Equal(0, (int)fringe["governmentChanges"]!);
            Assert.Equal(0, (int)fringe["mayorChanges"]!);
        }

        /// <summary>A v1 file walks the whole chain, not just the step it was written for.</summary>
        [Fact]
        public void Migrate_StateV1_ReachesVersionThree()
        {
            MigrationResult result;
            JObject root = Migrate(StateV1(elections: OneElection()), out result);

            Assert.Equal(MigrationOutcome.Upgraded, result.Outcome);
            Assert.Equal(SidecarSchema.CurrentStateVersion, (int)root[SidecarSchema.VersionProperty]!);
            Assert.NotNull(root["fringe"]);
        }

        // --- 6. Idempotency ---------------------------------------------------------------------------

        /// <summary>
        /// The operational form of non-negotiable #6. The upgrade moves the serialized fingerprint
        /// exactly once; every reload after that must produce the same bytes, or the desync check in
        /// tests/CLAUDE.md fires on a file that is in fact perfectly healthy.
        /// </summary>
        [Fact]
        public void Migrate_IsIdempotent()
        {
            MigrationResult first;
            JObject root = Migrate(StateV1(elections: OneElection()), out first);
            string once = AgoraJson.Serialize(root);

            MigrationResult second = SidecarSchema.Migrate(root, SidecarDocument.State);

            Assert.Equal(MigrationOutcome.Current, second.Outcome);
            Assert.Equal(once, AgoraJson.Serialize(root));
        }

        // --- 7. Refusal --------------------------------------------------------------------------------

        /// <summary>
        /// A file from a newer build is declined, not guessed at — and declined without touching the
        /// DOM, because <c>SidecarStore</c> deliberately does not quarantine such a file and the
        /// player may well be about to reinstall the build that wrote it.
        /// </summary>
        [Fact]
        public void Migrate_RefusesAStateFileFromTheFuture()
        {
            string json = StateV1(versionLine: "\"schemaVersion\": 4,");

            MigrationResult result;
            JObject root = Migrate(json, out result);

            Assert.Equal(MigrationOutcome.TooNew, result.Outcome);
            Assert.False(result.IsLoadable);
            Assert.Equal(AgoraJson.Serialize(AgoraJson.ParseObject(json)), AgoraJson.Serialize(root));
        }

        // --- 8. The standalone settings file -------------------------------------------------------------

        /// <summary>
        /// <c>settings.json</c> migrates on its own for the save that has no state file yet. It
        /// cannot see election history, so it leaves <c>themeLocked</c> false and the runtime
        /// re-locks at the next election check.
        /// </summary>
        [Fact]
        public void Migrate_SettingsFileV1_UpgradesStandalone()
        {
            JObject root = AgoraJson.ParseObject(SettingsV1());
            MigrationResult result = SidecarSchema.Migrate(root, SidecarDocument.Settings);

            Assert.Equal(MigrationOutcome.Upgraded, result.Outcome);
            Assert.Equal(SidecarSchema.CurrentSettingsVersion, Int(root, SidecarSchema.VersionProperty));

            Assert.False(Bool(root, "themeLocked"));
            Assert.True(Bool(root, "pauseOnMajorNews"));
            Assert.False(Bool(root, "showAllReports"));
        }

        // --- 9. The round trip ------------------------------------------------------------------------

        /// <summary>
        /// The whole path a player's thirty-year save takes: a v1 file on disk, read, migrated,
        /// materialised into the current contract, written back, and read again. A step that is
        /// correct against a fixture but wrong in that sequence is exactly what a fixture-only test
        /// cannot see.
        /// </summary>
        [Fact]
        public void SidecarStore_RoundTripsAnOldVersionStateFile()
        {
            string root = TempRoot("roundtrip");

            try
            {
                string directory = Path.Combine(root, SidecarPaths.FormatGuid(Save));
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "state_1994_03.json"), StateV1());

                var store = new SidecarStore(root, NullSidecarLog.Instance);
                var date = new SimDate(1994, 3, 1);

                SidecarLoadResult loaded = store.Load(Save, date);

                Assert.True(loaded.HasState);
                Assert.Empty(loaded.Warnings);
                Assert.True(loaded.Settings.PauseOnMajorNews);
                Assert.False(loaded.Settings.ShowAllReports);
                Assert.False(loaded.Settings.ThemeLocked);
                Assert.Equal(PartyOverrides.None, loaded.State.Parties[0].PlayerOverrides);

                // The point of the whole exercise: the party's name survives the version bump. A
                // migration that discarded it would show up in game as "party-01" on the seat chart.
                Assert.Equal("Green Alliance", loaded.State.Parties[0].Name);

                Assert.True(store.SaveState(loaded.State));

                SidecarLoadResult again = store.Load(Save, date);

                Assert.Equal(AgoraJson.Fingerprint(loaded.State), AgoraJson.Fingerprint(again.State));
            }
            finally
            {
                Delete(root);
            }
        }

        // --- 10. The wire form -------------------------------------------------------------------------

        /// <summary>
        /// Guards the shape <c>political_state.schema.json</c> declares for <c>playerOverrides</c>:
        /// a comma-separated list of Pascal-cased member names, because <c>AgoraJson</c> adds
        /// <c>StringEnumConverter</c> with no naming strategy.
        /// </summary>
        [Fact]
        public void Party_PlayerOverrides_SerializesAsMemberNames()
        {
            var party = new Party
            {
                Id = "party-01",
                PlayerOverrides = PartyOverrides.NameLocked | PartyOverrides.ColorLocked
            };

            Assert.Equal("NameLocked, ColorLocked",
                         Text(AgoraJson.ParseObject(AgoraJson.Serialize(party)), "playerOverrides"));

            Assert.Equal("None",
                         Text(AgoraJson.ParseObject(AgoraJson.Serialize(new Party { Id = "party-02" })),
                              "playerOverrides"));

            Party back = AgoraJson.Deserialize<Party>(AgoraJson.Serialize(party));
            Assert.Equal(PartyOverrides.NameLocked | PartyOverrides.ColorLocked, back.PlayerOverrides);
        }

        // --- DOM accessors -----------------------------------------------------------------------------

        // Asserting on the way through rather than annotating every call site: an absent property is
        // a migration failure, and it should read as one rather than as a null dereference.

        private static JToken Get(JToken parent, string name)
        {
            JToken? token = parent[name];
            Assert.True(token != null, "Expected a '" + name + "' property.");
            return token!;
        }

        private static JObject Obj(JToken parent, string name)
        {
            return Assert.IsType<JObject>(Get(parent, name));
        }

        private static JArray Arr(JToken parent, string name)
        {
            return Assert.IsType<JArray>(Get(parent, name));
        }

        private static string Text(JToken parent, string name)
        {
            return Get(parent, name).Value<string>() ?? "";
        }

        private static int Int(JToken parent, string name)
        {
            return Get(parent, name).Value<int>();
        }

        private static bool Bool(JToken parent, string name)
        {
            return Get(parent, name).Value<bool>();
        }

        // --- Temp directories --------------------------------------------------------------------------

        private static string TempRoot(string name)
        {
            string path = Path.Combine(Path.GetTempPath(), "agora-sidecar-migration-tests", name);
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
                // A leftover temp directory is not worth failing a test over.
            }
        }
    }
}
