// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Agora.Core.Contracts;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// Assembles the prompt sent to the Claude CLI.
    ///
    /// <para>
    /// <b>The city is described in words, not figures.</b> Happiness 62.4 becomes "content";
    /// unemployment 0.11 becomes "high". That is not decoration. The single failure this whole
    /// packet exists to prevent is a model-authored number reaching engine state, and the cheapest
    /// structural defence is to never put a number in front of the model in the first place - a
    /// model that was never told the unemployment rate cannot write an article quoting it slightly
    /// wrong, and a reader who never sees a figure in the prose cannot mistake one for engine truth.
    /// The dashboard shows the real values, from the real snapshot, one panel away.
    /// </para>
    ///
    /// <para>
    /// The band thresholds below are prose vocabulary, not tuning: they choose an adjective and
    /// touch no engine state, so moving one cannot change a vote. They are still called out in the
    /// report as candidates for a future <c>llm</c> section in <c>engine_tuning.json</c>.
    /// </para>
    ///
    /// <para>Pure and deterministic: same request in, same prompt out. No I/O, no clock, no game types.</para>
    /// </summary>
    public static class FlavorPromptBuilder
    {
        /// <summary>Hard cap on prompt size. A prompt past this has a bug upstream, not a big city.</summary>
        public const int MaxPromptCharacters = 120_000;

        /// <summary>Districts named in the prompt, worst-off first. Whole-city prose does not need 60.</summary>
        public const int MaxDistrictsInPrompt = 12;

        /// <summary>
        /// What replaces the tail of an over-long city description. Written as an instruction rather
        /// than a note, because the model reads everything in the prompt as one.
        /// </summary>
        private const string SituationTruncationNotice =
            "- (the rest of the city description was omitted to fit the prompt; do not refer to it)\n";

        /// <summary>
        /// Assembles the prompt, keeping it inside <see cref="MaxPromptCharacters"/> by shortening the
        /// city description and nothing else.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The cap used to be applied to the finished string. The schema is the last thing appended,
        /// so on a city large enough to reach the cap the cut landed inside it — and a model handed
        /// half a schema writes a document that fails validation every single time. The failure mode
        /// was silent and scaled with the player's city, which is the worst combination available.
        /// </para>
        /// <para>
        /// The situation block is the right thing to spend, because it is the only section whose value
        /// degrades gracefully: a description missing its last few districts still describes the city,
        /// whereas a truncated schema, roster or task instruction is simply broken. If the fixed
        /// sections alone exceed the cap the prompt is allowed over it rather than losing the schema —
        /// an oversized prompt costs tokens, a truncated schema costs the whole generation.
        /// </para>
        /// </remarks>
        public static string Build(FlavorRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var head = new StringBuilder(2048);
            AppendRole(head, request);
            AppendRules(head);

            var situation = new StringBuilder(4096);
            AppendSituation(situation, request);

            var tail = new StringBuilder(8192);
            AppendRoster(tail, request);
            AppendEvents(tail, request);
            AppendTask(tail, request);
            AppendSchema(tail);

            int budget = MaxPromptCharacters - head.Length - tail.Length;

            var sb = new StringBuilder(head.Length + situation.Length + tail.Length);
            sb.Append(head);
            sb.Append(situation.Length > budget ? TruncateSituation(situation.ToString(), budget) : situation.ToString());
            sb.Append(tail);
            return sb.ToString();
        }

        /// <summary>
        /// Cuts the city description down to <paramref name="budget"/> characters at a line boundary,
        /// so the model never reads half a district record, and says so where the cut was made.
        /// </summary>
        private static string TruncateSituation(string situation, int budget)
        {
            // No room even for the notice: drop the block outright. AppendSituation's own "no
            // measurements are available" wording is not substituted here, because that would be a
            // second, quieter claim about the city rather than an admission that we cut it.
            if (budget <= SituationTruncationNotice.Length + 1) return string.Empty;

            int keep = budget - SituationTruncationNotice.Length;

            int lastNewline = situation.LastIndexOf('\n', keep - 1);
            if (lastNewline >= 0) keep = lastNewline + 1;

            return situation.Substring(0, keep) + SituationTruncationNotice;
        }

        private static void AppendRole(StringBuilder sb, FlavorRequest request)
        {
            sb.Append("You are the writers' room for a city's political press. ");
            sb.Append("The city is simulated; a separate engine already decided every fact, every number ");
            sb.Append("and every outcome. You supply only the words.\n\n");

            sb.Append("Date: ").Append(request.Date.ToString()).Append('\n');
            sb.Append("Political system: ").Append(SystemDescription(request.Theme)).Append('\n');
            sb.Append("Occasion: ").Append(ReasonDescription(request.Reason)).Append("\n\n");
        }

        private static void AppendRules(StringBuilder sb)
        {
            sb.Append("RULES - a response breaking any of these is discarded unread:\n");
            sb.Append("1. Output one JSON object and nothing else. No preamble, no explanation, no markdown fence.\n");
            sb.Append("2. Prose, IDs and dates only. Do not write a single number, percentage, poll figure, ");
            sb.Append("seat count, budget figure or year-over-year change anywhere in any field - not in a name, ");
            sb.Append("not in a headline, not in a body. Say \"support slipped\", never a figure.\n");
            sb.Append("3. Use only the IDs listed below, spelled exactly. Do not invent a party, faction, ");
            sb.Append("district or event. An entry referencing an unknown ID is dropped.\n");
            sb.Append("4. English only.\n");
            sb.Append("5. Do not predict results, announce winners, or state what the city government will do next. ");
            sb.Append("The engine decides that and your text is written before it does.\n");
            sb.Append("6. Keep every field inside its length limit in the schema at the end of this prompt.\n");
            sb.Append("7. Invent no real-world people, brands or organisations.\n\n");
        }

        private static void AppendSituation(StringBuilder sb, FlavorRequest request)
        {
            var snapshot = request.Snapshot;
            if (snapshot == null)
            {
                sb.Append("THE CITY: no measurements are available this cycle. Write generally.\n\n");
                return;
            }

            sb.Append("THE CITY (qualitative only - the engine holds the figures):\n");
            sb.Append("- size: ").Append(PopulationBand(snapshot.Population)).Append('\n');
            sb.Append("- mood: ").Append(HappinessBand(snapshot.Happiness)).Append('\n');
            sb.Append("- work: ").Append(UnemploymentBand(snapshot.Unemployment)).Append('\n');
            sb.Append("- cost of living: ").Append(RentBurdenBand(snapshot.RentBurden)).Append('\n');
            sb.Append("- household budgets: ").Append(DisposableMarginBand(snapshot.DisposableMargin)).Append('\n');
            sb.Append("- pollution: ").Append(UnitBand(snapshot.Pollution.Mean(), "clean", "mostly clean", "noticeable", "bad", "choking")).Append('\n');
            sb.Append("- public services: ").Append(CoverageBand(snapshot.Services.Mean())).Append('\n');
            sb.Append("- crime: ").Append(UnitBand(Normalise01(snapshot.CrimeRate), "rare", "low", "a live issue", "serious", "out of hand")).Append('\n');
            sb.Append("- commuting: ").Append(CommuteBand(snapshot.AverageCommuteMinutes)).Append('\n');
            sb.Append("- public finances: ").Append(BudgetBand(snapshot.BudgetBalance, snapshot.Debt)).Append('\n');

            // The v4 city-statistics block, as words. Four lines out of the eighteen numbers that
            // pass added, and every omission below is a decision rather than an oversight - the
            // prompt has a cap, and a line that cannot say anything true costs the budget of one
            // that can:
            //
            // - IndustryTaxRates and UnlockedFeatureIds are lists of ids, not a state of the city.
            //   Forty resource names and every unlocked feature would outweigh every other line in
            //   this block put together, and neither is a thing a reporter writes a paragraph about.
            //   The dashboard carries both.
            // - Attractiveness, AttractionCount and SignatureBuildingCount have no denominator:
            //   attractiveness is a raw dimensionless index by contract, the other two are counts.
            //   Banding any of them needs a reference maximum nobody has measured, and an invented
            //   threshold picks its adjective at random.
            // - UncollectedGarbage is in raw game units with the same problem, and the collection
            //   story a model can actually write from is already carried by "public services".
            // - Births and Deaths are the same "who is here" story as migration at a far slower
            //   cadence, and their collection period is still open (scout 0004 Q2) - "births" may
            //   mean this month or since the city was founded. The migration line survives that
            //   question open because it bands the *ratio* of two figures gathered on the same
            //   footing, which reads the same whichever period they cover; a births line would not.
            sb.Append("- homelessness: ").Append(HomelessnessBand(snapshot.Statistics.HomelessShare)).Append('\n');
            sb.Append("- migration: ").Append(MigrationBand(snapshot.Statistics)).Append('\n');
            sb.Append("- visitors: ").Append(TourismBand(snapshot.Tourism, snapshot.Population)).Append('\n');
            sb.Append("- city standing: ").Append(MilestoneBand(snapshot.Progression.MilestoneLevel)).Append('\n');

            // All four are city-only at source - CityStatisticsSystem is keyed by (StatisticType,
            // parameter) with no district dimension, Tourism lives on the city entity alone, and a
            // district has no milestone (see the remarks on CityStatistics and DistrictSnapshot). The
            // district block below carries no equivalent, so the only way one of these reaches the
            // prose as a local fact is the model placing it there, and saying so is the same defence
            // the fallback note on a district line makes.
            sb.Append("- note: homelessness, migration, visitors and city standing are city-wide ");
            sb.Append("measures; do not attribute any of them to a named district\n");

            var indices = snapshot.Indices;
            if (indices != null)
            {
                sb.Append("- inequality: ").Append(UnitBand(indices.GiniCoefficient, "flat", "mild", "visible", "stark", "two cities")).Append('\n');
                sb.Append("- service fairness: ").Append(UnitBand(1.0 - indices.ServiceInequalityIndex, "wildly uneven", "uneven", "patchy", "fairly even", "even")).Append('\n');
                sb.Append("- political temperature: ").Append(UnitBand(indices.PolarizationIndex, "calm", "settled", "restive", "polarised", "at each other's throats")).Append('\n');
                sb.Append("- faith in the council: ").Append(UnitBand(indices.LegitimacyIndex, "gone", "thin", "grudging", "solid", "strong")).Append('\n');
            }

            AppendDistricts(sb, snapshot);
            sb.Append('\n');
        }

        private static void AppendDistricts(StringBuilder sb, CitySnapshot snapshot)
        {
            if (snapshot.Districts == null || snapshot.Districts.Count == 0) return;

            // Unhappiest first, so a limited prompt spends its budget on the districts with a story.
            // Ties broken on Id, because a sort whose result depends on the input order makes two
            // otherwise identical prompts impossible to diff.
            var districts = new List<DistrictSnapshot>();
            for (int i = 0; i < snapshot.Districts.Count; i++)
            {
                if (snapshot.Districts[i] != null) districts.Add(snapshot.Districts[i]);
            }
            districts.Sort((a, b) =>
            {
                int byHappiness = a.Happiness.CompareTo(b.Happiness);
                if (byHappiness != 0) return byHappiness;
                return string.CompareOrdinal(a.Id, b.Id);
            });

            int count = districts.Count < MaxDistrictsInPrompt ? districts.Count : MaxDistrictsInPrompt;
            if (count == 0) return;

            sb.Append("\nDISTRICTS (id | name | mood | note):\n");
            for (int i = 0; i < count; i++)
            {
                var district = districts[i];
                sb.Append("- ").Append(district.Id)
                  .Append(" | ").Append(Sanitize(district.Name))
                  .Append(" | ").Append(HappinessBand(district.Happiness));

                // Non-negotiable: a city figure standing in for a local one must never be presented
                // as a local fact (§6). Telling the model keeps it out of the prose in the first place.
                if (district.HasCityFallbacks)
                {
                    sb.Append(" | some local measurements are unavailable; do not write about local specifics here");
                }
                sb.Append('\n');
            }

            if (districts.Count > count)
            {
                sb.Append("- (").Append((districts.Count - count).ToString(CultureInfo.InvariantCulture))
                  .Append(" further districts omitted; do not reference them)\n");
            }
        }

        private static void AppendRoster(StringBuilder sb, FlavorRequest request)
        {
            if (request.Parties.Count > 0)
            {
                sb.Append("PARTIES (partyId | archetype | leads on | standing | current name):\n");
                var parties = new List<PartyBrief>(request.Parties);
                parties.Sort((a, b) => string.CompareOrdinal(a.PartyId, b.PartyId));
                for (int i = 0; i < parties.Count; i++)
                {
                    var p = parties[i];
                    sb.Append("- ").Append(p.PartyId)
                      .Append(" | ").Append(Sanitize(p.ArchetypeId))
                      .Append(" | ").Append(Issues.ToKey(p.CoreGrievance))
                      .Append(" | ").Append(Sanitize(p.StatusWord))
                      .Append(" | ").Append(string.IsNullOrEmpty(p.CurrentName) ? "(unnamed)" : Sanitize(p.CurrentName))
                      .Append('\n');
                }
                sb.Append('\n');
            }

            if (request.Factions.Count > 0)
            {
                sb.Append("FACTIONS (factionId | partyId | archetype | leads on | standing | current name):\n");
                var factions = new List<FactionBrief>(request.Factions);
                factions.Sort((a, b) => string.CompareOrdinal(a.FactionId, b.FactionId));
                for (int i = 0; i < factions.Count; i++)
                {
                    var f = factions[i];
                    sb.Append("- ").Append(f.FactionId)
                      .Append(" | ").Append(f.PartyId)
                      .Append(" | ").Append(Sanitize(f.ArchetypeId))
                      .Append(" | ").Append(Issues.ToKey(f.CoreGrievance))
                      .Append(" | ").Append(Sanitize(f.StatusWord))
                      .Append(" | ").Append(string.IsNullOrEmpty(f.CurrentName) ? "(unnamed)" : Sanitize(f.CurrentName))
                      .Append('\n');
                }
                sb.Append('\n');
            }
        }

        private static void AppendEvents(StringBuilder sb, FlavorRequest request)
        {
            if (request.Events.Count == 0) return;

            sb.Append("EVENTS IN PLAY (eventId | title | factual brief):\n");
            var events = new List<EventBrief>(request.Events);
            events.Sort((a, b) => string.CompareOrdinal(a.EventId, b.EventId));
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                sb.Append("- ").Append(e.EventId)
                  .Append(" | ").Append(Sanitize(e.Title))
                  .Append(" | ").Append(Sanitize(e.HeadlineBrief))
                  .Append('\n');
            }
            sb.Append('\n');
        }

        private static void AppendTask(StringBuilder sb, FlavorRequest request)
        {
            int articles = request.ArticleCount;
            if (articles < 0) articles = 0;
            if (articles > 12) articles = 12;

            sb.Append("WRITE:\n");
            if (request.Parties.Count > 0)
            {
                sb.Append("- partyFlavor for every party listed, with a name, a short name, ");
                sb.Append("a description of who it speaks for, and a slogan. Keep an existing name unless ");
                // Only the absorbed side of a merge carries the merged standing word, and that party
                // is off the ballot - the survivor keeps its own name and its own word (see
                // PartyLifecycle.ApplyMerges). So a merge is not a rename occasion for anyone the
                // model can write about, and the clause names only the revived case.
                sb.Append("there is none, or the party's standing says it has recently revived.\n");
            }
            if (request.Factions.Count > 0)
            {
                sb.Append("- factionFlavor for every faction listed, including a leader's name.\n");
            }
            sb.Append("- ").Append(articles.ToString(CultureInfo.InvariantCulture));
            sb.Append(" articles from local outlets covering the city as described above.\n");
            sb.Append("  1. Lead with what happened, to whom, and why it matters. ");
            sb.Append("The concrete change goes in the first sentence, not the last.\n");
            // The claim in the last sentence is now true: FilterAgainstCatalog drops an article whose
            // three ref fields are all empty. Do not soften it again without loosening that check in
            // the same edit - the prompt must not describe a check that does not run, and it must not
            // understate one that does. The round-level consequence is stated for the same reason:
            // FlavorValidationResult.ArticlesAllDiscarded turns an emptied round into a failed one in
            // both holders (ClaudeCliProvider retries it, FlavorCache refuses to load it), so a prompt
            // that mentioned only the per-article drop would understate what a missing refs costs.
            sb.Append("  2. Every article must include refs: name at least one party or district in the ");
            sb.Append("prose by the id given in the lists above, and put that same id in refs. refs takes ");
            sb.Append("at least one of eventId, districtId or partyId, and only ids from the lists above. ");
            sb.Append("Write nothing you cannot point at; an article without refs is dropped. ");
            // The embedded schema is a verbatim copy of data/schemas/politics_flavor.schema.json and
            // FlavorSchemaDriftTests pins the two together, so making it agree with this rule would be
            // a /schema-change. Saying which of the two surfaces governs is the honest fix here.
            sb.Append("The schema below lists refs among the optional properties; that is the schema's ");
            sb.Append("reading and not the rule - the drop runs after schema validation, so an article ");
            sb.Append("that satisfies the schema without refs is discarded all the same. ");
            sb.Append("If that leaves no articles at all, the whole response is rejected, the previous ");
            sb.Append("round's prose is kept and the round is asked for again.\n");
            sb.Append("  3. Never attribute to a subject you have not named. Do not write \"residents say\", ");
            sb.Append("\"officials say\", \"critics say\", \"sources say\", \"some argue\", \"many feel\", ");
            sb.Append("or any variant of them. Name the party, the faction or the district, or do not attribute at all.\n");
            sb.Append("  4. Vary the outlets and the tones. Each article's id must be unique and kebab-case.\n");
            // The cap sentence is left exactly as it was, interpolation and all: FlavorCacheMigration
            // is the single source of truth for both lengths, and the drift gate reads it there.
            sb.Append("  ");
            sb.Append("Headlines are at most ")
              .Append(FlavorCacheMigration.HeadlineMaxLength.ToString(CultureInfo.InvariantCulture))
              .Append(" characters and bodies at most ")
              .Append(FlavorCacheMigration.BodyMaxLength.ToString(CultureInfo.InvariantCulture))
              .Append(" - a longer one fails validation and the whole response is discarded.\n");
            AppendElectionCoverage(sb, request);
            if (request.Events.Count > 0)
            {
                sb.Append("- eventProse for every event listed: how that event lands in THIS city specifically.\n");
            }
            sb.Append("- generatedAtSimDate exactly \"").Append(request.Date.ToString()).Append("\".\n");
            sb.Append("- schemaVersion exactly ").Append(FlavorSchema.SupportedSchemaVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append(" (the only number allowed in your entire response).\n\n");
        }

        /// <summary>
        /// The extra pieces an election round asks for, emitted inside WRITE so that a non-election
        /// prompt is unchanged byte for byte.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Neither <see cref="FlavorRequest"/> nor <see cref="PartyBrief"/> carries a vote share, a
        /// seat count or a turnout figure — deliberately, see the remarks on <c>PartyBrief</c>. The
        /// block says that outright rather than leaving the gap for the model to fill, because "write
        /// the result" with no result in the brief is an invitation to invent one (non-negotiable #1).
        /// </para>
        /// <para>
        /// It no longer claims the standing word is "the whole of the outcome you may write from",
        /// because it is not one. <see cref="PartyBrief.StandingWord"/> says who governs as things
        /// stand, and on the morning after a count that is the arrangement the count has just
        /// unseated — formation has not run. So the block tells the model there is no winner in the
        /// brief and not to name one, which is the true statement and the safe one.
        /// </para>
        /// <para>
        /// The slot list says the same thing as that prohibition, which it did not always do. Slots
        /// asking for "the winning side's reaction" and "the losing side's reaction" required the
        /// model to decide the outcome before it could write them, and a specific instruction beats
        /// an abstract ban every time — so the two reaction slots ask instead for a party's own claim
        /// on the mandate and a party's own challenge to the reading of the count, both of which are
        /// true of a party whichever way the count went. That is the framing
        /// <see cref="StaticPoolContent.ElectionClaimHeadlines"/> and
        /// <see cref="StaticPoolContent.ElectionChallengeHeadlines"/> already file under; keep the
        /// prompt and the pool describing one set of slots.
        /// </para>
        /// <para>
        /// The closing sentence carries no worked example, and must not gain one. It used to end
        /// "and write \"held the council\" or \"lost ground\" rather than any figure" — an exemplar
        /// pair naming a winner and a loser one clause after forbidding both, which is the same shape
        /// as the reaction slots above: a concrete imitable phrase next to an abstract ban, and the
        /// concrete one wins. Its job — words instead of figures — is already carried by rule 2 in
        /// <see cref="AppendRules"/> and by the first half of this paragraph, so it was redundant as
        /// well as contradictory. Any example added here has to be sayable without knowing the
        /// outcome; the whole block is pinned verbatim by the golden strings in
        /// <c>FlavorPromptBuilderTests</c>, so an edit has to be made in both places on purpose.
        /// </para>
        /// </remarks>
        private static void AppendElectionCoverage(StringBuilder sb, FlavorRequest request)
        {
            if (request.Reason != FlavorWakeReason.Election) return;

            sb.Append("  The election just decided is the round's lead. Among those articles, include:\n");
            sb.Append("  a) a result piece: that the count has happened and what it changes procedurally, ");
            sb.Append("not what it decided,\n");
            sb.Append("  b) a piece carrying one party's own claim on the mandate,\n");
            sb.Append("  c) a piece carrying one party's own challenge to the reading of the count");
            if (request.Theme == RegionTheme.Eu)
            {
                sb.Append(",\n");
                sb.Append("  d) a piece on the coalition outlook - who might govern with whom, and on what, ");
                sb.Append("with nothing settled yet.\n");
            }
            else
            {
                sb.Append(".\n");
            }
            sb.Append("  Name the parties involved by id only. You have not been given the vote shares, the ");
            sb.Append("seat counts or the turnout, and you must not invent them; the dashboard carries the ");
            sb.Append("figures. You have not been given the winner either. The standing word in the party ");
            sb.Append("list says who governs as things stand, which is the arrangement the count may have ");
            sb.Append("just unseated - it is not the result. Do not name a winner or a loser. Write the two ");
            sb.Append("reactions as what a party says of the count, not as what the count decided.\n");
        }

        private static void AppendSchema(StringBuilder sb)
        {
            sb.Append("SCHEMA - your output is validated against this and silently discarded if it fails:\n");
            sb.Append(FlavorSchema.EmbeddedJson);
            sb.Append('\n');
        }

        // ---- banding -------------------------------------------------------------------------
        //
        // Every threshold below picks an adjective. None of them feeds engine state, so none of them
        // is a tuning coefficient in the sense of non-negotiable "no hardcoded tuning constants" -
        // changing one changes a word in a prompt and nothing else.
        //
        // ONE RULE, stated here because four bands now depend on it: THE BOTTOM BAND OF EVERY LINE
        // FED BY A SENSOR MUST BE TRUE OF AN UNMEASURED CITY AS WELL AS AN EMPTY ONE. Every reading
        // in the v4 block arrives as a struct with a zero default, and a sensor that never ran, a
        // singleton guard that declined to read, or an Invalidate() before the first sample all
        // produce exactly the same zeros as a city that genuinely has none of the thing. The contract
        // cannot tell them apart, so a bottom band phrased as a positive fact - "a brand-new
        // settlement", "nobody is homeless" - states something about the city on the strength of a
        // measurement that may never have happened, and goes on stating it for the rest of the
        // session while the lines fed by other sensor families stay correct beside it. A model handed
        // "a metropolis" and "a brand-new settlement" two lines apart writes the contradiction into
        // the prose, from a defect that logged one warning at startup and is otherwise silent.
        // Phrase the bottom band so that it is true either way, and it costs nothing.

        private static string SystemDescription(RegionTheme theme) =>
            theme == RegionTheme.Na
                ? "first-past-the-post wards with a directly elected mayor; two dominant parties, each with internal factions"
                : "proportional representation with coalition government; several parties, none usually near a majority";

        private static string ReasonDescription(FlavorWakeReason reason)
        {
            switch (reason)
            {
                case FlavorWakeReason.Election: return "an election has just been decided";
                case FlavorWakeReason.Manual: return "a mid-cycle news round, requested by the desk";
                default: return "the annual round-up";
            }
        }

        private static string PopulationBand(int population)
        {
            if (population < 2_000) return "a village";
            if (population < 10_000) return "a small town";
            if (population < 50_000) return "a town";
            if (population < 200_000) return "a city";
            if (population < 750_000) return "a large city";
            return "a metropolis";
        }

        /// <summary>
        /// Happiness arrives 0-100 (see CitySnapshot). Public because
        /// <see cref="StaticPoolProvider"/> fills the same <c>{mood}</c> slot in its canned prose and
        /// the two must agree - a fallback article calling the city "content" while the prompt calls
        /// it "furious" would read as a bug the moment the CLI came back.
        /// </summary>
        public static string HappinessBand(double happiness) =>
            UnitBand(Clamp01(happiness / 100.0), "furious", "unhappy", "grumbling", "content", "delighted");

        /// <summary>
        /// The same banding as <see cref="HappinessBand"/>, as an index 0 (furious) to 4 (delighted).
        /// <see cref="StaticPoolProvider"/> uses it to pick a tone that matches the mood, so a canned
        /// article does not report a furious city in a celebratory voice.
        /// </summary>
        public static int HappinessBandIndex(double happiness)
        {
            double v = Clamp01(happiness / 100.0);
            if (v < 0.20) return 0;
            if (v < 0.40) return 1;
            if (v < 0.60) return 2;
            if (v < 0.80) return 3;
            return 4;
        }

        /// <summary>Unemployment arrives 0-1.</summary>
        private static string UnemploymentBand(double unemployment) =>
            UnitBand(Clamp01(1.0 - unemployment * 5.0), "mass unemployment", "high unemployment",
                     "noticeable unemployment", "most people working", "near full employment");

        private static string RentBurdenBand(double rentBurden) =>
            UnitBand(Clamp01(1.0 - rentBurden), "rents are crushing", "rents bite hard",
                     "rents are a common complaint", "rents are manageable", "rents are easy");

        /// <summary>
        /// What is left of a day's household income after rent, upkeep, goods and utility fees, as a
        /// phrase.
        /// </summary>
        /// <remarks>
        /// Qualitative, like every other line in this block, because non-negotiable #1 runs in both
        /// directions: the model must not read a number here any more than it may write one back.
        /// The margin arrives signed and uncapped, so it is clamped into the band scale rather than
        /// asserted to be in it — a district drawing down savings reads -0.4, not 0.
        /// </remarks>
        private static string DisposableMarginBand(double disposableMargin) =>
            UnitBand(Clamp01(disposableMargin), "households are going backwards",
                     "nothing is left at the end of the day", "budgets are tight",
                     "most households have something spare", "households are comfortable");

        private static string CoverageBand(double coverage) =>
            UnitBand(Clamp01(coverage), "absent", "thin", "patchy", "decent", "excellent");

        private static string CommuteBand(double minutes)
        {
            if (minutes <= 0.0) return "unmeasured";
            if (minutes < 12.0) return "short";
            if (minutes < 25.0) return "reasonable";
            if (minutes < 40.0) return "long";
            if (minutes < 60.0) return "punishing";
            return "an ordeal";
        }

        private static string BudgetBand(long balance, long debt)
        {
            if (balance < 0 && debt > 0) return "in deficit and carrying debt";
            if (balance < 0) return "spending more than it takes in";
            if (debt > 0) return "balanced, but servicing debt";
            return "in the black";
        }

        /// <summary>
        /// Homelessness as a phrase. The share arrives 0-1 — the sensor has already divided the
        /// game's 0-100 percentage down (see <see cref="CityStatistics.HomelessShare"/>).
        /// </summary>
        /// <remarks>
        /// Not <see cref="UnitBand"/>: real homelessness lives in the bottom couple of percent of the
        /// scale, so five even fifths would report a city in crisis and a city with nobody sleeping
        /// rough in the same words. The bottom band says "no visible homelessness" rather than "none"
        /// because a city whose statistics sensor found nothing also reads 0 here, and the contract
        /// cannot tell the two apart — a phrase true of both is the honest one, on the same reasoning
        /// that makes <see cref="CommuteBand"/> say "unmeasured".
        /// </remarks>
        private static string HomelessnessBand(double share)
        {
            double v = Clamp01(share);
            if (v < 0.005) return "no visible homelessness";
            if (v < 0.02) return "a few households with nowhere to live";
            if (v < 0.05) return "homelessness is a visible problem";
            if (v < 0.10) return "homelessness is a serious problem";
            return "a homelessness crisis";
        }

        /// <summary>
        /// Whether the city is filling or emptying, and whether unhappiness is why people are going.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Bands the arrivals' share of all movement rather than either count on its own, which is
        /// what lets the line stand while scout 0004 Q2 is open: nobody yet knows whether these
        /// statistics cover a day, a month or the whole life of the city, and a ratio of two figures
        /// gathered on the same footing reads the same either way. A line banding arrivals against
        /// population would not.
        /// </para>
        /// <para>
        /// The second clause is the political half. <see cref="CityStatistics.MovedAwayUnhappy"/> is
        /// carried separately from <see cref="CityStatistics.CitizensMovedAway"/> precisely because
        /// the two mean opposite things — people leaving because there is nowhere to live is a
        /// housing story, people leaving because they are miserable is a government story — and a
        /// prompt that printed only the total would hand the model the wrong one.
        /// </para>
        /// </remarks>
        private static string MigrationBand(CityStatistics statistics)
        {
            int arriving = statistics.CitizensMovedIn < 0 ? 0 : statistics.CitizensMovedIn;
            int leaving = statistics.CitizensMovedAway < 0 ? 0 : statistics.CitizensMovedAway;

            long total = (long)arriving + leaving;
            if (total == 0) return "few people are moving either way";

            string flow = UnitBand((double)arriving / total,
                                   "people are leaving in numbers", "more are leaving than arriving",
                                   "arrivals and departures roughly balance",
                                   "more are arriving than leaving", "people are pouring in");

            if (leaving > 0 && statistics.MovedAwayUnhappy > 0)
            {
                double unhappyShare = (double)statistics.MovedAwayUnhappy / leaving;
                if (unhappyShare >= 0.5)
                    return flow + ", and most of those leaving go because they are unhappy here";
                if (unhappyShare >= 0.2)
                    return flow + ", and a good share of those leaving go because they are unhappy here";
            }

            return flow;
        }

        /// <summary>
        /// Visitor pressure, measured against the resident population rather than in absolute terms —
        /// a thousand tourists is a curiosity in a metropolis and an occupation in a village.
        /// </summary>
        /// <remarks>
        /// <see cref="TourismLevels.Attractiveness"/> is deliberately not banded here. It is a raw
        /// dimensionless index by contract, so every threshold would be measured against a reference
        /// maximum nobody has established, and the adjective would then be chosen by that guess
        /// rather than by the city. Lodging is reported only at the two ends because a hotel trade
        /// running half full is not a story, and a clause that printed every single month would cost
        /// prompt budget without ever telling the model anything.
        /// </remarks>
        private static string TourismBand(TourismLevels tourism, int population)
        {
            int tourists = tourism.Tourists < 0 ? 0 : tourism.Tourists;

            string pressure;
            double perResident = population > 0 ? (double)tourists / population : 0.0;
            if (perResident < 0.01) pressure = "the city is not on the tourist map";
            else if (perResident < 0.05) pressure = "a few visitors about";
            else if (perResident < 0.15) pressure = "a steady tourist trade";
            else if (perResident < 0.30) pressure = "visitors are a presence everywhere";
            else pressure = "the city is thronged with visitors";

            if (tourism.LodgingTotal > 0)
            {
                double occupancy = (double)tourism.LodgingUsed / tourism.LodgingTotal;
                if (occupancy >= 0.90) return pressure + "; the hotels are full";
                if (occupancy <= 0.25) return pressure + "; hotel beds are going empty";
            }

            return pressure;
        }

        /// <summary>
        /// How far into the milestone track the city has got, as a phrase. This is also what the game
        /// calls "city level" — one number, not two (see <see cref="ProgressionState.MilestoneLevel"/>).
        /// </summary>
        /// <remarks>
        /// It says what kind of city the model is writing about in a way population cannot: a village
        /// and a small city of the same size have different arguments available to them, because one
        /// of them has unlocked public transport and the other has not.
        /// <para>
        /// Level 0 says "no milestones on record" rather than describing a brand-new settlement,
        /// under the bottom-band rule stated at the head of this section. It is the reading where
        /// getting it wrong is loudest: <c>AgoraProgressionSensorSystem</c> reports nothing for the
        /// rest of the session if <c>CreateQueries</c> throws, and the milestone singleton is guarded
        /// besides, so a 400,000-resident city can arrive here at level 0 — and the size line, fed by
        /// a different sensor family, would go on calling it a metropolis in the line above.
        /// </para>
        /// <para>
        /// <see cref="ProgressionState.MilestoneProgress"/> is deliberately not printed beside it. The
        /// obvious clause — "and close to its next milestone" past some threshold — is permanently
        /// wrong on a city that has finished the track, where there is no next milestone for the
        /// progress figure to be close to, and a line that is wrong forever on the largest cities is
        /// worse than one that is absent.
        /// </para>
        /// </remarks>
        private static string MilestoneBand(int milestoneLevel)
        {
            if (milestoneLevel <= 0) return "no milestones on record";
            if (milestoneLevel < 5) return "early in its development, with much still out of reach";
            if (milestoneLevel < 10) return "growing into itself, unlocking more as it goes";
            if (milestoneLevel < 15) return "well established, with most of its options open";
            return "a mature city with little left to unlock";
        }

        /// <summary>Maps a [0,1] value onto five adjectives, lowest first.</summary>
        private static string UnitBand(double value, string veryLow, string low, string middle, string high, string veryHigh)
        {
            double v = Clamp01(value);
            if (v < 0.20) return veryLow;
            if (v < 0.40) return low;
            if (v < 0.60) return middle;
            if (v < 0.80) return high;
            return veryHigh;
        }

        /// <summary>
        /// CrimeRate has no documented unit on the snapshot contract, so treat anything above 1 as a
        /// 0-100 scale and anything else as already normalised. A wrong guess costs one adjective.
        /// </summary>
        private static double Normalise01(double value) => Clamp01(value > 1.0 ? value / 100.0 : value);

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value)) return 0.0;
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }

        /// <summary>
        /// Flattens player-authored text (district names) for the prompt: newlines and pipes would
        /// break the line-per-record layout the model is reading, and a name is not trusted input.
        /// </summary>
        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length && sb.Length < 120; i++)
            {
                char c = text[i];
                if (c == '\r' || c == '\n' || c == '|') { sb.Append(' '); continue; }
                if (char.IsControl(c)) continue;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
