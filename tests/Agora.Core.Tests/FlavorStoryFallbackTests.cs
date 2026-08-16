// Requires the StaticPoolProvider.cs / StaticPoolContent.cs / FlavorValidator.cs / FlavorRequest.cs
// <Compile Link> lines in Agora.Core.Tests.csproj (see the comment there for why).

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Stories;
using Agora.Core.Stories.Catalog;
using Agora.Mod.Llm;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Story prose with no Claude CLI installed — which is most players, all game, and everyone for
    /// the first month of every save.
    ///
    /// <para>
    /// The canned pool is the fallback for the fallback, so its story prose is held to the same shape
    /// the model is asked for: a headline that is the <b>major</b> event's name, and an article that
    /// walks the story's slots in order and says what each event was. The slot briefs carry that text
    /// straight off the civic catalog (<c>AgoraRuntime.BuildStoryBrief</c> fills <c>Title</c> from
    /// <see cref="CivicEvent.Name"/> and <c>HeadlineBrief</c> from <see cref="CivicEvent.Description"/>),
    /// so the fixtures below reach it through <see cref="CivicEventCatalog.Find"/> rather than
    /// restating it — a test written against its own copy of the prose would keep passing after the
    /// pool stopped reading the catalog's.
    /// </para>
    ///
    /// <para>
    /// The determinism half is <c>tests/CLAUDE.md</c>'s canonical pattern, and it matters more here
    /// than it looks: the pool is polled every month for the life of a story, and prose that
    /// reshuffled between two identical requests would be the one visible thing that did not survive
    /// a reload.
    /// </para>
    /// </summary>
    public class FlavorStoryFallbackTests
    {
        private static readonly Guid Save = new Guid("9a4f10d2-0000-4000-8000-abcdefabcdef");
        private static readonly SimDate Founded = new SimDate(2018, 4, 1);
        private static readonly SimDate Date = new SimDate(2031, 5, 1);

        private const string StoryId = "story-harbour-2031-05";
        private const string MajorId = "civic-tram-bridge-stalls";
        private const string FirstMinorId = "civic-clinic-queue-grows";
        private const string SecondMinorId = "civic-wharf-lights-fail";

        /// <summary>
        /// Three synthetic civic events. Short prose on purpose: the whole article has to fit inside
        /// <c>FlavorCacheMigration.StoryArticleMaxLength</c>, and a fixture that pushed it over would
        /// be testing the pool's length handling rather than what it wrote.
        /// </summary>
        private static readonly CivicEventCatalog Events = new CivicEventCatalog(
            new List<CivicEvent>
            {
                new CivicEvent
                {
                    Id = MajorId,
                    Severity = 4,
                    Name = "The tram bridge stalls",
                    Description = "Work stopped at the third pier and nobody will say for how long."
                },
                new CivicEvent
                {
                    Id = FirstMinorId,
                    Severity = 2,
                    Name = "The clinic queue grows",
                    Description = "Two hours on a Tuesday morning, and the ridge has no clinic at all."
                },
                new CivicEvent
                {
                    Id = SecondMinorId,
                    Severity = 1,
                    Name = "The wharf lights fail",
                    Description = "A whole row of them, out since the storm."
                }
            },
            new List<string>());

        private static CivicEvent Event(string id)
        {
            CivicEvent? found = Events.Find(id);
            Assert.NotNull(found);
            return found!;
        }

        private static StaticPoolProvider Pool() =>
            new StaticPoolProvider(Save, RegionTheme.Eu,
                                   FlavorValidator.Create(null, NullFlavorLog.Instance),
                                   NullFlavorLog.Instance);

        // ---- fixtures ------------------------------------------------------------------------------

        /// <summary>
        /// One slot, filled from the catalog exactly as the runtime fills it.
        /// </summary>
        private static StorySlotBrief Slot(string eventId, bool isMajor, string outcomeWord)
        {
            CivicEvent authored = Event(eventId);
            return new StorySlotBrief
            {
                EventId = eventId,
                IsMajor = isMajor,
                Title = authored.Name,
                HeadlineBrief = authored.Description,
                OutcomeWord = outcomeWord
            };
        }

        /// <summary>
        /// One story: a major and two minors, in the order <c>Story.Slots</c> holds them — major
        /// first, then by event id ordinal.
        /// </summary>
        private static StoryBrief Story(bool resolved)
        {
            return new StoryBrief
            {
                StoryId = StoryId,
                IsResolved = resolved,
                OutcomeWord = resolved ? "success" : "",
                Slots = new List<StorySlotBrief>
                {
                    Slot(MajorId, true, resolved ? "met" : ""),
                    Slot(FirstMinorId, false, resolved ? "met" : ""),
                    Slot(SecondMinorId, false, resolved ? "not met" : "")
                }
            };
        }

        private static FlavorRequest Request(bool resolved)
        {
            var request = new FlavorRequest
            {
                Date = Date,
                Reason = FlavorWakeReason.StoryDraft,
                Theme = RegionTheme.Eu,
                ArticleCount = FlavorRequest.DefaultArticleCount,
                Snapshot = new CitySnapshot
                {
                    Date = Date,
                    Population = 140_000,
                    Happiness = 47.0,
                    Districts = new List<DistrictSnapshot>
                    {
                        new DistrictSnapshot
                        {
                            Id = "district-harbour",
                            Name = "Harbour",
                            Happiness = 41.0,
                            Population = 20_000
                        }
                    }
                }
            };

            request.Parties.Add(new PartyBrief
            {
                PartyId = "party-riverside",
                ArchetypeId = "archetype-00",
                CoreGrievance = Issues.All[0],
                StatusWord = "in opposition",
                FoundedDate = Founded
            });

            request.Stories.Add(Story(resolved));
            return request;
        }

        // ---- the golden ------------------------------------------------------------------------------

        [Fact]
        public void LiveStory_IsWrittenIntoStoriesAndNotResolutions()
        {
            FlavorDocument? document = Pool().Generate(Request(resolved: false));

            Assert.NotNull(document);
            Assert.Equal(StoryId, Assert.Single(document!.Stories).StoryId);
            Assert.Empty(document.Resolutions);
            Assert.Equal(ProseSource.Pool, document.Source);
        }

        [Fact]
        public void ResolvedStory_IsWrittenIntoResolutionsAndNotStories()
        {
            // Which collection an entry lands in is the whole of the difference between opening prose
            // and closing prose on the dashboard, and it is one bool on the brief.
            FlavorDocument? document = Pool().Generate(Request(resolved: true));

            Assert.NotNull(document);
            Assert.Equal(StoryId, Assert.Single(document!.Resolutions).StoryId);
            Assert.Empty(document.Stories);
        }

        [Fact]
        public void Headline_IsTheMajorEventsName()
        {
            // The major slot is the story's lead. A headline drawn from a minor would put the wharf
            // lights above a stalled bridge, which reads as a bug rather than as an editorial choice.
            FlavorDocument? document = Pool().Generate(Request(resolved: false));

            Assert.NotNull(document);
            Assert.Equal(Event(MajorId).Name, Assert.Single(document!.Stories).Headline);
        }

        [Fact]
        public void Article_CarriesEverySlotsNameAndDescriptionInSlotOrder()
        {
            FlavorDocument? document = Pool().Generate(Request(resolved: false));

            Assert.NotNull(document);
            string article = Assert.Single(document!.Stories).Article;

            int previous = -1;
            string[] slotIds = { MajorId, FirstMinorId, SecondMinorId };
            for (int i = 0; i < slotIds.Length; i++)
            {
                CivicEvent authored = Event(slotIds[i]);

                int nameAt = article.IndexOf(authored.Name, StringComparison.Ordinal);
                Assert.True(nameAt >= 0, "the article never names '" + authored.Id + "': " + article);
                Assert.True(nameAt > previous,
                            "'" + authored.Id + "' is out of slot order in the article: " + article);
                previous = nameAt;

                Assert.Contains(authored.Description, article, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void TheSameRequestTwice_ProducesAByteIdenticalDocument()
        {
            // The canonical determinism pattern: two runs from identical inputs, compared as one
            // serialized string rather than field by field, so a field a hand-written assertion
            // forgot cannot drift between polls.
            FlavorDocument? first = Pool().Generate(Request(resolved: false));
            FlavorDocument? second = Pool().Generate(Request(resolved: false));

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal(Hash(first!), Hash(second!));
        }

        [Fact]
        public void AResolvedStoryReadsDifferentlyFromALiveOne()
        {
            // The negative half. Without it, a pool that ignored the brief entirely and filed one
            // fixed paragraph would satisfy every assertion above.
            FlavorDocument? live = Pool().Generate(Request(resolved: false));
            FlavorDocument? resolved = Pool().Generate(Request(resolved: true));

            Assert.NotNull(live);
            Assert.NotNull(resolved);
            Assert.NotEqual(Assert.Single(live!.Stories).Article,
                            Assert.Single(resolved!.Resolutions).Article);
        }

        // ---- helpers ---------------------------------------------------------------------------------

        /// <summary>
        /// The whole document as one string, then hashed. Everything the document carries is written
        /// out — a serialization that skipped a collection would be a determinism test with a hole in
        /// exactly the place the new collections went.
        /// </summary>
        private static string Hash(FlavorDocument document)
        {
            var sb = new StringBuilder();
            sb.Append(document.SchemaVersion).Append('|')
              .Append(document.GeneratedAtSimDateText).Append('|')
              .Append(document.Source).Append('\n');

            for (int i = 0; i < document.PartyFlavor.Count; i++)
            {
                PartyFlavorEntry p = document.PartyFlavor[i];
                sb.Append("party|").Append(p.PartyId).Append('|').Append(p.Name).Append('|')
                  .Append(p.ShortName).Append('|').Append(p.Description).Append('|')
                  .Append(p.Slogan).Append('\n');
            }

            for (int i = 0; i < document.FactionFlavor.Count; i++)
            {
                FactionFlavorEntry f = document.FactionFlavor[i];
                sb.Append("faction|").Append(f.FactionId).Append('|').Append(f.PartyId).Append('|')
                  .Append(f.Name).Append('|').Append(f.ShortName).Append('|')
                  .Append(f.Description).Append('|').Append(f.LeaderName).Append('\n');
            }

            for (int i = 0; i < document.Articles.Count; i++)
            {
                ArticleEntry a = document.Articles[i];
                sb.Append("article|").Append(a.Id).Append('|').Append(a.Outlet).Append('|')
                  .Append(a.Headline).Append('|').Append(a.Body).Append('|').Append(a.Tone).Append('|')
                  .Append(a.EventId).Append('|').Append(a.DistrictId).Append('|')
                  .Append(a.PartyId).Append('\n');
            }

            for (int i = 0; i < document.EventProse.Count; i++)
            {
                EventProseEntry e = document.EventProse[i];
                sb.Append("eventProse|").Append(e.EventId).Append('|').Append(e.LocalAngle).Append('\n');
            }

            Append(sb, "story", document.Stories);
            Append(sb, "resolution", document.Resolutions);

            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
            }
        }

        private static void Append(StringBuilder sb, string label, List<StoryProseEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                StoryProseEntry s = entries[i];
                sb.Append(label).Append('|').Append(s.StoryId).Append('|').Append(s.Headline)
                  .Append('|').Append(s.Article).Append('\n');
            }
        }
    }
}
