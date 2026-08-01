using System;
using System.Collections.Generic;
using System.Globalization;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Elections.Proportional
{
    /// <summary>
    /// An exact tie broken by the <c>election.tiebreak</c> stream, recorded so the outcome is
    /// auditable rather than mysterious.
    /// </summary>
    /// <remarks>
    /// A tie is <em>exact</em> equality of two highest-average quotients, not near-equality. Quotients
    /// are all computed as <c>votes / divisor</c> from the same expression, and IEEE-754 division is
    /// correctly rounded, so two mathematically equal ratios produce bit-identical doubles. An
    /// epsilon band here would turn "nearly won" into "coin flip", which is a different — and wrong —
    /// electoral rule.
    /// </remarks>
    public readonly struct SeatTieBreak
    {
        /// <summary>1-based index of the seat being awarded when the tie occurred.</summary>
        public int SeatNumber { get; }

        public string WinningPartyId { get; }

        /// <summary>Everyone tied for the seat, sorted by party id ordinal ascending.</summary>
        public IReadOnlyList<string> CandidatePartyIds { get; }

        /// <summary>
        /// The entity id handed to <c>SeedStreams.RngFor</c>. Recording it makes the draw
        /// reproducible from the save alone.
        /// </summary>
        public string StreamContext { get; }

        public SeatTieBreak(int seatNumber, string? winningPartyId,
                            IReadOnlyList<string>? candidatePartyIds, string? streamContext)
        {
            SeatNumber = seatNumber;
            WinningPartyId = winningPartyId ?? string.Empty;
            CandidatePartyIds = candidatePartyIds ?? new string[0];
            StreamContext = streamContext ?? string.Empty;
        }

        public override string ToString() =>
            "seat " + SeatNumber.ToString(CultureInfo.InvariantCulture) + " -> " + WinningPartyId +
            " (" + string.Join("|", ToArray(CandidatePartyIds)) + ")";

        private static string[] ToArray(IReadOnlyList<string> items)
        {
            var copy = new string[items.Count];
            for (int i = 0; i < items.Count; i++) copy[i] = items[i];
            return copy;
        }
    }

    /// <summary>
    /// The outcome of one proportional allocation: seats per party plus everything a caller needs to
    /// explain them.
    /// </summary>
    /// <remarks>
    /// <see cref="Seats"/> covers <em>every</em> party that was on the ballot, including those that
    /// fell below the threshold — they appear with zero seats and
    /// <c>PassedThreshold = false</c>. Dropping them would make the seat chart silently disagree with
    /// the vote chart, and would hide exactly the parties the lifecycle rules are watching (a party
    /// under 3% in two consecutive elections dies, §3).
    /// </remarks>
    public sealed class SeatAllocationResult
    {
        /// <summary>Seats for every party on the ballot, sorted by party id ordinal ascending.</summary>
        public List<SeatAllocation> Seats { get; }

        /// <summary>Chamber size. <see cref="Seats"/> sums to this whenever any party has votes.</summary>
        public int TotalSeats { get; }

        /// <summary>Total valid votes cast across all parties.</summary>
        public int TotalVotes { get; }

        /// <summary>Votes belonging to parties that qualified for seats.</summary>
        public int QualifyingVotes { get; }

        /// <summary>The threshold actually applied, from <c>electionsPr.thresholdShare</c>.</summary>
        public double ThresholdShare { get; }

        /// <summary>Allocation method actually used, after normalisation of the tuning string.</summary>
        public string Method { get; }

        /// <summary>Parties that received a share of the seats, sorted by party id ordinal.</summary>
        public List<string> QualifiedPartyIds { get; }

        /// <summary>Parties excluded by the threshold (or with no votes), sorted by party id ordinal.</summary>
        public List<string> ExcludedPartyIds { get; }

        /// <summary>
        /// True when no party cleared the threshold and it was waived so the chamber could still be
        /// filled. Degenerate — with 4–7 parties someone is always above 5% — but an empty chamber
        /// would leave government formation with nothing to do, so the allocator fails open here.
        /// </summary>
        public bool ThresholdWaived { get; }

        /// <summary>Every tie the seeded stream resolved, in award order.</summary>
        public List<SeatTieBreak> TieBreaks { get; }

        /// <summary>
        /// Gallagher least-squares disproportionality on 0–1 fractions (not percentages):
        /// <c>sqrt(0.5 * Σ (voteShare − seatShare)²)</c>. Reporting only; nothing consumes it.
        /// </summary>
        public double Disproportionality { get; }

        public SeatAllocationResult(
            List<SeatAllocation>? seats,
            int totalSeats,
            int totalVotes,
            int qualifyingVotes,
            double thresholdShare,
            string? method,
            List<string>? qualifiedPartyIds,
            List<string>? excludedPartyIds,
            bool thresholdWaived,
            List<SeatTieBreak>? tieBreaks,
            double disproportionality)
        {
            Seats = seats ?? new List<SeatAllocation>();
            TotalSeats = totalSeats;
            TotalVotes = totalVotes;
            QualifyingVotes = qualifyingVotes;
            ThresholdShare = thresholdShare;
            Method = method ?? string.Empty;
            QualifiedPartyIds = qualifiedPartyIds ?? new List<string>();
            ExcludedPartyIds = excludedPartyIds ?? new List<string>();
            ThresholdWaived = thresholdWaived;
            TieBreaks = tieBreaks ?? new List<SeatTieBreak>();
            Disproportionality = disproportionality;
        }

        /// <summary>Seats held by one party; 0 for a party that is not in the result.</summary>
        public int SeatsFor(string? partyId)
        {
            for (int i = 0; i < Seats.Count; i++)
            {
                if (string.Equals(Seats[i].PartyId, partyId, StringComparison.Ordinal))
                    return Seats[i].Seats;
            }
            return 0;
        }

        /// <summary>
        /// A stable one-line rendering, invariant-culture and round-trip formatted. Exists so the
        /// determinism suite can hash an allocation instead of asserting field by field — the field a
        /// hand-written assertion forgets is exactly where a desync hides (§12).
        /// </summary>
        public string ToCanonicalString()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("method=").Append(Method);
            sb.Append(";totalSeats=").Append(TotalSeats.ToString(CultureInfo.InvariantCulture));
            sb.Append(";totalVotes=").Append(TotalVotes.ToString(CultureInfo.InvariantCulture));
            sb.Append(";qualifyingVotes=").Append(QualifyingVotes.ToString(CultureInfo.InvariantCulture));
            sb.Append(";threshold=").Append(ThresholdShare.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(";waived=").Append(ThresholdWaived ? "1" : "0");
            sb.Append(";lsq=").Append(Disproportionality.ToString("R", CultureInfo.InvariantCulture));

            sb.Append(";seats=[");
            for (int i = 0; i < Seats.Count; i++)
            {
                SeatAllocation a = Seats[i];
                if (i > 0) sb.Append(',');
                sb.Append(a.PartyId).Append(':')
                  .Append(a.Seats.ToString(CultureInfo.InvariantCulture)).Append('/')
                  .Append(a.DistrictSeats.ToString(CultureInfo.InvariantCulture)).Append('/')
                  .Append(a.ListSeats.ToString(CultureInfo.InvariantCulture)).Append('/')
                  .Append(a.SeatShare.ToString("R", CultureInfo.InvariantCulture)).Append('/')
                  .Append(a.VoteShare.ToString("R", CultureInfo.InvariantCulture)).Append('/')
                  .Append(a.PassedThreshold ? "1" : "0");
            }
            sb.Append(']');

            sb.Append(";ties=[");
            for (int i = 0; i < TieBreaks.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(TieBreaks[i].ToString());
            }
            sb.Append(']');
            return sb.ToString();
        }

        public override string ToString() => ToCanonicalString();
    }
}
