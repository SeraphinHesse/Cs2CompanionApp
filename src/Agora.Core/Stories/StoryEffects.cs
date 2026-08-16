using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Engine.Effects;
using Agora.Core.Tuning;

namespace Agora.Core.Stories
{
    /// <summary>
    /// Turns a story's authored effect id lists into capped <see cref="EffectRequest"/>s.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The existing resolver does the capping; there is no second clamp in this file.</b>
    /// <see cref="EffectPalette"/> and <see cref="EffectResolver"/> already carry every magnitude cap,
    /// duration cap and fallback chain, and the sink clamps again on the far side. Non-negotiable #5
    /// is satisfied by going through them, and a cap enforced in a second place is a cap that will
    /// eventually disagree with the first.
    /// </para>
    /// <para>
    /// <b>A story names an effect and nothing else, so the declaration supplies the rest.</b> A
    /// <see cref="TimelineEventEffect"/> authors a magnitude and a duration;
    /// <see cref="CivicEvent.ActiveEffects"/> and its two siblings are bare id lists. The magnitude is
    /// therefore the entry's own <c>magnitudeCap</c> — the only figure the palette declares for it —
    /// and <c>stories.activeEffectScale</c>, <c>successEffectScale</c> and <c>failureEffectScale</c>
    /// are the fraction of that ceiling each phase asks for. All three ship positive and none of them
    /// carries a sign: the palette has no sign either, and the authored catalogs say so in as many
    /// words. Severity then scales the request through the effects packet's own
    /// <see cref="EffectResolver.ScaleForSeverity"/>, so a story effect and a timeline effect read
    /// severity through one function rather than two.
    /// </para>
    /// <para>
    /// <b>Duration, and a cap is a ceiling rather than a default.</b> A live story's effect lasts the
    /// story: <c>stories.cycleMonths</c>. A resolution's is the consequence and lasts
    /// <c>stories.resolutionEffectMonths</c> — <b>not</b> the palette entry's own
    /// <c>durationCapMonths</c>, which is 24 to 60 months against a two-month cadence and so let 12 to
    /// 30 cycles of consequences overlap on one modifier. That is what the key exists to bound; the
    /// entry's cap still clamps the request through <see cref="EffectResolver"/>, so tuning can only
    /// ever shorten it.
    /// </para>
    /// <para>
    /// <b>Breadth is capped here, against <c>stories.maxStoryEffectsPerModifier</c>.</b> The effects
    /// packet ships <c>stackingMode: sum</c> with <c>maxStackedPerModifier</c> 4, so several slots of
    /// one story sharing a modifier reach that limit and the surplus is <i>silently dropped</i> in the
    /// ledger. The count is kept per (scope, modifier, district) — the same key
    /// <see cref="EffectResolver.Stack"/> groups on — and it is taken <b>after</b> resolution, because
    /// degradation down a fallback chain is exactly what makes several ids converge on one modifier.
    /// <b>It is applied per call, so on the resolution path it bounds one story's breadth and never
    /// the cycle's</b>; what keeps concurrent story effects under the ledger limit is
    /// <c>stories.resolutionEffectMonths</c> deciding how many cycles can overlap at all.
    /// </para>
    /// <para>
    /// <b>An unmeasurable slot requests nothing.</b> It means the engine could not read the city, and
    /// a sensor gap must not move the city either — the same rule
    /// <see cref="PoliticalPower.AwardFor"/> applies to the currency.
    /// </para>
    /// <para>
    /// <b>The caller names the district, exactly as the scheduler does on the timeline path.</b> An
    /// <see cref="EffectRequest"/> of district scope requires a district id and throws without one,
    /// and catalog entries never name one — real events do not know the player's district names. So
    /// the target arrives as a parameter, which is the same shape
    /// <see cref="EffectResolver.ResolveForEvent"/> already has. It is deliberately <i>not</i> a field
    /// on <see cref="Story"/> or <see cref="StorySlot"/>: those are persisted, so it would need a
    /// sidecar migration, and it would be the wrong answer for two of a story's three slots anyway.
    /// An empty target keeps the degraded behaviour — district-scoped ids are skipped — because a
    /// city with no districts is a real save state and not an error.
    /// </para>
    /// <para>
    /// <b>Determinism.</b> Requests leave in a declared total order: the story list order, then the
    /// story's own slot order, then the authored effect list order. The one dictionary in the file
    /// counts modifiers and is never enumerated, so no hash order reaches the output.
    /// </para>
    /// </remarks>
    public static class StoryEffects
    {
        /// <summary>
        /// What the city's live stories are doing to it right now, from every open slot's
        /// <see cref="CivicEvent.ActiveEffects"/>.
        /// </summary>
        /// <param name="districtId">
        /// Where a district-scoped effect lands, chosen by the caller from the tick's snapshot the way
        /// the scheduler chooses one for a timeline event. Empty or null skips district-scoped ids
        /// rather than throwing — a city with no districts is a save state, not an error.
        /// </param>
        /// <remarks>
        /// One call covers every live story, so on this path the breadth cap is a cycle-wide bound
        /// rather than a per-story one. The resolution path is the opposite; see there.
        /// </remarks>
        public static List<EffectRequest> ForActive(IReadOnlyList<Story> live,
                                                    IReadOnlyList<CivicEvent> catalog,
                                                    string districtId,
                                                    EngineTuning tuning)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            var draft = new Draft(districtId, tuning);
            if (live == null || !draft.IsLive) return draft.Requests;

