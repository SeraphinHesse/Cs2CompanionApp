// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using Agora.Core.Engine.Parties;
using Newtonsoft.Json.Linq;

namespace Agora.Mod.Persistence
{
    /// <summary>Which of the five sidecar documents a version number belongs to.</summary>
    public enum SidecarDocument
    {
        State = 0,
        Settings = 1,
        TimelineProgress = 2,
        FlavorCache = 3,
        MetricHistory = 4
    }

    /// <summary>What happened when a document's <c>schemaVersion</c> was reconciled with this build.</summary>
    public enum MigrationOutcome
    {
        /// <summary>Already at the current version. Nothing was changed.</summary>
        Current = 0,

        /// <summary>Older, and every step from its version to the current one was available.</summary>
        Upgraded = 1,

        /// <summary>
        /// No <c>schemaVersion</c> at all. Treated as version 1 — the version that shipped before the
        /// field was mandatory — and stamped, so the next save records it properly.
        /// </summary>
        AssumedVersionOne = 2,

        /// <summary>
        /// Written by a newer Agora than this one. Refused: a forward migration cannot be invented,
        /// and guessing would silently drop whatever the newer build added.
        /// </summary>
        TooNew = 3,

        /// <summary>Older than any registered migration step can reach. Refused, file left intact.</summary>
        NoPathForward = 4,

        /// <summary>The document was malformed enough that the version could not be read.</summary>
        Unreadable = 5
    }

    /// <summary>Outcome plus a log-ready explanation. The document is migrated in place.</summary>
    public sealed class MigrationResult
    {
        public MigrationResult(MigrationOutcome outcome, int fromVersion, int toVersion, string message)
        {
            Outcome = outcome;
            FromVersion = fromVersion;
            ToVersion = toVersion;
            Message = message;
        }

        public MigrationOutcome Outcome { get; private set; }
        public int FromVersion { get; private set; }
        public int ToVersion { get; private set; }
        public string Message { get; private set; }

        /// <summary>True when the migrated document may be materialised into a contract type.</summary>
        public bool IsLoadable
        {
            get
            {
                return Outcome == MigrationOutcome.Current
                    || Outcome == MigrationOutcome.Upgraded
                    || Outcome == MigrationOutcome.AssumedVersionOne;
            }
        }
    }

    /// <summary>
    /// Schema versions for the sidecar, and the migrations between them.
    ///
    /// <para>
    /// Non-negotiable #9 puts a <c>schemaVersion</c> on every contract; <c>/schema-change</c> step 2
    /// says what has to happen when one moves: <i>"Loading an older version must upgrade in memory
    /// and continue — never reset politics, never crash, never silently drop a field. Someone has a
    /// thirty-year save; it has to keep working."</i>
    /// </para>
    ///
    /// <para>
    /// So migration is explicit and registered, not implicit. Each step is a function that rewrites
    /// the JSON DOM from version N to version N+1; the chain runs before the document is allowed to
    /// become a contract object. Relying on Newtonsoft's tolerance of unknown and missing fields is
    /// not a migration — it is a silent default, and a silent default is how a thirty-year save
    /// quietly loses its coalition history.
    /// </para>
    ///
    /// <para>
    /// State is at version 6; settings is at 4 and the flavor cache at 2; timeline progress and the
    /// metric history are still at 1, so their tables are empty. So is the flavor cache's, which is
    /// not an omission: nothing routes
    /// <c>flavor_cache.json</c> through <see cref="Migrate"/> at all — <c>Agora.Mod/Llm</c> upgrades
    /// it in <c>FlavorCacheMigration</c> and validates it against
    /// <c>FlavorSchema.SupportedSchemaVersion</c>, and <see cref="CurrentFlavorCacheVersion"/> says
    /// so at length. The refusals matter as much as the steps: a document from a newer
    /// build is declined rather than guessed at, and an unversioned one is assumed to be version 1
    /// — and then actually migrated, which is the part that is easy to get wrong. Stamping the
    /// target version on an unversioned document without running the chain labels a v1 file as
    /// current and makes it unrepairable, because nothing afterwards can tell it apart from a file
    /// that was genuinely written by this build.
    /// </para>
    ///
    /// <para>
    /// <b>Adding a version.</b> Bump the constant, add a <see cref="MigrationStep"/> from the old
    /// number to the new one, update <c>data/schemas/</c>, and add a fixture at the old version —
    /// <c>/schema-change</c> step 5: an untested migration is a guess.
    /// </para>
    /// </summary>
    public static class SidecarSchema
    {
        public const string VersionProperty = "schemaVersion";

        /// <summary>
        /// Kept in step with <c>Agora.Core.Contracts.PoliticalState.SchemaVersion</c>'s own default,
        /// which cannot reference this constant because Core may not see Mod. The two drifted once —
        /// the default sat at 3 while this was 4 — and a freshly constructed state consequently
        /// claimed a version it had never been. <c>SidecarMigrationTests</c> pins them together.
        /// </summary>
        public const int CurrentStateVersion = 9;

        public const int CurrentSettingsVersion = 7;

        /// <summary><c>timeline_progress.json</c> has not moved; it is still a list of fired ids.</summary>
        public const int CurrentTimelineProgressVersion = 1;

        /// <summary>
        /// <c>metric_history.json</c>, the sensor layer's rent and land-value memory. New in this
        /// build, so there is no v0 to migrate from and <see cref="MetricHistorySteps"/> is empty.
        /// </summary>
        /// <remarks>
        /// An absent file is the normal case for every save that predates it, and it is handled
        /// without a migration step: <c>SidecarStore.LoadMetricHistory</c> returns null, the sensor
        /// starts with no samples, and the first year of play refills it. That is the same position
        /// every save was already in on every load, so this is a floor that rises rather than a
        /// behaviour change that needs unwinding.
        /// </remarks>
        public const int CurrentMetricHistoryVersion = 1;

