// Requires the FlavorSchema.cs / FlavorJsonReader.cs / FlavorCacheMigration.cs <Compile Link> lines
// in Agora.Core.Tests.csproj (see the comment there for why).

using System;
using System.IO;
using Agora.Mod.Llm;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The embedded <c>politics_flavor</c> schema against the file it is a copy of.
    ///
    /// <para>
    /// <c>data/</c> is not deployed, so the literal in <c>FlavorSchema</c> is the runtime authority
    /// and the file on disk is what a human edits. Nothing kept them in step: <c>MatchesFile</c> has
    /// documented an anti-drift gate since it was written and was called by nothing at all, so a
    /// constraint tightened on one side and forgotten on the other would have shipped, and shown up
    /// as a live response the player never sees rather than as a red build.
    /// </para>
    /// </summary>
    public class FlavorSchemaDriftTests
    {
        /// <summary>
        /// AppContext.BaseDirectory, not Environment.CurrentDirectory: the runner's cwd varies, the
        /// assembly's own location does not.
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

        private static string SchemaPath() =>
            Path.Combine(RepoRoot(), FlavorSchema.RepoRelativePath.Replace('/', Path.DirectorySeparatorChar));

        [Fact]
        public void EmbeddedSchema_MatchesTheFileOnDisk()
        {
            string path = SchemaPath();
            Assert.True(File.Exists(path), FlavorSchema.RepoRelativePath + " must ship.");

            Assert.True(FlavorSchema.MatchesFile(path),
                        "The embedded politics_flavor schema and " + FlavorSchema.RepoRelativePath +
                        " have drifted. The embedded copy is what the deployed mod validates against, " +
                        "so the file is the one a human reads and the literal is the one that runs.");
        }

        [Fact]
        public void EmbeddedSchema_DeclaresTheTightenedArticleLimits()
        {
            // Read literally rather than through MatchesFile: that test proves the two sides agree,
            // this one proves what they agree on. Both moving together in the wrong direction is a
            // change nobody would notice until an article came back a paragraph long.
            JObject embedded = FlavorJsonReader.ParseObject(FlavorSchema.EmbeddedJson);
            Assert.NotNull(embedded);

            JToken properties = embedded!["properties"]!["articles"]!["items"]!["properties"]!;

            Assert.Equal(FlavorCacheMigration.HeadlineMaxLength, (int)properties["headline"]!["maxLength"]!);
            Assert.Equal(FlavorCacheMigration.BodyMaxLength, (int)properties["body"]!["maxLength"]!);
            Assert.Equal(90, (int)properties["headline"]!["maxLength"]!);
            Assert.Equal(420, (int)properties["body"]!["maxLength"]!);

            // Unchanged by the same pass, and stated here so a future tightening cannot sweep them in
            // by accident: articles were the only thing that moved.
            Assert.Equal(60, (int)properties["outlet"]!["maxLength"]!);
            Assert.Equal(900, (int)embedded["properties"]!["eventProse"]!["items"]!["properties"]!
                                       ["localAngle"]!["maxLength"]!);
        }
    }
}
