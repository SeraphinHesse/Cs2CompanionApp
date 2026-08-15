using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Parties
{
    /// <summary>What <see cref="AnchoredBrandRepair.Apply"/> changed, if anything.</summary>
    public sealed class BrandRepairResult
    {
        internal BrandRepairResult(List<string> renamed, List<string> recoloured, List<string> displaced)
        {
            Renamed = renamed;
            Recoloured = recoloured;
            Displaced = displaced;
        }

        /// <summary>Parties given their archetype's name. Ordinal-sorted.</summary>
        public List<string> Renamed { get; }

        /// <summary>Parties given their archetype's colour. Ordinal-sorted.</summary>
        public List<string> Recoloured { get; }

        /// <summary>
        /// Parties moved off a colour an anchored brand reclaimed. Not a repair in itself — the
        /// consequence of one, and worth logging separately so a player who sees a third party change
        /// colour can tell it was a knock-on rather than a second bug.
        /// </summary>
        public List<string> Displaced { get; }

        public bool Changed => Renamed.Count > 0 || Recoloured.Count > 0 || Displaced.Count > 0;

        /// <summary>Log-ready one-liner. Never null.</summary>
        public string Summary
        {
            get
            {
                if (!Changed) return "anchored brand identities already correct";
                return "renamed [" + string.Join(", ", Renamed.ToArray()) + "], " +
                       "recoloured [" + string.Join(", ", Recoloured.ToArray()) + "], " +
                       "displaced [" + string.Join(", ", Displaced.ToArray()) + "]";
            }
        }
    }

    /// <summary>
    /// Gives an existing save's anchored brands the identity they would have been generated with.
    ///
    /// <para>
    /// The anchored-brand change is generation-time: <c>PartyRegistry.GenerateInitial</c> runs once,
    /// at save creation, so a save made before it landed keeps a liberal party wearing the palette's
    /// red, a conservative party wearing its blue, and whatever names the flavor pipeline invented.
    /// Nothing else in the engine would ever correct that, because nothing else writes a party's
    /// identity. This is the load-time repair that does, in the same spirit as
    /// <see cref="NaMajorParties.Repair"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Identity only. Platforms are deliberately not touched.</b> A platform is history: it has
    /// moved with <c>platformDriftPerCycle</c> and with every campaign's manifesto refresh since the
    /// save began, and blocs' <c>PreviousVote</c> records were taken against those positions.
    /// Overwriting it would rewrite the political record to match a party the city never voted on,
    /// which is a far worse outcome than a brand whose founding jitter was wider than it should have
    /// been. An existing save's anchored parties therefore keep the stance they have always had; only
    /// a theme change, which regenerates the whole registry, re-rolls them under the tight sigma.
    /// </para>
    ///
    /// <para>
    /// Pure and deterministic: no seeds, no clock. Sorted by party id before anything is written, so
    /// the result cannot depend on the order the registry happens to be in.
    /// </para>
    /// </summary>
    public static class AnchoredBrandRepair
    {
        /// <summary>
        /// Repairs every party generated from an anchored entry of <paramref name="catalog"/>.
        /// </summary>
        /// <param name="parties">The whole registry, live and dead. Mutated in place.</param>
        /// <param name="catalog">
        /// The theme's archetype catalog. An EU catalog anchors nothing, so this is a no-op on an EU
        /// save without the caller needing to know that.
        /// </param>
        /// <param name="tuning">Supplies the palette that displaced parties are reassigned from.</param>
        /// <remarks>
        /// <para>
        /// <b>Player locks win.</b> A party the player renamed carries
        /// <see cref="PartyOverrides.NameLocked"/> and keeps its name; the same for
        /// <see cref="PartyOverrides.ColorLocked"/>. The repair exists to undo the engine's mistake,
        /// not the player's choice.
        /// </para>
        /// <para>
        /// <b>Splinters are not the institution.</b> A breakaway copies its parent's
        /// <see cref="Party.ArchetypeId"/> verbatim so the flavor prompt keeps working, so archetype
        /// alone would match it too — <see cref="Party.PredecessorPartyId"/> is what separates the
        /// original brand from its offspring, exactly as in <see cref="NaMajorParties.Reconstruct"/>.
        /// </para>
        /// <para>
        /// <b>The colour fix is a swap, not an assignment.</b> The bug it undoes handed liberal the
        /// colour conservative should have had and vice versa, so writing one target at a time would
        /// collide halfway through. Every anchored target is written first, and only then is any other
        /// party still holding one of those colours moved off it.
        /// </para>
        /// </remarks>
        // List<Party> rather than IList<Party>: pass 2 hands the whole registry to
        // PartyRegistry.RegenerateColor, which takes IReadOnlyList<Party>, and IList<T> does not
        // derive from IReadOnlyList<T>. Every caller already passes PoliticalState.Parties or a
        // List<Party> built for a test, so narrowing the parameter costs nothing and keeps the
        // taken-colour set the full registry rather than a filtered copy of it.
        public static BrandRepairResult Apply(List<Party> parties,
                                              IReadOnlyList<PartyArchetype> catalog,
                                              EngineTuning tuning)
        {
            var renamed = new List<string>();
            var recoloured = new List<string>();
            var displaced = new List<string>();

            if (parties == null || parties.Count == 0 || catalog == null || tuning == null)
                return new BrandRepairResult(renamed, recoloured, displaced);

            // A total order before any write, so two machines repairing the same save agree.
            var ordered = new List<Party>();
            for (int i = 0; i < parties.Count; i++)
            {
                if (parties[i] != null && !string.IsNullOrEmpty(parties[i].Id)) ordered.Add(parties[i]);
            }
            ordered.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            var claimed = new List<string>();

            // --- pass 1: write every anchored identity ---------------------------------------------
            for (int i = 0; i < ordered.Count; i++)
            {
                Party party = ordered[i];
                PartyArchetype archetype = MatchingAnchoredArchetype(party, catalog);
                if (archetype == null) continue;

                if (!string.IsNullOrEmpty(archetype.Name) &&
                    (party.PlayerOverrides & PartyOverrides.NameLocked) == 0 &&
                    string.CompareOrdinal(party.Name, archetype.Name) != 0)
                {
                    party.Name = archetype.Name;
                    party.ShortName = archetype.ShortName;
                    renamed.Add(party.Id);
                }

                if (!PartyIdentity.IsValidHex(archetype.ColorHex)) continue;
                if ((party.PlayerOverrides & PartyOverrides.ColorLocked) != 0) continue;

                claimed.Add(archetype.ColorHex);

                if (string.CompareOrdinal(PartyIdentity.NormalizeHex(party.ColorHex), archetype.ColorHex) == 0)
                    continue;

                party.ColorHex = archetype.ColorHex;
                recoloured.Add(party.Id);
            }

            // --- pass 2: move anyone else off a reclaimed colour -----------------------------------
            for (int i = 0; i < ordered.Count; i++)
            {
                Party party = ordered[i];
                if (MatchingAnchoredArchetype(party, catalog) != null) continue;
                if ((party.PlayerOverrides & PartyOverrides.ColorLocked) != 0) continue;

                string held = PartyIdentity.NormalizeHex(party.ColorHex);
                if (!ContainsOrdinal(claimed, held)) continue;

                // Clear it first. RegenerateColor excludes this party's own colour from the
                // taken-set — which is right when it is merely reshuffling one party, and wrong here:
                // the colour it would be excluding is precisely the one that now belongs to somebody
                // else, so it would be free to hand it straight back and the duplicate would survive.
                party.ColorHex = "";
                party.ColorHex = PartyRegistry.RegenerateColor(parties, party.Id, tuning);
                displaced.Add(party.Id);
            }

            renamed.Sort(CompareOrdinal);
            recoloured.Sort(CompareOrdinal);
            displaced.Sort(CompareOrdinal);
            return new BrandRepairResult(renamed, recoloured, displaced);
        }

        /// <summary>
        /// The anchored catalog entry this party was generated from, or null. A party with a
        /// predecessor is a splinter and never matches, however its archetype id reads.
        /// </summary>
        private static PartyArchetype MatchingAnchoredArchetype(Party party,
                                                                IReadOnlyList<PartyArchetype> catalog)
        {
            if (party == null) return null;
            if (!string.IsNullOrEmpty(party.PredecessorPartyId)) return null;
            if (string.IsNullOrEmpty(party.ArchetypeId)) return null;

            for (int i = 0; i < catalog.Count; i++)
            {
                PartyArchetype entry = catalog[i];
                if (entry == null || !entry.IsAnchored) continue;
                if (string.CompareOrdinal(entry.Id, party.ArchetypeId) == 0) return entry;
            }
            return null;
        }

        private static bool ContainsOrdinal(List<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.CompareOrdinal(values[i], value) == 0) return true;
            }
            return false;
        }

        private static int CompareOrdinal(string a, string b) => string.CompareOrdinal(a, b);
    }
}
