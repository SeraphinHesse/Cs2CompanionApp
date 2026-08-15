using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Mod.Llm;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// What the prompt builder is allowed to cut when a city outgrows the character cap.
    ///
    /// <para>
    /// <c>MaxDistrictsInPrompt</c> bounds the city description to a few kilobytes today, so nothing a
    /// real city can do reaches the truncation path — which is exactly why it is pinned here. The
    /// path is one edit to <c>AppendSituation</c> away from running for the first time, in a build
    /// nobody thought was touching prompt sizing, and its failure mode when it goes wrong is a schema
    /// cut in half: every generation fails validation, silently, on the largest cities only.
    /// </para>
    ///
    /// <para>
    /// The fixtures below reach the cap through the two fields the builder passes through verbatim
    /// (district and event ids are engine-authored, so unlike names they are not length-capped by
    /// <c>Sanitize</c>). That is a lever, not a claim that such an id is realistic.
    /// </para>
    /// </summary>
    public class FlavorPromptBuilderTests
    {
        /// <summary>
        /// The builder's own notice, repeated here rather than exposed. A test that read the constant
        /// would still pass if the constant were emptied, which is the one change that would leave
        /// the model reading a silently shortened city as though it were the whole city.
        /// </summary>
        private const string TruncationNotice =
            "- (the rest of the city description was omitted to fit the prompt; do not refer to it)\n";

        private const string SchemaHeader =
            "SCHEMA - your output is validated against this and silently discarded if it fails:\n";

        [Fact]
        public void ModestCity_IsWellUnderTheCapAndKeepsEveryDistrict()
        {
            // The baseline the two truncation cases are read against: if this one ever truncates, the
            // fixtures below stop testing what they claim to.
            string prompt = FlavorPromptBuilder.Build(Request(districts: 6, districtIdLength: 12));

            Assert.True(prompt.Length < FlavorPromptBuilder.MaxPromptCharacters);
            Assert.DoesNotContain(TruncationNotice, prompt);
            Assert.EndsWith(SchemaHeader + FlavorSchema.EmbeddedJson + "\n", prompt);
        }

        [Fact]
        public void SituationPastTheCap_IsCutAndTheSchemaSurvivesWhole()
        {
            // Twelve districts at twelve thousand characters each: the city description alone is past
            // the cap, before a single byte of role, rules, roster, task or schema.
            var request = Request(districts: FlavorPromptBuilder.MaxDistrictsInPrompt, districtIdLength: 12_000);

            string prompt = FlavorPromptBuilder.Build(request);

            Assert.True(prompt.Length <= FlavorPromptBuilder.MaxPromptCharacters,
                        "the situation block is the section that gives way; the prompt must come in under the cap");

            // The whole schema, character for character, as the last thing in the prompt. This is the
            // assertion the cap logic exists for: a model handed half a schema fails validation every
            // time and the log blames the model.
            Assert.EndsWith(SchemaHeader + FlavorSchema.EmbeddedJson + "\n", prompt);

            // Everything between the cut and the schema is intact too.
            Assert.Contains("PARTIES (partyId", prompt);
            Assert.Contains("WRITE:", prompt);

            // The cut is announced, and made at a line boundary, so the model never reads half a
            // district record and never treats the surviving part as the whole city.
            Assert.Contains(TruncationNotice, prompt);
            int notice = prompt.IndexOf(TruncationNotice, System.StringComparison.Ordinal);
            Assert.Equal('\n', prompt[notice - 1]);

            // What survived is the front of the description - the unhappiest districts, which is what
            // the block is sorted to put first.
            Assert.Contains("THE CITY (qualitative only", prompt);
            Assert.Contains("district-00", prompt);
        }

        [Fact]
        public void FixedSectionsPastTheCap_GoOverRatherThanLoseTheSchema()
        {
            // The documented overflow: the sections that cannot degrade - roster, events, task,
            // schema - exceed the cap on their own, so there is nothing left to spend. Pinned because
            // it is a deliberate trade and not a missing bound: an oversized prompt costs tokens,
            // whereas a truncated schema fails the generation, and fails it without saying so.
            var request = Request(districts: 4, districtIdLength: 12);
            request.Events.Add(new EventBrief
            {
                EventId = new string('e', FlavorPromptBuilder.MaxPromptCharacters * 2),
                Title = "budget hearing",
                HeadlineBrief = "the council meets"
            });

            string prompt = FlavorPromptBuilder.Build(request);

            Assert.True(prompt.Length > FlavorPromptBuilder.MaxPromptCharacters);
            Assert.EndsWith(SchemaHeader + FlavorSchema.EmbeddedJson + "\n", prompt);
            Assert.Contains("EVENTS IN PLAY", prompt);
            Assert.Contains("WRITE:", prompt);

            // No budget for the notice either, so the city description goes rather than arriving as a
            // stub the model would read as the whole city.
            Assert.DoesNotContain("THE CITY (qualitative only", prompt);
            Assert.DoesNotContain(TruncationNotice, prompt);
        }

        // ---- the article instruction ------------------------------------------------------------

        [Fact]
        public void EveryRound_DemandsRefsAndBansAttributionToNobody()
        {
            // The two failure modes the instruction was rewritten for: an article that references no
            // id at all, and one that sources every claim to "residents". Both produced prose with no
            // identifiable subject, and both are only prevented by wording actually reaching the model.
            string prompt = FlavorPromptBuilder.Build(Request(districts: 6, districtIdLength: 12));

            // The contract, and only the contract: every article carries a catalog id in refs, and
            // nothing is sourced to a subject the article did not name. The exact wording around them
            // is prose and is free to move.
            // "Every article must" is the load-bearing phrase, and it is the universal quantifier that
            // a previous round lost: a prompt that made refs optional would still satisfy every other
            // assertion here. The second assertion pins the other half of the same rule - the id has
            // to be in the prose as well as in refs, which is what makes the reference checkable at
            // all - and the third pins the consequence, which FlavorValidator.FilterAgainstCatalog now
            // actually carries out.
            Assert.Contains("Every article must include refs", prompt);
            Assert.Contains("name at least one party or district in the prose by the id given in the " +
                            "lists above, and put that same id in refs", prompt);
            Assert.Contains("at least one of eventId, districtId or partyId", prompt);
            Assert.Contains("Write nothing you cannot point at", prompt);
            Assert.Contains("an article without refs is dropped", prompt);
            Assert.Contains("Never attribute to a subject you have not named", prompt);
            Assert.Contains("\"residents say\"", prompt);
            Assert.Contains("\"officials say\"", prompt);

            // The two phrasings this replaced, in order of weakness: the original permissive one, and
            // the deliberately softened "Always include refs" that stood while the check it described
            // did not run. Left as assertions because restoring either would pass every check above
            // but one, and would put the prompt back to understating a rule the validator enforces.
            Assert.DoesNotContain("Set refs only to IDs from the lists above", prompt);
            Assert.DoesNotContain("Always include refs", prompt);

            // The embedded schema, printed further down the same prompt, lists refs among the optional
            // properties - it is a verbatim copy of the repo schema and changing it is a
            // /schema-change. So the rule says which of the two surfaces governs, rather than leaving
            // one prompt stating two things about one field.
            Assert.Contains("\"refs\": {", prompt);
            Assert.Contains("The schema below lists refs among the optional properties", prompt);
            Assert.Contains("the drop runs after schema validation", prompt);
        }

        [Fact]
        public void EveryRound_StillPrintsBothLengthCapsFromTheMigrationConstants()
        {
            // The drift gate's arrangement: the prompt quotes FlavorCacheMigration rather than a
            // literal, so raising a cap in one place cannot leave the model writing to the old one.
            string prompt = FlavorPromptBuilder.Build(Request(districts: 6, districtIdLength: 12));

            Assert.Contains(
                "Headlines are at most " + FlavorCacheMigration.HeadlineMaxLength +
                " characters and bodies at most " + FlavorCacheMigration.BodyMaxLength +
                " - a longer one fails validation and the whole response is discarded.",
                prompt);
        }

        // ---- the election block -----------------------------------------------------------------

        [Fact]
        public void YearlyRound_AsksForNoElectionCoverageAtAll()
        {
            string prompt = FlavorPromptBuilder.Build(Request(districts: 6, districtIdLength: 12));

            Assert.DoesNotContain("The election just decided", prompt);
            Assert.DoesNotContain("a) a result piece", prompt);
            Assert.DoesNotContain("claim on the mandate", prompt);
            Assert.DoesNotContain("coalition outlook", prompt);
        }

        [Fact]
        public void ElectionRound_IsTheOnlyThingThatChangesInTheProsePrompt()
        {
            // The block is emitted inside WRITE rather than as a section of its own, so an election
            // prompt must differ from a yearly one only by the block and the raised article count.
            // Anything else moving means the ordering of the prompt changed for every round.
            var yearly = Request(districts: 6, districtIdLength: 12);
            yearly.Events.Add(Event());

            var election = Request(districts: 6, districtIdLength: 12);
            election.Events.Add(Event());
            election.Reason = FlavorWakeReason.Election;
            election.ArticleCount = yearly.ArticleCount;

            string a = FlavorPromptBuilder.Build(yearly);
            string b = FlavorPromptBuilder.Build(election);

            // Occasion is set from Reason, so that one line legitimately differs; splice the yearly
            // wording back in and the remainder must be the yearly prompt plus the block.
            b = b.Replace("Occasion: an election has just been decided",
                          "Occasion: the annual round-up");

            int inserted = b.IndexOf("  The election just decided", System.StringComparison.Ordinal);
            Assert.True(inserted > 0, "the block must be emitted");

            // The block runs to the line after it, which is eventProse — hence the event on both
            // fixtures above. That is the arrangement of a real election round anyway: the tick that
            // woke the flavor provider has just written the result event, so the block is never the
            // last thing in WRITE in practice, and testing it as though it were would test a shape
            // the mod does not produce.
            int end = b.IndexOf("\n- eventProse", System.StringComparison.Ordinal);
            Assert.True(end > inserted, "the fixtures carry an event, so eventProse follows the block");

            Assert.Equal(a, b.Remove(inserted, end + 1 - inserted));
        }

        // The block below is pinned whole, per theme, rather than by a handful of Contains. Two
        // rounds of substring assertions let the same class of defect through - a slot or an exemplar
        // that requires the model to decide the outcome before it can write - because a substring test
        // only fails on the phrasing whoever wrote it thought of. "the victor's reaction", "the party
        // that won", "held the council" and "lost ground" all sailed past the previous helper, the
        // last two while sitting in the file. This surface's exact text is the contract with the
        // model, so the exact text is what the test holds: any edit to it has to be made deliberately
        // here as well as in the builder, and a reviewer reading this file sees what the model is told.

        /// <summary>
        /// The whole election block for a first-past-the-post round, verbatim.
        /// </summary>
        /// <remarks>
        /// No figure and no worked example appears in it, and both absences are load-bearing: the
        /// model has not been given the shares, the seats, the turnout or the winner, so any exemplar
        /// it could imitate would have to be sayable without knowing the outcome. The pair that used
        /// to close it - "held the council" or "lost ground" - was not, and named a winner and a loser
        /// one clause after forbidding both.
        /// </remarks>
        private const string ExpectedElectionBlockNa =
            "  The election just decided is the round's lead. Among those articles, include:\n" +
            "  a) a result piece: that the count has happened and what it changes procedurally, " +
            "not what it decided,\n" +
            "  b) a piece carrying one party's own claim on the mandate,\n" +
            "  c) a piece carrying one party's own challenge to the reading of the count.\n" +
            "  Name the parties involved by id only. You have not been given the vote shares, the " +
            "seat counts or the turnout, and you must not invent them; the dashboard carries the " +
            "figures. You have not been given the winner either. The standing word in the party " +
            "list says who governs as things stand, which is the arrangement the count may have " +
            "just unseated - it is not the result. Do not name a winner or a loser. Write the two " +
            "reactions as what a party says of the count, not as what the count decided.\n";

        /// <summary>
        /// The same block under proportional representation: one extra slot, and the comma before it.
        /// </summary>
        private const string ExpectedElectionBlockEu =
            "  The election just decided is the round's lead. Among those articles, include:\n" +
            "  a) a result piece: that the count has happened and what it changes procedurally, " +
            "not what it decided,\n" +
            "  b) a piece carrying one party's own claim on the mandate,\n" +
            "  c) a piece carrying one party's own challenge to the reading of the count,\n" +
            "  d) a piece on the coalition outlook - who might govern with whom, and on what, " +
            "with nothing settled yet.\n" +
            "  Name the parties involved by id only. You have not been given the vote shares, the " +
            "seat counts or the turnout, and you must not invent them; the dashboard carries the " +
            "figures. You have not been given the winner either. The standing word in the party " +
            "list says who governs as things stand, which is the arrangement the count may have " +
            "just unseated - it is not the result. Do not name a winner or a loser. Write the two " +
            "reactions as what a party says of the count, not as what the count decided.\n";

        [Fact]
        public void ElectionUnderNa_EmitsExactlyTheExpectedBlock()
        {
            var request = Request(districts: 6, districtIdLength: 12);
            request.Reason = FlavorWakeReason.Election;
            request.Theme = RegionTheme.Na;

            string prompt = FlavorPromptBuilder.Build(request);

            Assert.Equal(ExpectedElectionBlockNa, ElectionBlock(prompt));

            // The one thing the slice cannot speak for: there are no coalitions to have an outlook on
            // under first-past-the-post wards with a directly elected mayor, so the phrase must be
            // absent from the whole prompt and not merely from the block.
            Assert.DoesNotContain("coalition outlook", prompt);
        }

        [Fact]
        public void ElectionUnderEu_EmitsExactlyTheExpectedBlock()
        {
            var request = Request(districts: 6, districtIdLength: 12);
            request.Reason = FlavorWakeReason.Election;
            request.Theme = RegionTheme.Eu;

            string prompt = FlavorPromptBuilder.Build(request);

            Assert.Equal(ExpectedElectionBlockEu, ElectionBlock(prompt));
        }

        /// <summary>
        /// The election block, cut out of the finished prompt.
        /// </summary>
        /// <remarks>
        /// The builder emits the block inside WRITE rather than as a section of its own, so there is
        /// no accessor to call. The slice runs from the block's first line to the next top-level WRITE
        /// bullet, which is <c>eventProse</c> when the round carries events and
        /// <c>generatedAtSimDate</c> when it does not — the block's own lines are indented two spaces
        /// and none of them starts <c>"- "</c>, so the first <c>"\n- "</c> at or after the start is
        /// the block's own trailing newline. Both ends assert rather than returning a short string:
        /// a slice that silently found nothing would make the equality below compare two wrong values.
        /// </remarks>
        private static string ElectionBlock(string prompt)
        {
            int start = prompt.IndexOf("  The election just decided", System.StringComparison.Ordinal);
            Assert.True(start >= 0, "the election block must be emitted on an election round");

            int end = prompt.IndexOf("\n- ", start, System.StringComparison.Ordinal);
            Assert.True(end > start, "the block is followed by a top-level WRITE bullet");

            return prompt.Substring(start, end + 1 - start);
        }

        // ---- the v4 city-statistics block ---------------------------------------------------------
        //
        // /schema-change step 3: a snapshot field the model cannot see is a contract break, because
        // the model then writes prose about a city it has no view of. These pin the four lines the
        // v4 fields arrive as, the two fields that deliberately do not arrive at all, and - the one
        // that would fail invisibly - that none of them arrives as a figure.

        // None of these pins a sentence. The adjectives in a band table are prose vocabulary - the
        // builder says so itself - so a test that held them would go red on a copy edit, and a suite
        // that goes red on copy edits trains people to delete tests. What is pinned instead is what
        // an edit must not change without meaning to: that a field reaches the model at all, where
        // each band boundary sits, that the bands are ordered and distinct, and that a conditional
        // clause is conditional. Every one of those fails on a real defect and none on a reword.

        [Fact]
        public void TheCityBlock_CarriesHomelessnessMigrationVisitorsAndStanding()
        {
            // /schema-change step 3 in one test: the four lines exist and each one moves when the
            // field behind it moves. A line whose text is the same on a city in a homelessness crisis
            // as on an empty snapshot is not reporting the field, whatever it says.
            var measured = Request(districts: 3, districtIdLength: 12);
            measured.Snapshot.Statistics = new CityStatistics(
                homeless: 900, homelessShare: 0.06, citizensMovedIn: 200, citizensMovedAway: 1_400,
                movedAwayUnhappy: 900, births: 300, deaths: 250, garbageProductionRate: 4_000.0);
            measured.Snapshot.Tourism = new TourismLevels(
                tourists: 36_000, attractiveness: 120, lodgingUsed: 950, lodgingTotal: 1_000);
            measured.Snapshot.Progression = new ProgressionState(
                milestoneLevel: 12, experience: 40_000, milestoneProgress: 0.4);

            var unmeasured = Request(districts: 3, districtIdLength: 12);

            string[] labels = { "homelessness", "migration", "visitors", "city standing" };
            for (int i = 0; i < labels.Length; i++)
            {
                string described = CityLine(measured, labels[i]);
                Assert.NotEqual(string.Empty, described);
                Assert.NotEqual(CityLine(unmeasured, labels[i]), described);
            }
        }

        [Fact]
        public void TheCityBlock_SaysTheNewLinesAreCityWideAndNotADistrictFact()
        {
            // Every one of the four is city-only at source: CityStatisticsSystem is keyed by
            // (StatisticType, parameter) with no district dimension, Tourism lives on the city entity
            // and a district has no milestone. The district block carries no equivalent, so the only
            // route by which one becomes a local claim is the model putting it there. This is the
            // section that fails invisibly if it is cut - the prose still reads fine, it is just
            // about a district that was never measured.
            //
            // Held by content rather than verbatim: the note may be rewritten, but it has to go on
            // naming all four lines, and dropping one from the list is exactly how a rewrite would
            // quietly stop covering it.
            string note = CityLine(Request(districts: 3, districtIdLength: 12), "note");

            Assert.Contains("homelessness", note);
            Assert.Contains("migration", note);
            Assert.Contains("visitors", note);
            Assert.Contains("city standing", note);
            Assert.Contains("city-wide", note);
            Assert.Contains("do not attribute", note);
        }

        [Fact]
        public void Homelessness_BandsAtTheDocumentedThresholdsAndNowhereElse()
        {
            var request = Request(districts: 1, districtIdLength: 12);
            System.Func<double, string> line = share =>
            {
                request.Snapshot.Statistics = new CityStatistics(0, share, 0, 0, 0, 0, 0, 0.0);
                return CityLine(request, "homelessness");
            };

            Assert.Equal(5, BandRuns(new[] { 0.0, 0.0049, 0.005, 0.019, 0.02, 0.049, 0.05, 0.099, 0.10, 0.90 },
                                     line).Count);

            // Where the four thresholds are. The scale is packed into the bottom few percent of the
            // range on purpose: a city at 6% homeless is in serious trouble, not "a fifth of the way
            // up the scale". A sensor that forgot to divide the game's 0-100 percentage down would
            // land every fixture here in the top band, which is the failure the contract's own remarks
            // on HomelessShare warn about - and this test would then see one band, not five.
            AssertThresholds(line, new[] { 0.0049, 0.019, 0.049, 0.099 }, new[] { 0.005, 0.02, 0.05, 0.10 });
        }

        [Fact]
        public void Migration_BandsOnTheArrivalsShareOfAllMovement()
        {
            // Banded on the ratio rather than on either count, which is what lets the line stand while
            // scout 0004 Q2 is open: nobody yet knows what period these statistics cover, and a ratio
            // of two figures gathered on the same footing reads the same either way.
            var request = Request(districts: 1, districtIdLength: 12);
            System.Func<double, string> line = arrivalsShare =>
            {
                int arriving = (int)System.Math.Round(arrivalsShare * 1_000.0);
                request.Snapshot.Statistics =
                    new CityStatistics(0, 0.0, arriving, 1_000 - arriving, 0, 0, 0, 0.0);
                return CityLine(request, "migration");
            };

            Assert.Equal(5, BandRuns(new[] { 0.0, 0.19, 0.20, 0.39, 0.40, 0.59, 0.60, 0.79, 0.80, 1.0 },
                                     line).Count);
            AssertThresholds(line, new[] { 0.19, 0.39, 0.59, 0.79 }, new[] { 0.20, 0.40, 0.60, 0.80 });
        }

        [Fact]
        public void Migration_OnAnUnmeasuredCity_ClaimsNothingEitherWay()
        {
            // Zero in and zero out is what a capture taken before the statistics sensor ran looks
            // like, and it is indistinguishable on the contract from a genuinely settled city. So the
            // phrase has to be one that is true of both, which means it must not be any of the five
            // the band scale hands out - each of those is a claim about which way the city is going,
            // and 0/0 does not support one.
            var request = Request(districts: 1, districtIdLength: 12);
            string unmeasured = CityLine(request, "migration");

            System.Func<double, string> line = arrivalsShare =>
            {
                int arriving = (int)System.Math.Round(arrivalsShare * 1_000.0);
                request.Snapshot.Statistics =
                    new CityStatistics(0, 0.0, arriving, 1_000 - arriving, 0, 0, 0, 0.0);
                return CityLine(request, "migration");
            };

            List<string> bands = BandRuns(new[] { 0.0, 0.30, 0.50, 0.70, 1.0 }, line);
            Assert.Equal(5, bands.Count);
            Assert.DoesNotContain(unmeasured, bands);
        }

        [Fact]
        public void Migration_MentionsUnhappinessOnlyWhenItIsWhyPeopleAreGoing()
        {
            // MovedAwayUnhappy is carried apart from CitizensMovedAway because the two mean opposite
            // things politically: people leaving because there is nowhere to live is a housing story,
            // people leaving because they are miserable is a government story. A city losing residents
            // to a housing shortage must not be described to the model as a city walking out on its
            // council.
            var request = Request(districts: 1, districtIdLength: 12);
            System.Func<double, string> line = unhappyShare =>
            {
                int unhappy = (int)System.Math.Round(unhappyShare * 900.0);
                request.Snapshot.Statistics = new CityStatistics(0, 0.0, 100, 900, unhappy, 0, 0, 0.0);
                return CityLine(request, "migration");
            };

            string bare = line(0.0);

            // Three states, and the clause is an addition to the flow phrase rather than a
            // replacement of it - the city is still emptying whatever the reason.
            List<string> states = BandRuns(new[] { 0.0, 0.199, 0.20, 0.499, 0.50, 1.0 }, line);
            Assert.Equal(3, states.Count);
            Assert.Equal(bare, states[0]);
            Assert.StartsWith(bare, states[1]);
            Assert.StartsWith(bare, states[2]);

            AssertThresholds(line, new[] { 0.199, 0.499 }, new[] { 0.20, 0.50 });
        }

        [Fact]
        public void Visitors_BandOnVisitorsPerResident()
        {
            // Against the resident population, not in absolute terms: a thousand tourists is a
            // curiosity in a metropolis and an occupation in a village.
            var request = Request(districts: 1, districtIdLength: 12);
            request.Snapshot.Population = 100_000;

            System.Func<double, string> line = perResident =>
            {
                int tourists = (int)System.Math.Round(perResident * 100_000.0);
                request.Snapshot.Tourism = new TourismLevels(tourists, 40, 0, 0);
                return CityLine(request, "visitors");
            };

            Assert.Equal(5, BandRuns(new[] { 0.0, 0.009, 0.01, 0.049, 0.05, 0.149, 0.15, 0.299, 0.30, 0.90 },
                                     line).Count);
            AssertThresholds(line, new[] { 0.009, 0.049, 0.149, 0.299 }, new[] { 0.01, 0.05, 0.15, 0.30 });
        }

        [Fact]
        public void Visitors_ReportLodgingOnlyAtTheTwoEnds()
        {
            // A hotel trade running half full is not a story, and a clause that printed every month
            // would cost prompt budget without ever telling the model anything.
            var request = Request(districts: 1, districtIdLength: 12);
            request.Snapshot.Population = 100_000;

            System.Func<int, int, string> line = (used, total) =>
            {
                request.Snapshot.Tourism = new TourismLevels(2_000, 40, used, total);
                return CityLine(request, "visitors");
            };

            // No hotels at all: no clause, and in particular no claim that the beds are empty.
            string bare = line(0, 0);
            Assert.Equal(bare, line(300, 1_000));       // about a third full: nothing to say
            Assert.Equal(bare, line(899, 1_000));       // a shade under full: still nothing

            string empty = line(250, 1_000);
            string full = line(900, 1_000);

            Assert.NotEqual(bare, empty);
            Assert.NotEqual(bare, full);
            Assert.NotEqual(empty, full);

            // Both are additions to the visitor phrase, so the pressure reading survives the clause.
            Assert.StartsWith(bare, empty);
            Assert.StartsWith(bare, full);

            // And the two thresholds, each read from the last value inside the clause and the first
            // value outside it.
            Assert.Equal(empty, line(250, 1_000));
            Assert.Equal(bare, line(251, 1_000));
            Assert.Equal(full, line(900, 1_000));
        }

        [Fact]
        public void CityStanding_BandsOnTheMilestoneLevel()
        {
            // MilestoneProgress is deliberately not printed beside it, so nothing here sweeps it: the
            // obvious "close to its next milestone" clause is permanently wrong on a city that has
            // finished the track.
            var request = Request(districts: 1, districtIdLength: 12);
            System.Func<double, string> line = level =>
            {
                request.Snapshot.Progression = new ProgressionState((int)level, 0, 0.0);
                return CityLine(request, "city standing");
            };

            Assert.Equal(5, BandRuns(new double[] { 0, 1, 4, 5, 9, 10, 14, 15, 30 }, line).Count);
            AssertThresholds(line, new double[] { 0, 4, 9, 14 }, new double[] { 1, 5, 10, 15 });
        }

        [Fact]
        public void CityStanding_OnAnUnmeasuredCity_ClaimsNothingAboutItsAge()
        {
            // A milestone level of 0 means "brand new" or "the progression sensor never read
            // anything", and the contract cannot tell the two apart: AgoraProgressionSensorSystem
            // reports nothing for the rest of the session if CreateQueries throws, the milestone
            // singleton is guarded besides, and Invalidate() empties the reading before the first
            // sample. All three leave the reading null, which SnapshotAssembly resolves to
            // ProgressionState(0, 0, 0) - the same zeros a genuinely new city produces.
            //
            // So the bottom band may not describe a young city, under the rule stated at the head of
            // the banding section. This is the loudest place in the block to break it, because the
            // size line is fed by a different sensor family and stays correct: a prompt reading "a
            // large city" and "a brand-new settlement" two lines apart writes the contradiction into
            // a year of articles, from a defect that logs one warning at startup and is silent after.
            var request = Request(districts: 1, districtIdLength: 12);
            request.Snapshot.Population = 400_000;

            string size = CityLine(request, "size");
            string standing = CityLine(request, "city standing");

            // Held as forbidden vocabulary rather than as a sentence, which is the non-brittle
            // direction for this rule: any rewording that keeps it passes, and only one that puts the
            // claim back fails.
            string[] claimsTheCityIsYoung = { "settlement", "village", "brand-new", "brand new", "young", "new " };
            for (int i = 0; i < claimsTheCityIsYoung.Length; i++)
            {
                Assert.DoesNotContain(claimsTheCityIsYoung[i], standing);
            }

            // The size line beside it is untouched, which is what makes the contradiction visible at
            // all - this test would have nothing to say if both lines went quiet together.
            Assert.NotEqual(string.Empty, size);
            Assert.NotEqual(size, standing);
        }

        /// <summary>
        /// The value of one labelled line in the city block: everything after <c>"- label: "</c> up to
        /// the newline. Asserts the line exists, so a band test cannot silently pass by comparing two
        /// empty strings when the line it was written for has been removed.
        /// </summary>
        private static string CityLine(FlavorRequest request, string label)
        {
            string prompt = FlavorPromptBuilder.Build(request);
            string prefix = "- " + label + ": ";

            int start = prompt.IndexOf(prefix, System.StringComparison.Ordinal);
            Assert.True(start >= 0, "the city block must carry a \"" + label + "\" line");
            start += prefix.Length;

            int end = prompt.IndexOf('\n', start);
            Assert.True(end > start, "the \"" + label + "\" line must carry a value");
            return prompt.Substring(start, end - start);
        }

        /// <summary>
        /// Walks an ascending sweep of inputs and returns the distinct phrases in the order they first
        /// appeared, asserting as it goes that no phrase comes back after the sweep has moved off it.
        /// </summary>
        /// <remarks>
        /// This is how a band table is pinned without pinning its wording. Rewriting any adjective
        /// leaves every assertion standing; a duplicated row, a swapped pair or a threshold that
        /// stopped being monotonic does not, because the sweep would hand back a phrase it had already
        /// left. Combined with <see cref="AssertThresholds"/> - which says where the changes happen -
        /// that is the whole of what a band table promises.
        /// </remarks>
        private static List<string> BandRuns(double[] ascending, System.Func<double, string> line)
        {
            var runs = new List<string>();
            for (int i = 0; i < ascending.Length; i++)
            {
                string phrase = line(ascending[i]);
                if (runs.Count > 0 && runs[runs.Count - 1] == phrase) continue;

                Assert.DoesNotContain(phrase, runs);
                runs.Add(phrase);
            }
            return runs;
        }

        /// <summary>
        /// Each pair brackets one threshold: the phrase must change across it, which is what fails
        /// when a boundary is moved, and the two arrays are the last value in one band and the first
        /// value in the next.
        /// </summary>
        private static void AssertThresholds(System.Func<double, string> line, double[] below, double[] atOrAbove)
        {
            Assert.Equal(below.Length, atOrAbove.Length);
            for (int i = 0; i < below.Length; i++)
            {
                Assert.NotEqual(line(below[i]), line(atOrAbove[i]));
            }
        }

        [Fact]
        public void TaxRatesAndUnlockedFeatures_NeverReachTheModel()
        {
            // Both are lists of ids rather than a state of the city, and leaving them out is a
            // decision recorded in the builder's own comment. Pinned so that a later pass adding
            // them has to delete this test on purpose rather than discover the omission and "fix" it:
            // forty resource names would outweigh every other line in the city block put together.
            var request = Request(districts: 3, districtIdLength: 12);
            request.Snapshot.UnlockedFeatureIds.Add("Feature_Zoning_Signature");
            request.Snapshot.IndustryTaxRates.Add(
                new ResourceTaxRate(TaxArea.Office, 21, "Software", 0.11));

            string prompt = FlavorPromptBuilder.Build(request);

            Assert.DoesNotContain("Feature_Zoning_Signature", prompt);
            Assert.DoesNotContain("Software", prompt);
        }

        [Fact]
        public void TheCityBlock_CarriesNoFigureAtAll()
        {
            // The other direction of non-negotiable #1, and the reason every line above is a band: a
            // model never shown a figure cannot quote one back slightly wrong. Swept over the whole
            // city block rather than the four new lines, because the cheapest way for a future field
            // to arrive as a number is for it to be appended next to them.
            var request = Request(districts: 0, districtIdLength: 12);
            request.Snapshot.Statistics = new CityStatistics(900, 0.06, 200, 1_400, 900, 300, 250, 4_000.0);
            request.Snapshot.Tourism = new TourismLevels(36_000, 120, 950, 1_000);
            request.Snapshot.Progression = new ProgressionState(12, 40_000, 0.4);

            string prompt = FlavorPromptBuilder.Build(request);

            // Districts are excluded from the fixture rather than from the slice: their ids and names
            // are engine-authored and are the one thing in this block that is allowed to carry digits.
            int start = prompt.IndexOf("THE CITY (qualitative only", System.StringComparison.Ordinal);
            Assert.True(start >= 0);
            int end = prompt.IndexOf("PARTIES (partyId", System.StringComparison.Ordinal);
            Assert.True(end > start);

            Assert.Equal(new List<string>(), DigitRuns(prompt.Substring(start, end - start)));
        }

        // ---- the numeric sweep ------------------------------------------------------------------

        [Theory]
        [InlineData(FlavorWakeReason.Yearly, RegionTheme.Eu)]
        [InlineData(FlavorWakeReason.Election, RegionTheme.Eu)]
        [InlineData(FlavorWakeReason.Election, RegionTheme.Na)]
        public void WriteSection_CarriesNoFigureItIsNotEntitledTo(FlavorWakeReason reason, RegionTheme theme)
        {
            // The instructions block is the one section written as prose to a model, which makes it
            // the one section where a stray figure - a vote share, a seat count, "at least three
            // paragraphs" - would read to the model as a fact about the city and come back in an
            // article. Pinned as an exact ordered list rather than a bound, because a bound passes
            // for any number that happens to fit.
            var request = Request(districts: 6, districtIdLength: 12);
            request.Reason = reason;
            request.Theme = theme;
            request.ArticleCount = 5;

            string prompt = FlavorPromptBuilder.Build(request);
            string write = StripRuleNumbers(WriteSection(prompt));

            var expected = new List<string>
            {
                "5",                                                    // the article count asked for
                FlavorCacheMigration.HeadlineMaxLength.ToString(),
                FlavorCacheMigration.BodyMaxLength.ToString(),
                "2031", "05", "01",                                     // the generatedAtSimDate echo
                FlavorSchema.SupportedSchemaVersion.ToString()
            };

            Assert.Equal(expected, DigitRuns(write));
        }

        /// <summary>
        /// Everything from the WRITE header to the schema header.
        /// </summary>
        /// <remarks>
        /// The sweep is scoped to this slice because the two sections outside it carry digits that
        /// are not the builder's prose: the embedded schema is JSON, and every <c>maxLength</c> in it
        /// is a number, while the situation block prints engine-authored district ids and names, which
        /// the fixture below numbers. Neither is text a model reads as a fact about the city, and
        /// neither can be pinned as a list. Everything the instructions themselves say is in here —
        /// the <c>generatedAtSimDate</c> echo included, which is why its digits are in the expectation.
        /// </remarks>
        private static string WriteSection(string prompt)
        {
            int start = prompt.IndexOf("WRITE:\n", System.StringComparison.Ordinal);
            Assert.True(start >= 0);
            int end = prompt.IndexOf(SchemaHeader, System.StringComparison.Ordinal);
            Assert.True(end > start);
            return prompt.Substring(start, end - start);
        }

        /// <summary>
        /// Drops the "  1. " enumerators the article rules are numbered with.
        /// </summary>
        /// <remarks>
        /// Those digits come from the rule numbering, not from any figure, so leaving them in the
        /// sweep makes adding or merging a rule fail a test whose subject is "no stray figures reach
        /// the model". The rules are the only numbered list in the section; the election block is
        /// lettered.
        /// </remarks>
        private static string StripRuleNumbers(string write) =>
            System.Text.RegularExpressions.Regex.Replace(write, @"(?m)^  \d+\. ", "  ");

        private static List<string> DigitRuns(string text)
        {
            var runs = new List<string>();
            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsDigit(text[i])) continue;
                int j = i;
                while (j < text.Length && char.IsDigit(text[j])) j++;
                runs.Add(text.Substring(i, j - i));
                i = j;
            }
            return runs;
        }

        private static FlavorRequest Request(int districts, int districtIdLength)
        {
            var request = new FlavorRequest
            {
                Date = new SimDate(2031, 5, 1),
                Reason = FlavorWakeReason.Yearly,
                Theme = RegionTheme.Eu,
                ArticleCount = 4,
                Snapshot = Snapshot(districts, districtIdLength)
            };

            request.Parties.Add(new PartyBrief
            {
                PartyId = "party-01",
                ArchetypeId = "greens",
                CoreGrievance = Issue.Environment,
                StatusWord = "governing",
                CurrentName = "Riverside Slate"
            });

            return request;
        }

        /// <summary>One event, id and brief free of digits so it cannot disturb the numeric sweep.</summary>
        private static EventBrief Event() => new EventBrief
        {
            EventId = "event-budget-hearing",
            Title = "budget hearing",
            HeadlineBrief = "the council meets to set the rate"
        };

        private static CitySnapshot Snapshot(int districts, int districtIdLength)
        {
            var snapshot = new CitySnapshot
            {
                Date = new SimDate(2031, 5, 1),
                Population = 180_000,
                Happiness = 54.0,
                Unemployment = 0.08,
                RentBurden = 0.4,
                CrimeRate = 0.2,
                AverageCommuteMinutes = 22.0,
                BudgetBalance = -1200,
                Debt = 40_000,
                Districts = new List<DistrictSnapshot>()
            };

            for (int i = 0; i < districts; i++)
            {
                // Ordinal-padded so the sort order is the declaration order, and the id carries the
                // length: the builder writes district ids through unchanged, which is what lets this
                // fixture reach a cap no real city reaches.
                string id = "district-" + i.ToString("00");
                snapshot.Districts.Add(new DistrictSnapshot
                {
                    Id = id + new string('x', districtIdLength > id.Length ? districtIdLength - id.Length : 0),
                    Name = "District " + i.ToString("00"),
                    Happiness = 20.0 + i,
                    Population = 9_000
                });
            }

            return snapshot;
        }
    }
}
