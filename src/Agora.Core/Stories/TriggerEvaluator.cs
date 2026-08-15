using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Stories
{
    /// <summary>
    /// Evaluates a <see cref="TriggerSpec"/> against the city, and a <see cref="CheckSpec"/> against
    /// the city plus the slot's baseline. <b>One implementation, two callers.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lanes 2b and 2c call these two methods and must not write their own comparison arithmetic: a
    /// threshold has to mean the same thing at draft as at resolution, and two implementations is
    /// exactly how that stops being true. <see cref="EvaluateCheck"/> does not re-derive anything —
    /// it folds the baseline into the threshold and hands the result to the same path
    /// <see cref="Evaluate"/> uses.
    /// </para>
    /// <para>
    /// <b>Sorted iteration throughout, no dictionary-order dependence.</b> An <c>AnyDistrict</c> spec
    /// walks districts in sorted id order even though "any" does not care which one matched — because
    /// <see cref="TriggerScope.AnyDistrict"/> at draft feeds a district-targeted effect at
    /// resolution, and "whichever came first" is the determinism bug <c>Agora.Core/CLAUDE.md</c>
    /// calls the most common one. The sort is by id ordinal with the source index as tiebreak, so
    /// even a snapshot that somehow carried two districts under one id orders identically twice.
    /// </para>
    /// <para>
    /// <b>The invariant every branch below is written to.</b> <i>A reading that could not be taken is
    /// never reported as a reading that came out against the player.</i> Whenever this file has to
    /// choose between <see cref="CheckResult.NotMet"/> and <see cref="CheckResult.Unmeasurable"/> for
    /// a situation it cannot see through, it chooses <see cref="CheckResult.Unmeasurable"/>: that
    /// answer costs the player nothing in either direction (2c scores it in neither half of the
    /// 2-of-3, 2d moves the balance by exactly zero), while a wrong <see cref="CheckResult.NotMet"/>
    /// takes political power away because a sensor went blind. The two failure modes are not
    /// symmetric and this file is not neutral between them.
    /// </para>
    /// </remarks>
    public static class TriggerEvaluator
    {
        /// <summary>
        /// Whether the city satisfies <paramref name="spec"/> as of the context's month.
        /// </summary>
        /// <returns>
        /// <see cref="CheckResult.Met"/>, <see cref="CheckResult.NotMet"/>, or
        /// <see cref="CheckResult.Unmeasurable"/> when the reading is unavailable — which is never
        /// the same as not met.
        /// </returns>
        public static CheckResult Evaluate(TriggerSpec spec, StoryReadContext context)
        {
            if (spec == null || context == null || context.Today == null) return CheckResult.Unmeasurable;
            return EvaluateAgainst(spec, spec.Threshold, context);
        }

        /// <summary>
        /// Whether a slot the player took a goal on was met.
        /// </summary>
        /// <param name="baseline">
        /// The slot's <c>BaselineMetric</c> — the reading captured when the story opened. Null when
        /// the metric was unreadable then, which makes a <c>RelativeToBaseline</c> check
        /// <see cref="CheckResult.Unmeasurable"/>: there is no honest verdict without the number the
        /// comparison is against.
        /// </param>
        /// <remarks>
        /// A relative check is expressed by <b>shifting the threshold</b> rather than by subtracting
        /// the baseline from the reading. The two are the same arithmetic for all four comparisons,
        /// and shifting is what keeps this method from owning a second copy of the scope and
        /// unmeasurability rules: everything after the shift is <see cref="Evaluate"/>'s own path.
        /// </remarks>
        public static CheckResult EvaluateCheck(CheckSpec check, double? baseline,
                                                StoryReadContext context)
        {
            if (check == null || check.Spec == null) return CheckResult.Unmeasurable;
            if (context == null || context.Today == null) return CheckResult.Unmeasurable;

            TriggerSpec spec = check.Spec;
            if (!check.RelativeToBaseline) return EvaluateAgainst(spec, spec.Threshold, context);

            // No baseline, no honest verdict. The story opened on a month where the metric could not
            // be read, so the question "did it move by this much" has no left-hand side.
            if (!baseline.HasValue) return CheckResult.Unmeasurable;

            double shifted = baseline.Value + spec.Threshold;
            if (!IsFinite(shifted)) return CheckResult.Unmeasurable;

            return EvaluateAgainst(spec, shifted, context);
        }

        // --------------------------------------------------------------------------- the one path

        private static CheckResult EvaluateAgainst(TriggerSpec spec, double threshold,
                                                   StoryReadContext context)
        {
            // A non-finite threshold is a broken catalog entry, not a city that failed to clear it.
            // Every comparison against NaN is false, so without this guard an authoring mistake would
            // silently report NotMet for every city forever — the exact shape of failure this file
            // exists to refuse.
            if (!IsFinite(threshold)) return CheckResult.Unmeasurable;

            switch (spec.Kind)
            {
                case TriggerKind.Metric:
                    return EvaluateNumeric(spec, threshold, context, false);

                case TriggerKind.Delta:
                    return EvaluateNumeric(spec, threshold, context, true);

                case TriggerKind.Unlock:
                    // A progression feature id, not a registry metric. Present tense only: there is no
                    // historical series behind UnlockedFeatureIds, by decision rather than omission.
                    return EvaluateMembership(context.Today.UnlockedFeatureIds, spec.MetricId,
                                              spec.Scope, true);

                case TriggerKind.Policy:
                    return EvaluateMembership(context.Today.ActivePolicyIds, spec.MetricId,
                                              spec.Scope, false);

                case TriggerKind.Absent:
                    return Negate(EvaluatePresence(spec, threshold, context));

                case TriggerKind.Manual:
                    // "Never fires from the city" — there is no measurement to take, so there is no
                    // reading to report. Unmeasurable rather than NotMet, and the asymmetry is the
                    // reason: both keep a manual event out of a trigger-driven pool at draft (2b
                    // admits only Met), but only NotMet would charge the player at resolution if a
                    // catalog entry ever carried a Manual check. Costing nothing is the safe half of
                    // an authoring error.
                    return CheckResult.Unmeasurable;

                default:
                    // A kind this build does not understand. A catalog written against a newer
                    // grammar must not score as a city that failed.
                    return CheckResult.Unmeasurable;
            }
        }

        /// <summary>
        /// What <see cref="TriggerKind.Absent"/> negates.
        /// </summary>
        /// <remarks>
        /// Absent is the only way to express "this is <i>not</i> in force", and that is what it is
        /// for: the four <see cref="Comparison"/> operators are already closed under negation
        /// (<c>LessThan</c> is <c>GreaterThanOrEqual</c> inverted), so negating a threshold read would
        /// be pure redundancy while negating a set membership has no other spelling. So an
        /// <c>Absent</c> whose <c>MetricId</c> names a registry metric is read as a negated threshold
        /// anyway — harmless, and it keeps the kind total — and one that does not is read as
        /// membership of the city's two id sets.
        /// </remarks>
        private static CheckResult EvaluatePresence(TriggerSpec spec, double threshold,
                                                    StoryReadContext context)
        {
            if (MetricRegistry.IsKnown(spec.MetricId, spec.Scope))
            {
                return EvaluateNumeric(spec, threshold, context, false);
            }

            if (spec.Scope != TriggerScope.City) return CheckResult.Unmeasurable;
            if (string.IsNullOrEmpty(spec.MetricId)) return CheckResult.Unmeasurable;

            List<string>? features = context.Today.UnlockedFeatureIds;
            List<string>? policies = context.Today.ActivePolicyIds;

            bool featuresBlank = features == null || features.Count == 0;
            bool policiesBlank = policies == null || policies.Count == 0;

            // Both lists empty is the shape a capture taken before anything was read has, so there is
            // no set to test membership against rather than an id that is genuinely absent from one.
            if (featuresBlank && policiesBlank) return CheckResult.Unmeasurable;

            bool present = ContainsOrdinal(features, spec.MetricId) ||
                           ContainsOrdinal(policies, spec.MetricId);

            return present ? CheckResult.Met : CheckResult.NotMet;
        }

        // ------------------------------------------------------------------- numeric reads

        private static CheckResult EvaluateNumeric(TriggerSpec spec, double threshold,
                                                   StoryReadContext context, bool delta)
        {
            CitySnapshot today = context.Today;
            CitySnapshot? past = null;

            if (delta)
            {
                // A window of zero or less names no earlier month at all. Nothing to subtract, so
                // nothing was measured — not a change of zero, which is a claim about a city that
                // held steady.
                if (spec.WindowMonths <= 0) return CheckResult.Unmeasurable;

                past = BaselineSnapshot(context, spec.WindowMonths);

                // The window reaches further back than the history actually held. A young save, a
                // truncated catch-up, or a sidecar that lost its history all land here, and none of
                // them is a city that failed to change: there is no earlier reading to subtract.
                if (past == null) return CheckResult.Unmeasurable;
            }

            switch (spec.Scope)
            {
                case TriggerScope.City:
                {
                    double? value = CityValue(spec.MetricId, today, past, delta, context);
                    return value.HasValue
                        ? Compare(value.Value, spec.Comparison, threshold)
                        : CheckResult.Unmeasurable;
                }

                case TriggerScope.AnyDistrict:
                case TriggerScope.AllDistricts:
                    return EvaluateDistricts(spec, threshold, today, past, delta, context);

                default:
                    return CheckResult.Unmeasurable;
            }
        }

        /// <summary>
        /// The city-scope comparand: a level, or a change across the window.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Recorded evidence wins over a live measurement</b> — that is what makes an early resolve
        /// deterministic on replay, per <c>StoryReadContext.RecordedEvidence</c>. A recorded entry
        /// whose <c>Value</c> is null is a recorded <i>absence</i>, and stays unmeasurable rather than
        /// falling through to a fresh sample that would disagree with the month it was scored in.
        /// </para>
        /// <para>
        /// A <c>Delta</c> is the <b>absolute</b> change, today minus then. Not a fractional one:
        /// several of these metrics are counts that are legitimately zero in a month (births in a
        /// hamlet, tourists in a city with no hotels), and a fraction over a zero baseline is either
        /// an infinity or a special case per metric. <c>MetricHistory.TrendOver</c> reports fractional
        /// change because a trend line wants one; a threshold wants a quantity. A fractional form, if
        /// wave 3 ever needs one, is a new <see cref="TriggerKind"/> in a reviewed commit — not a
        /// reinterpretation of this one, which would move every authored threshold silently.
        /// </para>
        /// </remarks>
        private static double? CityValue(string metricId, CitySnapshot today, CitySnapshot? past,
                                         bool delta, StoryReadContext context)
        {
            double? recorded;
            if (TryRecordedEvidence(context, metricId, CityDistrictId, out recorded)) return recorded;

            double? now = MetricRegistry.ReadCity(today, metricId);
            if (!now.HasValue) return null;
            if (!delta) return now;

            // Restated rather than asserted: the caller only passes null here when it has already
            // answered unmeasurable, but a guard costs nothing and a missing earlier month must never
            // become a change of zero.
            if (past == null) return null;

            double? then = MetricRegistry.ReadCity(past, metricId);
            if (!then.HasValue) return null;

            double change = now.Value - then.Value;
            return IsFinite(change) ? (double?)change : null;
        }

        /// <summary>
        /// One district's comparand, or null when this district cannot be read.
        /// </summary>
        /// <remarks>
        /// <b>The historical half of a district <c>Delta</c> is the weak leg, and it is weak by
        /// construction rather than by oversight.</b> A rehydrated district reports no fallbacks
        /// whatever the original month looked like — <c>SnapshotRehydration</c> rebuilds it from
        /// recorded samples alone — so <c>ReadDistrict</c> cannot tell a measured figure from a
        /// contract default there, and the honest probe is against <c>MetricHistory</c>, which lives
        /// in an assembly <c>Agora.Core</c> may never reference. What this file <i>can</i> establish
        /// is that the district existed in that month at all, and it does: a district absent from the
        /// earlier snapshot is unmeasurable rather than unchanged. Anything finer needs the history
        /// store passed in, which would be a change to <c>StoryReadContext</c> — a spine file.
        /// <para>
        /// <b>Recorded evidence is consulted here too, keyed on this district's id</b>, on the same
        /// rule and for the same reason as the city path. It short-circuits both legs of a delta: a
        /// recorded reading is the comparand the verdict was reached on, so replay must not re-derive
        /// it from two snapshots that have since moved.
        /// </para>
        /// </remarks>
        private static double? DistrictValue(string metricId, DistrictSnapshot today,
                                             CitySnapshot? past, bool delta, StoryReadContext context)
        {
            double? recorded;
            if (TryRecordedEvidence(context, metricId, today.Id, out recorded)) return recorded;

            double? now = MetricRegistry.ReadDistrict(today, metricId);
            if (!now.HasValue) return null;
            if (!delta) return now;

            DistrictSnapshot? before = FindDistrict(past, today.Id);
            if (before == null) return null;

            double? then = MetricRegistry.ReadDistrict(before, metricId);
            if (!then.HasValue) return null;

            double change = now.Value - then.Value;
            return IsFinite(change) ? (double?)change : null;
        }

        /// <summary>
        /// The quantified scopes, and the whole of the three-state reasoning for them.
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item><b>No districts at all</b> — unmeasurable at both scopes. "Every district" over an
        /// empty set is vacuously true in logic, and reporting <see cref="CheckResult.Met"/> for it
        /// would let a story about district conditions succeed on a city where none are drawn, which
        /// rewards the player for having nothing to measure. "Some district" over an empty set is
        /// vacuously false, and reporting <see cref="CheckResult.NotMet"/> would charge them for the
        /// same absence. Neither is a reading; both answer unmeasurable.</item>
        /// <item><b>Some read, some cannot, and one that read is Met</b> — <c>AnyDistrict</c> is
        /// <see cref="CheckResult.Met"/>. A positive existential is settled by one witness, and the
        /// districts that could not be read cannot unsettle it.</item>
        /// <item><b>Some read, none of them Met, and at least one could not be read</b> —
        /// <c>AnyDistrict</c> is unmeasurable. Denying an existential means having looked everywhere,
        /// and we did not.</item>
        /// <item><b>None of them can be read</b> — unmeasurable at both scopes. There is no witness
        /// and no counterexample; there is no measurement.</item>
        /// <item><b>One district fails the comparison</b> — <c>AllDistricts</c> is
        /// <see cref="CheckResult.NotMet"/> even if others could not be read. A universal is refuted
        /// by one counterexample, and that counterexample was genuinely measured.</item>
        /// <item><b>Every district that read is Met, but one could not be read</b> —
        /// <c>AllDistricts</c> is unmeasurable. Asserting a universal means having looked everywhere.
        /// This is the case the brief singles out, and it is the one where NotMet would be most
        /// tempting and most wrong.</item>
        /// </list>
        /// </remarks>
        private static CheckResult EvaluateDistricts(TriggerSpec spec, double threshold,
                                                     CitySnapshot today, CitySnapshot? past, bool delta,
                                                     StoryReadContext context)
        {
            List<DistrictSnapshot> ordered = SortedDistricts(today);
            if (ordered.Count == 0) return CheckResult.Unmeasurable;

            bool anyMet = false;
            bool anyNotMet = false;
            bool anyUnreadable = false;

            for (int i = 0; i < ordered.Count; i++)
            {
                double? value = DistrictValue(spec.MetricId, ordered[i], past, delta, context);
                if (!value.HasValue)
                {
                    anyUnreadable = true;
                    continue;
                }

                if (Compare(value.Value, spec.Comparison, threshold) == CheckResult.Met) anyMet = true;
                else anyNotMet = true;
            }

            if (spec.Scope == TriggerScope.AllDistricts)
            {
                if (anyNotMet) return CheckResult.NotMet;
                if (anyUnreadable) return CheckResult.Unmeasurable;
                return CheckResult.Met;
            }

            if (anyMet) return CheckResult.Met;
            if (anyUnreadable) return CheckResult.Unmeasurable;
            return CheckResult.NotMet;
        }

        // ------------------------------------------------------------------- ordering and lookup

        /// <summary>
        /// The snapshot's districts by id ordinal, source index breaking ties.
        /// </summary>
        /// <remarks>
        /// The contract already says the list is sorted, and this sorts it again anyway: the loop
        /// above feeds a district-targeted effect at resolution, so a snapshot assembled by a future
        /// sensor that forgot the ordering would produce a run-dependent target rather than a loud
        /// failure. The index tiebreak makes the sort total, so it does not matter that
        /// <c>List.Sort</c> is unstable. Districts with no id are dropped — an id is what matches a
        /// district to its earlier self and to an effect, so one without an id cannot be either.
        /// </remarks>
        private static List<DistrictSnapshot> SortedDistricts(CitySnapshot snapshot)
        {
            var ordered = new List<DistrictSnapshot>();
            var index = new Dictionary<DistrictSnapshot, int>();

            List<DistrictSnapshot>? source = snapshot.Districts;
            if (source == null) return ordered;

            for (int i = 0; i < source.Count; i++)
            {
                DistrictSnapshot? d = source[i];
                if (d == null || string.IsNullOrEmpty(d.Id)) continue;
                if (index.ContainsKey(d)) continue;

                index[d] = i;
                ordered.Add(d);
            }

            // The dictionary is a source-index lookup only; nothing iterates it, so no output order
            // depends on it.
            ordered.Sort((a, b) =>
            {
                int byId = string.CompareOrdinal(a.Id, b.Id);
                return byId != 0 ? byId : index[a].CompareTo(index[b]);
            });

            return ordered;
        }

        private static DistrictSnapshot? FindDistrict(CitySnapshot? snapshot, string districtId)
        {
            if (snapshot == null) return null;

            List<DistrictSnapshot>? districts = snapshot.Districts;
            if (districts == null) return null;

            for (int i = 0; i < districts.Count; i++)
            {
                DistrictSnapshot? d = districts[i];
                if (d == null) continue;
                if (string.Equals(d.Id, districtId, StringComparison.Ordinal)) return d;
            }

            return null;
        }

        /// <summary>
        /// The newest snapshot in the history no later than <paramref name="windowMonths"/> before
        /// the context's month, or null when the history does not reach that far back.
        /// </summary>
        /// <remarks>
        /// "No later than" rather than "exactly that month", matching
        /// <c>MetricHistory.TrendOver</c>'s own rule, so a month with no capture widens the window
        /// rather than blinding the trigger. The whole list is scanned rather than trusting the
        /// documented oldest-first order, and ties on the same month resolve to the later entry — so
        /// the choice is a function of the list's contents and not of where the scan happened to
        /// start.
        /// </remarks>
        private static CitySnapshot? BaselineSnapshot(StoryReadContext context, int windowMonths)
        {
            IReadOnlyList<CitySnapshot>? history = context.History;
            if (history == null || history.Count == 0) return null;

            int cutoff = context.Today.Date.TotalMonths - windowMonths;

            CitySnapshot? best = null;
            int bestMonths = 0;

            for (int i = 0; i < history.Count; i++)
            {
                CitySnapshot? candidate = history[i];
                if (candidate == null) continue;

                int months = candidate.Date.TotalMonths;
                if (months > cutoff) continue;
                if (best != null && months < bestMonths) continue;

                best = candidate;
                bestMonths = months;
            }

            return best;
        }

        /// <summary>
        /// The <see cref="MetricReading.DistrictId"/> of a city-wide reading. Empty, per the contract
        /// — spelled once here so the two call sites cannot disagree about what "no district" is.
        /// </summary>
        private const string CityDistrictId = "";

        /// <summary>
        /// The recorded reading for <paramref name="metricId"/> in <paramref name="districtId"/>,
        /// when the context carries one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>A reading is identified by metric id and district id together, and both halves are
        /// matched.</b> Matching on the metric alone would let one district's recorded reading answer
        /// for another's, which is worse than having no record at all: it is a confident wrong answer,
        /// and because the record is what replay reads instead of re-measuring, it would be believed
        /// permanently rather than corrected by the next capture. A city-wide reading carries an empty
        /// district id, so the city path asks for exactly that and cannot be answered by a district's
        /// entry either — which holds because the district walk already drops any district with no
        /// id, so no district read ever asks under the city's key.
        /// </para>
        /// <para>
        /// The evidence list is walked in its own order and matched by exact id on both fields, so
        /// nothing depends on it being sorted even though the contract says it is. A recorded entry
        /// whose <c>Value</c> is null is a recorded <i>absence</i> and is honoured as one; so is a
        /// recorded non-finite value, which no honest capture produces and which must not be allowed
        /// to reach the comparison.
        /// </para>
        /// </remarks>
        private static bool TryRecordedEvidence(StoryReadContext context, string metricId,
                                                string districtId, out double? value)
        {
            value = null;

            IReadOnlyList<MetricReading>? evidence = context.RecordedEvidence;
            if (evidence == null || evidence.Count == 0) return false;
            if (string.IsNullOrEmpty(metricId)) return false;

            for (int i = 0; i < evidence.Count; i++)
            {
                MetricReading? reading = evidence[i];
                if (reading == null) continue;
                if (!string.Equals(reading.MetricId, metricId, StringComparison.Ordinal)) continue;

                // Null and empty are the same claim here — "no district" — because a reading
                // deserialised from a sidecar written before this field existed carries null where a
                // freshly built one carries "".
                string recordedDistrict = reading.DistrictId ?? CityDistrictId;
                if (!string.Equals(recordedDistrict, districtId, StringComparison.Ordinal)) continue;

                if (reading.Value.HasValue && !IsFinite(reading.Value.Value)) return true;

                value = reading.Value;
                return true;
            }

            return false;
        }

        // -------------------------------------------------------------------------- small helpers

        /// <summary>
        /// Membership of one of the city's two authored id sets.
        /// </summary>
        /// <param name="blankIsUnmeasurable">
        /// Whether an empty list means "nothing was read" rather than "nothing is in force". True for
        /// unlocked features and false for policies, and the asymmetry is about the two quantities
        /// rather than about caution: progression is monotonic and no real save past its first month
        /// has zero features unlocked, so an empty list there is far likelier to be a blind sensor
        /// than a fact — while a city that runs no policies at all is ordinary and may stay that way
        /// forever. Neither list carries a fallback marker, so this is the only signal available.
        /// The cost of the true case is that an early-game <c>Absent</c>-style unlock trigger stays
        /// out of the pool for a month or two, and a degradation is a valid outcome; the cost of the
        /// false case would be scoring a slot against a sensor gap.
        /// </param>
        private static CheckResult EvaluateMembership(List<string>? ids, string wanted,
                                                      TriggerScope scope, bool blankIsUnmeasurable)
        {
            // No district-scope reading exists for either list: features and policies are city-wide at
            // source. Asking a district is a question with nothing behind it, not a question answered
            // no.
            if (scope != TriggerScope.City) return CheckResult.Unmeasurable;
            if (string.IsNullOrEmpty(wanted)) return CheckResult.Unmeasurable;

            if (ids == null || ids.Count == 0)
            {
                return blankIsUnmeasurable ? CheckResult.Unmeasurable : CheckResult.NotMet;
            }

            return ContainsOrdinal(ids, wanted) ? CheckResult.Met : CheckResult.NotMet;
        }

        private static bool ContainsOrdinal(List<string>? ids, string wanted)
        {
            if (ids == null) return false;

            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], wanted, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>
        /// The comparison itself — the one place a threshold is applied, at draft and at resolution
        /// alike.
        /// </summary>
        private static CheckResult Compare(double value, Comparison comparison, double threshold)
        {
            // A non-finite reading is not a reading. It cannot reach here from a registry accessor,
            // which reports contract-shaped numbers, but a Delta subtracts two of them and a relative
            // check shifts by a stored baseline, so the guard sits where the arithmetic ends.
            if (!IsFinite(value)) return CheckResult.Unmeasurable;

            switch (comparison)
            {
                case Comparison.LessThan:
                    return value < threshold ? CheckResult.Met : CheckResult.NotMet;
                case Comparison.LessThanOrEqual:
                    return value <= threshold ? CheckResult.Met : CheckResult.NotMet;
                case Comparison.GreaterThan:
                    return value > threshold ? CheckResult.Met : CheckResult.NotMet;
                case Comparison.GreaterThanOrEqual:
                    return value >= threshold ? CheckResult.Met : CheckResult.NotMet;
                default:
                    // An operator this build does not understand, same reasoning as an unknown kind.
                    return CheckResult.Unmeasurable;
            }
        }

        /// <summary>
        /// Negation that leaves <see cref="CheckResult.Unmeasurable"/> alone. The opposite of "we
        /// could not see" is still "we could not see" — folding it to <see cref="CheckResult.Met"/>
        /// would let an <c>Absent</c> trigger fire on every blind sensor in the city.
        /// </summary>
        private static CheckResult Negate(CheckResult result)
        {
            switch (result)
            {
                case CheckResult.Met: return CheckResult.NotMet;
                case CheckResult.NotMet: return CheckResult.Met;
                default: return CheckResult.Unmeasurable;
            }
        }

        /// <summary>
        /// <c>double.IsFinite</c> does not exist on netstandard2.0, so it is spelled out here rather
        /// than reached for and silently missed.
        /// </summary>
        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
