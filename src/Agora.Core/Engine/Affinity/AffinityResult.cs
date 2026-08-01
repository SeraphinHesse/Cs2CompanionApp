using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Affinity
{
    /// <summary>
    /// One bloc's preference distribution over the parties on the ballot: its affinities put through
    /// the softmax at <c>affinity.softmaxTemperature</c>.
    ///
    /// <para>
    /// This is where the affinity packet stops. Turning per-bloc preference into a district or city
    /// result is turnout-weighted aggregation, which belongs to the turnout and election packets —
    /// they multiply these shares by <see cref="BlocTurnout.ProjectedVotes"/>.
    /// </para>
    /// </summary>
    public sealed class BlocVoteShares
    {
        public string DistrictId { get; }

        public BlocKey Bloc { get; }

        /// <summary>
        /// Shares in <c>[0, 1]</c> summing to 1 (empty only when no party is on the ballot), sorted
        /// by <see cref="PartyVoteShare.PartyId"/> ordinal ascending as the contract requires.
        /// </summary>
        public IReadOnlyList<PartyVoteShare> Shares { get; }

        public BlocVoteShares(string districtId, BlocKey bloc, IReadOnlyList<PartyVoteShare> shares)
        {
            DistrictId = districtId;
            Bloc = bloc;
            Shares = shares;
        }
    }

    /// <summary>
    /// Output of one affinity pass. Every list has a documented, total sort order, because the
    /// serialized hash of engine state must not depend on the order the caller happened to build its
    /// inputs in (non-negotiable #3).
    /// </summary>
    public sealed class AffinityResult
    {
        /// <summary>
        /// Every (bloc, party) score, sorted by district id ordinal, then
        /// <see cref="BlocKey.Ordinal"/>, then party id ordinal.
        /// </summary>
        public IReadOnlyList<BlocAffinity> Affinities { get; }

        /// <summary>
        /// One entry per bloc, in the same district/bloc order as <see cref="Affinities"/>.
        /// </summary>
        public IReadOnlyList<BlocVoteShares> BlocShares { get; }

        /// <summary>
        /// Ids of the parties actually scored — those on the ballot — sorted ordinal ascending.
        /// Callers use this to size seat tables without re-deriving the ballot filter.
        /// </summary>
        public IReadOnlyList<string> ContestingPartyIds { get; }

        public AffinityResult(IReadOnlyList<BlocAffinity> affinities,
                              IReadOnlyList<BlocVoteShares> blocShares,
                              IReadOnlyList<string> contestingPartyIds)
        {
            Affinities = affinities;
            BlocShares = blocShares;
            ContestingPartyIds = contestingPartyIds;
        }
    }
}
