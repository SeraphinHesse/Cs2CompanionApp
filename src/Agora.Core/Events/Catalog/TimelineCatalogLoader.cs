using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Events.Catalog
{
    /// <summary>
    /// Reads and validates <c>timeline_*.json</c> (<c>politicsmodplan.md</c> §6).
    ///
    /// <para>
    /// The loader is pure: it takes document text and an <see cref="EngineTuning"/>, and returns
    /// events plus findings. It never opens a file, never reads a clock, and never draws a random
    /// number — the catalog is content, and loading content must not be able to change history.
    /// </para>
    ///
    /// <para>
    /// Validation exists so that a broken catalog fails at the schema suite rather than as a silently
    /// clamped effect three in-game decades later. It rejects, per <c>data/CLAUDE.md</c> rule 2 and
    /// the timeline schema: an <c>effectId</c> absent from the packet-14 effect registry, a magnitude
    /// or duration outside that effect's declared cap, a malformed date, and a duplicate event id.
    /// </para>
    /// </summary>
    /// <remarks>
    /// AGORA-SEAM(§14.2): timeline jitter (fixed real dates vs seeded ±6 months) is an open decision.
    /// The loader therefore stores the authored date verbatim and never touches
    /// <c>StreamNames.EventJitter</c>. When the decision closes, jitter belongs in the scheduler that
    /// consumes this catalog, not here — the loaded catalog must stay a pure function of its text.
    ///
    /// AGORA-SEAM(§14.4): the post-2026 procedural split is an open decision. This loader handles
    /// curated entries only; <c>catalog.procedural*</c> is read by nothing here.
    /// </remarks>
    public static class TimelineCatalogLoader
    {
        /// <summary>
        /// The only <c>schemaVersion</c> this loader accepts. A contract version, not a coefficient —
        /// it lives with the code that understands the shape, and changing it goes through
        /// <c>/schema-change</c> (non-negotiable #9).
        /// </summary>
        public const int SupportedSchemaVersion = 1;

        private static readonly string[] RootKeys = { "schemaVersion", "events" };

        private static readonly string[] EventKeys =
        {
            "id", "dateISO", "region", "title", "severity", "durationMonths",
            "effects", "headlineBrief", "tags", "issuePressure"
        };

        // districtId is listed as "known" so a catalog that sets it gets one precise error rather than
        // an error plus an unknown-property warning.
        private static readonly string[] EffectKeys =
        {
            "effectId", "scope", "magnitude", "durationMonths", "districtId"
        };

        private static readonly string[] IssueKeys = BuildIssueKeys();

        /// <summary>Days per month. February takes 29 in every year: <see cref="SimDate"/> states the
        /// calendar is regular, so the validator must not encode a leap rule the engine does not have.</summary>
        private static readonly int[] DaysInMonth = { 31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

        /// <summary>Validates one document.</summary>
        public static TimelineCatalogLoadResult Load(string sourceName, string json, EngineTuning tuning) =>
            Load(new[] { new TimelineCatalogSource(sourceName, json) }, tuning);

        /// <summary>
        /// Validates one document read from a <see cref="TextReader"/>. The reader is the caller's —
        /// Core does not own streams and does not dispose it.
        /// </summary>
        public static TimelineCatalogLoadResult LoadFrom(string sourceName, TextReader reader, EngineTuning tuning)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            return Load(sourceName, reader.ReadToEnd(), tuning);
        }

        /// <summary>
        /// Validates every document as one catalog: ids are unique across documents, and the resulting
        /// event list is sorted by date then id.
        /// </summary>
        /// <remarks>
        /// Sources are processed in name order, not in the order the caller enumerated them, so which
        /// copy of a duplicated id survives cannot depend on a directory listing.
        /// </remarks>
        public static TimelineCatalogLoadResult Load(IEnumerable<TimelineCatalogSource> sources, EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            var errors = new List<CatalogIssue>();
            var warnings = new List<CatalogIssue>();
            var accepted = new List<TimelineEvent>();
            var seenIds = new Dictionary<string, string>(StringComparer.Ordinal);
            int rejected = 0;

            List<TimelineCatalogSource> ordered = OrderSources(sources, warnings);

            for (int i = 0; i < ordered.Count; i++)
            {
                ReadSource(ordered[i], tuning, seenIds, accepted, errors, warnings, ref rejected);
            }

            accepted.Sort(CompareEvents);
            return new TimelineCatalogLoadResult(new TimelineCatalog(accepted), errors, warnings, rejected);
        }

        // --- documents ---------------------------------------------------------------------------

        private static List<TimelineCatalogSource> OrderSources(IEnumerable<TimelineCatalogSource> sources,
                                                               List<CatalogIssue> warnings)
        {
            var list = new List<TimelineCatalogSource>();
            if (sources != null)
            {
                foreach (TimelineCatalogSource source in sources)
                {
                    if (source != null) list.Add(source);
                }
            }

            // Decorate with the original index so equal names keep their input order: List<T>.Sort is
            // not stable, and an unstable tie-break would make the result depend on element count.
            var decorated = new List<KeyValuePair<int, TimelineCatalogSource>>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                decorated.Add(new KeyValuePair<int, TimelineCatalogSource>(i, list[i]));
            }

            decorated.Sort((a, b) =>
            {
                int byName = string.CompareOrdinal(a.Value.Name, b.Value.Name);
                return byName != 0 ? byName : a.Key.CompareTo(b.Key);
            });

            var result = new List<TimelineCatalogSource>(decorated.Count);
            for (int i = 0; i < decorated.Count; i++)
            {
                TimelineCatalogSource source = decorated[i].Value;
                if (i > 0 && string.CompareOrdinal(source.Name, decorated[i - 1].Value.Name) == 0)
                {
                    warnings.Add(Warn(CatalogIssueCode.DuplicateSourceName, source.Name, "", "",
                        "two sources share this name; findings from them are indistinguishable"));
                }

                result.Add(source);
            }

            return result;
        }

        private static void ReadSource(TimelineCatalogSource source, EngineTuning tuning,
                                       Dictionary<string, string> seenIds, List<TimelineEvent> accepted,
                                       List<CatalogIssue> errors, List<CatalogIssue> warnings,
                                       ref int rejected)
        {
            JsonNode root;
            try
            {
                root = TuningJsonParser.Parse(source.Json);
            }
            catch (Exception ex) when (ex is TuningFormatException || ex is FormatException || ex is OverflowException)
            {
                // A corrupt catalog must not take the save down: report and contribute nothing (#7).
                errors.Add(Error(CatalogIssueCode.MalformedJson, source.Name, "", "",
                    "not valid JSON: " + ex.Message));
                return;
            }

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
                    "expected an array of events"));
                return;
            }

            for (int i = 0; i < eventsNode.Items.Count; i++)
            {
                TimelineEvent? loaded = ReadEvent(eventsNode.Items[i], i, source, tuning, seenIds, errors, warnings);
                if (loaded == null) rejected++;
                else accepted.Add(loaded);
            }
        }

        // --- events ------------------------------------------------------------------------------

        private static TimelineEvent? ReadEvent(JsonNode node, int index, TimelineCatalogSource source,
                                                EngineTuning tuning, Dictionary<string, string> seenIds,
                                                List<CatalogIssue> errors, List<CatalogIssue> warnings)
        {
            string path = "events[" + index.ToString(CultureInfo.InvariantCulture) + "]";

            if (node.Kind != JsonKind.Object)
            {
                errors.Add(Error(CatalogIssueCode.EventNotObject, source.Name, "", path,
                    "expected an object"));
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
                    "every event needs a non-empty id"));
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

            // --- date ------------------------------------------------------------------------
            SimDate date = default(SimDate);
            JsonNode? dateNode = Member(node, "dateISO");
            if (dateNode == null || dateNode.Kind != JsonKind.String || !TryParseIsoDate(dateNode.Text, out date))
            {
                errors.Add(Error(CatalogIssueCode.MalformedDate, source.Name, id, path + ".dateISO",
                    "expected a real calendar date as \"YYYY-MM-DD\""));
                ok = false;
            }
            else if (date.Year < tuning.Catalog.StartYear || date.Year > tuning.Catalog.CatalogEndYear)
            {
                warnings.Add(Warn(CatalogIssueCode.DateOutsideCatalogWindow, source.Name, id, path + ".dateISO",
                    date.ToString() + " falls outside catalog.startYear..catalog.catalogEndYear (" +
                    tuning.Catalog.StartYear.ToString(CultureInfo.InvariantCulture) + ".." +
                    tuning.Catalog.CatalogEndYear.ToString(CultureInfo.InvariantCulture) +
                    "); it can never fire"));
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

            // --- title -----------------------------------------------------------------------
            string title = "";
            JsonNode? titleNode = Member(node, "title");
            if (titleNode != null && titleNode.Kind == JsonKind.String) title = titleNode.Text ?? "";
            if (title.Trim().Length == 0)
            {
                errors.Add(Error(CatalogIssueCode.MissingTitle, source.Name, id, path + ".title",
                    "expected a short factual title"));
                ok = false;
            }

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

            // --- durationMonths --------------------------------------------------------------
            // There is no separate ceiling key for an event's own lifetime, so it shares the catalog's
            // effect duration ceiling. Both are "how long may a catalog entry stay live".
            int durationCeiling = tuning.Catalog.EffectDurationCapMonths;
            int durationMonths = 0;
            JsonNode? durationNode = Member(node, "durationMonths");
            if (durationNode == null || !TryInt(durationNode, out durationMonths) || durationMonths < 0 ||
                (durationCeiling > 0 && durationMonths > durationCeiling))
            {
                errors.Add(Error(CatalogIssueCode.DurationOutOfRange, source.Name, id, path + ".durationMonths",
                    "expected an integer in 0.." + durationCeiling.ToString(CultureInfo.InvariantCulture) +
                    " (catalog.effectDurationCapMonths)"));
                ok = false;
                durationMonths = 0;
            }

            // --- headlineBrief ---------------------------------------------------------------
            string brief = "";
            JsonNode? briefNode = Member(node, "headlineBrief");
            if (briefNode != null && briefNode.Kind == JsonKind.String) brief = briefNode.Text ?? "";
            if (brief.Trim().Length == 0)
            {
                errors.Add(Error(CatalogIssueCode.MissingHeadlineBrief, source.Name, id, path + ".headlineBrief",
                    "expected a terse factual brief; it is the prompt input the article is written from"));
                ok = false;
            }

            // --- tags ------------------------------------------------------------------------
            var tags = new List<string>();
            JsonNode? tagsNode = Member(node, "tags");
            if (tagsNode != null)
            {
                if (tagsNode.Kind != JsonKind.Array || tagsNode.Items == null)
                {
                    errors.Add(Error(CatalogIssueCode.MalformedTags, source.Name, id, path + ".tags",
                        "expected an array of strings"));
                    ok = false;
                }
                else
                {
                    for (int j = 0; j < tagsNode.Items.Count; j++)
                    {
                        string tagPath = path + ".tags[" + j.ToString(CultureInfo.InvariantCulture) + "]";
                        JsonNode item = tagsNode.Items[j];
                        if (item.Kind != JsonKind.String || item.Text == null)
                        {
                            errors.Add(Error(CatalogIssueCode.MalformedTags, source.Name, id, tagPath,
                                "expected a string"));
                            ok = false;
                            continue;
                        }

                        if (!IsKebabCase(item.Text))
                        {
                            warnings.Add(Warn(CatalogIssueCode.MalformedTag, source.Name, id, tagPath,
                                "'" + item.Text + "' is not lowercase kebab-case; kept as authored"));
                        }

                        tags.Add(item.Text);
                    }
                }
            }

            // --- issuePressure ---------------------------------------------------------------
            IssuePosition pressure = ReadIssuePressure(node, path, id, source, errors, warnings, ref ok);

            // --- effects ---------------------------------------------------------------------
            var effects = new List<TimelineEventEffect>();
            JsonNode? effectsNode = Member(node, "effects");
            if (effectsNode != null)
            {
                if (effectsNode.Kind != JsonKind.Array || effectsNode.Items == null)
                {
                    errors.Add(Error(CatalogIssueCode.EffectsNotArray, source.Name, id, path + ".effects",
                        "expected an array of effect requests"));
                    ok = false;
                }
                else
                {
                    for (int j = 0; j < effectsNode.Items.Count; j++)
                    {
                        string effectPath = path + ".effects[" + j.ToString(CultureInfo.InvariantCulture) + "]";
                        if (!ReadEffect(effectsNode.Items[j], effectPath, id, severity, source, tuning,
                                        effects, errors, warnings))
                        {
                            // One bad effect rejects the whole event. Loading it with the effect
                            // quietly dropped would ship an event that means something different from
                            // what was authored.
                            ok = false;
                        }
                    }
                }
            }

            if (!ok) return null;

            return new TimelineEvent
            {
                SchemaVersion = SupportedSchemaVersion,
                Id = id,
                Date = date,
                Region = region,
                Origin = EventOrigin.Catalog,
                Title = title,
                Severity = severity,
                DurationMonths = durationMonths,
                Effects = effects,
                HeadlineBrief = brief,
                Tags = tags,
                IssuePressure = pressure,
                ArchetypeId = "",
                FiredDate = null,
                ExpiresDate = null,
                LocalAngle = ""
            };
        }

        private static IssuePosition ReadIssuePressure(JsonNode node, string path, string id,
                                                       TimelineCatalogSource source,
                                                       List<CatalogIssue> errors, List<CatalogIssue> warnings,
                                                       ref bool ok)
        {
            IssuePosition pressure = IssuePosition.Centre;

            JsonNode? pressureNode = Member(node, "issuePressure");
            if (pressureNode == null) return pressure;

            string pressurePath = path + ".issuePressure";
            if (pressureNode.Kind != JsonKind.Object)
            {
                errors.Add(Error(CatalogIssueCode.MalformedIssuePressure, source.Name, id, pressurePath,
                    "expected an object of per-issue numbers in [-1, +1]"));
                ok = false;
                return pressure;
            }

            WarnUnknownProperties(pressureNode, IssueKeys, source.Name, id, pressurePath, warnings);

            // Issues.All order, never Enum.GetValues: the sum order is what makes the result bit-stable.
            for (int i = 0; i < Issues.All.Count; i++)
            {
                Issue issue = Issues.All[i];
                string key = Issues.ToKey(issue);
                JsonNode? component = Member(pressureNode, key);
                if (component == null) continue; // an unstated issue is simply not pressed

                string componentPath = pressurePath + "." + key;
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

        // --- effects -----------------------------------------------------------------------------

        private static bool ReadEffect(JsonNode node, string path, string eventId, int severity,
                                       TimelineCatalogSource source, EngineTuning tuning,
                                       List<TimelineEventEffect> into,
                                       List<CatalogIssue> errors, List<CatalogIssue> warnings)
        {
            if (node.Kind != JsonKind.Object)
            {
                errors.Add(Error(CatalogIssueCode.EffectNotObject, source.Name, eventId, path,
                    "expected an object"));
                return false;
            }

            WarnUnknownProperties(node, EffectKeys, source.Name, eventId, path, warnings);

            bool ok = true;

            if (Member(node, "districtId") != null)
            {
                errors.Add(Error(CatalogIssueCode.DistrictIdNotAllowed, source.Name, eventId, path + ".districtId",
                    "catalog effects never name a district; the scheduler picks the target deterministically"));
                ok = false;
            }

            // --- effectId against the packet-14 registry (the closed palette) ------------------
            string effectId = "";
            JsonNode? idNode = Member(node, "effectId");
            if (idNode != null && idNode.Kind == JsonKind.String) effectId = idNode.Text ?? "";

            EffectCap cap = default(EffectCap);
            bool known = false;
            if (effectId.Length == 0)
            {
                errors.Add(Error(CatalogIssueCode.UnknownEffectId, source.Name, eventId, path + ".effectId",
                    "every effect needs a non-empty effectId"));
                ok = false;
            }
            else if (!tuning.Effects.TryGetEffect(effectId, out cap))
            {
                errors.Add(Error(CatalogIssueCode.UnknownEffectId, source.Name, eventId, path + ".effectId",
                    "'" + effectId + "' is not in the effect palette registry (effects.perEffect)"));
                ok = false;
            }
            else
            {
                known = true;
            }

            // --- scope -------------------------------------------------------------------------
            EffectScope scope;
            JsonNode? scopeNode = Member(node, "scope");
            string scopeText = scopeNode != null && scopeNode.Kind == JsonKind.String ? (scopeNode.Text ?? "") : "";
            if (!TryParseScope(scopeText, out scope))
            {
                errors.Add(Error(CatalogIssueCode.UnknownEffectScope, source.Name, eventId, path + ".scope",
                    "expected \"city\" or \"district\""));
                ok = false;
            }
            else if (known && scope != cap.Scope)
            {
                errors.Add(Error(CatalogIssueCode.EffectScopeMismatch, source.Name, eventId, path + ".scope",
                    "declared " + ScopeKey(scope) + " but the palette declares '" + effectId + "' as " +
                    ScopeKey(cap.Scope)));
                ok = false;
            }

            // --- magnitude, against the declared cap -------------------------------------------
            double magnitude = 0.0;
            JsonNode? magnitudeNode = Member(node, "magnitude");
            if (magnitudeNode == null || magnitudeNode.Kind != JsonKind.Number || !IsFinite(magnitudeNode.Number))
            {
                errors.Add(Error(CatalogIssueCode.MagnitudeNotFinite, source.Name, eventId, path + ".magnitude",
                    "expected a finite number"));
                ok = false;
            }
            else
            {
                magnitude = magnitudeNode.Number;

                if (known)
                {
                    double ceiling = MagnitudeCeiling(cap, tuning);
                    if (Math.Abs(magnitude) > ceiling)
                    {
                        errors.Add(Error(CatalogIssueCode.MagnitudeOutOfCap, source.Name, eventId, path + ".magnitude",
                            Describe(magnitude) + " exceeds the declared cap for '" + effectId + "' (|magnitude| <= " +
                            Describe(ceiling) + ")"));
                        ok = false;
                    }
                    else
                    {
                        if (magnitude == 0.0)
                        {
                            warnings.Add(Warn(CatalogIssueCode.ZeroMagnitude, source.Name, eventId, path + ".magnitude",
                                "a zero magnitude requests nothing"));
                        }

                        double scaled = Math.Abs(magnitude) * SeverityScale(severity, tuning);
                        if (scaled > ceiling)
                        {
                            warnings.Add(Warn(CatalogIssueCode.SeverityScaledMagnitudeClamped, source.Name, eventId,
                                path + ".magnitude",
                                "severity " + severity.ToString(CultureInfo.InvariantCulture) + " scales this to " +
                                Describe(scaled) + ", above the cap of " + Describe(ceiling) +
                                "; the sink will clamp it"));
                        }
                    }
                }
            }

            // --- duration, against the declared cap --------------------------------------------
            int durationMonths = 0;
            JsonNode? durationNode = Member(node, "durationMonths");
            if (durationNode == null || !TryInt(durationNode, out durationMonths) || durationMonths < 0)
            {
                errors.Add(Error(CatalogIssueCode.EffectDurationOutOfCap, source.Name, eventId,
                    path + ".durationMonths", "expected a non-negative integer number of months"));
                ok = false;
                durationMonths = 0;
            }
            else if (known)
            {
                int ceiling = DurationCeiling(cap, tuning);
                if (durationMonths > ceiling)
                {
                    errors.Add(Error(CatalogIssueCode.EffectDurationOutOfCap, source.Name, eventId,
                        path + ".durationMonths",
                        durationMonths.ToString(CultureInfo.InvariantCulture) +
                        " months exceeds the declared cap for '" + effectId + "' (" +
                        ceiling.ToString(CultureInfo.InvariantCulture) + ")"));
                    ok = false;
                }
            }

            if (!ok) return false;

            // districtId stays null: real history does not know the player's map (§6).
            into.Add(new TimelineEventEffect(effectId, scope, magnitude, durationMonths, null));
            return true;
        }

        /// <summary>
        /// The strictest ceiling that applies to a catalog magnitude: the effect's own declared cap,
        /// tightened by <c>catalog.effectMagnitudeGlobalCap</c> when that is the smaller of the two.
        /// A non-positive global cap is treated as unset rather than as "reject everything".
        /// </summary>
        private static double MagnitudeCeiling(EffectCap cap, EngineTuning tuning)
        {
            double ceiling = cap.MagnitudeCap;
            double global = tuning.Catalog.EffectMagnitudeGlobalCap;
            if (global > 0.0 && global < ceiling) ceiling = global;
            return ceiling;
        }

        private static int DurationCeiling(EffectCap cap, EngineTuning tuning)
        {
            int ceiling = cap.DurationCapMonths;
            int global = tuning.Catalog.EffectDurationCapMonths;
            if (global > 0 && global < ceiling) ceiling = global;
            return ceiling;
        }

        /// <summary>
        /// The multiplier severity applies to an authored magnitude. Kept private: the scheduler owns
        /// the actual scaling, and two implementations of one formula is how they drift apart.
        /// </summary>
        private static double SeverityScale(int severity, EngineTuning tuning)
        {
            int steps = severity - 1;
            if (steps < 0) steps = 0;
            return 1.0 + tuning.Catalog.SeverityEffectScale * steps;
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
                    "not declared by timeline.schema.json; ignored"));
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

        /// <summary>Strict <c>YYYY-MM-DD</c>. No culture, no <see cref="DateTime"/>, no leniency.</summary>
        private static bool TryParseIsoDate(string? text, out SimDate date)
        {
            date = default(SimDate);

            if (text == null || text.Length != 10) return false;
            if (text[4] != '-' || text[7] != '-') return false;

            int year, month, day;
            if (!TryDigits(text, 0, 4, out year)) return false;
            if (!TryDigits(text, 5, 2, out month)) return false;
            if (!TryDigits(text, 8, 2, out day)) return false;

            if (month < 1 || month > 12) return false;
            if (day < 1 || day > DaysInMonth[month - 1]) return false;

            date = new SimDate(year, month, day);
            return true;
        }

        private static bool TryDigits(string text, int start, int length, out int value)
        {
            value = 0;
            for (int i = start; i < start + length; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9') return false;
                value = value * 10 + (c - '0');
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

        private static bool TryParseScope(string text, out EffectScope scope)
        {
            if (string.CompareOrdinal(text, "city") == 0) { scope = EffectScope.City; return true; }
            if (string.CompareOrdinal(text, "district") == 0) { scope = EffectScope.District; return true; }

            scope = EffectScope.City;
            return false;
        }

        private static string ScopeKey(EffectScope scope) => scope == EffectScope.District ? "'district'" : "'city'";

        private static string Describe(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static int CompareEvents(TimelineEvent a, TimelineEvent b)
        {
            int byDate = a.Date.CompareTo(b.Date);
            return byDate != 0 ? byDate : string.CompareOrdinal(a.Id, b.Id);
        }

        private static string[] BuildIssueKeys()
        {
            var keys = new string[Issues.All.Count];
            for (int i = 0; i < Issues.All.Count; i++) keys[i] = Issues.ToKey(Issues.All[i]);
            return keys;
        }

        private static CatalogIssue Error(CatalogIssueCode code, string sourceName, string eventId,
                                          string path, string message) =>
            new CatalogIssue(CatalogIssueSeverity.Error, code, sourceName, eventId, path, message);

        private static CatalogIssue Warn(CatalogIssueCode code, string sourceName, string eventId,
                                         string path, string message) =>
            new CatalogIssue(CatalogIssueSeverity.Warning, code, sourceName, eventId, path, message);
    }
}