        /// <summary>
        /// Not consulted by anything. <c>flavor_cache.json</c> is read by <c>Agora.Mod/Llm</c>, which
        /// validates it against <c>FlavorSchema.SupportedSchemaVersion</c> and upgrades it through
        /// <c>FlavorCacheMigration</c> rather than routing it through <see cref="Migrate"/> — so two
        /// constants version one file and only the other one is read. The honest fix is to delete
        /// this and <see cref="SidecarDocument.FlavorCache"/>, which is a follow-up rather than a
        /// mid-migration edit.
        ///
        /// <para>
        /// Kept in step with <c>FlavorSchema.SupportedSchemaVersion</c> and
        /// <c>data/schemas/politics_flavor.schema.json</c>, never ahead of them: a reader who trusts
        /// this constant must not be told a version the real authority has not reached.
        /// </para>
        ///
        /// <para>
        /// <see cref="FlavorCacheSteps"/> stays empty, and that is safe only because no caller passes
        /// <see cref="SidecarDocument.FlavorCache"/> to <see cref="Migrate"/> — verified by a
        /// repo-wide grep for <c>SidecarDocument.FlavorCache</c>, which finds this file and nothing
        /// else. Whoever wires the flavor cache through <see cref="Migrate"/> must add a 1 -> 2 step
        /// first: without one the loop returns <see cref="MigrationOutcome.NoPathForward"/> on every
        /// existing cache and <c>IsLoadable</c> goes false, which surfaces as a load failure rather
        /// than as an obviously missing step.
        /// </para>
        /// </summary>
        public const int CurrentFlavorCacheVersion = 3;

        /// <summary>One in-place rewrite, from <see cref="FromVersion"/> to <c>FromVersion + 1</c>.</summary>
        public sealed class MigrationStep
        {
            public MigrationStep(int fromVersion, string description, Action<JObject> apply)
            {
                FromVersion = fromVersion;
                Description = description;
                Apply = apply;
            }

            public int FromVersion { get; private set; }
            public string Description { get; private set; }
            public Action<JObject> Apply { get; private set; }
        }

        /// <summary>
        /// Brings one settings object — standalone <c>settings.json</c> or the block nested in a
        /// state file — from v1 to v2. Idempotent: a property already present is left alone, so
        /// running it twice cannot change a value the player set.
        /// </summary>
        internal static void UpgradeSettingsObjectToV2(JObject settings)
        {
            if (settings == null) return;

            if (settings["themeLocked"] == null) settings["themeLocked"] = false;
            if (settings["pauseOnMajorNews"] == null) settings["pauseOnMajorNews"] = true;
            if (settings["showAllReports"] == null) settings["showAllReports"] = false;

            settings[VersionProperty] = 2;
        }

        /// <summary>
        /// Brings one settings object from v2 to v3: the three voter-model levels.
        ///
        /// <para>
        /// All three default to <c>Default</c>, which is not a value but an instruction to leave the
        /// tuning file alone (see <c>Agora.Core.Tuning.TuningPresets</c>). That is what makes this
        /// migration behaviour-preserving: a save upgraded here runs on exactly the coefficients it
        /// ran on before, and would do so even if those coefficients were retuned again tomorrow.
        /// </para>
        /// </summary>
        /// <remarks>
        /// Written as enum <i>names</i> rather than ordinals. A save file is read by humans when
        /// something goes wrong, and an ordinal silently means something different the moment a
        /// member is inserted above it.
        /// </remarks>
        internal static void UpgradeSettingsObjectToV3(JObject settings)
        {
            if (settings == null) return;

            // v1 saves reach here through the step table, which runs v1→v2 first; a v2 save arrives
            // with these absent. Either way, absent means "never chosen".
            if (settings["voteSharpness"] == null) settings["voteSharpness"] = "Default";
            if (settings["newsInfluence"] == null) settings["newsInfluence"] = "Default";
            if (settings["brandDiscipline"] == null) settings["brandDiscipline"] = "Default";

            settings[VersionProperty] = 3;
        }

