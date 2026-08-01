using System.Collections.Generic;

namespace Agora.Core.Contracts
{
    /// <summary>
    /// One party's share of a vote. The atom of polls, district results and city results.
    /// </summary>
    /// <remarks>
    /// Lists of these are always sorted by <see cref="PartyId"/> ordinal ascending. That is a
    /// contract, not a convention: an unsorted list makes a serialized-state hash depend on
    /// construction order, which is exactly how a "desync" (§2.3) appears without a real bug.
    /// </remarks>
    public readonly struct PartyVoteShare
    {
        public string PartyId { get; }

        /// <summary>0–1. Within one result set, shares sum to 1 within rounding.</summary>
        public double Share { get; }

        public PartyVoteShare(string partyId, double share)
        {
            PartyId = partyId;
            Share = share;
        }

        public override string ToString() => PartyId + "=" + Share.ToString("F4",
            System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A published opinion poll. Deliberately *not* the truth: the whole point is that published
    /// polls under-sample low-education, low-turnout districts (§3 Campaigns), and the simulation
    /// harness asserts the direction of that error.
    /// </summary>
    /// <remarks>
    /// Polls are engine state and are persisted, because a reload must reproduce the same published
    /// numbers. The error draw comes from <c>StreamNames.PollError</c>, the house effect from
    /// <c>StreamNames.PollHouseEffect</c>.
    /// </remarks>
    public sealed class PollResult
    {
        public int SchemaVersion { get; set; } = 1;

        /// <summary>Stable id, e.g. <c>"poll-1994-04-11"</c>.</summary>
        public string Id { get; set; } = "";

        /// <summary>Publication date. Polls publish every <c>polling.publishIntervalDays</c>.</summary>
        public SimDate Date { get; set; }

        /// <summary>Pollster / outlet name. Flavor-owned; never affects the numbers.</summary>
        public string PollsterName { get; set; } = "";

        /// <summary>
        /// Pollster id. Engine-owned and stable, so a house effect is consistent across a campaign.
        /// </summary>
        public string PollsterId { get; set; } = "";

        /// <summary>Published city-wide shares, sorted by party id. Excludes undecideds.</summary>
        public List<PartyVoteShare> Shares { get; set; } = new List<PartyVoteShare>();

        /// <summary>
        /// What the engine's own model says the shares would be if the election were held today.
        /// Never shown to the player — it exists so the harness can measure poll error.
        /// </summary>
        public List<PartyVoteShare> TrueShares { get; set; } = new List<PartyVoteShare>();

        /// <summary>Optional per-district breakdown, sorted by district id.</summary>
        public List<DistrictPollResult> Districts { get; set; } = new List<DistrictPollResult>();

        /// <summary>Share reported as undecided, 0–1. Decays toward election day.</summary>
        public double UndecidedShare { get; set; }

        /// <summary>Projected turnout, 0–1.</summary>
        public double ProjectedTurnout { get; set; }

        /// <summary>Nominal sample size. Drives the reported margin of error.</summary>
        public int SampleSize { get; set; }

        /// <summary>Reported margin of error, 0–1 (e.g. 0.031 for ±3.1 points).</summary>
        public double MarginOfError { get; set; }

        /// <summary>Weeks until the election this poll is about. Zero on election day.</summary>
        public int WeeksToElection { get; set; }

        /// <summary>The election this poll anticipates, if scheduled.</summary>
        public SimDate? ElectionDate { get; set; }

        /// <summary>False for internal model snapshots that are never shown in the news feed.</summary>
        public bool IsPublished { get; set; } = true;
    }

    /// <summary>One district's slice of a poll. Same sampling bias as the city figure, applied locally.</summary>
    public sealed class DistrictPollResult
    {
        public string DistrictId { get; set; } = "";

        /// <summary>Published shares, sorted by party id.</summary>
        public List<PartyVoteShare> Shares { get; set; } = new List<PartyVoteShare>();

        public double ProjectedTurnout { get; set; }

        /// <summary>
        /// Signed sampling weight applied to this district, where negative means under-sampled.
        /// The harness asserts this is negative for low-education districts.
        /// </summary>
        public double SamplingBias { get; set; }
    }

    /// <summary>
    /// One party's seats after an election. Produced by the PR allocator or by counting FPTP wins.
    /// </summary>
    public readonly struct SeatAllocation
    {
        public string PartyId { get; }

        /// <summary>Total seats won.</summary>
        public int Seats { get; }

        /// <summary>Seats as a fraction of the chamber, 0–1.</summary>
        public double SeatShare { get; }

        /// <summary>Vote share that produced them, 0–1. Kept so disproportionality is visible.</summary>
        public double VoteShare { get; }

        /// <summary>
        /// Seats won in district contests. Equals <see cref="Seats"/> under FPTP and is zero under a
        /// pure list system.
        /// </summary>
        public int DistrictSeats { get; }

        /// <summary>Seats won from the party list. Zero under FPTP.</summary>
        public int ListSeats { get; }

        /// <summary>False when the party fell below <c>electionsPr.thresholdShare</c>.</summary>
        public bool PassedThreshold { get; }

        public SeatAllocation(string partyId, int seats, double seatShare, double voteShare,
                              int districtSeats, int listSeats, bool passedThreshold)
        {
            PartyId = partyId;
            Seats = seats;
            SeatShare = seatShare;
            VoteShare = voteShare;
            DistrictSeats = districtSeats;
            ListSeats = listSeats;
            PassedThreshold = passedThreshold;
        }
    }

    /// <summary>One district's count. Under FPTP this decides a seat; under PR it is reporting only.</summary>
    public sealed class DistrictResult
    {
        public string DistrictId { get; set; } = "";

        /// <summary>Final shares, sorted by party id.</summary>
        public List<PartyVoteShare> Shares { get; set; } = new List<PartyVoteShare>();

        /// <summary>Realised turnout, 0–1.</summary>
        public double Turnout { get; set; }

        /// <summary>Whole votes cast. Integer counts, so a margin of one vote is representable.</summary>
        public int VotesCast { get; set; }

        public int EligibleVoters { get; set; }

        /// <summary>Leading party.</summary>
        public string WinningPartyId { get; set; } = "";

        /// <summary>Lead over second place, 0–1.</summary>
        public double Margin { get; set; }

        /// <summary>Seats this district awarded. 1 under FPTP, 0 under a pure list system.</summary>
        public int Seats { get; set; }

        /// <summary>
        /// True when the top two were within <c>electionsFptp.tieMarginEpsilon</c> and the winner came
        /// from the <c>StreamNames.ElectionTieBreak</c> stream.
        /// </summary>
        public bool DecidedByTieBreak { get; set; }
    }

    /// <summary>
    /// A completed election. Immutable history once written: the coalition, mandate and lifecycle
    /// packets all read from it, and rewriting one would rewrite the save's politics.
    /// </summary>
    public sealed class ElectionResult
    {
        public int SchemaVersion { get; set; } = 1;

        /// <summary>Stable id, e.g. <c>"election-1994-05"</c>.</summary>
        public string Id { get; set; } = "";

        public SimDate Date { get; set; }

        public ElectoralSystem System { get; set; } = ElectoralSystem.Proportional;

        /// <summary>Sequential term number this election opens, starting at 1.</summary>
        public int TermNumber { get; set; }

        /// <summary>True for an election triggered by a coalition collapse rather than the calendar.</summary>
        public bool IsSnapElection { get; set; }

        /// <summary>Party ids on the ballot, sorted ascending.</summary>
        public List<string> PartyIdsOnBallot { get; set; } = new List<string>();

        /// <summary>City-wide shares, sorted by party id.</summary>
        public List<PartyVoteShare> CityVoteShares { get; set; } = new List<PartyVoteShare>();

        /// <summary>Per-district counts, sorted by district id.</summary>
        public List<DistrictResult> Districts { get; set; } = new List<DistrictResult>();

        /// <summary>Seats, sorted by party id. Sums to <see cref="TotalSeats"/>.</summary>
        public List<SeatAllocation> Seats { get; set; } = new List<SeatAllocation>();

        public int TotalSeats { get; set; }

        /// <summary>Realised city-wide turnout, 0–1.</summary>
        public double Turnout { get; set; }

        public int TotalVotesCast { get; set; }

        public int TotalEligibleVoters { get; set; }

        /// <summary>Winner of the mayoral race. Null under the Proportional system.</summary>
        public string? MayorPartyId { get; set; }

        /// <summary>Mayor's name. Flavor-owned.</summary>
        public string? MayorName { get; set; }

        /// <summary>Mayoral shares, sorted by party id. Empty under the Proportional system.</summary>
        public List<PartyVoteShare> MayorVoteShares { get; set; } = new List<PartyVoteShare>();

        /// <summary>
        /// Mean absolute deviation between the last published poll and the result, 0–1. Reporting
        /// only; the harness asserts poll-error *direction* against per-district figures.
        /// </summary>
        public double FinalPollDeviation { get; set; }

        /// <summary>When the next scheduled election falls, given the term length in tuning.</summary>
        public SimDate NextElectionDate { get; set; }
    }
}
