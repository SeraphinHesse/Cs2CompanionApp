using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Indices
{
    /// <summary>
    /// Everything <see cref="IndicesEngine.Compute"/> reads. One argument object rather than nine
    /// parameters, so a later index can take a new input without breaking every caller.
    ///
    /// <para>
    /// Every member has a neutral default: an input with only <see cref="Snapshot"/> set is valid and
    /// produces the snapshot-only indices with the history-dependent legs reading zero. That matters
    /// because the very first tick of a save genuinely has no history, no election and no government,
    /// and the engine must still publish a full <see cref="DerivedIndices"/>.
    /// </para>
    /// </summary>
    public sealed class IndicesInput
    {
        private static readonly CitySnapshot[] NoSnapshots = new CitySnapshot[0];
        private static readonly PartyVoteShare[] NoShares = new PartyVoteShare[0];
        private static readonly Mandate[] NoMandates = new Mandate[0];

        /// <summary>The snapshot being scored. Its <c>Indices</c> property is ignored — this is what fills it.</summary>
        public CitySnapshot Snapshot { get; set; } = new CitySnapshot();

        /// <summary>
        /// Earlier snapshots, oldest first and excluding <see cref="Snapshot"/>. Only the entry
        /// nearest each tuning window is read, and only entries strictly older than
        /// <see cref="Snapshot"/> are eligible, so passing the whole retained ring is fine.
        /// Selection is by distance-to-target with an earlier-date tie-break, so an unsorted list
        /// still gives the same answer.
        /// </summary>
        public IReadOnlyList<CitySnapshot> History { get; set; } = NoSnapshots;

        /// <summary>
        /// Last tick's indices, for the exponential smoothing in <c>indices.smoothingAlpha</c>. Null
        /// on the first tick, which makes every index pass through raw. Districts are matched by id;
        /// a district that did not exist last tick is not smoothed.
        /// </summary>
        public DerivedIndices? Previous { get; set; }

        /// <summary>
        /// Current city-wide vote shares, sorted by party id (the contractual order for every
        /// <see cref="PartyVoteShare"/> list). Drives <see cref="DerivedIndices.PolarizationIndex"/>.
        /// </summary>
        public IReadOnlyList<PartyVoteShare> VoteShares { get; set; } = NoShares;

        /// <summary>
        /// Turnout at the most recent election, 0–1. Null before the first election — see
        /// <see cref="IndexFormulas.Legitimacy"/> for why null is not zero.
        /// </summary>
        public double? LastElectionTurnout { get; set; }

        /// <summary>
        /// Every mandate the save knows about, sorted by id. Only resolved ones are scored, and they
        /// are re-sorted defensively before summation so the mean is bit-stable.
        /// </summary>
        public IReadOnlyList<Mandate> Mandates { get; set; } = NoMandates;

        /// <summary>The sitting government, or null between a collapse and a new formation.</summary>
        public Coalition? Government { get; set; }
    }
}
