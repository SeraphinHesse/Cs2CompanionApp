using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Mod.Core;

namespace Agora.Mod.UiBindings
{
    /// <summary>
    /// Turns engine state into dashboard payloads.
    ///
    /// <para>
    /// One place, rather than a private mapper inside each of the four publishers, because the sort
    /// keys and the empty-value rules in <c>docs/contracts/ui_bindings.md</c> are shared and a second
    /// copy of them is a second thing to get subtly wrong. Nothing here computes politics: every
    /// number is copied from <see cref="PoliticalState"/> or <see cref="CitySnapshot"/>, and the one
    /// arithmetic operation is summing four age bands into a crosstab cell.
    /// </para>
    /// </summary>
    internal static class AgoraUiProjection
    {
        internal const int NewsFeedMax = 40;
        internal const int EventsMax = 25;
        internal const int ElectionHistoryMax = 12;

        private const int DaysPerWeek = 7;

        // ------------------------------------------------------------------ agora.state

        internal static StateSummaryPayload BuildSummary(PoliticalState state)
        {
            var payload = new StateSummaryPayload();
            if (state == null) return payload;

            payload.SchemaVersion = state.SchemaVersion;
            payload.Date = state.Date;
            payload.TermNumber = state.TermNumber;
            payload.System = state.Settings.System.ToString();
            payload.Theme = state.Settings.Theme.ToString();
            payload.NextElectionDate = state.NextElectionDate;
            payload.IsCampaignSeason = state.IsCampaignSeason;
            payload.WeeksToElection = WeeksToElection(state.Date, state.NextElectionDate);
            payload.MayorPartyId = state.MayorPartyId ?? "";
            return payload;
        }

        /// <summary>
        /// Whole weeks from <paramref name="today"/> to the ballot, or -1 when none is scheduled.
        /// </summary>
        /// <remarks>
        /// -1 rather than 0 is contractual: "the election is this week" and "there is no election"
        /// must stay distinguishable, and a panel that renders "0 weeks" for both is exactly the bug
        /// the sentinel exists to prevent.
        /// </remarks>
        private static int WeeksToElection(SimDate today, SimDate? election)
        {
            if (!election.HasValue) return -1;

            // Months are the only unit the political calendar guarantees; approximating a week as
            // 30/7 of a month is enough for a countdown label and cannot drift into engine state.
            int months = today.MonthsUntil(election.Value);
            if (months < 0) return -1;

            return (int)Math.Round(months * 30.0 / DaysPerWeek, MidpointRounding.AwayFromZero);
        }

        // ------------------------------------------------------------------ agora.parties

        /// <summary>The roster, sorted by id ordinal ascending.</summary>
        internal static List<PartyBriefPayload> BuildRoster(PoliticalState state)
        {
            var rows = new List<PartyBriefPayload>();
            if (state == null) return rows;

            for (int i = 0; i < state.Parties.Count; i++)
            {
                Party party = state.Parties[i];
                if (party == null) continue;

                rows.Add(new PartyBriefPayload
                {
                    Id = party.Id,
                    Name = party.Name,
                    ShortName = party.ShortName,
                    ColorHex = party.ColorHex,
                    Status = party.Status.ToString(),
                    IsIncumbent = party.IsIncumbent,
                    IsInGovernment = party.IsInGovernment,
                    CoreGrievance = party.CoreGrievance.ToString(),
                    FoundedDate = party.FoundedDate,
                    DissolvedDate = party.DissolvedDate
                });
            }

            rows.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return rows;
        }

        /// <summary>Factions, by party id, then internal support descending, then id.</summary>
        internal static List<FactionBriefPayload> BuildFactions(PoliticalState state)
        {
            var rows = new List<FactionBriefPayload>();
            if (state == null) return rows;

            for (int i = 0; i < state.Factions.Count; i++)
            {
                Faction faction = state.Factions[i];
                if (faction == null) continue;

                rows.Add(new FactionBriefPayload
                {
                    Id = faction.Id,
                    PartyId = faction.PartyId,
                    Name = faction.Name,
                    ShortName = faction.ShortName,
                    LeaderName = faction.LeaderName,
                    InternalSupport = faction.InternalSupport,
                    IsDominant = faction.IsDominant,
                    TensionWithParty = faction.TensionWithParty,
                    Status = faction.Status.ToString(),
                    CoreGrievance = faction.CoreGrievance.ToString()
                });
            }

            rows.Sort(CompareFactionRows);
            return rows;
        }

