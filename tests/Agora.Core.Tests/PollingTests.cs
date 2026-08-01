using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Engine.Polling;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Packet 6 — polling.
    ///
    /// <para>
    /// The point of this packet is that polls are wrong in a *direction*, so most of these tests
    /// assert direction rather than magnitude: shift <c>polling.educationUnderSampleBias</c> and the
    /// numbers move, but low-education districts stay under-sampled. The two exceptions are the
    /// golden test, which pins the exact weighting formula, and the determinism tests, which pin the
    /// seed derivation.
    /// </para>
    /// </summary>
    public sealed class PollingTests
    {
        // ------------------------------------------------------------------------------------------
        // Fixtures
        // ------------------------------------------------------------------------------------------

        private static readonly Guid SaveA = new Guid("11111111-2222-3333-4444-555555555555");
        private static readonly Guid SaveB = new Guid("99999999-8888-7777-6666-555555555555");

        private static readonly SimDate PollDay = new SimDate(1994, 1, 10);
        private static readonly SimDate ElectionDay = new SimDate(1994, 6, 5); // 146 days -> 20 weeks

        /// <summary>Shipped tuning. Used wherever the test is about direction, not exact values.</summary>
        private static EngineTuning Shipped => EngineTuning.Default;

        /// <summary>
        /// Shipped tuning with both random components switched off, leaving only the structural
        /// sampling bias. Built through <see cref="EngineTuning.FromJson"/> rather than by mutating a
        /// tuning object, because every section property is deliberately get-only.
        /// </summary>
        private static EngineTuning NoNoise() =>
            EngineTuning.FromJson("{\"polling\":{\"errorSigma\":0.0,\"houseEffectSigma\":0.0}}");

        /// <summary>House effect only: isolates the per-pollster constant offset.</summary>
        private static EngineTuning HouseEffectOnly() =>
            EngineTuning.FromJson("{\"polling\":{\"errorSigma\":0.0,\"houseEffectSigma\":0.15}}");

        /// <summary>
        /// Sampling error only. The house effect is seeded on the *election* date, so switching it off
        /// is what lets two polls taken on the same day but pointed at different elections draw the
        /// identical random error — which turns a statistical comparison into an exact one.
        /// </summary>
        private static EngineTuning SamplingErrorOnly() =>
            EngineTuning.FromJson("{\"polling\":{\"houseEffectSigma\":0.0}}");

        /// <summary>
        /// A spread of saves. Direction tests aggregate across these rather than trusting one draw:
        /// a single seeded Gaussian can legitimately land near zero, and a test that fails only for
        /// one hardcoded Guid is a trap for whoever touches the packet next.
        /// </summary>
        private static readonly Guid[] Saves =
        {
            new Guid("00000000-0000-4000-8000-000000000001"),
            new Guid("00000000-0000-4000-8000-000000000002"),
            new Guid("00000000-0000-4000-8000-000000000003"),
            new Guid("00000000-0000-4000-8000-000000000004"),
            new Guid("00000000-0000-4000-8000-000000000005"),
            new Guid("00000000-0000-4000-8000-000000000006"),
            new Guid("00000000-0000-4000-8000-000000000007"),
            new Guid("00000000-0000-4000-8000-000000000008")
        };

        private static DistrictPollInput District(string id, double educationIndex, double turnout,
                                                  int eligible, double shareA, double shareB)
        {
            return new DistrictPollInput
            {
                DistrictId = id,
                EducationIndex = educationIndex,
                ProjectedTurnout = turnout,
                EligibleVoters = eligible,
                TrueShares = new List<PartyVoteShare>
                {
                    new PartyVoteShare("party-a", shareA),
                    new PartyVoteShare("party-b", shareB)
                }
            };
        }

        /// <summary>
        /// Two districts of equal size and turnout that differ only in education, and that vote in
        /// exactly opposite directions. Any gap between the published and the true share is therefore
        /// attributable to education-weighted sampling and nothing else.
        /// </summary>
        private static List<DistrictPollInput> EducationSplitCity() => new List<DistrictPollInput>
        {
            District("d-low-education",  0.2, 0.5, 1000, 1.0, 0.0),
            District("d-high-education", 0.8, 0.5, 1000, 0.0, 1.0)
        };

        /// <summary>Equal education, unequal turnout. Isolates the turnout half of the bias.</summary>
        private static List<DistrictPollInput> TurnoutSplitCity() => new List<DistrictPollInput>
        {
            District("d-low-turnout",  0.5, 0.3, 1000, 1.0, 0.0),
            District("d-high-turnout", 0.5, 0.7, 1000, 0.0, 1.0)
        };

        /// <summary>Demographically identical districts: the structural bias must vanish entirely.</summary>
        private static List<DistrictPollInput> HomogeneousCity() => new List<DistrictPollInput>
        {
            District("d-north", 0.5, 0.6, 1000, 0.60, 0.40),
            District("d-south", 0.5, 0.6, 1000, 0.40, 0.60)
        };

        private static PollRequest Request(Guid save, SimDate date, SimDate? election,
                                           List<DistrictPollInput> districts, string pollster = "pollster-01")
        {
            return new PollRequest
            {
                SaveGuid = save,
                Date = date,
                ElectionDate = election,
                PollsterId = pollster,
                Districts = districts
            };
        }

        // ------------------------------------------------------------------------------------------
        // Serialization + hashing, for the determinism pattern
        // ------------------------------------------------------------------------------------------

        private static string Serialize(PollResult p)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();

            sb.Append(p.SchemaVersion).Append('|').Append(p.Id).Append('|').Append(p.Date).Append('|')
              .Append(p.PollsterName).Append('|').Append(p.PollsterId).Append('|')
              .Append(p.UndecidedShare.ToString("R", c)).Append('|')
              .Append(p.ProjectedTurnout.ToString("R", c)).Append('|')
              .Append(p.SampleSize.ToString(c)).Append('|')
              .Append(p.MarginOfError.ToString("R", c)).Append('|')
              .Append(p.WeeksToElection.ToString(c)).Append('|')
              .Append(p.ElectionDate.HasValue ? p.ElectionDate.Value.ToString() : "-").Append('|')
              .Append(p.IsPublished).Append('\n');

            foreach (PartyVoteShare s in p.Shares)
                sb.Append("S:").Append(s.PartyId).Append('=').Append(s.Share.ToString("R", c)).Append('\n');
            foreach (PartyVoteShare s in p.TrueShares)
                sb.Append("T:").Append(s.PartyId).Append('=').Append(s.Share.ToString("R", c)).Append('\n');

            foreach (DistrictPollResult d in p.Districts)
            {
                sb.Append("D:").Append(d.DistrictId).Append('|')
                  .Append(d.ProjectedTurnout.ToString("R", c)).Append('|')
                  .Append(d.SamplingBias.ToString("R", c)).Append('\n');
                foreach (PartyVoteShare s in d.Shares)
                    sb.Append("  ").Append(s.PartyId).Append('=').Append(s.Share.ToString("R", c)).Append('\n');
            }

            return sb.ToString();
        }

        private static string Hash(PollResult p)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Serialize(p))));
        }

        private static double ShareOf(IEnumerable<PartyVoteShare> shares, string partyId)
        {
            foreach (PartyVoteShare s in shares)
                if (string.CompareOrdinal(s.PartyId, partyId) == 0) return s.Share;
            throw new InvalidOperationException("No share for " + partyId);
        }

        private static DistrictPollResult DistrictOf(PollResult poll, string districtId)
        {
            foreach (DistrictPollResult d in poll.Districts)
                if (string.CompareOrdinal(d.DistrictId, districtId) == 0) return d;
            throw new InvalidOperationException("No district " + districtId);
        }

        private static double Sum(IEnumerable<PartyVoteShare> shares)
        {
            double total = 0.0;
            foreach (PartyVoteShare s in shares) total += s.Share;
            return total;
        }

        // ==========================================================================================
        // Determinism
        // ==========================================================================================

        [Fact]
        public void Run_ProducesIdenticalOutputTwice()
        {
            string first = Hash(PollingEngine.Run(Request(SaveA, PollDay, ElectionDay, EducationSplitCity()), Shipped));
            string second = Hash(PollingEngine.Run(Request(SaveA, PollDay, ElectionDay, EducationSplitCity()), Shipped));

            Assert.Equal(first, second);
        }

        [Fact]
        public void Run_DiffersBySave_AndByDate_AndByPollster()
        {
            string baseline = Hash(PollingEngine.Run(Request(SaveA, PollDay, ElectionDay, EducationSplitCity()), Shipped));

            string otherSave = Hash(PollingEngine.Run(Request(SaveB, PollDay, ElectionDay, EducationSplitCity()), Shipped));
            string otherDate = Hash(PollingEngine.Run(
                Request(SaveA, new SimDate(1994, 1, 17), ElectionDay, EducationSplitCity()), Shipped));
            string otherPollster = Hash(PollingEngine.Run(
                Request(SaveA, PollDay, ElectionDay, EducationSplitCity(), "pollster-02"), Shipped));

            // Without these the determinism test above would pass for a function returning a constant.
            Assert.NotEqual(baseline, otherSave);
            Assert.NotEqual(baseline, otherDate);
            Assert.NotEqual(baseline, otherPollster);

            // The serialized form contains the pollster id, so the assertion above would still pass if
            // the id never reached a seed. Prove the published numbers move as well. Counted across a
            // spread of saves because two independent draws can legitimately round to the same figure.
            int movedByPollster = 0;
            foreach (Guid save in Saves)
            {
                PollResult a = PollingEngine.Run(
                    Request(save, PollDay, ElectionDay, HomogeneousCity(), "pollster-01"), Shipped);
                PollResult b = PollingEngine.Run(
                    Request(save, PollDay, ElectionDay, HomogeneousCity(), "pollster-02"), Shipped);

                if (ShareOf(a.Shares, "party-a") != ShareOf(b.Shares, "party-a")) movedByPollster++;
            }

            Assert.True(movedByPollster > 0,
                "the pollster id must feed the house-effect and sampling-error seeds");
        }

        [Fact]
        public void Run_IsIndependentOfDistrictInputOrder()
        {
            var forward = EducationSplitCity();
            var reversed = EducationSplitCity();
            reversed.Reverse();

            string a = Hash(PollingEngine.Run(Request(SaveA, PollDay, ElectionDay, forward), Shipped));
            string b = Hash(PollingEngine.Run(Request(SaveA, PollDay, ElectionDay, reversed), Shipped));

            // Floating-point addition is not associative, so this only holds because the engine sorts
            // districts before aggregating. It is the cheapest guard against that sort being dropped.
            Assert.Equal(a, b);
        }

        [Fact]
        public void HouseEffect_IsAnchoredToTheElection_NotThePollDate()
        {
            // Two election dates one day apart. Both are 145-146 days out, so WeeksToElection is 20
            // either way and every other input to the poll is identical — only the campaign anchor,
            // and therefore the house effect, differs.
            SimDate electionEarly = new SimDate(1994, 6, 4);
            SimDate electionLate = new SimDate(1994, 6, 5);

            EngineTuning houseOnly = HouseEffectOnly();
            EngineTuning noNoise = NoNoise();

            double totalDifference = 0.0;
            foreach (Guid save in Saves)
            {
                PollResult a = PollingEngine.Run(
                    Request(save, PollDay, electionEarly, HomogeneousCity()), houseOnly);
                PollResult b = PollingEngine.Run(
                    Request(save, PollDay, electionLate, HomogeneousCity()), houseOnly);

                Assert.Equal(20, a.WeeksToElection);
                Assert.Equal(20, b.WeeksToElection);

                totalDifference += Math.Abs(ShareOf(a.Shares, "party-a") - ShareOf(b.Shares, "party-a"));

                // With the house effect switched off the anchor has nothing left to influence. This is
                // the control: it proves the difference above comes from the house effect and not from
                // some other path that quietly reads the election date.
                PollResult c = PollingEngine.Run(
                    Request(save, PollDay, electionEarly, HomogeneousCity()), noNoise);
                PollResult d = PollingEngine.Run(
                    Request(save, PollDay, electionLate, HomogeneousCity()), noNoise);
                Assert.Equal(ShareOf(c.Shares, "party-a"), ShareOf(d.Shares, "party-a"), 12);
            }

            Assert.True(totalDifference > 0.0,
                "changing the campaign anchor must re-draw the house effect");
        }

        // ==========================================================================================
        // Golden value — pins the weighting formula
        // ==========================================================================================

        [Fact]
        public void Golden_EducationSplitCity_PublishesExactlyThePredictedShares()
        {
            // Hand-computable. Equal electorates and equal turnout give true weights of 0.5 each, so
            // the electorate-weighted mean education is 0.5 and the biases are
            //     +/- educationUnderSampleBias * 0.3 = +/- 0.012.
            // Sampling weights are proportional to w * exp(bias), so the high-education district's
            // weight is exp(0.012) / (exp(0.012) + exp(-0.012)) = 0.5 * (1 + tanh(0.012)) = 0.5059997,
            // which rounds to 0.506 at polling.roundingDecimals = 3.
            //
            // If this test fails, do NOT update the constant. Establish first whether the change to
            // the weighting was intended: it rewrites every published poll in every existing save.
            PollResult poll = PollingEngine.Run(
                Request(SaveA, PollDay, ElectionDay, EducationSplitCity()), NoNoise());

            Assert.Equal(0.494, ShareOf(poll.Shares, "party-a"), 12);
            Assert.Equal(0.506, ShareOf(poll.Shares, "party-b"), 12);

            // The truth is a dead heat. The poll is not.
            Assert.Equal(0.5, ShareOf(poll.TrueShares, "party-a"), 12);
            Assert.Equal(0.5, ShareOf(poll.TrueShares, "party-b"), 12);
        }

        // ==========================================================================================
        // Direction of the bias — the reason this packet exists
        // ==========================================================================================

        [Fact]
        public void SamplingBias_IsNegativeInLowEducationDistricts()
        {
            PollResult poll = PollingEngine.Run(
                Request(SaveA, PollDay, ElectionDay, EducationSplitCity()), Shipped);

            double low = DistrictOf(poll, "d-low-education").SamplingBias;
            double high = DistrictOf(poll, "d-high-education").SamplingBias;

            Assert.True(low < 0.0, "low-education district must be under-sampled, was " + low);
            Assert.True(high > 0.0, "high-education district must be over-sampled, was " + high);
            Assert.True(low < high);
        }

        [Fact]
        public void SamplingBias_IsNegativeInLowTurnoutDistricts()
        {
            PollResult poll = PollingEngine.Run(
                Request(SaveA, PollDay, ElectionDay, TurnoutSplitCity()), Shipped);

            Assert.True(DistrictOf(poll, "d-low-turnout").SamplingBias < 0.0);
            Assert.True(DistrictOf(poll, "d-high-turnout").SamplingBias > 0.0);
        }

        [Fact]
        public void SamplingBias_IsZeroWhenEveryDistrictIsDemographicallyIdentical()
        {
            PollResult poll = PollingEngine.Run(
                Request(SaveA, PollDay, ElectionDay, HomogeneousCity()), Shipped);

            foreach (DistrictPollResult d in poll.Districts)
                Assert.Equal(0.0, d.SamplingBias, 12);
        }

        [Fact]
        public void SamplingBias_IsZeroSumAcrossTheElectorate()
        {
            PollResult poll = PollingEngine.Run(
                Request(SaveA, PollDay, ElectionDay, TurnoutSplitCity()), Shipped);

            // Electorate-weighted, the biases cancel: a pollster mis-allocates weight between
            // districts, it does not invent or destroy voters. Without this the whole city's published
            // numbers would drift with the number of districts the player happens to have drawn.
            var inputs = TurnoutSplitCity();
            double totalMass = 0.0;
            foreach (DistrictPollInput d in inputs) totalMass += d.EligibleVoters * d.ProjectedTurnout;

            double weighted = 0.0;
            foreach (DistrictPollInput d in inputs)
            {
                double w = d.EligibleVoters * d.ProjectedTurnout / totalMass;
                weighted += w * DistrictOf(poll, d.DistrictId).SamplingBias;
            }

            Assert.Equal(0.0, weighted, 12);
        }

        [Fact]
        public void PublishedShares_FavourTheHighEducationDistrictsParty()
        {
            PollResult poll = PollingEngine.Run(
                Request(SaveA, PollDay, ElectionDay, EducationSplitCity()), NoNoise());

            double publishedB = ShareOf(poll.Shares, "party-b");
            double trueB = ShareOf(poll.TrueShares, "party-b");

            // party-b is the party of the district the pollster over-reaches, so the poll overstates it.
            Assert.True(publishedB > trueB,
                "published " + publishedB + " should exceed true " + trueB);
            Assert.True(ShareOf(poll.Shares, "party-a") < ShareOf(poll.TrueShares, "party-a"));
        }

        [Fact]
        public void SamplingBias_IsMonotoneInEducation_AcrossAWholeCity()
        {
            // The two-district fixtures prove the sign; this proves the ordering holds across a city
            // of unequal districts, which is the shape the M4a gate actually inspects. Turnout is
            // uniform, so education alone orders the bias.
            var districts = new List<DistrictPollInput>
            {
                District("d-1", 0.10, 0.55, 1200, 0.7, 0.3),
                District("d-2", 0.30, 0.55,  900, 0.6, 0.4),
                District("d-3", 0.50, 0.55, 1500, 0.5, 0.5),
                District("d-4", 0.70, 0.55,  800, 0.4, 0.6),
                District("d-5", 0.90, 0.55, 1100, 0.3, 0.7)
            };

            PollResult poll = PollingEngine.Run(Request(SaveA, PollDay, ElectionDay, districts), Shipped);

            double previous = double.NegativeInfinity;
            foreach (DistrictPollInput d in districts)
            {
                double bias = DistrictOf(poll, d.DistrictId).SamplingBias;
                Assert.True(bias > previous,
                    d.DistrictId + " broke the education ordering of the sampling bias");
                previous = bias;
            }

            // Electorate-weighted mean education is 0.4891, so the bottom two districts sit below it.
            Assert.True(DistrictOf(poll, "d-1").SamplingBias < 0.0);
            Assert.True(DistrictOf(poll, "d-2").SamplingBias < 0.0);
            Assert.True(DistrictOf(poll, "d-5").SamplingBias > 0.0);

            // And the consequence: party-b is strongest exactly where the pollster over-reaches, so
            // the published city figure overstates it. Rounded to six decimals rather than the shipped
            // three, because the effect here is a fifth of a point and three decimals would blunt it.
            EngineTuning fine = EngineTuning.FromJson(
                "{\"polling\":{\"errorSigma\":0.0,\"houseEffectSigma\":0.0,\"roundingDecimals\":6}}");
            PollResult clean = PollingEngine.Run(Request(SaveA, PollDay, ElectionDay, districts), fine);

            Assert.True(ShareOf(clean.Shares, "party-b") > ShareOf(clean.TrueShares, "party-b"),
                "published " + ShareOf(clean.Shares, "party-b") +
                " should exceed true " + ShareOf(clean.TrueShares, "party-b"));
            Assert.True(ShareOf(clean.Shares, "party-a") < ShareOf(clean.TrueShares, "party-a"));
        }

        [Fact]
        public void ProjectedTurnout_IsOverstatedWhenTurnoutVariesAcrossDistricts()
        {
            PollResult poll = PollingEngine.Run(
                Request(SaveA, PollDay, ElectionDay, TurnoutSplitCity()), NoNoise());

            // True electorate-weighted turnout: (0.3 * 0.3) + (0.7 * 0.7) = 0.58.
            // Over-weighting the high-turnout district pushes the projection above it.
            Assert.True(poll.ProjectedTurnout > 0.58,
                "projected turnout " + poll.ProjectedTurnout + " should exceed the true 0.58");
        }

        [Fact]
        public void DistrictCrosstabs_AreWellFormedInBothDirectionsOfBias()
        {
            PollResult poll = PollingEngine.Run(
                Request(SaveA, PollDay, ElectionDay, EducationSplitCity()), Shipped);

            // The engine does not reweight a district's internal composition, but an under-sampled
            // district rests on fewer respondents and so gets a noisier crosstab. Inflating that noise
            // must not push a published share out of range in either district.
            foreach (DistrictPollResult d in poll.Districts)
            {
                Assert.Equal(1.0, Sum(d.Shares), 9);
                foreach (PartyVoteShare s in d.Shares) Assert.True(s.Share > 0.0);
            }
        }

        // ==========================================================================================
        // Campaign dynamics
        // ==========================================================================================

        [Fact]
        public void RandomError_ShrinksTowardElectionDay()
        {
            // Homogeneous city, so the structural bias is exactly zero and every deviation from the
            // truth is idiosyncratic error. With the house effect off, both runs consume the identical
            // sampling draw — it is seeded on the poll date, which is the same in both — so the only
            // thing that differs is the decay-plus-herding scale. That makes this an exact comparison
            // rather than a statistical one, which matters: a test that passes on eight hardcoded
            // Guids by luck is worse than no test.
            EngineTuning tuning = SamplingErrorOnly();
            SimDate campaignOpen = PollCalendar.AddDays(PollDay, 26 * 7);

            double farError = 0.0;
            double nearError = 0.0;

            foreach (Guid save in Saves)
            {
                PollResult farPoll = PollingEngine.Run(
                    Request(save, PollDay, campaignOpen, HomogeneousCity()), tuning);
                PollResult nearPoll = PollingEngine.Run(
                    Request(save, PollDay, PollDay, HomogeneousCity()), tuning);

                Assert.Equal(26, farPoll.WeeksToElection);
                Assert.Equal(0, nearPoll.WeeksToElection);

                farError += PollingEngine.MeanAbsoluteDeviation(farPoll.Shares, farPoll.TrueShares);
                nearError += PollingEngine.MeanAbsoluteDeviation(nearPoll.Shares, nearPoll.TrueShares);
            }

            // errorDecayTowardElection = 0.6 and herdingFactor = 0.2, so on election day
            // (1 - 0.6) * (1 - 0.2) = 0.32 of the opening scale survives.
            Assert.True(farError > 0.0, "the far poll must actually contain some error");
            Assert.True(nearError > 0.0, "herding damps the error, it does not erase it");
            Assert.True(nearError * 2.0 < farError,
                "error should decay toward election day: near=" + nearError + " far=" + farError);
        }

        [Fact]
        public void StructuralBias_DoesNotDecayTowardElectionDay()
        {
            // The counterpart to the test above and the more important half: herding makes pollsters
            // agree with each other, not with reality. On election eve the poll is still wrong.
            var near = Request(SaveA, PollDay, PollDay, EducationSplitCity());
            PollResult poll = PollingEngine.Run(near, NoNoise());

            Assert.Equal(0, poll.WeeksToElection);
            Assert.Equal(0.506, ShareOf(poll.Shares, "party-b"), 12);
        }

        [Fact]
        public void UndecidedShare_DecaysTowardElectionDay()
        {
            PollResult far = PollingEngine.Run(
                Request(SaveA, PollDay, PollCalendar.AddDays(PollDay, 26 * 7), HomogeneousCity()), Shipped);
            PollResult near = PollingEngine.Run(
                Request(SaveA, PollDay, PollDay, HomogeneousCity()), Shipped);

            Assert.Equal(0.15, far.UndecidedShare, 9);           // undecidedShareBase, nothing elapsed
            Assert.True(near.UndecidedShare < far.UndecidedShare);
            Assert.True(near.UndecidedShare >= 0.0);
        }

        [Fact]
        public void NoElectionScheduled_IsTreatedAsMaximallyDistant()
        {
            PollResult poll = PollingEngine.Run(
                Request(SaveA, PollDay, null, HomogeneousCity()), Shipped);

            Assert.Equal(26, poll.WeeksToElection);   // polling.weeksBeforeElection
            Assert.Null(poll.ElectionDate);
            Assert.Equal(0.15, poll.UndecidedShare, 9);
        }

        // ==========================================================================================
        // Shape of the published result
        // ==========================================================================================

        [Fact]
        public void PublishedShares_SumToOne_AndAreSortedByPartyId()
        {
            PollResult poll = PollingEngine.Run(
                Request(SaveA, PollDay, ElectionDay, EducationSplitCity()), Shipped);

            Assert.Equal(1.0, Sum(poll.Shares), 9);
            Assert.Equal(1.0, Sum(poll.TrueShares), 9);

            for (int i = 1; i < poll.Shares.Count; i++)
                Assert.True(string.CompareOrdinal(poll.Shares[i - 1].PartyId, poll.Shares[i].PartyId) < 0);
        }

        [Fact]
        public void Districts_AreSortedByDistrictId()
        {
            var districts = EducationSplitCity();
            districts.Reverse();

            PollResult poll = PollingEngine.Run(Request(SaveA, PollDay, ElectionDay, districts), Shipped);

            for (int i = 1; i < poll.Districts.Count; i++)
                Assert.True(string.CompareOrdinal(poll.Districts[i - 1].DistrictId,
                                                  poll.Districts[i].DistrictId) < 0);
        }

        [Fact]
        public void PublishedShares_NeverReportAPartyAtZero_ButTrueSharesMay()
        {
            var districts = new List<DistrictPollInput>
            {
                new DistrictPollInput
                {
                    DistrictId = "d-only",
                    EducationIndex = 0.5,
                    ProjectedTurnout = 0.6,
                    EligibleVoters = 1000,
                    TrueShares = new List<PartyVoteShare>
                    {
                        new PartyVoteShare("party-a", 0.5),
                        new PartyVoteShare("party-b", 0.5),
                        new PartyVoteShare("party-c", 0.0)
                    }
                }
            };

            PollResult poll = PollingEngine.Run(Request(SaveA, PollDay, ElectionDay, districts), NoNoise());

            // The floor applies to the published figure only. Model truth must be able to say zero.
            Assert.True(ShareOf(poll.Shares, "party-c") > 0.0);
            Assert.Equal(0.0, ShareOf(poll.TrueShares, "party-c"), 12);
            Assert.Equal(1.0, Sum(poll.Shares), 9);
        }

        [Fact]
        public void MarginOfError_TightensAsTheSampleGrows()
        {
            EngineTuning small = EngineTuning.FromJson(
                "{\"polling\":{\"sampleSizeBase\":100,\"sampleSizeVariance\":0.0}}");
            EngineTuning large = EngineTuning.FromJson(
                "{\"polling\":{\"sampleSizeBase\":4000,\"sampleSizeVariance\":0.0}}");

            PollResult a = PollingEngine.Run(Request(SaveA, PollDay, ElectionDay, HomogeneousCity()), small);
            PollResult b = PollingEngine.Run(Request(SaveA, PollDay, ElectionDay, HomogeneousCity()), large);

            Assert.Equal(100, a.SampleSize);
            Assert.Equal(4000, b.SampleSize);
            Assert.True(b.MarginOfError < a.MarginOfError);

            // 1.96 * sqrt(0.25 / 4000) = 0.0154951 -> 0.015 at polling.roundingDecimals = 3.
            Assert.Equal(0.015, b.MarginOfError, 9);
            // 1.96 * sqrt(0.25 / 100) = 0.098 exactly.
            Assert.Equal(0.098, a.MarginOfError, 9);
        }

        [Fact]
        public void EmptyCity_ProducesAnEmptyPollRatherThanThrowing()
        {
            PollResult poll = PollingEngine.Run(
                Request(SaveA, PollDay, ElectionDay, new List<DistrictPollInput>()), Shipped);

            Assert.Empty(poll.Shares);
            Assert.Empty(poll.TrueShares);
            Assert.Empty(poll.Districts);
            Assert.Equal(0.0, poll.ProjectedTurnout, 12);
            Assert.True(poll.SampleSize >= 1);
        }

        [Fact]
        public void CityWithNoModelledVoters_DoesNotDivideByZero()
        {
            var districts = new List<DistrictPollInput>
            {
                District("d-a", 0.5, 0.0, 0, 0.5, 0.5),
                District("d-b", 0.5, 0.0, 0, 0.5, 0.5)
            };

            PollResult poll = PollingEngine.Run(Request(SaveA, PollDay, ElectionDay, districts), Shipped);

            Assert.Equal(1.0, Sum(poll.Shares), 9);
            foreach (PartyVoteShare s in poll.Shares) Assert.False(double.IsNaN(s.Share));
        }

        [Fact]
        public void PollsterName_IsLeftToTheFlavorLayer()
        {
            PollResult poll = PollingEngine.Run(
                Request(SaveA, PollDay, ElectionDay, HomogeneousCity()), Shipped);

            // Non-negotiable #1 in reverse: the engine owns the id, the flavor provider owns the name.
            Assert.Equal("pollster-01", poll.PollsterId);
            Assert.Equal("", poll.PollsterName);
        }

        [Fact]
        public void MeanAbsoluteDeviation_IsSymmetricAndCountsMissingPartiesInFull()
        {
            var a = new List<PartyVoteShare> { new PartyVoteShare("party-a", 0.6), new PartyVoteShare("party-b", 0.4) };
            var b = new List<PartyVoteShare> { new PartyVoteShare("party-a", 0.5), new PartyVoteShare("party-b", 0.5) };

            Assert.Equal(0.1, PollingEngine.MeanAbsoluteDeviation(a, b), 9);
            Assert.Equal(0.1, PollingEngine.MeanAbsoluteDeviation(b, a), 9);
            Assert.Equal(0.0, PollingEngine.MeanAbsoluteDeviation(a, a), 12);

            var c = new List<PartyVoteShare> { new PartyVoteShare("party-a", 1.0) };
            // party-b is absent from c, so it contributes its full 0.4 across a universe of two.
            Assert.Equal((0.4 + 0.4) / 2.0, PollingEngine.MeanAbsoluteDeviation(a, c), 9);
        }

        // ==========================================================================================
        // Calendar
        // ==========================================================================================

        [Fact]
        public void PollCalendar_RoundTripsEveryDate()
        {
            var dates = new[]
            {
                new SimDate(1970, 1, 1), new SimDate(1990, 1, 1), new SimDate(1994, 6, 5),
                new SimDate(2000, 2, 29), new SimDate(1999, 12, 31), new SimDate(2026, 7, 30)
            };

            foreach (SimDate d in dates)
                Assert.Equal(d, PollCalendar.FromDayNumber(PollCalendar.ToDayNumber(d)));

            Assert.Equal(0, PollCalendar.ToDayNumber(new SimDate(1970, 1, 1)));
        }

        [Fact]
        public void PollCalendar_CountsDaysAcrossMonthAndLeapBoundaries()
        {
            Assert.Equal(7, PollCalendar.DaysBetween(new SimDate(1990, 1, 1), new SimDate(1990, 1, 8)));
            Assert.Equal(146, PollCalendar.DaysBetween(PollDay, ElectionDay));
            Assert.Equal(20, PollCalendar.WeeksBetween(PollDay, ElectionDay));

            Assert.Equal(2, PollCalendar.DaysBetween(new SimDate(1996, 2, 28), new SimDate(1996, 3, 1)));
            Assert.Equal(1, PollCalendar.DaysBetween(new SimDate(1997, 2, 28), new SimDate(1997, 3, 1)));
            Assert.Equal(-7, PollCalendar.DaysBetween(new SimDate(1990, 1, 8), new SimDate(1990, 1, 1)));
        }

        [Fact]
        public void PollCalendar_ClampsAnOverlongDayRatherThanRollingIntoTheNextMonth()
        {
            // SimDate validates only 1-31, so "1994-02-31" is constructible. Rolling it over would make
            // it collide with 1994-03-03 and two distinct sim dates would seed identically.
            Assert.Equal(PollCalendar.ToDayNumber(new SimDate(1994, 2, 28)),
                         PollCalendar.ToDayNumber(new SimDate(1994, 2, 31)));
        }

        // ==========================================================================================
        // Schedule
        // ==========================================================================================

        [Fact]
        public void PublishDates_CoverTheCampaignWeeklyAndEndOnElectionDay()
        {
            List<SimDate> dates = PollSchedule.PublishDates(ElectionDay, Shipped);

            Assert.Equal(27, dates.Count);                                  // 26 weeks inclusive of both ends
            Assert.Equal(PollSchedule.CampaignStart(ElectionDay, Shipped), dates[0]);
            Assert.Equal(ElectionDay, dates[dates.Count - 1]);

            for (int i = 1; i < dates.Count; i++)
                Assert.Equal(7, PollCalendar.DaysBetween(dates[i - 1], dates[i]));
        }

        [Fact]
        public void IsPublishDay_IsTrueOnlyOnScheduleDays()
        {
            SimDate start = PollSchedule.CampaignStart(ElectionDay, Shipped);

            Assert.True(PollSchedule.IsPublishDay(start, ElectionDay, Shipped));
            Assert.False(PollSchedule.IsPublishDay(PollCalendar.AddDays(start, 1), ElectionDay, Shipped));
            Assert.True(PollSchedule.IsPublishDay(PollCalendar.AddDays(start, 7), ElectionDay, Shipped));
            Assert.False(PollSchedule.IsPublishDay(PollCalendar.AddDays(start, -1), ElectionDay, Shipped));
            Assert.False(PollSchedule.IsPublishDay(PollCalendar.AddDays(ElectionDay, 1), ElectionDay, Shipped));
        }

        [Fact]
        public void PollsterRotation_CyclesThroughEveryHouse()
        {
            Assert.Equal("pollster-01", PollSchedule.PollsterIdFor(0, Shipped));
            Assert.Equal("pollster-02", PollSchedule.PollsterIdFor(1, Shipped));
            Assert.Equal("pollster-03", PollSchedule.PollsterIdFor(2, Shipped));
            Assert.Equal("pollster-01", PollSchedule.PollsterIdFor(3, Shipped));   // pollsterCount = 3

            SimDate start = PollSchedule.CampaignStart(ElectionDay, Shipped);
            Assert.Equal("pollster-01", PollSchedule.PollsterForDate(start, ElectionDay, Shipped));
            Assert.Equal("pollster-02", PollSchedule.PollsterForDate(PollCalendar.AddDays(start, 7), ElectionDay, Shipped));
            Assert.Null(PollSchedule.PollsterForDate(PollCalendar.AddDays(start, 3), ElectionDay, Shipped));
        }

        [Fact]
        public void Trim_KeepsTheNewestPollsOldestFirst()
        {
            var polls = new List<PollResult>();
            for (int i = 0; i < 70; i++)
            {
                SimDate d = PollCalendar.AddDays(new SimDate(1990, 1, 1), i * 7);
                polls.Add(new PollResult { Id = "poll-" + d, Date = d });
            }

            // Reversed on the way in: the trim must not depend on the caller's insertion order.
            polls.Reverse();
            List<PollResult> kept = PollSchedule.Trim(polls, Shipped);

            Assert.Equal(60, kept.Count);                                    // polling.maxStoredPolls
            Assert.Equal(PollCalendar.AddDays(new SimDate(1990, 1, 1), 10 * 7), kept[0].Date);
            Assert.Equal(PollCalendar.AddDays(new SimDate(1990, 1, 1), 69 * 7), kept[kept.Count - 1].Date);

            for (int i = 1; i < kept.Count; i++)
                Assert.True(kept[i - 1].Date < kept[i].Date);
        }

        [Fact]
        public void Trim_LeavesAShortHistoryAlone()
        {
            var polls = new List<PollResult>
            {
                new PollResult { Id = "poll-1990-01-01", Date = new SimDate(1990, 1, 1) },
                new PollResult { Id = "poll-1990-01-08", Date = new SimDate(1990, 1, 8) }
            };

            Assert.Equal(2, PollSchedule.Trim(polls, Shipped).Count);
            Assert.Empty(PollSchedule.Trim(null, Shipped));
        }

        // ==========================================================================================
        // Tuning wiring
        // ==========================================================================================

        [Fact]
        public void BiasMagnitude_ScalesWithTheTuningCoefficient()
        {
            EngineTuning doubled = EngineTuning.FromJson(
                "{\"polling\":{\"errorSigma\":0.0,\"houseEffectSigma\":0.0,\"educationUnderSampleBias\":0.08}}");

            PollResult shipped = PollingEngine.Run(
                Request(SaveA, PollDay, ElectionDay, EducationSplitCity()), NoNoise());
            PollResult stronger = PollingEngine.Run(
                Request(SaveA, PollDay, ElectionDay, EducationSplitCity()), doubled);

            // No coefficient is hardcoded: doubling the tuning value must widen the gap.
            Assert.True(ShareOf(stronger.Shares, "party-b") > ShareOf(shipped.Shares, "party-b"));
            Assert.True(DistrictOf(stronger, "d-low-education").SamplingBias
                        < DistrictOf(shipped, "d-low-education").SamplingBias);
        }

        [Fact]
        public void ZeroBias_ProducesAnHonestPoll()
        {
            EngineTuning honest = EngineTuning.FromJson(
                "{\"polling\":{\"errorSigma\":0.0,\"houseEffectSigma\":0.0," +
                "\"educationUnderSampleBias\":0.0,\"turnoutUnderSampleBias\":0.0}}");

            PollResult poll = PollingEngine.Run(
                Request(SaveA, PollDay, ElectionDay, EducationSplitCity()), honest);

            Assert.Equal(ShareOf(poll.TrueShares, "party-a"), ShareOf(poll.Shares, "party-a"), 9);
            foreach (DistrictPollResult d in poll.Districts)
                Assert.Equal(0.0, d.SamplingBias, 12);
        }
    }
}
