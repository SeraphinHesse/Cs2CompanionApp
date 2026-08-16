using System.Collections.Generic;
using Agora.Core.Stories;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The command log's ordering rule, pinned in the same commit that claims it.
    /// </summary>
    /// <remarks>
    /// Wave 3's handoff records a doc comment that asserted a test pinned two constants together when
    /// no such test existed, and the two had already drifted once on the strength of the claim. These
    /// exist so <see cref="PlayerCommandLog"/>'s remarks are checkable rather than merely stated.
    /// </remarks>
    public sealed class PlayerCommandLogTests
    {
        private static PlayerCommand Command(string eventId, int month,
                                             PlayerCommandKind kind = PlayerCommandKind.SetResponse) =>
            new PlayerCommand
            {
                StoryId = "story-01",
                EventId = eventId,
                Kind = kind,
                DecidedMonth = month
            };

        /// <summary>Two commands in one month get distinct, ascending sequences.</summary>
        /// <remarks>
        /// The property the ledger's own key depends on: without it both rows carry 0 and the
        /// tiebreak — event id — decides an order the player's actions should have decided.
        /// </remarks>
        [Fact]
        public void TwoCommandsInOneMonthGetAscendingSequences()
        {
            var log = new List<PlayerCommand>();

            PlayerCommandLog.Append(log, Command("ev-b", 100));
            PlayerCommandLog.Append(log, Command("ev-a", 100));
            PlayerCommandLog.Append(log, Command("ev-c", 100));

            Assert.Equal(new[] { 0, 1, 2 }, new[] { log[0].Sequence, log[1].Sequence, log[2].Sequence });

            // Order is the order the player acted, not the alphabetical order of the event ids.
            Assert.Equal(new[] { "ev-b", "ev-a", "ev-c" },
                         new[] { log[0].EventId, log[1].EventId, log[2].EventId });
        }

        /// <summary>Each month's sequence starts again at zero.</summary>
        [Fact]
        public void SequenceRestartsEachMonth()
        {
            var log = new List<PlayerCommand>();

            PlayerCommandLog.Append(log, Command("ev-a", 100));
            PlayerCommandLog.Append(log, Command("ev-b", 100));
            PlayerCommandLog.Append(log, Command("ev-c", 101));

            Assert.Equal(0, log[2].Sequence);
            Assert.Equal(101, log[2].DecidedMonth);
        }

        /// <summary>
        /// A command recorded for an earlier month than one already logged still sorts into position.
        /// </summary>
        /// <remarks>
        /// Not a hypothetical: a catch-up run resolves several months in one pass, and nothing
        /// guarantees the command that lands first belongs to the earliest of them. Appending blindly
        /// would leave the log out of order and fail the state hash while nothing was actually wrong.
        /// </remarks>
        [Fact]
        public void AnOutOfOrderMonthSortsIntoPosition()
        {
            var log = new List<PlayerCommand>();

            PlayerCommandLog.Append(log, Command("ev-a", 105));
            PlayerCommandLog.Append(log, Command("ev-b", 100));

            Assert.Equal(100, log[0].DecidedMonth);
            Assert.Equal(105, log[1].DecidedMonth);
            Assert.Equal(0, log[0].Sequence);
        }

        /// <summary>The log stays sorted by its documented key after every append.</summary>
        [Fact]
        public void TheLogIsSortedByItsDocumentedKeyAfterEveryAppend()
        {
            var log = new List<PlayerCommand>();
            int[] months = { 103, 100, 101, 100, 103, 102, 100 };

            for (int i = 0; i < months.Length; i++)
            {
                PlayerCommandLog.Append(log, Command("ev-" + i, months[i]));

                for (int j = 1; j < log.Count; j++)
                {
                    PlayerCommand a = log[j - 1];
                    PlayerCommand b = log[j];

                    bool ordered = a.DecidedMonth < b.DecidedMonth
                                || (a.DecidedMonth == b.DecidedMonth && a.Sequence < b.Sequence);

                    Assert.True(ordered,
                        "The log fell out of (DecidedMonth, Sequence) order at index " + j +
                        " after appending month " + months[i] + ".");
                }
            }
        }

        /// <summary>
        /// The declared outcome survives on the record, so the log can tell a declared success from a
        /// declared failure.
        /// </summary>
        /// <remarks>
        /// The contract on <see cref="PlayerCommand"/> says the log is replayed rather than
        /// re-solicited. Before <c>DeclaredMet</c> existed the two declarations appended rows
        /// differing in no field at all, so a replay could not reconstruct
        /// <see cref="StorySlot.ManualDeclared"/> — and that flag is what
        /// <see cref="PoliticalPower.AwardFor"/> reads to decide the award.
        /// </remarks>
        [Fact]
        public void ADeclaredSuccessAndADeclaredFailureAreDistinguishableInTheLog()
        {
            var log = new List<PlayerCommand>();

            PlayerCommand met = Command("ev-a", 100, PlayerCommandKind.DeclareManualOutcome);
            met.DeclaredMet = true;
            met.FreeText = "we did it";

            PlayerCommand notMet = Command("ev-b", 100, PlayerCommandKind.DeclareManualOutcome);
            notMet.DeclaredMet = false;
            notMet.FreeText = "we did it";

            PlayerCommandLog.Append(log, met);
            PlayerCommandLog.Append(log, notMet);

            Assert.NotEqual(log[0].DeclaredMet, log[1].DeclaredMet);
        }

        /// <summary>A null log or a null command is ignored rather than throwing.</summary>
        /// <remarks>
        /// This runs from a command handler on the UI thread, where an escaping exception costs far
        /// more than a dropped record — the same posture every other data path in the runtime takes.
        /// </remarks>
        [Fact]
        public void NullsAreIgnoredRatherThanThrown()
        {
            var log = new List<PlayerCommand>();

            PlayerCommandLog.Append(null, Command("ev-a", 100));
            PlayerCommandLog.Append(log, null);

            Assert.Empty(log);
        }
    }
}
