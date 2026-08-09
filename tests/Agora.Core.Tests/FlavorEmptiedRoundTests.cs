// Requires the FlavorValidator.cs / FlavorCache.cs / FlavorDocument.cs <Compile Link> lines in
// Agora.Core.Tests.csproj (see the comment there for why).

using System.Globalization;
using System.IO;
using System.Text;
using Agora.Core.Contracts;
using Agora.Mod.Llm;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Non-negotiable #7, at the one place the refs check can turn it inside out.
    ///
    /// <para>
    /// <see cref="FlavorValidator"/> drops an article whose three ref fields are all empty, and that
    /// drop is per-entry rather than fatal — which is right until it takes the last article in the
    /// round. At that point a response that <i>validates</i> carries no prose, and the two things
    /// that hold a last-good document would each install it: the CLI provider would publish it and
    /// write it to <c>flavor_cache.json</c>, and the cache load would restore it over nothing. A
    /// model that simply omitted <c>refs</c> — the likeliest deviation there is, which is why the
    /// prompt has to spell the rule out — would then have destroyed good prose permanently, which is
    /// the exact opposite of "keep last good flavor, log, continue".
    /// </para>
    ///
    /// <para>
    /// So the emptied round is reported as
    /// <see cref="FlavorValidationResult.ArticlesAllDiscarded"/> and both holders treat it as a
    /// failed round. Two things it is deliberately not: a <i>partial</i> drop, which is thin prose
    /// and thin prose beats none; and a round that carried no articles to begin with, which is the
    /// correct output for a save with nothing to write about.
    /// </para>
    /// </summary>
    public class FlavorEmptiedRoundTests
    {
        private static readonly SimDate RequestDate = new SimDate(1997, 6, 1);

        private static FlavorCatalog Catalog() => new FlavorCatalog(
            new[] { "party-riverside" },
            new string[0],
            new[] { "district-harbour" },
            new string[0]);

        private static FlavorValidator Validator() =>
            new FlavorValidator(FlavorSchema.Load(null, null), null);

        /// <summary>An article with refs, and therefore one that survives the filter.</summary>
        private static string Kept(string id) =>
            @"{ ""id"": """ + id + @""", ""outlet"": ""Harbour Register"",
                ""headline"": ""Tram money finds the wharf"",
                ""body"": ""The slate spent the morning explaining a bridge."", ""tone"": ""neutral"",
                ""refs"": { ""districtId"": ""district-harbour"" } }";

        /// <summary>
        /// An article with no refs at all — the shape every city-branch article had before the check
        /// existed, and the shape a model that skipped rule 2 produces.
        /// </summary>
        private static string Refless(string id) =>
            @"{ ""id"": """ + id + @""", ""outlet"": ""Harbour Register"",
                ""headline"": ""Council adjourns early"",
                ""body"": ""Nobody could agree on the agenda."", ""tone"": ""neutral"" }";

        private static string Document(params string[] articles)
        {
            var sb = new StringBuilder();
            sb.Append(@"{ ""schemaVersion"": ")
              .Append(FlavorSchema.SupportedSchemaVersion.ToString(CultureInfo.InvariantCulture))
              .Append(@", ""generatedAtSimDate"": ""1997-06-01"",
                          ""partyFlavor"": [ { ""partyId"": ""party-riverside"",
                                               ""name"": ""Riverside Slate"" } ]");

            if (articles.Length > 0)
            {
                sb.Append(@", ""articles"": [");
                for (int i = 0; i < articles.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(articles[i]);
                }
                sb.Append(']');
            }

            sb.Append('}');
            return sb.ToString();
        }

        // --- the signal itself --------------------------------------------------------------------

        [Fact]
        public void ARoundWhoseEveryArticleIsReflessIsReportedAsEmptied()
        {
            FlavorValidationResult result = Validator().Validate(
                Document(Refless("article-01"), Refless("article-02")), Catalog(), RequestDate);

            // Still valid: the document is well formed and its party names are worth having. The
            // caller is the one that decides an article-less round is not worth publishing.
            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            Assert.Empty(result.Document!.Articles);

            Assert.Equal(2, result.ArticlesReceived);
            Assert.True(result.ArticlesAllDiscarded);
        }

        [Fact]
        public void APartialDropIsDegradedRatherThanEmptied()
        {
            // Eight in and one out is thin prose, and thin prose beats none. Rejecting this would
            // throw away seven good articles' worth of company for the sake of the eighth.
            FlavorValidationResult result = Validator().Validate(
                Document(Refless("article-01"), Refless("article-02"), Kept("article-03")),
                Catalog(), RequestDate);

            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            Assert.Equal("article-03", Assert.Single(result.Document!.Articles).Id);

            Assert.Equal(3, result.ArticlesReceived);
            Assert.False(result.ArticlesAllDiscarded);
        }

        [Fact]
        public void ARoundThatCarriedNoArticlesAtAllIsNotAFailure()
        {
            // Zero in, zero out. The canned pool files exactly this on a save with no parties and no
            // districts, and a model asked for party names alone could too. Nothing was lost, so
            // nothing failed — conflating the two would reject the correct output for an early save.
            FlavorValidationResult result = Validator().Validate(Document(), Catalog(), RequestDate);

            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            Assert.Empty(result.Document!.Articles);

            Assert.Equal(0, result.ArticlesReceived);
            Assert.False(result.ArticlesAllDiscarded);
        }

        [Fact]
        public void AFailedValidationReportsNoArticlesReceived()
        {
            // ArticlesReceived is counted after the schema and the sweep have passed, so a rejected
            // response leaves it at zero and ArticlesAllDiscarded false. That matters because the CLI
            // provider tests the two conditions in sequence: a document-less result must not also
            // claim to have emptied a round.
            FlavorValidationResult result = Validator().Validate(
                @"{ ""schemaVersion"": " + FlavorSchema.SupportedSchemaVersion.ToString(CultureInfo.InvariantCulture) +
                @", ""generatedAtSimDate"": ""1997-06-01"",
                    ""partyFlavor"": [ { ""partyId"": ""party-riverside"", ""expectedSwing"": 4.2 } ] }",
                Catalog(), RequestDate);

            Assert.False(result.IsValid);
            Assert.Equal(0, result.ArticlesReceived);
            Assert.False(result.ArticlesAllDiscarded);
        }

        // --- the cache load path --------------------------------------------------------------------

        [Fact]
        public void ACachedRoundThatEmptiesDoesNotLoadAsAnEmptyLastGood()
        {
            // Every flavor_cache.json written before the refs check holds city-branch articles with
            // no refs, so this is not a hypothetical: it is what the first load after that change
            // finds on an existing save. Null means "no last good yet", which puts the canned pool in
            // front of the player until the next wake — degraded, and recoverable. Returning the
            // emptied document instead would install prose-less last-good that survives every reload.
            string directory = TempRoot("cached-round-emptied");
            var cache = new FileFlavorCache(directory, Validator(), Catalog(), null);
            Write(directory, Document(Refless("article-01"), Refless("article-02")));

            Assert.Null(cache.Load());
        }

        [Fact]
        public void ACachedRoundThatOnlyPartlyEmptiesStillLoads()
        {
            // The other side of the same branch, and the reason it is a branch rather than a blanket
            // "any discard is fatal": a cache that loses one article to a dissolved party still
            // carries every party name, and party names are the load-bearing content of the file.
            string directory = TempRoot("cached-round-partly-emptied");
            var cache = new FileFlavorCache(directory, Validator(), Catalog(), null);
            Write(directory, Document(Refless("article-01"), Kept("article-02")));

            FlavorDocument loaded = cache.Load();

            Assert.NotNull(loaded);
            Assert.Equal("article-02", Assert.Single(loaded!.Articles).Id);
            Assert.Equal("Riverside Slate", Assert.Single(loaded.PartyFlavor).Name);
        }

        [Fact]
        public void ACachedRoundWithNoArticlesStillLoadsForItsPartyNames()
        {
            // Zero in, zero out on the load path too. A cache written for a save that had nothing to
            // report is not a damaged cache, and discarding it would cost the player their party
            // names for no reason at all.
            string directory = TempRoot("cached-round-articleless");
            var cache = new FileFlavorCache(directory, Validator(), Catalog(), null);
            Write(directory, Document());

            FlavorDocument loaded = cache.Load();

            Assert.NotNull(loaded);
            Assert.Empty(loaded!.Articles);
            Assert.Equal("Riverside Slate", Assert.Single(loaded.PartyFlavor).Name);
        }

        [Fact]
        public void AnEmptiedRoundIsNeverWrittenBackToTheCache()
        {
            // The write half, asserted through the only route the test suite can reach: the provider
            // that would call Save is game-facing, so what is pinned here is that a cache which never
            // received the emptied round hands back the good one it already had. The guard that keeps
            // Save from being called at all lives in ClaudeCliProvider.GenerateWithRetry and is a
            // manual-gate item.
            string directory = TempRoot("cache-keeps-the-good-round");
            var cache = new FileFlavorCache(directory, Validator(), Catalog(), null);

            string good = Document(Kept("article-01"));
            FlavorValidationResult accepted = Validator().Validate(good, Catalog(), RequestDate);
            Assert.True(accepted.IsValid, string.Join("; ", accepted.Errors));
            cache.Save(accepted.Document!, good);

            FlavorValidationResult emptied = Validator().Validate(
                Document(Refless("article-02")), Catalog(), RequestDate);
            Assert.True(emptied.ArticlesAllDiscarded);

            FlavorDocument loaded = cache.Load();
            Assert.NotNull(loaded);
            Assert.Equal("article-01", Assert.Single(loaded!.Articles).Id);
        }

        // --- fixtures ----------------------------------------------------------------------------

        private static void Write(string directory, string json) =>
            File.WriteAllText(Path.Combine(directory, FileFlavorCache.FileName), json,
                              new UTF8Encoding(false));

        private static string TempRoot(string name)
        {
            string path = Path.Combine(Path.GetTempPath(), "agora-flavor-emptied-round-tests", name);
            Delete(path);
            Directory.CreateDirectory(path);
            return path;
        }

        private static void Delete(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a test over.
            }
        }
    }
}
