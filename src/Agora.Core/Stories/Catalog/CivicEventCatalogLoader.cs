using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Agora.Core.Contracts;
using Agora.Core.Events.Catalog;
using Agora.Core.Tuning;

namespace Agora.Core.Stories.Catalog
{
    /// <summary>
    /// Reads and validates <c>events_*.json</c> — the authored civic events a story is assembled from.
    ///
    /// <para>
    /// Pure, like <c>TimelineCatalogLoader</c>: it takes document text and an
    /// <see cref="EngineTuning"/>, and returns events plus findings. It never opens a file, never
    /// reads a clock and never draws a random number. It never throws on bad content either — it
    /// degrades to the valid subset and reports (non-negotiable #7).
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The validation this loader exists for is not shape validation.</b>
    /// <c>civic_events.schema.json</c> already covers shape, and JSON Schema can express neither of
    /// the two checks that actually matter: that every <c>effectId</c> exists in the closed effect
    /// palette, and that every <c>metricId</c> exists in <c>MetricRegistry</c> at the scope it is
    /// read at. Both are done here.
    /// </para>
    /// <para>
    /// <b>Three id vocabularies reach <c>MetricId</c>, and only one of them is checkable against the
    /// engine.</b> Registry metrics are; progression feature ids are raw prefab-name strings and
    /// policy ids are not written by anything. That asymmetry is dangerous in one specific direction:
    /// <c>TriggerKind.Absent</c> negates whatever its spec resolves to, so an id that resolves to
    /// <i>nothing</i> — a typo, a policy, a feature name that does not exist — reads as "not present"
    /// and therefore fires <c>Met</c> on every city, forever, silently. Every rule below that looks
    /// pedantic is closing one route to that outcome, and closing it at <b>load time</b>, which is
    /// the only place it is visible.
    /// </para>
    /// </remarks>
    public static class CivicEventCatalogLoader
    {
        /// <summary>
        /// The only <c>schemaVersion</c> this loader accepts. A contract version, not a coefficient;
        /// changing it goes through <c>/schema-change</c> (non-negotiable #9).
        /// </summary>
        public const int SupportedSchemaVersion = 1;

        /// <summary>
        /// The metric ids whose <b>units are unresolved</b> until wave 1's <c>AGORA-STATCOLLECTION</c>
        /// census gate is walked, and which may therefore carry a <c>delta</c> spec but never an
        /// absolute <c>metric</c> one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>CityStatisticsSystem</c> exposes no unit metadata, so each of these five is either a
        /// per-in-game-day rate or a total accumulated since the city was founded, and the two differ
        /// by orders of magnitude. An absolute threshold authored against the wrong reading is not
        /// merely mistuned — it is either unreachable forever or true from month one, and nothing in
        /// the build would say so.
        /// </para>
        /// <para>
        /// A <c>delta</c> survives the ambiguity in <i>direction</i>: both readings rise when the
        /// underlying thing rises. It does <b>not</b> survive it in <i>magnitude</i>, so the authored
        /// thresholds on these five stay provisional until the census is read, and wave 7's balance
        /// pass owns them. This list shrinks to empty when that gate is walked; it is not a permanent
        /// property of the metrics.
        /// </para>
        /// <para>
        /// Sorted ordinal, and compared by binary search, so adding an id here cannot depend on where
        /// it was typed.
        /// </para>
        /// </remarks>
        public static readonly IReadOnlyList<string> CensusGatedMetricIds = SortedOrdinal(new[]
        {
            MetricRegistry.Births,
            MetricRegistry.CitizensMovedAway,
            MetricRegistry.CitizensMovedIn,
            MetricRegistry.Deaths,
            MetricRegistry.MovedAwayUnhappy
        });

        /// <summary>
        /// Metrics whose sensor cannot reach 1.0, and the value they actually top out at.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Both are means over channels the game does not all expose, so the unmeasured ones are
        /// written as literal zeros and drag the mean down permanently:
        /// </para>
        /// <list type="bullet">
        /// <item><c>serviceCoverage</c> — the mean of <b>nine</b> channels
        /// (<c>CitySnapshot.Mean()</c>), of which garbage, transit, water and electricity have no
        /// coverage concept in the game at all and are hard-zeroed
        /// (<c>AgoraServiceCoverageSensorSystem</c>). Five of nine measured, so the ceiling is
        /// 5/9 ≈ <b>0.5556</b>. A threshold of 0.45 is therefore 81% of the attainable maximum, not
        /// "a bit over half".</item>
        /// <item><c>pollution</c> — the mean of four, with water not measurable from the CPU
        /// (<c>AgoraEnvironmentSensorSystem</c>). Ceiling <b>0.75</b>. Here the bias is benign for a
        /// "too high" trigger and hostile for a "get it below" check, which is the asymmetry to
        /// watch.</item>
        /// </list>
        /// <para>
        /// <b>Only the provable half is machine-checked</b> — a threshold strictly above the ceiling
        /// can never be met, and that is a warning. A threshold merely <i>close</i> to the ceiling is
        /// demanding rather than impossible, and this loader has no basis to say how demanding is too
        /// demanding. The numbers are published here so an author and a reviewer can make that
        /// judgement against something real instead of assuming a 0–1 range.
        /// </para>
        /// </remarks>
        public static double? AttainableMaximum(string metricId)
        {
            if (string.CompareOrdinal(metricId, MetricRegistry.ServiceCoverageMean) == 0) return 5.0 / 9.0;
            if (string.CompareOrdinal(metricId, MetricRegistry.PollutionMean) == 0) return 0.75;
            return null;
        }

        private static readonly string[] RootKeys = { "schemaVersion", "featureIds", "events" };

        private static readonly string[] EventKeys =
        {
            "id", "severity", "region", "trigger", "check",
            "activeEffects", "successEffects", "failureEffects",
            "activePressure", "successPressure", "failurePressure",
            "districtAffinity", "tags", "notes",
            "name", "description", "ignoreText", "goalText", "powerOverrideText",
            "successText", "failText"
        };

        private static readonly string[] SpecKeys =
        {
            "kind", "metricId", "comparison", "threshold", "windowMonths", "scope"
        };

        private static readonly string[] CheckKeys = { "spec", "relativeToBaseline" };

        private static readonly string[] IssueKeys = BuildIssueKeys();

        // --- entry points ----------------------------------------------------------------------

        /// <summary>Validates one document.</summary>
        public static CivicEventCatalogLoadResult Load(string sourceName, string json, EngineTuning tuning) =>
            Load(new[] { new CivicEventCatalogSource(sourceName, json) }, tuning);

