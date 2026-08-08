// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using Agora.Core.Contracts;

namespace Agora.Mod.Persistence
{
    /// <summary>
    /// How a load was reconciled against what is on disk (<c>politicsmodplan.md</c> §5).
    /// </summary>
    public enum ReconciliationOutcome
    {
        /// <summary>
        /// No usable snapshot for this save. The engine cold-starts. This is the correct outcome for
        /// a brand new city and for the first load after installing Agora into an existing one —
        /// and it is the only outcome that legitimately begins with no political history.
        /// </summary>
        FreshStart = 0,

        /// <summary>A snapshot for the current sim month. Load it and continue; nothing to replay.</summary>
        ExactMatch = 1,

        /// <summary>
        /// The nearest earlier snapshot was chosen. The engine replays
        /// <see cref="ReconciliationPlan.MonthsToReplay"/> months deterministically from it, using
        /// current city state. §5: log a warning, never crash, never reset politics.
        /// </summary>
        FastForward = 2,

        /// <summary>
        /// Every snapshot is later than the current sim date — the player reloaded a city save older
        /// than any political state Agora has written. The earliest snapshot is used for identity
        /// and settings only; later snapshots are left on disk untouched, because reloading forward
        /// again must find them intact.
        /// </summary>
        RewindBeforeHistory = 3,

        /// <summary>
        /// The preferred snapshot would not load, and an older one was used instead. Same handling
        /// as <see cref="FastForward"/> from the caller's point of view, but it means real damage
        /// was found and quarantined, and it should be loud.
        /// </summary>
        RecoveredAfterDamage = 4
    }

    /// <summary>
    /// Which file to load and how far the engine has to catch up. Produced before any file is
    /// opened, so the decision can be reasoned about — and tested — separately from the IO.
    /// </summary>
    public sealed class ReconciliationPlan
    {
        public ReconciliationPlan(ReconciliationOutcome outcome, StateFileRef chosen,
                                  int monthsToReplay, string explanation)
        {
            Outcome = outcome;
            Chosen = chosen;
            MonthsToReplay = monthsToReplay;
            Explanation = explanation;
        }

        public ReconciliationOutcome Outcome { get; private set; }

        /// <summary>The snapshot to load, or null for <see cref="ReconciliationOutcome.FreshStart"/>.</summary>
        public StateFileRef Chosen { get; private set; }

        /// <summary>
        /// Whole months the engine must advance after loading. Zero for an exact match. Never
        /// negative: the engine has no reverse gear, which is what
        /// <see cref="ReconciliationOutcome.RewindBeforeHistory"/> exists to say out loud.
        /// </summary>
        public int MonthsToReplay { get; private set; }

        /// <summary>One log-ready sentence describing the decision.</summary>
        public string Explanation { get; private set; }
    }

