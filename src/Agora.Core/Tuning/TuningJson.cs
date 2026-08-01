using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Agora.Core.Contracts;

namespace Agora.Core.Tuning
{
    /// <summary>Thrown when <c>engine_tuning.json</c> is not valid JSON. Missing keys never throw.</summary>
    public sealed class TuningFormatException : Exception
    {
        public TuningFormatException(string message) : base(message) { }
    }

    internal enum JsonKind
    {
        Object,
        Array,
        String,
        Number,
        Bool,
        Null
    }

    /// <summary>
    /// A parsed JSON value. Deliberately hand-rolled: <c>Agora.Core</c> takes no dependencies, and
    /// the test suite must run with nothing but the framework (see <c>tests/CLAUDE.md</c>).
    /// </summary>
    internal sealed class JsonNode
    {
        public JsonKind Kind;
        public Dictionary<string, JsonNode>? Members;
        public List<JsonNode>? Items;
        public string? Text;
        public double Number;
        public bool Flag;

        public static readonly JsonNode EmptyObject = new JsonNode
        {
            Kind = JsonKind.Object,
            Members = new Dictionary<string, JsonNode>(StringComparer.Ordinal)
        };
    }

    /// <summary>
    /// A minimal, allocation-honest JSON parser covering exactly what a tuning file contains:
    /// objects, arrays, strings, numbers, booleans and null.
    /// </summary>
    /// <remarks>
    /// It is intentionally strict about numbers — always <see cref="CultureInfo.InvariantCulture"/> —
    /// because a culture-sensitive parse would read <c>0.55</c> as <c>55</c> on a machine with a
    /// comma decimal separator, and the resulting politics would differ by locale. That is a
    /// determinism defect that no test on a single machine would ever catch.
    /// </remarks>
    internal static class TuningJsonParser
    {
        public static JsonNode Parse(string json)
        {
            if (json == null) throw new TuningFormatException("Tuning JSON was null.");

            int i = 0;
            SkipWhitespace(json, ref i);
            JsonNode root = ParseValue(json, ref i);
            SkipWhitespace(json, ref i);

            if (i != json.Length)
                throw new TuningFormatException($"Trailing content at offset {i}.");

            return root;
        }

        private static JsonNode ParseValue(string s, ref int i)
        {
            if (i >= s.Length) throw new TuningFormatException("Unexpected end of input.");

            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return new JsonNode { Kind = JsonKind.String, Text = ParseString(s, ref i) };
                case 't': Expect(s, ref i, "true"); return new JsonNode { Kind = JsonKind.Bool, Flag = true };
                case 'f': Expect(s, ref i, "false"); return new JsonNode { Kind = JsonKind.Bool, Flag = false };
                case 'n': Expect(s, ref i, "null"); return new JsonNode { Kind = JsonKind.Null };
                default: return ParseNumber(s, ref i);
            }
        }

        private static JsonNode ParseObject(string s, ref int i)
        {
            var members = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            i++; // '{'
            SkipWhitespace(s, ref i);

            if (i < s.Length && s[i] == '}') { i++; return new JsonNode { Kind = JsonKind.Object, Members = members }; }

            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"')
                    throw new TuningFormatException($"Expected a property name at offset {i}.");

                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);

                if (i >= s.Length || s[i] != ':')
                    throw new TuningFormatException($"Expected ':' after '{key}' at offset {i}.");
                i++;

                SkipWhitespace(s, ref i);
                members[key] = ParseValue(s, ref i);
                SkipWhitespace(s, ref i);

                if (i >= s.Length) throw new TuningFormatException("Unterminated object.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; break; }

                throw new TuningFormatException($"Expected ',' or '}}' at offset {i}.");
            }

