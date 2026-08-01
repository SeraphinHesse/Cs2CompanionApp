using System;
using Newtonsoft.Json.Linq;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// Turns whatever the CLI printed into the candidate flavor document.
    ///
    /// <para>
    /// There are two layers of wrapping to get through, and neither is guaranteed to be present:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>The CLI envelope.</b> <c>--output-format json</c> prints a result object whose
    /// <c>result</c> field is the model's text. Envelopes that report an error
    /// (<c>is_error: true</c>, or a <c>subtype</c> other than success) are rejected here rather than
    /// being fed to the validator, so the log says "the CLI reported an error" instead of "malformed
    /// JSON".
    /// </description></item>
    /// <item><description>
    /// <b>The model's own framing.</b> Even under a strict instruction, a model will sometimes wrap
    /// JSON in a <c>```json</c> fence or open with a sentence. Stripping fences and then taking the
    /// first balanced brace span recovers those without loosening validation one bit - whatever comes
    /// out still has to pass the schema.
    /// </description></item>
    /// </list>
    ///
    /// <para>
    /// The forgiving step is deliberately confined to <i>locating</i> the JSON. Nothing here repairs,
    /// coerces or fills in a document; a response that needs that is a failed response.
    /// </para>
    ///
    /// <para>Pure: no I/O, no game types, no randomness.</para>
    /// </summary>
    public static class ClaudeResponseReader
    {
        /// <summary>
        /// Extracts the flavor JSON from the CLI's stdout. Returns null and sets
        /// <paramref name="error"/> when there is nothing usable.
        /// </summary>
        public static string ExtractFlavorJson(string standardOutput, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(standardOutput) || standardOutput.Trim().Length == 0)
            {
                error = "the CLI produced no output";
                return null;
            }

            string text = standardOutput.Trim();

            // Layer 1: the CLI envelope. Absent when someone points the option at a plain
            // pass-through binary, so a failure to parse it is not fatal by itself.
            string envelopeError;
            string inner = UnwrapEnvelope(text, out envelopeError);
            if (envelopeError != null)
            {
                error = envelopeError;
                return null;
            }
            if (inner != null) text = inner.Trim();

            // Layer 2: the model's framing.
            text = StripCodeFence(text).Trim();

            // Always go through the balance check, even when the text already opens with a brace.
            // Returning it unchecked would hand the validator a truncated document and turn "the
            // stream was cut off" into "malformed JSON at line 1, position 4000", which is a much
            // worse thing to find in a log a month later.
            string span = FirstBalancedObject(text);
            if (span != null) return span;

            error = text.Length > 0 && text[0] == '{'
                ? "the JSON object is unterminated; the response was truncated"
                : "no JSON object found in the response";
            return null;
        }

        /// <summary>
        /// Pulls <c>result</c> out of the CLI's JSON envelope.
        /// Returns null when the text is not an envelope (leave it alone), and sets
        /// <paramref name="error"/> when it is an envelope that reports failure.
        /// </summary>
        public static string UnwrapEnvelope(string text, out string error)
        {
            error = null;

            string ignored;
            JToken token = FlavorJsonReader.Parse(text, out ignored);
            var envelope = token as JObject;
            if (envelope == null) return null;

            // The flavor document itself is an object too. Tell them apart by a field only the
            // envelope has - never by a field only the flavor has, since a truncated flavor document
            // would then be misread as an envelope.
            JToken result = envelope["result"];
            JToken isError = envelope["is_error"];
            JToken subtype = envelope["subtype"];
            JToken type = envelope["type"];

            bool looksLikeEnvelope = result != null || isError != null ||
                                     (type != null && type.Type == JTokenType.String &&
                                      string.Equals(type.Value<string>(), "result", StringComparison.Ordinal));
            if (!looksLikeEnvelope) return null;

            if (isError != null && isError.Type == JTokenType.Boolean && isError.Value<bool>())
            {
                error = "the CLI reported an error" + DescribeSubtype(subtype) + ResultSnippet(result);
                return null;
            }

            if (subtype != null && subtype.Type == JTokenType.String)
            {
                string s = subtype.Value<string>();
                if (!string.Equals(s, "success", StringComparison.Ordinal))
                {
                    error = "the CLI returned subtype '" + s + "'" + ResultSnippet(result);
                    return null;
                }
            }

            if (result == null)
            {
                error = "the CLI envelope carried no 'result' field";
                return null;
            }

            if (result.Type == JTokenType.Object)
            {
                // Some CLI versions can return structured output directly. Re-serialise it and let
                // the validator judge the content.
                return result.ToString(Newtonsoft.Json.Formatting.None);
            }

            if (result.Type != JTokenType.String)
            {
                error = "the CLI envelope's 'result' was " + result.Type.ToString().ToLowerInvariant() +
                        ", not text";
                return null;
            }

            return result.Value<string>();
        }

        /// <summary>
        /// Removes a single surrounding markdown fence, with or without a language tag. Text that is
        /// not fenced comes back unchanged.
        /// </summary>
        public static string StripCodeFence(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

            string trimmed = text.Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return text;

            int firstNewline = trimmed.IndexOf('\n');
            if (firstNewline < 0) return text;

            int closing = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (closing <= firstNewline) return trimmed.Substring(firstNewline + 1);

            return trimmed.Substring(firstNewline + 1, closing - firstNewline - 1);
        }

        /// <summary>
        /// Returns the first balanced <c>{ ... }</c> span, or null.
        /// </summary>
        /// <remarks>
        /// String-aware: a brace inside a quoted value, and an escaped quote inside that value, must
        /// not move the depth counter. Getting that wrong truncates any document containing a slogan
        /// with a brace in it - rare, and exactly the sort of rare that shows up once and is never
        /// reproducible.
        /// </remarks>
        public static string FirstBalancedObject(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            int start = text.IndexOf('{');
            if (start < 0) return null;

            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];

                if (inString)
                {
                    if (escaped) { escaped = false; continue; }
                    if (c == '\\') { escaped = true; continue; }
                    if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') { inString = true; continue; }
                if (c == '{') { depth++; continue; }
                if (c == '}')
                {
                    depth--;
                    if (depth == 0) return text.Substring(start, i - start + 1);
                }
            }

            return null;
        }

        private static string DescribeSubtype(JToken subtype) =>
            subtype != null && subtype.Type == JTokenType.String
                ? " (" + subtype.Value<string>() + ")"
                : string.Empty;

        private static string ResultSnippet(JToken result)
        {
            if (result == null || result.Type != JTokenType.String) return string.Empty;
            string s = result.Value<string>() ?? string.Empty;
            if (s.Length == 0) return string.Empty;
            if (s.Length > 200) s = s.Substring(0, 200);
            return ": " + s.Replace('\n', ' ').Replace('\r', ' ');
        }
    }
}