        private static int CompareFactionRows(FactionBriefPayload a, FactionBriefPayload b)
        {
            int byParty = string.CompareOrdinal(a.PartyId, b.PartyId);
            if (byParty != 0) return byParty;

            int bySupport = b.InternalSupport.CompareTo(a.InternalSupport);
            if (bySupport != 0) return bySupport;

            return string.CompareOrdinal(a.Id, b.Id);
        }

        // ------------------------------------------------------------------ agora.seats

        /// <summary>Seat rows, sorted by seats descending then party id so ties do not reshuffle.</summary>
        internal static List<SeatRowPayload> BuildAllocation(PoliticalState state)
        {
            var rows = new List<SeatRowPayload>();
            if (state == null || state.ElectionHistory.Count == 0) return rows;

            ElectionResult latest = state.ElectionHistory[state.ElectionHistory.Count - 1];

            for (int i = 0; i < latest.Seats.Count; i++)
            {
                SeatAllocation seat = latest.Seats[i];
                rows.Add(new SeatRowPayload
                {
                    PartyId = seat.PartyId,
                    Seats = seat.Seats,
                    SeatShare = seat.SeatShare,
                    VoteShare = seat.VoteShare,
                    DistrictSeats = seat.DistrictSeats,
                    ListSeats = seat.ListSeats,
                    PassedThreshold = seat.PassedThreshold
                });
            }

            rows.Sort(CompareSeatRows);
            return rows;
        }

        private static int CompareSeatRows(SeatRowPayload a, SeatRowPayload b)
        {
            int bySeats = b.Seats.CompareTo(a.Seats);
            return bySeats != 0 ? bySeats : string.CompareOrdinal(a.PartyId, b.PartyId);
        }

        internal static int TotalSeats(PoliticalState state) =>
            state == null || state.ElectionHistory.Count == 0
                ? 0
                : state.ElectionHistory[state.ElectionHistory.Count - 1].TotalSeats;

        internal static GovernmentSummaryPayload BuildGovernment(PoliticalState state)
        {
            if (state == null || state.Government == null) return null;

            Coalition government = state.Government;

            var payload = new GovernmentSummaryPayload
            {
                Id = government.Id,
                Status = government.Status.ToString(),
                LeadPartyId = government.LeadPartyId,
                MemberPartyIds = SortedCopy(government.MemberPartyIds),
                OppositionPartyIds = SortedCopy(government.OppositionPartyIds),
                Seats = government.Seats,
                SeatShare = government.SeatShare,
                HasMajority = government.HasMajority,
                Cohesion = government.Cohesion,
                Stability = government.Stability,
                CollapseReason = government.CollapseReason.ToString(),
                FormedDate = government.FormedDate,
                EndedDate = government.EndedDate,
                FormationAttempts = government.FormationAttempts,
                ElectionId = government.ElectionId,
                MandateIds = SortedCopy(government.MandateIds)
            };

            return payload;
        }

        internal static MayorSummaryPayload BuildMayor(PoliticalState state)
        {
            if (state == null || string.IsNullOrEmpty(state.MayorPartyId)) return null;
            if (state.ElectionHistory.Count == 0) return null;

            ElectionResult latest = state.ElectionHistory[state.ElectionHistory.Count - 1];

            return new MayorSummaryPayload
            {
                PartyId = state.MayorPartyId,
                Name = latest.MayorName ?? "",
                ElectionId = latest.Id,
                SinceDate = latest.Date,
                Margin = MayorMargin(latest),
                VoteShares = Shares(latest.MayorVoteShares)
            };
        }

        private static double MayorMargin(ElectionResult election)
        {
            double best = -1.0;
            double second = -1.0;

            for (int i = 0; i < election.MayorVoteShares.Count; i++)
            {
                double share = election.MayorVoteShares[i].Share;
                if (share > best) { second = best; best = share; }
                else if (share > second) { second = share; }
            }

            if (best < 0.0) return 0.0;
            return second < 0.0 ? best : best - second;
        }

        internal static ElectionSummaryPayload BuildLastElection(PoliticalState state)
        {
            if (state == null || state.ElectionHistory.Count == 0) return null;

            ElectionResult election = state.ElectionHistory[state.ElectionHistory.Count - 1];

            return new ElectionSummaryPayload
            {
                Id = election.Id,
                Date = election.Date,
                System = election.System.ToString(),
                TermNumber = election.TermNumber,
                IsSnapElection = election.IsSnapElection,
                Turnout = election.Turnout,
                TotalSeats = election.TotalSeats,
                TotalVotesCast = election.TotalVotesCast,
                TotalEligibleVoters = election.TotalEligibleVoters,
                FinalPollDeviation = election.FinalPollDeviation,
                NextElectionDate = election.NextElectionDate,
                CityVoteShares = Shares(election.CityVoteShares)
            };
        }

