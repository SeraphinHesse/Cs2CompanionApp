using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Indices
{
    /// <summary>
    /// Packet 13 — turns a <see cref="CitySnapshot"/> (plus a little political context) into a
    /// <see cref="DerivedIndices"/>.
    ///
    /// <para>
    /// The whole packet is one static function. It reads frozen contract types, reads coefficients
    /// from <see cref="EngineTuning"/>, and returns a fresh <see cref="DerivedIndices"/>. It holds no
    /// state, mutates none of its arguments, and makes no stochastic draw — an index is an aggregate,
    /// not a sample, so there is nothing here for <c>SeedStreams</c> to seed.
    /// </para>
    ///
    /// <para>
    /// Determinism: every loop walks either a fixed array (<see cref="BlocAxes.Wealth"/>, the nine
    /// services in declaration order) or a list the function sorted itself by ordinal string
    /// comparison. No dictionary or hash set is enumerated anywhere in this packet.
    /// </para>
    /// </summary>
    public static class IndicesEngine
    {
        // AGORA-SEAM(§14.3): how far back Compute can look is bounded by whatever the caller
        // retained (scheduler.snapshotRetention, proposed 25). This packet neither prunes nor
        // extrapolates: if the window predates the oldest retained snapshot it takes the nearest one
        // it was given, and if it was given none the backward-looking legs read zero. No pruning
        // policy beyond "keep the newest N" is implemented here.
        //
        // AGORA-SEAM(§14): a low LegitimacyIndex does NOT schedule anything. The link from
        // legitimacy and mandate defiance to unrest events (mandates.unrestEventProbabilityOnDefiance)
        // is an open decision and belongs to the event scheduler; this packet only publishes the
        // number for it to read.


        /// <summary>
        /// The <see cref="DistrictSnapshot.CityFallbackFields"/> entry that means "this district's
        /// service coverage is really the city's". Such a district is excluded from the
        /// service-inequality spread — counting it would report the city against itself and make an
        /// unequal city look even.
        /// </summary>
        private const string ServicesFallbackField = "Services";

        /// <summary>Number of members on <see cref="ServiceCoverage"/>. Fixed by the contract.</summary>
        private const int ServiceCount = 9;

        /// <summary>
        /// Computes every derived index for one snapshot.
        /// </summary>
        /// <param name="input">Snapshot plus optional history and political context. Not mutated.</param>
        /// <param name="tuning">Coefficient source. Only the <c>indices</c> section is read.</param>
        /// <returns>
        /// A new <see cref="DerivedIndices"/> whose <see cref="DerivedIndices.Districts"/> list is
        /// sorted by district id, ordinal ascending.
        /// </returns>
        public static DerivedIndices Compute(IndicesInput input, EngineTuning tuning)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            CitySnapshot snapshot = input.Snapshot;
            IndicesTuning t = tuning.Indices;

            CitySnapshot? gentrificationBase = FindHistorical(input.History, snapshot.Date, t.GentrificationWindowMonths);
            CitySnapshot? brainDrainBase = FindHistorical(input.History, snapshot.Date, t.BrainDrainWindowMonths);

            List<DistrictSnapshot> districts = SortedDistricts(snapshot);

            // --- City-wide ---------------------------------------------------------------------

            double gini = IndexFormulas.Gini(snapshot.Wealth, t.GiniSampleBuckets);

            double brainDrain = brainDrainBase == null
                ? 0.0
                : IndexFormulas.BrainDrain(
                    brainDrainBase.Education.Index(),
                    snapshot.Education.Index(),
                    IndexFormulas.SkilledResidents(brainDrainBase.Population, brainDrainBase.Education),
                    IndexFormulas.SkilledResidents(snapshot.Population, snapshot.Education),
                    true,
                    t);

            double serviceInequality = ServiceInequality(districts, t);

            double commuteMisery = IndexFormulas.CommuteMisery(
                snapshot.AverageCommuteMinutes, snapshot.TrafficCongestion, t);

            double polarization = IndexFormulas.Polarization(SortedShares(input.VoteShares), t);

            double cityCoverage = IndexFormulas.WeightedCoverage(snapshot.Services, t.ServiceInequalityWeights);
            double discontent = IndexFormulas.Discontent(
                snapshot.Happiness, snapshot.Unemployment, cityCoverage, t);

            double legitimacy = IndexFormulas.Legitimacy(
                input.LastElectionTurnout,
                MandateDelivery(input.Mandates),
                input.Government == null ? (double?)null : input.Government.Stability,
                t);

            DerivedIndices? previous = input.Previous;

            var result = new DerivedIndices
            {
                GiniCoefficient = IndexFormulas.Smooth(gini, previous == null ? (double?)null : previous.GiniCoefficient, t),
                BrainDrainIndex = IndexFormulas.Smooth(brainDrain, previous == null ? (double?)null : previous.BrainDrainIndex, t),
                ServiceInequalityIndex = IndexFormulas.Smooth(serviceInequality, previous == null ? (double?)null : previous.ServiceInequalityIndex, t),
                CommuteMiseryIndex = IndexFormulas.Smooth(commuteMisery, previous == null ? (double?)null : previous.CommuteMiseryIndex, t),
                PolarizationIndex = IndexFormulas.Smooth(polarization, previous == null ? (double?)null : previous.PolarizationIndex, t),
                LegitimacyIndex = IndexFormulas.Smooth(legitimacy, previous == null ? (double?)null : previous.LegitimacyIndex, t),
                DiscontentIndex = IndexFormulas.Smooth(discontent, previous == null ? (double?)null : previous.DiscontentIndex, t)
            };

            // --- Per district ------------------------------------------------------------------

            for (int i = 0; i < districts.Count; i++)
            {
                DistrictSnapshot d = districts[i];
                DistrictSnapshot? gentBase = gentrificationBase == null
                    ? null
                    : FindDistrict(gentrificationBase.Districts, d.Id);
                DistrictIndices? prev = FindPreviousDistrict(previous, d.Id);

                double dGini = IndexFormulas.Gini(d.Wealth, t.GiniSampleBuckets);

                double gentrification = IndexFormulas.Gentrification(
                    d.RentTrend,
                    gentBase == null ? 0.0 : gentBase.Education.Index(),
                    d.Education.Index(),
                    gentBase == null ? 0.0 : gentBase.Wealth[WealthTier.Low],
                    d.Wealth[WealthTier.Low],
                    gentBase != null,
                    t);

                double dCommute = IndexFormulas.CommuteMisery(d.AverageCommuteMinutes, d.TrafficCongestion, t);
                double dCoverage = IndexFormulas.WeightedCoverage(d.Services, t.ServiceInequalityWeights);
                double dDiscontent = IndexFormulas.Discontent(d.Happiness, d.Unemployment, dCoverage, t);

                result.Districts.Add(new DistrictIndices
                {
                    DistrictId = d.Id,
                    GiniCoefficient = IndexFormulas.Smooth(dGini, prev == null ? (double?)null : prev.GiniCoefficient, t),
                    GentrificationIndex = IndexFormulas.Smooth(gentrification, prev == null ? (double?)null : prev.GentrificationIndex, t),
                    CommuteMiseryIndex = IndexFormulas.Smooth(dCommute, prev == null ? (double?)null : prev.CommuteMiseryIndex, t),
                    ServiceCoverageIndex = IndexFormulas.Smooth(dCoverage, prev == null ? (double?)null : prev.ServiceCoverageIndex, t),
                    DiscontentIndex = IndexFormulas.Smooth(dDiscontent, prev == null ? (double?)null : prev.DiscontentIndex, t),
                    HasCityFallbacks = d.HasCityFallbacks
                });
            }

            return result;
        }

        // -- Service inequality ---------------------------------------------------------------

        /// <summary>
        /// Weighted mean of the nine per-service dispersions across districts, in <c>[0, 1]</c>.
        ///
        /// <para><b>Formula.</b> For each service <c>s</c>, take the population-weighted dispersion
        /// (see <see cref="IndexFormulas.Dispersion"/>) of that service's coverage across the
        /// districts that actually measured it, then average the nine with
        /// <c>indices.serviceInequalityWeights</c>.</para>
        ///
        /// <para>Districts whose <c>Services</c> value is a city-wide fallback are dropped first.
        /// Fewer than two measured districts reads 0.</para>
        /// </summary>
        private static double ServiceInequality(List<DistrictSnapshot> sortedDistricts, IndicesTuning t)
        {
            var measured = new List<DistrictSnapshot>(sortedDistricts.Count);
            for (int i = 0; i < sortedDistricts.Count; i++)
            {
                if (!HasServicesFallback(sortedDistricts[i])) measured.Add(sortedDistricts[i]);
            }

            if (measured.Count < 2) return IndexFormulas.Clamp(0.0, t.ClampMin, t.ClampMax);

            var populations = new double[measured.Count];
            for (int i = 0; i < measured.Count; i++)
                populations[i] = measured[i].Population > 0 ? measured[i].Population : 0.0;

            var values = new double[measured.Count];
            double num = 0.0;
            double den = 0.0;

            for (int s = 0; s < ServiceCount; s++)
            {
                double weight = ServiceAt(t.ServiceInequalityWeights, s);
                if (double.IsNaN(weight) || weight <= 0.0) continue;

                for (int i = 0; i < measured.Count; i++)
                    values[i] = ServiceAt(measured[i].Services, s);

                num += weight * IndexFormulas.Dispersion(values, populations);
                den += weight;
            }

            double raw = den <= 0.0 ? 0.0 : num / den;
            return IndexFormulas.Clamp(raw, t.ClampMin, t.ClampMax);
        }

        private static bool HasServicesFallback(DistrictSnapshot d)
        {
            List<string> fields = d.CityFallbackFields;
            if (fields == null) return false;
            for (int i = 0; i < fields.Count; i++)
            {
                if (string.Equals(fields[i], ServicesFallbackField, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// The <paramref name="index"/>-th service, in <see cref="ServiceCoverage"/> declaration
        /// order. A fixed switch rather than reflection, so the iteration order is a compile-time
        /// fact.
        /// </summary>
        private static double ServiceAt(ServiceCoverage c, int index)
        {
            switch (index)
            {
                case 0: return c.Health;
                case 1: return c.Education;
                case 2: return c.Police;
                case 3: return c.Fire;
                case 4: return c.Garbage;
                case 5: return c.Transit;
                case 6: return c.Water;
                case 7: return c.Electricity;
                case 8: return c.Parks;
                default: throw new ArgumentOutOfRangeException(nameof(index), index, "Service index must be 0–8.");
            }
        }

        // -- Mandate delivery -------------------------------------------------------------------

        /// <summary>
        /// Mean delivery over resolved mandates, in <c>[0, 1]</c>, or null when nothing has resolved
        /// yet. <c>Fulfilled</c> counts 1; <c>PartiallyFulfilled</c> and <c>Defied</c> count their
        /// own recorded progress; <c>Abandoned</c> is excluded, because a government that fell was
        /// never given the chance to deliver, and <c>Pending</c>/<c>Active</c> are still running.
        /// </summary>
        private static double? MandateDelivery(IReadOnlyList<Mandate> mandates)
        {
            if (mandates == null || mandates.Count == 0) return null;

            var resolved = new List<Mandate>(mandates.Count);
            for (int i = 0; i < mandates.Count; i++)
            {
                Mandate m = mandates[i];
                if (m == null) continue;
                if (m.Status == MandateStatus.Fulfilled ||
                    m.Status == MandateStatus.PartiallyFulfilled ||
                    m.Status == MandateStatus.Defied)
                {
                    resolved.Add(m);
                }
            }

            if (resolved.Count == 0) return null;

            // Sorted by id so the summation order — and therefore the last bits of the mean — does
            // not depend on the order the caller happened to build the list in.
            resolved.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            double total = 0.0;
            for (int i = 0; i < resolved.Count; i++)
            {
                Mandate m = resolved[i];
                total += m.Status == MandateStatus.Fulfilled ? 1.0 : IndexFormulas.Clamp01(m.Progress);
            }

            return total / resolved.Count;
        }

        // -- Lookups ----------------------------------------------------------------------------

        /// <summary>
        /// The historical snapshot nearest <paramref name="months"/> before <paramref name="current"/>.
        /// Only strictly earlier snapshots are eligible. Ties break to the earlier date, so the answer
        /// does not depend on the order of <paramref name="history"/>.
        /// </summary>
        private static CitySnapshot? FindHistorical(IReadOnlyList<CitySnapshot> history, SimDate current, int months)
        {
            if (history == null || history.Count == 0 || months <= 0) return null;

            SimDate target = current.AddMonths(-months);
            CitySnapshot? best = null;
            int bestDistance = 0;

            for (int i = 0; i < history.Count; i++)
            {
                CitySnapshot candidate = history[i];
                if (candidate == null || candidate.Date >= current) continue;

                int distance = Math.Abs(candidate.Date.MonthsUntil(target));
                if (best == null || distance < bestDistance ||
                    (distance == bestDistance && candidate.Date < best.Date))
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static DistrictSnapshot? FindDistrict(List<DistrictSnapshot> districts, string id)
        {
            if (districts == null) return null;
            for (int i = 0; i < districts.Count; i++)
            {
                if (districts[i] != null && string.Equals(districts[i].Id, id, StringComparison.Ordinal))
                    return districts[i];
            }
            return null;
        }

        private static DistrictIndices? FindPreviousDistrict(DerivedIndices? previous, string id)
        {
            if (previous == null || previous.Districts == null) return null;
            for (int i = 0; i < previous.Districts.Count; i++)
            {
                DistrictIndices candidate = previous.Districts[i];
                if (candidate != null && string.Equals(candidate.DistrictId, id, StringComparison.Ordinal))
                    return candidate;
            }
            return null;
        }

        // -- Ordering ---------------------------------------------------------------------------

        /// <summary>
        /// The snapshot's districts, copied and sorted by id ordinal ascending. The contract already
        /// promises that order; sorting a copy makes this packet's output independent of whether the
        /// promise was kept, and never mutates the caller's list.
        /// </summary>
        private static List<DistrictSnapshot> SortedDistricts(CitySnapshot snapshot)
        {
            var copy = new List<DistrictSnapshot>();
            if (snapshot.Districts != null)
            {
                for (int i = 0; i < snapshot.Districts.Count; i++)
                {
                    if (snapshot.Districts[i] != null) copy.Add(snapshot.Districts[i]);
                }
            }
            copy.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return copy;
        }

        /// <summary>Vote shares copied and sorted by party id ordinal ascending — the contractual order.</summary>
        private static List<PartyVoteShare> SortedShares(IReadOnlyList<PartyVoteShare> shares)
        {
            var copy = new List<PartyVoteShare>();
            if (shares != null)
            {
                for (int i = 0; i < shares.Count; i++) copy.Add(shares[i]);
            }
            copy.Sort((a, b) => string.CompareOrdinal(a.PartyId, b.PartyId));
            return copy;
        }
    }
}
