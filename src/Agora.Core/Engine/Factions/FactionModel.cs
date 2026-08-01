using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Factions
{
    /// <summary>
    /// Everything one faction cycle produced. The caller decides what to persist; nothing here writes
    /// to <c>PoliticalState</c>.
    /// </summary>
    public sealed class FactionCycleResult
    {
        /// <summary>Every faction after the cycle, including dissolved and merged brands, sorted by id.</summary>
        public List<Faction> Factions { get; set; } = new List<Faction>();

        /// <summary>One entry per party that has factions, in party-id order.</summary>
        public List<DominanceOutcome> Dominance { get; set; } = new List<DominanceOutcome>();

        /// <summary>The platform each party's factions wrote, in party-id order.</summary>
        public List<PlatformAuthorship> Platforms { get; set; } = new List<PlatformAuthorship>();

        /// <summary>Lifecycle outcomes, in the order they occurred (party-id, then phase, then faction-id).</summary>
        public List<FactionLifecycleEvent> Events { get; set; } = new List<FactionLifecycleEvent>();

        public PlatformAuthorship? PlatformFor(string? partyId)
        {
            for (int i = 0; i < Platforms.Count; i++)
                if (string.CompareOrdinal(Platforms[i].PartyId, partyId ?? "") == 0) return Platforms[i];
            return null;
        }
    }

    /// <summary>
    /// The faction packet's front door (§3, NA theme).
    ///
    /// <para>
    /// Two entry points: <see cref="Generate"/> at save creation, and <see cref="Advance"/> once per
    /// lifecycle interval. Both are pure functions of (parties, blocs, saveGuid, date, tuning) —
    /// <see cref="Advance"/> clones its inputs, so calling it twice with the same arguments returns
    /// two byte-identical results and leaves the caller's objects untouched.
    /// </para>
    /// </summary>
    public static class FactionModel
    {
        /// <summary>
        /// Factions are the NA theme's answer to party churn (§3). EU saves run their politics through
        /// party split/merge/die instead and normally carry no factions at all.
        /// </summary>
        public static bool AppliesTo(AgoraSettings? settings) =>
            settings != null &&
            (settings.Theme == RegionTheme.Na || settings.System == ElectoralSystem.FirstPastThePost);

        /// <summary>Generates the initial faction set. See <see cref="FactionGenerator.GenerateAll"/>.</summary>
        /// <remarks>
        /// Every faction-bearing party in <paramref name="parties"/> gets factions. Which parties those
        /// are is the party packet's call, not this one's: <c>Party</c> carries no major/minor flag, and
        /// inventing one here would duplicate <c>parties.targetCountNa</c> / <c>parties.minorPartyCountNa</c>
        /// in a second place. A caller that wants factions only inside the dominant parties passes only
        /// those parties.
        /// </remarks>
        public static List<Faction> Generate(
            IReadOnlyList<Party>? parties,
            IReadOnlyList<Bloc>? blocs,
            Guid saveGuid,
            SimDate date,
            EngineTuning tuning) =>
            FactionGenerator.GenerateAll(parties, blocs, saveGuid, date, tuning);

        /// <summary>
        /// Runs one faction lifecycle cycle: support drift, death, merge, split, revival, dominance,
        /// platform authorship, leader change.
        /// </summary>
        /// <remarks>
        /// Phase order is fixed and load-bearing. Support moves first so every later decision reads the
        /// same numbers; death runs before merge so a dying faction is not merged into instead of
        /// buried; split runs after merge so a faction cannot be merged and split in one cycle;
        /// revival runs last among the structural phases so it can take a seat a death just freed.
        /// Dominance and authorship then run on the settled set.
        /// </remarks>
        public static FactionCycleResult Advance(
            IReadOnlyList<Party>? parties,
            IReadOnlyList<Faction>? factions,
            IReadOnlyList<Bloc>? blocs,
            Guid saveGuid,
            SimDate date,
            EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            var result = new FactionCycleResult();

            var all = new List<Faction>();
            if (factions != null)
            {
                for (int i = 0; i < factions.Count; i++)
                    if (factions[i] != null) all.Add(Clone(factions[i]));
            }
            all.Sort(FactionIds.ByIdComparison);

            if (parties == null || parties.Count == 0)
            {
                result.Factions = all;
                return result;
            }

            IssueClimate climate = IssueClimate.FromBlocs(blocs);
            FactionsTuning t = tuning.Factions;

            var ordered = new List<Party>();
            for (int i = 0; i < parties.Count; i++)
                if (parties[i] != null) ordered.Add(parties[i]);
            ordered.Sort((a, b) => string.CompareOrdinal(a.Id ?? "", b.Id ?? ""));

            int nextOrdinal = FactionIds.NextOrdinal(all);

            for (int p = 0; p < ordered.Count; p++)
            {
                Party party = ordered[p];
                var mine = new List<Faction>();
                for (int i = 0; i < all.Count; i++)
                    if (string.CompareOrdinal(all[i].PartyId ?? "", party.Id ?? "") == 0) mine.Add(all[i]);
                if (mine.Count == 0) continue;
                mine.Sort(FactionIds.ByIdComparison);

                FactionSupport.ApplyDrift(mine, blocs, tuning);

                RunDeaths(party, mine, t, date, result.Events);
                RunMerge(party, mine, t, saveGuid, date, result.Events);
                RunSplits(party, mine, blocs, t, tuning, saveGuid, date, ref nextOrdinal, result.Events, all);
                RunRevivals(party, mine, climate, t, date, result.Events);

                List<Faction> eligible = FactionSupport.EligibleSortedById(mine);
                FactionSupport.Normalize(eligible);
                RefreshCoreBlocs(eligible, blocs);

                DominanceOutcome outcome = FactionDominance.Apply(party.Id ?? "", mine, tuning);
                result.Dominance.Add(outcome);
                if (outcome.IsTakeover)
                {
                    result.Events.Add(new FactionLifecycleEvent(
                        party.Id ?? "", outcome.DominantFactionId ?? "",
                        FactionLifecycleKind.Takeover, outcome.PreviousDominantFactionId));
                }

                PlatformAuthorship authorship = FactionPlatform.Author(party, mine, tuning);
                result.Platforms.Add(authorship);

                // Tension is measured against the platform the party has just adopted: a faction that
                // lost the argument this cycle is the one that feels the strain next cycle.
                FactionGenerator.RefreshDemandsAndTension(eligible, authorship.Platform, tuning);

                RunLeaderChanges(party, eligible, t, saveGuid, date, result.Events);
            }

            all.Sort(FactionIds.ByIdComparison);
            result.Factions = all;
            return result;
        }

        /// <summary>
        /// Writes <c>Party.FactionIds</c> from a faction set. Includes dissolved brands: they still
        /// belong to the party and are the pool revival draws from.
        /// </summary>
        public static void ApplyFactionIds(IReadOnlyList<Party>? parties, IReadOnlyList<Faction>? factions)
        {
            if (parties == null) return;

            for (int p = 0; p < parties.Count; p++)
            {
                Party party = parties[p];
                if (party == null) continue;

                var ids = new List<string>();
                if (factions != null)
                {
                    for (int i = 0; i < factions.Count; i++)
                    {
                        Faction f = factions[i];
                        if (f == null) continue;
                        if (string.CompareOrdinal(f.PartyId ?? "", party.Id ?? "") == 0) ids.Add(f.Id);
                    }
                }
                ids.Sort(FactionIds.Compare);
                party.FactionIds = ids;
            }
        }

        /// <summary>
        /// Copies each authored platform onto its party. Separate from <see cref="Advance"/> on purpose
        /// — <c>Party</c> belongs to the party packet, so mutating it stays an explicit, opt-in step.
        /// </summary>
        public static void ApplyPlatforms(IReadOnlyList<Party>? parties, FactionCycleResult result)
        {
            if (parties == null || result == null) return;

            for (int p = 0; p < parties.Count; p++)
            {
                Party party = parties[p];
                if (party == null) continue;
                PlatformAuthorship? authored = result.PlatformFor(party.Id);
                if (authored != null) party.Platform = authored.Platform;
            }
        }

        // ---------------------------------------------------------------- lifecycle phases

        private static void RunDeaths(Party party, List<Faction> mine, FactionsTuning t,
                                      SimDate date, List<FactionLifecycleEvent> events)
        {
            int minPerParty = t.MinPerParty < 1 ? 1 : t.MinPerParty;
            int deathCycles = t.DeathConsecutiveCycles < 1 ? 1 : t.DeathConsecutiveCycles;

            List<Faction> eligible = FactionSupport.EligibleSortedById(mine);
            int alive = eligible.Count;

            for (int i = 0; i < eligible.Count; i++)
            {
                Faction f = eligible[i];

                if (f.InternalSupport < t.DeathSupportThreshold)
                {
                    f.ConsecutiveCyclesBelowThreshold++;

                    if (f.ConsecutiveCyclesBelowThreshold >= deathCycles && alive - 1 >= minPerParty)
                    {
                        f.Status = FactionStatus.Dissolved;
                        f.DissolvedDate = date;
                        f.InternalSupport = 0.0;
                        f.IsDominant = false;
                        alive--;
                        events.Add(new FactionLifecycleEvent(party.Id ?? "", f.Id, FactionLifecycleKind.Dissolved));
                    }
                    else if (f.Status != FactionStatus.Endangered)
                    {
                        // Held rather than killed: a party may not fall below factions.minPerParty.
                        f.Status = FactionStatus.Endangered;
                        events.Add(new FactionLifecycleEvent(party.Id ?? "", f.Id, FactionLifecycleKind.Endangered));
                    }
                }
                else if (f.ConsecutiveCyclesBelowThreshold > 0 || f.Status == FactionStatus.Endangered)
                {
                    f.ConsecutiveCyclesBelowThreshold = 0;
                    if (f.Status == FactionStatus.Endangered) f.Status = FactionStatus.Active;
                    events.Add(new FactionLifecycleEvent(party.Id ?? "", f.Id, FactionLifecycleKind.Recovered));
                }
            }
        }

        private static void RunMerge(Party party, List<Faction> mine, FactionsTuning t,
                                     Guid saveGuid, SimDate date, List<FactionLifecycleEvent> events)
        {
            int minPerParty = t.MinPerParty < 1 ? 1 : t.MinPerParty;

            List<Faction> eligible = FactionSupport.EligibleSortedById(mine);
            if (eligible.Count - 1 < minPerParty) return;

            int bestA = -1, bestB = -1;
            double bestAffinity = double.NegativeInfinity;
            for (int i = 0; i < eligible.Count; i++)
            {
                for (int j = i + 1; j < eligible.Count; j++)
                {
                    double affinity = 1.0 - eligible[i].Platform.Distance(eligible[j].Platform);
                    if (affinity > bestAffinity)
                    {
                        bestAffinity = affinity;
                        bestA = i;
                        bestB = j;
                    }
                }
            }

            if (bestA < 0 || bestAffinity < t.MergeAffinityThreshold) return;

            DeterministicRng rng = SeedStreams.RngFor(
                saveGuid, date, StreamNames.FactionLifecycle, (party.Id ?? "") + ":merge");
            if (!rng.NextBool(t.MergeProbabilityPerCycle)) return;

            Faction a = eligible[bestA];
            Faction b = eligible[bestB];
            // Higher support survives; an exact tie keeps the lower id, which is `a` because the list
            // is id-sorted. No coin flip — a re-rolled tie-break would rewrite history on reload.
            Faction survivor = b.InternalSupport > a.InternalSupport ? b : a;
            Faction absorbed = ReferenceEquals(survivor, a) ? b : a;

            survivor.InternalSupport += absorbed.InternalSupport;
            absorbed.Status = FactionStatus.Merged;
            absorbed.SuccessorFactionId = survivor.Id;
            absorbed.DissolvedDate = date;
            absorbed.InternalSupport = 0.0;
            absorbed.IsDominant = false;

            events.Add(new FactionLifecycleEvent(
                party.Id ?? "", absorbed.Id, FactionLifecycleKind.Merged, survivor.Id));
        }

        private static void RunSplits(Party party, List<Faction> mine, IReadOnlyList<Bloc>? blocs,
                                      FactionsTuning t, EngineTuning tuning, Guid saveGuid, SimDate date,
                                      ref int nextOrdinal, List<FactionLifecycleEvent> events, List<Faction> all)
        {
            int maxPerParty = t.MaxPerParty < 1 ? 1 : t.MaxPerParty;

            List<Faction> candidates = FactionSupport.EligibleSortedById(mine);
            int alive = candidates.Count;

            for (int i = 0; i < candidates.Count; i++)
            {
                if (alive >= maxPerParty) return;

                Faction parent = candidates[i];
                if (parent.TensionWithParty < t.InternalTensionThreshold) continue;

                DeterministicRng rng = SeedStreams.RngFor(
                    saveGuid, date, StreamNames.FactionLifecycle, parent.Id + ":split");
                if (!rng.NextBool(t.SplitProbabilityPerCycle)) continue;

                Faction splinter = BuildSplinter(party, parent, t, date, FactionIds.Format(nextOrdinal++));

                // Support divides by demography, not by a magic fraction: repartition the parent's
                // support between the two platforms and give each what its base is worth.
                var pair = new List<Faction> { parent, splinter };
                pair.Sort(FactionIds.ByIdComparison);
                List<FactionConstituency> shares = FactionSupport.Constituencies(pair, blocs);
                FactionConstituency? parentShare = FactionSupport.FindConstituency(shares, parent.Id);
                FactionConstituency? splinterShare = FactionSupport.FindConstituency(shares, splinter.Id);

                double pool = parent.InternalSupport;
                double parentFraction = parentShare == null ? 0.5 : parentShare.TargetShare;
                double splinterFraction = splinterShare == null ? 0.5 : splinterShare.TargetShare;
                double fractionTotal = parentFraction + splinterFraction;
                if (fractionTotal <= 0.0) { parentFraction = 0.5; splinterFraction = 0.5; fractionTotal = 1.0; }

                parent.InternalSupport = pool * (parentFraction / fractionTotal);
                splinter.InternalSupport = pool * (splinterFraction / fractionTotal);
                splinter.Demands = FactionPlatform.Demands(splinter, party.Platform, tuning);
                splinter.TensionWithParty = FactionPlatform.Tension(splinter, party.Platform);

                mine.Add(splinter);
                all.Add(splinter);
                alive++;

                events.Add(new FactionLifecycleEvent(
                    party.Id ?? "", parent.Id, FactionLifecycleKind.Split, splinter.Id));
            }
        }

        private static Faction BuildSplinter(Party party, Faction parent, FactionsTuning t, SimDate date, string id)
        {
            IssuePosition partyPlatform = party.Platform;
            IssuePosition parentPlatform = parent.Platform;

            var components = new double[Contracts.Issues.Count];
            for (int n = 0; n < Contracts.Issues.All.Count; n++)
                components[n] = parentPlatform[Contracts.Issues.All[n]];

            IReadOnlyList<Issue> demands = parent.Demands != null && parent.Demands.Count > 0
                ? (IReadOnlyList<Issue>)parent.Demands
                : new List<Issue> { parent.CoreGrievance };

            // The splinter is the parent's argument taken further: it pushes past the parent on every
            // issue the parent merely demanded. No new draw — the split roll already happened, and a
            // second stream here would let a rejected split still move the platform.
            for (int d = 0; d < demands.Count; d++)
            {
                Issue issue = demands[d];
                int index = IssueVectors.IndexOf(issue);
                double gap = parentPlatform[issue] - partyPlatform[issue];
                int sign = gap >= 0.0 ? 1 : -1;
                components[index] += sign * t.ArchetypeSpreadSigma;
            }

            Issue grievance = parent.CoreGrievance;
            double widest = -1.0;
            for (int d = 0; d < demands.Count; d++)
            {
                if (demands[d] == parent.CoreGrievance) continue;
                double gap = Math.Abs(parentPlatform[demands[d]] - partyPlatform[demands[d]]);
                if (gap > widest) { widest = gap; grievance = demands[d]; }
            }

            IssuePosition platform = IssueVectors.Position(components).Clamped();
            int grievanceSign = (platform[grievance] - partyPlatform[grievance]) >= 0.0 ? 1 : -1;

            return new Faction
            {
                Id = id,
                PartyId = parent.PartyId,
                ArchetypeId = FactionArchetypes.For(grievance, grievanceSign).Id,
                Platform = platform,
                CoreGrievance = grievance,
                Status = FactionStatus.Active,
                FoundedDate = date,
                PredecessorFactionId = parent.Id,
                IsDominant = false,
                ConsecutiveCyclesBelowThreshold = 0
                // Name / ShortName / Description / LeaderName are flavor-owned and stay empty.
            };
        }

        private static void RunRevivals(Party party, List<Faction> mine, IssueClimate climate,
                                        FactionsTuning t, SimDate date, List<FactionLifecycleEvent> events)
        {
            int maxPerParty = t.MaxPerParty < 1 ? 1 : t.MaxPerParty;
            if (FactionSupport.EligibleSortedById(mine).Count >= maxPerParty) return;

            var dissolved = new List<Faction>();
            for (int i = 0; i < mine.Count; i++)
            {
                Faction candidate = mine[i];
                if (candidate.Status != FactionStatus.Dissolved) continue;

                // A brand that died *this* cycle may not return in the same cycle. RunDeaths runs
                // first, so without this gate a faction whose core grievance is currently high is
                // dissolved and immediately revived by the very grievance that is draining it —
                // emitting a Dissolved/Revived pair on one date and silently resetting the death
                // counter, so the faction can never actually die. Revival is a next-cycle story beat.
                if (candidate.DissolvedDate.HasValue && candidate.DissolvedDate.Value >= date) continue;

                dissolved.Add(candidate);
            }
            if (dissolved.Count == 0) return;
            dissolved.Sort(FactionIds.ByIdComparison);

            // One revival per cycle: the lowest id whose grievance has come back. A dead brand
            // returning is a story beat, not a background process.
            for (int i = 0; i < dissolved.Count; i++)
            {
                Faction f = dissolved[i];
                if (climate.Grievance[f.CoreGrievance] < t.RevivalGrievanceThreshold) continue;

                f.Status = FactionStatus.Revived;
                f.DissolvedDate = null;
                f.ConsecutiveCyclesBelowThreshold = 0;
                f.InternalSupport = t.DeathSupportThreshold;
                events.Add(new FactionLifecycleEvent(party.Id ?? "", f.Id, FactionLifecycleKind.Revived));
                return;
            }
        }

        private static void RunLeaderChanges(Party party, List<Faction> eligible, FactionsTuning t,
                                             Guid saveGuid, SimDate date, List<FactionLifecycleEvent> events)
        {
            for (int i = 0; i < eligible.Count; i++)
            {
                DeterministicRng rng = SeedStreams.RngFor(
                    saveGuid, date, StreamNames.FactionLifecycle, eligible[i].Id + ":leader");
                if (!rng.NextBool(t.LeaderChangeProbabilityPerCycle)) continue;

                // The engine records the change and nothing else. LeaderName is flavor-owned; writing
                // a name here would be an LLM-boundary inversion (non-negotiable #1).
                events.Add(new FactionLifecycleEvent(party.Id ?? "", eligible[i].Id, FactionLifecycleKind.LeaderChange));
            }
        }

        private static void RefreshCoreBlocs(List<Faction> eligible, IReadOnlyList<Bloc>? blocs)
        {
            if (eligible.Count == 0) return;
            List<FactionConstituency> constituencies = FactionSupport.Constituencies(eligible, blocs);
            for (int i = 0; i < eligible.Count; i++)
            {
                FactionConstituency? c = FactionSupport.FindConstituency(constituencies, eligible[i].Id);
                if (c != null) eligible[i].CoreBlocs = new List<BlocKey>(c.CoreBlocs);
            }
        }

        internal static Faction Clone(Faction f) => new Faction
        {
            Id = f.Id,
            PartyId = f.PartyId,
            Name = f.Name,
            ShortName = f.ShortName,
            Description = f.Description,
            LeaderName = f.LeaderName,
            ArchetypeId = f.ArchetypeId,
            Platform = f.Platform,
            InternalSupport = f.InternalSupport,
            IsDominant = f.IsDominant,
            TensionWithParty = f.TensionWithParty,
            Status = f.Status,
            FoundedDate = f.FoundedDate,
            DissolvedDate = f.DissolvedDate,
            PredecessorFactionId = f.PredecessorFactionId,
            SuccessorFactionId = f.SuccessorFactionId,
            Demands = f.Demands == null ? new List<Issue>() : new List<Issue>(f.Demands),
            CoreBlocs = f.CoreBlocs == null ? new List<BlocKey>() : new List<BlocKey>(f.CoreBlocs),
            ConsecutiveCyclesBelowThreshold = f.ConsecutiveCyclesBelowThreshold,
            CoreGrievance = f.CoreGrievance
        };
    }
}