        /// <summary>Election history, newest first, capped at twelve rows.</summary>
        internal static List<ElectionHistoryRowPayload> BuildHistory(PoliticalState state)
        {
            var rows = new List<ElectionHistoryRowPayload>();
            if (state == null) return rows;

            for (int i = 0; i < state.ElectionHistory.Count; i++)
            {
                ElectionResult election = state.ElectionHistory[i];
                if (election == null) continue;

                rows.Add(new ElectionHistoryRowPayload
                {
                    Id = election.Id,
                    Date = election.Date,
                    TermNumber = election.TermNumber,
                    IsSnapElection = election.IsSnapElection,
                    Turnout = election.Turnout,
                    WinningPartyId = WinnerOf(election),
                    MayorPartyId = election.MayorPartyId ?? "",
                    TotalSeats = election.TotalSeats
                });
            }

            rows.Sort(CompareHistoryRows);
            if (rows.Count > ElectionHistoryMax) rows.RemoveRange(ElectionHistoryMax, rows.Count - ElectionHistoryMax);
            return rows;
        }

        private static int CompareHistoryRows(ElectionHistoryRowPayload a, ElectionHistoryRowPayload b)
        {
            int byDate = b.Date.Value.CompareTo(a.Date.Value);
            return byDate != 0 ? byDate : string.CompareOrdinal(a.Id, b.Id);
        }

        /// <summary>The party with most seats; ties fall to the lowest id so the label is stable.</summary>
        private static string WinnerOf(ElectionResult election)
        {
            string winner = "";
            int best = -1;

            for (int i = 0; i < election.Seats.Count; i++)
            {
                SeatAllocation seat = election.Seats[i];
                if (seat.Seats > best || (seat.Seats == best && string.CompareOrdinal(seat.PartyId, winner) < 0))
                {
                    best = seat.Seats;
                    winner = seat.PartyId;
                }
            }

            return winner;
        }

        /// <summary>
        /// The most recent published poll.
        /// </summary>
        /// <remarks>
        /// <c>PollResult.TrueShares</c> is never read here. It is the model's own answer, and the
        /// published figure is supposed to be a noisy estimate of it — putting both on the bridge
        /// would hand the panel the answer alongside the guess.
        /// </remarks>
        internal static PollSummaryPayload BuildLatestPoll(PoliticalState state)
        {
            if (state == null) return null;

            for (int i = state.RecentPolls.Count - 1; i >= 0; i--)
            {
                PollResult poll = state.RecentPolls[i];
                if (poll == null || !poll.IsPublished) continue;

                return new PollSummaryPayload
                {
                    Id = poll.Id,
                    Date = poll.Date,
                    PollsterId = poll.PollsterId,
                    PollsterName = poll.PollsterName,
                    SampleSize = poll.SampleSize,
                    MarginOfError = poll.MarginOfError,
                    UndecidedShare = poll.UndecidedShare,
                    ProjectedTurnout = poll.ProjectedTurnout,
                    WeeksToElection = poll.ElectionDate.HasValue ? poll.WeeksToElection : -1,
                    ElectionDate = poll.ElectionDate,
                    Shares = Shares(poll.Shares)
                };
            }

            return null;
        }

        internal static List<PartySharePayload> BuildVoteShares(PoliticalState state) =>
            state == null ? new List<PartySharePayload>() : Shares(state.CurrentVoteShares);

        // ------------------------------------------------------------------ agora.districts

        /// <summary>District rows, sorted by id ordinal ascending to match the snapshot.</summary>
        internal static List<DistrictBriefPayload> BuildDistrictList(PoliticalState state, CitySnapshot snapshot)
        {
            var rows = new List<DistrictBriefPayload>();
            if (snapshot == null) return rows;

            for (int i = 0; i < snapshot.Districts.Count; i++)
            {
                DistrictSnapshot district = snapshot.Districts[i];
                if (district == null) continue;

                DistrictResult standing = FindStanding(state, district.Id);
                DistrictIndices indices = FindDistrictIndices(state, district.Id);

                string leader = "";
                string runnerUp = "";
                double leadingShare = 0.0;
                double margin = 0.0;

                if (standing != null)
                {
                    TopTwo(standing.Shares, out leader, out leadingShare, out runnerUp, out double secondShare);
                    margin = runnerUp.Length == 0 ? leadingShare : leadingShare - secondShare;
                }

                rows.Add(new DistrictBriefPayload
                {
                    Id = district.Id,
                    Name = district.Name,
                    Population = district.Population,
                    EligibleVoters = standing != null ? standing.EligibleVoters : 0,
                    LeadingPartyId = leader,
                    LeadingShare = leadingShare,
                    RunnerUpPartyId = runnerUp,
                    Margin = margin,
                    Turnout = standing != null ? standing.Turnout : 0.0,
                    Happiness = district.Happiness,
                    Discontent = indices != null ? indices.DiscontentIndex : 0.0,
                    HasCityFallbacks = district.HasCityFallbacks
                });
            }

            rows.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return rows;
        }

