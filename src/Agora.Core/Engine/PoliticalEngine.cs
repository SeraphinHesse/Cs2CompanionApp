using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Engine.Affinity;
using Agora.Core.Engine.Blocs;
using Agora.Core.Engine.Elections.Fptp;
using Agora.Core.Engine.Elections.Proportional;
using Agora.Core.Engine.Factions;
using Agora.Core.Engine.Government.Coalitions;
using Agora.Core.Engine.Government.Mandates;
using Agora.Core.Engine.Indices;
using Agora.Core.Engine.Parties;
using Agora.Core.Engine.Polling;
using Agora.Core.Engine.Turnout;
using Agora.Core.Events.Scheduler;
using Agora.Core.Stories;
using Agora.Core.Tuning;
using Mandate = Agora.Core.Contracts.Mandate;

namespace Agora.Core.Engine
{
    /// <summary>
    /// The monthly political tick: the one place the fourteen engine packets are run in order.
    ///
    /// <para>
    /// Every packet was written as a pure function taking frozen contract types, on the understanding
    /// that something would eventually sequence them. This is that something, and it is deliberately
    /// the <i>only</i> thing that knows the order. Each packet still knows nothing about its
    /// neighbours, which is what keeps them independently testable — the cost is that the order lives
    /// here and is therefore worth reading carefully.
    /// </para>
    ///
    /// <para>
    /// <b>Purity.</b> <see cref="Advance"/> is a pure function of <see cref="EngineTickInput"/> plus
    /// tuning. It never mutates the input state, never touches a clock, never dispatches an effect and
    /// never calls the flavor provider — it reports what should happen and the caller does it. That is
    /// what makes a decade of politics replayable in milliseconds with no game installed, and it is
    /// what makes non-negotiable #3 checkable rather than merely intended.
    /// </para>
    ///
    /// <para>
    /// <b>Stage order</b>, and why: blocs are rebuilt first because everything downstream is expressed
    /// per bloc. Events fire before affinity so a disaster is already live when voters are scored
    /// against it. Indices come before affinity because the incumbency penalty reads city discontent.
    /// Turnout follows affinity because it needs the competitiveness of the race. Mandates are
    /// monitored before an election so a promise resolved this month is counted in the vote that
    /// judges it. Lifecycle runs last because a party that dies this cycle must still have contested
    /// the election that killed it.
    /// </para>
    /// </summary>
    public static class PoliticalEngine
    {
        private static readonly TimelineEvent[] NoEvents = new TimelineEvent[0];
        private static readonly CivicEvent[] NoCivicEvents = new CivicEvent[0];
        private static readonly CitySnapshot[] NoSnapshots = new CitySnapshot[0];

        // ------------------------------------------------------------------ save creation

        /// <summary>
        /// Builds the state a brand-new save starts from: the initial party registry, the first bloc
        /// set, and factions where the theme calls for them. No election is scheduled yet — that
        /// happens on the first tick past <c>scheduler.warmupMonths</c>, once there is enough metric
        /// history for the politics to be about something.
        /// </summary>
        public static PoliticalState CreateInitialState(Guid saveGuid, SimDate startDate,
                                                        AgoraSettings? settings, CitySnapshot? snapshot,
                                                        EngineTuning? tuning)
        {
            EngineTuning t = tuning ?? EngineTuning.Default;
            AgoraSettings s = settings ?? new AgoraSettings();

            // The system is a function of the theme and nothing else, so it is derived here rather
            // than trusted from the settings object. Without this line an NA save kept the
            // initialiser's Proportional and ran North American parties through a list election with
            // no mayor — silently, since neither half complains.
            s.System = RegionThemeRules.SystemFor(s.Theme);

            var state = new PoliticalState
            {
                SaveGuid = saveGuid,
                Date = startDate,
                Settings = s,
                TermNumber = 1
            };

            state.Parties = PartyRegistry.GenerateInitial(saveGuid, startDate, s.Theme, t);

            if (snapshot != null)
            {
                state.Blocs = BlocBuilder.Build(snapshot, t, null);
            }

            if (FactionModel.AppliesTo(s))
            {
                state.Factions = FactionModel.Generate(state.Parties, state.Blocs, saveGuid, startDate, t);
                FactionModel.ApplyFactionIds(state.Parties, state.Factions);
            }

            state.Parties.Sort(PartyRegistry.CompareById);
            return state;
        }

        // ------------------------------------------------------------------ retheme

        /// <summary>
        /// Changes the save's region theme before its first election, regenerating everything whose
        /// meaning depends on it (fixplan W3).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why this is not "swap the party list".</b> Party ids are positional and are reused
        /// across themes with different meanings — EU <c>party-01</c> is the green brand, NA
        /// <c>party-01</c> is the liberal one. Every list keyed by a party id therefore holds a
        /// statement about a party that is about to stop existing, and not one of them fails loudly:
        /// a stale <see cref="PoliticalState.CurrentVoteShares"/> or
        /// <see cref="Bloc.PreviousVote"/> is a perfectly well-formed number attached to the wrong
        /// brand. So all of it goes, and the list of what goes is exhaustive by design.
        /// </para>
        /// <para>
        /// <b>Regeneration happens at <paramref name="startDate"/>, not at the state's current
        /// date.</b> A party's <see cref="Party.FoundedDate"/> seeds its canned name, so regenerating
        /// in month forty would give a rethemed save a different roster from one that chose the same
        /// theme at frame zero. Using the save's political start date makes the two rosters
        /// byte-identical, which is the property the parity test pins.
        /// </para>
        /// <para>
        /// <b>That parity is the parties', not the factions'.</b> Factions are seeded from the party
        /// set <i>and the bloc set</i>, and the blocs a month-forty retheme hands
        /// <c>FactionModel.Generate</c> are the city's current demography, not the frame-zero
        /// demography a natively-minted save was built from — so <c>IssueClimate.FromBlocs</c> differs
        /// and the faction set is not the mint-time one. That is the intended behaviour rather than a
        /// gap: a faction is a reading of who lives here now, and regenerating it from a four-year-old
        /// city would be the stranger choice. The two only coincide when the blocs have not moved,
        /// which is the case the faction test covers.
        /// </para>
        /// <para>
        /// <b>Pure.</b> <paramref name="prior"/> is never mutated, and that includes its
        /// <see cref="PoliticalState.Settings"/> — the settings object is shared by reference across
        /// <c>CloneState</c>, so writing the new theme into it would reach straight back into the
        /// caller's state. A fresh <see cref="AgoraSettings"/> is built instead.
        /// </para>
        /// </remarks>
        /// <param name="prior">The state to reinterpret. Left exactly as it was found.</param>
        /// <param name="theme">The theme the player picked.</param>
        /// <param name="startDate">
        /// The save's first political date — <c>January of AgoraSettings.StartYear</c>, the same value
        /// <see cref="CreateInitialState"/> was given. Everything regenerated is seeded from it.
        /// </param>
        public static RethemeResult Retheme(PoliticalState? prior, RegionTheme theme, SimDate startDate,
                                            EngineTuning? tuning)
        {
            if (prior == null) return new RethemeResult(CommandOutcome.Failed, null, false);

            EngineTuning t = tuning ?? EngineTuning.Default;
            AgoraSettings priorSettings = prior.Settings ?? new AgoraSettings();

            // Asking for the theme the save already runs is not a change to refuse — it is a change
            // that has already happened. Checked before the lock so that re-confirming your own theme
            // after the first election reads as "yes, that is what you have" rather than as a refusal.
            if (priorSettings.Theme == theme) return new RethemeResult(CommandOutcome.Ok, prior, false);

            // ElectionHistory is the authority and ThemeLocked is the convenience. Both are checked so
            // that a flag lost to a failed migration cannot open a hole in a rule the sidecar's own
            // contents already settle: a save that has voted has a political history keyed to brands
            // that must not be redefined under it.
            if (prior.ElectionHistory.Count > 0 || priorSettings.ThemeLocked)
                return new RethemeResult(CommandOutcome.ThemeLocked, prior, false);

            AgoraSettings settings = priorSettings.Clone();
            settings.Theme = theme;
            settings.System = RegionThemeRules.SystemFor(theme);

            PoliticalState state = CloneState(prior, prior.Date);
            state.Settings = settings;

            // Replaced, never merged: a party the old theme placed at party-03 and the new one places
            // there too are different brands that happen to share a slot.
            state.Parties = PartyRegistry.GenerateInitial(prior.SaveGuid, startDate, theme, t);

            // The failure streak is a claim about how the old theme's majors governed. Those brands
            // are gone, so carrying it would hand the new ballot's fringe an unlock nobody earned.
            state.Fringe = new FringeWatch();

            // Blocs survive — they are demography and know nothing about parties — but each one's
            // memory of how it voted is a party-id vector, and CloneState shares the Bloc objects with
            // the caller. So the blocs are copied here, shallowly, with that one field emptied.
            state.Blocs = ClearPreviousVote(state.Blocs);

            state.Factions = FactionModel.AppliesTo(settings)
                ? FactionModel.Generate(state.Parties, state.Blocs, prior.SaveGuid, startDate, t)
                : new List<Faction>();

            FactionModel.ApplyFactionIds(state.Parties, state.Factions);

            // The prose that date refers to described the old theme's brands and is discarded with
            // them, so a date claiming it was produced is a claim about nothing. Nothing restores the
            // payload from it today, which is exactly why it has to go now rather than when something
            // does: a stale date here would be a lie waiting for its first reader.
            state.LastFlavorDate = null;

            state.CurrentVoteShares = new List<PartyVoteShare>();
            state.CurrentDistrictStandings = new List<DistrictResult>();
            state.RecentPolls = new List<PollResult>();
            state.Government = null;
            state.CoalitionHistory = new List<Coalition>();
            state.Mandates = new List<Mandate>();
            state.MayorPartyId = null;

            // The term length changes with the system (3y ↔ 4y), so a date scheduled under the old one
            // is wrong under the new one. Cleared rather than recomputed: Advance re-derives the first
            // ballot from the start date once warmup is complete, and that is the only place that rule
            // is allowed to live.
            state.NextElectionDate = null;
            state.IsCampaignSeason = false;
            state.TermNumber = 1;

            Normalize(state);
            return new RethemeResult(CommandOutcome.Ok, state, true);
        }

