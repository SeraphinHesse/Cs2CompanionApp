// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// A validator for the slice of JSON Schema that <c>data/schemas/politics_flavor.schema.json</c>
    /// actually uses.
    ///
    /// <para>
    /// <b>Why hand-rolled.</b> <c>Newtonsoft.Json.Schema</c> is a separate, commercially licensed
    /// package; only <c>Newtonsoft.Json</c> 13.0.2 ships with the game, and <c>src/CLAUDE.md</c>
    /// forbids adding a JSON dependency. So rather than approximate the schema in C# and let the two
    /// drift, this reads the schema document itself and interprets it.
    /// </para>
    ///
    /// <para>
    /// <b>Supported keywords:</b> <c>type</c> (object / array / string / integer / boolean),
    /// <c>const</c>, <c>enum</c>, <c>pattern</c>, <c>maxLength</c>, <c>minLength</c>,
    /// <c>required</c>, <c>properties</c>, <c>additionalProperties: false</c>, <c>items</c>,
    /// <c>maxItems</c>. Annotation keywords (<c>$schema</c>, <c>$id</c>, <c>title</c>,
    /// <c>$comment</c>, <c>description</c>, <c>default</c>) are ignored. <b>Anything else in the
    /// schema is a hard error</b>, reported as <c>unsupported schema keyword</c> — silently ignoring
    /// a constraint we do not understand is how a validator stops validating without anyone noticing.
    /// </para>
    ///
    /// <para>
    /// This class is pure: no game types, no I/O, no clock, no randomness. It is safe to call from
    /// the flavor worker thread.
    /// </para>
    /// </summary>
    public static class JsonSchemaSubsetValidator
    {
        /// <summary>Cap on reported errors, so a wildly wrong document cannot flood the log.</summary>
        public const int MaxErrors = 40;

        private static readonly HashSet<string> IgnoredKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "$schema", "$id", "title", "$comment", "description", "default", "examples", "deprecated"
        };

        private static readonly HashSet<string> KnownKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "type", "const", "enum", "pattern", "maxLength", "minLength",
            "required", "properties", "additionalProperties", "items", "maxItems"
        };

        /// <summary>
        /// Validates <paramref name="instance"/> against <paramref name="schema"/>.
        /// Returns every violation found (up to <see cref="MaxErrors"/>), newest last.
        /// Never throws — a malformed schema is reported as an error, not raised.
        /// </summary>
        public static IReadOnlyList<string> Validate(JToken instance, JObject schema)
        {
            var errors = new List<string>();

            if (schema == null)
            {
                errors.Add("schema: missing (nothing to validate against)");
                return errors;
            }

            try
            {
                ValidateNode(instance, schema, "$", errors);
            }
            catch (Exception ex)
            {
                errors.Add("schema: validator failed unexpectedly: " + ex.Message);
            }

            return errors;
        }

        private static void ValidateNode(JToken node, JObject schema, string path, List<string> errors)
        {
            if (errors.Count >= MaxErrors) return;

            if (node == null)
            {
                Add(errors, path + ": missing");
                return;
            }

            foreach (var property in schema.Properties())
            {
                string keyword = property.Name;
                if (IgnoredKeywords.Contains(keyword)) continue;
                if (!KnownKeywords.Contains(keyword))
                {
                    Add(errors, path + ": unsupported schema keyword '" + keyword +
                                "' - JsonSchemaSubsetValidator must be taught it before this schema can be trusted");
                }
            }

            var typeToken = schema["type"];
            if (typeToken != null && typeToken.Type == JTokenType.String)
            {
                string expected = typeToken.Value<string>();
                if (!MatchesType(node, expected))
                {
                    Add(errors, path + ": expected type '" + expected + "', found " + Describe(node));
                    // Once the type is wrong, the sub-keywords below would only produce noise.
                    return;
                }
            }

            var constToken = schema["const"];
            if (constToken != null && !JToken.DeepEquals(Normalise(node), Normalise(constToken)))
            {
                Add(errors, path + ": expected constant " + constToken.ToString(Newtonsoft.Json.Formatting.None) +
                            ", found " + Describe(node));
            }

            if (schema["enum"] is JArray allowed)
            {
                bool hit = false;
                foreach (var candidate in allowed)
                {
                    if (JToken.DeepEquals(Normalise(node), Normalise(candidate))) { hit = true; break; }
                }
                if (!hit)
                {
                    Add(errors, path + ": value " + Describe(node) + " is not one of " +
                                allowed.ToString(Newtonsoft.Json.Formatting.None));
                }
            }

            if (node.Type == JTokenType.String)
            {
                string value = node.Value<string>() ?? string.Empty;

                var maxLength = schema["maxLength"];
                if (maxLength != null && value.Length > maxLength.Value<int>())
                {
                    Add(errors, path + ": string is " + value.Length.ToString(CultureInfo.InvariantCulture) +
                                " characters, maximum is " + maxLength.Value<int>().ToString(CultureInfo.InvariantCulture));
                }

                var minLength = schema["minLength"];
                if (minLength != null && value.Length < minLength.Value<int>())
                {
                    Add(errors, path + ": string is shorter than the minimum of " +
                                minLength.Value<int>().ToString(CultureInfo.InvariantCulture));
                }

                var pattern = schema["pattern"];
                if (pattern != null)
                {
                    string regex = pattern.Value<string>();
                    bool ok;
                    try
                    {
                        // Timeout, not trust: the pattern comes from a file on disk, and a
                        // catastrophically backtracking regex must not wedge the worker thread.
                        ok = Regex.IsMatch(value, regex, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                    }
                    catch (Exception ex)
                    {
                        Add(errors, path + ": pattern '" + regex + "' could not be evaluated: " + ex.Message);
                        ok = true;
                    }
                    if (!ok) Add(errors, path + ": string does not match pattern '" + regex + "'");
                }
            }

            if (node is JObject obj)
            {
                ValidateObject(obj, schema, path, errors);
            }
            else if (node is JArray array)
            {
                ValidateArray(array, schema, path, errors);
            }
        }

        private static void ValidateObject(JObject obj, JObject schema, string path, List<string> errors)
        {
            var properties = schema["properties"] as JObject;

            if (schema["required"] is JArray required)
            {
                foreach (var name in required)
                {
                    string key = name.Value<string>();
                    if (obj[key] == null)
                    {
                        Add(errors, path + ": missing required property '" + key + "'");
                    }
                }
            }

            bool additionalAllowed = true;
            var additional = schema["additionalProperties"];
            if (additional != null && additional.Type == JTokenType.Boolean)
            {
                additionalAllowed = additional.Value<bool>();
            }

            foreach (var member in obj.Properties())
            {
                if (errors.Count >= MaxErrors) return;

                JToken subSchema = properties != null ? properties[member.Name] : null;
                if (subSchema == null)
                {
                    if (!additionalAllowed)
                    {
                        // This branch is the structural half of non-negotiable #1. The flavor schema
                        // sets additionalProperties:false everywhere precisely so that a smuggled
                        // numeric field has nowhere legal to land.
                        Add(errors, path + ": property '" + member.Name + "' is not allowed here");
                    }
                    continue;
                }

                if (subSchema is JObject subObject)
                {
                    ValidateNode(member.Value, subObject, path + "." + member.Name, errors);
                }
            }
        }

        private static void ValidateArray(JArray array, JObject schema, string path, List<string> errors)
        {
            var maxItems = schema["maxItems"];
            if (maxItems != null && array.Count > maxItems.Value<int>())
            {
                Add(errors, path + ": array has " + array.Count.ToString(CultureInfo.InvariantCulture) +
                            " items, maximum is " + maxItems.Value<int>().ToString(CultureInfo.InvariantCulture));
            }

            if (!(schema["items"] is JObject itemSchema)) return;

            for (int i = 0; i < array.Count; i++)
            {
                if (errors.Count >= MaxErrors) return;
                ValidateNode(array[i], itemSchema, path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", errors);
            }
        }

        private static bool MatchesType(JToken node, string expected)
        {
            switch (expected)
            {
                case "object": return node.Type == JTokenType.Object;
                case "array": return node.Type == JTokenType.Array;
                // Date/TimeSpan/Uri/Guid are deliberately excluded: the reader is configured with
                // DateParseHandling.None (see FlavorJsonReader), so a genuine JSON string always
                // arrives as JTokenType.String. If one of those shows up, the reader was
                // misconfigured and quietly accepting it would hide the bug.
                case "string": return node.Type == JTokenType.String;
                case "integer": return node.Type == JTokenType.Integer;
                case "number": return node.Type == JTokenType.Integer || node.Type == JTokenType.Float;
                case "boolean": return node.Type == JTokenType.Boolean;
                case "null": return node.Type == JTokenType.Null;
                default: return false;
            }
        }

        /// <summary>
        /// <c>JToken.DeepEquals</c> compares JValue by value and type, and an integer literal in the
        /// schema may load as Integer while the instance loads as Integer too — but a Float 1.0 and an
        /// Integer 1 are not DeepEqual. Comparing the invariant string form sidesteps that without
        /// loosening anything that matters here.
        /// </summary>
        private static JToken Normalise(JToken token)
        {
            if (token != null && token.Type == JTokenType.Float)
            {
                double d = token.Value<double>();
                if (d == Math.Floor(d) && !double.IsInfinity(d))
                {
                    return new JValue((long)d);
                }
            }
            return token;
        }

        private static string Describe(JToken node)
        {
            if (node == null) return "nothing";
            switch (node.Type)
            {
                case JTokenType.String:
                    string s = node.Value<string>() ?? string.Empty;
                    return "a string" + (s.Length <= 24 ? " (\"" + s + "\")" : " of " + s.Length + " characters");
                case JTokenType.Integer:
                case JTokenType.Float:
                    return "a number (" + node.ToString(Newtonsoft.Json.Formatting.None) + ")";
                default:
                    return "a " + node.Type.ToString().ToLowerInvariant();
            }
        }

        private static void Add(List<string> errors, string message)
        {
            if (errors.Count < MaxErrors) errors.Add(message);
            else if (errors.Count == MaxErrors) errors.Add("... further errors suppressed");
        }
    }
}
