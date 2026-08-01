using System;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Agora.Core.Tuning;
using Mandate = Agora.Core.Contracts.Mandate;

namespace Agora.Core.Engine.Government.Mandates
{
    /// <summary>
    /// What a resolved promise costs or earns. Pure data: the engine hands this to the indices, voter
    /// and effect packets, and none of it touches the city directly.
    /// </summary>
    public sealed class MandateResolution
    {
        public string MandateId { get; }

        /// <summary>Party that owns the promise — the one the reward or the blame attaches to.</summary>
        public string PartyId { get; }

        public string CoalitionId { get; }

        /// <summary>Target district, or null for a city-wide promise.</summary>
        public string? DistrictId { get; }

        public Issue Issue { get; }

        public MandateMetric Metric { get; }

        /// <summary>Fulfilled, PartiallyFulfilled, Defied or Abandoned. Never a live status.</summary>
        public MandateStatus Status { get; }

        /// <summary>Progress at resolution, <c>[0, 1]</c>.</summary>
        public double Progress { get; }

        /// <summary>Salience actually applied, floored at <c>mandates.salienceFloor</c>.</summary>
        public double Salience { get; }

        /// <summary>Signed happiness points on the 0–100 scale. Positive rewards the government.</summary>
        public double HappinessDelta { get; }

        /// <summary>Signed change to the legitimacy index, consumed by the indices packet.</summary>
        public double LegitimacyDelta { get; }

        /// <summary>Vote share that swings to the opposition. Non-negative; zero unless defied.</summary>
        public double OppositionSurge { get; }

        /// <summary>
        /// True when the defiance roll came up unrest. Statistical only — see the §14.5 seam in
        /// <see cref="MandateResolver"/>.
        /// </summary>
        public bool UnrestTriggered { get; }

        /// <summary>Palette id of the applied effect, or null when no effect was requested.</summary>
        public string? ResolutionEffectId { get; }

        /// <summary>The capped effect request, or null. Magnitude and duration are already clamped.</summary>
        public EffectRequest? Effect { get; }

        public SimDate ResolvedDate { get; }

        internal MandateResolution(string mandateId, string partyId, string coalitionId, string? districtId,
                                   Issue issue, MandateMetric metric, MandateStatus status, double progress,
                                   double salience, double happinessDelta, double legitimacyDelta,
                                   double oppositionSurge, bool unrestTriggered, string? resolutionEffectId,
                                   EffectRequest? effect, SimDate resolvedDate)
        {
            MandateId = mandateId;
            PartyId = partyId;
            CoalitionId = coalitionId;
            DistrictId = districtId;
            Issue = issue;
            Metric = metric;
            Status = status;
            Progress = progress;
            Salience = salience;
            HappinessDelta = happinessDelta;
            LegitimacyDelta = legitimacyDelta;
            OppositionSurge = oppositionSurge;
            UnrestTriggered = unrestTriggered;
            ResolutionEffectId = resolutionEffectId;
            Effect = effect;
            ResolvedDate = resolvedDate;
        }
    }

