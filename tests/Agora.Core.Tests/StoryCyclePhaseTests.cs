using System;
using System.Collections.Generic;
using System.IO;
using Agora.Core.Contracts;
using Agora.Core.Engine;
using Agora.Core.Engine.Affinity;
using Agora.Core.Events.Scheduler;
using Agora.Core.Stories;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The wave-4 spine's own guards: the story cadence, the affinity term's wiring, and the two
    /// contract pins the spine's doc comments assert.
    /// </summary>
    /// <remarks>
    /// Everything here is about the <i>seam</i>, never about a lane's body. <see cref="StoryCycle"/>
    /// is a stub at this commit and these tests must keep passing when lane 4a replaces it wholesale —
    /// so nothing below asserts what a cycle produces, only when one is asked to run and what the
    /// engine does with what it hands back.
    /// </remarks>
    public sealed class StoryCyclePhaseTests
    {
        private static readonly SimDate Start = new SimDate(1990, 1, 1);

        private static EngineTuning Tuning() => EngineTuning.Default;

        private static TickPlan PlanAt(int elapsedMonths, EngineTuning tuning) =>
            TickPlanner.Plan(Start, Start.AddMonths(elapsedMonths), new AgoraSettings(),
                             null, false, false, tuning);

        // ---------------------------------------------------------------- the cadence

        /// <summary>
        /// Draft on phase 0, resolve on phase 1, and nothing on the months in between.
        /// </summary>
        /// <remarks>
        /// Driven off <c>stories.cycleMonths</c> read from tuning rather than a literal 2, so a
        /// balance pass that widens the cadence moves this test with it instead of turning it red for
        /// a reason unrelated to what it guards.
        /// </remarks>
        [Fact]
        public void DraftIsPhaseZero_ResolveIsPhaseOne()
        {
            EngineTuning tuning = Tuning();
            int cycle = tuning.Stories.CycleMonths;
            Assert.True(cycle >= 2, "A cadence below 2 has no resolve phase; see TickPlanner.");

            for (int elapsed = 0; elapsed < cycle * 4; elapsed++)
            {
                TickPlan plan = PlanAt(elapsed, tuning);
                int phase = elapsed % cycle;

                Assert.Equal(phase == 0, plan.IsStoryDraft);
                Assert.Equal(phase == 1, plan.IsStoryResolve);
            }
        }

        /// <summary>
        /// A story is due the month <b>after</b> it drafts, whatever the cadence — the resolve phase
        /// is 1, not <c>cycleMonths - 1</c>.
        /// </summary>
        /// <remarks>
        /// The two are the same number only because the cadence ships at 2, which is exactly what
        /// makes the confusion survivable and therefore dangerous. <c>StoryAssembler.NewStory</c>
        /// fixes a story's life at <c>cycleMonths - 1</c> months and the resolve phase has to land on
        /// the month it is actually due; reading the phase as "the month before the next draft" would
        /// stretch the window a player is scored over to match the cadence. Wave 3 made that mistake
        /// and roughly forty authored thresholds had to be re-derived by hand.
        /// </remarks>
        [Fact]
        public void ResolvePhaseIsOne_EvenWhenTheCadenceIsWiderThanTwo()
        {
            EngineTuning tuning = EngineTuning.FromJson("{\"stories\":{\"cycleMonths\":4}}");

            Assert.True(PlanAt(0, tuning).IsStoryDraft);
            Assert.True(PlanAt(1, tuning).IsStoryResolve);

            // The two idle months. Not the resolve phase, and in particular month 3 — which is
            // cycleMonths - 1 — is not it either.
            Assert.False(PlanAt(2, tuning).IsStoryResolve);
            Assert.False(PlanAt(3, tuning).IsStoryResolve);
            Assert.True(PlanAt(4, tuning).IsStoryDraft);
        }

        /// <summary>
        /// A cadence of 1 is floored to 2 rather than producing a save where nothing ever resolves.
        /// </summary>
        /// <remarks>
        /// At a cadence of 1 every month is phase 0, so the resolve phase never arrives, every story
        /// drafted sits pending until the stranded sweep abandons it, and the player is never scored
        /// on anything. Reachable only from a hand-edited tuning file, which is precisely the case
        /// the floor exists for.
        /// </remarks>
        [Fact]
        public void ACadenceBelowTwoIsFloored_SoTheResolvePhaseStillArrives()
        {
            EngineTuning tuning = EngineTuning.FromJson("{\"stories\":{\"cycleMonths\":1}}");

            Assert.True(PlanAt(0, tuning).IsStoryDraft);
            Assert.True(PlanAt(1, tuning).IsStoryResolve);
            Assert.False(PlanAt(1, tuning).IsStoryDraft);
        }

        /// <summary>Both phases are gated on the engine tick, like every other cadence in the file.</summary>
        [Fact]
        public void NeitherPhaseFiresOnAMonthTheEngineSkips()
        {
            EngineTuning tuning = EngineTuning.FromJson("{\"scheduler\":{\"tickIntervalMonths\":3}}");

            for (int elapsed = 0; elapsed < 12; elapsed++)
            {
                TickPlan plan = PlanAt(elapsed, tuning);
                if (plan.IsEngineTick) continue;

                Assert.False(plan.IsStoryDraft);
                Assert.False(plan.IsStoryResolve);
            }
        }

        /// <summary>A date before the save started is due nothing, story phases included.</summary>
        [Fact]
        public void ADateBeforeTheSaveStartedDraftsNothing()
        {
            TickPlan plan = TickPlanner.Plan(Start, Start.AddMonths(-3), new AgoraSettings(),
                                             null, false, false, Tuning());

            Assert.False(plan.IsStoryDraft);
            Assert.False(plan.IsStoryResolve);
        }

        // ---------------------------------------------------------------- the affinity term

        /// <summary>
        /// With no story pressures the story term is exactly zero, so a save with the layer off
        /// scores identically to one built before the term existed.
        /// </summary>
        [Fact]
        public void NoStoryPressures_LeavesTheStoryComponentAtZero()
        {
            AffinityResult result = ComputeWith(new List<StoryPressureContribution>());

            foreach (BlocAffinity row in result.Affinities)
            {
                Assert.Equal(0.0, row.StoryComponent, 12);
            }
        }

        /// <summary>
        /// <b>Government credit reaches the governing party and nobody else.</b>
        /// </summary>
        /// <remarks>
        /// The shape, not the coefficient: the assertion is that the governing party's story
        /// component moves in the credit's direction and the opposition's does not move at all, which
        /// stays true across every balance pass. Pinning the number would go red on the next one for
        /// a reason unrelated to what this guards. The relative loss for the opposition is left to
        /// share normalisation on purpose — paying it explicitly would count the movement twice and
        /// make the swing depend on how many parties happen to exist.
        /// </remarks>
        [Fact]
        public void GovernmentCredit_MovesOnlyTheGoverningParty()
        {
            var pressures = new List<StoryPressureContribution>
            {
                new StoryPressureContribution
                {
                    StoryId = "story-1",
                    // Centre, so salience contributes nothing and credit is the only mover.
                    Pressure = IssuePosition.Centre,
                    GovernmentCredit = 1.0,
                    Severity = 5,
                    OpenedDate = Start
                }
            };

            AffinityResult result = ComputeWith(pressures);

            bool sawGoverning = false;
            bool sawOpposition = false;

            foreach (BlocAffinity row in result.Affinities)
            {
                if (row.PartyId == GoverningPartyId)
                {
                    sawGoverning = true;
                    Assert.True(row.StoryComponent > 0.0,
                        "A delivered story must pull voters toward the government.");
                }
                else
                {
                    sawOpposition = true;
                    Assert.Equal(0.0, row.StoryComponent, 12);
                }
            }

            Assert.True(sawGoverning && sawOpposition, "The fixture must contain both sides.");
        }

        /// <summary>Blame is the mirror of credit, and lands on the same party.</summary>
        [Fact]
        public void GovernmentBlame_PushesVotersAwayFromTheGoverningParty()
        {
            var pressures = new List<StoryPressureContribution>
            {
                new StoryPressureContribution
                {
                    StoryId = "story-1",
                    Pressure = IssuePosition.Centre,
                    GovernmentCredit = -1.0,
                    Severity = 5,
                    OpenedDate = Start
                }
            };

            foreach (BlocAffinity row in ComputeWith(pressures).Affinities)
            {
                if (row.PartyId == GoverningPartyId) Assert.True(row.StoryComponent < 0.0);
                else Assert.Equal(0.0, row.StoryComponent, 12);
            }
        }

        /// <summary>
        /// The story term has its own budget: the summed contribution is bounded by
        /// <c>affinity.storyPressureWeight</c> however many stories are open.
        /// </summary>
        /// <remarks>
        /// Read from tuning rather than asserted as a literal, and stated as a bound rather than as a
        /// value — the clamp is the property, the coefficient is a balance decision.
        /// </remarks>
        [Fact]
        public void TheStoryTermIsBoundedByItsOwnWeight_HoweverManyStoriesAreOpen()
        {
            EngineTuning tuning = Tuning();
            double bound = tuning.Affinity.StoryPressureWeight;

            var pressures = new List<StoryPressureContribution>();
            for (int i = 0; i < 40; i++)
            {
                pressures.Add(new StoryPressureContribution
                {
                    StoryId = "story-" + i.ToString("D2"),
                    Pressure = IssuePosition.Centre.With(Issue.Services, 1.0),
                    GovernmentCredit = 1.0,
                    Severity = 5,
                    OpenedDate = Start
                });
            }

            foreach (BlocAffinity row in ComputeWith(pressures, tuning).Affinities)
            {
                Assert.InRange(row.StoryComponent, -bound, bound);
            }
        }

        /// <summary>
        /// Story pressures are sorted before they are summed, so the caller's ordering cannot reach
        /// the result.
        /// </summary>
        /// <remarks>
        /// The canonical determinism shape: the same set in two orders must score identically. This
        /// is the bug <c>Agora.Core/CLAUDE.md</c> calls the most common one in the repo, and the
        /// story list is a fresh place for it to appear.
        /// </remarks>
        [Fact]
        public void StoryPressureOrderDoesNotReachTheResult()
        {
            var forward = new List<StoryPressureContribution>
            {
                Contribution("story-a", Issue.Services, 0.7),
                Contribution("story-b", Issue.Transit, -0.4),
                Contribution("story-c", Issue.Environment, 0.9)
            };

            var reversed = new List<StoryPressureContribution>
            {
                Contribution("story-c", Issue.Environment, 0.9),
                Contribution("story-b", Issue.Transit, -0.4),
                Contribution("story-a", Issue.Services, 0.7)
            };

            Assert.Equal(Canonical(ComputeWith(forward)), Canonical(ComputeWith(reversed)));
        }

        // ---------------------------------------------------------------- the contract pins

        /// <summary>
        /// <b>The shipped <c>engine_tuning.json</c> declares the schema version its schema pins.</b>
        /// </summary>
        /// <remarks>
        /// Landed with the wave-4 bump rather than after it. Wave 3's handoff records a comment that
        /// claimed a test pinned two constants together when no such test existed, and the two had
        /// already drifted once on the strength of the claim. Both sides are read from the files, so
        /// this cannot go stale at the next bump — it moves with it or it fails.
        /// </remarks>
        [Fact]
        public void TheShippedTuningAndItsSchemaAgreeOnTheVersion()
        {
            int declared = IntProperty(Path.Combine(RepoRoot(), "data", "engine_tuning.json"),
                                       "schemaVersion");

            using System.Text.Json.JsonDocument schema = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(RepoRoot(), "data", "schemas",
                                              "engine_tuning.schema.json")));

            int pinned = schema.RootElement
                .GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32();

            Assert.Equal(pinned, declared);
        }

        /// <summary>
        /// The three story-pressure weights are all present in the shipped tuning, and its schema
        /// requires all three.
        /// </summary>
        /// <remarks>
        /// The <c>affinity</c> section is <c>additionalProperties: false</c> with an explicit
        /// <c>required</c> list, so a key added to one side and not the other fails the schema suite
        /// rather than silently falling back to a compiled-in default at runtime — which is the whole
        /// reason the version moved.
        /// </remarks>
        [Fact]
        public void TheStoryPressureWeightsExistOnBothSidesOfTheContract()
        {
            string[] keys = { "storyPressureWeight", "storyPressureWeightMuted", "storyPressureWeightLoud" };

            using System.Text.Json.JsonDocument data = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(RepoRoot(), "data", "engine_tuning.json")));
            System.Text.Json.JsonElement affinity = data.RootElement.GetProperty("affinity");

            using System.Text.Json.JsonDocument schema = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(RepoRoot(), "data", "schemas",
                                              "engine_tuning.schema.json")));
            System.Text.Json.JsonElement section =
                schema.RootElement.GetProperty("properties").GetProperty("affinity");

            var required = new List<string>();
            foreach (System.Text.Json.JsonElement r in section.GetProperty("required").EnumerateArray())
                required.Add(r.GetString() ?? "");

            foreach (string key in keys)
            {
                Assert.True(affinity.TryGetProperty(key, out _),
                    "data/engine_tuning.json is missing " + key + ".");
                Assert.True(section.GetProperty("properties").TryGetProperty(key, out _),
                    "The schema does not declare " + key + ".");
                Assert.Contains(key, required);
            }
        }

        /// <summary>
        /// <b>The timeline schema declares every property its loader reads.</b> The pair this wave
        /// closed is <c>issuePressure</c>.
        /// </summary>
        /// <remarks>
        /// The schema sets <c>additionalProperties: false</c>, so a key the loader accepts and the
        /// schema does not is a file that works at runtime and fails the schema suite — the exact
        /// mismatch wave 3 reported twice and routed here.
        /// </remarks>
        [Fact]
        public void TheTimelineSchemaDeclaresIssuePressure()
        {
            using System.Text.Json.JsonDocument schema = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(RepoRoot(), "data", "schemas", "timeline.schema.json")));

            System.Text.Json.JsonElement properties = schema.RootElement
                .GetProperty("properties").GetProperty("events")
                .GetProperty("items").GetProperty("properties");

            Assert.True(properties.TryGetProperty("issuePressure", out System.Text.Json.JsonElement pressure));

            // Every axis Issues.All knows, and nothing else: a typo'd axis must fail the schema
            // rather than be read as an unstated issue.
            System.Text.Json.JsonElement axes = pressure.GetProperty("properties");
            for (int i = 0; i < Issues.All.Count; i++)
            {
                Assert.True(axes.TryGetProperty(Issues.ToKey(Issues.All[i]), out _),
                    "The issuePressure object does not declare " + Issues.ToKey(Issues.All[i]) + ".");
            }

            Assert.False(pressure.GetProperty("additionalProperties").GetBoolean());
        }

        /// <summary>
        /// The timeline schema version is <b>unchanged</b>, and the shipped catalogs still declare it.
        /// </summary>
        /// <remarks>
        /// Deliberate, and pinned so that a later change has to argue with this test rather than drift
        /// past it. <c>TimelineCatalogLoader.SupportedSchemaVersion</c> is a hard equality, so a bump
        /// is a rejection rather than a migration: it would break an old build reading a new file,
        /// which is the one direction that currently works, in exchange for nothing —
        /// <c>issuePressure</c> is optional and the loader has always read it. The wave-3 precedent is
        /// <c>engine_tuning</c>, which bumped because a section gained a <b>required</b> property.
        /// </remarks>
        [Fact]
        public void TheTimelineSchemaVersionDidNotMove()
        {
            using System.Text.Json.JsonDocument schema = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(RepoRoot(), "data", "schemas", "timeline.schema.json")));

            int pinned = schema.RootElement.GetProperty("properties")
                .GetProperty("schemaVersion").GetProperty("const").GetInt32();

            Assert.Equal(Agora.Core.Events.Catalog.TimelineCatalogLoader.SupportedSchemaVersion, pinned);

            string[] files = { "timeline_global.json", "timeline_eu.json", "timeline_na.json" };
            foreach (string file in files)
            {
                Assert.Equal(pinned, IntProperty(Path.Combine(RepoRoot(), "data", file), "schemaVersion"));
            }
        }

        // ---------------------------------------------------------------- fixtures

        private const string GoverningPartyId = "party-gov";
        private const string OppositionPartyId = "party-opp";

        private static StoryPressureContribution Contribution(string id, Issue issue, double value) =>
            new StoryPressureContribution
            {
                StoryId = id,
                Pressure = IssuePosition.Centre.With(issue, value),
                GovernmentCredit = 0.0,
                Severity = 3,
                OpenedDate = Start
            };

        /// <summary>
        /// Two blocs, two parties on identical platforms, one of them governing. Kept local rather
        /// than added to <c>StoryTestFixtures</c> so the spine touches no file a lane might also
        /// open.
        /// </summary>
        /// <remarks>
        /// <b>The two platforms are identical on purpose.</b> Every other term then scores the same
        /// for both parties, so any difference between their rows is the story term and nothing else.
        /// The noise term is switched off for the same reason: a seeded draw per (bloc, party) would
        /// separate them by a tenth of a point and the credit assertions would be reading it rather
        /// than the credit.
        /// </remarks>
        private static AffinityResult ComputeWith(List<StoryPressureContribution> pressures,
                                                  EngineTuning? tuning = null)
        {
            EngineTuning t = tuning ?? EngineTuning.FromJson("{\"affinity\":{\"noiseSigma\":0.0}}");

            var key = new BlocKey(WealthTier.Middle, EducationTier.Educated, AgeBand.Adult);
            var platform = new IssuePosition(0.2, -0.1, 0.3, 0.1, 0.0, -0.2);

            var request = new AffinityRequest
            {
                SaveGuid = new Guid("11111111-2222-3333-4444-555555555555"),
                Date = Start.AddMonths(1),
                Blocs = new List<Bloc>
                {
                    MakeBloc("district-a", key),
                    MakeBloc("district-b", new BlocKey(WealthTier.Low, EducationTier.Uneducated, AgeBand.Adult))
                },
                Parties = new List<Party>
                {
                    MakeParty(GoverningPartyId, platform),
                    MakeParty(OppositionPartyId, platform)
                },
                Government = new Coalition
                {
                    Id = "coalition-1",
                    Status = CoalitionStatus.Governing,
                    LeadPartyId = GoverningPartyId,
                    MemberPartyIds = new List<string> { GoverningPartyId }
                },
                StoryPressures = pressures
            };

            return AffinityEngine.Compute(request, t);
        }

        private static Bloc MakeBloc(string districtId, BlocKey key) => new Bloc
        {
            DistrictId = districtId,
            Key = key,
            Population = 1000,
            PopulationShare = 0.5,
            EligibleVoters = 800,
            Weights = IssueWeights.Uniform,
            Ideal = new IssuePosition(0.2, -0.1, 0.3, 0.1, 0.0, -0.2)
        };

        private static Party MakeParty(string id, IssuePosition platform) => new Party
        {
            Id = id,
            Name = id,
            Status = PartyStatus.Active,
            Platform = platform
        };

        /// <summary>
        /// A canonical, culture-invariant rendering of the whole result. Hashing the serialized form
        /// rather than comparing fields by hand is the pattern <c>tests/CLAUDE.md</c> prescribes,
        /// because a hand-written assertion silently stops covering any field added later.
        /// </summary>
        private static string Canonical(AffinityResult result)
        {
            var sb = new System.Text.StringBuilder();
            foreach (BlocAffinity row in result.Affinities)
            {
                sb.Append(row.DistrictId).Append('|')
                  .Append(row.Bloc).Append('|')
                  .Append(row.PartyId).Append('|')
                  .Append(row.StoryComponent.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                  .Append('|')
                  .Append(row.Affinity.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                  .Append('\n');
            }

            return sb.ToString();
        }

        private static int IntProperty(string path, string name)
        {
            using System.Text.Json.JsonDocument doc =
                System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.GetProperty(name).GetInt32();
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Agora.sln")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            return dir!.FullName;
        }
    }
}