        /// <summary>
        /// One district's full detail. An unknown id returns an empty payload rather than throwing —
        /// the player can delete a district while its panel is open.
        /// </summary>
        internal static DistrictDetailPayload BuildDistrictDetail(PoliticalState state, CitySnapshot snapshot,
                                                                  string districtId)
        {
            var payload = new DistrictDetailPayload();
            if (snapshot == null || string.IsNullOrEmpty(districtId)) return payload;

            DistrictSnapshot district = null;
            for (int i = 0; i < snapshot.Districts.Count; i++)
            {
                if (string.CompareOrdinal(snapshot.Districts[i].Id, districtId) == 0)
                {
                    district = snapshot.Districts[i];
                    break;
                }
            }

            if (district == null) return payload;

            DistrictResult standing = FindStanding(state, districtId);
            DistrictIndices indices = FindDistrictIndices(state, districtId);

            payload.Id = district.Id;
            payload.Name = district.Name;
            payload.Population = district.Population;
            payload.Households = district.Households;
            payload.Happiness = district.Happiness;
            payload.Unemployment = district.Unemployment;

            payload.WealthLow = district.Wealth.LowShare;
            payload.WealthMiddle = district.Wealth.MiddleShare;
            payload.WealthHigh = district.Wealth.HighShare;

            payload.EduUneducated = district.Education.UneducatedShare;
            payload.EduPoorly = district.Education.PoorlyEducatedShare;
            payload.EduEducated = district.Education.EducatedShare;
            payload.EduWell = district.Education.WellEducatedShare;
            payload.EduHighly = district.Education.HighlyEducatedShare;

            payload.AgeChild = district.Age.ChildShare;
            payload.AgeTeen = district.Age.TeenShare;
            payload.AgeAdult = district.Age.AdultShare;
            payload.AgeElderly = district.Age.ElderlyShare;

            if (indices != null)
            {
                payload.IdxGentrification = indices.GentrificationIndex;
                payload.IdxCommuteMisery = indices.CommuteMiseryIndex;
                payload.IdxServiceCoverage = indices.ServiceCoverageIndex;
                payload.IdxDiscontent = indices.DiscontentIndex;
                payload.IdxGini = indices.GiniCoefficient;
            }

            if (standing != null)
            {
                payload.EligibleVoters = standing.EligibleVoters;
                payload.VotesCast = standing.VotesCast;
                payload.Turnout = standing.Turnout;
                payload.WinningPartyId = standing.WinningPartyId;
                payload.Margin = standing.Margin;
                payload.Seats = standing.Seats;
                payload.DecidedByTieBreak = standing.DecidedByTieBreak;
                payload.Shares = Shares(standing.Shares);
            }

            payload.HasCityFallbacks = district.HasCityFallbacks;
            payload.CityFallbackFields = SortedCopy(district.CityFallbackFields);
            return payload;
        }

