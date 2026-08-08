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
    /// The region theme — the mapping it implies, and <see cref="PoliticalEngine.Retheme"/>, the one
    /// operation in the engine that deliberately throws politics away (fixplan W3).
    ///
    /// <para>
    /// The defect this suite exists for is not that a retheme might crash. It is that it might
    /// <i>succeed quietly and wrongly</i>: party ids are positional and are reused across themes with
    /// different meanings — EU <c>party-01</c> is the green brand, NA <c>party-01</c> is the liberal
    /// one — so every list keyed by a party id survives a naive retheme as a perfectly well-formed
    /// number attached to the wrong party. Nothing in the engine would ever reject it. So most of what
    /// is below asserts that things are <b>gone</b>.
    /// </para>
    /// </summary>
    public class RethemeTests
    {
        private static readonly Guid Save = new Guid("b71d5c90-2f44-4a17-8e6b-3c0d9a1e5f77");
        private static readonly SimDate Start = new SimDate(1990, 1, 1);

        // --- the mapping -----------------------------------------------------------------------------

        /// <summary>
        /// Both values, because a mapping written as an <c>if</c> has exactly two ways to be wrong and
        /// asserting one of them catches neither.
        /// </summary>
        [Fact]
        public void SystemFor_MapsNaToFptpAndEuToProportional()
        {
            Assert.Equal(ElectoralSystem.FirstPastThePost, RegionThemeRules.SystemFor(RegionTheme.Na));
            Assert.Equal(ElectoralSystem.Proportional, RegionThemeRules.SystemFor(RegionTheme.Eu));
        }

        /// <summary>
        /// The live latent bug this fixes: <c>AgoraSettings.System</c> stays at its initialiser
        /// <c>Proportional</c> unless something derives it, and nothing did. A save minted with the NA
        /// theme therefore ran North American parties through a list election on three-year terms with
        /// no mayor — silently, because neither half of that arrangement complains.
        /// </summary>
        [Fact]
        public void CreateInitialState_DerivesFirstPastThePostFromTheNaTheme()
        {
            PoliticalState state = Mint(RegionTheme.Na);

            Assert.Equal(ElectoralSystem.FirstPastThePost, state.Settings.System);
        }

        /// <summary>
        /// The EU half, and the reason every existing test in this repo was unaffected by the change:
        /// EU already mapped to the initialiser's value.
        /// </summary>
        [Fact]
        public void CreateInitialState_LeavesTheEuThemeProportional()
        {
            Assert.Equal(ElectoralSystem.Proportional, Mint(RegionTheme.Eu).Settings.System);
        }

        /// <summary>
        /// A settings object arriving with a system that contradicts its theme — a hand-edited sidecar,
        /// or one written before the derivation existed — is corrected rather than honoured. There is
        /// no override marker on the object, so an inconsistent pair carries no intent to respect.
        /// </summary>
        [Fact]
        public void CreateInitialState_OverridesAContradictorySystem()
        {
            AgoraSettings settings = Settings(RegionTheme.Na);
            settings.System = ElectoralSystem.Proportional;

            PoliticalState state = PoliticalEngine.CreateInitialState(
                Save, Start, settings, City(), EngineTuning.Default);

            Assert.Equal(ElectoralSystem.FirstPastThePost, state.Settings.System);
        }

        // --- native parity ---------------------------------------------------------------------------

        /// <summary>
        /// <b>The test that proves the retheme is not a second, divergent generator.</b> A save that
        /// switches to NA must end up with exactly the roster it would have had if the player had
        /// chosen NA at the first-run prompt — same ids, same colours, same platforms, same founding
        /// dates, same everything.
        ///
        /// <para>
        /// It is why regeneration is seeded at the save's <i>start</i> date rather than at the current
        /// one. <see cref="Party.FoundedDate"/> feeds the canned-name draw, so regenerating in month
        /// forty would give a rethemed save different party names from a natively-NA save forever
        /// after — a divergence with no error, no warning, and no way to notice except by owning both
        /// saves.
        /// </para>
        /// </summary>
        [Fact]
        public void Retheme_ProducesTheSamePartiesAsChoosingThatThemeAtMint()
        {
            PoliticalState native = Mint(RegionTheme.Na);
            RethemeResult switched = PoliticalEngine.Retheme(
                Mint(RegionTheme.Eu), RegionTheme.Na, Start, EngineTuning.Default);

            Assert.True(switched.Accepted);
            Assert.Equal(PartyDigest(native.Parties), PartyDigest(switched.State!.Parties));
        }

        /// <summary>
        /// The faction analogue, and it proves less than the party one — deliberately, because less is
        /// true. Factions are seeded from the party set <i>and the bloc set</i>, so this holds only
        /// while the blocs still match the ones a mint-time save was built from: here, where the
        /// retheme happens on the start date with the same city. A month-forty retheme regenerates
        /// factions from the city's <i>current</i> demography, <c>IssueClimate.FromBlocs</c> differs,
        /// and the faction set is not the mint-time one. What this pins is that the second seed
        /// argument has not drifted for the same inputs, not that the two saves converge in general.
        /// </summary>
        [Fact]
        public void Retheme_ProducesTheSameFactionsAsMintWhenTheBlocsHaveNotMoved()
        {
            PoliticalState native = Mint(RegionTheme.Na);
            RethemeResult switched = PoliticalEngine.Retheme(
                Mint(RegionTheme.Eu), RegionTheme.Na, Start, EngineTuning.Default);

            Assert.NotEmpty(native.Factions);
            Assert.Equal(FactionDigest(native.Factions), FactionDigest(switched.State!.Factions));
        }

        /// <summary>
        /// The parity above must not be an artefact of rethemeing on the start date itself. Regenerating
        /// from a state that has run for four years still lands on the mint roster, because the seed is
        /// the start date and not <see cref="PoliticalState.Date"/>.
        /// </summary>
        [Fact]
        public void Retheme_SeedsFromTheStartDateNotTheCurrentDate()
        {
            PoliticalState aged = Mint(RegionTheme.Eu);
            aged.Date = Start.AddMonths(48);

            RethemeResult switched = PoliticalEngine.Retheme(aged, RegionTheme.Na, Start, EngineTuning.Default);

            Assert.Equal(PartyDigest(Mint(RegionTheme.Na).Parties), PartyDigest(switched.State!.Parties));
        }

        // --- determinism and purity ------------------------------------------------------------------

        [Fact]
        public void Retheme_IsDeterministic()
        {
            string first = Hash(PoliticalEngine.Retheme(
                Mint(RegionTheme.Eu), RegionTheme.Na, Start, EngineTuning.Default).State!);

            string second = Hash(PoliticalEngine.Retheme(
                Mint(RegionTheme.Eu), RegionTheme.Na, Start, EngineTuning.Default).State!);

            Assert.Equal(first, second);
        }

        /// <summary>
        /// Purity, including the trap: <see cref="PoliticalState.Settings"/> is shared by reference
        /// across the engine's clone, so a retheme that wrote the new theme into it would reach
        /// straight back into the caller's state and change the save it was asked not to touch.
        /// </summary>
        [Fact]
        public void Retheme_DoesNotMutateThePriorState()
        {
            PoliticalState prior = Populated(RegionTheme.Eu);
            string before = Hash(prior);

            RethemeResult result = PoliticalEngine.Retheme(prior, RegionTheme.Na, Start, EngineTuning.Default);

            Assert.Equal(before, Hash(prior));
            Assert.Equal(RegionTheme.Eu, prior.Settings.Theme);
            Assert.Equal(ElectoralSystem.Proportional, prior.Settings.System);
            Assert.NotSame(prior.Settings, result.State!.Settings);
            Assert.NotSame(prior, result.State);
        }

        /// <summary>
        /// Every other setting rides across untouched. A retheme that reset the wake cadence or the
        /// snapshot retention to their defaults would be a settings wipe wearing a theme change's
        /// clothes.
        ///
        /// <para>
        /// Every field <c>AgoraSettings.Clone</c> copies is set to a non-default value and checked,
        /// except the three a retheme is entitled to have an opinion about: <c>Theme</c> and
        /// <c>System</c> are what it changes, and <c>ThemeLocked</c> true is what makes it refuse
        /// (covered by its own test). A field this test omits is one <c>Clone</c> may quietly drop.
        /// </para>
        /// </summary>
        [Fact]
        public void Retheme_CarriesEveryOtherSettingAcross()
        {
            PoliticalState prior = Mint(RegionTheme.Eu);
            prior.Settings.SchemaVersion = 4;
            prior.Settings.WakeCadence = LlmWakeCadence.Manual;
            prior.Settings.SnapshotRetention = 7;
            prior.Settings.Enabled = false;
            prior.Settings.EffectsEnabled = false;
            prior.Settings.PauseOnMajorNews = false;
            prior.Settings.ShowAllReports = true;
            prior.Settings.StartYear = 1990;

            AgoraSettings after = PoliticalEngine
                .Retheme(prior, RegionTheme.Na, Start, EngineTuning.Default).State!.Settings;

            Assert.Equal(4, after.SchemaVersion);
            Assert.Equal(LlmWakeCadence.Manual, after.WakeCadence);
            Assert.Equal(7, after.SnapshotRetention);
            Assert.False(after.Enabled);
            Assert.False(after.EffectsEnabled);
            Assert.False(after.PauseOnMajorNews);
            Assert.True(after.ShowAllReports);
            Assert.Equal(1990, after.StartYear);
        }

        // --- the guard -------------------------------------------------------------------------------

        /// <summary>
        /// <see cref="PoliticalState.ElectionHistory"/> is the authority, deliberately: a save that has
        /// voted has a political history keyed to brands that must not be redefined under it, and that
        /// fact is in the state itself rather than in a flag a failed migration could have dropped.
        /// </summary>
        [Fact]
        public void Retheme_RefusesOnceAnElectionHasBeenHeld()
        {
            PoliticalState prior = Mint(RegionTheme.Eu);
            prior.ElectionHistory.Add(new ElectionResult { Id = "election-1993-01", Date = new SimDate(1993, 1, 1) });

            RethemeResult result = PoliticalEngine.Retheme(prior, RegionTheme.Na, Start, EngineTuning.Default);

            Assert.Equal(CommandOutcome.ThemeLocked, result.Outcome);
            Assert.False(result.Changed);
            Assert.Same(prior, result.State);
        }

        /// <summary>
        /// The flag on its own is also honoured, with the history empty. The two checks are belt and
        /// braces on purpose — one covers a state whose flag is missing, the other a save whose history
        /// was trimmed or whose lock was set for a reason this build does not know about.
        /// </summary>
        [Fact]
        public void Retheme_RefusesWhenTheFlagIsSetWithNoElectionHistory()
        {
            PoliticalState prior = Mint(RegionTheme.Eu);
            prior.Settings.ThemeLocked = true;

            RethemeResult result = PoliticalEngine.Retheme(prior, RegionTheme.Na, Start, EngineTuning.Default);

            Assert.Equal(CommandOutcome.ThemeLocked, result.Outcome);
            Assert.False(result.Changed);
        }

        /// <summary>
        /// Asking for the theme the save already runs is accepted and does nothing — even after the
        /// lock. Re-confirming your own theme is not a change to refuse, and refusing it would show the
        /// player a rejection for a request that was already satisfied.
        /// </summary>
        [Fact]
        public void Retheme_AcceptsTheUnchangedThemeAsANoOp()
        {
            PoliticalState prior = Mint(RegionTheme.Eu);
            prior.Settings.ThemeLocked = true;

            RethemeResult result = PoliticalEngine.Retheme(prior, RegionTheme.Eu, Start, EngineTuning.Default);

            Assert.Equal(CommandOutcome.Ok, result.Outcome);
            Assert.False(result.Changed);
            Assert.Same(prior, result.State);
        }

        [Fact]
        public void Retheme_ReportsFailureOnANullState()
        {
            RethemeResult result = PoliticalEngine.Retheme(null, RegionTheme.Na, Start, EngineTuning.Default);

            Assert.Equal(CommandOutcome.Failed, result.Outcome);
            Assert.Null(result.State);
        }

        // --- what is cleared -------------------------------------------------------------------------

        /// <summary>
        /// The full list, in one test, because the failure mode is a single forgotten line and a
        /// per-field test would let the reviewer check off thirteen and miss the fourteenth. Every one
        /// of these is keyed to a party id whose meaning has just changed, and not one of them would
        /// throw if it survived.
        /// </summary>
        [Fact]
        public void Retheme_ClearsEverythingKeyedToTheOldPartyIds()
        {
            PoliticalState after = PoliticalEngine
                .Retheme(Populated(RegionTheme.Eu), RegionTheme.Na, Start, EngineTuning.Default).State!;

            Assert.Empty(after.CurrentVoteShares);
            Assert.Empty(after.CurrentDistrictStandings);
            Assert.Empty(after.RecentPolls);
            Assert.Null(after.Government);
            Assert.Empty(after.CoalitionHistory);
            Assert.Null(after.MayorPartyId);
            Assert.Empty(after.Mandates);
            Assert.Null(after.NextElectionDate);
            Assert.Null(after.LastFlavorDate);
            Assert.Equal(1, after.TermNumber);
            Assert.Equal(RegionTheme.Na, after.Settings.Theme);
            Assert.Equal(ElectoralSystem.FirstPastThePost, after.Settings.System);
            Assert.All(after.Blocs, b => Assert.Empty(b.PreviousVote));

            // Parties and factions are replaced, never merged: a brand the old theme placed at
            // party-03 and one the new theme places there are different parties sharing a slot.
            Assert.Equal(PartyDigest(Mint(RegionTheme.Na).Parties), PartyDigest(after.Parties));
            Assert.NotEmpty(after.Factions);
            Assert.All(after.Factions, f => Assert.Contains(after.Parties, p => p.Id == f.PartyId));

            // Both directions, because only this one catches ApplyFactionIds not having run: a party
            // that kept the old theme's ids, or never received the new ones, fails here.
            Assert.Contains(after.Parties, p => p.FactionIds.Count > 0);
            Assert.All(after.Parties, p => Assert.All(p.FactionIds,
                id => Assert.Contains(after.Factions, f => f.Id == id)));
        }

        /// <summary>
        /// Its own test despite being covered above, because it is the quietest of the fourteen: a
        /// bloc's memory of how it voted is a party-id vector fed straight into next cycle's habitual
        /// loyalty, so a stale one makes NA voters loyal to EU brands with no visible symptom beyond
        /// a slightly odd first election.
        /// </summary>
        [Fact]
        public void Retheme_ClearsEveryBlocsPreviousVote()
        {
            PoliticalState prior = Populated(RegionTheme.Eu);
            Assert.Contains(prior.Blocs, b => b.PreviousVote.Count > 0);

            PoliticalState after = PoliticalEngine
                .Retheme(prior, RegionTheme.Na, Start, EngineTuning.Default).State!;

            Assert.All(after.Blocs, b => Assert.Empty(b.PreviousVote));

            // …and the caller's blocs still remember, because the copy is what was emptied.
            Assert.Contains(prior.Blocs, b => b.PreviousVote.Count > 0);
        }

        /// <summary>
        /// The other half of the contract. A retheme that cleared the fired-event set would let a
        /// decade of one-shot history fire again, and one that dropped the blocs would throw away the
        /// demography the sensors spent the save building — neither has anything to do with the theme.
        /// </summary>
        [Fact]
        public void Retheme_LeavesEverythingThatIsNotAboutPartiesAlone()
        {
            PoliticalState prior = Populated(RegionTheme.Eu);
            PoliticalState after = PoliticalEngine
                .Retheme(prior, RegionTheme.Na, Start, EngineTuning.Default).State!;

            Assert.Equal(prior.SaveGuid, after.SaveGuid);
            Assert.Equal(prior.Date, after.Date);
            Assert.Equal(prior.SchemaVersion, after.SchemaVersion);
            Assert.Equal(prior.Settings.StartYear, after.Settings.StartYear);
            Assert.Same(prior.Indices, after.Indices);
            Assert.Equal(prior.FiredEventIds, after.FiredEventIds);
            Assert.Equal(prior.ActiveEvents.Select(e => e.Id), after.ActiveEvents.Select(e => e.Id));

            // The blocs themselves: same set, same demography, only the vote memory gone.
            Assert.Equal(prior.Blocs.Count, after.Blocs.Count);
            Assert.Equal(prior.Blocs.Sum(b => b.Population), after.Blocs.Sum(b => b.Population));
            Assert.Equal(prior.Blocs.Select(b => b.DistrictId + "/" + b.Key.Ordinal),
                         after.Blocs.Select(b => b.DistrictId + "/" + b.Key.Ordinal));
        }

        /// <summary>
        /// Rethemeing back to EU empties the faction set rather than leaving NA's behind. EU models no
        /// factions, and a leftover one would be a faction of a party that no longer exists.
        ///
        /// <para>
        /// Only the faction set is asserted. The parties' <c>FactionIds</c> would be empty here even if
        /// <c>ApplyFactionIds</c> never ran, because the roster is freshly generated — an assertion on
        /// them would restate the implementation and pass on a no-op, which is worse than no assertion
        /// at all. That <c>ApplyFactionIds</c> does its job on the way in is what
        /// <see cref="Retheme_ClearsEverythingKeyedToTheOldPartyIds"/> checks, from the NA side where
        /// the ids are non-empty and have to point somewhere.
        /// </para>
        /// </summary>
        [Fact]
        public void Retheme_EmptiesFactionsWhenTheNewThemeDoesNotModelThem()
        {
            PoliticalState na = PoliticalEngine
                .Retheme(Mint(RegionTheme.Eu), RegionTheme.Na, Start, EngineTuning.Default).State!;

            Assert.NotEmpty(na.Factions);

            PoliticalState eu = PoliticalEngine
                .Retheme(na, RegionTheme.Eu, Start, EngineTuning.Default).State!;

            Assert.Empty(eu.Factions);
        }

        // --- fixtures --------------------------------------------------------------------------------

        private static AgoraSettings Settings(RegionTheme theme) => new AgoraSettings
        {
            StartYear = Start.Year,
            Theme = theme
        };

        private static PoliticalState Mint(RegionTheme theme) =>
            PoliticalEngine.CreateInitialState(Save, Start, Settings(theme), City(), EngineTuning.Default);

        /// <summary>
        /// A minted state with every party-keyed field filled in by hand. Hand-filled rather than
        /// simulated because the engine only populates most of these <i>after</i> an election, and an
        /// election is precisely the thing that makes a retheme illegal — so the state this method
        /// builds is not one the engine can reach, and it is exactly the state the clearing rules have
        /// to be checked against.
        /// </summary>
        private static PoliticalState Populated(RegionTheme theme)
        {
            PoliticalState state = Mint(theme);
            string first = state.Parties[0].Id;

            state.Date = Start.AddMonths(30);
            state.TermNumber = 3;
            state.NextElectionDate = Start.AddMonths(36);
            state.MayorPartyId = first;
            state.LastFlavorDate = Start.AddMonths(29);

            state.CurrentVoteShares.Add(new PartyVoteShare(first, 1.0));
            state.CurrentDistrictStandings.Add(new DistrictResult
            {
                DistrictId = "east",
                WinningPartyId = first,
                Shares = new List<PartyVoteShare> { new PartyVoteShare(first, 1.0) }
            });

            state.RecentPolls.Add(new PollResult { Id = "poll-1", PollsterId = "pollster-a" });
            state.Government = new Coalition { Id = "coalition-1", LeadPartyId = first };
            state.CoalitionHistory.Add(new Coalition { Id = "coalition-0", LeadPartyId = first });
            state.Mandates.Add(new Mandate { Id = "mandate-1", PartyId = first });

            state.ActiveEvents.Add(new TimelineEvent { Id = "event-1", Severity = 3 });
            state.FiredEventIds.Add("event-1");

            foreach (Bloc bloc in state.Blocs)
                bloc.PreviousVote = new List<PartyVoteShare> { new PartyVoteShare(first, 1.0) };

            return state;
        }

        /// <summary>
        /// Everything about a party that generation decides. Compared as one string rather than field
        /// by field for the same reason the state hash exists: the field a hand-written assertion
        /// forgets is where the divergence hides.
        /// </summary>
        private static string PartyDigest(IReadOnlyList<Party> parties)
        {
            var text = new StringBuilder();

            foreach (Party p in parties)
            {
                text.Append(p.Id).Append(';').Append(p.ArchetypeId).Append(';').Append(p.ColorHex)
                    .Append(';').Append(p.CoreGrievance).Append(';').Append(p.Status).Append(';')
                    .Append(p.FoundedDate).Append(';');

                foreach (Issue issue in Issues.All) text.Append(N(p.Platform[issue])).Append(',');
                text.Append('\n');
            }

            return text.ToString();
        }

        private static string FactionDigest(IReadOnlyList<Faction> factions)
        {
            var text = new StringBuilder();

            foreach (Faction f in factions)
            {
                text.Append(f.Id).Append(';').Append(f.PartyId).Append(';').Append(f.ArchetypeId)
                    .Append(';').Append(N(f.InternalSupport)).Append(';').Append(N(f.TensionWithParty))
                    .Append(';').Append(f.Status).Append('\n');
            }

            return text.ToString();
        }

        /// <summary>
        /// A digest of everything a retheme could plausibly touch. Used for the determinism pair and
        /// for the purity check, where the question is "did anything at all move".
        /// </summary>
        private static string Hash(PoliticalState state)
        {
            var text = new StringBuilder();

            text.Append(state.SchemaVersion).Append('|').Append(state.SaveGuid).Append('|')
                .Append(state.Date).Append('|').Append(state.TermNumber).Append('|')
                .Append(state.NextElectionDate).Append('|').Append(state.IsCampaignSeason ? '1' : '0')
                .Append('|').Append(state.MayorPartyId ?? "-").Append('|')
                .Append(state.LastFlavorDate).Append('\n');

            text.Append(state.Settings.Theme).Append(';').Append(state.Settings.System).Append(';')
                .Append(state.Settings.ThemeLocked ? '1' : '0').Append(';')
                .Append(state.Settings.StartYear).Append(';').Append(state.Settings.WakeCadence)
                .Append(';').Append(state.Settings.SnapshotRetention).Append(';')
                .Append(state.Settings.EffectsEnabled ? '1' : '0').Append('\n');

            text.Append(PartyDigest(state.Parties)).Append(FactionDigest(state.Factions));

            foreach (Bloc b in state.Blocs)
            {
                text.Append(b.DistrictId).Append(';').Append(b.Key.Id).Append(';').Append(b.Population)
                    .Append(';').Append(N(b.Discontent)).Append(';');
                foreach (PartyVoteShare v in b.PreviousVote) text.Append(v.PartyId).Append('=').Append(N(v.Share)).Append(',');
                text.Append('\n');
            }

            foreach (PartyVoteShare s in state.CurrentVoteShares)
                text.Append(s.PartyId).Append('=').Append(N(s.Share)).Append(';');
            text.Append('\n');

            foreach (DistrictResult d in state.CurrentDistrictStandings)
                text.Append(d.DistrictId).Append(';').Append(d.WinningPartyId).Append('\n');

            foreach (PollResult p in state.RecentPolls) text.Append(p.Id).Append('\n');
            foreach (ElectionResult e in state.ElectionHistory) text.Append(e.Id).Append('\n');
            foreach (Coalition g in state.CoalitionHistory) text.Append(g.Id).Append('\n');
            foreach (Mandate m in state.Mandates) text.Append(m.Id).Append('\n');
            foreach (TimelineEvent e in state.ActiveEvents) text.Append(e.Id).Append('\n');

            text.Append(state.Government == null ? "-" : state.Government.Id).Append('\n');
            text.Append(string.Join(",", state.FiredEventIds)).Append('\n');

            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())))
                                   .Replace("-", "");
        }

        private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>
        /// A static three-district city, the same shape the engine suite uses. It never moves, so any
        /// difference between two runs is the engine's and not the city's.
        /// </summary>
        private static CitySnapshot City()
        {
            var districts = new List<DistrictSnapshot>
            {
                District("east", 40000, 42.0, 0.14, 41.0, 0.44),
                District("north", 60000, 55.0, 0.07, 24.0, 0.29),
                District("south", 50000, 61.0, 0.05, 19.0, 0.24)
            };

            return new CitySnapshot
            {
                Date = Start,
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
                Services = Coverage(0.68),
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
                                                 double unemployment, double commute, double rentBurden) =>
            new DistrictSnapshot
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
                Services = Coverage(0.68),
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

        private static ServiceCoverage Coverage(double level) =>
            new ServiceCoverage(level, level, level, level, level, level, level, level, level);
    }
}
