using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Mod.Core;
using Colossal.UI.Binding;

namespace Agora.Mod.UiBindings
{
    /// <summary>
    /// Publishes <c>agora.news</c>: the mandate tracker, article bodies, the flavor provider's health
    /// line and the political-event alert queue — plus two inbound bindings, the manual LLM wake and
    /// the alert ack (<c>docs/contracts/ui_bindings.md</c> §4.5).
    /// </summary>
    /// <remarks>
    /// <b>The group is named for a panel that no longer exists, and stays that way.</b> Version 10
    /// retired <c>feed</c> and <c>events</c> with <c>ui/src/panels/News/**</c>, leaving the six
    /// bindings below; renaming them to <c>agora.stories.*</c> would break every live consumer to fix
    /// a word, which is exactly the trade §7 rule 2 forbids. Their renderers moved into the Stories
    /// panel; their binding names did not move.
    /// </remarks>
    public sealed partial class AgoraNewsUISystem : AgoraUISystemBase
    {
        private const string Group = "agora.news";

        private ValueBinding<List<MandateRowPayload>> _mandates;
        private ValueBinding<FlavorStatusPayload> _flavorStatus;
        private ValueBinding<List<NewsAlertPayload>> _alerts;

        private GetterMapBinding<string, NewsArticlePayload> _article;

        protected override void CreateBindings()
        {
            AddBinding(_mandates = new ValueBinding<List<MandateRowPayload>>(
                Group, "mandates", new List<MandateRowPayload>(), ListOf<MandateRowPayload>()));

            AddBinding(_flavorStatus = new ValueBinding<FlavorStatusPayload>(
                Group, "flavorStatus", new FlavorStatusPayload()));

            // The unanswered interruptions, oldest first. A queue rather than a single card because
            // one tick can produce several, and the modal shows exactly one at a time.
            AddBinding(_alerts = new ValueBinding<List<NewsAlertPayload>>(
                Group, "alerts", new List<NewsAlertPayload>(), ListOf<NewsAlertPayload>()));

            // Bodies are fetched per card, on open, and only when the alert says it has one. The feed
            // that was this map's other reader is gone; the alert cards are not, and an election,
            // coalition or party card with no map behind it is a blank masthead with nothing logged.
            AddBinding(_article = new GetterMapBinding<string, NewsArticlePayload>(
                Group, "article", GetArticle));

            // The contract's only trigger. It *requests*; the engine decides whether a wake is
            // permitted, and a refused or failed one keeps the last good flavor (#7). A trigger is
            // enough precisely because a refused wake and a successful one that produced nothing look
            // identical to the player.
            AddBinding(new TriggerBinding(Group, "wakeFlavor", OnWakeFlavor));

            // A call and not a trigger, for the opposite reason: if the dismiss did not land, the
            // modal must not close over an alert the engine still holds, and only a call can say so.
            // The argument is the alert id, or "*" for dismiss-all.
            AddBinding(new CallBinding<string, string>(Group, "ackAlert", OnAckAlert));
        }

        private static NewsArticlePayload GetArticle(string id) =>
            AgoraUiProjection.BuildArticle(AgoraRuntime.Prose, id);

        private static void OnWakeFlavor()
        {
            AgoraRuntime.RequestManualFlavorWake();
            AgoraMod.Log.Info("Agora flavor: manual wake requested from the dashboard.");
        }

        /// <summary>
        /// The player dismissed an alert, or all of them. Answers the outcome's wire form and nothing
        /// else — never an exception message, for the same reason as the party editors.
        /// </summary>
        private static string OnAckAlert(string id) =>
            CommandOutcomes.ToWire(AgoraRuntime.AckAlert(id));

        protected override void Publish()
        {
            var state = AgoraRuntime.State;

            _mandates.Update(AgoraUiProjection.BuildMandates(state));
            _flavorStatus.Update(AgoraUiProjection.BuildFlavorStatus(state));

            // Registering the binding above without this line compiles, registers, and silently never
            // updates. One Update per ValueBinding field is the count to keep.
            _alerts.Update(AgoraUiProjection.BuildAlerts(AgoraRuntime.Alerts));

            _article.UpdateAll();
        }
    }
}
