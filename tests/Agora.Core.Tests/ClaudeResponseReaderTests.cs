using Agora.Mod.Llm;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The balanced-brace scanner that locates the flavor document inside whatever the CLI printed.
    ///
    /// <para>
    /// Every case here is a character the model is allowed to put inside a slogan or a headline and
    /// that also means something to the scanner. Getting one wrong truncates the document, and the
    /// resulting log line reads as "malformed JSON" — a failure that points at the model rather than
    /// at us, which is why these are worth pinning down rather than reasoning about.
    /// </para>
    /// </summary>
    public class ClaudeResponseReaderTests
    {
        [Fact]
        public void BalancedObject_SurvivesABackslashBeforeAQuote()
        {
            // A slogan of literally: Rock \ Roll — an escaped backslash immediately before the
            // closing quote, so a scanner that treats the second backslash as escaping the quote
            // never leaves the string and never closes the object.
            string json = "{\"slogan\":\"Rock \\\\\",\"name\":\"Riverside Slate\"}";

            string span = ClaudeResponseReader.FirstBalancedObject(json);

            Assert.Equal(json, span);

            JObject parsed = FlavorJsonReader.ParseObject(span);
            Assert.NotNull(parsed);
            Assert.Equal("Rock \\", parsed["slogan"]!.Value<string>());
            Assert.Equal("Riverside Slate", parsed["name"]!.Value<string>());
        }

        [Fact]
        public void BalancedObject_SurvivesAnEscapedQuoteAndAnEscapedBackslashTogether()
        {
            // Literally: he said \"go\" — a backslash, then an escaped quote, then a trailing pair.
            string json = "{\"slogan\":\"he said \\\\\\\"go\\\\\\\"\",\"id\":\"party-01\"}";

            string span = ClaudeResponseReader.FirstBalancedObject(json);

            Assert.Equal(json, span);
            Assert.Equal("party-01", FlavorJsonReader.ParseObject(span)!["id"]!.Value<string>());
        }

        [Fact]
        public void BalancedObject_IgnoresBracesInsideStrings()
        {
            string json = "{\"slogan\":\"a { and a } and a \\\\}\",\"id\":\"party-02\"}";

            Assert.Equal(json, ClaudeResponseReader.FirstBalancedObject(json));
        }

        [Fact]
        public void BalancedObject_ReturnsNullWhenTruncated()
        {
            Assert.Null(ClaudeResponseReader.FirstBalancedObject("{\"slogan\":\"Rock \\\\\","));
        }

        [Fact]
        public void ExtractFlavorJson_RecoversABackslashBearingDocumentFromTheCliEnvelope()
        {
            // The whole pipeline: envelope, fence, then the scanner. The inner document carries the
            // backslash, so it is double-escaped once for the envelope's own string.
            var envelope = new JObject
            {
                ["type"] = "result",
                ["subtype"] = "success",
                ["is_error"] = false,
                ["result"] = "```json\n{\"slogan\":\"Rock \\\\\",\"id\":\"party-03\"}\n```"
            };

            string error;
            string extracted = ClaudeResponseReader.ExtractFlavorJson(
                envelope.ToString(Newtonsoft.Json.Formatting.None), out error);

            Assert.Null(error);
            Assert.NotNull(extracted);

            JObject document = FlavorJsonReader.ParseObject(extracted);
            Assert.NotNull(document);
            Assert.Equal("Rock \\", document["slogan"]!.Value<string>());
            Assert.Equal("party-03", document["id"]!.Value<string>());
        }

        // ---- trailing content after the envelope ---------------------------------------------
        //
        // A CLI is entitled to print a warning line, or a second status object, after the result
        // envelope. The strict parse rejects that as two glued documents, so the unwrap seam has to
        // fall back to the first balanced span — otherwise the envelope is declared "not an
        // envelope", extracted whole, and the validator blames the model for our parse.

        /// <summary>An envelope with a document in it, plus whatever the CLI printed afterwards.</summary>
        private static string EnvelopeWithTrailer(string trailer) =>
            new JObject
            {
                ["type"] = "result",
                ["subtype"] = "success",
                ["is_error"] = false,
                ["result"] = "{\"schemaVersion\":2,\"slogan\":\"Riverside Rising\",\"id\":\"party-04\"}"
            }.ToString(Newtonsoft.Json.Formatting.None) + trailer;

        [Theory]
        [InlineData("\nwarning: model context window is nearly full")]
        [InlineData("\n{\"type\":\"progress\",\"done\":true}")]
        [InlineData("\n\n  trailing whitespace and then noise  \n")]
        public void ExtractFlavorJson_UnwrapsAnEnvelopeFollowedByTrailingContent(string trailer)
        {
            string error;
            string extracted = ClaudeResponseReader.ExtractFlavorJson(EnvelopeWithTrailer(trailer), out error);

            Assert.Null(error);
            Assert.NotNull(extracted);

            JObject document = FlavorJsonReader.ParseObject(extracted!)!;
            Assert.Equal("Riverside Rising", document["slogan"]!.Value<string>());
            Assert.Equal("party-04", document["id"]!.Value<string>());

            // The envelope must not be what came out. Non-null proves nothing on its own.
            Assert.Null(document["is_error"]);
            Assert.Null(document["subtype"]);
        }

        [Fact]
        public void ExtractFlavorJson_ReportsTheCliErrorWhenAnErrorEnvelopeHasTrailingContent()
        {
            string output = new JObject
            {
                ["type"] = "result",
                ["subtype"] = "error_during_execution",
                ["is_error"] = true,
                ["result"] = "the request was refused"
            }.ToString(Newtonsoft.Json.Formatting.None) + "\nwarning: exiting with status 1";

            string error;
            string extracted = ClaudeResponseReader.ExtractFlavorJson(output, out error);

            Assert.Null(extracted);
            Assert.NotNull(error);
            Assert.Contains("the CLI reported an error", error!);
            Assert.Contains("error_during_execution", error!);
            Assert.Contains("the request was refused", error!);
        }

        [Fact]
        public void ExtractFlavorJson_NamesTheUnwrapSeamWhenTheEnvelopeCarriesNoResult()
        {
            // Envelope bookkeeping and nothing to unwrap. Passing this on would fail validation with
            // a page of unknown fields and point at the model; the seam has to own it instead.
            string output = new JObject
            {
                ["session_id"] = "abc-123",
                ["duration_ms"] = 4210
            }.ToString(Newtonsoft.Json.Formatting.None) + "\nnote: no result was produced";

            string error;
            string extracted = ClaudeResponseReader.ExtractFlavorJson(output, out error);

            Assert.Null(extracted);
            Assert.NotNull(error);
            Assert.Contains("the CLI envelope could not be unwrapped", error!);
        }

        [Theory]
        [InlineData("I was unable to produce JSON for that request.", "no JSON object found")]
        [InlineData("{\"schemaVersion\":2,\"parties\":[", "unterminated")]
        public void ExtractFlavorJson_StillFailsClosedOnAnUnusableResponse(string output, string expected)
        {
            string error;
            string extracted = ClaudeResponseReader.ExtractFlavorJson(output, out error);

            Assert.Null(extracted);
            Assert.NotNull(error);
            Assert.Contains(expected, error!);
        }

        [Fact]
        public void ExtractFlavorJson_TakesTheDocumentFromABareResponseWithTrailingGarbage()
        {
            // No envelope at all: the first balanced span is the document, and trailing prose after it
            // is the model's framing, which layer 2 has always been allowed to drop.
            string output = "{\"schemaVersion\":2,\"id\":\"party-05\"}\nLet me know if you want another.";

            string error;
            string extracted = ClaudeResponseReader.ExtractFlavorJson(output, out error);

            Assert.Null(error);
            Assert.Equal("{\"schemaVersion\":2,\"id\":\"party-05\"}", extracted);
        }

        [Fact]
        public void UnwrapEnvelope_LeavesABareFlavorDocumentAlone()
        {
            // The fallback must not turn "first balanced object" into "assume an envelope". A document
            // with no envelope-only field, trailing content or not, is not an envelope.
            string error;

            Assert.Null(ClaudeResponseReader.UnwrapEnvelope(
                "{\"schemaVersion\":2,\"id\":\"party-06\"}\nthanks!", out error));
            Assert.Null(error);
        }
    }
}