        /// <summary>
        /// Copies a bloc list with <see cref="Bloc.PreviousVote"/> emptied, leaving the originals
        /// alone. Everything else about a bloc is demographic and theme-independent.
        /// </summary>
        private static List<Bloc> ClearPreviousVote(List<Bloc> blocs)
        {
            var copy = new List<Bloc>(blocs.Count);

            for (int i = 0; i < blocs.Count; i++)
            {
                Bloc source = blocs[i];
                if (source == null) continue;

                copy.Add(new Bloc
                {
                    DistrictId = source.DistrictId,
                    Key = source.Key,
                    Population = source.Population,
                    PopulationShare = source.PopulationShare,
                    EligibleVoters = source.EligibleVoters,
                    Weights = source.Weights,
                    Ideal = source.Ideal,
                    Happiness = source.Happiness,
                    Discontent = source.Discontent,
                    PreviousVote = new List<PartyVoteShare>(),
                    HasCityFallbacks = source.HasCityFallbacks
                });
            }

            return copy;
        }

        // ------------------------------------------------------------------ the tick

        /// <summary>
        /// Advances the political state by one sim date.
        /// </summary>
        /// <remarks>
        /// Safe to call on every date the caller sees: dates that fall between engine intervals return
        /// <see cref="EngineTickResult.DidWork"/> false and a state that differs from the prior one
        /// only in <see cref="PoliticalState.Date"/>.
        /// </remarks>
        public static EngineTickResult Advance(EngineTickInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            EngineTuning tuning = input.Tuning ?? EngineTuning.Default;
            PoliticalState prior = input.PriorState ?? new PoliticalState();
            AgoraSettings settings = prior.Settings ?? new AgoraSettings();
            SimDate date = input.Date;
            Guid saveGuid = input.SaveGuid;

            var result = new EngineTickResult();

            // An election is due when the calendar says so, decided before the plan is built because
            // TickPlanner needs to know in order to authorise the election LLM wake.
            bool electionDue = prior.NextElectionDate.HasValue
                            && date.TotalMonths >= prior.NextElectionDate.Value.TotalMonths;

            TickPlan plan = TickPlanner.Plan(input.StartDate, date, settings, prior.NextElectionDate,
                                             electionDue, input.ManualFlavorWakeRequested, tuning);

            result.Plan = plan;
            result.LlmWake = plan.LlmWake;

            if (!plan.IsEngineTick)
            {
                // Not our month. Hand back a copy rather than the caller's object so nobody can come to
                // depend on "the engine returns what I gave it when nothing happened".
                result.State = CloneState(prior, date);
                result.DidWork = false;
                return result;
            }

            PoliticalState state = CloneState(prior, date);
            result.DidWork = true;

            // The first ballot is scheduled once warmup has passed, never at save creation: before
            // there is metric history the politics would be about nothing, and a campaign fought over
            // a city the sensors have not yet measured is the one thing §3 says must not happen. One
            // full term from the political start date, so it does not land in the same tick.
            if (!state.NextElectionDate.HasValue && plan.IsWarmupComplete)
            {
                int termMonths = TermMonths(settings.System, tuning);
                SimDate first = input.StartDate.AddMonths(termMonths);
                if (first <= date) first = date.AddMonths(termMonths);
                state.NextElectionDate = first;
            }

            CitySnapshot? snapshot = input.Snapshot;

            // --- 1. Blocs. Everything downstream is expressed per bloc, so this is first and is the
            // only stage that reads the raw snapshot demographics.
            if (snapshot != null)
            {
                state.Blocs = BlocBuilder.Build(snapshot, tuning, prior.Blocs);
            }
            else
            {
                result.Warnings.Add("No snapshot this tick; blocs, indices and mandates were carried " +
                                    "forward unchanged.");
            }

            // --- 2. Parties. A save that somehow reached a tick with no registry gets one rather than
            // an empty ballot for the rest of its life.
            if (state.Parties.Count == 0)
            {
                state.Parties = PartyRegistry.GenerateInitial(saveGuid, date, settings.Theme, tuning);
                result.Warnings.Add("Party registry was empty; generated the initial set at " + date + ".");
            }

            // --- 3. Events. Fired before voters are scored, so a disaster is already live in the
            // affinity pass that reacts to it.
            var eventSeverities = new List<int>();
            if (plan.IsEventScan)
            {
                var context = new SchedulerContext
                {
                    SaveGuid = saveGuid,
                    Date = date,
                    StartDate = input.StartDate,
                    Theme = settings.Theme,
                    Catalog = input.Catalog ?? NoEvents,
                    FiredEventIds = state.FiredEventIds,
                    ActiveEvents = state.ActiveEvents,
                    DistrictIds = DistrictIds(snapshot),
                    Archetypes = input.Archetypes,
                    EffectsEnabled = settings.EffectsEnabled
                };

                SchedulerTick events = EventScheduler.Run(context, tuning);

                state.ActiveEvents = events.NextActiveEvents;
                state.FiredEventIds = MergeIds(state.FiredEventIds, events.RecordedEventIds);

                result.FiredEvents.AddRange(events.Fired);
                result.EffectRequests.AddRange(events.EffectRequests);
                result.Warnings.AddRange(events.Warnings);

                for (int i = 0; i < events.Fired.Count; i++) eventSeverities.Add(events.Fired[i].Severity);
            }

            // --- 3b. Stories. After the event scan and before affinity, so this cycle's active
            // effects and issue pressures are visible to the voter model on the same tick — which is
            // how timeline events have always worked, and the reason a verdict is felt in the month
            // it lands rather than the month after. Resolution runs in this same slot on the resolve
            // month, before the pressures it changes are read.
            //
            // The whole cycle lives in Agora.Core rather than in AgoraRuntime, deliberately: the
            // idempotence guards and the stranded sweep are exactly the arithmetic that has to be
            // provable, and AgoraRuntime compiles into no test.
            var storyPressures = new List<StoryPressureContribution>();

            if (snapshot != null)
            {
                var storyInput = new StoryCycleInput
                {
                    State = state,
                    Catalog = input.CivicCatalog ?? NoCivicEvents,
                    Context = new StoryReadContext
                    {
                        Today = snapshot,
                        History = input.SnapshotHistory ?? NoSnapshots
                    },
                    SaveGuid = saveGuid,
                    Today = date,
                    IsStoryDraft = plan.IsStoryDraft,
                    IsStoryResolve = plan.IsStoryResolve,
                    IsReplay = input.IsReplay,
                    GoverningVoteShare = GoverningVoteShare(state),
                    Tuning = tuning
                };

                StoryCycleResult stories = StoryCycle.Run(storyInput);

                result.DraftedStories.AddRange(stories.DraftedStories);
                result.ResolvedStories.AddRange(stories.ResolvedStories);
                result.EffectRequests.AddRange(stories.EffectRequests);
                result.Warnings.AddRange(stories.Warnings);
                result.PowerDelta = stories.PowerDelta;
                storyPressures = stories.Pressures;
            }
            else
            {
                // No reading means no trigger can be evaluated and no check can be scored. Skipping
                // the cycle entirely is the honest answer: drafting against a zeroed CitySnapshot
                // would hand the player obligations derived from a city nobody measured, and every
                // one of them would resolve against a different city next month.
                result.Warnings.Add("story cycle skipped at " + date + ": the sensors reported nothing.");
            }

            // --- 4. Derived indices. Before affinity: the incumbency penalty reads city discontent,
            // and reading last month's would make the penalty lag the events that caused it.
            if (plan.IsIndices && snapshot != null)
            {
                var indicesInput = new IndicesInput
                {
                    Snapshot = snapshot,
                    History = input.SnapshotHistory ?? new CitySnapshot[0],
                    Previous = prior.Indices,
                    VoteShares = state.CurrentVoteShares,
                    LastElectionTurnout = LastElectionTurnout(state),
                    Mandates = state.Mandates,
                    Government = state.Government
                };

                state.Indices = IndicesEngine.Compute(indicesInput, tuning);

                // The snapshot's own Indices block is documented as "this is what fills it" — the
                // sidecar and the flavor prompt both read it from there rather than from state.
                snapshot.Indices = state.Indices;
            }

            // Per-issue city grievance. Hoisted out of RunLifecycle, which used to be its only caller:
            // the fringe ceiling needs it every tick, not only on a lifecycle month, and computing it
            // twice would be both wasteful and a chance for the two readings to disagree. Blocs are
            // settled in stage 1 and nothing between here and stage 12 touches them, so one reading
            // serves both.
            IssueClimate climate = IssueClimate.FromBlocs(state.Blocs);

            // --- 4b. Manifestos. Parties on the ballot move toward whatever the city is currently
            // aggrieved about, once per campaign.
            //
            // Edge-triggered on campaign season opening, not run every campaign month: the drift is
            // capped per cycle, and applying it monthly would compound that cap into a platform that
            // sprints across the issue space. `state` is still a clone of the prior tick here, so its
            // IsCampaignSeason is last month's — the comparison against the plan is the edge.
            //
            // This is what lets a major party win a protest vote back. Without it the fringe ceiling
            // is a ratchet: grievance opens it, and nothing an establishment party does can answer the
            // grievance and close it again. Placed before affinity so voters are scored against the
            // platform the party is actually campaigning on, and before the election so the winner's
            // mandates are generated from the manifesto it ran on.
            if (plan.IsCampaignSeason && !state.IsCampaignSeason)
            {
                state.Parties = RefreshManifestos(state.Parties, saveGuid, date, climate.Grievance, tuning);
            }

            // --- 5. Affinity. The voter model proper: how much each bloc likes each party today.
            IReadOnlyList<Party> ballot = OnBallot(state.Parties);

            // --- 5a. Fringe ceilings. Built from the CLOSED failure record, so the ballot that ends a
            // term is fought under the ceiling that term inherited — an unlock earned this month first
            // shows up in next month's standings. That is the literal reading of "three consecutive
            // failure terms before fringe support may exceed 3% at all".
            FringeCeilings fringeCeilings = FringeFailureModel.Ceilings(
                state.Parties, state.Fringe, climate.Grievance, settings.System, tuning.Fringe);

            // Whether this save keeps a failure ledger at all. Proportional saves do not: the ceiling
            // is FPTP-only, so recording the inputs to it there would be churn nothing ever reads.
            bool fringeActive = FringeActive(state, tuning);

            var affinityRequest = new AffinityRequest
            {
                FringeCeilings = fringeCeilings,
                SaveGuid = saveGuid,
                Date = date,
                Blocs = state.Blocs,
                Parties = ballot,
                Mandates = state.Mandates,
                ActiveEvents = state.ActiveEvents,
                Government = state.Government,
                Indices = state.Indices,
                StoryPressures = storyPressures,
                LastElectionDate = LastElectionDate(state)
            };

            AffinityResult affinity = AffinityEngine.Compute(affinityRequest, tuning);

            // --- 6. Turnout. After affinity, because who bothers to vote depends on how close the
            // race is, and how close the race is comes out of the affinity pass.
            double campaignIntensity = CampaignIntensity(date, state.NextElectionDate, tuning);

            var turnoutInputs = new TurnoutInputs
            {
                SaveGuid = saveGuid,
                Date = date,
                Blocs = state.Blocs,
                DistrictStandings = state.CurrentDistrictStandings,
                CityStandings = state.CurrentVoteShares,
                CampaignIntensity = campaignIntensity,
                IsSnapElection = false,
                IncumbentConsecutiveTerms = IncumbentTerms(state)
            };

            TurnoutProjection projection = TurnoutModel.Project(turnoutInputs, tuning);

            // --- 7. Current standings — "if the election were held today". Model truth, not a poll:
            // the polling packet distorts these, it does not produce them.
            List<DistrictResult> districtStandings;
            List<PartyVoteShare> cityStandings = AggregateStandings(affinity, projection, out districtStandings);

            state.CurrentVoteShares = cityStandings;
            state.CurrentDistrictStandings = districtStandings;
            state.IsCampaignSeason = plan.IsCampaignSeason;

            // --- 8. Polls. Published only inside a campaign and only on a publication day — the two
            // are ANDed here rather than baked into the calendar (see TickPlan.IsPollTick).
            if (plan.IsPollTick && state.NextElectionDate.HasValue
                && PollSchedule.IsPublishDay(date, state.NextElectionDate.Value, tuning))
            {
                PollResult? poll = RunPoll(saveGuid, date, state.NextElectionDate.Value, snapshot,
                                            districtStandings, projection, tuning);
                if (poll != null)
                {
                    state.RecentPolls.Add(poll);
                    state.RecentPolls = PollSchedule.Trim(state.RecentPolls, tuning);
                    result.Poll = poll;
                }
            }

            // --- 9. Mandate monitoring. Before the election, so a promise resolved this month is
            // already counted in the vote that judges it.
            int fulfilled = 0;
            int defied = 0;
            double majorDefianceSurge = 0.0;

            if (plan.IsMandateMonitor && snapshot != null && state.Mandates.Count > 0)
            {
                MandateTickResult mandateTick =
                    MandateMonitor.Tick(saveGuid, date, snapshot, state.Mandates, tuning);

                state.Mandates = new List<Mandate>(mandateTick.Mandates);

                // The kill switch is honoured here as well as in the scheduler, and on the same rule:
                // the politics are computed and the resolution is recorded, only the request to the
                // sink is withheld. Leaving this to the Mod would make a per-save switch depend on the
                // caller remembering, and a switch that only works when someone remembers is not one.
                if (settings.EffectsEnabled && tuning.Effects.Enabled)
                {
                    result.EffectRequests.AddRange(mandateTick.Effects);
                }

                for (int i = 0; i < mandateTick.Resolutions.Count; i++)
                {
                    MandateResolution resolution = mandateTick.Resolutions[i];
                    MandateStatus status = resolution.Status;
                    if (status == MandateStatus.Fulfilled) fulfilled++;
                    else if (status == MandateStatus.Defied) defied++;

                    // OppositionSurge has been computed here since the mandate packet was written and
                    // read by nobody. This is its first reader: a promise broken by a major party is
                    // the clearest evidence the establishment is failing, and it arrives already
                    // weighted by how much the city cared.
                    if (status == MandateStatus.Defied && IsMajorParty(state.Parties, resolution.PartyId))
                        majorDefianceSurge += resolution.OppositionSurge;
                }
            }

            // --- 9b. Fold this tick into the fringe watch. After mandate monitoring so a promise
            // broken this month counts against the term it was broken in, and before the election so
            // the term that closes below has already seen its final month.
            //
            // Gated on the system as well as the master switch, so that a proportional save is
            // bit-identical with the packet on and off. The watch would be harmless there — nothing
            // reads it under PR — but "harmless" and "absent" are different claims, and only the
            // second one is testable.
            if (fringeActive)
            {
                FringeFailureModel.Observe(state.Fringe, new FringeMonth
                {
                    CityDiscontent = state.Indices.DiscontentIndex,
                    MajorDefianceSurge = majorDefianceSurge
                }, tuning.Fringe);
            }

            // --- 10. The election, or the government's monthly confidence check. Never both: an
            // election ends the outgoing government by definition.
            bool holdElection = electionDue && plan.IsWarmupComplete && ballot.Count > 0;

            if (holdElection)
            {
                RunElection(state, result, saveGuid, date, snapshot, affinity, projection, tuning);
            }
            else
            {
                TickGovernment(state, result, saveGuid, date, fulfilled, defied, eventSeverities, tuning);
            }

            // --- 11. Mandate generation. After formation, so a government elected this month leaves
            // with promises rather than waiting a term for them.
            if (state.Government != null && snapshot != null)
            {
                IReadOnlyList<Mandate> issued = MandateGenerator.Generate(
                    saveGuid, date, snapshot, state.Government, state.Blocs, state.Mandates, tuning);

                if (issued.Count > 0)
                {
                    state.Mandates.AddRange(issued);
                    for (int i = 0; i < issued.Count; i++)
                    {
                        if (!state.Government.MandateIds.Contains(issued[i].Id))
                            state.Government.MandateIds.Add(issued[i].Id);
                    }
                    state.Government.MandateIds.Sort(CompareOrdinal);
                }
            }

            // --- 12. Lifecycle. Last, because a party that dies this cycle must still have contested
            // the election that killed it, and a faction that splits must split from the platform the
            // voters actually saw.
            if (plan.IsLifecycle)
            {
                RunLifecycle(state, result, saveGuid, date, settings, tuning, climate);
            }

            // --- 13. Assemble. Every list leaves in its contractual order, every time: an unsorted
            // list changes the state hash without anything actually being wrong (§2.3).
            Normalize(state);

            result.State = state;
            result.KnownPartyIds = PartyIds(state.Parties);
            return result;
        }

