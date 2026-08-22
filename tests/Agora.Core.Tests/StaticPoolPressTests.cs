// Requires the StaticPoolContent.cs / StaticPoolProvider.cs / FlavorValidator.cs <Compile Link>
// lines in Agora.Core.Tests.csproj (see the comment there for why).

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Agora.Core.Contracts;
using Agora.Core.Stories;
using Agora.Core.Stories.Catalog;
using Agora.Mod.Llm;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The canned press pool, held to the rules the prompt imposes on the model.
    ///
    /// <para>
    /// The pool is the fallback for the fallback: it is what a player with no Claude CLI reads all
    /// game, and it is what everyone reads for the first month of every save. Nothing was checking
    /// its prose. Two of its articles in every round carried no <c>refs</c> at all, which is why the
    /// prompt's refs rule had to be worded softly, and its headline truncator cut a district name
    /// mid-word and then cut the template's own last word off behind it.
    /// </para>
    ///
    /// <para>
    /// The template-level tests below assert over <see cref="StaticPoolContent"/>'s arrays rather
    /// than over generated output, so a future author adding a template that breaks a rule fails the
    /// build rather than shipping one bad article in one seed nobody rolls.
    /// </para>
    ///
    /// <para>
    /// <b>What the pool writes changed in wave 7: the ballot, not the month.</b> General monthly
    /// coverage — the city piece and the district piece — existed to fill the news feed, and v10 of
    /// <c>docs/contracts/ui_bindings.md</c> retired the feed. So an ordinary month now files no
    /// articles at all, which is a smaller round and not a failed one, and an election month files
    /// exactly the dedicated pieces: three under NA rules and four under EU. The tests that used to
    /// prove the city and district branches are gone with the branches; what replaced them is the
    /// pair that proves an ordinary month is silent and an election month is not.
    /// </para>
    /// </summary>
    public class StaticPoolPressTests
    {
        private static readonly Guid Save = new Guid("9a4f10d2-0000-4000-8000-abcdefabcdef");

        /// <summary>A second city, for the draws that must not be the same in both.</summary>
        private static readonly Guid OtherSave = new Guid("1c7b3e55-0000-4000-8000-abcdefabcdef");
        private static readonly SimDate Founded = new SimDate(2018, 4, 1);
        private static readonly SimDate Date = new SimDate(2031, 5, 1);

        private static StaticPoolProvider Pool(RegionTheme theme) => Pool(Save, theme);

        private static StaticPoolProvider Pool(Guid saveGuid, RegionTheme theme) =>
            new StaticPoolProvider(saveGuid, theme,
                                   FlavorValidator.Create(null, NullFlavorLog.Instance),
                                   NullFlavorLog.Instance);

        // ---- what a round is for -------------------------------------------------------------------

        [Theory]
        [InlineData(FlavorWakeReason.Yearly)]
        [InlineData(FlavorWakeReason.Manual)]
        [InlineData(FlavorWakeReason.StoryDraft)]
        public void AnOrdinaryMonthFilesNoArticlesAtAll(FlavorWakeReason reason)
        {
            // The whole of this lane, in one assertion. The four articles an ordinary month used to
            // file were general coverage of the city, written for the news feed; the feed is gone, so
            // that prose reached no reader. A full roster and a city full of districts is deliberately
            // handed in: the round is silent because there is no occasion, not because there is
            // nothing to write about.
            for (int month = 1; month <= 12; month++)
            {
                FlavorRequest request = Request(new SimDate(2031, month, 1), reason, RegionTheme.Eu,
                                                parties: 4, districts: 5);

                FlavorDocument document = Pool(RegionTheme.Eu).Generate(request);

                Assert.NotNull(document);
                Assert.Empty(document.Articles);

                // And the round is not thereby empty: the parts of it a surface still renders are
                // untouched, which is what makes a silent month a smaller round rather than a lost one.
                Assert.Equal(4, document.PartyFlavor.Count);
            }
        }

        [Theory]
        [InlineData(RegionTheme.Eu, FlavorRequest.ElectionArticleCountEu)]
        [InlineData(RegionTheme.Na, FlavorRequest.ElectionArticleCountNa)]
        public void AnElectionMonthFilesExactlyTheDedicatedPieces(RegionTheme theme, int expected)
        {
            // The count is the pieces, not a number carried over from the old round: three under NA
            // rules — result, claim, challenge — and four under EU, which adds the coalition outlook.
            // Several seeds, because the party each piece lands on is drawn.
            for (int month = 1; month <= 12; month++)
            {
                var date = new SimDate(2031, month, 1);
                FlavorRequest request = Request(date, FlavorWakeReason.Election, theme,
                                                parties: 3, districts: 4);

                FlavorDocument document = Pool(theme).Generate(request);
                Assert.NotNull(document);
                Assert.Equal(expected, document.Articles.Count);

                for (int i = 0; i < document.Articles.Count; i++)
                {
                    ArticleEntry article = document.Articles[i];
                    Assert.False(string.IsNullOrEmpty(article.PartyId),
                                 "article " + article.Id + " in " + date + " points at nothing.");

                    // Districts are in the snapshot and reach no article: the district piece went with
                    // the city piece, and a ref this pool cannot name in its prose is one it must not
                    // write.
                    Assert.Equal(string.Empty, article.DistrictId);
                }
            }
        }

        [Fact]
        public void TheElectionCountIsTheNumberOfDistinctPiecesTheRoundFiles()
        {
            // The derivation itself, asserted rather than asserted about: whatever
            // ElectionArticleCount says, the round has to be that many *different* pieces. A count
            // raised without a piece to spend it on would show up here as a short list of pools drawn
            // from, and a piece added without raising the count as a short round.
            foreach (RegionTheme theme in new[] { RegionTheme.Eu, RegionTheme.Na })
            {
                FlavorRequest request = Request(Date, FlavorWakeReason.Election, theme,
                                                parties: 3, districts: 4);

                FlavorDocument document = Pool(theme).Generate(request);
                Assert.NotNull(document);

                int pieces = 0;
                foreach (string[] pool in ElectionHeadlinePools())
                {
                    if (DrawnFrom(document, pool)) pieces++;
                }

                Assert.Equal(FlavorRequest.ElectionArticleCount(theme), pieces);
                Assert.Equal(FlavorRequest.ElectionArticleCount(theme), document.Articles.Count);
            }
        }

        [Fact]
        public void TheRosterCopysCeilingStillBuysAWholeEuElectionRound()
        {
            // FlavorRequest.RosterCopy hands the pool DefaultArticleCount, and the pool is the only
            // writer on the no-CLI path — so a ceiling below the EU set would cut the coalition piece
            // off the one round that most needs it, silently and only for players without the CLI.
            Assert.True(FlavorRequest.DefaultArticleCount >= FlavorRequest.ElectionArticleCountEu,
                        "the roster's ceiling has fallen below the EU election set.");

            FlavorRequest request = Request(Date, FlavorWakeReason.Election, RegionTheme.Eu,
                                            parties: 3, districts: 4);
            request.ArticleCount = FlavorRequest.DefaultArticleCount;

            FlavorDocument document = Pool(RegionTheme.Eu).Generate(request);
            Assert.NotNull(document);

            Assert.True(DrawnFrom(document, StaticPoolContent.ElectionCoalitionHeadlines),
                        "the coalition piece did not fit inside the roster copy's ceiling.");
        }

        [Fact]
        public void NoPartiesAndNoDistricts_FilesNoArticlesAtAll()
        {
            // The very early save. There is no id an article could honestly reference, so the correct
            // round is an empty one — not a round of prose about nobody, which is what the pool used
            // to file and what the validator would now silently drop anyway.
            FlavorRequest request = Request(Date, FlavorWakeReason.Yearly, RegionTheme.Eu,
                                            parties: 0, districts: 0);

            FlavorDocument document = Pool(RegionTheme.Eu).Generate(request);

            Assert.NotNull(document);
            Assert.Empty(document.Articles);
        }

        [Fact]
        public void AnArticleNamesTheVeryPartyItRefs()
        {
            // The ref is only checkable by a reader if the prose names the same party the id points
            // at, which means the article has to use the name this document gave that party rather
            // than a fresh draw.
            FlavorRequest request = Request(Date, FlavorWakeReason.Election, RegionTheme.Eu,
                                            parties: 3, districts: 0);

            FlavorDocument document = Pool(RegionTheme.Eu).Generate(request);
            Assert.NotNull(document);

            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < document.PartyFlavor.Count; i++)
            {
                names[document.PartyFlavor[i].PartyId] = document.PartyFlavor[i].Name;
            }

            for (int i = 0; i < document.Articles.Count; i++)
            {
                ArticleEntry article = document.Articles[i];
                string name = names[article.PartyId];
                Assert.True(article.Headline.Contains(name) || article.Body.Contains(name),
                            "article " + article.Id + " refs " + article.PartyId +
                            " but never names it (" + name + ").");
            }
        }

        // ---- the election round ---------------------------------------------------------------------

        [Fact]
        public void ElectionUnderEu_FilesTheCoalitionPiece()
        {
            FlavorRequest request = Request(Date, FlavorWakeReason.Election, RegionTheme.Eu,
                                            parties: 3, districts: 4);

            FlavorDocument document = Pool(RegionTheme.Eu).Generate(request);
            Assert.NotNull(document);

            Assert.True(DrawnFrom(document, StaticPoolContent.ElectionResultHeadlines));
            Assert.True(DrawnFrom(document, StaticPoolContent.ElectionClaimHeadlines));
            Assert.True(DrawnFrom(document, StaticPoolContent.ElectionChallengeHeadlines));
            Assert.True(DrawnFrom(document, StaticPoolContent.ElectionCoalitionHeadlines));
        }

        [Fact]
        public void ElectionUnderNa_FilesTheOtherThreeAndNoCoalitionPiece()
        {
            // There are no coalitions to have an outlook on under first-past-the-post wards with a
            // directly elected mayor — the same reason FlavorPromptBuilder withholds the piece from an
            // NA prompt. A canned round that filed one anyway would be inventing politics the save
            // does not have.
            FlavorRequest request = Request(Date, FlavorWakeReason.Election, RegionTheme.Na,
                                            parties: 3, districts: 4);

            FlavorDocument document = Pool(RegionTheme.Na).Generate(request);
            Assert.NotNull(document);

            Assert.True(DrawnFrom(document, StaticPoolContent.ElectionResultHeadlines));
            Assert.True(DrawnFrom(document, StaticPoolContent.ElectionClaimHeadlines));
            Assert.True(DrawnFrom(document, StaticPoolContent.ElectionChallengeHeadlines));
            Assert.False(DrawnFrom(document, StaticPoolContent.ElectionCoalitionHeadlines));
        }

        [Fact]
        public void AYearlyRoundFilesNoElectionCoverage()
        {
            // The ceiling is set as high as anything ever sets it, and buys nothing: the round's
            // occasion decides what it files, and a yearly round has no election to cover.
            FlavorRequest request = Request(Date, FlavorWakeReason.Yearly, RegionTheme.Eu,
                                            parties: 3, districts: 4);
            request.ArticleCount = FlavorRequest.ElectionArticleCountEu;

            FlavorDocument document = Pool(RegionTheme.Eu).Generate(request);
            Assert.NotNull(document);

            Assert.False(DrawnFrom(document, StaticPoolContent.ElectionResultHeadlines));
            Assert.False(DrawnFrom(document, StaticPoolContent.ElectionClaimHeadlines));
            Assert.False(DrawnFrom(document, StaticPoolContent.ElectionChallengeHeadlines));
            Assert.False(DrawnFrom(document, StaticPoolContent.ElectionCoalitionHeadlines));
            Assert.Empty(document.Articles);
        }

        [Fact]
        public void AnElectionWithNoPartiesFilesNothingRatherThanSomethingElse()
        {
            // Every election piece names a party, so a roster that failed to build cannot have one.
            // Filing nothing is the fail-closed answer (non-negotiable #7); inventing a subject for
            // the result piece is not, and the district pieces this used to fall back to no longer
            // exist to fall back to.
            FlavorRequest request = Request(Date, FlavorWakeReason.Election, RegionTheme.Eu,
                                            parties: 0, districts: 4);

            FlavorDocument document = Pool(RegionTheme.Eu).Generate(request);
            Assert.NotNull(document);
            Assert.Empty(document.Articles);
        }

        // ---- length -------------------------------------------------------------------------------
        //
        // Two tests went with the district piece, and what they proved is worth recording because
        // nothing replaces it: they drove a player-typed district name past the headline cap and then
        // past the body cap, and pinned that StaticPoolProvider.Fitting drops the placeholder and
        // takes a whole generic line rather than cutting the name mid-word. No template left
        // substitutes a string the player controls — a party's prose name is drawn from
        // StaticPoolContent's own pools, whose worst case is computable and pinned below — so the
        // fallback is now reachable only if an authored pool grows past a cap. The floor under it is
        // still asserted, and it is still the reason a cut line can never be published.

        [Fact]
        public void EveryHeadlineAndBodyAcrossManyRoundsComesInWholeAndInsideItsCap()
        {
            foreach (RegionTheme theme in new[] { RegionTheme.Eu, RegionTheme.Na })
            {
                foreach (FlavorWakeReason reason in new[] { FlavorWakeReason.Yearly, FlavorWakeReason.Election })
                {
                    for (int month = 1; month <= 12; month++)
                    {
                        FlavorRequest request = Request(new SimDate(2031, month, 1), reason, theme,
                                                        parties: 4, districts: 5);
                        request.ArticleCount = FlavorRequest.ElectionArticleCount(theme);

                        FlavorDocument document = Pool(theme).Generate(request);
                        Assert.NotNull(document);

                        for (int i = 0; i < document.Articles.Count; i++)
                        {
                            ArticleEntry article = document.Articles[i];
                            Assert.True(article.Headline.Length <= FlavorCacheMigration.HeadlineMaxLength,
                                        "headline over the cap: " + article.Headline);
                            Assert.True(article.Body.Length <= FlavorCacheMigration.BodyMaxLength,
                                        "body over the cap: " + article.Body);
                            Assert.False(article.Headline.EndsWith(" ", StringComparison.Ordinal));
                        }
                    }
                }
            }
        }

        [Fact]
        public void EveryPartyTemplateFitsItsCapUnderTheLongestNameThePoolCanBuild()
        {
            // Party names are ours, not the player's, so unlike district names their worst case is
            // computable — and a template that only fits the short ones is a bug that surfaces on one
            // save in ten. The mood word goes in at its longest too, including the "no snapshot"
            // wording, which is the longest of the lot.
            string party = LongestPartyName();
            string mood = LongestMoodWord();

            foreach (string[] pool in PartyHeadlinePools())
            {
                AssertPoolFits(pool, FlavorCacheMigration.HeadlineMaxLength, party, mood);
            }
            foreach (string[] pool in PartyBodyPools())
            {
                AssertPoolFits(pool, FlavorCacheMigration.BodyMaxLength, party, mood);
            }
        }

        [Fact]
        public void TheGenericPoolFitsBothCapsWithNothingSubstituted()
        {
            // The floor under the whole arrangement. StaticPoolProvider.Fitting falls back here when a
            // substituted line cannot fit, and only if these fit is its last-resort word-boundary trim
            // unreachable — which is what lets the pool promise it never publishes a cut sentence.
            for (int i = 0; i < StaticPoolContent.GenericHeadlines.Length; i++)
            {
                Assert.True(StaticPoolContent.GenericHeadlines[i].Length <= FlavorCacheMigration.HeadlineMaxLength,
                            "generic headline over the cap: " + StaticPoolContent.GenericHeadlines[i]);
            }
            for (int i = 0; i < StaticPoolContent.GenericBodies.Length; i++)
            {
                Assert.True(StaticPoolContent.GenericBodies[i].Length <= FlavorCacheMigration.BodyMaxLength,
                            "generic body over the cap: " + StaticPoolContent.GenericBodies[i]);
            }
        }

        // ---- the prose rules, asserted over the arrays themselves --------------------------------

        /// <summary>
        /// Unattributed sourcing, as the prompt's rule 3 enumerates it and then some. A collective
        /// noun plus a verb of speech or opinion is a claim with nobody behind it, and the pool used
        /// to open on two of them.
        /// </summary>
        private static readonly Regex UnattributedSourcing = new Regex(
            @"\b(residents?|officials?|critics?|sources?|observers?|analysts?|locals?|neighbours?|" +
            @"people|some|many|others|everyone|nobody)\s+" +
            @"(say|says|said|argue|argues|argued|feel|feels|felt|describe|describes|described|" +
            @"point|points|pointed|report|reports|reported|insist|insists|complain|complains|" +
            @"claim|claims|believe|believes|think|thinks|note|notes|reckon|reckons)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        [Fact]
        public void NoTemplateSourcesAnythingToNobody()
        {
            foreach (KeyValuePair<string, string[]> pool in AllPressPools())
            {
                for (int i = 0; i < pool.Value.Length; i++)
                {
                    Match match = UnattributedSourcing.Match(pool.Value[i]);
                    Assert.False(match.Success,
                                 pool.Key + "[" + i + "] attributes to nobody (\"" + match.Value +
                                 "\"). Name the party or the district the article refs, or do not " +
                                 "attribute at all: " + pool.Value[i]);
                }
            }
        }

        [Fact]
        public void NoTemplateStatesAFigure()
        {
            // The dashboard carries the figures and the prose does not. A digit in a template is the
            // cheap tell — the numeric sweep cannot see one, because it is inside a string by then.
            foreach (KeyValuePair<string, string[]> pool in AllPressPools())
            {
                for (int i = 0; i < pool.Value.Length; i++)
                {
                    Assert.DoesNotMatch(@"\d", pool.Value[i]);
                }
            }
        }

        [Fact]
        public void EveryTemplateNamesTheSubjectItsArticleWillRef()
        {
            // The rule that makes every ref checkable. Every piece refs a party and must name it.
            // The generic pool is the one exception and carries no placeholder at all, by
            // construction — it exists precisely for the case where the name will not fit.
            foreach (string[] pool in PartyHeadlinePools()) AssertEachContains(pool, "{party}");
            foreach (string[] pool in PartyBodyPools()) AssertEachContains(pool, "{party}");

            AssertEachContainsNoPlaceholder(StaticPoolContent.GenericHeadlines);
            AssertEachContainsNoPlaceholder(StaticPoolContent.GenericBodies);
        }

        [Fact]
        public void NoRoundFilesTheSameHeadlineOrBodyTwice()
        {
            // What "room for the de-duplication retry" is actually for. The old form of this test
            // asserted pool.Value.Length >= 3 against a round that can ask for eight, which a pool
            // that repeated constantly would sail through — reassurance rather than a check. The
            // property worth protecting is the observable one, so it is asserted on generated rounds
            // at the largest count anything asks for, across both themes and a year of seeds.
            //
            // A repeat is only unavoidable when one pool has to fill more slots than it has lines;
            // UniqueLine's bounded retry then gives up and allows one. PlanRound gives each election
            // piece its own pool, so no pool is asked for more than one line and no repeat is
            // legitimate — outlets are the one pool a round draws several times from.
            foreach (RegionTheme theme in new[] { RegionTheme.Eu, RegionTheme.Na })
            {
                foreach (FlavorWakeReason reason in new[] { FlavorWakeReason.Yearly, FlavorWakeReason.Election })
                {
                    for (int month = 1; month <= 12; month++)
                    {
                        FlavorRequest request = Request(new SimDate(2031, month, 1), reason, theme,
                                                        parties: 4, districts: 5);
                        request.ArticleCount = FlavorRequest.ElectionArticleCount(theme);

                        FlavorDocument document = Pool(theme).Generate(request);
                        Assert.NotNull(document);
                        Assert.Equal(reason == FlavorWakeReason.Election ? request.ArticleCount : 0,
                                     document.Articles.Count);

                        var headlines = new HashSet<string>(StringComparer.Ordinal);
                        var bodies = new HashSet<string>(StringComparer.Ordinal);
                        var outlets = new HashSet<string>(StringComparer.Ordinal);

                        for (int i = 0; i < document.Articles.Count; i++)
                        {
                            ArticleEntry article = document.Articles[i];
                            string where = theme + "/" + reason + "/" + month + ": ";

                            Assert.True(headlines.Add(article.Headline), where + "repeated headline: " + article.Headline);
                            Assert.True(bodies.Add(article.Body), where + "repeated body: " + article.Body);
                            Assert.True(outlets.Add(article.Outlet), where + "repeated outlet: " + article.Outlet);
                        }
                    }
                }
            }
        }

        // ---- the two reaction pieces --------------------------------------------------------------

        [Fact]
        public void TheChallengePieceIsNotTheSamePartyAsTheClaimPiece()
        {
            // Two independent draws over three parties put one party on both sides of the argument
            // about one round in three: the same slate claiming the mandate and challenging the count
            // in the same morning's press, which reads as a bug rather than as politics. The
            // challenge draw now skips whoever the claim landed on.
            foreach (RegionTheme theme in new[] { RegionTheme.Eu, RegionTheme.Na })
            {
                for (int month = 1; month <= 12; month++)
                {
                    FlavorRequest request = Request(new SimDate(2031, month, 1), FlavorWakeReason.Election,
                                                    theme, parties: 3, districts: 4);
                    request.ArticleCount = FlavorRequest.ElectionArticleCount(theme);

                    FlavorDocument document = Pool(theme).Generate(request);
                    Assert.NotNull(document);

                    string? claim = PartyBehind(document, StaticPoolContent.ElectionClaimHeadlines);
                    string? challenge = PartyBehind(document, StaticPoolContent.ElectionChallengeHeadlines);

                    Assert.NotNull(claim);
                    Assert.NotNull(challenge);
                    Assert.NotEqual(claim, challenge);
                }
            }
        }

        [Fact]
        public void AOnePartySaveStillFilesBothReactionPieces()
        {
            // The graceful fallback. With nobody else to hand the challenge to, refusing to file it
            // would cost the round an article and leave the election coverage short — worse than one
            // party being quoted twice, which on a one-party council is simply what happened.
            FlavorRequest request = Request(Date, FlavorWakeReason.Election, RegionTheme.Eu,
                                            parties: 1, districts: 4);
            request.ArticleCount = FlavorRequest.ElectionArticleCountEu;

            FlavorDocument document = Pool(RegionTheme.Eu).Generate(request);
            Assert.NotNull(document);
            Assert.Equal(FlavorRequest.ElectionArticleCountEu, document.Articles.Count);

            Assert.Equal("party-00", PartyBehind(document, StaticPoolContent.ElectionClaimHeadlines));
            Assert.Equal("party-00", PartyBehind(document, StaticPoolContent.ElectionChallengeHeadlines));
        }

        // ---- determinism ---------------------------------------------------------------------------

        [Theory]
        [InlineData(FlavorWakeReason.Yearly)]
        [InlineData(FlavorWakeReason.Election)]
        public void TheSameRequestTwiceProducesByteIdenticalProse(FlavorWakeReason reason)
        {
            // The canonical pattern from tests/CLAUDE.md: run it twice and compare serialized output,
            // not field by field. The party draw added in W5-3 rides the article's existing sub-stream
            // and indexes a list sorted by id, so nothing here may depend on Dictionary order.
            FlavorRequest first = Request(Date, reason, RegionTheme.Eu, parties: 5, districts: 6);
            FlavorRequest second = Request(Date, reason, RegionTheme.Eu, parties: 5, districts: 6);

            Assert.Equal(Fingerprint(Pool(RegionTheme.Eu).Generate(first)),
                         Fingerprint(Pool(RegionTheme.Eu).Generate(second)));
        }

        [Fact]
        public void ReorderingTheRosterDoesNotReorderThePress()
        {
            // Same cast, handed in backwards. Everything downstream sorts by id before indexing, so
            // the round must be identical — this is the desync that is stable within a run and
            // different across runs.
            FlavorRequest forwards = Request(Date, FlavorWakeReason.Election, RegionTheme.Eu,
                                             parties: 5, districts: 6);

            FlavorRequest backwards = Request(Date, FlavorWakeReason.Election, RegionTheme.Eu,
                                              parties: 5, districts: 6);
            backwards.Parties.Reverse();
            backwards.Snapshot.Districts.Reverse();

            Assert.Equal(Fingerprint(Pool(RegionTheme.Eu).Generate(forwards)),
                         Fingerprint(Pool(RegionTheme.Eu).Generate(backwards)));
        }

        // The third determinism case here drove a player-typed district name past the headline cap
        // and pinned that the same seed still produced the same round down UniqueLine's rejecting
        // path — the path where Pick has already consumed a NextInt before the candidate is thrown
        // away, so the stream is further on than the number of published lines suggests. It went with
        // the district piece: no template left substitutes a string the player controls, so nothing a
        // save can contain reaches that branch any more. If a piece is ever added that substitutes
        // one, restore it rather than reinventing it.

        // ---- stories and resolutions ----------------------------------------------------------------

        [Fact]
        public void AStoryCardOpensWithItsMajorEventsName()
        {
            // The headline is a transcription, not a draw: the card a player opens says the name of
            // the thing the story is about, and the article walks the slots in the story's own order.
            FlavorRequest request = Request(Date, FlavorWakeReason.StoryDraft, RegionTheme.Eu,
                                            parties: 2, districts: 2);
            request.Stories.Add(Story("story-01", resolved: false));

            FlavorDocument document = PoolWithCatalog().Generate(request);
            Assert.NotNull(document);

            StoryProseEntry entry = Assert.Single(document.Stories);
            Assert.Empty(document.Resolutions);

            Assert.Equal("story-01", entry.StoryId);
            Assert.Equal("The major thing", entry.Headline);

            // Every slot, name then description, in slot order and with nothing else between them.
            Assert.Equal("The major thing. What the major thing is. The minor thing. " +
                         "What the minor thing is.", entry.Article);
        }

        [Fact]
        public void AResolvedStoryIsAResolutionAndSaysHowEachSlotWentOut()
        {
            // met takes the authored success line, not met the authored failure line - the two fields
            // the brief does not carry and CivicEventCatalog.Find is the way back to.
            FlavorRequest request = Request(Date, FlavorWakeReason.Yearly, RegionTheme.Eu,
                                            parties: 2, districts: 2);
            request.Stories.Add(Story("story-01", resolved: true));

            FlavorDocument document = PoolWithCatalog().Generate(request);
            Assert.NotNull(document);

            Assert.Empty(document.Stories);
            StoryProseEntry entry = Assert.Single(document.Resolutions);

            Assert.Equal("story-01", entry.StoryId);
            Assert.Equal("The major thing", entry.Headline);
            Assert.Equal(StaticPoolContent.ResolutionFailureLead +
                         " The major thing. The major thing worked. The minor thing. " +
                         "The minor thing did not.", entry.Article);
        }

        [Theory]
        [InlineData("success", "story ended in success")]
        [InlineData("failure", "story ended in failure")]
        [InlineData("abandoned", "story was abandoned")]
        [InlineData("", "brief arrived resolved with no word on it")]
        public void AResolutionSaysTheStoryClosedEvenWhenNoSlotCanSayHow(string outcomeWord, string because)
        {
            // The three cases where slot text alone cannot tell a closing card from an opening one:
            // an abandoned story leaves every slot Pending and so every slot word empty, an
            // unmeasurable slot has no authored outcome to switch to, and a save whose civic catalog
            // has not reached the pool resolves nothing at all. All three are here at once - no
            // catalog, no slot words - so the only thing that can carry the news is the story's own
            // outcome word.
            FlavorRequest live = Request(Date, FlavorWakeReason.Yearly, RegionTheme.Eu,
                                         parties: 2, districts: 2);
            live.Stories.Add(Story("story-01", resolved: false));

            FlavorRequest closed = Request(Date, FlavorWakeReason.Yearly, RegionTheme.Eu,
                                           parties: 2, districts: 2);
            StoryBrief story = Story("story-01", resolved: true);
            story.OutcomeWord = outcomeWord;
            for (int i = 0; i < story.Slots.Count; i++) story.Slots[i].OutcomeWord = "";
            closed.Stories.Add(story);

            // Pool(), not PoolWithCatalog(): the catalog is unwired, which is every save today.
            FlavorDocument opening = Pool(RegionTheme.Eu).Generate(live);
            FlavorDocument closing = Pool(RegionTheme.Eu).Generate(closed);
            Assert.NotNull(opening);
            Assert.NotNull(closing);

            string opened = Assert.Single(opening.Stories).Article;
            string ended = Assert.Single(closing.Resolutions).Article;

            Assert.True(opened != ended, "the " + because + " reads exactly like the opening card.");
            Assert.StartsWith(ClosingLead(outcomeWord), ended, StringComparison.Ordinal);

            // Everything the opening card said is still there; the lead-in is added in front of it
            // rather than displacing what the story was about.
            Assert.EndsWith(opened, ended, StringComparison.Ordinal);
        }

        [Fact]
        public void TheFourClosingLeadsAreFourDifferentSentences()
        {
            // Otherwise the outcome word is being read and thrown away, which the test above would
            // not notice: it compares a resolution against a live card, not against another outcome.
            var leads = new HashSet<string>(StringComparer.Ordinal);
            foreach (string word in new[] { "success", "failure", "abandoned", "" })
            {
                Assert.True(leads.Add(ClosingLead(word)), "two outcomes share a lead-in: " + word);
            }
        }

        [Fact]
        public void AStoryThatOutlivedItsContentStillGetsAWholeResolution()
        {
            // A data file edited between sessions, or a save opened on a build whose catalog dropped
            // the event: Find answers null, which CivicEventCatalog documents as an ordinary answer.
            // The slot still has its description on the brief, so the card says what the story was
            // rather than how it went - and it is still a card, not a gap.
            FlavorRequest request = Request(Date, FlavorWakeReason.Yearly, RegionTheme.Eu,
                                            parties: 2, districts: 2);
            request.Stories.Add(Story("story-01", resolved: true));

            // The default catalog, which is Empty: nothing is wired in.
            FlavorDocument document = Pool(RegionTheme.Eu).Generate(request);
            Assert.NotNull(document);

            StoryProseEntry entry = Assert.Single(document.Resolutions);
            Assert.Equal("The major thing", entry.Headline);

            // What each slot was, because there is no authored outcome to be had - but still opening
            // on the news that the file is closed, which is the part that needs no catalog.
            Assert.Equal(StaticPoolContent.ResolutionFailureLead +
                         " The major thing. What the major thing is. The minor thing. " +
                         "What the minor thing is.", entry.Article);
        }

        [Fact]
        public void AnUnmeasurableSlotFallsBackToWhatTheStoryWasAbout()
        {
            FlavorRequest request = Request(Date, FlavorWakeReason.Yearly, RegionTheme.Eu,
                                            parties: 2, districts: 2);

            StoryBrief story = Story("story-01", resolved: true);
            story.Slots[1].OutcomeWord = "unmeasurable";
            request.Stories.Add(story);

            FlavorDocument document = PoolWithCatalog().Generate(request);
            Assert.NotNull(document);

            StoryProseEntry entry = Assert.Single(document.Resolutions);
            Assert.Equal(StaticPoolContent.ResolutionFailureLead +
                         " The major thing. The major thing worked. The minor thing. " +
                         "What the minor thing is.", entry.Article);
        }

        [Fact]
        public void ALongStoryDropsADescriptionRatherThanASlot()
        {
            // Three slots of authored description can pass the article cap between them, which is not
            // hypothetical: the shipped NA catalog's longest triples do it. Names are short - the
            // longest shipped one is a fifth of the headline cap - so the card can always say what
            // every slot was and spend the rest of the cap on descriptions in slot order. Dropping a
            // slot outright would lose the event's name too, with nothing on the card to say it
            // happened.
            string filler = SentenceOfLength(FlavorCacheMigration.StoryArticleMaxLength * 3 / 4);

            FlavorRequest request = Request(Date, FlavorWakeReason.Yearly, RegionTheme.Eu,
                                            parties: 2, districts: 2);
            StoryBrief story = Story("story-01", resolved: false);
            for (int i = 0; i < story.Slots.Count; i++) story.Slots[i].HeadlineBrief = filler;
            request.Stories.Add(story);

            FlavorDocument document = PoolWithCatalog().Generate(request);
            Assert.NotNull(document);

            StoryProseEntry entry = Assert.Single(document.Stories);
            Assert.True(entry.Article.Length <= FlavorCacheMigration.StoryArticleMaxLength,
                        "article over the cap: " + entry.Article.Length);

            // Both slots named, in slot order, with the first slot's description carried whole and
            // the second's dropped whole. Nothing cut.
            Assert.Equal("The major thing. " + filler + " The minor thing.", entry.Article);
        }

        [Fact]
        public void AStoryWhoseEventNamesDoNotFitFallsBackToWholeGenericLines()
        {
            // Absurd input rather than shipped content: no authored name comes near either cap, so
            // this is the floor that keeps a bad catalog from putting an over-cap entry through the
            // schema and taking the whole document down with it. Whole lines, not cut ones - the same
            // call Fitting makes for an article whose district name will not fit its headline.
            FlavorRequest request = Request(Date, FlavorWakeReason.Yearly, RegionTheme.Eu,
                                            parties: 2, districts: 2);
            request.Stories.Add(OverflowingStory("story-01", resolved: false));

            FlavorDocument document = PoolWithCatalog().Generate(request);
            Assert.NotNull(document);

            StoryProseEntry entry = Assert.Single(document.Stories);
            Assert.Contains(entry.Headline, StaticPoolContent.StoryHeadlines);
            Assert.Contains(entry.Article, StaticPoolContent.StoryArticles);
        }

        [Fact]
        public void AResolutionTooLongToTranscribeIsStillAWholeClosingLine()
        {
            // A resolution has no generic article pool behind it and does not need one: the lead-in
            // is authored here, always fits, and already says the thing a closing card exists to say.
            FlavorRequest request = Request(Date, FlavorWakeReason.Yearly, RegionTheme.Eu,
                                            parties: 2, districts: 2);
            request.Stories.Add(OverflowingStory("story-01", resolved: true));

            FlavorDocument document = PoolWithCatalog().Generate(request);
            Assert.NotNull(document);

            StoryProseEntry entry = Assert.Single(document.Resolutions);
            Assert.Contains(entry.Headline, StaticPoolContent.ResolutionHeadlines);
            Assert.Equal(StaticPoolContent.ResolutionFailureLead, entry.Article);
        }

        [Fact]
        public void AStorysProseDoesNotChangeWithTheDateItWasRegeneratedFor()
        {
            // The defect this is written against is the one the party name draw already had: a story
            // card is regenerated on every poll for as long as the story is open, and a headline that
            // moved every sim month would change under a player mid-read. The story here overflows
            // both caps, so the drawn fallback - the only draw a story entry makes - is what moves if
            // anything does.
            StoryProseEntry? first = null;

            for (int month = 1; month <= 12; month++)
            {
                var date = new SimDate(2031, month, 1);
                FlavorRequest request = Request(date, FlavorWakeReason.StoryDraft, RegionTheme.Eu,
                                                parties: 2, districts: 2);
                request.Stories.Add(OverflowingStory("story-01", resolved: false));

                FlavorDocument document = PoolWithCatalog().Generate(request);
                Assert.NotNull(document);

                StoryProseEntry entry = Assert.Single(document.Stories);
                if (first == null)
                {
                    first = entry;
                    continue;
                }

                Assert.Equal(first.Headline, entry.Headline);
                Assert.Equal(first.Article, entry.Article);
            }
        }

        [Fact]
        public void StoriesInOneSaveDoNotAllFallBackToTheSameLine()
        {
            // The fallback is drawn per story, on the story's own sub-stream. Asserted across six ids
            // rather than on one pair, because a pair against a three-line pool is a coin flip that a
            // fourth line could flip red for no reason at all.
            List<string> articles = FallbackArticles(Save, 6);

            Assert.True(new HashSet<string>(articles, StringComparer.Ordinal).Count > 1,
                        "six stories in one save all opened on the same line.");
        }

        [Fact]
        public void TwoSavesDoNotDrawTheSameFallbackLines()
        {
            // Structurally guaranteed - the save GUID is the first thing hashed into the seed - and
            // cheap to hold, because the failure it would catch is a stream that quietly stopped
            // reading it and made every city's fallback prose identical.
            Assert.NotEqual(FallbackArticles(Save, 6), FallbackArticles(OtherSave, 6));
        }

        [Fact]
        public void ReorderingTheStoriesDoesNotReorderTheCards()
        {
            FlavorRequest forwards = Request(Date, FlavorWakeReason.StoryDraft, RegionTheme.Eu,
                                             parties: 2, districts: 2);
            FlavorRequest backwards = Request(Date, FlavorWakeReason.StoryDraft, RegionTheme.Eu,
                                              parties: 2, districts: 2);

            foreach (string id in new[] { "story-01", "story-02", "story-03" })
            {
                forwards.Stories.Add(Story(id, resolved: false));
                backwards.Stories.Add(Story(id, resolved: false));
            }
            backwards.Stories.Reverse();

            FlavorDocument a = PoolWithCatalog().Generate(forwards);
            FlavorDocument b = PoolWithCatalog().Generate(backwards);

            Assert.Equal(StoryFingerprint(a), StoryFingerprint(b));
        }

        [Fact]
        public void TheStoryPoolsFitBothCapsWithNothingSubstituted()
        {
            // The floor under the story fallback, as TheGenericPoolFitsBothCapsWithNothingSubstituted
            // is the floor under the article one. Only if these fit is the word-boundary trim behind
            // them unreachable, which is what lets a story card promise it never opens on a cut line.
            foreach (string[] pool in new[] { StaticPoolContent.StoryHeadlines,
                                              StaticPoolContent.ResolutionHeadlines })
            {
                for (int i = 0; i < pool.Length; i++)
                {
                    Assert.True(pool[i].Length <= FlavorCacheMigration.StoryHeadlineMaxLength,
                                "story headline over the cap: " + pool[i]);
                }
            }

            for (int i = 0; i < StaticPoolContent.StoryArticles.Length; i++)
            {
                Assert.True(StaticPoolContent.StoryArticles[i].Length <= FlavorCacheMigration.StoryArticleMaxLength,
                            "story article over the cap: " + StaticPoolContent.StoryArticles[i]);
            }

            // The closing lines are not a floor - a resolution carries one every time - so they have
            // to leave room for the story itself rather than merely fitting the cap on their own.
            foreach (string lead in ClosingLeads())
            {
                Assert.True(lead.Length * 4 <= FlavorCacheMigration.StoryArticleMaxLength,
                            "a closing line is eating the article cap: " + lead);
            }
        }

        // ---- fixtures and helpers -----------------------------------------------------------------

        /// <summary>
        /// Two slots, major first, in the order <c>Story.Slots</c> hands them over. Resolved stories
        /// take one met slot and one not-met one, so a resolution exercises both authored fields.
        /// </summary>
        private static StoryBrief Story(string storyId, bool resolved)
        {
            return new StoryBrief
            {
                StoryId = storyId,
                IsResolved = resolved,
                OutcomeWord = resolved ? "failure" : "",
                Slots =
                {
                    new StorySlotBrief
                    {
                        EventId = "event-major",
                        IsMajor = true,
                        Title = "The major thing",
                        HeadlineBrief = "What the major thing is",
                        OutcomeWord = resolved ? "met" : ""
                    },
                    new StorySlotBrief
                    {
                        EventId = "event-minor",
                        IsMajor = false,
                        Title = "The minor thing",
                        HeadlineBrief = "What the minor thing is",
                        OutcomeWord = resolved ? "not met" : ""
                    }
                }
            };
        }

        /// <summary>
        /// One slot, over both caps on its own: the only input that reaches the generic story pools.
        /// </summary>
        /// <remarks>
        /// Nothing shipped is anywhere near this - the reviewer measured the longest name in the three
        /// catalogs at a fifth of the headline cap - so this is a bad-catalog fixture rather than a
        /// gameplay one. It is here because an entry over the cap fails the schema and takes the whole
        /// document with it, which is a far worse failure than a generic paragraph.
        /// </remarks>
        private static StoryBrief OverflowingStory(string storyId, bool resolved)
        {
            StoryBrief story = Story(storyId, resolved);

            // Unmeasurable, so a resolved slot falls back to its description too: the catalog's
            // authored outcome lines are a couple of sentences each and would fit comfortably.
            story.Slots[0].OutcomeWord = "unmeasurable";
            story.Slots[0].Title = SentenceOfLength(FlavorCacheMigration.StoryArticleMaxLength + 100);
            story.Slots[0].HeadlineBrief = SentenceOfLength(FlavorCacheMigration.StoryArticleMaxLength + 100);
            story.Slots.RemoveAt(1);

            return story;
        }

        /// <summary>
        /// The fallback article each of <paramref name="count"/> overflowing stories draws in
        /// <paramref name="saveGuid"/>, in story order.
        /// </summary>
        private static List<string> FallbackArticles(Guid saveGuid, int count)
        {
            FlavorRequest request = Request(Date, FlavorWakeReason.StoryDraft, RegionTheme.Eu,
                                            parties: 2, districts: 2);
            for (int i = 0; i < count; i++)
            {
                request.Stories.Add(OverflowingStory("story-" + i.ToString("00"), resolved: false));
            }

            FlavorDocument document = Pool(saveGuid, RegionTheme.Eu).Generate(request);
            Assert.NotNull(document);
            Assert.Equal(count, document.Stories.Count);

            var articles = new List<string>();
            for (int i = 0; i < document.Stories.Count; i++)
            {
                string article = document.Stories[i].Article;
                Assert.Contains(article, StaticPoolContent.StoryArticles);
                articles.Add(article);
            }
            return articles;
        }

        /// <summary>The closing line an outcome word selects, as the provider selects it.</summary>
        private static string ClosingLead(string outcomeWord)
        {
            switch (outcomeWord)
            {
                case "success": return StaticPoolContent.ResolutionSuccessLead;
                case "failure": return StaticPoolContent.ResolutionFailureLead;
                case "abandoned": return StaticPoolContent.ResolutionAbandonedLead;
                default: return StaticPoolContent.ResolutionClosedLead;
            }
        }

        private static string[] ClosingLeads() => new[]
        {
            StaticPoolContent.ResolutionSuccessLead,
            StaticPoolContent.ResolutionFailureLead,
            StaticPoolContent.ResolutionAbandonedLead,
            StaticPoolContent.ResolutionClosedLead
        };

        /// <summary>The pool with the two events <see cref="Story"/>'s slots point at.</summary>
        private static StaticPoolProvider PoolWithCatalog()
        {
            StaticPoolProvider pool = Pool(RegionTheme.Eu);
            pool.CivicCatalog = new CivicEventCatalog(
                new List<CivicEvent>
                {
                    new CivicEvent
                    {
                        Id = "event-major",
                        Name = "The major thing",
                        Description = "What the major thing is",
                        SuccessText = "The major thing worked",
                        FailText = "The major thing did not"
                    },
                    new CivicEvent
                    {
                        Id = "event-minor",
                        Name = "The minor thing",
                        Description = "What the minor thing is",
                        SuccessText = "The minor thing worked",
                        FailText = "The minor thing did not"
                    }
                },
                new List<string>());
            return pool;
        }

        /// <summary>
        /// A whole sentence of about <paramref name="length"/> characters, built from words rather
        /// than from a run of one letter so that a cut is distinguishable from a clean drop.
        /// </summary>
        private static string SentenceOfLength(int length)
        {
            var sb = new StringBuilder();
            while (sb.Length < length) sb.Append("the committee reconvened and adjourned again ");
            return sb.ToString().TrimEnd() + ".";
        }

        /// <summary>Every story and resolution the document carries, in one comparable string.</summary>
        private static string StoryFingerprint(FlavorDocument document)
        {
            Assert.NotNull(document);

            var sb = new StringBuilder();
            for (int i = 0; i < document.Stories.Count; i++)
            {
                StoryProseEntry entry = document.Stories[i];
                sb.Append("story|").Append(entry.StoryId).Append('|').Append(entry.Headline)
                  .Append('|').Append(entry.Article).Append('\n');
            }
            for (int i = 0; i < document.Resolutions.Count; i++)
            {
                StoryProseEntry entry = document.Resolutions[i];
                sb.Append("resolution|").Append(entry.StoryId).Append('|').Append(entry.Headline)
                  .Append('|').Append(entry.Article).Append('\n');
            }
            return sb.ToString();
        }


        private static FlavorRequest Request(SimDate date, FlavorWakeReason reason, RegionTheme theme,
                                             int parties, int districts)
        {
            var request = new FlavorRequest
            {
                Date = date,
                Reason = reason,
                Theme = theme,
                ArticleCount = FlavorRequest.DefaultArticleCount,
                Snapshot = Snapshot(date, districts)
            };

            for (int i = 0; i < parties; i++)
            {
                request.Parties.Add(new PartyBrief
                {
                    PartyId = "party-" + i.ToString("00"),
                    ArchetypeId = "archetype-" + i.ToString("00"),
                    CoreGrievance = Issues.All[i % Issues.Count],
                    StatusWord = "Active",
                    FoundedDate = Founded
                });
            }

            return request;
        }

        private static CitySnapshot Snapshot(SimDate date, int districts)
        {
            var snapshot = new CitySnapshot
            {
                Date = date,
                Population = 140_000,
                Happiness = 47.0,
                Districts = new List<DistrictSnapshot>()
            };

            for (int i = 0; i < districts; i++)
            {
                snapshot.Districts.Add(new DistrictSnapshot
                {
                    Id = "district-" + i.ToString("00"),
                    Name = "Marchfield " + (char)('A' + i),
                    Happiness = 38.0 + i,
                    Population = 20_000
                });
            }

            return snapshot;
        }

        /// <summary>Every press pool, by the name the failure message should print.</summary>
        private static IEnumerable<KeyValuePair<string, string[]>> AllPressPools()
        {
            yield return Pair("GenericHeadlines", StaticPoolContent.GenericHeadlines);
            yield return Pair("GenericBodies", StaticPoolContent.GenericBodies);
            yield return Pair("ElectionResultHeadlines", StaticPoolContent.ElectionResultHeadlines);
            yield return Pair("ElectionResultBodies", StaticPoolContent.ElectionResultBodies);
            yield return Pair("ElectionClaimHeadlines", StaticPoolContent.ElectionClaimHeadlines);
            yield return Pair("ElectionClaimBodies", StaticPoolContent.ElectionClaimBodies);
            yield return Pair("ElectionChallengeHeadlines", StaticPoolContent.ElectionChallengeHeadlines);
            yield return Pair("ElectionChallengeBodies", StaticPoolContent.ElectionChallengeBodies);
            yield return Pair("ElectionCoalitionHeadlines", StaticPoolContent.ElectionCoalitionHeadlines);
            yield return Pair("ElectionCoalitionBodies", StaticPoolContent.ElectionCoalitionBodies);
            yield return Pair("EventAngles", StaticPoolContent.EventAngles);
            yield return Pair("StoryHeadlines", StaticPoolContent.StoryHeadlines);
            yield return Pair("StoryArticles", StaticPoolContent.StoryArticles);
            yield return Pair("ResolutionHeadlines", StaticPoolContent.ResolutionHeadlines);
            yield return Pair("ClosingLeads", ClosingLeads());
        }

        private static KeyValuePair<string, string[]> Pair(string name, string[] pool) =>
            new KeyValuePair<string, string[]>(name, pool);

        /// <summary>
        /// Every headline pool an article can be written from. Since wave 7 that is the election set
        /// and nothing else, so this and <see cref="ElectionHeadlinePools"/> are the same list read
        /// two ways: this one asks "does every template name its party", the other "did the round
        /// file every piece". They are kept apart because the first would still be the right question
        /// if a piece for another political occasion were ever added.
        /// </summary>
        private static string[][] PartyHeadlinePools() => new[]
        {
            StaticPoolContent.ElectionResultHeadlines,
            StaticPoolContent.ElectionClaimHeadlines,
            StaticPoolContent.ElectionChallengeHeadlines,
            StaticPoolContent.ElectionCoalitionHeadlines
        };

        private static string[][] PartyBodyPools() => new[]
        {
            StaticPoolContent.ElectionResultBodies,
            StaticPoolContent.ElectionClaimBodies,
            StaticPoolContent.ElectionChallengeBodies,
            StaticPoolContent.ElectionCoalitionBodies
        };

        /// <summary>The four dedicated election pieces, in the order <c>PlanRound</c> files them.</summary>
        private static string[][] ElectionHeadlinePools() => new[]
        {
            StaticPoolContent.ElectionResultHeadlines,
            StaticPoolContent.ElectionClaimHeadlines,
            StaticPoolContent.ElectionChallengeHeadlines,
            StaticPoolContent.ElectionCoalitionHeadlines
        };

        private static void AssertPoolFits(string[] pool, int maxLength, string party, string mood)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                string line = pool[i].Replace("{party}", party).Replace("{mood}", mood);
                Assert.True(line.Length <= maxLength,
                            "over the cap by " + (line.Length - maxLength) + " at the worst name: " + line);
            }
        }

        private static void AssertEachContains(string[] pool, string placeholder)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                Assert.Contains(placeholder, pool[i]);
            }
        }

        private static void AssertEachContainsNoPlaceholder(string[] pool)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                Assert.DoesNotContain("{", pool[i]);
            }
        }

        /// <summary>The longest "Adjective Noun" either theme's pools can produce.</summary>
        private static string LongestPartyName()
        {
            string longest = string.Empty;
            string[][] adjectives = { StaticPoolContent.EuPartyAdjectives, StaticPoolContent.NaPartyAdjectives };
            string[][] nouns = { StaticPoolContent.EuPartyNouns, StaticPoolContent.NaPartyNouns };

            for (int theme = 0; theme < adjectives.Length; theme++)
            {
                for (int a = 0; a < adjectives[theme].Length; a++)
                {
                    for (int n = 0; n < nouns[theme].Length; n++)
                    {
                        string name = adjectives[theme][a] + " " + nouns[theme][n];
                        if (name.Length > longest.Length) longest = name;
                    }
                }
            }

            return longest;
        }

        /// <summary>
        /// The longest word <c>{mood}</c> can be, including the wording the pool uses when there is no
        /// snapshot at all, which is longer than any band.
        /// </summary>
        private static string LongestMoodWord()
        {
            string longest = "hard to read";
            for (int happiness = 0; happiness <= 100; happiness += 5)
            {
                string band = FlavorPromptBuilder.HappinessBand(happiness);
                if (band.Length > longest.Length) longest = band;
            }
            return longest;
        }

        /// <summary>
        /// The party refd by the round's article whose headline came from <paramref name="pool"/>, or
        /// null when no article did. Null rather than an empty string so a missing piece fails the
        /// caller's assertion rather than quietly comparing equal to another missing one.
        /// </summary>
        private static string? PartyBehind(FlavorDocument document, string[] pool)
        {
            for (int i = 0; i < document.Articles.Count; i++)
            {
                if (MatchesSome(document.Articles[i].Headline, pool)) return document.Articles[i].PartyId;
            }
            return null;
        }

        /// <summary>Did any article in the round draw its headline from <paramref name="pool"/>?</summary>
        private static bool DrawnFrom(FlavorDocument document, string[] pool)
        {
            for (int i = 0; i < document.Articles.Count; i++)
            {
                if (MatchesSome(document.Articles[i].Headline, pool)) return true;
            }
            return false;
        }

        /// <summary>
        /// Is <paramref name="line"/> one of <paramref name="pool"/>'s templates with its placeholder
        /// filled? Matched on the fixed text either side of the placeholder rather than on the whole
        /// string, because the substituted value is drawn and the test does not know it. A truncated
        /// line fails the tail half, which is what makes this usable as a "came in whole" check.
        /// </summary>
        private static bool MatchesSome(string line, string[] pool)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                if (Matches(line, pool[i])) return true;
            }
            return false;
        }

        private static bool Matches(string line, string template)
        {
            int open = template.IndexOf('{');
            if (open < 0) return string.Equals(line, template, StringComparison.Ordinal);

            int close = template.IndexOf('}', open);
            if (close < 0) return false;

            string head = template.Substring(0, open);
            string tail = template.Substring(close + 1);

            return line.Length >= head.Length + tail.Length
                && line.StartsWith(head, StringComparison.Ordinal)
                && line.EndsWith(tail, StringComparison.Ordinal);
        }

        /// <summary>Everything the document says, in one comparable string.</summary>
        private static string Fingerprint(FlavorDocument document)
        {
            Assert.NotNull(document);

            var sb = new StringBuilder();
            for (int i = 0; i < document.PartyFlavor.Count; i++)
            {
                sb.Append(document.PartyFlavor[i].PartyId).Append('|')
                  .Append(document.PartyFlavor[i].Name).Append('\n');
            }
            for (int i = 0; i < document.Articles.Count; i++)
            {
                ArticleEntry a = document.Articles[i];
                sb.Append(a.Id).Append('|').Append(a.Outlet).Append('|').Append(a.Headline).Append('|')
                  .Append(a.Body).Append('|').Append(a.Tone).Append('|')
                  .Append(a.PartyId).Append('|').Append(a.DistrictId).Append('|').Append(a.EventId)
                  .Append('\n');
            }
            return sb.ToString();
        }
    }
}
