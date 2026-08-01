using System;
using System.Collections.Generic;
using System.Globalization;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Agora.Core.Tuning;
using Bloc = Agora.Core.Contracts.Bloc;
using Coalition = Agora.Core.Contracts.Coalition;
using Mandate = Agora.Core.Contracts.Mandate;

namespace Agora.Core.Engine.Government.Mandates
{
    /// <summary>
    /// One measured deficit that could become a promise. Built from the snapshot alone — no
    /// randomness, no state — so the candidate list is inspectable and testable on its own.
    /// </summary>
    public sealed class MandateCandidate
    {
        /// <summary>Target district, or null for a city-wide promise.</summary>
        public string? DistrictId { get; }

        public MandateMetric Metric { get; }

        public Issue Issue { get; }

        public MandateDirection Direction { get; }

        /// <summary>Measured value now, in the metric's own units.</summary>
        public double BaselineValue { get; }

        /// <summary>What fulfilment would mean, in the metric's own units.</summary>
        public double TargetValue { get; }

        /// <summary>
        /// The value the deficit is measured against: the best district for a district promise, the
        /// metric's ideal for a city-wide one.
        /// </summary>
        public double ReferenceValue { get; }

        /// <summary>Normalised gap to the reference, <c>[0, 1]</c>. Compared to <c>mandates.minDeficitToGenerate</c>.</summary>
        public double Deficit { get; }

        /// <summary>How much the affected blocs care, <c>[mandates.salienceFloor, 1]</c>.</summary>
        public double Salience { get; }

        /// <summary>Deficit weighted by salience. Selection samples proportionally to this.</summary>
        public double Score { get; }

        /// <summary>Stable identity of the deficit — the sort tie-break and the seed entity key.</summary>
        public string Key { get; }

        internal MandateCandidate(string? districtId, MandateMetric metric, Issue issue,
                                  MandateDirection direction, double baselineValue, double targetValue,
                                  double referenceValue, double deficit, double salience)
        {
            DistrictId = districtId;
            Metric = metric;
            Issue = issue;
            Direction = direction;
            BaselineValue = baselineValue;
            TargetValue = targetValue;
            ReferenceValue = referenceValue;
            Deficit = deficit;
            Salience = salience;
            Score = deficit * salience;
            Key = (districtId ?? MandateGenerator.CityScopeKey) + ":" + MandateMetrics.ToKey(metric);
        }
    }

    /// <summary>
    /// Turns real measured deficits into the governing party's promises (§3 Mandates).
    ///
    /// <para>
    /// Nothing here reads the LLM, and nothing here writes to the city. The output is a list of
    /// <see cref="Mandate"/> objects the caller appends to state and to
    /// <see cref="Coalition.MandateIds"/>; the packet keeps no state of its own.
    /// </para>
    /// </summary>
    public static class MandateGenerator
    {
        /// <summary>Scope key used for city-wide candidates in ids and seed entity keys.</summary>
        public const string CityScopeKey = "city";

        private const string IdPrefix = "mandate-";

        /// <summary>
        /// Every deficit worth promising against, sorted by score descending then by
        /// <see cref="MandateCandidate.Key"/> ascending. Deterministic and side-effect free.
        /// </summary>
        /// <param name="snapshot">The measured city. District order is taken from the contract's sort.</param>
        /// <param name="blocs">Blocs, used only for salience. Null or empty gives every candidate the floor.</param>
        public static IReadOnlyList<MandateCandidate> BuildCandidates(
            CitySnapshot? snapshot, IReadOnlyList<Bloc>? blocs, EngineTuning tuning)
        {
            var result = new List<MandateCandidate>();
            if (snapshot == null || tuning == null) return result;

            MandatesTuning m = tuning.Mandates;
            double floor = MandateMath.Clamp01(m.SalienceFloor);
            double fraction = MandateMath.Clamp01(m.TargetImprovementFraction);

            List<DistrictSnapshot> districts = OrderedDistricts(snapshot);

            IReadOnlyList<MandateMetric> metrics = MandateMetrics.Generatable;
            for (int mi = 0; mi < metrics.Count; mi++)
            {
                MandateMetric metric = metrics[mi];
                Issue issue = MandateMetrics.IssueFor(metric);

                // ---- city-wide candidate: the deficit is the distance to the metric's ideal --------
                if (MandateMetrics.TryReadCity(snapshot, metric, out double cityValue) &&
                    MandateMetrics.TryBadness(metric, cityValue, out double cityBadness) &&
                    MandateMetrics.TryIdealValue(metric, out double ideal))
                {
                    double salience = Salience(blocs, null, issue, floor);
                    MandateCandidate? candidate =
                        MakeCandidate(null, metric, issue, cityValue, ideal, cityBadness, salience, fraction);
                    if (candidate != null) result.Add(candidate);
                }

                // ---- district candidates: the deficit is the distance to the best district ---------
                if (districts.Count > 1 &&
                    TryBestDistrictValue(districts, metric, out double bestValue, out double bestBadness))
                {
                    for (int di = 0; di < districts.Count; di++)
                    {
                        DistrictSnapshot d = districts[di];
                        if (d.Population <= 0) continue;
                        if (!MandateMetrics.TryReadDistrict(d, metric, out double value)) continue;
                        if (!MandateMetrics.TryBadness(metric, value, out double badness)) continue;

                        double deficit = badness - bestBadness;
                        if (deficit <= 0.0) continue;

                        double salience = Salience(blocs, d.Id, issue, floor);
                        MandateCandidate? candidate =
                            MakeCandidate(d.Id, metric, issue, value, bestValue, deficit, salience, fraction);
                        if (candidate != null) result.Add(candidate);
                    }
                }
            }

            result.Sort(CompareCandidates);
            return result;
        }