        // ------------------------------------------------------------------ election

        private static void RunElection(PoliticalState state, EngineTickResult result, Guid saveGuid,
                                        SimDate date, CitySnapshot? snapshot, AffinityResult affinity,
                                        TurnoutProjection projection, EngineTuning tuning)
        {
            string electionId = "election-" + date.Year.ToString("D4") + "-" + date.Month.ToString("D2");
            bool isSnap = IsSnapElection(state, date, tuning);
            PollResult? finalPoll = LastPublishedPoll(state);

            ElectionResult election = state.Settings.System == ElectoralSystem.FirstPastThePost
                ? RunFptp(state, saveGuid, date, electionId, isSnap, affinity, projection, finalPoll, tuning)
                : RunProportional(state, saveGuid, date, electionId, isSnap, snapshot, projection,
                                  finalPoll, tuning);

            int termMonths = TermMonths(state.Settings.System, tuning);
            election.NextElectionDate = date.AddMonths(termMonths);

            // Turnover, counted before MayorPartyId is overwritten. A city that throws its mayor out
            // every cycle is one whose establishment is visibly not holding together, which is the
            // third of the fringe packet's city-wide failure signals.
            if (FringeActive(state, tuning) &&
                !string.Equals(election.MayorPartyId, state.MayorPartyId, StringComparison.Ordinal))
                state.Fringe.MayorChanges++;

            state.ElectionHistory.Add(election);
            state.TermNumber = election.TermNumber;
            state.NextElectionDate = election.NextElectionDate;
            state.MayorPartyId = election.MayorPartyId;

            // Close the term the ballot just ended. Scores it, extends or breaks the failure streak,
            // and zeroes the accumulator. Runs after TermNumber has advanced so the close is stamped
            // with the term that finished, which is what makes it idempotent across a reload.
            if (FringeActive(state, tuning))
                FringeFailureModel.CloseTerm(state.Fringe, election.TermNumber, tuning.Fringe);

            // How each bloc actually voted, for next cycle's habitual loyalty. Taken from the affinity
            // pass rather than from the district totals: loyalty is a bloc-level habit, and district
            // totals would give every bloc in a district the same memory.
            ApplyPreviousVote(state.Blocs, affinity);

            ApplyElectionToParties(state.Parties, election);

            // The outgoing government ends with the ballot, and its live promises die with it — they
            // were that government's promises, and scoring them against its successor would punish
            // the wrong party.
            if (state.Government != null)
            {
                Coalition outgoing = state.Government;
                if (!outgoing.EndedDate.HasValue) outgoing.EndedDate = date;
                if (outgoing.Status != CoalitionStatus.Collapsed) outgoing.Status = CoalitionStatus.Expired;

                state.CoalitionHistory.Add(outgoing);
                state.Mandates = new List<Mandate>(
                    MandateMonitor.AbandonAll(state.Mandates, outgoing.Id, date));
                state.Government = null;
            }

            CoalitionFormationResult formation = CoalitionFormation.Form(
                saveGuid, date, election.Id, state.Settings.System, election.Seats, state.Parties,
                election.MayorPartyId, tuning);

            if (formation.Succeeded)
            {
                state.Government = formation.Government;
                ApplyGovernmentToParties(state.Parties, formation.Government!);
            }
            else
            {
                // Nobody could form a government. The calendar gets a fresh ballot rather than the
                // save being left permanently ungoverned.
                if (formation.SnapElectionDate.HasValue)
                    state.NextElectionDate = formation.SnapElectionDate.Value;

                result.Warnings.Add("No government could be formed after " + election.Id +
                                    "; a fresh ballot is set for " + state.NextElectionDate + ".");
            }

            result.Election = election;
            result.GovernmentChanged = true;
        }

