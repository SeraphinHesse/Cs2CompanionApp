using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Effects
{
    /// <summary>
    /// Turns what the engine <i>wants</i> into what the city will actually get: severity scaling,
    /// fallback degradation, cap clamping and stack limiting — all pure, all deterministic, no draws.
    ///
    /// <para>
    /// The engine may request any magnitude for any duration; nothing past this point can exceed a
    /// declared cap (non-negotiable #5). Nothing here is random and nothing here iterates a dictionary
    /// in a way that reaches the output, so the same requests always resolve the same way
    /// (non-negotiable #3).
    /// </para>
    /// </summary>
    public static class EffectResolver
    {
        /// <summary>Event severity is 1–5 (<c>TimelineEvent.Severity</c>); anything outside is clamped in.</summary>
        private const int MinSeverity = 1;
        private const int MaxSeverity = 5;

        /// <summary>
        /// Scales an authored magnitude by event severity:
        /// <c>magnitude * (1 + effects.severityMagnitudeScale * (severity - 1))</c>. The cap still
        /// applies afterwards — severity buys intensity up to the ceiling, never past it.
        /// </summary>
        public static double ScaleForSeverity(EffectsTuning tuning, double magnitude, int severity)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            if (double.IsNaN(magnitude)) return 0.0;

            int s = severity < MinSeverity ? MinSeverity : (severity > MaxSeverity ? MaxSeverity : severity);
            double scale = tuning.SeverityMagnitudeScale;
            if (double.IsNaN(scale)) scale = 0.0;

            double factor = 1.0 + (scale * (s - MinSeverity));
            if (factor < 0.0) factor = 0.0;
            return magnitude * factor;
        }

        /// <summary>
        /// Resolves one request against the palette, degrading down the fallback chain when the
        /// requested effect is unknown or unavailable.
        /// </summary>
        /// <param name="availability">
        /// Optional. Agora.Mod passes a check that resolves the entry's modifier to a real game enum
        /// member; null means every registered effect is available.
        /// </param>
        public static EffectResolution Resolve(EffectPalette palette, EffectRequest request,
                                               EffectAvailabilityCheck? availability = null)
        {
            if (palette == null) throw new ArgumentNullException(nameof(palette));

            string requestedId = request.EffectId ?? "";

            if (!palette.Enabled) return EffectResolution.Suppress(requestedId);
            if (string.IsNullOrEmpty(requestedId)) return EffectResolution.Drop(requestedId, EffectDropReason.UnknownEffectId);
            if (double.IsNaN(request.Magnitude) || double.IsInfinity(request.Magnitude))
                return EffectResolution.Drop(requestedId, EffectDropReason.NotFinite);

            IReadOnlyList<string> chain = palette.FallbackChain(requestedId, request.Scope);
            EffectDropReason lastReason = EffectDropReason.UnknownEffectId;

            for (int depth = 0; depth < chain.Count; depth++)
            {
                string candidate = chain[depth];

                EffectCap cap;
                if (!palette.TryGetCap(candidate, out cap))
                {
                    lastReason = EffectDropReason.UnknownEffectId;
                    continue;
                }

                // District entries need a target. A city-scoped request can never satisfy one.
                if (cap.Scope == EffectScope.District && string.IsNullOrEmpty(request.DistrictId))
                {
                    lastReason = request.Scope == EffectScope.District
                        ? EffectDropReason.MissingDistrictId
                        : EffectDropReason.ScopeMismatch;
                    continue;
                }

                if (availability != null && !availability(candidate))
                {
                    lastReason = EffectDropReason.NoAvailableFallback;
                    continue;
                }

                double magnitude = palette.ClampMagnitude(cap, request.Magnitude);
                bool magnitudeClamped = magnitude != request.Magnitude;

                if (palette.IsBelowMinimum(magnitude))
                    return EffectResolution.Drop(requestedId, EffectDropReason.MagnitudeBelowMinimum);

                int duration = palette.ClampDuration(cap, request.DurationMonths);
                bool durationClamped = duration != request.DurationMonths;

                if (duration <= 0)
                    return EffectResolution.Drop(requestedId, EffectDropReason.ZeroDuration);

                // City-scoped effects carry no district: the contract says DistrictId is null for city scope.
                string? districtId = cap.Scope == EffectScope.District ? request.DistrictId : null;

                var applied = new EffectRequest(candidate, cap.Scope, magnitude, duration, districtId, request.SourceId);
                return EffectResolution.Apply(requestedId, applied, cap.Modifier, magnitudeClamped, durationClamped, depth);
            }

            return EffectResolution.Drop(requestedId, lastReason);
        }

        /// <summary>
        /// Resolves one catalog effect entry as fired by an event of the given severity.
        /// </summary>
        /// <param name="districtId">
        /// Target chosen by the scheduler. Catalog entries never name a district themselves; an entry
        /// that does name one wins over this parameter.
        /// </param>
        public static EffectResolution ResolveForEvent(EffectPalette palette, TimelineEventEffect effect,
                                                       int severity, string? sourceId,
                                                       string? districtId = null,
                                                       EffectAvailabilityCheck? availability = null)
        {
            if (palette == null) throw new ArgumentNullException(nameof(palette));

            string requestedId = effect.EffectId ?? "";
            if (!palette.Enabled) return EffectResolution.Suppress(requestedId);
            if (string.IsNullOrEmpty(requestedId))
                return EffectResolution.Drop(requestedId, EffectDropReason.UnknownEffectId);

            string? target = string.IsNullOrEmpty(effect.DistrictId) ? districtId : effect.DistrictId;

            // The registry decides scope; a catalog entry that disagrees is corrected here, not obeyed.
            EffectScope scope = effect.Scope;
            EffectCap cap;
            if (palette.TryGetCap(requestedId, out cap)) scope = cap.Scope;

            if (scope == EffectScope.District && string.IsNullOrEmpty(target))
                return EffectResolution.Drop(requestedId, EffectDropReason.MissingDistrictId);

            double magnitude = ScaleForSeverity(palette.Tuning, effect.Magnitude, severity);
            if (double.IsNaN(magnitude) || double.IsInfinity(magnitude))
                return EffectResolution.Drop(requestedId, EffectDropReason.NotFinite);

            var request = new EffectRequest(requestedId, scope, magnitude, effect.DurationMonths,
                                            scope == EffectScope.District ? target : null, sourceId);
            return Resolve(palette, request, availability);
        }

        /// <summary>
        /// Resolves a list of requests, preserving the caller's order. Callers must supply a
        /// deterministically ordered list — authored order for an event's effects, sorted order
        /// everywhere else — because the result order mirrors it exactly.
        /// </summary>
        public static IReadOnlyList<EffectResolution> ResolveAll(EffectPalette palette,
                                                                 IReadOnlyList<EffectRequest> requests,
                                                                 EffectAvailabilityCheck? availability = null)
        {
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            if (requests == null) throw new ArgumentNullException(nameof(requests));

            var results = new List<EffectResolution>(requests.Count);
            for (int i = 0; i < requests.Count; i++)
                results.Add(Resolve(palette, requests[i], availability));
            return results;
        }

        /// <summary>
        /// Applies the stacking policy across resolved effects that land on the same modifier and the
        /// same target, and returns a list in the same order and of the same length as the input —
        /// every input keeps its slot, either retained (possibly scaled down) or turned into a drop.
        ///
        /// <para>
        /// Whether the game combines several modifier sources additively or multiplicatively is
        /// unverified (Scout 0002, question 7), so this assumes the worst case: at most
        /// <c>effects.maxStackedPerModifier</c> effects survive per modifier and target, and in
        /// <c>sum</c> mode their magnitudes are scaled so the total absolute magnitude cannot exceed
        /// the tightest cap in the group. In <c>max</c> mode only the strongest survives.
        /// </para>
        /// </summary>
        public static IReadOnlyList<EffectResolution> Stack(EffectPalette palette,
                                                            IReadOnlyList<EffectResolution> resolved)
        {
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            if (resolved == null) throw new ArgumentNullException(nameof(resolved));

            var output = new List<EffectResolution>(resolved.Count);
            for (int i = 0; i < resolved.Count; i++) output.Add(resolved[i]);

            // Group by (scope, modifier, target). Groups are collected in a list in first-seen order and
            // the dictionary is only an index into it, so no dictionary iteration reaches the output.
            var groupIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var groups = new List<List<int>>();

            for (int i = 0; i < resolved.Count; i++)
            {
                EffectResolution r = resolved[i];
                if (!r.IsApplicable) continue;

                string key = GroupKey(r);
                int slot;
                if (!groupIndex.TryGetValue(key, out slot))
                {
                    slot = groups.Count;
                    groupIndex[key] = slot;
                    groups.Add(new List<int>(2));
                }
                groups[slot].Add(i);
            }

            bool maxMode = string.Equals(palette.Tuning.StackingMode, "max", StringComparison.Ordinal);
            int maxStacked = palette.Tuning.MaxStackedPerModifier;
            if (maxStacked < 1) maxStacked = 1;

            for (int g = 0; g < groups.Count; g++)
            {
                List<int> members = groups[g];
                if (members.Count > 1) SortGroup(members, resolved);

                int keep = maxMode ? 1 : (members.Count < maxStacked ? members.Count : maxStacked);

                for (int m = keep; m < members.Count; m++)
                {
                    int idx = members[m];
                    output[idx] = resolved[idx].AsDropped(EffectDropReason.StackLimit);
                }

                if (maxMode || keep <= 0) continue;

                // Sum mode: the group total must respect the tightest cap in the group.
                double total = 0.0;
                double groupCap = double.MaxValue;
                for (int m = 0; m < keep; m++)
                {
                    EffectResolution r = resolved[members[m]];
                    total += Math.Abs(r.Request.Magnitude);

                    EffectCap cap;
                    double capValue = palette.TryGetCap(r.Request.EffectId, out cap)
                        ? palette.EffectiveMagnitudeCap(cap)
                        : Math.Abs(palette.Tuning.GlobalMagnitudeCap);
                    if (capValue < groupCap) groupCap = capValue;
                }

                if (total <= groupCap || total <= 0.0) continue;

                double factor = groupCap / total;
                for (int m = 0; m < keep; m++)
                {
                    int idx = members[m];
                    EffectResolution r = resolved[idx];
                    double scaled = r.Request.Magnitude * factor;

                    output[idx] = palette.IsBelowMinimum(scaled)
                        ? r.AsDropped(EffectDropReason.MagnitudeBelowMinimum)
                        : r.WithMagnitude(scaled);
                }
            }

            return output;
        }

        private static string GroupKey(EffectResolution r)
        {
            string modifier = string.IsNullOrEmpty(r.Modifier) ? r.Request.EffectId : r.Modifier;
            return r.Request.Scope.ToString() + "|" + modifier + "|" + (r.Request.DistrictId ?? "");
        }

        /// <summary>
        /// Orders a group strongest first. The comparison is total — it ends on the input index — so it
        /// does not matter that <see cref="List{T}.Sort(Comparison{T})"/> is an unstable sort.
        /// </summary>
        private static void SortGroup(List<int> members, IReadOnlyList<EffectResolution> resolved)
        {
            members.Sort(delegate (int a, int b)
            {
                EffectResolution ra = resolved[a];
                EffectResolution rb = resolved[b];

                int byMagnitude = Math.Abs(rb.Request.Magnitude).CompareTo(Math.Abs(ra.Request.Magnitude));
                if (byMagnitude != 0) return byMagnitude;

                int byEffect = string.CompareOrdinal(ra.Request.EffectId, rb.Request.EffectId);
                if (byEffect != 0) return byEffect;

                int byDistrict = string.CompareOrdinal(ra.Request.DistrictId ?? "", rb.Request.DistrictId ?? "");
                if (byDistrict != 0) return byDistrict;

                int bySource = string.CompareOrdinal(ra.Request.SourceId ?? "", rb.Request.SourceId ?? "");
                if (bySource != 0) return bySource;

                return a.CompareTo(b);
            });
        }
    }
}
