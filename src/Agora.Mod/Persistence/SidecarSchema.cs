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
    /// <b>Currently every document is at version 1</b>, so the step tables are empty by design and
    /// the interesting paths are the refusals: a document from a newer build is declined rather than
    /// guessed at, and an unversioned one is assumed to be version 1 and stamped.
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

        public const int CurrentStateVersion = 1;
        public const int CurrentSettingsVersion = 1;
        public const int CurrentTimelineProgressVersion = 1;
        public const int CurrentFlavorCacheVersion = 1;

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

        // Empty until a contract actually moves. Listed explicitly, one table per document, so that
        // adding a step is a visible edit in a reviewed place rather than a conditional buried in a
        // loader. Each list must be ordered by FromVersion ascending; Chain() asserts that by
        // walking versions one at a time rather than trusting the order.
        private static readonly List<MigrationStep> StateSteps = new List<MigrationStep>();
        private static readonly List<MigrationStep> SettingsSteps = new List<MigrationStep>();
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

            if (!hadVersion)
            {
                // Deliberately generous. The alternative — refusing to load — would strand any save
                // written before the field became mandatory, and version 1 is by definition the
                // shape that predates versioning.
                root[VersionProperty] = target;
                return new MigrationResult(MigrationOutcome.AssumedVersionOne, 1, target,
                    "No schemaVersion; assumed 1 and stamped as " + Format(target) + ".");
            }

            if (version == target)
            {
                return new MigrationResult(MigrationOutcome.Current, version, target, null);
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

            return new MigrationResult(MigrationOutcome.Upgraded, version, target,
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
