// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

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
            if (span != null)
            {
                // Nothing was unwrapped, yet what we located is shaped like the CLI's own envelope.
                // Handing that to the validator produces a page of unknown-field errors and blames
                // the model for a seam that never ran, so say which it was.
                if (inner == null && IsEnvelopeShaped(span))
                {
                    error = "the CLI envelope could not be unwrapped; the object found is the " +
                            "envelope itself, not a flavor document";
                    return null;
                }

                return span;
            }

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

            JObject envelope = FindEnvelope(text);
            if (envelope == null) return null;

            JToken result = envelope["result"];
            JToken isError = envelope["is_error"];
            JToken subtype = envelope["subtype"];

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
        ///
        /// <para>
        /// <b>The two escape checks below are ordered, not interchangeable.</b> Consuming the
        /// already-escaped character has to come first; testing for a backslash first would let the
        /// second half of a <c>\\</c> pair re-arm the flag, so the quote after it would be swallowed
        /// and a slogan containing a single backslash would truncate the whole document.
        /// <c>ClaudeResponseReaderTests</c> pins this.
        /// </para>
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

        /// <summary>
        /// Locates the CLI envelope in <paramref name="text"/>, or null when there is not one.
        /// </summary>
        /// <remarks>
        /// The strict whole-text parse comes first: that is what a well-behaved CLI prints, and it is
        /// the only reading that can see two documents glued together and refuse them.
        ///
        /// <para>
        /// When it fails we retry against the first balanced brace span alone. A CLI that prints a
        /// warning line, a progress object, or any other byte after the envelope trips
        /// <see cref="FlavorJsonReader"/>'s trailing-content check, and without this retry the
        /// envelope would be declared "not an envelope", left alone, and then extracted whole as if
        /// it were the flavor document - a validator failure that reads as the model's fault. Only
        /// <i>locating</i> is forgiving here; the document that comes out still goes through the
        /// unchanged strict path.
        /// </para>
        /// </remarks>
        private static JObject FindEnvelope(string text)
        {
            string ignored;
            var candidate = FlavorJsonReader.Parse(text, out ignored) as JObject;

            if (candidate == null)
            {
                string span = FirstBalancedObject(text);
                if (span == null || span.Length == text.Length) return null;
                candidate = FlavorJsonReader.Parse(span, out ignored) as JObject;
                if (candidate == null) return null;
            }

            // The flavor document itself is an object too. Tell them apart by a field only the
            // envelope has - never by a field only the flavor has, since a truncated flavor document
            // would then be misread as an envelope.
            JToken type = candidate["type"];
            bool looksLikeEnvelope = candidate["result"] != null || candidate["is_error"] != null ||
                                     (type != null && type.Type == JTokenType.String &&
                                      string.Equals(type.Value<string>(), "result", StringComparison.Ordinal));

            return looksLikeEnvelope ? candidate : null;
        }

        /// <summary>
        /// Diagnostic only: true when <paramref name="span"/> carries bookkeeping fields the CLI puts
        /// on its envelope and the flavor schema has none of. Wider than the unwrap test on purpose -
        /// it never decides what gets unwrapped, only which failure gets reported - but still keyed
        /// exclusively off envelope fields, so a flavor document cannot land here.
        /// </summary>
        private static bool IsEnvelopeShaped(string span)
        {
            string ignored;
            var obj = FlavorJsonReader.Parse(span, out ignored) as JObject;
            if (obj == null) return false;

            JToken type = obj["type"];
            return obj["result"] != null || obj["is_error"] != null ||
                   obj["session_id"] != null || obj["usage"] != null || obj["duration_ms"] != null ||
                   (type != null && type.Type == JTokenType.String &&
                    string.Equals(type.Value<string>(), "result", StringComparison.Ordinal));
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