        /// <summary>
        /// Brings one settings object from v3 to v4: the story and political-power tunables.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Frozen local constants, never a live tuning read — <c>/schema-change</c>'s rule and the
        /// reason <see cref="NaMajorCount"/> is a constant too. A step must reproduce what the file
        /// was written with, and reading <c>stories.storiesPerCycle</c> here would let a balance pass
        /// two years from now retroactively change what an old save migrates into.
        /// </para>
        /// <para>
        /// <see cref="StoriesEnabledAtV4"/> is <c>true</c>, so an existing save gets the story layer
        /// switched on and starts drafting at the next cycle boundary. It gets no <i>history</i>:
        /// nothing here backfills a story, because a story invented for a month the player already
        /// played would be fiction they never got to participate in, and it would desync the state
        /// hash besides. The empty collections are written by the state step, not here.
        /// </para>
        /// <para>
        /// Idempotent, like every step in this table: a property already present is left alone, so
        /// re-running the chain cannot overwrite a choice the player has since made.
        /// </para>
        /// </remarks>
        internal static void UpgradeSettingsObjectToV4(JObject settings)
        {
            if (settings == null) return;

            if (settings["storiesEnabled"] == null) settings["storiesEnabled"] = StoriesEnabledAtV4;
            if (settings["storiesPerCycle"] == null) settings["storiesPerCycle"] = StoriesPerCycleAtV4;
            if (settings["eventsPerStory"] == null) settings["eventsPerStory"] = EventsPerStoryAtV4;
            if (settings["politicalPowerEnabled"] == null)
                settings["politicalPowerEnabled"] = PoliticalPowerEnabledAtV4;

            // Enum NAMES rather than ordinals, for the reason given on UpgradeSettingsObjectToV3: a
            // save file is read by a human when something goes wrong, and an ordinal silently means
            // something else the moment a member is inserted above it.
            if (settings["powerIntensity"] == null) settings["powerIntensity"] = "Default";
            if (settings["storyDifficulty"] == null) settings["storyDifficulty"] = "Default";

            // The LITERAL 4, not CurrentSettingsVersion. This step stamps the version it produces,
            // which is fixed forever at the version it was written for; reading the live constant
            // made the two identical only for as long as 4 was current. The moment the constant moved
            // to 5, a v3 save ran this step, was stamped 5, and the v4 -> v5 step was skipped
            // entirely - a settings file claiming a version whose fields it does not have, with no
            // error anywhere. Every step in these tables stamps a literal, and
            // SidecarMigrationTests pins that each one stamps exactly FromVersion + 1.
            settings[VersionProperty] = 4;
        }

        /// <summary>
        /// Brings one settings object from v4 to v5: the story LLM wake.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>wakeCadence</c> is a flags enum persisted as a comma-separated list of member
        /// <i>names</i>, so a save written before <see cref="LlmWakeCadence.Story"/> existed holds
        /// the literal text <c>"Yearly, Election, Manual"</c> and would never wake on a story draft
        /// however the enum's <c>Default</c> is redefined. Widening the default only reaches new
        /// saves; this step is what reaches the existing ones.
        /// </para>
        /// <para>
        /// <b>It appends rather than overwrites, and only when the save still holds the old default
        /// in full.</b> A player who has already narrowed their cadence - turned the yearly wake off,
        /// say - has expressed a preference about how often this mod starts a subprocess, and
        /// granting them a new and more frequent wake on the strength of a version bump would
        /// override a decision they made deliberately. Such a save keeps exactly what it chose and
        /// picks the story wake up from the settings panel if it wants it.
        /// </para>
        /// <para>
        /// Idempotent: a cadence that already names <c>Story</c> is left untouched.
        /// </para>
        /// </remarks>
        internal static void UpgradeSettingsObjectToV5(JObject settings)
        {
            if (settings == null) return;

            JToken cadence = settings["wakeCadence"];
            if (cadence == null)
            {
                // Absent means "never written", which reads as the default - and the default now
                // includes the story wake. Writing it out keeps the file self-describing.
                settings["wakeCadence"] = WakeCadenceDefaultAtV5;
            }
            else if (cadence.Type == JTokenType.String)
            {
                string text = cadence.Value<string>() ?? string.Empty;
                if (text == WakeCadenceDefaultAtV4) settings["wakeCadence"] = WakeCadenceDefaultAtV5;
            }

            settings[VersionProperty] = 5;
        }

        /// <summary>
        /// Brings one settings object from v5 to v6: <c>pauseOnMajorStory</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Seeded <c>true</c>, which is behaviour-preserving rather than merely a sensible default:
        /// before this field the story card held the clock on the engine's <c>major</c> verdict
        /// alone, unconditionally, so every existing save has been playing with this switch
        /// effectively on. Seeding it <c>false</c> would silently turn an interruption off for
        /// players who never asked for that.
        /// </para>
        /// <para>
        /// It is deliberately <b>not</b> copied from the save's own <c>pauseOnMajorNews</c>. That
        /// control's hint enumerates news categories, so a player who turned it off was answering a
        /// question about elections and party lifecycle, not about stories — inheriting their answer
        /// here would put words in their mouth. The two switches start independent and stay so.
        /// </para>
        /// <para>
        /// Idempotent, like every step in these tables: a property already present is left alone.
        /// </para>
        /// </remarks>
        internal static void UpgradeSettingsObjectToV6(JObject settings)
        {
            if (settings == null) return;

            if (settings["pauseOnMajorStory"] == null)
                settings["pauseOnMajorStory"] = PauseOnMajorStoryAtV6;

            // The LITERAL 6, never CurrentSettingsVersion — see UpgradeSettingsObjectToV4 for the
            // failure that rule exists to stop.
            settings[VersionProperty] = 6;
        }

        /// <summary>
        /// Mirrors <c>AgoraSettings.PauseOnMajorStory</c>'s default at the version this step was
        /// written for. Frozen, never a live read: a later change of heart about the default must not
        /// retroactively change what an old save migrates into.
        /// </summary>
        private const bool PauseOnMajorStoryAtV6 = true;

        /// <summary>
        /// State v7 to v8: <c>pauseOnMajorStory</c> in the nested settings block.
        /// </summary>
        /// <remarks>
        /// The nested block is unreachable from <see cref="SettingsSteps"/> — a state file carries its
        /// settings inside itself and the settings table only ever sees a standalone
        /// <c>settings.json</c> — so a settings addition needs a state step to carry it or the nested
        /// copy sits at the old version forever. Same arrangement and same reason as
        /// <see cref="MigrateStateV6ToV7"/>; waves 2 and 5 each had to learn it once.
        /// </remarks>
        private static void MigrateStateV7ToV8(JObject root)
        {
            var settings = root["settings"] as JObject;
            if (settings != null) UpgradeSettingsObjectToV6(settings);
        }

