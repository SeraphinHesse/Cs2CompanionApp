// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Newtonsoft.Json.Linq;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// Canned, fully deterministic prose. The <see cref="IFlavorProvider"/> used when no Claude CLI
    /// is installed, in tests, and - per §3 - as the shape the project moves to post-v3 when live
    /// LLM calls are replaced by pregenerated content pools.
    ///
    /// <para>
    /// <b>Deterministic, and by the same rules as the engine.</b> Every choice here goes through
    /// <see cref="SeedStreams.RngFor"/> on <see cref="StreamNames.NameSelection"/>, keyed by the
    /// entity's own ID - never <c>System.Random</c>, never a hash code, never an index. Two players
    /// on the same save GUID at the same date get identical names, identical outlets and identical
    /// articles (non-negotiable #2). Names go further and drop the date entirely, keying on the
    /// entity's founding date instead, so they survive regeneration; articles and event prose keep
    /// the request date, because those are meant to move. That is not decoration either: §3 promises
    /// save-scumming converges, and prose that reshuffled on reload would be the one visible thing
    /// that did not.
    /// </para>
    ///
    /// <para>
    /// <b>It validates itself.</b> The pool builds a JSON document and puts it through the same
    /// <see cref="FlavorValidator"/> the CLI's output goes through - same schema, same numeric sweep,
    /// same ID catalog. A template that grew past its length limit, or a stray digit in a slogan,
    /// fails here exactly as a bad model response would, rather than shipping as the trusted path.
    /// </para>
    /// </summary>
    public sealed class StaticPoolProvider : IFlavorProvider
    {
        private readonly Guid _saveGuid;
        private readonly RegionTheme _theme;
        private readonly FlavorValidator _validator;
        private readonly IFlavorLog _log;

        /// <summary>
        /// The roster to write about. Set by the caller before polling; <see cref="TryGetFlavor"/>
        /// alone cannot know which parties exist, because <see cref="IFlavorProvider"/> hands it only
        /// a snapshot and a date.
        /// </summary>
        public FlavorRequest Roster { get; set; }

        private SimDate? _lastGeneratedFor;
        private FlavorPayload _lastPayload;
        private FlavorDocument _lastDocument;

        public StaticPoolProvider(Guid saveGuid, RegionTheme theme, FlavorValidator validator, IFlavorLog log)
        {
            _saveGuid = saveGuid;
            _theme = theme;
            _log = log ?? NullFlavorLog.Instance;
            _validator = validator ?? FlavorValidator.Create(null, _log);
        }

        /// <summary>The last document generated, including faction and event prose.</summary>
        public FlavorDocument LastDocument => _lastDocument;

        /// <summary>
        /// Generates for <paramref name="date"/>, once per date. Returns null on a repeat poll for
        /// the same date - the contract's "no fresh flavor, keep what you have".
        /// </summary>
        public FlavorPayload TryGetFlavor(CitySnapshot snapshot, SimDate date)
        {
            if (_lastGeneratedFor.HasValue && _lastGeneratedFor.Value == date) return null;

            var request = Roster ?? new FlavorRequest();
            request.Date = date;
            if (request.Snapshot == null) request.Snapshot = snapshot;
            request.Theme = _theme;

            FlavorDocument document = Generate(request);
            _lastGeneratedFor = date;

            if (document == null) return null;

            _lastDocument = document;
            _lastPayload = document.ToPayload(date);
            return _lastPayload;
        }

        /// <summary>
        /// Builds and validates a canned document. Returns null only if the pool itself failed
        /// validation, which is a bug in <see cref="StaticPoolContent"/> and is logged as one.
        /// </summary>
        public FlavorDocument Generate(FlavorRequest request)
        {
            if (request == null) return null;

            JObject root;
            try
            {
                root = BuildDocument(request);
            }
            catch (Exception ex)
            {
                _log.Error("the static flavor pool failed to build a document", ex);
                return null;
            }

            var result = _validator.Validate(root.ToString(Newtonsoft.Json.Formatting.None),
                                             request.EffectiveCatalog(), request.Date);
            if (!result.IsValid)
            {
                // Loud on purpose. The static pool is the fallback for the fallback; if it cannot
                // pass its own schema there is nothing left underneath it.
                _log.Error("the static flavor pool produced output that fails politics_flavor.schema.json: " +
                           string.Join("; ", ToArray(result.Errors)));
                return null;
            }

            return result.Document;
        }

        // ---- document assembly -------------------------------------------------------------------

        private JObject BuildDocument(FlavorRequest request)
        {
            var root = new JObject
            {
                ["schemaVersion"] = FlavorSchema.SupportedSchemaVersion,
                ["generatedAtSimDate"] = request.Date.ToString()
            };

            var usedNames = new HashSet<string>(StringComparer.Ordinal);

            JArray parties = BuildParties(request, usedNames);
            if (parties.Count > 0) root["partyFlavor"] = parties;

            JArray factions = BuildFactions(request, usedNames);
            if (factions.Count > 0) root["factionFlavor"] = factions;

            JArray articles = BuildArticles(request);
            if (articles.Count > 0) root["articles"] = articles;

            JArray eventProse = BuildEventProse(request);
            if (eventProse.Count > 0) root["eventProse"] = eventProse;

            return root;
        }

        private JArray BuildParties(FlavorRequest request, HashSet<string> usedNames)
        {
            var array = new JArray();

            // Sorted by PartyId so the de-duplication pass below is order-stable.
            //
            // CALLER CONTRACT: pass the FULL roster, always. usedNames is allocated per Generate call
            // (see BuildDocument), so uniqueness is only ever enforced against the parties present in
            // *this* call - a party's final name is a function of its own stream AND of who else is in
            // the request. Generating for a subset, e.g. only the parties still lacking a name, is
            // therefore not an optimisation: it lets a newcomer take its first draw unchallenged, and
            // that draw may be a name an existing party already holds. The next full-roster generate
            // then resolves the clash by moving whichever of the two sorts later, which is either a
            // rename the runtime's name lock is built to prevent or, if the newcomer sorted first, two
            // parties wearing one name with nothing left to repair it. Party ids are handed out
            // ascending (PartyRegistry.NextPartyId), so with the full roster a newly founded party
            // sorts last and settles its own collisions without disturbing anyone already named.
            var parties = new List<PartyBrief>(request.Parties);
            parties.Sort((a, b) => string.CompareOrdinal(a.PartyId, b.PartyId));

            string[] adjectives = _theme == RegionTheme.Na
                ? StaticPoolContent.NaPartyAdjectives
                : StaticPoolContent.EuPartyAdjectives;
            string[] nouns = _theme == RegionTheme.Na
                ? StaticPoolContent.NaPartyNouns
                : StaticPoolContent.EuPartyNouns;

            for (int i = 0; i < parties.Count; i++)
            {
                PartyBrief party = parties[i];
                if (string.IsNullOrEmpty(party.PartyId)) continue;

                // Keyed on the party's founding date, not the request date. A name is identity, and a
                // pool regenerated next month must produce the same one - seeding from request.Date
                // renamed every party on every prose collection.
                var rng = SeedStreams.RngFor(_saveGuid, party.FoundedDate, StreamNames.NameSelection,
                                             "party:" + party.PartyId);

                string name = UniqueName(rng, adjectives, nouns, usedNames);
                int issue = IssueIndex(party.CoreGrievance);

                array.Add(new JObject
                {
                    ["partyId"] = party.PartyId,
                    ["name"] = Cap(name, 80),
                    ["shortName"] = Cap(StaticPoolContent.IssueShortNames[issue], 12),
                    ["description"] = Cap("The " + name + " speaks for " +
                                          StaticPoolContent.IssueDescriptions[issue] + ".", 600),
                    ["slogan"] = Cap(StaticPoolContent.IssueSlogans[issue], 120)
                });
            }

            return array;
        }

        private JArray BuildFactions(FlavorRequest request, HashSet<string> usedNames)
        {
            var array = new JArray();

            var factions = new List<FactionBrief>(request.Factions);
            factions.Sort((a, b) => string.CompareOrdinal(a.FactionId, b.FactionId));

            for (int i = 0; i < factions.Count; i++)
            {
                FactionBrief faction = factions[i];
                if (string.IsNullOrEmpty(faction.FactionId)) continue;

                // Founding date, for the same reason as parties above.
                var rng = SeedStreams.RngFor(_saveGuid, faction.FoundedDate, StreamNames.NameSelection,
                                             "faction:" + faction.FactionId);

                string name = UniqueName(rng, StaticPoolContent.FactionAdjectives,
                                         StaticPoolContent.FactionNouns, usedNames);
                int issue = IssueIndex(faction.CoreGrievance);

                string leader = StaticPoolContent.Pick(StaticPoolContent.LeaderGivenNames, rng) + " " +
                                StaticPoolContent.Pick(StaticPoolContent.LeaderFamilyNames, rng);

                var entry = new JObject
                {
                    ["factionId"] = faction.FactionId,
                    ["name"] = Cap(name, 80),
                    ["shortName"] = Cap(StaticPoolContent.IssueShortNames[issue], 12),
                    ["description"] = Cap("Inside the party, the " + name + " argues for " +
                                          StaticPoolContent.IssueDescriptions[issue] + ".", 600),
                    ["leaderName"] = Cap(leader, 80)
                };

                // Only claim a party when there is one; an empty partyId would be dropped by the
                // catalog check as an unknown party.
                if (!string.IsNullOrEmpty(faction.PartyId)) entry["partyId"] = faction.PartyId;

                array.Add(entry);
            }

            return array;
        }

        private JArray BuildArticles(FlavorRequest request)
        {
            var array = new JArray();

            int count = request.ArticleCount;
            if (count < 1) count = 1;
            if (count > 8) count = 8;

            string mood = request.Snapshot == null
                ? "hard to read"
                : FlavorPromptBuilder.HappinessBand(request.Snapshot.Happiness);
            int moodIndex = request.Snapshot == null
                ? 2
                : FlavorPromptBuilder.HappinessBandIndex(request.Snapshot.Happiness);

            List<DistrictSnapshot> districts = SortedDistricts(request.Snapshot);
            string datePart = request.Date.ToString();

            // A news round in which two outlets run the identical headline reads as a bug, so
            // headlines, bodies and outlets are drawn like names: retried within the article's own
            // stream until unused.
            var usedHeadlines = new HashSet<string>(StringComparer.Ordinal);
            var usedBodies = new HashSet<string>(StringComparer.Ordinal);
            var usedOutlets = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < count; i++)
            {
                string id = "static-" + datePart + "-" + (i + 1).ToString(CultureInfo.InvariantCulture);

                // One sub-stream per article, keyed by the article's own id: article three's text
                // does not shift because article two's template changed.
                var rng = SeedStreams.RngFor(_saveGuid, request.Date, StreamNames.NameSelection, "article:" + id);

                string outlet = UniqueLine(rng, StaticPoolContent.Outlets, null, null, usedOutlets);
                string tone = StaticPoolContent.Pick(StaticPoolContent.TonesByMood[moodIndex], rng);

                // Alternate city and district pieces so a run of articles is not all one shape.
                bool local = districts.Count > 0 && (i % 2 == 1);

                JObject article;
                if (local)
                {
                    DistrictSnapshot district = districts[rng.NextInt(0, districts.Count)];
                    string districtName = SafeName(district);

                    article = new JObject
                    {
                        ["id"] = id,
                        ["outlet"] = Cap(outlet, 60),
                        ["headline"] = Cap(UniqueLine(rng, StaticPoolContent.DistrictHeadlines,
                                                      "{district}", districtName, usedHeadlines),
                                           FlavorCacheMigration.HeadlineMaxLength),
                        ["body"] = Cap(UniqueLine(rng, StaticPoolContent.DistrictBodies,
                                                  "{district}", districtName, usedBodies),
                                       FlavorCacheMigration.BodyMaxLength),
                        ["tone"] = tone,
                        ["refs"] = new JObject { ["districtId"] = district.Id }
                    };
                }
                else
                {
                    article = new JObject
                    {
                        ["id"] = id,
                        ["outlet"] = Cap(outlet, 60),
                        ["headline"] = Cap(UniqueLine(rng, StaticPoolContent.CityHeadlines,
                                                      "{mood}", mood, usedHeadlines),
                                           FlavorCacheMigration.HeadlineMaxLength),
                        ["body"] = Cap(UniqueLine(rng, StaticPoolContent.CityBodies,
                                                  "{mood}", mood, usedBodies),
                                       FlavorCacheMigration.BodyMaxLength),
                        ["tone"] = tone
                    };
                }

                array.Add(article);
            }

            return array;
        }

        private JArray BuildEventProse(FlavorRequest request)
        {
            var array = new JArray();

            var events = new List<EventBrief>(request.Events);
            events.Sort((a, b) => string.CompareOrdinal(a.EventId, b.EventId));

            for (int i = 0; i < events.Count; i++)
            {
                EventBrief e = events[i];
                if (string.IsNullOrEmpty(e.EventId)) continue;

                var rng = SeedStreams.RngFor(_saveGuid, request.Date, StreamNames.NameSelection,
                                             "event:" + e.EventId);

                array.Add(new JObject
                {
                    ["eventId"] = e.EventId,
                    ["localAngle"] = Cap(StaticPoolContent.Pick(StaticPoolContent.EventAngles, rng), 900)
                });
            }

            return array;
        }

        // ---- helpers -----------------------------------------------------------------------------

        /// <summary>
        /// Draws an "Adjective Noun" name, retrying within the entity's own stream until it is
        /// unused. Bounded: after the attempts run out it takes the last draw and lets the collision
        /// stand, because an unbounded loop on a small pool would hang on a city with many parties.
        /// </summary>
        private static string UniqueName(DeterministicRng rng, string[] adjectives, string[] nouns,
                                         HashSet<string> used)
        {
            string name = string.Empty;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                name = StaticPoolContent.Pick(adjectives, rng) + " " + StaticPoolContent.Pick(nouns, rng);
                if (used.Add(name)) return name;
            }
            return name;
        }

        /// <summary>
        /// Draws a template, substitutes one placeholder, and retries within the caller's stream
        /// until the result is unused. Bounded like <see cref="UniqueName"/>: with more articles than
        /// templates a repeat is unavoidable, and hanging is not an acceptable alternative.
        /// </summary>
        private static string UniqueLine(DeterministicRng rng, string[] templates,
                                         string placeholder, string value, HashSet<string> used)
        {
            string line = string.Empty;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                line = StaticPoolContent.Pick(templates, rng);
                // string.Replace throws on an empty needle, so a null placeholder means "no
                // substitution" - which is how this doubles as the outlet picker.
                if (!string.IsNullOrEmpty(placeholder)) line = line.Replace(placeholder, value);
                if (used.Add(line)) return line;
            }
            return line;
        }

        private static List<DistrictSnapshot> SortedDistricts(CitySnapshot snapshot)
        {
            var districts = new List<DistrictSnapshot>();
            if (snapshot == null || snapshot.Districts == null) return districts;

            for (int i = 0; i < snapshot.Districts.Count; i++)
            {
                DistrictSnapshot district = snapshot.Districts[i];
                if (district != null && !string.IsNullOrEmpty(district.Id)) districts.Add(district);
            }

            // The snapshot contract already sorts by Id, but sorting again costs nothing and makes
            // this class's determinism independent of that guarantee holding.
            districts.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return districts;
        }

        private static string SafeName(DistrictSnapshot district)
        {
            string name = district.Name;
            if (string.IsNullOrEmpty(name)) name = district.Id;
            return Cap(name, 60);
        }

        /// <summary>Enum value to pool index, clamped. The pools are ordered by <c>Issues.All</c>.</summary>
        private static int IssueIndex(Issue issue)
        {
            int index = (int)issue;
            if (index < 0 || index >= Issues.Count) return 0;
            return index;
        }

        private static string Cap(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= maxLength ? text : text.Substring(0, maxLength).TrimEnd();
        }

        private static string[] ToArray(IReadOnlyList<string> items)
        {
            if (items == null) return new string[0];
            var array = new string[items.Count];
            for (int i = 0; i < items.Count; i++) array[i] = items[i];
            return array;
        }
    }
}
