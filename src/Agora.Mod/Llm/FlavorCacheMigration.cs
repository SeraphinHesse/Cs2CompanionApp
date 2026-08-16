// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// Brings a cached <c>politics_flavor</c> document up to the current schema version before it is
    /// re-validated.
    ///
    /// <para>
    /// <b>Cache only.</b> A live CLI response still fails closed (non-negotiable #7, and the version
    /// check in <see cref="FlavorValidator"/>): the model has to learn the constraint rather than
    /// have it papered over. What is on disk was written by an older build and cannot be asked again.
    /// </para>
    ///
    /// <para>
    /// <b>Why this exists at all.</b> <see cref="FlavorValidator"/> treats a schema error as fatal to
    /// the whole document, deliberately unlike the per-entry catalog drop beside it. So tightening the
    /// article limits without this would make one over-long cached body discard the entire
    /// <c>flavor_cache.json</c> - every <c>partyFlavor</c> entry with it, which is every party
    /// <i>name</i>. The player reloads and sees <c>party-01</c> again. Pruning the offending articles
    /// and leaving everything else alone is the whole point.
    /// </para>
    ///
    /// <para>
    /// <b>It prunes; it never truncates.</b> A body cut at the limit ends mid-sentence and would
    /// be published to the player as though it had been written that way. Article count is a prompt
    /// instruction, not engine state, and no engine number ever depended on prose (non-negotiable
    /// #1), so one fewer article costs nothing and one bad article costs the reader.
    /// </para>
    /// </summary>
    public static class FlavorCacheMigration
    {
        /// <summary>
        /// The article limits the current schema declares. Spelled here so the schema, the fallback
        /// pool and the prompt all read one pair of numbers rather than four copies of them.
        /// </summary>
        public const int HeadlineMaxLength = 270;

        /// <inheritdoc cref="HeadlineMaxLength"/>
        public const int BodyMaxLength = 1260;

        /// <summary>
        /// The story and resolution limits the current schema declares. Same pair of numbers as the
        /// article limits above, and deliberately spelled separately rather than aliased: they are
        /// two independent schema decisions that happen to agree today, and a future retune of one
        /// must not silently move the other.
        /// </summary>
        public const int StoryHeadlineMaxLength = 270;

        /// <inheritdoc cref="StoryHeadlineMaxLength"/>
        public const int StoryArticleMaxLength = 1260;

        /// <summary>
        /// Returns the migrated JSON, or <paramref name="json"/> unchanged when nothing applies.
        /// Never throws - a cache that cannot be upgraded is handed on untouched so the validator
        /// rejects it and says why, which is the outcome that already has a log line.
        /// </summary>
        /// <param name="fromVersion">
        /// The <c>schemaVersion</c> that was on disk, or <c>0</c> when it could not be read.
        /// </param>
        /// <param name="prunedArticles">
        /// How many entries were dropped for exceeding a new limit, across <c>articles</c>,
        /// <c>stories</c> and <c>resolutions</c> together. One counter rather than three because it
        /// exists for a log line that says how much prose the upgrade cost, and the reader does not
        /// act differently on which collection it came from.
        /// </param>
        public static string UpgradeToCurrent(string json, IFlavorLog log,
                                              out int fromVersion, out int prunedArticles)
        {
            log = log ?? NullFlavorLog.Instance;
            fromVersion = 0;
            prunedArticles = 0;

            try
            {
                JObject root = FlavorJsonReader.ParseObject(json);
                if (root == null) return json;

                JToken versionToken = root["schemaVersion"];
                if (versionToken == null || versionToken.Type != JTokenType.Integer) return json;
                fromVersion = versionToken.Value<int>();

                if (fromVersion == FlavorSchema.SupportedSchemaVersion) return json;

                // Every version below the current one routes forward through the same rewrite, so a
                // cache written by any shipped build upgrades in one hop rather than needing a chain.
                // That works because every step so far has only ever moved a length limit, and the
                // prune below is written against the CURRENT limits rather than against the ones the
                // file was authored under - so it is by construction correct from any older version.
                // Add a case here the moment a step has to do something a re-prune cannot express.
                //
                // Anything else is a version this build has no route from - including a version from
                // the future. Hand it back untouched: the validator's version check rejects it, and a
                // session with no cached prose is the honest outcome.
                if (fromVersion < 1 || fromVersion > FlavorSchema.SupportedSchemaVersion) return json;

                prunedArticles = PruneOverLengthArticles(root);
                root["schemaVersion"] = FlavorSchema.SupportedSchemaVersion;

                return root.ToString(Formatting.None);
            }
            catch (Exception ex)
            {
                log.Warn("cached flavor could not be upgraded (" + ex.Message +
                         "); it is being validated as it was found");
                prunedArticles = 0;
                return json;
            }
        }

        /// <summary>
        /// Drops every capped prose entry that is over the current limits and returns how many went.
        /// <c>partyFlavor</c>, <c>factionFlavor</c>, <c>eventProse</c> and <c>generatedAtSimDate</c>
        /// are not touched - none of their limits moved, and the party names among them are the
        /// load-bearing content of the cache.
        /// </summary>
        /// <remarks>
        /// <c>stories</c> and <c>resolutions</c> are swept on the same rule as <c>articles</c> even
        /// though the limits that arrived with them only ever went <i>up</i>, so this pass drops
        /// nothing today. It is here because the rule is "a capped collection is pruned, never
        /// truncated", and a collection that joins the file without its share of that rule is one
        /// tightening away from taking the whole cache - the exact failure the class exists to stop.
        /// </remarks>
        private static int PruneOverLengthArticles(JObject root)
        {
            // MUTANT: stories and resolutions no longer swept
            return Prune(root["articles"] as JArray, "headline", HeadlineMaxLength, "body", BodyMaxLength);
        }

        private static int Prune(JArray entries, string shortField, int shortMax, string longField, int longMax)
        {
            if (entries == null) return 0;

            int pruned = 0;

            // Backwards, so removing an element cannot skip the one after it.
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (IsWithinLimits(entries[i] as JObject, shortField, shortMax, longField, longMax)) continue;

                entries[i].Remove();
                pruned++;
            }

            return pruned;
        }

        private static bool IsWithinLimits(JObject entry, string shortField, int shortMax,
                                           string longField, int longMax)
        {
            // A non-object element, or one missing a required field, is not this migration's problem:
            // leave it for the validator, which reports it far better than a silent drop would.
            if (entry == null) return true;

            return Fits(entry[shortField], shortMax)
                && Fits(entry[longField], longMax);
        }

        private static bool Fits(JToken token, int maxLength)
        {
            if (token == null || token.Type != JTokenType.String) return true;

            string text = token.Value<string>();
            return text == null || text.Length <= maxLength;
        }
    }
}
