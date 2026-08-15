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

        /// <summary>
        /// The brand's canonical name, or <c>""</c> when the name is flavor-owned.
        ///
        /// <para>
        /// A non-empty value is written straight onto <see cref="Party.Name"/> at generation, which
        /// has a deliberate second effect: <c>AgoraRuntime.ApplyProseNames</c> computes
        /// <c>mayRename</c> as "the name is empty, or the canned pool wrote it", so a party that
        /// arrives already named is one the flavor pipeline will never rename. That is the point —
        /// an anchored brand is an institution, not a name to be re-drawn each run. Description and
        /// slogan still move with the politics, because <see cref="PartyIdentity.ApplyFlavor"/>
        /// gates those independently of the rename.
        /// </para>
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The brand's canonical short name, or <c>""</c>. Bounded by
        /// <see cref="PartyIdentity.ShortNameMax"/> — the sidecar schema rejects a longer one, so a
        /// catalog entry that exceeded it would fail the save, not the ballot.
        /// </summary>
        public string ShortName { get; }

        /// <summary>
        /// The brand's canonical colour, normalised, or <c>""</c> when colour comes from
        /// <c>parties.colorPalette</c> by position.
        ///
        /// <para>
        /// Position is the wrong source for a brand whose colour is part of its identity. The NA
        /// catalog lists <c>liberal</c> first and <c>conservative</c> second, and the palette opens
        /// red then blue, so allocating by index handed the liberal party red and the conservative
        /// party blue — inverted for the theme they are modelling, and invisible until someone
        /// looked at a seat chart.
        /// </para>
        /// </summary>
        public string ColorHex { get; }

        /// <summary>
        /// True when this entry is a fixed institution rather than a generated brand: it keeps its
        /// name and colour, and its platform is jittered at <c>parties.anchoredSpreadSigma</c>
        /// instead of <c>parties.archetypeSpreadSigma</c>.
        /// </summary>
        /// <remarks>
        /// A property of the catalog <em>entry</em>, not of the archetype id. The EU catalog's
        /// <c>liberal</c> and the NA catalog's <c>liberal</c> share an id — <see cref="Party.ArchetypeId"/>
        /// is what <see cref="NaMajorParties"/> and the sidecar migration key off, so the ids must
        /// stay equal — but only the NA one is an institution. Anchoring the id rather than the entry
        /// would freeze every EU liberal party into the same brand.
        /// </remarks>
        public bool IsAnchored { get; }

        public PartyArchetype(string id, IssuePosition basePlatform, Issue coreGrievance)
            : this(id, basePlatform, coreGrievance, "", "", "", false)
        {
        }

        public PartyArchetype(string id, IssuePosition basePlatform, Issue coreGrievance,
                              string name, string shortName, string colorHex, bool isAnchored)
        {
            Id = id ?? "";
            BasePlatform = basePlatform.Clamped();
            CoreGrievance = coreGrievance;
            Name = name ?? "";
            ShortName = shortName ?? "";
            ColorHex = PartyIdentity.NormalizeHex(colorHex ?? "");
            IsAnchored = isAnchored;
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
        //
        // These are separate instances from the EU entries they take their stance from, not aliases
        // of them. The NA theme models four named institutions, so each entry carries a fixed name
        // and colour and is anchored; the EU catalog's parties are generated brands and stay
        // flavor-named. The ids and the base platforms are deliberately identical to the EU entries:
        // Party.ArchetypeId is the evidence NaMajorParties.Reconstruct and the sidecar migration key
        // off, and the stance is the same politics either way — only the jitter, the name and the
        // colour differ.
        private static readonly PartyArchetype[] NaArray =
        {
            new PartyArchetype("liberal", EuArray[2].BasePlatform, EuArray[2].CoreGrievance,
                "Democratic Party", "Dem", "#2E86C1", true),        // major — palette blue
            new PartyArchetype("conservative", EuArray[3].BasePlatform, EuArray[3].CoreGrievance,
                "Republican Party", "GOP", "#C0392B", true),        // major — palette red
            new PartyArchetype("green", EuArray[0].BasePlatform, EuArray[0].CoreGrievance,
                "Green Party", "Grn", "#27AE60", true),             // minor
            new PartyArchetype("populist", EuArray[4].BasePlatform, EuArray[4].CoreGrievance,
                "Reform Party", "Ref", "#F1C40F", true)             // minor
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
