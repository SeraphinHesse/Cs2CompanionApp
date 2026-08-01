using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Determinism;
using Agora.Core.Tuning;

namespace Agora.Core.Events.Scheduler
{
    /// <summary>
    /// Turns an authored effect request into a sanctioned one: severity scaling, the catalog ceiling,
    /// the per-effect palette cap, the global belt-and-braces cap, and a deterministic district target.
    /// </summary>
    /// <remarks>
    /// Non-negotiable #5 — every effect declares scope, magnitude cap, duration cap and a fallback.
    /// The sink clamps again at apply time; clamping here as well means a bad catalog entry cannot even
    /// reach the sink, and the scheduler's own output is inspectable in a test without a game.
    /// </remarks>
    internal static class EffectResolution
    {
        /// <summary>Severity 1 is the authored magnitude; each further point adds <c>catalog.severityEffectScale</c>.</summary>
        internal static double ScaleForSeverity(double magnitude, int severity, CatalogTuning catalog)
        {
            int s = ClampSeverity(severity, catalog);
            return magnitude * (1.0 + catalog.SeverityEffectScale * (s - 1));
        }

        internal static int ClampSeverity(int severity, CatalogTuning catalog)
        {
            int max = catalog.SeverityMax < 1 ? 1 : catalog.SeverityMax;
            if (severity < 1) return 1;
            return severity > max ? max : severity;
        }

        /// <summary>Symmetric clamp. netstandard2.0 has no <c>Math.Clamp</c>; this is the polyfill.</summary>
        internal static double ClampAbs(double value, double cap)
        {
            if (double.IsNaN(value)) return 0.0;
            double c = cap < 0.0 ? -cap : cap;
            if (value > c) return c;
            if (value < -c) return -c;
            return value;
        }

        internal static int ClampMonths(int months, int cap)
        {
            if (months < 0) return 0;
            int c = cap < 0 ? 0 : cap;
            return months > c ? c : months;
        }

        /// <summary>
        /// Resolves one authored effect. Returns false when the effect must be dropped — below the
        /// noise floor, or district-scoped in a city with no districts.
        /// </summary>
        /// <param name="authored">The catalog / archetype entry, with a null district id.</param>
        /// <param name="severity">The firing event's severity.</param>
        /// <param name="seedDate">
        /// The event's <b>authored</b> date, not the tick date. District targeting keys on it so that a
        /// catch-up tick that fires an event late still picks the same district as a live tick would.
        /// </param>
        internal static bool TryResolve(TimelineEventEffect authored, int severity, Guid saveGuid,
                                        SimDate seedDate, string eventId, int effectIndex,
                                        IReadOnlyList<string> sortedDistrictIds, EngineTuning tuning,
                                        List<string> warnings, out TimelineEventEffect resolved)
        {
            resolved = default(TimelineEventEffect);

            CatalogTuning catalog = tuning.Catalog;
            EffectsTuning effects = tuning.Effects;

            string id = authored.EffectId ?? "";
            EffectScope scope = authored.Scope;

            EffectCap cap;
            if (id.Length == 0 || !effects.TryGetEffect(id, out cap))
            {
                // The palette is a closed registry (non-negotiable #5 / §7). An id that is not in it
                // does not exist, so it degrades to the scope's terminal fallback rather than being
                // invented or silently dropped.
                string substitute = scope == EffectScope.District
                    ? effects.DefaultFallbackDistrictEffectId
                    : effects.DefaultFallbackCityEffectId;

                warnings.Add("event " + eventId + ": effect '" + id + "' is not in the palette registry; " +
                             "substituted terminal fallback '" + substitute + "'.");

                if (string.IsNullOrEmpty(substitute)) return false;

                id = substitute;
                if (!effects.TryGetEffect(id, out cap)) cap = effects.CapFor(id, scope);
            }

            // The registry owns the scope. A catalog entry that disagrees with it is corrected here,
            // because an effect applied at the wrong scope is a silent gameplay bug.
            scope = cap.Scope;

            double magnitude = ScaleForSeverity(authored.Magnitude, severity, catalog);
            magnitude = ClampAbs(magnitude, catalog.EffectMagnitudeGlobalCap);
            magnitude = cap.ClampMagnitude(magnitude);
            magnitude = ClampAbs(magnitude, effects.GlobalMagnitudeCap);

            int months = ClampMonths(authored.DurationMonths, catalog.EffectDurationCapMonths);
            months = cap.ClampDuration(months);
            months = ClampMonths(months, effects.GlobalDurationCapMonths);

            double floor = effects.MinEffectiveMagnitude < 0.0 ? 0.0 : effects.MinEffectiveMagnitude;
            if (Math.Abs(magnitude) < floor) return false;

            string? districtId = null;
            if (scope == EffectScope.District)
            {
                districtId = PickDistrict(saveGuid, seedDate, eventId, effectIndex, sortedDistrictIds);
                if (districtId == null)
                {
                    warnings.Add("event " + eventId + ": district-scoped effect '" + id +
                                 "' dropped — the city has no districts to target.");
                    return false;
                }
            }

            resolved = new TimelineEventEffect(id, scope, magnitude, months, districtId);
            return true;
        }

        /// <summary>
        /// Picks the district a district-scoped effect lands on. Catalog entries never name one — real
        /// history does not know the player's district names — so the scheduler picks, deterministically.
        /// </summary>
        /// <remarks>
        /// The draw is a per-entity sub-stream keyed on the event id and the effect's authored index, so
        /// adding an effect to an event does not move any other effect's target, and adding a district
        /// changes only the modulus. See the packet report: this reuses <see cref="StreamNames.EventProcedural"/>
        /// under a "target:" entity prefix because there is no dedicated targeting stream constant and
        /// <c>Determinism/</c> is frozen for this pass.
        /// </remarks>
        private static string? PickDistrict(Guid saveGuid, SimDate seedDate, string eventId, int effectIndex,
                                            IReadOnlyList<string> sortedDistrictIds)
        {
            if (sortedDistrictIds == null || sortedDistrictIds.Count == 0) return null;
            if (sortedDistrictIds.Count == 1) return sortedDistrictIds[0];

            DeterministicRng rng = SeedStreams.RngFor(saveGuid, seedDate, StreamNames.EventProcedural,
                                                      "target:" + eventId + ":" + effectIndex.ToString(
                                                          System.Globalization.CultureInfo.InvariantCulture));

            return sortedDistrictIds[rng.NextInt(0, sortedDistrictIds.Count)];
        }
    }
}
