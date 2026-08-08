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
