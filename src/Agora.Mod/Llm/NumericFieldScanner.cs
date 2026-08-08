// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// Walks a parsed flavor document and reports every numeric or boolean leaf outside the one
    /// place a number is legal: the top-level <c>schemaVersion</c>.
    ///
    /// <para>
    /// This is redundant with the schema - <c>additionalProperties: false</c> plus
    /// <c>type: "string"</c> on every other property already makes a number unrepresentable - and it
    /// is here anyway. Non-negotiable #1 is the rule the whole design hangs off, the schema and the
    /// C# are two artefacts that can drift apart, and this check is a dozen lines that cannot. If it
    /// ever fires while the schema validator passes, the schema is wrong.
    /// </para>
    ///
    /// <para>Pure: no I/O, no game types, no randomness.</para>
    /// </summary>
    public static class NumericFieldScanner
    {
        /// <summary>The only legal number in a flavor document.</summary>
        public const string AllowedNumericPath = "$.schemaVersion";

        /// <summary>
        /// Returns a human-readable description of every illegal numeric/boolean leaf. Empty means
        /// the document carries prose, IDs and dates only.
        /// </summary>
        public static IReadOnlyList<string> FindNumbers(JToken root)
        {
            var found = new List<string>();
            if (root != null) Walk(root, "$", found);
            return found;
        }

        private static void Walk(JToken node, string path, List<string> found)
        {
            switch (node.Type)
            {
                case JTokenType.Object:
                    foreach (var property in ((JObject)node).Properties())
                    {
                        Walk(property.Value, path + "." + property.Name, found);
                    }
                    return;

                case JTokenType.Array:
                    var array = (JArray)node;
                    for (int i = 0; i < array.Count; i++)
                    {
                        Walk(array[i], path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", found);
                    }
                    return;

                case JTokenType.Integer:
                case JTokenType.Float:
                    if (path != AllowedNumericPath)
                    {
                        found.Add(path + " carries the number " +
                                  node.ToString(Newtonsoft.Json.Formatting.None) +
                                  " (non-negotiable #1: flavor is prose, IDs and dates only)");
                    }
                    return;

                case JTokenType.Boolean:
                    // A boolean is a one-bit number. Same rule, same reason.
                    found.Add(path + " carries a boolean (non-negotiable #1: flavor is prose, IDs and dates only)");
                    return;

                default:
                    return;
            }
        }
    }
}
