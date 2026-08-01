using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Factions
{
    /// <summary>
    /// What happened to a party's dominance this cycle.
    /// </summary>
    public readonly struct DominanceOutcome
    {
        public string PartyId { get; }

        /// <summary>Faction that held the platform pen going in, or null.</summary>
        public string? PreviousDominantFactionId { get; }

        /// <summary>Faction that holds it coming out, or null when no faction reaches the threshold.</summary>
        public string? DominantFactionId { get; }

        public DominanceOutcome(string partyId, string? previous, string? current)
        {
            PartyId = partyId;
            PreviousDominantFactionId = previous;
            DominantFactionId = current;
        }

        /// <summary>A different faction now writes the platform. The M4b NA gate watches this.</summary>
        public bool IsTakeover =>
            !string.IsNullOrEmpty(DominantFactionId)
            && !string.IsNullOrEmpty(PreviousDominantFactionId)
            && string.CompareOrdinal(DominantFactionId, PreviousDominantFactionId) != 0;

        /// <summary>No faction clears <c>factions.dominanceThreshold</c>: the party writes its platform
        /// by committee this cycle.</summary>
        public bool IsVacant => string.IsNullOrEmpty(DominantFactionId);

        public bool IsFirstClaim =>
            !string.IsNullOrEmpty(DominantFactionId) && string.IsNullOrEmpty(PreviousDominantFactionId);
    }

    /// <summary>
    /// Which faction writes the party platform (§3, NA theme).
    ///
    /// <para>
    /// Two tuned numbers decide it. <c>factions.dominanceThreshold</c> is the support a faction needs
    /// to own the pen at all; below it the party has no dominant faction and the platform is written
    /// by committee. <c>factions.dominanceHysteresis</c> is the extra margin a challenger needs to
    /// take the pen off an incumbent — without it a party with two factions near 50% would flip its
    /// platform author every single cycle on floating-point noise.
    /// </para>
    ///
    /// <para>
    /// There is no seeded draw anywhere in here. Dominance is a deterministic function of support, and
    /// exact ties break to the lower faction id — a total order, so no <c>SeedStreams</c> call is
    /// needed and none is made.
    /// </para>
    ///
    /// <para>
    /// AGORA-SEAM(§14.1): modelling NA primaries as real elections is an open decision, and
    /// <c>electionsFptp.primariesEnabled</c> ships pinned false. Until it closes this class is the
    /// stand-in — the faction that wins the internal argument writes the platform, and no ballot is
    /// simulated. Nothing here should grow a candidate-selection path before the decision closes.
    /// </para>
    /// </summary>
    public static class FactionDominance
    {
        /// <summary>
        /// The faction that should hold the pen, or null when nobody clears the threshold. Pure — it
        /// reads <c>IsDominant</c> to identify the incumbent but writes nothing.
        /// </summary>
        public static string? Select(IReadOnlyList<Faction>? partyFactions, EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            List<Faction> eligible = FactionSupport.EligibleSortedById(partyFactions);
            if (eligible.Count == 0) return null;

            double threshold = tuning.Factions.DominanceThreshold;
            double hysteresis = Math.Abs(tuning.Factions.DominanceHysteresis);

            Faction top = eligible[0];
            for (int i = 1; i < eligible.Count; i++)
            {
                // Strictly greater: eligible is id-sorted, so an exact tie keeps the lower id.
                if (Support(eligible[i]) > Support(top)) top = eligible[i];
            }

            Faction? incumbent = null;
            for (int i = 0; i < eligible.Count; i++)
            {
                if (eligible[i].IsDominant) { incumbent = eligible[i]; break; }
            }

            if (incumbent != null && Support(incumbent) >= threshold)
            {
                bool challengerWins =
                    !ReferenceEquals(top, incumbent)
                    && Support(top) > Support(incumbent) + hysteresis
                    && Support(top) >= threshold;

                return challengerWins ? top.Id : incumbent.Id;
            }

            return Support(top) >= threshold ? top.Id : null;
        }

        /// <summary>
        /// Runs <see cref="Select"/> and writes <c>IsDominant</c> across the party. Every ineligible
        /// faction (dissolved, merged) is cleared, so a dead faction can never be found holding the pen.
        /// </summary>
        public static DominanceOutcome Apply(string? partyId, IReadOnlyList<Faction>? partyFactions, EngineTuning tuning)
        {
            string? previous = null;
            if (partyFactions != null)
            {
                List<Faction> incumbentSearch = FactionSupport.EligibleSortedById(partyFactions);
                for (int i = 0; i < incumbentSearch.Count; i++)
                    if (incumbentSearch[i].IsDominant) { previous = incumbentSearch[i].Id; break; }
            }

            string? selected = Select(partyFactions, tuning);

            if (partyFactions != null)
            {
                for (int i = 0; i < partyFactions.Count; i++)
                {
                    Faction f = partyFactions[i];
                    if (f == null) continue;
                    f.IsDominant = FactionSupport.IsEligible(f)
                                   && selected != null
                                   && string.CompareOrdinal(f.Id, selected) == 0;
                }
            }

            return new DominanceOutcome(partyId ?? "", previous, selected);
        }

        private static double Support(Faction f)
        {
            double v = f.InternalSupport;
            return IssueVectors.IsFinite(v) ? v : 0.0;
        }
    }
}
