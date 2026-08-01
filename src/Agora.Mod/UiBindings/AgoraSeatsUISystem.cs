using System.Collections.Generic;
using Agora.Mod.Core;
using Colossal.UI.Binding;

namespace Agora.Mod.UiBindings
{
    /// <summary>
    /// Publishes <c>agora.seats</c>: the seat chart, the government breakdown, the mayor, the last
    /// election, the latest published poll and the election history
    /// (<c>docs/contracts/ui_bindings.md</c> §4.3).
    /// </summary>
    /// <remarks>
    /// Four of these payloads are legitimately null — there is no government before the first
    /// formation and no poll outside a campaign — so each is wrapped in a
    /// <see cref="NullableWriter{T}"/>. Without it the default writer throws
    /// <see cref="System.ArgumentNullException"/> the first time it is handed a null, which on a
    /// fresh save is immediately.
    /// </remarks>
    public sealed partial class AgoraSeatsUISystem : AgoraUISystemBase
    {
        private const string Group = "agora.seats";

        private ValueBinding<List<SeatRowPayload>> _allocation;
        private ValueBinding<List<PartySharePayload>> _voteShares;
        private ValueBinding<GovernmentSummaryPayload> _government;
        private ValueBinding<MayorSummaryPayload> _mayor;
        private ValueBinding<ElectionSummaryPayload> _lastElection;
        private ValueBinding<PollSummaryPayload> _latestPoll;
        private ValueBinding<List<ElectionHistoryRowPayload>> _history;

        protected override void CreateBindings()
        {
            // Reads a cached int off the last election; safe to re-evaluate on the UI tick.
            AddUpdateBinding(new GetterValueBinding<int>(Group, "total", GetTotalSeats));

            AddBinding(_allocation = new ValueBinding<List<SeatRowPayload>>(
                Group, "allocation", new List<SeatRowPayload>(), ListOf<SeatRowPayload>()));

            AddBinding(_voteShares = new ValueBinding<List<PartySharePayload>>(
                Group, "voteShares", new List<PartySharePayload>(), ListOf<PartySharePayload>()));

            AddBinding(_government = new ValueBinding<GovernmentSummaryPayload>(
                Group, "government", null, Nullable<GovernmentSummaryPayload>()));

            AddBinding(_mayor = new ValueBinding<MayorSummaryPayload>(
                Group, "mayor", null, Nullable<MayorSummaryPayload>()));

            AddBinding(_lastElection = new ValueBinding<ElectionSummaryPayload>(
                Group, "lastElection", null, Nullable<ElectionSummaryPayload>()));

            AddBinding(_latestPoll = new ValueBinding<PollSummaryPayload>(
                Group, "latestPoll", null, Nullable<PollSummaryPayload>()));

            AddBinding(_history = new ValueBinding<List<ElectionHistoryRowPayload>>(
                Group, "history", new List<ElectionHistoryRowPayload>(),
                ListOf<ElectionHistoryRowPayload>()));
        }

        private static int GetTotalSeats() => AgoraUiProjection.TotalSeats(AgoraRuntime.State);

        protected override void Publish()
        {
            var state = AgoraRuntime.State;

            _allocation.Update(AgoraUiProjection.BuildAllocation(state));
            _voteShares.Update(AgoraUiProjection.BuildVoteShares(state));
            _government.Update(AgoraUiProjection.BuildGovernment(state));
            _mayor.Update(AgoraUiProjection.BuildMayor(state));
            _lastElection.Update(AgoraUiProjection.BuildLastElection(state));
            _latestPoll.Update(AgoraUiProjection.BuildLatestPoll(state));
            _history.Update(AgoraUiProjection.BuildHistory(state));
        }
    }
}
