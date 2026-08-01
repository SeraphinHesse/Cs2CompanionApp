using System;
using System.Collections.Generic;
using System.Globalization;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Factions
{
    /// <summary>
    /// Allocation and comparison of engine-owned faction ids.
    ///
    /// <para>
    /// Ids are the entity id fed to <c>SeedStreams.RngFor</c>, so they must be stable for the life of
    /// the save and must never be an index into a list that can be reordered. They are also the sort
    /// key for <c>PoliticalState.Factions</c>, which the contract fixes as "by Id".
    /// </para>
    /// </summary>
    public static class FactionIds
    {
        public const string Prefix = "faction-";

        /// <summary>
        /// <c>faction-01</c>, <c>faction-07</c>, … Two-digit zero padding matches the contract's
        /// example. Past ordinal 99 the id keeps growing (<c>faction-100</c>); ordinal ordering and
        /// ordinal-string ordering diverge there, which is cosmetic only — every comparison in this
        /// packet needs a *total* order, not a chronological one.
        /// </summary>
        public static string Format(int ordinal)
        {
            if (ordinal < 0)
                throw new ArgumentOutOfRangeException(nameof(ordinal), ordinal, "Faction ordinal must be non-negative.");
            return Prefix + ordinal.ToString("D2", CultureInfo.InvariantCulture);
        }

        public static bool TryParseOrdinal(string? id, out int ordinal)
        {
            ordinal = 0;
            if (string.IsNullOrEmpty(id)) return false;
            string s = id!;
            if (s.Length <= Prefix.Length) return false;
            if (string.CompareOrdinal(s, 0, Prefix, 0, Prefix.Length) != 0) return false;
            return int.TryParse(s.Substring(Prefix.Length), NumberStyles.None,
                                CultureInfo.InvariantCulture, out ordinal);
        }

        /// <summary>
        /// The next free ordinal, given every faction that has ever existed in the save (including
        /// dissolved and merged ones — an id is never recycled, because a recycled id would collide
        /// with the dead faction's seed sub-stream).
        /// </summary>
        public static int NextOrdinal(IEnumerable<Faction>? existing)
        {
            int max = 0;
            if (existing != null)
            {
                foreach (Faction f in existing)
                {
                    if (f == null) continue;
                    int o;
                    if (TryParseOrdinal(f.Id, out o) && o > max) max = o;
                }
            }
            return max + 1;
        }

        /// <summary>Ordinal string comparison. Culture-invariant on purpose: culture-sensitive
        /// comparison would make list order depend on the player's locale.</summary>
        public static int Compare(string? a, string? b) => string.CompareOrdinal(a ?? "", b ?? "");

        internal static readonly Comparison<Faction> ByIdComparison =
            (a, b) => Compare(a == null ? "" : a.Id, b == null ? "" : b.Id);
    }
}
