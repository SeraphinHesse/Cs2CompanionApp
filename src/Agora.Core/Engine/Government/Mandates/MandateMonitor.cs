using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Tuning;
using Mandate = Agora.Core.Contracts.Mandate;

namespace Agora.Core.Engine.Government.Mandates
{
    /// <summary>The outcome of one monitoring tick. Everything the caller needs, nothing it does not.</summary>
    public sealed class MandateTickResult
    {
        /// <summary>Every mandate handed in, updated where it moved, sorted by id. New instances.</summary>
        public IReadOnlyList<Mandate> Mandates { get; }

        /// <summary>Mandates that finished on this tick, sorted by mandate id. Usually empty.</summary>
        public IReadOnlyList<MandateResolution> Resolutions { get; }

        /// <summary>Capped effect requests from those resolutions, in resolution order.</summary>
        public IReadOnlyList<EffectRequest> Effects { get; }

        internal MandateTickResult(IReadOnlyList<Mandate> mandates,
                                   IReadOnlyList<MandateResolution> resolutions,
                                   IReadOnlyList<EffectRequest> effects)
        {
            Mandates = mandates;
            Resolutions = resolutions;
            Effects = effects;
        }
    }

    /// <summary>
    /// Monthly monitoring of live promises against the real city (§3 Mandates).
    ///
    /// <para>
    /// Reads the snapshot and nothing else, and writes to the city not at all — the only outputs are
    /// updated mandate copies and capped effect requests the caller may choose to apply. The inputs
    /// are never mutated, so a caller can diff before and after.
    /// </para>
    /// </summary>
    public static class MandateMonitor
    {
        /// <summary>Pending and Active are live; the other four are terminal.</summary>
        public static bool IsLive(Mandate? mandate) =>
            mandate != null &&
            (mandate.Status == MandateStatus.Pending || mandate.Status == MandateStatus.Active);

        /// <summary>
        /// Fraction of the way from baseline to target, clamped to <c>[0, 1]</c>. Direction-agnostic:
        /// the span carries the sign, so a promise to cut pollution and one to raise coverage score
        /// identically.
        /// </summary>
        public static double ComputeProgress(double baseline, double target, double current) =>
            MandateMath.Progress(baseline, target, current);

        /// <summary>
        /// Advances every live mandate one tick.
        /// </summary>
        /// <remarks>
        /// The state machine, in order:
        /// <list type="bullet">
        /// <item>Unmeasurable this tick — district gone, metric absent per-district, or the sensor fell
        /// back to the city value for that field — sets <see cref="Mandate.IsMeasurementStalled"/> and
        /// holds. A held mandate accrues no progress and cannot fail its deadline; only once the
        /// deadline plus <c>mandates.stalledMetricGraceMonths</c> has also passed is it abandoned.</item>
        /// <item>Pending becomes Active after <c>mandates.graceMonths</c>, or immediately at the deadline.</item>
        /// <item>Active with progress at 1 is Fulfilled, whenever that happens.</item>
        /// <item>At the deadline, progress at or above <c>mandates.partialCreditThreshold</c> is
        /// PartiallyFulfilled and anything below it is Defied.</item>
        /// </list>
        /// Measurements are refreshed every <c>mandates.monitoringIntervalMonths</c> counted from the
        /// issue date, and always on a tick at or past the deadline so a coarse interval cannot skip
        /// the one measurement that decides the outcome.
        /// </remarks>
        public static MandateTickResult Tick(Guid saveGuid, SimDate date, CitySnapshot? snapshot,
                                             IReadOnlyList<Mandate>? mandates, EngineTuning tuning)
        {
            var updated = new List<Mandate>();
            var resolutions = new List<MandateResolution>();
            var effects = new List<EffectRequest>();

            if (mandates == null || mandates.Count == 0 || tuning == null)
                return new MandateTickResult(updated, resolutions, effects);

            MandatesTuning t = tuning.Mandates;
            int interval = t.MonitoringIntervalMonths < 1 ? 1 : t.MonitoringIntervalMonths;
            int stalledGrace = t.StalledMetricGraceMonths < 0 ? 0 : t.StalledMetricGraceMonths;
            double partial = MandateMath.Clamp01(t.PartialCreditThreshold);

            // Ordered first so resolutions come out in id order regardless of how the caller stored them.
            var ordered = new List<Mandate>();
            for (int i = 0; i < mandates.Count; i++)
            {
                if (mandates[i] != null) ordered.Add(mandates[i]);
            }
            ordered.Sort(CompareById);

            for (int i = 0; i < ordered.Count; i++)
            {
                Mandate source = ordered[i];
                Mandate m = Clone(source);

                if (!IsLive(m))
                {
                    updated.Add(m);
                    continue;
                }

                string? districtId = string.IsNullOrEmpty(m.DistrictId) ? null : m.DistrictId;
                bool measurable = MandateMetrics.TryRead(snapshot, districtId, m.Metric, out double value);

                int monthsSinceIssue = m.IssuedDate.MonthsUntil(date);
                bool deadlineReached = date >= m.DeadlineDate;
                bool onInterval = monthsSinceIssue < 0 || interval <= 1 || (monthsSinceIssue % interval) == 0;

                if (measurable)
                {
                    if (onInterval || deadlineReached)
                    {
                        m.CurrentValue = value;
                        m.Progress = MandateMath.Progress(m.BaselineValue, m.TargetValue, value);
                    }

                    m.IsMeasurementStalled = false;
                }
                else
                {
                    m.IsMeasurementStalled = true;
                }

                // Grace: a promise is not scored while the government is still turning the wheel.
                if (m.Status == MandateStatus.Pending && (monthsSinceIssue >= t.GraceMonths || deadlineReached))
                {
                    m.Status = MandateStatus.Active;
                }

                if (m.IsMeasurementStalled)
                {
                    // Held, not failed. Abandoned only when the metric has stayed unreadable well past
                    // the deadline — at which point there is no evidence either way, so nobody is blamed.
                    if (date >= m.DeadlineDate.AddMonths(stalledGrace))
                    {
                        Finish(saveGuid, date, m, MandateStatus.Abandoned, tuning, resolutions, effects);
                    }

                    updated.Add(m);
                    continue;
                }

                if (m.Status == MandateStatus.Active)
                {
                    if (m.Progress >= 1.0 - MandateMath.Epsilon)
                    {
                        Finish(saveGuid, date, m, MandateStatus.Fulfilled, tuning, resolutions, effects);
                    }
                    else if (deadlineReached)
                    {
                        MandateStatus outcome = m.Progress >= partial
                            ? MandateStatus.PartiallyFulfilled
                            : MandateStatus.Defied;

                        Finish(saveGuid, date, m, outcome, tuning, resolutions, effects);
                    }
                }

                updated.Add(m);
            }

            return new MandateTickResult(updated, resolutions, effects);
        }

