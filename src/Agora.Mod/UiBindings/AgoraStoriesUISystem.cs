using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Stories;
using Agora.Mod.Core;
using Colossal.UI.Binding;

namespace Agora.Mod.UiBindings
{
    /// <summary>
    /// Publishes <c>agora.stories</c>: the live stories, the archive, a story's prose, the
    /// political-power counter and the story card queue — plus the five inbound bindings that are the
    /// only way a player can answer a story (<c>docs/contracts/ui_bindings.md</c> §4.7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Five inbound bindings, not the three the rework plan's table lists.</b> The plan assumed the
    /// panel would buy a slot off through <c>setResponse</c>; wave 4 refuses that, because a
    /// <c>PowerOverride</c> arriving as an ordinary response would be a free <c>Met</c> nobody paid
    /// for. So the purchase has its own channel, and the card dismissal has a fifth.
    /// </para>
    /// <para>
    /// Every one is a <c>CallBinding</c> and none is a <c>TriggerBinding</c>. The engine may refuse
    /// any of them — insufficient power, an already-resolved story, an over-length justification — and
    /// a refusal the player cannot see is indistinguishable from a broken panel. The handlers below do
    /// nothing but hand the runtime's own verdict back in its wire form; no exception text ever
    /// reaches the panel, and this system never decides a rejection of its own (contract rule 5).
    /// </para>
    /// </remarks>
    public sealed partial class AgoraStoriesUISystem : AgoraUISystemBase
    {
        private const string Group = "agora.stories";

        private ValueBinding<List<StoryPayload>> _live;
        private ValueBinding<List<StoryBriefPayload>> _archive;
        private ValueBinding<PowerPayload> _power;
        private ValueBinding<List<StoryAlertPayload>> _alerts;

        private GetterMapBinding<string, StoryArticlePayload> _article;

        protected override void CreateBindings()
        {
            AddBinding(_live = new ValueBinding<List<StoryPayload>>(
                Group, "live", new List<StoryPayload>(), ListOf<StoryPayload>()));

            AddBinding(_archive = new ValueBinding<List<StoryBriefPayload>>(
                Group, "archive", new List<StoryBriefPayload>(), ListOf<StoryBriefPayload>()));

            AddBinding(_power = new ValueBinding<PowerPayload>(
                Group, "power", new PowerPayload()));

            // The unanswered story cards, oldest first. Its own queue and not a share of
            // agora.news.alerts — see AgoraRuntime._storyAlerts for the three reasons.
            AddBinding(_alerts = new ValueBinding<List<StoryAlertPayload>>(
                Group, "alerts", new List<StoryAlertPayload>(), ListOf<StoryAlertPayload>()));

            // Bodies per story, on open. A story carries two articles in up to two voices at 1260
            // characters each, so shipping them on `live` would push the largest thing on this bridge
            // across it every republish to render one of them — the same reasoning as
            // agora.news.article.
            AddBinding(_article = new GetterMapBinding<string, StoryArticlePayload>(
                Group, "article", GetArticle));

            AddBinding(new CallBinding<string, string, string, string, string>(
                Group, "setResponse", OnSetResponse));

            AddBinding(new CallBinding<string, string, bool, string, string>(
                Group, "declareManual", OnDeclareManual));

            AddBinding(new CallBinding<string, string>(Group, "resolveNow", OnResolveNow));

            AddBinding(new CallBinding<string, string, string>(
                Group, "spendPowerOverride", OnSpendPowerOverride));

            AddBinding(new CallBinding<string, string>(Group, "ackAlert", OnAckAlert));
        }

        private static StoryArticlePayload GetArticle(string id) =>
            AgoraUiProjection.BuildStoryArticle(AgoraRuntime.StoryProse, id);

        private static string OnSetResponse(string storyId, string eventId, string mode, string text) =>
            CommandOutcomes.ToWire(AgoraRuntime.SetStoryResponse(storyId, eventId, mode, text));

        private static string OnDeclareManual(string storyId, string eventId, bool met, string text) =>
            CommandOutcomes.ToWire(AgoraRuntime.DeclareManualOutcome(storyId, eventId, met, text));

        private static string OnResolveNow(string storyId) =>
            CommandOutcomes.ToWire(AgoraRuntime.ResolveNow(storyId));

        private static string OnSpendPowerOverride(string storyId, string eventId) =>
            CommandOutcomes.ToWire(AgoraRuntime.SpendPowerOverride(storyId, eventId));

        /// <summary>
        /// The player dismissed a story card, or all of them.
        /// </summary>
        /// <remarks>
        /// <b>Dismissing a card answers nothing.</b> It closes the interruption; the story stays live
        /// and is still answered from the Stories panel. Nothing in the engine moves.
        /// </remarks>
        private static string OnAckAlert(string id) =>
            CommandOutcomes.ToWire(AgoraRuntime.AckStoryAlert(id));

        protected override void Publish()
        {
            PoliticalState state = AgoraRuntime.State;

            _live.Update(AgoraUiProjection.BuildLiveStories(
                state, AgoraRuntime.CivicCatalog, AgoraRuntime.Tuning));
            _archive.Update(AgoraUiProjection.BuildStoryArchive(state, AgoraRuntime.CivicCatalog));
            _power.Update(AgoraUiProjection.BuildPower(state, AgoraRuntime.Tuning));

            // Registering the binding above without this line compiles, registers, and silently never
            // updates. One Update per ValueBinding field is the count to keep.
            _alerts.Update(AgoraUiProjection.BuildStoryAlerts(AgoraRuntime.StoryAlerts));

            _article.UpdateAll();
        }
    }
}
