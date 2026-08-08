using System.Collections.Generic;
using Agora.Core.Contracts;
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
        private ValueBinding<SettingsPayload> _settings;
        private ValueBinding<List<PartyBriefPayload>> _roster;
        private ValueBinding<List<FactionBriefPayload>> _factions;
        private GetterMapBinding<string, PartyDetailPayload> _partyDetail;
        private GetterMapBinding<string, List<PollTrendPointPayload>> _pollTrend;

        protected override void CreateBindings()
        {
            // Getters re-evaluate on the UI tick, so all three of these are field reads and nothing
            // more (§7 rule 6). isFirstRun is a getter rather than a pushed value precisely because it
            // has to go false the instant the dialog is answered, which is not an engine tick.
            AddUpdateBinding(new GetterValueBinding<bool>(StateGroup, "enabled", GetEnabled));
            AddUpdateBinding(new GetterValueBinding<bool>(StateGroup, "ready", GetReady));
            AddUpdateBinding(new GetterValueBinding<bool>(StateGroup, "isFirstRun", GetIsFirstRun));

            AddBinding(_summary = new ValueBinding<StateSummaryPayload>(
                StateGroup, "summary", new StateSummaryPayload()));

            AddBinding(_settings = new ValueBinding<SettingsPayload>(
                StateGroup, "settings", new SettingsPayload()));

            // The contract's first CallBinding, and its first write channel of any kind beyond the
            // single wakeFlavor trigger. A call rather than a trigger because a rejection has to reach
            // the player: a setting that silently will not stay set is indistinguishable from a broken
            // panel. It *requests*; AgoraRuntime validates, decides, and returns the reason.
            AddBinding(new CallBinding<string, string, string>(
                StateGroup, "setSetting", OnSetSetting));

            AddBinding(_roster = new ValueBinding<List<PartyBriefPayload>>(
                PartiesGroup, "roster", new List<PartyBriefPayload>(), ListOf<PartyBriefPayload>()));

            AddBinding(_factions = new ValueBinding<List<FactionBriefPayload>>(
                PartiesGroup, "factions", new List<FactionBriefPayload>(), ListOf<FactionBriefPayload>()));

            // AddBinding, not AddUpdateBinding — for the reason given in
            // AgoraDistrictsUISystem.CreateBindings, above its own detail map binding.
            AddBinding(_partyDetail = new GetterMapBinding<string, PartyDetailPayload>(
                PartiesGroup, "detail", GetPartyDetail));

            // Named argument: keyReader and keyWriter come first in the signature, and this value is
            // a list, so it needs the same explicit writer a ValueBinding<List<T>> does — omitting it
            // throws MissingMethodException on construction. Same shape as the districts crosstab.
            AddBinding(_pollTrend = new GetterMapBinding<string, List<PollTrendPointPayload>>(
                PartiesGroup, "pollTrend", GetPollTrend,
                valueWriter: ListOf<PollTrendPointPayload>()));
        }

        /// <summary>
        /// One party's detail. An unknown key returns the empty payload rather than throwing: a map
        /// binding that threw would take the interface down with it.
        /// </summary>
        private static PartyDetailPayload GetPartyDetail(string partyId) =>
            AgoraUiProjection.BuildPartyDetail(AgoraRuntime.State, partyId);

        /// <summary>
        /// One party's published poll shares over time, oldest first. An unknown key returns an empty
        /// list, for the same reason the detail returns an empty payload.
        /// </summary>
        private static List<PollTrendPointPayload> GetPollTrend(string partyId) =>
            AgoraUiProjection.BuildPollTrend(AgoraRuntime.State, partyId);

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

        /// <summary>
        /// True while this save has never chosen a region theme. One-shot: it goes false as soon as
        /// the dialog is answered or dismissed, and stays false for the rest of the save.
        /// </summary>
        private static bool GetIsFirstRun() => AgoraRuntime.IsFirstRun;

        /// <summary>
        /// The whole inbound surface of <c>agora.state</c>. Returns the outcome's wire form — the
        /// empty string when the value took, a short engine-authored code otherwise. Never an
        /// exception message: <see cref="AgoraRuntime.SetSetting"/> catches its own and answers
        /// <see cref="CommandOutcome.Failed"/>.
        /// </summary>
        private static string OnSetSetting(string key, string value) =>
            CommandOutcomes.ToWire(AgoraRuntime.SetSetting(key, value));

        protected override void Publish()
        {
            var state = AgoraRuntime.State;

            _summary.Update(AgoraUiProjection.BuildSummary(state));
            _settings.Update(AgoraUiProjection.BuildSettings(AgoraRuntime.SaveSettings));
            _roster.Update(AgoraUiProjection.BuildRoster(state));
            _factions.Update(AgoraUiProjection.BuildFactions(state));

            // UpdateAll only pushes keys the panel has actually subscribed, so this costs nothing
            // when the Parties tab is closed.
            _partyDetail.UpdateAll();
            _pollTrend.UpdateAll();
        }
    }
}
