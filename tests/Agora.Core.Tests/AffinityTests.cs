using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Engine.Affinity;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Packet 4 — bloc→party affinity.
    ///
    /// <para>
    /// Affinity is the hot path every electoral packet reads, so these tests pin three things: that
    /// the result is a pure function of its inputs (hash twice, compare), that each term moves in the
    /// direction the design says it does, and that the noise term is genuinely clamped — a tail draw
    /// that could swing an election would break non-negotiable #3 in a way no aggregate assertion
    /// would notice.
    /// </para>
    /// </summary>
    public class AffinityTests
    {
        private static readonly Guid SaveA = new Guid("11111111-2222-3333-4444-555555555555");
        private static readonly Guid SaveB = new Guid("99999999-8888-7777-6666-555555555555");
        private static readonly SimDate Jan1990 = new SimDate(1990, 1, 1);
        private static readonly SimDate Oct1990 = new SimDate(1990, 10, 1);

        // Platform on Environment only, so alignment arithmetic below is checkable by hand.
        private static readonly IssuePosition GreenPlatform = new IssuePosition(0, 0, 1, 0, 0, 0);
        private static readonly IssuePosition GreyPlatform = new IssuePosition(0, 0, -1, 0, 0, 0);

        // --- Fixtures --------------------------------------------------------------------------

        private static Party MakeParty(string id, IssuePosition platform,
                                       PartyStatus status = PartyStatus.Active,
                                       bool incumbent = false) =>
            new Party
            {
                Id = id,
                Name = "placeholder",
                ArchetypeId = "test",
                Platform = platform,
                LastManifesto = platform,
                Status = status,
                IsIncumbent = incumbent,
                FoundedDate = Jan1990
            };

        private static Bloc MakeBloc(string districtId, BlocKey key,
                                     IssuePosition? ideal = null,
                                     double discontent = 0.0,
                                     IssueWeights? weights = null,
                                     List<PartyVoteShare>? previousVote = null) =>
            new Bloc
            {
                DistrictId = districtId,
                Key = key,
                Population = 1000,
                PopulationShare = 0.1,
                EligibleVoters = 800,
                Weights = weights ?? IssueWeights.Uniform,
                Ideal = ideal ?? IssuePosition.Centre,
                Happiness = 60,
                Discontent = discontent,
                PreviousVote = previousVote ?? new List<PartyVoteShare>()
            };

        private static readonly BlocKey MiddleEducatedAdult =
            new BlocKey(WealthTier.Middle, EducationTier.Educated, AgeBand.Adult);

        /// <summary>
        /// Tuning built from JSON, because every setter on <see cref="EngineTuning"/> is internal —
        /// which is the point: a test cannot invent a coefficient the shipped file does not have.
        /// Sections left out fall back to the built-in defaults.
        /// </summary>
        private static EngineTuning Tune(string affinitySection) =>
            EngineTuning.FromJson("{\"affinity\":" + affinitySection + "}");

        private static AffinityRequest Request(IEnumerable<Bloc> blocs, IEnumerable<Party> parties,
                                               Guid? save = null, SimDate? date = null) =>
            new AffinityRequest
            {
                SaveGuid = save ?? SaveA,
                Date = date ?? Jan1990,
                Blocs = blocs.ToList(),
                Parties = parties.ToList()
            };

        private static BlocAffinity Cell(AffinityResult result, string districtId, BlocKey key, string partyId) =>
            result.Affinities.Single(a => a.DistrictId == districtId && a.Bloc.Equals(key) && a.PartyId == partyId);

        // --- Determinism -----------------------------------------------------------------------

        /// <summary>
        /// The canonical pattern from <c>/write-test</c>: hash the whole serialized result rather than
        /// assert field by field, so a term someone forgets to compare still fails the test.
        /// </summary>
        private static string HashOf(AffinityResult result)
        {
            var sb = new StringBuilder();

            foreach (BlocAffinity a in result.Affinities)
            {
                sb.Append(a.DistrictId).Append('|')
                  .Append(a.Bloc.Id).Append('|')
                  .Append(a.PartyId).Append('|')
                  .Append(R(a.Affinity)).Append('|')
                  .Append(R(a.IssueComponent)).Append('|')
                  .Append(R(a.IncumbencyComponent)).Append('|')
                  .Append(R(a.MandateComponent)).Append('|')
                  .Append(R(a.EventComponent)).Append('|')
                  .Append(R(a.LoyaltyComponent)).Append('|')
                  .Append(R(a.NoiseComponent)).Append('\n');
            }

            foreach (BlocVoteShares s in result.BlocShares)
            {
                sb.Append(s.DistrictId).Append('|').Append(s.Bloc.Id);
                foreach (PartyVoteShare v in s.Shares) sb.Append('|').Append(v.PartyId).Append('=').Append(R(v.Share));
                sb.Append('\n');
            }

            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
            }
        }

        private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static AffinityResult FullCityRun(Guid save, SimDate date)
        {
            var blocs = new List<Bloc>();
            foreach (string district in new[] { "district-a", "district-b" })
                foreach (BlocKey key in BlocAxes.AllKeys)
                    blocs.Add(MakeBloc(district, key, new IssuePosition(0.2, -0.1, 0.4, 0.0, 0.3, -0.2), 0.35));

            var parties = new[]
            {
                MakeParty("party-01", GreenPlatform),
                MakeParty("party-02", GreyPlatform),
                MakeParty("party-03", IssuePosition.Centre)
            };

            return AffinityEngine.Compute(Request(blocs, parties, save, date), EngineTuning.Default);
        }

        [Fact]
        public void Compute_IsIdenticalAcrossRuns()
        {
            Assert.Equal(HashOf(FullCityRun(SaveA, Jan1990)), HashOf(FullCityRun(SaveA, Jan1990)));
        }

        [Fact]
        public void Compute_DiffersBySave()
        {
            Assert.NotEqual(HashOf(FullCityRun(SaveA, Jan1990)), HashOf(FullCityRun(SaveB, Jan1990)));
        }

        [Fact]
        public void Compute_DiffersByDate()
        {
            Assert.NotEqual(HashOf(FullCityRun(SaveA, Jan1990)), HashOf(FullCityRun(SaveA, Oct1990)));
        }

        /// <summary>
        /// The determinism that actually breaks in practice: the caller hands the same blocs in a
        /// different order. Output order — and therefore the state hash — must not move.
        /// </summary>
        [Fact]
        public void Compute_IsIndependentOfInputOrder()
        {
            var blocs = new List<Bloc>
            {
                MakeBloc("district-b", new BlocKey(WealthTier.High, EducationTier.WellEducated, AgeBand.Elderly)),
                MakeBloc("district-a", MiddleEducatedAdult),
                MakeBloc("district-a", new BlocKey(WealthTier.Low, EducationTier.Uneducated, AgeBand.Adult))
            };

            var parties = new[] { MakeParty("party-02", GreyPlatform), MakeParty("party-01", GreenPlatform) };

            AffinityResult forward = AffinityEngine.Compute(Request(blocs, parties), EngineTuning.Default);

            blocs.Reverse();
            AffinityResult reversed = AffinityEngine.Compute(
                Request(blocs, parties.Reverse().ToList()), EngineTuning.Default);

            Assert.Equal(HashOf(forward), HashOf(reversed));

            // And that order is the documented one, not merely "stable".
            Assert.Equal(
                new[] { "district-a", "district-a", "district-a", "district-a", "district-b", "district-b" },
                forward.Affinities.Select(a => a.DistrictId).ToArray());
            Assert.Equal(
                new[] { "party-01", "party-02", "party-01", "party-02", "party-01", "party-02" },
                forward.Affinities.Select(a => a.PartyId).ToArray());
            Assert.True(forward.Affinities[0].Bloc.Ordinal < forward.Affinities[2].Bloc.Ordinal);
        }

        // --- Issue proximity -------------------------------------------------------------------

        [Fact]
        public void IssueProximity_Linear_FallsFromOneToZero()
        {
            AffinityTuning t = EngineTuning.Default.Affinity;

            Assert.Equal(1.0, AffinityEngine.IssueProximity(0.0, t), 12);
            Assert.Equal(0.75, AffinityEngine.IssueProximity(0.25, t), 12);
            Assert.Equal(0.0, AffinityEngine.IssueProximity(1.0, t), 12);
        }

        [Fact]
        public void IssueProximity_Gaussian_MatchesClosedForm()
        {
            EngineTuning t = Tune("{\"distanceKernel\":\"gaussian\",\"distanceKernelSigma\":0.6}");

            // exp(-(0.6^2) / (2 * 0.6^2)) = exp(-0.5)
            Assert.Equal(Math.Exp(-0.5), AffinityEngine.IssueProximity(0.6, t.Affinity), 12);
            Assert.Equal(1.0, AffinityEngine.IssueProximity(0.0, t.Affinity), 12);
        }

        [Fact]
        public void IssueProximity_Quadratic_PunishesDistanceLessNearTheCentre()
        {
            EngineTuning t = Tune("{\"distanceKernel\":\"quadratic\"}");

            Assert.Equal(1.0 - 0.25 * 0.25, AffinityEngine.IssueProximity(0.25, t.Affinity), 12);
            Assert.True(AffinityEngine.IssueProximity(0.25, t.Affinity)
                        > AffinityEngine.IssueProximity(0.25, EngineTuning.Default.Affinity));
        }

        /// <summary>A typo in the tuning file must degrade, not throw mid-election.</summary>
        [Fact]
        public void IssueProximity_UnknownKernel_DegradesToLinear()
        {
            EngineTuning t = Tune("{\"distanceKernel\":\"sigmoid\"}");

            Assert.Equal(AffinityEngine.IssueProximity(0.4, EngineTuning.Default.Affinity),
                         AffinityEngine.IssueProximity(0.4, t.Affinity), 12);
        }

        [Fact]
        public void Affinity_PrefersTheClosestPlatform()
        {
            // A bloc that wants maximum environmental protection, and cares only about that.
            Bloc bloc = MakeBloc("district-a", MiddleEducatedAdult,
                                 ideal: GreenPlatform,
                                 weights: new IssueWeights(0, 0, 6, 0, 0, 0));

            var parties = new[] { MakeParty("party-01", GreenPlatform), MakeParty("party-02", GreyPlatform) };
            AffinityResult r = AffinityEngine.Compute(Request(new[] { bloc }, parties), EngineTuning.Default);

            BlocAffinity green = Cell(r, "district-a", MiddleEducatedAdult, "party-01");
            BlocAffinity grey = Cell(r, "district-a", MiddleEducatedAdult, "party-02");

            // Distance 0 → proximity 1; distance 1 → proximity 0, both times issueWeight 1.0.
            Assert.Equal(1.0, green.IssueComponent, 12);
            Assert.Equal(0.0, grey.IssueComponent, 12);
            Assert.True(green.Affinity > grey.Affinity);

            // And the softmax turns that into a majority, not a tie.
            double greenShare = r.BlocShares[0].Shares.Single(s => s.PartyId == "party-01").Share;
            Assert.True(greenShare > 0.9, "green share was " + greenShare);
        }

        [Fact]
        public void Compute_ExcludesPartiesOffTheBallot()
        {
            var parties = new[]
            {
                MakeParty("party-01", GreenPlatform),
                MakeParty("party-02", GreyPlatform, PartyStatus.Dissolved),
                MakeParty("party-03", IssuePosition.Centre, PartyStatus.Merged),
                MakeParty("party-04", IssuePosition.Centre, PartyStatus.Endangered)
            };

            AffinityResult r = AffinityEngine.Compute(
                Request(new[] { MakeBloc("district-a", MiddleEducatedAdult) }, parties), EngineTuning.Default);

            Assert.Equal(new[] { "party-01", "party-04" }, r.ContestingPartyIds.ToArray());
            Assert.DoesNotContain(r.Affinities, a => a.PartyId == "party-02");
        }

        // --- Incumbency ------------------------------------------------------------------------

        [Fact]
        public void Incumbency_IsABonusForAContentedBlocAndAPenaltyForAnAngryOne()
        {
            var incumbent = MakeParty("party-01", IssuePosition.Centre, incumbent: true);
            var challenger = MakeParty("party-02", IssuePosition.Centre);

            AffinityResult content = AffinityEngine.Compute(
                Request(new[] { MakeBloc("district-a", MiddleEducatedAdult, discontent: 0.0) },
                        new[] { incumbent, challenger }), EngineTuning.Default);

            AffinityResult angry = AffinityEngine.Compute(
                Request(new[] { MakeBloc("district-a", MiddleEducatedAdult, discontent: 1.0) },
                        new[] { incumbent, challenger }), EngineTuning.Default);

            // Shipped tuning: bonus 0.05, discontent penalty 0.10.
            Assert.Equal(0.05, Cell(content, "district-a", MiddleEducatedAdult, "party-01").IncumbencyComponent, 12);
            Assert.Equal(-0.05, Cell(angry, "district-a", MiddleEducatedAdult, "party-01").IncumbencyComponent, 12);

            // The challenger carries no incumbency term at all, in either mood.
            Assert.Equal(0.0, Cell(angry, "district-a", MiddleEducatedAdult, "party-02").IncumbencyComponent, 12);
        }

        /// <summary>
        /// National mood leaks into a locally contented bloc, weighted 0.10 against local 0.20.
        /// </summary>
        [Fact]
        public void Incumbency_BlendsNationalMoodWithLocalGrievance()
        {
            AffinityRequest request = Request(
                new[] { MakeBloc("district-a", MiddleEducatedAdult, discontent: 0.0) },
                new[] { MakeParty("party-01", IssuePosition.Centre, incumbent: true) });
            request.Indices = new DerivedIndices { DiscontentIndex = 1.0 };

            AffinityResult r = AffinityEngine.Compute(request, EngineTuning.Default);

            // blended = (0.20*0 + 0.10*1) / 0.30 = 1/3 → 0.05 - 0.10/3
            Assert.Equal(0.05 - 0.1 / 3.0,
                         Cell(r, "district-a", MiddleEducatedAdult, "party-01").IncumbencyComponent, 12);
        }

        [Fact]
        public void Incumbency_AppliesToCoalitionPartnersNotJustTheLead()
        {
            var request = Request(
                new[] { MakeBloc("district-a", MiddleEducatedAdult, discontent: 0.0) },
                new[] { MakeParty("party-01", IssuePosition.Centre), MakeParty("party-02", IssuePosition.Centre) });

            request.Government = new Coalition
            {
                Id = "gov-1990-01",
                LeadPartyId = "party-01",
                MemberPartyIds = new List<string> { "party-01", "party-02" },
                Status = CoalitionStatus.Governing
            };

            AffinityResult r = AffinityEngine.Compute(request, EngineTuning.Default);

            Assert.Equal(0.05, Cell(r, "district-a", MiddleEducatedAdult, "party-02").IncumbencyComponent, 12);
        }

        // --- Mandates --------------------------------------------------------------------------

        private static Mandate MakeMandate(string id, string partyId, MandateStatus status,
                                           double progress = 0.0, string? districtId = null,
                                           bool stalled = false) =>
            new Mandate
            {
                Id = id,
                PartyId = partyId,
                CoalitionId = "gov-1990-01",
                DistrictId = districtId,
                Issue = Issue.Environment,
                Metric = MandateMetric.AirPollution,
                Direction = MandateDirection.Decrease,
                Status = status,
                Progress = progress,
                Salience = 0.0, // deliberately zero: salience is the resolution stake, not an affinity input
                IssuedDate = Jan1990,
                DeadlineDate = new SimDate(1993, 1, 1),
                IsMeasurementStalled = stalled
            };

        private static double MandateComponentWith(params Mandate[] mandates)
        {
            AffinityRequest request = Request(
                new[] { MakeBloc("district-a", MiddleEducatedAdult) },
                new[] { MakeParty("party-01", IssuePosition.Centre) });
            request.Mandates = mandates;

            AffinityResult r = AffinityEngine.Compute(request, EngineTuning.Default);
            return Cell(r, "district-a", MiddleEducatedAdult, "party-01").MandateComponent;
        }

        [Fact]
        public void Mandate_DeliveryRewardsAndDefiancePunishes()
        {
            // Shipped tuning: performance weight 0.15, failure penalty 0.20; uniform weights → care 1.
            Assert.Equal(0.15, MandateComponentWith(MakeMandate("m-1", "party-01", MandateStatus.Fulfilled)), 12);
            Assert.Equal(-0.20, MandateComponentWith(MakeMandate("m-1", "party-01", MandateStatus.Defied)), 12);
            Assert.Equal(0.075, MandateComponentWith(
                MakeMandate("m-1", "party-01", MandateStatus.Active, progress: 0.5)), 12);
        }

        /// <summary>
        /// Averaged, not summed: a government that promised ten things is not punished ten times over
        /// for the same delivery record.
        /// </summary>
        [Fact]
        public void Mandate_TermIsAveragedOverMandates()
        {
            double one = MandateComponentWith(MakeMandate("m-1", "party-01", MandateStatus.Defied));
            double two = MandateComponentWith(
                MakeMandate("m-1", "party-01", MandateStatus.Defied),
                MakeMandate("m-2", "party-01", MandateStatus.Fulfilled));

            Assert.Equal(-0.20, one, 12);
            Assert.Equal((-0.20 + 0.15) / 2.0, two, 12);
        }

        /// <summary>
        /// Contract: "a mandate whose metric is unmeasurable is HELD, never failed." A stalled defied
        /// mandate must score exactly nothing — the sensor gap is not the government's fault.
        /// </summary>
        [Fact]
        public void Mandate_StalledMeasurementIsHeldNotFailed()
        {
            Assert.Equal(0.0, MandateComponentWith(
                MakeMandate("m-1", "party-01", MandateStatus.Defied, stalled: true)), 12);

            Assert.Equal(0.0, MandateComponentWith(
                MakeMandate("m-1", "party-01", MandateStatus.Pending)), 12);
        }

        [Fact]
        public void Mandate_DistrictPromiseOnlyMovesThatDistrict()
        {
            AffinityRequest request = Request(
                new[] { MakeBloc("district-a", MiddleEducatedAdult), MakeBloc("district-b", MiddleEducatedAdult) },
                new[] { MakeParty("party-01", IssuePosition.Centre) });
            request.Mandates = new[] { MakeMandate("m-1", "party-01", MandateStatus.Defied, districtId: "district-a") };

            AffinityResult r = AffinityEngine.Compute(request, EngineTuning.Default);

            Assert.Equal(-0.20, Cell(r, "district-a", MiddleEducatedAdult, "party-01").MandateComponent, 12);
            Assert.Equal(0.0, Cell(r, "district-b", MiddleEducatedAdult, "party-01").MandateComponent, 12);
        }

        [Fact]
        public void Mandate_WeightsByHowMuchTheBlocCaresAboutTheIssue()
        {
            AffinityRequest request = Request(
                new[]
                {
                    MakeBloc("district-a", MiddleEducatedAdult, weights: new IssueWeights(0, 0, 6, 0, 0, 0)),
                    MakeBloc("district-b", MiddleEducatedAdult, weights: IssueWeights.Uniform)
                },
                new[] { MakeParty("party-01", IssuePosition.Centre) });
            request.Mandates = new[] { MakeMandate("m-1", "party-01", MandateStatus.Defied) };

            AffinityResult r = AffinityEngine.Compute(request, EngineTuning.Default);

            // Single-issue bloc: weight 6 against a mean of 1 → six times the ordinary punishment.
            Assert.Equal(-1.20, Cell(r, "district-a", MiddleEducatedAdult, "party-01").MandateComponent, 12);
            Assert.Equal(-0.20, Cell(r, "district-b", MiddleEducatedAdult, "party-01").MandateComponent, 12);
        }

        // --- Events ----------------------------------------------------------------------------

        private static TimelineEvent MakeEvent(string id, IssuePosition pressure, int severity, SimDate fired) =>
            new TimelineEvent
            {
                Id = id,
                Date = fired,
                FiredDate = fired,
                Region = EventRegion.Global,
                Origin = EventOrigin.Catalog,
                Title = "test",
                Severity = severity,
                DurationMonths = 24,
                IssuePressure = pressure
            };

        private static double EventComponentWith(SimDate date, params TimelineEvent[] events)
        {
            AffinityRequest request = Request(
                new[] { MakeBloc("district-a", MiddleEducatedAdult) },
                new[] { MakeParty("party-01", GreenPlatform) },
                date: date);
            request.ActiveEvents = events;

            AffinityResult r = AffinityEngine.Compute(request, EngineTuning.Default);
            return Cell(r, "district-a", MiddleEducatedAdult, "party-01").EventComponent;
        }

        [Fact]
        public void Event_PushesTowardTheAlignedPlatformAndAwayFromTheOpposed()
        {
            // Uniform weights: alignment = (1 * pressure.env * platform.env) / 6.
            // Component = eventModifierWeight (0.25) * alignment * decay(1) * severity 5/5.
            double aligned = EventComponentWith(Jan1990, MakeEvent("e-1", GreenPlatform, 5, Jan1990));
            double opposed = EventComponentWith(Jan1990, MakeEvent("e-1", GreyPlatform, 5, Jan1990));

            Assert.Equal(0.25 / 6.0, aligned, 12);
            Assert.Equal(-0.25 / 6.0, opposed, 12);
        }

        [Fact]
        public void Event_InfluenceHalvesOverTheTunedHalfLife()
        {
            // Shipped half-life is 9 months; Jan → Oct 1990 is exactly that.
            double fresh = EventComponentWith(Jan1990, MakeEvent("e-1", GreenPlatform, 5, Jan1990));
            double faded = EventComponentWith(Oct1990, MakeEvent("e-1", GreenPlatform, 5, Jan1990));

            Assert.Equal(fresh / 2.0, faded, 12);
        }

        [Fact]
        public void Event_SeverityScalesTheInfluence()
        {
            double severe = EventComponentWith(Jan1990, MakeEvent("e-1", GreenPlatform, 5, Jan1990));
            double mild = EventComponentWith(Jan1990, MakeEvent("e-1", GreenPlatform, 1, Jan1990));

            Assert.Equal(severe / 5.0, mild, 12);
        }

        /// <summary>
        /// The cap that matters: a decade of aligned crises must not drown the issue term. Total
        /// event influence is bounded by <c>affinity.eventModifierWeight</c> in both directions.
        /// </summary>
        [Fact]
        public void Event_TotalInfluenceIsCappedInBothDirections()
        {
            TimelineEvent[] aligned = Enumerable.Range(0, 40)
                .Select(i => MakeEvent("e-" + i.ToString("D2"), GreenPlatform, 5, Jan1990)).ToArray();
            TimelineEvent[] opposed = Enumerable.Range(0, 40)
                .Select(i => MakeEvent("e-" + i.ToString("D2"), GreyPlatform, 5, Jan1990)).ToArray();

            Assert.Equal(0.25, EventComponentWith(Jan1990, aligned), 12);
            Assert.Equal(-0.25, EventComponentWith(Jan1990, opposed), 12);
        }

        [Fact]
        public void Event_IgnoresUnfiredAndExpiredEntries()
        {
            TimelineEvent future = MakeEvent("e-1", GreenPlatform, 5, new SimDate(1995, 1, 1));

            TimelineEvent expired = MakeEvent("e-2", GreenPlatform, 5, new SimDate(1985, 1, 1));
            expired.ExpiresDate = new SimDate(1989, 1, 1);

            Assert.Equal(0.0, EventComponentWith(Jan1990, future), 12);
            Assert.Equal(0.0, EventComponentWith(Jan1990, expired), 12);
        }

        // --- Habitual loyalty ------------------------------------------------------------------

        [Fact]
        public void Loyalty_RewardsThePreviousVoteAndDecaysMonthly()
        {
            var previous = new List<PartyVoteShare> { new PartyVoteShare("party-01", 0.5) };

            AffinityRequest fresh = Request(
                new[] { MakeBloc("district-a", MiddleEducatedAdult, previousVote: previous) },
                new[] { MakeParty("party-01", IssuePosition.Centre) });
            fresh.LastElectionDate = Jan1990;

            AffinityRequest stale = Request(
                new[] { MakeBloc("district-a", MiddleEducatedAdult, previousVote: previous) },
                new[] { MakeParty("party-01", IssuePosition.Centre) },
                date: new SimDate(1990, 11, 1));
            stale.LastElectionDate = Jan1990;

            double atElection = Cell(AffinityEngine.Compute(fresh, EngineTuning.Default),
                                     "district-a", MiddleEducatedAdult, "party-01").LoyaltyComponent;
            double tenMonthsLater = Cell(AffinityEngine.Compute(stale, EngineTuning.Default),
                                         "district-a", MiddleEducatedAdult, "party-01").LoyaltyComponent;

            // habitualLoyalty 0.20 × share 0.5; decay 1 - 0.02 × 10 months = 0.8.
            Assert.Equal(0.10, atElection, 12);
            Assert.Equal(0.08, tenMonthsLater, 12);
        }

        [Fact]
        public void Loyalty_DecaysToZeroAndNeverGoesNegative()
        {
            var previous = new List<PartyVoteShare> { new PartyVoteShare("party-01", 0.9) };

            AffinityRequest request = Request(
                new[] { MakeBloc("district-a", MiddleEducatedAdult, previousVote: previous) },
                new[] { MakeParty("party-01", IssuePosition.Centre) },
                date: new SimDate(2000, 1, 1)); // 120 months at 0.02/month
            request.LastElectionDate = Jan1990;

            Assert.Equal(0.0, Cell(AffinityEngine.Compute(request, EngineTuning.Default),
                                   "district-a", MiddleEducatedAdult, "party-01").LoyaltyComponent, 12);
        }

        [Fact]
        public void Loyalty_IsZeroForABlocThatHasNeverVoted()
        {
            AffinityResult r = AffinityEngine.Compute(
                Request(new[] { MakeBloc("district-a", MiddleEducatedAdult) },
                        new[] { MakeParty("party-01", IssuePosition.Centre) }), EngineTuning.Default);

            Assert.Equal(0.0, Cell(r, "district-a", MiddleEducatedAdult, "party-01").LoyaltyComponent, 12);
        }

        // --- Noise -----------------------------------------------------------------------------

        private static AffinityResult NoisyCityRun(string affinitySection)
        {
            var blocs = BlocAxes.AllKeys.Select(k => MakeBloc("district-a", k)).ToList();
            var parties = new[] { MakeParty("party-01", GreenPlatform), MakeParty("party-02", GreyPlatform) };

            return AffinityEngine.Compute(Request(blocs, parties), Tune(affinitySection));
        }

        /// <summary>
        /// The cap test. Driven far past the bound (sigma 5.0 against a clamp of 0.1) and asserted in
        /// both directions — a clamp that only holds for positive draws is not a clamp.
        /// </summary>
        [Fact]
        public void Noise_IsClampedToTheTunedBoundInBothDirections()
        {
            AffinityResult r = NoisyCityRun("{\"noiseSigma\":5.0,\"noiseClamp\":0.1}");

            Assert.All(r.Affinities, a => Assert.True(Math.Abs(a.NoiseComponent) <= 0.1 + 1e-12,
                "noise escaped the clamp: " + a.NoiseComponent));

            Assert.Contains(r.Affinities, a => a.NoiseComponent >= 0.1 - 1e-12);
            Assert.Contains(r.Affinities, a => a.NoiseComponent <= -0.1 + 1e-12);
        }

        [Fact]
        public void Noise_IsZeroWhenTheClampIsZero()
        {
            AffinityResult r = NoisyCityRun("{\"noiseSigma\":5.0,\"noiseClamp\":0.0}");

            Assert.All(r.Affinities, a => Assert.Equal(0.0, a.NoiseComponent));
        }

        /// <summary>
        /// Each cell draws from its own <c>voter.affinity.noise</c> sub-stream, so two blocs with
        /// identical politics still differ — and neither depends on where it sat in the loop.
        /// </summary>
        [Fact]
        public void Noise_IsDrawnPerBlocAndParty()
        {
            AffinityResult r = NoisyCityRun("{\"noiseSigma\":0.03,\"noiseClamp\":0.1}");

            Assert.True(r.Affinities.Select(a => a.NoiseComponent).Distinct().Count() > 100);
        }

        /// <summary>
        /// Golden value, per <c>/write-test</c>. This pins the exact shape of the affinity draw: the
        /// stream name <c>voter.affinity.noise</c>, the entity-id format
        /// <c>district|bloc|party</c>, and the order the seed's components are mixed. Change any of
        /// the three and every existing save's politics silently rewrites — the failure a golden
        /// literal exists to catch.
        ///
        /// <para>
        /// It also pins the summation order of the composite: a contented bloc at the centre facing a
        /// centrist party scores <c>baseAffinity 0.5 + issue 1.0 + noise</c>, every other term zero.
        /// </para>
        ///
        /// <para>
        /// If this fails, establish whether the change was intended before touching the constants.
        /// </para>
        /// </summary>
        [Fact]
        public void Noise_GoldenValueForAKnownCell()
        {
            AffinityResult r = AffinityEngine.Compute(
                Request(new[] { MakeBloc("district-a", MiddleEducatedAdult) },
                        new[] { MakeParty("party-01", IssuePosition.Centre) }), EngineTuning.Default);

            BlocAffinity cell = Cell(r, "district-a", MiddleEducatedAdult, "party-01");

            // Save 11111111-2222-3333-4444-555555555555, 1990-01-01, stream
            // "voter.affinity.noise:district-a|middle.educated.adult|party-01", noiseSigma 0.03.
            Assert.Equal(0.04981833058299227, cell.NoiseComponent, 12);
            Assert.Equal(1.0, cell.IssueComponent, 12);
            Assert.Equal(1.5498183305829923, cell.Affinity, 12);
        }

        // --- Vote shares -----------------------------------------------------------------------

        [Fact]
        public void ToVoteShares_SumToOneAndAreSortedByPartyId()
        {
            AffinityResult r = NoisyCityRun("{}");

            foreach (BlocVoteShares shares in r.BlocShares)
            {
                Assert.Equal(1.0, shares.Shares.Sum(s => s.Share), 12);
                Assert.Equal(shares.Shares.Select(s => s.PartyId).OrderBy(id => id, StringComparer.Ordinal),
                             shares.Shares.Select(s => s.PartyId));
            }
        }

        [Fact]
        public void ToVoteShares_RankOrderFollowsAffinity()
        {
            Bloc bloc = MakeBloc("district-a", MiddleEducatedAdult,
                                 ideal: GreenPlatform, weights: new IssueWeights(0, 0, 6, 0, 0, 0));

            var parties = new[]
            {
                MakeParty("party-01", GreenPlatform),
                MakeParty("party-02", IssuePosition.Centre),
                MakeParty("party-03", GreyPlatform)
            };

            AffinityResult r = AffinityEngine.Compute(Request(new[] { bloc }, parties), EngineTuning.Default);
            List<PartyVoteShare> shares = r.BlocShares[0].Shares.ToList();

            double Share(string id) => shares.Single(s => s.PartyId == id).Share;

            Assert.True(Share("party-01") > Share("party-02"));
            Assert.True(Share("party-02") > Share("party-03"));
        }

        /// <summary>
        /// A hopeless party is reported as zero, not as a rounding artefact, and the remainder still
        /// sums to one.
        /// </summary>
        [Fact]
        public void ToVoteShares_PrunesSharesUnderTheFloor()
        {
            Bloc bloc = MakeBloc("district-a", MiddleEducatedAdult,
                                 ideal: GreenPlatform, weights: new IssueWeights(0, 0, 6, 0, 0, 0));

            var parties = new[] { MakeParty("party-01", GreenPlatform), MakeParty("party-02", GreyPlatform) };

            EngineTuning tuning = Tune("{\"softmaxTemperature\":0.05,\"minPartyShare\":0.05,\"noiseClamp\":0.0}");
            AffinityResult r = AffinityEngine.Compute(Request(new[] { bloc }, parties), tuning);

            Assert.Equal(0.0, r.BlocShares[0].Shares.Single(s => s.PartyId == "party-02").Share);
            Assert.Equal(1.0, r.BlocShares[0].Shares.Sum(s => s.Share), 12);
        }

        /// <summary>An impossible floor must not empty the ballot.</summary>
        [Fact]
        public void ToVoteShares_FloorAboveEvenSplitLeavesTheBallotIntact()
        {
            EngineTuning tuning = Tune("{\"minPartyShare\":0.9,\"noiseClamp\":0.0}");

            var parties = new[]
            {
                MakeParty("party-01", IssuePosition.Centre),
                MakeParty("party-02", IssuePosition.Centre),
                MakeParty("party-03", IssuePosition.Centre)
            };

            AffinityResult r = AffinityEngine.Compute(
                Request(new[] { MakeBloc("district-a", MiddleEducatedAdult) }, parties), tuning);

            Assert.Equal(1.0, r.BlocShares[0].Shares.Sum(s => s.Share), 12);
            Assert.All(r.BlocShares[0].Shares, s => Assert.True(s.Share > 0.0));
        }

        // --- Tactical voting (FPTP) ------------------------------------------------------------

        [Fact]
        public void ApplyTacticalVoting_MigratesNonViableSupportToTheNearerFrontRunner()
        {
            var shares = new List<PartyVoteShare>
            {
                new PartyVoteShare("party-01", 0.42), // leader, grey
                new PartyVoteShare("party-02", 0.38), // second, centre
                new PartyVoteShare("party-03", 0.20)  // hopeless, green → nearer the centre than the grey
            };

            var parties = new[]
            {
                MakeParty("party-01", GreyPlatform),
                MakeParty("party-02", IssuePosition.Centre),
                MakeParty("party-03", GreenPlatform)
            };

            List<PartyVoteShare> after = AffinityEngine.ApplyTacticalVoting(
                shares, parties, EngineTuning.Default, migrationShare: 0.5);

            double Share(string id) => after.Single(s => s.PartyId == id).Share;

            // 0.20 is more than the 0.05 threshold behind second place, so half of it defects to the
            // ideologically nearer of the top two — the centrist, not the leader.
            Assert.Equal(0.10, Share("party-03"), 12);
            Assert.Equal(0.48, Share("party-02"), 12);
            Assert.Equal(0.42, Share("party-01"), 12);
            Assert.Equal(1.0, after.Sum(s => s.Share), 12);
        }

        [Fact]
        public void ApplyTacticalVoting_LeavesAViableThirdPartyAlone()
        {
            var shares = new List<PartyVoteShare>
            {
                new PartyVoteShare("party-01", 0.36),
                new PartyVoteShare("party-02", 0.33),
                new PartyVoteShare("party-03", 0.31) // within 0.05 of second — still in the race
            };

            var parties = new[]
            {
                MakeParty("party-01", GreyPlatform),
                MakeParty("party-02", IssuePosition.Centre),
                MakeParty("party-03", GreenPlatform)
            };

            List<PartyVoteShare> after = AffinityEngine.ApplyTacticalVoting(
                shares, parties, EngineTuning.Default, migrationShare: 1.0);

            Assert.Equal(0.31, after.Single(s => s.PartyId == "party-03").Share, 12);
        }

        [Fact]
        public void ApplyTacticalVoting_WithNoMigrationIsIdentity()
        {
            var shares = new List<PartyVoteShare>
            {
                new PartyVoteShare("party-03", 0.10),
                new PartyVoteShare("party-01", 0.50),
                new PartyVoteShare("party-02", 0.40)
            };

            List<PartyVoteShare> after = AffinityEngine.ApplyTacticalVoting(
                shares, new List<Party>(), EngineTuning.Default, migrationShare: 0.0);

            // Unchanged in value, and sorted by party id as the contract requires.
            Assert.Equal(new[] { "party-01", "party-02", "party-03" }, after.Select(s => s.PartyId).ToArray());
            Assert.Equal(new[] { 0.50, 0.40, 0.10 }, after.Select(s => s.Share).ToArray());
        }
    }
}
