using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Stories;
using Agora.Core.Tuning;
using Agora.Mod.Persistence;
using Agora.Mod.Sensors;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// A whole story arc — open, respond, resolve, pay — run twice from identical seeds and compared
    /// as a hash.
    ///
    /// <para>
    /// This is the canonical pattern from <c>tests/CLAUDE.md</c>, and it is here rather than only in
    /// the per-lane files because the failure it catches lives <i>between</i> lanes: a draft that is
    /// deterministic and a resolution that is deterministic can still compose into an arc that is not,
    /// if the second reads something the first left in collection order. Field-by-field assertions
    /// cover the fields whoever wrote them thought of; a hash over the serialized arc covers the ones
    /// they did not.
    /// </para>
    /// </summary>
    public class StoryArcDeterminismTests
    {
        private static readonly SimDate Open = StoryTestFixtures.March1994;
        private static readonly EngineTuning Tuning = EngineTuning.Default;

        private static SimDate Resolves => Open.AddMonths(Tuning.Stories.CycleMonths);

        /// <summary>
        /// A catalog whose events all trigger manually — so the pool a run states is the pool it draws
        /// from — and whose checks are absolute happiness reads, so the resolution month's city
        /// decides them.
        /// </summary>
        private static List<CivicEvent> Catalog()
        {
            var catalog = new List<CivicEvent>();

            for (int i = 0; i < 4; i++)
            {
                catalog.Add(Authored("evt-major-" + i.ToString("00"), Tuning.Stories.MajorSeverityThreshold, i));
            }

            for (int i = 0; i < 14; i++)
            {
                catalog.Add(Authored("evt-minor-" + i.ToString("00"),
                                     Math.Max(1, Tuning.Stories.MajorSeverityThreshold - 1), i));
            }

            catalog.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return catalog;
        }

        /// <summary>
        /// One authored event. The threshold varies with the index so that the resolution month's
        /// single happiness reading decides some slots met and others not — an arc where every slot
        /// came out the same way would hash identically under a scoring bug too.
        /// </summary>
        private static CivicEvent Authored(string id, int severity, int index)
        {
            return StoryTestFixtures.Event(id, severity, check: StoryTestFixtures.Check(
                StoryTestFixtures.Metric(MetricHistory.Happiness, Comparison.GreaterThanOrEqual,
                                         40.0 + (index * 5))));
        }

        private static List<EventPoolEntry> Pool(List<CivicEvent> catalog)
        {
            var pool = new List<EventPoolEntry>();
            for (int i = 0; i < catalog.Count; i++)
            {
                pool.Add(StoryTestFixtures.Pooled(catalog[i].Id, missStreak: i % 4));
            }

            return pool;
        }

        /// <summary>
        /// The player's decisions, derived from the slot's position in its story rather than drawn.
        /// A response is an appended record in the real engine and it is replayed, never re-solicited;
        /// deriving it here keeps the arc a pure function of its inputs, which is the property under
        /// test.
        /// </summary>
        private static SlotResponse ResponseFor(int slotIndex)
        {
            switch (slotIndex % 4)
            {
                case 0: return SlotResponse.Goal;
                case 1: return SlotResponse.Ignore;
                case 2: return SlotResponse.PowerOverride;
                default: return SlotResponse.Manual;
            }
        }

        /// <summary>
        /// Everything one run of the arc produced, in a shape a fingerprint can be taken over: the
        /// draft, the stories as they stood after the player answered, every resolution, and the
        /// power the arc paid out.
        /// </summary>
        private sealed class ArcRecord
        {
            public StoryDraftResult Draft { get; set; } = new StoryDraftResult();
            public List<Story> Answered { get; set; } = new List<Story>();
            public List<StoryResolutionResult> Resolutions { get; set; } =
                new List<StoryResolutionResult>();
            public int PowerDelta { get; set; }
        }

        /// <summary>
        /// Open → respond → resolve → pay, with no wall clock, no unnamed draw and no input the caller
        /// did not supply.
        /// </summary>
        private static ArcRecord RunArc(Guid save, double resolutionHappiness = 55.0)
        {
            List<CivicEvent> catalog = Catalog();
            PoliticalState prior = StoryTestFixtures.State(Open, Pool(catalog).ToArray());

            StoryReadContext atOpen = StoryTestFixtures.Context(
                StoryTestFixtures.City(Open, happiness: 50.0));

            var record = new ArcRecord
            {
                Draft = StoryAssembler.Draft(prior, catalog, atOpen, save, Open, Tuning)
            };

            // --- respond. The slot index within its own story decides, so the answers depend on the
            // draw and on nothing else.
            foreach (Story drafted in record.Draft.DraftedStories)
            {
                Story answered = drafted.Clone();
                for (int i = 0; i < answered.Slots.Count; i++)
                {
                    answered.Slots[i].Response = ResponseFor(i);
                    answered.Slots[i].ManualDeclared =
                        answered.Slots[i].Response == SlotResponse.Manual && (i % 8) == 3;
                    answered.Slots[i].BaselineMetric = 50.0;
                }

                record.Answered.Add(answered);
            }

            // --- resolve, one cycle later, against a city that has moved.
            StoryReadContext atResolution = StoryTestFixtures.Context(
                StoryTestFixtures.City(Resolves, happiness: resolutionHappiness),
                StoryTestFixtures.City(Open, happiness: 50.0));

            foreach (Story answered in record.Answered)
            {
                StoryResolutionResult resolution =
                    StoryResolution.Resolve(answered, catalog, atResolution, Tuning);
                record.Resolutions.Add(resolution);

                for (int i = 0; i < resolution.SlotOutcomes.Count; i++)
                {
                    StorySlot slot = answered.Slots[i];
                    CivicEvent civicEvent = Find(catalog, slot.EventId);

                    StoryTier tier = civicEvent.TierUnder(Tuning.Stories.MandatorySeverityThreshold,
                                                          Tuning.Stories.MajorSeverityThreshold);

                    record.PowerDelta += PoliticalPower.AwardFor(
                        resolution.SlotOutcomes[i], tier, slot.ManualDeclared, Tuning);
                }
            }

            return record;
        }

        private static CivicEvent Find(List<CivicEvent> catalog, string id)
        {
            foreach (CivicEvent civicEvent in catalog)
            {
                if (string.Equals(civicEvent.Id, id, StringComparison.Ordinal)) return civicEvent;
            }

            throw new Xunit.Sdk.XunitException("the arc drew '" + id + "', which is not in the catalog");
        }

        // --- the arc ------------------------------------------------------------------------------

        /// <summary>
        /// <b>Two identical runs, one hash.</b> Non-negotiable #3 says engine state is a pure function
        /// of its inputs; this is the assertion that makes the claim falsifiable across the whole
        /// story layer at once.
        /// </summary>
        [Fact]
        public void TheWholeArc_IsByteIdenticalFromIdenticalSeeds()
        {
            Assert.Equal(AgoraJson.Fingerprint(RunArc(StoryTestFixtures.Save)),
                         AgoraJson.Fingerprint(RunArc(StoryTestFixtures.Save)));
        }

        /// <summary>
        /// The seed reaches the whole arc, not merely the draft. A save whose draw differed but whose
        /// resolutions and payouts did not would mean the arc had stopped depending on which stories
        /// were actually drawn.
        /// </summary>
        [Fact]
        public void TheWholeArc_DependsOnTheSaveIdentity()
        {
            Assert.NotEqual(AgoraJson.Fingerprint(RunArc(StoryTestFixtures.Save)),
                            AgoraJson.Fingerprint(RunArc(StoryTestFixtures.OtherSave)));
        }

        /// <summary>
        /// And it depends on the city. A resolution month where nothing was met must not hash the same
        /// as one where everything was — the guard against an arc that is deterministic because it is
        /// inert.
        /// </summary>
        [Fact]
        public void TheWholeArc_DependsOnTheCityItResolvesAgainst()
        {
            Assert.NotEqual(
                AgoraJson.Fingerprint(RunArc(StoryTestFixtures.Save, resolutionHappiness: 0.0)),
                AgoraJson.Fingerprint(RunArc(StoryTestFixtures.Save, resolutionHappiness: 100.0)));
        }

        /// <summary>
        /// The arc actually did something. A hash comparison over two empty runs passes cheerfully, so
        /// this is the assertion that keeps the three above honest.
        /// </summary>
        [Fact]
        public void TheWholeArc_ReachesAVerdictOnEveryStoryItDrafted()
        {
            ArcRecord arc = RunArc(StoryTestFixtures.Save);

            Assert.NotEmpty(arc.Draft.DraftedStories);
            Assert.Equal(arc.Draft.DraftedStories.Count, arc.Resolutions.Count);

            foreach (StoryResolutionResult resolution in arc.Resolutions)
            {
                Assert.NotEqual(StoryOutcome.Pending, resolution.Outcome);
            }
        }
    }
}
