using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Engine.Parties;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The load-time repair that carries the anchored-brand fix onto saves that already exist.
    ///
    /// <para>
    /// Anchoring happens in <c>PartyRegistry.GenerateInitial</c>, which runs once per save. Without
    /// this repair the whole change would only ever reach cities founded after it shipped, and an
    /// existing North American save would keep a red liberal party and a blue conservative one
    /// forever, because nothing else in the engine writes a party's identity.
    /// </para>
    /// </summary>
    public class AnchoredBrandRepairTests
    {
        private static EngineTuning Tuning => EngineTuning.Default;

        /// <summary>
        /// A registry as a pre-anchoring save held it: colours allocated by catalog index, so the
        /// liberal party wears the palette's first entry (red) and the conservative party its second
        /// (blue). This is the exact inversion the fix undoes.
        /// </summary>
        private static List<Party> LegacyNaRegistry() => new List<Party>
        {
            new Party { Id = "party-01", ArchetypeId = "liberal",      Name = "Freedom Union",  ShortName = "FU",  ColorHex = "#C0392B" },
            new Party { Id = "party-02", ArchetypeId = "conservative", Name = "Order Alliance", ShortName = "OA",  ColorHex = "#2E86C1" },
            new Party { Id = "party-03", ArchetypeId = "green",        Name = "Leaf",           ShortName = "Lf",  ColorHex = "#27AE60" },
            new Party { Id = "party-04", ArchetypeId = "populist",     Name = "The People",     ShortName = "TP",  ColorHex = "#F1C40F" }
        };

        private static Party ById(IEnumerable<Party> parties, string id)
        {
            foreach (Party p in parties)
            {
                if (p.Id == id) return p;
            }
            return null;
        }

        [Fact]
        public void SwapsTheInvertedColours()
        {
            List<Party> parties = LegacyNaRegistry();

            BrandRepairResult result = AnchoredBrandRepair.Apply(parties, PartyArchetypes.Na, Tuning);

            Assert.Equal("#2E86C1", ById(parties, "party-01").ColorHex);   // liberal -> blue
            Assert.Equal("#C0392B", ById(parties, "party-02").ColorHex);   // conservative -> red
            Assert.Contains("party-01", result.Recoloured);
            Assert.Contains("party-02", result.Recoloured);
        }

        [Fact]
        public void GivesTheBrandsTheirNames()
        {
            List<Party> parties = LegacyNaRegistry();

            AnchoredBrandRepair.Apply(parties, PartyArchetypes.Na, Tuning);

            Assert.Equal("Democratic Party", ById(parties, "party-01").Name);
            Assert.Equal("Dem", ById(parties, "party-01").ShortName);
            Assert.Equal("Republican Party", ById(parties, "party-02").Name);
            Assert.Equal("GOP", ById(parties, "party-02").ShortName);
        }

        [Fact]
        public void LeavesNoTwoPartiesSharingAColour()
        {
            List<Party> parties = LegacyNaRegistry();

            AnchoredBrandRepair.Apply(parties, PartyArchetypes.Na, Tuning);

            var seen = new HashSet<string>();
            foreach (Party p in parties)
            {
                Assert.True(seen.Add(PartyIdentity.NormalizeHex(p.ColorHex)),
                            "two parties ended the repair wearing " + p.ColorHex);
            }
        }

        [Fact]
        public void MovesAnUnanchoredPartyOffAReclaimedColour()
        {
            // A splinter sitting on the colour an anchored brand is about to reclaim. It has to move,
            // or the repair trades an inverted palette for a duplicated one.
            List<Party> parties = LegacyNaRegistry();
            parties.Add(new Party
            {
                Id = "party-05",
                ArchetypeId = "liberal",
                PredecessorPartyId = "party-01",
                Name = "Freedom Splinter",
                ColorHex = "#2E86C1"
            });

            BrandRepairResult result = AnchoredBrandRepair.Apply(parties, PartyArchetypes.Na, Tuning);

            Assert.NotEqual("#2E86C1", PartyIdentity.NormalizeHex(ById(parties, "party-05").ColorHex));
            Assert.Contains("party-05", result.Displaced);
        }

        [Fact]
        public void LeavesASplintersIdentityAlone()
        {
            // A splinter copies its parent's archetype id so the flavor prompt keeps working. It is
            // not the institution, and must not be handed the institution's name.
            List<Party> parties = LegacyNaRegistry();
            parties.Add(new Party
            {
                Id = "party-05",
                ArchetypeId = "conservative",
                PredecessorPartyId = "party-02",
                Name = "Reform Conservatives",
                ColorHex = "#8E44AD"
            });

            AnchoredBrandRepair.Apply(parties, PartyArchetypes.Na, Tuning);

            Assert.Equal("Reform Conservatives", ById(parties, "party-05").Name);
        }

        [Fact]
        public void RespectsAPlayersRename()
        {
            List<Party> parties = LegacyNaRegistry();
            ById(parties, "party-01").Name = "My Party";
            ById(parties, "party-01").PlayerOverrides = PartyOverrides.NameLocked;

            BrandRepairResult result = AnchoredBrandRepair.Apply(parties, PartyArchetypes.Na, Tuning);

            Assert.Equal("My Party", ById(parties, "party-01").Name);
            Assert.DoesNotContain("party-01", result.Renamed);
            // The colour is a separate lock and still moves.
            Assert.Equal("#2E86C1", ById(parties, "party-01").ColorHex);
        }

        [Fact]
        public void RespectsAPlayersColour()
        {
            List<Party> parties = LegacyNaRegistry();
            ById(parties, "party-02").ColorHex = "#123456";
            ById(parties, "party-02").PlayerOverrides = PartyOverrides.ColorLocked;

            AnchoredBrandRepair.Apply(parties, PartyArchetypes.Na, Tuning);

            Assert.Equal("#123456", ById(parties, "party-02").ColorHex);
            Assert.Equal("Republican Party", ById(parties, "party-02").Name);
        }

        [Fact]
        public void NeverTouchesAPlatform()
        {
            // The load-bearing limitation. A platform is the record of how a party has governed, and
            // the blocs' PreviousVote entries were taken against it.
            List<Party> parties = LegacyNaRegistry();
            var stance = new IssuePosition(0.9, -0.4, 0.1, 0.2, -0.7, 0.3);
            ById(parties, "party-01").Platform = stance;
            ById(parties, "party-01").LastManifesto = stance;

            AnchoredBrandRepair.Apply(parties, PartyArchetypes.Na, Tuning);

            for (int i = 0; i < Issues.All.Count; i++)
            {
                Issue issue = Issues.All[i];
                Assert.Equal(stance[issue], ById(parties, "party-01").Platform[issue], 12);
                Assert.Equal(stance[issue], ById(parties, "party-01").LastManifesto[issue], 12);
            }
        }

        [Fact]
        public void IsIdempotent()
        {
            List<Party> parties = LegacyNaRegistry();

            AnchoredBrandRepair.Apply(parties, PartyArchetypes.Na, Tuning);
            BrandRepairResult second = AnchoredBrandRepair.Apply(parties, PartyArchetypes.Na, Tuning);

            Assert.False(second.Changed, "a repaired save must not be repaired again on the next load");
        }

        [Fact]
        public void AFreshlyGeneratedRegistryNeedsNoRepair()
        {
            List<Party> parties = PartyRegistry.GenerateInitial(
                new System.Guid(new byte[16]), new SimDate(1990, 1, 1), RegionTheme.Na, Tuning);

            BrandRepairResult result = AnchoredBrandRepair.Apply(parties, PartyArchetypes.Na, Tuning);

            Assert.False(result.Changed,
                         "GenerateInitial and AnchoredBrandRepair disagree about what an anchored " +
                         "brand looks like: " + result.Summary);
        }

        [Fact]
        public void DoesNothingOnAnEuSave()
        {
            // The EU catalog anchors nothing, so EU parties keep their generated names and their
            // palette-order colours. Freezing them into fixed institutions would be a worse bug than
            // the one being fixed.
            var parties = new List<Party>
            {
                new Party { Id = "party-01", ArchetypeId = "green",  Name = "Verdant", ColorHex = "#C0392B" },
                new Party { Id = "party-02", ArchetypeId = "labour", Name = "Toil",    ColorHex = "#2E86C1" }
            };

            BrandRepairResult result = AnchoredBrandRepair.Apply(parties, PartyArchetypes.Eu, Tuning);

            Assert.False(result.Changed);
            Assert.Equal("Verdant", ById(parties, "party-01").Name);
            Assert.Equal("#C0392B", ById(parties, "party-01").ColorHex);
        }
    }
}
