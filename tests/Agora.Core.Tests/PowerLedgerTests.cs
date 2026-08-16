using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Stories;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The political-power state transition: the once-per-month accrual guard, the ledger, spends,
    /// and the debt penalty.
    ///
    /// <para>
    /// Every expected figure is taken from <see cref="PoliticalPower"/> or from
    /// <see cref="EngineTuning.Default"/> rather than memorised, because the shapes worth guarding
    /// here are "a month never pays twice", "every movement is explained by an entry" and "the ledger
    /// applies exactly what the arithmetic says" — all of which survive a balance pass that a literal
    /// 5 or 50 would not.
    /// </para>
    /// </summary>
    public class PowerLedgerTests
    {
        private static readonly EngineTuning Tuning = EngineTuning.Default;
        private static readonly PowerTuning Power = EngineTuning.Default.Power;
        private static readonly StoriesTuning Stories = EngineTuning.Default.Stories;

        /// <summary>The master switch off — the control for the whole surface.</summary>
        private static readonly EngineTuning PowerOff =
            EngineTuning.FromJson("{\"power\":{\"enabled\":false}}");

        private static readonly SimDate March = new SimDate(2000, 3, 1);
        private static readonly SimDate April = new SimDate(2000, 4, 1);

        /// <summary>A share that earns something under shipped tuning.</summary>
        private const double GoverningShare = 1.0;

        // --- fixtures -----------------------------------------------------------------------------

        /// <summary>The severity an event needs to project onto <paramref name="tier"/>.</summary>
        private static int SeverityFor(StoryTier tier)
        {
            switch (tier)
            {
                case StoryTier.Mandatory: return Stories.MandatorySeverityThreshold;
                case StoryTier.Major: return Stories.MajorSeverityThreshold;
                default: return 1;
            }
        }

        private static CivicEvent Event(string id, StoryTier tier) =>
            new CivicEvent { Id = id, Severity = SeverityFor(tier) };

        private static StorySlot Slot(string eventId, SlotOutcome outcome,
                                      SlotResponse response = SlotResponse.Goal,
                                      bool manualDeclared = false) =>
            new StorySlot
            {
                EventId = eventId,
                Response = response,
                SlotOutcome = outcome,
                ManualDeclared = manualDeclared
            };

        private static Story StoryOf(string id, params StorySlot[] slots) =>
            new Story { Id = id, Slots = new List<StorySlot>(slots) };

        private static List<CivicEvent> Catalog(params CivicEvent[] events) =>
            new List<CivicEvent>(events);

        private static PoliticalPowerState Balance(int balance) =>
            new PoliticalPowerState { Balance = balance };

        // --- the accrual guard --------------------------------------------------------------------

        /// <summary>
        /// <b>The save-scum case, stated as a sequence of calls.</b> Re-entering a month that has
        /// already paid — which is exactly what loading a save across a month boundary does — must
        /// pay nothing the second time and nothing the tenth.
        /// </summary>
        [Fact]
        public void Accrue_PaysOnceForAMonthHoweverManyTimesTheMonthIsReEntered()
        {
            int expected = PoliticalPower.AccrualFor(GoverningShare, Tuning);
            Assert.True(expected > 0, "Shipped tuning must pay something to a fully backed government, "
                                      + "or this test cannot tell a guard from an empty accrual.");

            PoliticalPowerState first = PowerLedger.Accrue(new PoliticalPowerState(), GoverningShare, March, Tuning);
            Assert.Equal(expected, first.Balance);

            PoliticalPowerState replayed = first;
            for (int i = 0; i < 10; i++)
                replayed = PowerLedger.Accrue(replayed, GoverningShare, March, Tuning);

            Assert.Equal(expected, replayed.Balance);
            Assert.Single(replayed.Ledger);
        }

        /// <summary>The guard is per month, not forever: the next month pays again.</summary>
        [Fact]
        public void Accrue_PaysAgainInTheFollowingMonth()
        {
            PoliticalPowerState march = PowerLedger.Accrue(new PoliticalPowerState(), GoverningShare, March, Tuning);
            PoliticalPowerState april = PowerLedger.Accrue(march, GoverningShare, April, Tuning);

            Assert.Equal(march.Balance * 2, april.Balance);
            Assert.Equal(2, april.Ledger.Count);
        }

        /// <summary>
        /// <b>A rollback resumes; it does not freeze.</b> A city save rolled back re-enters an
        /// <i>earlier</i> month than the one already stamped, and that month must pay and re-stamp —
        /// which is why the guard is <c>==</c> and not <c>&gt;=</c>. Under <c>&gt;=</c> the currency
        /// would be frozen from the rollback point up to the old watermark while failures kept
        /// charging, and refusing before the stamp means it would never heal.
        /// </summary>
        [Fact]
        public void Accrue_PaysAgainAfterARollbackToAnEarlierMonth()
        {
            PoliticalPowerState april = PowerLedger.Accrue(new PoliticalPowerState(), GoverningShare, April, Tuning);
            PoliticalPowerState rolledBack = PowerLedger.Accrue(april, GoverningShare, March, Tuning);

            Assert.Equal(april.Balance * 2, rolledBack.Balance);
            Assert.Equal(2, rolledBack.Ledger.Count);

            // Re-stamped to the rolled-back month, so the walk forward from here is an ordinary one.
            Assert.Equal(March.TotalMonths, rolledBack.LastAccrualMonth);
        }

        /// <summary>
        /// The rollback stated as the whole sequence: the currency neither freezes nor farms. Sixty
        /// months are replayed after a rewind and each pays exactly once, as it did the first time.
        /// </summary>
        [Fact]
        public void Accrue_NeitherFreezesNorFarmsAcrossARewindAndReplay()
        {
            int monthly = PoliticalPower.AccrualFor(GoverningShare, Tuning);
            Assert.True(monthly > 0, "Shipped tuning must pay something to a fully backed government, "
                                     + "or this test cannot tell a freeze from an empty accrual.");

            // Run twelve months forward.
            PoliticalPowerState state = new PoliticalPowerState();
            SimDate month = March;
            for (int i = 0; i < 12; i++)
            {
                state = PowerLedger.Accrue(state, GoverningShare, month, Tuning);
                month = month.AddMonths(1);
            }
            Assert.Equal(monthly * 12, state.Balance);

            // Roll back six months and replay them, twice per month for good measure — the re-entries
            // must be refused, and the rollback itself must not be.
            SimDate rolledBackTo = March.AddMonths(6);
            PoliticalPowerState replayed = state;
            SimDate replayMonth = rolledBackTo;
            for (int i = 0; i < 6; i++)
            {
                replayed = PowerLedger.Accrue(replayed, GoverningShare, replayMonth, Tuning);
                replayed = PowerLedger.Accrue(replayed, GoverningShare, replayMonth, Tuning);
                replayMonth = replayMonth.AddMonths(1);
            }

            Assert.Equal(monthly * 18, replayed.Balance);
        }

        /// <summary>
        /// An interregnum earns nothing, and the month is still stamped: the answer for that month is
        /// zero, and re-asking it later must not find a share that has since changed.
        /// </summary>
        [Fact]
        public void Accrue_StampsTheMonthEvenWhenNoGovernmentEarnsAnything()
        {
            PoliticalPowerState state = PowerLedger.Accrue(new PoliticalPowerState(), 0.0, March, Tuning);

            Assert.Equal(0, state.Balance);
            Assert.Equal(March.TotalMonths, state.LastAccrualMonth);
            Assert.Empty(state.Ledger); // Zero is not a movement, so nothing is recorded.

            PoliticalPowerState later = PowerLedger.Accrue(state, GoverningShare, March, Tuning);
            Assert.Equal(0, later.Balance);
        }

        /// <summary>The ledger applies exactly what <see cref="PoliticalPower.AccrualFor"/> says.</summary>
        [Theory]
        [InlineData(0.15)]
        [InlineData(0.42)]
        [InlineData(0.80)]
        [InlineData(1.00)]
        public void Accrue_MovesTheBalanceByTheArithmeticAndNothingElse(double share)
        {
            PoliticalPowerState state = PowerLedger.Accrue(Balance(7), share, March, Tuning);

            Assert.Equal(7 + PoliticalPower.AccrualFor(share, Tuning), state.Balance);
        }

        /// <summary>An accrual entry is dated, signed and reasoned.</summary>
        [Fact]
        public void Accrue_RecordsTheMovementAsAnAccrual()
        {
            PoliticalPowerState state = PowerLedger.Accrue(new PoliticalPowerState(), GoverningShare, March, Tuning);

            PowerLedgerEntry entry = Assert.Single(state.Ledger);
            Assert.Equal(PowerLedgerReason.Accrual, entry.Reason);
            Assert.Equal(March.TotalMonths, entry.Month);
            Assert.Equal(state.Balance, entry.Delta);
            Assert.Equal("", entry.StoryId);
            Assert.Equal("", entry.EventId);
        }

        // --- awards -------------------------------------------------------------------------------

        /// <summary>
        /// <b>Every scored slot is a movement, and every movement is an entry with its own place in
        /// the month's order.</b> Two entries sharing a sequence would leave the ledger's documented
        /// sort key unable to order them.
        /// </summary>
        [Fact]
        public void AwardForStory_WritesOneEntryPerScoredSlotWithDistinctSequences()
        {
            Story story = StoryOf("story-1",
                                  Slot("e-major", SlotOutcome.Met),
                                  Slot("e-minor-a", SlotOutcome.NotMet),
                                  Slot("e-minor-b", SlotOutcome.Met));

            List<CivicEvent> catalog = Catalog(Event("e-major", StoryTier.Major),
                                               Event("e-minor-a", StoryTier.Minor),
                                               Event("e-minor-b", StoryTier.Minor));

            PoliticalPowerState state = PowerLedger.AwardForStory(new PoliticalPowerState(), story,
                                                                  catalog, March, Tuning);

            Assert.Equal(3, state.Ledger.Count);

            var seen = new List<int>();
            for (int i = 0; i < state.Ledger.Count; i++)
            {
                PowerLedgerEntry entry = state.Ledger[i];
                Assert.Equal(March.TotalMonths, entry.Month);
                Assert.Equal("story-1", entry.StoryId);
                Assert.NotEqual("", entry.EventId);
                Assert.DoesNotContain(entry.Sequence, seen);
                seen.Add(entry.Sequence);
            }
        }

        /// <summary>
        /// The whole story's movement is the sum of <see cref="PoliticalPower.AwardFor"/> over its
        /// slots — the ledger applies that arithmetic, it does not re-derive it.
        /// </summary>
        [Fact]
        public void AwardForStory_MovesTheBalanceByTheSumOfItsSlots()
        {
            Story story = StoryOf("story-2",
                                  Slot("e-mand", SlotOutcome.Met),
                                  Slot("e-major", SlotOutcome.NotMet));

            List<CivicEvent> catalog = Catalog(Event("e-mand", StoryTier.Mandatory),
                                               Event("e-major", StoryTier.Major));

            int expected = PoliticalPower.AwardFor(SlotOutcome.Met, StoryTier.Mandatory, false, Tuning)
                         + PoliticalPower.AwardFor(SlotOutcome.NotMet, StoryTier.Major, false, Tuning);

            PoliticalPowerState state = PowerLedger.AwardForStory(Balance(100), story, catalog, March, Tuning);

            Assert.Equal(100 + expected, state.Balance);
        }

        /// <summary>
        /// A declared manual success is paid at the minor rate whatever its tier, and is recorded
        /// under its own reason so the cap is auditable rather than hidden inside a number.
        /// </summary>
        [Fact]
        public void AwardForStory_PaysADeclaredManualSuccessAtTheMinorRateAndSaysSo()
        {
            Story story = StoryOf("story-3",
                                  Slot("e-mand", SlotOutcome.Met, SlotResponse.Manual, manualDeclared: true));
            List<CivicEvent> catalog = Catalog(Event("e-mand", StoryTier.Mandatory));

            PoliticalPowerState state = PowerLedger.AwardForStory(new PoliticalPowerState(), story,
                                                                  catalog, March, Tuning);

            PowerLedgerEntry entry = Assert.Single(state.Ledger);
            Assert.Equal(PowerLedgerReason.ManualAward, entry.Reason);
            Assert.Equal(PoliticalPower.AwardFor(SlotOutcome.Met, StoryTier.Minor, false, Tuning), entry.Delta);
        }

        /// <summary>
        /// <b>The cap is one-sided.</b> A self-declared failure is charged at the event's real tier,
        /// so honest self-reporting costs exactly what silence costs and never less — the discount the
        /// symmetric version would hand out.
        /// </summary>
        [Fact]
        public void AwardForStory_ChargesASelfDeclaredFailureAtTheEventsRealTier()
        {
            Story declared = StoryOf("story-4a",
                                     Slot("e-mand", SlotOutcome.NotMet, SlotResponse.Manual, manualDeclared: true));
            Story ignored = StoryOf("story-4b",
                                    Slot("e-mand", SlotOutcome.NotMet, SlotResponse.Ignore));
            List<CivicEvent> catalog = Catalog(Event("e-mand", StoryTier.Mandatory));

            int declaredDelta = PowerLedger.AwardForStory(new PoliticalPowerState(), declared, catalog, March, Tuning).Balance;
            int ignoredDelta = PowerLedger.AwardForStory(new PoliticalPowerState(), ignored, catalog, March, Tuning).Balance;

            Assert.True(declaredDelta < 0, "A mandatory failure must cost something.");
            Assert.Equal(ignoredDelta, declaredDelta);
            Assert.Equal(PowerLedgerReason.FailurePenalty,
                         Assert.Single(PowerLedger.AwardForStory(new PoliticalPowerState(), declared, catalog,
                                                                 March, Tuning).Ledger).Reason);
        }

        /// <summary>
        /// A sensor gap costs nothing, so it moves nothing and explains nothing — no entry either.
        /// </summary>
        [Fact]
        public void AwardForStory_MovesNothingForAnUnmeasurableOrPendingSlot()
        {
            Story story = StoryOf("story-5",
                                  Slot("e-a", SlotOutcome.Unmeasurable),
                                  Slot("e-b", SlotOutcome.Pending));
            List<CivicEvent> catalog = Catalog(Event("e-a", StoryTier.Mandatory), Event("e-b", StoryTier.Major));

            PoliticalPowerState state = PowerLedger.AwardForStory(Balance(40), story, catalog, March, Tuning);

            Assert.Equal(40, state.Balance);
            Assert.Empty(state.Ledger);
        }

        /// <summary>
        /// An event the catalog no longer carries pays nothing: guessing a tier for it would charge
        /// the player for content removed from under them.
        /// </summary>
        [Fact]
        public void AwardForStory_SkipsASlotWhoseEventTheCatalogNoLongerCarries()
        {
            Story story = StoryOf("story-6",
                                  Slot("e-gone", SlotOutcome.NotMet),
                                  Slot("e-here", SlotOutcome.Met));
            List<CivicEvent> catalog = Catalog(Event("e-here", StoryTier.Minor));

            PoliticalPowerState state = PowerLedger.AwardForStory(new PoliticalPowerState(), story,
                                                                  catalog, March, Tuning);

            PowerLedgerEntry entry = Assert.Single(state.Ledger);
            Assert.Equal("e-here", entry.EventId);
        }

        /// <summary>
        /// <b>Debt arrives through penalties the player did not choose.</b> A failure is never refused
        /// for taking the balance below zero.
        /// </summary>
        [Fact]
        public void AwardForStory_LetsAPenaltyDriveTheBalanceNegative()
        {
            Story story = StoryOf("story-7", Slot("e-mand", SlotOutcome.NotMet));
            List<CivicEvent> catalog = Catalog(Event("e-mand", StoryTier.Mandatory));

            PoliticalPowerState state = PowerLedger.AwardForStory(new PoliticalPowerState(), story,
                                                                  catalog, March, Tuning);

            Assert.True(state.Balance < 0);
            Assert.True(PoliticalPower.IsInDebt(state));
        }

        // --- spends -------------------------------------------------------------------------------

        /// <summary>A granted spend debits the quoted cost and says which slot bought it.</summary>
        [Fact]
        public void TrySpend_DebitsTheQuotedCostAndRecordsTheSlot()
        {
            int cost = PoliticalPower.OverrideCost(StoryTier.Major, Tuning);

            PoliticalPowerState state;
            Assert.True(PowerLedger.TrySpend(Balance(cost + 5), "story-8", "e-x",
                                             StoryTier.Major, March, Tuning, out state));

            Assert.Equal(5, state.Balance);

            PowerLedgerEntry entry = Assert.Single(state.Ledger);
            Assert.Equal(PowerLedgerReason.OverrideSpend, entry.Reason);
            Assert.Equal(-cost, entry.Delta);
            Assert.Equal("story-8", entry.StoryId);
            Assert.Equal("e-x", entry.EventId);
        }

        /// <summary>
        /// <b>The answer and the affordability check can never disagree.</b>
        /// <see cref="PoliticalPower.CanAfford"/> is the only solvency rule there is: whatever it
        /// permits is granted, whatever it refuses is not — including from a negative balance, where
        /// it still permits anything the balance covers. <c>next</c> is a usable state either way.
        /// </summary>
        [Fact]
        public void TrySpend_SucceedsExactlyWhenCanAffordPermitsIt()
        {
            int cost = PoliticalPower.OverrideCost(StoryTier.Minor, Tuning);

            for (int balance = -cost * 2; balance <= cost * 2; balance += 1)
            {
                PoliticalPowerState prior = Balance(balance);

                PoliticalPowerState next;
                bool granted = PowerLedger.TrySpend(prior, "story-9", "e-y",
                                                    StoryTier.Minor, March, Tuning, out next);

                Assert.Equal(PoliticalPower.CanAfford(prior, StoryTier.Minor, Tuning), granted);
                Assert.NotNull(next);
                Assert.Equal(granted ? balance - cost : balance, next.Balance);
            }
        }

        /// <summary>
        /// <b>The case the bool exists for.</b> A free override is granted and moves nothing, which is
        /// indistinguishable from a refusal by the balance alone — a caller comparing balances would
        /// tell the player they could not afford something they were just given.
        /// </summary>
        [Fact]
        public void TrySpend_ReportsAFreeOverrideAsGrantedRatherThanAsRefused()
        {
            EngineTuning free = EngineTuning.FromJson("{\"power\":{\"overrideCost\":{\"minor\":0}}}");
            Assert.Equal(0, PoliticalPower.OverrideCost(StoryTier.Minor, free));

            PoliticalPowerState next;
            bool granted = PowerLedger.TrySpend(Balance(0), "story-9b", "e-free",
                                                StoryTier.Minor, March, free, out next);

            Assert.True(granted, "A free override is affordable, so it is granted, not refused.");
            Assert.Equal(0, next.Balance);
            Assert.Empty(next.Ledger); // Nothing moved, so there is nothing to explain.
        }

        /// <summary>A refusal still hands back a state the caller can keep using.</summary>
        [Fact]
        public void TrySpend_ReturnsTheUntouchedPriorOnARefusal()
        {
            int cost = PoliticalPower.OverrideCost(StoryTier.Mandatory, Tuning);

            PoliticalPowerState next;
            Assert.False(PowerLedger.TrySpend(Balance(cost - 1), "story-9c", "e-z",
                                              StoryTier.Mandatory, March, Tuning, out next));

            Assert.NotNull(next);
            Assert.Equal(cost - 1, next.Balance);
            Assert.Empty(next.Ledger);
        }

        // --- the master switch --------------------------------------------------------------------

        /// <summary>
        /// With <c>power.enabled</c> off the whole economy is inert: nothing accrues — not even the
        /// month stamp — nothing is awarded, nothing is bought and debt costs nothing.
        /// </summary>
        [Fact]
        public void EverySurfaceIsInertWhenThePacketIsDisabled()
        {
            Story story = StoryOf("story-10", Slot("e-mand", SlotOutcome.Met));
            List<CivicEvent> catalog = Catalog(Event("e-mand", StoryTier.Mandatory));

            PoliticalPowerState accrued = PowerLedger.Accrue(Balance(-20), GoverningShare, March, PowerOff);
            Assert.Equal(-20, accrued.Balance);
            Assert.Equal(-1, accrued.LastAccrualMonth);
            Assert.Empty(accrued.Ledger);

            PoliticalPowerState awarded = PowerLedger.AwardForStory(Balance(-20), story, catalog, March, PowerOff);
            Assert.Equal(-20, awarded.Balance);
            Assert.Empty(awarded.Ledger);

            PoliticalPowerState spent;
            Assert.False(PowerLedger.TrySpend(Balance(10000), "story-10", "e-mand",
                                              StoryTier.Minor, March, PowerOff, out spent));
            Assert.Equal(10000, spent.Balance);
            Assert.Empty(spent.Ledger);

            EffectRequest request;
            Assert.False(PowerLedger.TryDebtPenalty(Balance(-20), PowerOff, out request));
        }

        // --- no mutation --------------------------------------------------------------------------

        /// <summary>
        /// <b>Nothing mutates its argument.</b> A second aliasing writer inside the tick would let a
        /// speculative advance move the caller's own balance and append to the caller's own ledger.
        /// </summary>
        [Fact]
        public void NoEntranceMutatesTheStateItIsHanded()
        {
            Story story = StoryOf("story-11", Slot("e-mand", SlotOutcome.Met));
            List<CivicEvent> catalog = Catalog(Event("e-mand", StoryTier.Mandatory));

            var prior = new PoliticalPowerState { Balance = 500 };
            List<PowerLedgerEntry> priorLedger = prior.Ledger;

            PoliticalPowerState accrued = PowerLedger.Accrue(prior, GoverningShare, March, Tuning);
            PoliticalPowerState awarded = PowerLedger.AwardForStory(prior, story, catalog, March, Tuning);

            PoliticalPowerState spent;
            Assert.True(PowerLedger.TrySpend(prior, "story-11", "e-mand",
                                             StoryTier.Minor, March, Tuning, out spent));

            Assert.Equal(500, prior.Balance);
            Assert.Equal(-1, prior.LastAccrualMonth);
            Assert.Empty(prior.Ledger);
            Assert.Same(priorLedger, prior.Ledger);

            foreach (PoliticalPowerState result in new[] { accrued, awarded, spent })
            {
                Assert.NotSame(prior, result);
                Assert.NotSame(prior.Ledger, result.Ledger);
                Assert.NotEqual(500, result.Balance);
            }
        }

        /// <summary>A null prior is a save written before the power block existed, not a defect.</summary>
        [Fact]
        public void ANullPriorStartsAnEmptyBalanceRatherThanThrowing()
        {
            PoliticalPowerState state = PowerLedger.Accrue(null!, GoverningShare, March, Tuning);

            Assert.Equal(PoliticalPower.AccrualFor(GoverningShare, Tuning), state.Balance);
        }

        // --- the ledger ---------------------------------------------------------------------------

        /// <summary>
        /// The ledger is bounded by <c>power.ledgerRetention</c>, and the oldest go first so the
        /// newest movement is always still explicable.
        /// </summary>
        [Fact]
        public void TheLedgerKeepsTheNewestEntriesUpToTheRetentionDial()
        {
            EngineTuning shortLedger = EngineTuning.FromJson("{\"power\":{\"ledgerRetention\":3}}");
            int retention = shortLedger.Power.LedgerRetention;

            PoliticalPowerState state = Balance(10000);
            SimDate month = March;
            for (int i = 0; i < retention + 4; i++)
            {
                state = PowerLedger.Accrue(state, GoverningShare, month, shortLedger);
                month = month.AddMonths(1);
            }

            Assert.Equal(retention, state.Ledger.Count);
            Assert.Equal(month.AddMonths(-1).TotalMonths, state.Ledger[state.Ledger.Count - 1].Month);
        }

        /// <summary>
        /// <b>The lifetime totals reconcile with the balance.</b> They are the only reading of the
        /// balance's history the UI gets that is not the bounded ledger, so a penalty missing from
        /// both would make them unable to explain the number they sit beside.
        /// </summary>
        [Fact]
        public void LifetimeTotalsReconcileWithTheBalanceAcrossAMixedRun()
        {
            Story story = StoryOf("story-12",
                                  Slot("e-mand", SlotOutcome.Met),
                                  Slot("e-major", SlotOutcome.NotMet));
            List<CivicEvent> catalog = Catalog(Event("e-mand", StoryTier.Mandatory),
                                               Event("e-major", StoryTier.Major));

            PoliticalPowerState state = PowerLedger.Accrue(new PoliticalPowerState(), GoverningShare, March, Tuning);
            state = PowerLedger.AwardForStory(state, story, catalog, March, Tuning);
            PowerLedger.TrySpend(state, "story-12", "e-major", StoryTier.Minor, March, Tuning, out state);

            Assert.Equal(state.Balance, state.LifetimeEarned - state.LifetimeSpent);
        }

        /// <summary>Movements made in one month are ordered against each other, not all at zero.</summary>
        [Fact]
        public void SequenceKeepsTwoMovementsInOneMonthApart()
        {
            Story story = StoryOf("story-13", Slot("e-minor", SlotOutcome.Met));
            List<CivicEvent> catalog = Catalog(Event("e-minor", StoryTier.Minor));

            PoliticalPowerState state = PowerLedger.Accrue(new PoliticalPowerState(), GoverningShare, March, Tuning);
            state = PowerLedger.AwardForStory(state, story, catalog, March, Tuning);

            Assert.Equal(2, state.Ledger.Count);
            Assert.NotEqual(state.Ledger[0].Sequence, state.Ledger[1].Sequence);
            Assert.True(state.Ledger[1].Sequence > state.Ledger[0].Sequence,
                        "A later movement in the same month must sort after an earlier one.");
        }

        // --- the debt penalty ---------------------------------------------------------------------

        /// <summary>
        /// Debt buys the shipped palette entry, city-scoped, at the tuned magnitude and for the one
        /// month it is re-asked for. It is not clamped here: the palette's own caps apply on the way
        /// through the resolver.
        /// </summary>
        [Fact]
        public void TryDebtPenalty_RequestsTheShippedPaletteEntryWhileTheBalanceIsNegative()
        {
            EffectRequest request;
            Assert.True(PowerLedger.TryDebtPenalty(Balance(-1), Tuning, out request));

            Assert.Equal(EffectScope.City, request.Scope);
            Assert.Null(request.DistrictId);
            Assert.Equal(Power.DebtRevenuePenalty, request.Magnitude);
            Assert.Equal(1, request.DurationMonths);

            // The id must name a real city-scoped palette entry, or the resolver would drop it.
            EffectCap cap;
            Assert.True(Tuning.Effects.TryGetEffect(request.EffectId, out cap));
            Assert.Equal(EffectScope.City, cap.Scope);
        }

        /// <summary>A solvent city owes nothing, and a zero balance is solvent.</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(9999)]
        public void TryDebtPenalty_IsFalseWhenTheBalanceIsNotNegative(int balance)
        {
            EffectRequest request;
            Assert.False(PowerLedger.TryDebtPenalty(Balance(balance), Tuning, out request));
        }

        /// <summary>A save with no power block at all owes nothing either.</summary>
        [Fact]
        public void TryDebtPenalty_IsFalseForANullState()
        {
            EffectRequest request;
            Assert.False(PowerLedger.TryDebtPenalty(null!, Tuning, out request));
        }

        // --- determinism --------------------------------------------------------------------------

        /// <summary>
        /// The same calls from the same state twice produce the same ledger, entry for entry. The
        /// ledger is persisted state, so a difference here is a desync rather than a cosmetic one.
        /// </summary>
        [Fact]
        public void TheSameSequenceOfCallsTwiceProducesTheSameLedger()
        {
            Story story = StoryOf("story-14",
                                  Slot("e-a", SlotOutcome.Met),
                                  Slot("e-b", SlotOutcome.NotMet),
                                  Slot("e-c", SlotOutcome.Met, SlotResponse.Manual, manualDeclared: true));
            List<CivicEvent> catalog = Catalog(Event("e-a", StoryTier.Major),
                                               Event("e-b", StoryTier.Minor),
                                               Event("e-c", StoryTier.Mandatory));

            string first = Serialize(Run(story, catalog));
            string second = Serialize(Run(story, catalog));

            Assert.Equal(first, second);
        }

        private static PoliticalPowerState Run(Story story, List<CivicEvent> catalog)
        {
            PoliticalPowerState state = PowerLedger.Accrue(new PoliticalPowerState(), 0.55, March, Tuning);
            state = PowerLedger.AwardForStory(state, story, catalog, March, Tuning);
            PowerLedger.TrySpend(state, story.Id, "e-a", StoryTier.Minor, April, Tuning, out state);
            return PowerLedger.Accrue(state, 0.55, April, Tuning);
        }

        /// <summary>Field-by-field, so a field a hand-written assertion forgets still shows up.</summary>
        private static string Serialize(PoliticalPowerState state)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(state.Balance).Append('|')
              .Append(state.LifetimeEarned).Append('|')
              .Append(state.LifetimeSpent).Append('|')
              .Append(state.LastAccrualMonth).Append('\n');

            for (int i = 0; i < state.Ledger.Count; i++)
            {
                PowerLedgerEntry e = state.Ledger[i];
                sb.Append(e.Month).Append('|').Append(e.Sequence).Append('|')
                  .Append((int)e.Reason).Append('|').Append(e.Delta).Append('|')
                  .Append(e.StoryId).Append('|').Append(e.EventId).Append('\n');
            }
            return sb.ToString();
        }
    }
}
