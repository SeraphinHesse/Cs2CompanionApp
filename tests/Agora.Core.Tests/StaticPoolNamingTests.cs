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
