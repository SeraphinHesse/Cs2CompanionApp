using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The serial gate's cross-packet test: every other suite in this project runs against
    /// <see cref="EngineTuning.Default"/>, and the game runs against <c>data/engine_tuning.json</c>.
    /// Nothing before this file compared the two. If they drift, the entire test suite is green
    /// while the shipped engine behaves differently — coefficients are the one thing 776 passing
    /// tests cannot vouch for, because they are deliberately not literals in the code.
    /// </summary>
    /// <remarks>
    /// Packet 14 reported this gap explicitly ("no test asserts they stay in sync, because a
    /// file-path-relative test is fragile under the gate agent's cwd"). The fragility is solved by
    /// walking up from the test assembly's own location to the repo root rather than trusting the
    /// working directory, so the test is stable under <c>dotnet test</c>, VSTest and an IDE runner.
    /// </remarks>
    public class ShippedTuningTests
    {
        private static string RepoRoot()
        {
            // AppContext.BaseDirectory, not Environment.CurrentDirectory: the runner's cwd varies,
            // the assembly's own location does not.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Agora.sln"))) return dir.FullName;
                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate the repository root (no Agora.sln above " + AppContext.BaseDirectory + ").");
        }

        private static string ShippedTuningPath() =>
            Path.Combine(RepoRoot(), "data", "engine_tuning.json");

        private static EngineTuning LoadShipped() =>
            EngineTuning.FromJson(File.ReadAllText(ShippedTuningPath()));

        [Fact]
        public void ShippedTuningFile_Exists()
        {
            Assert.True(File.Exists(ShippedTuningPath()),
                        "data/engine_tuning.json is the only place a coefficient may live; it must ship.");
        }

        [Fact]
        public void ShippedTuningFile_ParsesWithoutWarnings()
        {
            EngineTuning shipped = LoadShipped();

            // A warning means a key the engine reads is missing from the file or is the wrong shape,
            // i.e. the engine silently fell back to a built-in default the file does not declare.
            Assert.True(shipped.Warnings.Count == 0,
                        "data/engine_tuning.json has drifted from the engine: " +
                        string.Join("; ", shipped.Warnings));
        }

        [Fact]
        public void ShippedTuningFile_MatchesBuiltInDefaults()
        {
            EngineTuning shipped = LoadShipped();
            EngineTuning defaults = EngineTuning.Default;

            var mismatches = new List<string>();
            CompareSections(defaults, shipped, mismatches);

            Assert.True(mismatches.Count == 0,
                        "EngineTuning.Default and data/engine_tuning.json disagree. Every test in " +
                        "this suite runs against Default, so these values are unverified in the " +
                        "shipped engine:" + Environment.NewLine + string.Join(Environment.NewLine, mismatches));
        }

        [Fact]
        public void ShippedTuningFile_ShipsTheSameEffectPaletteAsTheBuiltInRegistry()
        {
            EffectsTuning shipped = LoadShipped().Effects;
            EffectsTuning defaults = EngineTuning.Default.Effects;

            Assert.Equal(defaults.EffectIds, shipped.EffectIds);

            for (int i = 0; i < defaults.EffectIds.Count; i++)
            {
                string id = defaults.EffectIds[i];

                EffectCap a, b;
                Assert.True(defaults.TryGetEffect(id, out a));
                Assert.True(shipped.TryGetEffect(id, out b), "shipped palette is missing '" + id + "'");

                Assert.Equal(a.Scope, b.Scope);
                Assert.Equal(a.Modifier, b.Modifier);
                Assert.Equal(a.MagnitudeCap, b.MagnitudeCap, 12);
                Assert.Equal(a.DurationCapMonths, b.DurationCapMonths);
                Assert.Equal(a.FallbackEffectId, b.FallbackEffectId);
            }
        }

        /// <summary>
        /// The shipped timeline catalogs must load through the real loader with nothing rejected.
        /// Today they are empty stubs and this is close to vacuous; it stops being vacuous the first
        /// time <c>/add-event</c> authors an entry whose effect id is not in the sanctioned palette,
        /// which is exactly the failure the loader exists to catch and which no synthetic-JSON test
        /// can see.
        /// </summary>
        [Theory]
        [InlineData("timeline_global.json")]
        [InlineData("timeline_eu.json")]
        [InlineData("timeline_na.json")]
        public void ShippedTimelineCatalog_LoadsWithNothingRejected(string fileName)
        {
            string path = Path.Combine(RepoRoot(), "data", fileName);
            Assert.True(File.Exists(path), fileName + " must ship.");

            Agora.Core.Events.Catalog.TimelineCatalogLoadResult result =
                Agora.Core.Events.Catalog.TimelineCatalogLoader.Load(
                    fileName, File.ReadAllText(path), EngineTuning.Default);

            var messages = new List<string>();
            for (int i = 0; i < result.Errors.Count; i++) messages.Add(result.Errors[i].ToString());

            Assert.True(result.IsValid, fileName + " has authoring errors:" + Environment.NewLine +
                                        string.Join(Environment.NewLine, messages));
            Assert.Equal(0, result.RejectedEventCount);
        }

        // ------------------------------------------------------------------------------------------
        // Reflective comparison. Sections are added by packet, so an explicit list would rot; walking
        // EngineTuning's own properties means a fifteenth section is covered the day it is added.
        // ------------------------------------------------------------------------------------------

        private static void CompareSections(EngineTuning defaults, EngineTuning shipped, List<string> mismatches)
        {
            PropertyInfo[] sections = typeof(EngineTuning).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            Array.Sort(sections, (a, b) => string.CompareOrdinal(a.Name, b.Name));

            for (int i = 0; i < sections.Length; i++)
            {
                PropertyInfo section = sections[i];
                if (section.Name == "Warnings") continue;   // load metadata, not tuning

                object? left = section.GetValue(defaults);
                object? right = section.GetValue(shipped);

                if (section.PropertyType.IsPrimitive)
                {
                    if (!Equals(left, right))
                        mismatches.Add(section.Name + ": default=" + left + " file=" + right);
                    continue;
                }

                if (left == null || right == null) continue;
                CompareScalars(section.Name, left, right, mismatches);
            }
        }

        private static void CompareScalars(string sectionName, object defaults, object shipped, List<string> mismatches)
        {
            PropertyInfo[] props = defaults.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            Array.Sort(props, (a, b) => string.CompareOrdinal(a.Name, b.Name));

            for (int i = 0; i < props.Length; i++)
            {
                PropertyInfo p = props[i];
                if (p.GetIndexParameters().Length != 0) continue;

                object? a = p.GetValue(defaults);
                object? b = p.GetValue(shipped);
                string label = sectionName + "." + p.Name;

                if (a is double da && b is double db)
                {
                    if (Math.Abs(da - db) > 1e-12) mismatches.Add(label + ": default=" + da + " file=" + db);
                    continue;
                }

                if (a is int || a is bool || a is string || (a != null && a.GetType().IsEnum))
                {
                    if (!Equals(a, b)) mismatches.Add(label + ": default=" + a + " file=" + b);
                    continue;
                }

                if (a is double[] arrA && b is double[] arrB)
                {
                    if (arrA.Length != arrB.Length)
                    {
                        mismatches.Add(label + ": default has " + arrA.Length + " entries, file has " + arrB.Length);
                        continue;
                    }

                    for (int k = 0; k < arrA.Length; k++)
                    {
                        if (Math.Abs(arrA[k] - arrB[k]) > 1e-12)
                            mismatches.Add(label + "[" + k + "]: default=" + arrA[k] + " file=" + arrB[k]);
                    }

                    continue;
                }

                // Nested value objects (IssueWeights, IssuePosition and friends): one level down is
                // enough — they bottom out in doubles.
                if (a != null && b != null && a.GetType() == b.GetType() &&
                    a.GetType().Namespace != null && a.GetType().Namespace!.StartsWith("Agora.Core", StringComparison.Ordinal))
                {
                    CompareScalars(label, a, b, mismatches);
                }
            }
        }
    }
}
