// Requires the Persistence <Compile Link> lines in Agora.Core.Tests.csproj — AgoraJson, whose wire
// conventions this schema is written against, and SidecarSchema, which owns the version constants.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Agora.Core.Contracts;
using Agora.Mod.Persistence;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// <c>data/schemas/political_state.schema.json</c> still describes the file this build writes.
    ///
    /// <para>
    /// The schema is documentation — nothing in the runtime validates against it — and documentation
    /// that is wrong about the shape of a file is worse than none. It drifted through six waves
    /// unnoticed, ending up five versions behind with <c>additionalProperties: false</c> over a
    /// property list that would have rejected every save this build produces. Nothing said so,
    /// because nothing read it.
    /// </para>
    ///
    /// <para>
    /// So this reads it. A fully populated <see cref="PoliticalState"/> is serialized through
    /// <see cref="AgoraJson"/> — the same path <c>SidecarStore</c> writes with — and walked against
    /// the schema. Every emitted property must be declared, and every declared property must have
    /// been emitted: one direction catches a contract that grew, the other a schema that kept a
    /// field the contract dropped.
    /// </para>
    ///
    /// <para>
    /// <b>Structure, not values.</b> The walk checks property names, <c>additionalProperties</c>,
    /// types, <c>const</c> and <c>enum</c>; it deliberately does not check <c>pattern</c>,
    /// <c>minLength</c>, <c>minimum</c> or their siblings, because a synthetic fixture's strings and
    /// numbers are not the ones a real save carries and asserting on them would only test the
    /// fixture. Those keywords must still be <i>recognised</i>: a keyword this walker does not
    /// understand is a hard failure rather than a skip, for the same reason
    /// <c>CloneStateCoverageTests</c> throws on a type it cannot seed — a silent skip shrinks a guard
    /// back to whatever it happened to cover and it stops failing without anyone noticing.
    /// </para>
    /// </summary>
    public class PoliticalStateSchemaTests
    {
        private const string SchemaFile = "political_state.schema.json";

        // --- 1. The two version pins ---------------------------------------------------------------

        /// <summary>
        /// The root <c>schemaVersion</c> the schema pins is the one the contract declares, and the one
        /// <c>SidecarSchema</c> migrates to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Deliberately version-<i>relative</i>, exactly as
        /// <c>HouseholdBudgetTests.TheSnapshotContractVersion_MatchesTheShippedSchema</c> is. A test
        /// that memorises <c>const: 8</c> goes red on the next bump for a reason that has nothing to
        /// do with what it guards, and the fix for it is to type the new number in two places — which
        /// is precisely the one-sided bump this exists to catch. This file's whole history is the
        /// lesson: the schema sat at <c>const: 3</c> while the contract reached 8, and no test could
        /// tell because none of them read the schema.
        /// </para>
        /// <para>
        /// Both sides are asserted — the contract default and the migration constant — because they
        /// are three numbers that must agree and pinning only two of them leaves a pair free to drift.
        /// </para>
        /// </remarks>
        [Fact]
        public void TheStateSchemaVersion_MatchesTheContractAndTheMigrationChain()
        {
            JObject schema = Schema();
            JToken? pinned = schema["properties"]?["schemaVersion"]?["const"];

            Assert.True(pinned != null,
                        SchemaFile + " must pin schemaVersion with a const, or nothing checks that " +
                        "the two sides of the contract agree.");

            Assert.Equal((int)pinned!, new PoliticalState().SchemaVersion);
            Assert.Equal(SidecarSchema.CurrentStateVersion, (int)pinned!);
        }

        /// <summary>
        /// The nested settings block carries its own version, and it moves on its own schedule — the
        /// state has been ahead of it by two since wave 0. It needs its own pin for that reason.
        /// </summary>
        [Fact]
        public void TheSettingsSchemaVersion_MatchesTheContractAndTheMigrationChain()
        {
            JObject schema = Schema();
            JToken? pinned = schema["$defs"]?["settings"]?["properties"]?["schemaVersion"]?["const"];

            Assert.True(pinned != null,
                        SchemaFile + " must pin the nested settings schemaVersion with a const.");

            Assert.Equal((int)pinned!, new AgoraSettings().SchemaVersion);
            Assert.Equal(SidecarSchema.CurrentSettingsVersion, (int)pinned!);
        }

        // --- 2. The shape ---------------------------------------------------------------------------

        /// <summary>
        /// A save this build writes validates against the schema that documents it.
        /// </summary>
        /// <remarks>
        /// The fixture is populated reflectively rather than by hand, so a property added to any
        /// contract on the graph is covered the moment it exists — nobody has to remember to come
        /// back. That is what makes this a guard rather than a snapshot of one afternoon's contract.
        /// </remarks>
        [Fact]
        public void AStateThisBuildWrites_IsDescribedByTheSchema()
        {
            JObject schema = Schema();
            JObject instance = AgoraJson.ParseObject(AgoraJson.Serialize(Populated()));

            var errors = new List<string>();
            var observed = new HashSet<string>(StringComparer.Ordinal);
            Walk(instance, schema, "$", schema, errors, observed);

            Assert.True(errors.Count == 0,
                        SchemaFile + " does not describe the state this build serializes:\n  " +
                        string.Join("\n  ", errors));
        }

        /// <summary>
        /// And the reverse: nothing is declared that the contract no longer writes.
        /// </summary>
        /// <remarks>
        /// The direction that rots silently. A field removed from a contract leaves its schema entry
        /// behind, and since <c>additionalProperties: false</c> only ever rejects <i>extra</i>
        /// properties, a stale declaration never fails anything — it just quietly documents a field
        /// that has not existed for a year.
        /// </remarks>
        [Fact]
        public void TheSchemaDeclaresNothingTheContractNoLongerWrites()
        {
            JObject schema = Schema();
            JObject instance = AgoraJson.ParseObject(AgoraJson.Serialize(Populated()));

            var errors = new List<string>();
            var observed = new HashSet<string>(StringComparer.Ordinal);
            Walk(instance, schema, "$", schema, errors, observed);

            var stale = new List<string>();
            foreach (string declared in Declared(schema))
            {
                if (!observed.Contains(declared)) stale.Add(declared);
            }

            stale.Sort(StringComparer.Ordinal);

            Assert.True(stale.Count == 0,
                        SchemaFile + " declares properties the contract never writes:\n  " +
                        string.Join("\n  ", stale));
        }

        // --- the schema -----------------------------------------------------------------------------

        private static JObject Schema()
        {
            string path = Path.Combine(RepoRoot(), "data", "schemas", SchemaFile);
            Assert.True(File.Exists(path),
                        "data/schemas/" + SchemaFile + " is the wire mirror of PoliticalState; it must ship.");

            return JObject.Parse(File.ReadAllText(path));
        }

        /// <summary>
        /// Walks up from the test binary to the repository root, the same way <c>ShippedTuningTests</c>
        /// and <c>HouseholdBudgetTests</c> do: the runner's working directory varies, so
        /// <c>AppContext.BaseDirectory</c> is the only stable anchor.
        /// </summary>
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Agora.sln"))) return dir.FullName;
                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate the repository root (no Agora.sln above " + AppContext.BaseDirectory + ").");
        }

        // --- the walker -----------------------------------------------------------------------------

        /// <summary>
        /// Keywords the walk understands and acts on. <c>$defs</c> is here as the definition
        /// container rather than as a constraint — it is never walked directly, only reached through
        /// a <c>$ref</c>.
        /// </summary>
        private static readonly HashSet<string> Enforced = new HashSet<string>(StringComparer.Ordinal)
        {
            "type", "const", "enum", "properties", "additionalProperties", "items", "oneOf", "$ref",
            "required", "$defs"
        };

        /// <summary>
        /// Keywords the walk recognises and deliberately does not act on — value constraints, which a
        /// synthetic fixture cannot honestly exercise, plus the annotation keywords.
        /// </summary>
        /// <remarks>
        /// The split between this set and <see cref="Enforced"/> is the honest part: a keyword in
        /// neither is a hard failure, so a constraint added to the schema cannot quietly become
        /// decoration. Widening this set is a decision someone has to write down, which is the
        /// difference between a guard and a formality.
        /// </remarks>
        private static readonly HashSet<string> Recognised = new HashSet<string>(StringComparer.Ordinal)
        {
            "$schema", "$id", "title", "$comment", "description", "default",
            "pattern", "minLength", "maxLength", "minimum", "maximum", "minItems", "maxItems"
        };

        private static void Walk(JToken? instance, JObject schema, string path, JObject root,
                                 List<string> errors, HashSet<string> observed)
        {
            if (instance == null || instance.Type == JTokenType.Null) return;

            foreach (KeyValuePair<string, JToken?> keyword in schema)
            {
                if (Enforced.Contains(keyword.Key) || Recognised.Contains(keyword.Key)) continue;

                throw new Xunit.Sdk.XunitException(
                    SchemaFile + " uses the keyword '" + keyword.Key + "' at " + schema.Path +
                    ", which this walk does not understand. Teach it, or drop the keyword — " +
                    "silently ignoring a constraint is how a guard stops guarding.");
            }

            var reference = schema["$ref"] as JValue;
            if (reference != null)
            {
                Walk(instance, Resolve((string)reference.Value!, root), path, root, errors, observed);
                return;
            }

            var alternatives = schema["oneOf"] as JArray;
            if (alternatives != null)
            {
                for (int i = 0; i < alternatives.Count; i++)
                {
                    var branchErrors = new List<string>();
                    var branchObserved = new HashSet<string>(StringComparer.Ordinal);
                    Walk(instance, (JObject)alternatives[i], path, root, branchErrors, branchObserved);

                    if (branchErrors.Count > 0) continue;

                    foreach (string seen in branchObserved) observed.Add(seen);
                    return;
                }

                errors.Add(path + ": matched none of the oneOf branches");
                return;
            }

            if (!TypeAllows(schema["type"], instance))
            {
                errors.Add(path + ": is " + instance.Type + ", which the declared type does not allow");
                return;
            }

            var constant = schema["const"];
            if (constant != null && !JToken.DeepEquals(constant, instance))
            {
                errors.Add(path + ": is " + instance + ", but the schema pins const " + constant);
            }

            var allowed = schema["enum"] as JArray;
            if (allowed != null && !Contains(allowed, instance))
            {
                errors.Add(path + ": is " + instance + ", which is not one of the declared members");
            }

            var obj = instance as JObject;
            if (obj != null)
            {
                WalkObject(obj, schema, path, root, errors, observed);
                return;
            }

            var array = instance as JArray;
            if (array != null)
            {
                var items = schema["items"] as JObject;
                if (items == null) return;

                for (int i = 0; i < array.Count; i++)
                {
                    Walk(array[i], items, path + "[" + i + "]", root, errors, observed);
                }
            }
        }

        private static void WalkObject(JObject instance, JObject schema, string path, JObject root,
                                       List<string> errors, HashSet<string> observed)
        {
            var declared = schema["properties"] as JObject;
            bool open = !(schema["additionalProperties"] is JValue closed) || (bool)closed.Value! != false;

            // A required property the writer never emits is a schema that would reject its own file.
            // The fixture writes everything, so this only ever fires on a name that was misspelled or
            // renamed on the contract and left behind here — which additionalProperties cannot catch,
            // because it only ever rejects what is extra.
            var mandatory = schema["required"] as JArray;
            if (mandatory != null)
            {
                for (int i = 0; i < mandatory.Count; i++)
                {
                    string name = (string)mandatory[i]!;
                    if (instance[name] == null)
                    {
                        errors.Add(path + "." + name + ": declared required, but never written");
                    }
                }
            }

            foreach (KeyValuePair<string, JToken?> property in instance)
            {
                JToken? subschema = declared == null ? null : declared[property.Key];

                if (subschema == null)
                {
                    if (!open) errors.Add(path + "." + property.Key + ": written, but not declared");
                    continue;
                }

                observed.Add(declared!.Path + "." + property.Key);
                Walk(property.Value, (JObject)subschema, path + "." + property.Key, root, errors, observed);
            }
        }

        /// <summary>Every property any object node in the schema declares, as a schema-document path.</summary>
        private static List<string> Declared(JObject schema)
        {
            var all = new List<string>();

            foreach (JToken node in schema.DescendantsAndSelf())
            {
                var obj = node as JObject;
                if (obj == null) continue;

                var properties = obj["properties"] as JObject;
                if (properties == null) continue;

                foreach (KeyValuePair<string, JToken?> property in properties)
                {
                    all.Add(properties.Path + "." + property.Key);
                }
            }

            return all;
        }

        private static JObject Resolve(string pointer, JObject root)
        {
            Assert.StartsWith("#/", pointer);

            JToken node = root;
            string[] parts = pointer.Substring(2).Split('/');

            for (int i = 0; i < parts.Length; i++)
            {
                JToken? next = node[parts[i]];
                Assert.True(next != null, SchemaFile + " has a dangling $ref: " + pointer);
                node = next!;
            }

            return (JObject)node;
        }

        private static bool TypeAllows(JToken? declared, JToken instance)
        {
            if (declared == null) return true;

            if (declared is JArray any)
            {
                for (int i = 0; i < any.Count; i++)
                {
                    if (Matches((string)any[i]!, instance)) return true;
                }

                return false;
            }

            return Matches((string)((JValue)declared).Value!, instance);
        }

        private static bool Matches(string type, JToken instance)
        {
            switch (type)
            {
                case "object": return instance.Type == JTokenType.Object;
                case "array": return instance.Type == JTokenType.Array;
                case "string": return instance.Type == JTokenType.String;
                case "boolean": return instance.Type == JTokenType.Boolean;
                case "integer": return instance.Type == JTokenType.Integer;
                case "number":
                    return instance.Type == JTokenType.Float || instance.Type == JTokenType.Integer;
                case "null": return instance.Type == JTokenType.Null;
                default:
                    throw new Xunit.Sdk.XunitException(
                        SchemaFile + " declares the type '" + type + "', which this walk does not know.");
            }
        }

        private static bool Contains(JArray allowed, JToken value)
        {
            for (int i = 0; i < allowed.Count; i++)
            {
                if (JToken.DeepEquals(allowed[i], value)) return true;
            }

            return false;
        }

        // --- the fixture ----------------------------------------------------------------------------

        /// <summary>
        /// A state with every property of every contract on its graph written, every list holding an
        /// element and every optional carrying a value — so the serialized document contains every
        /// property the wire format can ever contain.
        /// </summary>
        /// <remarks>
        /// <c>schemaVersion</c> is the one property left at its contract default, on every type that
        /// carries one. That is what makes the <c>const</c> checks in the walk mean something: they
        /// compare the schema's pin against the number the contract itself declares, rather than
        /// against a value this fixture invented.
        /// </remarks>
        private static PoliticalState Populated() => (PoliticalState)Fill(typeof(PoliticalState), 0);

        private static object Fill(Type type, int depth)
        {
            if (depth > 12)
            {
                throw new Xunit.Sdk.XunitException(
                    "The contract graph under PoliticalState is deeper than 12 levels, or it has a " +
                    "cycle. Either way this fixture cannot populate it.");
            }

            Type bare = Nullable.GetUnderlyingType(type) ?? type;

            if (bare == typeof(string)) return "populated";
            if (bare == typeof(bool)) return true;
            if (bare == typeof(int)) return 1;
            if (bare == typeof(long)) return 1L;
            if (bare == typeof(double)) return 0.5;
            if (bare == typeof(Guid)) return new Guid("11112222-3333-4444-5555-666677778888");
            if (bare == typeof(SimDate)) return new SimDate(1994, 3, 1);

            if (bare.IsEnum) return LastMember(bare);

            if (bare.IsGenericType && bare.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type element = bare.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(bare)!;
                list.Add(Fill(element, depth + 1));
                return list;
            }

            if (bare.IsValueType && !bare.IsPrimitive) return FillStruct(bare, depth);
            if (bare.IsClass) return FillObject(bare, depth);

            throw new Xunit.Sdk.XunitException(
                bare.Name + " appears on the PoliticalState graph and this fixture does not know how " +
                "to populate it. Teach Fill about it — skipping it would leave every property it " +
                "carries undeclared in the schema and unnoticed here.");
        }

        /// <summary>
        /// A contract struct is immutable by convention, so it is built through its widest
        /// constructor rather than by assignment.
        /// </summary>
        private static object FillStruct(Type type, int depth)
        {
            ConstructorInfo? widest = null;
            foreach (ConstructorInfo candidate in type.GetConstructors())
            {
                if (widest == null || candidate.GetParameters().Length > widest.GetParameters().Length)
                {
                    widest = candidate;
                }
            }

            if (widest == null || widest.GetParameters().Length == 0)
            {
                throw new Xunit.Sdk.XunitException(
                    type.Name + " is a struct with no constructor this fixture can use, so its " +
                    "properties would all serialize at their defaults and prove nothing.");
            }

            ParameterInfo[] parameters = widest.GetParameters();
            var arguments = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                arguments[i] = Fill(parameters[i].ParameterType, depth + 1);
            }

            return widest.Invoke(arguments);
        }

        private static object FillObject(Type type, int depth)
        {
            object instance = Activator.CreateInstance(type)!;

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || !property.CanWrite) continue;
                if (property.GetIndexParameters().Length > 0) continue;

                // Left at the contract's own default, so the schema's const is checked against the
                // number the contract declares rather than one this fixture chose.
                if (string.Equals(property.Name, "SchemaVersion", StringComparison.Ordinal)) continue;

                property.SetValue(instance, Fill(property.PropertyType, depth + 1));
            }

            return instance;
        }

        /// <summary>
        /// The last declared member. Not the first: a zero-valued member is what a default-constructed
        /// object already holds, and for a flags enum the last member is the widest combination, which
        /// exercises the comma-separated wire form the schema documents.
        /// </summary>
        private static object LastMember(Type enumType)
        {
            Array values = Enum.GetValues(enumType);
            Assert.True(values.Length > 0, enumType.Name + " has no members.");
            return values.GetValue(values.Length - 1)!;
        }
    }
}