        private static ElectionResult RunFptp(PoliticalState state, Guid saveGuid, SimDate date,
                                              string electionId, bool isSnap, AffinityResult affinity,
                                              TurnoutProjection projection, PollResult? finalPoll,
                                              EngineTuning tuning)
        {
            var input = new FptpElectionInput
            {
                SaveGuid = saveGuid,
                Date = date,
                Id = electionId,
                TermNumber = state.TermNumber + 1,
                IsSnapElection = isSnap,
                Parties = state.Parties,
                Affinities = affinity.Affinities,
                Turnouts = AllBlocTurnouts(projection),
                IncumbentMayorPartyId = state.MayorPartyId,
                FinalPoll = finalPoll
            };

            return FptpElection.Run(input, tuning);
        }

        private static ElectionResult RunProportional(PoliticalState state, Guid saveGuid, SimDate date,
                                                      string electionId, bool isSnap, CitySnapshot? snapshot,
                                                      TurnoutProjection projection, PollResult? finalPoll,
                                                      EngineTuning tuning)
        {
            List<PartyVoteShare> shares = state.CurrentVoteShares;
            int totalVotes = projection.TotalProjectedVotes;

            int population = snapshot != null ? snapshot.Population : 0;
            int chamber = ProportionalAllocator.ChamberSize(population, tuning.ElectionsPr);

            // No district seats: electionsPr.districtSeatShare ships at 0, so the whole chamber is a
            // single national list. Passing null rather than an empty list says "there were no
            // district contests" rather than "every party won nothing in them".
            SeatAllocationResult allocation = ProportionalAllocator.Allocate(
                VoteCounts.FromShares(shares, totalVotes), chamber, null, tuning, saveGuid, date, electionId);

            var election = new ElectionResult
            {
                Id = electionId,
                Date = date,
                System = ElectoralSystem.Proportional,
                TermNumber = state.TermNumber + 1,
                IsSnapElection = isSnap,
                PartyIdsOnBallot = PartyIds(OnBallot(state.Parties)),
                CityVoteShares = new List<PartyVoteShare>(shares),
                Districts = CloneDistrictResults(state.CurrentDistrictStandings),
                Seats = allocation.Seats,
                TotalSeats = allocation.TotalSeats,
                Turnout = projection.CityTurnout,
                TotalVotesCast = totalVotes,
                TotalEligibleVoters = projection.TotalEligibleVoters,
                MayorPartyId = null,
                FinalPollDeviation = finalPoll == null
                    ? 0.0
                    : PollingEngine.MeanAbsoluteDeviation(finalPoll.Shares, shares)
            };

            return election;
        }