        /// <summary>
        /// Brings one settings object from v6 to v7: <c>showAllReports</c> is REMOVED.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The first step in this table that takes a property away rather than adding one. It raised
        /// a modal for every article rather than only the major ones, and wave 7 retired the article
        /// alert along with the feed that fed it — so its only reader is gone and the switch
        /// persisted a value that changed no number. That is the defect W5 closed for
        /// <c>PauseOnMajorNews</c>, and keeping the field would have kept it alive.
        /// </para>
        /// <para>
        /// <b>Removal is safe here in a way it would not be for a field that carries meaning.</b>
        /// Nothing reads it, so no upgraded save behaves differently; and a player who had turned it
        /// on was asking for an interruption that no longer exists at all, rather than one this
        /// change is silently declining to give them.
        /// </para>
        /// <para>
        /// Note that <see cref="UpgradeSettingsObjectToV2"/> still ADDS <c>showAllReports</c>, and
        /// must: a step reproduces what the file was written with at the version it was written for,
        /// never what the current build wants. A v1 save therefore gains the property at v2 and loses
        /// it again here, which is the chain being honest about its own history rather than
        /// rewriting it.
        /// </para>
        /// <para>
        /// Idempotent: removing an absent property is a no-op.
        /// </para>
        /// </remarks>
        internal static void UpgradeSettingsObjectToV7(JObject settings)
        {
            if (settings == null) return;

            settings.Remove("showAllReports");

            // The LITERAL 7 — see UpgradeSettingsObjectToV4 for the failure that rule exists to stop.
            settings[VersionProperty] = 7;
        }

        /// <summary>
        /// State v8 to v9: carries the <c>showAllReports</c> removal into the nested settings block.
        /// </summary>
        /// <remarks>
        /// The nested block is unreachable from <see cref="SettingsSteps"/>, so a settings change
        /// needs a state step to carry it or the nested copy keeps the property forever. Waves 2, 5
        /// and 7 have each had to learn this once.
        /// </remarks>
        private static void MigrateStateV8ToV9(JObject root)
        {
            var settings = root["settings"] as JObject;
            if (settings != null) UpgradeSettingsObjectToV7(settings);
        }

        /// <summary>
        /// The exact <c>wakeCadence</c> text a save at settings v4 holds when the player has changed
        /// nothing. Frozen local constants, never a live
        /// <c>LlmWakeCadence.Default.ToString()</c> - see <see cref="UpgradeSettingsObjectToV4"/> for
        /// why a step may not read anything that a later change can move.
        /// </summary>
        private const string WakeCadenceDefaultAtV4 = "Yearly, Election, Manual";

        /// <inheritdoc cref="WakeCadenceDefaultAtV4"/>
        private const string WakeCadenceDefaultAtV5 = "Yearly, Election, Manual, Story";

        /// <summary>
        /// State v5 to v6: the story collections, the power state, and the two story watermarks.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Everything is written empty, and no story is ever generated retroactively.</b> An
        /// existing save has never had a story, so it has no story history; inventing one would
        /// invent a month of politics the player never played and would change the state hash for a
        /// reason that is not a real difference in the city.
        /// </para>
        /// <para>
        /// The two watermarks seed to <c>-1</c> ("never"), which is correct rather than merely safe:
        /// the first cycle boundary after the upgrade is genuinely the first draft this save has ever
        /// run, and any other value would claim a cycle happened. That is the opposite of wave 0's
        /// <c>lastCompletedTickMonth</c>, which had to be seeded from the document's own date
        /// <i>because</i> a state file is by definition the record of a month that finished — there
        /// is no equivalent evidence here, because there is nothing that finished.
        /// </para>
        /// <para>
        /// <c>power.lastAccrualMonth</c> is <c>-1</c> for the same reason, and it matters more than
        /// it looks: seeding it to the current month would silently deny the save its first accrual,
        /// while seeding it to 0 on a thirty-year save would not grant thirty years of back pay
        /// either — the accrual is a single per-month grant, not an integral. -1 is simply "no
        /// accrual has happened", which is true.
        /// </para>
        /// </remarks>
        private static void MigrateStateV5ToV6(JObject root)
        {
            if (root["liveStories"] == null) root["liveStories"] = new JArray();
            if (root["storyArchive"] == null) root["storyArchive"] = new JArray();
            if (root["eventPool"] == null) root["eventPool"] = new JArray();
            if (root["playerCommands"] == null) root["playerCommands"] = new JArray();

            if (root["power"] == null)
            {
                root["power"] = new JObject
                {
                    ["balance"] = 0,
                    ["lifetimeEarned"] = 0,
                    ["lifetimeSpent"] = 0,
                    ["lastAccrualMonth"] = -1,
                    ["ledger"] = new JArray()
                };
            }

            if (root["lastStoryDraftMonth"] == null) root["lastStoryDraftMonth"] = -1;
            if (root["lastStoryResolveMonth"] == null) root["lastStoryResolveMonth"] = -1;

            var settings = root["settings"] as JObject;
            if (settings != null) UpgradeSettingsObjectToV4(settings);
        }

