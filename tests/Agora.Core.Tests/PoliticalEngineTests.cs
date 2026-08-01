using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Engine;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The monthly tick — <see cref="PoliticalEngine"/>, the orchestrator that runs the fourteen
    /// engine packets in order.
    ///
    /// <para>
    /// Every other suite in this project tests one packet in isolation. This one tests the sequence:
    /// that a decade of ticking is reproducible, that the tick never mutates what it was handed, that
    /// elections actually arrive, and that a government forms, governs and eventually ends. Those are
    /// the properties no single-packet test can see, and they are exactly the ones a reload has to
    /// preserve (non-negotiable #3, §2.3).
    /// </para>
    ///
    /// <para>
    /// The fixture city is deliberately static: it does not improve or decay over the run. That makes
    /// the political churn attributable to the engine rather than to a moving city, which is what
    /// lets the determinism assertions below mean something.
    /// </para>
    /// </summary>
    public class PoliticalEngineTests
    {
        private static readonly Guid SaveGuid = new Guid("a10f7e2c-4d3b-4f8a-9c1e-5b6d7f8a9c02");
        private static readonly SimDate Start = new SimDate(1990, 1, 1);

        // --- determinism -------------------------------------------------------------------------

        /// <summary>
        /// The canonical pattern at whole-engine scale: the same tick run twice from the same prior
        /// state produces byte-identical output. Hashing the state rather than asserting field by
        /// field is the point — the field a hand-written assertion forgets is where a desync hides.
        /// </summary>
        [Fact]
        public void Advance_ProducesIdenticalStateTwice()
        {
            EngineTickResult first = Advance(InitialState(), new SimDate(1990, 2, 1));
            EngineTickResult second = Advance(InitialState(), new SimDate(1990, 2, 1));

            Assert.Equal(Hash(first.State), Hash(second.State));
        }

        /// <summary>
        /// The negative half. Without it an engine that returned a constant would pass every
        /// determinism test in this file perfectly.
        /// </summary>
        [Fact]
        public void Advance_DiffersWhenTheSaveDiffers()
        {
            PoliticalState a = PoliticalEngine.CreateInitialState(
                SaveGuid, Start, Settings(), City(), EngineTuning.Default);

            PoliticalState b = PoliticalEngine.CreateInitialState(
                new Guid("ffffffff-4d3b-4f8a-9c1e-5b6d7f8a9c02"), Start, Settings(), City(),
                EngineTuning.Default);

            Assert.NotEqual(Hash(Advance(a, new SimDate(1990, 2, 1)).State),
                            Hash(Advance(b, new SimDate(1990, 2, 1)).State));
        }

        /// <summary>
        /// The assertion that actually protects a save: replaying twenty years from the same seed
        /// twice lands on the same politics. A single tick can be deterministic while the sequence is
        /// not — one unsorted list folded into the next tick's input is enough — so the long run is
        /// the one that matters.
        /// </summary>
        [Fact]
        public void Advance_TwentyYearReplayIsReproducible()
        {
            Assert.Equal(Hash(Run(240).State), Hash(Run(240).State));
        }

        /// <summary>
        /// Purity: the tick reports what should happen, it does not edit the caller's state. A caller
        /// that keeps the prior state to compare against — the reconciler does exactly this — must be
        /// able to trust that it still says what it said.
        /// </summary>
        [Fact]
        public void Advance_DoesNotMutateThePriorState()
        {
            PoliticalState prior = Run(60).State;
            string before = Hash(prior);

            PoliticalEngine.Advance(new EngineTickInput
            {
                SaveGuid = SaveGuid,
                Date = prior.Date.AddMonths(1),
                StartDate = Start,
                PriorState = prior,
                Snapshot = City(),
                Tuning = EngineTuning.Default
            });

            Assert.Equal(before, Hash(prior));
        }

        /// <summary>
        /// A returned state is never the object that was handed in, even on a tick where nothing was
        /// due. Sharing it would let a caller come to depend on aliasing that stops holding the moment
        /// the tick does any work.
        /// </summary>
        [Fact]
        public void Advance_NeverReturnsThePriorInstance()
        {
            PoliticalState prior = InitialState();
            Assert.NotSame(prior, Advance(prior, new SimDate(1990, 2, 1)).State);
        }

        // --- cadence -----------------------------------------------------------------------------

        /// <summary>
        /// A date before the save's political start is not an error — the clock belongs to the Mod,
        /// and a load can legitimately ask about one. Nothing is due, and nothing throws.
        /// </summary>
        [Fact]
        public void Advance_BeforeTheStartDateDoesNothing()
        {
            EngineTickResult result = Advance(InitialState(), new SimDate(1989, 6, 1));

            Assert.False(result.DidWork);
            Assert.Empty(result.EffectRequests);
            Assert.Null(result.Election);
        }

        /// <summary>
        /// The tick advances the date it was given even when no subsystem was due, so the state always
        /// describes the month it claims to.
        /// </summary>
        [Fact]
        public void Advance_CarriesTheDateForward()
        {
            Assert.Equal(new SimDate(1990, 2, 1), Advance(InitialState(), new SimDate(1990, 2, 1)).State.Date);
        }

        // --- elections ---------------------------------------------------------------------------

        /// <summary>
        /// No election is scheduled at save creation: the first one is set once
        /// <c>scheduler.warmupMonths</c> has passed, so the campaign is fought over a city the sensors
        /// have actually measured.
        /// </summary>
        [Fact]
        public void FirstElection_IsScheduledOnlyAfterWarmup()
        {
            Assert.Null(InitialState().NextElectionDate);
            Assert.NotNull(Run(24).State.NextElectionDate);
        }

        /// <summary>Twenty years of a three-year term must produce several ballots, not zero and not one.</summary>
        [Fact]
        public void Advance_HoldsElectionsOverALongRun()
        {
            PoliticalState state = Run(240).State;

            Assert.True(state.ElectionHistory.Count >= 4,
                        "Expected several elections in twenty years, got " + state.ElectionHistory.Count + ".");
        }

        /// <summary>
        /// Every election's shares are a distribution over the ballot: sorted by party id (the
        /// contract for every <see cref="PartyVoteShare"/> list) and summing to 1.
        /// </summary>
        [Fact]
        public void ElectionShares_AreSortedAndSumToOne()
        {
            PoliticalState state = Run(240).State;
            Assert.NotEmpty(state.ElectionHistory);

            foreach (ElectionResult election in state.ElectionHistory)
            {
                AssertSortedById(election.CityVoteShares);
                Assert.Equal(1.0, election.CityVoteShares.Sum(s => s.Share), 6);
            }
        }

        /// <summary>Seats add up to the chamber, and no party holds a negative number of them.</summary>
        [Fact]
        public void ElectionSeats_SumToTheChamber()
        {
            foreach (ElectionResult election in Run(240).State.ElectionHistory)
            {
                Assert.Equal(election.TotalSeats, election.Seats.Sum(s => s.Seats));
                Assert.All(election.Seats, s => Assert.True(s.Seats >= 0));
            }
        }

        /// <summary>The calendar moves forward by a whole term, so terms cannot silently overlap.</summary>
        [Fact]
        public void Elections_AreOneTermApart()
        {
            List<ElectionResult> history = Run(240).State.ElectionHistory;
            int term = EngineTuning.Default.ElectionsPr.TermYears * 12;

            for (int i = 1; i < history.Count; i++)
            {
                int gap = history[i - 1].Date.MonthsUntil(history[i].Date);
                Assert.True(gap > 0 && gap <= term,
                            "Elections " + history[i - 1].Id + " and " + history[i].Id +
                            " are " + gap + " months apart; a term is " + term + ".");
            }
        }

        // --- government --------------------------------------------------------------------------

        /// <summary>An election that produces a chamber must produce someone to sit in it.</summary>
        [Fact]
        public void Government_FormsAfterAnElection()
        {
            PoliticalState state = Run(240).State;

            Assert.True(state.Government != null || state.CoalitionHistory.Count > 0,
                        "Twenty years passed with no government ever forming.");
        }

        /// <summary>
        /// A sitting government's members are real parties and its lead is one of them. A government
        /// naming a party that does not exist would break every downstream consumer silently.
        /// </summary>
        [Fact]
        public void Government_MembersExistInTheRegistry()
        {
            PoliticalState state = Run(240).State;
            if (state.Government == null) return;

            var ids = new HashSet<string>(state.Parties.Select(p => p.Id), StringComparer.Ordinal);

            Assert.All(state.Government.MemberPartyIds, id => Assert.Contains(id, ids));
            Assert.Contains(state.Government.LeadPartyId, state.Government.MemberPartyIds);
        }

        // --- ordering ----------------------------------------------------------------------------

        /// <summary>
        /// Every persisted list leaves in its documented order. This is the assertion that catches a
        /// desync before a player does: an unsorted list changes the serialized hash of the state
        /// without anything actually being wrong.
        /// </summary>
        [Fact]
        public void State_ListsLeaveInContractualOrder()
        {
            PoliticalState state = Run(240).State;

            AssertSorted(state.Parties.Select(p => p.Id).ToList(), "Parties");
            AssertSorted(state.Mandates.Select(m => m.Id).ToList(), "Mandates");
            AssertSorted(state.ActiveEvents.Select(e => e.Id).ToList(), "ActiveEvents");
            AssertSorted(state.FiredEventIds, "FiredEventIds");
            AssertSorted(state.CurrentDistrictStandings.Select(d => d.DistrictId).ToList(), "DistrictStandings");
            AssertSortedById(state.CurrentVoteShares);
        }

        /// <summary>An event fires at most once, ever. A duplicate id means the scan reconsidered history.</summary>
        [Fact]
        public void FiredEventIds_ContainNoDuplicates()
        {
            List<string> fired = Run(240).State.FiredEventIds;
            Assert.Equal(fired.Count, fired.Distinct(StringComparer.Ordinal).Count());
        }

        // --- effects and flavor ------------------------------------------------------------------

        /// <summary>
        /// Effects are requested, never applied, and every request names an effect and a sane duration.
        /// The sink clamps them again regardless (non-negotiable #5) — this asserts the engine is not
        /// emitting nonsense for it to clamp.
        /// </summary>
        [Fact]
        public void EffectRequests_AreWellFormed()
        {
            foreach (EngineTickResult tick in RunAll(240))
            {
                foreach (EffectRequest request in tick.EffectRequests)
                {
                    Assert.False(string.IsNullOrEmpty(request.EffectId));
                    Assert.True(request.DurationMonths >= 0);

                    if (request.Scope == EffectScope.District)
                        Assert.False(string.IsNullOrEmpty(request.DistrictId));
                }
            }
        }

        /// <summary>
        /// The per-save kill switch withholds effects without stopping the politics: the engine still
        /// computes a full state, it just asks for nothing to be applied (§7, non-negotiable #10).
        /// </summary>
        [Fact]
        public void EffectsDisabled_StillComputesPoliticsAndRequestsNothing()
        {
            AgoraSettings off = Settings();
            off.EffectsEnabled = false;

            List<EngineTickResult> ticks = RunAll(120, off);

            Assert.All(ticks, t => Assert.Empty(t.EffectRequests));
            Assert.NotEmpty(ticks[ticks.Count - 1].State.Parties);
        }

        /// <summary>
        /// The yearly LLM wake fires, and only in the tuned month. The engine decides <em>whether</em>
        /// to wake; it never waits on the answer (non-negotiable #7).
        /// </summary>
        [Fact]
        public void LlmWake_FiresYearlyInTheTunedMonth()
        {
            int wakeMonth = EngineTuning.Default.Scheduler.LlmWakeMonth;

            List<EngineTickResult> yearly = RunAll(120)
                .Where(t => (t.LlmWake & LlmWakeCadence.Yearly) != 0)
                .ToList();

            Assert.NotEmpty(yearly);
            Assert.All(yearly, t => Assert.Equal(wakeMonth, t.State.Date.Month));
        }

        /// <summary>
        /// A save that switched the LLM off entirely never asks it for anything, whatever the calendar
        /// says. The cadence is per-save state, not global config (non-negotiable #10).
        /// </summary>
        [Fact]
        public void LlmWake_RespectsThePerSaveCadence()
        {
            AgoraSettings silent = Settings();
            silent.WakeCadence = LlmWakeCadence.None;

            Assert.All(RunAll(120, silent), t => Assert.Equal(LlmWakeCadence.None, t.LlmWake));
        }

        // --- resilience --------------------------------------------------------------------------

        /// <summary>
        /// A tick with no snapshot is a sensor gap, not a crash: the political state carries forward
        /// rather than being reset. This runs inside GameSimulation, where a throw takes the player's
        /// city down over something nobody asked for.
        /// </summary>
        [Fact]
        public void Advance_SurvivesAMissingSnapshot()
        {
            PoliticalState prior = Run(60).State;

            EngineTickResult result = PoliticalEngine.Advance(new EngineTickInput
            {
                SaveGuid = SaveGuid,
                Date = prior.Date.AddMonths(1),
                StartDate = Start,
                PriorState = prior,
                Snapshot = null,
                Tuning = EngineTuning.Default
            });

            Assert.NotEmpty(result.Warnings);
            Assert.Equal(prior.Parties.Count, result.State.Parties.Count);
        }

        /// <summary>A city with no districts at all still ticks. It has no blocs, so it has no politics —
        /// but it must not throw its way out of the simulation loop.</summary>
        [Fact]
        public void Advance_SurvivesACityWithNoDistricts()
        {
            var empty = City();
            empty.Districts.Clear();

            PoliticalState state = PoliticalEngine.CreateInitialState(
                SaveGuid, Start, Settings(), empty, EngineTuning.Default);

            EngineTickResult result = PoliticalEngine.Advance(new EngineTickInput
            {
                SaveGuid = SaveGuid,
                Date = new SimDate(1990, 2, 1),
                StartDate = Start,
                PriorState = state,
                Snapshot = empty,
                Tuning = EngineTuning.Default
            });

            Assert.True(result.DidWork);
            Assert.Empty(result.State.Blocs);
        }

        /// <summary>
        /// A save created with no snapshot at all — the sensors were not ready — still gets a party
        /// registry, so the first real tick has a ballot to work with.
        /// </summary>
        [Fact]
        public void CreateInitialState_GeneratesPartiesWithoutASnapshot()
        {
            PoliticalState state = PoliticalEngine.CreateInitialState(
                SaveGuid, Start, Settings(), null, EngineTuning.Default);

            Assert.NotEmpty(state.Parties);
            Assert.Empty(state.Blocs);
        }

        // --- harness -----------------------------------------------------------------------------

        private static AgoraSettings Settings() => new AgoraSettings
        {
            StartYear = Start.Year,
            Theme = RegionTheme.Eu,
            System = ElectoralSystem.Proportional
        };

        private static PoliticalState InitialState() =>
            PoliticalEngine.CreateInitialState(SaveGuid, Start, Settings(), City(), EngineTuning.Default);

        private static EngineTickResult Advance(PoliticalState prior, SimDate date) =>
            PoliticalEngine.Advance(new EngineTickInput
            {
                SaveGuid = SaveGuid,
                Date = date,
                StartDate = Start,
                PriorState = prior,
                Snapshot = City(),
                Tuning = EngineTuning.Default
            });

        /// <summary>Ticks <paramref name="months"/> months from the start date and returns the last tick.</summary>
        private static EngineTickResult Run(int months, AgoraSettings? settings = null)
        {
            List<EngineTickResult> all = RunAll(months, settings);
            return all[all.Count - 1];
        }

        /// <summary>
        /// Every tick of a run. The snapshot history is threaded through because the trend legs of the
        /// derived indices read it, and a run that never supplied one would leave them permanently
        /// zero — which would quietly make this a test of a simpler engine than the real one.
        /// </summary>
        private static List<EngineTickResult> RunAll(int months, AgoraSettings? settings = null)
        {
            CitySnapshot city = City();
            PoliticalState state = PoliticalEngine.CreateInitialState(
                SaveGuid, Start, settings ?? Settings(), city, EngineTuning.Default);

            var history = new List<CitySnapshot>();
            var results = new List<EngineTickResult>();

            for (int i = 1; i <= months; i++)
            {
                SimDate date = Start.AddMonths(i);
                CitySnapshot snapshot = City(date);

                EngineTickResult result = PoliticalEngine.Advance(new EngineTickInput
                {
                    SaveGuid = SaveGuid,
                    Date = date,
                    StartDate = Start,
                    PriorState = state,
                    Snapshot = snapshot,
                    SnapshotHistory = history,
                    Tuning = EngineTuning.Default
                });

                history.Add(snapshot);
                state = result.State;
                results.Add(result);
            }

            return results;
        }

        // --- assertions --------------------------------------------------------------------------

        private static void AssertSorted(List<string> ids, string what)
        {
            for (int i = 1; i < ids.Count; i++)
            {
                Assert.True(string.CompareOrdinal(ids[i - 1], ids[i]) <= 0,
                            what + " is not sorted: '" + ids[i - 1] + "' precedes '" + ids[i] + "'.");
            }
        }

        private static void AssertSortedById(List<PartyVoteShare> shares) =>
            AssertSorted(shares.Select(s => s.PartyId).ToList(), "vote shares");

        // --- fixtures ----------------------------------------------------------------------------

        /// <summary>
        /// A static three-district city. It does not improve or decay, so any political churn over a
        /// twenty-year run is attributable to the engine rather than to a moving target.
        /// </summary>
        private static CitySnapshot City(SimDate? date = null)
        {
            var districts = new List<DistrictSnapshot>
            {
                District("east", 40000, happiness: 42.0, unemployment: 0.14, commute: 41.0, rentBurden: 0.44),
                District("north", 60000, happiness: 55.0, unemployment: 0.07, commute: 24.0, rentBurden: 0.29),
                District("south", 50000, happiness: 61.0, unemployment: 0.05, commute: 19.0, rentBurden: 0.24)
            };

            return new CitySnapshot
            {
                Date = date ?? Start,
                Population = districts.Sum(d => d.Population),
                Households = districts.Sum(d => d.Households),
                Happiness = 52.0,
                Unemployment = 0.09,
                Money = 250000,
                Income = 18000,
                Expenses = 15000,
                BudgetBalance = 3000,
                Debt = 0,
                Wealth = new WealthDistribution(0.36, 0.44, 0.20),
                Education = new EducationDistribution(0.14, 0.22, 0.30, 0.22, 0.12),
                Age = new AgeDistribution(0.18, 0.10, 0.55, 0.17),
                Pollution = new PollutionLevels(0.24, 0.18, 0.30, 0.11),
                Services = Services(0.68),
                Taxes = new TaxRates(0.11, 0.10, 0.09, 0.10),
                CrimeRate = 0.12,
                SickRate = 0.06,
                AverageLandValue = 1200.0,
                LandValueTrend = 0.02,
                AverageRent = 900.0,
                RentTrend = 0.03,
                RentBurden = 0.32,
                TransitRidership = 0.22,
                AverageCommuteMinutes = 28.0,
                TrafficCongestion = 0.34,
                Districts = districts
            };
        }

        private static DistrictSnapshot District(string id, int population, double happiness,
                                                 double unemployment, double commute, double rentBurden)
        {
            return new DistrictSnapshot
            {
                Id = id,
                Name = id,
                Population = population,
                Households = population / 2,
                Happiness = happiness,
                Unemployment = unemployment,
                Wealth = new WealthDistribution(0.36, 0.44, 0.20),
                Education = new EducationDistribution(0.14, 0.22, 0.30, 0.22, 0.12),
                Age = new AgeDistribution(0.18, 0.10, 0.55, 0.17),
                Pollution = new PollutionLevels(0.24, 0.18, 0.30, 0.11),
                Services = Services(0.68),
                CrimeRate = 0.12,
                SickRate = 0.06,
                AverageLandValue = 1200.0,
                LandValueTrend = 0.02,
                AverageRent = 900.0,
                RentTrend = 0.03,
                RentBurden = rentBurden,
                TransitRidership = 0.22,
                AverageCommuteMinutes = commute,
                TrafficCongestion = 0.34,
                HasCityFallbacks = false,
                CityFallbackFields = new List<string>()
            };
        }

        private static ServiceCoverage Services(double level) =>
            new ServiceCoverage(level, level, level, level, level, level, level, level, level);

        /// <summary>
        /// A stable digest of everything persisted. Deliberately covers the whole state rather than
        /// the fields a given test is about: "desync" is defined as this hash changing across a
        /// reload, so the test's definition and the contract's should be the same one.
        /// </summary>
        private static string Hash(PoliticalState state)
        {
            var text = new StringBuilder();

            text.Append(state.Date).Append('|').Append(state.TermNumber).Append('|')
                .Append(state.NextElectionDate).Append('|').Append(state.IsCampaignSeason ? '1' : '0')
                .Append('|').Append(state.MayorPartyId ?? "-").Append('\n');

            foreach (Party p in state.Parties)
            {
                text.Append(p.Id).Append(';').Append(p.Status).Append(';')
                    .Append(N(p.LastVoteShare)).Append(';').Append(p.SeatsHeld).Append(';')
                    .Append(p.IsInGovernment ? '1' : '0').Append(';')
                    .Append(p.ConsecutiveElectionsBelowThreshold).Append('\n');
            }

            foreach (Faction f in state.Factions)
                text.Append(f.Id).Append(';').Append(f.PartyId).Append(';')
                    .Append(N(f.InternalSupport)).Append(';').Append(f.Status).Append('\n');

            foreach (Bloc b in state.Blocs)
                text.Append(b.DistrictId).Append(';').Append(b.Key.Id).Append(';')
                    .Append(b.Population).Append(';').Append(N(b.Discontent)).Append('\n');

            foreach (PartyVoteShare s in state.CurrentVoteShares)
                text.Append(s.PartyId).Append('=').Append(N(s.Share)).Append(';');
            text.Append('\n');

            foreach (DistrictResult d in state.CurrentDistrictStandings)
            {
                text.Append(d.DistrictId).Append(';').Append(d.WinningPartyId).Append(';')
                    .Append(N(d.Turnout)).Append(';').Append(d.VotesCast).Append('\n');
            }

            foreach (ElectionResult e in state.ElectionHistory)
            {
                text.Append(e.Id).Append(';').Append(e.Date).Append(';').Append(e.TotalSeats).Append(';')
                    .Append(N(e.Turnout)).Append(';');
                foreach (SeatAllocation a in e.Seats) text.Append(a.PartyId).Append(':').Append(a.Seats).Append(',');
                text.Append('\n');
            }

            foreach (PollResult p in state.RecentPolls)
                text.Append(p.Id).Append(';').Append(p.PollsterId).Append(';').Append(N(p.ProjectedTurnout)).Append('\n');

            if (state.Government != null)
            {
                Coalition g = state.Government;
                text.Append(g.Id).Append(';').Append(g.LeadPartyId).Append(';')
                    .Append(string.Join(",", g.MemberPartyIds)).Append(';')
                    .Append(N(g.Stability)).Append(';').Append(g.Status).Append('\n');
            }

            foreach (Coalition g in state.CoalitionHistory)
                text.Append(g.Id).Append(';').Append(g.Status).Append(';').Append(g.CollapseReason).Append('\n');

            foreach (Mandate m in state.Mandates)
                text.Append(m.Id).Append(';').Append(m.Status).Append(';').Append(N(m.Progress)).Append('\n');

            foreach (TimelineEvent e in state.ActiveEvents)
                text.Append(e.Id).Append(';').Append(e.Severity).Append(';').Append(e.ExpiresDate).Append('\n');

            text.Append(string.Join(",", state.FiredEventIds)).Append('\n');

            foreach (Issue issue in Issues.All)
                text.Append(N(state.Indices.DiscontentIndex)).Append(',');
            text.Append(N(state.Indices.LegitimacyIndex)).Append(',')
                .Append(N(state.Indices.PolarizationIndex)).Append('\n');

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
                return BitConverter.ToString(hash).Replace("-", "");
            }
        }

        private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
