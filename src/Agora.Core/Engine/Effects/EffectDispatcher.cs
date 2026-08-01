using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Effects
{
    /// <summary>
    /// The one road from the engine to the city: resolve, stack, then hand the survivors to the
    /// <see cref="IEffectSink"/> in the caller's order.
    ///
    /// <para>
    /// It exists so no packet can reach a sink directly with an unclamped request. The sink clamps
    /// again on the far side — that is deliberate belt and braces, not duplication (non-negotiable #5).
    /// </para>
    /// </summary>
    public sealed class EffectDispatcher
    {
        private readonly EffectPalette _palette;
        private readonly IEffectSink _sink;
        private readonly EffectAvailabilityCheck? _availability;

        public EffectDispatcher(EffectPalette palette, IEffectSink sink, EffectAvailabilityCheck? availability = null)
        {
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            if (sink == null) throw new ArgumentNullException(nameof(sink));

            _palette = palette;
            _sink = sink;
            _availability = availability;
        }

        public EffectPalette Palette => _palette;

        /// <summary>
        /// Resolves and applies. Returns one resolution per input request, in input order, so the
        /// caller can log or surface exactly what happened to each.
        /// </summary>
        /// <param name="effectsEnabled">
        /// The per-save switch (<c>AgoraSettings.EffectsEnabled</c>). False, or a palette with
        /// <c>effects.enabled</c> false, computes everything and applies nothing — the politics still
        /// happen, the city is simply left alone.
        /// </param>
        public IReadOnlyList<EffectResolution> Dispatch(IReadOnlyList<EffectRequest> requests, bool effectsEnabled = true)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));

            if (!effectsEnabled || !_palette.Enabled)
            {
                var suppressed = new List<EffectResolution>(requests.Count);
                for (int i = 0; i < requests.Count; i++)
                    suppressed.Add(EffectResolution.Suppress(requests[i].EffectId));
                return suppressed;
            }

            IReadOnlyList<EffectResolution> resolved = EffectResolver.ResolveAll(_palette, requests, _availability);
            IReadOnlyList<EffectResolution> stacked = EffectResolver.Stack(_palette, resolved);

            for (int i = 0; i < stacked.Count; i++)
            {
                if (!stacked[i].IsApplicable) continue;
                _sink.Apply(stacked[i].Request);
            }

            return stacked;
        }

        /// <summary>
        /// Resolves and applies without a sink, for callers that only want to know what <i>would</i>
        /// happen — the dashboard preview and the headless harness both use this.
        /// </summary>
        public static IReadOnlyList<EffectResolution> Preview(EffectPalette palette,
                                                              IReadOnlyList<EffectRequest> requests,
                                                              EffectAvailabilityCheck? availability = null)
        {
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            if (requests == null) throw new ArgumentNullException(nameof(requests));

            return EffectResolver.Stack(palette, EffectResolver.ResolveAll(palette, requests, availability));
        }
    }
}