    /// <summary>
    /// Scores a finished promise and prices its consequence (§3 Mandates: fulfilled → happiness up and
    /// governing credit; defied → happiness down, opposition surge, possible unrest).
    ///
    /// <para>
    /// The player is never punished beyond a sanctioned effect. Every request produced here is
    /// clamped twice — once against the palette entry's own cap, once against the global ceiling —
    /// before it leaves this class, and the sink clamps a third time.
    /// </para>
    /// </summary>
    public static class MandateResolver
    {
        /// <summary>
        /// Prices one resolution. <paramref name="outcome"/> comes from
        /// <see cref="MandateMonitor"/>; this method never decides whether a mandate is finished, only
        /// what it costs.
        /// </summary>
        /// <remarks>
        /// The only stochastic step is the unrest roll on defiance, drawn from
        /// <c>event.unrest</c> keyed by the mandate id, so the outcome is independent of how many
        /// other mandates resolved this tick.
        /// </remarks>
        public static MandateResolution Resolve(Guid saveGuid, SimDate date, Mandate mandate,
                                                MandateStatus outcome, EngineTuning tuning)
        {
            if (mandate == null) throw new ArgumentNullException(nameof(mandate));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            MandatesTuning m = tuning.Mandates;

            double salience = MandateMath.Clamp(
                MandateMath.IsFinite(mandate.Salience) ? mandate.Salience : 0.0,
                MandateMath.Clamp01(m.SalienceFloor),
                1.0);

            double happiness = 0.0;
            double legitimacy = 0.0;
            double surge = 0.0;
            bool unrest = false;

            switch (outcome)
            {
                case MandateStatus.Fulfilled:
                    happiness = m.FulfilledHappinessBonus * salience;
                    legitimacy = m.FulfilledLegitimacyBonus * salience;
                    break;

                case MandateStatus.PartiallyFulfilled:
                    // Partial credit buys goodwill but not legitimacy: the government moved the number
                    // without keeping the promise. There is deliberately no partial legitimacy key.
                    happiness = m.PartialHappinessBonus * salience;
                    break;

                case MandateStatus.Defied:
                    happiness = -m.DefiedHappinessPenalty * salience;
                    legitimacy = -m.DefiedLegitimacyPenalty * salience;
                    surge = m.OppositionSurgeOnDefiance * salience;

                    // AGORA-SEAM(§14.5): unrest is confirmed statistical-only — no visual destruction,
                    // no map mutation. This rolls the flag and stops there; turning it into a timeline
                    // event (and any effect that carries it) belongs to the events packet, which owns
                    // the ceiling decision. Do not add destruction here when §14.5 closes.
                    unrest = SeedStreams
                        .RngFor(saveGuid, date, StreamNames.UnrestTrigger, EntityKey(mandate))
                        .NextBool(MandateMath.Clamp01(m.UnrestEventProbabilityOnDefiance));
                    break;

                case MandateStatus.Abandoned:
                    // Never scored (§6). A promise the engine could not measure costs the player nothing.
                    return new MandateResolution(mandate.Id, mandate.PartyId, mandate.CoalitionId,
                        mandate.DistrictId, mandate.Issue, mandate.Metric, MandateStatus.Abandoned,
                        MandateMath.SafeProgress(mandate.Progress), salience, 0.0, 0.0, 0.0, false, null, null, date);

                default:
                    throw new ArgumentOutOfRangeException(nameof(outcome), outcome,
                        "Only Fulfilled, PartiallyFulfilled, Defied or Abandoned can be resolved.");
            }

            EffectRequest? effect = BuildEffect(mandate, happiness, tuning);

            return new MandateResolution(mandate.Id, mandate.PartyId, mandate.CoalitionId, mandate.DistrictId,
                mandate.Issue, mandate.Metric, outcome, MandateMath.SafeProgress(mandate.Progress), salience,
                happiness, legitimacy, surge, unrest,
                effect.HasValue ? effect.Value.EffectId : null, effect, date);
        }

        /// <summary>
        /// Turns a happiness stake into a capped effect request, or null when nothing should be applied.
        ///
        /// <para>
        /// Scope, magnitude cap, duration cap and fallback (non-negotiable #5): the scope follows the
        /// mandate, the effect id is the terminal happiness entry for that scope
        /// (<c>effects.defaultFallbackDistrictEffectId</c> / <c>...CityEffectId</c>, whose own
        /// <c>fallbackEffectId</c> is empty so the sink cannot loop), the magnitude is clamped by the
        /// palette entry and then by <c>effects.globalMagnitudeCap</c>, and the duration is
        /// <c>mandates.resolutionEffectDurationMonths</c> clamped the same way.
        /// </para>
        /// </summary>
        /// <param name="happinessDelta">Signed happiness points on the 0–100 scale.</param>
        public static EffectRequest? BuildEffect(Mandate mandate, double happinessDelta, EngineTuning tuning)
        {
            if (mandate == null || tuning == null) return null;

            EffectsTuning e = tuning.Effects;
            if (!e.Enabled) return null;
            if (!MandateMath.IsFinite(happinessDelta) || happinessDelta == 0.0) return null;

            bool district = !string.IsNullOrEmpty(mandate.DistrictId);
            EffectScope scope = district ? EffectScope.District : EffectScope.City;

            string effectId = district ? e.DefaultFallbackDistrictEffectId : e.DefaultFallbackCityEffectId;
            if (string.IsNullOrEmpty(effectId)) return null;

            EffectCap cap = e.CapFor(effectId, scope);

            // Happiness is 0–100 by contract; the modifier wants a fraction, so the stake divides by
            // the scale it is expressed in. A unit conversion, not a tuned coefficient.
            double requested = happinessDelta / MandateMetrics.HappinessScale;

            double magnitude = cap.ClampMagnitude(requested);
            magnitude = MandateMath.Clamp(magnitude, -e.GlobalMagnitudeCap, e.GlobalMagnitudeCap);

            int months = cap.ClampDuration(tuning.Mandates.ResolutionEffectDurationMonths);
            if (months > e.GlobalDurationCapMonths) months = e.GlobalDurationCapMonths;
            if (months <= 0) return null;

            if (Math.Abs(magnitude) < e.MinEffectiveMagnitude) return null;

            return new EffectRequest(effectId, scope, magnitude, months,
                                     district ? mandate.DistrictId : null, mandate.Id);
        }

        private static string EntityKey(Mandate mandate) =>
            string.IsNullOrEmpty(mandate.Id) ? "mandate-unknown" : mandate.Id;
    }
}
