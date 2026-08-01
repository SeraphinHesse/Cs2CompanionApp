using System;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Blocs
{
    /// <summary>
    /// Turns a bloc's identity and its lived metrics into the two vectors the voter model runs on:
    /// <see cref="IssueWeights"/> (how much it cares) and <see cref="IssuePosition"/> (what it wants).
    ///
    /// <para>
    /// The split matters. <b>Composition sets the stance; lived metrics set the salience.</b> A
    /// retired homeowner and a young renter want different things whatever the city does to them, and
    /// that is composition. But which of those things they spend the election arguing about is
    /// decided by the commute they sat in and the rent they paid, and that is lived metrics. So the
    /// ideal point is a pure function of <see cref="BlocKey"/>, and only the weights move with the
    /// snapshot.
    /// </para>
    ///
    /// <para>
    /// Every method here is a pure function of its arguments — no state, no clock, no randomness.
    /// Bloc construction has no stochastic term at all: <c>blocs</c> declares no noise sigma, and
    /// inventing one would be a hardcoded coefficient. Noise enters the model downstream, in
    /// <c>voter.affinity.noise</c> and <c>voter.turnout.noise</c>.
    /// </para>
    /// </summary>
    public static class BlocIssueModel
    {
        /// <summary>
        /// The part of a bloc's issue weights that comes from who it is: priors, plus each axis
        /// sensitivity scaled by that axis's position in <c>[-1, +1]</c>.
        /// </summary>
        /// <remarks>
        /// Time-invariant for a given key and tuning. That is what lets <see cref="Resolve"/> smooth
        /// the whole weight vector and still be smoothing only the lived-metric term — see the note
        /// there.
        /// </remarks>
        public static IssueWeights CompositionWeights(BlocKey key, BlocsTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            return tuning.IssueWeightPriors
                .Add(tuning.WealthWeightSensitivity.Scale(BlocAxes.Axis(key.Wealth)))
                .Add(tuning.EducationWeightSensitivity.Scale(BlocAxes.Axis(key.Education)))
                .Add(tuning.AgeWeightSensitivity.Scale(BlocAxes.Axis(key.Age)));
        }

        /// <summary>
        /// Where this bloc would put policy if it could. Composition only, clamped back into
        /// <c>[-1, +1]</c> so the affinity kernel's distance normalisation stays honest.
        /// </summary>
        public static IssuePosition Ideal(BlocKey key, BlocsTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            return tuning.IdealBase
                .Add(tuning.WealthIdealSensitivity.Scale(BlocAxes.Axis(key.Wealth)))
                .Add(tuning.EducationIdealSensitivity.Scale(BlocAxes.Axis(key.Education)))
                .Add(tuning.AgeIdealSensitivity.Scale(BlocAxes.Axis(key.Age)))
                .Clamped();
        }

        /// <summary>
        /// How far lived metrics move each issue's weight, before smoothing and clamping.
        ///
        /// <para>
        /// Two halves, each with an implicit coefficient of one so that <c>livedMetricWeightGain</c>
        /// is the only dial:
        /// </para>
        /// <list type="bullet">
        /// <item><b>absolute</b> — the grievance itself. A city with filthy air raises environment
        /// salience everywhere, including in the districts that are merely as bad as everywhere else.</item>
        /// <item><b>relative</b> — the same grievance minus the city-wide level. Being worse off than
        /// the rest of town is its own, separate political fact.</item>
        /// </list>
        ///
        /// <para>
        /// The sum is then scaled by the bloc's <em>exposure</em> to that issue — its composition
        /// weight relative to the prior. This is what makes §4.3's example true as written: a long
        /// commute raises transit weight <em>for commuting blocs</em>, not uniformly for the
        /// pensioners on the same street. Exposure needs no new coefficient because the composition
        /// sensitivities already encode who is exposed to what.
        /// </para>
        ///
        /// <para>
        /// Every component is capped at <c>±livedMetricMaxShift</c>, so no single metric can
        /// dominate a bloc's politics however extreme the city gets (non-negotiable #5's spirit
        /// applied to the voter model).
        /// </para>
        /// </summary>
        public static IssueWeights LivedShift(IssueWeights composition, LivedPressure district,
                                              LivedPressure city, BlocsTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            double cap = Math.Abs(tuning.LivedMetricMaxShift);
            IssueWeights shift = new IssueWeights(0, 0, 0, 0, 0, 0);

            // Issues.All, never Enum.GetValues: the framework's ordering is unspecified and an
            // unspecified order in the engine is a determinism defect.
            for (int i = 0; i < Issues.All.Count; i++)
            {
                Issue issue = Issues.All[i];

                double absolute = district[issue];
                double relative = district[issue] - city[issue];
                double raw = tuning.LivedMetricWeightGain * (absolute + relative) * Exposure(composition, tuning, issue);

                shift = shift.With(issue, BlocMath.Clamp(raw, -cap, cap));
            }

            return shift;
        }

        /// <summary>
        /// The full weight pipeline: composition, plus the lived shift, smoothed against last tick,
        /// clamped to the tuned floor and ceiling, and renormalised to mean 1.0 if
        /// <c>blocs.normalizeWeights</c> says so.
        /// </summary>
        /// <param name="previous">
        /// Last tick's weights for this same bloc, or null on the first tick. This must come from the
        /// persisted <see cref="Bloc"/> and nowhere else: smoothing state that lives only in memory
        /// would produce different weights after a reload, which is precisely the desync
        /// non-negotiable #3 forbids.
        /// </param>
        /// <remarks>
        /// Smoothing is applied to the whole weight vector rather than to the lived term alone,
        /// because the lived term is not separately persisted. That is not an approximation:
        /// <see cref="CompositionWeights"/> is time-invariant, so
        /// <c>EMA(composition + lived) == composition + EMA(lived)</c> exactly, up to the clamp and
        /// the renormalisation that both forms share.
        /// </remarks>
        public static IssueWeights Resolve(BlocKey key, LivedPressure district, LivedPressure city,
                                           IssueWeights? previous, BlocsTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            IssueWeights composition = CompositionWeights(key, tuning);
            IssueWeights target = composition.Add(LivedShift(composition, district, city, tuning));

            IssueWeights smoothed = previous.HasValue
                ? Blend(target, previous.Value, tuning.LivedMetricSmoothingAlpha)
                : target;

            IssueWeights clamped = smoothed.Clamped(tuning.WeightFloor, tuning.WeightCeiling);
            return tuning.NormalizeWeights ? clamped.Normalized() : clamped;
        }

        /// <summary>
        /// Aggregate dissatisfaction, <c>[0, 1]</c>. Grievance weighted by how much this bloc cares
        /// about it — the same shortfall reads as fury in a bloc that prioritises services and as a
        /// shrug in one that does not.
        /// </summary>
        /// <remarks>
        /// The three tuned weights are renormalised by their own sum, so retuning one of them changes
        /// the balance without silently rescaling the whole discontent axis that turnout is calibrated
        /// against.
        /// </remarks>
        public static double Discontent(double happiness, LivedPressure district,
                                        IssueWeights weights, BlocsTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            double happinessWeight = tuning.DiscontentHappinessWeight;
            double serviceWeight = tuning.DiscontentServiceWeight;
            double costWeight = tuning.DiscontentCostWeight;

            double total = happinessWeight + serviceWeight + costWeight;
            if (total <= 0.0 || double.IsNaN(total)) return 0.0;

            double happinessGrievance = HappinessGrievance(happiness, tuning.ReferenceHappiness);
            double serviceGrievance = BlocMath.Clamp01(district[Issue.Services] * Salience(weights, Issue.Services));
            double costGrievance = BlocMath.Clamp01(district[Issue.CostOfLiving] * Salience(weights, Issue.CostOfLiving));

            double sum = happinessWeight * happinessGrievance
                       + serviceWeight * serviceGrievance
                       + costWeight * costGrievance;

            return BlocMath.Clamp01(sum / total);
        }

        /// <summary>
        /// Happiness as a grievance in <c>[0, 1]</c>: 0.5 at <c>blocs.referenceHappiness</c>, 1 at
        /// zero happiness, 0 at twice the reference. Linear, so no curve shape is smuggled in as an
        /// untunable constant.
        /// </summary>
        internal static double HappinessGrievance(double happiness, double referenceHappiness)
        {
            if (referenceHappiness <= 0.0 || double.IsNaN(referenceHappiness)) return 0.0;
            return BlocMath.Clamp01(0.5 * (1.0 + (referenceHappiness - happiness) / referenceHappiness));
        }

        /// <summary>
        /// A bloc's exposure to an issue: its composition weight over the prior for that issue. 1.0
        /// means "as exposed as the average bloc", and a bloc the sensitivities push to zero concern
        /// takes no lived kick at all.
        /// </summary>
        private static double Exposure(IssueWeights composition, BlocsTuning tuning, Issue issue)
        {
            double prior = tuning.IssueWeightPriors[issue];
            if (prior <= 0.0 || double.IsNaN(prior)) return 1.0;

            double weight = composition[issue];
            if (double.IsNaN(weight) || weight <= 0.0) return 0.0;

            return weight / prior;
        }

        /// <summary>
        /// How much this bloc cares about one issue relative to its own average concern. Independent
        /// of whether <c>normalizeWeights</c> is on, so discontent does not change scale with a flag.
        /// </summary>
        private static double Salience(IssueWeights weights, Issue issue)
        {
            double mean = weights.Sum() / Issues.Count;
            if (mean <= 0.0 || double.IsNaN(mean)) return 1.0;
            return weights[issue] / mean;
        }

        private static IssueWeights Blend(IssueWeights target, IssueWeights prior, double alpha)
        {
            // A tuning file with a nonsense alpha degrades to "no smoothing" (take the target whole)
            // rather than to a frozen or divergent weight vector.
            double a = double.IsNaN(alpha) ? 1.0 : BlocMath.Clamp(alpha, 0.0, 1.0);
            return target.Scale(a).Add(prior.Scale(1.0 - a));
        }
    }
}
