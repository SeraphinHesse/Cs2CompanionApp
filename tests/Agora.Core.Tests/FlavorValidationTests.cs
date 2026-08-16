// Requires the FlavorDocument.cs / JsonSchemaSubsetValidator.cs / NumericFieldScanner.cs /
// FlavorValidator.cs <Compile Link> lines in Agora.Core.Tests.csproj (see the comment there for why).

using System.Collections.Generic;
using System.Globalization;
using Agora.Core.Contracts;
using Agora.Mod.Llm;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Non-negotiable #1, executable: no number entering engine state may originate from LLM output.
    ///
    /// <para>
    /// Two artefacts enforce it and they can drift apart. <c>politics_flavor.schema.json</c> makes a
    /// number structurally unrepresentable (<c>type: "string"</c> everywhere, plus
    /// <c>additionalProperties: false</c> so an extra key has nowhere to land), and
    /// <see cref="NumericFieldScanner"/> walks the parsed document and reports every numeric leaf
    /// regardless of what the schema said. The tests below therefore exercise the sweep against a
    /// <i>permissive</i> schema as well as the shipped one: a test that only ever runs both gates
    /// together cannot tell which of them is doing the work, and would keep passing after the sweep
    /// was deleted.
    /// </para>
    ///
    /// <para>
    /// <c>schemaVersion</c> is the single exception, and it is covered here for the same reason: it is
    /// the one number parsed out of model output, and what makes that legal is that it is only ever
    /// compared and used to reject. Nothing downstream computes with it.
    /// </para>
    /// </summary>
    public class FlavorValidationTests
    {
        /// <summary>The sim date the request was made for.</summary>
        private static readonly SimDate RequestDate = new SimDate(1997, 6, 1);

        /// <summary>Everything the sample documents below reference, so nothing is discarded on IDs.</summary>
        private static FlavorCatalog Catalog() => new FlavorCatalog(
            new[] { "party-riverside" },
            new[] { "faction-riverside-left" },
            new[] { "district-harbour" },
            new[] { "event-harbour-flood" },
            new[] { "story-harbour-1997-06" });

        /// <summary>The schema the deployed mod validates against.</summary>
        private static JObject ShippedSchema() => FlavorSchema.Load(null, null);

        /// <summary>
        /// A schema that constrains nothing beyond "it is an object". Stands in for a schema that has
        /// drifted away from the C#, which is the case the numeric sweep exists to survive.
        /// </summary>
        private static JObject PermissiveSchema() => JObject.Parse("{ \"type\": \"object\" }");

        /// <summary>A document the shipped schema accepts: prose, IDs and one date.</summary>
        private static string CleanJson() => JsonAtVersion(FlavorSchema.SupportedSchemaVersion);

        /// <summary>
        /// The same document stamped with an arbitrary version. Written against the constant rather
        /// than a literal so that a version bump moves the fixture with the gate: hardcoding the
        /// number would turn these into tests of what the fixture says instead of what the validator
        /// does, and they would start failing for the wrong reason on every <c>/schema-change</c>.
        /// </summary>
        private static string JsonAtVersion(int schemaVersion) => @"{
  ""schemaVersion"": " + schemaVersion.ToString(CultureInfo.InvariantCulture) + @",
  ""generatedAtSimDate"": ""1997-06-01"",
  ""partyFlavor"": [
    { ""partyId"": ""party-riverside"", ""name"": ""Riverside Slate"", ""shortName"": ""RS"",
      ""description"": ""Harbour wards, tram money, long memories."", ""slogan"": ""Keep the water working."" }
  ],
  ""factionFlavor"": [
    { ""factionId"": ""faction-riverside-left"", ""partyId"": ""party-riverside"",
      ""name"": ""The Wharf Group"", ""leaderName"": ""Ada Nkemelu"" }
  ],
  ""articles"": [
    { ""id"": ""article-01"", ""outlet"": ""Harbour Register"", ""headline"": ""Tram money finds the wharf"",
      ""body"": ""The slate spent the morning explaining a bridge."", ""tone"": ""neutral"",
      ""refs"": { ""eventId"": ""event-harbour-flood"", ""districtId"": ""district-harbour"",
                  ""partyId"": ""party-riverside"" } }
  ],
  ""eventProse"": [
    { ""eventId"": ""event-harbour-flood"", ""localAngle"": ""The tide came up the slipway again."" }
  ],
  ""stories"": [
    { ""storyId"": ""story-harbour-1997-06"", ""headline"": ""The wharf waits on a decision"",
      ""article"": ""Three months of tide reports, and still nobody has signed anything."" }
  ],
  ""resolutions"": [
    { ""storyId"": ""story-harbour-1997-06"", ""headline"": ""The wharf gets its answer"",
      ""article"": ""The council signed, eventually, and the slipway is to be raised."" }
  ]
}";

        /// <summary>
        /// A document whose only fault is a number attached to a party, so a rejection can only be
        /// the sweep's doing. Also stamped from the constant: a stale version here would reject the
        /// document for a second reason and hide the one being tested.
        /// </summary>
        private static string SmuggledNumberJson() =>
            @"{ ""schemaVersion"": " + FlavorSchema.SupportedSchemaVersion.ToString(CultureInfo.InvariantCulture) +
            @", ""generatedAtSimDate"": ""1997-06-01"",
                ""partyFlavor"": [ { ""partyId"": ""party-riverside"", ""name"": ""Riverside Slate"",
                                     ""expectedSwing"": 4.2 } ] }";

        // --- the sweep, on its own --------------------------------------------------------------

        [Fact]
        public void CleanDocumentCarriesNoNumberBeyondSchemaVersion()
        {
            JObject root = FlavorJsonReader.ParseObject(CleanJson());
            Assert.NotNull(root);

            Assert.Empty(NumericFieldScanner.FindNumbers(root!));
        }

        [Fact]
        public void SweepReportsASmuggledNumberWithItsPath()
        {
            // The shape that would matter: a poll number attached to a party, phrased as though it
            // belonged there. Nothing reads it today — the point is that it never gets the chance.
            JObject root = FlavorJsonReader.ParseObject(
                @"{ ""schemaVersion"": 1, ""generatedAtSimDate"": ""1997-06-01"",
                    ""partyFlavor"": [ { ""partyId"": ""party-riverside"", ""name"": ""Riverside Slate"",
                                         ""expectedSwing"": 4.2 } ] }");
            Assert.NotNull(root);

            IReadOnlyList<string> found = NumericFieldScanner.FindNumbers(root!);

            string only = Assert.Single(found);
            Assert.Contains("$.partyFlavor[0].expectedSwing", only);
            Assert.Contains("non-negotiable #1", only);
        }

        [Fact]
        public void SweepTreatsABooleanAsANumber()
        {
            // One bit is still a number, and "shouldGovern": true is exactly the kind of field a model
            // volunteers when it decides it is being helpful.
            JObject root = FlavorJsonReader.ParseObject(
                @"{ ""schemaVersion"": 1, ""generatedAtSimDate"": ""1997-06-01"", ""shouldGovern"": true }");
            Assert.NotNull(root);

            string only = Assert.Single(NumericFieldScanner.FindNumbers(root!));
            Assert.Contains("$.shouldGovern", only);
            Assert.Contains("boolean", only);
        }

        [Fact]
        public void SweepAllowsSchemaVersionOnlyAtTheTopLevel()
        {
            // The allow-list is a full path, not a property name, so a nested field borrowing the name
            // does not inherit the exemption.
            JObject root = FlavorJsonReader.ParseObject(
                @"{ ""schemaVersion"": 1, ""generatedAtSimDate"": ""1997-06-01"",
                    ""articles"": [ { ""id"": ""article-01"", ""schemaVersion"": 1 } ] }");
            Assert.NotNull(root);

            string only = Assert.Single(NumericFieldScanner.FindNumbers(root!));
            Assert.Contains("$.articles[0].schemaVersion", only);
        }

        // --- the sweep, through the validator ----------------------------------------------------

        [Fact]
        public void ValidatorRejectsASmuggledNumberEvenWhenTheSchemaWouldNotCatchIt()
        {
            // The drift case. With a schema that has stopped constraining anything, the sweep is the
            // only thing standing between the model and engine-visible state — so it is asserted with
            // the schema deliberately out of the way.
            var validator = new FlavorValidator(PermissiveSchema(), null);

            FlavorValidationResult result = validator.Validate(
                SmuggledNumberJson(), Catalog(), RequestDate);

            Assert.False(result.IsValid);
            Assert.Null(result.Document);
            Assert.Contains(result.Errors, e => e.Contains("$.partyFlavor[0].expectedSwing"));
        }

        [Fact]
        public void ValidatorRejectsASmuggledNumberUnderTheShippedSchemaToo()
        {
            var validator = new FlavorValidator(ShippedSchema(), null);

            FlavorValidationResult result = validator.Validate(
                SmuggledNumberJson(), Catalog(), RequestDate);

            Assert.False(result.IsValid);

            // Both gates fire, and the assertion names both: the structural one (the property has
            // nowhere legal to land) and the sweep. If either message disappears, one gate has gone.
            Assert.Contains(result.Errors, e => e.Contains("'expectedSwing' is not allowed here"));
            Assert.Contains(result.Errors, e => e.Contains("non-negotiable #1"));
        }

        [Fact]
        public void ValidatorAcceptsACleanDocument()
        {
            var validator = new FlavorValidator(ShippedSchema(), null);

            FlavorValidationResult result = validator.Validate(CleanJson(), Catalog(), RequestDate);

            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            Assert.Empty(result.Discarded);

            FlavorDocument document = result.Document!;
            Assert.Equal("Riverside Slate", Assert.Single(document.PartyFlavor).Name);
            Assert.Equal("The Wharf Group", Assert.Single(document.FactionFlavor).Name);
            Assert.Equal("article-01", Assert.Single(document.Articles).Id);
            Assert.Equal("event-harbour-flood", Assert.Single(document.EventProse).EventId);

            // The same story id in both collections is the ordinary case, not a duplicate: one entry
            // is the opening and the other the closing, and both survive.
            Assert.Equal("The wharf waits on a decision", Assert.Single(document.Stories).Headline);
            Assert.Equal("The wharf gets its answer", Assert.Single(document.Resolutions).Headline);
        }

        [Fact]
        public void ANumberInAProseFieldIsNeverLaunderedIntoText()
        {
            // FlavorDocument maps by JToken type rather than deserialising, so "name": 42 yields an
            // empty string rather than "42". This is the last line of the defence: even on a build
            // where both gates above had been removed, no digit reaches a prose field.
            JObject root = FlavorJsonReader.ParseObject(
                @"{ ""schemaVersion"": 1, ""generatedAtSimDate"": ""1997-06-01"",
                    ""partyFlavor"": [ { ""partyId"": ""party-riverside"", ""name"": 42 } ] }");
            Assert.NotNull(root);

            FlavorDocument document = FlavorDocument.FromValidatedObject(root!);

            Assert.Equal(string.Empty, Assert.Single(document.PartyFlavor).Name);
        }

        // --- refs: the ids an article is about, all the way to the boundary contract --------------

        [Fact]
        public void ArticleRefsSurviveTheProjectionOntoTheBoundaryContract()
        {
            // The refs were parsed onto ArticleEntry and then dropped by ToPayload, so the dashboard
            // could never tell which party or district a story was about. Asserted on the payload
            // rather than on FlavorDocument because the document half already worked: the boundary
            // contract is where the ids were being lost.
            var validator = new FlavorValidator(ShippedSchema(), null);

            FlavorValidationResult result = validator.Validate(CleanJson(), Catalog(), RequestDate);
            Assert.True(result.IsValid, string.Join("; ", result.Errors));

            FlavorPayload payload = result.Document!.ToPayload(RequestDate);

            Article article = Assert.Single(payload.Articles);
            Assert.Equal("party-riverside", article.PartyId);
            Assert.Equal("district-harbour", article.DistrictId);
            Assert.Equal("event-harbour-flood", article.EventId);
        }

        [Fact]
        public void APartlyReferencedArticleCarriesEmptyIdsRatherThanNulls()
        {
            // refs is optional in the schema and each of its three fields is optional within it, so
            // an article that names a district and nothing else is the ordinary case. The consumers
            // concatenate and compare these, so the two absent ids have to be "" — a null here would
            // surface as a NullReferenceException in the projection, months from this change.
            var validator = new FlavorValidator(ShippedSchema(), null);

            FlavorValidationResult result = validator.Validate(
                @"{ ""schemaVersion"": " + FlavorSchema.SupportedSchemaVersion.ToString(CultureInfo.InvariantCulture) +
                @", ""generatedAtSimDate"": ""1997-06-01"",
                    ""articles"": [ { ""id"": ""article-02"", ""outlet"": ""Harbour Register"",
                                      ""headline"": ""Council adjourns early"",
                                      ""body"": ""Nobody could agree on the agenda."", ""tone"": ""neutral"",
                                      ""refs"": { ""districtId"": ""district-harbour"" } } ] }",
                Catalog(), RequestDate);

            Assert.True(result.IsValid, string.Join("; ", result.Errors));

            Article article = Assert.Single(result.Document!.ToPayload(RequestDate).Articles);
            Assert.Equal("district-harbour", article.DistrictId);
            Assert.Equal(string.Empty, article.PartyId);
            Assert.Equal(string.Empty, article.EventId);
        }

        [Theory]
        [InlineData(@"""tone"": ""neutral""")]
        [InlineData(@"""tone"": ""neutral"", ""refs"": { }")]
        [InlineData(@"""tone"": ""neutral"", ""refs"": { ""districtId"": """" }")]
        public void AnArticleThatPointsAtNothingIsDropped(string tail)
        {
            // The prompt tells the model that an article without refs is dropped. This is the check
            // that makes the sentence true, and all three shapes of "no refs" have to reach it: the
            // key absent, the object empty, and the one id present but blank. Dropped rather than
            // fatal, like every other catalog miss beside it — the rest of the round survives.
            var validator = new FlavorValidator(ShippedSchema(), null);

            FlavorValidationResult result = validator.Validate(
                @"{ ""schemaVersion"": " + FlavorSchema.SupportedSchemaVersion.ToString(CultureInfo.InvariantCulture) +
                @", ""generatedAtSimDate"": ""1997-06-01"",
                    ""articles"": [ { ""id"": ""article-02"", ""outlet"": ""Harbour Register"",
                                      ""headline"": ""Council adjourns early"",
                                      ""body"": ""Nobody could agree on the agenda."", " + tail + @" } ] }",
                Catalog(), RequestDate);

            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            Assert.Empty(result.Document!.Articles);
            Assert.Contains(result.Discarded, d => d.Contains("article-02") && d.Contains("no refs"));
        }

        // --- storyId: the ids the two ends of a story are hung on ---------------------------------

        /// <summary>
        /// A document carrying nothing but the given <c>stories</c> and <c>resolutions</c> bodies, so
        /// that what survives the filter is the only thing the assertions can be reading.
        /// </summary>
        private static string StoryJson(string stories, string resolutions) =>
            @"{ ""schemaVersion"": " + FlavorSchema.SupportedSchemaVersion.ToString(CultureInfo.InvariantCulture) +
            @", ""generatedAtSimDate"": ""1997-06-01"",
                ""stories"": [ " + stories + @" ],
                ""resolutions"": [ " + resolutions + @" ] }";

        private static string Story(string storyId) =>
            @"{ ""storyId"": """ + storyId + @""", ""headline"": ""The wharf waits on a decision"",
                ""article"": ""Three months of tide reports, and still nobody has signed anything."" }";

        [Fact]
        public void AStoryEntryNamingAnUnknownStoryIsDropped()
        {
            // A story id the engine does not hold cannot be shown against anything — the prose would
            // land beside whatever story now occupies that slot. Dropped per entry like every other
            // catalog miss, and the discard line names the id so a log reader can see which.
            var validator = new FlavorValidator(ShippedSchema(), null);

            FlavorValidationResult result = validator.Validate(
                StoryJson(Story("story-harbour-1997-06") + ", " + Story("story-invented"),
                          Story("story-invented")),
                Catalog(), RequestDate);

            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            Assert.Equal("story-harbour-1997-06", Assert.Single(result.Document!.Stories).StoryId);
            Assert.Empty(result.Document.Resolutions);

            Assert.Contains(result.Discarded, d => d.Contains("stories") && d.Contains("story-invented"));
            Assert.Contains(result.Discarded, d => d.Contains("resolutions") && d.Contains("story-invented"));
        }

        [Fact]
        public void AStoryEntryWithAnEmptyStoryIdIsDropped()
        {
            var validator = new FlavorValidator(ShippedSchema(), null);

            FlavorValidationResult result = validator.Validate(
                StoryJson(Story(string.Empty), Story(string.Empty)), Catalog(), RequestDate);

            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            Assert.Empty(result.Document!.Stories);
            Assert.Empty(result.Document.Resolutions);
            Assert.Contains(result.Discarded, d => d.Contains("stories") && d.Contains("empty storyId"));
        }

        [Fact]
        public void ASecondEntryForOneStoryInOneCollectionIsDropped()
        {
            // Two openings for one story is ambiguous the same way two names for one party are, and
            // which one won would depend on the model's output order. The first is kept.
            var validator = new FlavorValidator(ShippedSchema(), null);

            FlavorValidationResult result = validator.Validate(
                StoryJson(Story("story-harbour-1997-06") + ", " +
                          @"{ ""storyId"": ""story-harbour-1997-06"", ""headline"": ""A second opinion"",
                              ""article"": ""The same story, told again."" }",
                          Story("story-harbour-1997-06")),
                Catalog(), RequestDate);

            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            Assert.Equal("The wharf waits on a decision",
                         Assert.Single(result.Document!.Stories).Headline);
            Assert.Contains(result.Discarded, d => d.Contains("duplicate stories"));

            // And the resolution carrying that same id is untouched: the two collections are filtered
            // independently, because one story legitimately appears in both.
            Assert.Equal("story-harbour-1997-06", Assert.Single(result.Document.Resolutions).StoryId);
        }

        [Fact]
        public void AnUnknownStoryCostsItsOwnEntryAndNothingElse()
        {
            // The rule the whole filter runs on, stated where it can fail: a bad id costs its entry,
            // never the document. Story ids turn over every few cycles, so a stale response naming one
            // is routine — and losing a year of party names over it would be far worse than losing
            // that story's prose.
            var validator = new FlavorValidator(ShippedSchema(), null);

            FlavorValidationResult result = validator.Validate(
                @"{ ""schemaVersion"": " + FlavorSchema.SupportedSchemaVersion.ToString(CultureInfo.InvariantCulture) +
                @", ""generatedAtSimDate"": ""1997-06-01"",
                    ""partyFlavor"": [ { ""partyId"": ""party-riverside"", ""name"": ""Riverside Slate"" } ],
                    ""stories"": [ " + Story("story-invented") + @" ] }",
                Catalog(), RequestDate);

            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            Assert.Empty(result.Document!.Stories);
            Assert.Equal("Riverside Slate", Assert.Single(result.Document.PartyFlavor).Name);
        }

        // --- schemaVersion: the one number, and what it is allowed to do -------------------------

        [Fact]
        public void SchemaVersionIsParsedAndUsedOnlyToAcceptTheDocument()
        {
            var validator = new FlavorValidator(ShippedSchema(), null);

            FlavorValidationResult result = validator.Validate(CleanJson(), Catalog(), RequestDate);

            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            Assert.Equal(FlavorSchema.SupportedSchemaVersion, result.Document!.SchemaVersion);
        }

        [Fact]
        public void AnUnsupportedSchemaVersionRejectsTheWholeDocument()
        {
            // Under the shipped schema this is caught structurally, by the schemaVersion const.
            var validator = new FlavorValidator(ShippedSchema(), null);

            FlavorValidationResult result = validator.Validate(
                JsonAtVersion(FlavorSchema.SupportedSchemaVersion + 1), Catalog(), RequestDate);

            Assert.False(result.IsValid);
            Assert.Null(result.Document);
        }

        [Fact]
        public void TheSchemaVersionGateStandsWithoutHelpFromTheSchema()
        {
            // And under a schema that has stopped checking it, FlavorValidator's own comparison still
            // rejects — which is the whole justification for reading this number at all. It is a
            // rejection gate, not an input: an unsupported version yields no document rather than a
            // document interpreted some other way.
            var validator = new FlavorValidator(PermissiveSchema(), null);

            FlavorValidationResult result = validator.Validate(
                JsonAtVersion(FlavorSchema.SupportedSchemaVersion + 1), Catalog(), RequestDate);

            Assert.False(result.IsValid);
            Assert.Null(result.Document);
            Assert.Contains(result.Errors,
                            e => e.Contains("this build speaks " + FlavorSchema.SupportedSchemaVersion));
        }
    }
}
