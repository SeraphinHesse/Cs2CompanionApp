using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Blocs
{
    /// <summary>
    /// Builds the voter blocs for a city from one <see cref="CitySnapshot"/> — packet 1 of the engine
    /// (<c>politicsmodplan.md</c> §4.3). Blocs are wealth × education × age cells, one set per
    /// district, and everything about them is derived from measurements: composition from the
    /// district's demographic marginals, issue weights from what the city is doing to the people who
    /// live there, ideal points from who those people are.
    ///
    /// <para>
    /// Pure and stateless. Contract types in, contract types out, plus the tuning accessor. No
    /// randomness is drawn at all — <c>blocs</c> declares no noise sigma, and a bloc that shifted for
    /// unmeasured reasons would be exactly the kind of unexplainable politics the plan rules out.
    /// Noise belongs downstream, in <c>voter.affinity.noise</c> and <c>voter.turnout.noise</c>.
    /// </para>
    /// </summary>
    public static class BlocBuilder
    {
        /// <summary>
        /// Builds every district's blocs. The result is ordered by <c>DistrictId</c> (ordinal) then by
        /// <see cref="BlocKey.Ordinal"/> — the order <see cref="PoliticalState.Blocs"/> is contractually
        /// stored in, and the order every downstream aggregate sums in.
        /// </summary>
        public static List<Bloc> Build(CitySnapshot city, EngineTuning tuning)
        {
            return Build(city, tuning, null);
        }

        /// <summary>
        /// Builds every district's blocs, carrying last tick's blocs forward for composition and
        /// weight smoothing.
        /// </summary>
        /// <param name="previous">
        /// The blocs persisted in <see cref="PoliticalState"/> at the previous tick, or null on the
        /// first one. Smoothing state must arrive this way rather than being held in a field: state
        /// that does not survive save/load produces different politics after a reload, which is the
        /// desync non-negotiable #3 defines.
        /// </param>
        public static List<Bloc> Build(CitySnapshot city, EngineTuning tuning, IReadOnlyList<Bloc>? previous)
        {
            if (city == null) throw new ArgumentNullException(nameof(city));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            LivedPressure cityPressure = LivedPressure.ForCity(city, tuning);
            Dictionary<string, Bloc>? index = IndexPrevious(previous);

            // CitySnapshot.Districts is contractually sorted by Id, but sorting a local copy costs
            // nothing and removes the engine's dependence on a sensor honouring that contract. A
            // resorted district list would otherwise change nothing here yet reorder the output, and
            // downstream packets sum in list order.
            var districts = new List<DistrictSnapshot>();
            if (city.Districts != null)
            {
                for (int i = 0; i < city.Districts.Count; i++)
                {
                    DistrictSnapshot? d = city.Districts[i];
                    if (d != null) districts.Add(d);
                }
            }
            districts.Sort(CompareDistrictsById);

            var result = new List<Bloc>();
            for (int i = 0; i < districts.Count; i++)
            {
                result.AddRange(BuildDistrictCore(districts[i], cityPressure, tuning, index));
            }

            return result;
        }

        /// <summary>
        /// Builds one district's blocs. The city snapshot is still required: half of the lived-metric
        /// signal is the district measured <em>against</em> the city, so a district cannot be scored
        /// in isolation.
        /// </summary>
        public static List<Bloc> BuildDistrict(DistrictSnapshot district, CitySnapshot city,
                                               EngineTuning tuning, IReadOnlyList<Bloc>? previous)
        {
            if (district == null) throw new ArgumentNullException(nameof(district));
            if (city == null) throw new ArgumentNullException(nameof(city));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            return BuildDistrictCore(district, LivedPressure.ForCity(city, tuning), tuning, IndexPrevious(previous));
        }

        // --- district construction ---------------------------------------------------------------

        private static List<Bloc> BuildDistrictCore(DistrictSnapshot district, LivedPressure cityPressure,
                                                    EngineTuning tuning, Dictionary<string, Bloc>? previous)
        {
            var kept = new List<Bloc>();

            int population = district.Population;
            if (population <= 0) return kept;

            BlocsTuning t = tuning.Blocs;
            AgeBandMultipliers ageMultipliers = tuning.Turnout.AgeBandMultipliers;
            LivedPressure pressure = LivedPressure.ForDistrict(district, tuning);

            IReadOnlyList<BlocKey> keys = BlocAxes.AllKeys;

            double[]? shares = CompositionShares(district, t, previous, keys);
            if (shares == null) return kept; // no measurable composition; invent none.

            int[] counts = Apportion(population, shares);

            var candidates = new List<Bloc>(keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                BlocKey key = keys[i];
                Bloc? prior = Lookup(previous, district.Id, key);

                IssueWeights weights = BlocIssueModel.Resolve(
                    key, pressure, cityPressure, prior == null ? (IssueWeights?)null : prior.Weights, t);

                var bloc = new Bloc
                {
                    DistrictId = district.Id,
                    Key = key,
                    Population = counts[i],
                    PopulationShare = counts[i] / (double)population,
                    // Minors are disenfranchised by the turnout multiplier, never by a missing bloc
                    // (Contracts/Blocs.cs). The multiplier is the single source of truth for who may
                    // vote, so eligibility is read from it rather than restated here.
                    EligibleVoters = ageMultipliers[key.Age] > 0.0 ? counts[i] : 0,
                    Weights = weights,
                    Ideal = BlocIssueModel.Ideal(key, t),
                    Happiness = district.Happiness,
                    Discontent = BlocIssueModel.Discontent(district.Happiness, pressure, weights, t),
                    PreviousVote = CarryVote(prior),
                    HasCityFallbacks = district.HasCityFallbacks
                };

                candidates.Add(bloc);
            }

            // Prune the cells too small to be politically meaningful. Both thresholds apply: a head
            // count keeps tiny districts from spawning 60 statistically meaningless blocs, and a
            // share keeps a huge district from keeping 60 cells that each round to nothing.
            for (int i = 0; i < candidates.Count; i++)
            {
                Bloc bloc = candidates[i];
                if (bloc.Population >= t.MinBlocPopulation && bloc.PopulationShare >= t.MinBlocShare)
                {
                    kept.Add(bloc);
                }
            }

            // A district smaller than minBlocPopulation would otherwise vanish from the electorate
            // entirely. Keep its largest cell so it still votes; ties go to the lowest ordinal, never
            // to a coin flip.
            if (kept.Count == 0)
            {
                int best = -1;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (best < 0 || candidates[i].Population > candidates[best].Population) best = i;
                }
                if (best >= 0 && candidates[best].Population > 0) kept.Add(candidates[best]);
            }

            return kept;
        }

        /// <summary>
        /// The joint wealth × education × age composition, as 60 shares summing to 1.
        /// </summary>
        /// <remarks>
        /// The sensor reports three <em>marginal</em> distributions, not the joint one, so the joint
        /// is their outer product — the maximum-entropy reconstruction, and the only one that invents
        /// nothing. It does mean the model cannot see that the highly educated are also richer; a
        /// real wealth × education correlation would need either a joint-histogram sensor or a
        /// correlation coefficient in tuning, and neither exists. Returns null when the district
        /// reports no measurable composition at all.
        /// </remarks>
        private static double[]? CompositionShares(DistrictSnapshot district, BlocsTuning t,
                                                   Dictionary<string, Bloc>? previous,
                                                   IReadOnlyList<BlocKey> keys)
        {
            var observed = new double[keys.Count];
            double total = 0.0;

            for (int i = 0; i < keys.Count; i++)
            {
                BlocKey key = keys[i];
                double share = NonNegative(district.Wealth[key.Wealth])
                             * NonNegative(district.Education[key.Education])
                             * NonNegative(district.Age[key.Age]);
                observed[i] = share;
                total += share;
            }

            if (total <= 0.0 || double.IsNaN(total)) return null;

            for (int i = 0; i < observed.Length; i++) observed[i] /= total;

            if (previous == null) return observed;

            double alpha = double.IsNaN(t.CompositionSmoothingAlpha)
                ? 1.0
                : BlocMath.Clamp(t.CompositionSmoothingAlpha, 0.0, 1.0);
            if (alpha >= 1.0) return observed;

            // EMA against last tick, so a month of churn does not reshuffle the electorate. The prior
            // shares are read from the persisted blocs; cells pruned last tick read as zero, which is
            // why the result is renormalised rather than assumed to still sum to 1.
            var smoothed = new double[observed.Length];
            double smoothedTotal = 0.0;
            for (int i = 0; i < observed.Length; i++)
            {
                Bloc? prior = Lookup(previous, district.Id, keys[i]);
                double priorShare = prior == null ? 0.0 : NonNegative(prior.PopulationShare);
                smoothed[i] = alpha * observed[i] + (1.0 - alpha) * priorShare;
                smoothedTotal += smoothed[i];
            }

            if (smoothedTotal <= 0.0 || double.IsNaN(smoothedTotal)) return observed;

            for (int i = 0; i < smoothed.Length; i++) smoothed[i] /= smoothedTotal;
            return smoothed;
        }

        /// <summary>
        /// Largest-remainder apportionment of a head count across the bloc cells.
        /// </summary>
        /// <remarks>
        /// Rounding each cell independently would lose or gain people, and the election packet counts
        /// integer votes — a district whose blocs do not sum to its population would quietly
        /// manufacture or disenfranchise voters. Remainder ties break toward the lower index, which is
        /// <see cref="BlocKey.Ordinal"/> order, so the outcome never depends on sort stability.
        /// </remarks>
        private static int[] Apportion(int total, double[] shares)
        {
            var counts = new int[shares.Length];
            if (total <= 0) return counts;

            var remainders = new double[shares.Length];
            var order = new int[shares.Length];
            int assigned = 0;

            for (int i = 0; i < shares.Length; i++)
            {
                double exact = total * shares[i];
                if (double.IsNaN(exact) || exact < 0.0) exact = 0.0;

                int whole = (int)Math.Floor(exact);
                counts[i] = whole;
                assigned += whole;
                remainders[i] = exact - whole;
                order[i] = i;
            }

            int left = total - assigned;
            if (left <= 0) return counts;

            Array.Sort(order, delegate (int a, int b)
            {
                int byRemainder = remainders[b].CompareTo(remainders[a]);
                return byRemainder != 0 ? byRemainder : a.CompareTo(b);
            });

            for (int i = 0; i < left && i < order.Length; i++) counts[order[i]]++;
            return counts;
        }

        // --- previous-state lookup ---------------------------------------------------------------

        /// <summary>
        /// Indexes last tick's blocs for lookup. A dictionary is safe here precisely because it is
        /// only ever probed by key — it is never enumerated, so its iteration order can never reach
        /// engine state.
        /// </summary>
        private static Dictionary<string, Bloc>? IndexPrevious(IReadOnlyList<Bloc>? previous)
        {
            if (previous == null || previous.Count == 0) return null;

            var index = new Dictionary<string, Bloc>(previous.Count, StringComparer.Ordinal);
            for (int i = 0; i < previous.Count; i++)
            {
                Bloc? bloc = previous[i];
                if (bloc == null) continue;
                index[LookupKey(bloc.DistrictId, bloc.Key)] = bloc;
            }

            return index;
        }

        private static Bloc? Lookup(Dictionary<string, Bloc>? index, string districtId, BlocKey key)
        {
            if (index == null) return null;
            Bloc found;
            return index.TryGetValue(LookupKey(districtId, key), out found) ? found : null;
        }

        private static string LookupKey(string districtId, BlocKey key)
        {
            // The ordinal goes first, zero-padded to a fixed two digits, so no district name
            // can ever be parsed as part of it: "a1" + ordinal 2 must not collide with
            // "a" + ordinal 12.
            return key.Ordinal.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)
                   + ":" + (districtId ?? "");
        }

        private static List<PartyVoteShare> CarryVote(Bloc? prior)
        {
            if (prior == null || prior.PreviousVote == null) return new List<PartyVoteShare>();

            // Copied, not aliased: the caller's persisted state must not change under it, and the
            // order (by PartyId ordinal) is contractual, so it is preserved as-is.
            return new List<PartyVoteShare>(prior.PreviousVote);
        }

        private static int CompareDistrictsById(DistrictSnapshot a, DistrictSnapshot b)
        {
            return string.CompareOrdinal(a.Id ?? "", b.Id ?? "");
        }

        private static double NonNegative(double value)
        {
            if (double.IsNaN(value) || value < 0.0) return 0.0;
            return value;
        }
    }
}
