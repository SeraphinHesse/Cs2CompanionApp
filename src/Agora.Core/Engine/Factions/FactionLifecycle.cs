using System;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Factions
{
    /// <summary>What a lifecycle cycle did to one faction. Reported, never inferred by the caller.</summary>
    public enum FactionLifecycleKind
    {
        /// <summary>Support fell under <c>factions.deathSupportThreshold</c>; one more cycle kills it.</summary>
        Endangered = 0,

        /// <summary>Support recovered above the threshold; the death counter reset.</summary>
        Recovered = 1,

        /// <summary>Dissolved. The brand persists so it can revive.</summary>
        Dissolved = 2,

        /// <summary>A splinter formed. <c>RelatedFactionId</c> is the new faction.</summary>
        Split = 3,

        /// <summary>Absorbed. <c>RelatedFactionId</c> is the survivor.</summary>
        Merged = 4,

        /// <summary>A dissolved faction returned because its core grievance resurged.</summary>
        Revived = 5,

        /// <summary>A different faction now writes the platform.</summary>
        Takeover = 6,

        /// <summary>
        /// The faction changed leader. The engine records only that it happened — the name itself is
        /// flavor-owned (non-negotiable #1) and arrives from <c>IFlavorProvider</c>.
        /// </summary>
        LeaderChange = 7
    }

    /// <summary>One reported lifecycle outcome.</summary>
    public readonly struct FactionLifecycleEvent
    {
        public string PartyId { get; }
        public string FactionId { get; }
        public FactionLifecycleKind Kind { get; }

        /// <summary>Splinter, survivor or predecessor, depending on <see cref="Kind"/>.</summary>
        public string? RelatedFactionId { get; }

        public FactionLifecycleEvent(string partyId, string factionId, FactionLifecycleKind kind, string? related = null)
        {
            PartyId = partyId;
            FactionId = factionId;
            Kind = kind;
            RelatedFactionId = related;
        }

        public override string ToString() =>
            PartyId + "/" + FactionId + ":" + Kind + (RelatedFactionId == null ? "" : "->" + RelatedFactionId);
    }

    /// <summary>Cadence and cross-packet gates for faction lifecycle.</summary>
    public static class FactionLifecycle
    {
        /// <summary>
        /// True once <c>factions.lifecycleCheckIntervalMonths</c> have elapsed since the last check.
        /// </summary>
        /// <remarks>
        /// The engine ticks monthly; lifecycle does not. Running it every tick would make a faction's
        /// fate depend on tick count rather than on elapsed political time.
        /// </remarks>
        public static bool IsCheckDue(SimDate lastCheck, SimDate now, EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            int interval = tuning.Factions.LifecycleCheckIntervalMonths;
            if (interval < 1) interval = 1;
            return lastCheck.MonthsUntil(now) >= interval;
        }

        /// <summary>
        /// The NA gate on *party*-level lifecycle. §3: in the NA theme a party splitting, merging or
        /// dying is possible but extremely unlikely — the churn happens between factions instead.
        /// </summary>
        /// <remarks>
        /// The coefficient lives in the <c>factions</c> section because it is a statement about the
        /// faction system, but the draw belongs to the party packet — so the generator is a parameter
        /// rather than something this method derives. The party packet passes its own
        /// <c>StreamNames.PartyLifecycle</c> sub-stream and stays the only thing drawing from it.
        /// </remarks>
        public static bool NaPartyLifecycleAllowed(DeterministicRng rng, EngineTuning tuning)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            return rng.NextBool(tuning.Factions.NaPartyLifecycleProbability);
        }
    }
}