        /// <summary>
        /// State v6 to v7: the story LLM wake, in the nested settings block.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Nothing on the state root moves. This step exists only because a save with a state file
        /// never reads <c>settings.json</c> at all - <c>SidecarStore.ResolveSettings</c> prefers the
        /// state's own block - so <see cref="SettingsSteps"/> never sees those settings, and without
        /// a state step to carry it the nested block would sit at v4 forever. That is the same
        /// arrangement, and the same reason, as <c>MigrateStateV3ToV4</c>.
        /// </para>
        /// <para>
        /// A state file at v6 was written by a build whose settings block was already at v4, so this
        /// runs exactly one settings step and no chain is needed here.
        /// </para>
        /// </remarks>
        private static void MigrateStateV6ToV7(JObject root)
        {
            var settings = root["settings"] as JObject;
            if (settings != null) UpgradeSettingsObjectToV5(settings);
        }

        /// <summary>
        /// Mirrors <c>AgoraSettings</c>'s own defaults at the version this step was written for.
        /// Frozen local constants, never live reads — see <see cref="UpgradeSettingsObjectToV4"/>.
        /// </summary>
        private const bool StoriesEnabledAtV4 = true;
        private const int StoriesPerCycleAtV4 = 2;
        private const int EventsPerStoryAtV4 = 3;
        private const bool PoliticalPowerEnabledAtV4 = true;

        /// <summary>
        /// State v1 to v2: <c>parties[].playerOverrides</c>, plus the settings block nested at
        /// <c>#/settings</c>.
        /// </summary>
        /// <remarks>
        /// The nested settings block is here rather than in <see cref="SettingsSteps"/> because
        /// <see cref="Migrate"/> reads and stamps only the <i>root</i> version, so the Settings step
        /// table never sees settings that arrive inside a state file — and a save with a state file
        /// never reads <c>settings.json</c> at all (<c>SidecarStore.ResolveSettings</c> prefers the
        /// state's own block). Both paths call
        /// <see cref="UpgradeSettingsObjectToV2"/> so there is one implementation, not two.
        /// </remarks>
        private static void MigrateStateV1ToV2(JObject root)
        {
            // Parties: absent playerOverrides means "the player has taken nothing over". They could
            // not have locked a field on a build that had no lock UI.
            var parties = root["parties"] as JArray;
            if (parties != null)
            {
                foreach (JToken token in parties)
                {
                    var party = token as JObject;
                    if (party == null) continue;
                    if (party["playerOverrides"] == null) party["playerOverrides"] = "None";
                }
            }

            var settings = root["settings"] as JObject;
            if (settings == null)
            {
                // A state file with no settings block. ResolveSettings would fall back to defaults
                // anyway; writing them here makes the file self-describing instead of relying on
                // that fallback, and matches what `new AgoraSettings()` produces.
                settings = new JObject();
                root["settings"] = settings;
                settings["startYear"] = 1990;
                settings["theme"] = "Eu";
                settings["system"] = "Proportional";
                settings["wakeCadence"] = "Yearly, Election, Manual";
                settings["snapshotRetention"] = 25;
                settings["enabled"] = true;
                settings["effectsEnabled"] = true;
            }

            UpgradeSettingsObjectToV2(settings);

            // A refinement available only in this document: the theme locks at the first election,
            // and a save that has already held one is past that point. The standalone settings.json
            // step cannot make this call — it cannot see election history — so it leaves the flag
            // false and the runtime re-locks at the next election check.
            var history = root["electionHistory"] as JArray;
            if (history != null && history.Count > 0) settings["themeLocked"] = true;
        }

        /// <summary>
        /// v2 → v3: <c>parties[].isMajor</c>, the root <c>fringe</c> watch, and a one-off correction of
        /// the vote shares the ceiling was never applied to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>isMajor</c> is reconstructed rather than defaulted, because defaulting it to false would
        /// tell the fringe ceiling that an existing NA save has no major parties at all and pin the
        /// whole ballot at 3%. The reconstruction runs on <c>archetypeId</c>, which these files have
        /// carried all along: the NA majors are exactly the brands generated from <c>liberal</c> and
        /// <c>conservative</c>, with <c>predecessorPartyId</c> separating an original brand from a
        /// splinter that copied its archetype. The rule itself lives in
        /// <see cref="NaMajorParties.Reconstruct"/> and is shared with the load-time repair, so the two
        /// cannot come to disagree; this method only projects the DOM into candidates.
        /// </para>
        /// <para>
        /// An earlier version of this step guessed from id order — "the two lowest live ids are the
        /// majors" — on the reasoning that NA generation hands out ids in majors-first catalog order.
        /// That is true at generation and false afterwards: a save whose original <c>liberal</c> had
        /// dissolved would promote whichever brand held the next-lowest id, which is a fringe party by
        /// construction, and the ceiling would then leave it uncapped for good. The id-order rule
        /// survives only as the fallback for a file old enough to predate <c>archetypeId</c>.
        /// </para>
        /// <para>
        /// The <c>fringe</c> block is written zeroed — a save that has never observed a failure term
        /// has not had one, and inventing a streak here would hand an existing save an unearned fringe
        /// surge on its next tick. Off-ballot brands are left at false for the same reason the live
        /// repair leaves them alone: nothing reads the flag while a brand is dead, and
        /// <c>PartyLifecycle.ApplyRevivals</c> does not restore it either, so this matches the engine.
        /// </para>
        /// </remarks>
        /// <summary>
        /// State v3 to v4: the nested settings block gains the three voter-model levels.
        /// </summary>
        /// <remarks>
        /// A state file carries its own copy of the settings and <c>SidecarStore.ResolveSettings</c>
        /// prefers it over <c>settings.json</c>, so a save with a state file never reaches
        /// <see cref="SettingsSteps"/> at all. Without this step the nested block would sit at v2
        /// forever and the three levels would be absent on every existing save — which the reader
        /// tolerates, but it would also mean the settings panel showed a level the sidecar never
        /// stored. Same helper as the standalone path, for the reason given on
        /// <see cref="MigrateStateV1ToV2"/>.
        /// </remarks>
        private static void MigrateStateV3ToV4(JObject root)
        {
            var settings = root["settings"] as JObject;
            if (settings != null) UpgradeSettingsObjectToV3(settings);
        }

