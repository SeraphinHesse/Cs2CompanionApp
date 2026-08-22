using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Mod.Core
{
    /// <summary>
    /// Which articles cover the election an alert announces, and the one resolver that both the card
    /// list and the body fetch go through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gap this closes.</b> An election alert's id is <c>"election:&lt;electionId&gt;"</c> and
    /// no article carries that id — an article is keyed on its own <c>Article.Id</c>, and putting an
    /// election id in an article's <c>eventId</c> would have it discarded by
    /// <c>FlavorValidator</c> as an unknown catalog event. So the join lives here instead: the round
    /// woken for an election records the ids of the articles it produced against the alert that
    /// announced the same election, and nothing about the flavor contract has to move.
    /// </para>
    /// <para>
    /// <b>Session state, and no schema version.</b> The alert ring is never persisted — an alert does
    /// not replay after a reload — so an association that outlives the alert has nothing left to
    /// point at. This is held beside the ring in <c>AgoraRuntime</c> and cleared in the same block of
    /// <c>ResetForNewSave</c>, because city A's coverage appearing over city B's card is a bug class
    /// this repo has shipped once already.
    /// </para>
    /// <para>
    /// <b>One slot, not a map.</b> At most one election round can be in flight — the CLI provider
    /// refuses a second request while one is running, and elections are years apart — so a single
    /// recorded association answers every question a map would, and it answers the stale one by
    /// construction: <see cref="Expect"/> drops whatever was recorded before, so the previous
    /// election's coverage is gone before the next election's round can land. That is the second of
    /// two defences; the first is that the lookup is keyed on the alert id, so a card can only ever
    /// be offered the round recorded against its own election.
    /// </para>
    /// <para>
    /// <b>Fail closed (#7).</b> Nothing is recorded until prose actually arrives, and
    /// <see cref="ResolveArticleId"/> re-checks the live payload every call rather than trusting what
    /// was recorded. A missing CLI, a timeout or bad JSON therefore resolves to <c>""</c> — the card
    /// keeps its engine-written headline and summary, which is what ships today — instead of
    /// claiming a body that no lookup can find. The last good payload staying in memory cannot
    /// re-attach an old round either: the recorded ids stop resolving as soon as the next payload
    /// replaces them, and an ordinary month's payload carries no articles at all.
    /// </para>
    /// <para>
    /// <b>A body reaches an election card only when a CLI round lands. The canned pool's election
    /// articles never reach <see cref="Absorb"/>, and that is ratified rather than a gap</b> — with
    /// no Claude CLI the card shows its engine-written headline and summary, which is a correct card.
    /// Three separate mechanisms close that path, and anyone tempted to "fix" it by making the pool
    /// emit articles has to move all three in one edit or change nothing at all:
    /// </para>
    /// <para>
    /// (1) <c>AgoraRuntime.SeedFlavorRoster</c> runs at the top of every month and installs a roster
    /// carrying the default wake reason, and <c>StaticPoolProvider.PlanRound</c> writes articles only
    /// for <c>FlavorWakeReason.Election</c>. (2) The election-reason roster is installed by
    /// <c>MaybeWakeFlavor</c>, which runs <i>after</i> that month's <c>CollectProse</c> — so it sits
    /// unread until month M+1, where the roster reset overwrites it first; and the other
    /// <c>CollectProse</c> call site, the daily heartbeat, fires only on a provider transition to
    /// <c>FlavorProviderState.Succeeded</c>, which never happens when there is no CLI to succeed.
    /// (3) <c>StaticPoolProvider.TryGetFlavor</c> answers at most once per date, so even inserting a
    /// re-poll between the wake and the end of the tick returns null.
    /// </para>
    /// <para>
    /// The ordering behind (1) and (2) is the prose plumbing every month runs through, and (3) is the
    /// guard that stops the pool re-deriving a save's party names on every sim day. Neither is worth
    /// disturbing for a card body, so this class is honest about answering <c>""</c> on the offline
    /// path rather than pretending to cover it.
    /// </para>
    /// <para>
    /// Deliberately free of every <c>Game.*</c>, <c>Colossal.*</c> and <c>Unity.*</c> type, so it is
    /// compile-linked into <c>Agora.Core.Tests</c>: <c>AgoraRuntime</c> and <c>UiBindings</c> link
    /// into no test, and this is the part of the mechanism worth asserting on.
    /// </para>
    /// </remarks>
    public sealed class ElectionCoverage
    {
        /// <summary>
        /// How many sim months an expectation waits for its round before it is abandoned.
        /// </summary>
        /// <remarks>
        /// Not a tuning key, for the same reason <c>AgoraRuntime.AlertQueueMax</c> is not one: it
        /// bounds a session-scoped UI association, and <c>data/engine_tuning.json</c> is for numbers
        /// the engine reasons with. The round it waits for lands within a sim day of the wake (the
        /// daily heartbeat collects on the provider's state transition) or on the next monthly poll,
        /// so two months is generous — and the bound is what stops an expectation nobody ever
        /// answered from latching onto a round drawn years later.
        /// </remarks>
        public const int WaitMonths = 2;

        private bool _expecting;
        private string _expectedAlertId = "";
        private int _expectedSinceMonth;

        private string _alertId = "";
        private readonly List<string> _articleIds = new List<string>();

        /// <summary>The alert id an association is recorded against, or <c>""</c> when there is none.</summary>
        public string RecordedAlertId
        {
            get { return _articleIds.Count == 0 ? "" : _alertId; }
        }

        /// <summary>True between a wake for an election and the round that answers it.</summary>
        public bool IsExpecting
        {
            get { return _expecting; }
        }

        /// <summary>
        /// Notes that the round just requested is an election round, and which alert announces that
        /// election.
        /// </summary>
        /// <remarks>
        /// The alert id is taken from the caller rather than inferred from a date: the runtime knows
        /// which election it woke for, and two sites deriving "the election of this month" from the
        /// calendar is how they come to disagree. Recording is deferred to <see cref="Absorb"/>
        /// because the round is generated on a background thread and lands ticks later.
        /// </remarks>
        /// <param name="alertId">The prefixed alert id, from <c>NewsAlert.ElectionAlertId</c>.</param>
        /// <param name="wakeDate">The date the tick was handed — never one computed here (#8).</param>
        public void Expect(string alertId, SimDate wakeDate)
        {
            // Before the guard, not after: a wake we cannot key on still supersedes the last one, and
            // leaving the previous election's articles recorded would be the stale attachment this
            // class exists to make impossible.
            _alertId = "";
            _articleIds.Clear();

            if (string.IsNullOrEmpty(alertId))
            {
                _expecting = false;
                _expectedAlertId = "";
                return;
            }

            _expecting = true;
            _expectedAlertId = alertId;
            _expectedSinceMonth = wakeDate.TotalMonths;
        }

        /// <summary>
        /// Files a payload that has just landed against the election round it was woken for, if it is
        /// that round.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A payload carrying no articles does <b>not</b> answer the expectation and does not consume
        /// it. Every poll between the wake and the round is an ordinary one — the canned pool writes
        /// articles only on an election round (<c>StaticPoolProvider.PlanRound</c>) — so consuming
        /// the expectation on the first payload of any kind would file an empty association and leave
        /// the real round, arriving from the CLI a moment later, with nowhere to go.
        /// </para>
        /// <para>
        /// The window is checked against the tick's date. A payload arriving outside it, or after a
        /// clock that moved backwards, abandons the expectation rather than filing it.
        /// </para>
        /// </remarks>
        public void Absorb(FlavorPayload payload, SimDate today)
        {
            if (!_expecting) return;

            int elapsed = today.TotalMonths - _expectedSinceMonth;
            if (elapsed < 0 || elapsed > WaitMonths)
            {
                _expecting = false;
                _expectedAlertId = "";
                return;
            }

            if (payload == null || payload.Articles == null || payload.Articles.Count == 0) return;

            var ids = new List<string>();
            for (int i = 0; i < payload.Articles.Count; i++)
            {
                Article article = payload.Articles[i];
                if (article == null || string.IsNullOrEmpty(article.Id)) continue;
                if (ids.Contains(article.Id)) continue;

                ids.Add(article.Id);
            }

            if (ids.Count == 0) return;

            // The declared total order, and the reason the card is not "whichever piece happened to
            // be first in the payload": ordinal ascending on the id, the house convention, sorted
            // here so the choice is made once and both callers of ResolveArticleId inherit it. The
            // canned pool numbers a round's pieces "static-<date>-1" upward in the order PlanRound
            // files them, and PlanRound files the result piece first — so on the pool this lands on
            // the piece that says the count happened, which is the one claim true of every election
            // round. On a model round the ids are the model's, so the order is arbitrary but stable,
            // which is the property that matters: the same save shows the same body every publish.
            ids.Sort(StringComparer.Ordinal);

            _alertId = _expectedAlertId;
            _articleIds.Clear();
            _articleIds.AddRange(ids);

            _expecting = false;
            _expectedAlertId = "";
        }

        /// <summary>
        /// The id of the article a card should show as its body, or <c>""</c> when there is none.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>One function, two callers.</b> <c>AgoraUiProjection.BuildAlerts</c> sets
        /// <c>hasArticle</c> from whether this answers, and <c>BuildArticle</c> translates the alert
        /// id into the article to publish with it. If those two could disagree the player would get a
        /// card promising a body and a blank masthead behind it, with nothing logged — the exact
        /// failure <c>docs/contracts/ui_bindings.md</c> §4.5 writes down.
        /// </para>
        /// <para>
        /// The answer is recomputed against <paramref name="prose"/> on every call rather than cached,
        /// which is what makes it self-correcting in both directions: the flag turns true when a round
        /// lands after the card was published, and false again when the next payload replaces those
        /// articles. An id with no body is not an answer — a masthead with no text under it is the
        /// thing being avoided, not a partial success.
        /// </para>
        /// </remarks>
        public string ResolveArticleId(FlavorPayload prose, string alertId)
        {
            if (prose == null || prose.Articles == null) return "";
            if (string.IsNullOrEmpty(alertId)) return "";
            if (_articleIds.Count == 0) return "";
            if (!string.Equals(_alertId, alertId, StringComparison.Ordinal)) return "";

            for (int i = 0; i < _articleIds.Count; i++)
            {
                string candidate = _articleIds[i];

                for (int a = 0; a < prose.Articles.Count; a++)
                {
                    Article article = prose.Articles[a];
                    if (article == null) continue;
                    if (string.CompareOrdinal(article.Id, candidate) != 0) continue;
                    if (string.IsNullOrEmpty(article.Body)) continue;

                    return candidate;
                }
            }

            return "";
        }

        /// <summary>
        /// Forgets both the expectation and the association. Called from
        /// <c>AgoraRuntime.ResetForNewSave</c>, in the same block that clears the ring.
        /// </summary>
        public void Clear()
        {
            _expecting = false;
            _expectedAlertId = "";
            _expectedSinceMonth = 0;
            _alertId = "";
            _articleIds.Clear();
        }
    }
}
