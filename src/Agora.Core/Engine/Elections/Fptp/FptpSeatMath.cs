using Agora.Core.Tuning;

namespace Agora.Core.Engine.Elections.Fptp
{
    /// <summary>
    /// The shape of a first-past-the-post council for a city with a given number of districts.
    /// </summary>
    public readonly struct FptpChamber
    {
        /// <summary>Districts contesting seats.</summary>
        public int DistrictCount { get; }

        /// <summary>Seats each district awards to its winner, after the maximum-size cap.</summary>
        public int SeatsPerDistrict { get; }

        /// <summary>Seats decided by district contests: <see cref="DistrictCount"/> × <see cref="SeatsPerDistrict"/>.</summary>
        public int DistrictSeats { get; }

        /// <summary>Top-up seats decided by the city-wide popular vote.</summary>
        public int AtLargeSeats { get; }

        /// <summary>Nominal chamber size.</summary>
        public int TotalSeats { get; }

        public FptpChamber(int districtCount, int seatsPerDistrict, int districtSeats,
                           int atLargeSeats, int totalSeats)
        {
            DistrictCount = districtCount;
            SeatsPerDistrict = seatsPerDistrict;
            DistrictSeats = districtSeats;
            AtLargeSeats = atLargeSeats;
            TotalSeats = totalSeats;
        }
    }

    /// <summary>
    /// Chamber sizing for the NA theme. Pure arithmetic over <c>electionsFptp</c>; no randomness.
    /// </summary>
    public static class FptpSeatMath
    {
        /// <summary>
        /// Sizes the council for <paramref name="districtCount"/> districts.
        /// </summary>
        /// <remarks>
        /// Three tuning keys interact and the precedence between them is a decision, so it is stated
        /// here rather than left to whichever branch happens to run first:
        /// <list type="number">
        /// <item>Every district always returns at least one member. A player's district is never left
        /// unrepresented to satisfy a size cap — that would make the council stop describing the map.</item>
        /// <item><c>maxCouncilSeats</c> therefore first bites on <c>councilSeatsPerDistrict</c>,
        /// reducing it (never below 1) until the district seats fit. Only if the district count alone
        /// exceeds the cap is the cap exceeded, and then by exactly the districts' own seats.</item>
        /// <item><c>minCouncilSeats</c> is a floor met with at-large seats, allocated from the
        /// city-wide popular vote. A three-district city otherwise elects a three-member council, in
        /// which one district win is a governing majority.</item>
        /// </list>
        /// </remarks>
        public static FptpChamber Chamber(int districtCount, EngineTuning tuning)
        {
            ElectionsFptpTuning t = tuning.ElectionsFptp;

            int perDistrict = t.CouncilSeatsPerDistrict < 1 ? 1 : t.CouncilSeatsPerDistrict;
            int max = t.MaxCouncilSeats < 1 ? 1 : t.MaxCouncilSeats;
            int min = t.MinCouncilSeats < 0 ? 0 : t.MinCouncilSeats;
            if (min > max) min = max;

            if (districtCount <= 0)
                return new FptpChamber(0, perDistrict, 0, min, min);

            if ((long)districtCount * perDistrict > max)
            {
                perDistrict = max / districtCount;
                if (perDistrict < 1) perDistrict = 1;
            }

            int districtSeats = districtCount * perDistrict;

            int atLarge = min - districtSeats;
            if (atLarge < 0) atLarge = 0;
            if (districtSeats + atLarge > max)
                atLarge = districtSeats >= max ? 0 : max - districtSeats;

            return new FptpChamber(districtCount, perDistrict, districtSeats, atLarge,
                                   districtSeats + atLarge);
        }
    }
}
