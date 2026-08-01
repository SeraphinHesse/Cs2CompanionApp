using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// The single place JSON enters the flavor pipeline, configured so the parser cannot help the
    /// model cheat.
    ///
    /// <para>
    /// Two settings matter and neither is Newtonsoft's default:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>DateParseHandling.None</c>. By default Newtonsoft turns any ISO-8601-looking string into a
    /// <c>JTokenType.Date</c>. <c>generatedAtSimDate</c> is exactly that shape, so with the default
    /// it would arrive as a Date and fail a <c>type: "string"</c> check - and worse, a district name
    /// that happened to look like a date would silently become a <c>DateTime</c>, dragging a clock
    /// into a pipeline that non-negotiable #8 says has none.
    /// </description></item>
    /// <item><description>
    /// <c>FloatParseHandling.Decimal</c> is deliberately <i>not</i> used; numbers stay Integer/Float
    /// so <see cref="NumericFieldScanner"/> can find them. The point is never to be tolerant of a
    /// number, only to be able to see one.
    /// </description></item>
    /// </list>
    ///
    /// <para>
    /// <c>MaxDepth</c> is capped because the input is untrusted text from a subprocess and a deeply
    /// nested document would otherwise recurse the parser off the stack - which is a crash, and
    /// non-negotiable #7 says the LLM path never crashes.
    /// </para>
    /// </summary>
    public static class FlavorJsonReader
    {
        /// <summary>Nesting cap. The flavor schema is three levels deep; 32 is generous.</summary>
        public const int MaxDepth = 32;

        /// <summary>Refuse to even parse beyond this. A well-formed payload is a few tens of KB.</summary>
        public const int MaxCharacters = 2 * 1024 * 1024;

        /// <summary>
        /// Parses <paramref name="json"/> into a token tree. Returns null on any failure - malformed
        /// input is an expected outcome here, not an exception.
        /// </summary>
        public static JToken Parse(string json, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(json))
            {
                error = "empty response";
                return null;
            }

            if (json.Length > MaxCharacters)
            {
                error = "response is " + json.Length + " characters, over the " + MaxCharacters + " limit";
                return null;
            }

            try
            {
                using (var stringReader = new StringReader(json))
                using (var reader = new JsonTextReader(stringReader))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.MaxDepth = MaxDepth;

                    JToken token = JToken.ReadFrom(reader);

                    // Trailing content after the first value means we were handed two documents
                    // glued together, which is a sign the extraction step picked the wrong braces.
                    if (reader.Read() && reader.TokenType != JsonToken.None)
                    {
                        error = "trailing content after the JSON document";
                        return null;
                    }

                    return token;
                }
            }
            catch (JsonReaderException ex)
            {
                error = "malformed JSON at line " + ex.LineNumber + " position " + ex.LinePosition + ": " + ex.Message;
                return null;
            }
            catch (Exception ex)
            {
                error = "JSON could not be read: " + ex.Message;
                return null;
            }
        }

        /// <summary>Parses and requires an object at the root. Null when it is not one.</summary>
        public static JObject ParseObject(string json)
        {
            string ignored;
            return Parse(json, out ignored) as JObject;
        }

        /// <summary>Parses and requires an object at the root, reporting why when it fails.</summary>
        public static JObject ParseObject(string json, out string error)
        {
            JToken token = Parse(json, out error);
            if (token == null) return null;

            var obj = token as JObject;
            if (obj == null)
            {
                error = "expected a JSON object at the root, found " + token.Type.ToString().ToLowerInvariant();
                return null;
            }
            return obj;
        }
    }
}
