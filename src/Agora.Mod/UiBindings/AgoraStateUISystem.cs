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
        private ValueBinding<PartyPalettePayload> _colorPalette;
        private ValueBinding<PartyEditLimitsPayload> _editLimits;

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

            // Published rather than hard-coded in the panel so the swatches and the character
            // counters cannot become second copies of the tuning and of PartyIdentity's limits.
            AddBinding(_colorPalette = new ValueBinding<PartyPalettePayload>(
                PartiesGroup, "colorPalette", new PartyPalettePayload()));

            AddBinding(_editLimits = new ValueBinding<PartyEditLimitsPayload>(
                PartiesGroup, "editLimits", new PartyEditLimitsPayload()));

            // The party editors. Paired fields travel together — a rename that could not also set
            // the short name would lock the short name away from flavor with no way to write it.
            AddBinding(new CallBinding<string, string, string, string>(
                PartiesGroup, "rename", OnRenameParty));

            AddBinding(new CallBinding<string, string, string, string>(
                PartiesGroup, "setDescription", OnSetPartyDescription));

            AddBinding(new CallBinding<string, string, string>(
                PartiesGroup, "setColor", OnSetPartyColor));

            // Separate bindings rather than a setter called with "": a cleared text box is a slipped
            // keystroke as often as it is a deliberate hand-back, and the two have opposite meanings.
            AddBinding(new CallBinding<string, string>(
                PartiesGroup, "resetName", OnResetPartyName));

            AddBinding(new CallBinding<string, string>(
                PartiesGroup, "resetDescription", OnResetPartyDescription));

            AddBinding(new CallBinding<string, string>(
                PartiesGroup, "resetColor", OnResetPartyColor));
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

        /// <summary>
        /// The six write channels of <c>agora.parties</c>. Each answers the outcome's wire form and
        /// nothing else — never an exception message, for the same reason as
        /// <see cref="OnSetSetting"/>.
        /// </summary>
        /// <remarks>
        /// The wire form is taken from the outcome as a whole rather than tested against
        /// <see cref="CommandOutcome.Ok"/>: <c>OkColorInUse</c> is an acceptance that still crosses
        /// under its own name, and a panel that saw only Ok would drop the warning.
        /// </remarks>
        private static string OnRenameParty(string partyId, string name, string shortName) =>
            CommandOutcomes.ToWire(AgoraRuntime.RenameParty(partyId, name, shortName));

        private static string OnSetPartyDescription(string partyId, string description, string slogan) =>
            CommandOutcomes.ToWire(AgoraRuntime.SetPartyDescription(partyId, description, slogan));

        private static string OnSetPartyColor(string partyId, string colorHex) =>
            CommandOutcomes.ToWire(AgoraRuntime.SetPartyColor(partyId, colorHex));

        private static string OnResetPartyName(string partyId) =>
            CommandOutcomes.ToWire(AgoraRuntime.ResetPartyName(partyId));

        private static string OnResetPartyDescription(string partyId) =>
            CommandOutcomes.ToWire(AgoraRuntime.ResetPartyDescription(partyId));

        private static string OnResetPartyColor(string partyId) =>
            CommandOutcomes.ToWire(AgoraRuntime.ResetPartyColor(partyId));

        protected override void Publish()
        {
            var state = AgoraRuntime.State;

            _summary.Update(AgoraUiProjection.BuildSummary(state));
            _settings.Update(AgoraUiProjection.BuildSettings(AgoraRuntime.SaveSettings));
            _roster.Update(AgoraUiProjection.BuildRoster(state));
            _factions.Update(AgoraUiProjection.BuildFactions(state));

            // On the roster's tick, per the contract: an editor that had the roster but not yet the
            // limits would count characters against zero.
            _colorPalette.Update(AgoraUiProjection.BuildPalette(AgoraRuntime.Tuning));
            _editLimits.Update(AgoraUiProjection.BuildEditLimits());
        }
    }
}
