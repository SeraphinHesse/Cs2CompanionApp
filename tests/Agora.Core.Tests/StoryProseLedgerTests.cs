// Requires the StoryProseLedger.cs <Compile Link> line in Agora.Core.Tests.csproj (see the comment
// there for why).

using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Mod.Llm;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The merge rule that decides whether the model's story prose ever reaches a player.
    ///
    /// <para>
    /// The asymmetry is the whole subject. The canned pool answers every poll — several times a sim
    /// month, forever, and always with something. The CLI answers after a wake, tens of seconds
    /// later, and often never. So the difference between "first write wins" and "latest write wins"
    /// is not a preference: under the second, model prose is erased by the next canned poll every
    /// single time, and the feature the wave exists for silently never ships. These tests are what
    /// stops that branch being flipped by someone who reads it as a tidy-up.
    /// </para>
    /// </summary>
    public class StoryProseLedgerTests
    {
        private static FlavorPayload Payload(params StoryProse[] stories)
        {
            var payload = new FlavorPayload();
            for (int i = 0; i < stories.Length; i++) payload.Stories.Add(stories[i]);
            return payload;
        }

        private static StoryProse Prose(string storyId, string headline, ProseSource source) =>
            new StoryProse { StoryId = storyId, Headline = headline, Article = headline + " body", Source = source };

        /// <summary>
        /// The canned poll that follows a model wake must not erase what the model wrote.
        /// </summary>
        [Fact]
        public void APoolPollAfterTheModelHasWritten_LeavesTheModelsProseAlone()
        {
            var ledger = new StoryProseLedger();

            ledger.Absorb(Payload(Prose("story-1", "canned", ProseSource.Pool)));
            ledger.Absorb(Payload(Prose("story-1", "written", ProseSource.Cli)));

            // The poll that would have erased it under a "latest wins" rule — and there are hundreds
            // of these over a story's life, against one of the wake above.
            ledger.Absorb(Payload(Prose("story-1", "canned again", ProseSource.Pool)));

            Assert.Equal("written", ledger.Get("story-1", StoryProseKind.Opening, ProseSource.Cli)!.Headline);

            // And the canned text the player first read is still the canned text they first read.
            Assert.Equal("canned", ledger.Get("story-1", StoryProseKind.Opening, ProseSource.Pool)!.Headline);
        }

        /// <summary>
        /// Both writers' prose is held at once — the model's is added, not substituted.
        /// </summary>
        [Fact]
        public void BothWriters_AreHeldSideBySide()
        {
            var ledger = new StoryProseLedger();

            ledger.Absorb(Payload(Prose("story-1", "canned", ProseSource.Pool),
                                  Prose("story-1", "written", ProseSource.Cli)));

            Assert.NotNull(ledger.Get("story-1", StoryProseKind.Opening, ProseSource.Pool));
            Assert.NotNull(ledger.Get("story-1", StoryProseKind.Opening, ProseSource.Cli));
            Assert.Equal(2, ledger.Count);
        }

        /// <summary>
        /// Openings and resolutions are separate slots, so a resolution cannot answer for an opening.
        /// </summary>
        [Fact]
        public void AnOpeningAndAResolution_DoNotShareASlot()
        {
            var ledger = new StoryProseLedger();

            var payload = new FlavorPayload();
            payload.Stories.Add(Prose("story-1", "it begins", ProseSource.Pool));
            payload.Resolutions.Add(Prose("story-1", "it ends", ProseSource.Pool));

            ledger.Absorb(payload);

            Assert.Equal("it begins", ledger.Get("story-1", StoryProseKind.Opening, ProseSource.Pool)!.Headline);
            Assert.Equal("it ends", ledger.Get("story-1", StoryProseKind.Resolution, ProseSource.Pool)!.Headline);
        }

        /// <summary>
        /// An entry with no text at all is not filed, because first-write-wins would make that
        /// emptiness permanent.
        /// </summary>
        /// <remarks>
        /// The pool builds its document without passing through <c>FlavorValidator</c>, so this is
        /// the only thing standing between a story that produced no text and a slot that can never
        /// afterwards be filled by the writer that had something to say.
        /// </remarks>
        [Fact]
        public void AnEmptyEntry_DoesNotClaimTheSlot()
        {
            var ledger = new StoryProseLedger();

            ledger.Absorb(Payload(new StoryProse { StoryId = "story-1", Source = ProseSource.Pool }));
            Assert.Null(ledger.Get("story-1", StoryProseKind.Opening, ProseSource.Pool));

            ledger.Absorb(Payload(Prose("story-1", "a real headline", ProseSource.Pool)));
            Assert.Equal("a real headline", ledger.Get("story-1", StoryProseKind.Opening, ProseSource.Pool)!.Headline);
        }

        /// <summary>
        /// Prose for a story that no longer exists is dropped, in both directions and from both
        /// writers.
        /// </summary>
        [Fact]
        public void RetainOnly_DropsEveryTraceOfAVanishedStory()
        {
            var ledger = new StoryProseLedger();

            var payload = new FlavorPayload();
            payload.Stories.Add(Prose("story-gone", "a", ProseSource.Pool));
            payload.Stories.Add(Prose("story-gone", "b", ProseSource.Cli));
            payload.Resolutions.Add(Prose("story-gone", "c", ProseSource.Pool));
            payload.Stories.Add(Prose("story-kept", "d", ProseSource.Pool));
            ledger.Absorb(payload);

            Assert.Equal(4, ledger.Count);

            ledger.RetainOnly(new HashSet<string> { "story-kept" });

            Assert.Equal(1, ledger.Count);
            Assert.NotNull(ledger.Get("story-kept", StoryProseKind.Opening, ProseSource.Pool));
            Assert.Null(ledger.Get("story-gone", StoryProseKind.Opening, ProseSource.Pool));
            Assert.Null(ledger.Get("story-gone", StoryProseKind.Opening, ProseSource.Cli));
            Assert.Null(ledger.Get("story-gone", StoryProseKind.Resolution, ProseSource.Pool));
        }

        /// <summary>
        /// A document the canned pool built is labelled as the pool's.
        /// </summary>
        /// <remarks>
        /// <see cref="FlavorDocument.Source"/> defaults to <see cref="ProseSource.Cli"/>, so the
        /// pool has to overwrite it and this is what says it does. A mislabelled pool document is
        /// not a cosmetic slip: under the first-write-wins rule above it would take the slot the
        /// model's prose is added to, and hold it for the life of the story — the model's writing
        /// would arrive, find the slot taken, and be dropped, permanently and silently.
        /// </remarks>
        [Fact]
        public void ADocumentFromTheCannedPool_IsLabelledAsThePools()
        {
            var pool = new StaticPoolProvider(
                new System.Guid("3c1d5f60-0000-4000-8000-0123456789ab"), RegionTheme.Eu,
                FlavorValidator.Create(null, NullFlavorLog.Instance), NullFlavorLog.Instance);

            var request = new FlavorRequest
            {
                Date = new SimDate(2031, 5, 1),
                Theme = RegionTheme.Eu,
                Snapshot = new CitySnapshot
                {
                    Date = new SimDate(2031, 5, 1),
                    Districts = new List<DistrictSnapshot>
                    {
                        new DistrictSnapshot { Id = "district-00", Name = "Harbour", Population = 30_000 }
                    }
                }
            };

            FlavorDocument document = pool.Generate(request);

            Assert.NotNull(document);
            Assert.Equal(ProseSource.Pool, document!.Source);
        }

        /// <summary>
        /// A story id carrying the key separator is still recovered whole.
        /// </summary>
        /// <remarks>
        /// Engine-minted ids would not contain it today. This pins the key layout that makes that
        /// irrelevant — the id goes last, so recovering it is an index rather than a split, and a
        /// future id format cannot quietly make <c>RetainOnly</c> drop the wrong rows.
        /// </remarks>
        [Fact]
        public void AStoryIdContainingTheKeySeparator_IsStillMatchedWhole()
        {
            var ledger = new StoryProseLedger();
            ledger.Absorb(Payload(Prose("story|odd|1", "x", ProseSource.Pool)));

            ledger.RetainOnly(new HashSet<string> { "story|odd|1" });

            Assert.Equal(1, ledger.Count);
            Assert.NotNull(ledger.Get("story|odd|1", StoryProseKind.Opening, ProseSource.Pool));
        }
    }
}