        /// <summary>
        /// A wealth × education crosstab: always exactly fifteen cells in a fixed order, even where a
        /// cell has no population, so the panel renders a 3×5 grid without handling holes.
        /// </summary>
        /// <param name="districtId">Null or empty aggregates the whole city.</param>
        /// <remarks>
        /// <b>Known gap.</b> <see cref="CrosstabCellPayload.Turnout"/> reports the district's realised
        /// turnout rather than the cell's own. Per-bloc turnout is computed by the engine every tick
        /// but is not persisted on <see cref="Bloc"/>, so it is not available here; publishing the
        /// district figure is honest about the granularity we actually have, where interpolating a
        /// per-cell rate would invent one. Closing it means adding a field to <c>Bloc</c>, which is a
        /// contract change and goes through <c>/schema-change</c>.
        /// </remarks>
        internal static List<CrosstabCellPayload> BuildCrosstab(PoliticalState state, string districtId)
        {
            var cells = new List<CrosstabCellPayload>();
            if (state == null) return cells;

            bool cityWide = string.IsNullOrEmpty(districtId);

            // Accumulators indexed [wealth, education] — a fixed 3×5 grid, never a dictionary, so the
            // output order cannot depend on insertion order.
            var population = new int[3, 5];
            var eligible = new int[3, 5];
            var happiness = new double[3, 5];
            var discontent = new double[3, 5];
            var voteWeight = new double[3, 5];
            var votes = new Dictionary<string, double>[3, 5];

            int totalPopulation = 0;

            for (int i = 0; i < state.Blocs.Count; i++)
            {
                Bloc bloc = state.Blocs[i];
                if (bloc == null) continue;
                if (!cityWide && string.CompareOrdinal(bloc.DistrictId, districtId) != 0) continue;

                int w = (int)bloc.Key.Wealth;
                int e = (int)bloc.Key.Education;

                population[w, e] += bloc.Population;
                eligible[w, e] += bloc.EligibleVoters;
                totalPopulation += bloc.Population;

                // Population-weighted, so a large bloc is not averaged away by a small one.
                happiness[w, e] += bloc.Happiness * bloc.Population;
                discontent[w, e] += bloc.Discontent * bloc.Population;

                if (bloc.PreviousVote.Count > 0 && bloc.EligibleVoters > 0)
                {
                    if (votes[w, e] == null) votes[w, e] = new Dictionary<string, double>(StringComparer.Ordinal);

                    for (int p = 0; p < bloc.PreviousVote.Count; p++)
                    {
                        PartyVoteShare share = bloc.PreviousVote[p];
                        double weighted = share.Share * bloc.EligibleVoters;

                        double current;
                        votes[w, e][share.PartyId] =
                            votes[w, e].TryGetValue(share.PartyId, out current) ? current + weighted : weighted;
                    }

                    voteWeight[w, e] += bloc.EligibleVoters;
                }
            }

            double turnout = cityWide ? CityTurnout(state) : DistrictTurnout(state, districtId);

            for (int w = 0; w < 3; w++)
            {
                for (int e = 0; e < 5; e++)
                {
                    int pop = population[w, e];

                    string leader = "";
                    double leadingShare = 0.0;

                    if (votes[w, e] != null && voteWeight[w, e] > 0.0)
                    {
                        LeadingOf(votes[w, e], voteWeight[w, e], out leader, out leadingShare);
                    }

                    cells.Add(new CrosstabCellPayload
                    {
                        Wealth = ((WealthTier)w).ToString(),
                        Education = ((EducationTier)e).ToString(),
                        Population = pop,
                        PopulationShare = totalPopulation > 0 ? (double)pop / totalPopulation : 0.0,
                        EligibleVoters = eligible[w, e],
                        Turnout = eligible[w, e] > 0 ? turnout : 0.0,
                        LeadingPartyId = leader,
                        LeadingShare = leadingShare,
                        Happiness = pop > 0 ? happiness[w, e] / pop : 0.0,
                        Discontent = pop > 0 ? discontent[w, e] / pop : 0.0
                    });
                }
            }

            return cells;
        }

        /// <summary>Highest accumulated vote in a cell; ties fall to the lowest id ordinal.</summary>
        private static void LeadingOf(Dictionary<string, double> votes, double total,
                                      out string leader, out double leadingShare)
        {
            leader = "";
            double best = -1.0;

            // Sorted first: a dictionary's enumeration order must never decide a displayed winner.
            var ids = new List<string>(votes.Count);
            foreach (KeyValuePair<string, double> entry in votes) ids.Add(entry.Key);
            ids.Sort(CompareOrdinal);

            for (int i = 0; i < ids.Count; i++)
            {
                if (votes[ids[i]] > best)
                {
                    best = votes[ids[i]];
                    leader = ids[i];
                }
            }

            leadingShare = total > 0.0 && best > 0.0 ? best / total : 0.0;
        }

        internal static CityIndicesPayload BuildCityIndices(PoliticalState state)
        {
            var payload = new CityIndicesPayload();
            if (state == null || state.Indices == null) return payload;

            DerivedIndices indices = state.Indices;
            payload.Gini = indices.GiniCoefficient;
            payload.BrainDrain = indices.BrainDrainIndex;
            payload.ServiceInequality = indices.ServiceInequalityIndex;
            payload.CommuteMisery = indices.CommuteMiseryIndex;
            payload.Polarization = indices.PolarizationIndex;
            payload.Legitimacy = indices.LegitimacyIndex;
            payload.Discontent = indices.DiscontentIndex;
            return payload;
        }

        // ------------------------------------------------------------------ agora.news

