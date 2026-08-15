using System;
using System.Collections.Generic;
using Agora.Core.Tuning;

namespace Agora.Core.Stories
{
    /// <summary>
    /// The 2-of-3 rule and its edge cases.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One static function over frozen contract types. It reads the story, the catalog the story's
    /// slots name, the month's readings and the <c>stories</c> tuning section, and returns a verdict.
    /// It holds no state, mutates none of its arguments, applies nothing and makes no stochastic
    /// draw — a verdict is a reading, not a sample, so there is nothing here for <c>SeedStreams</c>
    /// to seed. Awarding power for the verdict is <c>PoliticalPower</c>'s and dispatching its effects
    /// is wave 4's.
    /// </para>
    /// <para>
    /// Determinism: the only loops walk the story's own slot list, the catalog list and the recorded
    /// evidence list, all in the order they were given. No dictionary or hash set is enumerated, and
    /// the one collection this file builds is sorted by ordinal string comparison before it leaves.
    /// </para>
    /// </remarks>
    public static class StoryResolution
    {
        /// <summary>
        /// Scores every slot, then the story.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Per-slot verdict by response mode.</b> <c>Goal</c> runs the <see cref="CheckSpec"/>
        /// through <see cref="TriggerEvaluator"/>. <c>PowerOverride</c> is an automatic success that
        /// was already paid for. <c>Ignore</c> is an automatic failure — the player decided.
        /// <c>Manual</c> reads the player's own declaration and is <b>neutral until declared</b>,
        /// which is to say <see cref="SlotOutcome.Unmeasurable"/> rather than failed.
        /// <c>Unaddressed</c> is silence, not a decision, and is likewise not scored as failure.
        /// </para>
        /// <para>
        /// <b>The story threshold is a ratio over SCORED slots, not over all of them.</b> A full
        /// story of three needs <c>stories.successThreshold</c> met; a story of fewer than three —
        /// a degraded draft, or one whose slots went unmeasurable — needs <b>all</b> its scored slots
        /// met. A story with no scored slots at all resolves <see cref="StoryOutcome.Abandoned"/>:
        /// there is nothing to have a verdict about, and calling that a failure would charge the
        /// player for a sensor gap.
        /// </para>
        /// </remarks>
        /// <param name="story">The story to score. Not mutated — the verdict is returned, not applied.</param>
        /// <param name="catalog">
        /// The loaded civic events. A slot naming an event the catalog no longer holds cannot be
        /// checked, so it is <see cref="SlotOutcome.Unmeasurable"/> — a catalog that shrank under a
        /// save is a gap on our side, not something to charge the player for.
        /// </param>
        /// <param name="context">
        /// The month's readings. Its <see cref="StoryReadContext.RecordedEvidence"/> is preferred
        /// over a fresh measurement wherever it carries the metric — see
        /// <see cref="StoryResolutionResult.Evidence"/>.
        /// </param>
        /// <param name="tuning">Threshold source. Only the <c>stories</c> section is read.</param>
        public static StoryResolutionResult Resolve(Story story,
                                                    IReadOnlyList<CivicEvent> catalog,
                                                    StoryReadContext context,
                                                    EngineTuning tuning)
        {
            if (story == null) throw new ArgumentNullException(nameof(story));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            StoriesTuning t = tuning.Stories;
            var result = new StoryResolutionResult();
            var evidence = new List<MetricReading>();

            List<StorySlot> slots = story.Slots ?? new List<StorySlot>();

            for (int i = 0; i < slots.Count; i++)
            {
                StorySlot slot = slots[i];
                if (slot == null)
                {
                    // A hole in the slot list is not a verdict about anything. Keep the index
                    // alignment SlotOutcomes promises and score it as unreadable.
                    result.SlotOutcomes.Add(SlotOutcome.Unmeasurable);
                    continue;
                }

                CivicEvent? civicEvent = FindEvent(catalog, slot.EventId);
                SlotOutcome outcome = ScoreSlot(slot, civicEvent, context);

                result.SlotOutcomes.Add(outcome);
                if (outcome == SlotOutcome.Met) result.MetCount++;
                if (outcome == SlotOutcome.Met || outcome == SlotOutcome.NotMet) result.ScoredCount++;

                // Evidence is recorded for what was LOOKED at, not only for what scored: a goal that
                // came back unreadable is exactly the case a later replay has to reproduce.
                if (slot.Response == SlotResponse.Goal && civicEvent != null)
                {
                    RecordEvidence(evidence, civicEvent.Check, context);
                }
            }

            evidence.Sort(CompareByMetricId);
            result.Evidence = evidence;
            result.Outcome = Verdict(result.MetCount, result.ScoredCount, t);
            return result;
        }