        // ------------------------------------------------------------------ government between elections

        private static void TickGovernment(PoliticalState state, EngineTickResult result, Guid saveGuid,
                                           SimDate date, int fulfilled, int defied,
                                           List<int> eventSeverities, EngineTuning tuning)
        {
            Coalition? government = state.Government;
            if (government == null) return;
            if (government.Status != CoalitionStatus.Governing && government.Status != CoalitionStatus.Minority)
                return;

            var inputs = new CoalitionTickInputs
            {
                MonthsElapsed = tuning.Scheduler.TickIntervalMonths <= 0 ? 1 : tuning.Scheduler.TickIntervalMonths,
                FailedMandates = defied,
                FulfilledMandates = fulfilled,
                EventSeverities = eventSeverities,
                Parties = new List<Party>(state.Parties),
                Seats = LastElectionSeats(state),
                TermExpired = false
            };

            CoalitionTickResult tick = CoalitionStability.Advance(government, inputs, saveGuid, date, tuning);
            tick.ApplyTo(government);

            if (!tick.Ended) return;

            // The government fell. Its promises are abandoned rather than failed — they were never
            // given the term they were measured over.
            state.CoalitionHistory.Add(government);
            state.Mandates = new List<Mandate>(MandateMonitor.AbandonAll(state.Mandates, government.Id, date));
            state.Government = null;
            ClearGovernmentFromParties(state.Parties);

            if (tick.SnapElectionDate.HasValue) state.NextElectionDate = tick.SnapElectionDate.Value;

            // A government falling over mid-term is establishment failure by any reading.
            if (FringeActive(state, tuning)) state.Fringe.GovernmentChanges++;

            result.GovernmentChanged = true;
            result.Warnings.Add("Government " + government.Id + " ended at " + date + " (" +
                                tick.CollapseReason + "); next ballot " + state.NextElectionDate + ".");
        }

        // ------------------------------------------------------------------ lifecycle

        private static void RunLifecycle(PoliticalState state, EngineTickResult result, Guid saveGuid,
                                         SimDate date, AgoraSettings settings, EngineTuning tuning,
                                         IssueClimate climate)
        {
            var lifecycleInput = new PartyLifecycleInput
            {
                SaveGuid = saveGuid,
                Date = date,
                Theme = settings.Theme,
                Parties = state.Parties,
                Factions = state.Factions,
                LastElection = result.Election,
                CityGrievance = climate.Grievance
            };

            PartyLifecycleOutcome outcome = PartyLifecycle.Advance(lifecycleInput, tuning);
            state.Parties = new List<Party>(outcome.Parties);

            if (FactionModel.AppliesTo(settings))
            {
                FactionCycleResult factions = FactionModel.Advance(
                    state.Parties, state.Factions, state.Blocs, saveGuid, date, tuning);

                state.Factions = factions.Factions;
                FactionModel.ApplyFactionIds(state.Parties, state.Factions);
                FactionModel.ApplyPlatforms(state.Parties, factions);
            }
        }

        // ------------------------------------------------------------------ standings

        /// <summary>
        /// Turns per-bloc preference into district and city vote shares, weighted by the votes each
        /// bloc is projected to actually cast.
        /// </summary>
        /// <remarks>
        /// Summation order is fixed: districts in the projection's own (sorted) order, then blocs in
        /// <see cref="DistrictTurnout.Blocs"/> order, which is bloc-ordinal ascending. Floating-point
        /// addition is not associative, so this is the difference between a reproducible state hash
        /// and a desync that only shows up on someone else's machine.
        /// </remarks>
        private static List<PartyVoteShare> AggregateStandings(AffinityResult affinity,
                                                               TurnoutProjection projection,
                                                               out List<DistrictResult> districts)
        {
            districts = new List<DistrictResult>();

            // Bloc shares indexed for lookup only — never enumerated, so its ordering cannot leak.
            var byBloc = new Dictionary<string, BlocVoteShares>(StringComparer.Ordinal);
            for (int i = 0; i < affinity.BlocShares.Count; i++)
            {
                BlocVoteShares b = affinity.BlocShares[i];
                byBloc[b.DistrictId + "|" + b.Bloc.Ordinal.ToString()] = b;
            }

            var cityVotes = new Dictionary<string, double>(StringComparer.Ordinal);
            double cityTotal = 0.0;

            for (int d = 0; d < projection.Districts.Count; d++)
            {
                DistrictTurnout district = projection.Districts[d];

                var districtVotes = new Dictionary<string, double>(StringComparer.Ordinal);
                double districtTotal = 0.0;

                for (int b = 0; b < district.Blocs.Count; b++)
                {
                    BlocTurnout bloc = district.Blocs[b];
                    if (bloc.ProjectedVotes <= 0) continue;

                    BlocVoteShares shares;
                    if (!byBloc.TryGetValue(district.DistrictId + "|" + bloc.Bloc.Ordinal.ToString(), out shares))
                        continue;

                    for (int p = 0; p < shares.Shares.Count; p++)
                    {
                        PartyVoteShare share = shares.Shares[p];
                        double votes = share.Share * bloc.ProjectedVotes;

                        Accumulate(districtVotes, share.PartyId, votes);
                        Accumulate(cityVotes, share.PartyId, votes);
                        districtTotal += votes;
                        cityTotal += votes;
                    }
                }

                List<PartyVoteShare> districtShares = ToShares(districtVotes, districtTotal);

                var districtResult = new DistrictResult
                {
                    DistrictId = district.DistrictId,
                    Shares = districtShares,
                    Turnout = district.Turnout,
                    VotesCast = district.ProjectedVotes,
                    EligibleVoters = district.EligibleVoters,
                    Seats = 0
                };

                SetWinner(districtResult);
                districts.Add(districtResult);
            }

            return ToShares(cityVotes, cityTotal);
        }

