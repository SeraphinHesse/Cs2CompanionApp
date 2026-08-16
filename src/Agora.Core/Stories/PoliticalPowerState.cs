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
    /// <para>
    /// <b>The cap applies to the award only, never to the penalty.</b> A self-declared <i>failure</i>
    /// is charged at the event's real tier. Capping both sides looks symmetrical and is a trap: it
    /// would make a truthfully self-declared mandatory failure cost 5 where <c>Ignore</c> costs 25,
    /// so a player who simply preferred the Manual button would take an 80% discount on every
    /// mandatory failure in the game — no lying required, and the tier ladder would survive on the
    /// award side alone. Charging the real tier keeps honest self-reporting exactly as expensive as
    /// <c>Ignore</c> and never worse, so honesty is never punished relative to silence. The remaining
    /// gap — that a <i>false</i> declaration of success still beats an honest failure — is not
    /// closable in arithmetic: the design concedes unverifiable declarations, and a player set on
    /// cheating can edit the sidecar. If it needs closing it belongs at the response layer, by making
    /// <c>Manual</c> unavailable when the slot's check is measurable.
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

        /// <summary>
        /// The declared outcome, for <see cref="PlayerCommandKind.DeclareManualOutcome"/> only.
        /// Meaningless — and always false — on every other kind.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Without this the log cannot tell a declared success from a declared failure</b>, and the
        /// contract above promises it can. The two commands would append rows differing in no field,
        /// while the flag they set — <see cref="StorySlot.ManualDeclared"/> — is what
        /// <see cref="PoliticalPower.AwardFor"/>'s <c>manualDeclared</c> parameter reads. A replay
        /// that rebuilt state from the log, which this contract explicitly permits, would score a
        /// different award from the one the player earned. Found by review of wave 4's command lane,
        /// which had written a remark asserting the log told them apart when nothing in the log could.
        /// </para>
        /// <para>
        /// <b>Additive and optional, so no schema version moves.</b> A sidecar written before this
        /// field reads it as <c>false</c>, which is correct rather than merely tolerable: wave 4 is
        /// the first wave that writes any story command at all, so there is no older save containing
        /// a <see cref="PlayerCommandKind.DeclareManualOutcome"/> row for the default to be wrong
        /// about. The wave-3 precedent is <c>engine_tuning</c>, which bumped because a section gained
        /// a <b>required</b> property.
        /// </para>
        /// </remarks>
        public bool DeclaredMet { get; set; }

        /// <summary>Month the command was issued, as <c>SimDate.TotalMonths</c>.</summary>
        public int DecidedMonth { get; set; }

        /// <summary>
        /// Ordinal within the month. Two commands in one month are ordered by this and never by
        /// arrival — arrival order is wall-clock, which is not engine state.
        /// </summary>
        public int Sequence { get; set; }
    }

    /// <summary>
    /// The one implementation of the command log's ordering rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Here, in <c>Agora.Core</c>, because deciding where a record sorts in engine state is
    /// computing rather than glue.</b> Wave 4's command lane implemented this inside
    /// <c>Agora.Mod</c>, which <c>src/Agora.Mod/CLAUDE.md</c> forbids — "compute nothing that belongs
    /// in Agora.Core" — and which would have left one documented ordering rule with two
    /// implementations on opposite sides of the assembly boundary. That is how they drift.
    /// </para>
    /// <para>
    /// The sort key is <c>(DecidedMonth, Sequence, EventId ordinal)</c>, and the third component is
    /// never actually reached: <see cref="Append"/> always issues the highest sequence in its month,
    /// so two commands in one month are separated before the tiebreak matters. It is in the key
    /// anyway, because a total order that depends on nothing ever colliding is not a total order.
    /// </para>
    /// </remarks>
    public static class PlayerCommandLog
    {
        /// <summary>
        /// Appends <paramref name="command"/> in sort position, stamping its
        /// <see cref="PlayerCommand.Sequence"/> as the highest yet issued in its month.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Inserted at position rather than appended-and-sorted. <c>List.Sort</c> is unstable, so
        /// re-sorting a log whose sequences are already correct could still permute equal keys and
        /// change the serialized bytes — a determinism failure with nothing wrong behind it.
        /// </para>
        /// <para>
        /// <b>Both parameters are nullable because both are tolerated.</b> This runs from a command
        /// handler on the UI thread, where an escaping exception costs far more than a dropped
        /// record — the posture every other data path in the runtime takes. The signature says so
        /// rather than leaving a caller to discover it from the body.
        /// </para>
        /// </remarks>
        public static void Append(List<PlayerCommand>? log, PlayerCommand? command)
        {
            if (log == null || command == null) return;

            int next = 0;
            int insertAt = log.Count;

            for (int i = 0; i < log.Count; i++)
            {
                PlayerCommand existing = log[i];
                if (existing == null) continue;

                if (existing.DecidedMonth == command.DecidedMonth && existing.Sequence >= next)
                {
                    next = existing.Sequence + 1;
                }

                // The first entry belonging to a later month is where this one goes.
                if (insertAt == log.Count && existing.DecidedMonth > command.DecidedMonth)
                {
                    insertAt = i;
                }
            }

            command.Sequence = next;
            log.Insert(insertAt, command);
        }
    }
}