        /// <summary>
        /// The verdict on a story that scored <paramref name="metCount"/> of
        /// <paramref name="scoredCount"/> slots.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Two rules and a floor, and the boundary between them is the whole point of this
        /// function.</b> A story with a full complement of scored slots — <c>eventsPerStory</c> of
        /// them — needs <c>successThreshold</c> met, which is the 2-of-3 the design is written
        /// around. A story with <i>fewer</i> scored slots than that needs <b>all</b> of them.
        /// </para>
        /// <para>
        /// The comparison is <c>met &gt;= required</c>, and the strictness of each half is chosen so
        /// that neither error the brief names can occur. Carrying the fixed <c>successThreshold</c>
        /// down to a short story would make a one-slot story — a mandatory event's bare story, or a
        /// three-slot story two of whose slots went unmeasurable — need two of its one slot, and so
        /// unwinnable however well the player played. Scaling the threshold down by one instead
        /// (<c>scored - 1</c>) would make a two-slot story succeed on a single met slot and so
        /// impossible to lose outright. Requiring all of a short story's scored slots sits between
        /// them: it is reachable at every size down to one, and at size one it still distinguishes
        /// met from not-met.
        /// </para>
        /// <para>
        /// Note what "all of them" does as unmeasurability grows: it makes the surviving slots
        /// matter <i>more</i>, not less. That is the deliberate reading of the rule — an unreadable
        /// slot leaves both halves of the ratio, so what is left is the whole of what we can honestly
        /// ask about, and the alternative (scaling the numerator down as well) would let a story with
        /// one readable slot succeed on zero met slots.
        /// </para>
        /// <para>
        /// <c>successThreshold</c> is clamped into <c>[1, scoredCount]</c> before use, so a
        /// misconfigured tuning file degrades rather than producing an unwinnable story (a threshold
        /// above the slot count) or a free one (a threshold of zero, which would make every story a
        /// success without a single met slot). The clamp is only ever reached by misconfiguration:
        /// under shipped tuning the threshold is 2 and a full story has 3 scored slots.
        /// </para>
        /// </remarks>
        private static StoryOutcome Verdict(int metCount, int scoredCount, StoriesTuning tuning)
        {
            // Nothing was readable, so there is nothing to have a verdict about. Abandoned pays out
            // in neither direction; Failure here would charge the player for a sensor gap.
            if (scoredCount <= 0) return StoryOutcome.Abandoned;

            int fullStory = tuning.EventsPerStory;
            int required = scoredCount < fullStory
                ? scoredCount
                : Clamp(tuning.SuccessThreshold, 1, scoredCount);

            return metCount >= required ? StoryOutcome.Success : StoryOutcome.Failure;
        }

        /// <summary>
        /// The verdict on one slot, by the way the player chose to tackle it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>Unaddressed</c> is <see cref="SlotOutcome.Unmeasurable"/> and that is a considered
        /// answer rather than a fallback. <see cref="SlotResponse"/> separates it from
        /// <c>Ignore</c> precisely because one is silence and the other is a decision, and its doc
        /// comment says only the second is the player's fault; scoring silence as
        /// <see cref="SlotOutcome.NotMet"/> would collapse that distinction and make the two
        /// responses identical in every way that reaches the numbers. It cannot be
        /// <see cref="SlotOutcome.Met"/> either — nothing was done. So it leaves both halves of the
        /// ratio, and a story the player never opened resolves <see cref="StoryOutcome.Abandoned"/>
        /// and costs nothing, which is the same treatment a sensor gap gets and for the same reason.
        /// </para>
        /// <para>
        /// A <c>Manual</c> slot is the same shape: neutral until <c>ManualDeclared</c>, then met.
        /// <c>PlayerText</c> is prose and is never read for a verdict (non-negotiable #1); the
        /// exploit that buys is closed on the award side, where a manual-declared slot pays the minor
        /// rate whatever the tier.
        /// </para>
        /// </remarks>
        private static SlotOutcome ScoreSlot(StorySlot slot, CivicEvent? civicEvent,
                                             StoryReadContext context)
        {
            switch (slot.Response)
            {
                case SlotResponse.PowerOverride:
                    // Already paid for at the point of choosing it. Nothing left to measure.
                    return SlotOutcome.Met;

                case SlotResponse.Ignore:
                    return SlotOutcome.NotMet;

                case SlotResponse.Manual:
                    return slot.ManualDeclared ? SlotOutcome.Met : SlotOutcome.Unmeasurable;

                case SlotResponse.Goal:
                    if (civicEvent == null || civicEvent.Check == null) return SlotOutcome.Unmeasurable;
                    return FromCheck(
                        TriggerEvaluator.EvaluateCheck(civicEvent.Check, slot.BaselineMetric, context));

                default:
                    return SlotOutcome.Unmeasurable;
            }
        }

