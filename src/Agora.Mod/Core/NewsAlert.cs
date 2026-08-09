using Agora.Core.Contracts;

namespace Agora.Mod.Core
{
    /// <summary>
    /// One interruption the player has not answered yet: a pointer at a feed row that already exists,
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
    /// severity, a date the tick was handed, prose that was validated when it arrived. Nothing here is
    /// computed at display time and nothing is model-authored beyond the prose fields
    /// (non-negotiable #1), and <see cref="Date"/> comes from the tick rather than from a clock of its
    /// own (non-negotiable #8).
    /// </para>
    /// </remarks>
    public sealed class NewsAlert
    {
        /// <summary>
        /// The feed-row id this points at: <c>"article:&lt;id&gt;"</c>, <c>"event:&lt;id&gt;"</c>,
        /// <c>"election:&lt;id&gt;"</c>, <c>"coalition:&lt;id&gt;"</c> or
        /// <c>"party:&lt;id&gt;:founded"</c> / <c>":dissolved"</c>. Also the ack key.
        /// </summary>
        public string Id = "";

        /// <summary>
        /// <c>"Article"</c>, <c>"Event"</c>, <c>"Election"</c>, <c>"Coalition"</c> or <c>"Party"</c> —
        /// the closed vocabulary <c>NewsHeadlinePayload.Kind</c> already uses.
        /// </summary>
        public string Kind = "Article";

        /// <summary>The date the tick was handed, never one computed here.</summary>
        public SimDate? Date;

        public string Headline = "";

        /// <summary>One line — the same first line of the body the feed row carries.</summary>
        public string Summary = "";

        /// <summary>Empty for every kind but an article.</summary>
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

        /// <summary>Whether <c>agora.news.article</c> may be fetched for <see cref="Id"/>.</summary>
        public bool HasArticle;
    }
}