            return new JsonNode { Kind = JsonKind.Object, Members = members };
        }

        private static JsonNode ParseArray(string s, ref int i)
        {
            var items = new List<JsonNode>();
            i++; // '['
            SkipWhitespace(s, ref i);

            if (i < s.Length && s[i] == ']') { i++; return new JsonNode { Kind = JsonKind.Array, Items = items }; }

            while (true)
            {
                SkipWhitespace(s, ref i);
                items.Add(ParseValue(s, ref i));
                SkipWhitespace(s, ref i);

                if (i >= s.Length) throw new TuningFormatException("Unterminated array.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; break; }

                throw new TuningFormatException($"Expected ',' or ']' at offset {i}.");
            }

            return new JsonNode { Kind = JsonKind.Array, Items = items };
        }

        private static string ParseString(string s, ref int i)
        {
            i++; // opening quote
            var sb = new StringBuilder();

            while (i < s.Length)
            {
                char c = s[i++];

                if (c == '"') return sb.ToString();

                if (c != '\\') { sb.Append(c); continue; }

                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new TuningFormatException("Truncated \\u escape.");
                        sb.Append((char)ushort.Parse(s.Substring(i, 4), NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new TuningFormatException($"Unknown escape '\\{e}'.");
                }
            }

            throw new TuningFormatException("Unterminated string.");
        }

        private static JsonNode ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;

            while (i < s.Length)
            {
                char c = s[i];
                bool part = (c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-';
                if (!part) break;
                i++;
            }

            string token = s.Substring(start, i - start);
            if (token.Length == 0)
                throw new TuningFormatException($"Expected a value at offset {start}.");

            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new TuningFormatException($"'{token}' at offset {start} is not a number.");

            return new JsonNode { Kind = JsonKind.Number, Number = value };
        }

        private static void Expect(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
                throw new TuningFormatException($"Expected '{literal}' at offset {i}.");
            i += literal.Length;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
                break;
            }
        }
    }

    /// <summary>
    /// Reads one object out of the tuning tree, falling back to a supplied default and recording a
    /// warning whenever a key is missing or the wrong shape.
    /// </summary>
    /// <remarks>
    /// Reading never throws. A tuning file that has drifted from the code produces a fully-populated
    /// <see cref="EngineTuning"/> plus a warning list — the engine keeps running with documented
    /// defaults rather than failing at the worst possible moment, which is the same fail-closed
    /// posture non-negotiable #7 takes with the LLM.
    /// </remarks>
    public sealed class TuningReader
    {
        private readonly JsonNode _node;
        private readonly string _path;
        private readonly List<string> _warnings;

        internal TuningReader(JsonNode node, string path, List<string> warnings)
        {
            _node = node.Kind == JsonKind.Object ? node : JsonNode.EmptyObject;
            _path = path;
            _warnings = warnings;
        }

        /// <summary>Child keys, sorted ordinal ascending. Never enumerate the raw dictionary.</summary>
        public IReadOnlyList<string> ChildKeys()
        {
            var keys = new List<string>();
            if (_node.Members != null)
            {
                foreach (var kv in _node.Members)
                {
                    if (kv.Key.Length > 0 && kv.Key[0] == '_') continue; // "_comment" and friends
                    keys.Add(kv.Key);
                }
            }
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        /// <summary>True when the key exists on this object.</summary>
        public bool Has(string key) => _node.Members != null && _node.Members.ContainsKey(key);

        /// <summary>A reader for a child object. A missing child yields an empty reader, not null.</summary>
        public TuningReader Section(string key)
        {
            if (_node.Members != null && _node.Members.TryGetValue(key, out JsonNode child))
            {
                if (child.Kind == JsonKind.Object)
                    return new TuningReader(child, Join(key), _warnings);

                Warn(key, "expected an object");
            }
            else
            {
                Warn(key, "section missing; using defaults");
            }

            return new TuningReader(JsonNode.EmptyObject, Join(key), _warnings);
        }

        public double Num(string key, double fallback)
        {
            if (_node.Members != null && _node.Members.TryGetValue(key, out JsonNode child))
            {
                if (child.Kind == JsonKind.Number) return child.Number;
                Warn(key, "expected a number");
                return fallback;
            }

            Warn(key, "missing; using default " + fallback.ToString("R", CultureInfo.InvariantCulture));
            return fallback;
        }

        public int Int(string key, int fallback)
        {
            if (_node.Members != null && _node.Members.TryGetValue(key, out JsonNode child))
            {
                if (child.Kind == JsonKind.Number)
                {
                    double d = child.Number;
                    // Round-half-away-from-zero, stated explicitly so the result never depends on
                    // banker's rounding turning 2.5 into 2 on one path and 3 on another.
                    return (int)(d >= 0 ? Math.Floor(d + 0.5) : Math.Ceiling(d - 0.5));
                }

                Warn(key, "expected an integer");
                return fallback;
            }

            Warn(key, "missing; using default " + fallback.ToString(CultureInfo.InvariantCulture));
            return fallback;
        }

        public bool Flag(string key, bool fallback)
        {
            if (_node.Members != null && _node.Members.TryGetValue(key, out JsonNode child))
            {
                if (child.Kind == JsonKind.Bool) return child.Flag;
                Warn(key, "expected a boolean");
                return fallback;
            }

            Warn(key, "missing; using default " + (fallback ? "true" : "false"));
            return fallback;
        }

        public string Text(string key, string fallback)
        {
            if (_node.Members != null && _node.Members.TryGetValue(key, out JsonNode child))
            {
                if (child.Kind == JsonKind.String) return child.Text ?? fallback;
                Warn(key, "expected a string");
                return fallback;
            }

            Warn(key, "missing; using default '" + fallback + "'");
            return fallback;
        }

        /// <summary>An array of numbers, in file order. A missing or malformed array yields the fallback.</summary>
        public double[] Numbers(string key, double[] fallback)
        {
            if (_node.Members != null && _node.Members.TryGetValue(key, out JsonNode child))
            {
                if (child.Kind == JsonKind.Array && child.Items != null)
                {
                    var values = new double[child.Items.Count];
                    for (int i = 0; i < child.Items.Count; i++)
                    {
                        if (child.Items[i].Kind != JsonKind.Number)
                        {
                            Warn(key, "array element " + i + " is not a number");
                            return fallback;
                        }
                        values[i] = child.Items[i].Number;
                    }
                    return values;
                }

                Warn(key, "expected an array of numbers");
                return fallback;
            }

            Warn(key, "missing; using default array");
            return fallback;
        }

        /// <summary>An array of strings, in file order.</summary>
        public string[] Strings(string key, string[] fallback)
        {
            if (_node.Members != null && _node.Members.TryGetValue(key, out JsonNode child))
            {
                if (child.Kind == JsonKind.Array && child.Items != null)
                {
                    var values = new string[child.Items.Count];
                    for (int i = 0; i < child.Items.Count; i++)
                    {
                        if (child.Items[i].Kind != JsonKind.String)
                        {
                            Warn(key, "array element " + i + " is not a string");
                            return fallback;
                        }
                        values[i] = child.Items[i].Text ?? "";
                    }
                    return values;
                }

                Warn(key, "expected an array of strings");
                return fallback;
            }

            Warn(key, "missing; using default array");
            return fallback;
        }

        /// <summary>
        /// A six-key issue map (<c>services</c>, <c>costOfLiving</c>, <c>environment</c>,
        /// <c>transit</c>, <c>growth</c>, <c>heritageOrder</c>) read as per-issue weights.
        /// </summary>
        public IssueWeights Weights(string key, IssueWeights fallback)
        {
            TuningReader s = Section(key);
            return new IssueWeights(
                s.Num("services", fallback.Services),
                s.Num("costOfLiving", fallback.CostOfLiving),
                s.Num("environment", fallback.Environment),
                s.Num("transit", fallback.Transit),
                s.Num("growth", fallback.Growth),
                s.Num("heritageOrder", fallback.HeritageOrder));
        }

        /// <summary>The same six-key issue map read as a stance vector.</summary>
        public IssuePosition Position(string key, IssuePosition fallback)
        {
            TuningReader s = Section(key);
            return new IssuePosition(
                s.Num("services", fallback.Services),
                s.Num("costOfLiving", fallback.CostOfLiving),
                s.Num("environment", fallback.Environment),
                s.Num("transit", fallback.Transit),
                s.Num("growth", fallback.Growth),
                s.Num("heritageOrder", fallback.HeritageOrder));
        }

        /// <summary>A nine-key service map read as coverage weights.</summary>
        public ServiceCoverage Services(string key, ServiceCoverage fallback)
        {
            TuningReader s = Section(key);
            return new ServiceCoverage(
                s.Num("health", fallback.Health),
                s.Num("education", fallback.Education),
                s.Num("police", fallback.Police),
                s.Num("fire", fallback.Fire),
                s.Num("garbage", fallback.Garbage),
                s.Num("transit", fallback.Transit),
                s.Num("water", fallback.Water),
                s.Num("electricity", fallback.Electricity),
                s.Num("parks", fallback.Parks));
        }

        /// <summary>A four-key age-band map (<c>child</c>, <c>teen</c>, <c>adult</c>, <c>elderly</c>).</summary>
        public AgeBandMultipliers Ages(string key, AgeBandMultipliers fallback)
        {
            TuningReader s = Section(key);
            return new AgeBandMultipliers(
                s.Num("child", fallback.Child),
                s.Num("teen", fallback.Teen),
                s.Num("adult", fallback.Adult),
                s.Num("elderly", fallback.Elderly));
        }

        private string Join(string key) => _path.Length == 0 ? key : _path + "." + key;

        private void Warn(string key, string reason) => _warnings.Add(Join(key) + ": " + reason);
    }

    /// <summary>Per-age-band scalar, e.g. the turnout multiplier that disenfranchises minors.</summary>
    public readonly struct AgeBandMultipliers
    {
        public double Child { get; }
        public double Teen { get; }
        public double Adult { get; }
        public double Elderly { get; }

        public AgeBandMultipliers(double child, double teen, double adult, double elderly)
        {
            Child = child;
            Teen = teen;
            Adult = adult;
            Elderly = elderly;
        }

        public double this[AgeBand band]
        {
            get
            {
                switch (band)
                {
                    case AgeBand.Child: return Child;
                    case AgeBand.Teen: return Teen;
                    case AgeBand.Adult: return Adult;
                    case AgeBand.Elderly: return Elderly;
                    default: throw new ArgumentOutOfRangeException(nameof(band), band, "Unknown age band.");
                }
            }
        }
    }
}
