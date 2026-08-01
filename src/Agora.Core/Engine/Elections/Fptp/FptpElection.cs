using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Elections.Fptp
{
    /// <summary>
    /// Packet 8 — the NA first-past-the-post election: one contest per district plus a directly
    /// elected mayor.
    ///
    /// <para>
    /// One entry point, <see cref="Run"/>. Frozen contract types in, an <see cref="ElectionResult"/>
    /// out, and every coefficient from <see cref="EngineTuning"/>. No state is held between calls, so
    /// two calls with the same inputs are indistinguishable — which is the whole determinism claim
    /// (§2.3) reduced to something a test can falsify.
    /// </para>
    ///
    /// <para><b>The count, in order.</b></para>
    /// <list type="number">
    /// <item><b>Ballot.</b> Parties that exist on polling day and that the voter model scored.</item>
    /// <item><b>Bloc shares.</b> Each bloc's affinities become shares through a softmax at
    /// <c>affinity.softmaxTemperature</c>, weighted by that bloc's projected votes. Summing over the
    /// blocs of a district gives its baseline; summing over districts gives the city baseline.</item>
    /// <item><b>Mayor.</b> The city baseline plus <c>incumbentMayorBonus</c>, squeezed, and — if
    /// <c>mayorRunoffThreshold</c> is set and nobody clears it — settled in a runoff.</item>
    /// <item><b>Districts.</b> Baseline, plus a seeded per-district swing, plus coattails carrying
    /// <c>straightTicketFactor</c> of the mayoral candidate's over-performance down the ticket, then
    /// the tactical squeeze.</item>
    /// <item><b>Counting.</b> Shares become whole votes by largest remainder, so the per-party counts
    /// sum exactly to votes cast and a one-vote margin exists.</item>
    /// <item><b>Seats.</b> Each district's winner takes its seats; an at-large top-up fills the
    /// chamber to <c>minCouncilSeats</c> from the city-wide popular vote.</item>
    /// </list>
    ///
    /// <para>
    /// The mayoral race is computed <i>before</i> the districts on purpose. Coattails run downward in
    /// a real NA ballot — a strong mayoral candidate lifts the council ticket — and computing the
    /// council first would force the influence to run the wrong way.
    /// </para>
    /// </summary>
    public static class FptpElection
    {
        /// <summary>Separates composite seed entity ids. Never appears in a district or party id.</summary>
        private const string EntitySeparator = "|";

        /// <summary>
        /// Runs one first-past-the-post general election.
        /// </summary>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        public static ElectionResult Run(FptpElectionInput input, EngineTuning tuning)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            ElectionsFptpTuning fptp = tuning.ElectionsFptp;
            AffinityTuning affinity = tuning.Affinity;

            // AGORA-SEAM(§14.1): electionsFptp.primariesEnabled is an open decision, pinned false in
            // both the tuning file and the schema. When it closes, each party's mayoral and district
            // candidates get picked here — before the ballot is fixed — by a primary contested
            // between that party's factions. Until then the dominant faction (Faction.IsDominant)
            // stands in for a primary winner and no nomination contest is simulated. Deliberately no
            // branch on the flag: a half-built primary is worse than none.

            string[] ballot = BuildBallot(input);
            string[] districtIds = BuildDistrictIds(input);
            int partyCount = ballot.Length;
            int districtCount = districtIds.Length;

            if (partyCount == 0 || districtCount == 0)
                return EmptyResult(input, tuning, ballot);

            var partyIndex = BuildIndex(ballot);
            var districtIndex = BuildIndex(districtIds);

            double[] affinityGrid = BuildAffinityGrid(input, partyIndex, districtIndex, partyCount);
            int[] votesGrid = new int[districtCount * BlocAxes.BlocCount];
            int[] eligibleGrid = new int[districtCount * BlocAxes.BlocCount];
            FillTurnoutGrids(input, districtIndex, votesGrid, eligibleGrid);

            // --- Baselines -----------------------------------------------------------------------

            var districtBase = new double[districtCount][];
            var districtVotes = new int[districtCount];
            var districtEligible = new int[districtCount];
            var cityWeighted = new double[partyCount];
            long cityVotesTotal = 0;
            long cityEligibleTotal = 0;

            for (int d = 0; d < districtCount; d++)
            {
                var weighted = new double[partyCount];
                int votes = 0;
                int eligible = 0;

                // BlocAxes.AllKeys, never a dictionary walk: the summation order of these doubles is
                // part of the determinism contract, so the same city must always add them up the same
                // way to produce a bit-identical share.
                for (int k = 0; k < BlocAxes.BlocCount; k++)
                {
                    int cell = d * BlocAxes.BlocCount + k;

                    int cellEligible = eligibleGrid[cell];
                    if (cellEligible > 0) eligible += cellEligible;

                    int cellVotes = votesGrid[cell];
                    if (cellVotes <= 0) continue;

                    var scores = new double[partyCount];
                    for (int p = 0; p < partyCount; p++)
                    {
                        double a = affinityGrid[cell * partyCount + p];
                        // An unscored party gets the neutral baseline rather than zero: zero is a
                        // meaningful affinity, and treating "no record" as active hostility would let
                        // a missing row swing a seat.
                        scores[p] = double.IsNaN(a) ? affinity.BaseAffinity : a;
                    }

                    double[] blocShares = FptpShareMath.Softmax(scores, affinity.SoftmaxTemperature);
                    for (int p = 0; p < partyCount; p++)
                        weighted[p] += blocShares[p] * cellVotes;

                    votes += cellVotes;
                }

                for (int p = 0; p < partyCount; p++) cityWeighted[p] += weighted[p];

                if (votes > 0)
                {
                    for (int p = 0; p < partyCount; p++) weighted[p] /= votes;
                }
                else
                {
                    double even = 1.0 / partyCount;
                    for (int p = 0; p < partyCount; p++) weighted[p] = even;
                }

                districtBase[d] = weighted;
                districtVotes[d] = votes;
                districtEligible[d] = eligible;
                cityVotesTotal += votes;
                cityEligibleTotal += eligible;
            }

            var cityBase = new double[partyCount];
            if (cityVotesTotal > 0)
            {
                for (int p = 0; p < partyCount; p++) cityBase[p] = cityWeighted[p] / cityVotesTotal;
            }
            FptpShareMath.Normalize(cityBase);

            // --- The mayoral race ----------------------------------------------------------------

            var mayorFirstRound = (double[])cityBase.Clone();

            if (!string.IsNullOrEmpty(input.IncumbentMayorPartyId) &&
                partyIndex.TryGetValue(input.IncumbentMayorPartyId!, out int incumbent))
            {
                // Additive on the share, not multiplicative: a personal-vote bonus is worth roughly
                // the same number of points to a small candidate as to a large one.
                mayorFirstRound[incumbent] += fptp.IncumbentMayorBonus;
            }

            FptpShareMath.Normalize(mayorFirstRound);
            FptpShareMath.ApplyTacticalSqueeze(mayorFirstRound, fptp.ThirdPartyPenalty,
                                               affinity.TacticalVotingThresholdFptp);
            FptpShareMath.Normalize(mayorFirstRound);
            FptpShareMath.ZeroTinyShares(mayorFirstRound, affinity.MinPartyShare);

            double[] mayorFinal = ResolveMayoralRunoff(mayorFirstRound, ballot, input, fptp);

            int mayorWinner = PickMayor(mayorFinal, input, fptp, cityVotesTotal);

            // --- District contests ---------------------------------------------------------------

            var districts = new List<DistrictResult>(districtCount);
            var cityVotes = new long[partyCount];
            var districtSeatsWon = new int[partyCount];
            FptpChamber chamber = FptpSeatMath.Chamber(districtCount, tuning);
            int totalVotesCast = 0;
            int totalEligible = cityEligibleTotal > int.MaxValue ? int.MaxValue : (int)cityEligibleTotal;

            for (int d = 0; d < districtCount; d++)
            {
                string districtId = districtIds[d];
                var shares = (double[])districtBase[d].Clone();
                int votesCast = districtVotes[d];

                if (votesCast > 0)
                {
                    // One sub-stream per (district, party). Drawing every party's swing from a single
                    // per-district stream would couple each party's draw to its ballot position, so
                    // adding a party would silently redraw every other party's swing in that district.
                    for (int p = 0; p < partyCount; p++)
                    {
                        DeterministicRng swing = SeedStreams.RngFor(
                            input.SaveGuid, input.Date, StreamNames.ElectionDistrictSwing,
                            districtId + EntitySeparator + ballot[p]);

                        shares[p] += swing.NextGaussian() * fptp.DistrictSwingSigma;
                    }

                    FptpShareMath.Normalize(shares);

                    // Coattails: the mayoral candidate's over- or under-performance against the city
                    // baseline transfers down the ticket at straightTicketFactor. Transferring the
                    // mayoral *share* instead would flatten every district onto the city result and
                    // erase the district geography that FPTP exists to express.
                    for (int p = 0; p < partyCount; p++)
                        shares[p] += fptp.StraightTicketFactor * (mayorFirstRound[p] - cityBase[p]);

                    FptpShareMath.Normalize(shares);

                    FptpShareMath.ApplyTacticalSqueeze(shares, fptp.ThirdPartyPenalty,
                                                       affinity.TacticalVotingThresholdFptp);
                    FptpShareMath.Normalize(shares);
                    FptpShareMath.ZeroTinyShares(shares, affinity.MinPartyShare);
                }

                DeterministicRng roundingRng = SeedStreams.RngFor(
                    input.SaveGuid, input.Date, StreamNames.ElectionTieBreak,
                    districtId + EntitySeparator + "rounding");

                int[] votes = FptpShareMath.Apportion(shares, votesCast, roundingRng);

                var reported = new double[partyCount];
                for (int p = 0; p < partyCount; p++)
                    reported[p] = votesCast > 0 ? votes[p] / (double)votesCast : shares[p];

                bool decidedByTieBreak;
                int winner = PickDistrictWinner(shares, votes, votesCast, districtId, input, fptp,
                                                out decidedByTieBreak);

                double margin = 0.0;
                if (winner >= 0)
                {
                    double runnerUp = 0.0;
                    for (int p = 0; p < partyCount; p++)
                        if (p != winner && reported[p] > runnerUp) runnerUp = reported[p];

                    margin = reported[winner] - runnerUp;
                    // A tie-broken seat can leave the winner a single vote behind on the reported
                    // share; report that as a zero margin rather than a negative one.
                    if (margin < 0.0) margin = 0.0;

                    districtSeatsWon[winner] += chamber.SeatsPerDistrict;
                }

                for (int p = 0; p < partyCount; p++) cityVotes[p] += votes[p];
                totalVotesCast += votesCast;

                districts.Add(new DistrictResult
                {
                    DistrictId = districtId,
                    Shares = ToShareList(ballot, reported),
                    Turnout = districtEligible[d] > 0 ? votesCast / (double)districtEligible[d] : 0.0,
                    VotesCast = votesCast,
                    EligibleVoters = districtEligible[d],
                    WinningPartyId = winner >= 0 ? ballot[winner] : "",
                    Margin = margin,
                    Seats = winner >= 0 ? chamber.SeatsPerDistrict : 0,
                    DecidedByTieBreak = decidedByTieBreak
                });
            }

            // --- City-wide count and seats -------------------------------------------------------

            var cityShares = new double[partyCount];
            if (totalVotesCast > 0)
            {
                for (int p = 0; p < partyCount; p++)
                    cityShares[p] = cityVotes[p] / (double)totalVotesCast;
            }

            int[] atLargeSeats;
            if (chamber.AtLargeSeats > 0 && totalVotesCast > 0)
            {
                DeterministicRng atLargeRng = SeedStreams.RngFor(
                    input.SaveGuid, input.Date, StreamNames.ElectionTieBreak, "council.at-large");
                atLargeSeats = FptpShareMath.Apportion(cityShares, chamber.AtLargeSeats, atLargeRng);
            }
            else
            {
                atLargeSeats = new int[partyCount];
            }

            int totalSeats = 0;
            for (int p = 0; p < partyCount; p++) totalSeats += districtSeatsWon[p] + atLargeSeats[p];

            var seats = new List<SeatAllocation>(partyCount);
            for (int p = 0; p < partyCount; p++)
            {
                int won = districtSeatsWon[p] + atLargeSeats[p];
                seats.Add(new SeatAllocation(
                    ballot[p],
                    won,
                    totalSeats > 0 ? won / (double)totalSeats : 0.0,
                    cityShares[p],
                    districtSeatsWon[p],
                    atLargeSeats[p],
                    // FPTP has no legal threshold — the tactical squeeze is the de-facto one, and it
                    // is already priced into the shares. Reporting a party as having failed a
                    // threshold that does not exist would be a lie the dashboard would repeat.
                    true));
            }

            return new ElectionResult
            {
                SchemaVersion = 1,
                Id = string.IsNullOrEmpty(input.Id) ? DeriveId(input.Date) : input.Id,
                Date = input.Date,
                System = ElectoralSystem.FirstPastThePost,
                TermNumber = input.TermNumber,
                IsSnapElection = input.IsSnapElection,
                PartyIdsOnBallot = new List<string>(ballot),
                CityVoteShares = ToShareList(ballot, cityShares),
                Districts = districts,
                Seats = seats,
                TotalSeats = totalSeats,
                Turnout = totalEligible > 0 ? totalVotesCast / (double)totalEligible : 0.0,
                TotalVotesCast = totalVotesCast,
                TotalEligibleVoters = totalEligible,
                MayorPartyId = mayorWinner >= 0 ? ballot[mayorWinner] : null,
                MayorName = null,   // flavor-owned (non-negotiable #1); IFlavorProvider fills it in
                MayorVoteShares = ToShareList(ballot, mayorFinal),
                FinalPollDeviation = PollDeviation(input.FinalPoll, ballot, cityShares),
                NextElectionDate = FptpCalendar.NextElection(input.Date, tuning)
            };
        }

        // --- Ballot and indexing ------------------------------------------------------------------

        /// <summary>
        /// Party ids contesting the election, sorted ordinal ascending — the contractual order for
        /// every <see cref="PartyVoteShare"/> list, which is why every array in this file is indexed
        /// by ballot position.
        /// </summary>
        private static string[] BuildBallot(FptpElectionInput input)
        {
            var existing = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            IReadOnlyList<Party> parties = input.Parties;
            for (int i = 0; i < parties.Count; i++)
            {
                Party p = parties[i];
                if (p == null || string.IsNullOrEmpty(p.Id)) continue;
                if (p.Status == PartyStatus.Dissolved || p.Status == PartyStatus.Merged) continue;
                if (p.DissolvedDate.HasValue && p.DissolvedDate.Value <= input.Date) continue;
                if (p.FoundedDate > input.Date) continue;
                if (!seen.Add(p.Id)) continue;

                existing.Add(p.Id);
            }

            // Only parties the voter model actually scored can be counted. A party with no affinity
            // anywhere would otherwise inherit the neutral baseline in every bloc and poll like a
            // serious contender purely by existing.
            var scored = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<BlocAffinity> affinities = input.Affinities;
            for (int i = 0; i < affinities.Count; i++)
            {
                BlocAffinity a = affinities[i];
                if (a != null && !string.IsNullOrEmpty(a.PartyId)) scored.Add(a.PartyId);
            }

            var onBallot = new List<string>();
            for (int i = 0; i < existing.Count; i++)
                if (scored.Contains(existing[i])) onBallot.Add(existing[i]);

            if (onBallot.Count == 0) onBallot = existing;

            onBallot.Sort(StringComparer.Ordinal);
            return onBallot.ToArray();
        }

        /// <summary>District ids in the union of the turnout and affinity sets, sorted ordinal ascending.</summary>
        private static string[] BuildDistrictIds(FptpElectionInput input)
        {
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            IReadOnlyList<BlocTurnout> turnouts = input.Turnouts;
            for (int i = 0; i < turnouts.Count; i++)
            {
                BlocTurnout t = turnouts[i];
                if (t != null && !string.IsNullOrEmpty(t.DistrictId) && seen.Add(t.DistrictId))
                    ids.Add(t.DistrictId);
            }

            IReadOnlyList<BlocAffinity> affinities = input.Affinities;
            for (int i = 0; i < affinities.Count; i++)
            {
                BlocAffinity a = affinities[i];
                if (a != null && !string.IsNullOrEmpty(a.DistrictId) && seen.Add(a.DistrictId))
                    ids.Add(a.DistrictId);
            }

            ids.Sort(StringComparer.Ordinal);
            return ids.ToArray();
        }

        private static Dictionary<string, int> BuildIndex(string[] ids)
        {
            var map = new Dictionary<string, int>(ids.Length, StringComparer.Ordinal);
            for (int i = 0; i < ids.Length; i++) map[ids[i]] = i;
            return map;
        }

        /// <summary>
        /// Dense (district × bloc × party) affinity grid, NaN where no record exists.
        /// </summary>
        /// <remarks>
        /// The dictionaries are built once and only ever probed. Nothing iterates them, so no output
        /// can depend on hash order — the failure mode §2.3 calls out by name.
        /// </remarks>
        private static double[] BuildAffinityGrid(FptpElectionInput input,
                                                  Dictionary<string, int> partyIndex,
                                                  Dictionary<string, int> districtIndex,
                                                  int partyCount)
        {
            var grid = new double[districtIndex.Count * BlocAxes.BlocCount * partyCount];
            for (int i = 0; i < grid.Length; i++) grid[i] = double.NaN;

            IReadOnlyList<BlocAffinity> affinities = input.Affinities;
            for (int i = 0; i < affinities.Count; i++)
            {
                BlocAffinity a = affinities[i];
                if (a == null) continue;
                if (!districtIndex.TryGetValue(a.DistrictId ?? "", out int d)) continue;
                if (!partyIndex.TryGetValue(a.PartyId ?? "", out int p)) continue;

                int ordinal = a.Bloc.Ordinal;
                if (ordinal < 0 || ordinal >= BlocAxes.BlocCount) continue;

                grid[(d * BlocAxes.BlocCount + ordinal) * partyCount + p] = a.Affinity;
            }

            return grid;
        }

        private static void FillTurnoutGrids(FptpElectionInput input,
                                             Dictionary<string, int> districtIndex,
                                             int[] votesGrid, int[] eligibleGrid)
        {
            IReadOnlyList<BlocTurnout> turnouts = input.Turnouts;
            for (int i = 0; i < turnouts.Count; i++)
            {
                BlocTurnout t = turnouts[i];
                if (t == null) continue;
                if (!districtIndex.TryGetValue(t.DistrictId ?? "", out int d)) continue;

                int ordinal = t.Bloc.Ordinal;
                if (ordinal < 0 || ordinal >= BlocAxes.BlocCount) continue;

                int cell = d * BlocAxes.BlocCount + ordinal;

                // First record wins. A duplicate (district, bloc) pair is a caller defect; taking the
                // first keeps the result a function of the input list rather than of which duplicate
                // happened to be written last.
                if (eligibleGrid[cell] != 0 || votesGrid[cell] != 0) continue;

                int eligible = t.EligibleVoters > 0 ? t.EligibleVoters : 0;
                int votes = t.ProjectedVotes > 0 ? t.ProjectedVotes : 0;
                if (votes > eligible) votes = eligible;   // turnout cannot exceed the electorate

                eligibleGrid[cell] = eligible;
                votesGrid[cell] = votes;
            }
        }

        // --- The mayoral race ----------------------------------------------------------------------

        /// <summary>
        /// Runs a runoff when <c>mayorRunoffThreshold</c> is set and the leader misses it. Ships
        /// dormant: the threshold is 0.0, so plurality wins outright and this returns its input.
        /// </summary>
        /// <remarks>
        /// Eliminated candidates' support transfers to whichever finalist is ideologically closer,
        /// in proportion to <c>1 - IssuePosition.Distance</c>. No coefficient governs the split
        /// because none is needed: proximity is already normalised to [0,1] and the two finalists'
        /// proximities are the only quantities in play. Every vote transfers — runoff abstention is
        /// not modelled, and inventing a drop-off rate would mean inventing a tuning key for a path
        /// the shipped tuning never takes.
        /// </remarks>
        private static double[] ResolveMayoralRunoff(double[] firstRound, string[] ballot,
                                                     FptpElectionInput input, ElectionsFptpTuning fptp)
        {
            int n = firstRound.Length;
            if (n < 3 || !(fptp.MayorRunoffThreshold > 0.0)) return firstRound;

            int[] order = FptpShareMath.RankOrder(firstRound);
            if (firstRound[order[0]] >= fptp.MayorRunoffThreshold) return firstRound;

            int first = order[0];
            int second = order[1];

            IssuePosition firstPlatform = PlatformOf(input, ballot[first]);
            IssuePosition secondPlatform = PlatformOf(input, ballot[second]);

            var final = new double[n];
            final[first] = firstRound[first];
            final[second] = firstRound[second];

            for (int p = 0; p < n; p++)
            {
                if (p == first || p == second) continue;

                double transferable = firstRound[p];
                if (transferable <= 0.0) continue;

                IssuePosition eliminated = PlatformOf(input, ballot[p]);
                double toFirst = 1.0 - eliminated.Distance(firstPlatform);
                double toSecond = 1.0 - eliminated.Distance(secondPlatform);
                double total = toFirst + toSecond;

                if (total <= 0.0)
                {
                    final[first] += transferable * 0.5;
                    final[second] += transferable * 0.5;
                }
                else
                {
                    final[first] += transferable * (toFirst / total);
                    final[second] += transferable * (toSecond / total);
                }
            }

            FptpShareMath.Normalize(final);
            return final;
        }

        private static IssuePosition PlatformOf(FptpElectionInput input, string partyId)
        {
            IReadOnlyList<Party> parties = input.Parties;
            for (int i = 0; i < parties.Count; i++)
            {
                Party p = parties[i];
                if (p != null && string.Equals(p.Id, partyId, StringComparison.Ordinal))
                    return p.Platform;
            }

            return IssuePosition.Centre;
        }

        /// <summary>
        /// The mayor: whoever leads the final round. An exact tie inside
        /// <c>electionsFptp.tieMarginEpsilon</c> goes to the <c>election.tiebreak</c> stream — never a
        /// coin flip in place, never the alphabetically first party.
        /// </summary>
        private static int PickMayor(double[] mayorShares, FptpElectionInput input,
                                     ElectionsFptpTuning fptp, long cityVotesTotal)
        {
            int n = mayorShares.Length;
            if (n == 0 || cityVotesTotal <= 0) return -1;

            int[] order = FptpShareMath.RankOrder(mayorShares);
            double top = mayorShares[order[0]];

            var tied = new List<int>();
            for (int p = 0; p < n; p++)
                if (top - mayorShares[p] <= fptp.TieMarginEpsilon) tied.Add(p);

            if (tied.Count <= 1) return order[0];

            DeterministicRng rng = SeedStreams.RngFor(
                input.SaveGuid, input.Date, StreamNames.ElectionTieBreak, "mayor");
            return tied[rng.NextInt(0, tied.Count)];
        }

        // --- District winners ----------------------------------------------------------------------

        /// <summary>
        /// The district winner, and whether the tie-break stream decided it.
        /// </summary>
        /// <remarks>
        /// Two distinct ties exist and both must be handled. A <i>modelled</i> tie is two parties
        /// within <c>tieMarginEpsilon</c> of each other before rounding — the genuinely dead heat. A
        /// <i>counted</i> tie is equal whole votes after rounding. Deciding only on counted votes
        /// would let largest-remainder rounding quietly award a dead heat, and deciding only on
        /// modelled shares could name a winner with fewer votes than the runner-up.
        /// </remarks>
        private static int PickDistrictWinner(double[] shares, int[] votes, int votesCast,
                                              string districtId, FptpElectionInput input,
                                              ElectionsFptpTuning fptp, out bool decidedByTieBreak)
        {
            decidedByTieBreak = false;

            int n = shares.Length;
            if (n == 0 || votesCast <= 0) return -1;

            int[] order = FptpShareMath.RankOrder(shares);
            bool modelledTie = n >= 2 && (shares[order[0]] - shares[order[1]]) <= fptp.TieMarginEpsilon;

            var tied = new List<int>();
            if (modelledTie)
            {
                double top = shares[order[0]];
                for (int p = 0; p < n; p++)
                    if (top - shares[p] <= fptp.TieMarginEpsilon) tied.Add(p);
            }
            else
            {
                int most = -1;
                for (int p = 0; p < n; p++) if (votes[p] > most) most = votes[p];
                if (most <= 0) return -1;
                for (int p = 0; p < n; p++) if (votes[p] == most) tied.Add(p);
            }

            if (tied.Count == 1) return tied[0];
            if (tied.Count == 0) return -1;

            DeterministicRng rng = SeedStreams.RngFor(
                input.SaveGuid, input.Date, StreamNames.ElectionTieBreak, districtId);

            decidedByTieBreak = true;
            return tied[rng.NextInt(0, tied.Count)];
        }

        // --- Reporting ------------------------------------------------------------------------------

        /// <summary>
        /// Mean absolute deviation between the final published poll and the counted city-wide result.
        /// Reporting only — no number here re-enters engine state.
        /// </summary>
        private static double PollDeviation(PollResult? poll, string[] ballot, double[] cityShares)
        {
            if (poll == null || poll.Shares == null || poll.Shares.Count == 0) return 0.0;
            if (ballot.Length == 0) return 0.0;

            var published = new Dictionary<string, double>(poll.Shares.Count, StringComparer.Ordinal);
            for (int i = 0; i < poll.Shares.Count; i++)
            {
                PartyVoteShare s = poll.Shares[i];
                if (string.IsNullOrEmpty(s.PartyId)) continue;
                if (!published.ContainsKey(s.PartyId)) published[s.PartyId] = s.Share;
            }

            double sum = 0.0;
            for (int p = 0; p < ballot.Length; p++)
            {
                double reported;
                published.TryGetValue(ballot[p], out reported);
                sum += Math.Abs(reported - cityShares[p]);
            }

            return sum / ballot.Length;
        }

        /// <summary>Shares in ballot order, which is already party-id ordinal ascending (§6).</summary>
        private static List<PartyVoteShare> ToShareList(string[] ballot, double[] shares)
        {
            var list = new List<PartyVoteShare>(ballot.Length);
            for (int p = 0; p < ballot.Length; p++)
                list.Add(new PartyVoteShare(ballot[p], shares[p]));
            return list;
        }

        private static string DeriveId(SimDate date) =>
            "election-" + date.Year.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)
            + "-" + date.Month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// A well-formed, empty result for a city with no districts or no parties. Returning null or
        /// throwing would make the caller's fail path the interesting one; an election nobody
        /// contested is a real state and the sidecar has to be able to hold it.
        /// </summary>
        private static ElectionResult EmptyResult(FptpElectionInput input, EngineTuning tuning,
                                                  string[] ballot)
        {
            return new ElectionResult
            {
                SchemaVersion = 1,
                Id = string.IsNullOrEmpty(input.Id) ? DeriveId(input.Date) : input.Id,
                Date = input.Date,
                System = ElectoralSystem.FirstPastThePost,
                TermNumber = input.TermNumber,
                IsSnapElection = input.IsSnapElection,
                PartyIdsOnBallot = new List<string>(ballot),
                CityVoteShares = new List<PartyVoteShare>(),
                Districts = new List<DistrictResult>(),
                Seats = new List<SeatAllocation>(),
                TotalSeats = 0,
                Turnout = 0.0,
                TotalVotesCast = 0,
                TotalEligibleVoters = 0,
                MayorPartyId = null,
                MayorName = null,
                MayorVoteShares = new List<PartyVoteShare>(),
                FinalPollDeviation = 0.0,
                NextElectionDate = FptpCalendar.NextElection(input.Date, tuning)
            };
        }
    }
}
