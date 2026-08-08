// Requires the FlavorCacheMigration.cs / FlavorSchema.cs / FlavorJsonReader.cs / FlavorLog.cs
// <Compile Link> lines in Agora.Core.Tests.csproj (see the comment there for why).

using System.Globalization;
using Agora.Mod.Llm;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// What happens to a cache written before the article limits tightened.
    ///
    /// <para>
    /// The stakes are asymmetric and that is the whole design. <c>FlavorValidator</c> treats a schema
    /// error as fatal to the entire document — deliberately, unlike the per-entry catalog drop beside
    /// it — so a single over-long body in <c>flavor_cache.json</c> would discard every
    /// <c>partyFlavor</c> entry with it, which is every party <i>name</i>. The player reloads and sees
    /// <c>party-01</c>. Pruning the offending articles before validation is what stands between the
    /// tightening and that regression, and
    /// <see cref="Upgrade_KeepsPartyFlavorWhenEveryArticleIsDropped"/> is the guard on it.
    /// </para>
    /// </summary>
    public class FlavorCacheMigrationTests
    {
        private const string PreviousVersion = "1";

        /// <summary>A party block worth protecting: it is what the player sees in the seat chart.</summary>
        private const string PartyFlavor =
            @"""partyFlavor"": [
    { ""partyId"": ""party-riverside"", ""name"": ""Riverside Slate"", ""shortName"": ""RS"",
      ""description"": ""Harbour wards, tram money, long memories."", ""slogan"": ""Keep the water working."" }
  ]";

        private static string Repeat(char c, int count) => new string(c, count);

        private static string Article(string id, int headlineLength, int bodyLength) =>
            @"{ ""id"": """ + id + @""", ""outlet"": ""Harbour Register"", ""headline"": """ +
            Repeat('h', headlineLength) + @""", ""body"": """ + Repeat('b', bodyLength) + @""" }";

        private static string Cache(string schemaVersion, string articles, string? extra = null) =>
            @"{
  ""schemaVersion"": " + schemaVersion + @",
  ""generatedAtSimDate"": ""1997-06-01"",
  " + PartyFlavor + @",
  ""articles"": [" + articles + @"]" + (extra == null ? "" : ",\n  " + extra) + @"
}";

        private static JObject Upgrade(string json, out int fromVersion, out int pruned)
        {
            string migrated = FlavorCacheMigration.UpgradeToCurrent(json, null, out fromVersion, out pruned);
            JObject root = FlavorJsonReader.ParseObject(migrated);
            Assert.NotNull(root);
            return root!;
        }

        [Fact]
        public void Upgrade_DropsOnlyTheOverLengthArticles()
        {
            string json = Cache(PreviousVersion, string.Join(",",
                Article("article-long-headline", FlavorCacheMigration.HeadlineMaxLength + 110, 100),
                Article("article-long-body", 40, FlavorCacheMigration.BodyMaxLength + 280),
                Article("article-ok", FlavorCacheMigration.HeadlineMaxLength, FlavorCacheMigration.BodyMaxLength)));

            int fromVersion, pruned;
            JObject root = Upgrade(json, out fromVersion, out pruned);

            Assert.Equal(1, fromVersion);
            Assert.Equal(2, pruned);

            // The survivor is the one exactly on both limits: maxLength is inclusive, and an
            // off-by-one here would quietly drop every article a compliant model writes to length.
            var articles = (JArray)root["articles"]!;
            Assert.Equal("article-ok", (string)Assert.Single(articles)["id"]!);
        }

        [Fact]
        public void Upgrade_KeepsPartyFlavorWhenEveryArticleIsDropped()
        {
            // The W2 regression guard. Every article goes; the party names must not go with them.
            string json = Cache(PreviousVersion, string.Join(",",
                Article("article-a", 400, 100),
                Article("article-b", 40, 4000)));

            JToken before = FlavorJsonReader.ParseObject(json)!["partyFlavor"]!;

            int fromVersion, pruned;
            JObject root = Upgrade(json, out fromVersion, out pruned);

            Assert.Equal(2, pruned);
            Assert.Empty((JArray)root["articles"]!);
            Assert.True(JToken.DeepEquals(before, root["partyFlavor"]),
                        "party flavor must survive a cache upgrade untouched - it is every party's name.");
            Assert.Equal("1997-06-01", (string)root["generatedAtSimDate"]!);
        }

        [Fact]
        public void Upgrade_StampsSchemaVersionTwo()
        {
            string json = Cache(PreviousVersion, Article("article-ok", 40, 100));

            int fromVersion, pruned;
            JObject root = Upgrade(json, out fromVersion, out pruned);

            Assert.Equal(0, pruned);
            Assert.Equal(FlavorSchema.SupportedSchemaVersion, (int)root["schemaVersion"]!);
        }

        [Fact]
        public void Upgrade_NeverTruncates()
        {
            // A body one character inside the limit comes back character-identical. Cutting at the
            // limit would end a sentence mid-word and publish it to the player as written prose.
            string body = Repeat('b', FlavorCacheMigration.BodyMaxLength - 1);
            string json = Cache(PreviousVersion,
                                Article("article-ok", 40, FlavorCacheMigration.BodyMaxLength - 1));

            int fromVersion, pruned;
            JObject root = Upgrade(json, out fromVersion, out pruned);

            Assert.Equal(body, (string)Assert.Single((JArray)root["articles"]!)["body"]!);
        }

        [Fact]
        public void Upgrade_LeavesACurrentDocumentUntouched()
        {
            string json = Cache(FlavorSchema.SupportedSchemaVersion.ToString(CultureInfo.InvariantCulture),
                                Article("article-ok", 40, 100));

            int fromVersion, pruned;
            string migrated = FlavorCacheMigration.UpgradeToCurrent(json, null, out fromVersion, out pruned);

            Assert.Equal(FlavorSchema.SupportedSchemaVersion, fromVersion);
            Assert.Equal(0, pruned);
            Assert.Equal(json, migrated);
        }

        [Fact]
        public void Upgrade_LeavesEventProseAlone()
        {
            // eventProse.localAngle still allows 900 characters. Sweeping it with the article limits
            // would silently delete every event's local colour on the first reload after the update.
            string localAngle = Repeat('e', 800);
            string json = Cache(PreviousVersion, Article("article-ok", 40, 100),
                @"""eventProse"": [ { ""eventId"": ""event-harbour-flood"", ""localAngle"": """ +
                localAngle + @""" } ]");

            int fromVersion, pruned;
            JObject root = Upgrade(json, out fromVersion, out pruned);

            Assert.Equal(0, pruned);
            Assert.Equal(localAngle, (string)Assert.Single((JArray)root["eventProse"]!)["localAngle"]!);
        }
    }
}