        /// <summary>
        /// Validates one document read from a <see cref="TextReader"/>. The reader is the caller's —
        /// Core does not own streams and does not dispose it.
        /// </summary>
        public static CivicEventCatalogLoadResult LoadFrom(string sourceName, TextReader reader, EngineTuning tuning)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            return Load(sourceName, reader.ReadToEnd(), tuning);
        }

        /// <summary>
        /// Validates every document as one catalog: ids are unique across documents, the
        /// <c>featureIds</c> allow-lists union, and the resulting event list is sorted by id.
        /// </summary>
        /// <remarks>
        /// Sources are processed in name order rather than in the order the caller enumerated them, so
        /// which copy of a duplicated id survives cannot depend on a directory listing.
        /// </remarks>
        public static CivicEventCatalogLoadResult Load(IEnumerable<CivicEventCatalogSource> sources,
                                                       EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            var errors = new List<CatalogIssue>();
            var warnings = new List<CatalogIssue>();
            var accepted = new List<CivicEvent>();
            var seenIds = new Dictionary<string, string>(StringComparer.Ordinal);
            int rejected = 0;

            List<CivicEventCatalogSource> ordered = OrderSources(sources, warnings);

            // Two passes. Every document's featureIds allow-list is collected FIRST, because an
            // unlock trigger in events_eu.json is entitled to name a feature declared in
            // events_global.json — the allow-list is a property of the catalog, not of one file.
            // Validating in one pass would make an id's legality depend on which file was read first.
            var declaredFeatures = new List<string>();
            var featureSet = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < ordered.Count; i++)
            {
                CollectFeatureIds(ordered[i], featureSet, declaredFeatures, errors, warnings);
            }

            for (int i = 0; i < ordered.Count; i++)
            {
                ReadSource(ordered[i], tuning, featureSet, seenIds, accepted, errors, warnings, ref rejected);
            }

