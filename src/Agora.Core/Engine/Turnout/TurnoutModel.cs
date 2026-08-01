using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Turnout
{
    /// <summary>
    /// Packet 5 — the turnout model. Projects, for every bloc in every district, what fraction of its
    /// eligible voters actually casts a ballot (<c>politicsmodplan.md</c> §3 Campaigns: "turnout
    /// scales with happiness and education and can flip close races").
    ///
    /// <para>
    /// <b>Pure by construction.</b> <see cref="Project"/> is static, holds no field, caches nothing and
    /// mutates none of its arguments. Turnout is an input to <em>both</em> poll error and seat
    /// allocation, so any hidden state here would desync two downstream packets against each other —
    /// and the symptom would show up as a wrong seat count, a long way from the cause.
    /// </para>
    ///
    /// <para>
    /// <b>The order of operations matters.</b> The behavioural terms are summed, the sum is clamped to
    /// <c>[floor, ceiling]</c>, and only <em>then</em> is the age-band multiplier applied. Doing it the
    /// other way round would lift child and teen blocs — multiplier 0, which is how §4.3 disenfranchises
    /// minors — back up to the 10% floor and hand the vote to eight-year-olds.
    /// </para>
    /// </summary>
    public static class TurnoutModel
    {
        /// <summary>
        /// Projects turnout for the whole city. Deterministic: the same
        /// <see cref="TurnoutInputs"/> and <see cref="EngineTuning"/> always produce identical output,
        /// down to the last integer vote, on any machine and any runtime.
        /// </summary>
        /// <remarks>
        /// Every noise draw comes from a per-bloc sub-stream
        /// (<c>voter.turnout.noise:&lt;districtId&gt;/&lt;blocId&gt;</c>) rather than from one generator
        /// walked in a loop, so adding a district cannot shift any other district's result.
        /// </remarks>
        public static TurnoutProjection Project(TurnoutInputs inputs, EngineTuning tuning)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            TurnoutTuning t = tuning.Turnout;

            // Bounds are read, not assumed: a hand-edited tuning file with floor above ceiling must
            // degrade to a usable interval rather than clamp everything to a single value.
            double lo = Math.Min(Finite(t.Floor, 0.0), Finite(t.Ceiling, 1.0));
            double hi = Math.Max(Finite(t.Floor, 0.0), Finite(t.Ceiling, 1.0));
            lo = Clamp(lo, 0.0, 1.0);
            hi = Clamp(hi, 0.0, 1.0);

            double campaignIntensity = Clamp(Finite(inputs.CampaignIntensity, 0.0), 0.0, 1.0);
            int incumbentTerms = inputs.IncumbentConsecutiveTerms > 0 ? inputs.IncumbentConsecutiveTerms : 0;

            // --- group blocs by district ---------------------------------------------------------
            // districtIds is collected in input order and then sorted; the dictionary is a lookup
            // table only and is never enumerated, so no result depends on its bucket order.
            var districtIds = new List<string>();
            var byDistrict = new Dictionary<string, List<BlocEntry>>(StringComparer.Ordinal);

            IReadOnlyList<Bloc> blocs = inputs.Blocs;
            if (blocs == null) blocs = new List<Bloc>();

            for (int i = 0; i < blocs.Count; i++)
            {
                Bloc b = blocs[i];
                if (b == null) continue;

                string districtId = b.DistrictId;
                if (districtId == null) districtId = "";

                List<BlocEntry> bucket;
                if (!byDistrict.TryGetValue(districtId, out bucket))
                {
                    bucket = new List<BlocEntry>();
                    byDistrict[districtId] = bucket;
                    districtIds.Add(districtId);
                }

                bucket.Add(new BlocEntry(b, i));
            }

            districtIds.Sort(StringComparer.Ordinal);

            double cityCompetitiveness = Competitiveness(inputs.CityStandings);
            Dictionary<string, double> districtCompetitiveness = BuildCompetitiveness(inputs.DistrictStandings);

            // --- project each district -----------------------------------------------------------
            var districts = new List<DistrictTurnout>(districtIds.Count);
            long cityEligible = 0;
            long cityVotes = 0;

            for (int d = 0; d < districtIds.Count; d++)
            {
                string districtId = districtIds[d];
                List<BlocEntry> entries = byDistrict[districtId];

                // Total order (bloc ordinal, then input index) — no reliance on sort stability, and
                // duplicate keys from a malformed caller still land in a fixed order.
                entries.Sort(CompareEntries);

                double competitiveness;
                if (!districtCompetitiveness.TryGetValue(districtId, out competitiveness))
                    competitiveness = cityCompetitiveness;

                var blocTurnouts = new List<BlocTurnout>(entries.Count);
                int districtEligible = 0;
                int districtVotes = 0;

                for (int e = 0; e < entries.Count; e++)
                {
                    Bloc bloc = entries[e].Bloc;
                    BlocKey key = bloc.Key;

                    DeterministicRng rng = SeedStreams.RngFor(
                        inputs.SaveGuid, inputs.Date, StreamNames.TurnoutNoise, districtId + "/" + key.Id);
                    double noise = rng.NextGaussian() * Finite(t.NoiseSigma, 0.0);

                    // Summed in this literal order so the floating-point result is bit-stable.
                    double raw =
                        Finite(t.Base, 0.0)
                        + t.HappinessCoefficient * ((Clamp(Finite(bloc.Happiness, t.ReferenceHappiness), 0.0, 100.0)
                                                     - Finite(t.ReferenceHappiness, 50.0)) / 100.0)
                        + t.EducationCoefficient * (EducationIndex(key.Education)
                                                    - Finite(t.ReferenceEducationIndex, 0.5))
                        + t.WealthCoefficient * BlocAxes.Axis(key.Wealth)
                        + t.DiscontentCoefficient * Clamp(Finite(bloc.Discontent, 0.0), 0.0, 1.0)
                        + t.CompetitivenessCoefficient * competitiveness
                        + t.CampaignIntensityCoefficient * campaignIntensity
                        - t.IncumbentTermFatigue * incumbentTerms
                        - (inputs.IsSnapElection ? Finite(t.SnapElectionPenalty, 0.0) : 0.0)
                        + noise;

                    double bounded = Clamp(Finite(raw, lo), lo, hi);

                    // Age multiplier last, so the 0 for minors survives the floor. The ceiling is
                    // re-applied because the elderly multiplier is above 1 and turnout above the
                    // ceiling — or above 100% — is not a thing.
                    double multiplier = Math.Max(0.0, Finite(t.AgeBandMultipliers[key.Age], 0.0));
                    double rate = Clamp(bounded * multiplier, 0.0, hi);

                    int eligible = bloc.EligibleVoters > 0 ? bloc.EligibleVoters : 0;
                    int votes = (int)Math.Round(rate * eligible, MidpointRounding.AwayFromZero);
                    if (votes < 0) votes = 0;
                    if (votes > eligible) votes = eligible;

                    blocTurnouts.Add(new BlocTurnout
                    {
                        DistrictId = districtId,
                        Bloc = key,
                        Turnout = rate,
                        EligibleVoters = eligible,
                        ProjectedVotes = votes,
                        NoiseComponent = noise
                    });

                    districtEligible += eligible;
                    districtVotes += votes;
                }

                double districtRate = districtEligible > 0 ? districtVotes / (double)districtEligible : 0.0;

                districts.Add(new DistrictTurnout(districtId, districtRate, districtEligible, districtVotes,
                                                  competitiveness, blocTurnouts));

                cityEligible += districtEligible;
                cityVotes += districtVotes;
            }

            double cityRate = cityEligible > 0 ? cityVotes / (double)cityEligible : 0.0;

            return new TurnoutProjection(inputs.Date, cityRate, (int)cityEligible, (int)cityVotes, districts);
        }

        /// <summary>
        /// How close a race is, 0–1: the runner-up's share over the leader's. 1 is a dead heat, 0 an
        /// uncontested field.
        /// </summary>
        /// <remarks>
        /// Deliberately parameter-free rather than <c>1 − margin</c> against some normalising width.
        /// A raw top-two margin is not comparable across electoral systems — a six-party PR field
        /// leading 28–24 is a knife-edge, a two-party FPTP field leading 28–24 is a rout — so a shared
        /// margin scale would have to be tuned twice. The ratio self-normalises for party count.
        /// Order-independent: max and second-max do not depend on how the list is walked.
        /// </remarks>
        public static double Competitiveness(IReadOnlyList<PartyVoteShare>? shares)
        {
            if (shares == null || shares.Count < 2) return 0.0;

            double top = double.NegativeInfinity;
            double second = double.NegativeInfinity;

            for (int i = 0; i < shares.Count; i++)
            {
                double s = shares[i].Share;
                if (double.IsNaN(s) || double.IsInfinity(s)) continue;
                if (s < 0.0) s = 0.0;

                if (s > top)
                {
                    second = top;
                    top = s;
                }
                else if (s > second)
                {
                    second = s;
                }
            }

            if (double.IsNegativeInfinity(top) || top <= 0.0) return 0.0;
            if (double.IsNegativeInfinity(second) || second <= 0.0) return 0.0;

            return Clamp(second / top, 0.0, 1.0);
        }

        /// <summary>
        /// A bloc's education on <c>[0, 1]</c>. Uses the same 0 / .25 / .5 / .75 / 1 ladder as
        /// <see cref="EducationDistribution.Index"/>, so a bloc-level and a district-level education
        /// index mean the same thing and <c>turnout.referenceEducationIndex</c> reads correctly
        /// against either.
        /// </summary>
        public static double EducationIndex(EducationTier tier) => (int)tier / 4.0;

        private static Dictionary<string, double> BuildCompetitiveness(IReadOnlyList<DistrictResult>? standings)
        {
            var map = new Dictionary<string, double>(StringComparer.Ordinal);
            if (standings == null) return map;

            for (int i = 0; i < standings.Count; i++)
            {
                DistrictResult r = standings[i];
                if (r == null || r.DistrictId == null) continue;

                // First entry wins, so a duplicated district id cannot make the result depend on
                // which copy the loop happened to see last.
                if (!map.ContainsKey(r.DistrictId))
                    map[r.DistrictId] = Competitiveness(r.Shares);
            }

            return map;
        }

        private static int CompareEntries(BlocEntry a, BlocEntry b)
        {
            int byOrdinal = a.Bloc.Key.Ordinal.CompareTo(b.Bloc.Key.Ordinal);
            return byOrdinal != 0 ? byOrdinal : a.Index.CompareTo(b.Index);
        }

        /// <summary>netstandard2.0 has no <c>Math.Clamp</c>. Polyfilled here rather than raising the target.</summary>
        private static double Clamp(double value, double min, double max) =>
            value < min ? min : value > max ? max : value;

        /// <summary>Maps NaN and the infinities onto a sane value, so one bad metric cannot poison a whole election.</summary>
        private static double Finite(double value, double fallback) =>
            double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;

        private readonly struct BlocEntry
        {
            public readonly Bloc Bloc;
            public readonly int Index;

            public BlocEntry(Bloc bloc, int index)
            {
                Bloc = bloc;
                Index = index;
            }
        }
    }
}
