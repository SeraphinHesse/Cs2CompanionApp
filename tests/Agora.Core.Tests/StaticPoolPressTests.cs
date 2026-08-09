// Requires the StaticPoolContent.cs / StaticPoolProvider.cs / FlavorValidator.cs <Compile Link>
// lines in Agora.Core.Tests.csproj (see the comment there for why).

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Agora.Core.Contracts;
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
    /// </summary>
    public class StaticPoolPressTests
    {
        private static readonly Guid Save = new Guid("9a4f10d2-0000-4000-8000-abcdefabcdef");
        private static readonly SimDate Founded = new SimDate(2018, 4, 1);
        private static readonly SimDate Date = new SimDate(2031, 5, 1);

        private static StaticPoolProvider Pool(RegionTheme theme) =>
            new StaticPoolProvider(Save, theme,
                                   FlavorValidator.Create(null, NullFlavorLog.Instance),
                                   NullFlavorLog.Instance);

        // ---- refs ---------------------------------------------------------------------------------

        [Theory]
        [InlineData(FlavorWakeReason.Yearly, RegionTheme.Eu, 4)]
        [InlineData(FlavorWakeReason.Yearly, RegionTheme.Na, 4)]
        [InlineData(FlavorWakeReason.Election, RegionTheme.Eu, 8)]
        [InlineData(FlavorWakeReason.Election, RegionTheme.Na, 7)]
        public void EveryArticleInARoundPointsAtSomething(FlavorWakeReason reason, RegionTheme theme, int count)
        {
            // Several seeds, because the branch an article takes and the party or district it lands on
            // are both drawn: a single date would exercise one path through the round and call it the
            // round. The count assertion is half the test — FlavorValidator now drops a refless
            // article, so a city piece that lost its refs would show up here as a short round rather
            // than as an article failing the loop below.
            for (int month = 1; month <= 12; month++)
            {
                var date = new SimDate(2031, month, 1);
                FlavorRequest request = Request(date, reason, theme, parties: 3, districts: 4);
                request.ArticleCount = count;

                FlavorDocument document = Pool(theme).Generate(request);
                Assert.NotNull(document);
                Assert.Equal(count, document.Articles.Count);

                for (int i = 0; i < document.Articles.Count; i++)
                {
                    ArticleEntry article = document.Articles[i];
                    Assert.True(!string.IsNullOrEmpty(article.PartyId) ||
                                !string.IsNullOrEmpty(article.DistrictId) ||
                                !string.IsNullOrEmpty(article.EventId),
                                "article " + article.Id + " in " + date + " points at nothing.");
                }
            }
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
        public void PartiesButNoDistricts_FilesEveryArticleAgainstAParty()
        {
            // The old alternation was "district on odd i", so with no districts every other article
            // took the city branch, which carried no refs. Now the branch with nothing to point at is
            // not taken at all.
            FlavorRequest request = Request(Date, FlavorWakeReason.Yearly, RegionTheme.Eu,
                                            parties: 3, districts: 0);

            FlavorDocument document = Pool(RegionTheme.Eu).Generate(request);
            Assert.NotNull(document);
            Assert.Equal(FlavorRequest.DefaultArticleCount, document.Articles.Count);

            for (int i = 0; i < document.Articles.Count; i++)
            {
                Assert.False(string.IsNullOrEmpty(document.Articles[i].PartyId));
                Assert.Equal(string.Empty, document.Articles[i].DistrictId);
            }
        }

        [Fact]
        public void DistrictsButNoParties_FilesEveryArticleAgainstADistrict()
        {
            FlavorRequest request = Request(Date, FlavorWakeReason.Yearly, RegionTheme.Eu,
                                            parties: 0, districts: 4);

            FlavorDocument document = Pool(RegionTheme.Eu).Generate(request);
            Assert.NotNull(document);
            Assert.Equal(FlavorRequest.DefaultArticleCount, document.Articles.Count);

            for (int i = 0; i < document.Articles.Count; i++)
            {
                Assert.False(string.IsNullOrEmpty(document.Articles[i].DistrictId));
                Assert.Equal(string.Empty, document.Articles[i].PartyId);
            }
        }

        [Fact]
        public void AnArticleNamesTheVeryPartyItRefs()
        {
            // The ref is only checkable by a reader if the prose names the same party the id points
            // at, which means the article has to use the name this document gave that party rather
            // than a fresh draw.
            FlavorRequest request = Request(Date, FlavorWakeReason.Yearly, RegionTheme.Eu,
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
            FlavorRequest request = Request(Date, FlavorWakeReason.Yearly, RegionTheme.Eu,
                                            parties: 3, districts: 4);
            request.ArticleCount = FlavorRequest.ElectionArticleCountEu;

            FlavorDocument document = Pool(RegionTheme.Eu).Generate(request);
            Assert.NotNull(document);

            Assert.False(DrawnFrom(document, StaticPoolContent.ElectionResultHeadlines));
            Assert.False(DrawnFrom(document, StaticPoolContent.ElectionClaimHeadlines));
            Assert.False(DrawnFrom(document, StaticPoolContent.ElectionChallengeHeadlines));
            Assert.False(DrawnFrom(document, StaticPoolContent.ElectionCoalitionHeadlines));
        }

        [Fact]
        public void AnElectionWithNoPartiesFallsBackToAnOrdinaryRound()
        {
            // Every election piece names a party, so a roster that failed to build cannot have one.
            // Filing district pieces instead is the fail-closed answer (non-negotiable #7); inventing
            // a subject for the result piece is not.
            FlavorRequest request = Request(Date, FlavorWakeReason.Election, RegionTheme.Eu,
                                            parties: 0, districts: 4);

            FlavorDocument document = Pool(RegionTheme.Eu).Generate(request);
            Assert.NotNull(document);
            Assert.Equal(FlavorRequest.DefaultArticleCount, document.Articles.Count);

            Assert.False(DrawnFrom(document, StaticPoolContent.ElectionResultHeadlines));
            for (int i = 0; i < document.Articles.Count; i++)
            {
                Assert.False(string.IsNullOrEmpty(document.Articles[i].DistrictId));
            }
        }

        // ---- length, and the headline truncator -------------------------------------------------

        [Fact]
        public void AnAbsurdlyLongDistrictNameCostsThePlaceholder_NotTheLastWord()
        {
            // The defect: SafeName cut the name at sixty, mid-word, and Cap then cut the composed
            // headline at ninety, taking the template's own trailing words with it —
            // "{district} says it has been waiting long enough" came out ending "...waiting long e".
            // The rule now is to drop the placeholder rather than cut a name, so the article gets a
            // clean generic headline instead of a mangled specific one.
            string name = "The Old Harbourside Wharves and Cooperage Quarter Conservation Area " +
                          "Extension, North Bank";
            Assert.True(name.Length > 60, "the fixture must be long enough to force the fallback.");

            FlavorRequest request = Request(Date, FlavorWakeReason.Yearly, RegionTheme.Eu,
                                            parties: 0, districts: 3);
            for (int i = 0; i < request.Snapshot.Districts.Count; i++) request.Snapshot.Districts[i].Name = name;

            FlavorDocument document = Pool(RegionTheme.Eu).Generate(request);
            Assert.NotNull(document);
            Assert.Equal(FlavorRequest.DefaultArticleCount, document.Articles.Count);

            for (int i = 0; i < document.Articles.Count; i++)
            {
                ArticleEntry article = document.Articles[i];

                Assert.True(article.Headline.Length <= FlavorCacheMigration.HeadlineMaxLength,
                            "headline over the cap: " + article.Headline);

                // Whole, not cut: the headline is one of the pool's own lines, and with a name this
                // long that has to be a generic one. A truncated line would match neither.
                Assert.True(MatchesSome(article.Headline, StaticPoolContent.GenericHeadlines),
                            "headline is neither a generic line nor a whole one: " + article.Headline);

                // The body has four hundred and twenty characters to play with, so a name this long
                // still fits one and the placeholder is kept. Which pool it came from is the point:
                // the fallback is per line and per cap, not a switch thrown for the whole article.
                Assert.True(article.Body.Length <= FlavorCacheMigration.BodyMaxLength);
                Assert.True(MatchesSome(article.Body, StaticPoolContent.DistrictBodies) ||
                            MatchesSome(article.Body, StaticPoolContent.GenericBodies),
                            "body came in neither whole nor generic: " + article.Body);
                Assert.EndsWith(".", article.Body, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void ADistrictNameLongerThanAnArticle_StillProducesAWholeArticle()
        {
            // Past the body cap as well as the headline cap, which is the only way to reach the body's
            // own fallback. Nothing here may throw, nothing may be cut, and the round must still be
            // the length it was asked for — a player who names a district by pasting a paragraph into
            // the box is a nuisance, not a crash (non-negotiable #7).
            string name = new string('x', FlavorCacheMigration.BodyMaxLength + 100);

            FlavorRequest request = Request(Date, FlavorWakeReason.Yearly, RegionTheme.Eu,
                                            parties: 0, districts: 3);
            for (int i = 0; i < request.Snapshot.Districts.Count; i++) request.Snapshot.Districts[i].Name = name;

            FlavorDocument document = Pool(RegionTheme.Eu).Generate(request);
            Assert.NotNull(document);
            Assert.Equal(FlavorRequest.DefaultArticleCount, document.Articles.Count);

            for (int i = 0; i < document.Articles.Count; i++)
            {
                ArticleEntry article = document.Articles[i];
                Assert.True(MatchesSome(article.Headline, StaticPoolContent.GenericHeadlines),
                            "headline came in cut rather than generic: " + article.Headline);
                Assert.True(MatchesSome(article.Body, StaticPoolContent.GenericBodies),
                            "body came in cut rather than generic: " + article.Body);
                Assert.False(string.IsNullOrEmpty(article.DistrictId));
            }
        }

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
            // The rule that makes every ref checkable. City and election pieces ref a party and must
            // name it; district pieces ref a district and must name it. The generic pool is the one
            // exception and carries neither, by construction — it exists precisely for the case where
            // the name will not fit.
            foreach (string[] pool in PartyHeadlinePools()) AssertEachContains(pool, "{party}");
            foreach (string[] pool in PartyBodyPools()) AssertEachContains(pool, "{party}");

            AssertEachContains(StaticPoolContent.DistrictHeadlines, "{district}");
            AssertEachContains(StaticPoolContent.DistrictBodies, "{district}");

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
            // UniqueLine's bounded retry then gives up and allows one. PlanRound spreads an election
            // round over five pools and an ordinary one over two, so at these counts no pool is asked
            // for more than it holds and no repeat is legitimate.
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
                        Assert.Equal(request.ArticleCount, document.Articles.Count);

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

        [Fact]
        public void TheSameRequestTwiceIsStillByteIdenticalWhenCandidatesAreRejected()
        {
            // The two cases above use short district names, so every substituted headline fits and
            // UniqueLine never takes its "over the cap: continue" branch — the interesting path was
            // going unexercised. It matters because a rejected candidate is not a free skip: Pick has
            // already consumed a NextInt by the time the continue fires, so the stream is further on
            // than the number of published lines suggests. That is fine, and deterministic, but only
            // a same-seed comparison down the rejecting path can say so.
            string name = "The Old Harbourside Wharves and Cooperage Quarter Conservation Area " +
                          "Extension, North Bank";
            Assert.True(name.Length > 60, "the fixture must be long enough to force the rejections.");

            // Yearly, because only the district branch substitutes a name the player controls: an
            // election round at the default count is all party pieces, and a party name is ours and
            // always fits.
            FlavorRequest first = Request(Date, FlavorWakeReason.Yearly, RegionTheme.Eu,
                                          parties: 5, districts: 6);
            FlavorRequest second = Request(Date, FlavorWakeReason.Yearly, RegionTheme.Eu,
                                           parties: 5, districts: 6);
            for (int i = 0; i < first.Snapshot.Districts.Count; i++)
            {
                first.Snapshot.Districts[i].Name = name;
                second.Snapshot.Districts[i].Name = name;
            }

            FlavorDocument a = Pool(RegionTheme.Eu).Generate(first);
            FlavorDocument b = Pool(RegionTheme.Eu).Generate(second);

            // The rejecting path must actually have been taken, or this is the short-name case again
            // wearing a long name: a district headline that fits would never reach the generic pool.
            Assert.True(DrawnFrom(a, StaticPoolContent.GenericHeadlines),
                        "no article fell back, so no candidate was rejected.");

            Assert.Equal(Fingerprint(a), Fingerprint(b));
        }

        // ---- fixtures and helpers -----------------------------------------------------------------

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
            yield return Pair("CityHeadlines", StaticPoolContent.CityHeadlines);
            yield return Pair("CityBodies", StaticPoolContent.CityBodies);
            yield return Pair("DistrictHeadlines", StaticPoolContent.DistrictHeadlines);
            yield return Pair("DistrictBodies", StaticPoolContent.DistrictBodies);
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
        }

        private static KeyValuePair<string, string[]> Pair(string name, string[] pool) =>
            new KeyValuePair<string, string[]>(name, pool);

        private static string[][] PartyHeadlinePools() => new[]
        {
            StaticPoolContent.CityHeadlines,
            StaticPoolContent.ElectionResultHeadlines,
            StaticPoolContent.ElectionClaimHeadlines,
            StaticPoolContent.ElectionChallengeHeadlines,
            StaticPoolContent.ElectionCoalitionHeadlines
        };

        private static string[][] PartyBodyPools() => new[]
        {
            StaticPoolContent.CityBodies,
            StaticPoolContent.ElectionResultBodies,
            StaticPoolContent.ElectionClaimBodies,
            StaticPoolContent.ElectionChallengeBodies,
            StaticPoolContent.ElectionCoalitionBodies
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
