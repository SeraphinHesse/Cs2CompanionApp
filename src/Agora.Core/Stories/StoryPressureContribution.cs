using Agora.Core.Contracts;

namespace Agora.Core.Stories
{
    /// <summary>
    /// One story's contribution to the voter model for one tick. Derived every tick and never
    /// persisted: it is a projection of state that already survives a reload, not state of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two halves, and keeping them apart is the whole point.</b> The wave-3 ruling on
    /// <see cref="CivicEvent.ActivePressure"/> split <i>salience</i> from <i>credit</i> because an
    /// <see cref="IssuePosition"/> cannot express both. Salience says which issues the city is
    /// arguing about; it is dot-producted against each party's platform by
    /// <c>AffinityEngine</c> exactly as a timeline event's pressure is, and it has no idea who
    /// governs. Credit says the government did or did not deliver, which is a statement about one
    /// party rather than about an axis. All three content lanes independently tried to encode the
    /// second as the first, under which fixing the clinics rewarded the anti-services party.
    /// </para>
    /// <para>
    /// <b>The authored catalogs carry only the first half.</b> The second is derived — from the
    /// slot's own outcome and tier through <c>stories.enfranchisementWeight</c> and
    /// <c>stories.alienationWeight</c>, which have existed and gone unread since wave 2. Nothing in
    /// any catalog expresses it and nothing should: an author writes what the story is about, not
    /// how much the mayor is to blame.
    /// </para>
    /// </remarks>
    public sealed class StoryPressureContribution
    {
        /// <summary>The story this came from. The sort key, compared ordinal.</summary>
        public string StoryId { get; set; } = "";

        /// <summary>
        /// Salience: which issues this story pushes the city toward, per axis, in <c>[-1, +1]</c>.
        /// </summary>
        /// <remarks>
        /// Consumed by dot product against a party's platform, so its <i>direction</i> is a claim
        /// about the argument and never about a party. A sign flip between a story's live and
        /// resolved pressure is rejected at load time as <c>PressureSignFlip</c>; nothing here
        /// re-checks that, because by this point the catalog has already passed.
        /// </remarks>
        public IssuePosition Pressure { get; set; } = IssuePosition.Centre;

        /// <summary>
        /// Credit owed to whoever is governing, in <c>[-1, +1]</c>. Positive pulls voters toward the
        /// government, negative pushes them away, zero is the live-but-undecided case.
        /// </summary>
        /// <remarks>
        /// <b>Zero when nobody governs.</b> There is no one to credit or blame during a caretaker
        /// gap, and spreading the movement over the opposition instead would reward parties for a
        /// verdict none of them was in office for.
        /// </remarks>
        public double GovernmentCredit { get; set; }

        /// <summary>
        /// The story's severity, 1–5 — the major slot's, or the only slot's on a mandatory story.
        /// Scales the term exactly as <c>AffinityEngine.SeverityScale</c> scales a timeline event's.
        /// </summary>
        /// <remarks>
        /// <b>One number per concept</b> (owner decision 5): this is the same 1–5 integer the tier
        /// projection reads, not a second magnitude alongside it.
        /// </remarks>
        public int Severity { get; set; } = 1;

        /// <summary>
        /// The month the story opened. The decay anchor, read the same way
        /// <c>AffinityEngine.EventDecay</c> reads a fired event's date.
        /// </summary>
        public SimDate OpenedDate { get; set; }
    }
}
