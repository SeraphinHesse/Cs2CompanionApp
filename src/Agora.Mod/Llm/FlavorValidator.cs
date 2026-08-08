// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Newtonsoft.Json.Linq;

namespace Agora.Mod.Llm
{
    /// <summary>Outcome of validating one candidate flavor response.</summary>
    public sealed class FlavorValidationResult
    {
        /// <summary>Null when validation failed.</summary>
        public FlavorDocument Document { get; internal set; }

        /// <summary>Reasons the response was rejected. Empty when <see cref="Document"/> is set.</summary>
        public IReadOnlyList<string> Errors { get; internal set; }

        /// <summary>
        /// Entries that were accepted overall but individually discarded - an unknown party ID, an
        /// article referencing a district that does not exist. Not fatal; worth a log line.
        /// </summary>
        public IReadOnlyList<string> Discarded { get; internal set; }

        public bool IsValid => Document != null;

        internal FlavorValidationResult()
        {
            Errors = new string[0];
            Discarded = new string[0];
        }

        public static FlavorValidationResult Failed(params string[] errors) =>
            new FlavorValidationResult { Errors = errors ?? new string[0] };

        public static FlavorValidationResult Failed(IReadOnlyList<string> errors) =>
            new FlavorValidationResult { Errors = errors ?? (IReadOnlyList<string>)new string[0] };
    }

    /// <summary>
    /// The gate every LLM response passes before a single character reaches engine-visible state.
    ///
    /// <para>Four checks, in order, each cheap enough to run on every response:</para>
    /// <list type="number">
    /// <item><description>
    /// <b>Parse</b> with <see cref="FlavorJsonReader"/> - dates off, depth capped, size capped.
    /// </description></item>
    /// <item><description>
    /// <b>Schema</b> via <see cref="JsonSchemaSubsetValidator"/> against
    /// <c>politics_flavor.schema.json</c>. This is where <c>additionalProperties: false</c> makes a
    /// numeric field structurally unrepresentable.
    /// </description></item>
    /// <item><description>
    /// <b>Numeric sweep</b> via <see cref="NumericFieldScanner"/> - belt and braces on #1.
    /// </description></item>
    /// <item><description>
    /// <b>ID check</b> against <see cref="FlavorCatalog"/>. Unknown IDs drop the entry; they do not
    /// fail the response, because losing one article to a hallucinated district is much better than
    /// losing a whole year of prose.
    /// </description></item>
    /// </list>
    ///
    /// <para>
    /// Pure and side-effect free apart from logging. Constructed once and reused; safe to call from
    /// the flavor worker thread as long as one instance is not shared across threads mid-call.
    /// </para>
    /// </summary>
    public sealed class FlavorValidator
    {
        private readonly JObject _schema;
        private readonly IFlavorLog _log;

        public FlavorValidator(JObject schema, IFlavorLog log)
        {
            _schema = schema;
            _log = log ?? NullFlavorLog.Instance;
        }

        /// <summary>Loads the schema itself. <paramref name="schemaFilePath"/> may be null.</summary>
        public static FlavorValidator Create(string schemaFilePath, IFlavorLog log) =>
            new FlavorValidator(FlavorSchema.Load(schemaFilePath, log), log);

        /// <summary>
        /// Validates <paramref name="json"/>. Never throws: every failure mode is a returned error.
        /// </summary>
        /// <param name="json">The candidate flavor document, already unwrapped from any CLI envelope.</param>
        /// <param name="catalog">Legal IDs. Pass <see cref="FlavorCatalog.Empty"/> to reject all references.</param>
        /// <param name="expectedDate">
        /// The sim date the request was made for. A response stamped with a different date is accepted
        /// but logged - the model does not own the clock (non-negotiable #8), so the caller's date wins.
        /// </param>
        public FlavorValidationResult Validate(string json, FlavorCatalog catalog, SimDate expectedDate)
        {
            try
            {
                return ValidateCore(json, catalog ?? FlavorCatalog.Empty, expectedDate);
            }
            catch (Exception ex)
            {
                // Nothing below is supposed to throw. If it does, that is a bug in Agora, not in the
                // model - and #7 still says we log and carry on rather than take the sim down.
                _log.Error("flavor validation threw unexpectedly; treating the response as invalid", ex);
                return FlavorValidationResult.Failed("validator threw: " + ex.Message);
            }
        }

