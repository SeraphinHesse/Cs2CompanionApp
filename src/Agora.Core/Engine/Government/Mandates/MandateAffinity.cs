using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Tuning;
using Mandate = Agora.Core.Contracts.Mandate;

namespace Agora.Core.Engine.Government.Mandates
{
    /// <summary>
    /// A party's track record on its promises, as the voter model sees it.
    /// </summary>
    public readonly struct MandatePerformance
    {
        /// <summary>Salience-weighted record on <c>[-1, +1]</c>. 0 when nothing has been judged yet.</summary>
        public double Score { get; }

        public int Fulfilled { get; }
        public int PartiallyFulfilled { get; }
        public int Defied { get; }
        public int Abandoned { get; }

        /// <summary>Still Pending or Active — counted, never scored.</summary>
        public int Live { get; }

        /// <summary>Sum of the salience of the scored mandates. 0 means <see cref="Score"/> is empty.</summary>
        public double ScoredSalience { get; }

        internal MandatePerformance(double score, int fulfilled, int partiallyFulfilled, int defied,
                                    int abandoned, int live, double scoredSalience)
        {
            Score = score;
            Fulfilled = fulfilled;
            PartiallyFulfilled = partiallyFulfilled;
            Defied = defied;
            Abandoned = abandoned;
            Live = live;
            ScoredSalience = scoredSalience;
        }

        /// <summary>No promises judged: a neutral record, not a bad one.</summary>
        public static MandatePerformance Empty => new MandatePerformance(0.0, 0, 0, 0, 0, 0, 0.0);
    }

    /// <summary>
    /// The mandate term of the affinity kernel (<c>politicsmodplan.md</c> §4.3: "affinity = weighted
    /// dot product + incumbency term + mandate performance term + …").
    ///
    /// <para>
    /// This returns the raw record on <c>[-1, +1]</c> and stops. The affinity packet owns the
    /// coefficients that price it — <c>affinity.mandatePerformanceWeight</c> and
    /// <c>affinity.mandateFailurePenalty</c> — so the two packets cannot double-count the same weight.
    /// </para>
    /// </summary>
    public static class MandateAffinity
    {
        /// <summary>
        /// A party's record over the mandates handed in. The caller chooses the window by choosing the
        /// list: current government only, this term, or all of history.
        /// </summary>
        /// <param name="districtId">
        /// null scores every mandate. A district id scores that district's promises plus every
        /// city-wide one, which is what a bloc in that district actually experiences.
        /// </param>
        /// <remarks>
        /// Each judged mandate contributes <c>2 × progress − 1</c>: a target met reads +1, a promise
        /// abandoned at the baseline reads −1, and halfway reads 0. Contributions are weighted by
        /// salience, so a promise nobody cared about barely moves the record. Live mandates contribute
        /// nothing — a government is not punished for work still in hand — and abandoned ones are
        /// excluded entirely, because an unmeasurable promise is never scored (§6).
        /// </remarks>
        public static MandatePerformance ScoreForParty(string? partyId, string? districtId,
                                                       IReadOnlyList<Mandate>? mandates, EngineTuning tuning)
        {
            if (string.IsNullOrEmpty(partyId) || mandates == null || mandates.Count == 0 || tuning == null)
                return MandatePerformance.Empty;

            double floor = MandateMath.Clamp01(tuning.Mandates.SalienceFloor);

            double weighted = 0.0;
            double totalSalience = 0.0;
            int fulfilled = 0, partial = 0, defied = 0, abandoned = 0, live = 0;

            // List order, which the contract fixes by id: the sum is bit-stable.
            for (int i = 0; i < mandates.Count; i++)
            {
                Mandate m = mandates[i];
                if (m == null) continue;
                if (!string.Equals(m.PartyId, partyId, StringComparison.Ordinal)) continue;

                if (districtId != null &&
                    !string.IsNullOrEmpty(m.DistrictId) &&
                    !string.Equals(m.DistrictId, districtId, StringComparison.Ordinal))
                {
                    continue;
                }

                switch (m.Status)
                {
                    case MandateStatus.Pending:
                    case MandateStatus.Active:
                        live++;
                        continue;

                    case MandateStatus.Abandoned:
                        abandoned++;
                        continue;

                    case MandateStatus.Fulfilled:
                        fulfilled++;
                        break;

                    case MandateStatus.PartiallyFulfilled:
                        partial++;
                        break;

                    case MandateStatus.Defied:
                        defied++;
                        break;
                }

                double salience = MandateMath.Clamp(
                    MandateMath.IsFinite(m.Salience) ? m.Salience : 0.0, floor, 1.0);

                double contribution = 2.0 * MandateMath.SafeProgress(m.Progress) - 1.0;

                weighted += salience * contribution;
                totalSalience += salience;
            }

            if (totalSalience <= MandateMath.Epsilon)
            {
                return new MandatePerformance(0.0, fulfilled, partial, defied, abandoned, live, 0.0);
            }

            double score = MandateMath.Clamp(weighted / totalSalience, -1.0, 1.0);
            return new MandatePerformance(score, fulfilled, partial, defied, abandoned, live, totalSalience);
        }

        /// <summary>City-wide record for a party.</summary>
        public static MandatePerformance ScoreForParty(string? partyId, IReadOnlyList<Mandate>? mandates,
                                                       EngineTuning tuning) =>
            ScoreForParty(partyId, null, mandates, tuning);

        /// <summary>
        /// Every live mandate a government still owes, sorted by id. The dashboard's mandate tracker
        /// and the coalition packet's stability shock both read this.
        /// </summary>
        public static IReadOnlyList<Mandate> LiveFor(string? coalitionId, IReadOnlyList<Mandate>? mandates)
        {
            var result = new List<Mandate>();
            if (mandates == null) return result;

            for (int i = 0; i < mandates.Count; i++)
            {
                Mandate m = mandates[i];
                if (m == null || !MandateMonitor.IsLive(m)) continue;
                if (coalitionId != null && !string.Equals(m.CoalitionId, coalitionId, StringComparison.Ordinal))
                    continue;

                result.Add(m);
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return result;
        }
    }
}
