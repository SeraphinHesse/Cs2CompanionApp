using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Factions
{
    /// <summary>
    /// Which way a faction leans on the issue it owns, relative to its party.
    /// </summary>
    /// <remarks>
    /// The sign convention is <see cref="IssuePosition"/>'s, and it is fixed: <c>+1</c> is
    /// "spend/protect/restrict more". A <see cref="Champion"/> wants more of its issue than the party
    /// currently offers; <see cref="Restraint"/> wants less.
    /// </remarks>
    public enum FactionDirection
    {
        Restraint = -1,
        Champion = 1
    }

    /// <summary>
    /// One entry of the closed faction-archetype registry: an issue plus a direction.
    /// </summary>
    /// <remarks>
    /// Deliberately not a table of authored platform vectors. A faction's platform is derived from its
    /// party's platform and from the blocs that back it, so the archetype carries no coefficient at
    /// all — it is an identity for the flavor prompt and for the dashboard, nothing more.
    /// </remarks>
    public readonly struct FactionArchetype : IEquatable<FactionArchetype>
    {
        /// <summary>Stable engine id, e.g. <c>"transit-champion"</c>, <c>"growth-restraint"</c>.</summary>
        public string Id { get; }

        public Issue Issue { get; }

        public FactionDirection Direction { get; }

        internal FactionArchetype(string id, Issue issue, FactionDirection direction)
        {
            Id = id;
            Issue = issue;
            Direction = direction;
        }

        /// <summary><c>+1</c> or <c>-1</c>.</summary>
        public int Sign => Direction == FactionDirection.Champion ? 1 : -1;

        public bool Equals(FactionArchetype other) =>
            Issue == other.Issue && Direction == other.Direction;

        public override bool Equals(object? obj) => obj is FactionArchetype other && Equals(other);

        // Not HashCode.Combine — netstandard2.0 does not have it.
        public override int GetHashCode() => ((int)Issue * 2) + (Direction == FactionDirection.Champion ? 0 : 1);

        public override string ToString() => Id;
    }

    /// <summary>
    /// The closed set of faction archetypes: six issues × two directions = twelve.
    /// </summary>
    /// <remarks>
    /// Closed for the same reason <see cref="Issue"/> is: every archetype id that reaches the flavor
    /// prompt or the dashboard must be one the engine can round-trip. An id not in
    /// <see cref="All"/> does not exist.
    /// </remarks>
    public static class FactionArchetypes
    {
        public const string ChampionSuffix = "-champion";
        public const string RestraintSuffix = "-restraint";

        private static readonly FactionArchetype[] AllArray = Build();

        private static FactionArchetype[] Build()
        {
            var list = new FactionArchetype[Issues.Count * 2];
            int i = 0;
            for (int n = 0; n < Issues.All.Count; n++)
            {
                Issue issue = Issues.All[n];
                string key = Issues.ToKey(issue);
                list[i++] = new FactionArchetype(key + ChampionSuffix, issue, FactionDirection.Champion);
                list[i++] = new FactionArchetype(key + RestraintSuffix, issue, FactionDirection.Restraint);
            }
            return list;
        }

        /// <summary>All twelve, issue-major (in <see cref="Issues.All"/> order), champion before
        /// restraint. Iterate this, never a dictionary.</summary>
        public static IReadOnlyList<FactionArchetype> All => AllArray;

        public static FactionArchetype For(Issue issue, FactionDirection direction)
        {
            int index = IndexOf(issue) * 2 + (direction == FactionDirection.Champion ? 0 : 1);
            return AllArray[index];
        }

        /// <summary>Convenience for callers holding a raw sign.</summary>
        public static FactionArchetype For(Issue issue, int sign) =>
            For(issue, sign >= 0 ? FactionDirection.Champion : FactionDirection.Restraint);

        public static bool TryGet(string? archetypeId, out FactionArchetype archetype)
        {
            for (int i = 0; i < AllArray.Length; i++)
            {
                if (string.CompareOrdinal(AllArray[i].Id, archetypeId ?? "") == 0)
                {
                    archetype = AllArray[i];
                    return true;
                }
            }
            archetype = default(FactionArchetype);
            return false;
        }

        private static int IndexOf(Issue issue)
        {
            for (int i = 0; i < Issues.All.Count; i++)
                if (Issues.All[i] == issue) return i;
            throw new ArgumentOutOfRangeException(nameof(issue), issue, "Unknown issue.");
        }
    }
}
