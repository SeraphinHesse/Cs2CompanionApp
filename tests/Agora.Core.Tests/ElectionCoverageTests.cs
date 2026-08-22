// Requires the ElectionCoverage.cs and NewsAlert.cs <Compile Link> lines in Agora.Core.Tests.csproj
// (see the comment there for why).

using Agora.Core.Contracts;
using Agora.Mod.Core;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The join between an election alert and the articles that covered its election.
    ///
    /// <para>
    /// The failure being guarded is silent in both directions and the reason the whole mechanism is
    /// one function. If the card says <c>hasArticle</c> and the fetch finds nothing, the player gets
    /// a blank masthead and nothing is logged — the shape of bug <c>ui_bindings.md</c> §4.5 writes
    /// down and this repo has already shipped once. If the card says nothing and the fetch would have
    /// found prose, an election round's writing reaches no surface at all, which is the gap wave 7
    /// opened. So every test here is written as "these two answers agree", not as "the flag is true".
    /// </para>
    /// <para>
    /// <c>AgoraRuntime</c> and <c>UiBindings</c> link into no test by design, so what is asserted is
    /// the resolver they both call. The wiring around it — that the wake expects, that the alert is
    /// raised with the same id, that the projection asks — is covered by the manual gate rows in the
    /// lane's report.
    /// </para>
    /// </summary>
    public class ElectionCoverageTests
    {
        private static readonly SimDate Wake = new SimDate(2031, 6, 1);

        private static FlavorPayload Round(params string[] articleIds)
        {
            var payload = new FlavorPayload { GeneratedAt = Wake };
            for (int i = 0; i < articleIds.Length; i++)
            {
                payload.Articles.Add(new Article
                {
                    Id = articleIds[i],
                    Headline = articleIds[i] + " headline",
                    Body = articleIds[i] + " body"
                });
            }

            return payload;
        }

        /// <summary>
        /// The whole point, in one test: a round woken for an election gives that election's card a
        /// body, and both callers get the same answer.
        /// </summary>
        [Fact]
        public void ARoundWokenForAnElection_GivesThatElectionsCardABody()
        {
            var coverage = new ElectionCoverage();
            string alertId = NewsAlert.ElectionAlertId("election-2031");

            coverage.Expect(alertId, Wake);

            FlavorPayload prose = Round("static-2031-06-1", "static-2031-06-2", "static-2031-06-3");
            coverage.Absorb(prose, Wake);

            // BuildAlerts asks this to decide hasArticle; BuildArticle asks it to find the body. One
            // answer, so they cannot disagree.
            string resolved = coverage.ResolveArticleId(prose, alertId);
            Assert.Equal("static-2031-06-1", resolved);
        }

        /// <summary>
        /// The declared total order, asserted against the order the payload happens to list. Ordinal
        /// ascending on the id is the house convention; "whichever was first in the payload" is the
        /// determinism bug it exists to avoid.
        /// </summary>
        [Fact]
        public void TheBodyIsTheOrdinallyFirstArticleId_NotThePayloadsFirstEntry()
        {
            var coverage = new ElectionCoverage();
            string alertId = NewsAlert.ElectionAlertId("election-2031");

            coverage.Expect(alertId, Wake);

            // Deliberately reversed. Under "first in the payload" this answers -3.
            FlavorPayload prose = Round("static-2031-06-3", "static-2031-06-2", "static-2031-06-1");
            coverage.Absorb(prose, Wake);

            Assert.Equal("static-2031-06-1", coverage.ResolveArticleId(prose, alertId));
        }

        /// <summary>
        /// Two payloads carrying the same round in different orders resolve to the same body — the
        /// property "sorted" is actually for.
        /// </summary>
        [Fact]
        public void TheSameRoundInEitherOrder_ResolvesToTheSameBody()
        {
            string alertId = NewsAlert.ElectionAlertId("election-2031");

            var forwards = new ElectionCoverage();
            forwards.Expect(alertId, Wake);
            FlavorPayload a = Round("a-1", "a-2", "a-3", "a-4");
            forwards.Absorb(a, Wake);

            var backwards = new ElectionCoverage();
            backwards.Expect(alertId, Wake);
            FlavorPayload b = Round("a-4", "a-3", "a-2", "a-1");
            backwards.Absorb(b, Wake);

            Assert.Equal(forwards.ResolveArticleId(a, alertId), backwards.ResolveArticleId(b, alertId));
        }

        /// <summary>
        /// No prose at all — no CLI, a timeout, bad JSON — answers "no body" rather than a body the
        /// fetch cannot find. Fail closed (#7): a headline and a summary is a correct card.
        /// </summary>
        [Fact]
        public void WithNoRound_TheCardClaimsNoBody()
        {
            var coverage = new ElectionCoverage();
            string alertId = NewsAlert.ElectionAlertId("election-2031");

            coverage.Expect(alertId, Wake);

            Assert.Equal("", coverage.ResolveArticleId(null!, alertId));
            Assert.Equal("", coverage.ResolveArticleId(new FlavorPayload(), alertId));
        }

        /// <summary>
        /// The ordinary months between the wake and the round must not consume the expectation. The
        /// canned pool answers every poll and writes articles only on an election round, so an
        /// article-less payload is the common case, not the answer.
        /// </summary>
        [Fact]
        public void AnArticlelessPollBeforeTheRound_DoesNotConsumeTheExpectation()
        {
            var coverage = new ElectionCoverage();
            string alertId = NewsAlert.ElectionAlertId("election-2031");

            coverage.Expect(alertId, Wake);
            coverage.Absorb(new FlavorPayload(), Wake);

            Assert.True(coverage.IsExpecting);

            FlavorPayload round = Round("static-2031-06-1");
            coverage.Absorb(round, Wake);

            Assert.Equal("static-2031-06-1", coverage.ResolveArticleId(round, alertId));
        }

        /// <summary>
        /// The stale-attachment guard, stated as the bug it prevents: the previous election's
        /// coverage must never appear over the next election's card, even though the payload that
        /// carried it is still the last good one in memory.
        /// </summary>
        [Fact]
        public void ANewElection_NeverInheritsThePreviousElectionsCoverage()
        {
            var coverage = new ElectionCoverage();
            string first = NewsAlert.ElectionAlertId("election-2031");
            string second = NewsAlert.ElectionAlertId("election-2035");

            coverage.Expect(first, Wake);
            FlavorPayload round = Round("static-2031-06-1");
            coverage.Absorb(round, Wake);
            Assert.Equal("static-2031-06-1", coverage.ResolveArticleId(round, first));

            // Four years later, and the 2031 round is still the payload in force because the new one
            // has not landed yet. Both defences are asserted: the key is the alert's own id, and the
            // wake dropped what was recorded.
            var later = new SimDate(2035, 6, 1);
            coverage.Expect(second, later);

            Assert.Equal("", coverage.ResolveArticleId(round, second));
            Assert.Equal("", coverage.ResolveArticleId(round, first));
        }

        /// <summary>
        /// A card for something else — a coalition, a party, an event — resolves nothing. Nothing
        /// records coverage for those, and a resolver that answered on kind rather than on the
        /// recorded id would give all of them the election's body.
        /// </summary>
        [Fact]
        public void AnAlertOfAnotherKind_ResolvesNothing()
        {
            var coverage = new ElectionCoverage();
            coverage.Expect(NewsAlert.ElectionAlertId("election-2031"), Wake);

            FlavorPayload round = Round("static-2031-06-1");
            coverage.Absorb(round, Wake);

            Assert.Equal("", coverage.ResolveArticleId(round, "coalition:gov-3:formed"));
            Assert.Equal("", coverage.ResolveArticleId(round, "party:p-2:founded"));
            Assert.Equal("", coverage.ResolveArticleId(round, "event:flood-2031"));
            Assert.Equal("", coverage.ResolveArticleId(round, ""));
        }

        /// <summary>
        /// The next payload replaces the articles the join named, and the flag goes false again with
        /// them. This is the self-correcting half: what is recorded is re-checked against the live
        /// payload every call, never trusted.
        /// </summary>
        [Fact]
        public void WhenTheNextPayloadReplacesTheRound_TheCardStopsClaimingABody()
        {
            var coverage = new ElectionCoverage();
            string alertId = NewsAlert.ElectionAlertId("election-2031");

            coverage.Expect(alertId, Wake);
            FlavorPayload round = Round("static-2031-06-1");
            coverage.Absorb(round, Wake);

            Assert.Equal("static-2031-06-1", coverage.ResolveArticleId(round, alertId));

            // The following month's ordinary poll: same ledger, a payload with no articles in it.
            Assert.Equal("", coverage.ResolveArticleId(new FlavorPayload(), alertId));
        }

        /// <summary>
        /// An article with a headline but no body is not a resolvable body. Claiming it would put a
        /// masthead with nothing under it in front of the player, which is the failure, not a partial
        /// success — so the resolver walks on to the next piece of the round.
        /// </summary>
        [Fact]
        public void AnArticleWithNoBody_IsSkippedForOneThatHasOne()
        {
            var coverage = new ElectionCoverage();
            string alertId = NewsAlert.ElectionAlertId("election-2031");

            coverage.Expect(alertId, Wake);

            FlavorPayload prose = Round("static-2031-06-1", "static-2031-06-2");
            prose.Articles[0].Body = "";
            coverage.Absorb(prose, Wake);

            Assert.Equal("static-2031-06-2", coverage.ResolveArticleId(prose, alertId));

            // And when none of them has one, nothing is claimed.
            prose.Articles[1].Body = "";
            Assert.Equal("", coverage.ResolveArticleId(prose, alertId));
        }

        /// <summary>
        /// An expectation nobody answered lapses instead of latching onto a round drawn years later.
        /// "The last good payload is still in memory" is exactly how it would otherwise happen.
        /// </summary>
        [Fact]
        public void AnUnansweredExpectation_LapsesRatherThanTakingALaterRound()
        {
            var coverage = new ElectionCoverage();
            string alertId = NewsAlert.ElectionAlertId("election-2031");

            coverage.Expect(alertId, Wake);

            var muchLater = Wake.AddMonths(ElectionCoverage.WaitMonths + 1);
            FlavorPayload round = Round("static-2031-09-1");
            coverage.Absorb(round, muchLater);

            Assert.False(coverage.IsExpecting);
            Assert.Equal("", coverage.ResolveArticleId(round, alertId));
        }

        /// <summary>
        /// A clock that moved backwards — a §5 reconciliation onto an earlier snapshot — abandons the
        /// expectation rather than filing against it.
        /// </summary>
        [Fact]
        public void APayloadArrivingBeforeTheWake_AbandonsTheExpectation()
        {
            var coverage = new ElectionCoverage();
            string alertId = NewsAlert.ElectionAlertId("election-2031");

            coverage.Expect(alertId, Wake);

            FlavorPayload round = Round("static-2031-06-1");
            coverage.Absorb(round, Wake.AddMonths(-1));

            Assert.False(coverage.IsExpecting);
            Assert.Equal("", coverage.ResolveArticleId(round, alertId));
        }

        /// <summary>
        /// The save boundary. A card from city A resolving city B's prose is the bug class this repo
        /// has shipped once, which is why <c>ResetForNewSave</c> clears this in the same block as the
        /// ring.
        /// </summary>
        [Fact]
        public void Clear_ForgetsBothTheExpectationAndTheAssociation()
        {
            var coverage = new ElectionCoverage();
            string alertId = NewsAlert.ElectionAlertId("election-2031");

            coverage.Expect(alertId, Wake);
            FlavorPayload round = Round("static-2031-06-1");
            coverage.Absorb(round, Wake);

            coverage.Clear();

            Assert.False(coverage.IsExpecting);
            Assert.Equal("", coverage.RecordedAlertId);
            Assert.Equal("", coverage.ResolveArticleId(round, alertId));
        }

        /// <summary>
        /// A tick that woke for an election it cannot name records nothing — and still supersedes
        /// what was there, because the alternative is the previous election's coverage surviving a
        /// wake that was not for it.
        /// </summary>
        [Fact]
        public void AWakeWithNoElectionId_RecordsNothingAndDropsWhatWasThere()
        {
            var coverage = new ElectionCoverage();
            string alertId = NewsAlert.ElectionAlertId("election-2031");

            coverage.Expect(alertId, Wake);
            FlavorPayload round = Round("static-2031-06-1");
            coverage.Absorb(round, Wake);

            coverage.Expect(NewsAlert.ElectionAlertId(""), Wake.AddMonths(48));

            Assert.False(coverage.IsExpecting);
            Assert.Equal("", coverage.ResolveArticleId(round, alertId));
        }

        /// <summary>
        /// The id both sites mint. <c>AgoraRuntime</c> builds it once for the wake and once for the
        /// raise; a second literal concatenation in either place is how they come to differ by a
        /// character and the card silently resolves nothing.
        /// </summary>
        [Fact]
        public void TheElectionAlertId_IsPrefixedAndEmptyForNoElection()
        {
            Assert.Equal("election:election-2031", NewsAlert.ElectionAlertId("election-2031"));
            Assert.Equal("", NewsAlert.ElectionAlertId(""));
            Assert.Equal("", NewsAlert.ElectionAlertId(null!));
        }
    }
}
