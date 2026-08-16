// Requires the FlavorSchema.cs / FlavorJsonReader.cs / FlavorCacheMigration.cs <Compile Link> lines
// in Agora.Core.Tests.csproj (see the comment there for why).

using System;
using System.Collections.Generic;
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

            // The SHAPE of the pair, not the pair itself. This test used to assert the literals 90
            // and 420 alongside the two lines above, which made it a second copy of the constants
            // rather than a check on them: it could only ever go red on the balance pass that moved
            // the limits deliberately, which is the one occasion its opinion is worth nothing. What
            // is actually worth pinning is that a body has room for several sentences more than a
            // headline, and that neither has been zeroed or swapped - the failures that would
            // silently produce a one-word article or a paragraph-long headline.
            int headline = (int)properties["headline"]!["maxLength"]!;
            int body = (int)properties["body"]!["maxLength"]!;

            Assert.True(headline >= 60, "a headline limit under 60 cannot hold a headline");
            Assert.True(body >= headline * 3, "a body must have room for far more than a headline");

            // The story pair is the same shape and, today, the same numbers - asserted against the
            // constants rather than against the article limits, because the two are independent
            // schema decisions that happen to agree.
            JToken stories = embedded["properties"]!["stories"]!["items"]!["properties"]!;
            Assert.Equal(FlavorCacheMigration.StoryHeadlineMaxLength, (int)stories["headline"]!["maxLength"]!);
            Assert.Equal(FlavorCacheMigration.StoryArticleMaxLength, (int)stories["article"]!["maxLength"]!);

            // resolutions is stories' twin. Two declarations rather than a $ref, so this is where
            // they are held together.
            JToken resolutions = embedded["properties"]!["resolutions"]!["items"]!["properties"]!;
            Assert.Equal((int)stories["headline"]!["maxLength"]!, (int)resolutions["headline"]!["maxLength"]!);
            Assert.Equal((int)stories["article"]!["maxLength"]!, (int)resolutions["article"]!["maxLength"]!);

            // Unchanged by the same pass, and stated here so a future tightening cannot sweep them in
            // by accident: articles were the only existing thing that moved.
            Assert.Equal(60, (int)properties["outlet"]!["maxLength"]!);
            Assert.Equal(900, (int)embedded["properties"]!["eventProse"]!["items"]!["properties"]!
                                       ["localAngle"]!["maxLength"]!);
        }

        /// <summary>
        /// Every capped prose collection in the schema is one the cache migration knows how to prune.
        /// </summary>
        /// <remarks>
        /// The failure this exists for is silent and total. <c>FlavorValidator</c> treats a schema
        /// error as fatal to the <i>whole</i> document, so one over-length entry in a collection the
        /// migration does not sweep discards the entire cache - every party name with it. That is
        /// the exact bug <c>FlavorCacheMigration</c> was written for, and the way it comes back is
        /// not by breaking the migration but by adding a collection beside the ones it knows.
        /// Enumerating the schema rather than listing the collections is what makes this notice.
        /// </remarks>
        [Fact]
        public void EveryCappedProseCollection_IsSweptByTheCacheMigration()
        {
            JObject embedded = FlavorJsonReader.ParseObject(FlavorSchema.EmbeddedJson);
            Assert.NotNull(embedded);

            // The collections the migration prunes, by name. Adding one to the schema without adding
            // it here is what this test is for.
            var swept = new HashSet<string> { "articles", "stories", "resolutions" };

            // Collections deliberately NOT swept, with the reason each is safe: no limit of theirs
            // has ever moved, and the identity content among them is what the cache exists to keep.
            var exempt = new HashSet<string> { "partyFlavor", "factionFlavor", "eventProse" };

            var properties = (JObject)embedded!["properties"]!;

            foreach (var property in properties)
            {
                if (property.Value?["type"]?.Value<string>() != "array") continue;

                Assert.True(swept.Contains(property.Key) || exempt.Contains(property.Key),
                    "politics_flavor gained the array '" + property.Key + "'. Either add it to " +
                    "FlavorCacheMigration.PruneOverLengthArticles and to the swept set here, or " +
                    "add it to the exempt set with the reason its limits can never tighten. An " +
                    "unswept capped collection discards the whole flavor cache on the first " +
                    "tightening, party names included.");
            }
        }
    }
}
