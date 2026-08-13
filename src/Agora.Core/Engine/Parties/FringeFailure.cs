using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Parties
{
    /// <summary>One tick's worth of observations about how the establishment is governing.</summary>
    public struct FringeMonth
    {
        /// <summary><see cref="DerivedIndices.DiscontentIndex"/>, 0–1.</summary>
        public double CityDiscontent { get; set; }

        /// <summary>
        /// Summed <c>MandateResolution.OppositionSurge</c> for mandates resolved this tick as Defied
        /// and owned by a party with <see cref="Party.IsMajor"/>. Salience-weighted at source.
        /// </summary>
        public double MajorDefianceSurge { get; set; }

        /// <summary>Governments that collapsed this tick.</summary>
        public int GovernmentChanges { get; set; }

        /// <summary>Elections this tick that changed which party holds the mayoralty.</summary>
        public int MayorChanges { get; set; }
    }

    /// <summary>
    /// Packet 15 — scores how badly the major parties are governing, and turns that into a per-party
    /// ceiling for the minor ones.
    ///
    /// <para>
    /// The problem this exists to solve is that nothing in the voter model converted major-party
    /// failure into minor-party gain. The incumbency and mandate terms are both party-scoped: they can
    /// subtract from the government, but they never add to anyone. So a fringe party's support was a
    /// function of platform proximity and habitual loyalty and nothing else, and no amount of
    /// misgovernment made a third party viable — nor any amount of good government made it go away.
    /// </para>
    ///
    /// <para>
    /// Four signals feed the answer, and they split into two kinds. Three are city-wide and describe
    /// the establishment's record — defied promises, sustained discontent, and turnover — and combine
    /// into one <see cref="Score"/> per term. The fourth is per-party: a fringe party only rises if the
    /// city is aggrieved on <see cref="Party.CoreGrievance"/>, the issue that brand actually owns. That
    /// is what stops a bad government handing an automatic windfall to every minor party at once, and
    /// what makes the surge legible — the environmentalists gain when the environment is being
    /// neglected, not merely when the mayor is unpopular.
    /// </para>
    ///
    /// <para>
    /// Nothing here draws a random number. There is no seeded stream for this packet and no stream
    /// rename to migrate: the score is a deterministic fold over state the engine already keeps.
    /// </para>
    /// </summary>
    public static class FringeFailureModel
    {
        /// <summary>Guard against dividing by a tuning value someone set to zero.</summary>
        private const double MinDivisor = 1e-9;

        // ---------------------------------------------------------------------------------------
        // Accumulating a term
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Folds one tick into the open accumulator. Called every tick; cheap and order-free.
        /// </summary>
        public static void Observe(FringeWatch watch, FringeMonth month, FringeTuning tuning)
        {
            if (watch == null) throw new ArgumentNullException(nameof(watch));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            if (!tuning.Enabled) return;

            watch.MonthsObserved++;
            watch.DiscontentSum += Clamp01(month.CityDiscontent);

            double surge = month.MajorDefianceSurge;
            if (surge > 0.0 && !double.IsNaN(surge) && !double.IsInfinity(surge))
                watch.DefianceSurgeSum += surge;

            if (month.GovernmentChanges > 0) watch.GovernmentChanges += month.GovernmentChanges;
            if (month.MayorChanges > 0) watch.MayorChanges += month.MayorChanges;
        }

        /// <summary>
        /// Scores the term currently accumulating, 0–1. A weighted mean of the three city-wide
        /// signals, each normalised to <c>[0, 1]</c> against its own saturation point.
        /// </summary>
        /// <remarks>
        /// The weights are asserted to sum to 1 in <c>ShippedTuningTests</c> rather than renormalised
        /// here: renormalising would let a mis-tuned file quietly redefine what
        /// <c>failureTermScoreThreshold</c> means instead of failing a test.
        /// </remarks>
        public static double Score(FringeWatch watch, FringeTuning tuning)
        {
            if (watch == null) throw new ArgumentNullException(nameof(watch));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            // Broken promises, salience-weighted at source by the mandate packet.
            double defiance = Clamp01(watch.DefianceSurgeSum /
                                      Math.Max(MinDivisor, tuning.DefianceSurgeForFullSignal));

            // Sustained unhappiness. Measured against a floor, so an ordinarily grumpy city does not
            // read as a failing one — only the part above the floor counts.
            double meanDiscontent = watch.MonthsObserved > 0
                ? watch.DiscontentSum / watch.MonthsObserved
                : 0.0;
            double floor = Clamp01(tuning.DiscontentFloor);
            double discontent = Clamp01((meanDiscontent - floor) / Math.Max(MinDivisor, 1.0 - floor));

            // Governments falling over and mayors being turned out.
            int churnEvents = watch.GovernmentChanges + watch.MayorChanges;
            double churn = Clamp01(churnEvents /
                                   (double)Math.Max(1, tuning.ChurnEventsForFullSignal));

            return Clamp01(tuning.DefianceWeight * defiance +
                           tuning.DiscontentWeight * discontent +
                           tuning.ChurnWeight * churn);
        }

        /// <summary>
        /// Closes the term at an election: scores it, extends or breaks the streak, and zeroes the
        /// accumulator for the next one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Idempotent by term number. Scoring the same term twice — a reload replaying the election
        /// month, say — would otherwise double a streak the city never actually lived through.
        /// </para>
        /// <para>
        /// One good term resets the streak to zero outright rather than decaying it. The ratified rule
        /// is "unless the major parties fail to deliver, repeatedly", and a decay would let a fringe
        /// party keep most of an unlock it had stopped earning.
        /// </para>
        /// </remarks>
        public static void CloseTerm(FringeWatch watch, int termNumber, FringeTuning tuning)
        {
            if (watch == null) throw new ArgumentNullException(nameof(watch));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            if (!tuning.Enabled) return;
            if (termNumber <= watch.LastClosedTermNumber) return;

            double score = Score(watch, tuning);

            if (score >= tuning.FailureTermScoreThreshold) watch.ConsecutiveFailureTerms++;
            else watch.ConsecutiveFailureTerms = 0;

            watch.LastTermFailureScore = score;
            watch.LastClosedTermNumber = termNumber;

            watch.TermNumber = termNumber;
            watch.MonthsObserved = 0;
            watch.DiscontentSum = 0.0;
            watch.DefianceSurgeSum = 0.0;
            watch.GovernmentChanges = 0;
            watch.MayorChanges = 0;
        }

        // ---------------------------------------------------------------------------------------
        // Turning the record into ceilings
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The ceilings in force this tick, one entry per minor party. Majors never get an entry, and
        /// neither does anything at all outside the FPTP system.
        /// </summary>
        /// <param name="parties">The registry. Only parties on the ballot are capped.</param>
        /// <param name="watch">The closed failure record. The open accumulator is not consulted.</param>
        /// <param name="cityGrievance">
        /// Per-issue city grievance from <c>IssueClimate.FromBlocs</c>, already computed each tick for
        /// the lifecycle pass.
        /// </param>
        /// <param name="system">Proportional returns <see cref="FringeCeilings.None"/>.</param>
        public static FringeCeilings Ceilings(IReadOnlyList<Party> parties, FringeWatch watch,
                                              IssueWeights cityGrievance, ElectoralSystem system,
                                              FringeTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            if (!tuning.Enabled) return FringeCeilings.None;
            if (system != ElectoralSystem.FirstPastThePost) return FringeCeilings.None;
            if (parties == null || parties.Count == 0) return FringeCeilings.None;

            FringeWatch w = watch ?? new FringeWatch();
            double streak = StreakFactor(w, tuning);

            var list = new List<PartyCeiling>();

            for (int i = 0; i < parties.Count; i++)
            {
                Party p = parties[i];
                if (p == null || string.IsNullOrEmpty(p.Id)) continue;
                if (p.IsMajor) continue;
                if (!IsOnBallot(p.Status)) continue;

                list.Add(new PartyCeiling(p.Id, CeilingFor(p, w, streak, cityGrievance, tuning)));
            }

            return FringeCeilings.FromList(list);
        }

        /// <summary>
        /// One minor party's ceiling. Exposed for the dashboard's explanation of why a party is stuck,
        /// and for tests that want the number without building a registry.
        /// </summary>
        public static double CeilingFor(Party party, FringeWatch watch, IssueWeights cityGrievance,
                                        FringeTuning tuning)
        {
            if (party == null) throw new ArgumentNullException(nameof(party));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            FringeWatch w = watch ?? new FringeWatch();
            return CeilingFor(party, w, StreakFactor(w, tuning), cityGrievance, tuning);
        }

        private static double CeilingParkedAt(FringeTuning tuning) => Clamp01(tuning.BaseCeiling);

        private static double CeilingFor(Party party, FringeWatch watch, double streak,
                                         IssueWeights cityGrievance, FringeTuning tuning)
        {
            double baseCeiling = CeilingParkedAt(tuning);

            // Shut until the majors have failed for long enough. This is the literal reading of the
            // ratified rule: below the unlock, no amount of grievance moves the ceiling at all.
            if (streak <= 0.0) return baseCeiling;

            // How badly they failed. Uses the last CLOSED term, not the term in progress, so the
            // ceiling is a judgement on a completed record rather than a running commentary.
            double severity = Clamp01(watch.LastTermFailureScore);
            if (severity <= 0.0) return baseCeiling;

            // Whose grievance it is. This is the signal that decides WHICH fringe party benefits.
            double gate = GrievanceGate(party, cityGrievance, tuning);
            if (gate <= 0.0) return baseCeiling;

            double maxCeiling = Clamp01(tuning.MaxCeiling);
            if (maxCeiling <= baseCeiling) return baseCeiling;

            double ceiling = baseCeiling + (maxCeiling - baseCeiling) * severity * streak * gate;

            if (ceiling < baseCeiling) ceiling = baseCeiling;
            if (ceiling > maxCeiling) ceiling = maxCeiling;
            return ceiling;
        }

        /// <summary>
        /// 0 until <c>unlockConsecutiveTerms</c> failure terms have run, then rising to 1 at
        /// <c>fullUnlockTerms</c>. The first qualifying term already scores above zero — reaching the
        /// unlock is itself the event, and a factor that started at zero there would make the third
        /// term indistinguishable from the second.
        /// </summary>
        private static double StreakFactor(FringeWatch watch, FringeTuning tuning)
        {
            int unlock = tuning.UnlockConsecutiveTerms;
            if (unlock < 1) unlock = 1;

            int streak = watch.ConsecutiveFailureTerms;
            if (streak < unlock) return 0.0;

            int full = tuning.FullUnlockTerms;
            if (full < unlock) full = unlock;

            int span = full - unlock + 1;
            return Clamp01((streak - unlock + 1) / (double)Math.Max(1, span));
        }

        /// <summary>
        /// How aggrieved the city is on this party's own core issue, rescaled so that
        /// <c>grievanceFloor</c> reads 0 and total grievance reads 1.
        /// </summary>
        private static double GrievanceGate(Party party, IssueWeights cityGrievance, FringeTuning tuning)
        {
            double grievance = Clamp01(cityGrievance[party.CoreGrievance]);
            double floor = Clamp01(tuning.GrievanceFloor);

            return Clamp01((grievance - floor) / Math.Max(MinDivisor, 1.0 - floor));
        }

        private static bool IsOnBallot(PartyStatus status) =>
            status == PartyStatus.Active || status == PartyStatus.Endangered || status == PartyStatus.Revived;

        // netstandard2.0 has no Math.Clamp.
        private static double Clamp01(double v)
        {
            if (double.IsNaN(v)) return 0.0;
            return v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
        }
    }
}
