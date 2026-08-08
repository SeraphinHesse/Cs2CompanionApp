// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.Collections.Generic;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// The closed set of IDs a flavor response is allowed to reference.
    ///
    /// <para>
    /// Non-negotiable #1 stops the model contributing numbers; this stops it contributing
    /// <i>entities</i>. An article about the "Riverside Greens" is harmless prose right up until
    /// something tries to look that party up, so every ID the model returns is checked against the
    /// engine's own registry and anything unrecognised is dropped with a log line rather than being
    /// carried into state.
    /// </para>
    ///
    /// <para>
    /// IDs are matched with <see cref="StringComparer.Ordinal"/>. Not culture-aware, not
    /// case-insensitive: engine IDs are lowercase kebab-case ASCII produced by the engine itself, and
    /// a culture-sensitive comparison is one Turkish locale away from matching "PARTY-I" to
    /// "party-i".
    /// </para>
    /// </summary>
    public sealed class FlavorCatalog
    {
        private readonly HashSet<string> _partyIds;
        private readonly HashSet<string> _factionIds;
        private readonly HashSet<string> _districtIds;
        private readonly HashSet<string> _eventIds;

        /// <summary>A catalog that recognises nothing. Every referenced ID will be rejected.</summary>
        public static readonly FlavorCatalog Empty = new FlavorCatalog(null, null, null, null);

        public FlavorCatalog(
            IEnumerable<string> partyIds,
            IEnumerable<string> factionIds,
            IEnumerable<string> districtIds,
            IEnumerable<string> eventIds)
        {
            _partyIds = Build(partyIds);
            _factionIds = Build(factionIds);
            _districtIds = Build(districtIds);
            _eventIds = Build(eventIds);
        }

        private static HashSet<string> Build(IEnumerable<string> ids)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (ids != null)
            {
                foreach (string id in ids)
                {
                    if (!string.IsNullOrEmpty(id)) set.Add(id);
                }
            }
            return set;
        }

        public bool HasParty(string id) => !string.IsNullOrEmpty(id) && _partyIds.Contains(id);
        public bool HasFaction(string id) => !string.IsNullOrEmpty(id) && _factionIds.Contains(id);
        public bool HasDistrict(string id) => !string.IsNullOrEmpty(id) && _districtIds.Contains(id);
        public bool HasEvent(string id) => !string.IsNullOrEmpty(id) && _eventIds.Contains(id);

        public int PartyCount => _partyIds.Count;
        public int FactionCount => _factionIds.Count;
        public int DistrictCount => _districtIds.Count;
        public int EventCount => _eventIds.Count;

        /// <summary>
        /// The IDs in ordinal-ascending order, for the prompt.
        /// </summary>
        /// <remarks>
        /// Sorted, not enumerated: a <see cref="HashSet{T}"/> has no defined iteration order, and
        /// while the prompt only feeds an LLM, an unstable prompt makes two otherwise identical runs
        /// impossible to diff when debugging.
        /// </remarks>
        public IReadOnlyList<string> SortedPartyIds() => Sorted(_partyIds);
        public IReadOnlyList<string> SortedFactionIds() => Sorted(_factionIds);
        public IReadOnlyList<string> SortedDistrictIds() => Sorted(_districtIds);
        public IReadOnlyList<string> SortedEventIds() => Sorted(_eventIds);

        private static IReadOnlyList<string> Sorted(HashSet<string> set)
        {
            var list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);
            return list;
        }
    }
}
