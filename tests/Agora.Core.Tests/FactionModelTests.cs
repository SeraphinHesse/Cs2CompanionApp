using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Agora.Core.Engine.Factions;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Packet 3 — factions (NA theme): generation, internal support, dominance, platform authorship
    /// and lifecycle.
    ///
    /// <para>
    /// Fixtures are synthetic and built here on purpose (see <c>/write-test</c>): they diff cleanly
    /// and they do not rot when <c>CitySnapshot</c> gains a field. Nothing in this file touches the
    /// filesystem, the clock or the game.
    /// </para>
    /// </summary>
    public class FactionModelTests
    {
        private static readonly Guid SaveA = new Guid("11111111-2222-3333-4444-555555555555");
        private static readonly Guid SaveB = new Guid("99999999-8888-7777-6666-555555555555");
        private static readonly SimDate Jan1990 = new SimDate(1990, 1, 1);
        private static readonly SimDate Jan1991 = new SimDate(1991, 1, 1);

        private static EngineTuning Tuning => EngineTuning.Default;

        // ------------------------------------------------------------------ fixtures

        /// <summary>
        /// A synthetic city: every bloc key present in each district, ideals and weights driven off the
        /// three demographic axes, minors correctly disenfranchised (eligible voters = 0).
        /// </summary>
        private static List<Bloc> SyntheticBlocs(params string[] districtIds)
        {
            var blocs = new List<Bloc>();
            for (int d = 0; d < districtIds.Length; d++)
            {
                string district = districtIds[d];
                for (int k = 0; k < BlocAxes.AllKeys.Count; k++)
                {
                    BlocKey key = BlocAxes.AllKeys[k];
                    double w = BlocAxes.Axis(key.Wealth);
                    double e = BlocAxes.Axis(key.Education);
                    double a = BlocAxes.Axis(key.Age);

                    int population = 100 + key.Ordinal * 5 + d * 7;
                    bool ofAge = key.Age == AgeBand.Adult || key.Age == AgeBand.Elderly;

                    blocs.Add(new Bloc
                    {
                        DistrictId = district,
                        Key = key,
                        Population = population,
                        PopulationShare = 1.0 / BlocAxes.BlocCount,
                        EligibleVoters = ofAge ? population : 0,
                        Weights = new IssueWeights(
                            1.0 + 0.20 * e, 1.0 - 0.30 * w, 1.0 + 0.40 * e,
                            1.0 + 0.30 * a, 1.0 - 0.20 * w, 1.0 + 0.25 * a).Clamped(0.05, 3.0),
                        Ideal = new IssuePosition(
                            0.30 - 0.30 * w, 0.40 - 0.40 * w, 0.20 + 0.30 * e,
                            0.20 + 0.20 * e, 0.10 + 0.20 * w, 0.20 + 0.30 * a).Clamped(),
                        Happiness = 55.0,
                        Discontent = 0.50 + 0.10 * w
                    });
                }
            }
            return blocs;
        }

        private static Party MakeParty(string id, IssuePosition platform, Issue grievance) => new Party
        {
            Id = id,
            ArchetypeId = "test",
            Platform = platform,
            LastManifesto = platform,
            CoreGrievance = grievance,
            Status = PartyStatus.Active,
            FoundedDate = Jan1990
        };

        private static List<Party> TwoParties() => new List<Party>
        {
            MakeParty("party-01", new IssuePosition(0.4, 0.3, 0.1, 0.2, -0.1, 0.0), Issue.Services),
            MakeParty("party-02", new IssuePosition(-0.2, -0.3, 0.0, -0.1, 0.4, 0.3), Issue.Growth)
        };

        private static Faction MakeFaction(string id, string partyId, double support,
                                           bool dominant, IssuePosition platform, Issue grievance)
            => new Faction
            {
                Id = id,
                PartyId = partyId,
                ArchetypeId = FactionArchetypes.For(grievance, 1).Id,
                Platform = platform,
                InternalSupport = support,
                IsDominant = dominant,
                Status = FactionStatus.Active,
                FoundedDate = Jan1990,
                CoreGrievance = grievance,
                Demands = new List<Issue> { grievance }
            };

        private static IssuePosition Flat(double v) => new IssuePosition(v, v, v, v, v, v);

        // ------------------------------------------------------------------ canonical hashing

        /// <summary>
        /// Serializes a faction set to a canonical string and hashes it. Hashing rather than asserting
        /// field by field is the point: it catches the field a hand-written assertion forgot, which is
        /// exactly where a desync hides.
        /// </summary>
        private static string Hash(IEnumerable<Faction> factions) => Sha(Serialize(factions));

        /// <summary>
        /// Canonical hash of a whole cycle: factions, dominance, authored platforms and lifecycle
        /// events.
        /// </summary>
        /// <remarks>
        /// Hashing only the faction list is not enough for the negative control. A cycle in which no
        /// structural roll fires leaves the faction list byte-identical whatever the date, so a
        /// "different date must differ" assertion over that list alone would be asserting a
        /// coincidence rather than the seed derivation. The cycle's reported events are part of its
        /// output and carry the draws that changed nothing else.
        /// </remarks>
        private static string HashCycle(FactionCycleResult r)
        {
            var sb = new StringBuilder();
            sb.Append(Serialize(r.Factions));

            for (int i = 0; i < r.Dominance.Count; i++)
            {
                DominanceOutcome d = r.Dominance[i];
                sb.Append(d.PartyId).Append('>')
                  .Append(d.PreviousDominantFactionId ?? "-").Append('>')
                  .Append(d.DominantFactionId ?? "-").Append('\n');
            }

            for (int i = 0; i < r.Platforms.Count; i++)
            {
                PlatformAuthorship a = r.Platforms[i];
                sb.Append(a.PartyId).Append('>').Append(a.DominantFactionId ?? "-").Append('>');

                for (int n = 0; n < Issues.All.Count; n++)
                    sb.Append(N(a.Platform[Issues.All[n]])).Append(',');
                sb.Append('|');

                for (int w = 0; w < a.Weights.Count; w++)
                    sb.Append(a.Weights[w].FactionId).Append('=').Append(N(a.Weights[w].Weight)).Append(';');
                sb.Append('|');

                for (int n = 0; n < a.Issues.Count; n++)
                    sb.Append(a.Issues[n].Issue).Append('=').Append(a.Issues[n].FactionId).Append('=')
                      .Append(N(a.Issues[n].Contribution)).Append(';');
                sb.Append('\n');
            }

            for (int i = 0; i < r.Events.Count; i++)
                sb.Append(r.Events[i].ToString()).Append('\n');

            return Sha(sb.ToString());
        }

        private static string Serialize(IEnumerable<Faction> factions)
        {
            var sb = new StringBuilder();
            foreach (Faction f in factions)
            {
                sb.Append(f.Id).Append('|')
                  .Append(f.PartyId).Append('|')
                  .Append(f.ArchetypeId).Append('|')
                  .Append(f.Status).Append('|')
                  .Append(f.CoreGrievance).Append('|')
                  .Append(f.FoundedDate).Append('|')
                  .Append(f.DissolvedDate.HasValue ? f.DissolvedDate.Value.ToString() : "-").Append('|')
                  .Append(f.PredecessorFactionId ?? "-").Append('|')
                  .Append(f.SuccessorFactionId ?? "-").Append('|')
                  .Append(N(f.InternalSupport)).Append('|')
                  .Append(N(f.TensionWithParty)).Append('|')
                  .Append(f.IsDominant ? "1" : "0").Append('|')
                  .Append(f.ConsecutiveCyclesBelowThreshold).Append('|');

                for (int i = 0; i < Issues.All.Count; i++)
                    sb.Append(N(f.Platform[Issues.All[i]])).Append(',');
                sb.Append('|');

                for (int i = 0; i < f.Demands.Count; i++) sb.Append(f.Demands[i]).Append(',');
                sb.Append('|');

                for (int i = 0; i < f.CoreBlocs.Count; i++) sb.Append(f.CoreBlocs[i].Id).Append(',');
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static string Sha(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var hex = new StringBuilder(digest.Length * 2);
                for (int i = 0; i < digest.Length; i++) hex.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        private static string N(double v) => v.ToString("G17", CultureInfo.InvariantCulture);

        // ================================================================== generation

        [Fact]
        public void Generate_ProducesIdenticalOutputTwiceFromTheSameSeed()
        {
            List<Bloc> blocs = SyntheticBlocs("district-a", "district-b");

            string first = Hash(FactionModel.Generate(TwoParties(), blocs, SaveA, Jan1990, Tuning));
            string second = Hash(FactionModel.Generate(TwoParties(), blocs, SaveA, Jan1990, Tuning));

            Assert.Equal(first, second);
        }

        [Fact]
        public void Generate_DiffersForADifferentSaveOrDate()
        {
            List<Bloc> blocs = SyntheticBlocs("district-a", "district-b");

            string baseline = Hash(FactionModel.Generate(TwoParties(), blocs, SaveA, Jan1990, Tuning));
            string otherSave = Hash(FactionModel.Generate(TwoParties(), blocs, SaveB, Jan1990, Tuning));
            string otherDate = Hash(FactionModel.Generate(TwoParties(), blocs, SaveA, Jan1991, Tuning));

            // Without these a generator that returns a constant would pass the determinism test above.
            Assert.NotEqual(baseline, otherSave);
            Assert.NotEqual(baseline, otherDate);
        }

        [Fact]
        public void Generate_KeepsEachPartyWithinTheTunedFactionCount()
        {
            List<Party> parties = TwoParties();
            List<Faction> factions = FactionModel.Generate(parties, SyntheticBlocs("district-a"), SaveA, Jan1990, Tuning);

            FactionsTuning t = Tuning.Factions;
            for (int p = 0; p < parties.Count; p++)
            {
                int count = 0;
                for (int i = 0; i < factions.Count; i++)
                    if (factions[i].PartyId == parties[p].Id) count++;

                Assert.InRange(count, t.MinPerParty, t.MaxPerParty);
            }
        }

        [Fact]
        public void Generate_SupportSumsToExactlyOnePerParty()
        {
            List<Party> parties = TwoParties();
            List<Faction> factions = FactionModel.Generate(parties, SyntheticBlocs("district-a"), SaveA, Jan1990, Tuning);

            for (int p = 0; p < parties.Count; p++)
            {
                double sum = 0.0;
                for (int i = 0; i < factions.Count; i++)
                    if (factions[i].PartyId == parties[p].Id) sum += factions[i].InternalSupport;

                Assert.Equal(1.0, sum, 12);
            }
        }

        [Fact]
        public void Generate_GivesEveryFactionOfAPartyADistinctCoreGrievance()
        {
            List<Party> parties = TwoParties();
            List<Faction> factions = FactionModel.Generate(parties, SyntheticBlocs("district-a"), SaveA, Jan1990, Tuning);

            for (int p = 0; p < parties.Count; p++)
            {
                var seen = new List<Issue>();
                for (int i = 0; i < factions.Count; i++)
                {
                    if (factions[i].PartyId != parties[p].Id) continue;
                    Assert.DoesNotContain(factions[i].CoreGrievance, seen);
                    seen.Add(factions[i].CoreGrievance);
                }

                // The party's own grievance always has an internal champion (or sceptic).
                Assert.Contains(parties[p].CoreGrievance, seen);
            }
        }

        [Fact]
        public void Generate_LeavesEveryFlavorOwnedFieldEmpty()
        {
            // Non-negotiable #1: names, descriptions and leader names come from IFlavorProvider.
            // The engine must never author them, not even as a placeholder that could be mistaken
            // for prose and fed back into a calculation.
            List<Faction> factions = FactionModel.Generate(TwoParties(), SyntheticBlocs("district-a"), SaveA, Jan1990, Tuning);

            Assert.NotEmpty(factions);
            for (int i = 0; i < factions.Count; i++)
            {
                Assert.Equal("", factions[i].Name);
                Assert.Equal("", factions[i].ShortName);
                Assert.Equal("", factions[i].Description);
                Assert.Equal("", factions[i].LeaderName);

                // …but engine-owned identity is always present.
                Assert.False(string.IsNullOrEmpty(factions[i].Id));
                Assert.True(FactionArchetypes.TryGet(factions[i].ArchetypeId, out FactionArchetype _));
            }
        }

        [Fact]
        public void Generate_AssignsUniqueParseableIdsAcrossParties()
        {
            List<Faction> factions = FactionModel.Generate(TwoParties(), SyntheticBlocs("district-a"), SaveA, Jan1990, Tuning);

            var seen = new List<string>();
            for (int i = 0; i < factions.Count; i++)
            {
                Assert.DoesNotContain(factions[i].Id, seen);
                seen.Add(factions[i].Id);
                Assert.True(FactionIds.TryParseOrdinal(factions[i].Id, out int ordinal));
                Assert.True(ordinal >= 1);
            }
        }

        [Fact]
        public void Generate_ClampsPlatformsIntoUnitRangeEvenWithAnAbsurdSpread()
        {
            // Cap test: drive the tuned spread far past anything sane and prove the clamp still holds
            // in both directions. A platform component outside [-1, +1] would make WeightedDistance
            // exceed 1 and corrupt every affinity downstream.
            EngineTuning wild = EngineTuning.FromJson("{\"factions\":{\"archetypeSpreadSigma\":25.0}}");
            Assert.Equal(25.0, wild.Factions.ArchetypeSpreadSigma, 12);

            var parties = new List<Party>
            {
                MakeParty("party-01", Flat(1.0), Issue.Services),
                MakeParty("party-02", Flat(-1.0), Issue.Environment)
            };

            List<Faction> factions = FactionModel.Generate(parties, SyntheticBlocs("district-a"), SaveA, Jan1990, wild);

            Assert.NotEmpty(factions);
            for (int i = 0; i < factions.Count; i++)
                for (int n = 0; n < Issues.All.Count; n++)
                    Assert.InRange(factions[i].Platform[Issues.All[n]], -1.0, 1.0);
        }

        [Fact]
        public void Generate_ExcludesMinorsFromEveryCoreConstituency()
        {
            // Children and teens are disenfranchised by a turnout multiplier of 0, not by omission —
            // so they exist as blocs but must never appear in a faction's political base.
            List<Faction> factions = FactionModel.Generate(TwoParties(), SyntheticBlocs("district-a"), SaveA, Jan1990, Tuning);

            bool anyCoreBlocs = false;
            for (int i = 0; i < factions.Count; i++)
            {
                for (int b = 0; b < factions[i].CoreBlocs.Count; b++)
                {
                    anyCoreBlocs = true;
                    AgeBand band = factions[i].CoreBlocs[b].Age;
                    Assert.True(band == AgeBand.Adult || band == AgeBand.Elderly);
                }
            }
            Assert.True(anyCoreBlocs);
        }

        // ================================================================== dominance

        [Fact]
        public void Dominance_IsVacantWhenNobodyClearsTheThreshold()
        {
            // Three-way 1/3 split: nobody reaches dominanceThreshold (0.45), so the party writes its
            // platform by committee rather than handing the pen to a plurality.
            var factions = new List<Faction>
            {
                MakeFaction("faction-01", "party-01", 0.34, false, Flat(0.2), Issue.Services),
                MakeFaction("faction-02", "party-01", 0.33, false, Flat(-0.2), Issue.Growth),
                MakeFaction("faction-03", "party-01", 0.33, false, Flat(0.0), Issue.Transit)
            };

            DominanceOutcome outcome = FactionDominance.Apply("party-01", factions, Tuning);

            Assert.True(outcome.IsVacant);
            Assert.Null(outcome.DominantFactionId);
            for (int i = 0; i < factions.Count; i++) Assert.False(factions[i].IsDominant);
        }

        [Fact]
        public void Dominance_HysteresisKeepsTheIncumbentAgainstANarrowChallenger()
        {
            // Challenger leads by 0.03; dominanceHysteresis is 0.05, so the pen does not move.
            var factions = new List<Faction>
            {
                MakeFaction("faction-01", "party-01", 0.485, true, Flat(0.2), Issue.Services),
                MakeFaction("faction-02", "party-01", 0.515, false, Flat(-0.2), Issue.Growth)
            };

            DominanceOutcome outcome = FactionDominance.Apply("party-01", factions, Tuning);

            Assert.Equal("faction-01", outcome.DominantFactionId);
            Assert.False(outcome.IsTakeover);
            Assert.True(factions[0].IsDominant);
            Assert.False(factions[1].IsDominant);
        }

        [Fact]
        public void Dominance_ChallengerTakesOverOnceItClearsTheHysteresisBand()
        {
            var factions = new List<Faction>
            {
                MakeFaction("faction-01", "party-01", 0.45, true, Flat(0.2), Issue.Services),
                MakeFaction("faction-02", "party-01", 0.55, false, Flat(-0.2), Issue.Growth)
            };

            DominanceOutcome outcome = FactionDominance.Apply("party-01", factions, Tuning);

            Assert.Equal("faction-02", outcome.DominantFactionId);
            Assert.Equal("faction-01", outcome.PreviousDominantFactionId);
            Assert.True(outcome.IsTakeover);
        }

        [Fact]
        public void Dominance_BreaksAnExactTieOnTheLowerIdWithNoDraw()
        {
            var factions = new List<Faction>
            {
                MakeFaction("faction-02", "party-01", 0.5, false, Flat(0.2), Issue.Services),
                MakeFaction("faction-01", "party-01", 0.5, false, Flat(-0.2), Issue.Growth)
            };

            Assert.Equal("faction-01", FactionDominance.Select(factions, Tuning));
            // Stable across repeats: there is no seeded draw in the tie-break at all.
            Assert.Equal("faction-01", FactionDominance.Select(factions, Tuning));
        }

        [Fact]
        public void Dominance_NeverLeavesThePenWithADissolvedFaction()
        {
            var factions = new List<Faction>
            {
                MakeFaction("faction-01", "party-01", 0.0, true, Flat(0.2), Issue.Services),
                MakeFaction("faction-02", "party-01", 1.0, false, Flat(-0.2), Issue.Growth)
            };
            factions[0].Status = FactionStatus.Dissolved;

            DominanceOutcome outcome = FactionDominance.Apply("party-01", factions, Tuning);

            Assert.Equal("faction-02", outcome.DominantFactionId);
            Assert.False(factions[0].IsDominant);
        }

        // ================================================================== platform authorship

        [Fact]
        public void Platform_DominantFactionCarriesItsTunedShareOfTheBlend()
        {
            // platformWeightDominant 0.60 / platformWeightOthers 0.40 → 0.6·(+1) + 0.4·(−1) = +0.2.
            var party = MakeParty("party-01", IssuePosition.Centre, Issue.Services);
            var factions = new List<Faction>
            {
                MakeFaction("faction-01", "party-01", 0.6, true, Flat(1.0), Issue.Services),
                MakeFaction("faction-02", "party-01", 0.4, false, Flat(-1.0), Issue.Growth)
            };

            PlatformAuthorship authored = FactionPlatform.Author(party, factions, Tuning);

            Assert.Equal("faction-01", authored.DominantFactionId);
            Assert.Equal(0.2, authored.Platform.Services, 12);
            Assert.Equal(0.2, authored.Platform.HeritageOrder, 12);
            Assert.Equal(0.6, authored.Weights[0].Weight, 12);
            Assert.Equal(0.4, authored.Weights[1].Weight, 12);
        }

        [Fact]
        public void Platform_WithoutADominantFactionIsAPlainSupportWeightedMean()
        {
            var party = MakeParty("party-01", IssuePosition.Centre, Issue.Services);
            var factions = new List<Faction>
            {
                MakeFaction("faction-01", "party-01", 0.25, false, Flat(1.0), Issue.Services),
                MakeFaction("faction-02", "party-01", 0.75, false, Flat(-1.0), Issue.Growth)
            };

            PlatformAuthorship authored = FactionPlatform.Author(party, factions, Tuning);

            Assert.Null(authored.DominantFactionId);
            Assert.Equal(-0.5, authored.Platform.Transit, 12);
        }

        [Fact]
        public void Platform_AttributesEveryIssueToTheFactionThatDroveIt()
        {
            var party = MakeParty("party-01", IssuePosition.Centre, Issue.Services);
            var factions = new List<Faction>
            {
                MakeFaction("faction-01", "party-01", 0.6, true,
                            new IssuePosition(1.0, 0.0, 0.0, 0.0, 0.0, 0.0), Issue.Services),
                MakeFaction("faction-02", "party-01", 0.4, false,
                            new IssuePosition(0.0, -1.0, 0.0, 0.0, 0.0, 0.0), Issue.CostOfLiving)
            };

            PlatformAuthorship authored = FactionPlatform.Author(party, factions, Tuning);

            Assert.Equal(Issues.Count, authored.Issues.Count);
            for (int i = 0; i < authored.Issues.Count; i++)
                Assert.Equal(Issues.All[i], authored.Issues[i].Issue);

            Assert.Equal("faction-01", Owner(authored, Issue.Services));
            Assert.Equal("faction-02", Owner(authored, Issue.CostOfLiving));
            Assert.Equal(0.6, ContributionOf(authored, Issue.Services), 12);
            Assert.Equal(-0.4, ContributionOf(authored, Issue.CostOfLiving), 12);
        }

        private static string Owner(PlatformAuthorship a, Issue issue)
        {
            for (int i = 0; i < a.Issues.Count; i++) if (a.Issues[i].Issue == issue) return a.Issues[i].FactionId;
            throw new InvalidOperationException("Issue missing from authorship.");
        }

        private static double ContributionOf(PlatformAuthorship a, Issue issue)
        {
            for (int i = 0; i < a.Issues.Count; i++) if (a.Issues[i].Issue == issue) return a.Issues[i].Contribution;
            throw new InvalidOperationException("Issue missing from authorship.");
        }

        [Fact]
        public void Platform_BlendIsConvexSoItCannotEscapeTheFactionHull()
        {
            var party = MakeParty("party-01", IssuePosition.Centre, Issue.Services);
            var factions = new List<Faction>
            {
                MakeFaction("faction-01", "party-01", 0.9, true, Flat(1.0), Issue.Services),
                MakeFaction("faction-02", "party-01", 0.05, false, Flat(1.0), Issue.Growth),
                MakeFaction("faction-03", "party-01", 0.05, false, Flat(1.0), Issue.Transit)
            };

            PlatformAuthorship authored = FactionPlatform.Author(party, factions, Tuning);

            for (int n = 0; n < Issues.All.Count; n++)
                Assert.Equal(1.0, authored.Platform[Issues.All[n]], 12);
        }

        // ================================================================== tension and demands

        [Fact]
        public void Tension_IsMeasuredOverDemandsNotOverAllSixIssues()
        {
            // Faction differs from the party by 1.0 on Services and 0.5 on CostOfLiving, and by
            // nothing elsewhere. Weighted over its two demands: (1.0 + 0.5) / (2 · 2) = 0.375.
            // Flat over all six it would be (1.0 + 0.5) / (6 · 2) = 0.125 — permanently below
            // internalTensionThreshold, which would make a split impossible by construction.
            var faction = MakeFaction("faction-01", "party-01", 0.5, false,
                new IssuePosition(1.0, 0.5, 0.0, 0.0, 0.0, 0.0), Issue.Services);
            faction.Demands = new List<Issue> { Issue.Services, Issue.CostOfLiving };

            double tension = FactionPlatform.Tension(faction, IssuePosition.Centre);

            Assert.Equal(0.375, tension, 12);
            Assert.Equal(0.125, faction.Platform.Distance(IssuePosition.Centre), 12);
            Assert.True(tension > faction.Platform.Distance(IssuePosition.Centre));
        }

        [Fact]
        public void Tension_RisesAsThePartyWalksAwayFromTheFaction()
        {
            var faction = MakeFaction("faction-01", "party-01", 0.5, false, Flat(0.8), Issue.Services);
            faction.Demands = new List<Issue> { Issue.Services, Issue.Growth };

            double near = FactionPlatform.Tension(faction, Flat(0.7));
            double mid = FactionPlatform.Tension(faction, Flat(0.0));
            double far = FactionPlatform.Tension(faction, Flat(-1.0));

            Assert.True(near < mid, "tension must rise as the party moves away");
            Assert.True(mid < far, "tension must keep rising as the gap widens");
            Assert.InRange(far, 0.0, 1.0);
        }

        [Fact]
        public void Demands_LeadWithTheCoreGrievanceThenTheWidestGap()
        {
            var faction = MakeFaction("faction-01", "party-01", 0.5, false,
                new IssuePosition(0.1, 0.0, 0.0, 0.9, 0.0, 0.0), Issue.Services);

            List<Issue> demands = FactionPlatform.Demands(faction, IssuePosition.Centre, Tuning);

            Assert.Equal(Tuning.Factions.DemandCountPerFaction, demands.Count);
            Assert.Equal(Issue.Services, demands[0]);   // core grievance always leads
            Assert.Equal(Issue.Transit, demands[1]);    // widest remaining gap (0.9)
        }

        // ================================================================== support

        [Fact]
        public void DriftStep_ClampsAtTheTunedCapInBothDirections()
        {
            // Cap test: push the gap far past anything the model can produce and prove the clamp holds
            // for a rising and a falling faction alike.
            double cap = Tuning.Factions.SupportDriftCapPerCycle;

            Assert.Equal(cap, FactionSupport.DriftStep(0.0, 5.0, Tuning), 12);
            Assert.Equal(-cap, FactionSupport.DriftStep(1.0, -5.0, Tuning), 12);

            // Inside the cap the step is the tuned fraction of the gap, not the cap.
            Assert.Equal(0.1 * 0.4, FactionSupport.DriftStep(0.2, 0.6, Tuning), 12);
        }

        [Fact]
        public void Constituencies_PartitionBlocsWithoutOverlapAndSumToOne()
        {
            List<Bloc> blocs = SyntheticBlocs("district-a", "district-b");
            var factions = new List<Faction>
            {
                MakeFaction("faction-01", "party-01", 0.5, false, Flat(0.6), Issue.Services),
                MakeFaction("faction-02", "party-01", 0.3, false, Flat(-0.4), Issue.Growth),
                MakeFaction("faction-03", "party-01", 0.2, false,
                            new IssuePosition(0.9, -0.9, 0.5, 0.0, 0.2, -0.3), Issue.Transit)
            };

            List<FactionConstituency> constituencies = FactionSupport.Constituencies(factions, blocs);

            var claimed = new List<int>();
            double sum = 0.0;
            for (int i = 0; i < constituencies.Count; i++)
            {
                sum += constituencies[i].TargetShare;
                for (int b = 0; b < constituencies[i].CoreBlocs.Count; b++)
                {
                    int ordinal = constituencies[i].CoreBlocs[b].Ordinal;
                    Assert.DoesNotContain(ordinal, claimed);
                    claimed.Add(ordinal);
                }

                // CoreBlocs are contractually sorted by BlocKey.Ordinal.
                for (int b = 1; b < constituencies[i].CoreBlocs.Count; b++)
                    Assert.True(constituencies[i].CoreBlocs[b - 1].Ordinal < constituencies[i].CoreBlocs[b].Ordinal);
            }

            Assert.Equal(1.0, sum, 12);
            Assert.NotEmpty(claimed);
        }

        // ================================================================== lifecycle

        /// <summary>
        /// A party whose fourth faction sits in a corner of issue space no bloc occupies, so it wins
        /// no constituency and its support drains away.
        /// </summary>
        private static List<Faction> PartyWithADyingFaction()
        {
            return new List<Faction>
            {
                MakeFaction("faction-01", "party-01", 0.40, true, Flat(0.4), Issue.Services),
                MakeFaction("faction-02", "party-01", 0.33, false, new IssuePosition(0.1, 0.6, 0.2, 0.1, 0.0, 0.3), Issue.CostOfLiving),
                MakeFaction("faction-03", "party-01", 0.25, false, new IssuePosition(0.5, 0.1, 0.6, 0.4, 0.1, 0.0), Issue.Environment),
                MakeFaction("faction-04", "party-01", 0.02, false, Flat(-1.0), Issue.HeritageOrder)
            };
        }

        [Fact]
        public void Advance_DissolvesAFactionOnceItsSupportHasFailedTwice()
        {
            var parties = new List<Party> { MakeParty("party-01", Flat(0.3), Issue.Services) };
            List<Faction> factions = PartyWithADyingFaction();
            factions[3].ConsecutiveCyclesBelowThreshold = 1;   // one strike already recorded

            FactionCycleResult result = FactionModel.Advance(
                parties, factions, SyntheticBlocs("district-a"), SaveA, Jan1991, Tuning);

            Faction doomed = Find(result.Factions, "faction-04");
            Assert.Equal(FactionStatus.Dissolved, doomed.Status);
            Assert.True(doomed.DissolvedDate.HasValue);
            Assert.Equal(Jan1991, doomed.DissolvedDate.Value);
            Assert.Equal(0.0, doomed.InternalSupport, 12);
            Assert.Contains(result.Events, e => e.FactionId == "faction-04" && e.Kind == FactionLifecycleKind.Dissolved);
        }

        [Fact]
        public void Advance_HoldsAFailingFactionRatherThanBreachingMinPerParty()
        {
            // Two factions is the floor. Even a faction that has failed repeatedly is held as
            // Endangered rather than dissolved, because a party with one faction has no faction system.
            var parties = new List<Party> { MakeParty("party-01", Flat(0.3), Issue.Services) };
            var factions = new List<Faction>
            {
                MakeFaction("faction-01", "party-01", 0.98, true, Flat(0.4), Issue.Services),
                MakeFaction("faction-02", "party-01", 0.02, false, Flat(-1.0), Issue.HeritageOrder)
            };
            factions[1].ConsecutiveCyclesBelowThreshold = 5;

            FactionCycleResult result = FactionModel.Advance(
                parties, factions, SyntheticBlocs("district-a"), SaveA, Jan1991, Tuning);

            Faction survivor = Find(result.Factions, "faction-02");
            Assert.Equal(FactionStatus.Endangered, survivor.Status);
            Assert.Equal(2, FactionSupport.EligibleSortedById(result.Factions).Count);
        }

        [Fact]
        public void Advance_RevivesADissolvedFactionWhenItsGrievanceReturns()
        {
            var parties = new List<Party> { MakeParty("party-01", Flat(0.3), Issue.Services) };
            var factions = new List<Faction>
            {
                MakeFaction("faction-01", "party-01", 0.6, true, Flat(0.4), Issue.Services),
                MakeFaction("faction-02", "party-01", 0.4, false, new IssuePosition(0.1, 0.6, 0.2, 0.1, 0.0, 0.3), Issue.CostOfLiving),
                MakeFaction("faction-03", "party-01", 0.0, false, Flat(-0.2), Issue.Environment)
            };
            factions[2].Status = FactionStatus.Dissolved;
            factions[2].DissolvedDate = Jan1990;

            List<Bloc> blocs = SyntheticBlocs("district-a");
            IssueClimate climate = IssueClimate.FromBlocs(blocs);
            Assert.True(climate.Grievance[Issue.Environment] >= Tuning.Factions.RevivalGrievanceThreshold,
                        "fixture must be aggrieved enough for revival to be possible");

            FactionCycleResult result = FactionModel.Advance(parties, factions, blocs, SaveA, Jan1991, Tuning);

            Faction revived = Find(result.Factions, "faction-03");
            Assert.Equal(FactionStatus.Revived, revived.Status);
            Assert.Null(revived.DissolvedDate);
            Assert.True(revived.InternalSupport > 0.0);
            Assert.Contains(result.Events, e => e.FactionId == "faction-03" && e.Kind == FactionLifecycleKind.Revived);
        }

        [Fact]
        public void Advance_ProducesAFactionTakeoverWhenTheElectorateMovesUnderTheIncumbent()
        {
            // The M4b NA gate: a faction takeover must actually occur under forced conditions and
            // resolve correctly. The incumbent sits where no bloc does; the challenger sits in the
            // middle of the electorate, so support drains across and the pen changes hands.
            var parties = new List<Party> { MakeParty("party-01", Flat(0.2), Issue.Services) };
            var factions = new List<Faction>
            {
                MakeFaction("faction-01", "party-01", 0.60, true, Flat(-0.95), Issue.HeritageOrder),
                MakeFaction("faction-02", "party-01", 0.40, false, new IssuePosition(0.3, 0.4, 0.35, 0.3, 0.2, 0.3), Issue.Services)
            };

            List<Bloc> blocs = SyntheticBlocs("district-a", "district-b");
            List<Faction> state = factions;
            bool takeover = false;
            string? holder = null;

            for (int cycle = 0; cycle < 8 && !takeover; cycle++)
            {
                FactionCycleResult result = FactionModel.Advance(
                    parties, state, blocs, SaveA, new SimDate(1991 + cycle, 1, 1), Tuning);
                state = result.Factions;

                for (int i = 0; i < result.Events.Count; i++)
                    if (result.Events[i].Kind == FactionLifecycleKind.Takeover) takeover = true;

                holder = result.Dominance[0].DominantFactionId;
            }

            Assert.True(takeover, "the challenger never took the platform pen");
            Assert.Equal("faction-02", holder);
            Assert.True(Find(state, "faction-02").IsDominant);
            Assert.False(Find(state, "faction-01").IsDominant);
        }

        [Fact]
        public void Advance_ProducesIdenticalOutputTwiceAndLeavesItsInputsAlone()
        {
            var parties = new List<Party> { MakeParty("party-01", Flat(0.3), Issue.Services) };
            List<Faction> factions = PartyWithADyingFaction();
            List<Bloc> blocs = SyntheticBlocs("district-a", "district-b");

            double supportBefore = factions[0].InternalSupport;
            FactionStatus statusBefore = factions[3].Status;

            string first = HashCycle(FactionModel.Advance(parties, factions, blocs, SaveA, Jan1991, Tuning));
            string second = HashCycle(FactionModel.Advance(parties, factions, blocs, SaveA, Jan1991, Tuning));

            Assert.Equal(first, second);

            // Advance clones: running it must not move the caller's state, or a rollback would desync.
            Assert.Equal(supportBefore, factions[0].InternalSupport, 12);
            Assert.Equal(statusBefore, factions[3].Status);

            // Negative control. Without it a cycle that returned a constant would pass above.
            string otherDate = HashCycle(FactionModel.Advance(parties, factions, blocs, SaveA, new SimDate(1995, 1, 1), Tuning));
            string otherSave = HashCycle(FactionModel.Advance(parties, factions, blocs, SaveB, Jan1991, Tuning));
            Assert.NotEqual(first, otherDate);
            Assert.NotEqual(first, otherSave);
        }

        [Fact]
        public void Advance_NeverWritesAFlavorOwnedField()
        {
            var parties = new List<Party> { MakeParty("party-01", Flat(0.3), Issue.Services) };
            List<Faction> factions = PartyWithADyingFaction();
            for (int i = 0; i < factions.Count; i++) factions[i].LeaderName = "Leader " + i;

            FactionCycleResult result = FactionModel.Advance(
                parties, factions, SyntheticBlocs("district-a"), SaveA, Jan1991, Tuning);

            // A leader change is reported as an event; the name itself stays exactly as the flavor
            // layer left it (non-negotiable #1).
            for (int i = 0; i < result.Factions.Count; i++)
            {
                Faction f = result.Factions[i];
                if (FactionIds.TryParseOrdinal(f.Id, out int ordinal) && ordinal <= factions.Count)
                    Assert.Equal("Leader " + (ordinal - 1), f.LeaderName);
            }
        }

        [Fact]
        public void ApplyFactionIds_WritesSortedIdsOntoEachParty()
        {
            List<Party> parties = TwoParties();
            List<Faction> factions = FactionModel.Generate(parties, SyntheticBlocs("district-a"), SaveA, Jan1990, Tuning);

            FactionModel.ApplyFactionIds(parties, factions);

            for (int p = 0; p < parties.Count; p++)
            {
                Assert.NotEmpty(parties[p].FactionIds);
                for (int i = 1; i < parties[p].FactionIds.Count; i++)
                    Assert.True(string.CompareOrdinal(parties[p].FactionIds[i - 1], parties[p].FactionIds[i]) < 0);

                for (int i = 0; i < parties[p].FactionIds.Count; i++)
                    Assert.Equal(parties[p].Id, Find(factions, parties[p].FactionIds[i]).PartyId);
            }
        }

        // ================================================================== climate

        [Fact]
        public void Climate_GrievanceStaysInUnitRangeAndTracksDiscontent()
        {
            List<Bloc> calm = SyntheticBlocs("district-a");
            for (int i = 0; i < calm.Count; i++) calm[i].Discontent = 0.1;

            List<Bloc> angry = SyntheticBlocs("district-a");
            for (int i = 0; i < angry.Count; i++) angry[i].Discontent = 0.9;

            IssueClimate calmClimate = IssueClimate.FromBlocs(calm);
            IssueClimate angryClimate = IssueClimate.FromBlocs(angry);

            for (int n = 0; n < Issues.All.Count; n++)
            {
                Issue issue = Issues.All[n];
                Assert.InRange(calmClimate.Grievance[issue], 0.0, 1.0);
                Assert.Equal(0.1, calmClimate.Grievance[issue], 10);
                Assert.Equal(0.9, angryClimate.Grievance[issue], 10);
                Assert.True(angryClimate.Salience[issue] > calmClimate.Salience[issue]);
            }
        }

        [Fact]
        public void Climate_RanksTheIssueTheAngriestVotersCareAboutFirst()
        {
            List<Bloc> blocs = SyntheticBlocs("district-a");
            for (int i = 0; i < blocs.Count; i++)
            {
                blocs[i].Discontent = 0.2;
                blocs[i].Weights = IssueWeights.Uniform;
            }

            // One clearly aggrieved, transit-obsessed slice of the city.
            for (int i = 0; i < blocs.Count; i++)
            {
                if (blocs[i].Key.Age != AgeBand.Adult) continue;
                blocs[i].Discontent = 0.95;
                blocs[i].Weights = IssueWeights.Uniform.With(Issue.Transit, 3.0);
            }

            IssueClimate climate = IssueClimate.FromBlocs(blocs);

            Assert.Equal(Issue.Transit, climate.TopSalient());
            Assert.Equal(Issue.Transit, climate.IssuesBySalience()[0]);
        }

        [Fact]
        public void Climate_WithNoBlocsIsNeutralRatherThanNaN()
        {
            IssueClimate climate = IssueClimate.FromBlocs(null);

            Assert.False(climate.HasData);
            for (int n = 0; n < Issues.All.Count; n++)
            {
                Assert.Equal(0.0, climate.Grievance[Issues.All[n]], 12);
                Assert.Equal(0.0, climate.MeanIdeal[Issues.All[n]], 12);
            }
            Assert.Equal(Issue.Services, climate.TopSalient());
        }

        // ================================================================== registry and gates

        [Fact]
        public void Archetypes_AreTwelveAndRoundTripThroughTheirIds()
        {
            Assert.Equal(Issues.Count * 2, FactionArchetypes.All.Count);

            for (int i = 0; i < FactionArchetypes.All.Count; i++)
            {
                FactionArchetype a = FactionArchetypes.All[i];
                Assert.True(FactionArchetypes.TryGet(a.Id, out FactionArchetype back));
                Assert.Equal(a.Issue, back.Issue);
                Assert.Equal(a.Direction, back.Direction);
                Assert.Equal(a, FactionArchetypes.For(a.Issue, a.Direction));
            }

            Assert.False(FactionArchetypes.TryGet("not-an-archetype", out FactionArchetype _));
            Assert.Equal("transit-champion", FactionArchetypes.For(Issue.Transit, 1).Id);
            Assert.Equal("costOfLiving-restraint", FactionArchetypes.For(Issue.CostOfLiving, -1).Id);
        }

        [Fact]
        public void FactionIds_NeverReissueAnOrdinalThatHasBeenUsed()
        {
            var existing = new List<Faction>
            {
                MakeFaction("faction-01", "party-01", 0.5, false, Flat(0.0), Issue.Services),
                MakeFaction("faction-07", "party-01", 0.5, false, Flat(0.0), Issue.Growth)
            };
            existing[1].Status = FactionStatus.Dissolved;   // a dead brand still owns its id

            Assert.Equal(8, FactionIds.NextOrdinal(existing));
            Assert.Equal("faction-08", FactionIds.Format(8));
            Assert.Equal(1, FactionIds.NextOrdinal(new List<Faction>()));
        }

        [Fact]
        public void NaPartyLifecycle_FiresFarBelowOnePercentOfTheTimeButIsNotDead()
        {
            // §3: NA party-level lifecycle events are possible but extremely unlikely — the churn is
            // supposed to happen between factions instead.
            int fired = 0;
            const int trials = 3000;
            for (int i = 0; i < trials; i++)
            {
                DeterministicRng rng = SeedStreams.RngFor(
                    SaveA, new SimDate(1990 + (i / 12), (i % 12) + 1, 1), StreamNames.PartyLifecycle, "party-01");
                if (FactionLifecycle.NaPartyLifecycleAllowed(rng, Tuning)) fired++;
            }

            Assert.InRange(fired, 1, (int)(trials * 0.04));
        }

        [Fact]
        public void LifecycleCheck_IsDueOnlyAfterTheTunedInterval()
        {
            int interval = Tuning.Factions.LifecycleCheckIntervalMonths;

            Assert.False(FactionLifecycle.IsCheckDue(Jan1990, Jan1990.AddMonths(interval - 1), Tuning));
            Assert.True(FactionLifecycle.IsCheckDue(Jan1990, Jan1990.AddMonths(interval), Tuning));
            Assert.True(FactionLifecycle.IsCheckDue(Jan1990, Jan1990.AddMonths(interval + 5), Tuning));
        }

        [Fact]
        public void AppliesTo_IsTrueForTheNaThemeAndFalseForAPlainEuSave()
        {
            Assert.True(FactionModel.AppliesTo(new AgoraSettings { Theme = RegionTheme.Na, System = ElectoralSystem.FirstPastThePost }));
            Assert.True(FactionModel.AppliesTo(new AgoraSettings { Theme = RegionTheme.Eu, System = ElectoralSystem.FirstPastThePost }));
            Assert.False(FactionModel.AppliesTo(new AgoraSettings { Theme = RegionTheme.Eu, System = ElectoralSystem.Proportional }));
        }

        private static Faction Find(IEnumerable<Faction> factions, string id)
        {
            foreach (Faction f in factions) if (f.Id == id) return f;
            throw new InvalidOperationException("No faction " + id);
        }
    }
}
