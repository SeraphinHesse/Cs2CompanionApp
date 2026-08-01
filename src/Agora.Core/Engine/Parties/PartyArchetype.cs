using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Parties
{
    /// <summary>
    /// The template a party brand is generated from: a base stance and the grievance the brand owns.
    ///
    /// <para>
    /// An archetype is <b>content</b>, not tuning — the same class of thing as the name pools in
    /// <c>data/seeds/</c>. It is deliberately injectable everywhere in this packet
    /// (<see cref="PartyRegistry.GenerateInitial"/>, <see cref="PartyLifecycleInput.Archetypes"/>) so
    /// the catalog can move to <c>data/seeds/party_archetypes.json</c> in M3 without touching engine
    /// code. <see cref="PartyArchetypes"/> is the built-in fallback catalog until then.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <see cref="Id"/> lands on <see cref="Party.ArchetypeId"/> and is engine-owned: it drives the
    /// initial platform and is an input to the flavor prompt. It is never parsed for meaning by the
    /// engine beyond identity comparison.
    /// </remarks>
    public sealed class PartyArchetype
    {
        /// <summary>Stable kebab-case id, e.g. <c>"green"</c>. Engine-owned.</summary>
        public string Id { get; }

        /// <summary>The stance a party of this archetype starts from, before seeded jitter.</summary>
        public IssuePosition BasePlatform { get; }

        /// <summary>The issue this brand owns. Drives revival when that grievance resurges.</summary>
        public Issue CoreGrievance { get; }

        public PartyArchetype(string id, IssuePosition basePlatform, Issue coreGrievance)
        {
            Id = id ?? "";
            BasePlatform = basePlatform.Clamped();
            CoreGrievance = coreGrievance;
        }
    }

    /// <summary>
    /// The built-in party archetype catalogs. Ordered, never a dictionary — position in this list is
    /// the deterministic pick order for generation and for new-party entry.
    /// </summary>
    /// <remarks>
    /// Sign convention (<see cref="IssuePosition"/>): <c>+1</c> = spend / protect / restrict more.
    /// The six named in the <see cref="Party.ArchetypeId"/> contract doc come first, in that order;
    /// the last three exist so that <c>parties.maxPartiesTotal</c> (9) can be reached and so that a
    /// new-entry roll always has an unused brand available while a dissolved brand waits to revive.
    /// Every one of the six <see cref="Issue"/> values is some archetype's core grievance.
    /// </remarks>
    public static class PartyArchetypes
    {
        private static readonly PartyArchetype[] EuArray =
        {
            //                                        services  cost   env   transit growth heritage
            new PartyArchetype("green",
                new IssuePosition(0.40, 0.10, 0.90, 0.70, -0.60, -0.10), Issue.Environment),
            new PartyArchetype("labour",
                new IssuePosition(0.90, 0.80, 0.20, 0.50, 0.20, -0.20), Issue.Services),
            new PartyArchetype("liberal",
                new IssuePosition(-0.30, -0.40, 0.00, -0.10, 0.80, -0.40), Issue.Growth),
            new PartyArchetype("conservative",
                new IssuePosition(-0.20, -0.30, -0.30, -0.40, 0.10, 0.80), Issue.HeritageOrder),
            new PartyArchetype("populist",
                new IssuePosition(0.50, 0.90, -0.40, -0.20, -0.30, 0.60), Issue.CostOfLiving),
            new PartyArchetype("localist",
                new IssuePosition(0.20, 0.30, 0.50, 0.10, -0.80, 0.50), Issue.Growth),
            new PartyArchetype("commuter",
                new IssuePosition(0.30, 0.20, 0.30, 0.90, 0.40, -0.20), Issue.Transit),
            new PartyArchetype("motorist",
                new IssuePosition(-0.10, 0.40, -0.50, -0.90, 0.30, 0.20), Issue.Transit),
            new PartyArchetype("civic",
                new IssuePosition(0.70, -0.20, 0.40, 0.40, 0.50, 0.10), Issue.Services)
        };

        // NA runs two dominant parties plus two minors (parties.targetCountNa + minorPartyCountNa);
        // the majors come first so PartyRegistry.GenerateInitial can take a prefix.
        private static readonly PartyArchetype[] NaArray =
        {
            EuArray[2], // liberal   — major
            EuArray[3], // conservative — major
            EuArray[0], // green     — minor
            EuArray[4]  // populist  — minor
        };

        /// <summary>EU catalog, in pick order.</summary>
        public static IReadOnlyList<PartyArchetype> Eu => EuArray;

        /// <summary>NA catalog, majors first, in pick order.</summary>
        public static IReadOnlyList<PartyArchetype> Na => NaArray;

        /// <summary>The default catalog for a theme.</summary>
        public static IReadOnlyList<PartyArchetype> For(RegionTheme theme) =>
            theme == RegionTheme.Na ? Na : Eu;

        /// <summary>First archetype with this id, or null. Ordinal comparison, never culture-aware.</summary>
        public static PartyArchetype? Find(IReadOnlyList<PartyArchetype> catalog, string id)
        {
            if (catalog == null || id == null) return null;
            for (int i = 0; i < catalog.Count; i++)
            {
                if (string.CompareOrdinal(catalog[i].Id, id) == 0) return catalog[i];
            }
            return null;
        }
    }
}