        /// <summary>
        /// The slot outcome a <see cref="CheckResult"/> projects onto. One-to-one: the evaluator
        /// already answers in three states, and flattening its third one here is exactly the bug the
        /// three states exist to prevent.
        /// </summary>
        private static SlotOutcome FromCheck(CheckResult check)
        {
            switch (check)
            {
                case CheckResult.Met: return SlotOutcome.Met;
                case CheckResult.NotMet: return SlotOutcome.NotMet;
                default: return SlotOutcome.Unmeasurable;
            }
        }

        /// <summary>
        /// Notes the reading a check was scored against, for the story record.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>A recorded reading beats a fresh one.</b> When
        /// <see cref="StoryReadContext.RecordedEvidence"/> carries this metric the story is resolving
        /// early at the player's request and the reading was already taken; replaying it rather than
        /// measuring again is what makes an early resolve deterministic, because the command's timing
        /// is exogenous but the city it sampled has since moved. Measuring first and recording second
        /// would replay the same save to a different verdict.
        /// </para>
        /// <para>
        /// With no recorded reading, only a <see cref="TriggerScope.City"/> check contributes: no
        /// single number stands behind an <c>AnyDistrict</c> or <c>AllDistricts</c> verdict, so
        /// writing one down would be a fiction, and writing down a null would say "unreadable" of a
        /// check that was read perfectly well — and then be believed on replay.
        /// </para>
        /// </remarks>
        private static void RecordEvidence(List<MetricReading> evidence, CheckSpec check,
                                           StoryReadContext context)
        {
            TriggerSpec spec = check.Spec;
            if (spec == null) return;

            string metricId = spec.MetricId ?? "";
            if (metricId.Length == 0) return;   // a Manual-kind check reads no metric at all
            if (Contains(evidence, metricId)) return;

            double? recorded;
            if (TryRecorded(context, metricId, out recorded))
            {
                evidence.Add(new MetricReading { MetricId = metricId, Value = recorded });
                return;
            }

            if (spec.Scope != TriggerScope.City) return;

            evidence.Add(new MetricReading
            {
                MetricId = metricId,
                Value = MetricRegistry.ReadCity(context.Today, metricId)
            });
        }

        /// <summary>
        /// The recorded reading for <paramref name="metricId"/>, if the context carries one.
        /// </summary>
        /// <remarks>
        /// True with a null <paramref name="value"/> is a meaningful answer and not a miss: it is a
        /// recorded "this could not be read", and it has to replay as unmeasurable rather than send
        /// us back to the city for a second opinion.
        /// </remarks>
        private static bool TryRecorded(StoryReadContext context, string metricId, out double? value)
        {
            IReadOnlyList<MetricReading> recorded = context.RecordedEvidence;
            if (recorded != null)
            {
                for (int i = 0; i < recorded.Count; i++)
                {
                    MetricReading reading = recorded[i];
                    if (reading != null && string.Equals(reading.MetricId, metricId, StringComparison.Ordinal))
                    {
                        value = reading.Value;
                        return true;
                    }
                }
            }

            value = null;
            return false;
        }

        /// <summary>The catalog entry with this id, or null when the catalog no longer holds it.</summary>
        private static CivicEvent? FindEvent(IReadOnlyList<CivicEvent> catalog, string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return null;

            for (int i = 0; i < catalog.Count; i++)
            {
                CivicEvent entry = catalog[i];
                if (entry != null && string.Equals(entry.Id, eventId, StringComparison.Ordinal)) return entry;
            }

            return null;
        }

        private static bool Contains(List<MetricReading> evidence, string metricId)
        {
            for (int i = 0; i < evidence.Count; i++)
            {
                if (string.Equals(evidence[i].MetricId, metricId, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>Ordinal by metric id. Ids are unique in the list, so the sort is total.</summary>
        private static int CompareByMetricId(MetricReading a, MetricReading b)
        {
            return string.CompareOrdinal(a.MetricId, b.MetricId);
        }

        /// <summary>netstandard2.0 has no <c>Math.Clamp</c> — see the note in <c>Agora.Core.csproj</c>.</summary>
        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
