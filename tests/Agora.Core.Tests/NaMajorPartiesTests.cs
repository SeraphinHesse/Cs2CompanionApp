using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Engine.Parties;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The major-versus-minor rule, and the load-time repair built on it.
    ///
    /// <para>
    /// The behaviour under test is narrow but load-bearing: on an NA save, exactly the brands
    /// generated from <c>liberal</c> and <c>conservative</c> are majors, and everything else is
    /// capped by the fringe ceiling at 3%. The tests that matter most are the ones where the old
    /// id-order heuristic gets it wrong — a save whose original major dissolved, and a splinter that
    /// copied its parent's archetype id — because those are the states that leave a fringe party
    /// permanently uncapped.
    /// </para>
    /// </summary>
    public class NaMajorPartiesTests
    {
        private static readonly string[] NaMajors = { "liberal", "conservative" };

        private static MajorCandidate Candidate(string id, string archetype,
                                                bool onBallot = true, bool hasPredecessor = false) =>
            new MajorCandidate
            {
                PartyId = id,
                ArchetypeId = archetype,
                IsOnBallot = onBallot,
                HasPredecessor = hasPredecessor
            };

        private static Party PartyOf(string id, string archetype,
                                     PartyStatus status = PartyStatus.Active,
                                     bool isMajor = false, string? predecessor = null) =>
            new Party
            {
                Id = id,
                ArchetypeId = archetype,
                Status = status,
                IsMajor = isMajor,
                PredecessorPartyId = predecessor
            };

        // ------------------------------------------------------------------------------------------
        // The rule
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// The headline case. Ids are deliberately scrambled so that the majors hold the HIGHEST ids —
        /// the old "two lowest live ids" heuristic would answer party-01 and party-02, which are the
        /// green and the populist.
        /// </summary>
        [Fact]
        public void Reconstruct_PicksTheTwoMajorArchetypes_RegardlessOfIdOrder()
        {
            var candidates = new List<MajorCandidate>
            {
                Candidate("party-01", "green"),
                Candidate("party-02", "populist"),
                Candidate("party-03", "liberal"),
                Candidate("party-04", "conservative")
            };

            Assert.Equal(new[] { "party-03", "party-04" },
                         NaMajorParties.Reconstruct(candidates, NaMajors, 2).ToArray());
        }

        /// <summary>
        /// A splinter copies its parent's ArchetypeId verbatim and is the only thing that carries a
        /// PredecessorPartyId, so the predecessor is the entire discriminator between the real liberal
        /// and its offspring. Without this rule a split would create a second "major".
        /// </summary>
        [Fact]
        public void Reconstruct_IgnoresASplinterThatCopiedItsParentsArchetype()
        {
            var candidates = new List<MajorCandidate>
            {
                Candidate("party-01", "liberal"),
                Candidate("party-02", "conservative"),
                Candidate("party-05", "liberal", hasPredecessor: true),
                Candidate("party-06", "liberal", hasPredecessor: true)
            };

            Assert.Equal(new[] { "party-01", "party-02" },
                         NaMajorParties.Reconstruct(candidates, NaMajors, 2).ToArray());
        }

        /// <summary>
        /// Two predecessor-free brands both claiming `liberal` is a state the engine will not produce
        /// but a hand-edited file can contain. One archetype must never occupy both slots.
        /// </summary>
        [Fact]
        public void Reconstruct_TakesAtMostOnePerArchetype()
        {
            var candidates = new List<MajorCandidate>
            {
                Candidate("party-01", "liberal"),
                Candidate("party-02", "liberal"),
                Candidate("party-03", "conservative")
            };

            Assert.Equal(new[] { "party-01", "party-03" },
                         NaMajorParties.Reconstruct(candidates, NaMajors, 2).ToArray());
        }

        /// <summary>
        /// A dissolved major does not consume a slot, and — the important half — the answer is NOT
        /// padded back up to two from the remaining fringe brands.
        /// </summary>
        [Fact]
        public void Reconstruct_SkipsOffBallotBrandsWithoutPaddingBackToTwo()
        {
            var candidates = new List<MajorCandidate>
            {
                Candidate("party-01", "liberal", onBallot: false),
                Candidate("party-02", "conservative"),
                Candidate("party-03", "green"),
                Candidate("party-04", "populist")
            };

            Assert.Equal(new[] { "party-02" },
                         NaMajorParties.Reconstruct(candidates, NaMajors, 2).ToArray());
        }

        /// <summary>
        /// One legitimate major beats one major plus a promoted green. Padding a partial answer from
        /// id order would flag party-03, and the fringe ceiling would then never cap it.
        /// </summary>
        [Fact]
        public void Reconstruct_DoesNotPadAPartialArchetypeAnswerFromIdOrder()
        {
            var candidates = new List<MajorCandidate>
            {
                Candidate("party-01", "liberal"),
                Candidate("party-03", "green"),
                Candidate("party-04", "populist")
            };

            Assert.Equal(new[] { "party-01" },
                         NaMajorParties.Reconstruct(candidates, NaMajors, 2).ToArray());
        }

        /// <summary>
        /// The old-file case: no party carries an archetype id at all. Zero majors on an NA save pins
        /// the whole ballot at baseCeiling, so the historical id-order rule is the better guess.
        /// </summary>
        [Fact]
        public void Reconstruct_FallsBackToIdOrderWhenNoCandidateCarriesAnArchetype()
        {
            var candidates = new List<MajorCandidate>
            {
                Candidate("party-01", ""),
                Candidate("party-02", ""),
                Candidate("party-03", ""),
                Candidate("party-04", "")
            };

            Assert.Equal(new[] { "party-01", "party-02" },
                         NaMajorParties.Reconstruct(candidates, NaMajors, 2).ToArray());
        }

        /// <summary>The fallback still refuses dead brands and splinters.</summary>
        [Fact]
        public void Reconstruct_FallbackStillSkipsDeadBrandsAndSplinters()
        {
            var candidates = new List<MajorCandidate>
            {
                Candidate("party-01", "", onBallot: false),
                Candidate("party-02", "", hasPredecessor: true),
                Candidate("party-03", ""),
                Candidate("party-04", "")
            };

            Assert.Equal(new[] { "party-03", "party-04" },
                         NaMajorParties.Reconstruct(candidates, NaMajors, 2).ToArray());
        }

        /// <summary>majorCount 0 is the EU branch: no party is ever a major under proportional.</summary>
        [Fact]
        public void Reconstruct_ReturnsNothingForEu()
        {
            var candidates = new List<MajorCandidate>
            {
                Candidate("party-01", "liberal"),
                Candidate("party-02", "conservative")
            };

            Assert.Empty(NaMajorParties.Reconstruct(candidates, NaMajors, 0));
        }

        /// <summary>
        /// The determinism kernel: the caller's array order must not reach the answer. The migration
        /// reads whatever order the writer happened to emit, so this is not hypothetical.
        /// </summary>
        [Fact]
        public void Reconstruct_IsOrderIndependent()
        {
            var a = new List<MajorCandidate>
            {
                Candidate("party-01", "liberal"),
                Candidate("party-02", "conservative"),
                Candidate("party-03", "green")
            };
            var b = new List<MajorCandidate> { a[2], a[0], a[1] };
            var c = new List<MajorCandidate> { a[1], a[2], a[0] };

            string[] expected = { "party-01", "party-02" };
            Assert.Equal(expected, NaMajorParties.Reconstruct(a, NaMajors, 2).ToArray());
            Assert.Equal(expected, NaMajorParties.Reconstruct(b, NaMajors, 2).ToArray());
            Assert.Equal(expected, NaMajorParties.Reconstruct(c, NaMajors, 2).ToArray());
        }

        [Fact]
        public void Reconstruct_ToleratesAnEmptyRegistry()
        {
            Assert.Empty(NaMajorParties.Reconstruct(new List<MajorCandidate>(), NaMajors, 2));
        }

        // ------------------------------------------------------------------------------------------
        // The repair
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// The guard on AgoraRuntime's fresh-mint branch, and on NaArray's majors-first contract:
        /// generation and reconstruction must agree, so the repair is a provable no-op on a save that
        /// was just created. If NaArray is ever reordered without updating the catalog prefix, this
        /// fails.
        /// </summary>
        [Fact]
        public void Repair_LeavesAFreshlyGeneratedNaRegistryAlone()
        {
            EngineTuning tuning = EngineTuning.Default;
            List<Party> parties = PartyRegistry.GenerateInitial(
                new Guid("c0ffee11-2222-3333-4444-555566667777"),
                new SimDate(1990, 1, 1), RegionTheme.Na, tuning);

            MajorRepairResult result = NaMajorParties.Repair(
                parties, NaMajorParties.DefaultMajorArchetypeIds(tuning.Parties.TargetCountNa),
                tuning.Parties.TargetCountNa);

            Assert.False(result.Changed);
            Assert.Equal(2, CountMajors(parties));
        }

        [Fact]
        public void Repair_DemotesEveryPartyFlaggedMajorByMistake()
        {
            var parties = new List<Party>
            {
                PartyOf("party-01", "liberal", isMajor: true),
                PartyOf("party-02", "conservative", isMajor: true),
                PartyOf("party-03", "green", isMajor: true),
                PartyOf("party-04", "populist", isMajor: true)
            };

            MajorRepairResult result = NaMajorParties.Repair(parties, NaMajors, 2);

            Assert.True(result.Changed);
            Assert.Equal(new[] { "party-03", "party-04" }, result.Demoted.ToArray());
            Assert.Empty(result.Promoted);
            Assert.True(parties[0].IsMajor);
            Assert.True(parties[1].IsMajor);
            Assert.False(parties[2].IsMajor);
            Assert.False(parties[3].IsMajor);
        }

        /// <summary>
        /// The case the id-order heuristic gets wrong in the field: the original liberal dissolved, so
        /// the lowest live id belongs to the green. Nothing may promote it.
        /// </summary>
        [Fact]
        public void Repair_DoesNotPromoteTheGreenWhenTheOriginalMajorDissolved()
        {
            var parties = new List<Party>
            {
                PartyOf("party-01", "liberal", PartyStatus.Dissolved, isMajor: true),
                PartyOf("party-03", "green"),
                PartyOf("party-04", "populist"),
                PartyOf("party-05", "conservative")
            };

            NaMajorParties.Repair(parties, NaMajors, 2);

            Assert.False(parties[1].IsMajor);
            Assert.False(parties[2].IsMajor);
            Assert.True(parties[3].IsMajor);
        }

        /// <summary>Non-negotiable #6: the state fingerprint moves once, never on every load.</summary>
        [Fact]
        public void Repair_IsIdempotent()
        {
            var parties = new List<Party>
            {
                PartyOf("party-01", "green", isMajor: true),
                PartyOf("party-02", "populist"),
                PartyOf("party-03", "liberal"),
                PartyOf("party-04", "conservative")
            };

            Assert.True(NaMajorParties.Repair(parties, NaMajors, 2).Changed);
            Assert.False(NaMajorParties.Repair(parties, NaMajors, 2).Changed);
            Assert.False(NaMajorParties.Repair(parties, NaMajors, 2).Changed);
        }

        /// <summary>
        /// A dissolved major keeps its flag. ApplyRevivals never restores IsMajor, so clearing it here
        /// would bring the brand back as a minor — and Ceilings skips off-ballot parties anyway, so
        /// the retained flag changes nothing while it is dead.
        /// </summary>
        [Fact]
        public void Repair_DoesNotTouchOffBallotFlags()
        {
            var parties = new List<Party>
            {
                PartyOf("party-01", "liberal", PartyStatus.Dissolved, isMajor: true),
                PartyOf("party-02", "conservative"),
                PartyOf("party-03", "green")
            };

            MajorRepairResult result = NaMajorParties.Repair(parties, NaMajors, 2);

            Assert.True(parties[0].IsMajor);
            Assert.DoesNotContain("party-01", result.Demoted);
        }

        [Fact]
        public void Repair_ClearsAStrayMajorFlagOnAnEuSave()
        {
            var parties = new List<Party>
            {
                PartyOf("party-01", "green", isMajor: true),
                PartyOf("party-02", "labour")
            };

            MajorRepairResult result = NaMajorParties.Repair(parties, NaMajorParties.DefaultMajorArchetypeIds(0), 0);

            Assert.True(result.Changed);
            Assert.Equal(new[] { "party-01" }, result.Demoted.ToArray());
            Assert.False(parties[0].IsMajor);
        }

        [Fact]
        public void Repair_ReportsNoChangeAndASummaryOnAHealthyRegistry()
        {
            var parties = new List<Party>
            {
                PartyOf("party-01", "liberal", isMajor: true),
                PartyOf("party-02", "conservative", isMajor: true),
                PartyOf("party-03", "green")
            };

            MajorRepairResult result = NaMajorParties.Repair(parties, NaMajors, 2);

            Assert.False(result.Changed);
            Assert.False(string.IsNullOrEmpty(result.Summary));
        }

        /// <summary>
        /// The frozen list the sidecar migration carries must still describe the live catalog. If NA
        /// ever gains a third major or renames one, this is the tripwire.
        /// </summary>
        [Fact]
        public void DefaultMajorArchetypeIds_MatchesTheNaCatalogPrefix()
        {
            Assert.Equal(new[] { "liberal", "conservative" },
                         NaMajorParties.DefaultMajorArchetypeIds(2).ToArray());
        }

        private static int CountMajors(IReadOnlyList<Party> parties)
        {
            int n = 0;
            for (int i = 0; i < parties.Count; i++)
            {
                if (parties[i].IsMajor) n++;
            }
            return n;
        }
    }
}
