using System;
using System.Collections.Generic;
using System.Globalization;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Parties
{
    /// <summary>
    /// The party registry: generation of the opening field, and the stateless helpers every other
    /// packet needs to reason about a party list (id allocation, colour allocation, ballot
    /// membership, ordering, incumbency weight).
    ///
    /// <para>
    /// Deliberately a static class of pure functions rather than an object that owns the party list.
    /// The single owner of party state is <see cref="PoliticalState.Parties"/>; a second stateful
    /// registry would be a second source of truth and the two would drift on load.
    /// </para>
    /// </summary>
    public static class PartyRegistry
    {
        private const string IdPrefix = "party-";

        /// <summary>Fallback colour when the palette is empty. Never a party's real colour.</summary>
        public const string NeutralColorHex = "#808080";

        private static readonly Party[] NoParties = new Party[0];

        // --- Ordering and lookup ------------------------------------------------------------------

        /// <summary>
        /// Ordinal comparison on <see cref="Party.Id"/>. The contract sort key for every party list;
        /// ordinal (not culture-aware) because a culture-sensitive sort would reorder history when
        /// the player changes locale.
        /// </summary>
        public static int CompareById(Party a, Party b) => string.CompareOrdinal(a.Id, b.Id);

        /// <summary>A copy sorted by <see cref="Party.Id"/> ordinal ascending.</summary>
        public static List<Party> SortedById(IEnumerable<Party> parties)
        {
            var list = new List<Party>(parties ?? NoParties);
            list.Sort(CompareById);
            return list;
        }

        /// <summary>First party with this id, or null. Ordinal comparison.</summary>
        public static Party? Find(IReadOnlyList<Party> parties, string partyId)
        {
            if (parties == null || partyId == null) return null;
            for (int i = 0; i < parties.Count; i++)
            {
                if (string.CompareOrdinal(parties[i].Id, partyId) == 0) return parties[i];
            }
            return null;
        }

        /// <summary>
        /// True while the party contests elections. <see cref="PartyStatus.Endangered"/> parties are
        /// still on the ballot — that is what makes the second sub-threshold result possible — and
        /// <see cref="PartyStatus.Revived"/> is active for every electoral purpose.
        /// </summary>
        public static bool IsOnBallot(Party party) =>
            party != null &&
            (party.Status == PartyStatus.Active ||
             party.Status == PartyStatus.Endangered ||
             party.Status == PartyStatus.Revived);

        /// <summary>How many parties currently contest elections.</summary>
        public static int OnBallotCount(IReadOnlyList<Party> parties)
        {
            if (parties == null) return 0;
            int count = 0;
            for (int i = 0; i < parties.Count; i++)
            {
                if (IsOnBallot(parties[i])) count++;
            }
            return count;
        }

        /// <summary>The status a healthy party carries: <c>Revived</c> once it has come back, else <c>Active</c>.</summary>
        public static PartyStatus HealthyStatus(Party party) =>
            party != null && party.RevivalCount > 0 ? PartyStatus.Revived : PartyStatus.Active;

        // --- Identity ------------------------------------------------------------------------------

        /// <summary>
        /// The next free <c>party-NN</c> id. Derived from the highest numeric suffix in use — never
        /// from the list length, so ids stay unique after a brand dissolves and a new one is founded.
        /// </summary>
        public static string NextPartyId(IReadOnlyList<Party> existing)
        {
            int highest = 0;
            if (existing != null)
            {
                for (int i = 0; i < existing.Count; i++)
                {
                    int n = OrdinalOf(existing[i].Id);
                    if (n > highest) highest = n;
                }
            }
            return FormatId(highest + 1);
        }

        /// <summary>Formats a 1-based party ordinal as <c>party-01</c>.</summary>
        public static string FormatId(int ordinal) =>
            IdPrefix + ordinal.ToString("D2", CultureInfo.InvariantCulture);

        /// <summary>
        /// The numeric suffix of a <c>party-NN</c> id, or 0 when the id is empty, foreign or
        /// unparseable. Public because the ordinal is also a party's <i>preferred palette slot</i>
        /// (see <see cref="RegenerateColor"/>), which is not a fact the registry can keep to itself.
        /// </summary>
        public static int OrdinalOf(string id)
        {
            if (string.IsNullOrEmpty(id) || !id.StartsWith(IdPrefix, StringComparison.Ordinal)) return 0;
            string tail = id.Substring(IdPrefix.Length);
            int value;
            return int.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        /// <summary>
        /// First palette colour not already taken, scanning from <paramref name="preferredIndex"/>.
        /// Colours are held for the life of the brand, dissolved brands included, so a revived party
        /// comes back the colour the player remembers.
        /// </summary>
        public static string AllocateColor(IReadOnlyList<Party> existing, int preferredIndex, EngineTuning tuning)
        {
            return AllocateColor(existing, preferredIndex, tuning, null);
        }

        /// <summary>
        /// The shared allocation core. <paramref name="excludingPartyId"/> is the one party whose
        /// current colour does not count as taken — needed only when reallocating a colour to a party
        /// that already has one.
        /// </summary>
        private static string AllocateColor(IReadOnlyList<Party> existing, int preferredIndex,
                                            EngineTuning tuning, string? excludingPartyId)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            string[] palette = tuning.Parties.ColorPalette;
            if (palette == null || palette.Length == 0) return NeutralColorHex;

            int start = preferredIndex % palette.Length;
            if (start < 0) start += palette.Length;

            for (int offset = 0; offset < palette.Length; offset++)
            {
                string candidate = palette[(start + offset) % palette.Length];
                if (!IsColorTaken(existing, candidate, excludingPartyId)) return candidate;
            }
            return palette[start];
        }

        /// <summary>
        /// Whether any party already wears this colour, comparing on
        /// <see cref="PartyIdentity.NormalizeHex"/> of both sides. Dissolved and merged brands count:
        /// a colour is held for the life of the brand, so a revived party comes back the colour the
        /// player remembers.
        /// </summary>
        /// <param name="excludingPartyId">
        /// A party to skip, ordinal match on <see cref="Party.Id"/>. Null or empty excludes nothing.
        /// </param>
        /// <remarks>
        /// The normalisation is a fix, not tidying. This check used to compare raw
        /// <see cref="string.CompareOrdinal(string,string)"/>, so <c>#c0392b</c> as a player typed it
        /// was byte-different from the palette's <c>#C0392B</c> and simply did not register as taken —
        /// and the next splinter was handed the identical-looking colour, leaving two indistinguishable
        /// slices on every chart.
        /// </remarks>
        public static bool IsColorTaken(IReadOnlyList<Party> parties, string colorHex, string? excludingPartyId)
        {
            if (parties == null) return false;

            string wanted = PartyIdentity.NormalizeHex(colorHex);
            bool excluding = !string.IsNullOrEmpty(excludingPartyId);

            for (int i = 0; i < parties.Count; i++)
            {
                Party p = parties[i];
                if (p == null) continue;
                if (excluding && string.CompareOrdinal(p.Id, excludingPartyId) == 0) continue;
                if (string.CompareOrdinal(PartyIdentity.NormalizeHex(p.ColorHex), wanted) == 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Reassigns a party its palette colour from <i>today's</i> registry: the first free entry
        /// scanning from the slot its ordinal originally drew from.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>This is not a restore, and the name says so.</b> It reallocates against the roster as it
        /// stands now, so if the party's launch colour has since gone to another brand — a splinter
        /// founded while the player was wearing a custom colour, say — the party comes back a
        /// different one. A method called <c>RestoreColor</c> would be a lie, and the player would
        /// read the difference as the reset having failed.
        /// </para>
        /// <para>
        /// The party is excluded from the taken-colour scan. Without that its own current colour
        /// blocks its own preferred slot, and a reset could never give the slot back — the one case
        /// this method exists for.
        /// </para>
        /// </remarks>
        public static string RegenerateColor(IReadOnlyList<Party> parties, string partyId, EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            // GenerateInitial allocated party-NN from index NN-1: `AllocateColor(parties, i, tuning)`
            // beside `Id = FormatId(i + 1)`. An unparseable id has ordinal 0 and starts at the top.
            int preferred = OrdinalOf(partyId) - 1;
            if (preferred < 0) preferred = 0;

            return AllocateColor(parties, preferred, tuning, partyId);
        }

        /// <summary>
        /// A field-by-field copy. Every function in this packet is pure, which means cloning before
        /// mutating: <see cref="Party"/> is a mutable class and the caller's list must be untouched.
        /// </summary>
        public static Party Clone(Party source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return new Party
            {
                Id = source.Id,
                Name = source.Name,
                ShortName = source.ShortName,
                Description = source.Description,
                Slogan = source.Slogan,
                ColorHex = source.ColorHex,
                ArchetypeId = source.ArchetypeId,
                Platform = source.Platform,
                LastManifesto = source.LastManifesto,
                Status = source.Status,
                FoundedDate = source.FoundedDate,
                DissolvedDate = source.DissolvedDate,
                LastVoteShare = source.LastVoteShare,
                SeatsHeld = source.SeatsHeld,
                IsIncumbent = source.IsIncumbent,
                IsInGovernment = source.IsInGovernment,
                ConsecutiveElectionsBelowThreshold = source.ConsecutiveElectionsBelowThreshold,
                PredecessorPartyId = source.PredecessorPartyId,
                SuccessorPartyId = source.SuccessorPartyId,
                FactionIds = new List<string>(source.FactionIds),
                CoreGrievance = source.CoreGrievance,
                RevivalCount = source.RevivalCount,

                // Set once at generation and never recomputed, so dropping it here would quietly
                // demote both NA majors to fringe at the first lifecycle pass — and the fringe
                // ceiling would then pin the entire ballot at 3%.
                IsMajor = source.IsMajor,

                // Player-owned, so easy to forget in a field-by-field copy — and dropping it here
                // silently un-locks a party the player renamed, which reads as the rename simply
                // coming back a few months later.
                PlayerOverrides = source.PlayerOverrides
            };
        }

        /// <summary>Clones a whole list and returns it sorted by id.</summary>
        public static List<Party> CloneAll(IReadOnlyList<Party> parties)
        {
            var list = new List<Party>();
            if (parties != null)
            {
                for (int i = 0; i < parties.Count; i++) list.Add(Clone(parties[i]));
            }
            list.Sort(CompareById);
            return list;
        }

        // --- Incumbency ----------------------------------------------------------------------------

        /// <summary>
        /// The incumbency bonus a party still carries after <paramref name="termsInPower"/> terms:
        /// <c>parties.incumbencyBonus</c> decayed by <c>parties.incumbencyDecayPerTerm</c> for each
        /// term after the first. Zero terms means no bonus at all.
        /// </summary>
        /// <remarks>
        /// This is the <i>party-side</i> incumbency curve. The affinity packet has its own
        /// <c>affinity.incumbencyBonus</c> for the per-bloc term; the two are different coefficients
        /// on purpose and must not be conflated.
        /// </remarks>
        public static double IncumbencyBonus(int termsInPower, EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            if (termsInPower <= 0) return 0.0;

            double retained = 1.0 - tuning.Parties.IncumbencyDecayPerTerm;
            if (retained < 0.0) retained = 0.0;

            return tuning.Parties.IncumbencyBonus * Math.Pow(retained, termsInPower - 1);
        }

        // --- Generation ----------------------------------------------------------------------------

        /// <summary>
        /// The opening field of parties for a new save. Pure: identical arguments always produce an
        /// identical list, including colours and ids.
        /// </summary>
        /// <param name="archetypes">
        /// Catalog to draw from, in pick order. Null uses <see cref="PartyArchetypes.For"/>.
        /// </param>
        /// <returns>Parties sorted by <see cref="Party.Id"/>, all <see cref="PartyStatus.Active"/>.</returns>
        public static List<Party> GenerateInitial(Guid saveGuid, SimDate date, RegionTheme theme,
                                                  EngineTuning tuning,
                                                  IReadOnlyList<PartyArchetype>? archetypes = null)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            IReadOnlyList<PartyArchetype> catalog = archetypes ?? PartyArchetypes.For(theme);
            PartiesTuning t = tuning.Parties;

            int wanted = theme == RegionTheme.Na
                ? t.TargetCountNa + t.MinorPartyCountNa
                : ClampInt(t.TargetCountEu, t.MinCountEu, t.MaxCountEu);

            if (wanted > t.MaxPartiesTotal) wanted = t.MaxPartiesTotal;
            if (wanted > catalog.Count) wanted = catalog.Count;
            if (wanted < 0) wanted = 0;

            var parties = new List<Party>();
            for (int i = 0; i < wanted; i++)
            {
                PartyArchetype archetype = catalog[i];
                var rng = SeedStreams.RngFor(saveGuid, date, StreamNames.PartyGeneration, archetype.Id);

                IssuePosition platform = PartyPlatform.Instantiate(archetype, rng, t.ArchetypeSpreadSigma);

                // Keep the ballot legible: nudge away from every party already placed. Earlier
                // parties are never moved, so the result does not depend on placement order beyond
                // the catalog order itself. Re-checked from the start after any nudge, because
                // pushing away from one neighbour can walk into another.
                for (int pass = 0; pass < 8; pass++)
                {
                    bool settled = true;
                    for (int j = 0; j < parties.Count; j++)
                    {
                        if (platform.Distance(parties[j].Platform) >= t.MinPlatformDistance) continue;

                        platform = PartyPlatform.SeparateFrom(platform, parties[j].Platform,
                                                              t.MinPlatformDistance, archetype.CoreGrievance, rng);
                        settled = false;
                    }
                    if (settled) break;
                }

                parties.Add(new Party
                {
                    Id = FormatId(i + 1),
                    ColorHex = AllocateColor(parties, i, tuning),
                    ArchetypeId = archetype.Id,
                    Platform = platform,
                    LastManifesto = platform,
                    Status = PartyStatus.Active,
                    FoundedDate = date,
                    CoreGrievance = archetype.CoreGrievance,
                    // The NA catalog lists its two dominant parties first precisely so this prefix
                    // test works; see PartyArchetypes.NaArray. EU has no majors.
                    IsMajor = theme == RegionTheme.Na && i < t.TargetCountNa
                });
            }

            parties.Sort(CompareById);
            return parties;
        }

        // netstandard2.0 has no Math.Clamp. A min above max resolves to max, never throws.
        internal static int ClampInt(int v, int min, int max)
        {
            if (max < min) max = min;
            return v < min ? min : (v > max ? max : v);
        }
    }
}
