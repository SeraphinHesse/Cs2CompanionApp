using System;
using Agora.Core.Tuning;

namespace Agora.Core.Stories
{
    /// <summary>
    /// The political-power arithmetic: accrual, award, penalty, affordability and debt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pure arithmetic only: the <i>consequence</i> of debt is a wave-4 effect, and nothing here
    /// applies anything. Every number comes from <c>tuning.Power</c> — none lives in this file.
    /// </para>
    /// <para>
    /// <b>Rounding convention: every currency amount truncates toward zero.</b> One rule, applied to
    /// both signs, and it is conservative in both directions — a gain never rounds up past what the
    /// tuning promises, a penalty never rounds up past what the tuning threatens. Rounding a gain up
    /// instead would hand a 1%-of-the-vote government a guaranteed point a month and flatten
    /// <c>gainPopularityCurve</c> exactly where it is meant to bite hardest; rounding a penalty up
    /// would charge the player more than the ratio says. So: toward zero, everywhere.
    /// </para>
    /// </remarks>
    public static class PoliticalPower
    {
        /// <summary>
        /// The accrual for one month, capped at <c>power.maxMonthlyGain</c> and scaled by the
        /// governing party's or coalition's current vote share through
        /// <c>power.gainPopularityCurve</c>.
        /// </summary>
        /// <param name="governingVoteShare">
        /// 0–1. Zero when nobody is governing, which yields nothing rather than a negative accrual.
        /// A share outside 0–1 or non-finite is clamped into range rather than propagated: this is a
        /// currency, and a NaN entering it would poison the balance and the ledger together.
        /// </param>
        /// <remarks>
        /// <b>Idempotent per month, and that is load-bearing.</b> The caller must refuse to accrue
        /// twice for one month — <c>PoliticalPowerState.LastAccrualMonth</c> is the guard, and it is
        /// the partner of wave 0's <c>LastCompletedTickMonth</c>. Without it, save-scumming a month
        /// boundary farms power without limit, which is the exploit wave 0 landed first to prevent.
        /// <b>This function does not and cannot enforce that</b> — it is a pure function of share and
        /// tuning, with no state to check the month against. Wave 4 wires the guard at the call site;
        /// it is said out loud here so nobody assumes it already happened somewhere.
        /// </remarks>
        public static int AccrualFor(double governingVoteShare, EngineTuning tuning)
        {
            PowerTuning t = tuning.Power;
            if (!t.Enabled) return 0;

            // A negative or absent ceiling means nothing accrues, never a debit.
            int cap = t.MaxMonthlyGain;
            if (cap <= 0) return 0;

            double share = Clamp01(governingVoteShare);
            if (share <= 0.0) return 0; // Nobody governing: nothing, and in particular not a penalty.

            // The schema pins gainPopularityCurve above zero; a hand-edited file that breaks that
            // falls back to the identity exponent rather than to a shaped guess.
            double curve = t.GainPopularityCurve;
            if (!IsFinite(curve) || curve <= 0.0) curve = 1.0;

            double scaled = cap * Math.Pow(share, curve);
            if (!IsFinite(scaled)) return 0;

            int gain = (int)scaled; // Truncates toward zero — see the rounding convention above.
            if (gain < 0) gain = 0;
            return gain > cap ? cap : gain;
        }

        /// <summary>What buying off a slot of this tier costs.</summary>
        public static int OverrideCost(StoryTier tier, EngineTuning tuning)
        {
            int cost = AmountFor(tuning.Power.OverrideCost, tier);
            return cost < 0 ? 0 : cost; // A negative cost would pay the player to skip the work.
        }

