using Agora.Core.Contracts;

namespace Agora.Mod.Core
{
    /// <summary>
    /// One story the player has not answered yet: a pointer at a story that already exists in
    /// <c>PoliticalState.LiveStories</c>, held in a session-scoped queue until it is acked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A Mod-side class, deliberately not a Core contract</b>, for exactly the reasons written on
    /// <see cref="NewsAlert"/>: it never enters <see cref="PoliticalState"/>, is never serialised, and
    /// nothing in the engine has any reason to know a UI queue exists. Putting it in
    /// <c>Agora.Core</c> would push a presentation concern across the boundary that makes the engine
    /// testable without the game (<c>src/CLAUDE.md</c>).
    /// </para>
    /// <para>
    /// <b>One card per story, never one per event.</b> All of a story's slots render inside this one
    /// card, which is why there is no <c>EventId</c> here and a <see cref="SlotCount"/> instead. At
    /// two stories per cycle that is two interruptions; one card per event would be six serialised
    /// forced pauses on the first frame of the month, each needing its own ack round trip.
    /// </para>
    /// <para>
    /// Every field is copied from state the engine already published — a story id the assembler
    /// wrote, a date the tick was handed, prose that was validated when it arrived. Nothing here is
    /// computed at display time (non-negotiable #1), and <see cref="Date"/> comes from the tick rather
    /// than from a clock of its own (non-negotiable #8).
    /// </para>
    /// </remarks>
    public sealed class StoryAlert
    {
        /// <summary>
        /// The story id this points at, and the ack key.
        /// <para>
        /// <b>Bare, with no prefix</b> — it is simultaneously the <c>agora.stories.article</c> map key,
        /// so the card fetches its body with <c>useMapValue(storyArticle$, id)</c> using this very
        /// string. The news lane's article ids are bare for the same reason and its event, election,
        /// coalition and party ids are prefixed because they are not map keys. There is only one kind
        /// of story alert, so there is no namespace to separate and nothing to prefix it with.
        /// </para>
        /// </summary>
        public string Id = "";

        /// <summary>The date the tick was handed, never one computed here.</summary>
        public SimDate? Date;

        /// <summary>The story's headline — the canned pool's, which always exists.</summary>
        public string Headline = "";

        /// <summary>One line: the major event's description.</summary>
        public string Summary = "";

        /// <summary>How many events this story bundles. One card shows all of them.</summary>
        public int SlotCount;

        /// <summary>
        /// The engine's verdict on whether this card qualifies to hold the clock. Decided once, when
        /// the alert is raised, from the story's own major slot against the tuned severity threshold —
        /// never recomputed downstream, and never re-derived by the UI.
        /// </summary>
        public bool Major;
    }
}
