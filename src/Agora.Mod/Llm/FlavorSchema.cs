// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// Supplies the <c>politics_flavor</c> JSON Schema that every LLM response is checked against.
    ///
    /// <para>
    /// <b>Why there is an embedded copy.</b> <c>data/</c> is not deployed: the mod folder receives
    /// <c>Agora.Mod.dll</c>, <c>Agora.Core.dll</c> and the UI bundle, and nothing else (see
    /// <c>Agora.Mod.csproj</c> - there are no <c>Content</c> items). So at runtime there is no schema
    /// file to read, and a provider that fell back to "no schema, accept anything" would quietly
    /// disable the one check that enforces non-negotiable #1. The embedded copy is therefore the
    /// runtime authority; the on-disk file is a development-time override.
    /// </para>
    ///
    /// <para>
    /// <b>Drift.</b> <see cref="EmbeddedJson"/> is a verbatim copy of
    /// <c>data/schemas/politics_flavor.schema.json</c>. <see cref="MatchesFile"/> exists so a test or
    /// a gate can assert the two are still equivalent. Changing the schema means changing both, via
    /// <c>/schema-change</c>.
    /// </para>
    /// </summary>
    public static class FlavorSchema
    {
        /// <summary>The <c>schemaVersion</c> this provider speaks.</summary>
        public const int SupportedSchemaVersion = 2;

        /// <summary>Repo-relative path of the authoritative file.</summary>
        public const string RepoRelativePath = "data/schemas/politics_flavor.schema.json";

        /// <summary>
        /// Verbatim copy of <c>data/schemas/politics_flavor.schema.json</c>. Keep byte-equivalent.
        /// </summary>
        public const string EmbeddedJson = @"{
  ""$schema"": ""https://json-schema.org/draft/2020-12/schema"",
  ""$id"": ""https://agora.local/schemas/politics_flavor.schema.json"",
  ""title"": ""politics_flavor.json - LLM output, prose only"",

  ""type"": ""object"",
  ""additionalProperties"": false,
  ""required"": [""schemaVersion"", ""generatedAtSimDate""],

  ""properties"": {
    ""schemaVersion"": { ""type"": ""integer"", ""const"": 2 },
    ""generatedAtSimDate"": { ""type"": ""string"", ""pattern"": ""^\\d{4}-\\d{2}-\\d{2}$"" },

    ""partyFlavor"": {
      ""type"": ""array"",
      ""items"": {
        ""type"": ""object"",
        ""additionalProperties"": false,
        ""required"": [""partyId"", ""name""],
        ""properties"": {
          ""partyId"": { ""type"": ""string"" },
          ""name"": { ""type"": ""string"", ""maxLength"": 80 },
          ""shortName"": { ""type"": ""string"", ""maxLength"": 12 },
          ""description"": { ""type"": ""string"", ""maxLength"": 600 },
          ""slogan"": { ""type"": ""string"", ""maxLength"": 120 }
        }
      }
    },

    ""factionFlavor"": {
      ""type"": ""array"",
      ""items"": {
        ""type"": ""object"",
        ""additionalProperties"": false,
        ""required"": [""factionId"", ""name""],
        ""properties"": {
          ""factionId"": { ""type"": ""string"" },
          ""partyId"": { ""type"": ""string"" },
          ""name"": { ""type"": ""string"", ""maxLength"": 80 },
          ""shortName"": { ""type"": ""string"", ""maxLength"": 12 },
          ""description"": { ""type"": ""string"", ""maxLength"": 600 },
          ""leaderName"": { ""type"": ""string"", ""maxLength"": 80 }
        }
      }
    },

    ""articles"": {
      ""type"": ""array"",
      ""items"": {
        ""type"": ""object"",
        ""additionalProperties"": false,
        ""required"": [""id"", ""outlet"", ""headline"", ""body""],
        ""properties"": {
          ""id"": { ""type"": ""string"" },
          ""outlet"": { ""type"": ""string"", ""maxLength"": 60 },
          ""headline"": { ""type"": ""string"", ""maxLength"": 90 },
          ""body"": { ""type"": ""string"", ""maxLength"": 420 },
          ""tone"": { ""type"": ""string"", ""enum"": [""neutral"", ""supportive"", ""critical"", ""alarmed"", ""celebratory""] },
          ""refs"": {
            ""type"": ""object"",
            ""additionalProperties"": false,
            ""properties"": {
              ""eventId"": { ""type"": ""string"" },
              ""districtId"": { ""type"": ""string"" },
              ""partyId"": { ""type"": ""string"" }
            }
          }
        }
      }
    },

    ""eventProse"": {
      ""type"": ""array"",
      ""items"": {
        ""type"": ""object"",
        ""additionalProperties"": false,
        ""required"": [""eventId"", ""localAngle""],
        ""properties"": {
          ""eventId"": { ""type"": ""string"" },
          ""localAngle"": { ""type"": ""string"", ""maxLength"": 900 }
        }
      }
    }
  }
}";

        /// <summary>
        /// Loads the schema. Prefers <paramref name="overrideFilePath"/> when it exists and parses;
        /// otherwise returns the embedded copy. Never throws and never returns null - a provider with
        /// no schema would have to either reject everything or accept everything, and both are worse
        /// than validating against the compiled-in copy.
        /// </summary>
        public static JObject Load(string overrideFilePath, IFlavorLog log)
        {
            log = log ?? NullFlavorLog.Instance;

            if (!string.IsNullOrEmpty(overrideFilePath))
            {
                try
                {
                    if (File.Exists(overrideFilePath))
                    {
                        var fromFile = FlavorJsonReader.ParseObject(File.ReadAllText(overrideFilePath));
                        if (fromFile != null)
                        {
                            log.Debug("schema loaded from " + overrideFilePath);
                            return fromFile;
                        }
                        log.Warn("schema at " + overrideFilePath + " did not parse as an object; using the embedded copy");
                    }
                }
                catch (Exception ex)
                {
                    log.Warn("schema at " + overrideFilePath + " could not be read (" + ex.Message +
                             "); using the embedded copy");
                }
            }

            var embedded = FlavorJsonReader.ParseObject(EmbeddedJson);
            if (embedded == null)
            {
                // Unreachable short of someone breaking the literal above, but a null here would
                // disable validation, so say so loudly rather than degrade.
                log.Error("the embedded politics_flavor schema failed to parse - flavor validation cannot run");
            }
            return embedded;
        }

        /// <summary>
        /// True when the file at <paramref name="filePath"/> is structurally identical to
        /// <see cref="EmbeddedJson"/>. For a drift test; whitespace and key order are ignored, so this
        /// compares meaning rather than formatting.
        /// </summary>
        public static bool MatchesFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                var onDisk = FlavorJsonReader.ParseObject(File.ReadAllText(filePath));
                var embedded = FlavorJsonReader.ParseObject(EmbeddedJson);
                if (onDisk == null || embedded == null) return false;

                // "title" and "$comment" are annotation keywords the validator ignores, and both
                // carry em dashes in the repo file that this ASCII-only literal does not reproduce.
                // Comparing them would report drift where there is none; comparing everything else
                // catches the drift that matters - a constraint added on one side and not the other.
                foreach (string annotation in new[] { "title", "$comment" })
                {
                    onDisk.Remove(annotation);
                    embedded.Remove(annotation);
                }
                return JToken.DeepEquals(onDisk, embedded);
            }
            catch
            {
                return false;
            }
        }
    }
}
