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
    /// <b>Rounding convention: the magnitude of every movement rounds down.</b> One rule for both
    /// signs — the balance never moves further than the tuning says it should. On a gain that is
    /// against the player and on a penalty it is for them, so this is not "conservative in the
    /// player's favour"; it is conservative about the <i>number</i>, which is what keeps a
    /// hand-edited tuning file from paying out more than it declares. Rounding a gain up instead
    /// would hand a 1%-of-the-vote government a guaranteed point a month and flatten
    /// <c>gainPopularityCurve</c> exactly where it is meant to bite hardest.
    /// </para>
    /// <para>
    /// The one deliberate exception: a penalty that rounds down to nothing is floored at 1, because a
    /// free failure alongside a paid success is a strictly better outcome than not playing, and
    /// <c>failureLossRatio</c> is per-save tuning that can reach that region.
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

        /// <summary>
        /// What buying off a slot of this tier costs, or 0 when the packet is off and nothing may be
        /// bought at all.
        /// </summary>
        /// <remarks>
        /// A disabled packet must not keep quoting a live price: the balance can no longer grow and
        /// <see cref="CanAfford"/> refuses every spend, so a UI rendering the quote would show a
        /// grey button against a number the player can never reach and nothing to explain it.
        /// </remarks>
        public static int OverrideCost(StoryTier tier, EngineTuning tuning)
        {
            PowerTuning t = tuning.Power;
            if (!t.Enabled) return 0;

            int cost = AmountFor(t.OverrideCost, tier);
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
            // The master switch off means the economy is inert: nothing may be bought, and the cost
            // quote is 0 to match rather than advertising a price against a frozen balance.
            if (!tuning.Power.Enabled) return false;

            int cost = OverrideCost(tier, tuning);
            if (cost <= 0) return true;

            // The state may legitimately be null: a sidecar written before the power block carries no
            // PoliticalPowerState, and PoliticalEngine.CloneState substitutes a fresh one only on its
            // own clone path. Opening a story slot on such a save must refuse, not throw on load.
            return power != null && power.Balance >= cost;
        }

        /// <summary>
        /// The signed power movement for one resolved slot.
        /// </summary>
        /// <param name="manualDeclared">
        /// True when the player declared this outcome themselves. <b>A manual award is capped at the
        /// MINOR rate whatever the tier</b> — otherwise a one-word justification on a mandatory event
        /// mints the mandatory award, which is the one real exploit surface in the whole design.
        /// <b>The cap is one-sided: it applies to the award only, never to the penalty</b>, so a
        /// self-declared failure is charged at the event's real tier. Capping both sides looks
        /// symmetrical and is a trap — it would make a truthful self-declared mandatory failure cost
        /// a fifth of what <c>Ignore</c> costs, handing that discount to any player who merely
        /// preferred the Manual button, no lying required. One-sided keeps honest self-reporting
        /// exactly as expensive as silence and never worse. See <see cref="PoliticalPowerState"/> for
        /// the residue this leaves and why it is not closable here.
        /// </param>
        /// <remarks>
        /// A not-met slot loses <c>power.failureLossRatio</c> of the tier's gain — below 1 on
        /// purpose, so failing costs less than succeeding pays and the economy rewards engagement.
        /// An <see cref="SlotOutcome.Unmeasurable"/> slot moves the balance by <b>zero</b>: it means
        /// the engine could not read the city, and a sensor gap must never cost the player anything.
        /// Silence is <i>not</i> a sensor gap and does not arrive here as one — an unaddressed or
        /// undeclared slot reaches this function as an ordinary <see cref="SlotOutcome.NotMet"/> and
        /// is charged as one. A <see cref="SlotOutcome.Pending"/> slot has not resolved and moves
        /// nothing.
        /// </remarks>
        public static int AwardFor(SlotOutcome outcome, StoryTier tier, bool manualDeclared,
                                   EngineTuning tuning)
        {
            PowerTuning t = tuning.Power;
            if (!t.Enabled) return 0;

            // Unmeasurable and Pending both move the balance by exactly zero, and are handled before
            // any tuning is read so that no rounding path can turn "no reading" into a movement.
            if (outcome != SlotOutcome.Met && outcome != SlotOutcome.NotMet) return 0;

            // The manual cap, one-sided: it lowers the tier an *award* is paid at and leaves a
            // penalty at the event's real tier.
            bool met = outcome == SlotOutcome.Met;
            StoryTier scoredTier = (manualDeclared && met) ? StoryTier.Minor : tier;

            int gain = AmountFor(t.SuccessGain, scoredTier);
            if (gain < 0) gain = 0; // A tier that pays nothing pays nothing; it never debits.

            if (met) return gain;

            // Not met. The schema pins failureLossRatio to the unit interval; clamping to it here
            // keeps a hand-edited file from making failure cost more than success pays, which is the
            // one property the ratio exists to guarantee.
            double ratio = t.FailureLossRatio;
            if (!IsFinite(ratio) || ratio <= 0.0 || gain == 0) return 0;
            if (ratio > 1.0) ratio = 1.0;

            // In decimal, not double: 90 * 0.7 in binary floating point is 62.99999999999999, which
            // would truncate to 62 and undercharge by a whole point on nothing but representation.
            decimal exact = gain * (decimal)ratio;
            int loss = (int)decimal.Floor(exact);

            // A penalty that rounds away entirely would make failing free while succeeding still
            // pays, which is strictly better for the player than not engaging at all. Unreachable at
            // shipped defaults, reachable at a low per-save failureLossRatio: floor it at 1.
            if (loss < 1) loss = 1;
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