        /// <summary>
        /// State v4 to v5: <c>lastCompletedTickMonth</c>, the persisted watermark that stops a reload
        /// re-running a month it already advanced through.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Seeded from the document's <i>own</i> <c>date</c>, not from zero and not from the live
        /// clock. A state file is by definition the record of a month that finished — it is written
        /// after the tick, not before — so the month it names is exactly the last completed one.
        /// Seeding zero would tell the runtime that no month had ever run and hand every existing
        /// save one free duplicate tick on its first load after upgrading, which is the precise bug
        /// this field exists to close.
        /// </para>
        /// <para>
        /// A file with no readable <c>date</c> is left at the contract default of <c>-1</c>. That
        /// costs at most the one duplicate tick the save was already getting, whereas guessing a
        /// month would suppress a real tick — and a suppressed month is unrecoverable where a
        /// duplicated one is merely wrong once.
        /// </para>
        /// <para>
        /// Idempotent, as every step in this table must be: a document that already carries the
        /// property is left alone, so re-running the chain cannot rewind a watermark the runtime has
        /// since advanced.
        /// </para>
        /// </remarks>
        private static void MigrateStateV4ToV5(JObject root)
        {
            if (root["lastCompletedTickMonth"] != null) return;

            int totalMonths;
            root["lastCompletedTickMonth"] =
                TryReadTotalMonths(root["date"], out totalMonths) ? totalMonths : -1;
        }

