// Requires the Persistence and Llm <Compile Link> lines in Agora.Core.Tests.csproj — SidecarStore,
// SidecarSchema, AgoraJson, FileFlavorCache and the validator chain. See the comment there for why
// they are linked rather than referenced.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Agora.Core.Contracts;
using Agora.Core.Engine;
using Agora.Core.Tuning;
using Agora.Mod.Llm;
using Agora.Mod.Persistence;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// A save written by a build that had never heard of the story system, taken all the way through:
    /// off the disk, up four state versions and three settings versions, through a flavor cache
    /// upgrade, and into a live engine tick.
    ///
    /// <para>
    /// <c>SidecarMigrationTests</c> proves each step writes what it says it writes, against a DOM.
    /// That is the right shape for a step and the wrong shape for the thing a player actually does,
    /// which is to open a save from before the rework and press play. Between the last migration
    /// assertion and the first frame of that session there are three joins nothing else covers: the
    /// migrated document has to <i>materialise</i> into the current contract, the flavor cache beside
    /// it has to survive its own independent version bump, and the result has to be something
    /// <see cref="PoliticalEngine.Advance"/> can tick without a story collection it expected turning
    /// out to be null. Each of those is invisible in a DOM assertion and obvious in a fixture.
    /// </para>
    ///
    /// <para>
    /// <b>The fixture is hand-authored at the old versions, not serialized from the current
    /// contract.</b> A fixture round-tripped through today's <see cref="PoliticalState"/> would carry
    /// today's fields — which is precisely the thing a pre-rework save does not have, so the migration
    /// would be handed its own answer and every assertion below would pass on a build where the steps
    /// had been deleted.
    /// </para>
    ///
    /// <para>
    /// State v4 / settings v3 / flavor cache v2 is the last shape that predates the story layer
    /// entirely: v4 is the version before <c>lastCompletedTickMonth</c>, settings v3 the version
    /// before the story wake joined the cadence, and cache v2 the version before <c>stories</c> and
    /// <c>resolutions</c> joined the prose file. A save at those three versions has none of the
    /// story system in it, which is what makes it the honest worst case.
    /// </para>
    /// </summary>
    public class LegacySaveUpgradeTests
    {
        private static readonly Guid Save = new Guid("11112222-3333-4444-5555-666677778888");
        private static readonly Guid OtherSave = new Guid("aaaabbbb-cccc-dddd-eeee-ffff00001111");

        /// <summary>The month the fixture's state file names, and therefore the month it is loaded at.</summary>
        private static readonly SimDate Written = new SimDate(1994, 3, 1);

        /// <summary>The save's political start. Four years before the fixture, so warmup is long past.</summary>
        private static readonly SimDate Start = new SimDate(1990, 1, 1);

        private const int TickMonths = 24;

        // --- The fixture -----------------------------------------------------------------------------

        /// <summary>
        /// The four party ids the fixture's registry carries. Named here because three different
        /// files in the fixture have to agree about them — the state, the flavor cache, and the
        /// catalog the validator filters the cache against — and a silent disagreement would look
        /// exactly like a migration that lost the registry.
        /// </summary>
        private static readonly string[] PartyIds =
        {
            "party-01", "party-02", "party-03", "party-04"
        };

        /// <summary>
        /// A state document at exactly the shape a v4-era build wrote: the nested settings block at
        /// version 3 with the three voter-model levels and nothing after them, every party carrying
        /// <c>playerOverrides</c> and <c>isMajor</c>, the fringe watch present, and no
        /// <c>lastCompletedTickMonth</c>, no story collection, no power block and no command log —
        /// because none of those existed.
        /// </summary>
        private static string StateV4()
        {
            return "{" +
                "\"schemaVersion\": 4," +
                "\"saveGuid\": \"" + SidecarPaths.FormatGuid(Save) + "\"," +
                "\"date\": \"1994-03-01\"," +
                "\"settings\": {" +
                    "\"schemaVersion\": 3," +
                    "\"startYear\": 1990," +
                    "\"theme\": \"Eu\"," +
                    "\"system\": \"Proportional\"," +
                    "\"wakeCadence\": \"Yearly, Election, Manual\"," +
                    "\"snapshotRetention\": 25," +
                    "\"enabled\": true," +
                    "\"effectsEnabled\": true," +
                    "\"themeLocked\": true," +
                    "\"pauseOnMajorNews\": false," +
                    "\"showAllReports\": true," +
                    "\"voteSharpness\": \"Sharp\"," +
                    "\"newsInfluence\": \"Default\"," +
                    "\"brandDiscipline\": \"Default\"" +
                "}," +
                "\"parties\": [" +
                    PartyV4("party-01", "Green Alliance", "green", 0.31, 12) + "," +
                    PartyV4("party-02", "Civic Union", "conservative", 0.28, 11) + "," +
                    PartyV4("party-03", "Labour Front", "labour", 0.24, 9) + "," +
                    PartyV4("party-04", "Heritage List", "heritage", 0.17, 7) +
                "]," +
                "\"factions\": []," +
                "\"blocs\": []," +
                "\"currentVoteShares\": [" +
                    "{\"partyId\": \"party-01\", \"share\": 0.31}," +
                    "{\"partyId\": \"party-02\", \"share\": 0.28}," +
                    "{\"partyId\": \"party-03\", \"share\": 0.24}," +
                    "{\"partyId\": \"party-04\", \"share\": 0.17}" +
                "]," +
                "\"electionHistory\": []," +
                "\"firedEventIds\": [\"timeline-eu-1992-maastricht\"]," +
                "\"activeEvents\": []," +
                "\"fringe\": {" +
                    "\"consecutiveFailureTerms\": 0," +
                    "\"lastClosedTermNumber\": 0," +
                    "\"lastTermFailureScore\": 0.0," +
                    "\"termNumber\": 0," +
                    "\"monthsObserved\": 0," +
                    "\"discontentSum\": 0.0," +
                    "\"defianceSurgeSum\": 0.0," +
                    "\"governmentChanges\": 0," +
                    "\"mayorChanges\": 0" +
                "}," +
                "\"termNumber\": 2," +
                "\"nextElectionDate\": \"1998-01-01\"," +
                "\"isCampaignSeason\": false" +
            "}";
        }

        private static string PartyV4(string id, string name, string archetypeId, double share, int seats)
        {
            return "{" +
                "\"id\": \"" + id + "\"," +
                "\"name\": \"" + name + "\"," +
                "\"shortName\": \"" + id + "\"," +
                "\"colorHex\": \"#3366aa\"," +
                "\"archetypeId\": \"" + archetypeId + "\"," +
                "\"platform\": {" +
                    "\"services\": 0.6, \"costOfLiving\": 0.5, \"environment\": 0.7," +
                    "\"transit\": 0.6, \"growth\": 0.4, \"heritageOrder\": 0.3" +
                "}," +
                "\"status\": \"Active\"," +
                "\"foundedDate\": \"1990-01-01\"," +
                "\"lastVoteShare\": " + share.ToString("0.00", CultureInfo.InvariantCulture) + "," +
                "\"seatsHeld\": " + seats.ToString(CultureInfo.InvariantCulture) + "," +
                "\"revivalCount\": 0," +
                "\"playerOverrides\": \"None\"," +
                "\"isMajor\": false" +
            "}";
        }

        /// <summary>
        /// A flavor cache at version 2 — the last shape before the story prose collections joined it.
        /// It carries the four party names, which are the load-bearing content of the file: they are
        /// what the seat chart renders, and losing them to the upgrade is the regression
        /// <c>FlavorCacheMigrationTests</c> guards at the unit level and this guards at the file level.
        /// </summary>
        private static string FlavorCacheV2()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"schemaVersion\": 2,\"generatedAtSimDate\": \"1994-03-01\",\"partyFlavor\": [");

            for (int i = 0; i < PartyIds.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"partyId\": \"").Append(PartyIds[i])
                  .Append("\",\"name\": \"Cached Name ").Append(i.ToString(CultureInfo.InvariantCulture))
                  .Append("\",\"shortName\": \"CN").Append(i.ToString(CultureInfo.InvariantCulture))
                  .Append("\",\"description\": \"A party with a history and a cached name.\"")
                  .Append(",\"slogan\": \"Keep the trams running.\"}");
            }

            sb.Append("],\"articles\": [{\"id\": \"article-01\",\"outlet\": \"Harbour Register\"," +
                      "\"headline\": \"Council adjourns over the tram budget\"," +
                      "\"body\": \"The chamber spent the morning on a bridge nobody wanted.\"," +
                      "\"tone\": \"neutral\",\"refs\": {\"districtId\": \"" +
                      SyntheticCityHistory.DistrictIds[0] + "\"}}]}");

            return sb.ToString();
        }

        /// <summary>
        /// The catalog the cache is filtered against, built from the fixture's own rosters rather than
        /// spelled a second time — a hand-copied list here would fail as a lost cache entry and read
        /// as a migration bug.
        /// </summary>
        private static FlavorCatalog Catalog()
        {
            return new FlavorCatalog(PartyIds, new string[0], SyntheticCityHistory.DistrictIds,
                                     new string[0], new string[0]);
        }

        // --- The run ---------------------------------------------------------------------------------

        /// <summary>Everything one pass over the legacy save produced, in a shape a hash can be taken over.</summary>
        private sealed class UpgradeRun
        {
            public PoliticalState Loaded { get; set; } = new PoliticalState();
            public AgoraSettings Settings { get; set; } = new AgoraSettings();
            public List<string> Warnings { get; set; } = new List<string>();
            public FlavorDocument? Flavor { get; set; }
            public List<PoliticalState> Ticked { get; set; } = new List<PoliticalState>();
            public int EngineTicks { get; set; }

            /// <summary>The state after the last tick — what a session that ran two years would persist.</summary>
            public PoliticalState Final
            {
                get { return Ticked.Count == 0 ? Loaded : Ticked[Ticked.Count - 1]; }
            }
        }

        /// <summary>
        /// Writes the legacy save into <paramref name="directory"/>, loads it, upgrades it, and ticks
        /// <see cref="TickMonths"/> months of synthetic city on top of it.
        /// </summary>
        /// <remarks>
        /// No wall clock and no unnamed draw anywhere on this path: the city comes from
        /// <see cref="SyntheticCityHistory"/>, which derives every value from the month index alone,
        /// and the save identity is a parameter. That is what makes the fingerprint comparison below
        /// mean something.
        /// </remarks>
        private static UpgradeRun Run(string root, Guid saveGuid)
        {
            string directory = Path.Combine(root, SidecarPaths.FormatGuid(saveGuid));
            Directory.CreateDirectory(directory);

            File.WriteAllText(Path.Combine(directory, "state_1994_03.json"), StateV4());
            File.WriteAllText(Path.Combine(directory, SidecarPaths.FlavorCacheFileName), FlavorCacheV2());

            var store = new SidecarStore(root, NullSidecarLog.Instance);
            SidecarLoadResult loaded = store.Load(saveGuid, Written);

            var run = new UpgradeRun
            {
                Loaded = loaded.State,
                Settings = loaded.Settings,
                Warnings = new List<string>(loaded.Warnings),
                Flavor = new FileFlavorCache(directory, new FlavorValidator(FlavorSchema.Load(null, null), null),
                                             Catalog(), null).Load()
            };

            var history = new List<CitySnapshot>();
            PoliticalState state = loaded.State;

            for (int month = 1; month <= TickMonths; month++)
            {
                SimDate date = Written.AddMonths(month);
                CitySnapshot city = SyntheticCityHistory.Snapshot(date, month);

                EngineTickResult tick = PoliticalEngine.Advance(new EngineTickInput
                {
                    SaveGuid = saveGuid,
                    Date = date,
                    StartDate = Start,
                    PriorState = state,
                    Snapshot = city,
                    SnapshotHistory = history.ToArray(),
                    Tuning = EngineTuning.Default
                });

                if (tick.DidWork) run.EngineTicks++;

                state = tick.State;
                run.Ticked.Add(state);
                history.Add(city);
            }

            return run;
        }

        // --- 1. It loads and it upgrades ---------------------------------------------------------------

        /// <summary>
        /// The state and its nested settings both arrive at the versions this build writes. Two
        /// assertions rather than one because the two versions move on separate schedules — the state
        /// has been ahead of the settings since wave 0 — and a chain that stalled on one of them
        /// would still satisfy the other.
        /// </summary>
        [Fact]
        public void APreReworkSave_LoadsAtTheCurrentStateAndSettingsVersions()
        {
            string root = TempRoot("loads");

            try
            {
                UpgradeRun run = Run(root, Save);

                Assert.Equal(SidecarSchema.CurrentStateVersion, run.Loaded.SchemaVersion);
                Assert.Equal(SidecarSchema.CurrentSettingsVersion, run.Loaded.Settings.SchemaVersion);
                Assert.Equal(SidecarSchema.CurrentSettingsVersion, run.Settings.SchemaVersion);

                // A migration is not supposed to be eventful. A warning here means the load path found
                // something it could not account for, which on a fixture this build wrote the shape of
                // is a defect rather than a tolerated degradation.
                Assert.Empty(run.Warnings);
            }
            finally
            {
                Delete(root);
            }
        }

        /// <summary>
        /// The politics survive the upgrade. Every one of these is a field the player would see go
        /// missing: the party names on the seat chart, the seat counts, the term they are in, and the
        /// date of the ballot they are heading toward.
        /// </summary>
        [Fact]
        public void APreReworkSave_KeepsThePoliticsItWasWrittenWith()
        {
            string root = TempRoot("politics");

            try
            {
                UpgradeRun run = Run(root, Save);
                PoliticalState state = run.Loaded;

                Assert.Equal(4, state.Parties.Count);
                Assert.Equal("Green Alliance", state.Parties[0].Name);
                Assert.Equal("Heritage List", state.Parties[3].Name);
                Assert.Equal(12, state.Parties[0].SeatsHeld);
                Assert.Equal(0.31, state.Parties[0].LastVoteShare, 6);

                Assert.Equal(2, state.TermNumber);
                Assert.Equal(new SimDate(1998, 1, 1), state.NextElectionDate);
                Assert.Contains("timeline-eu-1992-maastricht", state.FiredEventIds);

                // The player's own choices, not defaults. A settings migration that reset these would
                // be the most quietly destructive of the lot: nothing breaks, the save simply stops
                // behaving the way its owner set it up to.
                Assert.Equal(1990, state.Settings.StartYear);
                Assert.Equal(RegionTheme.Eu, state.Settings.Theme);
                Assert.True(state.Settings.ThemeLocked);
                Assert.False(state.Settings.PauseOnMajorNews);
                Assert.True(state.Settings.ShowAllReports);
                Assert.Equal(VoteSharpness.Sharp, state.Settings.VoteSharpness);
            }
            finally
            {
                Delete(root);
            }
        }

        /// <summary>
        /// The story layer arrives empty rather than absent, and the two watermarks arrive at "never".
        /// </summary>
        /// <remarks>
        /// The distinction is the whole point. A null collection here would not fail a migration
        /// assertion — the document simply would not have the property — and would then be
        /// dereferenced by the first story tick. Materialising the contract is where "absent" becomes
        /// "empty", which is why this is asserted on the loaded state rather than on the DOM.
        /// </remarks>
        [Fact]
        public void APreReworkSave_ArrivesWithAnEmptyStoryLayerRatherThanAnAbsentOne()
        {
            string root = TempRoot("story-layer");

            try
            {
                PoliticalState state = Run(root, Save).Loaded;

                Assert.NotNull(state.LiveStories);
                Assert.NotNull(state.StoryArchive);
                Assert.NotNull(state.EventPool);
                Assert.NotNull(state.PlayerCommands);
                Assert.NotNull(state.Power);

                Assert.Empty(state.LiveStories);
                Assert.Empty(state.StoryArchive);
                Assert.Empty(state.EventPool);
                Assert.Empty(state.PlayerCommands);
                Assert.Empty(state.Power.Ledger);

                // Zero, not "whatever a fresh save starts with": a save that predates the currency
                // has not earned or spent any of it, and seeding a balance would hand every existing
                // player a windfall the economy was never balanced against.
                Assert.Equal(0, state.Power.Balance);
                Assert.Equal(0, state.Power.LifetimeEarned);
                Assert.Equal(0, state.Power.LifetimeSpent);
                Assert.Equal(-1, state.Power.LastAccrualMonth);

                Assert.Equal(-1, state.LastStoryDraftMonth);
                Assert.Equal(-1, state.LastStoryResolveMonth);

                // The tick watermark is the one field that is NOT seeded to "never". A state file is
                // written after the month it names has finished, so the month it names is the last
                // completed one — seeding -1 would hand the save one free duplicate tick on its first
                // load, which is the bug the field exists to close.
                Assert.Equal(Written.TotalMonths, state.LastCompletedTickMonth);
            }
            finally
            {
                Delete(root);
            }
        }

        /// <summary>
        /// The story wake joins a cadence the player never narrowed, and <c>pauseOnMajorStory</c>
        /// arrives at its default.
        /// </summary>
        [Fact]
        public void APreReworkSave_GainsTheStoryWakeAndTheStoryPause()
        {
            string root = TempRoot("cadence");

            try
            {
                AgoraSettings settings = Run(root, Save).Loaded.Settings;

                // The fixture's cadence is the whole of the old Default, so the upgrade widens it and
                // the save lands on today's Default rather than on a narrowed set frozen in 1994.
                Assert.Equal(LlmWakeCadence.Default, settings.WakeCadence);
                Assert.True((settings.WakeCadence & LlmWakeCadence.Story) != 0);

                Assert.True(settings.StoriesEnabled);
                Assert.True(settings.PoliticalPowerEnabled);
                Assert.Equal(PowerIntensity.Default, settings.PowerIntensity);
                Assert.Equal(StoryDifficulty.Default, settings.StoryDifficulty);
                Assert.Equal(new AgoraSettings().PauseOnMajorStory, settings.PauseOnMajorStory);
            }
            finally
            {
                Delete(root);
            }
        }

        // --- 2. The prose file beside it ---------------------------------------------------------------

        /// <summary>
        /// The flavor cache upgrades on its own schedule and keeps its party names.
        /// </summary>
        /// <remarks>
        /// It has to be exercised here rather than only in <c>FlavorCacheMigrationTests</c> because
        /// the two files version independently: a state at v4 sits beside a cache at v2, and nothing
        /// in either upgrade knows about the other. The failure mode is a load that produces a
        /// perfectly migrated state and a null document, which in game is a seat chart reading
        /// <c>party-01</c> — and it is silent, because a missing cache is also the correct answer for
        /// a save that has never woken the model.
        /// </remarks>
        [Fact]
        public void APreReworkFlavorCache_UpgradesBesideItAndKeepsThePartyNames()
        {
            string root = TempRoot("flavor");

            try
            {
                UpgradeRun run = Run(root, Save);

                Assert.NotNull(run.Flavor);
                Assert.Equal(PartyIds.Length, run.Flavor!.PartyFlavor.Count);
                Assert.Equal("Cached Name 0", run.Flavor.PartyFlavor[0].Name);

                // The article survives too. It is the less valuable half — a cache is derived and the
                // next wake rebuilds it — but an upgrade that dropped it would mean the v2 → v3 step
                // had started pruning prose that is inside the current limits.
                Assert.Single(run.Flavor.Articles);
            }
            finally
            {
                Delete(root);
            }
        }

        // --- 3. And it ticks ---------------------------------------------------------------------------

        /// <summary>
        /// Two years of engine on top of the upgraded save, and the engine did real work in them.
        /// </summary>
        /// <remarks>
        /// The count matters as much as the absence of an exception. Every subsystem in the tick is
        /// on a cadence, so a run that ticked zero months would sail through a "did not throw"
        /// assertion having exercised nothing at all — which is exactly what a migration that left
        /// the tick watermark ahead of the clock would produce.
        /// </remarks>
        [Fact]
        public void APreReworkSave_TicksAfterUpgrading()
        {
            string root = TempRoot("ticks");

            try
            {
                UpgradeRun run = Run(root, Save);

                Assert.True(run.EngineTicks > 0,
                            "the upgraded save ticked " + TickMonths + " months and the engine did " +
                            "work in none of them");

                Assert.Equal(SidecarSchema.CurrentStateVersion, run.Final.SchemaVersion);
                Assert.Equal(Written.AddMonths(TickMonths), run.Final.Date);

                // The watermark advances with the clock rather than staying where the migration put
                // it, and the registry is still the one the save was written with rather than a fresh
                // one the tick generated because it found the roster empty.
                Assert.Equal(4, run.Final.Parties.Count);
                Assert.Equal("Green Alliance", run.Final.Parties[0].Name);
            }
            finally
            {
                Delete(root);
            }
        }

        /// <summary>
        /// <b>The canonical determinism pattern, over the whole upgrade.</b> Two independent
        /// directories, the same bytes on disk, the same save identity — one fingerprint.
        /// </summary>
        /// <remarks>
        /// This is the assertion the whole file exists for. Non-negotiable #3 says engine state is a
        /// pure function of its inputs, and a migration is an input like any other: a step that
        /// iterated a dictionary, or read a wall clock to fill a date it could not find, would be
        /// stable within one run and different across two. Hashing the serialized result rather than
        /// comparing fields catches the ones a hand-written assertion did not think of.
        /// </remarks>
        [Fact]
        public void TheWholeUpgradeAndTick_IsByteIdenticalFromIdenticalInputs()
        {
            string first = TempRoot("determinism-a");
            string second = TempRoot("determinism-b");

            try
            {
                Assert.Equal(AgoraJson.Fingerprint(Run(first, Save).Final),
                             AgoraJson.Fingerprint(Run(second, Save).Final));
            }
            finally
            {
                Delete(first);
                Delete(second);
            }
        }

        /// <summary>
        /// And it is not deterministic by being inert: the same legacy bytes under a different save
        /// guid produce a different history.
        /// </summary>
        /// <remarks>
        /// The guard on the test above. Two runs that both produced nothing would agree perfectly,
        /// and the save guid is the first argument to every seed derivation — so if it stops reaching
        /// the tick, every save in the world starts sharing one political history and the only thing
        /// that says so is this assertion.
        /// </remarks>
        [Fact]
        public void TheWholeUpgradeAndTick_DependsOnTheSaveIdentity()
        {
            string first = TempRoot("identity-a");
            string second = TempRoot("identity-b");

            try
            {
                Assert.NotEqual(AgoraJson.Fingerprint(Run(first, Save).Final),
                                AgoraJson.Fingerprint(Run(second, OtherSave).Final));
            }
            finally
            {
                Delete(first);
                Delete(second);
            }
        }

        // --- 4. The reload the player performs next ------------------------------------------------------

        /// <summary>
        /// Non-negotiable #6, at the join the rest of this file sets up: the upgraded save is written
        /// back, read again, and has not moved.
        /// </summary>
        /// <remarks>
        /// This is <c>Migrate</c> proven idempotent over its own output, through the store rather
        /// than against a DOM — the migration runs a second time on the file the first one produced,
        /// because the reload cannot tell that it already ran. A step that stamped its version but
        /// kept re-applying its edit would be invisible to a single-pass fixture and would move this
        /// fingerprint.
        /// </remarks>
        [Fact]
        public void TheUpgradedSave_SurvivesASaveAndReloadWithoutMoving()
        {
            string root = TempRoot("reload");

            try
            {
                UpgradeRun run = Run(root, Save);
                var store = new SidecarStore(root, NullSidecarLog.Instance);

                Assert.True(store.SaveState(run.Final));

                SidecarLoadResult again = store.Load(Save, run.Final.Date);

                Assert.True(again.HasState);
                Assert.Empty(again.Warnings);
                Assert.Equal(AgoraJson.Fingerprint(run.Final), AgoraJson.Fingerprint(again.State));
            }
            finally
            {
                Delete(root);
            }
        }

        // --- Temp directories ----------------------------------------------------------------------------

        private static string TempRoot(string name)
        {
            string path = Path.Combine(Path.GetTempPath(), "agora-legacy-save-tests", name);
            Delete(path);
            Directory.CreateDirectory(path);
            return path;
        }

        private static void Delete(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a test over.
            }
        }
    }
}
