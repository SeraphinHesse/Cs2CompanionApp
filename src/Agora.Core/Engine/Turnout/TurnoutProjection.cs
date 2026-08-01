using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Turnout
{
    /// <summary>
    /// One district's projected turnout, and the blocs it was built from.
    /// </summary>
    /// <remarks>
    /// <see cref="Turnout"/> is deliberately derived from the integer head counts rather than from a
    /// weighted mean of the bloc rates. The election packet counts <see cref="ProjectedVotes"/>, so
    /// if the reported rate were computed any other way the two would disagree by a vote or two in
    /// exactly the close races where that matters.
    /// </remarks>
    public sealed class DistrictTurnout
    {
        private readonly Dictionary<int, BlocTurnout> _byBloc;

        /// <summary>Matches <see cref="DistrictSnapshot.Id"/>.</summary>
        public string DistrictId { get; }

        /// <summary><see cref="ProjectedVotes"/> / <see cref="EligibleVoters"/>, 0–1. Zero when nobody is eligible.</summary>
        public double Turnout { get; }

        /// <summary>Sum of the blocs' eligible head counts.</summary>
        public int EligibleVoters { get; }

        /// <summary>Sum of the blocs' projected votes. Whole voters, never a fraction.</summary>
        public int ProjectedVotes { get; }

        /// <summary>
        /// How close this district's race is, 0–1: the runner-up's share divided by the leader's.
        /// 1 is a dead heat, 0 an uncontested seat. Kept for the dashboard and for the poll packet,
        /// which under-samples low-turnout districts.
        /// </summary>
        public double Competitiveness { get; }

        /// <summary>Per-bloc detail, sorted by <see cref="BlocKey.Ordinal"/> ascending.</summary>
        public IReadOnlyList<BlocTurnout> Blocs { get; }

        internal DistrictTurnout(string districtId, double turnout, int eligibleVoters, int projectedVotes,
                                 double competitiveness, IReadOnlyList<BlocTurnout> blocs)
        {
            DistrictId = districtId;
            Turnout = turnout;
            EligibleVoters = eligibleVoters;
            ProjectedVotes = projectedVotes;
            Competitiveness = competitiveness;
            Blocs = blocs;

            // Lookup only — never enumerated, so its ordering cannot leak into engine state.
            _byBloc = new Dictionary<int, BlocTurnout>(blocs.Count);
            for (int i = 0; i < blocs.Count; i++)
            {
                int ordinal = blocs[i].Bloc.Ordinal;
                if (!_byBloc.ContainsKey(ordinal)) _byBloc[ordinal] = blocs[i];
            }
        }

        /// <summary>O(1) lookup of one bloc's projection.</summary>
        public bool TryGetBloc(BlocKey key, out BlocTurnout? result)
        {
            if (_byBloc.TryGetValue(key.Ordinal, out BlocTurnout found))
            {
                result = found;
                return true;
            }

            result = null;
            return false;
        }

        /// <summary>This bloc's projected rate, or 0 if the bloc is not present.</summary>
        public double RateFor(BlocKey key) =>
            _byBloc.TryGetValue(key.Ordinal, out BlocTurnout found) ? found.Turnout : 0.0;
    }

    /// <summary>
    /// The whole city's projected turnout at one date — the output of packet 5.
    ///
    /// <para>
    /// Consumed by the polling packet (which biases published shares against low-turnout districts)
    /// and by the election packet (which allocates seats from the integer vote counts). Both read the
    /// same object, so a poll and the election it precedes can never be built on different turnout.
    /// </para>
    /// </summary>
    public sealed class TurnoutProjection
    {
        private readonly Dictionary<string, DistrictTurnout> _byDistrict;

        public SimDate Date { get; }

        /// <summary><see cref="TotalProjectedVotes"/> / <see cref="TotalEligibleVoters"/>, 0–1.</summary>
        public double CityTurnout { get; }

        public int TotalEligibleVoters { get; }

        public int TotalProjectedVotes { get; }

        /// <summary>Sorted by <see cref="DistrictTurnout.DistrictId"/>, ordinal ascending.</summary>
        public IReadOnlyList<DistrictTurnout> Districts { get; }

        internal TurnoutProjection(SimDate date, double cityTurnout, int totalEligible, int totalVotes,
                                   IReadOnlyList<DistrictTurnout> districts)
        {
            Date = date;
            CityTurnout = cityTurnout;
            TotalEligibleVoters = totalEligible;
            TotalProjectedVotes = totalVotes;
            Districts = districts;

            _byDistrict = new Dictionary<string, DistrictTurnout>(districts.Count, StringComparer.Ordinal);
            for (int i = 0; i < districts.Count; i++)
            {
                if (!_byDistrict.ContainsKey(districts[i].DistrictId))
                    _byDistrict[districts[i].DistrictId] = districts[i];
            }
        }

        /// <summary>O(1) lookup by district id.</summary>
        public bool TryGetDistrict(string districtId, out DistrictTurnout? result)
        {
            if (districtId != null && _byDistrict.TryGetValue(districtId, out DistrictTurnout found))
            {
                result = found;
                return true;
            }

            result = null;
            return false;
        }

        /// <summary>That district's projected rate, or 0 if it is not present.</summary>
        public double TurnoutFor(string districtId) =>
            districtId != null && _byDistrict.TryGetValue(districtId, out DistrictTurnout found)
                ? found.Turnout
                : 0.0;
    }
}
