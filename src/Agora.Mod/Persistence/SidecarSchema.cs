// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Agora.Mod.Persistence
{
    /// <summary>Which of the four sidecar documents a version number belongs to.</summary>
    public enum SidecarDocument
    {
        State = 0,
        Settings = 1,
        TimelineProgress = 2,
        FlavorCache = 3
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
    /// State is at version 3; settings and the flavor cache are at 2; timeline progress is still at 1,
    /// so its table is empty. So is the flavor cache's, which is not an omission: nothing routes
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

        public const int CurrentStateVersion = 3;
        public const int CurrentSettingsVersion = 2;

        /// <summary><c>timeline_progress.json</c> has not moved; it is still a list of fired ids.</summary>
        public const int CurrentTimelineProgressVersion = 1;

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
        public const int CurrentFlavorCacheVersion = 2;

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

            settings[VersionProperty] = CurrentSettingsVersion;
        }

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
        /// v2 → v3: <c>parties[].isMajor</c> and the root <c>fringe</c> watch.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>isMajor</c> is reconstructed rather than defaulted, because defaulting it to false would
        /// tell the fringe ceiling that an existing NA save has no major parties at all and pin the
        /// whole ballot at 3%. The reconstruction is exact: NA generation walks
        /// <c>PartyArchetypes.NaArray</c> majors-first and hands out <c>party-01</c>, <c>party-02</c>,
        /// … in that order, so the two lowest ids are the two majors. Later ids can only be splinters
        /// or entrants, which are fringe by definition, and EU saves have no majors at all.
        /// </para>
        /// <para>
        /// Dissolved and merged brands are skipped when picking the two lowest, for the same reason
        /// <c>NextPartyId</c> counts past them: a dead <c>party-01</c> must not consume a major slot
        /// that belongs to a live party. The <c>fringe</c> block is written zeroed — a save that has
        /// never observed a failure term has not had one, and inventing a streak here would hand an
        /// existing save an unearned fringe surge on its next tick.
        /// </para>
        /// </remarks>
        private static void MigrateStateV2ToV3(JObject root)
        {
            bool isNa = string.Equals((string)root["settings"]?["theme"], "Na", StringComparison.OrdinalIgnoreCase);

            var parties = root["parties"] as JArray;
            if (parties != null)
            {
                // Ordinal sort over ids reproduces generation order: FormatId zero-pads, so string
                // order and numeric order agree. Explicit, because JArray order is whatever the
                // writer happened to emit.
                var live = new List<JObject>();
                foreach (JToken token in parties)
                {
                    var party = token as JObject;
                    if (party == null) continue;

                    party["isMajor"] = false;

                    string status = (string)party["status"] ?? "Active";
                    if (string.Equals(status, "Dissolved", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(status, "Merged", StringComparison.OrdinalIgnoreCase)) continue;

                    live.Add(party);
                }

                if (isNa)
                {
                    live.Sort((a, b) => string.CompareOrdinal((string)a["id"] ?? "", (string)b["id"] ?? ""));
                    for (int i = 0; i < live.Count && i < NaMajorCount; i++) live[i]["isMajor"] = true;
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
        /// Mirrors <c>parties.targetCountNa</c>. Deliberately a local constant and not a tuning read:
        /// a migration must reproduce what the file was written with, and tuning is free to change.
        /// </summary>
        private const int NaMajorCount = 2;

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
                MigrateStateV2ToV3)
        };

        private static readonly List<MigrationStep> SettingsSteps = new List<MigrationStep>
        {
            new MigrationStep(1, "added themeLocked, pauseOnMajorNews, showAllReports",
                root => UpgradeSettingsObjectToV2(root))
        };

        private static readonly List<MigrationStep> TimelineProgressSteps = new List<MigrationStep>();
        private static readonly List<MigrationStep> FlavorCacheSteps = new List<MigrationStep>();

        public static int CurrentVersionOf(SidecarDocument document)
        {
            switch (document)
            {
                case SidecarDocument.State: return CurrentStateVersion;
                case SidecarDocument.Settings: return CurrentSettingsVersion;
                case SidecarDocument.TimelineProgress: return CurrentTimelineProgressVersion;
                case SidecarDocument.FlavorCache: return CurrentFlavorCacheVersion;
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
