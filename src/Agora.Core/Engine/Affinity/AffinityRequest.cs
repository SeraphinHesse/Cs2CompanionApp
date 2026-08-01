using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Affinity
{
    /// <summary>
    /// Everything the affinity packet needs to score one tick. A plain input bag: the engine reads it
    /// and never mutates it, so the same request scored twice produces the same result.
    ///
    /// <para>
    /// Deliberately narrow. Affinity is the hot path every electoral packet consumes, so it takes
    /// frozen contract types in and hands frozen contract types back — no snapshot, no political
    /// state, no services. Anything the caller already has to compute (blocs, parties, the current
    /// government) is passed in rather than derived here.
    /// </para>
    /// </summary>
    public sealed class AffinityRequest
    {
        /// <summary>Save identity. Feeds <c>SeedStreams</c>; never a filename, never a new Guid.</summary>
        public Guid SaveGuid { get; set; }

        /// <summary>The tick being scored. The only date the packet knows (non-negotiable #8).</summary>
        public SimDate Date { get; set; }

        /// <summary>
        /// Blocs to score, normally <see cref="PoliticalState.Blocs"/>. Sorted internally by
        /// (district id ordinal, <see cref="BlocKey.Ordinal"/>) so the caller's order cannot leak
        /// into the output.
        /// </summary>
        public IReadOnlyList<Bloc> Blocs { get; set; } = new List<Bloc>();

        /// <summary>
        /// Every known party. Dissolved and merged parties are filtered out here — a brand that is
        /// off the ballot draws no affinity — so callers may pass the whole registry.
        /// </summary>
        public IReadOnlyList<Party> Parties { get; set; } = new List<Party>();

        /// <summary>
        /// Live mandates. Scored against the party in <see cref="Mandate.PartyId"/>; a mandate whose
        /// <see cref="Mandate.IsMeasurementStalled"/> is set is held, never counted against anyone.
        /// </summary>
        public IReadOnlyList<Mandate> Mandates { get; set; } = new List<Mandate>();

        /// <summary>
        /// Events currently live, normally <see cref="PoliticalState.ActiveEvents"/>. Entries that
        /// have not fired yet, or whose <see cref="TimelineEvent.ExpiresDate"/> has passed, are
        /// ignored rather than rejected.
        /// </summary>
        public IReadOnlyList<TimelineEvent> ActiveEvents { get; set; } = new List<TimelineEvent>();

        /// <summary>
        /// The sitting government, or null between elections. Its members carry the incumbency term.
        /// </summary>
        public Coalition? Government { get; set; }

        /// <summary>
        /// City-wide derived indices. Only <see cref="DerivedIndices.DiscontentIndex"/> is read, as
        /// the "national mood" half of the incumbency penalty; the local half comes from
        /// <see cref="Bloc.Discontent"/>, which is already finer-grained than
        /// <see cref="DistrictIndices"/>. Null means "no national signal" — the local one is used for
        /// both halves rather than assuming a contented city.
        /// </summary>
        public DerivedIndices? Indices { get; set; }

        /// <summary>
        /// When the vote in <see cref="Bloc.PreviousVote"/> was cast. Habitual loyalty decays from
        /// this date at <c>affinity.loyaltyDecayPerMonth</c>. Null (no election yet, or an unknown
        /// date) applies loyalty undecayed — blocs with no previous vote score zero loyalty anyway.
        /// </summary>
        public SimDate? LastElectionDate { get; set; }
    }
}