        /// <summary>Live timeline events, newest fired first, capped at twenty-five.</summary>
        internal static List<TimelineEventBriefPayload> BuildEvents(PoliticalState state)
        {
            var rows = new List<TimelineEventBriefPayload>();
            if (state == null) return rows;

            for (int i = 0; i < state.ActiveEvents.Count; i++)
            {
                TimelineEvent ev = state.ActiveEvents[i];
                if (ev == null) continue;

                var row = new TimelineEventBriefPayload
                {
                    Id = ev.Id,
                    Date = ev.Date,
                    Title = ev.Title,
                    Region = ev.Region.ToString(),
                    Origin = ev.Origin.ToString(),
                    Severity = ev.Severity,
                    DurationMonths = ev.DurationMonths,
                    FiredDate = ev.FiredDate,
                    ExpiresDate = ev.ExpiresDate,
                    ArchetypeId = ev.ArchetypeId ?? "",
                    LocalAngle = ev.LocalAngle ?? "",
                    Tags = SortedCopy(ev.Tags)
                };

                // Districts this event's effects actually landed on. Empty reads as city-wide.
                var districts = new List<string>();
                for (int f = 0; f < ev.Effects.Count; f++)
                {
                    string id = ev.Effects[f].DistrictId;
                    if (!string.IsNullOrEmpty(id) && !districts.Contains(id)) districts.Add(id);
                }
                districts.Sort(CompareOrdinal);
                row.DistrictIds = districts;

                rows.Add(row);
            }

            rows.Sort(CompareEventRows);
            if (rows.Count > EventsMax) rows.RemoveRange(EventsMax, rows.Count - EventsMax);
            return rows;
        }

        private static int CompareEventRows(TimelineEventBriefPayload a, TimelineEventBriefPayload b)
        {
            bool aHas = a.FiredDate.HasValue;
            bool bHas = b.FiredDate.HasValue;

            if (aHas && bHas)
            {
                int byDate = b.FiredDate.Value.CompareTo(a.FiredDate.Value);
                if (byDate != 0) return byDate;
            }
            else if (aHas != bHas)
            {
                return aHas ? -1 : 1;
            }

            return string.CompareOrdinal(a.Id, b.Id);
        }

        /// <summary>
        /// The mandate tracker, ordered so it opens on what is live and closest to its deadline:
        /// status rank, then deadline, then id.
        /// </summary>
        internal static List<MandateRowPayload> BuildMandates(PoliticalState state)
        {
            var rows = new List<MandateRowPayload>();
            if (state == null) return rows;

            for (int i = 0; i < state.Mandates.Count; i++)
            {
                Mandate mandate = state.Mandates[i];
                if (mandate == null) continue;

                rows.Add(new MandateRowPayload
                {
                    Id = mandate.Id,
                    PartyId = mandate.PartyId,
                    CoalitionId = mandate.CoalitionId,
                    DistrictId = mandate.DistrictId ?? "",
                    Issue = mandate.Issue.ToString(),
                    Metric = mandate.Metric.ToString(),
                    Direction = mandate.Direction.ToString(),
                    BaselineValue = mandate.BaselineValue,
                    TargetValue = mandate.TargetValue,
                    CurrentValue = mandate.CurrentValue,
                    Progress = mandate.Progress,
                    IssuedDate = mandate.IssuedDate,
                    DeadlineDate = mandate.DeadlineDate,
                    ResolvedDate = mandate.ResolvedDate,
                    Status = mandate.Status.ToString(),
                    Salience = mandate.Salience,
                    Text = mandate.Text,
                    IsMeasurementStalled = mandate.IsMeasurementStalled,
                    MonthsRemaining = state.Date.MonthsUntil(mandate.DeadlineDate)
                });
            }

            rows.Sort(CompareMandateRows);
            return rows;
        }

        private static int CompareMandateRows(MandateRowPayload a, MandateRowPayload b)
        {
            int byStatus = StatusRank(a.Status).CompareTo(StatusRank(b.Status));
            if (byStatus != 0) return byStatus;

            int byDeadline = a.DeadlineDate.Value.CompareTo(b.DeadlineDate.Value);
            if (byDeadline != 0) return byDeadline;

            return string.CompareOrdinal(a.Id, b.Id);
        }

        /// <summary>Live promises first; the tracker is about what is still in play.</summary>
        private static int StatusRank(string status)
        {
            switch (status)
            {
                case "Active": return 0;
                case "Pending": return 1;
                case "PartiallyFulfilled": return 2;
                case "Fulfilled": return 3;
                case "Defied": return 4;
                default: return 5;
            }
        }

