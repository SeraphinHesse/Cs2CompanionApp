// Requires the FlavorRequest.cs / StaticPoolContent.cs / StaticPoolProvider.cs <Compile Link> lines
// in Agora.Core.Tests.csproj (see the comment there for why).

using System;
using System.Collections.Generic;
using System.Reflection;
using Agora.Core.Contracts;
using Agora.Mod.Llm;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The seam between a CLI request and the canned pool's roster.
    ///
    /// <para>
    /// <c>LayeredFlavorProvider.RequestFlavor</c> used to assign the request itself to
    /// <c>StaticPoolProvider.Roster</c>, which made the two the same object. An election wake raises
    /// <c>ArticleCount</c> to seven or eight for the prompt, and the pool then read that count as its
    /// own — for the whole window until the next month boundary rebuilt the roster, on the no-CLI
    /// fail-closed path included. At eight, the pool alternates four city and four district pieces
    /// out of three district body templates, so <c>UniqueLine</c> exhausts its bounded retry and two
    /// outlets file the same paragraph.
    /// </para>
    ///
    /// <para>
    /// The fix is <c>FlavorRequest.RosterCopy</c>, and it is what is pinned here.
    /// <c>LayeredFlavorProvider</c> itself is not reachable from this suite — it constructs a
    /// <c>ClaudeCliProvider</c>, which logs through <c>ColossalFlavorLog</c> and so names the game —
    /// so the call site is a manual gate and the seam it depends on is covered below.
    /// </para>
    /// </summary>
    public class FlavorRosterCopyTests
    {
        private static readonly Guid Save = new Guid("3c1d5f60-0000-4000-8000-0123456789ab");
        private static readonly SimDate Founded = new SimDate(2019, 4, 1);

        private static StaticPoolProvider Pool() =>
            new StaticPoolProvider(Save, RegionTheme.Eu,
                                   FlavorValidator.Create(null, NullFlavorLog.Instance),
                                   NullFlavorLog.Instance);

        /// <summary>
        /// The election wake as <c>AgoraRuntime.MaybeWakeFlavor</c> builds it: full roster, a snapshot
        /// with districts to write about, and the raised count under EU rules.
        /// </summary>
        private static FlavorRequest ElectionRequest(SimDate date)
        {
            var request = new FlavorRequest
            {
                Date = date,
                Reason = FlavorWakeReason.Election,
                Theme = RegionTheme.Eu,
                Snapshot = Snapshot(date),
                ArticleCount = FlavorRequest.ElectionArticleCount(RegionTheme.Eu)
            };

            for (int i = 0; i < 3; i++)
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

        private static CitySnapshot Snapshot(SimDate date)
        {
            var snapshot = new CitySnapshot
            {
                Date = date,
                Population = 120_000,
                Happiness = 58.0,
                Districts = new List<DistrictSnapshot>()
            };

            for (int i = 0; i < 4; i++)
            {
                snapshot.Districts.Add(new DistrictSnapshot
                {
                    Id = "district-" + i.ToString("00"),
                    Name = "District " + i.ToString("00"),
                    Happiness = 40.0 + i,
                    Population = 30_000
                });
            }

            return snapshot;
        }

        /// <summary>
        /// The seam itself: the copy keeps the cast and drops the raised count. Everything the pool
        /// needs to write about who exists comes through; the one field that is a prompt instruction
        /// rather than a fact about the save does not.
        /// </summary>
        [Fact]
        public void RosterCopy_KeepsTheCastAndTheDefaultArticleCount()
        {
            FlavorRequest request = ElectionRequest(new SimDate(2031, 5, 1));
            Assert.Equal(FlavorRequest.ElectionArticleCountEu, request.ArticleCount);

            FlavorRequest roster = request.RosterCopy();

            Assert.Equal(FlavorRequest.DefaultArticleCount, roster.ArticleCount);
            Assert.Equal(request.Date, roster.Date);
            Assert.Equal(request.Theme, roster.Theme);
            Assert.Same(request.Snapshot, roster.Snapshot);
            Assert.Same(request.Parties, roster.Parties);
            Assert.Same(request.Factions, roster.Factions);
            Assert.Same(request.Events, roster.Events);
            Assert.Same(request.Stories, roster.Stories);

            // And the request it was copied from is untouched — the CLI still asks for its extra
            // pieces. A fix that reset the count in place would pass every assertion above and lose
            // the election coverage the prompt exists to ask for.
            Assert.Equal(FlavorRequest.ElectionArticleCountEu, request.ArticleCount);
        }

        /// <summary>
        /// A copy also stops the pool writing on the object the CLI worker is reading. The pool sets
        /// <c>Date</c>, <c>Snapshot</c> and <c>Theme</c> on its roster on every poll, from the sim
        /// thread; while the roster was the request, those writes raced the background generation.
        /// </summary>
        [Fact]
        public void PollingThePool_DoesNotWriteBackOntoTheRequest()
        {
            var wake = new SimDate(2031, 5, 1);
            FlavorRequest request = ElectionRequest(wake);

            StaticPoolProvider pool = Pool();
            pool.Roster = request.RosterCopy();

            var later = new SimDate(2031, 6, 1);
            Assert.NotNull(pool.TryGetFlavor(Snapshot(later), later));

            Assert.Equal(wake, request.Date);
            Assert.Equal(FlavorRequest.ElectionArticleCountEu, request.ArticleCount);
        }

        /// <summary>
        /// The consequence, end to end: the count the pool files is the count on the object it was
        /// handed, and nothing it files repeats itself.
        /// </summary>
        /// <remarks>
        /// The second half used to reproduce the defect directly — eight slots against three district
        /// body templates made the pool file the same paragraph twice — and it carried a message
        /// saying what to do if the template lists ever grew. W5-3 grew them, so that half is gone and
        /// this is what is left of it: the two counts, asserted side by side. The copy is still the
        /// only thing deciding which one the pool writes, and that is the property the seam exists
        /// for; the repetition was a symptom, and it was never the reason a raised count must not leak
        /// into a roster that outlives the request.
        /// </remarks>
        [Fact]
        public void PoolFilesTheCountItWasHanded_AndTheCopyIsWhatDecidesIt()
        {
            var date = new SimDate(2031, 5, 1);
            FlavorRequest request = ElectionRequest(date);

            FlavorDocument fromCopy = Pool().Generate(request.RosterCopy());
            Assert.NotNull(fromCopy);
            Assert.Equal(FlavorRequest.DefaultArticleCount, fromCopy.Articles.Count);

            FlavorDocument fromRequest = Pool().Generate(request);
            Assert.NotNull(fromRequest);
            Assert.Equal(FlavorRequest.ElectionArticleCountEu, fromRequest.Articles.Count);

            // Neither round repeats itself. At the raised count this is the stronger claim of the
            // two, and it is the one the enlarged template lists have to keep earning.
            AssertNoRepeatedBody(fromCopy);
            AssertNoRepeatedBody(fromRequest);
        }

        /// <summary>
        /// <c>RosterCopy</c> carries every property on <see cref="FlavorRequest"/>, discovered by
        /// reflection rather than by the list a human happened to write.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>RosterCopy</c> is a hand-maintained field-by-field copy, and the failure mode of one of
        /// those is not a compile error — it is a property arriving at its type default with
        /// everything reporting success. Wave 5 added <c>Stories</c>, and a roster that dropped it
        /// would leave the canned pool writing prose for no stories at all while the log said the
        /// round succeeded; the pool is the everyday story voice, so that is the whole feature
        /// missing on the path that matters most. The assertion above names <c>Stories</c>
        /// explicitly, which fixes exactly that one property and nothing about the next one.
        /// </para>
        /// <para>
        /// <b>Unrecognised types throw rather than being skipped.</b> A silent skip shrinks this
        /// guard back to whatever it happened to cover on the day it was written, and it stops
        /// failing without anyone noticing — the same rule <c>CloneStateCoverageTests</c> is written
        /// under. It asserts only that a value was <i>carried</i>, never how: the collections are
        /// deliberately shared rather than cloned, and pinning that here would forbid a later
        /// decision to deep-copy them.
        /// </para>
        /// </remarks>
        [Fact]
        public void RosterCopy_CarriesEveryPropertyExceptTheOneItIsFor()
        {
            // The single deliberate exception, and the reason RosterCopy exists at all: a raised
            // article count is a prompt instruction for one round, not a fact about the save, and it
            // must not leak into a roster that outlives the request.
            var deliberatelyReset = new HashSet<string>(StringComparer.Ordinal) { "ArticleCount" };

            var source = new FlavorRequest();
            var seeded = new List<PropertyInfo>();

            foreach (PropertyInfo property in typeof(FlavorRequest).GetProperties(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || !property.CanWrite) continue;
                if (deliberatelyReset.Contains(property.Name)) continue;

                property.SetValue(source, DistinctValueFor(property));
                seeded.Add(property);
            }

            FlavorRequest copy = source.RosterCopy();

            foreach (PropertyInfo property in seeded)
            {
                Assert.True(Equals(property.GetValue(source), property.GetValue(copy)),
                            "FlavorRequest." + property.Name + " did not survive RosterCopy. Add it " +
                            "to the copy, or — if it genuinely must be reset — add it to " +
                            "deliberatelyReset with the reason. A dropped property arrives at its " +
                            "type default and nothing anywhere reports it.");
            }

            Assert.Equal(FlavorRequest.DefaultArticleCount, copy.ArticleCount);
        }

        /// <summary>
        /// A value distinguishable from the type's default, so "carried" and "defaulted" cannot look
        /// alike. Throws on a type it has never seen — see the calling test's remarks.
        /// </summary>
        private static object DistinctValueFor(PropertyInfo property)
        {
            Type type = property.PropertyType;

            if (type == typeof(SimDate)) return new SimDate(2044, 9, 1);
            if (type == typeof(FlavorWakeReason)) return FlavorWakeReason.StoryDraft;
            if (type == typeof(RegionTheme)) return RegionTheme.Na;
            if (type == typeof(CitySnapshot)) return new CitySnapshot { Districts = new List<DistrictSnapshot>() };
            if (type == typeof(FlavorCatalog)) return new FlavorCatalog(null, null, null, null, new[] { "story-x" });
            if (type == typeof(List<PartyBrief>)) return new List<PartyBrief> { new PartyBrief() };
            if (type == typeof(List<FactionBrief>)) return new List<FactionBrief> { new FactionBrief() };
            if (type == typeof(List<EventBrief>)) return new List<EventBrief> { new EventBrief() };
            if (type == typeof(List<StoryBrief>)) return new List<StoryBrief> { new StoryBrief() };

            throw new Xunit.Sdk.XunitException(
                "FlavorRequest." + property.Name + " is a " + type.Name + ", which this guard does " +
                "not know how to seed. Teach it, rather than letting the property go unchecked — a " +
                "skip here is how the guard quietly stops covering what it was written for.");
        }

        private static void AssertNoRepeatedBody(FlavorDocument document)
        {
            var bodies = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < document.Articles.Count; i++)
            {
                Assert.True(bodies.Add(document.Articles[i].Body),
                            "two canned articles were filed with the same body.");
            }
        }
    }
}
