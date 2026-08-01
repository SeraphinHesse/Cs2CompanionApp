using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Polling
{
    /// <summary>
    /// One district's contribution to a poll: what the voter model actually believes, plus the two
    /// demographic facts that decide how badly the pollster will mis-sample it.
    /// </summary>
    /// <remarks>
    /// This is the seam between packet 6 and the affinity/turnout packets. Polling takes numbers in
    /// and never reaches back into blocs, parties or snapshots, so a change to the voter model cannot
    /// change how a poll is distorted — only what it is distorted from.
    /// </remarks>
    public sealed class DistrictPollInput
    {
        /// <summary>Matches <c>DistrictSnapshot.Id</c>. Used as the seed sub-stream entity id.</summary>
        public string DistrictId { get; set; } = "";

        /// <summary>
        /// The voter model's shares for this district, 0–1, summing to 1. Sorted by party id ordinal
        /// ascending, like every other list of these (see <see cref="PartyVoteShare"/>).
        /// </summary>
        public List<PartyVoteShare> TrueShares { get; set; } = new List<PartyVoteShare>();

        /// <summary>Modelled turnout for this district, 0–1. From packet 5.</summary>
        public double ProjectedTurnout { get; set; }

        /// <summary>Eligible voters. Together with turnout this gives the district's real weight.</summary>
        public int EligibleVoters { get; set; }

        /// <summary>
        /// Mean education on [0,1] — <c>DistrictSnapshot.Education.Index()</c>. The lower this is, the
        /// more the district is under-sampled.
        /// </summary>
        public double EducationIndex { get; set; }

        /// <summary>
        /// Builds an input from a snapshot so every caller derives the education index the same way.
        /// A district running on city fallbacks is still polled — a pollster does not know which of
        /// its numbers are estimates either — but callers may wish to skip it.
        /// </summary>
        public static DistrictPollInput FromSnapshot(
            DistrictSnapshot district,
            IEnumerable<PartyVoteShare> trueShares,
            double projectedTurnout,
            int eligibleVoters)
        {
            if (district == null) throw new ArgumentNullException(nameof(district));

            return new DistrictPollInput
            {
                DistrictId = district.Id,
                TrueShares = trueShares == null ? new List<PartyVoteShare>() : new List<PartyVoteShare>(trueShares),
                ProjectedTurnout = projectedTurnout,
                EligibleVoters = eligibleVoters,
                EducationIndex = district.Education.Index()
            };
        }
    }

    /// <summary>
    /// Everything one published poll needs. Pure input: the engine reads it, never stores it.
    /// </summary>
    public sealed class PollRequest
    {
        /// <summary>The save's identity, from <c>PoliticalState.SaveGuid</c>. Half of every seed.</summary>
        public Guid SaveGuid { get; set; }

        /// <summary>Publication date. The other half of the sampling-error seed.</summary>
        public SimDate Date { get; set; }

        /// <summary>
        /// The election this poll anticipates. Null outside a campaign, in which case the poll is
        /// treated as maximally distant from election day: full error, no herding.
        /// </summary>
        public SimDate? ElectionDate { get; set; }

        /// <summary>
        /// Engine-owned pollster id, e.g. <c>"pollster-01"</c> from <see cref="PollSchedule"/>. A
        /// pollster's house effect is keyed on this and on the election date, so it stays constant
        /// across one campaign and is re-drawn for the next.
        /// </summary>
        public string PollsterId { get; set; } = "";

        /// <summary>Per-district model truth. Order is irrelevant; the engine sorts by district id.</summary>
        public List<DistrictPollInput> Districts { get; set; } = new List<DistrictPollInput>();

        /// <summary>False for an internal model snapshot that never reaches the news feed.</summary>
        public bool IsPublished { get; set; } = true;

        /// <summary>
        /// Optional id override. Defaults to <c>"poll-YYYY-MM-DD"</c>, which is unique because
        /// <see cref="PollSchedule"/> puts exactly one pollster in the field per publication day.
        /// </summary>
        public string? Id { get; set; }
    }
}