        private FlavorValidationResult ValidateCore(string json, FlavorCatalog catalog, SimDate expectedDate)
        {
            string parseError;
            JObject root = FlavorJsonReader.ParseObject(json, out parseError);
            if (root == null)
            {
                return FlavorValidationResult.Failed(parseError ?? "response was not a JSON object");
            }

            if (_schema == null)
            {
                // Fail closed. An unvalidated response is exactly the thing this class exists to
                // prevent, so no schema means no flavor.
                return FlavorValidationResult.Failed(
                    "no politics_flavor schema is available, so the response cannot be validated");
            }

            var errors = new List<string>(JsonSchemaSubsetValidator.Validate(root, _schema));

            foreach (string numeric in NumericFieldScanner.FindNumbers(root))
            {
                errors.Add(numeric);
            }

            if (errors.Count > 0)
            {
                return FlavorValidationResult.Failed(errors);
            }

            FlavorDocument document = FlavorDocument.FromValidatedObject(root);

            if (document.SchemaVersion != FlavorSchema.SupportedSchemaVersion)
            {
                return FlavorValidationResult.Failed(
                    "schemaVersion is " + document.SchemaVersion + ", this build speaks " +
                    FlavorSchema.SupportedSchemaVersion);
            }

            if (document.GeneratedAt.HasValue && document.GeneratedAt.Value != expectedDate)
            {
                _log.Debug("response is stamped " + document.GeneratedAtSimDateText + " but was requested for " +
                           expectedDate + "; the caller's date wins");
            }

            var discarded = new List<string>();
            FilterAgainstCatalog(document, catalog, discarded);

            return new FlavorValidationResult
            {
                Document = document,
                Errors = new string[0],
                Discarded = discarded
            };
        }

        /// <summary>
        /// Drops every entry whose IDs the engine does not recognise. Mutates the document's lists in
        /// place; it has not been handed to anyone yet.
        /// </summary>
        private static void FilterAgainstCatalog(FlavorDocument document, FlavorCatalog catalog, List<string> discarded)
        {
            var seenParties = new HashSet<string>(StringComparer.Ordinal);
            document.PartyFlavor.RemoveAll(entry =>
            {
                if (!catalog.HasParty(entry.PartyId))
                {
                    discarded.Add("partyFlavor for unknown party '" + entry.PartyId + "'");
                    return true;
                }
                if (!seenParties.Add(entry.PartyId))
                {
                    // Two names for one party is ambiguous, and picking one arbitrarily would depend
                    // on the model's output order. Keep the first, drop the rest.
                    discarded.Add("duplicate partyFlavor for '" + entry.PartyId + "'");
                    return true;
                }
                return false;
            });

            var seenFactions = new HashSet<string>(StringComparer.Ordinal);
            document.FactionFlavor.RemoveAll(entry =>
            {
                if (!catalog.HasFaction(entry.FactionId))
                {
                    discarded.Add("factionFlavor for unknown faction '" + entry.FactionId + "'");
                    return true;
                }
                if (!string.IsNullOrEmpty(entry.PartyId) && !catalog.HasParty(entry.PartyId))
                {
                    discarded.Add("factionFlavor '" + entry.FactionId + "' claims unknown party '" + entry.PartyId + "'");
                    return true;
                }
                if (!seenFactions.Add(entry.FactionId))
                {
                    discarded.Add("duplicate factionFlavor for '" + entry.FactionId + "'");
                    return true;
                }
                return false;
            });

            var seenArticles = new HashSet<string>(StringComparer.Ordinal);
            document.Articles.RemoveAll(entry =>
            {
                if (string.IsNullOrEmpty(entry.Id))
                {
                    discarded.Add("article with an empty id");
                    return true;
                }
                if (!seenArticles.Add(entry.Id))
                {
                    discarded.Add("duplicate article id '" + entry.Id + "'");
                    return true;
                }
                if (!string.IsNullOrEmpty(entry.PartyId) && !catalog.HasParty(entry.PartyId))
                {
                    discarded.Add("article '" + entry.Id + "' references unknown party '" + entry.PartyId + "'");
                    return true;
                }
                if (!string.IsNullOrEmpty(entry.DistrictId) && !catalog.HasDistrict(entry.DistrictId))
                {
                    discarded.Add("article '" + entry.Id + "' references unknown district '" + entry.DistrictId + "'");
                    return true;
                }
                if (!string.IsNullOrEmpty(entry.EventId) && !catalog.HasEvent(entry.EventId))
                {
                    discarded.Add("article '" + entry.Id + "' references unknown event '" + entry.EventId + "'");
                    return true;
                }
                return false;
            });

            var seenEvents = new HashSet<string>(StringComparer.Ordinal);
            document.EventProse.RemoveAll(entry =>
            {
                if (!catalog.HasEvent(entry.EventId))
                {
                    discarded.Add("eventProse for unknown event '" + entry.EventId + "'");
                    return true;
                }
                if (!seenEvents.Add(entry.EventId))
                {
                    discarded.Add("duplicate eventProse for '" + entry.EventId + "'");
                    return true;
                }
                return false;
            });
        }
    }
}