        private static void Accumulate(Dictionary<string, double> totals, string partyId, double votes)
        {
            double current;
            totals[partyId] = totals.TryGetValue(partyId, out current) ? current + votes : votes;
        }

        /// <summary>
        /// Normalises accumulated votes into shares sorted by party id — the contractual order for
        /// every <see cref="PartyVoteShare"/> list.
        /// </summary>
        private static List<PartyVoteShare> ToShares(Dictionary<string, double> votes, double total)
        {
            var ids = new List<string>(votes.Count);
            foreach (KeyValuePair<string, double> entry in votes) ids.Add(entry.Key);

            // The dictionary was a scratch accumulator; sorting here is what makes the output order
            // independent of the insertion order above.
            ids.Sort(CompareOrdinal);

            var shares = new List<PartyVoteShare>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                double share = total > 0.0 ? votes[ids[i]] / total : 0.0;
                shares.Add(new PartyVoteShare(ids[i], share));
            }

            return shares;
        }

        private static void SetWinner(DistrictResult district)
        {
            double best = -1.0;
            double second = -1.0;
            string winner = "";

            for (int i = 0; i < district.Shares.Count; i++)
            {
                double share = district.Shares[i].Share;
                if (share > best)
                {
                    second = best;
                    best = share;
                    winner = district.Shares[i].PartyId;
                }
                else if (share > second)
                {
                    second = share;
                }
            }

            district.WinningPartyId = winner;
            district.Margin = best > 0.0 && second > 0.0 ? best - second : (best > 0.0 ? best : 0.0);
        }

        // ------------------------------------------------------------------ polling

        private static PollResult? RunPoll(Guid saveGuid, SimDate date, SimDate electionDate,
                                           CitySnapshot? snapshot, List<DistrictResult> standings,
                                           TurnoutProjection projection, EngineTuning tuning)
        {
            if (snapshot == null || standings.Count == 0) return null;

            string? pollsterId = PollSchedule.PollsterForDate(date, electionDate, tuning);
            if (pollsterId == null) return null;

            var request = new PollRequest
            {
                SaveGuid = saveGuid,
                Date = date,
                ElectionDate = electionDate,
                PollsterId = pollsterId,
                IsPublished = true
            };

            for (int i = 0; i < standings.Count; i++)
            {
                DistrictResult standing = standings[i];

                DistrictSnapshot? district = MandateMetrics.FindDistrict(snapshot, standing.DistrictId);
                if (district == null) continue;

                request.Districts.Add(DistrictPollInput.FromSnapshot(
                    district, standing.Shares, projection.TurnoutFor(standing.DistrictId),
                    standing.EligibleVoters));
            }

            if (request.Districts.Count == 0) return null;

            return PollingEngine.Run(request, tuning);
        }

        // ------------------------------------------------------------------ write-backs

        private static void ApplyPreviousVote(List<Bloc> blocs, AffinityResult affinity)
        {
            var byBloc = new Dictionary<string, BlocVoteShares>(StringComparer.Ordinal);
            for (int i = 0; i < affinity.BlocShares.Count; i++)
            {
                BlocVoteShares b = affinity.BlocShares[i];
                byBloc[b.DistrictId + "|" + b.Bloc.Ordinal.ToString()] = b;
            }

            for (int i = 0; i < blocs.Count; i++)
            {
                Bloc bloc = blocs[i];

                BlocVoteShares shares;
                if (!byBloc.TryGetValue(bloc.DistrictId + "|" + bloc.Key.Ordinal.ToString(), out shares))
                    continue;

                bloc.PreviousVote = new List<PartyVoteShare>(shares.Shares);
            }
        }

        private static void ApplyElectionToParties(List<Party> parties, ElectionResult election)
        {
            for (int i = 0; i < parties.Count; i++)
            {
                Party party = parties[i];

                party.LastVoteShare = ShareFor(election.CityVoteShares, party.Id);
                party.SeatsHeld = SeatsFor(election.Seats, party.Id);
                party.LastManifesto = party.Platform;
            }
        }

        private static void ApplyGovernmentToParties(List<Party> parties, Coalition government)
        {
            for (int i = 0; i < parties.Count; i++)
            {
                Party party = parties[i];
                bool member = government.MemberPartyIds.Contains(party.Id);

                party.IsInGovernment = member;
                party.IsIncumbent = string.CompareOrdinal(party.Id, government.LeadPartyId) == 0;
            }
        }

        private static void ClearGovernmentFromParties(List<Party> parties)
        {
            for (int i = 0; i < parties.Count; i++)
            {
                parties[i].IsInGovernment = false;
                parties[i].IsIncumbent = false;
            }
        }

        // ------------------------------------------------------------------ state plumbing