        /// <summary>
        /// Whether the balance covers an override of this tier.
        /// </summary>
        /// <remarks>
        /// A refusal must reach the player as a legible reason, never a silent no-op. The balance is
        /// allowed to be negative already — debt is a state, not a bar to further play — so this asks
        /// whether the spend is affordable, not whether the player is solvent: a balance of -10 buys
        /// nothing that costs 5, but a balance of 60 buys a 50 override whatever the balance's
        /// history. A free override is always affordable, debt or not.
        /// </remarks>
        public static bool CanAfford(PoliticalPowerState power, StoryTier tier, EngineTuning tuning)
        {
            // The master switch off means the economy is inert, so no override may be bought — the
            // cost quote above stays honest, but nothing may be spent against it.
            if (!tuning.Power.Enabled) return false;

            int cost = OverrideCost(tier, tuning);
            if (cost <= 0) return true;
            return power.Balance >= cost;
        }

        /// <summary>
        /// The signed power movement for one resolved slot.
        /// </summary>
        /// <param name="manualDeclared">
        /// True when the player declared this outcome themselves. <b>A manual award is capped at the
        /// MINOR rate whatever the tier</b> — otherwise a one-word justification on a mandatory event
        /// mints the mandatory award, which is the one real exploit surface in the whole design. The
        /// cap is applied to the <i>tier</i>, so a self-declared not-met is scored at the minor rate
        /// too: an unverified declaration is worth no more and no less than a minor one in either
        /// direction, and treating the two directions differently would make honesty about a failure
        /// cost more than the same honesty could ever earn.
        /// </param>
        /// <remarks>
        /// A not-met slot loses <c>power.failureLossRatio</c> of the tier's gain — below 1 on
        /// purpose, so failing costs less than succeeding pays and the economy rewards engagement.
        /// An <see cref="SlotOutcome.Unmeasurable"/> slot moves the balance by <b>zero</b>: the whole
        /// three-state design exists so that a sensor gap never costs the player anything, and it is
        /// neither a small award nor a small penalty. A <see cref="SlotOutcome.Pending"/> slot has
        /// not resolved and likewise moves nothing.
        /// </remarks>
        public static int AwardFor(SlotOutcome outcome, StoryTier tier, bool manualDeclared,
                                   EngineTuning tuning)
        {
            PowerTuning t = tuning.Power;
            if (!t.Enabled) return 0;

            // Unmeasurable and Pending both move the balance by exactly zero, and are handled before
            // any tuning is read so that no rounding path can turn "no reading" into a movement.
            if (outcome != SlotOutcome.Met && outcome != SlotOutcome.NotMet) return 0;

            // The manual cap: a self-declared outcome is scored at the minor rate whatever the event.
            StoryTier scoredTier = manualDeclared ? StoryTier.Minor : tier;

            int gain = AmountFor(t.SuccessGain, scoredTier);
            if (gain < 0) gain = 0; // A tier that pays nothing pays nothing; it never debits.

            if (outcome == SlotOutcome.Met) return gain;

            // Not met. The schema pins failureLossRatio to the unit interval; clamping to it here
            // keeps a hand-edited file from making failure cost more than success pays, which is the
            // one property the ratio exists to guarantee.
            double ratio = t.FailureLossRatio;
            if (!IsFinite(ratio) || ratio <= 0.0) return 0;
            if (ratio > 1.0) ratio = 1.0;

            int loss = (int)(gain * ratio); // Truncates toward zero: the player is never overcharged.
            return -loss;
        }

        /// <summary>True when the balance is negative and the debt penalty applies.</summary>
        public static bool IsInDebt(PoliticalPowerState power)
        {
            return power != null && power.Balance < 0;
        }

        /// <summary>The tier's entry in a <see cref="PowerTierAmounts"/> block.</summary>
        private static int AmountFor(PowerTierAmounts amounts, StoryTier tier)
        {
            if (amounts == null) return 0;
            switch (tier)
            {
                case StoryTier.Mandatory: return amounts.Mandatory;
                case StoryTier.Major: return amounts.Major;
                default: return amounts.Minor; // Minor, and the fallback for an unknown tier value.
            }
        }

        /// <summary>netstandard2.0 has no <c>Math.Clamp</c>; this is the polyfill. NaN clamps to 0.</summary>
        private static double Clamp01(double v)
        {
            if (double.IsNaN(v)) return 0.0;
            if (v < 0.0) return 0.0;
            return v > 1.0 ? 1.0 : v;
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
