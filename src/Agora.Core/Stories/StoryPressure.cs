using System;
using System.Collections.Generic;
using System.Linq;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Stories
{
    /// <summary>
    /// Turns stories into what the voter model reads: salience from the catalog, credit from the
    /// verdict.
    /// </summary>
    /// <remarks>
    /// <para><b>Salience — read, not invented.</b></para>
    /// <para>
    /// A live slot contributes its event's <see cref="CivicEvent.ActivePressure"/>; a slot that
    /// resolved met contributes <see cref="CivicEvent.SuccessPressure"/>, one that resolved not-met
    /// <see cref="CivicEvent.FailurePressure"/>. All three point the same way on each axis by
    /// construction and differ only in magnitude — that is machine-checked at load time as
    /// <c>PressureSignFlip</c>, so nothing here re-checks it and <b>nothing here negates one</b>. The
    /// only consumer is a dot product against a party's platform, so a negated success pressure would
    /// not release the argument, it would move voters to the opposite pole: fixing the clinics would
    /// reward the anti-services party. An unmeasurable slot contributes nothing at all.
    /// </para>
    ///
    /// <para><b>Credit — derived, and nothing in any catalog expresses it.</b></para>
    /// <para>
    /// A story the government delivered on pulls voters <i>toward</i> whoever governs, and one it
    /// failed pushes them away — regardless of which issues the story was about, and regardless of
    /// which party happens to agree with those issues. <c>stories.enfranchisementWeight</c> and
    /// <c>stories.alienationWeight</c> are the two dials, and their own doc comments already define
    /// them as exactly this. Both have existed since wave 2 and neither had ever been read.
    /// </para>
    /// <para>
    /// <b>Stake is the slot's tier, and the tier is severity.</b> Credit is scaled by what was
    /// actually at stake: the slot's severity over <c>catalog.severityMax</c>, which is the number
    /// <see cref="StoryTiers"/> projects the tier from. Mapping the three tier names onto three fresh
    /// coefficients would be a second magnitude for a concept that already has exactly one number
    /// (owner decision 5), and it would drift from the thresholds on the next balance pass.
    /// </para>
    /// <para>
    /// <b>Two ceilings for the 1–5 range, and they are only aligned by coincidence.</b> The severity
    /// this file writes onto <see cref="StoryPressureContribution.Severity"/> is clamped to
    /// <c>catalog.severityMax</c>, while <c>AffinityEngine.SeverityScale</c> divides by its own
    /// <c>MaxEventSeverity</c> constant — which that file documents as a schema bound rather than a
    /// dial, and which is therefore not going to follow a retune. Both are 5 today, so the story term
    /// scales exactly as intended. Lower <c>catalog.severityMax</c> and every story's term silently
    /// shrinks by the ratio; raise it and the clamp here stops binding before the divide there does.
    /// Recorded rather than repaired: reconciling them means deciding which of the two is the
    /// authority, and that is a spine question about a spine constant.
    /// </para>
    /// <para>
    /// <b>Zero while the story is live</b> — the city has not yet learned whether the mayor delivered
    /// — and the sum is bounded to <c>[-1, +1]</c> before it leaves, for the same reason
    /// <c>AffinityEngine.EventTerm</c> clamps before weighting: without a bound a busy cycle drowns
    /// every other term and the model stops discriminating between a flood and a bus-fare rise.
    /// </para>
    /// <para>
    /// <b>Zero when nobody governs, decided by the consumer.</b> Credit is a figure owed to whoever is
    /// in office, and this function is not told who that is — deliberately, because
    /// <c>AffinityEngine.StoryTerm</c> is where the question is already answered: it pays credit to
    /// governing parties only, so a caretaker gap pays it to nobody. It also does <i>not</i> spread a
    /// mirrored negative over the opposition, because affinity is normalised into shares and paying
    /// the movement explicitly would count it twice. Re-asking the question here would be a second
    /// answer to it.
    /// </para>
    ///
    /// <para><b>Determinism.</b></para>
    /// <para>
    /// The story lists are walked in the order given and the slots in the story's own order; the sums
    /// run in that declared order and the result is sorted by <c>StoryId</c> ordinal. The sort is
    /// <c>OrderBy</c>, which is documented stable, so two rows carrying the same id keep their
    /// emission order — <b>there is no explicit tiebreak and the stability is what supplies one</b>.
    /// <c>List{T}.Sort</c> would not, and swapping to it without adding a tiebreak would be a
    /// determinism regression that no shipped catalog could surface. No dictionary is enumerated
    /// anywhere.
    /// </para>
    /// </remarks>
    public static class StoryPressure
    {
        /// <summary>
        /// One contribution per story that should move the voter model this tick — the open ones and
        /// the ones that reached a verdict on this very tick.
        /// </summary>
        /// <param name="live">Stories still open, sorted by <c>Id</c> ordinal.</param>
        /// <param name="justResolved">
        /// Stories that reached a verdict on this tick. Separate from <paramref name="live"/> because
        /// a verdict lands in the same tick the pressures it changes are read — that ordering is why
        /// the story stage sits before affinity rather than after it.
        /// </param>
        /// <remarks>
        /// A story that would move nothing at all — no salience on any axis and no credit — is left
        /// out rather than reported as an inert row. That is the shape of a story every slot of which
        /// went unreadable, and it is the same posture the resolution takes: a sensor gap moves the
        /// city in no direction.
        /// </remarks>
        public static List<StoryPressureContribution> For(IReadOnlyList<Story> live,
                                                          IReadOnlyList<Story> justResolved,
                                                          IReadOnlyList<CivicEvent> catalog,
                                                          EngineTuning tuning)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            var contributions = new List<StoryPressureContribution>();
            if (!tuning.Stories.Enabled) return contributions;

            Collect(contributions, live, catalog, tuning, resolved: false);
            Collect(contributions, justResolved, catalog, tuning, resolved: true);

            // OrderBy is a stable sort, so two rows carrying the same id keep their emission order
            // rather than being reordered by an unspecified comparison. Same call the affinity
            // context makes on the same key.
            return contributions.OrderBy(c => c.StoryId, StringComparer.Ordinal).ToList();
        }

        private static void Collect(List<StoryPressureContribution> into, IReadOnlyList<Story>? stories,
                                    IReadOnlyList<CivicEvent> catalog, EngineTuning tuning, bool resolved)
        {
            if (stories == null) return;

            for (int i = 0; i < stories.Count; i++)
            {
                StoryPressureContribution? contribution = Build(stories[i], catalog, tuning, resolved);
                if (contribution != null) into.Add(contribution);
            }
        }

        /// <summary>
        /// One story's contribution, or null when it moves nothing.
        /// </summary>
        private static StoryPressureContribution? Build(Story? story, IReadOnlyList<CivicEvent> catalog,
                                                        EngineTuning tuning, bool resolved)
        {
            if (story == null) return null;

            StoriesTuning t = tuning.Stories;
            int severityMax = tuning.Catalog.SeverityMax;
            if (severityMax < 1) severityMax = 1;

            List<StorySlot> slots = story.Slots ?? new List<StorySlot>();

            IssuePosition pressure = IssuePosition.Centre;
            double credit = 0.0;
            int severity = 0;

            for (int i = 0; i < slots.Count; i++)
            {
                StorySlot slot = slots[i];
                if (slot == null) continue;

                CivicEvent? civicEvent = FindEvent(catalog, slot.EventId);
                if (civicEvent == null) continue;

                // Slots arrive Major first, then by event id ordinal, so the first one the catalog
                // still explains is the major slot — or the only slot on a mandatory story.
                if (severity == 0) severity = Clamp(civicEvent.Severity, 1, severityMax);

                SlotOutcome outcome = slot.SlotOutcome;
                if (outcome == SlotOutcome.Unmeasurable) continue;

                if (!resolved)
                {
                    // The argument is running. Salience only: nobody yet knows how it ends.
                    pressure = pressure.Add(civicEvent.ActivePressure);
                    continue;
                }

                if (outcome != SlotOutcome.Met && outcome != SlotOutcome.NotMet) continue;

                bool met = outcome == SlotOutcome.Met;

                // Same direction as the live pressure, quieter on success and louder on failure. Never
                // negated — see the class remarks.
                pressure = pressure.Add(met ? civicEvent.SuccessPressure : civicEvent.FailurePressure);

                double stake = Clamp(civicEvent.Severity, 1, severityMax) / (double)severityMax;
                double weight = NonNegative(met ? t.EnfranchisementWeight : t.AlienationWeight);
                credit += (met ? weight : -weight) * stake;
            }

            if (severity == 0) severity = 1;

            pressure = pressure.Clamped();
            credit = Clamp(credit, -1.0, 1.0);

            if (IsCentre(pressure) && credit == 0.0) return null;

            return new StoryPressureContribution
            {
                StoryId = story.Id ?? "",
                Pressure = pressure,
                GovernmentCredit = credit,
                Severity = severity,
                OpenedDate = story.OpenedDate
            };
        }

        /// <summary>The catalog entry with this id, or null when the catalog no longer holds it.</summary>
        private static CivicEvent? FindEvent(IReadOnlyList<CivicEvent> catalog, string? eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return null;

            for (int i = 0; i < catalog.Count; i++)
            {
                CivicEvent entry = catalog[i];
                if (entry != null && string.Equals(entry.Id, eventId, StringComparison.Ordinal)) return entry;
            }

            return null;
        }

        private static bool IsCentre(IssuePosition p)
        {
            for (int i = 0; i < Issues.All.Count; i++)
                if (p[Issues.All[i]] != 0.0) return false;

            return true;
        }

        /// <summary>
        /// A dial read as a magnitude. <b>A negative weight is read as zero, never as an inversion</b>
        /// — a hand-edited <c>enfranchisementWeight</c> below zero would make delivering a story punish
        /// the government, which is the one reading this whole split exists to make impossible.
        /// </summary>
        private static double NonNegative(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v < 0.0) return 0.0;
            return v;
        }

        // netstandard2.0 has no Math.Clamp.
        private static double Clamp(double v, double min, double max)
        {
            if (double.IsNaN(v)) return 0.0;
            return v < min ? min : (v > max ? max : v);
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    }
}
