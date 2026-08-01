using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Turnout
{
    /// <summary>
    /// Everything the turnout model is allowed to see. Packet 5 (<c>politicsmodplan.md</c> §4.3,
    /// §3 Campaigns) is a pure function of this object plus <c>engine_tuning.json</c> — there is no
    /// hidden state anywhere in the packet, because turnout feeds both poll error and seat
    /// allocation and a stateful turnout model would make both of those irreproducible.
    ///
    /// <para>
    /// Note what is <em>not</em> here: no <see cref="CitySnapshot"/>. Happiness, education, wealth and
    /// discontent all arrive already attributed to a bloc by the bloc packet, so turnout never
    /// re-reads the raw city metrics and the two packets cannot disagree about what a district's
    /// happiness was.
    /// </para>
    /// </summary>
    public sealed class TurnoutInputs
    {
        /// <summary>Save identity, for <c>SeedStreams</c>. Never <see cref="Guid.NewGuid"/>.</summary>
        public Guid SaveGuid { get; set; }

        /// <summary>The sim date the projection belongs to. Part of every seed.</summary>
        public SimDate Date { get; set; }

        /// <summary>
        /// Every bloc in the city, including the disenfranchised child and teen bands. Order is not
        /// trusted: the model sorts by district id (ordinal) then <see cref="BlocKey.Ordinal"/> before
        /// it sums anything.
        /// </summary>
        public IReadOnlyList<Bloc> Blocs { get; set; } = new List<Bloc>();

        /// <summary>
        /// Current standing per district — the last election's result, or the district block of the
        /// latest poll wrapped in the same type. Only <see cref="DistrictResult.DistrictId"/> and
        /// <see cref="DistrictResult.Shares"/> are read; the rest is ignored. Empty before the first
        /// election, in which case <see cref="CityStandings"/> is used instead.
        /// </summary>
        public IReadOnlyList<DistrictResult> DistrictStandings { get; set; } = new List<DistrictResult>();

        /// <summary>
        /// City-wide standing, used for districts absent from <see cref="DistrictStandings"/>. Empty
        /// on a fresh save, which reads as an uncontested race (competitiveness 0).
        /// </summary>
        public IReadOnlyList<PartyVoteShare> CityStandings { get; set; } = new List<PartyVoteShare>();

        /// <summary>
        /// How hard the campaign is being fought, 0–1. Supplied by the campaign/polling packet (it
        /// owns the term calendar); clamped here. 0 outside campaign season.
        /// </summary>
        public double CampaignIntensity { get; set; }

        /// <summary>
        /// True for an election called mid-term. Costs <c>turnout.snapElectionPenalty</c>: a race
        /// nobody had scheduled draws fewer voters.
        /// </summary>
        public bool IsSnapElection { get; set; }

        /// <summary>
        /// Consecutive completed terms served by the current incumbent. Each one costs
        /// <c>turnout.incumbentTermFatigue</c>. Negative values are treated as 0.
        /// </summary>
        public int IncumbentConsecutiveTerms { get; set; }
    }
}
