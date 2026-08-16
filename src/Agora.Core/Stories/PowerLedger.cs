using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Stories
{
    /// <summary>
    /// Applies <see cref="PoliticalPower"/>'s arithmetic to a balance: accrual, awards, spends and
    /// the consequence of debt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The split with <see cref="PoliticalPower"/> is deliberate and must hold.</b> That class is
    /// pure arithmetic over tuning and holds no state; this one owns the state transition and the
    /// ledger. Do not re-derive a number here that <see cref="PoliticalPower"/> already computes —
    /// <see cref="PoliticalPower.AccrualFor"/>, <see cref="PoliticalPower.AwardFor"/>,
    /// <see cref="PoliticalPower.OverrideCost"/>, <see cref="PoliticalPower.CanAfford"/> and
    /// <see cref="PoliticalPower.IsInDebt"/> are the only sources of those five figures.
    /// </para>
    ///
    /// <para><b>What this file owes, and how each obligation is met.</b></para>
    ///
    /// <para>
    /// <b>1. Accrual is once per month, and the guard is the point.</b>
    /// <c>PoliticalPowerState.LastAccrualMonth</c> exists for it, and
    /// <see cref="PoliticalPower.AccrualFor"/> says out loud that it is a pure function with no state
    /// to check the month against, so the guard lives here and nowhere else. What must be true: a
    /// month that has already paid out never pays out again, however many times it is re-entered.
    /// Without that, saving and reloading across a month boundary farms power without limit — the
    /// exploit wave 0 landed <c>LastCompletedTickMonth</c> to prevent, of which this is the partner.
    /// </para>
    /// <para>
    /// <b>The comparison is <c>==</c>, not <c>&gt;=</c>, and that is the whole of the rewind policy.</b>
    /// <c>&gt;=</c> refuses a month <i>earlier</i> than the one already stamped, which looks like the
    /// stricter reading of the same rule and is not: a city save rolled back from month 400 to month
    /// 340 — ordinary play, because <c>TickPlanner.SnapshotsToPrune</c> keeps only the newest few
    /// snapshots — would then be refused an accrual for 340, 341 and every month up to 400, while the
    /// debit side stayed live. Failures would still charge, the balance would cross zero, and
    /// <see cref="TryDebtPenalty"/> would fire every one of those months: a legitimate rollback turned
    /// into an unrecoverable debt spiral. It would not self-heal either, because refusing before the
    /// stamp never pulls the watermark down.
    /// </para>
    /// <para>
    /// This is also the policy wave 0 actually chose, rather than the half of it that is visible in
    /// the guard alone. <c>AgoraRuntime.ClampWatermarkToClock</c> repairs
    /// <c>LastCompletedTickMonth</c> on a backwards load precisely so the freeze cannot happen, and
    /// says why in its own words: <i>a month run twice is wrong once; a month never run does not come
    /// back</i>. It does not touch <c>LastAccrualMonth</c> and there is no repair path for this field,
    /// so the equality has to carry the policy by itself. <c>==</c> keeps the save-scum property
    /// exactly — month M pays, stamps M, and any re-entry of M is refused — while a genuine rollback
    /// pays 340, stamps 340, and walks forward.
    /// </para>
    /// <para>
    /// <b>2. Every movement writes a <see cref="PowerLedgerEntry"/>.</b> The ledger is the record the
    /// player is shown and the only way a balance can be explained after the fact. Its documented
    /// sort key is <c>(Month, Sequence, EventId ordinal)</c>; <c>Sequence</c> is what keeps two
    /// movements in one month ordered, so it is assigned from the entries already standing in that
    /// month rather than left at zero. A movement of zero is not a movement and writes nothing.
    /// </para>
    /// <para>
    /// <b>3. Debt is a state, not a bar to play.</b> <see cref="PoliticalPower.CanAfford"/> already
    /// encodes that a negative balance still buys anything it covers, and nothing here refuses a
    /// spend on solvency grounds that that function permits.
    /// </para>
    /// <para>
    /// <b>4. The debt penalty is the shipped palette entry, and there is no treasury.</b> Owner
    /// decision, recorded in the wave-3 handoff: it ships as <c>city-service-building-upkeep</c>.
    /// There is no <c>kind: "money"</c> effect, no <c>PlayerMoney</c> debit and no
    /// <c>AgoraTreasurySystem</c> — the plan's "primary route" was never built and must not be
    /// invented here. The request goes out through the ordinary resolver so the palette's own caps
    /// apply; it is not clamped a second time, and <c>power.debtPenaltyCapPerMonth</c> is
    /// deliberately unread because it is denominated in money, which nothing on this route spends.
    /// </para>
    /// <para>
    /// <b>5. Nothing mutates its argument.</b> Every method returns a new
    /// <see cref="PoliticalPowerState"/>. The tick's state is cloned once by
    /// <c>PoliticalEngine.Advance</c> and a second aliasing writer inside it would let a speculative
    /// advance move the caller's own balance.
    /// </para>
    /// <para>
    /// <b>6. The master switch is honoured at every entrance.</b> With <c>power.enabled</c> off the
    /// economy is inert: nothing accrues — not even the month stamp — nothing is awarded, nothing may
    /// be bought and the debt penalty is not requested.
    /// </para>
    /// <para>
    /// <b>The lifetime totals reconcile with the balance.</b> <c>LifetimeEarned</c> takes every gain
    /// and <c>LifetimeSpent</c> the magnitude of every loss, penalties included, so
    /// <c>Balance == LifetimeEarned - LifetimeSpent</c> holds over any run that starts from a fresh
    /// state. Leaving penalties out of both would make the pair unable to explain the balance they
    /// sit beside, which is the one job they have.
    /// </para>
    /// </remarks>
    public static class PowerLedger
    {
        /// <summary>
        /// The palette entry a negative balance costs the city. A content id, not a tuning constant —
        /// the magnitude and the caps both come from tuning.
        /// </summary>
        private const string DebtEffectId = "city-service-building-upkeep";

        /// <summary>What the news feed and the effect ledger call the debt penalty.</summary>
        private const string DebtSourceId = "power-debt";

        private static readonly StorySlot[] NoSlots = new StorySlot[0];

        /// <summary>One month's accrual, scaled by how popular the government is. Idempotent per month.</summary>
        public static PoliticalPowerState Accrue(PoliticalPowerState prior, double governingVoteShare,
                                                 SimDate today, EngineTuning tuning)
        {
            PoliticalPowerState state = Working(prior);
            if (!tuning.Power.Enabled) return state;

            // The save-scum guard. The month that has already paid never pays again, however many
            // times it is re-entered. Equality, not >=: an earlier month is a rollback, not a
            // re-entry, and refusing it would freeze the currency while the debits kept running.
            // See obligation 1.
            int month = today.TotalMonths;
            if (state.LastAccrualMonth == month) return state;

            int gain = PoliticalPower.AccrualFor(governingVoteShare, tuning);

            // Stamped whether or not the month paid anything: an interregnum's zero is still that
            // month's answer, and re-asking it later must not find a share that has since changed.
            state.LastAccrualMonth = month;
            if (gain == 0) return state;

            Move(state, gain, PowerLedgerReason.Accrual, "", "", month, tuning);
            return state;
        }

        /// <summary>
        /// The award or penalty for every scored slot of one resolved story, as a single new state.
        /// </summary>
        /// <remarks>
        /// A slot's tier comes from its own event, not from the story: a story is a bundle and its
        /// slots can differ. <see cref="PoliticalPower.AwardFor"/>'s <c>manualDeclared</c> cap is
        /// one-sided by design — read its remarks before touching it, because the symmetric version
        /// looks correct and hands a discount to anyone who prefers the Manual button.
        /// </remarks>
        public static PoliticalPowerState AwardForStory(PoliticalPowerState prior, Story story,
                                                        IReadOnlyList<CivicEvent> catalog,
                                                        SimDate today, EngineTuning tuning)
        {
            PoliticalPowerState state = Working(prior);
            if (!tuning.Power.Enabled || story == null) return state;

            StoriesTuning stories = tuning.Stories;
            IReadOnlyList<StorySlot> slots = story.Slots ?? (IReadOnlyList<StorySlot>)NoSlots;
            int month = today.TotalMonths;

            // Slot order is already a declared total order (major first, then EventId ordinal), so
            // walking the list is enough to give the month's entries a deterministic sequence.
            for (int i = 0; i < slots.Count; i++)
            {
                StorySlot slot = slots[i];
                if (slot == null) continue;

                CivicEvent? ev = Find(catalog, slot.EventId);

                // An id the catalog no longer carries pays nothing. Guessing a tier for it would
                // charge the player for content that was removed from under them.
                if (ev == null) continue;

                StoryTier tier = ev.TierUnder(stories.MandatorySeverityThreshold,
                                              stories.MajorSeverityThreshold);

                // Only a declared manual outcome is capped: an undeclared Manual slot has reached
                // resolution as an ordinary not-met and is charged at the event's real tier.
                bool manualDeclared = slot.Response == SlotResponse.Manual && slot.ManualDeclared;

                int delta = PoliticalPower.AwardFor(slot.SlotOutcome, tier, manualDeclared, tuning);
                if (delta == 0) continue;

                // ManualAward is its own reason so the one-sided cap is auditable in the ledger; a
                // self-declared failure is a plain penalty, because the cap never touched it.
                PowerLedgerReason reason = delta < 0
                    ? PowerLedgerReason.FailurePenalty
                    : (manualDeclared ? PowerLedgerReason.ManualAward : PowerLedgerReason.SuccessAward);

                Move(state, delta, reason, story.Id, slot.EventId, month, tuning);
            }

            return state;
        }

        /// <summary>
        /// Debits an override, reporting whether it went through.
        /// </summary>
        /// <param name="next">
        /// Always a valid state and never null: the debited state on success, the untouched prior
        /// clone on a refusal.
        /// </param>
        /// <returns>
        /// True when the override was granted — <b>including when it was legitimately free</b> — and
        /// false only when <see cref="PoliticalPower.CanAfford"/> refused it.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>The success signal is explicit because the balance cannot carry it.</b> A refused spend
        /// and a spend that cost nothing both leave the balance where it was and write no ledger
        /// entry, so a caller inferring the outcome from the movement cannot tell them apart. A
        /// hand-edited <c>overrideCost</c> of 0 is the case that separates them: the override is
        /// granted and correctly free, and a balance comparison would report it to the player as
        /// <see cref="CommandOutcome.InsufficientPower"/>.
        /// </para>
        /// <para>
        /// Callers must still surface a false as <see cref="CommandOutcome.InsufficientPower"/>; a
        /// spend that silently no-ops is the one failure the command surface may not have. The
        /// affordability check is repeated here as a backstop rather than as the decision — minting a
        /// debit the affordability rule forbids would be worse than a caller that forgot to ask, and
        /// the two answers can never disagree because both are
        /// <see cref="PoliticalPower.CanAfford"/>.
        /// </para>
        /// </remarks>
        public static bool TrySpend(PoliticalPowerState prior, string storyId, string eventId,
                                    StoryTier tier, SimDate today, EngineTuning tuning,
                                    out PoliticalPowerState next)
        {
            PoliticalPowerState state = Working(prior);
            next = state;

            // Covers the master switch too: CanAfford refuses everything while the packet is off.
            if (!PoliticalPower.CanAfford(state, tier, tuning)) return false;

            int cost = PoliticalPower.OverrideCost(tier, tuning);

            // A free override is granted, and granted is what the caller is told. It moves no balance
            // and so records nothing — the ledger explains movements, and there was not one.
            if (cost <= 0) return true;

            Move(state, -cost, PowerLedgerReason.OverrideSpend, storyId, eventId, today.TotalMonths, tuning);
            return true;
        }

        /// <summary>
        /// The effect a negative balance costs the city this month, or false when it costs nothing.
        /// </summary>
        /// <remarks>
        /// One month's duration, because the penalty is re-asked every month the balance stays
        /// negative — a longer request would keep charging a city that had already cleared its debt.
        /// </remarks>
        public static bool TryDebtPenalty(PoliticalPowerState power, EngineTuning tuning,
                                          out EffectRequest request)
        {
            request = default(EffectRequest);

            PowerTuning t = tuning.Power;
            if (!t.Enabled) return false;
            if (!PoliticalPower.IsInDebt(power)) return false;

            // The palette entry declares its own magnitudeCap and the resolver applies it, so the
            // tuned magnitude goes out as authored rather than being clamped twice.
            double magnitude = t.DebtRevenuePenalty;
            if (double.IsNaN(magnitude) || double.IsInfinity(magnitude) || magnitude <= 0.0) return false;

            request = new EffectRequest(DebtEffectId, EffectScope.City, magnitude, 1, null, DebtSourceId);
            return true;
        }

        /// <summary>
        /// The state every method works on: a copy, never the caller's instance — see obligation 5.
        /// </summary>
        /// <remarks>
        /// A null prior is legitimate rather than a defect: a sidecar written before the power block
        /// carries no <see cref="PoliticalPowerState"/> at all, and the first tick after such a load
        /// must start an empty balance rather than throw.
        /// </remarks>
        private static PoliticalPowerState Working(PoliticalPowerState prior)
        {
            return prior == null ? new PoliticalPowerState() : prior.Clone();
        }

        /// <summary>Applies one non-zero movement to the working state and records it.</summary>
        private static void Move(PoliticalPowerState state, int delta, PowerLedgerReason reason,
                                 string storyId, string eventId, int month, EngineTuning tuning)
        {
            state.Balance += delta;
            if (delta > 0) state.LifetimeEarned += delta;
            else state.LifetimeSpent += -delta;

            if (state.Ledger == null) state.Ledger = new List<PowerLedgerEntry>();

            state.Ledger.Add(new PowerLedgerEntry
            {
                Month = month,
                Sequence = NextSequence(state.Ledger, month),
                Reason = reason,
                Delta = delta,
                StoryId = storyId ?? "",
                EventId = eventId ?? ""
            });

            Trim(state.Ledger, tuning.Power.LedgerRetention);
        }

        /// <summary>
        /// The next free ordinal within <paramref name="month"/>.
        /// </summary>
        /// <remarks>
        /// Read from the entries already standing rather than from a counter, because the ledger is
        /// bounded and reloaded: a counter would restart at zero after a load and collide with the
        /// entries the same month had already written. A month whose earlier entries have all been
        /// trimmed away restarts at zero, which is harmless — there is nothing left to collide with.
        /// </remarks>
        private static int NextSequence(List<PowerLedgerEntry> ledger, int month)
        {
            int next = 0;
            for (int i = 0; i < ledger.Count; i++)
            {
                PowerLedgerEntry entry = ledger[i];
                if (entry == null || entry.Month != month) continue;
                if (entry.Sequence >= next) next = entry.Sequence + 1;
            }
            return next;
        }

        /// <summary>
        /// Drops the oldest entries beyond <c>power.ledgerRetention</c>. Newest last, so the front
        /// goes first.
        /// </summary>
        /// <remarks>
        /// A retention below 1 is floored at 1 rather than honoured: the movement just made must be
        /// explicable at the moment it is made, and a hand-edited zero would delete the record of a
        /// balance change in the same call that made it.
        /// </remarks>
        private static void Trim(List<PowerLedgerEntry> ledger, int retention)
        {
            if (retention < 1) retention = 1;
            int excess = ledger.Count - retention;
            if (excess > 0) ledger.RemoveRange(0, excess);
        }

        /// <summary>
        /// The catalog entry with this id, or null. A linear scan: the catalog is tens of entries and
        /// a story has three slots, so an index would cost more to build than it saves.
        /// </summary>
        private static CivicEvent? Find(IReadOnlyList<CivicEvent> catalog, string eventId)
        {
            if (catalog == null || string.IsNullOrEmpty(eventId)) return null;

            for (int i = 0; i < catalog.Count; i++)
            {
                CivicEvent ev = catalog[i];
                if (ev != null && string.Equals(ev.Id, eventId, StringComparison.Ordinal)) return ev;
            }
            return null;
        }
    }
}
