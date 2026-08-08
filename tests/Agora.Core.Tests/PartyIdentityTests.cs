using System;
using System.Collections.Generic;
using System.IO;
using Agora.Core.Contracts;
using Agora.Core.Engine.Parties;
using Agora.Core.Tuning;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// W4 packet 1 — the identity rules a player edit and a flavor wake both have to obey.
    ///
    /// <para>
    /// Two of these are regression guards for live defects rather than descriptions of the code:
    /// the description/slogan lock (flavor used to rewrite both on every generation, so a
    /// player-written description was gone at the next wake) and the case-insensitive colour
    /// comparison (a lowercase player colour did not register as taken, so the next splinter was
    /// handed the same one). Both are called out on the tests themselves.
    /// </para>
    /// </summary>
    public class PartyIdentityTests
    {
        private static readonly SimDate Y1990 = new SimDate(1990, 1, 1);
        private static readonly Guid SaveA = new Guid("11111111-2222-3333-4444-555555555555");

        // ============================ ApplyFlavor: the locks ======================================

        [Fact]
        public void ApplyFlavor_WithNoLocks_WritesAllFourFieldsAndReportsTheNameWrite()
        {
            Party party = Named("party-01", PartyOverrides.None);

            PartyIdentity.ApplyFlavor(party, Prose(), true, out bool wroteName);

            Assert.True(wroteName);
            Assert.Equal("Prose Name", party.Name);
            Assert.Equal("Prose", party.ShortName);
            Assert.Equal("Prose description.", party.Description);
            Assert.Equal("Prose slogan.", party.Slogan);
            Assert.Equal("#C0392B", party.ColorHex);   // prose is prose; colour is not the flavor's to write
        }

        /// <summary>
        /// The name lock must not reach the description. A player who renamed a party still wants its
        /// blurb to move with the politics — that is why the two flags exist separately at all.
        /// </summary>
        [Fact]
        public void ApplyFlavor_WithOnlyTheNameLocked_LeavesTheNameButStillWritesTheDescription()
        {
            Party party = Named("party-01", PartyOverrides.NameLocked);

            PartyIdentity.ApplyFlavor(party, Prose(), true, out bool wroteName);

            Assert.False(wroteName);
            Assert.Equal("Player Name", party.Name);
            Assert.Equal("PLR", party.ShortName);
            Assert.Equal("Prose description.", party.Description);
            Assert.Equal("Prose slogan.", party.Slogan);
        }

        /// <summary>
        /// The other direction, and the one that did not work before W4: <c>ApplyProseNames</c> wrote
        /// description and slogan unconditionally, so a player-written description survived exactly
        /// until the next successful generation.
        /// </summary>
        [Fact]
        public void ApplyFlavor_WithOnlyTheDescriptionLocked_LeavesTheProseButStillWritesTheName()
        {
            Party party = Named("party-01", PartyOverrides.DescriptionLocked);

            PartyIdentity.ApplyFlavor(party, Prose(), true, out bool wroteName);

            Assert.True(wroteName);
            Assert.Equal("Prose Name", party.Name);
            Assert.Equal("Prose", party.ShortName);
            Assert.Equal("Player description.", party.Description);
            Assert.Equal("Player slogan.", party.Slogan);
        }

        [Fact]
        public void ApplyFlavor_WithBothLocked_WritesNothingAtAll()
        {
            Party party = Named("party-01", PartyOverrides.NameLocked | PartyOverrides.DescriptionLocked);

            PartyIdentity.ApplyFlavor(party, Prose(), true, out bool wroteName);

            Assert.False(wroteName);
            Assert.Equal("Player Name", party.Name);
            Assert.Equal("PLR", party.ShortName);
            Assert.Equal("Player description.", party.Description);
            Assert.Equal("Player slogan.", party.Slogan);
        }

        /// <summary>
        /// <c>mayRename: false</c> is the flavor pipeline's own rule — a settled name is not
        /// re-christened by a fresh document — and it has never stopped the blurb from moving. That
        /// is today's behaviour in <c>AgoraRuntime</c> and the lock work must not change it.
        /// </summary>
        [Fact]
        public void ApplyFlavor_WithoutRenamePermission_StillWritesDescriptionAndSlogan()
        {
            Party party = Named("party-01", PartyOverrides.None);

            PartyIdentity.ApplyFlavor(party, Prose(), false, out bool wroteName);

            Assert.False(wroteName);
            Assert.Equal("Player Name", party.Name);
            Assert.Equal("PLR", party.ShortName);
            Assert.Equal("Prose description.", party.Description);
            Assert.Equal("Prose slogan.", party.Slogan);
        }

        [Fact]
        public void ApplyFlavor_EmptyFlavorFieldsNeverBlankAnExistingValue()
        {
            Party party = Named("party-01", PartyOverrides.None);

            // A document that named the party and said nothing else: three empty strings that must
            // not become three cleared fields.
            var sparse = new PartyFlavor { PartyId = "party-01", Name = "Prose Name" };

            PartyIdentity.ApplyFlavor(party, sparse, true, out bool wroteName);

            Assert.True(wroteName);
            Assert.Equal("Prose Name", party.Name);
            Assert.Equal("PLR", party.ShortName);
            Assert.Equal("Player description.", party.Description);
            Assert.Equal("Player slogan.", party.Slogan);
        }

        [Fact]
        public void ApplyFlavor_WithAnEmptyName_WritesNeitherNameNorShortName()
        {
            Party party = Named("party-01", PartyOverrides.None);
            var noName = new PartyFlavor { PartyId = "party-01", ShortName = "Prose", Slogan = "Prose slogan." };

            PartyIdentity.ApplyFlavor(party, noName, true, out bool wroteName);

            Assert.False(wroteName);
            Assert.Equal("Player Name", party.Name);
            Assert.Equal("PLR", party.ShortName);      // the short name rides with the name, not alone
            Assert.Equal("Prose slogan.", party.Slogan);
        }

        [Fact]
        public void ApplyFlavor_NullArgumentsAreANoOp()
        {
            Party party = Named("party-01", PartyOverrides.None);

            PartyIdentity.ApplyFlavor(party, null!, true, out bool wroteFlavor);
            PartyIdentity.ApplyFlavor(null!, Prose(), true, out bool wroteParty);

            Assert.False(wroteFlavor);
            Assert.False(wroteParty);
            Assert.Equal("Player Name", party.Name);
        }

        // ============================ Validation ==================================================

        [Fact]
        public void ValidateName_AcceptsExactlyTheLimitAndRejectsOneOver()
        {
            Assert.Equal(CommandOutcome.Ok,
                         PartyIdentity.ValidateName(Repeat(PartyIdentity.NameMax), Repeat(PartyIdentity.ShortNameMax)));
            Assert.Equal(CommandOutcome.TooLong,
                         PartyIdentity.ValidateName(Repeat(PartyIdentity.NameMax + 1), "Short"));
            Assert.Equal(CommandOutcome.TooLong,
                         PartyIdentity.ValidateName("A Name", Repeat(PartyIdentity.ShortNameMax + 1)));
        }

        [Fact]
        public void ValidateName_RejectsEmptyAndWhitespaceOnlyInEitherField()
        {
            Assert.Equal(CommandOutcome.ValueRequired, PartyIdentity.ValidateName("", "Short"));
            Assert.Equal(CommandOutcome.ValueRequired, PartyIdentity.ValidateName("   ", "Short"));
            Assert.Equal(CommandOutcome.ValueRequired, PartyIdentity.ValidateName("A Name", ""));
            Assert.Equal(CommandOutcome.ValueRequired, PartyIdentity.ValidateName("A Name", " \t "));
            Assert.Equal(CommandOutcome.ValueRequired, PartyIdentity.ValidateName(null!, null!));
        }

        [Fact]
        public void ValidateDescription_AcceptsExactlyTheLimitAndRejectsOneOver()
        {
            Assert.Equal(CommandOutcome.Ok,
                         PartyIdentity.ValidateDescription(Repeat(PartyIdentity.DescriptionMax),
                                                           Repeat(PartyIdentity.SloganMax)));
            Assert.Equal(CommandOutcome.TooLong,
                         PartyIdentity.ValidateDescription(Repeat(PartyIdentity.DescriptionMax + 1), "Onward"));
            Assert.Equal(CommandOutcome.TooLong,
                         PartyIdentity.ValidateDescription("A blurb.", Repeat(PartyIdentity.SloganMax + 1)));
        }

        [Fact]
        public void ValidateDescription_RejectsEmptyAndWhitespaceOnlyInEitherField()
        {
            Assert.Equal(CommandOutcome.ValueRequired, PartyIdentity.ValidateDescription("", "Onward"));
            Assert.Equal(CommandOutcome.ValueRequired, PartyIdentity.ValidateDescription("   ", "Onward"));
            Assert.Equal(CommandOutcome.ValueRequired, PartyIdentity.ValidateDescription("A blurb.", ""));
            Assert.Equal(CommandOutcome.ValueRequired, PartyIdentity.ValidateDescription("A blurb.", "\n"));
        }

        /// <summary>
        /// A validator that quietly trims or truncates is a write the player did not ask for, and the
        /// one time it matters — a name cut off mid-word — they have no way to tell it happened.
        ///
        /// <para>
        /// Asserting "the string came back unchanged" would prove nothing (they are immutable), so
        /// this asserts the observable consequence instead: the verdict is reached on the <i>raw</i>
        /// input. A value that is only within the limit once trimmed is still <c>TooLong</c>, and a
        /// colour that is only valid once trimmed is still <c>BadValue</c> — no path here quietly
        /// trims a value into shape and then accepts it.
        /// </para>
        /// </summary>
        [Fact]
        public void Validators_JudgeTheRawInputRatherThanTrimmingItIntoShape()
        {
            // Length NameMax + 2, but only NameMax characters once the padding is gone.
            string paddedName = " " + Repeat(PartyIdentity.NameMax) + " ";
            Assert.Equal(CommandOutcome.TooLong, PartyIdentity.ValidateName(paddedName, "Short"));

            string paddedShort = " " + Repeat(PartyIdentity.ShortNameMax) + " ";
            Assert.Equal(CommandOutcome.TooLong, PartyIdentity.ValidateName("A Name", paddedShort));

            string paddedBlurb = " " + Repeat(PartyIdentity.DescriptionMax) + " ";
            Assert.Equal(CommandOutcome.TooLong, PartyIdentity.ValidateDescription(paddedBlurb, "Onward"));

            string paddedSlogan = " " + Repeat(PartyIdentity.SloganMax) + " ";
            Assert.Equal(CommandOutcome.TooLong, PartyIdentity.ValidateDescription("A blurb.", paddedSlogan));

            // NormalizeHex exists and would make this pass; ValidateColor deliberately does not call
            // it, because the value the caller stores must be the value the caller was judged on.
            Assert.Equal(CommandOutcome.BadValue, PartyIdentity.ValidateColor(" #c0392b "));
        }

        [Theory]
        [InlineData("#C0392B", CommandOutcome.Ok)]
        [InlineData("#c0392b", CommandOutcome.Ok)]          // the schema pattern accepts either case
        [InlineData("#Ff00aA", CommandOutcome.Ok)]
        [InlineData("C0392B", CommandOutcome.BadValue)]     // no hash
        [InlineData("#GGGGGG", CommandOutcome.BadValue)]    // not hex
        [InlineData("#C0392BB", CommandOutcome.BadValue)]   // seven digits
        [InlineData("#C039", CommandOutcome.BadValue)]      // three digits, the CSS shorthand
        [InlineData("", CommandOutcome.ValueRequired)]
        [InlineData("   ", CommandOutcome.ValueRequired)]
        public void ValidateColor_AcceptsOnlyASixDigitHashedHex(string input, CommandOutcome expected)
        {
            Assert.Equal(expected, PartyIdentity.ValidateColor(input));
        }

        /// <summary>
        /// Duplicate detection is not a validity question — the whole roster decides it, and the
        /// answer is an acceptance with a warning. <see cref="PartyIdentity.ValidateColor"/> must stay
        /// out of it or the two would disagree about what "invalid" means.
        /// </summary>
        [Fact]
        public void ValidateColor_SaysNothingAboutWhetherAnotherPartyHoldsTheColour()
        {
            Assert.Equal(CommandOutcome.Ok, PartyIdentity.ValidateColor(EngineTuning.Default.Parties.ColorPalette[0]));
        }

        [Fact]
        public void NormalizeHex_TrimsAndUpperCases()
        {
            Assert.Equal("#C0392B", PartyIdentity.NormalizeHex(" #c0392b "));
            Assert.Equal("#C0392B", PartyIdentity.NormalizeHex("#C0392B"));
            Assert.Equal("", PartyIdentity.NormalizeHex(null!));
            Assert.Equal("", PartyIdentity.NormalizeHex("   "));
        }

        // ============================ Colour ownership ============================================

        /// <summary>
        /// The regression guard. <b>This assertion fails against the pre-W4 code</b>: the old private
        /// check compared raw <c>string.CompareOrdinal</c>, so a player's <c>#c0392b</c> was
        /// byte-different from the palette's <c>#C0392B</c>, did not register as taken, and the next
        /// splinter was handed a colour indistinguishable from theirs on every chart.
        /// </summary>
        [Fact]
        public void IsColorTaken_SeesAColourAPlayerTypedInLowerCase()
        {
            var roster = new List<Party>
            {
                MakeParty("party-01", "#c0392b"),
                MakeParty("party-02", "#2E86C1")
            };

            Assert.True(PartyRegistry.IsColorTaken(roster, "#C0392B", null));
            Assert.True(PartyRegistry.IsColorTaken(roster, " #c0392b ", null));
            Assert.False(PartyRegistry.IsColorTaken(roster, "#27AE60", null));
        }

        [Fact]
        public void IsColorTaken_SkipsTheExcludedParty()
        {
            var roster = new List<Party>
            {
                MakeParty("party-01", "#C0392B"),
                MakeParty("party-02", "#2E86C1")
            };

            Assert.False(PartyRegistry.IsColorTaken(roster, "#C0392B", "party-01"));
            Assert.True(PartyRegistry.IsColorTaken(roster, "#C0392B", "party-02"));
            Assert.True(PartyRegistry.IsColorTaken(roster, "#C0392B", ""));      // excludes nothing
        }

        /// <summary>
        /// A colour is held for the life of the brand, dissolved brands included, so a revived party
        /// comes back the colour the player remembers. Handing it away in the meantime is what would
        /// break that promise.
        /// </summary>
        [Fact]
        public void IsColorTaken_CountsDissolvedAndMergedBrands()
        {
            var roster = new List<Party>
            {
                MakeParty("party-01", "#C0392B"),
                MakeParty("party-02", "#2E86C1"),
                MakeParty("party-03", "#27AE60")
            };
            roster[1].Status = PartyStatus.Dissolved;
            roster[2].Status = PartyStatus.Merged;

            Assert.True(PartyRegistry.IsColorTaken(roster, "#2E86C1", null));
            Assert.True(PartyRegistry.IsColorTaken(roster, "#27AE60", null));
        }

        [Fact]
        public void OrdinalOf_ReadsTheSuffixAndReturnsZeroForAnythingElse()
        {
            Assert.Equal(3, PartyRegistry.OrdinalOf("party-03"));
            Assert.Equal(11, PartyRegistry.OrdinalOf("party-11"));
            Assert.Equal(0, PartyRegistry.OrdinalOf("faction-03"));
            Assert.Equal(0, PartyRegistry.OrdinalOf("party-xx"));
            Assert.Equal(0, PartyRegistry.OrdinalOf(""));
        }

        /// <summary>
        /// The self-exclusion guard, and the only test that exercises it. The party is left holding
        /// the palette colour its ordinal drew at launch — the state a player is in when they hit
        /// "reset colour" on a party they never recoloured, and the state the exclusion exists for.
        ///
        /// <para>
        /// Without <c>excludingPartyId</c>, the allocation scan sees the party's <i>own</i> colour
        /// sitting in its preferred slot, reads that slot as taken, and walks on to the next free
        /// one — so a reset that should be a no-op hands back a colour the party did not have. Every
        /// other <c>RegenerateColor</c> test overwrites <c>ColorHex</c> first, which vacates the slot
        /// and makes the exclusion unobservable; drop the fourth argument at the
        /// <c>AllocateColor</c> call in <c>RegenerateColor</c> and this is the assertion that fails.
        /// </para>
        /// </summary>
        [Fact]
        public void RegenerateColor_IsANoOpForAPartyStillWearingItsOwnPaletteColour()
        {
            EngineTuning tuning = EngineTuning.Default;
            List<Party> parties = PartyRegistry.GenerateInitial(SaveA, Y1990, RegionTheme.Eu, tuning);

            // Untouched: party-03 still holds whatever its ordinal drew.
            Party party = PartyRegistry.Find(parties, "party-03")!;

            Assert.Equal(party.ColorHex, PartyRegistry.RegenerateColor(parties, "party-03", tuning));
        }

        [Fact]
        public void RegenerateColor_ReturnsThePartyToTheColourItsOrdinalOriginallyDrew()
        {
            EngineTuning tuning = EngineTuning.Default;
            List<Party> parties = PartyRegistry.GenerateInitial(SaveA, Y1990, RegionTheme.Eu, tuning);

            Party party = PartyRegistry.Find(parties, "party-03")!;
            string original = party.ColorHex;
            party.ColorHex = "#123456";                       // the player's own choice

            Assert.Equal(original, PartyRegistry.RegenerateColor(parties, "party-03", tuning));
        }

        /// <summary>
        /// The honest-naming case. It reassigns from today's registry, so when the party's launch
        /// colour has gone to another brand it comes back a <i>different</i> free colour rather than
        /// duplicating one. That is why this is not called <c>RestoreColor</c>.
        /// </summary>
        [Fact]
        public void RegenerateColor_YieldsADifferentFreeColourWhenTheOriginalHasBeenTakenSince()
        {
            EngineTuning tuning = EngineTuning.Default;
            List<Party> parties = PartyRegistry.GenerateInitial(SaveA, Y1990, RegionTheme.Eu, tuning);

            Party party = PartyRegistry.Find(parties, "party-03")!;
            Party usurper = PartyRegistry.Find(parties, "party-04")!;
            string original = party.ColorHex;

            party.ColorHex = "#123456";
            usurper.ColorHex = original;

            string regenerated = PartyRegistry.RegenerateColor(parties, "party-03", tuning);

            Assert.NotEqual(original, regenerated);
            Assert.False(PartyRegistry.IsColorTaken(parties, regenerated, "party-03"));
            Assert.Contains(regenerated, tuning.Parties.ColorPalette);
        }

        // ============================ CommandOutcome ==============================================

        [Fact]
        public void IsAccepted_TreatsTheColourWarningAsAnAcceptance()
        {
            Assert.True(CommandOutcomes.IsAccepted(CommandOutcome.Ok));
            Assert.True(CommandOutcomes.IsAccepted(CommandOutcome.OkColorInUse));

            Assert.False(CommandOutcomes.IsAccepted(CommandOutcome.NotFound));
            Assert.False(CommandOutcomes.IsAccepted(CommandOutcome.ValueRequired));
            Assert.False(CommandOutcomes.IsAccepted(CommandOutcome.TooLong));
            Assert.False(CommandOutcomes.IsAccepted(CommandOutcome.BadValue));
            Assert.False(CommandOutcomes.IsAccepted(CommandOutcome.Failed));
        }

        /// <summary>
        /// The warning has to survive the trip. If <c>OkColorInUse</c> crossed as <c>""</c> the panel
        /// would have nothing to show, which is the whole reason it is a separate member.
        /// </summary>
        [Fact]
        public void ToWire_KeepsTheColourWarningVisibleWhileOkStaysEmpty()
        {
            Assert.Equal("", CommandOutcomes.ToWire(CommandOutcome.Ok));
            Assert.Equal("OkColorInUse", CommandOutcomes.ToWire(CommandOutcome.OkColorInUse));
            Assert.Equal("NotFound", CommandOutcomes.ToWire(CommandOutcome.NotFound));
            Assert.Equal("ValueRequired", CommandOutcomes.ToWire(CommandOutcome.ValueRequired));
            Assert.Equal("TooLong", CommandOutcomes.ToWire(CommandOutcome.TooLong));
        }

        /// <summary>
        /// The four members W4 appends carry the numbers they were assigned. The enum crosses the
        /// bridge by member name, but it is also persisted nowhere and compared everywhere by value,
        /// and renumbering an existing member is the kind of change that looks like a tidy-up.
        /// </summary>
        [Fact]
        public void CommandOutcome_AppendsWithoutRenumberingTheExistingMembers()
        {
            Assert.Equal(0, (int)CommandOutcome.Ok);
            Assert.Equal(6, (int)CommandOutcome.Failed);
            Assert.Equal(7, (int)CommandOutcome.NotFound);
            Assert.Equal(8, (int)CommandOutcome.ValueRequired);
            Assert.Equal(9, (int)CommandOutcome.TooLong);
            Assert.Equal(10, (int)CommandOutcome.OkColorInUse);
        }

        // ============================ Limits against the shipped schemas ===========================

        /// <summary>
        /// <see cref="PartyIdentity.ShortNameMax"/>'s justification is the state schema's own
        /// <c>maxLength</c>, and a justification nothing checks is a comment. A short name over the
        /// schema's limit makes the sidecar fail validation on save — the field the player typed it
        /// into is the field that breaks the file.
        /// </summary>
        [Fact]
        public void ShortNameMax_MatchesTheStateSchemasOwnLimit()
        {
            JObject schema = LoadSchema("political_state.schema.json");
            JToken party = schema["$defs"]!["party"]!["properties"]!;

            Assert.Equal(PartyIdentity.ShortNameMax, (int)party["shortName"]!["maxLength"]!);

            // Published for the UI to pre-validate with; it must be the expression the sidecar is
            // actually checked against, or the panel would accept what the file rejects.
            Assert.Equal(PartyIdentity.ColorPattern, (string)party["colorHex"]!["pattern"]!);
        }

        /// <summary>
        /// The other three limits come from the flavor schema, so that a player-typed value and a
        /// generated one are subject to the same ceiling. A lower ceiling here would let the
        /// generator produce prose the player is forbidden to retype.
        /// </summary>
        [Fact]
        public void NameDescriptionAndSloganMax_MatchTheFlavorSchemasLimits()
        {
            JObject schema = LoadSchema("politics_flavor.schema.json");
            JToken party = schema["properties"]!["partyFlavor"]!["items"]!["properties"]!;

            Assert.Equal(PartyIdentity.NameMax, (int)party["name"]!["maxLength"]!);
            Assert.Equal(PartyIdentity.ShortNameMax, (int)party["shortName"]!["maxLength"]!);
            Assert.Equal(PartyIdentity.DescriptionMax, (int)party["description"]!["maxLength"]!);
            Assert.Equal(PartyIdentity.SloganMax, (int)party["slogan"]!["maxLength"]!);
        }

        // ============================ Fixtures ====================================================

        /// <summary>
        /// AppContext.BaseDirectory, not Environment.CurrentDirectory: the runner's cwd varies, the
        /// assembly's own location does not.
        /// </summary>
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Agora.sln"))) return dir.FullName;
                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate the repository root (no Agora.sln above " + AppContext.BaseDirectory + ").");
        }

        private static JObject LoadSchema(string fileName)
        {
            string path = Path.Combine(RepoRoot(), "data", "schemas", fileName);
            Assert.True(File.Exists(path), "data/schemas/" + fileName + " must ship.");
            return JObject.Parse(File.ReadAllText(path));
        }

        /// <summary>A party with all four prose fields already carrying the player's own text.</summary>
        private static Party Named(string id, PartyOverrides overrides) => new Party
        {
            Id = id,
            Name = "Player Name",
            ShortName = "PLR",
            Description = "Player description.",
            Slogan = "Player slogan.",
            ColorHex = "#C0392B",
            PlayerOverrides = overrides
        };

        private static PartyFlavor Prose() => new PartyFlavor
        {
            PartyId = "party-01",
            Name = "Prose Name",
            ShortName = "Prose",
            Description = "Prose description.",
            Slogan = "Prose slogan."
        };

        private static Party MakeParty(string id, string colorHex) => new Party
        {
            Id = id,
            ColorHex = colorHex,
            ArchetypeId = "test",
            Status = PartyStatus.Active,
            FoundedDate = Y1990
        };

        private static string Repeat(int length) => new string('x', length);
    }
}
