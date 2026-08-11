using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Engine.Elections.Proportional;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Government.Coalitions
{
    /// <summary>
    /// The chamber a city would seat if the latest published poll were the ballot — a projection,
    /// never a result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It exists so the dashboard's coalition arithmetic can answer "who could govern" before the
    /// save's first election, which is the whole promise of that binding being a LIVE view
    /// (<c>docs/contracts/ui_bindings.md</c> §4.2). Without it a fresh EU city shows an empty box for
    /// the first four sim years and reads as broken.
    /// </para>
    /// <para>
    /// Purity (non-negotiable #3): a function of (shares, tuning, saveGuid, date, population) and
    /// nothing else. It writes nothing, records no election, and never touches
    /// <see cref="PoliticalState"/>. The only draw it can reach is the allocator's
    /// <c>election.tiebreak</c> stream, which is named and seeded (#2) — an exact tie between two
    /// parties' next-seat quotients — and the same (saveGuid, date) therefore yields the same
    /// chamber every call.
    /// </para>
    /// <para>
    /// It reads the latest <b>published</b> poll rather than
    /// <see cref="PoliticalState.CurrentVoteShares"/>, for the reason
    /// <c>AgoraUiProjection.BuildLatestPoll</c> refuses <c>PollResult.TrueShares</c>: the dashboard is
    /// the player's view of what is publicly known, and seating a chamber off the model's own answer
    /// would put a number on screen no pollster ever reported. The consequence is deliberate — before
    /// the first poll is published there is no projection, and the panel says so.
    /// </para>
    /// </remarks>
    public static class ProvisionalChamber
    {
        /// <summary>
        /// Stands in for an election id in the allocator's tie-break entity ids. Distinct from every
        /// real one (<c>election-YYYY-MM</c>), so a projected tie can never share a sub-stream with a
        /// counted one.
        /// </summary>
        public const string ProjectionId = "provisional";

        /// <summary>
        /// Ballots the poll's shares are spread over.
        /// </summary>
        /// <remarks>
        /// A projection has no turnout model to ask — turnout is computed at the election, from a
        /// snapshot this has no access to — so it needs a nominal electorate, and any fixed one gives
        /// the same seat vector because every method here reads only the ratios between the counts.
        /// Large enough that <see cref="VoteCounts.FromShares"/>'s rounding is far below the
        /// threshold test, and a constant rather than a tuning key because it is not a tuned quantity.
        /// </remarks>
        public const int NominalBallots = 1000000;

        /// <summary>
        /// The projected chamber for a save that has not voted yet, or an empty list when there is
        /// nothing to project from.
        /// </summary>
        /// <remarks>
        /// Answers empty — never a fabricated chamber — under first past the post, which seats no
        /// proportional chamber at all and whose coalition arithmetic is ratified as absent.
        /// </remarks>
        /// <param name="state">The save. Read only; nothing on it is written.</param>
        /// <param name="tuning">Engine tuning; the <c>electionsPr</c> section is read.</param>
        public static IReadOnlyList<SeatAllocation> Project(PoliticalState state, EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            var none = new List<SeatAllocation>();
            if (state == null || state.Settings == null) return none;

            return Project(
                state.Settings.System, LatestPublishedShares(state), tuning, state.SaveGuid, state.Date);
        }

        /// <summary>
        /// The projection itself, from shares the caller chose. Tested directly; <see cref="Project"/>
        /// is the overload that decides which shares a save's projection is entitled to use.
        /// </summary>
        /// <param name="system">Electoral system in force. FPTP projects nothing.</param>
        /// <param name="shares">City-wide shares to seat. Order-independent; duplicate ids are summed.</param>
        /// <param name="tuning">Engine tuning; the <c>electionsPr</c> section is read.</param>
        /// <param name="saveGuid">Save identity, for the allocator's tie-break stream.</param>
        /// <param name="date">Projection date, for the same stream.</param>
        /// <param name="population">
        /// City population, when the caller has one. Only <c>electionsPr.seatsPerPopulation &gt; 0</c>
        /// reads it, and that key ships at 0, so the default pins the projected chamber to the same
        /// <c>electionsPr.totalSeats</c> a real ballot would fill. A caller that can see the city
        /// should pass it rather than let the projection size a chamber the election would not.
        /// </param>
        public static IReadOnlyList<SeatAllocation> Project(
            ElectoralSystem system,
            IReadOnlyList<PartyVoteShare>? shares,
            EngineTuning tuning,
            Guid saveGuid,
            SimDate date,
            int population = 0)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            var none = new List<SeatAllocation>();
            if (system == ElectoralSystem.FirstPastThePost) return none;
            if (shares == null || shares.Count == 0) return none;

            int chamber = ProportionalAllocator.ChamberSize(population, tuning.ElectionsPr);

            // Null district seats, exactly as RunProportional passes: a projection has no district
            // contests to report, which is a different statement from every party having lost them.
            SeatAllocationResult allocation = ProportionalAllocator.Allocate(
                VoteCounts.FromShares(shares, NominalBallots),
                chamber, null, tuning, saveGuid, date, ProjectionId);

            return allocation.Seats;
        }

        /// <summary>
        /// The newest published poll's shares, or null when this save has never had one.
        /// </summary>
        /// <remarks>
        /// Walks backward over <see cref="PoliticalState.RecentPolls"/>, which is oldest first, and
        /// skips unpublished entries — the same scan, in the same direction, with the same
        /// <c>IsPublished</c> test that <c>AgoraUiProjection.BuildLatestPoll</c> makes, so the
        /// projection is always seated off the poll the dashboard is showing beside it.
        /// </remarks>
        private static IReadOnlyList<PartyVoteShare>? LatestPublishedShares(PoliticalState state)
        {
            if (state.RecentPolls == null) return null;

            for (int i = state.RecentPolls.Count - 1; i >= 0; i--)
            {
                PollResult poll = state.RecentPolls[i];
                if (poll == null || !poll.IsPublished) continue;
                return poll.Shares;
            }

            return null;
        }
    }
}
