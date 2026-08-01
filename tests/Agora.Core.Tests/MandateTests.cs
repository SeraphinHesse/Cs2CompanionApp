using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Engine.Government.Mandates;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Packet 10 — mandates: generation from measured deficits, monthly monitoring against the live
    /// snapshot, and resolution with its capped consequence.
    ///
    /// <para>
    /// Fixtures are synthetic <see cref="CitySnapshot"/> objects built here rather than recorded JSON:
    /// they diff cleanly and they do not rot when the snapshot schema gains a field
    /// (<c>tests/CLAUDE.md</c>).
    /// </para>
    /// </summary>
    public class MandateTests
    {
        private static readonly Guid SaveA = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        private static readonly SimDate Jun1994 = new SimDate(1994, 6, 1);

        private const string PartyId = "party-green";
        private const string GovId = "gov-1994-06";

        // =========================================================================================
        // Fixtures
        // =========================================================================================

        private static DistrictSnapshot District(
            string id,
            double groundPollution = 0.20,
            double healthCoverage = 0.80,
            double crimeRate = 0.20,
            int population = 10000,
            params string[] cityFallbackFields)
        {
            var fallbacks = new List<string>(cityFallbackFields);
            fallbacks.Sort(StringComparer.Ordinal);

            return new DistrictSnapshot
            {
                Id = id,
                Name = id,
                Population = population,
                Households = population / 2,
                Happiness = 60.0,
                Unemployment = 0.08,
                Wealth = new WealthDistribution(0.3, 0.5, 0.2),
                Education = new EducationDistribution(0.1, 0.2, 0.4, 0.2, 0.1),
                Age = new AgeDistribution(0.2, 0.1, 0.55, 0.15),
                Pollution = new PollutionLevels(0.20, groundPollution, 0.20, 0.10),
                Services = new ServiceCoverage(healthCoverage, 0.8, 0.8, 0.8, 0.8, 0.5, 0.9, 0.9, 0.6),
                CrimeRate = crimeRate,
                SickRate = 0.05,
                AverageLandValue = 1000,
                AverageRent = 900,
                RentBurden = 0.30,
                TransitRidership = 0.25,
                AverageCommuteMinutes = 24,
                TrafficCongestion = 0.4,
                HasCityFallbacks = fallbacks.Count > 0,
                CityFallbackFields = fallbacks
            };
        }

        private static CitySnapshot Snapshot(SimDate date, params DistrictSnapshot[] districts)
        {
            var list = new List<DistrictSnapshot>(districts);
            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            int population = 0;
            for (int i = 0; i < list.Count; i++) population += list[i].Population;

            return new CitySnapshot
            {
                Date = date,
                Population = population,
                Households = population / 2,
                Happiness = 60.0,
                Unemployment = 0.08,
                Money = 500000,
                Income = 40000,
                Expenses = 35000,
                BudgetBalance = 5000,
                Debt = 100000,
                Wealth = new WealthDistribution(0.3, 0.5, 0.2),
                Education = new EducationDistribution(0.1, 0.2, 0.4, 0.2, 0.1),
                Age = new AgeDistribution(0.2, 0.1, 0.55, 0.15),
                Pollution = new PollutionLevels(0.20, 0.35, 0.20, 0.10),
                Services = new ServiceCoverage(0.8, 0.8, 0.8, 0.8, 0.8, 0.5, 0.9, 0.9, 0.6),
                Taxes = new TaxRates(0.10, 0.10, 0.10, 0.10),
                CrimeRate = 0.50,
                SickRate = 0.05,
                AverageLandValue = 1000,
                AverageRent = 900,
                RentBurden = 0.30,
                TransitRidership = 0.25,
                AverageCommuteMinutes = 24,
                TrafficCongestion = 0.4,
                Districts = list
            };
        }

        /// <summary>Four districts with a wide spread on ground pollution, health and crime.</summary>
        private static CitySnapshot SpreadCity() => Snapshot(
            Jun1994,
            District("district-a", groundPollution: 0.05, healthCoverage: 0.95, crimeRate: 0.05),
            District("district-b", groundPollution: 0.60, healthCoverage: 0.40, crimeRate: 0.55),
            District("district-c", groundPollution: 0.45, healthCoverage: 0.55, crimeRate: 0.40),
            District("district-d", groundPollution: 0.30, healthCoverage: 0.70, crimeRate: 0.25));

        private static List<Bloc> Blocs(CitySnapshot snapshot, IssueWeights? weights = null)
        {
            var blocs = new List<Bloc>();
            IssueWeights w = weights ?? IssueWeights.Uniform;

            for (int i = 0; i < snapshot.Districts.Count; i++)
            {
                DistrictSnapshot d = snapshot.Districts[i];

                blocs.Add(new Bloc
                {
                    DistrictId = d.Id,
                    Key = new BlocKey(WealthTier.Middle, EducationTier.Educated, AgeBand.Adult),
                    Population = d.Population / 2,
                    PopulationShare = 0.5,
                    EligibleVoters = d.Population / 2,
                    Weights = w
                });

                blocs.Add(new Bloc
                {
                    DistrictId = d.Id,
                    Key = new BlocKey(WealthTier.Low, EducationTier.PoorlyEducated, AgeBand.Adult),
                    Population = d.Population / 2,
                    PopulationShare = 0.5,
                    EligibleVoters = d.Population / 2,
                    Weights = w
                });
            }

            // The contract sorts blocs by district id then bloc ordinal; the engine relies on it.
            blocs.Sort((a, b) =>
            {
                int byDistrict = string.CompareOrdinal(a.DistrictId, b.DistrictId);
                return byDistrict != 0 ? byDistrict : a.Key.Ordinal.CompareTo(b.Key.Ordinal);
            });

            return blocs;
        }

        private static Coalition Government() => new Coalition
        {
            Id = GovId,
            FormedDate = Jun1994,
            MemberPartyIds = new List<string> { PartyId, "party-liberal" },
            LeadPartyId = PartyId,
            OppositionPartyIds = new List<string> { "party-order" },
            Seats = 26,
            SeatShare = 0.52,
            HasMajority = true,
            Status = CoalitionStatus.Governing,
            ElectionId = "election-1994"
        };

        private static Mandate LiveMandate(
            MandateMetric metric,
            string? districtId,
            double baseline,
            double target,
            double salience = 1.0,
            MandateStatus status = MandateStatus.Active,
            SimDate? issued = null,
            int horizonMonths = 24)
        {
            SimDate issuedDate = issued ?? Jun1994;

            return new Mandate
            {
                Id = "mandate-1994-06-01",
                PartyId = PartyId,
                CoalitionId = GovId,
                DistrictId = districtId,
                Issue = MandateMetrics.IssueFor(metric),
                Metric = metric,
                Direction = target > baseline ? MandateDirection.Increase : MandateDirection.Decrease,
                BaselineValue = baseline,
                TargetValue = target,
                CurrentValue = baseline,
                Progress = 0.0,
                IssuedDate = issuedDate,
                DeadlineDate = issuedDate.AddMonths(horizonMonths),
                Status = status,
                Salience = salience
            };
        }

        private static string Canon(IEnumerable<Mandate> mandates)
        {
            var sb = new StringBuilder();
            foreach (Mandate m in mandates)
            {
                sb.Append(m.Id).Append('|')
                  .Append(m.PartyId).Append('|')
                  .Append(m.CoalitionId).Append('|')
                  .Append(m.DistrictId ?? "-").Append('|')
                  .Append(m.Issue).Append('|')
                  .Append(m.Metric).Append('|')
                  .Append(m.Direction).Append('|')
                  .Append(R(m.BaselineValue)).Append('|')
                  .Append(R(m.TargetValue)).Append('|')
                  .Append(R(m.CurrentValue)).Append('|')
                  .Append(R(m.Progress)).Append('|')
                  .Append(m.IssuedDate).Append('|')
                  .Append(m.DeadlineDate).Append('|')
                  .Append(m.ResolvedDate.HasValue ? m.ResolvedDate.Value.ToString() : "-").Append('|')
                  .Append(m.Status).Append('|')
                  .Append(R(m.Salience)).Append('|')
                  .Append(m.ResolutionEffectId ?? "-").Append('|')
                  .Append(m.IsMeasurementStalled)
                  .Append('\n');
            }

            return sb.ToString();
        }

        private static string Canon(MandateTickResult result)
        {
            var sb = new StringBuilder(Canon(result.Mandates));

            foreach (MandateResolution r in result.Resolutions)
            {
                sb.Append("R|").Append(r.MandateId).Append('|').Append(r.Status).Append('|')
                  .Append(R(r.HappinessDelta)).Append('|').Append(R(r.LegitimacyDelta)).Append('|')
                  .Append(R(r.OppositionSurge)).Append('|').Append(r.UnrestTriggered).Append('|')
                  .Append(r.ResolutionEffectId ?? "-").Append('\n');
            }

            foreach (EffectRequest e in result.Effects)
            {
                sb.Append("E|").Append(e.EffectId).Append('|').Append(e.Scope).Append('|')
                  .Append(R(e.Magnitude)).Append('|').Append(e.DurationMonths).Append('|')
                  .Append(e.DistrictId ?? "-").Append('|').Append(e.SourceId ?? "-").Append('\n');
            }

            return sb.ToString();
        }

        private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static string Hash(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
            }
        }

        // =========================================================================================
        // Determinism
        // =========================================================================================

        [Fact]
        public void Generate_ProducesIdenticalOutputTwice()
        {
            CitySnapshot city = SpreadCity();
            List<Bloc> blocs = Blocs(city);
            EngineTuning tuning = EngineTuning.Default;

            string first = Hash(Canon(
                MandateGenerator.Generate(SaveA, Jun1994, city, Government(), blocs, null, tuning)));
            string second = Hash(Canon(
                MandateGenerator.Generate(SaveA, Jun1994, city, Government(), blocs, null, tuning)));

            Assert.Equal(first, second);
        }

        /// <summary>
        /// The negative half of the determinism pair. Without it, a generator that always returned the
        /// same three mandates — or an empty list — would pass the stability test perfectly.
        /// </summary>
        [Fact]
        public void Generate_SelectionVariesBySave()
        {
            CitySnapshot city = SpreadCity();
            List<Bloc> blocs = Blocs(city);
            EngineTuning tuning = EngineTuning.Default;

            var distinct = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < 24; i++)
            {
                var save = new Guid(i.ToString("D8", CultureInfo.InvariantCulture) +
                                    "-0000-0000-0000-000000000000");

                IReadOnlyList<Mandate> issued =
                    MandateGenerator.Generate(save, Jun1994, city, Government(), blocs, null, tuning);

                distinct.Add(string.Join(",",
                    issued.Select(m => (m.DistrictId ?? "city") + ":" + m.Metric)));
            }

            Assert.True(distinct.Count > 1,
                "24 different saves produced one identical mandate set — the seeded selection is not wired up.");
        }

        [Fact]
        public void Tick_ProducesIdenticalOutputTwice()
        {
            CitySnapshot city = SpreadCity();
            EngineTuning tuning = EngineTuning.Default;

            var mandates = new List<Mandate>
            {
                LiveMandate(MandateMetric.GroundPollution, "district-b", 0.60, 0.50),
                LiveMandate(MandateMetric.CrimeRate, null, 0.50, 0.40)
            };
            mandates[1].Id = "mandate-1994-06-02";

            SimDate deadline = Jun1994.AddMonths(24);

            string first = Hash(Canon(MandateMonitor.Tick(SaveA, deadline, city, mandates, tuning)));
            string second = Hash(Canon(MandateMonitor.Tick(SaveA, deadline, city, mandates, tuning)));

            Assert.Equal(first, second);
        }

        [Fact]
        public void Tick_DoesNotMutateItsInput()
        {
            CitySnapshot city = SpreadCity();
            var mandate = LiveMandate(MandateMetric.GroundPollution, "district-b", 0.60, 0.50);
            string before = Canon(new[] { mandate });

            MandateMonitor.Tick(SaveA, Jun1994.AddMonths(24), city, new[] { mandate }, EngineTuning.Default);

            Assert.Equal(before, Canon(new[] { mandate }));
        }

        [Fact]
        public void Generate_IdsAreUniqueAndSorted()
        {
            CitySnapshot city = SpreadCity();
            EngineTuning tuning = EngineTuning.FromJson("{\"mandates\":{\"countPerTerm\":6,\"maxActive\":6}}");

            IReadOnlyList<Mandate> issued =
                MandateGenerator.Generate(SaveA, Jun1994, city, Government(), Blocs(city), null, tuning);

            Assert.True(issued.Count > 1);
            Assert.Equal(issued.Count, issued.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(issued.Select(m => m.Id).OrderBy(id => id, StringComparer.Ordinal).ToList(),
                         issued.Select(m => m.Id).ToList());
        }

        // =========================================================================================
        // Generation from real deficits
        // =========================================================================================

        [Fact]
        public void BuildCandidates_CityTargetClosesTheTunedFractionOfTheGapToIdeal()
        {
            CitySnapshot city = SpreadCity();
            EngineTuning tuning = EngineTuning.Default;

            MandateCandidate crime = MandateGenerator
                .BuildCandidates(city, Blocs(city), tuning)
                .Single(c => c.DistrictId == null && c.Metric == MandateMetric.CrimeRate);

            // City crime 0.50, ideal 0, targetImprovementFraction 0.20 → promise 0.40, not 0.
            Assert.Equal(0.50, crime.BaselineValue, 10);
            Assert.Equal(0.40, crime.TargetValue, 10);
            Assert.Equal(0.50, crime.Deficit, 10);
            Assert.Equal(MandateDirection.Decrease, crime.Direction);
            Assert.Equal(Issue.HeritageOrder, crime.Issue);
        }

        [Fact]
        public void BuildCandidates_DistrictDeficitIsMeasuredAgainstTheBestDistrict()
        {
            CitySnapshot city = SpreadCity();
            EngineTuning tuning = EngineTuning.Default;

            IReadOnlyList<MandateCandidate> candidates = MandateGenerator.BuildCandidates(city, Blocs(city), tuning);

            MandateCandidate worst = candidates
                .Single(c => c.DistrictId == "district-b" && c.Metric == MandateMetric.GroundPollution);

            // Best district is A at 0.05; B sits at 0.60, so the gap is 0.55 and the promise closes 20%.
            Assert.Equal(0.55, worst.Deficit, 10);
            Assert.Equal(0.60, worst.BaselineValue, 10);
            Assert.Equal(0.60 + 0.20 * (0.05 - 0.60), worst.TargetValue, 10);

            // The best district itself has no deficit on that metric, so it is never a candidate.
            Assert.DoesNotContain(candidates,
                c => c.DistrictId == "district-a" && c.Metric == MandateMetric.GroundPollution);
        }

        [Fact]
        public void BuildCandidates_CoverageDeficitRunsUpward()
        {
            CitySnapshot city = SpreadCity();

            MandateCandidate health = MandateGenerator
                .BuildCandidates(city, Blocs(city), EngineTuning.Default)
                .Single(c => c.DistrictId == "district-b" && c.Metric == MandateMetric.HealthCoverage);

            Assert.Equal(MandateDirection.Increase, health.Direction);
            Assert.True(health.TargetValue > health.BaselineValue);
            Assert.Equal(Issue.Services, health.Issue);
        }

        [Fact]
        public void BuildCandidates_SkipsUnmeasurableDistrictFields()
        {
            // The sensor could not resolve pollution per district and copied the city value in.
            CitySnapshot city = Snapshot(
                Jun1994,
                District("district-a", groundPollution: 0.05, crimeRate: 0.10),
                District("district-b", groundPollution: 0.60, crimeRate: 0.55,
                         cityFallbackFields: new[] { "Pollution" }));

            IReadOnlyList<MandateCandidate> candidates =
                MandateGenerator.BuildCandidates(city, Blocs(city), EngineTuning.Default);

            Assert.DoesNotContain(candidates,
                c => c.DistrictId == "district-b" && c.Metric == MandateMetric.GroundPollution);

            // A metric that did resolve locally is still fair game for the same district.
            Assert.Contains(candidates,
                c => c.DistrictId == "district-b" && c.Metric == MandateMetric.CrimeRate);
        }

        [Fact]
        public void BuildCandidates_NeverProposesAnUnboundedMetric()
        {
            IReadOnlyList<MandateCandidate> candidates =
                MandateGenerator.BuildCandidates(SpreadCity(), Blocs(SpreadCity()), EngineTuning.Default);

            Assert.All(candidates, c => Assert.True(MandateMetrics.IsBounded(c.Metric)));
            Assert.DoesNotContain(candidates, c => c.Metric == MandateMetric.Debt);
            Assert.DoesNotContain(candidates, c => c.Metric == MandateMetric.AverageRent);
        }

        [Fact]
        public void Generate_RespectsMaxActive()
        {
            CitySnapshot city = SpreadCity();
            EngineTuning tuning = EngineTuning.FromJson("{\"mandates\":{\"countPerTerm\":3,\"maxActive\":4}}");

            var existing = new List<Mandate>
            {
                LiveMandate(MandateMetric.CrimeRate, null, 0.5, 0.4),
                LiveMandate(MandateMetric.HealthCoverage, "district-b", 0.4, 0.5),
                LiveMandate(MandateMetric.GroundPollution, "district-c", 0.45, 0.4)
            };
            for (int i = 0; i < existing.Count; i++)
            {
                existing[i].Id = "mandate-1990-01-0" + (i + 1).ToString(CultureInfo.InvariantCulture);
            }

            IReadOnlyList<Mandate> issued =
                MandateGenerator.Generate(SaveA, Jun1994, city, Government(), Blocs(city), existing, tuning);

            // Three already live against a ceiling of four leaves exactly one slot.
            Assert.Single(issued);
        }

        [Fact]
        public void Generate_NeverDuplicatesALivePromise()
        {
            CitySnapshot city = SpreadCity();
            EngineTuning tuning = EngineTuning.FromJson("{\"mandates\":{\"countPerTerm\":8,\"maxActive\":20}}");

            var existing = new List<Mandate>
            {
                LiveMandate(MandateMetric.GroundPollution, "district-b", 0.60, 0.50)
            };

            IReadOnlyList<Mandate> issued =
                MandateGenerator.Generate(SaveA, Jun1994, city, Government(), Blocs(city), existing, tuning);

            Assert.DoesNotContain(issued,
                m => m.DistrictId == "district-b" && m.Metric == MandateMetric.GroundPollution);

            // And no two of the new promises score the same number either.
            var pairs = issued.Select(m => (m.DistrictId ?? "city") + ":" + m.Metric).ToList();
            Assert.Equal(pairs.Count, pairs.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void Generate_IssuesPendingMandatesOwnedByTheLeadParty()
        {
            CitySnapshot city = SpreadCity();

            IReadOnlyList<Mandate> issued =
                MandateGenerator.Generate(SaveA, Jun1994, city, Government(), Blocs(city), null, EngineTuning.Default);

            Assert.NotEmpty(issued);
            Assert.All(issued, m =>
            {
                Assert.Equal(PartyId, m.PartyId);
                Assert.Equal(GovId, m.CoalitionId);
                Assert.Equal(MandateStatus.Pending, m.Status);
                Assert.Equal(Jun1994, m.IssuedDate);
                Assert.Equal(Jun1994.AddMonths(24), m.DeadlineDate);
                Assert.Equal("", m.Text);            // prose is the flavor provider's job, never the engine's
                Assert.Null(m.ResolvedDate);
                Assert.Equal(0.0, m.Progress);
            });
        }

        [Fact]
        public void Salience_RisesWithBlocInterestInTheIssue()
        {
            CitySnapshot city = SpreadCity();

            double uniform = MandateGenerator.Salience(Blocs(city), null, Issue.Environment, 0.1);

            // Weights are normalised to sum 6; a bloc that cares only about the environment.
            var green = new IssueWeights(0.2, 0.2, 5.0, 0.2, 0.2, 0.2);
            double focused = MandateGenerator.Salience(Blocs(city, green), null, Issue.Environment, 0.1);

            Assert.Equal(1.0 / 6.0, uniform, 10);
            Assert.True(focused > uniform);
            Assert.True(focused <= 1.0);

            // An issue nobody weights still carries the tuned floor, never zero.
            Assert.Equal(0.1, MandateGenerator.Salience(Blocs(city, green), null, Issue.Transit, 0.1), 10);
        }

        // =========================================================================================
        // Monitoring
        // =========================================================================================

        [Fact]
        public void ComputeProgress_RunsTheRightWayForBothDirections()
        {
            // Decrease: 0.50 → 0.40 promised, measured 0.45 is halfway.
            Assert.Equal(0.5, MandateMonitor.ComputeProgress(0.50, 0.40, 0.45), 10);

            // Increase: 0.40 → 0.50 promised, measured 0.45 is halfway.
            Assert.Equal(0.5, MandateMonitor.ComputeProgress(0.40, 0.50, 0.45), 10);

            // Target met and overshot both read 1, never more.
            Assert.Equal(1.0, MandateMonitor.ComputeProgress(0.50, 0.40, 0.40), 10);
            Assert.Equal(1.0, MandateMonitor.ComputeProgress(0.50, 0.40, 0.10), 10);

            // Backsliding past the baseline reads 0, not negative.
            Assert.Equal(0.0, MandateMonitor.ComputeProgress(0.50, 0.40, 0.80), 10);
        }

        [Fact]
        public void Tick_HoldsScoringDuringTheGracePeriod()
        {
            CitySnapshot city = SpreadCity();

            // Already at target, but only one month in against a three-month grace period.
            Mandate mandate = LiveMandate(MandateMetric.GroundPollution, "district-a", 0.60, 0.50,
                                          status: MandateStatus.Pending);

            MandateTickResult result = MandateMonitor.Tick(
                SaveA, Jun1994.AddMonths(1), city, new[] { mandate }, EngineTuning.Default);

            Assert.Empty(result.Resolutions);
            Assert.Equal(MandateStatus.Pending, result.Mandates[0].Status);
            Assert.Equal(1.0, result.Mandates[0].Progress, 10);   // measured, just not scored
        }

        [Fact]
        public void Tick_FulfilsEarlyOnceTheTargetIsMet()
        {
            CitySnapshot city = SpreadCity();
            Mandate mandate = LiveMandate(MandateMetric.GroundPollution, "district-a", 0.60, 0.50,
                                          status: MandateStatus.Pending);

            MandateTickResult result = MandateMonitor.Tick(
                SaveA, Jun1994.AddMonths(4), city, new[] { mandate }, EngineTuning.Default);

            Assert.Equal(MandateStatus.Fulfilled, result.Mandates[0].Status);
            Assert.Equal(Jun1994.AddMonths(4), result.Mandates[0].ResolvedDate!.Value);

            MandateResolution resolution = Assert.Single(result.Resolutions);
            Assert.True(resolution.HappinessDelta > 0);
            Assert.True(resolution.LegitimacyDelta > 0);
            Assert.Equal(0.0, resolution.OppositionSurge);
            Assert.False(resolution.UnrestTriggered);
        }

        [Fact]
        public void Tick_DefiesAtTheDeadlineAndSurgesTheOpposition()
        {
            CitySnapshot city = SpreadCity();

            // District B sits at 0.60 and was promised 0.40 — no movement at all.
            Mandate mandate = LiveMandate(MandateMetric.GroundPollution, "district-b", 0.60, 0.40);

            MandateTickResult result = MandateMonitor.Tick(
                SaveA, Jun1994.AddMonths(24), city, new[] { mandate }, EngineTuning.Default);

            Assert.Equal(MandateStatus.Defied, result.Mandates[0].Status);

            MandateResolution resolution = Assert.Single(result.Resolutions);
            Assert.True(resolution.HappinessDelta < 0);
            Assert.True(resolution.LegitimacyDelta < 0);
            Assert.True(resolution.OppositionSurge > 0);
        }

        [Fact]
        public void Tick_GivesPartialCreditAboveTheThreshold()
        {
            // Promise 0.60 → 0.40; the city delivered 0.46, which is 70% of the way.
            CitySnapshot city = Snapshot(Jun1994, District("district-b", groundPollution: 0.46));
            Mandate mandate = LiveMandate(MandateMetric.GroundPollution, "district-b", 0.60, 0.40);

            MandateTickResult result = MandateMonitor.Tick(
                SaveA, Jun1994.AddMonths(24), city, new[] { mandate }, EngineTuning.Default);

            Assert.Equal(MandateStatus.PartiallyFulfilled, result.Mandates[0].Status);
            Assert.Equal(0.7, result.Mandates[0].Progress, 10);

            MandateResolution resolution = Assert.Single(result.Resolutions);
            Assert.True(resolution.HappinessDelta > 0);
            Assert.Equal(0.0, resolution.LegitimacyDelta);
            Assert.Equal(0.0, resolution.OppositionSurge);
        }

        [Fact]
        public void Tick_JustBelowTheThresholdIsDefiance()
        {
            // 0.49 is 55% of the way — under partialCreditThreshold of 0.60.
            CitySnapshot city = Snapshot(Jun1994, District("district-b", groundPollution: 0.49));
            Mandate mandate = LiveMandate(MandateMetric.GroundPollution, "district-b", 0.60, 0.40);

            MandateTickResult result = MandateMonitor.Tick(
                SaveA, Jun1994.AddMonths(24), city, new[] { mandate }, EngineTuning.Default);

            Assert.Equal(MandateStatus.Defied, result.Mandates[0].Status);
        }

        [Fact]
        public void Tick_HoldsAMandateWhoseDistrictVanished()
        {
            CitySnapshot city = Snapshot(Jun1994, District("district-a"));
            Mandate mandate = LiveMandate(MandateMetric.GroundPollution, "district-gone", 0.60, 0.40);

            // The deadline arrives with no measurement: held, not failed.
            MandateTickResult atDeadline = MandateMonitor.Tick(
                SaveA, Jun1994.AddMonths(24), city, new[] { mandate }, EngineTuning.Default);

            Assert.Empty(atDeadline.Resolutions);
            Assert.Equal(MandateStatus.Active, atDeadline.Mandates[0].Status);
            Assert.True(atDeadline.Mandates[0].IsMeasurementStalled);

            // Only once the stall grace has also run out is it abandoned — and abandoned is never scored.
            MandateTickResult later = MandateMonitor.Tick(
                SaveA, Jun1994.AddMonths(27), city, new[] { mandate }, EngineTuning.Default);

            Assert.Equal(MandateStatus.Abandoned, later.Mandates[0].Status);
            MandateResolution resolution = Assert.Single(later.Resolutions);
            Assert.Equal(0.0, resolution.HappinessDelta);
            Assert.Equal(0.0, resolution.LegitimacyDelta);
            Assert.Equal(0.0, resolution.OppositionSurge);
            Assert.Empty(later.Effects);
        }

        [Fact]
        public void Tick_NeverScoresAgainstAFallenBackField()
        {
            CitySnapshot city = Snapshot(
                Jun1994,
                District("district-b", groundPollution: 0.60, cityFallbackFields: new[] { "Pollution" }));

            Mandate mandate = LiveMandate(MandateMetric.GroundPollution, "district-b", 0.60, 0.40);

            MandateTickResult result = MandateMonitor.Tick(
                SaveA, Jun1994.AddMonths(24), city, new[] { mandate }, EngineTuning.Default);

            Assert.True(result.Mandates[0].IsMeasurementStalled);
            Assert.Empty(result.Resolutions);
        }

        [Fact]
        public void Tick_LeavesResolvedMandatesAlone()
        {
            CitySnapshot city = SpreadCity();
            Mandate mandate = LiveMandate(MandateMetric.GroundPollution, "district-b", 0.60, 0.40,
                                          status: MandateStatus.Defied);
            mandate.ResolvedDate = Jun1994.AddMonths(24);

            MandateTickResult result = MandateMonitor.Tick(
                SaveA, Jun1994.AddMonths(30), city, new[] { mandate }, EngineTuning.Default);

            Assert.Empty(result.Resolutions);
            Assert.Equal(MandateStatus.Defied, result.Mandates[0].Status);
        }

        [Fact]
        public void AbandonAll_CancelsOnlyTheNamedGovernmentsLivePromises()
        {
            Mandate mine = LiveMandate(MandateMetric.CrimeRate, null, 0.5, 0.4);
            Mandate theirs = LiveMandate(MandateMetric.CrimeRate, "district-b", 0.5, 0.4);
            theirs.Id = "mandate-1994-06-02";
            theirs.CoalitionId = "gov-1990-01";

            IReadOnlyList<Mandate> after =
                MandateMonitor.AbandonAll(new[] { mine, theirs }, GovId, Jun1994.AddMonths(6));

            Assert.Equal(MandateStatus.Abandoned, after.Single(m => m.Id == mine.Id).Status);
            Assert.Equal(MandateStatus.Active, after.Single(m => m.Id == theirs.Id).Status);
            Assert.Equal(MandateStatus.Active, mine.Status);   // input untouched
        }

        // =========================================================================================
        // Resolution effects — scope, magnitude cap, duration cap, fallback
        // =========================================================================================

        [Fact]
        public void Resolution_EffectIsScopedToTheMandate()
        {
            EngineTuning tuning = EngineTuning.Default;

            MandateResolution district = MandateResolver.Resolve(
                SaveA, Jun1994, LiveMandate(MandateMetric.GroundPollution, "district-b", 0.6, 0.4),
                MandateStatus.Fulfilled, tuning);

            MandateResolution city = MandateResolver.Resolve(
                SaveA, Jun1994, LiveMandate(MandateMetric.CrimeRate, null, 0.5, 0.4),
                MandateStatus.Fulfilled, tuning);

            EffectRequest districtEffect = district.Effect!.Value;
            EffectRequest cityEffect = city.Effect!.Value;

            Assert.Equal(EffectScope.District, districtEffect.Scope);
            Assert.Equal("district-b", districtEffect.DistrictId);
            Assert.Equal(tuning.Effects.DefaultFallbackDistrictEffectId, districtEffect.EffectId);
            Assert.Equal(district.MandateId, districtEffect.SourceId);

            Assert.Equal(EffectScope.City, cityEffect.Scope);
            Assert.Null(cityEffect.DistrictId);
            Assert.Equal(tuning.Effects.DefaultFallbackCityEffectId, cityEffect.EffectId);

            // Both are terminal palette entries, so the sink can never loop looking for a fallback.
            Assert.True(tuning.Effects.TryGetEffect(districtEffect.EffectId, out EffectCap dCap));
            Assert.Equal("", dCap.FallbackEffectId);
            Assert.True(tuning.Effects.TryGetEffect(cityEffect.EffectId, out EffectCap cCap));
            Assert.Equal("", cCap.FallbackEffectId);
        }

        [Fact]
        public void Resolution_MagnitudeCapHoldsInBothDirections()
        {
            // An absurd tuning: the cap, not the coefficient, must decide what reaches the city.
            EngineTuning tuning = EngineTuning.FromJson(
                "{\"mandates\":{\"fulfilledHappinessBonus\":10000,\"defiedHappinessPenalty\":10000," +
                "\"salienceFloor\":1.0,\"unrestEventProbabilityOnDefiance\":0.0}}");

            EffectCap cap = tuning.Effects.CapFor(tuning.Effects.DefaultFallbackDistrictEffectId,
                                                  EffectScope.District);

            MandateResolution reward = MandateResolver.Resolve(
                SaveA, Jun1994, LiveMandate(MandateMetric.GroundPollution, "district-b", 0.6, 0.4),
                MandateStatus.Fulfilled, tuning);

            MandateResolution punishment = MandateResolver.Resolve(
                SaveA, Jun1994, LiveMandate(MandateMetric.GroundPollution, "district-b", 0.6, 0.4),
                MandateStatus.Defied, tuning);

            EffectRequest up = reward.Effect!.Value;
            EffectRequest down = punishment.Effect!.Value;

            Assert.Equal(cap.MagnitudeCap, up.Magnitude, 10);
            Assert.Equal(-cap.MagnitudeCap, down.Magnitude, 10);
            Assert.True(Math.Abs(up.Magnitude) <= tuning.Effects.GlobalMagnitudeCap);
            Assert.True(Math.Abs(down.Magnitude) <= tuning.Effects.GlobalMagnitudeCap);
        }

        [Fact]
        public void Resolution_DurationCapHolds()
        {
            EngineTuning tuning = EngineTuning.FromJson(
                "{\"mandates\":{\"resolutionEffectDurationMonths\":9999}}");

            EffectCap cap = tuning.Effects.CapFor(tuning.Effects.DefaultFallbackCityEffectId, EffectScope.City);

            MandateResolution resolution = MandateResolver.Resolve(
                SaveA, Jun1994, LiveMandate(MandateMetric.CrimeRate, null, 0.5, 0.4),
                MandateStatus.Fulfilled, tuning);

            EffectRequest effect = resolution.Effect!.Value;

            Assert.Equal(cap.DurationCapMonths, effect.DurationMonths);
            Assert.True(effect.DurationMonths <= tuning.Effects.GlobalDurationCapMonths);
        }

        [Fact]
        public void Resolution_EffectIsDroppedWhenEffectsAreDisabled()
        {
            EngineTuning tuning = EngineTuning.FromJson("{\"effects\":{\"enabled\":false}}");

            MandateResolution resolution = MandateResolver.Resolve(
                SaveA, Jun1994, LiveMandate(MandateMetric.CrimeRate, null, 0.5, 0.4),
                MandateStatus.Fulfilled, tuning);

            Assert.Null(resolution.Effect);
            Assert.Null(resolution.ResolutionEffectId);
            Assert.True(resolution.HappinessDelta > 0);   // politics still happen; the city is untouched
        }

        [Fact]
        public void Resolution_EffectIdIsAlwaysInTheClosedPalette()
        {
            EngineTuning tuning = EngineTuning.Default;

            foreach (MandateStatus outcome in new[]
                     { MandateStatus.Fulfilled, MandateStatus.PartiallyFulfilled, MandateStatus.Defied })
            {
                foreach (string? districtId in new[] { null, "district-b" })
                {
                    MandateResolution resolution = MandateResolver.Resolve(
                        SaveA, Jun1994, LiveMandate(MandateMetric.CrimeRate, districtId, 0.5, 0.4),
                        outcome, tuning);

                    Assert.NotNull(resolution.ResolutionEffectId);
                    Assert.Contains(resolution.ResolutionEffectId!, tuning.Effects.EffectIds);
                }
            }
        }

        [Fact]
        public void Resolution_StakeScalesWithSalience()
        {
            EngineTuning tuning = EngineTuning.Default;

            double loud = MandateResolver.Resolve(SaveA, Jun1994,
                LiveMandate(MandateMetric.CrimeRate, null, 0.5, 0.4, salience: 1.0),
                MandateStatus.Defied, tuning).HappinessDelta;

            double quiet = MandateResolver.Resolve(SaveA, Jun1994,
                LiveMandate(MandateMetric.CrimeRate, null, 0.5, 0.4, salience: 0.2),
                MandateStatus.Defied, tuning).HappinessDelta;

            Assert.True(loud < quiet);            // both negative; the salient one hurts more
            Assert.Equal(loud * 0.2, quiet, 10);
        }

        // =========================================================================================
        // Unrest — statistical only (§14.5)
        // =========================================================================================

        [Fact]
        public void Unrest_IsRolledOnlyOnDefianceAndOnlyFromItsOwnStream()
        {
            EngineTuning certain = EngineTuning.FromJson(
                "{\"mandates\":{\"unrestEventProbabilityOnDefiance\":1.0}}");
            EngineTuning never = EngineTuning.FromJson(
                "{\"mandates\":{\"unrestEventProbabilityOnDefiance\":0.0}}");

            Mandate mandate = LiveMandate(MandateMetric.CrimeRate, null, 0.5, 0.4);

            Assert.True(MandateResolver.Resolve(SaveA, Jun1994, mandate, MandateStatus.Defied, certain).UnrestTriggered);
            Assert.False(MandateResolver.Resolve(SaveA, Jun1994, mandate, MandateStatus.Defied, never).UnrestTriggered);

            // Fulfilment never rolls, whatever the probability says.
            Assert.False(MandateResolver.Resolve(SaveA, Jun1994, mandate, MandateStatus.Fulfilled, certain).UnrestTriggered);
        }

        [Fact]
        public void Unrest_FiresAtRoughlyTheTunedRate()
        {
            EngineTuning tuning = EngineTuning.Default;   // 0.25
            int fired = 0;
            const int trials = 400;

            for (int i = 0; i < trials; i++)
            {
                Mandate mandate = LiveMandate(MandateMetric.CrimeRate, null, 0.5, 0.4);
                mandate.Id = "mandate-1994-06-" + i.ToString("D3", CultureInfo.InvariantCulture);

                if (MandateResolver.Resolve(SaveA, Jun1994, mandate, MandateStatus.Defied, tuning).UnrestTriggered)
                    fired++;
            }

            double rate = (double)fired / trials;
            Assert.InRange(rate, 0.15, 0.35);
        }

        // =========================================================================================
        // Affinity term
        // =========================================================================================

        [Fact]
        public void Performance_FulfilmentReadsPositiveAndDefianceNegative()
        {
            EngineTuning tuning = EngineTuning.Default;

            Mandate kept = LiveMandate(MandateMetric.CrimeRate, null, 0.5, 0.4, status: MandateStatus.Fulfilled);
            kept.Progress = 1.0;

            Mandate broken = LiveMandate(MandateMetric.GroundPollution, "district-b", 0.6, 0.4,
                                         status: MandateStatus.Defied);
            broken.Id = "mandate-1994-06-02";
            broken.Progress = 0.0;

            Assert.Equal(1.0, MandateAffinity.ScoreForParty(PartyId, new[] { kept }, tuning).Score, 10);
            Assert.Equal(-1.0, MandateAffinity.ScoreForParty(PartyId, new[] { broken }, tuning).Score, 10);
            Assert.Equal(0.0, MandateAffinity.ScoreForParty(PartyId, new[] { kept, broken }, tuning).Score, 10);
        }

        [Fact]
        public void Performance_IgnoresLiveAndAbandonedPromises()
        {
            EngineTuning tuning = EngineTuning.Default;

            Mandate live = LiveMandate(MandateMetric.CrimeRate, null, 0.5, 0.4);
            Mandate abandoned = LiveMandate(MandateMetric.HealthCoverage, "district-b", 0.4, 0.5,
                                            status: MandateStatus.Abandoned);
            abandoned.Id = "mandate-1994-06-02";

            MandatePerformance p = MandateAffinity.ScoreForParty(PartyId, new[] { live, abandoned }, tuning);

            Assert.Equal(0.0, p.Score);
            Assert.Equal(1, p.Live);
            Assert.Equal(1, p.Abandoned);
            Assert.Equal(0.0, p.ScoredSalience);
        }

        [Fact]
        public void Performance_IsScopedToTheParty()
        {
            EngineTuning tuning = EngineTuning.Default;

            Mandate mine = LiveMandate(MandateMetric.CrimeRate, null, 0.5, 0.4, status: MandateStatus.Defied);
            mine.Progress = 0.0;

            Assert.Equal(0.0, MandateAffinity.ScoreForParty("party-order", new[] { mine }, tuning).Score);
            Assert.Equal(-1.0, MandateAffinity.ScoreForParty(PartyId, new[] { mine }, tuning).Score, 10);
        }

        [Fact]
        public void Performance_DistrictViewKeepsCityWidePromises()
        {
            EngineTuning tuning = EngineTuning.Default;

            Mandate cityWide = LiveMandate(MandateMetric.CrimeRate, null, 0.5, 0.4, status: MandateStatus.Fulfilled);
            cityWide.Progress = 1.0;

            Mandate elsewhere = LiveMandate(MandateMetric.GroundPollution, "district-c", 0.6, 0.4,
                                            status: MandateStatus.Defied);
            elsewhere.Id = "mandate-1994-06-02";
            elsewhere.Progress = 0.0;

            MandatePerformance inB =
                MandateAffinity.ScoreForParty(PartyId, "district-b", new[] { cityWide, elsewhere }, tuning);

            // District B feels the city-wide win but not another district's failure.
            Assert.Equal(1.0, inB.Score, 10);
            Assert.Equal(1, inB.Fulfilled);
            Assert.Equal(0, inB.Defied);
        }

        [Fact]
        public void LiveFor_ReturnsOnlyThisGovernmentsUnfinishedPromises()
        {
            Mandate live = LiveMandate(MandateMetric.CrimeRate, null, 0.5, 0.4);
            Mandate done = LiveMandate(MandateMetric.HealthCoverage, "district-b", 0.4, 0.5,
                                       status: MandateStatus.Fulfilled);
            done.Id = "mandate-1994-06-02";

            IReadOnlyList<Mandate> result = MandateAffinity.LiveFor(GovId, new[] { live, done });

            Assert.Single(result);
            Assert.Equal(live.Id, result[0].Id);
        }

        // =========================================================================================
        // Metric plumbing
        // =========================================================================================

        [Fact]
        public void Metrics_EveryMemberIsReadableCityWide()
        {
            CitySnapshot city = SpreadCity();

            foreach (MandateMetric metric in MandateMetrics.All)
            {
                Assert.True(MandateMetrics.TryReadCity(city, metric, out double _),
                    metric + " is in the enum but cannot be read from CitySnapshot.");
            }
        }

        [Fact]
        public void Metrics_BudgetIsNotADistrictFact()
        {
            DistrictSnapshot district = District("district-a");

            Assert.False(MandateMetrics.TryReadDistrict(district, MandateMetric.BudgetBalance, out double _));
            Assert.False(MandateMetrics.TryReadDistrict(district, MandateMetric.Debt, out double _));
            Assert.True(MandateMetrics.TryReadDistrict(district, MandateMetric.CrimeRate, out double _));
        }

        [Fact]
        public void Metrics_FallbackDetectionAcceptsBothSpellings()
        {
            Assert.True(MandateMetrics.IsFallenBack(
                District("d", cityFallbackFields: new[] { "Pollution" }), MandateMetric.AirPollution));

            Assert.True(MandateMetrics.IsFallenBack(
                District("d", cityFallbackFields: new[] { "Services.Health" }), MandateMetric.HealthCoverage));

            Assert.False(MandateMetrics.IsFallenBack(
                District("d", cityFallbackFields: new[] { "AverageRent" }), MandateMetric.HealthCoverage));
        }

        [Fact]
        public void Metrics_BadnessRunsTowardsZeroForGoodCities()
        {
            Assert.True(MandateMetrics.TryBadness(MandateMetric.Happiness, 100.0, out double perfect));
            Assert.Equal(0.0, perfect, 10);

            Assert.True(MandateMetrics.TryBadness(MandateMetric.Happiness, 0.0, out double awful));
            Assert.Equal(1.0, awful, 10);

            Assert.True(MandateMetrics.TryBadness(MandateMetric.HealthCoverage, 1.0, out double covered));
            Assert.Equal(0.0, covered, 10);

            Assert.True(MandateMetrics.TryBadness(MandateMetric.CrimeRate, 0.75, out double crime));
            Assert.Equal(0.75, crime, 10);

            // Unbounded metrics decline to guess a scale rather than inventing one.
            Assert.False(MandateMetrics.TryBadness(MandateMetric.Debt, 100000, out double _));
        }
    }
}