        /// <summary>
        /// The news feed: prose articles, plus engine milestones that deserve a line whether or not
        /// the model ever ran. Newest first, capped at forty.
        /// </summary>
        /// <remarks>
        /// Bodies deliberately stay out of this payload — the feed carries a headline and one line,
        /// and <c>agora.news.article</c> serves the body when an item is opened. Forty feed rows with
        /// 120-word bodies attached would be the largest thing crossing the bridge, every month.
        /// </remarks>
        internal static List<NewsHeadlinePayload> BuildFeed(PoliticalState state, FlavorPayload prose)
        {
            var rows = new List<NewsHeadlinePayload>();

            if (prose != null)
            {
                for (int i = 0; i < prose.Articles.Count; i++)
                {
                    Article article = prose.Articles[i];
                    if (article == null || string.IsNullOrEmpty(article.Id)) continue;

                    rows.Add(new NewsHeadlinePayload
                    {
                        Id = article.Id,
                        Date = prose.GeneratedAt,
                        Kind = "Article",
                        Headline = article.Headline,
                        Summary = FirstLine(article.Body),
                        OutletId = article.Outlet ?? "",
                        OutletName = article.Outlet ?? "",
                        HasArticle = true
                    });
                }
            }

            if (state != null)
            {
                for (int i = 0; i < state.ActiveEvents.Count; i++)
                {
                    TimelineEvent ev = state.ActiveEvents[i];
                    if (ev == null || !ev.FiredDate.HasValue) continue;

                    rows.Add(new NewsHeadlinePayload
                    {
                        Id = "event:" + ev.Id,
                        Date = ev.FiredDate.Value,
                        Kind = "Event",
                        Headline = ev.Title,
                        Summary = ev.HeadlineBrief ?? "",
                        Severity = ev.Severity,
                        EventId = ev.Id,
                        HasArticle = false
                    });
                }

                for (int i = 0; i < state.ElectionHistory.Count; i++)
                {
                    ElectionResult election = state.ElectionHistory[i];
                    if (election == null) continue;

                    rows.Add(new NewsHeadlinePayload
                    {
                        Id = "election:" + election.Id,
                        Date = election.Date,
                        Kind = "Election",
                        Headline = election.IsSnapElection ? "Snap election held" : "Election held",
                        Summary = "Turnout " + Percent(election.Turnout) + " across " +
                                  election.TotalSeats + " seats.",
                        PartyId = WinnerOf(election),
                        HasArticle = false
                    });
                }

                for (int i = 0; i < state.CoalitionHistory.Count; i++)
                {
                    Coalition coalition = state.CoalitionHistory[i];
                    if (coalition == null || !coalition.EndedDate.HasValue) continue;

                    rows.Add(new NewsHeadlinePayload
                    {
                        Id = "coalition:" + coalition.Id,
                        Date = coalition.EndedDate.Value,
                        Kind = "Coalition",
                        Headline = coalition.Status == CoalitionStatus.Collapsed
                            ? "Government collapsed"
                            : "Government's term ended",
                        Summary = coalition.CollapseReason == CoalitionCollapseReason.None
                            ? ""
                            : "Reason: " + coalition.CollapseReason + ".",
                        PartyId = coalition.LeadPartyId,
                        HasArticle = false
                    });
                }
            }

            rows.Sort(CompareFeedRows);
            if (rows.Count > NewsFeedMax) rows.RemoveRange(NewsFeedMax, rows.Count - NewsFeedMax);
            return rows;
        }

        private static int CompareFeedRows(NewsHeadlinePayload a, NewsHeadlinePayload b)
        {
            int byDate = b.Date.Value.CompareTo(a.Date.Value);
            return byDate != 0 ? byDate : string.CompareOrdinal(a.Id, b.Id);
        }

        /// <summary>The body behind a feed item, or an empty payload for an unknown id.</summary>
        internal static NewsArticlePayload BuildArticle(FlavorPayload prose, string id)
        {
            var payload = new NewsArticlePayload();
            if (prose == null || string.IsNullOrEmpty(id)) return payload;

            for (int i = 0; i < prose.Articles.Count; i++)
            {
                Article article = prose.Articles[i];
                if (article == null || string.CompareOrdinal(article.Id, id) != 0) continue;

                payload.Id = article.Id;
                payload.Date = prose.GeneratedAt;
                payload.Headline = article.Headline;
                payload.Body = article.Body;
                payload.Tone = article.Tone ?? "";
                payload.OutletId = article.Outlet ?? "";
                payload.OutletName = article.Outlet ?? "";
                return payload;
            }

            return payload;
        }

