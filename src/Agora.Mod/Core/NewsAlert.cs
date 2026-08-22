using Agora.Core.Contracts;

namespace Agora.Mod.Core
{
    /// <summary>
    /// One interruption the player has not answered yet — an election, a government forming or
    /// falling, a party entering or leaving the field, or an event that cleared the severity gate —
    /// held in a session-scoped queue until it is acked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A Mod-side class, deliberately not a Core contract.</b> It never enters
    /// <see cref="PoliticalState"/>, is never serialised, and nothing in the engine has any reason to
    /// know a UI queue exists. Putting it in <c>Agora.Core</c> would push a presentation concern
    /// across the boundary that makes the engine testable without the game (<c>src/CLAUDE.md</c>).
    /// </para>
    /// <para>
    /// Every field is copied from state the engine already published — a catalog- or engine-authored
    /// severity, a date the tick was handed, a headline written in <c>AgoraRuntime</c>. Nothing here is
    /// computed at display time and, since v10 retired the article alert, <b>nothing on this class is
    /// model-authored at all</b> (non-negotiable #1); prose reaches a card only as a body the player
    /// opens. <see cref="Date"/> comes from the tick rather than from a clock of its own
    /// (non-negotiable #8).
    /// </para>
    /// </remarks>
    public sealed class NewsAlert
    {
        /// <summary>
        /// The ack key, and the key a card fetches its body under from <c>agora.news.article</c>.
        /// <para>
        /// Every id is prefixed, and as of v10 there is no exception —
        /// <c>"event:&lt;id&gt;"</c>, <c>"election:&lt;id&gt;"</c>, <c>"coalition:&lt;id&gt;"</c> for
        /// an ending and <c>"coalition:&lt;id&gt;:formed"</c> for a formation,
        /// <c>"party:&lt;id&gt;:founded"</c> / <c>":dissolved"</c>.
        /// </para>
        /// <para>
        /// The exception that existed until v10 is worth recording, because it is the shape of a whole
        /// class of bug this repo keeps meeting: an article alert carried the <b>bare</b>
        /// <c>Article.Id</c>, because that same string doubled as the map key. Prefixing it broke the
        /// fetch <b>silently</b> — <c>AgoraUiProjection.BuildArticle</c> answers an unknown id with an
        /// empty payload rather than throwing, so the player got a blank masthead and nothing was
        /// logged. The article alert is gone and the asymmetry with it; do not reintroduce an id that
        /// doubles as a lookup key without reading this paragraph first.
        /// </para>
        /// <para>
        /// <b>An id is still not itself an article id, and nothing on this class says whether a body
        /// exists.</b> The flag that used to is gone: its only producer retired with the feed, so it
        /// was false on every alert the engine raises and copying it into the payload published a
        /// permanent "no body" over prose that had been written. The answer is computed instead, at
        /// publish time, by <see cref="ElectionCoverage.ResolveArticleId"/> — the one resolver both
        /// <c>BuildAlerts</c> and <c>BuildArticle</c> ask, so the flag and the fetch cannot disagree.
        /// A field here could only be a second, staler copy of it.
        /// </para>
        /// </summary>
        public string Id = "";

        /// <summary>
        /// <c>"Event"</c>, <c>"Election"</c>, <c>"Coalition"</c> or <c>"Party"</c> — the closed
        /// vocabulary <c>NewsAlertPayload.Kind</c> publishes. <c>"Article"</c> was a fifth member and
        /// the initialiser here until v10 retired the article alert with the feed; the default is a
        /// kind that still exists, because a default naming a struck member is a card labelled with a
        /// category the dashboard has no map entry for.
        /// </summary>
        public string Kind = "Event";

        /// <summary>The date the tick was handed, never one computed here.</summary>
        public SimDate? Date;

        public string Headline = "";

        /// <summary>One line, engine-authored: what the card shows under the headline.</summary>
        public string Summary = "";

        /// <summary>
        /// Empty on every alert the engine raises today. Kept because the payload publishes the field
        /// and a card built from an article's masthead would fill it.
        /// </summary>
        public string OutletName = "";

        /// <summary>Empty when the item is about no single party; the card resolves the label.</summary>
        public string PartyId = "";

        public string DistrictId = "";

        public string EventId = "";

        /// <summary>1–5 for event-derived alerts, 0 otherwise.</summary>
        public int Severity;

        /// <summary>
        /// The engine's verdict on whether this one qualifies to hold the clock. Decided once, when the
        /// alert is raised, against the tuned threshold — never recomputed downstream.
        /// </summary>
        public bool Major;

        /// <summary>
        /// The alert id for an election, built in one place because two sites build it: the wake that
        /// records the round's coverage (<see cref="ElectionCoverage.Expect"/>) and the raise that
        /// enqueues the card. They must produce the identical string or the card resolves no body,
        /// and a second literal concatenation is how that comes to be untrue.
        /// </summary>
        public static string ElectionAlertId(string electionId)
        {
            return string.IsNullOrEmpty(electionId) ? "" : "election:" + electionId;
        }
    }
}
