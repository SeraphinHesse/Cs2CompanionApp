using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Engine.Parties;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Engine packet 2 — party registry and the EU party lifecycle (split, merge, death, revival).
    ///
    /// <para>
    /// Probabilistic gates are pinned to 0.0 or 1.0 through <see cref="EngineTuning.FromJson"/> so a
    /// behavioural test asserts the <i>rule</i> rather than a lucky seed. The determinism and
    /// multi-cycle tests run on the shipped defaults, where the rolls actually matter.
    /// </para>
    /// </summary>
    public class PartyLifecycleTests
    {
        private static readonly Guid SaveA = new Guid("11111111-2222-3333-4444-555555555555");
        private static readonly Guid SaveB = new Guid("99999999-8888-7777-6666-555555555555");
        private static readonly SimDate Y1990 = new SimDate(1990, 1, 1);
        private static readonly SimDate Y1993 = new SimDate(1993, 1, 1);
        private static readonly SimDate Y1996 = new SimDate(1996, 1, 1);
        private static readonly SimDate Y2000 = new SimDate(2000, 1, 1);

        // ============================ Generation =================================================

        [Fact]
        public void GenerateInitial_ProducesTargetCount_WithUniqueIdsAndColours()
        {
            EngineTuning tuning = EngineTuning.Default;
            List<Party> parties = PartyRegistry.GenerateInitial(SaveA, Y1990, RegionTheme.Eu, tuning);

            Assert.Equal(tuning.Parties.TargetCountEu, parties.Count);

            var ids = new List<string>();
            var colours = new List<string>();
            var archetypes = new List<string>();
            foreach (Party p in parties)
            {
                Assert.DoesNotContain(p.Id, ids);
                Assert.DoesNotContain(p.ColorHex, colours);
                Assert.DoesNotContain(p.ArchetypeId, archetypes);
                ids.Add(p.Id);
                colours.Add(p.ColorHex);
                archetypes.Add(p.ArchetypeId);

                Assert.Equal(PartyStatus.Active, p.Status);
                Assert.Equal(Y1990, p.FoundedDate);
                Assert.Equal(0.0, p.LastVoteShare);
                Assert.Null(p.DissolvedDate);
                // Flavor-owned fields stay empty: the engine never invents prose.
                Assert.Equal("", p.Name);
                Assert.Equal("", p.Slogan);
            }
        }

        [Fact]
        public void GenerateInitial_KeepsEveryPairOfPlatformsLegiblyApart()
        {
            EngineTuning tuning = EngineTuning.Default;
            List<Party> parties = PartyRegistry.GenerateInitial(SaveA, Y1990, RegionTheme.Eu, tuning);

            for (int i = 0; i < parties.Count; i++)
            {
                for (int j = i + 1; j < parties.Count; j++)
                {
                    double d = parties[i].Platform.Distance(parties[j].Platform);
                    Assert.True(d >= tuning.Parties.MinPlatformDistance - 1e-12,
                        parties[i].Id + " and " + parties[j].Id + " are only " + d + " apart");
                }
            }
        }

        [Fact]
        public void SeparateFrom_ReachesTheMinimumEvenFromACornerOrAnIdenticalPlatform()
        {
            const double min = 0.15;

            // Identical platforms — no gap to push along at all.
            IssuePosition a = PartyPlatform.SeparateFrom(Uniform(0.0), Uniform(0.0), min, Issue.Transit, null);
            Assert.True(a.Distance(Uniform(0.0)) >= min - 1e-12);

            // The candidate sits on the ceiling and the gap points further into it, so the natural
            // push is clipped to nothing however far it is scaled: only the interior direction works.
            IssuePosition near = Uniform(0.9);
            IssuePosition b = PartyPlatform.SeparateFrom(Uniform(1.0), near, min, Issue.Growth, null);
            Assert.True(b.Distance(near) >= min - 1e-12,
                "corner-blocked separation reached only " + b.Distance(near));

            // A pair that is already far enough apart is returned untouched.
            IssuePosition far = PartyPlatform.SeparateFrom(Uniform(0.5), Uniform(-0.5), min, Issue.Services, null);
            Assert.Equal(Format(Uniform(0.5)), Format(far));
        }

        [Fact]
        public void GenerateInitial_IsDeterministicAndSaveSpecific()
        {
            EngineTuning tuning = EngineTuning.Default;

            string a1 = HashParties(PartyRegistry.GenerateInitial(SaveA, Y1990, RegionTheme.Eu, tuning));
            string a2 = HashParties(PartyRegistry.GenerateInitial(SaveA, Y1990, RegionTheme.Eu, tuning));
            string b = HashParties(PartyRegistry.GenerateInitial(SaveB, Y1990, RegionTheme.Eu, tuning));

            Assert.Equal(a1, a2);
            Assert.NotEqual(a1, b);
        }

        [Fact]
        public void GenerateInitial_Na_ProducesMajorsPlusMinors()
        {
            EngineTuning tuning = EngineTuning.Default;
            List<Party> parties = PartyRegistry.GenerateInitial(SaveA, Y1990, RegionTheme.Na, tuning);

            Assert.Equal(tuning.Parties.TargetCountNa + tuning.Parties.MinorPartyCountNa, parties.Count);
        }

        [Fact]
        public void NextPartyId_ContinuesPastDissolvedBrands()
        {
            var parties = new List<Party>
            {
                MakeParty("party-01"), MakeParty("party-02"), MakeParty("party-07")
            };
            parties[1].Status = PartyStatus.Dissolved;

            Assert.Equal("party-08", PartyRegistry.NextPartyId(parties));
        }

        [Fact]
        public void IncumbencyBonus_DecaysPerTermAndIsZeroOutOfPower()
        {
            EngineTuning tuning = EngineTuning.Default;

            Assert.Equal(0.0, PartyRegistry.IncumbencyBonus(0, tuning));
            Assert.Equal(tuning.Parties.IncumbencyBonus, PartyRegistry.IncumbencyBonus(1, tuning), 12);
            Assert.Equal(tuning.Parties.IncumbencyBonus * 0.70,
                         PartyRegistry.IncumbencyBonus(2, tuning), 12);
            Assert.True(PartyRegistry.IncumbencyBonus(3, tuning) < PartyRegistry.IncumbencyBonus(2, tuning));
        }

        // ============================ Death =======================================================

        [Fact]
        public void FirstSubThresholdResult_EndangersButDoesNotDissolve()
        {
            EngineTuning tuning = Quiet();
            List<Party> parties = Field(6);

            PartyLifecycleOutcome outcome = Advance(tuning, parties,
                Election(Y1993, parties, "party-01", 0.01));

            Party punished = Get(outcome, "party-01");
            Assert.Equal(PartyStatus.Endangered, punished.Status);
            Assert.Equal(1, punished.ConsecutiveElectionsBelowThreshold);
            Assert.True(PartyRegistry.IsOnBallot(punished));
            Assert.Null(punished.DissolvedDate);
        }

        [Fact]
        public void SecondConsecutiveSubThresholdResult_DissolvesTheParty()
        {
            EngineTuning tuning = Quiet();
            List<Party> parties = Field(6);

            PartyLifecycleOutcome first = Advance(tuning, parties,
                Election(Y1993, parties, "party-01", 0.01));
            PartyLifecycleOutcome second = Advance(tuning, new List<Party>(first.Parties),
                Election(Y1996, new List<Party>(first.Parties), "party-01", 0.02), Y1996);

            Party dead = Get(second, "party-01");
            Assert.Equal(PartyStatus.Dissolved, dead.Status);
            Assert.Equal(Y1996, dead.DissolvedDate);
            Assert.Equal(0, dead.SeatsHeld);
            Assert.False(PartyRegistry.IsOnBallot(dead));
            Assert.Contains(second.Changes, c => c.Kind == PartyChangeKind.Dissolved && c.PartyId == "party-01");
        }

        [Fact]
        public void RecoveringAboveTheThreshold_ResetsTheDeathCounter()
        {
            EngineTuning tuning = Quiet();
            List<Party> parties = Field(6);

            PartyLifecycleOutcome first = Advance(tuning, parties,
                Election(Y1993, parties, "party-01", 0.01));
            // No punished party this time: everyone lands on an even share, well above 5%.
            PartyLifecycleOutcome second = Advance(tuning, new List<Party>(first.Parties),
                Election(Y1996, new List<Party>(first.Parties), null, 0.0), Y1996);

            Party recovered = Get(second, "party-01");
            Assert.Equal(PartyStatus.Active, recovered.Status);
            Assert.Equal(0, recovered.ConsecutiveElectionsBelowThreshold);
            Assert.Contains(second.Changes, c => c.Kind == PartyChangeKind.Recovered && c.PartyId == "party-01");
        }

        [Fact]
        public void ResultInTheWarningBand_EndangersWithoutStartingTheDeathCount()
        {
            EngineTuning tuning = Quiet();
            List<Party> parties = Field(6);

            // 4% — under the 5% warning band, over the 3% death threshold.
            PartyLifecycleOutcome outcome = Advance(tuning, parties,
                Election(Y1993, parties, "party-01", 0.04));

            Party warned = Get(outcome, "party-01");
            Assert.Equal(PartyStatus.Endangered, warned.Status);
            Assert.Equal(0, warned.ConsecutiveElectionsBelowThreshold);
        }

        [Fact]
        public void DeathIsDeferredRatherThanEmptyingTheBallotBelowTheMinimum()
        {
            EngineTuning tuning = Quiet();           // minCountEu = 4
            List<Party> parties = Field(4);
            parties[0].ConsecutiveElectionsBelowThreshold = 1;
            parties[0].Status = PartyStatus.Endangered;

            PartyLifecycleOutcome outcome = Advance(tuning, parties,
                Election(Y1993, parties, "party-01", 0.01));

            Party held = Get(outcome, "party-01");
            Assert.Equal(PartyStatus.Endangered, held.Status);
            Assert.Equal(2, held.ConsecutiveElectionsBelowThreshold);   // the counter is kept
            Assert.Equal(4, PartyRegistry.OnBallotCount(outcome.Parties));
            Assert.Contains(outcome.Changes, c => c.Kind == PartyChangeKind.DeathDeferred);
        }

        // ============================ Revival =====================================================

        [Fact]
        public void DissolvedBrandRevives_WhenItsGrievanceResurgesAfterTheCooldown()
        {
            EngineTuning tuning = Quiet();
            List<Party> parties = Field(5);
            Party dead = parties[0];
            dead.Status = PartyStatus.Dissolved;
            dead.DissolvedDate = Y1993;
            dead.CoreGrievance = Issue.Environment;
            dead.SeatsHeld = 0;

            PartyLifecycleOutcome outcome = Advance(tuning, parties, null, Y2000,
                Grievance(Issue.Environment, 0.9));

            Party revived = Get(outcome, dead.Id);
            Assert.Equal(PartyStatus.Revived, revived.Status);
            Assert.Equal(1, revived.RevivalCount);
            Assert.Null(revived.DissolvedDate);
            Assert.Equal(0.0, revived.LastVoteShare);
            Assert.Equal(0, revived.ConsecutiveElectionsBelowThreshold);
            Assert.True(PartyRegistry.IsOnBallot(revived));
            Assert.Contains(outcome.Changes, c => c.Kind == PartyChangeKind.Revived);
        }

        [Fact]
        public void RevivalIsBlockedInsideTheCooldownWindow()
        {
            EngineTuning tuning = Quiet();          // revivalCooldownMonths = 36
            List<Party> parties = Field(5);
            parties[0].Status = PartyStatus.Dissolved;
            parties[0].DissolvedDate = new SimDate(1998, 1, 1);   // 24 months before Y2000
            parties[0].CoreGrievance = Issue.Environment;

            PartyLifecycleOutcome outcome = Advance(tuning, parties, null, Y2000,
                Grievance(Issue.Environment, 0.9));

            Assert.Equal(PartyStatus.Dissolved, Get(outcome, parties[0].Id).Status);
        }

        [Fact]
        public void RevivalIsBlockedBelowTheGrievanceThreshold()
        {
            EngineTuning tuning = Quiet();          // revivalGrievanceThreshold = 0.35
            List<Party> parties = Field(5);
            parties[0].Status = PartyStatus.Dissolved;
            parties[0].DissolvedDate = Y1993;
            parties[0].CoreGrievance = Issue.Environment;

            PartyLifecycleOutcome outcome = Advance(tuning, parties, null, Y2000,
                Grievance(Issue.Environment, 0.30));

            Assert.Equal(PartyStatus.Dissolved, Get(outcome, parties[0].Id).Status);
        }

        [Fact]
        public void MergedBrandsNeverRevive()
        {
            EngineTuning tuning = Quiet();
            List<Party> parties = Field(5);
            parties[0].Status = PartyStatus.Merged;
            parties[0].SuccessorPartyId = "party-02";
            parties[0].DissolvedDate = Y1993;
            parties[0].CoreGrievance = Issue.Environment;

            PartyLifecycleOutcome outcome = Advance(tuning, parties, null, Y2000,
                Grievance(Issue.Environment, 1.0));

            Assert.Equal(PartyStatus.Merged, Get(outcome, parties[0].Id).Status);
        }

        // ============================ Split =======================================================

        [Fact]
        public void HighTensionParty_SpawnsASplinterThatKeepsTheAbandonedManifesto()
        {
            EngineTuning tuning = Tuning("\"splitProbabilityPerCycle\":1.0," +
                                         "\"mergeProbabilityPerCycle\":0.0," +
                                         "\"newPartyEntryProbability\":0.0");
            List<Party> parties = Field(5);
            Party parent = parties[0];
            parent.LastVoteShare = 0.30;
            parent.Platform = Uniform(0.5);
            parent.LastManifesto = Uniform(-0.5);      // tension = 0.5, over the 0.45 threshold

            PartyLifecycleOutcome outcome = Advance(tuning, parties, null, Y2000);

            Assert.Equal(6, outcome.Parties.Count);
            Party splinter = Get(outcome, "party-06");
            Assert.Equal(parent.Id, splinter.PredecessorPartyId);
            Assert.Equal(PartyStatus.Active, splinter.Status);
            Assert.Equal(0.0, splinter.LastVoteShare);          // it contested no election yet
            Assert.Equal(0, splinter.SeatsHeld);
            Assert.Equal(Y2000, splinter.FoundedDate);
            Assert.Equal(splinter.Platform.Services, splinter.LastManifesto.Services, 12);
            Assert.NotEqual(parent.ColorHex, splinter.ColorHex);
            Assert.True(splinter.Platform.Distance(Get(outcome, parent.Id).Platform)
                        >= tuning.Parties.MinPlatformDistance - 1e-12);
            Assert.Contains(outcome.Changes, c => c.Kind == PartyChangeKind.SplitFounded);
            Assert.Contains(outcome.Changes, c => c.Kind == PartyChangeKind.SplitParent);
        }

        [Fact]
        public void NoSplitBelowTheTensionThreshold_EvenAtCertainty()
        {
            EngineTuning tuning = Tuning("\"splitProbabilityPerCycle\":1.0," +
                                         "\"mergeProbabilityPerCycle\":0.0," +
                                         "\"newPartyEntryProbability\":0.0");
            List<Party> parties = Field(5);
            parties[0].LastVoteShare = 0.30;           // share qualifies, tension does not

            PartyLifecycleOutcome outcome = Advance(tuning, parties, null, Y2000);

            Assert.Equal(5, outcome.Parties.Count);
        }

        [Fact]
        public void NoSplitBelowTheMinimumVoteShare_EvenAtCertainty()
        {
            EngineTuning tuning = Tuning("\"splitProbabilityPerCycle\":1.0," +
                                         "\"mergeProbabilityPerCycle\":0.0," +
                                         "\"newPartyEntryProbability\":0.0");
            List<Party> parties = Field(5);
            parties[0].LastVoteShare = 0.04;           // under splitMinVoteShare = 0.08
            parties[0].Platform = Uniform(0.5);
            parties[0].LastManifesto = Uniform(-0.5);

            PartyLifecycleOutcome outcome = Advance(tuning, parties, null, Y2000);

            Assert.Equal(5, outcome.Parties.Count);
        }

        [Fact]
        public void SplitsStopAtTheMaximumBallotSize()
        {
            EngineTuning tuning = Tuning("\"splitProbabilityPerCycle\":1.0," +
                                         "\"mergeProbabilityPerCycle\":0.0," +
                                         "\"newPartyEntryProbability\":0.0");
            List<Party> parties = Field(7);            // already at maxCountEu
            foreach (Party p in parties)
            {
                p.LastVoteShare = 0.14;
                p.Platform = Uniform(0.5);
                p.LastManifesto = Uniform(-0.5);
            }

            PartyLifecycleOutcome outcome = Advance(tuning, parties, null, Y2000);

            Assert.Equal(7, PartyRegistry.OnBallotCount(outcome.Parties));
            Assert.Equal(7, outcome.Parties.Count);
        }

        // ============================ Merge =======================================================

        [Fact]
        public void ConvergedPartiesMerge_TransferringSeatsAndShareToTheSurvivor()
        {
            EngineTuning tuning = Tuning("\"mergeProbabilityPerCycle\":1.0," +
                                         "\"splitProbabilityPerCycle\":0.0," +
                                         "\"newPartyEntryProbability\":0.0");
            List<Party> parties = Field(5);
            parties[0].Platform = Uniform(0.4);
            parties[0].LastVoteShare = 0.20;
            parties[0].SeatsHeld = 9;
            parties[1].Platform = Uniform(0.4);        // identical → affinity 1.0
            parties[1].LastVoteShare = 0.10;
            parties[1].SeatsHeld = 4;

            PartyLifecycleOutcome outcome = Advance(tuning, parties, null, Y2000);

            Party survivor = Get(outcome, "party-01");   // higher share survives
            Party absorbed = Get(outcome, "party-02");

            Assert.Equal(PartyStatus.Merged, absorbed.Status);
            Assert.Equal("party-01", absorbed.SuccessorPartyId);
            Assert.Equal(Y2000, absorbed.DissolvedDate);
            Assert.Equal(0, absorbed.SeatsHeld);
            Assert.Equal(13, survivor.SeatsHeld);
            Assert.Equal(0.30, survivor.LastVoteShare, 12);
            Assert.Equal(4, PartyRegistry.OnBallotCount(outcome.Parties));
            Assert.Contains(outcome.Changes, c => c.Kind == PartyChangeKind.MergedInto && c.PartyId == "party-01");
            Assert.Contains(outcome.Changes, c => c.Kind == PartyChangeKind.MergedAway && c.PartyId == "party-02");
        }

        [Fact]
        public void TwoBigPartiesDoNotMerge_EvenWhenTheirPlatformsAreIdentical()
        {
            EngineTuning tuning = Tuning("\"mergeProbabilityPerCycle\":1.0," +
                                         "\"splitProbabilityPerCycle\":0.0," +
                                         "\"newPartyEntryProbability\":0.0");
            List<Party> parties = Field(5);
            parties[0].Platform = Uniform(0.4);
            parties[0].LastVoteShare = 0.35;
            parties[1].Platform = Uniform(0.4);
            parties[1].LastVoteShare = 0.30;           // combined 0.65 > mergeMaxCombinedVoteShare

            PartyLifecycleOutcome outcome = Advance(tuning, parties, null, Y2000);

            Assert.Equal(PartyStatus.Active, Get(outcome, "party-02").Status);
            Assert.Equal(5, PartyRegistry.OnBallotCount(outcome.Parties));
        }

        [Fact]
        public void MergesStopAtTheMinimumBallotSize()
        {
            EngineTuning tuning = Tuning("\"mergeProbabilityPerCycle\":1.0," +
                                         "\"splitProbabilityPerCycle\":0.0," +
                                         "\"newPartyEntryProbability\":0.0");
            List<Party> parties = Field(4);            // already at minCountEu
            parties[0].Platform = Uniform(0.4);
            parties[0].LastVoteShare = 0.20;
            parties[1].Platform = Uniform(0.4);
            parties[1].LastVoteShare = 0.10;

            PartyLifecycleOutcome outcome = Advance(tuning, parties, null, Y2000);

            Assert.Equal(4, PartyRegistry.OnBallotCount(outcome.Parties));
        }

        // ============================ New entry ===================================================

        [Fact]
        public void NewPartyEntersOnAnUnusedArchetype()
        {
            EngineTuning tuning = Tuning("\"newPartyEntryProbability\":1.0," +
                                         "\"splitProbabilityPerCycle\":0.0," +
                                         "\"mergeProbabilityPerCycle\":0.0");
            List<Party> parties = PartyRegistry.GenerateInitial(SaveA, Y1990, RegionTheme.Eu, tuning);

            PartyLifecycleOutcome outcome = Advance(tuning, parties, null, Y2000);

            Assert.Equal(7, outcome.Parties.Count);
            Party entrant = Get(outcome, "party-07");
            Assert.Equal(PartyStatus.Active, entrant.Status);
            Assert.Equal("commuter", entrant.ArchetypeId);      // first unused catalog entry
            Assert.Null(entrant.PredecessorPartyId);
            Assert.Contains(outcome.Changes, c => c.Kind == PartyChangeKind.Founded);
        }

        [Fact]
        public void NewEntryStopsAtTheMaximumBallotSize()
        {
            EngineTuning tuning = Tuning("\"newPartyEntryProbability\":1.0," +
                                         "\"splitProbabilityPerCycle\":0.0," +
                                         "\"mergeProbabilityPerCycle\":0.0");
            List<Party> parties = Field(7);

            PartyLifecycleOutcome outcome = Advance(tuning, parties, null, Y2000);

            Assert.Equal(7, outcome.Parties.Count);
        }

        [Fact]
        public void NaTheme_RunsNoStructuralPartyChange()
        {
            EngineTuning tuning = Tuning("\"splitProbabilityPerCycle\":1.0," +
                                         "\"mergeProbabilityPerCycle\":1.0," +
                                         "\"newPartyEntryProbability\":1.0");
            List<Party> parties = Field(4);
            parties[0].Platform = Uniform(0.4);
            parties[0].LastVoteShare = 0.20;
            parties[0].LastManifesto = Uniform(-0.5);
            parties[1].Platform = Uniform(0.4);
            parties[1].LastVoteShare = 0.10;

            var input = new PartyLifecycleInput
            {
                SaveGuid = SaveA,
                Date = Y2000,
                Theme = RegionTheme.Na,
                Parties = parties
            };
            PartyLifecycleOutcome outcome = PartyLifecycle.Advance(input, tuning);

            Assert.Equal(4, outcome.Parties.Count);
            foreach (Party p in outcome.Parties) Assert.Equal(PartyStatus.Active, p.Status);
        }

        // ============================ Manifesto refresh ============================================

        [Fact]
        public void ManifestoMovesTowardTheLoudestGrievance()
        {
            EngineTuning tuning = EngineTuning.Default;
            Party party = MakeParty("party-01");
            party.Platform = IssuePosition.Centre;
            party.LastManifesto = IssuePosition.Centre;

            Party refreshed = PartyPlatform.RefreshManifesto(SaveA, Y2000, party,
                Grievance(Issue.Transit, 1.0), tuning);

            Assert.True(refreshed.Platform.Transit > party.Platform.Transit);
            Assert.Equal(refreshed.Platform.Transit, refreshed.LastManifesto.Transit, 12);
            Assert.Equal(IssuePosition.Centre.Transit, party.Platform.Transit);   // input untouched
        }

        [Fact]
        public void ManifestoDriftIsCappedInBothDirections()
        {
            // Drift sigma far past the cap, so almost every draw would breach it if uncapped.
            EngineTuning tuning = Tuning("\"platformDriftPerCycle\":10.0");
            double cap = tuning.Parties.PlatformDriftCapPerCycle;

            for (int i = 0; i < 200; i++)
            {
                Party party = MakeParty("party-" + i.ToString("D2", CultureInfo.InvariantCulture));
                party.Platform = IssuePosition.Centre;
                party.LastManifesto = IssuePosition.Centre;

                Party refreshed = PartyPlatform.RefreshManifesto(SaveA, Y2000, party,
                    Grievance(Issue.Services, 5.0), tuning);

                for (int k = 0; k < Issues.All.Count; k++)
                {
                    Issue issue = Issues.All[k];
                    double move = refreshed.Platform[issue] - party.Platform[issue];
                    Assert.InRange(move, -cap - 1e-12, cap + 1e-12);
                    Assert.InRange(refreshed.Platform[issue], -1.0, 1.0);
                }
            }
        }

        [Fact]
        public void ManifestoRefreshIsDeterministic()
        {
            EngineTuning tuning = EngineTuning.Default;
            Party party = MakeParty("party-03");
            party.Platform = Uniform(0.2);

            Party a = PartyPlatform.RefreshManifesto(SaveA, Y2000, party, Grievance(Issue.Growth, 0.6), tuning);
            Party b = PartyPlatform.RefreshManifesto(SaveA, Y2000, party, Grievance(Issue.Growth, 0.6), tuning);
            Party c = PartyPlatform.RefreshManifesto(SaveB, Y2000, party, Grievance(Issue.Growth, 0.6), tuning);

            Assert.Equal(Format(a.Platform), Format(b.Platform));
            Assert.NotEqual(Format(a.Platform), Format(c.Platform));
        }

        [Fact]
        public void InternalTension_TakesTheWorstOfPlatformDriftAndFactionTension()
        {
            Party party = MakeParty("party-01");
            party.Platform = Uniform(0.1);
            party.LastManifesto = Uniform(-0.1);        // drift distance = 0.1

            Assert.Equal(0.1, PartyLifecycle.InternalTension(party, null), 12);

            var factions = new List<Faction>
            {
                new Faction { Id = "faction-01", PartyId = "party-01", TensionWithParty = 0.7 },
                new Faction { Id = "faction-02", PartyId = "party-02", TensionWithParty = 0.95 },
                new Faction { Id = "faction-03", PartyId = "party-01", TensionWithParty = 0.9,
                              Status = FactionStatus.Dissolved }
            };

            // Only live factions of this party count.
            Assert.Equal(0.7, PartyLifecycle.InternalTension(party, factions), 12);
        }

        // ============================ Determinism and invariants ===================================

        [Fact]
        public void Advance_ProducesIdenticalOutputTwice()
        {
            Assert.Equal(RunSimulation(SaveA), RunSimulation(SaveA));
        }

        [Fact]
        public void Advance_ProducesDifferentHistoriesForDifferentSaves()
        {
            Assert.NotEqual(RunSimulation(SaveA), RunSimulation(SaveB));
        }

        /// <summary>
        /// <see cref="PartyRegistry.Clone"/> is a hand-written field-by-field copy, and every
        /// lifecycle pass clones before it mutates. A field left out of it is therefore not merely
        /// missed once — it is cleared on every advance, which for the player's own edits reads as
        /// "my rename came back a few months later" rather than as a bug in the clone.
        /// </summary>
        [Fact]
        public void Clone_PreservesPlayerOverrides()
        {
            var source = new Party
            {
                Id = "party-01",
                Name = "The Player's Own Name",
                PlayerOverrides = PartyOverrides.NameLocked | PartyOverrides.ColorLocked
            };

            Party copy = PartyRegistry.Clone(source);

            Assert.Equal(PartyOverrides.NameLocked | PartyOverrides.ColorLocked, copy.PlayerOverrides);
            Assert.Equal(PartyOverrides.None, PartyRegistry.Clone(new Party()).PlayerOverrides);
        }

        [Fact]
        public void Advance_DoesNotMutateItsInput()
        {
            EngineTuning tuning = Quiet();
            List<Party> parties = Field(6);
            string before = HashParties(parties);

            Advance(tuning, parties, Election(Y1993, parties, "party-01", 0.01));

            Assert.Equal(before, HashParties(parties));
        }

        [Fact]
        public void Advance_ReturnsPartiesSortedById()
        {
            EngineTuning tuning = Tuning("\"splitProbabilityPerCycle\":1.0," +
                                         "\"mergeProbabilityPerCycle\":0.0," +
                                         "\"newPartyEntryProbability\":0.0");
            List<Party> parties = Field(5);
            parties.Reverse();
            parties[0].LastVoteShare = 0.30;
            parties[0].Platform = Uniform(0.5);
            parties[0].LastManifesto = Uniform(-0.5);

            PartyLifecycleOutcome outcome = Advance(tuning, parties, null, Y2000);

            for (int i = 1; i < outcome.Parties.Count; i++)
            {
                Assert.True(string.CompareOrdinal(outcome.Parties[i - 1].Id, outcome.Parties[i].Id) < 0);
            }
        }

        [Fact]
        public void MultiYearRun_HoldsTheBallotBetweenTheMinimumAndMaximum()
        {
            EngineTuning tuning = EngineTuning.Default;
            var date = Y1990;
            List<Party> parties = PartyRegistry.GenerateInitial(SaveA, date, RegionTheme.Eu, tuning);

            for (int cycle = 0; cycle < 30; cycle++)
            {
                date = date.AddMonths(36);
                parties = SimulateCycle(SaveA, date, cycle, parties, tuning, out PartyLifecycleOutcome outcome);

                int onBallot = PartyRegistry.OnBallotCount(outcome.Parties);
                Assert.InRange(onBallot, tuning.Parties.MinCountEu, tuning.Parties.MaxCountEu);
                Assert.True(outcome.Parties.Count <= tuning.Parties.MaxPartiesTotal,
                    "brand count " + outcome.Parties.Count + " exceeded maxPartiesTotal at cycle " + cycle);
            }
        }

        [Fact]
        public void MultiYearRun_ActuallyExercisesDeathAndRevival()
        {
            EngineTuning tuning = EngineTuning.Default;
            var date = Y1990;
            List<Party> parties = PartyRegistry.GenerateInitial(SaveA, date, RegionTheme.Eu, tuning);

            int deaths = 0;
            int revivals = 0;
            for (int cycle = 0; cycle < 30; cycle++)
            {
                date = date.AddMonths(36);
                parties = SimulateCycle(SaveA, date, cycle, parties, tuning, out PartyLifecycleOutcome outcome);
                foreach (PartyChange change in outcome.Changes)
                {
                    if (change.Kind == PartyChangeKind.Dissolved) deaths++;
                    if (change.Kind == PartyChangeKind.Revived) revivals++;
                }
            }

            // The synthetic history punishes a rotating party below 3% every cycle and cycles the
            // grievance vector, so both branches must fire. A zero here means a stage never ran.
            Assert.True(deaths > 0, "no party ever died across 30 cycles");
            Assert.True(revivals > 0, "no brand ever revived across 30 cycles");
        }

        // ============================ Fixtures ====================================================

        /// <summary>Tuning with the shipped defaults plus the given <c>parties</c> overrides.</summary>
        private static EngineTuning Tuning(string partiesOverrides) =>
            EngineTuning.FromJson("{\"parties\":{" + partiesOverrides + "}}");

        /// <summary>
        /// Defaults with every structural probability pinned to zero. Tests about results, death and
        /// revival use this so an unrelated split or entry roll cannot change a party count and turn
        /// a real assertion into a flaky one.
        /// </summary>
        private static EngineTuning Quiet() =>
            Tuning("\"splitProbabilityPerCycle\":0.0," +
                   "\"mergeProbabilityPerCycle\":0.0," +
                   "\"newPartyEntryProbability\":0.0");

        private static Party MakeParty(string id) => new Party
        {
            Id = id,
            ColorHex = "#000000",
            ArchetypeId = "test",
            Status = PartyStatus.Active,
            FoundedDate = Y1990,
            CoreGrievance = Issue.Services
        };

        /// <summary>A field of n plain active parties, ids party-01..party-0n, platforms far apart.</summary>
        private static List<Party> Field(int n)
        {
            var parties = new List<Party>();
            for (int i = 0; i < n; i++)
            {
                Party p = MakeParty(PartyRegistry.FormatId(i + 1));
                p.ColorHex = "#00000" + i.ToString(CultureInfo.InvariantCulture);
                p.ArchetypeId = PartyArchetypes.Eu[i % PartyArchetypes.Eu.Count].Id;
                p.CoreGrievance = Issues.All[i % Issues.Count];
                // Spread them along one axis each so no accidental merge pair exists.
                p.Platform = IssuePosition.Centre.With(Issues.All[i % Issues.Count], 1.0);
                p.LastManifesto = p.Platform;
                p.LastVoteShare = 1.0 / n;
                p.SeatsHeld = 45 / n;
                parties.Add(p);
            }
            return parties;
        }

        private static IssuePosition Uniform(double v) => new IssuePosition(v, v, v, v, v, v);

        private static IssueWeights Grievance(Issue issue, double value) =>
            new IssueWeights(0, 0, 0, 0, 0, 0).With(issue, value);

        /// <summary>
        /// An election where every on-ballot party gets an even share, except
        /// <paramref name="punishedId"/> which gets <paramref name="punishedShare"/>.
        /// </summary>
        private static ElectionResult Election(SimDate date, List<Party> parties,
                                               string? punishedId, double punishedShare)
        {
            var ballot = new List<Party>();
            foreach (Party p in parties)
            {
                if (PartyRegistry.IsOnBallot(p)) ballot.Add(p);
            }

            bool hasPunished = punishedId != null;
            int evenCount = hasPunished ? ballot.Count - 1 : ballot.Count;
            double remainder = hasPunished ? 1.0 - punishedShare : 1.0;
            double even = evenCount > 0 ? remainder / evenCount : 0.0;

            var result = new ElectionResult
            {
                Id = "election-" + date,
                Date = date,
                System = ElectoralSystem.Proportional,
                TotalSeats = 45
            };

            foreach (Party p in ballot)
            {
                double share = hasPunished && string.CompareOrdinal(p.Id, punishedId) == 0
                    ? punishedShare
                    : even;
                result.PartyIdsOnBallot.Add(p.Id);
                result.CityVoteShares.Add(new PartyVoteShare(p.Id, share));
                result.Seats.Add(new SeatAllocation(p.Id, (int)Math.Round(share * 45.0),
                    share, share, 0, (int)Math.Round(share * 45.0), share >= 0.03));
            }

            result.PartyIdsOnBallot.Sort(StringComparer.Ordinal);
            result.CityVoteShares.Sort((a, b) => string.CompareOrdinal(a.PartyId, b.PartyId));
            return result;
        }

        private static PartyLifecycleOutcome Advance(EngineTuning tuning, List<Party> parties,
                                                     ElectionResult? election)
            => Advance(tuning, parties, election, Y1993);

        private static PartyLifecycleOutcome Advance(EngineTuning tuning, List<Party> parties,
                                                     ElectionResult? election, SimDate date)
            => Advance(tuning, parties, election, date, default);

        private static PartyLifecycleOutcome Advance(EngineTuning tuning, List<Party> parties,
                                                     ElectionResult? election, SimDate date,
                                                     IssueWeights grievance)
        {
            var input = new PartyLifecycleInput
            {
                SaveGuid = SaveA,
                Date = date,
                Theme = RegionTheme.Eu,
                Parties = parties,
                LastElection = election,
                CityGrievance = grievance
            };
            return PartyLifecycle.Advance(input, tuning);
        }

        private static Party Get(PartyLifecycleOutcome outcome, string id)
        {
            Party? found = PartyRegistry.Find(outcome.Parties, id);
            Assert.NotNull(found);
            return found!;
        }

        /// <summary>
        /// One synthetic political cycle: an election that punishes a rotating party, a lifecycle
        /// pass, a manifesto refresh, then a governing drift that builds the tension a split needs.
        /// </summary>
        private static List<Party> SimulateCycle(Guid save, SimDate date, int cycle,
                                                 List<Party> parties, EngineTuning tuning,
                                                 out PartyLifecycleOutcome outcome)
        {
            var ballot = new List<Party>();
            foreach (Party p in parties)
            {
                if (PartyRegistry.IsOnBallot(p)) ballot.Add(p);
            }
            // Two consecutive cycles per victim, which is exactly what the death rule needs.
            string punished = ballot[(cycle / 2) % ballot.Count].Id;

            IssueWeights grievance = Grievance(Issues.All[cycle % Issues.Count], 0.8);

            var input = new PartyLifecycleInput
            {
                SaveGuid = save,
                Date = date,
                Theme = RegionTheme.Eu,
                Parties = parties,
                LastElection = Election(date, parties, punished, 0.01),
                CityGrievance = grievance
            };
            outcome = PartyLifecycle.Advance(input, tuning);

            var next = new List<Party>();
            foreach (Party p in outcome.Parties)
            {
                Party refreshed = PartyPlatform.RefreshManifesto(save, date, p, grievance, tuning);
                // Governing drags the platform away from the manifesto it was elected on.
                refreshed.Platform = refreshed.Platform
                    .With(Issues.All[(cycle + 3) % Issues.Count],
                          -refreshed.Platform[Issues.All[(cycle + 3) % Issues.Count]])
                    .Add(Uniform(cycle % 2 == 0 ? 0.25 : -0.25))
                    .Clamped();
                next.Add(refreshed);
            }
            return next;
        }

        private static string RunSimulation(Guid save)
        {
            EngineTuning tuning = EngineTuning.Default;
            var date = Y1990;
            List<Party> parties = PartyRegistry.GenerateInitial(save, date, RegionTheme.Eu, tuning);
            var log = new StringBuilder();

            for (int cycle = 0; cycle < 30; cycle++)
            {
                date = date.AddMonths(36);
                parties = SimulateCycle(save, date, cycle, parties, tuning, out PartyLifecycleOutcome outcome);

                log.Append(date).Append('\n');
                foreach (Party p in outcome.Parties) log.Append(Format(p)).Append('\n');
                foreach (PartyChange change in outcome.Changes) log.Append(change).Append('\n');
            }

            return Sha256(log.ToString());
        }

        private static string HashParties(IReadOnlyList<Party> parties)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < parties.Count; i++) sb.Append(Format(parties[i])).Append('\n');
            return Sha256(sb.ToString());
        }

        private static string Format(Party p)
        {
            var sb = new StringBuilder();
            sb.Append(p.Id).Append('|').Append(p.Status).Append('|').Append(p.ArchetypeId).Append('|')
              .Append(p.ColorHex).Append('|').Append(Format(p.Platform)).Append('|')
              .Append(Format(p.LastManifesto)).Append('|')
              .Append(p.LastVoteShare.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append(p.SeatsHeld).Append('|').Append(p.ConsecutiveElectionsBelowThreshold).Append('|')
              .Append(p.PredecessorPartyId ?? "-").Append('|').Append(p.SuccessorPartyId ?? "-").Append('|')
              .Append(p.FoundedDate).Append('|')
              .Append(p.DissolvedDate.HasValue ? p.DissolvedDate.Value.ToString() : "-").Append('|')
              .Append(p.RevivalCount).Append('|').Append(p.CoreGrievance).Append('|')
              .Append(p.IsIncumbent).Append(p.IsInGovernment);
            return sb.ToString();
        }

        private static string Format(IssuePosition p)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Issues.All.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(p[Issues.All[i]].ToString("R", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static string Sha256(string text)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            return BitConverter.ToString(hash).Replace("-", "");
        }
    }
}
