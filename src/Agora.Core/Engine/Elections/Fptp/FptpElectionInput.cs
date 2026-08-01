using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Elections.Fptp
{
    /// <summary>
    /// Everything <see cref="FptpElection.Run"/> needs. Frozen contract types only — the packet has
    /// no state, no services and no knowledge of who built the blocs or scored the affinities.
    /// </summary>
    /// <remarks>
    /// Deliberately does not take a <see cref="CitySnapshot"/>. The district list, the electorate and
    /// the vote are all recoverable from the turnout and affinity sets, so taking the snapshot as well
    /// would create a second source of truth for "which districts exist" — and the two would disagree
    /// the first time a district appeared mid-cycle.
    /// </remarks>
    public sealed class FptpElectionInput
    {
        /// <summary>The save's identity. Every draw is seeded from this plus <see cref="Date"/>.</summary>
        public Guid SaveGuid { get; set; }

        /// <summary>Polling day.</summary>
        public SimDate Date { get; set; }

        /// <summary>
        /// Stable election id. Left empty, the packet derives <c>"election-YYYY-MM"</c>.
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>Sequential term this election opens, starting at 1.</summary>
        public int TermNumber { get; set; } = 1;

        /// <summary>True when a coalition collapse rather than the calendar forced the vote.</summary>
        public bool IsSnapElection { get; set; }

        /// <summary>
        /// Every party the registry knows about. The packet filters to a ballot itself: dissolved and
        /// merged parties are dropped, as are parties founded after <see cref="Date"/>.
        /// </summary>
        public IReadOnlyList<Party> Parties { get; set; } = new List<Party>();

        /// <summary>
        /// Bloc affinities, one per (district, bloc, party). Order is irrelevant — the packet indexes
        /// them and iterates in <see cref="BlocAxes.AllKeys"/> order.
        /// </summary>
        public IReadOnlyList<BlocAffinity> Affinities { get; set; } = new List<BlocAffinity>();

        /// <summary>
        /// Bloc turnout, one per (district, bloc). <see cref="BlocTurnout.ProjectedVotes"/> is the
        /// electorate that actually shows up; blocs with none are counted toward eligibility and
        /// ignored in the count.
        /// </summary>
        public IReadOnlyList<BlocTurnout> Turnouts { get; set; } = new List<BlocTurnout>();

        /// <summary>
        /// Party of the sitting mayor, for <c>electionsFptp.incumbentMayorBonus</c>. Null before the
        /// first mayoral election, and null after a mayor's party dissolves.
        /// </summary>
        public string? IncumbentMayorPartyId { get; set; }

        /// <summary>
        /// The last published poll, used only to report <see cref="ElectionResult.FinalPollDeviation"/>.
        /// It never feeds the count — polls are a lagging report of the model, not an input to it.
        /// </summary>
        public PollResult? FinalPoll { get; set; }
    }
}
