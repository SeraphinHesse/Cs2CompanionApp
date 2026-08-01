using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Engine.Effects;
using Agora.Core.Tuning;

namespace Agora.Mod.Effects
{
    /// <summary>
    /// The effect layer's composition root: one palette, one ledger, one sink, one dispatcher, and
    /// the district resolver the sensor layer is expected to override.
    ///
    /// <para>
    /// The engine tick reaches the city through <see cref="Dispatcher"/> and nothing else. Handing a
    /// request straight to <see cref="Sink"/>, or reaching past both into
    /// <see cref="AgoraEffectApplicationSystem"/>, skips severity scaling, fallback degradation and
    /// stack limiting — so don't.
    /// </para>
    /// </summary>
    public static class AgoraEffects
    {
        private static EffectPalette _palette;
        private static EffectLedger _ledger;
        private static AgoraEffectSink _sink;
        private static EffectDispatcher _dispatcher;
        private static EffectAvailabilityCheck _availability;
        private static IDistrictEntityResolver _districtResolver;

        /// <summary>
        /// The per-save switch (<c>AgoraSettings.EffectsEnabled</c>, non-negotiable #10). The
        /// persistence layer sets it when a save loads. False computes all the politics and applies
        /// nothing — the city is simply left alone.
        /// </summary>
        public static bool EffectsEnabled { get; set; } = true;

        public static bool IsInitialised
        {
            get { return _palette != null; }
        }

        public static EffectPalette Palette
        {
            get { return _palette; }
        }

        public static EffectLedger Ledger
        {
            get { return _ledger; }
        }

        public static AgoraEffectSink Sink
        {
            get { return _sink; }
        }

        public static EffectDispatcher Dispatcher
        {
            get { return _dispatcher; }
        }

        /// <summary>
        /// The availability check to pass into Core's resolver: true only when the effect is in the
        /// palette <i>and</i> its modifier resolves to a real enum member on this build.
        /// </summary>
        public static EffectAvailabilityCheck Availability
        {
            get { return _availability; }
        }

        /// <summary>
        /// How district ids map to entities. Defaults to <see cref="EntityIndexDistrictResolver"/>;
        /// the sensor layer should replace it with one that knows the canonical ids.
        /// </summary>
        public static IDistrictEntityResolver DistrictResolver
        {
            get { return _districtResolver; }
            set { _districtResolver = value; }
        }

        /// <summary>
        /// Builds the layer from a loaded tuning file. Safe to call again — it replaces the palette
        /// and drops every live effect, which is what a new save wants.
        /// </summary>
        /// <remarks>
        /// <b>Call this from exactly one place.</b> <see cref="AgoraRuntime.Attach"/> is that place: it
        /// is the only caller holding the tuning actually read from <c>data/engine_tuning.json</c>.
        /// Because this replaces the palette, ledger, sink and dispatcher wholesale, a second
        /// initialiser running afterwards silently discards everything the first one recorded — which
        /// is precisely what an earlier <c>EnsureInitialised</c> call in
        /// <c>AgoraEffectApplicationSystem.OnCreate</c> used to do, non-deterministically, depending on
        /// which system the world happened to create first. Consumers wait on
        /// <see cref="IsInitialised"/> instead of initialising a fallback of their own.
        /// </remarks>
        public static void Initialize(EngineTuning tuning, IClock clock)
        {
            if (tuning == null) throw new ArgumentNullException("tuning");
            if (clock == null) throw new ArgumentNullException("clock");

            _palette = EffectPalette.From(tuning);
            _ledger = new EffectLedger(_palette, ResolvesToAGameModifier);
            _sink = new AgoraEffectSink(_palette, _ledger, clock);
            _availability = ModifierRegistry.AvailabilityFor(_palette);
            _dispatcher = new EffectDispatcher(_palette, _sink, _availability);

            LogCoverage();
        }

        // EnsureInitialised(IClock) used to live here, initialising from EngineTuning.Default for
        // whichever system got created first. It was removed rather than left unused: its only caller
        // raced AgoraRuntime.Attach for ownership of the palette, and keeping a second initialiser
        // around is an invitation to reintroduce that race. See Initialize's remarks.

        /// <summary>Drops every live effect. The application system reverts the buffers separately.</summary>
        /// <remarks>
        /// Every field is released, not just the ledger. Leaving <see cref="_palette"/> set would keep
        /// <see cref="IsInitialised"/> reporting true after a detach, which is a lie with consequences:
        /// the application system gates on it, and its district-index refresh would early-return
        /// forever against a resolver that had already been cleared.
        /// </remarks>
        public static void Shutdown()
        {
            if (_ledger != null) _ledger.Clear();
            if (_sink != null) _sink.Reset();

            _palette = null;
            _ledger = null;
            _sink = null;
            _dispatcher = null;
            _availability = null;
            _districtResolver = null;
        }

        /// <summary>
        /// The one place the ledger's game-free arithmetic touches the enum tables. Kept as a method
        /// group rather than inlined so the ledger never names a <c>Game.*</c> type.
        /// </summary>
        private static bool ResolvesToAGameModifier(EffectScope scope, string modifierName)
        {
            ModifierBinding binding;
            return ModifierRegistry.TryResolve(scope, modifierName, out binding);
        }

        private static void LogCoverage()
        {
            IReadOnlyList<string> unmapped = ModifierRegistry.UnmappedPaletteEntries(_palette);

            AgoraMod.Log.Info("effects: palette " + _palette.Count + " entries ("
                + _palette.CityIds.Count + " city, " + _palette.DistrictIds.Count + " district), "
                + (_palette.Count - unmapped.Count) + " mapped to game modifiers.");

            if (unmapped.Count == 0) return;

            // Reported, dropped, never invented (section 7). A palette entry naming a modifier this
            // build cannot resolve is a data bug: fix engine_tuning.json, do not add a mapping here.
            AgoraMod.Log.Warn("effects: " + unmapped.Count
                + " palette entries name a modifier that does not exist on this build and will be dropped: "
                + string.Join(", ", ToArray(unmapped)));
        }

        private static string[] ToArray(IReadOnlyList<string> values)
        {
            var array = new string[values.Count];
            for (int i = 0; i < values.Count; i++) array[i] = values[i];
            return array;
        }
    }
}
