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
            new[] { "event-harbour-flood" });

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
