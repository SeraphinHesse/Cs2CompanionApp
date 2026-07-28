using System;
using System.Text;
using Agora.Core.Contracts;

namespace Agora.Core.Determinism
{
    /// <summary>
    /// The determinism kernel. Non-negotiable #2: every stochastic draw in Agora comes from a named,
    /// seeded stream derived as <c>Hash(saveGuid, simDate, streamName)</c>.
    ///
    /// <para>
    /// Because the seed is a pure function of those three inputs, replaying a save reproduces the
    /// same political outcomes — which is what makes save-scumming converge rather than diverge
    /// (see <c>politicsmodplan.md</c> §3).
    /// </para>
    ///
    /// <para>
    /// The hash is FNV-1a, implemented here rather than taken from the framework. <see
    /// cref="string.GetHashCode()"/> is randomised per process on .NET Core, so using it would make
    /// every run of the game produce different politics from the same save — the exact failure this
    /// class exists to prevent.
    /// </para>
    /// </summary>
    public static class SeedStreams
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        /// <summary>
        /// Derives the 64-bit seed for a stream. Deterministic across machines, runtimes and sessions.
        /// </summary>
        /// <param name="saveGuid">
        /// The save's identity. Agora owns this value and writes it into the save itself, rather than
        /// relying on a filename or an engine-provided id (see <c>politicsmodplan.md</c> §5).
        /// </param>
        /// <param name="date">The sim date the draw belongs to.</param>
        /// <param name="streamName">
        /// A stable constant from <see cref="StreamNames"/>. Renaming a stream changes history and
        /// requires a migration note.
        /// </param>
        public static ulong Derive(Guid saveGuid, SimDate date, string streamName)
        {
            if (string.IsNullOrEmpty(streamName))
                throw new ArgumentException("Stream name must not be empty.", nameof(streamName));

            unchecked
            {
                ulong hash = FnvOffsetBasis;

                // Guid.ToByteArray() has a fixed layout, unlike ToString() which is culture-invariant
                // but longer to hash. Either is deterministic; bytes are cheaper.
                foreach (byte b in saveGuid.ToByteArray())
                {
                    hash = (hash ^ b) * FnvPrime;
                }

                // Fold the date in as its component parts rather than a formatted string, so the seed
                // never depends on formatting behaviour.
                hash = MixInt32(hash, date.Year);
                hash = MixInt32(hash, date.Month);
                hash = MixInt32(hash, date.Day);

                foreach (byte b in Encoding.UTF8.GetBytes(streamName))
                {
                    hash = (hash ^ b) * FnvPrime;
                }

                return hash;
            }
        }

        private static ulong MixInt32(ulong hash, int value)
        {
            unchecked
            {
                for (int shift = 0; shift < 32; shift += 8)
                {
                    hash = (hash ^ (byte)(value >> shift)) * FnvPrime;
                }
                return hash;
            }
        }

        /// <summary>
        /// The normal entry point: a generator for one named stream at one date.
        /// </summary>
        /// <example>
        /// <code>
        /// var rng = SeedStreams.Rng(saveGuid, date, StreamNames.PollError);
        /// double error = rng.NextGaussian() * tuning.PollErrorSigma;
        /// </code>
        /// </example>
        public static DeterministicRng Rng(Guid saveGuid, SimDate date, string streamName) =>
            new DeterministicRng(Derive(saveGuid, date, streamName));

        /// <summary>
        /// A per-entity sub-stream, for draws that must be independent per district, party or bloc.
        /// </summary>
        /// <remarks>
        /// Prefer this over drawing repeatedly from one stream in a loop. Loop-order draws couple the
        /// result to iteration order, so inserting a district silently changes every later district's
        /// outcome. Sub-streams make each entity's draw independent of the others.
        /// </remarks>
        public static DeterministicRng RngFor(Guid saveGuid, SimDate date, string streamName, string entityId) =>
            Rng(saveGuid, date, streamName + ":" + entityId);
    }

    /// <summary>
    /// Stable names for every seeded stream. Constants, never literals at call sites — a typo in a
    /// literal silently creates a new stream instead of failing.
    /// </summary>
    public static class StreamNames
    {
        public const string PollError = "poll.error";
        public const string PollTurnout = "poll.turnout";
        public const string AffinityNoise = "voter.affinity.noise";
        public const string TurnoutNoise = "voter.turnout.noise";
        public const string PartyLifecycle = "party.lifecycle";
        public const string FactionLifecycle = "faction.lifecycle";
        public const string CoalitionFormation = "coalition.formation";
        public const string CoalitionCollapse = "coalition.collapse";
        public const string EventJitter = "event.jitter";
        public const string EventProcedural = "event.procedural";
        public const string MandateSelection = "mandate.selection";
        public const string NameSelection = "flavor.name.selection";
    }
}