        /// <summary>
        /// A copy safe to mutate. History lists (<see cref="PoliticalState.ElectionHistory"/>,
        /// <see cref="PoliticalState.RecentPolls"/>) share their element references deliberately: those
        /// types are documented as immutable once written, and deep-copying a century of elections
        /// every month would be the single most expensive thing the engine does.
        /// </summary>
        private static PoliticalState CloneState(PoliticalState source, SimDate date)
        {
            var clone = new PoliticalState
            {
                SchemaVersion = source.SchemaVersion,
                SaveGuid = source.SaveGuid,
                Date = date,
                // Carried, not defaulted. This is a hand-maintained field list, and a scalar left
                // out of it does not fail to compile — it silently arrives at the property default.
                // For this field that default is -1, meaning "no month has ever completed", so
                // omitting it here would tell the tick gate that every month is fresh and hand the
                // caller back a state that re-runs the month it just finished. Retheme is the live
                // caller that would have hit it: it clones at the current date, mid-month.
                LastCompletedTickMonth = source.LastCompletedTickMonth,
                Settings = source.Settings ?? new AgoraSettings(),
                Parties = PartyRegistry.CloneAll(source.Parties ?? new List<Party>()),
                Factions = new List<Faction>(source.Factions ?? new List<Faction>()),
                Blocs = new List<Bloc>(source.Blocs ?? new List<Bloc>()),
                CurrentVoteShares = new List<PartyVoteShare>(source.CurrentVoteShares ?? new List<PartyVoteShare>()),
                CurrentDistrictStandings = CloneDistrictResults(source.CurrentDistrictStandings),
                RecentPolls = new List<PollResult>(source.RecentPolls ?? new List<PollResult>()),
                ElectionHistory = new List<ElectionResult>(source.ElectionHistory ?? new List<ElectionResult>()),
                Government = CloneCoalition(source.Government),
                CoalitionHistory = new List<Coalition>(source.CoalitionHistory ?? new List<Coalition>()),
                Mandates = CloneMandates(source.Mandates),
                ActiveEvents = new List<TimelineEvent>(source.ActiveEvents ?? new List<TimelineEvent>()),
                FiredEventIds = new List<string>(source.FiredEventIds ?? new List<string>()),
                Indices = source.Indices,
                // Deep-copied, not shared: the watch is mutated on every tick, so an alias would let
                // a speculative advance write back into the state the caller still holds.
                Fringe = (source.Fringe ?? new FringeWatch()).Clone(),
                TermNumber = source.TermNumber,
                NextElectionDate = source.NextElectionDate,
                IsCampaignSeason = source.IsCampaignSeason,
                MayorPartyId = source.MayorPartyId,
                LastFlavorDate = source.LastFlavorDate,

                // Story state. LiveStories, EventPool and Power are DEEP-copied because all three are
                // mutated during a tick — a story's slots take the player's response and its outcome,
                // a pool entry ages its MissStreak, the power ledger grows — and an alias would let a
                // speculative advance write into the prior state the caller still holds. That is the
                // hazard ActiveEvents above still carries, since it is only a shallow list copy.
                //
                // StoryArchive and PlayerCommands are shallow, deliberately and for the same reason
                // ElectionHistory is: both are append-only records of things that already happened,
                // and deep-copying a century of them every month would be waste.
                LiveStories = CloneStories(source.LiveStories),
                StoryArchive = new List<Story>(source.StoryArchive ?? new List<Story>()),
                EventPool = ClonePool(source.EventPool),
                Power = (source.Power ?? new PoliticalPowerState()).Clone(),
                PlayerCommands = new List<PlayerCommand>(source.PlayerCommands ?? new List<PlayerCommand>()),
                LastStoryDraftMonth = source.LastStoryDraftMonth,
                LastStoryResolveMonth = source.LastStoryResolveMonth
            };

            return clone;
        }