        /// <summary>
        /// Writes a government's promises. Called once when a coalition (or, under FPTP, the winning
        /// party plus mayor) takes office.
        /// </summary>
        /// <remarks>
        /// Two named streams do the deciding, and both draw from per-slot sub-streams so that adding a
        /// district cannot silently reshuffle every later slot:
        /// <c>mandate.generation</c> picks the scope (district vs city, at
        /// <c>mandates.districtMandateShare</c>) and <c>mandate.selection</c> samples one candidate
        /// from that pool with probability proportional to <see cref="MandateCandidate.Score"/>.
        /// </remarks>
        /// <param name="existingMandates">
        /// Everything already on the books. Live promises reserve their (district, metric) pair and
        /// count against <c>mandates.maxActive</c>; resolved ones only feed id allocation.
        /// </param>
        /// <returns>New mandates, sorted by id. The input lists are never mutated.</returns>
        public static IReadOnlyList<Mandate> Generate(
            Guid saveGuid,
            SimDate date,
            CitySnapshot? snapshot,
            Coalition? government,
            IReadOnlyList<Bloc>? blocs,
            IReadOnlyList<Mandate>? existingMandates,
            EngineTuning tuning)
        {
            var issued = new List<Mandate>();
            if (snapshot == null || government == null || tuning == null) return issued;

            string partyId = LeadPartyOf(government);
            if (string.IsNullOrEmpty(partyId)) return issued;

            MandatesTuning m = tuning.Mandates;

            int live = CountLive(existingMandates);
            int slots = m.CountPerTerm;
            int room = m.MaxActive - live;
            if (room < slots) slots = room;
            if (slots <= 0) return issued;

            IReadOnlyList<MandateCandidate> candidates = BuildCandidates(snapshot, blocs, tuning);

            var districtPool = new List<MandateCandidate>();
            var cityPool = new List<MandateCandidate>();
            for (int i = 0; i < candidates.Count; i++)
            {
                MandateCandidate c = candidates[i];
                if (c.Deficit < m.MinDeficitToGenerate) continue;
                if (IsPairTaken(existingMandates, c.DistrictId, c.Metric)) continue;

                if (c.DistrictId == null) cityPool.Add(c);
                else districtPool.Add(c);
            }

            int sequence = NextSequence(existingMandates, date);
            double districtShare = MandateMath.Clamp01(m.DistrictMandateShare);

            for (int slot = 0; slot < slots; slot++)
            {
                if (districtPool.Count == 0 && cityPool.Count == 0) break;

                string slotKey = government.Id + "#" + slot.ToString(CultureInfo.InvariantCulture);

                DeterministicRng scopeRng =
                    SeedStreams.RngFor(saveGuid, date, StreamNames.MandateGeneration, slotKey);
                bool wantDistrict = scopeRng.NextDouble() < districtShare;

                List<MandateCandidate> pool = wantDistrict ? districtPool : cityPool;
                if (pool.Count == 0) pool = wantDistrict ? cityPool : districtPool;
                if (pool.Count == 0) break;

                DeterministicRng pickRng =
                    SeedStreams.RngFor(saveGuid, date, StreamNames.MandateSelection, slotKey);
                int index = WeightedPick(pool, pickRng);

                MandateCandidate chosen = pool[index];
                pool.RemoveAt(index);

                // One promise per (scope, metric) per government: two mandates on the same number would
                // resolve identically and double the stake.
                RemoveKey(districtPool, chosen.Key);
                RemoveKey(cityPool, chosen.Key);

                issued.Add(BuildMandate(chosen, date, partyId, government.Id, sequence, m));
                sequence++;
            }

            issued.Sort(CompareById);
            return issued;
        }