    /// <summary>
    /// The load-reconciliation rule from <c>politicsmodplan.md</c> §5, as a pure function:
    ///
    /// <blockquote>
    /// On load: match GUID + sim date exactly → load that snapshot. Missing exact match → reconcile:
    /// nearest earlier snapshot + fast-forward the engine deterministically using current city state;
    /// log a warning; never crash, never reset politics.
    /// </blockquote>
    ///
    /// <para>
    /// The guid half of the match is structural — snapshots live in a directory named after the save
    /// guid, so a file that is present is by construction a file for this save. What is left is the
    /// date half, and the two ways it can go wrong: no snapshot at or before the current date, and a
    /// snapshot that will not parse.
    /// </para>
    ///
    /// <para>
    /// Nothing here touches the filesystem. <see cref="SidecarStore"/> supplies the candidate list
    /// and performs the IO, so this decision is reproducible from a list of (year, month) pairs.
    /// </para>
    /// </summary>
    public static class LoadReconciliation
    {
        /// <summary>
        /// Chooses a snapshot for <paramref name="currentDate"/>.
        /// </summary>
        /// <param name="available">
        /// Candidates, oldest first — the order <see cref="SidecarPaths.EnumerateStateFiles"/>
        /// guarantees. Callers that have already rejected a damaged file pass the shortened list.
        /// </param>
        /// <param name="currentDate">The sim date being loaded, from <c>AgoraTimeService</c> (#8).</param>
        /// <param name="recoveringFromDamage">
        /// True once at least one candidate has been rejected as unreadable, so the outcome is
        /// reported as <see cref="ReconciliationOutcome.RecoveredAfterDamage"/> rather than as a
        /// routine fast-forward.
        /// </param>
        public static ReconciliationPlan Plan(IList<StateFileRef> available, SimDate currentDate,
                                              bool recoveringFromDamage)
        {
            if (available == null || available.Count == 0)
            {
                return new ReconciliationPlan(
                    ReconciliationOutcome.FreshStart, null, 0,
                    recoveringFromDamage
                        ? "No readable political snapshot survived; starting fresh from the current city state."
                        : "No political snapshot for this save yet; starting fresh.");
            }

            int currentMonths = currentDate.Year * 12 + (currentDate.Month - 1);

            StateFileRef exact = null;
            StateFileRef nearestEarlier = null;
            StateFileRef earliest = null;

            // A single forward pass, not a LINQ query over an unordered source: the list is
            // documented as oldest-first and the "nearest earlier" answer depends on that order
            // being real.
            for (int i = 0; i < available.Count; i++)
            {
                StateFileRef candidate = available[i];
                if (candidate == null) continue;

                if (earliest == null || candidate.TotalMonths < earliest.TotalMonths)
                {
                    earliest = candidate;
                }

                if (candidate.TotalMonths == currentMonths)
                {
                    exact = candidate;
                }
                else if (candidate.TotalMonths < currentMonths)
                {
                    if (nearestEarlier == null || candidate.TotalMonths > nearestEarlier.TotalMonths)
                    {
                        nearestEarlier = candidate;
                    }
                }
            }

            if (exact != null)
            {
                return new ReconciliationPlan(
                    recoveringFromDamage ? ReconciliationOutcome.RecoveredAfterDamage : ReconciliationOutcome.ExactMatch,
                    exact, 0,
                    "Exact snapshot for " + Describe(currentDate) + ".");
            }

            if (nearestEarlier != null)
            {
                int replay = currentMonths - nearestEarlier.TotalMonths;

                return new ReconciliationPlan(
                    recoveringFromDamage ? ReconciliationOutcome.RecoveredAfterDamage : ReconciliationOutcome.FastForward,
                    nearestEarlier, replay,
                    "No snapshot for " + Describe(currentDate) + "; reconciling from " +
                    nearestEarlier + " and fast-forwarding " + Count(replay, "month") + ".");
            }

            if (earliest == null)
            {
                // Every entry was null — only reachable from a malformed candidate list, but the
                // alternative is a plan that names no file while claiming one was chosen.
                return new ReconciliationPlan(
                    ReconciliationOutcome.FreshStart, null, 0,
                    "No usable political snapshot for this save; starting fresh.");
            }

            // Everything on disk is in the future. The city save was rolled back further than any
            // political state Agora has written.
            //
            // Resetting here would be the easy wrong answer: it discards the party system, the
            // election history and the per-save settings, all of which are still the right ones for
            // this city. Instead the earliest snapshot supplies identity and settings, the engine
            // rebuilds current state from city metrics, and the later files stay where they are so
            // that reloading forward finds them.
            return new ReconciliationPlan(
                ReconciliationOutcome.RewindBeforeHistory, earliest, 0,
                "The city is at " + Describe(currentDate) + ", earlier than every political snapshot " +
                "(earliest is " + earliest + "). Reusing its settings and party system; later " +
                "snapshots are left in place.");
        }

        /// <summary>
        /// The candidate list with <paramref name="rejected"/> removed. Used to step back one file at
        /// a time when a snapshot will not parse, so that a damaged newest file costs one month of
        /// replay rather than the whole history.
        /// </summary>
        public static List<StateFileRef> Without(IList<StateFileRef> available, StateFileRef rejected)
        {
            var remaining = new List<StateFileRef>();
            if (available == null) return remaining;

            for (int i = 0; i < available.Count; i++)
            {
                StateFileRef candidate = available[i];
                if (candidate == null) continue;
                if (rejected != null && ReferenceEquals(candidate, rejected)) continue;

                remaining.Add(candidate);
            }

            return remaining;
        }

        private static string Describe(SimDate date)
        {
            return date.Year.ToString("D4", CultureInfo.InvariantCulture) + "-" +
                   date.Month.ToString("D2", CultureInfo.InvariantCulture);
        }

        private static string Count(int value, string noun)
        {
            return value.ToString(CultureInfo.InvariantCulture) + " " + noun + (value == 1 ? string.Empty : "s");
        }
    }
}
