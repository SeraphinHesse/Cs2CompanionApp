using System.Collections.Generic;
using Agora.Mod.Core;
using Colossal.UI.Binding;

namespace Agora.Mod.UiBindings
{
    /// <summary>
    /// Publishes <c>agora.news</c>: the feed, article bodies, live timeline events, the mandate
    /// tracker and the flavor provider's health line — plus the one inbound binding in the whole
    /// contract, the manual LLM wake (<c>docs/contracts/ui_bindings.md</c> §4.5).
    /// </summary>
    public sealed partial class AgoraNewsUISystem : AgoraUISystemBase
    {
        private const string Group = "agora.news";

        private ValueBinding<List<NewsHeadlinePayload>> _feed;
        private ValueBinding<List<TimelineEventBriefPayload>> _events;
        private ValueBinding<List<MandateRowPayload>> _mandates;
        private ValueBinding<FlavorStatusPayload> _flavorStatus;

        private GetterMapBinding<string, NewsArticlePayload> _article;

        protected override void CreateBindings()
        {
            AddBinding(_feed = new ValueBinding<List<NewsHeadlinePayload>>(
                Group, "feed", new List<NewsHeadlinePayload>(), ListOf<NewsHeadlinePayload>()));

            AddBinding(_events = new ValueBinding<List<TimelineEventBriefPayload>>(
                Group, "events", new List<TimelineEventBriefPayload>(),
                ListOf<TimelineEventBriefPayload>()));

            AddBinding(_mandates = new ValueBinding<List<MandateRowPayload>>(
                Group, "mandates", new List<MandateRowPayload>(), ListOf<MandateRowPayload>()));

            AddBinding(_flavorStatus = new ValueBinding<FlavorStatusPayload>(
                Group, "flavorStatus", new FlavorStatusPayload()));

            // Bodies are fetched per item, on open. Forty feed rows each carrying a 120-word body
            // would be the largest thing crossing this bridge, every month, to render one of them.
            AddBinding(_article = new GetterMapBinding<string, NewsArticlePayload>(
                Group, "article", GetArticle));

            // The only UI → C# binding in the contract. It *requests*; the engine decides whether a
            // wake is permitted, and a refused or failed one keeps the last good prose (#7).
            AddBinding(new TriggerBinding(Group, "wakeFlavor", OnWakeFlavor));
        }

        private static NewsArticlePayload GetArticle(string id) =>
            AgoraUiProjection.BuildArticle(AgoraRuntime.Prose, id);

        private static void OnWakeFlavor()
        {
            AgoraRuntime.RequestManualFlavorWake();
            AgoraMod.Log.Info("Agora flavor: manual wake requested from the dashboard.");
        }

        protected override void Publish()
        {
            var state = AgoraRuntime.State;

            _feed.Update(AgoraUiProjection.BuildFeed(state, AgoraRuntime.Prose));
            _events.Update(AgoraUiProjection.BuildEvents(state));
            _mandates.Update(AgoraUiProjection.BuildMandates(state));
            _flavorStatus.Update(AgoraUiProjection.BuildFlavorStatus(state));

            _article.UpdateAll();
        }
    }
}
