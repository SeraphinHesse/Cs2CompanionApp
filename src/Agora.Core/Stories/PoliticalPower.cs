using Agora.Core.Tuning;

namespace Agora.Core.Stories
{
    /// <summary>
    /// The political-power arithmetic: accrual, award, penalty, affordability and debt.
    /// </summary>
    /// <remarks>
    /// AGORA-SEAM(wave-2/2d) — <b>this is a stub.</b> Lane 2d delivers it. Pure arithmetic only: the
    /// <i>consequence</i> of debt is a wave-4 effect, and nothing here applies anything.
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
        /// </param>
        /// <remarks>
        /// <b>Idempotent per month, and that is load-bearing.</b> The caller must refuse to accrue
        /// twice for one month — <c>PoliticalPowerState.LastAccrualMonth</c> is the guard, and it is
        /// the partner of wave 0's <c>LastCompletedTickMonth</c>. Without it, save-scumming a month
        /// boundary farms power without limit, which is the exploit wave 0 landed first to prevent.
        /// </remarks>
        public static int AccrualFor(double governingVoteShare, EngineTuning tuning)
        {
            // AGORA-SEAM(wave-2/2d)
            return 0;
        }

        /// <summary>What buying off a slot of this tier costs.</summary>
        public static int OverrideCost(StoryTier tier, EngineTuning tuning)
        {
            // AGORA-SEAM(wave-2/2d)
            return 0;
        }

        /// <summary>
        /// Whether the balance covers an override of this tier.
        /// </summary>
        /// <remarks>
        /// A refusal must reach the player as a legible reason, never a silent no-op. The balance is
        /// allowed to be negative already — debt is a state, not a bar to further play — so this asks
        /// whether the spend is affordable, not whether the player is solvent.
        /// </remarks>
        public static bool CanAfford(PoliticalPowerState power, StoryTier tier, EngineTuning tuning)
        {
            // AGORA-SEAM(wave-2/2d)
            return false;
        }

        /// <summary>
        /// The signed power movement for one resolved slot.
        /// </summary>
        /// <param name="manualDeclared">
        /// True when the player declared this outcome themselves. <b>A manual award is capped at the
        /// MINOR rate whatever the tier</b> — otherwise a one-word justification on a mandatory event
        /// mints the mandatory award, which is the one real exploit surface in the whole design.
        /// </param>
        /// <remarks>
        /// A not-met slot loses <c>power.failureLossRatio</c> of the tier's gain — below 1 on
        /// purpose, so failing costs less than succeeding pays and the economy rewards engagement.
        /// An <see cref="SlotOutcome.Unmeasurable"/> slot moves the balance by <b>zero</b>.
        /// </remarks>
        public static int AwardFor(SlotOutcome outcome, StoryTier tier, bool manualDeclared,
                                   EngineTuning tuning)
        {
            // AGORA-SEAM(wave-2/2d)
            return 0;
        }

        /// <summary>True when the balance is negative and the debt penalty applies.</summary>
        public static bool IsInDebt(PoliticalPowerState power)
        {
            // AGORA-SEAM(wave-2/2d)
            return false;
        }
    }
}
