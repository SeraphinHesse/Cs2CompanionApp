// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Agora.Core.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Agora.Mod.Persistence
{
    /// <summary>
    /// The single place Agora's JSON wire conventions are defined. Every sidecar file, and every
    /// artifact handed to the flavor provider, goes through here.
    ///
    /// <para>
    /// The conventions are contractual, not stylistic — <c>data/schemas/political_state.schema.json</c>
    /// is written against them and declares <c>additionalProperties: false</c> on nearly every object,
    /// so a stray property is a validation failure rather than harmless noise:
    /// </para>
    ///
    /// <list type="bullet">
    ///   <item>camelCase property names</item>
    ///   <item><see cref="SimDate"/> as the string <c>"YYYY-MM-DD"</c></item>
    ///   <item>enums as their C# member names (flags as a comma-separated member list)</item>
    ///   <item><see cref="Guid"/> in the canonical 8-4-4-4-12 form</item>
    /// </list>
    ///
    /// <para>
    /// <b>Determinism.</b> Non-negotiable #3 defines desync as the SHA-256 of serialized state
    /// changing across a reload, so serialization has to be a pure function of the object. Culture is
    /// pinned to invariant and date auto-parsing is switched off; without the latter Newtonsoft turns
    /// anything shaped like a date into a <c>DateTime</c> when it lands in an <c>object</c>, which
    /// re-serializes differently depending on the machine's locale.
    /// </para>
    ///
    /// <para>
    /// Agora.Core never serializes anything (see <c>src/Agora.Core/CLAUDE.md</c>); this type is why.
    /// Newtonsoft.Json ships with the game, so no package dependency is added.
    /// </para>
    /// </summary>
    public static class AgoraJson
    {
        private static readonly JsonSerializerSettings SharedSettings = CreateSettings();
        private static readonly JsonSerializer SharedSerializer = JsonSerializer.Create(SharedSettings);

        /// <summary>Shared settings. Treat as read-only — mutating it changes every sidecar file.</summary>
        public static JsonSerializerSettings Settings
        {
            get { return SharedSettings; }
        }

        /// <summary>A fresh, independently mutable copy, for callers that need to tweak one thing.</summary>
        public static JsonSerializerSettings CreateSettings()
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                Culture = CultureInfo.InvariantCulture,

                // Off deliberately: with the default, every "1994-03-01" that passes through an
                // object-typed slot becomes a DateTime and re-serializes with a time and an offset.
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,

                // A field the current schema does not know about is a migration input, not a crash.
                MissingMemberHandling = MissingMemberHandling.Ignore,

                // Nulls are written out: the schema distinguishes "absent" from "explicitly null"
                // for optional dates, and a reader should not have to guess which it got.
                NullValueHandling = NullValueHandling.Include,
                DefaultValueHandling = DefaultValueHandling.Include,

                // Sidecar files describe engine state, never types. Never emit or honour $type:
                // deserializing a type name out of a file on disk is a remote-code-execution shape.
                TypeNameHandling = TypeNameHandling.None,

                ContractResolver = new DefaultContractResolver
                {
                    // Not CamelCasePropertyNamesContractResolver: that one also rewrites dictionary
                    // keys, and bloc ids and district ids travel as keys in other packets' payloads.
                    NamingStrategy = new CamelCaseNamingStrategy(false, false, false)
                }
            };

            settings.Converters.Add(new SimDateJsonConverter());
            settings.Converters.Add(new BlocKeyJsonConverter());

            // No naming strategy: the schemas spell enum values as their C# member names ("Governing",
            // "FirstPastThePost"), so camelCasing them here would break every enum in every file.
            settings.Converters.Add(new StringEnumConverter());

            return settings;
        }

        public static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, SharedSettings);
        }

        public static T Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, SharedSettings);
        }

        /// <summary>
        /// Parses to a DOM without materialising a contract type. This is the first step of every
        /// load: <c>schemaVersion</c> has to be read, and migrations applied, before the document is
        /// allowed to become a <see cref="PoliticalState"/>.
        /// </summary>
        public static JObject ParseObject(string json)
        {
            using (var stringReader = new StringReader(json))
            using (var jsonReader = new JsonTextReader(stringReader))
            {
                jsonReader.DateParseHandling = DateParseHandling.None;
                jsonReader.FloatParseHandling = FloatParseHandling.Double;
                jsonReader.Culture = CultureInfo.InvariantCulture;
                return JObject.Load(jsonReader);
            }
        }

        /// <summary>Materialises a (possibly migrated) DOM into a contract type.</summary>
        public static T ToObject<T>(JObject root)
        {
            if (root == null) throw new ArgumentNullException("root");
            return root.ToObject<T>(SharedSerializer);
        }

        /// <summary>
        /// SHA-256 of the serialized form, lowercase hex. This is the operational definition of
        /// "desync" in <c>tests/CLAUDE.md</c>: the fingerprint of state at sim date D must be equal
        /// before and after a reload.
        /// </summary>
        public static string Fingerprint(object value)
        {
            return FingerprintOf(Serialize(value));
        }

        public static string FingerprintOf(string text)
        {
            if (text == null) text = string.Empty;

            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }
                return builder.ToString();
            }
        }
    }

    /// <summary>
    /// <see cref="SimDate"/> ⇄ <c>"YYYY-MM-DD"</c>.
    /// </summary>
    /// <remarks>
    /// Mandatory, not cosmetic. <see cref="SimDate"/> is a struct whose three properties are
    /// get-only, and Newtonsoft's default handling of a struct is "create the default instance, then
    /// set the properties" — which for this type silently produces <c>0000-00-00</c> for every date
    /// in the file. The string form also matches <c>#/$defs/simDate</c> in the schemas.
    ///
    /// <para>
    /// <c>default(SimDate)</c> round-trips through <c>"0000-00-00"</c>. That value is reachable —
    /// a contract field of type <c>SimDate</c> that was never assigned holds it — and month 0 would
    /// otherwise throw out of the constructor on the way back in.
    /// </para>
    /// </remarks>
    public sealed class SimDateJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            Type underlying = Nullable.GetUnderlyingType(objectType);
            return (underlying ?? objectType) == typeof(SimDate);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteValue(Format((SimDate)value));
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
                                        JsonSerializer serializer)
        {
            bool nullable = Nullable.GetUnderlyingType(objectType) != null;

            if (reader.TokenType == JsonToken.Null)
            {
                if (nullable) return null;
                return default(SimDate);
            }

            string text = reader.Value == null
                ? null
                : Convert.ToString(reader.Value, CultureInfo.InvariantCulture);

            SimDate date;
            if (!TryParse(text, out date))
            {
                throw new JsonSerializationException(
                    "Expected a sim date of the form YYYY-MM-DD, got '" + (text ?? "null") + "'.");
            }

            return date;
        }

        public static string Format(SimDate date)
        {
            // SimDate.ToString() is already D4-D2-D2 and is documented as the sidecar filename and
            // seed-derivation form. Going through it keeps the two spellings from drifting apart.
            return date.ToString();
        }

        public static bool TryParse(string text, out SimDate date)
        {
            date = default(SimDate);
            if (string.IsNullOrEmpty(text)) return false;

            string[] parts = text.Split('-');
            if (parts.Length != 3) return false;

            int year, month, day;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out year)) return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out month)) return false;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out day)) return false;

            // The unset form. Accepted so a never-assigned SimDate field survives a round trip.
            if (month == 0 || day == 0)
            {
                date = default(SimDate);
                return true;
            }

            if (month < 1 || month > 12 || day < 1 || day > 31 || year < 0) return false;

            date = new SimDate(year, month, day);
            return true;
        }
    }

    /// <summary>
    /// <see cref="BlocKey"/> ⇄ <c>{ "wealth": …, "education": …, "age": … }</c>.
    /// </summary>
    /// <remarks>
    /// Also mandatory. <see cref="BlocKey"/> carries two computed get-only properties,
    /// <c>Ordinal</c> and <c>Id</c>, which Newtonsoft would happily emit — and
    /// <c>#/$defs/blocKey</c> declares <c>additionalProperties: false</c>, so every bloc in every
    /// sidecar file would fail schema validation. Writing the three axes explicitly is the fix.
    ///
    /// <para>
    /// Reading also accepts the dotted id form (<c>"middle.educated.adult"</c>), because that
    /// spelling is what appears in seed sub-streams and UI bindings and will inevitably be
    /// hand-written into a fixture at some point.
    /// </para>
    /// </remarks>
    public sealed class BlocKeyJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            Type underlying = Nullable.GetUnderlyingType(objectType);
            return (underlying ?? objectType) == typeof(BlocKey);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var key = (BlocKey)value;

            writer.WriteStartObject();
            writer.WritePropertyName("wealth");
            writer.WriteValue(key.Wealth.ToString());
            writer.WritePropertyName("education");
            writer.WriteValue(key.Education.ToString());
            writer.WritePropertyName("age");
            writer.WriteValue(key.Age.ToString());
            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
                                        JsonSerializer serializer)
        {
            bool nullable = Nullable.GetUnderlyingType(objectType) != null;

            if (reader.TokenType == JsonToken.Null)
            {
                if (nullable) return null;
                return default(BlocKey);
            }

            if (reader.TokenType == JsonToken.String)
            {
                string id = Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
                BlocKey byId;
                if (TryParseId(id, out byId)) return byId;
                throw new JsonSerializationException("Unknown bloc id '" + (id ?? "null") + "'.");
            }

            JObject obj = JObject.Load(reader);

            WealthTier wealth;
            EducationTier education;
            AgeBand age;

            if (!TryParseEnum(obj, "wealth", out wealth) ||
                !TryParseEnum(obj, "education", out education) ||
                !TryParseEnum(obj, "age", out age))
            {
                throw new JsonSerializationException(
                    "A bloc key needs wealth, education and age, each a valid member name.");
            }

            return new BlocKey(wealth, education, age);
        }

        private static bool TryParseEnum<TEnum>(JObject obj, string property, out TEnum result)
            where TEnum : struct
        {
            result = default(TEnum);

            JToken token = obj[property];
            if (token == null || token.Type == JTokenType.Null) return false;

            string text = token.Value<string>();
            if (string.IsNullOrEmpty(text)) return false;

            return Enum.TryParse(text, true, out result);
        }

        /// <summary>
        /// Resolves the dotted form via <see cref="BlocAxes.AllKeys"/>. Scanning the 60 keys is the
        /// only spelling of this mapping that cannot drift from <see cref="BlocKey.Id"/>.
        /// </summary>
        public static bool TryParseId(string id, out BlocKey key)
        {
            key = default(BlocKey);
            if (string.IsNullOrEmpty(id)) return false;

            var all = BlocAxes.AllKeys;
            for (int i = 0; i < all.Count; i++)
            {
                if (string.Equals(all[i].Id, id, StringComparison.Ordinal))
                {
                    key = all[i];
                    return true;
                }
            }

            return false;
        }
    }
}
