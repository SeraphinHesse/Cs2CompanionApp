using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Factions
{
    /// <summary>
    /// Builds a party's factions at save creation (§3, NA theme: 2–4 factions per party with their own
    /// demographic support, demands and leader).
    ///
    /// <para>
    /// Every draw comes from <c>StreamNames.FactionGeneration</c>. The per-party stream sizes the
    /// party and ranks its issues; each faction then draws its platform from its own sub-stream keyed
    /// by <c>partyId:issueKey</c>, so adding or removing a faction cannot shift the platform of any
    /// other faction — the failure mode a single loop-order stream would produce.
    /// </para>
    /// </summary>
    public static class FactionGenerator
    {
        /// <summary>
        /// Generates factions for every eligible party, allocating ids in one ascending run across
        /// parties sorted by id.
        /// </summary>
        /// <remarks>
        /// Returned sorted by faction id, matching <c>PoliticalState.Factions</c>. Parties are not
        /// mutated; call <see cref="FactionModel.ApplyFactionIds"/> to populate <c>Party.FactionIds</c>.
        /// </remarks>
        public static List<Faction> GenerateAll(
            IReadOnlyList<Party>? parties,
            IReadOnlyList<Bloc>? blocs,
            Guid saveGuid,
            SimDate date,
            EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            var result = new List<Faction>();
            if (parties == null || parties.Count == 0) return result;

            IssueClimate climate = IssueClimate.FromBlocs(blocs);

            var ordered = new List<Party>();
            for (int i = 0; i < parties.Count; i++)
                if (parties[i] != null && IsFactionBearing(parties[i])) ordered.Add(parties[i]);
            ordered.Sort((a, b) => string.CompareOrdinal(a.Id ?? "", b.Id ?? ""));

            int nextOrdinal = 1;
            for (int i = 0; i < ordered.Count; i++)
            {
                List<Faction> made = GenerateForParty(ordered[i], climate, blocs, saveGuid, date, tuning, ref nextOrdinal);
                result.AddRange(made);
            }

            result.Sort(FactionIds.ByIdComparison);
            return result;
        }

        /// <summary>A party that can hold factions: on the field, not a dissolved or merged brand.</summary>
        public static bool IsFactionBearing(Party? p) =>
            p != null && (p.Status == PartyStatus.Active
                          || p.Status == PartyStatus.Endangered
                          || p.Status == PartyStatus.Revived);

        /// <summary>
        /// Generates one party's factions. <paramref name="nextOrdinal"/> is advanced past the ids used.
        /// </summary>
        public static List<Faction> GenerateForParty(
            Party party,
            IssueClimate climate,
            IReadOnlyList<Bloc>? blocs,
            Guid saveGuid,
            SimDate date,
            EngineTuning tuning,
            ref int nextOrdinal)
        {
            if (party == null) throw new ArgumentNullException(nameof(party));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            FactionsTuning t = tuning.Factions;
            var factions = new List<Faction>();

            DeterministicRng partyRng =
                SeedStreams.RngFor(saveGuid, date, StreamNames.FactionGeneration, party.Id ?? "");

            int count = DrawCount(partyRng, t);
            if (count <= 0) return factions;

            IReadOnlyList<Issue> issues = RankIssues(party, climate, partyRng, t, count);

            for (int rank = 0; rank < issues.Count; rank++)
            {
                Issue issue = issues[rank];

                DeterministicRng rng = SeedStreams.RngFor(
                    saveGuid, date, StreamNames.FactionGeneration,
                    (party.Id ?? "") + ":" + Contracts.Issues.ToKey(issue));

                // Draw the whole platform jitter first, in Issues.All order, then the archetype boost.
                // Fixed draw order is what makes the platform a function of the seed and nothing else.
                var offsets = new double[Contracts.Issues.Count];
                for (int n = 0; n < Contracts.Issues.Count; n++)
                    offsets[n] = rng.NextGaussian() * t.ArchetypeSpreadSigma;
                double boost = Math.Abs(rng.NextGaussian());

                // Which way the faction leans: toward the electorate for the first faction, then
                // alternating, so a party is never all champions or all sceptics. No draw — the
                // alternation is what guarantees internal spread, and a coin flip would sometimes
                // produce a party whose factions all agree, which is not a faction system.
                int baseSign = (climate.MeanIdeal[issue] - party.Platform[issue]) >= 0.0 ? 1 : -1;
                int sign = (rank % 2 == 0) ? baseSign : -baseSign;

                var components = new double[Contracts.Issues.Count];
                for (int n = 0; n < Contracts.Issues.All.Count; n++)
                {
                    Issue k = Contracts.Issues.All[n];
                    components[n] = party.Platform[k] + offsets[n];
                }
                int owned = IssueVectors.IndexOf(issue);
                components[owned] += sign * t.ArchetypeSpreadSigma * (1.0 + boost);

                FactionArchetype archetype = FactionArchetypes.For(issue, sign);

                var faction = new Faction
                {
                    Id = FactionIds.Format(nextOrdinal++),
                    PartyId = party.Id ?? "",
                    ArchetypeId = archetype.Id,
                    Platform = IssueVectors.Position(components).Clamped(),
                    CoreGrievance = issue,
                    Status = FactionStatus.Active,
                    FoundedDate = date,
                    IsDominant = false,
                    ConsecutiveCyclesBelowThreshold = 0
                    // Name, ShortName, Description and LeaderName stay empty: they are flavor-owned
                    // (non-negotiable #1) and are filled by IFlavorProvider at the next wake.
                };

                factions.Add(faction);
            }

            factions.Sort(FactionIds.ByIdComparison);

            FactionSupport.ApplyTargets(factions, blocs);
            RefreshDemandsAndTension(factions, party.Platform, tuning);
            FactionDominance.Apply(party.Id ?? "", factions, tuning);

            return factions;
        }

        /// <summary>
        /// Faction count for one party: <c>factions.targetPerParty</c> ±1, clamped into
        /// <c>[minPerParty, maxPerParty]</c> and to the six available issues.
        /// </summary>
        internal static int DrawCount(DeterministicRng rng, FactionsTuning t)
        {
            int min = t.MinPerParty < 1 ? 1 : t.MinPerParty;
            int max = t.MaxPerParty < min ? min : t.MaxPerParty;
            if (max > Contracts.Issues.Count) max = Contracts.Issues.Count;
            if (min > max) min = max;

            int count = t.TargetPerParty + rng.NextInt(-1, 2);
            if (count < min) count = min;
            if (count > max) count = max;
            return count;
        }

        /// <summary>
        /// Picks the issues a party's factions form around: its own core grievance always, then the
        /// most salient city grievances with a per-party seeded nudge so two parties do not end up
        /// with the same internal argument.
        /// </summary>
        internal static IReadOnlyList<Issue> RankIssues(
            Party party, IssueClimate climate, DeterministicRng rng, FactionsTuning t, int count)
        {
            var score = new double[Contracts.Issues.Count];
            for (int n = 0; n < Contracts.Issues.All.Count; n++)
            {
                Issue issue = Contracts.Issues.All[n];
                double jitter = rng.NextGaussian() * t.ArchetypeSpreadSigma;
                // Salience is non-negative; the nudge is multiplicative and floored so a jitter draw
                // can reorder issues but can never make a score negative and flip the sort meaning.
                double factor = 1.0 + jitter;
                if (factor < 0.0) factor = 0.0;
                score[n] = climate.Salience[issue] * factor;
            }

            var ordered = new Issue[Contracts.Issues.Count];
            for (int i = 0; i < Contracts.Issues.Count; i++) ordered[i] = Contracts.Issues.All[i];
            Array.Sort(ordered, (a, b) =>
            {
                int c = score[IssueVectors.IndexOf(b)].CompareTo(score[IssueVectors.IndexOf(a)]);
                return c != 0 ? c : ((int)a).CompareTo((int)b);
            });

            var picked = new List<Issue>(count) { party.CoreGrievance };
            for (int i = 0; i < ordered.Length && picked.Count < count; i++)
            {
                if (ordered[i] == party.CoreGrievance) continue;
                picked.Add(ordered[i]);
            }
            return picked;
        }

        /// <summary>Recomputes <c>Demands</c> then <c>TensionWithParty</c> — in that order, because
        /// tension is measured over the demands.</summary>
        internal static void RefreshDemandsAndTension(
            IReadOnlyList<Faction> factions, IssuePosition partyPlatform, EngineTuning tuning)
        {
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f == null) continue;
                f.Demands = FactionPlatform.Demands(f, partyPlatform, tuning);
                f.TensionWithParty = FactionPlatform.Tension(f, partyPlatform);
            }
        }
    }
}
