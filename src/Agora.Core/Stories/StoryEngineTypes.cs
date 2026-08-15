using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Stories
{
    /// <summary>
    /// Everything the trigger and check evaluators are allowed to read about the city.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A closed input, deliberately. The engine is a pure function of its inputs (non-negotiable #3),
    /// and handing the evaluator the whole world would make "what did this trigger depend on?"
    /// unanswerable. Everything here is data the tick already had.
    /// </para>
    /// <para>
    /// <b><see cref="History"/> is oldest-first and may be empty.</b> An empty history is the normal
    /// case on a young save, not an error — a <c>Delta</c> spec with no history to read is
    /// <see cref="CheckResult.Unmeasurable"/>, never <see cref="CheckResult.NotMet"/>, because
    /// "we cannot see" and "it did not happen" are different claims and only the second may cost the
    /// player anything.
    /// </para>
    /// </remarks>
    public sealed class StoryReadContext
    {
        /// <summary>The city as of the evaluated month. Never null.</summary>
        public CitySnapshot Today { get; set; } = new CitySnapshot();

        /// <summary>
        /// Earlier snapshots, oldest first, not including <see cref="Today"/>. Rehydrated ones are
        /// indistinguishable from measured ones <i>by construction</i> — see the remarks on
        /// <see cref="CheckResult.Unmeasurable"/> for why a rehydrated district cannot be asked
        /// whether it fell back.
        /// </summary>
        public IReadOnlyList<CitySnapshot> History { get; set; } = new List<CitySnapshot>();

        /// <summary>
        /// Readings recorded as a story's resolution evidence, sorted by metric id. When present
        /// these are preferred over a live measurement — this is what makes an early resolve
        /// deterministic on replay. Empty for an ordinary evaluation.
        /// </summary>
        public IReadOnlyList<MetricReading> RecordedEvidence { get; set; } = new List<MetricReading>();
    }

    /// <summary>What one cycle's drafting produced.</summary>
    public sealed class StoryDraftResult
    {
        /// <summary>Stories drafted this cycle, sorted by <c>Id</c> ordinal.</summary>
        public List<Story> DraftedStories { get; set; } = new List<Story>();

        /// <summary>
        /// The pool as it stands after the draw: drawn entries removed, every entry left behind with
        /// its <c>MissStreak</c> incremented, sorted by <c>EventId</c> ordinal.
        /// </summary>
        public List<EventPoolEntry> UpdatedPool { get; set; } = new List<EventPoolEntry>();

        /// <summary>
        /// Degradations that were applied, for the log. A degradation is <b>never an error</b>: a
        /// story with no major left promotes a minor, and a cycle with too few events drafts a
        /// shorter story rather than failing.
        /// </summary>
        public List<string> Degradations { get; set; } = new List<string>();
    }

    /// <summary>What resolving one story produced. Pure — nothing here is applied.</summary>
    public sealed class StoryResolutionResult
    {
        /// <summary>The verdict on the story as a whole.</summary>
        public StoryOutcome Outcome { get; set; } = StoryOutcome.Pending;

        /// <summary>
        /// Per-slot verdicts, in the story's own slot order so the two lists line up by index.
        /// </summary>
        public List<SlotOutcome> SlotOutcomes { get; set; } = new List<SlotOutcome>();

        /// <summary>Slots that resolved <see cref="SlotOutcome.Met"/>.</summary>
        public int MetCount { get; set; }

        /// <summary>
        /// Slots that counted toward the verdict — met plus not-met.
        /// </summary>
        /// <remarks>
        /// <b>Unmeasurable slots are in neither this nor <see cref="MetCount"/>.</b> They are excluded
        /// from both the numerator and the denominator of the 2-of-3, so a sensor gap cannot cost the
        /// player political power. A story whose every slot is unmeasurable has a
        /// <see cref="ScoredCount"/> of zero and resolves <see cref="StoryOutcome.Abandoned"/> rather
        /// than failed.
        /// </remarks>
        public int ScoredCount { get; set; }

        /// <summary>The readings the verdict was reached on, sorted by metric id.</summary>
        public List<MetricReading> Evidence { get; set; } = new List<MetricReading>();
    }
}