        /// <summary>
        /// Cancels a government's live promises — used when a coalition collapses or a term ends.
        /// Abandoned mandates are never scored, so nothing is applied and nothing is charged.
        /// </summary>
        /// <param name="coalitionId">Only mandates owned by this government are cancelled. Null cancels all.</param>
        /// <returns>New instances, sorted by id. The input list is not mutated.</returns>
        public static IReadOnlyList<Mandate> AbandonAll(IReadOnlyList<Mandate>? mandates,
                                                        string? coalitionId, SimDate date)
        {
            var result = new List<Mandate>();
            if (mandates == null) return result;

            for (int i = 0; i < mandates.Count; i++)
            {
                Mandate source = mandates[i];
                if (source == null) continue;

                Mandate m = Clone(source);

                bool owned = coalitionId == null ||
                             string.Equals(m.CoalitionId, coalitionId, StringComparison.Ordinal);

                if (owned && IsLive(m))
                {
                    m.Status = MandateStatus.Abandoned;
                    m.ResolvedDate = date;
                }

                result.Add(m);
            }

            result.Sort(CompareById);
            return result;
        }

        private static void Finish(Guid saveGuid, SimDate date, Mandate m, MandateStatus outcome,
                                   EngineTuning tuning, List<MandateResolution> resolutions,
                                   List<EffectRequest> effects)
        {
            MandateResolution resolution = MandateResolver.Resolve(saveGuid, date, m, outcome, tuning);

            m.Status = outcome;
            m.ResolvedDate = date;
            m.ResolutionEffectId = resolution.ResolutionEffectId;

            resolutions.Add(resolution);
            if (resolution.Effect.HasValue) effects.Add(resolution.Effect.Value);
        }

        /// <summary>
        /// A field-for-field copy. Monitoring returns new instances rather than editing the caller's,
        /// which is what lets the determinism test compare two independent runs of the same input.
        /// </summary>
        public static Mandate Clone(Mandate source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return new Mandate
            {
                SchemaVersion = source.SchemaVersion,
                Id = source.Id,
                PartyId = source.PartyId,
                CoalitionId = source.CoalitionId,
                DistrictId = source.DistrictId,
                Issue = source.Issue,
                Metric = source.Metric,
                Direction = source.Direction,
                BaselineValue = source.BaselineValue,
                TargetValue = source.TargetValue,
                CurrentValue = source.CurrentValue,
                Progress = source.Progress,
                IssuedDate = source.IssuedDate,
                DeadlineDate = source.DeadlineDate,
                ResolvedDate = source.ResolvedDate,
                Status = source.Status,
                Salience = source.Salience,
                ResolutionEffectId = source.ResolutionEffectId,
                Text = source.Text,
                IsMeasurementStalled = source.IsMeasurementStalled
            };
        }

        private static int CompareById(Mandate a, Mandate b) => string.CompareOrdinal(a.Id, b.Id);
    }
}