        /// <summary>
        /// How much the affected blocs care about an issue, <c>[floor, 1]</c>. It is the share of the
        /// blocs' total issue-weight mass this one issue commands, head-count weighted: 1.0 would mean
        /// they care about nothing else, and uniform weights give <c>1/6</c>.
        /// </summary>
        /// <remarks>
        /// Summed in list order, which the contract fixes (district id, then bloc ordinal), so the
        /// floating-point result is stable. Population is used rather than
        /// <see cref="Bloc.PopulationShare"/> because head counts are exact and share is per-district.
        /// </remarks>
        public static double Salience(IReadOnlyList<Bloc>? blocs, string? districtId, Issue issue, double floor)
        {
            floor = MandateMath.Clamp01(floor);
            if (blocs == null || blocs.Count == 0) return floor;

            double weighted = 0.0;
            double total = 0.0;

            for (int i = 0; i < blocs.Count; i++)
            {
                Bloc b = blocs[i];
                if (b == null) continue;
                if (districtId != null && !string.Equals(b.DistrictId, districtId, StringComparison.Ordinal)) continue;

                double population = b.Population;
                if (population <= 0.0) continue;

                IssueWeights w = b.Weights;
                double sum = w.Sum();
                if (!MandateMath.IsFinite(sum) || sum <= 0.0) continue;

                double share = w[issue];
                if (!MandateMath.IsFinite(share) || share < 0.0) continue;

                weighted += population * share;
                total += population * sum;
            }

            if (total <= MandateMath.Epsilon) return floor;

            return MandateMath.Clamp(weighted / total, floor, 1.0);
        }

        // -------------------------------------------------------------------------------------------
        // Internals
        // -------------------------------------------------------------------------------------------

        private static MandateCandidate? MakeCandidate(string? districtId, MandateMetric metric, Issue issue,
                                                       double baseline, double reference, double deficit,
                                                       double salience, double fraction)
        {
            if (!MandateMath.IsFinite(baseline) || !MandateMath.IsFinite(reference)) return null;

            deficit = MandateMath.Clamp01(deficit);
            if (deficit <= 0.0) return null;

            // Close `targetImprovementFraction` of the measured gap — not the whole gap. A promise to
            // become the best district in two years is not a promise, it is a fantasy.
            double target = baseline + fraction * (reference - baseline);
            if (Math.Abs(target - baseline) < MandateMath.Epsilon) return null;

            MandateDirection direction = target > baseline ? MandateDirection.Increase : MandateDirection.Decrease;

            return new MandateCandidate(districtId, metric, issue, direction, baseline, target,
                                        reference, deficit, salience);
        }

        private static Mandate BuildMandate(MandateCandidate c, SimDate date, string partyId,
                                            string coalitionId, int sequence, MandatesTuning m)
        {
            return new Mandate
            {
                Id = MakeId(date, sequence),
                PartyId = partyId,
                CoalitionId = coalitionId,
                DistrictId = c.DistrictId,
                Issue = c.Issue,
                Metric = c.Metric,
                Direction = c.Direction,
                BaselineValue = c.BaselineValue,
                TargetValue = c.TargetValue,
                CurrentValue = c.BaselineValue,
                Progress = 0.0,
                IssuedDate = date,
                DeadlineDate = date.AddMonths(m.HorizonMonths),
                ResolvedDate = null,
                Status = MandateStatus.Pending,
                Salience = c.Salience,
                ResolutionEffectId = null,

                // Flavor-owned. The engine never writes prose and never parses it back.
                Text = "",
                IsMeasurementStalled = false
            };
        }

        private static string MakeId(SimDate date, int sequence) =>
            IdPrefix +
            date.Year.ToString("D4", CultureInfo.InvariantCulture) + "-" +
            date.Month.ToString("D2", CultureInfo.InvariantCulture) + "-" +
            sequence.ToString("D2", CultureInfo.InvariantCulture);