        /// <summary>
        /// Reads the <c>"YYYY-MM-DD"</c> form <c>SimDateJsonConverter</c> writes into
        /// <see cref="SimDate.TotalMonths"/>. Deliberately does not deserialize through the converter:
        /// a migration step runs on the raw DOM, before the document is allowed to become a contract
        /// object, and reaching for the materialised type here would invert that order.
        /// </summary>
        private static bool TryReadTotalMonths(JToken date, out int totalMonths)
        {
            totalMonths = 0;

            string text = date == null || date.Type != JTokenType.String ? null : date.Value<string>();
            if (string.IsNullOrEmpty(text)) return false;

            string[] parts = text.Split('-');
            if (parts.Length != 3) return false;

            int year, month;
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out year)) return false;
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out month)) return false;

            // "0000-00-00" is the round-trip of default(SimDate) and is reachable on a state whose
            // date was never assigned. Month 0 is not a month, and treating it as one would seed a
            // watermark a year adrift.
            if (month < 1 || month > 12) return false;

            totalMonths = year * 12 + (month - 1);
            return true;
        }

        private static void MigrateStateV2ToV3(JObject root)
        {
            bool isNa = string.Equals((string)root["settings"]?["theme"], "Na", StringComparison.OrdinalIgnoreCase);

            var parties = root["parties"] as JArray;
            if (parties != null)
            {
                var partyObjects = new List<JObject>();
                var candidates = new List<MajorCandidate>();

                foreach (JToken token in parties)
                {
                    var party = token as JObject;
                    if (party == null) continue;

                    party["isMajor"] = false;
                    partyObjects.Add(party);

                    // "Not Dissolved and not Merged" is exactly PartyRegistry.IsOnBallot over the five
                    // PartyStatus values, so the projection matches what the live repair will compute.
                    string status = (string)party["status"] ?? "Active";
                    bool onBallot =
                        !string.Equals(status, "Dissolved", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(status, "Merged", StringComparison.OrdinalIgnoreCase);

                    candidates.Add(new MajorCandidate
                    {
                        PartyId = (string)party["id"] ?? "",
                        ArchetypeId = (string)party["archetypeId"] ?? "",
                        IsOnBallot = onBallot,
                        HasPredecessor = !string.IsNullOrEmpty((string)party["predecessorPartyId"])
                    });
                }

                if (isNa)
                {
                    List<string> majors = NaMajorParties.Reconstruct(
                        candidates, NaMajorArchetypeIdsV3, NaMajorCount);

                    for (int i = 0; i < partyObjects.Count; i++)
                    {
                        string id = (string)partyObjects[i]["id"] ?? "";
                        if (IndexOfOrdinal(majors, id) >= 0) partyObjects[i]["isMajor"] = true;
                    }

                    ClampStalePreCeilingShares(root, partyObjects, majors);
                }
            }

            if (root["fringe"] == null)
            {
                root["fringe"] = new JObject
                {
                    ["consecutiveFailureTerms"] = 0,
                    ["lastClosedTermNumber"] = 0,
                    ["lastTermFailureScore"] = 0.0,
                    ["termNumber"] = 0,
                    ["monthsObserved"] = 0,
                    ["discontentSum"] = 0.0,
                    ["defianceSurgeSum"] = 0.0,
                    ["governmentChanges"] = 0,
                    ["mayorChanges"] = 0
                };
            }
        }

        /// <summary>
        /// Every minor share in a v2 file was computed before the fringe ceiling existed, so this step
        /// pulls them down to what the ceiling would have allowed. NA only: the ceiling is FPTP-only
        /// (<c>FringeFailureModel.Ceilings</c> returns None under proportional), and clamping an EU
        /// save's minor parties would be silent, permanent data loss on a theme that is supposed to
        /// have viable small parties.
        /// </summary>
        /// <remarks>
        /// Shares are clamped, never zeroed and never removed. Zeroing <c>lastVoteShare</c> would make
        /// the dashboard render a fabricated positive swing — it draws
        /// <c>currentPollShare - lastVoteShare</c> — for a party that is in fact being suppressed, and
        /// would erase the record of a real election. Removing a <c>previousVote</c> entry is the same
        /// thing by another route: <c>AffinityEngine.PreviousShare</c> returns 0 for a party it cannot
        /// find, so a stripped entry and a zeroed one both delete the loyalty term instead of capping it.
        /// <para>
        /// The rows are deliberately NOT renormalised. Neither consumer treats <c>previousVote</c> as a
        /// distribution: <c>AffinityEngine.LoyaltyTerm</c> reads one party's share on its own, and the
        /// dashboard divides by eligible voters rather than by the row sum. Renormalising would hand the
        /// majors a habitual-loyalty bonus no voter ever gave them.
        /// </para>
        /// </remarks>
        private static void ClampStalePreCeilingShares(JObject root, List<JObject> partyObjects,
                                                       List<string> majors)
        {
            for (int i = 0; i < partyObjects.Count; i++)
            {
                JObject party = partyObjects[i];
                if (IndexOfOrdinal(majors, (string)party["id"] ?? "") >= 0) continue;

                // Never CREATE the property. Migrate_StateV1_ChangesNothingElse compares everything
                // outside the fields a step is allowed to add, and a lastVoteShare conjured onto a
                // party that had none would fail it — correctly, since inventing an election result is
                // not this step's business.
                ClampShareInPlace(party["lastVoteShare"] as JValue);
            }

            var blocs = root["blocs"] as JArray;
            if (blocs == null) return;

            foreach (JToken blocToken in blocs)
            {
                var previous = (blocToken as JObject)?["previousVote"] as JArray;
                if (previous == null) continue;

                foreach (JToken entryToken in previous)
                {
                    var entry = entryToken as JObject;
                    if (entry == null) continue;
                    if (IndexOfOrdinal(majors, (string)entry["partyId"] ?? "") >= 0) continue;

                    ClampShareInPlace(entry["share"] as JValue);
                }
            }
        }

        private static void ClampShareInPlace(JValue share)
        {
            if (share == null) return;
            if (share.Type != JTokenType.Float && share.Type != JTokenType.Integer) return;

            double value = share.Value<double>();
            if (value > FringeBaseCeilingAtV3) share.Value = FringeBaseCeilingAtV3;
        }

        private static int IndexOfOrdinal(List<string> ids, string value)
        {
            if (ids == null || string.IsNullOrEmpty(value)) return -1;
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.CompareOrdinal(ids[i], value) == 0) return i;
            }
            return -1;
        }

        /// <summary>
        /// Mirrors <c>parties.targetCountNa</c>. Deliberately a local constant and not a tuning read:
        /// a migration must reproduce what the file was written with, and tuning is free to change.
        /// </summary>
        private const int NaMajorCount = 2;

        /// <summary>
        /// Mirrors the majors-first prefix of <c>PartyArchetypes.NaArray</c> at the version this step
        /// was written for. Frozen here for the same reason as <see cref="NaMajorCount"/> — the catalog
        /// may be reordered or renamed later, and this step must keep answering for the files it was
        /// written against. <c>NaMajorPartiesTests</c> asserts it still matches the live catalog, so a
        /// divergence is a deliberate decision rather than a silent one.
        /// </summary>
        internal static readonly string[] NaMajorArchetypeIdsV3 = { "liberal", "conservative" };

        /// <summary>Mirrors <c>fringe.baseCeiling</c> at the version this step was written for.</summary>
        private const double FringeBaseCeilingAtV3 = 0.03;

        // One table per document, so that adding a step is a visible edit in a reviewed place rather
        // than a conditional buried in a loader. Each list must be ordered by FromVersion ascending;
        // the step loop asserts that by walking versions one at a time rather than trusting the
        // order. Timeline progress has not moved, so its table is empty; the flavor cache has moved to
        // 2 but is never migrated through here, so its table is empty for the reason spelled out on
        // CurrentFlavorCacheVersion.
        private static readonly List<MigrationStep> StateSteps = new List<MigrationStep>
        {
            new MigrationStep(1, "added party playerOverrides and the three per-save UI settings",
                MigrateStateV1ToV2),
            new MigrationStep(2, "added party isMajor and the fringe watch",
                MigrateStateV2ToV3),
            new MigrationStep(3, "added the three voter-model levels to the nested settings block",
                MigrateStateV3ToV4),
            new MigrationStep(4, "added lastCompletedTickMonth, seeded from the state's own date",
                MigrateStateV4ToV5),
            new MigrationStep(5, "added the empty story collections, the power state and the story watermarks",
                MigrateStateV5ToV6),
            new MigrationStep(6, "added the story LLM wake to the nested settings block",
                MigrateStateV6ToV7),
            new MigrationStep(7, "added pauseOnMajorStory to the nested settings block",
                MigrateStateV7ToV8),
            new MigrationStep(8, "removed showAllReports from the nested settings block",
                MigrateStateV8ToV9)
        };

        private static readonly List<MigrationStep> SettingsSteps = new List<MigrationStep>
        {
            new MigrationStep(1, "added themeLocked, pauseOnMajorNews, showAllReports",
                root => UpgradeSettingsObjectToV2(root)),
            new MigrationStep(2, "added voteSharpness, newsInfluence, brandDiscipline",
                root => UpgradeSettingsObjectToV3(root)),
            new MigrationStep(3, "added the story and political-power tunables",
                root => UpgradeSettingsObjectToV4(root)),
            new MigrationStep(4, "added the story LLM wake to the cadence",
                root => UpgradeSettingsObjectToV5(root)),
            new MigrationStep(5, "added pauseOnMajorStory",
                root => UpgradeSettingsObjectToV6(root)),
            new MigrationStep(6, "removed showAllReports, whose only reader retired with the feed",
                root => UpgradeSettingsObjectToV7(root))
        };

        private static readonly List<MigrationStep> TimelineProgressSteps = new List<MigrationStep>();
        private static readonly List<MigrationStep> FlavorCacheSteps = new List<MigrationStep>();

        // Empty because metric_history.json is at its first version. Unlike the flavor cache's table
        // this one IS reached — SidecarStore.LoadMetricHistory routes through Migrate — which is safe
        // only while CurrentMetricHistoryVersion is 1: an unversioned or v1 file equals the target and
        // never enters the step loop. Bumping that constant without adding a 1 -> 2 step here turns
        // every existing history into NoPathForward, i.e. silently discards it.
        private static readonly List<MigrationStep> MetricHistorySteps = new List<MigrationStep>();

        public static int CurrentVersionOf(SidecarDocument document)
        {
            switch (document)
            {
                case SidecarDocument.State: return CurrentStateVersion;
                case SidecarDocument.Settings: return CurrentSettingsVersion;
                case SidecarDocument.TimelineProgress: return CurrentTimelineProgressVersion;
                case SidecarDocument.FlavorCache: return CurrentFlavorCacheVersion;
                case SidecarDocument.MetricHistory: return CurrentMetricHistoryVersion;
                default: throw new ArgumentOutOfRangeException("document");
            }
        }

        private static List<MigrationStep> StepsFor(SidecarDocument document)
        {
            switch (document)
            {
                case SidecarDocument.State: return StateSteps;
                case SidecarDocument.Settings: return SettingsSteps;
                case SidecarDocument.TimelineProgress: return TimelineProgressSteps;
                case SidecarDocument.FlavorCache: return FlavorCacheSteps;
                case SidecarDocument.MetricHistory: return MetricHistorySteps;
                default: throw new ArgumentOutOfRangeException("document");
            }
        }

        /// <summary>
        /// Reads <paramref name="root"/>'s version, upgrades it in place to the current one if it can,
        /// and reports what happened. Never throws for a version-related reason.
        /// </summary>
        public static MigrationResult Migrate(JObject root, SidecarDocument document)
        {
            int target = CurrentVersionOf(document);

            if (root == null)
            {
                return new MigrationResult(MigrationOutcome.Unreadable, 0, target,
                    "The document was empty.");
            }

            int version;
            bool hadVersion = TryReadVersion(root, out version);

            // Deliberately generous. The alternative — refusing to load — would strand any save
            // written before the field became mandatory, and version 1 is by definition the shape
            // that predates versioning. The generosity has to include actually migrating the thing:
            // an unversioned document is a v1 document, so it falls through to the chain below
            // rather than being stamped current on the spot.
            bool assumed = !hadVersion;
            if (assumed) version = 1;

            if (version == target)
            {
                if (assumed) root[VersionProperty] = target;

                return new MigrationResult(
                    assumed ? MigrationOutcome.AssumedVersionOne : MigrationOutcome.Current,
                    version, target,
                    assumed ? "No schemaVersion; assumed 1 and stamped as " + Format(target) + "." : null);
            }

            if (version > target)
            {
                return new MigrationResult(MigrationOutcome.TooNew, version, target,
                    "Written by a newer Agora (schemaVersion " + Format(version) + " > " +
                    Format(target) + "). Refusing to guess at a forward migration.");
            }

            List<MigrationStep> steps = StepsFor(document);
            var applied = new List<string>();
            int current = version;

            while (current < target)
            {
                MigrationStep step = Find(steps, current);
                if (step == null)
                {
                    return new MigrationResult(MigrationOutcome.NoPathForward, version, target,
                        "No migration step from schemaVersion " + Format(current) + "; the file is " +
                        "left untouched. " + string.Join(", ", applied.ToArray()));
                }

                try
                {
                    step.Apply(root);
                }
                catch (Exception ex)
                {
                    return new MigrationResult(MigrationOutcome.NoPathForward, version, target,
                        "Migration step " + Format(current) + " -> " + Format(current + 1) +
                        " failed: " + ex.Message);
                }

                applied.Add(step.Description);
                current++;
                root[VersionProperty] = current;
            }

            return new MigrationResult(
                assumed ? MigrationOutcome.AssumedVersionOne : MigrationOutcome.Upgraded,
                version, target,
                (assumed ? "No schemaVersion; assumed 1. " : "") +
                "Upgraded schemaVersion " + Format(version) + " -> " + Format(target) +
                (applied.Count == 0 ? "." : ": " + string.Join(", ", applied.ToArray())));
        }

        private static MigrationStep Find(List<MigrationStep> steps, int fromVersion)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].FromVersion == fromVersion) return steps[i];
            }

            return null;
        }

        public static bool TryReadVersion(JObject root, out int version)
        {
            version = 0;
            if (root == null) return false;

            JToken token = root[VersionProperty];
            if (token == null || token.Type == JTokenType.Null) return false;

            if (token.Type == JTokenType.Integer)
            {
                version = token.Value<int>();
                return true;
            }

            if (token.Type == JTokenType.String)
            {
                return int.TryParse(token.Value<string>(), NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out version);
            }

            return false;
        }

        private static string Format(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
