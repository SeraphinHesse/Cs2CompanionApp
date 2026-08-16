// Requires the FlavorValidator.cs / FlavorDocument.cs / NumericFieldScanner.cs /
// JsonSchemaSubsetValidator.cs / FlavorCacheMigration.cs / FlavorSchema.cs <Compile Link> lines in
// Agora.Core.Tests.csproj (see the comment there for why).

using System.Globalization;
using Agora.Core.Contracts;
using Agora.Mod.Llm;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The validation chain over <c>stories</c> and <c>resolutions</c>, the two collections wave 5
    /// added to <c>politics_flavor</c>.
    ///
    /// <para>
    /// Two rules meet here and they pull in opposite directions on purpose. A <b>number</b> anywhere
    /// in either collection is fatal to the whole document — non-negotiable #1, and the numeric sweep
    /// is its sole executable enforcement (<c>tests/CLAUDE.md</c> suite 2). An <b>unknown story id</b>
    /// costs one entry and nothing else, because story ids are minted per cycle and turn over
    /// completely every few months, so a stale cached document naming a story that has since been
    /// archived is an ordinary event rather than a reason to lose a year of prose.
    /// </para>
    ///
    /// <para>
    /// The migration half is the guard on the failure <see cref="FlavorCacheMigration"/> exists for: a
    /// schema error is fatal to the entire cache, so one over-length entry would take every
    /// <c>partyFlavor</c> entry with it — which is every party <i>name</i>, and the player reloads and
    /// sees <c>party-01</c>. The limits went <i>up</i> this wave, so a cache written by any shipped
    /// build must arrive at the current version with nothing pruned at all.
    /// </para>
    ///
    /// <para>
    /// Every length and version below is read from <see cref="FlavorCacheMigration"/> or
    /// <see cref="FlavorSchema"/>. A fixture that memorised the numbers would go red on the next
    /// retune for a reason unrelated to what it guards.
    /// </para>
    /// </summary>
    public class FlavorStoryProseTests
    {
        /// <summary>The sim date the request was made for.</summary>
        private static readonly SimDate RequestDate = new SimDate(1997, 6, 1);

        private const string GoodStoryId = "story-harbour-1997-06";
        private const string GhostStoryId = "story-that-resolved-last-spring";

        /// <summary>
        /// Everything the fixtures legitimately reference. <see cref="GhostStoryId"/> is deliberately
        /// absent: it stands in for an id the engine has never heard of.
        /// </summary>
        private static FlavorCatalog Catalog() => new FlavorCatalog(
            new[] { "party-riverside", "party-uplands" },
            new string[0],
            new string[0],
            new[] { "event-harbour-flood" },
            new[] { GoodStoryId });

        private static FlavorValidator Validator() =>
            FlavorValidator.Create(null, NullFlavorLog.Instance);

        // ---- fixtures ------------------------------------------------------------------------------

        /// <summary>
        /// A party block worth protecting. It is what the player sees in the seat chart, and it is
        /// what the migration is written to keep hold of.
        /// </summary>
        private static JArray PartyFlavor() => new JArray
        {
            new JObject
            {
                ["partyId"] = "party-riverside",
                ["name"] = "Riverside Slate",
                ["shortName"] = "RS",
                ["description"] = "Harbour wards, tram money, long memories.",
                ["slogan"] = "Keep the water working."
            },
            new JObject
            {
                ["partyId"] = "party-uplands",
                ["name"] = "Uplands Union",
                ["shortName"] = "UU",
                ["description"] = "The ridge, the reservoir, and the bus that never comes.",
                ["slogan"] = "Look up the hill."
            }
        };

        private static JObject Story(string storyId, string headline, string article) => new JObject
        {
            ["storyId"] = storyId,
            ["headline"] = headline,
            ["article"] = article
        };

        private static JObject Story(string storyId) =>
            Story(storyId, "The wharf argument reaches the chamber",
                  "Three weeks of it, and the slate still has not said what the tram costs.");

        /// <summary>A document the shipped schema accepts, carrying whatever stories are handed in.</summary>
        private static JObject Document(int schemaVersion) => new JObject
        {
            ["schemaVersion"] = schemaVersion,
            ["generatedAtSimDate"] = "1997-06-01",
            ["partyFlavor"] = PartyFlavor()
        };

        private static JObject Document() => Document(FlavorSchema.SupportedSchemaVersion);

        private static string Text(JObject root) => root.ToString(Formatting.None);

        private static string Repeat(char c, int count) => new string(c, count);

        // ---- non-negotiable #1, on the new collections ----------------------------------------------

        [Fact]
        public void NumberWherePolicyProseBelongs_FailsTheWholeDocument()
        {
            // The shape that would matter: the model reporting how the story went as a figure rather
            // than as a sentence. Nothing reads it — the point is that it never gets the chance, and
            // that the whole document goes rather than the entry, because a document that is willing
            // to state one number is not a document to accept the rest of.
            JObject root = Document();
            root["stories"] = new JArray
            {
                new JObject
                {
                    ["storyId"] = GoodStoryId,
                    ["headline"] = "The wharf argument reaches the chamber",
                    ["article"] = 4.2
                }
            };

            FlavorValidationResult result = Validator().Validate(Text(root), Catalog(), RequestDate);

            Assert.False(result.IsValid);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public void NumericPropertyAddedToAResolution_FailsTheWholeDocument()
        {
            JObject root = Document();
            root["resolutions"] = new JArray
            {
                new JObject
                {
                    ["storyId"] = GoodStoryId,
                    ["headline"] = "The tram money is found",
                    ["article"] = "It came out of the bridge, which nobody wanted to say out loud.",
                    ["powerCost"] = 12
                }
            };

            FlavorValidationResult result = Validator().Validate(Text(root), Catalog(), RequestDate);

            Assert.False(result.IsValid);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public void SweepAloneReportsANumberInEitherCollection()
        {
            // Asserted against the sweep on its own, not through the validator. The schema also
            // rejects both of these — type: "string" everywhere, additionalProperties: false — so a
            // test that only ever ran the two gates together could not tell which was doing the work
            // and would keep passing after the sweep was deleted.
            JObject root = Document();
            root["stories"] = new JArray { Story(GoodStoryId) };
            ((JObject)root["stories"]![0]!)["article"] = 4.2;
            root["resolutions"] = new JArray { Story(GoodStoryId) };
            ((JObject)root["resolutions"]![0]!)["powerCost"] = 12;

            var found = NumericFieldScanner.FindNumbers(root);

            Assert.Equal(2, found.Count);
            Assert.Contains(found, path => path.Contains("$.stories[0].article"));
            Assert.Contains(found, path => path.Contains("$.resolutions[0].powerCost"));
        }

        // ---- unknown story ids drop the entry, never the document ------------------------------------

        [Fact]
        public void UnknownStoryId_DropsTheEntryAndKeepsTheDocument()
        {
            // Story ids turn over every few months, so a cached document naming one that has since
            // been archived is the commonest bad id there is. Prose attached to it would be shown
            // against whatever story now sits in that slot.
            JObject root = Document();
            root["stories"] = new JArray { Story(GoodStoryId), Story(GhostStoryId) };

            FlavorValidationResult result = Validator().Validate(Text(root), Catalog(), RequestDate);

            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            StoryProseEntry kept = Assert.Single(result.Document!.Stories);
            Assert.Equal(GoodStoryId, kept.StoryId);
            Assert.Contains(result.Discarded, line => line.Contains(GhostStoryId));
        }

        [Fact]
        public void UnknownStoryId_DropsTheEntryFromResolutionsToo()
        {
            JObject root = Document();
            root["resolutions"] = new JArray { Story(GhostStoryId), Story(GoodStoryId) };

            FlavorValidationResult result = Validator().Validate(Text(root), Catalog(), RequestDate);

            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            StoryProseEntry kept = Assert.Single(result.Document!.Resolutions);
            Assert.Equal(GoodStoryId, kept.StoryId);
            Assert.Contains(result.Discarded, line => line.Contains(GhostStoryId));
        }

        [Fact]
        public void OneStoryMayAppearInBothCollections()
        {
            // Opening prose and closing prose for the same story is the ordinary case at the moment a
            // story resolves, so the two collections are filtered independently. A shared "seen" set
            // across both would drop the resolution — the half the player has not read yet.
            JObject root = Document();
            root["stories"] = new JArray { Story(GoodStoryId, "The wharf argument reaches the chamber",
                                                 "Three weeks of it, and no figure yet.") };
            root["resolutions"] = new JArray { Story(GoodStoryId, "The tram money is found",
                                                     "It came out of the bridge.") };

            FlavorValidationResult result = Validator().Validate(Text(root), Catalog(), RequestDate);

            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            Assert.Equal(GoodStoryId, Assert.Single(result.Document!.Stories).StoryId);
            Assert.Equal(GoodStoryId, Assert.Single(result.Document!.Resolutions).StoryId);
            Assert.Empty(result.Discarded);
        }

        // ---- the cache upgrade -----------------------------------------------------------------------

        private static JObject Upgrade(JObject root, out int fromVersion, out int pruned)
        {
            string migrated = FlavorCacheMigration.UpgradeToCurrent(Text(root), null,
                                                                    out fromVersion, out pruned);
            JObject? upgraded = FlavorJsonReader.ParseObject(migrated);
            Assert.NotNull(upgraded);
            return upgraded!;
        }

        /// <summary>
        /// Every version this build has a route from, one theory case each. Written from the current
        /// version rather than as literals so the set grows with the schema instead of going stale.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void CacheWrittenAtAnOlderVersion_ArrivesCurrentWithItsPartyNames(int writtenAt)
        {
            Assert.True(writtenAt < FlavorSchema.SupportedSchemaVersion,
                        "this case is stale: version " + writtenAt.ToString(CultureInfo.InvariantCulture) +
                        " is no longer older than the one this build speaks.");

            JObject root = Document(writtenAt);
            root["articles"] = new JArray
            {
                new JObject
                {
                    ["id"] = "article-01",
                    ["outlet"] = "Harbour Register",
                    ["headline"] = Repeat('h', FlavorCacheMigration.HeadlineMaxLength),
                    ["body"] = Repeat('b', FlavorCacheMigration.BodyMaxLength)
                }
            };

            JToken before = Document(writtenAt)["partyFlavor"]!;

            int fromVersion, pruned;
            JObject upgraded = Upgrade(root, out fromVersion, out pruned);

            Assert.Equal(writtenAt, fromVersion);
            Assert.Equal(FlavorSchema.SupportedSchemaVersion, (int)upgraded["schemaVersion"]!);

            // The limits went UP this wave. Nothing an older build could legally have written is over
            // one of them, so a prune here means the migration is sweeping something it should not —
            // and every party name in the file is one tightening away from going with it.
            Assert.Equal(0, pruned);
            Assert.True(JToken.DeepEquals(before, upgraded["partyFlavor"]),
                        "party flavor must survive a cache upgrade untouched - it is every party's name.");
            Assert.Equal("1997-06-01", (string)upgraded["generatedAtSimDate"]!);
            Assert.Single((JArray)upgraded["articles"]!);
        }

        [Fact]
        public void OverLengthStoryArticle_IsPrunedAndNotTruncated()
        {
            // A body cut at the limit ends mid-sentence and would be published to the player as though
            // it had been written that way, so the whole entry goes. Its neighbours are the point: the
            // one over-length story must cost one story.
            string keptArticle = Repeat('a', FlavorCacheMigration.StoryArticleMaxLength);
            JObject root = Document(1);
            root["stories"] = new JArray
            {
                Story("story-too-long", "A headline inside the limit",
                      Repeat('x', FlavorCacheMigration.StoryArticleMaxLength + 1)),
                Story(GoodStoryId, "The wharf argument reaches the chamber", keptArticle)
            };
            root["resolutions"] = new JArray { Story(GoodStoryId) };

            int fromVersion, pruned;
            JObject upgraded = Upgrade(root, out fromVersion, out pruned);

            Assert.Equal(1, pruned);

            // The survivor is exactly on the limit: maxLength is inclusive, and an off-by-one here
            // would quietly drop every story a compliant model writes to length.
            var stories = (JArray)upgraded["stories"]!;
            JToken kept = Assert.Single(stories);
            Assert.Equal(GoodStoryId, (string)kept["storyId"]!);
            Assert.Equal(keptArticle, (string)kept["article"]!);

            Assert.Single((JArray)upgraded["resolutions"]!);
            Assert.Equal(2, ((JArray)upgraded["partyFlavor"]!).Count);
        }

        [Fact]
        public void OverLengthStoryHeadline_IsPrunedOnItsOwnLimit()
        {
            // stories.headline and articles.headline are two independent schema decisions that happen
            // to agree today. This reads the story constant, so a future retune of one cannot silently
            // move what this test asserts about the other.
            JObject root = Document(1);
            root["resolutions"] = new JArray
            {
                Story(GoodStoryId, Repeat('h', FlavorCacheMigration.StoryHeadlineMaxLength + 1),
                      "It came out of the bridge."),
                Story(GoodStoryId, Repeat('h', FlavorCacheMigration.StoryHeadlineMaxLength),
                      "It came out of the bridge, which nobody wanted to say out loud.")
            };

            int fromVersion, pruned;
            JObject upgraded = Upgrade(root, out fromVersion, out pruned);

            Assert.Equal(1, pruned);
            JToken kept = Assert.Single((JArray)upgraded["resolutions"]!);
            Assert.Equal(FlavorCacheMigration.StoryHeadlineMaxLength, ((string)kept["headline"]!).Length);
        }
    }
}
