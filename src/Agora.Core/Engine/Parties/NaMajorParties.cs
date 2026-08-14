using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Parties
{
    /// <summary>
    /// One party, projected down to only the four facts the major-versus-minor question needs.
    ///
    /// <para>
    /// The projection exists so that the rule can be written once and run against two very different
    /// shapes: a live <see cref="Party"/> list on the load path, and a <c>JObject</c> DOM inside the
    /// sidecar migration, which runs before anything has been materialised into contracts. Neither
    /// side gets to hold a copy of the rule — both build this struct and call
    /// <see cref="NaMajorParties.Reconstruct"/>.
    /// </para>
    /// </summary>
    public struct MajorCandidate
    {
        /// <summary><see cref="Party.Id"/>. Empty ids are ignored by the rule.</summary>
        public string PartyId { get; set; }

        /// <summary><see cref="Party.ArchetypeId"/>. Null or empty on a file written before it was persisted.</summary>
        public string ArchetypeId { get; set; }

        /// <summary><see cref="PartyRegistry.IsOnBallot"/> — Active, Endangered or Revived.</summary>
        public bool IsOnBallot { get; set; }

        /// <summary><see cref="Party.PredecessorPartyId"/> is set, i.e. this brand split off another.</summary>
        public bool HasPredecessor { get; set; }
    }

    /// <summary>What <see cref="NaMajorParties.Repair"/> changed, if anything.</summary>
    public sealed class MajorRepairResult
    {
        internal MajorRepairResult(List<string> promoted, List<string> demoted)
        {
            Promoted = promoted;
            Demoted = demoted;
            Summary = BuildSummary(promoted, demoted);
        }

        /// <summary>Parties whose <see cref="Party.IsMajor"/> went false → true. Ordinal-sorted.</summary>
        public List<string> Promoted { get; }

        /// <summary>Parties whose <see cref="Party.IsMajor"/> went true → false. Ordinal-sorted.</summary>
        public List<string> Demoted { get; }

        /// <summary>True when anything moved. False is the expected answer on a healthy save.</summary>
        public bool Changed => Promoted.Count > 0 || Demoted.Count > 0;

        /// <summary>Log-ready one-liner. Never null.</summary>
        public string Summary { get; }

        private static string BuildSummary(List<string> promoted, List<string> demoted)
        {
            if (promoted.Count == 0 && demoted.Count == 0) return "major/minor flags already correct";
            return "promoted [" + string.Join(", ", promoted.ToArray()) + "], " +
                   "demoted [" + string.Join(", ", demoted.ToArray()) + "]";
        }
    }

    /// <summary>
    /// Decides which parties are the NA theme's dominant ones, from evidence rather than from
    /// position in a list.
    ///
    /// <para>
    /// <see cref="PartyRegistry.GenerateInitial"/> sets <see cref="Party.IsMajor"/> by taking a prefix
    /// of <c>PartyArchetypes.NaArray</c>, which is majors-first. That is correct at generation and
    /// useless afterwards: it is an ordering convention, not a record, and the first sidecar migration
    /// to reconstruct the flag had to guess from party ids because of it. Ids are the wrong evidence —
    /// a save whose original <c>liberal</c> dissolved would hand the major slot to whichever brand
    /// happened to hold the next-lowest id, which is a fringe party by construction, and
    /// <see cref="FringeFailureModel.Ceilings"/> would then leave it uncapped forever.
    /// </para>
    ///
    /// <para>
    /// <see cref="Party.ArchetypeId"/> is the right evidence, and it has been persisted all along:
    /// the NA majors are exactly the brands generated from <c>liberal</c> and <c>conservative</c>.
    /// A splinter copies its parent's archetype id verbatim (<c>PartyLifecycle</c> does this so the
    /// flavor prompt keeps working), so the archetype alone is not sufficient —
    /// <see cref="Party.PredecessorPartyId"/> is what separates the original brand from its offspring,
    /// and a brand with a predecessor is fringe by definition.
    /// </para>
    ///
    /// <para>
    /// Nothing here draws a random number, reads tuning, or touches the clock. Given the same
    /// candidates in any order it returns the same answer, which is what lets the migration and the
    /// load-time repair agree by construction rather than by hope.
    /// </para>
    /// </summary>
    public static class NaMajorParties
    {
        /// <summary>
        /// The major archetype ids of the live NA catalog — the same prefix
        /// <see cref="PartyRegistry.GenerateInitial"/> takes. Use this on the load path, where the
        /// question is "what is a major today". A migration must NOT use it: a migration reproduces
        /// what a file was written with, and the catalog is free to change.
        /// </summary>
        public static List<string> DefaultMajorArchetypeIds(int majorCount)
        {
            var ids = new List<string>();
            if (majorCount <= 0) return ids;

            IReadOnlyList<PartyArchetype> catalog = PartyArchetypes.Na;
            for (int i = 0; i < catalog.Count && i < majorCount; i++) ids.Add(catalog[i].Id);
            return ids;
        }

        /// <summary>
        /// The rule. Returns the ids that should carry <see cref="Party.IsMajor"/>, ordinal-sorted.
        /// </summary>
        /// <param name="candidates">Every party in the registry, live and dead. Order does not matter.</param>
        /// <param name="majorArchetypeIds">
        /// The archetypes that denote a major, in precedence order. For the NA catalog that is
        /// <c>liberal</c> then <c>conservative</c>.
        /// </param>
        /// <param name="majorCount">
        /// How many majors the theme has — <c>parties.targetCountNa</c>. <b>Zero is the EU branch</b>:
        /// callers pass 0 for EU so that theme knowledge stays at the call sites, where it already
        /// lives, and this function never has to know what a theme is.
        /// </param>
        public static List<string> Reconstruct(IReadOnlyList<MajorCandidate> candidates,
                                               IReadOnlyList<string> majorArchetypeIds,
                                               int majorCount)
        {
            var chosen = new List<string>();

            if (majorCount <= 0) return chosen;
            if (candidates == null || candidates.Count == 0) return chosen;
            if (majorArchetypeIds == null || majorArchetypeIds.Count == 0) return chosen;

            // Sort by id before anything else. Nothing downstream depends on the caller's array order
            // then, which is the determinism rule: sort explicitly rather than inherit an order.
            var ordered = new List<MajorCandidate>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++) ordered.Add(candidates[i]);
            ordered.Sort(CompareCandidateById);

            // Rank the eligible: archetype precedence first so liberal outranks conservative (matching
            // NaArray and the prefix GenerateInitial takes), id second so an original brand outranks a
            // later duplicate. Ids are unique, so this is a total order.
            var ranked = new List<RankedCandidate>();
            for (int i = 0; i < ordered.Count; i++)
            {
                MajorCandidate c = ordered[i];
                if (!Qualifies(c)) continue;

                int archetypeIndex = IndexOfOrdinal(majorArchetypeIds, c.ArchetypeId);
                if (archetypeIndex < 0) continue;

                ranked.Add(new RankedCandidate(archetypeIndex, c.PartyId));
            }
            ranked.Sort(CompareRanked);

            // At most one per archetype. Without this, two brands both claiming `liberal` — which a
            // hand-edited file can contain even though the engine will not produce it — would take
            // both slots and leave conservative unflagged.
            var usedArchetypes = new List<int>();
            for (int i = 0; i < ranked.Count && chosen.Count < majorCount; i++)
            {
                if (usedArchetypes.Contains(ranked[i].ArchetypeIndex)) continue;
                usedArchetypes.Add(ranked[i].ArchetypeIndex);
                chosen.Add(ranked[i].PartyId);
            }

            // Fallback, and only from zero. A file written before archetype ids were persisted has no
            // evidence at all, and zero majors on an NA save is the one catastrophic answer: Ceilings
            // caps every non-major on the ballot, so all-minor pins the entire ballot at baseCeiling.
            // Against that, a guess from id order is an improvement.
            //
            // A PARTIAL answer is never topped up. If the archetypes yield one major, padding the
            // second slot from id order would promote green or populist — strictly worse than having
            // one major, which is a legitimate state: a major can dissolve (ApplyDeaths has no
            // protection for one, and the NA ballot floor is satisfiable by two minors).
            if (chosen.Count == 0)
            {
                for (int i = 0; i < ordered.Count && chosen.Count < majorCount; i++)
                {
                    if (!Qualifies(ordered[i])) continue;
                    chosen.Add(ordered[i].PartyId);
                }
            }

            chosen.Sort(CompareOrdinal);
            return chosen;
        }

        /// <summary>
        /// Reconciles <see cref="Party.IsMajor"/> across a live registry and reports what moved.
        /// Idempotent: running it on its own output changes nothing.
        /// </summary>
        /// <remarks>
        /// Only parties on the ballot are written. Leaving a dissolved brand's flag alone is
        /// load-bearing rather than lazy: <c>PartyLifecycle.ApplyRevivals</c> never restores
        /// <see cref="Party.IsMajor"/>, so clearing a dead major's flag here would bring that brand
        /// back from revival as a minor — a behaviour change well outside the bug this fixes.
        /// <see cref="FringeFailureModel.Ceilings"/> skips off-ballot parties anyway, so the retained
        /// flag is unobservable while the brand is dead and correct again the moment it revives.
        /// </remarks>
        public static MajorRepairResult Repair(IList<Party> parties,
                                               IReadOnlyList<string> majorArchetypeIds,
                                               int majorCount)
        {
            var promoted = new List<string>();
            var demoted = new List<string>();

            if (parties == null || parties.Count == 0) return new MajorRepairResult(promoted, demoted);

            var candidates = new List<MajorCandidate>(parties.Count);
            for (int i = 0; i < parties.Count; i++)
            {
                Party p = parties[i];
                if (p == null || string.IsNullOrEmpty(p.Id)) continue;

                candidates.Add(new MajorCandidate
                {
                    PartyId = p.Id,
                    ArchetypeId = p.ArchetypeId,
                    IsOnBallot = PartyRegistry.IsOnBallot(p),
                    HasPredecessor = !string.IsNullOrEmpty(p.PredecessorPartyId)
                });
            }

            List<string> majors = Reconstruct(candidates, majorArchetypeIds, majorCount);

            for (int i = 0; i < parties.Count; i++)
            {
                Party p = parties[i];
                if (p == null || string.IsNullOrEmpty(p.Id)) continue;
                if (!PartyRegistry.IsOnBallot(p)) continue;

                bool shouldBeMajor = ContainsOrdinal(majors, p.Id);
                if (p.IsMajor == shouldBeMajor) continue;

                p.IsMajor = shouldBeMajor;
                if (shouldBeMajor) promoted.Add(p.Id);
                else demoted.Add(p.Id);
            }

            promoted.Sort(CompareOrdinal);
            demoted.Sort(CompareOrdinal);
            return new MajorRepairResult(promoted, demoted);
        }

        // --- internals ------------------------------------------------------------------------------

        /// <summary>
        /// On the ballot, not a splinter, and actually identifiable. A dead <c>party-01</c> must not
        /// consume a major slot that belongs to a live party — the same reason <c>NextPartyId</c>
        /// counts past dead brands.
        /// </summary>
        private static bool Qualifies(MajorCandidate c) =>
            !string.IsNullOrEmpty(c.PartyId) && c.IsOnBallot && !c.HasPredecessor;

        private struct RankedCandidate
        {
            public RankedCandidate(int archetypeIndex, string partyId)
            {
                ArchetypeIndex = archetypeIndex;
                PartyId = partyId;
            }

            public int ArchetypeIndex { get; }
            public string PartyId { get; }
        }

        private static int CompareRanked(RankedCandidate a, RankedCandidate b)
        {
            if (a.ArchetypeIndex != b.ArchetypeIndex) return a.ArchetypeIndex < b.ArchetypeIndex ? -1 : 1;
            return string.CompareOrdinal(a.PartyId, b.PartyId);
        }

        private static int CompareCandidateById(MajorCandidate a, MajorCandidate b) =>
            string.CompareOrdinal(a.PartyId, b.PartyId);

        private static int CompareOrdinal(string a, string b) => string.CompareOrdinal(a, b);

        /// <summary>Ordinal, never culture-aware — matching <c>PartyArchetypes.Find</c>.</summary>
        private static int IndexOfOrdinal(IReadOnlyList<string> ids, string value)
        {
            if (ids == null || string.IsNullOrEmpty(value)) return -1;
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.CompareOrdinal(ids[i], value) == 0) return i;
            }
            return -1;
        }

        private static bool ContainsOrdinal(List<string> ids, string value) =>
            IndexOfOrdinal(ids, value) >= 0;
    }
}
