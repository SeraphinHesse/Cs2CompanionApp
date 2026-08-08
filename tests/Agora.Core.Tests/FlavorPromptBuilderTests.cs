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
            // "Always include" is the load-bearing word: a prompt that made refs optional would still
            // satisfy every other assertion here, and the TODO(W5-3) seam pushes in exactly that
            // direction. The second assertion pins the other half of the same rule - the id has to be
            // in the prose as well as in refs, which is what makes the reference checkable at all.
            Assert.Contains("Always include refs", prompt);
            Assert.Contains("name at least one party or district in the prose by the id given in the " +
                            "lists above, and put that same id in refs", prompt);
            Assert.Contains("at least one of eventId, districtId or partyId", prompt);
            Assert.Contains("Write nothing you cannot point at", prompt);
            Assert.Contains("Never attribute to a subject you have not named", prompt);
            Assert.Contains("\"residents say\"", prompt);
            Assert.Contains("\"officials say\"", prompt);

            // The permissive phrasing this replaced. Left as an assertion because deleting the new
            // wording and restoring the old one would otherwise pass every check above but one.
            Assert.DoesNotContain("Set refs only to IDs from the lists above", prompt);
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
            Assert.DoesNotContain("a result piece", prompt);
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

        [Fact]
        public void ElectionUnderEu_AsksForResultBothReactionsAndTheCoalitionOutlook()
        {
            var request = Request(districts: 6, districtIdLength: 12);
            request.Reason = FlavorWakeReason.Election;
            request.Theme = RegionTheme.Eu;

            string prompt = FlavorPromptBuilder.Build(request);

            Assert.Contains("a) a result piece", prompt);
            Assert.Contains("b) a piece carrying the winning side's reaction", prompt);
            Assert.Contains("c) a piece carrying the losing side's reaction", prompt);
            Assert.Contains("d) a piece on the coalition outlook", prompt);

            // Non-negotiable #1 from the other end: the model is told it has no figures rather than
            // left to notice. A prompt that asks for a result and supplies none invites an invented one.
            Assert.Contains("Name the parties involved by id only", prompt);
            Assert.Contains("You have not been given the vote shares, the seat counts or the turnout", prompt);
        }

        [Fact]
        public void ElectionUnderNa_AsksForTheFirstThreeAndNoCoalitionOutlook()
        {
            // There are no coalitions to have an outlook on under first-past-the-post wards with a
            // directly elected mayor, so asking for the piece would be asking for invented politics.
            var request = Request(districts: 6, districtIdLength: 12);
            request.Reason = FlavorWakeReason.Election;
            request.Theme = RegionTheme.Na;

            string prompt = FlavorPromptBuilder.Build(request);

            Assert.Contains("a) a result piece", prompt);
            Assert.Contains("b) a piece carrying the winning side's reaction", prompt);
            Assert.Contains("c) a piece carrying the losing side's reaction.", prompt);
            Assert.DoesNotContain("coalition outlook", prompt);
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
