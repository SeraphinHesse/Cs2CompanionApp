using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Indices
{
    /// <summary>
    /// Packet 13 — the derived-index formulas, as free functions.
    ///
    /// <para>
    /// Every function here is pure, total and deterministic: no RNG, no clock, no collection
    /// iteration whose order is not fixed by the caller. Indices are aggregates, not draws, so this
    /// packet never touches <c>SeedStreams</c> — there is nothing here to seed.
    /// </para>
    ///
    /// <para>
    /// Every coefficient arrives through <see cref="IndicesTuning"/>. The only bare numbers below are
    /// structural normalisers whose value is forced by the definition of the quantity (the <c>2</c>
    /// that turns a mean absolute deviation of a <c>[0,1]</c> variable into a <c>[0,1]</c>
    /// dispersion; the <c>1</c> in the saturating map <c>x/(1+x)</c>), plus a divide-by-zero guard.
    /// Those are not tuning knobs and must not become tuning keys.
    /// </para>
    /// </summary>
    public static class IndexFormulas
    {
        /// <summary>Divide-by-zero guard. Not a tuning coefficient.</summary>
        private const double Epsilon = 1e-12;

        // -- Small numeric helpers (netstandard2.0: no Math.Clamp, no MathF) --------------------

        /// <summary>Clamps <paramref name="value"/> into <c>[min, max]</c>. NaN reads as <paramref name="min"/>.</summary>
        public static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value)) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>Clamps into <c>[0, 1]</c>. NaN reads as 0.</summary>
        public static double Clamp01(double value) => Clamp(value, 0.0, 1.0);

        /// <summary>
        /// Saturating normaliser for an unbounded non-negative quantity:
        /// <c>σ(x) = x / (1 + x)</c>, mapping <c>0 → 0</c>, <c>1 → 0.5</c>, <c>∞ → 1</c>.
        /// </summary>
        /// <remarks>
        /// Used wherever a ratio has no natural ceiling (a rent trend, a commute overrun). It is
        /// monotone, smooth and needs no scale constant, which is why the alternative — an arbitrary
        /// "full scale at N%" divisor — is deliberately not used. Negative input reads as 0.
        /// </remarks>
        public static double Saturate(double x)
        {
            if (double.IsNaN(x) || x <= 0.0) return 0.0;
            if (double.IsPositiveInfinity(x)) return 1.0;
            return x / (1.0 + x);
        }

        /// <summary>
        /// Relative rise from <paramref name="then"/> to <paramref name="now"/>, saturated into
        /// <c>[0, 1]</c>. A fall reads 0. A non-positive baseline reads 0 (nothing to grow from).
        /// </summary>
        public static double RelativeRise(double then, double now)
        {
            if (then <= Epsilon || double.IsNaN(then) || double.IsNaN(now)) return 0.0;
            return Saturate((now - then) / then);
        }

        /// <summary>
        /// Relative fall from <paramref name="then"/> to <paramref name="now"/> as a fraction of the
        /// baseline, in <c>[0, 1]</c>. A rise reads 0. Already bounded, so it is not saturated.
        /// </summary>
        public static double RelativeDrop(double then, double now)
        {
            if (then <= Epsilon || double.IsNaN(then) || double.IsNaN(now)) return 0.0;
            return Clamp01((then - now) / then);
        }

        // -- Gini -------------------------------------------------------------------------------

        /// <summary>
        /// Income proxy of a wealth tier on <c>[0, 1]</c>: the tier's normalised position on the
        /// bloc wealth axis, rescaled from <c>[-1, +1]</c>. Low = 0, Middle = 0.5, High = 1.
        /// </summary>
        public static double IncomeProxy(WealthTier tier) => (BlocAxes.Axis(tier) + 1.0) / 2.0;

        /// <summary>
        /// Wealth Gini coefficient of a three-tier distribution, in <c>[0, 1]</c>.
        ///
        /// <para><b>Formula.</b> Treat the population as three point masses at income
        /// <see cref="IncomeProxy"/>(tier). Its Lorenz curve <c>L(p)</c> — cumulative income share
        /// held by the poorest <c>p</c> of the population — is piecewise linear and convex. Sample it
        /// at <c>indices.giniSampleBuckets</c> equal-population points, integrate by the trapezoid
        /// rule to get the area <c>A</c> under it, and return <c>G = 1 - 2A</c>.</para>
        ///
        /// <para><b>Range.</b> <c>[0, 1]</c>. 0 when everyone sits in one tier (or when total income
        /// is zero, i.e. an all-Low city: everyone is equal, so there is no inequality to report).
        /// It approaches 1 as a vanishing elite holds all the income.</para>
        ///
        /// <para><b>Accuracy.</b> The trapezoid chords lie above a convex Lorenz curve, so a bucket
        /// grid that does not align with the tier breakpoints slightly overstates <c>A</c> and
        /// therefore understates <c>G</c>. Where the breakpoints land on bucket edges the result is
        /// exact — which is how the golden test pins it.</para>
        /// </summary>
        public static double Gini(WealthDistribution wealth, int buckets)
        {
            int b = buckets < 1 ? 1 : buckets;

            double totalPop = 0.0;
            double totalIncome = 0.0;
            for (int i = 0; i < BlocAxes.Wealth.Count; i++)
            {
                WealthTier tier = BlocAxes.Wealth[i];
                double share = wealth[tier];
                if (double.IsNaN(share) || share <= 0.0) continue;
                totalPop += share;
                totalIncome += share * IncomeProxy(tier);
            }

            if (totalPop <= Epsilon || totalIncome <= Epsilon) return 0.0;

            double area = 0.0;
            double prevP = 0.0;
            double prevL = 0.0;
            for (int k = 1; k <= b; k++)
            {
                double p = (double)k / b;
                double l = Lorenz(wealth, totalPop, totalIncome, p);
                area += (l + prevL) * 0.5 * (p - prevP);
                prevP = p;
                prevL = l;
            }

            return Clamp01(1.0 - 2.0 * area);
        }

        /// <summary>
        /// Cumulative income share held by the poorest <paramref name="p"/> of the population.
        /// Walks <see cref="BlocAxes.Wealth"/> in ascending order — a fixed array, never a dictionary.
        /// </summary>
        private static double Lorenz(WealthDistribution wealth, double totalPop, double totalIncome, double p)
        {
            double target = Clamp01(p) * totalPop;
            double cumPop = 0.0;
            double cumIncome = 0.0;
            for (int i = 0; i < BlocAxes.Wealth.Count; i++)
            {
                WealthTier tier = BlocAxes.Wealth[i];
                double share = wealth[tier];
                if (double.IsNaN(share) || share <= 0.0) continue;

                double take = target - cumPop;
                if (take <= 0.0) break;
                if (take > share) take = share;

                cumIncome += take * IncomeProxy(tier);
                cumPop += take;
            }

            return cumIncome / totalIncome;
        }

        // -- Gentrification ---------------------------------------------------------------------

        /// <summary>
        /// Per-district gentrification pressure, in <c>[0, 1]</c>.
        ///
        /// <para><b>Formula.</b> A weighted blend of three signals measured over
        /// <c>indices.gentrificationWindowMonths</c>:</para>
        /// <list type="bullet">
        /// <item><description><i>rent</i> = <c>σ(rentTrend)</c> — the rent already arrives as a
        /// fractional change over the window; a doubling scores 0.5, a fall scores 0.</description></item>
        /// <item><description><i>education</i> = relative rise in the district's education index —
        /// incomers are better schooled than the people they replace.</description></item>
        /// <item><description><i>turnover</i> = relative fall in the district's Low-wealth share —
        /// the displacement leg. Already a bounded fraction, so it is not saturated.</description></item>
        /// </list>
        /// <para>Weights are <c>gentrificationRentWeight</c> / <c>…EducationWeight</c> /
        /// <c>…TurnoverWeight</c>. With no history in the window every historical leg reads 0, so a
        /// fresh save reports rent pressure only — never a fabricated trend.</para>
        ///
        /// <para><b>Range.</b> <c>[0, 1]</c> (the shipped weights sum to 1; the result is clamped
        /// regardless, so a retune cannot push it out of range).</para>
        /// </summary>
        public static double Gentrification(
            double rentTrend,
            double educationIndexThen,
            double educationIndexNow,
            double lowWealthShareThen,
            double lowWealthShareNow,
            bool hasHistory,
            IndicesTuning t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));

            double rent = Saturate(rentTrend);
            double education = hasHistory ? RelativeRise(educationIndexThen, educationIndexNow) : 0.0;
            double turnover = hasHistory ? RelativeDrop(lowWealthShareThen, lowWealthShareNow) : 0.0;

            double raw = t.GentrificationRentWeight * rent
                       + t.GentrificationEducationWeight * education
                       + t.GentrificationTurnoverWeight * turnover;

            return Clamp(raw, t.ClampMin, t.ClampMax);
        }

        // -- Brain drain ------------------------------------------------------------------------

        /// <summary>
        /// City brain drain, in <c>[0, 1]</c>. Higher is worse.
        ///
        /// <para><b>Formula.</b> Over <c>indices.brainDrainWindowMonths</c>:</para>
        /// <list type="bullet">
        /// <item><description><i>education</i> = relative fall in the city education index — the
        /// composition leg.</description></item>
        /// <item><description><i>outflow</i> = relative fall in the head count of WellEducated +
        /// HighlyEducated residents — the absolute leg, which catches a shrinking city whose mix
        /// happens to hold steady.</description></item>
        /// </list>
        /// <para>Blended by <c>brainDrainEducationWeight</c> / <c>brainDrainOutflowWeight</c>. Both
        /// legs are bounded fractions of their baseline, so neither is saturated. No history in the
        /// window reads 0 — an unmeasured drain is not a drain.</para>
        ///
        /// <para><b>Range.</b> <c>[0, 1]</c>, clamped.</para>
        /// </summary>
        public static double BrainDrain(
            double educationIndexThen,
            double educationIndexNow,
            double skilledResidentsThen,
            double skilledResidentsNow,
            bool hasHistory,
            IndicesTuning t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            if (!hasHistory) return Clamp(0.0, t.ClampMin, t.ClampMax);

            double education = RelativeDrop(educationIndexThen, educationIndexNow);
            double outflow = RelativeDrop(skilledResidentsThen, skilledResidentsNow);

            double raw = t.BrainDrainEducationWeight * education + t.BrainDrainOutflowWeight * outflow;
            return Clamp(raw, t.ClampMin, t.ClampMax);
        }

        /// <summary>
        /// Head count of WellEducated + HighlyEducated residents implied by a population and an
        /// education mix. The "skilled" cut is the top two tiers of
        /// <see cref="EducationDistribution"/>.
        /// </summary>
        public static double SkilledResidents(int population, EducationDistribution education)
        {
            if (population <= 0) return 0.0;
            double share = education[EducationTier.WellEducated] + education[EducationTier.HighlyEducated];
            if (double.IsNaN(share) || share <= 0.0) return 0.0;
            return population * share;
        }

        // -- Commute misery ---------------------------------------------------------------------

        /// <summary>
        /// Commute misery, in <c>[0, 1]</c>.
        ///
        /// <para><b>Formula.</b> <c>w_time · σ((minutes − ref) / ref) + w_congestion · congestion</c>,
        /// where <c>ref</c> is <c>indices.commuteMiseryReferenceMinutes</c>. A commute at or under the
        /// reference contributes nothing; double the reference contributes half scale.</para>
        ///
        /// <para><b>Range.</b> <c>[0, 1]</c>, clamped.</para>
        /// </summary>
        public static double CommuteMisery(double averageCommuteMinutes, double trafficCongestion, IndicesTuning t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));

            double reference = t.CommuteMiseryReferenceMinutes;
            double overrun = reference <= Epsilon
                ? 0.0
                : Saturate((averageCommuteMinutes - reference) / reference);

            double raw = t.CommuteMiseryTimeWeight * overrun
                       + t.CommuteMiseryCongestionWeight * Clamp01(trafficCongestion);

            return Clamp(raw, t.ClampMin, t.ClampMax);
        }

        // -- Service coverage and inequality ------------------------------------------------------

        /// <summary>
        /// Weighted mean service coverage, in <c>[0, 1]</c>. Higher is better. Sums the nine services
        /// in the declaration order of <see cref="ServiceCoverage"/>, which is fixed, so the
        /// floating-point result is bit-stable.
        /// </summary>
        public static double WeightedCoverage(ServiceCoverage coverage, ServiceCoverage weights)
        {
            double num = 0.0;
            double den = 0.0;
            AccumulateService(coverage.Health, weights.Health, ref num, ref den);
            AccumulateService(coverage.Education, weights.Education, ref num, ref den);
            AccumulateService(coverage.Police, weights.Police, ref num, ref den);
            AccumulateService(coverage.Fire, weights.Fire, ref num, ref den);
            AccumulateService(coverage.Garbage, weights.Garbage, ref num, ref den);
            AccumulateService(coverage.Transit, weights.Transit, ref num, ref den);
            AccumulateService(coverage.Water, weights.Water, ref num, ref den);
            AccumulateService(coverage.Electricity, weights.Electricity, ref num, ref den);
            AccumulateService(coverage.Parks, weights.Parks, ref num, ref den);
            return den <= Epsilon ? 0.0 : Clamp01(num / den);
        }

        private static void AccumulateService(double value, double weight, ref double num, ref double den)
        {
            if (double.IsNaN(weight) || weight <= 0.0) return;
            num += weight * Clamp01(value);
            den += weight;
        }

        /// <summary>
        /// Population-weighted dispersion of a <c>[0, 1]</c> variable across districts, in
        /// <c>[0, 1]</c>.
        ///
        /// <para><b>Formula.</b> <c>2 · Σ p_i · |v_i − v̄|</c> where <c>v̄ = Σ p_i v_i</c> and the
        /// <c>p_i</c> are normalised population weights. The mean absolute deviation of a variable
        /// confined to <c>[0, 1]</c> is at most 0.5 (half the mass at each end), so the factor 2 is
        /// the exact normaliser, not a tuning choice.</para>
        ///
        /// <para><b>Range.</b> <c>[0, 1]</c>. Fewer than two samples reads 0 — one district cannot be
        /// unequal with itself. Zero total population falls back to equal weights so a paper city
        /// still reports its spread.</para>
        ///
        /// <para>The caller supplies the sample order; this function does not sort, because summation
        /// order is part of the determinism contract and belongs to the caller that owns the ids.</para>
        /// </summary>
        public static double Dispersion(IReadOnlyList<double> values, IReadOnlyList<double> populationWeights)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (populationWeights == null) throw new ArgumentNullException(nameof(populationWeights));
            if (values.Count != populationWeights.Count)
                throw new ArgumentException("values and populationWeights must be the same length.", nameof(populationWeights));
            if (values.Count < 2) return 0.0;

            double totalWeight = 0.0;
            for (int i = 0; i < populationWeights.Count; i++)
            {
                double w = populationWeights[i];
                if (double.IsNaN(w) || w <= 0.0) continue;
                totalWeight += w;
            }

            bool uniform = totalWeight <= Epsilon;
            if (uniform) totalWeight = values.Count;

            double mean = 0.0;
            for (int i = 0; i < values.Count; i++)
            {
                double w = Weight(populationWeights, i, uniform);
                mean += (w / totalWeight) * Clamp01(values[i]);
            }

            double mad = 0.0;
            for (int i = 0; i < values.Count; i++)
            {
                double w = Weight(populationWeights, i, uniform);
                mad += (w / totalWeight) * Math.Abs(Clamp01(values[i]) - mean);
            }

            return Clamp01(2.0 * mad);
        }

        private static double Weight(IReadOnlyList<double> weights, int i, bool uniform)
        {
            if (uniform) return 1.0;
            double w = weights[i];
            return double.IsNaN(w) || w <= 0.0 ? 0.0 : w;
        }

        // -- Polarization -----------------------------------------------------------------------

        /// <summary>
        /// Fragmentation of the party system, in <c>[0, 1]</c>.
        ///
        /// <para><b>Formula.</b> The normalised Herfindahl complement:
        /// <c>(1 − Σ sᵢ²) / (1 − 1/n)</c> over <c>n</c> parties with normalised vote shares
        /// <c>sᵢ</c>, scaled by <c>indices.polarizationDispersionWeight</c>. One party holding
        /// everything gives 0; <c>n</c> equal parties give 1 for any <c>n ≥ 2</c>.</para>
        ///
        /// <para><b>Range.</b> <c>[0, 1]</c>, clamped. Fewer than two parties reads 0.</para>
        ///
        /// <para>The caller must pass the shares sorted by party id — the contractual order for every
        /// <see cref="PartyVoteShare"/> list — because the summation order fixes the last bits of the
        /// result.</para>
        /// </summary>
        public static double Polarization(IReadOnlyList<PartyVoteShare> shares, IndicesTuning t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            if (shares == null || shares.Count < 2) return Clamp(0.0, t.ClampMin, t.ClampMax);

            double total = 0.0;
            for (int i = 0; i < shares.Count; i++)
            {
                double s = shares[i].Share;
                if (double.IsNaN(s) || s <= 0.0) continue;
                total += s;
            }
            if (total <= Epsilon) return Clamp(0.0, t.ClampMin, t.ClampMax);

            double herfindahl = 0.0;
            for (int i = 0; i < shares.Count; i++)
            {
                double s = shares[i].Share;
                if (double.IsNaN(s) || s <= 0.0) continue;
                double n = s / total;
                herfindahl += n * n;
            }

            double maxComplement = 1.0 - 1.0 / shares.Count;
            if (maxComplement <= Epsilon) return Clamp(0.0, t.ClampMin, t.ClampMax);

            double raw = t.PolarizationDispersionWeight * ((1.0 - herfindahl) / maxComplement);
            return Clamp(raw, t.ClampMin, t.ClampMax);
        }

        // -- Discontent -------------------------------------------------------------------------

        /// <summary>
        /// Discontent, in <c>[0, 1]</c>.
        ///
        /// <para><b>Formula.</b>
        /// <c>w_h · (1 − happiness/100) + w_u · unemployment + w_s · (1 − weightedCoverage)</c>,
        /// with the weights from <c>indices.discontent*Weight</c> and the coverage term supplied by
        /// <see cref="WeightedCoverage"/>. Happiness arrives on the game's 0–100 scale; the other two
        /// are already fractions.</para>
        ///
        /// <para><b>Range.</b> <c>[0, 1]</c>, clamped.</para>
        /// </summary>
        public static double Discontent(double happiness0To100, double unemployment, double weightedCoverage, IndicesTuning t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));

            double unhappy = Clamp01(1.0 - happiness0To100 / 100.0);
            double jobless = Clamp01(unemployment);
            double underserved = Clamp01(1.0 - Clamp01(weightedCoverage));

            double raw = t.DiscontentHappinessWeight * unhappy
                       + t.DiscontentUnemploymentWeight * jobless
                       + t.DiscontentServiceWeight * underserved;

            return Clamp(raw, t.ClampMin, t.ClampMax);
        }

        // -- Legitimacy -------------------------------------------------------------------------

        /// <summary>
        /// Confidence in the political system, in <c>[0, 1]</c>. Higher is better.
        ///
        /// <para><b>Formula.</b> <c>w_t · turnout + w_m · mandateDelivery + w_s · stability</c>, with
        /// the weights from <c>indices.legitimacy*Weight</c>.</para>
        ///
        /// <para><b>Unmeasured components.</b> Each argument is nullable, and a null reads as
        /// <c>indices.clampMax</c> — full legitimacy on that leg. Legitimacy is eroded by measured
        /// failure, so before the first election, before the first mandate resolves and between
        /// governments there is nothing to erode it. Renormalising the weights instead would let a
        /// single measured leg swing the whole index, and defaulting to zero would fire unrest on a
        /// brand-new save.</para>
        ///
        /// <para><b>Range.</b> <c>[0, 1]</c>, clamped.</para>
        /// </summary>
        public static double Legitimacy(double? turnout, double? mandateDelivery, double? governmentStability, IndicesTuning t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));

            double full = t.ClampMax;
            double turnoutLeg = turnout.HasValue ? Clamp01(turnout.Value) : full;
            double mandateLeg = mandateDelivery.HasValue ? Clamp01(mandateDelivery.Value) : full;
            double stabilityLeg = governmentStability.HasValue ? Clamp01(governmentStability.Value) : full;

            double raw = t.LegitimacyTurnoutWeight * turnoutLeg
                       + t.LegitimacyMandateWeight * mandateLeg
                       + t.LegitimacyStabilityWeight * stabilityLeg;

            return Clamp(raw, t.ClampMin, t.ClampMax);
        }

        // -- Smoothing --------------------------------------------------------------------------

        /// <summary>
        /// One-pole exponential moving average: <c>α · raw + (1 − α) · previous</c>, with
        /// <c>α = indices.smoothingAlpha</c>. Applied to every index so a one-month spike does not
        /// read as a trend. With no previous value the raw figure passes through unchanged.
        /// </summary>
        public static double Smooth(double raw, double? previous, IndicesTuning t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            if (!previous.HasValue) return Clamp(raw, t.ClampMin, t.ClampMax);

            double alpha = Clamp01(t.SmoothingAlpha);
            double blended = alpha * raw + (1.0 - alpha) * previous.Value;
            return Clamp(blended, t.ClampMin, t.ClampMax);
        }
    }
}
