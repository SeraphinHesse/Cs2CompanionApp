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
    /// Packet 15 end to end — the fringe ceiling as it behaves inside a real NA run, rather than as
    /// arithmetic on a hand-built row.
    ///
    /// <para>
    /// The single-packet suites prove the cap holds on a row and that the streak advances correctly.
    /// Neither can see the thing that actually matters: that the cap reaches all three surfaces which
    /// report support. City standings, published polls and election-day results are computed by three
    /// different code paths, and the whole reason the cap is applied in affinity space is that one
    /// edit should cover all three. If that reasoning is wrong, it is wrong here and nowhere else.
    /// </para>
    ///
    /// <para>
    /// The fixture city is deliberately unhappy and static. Static so political churn is attributable
    /// to the engine and not to a moving city; unhappy so that the fringe parties have something to
    /// be popular about, which is the case the ceiling exists for.
    /// </para>
    /// </summary>
    public class FringeCeilingEndToEndTests
    {
        private static readonly Guid SaveGuid = new Guid("c0ffee11-2222-3333-4444-555566667777");
        private static readonly SimDate Start = new SimDate(1990, 1, 1);

        private static AgoraSettings NaSettings() => new AgoraSettings
        {
            StartYear = Start.Year,
            Theme = RegionTheme.Na,
            System = ElectoralSystem.FirstPastThePost
        };

        // ------------------------------------------------------------------------------------------
        // The headline behaviour
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// The whole point, stated once. Over twenty years of a city whose majors are never given a
        /// reason to fail three terms running, no minor party may ever poll above 3% — not in the
        /// standings, not in a published poll, and not in an election result.
        /// </summary>
        [Fact]
        public void OverTwentyYears_NoMinorPartyEverPassesTheCeiling()
        {
            List<EngineTickResult> run = RunAll(240);
            EngineTuning tuning = EngineTuning.Default;
            double ceiling = tuning.Fringe.BaseCeiling + 1e-9;

            PoliticalState last = run[run.Count - 1].State!;
            HashSet<string> minors = MinorIds(last.Parties);
            Assert.NotEmpty(minors);

            foreach (EngineTickResult tick in run)
            {
                PoliticalState s = tick.State!;

                foreach (PartyVoteShare share in s.CurrentVoteShares)
                {
                    if (!minors.Contains(share.PartyId)) continue;
                    Assert.True(share.Share <= ceiling,
                        "city standing for " + share.PartyId + " reached " + share.Share + " at " + s.Date);
                }

                // Polls are allowed to be wrong — that is what the polling packet is for — so they are
                // held to the ceiling plus a generous multiple of the published error rather than to
                // the ceiling itself. What is being asserted is that a poll is noise around a
                // suppressed truth, not that it has escaped the suppression.
                double pollTolerance = ceiling + 6.0 * tuning.Polling.ErrorSigma;

                foreach (PollResult poll in s.RecentPolls)
                {
                    foreach (PartyVoteShare share in poll.Shares)
                    {
                        if (!minors.Contains(share.PartyId)) continue;
                        Assert.True(share.Share <= pollTolerance,
                            "poll " + poll.Id + " put " + share.PartyId + " at " + share.Share);
                    }
                }

                // Election day is the capped truth plus the two things the FPTP packet applies after
                // the softmax: a per-district gaussian swing at electionsFptp.districtSwingSigma, and
                // mayoral coattails. Both are legitimate — districts do differ from the city — so the
                // bound here is the ceiling plus one sigma of swing rather than the ceiling itself.
                // It still fails loudly on anything resembling a runaway.
                double electionTolerance = ceiling + tuning.ElectionsFptp.DistrictSwingSigma;

                foreach (ElectionResult election in s.ElectionHistory)
                {
                    foreach (PartyVoteShare share in election.CityVoteShares)
                    {
                        if (!minors.Contains(share.PartyId)) continue;
                        Assert.True(share.Share <= electionTolerance,
                            "election " + election.Id + " gave " + share.PartyId + " " + share.Share);
                    }
                }
            }
        }

        /// <summary>
        /// The premise, and the reason the ceiling had to exist. With the packet switched off the same
        /// city hands its minor parties a large share for no reason other than that they are on the
        /// ballot — nothing in the voter model was ever holding them down.
        /// </summary>
        [Fact]
        public void WithoutTheCeiling_MinorPartiesRunAwayWithTheVote()
        {
            EngineTuning off = EngineTuning.FromJson("{\"fringe\":{\"enabled\":false}}");

            PoliticalState last = RunAll(120, off)[119].State!;
            HashSet<string> minors = MinorIds(last.Parties);

            double total = 0.0;
            foreach (PartyVoteShare s in last.CurrentVoteShares)
                if (minors.Contains(s.PartyId)) total += s.Share;

            Assert.True(total > 0.20,
                "fixture is not exercising the problem: uncapped minor total was only " + total);
        }

        /// <summary>
        /// The consequence that makes the mechanic real rather than cosmetic: a suppressed party
        /// cannot win a district, and the mayoralty stays with a major.
        /// </summary>
        [Fact]
        public void OverTwentyYears_NoMinorPartyEverTakesTheMayoralty()
        {
            List<EngineTickResult> run = RunAll(240);
            PoliticalState last = run[run.Count - 1].State!;
            HashSet<string> minors = MinorIds(last.Parties);

            Assert.NotEmpty(last.ElectionHistory);

            foreach (ElectionResult election in last.ElectionHistory)
            {
                Assert.False(election.MayorPartyId != null && minors.Contains(election.MayorPartyId),
                    "election " + election.Id + " elected a suppressed party to the mayoralty");
            }
        }

        /// <summary>
        /// The failure mode that made the death threshold move. A party pinned at exactly the ceiling
        /// posts that share at every election; if the death threshold could reach it, the ceiling
        /// would dissolve the ballot it exists to shape, and would do it long before the unlock could
        /// ever fire. Both minors must still be contesting after twenty years.
        /// </summary>
        [Fact]
        public void OverTwentyYears_TheSuppressedPartiesAreStillOnTheBallot()
        {
            PoliticalState last = RunAll(240)[239].State!;

            var survivors = new List<string>();
            foreach (Party p in last.Parties)
            {
                if (p.IsMajor) continue;
                if (p.Status == PartyStatus.Active || p.Status == PartyStatus.Endangered ||
                    p.Status == PartyStatus.Revived) survivors.Add(p.Id);
            }

            Assert.Equal(EngineTuning.Default.Parties.MinorPartyCountNa, survivors.Count);
        }

        // ------------------------------------------------------------------------------------------
        // The unlock, in a live engine
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// The other half of the mechanic. Every test above proves the ceiling holds; this one proves
        /// it can be opened, through the live engine rather than by calling the ceiling model directly.
        ///
        /// <para>
        /// The failure streak is written onto the state rather than earned over sixty simulated years
        /// of deliberately-terrible governance. That is the honest way round: the streak is earned in
        /// <c>FringeFailureTests</c>, which drives it through Observe and CloseTerm; what is untested
        /// until here is the wiring between a streak that exists and a fringe party that actually
        /// gains votes for it.
        /// </para>
        /// </summary>
        [Fact]
        public void AfterThreeFailureTerms_TheAggrievedFringePartyBreaksThroughTheCeiling()
        {
            EngineTuning tuning = EngineTuning.Default;

            PoliticalState pinned = RunAll(60)[59].State!;
            Party minor = pinned.Parties.First(p => !p.IsMajor && OnBallot(p.Status));
            double before = ShareOf(pinned.CurrentVoteShares, minor.Id);

            // Sanity: it is currently suppressed.
            Assert.True(before <= tuning.Fringe.BaseCeiling + 1e-9);

            // Three consecutive failure terms, scored as a near-total collapse.
            PoliticalState unlocked = ClonedWithStreak(pinned, tuning.Fringe.UnlockConsecutiveTerms, 1.0);

            EngineTickResult next = PoliticalEngine.Advance(new EngineTickInput
            {
                SaveGuid = SaveGuid,
                Date = unlocked.Date.AddMonths(1),
                StartDate = Start,
                PriorState = unlocked,
                Snapshot = City(unlocked.Date.AddMonths(1)),
                Tuning = tuning
            });

            double after = ShareOf(next.State!.CurrentVoteShares, minor.Id);

            Assert.True(after > tuning.Fringe.BaseCeiling,
                "the unlock is unreachable: " + minor.Id + " still polls " + after +
                " after " + tuning.Fringe.UnlockConsecutiveTerms + " failure terms");
            Assert.True(after <= tuning.Fringe.MaxCeiling + 1e-9);
        }

        /// <summary>
        /// One term short of the unlock changes nothing at all. This is the assertion that makes
        /// "repeatedly" mean what the ratified rule says it means.
        /// </summary>
        [Fact]
        public void OneTermShortOfTheUnlock_NothingMoves()
        {
            EngineTuning tuning = EngineTuning.Default;
            PoliticalState pinned = RunAll(60)[59].State!;
            Party minor = pinned.Parties.First(p => !p.IsMajor && OnBallot(p.Status));

            PoliticalState almost = ClonedWithStreak(pinned, tuning.Fringe.UnlockConsecutiveTerms - 1, 1.0);

            EngineTickResult next = PoliticalEngine.Advance(new EngineTickInput
            {
                SaveGuid = SaveGuid,
                Date = almost.Date.AddMonths(1),
                StartDate = Start,
                PriorState = almost,
                Snapshot = City(almost.Date.AddMonths(1)),
                Tuning = tuning
            });

            Assert.True(ShareOf(next.State!.CurrentVoteShares, minor.Id) <= tuning.Fringe.BaseCeiling + 1e-9);
        }

        private static PoliticalState ClonedWithStreak(PoliticalState source, int terms, double score)
        {
            PoliticalState copy = PoliticalEngine.Advance(new EngineTickInput
            {
                SaveGuid = SaveGuid,
                Date = source.Date,
                StartDate = Start,
                PriorState = source,
                Snapshot = City(source.Date),
                Tuning = EngineTuning.Default
            }).State!;

            copy.Fringe.ConsecutiveFailureTerms = terms;
            copy.Fringe.LastTermFailureScore = score;
            copy.Fringe.LastClosedTermNumber = copy.TermNumber;
            return copy;
        }

        private static double ShareOf(IEnumerable<PartyVoteShare> shares, string partyId)
        {
            foreach (PartyVoteShare s in shares) if (s.PartyId == partyId) return s.Share;
            return 0.0;
        }

        // ------------------------------------------------------------------------------------------
        // EU is not touched
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Multiparty PR is supposed to have viable small parties, and it has its own 5% electoral
        /// threshold doing a different job. Running the EU theme with the packet on must be
        /// bit-identical to running it with the packet off.
        /// </summary>
        [Fact]
        public void UnderProportional_ThePacketIsCompletelyInert()
        {
            var eu = new AgoraSettings
            {
                StartYear = Start.Year,
                Theme = RegionTheme.Eu,
                System = ElectoralSystem.Proportional
            };

            EngineTuning off = EngineTuning.FromJson("{\"fringe\":{\"enabled\":false}}");

            string on = Hash(RunAll(120, EngineTuning.Default, eu)[119].State!);
            string without = Hash(RunAll(120, off, eu)[119].State!);

            Assert.Equal(on, without);
        }

        // ------------------------------------------------------------------------------------------
        // Determinism
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Twenty years of NA politics, replayed. This is the test that catches a FringeWatch shared
        /// by reference instead of deep-copied: the watch is mutated on every tick, so an alias shows
        /// up as a divergence long before anything else does.
        /// </summary>
        [Fact]
        public void TwentyYearNaReplayIsReproducible()
        {
            Assert.Equal(Hash(RunAll(240)[239].State!), Hash(RunAll(240)[239].State!));
        }

        [Fact]
        public void Advance_DoesNotMutateThePriorStatesFringeWatch()
        {
            List<EngineTickResult> run = RunAll(36);
            PoliticalState prior = run[run.Count - 2].State!;
            FringeWatch before = prior.Fringe.Clone();

            PoliticalEngine.Advance(new EngineTickInput
            {
                SaveGuid = SaveGuid,
                Date = prior.Date.AddMonths(1),
                StartDate = Start,
                PriorState = prior,
                Snapshot = City(prior.Date.AddMonths(1)),
                Tuning = EngineTuning.Default
            });

            Assert.Equal(before.MonthsObserved, prior.Fringe.MonthsObserved);
            Assert.Equal(before.DiscontentSum, prior.Fringe.DiscontentSum, 12);
            Assert.Equal(before.ConsecutiveFailureTerms, prior.Fringe.ConsecutiveFailureTerms);
        }

        // ------------------------------------------------------------------------------------------
        // The watch is actually being fed
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// A guard against the mechanic being silently dead. If the engine never observes a tick, or
        /// never closes a term, every assertion above would still pass — the ceiling would simply be
        /// shut forever for the wrong reason.
        /// </summary>
        [Fact]
        public void TheEngineObservesTicksAndClosesTerms()
        {
            List<EngineTickResult> run = RunAll(240);

            Assert.True(run[239].State!.Fringe.LastClosedTermNumber > 1,
                "no term was ever closed, so the failure streak can never move");

            // Sampled mid-term rather than at the end: month 240 lands on an election, and CloseTerm
            // has just zeroed the accumulator by then. Checking only the final tick would read a
            // correctly-reset ledger as a dead one.
            Assert.True(run[125].State!.Fringe.MonthsObserved > 0,
                "the open accumulator is empty mid-term, so nothing is being observed");
        }

        /// <summary>
        /// Mandates must resolve inside the term that issued them, or defiance — the largest single
        /// input to the failure score — can never fire and the unlock is unreachable in practice.
        /// This is what the 12-month horizon and 1-month grace were retuned for.
        /// </summary>
        [Fact]
        public void MandatesReachATerminalStatusRatherThanExpiringUnscored()
        {
            PoliticalState last = RunAll(240)[239].State!;

            int resolved = 0;
            foreach (Mandate m in last.Mandates)
            {
                if (m.Status == MandateStatus.Fulfilled || m.Status == MandateStatus.Defied ||
                    m.Status == MandateStatus.PartiallyFulfilled) resolved++;
            }

            Assert.True(resolved > 0,
                "no mandate ever resolved across twenty years, so the defiance signal is dead");
        }

        // ------------------------------------------------------------------------------------------
        // Fixtures
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// A capped party is normally <see cref="PartyStatus.Endangered"/> rather than Active, because
        /// 3% sits under <c>parties.endangeredVoteShareThreshold</c> (5%). That is the warning band
        /// doing its job on a share the engine itself imposed, and it is harmless — the death counter
        /// only starts below <c>deathVoteShareThreshold</c> (1%), which the ceiling keeps it clear of.
        /// Endangered parties still contest every election.
        /// </summary>
        private static bool OnBallot(PartyStatus status) =>
            status == PartyStatus.Active || status == PartyStatus.Endangered || status == PartyStatus.Revived;

        private static HashSet<string> MinorIds(IEnumerable<Party> parties)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Party p in parties) if (!p.IsMajor) ids.Add(p.Id);
            return ids;
        }

        private static List<EngineTickResult> RunAll(int months, EngineTuning? tuning = null,
                                                     AgoraSettings? settings = null)
        {
            EngineTuning t = tuning ?? EngineTuning.Default;
            AgoraSettings s = settings ?? NaSettings();

            CitySnapshot city = City();
            PoliticalState state = PoliticalEngine.CreateInitialState(SaveGuid, Start, s, city, t);

            var history = new List<CitySnapshot>();
            var results = new List<EngineTickResult>();

            for (int i = 1; i <= months; i++)
            {
                SimDate date = Start.AddMonths(i);
                CitySnapshot snapshot = City(date);
                history.Add(snapshot);

                EngineTickResult result = PoliticalEngine.Advance(new EngineTickInput
                {
                    SaveGuid = SaveGuid,
                    Date = date,
                    StartDate = Start,
                    PriorState = state,
                    Snapshot = snapshot,
                    SnapshotHistory = history,
                    Tuning = t
                });

                results.Add(result);
                state = result.State!;
            }

            return results;
        }

        /// <summary>
        /// A city with real problems — high rent, long commutes, patchy happiness — so the minor
        /// parties have grievances to own. Static across the run, so the churn is the engine's.
        /// </summary>
        private static CitySnapshot City(SimDate? date = null)
        {
            var districts = new List<DistrictSnapshot>
            {
                District("east", 40000, happiness: 38.0, unemployment: 0.17, commute: 46.0, rentBurden: 0.49),
                District("north", 60000, happiness: 51.0, unemployment: 0.09, commute: 27.0, rentBurden: 0.34),
                District("south", 50000, happiness: 58.0, unemployment: 0.06, commute: 21.0, rentBurden: 0.26)
            };

            return new CitySnapshot
            {
                Date = date ?? Start,
                Population = districts.Sum(d => d.Population),
                Households = districts.Sum(d => d.Households),
                Happiness = 48.0,
                Unemployment = 0.11,
                Money = 250000,
                Income = 18000,
                Expenses = 15000,
                BudgetBalance = 3000,
                Debt = 0,
                Wealth = new WealthDistribution(0.36, 0.44, 0.20),
                Education = new EducationDistribution(0.14, 0.22, 0.30, 0.22, 0.12),
                Age = new AgeDistribution(0.18, 0.10, 0.55, 0.17),
                Pollution = new PollutionLevels(0.34, 0.28, 0.40, 0.19),
                Services = Services(0.55),
                Taxes = new TaxRates(0.13, 0.12, 0.11, 0.12),
                CrimeRate = 0.19,
                SickRate = 0.09,
                AverageLandValue = 1200.0,
                LandValueTrend = 0.02,
                AverageRent = 1050.0,
                RentTrend = 0.05,
                RentBurden = 0.37,
                TransitRidership = 0.18,
                AverageCommuteMinutes = 33.0,
                TrafficCongestion = 0.46,
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
                Pollution = new PollutionLevels(0.34, 0.28, 0.40, 0.19),
                Services = Services(0.55),
                CrimeRate = 0.19,
                SickRate = 0.09,
                AverageLandValue = 1200.0,
                LandValueTrend = 0.02,
                AverageRent = 1050.0,
                RentTrend = 0.05,
                RentBurden = rentBurden,
                TransitRidership = 0.18,
                AverageCommuteMinutes = commute,
                TrafficCongestion = 0.46,
                HasCityFallbacks = false,
                CityFallbackFields = new List<string>()
            };
        }

        private static ServiceCoverage Services(double level) =>
            new ServiceCoverage(level, level, level, level, level, level, level, level, level);

        private static string Hash(PoliticalState state)
        {
            var sb = new StringBuilder();
            sb.Append(state.Date).Append('|').Append(state.TermNumber).Append('|');

            foreach (Party p in state.Parties.OrderBy(p => p.Id, StringComparer.Ordinal))
            {
                sb.Append(p.Id).Append(':').Append(p.Status).Append(':').Append(p.IsMajor).Append(':')
                  .Append(p.LastVoteShare.ToString("R", CultureInfo.InvariantCulture)).Append(';');
            }

            foreach (PartyVoteShare s in state.CurrentVoteShares)
                sb.Append(s.PartyId).Append('=').Append(s.Share.ToString("R", CultureInfo.InvariantCulture)).Append(';');

            foreach (ElectionResult e in state.ElectionHistory)
                sb.Append(e.Id).Append('/').Append(e.MayorPartyId).Append(';');

            sb.Append(state.Fringe.ConsecutiveFailureTerms).Append('|')
              .Append(state.Fringe.LastClosedTermNumber).Append('|')
              .Append(state.Fringe.LastTermFailureScore.ToString("R", CultureInfo.InvariantCulture));

            using (var sha = SHA256.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
            }
        }
    }
}
