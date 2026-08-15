using Agora.Core.Stories;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The political-power arithmetic: accrual, award, penalty, affordability and debt.
    ///
    /// <para>
    /// Pure arithmetic, and tested as such — the <i>consequence</i> of debt is a wave-4 effect and
    /// nothing here applies anything. Every expected value is computed from
    /// <see cref="EngineTuning.Default"/>, never memorised: the shapes these tests guard are "a manual
    /// award equals the minor award" and "unmeasurable moves nothing", both of which survive a balance
    /// pass that a literal 10 or 50 would not.
    /// </para>
    /// </summary>
    public class PoliticalPowerTests
    {
        private static readonly EngineTuning Tuning = EngineTuning.Default;
        private static readonly PowerTuning Power = EngineTuning.Default.Power;

        private static int Gain(StoryTier tier)
        {
            switch (tier)
            {
                case StoryTier.Mandatory: return Power.SuccessGain.Mandatory;
                case StoryTier.Major: return Power.SuccessGain.Major;
                default: return Power.SuccessGain.Minor;
            }
        }

        private static int Cost(StoryTier tier)
        {
            switch (tier)
            {
                case StoryTier.Mandatory: return Power.OverrideCost.Mandatory;
                case StoryTier.Major: return Power.OverrideCost.Major;
                default: return Power.OverrideCost.Minor;
            }
        }

        private static PoliticalPowerState Balance(int balance) =>
            new PoliticalPowerState { Balance = balance };

        // --- accrual ------------------------------------------------------------------------------

        /// <summary>
        /// <b>No government yields nothing, never a negative.</b> An interregnum should stop the
        /// player earning; it must not start charging them.
        /// </summary>
        [Fact]
        public void AccrualFor_IsZeroWithNoGovernment()
        {
            Assert.Equal(0, PoliticalPower.AccrualFor(0.0, Tuning));
        }

        /// <summary>
        /// A share outside 0–1 cannot arise from the engine but can arrive from a hand-edited sidecar,
        /// and neither end may produce a negative accrual or exceed the cap.
        /// </summary>
        [Theory]
        [InlineData(-1.0)]
        [InlineData(-0.25)]
        [InlineData(1.5)]
        [InlineData(99.0)]
        public void AccrualFor_ClampsAShareOutsideTheUnitRange(double share)
        {
            int accrual = PoliticalPower.AccrualFor(share, Tuning);

            Assert.True(accrual >= 0, "Accrual of " + accrual + " at vote share " + share + " is negative.");
            Assert.True(accrual <= Power.MaxMonthlyGain,
                        "Accrual of " + accrual + " at vote share " + share + " exceeds maxMonthlyGain.");
        }

        /// <summary>The ceiling holds everywhere in the range, and nothing in it goes below zero.</summary>
        [Fact]
        public void AccrualFor_StaysWithinZeroAndTheMonthlyCapAcrossTheWholeRange()
        {
            for (int step = 0; step <= 100; step++)
            {
                double share = step / 100.0;
                int accrual = PoliticalPower.AccrualFor(share, Tuning);

                Assert.True(accrual >= 0, "Accrual went negative at vote share " + share + ".");
                Assert.True(accrual <= Power.MaxMonthlyGain,
                            "Accrual of " + accrual + " at vote share " + share + " exceeds maxMonthlyGain.");
            }
        }

        /// <summary>
        /// A better-supported government never earns less than a worse-supported one. The shape the
        /// curve exponent is allowed to change is how fast, never which direction.
        /// </summary>
        [Fact]
        public void AccrualFor_NeverFallsAsSupportRises()
        {
            for (int step = 1; step <= 100; step++)
            {
                int previous = PoliticalPower.AccrualFor((step - 1) / 100.0, Tuning);
                int current = PoliticalPower.AccrualFor(step / 100.0, Tuning);

                Assert.True(current >= previous,
                    "Accrual fell from " + previous + " to " + current + " as vote share rose to " +
                    (step / 100.0) + ".");
            }
        }

        /// <summary>Total support earns the cap, whatever the curve exponent does in between.</summary>
        [Fact]
        public void AccrualFor_AtTotalSupportIsTheMonthlyCap()
        {
            Assert.Equal(Power.MaxMonthlyGain, PoliticalPower.AccrualFor(1.0, Tuning));
        }

        // --- override cost and affordability ------------------------------------------------------

        [Theory]
        [InlineData(StoryTier.Minor)]
        [InlineData(StoryTier.Major)]
        [InlineData(StoryTier.Mandatory)]
        public void OverrideCost_ReadsTheTierSchedule(StoryTier tier)
        {
            Assert.Equal(Cost(tier), PoliticalPower.OverrideCost(tier, Tuning));
        }

        [Theory]
        [InlineData(StoryTier.Minor)]
        [InlineData(StoryTier.Major)]
        [InlineData(StoryTier.Mandatory)]
        public void CanAfford_IsTrueExactlyAtTheCostAndFalseOneBelow(StoryTier tier)
        {
            int cost = Cost(tier);

            Assert.True(PoliticalPower.CanAfford(Balance(cost), tier, Tuning));
            Assert.True(PoliticalPower.CanAfford(Balance(cost + 1), tier, Tuning));
            Assert.False(PoliticalPower.CanAfford(Balance(cost - 1), tier, Tuning));
        }

        /// <summary>
        /// <b>Debt is a state, not a bar to play</b> — so this has to answer from an already-negative
        /// balance rather than refuse to look at one. It asks whether the spend is affordable, which
        /// on a negative balance it is not, and it does so without throwing.
        /// </summary>
        [Theory]
        [InlineData(StoryTier.Minor)]
        [InlineData(StoryTier.Major)]
        [InlineData(StoryTier.Mandatory)]
        public void CanAfford_AnswersFromAnAlreadyNegativeBalance(StoryTier tier)
        {
            Assert.False(PoliticalPower.CanAfford(Balance(-1), tier, Tuning));
            Assert.False(PoliticalPower.CanAfford(Balance(-100000), tier, Tuning));
        }

        [Fact]
        public void CanAfford_RefusesANullStateWithoutThrowing()
        {
            Assert.False(PoliticalPower.CanAfford(null!, StoryTier.Minor, Tuning));
        }

        // --- awards -------------------------------------------------------------------------------

        [Theory]
        [InlineData(StoryTier.Minor)]
        [InlineData(StoryTier.Major)]
        [InlineData(StoryTier.Mandatory)]
        public void AwardFor_AMetSlotPaysTheTiersGain(StoryTier tier)
        {
            Assert.Equal(Gain(tier),
                         PoliticalPower.AwardFor(SlotOutcome.Met, tier, false, Tuning));
        }

        /// <summary>
        /// A not-met slot costs <c>power.failureLossRatio</c> of the tier's gain — below 1 on purpose,
        /// so failing costs less than succeeding pays and the economy rewards engagement. Asserted as
        /// the relationship rather than as the arithmetic, because the rounding is lane 2d's to choose.
        /// </summary>
        [Theory]
        [InlineData(StoryTier.Minor)]
        [InlineData(StoryTier.Major)]
        [InlineData(StoryTier.Mandatory)]
        public void AwardFor_ANotMetSlotCostsLessThanAMetOnePays(StoryTier tier)
        {
            Assert.True(Power.FailureLossRatio < 1.0,
                        "This test is meaningless unless failing costs less than succeeding pays.");

            int penalty = PoliticalPower.AwardFor(SlotOutcome.NotMet, tier, false, Tuning);

            Assert.True(penalty < 0, "A not-met slot moved the balance by " + penalty + ", not downward.");
            Assert.True(-penalty < Gain(tier),
                "A not-met " + tier + " slot costs " + (-penalty) + ", which is not less than the " +
                Gain(tier) + " a met one pays.");
        }

        /// <summary>
        /// <b>The one real exploit surface in the whole design.</b> A player who declares their own
        /// success on a mandatory event would otherwise mint the mandatory rate on a one-word
        /// justification, so a manual award is capped at the minor rate whatever the tier — and the
        /// cap has to hold at every tier, which is why this is a theory rather than one mandatory case.
        /// </summary>
        [Theory]
        [InlineData(StoryTier.Minor)]
        [InlineData(StoryTier.Major)]
        [InlineData(StoryTier.Mandatory)]
        public void AwardFor_AManualDeclaredSuccessIsCappedAtTheMinorRate(StoryTier tier)
        {
            int minor = PoliticalPower.AwardFor(SlotOutcome.Met, StoryTier.Minor, false, Tuning);

            Assert.Equal(minor, PoliticalPower.AwardFor(SlotOutcome.Met, tier, true, Tuning));
        }

        /// <summary>
        /// <b>The cap is one-sided: it applies to the award and never to the penalty.</b> A
        /// self-declared failure is charged at the event's real tier, exactly as <c>Ignore</c> would be.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Capping both sides looks symmetrical and is a trap. Under shipped tuning it would make a
        /// truthfully self-declared mandatory failure cost 5 where <c>Ignore</c> costs 25 — so a player
        /// who simply preferred the Manual button would take an 80% discount on every mandatory failure
        /// in the game, with no lying required, and the tier ladder would survive on the award side
        /// alone.
        /// </para>
        /// <para>
        /// Charging the real tier keeps honest self-reporting exactly as expensive as <c>Ignore</c> and
        /// never worse, so honesty is never punished relative to silence. That a <i>false</i>
        /// declaration of success still beats an honest failure is not closable in arithmetic; the
        /// design concedes unverifiable declarations.
        /// </para>
        /// </remarks>
        [Theory]
        [InlineData(StoryTier.Minor)]
        [InlineData(StoryTier.Major)]
        [InlineData(StoryTier.Mandatory)]
        public void AwardFor_DoesNotCapTheFailurePenaltyOfADeclaredSlot(StoryTier tier)
        {
            Assert.Equal(PoliticalPower.AwardFor(SlotOutcome.NotMet, tier, false, Tuning),
                         PoliticalPower.AwardFor(SlotOutcome.NotMet, tier, true, Tuning));
        }

        /// <summary>
        /// The consequence that makes the one-sidedness worth having: a declared failure on a big
        /// event costs strictly more than one on a small event. Capped at the minor rate these would
        /// be equal, and the tier ladder would exist only on the way up.
        /// </summary>
        [Fact]
        public void AwardFor_ADeclaredFailureStillClimbsWithTheTier()
        {
            int minor = PoliticalPower.AwardFor(SlotOutcome.NotMet, StoryTier.Minor, true, Tuning);
            int mandatory = PoliticalPower.AwardFor(SlotOutcome.NotMet, StoryTier.Mandatory, true, Tuning);

            Assert.True(mandatory < minor,
                "A declared mandatory failure moves the balance by " + mandatory + " against a minor's " +
                minor + ": the penalty has been capped too, which hands the Manual button a discount " +
                "on every mandatory failure in the game.");
        }

        /// <summary>
        /// Both halves of the ruling in one place, on the tier where they differ most: on a mandatory
        /// event the declared award is the minor rate while the declared penalty is the mandatory one.
        /// </summary>
        [Fact]
        public void AwardFor_CapsTheDeclaredAwardButNotTheDeclaredPenalty()
        {
            Assert.Equal(Gain(StoryTier.Minor),
                         PoliticalPower.AwardFor(SlotOutcome.Met, StoryTier.Mandatory, true, Tuning));

            Assert.Equal(PoliticalPower.AwardFor(SlotOutcome.NotMet, StoryTier.Mandatory, false, Tuning),
                         PoliticalPower.AwardFor(SlotOutcome.NotMet, StoryTier.Mandatory, true, Tuning));
        }

        /// <summary>
        /// The cap is a cap, not a rewrite: it may not turn the minor rate into something larger, and
        /// declaring on a minor event is worth exactly what a minor event is worth.
        /// </summary>
        [Fact]
        public void AwardFor_AManualDeclarationOnAMinorEventChangesNothing()
        {
            Assert.Equal(PoliticalPower.AwardFor(SlotOutcome.Met, StoryTier.Minor, false, Tuning),
                         PoliticalPower.AwardFor(SlotOutcome.Met, StoryTier.Minor, true, Tuning));
        }

        /// <summary>
        /// A manual declaration must never be worth more than the honest route. Stated separately from
        /// the equality above because it is the property that would still hold — and still matter — if
        /// the cap were ever loosened deliberately.
        /// </summary>
        [Theory]
        [InlineData(StoryTier.Minor)]
        [InlineData(StoryTier.Major)]
        [InlineData(StoryTier.Mandatory)]
        public void AwardFor_NeverPaysMoreForADeclarationThanForTheRealThing(StoryTier tier)
        {
            Assert.True(PoliticalPower.AwardFor(SlotOutcome.Met, tier, true, Tuning)
                        <= PoliticalPower.AwardFor(SlotOutcome.Met, tier, false, Tuning));
        }

        /// <summary>
        /// <b>An unmeasurable slot moves the balance by exactly zero</b> — at every tier and whether or
        /// not the player declared it. A sensor gap costs nothing and pays nothing; that is the whole
        /// reason the outcome is a state rather than a failure.
        /// </summary>
        [Theory]
        [InlineData(StoryTier.Minor, true)]
        [InlineData(StoryTier.Minor, false)]
        [InlineData(StoryTier.Major, true)]
        [InlineData(StoryTier.Major, false)]
        [InlineData(StoryTier.Mandatory, true)]
        [InlineData(StoryTier.Mandatory, false)]
        public void AwardFor_AnUnmeasurableSlotMovesNothing(StoryTier tier, bool manualDeclared)
        {
            Assert.Equal(0, PoliticalPower.AwardFor(SlotOutcome.Unmeasurable, tier, manualDeclared, Tuning));
        }

        /// <summary>
        /// A slot still pending has not happened yet, so there is nothing to pay for. Guarded because
        /// a resolution that ran early would otherwise pay out on slots it had not scored.
        /// </summary>
        [Theory]
        [InlineData(StoryTier.Minor)]
        [InlineData(StoryTier.Major)]
        [InlineData(StoryTier.Mandatory)]
        public void AwardFor_APendingSlotMovesNothing(StoryTier tier)
        {
            Assert.Equal(0, PoliticalPower.AwardFor(SlotOutcome.Pending, tier, false, Tuning));
        }

        /// <summary>
        /// A bigger event is worth at least as much as a smaller one. The schedule may be flattened by
        /// a balance pass; it may not be inverted, or a player would be better off ignoring the
        /// mandatory event and taking the minor.
        /// </summary>
        [Fact]
        public void AwardFor_IsNonDecreasingAcrossTheTiers()
        {
            int minor = PoliticalPower.AwardFor(SlotOutcome.Met, StoryTier.Minor, false, Tuning);
            int major = PoliticalPower.AwardFor(SlotOutcome.Met, StoryTier.Major, false, Tuning);
            int mandatory = PoliticalPower.AwardFor(SlotOutcome.Met, StoryTier.Mandatory, false, Tuning);

            Assert.True(major >= minor);
            Assert.True(mandatory >= major);
        }

        // --- debt ---------------------------------------------------------------------------------

        /// <summary>
        /// Negative and only negative. A balance of zero is broke, not indebted, and the wave-4
        /// revenue penalty must not fire on a save that has simply never earned anything.
        /// </summary>
        [Theory]
        [InlineData(-500, true)]
        [InlineData(-1, true)]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(500, false)]
        public void IsInDebt_IsTrueOnlyBelowZero(int balance, bool expected)
        {
            Assert.Equal(expected, PoliticalPower.IsInDebt(Balance(balance)));
        }

        [Fact]
        public void IsInDebt_TreatsANullStateAsSolventWithoutThrowing()
        {
            Assert.False(PoliticalPower.IsInDebt(null!));
        }
    }
}