        internal static FlavorStatusPayload BuildFlavorStatus(PoliticalState state)
        {
            var payload = new FlavorStatusPayload
            {
                LastFlavorDate = AgoraRuntime.LastFlavorDate,
                LastAttemptDate = AgoraRuntime.LastAttemptDate,
                ProviderAvailable = AgoraRuntime.ProviderAvailable,
                PendingWake = AgoraRuntime.PendingWake,
                LastError = AgoraRuntime.LastFlavorError
            };

            FlavorPayload prose = AgoraRuntime.Prose;
            payload.ArticleCount = prose != null ? prose.Articles.Count : 0;

            // Stale means "older than the wake cadence expects" — a year, since the yearly wake is the
            // only one that fires on a schedule rather than on an event.
            if (state != null && AgoraRuntime.LastFlavorDate.HasValue)
            {
                payload.IsStale = AgoraRuntime.LastFlavorDate.Value.MonthsUntil(state.Date) > 12;
            }
            else
            {
                payload.IsStale = prose == null;
            }

            return payload;
        }

        // ------------------------------------------------------------------ helpers

        private static List<PartySharePayload> Shares(List<PartyVoteShare> shares)
        {
            var rows = new List<PartySharePayload>();
            if (shares == null) return rows;

            for (int i = 0; i < shares.Count; i++)
            {
                rows.Add(new PartySharePayload { PartyId = shares[i].PartyId, Share = shares[i].Share });
            }

            // Already the engine's contractual order; re-sorted defensively because a panel is
            // forbidden to sort and would render whatever arrives.
            rows.Sort((a, b) => string.CompareOrdinal(a.PartyId, b.PartyId));
            return rows;
        }

        private static DistrictResult FindStanding(PoliticalState state, string districtId)
        {
            if (state == null) return null;

            for (int i = 0; i < state.CurrentDistrictStandings.Count; i++)
            {
                if (string.CompareOrdinal(state.CurrentDistrictStandings[i].DistrictId, districtId) == 0)
                    return state.CurrentDistrictStandings[i];
            }

            return null;
        }

        private static DistrictIndices FindDistrictIndices(PoliticalState state, string districtId)
        {
            if (state == null || state.Indices == null) return null;

            for (int i = 0; i < state.Indices.Districts.Count; i++)
            {
                if (string.CompareOrdinal(state.Indices.Districts[i].DistrictId, districtId) == 0)
                    return state.Indices.Districts[i];
            }

            return null;
        }

        private static double DistrictTurnout(PoliticalState state, string districtId)
        {
            DistrictResult standing = FindStanding(state, districtId);
            return standing != null ? standing.Turnout : 0.0;
        }

        private static double CityTurnout(PoliticalState state)
        {
            if (state == null) return 0.0;

            long votes = 0;
            long eligible = 0;

            for (int i = 0; i < state.CurrentDistrictStandings.Count; i++)
            {
                votes += state.CurrentDistrictStandings[i].VotesCast;
                eligible += state.CurrentDistrictStandings[i].EligibleVoters;
            }

            return eligible > 0 ? (double)votes / eligible : 0.0;
        }

        private static void TopTwo(List<PartyVoteShare> shares, out string leader, out double leadingShare,
                                   out string runnerUp, out double secondShare)
        {
            leader = "";
            runnerUp = "";
            leadingShare = 0.0;
            secondShare = 0.0;

            if (shares == null) return;

            for (int i = 0; i < shares.Count; i++)
            {
                PartyVoteShare share = shares[i];
                if (share.Share > leadingShare)
                {
                    runnerUp = leader;
                    secondShare = leadingShare;
                    leader = share.PartyId;
                    leadingShare = share.Share;
                }
                else if (share.Share > secondShare)
                {
                    runnerUp = share.PartyId;
                    secondShare = share.Share;
                }
            }
        }

        private static List<string> SortedCopy(List<string> source)
        {
            var copy = new List<string>();
            if (source == null) return copy;

            for (int i = 0; i < source.Count; i++)
            {
                if (!string.IsNullOrEmpty(source[i])) copy.Add(source[i]);
            }

            copy.Sort(CompareOrdinal);
            return copy;
        }

        private static string FirstLine(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";

            int stop = body.IndexOf('.');
            if (stop > 0 && stop + 1 < body.Length) return body.Substring(0, stop + 1);
            return body.Length <= 160 ? body : body.Substring(0, 160);
        }

        private static string Percent(double value) =>
            ((int)Math.Round(value * 100.0, MidpointRounding.AwayFromZero)) + "%";

        private static int CompareOrdinal(string a, string b) => string.CompareOrdinal(a, b);
    }
}