            StoriesTuning t = tuning.Stories;

            for (int s = 0; s < live.Count; s++)
            {
                Story story = live[s];
                if (story == null) continue;

                List<StorySlot> slots = story.Slots ?? new List<StorySlot>();
                for (int i = 0; i < slots.Count; i++)
                {
                    StorySlot slot = slots[i];
                    if (slot == null) continue;

                    // Defensive: a live slot is Pending. One that already came back unreadable asks
                    // for nothing, for the same reason it costs the player nothing.
                    if (slot.SlotOutcome == SlotOutcome.Unmeasurable) continue;

                    CivicEvent? civicEvent = FindEvent(catalog, slot.EventId);
                    if (civicEvent == null) continue;

                    draft.AddAll(civicEvent.ActiveEffects, civicEvent.Severity, t.ActiveEffectScale,
                                 t.CycleMonths, SourceFor(story, slot));
                }
            }

            return draft.Requests;
        }

        /// <summary>
        /// The consequence of one story's verdict, from each slot's
        /// <see cref="CivicEvent.SuccessEffects"/> or <see cref="CivicEvent.FailureEffects"/>
        /// according to that slot's own outcome.
        /// </summary>
        /// <param name="outcomes">
        /// Index-aligned with <c>story.Slots</c>, exactly as
        /// <see cref="StoryResolutionResult.SlotOutcomes"/> promises. A shorter or longer list is a
        /// caller defect, not something to paper over.
        /// </param>
        /// <param name="districtId">
        /// Where a district-scoped effect lands. Same contract as on <see cref="ForActive"/>.
        /// </param>
        /// <remarks>
        /// <b>A call is one story, so the breadth cap bounds one story's breadth and never the
        /// cycle's.</b> Two stories resolving in one cycle can each contribute up to the cap on a
        /// shared modifier. That is deliberate rather than a residue to be closed here: bounding the
        /// cycle would need state carried across calls, which the seam has nowhere to keep and which
        /// would make the answer depend on call order. What actually keeps concurrent consequences
        /// under <c>effects.maxStackedPerModifier</c> is <c>stories.resolutionEffectMonths</c>, which
        /// decides how many cycles overlap at all.
        /// </remarks>
        public static List<EffectRequest> ForResolution(Story story,
                                                        IReadOnlyList<SlotOutcome> outcomes,
                                                        IReadOnlyList<CivicEvent> catalog,
                                                        string districtId,
                                                        EngineTuning tuning)
        {
            if (story == null) throw new ArgumentNullException(nameof(story));
            if (outcomes == null) throw new ArgumentNullException(nameof(outcomes));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            List<StorySlot> slots = story.Slots ?? new List<StorySlot>();
            if (outcomes.Count != slots.Count)
            {
                throw new ArgumentException(
                    "Outcome list is not index-aligned with the story's slots: " + outcomes.Count
                    + " outcomes for " + slots.Count + " slots.", nameof(outcomes));
            }

            var draft = new Draft(districtId, tuning);
            if (!draft.IsLive) return draft.Requests;

            StoriesTuning t = tuning.Stories;

            for (int i = 0; i < slots.Count; i++)
            {
                StorySlot slot = slots[i];
                if (slot == null) continue;

                SlotOutcome outcome = outcomes[i];

                // Unmeasurable and Pending both ask for nothing. A reading the engine could not take
                // is not a verdict, and a verdict is what a consequence follows from.
                if (outcome != SlotOutcome.Met && outcome != SlotOutcome.NotMet) continue;

                CivicEvent? civicEvent = FindEvent(catalog, slot.EventId);
                if (civicEvent == null) continue;

                bool met = outcome == SlotOutcome.Met;
                List<string> ids = met ? civicEvent.SuccessEffects : civicEvent.FailureEffects;
                double scale = met ? t.SuccessEffectScale : t.FailureEffectScale;

                // A consequence has its own length, and it is not the entry's cap. See the class
                // remarks: reading a ceiling as a default let 12 to 30 cycles overlap on one modifier.
                draft.AddAll(ids, civicEvent.Severity, scale, t.ResolutionEffectMonths,
                             SourceFor(story, slot));
            }

            return draft.Requests;
        }

