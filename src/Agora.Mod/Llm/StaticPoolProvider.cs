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
    ///
    /// <para>
    /// <b>Every article points at something.</b> <see cref="FlavorValidator"/> drops an article whose
    /// three ref fields are all empty, because the prompt tells the model it will - so the pool is
    /// held to its own rule rather than exempted from it. Each article names a party or a district
    /// and refs that same id, and a save with neither files no articles at all. That empty round is
    /// the correct output for a city with no politics yet, not a gap to be filled with prose about
    /// nobody.
    /// </para>
    /// </summary>
    public sealed class StaticPoolProvider : IFlavorProvider
    {
        /// <summary>
        /// The schema's limit on <c>outlet</c>. Named rather than typed twice, and unlike the article
        /// limits it has no home in <see cref="FlavorCacheMigration"/> because no migration ever moved
        /// it - <c>FlavorSchemaDriftTests</c> is what pins it against the schema.
        /// </summary>
        private const int OutletMaxLength = 60;

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

            // Stamped here rather than in FlavorDocument, which defaults to Cli because its only
            // other caller is the validator and everything reaching the validator came off the wire.
            // The label is what lets a consumer hold both writers' prose at once instead of one
            // erasing the other (StoryProseLedger), so a pool document wearing the model's label
            // would take the slot the model's prose is added to and keep it for the story's life.
            result.Document.Source = ProseSource.Pool;

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

            // Seeded with the names the roster already wears, before any draw happens. The set used to
            // start empty, so uniqueness was only ever enforced against this call's own draws: a name
            // an entity already held - including one the player typed for their own party - was no
            // obstacle at all, and an unnamed newcomer could draw that exact string. The runtime,
            // seeing an empty Name on the newcomer, would then write it. Two parties, one name, and
            // nothing left to repair it, because the incumbent's name is locked.
            //
            // Seeded here rather than inside either builder so that parties and factions share one
            // reservation - a party is built first and would otherwise be free to take a faction's
            // name - and so that neither builder can later be changed in a way that forgets.
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            SeedCurrentNames(request, usedNames);

            // Party id to the name this document gives it. Written by BuildParties, read by
            // BuildArticles so that an article naming a party in its prose names it exactly as the
            // partyFlavor entry does. Looked up by key only, never iterated: a Dictionary's order is
            // not one this class is allowed to depend on.
            var partyNames = new Dictionary<string, string>(StringComparer.Ordinal);

            JArray parties = BuildParties(request, usedNames, partyNames);
            if (parties.Count > 0) root["partyFlavor"] = parties;

            JArray factions = BuildFactions(request, usedNames);
            if (factions.Count > 0) root["factionFlavor"] = factions;

            JArray articles = BuildArticles(request, partyNames);
            if (articles.Count > 0) root["articles"] = articles;

            JArray eventProse = BuildEventProse(request);
            if (eventProse.Count > 0) root["eventProse"] = eventProse;

            return root;
        }

        /// <summary>
        /// Reserves every name the roster already wears, skipping the blanks.
        /// </summary>
        /// <remarks>
        /// Pure over the request: the set is seeded from request data and from nothing ambient, which
        /// is what keeps the document a function of its inputs (non-negotiable #3). It is only ever
        /// membership-tested afterwards - <see cref="UniqueName"/> reads it through
        /// <c>HashSet.Add</c>'s bool and never enumerates it - so its iteration order reaches no draw.
        /// </remarks>
        private static void SeedCurrentNames(FlavorRequest request, HashSet<string> used)
        {
            List<PartyBrief> parties = request.Parties;
            for (int i = 0; parties != null && i < parties.Count; i++)
            {
                PartyBrief party = parties[i];
                if (party != null && !string.IsNullOrEmpty(party.CurrentName)) used.Add(party.CurrentName);
            }

            List<FactionBrief> factions = request.Factions;
            for (int i = 0; factions != null && i < factions.Count; i++)
            {
                FactionBrief faction = factions[i];
                if (faction != null && !string.IsNullOrEmpty(faction.CurrentName)) used.Add(faction.CurrentName);
            }
        }

        private JArray BuildParties(FlavorRequest request, HashSet<string> usedNames,
                                    Dictionary<string, string> partyNames)
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
            //
            // A name is drawn for EVERY party here, already-named ones included, and the runtime
            // decides whether to apply it - it will not overwrite a locked name. That is why the
            // usedNames seeding in BuildDocument hands each party its own CurrentName back as a legal
            // draw (see UniqueName): a party whose current name is reserved against everyone else must
            // still be able to draw it itself, or its entry would come back holding a different name,
            // and a pool-written name is provisional, so ApplyProseNames would apply the replacement.
            // That is a rename every sim month - the exact defect keying the seed on the founding date
            // was introduced to kill.
            List<PartyBrief> parties = SortedParties(request);

            string[] adjectives = _theme == RegionTheme.Na
                ? StaticPoolContent.NaPartyAdjectives
                : StaticPoolContent.EuPartyAdjectives;
            string[] nouns = _theme == RegionTheme.Na
                ? StaticPoolContent.NaPartyNouns
                : StaticPoolContent.EuPartyNouns;

            for (int i = 0; i < parties.Count; i++)
            {
                PartyBrief party = parties[i];

                // Keyed on the party's founding date, not the request date. A name is identity, and a
                // pool regenerated next month must produce the same one - seeding from request.Date
                // renamed every party on every prose collection.
                var rng = SeedStreams.RngFor(_saveGuid, party.FoundedDate, StreamNames.NameSelection,
                                             "party:" + party.PartyId);

                string name = UniqueName(rng, adjectives, nouns, usedNames, party.CurrentName);
                int issue = IssueIndex(party.CoreGrievance);

                partyNames[party.PartyId] = name;

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
                                         StaticPoolContent.FactionNouns, usedNames, faction.CurrentName);
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

        /// <summary>
        /// What one article in a round is about. Every kind but <see cref="District"/> is about a
        /// party and names it, so every article can carry a ref either way - which is what lets
        /// <see cref="FlavorValidator"/> drop a refless article outright.
        /// </summary>
        private enum ArticleKind
        {
            /// <summary>The whole city, through one party's week. <c>{party}</c> and <c>{mood}</c>.</summary>
            CityMood,

            /// <summary>One district. <c>{district}</c>.</summary>
            District,

            /// <summary>Election slot (a): the result piece.</summary>
            ElectionResult,

            /// <summary>Election slot (b): a party's own claim on the mandate, not an outcome.</summary>
            ElectionClaim,

            /// <summary>
            /// Election slot (c): a party's own challenge to the reading of the count, likewise - and
            /// never the same party as <see cref="ElectionClaim"/> while the roster holds two.
            /// </summary>
            ElectionChallenge,

            /// <summary>Election slot (d): the coalition outlook. EU only.</summary>
            ElectionCoalition
        }

        private JArray BuildArticles(FlavorRequest request, Dictionary<string, string> partyNames)
        {
            var array = new JArray();

            int count = request.ArticleCount;
            if (count < 1) count = 1;
            // The cap is FlavorRequest.ElectionArticleCountEu itself, the largest count anything asks
            // for, so the two cannot drift apart: raising that raises this in the same edit, and an EU
            // election round can never be quietly cut short here.
            if (count > FlavorRequest.ElectionArticleCountEu) count = FlavorRequest.ElectionArticleCountEu;

            string mood = request.Snapshot == null
                ? "hard to read"
                : FlavorPromptBuilder.HappinessBand(request.Snapshot.Happiness);
            int moodIndex = request.Snapshot == null
                ? 2
                : FlavorPromptBuilder.HappinessBandIndex(request.Snapshot.Happiness);

            List<DistrictSnapshot> districts = SortedDistricts(request.Snapshot);
            List<PartyBrief> parties = SortedParties(request);

            // NOTHING TO POINT AT, SO NOTHING TO FILE. Every article carries a ref, and the only ids
            // this pool can honestly reference are a party's and a district's. A save with neither -
            // the first months, before the roster is built - therefore gets no canned articles at
            // all, on purpose. That is the correct outcome, not a gap: refless articles would be
            // dropped by FlavorValidator anyway, and inventing an id to satisfy the rule would put a
            // reference in front of the player that points at nothing.
            if (parties.Count == 0 && districts.Count == 0) return array;

            List<ArticleKind> kinds = PlanRound(request, count, parties.Count > 0, districts.Count > 0);
            string datePart = request.Date.ToString();

            // A news round in which two outlets run the identical headline reads as a bug, so
            // headlines, bodies and outlets are drawn like names: retried within the article's own
            // stream until unused.
            var usedHeadlines = new HashSet<string>(StringComparer.Ordinal);
            var usedBodies = new HashSet<string>(StringComparer.Ordinal);
            var usedOutlets = new HashSet<string>(StringComparer.Ordinal);

            // The party the claim piece landed on, so the challenge piece can avoid it. Two
            // independent draws over three parties put the same party on both sides of the argument
            // about one round in three, which reads as a bug rather than as politics. PlanRound always
            // files the claim before the challenge, so this is set by the time it is read.
            string claimPartyId = null;

            for (int i = 0; i < kinds.Count; i++)
            {
                ArticleKind kind = kinds[i];
                string id = "static-" + datePart + "-" + (i + 1).ToString(CultureInfo.InvariantCulture);

                // One sub-stream per article, keyed by the article's own id: article three's text
                // does not shift because article two's template changed. The party draw below rides
                // this same stream - RngFor's fourth argument is a sub-stream key under
                // StreamNames.NameSelection, so naming a party costs no new named stream.
                var rng = SeedStreams.RngFor(_saveGuid, request.Date, StreamNames.NameSelection, "article:" + id);
                // NB "article:" here is an RNG sub-stream salt, NOT an alert id. Alert ids carry the
                // BARE article id precisely because it doubles as the agora.news.article map key
                // (see NewsAlert.Id). A grep for "article:" hits this line first; do not "fix" it to
                // match, and do not copy it into an alert.

                string outlet = UniqueLine(rng, StaticPoolContent.Outlets, NoSubstitution, NoSubstitution,
                                           usedOutlets, 0);
                string tone = StaticPoolContent.Pick(StaticPoolContent.TonesByMood[moodIndex], rng);

                Substitution subject;
                Substitution second = NoSubstitution;
                string[] headlines;
                string[] bodies;
                JObject refs;

                if (kind == ArticleKind.District)
                {
                    DistrictSnapshot district = districts[rng.NextInt(0, districts.Count)];
                    subject = Substitution.Of("{district}", SafeName(district));
                    headlines = StaticPoolContent.DistrictHeadlines;
                    bodies = StaticPoolContent.DistrictBodies;
                    refs = new JObject { ["districtId"] = district.Id };
                }
                else
                {
                    // Every other kind is about a party. The id goes in refs and the same party's
                    // name goes in the prose, so the reference is one the reader can check.
                    PartyBrief party = PickParty(rng, parties,
                                                 kind == ArticleKind.ElectionChallenge ? claimPartyId : null);
                    if (kind == ArticleKind.ElectionClaim) claimPartyId = party.PartyId;

                    subject = Substitution.Of("{party}", PartyName(partyNames, party));
                    refs = new JObject { ["partyId"] = party.PartyId };

                    switch (kind)
                    {
                        case ArticleKind.ElectionResult:
                            headlines = StaticPoolContent.ElectionResultHeadlines;
                            bodies = StaticPoolContent.ElectionResultBodies;
                            break;
                        case ArticleKind.ElectionClaim:
                            headlines = StaticPoolContent.ElectionClaimHeadlines;
                            bodies = StaticPoolContent.ElectionClaimBodies;
                            break;
                        case ArticleKind.ElectionChallenge:
                            headlines = StaticPoolContent.ElectionChallengeHeadlines;
                            bodies = StaticPoolContent.ElectionChallengeBodies;
                            break;
                        case ArticleKind.ElectionCoalition:
                            headlines = StaticPoolContent.ElectionCoalitionHeadlines;
                            bodies = StaticPoolContent.ElectionCoalitionBodies;
                            break;
                        default:
                            headlines = StaticPoolContent.CityHeadlines;
                            bodies = StaticPoolContent.CityBodies;
                            second = Substitution.Of("{mood}", mood);
                            break;
                    }
                }

                array.Add(new JObject
                {
                    ["id"] = id,
                    ["outlet"] = Cap(outlet, OutletMaxLength),
                    ["headline"] = Fitting(rng, headlines, subject, second, StaticPoolContent.GenericHeadlines,
                                           usedHeadlines, FlavorCacheMigration.HeadlineMaxLength),
                    ["body"] = Fitting(rng, bodies, subject, second, StaticPoolContent.GenericBodies,
                                       usedBodies, FlavorCacheMigration.BodyMaxLength),
                    ["tone"] = tone,
                    ["refs"] = refs
                });
            }

            return array;
        }

        /// <summary>
        /// Decides what each article in the round is before any of them is written.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Four combinations of "are there parties" and "are there districts", and all four have to
        /// work. With both, the round alternates city and district pieces as it always has. With only
        /// parties, every article is a city piece; with only districts, every article is a district
        /// piece - because the missing side has no id to put in refs, and the old <c>i % 2 == 1</c>
        /// alternation would have filed a refless city article on a save with no parties. With
        /// neither, the caller has already returned an empty round.
        /// </para>
        /// <para>
        /// An election wake leads with the same four pieces <c>FlavorPromptBuilder.AppendElectionCoverage</c>
        /// asks the model for, and only when there is a party to name in them.
        /// </para>
        /// </remarks>
        private List<ArticleKind> PlanRound(FlavorRequest request, int count, bool haveParties, bool haveDistricts)
        {
            var kinds = new List<ArticleKind>(count);

            if (request.Reason == FlavorWakeReason.Election && haveParties)
            {
                kinds.Add(ArticleKind.ElectionResult);
                kinds.Add(ArticleKind.ElectionClaim);
                kinds.Add(ArticleKind.ElectionChallenge);

                // _theme rather than request.Theme, for the same reason BuildParties reads _theme:
                // a round whose coverage came from one theme and whose party names came from the
                // other would be a visible split down the middle of one document.
                if (_theme == RegionTheme.Eu) kinds.Add(ArticleKind.ElectionCoalition);

                // A round asked for fewer articles than the election set has pieces keeps the first
                // ones, which is the order the prompt lists them in.
                if (kinds.Count > count) kinds.RemoveRange(count, kinds.Count - count);
            }

            for (int i = kinds.Count; i < count; i++)
            {
                bool local = haveDistricts && (!haveParties || (i % 2 == 1));
                kinds.Add(local ? ArticleKind.District : ArticleKind.CityMood);
            }

            return kinds;
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
        ///
        /// <para>
        /// <paramref name="ownName"/> is the drawer's current name, and is the one already-reserved
        /// string it is allowed to draw. The set is seeded from the roster's current names
        /// (<c>SeedCurrentNames</c>), so without this exemption every named entity would collide with
        /// itself and retry into something else - see the note in <see cref="BuildParties"/> for why
        /// that is worse than the collision it would be avoiding. Null or empty means "no name yet",
        /// and never matches: a drawn name always contains a space.
        /// </para>
        /// </summary>
        private static string UniqueName(DeterministicRng rng, string[] adjectives, string[] nouns,
                                         HashSet<string> used, string ownName)
        {
            string name = string.Empty;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                name = StaticPoolContent.Pick(adjectives, rng) + " " + StaticPoolContent.Pick(nouns, rng);
                // The ownName test comes first because Add would report it as taken - the seeding put
                // it there, and the entity that put it there is the one drawing.
                if (string.Equals(name, ownName, StringComparison.Ordinal) || used.Add(name)) return name;
            }
            return name;
        }

        /// <summary>One placeholder and what it is replaced with. An unset one substitutes nothing.</summary>
        private struct Substitution
        {
            public string Placeholder;
            public string Value;

            public static Substitution Of(string placeholder, string value) =>
                new Substitution { Placeholder = placeholder, Value = value ?? string.Empty };
        }

        /// <summary>No substitution at all - the outlet picker and the generic template pools.</summary>
        private static readonly Substitution NoSubstitution = new Substitution();

        private static string Apply(string line, Substitution first, Substitution second)
        {
            if (string.IsNullOrEmpty(line)) return string.Empty;

            // string.Replace throws on an empty needle, so an unset Substitution means "no
            // substitution" - which is how this doubles as the outlet picker.
            if (!string.IsNullOrEmpty(first.Placeholder)) line = line.Replace(first.Placeholder, first.Value);
            if (!string.IsNullOrEmpty(second.Placeholder)) line = line.Replace(second.Placeholder, second.Value);
            return line;
        }

        /// <summary>
        /// Draws a line that fits <paramref name="maxLength"/> whole, preferring the specific
        /// templates and falling back to the placeholder-free generic pool.
        /// </summary>
        /// <remarks>
        /// The rule is: drop the placeholder rather than cut a name in half. A district name is the
        /// player's, so it can be any length; substituting a long one used to push the template past
        /// the cap, and the blunt cap then chopped the template's own last word off. A clean generic
        /// headline is a better article than a mangled specific one - the same call
        /// <see cref="FlavorCacheMigration"/> makes when it prunes an over-long cached article rather
        /// than trimming it. The final <see cref="TrimToWord"/> is unreachable while the generic pool
        /// is authored inside both caps, which a test pins; it is here so this cannot return
        /// something the schema would reject and take the whole document down with it.
        /// </remarks>
        private static string Fitting(DeterministicRng rng, string[] templates, Substitution first,
                                      Substitution second, string[] generic, HashSet<string> used,
                                      int maxLength)
        {
            string line = UniqueLine(rng, templates, first, second, used, maxLength);
            if (line != null) return line;

            line = UniqueLine(rng, generic, NoSubstitution, NoSubstitution, used, maxLength);
            if (line != null) return line;

            return TrimToWord(StaticPoolContent.Pick(generic, rng), maxLength);
        }

        /// <summary>
        /// Draws a template, substitutes, and retries within the caller's stream until the result is
        /// both unused and inside <paramref name="maxLength"/>. Bounded like <see cref="UniqueName"/>:
        /// with more articles than templates a repeat is unavoidable, and hanging is not an acceptable
        /// alternative. Returns null when nothing in this pool fits, which is the caller's cue to fall
        /// back rather than to truncate.
        /// </summary>
        /// <param name="maxLength">Zero disables the length filter, for fields with no cap of their own.</param>
        private static string UniqueLine(DeterministicRng rng, string[] templates, Substitution first,
                                         Substitution second, HashSet<string> used, int maxLength)
        {
            string fitting = null;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                string line = Apply(StaticPoolContent.Pick(templates, rng), first, second);

                // Over the cap: not published, and not counted as a draw either. It is not a
                // duplicate of anything, it simply does not exist as an option.
                if (maxLength > 0 && line.Length > maxLength) continue;

                fitting = line;
                if (used.Add(line)) return line;
            }
            return fitting;
        }

        /// <summary>
        /// Cuts at the last word boundary inside <paramref name="maxLength"/>, never mid-word.
        /// </summary>
        /// <remarks>
        /// The house policy is <see cref="FlavorCacheMigration"/>'s - prune rather than truncate - and
        /// <see cref="Fitting"/> follows it by dropping the placeholder instead of the name. This is
        /// the floor under that, for a pool whose generic templates have somehow all outgrown the cap,
        /// and it is the one place the alignment is imperfect: a body cut here still ends early, it
        /// just does not end mid-word.
        /// </remarks>
        private static string TrimToWord(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || maxLength <= 0) return string.Empty;
            if (text.Length <= maxLength) return text;

            int cut = text.LastIndexOf(' ', maxLength - 1);

            // No word boundary to cut at inside the cap - a single very long word. Returning nothing
            // here would be the opposite of what this method is for: an empty headline fails the
            // schema's minLength and takes the whole document down, where a cut word costs one
            // article's last syllable. Same for a boundary at index zero, which is a leading space.
            if (cut <= 0) return text.Substring(0, maxLength).TrimEnd();

            return text.Substring(0, cut).TrimEnd(' ', ',', ';', '-');
        }

        /// <summary>
        /// The parties this document writes about: those with an id, ordered by it. The same list
        /// <see cref="BuildParties"/> names from, so an article's party is one the document also
        /// carries a partyFlavor entry for.
        /// </summary>
        private static List<PartyBrief> SortedParties(FlavorRequest request)
        {
            var parties = new List<PartyBrief>();
            if (request.Parties == null) return parties;

            for (int i = 0; i < request.Parties.Count; i++)
            {
                PartyBrief party = request.Parties[i];
                if (party != null && !string.IsNullOrEmpty(party.PartyId)) parties.Add(party);
            }

            parties.Sort((a, b) => string.CompareOrdinal(a.PartyId, b.PartyId));
            return parties;
        }

        /// <summary>
        /// Draws one party from <paramref name="parties"/>, skipping <paramref name="exclude"/> when
        /// there is another to have instead.
        /// </summary>
        /// <remarks>
        /// The exclusion is the coherence rule between the election round's two reaction pieces: the
        /// side claiming the mandate and the side challenging the count should not be the same party.
        /// It narrows the draw rather than re-rolling it, so the pick is still one <c>NextInt</c> on
        /// the article's own stream and still a function of the roster's sorted order alone. A
        /// one-party save falls through to the ordinary draw, because there is no other side to give
        /// the challenge to and refusing to file it would cost the round an article.
        /// </remarks>
        private static PartyBrief PickParty(DeterministicRng rng, List<PartyBrief> parties, string exclude)
        {
            if (string.IsNullOrEmpty(exclude) || parties.Count < 2)
            {
                return parties[rng.NextInt(0, parties.Count)];
            }

            int index = rng.NextInt(0, parties.Count - 1);
            for (int i = 0; i < parties.Count; i++)
            {
                if (string.CompareOrdinal(parties[i].PartyId, exclude) == 0) continue;
                if (index == 0) return parties[i];
                index--;
            }

            // Only reachable if the excluded id is in the list more than once, which SortedParties
            // does not guarantee against. Taking the last entry keeps this total.
            return parties[parties.Count - 1];
        }

        /// <summary>
        /// The name this document gave the party, falling back to its id. The fallback is unreachable
        /// while <see cref="BuildParties"/> runs first over the same list, and is here because an
        /// article that says nothing where a name should be is worse than one that says the id.
        /// </summary>
        private static string PartyName(Dictionary<string, string> partyNames, PartyBrief party)
        {
            string name;
            if (partyNames != null && partyNames.TryGetValue(party.PartyId, out name) &&
                !string.IsNullOrEmpty(name))
            {
                return name;
            }
            return party.PartyId;
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

        /// <summary>
        /// The district's name, or its id when it has none.
        /// </summary>
        /// <remarks>
        /// Deliberately not length-capped any more. Capping here cut a player's district name mid-word
        /// before it had even reached a template, and the composed headline was then cut a second time
        /// at ninety, which took the template's own trailing words with it. Length is
        /// <see cref="Fitting"/>'s problem now: an over-long name makes every <c>{district}</c> line
        /// miss the cap, and the article takes a clean generic headline instead of a mangled specific
        /// one.
        /// </remarks>
        private static string SafeName(DistrictSnapshot district)
        {
            string name = district.Name;
            if (string.IsNullOrEmpty(name)) name = district.Id;
            return name ?? string.Empty;
        }

        /// <summary>Enum value to pool index, clamped. The pools are ordered by <c>Issues.All</c>.</summary>
        private static int IssueIndex(Issue issue)
        {
            int index = (int)issue;
            if (index < 0 || index >= Issues.Count) return 0;
            return index;
        }

        /// <summary>
        /// Blunt cap, kept only for the fields where blunt is right.
        /// </summary>
        /// <remarks>
        /// Outlets, slogans, short names, descriptions and leader names are all composed from
        /// <see cref="StaticPoolContent"/>, which authors them inside their limits, so a cut here
        /// would mean a pool entry had grown rather than that a player had typed something long. It is
        /// an assertion in the shape of a truncation. Headlines and bodies do not come through here:
        /// they carry the player's own district names, and cutting one of those mid-word is the defect
        /// <see cref="Fitting"/> replaced.
        /// </remarks>
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
