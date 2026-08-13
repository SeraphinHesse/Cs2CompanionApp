using System;
using System.Collections.Generic;
using System.Globalization;
using Agora.Core.Contracts;
using Agora.Core.Engine.Parties;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// <see cref="PartyLifecycleChanges"/> — the query the news feed asks to find out which parties
    /// were founded, dissolved or absorbed, and when.
    ///
    /// <para>
    /// The turns are driven through the real <see cref="PartyLifecycle"/> wherever a test is about
    /// what the engine actually produces, rather than hand-stamped onto a <see cref="Party"/>: the
    /// interesting failures here are all cases where the query and the lifecycle disagree about what
    /// a persisted field means, and a hand-stamped fixture cannot catch one.
    /// </para>
    ///
    /// <para>
    /// The feed row this feeds is in <c>Agora.Mod</c> and reaches a Colossal binding, so nothing
    /// below it is testable here — the wording, the id suffixes and the forty-row cap are manual
    /// gate items (plan 0003 §11, M9).
    /// </para>
    /// </summary>
    public class PartyLifecycleChangesTests
    {
        private static readonly Guid SaveA = new Guid("11111111-2222-3333-4444-555555555555");
        private static readonly SimDate Y1990 = new SimDate(1990, 1, 1);
        private static readonly SimDate Y1993 = new SimDate(1993, 1, 1);
        private static readonly SimDate Y1996 = new SimDate(1996, 1, 1);
        private static readonly SimDate Y2000 = new SimDate(2000, 1, 1);
        private static readonly SimDate Y2003 = new SimDate(2003, 1, 1);

        // ============================ The opening roster ==========================================

        [Fact]
        public void TheOpeningRosterIsNotNews()
        {
            EngineTuning tuning = EngineTuning.Default;
            List<Party> parties = PartyRegistry.GenerateInitial(SaveA, Y1990, RegionTheme.Eu, tuning);

            // Every one of them really is stamped with the date the registry was handed — which is
            // what makes the exclusion necessary rather than defensive.
            Assert.All(parties, p => Assert.Equal(Y1990, p.FoundedDate));

            PartyLifecycleChangeSet changes = PartyLifecycleChanges.Collect(parties, Y1990);

            Assert.Empty(changes.Records);
            Assert.Empty(changes.SuppressedDates);
        }

        [Fact]
        public void ARegistryRegeneratedMidSaveIsSuppressedWhole_AndNamesTheDate()
        {
            // PoliticalEngine's empty-registry recovery regenerates the whole field with the *current*
            // date, not the save's start date, so the start-date exclusion cannot catch it. The
            // per-date cap is the second half of that belt-and-braces pair.
            EngineTuning tuning = EngineTuning.Default;
            List<Party> parties = PartyRegistry.GenerateInitial(SaveA, Y2000, RegionTheme.Eu, tuning);

            // The whole field, elders and all, carries the one date — which is the thing the rule
            // recognises, and the thing no amount of ordinary churn can reproduce.
            Assert.True(parties.Count > 1);
            Assert.All(parties, p => Assert.Equal(Y2000, p.FoundedDate));

            PartyLifecycleChangeSet changes = PartyLifecycleChanges.Collect(parties, Y1990);

            Assert.Empty(changes.Records);
            Assert.Equal(new[] { Y2000 }, changes.SuppressedDates);
        }

        [Fact]
        public void ARegenerationStaysSuppressedOnceTheRosterHasGrownPastIt()
        {
            // The regeneration is only recognisable because nothing in the roster predates it, and
            // that stays true as the field grows: parties founded later are founded later. Counting
            // the date's foundings against the roster's *size* instead would have unsuppressed all six
            // rows the moment a seventh party appeared — and this is re-derived on every publish, so
            // the archive would have changed under the player mid-save.
            EngineTuning tuning = Tuning("\"newPartyEntryProbability\":1.0," +
                                         "\"splitProbabilityPerCycle\":0.0," +
                                         "\"mergeProbabilityPerCycle\":0.0");
            List<Party> regenerated = PartyRegistry.GenerateInitial(SaveA, Y2000, RegionTheme.Eu, tuning);
            PartyLifecycleOutcome outcome = Advance(tuning, regenerated, null, Y2003);
            Assert.Equal(regenerated.Count + 1, outcome.Parties.Count);

            PartyLifecycleChangeSet changes = PartyLifecycleChanges.Collect(outcome.Parties, Y1990);

            Assert.Equal(new[] { Y2000 }, changes.SuppressedDates);
            PartyLifecycleRecord only = Assert.Single(changes.Records);
            Assert.Equal(PartyLifecycleKind.Founded, only.Kind);
            Assert.Equal(Y2003, only.Date);
        }

        [Fact]
        public void APartyFoundedAfterTheStartIsReported_AndItsElderSiblingsAreNot()
        {
            EngineTuning tuning = Tuning("\"newPartyEntryProbability\":1.0," +
                                         "\"splitProbabilityPerCycle\":0.0," +
                                         "\"mergeProbabilityPerCycle\":0.0");
            List<Party> parties = PartyRegistry.GenerateInitial(SaveA, Y1990, RegionTheme.Eu, tuning);
            PartyLifecycleOutcome outcome = Advance(tuning, parties, null, Y2000);
            Assert.Equal(parties.Count + 1, outcome.Parties.Count);

            PartyLifecycleChangeSet changes = PartyLifecycleChanges.Collect(outcome.Parties, Y1990);

            PartyLifecycleRecord only = Assert.Single(changes.Records);
            Assert.Equal(PartyLifecycleKind.Founded, only.Kind);
            Assert.Equal(Y2000, only.Date);
            Assert.Equal(outcome.Parties[outcome.Parties.Count - 1].Id, only.PartyId);
        }

        // ============================ Merge versus death ==========================================

        [Fact]
        public void AnAbsorbedBrandReportsAsAMerge_NotAsADeath()
        {
            EngineTuning tuning = Tuning("\"mergeProbabilityPerCycle\":1.0," +
                                         "\"splitProbabilityPerCycle\":0.0," +
                                         "\"newPartyEntryProbability\":0.0");
            List<Party> parties = Field(5);
            parties[0].Platform = Uniform(0.4);        // identical platforms → affinity 1.0
            parties[0].LastVoteShare = 0.20;
            parties[1].Platform = Uniform(0.4);
            parties[1].LastVoteShare = 0.10;

            PartyLifecycleOutcome outcome = Advance(tuning, parties, null, Y2000);
            Assert.Equal(PartyStatus.Merged, Get(outcome, "party-02").Status);

            PartyLifecycleChangeSet changes = PartyLifecycleChanges.Collect(outcome.Parties, Y1990);

            PartyLifecycleRecord only = Assert.Single(changes.Records);
            Assert.Equal("party-02", only.PartyId);
            Assert.Equal(PartyLifecycleKind.Merged, only.Kind);
            Assert.Equal(Y2000, only.Date);
        }

        [Fact]
        public void ABrandThatDiedBelowThresholdReportsAsADeath_DatedWhenItDied()
        {
            EngineTuning tuning = Quiet();
            List<Party> parties = Field(6);

            // Two consecutive results under 3% is what kills a party. The second election is the one
            // the death is dated to, not the first.
            PartyLifecycleOutcome first = Advance(tuning, parties,
                Election(Y1993, parties, "party-01", 0.005), Y1993);
            PartyLifecycleOutcome second = Advance(tuning, new List<Party>(first.Parties),
                Election(Y1996, first.Parties, "party-01", 0.005), Y1996);
            Assert.Equal(PartyStatus.Dissolved, Get(second, "party-01").Status);

            PartyLifecycleChangeSet changes = PartyLifecycleChanges.Collect(second.Parties, Y1990);

            PartyLifecycleRecord only = Assert.Single(changes.Records);
            Assert.Equal("party-01", only.PartyId);
            Assert.Equal(PartyLifecycleKind.Dissolved, only.Kind);
            Assert.Equal(Y1996, only.Date);
        }

        [Fact]
        public void ThreeBrandsDyingAtTheSameElectionAreAllThreeReported()
        {
            // ApplyDeaths loops every party at or over the consecutive-elections threshold, so one
            // election day really can carry three deaths — and three deaths in one month is the
            // biggest political news the feed will ever have to carry, not a registry accident.
            EngineTuning tuning = Quiet();
            List<Party> parties = Field(7);   // one over the EU floor of four, times three deaths
            var doomed = new[] { "party-01", "party-02", "party-03" };

            PartyLifecycleOutcome first = Advance(tuning, parties,
                Election(Y1993, parties, doomed, 0.005), Y1993);
            PartyLifecycleOutcome second = Advance(tuning, new List<Party>(first.Parties),
                Election(Y1996, first.Parties, doomed, 0.005), Y1996);

            // Pre-condition: the engine, not the fixture, put three dissolutions on the one date.
            for (int i = 0; i < doomed.Length; i++)
            {
                Party dead = Get(second, doomed[i]);
                Assert.Equal(PartyStatus.Dissolved, dead.Status);
                Assert.Equal(Y1996, dead.DissolvedDate);
            }

            PartyLifecycleChangeSet changes = PartyLifecycleChanges.Collect(second.Parties, Y1990);

            Assert.Empty(changes.SuppressedDates);
            Assert.Equal(
                "1996-01-01/party-01/Dissolved;1996-01-01/party-02/Dissolved;" +
                "1996-01-01/party-03/Dissolved",
                Describe(changes));
        }

        // ============================ The accepted loss ===========================================

        [Fact]
        public void ARevivalErasesItsOwnDeath_AndThisIsAccepted()
        {
            EngineTuning tuning = Quiet();
            List<Party> parties = Field(5);
            Party doomed = parties[0];
            doomed.Status = PartyStatus.Dissolved;
            doomed.DissolvedDate = Y1993;
            doomed.CoreGrievance = Issue.Environment;
            doomed.SeatsHeld = 0;

            // Before: the death is on the record.
            PartyLifecycleChangeSet before = PartyLifecycleChanges.Collect(parties, Y1990);
            Assert.Equal(PartyLifecycleKind.Dissolved, Assert.Single(before.Records).Kind);

            PartyLifecycleOutcome outcome = Advance(tuning, parties, null, Y2000,
                Grievance(Issue.Environment, 0.9));
            Assert.Equal(PartyStatus.Revived, Get(outcome, doomed.Id).Status);

            // After: it is gone, retroactively, because PartyLifecycle cleared DissolvedDate. This is
            // the known loss recorded in plan 0003 §1c′ — the archive rewrites itself, the alert
            // having already fired at the time. This test exists so that a future reader finds the
            // loss documented rather than discovering it, and so that anyone who fixes it (which
            // needs a persisted lifecycle log, i.e. a sidecar field) has to come here and say so.
            PartyLifecycleChangeSet after = PartyLifecycleChanges.Collect(outcome.Parties, Y1990);
            Assert.Empty(after.Records);
        }

        // ============================ Determinism =================================================

        [Fact]
        public void TheAnswerDoesNotDependOnTheOrderThePartiesArriveIn()
        {
            List<Party> parties = Field(4);
            parties[0].FoundedDate = Y1993;
            parties[1].FoundedDate = Y1993;
            parties[2].Status = PartyStatus.Dissolved;
            parties[2].DissolvedDate = Y1996;
            parties[3].Status = PartyStatus.Merged;
            parties[3].SuccessorPartyId = parties[0].Id;
            parties[3].DissolvedDate = Y2000;

            var reversed = new List<Party>(parties);
            reversed.Reverse();

            PartyLifecycleChangeSet forward = PartyLifecycleChanges.Collect(parties, Y1990);
            PartyLifecycleChangeSet backward = PartyLifecycleChanges.Collect(reversed, Y1990);

            Assert.Equal(Describe(forward), Describe(backward));

            // And the order is the one the contract promises: date ascending, then party id.
            Assert.Equal(
                "1993-01-01/party-01/Founded;1993-01-01/party-02/Founded;" +
                "1996-01-01/party-03/Dissolved;2000-01-01/party-04/Merged",
                Describe(forward));
        }

        private static string Describe(PartyLifecycleChangeSet set)
        {
            var parts = new List<string>();
            for (int i = 0; i < set.Records.Count; i++)
            {
                PartyLifecycleRecord r = set.Records[i];
                parts.Add(r.Date + "/" + r.PartyId + "/" + r.Kind);
            }
            return string.Join(";", parts);
        }

        // ============================ Fixtures ====================================================
        //
        // Deliberately a local copy of the handful of helpers PartyLifecycleTests uses rather than a
        // shared base: these are four short builders, and making them shared would put a second
        // reason to edit that file in the way of anyone changing a lifecycle test.

        private static EngineTuning Tuning(string partiesOverrides) =>
            EngineTuning.FromJson("{\"parties\":{" + partiesOverrides + "}}");

        /// <summary>Defaults with every structural probability pinned to zero.</summary>
        private static EngineTuning Quiet() =>
            Tuning("\"splitProbabilityPerCycle\":0.0," +
                   "\"mergeProbabilityPerCycle\":0.0," +
                   "\"newPartyEntryProbability\":0.0");

        /// <summary>A field of n plain active parties founded at the save's start, platforms far apart.</summary>
        private static List<Party> Field(int n)
        {
            var parties = new List<Party>();
            for (int i = 0; i < n; i++)
            {
                var p = new Party
                {
                    Id = PartyRegistry.FormatId(i + 1),
                    ColorHex = "#00000" + i.ToString(CultureInfo.InvariantCulture),
                    ArchetypeId = PartyArchetypes.Eu[i % PartyArchetypes.Eu.Count].Id,
                    Status = PartyStatus.Active,
                    FoundedDate = Y1990,
                    CoreGrievance = Issues.All[i % Issues.Count],
                    LastVoteShare = 1.0 / n,
                    SeatsHeld = 45 / n
                };
                p.Platform = IssuePosition.Centre.With(Issues.All[i % Issues.Count], 1.0);
                p.LastManifesto = p.Platform;
                parties.Add(p);
            }
            return parties;
        }

        private static IssuePosition Uniform(double v) => new IssuePosition(v, v, v, v, v, v);

        private static IssueWeights Grievance(Issue issue, double value) =>
            new IssueWeights(0, 0, 0, 0, 0, 0).With(issue, value);

        private static ElectionResult Election(SimDate date, IReadOnlyList<Party> parties,
                                               string punishedId, double punishedShare)
            => Election(date, parties, new[] { punishedId }, punishedShare);

        /// <summary>
        /// An election where every on-ballot party gets an even share, except those in
        /// <paramref name="punishedIds"/>, which each get <paramref name="punishedShare"/>.
        /// </summary>
        private static ElectionResult Election(SimDate date, IReadOnlyList<Party> parties,
                                               string[] punishedIds, double punishedShare)
        {
            var ballot = new List<Party>();
            for (int i = 0; i < parties.Count; i++)
            {
                if (PartyRegistry.IsOnBallot(parties[i])) ballot.Add(parties[i]);
            }

            int punishedOnBallot = 0;
            for (int i = 0; i < ballot.Count; i++)
            {
                if (IsPunished(punishedIds, ballot[i].Id)) punishedOnBallot++;
            }

            int rest = ballot.Count - punishedOnBallot;
            double even = rest > 0 ? (1.0 - (punishedShare * punishedOnBallot)) / rest : 0.0;

            var result = new ElectionResult
            {
                Id = "election-" + date,
                Date = date,
                System = ElectoralSystem.Proportional,
                TotalSeats = 45
            };

            foreach (Party p in ballot)
            {
                double share = IsPunished(punishedIds, p.Id) ? punishedShare : even;
                result.PartyIdsOnBallot.Add(p.Id);
                result.CityVoteShares.Add(new PartyVoteShare(p.Id, share));
                result.Seats.Add(new SeatAllocation(p.Id, (int)Math.Round(share * 45.0),
                    share, share, 0, (int)Math.Round(share * 45.0), share >= 0.03));
            }

            result.PartyIdsOnBallot.Sort(StringComparer.Ordinal);
            result.CityVoteShares.Sort((a, b) => string.CompareOrdinal(a.PartyId, b.PartyId));
            return result;
        }

        private static bool IsPunished(string[] punishedIds, string partyId)
        {
            for (int i = 0; i < punishedIds.Length; i++)
            {
                if (string.CompareOrdinal(punishedIds[i], partyId) == 0) return true;
            }
            return false;
        }

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
    }
}