        /// <summary>
        /// The cause recorded on every request from one slot, in both phases.
        /// </summary>
        /// <remarks>
        /// <b>The phase is deliberately not part of it.</b> <c>EffectLedger.Add</c> identifies an
        /// entry by (effect, district, source) and <b>replaces a repeat in place</b> — magnitude,
        /// start month and duration all overwritten, whether the new one is louder or quieter. So a
        /// failure that re-applies an id the active phase already applied restates the same crisis
        /// once rather than stacking it on itself twice. It happens to be louder at shipped tuning
        /// (<c>failureEffectScale</c> above <c>activeEffectScale</c>) and nothing here relies on that.
        /// </remarks>
        private static string SourceFor(Story story, StorySlot slot) =>
            (story.Id ?? "") + "/" + (slot.EventId ?? "");

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

        /// <summary>
        /// One call's accumulating request list, plus the per-modifier count the breadth cap reads.
        /// </summary>
        private sealed class Draft
        {
            private readonly EffectPalette _palette;
            private readonly EffectsTuning _effects;
            private readonly int _breadthCap;
            private readonly string _districtId;
            private readonly Dictionary<string, int> _perModifier =
                new Dictionary<string, int>(StringComparer.Ordinal);

            internal readonly List<EffectRequest> Requests = new List<EffectRequest>();

            internal Draft(string? districtId, EngineTuning tuning)
            {
                _palette = EffectPalette.From(tuning);
                _effects = tuning.Effects;
                _districtId = districtId ?? "";

                // Floored at 1 exactly as EffectResolver.Stack floors its own, so a hand-edited zero
                // degrades to "one per modifier" rather than to a packet that silently does nothing.
                int cap = tuning.Stories.MaxStoryEffectsPerModifier;
                _breadthCap = cap < 1 ? 1 : cap;

                IsLive = tuning.Stories.Enabled && _palette.Enabled;
            }

            /// <summary>False when either master switch is off, which makes the whole draft empty.</summary>
            internal bool IsLive { get; }

            internal void AddAll(IReadOnlyList<string>? effectIds, int severity, double phaseScale,
                                 int durationMonths, string sourceId)
            {
                if (effectIds == null) return;

                // Authored order, which the catalog loader has already sorted by id.
                for (int i = 0; i < effectIds.Count; i++)
                    Add(effectIds[i], severity, phaseScale, durationMonths, sourceId);
            }

            private void Add(string? effectId, int severity, double phaseScale, int durationMonths,
                             string sourceId)
            {
                if (string.IsNullOrEmpty(effectId)) return;

                // CapFor never returns something uncapped: an unregistered id comes back at the global
                // caps and resolves down to the scope's terminal, per §13.5's "never cut an event for a
                // missing effect". The registry decides scope; the id's spelling does not.
                EffectCap cap = _palette.CapFor(effectId, EffectScope.City);

                bool district = cap.Scope == EffectScope.District;

                // No target to land on. Every district chain terminates at a district entry, so there
                // is nothing to degrade to either — see the class remarks.
                if (district && _districtId.Length == 0) return;

                double scale = phaseScale;
                if (double.IsNaN(scale) || double.IsInfinity(scale) || scale < 0.0) scale = 0.0;

                double requested = EffectResolver.ScaleForSeverity(
                    _effects, _palette.EffectiveMagnitudeCap(cap) * scale, severity);

                // The requested duration, not the entry's ceiling. EffectResolver.ClampDuration then
                // shortens it to the ceiling wherever the entry declares a tighter one.
                if (durationMonths <= 0) return; // The resolver would drop it as ZeroDuration anyway.

                var request = new EffectRequest(effectId!, cap.Scope, requested, durationMonths,
                                                district ? _districtId : null, sourceId);
                EffectResolution resolved = EffectResolver.Resolve(_palette, request);
                if (!resolved.IsApplicable) return;

                if (!TakeModifierSlot(resolved)) return;

                Requests.Add(resolved.Request);
            }

            /// <summary>
            /// Whether one more story effect may land on this resolution's modifier, and books it if
            /// so. Keyed on (scope, modifier, district) — <see cref="EffectResolver.Stack"/>'s own
            /// grouping, so the count here means the same thing the ledger's limit does.
            /// </summary>
            private bool TakeModifierSlot(EffectResolution resolved)
            {
                string modifier = string.IsNullOrEmpty(resolved.Modifier)
                    ? resolved.Request.EffectId
                    : resolved.Modifier;

                string key = resolved.Request.Scope.ToString() + "|" + modifier + "|"
                             + (resolved.Request.DistrictId ?? "");

                int used;
                if (!_perModifier.TryGetValue(key, out used)) used = 0;
                if (used >= _breadthCap) return false;

                _perModifier[key] = used + 1;
                return true;
            }
        }
    }
}
