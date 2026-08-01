using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Parties
{
    /// <summary>What happened to a party during one lifecycle cycle.</summary>
    public enum PartyChangeKind
    {
        /// <summary>Fell into the warning band, or took its first sub-threshold result.</summary>
        Endangered = 0,

        /// <summary>Climbed back out of the warning band; the death counter is reset.</summary>
        Recovered = 1,

        /// <summary>Dissolved after <c>parties.deathConsecutiveElections</c> sub-threshold results.</summary>
        Dissolved = 2,

        /// <summary>Earned dissolution but was held on the ballot to keep the field above the floor.</summary>
        DeathDeferred = 3,

        /// <summary>A dissolved brand returned because its core grievance resurged.</summary>
        Revived = 4,

        /// <summary>The party a splinter broke away from.</summary>
        SplitParent = 5,

        /// <summary>The splinter itself. <c>CounterpartPartyId</c> is the parent.</summary>
        SplitFounded = 6,

        /// <summary>Absorbed into another party. <c>CounterpartPartyId</c> is the survivor.</summary>
        MergedAway = 7,

        /// <summary>Absorbed another party. <c>CounterpartPartyId</c> is the party taken over.</summary>
        MergedInto = 8,

        /// <summary>A brand-new party entered the field.</summary>
        Founded = 9
    }

    /// <summary>
    /// Machine-readable reason codes. Stable constants, not prose — the news layer turns these into
    /// language, and the engine must never read prose back (non-negotiable #1).
    /// </summary>
    public static class PartyChangeReasons
    {
        public const string BelowDeathThreshold = "election-below-death-threshold";
        public const string BelowEndangeredThreshold = "election-below-endangered-threshold";
        public const string RecoveredAboveThreshold = "election-recovered";
        public const string ConsecutiveSubThresholdResults = "death-consecutive-sub-threshold";
        public const string HeldByMinimumPartyCount = "death-deferred-min-party-count";
        public const string GrievanceResurgence = "revival-grievance-resurgence";
        public const string InternalTension = "split-internal-tension";
        public const string PlatformConvergence = "merge-platform-convergence";
        public const string NewEntry = "new-party-entry";
    }

    /// <summary>One lifecycle transition. Ordered by the stage that produced it, then by party id.</summary>
    public sealed class PartyChange
    {
        public PartyChangeKind Kind { get; set; }

        public string PartyId { get; set; } = "";

        /// <summary>The other party in a split or merge; null otherwise.</summary>
        public string? CounterpartPartyId { get; set; }

        public SimDate Date { get; set; }

        /// <summary>A <see cref="PartyChangeReasons"/> constant.</summary>
        public string ReasonCode { get; set; } = "";

        public override string ToString() =>
            Kind + ":" + PartyId + (CounterpartPartyId == null ? "" : "->" + CounterpartPartyId) +
            "@" + Date + "(" + ReasonCode + ")";
    }

    /// <summary>
    /// Everything <see cref="PartyLifecycle.Advance"/> reads. A single input object rather than a
    /// long parameter list, because the packet is consumed by several others and an added optional
    /// field should not break every call site.
    /// </summary>
    public sealed class PartyLifecycleInput
    {
        public Guid SaveGuid { get; set; }

        /// <summary>The date the cycle runs on. Feeds every seeded draw.</summary>
        public SimDate Date { get; set; }

        public RegionTheme Theme { get; set; } = RegionTheme.Eu;

        /// <summary>Current parties, any status. Never mutated.</summary>
        public IReadOnlyList<Party> Parties { get; set; } = new Party[0];

        /// <summary>
        /// Factions, read only for <see cref="Faction.TensionWithParty"/>. Empty in the EU theme.
        /// Faction lifecycle itself belongs to the factions packet.
        /// </summary>
        public IReadOnlyList<Faction> Factions { get; set; } = new Faction[0];

        /// <summary>
        /// The election that has just been counted, or null when the cycle runs between elections
        /// (only revival, merge, split and entry can happen then).
        /// </summary>
        public ElectionResult? LastElection { get; set; }

        /// <summary>
        /// Per-issue city grievance, each component in [0,1] (clamped defensively). Supplied by the
        /// derived-indices packet; drives revival.
        /// </summary>
        public IssueWeights CityGrievance { get; set; }

        /// <summary>Archetype catalog for new brands. Null uses <see cref="PartyArchetypes.For"/>.</summary>
        public IReadOnlyList<PartyArchetype>? Archetypes { get; set; }
    }

    /// <summary>The result of one lifecycle cycle. Both lists are fresh objects; nothing is shared.</summary>
    public sealed class PartyLifecycleOutcome
    {
        /// <summary>Every party including dissolved and merged brands, sorted by <see cref="Party.Id"/>.</summary>
        public IReadOnlyList<Party> Parties { get; }

        /// <summary>Transitions in stage order: results, deaths, revivals, merges, splits, entry.</summary>
        public IReadOnlyList<PartyChange> Changes { get; }

        public PartyLifecycleOutcome(IReadOnlyList<Party> parties, IReadOnlyList<PartyChange> changes)
        {
            Parties = parties;
            Changes = changes;
        }
    }

    /// <summary>
    /// The EU party lifecycle: parties split, merge, die below <c>parties.deathVoteShareThreshold</c>
    /// across <c>parties.deathConsecutiveElections</c> consecutive elections, and revive when the
    /// grievance their brand owns resurges (<c>politicsmodplan.md</c> §3).
    ///
    /// <para>
    /// <see cref="Advance"/> is a pure function of (parties, factions, election result, grievance,
    /// tuning, save guid, date). Every stochastic gate is a single <see cref="DeterministicRng.NextBool"/>
    /// on a per-entity sub-stream of <see cref="StreamNames.PartyLifecycle"/>, so adding a party
    /// cannot perturb another party's roll.
    /// </para>
    ///
    /// <para>
    /// Stage order is fixed and a party may take at most one structural transition per cycle:
    /// <b>apply results → deaths → revivals → merges → splits → new entry</b>. Deaths run first so a
    /// party cannot merge its way out of dissolution; revivals run before merges and splits so a
    /// returning brand outranks a random new entry for the last slot on the ballot.
    /// </para>
    ///
    /// <para>
    /// In the NA theme only the result-driven stages run. Party-level structural change there is
    /// handled by the factions packet through <c>factions.naPartyLifecycleProbability</c>; running
    /// both would double-count it.
    /// </para>
    /// </summary>
    public static class PartyLifecycle
    {
        private static readonly Party[] NoParties = new Party[0];

        /// <summary>
        /// Whether <c>parties.lifecycleCheckIntervalMonths</c> has elapsed. The caller owns the
        /// schedule; <see cref="Advance"/> itself has no notion of "when".
        /// </summary>
        public static bool IsDue(SimDate lastCheck, SimDate now, EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            int interval = tuning.Parties.LifecycleCheckIntervalMonths;
            if (interval < 1) interval = 1;
            return lastCheck.MonthsUntil(now) >= interval;
        }

        /// <summary>
        /// How much strain a party is under, in [0,1]. The larger of
        /// (a) how far the leadership has moved from the manifesto it was elected on, and
        /// (b) the tension of its most disaffected live faction.
        /// </summary>
        /// <remarks>
        /// A maximum, not a sum, so the measure means the same thing in the EU theme (where there are
        /// no factions) as in NA. Taking a maximum is also order-independent, which a running sum
        /// over an unordered faction list would not be.
        /// </remarks>
        public static double InternalTension(Party party, IReadOnlyList<Faction>? factions)
        {
            if (party == null) throw new ArgumentNullException(nameof(party));

            double tension = party.Platform.Distance(party.LastManifesto);

            if (factions != null)
            {
                for (int i = 0; i < factions.Count; i++)
                {
                    Faction f = factions[i];
                    if (f == null || string.CompareOrdinal(f.PartyId, party.Id) != 0) continue;
                    if (f.Status == FactionStatus.Dissolved || f.Status == FactionStatus.Merged) continue;
                    if (f.TensionWithParty > tension) tension = f.TensionWithParty;
                }
            }

            return PartyPlatform.Clamp(tension, 0.0, 1.0);
        }

        /// <summary>Runs one lifecycle cycle. Pure — the input lists are never mutated.</summary>
        public static PartyLifecycleOutcome Advance(PartyLifecycleInput input, EngineTuning tuning)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            PartiesTuning t = tuning.Parties;
            List<Party> parties = PartyRegistry.CloneAll(input.Parties ?? NoParties);
            var changes = new List<PartyChange>();
            var touched = new List<string>();

            ApplyElectionResults(input, t, parties, changes);
            ApplyDeaths(input, t, parties, changes, touched);
            ApplyRevivals(input, t, parties, changes, touched);

            if (input.Theme == RegionTheme.Eu)
            {
                ApplyMerges(input, tuning, parties, changes, touched);
                ApplySplits(input, tuning, parties, changes, touched);
                ApplyNewEntry(input, tuning, parties, changes, touched);
            }

            parties.Sort(PartyRegistry.CompareById);
            return new PartyLifecycleOutcome(parties, changes);
        }

        // --- Stage 1: results ----------------------------------------------------------------------

        private static void ApplyElectionResults(PartyLifecycleInput input, PartiesTuning t,
                                                 List<Party> parties, List<PartyChange> changes)
        {
            ElectionResult? election = input.LastElection;
            if (election == null) return;

            for (int i = 0; i < parties.Count; i++)
            {
                Party party = parties[i];
                if (!PartyRegistry.IsOnBallot(party)) continue;
                if (!WasOnBallot(election, party.Id)) continue;

                double share = ShareOf(election.CityVoteShares, party.Id);
                party.LastVoteShare = share;
                party.SeatsHeld = SeatsOf(election.Seats, party.Id);

                if (share < t.DeathVoteShareThreshold) party.ConsecutiveElectionsBelowThreshold++;
                else party.ConsecutiveElectionsBelowThreshold = 0;

                bool endangered = party.ConsecutiveElectionsBelowThreshold >= 1 ||
                                  share < t.EndangeredVoteShareThreshold;

                if (endangered)
                {
                    if (party.Status != PartyStatus.Endangered)
                    {
                        changes.Add(Change(PartyChangeKind.Endangered, party.Id, null, input.Date,
                            party.ConsecutiveElectionsBelowThreshold >= 1
                                ? PartyChangeReasons.BelowDeathThreshold
                                : PartyChangeReasons.BelowEndangeredThreshold));
                    }
                    party.Status = PartyStatus.Endangered;
                }
                else
                {
                    if (party.Status == PartyStatus.Endangered)
                    {
                        changes.Add(Change(PartyChangeKind.Recovered, party.Id, null, input.Date,
                            PartyChangeReasons.RecoveredAboveThreshold));
                    }
                    party.Status = PartyRegistry.HealthyStatus(party);
                }
            }
        }

        // --- Stage 2: death ------------------------------------------------------------------------

        private static void ApplyDeaths(PartyLifecycleInput input, PartiesTuning t,
                                        List<Party> parties, List<PartyChange> changes, List<string> touched)
        {
            int required = t.DeathConsecutiveElections;
            if (required < 1) required = 1;

            var candidates = new List<Party>();
            for (int i = 0; i < parties.Count; i++)
            {
                Party p = parties[i];
                if (!PartyRegistry.IsOnBallot(p)) continue;
                if (p.ConsecutiveElectionsBelowThreshold < required) continue;
                candidates.Add(p);
            }
            if (candidates.Count == 0) return;

            // Worst result dies first; ties break on id so the order never depends on list order.
            candidates.Sort(delegate (Party a, Party b)
            {
                int byShare = a.LastVoteShare.CompareTo(b.LastVoteShare);
                return byShare != 0 ? byShare : string.CompareOrdinal(a.Id, b.Id);
            });

            int floor = MinimumOnBallot(input.Theme, t);
            int onBallot = PartyRegistry.OnBallotCount(parties);

            for (int i = 0; i < candidates.Count; i++)
            {
                Party party = candidates[i];

                if (onBallot - 1 < floor)
                {
                    // An empty ballot is a worse failure than a zombie party. The counter is kept, so
                    // it dies the moment the field can afford it.
                    party.Status = PartyStatus.Endangered;
                    touched.Add(party.Id);
                    changes.Add(Change(PartyChangeKind.DeathDeferred, party.Id, null, input.Date,
                        PartyChangeReasons.HeldByMinimumPartyCount));
                    continue;
                }

                party.Status = PartyStatus.Dissolved;
                party.DissolvedDate = input.Date;
                party.SeatsHeld = 0;
                party.IsIncumbent = false;
                party.IsInGovernment = false;
                onBallot--;
                touched.Add(party.Id);
                changes.Add(Change(PartyChangeKind.Dissolved, party.Id, null, input.Date,
                    PartyChangeReasons.ConsecutiveSubThresholdResults));
            }
        }

        // --- Stage 3: revival ----------------------------------------------------------------------

        private static void ApplyRevivals(PartyLifecycleInput input, PartiesTuning t,
                                          List<Party> parties, List<PartyChange> changes, List<string> touched)
        {
            int cooldown = t.RevivalCooldownMonths;
            if (cooldown < 0) cooldown = 0;

            var candidates = new List<Party>();
            for (int i = 0; i < parties.Count; i++)
            {
                Party p = parties[i];
                if (p.Status != PartyStatus.Dissolved) continue;   // a merged brand is gone for good
                if (Contains(touched, p.Id)) continue;
                if (!p.DissolvedDate.HasValue) continue;
                if (p.DissolvedDate.Value.MonthsUntil(input.Date) < cooldown) continue;
                if (Grievance(input.CityGrievance, p.CoreGrievance) < t.RevivalGrievanceThreshold) continue;
                candidates.Add(p);
            }
            if (candidates.Count == 0) return;

            // Loudest grievance first; then the brand that has come back least often; then id.
            candidates.Sort(delegate (Party a, Party b)
            {
                int byGrievance = Grievance(input.CityGrievance, b.CoreGrievance)
                    .CompareTo(Grievance(input.CityGrievance, a.CoreGrievance));
                if (byGrievance != 0) return byGrievance;
                int byRevivals = a.RevivalCount.CompareTo(b.RevivalCount);
                return byRevivals != 0 ? byRevivals : string.CompareOrdinal(a.Id, b.Id);
            });

            int ceiling = MaximumOnBallot(input.Theme, t);
            int onBallot = PartyRegistry.OnBallotCount(parties);

            for (int i = 0; i < candidates.Count && onBallot < ceiling; i++)
            {
                Party party = candidates[i];
                party.Status = PartyStatus.Revived;
                party.RevivalCount++;
                party.DissolvedDate = null;
                party.ConsecutiveElectionsBelowThreshold = 0;
                party.LastVoteShare = 0.0;
                party.SeatsHeld = 0;
                onBallot++;
                touched.Add(party.Id);
                changes.Add(Change(PartyChangeKind.Revived, party.Id, null, input.Date,
                    PartyChangeReasons.GrievanceResurgence));
            }
        }

        // --- Stage 4: merge ------------------------------------------------------------------------

        private static void ApplyMerges(PartyLifecycleInput input, EngineTuning tuning,
                                        List<Party> parties, List<PartyChange> changes, List<string> touched)
        {
            PartiesTuning t = tuning.Parties;

            var eligible = new List<Party>();
            for (int i = 0; i < parties.Count; i++)
            {
                Party p = parties[i];
                if (PartyRegistry.IsOnBallot(p) && !Contains(touched, p.Id)) eligible.Add(p);
            }
            if (eligible.Count < 2) return;

            var pairs = new List<MergeCandidate>();
            for (int i = 0; i < eligible.Count; i++)
            {
                for (int j = i + 1; j < eligible.Count; j++)
                {
                    Party a = eligible[i];
                    Party b = eligible[j];

                    double affinity = 1.0 - a.Platform.Distance(b.Platform);
                    if (affinity < t.MergeAffinityThreshold) continue;
                    if (a.LastVoteShare + b.LastVoteShare > t.MergeMaxCombinedVoteShare) continue;

                    bool aSurvives = a.LastVoteShare > b.LastVoteShare ||
                                     (a.LastVoteShare == b.LastVoteShare && string.CompareOrdinal(a.Id, b.Id) < 0);

                    pairs.Add(new MergeCandidate(aSurvives ? a : b, aSurvives ? b : a, affinity));
                }
            }
            if (pairs.Count == 0) return;

            // Closest platforms merge first.
            pairs.Sort(delegate (MergeCandidate x, MergeCandidate y)
            {
                int byAffinity = y.Affinity.CompareTo(x.Affinity);
                if (byAffinity != 0) return byAffinity;
                int bySurvivor = string.CompareOrdinal(x.Survivor.Id, y.Survivor.Id);
                return bySurvivor != 0 ? bySurvivor : string.CompareOrdinal(x.Absorbed.Id, y.Absorbed.Id);
            });

            int floor = MinimumOnBallot(input.Theme, t);
            int onBallot = PartyRegistry.OnBallotCount(parties);

            for (int i = 0; i < pairs.Count; i++)
            {
                MergeCandidate pair = pairs[i];
                if (onBallot - 1 < floor) break;
                if (Contains(touched, pair.Survivor.Id) || Contains(touched, pair.Absorbed.Id)) continue;

                var rng = SeedStreams.RngFor(input.SaveGuid, input.Date, StreamNames.PartyLifecycle,
                                             "merge:" + pair.Survivor.Id + "+" + pair.Absorbed.Id);
                if (!rng.NextBool(t.MergeProbabilityPerCycle)) continue;

                Party survivor = pair.Survivor;
                Party absorbed = pair.Absorbed;

                survivor.Platform = PartyPlatform.Blend(survivor.Platform, survivor.LastVoteShare,
                                                        absorbed.Platform, absorbed.LastVoteShare);
                survivor.SeatsHeld += absorbed.SeatsHeld;
                survivor.LastVoteShare += absorbed.LastVoteShare;
                survivor.IsInGovernment = survivor.IsInGovernment || absorbed.IsInGovernment;

                // The factions packet re-parents these off the MergedInto change; the party-side list
                // is moved here so Party never points at a faction of a party that no longer exists.
                for (int f = 0; f < absorbed.FactionIds.Count; f++)
                {
                    if (!Contains(survivor.FactionIds, absorbed.FactionIds[f]))
                        survivor.FactionIds.Add(absorbed.FactionIds[f]);
                }
                survivor.FactionIds.Sort(StringComparer.Ordinal);
                absorbed.FactionIds.Clear();

                absorbed.Status = PartyStatus.Merged;
                absorbed.SuccessorPartyId = survivor.Id;
                absorbed.DissolvedDate = input.Date;
                absorbed.SeatsHeld = 0;
                absorbed.IsIncumbent = false;
                absorbed.IsInGovernment = false;

                onBallot--;
                touched.Add(survivor.Id);
                touched.Add(absorbed.Id);
                changes.Add(Change(PartyChangeKind.MergedInto, survivor.Id, absorbed.Id, input.Date,
                    PartyChangeReasons.PlatformConvergence));
                changes.Add(Change(PartyChangeKind.MergedAway, absorbed.Id, survivor.Id, input.Date,
                    PartyChangeReasons.PlatformConvergence));
            }
        }

        private sealed class MergeCandidate
        {
            public readonly Party Survivor;
            public readonly Party Absorbed;
            public readonly double Affinity;

            public MergeCandidate(Party survivor, Party absorbed, double affinity)
            {
                Survivor = survivor;
                Absorbed = absorbed;
                Affinity = affinity;
            }
        }

        // --- Stage 5: split ------------------------------------------------------------------------

        private static void ApplySplits(PartyLifecycleInput input, EngineTuning tuning,
                                        List<Party> parties, List<PartyChange> changes, List<string> touched)
        {
            PartiesTuning t = tuning.Parties;
            int ceiling = MaximumOnBallot(input.Theme, t);
            int onBallot = PartyRegistry.OnBallotCount(parties);

            // Snapshot before iterating: splinters are appended to `parties` inside the loop.
            var candidates = new List<Party>(parties);

            for (int i = 0; i < candidates.Count; i++)
            {
                if (onBallot >= ceiling) return;
                if (parties.Count >= t.MaxPartiesTotal) return;

                Party party = candidates[i];
                if (!PartyRegistry.IsOnBallot(party)) continue;
                if (Contains(touched, party.Id)) continue;
                if (party.LastVoteShare < t.SplitMinVoteShare) continue;
                if (InternalTension(party, input.Factions) < t.SplitTensionThreshold) continue;

                var rng = SeedStreams.RngFor(input.SaveGuid, input.Date, StreamNames.PartyLifecycle,
                                             "split:" + party.Id);
                if (!rng.NextBool(t.SplitProbabilityPerCycle)) continue;

                string splinterId = PartyRegistry.NextPartyId(parties);
                var platformRng = SeedStreams.RngFor(input.SaveGuid, input.Date, StreamNames.PartyGeneration,
                                                     "split:" + splinterId);

                var splinter = new Party
                {
                    Id = splinterId,
                    ColorHex = PartyRegistry.AllocateColor(parties, parties.Count, tuning),
                    ArchetypeId = party.ArchetypeId,
                    Platform = PartyPlatform.SplinterPlatform(party, tuning, platformRng),
                    Status = PartyStatus.Active,
                    FoundedDate = input.Date,
                    PredecessorPartyId = party.Id,
                    CoreGrievance = PartyPlatform.MostBetrayedIssue(party)
                    // LastVoteShare stays 0: it records the last election, which this party did not
                    // contest. Its support is decided by the affinity model at the next one.
                };
                splinter.LastManifesto = splinter.Platform;

                parties.Add(splinter);
                onBallot++;
                touched.Add(party.Id);
                touched.Add(splinter.Id);
                changes.Add(Change(PartyChangeKind.SplitParent, party.Id, splinter.Id, input.Date,
                    PartyChangeReasons.InternalTension));
                changes.Add(Change(PartyChangeKind.SplitFounded, splinter.Id, party.Id, input.Date,
                    PartyChangeReasons.InternalTension));
            }
        }

        // --- Stage 6: new entry --------------------------------------------------------------------

        private static void ApplyNewEntry(PartyLifecycleInput input, EngineTuning tuning,
                                          List<Party> parties, List<PartyChange> changes, List<string> touched)
        {
            PartiesTuning t = tuning.Parties;
            if (PartyRegistry.OnBallotCount(parties) >= MaximumOnBallot(input.Theme, t)) return;
            if (parties.Count >= t.MaxPartiesTotal) return;

            IReadOnlyList<PartyArchetype> catalog = input.Archetypes ?? PartyArchetypes.For(input.Theme);
            PartyArchetype? archetype = FirstUnusedArchetype(catalog, parties);
            if (archetype == null) return;

            var rng = SeedStreams.RngFor(input.SaveGuid, input.Date, StreamNames.PartyLifecycle, "entry");
            if (!rng.NextBool(t.NewPartyEntryProbability)) return;

            string id = PartyRegistry.NextPartyId(parties);
            var platformRng = SeedStreams.RngFor(input.SaveGuid, input.Date, StreamNames.PartyGeneration,
                                                 "entry:" + id);

            IssuePosition platform = PartyPlatform.Instantiate(archetype, platformRng, t.ArchetypeSpreadSigma);
            for (int pass = 0; pass < 8; pass++)
            {
                bool settled = true;
                for (int i = 0; i < parties.Count; i++)
                {
                    if (!PartyRegistry.IsOnBallot(parties[i])) continue;
                    if (platform.Distance(parties[i].Platform) >= t.MinPlatformDistance) continue;

                    platform = PartyPlatform.SeparateFrom(platform, parties[i].Platform,
                                                          t.MinPlatformDistance, archetype.CoreGrievance, platformRng);
                    settled = false;
                }
                if (settled) break;
            }

            var party = new Party
            {
                Id = id,
                ColorHex = PartyRegistry.AllocateColor(parties, parties.Count, tuning),
                ArchetypeId = archetype.Id,
                Platform = platform,
                LastManifesto = platform,
                Status = PartyStatus.Active,
                FoundedDate = input.Date,
                CoreGrievance = archetype.CoreGrievance
            };

            parties.Add(party);
            touched.Add(party.Id);
            changes.Add(Change(PartyChangeKind.Founded, party.Id, null, input.Date, PartyChangeReasons.NewEntry));
        }

        private static PartyArchetype? FirstUnusedArchetype(IReadOnlyList<PartyArchetype> catalog,
                                                            List<Party> parties)
        {
            for (int i = 0; i < catalog.Count; i++)
            {
                bool used = false;
                for (int j = 0; j < parties.Count && !used; j++)
                {
                    // Dissolved brands count as used: the archetype is reserved until it revives or
                    // the save ends, so a revival never collides with a fresh party of the same kind.
                    if (string.CompareOrdinal(parties[j].ArchetypeId, catalog[i].Id) == 0) used = true;
                }
                if (!used) return catalog[i];
            }
            return null;
        }

        // --- Shared helpers ------------------------------------------------------------------------

        /// <summary>Floor on the number of parties contesting an election.</summary>
        private static int MinimumOnBallot(RegionTheme theme, PartiesTuning t) =>
            theme == RegionTheme.Na ? t.TargetCountNa : t.MinCountEu;

        /// <summary>Ceiling on the number of parties contesting an election.</summary>
        private static int MaximumOnBallot(RegionTheme theme, PartiesTuning t) =>
            theme == RegionTheme.Na ? t.TargetCountNa + t.MinorPartyCountNa : t.MaxCountEu;

        private static double Grievance(IssueWeights grievance, Issue issue) =>
            PartyPlatform.Clamp(grievance[issue], 0.0, 1.0);

        private static PartyChange Change(PartyChangeKind kind, string partyId, string? counterpart,
                                          SimDate date, string reason) =>
            new PartyChange
            {
                Kind = kind,
                PartyId = partyId,
                CounterpartPartyId = counterpart,
                Date = date,
                ReasonCode = reason
            };

        private static bool Contains(List<string> ids, string id)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.CompareOrdinal(ids[i], id) == 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether the party contested this election. <see cref="ElectionResult.PartyIdsOnBallot"/> is
        /// authoritative; an empty ballot list falls back to "appears in the city shares", so a caller
        /// that only filled the shares still gets its results applied rather than silently ignored.
        /// </summary>
        private static bool WasOnBallot(ElectionResult election, string partyId)
        {
            if (election.PartyIdsOnBallot.Count > 0)
            {
                for (int i = 0; i < election.PartyIdsOnBallot.Count; i++)
                {
                    if (string.CompareOrdinal(election.PartyIdsOnBallot[i], partyId) == 0) return true;
                }
                return false;
            }

            for (int i = 0; i < election.CityVoteShares.Count; i++)
            {
                if (string.CompareOrdinal(election.CityVoteShares[i].PartyId, partyId) == 0) return true;
            }
            return false;
        }

        private static double ShareOf(List<PartyVoteShare> shares, string partyId)
        {
            for (int i = 0; i < shares.Count; i++)
            {
                if (string.CompareOrdinal(shares[i].PartyId, partyId) == 0) return shares[i].Share;
            }
            return 0.0;
        }

        private static int SeatsOf(List<SeatAllocation> seats, string partyId)
        {
            for (int i = 0; i < seats.Count; i++)
            {
                if (string.CompareOrdinal(seats[i].PartyId, partyId) == 0) return seats[i].Seats;
            }
            return 0;
        }
    }
}