        private static List<Story> CloneStories(List<Story>? source)
        {
            var result = new List<Story>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++) result.Add(source[i].Clone());
            return result;
        }

        private static List<EventPoolEntry> ClonePool(List<EventPoolEntry>? source)
        {
            var result = new List<EventPoolEntry>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++) result.Add(source[i].Clone());
            return result;
        }

        private static Coalition? CloneCoalition(Coalition? source)
        {
            if (source == null) return null;

            return new Coalition
            {
                SchemaVersion = source.SchemaVersion,
                Id = source.Id,
                FormedDate = source.FormedDate,
                EndedDate = source.EndedDate,
                MemberPartyIds = new List<string>(source.MemberPartyIds),
                LeadPartyId = source.LeadPartyId,
                OppositionPartyIds = new List<string>(source.OppositionPartyIds),
                Seats = source.Seats,
                SeatShare = source.SeatShare,
                HasMajority = source.HasMajority,
                Cohesion = source.Cohesion,
                Stability = source.Stability,
                Status = source.Status,
                CollapseReason = source.CollapseReason,
                FormationAttempts = source.FormationAttempts,
                ElectionId = source.ElectionId,
                MandateIds = new List<string>(source.MandateIds)
            };
        }

        private static List<Mandate> CloneMandates(List<Mandate>? source)
        {
            var clone = new List<Mandate>();
            if (source == null) return clone;

            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null) clone.Add(MandateMonitor.Clone(source[i]));
            }
            return clone;
        }

        private static List<DistrictResult> CloneDistrictResults(List<DistrictResult>? source)
        {
            var clone = new List<DistrictResult>();
            if (source == null) return clone;

            for (int i = 0; i < source.Count; i++)
            {
                DistrictResult d = source[i];
                if (d == null) continue;

                clone.Add(new DistrictResult
                {
                    DistrictId = d.DistrictId,
                    Shares = new List<PartyVoteShare>(d.Shares),
                    Turnout = d.Turnout,
                    VotesCast = d.VotesCast,
                    EligibleVoters = d.EligibleVoters,
                    WinningPartyId = d.WinningPartyId,
                    Margin = d.Margin,
                    Seats = d.Seats,
                    DecidedByTieBreak = d.DecidedByTieBreak
                });
            }

            return clone;
        }

        /// <summary>Puts every list back into its contractual order before the state leaves.</summary>
        private static void Normalize(PoliticalState state)
        {
            state.Parties.Sort(PartyRegistry.CompareById);
            state.Factions.Sort(CompareFactions);
            state.Blocs.Sort(CompareBlocs);
            state.Mandates.Sort(CompareMandates);
            state.ActiveEvents.Sort(CompareEvents);
            state.CurrentDistrictStandings.Sort(CompareDistrictResults);
            state.FiredEventIds.Sort(CompareOrdinal);

            if (state.Government != null)
            {
                state.Government.MemberPartyIds.Sort(CompareOrdinal);
                state.Government.OppositionPartyIds.Sort(CompareOrdinal);
                state.Government.MandateIds.Sort(CompareOrdinal);
            }
        }

        // ------------------------------------------------------------------ small readers

        private static IReadOnlyList<Party> OnBallot(IReadOnlyList<Party> parties)
        {
            var ballot = new List<Party>();
            for (int i = 0; i < parties.Count; i++)
            {
                if (parties[i] != null && PartyRegistry.IsOnBallot(parties[i])) ballot.Add(parties[i]);
            }
            return ballot;
        }

        private static List<string> PartyIds(IReadOnlyList<Party> parties)
        {
            var ids = new List<string>(parties.Count);
            for (int i = 0; i < parties.Count; i++) ids.Add(parties[i].Id);
            ids.Sort(CompareOrdinal);
            return ids;
        }

        private static List<string> DistrictIds(CitySnapshot? snapshot)
        {
            var ids = new List<string>();
            if (snapshot == null) return ids;

            for (int i = 0; i < snapshot.Districts.Count; i++)
            {
                string id = snapshot.Districts[i].Id;
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }

            ids.Sort(CompareOrdinal);
            return ids;
        }

        private static List<BlocTurnout> AllBlocTurnouts(TurnoutProjection projection)
        {
            var turnouts = new List<BlocTurnout>();
            for (int d = 0; d < projection.Districts.Count; d++)
            {
                DistrictTurnout district = projection.Districts[d];
                for (int b = 0; b < district.Blocs.Count; b++) turnouts.Add(district.Blocs[b]);
            }
            return turnouts;
        }

        private static List<string> MergeIds(List<string> existing, List<string> added)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var merged = new List<string>();

            for (int i = 0; i < existing.Count; i++)
                if (!string.IsNullOrEmpty(existing[i]) && seen.Add(existing[i])) merged.Add(existing[i]);

            for (int i = 0; i < added.Count; i++)
                if (!string.IsNullOrEmpty(added[i]) && seen.Add(added[i])) merged.Add(added[i]);

            merged.Sort(CompareOrdinal);
            return merged;
        }

        /// <summary>
        /// The share of the vote the government currently commands, 0–1. Zero when nobody is
        /// governing, which <see cref="PoliticalPower.AccrualFor"/> reads as "no accrual" rather than
        /// as a penalty.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A coalition's share is the sum of its members', not the lead party's alone: political
        /// power is what the government can spend, and a minority lead party propped up by two
        /// partners governs on all three parties' votes. Summed in the state's own declared list
        /// order rather than over a lookup, so the figure does not depend on collection order.
        /// </para>
        /// <para>
        /// Falls back to <see cref="PoliticalState.MayorPartyId"/> when there is no coalition — an
        /// FPTP save has a mayor and no government object, and reading only the coalition would tell
        /// every such save that nobody governs and freeze the currency for the whole game.
        /// </para>
        /// </remarks>
        private static double GoverningVoteShare(PoliticalState state)
        {
            List<PartyVoteShare> shares = state.CurrentVoteShares;
            if (shares == null || shares.Count == 0) return 0.0;

            Coalition? government = state.Government;
            List<string>? members = government != null && government.Status != CoalitionStatus.Negotiating
                ? government.MemberPartyIds
                : null;

            double total = 0.0;
            for (int i = 0; i < shares.Count; i++)
            {
                PartyVoteShare s = shares[i];
                if (string.IsNullOrEmpty(s.PartyId)) continue;

                bool governing = members != null && members.Count > 0
                    ? members.Contains(s.PartyId)
                    : !string.IsNullOrEmpty(state.MayorPartyId) &&
                      string.Equals(s.PartyId, state.MayorPartyId, StringComparison.Ordinal);

                if (governing) total += s.Share;
            }

            if (double.IsNaN(total) || total <= 0.0) return 0.0;
            return total > 1.0 ? 1.0 : total;
        }

        private static SimDate? LastElectionDate(PoliticalState state) =>
            state.ElectionHistory.Count == 0
                ? (SimDate?)null
                : state.ElectionHistory[state.ElectionHistory.Count - 1].Date;

        private static double? LastElectionTurnout(PoliticalState state) =>
            state.ElectionHistory.Count == 0
                ? (double?)null
                : state.ElectionHistory[state.ElectionHistory.Count - 1].Turnout;

        private static List<SeatAllocation> LastElectionSeats(PoliticalState state) =>
            state.ElectionHistory.Count == 0
                ? new List<SeatAllocation>()
                : new List<SeatAllocation>(state.ElectionHistory[state.ElectionHistory.Count - 1].Seats);

        private static PollResult? LastPublishedPoll(PoliticalState state)
        {
            for (int i = state.RecentPolls.Count - 1; i >= 0; i--)
            {
                if (state.RecentPolls[i].IsPublished) return state.RecentPolls[i];
            }
            return null;
        }

        private static int IncumbentTerms(PoliticalState state)
        {
            if (state.Government == null) return 0;

            int terms = 0;
            for (int i = 0; i < state.CoalitionHistory.Count; i++)
            {
                if (string.CompareOrdinal(state.CoalitionHistory[i].LeadPartyId,
                                          state.Government.LeadPartyId) == 0) terms++;
            }
            return terms;
        }

        /// <summary>
        /// Rewrites every on-ballot party's manifesto against the current grievance vector, leaving
        /// parties that are off the ballot exactly as they were.
        /// </summary>
        /// <remarks>
        /// The returned list is sorted by id, like every other party list the engine hands on. Each
        /// party is refreshed through <see cref="PartyPlatform.RefreshManifesto"/>, which clones
        /// rather than mutating and draws its jitter from a per-party sub-stream — so adding a party
        /// cannot shift another party's drift, and the pass is order-free.
        /// </remarks>
        private static List<Party> RefreshManifestos(IReadOnlyList<Party> parties, Guid saveGuid,
                                                     SimDate date, IssueWeights grievance,
                                                     EngineTuning tuning)
        {
            var refreshed = new List<Party>(parties.Count);

            for (int i = 0; i < parties.Count; i++)
            {
                Party p = parties[i];
                if (p == null) continue;

                refreshed.Add(PartyRegistry.IsOnBallot(p)
                    ? PartyPlatform.RefreshManifesto(saveGuid, date, p, grievance, tuning)
                    : p);
            }

            refreshed.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return refreshed;
        }

        /// <summary>
        /// Whether this save keeps a fringe failure ledger. Proportional saves do not: the ceiling is
        /// FPTP-only, so recording its inputs there would be churn nothing ever reads — and it would
        /// make "the packet is inert under PR" an untestable claim, since the watch is persisted.
        /// </summary>
        private static bool FringeActive(PoliticalState state, EngineTuning tuning) =>
            tuning.Fringe.Enabled && state.Settings.System == ElectoralSystem.FirstPastThePost;

        /// <summary>
        /// Whether a resolution's owning party is one of the NA majors. Linear scan of a list that is
        /// never more than <c>parties.maxPartiesTotal</c> long, and correct regardless of its order.
        /// </summary>
        private static bool IsMajorParty(IReadOnlyList<Party> parties, string partyId)
        {
            if (parties == null || string.IsNullOrEmpty(partyId)) return false;

            for (int i = 0; i < parties.Count; i++)
                if (parties[i] != null && string.Equals(parties[i].Id, partyId, StringComparison.Ordinal))
                    return parties[i].IsMajor;

            return false;
        }

        /// <summary>
        /// True when this ballot was forced rather than scheduled: the previous government ended before
        /// its term ran out.
        /// </summary>
        private static bool IsSnapElection(PoliticalState state, SimDate date, EngineTuning tuning)
        {
            if (state.ElectionHistory.Count == 0) return false;

            ElectionResult last = state.ElectionHistory[state.ElectionHistory.Count - 1];
            int termMonths = TermMonths(state.Settings.System, tuning);
            return last.Date.MonthsUntil(date) < termMonths;
        }

        private static int TermMonths(ElectoralSystem system, EngineTuning tuning)
        {
            int years = system == ElectoralSystem.FirstPastThePost
                ? tuning.ElectionsFptp.TermYears
                : tuning.ElectionsPr.TermYears;

            return (years < 1 ? 1 : years) * 12;
        }

        /// <summary>
        /// How hard the campaign is being fought, 0 outside campaign season rising to 1 on polling day.
        /// </summary>
        private static double CampaignIntensity(SimDate date, SimDate? electionDate, EngineTuning tuning)
        {
            if (!electionDate.HasValue) return 0.0;

            int months = date.MonthsUntil(electionDate.Value);
            if (months < 0) return 0.0;

            int window = tuning.Scheduler.CampaignStartMonthsBeforeElection;
            if (window <= 0) return months == 0 ? 1.0 : 0.0;
            if (months > window) return 0.0;

            return Clamp01(1.0 - ((double)months / window));
        }

        private static double ShareFor(List<PartyVoteShare> shares, string partyId)
        {
            for (int i = 0; i < shares.Count; i++)
            {
                if (string.CompareOrdinal(shares[i].PartyId, partyId) == 0) return shares[i].Share;
            }
            return 0.0;
        }

        private static int SeatsFor(List<SeatAllocation> seats, string partyId)
        {
            for (int i = 0; i < seats.Count; i++)
            {
                if (string.CompareOrdinal(seats[i].PartyId, partyId) == 0) return seats[i].Seats;
            }
            return 0;
        }

        // netstandard2.0 has no Math.Clamp.
        private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);

        // ------------------------------------------------------------------ comparers

        private static int CompareOrdinal(string a, string b) => string.CompareOrdinal(a, b);

        private static int CompareFactions(Faction a, Faction b) => string.CompareOrdinal(a.Id, b.Id);

        private static int CompareMandates(Mandate a, Mandate b) => string.CompareOrdinal(a.Id, b.Id);

        private static int CompareEvents(TimelineEvent a, TimelineEvent b) => string.CompareOrdinal(a.Id, b.Id);

        private static int CompareDistrictResults(DistrictResult a, DistrictResult b) =>
            string.CompareOrdinal(a.DistrictId, b.DistrictId);

        private static int CompareBlocs(Bloc a, Bloc b)
        {
            int byDistrict = string.CompareOrdinal(a.DistrictId, b.DistrictId);
            return byDistrict != 0 ? byDistrict : a.Key.Ordinal.CompareTo(b.Key.Ordinal);
        }
    }
}
