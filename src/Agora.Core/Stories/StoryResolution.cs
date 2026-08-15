using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
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
    /// Determinism: the loops walk the story's own slot list, the catalog list and the recorded
    /// evidence list in the order they were given, and the district list in a copy this file sorts
    /// by ordinal id rather than trusting the producer's ordering. No dictionary or hash set is
    /// enumerated, and the one collection built here is sorted by ordinal string comparison over its
    /// full composite key before it leaves.
    /// </para>
    /// </remarks>
    public static class StoryResolution
    {
        /// <summary>
        /// The <see cref="MetricReading.DistrictId"/> of a city-wide reading. Empty by the contract's
        /// own definition, named here so the composite key never reads as a bare <c>""</c>.
        /// </summary>
        private const string CityWide = "";

        /// <summary>
        /// Scores every slot, then the story.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Per-slot verdict by response mode.</b> <c>Goal</c> runs the <see cref="CheckSpec"/>
        /// through <see cref="TriggerEvaluator"/>. <c>PowerOverride</c> is an automatic success that
        /// was already paid for. <c>Ignore</c> is an automatic failure — the player decided.
        /// <c>Manual</c> reads the player's own declaration, and an undeclared one at resolution
        /// scores as failure, as does <c>Unaddressed</c>: the story was open for a full cycle and
        /// declining to engage is a decision the city feels. See <see cref="SlotResponse"/> for why
        /// that is not the same claim as <see cref="SlotOutcome.Unmeasurable"/>.
        /// </para>
        /// <para>
        /// <b>The story threshold is a ratio over SCORED slots, not over all of them.</b> A full
        /// story of three needs <c>stories.successThreshold</c> met; a story of fewer than three —
        /// a degraded draft, or one whose slots went unmeasurable — needs <b>all</b> its scored slots
        /// met. A story with no scored slots at all resolves <see cref="StoryOutcome.Abandoned"/>:
        /// there is nothing to have a verdict about, and calling that a failure would charge the
        /// player for a sensor gap. <b>Only a reading that could not be taken reaches that state</b>
        /// — a story the player never opened scores three not-mets and fails.
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
        /// one readable slot succeed on zero met slots. Only a genuine sensor gap shrinks a story
        /// this way: silence scores not-met and keeps its slot in the denominator.
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
            // in neither direction; Failure here would charge the player for a sensor gap. This is
            // now reachable only through unreadable checks and slots the catalog no longer explains
            // — an unanswered story fills its slots with not-mets and fails like any other.
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
        /// <b><c>Unaddressed</c> scores <see cref="SlotOutcome.NotMet"/>, and so does a
        /// <c>Manual</c> slot still undeclared when its story resolves.</b> The story was open for a
        /// full cycle; declining to engage with it is a decision the city feels, and the remarks on
        /// <see cref="SlotResponse"/> hold the argument — scoring silence as neutral made doing
        /// nothing strictly cheaper than every response that could fail, which inverts the premise
        /// of a feature about tackling each event.
        /// </para>
        /// <para>
        /// What silence is <i>not</i> is a sensor gap. <see cref="SlotOutcome.Unmeasurable"/> keeps
        /// exactly one meaning here — the engine could not read the city — and nothing in this
        /// function routes a player state through it. That is what lets a later reader tell an
        /// outage from a disengaged player, and it is unrecoverable once the two are merged.
        /// </para>
        /// <para>
        /// A declared <c>Manual</c> slot is met; <c>PlayerText</c> is prose and is never read for a
        /// verdict (non-negotiable #1). The exploit that buys is closed on the award side, where a
        /// manual-declared slot's <i>award</i> is capped at the minor rate whatever the tier while
        /// its penalty is not.
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
                case SlotResponse.Unaddressed:
                    return SlotOutcome.NotMet;

                case SlotResponse.Manual:
                    return slot.ManualDeclared ? SlotOutcome.Met : SlotOutcome.NotMet;

                case SlotResponse.Goal:
                    // A catalog that no longer explains this slot is a gap on our side, so it degrades
                    // to unreadable rather than throwing or failing the player.
                    if (civicEvent == null || civicEvent.Check == null) return SlotOutcome.Unmeasurable;
                    return FromCheck(
                        TriggerEvaluator.EvaluateCheck(civicEvent.Check, slot.BaselineMetric, context));

                default:
                    // A value no member of the enum has — corrupt state on our side, not a player
                    // state, so it costs nothing. Every real response is handled above.
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
        /// <b>A district-scoped check records one reading per district</b>, because a reading is
        /// identified by metric <i>and</i> district together. Recording only the metric would let one
        /// district's number answer for another's on replay, which is worse than no record at all —
        /// it is a confident wrong answer — and recording nothing at all sent the replay back to a
        /// city that had since moved.
        /// </para>
        /// <para>
        /// A null <see cref="CheckSpec"/> is not an error to throw on. A catalog entry that authored
        /// no check cannot be scored, and this file's posture on a catalog gap is to degrade: the
        /// slot is already <see cref="SlotOutcome.Unmeasurable"/> by then, and killing the whole
        /// month's resolution over one malformed entry would cost the player every other story in it.
        /// </para>
        /// </remarks>
        private static void RecordEvidence(List<MetricReading> evidence, CheckSpec? check,
                                           StoryReadContext context)
        {
            if (check == null) return;

            TriggerSpec spec = check.Spec;
            if (spec == null) return;

            string metricId = spec.MetricId ?? "";
            if (metricId.Length == 0) return;   // a Manual-kind check reads no metric at all

            if (spec.Scope == TriggerScope.City)
            {
                double? recorded;
                Add(evidence, metricId, CityWide,
                    TryRecorded(context, metricId, CityWide, out recorded)
                        ? recorded
                        : MetricRegistry.ReadCity(context.Today, metricId));
                return;
            }

            if (CopyRecordedDistricts(evidence, context, metricId)) return;

            List<DistrictSnapshot> districts = SortedDistricts(context.Today);
            for (int i = 0; i < districts.Count; i++)
            {
                DistrictSnapshot district = districts[i];
                Add(evidence, metricId, district.Id, MetricRegistry.ReadDistrict(district, metricId));
            }
        }

        /// <summary>
        /// Replays every recorded district reading of <paramref name="metricId"/>, and says whether
        /// there was one.
        /// </summary>
        /// <remarks>
        /// All of them, not the first: an <c>AllDistricts</c> verdict was reached over the whole set,
        /// so replaying part of it would replay a different question. False means the context holds
        /// no district reading for this metric and the districts must be measured.
        /// </remarks>
        private static bool CopyRecordedDistricts(List<MetricReading> evidence,
                                                  StoryReadContext context, string metricId)
        {
            IReadOnlyList<MetricReading> recorded = context.RecordedEvidence;
            if (recorded == null) return false;

            bool any = false;
            for (int i = 0; i < recorded.Count; i++)
            {
                MetricReading reading = recorded[i];
                if (reading == null) continue;
                if (!string.Equals(reading.MetricId, metricId, StringComparison.Ordinal)) continue;

                string districtId = reading.DistrictId ?? CityWide;
                if (districtId.Length == 0) continue;   // a city-wide reading answers a city-wide check

                Add(evidence, metricId, districtId, reading.Value);
                any = true;
            }

            return any;
        }

        /// <summary>
        /// The recorded reading for one metric in one district, if the context carries one.
        /// </summary>
        /// <remarks>
        /// <b>Both halves of the key are matched.</b> Matching on <paramref name="metricId"/> alone
        /// would let one district's recorded reading answer for another's. True with a null
        /// <paramref name="value"/> is a meaningful answer and not a miss: it is a recorded "this
        /// could not be read", and it has to replay as unmeasurable rather than send us back to the
        /// city for a second opinion.
        /// </remarks>
        private static bool TryRecorded(StoryReadContext context, string metricId, string districtId,
                                        out double? value)
        {
            IReadOnlyList<MetricReading> recorded = context.RecordedEvidence;
            if (recorded != null)
            {
                for (int i = 0; i < recorded.Count; i++)
                {
                    MetricReading reading = recorded[i];
                    if (reading != null && SameReading(reading, metricId, districtId))
                    {
                        value = reading.Value;
                        return true;
                    }
                }
            }

            value = null;
            return false;
        }

        /// <summary>
        /// The districts of a snapshot, sorted by ordinal id.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="CitySnapshot.Districts"/> is documented as already ordered, but sorting a copy
        /// costs nothing and keeps the verdict independent of whether the producer honoured that.
        /// </para>
        /// <para>
        /// <b>A district with an empty id is dropped, and that filter is load-bearing.</b> An empty
        /// <see cref="MetricReading.DistrictId"/> is what marks a reading city-wide, so a district
        /// with an empty id would produce a reading indistinguishable from the city's own — the two
        /// keyspaces would collide on the one key that has to stay unambiguous. The contract does not
        /// rule this out: <see cref="DistrictSnapshot.Id"/> defaults to <c>""</c> and nothing in
        /// <c>Agora.Core</c> requires otherwise, so a hand-built snapshot — a test fixture, or any
        /// future non-sensor caller — can carry one. The sensor path happens to prevent it, but a
        /// guard that rests on a producer's habit is not a guarantee, and dropping here makes the two
        /// keyspaces provably disjoint whichever path built the snapshot.
        /// </para>
        /// </remarks>
        private static List<DistrictSnapshot> SortedDistricts(CitySnapshot snapshot)
        {
            var sorted = new List<DistrictSnapshot>();
            List<DistrictSnapshot> source = snapshot.Districts ?? new List<DistrictSnapshot>();

            for (int i = 0; i < source.Count; i++)
            {
                DistrictSnapshot district = source[i];
                if (district != null && !string.IsNullOrEmpty(district.Id)) sorted.Add(district);
            }

            sorted.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return sorted;
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

        /// <summary>
        /// Adds one reading unless the list already holds that metric in that district. Two slots of
        /// one story may name the same metric, and they read the same city, so the second reading
        /// would be the first one again.
        /// </summary>
        private static void Add(List<MetricReading> evidence, string metricId, string districtId,
                                double? value)
        {
            for (int i = 0; i < evidence.Count; i++)
            {
                if (SameReading(evidence[i], metricId, districtId)) return;
            }

            evidence.Add(new MetricReading
            {
                MetricId = metricId,
                DistrictId = districtId,
                Value = value
            });
        }

        /// <summary>Identity over the composite key — metric and district together, never one alone.</summary>
        private static bool SameReading(MetricReading reading, string metricId, string districtId)
        {
            return string.Equals(reading.MetricId, metricId, StringComparison.Ordinal)
                && string.Equals(reading.DistrictId ?? CityWide, districtId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ordinal by metric id, then by district id. The whole key is compared because only the
        /// whole key is unique — sorting on the metric alone would leave one metric's districts in
        /// whatever order they were added, which is the ordering bug this file exists to avoid.
        /// </summary>
        private static int CompareByMetricId(MetricReading a, MetricReading b)
        {
            int byMetric = string.CompareOrdinal(a.MetricId, b.MetricId);
            if (byMetric != 0) return byMetric;
            return string.CompareOrdinal(a.DistrictId ?? CityWide, b.DistrictId ?? CityWide);
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
