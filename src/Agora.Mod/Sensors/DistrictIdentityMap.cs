using System;
using System.Collections.Generic;
using System.Globalization;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// Assigns each district the stable string id the engine uses — as a snapshot key, as a sort
    /// key, and, most importantly, as the <c>entityId</c> argument to
    /// <c>SeedStreams.RngFor(saveGuid, date, stream, entityId)</c>.
    ///
    /// <para>
    /// That last use is why this is not an afterthought. If a district's id changed between two
    /// loads of the same save, every per-district draw would change with it and one city would
    /// produce different politics each launch. The id therefore derives from the district entity's
    /// ECS index, which the game serialises with the save, and it is zero-padded so ordinal sorting
    /// and numeric ordering agree — <c>d00000009</c> before <c>d00000010</c>, not after it.
    /// </para>
    ///
    /// <para>
    /// <b>Pinning.</b> The persistence packet may already know an id for a district from the sidecar.
    /// <see cref="Pin"/> lets it install that mapping before the first capture, so an id survives
    /// even if the underlying entity index does not. Nothing here writes to the sidecar; this type
    /// only holds the mapping.
    /// </para>
    ///
    /// <para>Pure — no game types, keyed by a plain <c>int</c>, so it is testable as it stands.</para>
    /// </summary>
    public sealed class DistrictIdentityMap
    {
        private readonly Dictionary<int, string> _byEntityIndex = new Dictionary<int, string>();

        /// <summary>Prefix on every generated id. Keeps ids self-describing in logs and the sidecar.</summary>
        public const string Prefix = "d";

        /// <summary>
        /// Installs a known id for an entity index, overriding the derived one. Intended for load
        /// reconciliation; ignored when either argument is empty.
        /// </summary>
        public void Pin(int entityIndex, string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            _byEntityIndex[entityIndex] = id;
        }

        /// <summary>The id for a district entity index, deriving and remembering one if needed.</summary>
        public string Resolve(int entityIndex)
        {
            string id;
            if (_byEntityIndex.TryGetValue(entityIndex, out id)) return id;

            id = Derive(entityIndex);
            _byEntityIndex[entityIndex] = id;
            return id;
        }

        /// <summary>
        /// The id an entity index maps to by default. Deterministic and side-effect free — the same
        /// index always yields the same id, on any machine, in any culture.
        /// </summary>
        public static string Derive(int entityIndex)
        {
            // Negative indices never occur in practice; folding them keeps the id well-formed rather
            // than producing "d-0000042" and a sort order nobody expects.
            long value = entityIndex < 0 ? (long)uint.MaxValue + entityIndex : entityIndex;
            return Prefix + value.ToString("D8", CultureInfo.InvariantCulture);
        }

        /// <summary>Every mapping currently held, ordered by id. Diagnostics and persistence only.</summary>
        public IList<KeyValuePair<int, string>> Entries()
        {
            var entries = new List<KeyValuePair<int, string>>(_byEntityIndex);
            entries.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
            return entries;
        }

        /// <summary>Forgets every mapping. Called when a different save is loaded.</summary>
        public void Clear() => _byEntityIndex.Clear();
    }
}