        /// <summary>
        /// First unused sequence number for this month. Scans existing ids rather than counting, so a
        /// government formed twice in one month cannot mint a duplicate id.
        /// </summary>
        private static int NextSequence(IReadOnlyList<Mandate>? existing, SimDate date)
        {
            string prefix = IdPrefix +
                            date.Year.ToString("D4", CultureInfo.InvariantCulture) + "-" +
                            date.Month.ToString("D2", CultureInfo.InvariantCulture) + "-";

            int highest = 0;
            if (existing == null) return highest + 1;

            for (int i = 0; i < existing.Count; i++)
            {
                Mandate mandate = existing[i];
                if (mandate == null || mandate.Id == null) continue;
                if (!mandate.Id.StartsWith(prefix, StringComparison.Ordinal)) continue;

                string tail = mandate.Id.Substring(prefix.Length);
                if (int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) &&
                    value > highest)
                {
                    highest = value;
                }
            }

            return highest + 1;
        }

        private static string LeadPartyOf(Coalition government)
        {
            if (!string.IsNullOrEmpty(government.LeadPartyId)) return government.LeadPartyId;

            List<string> members = government.MemberPartyIds;
            if (members != null && members.Count > 0 && !string.IsNullOrEmpty(members[0])) return members[0];

            return "";
        }

        private static int CountLive(IReadOnlyList<Mandate>? mandates)
        {
            if (mandates == null) return 0;

            int count = 0;
            for (int i = 0; i < mandates.Count; i++)
            {
                Mandate m = mandates[i];
                if (m != null && MandateMonitor.IsLive(m)) count++;
            }

            return count;
        }

        private static bool IsPairTaken(IReadOnlyList<Mandate>? mandates, string? districtId, MandateMetric metric)
        {
            if (mandates == null) return false;

            for (int i = 0; i < mandates.Count; i++)
            {
                Mandate m = mandates[i];
                if (m == null || !MandateMonitor.IsLive(m)) continue;
                if (m.Metric != metric) continue;
                if (string.Equals(m.DistrictId, districtId, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static void RemoveKey(List<MandateCandidate> pool, string key)
        {
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                if (string.Equals(pool[i].Key, key, StringComparison.Ordinal)) pool.RemoveAt(i);
            }
        }

        /// <summary>
        /// Samples one candidate with probability proportional to its score. The pool is in a fixed
        /// order and the walk is cumulative in that order, so one draw fully determines the pick.
        /// </summary>
        private static int WeightedPick(List<MandateCandidate> pool, DeterministicRng rng)
        {
            double total = 0.0;
            for (int i = 0; i < pool.Count; i++) total += Weight(pool[i]);
            if (total <= 0.0) return 0;

            double roll = rng.NextDouble() * total;
            double accumulated = 0.0;

            for (int i = 0; i < pool.Count; i++)
            {
                accumulated += Weight(pool[i]);
                if (roll < accumulated) return i;
            }

            return pool.Count - 1;
        }

        private static double Weight(MandateCandidate c) =>
            c.Score > MandateMath.Epsilon ? c.Score : MandateMath.Epsilon;

        private static List<DistrictSnapshot> OrderedDistricts(CitySnapshot snapshot)
        {
            var ordered = new List<DistrictSnapshot>();
            List<DistrictSnapshot> source = snapshot.Districts;
            if (source == null) return ordered;

            for (int i = 0; i < source.Count; i++)
            {
                DistrictSnapshot d = source[i];
                if (d != null && !string.IsNullOrEmpty(d.Id)) ordered.Add(d);
            }

            // The contract says the snapshot list is already sorted by id; sorting again costs nothing
            // and means a sensor bug degrades into a stable order rather than an unstable one.
            ordered.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return ordered;
        }

        private static bool TryBestDistrictValue(List<DistrictSnapshot> districts, MandateMetric metric,
                                                 out double bestValue, out double bestBadness)
        {
            bestValue = 0.0;
            bestBadness = 0.0;
            bool found = false;

            for (int i = 0; i < districts.Count; i++)
            {
                DistrictSnapshot d = districts[i];
                if (d.Population <= 0) continue;
                if (!MandateMetrics.TryReadDistrict(d, metric, out double value)) continue;
                if (!MandateMetrics.TryBadness(metric, value, out double badness)) continue;

                // Districts are walked in id order, so an exact tie keeps the first id — no dependence
                // on which one the sensor happened to emit first.
                if (!found || badness < bestBadness)
                {
                    found = true;
                    bestBadness = badness;
                    bestValue = value;
                }
            }

            return found;
        }

        private static int CompareCandidates(MandateCandidate a, MandateCandidate b)
        {
            int byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : string.CompareOrdinal(a.Key, b.Key);
        }

        private static int CompareById(Mandate a, Mandate b) => string.CompareOrdinal(a.Id, b.Id);
    }
}
