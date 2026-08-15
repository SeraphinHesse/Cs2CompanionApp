using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Stories
{
    /// <summary>Why a political-power balance moved.</summary>
    public enum PowerLedgerReason
    {
        /// <summary>The monthly accrual, scaled by the government's standing.</summary>
        Accrual = 0,

        /// <summary>A story slot resolved met.</summary>
        SuccessAward = 1,

        /// <summary>A story slot resolved not-met.</summary>
        FailurePenalty = 2,

        /// <summary>The player bought a slot off.</summary>
        OverrideSpend = 3,

        /// <summary>A slot the player declared themselves. Capped at the minor rate — see below.</summary>
        ManualAward = 4
    }

    /// <summary>One movement of the balance. Bounded history, for the UI.</summary>
    public sealed class PowerLedgerEntry
    {
        /// <summary>Month of the movement, as <c>SimDate.TotalMonths</c>.</summary>
        public int Month { get; set; }

        /// <summary>Ordinal within the month, so same-month entries have a total order.</summary>
        public int Sequence { get; set; }

        public PowerLedgerReason Reason { get; set; } = PowerLedgerReason.Accrual;

        /// <summary>Signed. Negative for a spend or a penalty.</summary>
        public int Delta { get; set; }

        /// <summary>The story this movement belongs to, or empty for an accrual.</summary>
        public string StoryId { get; set; } = "";

        /// <summary>The event this movement belongs to, or empty.</summary>
        public string EventId { get; set; } = "";
    }

    /// <summary>
    /// The political-power currency: what gates an override, and what debt punishes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="Balance"/> is signed, deliberately.</b> Debt is a state the player can be in,
    /// not a spend that gets refused — the refusal happens at the affordability check, and the debt
    /// arrives through failure penalties the player does not choose. Its consequence is wave 4's
    /// capped revenue effect; the arithmetic here is pure.
    /// </para>
    /// <para>
    /// <b>The manual-declaration path is the one real exploit surface.</b> A player who declares
    /// their own success on a mandatory event would otherwise mint the mandatory rate on a one-word
    /// justification. Manual awards are capped at the minor rate regardless of tier, which is why
    /// <see cref="PowerLedgerReason.ManualAward"/> is its own reason rather than a
    /// <see cref="PowerLedgerReason.SuccessAward"/> — the cap has to be auditable in the ledger.
    /// </para>
    /// </remarks>
    public sealed class PoliticalPowerState
    {
        /// <summary>Current balance. Negative means debt.</summary>
        public int Balance { get; set; }

        public int LifetimeEarned { get; set; }

        public int LifetimeSpent { get; set; }

        /// <summary>
        /// Last month an accrual was granted, as <c>SimDate.TotalMonths</c>. -1 means never.
        /// </summary>
        /// <remarks>
        /// The accrual's own idempotence guard, and the partner of wave 0's
        /// <c>PoliticalState.LastCompletedTickMonth</c>. Without it, save-scumming a month boundary
        /// farms power — which is exactly the exploit wave 0 landed first to prevent.
        /// </remarks>
        public int LastAccrualMonth { get; set; } = -1;

        /// <summary>
        /// Recent movements, newest last, bounded by <c>power.ledgerRetention</c>. Sorted by
        /// <c>(Month, Sequence)</c>.
        /// </summary>
        public List<PowerLedgerEntry> Ledger { get; set; } = new List<PowerLedgerEntry>();

        /// <summary>A field-by-field copy. Hand-maintained — see <c>PoliticalEngine.CloneState</c>.</summary>
        public PoliticalPowerState Clone()
        {
            var clone = new PoliticalPowerState
            {
                Balance = Balance,
                LifetimeEarned = LifetimeEarned,
                LifetimeSpent = LifetimeSpent,
                LastAccrualMonth = LastAccrualMonth,
                Ledger = new List<PowerLedgerEntry>(Ledger ?? new List<PowerLedgerEntry>())
            };
            return clone;
        }
    }

    /// <summary>What a player command asked for.</summary>
    public enum PlayerCommandKind
    {
        SetResponse = 0,
        DeclareManualOutcome = 1,
        ResolveNow = 2,
        SpendPowerOverride = 3
    }

    /// <summary>
    /// One dated, ordered player decision.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Amended non-negotiable #3.</b> Engine state at date D is a pure function of <i>(metrics
    /// history, prior state, seeds, catalogs, settings, and the ordered, dated log of player commands
    /// with timestamp ≤ D)</i>. The command log <b>is</b> engine state: it is persisted in
    /// <c>PoliticalState</c>, it has a total order, and it is <b>replayed, never re-solicited</b>.
    /// </para>
    /// <para>
    /// <b>A choice is an appended record, not a mutation</b>, and it is persisted the moment it is
    /// recorded rather than at resolution — <c>AgoraSidecarSystem.PreSerialize</c> already runs on
    /// every save, so a choice made in month M survives into M+1's tick.
    /// </para>
    /// </remarks>
    public sealed class PlayerCommand
    {
        public string StoryId { get; set; } = "";

        public string EventId { get; set; } = "";

        public PlayerCommandKind Kind { get; set; } = PlayerCommandKind.SetResponse;

        /// <summary>The response chosen, for <see cref="PlayerCommandKind.SetResponse"/>.</summary>
        public SlotResponse Response { get; set; } = SlotResponse.Unaddressed;

        /// <summary>The player's own words. Never parsed for a number.</summary>
        public string FreeText { get; set; } = "";

        /// <summary>Month the command was issued, as <c>SimDate.TotalMonths</c>.</summary>
        public int DecidedMonth { get; set; }

        /// <summary>
        /// Ordinal within the month. Two commands in one month are ordered by this and never by
        /// arrival — arrival order is wall-clock, which is not engine state.
        /// </summary>
        public int Sequence { get; set; }
    }
}
