using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Engine.Parties;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Anchored brands: the four NA catalog entries that model fixed institutions rather than
    /// generated ones.
    ///
    /// <para>
    /// Three things used to be wrong at once, and all three were invisible without looking at a
    /// generated save. The platform jitter was <c>parties.archetypeSpreadSigma</c> (0.35) for every
    /// party in both themes, which on a [-1,+1] axis flips the sign of everything except the
    /// defining one — a "conservative" party drew a positive environment stance about one run in
    /// five. The colour came from the palette by catalog index, and since the NA catalog opens
    /// liberal-then-conservative while the palette opens red-then-blue, the liberal party was
    /// assigned red and the conservative party blue. And no name was set at all, so both brands
    /// depended on whatever the flavor pipeline happened to invent that run.
    /// </para>
    ///
    /// <para>
    /// The EU catalog is deliberately left alone by all of this, and the last tests here exist to
    /// prove it: EU parties are generated brands, and freezing them into fixed institutions would be
    /// a worse bug than the one being fixed.
    /// </para>
    /// </summary>
    public class AnchoredPartyBrandTests
    {
        private static readonly SimDate Date = new SimDate(1990, 1, 1);

        /// <summary>
        /// Enough saves that a 1-in-200 event is very likely to show up at least once. The sign
        /// assertions below are absolute — one flip in this many draws fails the test — so this is
        /// the number that decides how strong "locked" actually means.
        /// </summary>
        private const int SaveCount = 200;

        /// <summary>
        /// The stance at which the lock is absolute rather than merely strong.
        ///
        /// <para>
        /// <c>anchoredSpreadSigma</c> is 0.08, so ±0.40 sits five sigma from centre and flips with
        /// probability about 3e-7 — over the 1,600-odd draws this suite makes, an expected 0.0005
        /// failures. That is what makes the assertion below a hard zero rather than a rate.
        /// </para>
        /// <para>
        /// <b>No sigma locks every axis, and none should.</b> The same 0.08 leaves ±0.20 flipping
        /// about 0.6% of the time and ±0.10 about 10%, and buying those down would need a sigma so
        /// small that every save generated the identical party — which is the thing the jitter
        /// exists to prevent. A mild lean landing mildly the other way is not a brand identity
        /// failure; conservative drawing a <i>positive environment stance</i>, which the old shared
        /// 0.35 did roughly one run in five, is. <see cref="AnchoredBrand_RarelyFlipsAMilderStance"/>
        /// is what pins that middle band.
        /// </para>
        /// </summary>
        private const double DefiningStance = 0.40;

        private static List<Party> GenerateNa(int seed) =>
            PartyRegistry.GenerateInitial(GuidFor(seed), Date, RegionTheme.Na, EngineTuning.Default);

        private static List<Party> GenerateEu(int seed) =>
            PartyRegistry.GenerateInitial(GuidFor(seed), Date, RegionTheme.Eu, EngineTuning.Default);

        /// <summary>A spread of distinct, reproducible save GUIDs. Never <c>Guid.NewGuid</c>.</summary>
        private static Guid GuidFor(int seed)
        {
            var bytes = new byte[16];
            bytes[0] = (byte)(seed & 0xFF);
            bytes[1] = (byte)((seed >> 8) & 0xFF);
            bytes[2] = (byte)((seed >> 16) & 0xFF);
            bytes[3] = (byte)((seed >> 24) & 0xFF);
            return new Guid(bytes);
        }

        private static Party ByArchetype(IReadOnlyList<Party> parties, string archetypeId)
        {
            for (int i = 0; i < parties.Count; i++)
            {
                if (string.CompareOrdinal(parties[i].ArchetypeId, archetypeId) == 0) return parties[i];
            }

            throw new InvalidOperationException("No party generated from archetype '" + archetypeId + "'.");
        }

        private static PartyArchetype ArchetypeOf(string id)
        {
            PartyArchetype? found = PartyArchetypes.Find(PartyArchetypes.Na, id);
            Assert.NotNull(found);
            return found!;
        }

        // --- the stance lock ------------------------------------------------------------------------

        [Theory]
        [InlineData("liberal")]
        [InlineData("conservative")]
        [InlineData("green")]
        [InlineData("populist")]
        public void AnchoredBrand_NeverAbandonsADefiningStance(string archetypeId)
        {
            PartyArchetype archetype = ArchetypeOf(archetypeId);
            var failures = new List<string>();

            for (int seed = 0; seed < SaveCount; seed++)
            {
                Party party = ByArchetype(GenerateNa(seed), archetypeId);

                for (int i = 0; i < Issues.All.Count; i++)
                {
                    Issue issue = Issues.All[i];
                    double baseline = archetype.BasePlatform[issue];
                    if (Math.Abs(baseline) < DefiningStance) continue;

                    double actual = party.Platform[issue];
                    if (Math.Sign(actual) == Math.Sign(baseline)) continue;

                    failures.Add(archetypeId + "." + Issues.ToKey(issue) + " went " +
                                 baseline.ToString("0.00") + " -> " + actual.ToString("0.00") +
                                 " on save " + seed);
                }
            }

            Assert.True(failures.Count == 0,
                        "An anchored brand changed what it stands for at generation:" +
                        Environment.NewLine + string.Join(Environment.NewLine, failures));
        }

        [Fact]
        public void AnchoredBrand_RarelyFlipsAMilderStance()
        {
            // The band between "centrist enough not to matter" and "defining". At 0.08 the true rate
            // here is about 0.6%; under the old shared 0.35 it was 20-30%, so this is the assertion
            // that would actually have caught the original bug. Stated as a rate rather than a hard
            // zero because 0.6% over this many draws is not reliably zero — asserting zero would be
            // asserting luck.
            const double maxFlipRate = 0.03;

            int considered = 0;
            int flipped = 0;

            for (int seed = 0; seed < SaveCount; seed++)
            {
                List<Party> parties = GenerateNa(seed);

                for (int p = 0; p < parties.Count; p++)
                {
                    PartyArchetype archetype = ArchetypeOf(parties[p].ArchetypeId);

                    for (int i = 0; i < Issues.All.Count; i++)
                    {
                        Issue issue = Issues.All[i];
                        double baseline = archetype.BasePlatform[issue];
                        double magnitude = Math.Abs(baseline);
                        if (magnitude < 0.20 || magnitude >= DefiningStance) continue;

                        considered++;
                        if (Math.Sign(parties[p].Platform[issue]) != Math.Sign(baseline)) flipped++;
                    }
                }
            }

            Assert.True(considered > 0, "The catalog has no stance in the 0.20-0.40 band to measure.");

            double rate = flipped / (double)considered;
            Assert.True(rate <= maxFlipRate,
                        "Mild stances flipped " + flipped + "/" + considered + " (" +
                        (rate * 100).ToString("0.0") + "%), over the " + (maxFlipRate * 100) +
                        "% bound. anchoredSpreadSigma is too loose to hold a brand together.");
        }

        [Fact]
        public void AnchoredBrand_StaysNearItsArchetypeOnEveryAxis()
        {
            // Six sigma at anchoredSpreadSigma = 0.08. A breach here means something other than the
            // jitter moved the platform — in practice PartyPlatform.SeparateFrom, which fires when two
            // generated platforms land closer than parties.minPlatformDistance and would silently
            // undo the lock by shoving a brand off its own stance.
            const double tolerance = 0.48;
            var failures = new List<string>();

            for (int seed = 0; seed < SaveCount; seed++)
            {
                List<Party> parties = GenerateNa(seed);

                for (int p = 0; p < parties.Count; p++)
                {
                    PartyArchetype archetype = ArchetypeOf(parties[p].ArchetypeId);

                    for (int i = 0; i < Issues.All.Count; i++)
                    {
                        Issue issue = Issues.All[i];
                        double drift = Math.Abs(parties[p].Platform[issue] - archetype.BasePlatform[issue]);
                        if (drift <= tolerance) continue;

                        failures.Add(archetype.Id + "." + Issues.ToKey(issue) + " drifted " +
                                     drift.ToString("0.00") + " on save " + seed);
                    }
                }
            }

            Assert.True(failures.Count == 0,
                        "An anchored platform moved further than the jitter can explain:" +
                        Environment.NewLine + string.Join(Environment.NewLine, failures));
        }

        // --- identity -------------------------------------------------------------------------------

        [Theory]
        [InlineData("liberal", "Democratic Party", "Dem", "#2E86C1")]
        [InlineData("conservative", "Republican Party", "GOP", "#C0392B")]
        [InlineData("green", "Green Party", "Grn", "#27AE60")]
        [InlineData("populist", "Reform Party", "Ref", "#F1C40F")]
        public void AnchoredBrand_CarriesItsOwnNameAndColour(string archetypeId, string name,
                                                             string shortName, string colorHex)
        {
            for (int seed = 0; seed < 25; seed++)
            {
                Party party = ByArchetype(GenerateNa(seed), archetypeId);

                Assert.Equal(name, party.Name);
                Assert.Equal(shortName, party.ShortName);
                Assert.Equal(colorHex, PartyIdentity.NormalizeHex(party.ColorHex));
            }
        }

        [Fact]
        public void AnchoredBrand_DoesNotTakeTheColourOfTheOppositeBrand()
        {
            // The specific inversion this change fixes, pinned on its own so a future edit to either
            // the catalog order or the palette order cannot quietly restore it.
            List<Party> parties = GenerateNa(0);

            string liberal = PartyIdentity.NormalizeHex(ByArchetype(parties, "liberal").ColorHex);
            string conservative = PartyIdentity.NormalizeHex(ByArchetype(parties, "conservative").ColorHex);

            Assert.NotEqual("#C0392B", liberal);
            Assert.NotEqual("#2E86C1", conservative);
        }

        [Fact]
        public void AnchoredBrand_IdentityFitsTheSidecarSchema()
        {
            // A short name over ShortNameMax makes political_state.json fail the schema it ships
            // with, which is a load failure rather than a cosmetic one.
            IReadOnlyList<PartyArchetype> catalog = PartyArchetypes.Na;

            for (int i = 0; i < catalog.Count; i++)
            {
                PartyArchetype archetype = catalog[i];

                Assert.True(archetype.Name.Length <= PartyIdentity.NameMax, archetype.Id + " name too long");
                Assert.True(archetype.ShortName.Length <= PartyIdentity.ShortNameMax,
                            archetype.Id + " short name too long");
                Assert.Equal(CommandOutcome.Ok, PartyIdentity.ValidateColor(archetype.ColorHex));
            }
        }

        [Fact]
        public void NaBallot_HasNoTwoPartiesWearingTheSameColour()
        {
            List<Party> parties = GenerateNa(0);

            for (int i = 0; i < parties.Count; i++)
            {
                for (int j = i + 1; j < parties.Count; j++)
                {
                    Assert.NotEqual(PartyIdentity.NormalizeHex(parties[i].ColorHex),
                                    PartyIdentity.NormalizeHex(parties[j].ColorHex));
                }
            }
        }

        // --- what must not have changed -------------------------------------------------------------

        [Fact]
        public void NaArchetypeIds_AreUnchanged_SoMajorReconstructionStillWorks()
        {
            // NaMajorParties.Reconstruct keys off Party.ArchetypeId, and so does the sidecar
            // migration. Giving the NA catalog its own instances must not have given it its own ids:
            // if it had, every existing NA save would reconstruct zero majors on load, and
            // FringeFailureModel.Ceilings would then pin the entire ballot at baseCeiling.
            List<string> majors = NaMajorParties.DefaultMajorArchetypeIds(2);

            Assert.Equal(new[] { "liberal", "conservative" }, majors);

            List<Party> parties = GenerateNa(0);
            Assert.True(ByArchetype(parties, "liberal").IsMajor);
            Assert.True(ByArchetype(parties, "conservative").IsMajor);
            Assert.False(ByArchetype(parties, "green").IsMajor);
            Assert.False(ByArchetype(parties, "populist").IsMajor);
        }

        [Fact]
        public void NaAndEuCatalogs_AgreeOnStanceForTheSharedArchetypes()
        {
            // The NA entries take their politics from the EU ones by construction. If they drift
            // apart, the two themes quietly stop modelling the same archetype under the same id.
            string[] shared = { "liberal", "conservative", "green", "populist" };

            for (int i = 0; i < shared.Length; i++)
            {
                PartyArchetype na = ArchetypeOf(shared[i]);
                PartyArchetype? eu = PartyArchetypes.Find(PartyArchetypes.Eu, shared[i]);

                Assert.NotNull(eu);
                Assert.Equal(eu!.CoreGrievance, na.CoreGrievance);

                for (int n = 0; n < Issues.All.Count; n++)
                {
                    Issue issue = Issues.All[n];
                    Assert.Equal(eu.BasePlatform[issue], na.BasePlatform[issue], 10);
                }
            }
        }

        [Fact]
        public void EuParties_AreStillGeneratedBrands()
        {
            // Unanchored: no catalog name, no catalog colour, and the loose sigma. The flavor
            // pipeline names them, and AgoraRuntime.ApplyProseNames only renames a party whose name
            // is empty — so a name set here would lock every EU party out of being named at all.
            IReadOnlyList<PartyArchetype> catalog = PartyArchetypes.Eu;
            for (int i = 0; i < catalog.Count; i++)
            {
                Assert.False(catalog[i].IsAnchored, catalog[i].Id + " must not be anchored in the EU catalog");
                Assert.Equal("", catalog[i].Name);
                Assert.Equal("", catalog[i].ColorHex);
            }

            List<Party> parties = GenerateEu(0);
            for (int i = 0; i < parties.Count; i++)
            {
                Assert.Equal("", parties[i].Name);
                Assert.Equal("", parties[i].ShortName);
            }
        }

        [Fact]
        public void EuParties_StillSpreadWidelyAcrossSaves()
        {
            // The regression guard for the lock leaking into the EU theme. With sigma 0.35 an EU
            // conservative's environment stance (-0.30) flips sign about a fifth of the time; with
            // the anchored 0.08 it would essentially never flip. Asserting that it DOES vary is what
            // catches an accidental global tightening — a test that only checked the NA side would
            // pass just as happily with every party in the game frozen.
            int flips = 0;

            for (int seed = 0; seed < SaveCount; seed++)
            {
                Party party = ByArchetype(GenerateEu(seed), "conservative");
                if (party.Platform[Issue.Environment] > 0.0) flips++;
            }

            Assert.True(flips > 0,
                        "No EU conservative in " + SaveCount + " saves drew a positive environment " +
                        "stance. At archetypeSpreadSigma = 0.35 that should happen in roughly a " +
                        "fifth of them, so the anchored sigma has leaked into the EU catalog.");
        }

        // --- determinism ----------------------------------------------------------------------------

        [Fact]
        public void AnchoredGeneration_IsDeterministic()
        {
            List<Party> first = GenerateNa(7);
            List<Party> second = GenerateNa(7);

            Assert.Equal(first.Count, second.Count);

            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i].Id, second[i].Id);
                Assert.Equal(first[i].Name, second[i].Name);
                Assert.Equal(first[i].ColorHex, second[i].ColorHex);

                for (int n = 0; n < Issues.All.Count; n++)
                {
                    Issue issue = Issues.All[n];
                    Assert.Equal(first[i].Platform[issue], second[i].Platform[issue], 12);
                }
            }
        }
    }
}
