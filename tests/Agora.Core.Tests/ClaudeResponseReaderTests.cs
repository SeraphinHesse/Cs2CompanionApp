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
    }
}
