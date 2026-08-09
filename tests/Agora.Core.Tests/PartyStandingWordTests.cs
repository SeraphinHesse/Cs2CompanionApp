// Requires the FlavorRequest.cs / FlavorPromptBuilder.cs <Compile Link> lines in
// Agora.Core.Tests.csproj (see the comment there for why).

using System;
using Agora.Core.Contracts;
using Agora.Mod.Llm;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The one word the election prompt is written against.
    ///
    /// <para>
    /// <c>AgoraRuntime.FillBriefs</c> used to fill <see cref="PartyBrief.StatusWord"/> with
    /// <c>Party.Status.ToString()</c> — Active, Endangered, Dissolved, Merged, Revived — while
    /// <c>FlavorPromptBuilder.AppendElectionCoverage</c> told the model that word was "the whole of
    /// the outcome you may write from". A lifecycle word carries no outcome at all, so between them
    /// the two artefacts asked for a result and supplied nothing to write one from, which is an
    /// invitation to invent one (non-negotiable #1).
    /// </para>
    ///
    /// <para>
    /// What goes instead is a governing phrase. It is still not a result — government formation may
    /// not have run when the brief is built — and the prompt now says so rather than overstating it,
    /// which is the second half of this file.
    /// </para>
    /// </summary>
    public class PartyStandingWordTests
    {
        private static Party Of(PartyStatus status, bool incumbent, bool inGovernment) => new Party
        {
            Id = "party-riverside",
            ArchetypeId = "greens",
            Status = status,
            IsIncumbent = incumbent,
            IsInGovernment = inGovernment
        };

        // --- the word itself ------------------------------------------------------------------------

        [Theory]
        [InlineData(PartyStatus.Active, true, true, "leads the government")]
        [InlineData(PartyStatus.Active, false, true, "in government")]
        [InlineData(PartyStatus.Active, false, false, "in opposition")]
        public void TheWordSaysWhoGoverns(PartyStatus status, bool incumbent, bool inGovernment,
                                          string expected)
        {
            Assert.Equal(expected, PartyBrief.StandingWord(Of(status, incumbent, inGovernment)));
        }

        [Theory]
        [InlineData(PartyStatus.Endangered, "in opposition, at risk of folding")]
        [InlineData(PartyStatus.Revived, "in opposition, recently revived")]
        public void ALifecycleWorthNamingQualifiesTheRoleRatherThanReplacingIt(PartyStatus status,
                                                                              string expected)
        {
            // Both are worth writing about and neither states a figure, so the phrase keeps the role
            // and adds them. The endangered qualifier is deliberately not "losing ground": that is
            // one adjective off "lost ground", an imitable loser-naming phrase the election block
            // dropped, and the roster reaches the model a section above "Do not name a winner or a
            // loser". "At risk of folding" is the same engine fact with no electoral reading.
            Assert.Equal(expected, PartyBrief.StandingWord(Of(status, false, false)));
        }

        [Theory]
        [InlineData(PartyStatus.Dissolved, "dissolved, off the ballot")]
        [InlineData(PartyStatus.Merged, "merged into another party")]
        public void APartyThatIsGoneIsNotDescribedAsInOpposition(PartyStatus status, string expected)
        {
            // PartyLifecycle clears both flags on a dissolved or merged party, so the role would come
            // out "in opposition" — which reads as a party still contesting things. Off the ballot is
            // the whole story there, and it overrides.
            Assert.Equal(expected, PartyBrief.StandingWord(Of(status, false, false)));
        }

        [Fact]
        public void NoCombinationYieldsABarePartyStatusName()
        {
            // The regression, pinned by exclusion rather than by wording: whatever the phrase becomes,
            // it must never be one of the five enum names again. A test on the exact strings above
            // alone would pass a change that reverted one branch to Status.ToString().
            foreach (PartyStatus status in Enum.GetValues(typeof(PartyStatus)))
            {
                foreach (bool incumbent in new[] { false, true })
                {
                    foreach (bool inGovernment in new[] { false, true })
                    {
                        string word = PartyBrief.StandingWord(Of(status, incumbent, inGovernment));

                        Assert.NotEqual(status.ToString(), word);
                        Assert.False(string.IsNullOrEmpty(word),
                                     "no standing phrase for " + status + "/" + incumbent + "/" + inGovernment);
                    }
                }
            }
        }

        [Fact]
        public void TheWordCarriesNoFigure()
        {
            // It reaches the model inside the party list, which the numeric sweep cannot see into
            // because it is a string by then. A digit here would be a figure the model reads as fact.
            foreach (PartyStatus status in Enum.GetValues(typeof(PartyStatus)))
            {
                string word = PartyBrief.StandingWord(Of(status, false, true));
                Assert.DoesNotMatch(@"\d", word);
            }
        }

        [Fact]
        public void ANullPartyYieldsAnEmptyWordRatherThanThrowing()
        {
            Assert.Equal(string.Empty, PartyBrief.StandingWord(null));
        }

        // --- and what the prompt claims of it -------------------------------------------------------

        [Fact]
        public void TheElectionBlockNoLongerCallsTheStandingWordTheOutcome()
        {
            // The three artefacts have to agree: the doc comment on StatusWord, what FillBriefs
            // assigns, and what the prompt says the model may write from. The word says who governs
            // as things stand — which after a count is quite possibly the arrangement the count just
            // unseated — so the block says that and forbids naming a winner, rather than presenting
            // the word as the result.
            var request = new FlavorRequest
            {
                Date = new SimDate(2031, 5, 1),
                Reason = FlavorWakeReason.Election,
                Theme = RegionTheme.Eu
            };
            request.Parties.Add(new PartyBrief
            {
                PartyId = "party-riverside",
                ArchetypeId = "greens",
                CoreGrievance = Issue.Environment,
                StatusWord = PartyBrief.StandingWord(Of(PartyStatus.Active, false, false)),
                CurrentName = "Riverside Slate"
            });

            string prompt = FlavorPromptBuilder.Build(request);

            Assert.DoesNotContain("the whole of the outcome you may write from", prompt);
            Assert.Contains("You have not been given the winner either", prompt);
            Assert.Contains("it is not the result", prompt);
            Assert.Contains("Do not name a winner or a loser", prompt);

            // And the word the block is talking about is in the list it points at.
            Assert.Contains("| in opposition |", prompt);
        }
    }
}
