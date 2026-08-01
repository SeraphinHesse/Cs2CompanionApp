using System.Collections.Generic;
using Agora.Mod.Core;
using Colossal.UI.Binding;
using Game.UI;

namespace Agora.Mod.UiBindings
{
    /// <summary>
    /// Publishes <c>agora.state</c> (dashboard chrome) and <c>agora.parties</c> (the shared party and
    /// faction lookup table), per <c>docs/contracts/ui_bindings.md</c> §4.1 and §4.2.
    ///
    /// <para>
    /// Two areas in one system on purpose: the contract assigns them to the same publisher because
    /// every panel needs both on its first frame, and splitting them would let a panel render seat
    /// rows before it can resolve a single party's colour.
    /// </para>
    /// </summary>
    public sealed partial class AgoraStateUISystem : AgoraUISystemBase
    {
        private const string StateGroup = "agora.state";
        private const string PartiesGroup = "agora.parties";

        private ValueBinding<StateSummaryPayload> _summary;
        private ValueBinding<List<PartyBriefPayload>> _roster;
        private ValueBinding<List<FactionBriefPayload>> _factions;

        protected override void CreateBindings()
        {
            // Getters re-evaluate on the UI tick, so both of these are field reads and nothing more.
            AddUpdateBinding(new GetterValueBinding<bool>(StateGroup, "enabled", GetEnabled));
            AddUpdateBinding(new GetterValueBinding<bool>(StateGroup, "ready", GetReady));

            AddBinding(_summary = new ValueBinding<StateSummaryPayload>(
                StateGroup, "summary", new StateSummaryPayload()));

            AddBinding(_roster = new ValueBinding<List<PartyBriefPayload>>(
                PartiesGroup, "roster", new List<PartyBriefPayload>(), ListOf<PartyBriefPayload>()));

            AddBinding(_factions = new ValueBinding<List<FactionBriefPayload>>(
                PartiesGroup, "factions", new List<FactionBriefPayload>(), ListOf<FactionBriefPayload>()));
        }

        /// <summary>
        /// The master toggle. Panels render <c>null</c> when this is false — not a disabled shell,
        /// because a mod that is switched off should leave no trace in the interface.
        /// </summary>
        private static bool GetEnabled() =>
            AgoraMod.Settings != null && AgoraMod.Settings.Enabled &&
            AgoraRuntime.IsSaveActive && AgoraRuntime.SaveSettings.Enabled;

        /// <summary>
        /// True once the engine has published a state. Until then every other binding in the contract
        /// is still at its empty value, and panels show a skeleton rather than an empty dashboard.
        /// </summary>
        private static bool GetReady() => AgoraRuntime.State != null;

        protected override void Publish()
        {
            var state = AgoraRuntime.State;

            _summary.Update(AgoraUiProjection.BuildSummary(state));
            _roster.Update(AgoraUiProjection.BuildRoster(state));
            _factions.Update(AgoraUiProjection.BuildFactions(state));
        }
    }
}