            return new CivicEventCatalogLoadResult(
                new CivicEventCatalog(accepted, declaredFeatures), errors, warnings, rejected);
        }

        // --- documents ---------------------------------------------------------------------------

        private static List<CivicEventCatalogSource> OrderSources(IEnumerable<CivicEventCatalogSource> sources,
                                                                  List<CatalogIssue> warnings)
        {
            var list = new List<CivicEventCatalogSource>();
            if (sources != null)
            {
                foreach (CivicEventCatalogSource source in sources)
                {
                    if (source != null) list.Add(source);
                }
            }

            // Decorated with the original index so equal names keep their input order: List<T>.Sort is
            // not stable, and an unstable tie-break would make the result depend on element count.
            var decorated = new List<KeyValuePair<int, CivicEventCatalogSource>>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                decorated.Add(new KeyValuePair<int, CivicEventCatalogSource>(i, list[i]));
            }

            decorated.Sort((a, b) =>
            {
                int byName = string.CompareOrdinal(a.Value.Name, b.Value.Name);
                return byName != 0 ? byName : a.Key.CompareTo(b.Key);
            });

            var result = new List<CivicEventCatalogSource>(decorated.Count);
            for (int i = 0; i < decorated.Count; i++)
            {
                CivicEventCatalogSource source = decorated[i].Value;
                if (i > 0 && string.CompareOrdinal(source.Name, decorated[i - 1].Value.Name) == 0)
                {
                    warnings.Add(Warn(CatalogIssueCode.DuplicateSourceName, source.Name, "", "",
                        "two sources share this name; findings from them are indistinguishable"));
                }

                result.Add(source);
            }

            return result;
        }

        /// <summary>
        /// Pass one: the <c>featureIds</c> allow-list only. Parse failures are reported by
        /// <see cref="ReadSource"/> on pass two rather than twice here.
        /// </summary>
        private static void CollectFeatureIds(CivicEventCatalogSource source, HashSet<string> featureSet,
                                              List<string> declared, List<CatalogIssue> errors,
                                              List<CatalogIssue> warnings)
        {
            JsonNode root;
            if (!TryParse(source, out root, null)) return;
            if (root.Kind != JsonKind.Object) return;

            JsonNode? node = Member(root, "featureIds");
            if (node == null) return;

            if (node.Kind != JsonKind.Array || node.Items == null)
            {
                errors.Add(Error(CatalogIssueCode.MalformedFeatureIds, source.Name, "", "featureIds",
                    "expected an array of progression feature id strings"));
                return;
            }

            for (int i = 0; i < node.Items.Count; i++)
            {
                string path = "featureIds[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                JsonNode item = node.Items[i];
                string? text = item.Kind == JsonKind.String ? item.Text : null;
                if (string.IsNullOrEmpty(text))
                {
                    errors.Add(Error(CatalogIssueCode.MalformedFeatureIds, source.Name, "", path,
                        "expected a non-empty string"));
                    continue;
                }

                if (featureSet.Add(text!)) declared.Add(text!);
            }
        }

        private static bool TryParse(CivicEventCatalogSource source, out JsonNode root,
                                     List<CatalogIssue>? errors)
        {
            root = JsonNode.EmptyObject;
            try
            {
                root = TuningJsonParser.Parse(source.Json);
                return true;
            }
            catch (Exception ex) when (ex is TuningFormatException || ex is FormatException || ex is OverflowException)
            {
                if (errors != null)
                {
                    errors.Add(Error(CatalogIssueCode.MalformedJson, source.Name, "", "",
                        "not valid JSON: " + ex.Message));
                }
                return false;
            }
        }

        private static void ReadSource(CivicEventCatalogSource source, EngineTuning tuning,
                                       HashSet<string> featureSet, Dictionary<string, string> seenIds,
                                       List<CivicEvent> accepted, List<CatalogIssue> errors,
                                       List<CatalogIssue> warnings, ref int rejected)
        {
            JsonNode root;
            if (!TryParse(source, out root, errors)) return;

            if (root.Kind != JsonKind.Object)
            {
                errors.Add(Error(CatalogIssueCode.RootNotObject, source.Name, "", "",
                    "the document root must be a JSON object"));
                return;
            }

            WarnUnknownProperties(root, RootKeys, source.Name, "", "", warnings);

            JsonNode? versionNode = Member(root, "schemaVersion");
            int version;
            if (versionNode == null || !TryInt(versionNode, out version) || version != SupportedSchemaVersion)
            {
                errors.Add(Error(CatalogIssueCode.UnsupportedSchemaVersion, source.Name, "", "schemaVersion",
                    "expected schemaVersion " + SupportedSchemaVersion.ToString(CultureInfo.InvariantCulture)));
                return;
            }

            JsonNode? eventsNode = Member(root, "events");
            if (eventsNode == null || eventsNode.Kind != JsonKind.Array || eventsNode.Items == null)
            {
                errors.Add(Error(CatalogIssueCode.EventsMissing, source.Name, "", "events",
                    "expected an array of civic events"));
                return;
            }

            for (int i = 0; i < eventsNode.Items.Count; i++)
            {
                CivicEvent? loaded = ReadEvent(eventsNode.Items[i], i, source, tuning, featureSet,
                                               seenIds, errors, warnings);
                if (loaded == null) rejected++;
                else accepted.Add(loaded);
            }
        }

        // --- events ------------------------------------------------------------------------------

        private static CivicEvent? ReadEvent(JsonNode node, int index, CivicEventCatalogSource source,
                                             EngineTuning tuning, HashSet<string> featureSet,
                                             Dictionary<string, string> seenIds,
                                             List<CatalogIssue> errors, List<CatalogIssue> warnings)
        {
            string path = "events[" + index.ToString(CultureInfo.InvariantCulture) + "]";

            if (node.Kind != JsonKind.Object)
            {
                errors.Add(Error(CatalogIssueCode.EventNotObject, source.Name, "", path, "expected an object"));
                return null;
            }

            bool ok = true;

            // --- id: claimed first, so every later finding can name the offender -----------------
            string id = "";
            JsonNode? idNode = Member(node, "id");
            if (idNode != null && idNode.Kind == JsonKind.String) id = idNode.Text ?? "";

            if (id.Length == 0)
            {
                errors.Add(Error(CatalogIssueCode.MissingEventId, source.Name, "", path + ".id",
                    "every civic event needs a non-empty id"));
                ok = false;
            }
            else if (!IsKebabCase(id))
            {
                errors.Add(Error(CatalogIssueCode.MalformedEventId, source.Name, id, path + ".id",
                    "'" + id + "' must be lowercase kebab-case ([a-z0-9-])"));
                ok = false;
            }
            else
            {
                string firstSource;
                if (seenIds.TryGetValue(id, out firstSource))
                {
                    errors.Add(Error(CatalogIssueCode.DuplicateEventId, source.Name, id, path + ".id",
                        "'" + id + "' was already declared in " + firstSource));
                    ok = false;
                }
                else
                {
                    seenIds.Add(id, source.Name);
                }
            }

            WarnUnknownProperties(node, EventKeys, source.Name, id, path, warnings);

            // --- severity --------------------------------------------------------------------
            int severityMax = tuning.Catalog.SeverityMax;
            if (severityMax < 1) severityMax = 1;

            int severity = 1;
            JsonNode? severityNode = Member(node, "severity");
            if (severityNode == null || !TryInt(severityNode, out severity) || severity < 1 || severity > severityMax)
            {
                errors.Add(Error(CatalogIssueCode.SeverityOutOfRange, source.Name, id, path + ".severity",
                    "expected an integer in 1.." + severityMax.ToString(CultureInfo.InvariantCulture) +
                    " (catalog.severityMax)"));
                ok = false;
                severity = 1;
            }

            // --- region ----------------------------------------------------------------------
            EventRegion region;
            JsonNode? regionNode = Member(node, "region");
            string regionText = regionNode != null && regionNode.Kind == JsonKind.String ? (regionNode.Text ?? "") : "";
            if (!TryParseRegion(regionText, out region))
            {
                errors.Add(Error(CatalogIssueCode.UnknownRegion, source.Name, id, path + ".region",
                    "expected \"eu\", \"na\" or \"global\""));
                ok = false;
            }

            // --- trigger ---------------------------------------------------------------------
            TriggerSpec trigger = ReadSpec(Member(node, "trigger"), path + ".trigger", id, source, tuning,
                                            featureSet, errors, warnings, required: true, ref ok);

            // --- check -----------------------------------------------------------------------
            CheckSpec check = ReadCheck(Member(node, "check"), path + ".check", id, source, tuning,
                                         featureSet, errors, warnings, ref ok);

            // --- the trigger and the check, considered together ------------------------------
            //
            // Everything else in this loader validates one spec at a time. This rule cannot be: it is
            // about the RELATIONSHIP between the two, and it is the shape that produces a story which
            // opens on the city's worst district and is scored against its best.
            WarnIfCheckIsNotBoundToTrigger(trigger, check, path, id, source, warnings);
            WarnIfCheckLeavesATrapBand(trigger, check, path, id, source, warnings);
            WarnIfCheckOutrunsStoryLife(check, tuning, path, id, source, warnings);

            // --- effect lists ----------------------------------------------------------------
            List<string> activeEffects = ReadEffectList(node, "activeEffects", path, id, source, tuning,
                                                        errors, warnings, ref ok);
            List<string> successEffects = ReadEffectList(node, "successEffects", path, id, source, tuning,
                                                         errors, warnings, ref ok);
            List<string> failureEffects = ReadEffectList(node, "failureEffects", path, id, source, tuning,
                                                         errors, warnings, ref ok);

            // --- pressures -------------------------------------------------------------------
            IssuePosition activePressure = ReadIssuePressure(node, "activePressure", path, id, source,
                                                             errors, warnings, ref ok);
            IssuePosition successPressure = ReadIssuePressure(node, "successPressure", path, id, source,
                                                              errors, warnings, ref ok);
            IssuePosition failurePressure = ReadIssuePressure(node, "failurePressure", path, id, source,
                                                              errors, warnings, ref ok);

            // Salience, not credit: an outcome pressure may be louder or quieter than the active one
            // but must not point the other way. See CivicEvent.ActivePressure's remarks.
            WarnOnPressureSignFlip(activePressure, successPressure, "successPressure",
                                   path, id, source, warnings);
            WarnOnPressureSignFlip(activePressure, failurePressure, "failurePressure",
                                   path, id, source, warnings);

            // --- string lists ----------------------------------------------------------------
            List<string> districtAffinity = ReadStringList(node, "districtAffinity", path, id, source,
                                                           CatalogIssueCode.MalformedDistrictAffinity,
                                                           errors, ref ok);
            List<string> tags = ReadStringList(node, "tags", path, id, source,
                                                CatalogIssueCode.MalformedTags, errors, ref ok);

            // --- the seven prose fields ------------------------------------------------------
            //
            // All seven are required, and blank is not "author it later": each one is rendered on a
            // surface the player acts from. An empty GoalText is a button with no label on it.
            string name = ReadProse(node, "name", path, id, source, errors, ref ok);
            string description = ReadProse(node, "description", path, id, source, errors, ref ok);
            string ignoreText = ReadProse(node, "ignoreText", path, id, source, errors, ref ok);
            string goalText = ReadProse(node, "goalText", path, id, source, errors, ref ok);
            string powerOverrideText = ReadProse(node, "powerOverrideText", path, id, source, errors, ref ok);
            string successText = ReadProse(node, "successText", path, id, source, errors, ref ok);
            string failText = ReadProse(node, "failText", path, id, source, errors, ref ok);

            if (!ok) return null;

            districtAffinity.Sort(StringComparer.Ordinal);
            tags.Sort(StringComparer.Ordinal);

            return new CivicEvent
            {
                Id = id,
                Severity = severity,
                Region = region,
                Trigger = trigger,
                Check = check,
                ActiveEffects = activeEffects,
                SuccessEffects = successEffects,
                FailureEffects = failureEffects,
                ActivePressure = activePressure,
                SuccessPressure = successPressure,
                FailurePressure = failurePressure,
                DistrictAffinity = districtAffinity,
                Tags = tags,
                Name = name,
                Description = description,
                IgnoreText = ignoreText,
                GoalText = goalText,
                PowerOverrideText = powerOverrideText,
                SuccessText = successText,
                FailText = failText
            };
        }

        // --- specs -------------------------------------------------------------------------------

        private static CheckSpec ReadCheck(JsonNode? node, string path, string id,
                                           CivicEventCatalogSource source, EngineTuning tuning,
                                           HashSet<string> featureSet, List<CatalogIssue> errors,
                                           List<CatalogIssue> warnings, ref bool ok)
        {
            var check = new CheckSpec();

            if (node == null)
            {
                errors.Add(Error(CatalogIssueCode.MalformedSpec, source.Name, id, path,
                    "every civic event needs a check; it is what a Goal response is scored against"));
                ok = false;
                return check;
            }

            if (node.Kind != JsonKind.Object)
            {
                errors.Add(Error(CatalogIssueCode.MalformedSpec, source.Name, id, path, "expected an object"));
                ok = false;
                return check;
            }

            WarnUnknownProperties(node, CheckKeys, source.Name, id, path, warnings);

            check.Spec = ReadSpec(Member(node, "spec"), path + ".spec", id, source, tuning, featureSet,
                                   errors, warnings, required: true, ref ok);

            JsonNode? relativeNode = Member(node, "relativeToBaseline");
            if (relativeNode != null)
            {
                if (relativeNode.Kind != JsonKind.Bool)
                {
                    errors.Add(Error(CatalogIssueCode.MalformedSpec, source.Name, id, path + ".relativeToBaseline",
                        "expected true or false"));
                    ok = false;
                }
                else
                {
                    check.RelativeToBaseline = relativeNode.Flag;

                    // A baseline is a recorded metric reading. Only a reading-shaped kind has one, so
                    // the flag on any other kind is authoring confusion rather than a request — kept
                    // as a warning because ignoring it changes nothing about what the check does.
                    if (check.RelativeToBaseline &&
                        check.Spec.Kind != TriggerKind.Metric && check.Spec.Kind != TriggerKind.Delta)
                    {
                        warnings.Add(Warn(CatalogIssueCode.BaselineOnNonMetricCheck, source.Name, id,
                            path + ".relativeToBaseline",
                            "only a metric or delta check has a baseline; the flag is ignored here"));
                    }

                    // An ERROR, not a warning, because it is provable rather than a judgement:
                    // StoryAssembler.Baseline returns null for every non-City scope, so this check
                    // resolves Unmeasurable on every save forever. It would score in neither half of
                    // the 2-of-3 and move the power balance by zero — an event that reads like a
                    // working goal and silently contributes nothing.
                    if (check.RelativeToBaseline && check.Spec.Scope != TriggerScope.City)
                    {
                        errors.Add(Error(CatalogIssueCode.BaselineCheckAtDistrictScope, source.Name, id,
                            path + ".relativeToBaseline",
                            "a relative check is city-scope only: nothing on StorySlot records which " +
                            "district the story landed on, so a district-scoped baseline is never " +
                            "captured and this check would resolve Unmeasurable forever"));
                        ok = false;
                    }
                }
            }

            return check;
        }

        private static TriggerSpec ReadSpec(JsonNode? node, string path, string id,
                                            CivicEventCatalogSource source, EngineTuning tuning,
                                            HashSet<string> featureSet, List<CatalogIssue> errors,
                                            List<CatalogIssue> warnings, bool required, ref bool ok)
        {
            var spec = new TriggerSpec();

            if (node == null)
            {
                if (required)
                {
                    errors.Add(Error(CatalogIssueCode.MalformedSpec, source.Name, id, path,
                        "expected a trigger specification"));
                    ok = false;
                }
                return spec;
            }

            if (node.Kind != JsonKind.Object)
            {
                errors.Add(Error(CatalogIssueCode.MalformedSpec, source.Name, id, path, "expected an object"));
                ok = false;
                return spec;
            }

            WarnUnknownProperties(node, SpecKeys, source.Name, id, path, warnings);

            // --- kind ------------------------------------------------------------------------
            TriggerKind kind;
            JsonNode? kindNode = Member(node, "kind");
            string kindText = kindNode != null && kindNode.Kind == JsonKind.String ? (kindNode.Text ?? "") : "";
            if (!TryParseKind(kindText, out kind))
            {
                errors.Add(Error(CatalogIssueCode.UnknownTriggerKind, source.Name, id, path + ".kind",
                    "expected \"metric\", \"delta\", \"unlock\", \"absent\" or \"manual\""));
                ok = false;
                return spec;
            }

            // "policy" parses — so the error names the real reason rather than "unknown kind" — and is
            // then refused. See CatalogIssueCode.PolicyTriggerUnsupported: nothing writes
            // CitySnapshot.ActivePolicyIds, so a policy trigger is permanently NotMet and an absent
            // policy trigger is permanently Met. Neither is authorable content.
            if (kind == TriggerKind.Policy)
            {
                errors.Add(Error(CatalogIssueCode.PolicyTriggerUnsupported, source.Name, id, path + ".kind",
                    "no sensor writes CitySnapshot.ActivePolicyIds, so a policy spec can never fire and " +
                    "an absent policy spec fires on every city forever; author a metric spec instead"));
                ok = false;
                return spec;
            }

            spec.Kind = kind;

            // --- scope -----------------------------------------------------------------------
            TriggerScope scope = TriggerScope.City;
            JsonNode? scopeNode = Member(node, "scope");
            if (scopeNode != null)
            {
                string scopeText = scopeNode.Kind == JsonKind.String ? (scopeNode.Text ?? "") : "";
                if (!TryParseScope(scopeText, out scope))
                {
                    errors.Add(Error(CatalogIssueCode.UnknownTriggerScope, source.Name, id, path + ".scope",
                        "expected \"city\", \"anyDistrict\" or \"allDistricts\""));
                    ok = false;
                    return spec;
                }
            }
            spec.Scope = scope;

            // --- comparison ------------------------------------------------------------------
            Comparison comparison = Comparison.GreaterThanOrEqual;
            JsonNode? comparisonNode = Member(node, "comparison");
            if (comparisonNode != null)
            {
                string comparisonText = comparisonNode.Kind == JsonKind.String ? (comparisonNode.Text ?? "") : "";
                if (!TryParseComparison(comparisonText, out comparison))
                {
                    errors.Add(Error(CatalogIssueCode.UnknownComparison, source.Name, id, path + ".comparison",
                        "expected \"lt\", \"lte\", \"gt\" or \"gte\""));
                    ok = false;
                }
            }
            spec.Comparison = comparison;

            string metricId = "";
            JsonNode? metricNode = Member(node, "metricId");
            if (metricNode != null && metricNode.Kind == JsonKind.String) metricId = metricNode.Text ?? "";
            spec.MetricId = metricId;

            switch (kind)
            {
                case TriggerKind.Manual:
                    // Nothing further to validate: a Manual spec reads no city state. It is also never
                    // pooled (see TriggerKind.Manual's remarks), so an authored event carrying one can
                    // never produce a story — worth a warning, since that is almost never intended.
                    if (metricId.Length != 0)
                    {
                        warnings.Add(Warn(CatalogIssueCode.UnknownProperty, source.Name, id, path + ".metricId",
                            "a manual spec reads no metric; the id is ignored"));
                    }
                    break;

                case TriggerKind.Metric:
                case TriggerKind.Delta:
                    ValidateMetricSpec(spec, path, id, source, tuning, errors, warnings, ref ok);
                    break;

                case TriggerKind.Unlock:
                    ValidateFeatureId(metricId, path, id, source, featureSet, errors, ref ok);
                    break;

                case TriggerKind.Absent:
                    // Absent negates whatever its id resolves to, and the evaluator resolves a registry
                    // metric as a negated threshold and anything else as set membership. So the id must
                    // resolve to exactly one of those two vocabularies, and an id in neither is the
                    // silent-forever-Met case this whole loader exists to refuse.
                    if (MetricRegistry.IsKnown(metricId, scope))
                    {
                        ValidateMetricSpec(spec, path, id, source, tuning, errors, warnings, ref ok);
                    }
                    else
                    {
                        ValidateFeatureId(metricId, path, id, source, featureSet, errors, ref ok);
                    }
                    break;
            }

            // --- threshold and window --------------------------------------------------------
            //
            // Read for every kind that compares a number. A missing threshold defaults to 0.0 in the
            // contract, and 0.0 is a real threshold rather than an obviously-absent one, so it is
            // required rather than defaulted here.
            if (kind == TriggerKind.Metric || kind == TriggerKind.Delta ||
                (kind == TriggerKind.Absent && MetricRegistry.IsKnown(metricId, scope)))
            {
                JsonNode? thresholdNode = Member(node, "threshold");
                if (thresholdNode == null || thresholdNode.Kind != JsonKind.Number || !IsFinite(thresholdNode.Number))
                {
                    errors.Add(Error(CatalogIssueCode.ThresholdNotFinite, source.Name, id, path + ".threshold",
                        "expected a finite number"));
                    ok = false;
                }
                else
                {
                    spec.Threshold = thresholdNode.Number;
                }
            }

            if (kind == TriggerKind.Delta)
            {
                // A delta over W months needs a sample W months back, and scheduler.snapshotRetention
                // is how many monthly samples are retained — so a window at or above it can never be
                // answered and would evaluate Unmeasurable for the life of every save. Read from
                // tuning rather than pinned to a literal, so a retention change re-checks the catalog
                // on the run that introduces it.
                int retention = tuning.Scheduler.SnapshotRetention;
                int maxWindow = retention > 1 ? retention - 1 : 1;

                int windowMonths = 0;
                JsonNode? windowNode = Member(node, "windowMonths");
                if (windowNode == null || !TryInt(windowNode, out windowMonths) ||
                    windowMonths < 1 || windowMonths > maxWindow)
                {
                    errors.Add(Error(CatalogIssueCode.WindowMonthsOutOfRange, source.Name, id,
                        path + ".windowMonths",
                        "a delta spec needs an integer window in 1.." +
                        maxWindow.ToString(CultureInfo.InvariantCulture) +
                        " (scheduler.snapshotRetention - 1)"));
                    ok = false;
                }
                else
                {
                    spec.WindowMonths = windowMonths;
                }
            }

            return spec;
        }

        /// <summary>
        /// Warns when a district-scoped check reads the same metric as its district-scoped trigger,
        /// which makes it answerable by a district that has nothing to do with the story.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>AnyDistrict</c> returns <c>Met</c> on the first district that clears the bar, walking
        /// all of them, and no district id survives onto <c>StorySlot</c> — so "some district is
        /// bad" followed by "some district is fine on the same metric" is answered by the healthiest
        /// block in the city, usually on the month the story opens.
        /// </para>
        /// <para>
        /// Restricted to the same <c>MetricId</c> deliberately. A district check on a <i>different</i>
        /// metric is a genuinely different question and may well be intended, so flagging it would
        /// train authors to ignore this warning — which is the failure mode a noisy check has.
        /// </para>
        /// </remarks>
        private static void WarnIfCheckIsNotBoundToTrigger(TriggerSpec trigger, CheckSpec check,
                                                           string path, string id,
                                                           CivicEventCatalogSource source,
                                                           List<CatalogIssue> warnings)
        {
            if (trigger == null || check == null || check.Spec == null) return;

            TriggerSpec spec = check.Spec;
            if (spec.Scope != TriggerScope.AnyDistrict) return;
            if (trigger.Scope != TriggerScope.AnyDistrict && trigger.Scope != TriggerScope.AllDistricts) return;
            if (string.CompareOrdinal(trigger.MetricId, spec.MetricId) != 0) return;
            if (string.IsNullOrEmpty(spec.MetricId)) return;

            warnings.Add(Warn(CatalogIssueCode.DistrictCheckNotBoundToTrigger, source.Name, id,
                path + ".check.spec.scope",
                "this check reads '" + spec.MetricId + "' at anyDistrict scope, the same metric the " +
                "trigger reads, so it is satisfied by whichever district already clears it rather " +
                "than by the one the story is about; use allDistricts, or a city-scope relative check"));
        }

        /// <summary>
        /// Warns when a district check's threshold is tighter than its trigger's, leaving a band of
        /// districts that never contributed to the trigger but can still fail the check.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Only a genuine numeric gap is reported, so an exact complement passes whatever the
        /// strictness of the two comparisons — <c>&gt;= T</c> against <c>&lt; T</c> and <c>&gt; T</c>
        /// against <c>&lt;= T</c> are both silent. The rule cares about the band, not the boundary.
        /// </para>
        /// <para>
        /// Restricted to a district trigger, a district check, the same metric, and both specs being
        /// plain readings. A <c>delta</c> pair would need the two windows normalised before the
        /// thresholds could be compared at all, and comparing them unnormalised would produce
        /// confident nonsense — that case is left to review.
        /// </para>
        /// </remarks>
        private static void WarnIfCheckLeavesATrapBand(TriggerSpec trigger, CheckSpec check,
                                                       string path, string id,
                                                       CivicEventCatalogSource source,
                                                       List<CatalogIssue> warnings)
        {
            if (trigger == null || check == null || check.Spec == null) return;

            TriggerSpec spec = check.Spec;
            if (trigger.Kind != TriggerKind.Metric || spec.Kind != TriggerKind.Metric) return;
            if (check.RelativeToBaseline) return; // the bar moves with the city; no fixed band exists
            if (trigger.Scope == TriggerScope.City || spec.Scope == TriggerScope.City) return;
            if (string.CompareOrdinal(trigger.MetricId, spec.MetricId) != 0) return;
            if (string.IsNullOrEmpty(spec.MetricId)) return;

            bool triggerOnHigh = trigger.Comparison == Comparison.GreaterThan ||
                                 trigger.Comparison == Comparison.GreaterThanOrEqual;
            bool checkWantsLow = spec.Comparison == Comparison.LessThan ||
                                 spec.Comparison == Comparison.LessThanOrEqual;

            // The two must oppose for this to be a "fix it" check at all; anything else is a shape
            // this rule has no opinion about.
            if (triggerOnHigh != checkWantsLow) return;

            bool hasBand = triggerOnHigh
                ? spec.Threshold < trigger.Threshold   // districts in [check, trigger) never triggered
                : spec.Threshold > trigger.Threshold;  // mirrored, for a low-is-bad trigger

            if (!hasBand) return;

            warnings.Add(Warn(CatalogIssueCode.CheckThresholdLeavesTrapBand, source.Name, id,
                path + ".check.spec.threshold",
                "the check demands " + Describe(spec.Threshold) + " while the trigger fires at " +
                Describe(trigger.Threshold) + ", so a district between the two never contributed to " +
                "the trigger, is invisible in the description, and fails the story anyway; use the " +
                "trigger's own threshold unless the wider ask is deliberate"));
        }

        /// <summary>
        /// Warns when a <c>delta</c> check reads back further than the story has existed.
        /// </summary>
        /// <remarks>
        /// <b><c>cycleMonths</c> is the cadence, not the story's life</b>, and the two differ by one:
        /// <c>StoryAssembler.NewStory</c> opens at M and resolves at M + <c>(cycleMonths - 1)</c>. A
        /// window wider than that scores the player on months that predate their decision.
        /// </remarks>
        private static void WarnIfCheckOutrunsStoryLife(CheckSpec check, EngineTuning tuning,
                                                        string path, string id,
                                                        CivicEventCatalogSource source,
                                                        List<CatalogIssue> warnings)
        {
            if (check == null || check.Spec == null) return;
            if (check.Spec.Kind != TriggerKind.Delta) return;
            if (tuning == null || tuning.Stories == null) return;

            int life = tuning.Stories.CycleMonths - 1;
            if (life < 1) life = 1;
            if (check.Spec.WindowMonths <= life) return;

            warnings.Add(Warn(CatalogIssueCode.CheckWindowOutrunsStoryLife, source.Name, id,
                path + ".check.spec.windowMonths",
                "reads back " + check.Spec.WindowMonths.ToString(CultureInfo.InvariantCulture) +
                " months, but a story lives " + life.ToString(CultureInfo.InvariantCulture) +
                " (stories.cycleMonths - 1), so part of the verdict was decided before the player " +
                "saw the card"));
        }

        /// <summary>
        /// The registry check, plus the census gate. Shared by <c>metric</c>, <c>delta</c> and the
        /// registry-resolving half of <c>absent</c>.
        /// </summary>
        private static void ValidateMetricSpec(TriggerSpec spec, string path, string id,
                                               CivicEventCatalogSource source, EngineTuning tuning,
                                               List<CatalogIssue> errors, List<CatalogIssue> warnings,
                                               ref bool ok)
        {
            if (!MetricRegistry.IsKnown(spec.MetricId, spec.Scope))
            {
                errors.Add(Error(CatalogIssueCode.UnknownMetricId, source.Name, id, path + ".metricId",
                    "'" + spec.MetricId + "' is not a metric the registry can read at scope " +
                    ScopeKey(spec.Scope) + "; see MetricRegistry.CityMetricIds / DistrictMetricIds"));
                ok = false;
                return;
            }

            // A threshold above what the sensor can ever report says nothing about the city: a gte
            // can never be met, a lt is met always. Only a Metric spec is checked — a delta is a
            // change, and a change may legitimately exceed the level's ceiling.
            if (spec.Kind == TriggerKind.Metric)
            {
                double? ceiling = AttainableMaximum(spec.MetricId);
                if (ceiling.HasValue && spec.Threshold > ceiling.Value)
                {
                    warnings.Add(Warn(CatalogIssueCode.ThresholdAboveAttainableMaximum, source.Name, id,
                        path + ".threshold",
                        Describe(spec.Threshold) + " is above the highest value '" + spec.MetricId +
                        "' can report (" + Describe(ceiling.Value) + "): its sensor hard-zeroes the " +
                        "channels the game does not expose, so the mean cannot reach 1.0"));
                }
            }

            // The census gate. Delta is permitted, absolute is not — the units are unresolved, and a
            // delta survives that in direction where an absolute threshold does not survive it at all.
            if (spec.Kind != TriggerKind.Delta && IsCensusGated(spec.MetricId))
            {
                errors.Add(Error(CatalogIssueCode.CensusGatedMetricNeedsDelta, source.Name, id,
                    path + ".metricId",
                    "'" + spec.MetricId + "' has unresolved units until wave 1's AGORA-STATCOLLECTION " +
                    "gate is walked (per in-game day, or cumulative since founding), so only a delta " +
                    "spec may name it"));
                ok = false;
            }
        }

        private static void ValidateFeatureId(string metricId, string path, string id,
                                              CivicEventCatalogSource source, HashSet<string> featureSet,
                                              List<CatalogIssue> errors, ref bool ok)
        {
            if (metricId.Length != 0 && featureSet.Contains(metricId)) return;

            errors.Add(Error(CatalogIssueCode.UnlockIdNotDeclared, source.Name, id, path + ".metricId",
                metricId.Length == 0
                    ? "an unlock or feature-absent spec needs a metricId"
                    : "'" + metricId + "' is not declared in any document's featureIds allow-list; feature " +
                      "ids are raw prefab names that nothing can check against the game, so an undeclared " +
                      "one would read as never-unlocked and, under absent, fire on every city forever"));
            ok = false;
        }

        private static bool IsCensusGated(string metricId)
        {
            for (int i = 0; i < CensusGatedMetricIds.Count; i++)
            {
                if (string.CompareOrdinal(CensusGatedMetricIds[i], metricId) == 0) return true;
            }
            return false;
        }

        // --- effects, pressures and lists --------------------------------------------------------

        private static List<string> ReadEffectList(JsonNode node, string key, string parentPath, string id,
                                                   CivicEventCatalogSource source, EngineTuning tuning,
                                                   List<CatalogIssue> errors, List<CatalogIssue> warnings,
                                                   ref bool ok)
        {
            var effects = new List<string>();
            string path = parentPath + "." + key;

            JsonNode? listNode = Member(node, key);
            if (listNode == null) return effects;

            if (listNode.Kind != JsonKind.Array || listNode.Items == null)
            {
                errors.Add(Error(CatalogIssueCode.MalformedEffectList, source.Name, id, path,
                    "expected an array of effect id strings"));
                ok = false;
                return effects;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < listNode.Items.Count; i++)
            {
                string itemPath = path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                JsonNode item = listNode.Items[i];

                string? text = item.Kind == JsonKind.String ? item.Text : null;
                if (string.IsNullOrEmpty(text))
                {
                    errors.Add(Error(CatalogIssueCode.MalformedEffectList, source.Name, id, itemPath,
                        "expected a non-empty effect id string"));
                    ok = false;
                    continue;
                }

                string effectId = text!;

                EffectCap cap;
                if (!tuning.Effects.TryGetEffect(effectId, out cap))
                {
                    errors.Add(Error(CatalogIssueCode.UnknownEffectId, source.Name, id, itemPath,
                        "'" + effectId + "' is not in the effect palette registry (effects.perEffect)"));
                    ok = false;
                    continue;
                }

                if (!seen.Add(effectId))
                {
                    warnings.Add(Warn(CatalogIssueCode.DuplicateEffectId, source.Name, id, itemPath,
                        "'" + effectId + "' is listed twice; the duplicate is dropped"));
                    continue;
                }

                effects.Add(effectId);
            }

            // Sorted, because CivicEvent declares these lists sorted by id and the story effect
            // builder walks them in order. Authoring order is not meaningful here.
            effects.Sort(StringComparer.Ordinal);
            return effects;
        }

        private static IssuePosition ReadIssuePressure(JsonNode node, string key, string parentPath, string id,
                                                       CivicEventCatalogSource source,
                                                       List<CatalogIssue> errors, List<CatalogIssue> warnings,
                                                       ref bool ok)
        {
            IssuePosition pressure = IssuePosition.Centre;
            string path = parentPath + "." + key;

            JsonNode? pressureNode = Member(node, key);
            if (pressureNode == null) return pressure;

            if (pressureNode.Kind != JsonKind.Object)
            {
                errors.Add(Error(CatalogIssueCode.MalformedIssuePressure, source.Name, id, path,
                    "expected an object of per-issue numbers in [-1, +1]"));
                ok = false;
                return pressure;
            }

            WarnUnknownProperties(pressureNode, IssueKeys, source.Name, id, path, warnings);

            // Issues.All order, never Enum.GetValues: the fold order is what makes the result bit-stable.
            for (int i = 0; i < Issues.All.Count; i++)
            {
                Issue issue = Issues.All[i];
                string issueKey = Issues.ToKey(issue);
                JsonNode? component = Member(pressureNode, issueKey);
                if (component == null) continue; // an unstated issue is simply not pressed

                string componentPath = path + "." + issueKey;
                if (component.Kind != JsonKind.Number || !IsFinite(component.Number))
                {
                    errors.Add(Error(CatalogIssueCode.MalformedIssuePressure, source.Name, id, componentPath,
                        "expected a finite number"));
                    ok = false;
                    continue;
                }

                if (component.Number < -1.0 || component.Number > 1.0)
                {
                    errors.Add(Error(CatalogIssueCode.IssuePressureOutOfRange, source.Name, id, componentPath,
                        Describe(component.Number) + " is outside [-1, +1]"));
                    ok = false;
                    continue;
                }

                pressure = pressure.With(issue, component.Number);
            }

            return pressure;
        }

        /// <summary>
        /// Warns when an outcome pressure points the opposite way on any issue from the event's own
        /// active pressure.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Walks <c>Issues.All</c> rather than reflecting over the struct, for the same reason the
        /// pressure reader does: the fold order is what makes the result bit-stable.
        /// </para>
        /// <para>
        /// Only a genuine sign flip is reported — a zero on either side is not a flip, because
        /// dropping an issue entirely at resolution is a legitimate way to say "this stopped
        /// mattering". Magnitude is not policed at all: louder on failure and quieter on success is
        /// the expected shape, but the reverse can be authored deliberately and this loader has no
        /// basis to judge it.
        /// </para>
        /// </remarks>
        private static void WarnOnPressureSignFlip(IssuePosition active, IssuePosition outcome,
                                                   string key, string parentPath, string id,
                                                   CivicEventCatalogSource source,
                                                   List<CatalogIssue> warnings)
        {
            for (int i = 0; i < Issues.All.Count; i++)
            {
                Issue issue = Issues.All[i];
                double a = active[issue];
                double o = outcome[issue];

                if (a == 0.0 || o == 0.0) continue;
                if ((a > 0.0) == (o > 0.0)) continue;

                warnings.Add(Warn(CatalogIssueCode.PressureSignFlip, source.Name, id,
                    parentPath + "." + key + "." + Issues.ToKey(issue),
                    "points the opposite way from activePressure (" + Describe(a) + " -> " +
                    Describe(o) + "); pressures are salience, not credit, so a flipped sign moves " +
                    "voters to the opposite pole rather than releasing the issue"));
            }
        }

        private static List<string> ReadStringList(JsonNode node, string key, string parentPath, string id,
                                                   CivicEventCatalogSource source, CatalogIssueCode code,
                                                   List<CatalogIssue> errors, ref bool ok)
        {
            var values = new List<string>();
            string path = parentPath + "." + key;

            JsonNode? listNode = Member(node, key);
            if (listNode == null) return values;

            if (listNode.Kind != JsonKind.Array || listNode.Items == null)
            {
                errors.Add(Error(code, source.Name, id, path, "expected an array of strings"));
                ok = false;
                return values;
            }

            for (int i = 0; i < listNode.Items.Count; i++)
            {
                string itemPath = path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                JsonNode item = listNode.Items[i];
                string? text = item.Kind == JsonKind.String ? item.Text : null;
                if (string.IsNullOrEmpty(text))
                {
                    errors.Add(Error(code, source.Name, id, itemPath, "expected a non-empty string"));
                    ok = false;
                    continue;
                }

                values.Add(text!);
            }

            return values;
        }

        private static string ReadProse(JsonNode node, string key, string parentPath, string id,
                                        CivicEventCatalogSource source, List<CatalogIssue> errors, ref bool ok)
        {
            JsonNode? proseNode = Member(node, key);
            string text = proseNode != null && proseNode.Kind == JsonKind.String ? (proseNode.Text ?? "") : "";

            if (text.Trim().Length == 0)
            {
                errors.Add(Error(CatalogIssueCode.MissingProse, source.Name, id, parentPath + "." + key,
                    "every civic event needs all seven prose fields; each one is rendered on a surface " +
                    "the player acts from"));
                ok = false;
            }

            return text;
        }

        // --- parsing helpers ---------------------------------------------------------------------

        private static JsonNode? Member(JsonNode node, string key)
        {
            JsonNode child;
            if (node.Members != null && node.Members.TryGetValue(key, out child)) return child;
            return null;
        }

        private static void WarnUnknownProperties(JsonNode node, string[] known, string sourceName,
                                                  string eventId, string path, List<CatalogIssue> warnings)
        {
            if (node.Members == null) return;

            // Sorted, never raw dictionary order: the warning list is part of the loader's output and
            // must not vary with insertion order.
            var keys = new List<string>(node.Members.Count);
            foreach (KeyValuePair<string, JsonNode> member in node.Members) keys.Add(member.Key);
            keys.Sort(StringComparer.Ordinal);

            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                if (key.Length > 0 && key[0] == '_') continue; // "_comment" and friends
                if (Contains(known, key)) continue;

                warnings.Add(Warn(CatalogIssueCode.UnknownProperty, sourceName, eventId,
                    path.Length == 0 ? key : path + "." + key,
                    "not declared by civic_events.schema.json; ignored"));
            }
        }

        private static bool Contains(string[] values, string value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (string.CompareOrdinal(values[i], value) == 0) return true;
            }
            return false;
        }

        private static bool TryInt(JsonNode node, out int value)
        {
            value = 0;
            if (node.Kind != JsonKind.Number) return false;

            double number = node.Number;
            if (!IsFinite(number)) return false;
            if (number != Math.Floor(number)) return false;
            if (number < int.MinValue || number > int.MaxValue) return false;

            value = (int)number;
            return true;
        }

        /// <summary>netstandard2.0 has no <c>double.IsFinite</c>.</summary>
        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool IsKebabCase(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool allowed = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-';
                if (!allowed) return false;
            }

            return true;
        }

        private static bool TryParseRegion(string text, out EventRegion region)
        {
            if (string.CompareOrdinal(text, "eu") == 0) { region = EventRegion.Eu; return true; }
            if (string.CompareOrdinal(text, "na") == 0) { region = EventRegion.Na; return true; }
            if (string.CompareOrdinal(text, "global") == 0) { region = EventRegion.Global; return true; }

            region = EventRegion.Global;
            return false;
        }

        private static bool TryParseKind(string text, out TriggerKind kind)
        {
            if (string.CompareOrdinal(text, "metric") == 0) { kind = TriggerKind.Metric; return true; }
            if (string.CompareOrdinal(text, "delta") == 0) { kind = TriggerKind.Delta; return true; }
            if (string.CompareOrdinal(text, "unlock") == 0) { kind = TriggerKind.Unlock; return true; }
            if (string.CompareOrdinal(text, "policy") == 0) { kind = TriggerKind.Policy; return true; }
            if (string.CompareOrdinal(text, "absent") == 0) { kind = TriggerKind.Absent; return true; }
            if (string.CompareOrdinal(text, "manual") == 0) { kind = TriggerKind.Manual; return true; }

            kind = TriggerKind.Manual;
            return false;
        }

        private static bool TryParseComparison(string text, out Comparison comparison)
        {
            if (string.CompareOrdinal(text, "lt") == 0) { comparison = Comparison.LessThan; return true; }
            if (string.CompareOrdinal(text, "lte") == 0) { comparison = Comparison.LessThanOrEqual; return true; }
            if (string.CompareOrdinal(text, "gt") == 0) { comparison = Comparison.GreaterThan; return true; }
            if (string.CompareOrdinal(text, "gte") == 0) { comparison = Comparison.GreaterThanOrEqual; return true; }

            comparison = Comparison.GreaterThanOrEqual;
            return false;
        }

        private static bool TryParseScope(string text, out TriggerScope scope)
        {
            if (string.CompareOrdinal(text, "city") == 0) { scope = TriggerScope.City; return true; }
            if (string.CompareOrdinal(text, "anyDistrict") == 0) { scope = TriggerScope.AnyDistrict; return true; }
            if (string.CompareOrdinal(text, "allDistricts") == 0) { scope = TriggerScope.AllDistricts; return true; }

            scope = TriggerScope.City;
            return false;
        }

        private static string ScopeKey(TriggerScope scope)
        {
            switch (scope)
            {
                case TriggerScope.AnyDistrict: return "'anyDistrict'";
                case TriggerScope.AllDistricts: return "'allDistricts'";
                default: return "'city'";
            }
        }

        private static string Describe(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static string[] BuildIssueKeys()
        {
            var keys = new string[Issues.All.Count];
            for (int i = 0; i < Issues.All.Count; i++) keys[i] = Issues.ToKey(Issues.All[i]);
            return keys;
        }

        private static IReadOnlyList<string> SortedOrdinal(string[] values)
        {
            var list = new List<string>(values);
            list.Sort(StringComparer.Ordinal);
            return list.AsReadOnly();
        }

        private static CatalogIssue Error(CatalogIssueCode code, string sourceName, string eventId,
                                          string path, string message) =>
            new CatalogIssue(CatalogIssueSeverity.Error, code, sourceName, eventId, path, message);

        private static CatalogIssue Warn(CatalogIssueCode code, string sourceName, string eventId,
                                         string path, string message) =>
            new CatalogIssue(CatalogIssueSeverity.Warning, code, sourceName, eventId, path, message);
    }
}
