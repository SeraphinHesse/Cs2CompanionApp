using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Polling
{
    /// <summary>
    /// Packet 6 — published opinion polls, and the structured way they are wrong.
    ///
    /// <para>
    /// A poll here is not noise added to the truth. It is a <em>re-weighting</em> of the truth: the
    /// pollster reaches high-education, high-turnout districts more easily than low-education,
    /// low-turnout ones, so those districts carry more than their share of the published figure
    /// (§3 Campaigns, <c>politicsmodplan.md</c>). Random error sits on top of that, but the bias is
    /// systematic and survives averaging — which is the point. Every pollster in the field shares it,
    /// so herding makes the polls agree with each other and stay wrong together.
    /// </para>
    ///
    /// <para>
    /// The direction is contractual: <c>DistrictPollResult.SamplingBias</c> is negative for a district
    /// whose education index is below the electorate-weighted city mean, and the M4a gate asserts it.
    /// Magnitude is tuning; direction is design.
    /// </para>
    ///
    /// <para>
    /// Pure and stateless. Contracts in, contracts out, <see cref="EngineTuning"/> for every
    /// coefficient, <see cref="SeedStreams"/> for every draw.
    /// </para>
    /// </summary>
    public static class PollingEngine
    {
        /// <summary>
        /// Runs one poll. Deterministic in (<c>SaveGuid</c>, <c>Date</c>, <c>ElectionDate</c>,
        /// <c>PollsterId</c>, district inputs, tuning) — nothing else.
        /// </summary>
        public static PollResult Run(PollRequest request, EngineTuning tuning)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            PollingTuning p = tuning.Polling;

            List<DistrictPollInput> districts = SortedDistricts(request.Districts);
            List<string> partyIds = PartyUniverse(districts);

            var poll = new PollResult
            {
                Id = string.IsNullOrEmpty(request.Id) ? "poll-" + request.Date : request.Id!,
                Date = request.Date,
                // Flavor-owned. The engine never invents a pollster's name (non-negotiable #1); the
                // flavor provider fills it in later, or the UI falls back to the id.
                PollsterName = "",
                PollsterId = request.PollsterId ?? "",
                ElectionDate = request.ElectionDate,
                IsPublished = request.IsPublished
            };

            int weeksToElection = WeeksToElection(request, p);
            poll.WeeksToElection = weeksToElection;

            int sampleSize = DrawSampleSize(request, p);
            poll.SampleSize = sampleSize;
            poll.MarginOfError = Round(MarginOfError(sampleSize, p), p.RoundingDecimals);
            poll.UndecidedShare = Round(UndecidedShare(weeksToElection, p), p.RoundingDecimals);

            if (districts.Count == 0 || partyIds.Count == 0)
            {
                // Fail closed: a city with no districts or no parties yields an empty poll rather
                // than a divide-by-zero. The caller decides whether to publish it.
                poll.ProjectedTurnout = 0.0;
                return poll;
            }

            // ---- 1. True weights: each district's real share of the votes that would be cast. -----
            double[] trueWeights = TrueWeights(districts);

            // ---- 2. The two reference points the bias is measured against. ------------------------
            // Weighting the reference by the electorate makes the bias zero-sum: sum(w_d * bias_d) = 0,
            // so the pollster mis-allocates weight between districts without inventing or destroying
            // any. A plain unweighted mean would let a city of many tiny districts drift.
            double refEducation = 0.0;
            double refTurnout = 0.0;
            for (int i = 0; i < districts.Count; i++)
            {
                refEducation += trueWeights[i] * districts[i].EducationIndex;
                refTurnout += trueWeights[i] * Clamp(districts[i].ProjectedTurnout, 0.0, 1.0);
            }

            // ---- 3. Signed sampling bias, and the distorted weights it produces. ------------------
            double[] bias = new double[districts.Count];
            double[] sampleWeights = new double[districts.Count];
            double sampleWeightTotal = 0.0;

            for (int i = 0; i < districts.Count; i++)
            {
                DistrictPollInput d = districts[i];
                bias[i] = p.EducationUnderSampleBias * (d.EducationIndex - refEducation)
                        + p.TurnoutUnderSampleBias * (Clamp(d.ProjectedTurnout, 0.0, 1.0) - refTurnout);

                // exp() rather than (1 + bias): a weight must never reach zero or go negative, and an
                // exponential guarantees that for any tuning value without a clamp constant that would
                // itself have to be tuned. Also makes the distortion multiplicative, so doubling the
                // bias coefficient doubles it in log space rather than saturating.
                sampleWeights[i] = trueWeights[i] * Math.Exp(bias[i]);
                sampleWeightTotal += sampleWeights[i];
            }

            if (sampleWeightTotal <= 0.0)
            {
                for (int i = 0; i < districts.Count; i++) sampleWeights[i] = 1.0 / districts.Count;
            }
            else
            {
                for (int i = 0; i < districts.Count; i++) sampleWeights[i] /= sampleWeightTotal;
            }

            // ---- 4. Aggregate the truth twice: honestly, and as the pollster sampled it. ----------
            double[] cityTrue = Aggregate(districts, partyIds, trueWeights);
            double[] citySampled = Aggregate(districts, partyIds, sampleWeights);

            // ---- 5. Idiosyncratic error: house effect + sampling noise, damped toward election day.
            // The structural bias above is deliberately NOT damped. Pollsters converge on each other
            // as the vote nears (herding) and their random error shrinks, but they all share the same
            // sampling flaw — so the polls agree more and more on a number that is still wrong.
            double progress = CampaignProgress(weeksToElection, p);
            double idioScale = (1.0 - Clamp(p.ErrorDecayTowardElection, 0.0, 1.0) * progress)
                             * (1.0 - Clamp(p.HerdingFactor, 0.0, 1.0) * progress);
            if (idioScale < 0.0) idioScale = 0.0;

            double sampleScale = SampleErrorScale(sampleSize, p);

            double[] published = new double[partyIds.Count];
            double[] houseEffect = new double[partyIds.Count];
            SimDate campaignAnchor = request.ElectionDate ?? request.Date;

            for (int j = 0; j < partyIds.Count; j++)
            {
                string partyId = partyIds[j];

                houseEffect[j] = SeedStreams
                    .RngFor(request.SaveGuid, campaignAnchor, StreamNames.PollHouseEffect, Entity(poll.PollsterId, partyId))
                    .NextGaussian() * p.HouseEffectSigma;

                double samplingError = SeedStreams
                    .RngFor(request.SaveGuid, request.Date, StreamNames.PollError, Entity(poll.PollsterId, partyId))
                    .NextGaussian() * p.ErrorSigma * sampleScale;

                published[j] = citySampled[j] + (houseEffect[j] + samplingError) * idioScale;
            }

            // Model truth is normalised and rounded but never floored: a party genuinely on 0% must
            // read 0% here, even though the published figure never shows a party below the floor.
            poll.TrueShares = FinalizeShares(partyIds, cityTrue, p, applyMinimum: false);
            poll.Shares = FinalizeShares(partyIds, published, p, applyMinimum: true);

            // ---- 6. Projected turnout inherits the same distortion. -------------------------------
            // Over-weighting high-turnout districts makes the published projection optimistic, which
            // is the second assertable direction in this packet.
            double sampledTurnout = 0.0;
            for (int i = 0; i < districts.Count; i++)
                sampledTurnout += sampleWeights[i] * Clamp(districts[i].ProjectedTurnout, 0.0, 1.0);

            double turnoutNoise = SeedStreams
                .RngFor(request.SaveGuid, request.Date, StreamNames.PollTurnout, poll.PollsterId)
                .NextGaussian() * p.ErrorSigma * sampleScale * idioScale;

            poll.ProjectedTurnout = Round(Clamp(sampledTurnout + turnoutNoise, 0.0, 1.0), p.RoundingDecimals);

            // ---- 7. District crosstabs. -----------------------------------------------------------
            poll.Districts = DistrictBreakdown(request, poll.PollsterId, districts, partyIds, bias,
                                               houseEffect, idioScale, sampleScale, p);

            return poll;
        }

        /// <summary>
        /// Mean absolute deviation between two share lists, 0–1. Used for
        /// <c>ElectionResult.FinalPollDeviation</c> and by the harness to measure poll error.
        /// Parties present in one list only count at their full share.
        /// </summary>
        public static double MeanAbsoluteDeviation(
            IReadOnlyList<PartyVoteShare>? a,
            IReadOnlyList<PartyVoteShare>? b)
        {
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            var left = new Dictionary<string, double>(StringComparer.Ordinal);
            var right = new Dictionary<string, double>(StringComparer.Ordinal);

            if (a != null)
                foreach (PartyVoteShare s in a) { ids.Add(s.PartyId ?? ""); left[s.PartyId ?? ""] = s.Share; }
            if (b != null)
                foreach (PartyVoteShare s in b) { ids.Add(s.PartyId ?? ""); right[s.PartyId ?? ""] = s.Share; }

            if (ids.Count == 0) return 0.0;

            double total = 0.0;
            // SortedSet enumerates in ordinal order, so the summation order is fixed and the result
            // is bit-stable regardless of how the two lists were built.
            foreach (string id in ids)
            {
                double x, y;
                if (!left.TryGetValue(id, out x)) x = 0.0;
                if (!right.TryGetValue(id, out y)) y = 0.0;
                total += Math.Abs(x - y);
            }

            return total / ids.Count;
        }

        // ------------------------------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------------------------------

        private static List<DistrictPollResult> DistrictBreakdown(
            PollRequest request,
            string pollsterId,
            List<DistrictPollInput> districts,
            List<string> partyIds,
            double[] bias,
            double[] houseEffect,
            double idioScale,
            double sampleScale,
            PollingTuning p)
        {
            var results = new List<DistrictPollResult>(districts.Count);

            for (int i = 0; i < districts.Count; i++)
            {
                DistrictPollInput d = districts[i];

                // An under-sampled district is not shifted within itself — the pollster has no reason
                // to mis-read the district's own composition — but it rests on fewer respondents, so
                // its crosstab is noisier. exp(-bias/2) is 1/sqrt of the sampling weight ratio, i.e.
                // the standard-error inflation of a smaller sub-sample.
                double crosstabNoiseScale = Math.Exp(-bias[i] * 0.5);

                double[] values = new double[partyIds.Count];
                double[] trueLocal = LocalShares(d, partyIds);

                for (int j = 0; j < partyIds.Count; j++)
                {
                    double localError = SeedStreams
                        .RngFor(request.SaveGuid, request.Date, StreamNames.PollError,
                                Entity(pollsterId, d.DistrictId, partyIds[j]))
                        .NextGaussian() * p.ErrorSigma * sampleScale * crosstabNoiseScale;

                    values[j] = trueLocal[j] + (houseEffect[j] + localError) * idioScale;
                }

                double localTurnoutNoise = SeedStreams
                    .RngFor(request.SaveGuid, request.Date, StreamNames.PollTurnout,
                            Entity(pollsterId, d.DistrictId))
                    .NextGaussian() * p.ErrorSigma * sampleScale * crosstabNoiseScale * idioScale;

                results.Add(new DistrictPollResult
                {
                    DistrictId = d.DistrictId,
                    Shares = FinalizeShares(partyIds, values, p, applyMinimum: true),
                    ProjectedTurnout = Round(
                        Clamp(Clamp(d.ProjectedTurnout, 0.0, 1.0) + localTurnoutNoise, 0.0, 1.0),
                        p.RoundingDecimals),
                    // Reported raw, not rounded: this is the diagnostic the gate asserts on, and
                    // rounding a value of order 0.01 to three decimals would blunt it.
                    SamplingBias = bias[i]
                });
            }

            return results;
        }

        private static List<DistrictPollInput> SortedDistricts(List<DistrictPollInput>? input)
        {
            var copy = new List<DistrictPollInput>();
            if (input == null) return copy;

            foreach (DistrictPollInput d in input)
                if (d != null) copy.Add(d);

            // Explicit ordinal sort by district id. Aggregation sums doubles, and floating-point
            // addition is not associative, so an unsorted input would change the published number.
            copy.Sort((x, y) => string.CompareOrdinal(x.DistrictId ?? "", y.DistrictId ?? ""));
            return copy;
        }

        private static List<string> PartyUniverse(List<DistrictPollInput> districts)
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (DistrictPollInput d in districts)
            {
                if (d.TrueShares == null) continue;
                foreach (PartyVoteShare s in d.TrueShares)
                    if (!string.IsNullOrEmpty(s.PartyId)) set.Add(s.PartyId);
            }

            return new List<string>(set); // SortedSet enumerates in ordinal ascending order.
        }

        private static double[] TrueWeights(List<DistrictPollInput> districts)
        {
            double[] mass = new double[districts.Count];
            double total = 0.0;

            for (int i = 0; i < districts.Count; i++)
            {
                int eligible = districts[i].EligibleVoters;
                if (eligible < 0) eligible = 0;
                double turnout = Clamp(districts[i].ProjectedTurnout, 0.0, 1.0);
                mass[i] = eligible * turnout;
                total += mass[i];
            }

            if (total <= 0.0)
            {
                // No modelled voters anywhere (an empty or brand-new city). Fall back to equal
                // weights so the poll is uninformative rather than undefined.
                for (int i = 0; i < mass.Length; i++) mass[i] = 1.0 / districts.Count;
                return mass;
            }

            for (int i = 0; i < mass.Length; i++) mass[i] /= total;
            return mass;
        }

        private static double[] LocalShares(DistrictPollInput district, List<string> partyIds)
        {
            double[] values = new double[partyIds.Count];
            if (district.TrueShares == null) return values;

            // Linear scan over a short, already-sorted list. A Dictionary would be faster and would
            // also be an iteration-order hazard the moment someone enumerated it.
            foreach (PartyVoteShare s in district.TrueShares)
            {
                int index = IndexOfOrdinal(partyIds, s.PartyId);
                if (index >= 0) values[index] += s.Share;
            }

            return values;
        }

        private static double[] Aggregate(List<DistrictPollInput> districts, List<string> partyIds, double[] weights)
        {
            double[] totals = new double[partyIds.Count];

            for (int i = 0; i < districts.Count; i++)
            {
                double[] local = LocalShares(districts[i], partyIds);
                double localSum = 0.0;
                for (int j = 0; j < local.Length; j++) localSum += local[j];

                // Renormalise each district before it enters the aggregate, so a district whose model
                // shares sum to 0.98 does not quietly lose weight relative to one that sums to 1.00.
                if (localSum <= 0.0) continue;
                for (int j = 0; j < local.Length; j++) totals[j] += weights[i] * (local[j] / localSum);
            }

            return totals;
        }

        /// <summary>
        /// Clamps, renormalises, rounds and packages shares. Sorted by party id, because
        /// <paramref name="partyIds"/> already is.
        /// </summary>
        private static List<PartyVoteShare> FinalizeShares(List<string> partyIds, double[] values,
                                                     PollingTuning p, bool applyMinimum)
        {
            int n = partyIds.Count;
            var result = new List<PartyVoteShare>(n);
            if (n == 0) return result;

            // A floor of x per party is infeasible once n * x > 1, so cap it at an equal split.
            double floor = applyMinimum ? Clamp(p.MinPublishedShare, 0.0, 1.0 / n) : 0.0;

            double[] work = new double[n];
            double total = 0.0;
            for (int j = 0; j < n; j++)
            {
                double v = values[j];
                if (double.IsNaN(v) || double.IsInfinity(v) || v < floor) v = floor;
                work[j] = v;
                total += v;
            }

            if (total <= 0.0)
            {
                for (int j = 0; j < n; j++) work[j] = 1.0 / n;
            }
            else
            {
                for (int j = 0; j < n; j++) work[j] /= total;
            }

            double roundedTotal = 0.0;
            int largest = 0;
            for (int j = 0; j < n; j++)
            {
                work[j] = Round(work[j], p.RoundingDecimals);
                roundedTotal += work[j];
                if (work[j] > work[largest]) largest = j; // ties keep the lowest party id, already sorted
            }

            // Rounding leaves a residual of at most n/2 ulps of the last decimal. Parking it on the
            // largest party keeps the published shares summing to exactly 1 without a visible edit.
            double residual = 1.0 - roundedTotal;
            if (residual != 0.0)
                work[largest] = Round(work[largest] + residual, p.RoundingDecimals);

            for (int j = 0; j < n; j++)
                result.Add(new PartyVoteShare(partyIds[j], work[j]));

            return result;
        }

        private static int WeeksToElection(PollRequest request, PollingTuning p)
        {
            int max = p.WeeksBeforeElection > 0 ? p.WeeksBeforeElection : 1;
            if (request.ElectionDate == null) return max;

            int weeks = PollCalendar.WeeksBetween(request.Date, request.ElectionDate.Value);
            if (weeks < 0) weeks = 0;
            return weeks > max ? max : weeks;
        }

        /// <summary>0 at the start of the campaign, 1 on election day.</summary>
        private static double CampaignProgress(int weeksToElection, PollingTuning p)
        {
            double max = p.WeeksBeforeElection > 0 ? p.WeeksBeforeElection : 1;
            return Clamp(1.0 - weeksToElection / max, 0.0, 1.0);
        }

        private static int DrawSampleSize(PollRequest request, PollingTuning p)
        {
            int baseSize = p.SampleSizeBase > 0 ? p.SampleSizeBase : 1;
            double variance = Clamp(p.SampleSizeVariance, 0.0, 1.0);

            double u = SeedStreams
                .RngFor(request.SaveGuid, request.Date, StreamNames.PollSample, request.PollsterId ?? "")
                .NextDouble();

            double size = baseSize * (1.0 + variance * (2.0 * u - 1.0));
            int n = (int)Math.Round(size, MidpointRounding.AwayFromZero);
            return n < 1 ? 1 : n;
        }

        private static double MarginOfError(int sampleSize, PollingTuning p)
        {
            // Standard error of a proportion at its worst case (p = 0.5), which is what every real
            // pollster reports as "the" margin of error.
            if (sampleSize < 1) sampleSize = 1;
            return p.MarginOfErrorMultiplier * Math.Sqrt(0.25 / sampleSize);
        }

        /// <summary>
        /// Scales the random error by the sample actually drawn: <c>errorSigma</c> is calibrated at
        /// <c>sampleSizeBase</c>, and a smaller sample is proportionally noisier.
        /// </summary>
        private static double SampleErrorScale(int sampleSize, PollingTuning p)
        {
            if (p.SampleSizeBase <= 0 || sampleSize < 1) return 1.0;
            return Math.Sqrt((double)p.SampleSizeBase / sampleSize);
        }

        private static double UndecidedShare(int weeksToElection, PollingTuning p)
        {
            int max = p.WeeksBeforeElection > 0 ? p.WeeksBeforeElection : 1;
            int weeksElapsed = max - weeksToElection;
            if (weeksElapsed < 0) weeksElapsed = 0;

            // Geometric decay, not linear: at the shipped values a linear decay would reach zero
            // undecideds two weeks into a nine week campaign.
            double retained = 1.0 - Clamp(p.UndecidedDecayPerWeek, 0.0, 1.0);
            return Clamp(p.UndecidedShareBase * Math.Pow(retained, weeksElapsed), 0.0, 1.0);
        }

        private static int IndexOfOrdinal(List<string> ids, string? value)
        {
            if (value == null) return -1;
            for (int i = 0; i < ids.Count; i++)
                if (string.CompareOrdinal(ids[i], value) == 0) return i;
            return -1;
        }

        private static string Entity(string a, string b) => a + "|" + b;
        private static string Entity(string a, string b, string c) => a + "|" + b + "|" + c;

        /// <summary>netstandard2.0 has no <c>Math.Clamp</c>; polyfilled here rather than raising the target.</summary>
        private static double Clamp(double value, double min, double max) =>
            value < min ? min : value > max ? max : value;

        private static double Round(double value, int decimals)
        {
            int d = decimals < 0 ? 0 : decimals > 15 ? 15 : decimals;
            return Math.Round(value, d, MidpointRounding.AwayFromZero);
        }
    }
}
