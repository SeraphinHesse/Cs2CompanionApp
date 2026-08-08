// Requires the StaticPoolContent.cs / StaticPoolProvider.cs <Compile Link> lines in
// Agora.Core.Tests.csproj (see the comment there for why).

using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Mod.Llm;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The canned pool's party names, and the one property that makes them usable as identity: they
    /// do not move.
    ///
    /// <para>
    /// The pool regenerates on every prose collection, which is every sim month. While the name draw
    /// was seeded on the request date, that meant every party was re-christened monthly — stable
    /// within a single generation, and therefore invisible to any test that generated once. The draw
    /// is now keyed on the party's own founding date, so these tests generate twice and compare.
    /// </para>
    ///
    /// <para>
    /// The save GUID is still in the seed, and T2 is what holds it there: keying purely on the
    /// founding date would be equally stable and would give every save on Earth the same ballot.
    /// </para>
    /// </summary>
    public class StaticPoolNamingTests
    {
        private static readonly Guid SaveA = new Guid("11111111-2222-3333-4444-555555555555");
        private static readonly Guid SaveB = new Guid("66666666-7777-8888-9999-aaaaaaaaaaaa");

        private static readonly SimDate Founded = new SimDate(1994, 3, 1);

        private static StaticPoolProvider Pool(Guid saveGuid) =>
            new StaticPoolProvider(saveGuid, RegionTheme.Eu,
                                   FlavorValidator.Create(null, NullFlavorLog.Instance),
                                   NullFlavorLog.Instance);

        /// <summary>
        /// A request for <paramref name="partyCount"/> parties, all founded on the same date. Ids are
        /// zero-padded so ordinal sort order matches numeric order past ten.
        /// </summary>
        private static FlavorRequest Request(SimDate date, int partyCount)
        {
            var request = new FlavorRequest { Date = date, Theme = RegionTheme.Eu, ArticleCount = 1 };

            for (int i = 0; i < partyCount; i++)
            {
                request.Parties.Add(new PartyBrief
                {
                    PartyId = "party-" + i.ToString("00"),
                    ArchetypeId = "archetype-" + i.ToString("00"),
                    CoreGrievance = Issues.All[i % Issues.Count],
                    StatusWord = "Opposition",
                    FoundedDate = Founded
                });
            }

            return request;
        }

        /// <summary>Party id to generated name, from a generation that must have succeeded.</summary>
        private static Dictionary<string, string> Names(StaticPoolProvider pool, FlavorRequest request)
        {
            FlavorDocument document = pool.Generate(request);
            Assert.NotNull(document);

            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < document.PartyFlavor.Count; i++)
            {
                var entry = document.PartyFlavor[i];
                names[entry.PartyId] = entry.Name;
            }

            return names;
        }

        /// <summary>
        /// T1 — the regression guard. Same save, same parties, two different request dates: the names
        /// must be byte-identical. This is the defect that renamed every party every sim month.
        /// </summary>
        [Fact]
        public void NamesAreIndependentOfTheRequestDate()
        {
            StaticPoolProvider pool = Pool(SaveA);

            Dictionary<string, string> january = Names(pool, Request(new SimDate(1994, 3, 1), 6));
            Dictionary<string, string> elevenYearsLater = Names(pool, Request(new SimDate(2005, 11, 1), 6));

            Assert.Equal(january.Count, elevenYearsLater.Count);
            foreach (KeyValuePair<string, string> pair in january)
            {
                Assert.Equal(pair.Value, elevenYearsLater[pair.Key]);
            }
        }

        /// <summary>
        /// T2 — and the save GUID still matters. Two saves whose parties share ids and founding dates
        /// (which is exactly what two fresh saves of the same start year look like) must not get the
        /// same ballot.
        /// </summary>
        [Fact]
        public void DifferentSaveGuidsGetDifferentNames()
        {
            var date = new SimDate(1994, 3, 1);

            Dictionary<string, string> a = Names(Pool(SaveA), Request(date, 6));
            Dictionary<string, string> b = Names(Pool(SaveB), Request(date, 6));

            int differences = 0;
            foreach (KeyValuePair<string, string> pair in a)
            {
                if (!string.Equals(pair.Value, b[pair.Key], StringComparison.Ordinal)) differences++;
            }

            // Not "all six differ": two independent draws from a finite word pool are allowed to
            // collide on one party. A shared seed would make all six identical, which is what this
            // rejects.
            Assert.True(differences >= 4,
                        "Only " + differences + " of " + a.Count + " names differ between saves; the " +
                        "save GUID looks to have dropped out of the name seed.");
        }

        /// <summary>
        /// T3 — the precondition for the runtime's synchronous unnamed sweep: every party handed in
        /// comes back with a name, and no two parties share one.
        /// </summary>
        [Fact]
        public void EveryPartyGetsANonEmptyDistinctName()
        {
            const int Count = 9;

            Dictionary<string, string> names = Names(Pool(SaveA), Request(new SimDate(1994, 3, 1), Count));

            Assert.Equal(Count, names.Count);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in names)
            {
                Assert.False(string.IsNullOrEmpty(pair.Value), pair.Key + " came back without a name.");
                Assert.True(seen.Add(pair.Value), "two parties were both named " + pair.Value + ".");
            }
        }

        /// <summary>
        /// T4 — a sixth party on the roster does not rename the five already there. One concern only:
        /// the request date is held fixed, because T1 owns the date and a test that moved both would
        /// not say which of them broke. Collision-freedom for the newcomer is T5's job.
        /// </summary>
        [Fact]
        public void AddingAPartyLeavesTheOthersAlone()
        {
            StaticPoolProvider pool = Pool(SaveA);
            var date = new SimDate(1994, 3, 1);

            Dictionary<string, string> before = Names(pool, Request(date, 5));

            // Same five, same date, plus one. Sorted last by id, so it cannot take a name out from
            // under them.
            FlavorRequest grown = Request(date, 5);
            grown.Parties.Add(NewParty("party-99", Founded));

            Dictionary<string, string> after = Names(pool, grown);

            Assert.Equal(before.Count + 1, after.Count);
            foreach (KeyValuePair<string, string> pair in before)
            {
                Assert.Equal(pair.Value, after[pair.Key]);
            }

            Assert.False(string.IsNullOrEmpty(after["party-99"]));
        }

        /// <summary>
        /// T5 — the regression guard for the splinter path. A party founded mid-save, added to a
        /// roster where everyone else is already named, must come back with a name no one else holds,
        /// and must not have moved anyone else's.
        ///
        /// <para>
        /// This is the property the runtime used to break by narrowing its naming request to the
        /// parties that still lacked a name. De-duplication is per <c>Generate</c> call — the used-set
        /// is allocated in <c>BuildDocument</c> — so a lone newcomer met an empty set and took its
        /// first draw unchallenged, which on a 96-to-168 name pool is a live chance of being a name an
        /// incumbent already held. T3's distinctness check could not see it, because it only ever
        /// generated one full roster.
        /// </para>
        ///
        /// <para>
        /// The founding date below is chosen so that the newcomer's <i>first</i> draw is
        /// <c>party-00</c>'s name: the subset generate is asserted to reproduce the clash, which both
        /// keeps the fixture honest and pins the defect. If the vocabulary in
        /// <c>StaticPoolContent</c> ever changes, that assertion fails first and says so — pick a new
        /// founding date rather than deleting it, or the rest of the test goes quietly vacuous.
        /// </para>
        /// </summary>
        [Fact]
        public void ANewlyFoundedPartyTakesNoNameAlreadyInUse()
        {
            StaticPoolProvider pool = Pool(SaveA);
            var date = new SimDate(2007, 6, 1);
            var splinterFounded = new SimDate(2005, 1, 1);

            Dictionary<string, string> before = Names(pool, Request(date, 7));

            // The subset path, as the runtime used to take it: this party alone, nothing to collide
            // with. It draws a name that is already on the ballot.
            var alone = new FlavorRequest { Date = date, Theme = RegionTheme.Eu, ArticleCount = 1 };
            alone.Parties.Add(NewParty("party-07", splinterFounded));

            string subsetName = Names(pool, alone)["party-07"];
            Assert.Equal(before["party-00"], subsetName);

            // The full roster, as it takes it now. Founded eleven years after the rest, as a splinter
            // is, and sorted last by id — which is the order PartyRegistry.NextPartyId hands them out.
            FlavorRequest grown = Request(date, 7);
            grown.Parties.Add(NewParty("party-07", splinterFounded));

            Dictionary<string, string> after = Names(pool, grown);

            Assert.Equal(before.Count + 1, after.Count);

            string newcomer = after["party-07"];
            Assert.False(string.IsNullOrEmpty(newcomer), "the newly founded party came back unnamed.");

            foreach (KeyValuePair<string, string> pair in before)
            {
                Assert.Equal(pair.Value, after[pair.Key]);
                Assert.False(string.Equals(pair.Value, newcomer, StringComparison.Ordinal),
                             "the new party was named " + newcomer + ", which " + pair.Key +
                             " already holds.");
            }
        }

        /// <summary>
        /// T6 — a name the roster already wears is off the table. Party A carries a CurrentName the
        /// engine (or the player, by hand) already gave it; no other party may be handed that string.
        ///
        /// <para>
        /// The fixture calibrates itself rather than hard-coding a word from
        /// <c>StaticPoolContent</c>: it generates once with everyone unnamed, takes the name
        /// <c>party-01</c> drew, and hands that exact string to <c>party-00</c> as its CurrentName.
        /// <c>party-00</c>'s draw is unaffected (its own stream did not move) and <c>party-01</c>'s
        /// first candidate is unchanged too — so against the unseeded code <c>party-01</c> would draw
        /// the reserved string again and the last assertion here would fail. That is the defect: the
        /// used-set started empty, so a current name reserved nothing, and the runtime would write the
        /// duplicate onto the still-unnamed party without ever noticing.
        /// </para>
        /// </summary>
        [Fact]
        public void ANameTheRosterAlreadyWearsIsNotHandedToAnotherParty()
        {
            StaticPoolProvider pool = Pool(SaveA);
            var date = new SimDate(2011, 4, 1);

            FlavorRequest baseline = Request(date, 2);
            string wanted = Names(pool, baseline)["party-01"];
            Assert.False(string.IsNullOrEmpty(wanted));

            // party-00 already holds it - the player renamed their own party to exactly that.
            // party-01 is still unnamed and is the one the pool is about to christen.
            FlavorRequest contested = Request(date, 2);
            contested.Parties[0].CurrentName = wanted;

            Dictionary<string, string> after = Names(pool, contested);

            Assert.False(string.Equals(after["party-01"], wanted, StringComparison.Ordinal),
                         "party-01 was named " + after["party-01"] + ", which party-00 already holds.");
        }

        /// <summary>
        /// T7 — and reserving a name must not cost its owner that name. The pool draws for every party
        /// in the roster, named ones included, so a party whose CurrentName is reserved has to be able
        /// to draw it back; otherwise its entry comes back holding something else, and because a
        /// pool-written name is provisional, <c>AgoraRuntime.ApplyProseNames</c> would apply the
        /// replacement — a rename every sim month, which is T1's defect returning by a side door.
        /// </summary>
        [Fact]
        public void APartyMayStillDrawItsOwnCurrentName()
        {
            StaticPoolProvider pool = Pool(SaveA);
            var date = new SimDate(2011, 4, 1);

            FlavorRequest baseline = Request(date, 4);
            Dictionary<string, string> drawn = Names(pool, baseline);

            // The steady state the runtime actually reaches: everyone wearing the name the pool gave
            // them last month, handed straight back in as CurrentName.
            FlavorRequest settled = Request(date, 4);
            for (int i = 0; i < settled.Parties.Count; i++)
            {
                settled.Parties[i].CurrentName = drawn[settled.Parties[i].PartyId];
            }

            Dictionary<string, string> again = Names(pool, settled);

            foreach (KeyValuePair<string, string> pair in drawn)
            {
                Assert.Equal(pair.Value, again[pair.Key]);
            }
        }

        /// <summary>
        /// T8 — factions are reserved too, and parties are built first, so this is the direction only
        /// the shared seeding can cover: a party drawing before any faction entry exists still cannot
        /// take a name a faction already wears. Calibrated the same way as T6.
        /// </summary>
        [Fact]
        public void APartyDoesNotTakeANameAFactionAlreadyWears()
        {
            StaticPoolProvider pool = Pool(SaveA);
            var date = new SimDate(2011, 4, 1);

            string wanted = Names(pool, Request(date, 2))["party-01"];

            FlavorRequest contested = Request(date, 2);
            contested.Factions.Add(new FactionBrief
            {
                FactionId = "faction-00",
                PartyId = "party-00",
                ArchetypeId = "archetype-faction-00",
                CoreGrievance = Issues.All[0],
                StatusWord = "Insurgent",
                CurrentName = wanted,
                FoundedDate = Founded
            });

            Dictionary<string, string> after = Names(pool, contested);

            Assert.False(string.Equals(after["party-01"], wanted, StringComparison.Ordinal),
                         "party-01 was named " + after["party-01"] + ", which faction-00 already holds.");
        }

        /// <summary>
        /// T9 — the seeding is request data, so the document stays a pure function of the request
        /// (non-negotiable #3). Two generations from one request, compared as serialized text rather
        /// than field by field, so a field a hand-written assertion forgot still counts.
        /// </summary>
        [Fact]
        public void GeneratingTwiceFromTheSameRequestIsIdentical()
        {
            StaticPoolProvider pool = Pool(SaveA);
            var date = new SimDate(2011, 4, 1);

            FlavorRequest request = Request(date, 5);
            request.Parties[0].CurrentName = "Steady Assembly";
            request.Parties[3].CurrentName = "Steady Assembly";   // a duplicate already in the wild
            request.Factions.Add(new FactionBrief
            {
                FactionId = "faction-00",
                PartyId = "party-00",
                ArchetypeId = "archetype-faction-00",
                CoreGrievance = Issues.All[1],
                StatusWord = "Insurgent",
                CurrentName = "Quiet Caucus",
                FoundedDate = Founded
            });

            Assert.Equal(Serialize(pool.Generate(request)), Serialize(pool.Generate(request)));
        }

        /// <summary>Every string the document carries, in document order, as one comparable blob.</summary>
        private static string Serialize(FlavorDocument document)
        {
            Assert.NotNull(document);

            var text = new System.Text.StringBuilder();
            text.Append(document.SchemaVersion).Append('\n').Append(document.GeneratedAtSimDateText).Append('\n');

            for (int i = 0; i < document.PartyFlavor.Count; i++)
            {
                PartyFlavorEntry p = document.PartyFlavor[i];
                text.Append("party\t").Append(p.PartyId).Append('\t').Append(p.Name).Append('\t')
                    .Append(p.ShortName).Append('\t').Append(p.Description).Append('\t')
                    .Append(p.Slogan).Append('\n');
            }

            for (int i = 0; i < document.FactionFlavor.Count; i++)
            {
                FactionFlavorEntry f = document.FactionFlavor[i];
                text.Append("faction\t").Append(f.FactionId).Append('\t').Append(f.PartyId).Append('\t')
                    .Append(f.Name).Append('\t').Append(f.ShortName).Append('\t')
                    .Append(f.Description).Append('\t').Append(f.LeaderName).Append('\n');
            }

            for (int i = 0; i < document.Articles.Count; i++)
            {
                ArticleEntry a = document.Articles[i];
                text.Append("article\t").Append(a.Id).Append('\t').Append(a.Outlet).Append('\t')
                    .Append(a.Headline).Append('\t').Append(a.Body).Append('\t').Append(a.Tone).Append('\t')
                    .Append(a.DistrictId).Append('\t').Append(a.EventId).Append('\t')
                    .Append(a.PartyId).Append('\n');
            }

            for (int i = 0; i < document.EventProse.Count; i++)
            {
                EventProseEntry e = document.EventProse[i];
                text.Append("event\t").Append(e.EventId).Append('\t').Append(e.LocalAngle).Append('\n');
            }

            return text.ToString();
        }

        private static PartyBrief NewParty(string partyId, SimDate founded) => new PartyBrief
        {
            PartyId = partyId,
            ArchetypeId = "archetype-" + partyId,
            CoreGrievance = Issues.All[0],
            StatusWord = "Founding",
            FoundedDate = founded
        };
    }
}
