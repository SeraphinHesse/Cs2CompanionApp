using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Mod.Time;
using Game;
using Game.Areas;
using Game.City;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Agora.Mod.Effects
{
    /// <summary>
    /// The only place in Agora that writes to the game. It takes the ledger's current aggregate and
    /// makes the <c>DistrictModifier</c> / <c>CityModifier</c> buffers reflect it — no more, no less.
    ///
    /// <para>
    /// <b>Modifiers only.</b> It never creates or edits a district, a zone, a building or terrain
    /// (non-negotiable #4). The only mutation it performs is assigning a <c>float2</c> into a modifier
    /// slot, and lengthening the modifier buffer to reach that slot, which is exactly what the game's
    /// own policy layer does.
    /// </para>
    ///
    /// <para>
    /// <b>Why re-apply, and why reconcile.</b> The game rebuilds these buffers from scratch:
    /// <c>CityModifierUpdateSystem.RefreshCityModifiers</c> and
    /// <c>DistrictModifierInitializeSystem.RefreshDistrictModifiers</c> both begin with
    /// <c>modifiers.Clear()</c> and re-derive every lane from the active policies, and the city one
    /// does so unconditionally every 256 simulation ticks. A write-once effect would be erased within
    /// a couple of in-game days. So this runs on a shorter interval and, each pass, works out the
    /// non-Agora baseline before composing our contribution back on top — which is what stops
    /// re-application from compounding into an uncapped drift.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Phase <see cref="SystemUpdatePhase.GameSimulation"/>: the same phase the game's own modifier
    /// refresh runs in, so a pass sees the buffers in their settled state for this frame rather than
    /// half-rebuilt. Mod systems are registered after the game's, so a rebuild and our re-apply in the
    /// same frame land in that order — but the reconciler does not depend on that being true.
    /// </remarks>
    public sealed partial class AgoraEffectApplicationSystem : GameSystemBase
    {
        /// <summary>
        /// Simulation frames between passes. Half of <c>CityModifierUpdateSystem</c>'s 256, so a
        /// wholesale rebuild is corrected within one of its cycles. Not a tuning coefficient — it is
        /// a property of the game's refresh cadence, and it belongs next to the reason for it.
        /// </summary>
        private const int UpdateIntervalFrames = 128;

        // AGORA-SEAM(persistence): the slot table below is Agora state and ideally round-trips through
        // the sidecar, because DistrictModifier is ISerializable and only the *city* buffer is rebuilt
        // on a timer — a district slot can come back from disk still carrying our last contribution.
        // Until the persistence packet saves and restores it, ModifierAggregate.IsCarriedOver is the
        // stand-in: on the first pass after a load the writer divides out what the ledger says we must
        // already have contributed, so the error is bounded by one month of decay rather than a full
        // double application. Do not paper over this with a "clear all modifiers on load" — that would
        // wipe the player's own policy modifiers too.
        private readonly Dictionary<SlotKey, SlotState> _slots = new Dictionary<SlotKey, SlotState>();
        private readonly List<SlotKey> _writtenThisPass = new List<SlotKey>();
        private readonly List<SlotKey> _stale = new List<SlotKey>();
        private readonly List<Entity> _districtEntities = new List<Entity>();
        private readonly EntityIndexDistrictResolver _fallbackResolver = new EntityIndexDistrictResolver();
        private readonly HashSet<string> _loggedMissingDistricts = new HashSet<string>(StringComparer.Ordinal);

        private EntityQuery _districtQuery;
        private Game.Simulation.CitySystem _citySystem;
        private AgoraTimeService _time;
        private bool _suspended;
        private bool _broken;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return UpdateIntervalFrames;
        }

        /// <summary>Modifier slots Agora is currently holding open. Diagnostics and the M5 gate.</summary>
        public int TrackedSlotCount
        {
            get { return _slots.Count; }
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            // Guarded so a throw cannot leave this system subscribed to GameManager.onGamePreload with
            // a freed state, which would make every subsequent load fail.
            try
            {
                _districtQuery = GetEntityQuery(
                    ComponentType.ReadOnly<District>(),
                    ComponentType.ReadWrite<DistrictModifier>(),
                    ComponentType.Exclude<Game.Common.Deleted>(),
                    ComponentType.Exclude<Game.Tools.Temp>());

                _citySystem = World.GetOrCreateSystemManaged<Game.Simulation.CitySystem>();
                _time = new AgoraTimeService(World);
            }
            catch (Exception ex)
            {
                _broken = true;
                Enabled = false;
                AgoraMod.Log.Error(ex, "effects: the application system could not initialise; no effect " +
                                       "will be applied to the city this session.");
            }

            // Deliberately NOT initialising AgoraEffects here. AgoraRuntime.Attach is the sole
            // initialiser (it is the only caller that has the tuning actually read off disk), and
            // Initialize replaces the palette, ledger, sink and dispatcher wholesale — so a second
            // initialiser racing it would silently discard whatever the first one had already
            // recorded. Until Attach runs, IsInitialised is false and OnUpdate idles.
        }

        protected override void OnDestroy()
        {
            // Leaving a modifier behind would poison a save Agora is no longer part of.
            TryRevertAll();
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            if (_broken) return;

            SimDate now;
            if (!_time.TryGetToday(out now)) return; // main menu: nothing loaded, nothing to apply

            // Before AgoraRuntime.Attach there is no palette and nothing has ever been applied, so
            // there is also nothing to revert — return outright rather than falling into the
            // suspend-and-revert path below, which is for the master toggle being switched off.
            if (!AgoraEffects.IsInitialised) return;

            if (!IsEnabled())
            {
                // Master toggle off mid-save: hand the city back exactly as we found it, then idle.
                if (!_suspended)
                {
                    TryRevertAll();
                    _suspended = true;
                }
                return;
            }

            _suspended = false;

            EffectLedger ledger = AgoraEffects.Ledger;
            if (ledger == null) return;

            ledger.PruneExpired(now);
            RefreshDistrictIndex();

            IReadOnlyList<ModifierAggregate> aggregates = ledger.Aggregate(now);
            double globalCap = Math.Abs(AgoraEffects.Palette.Tuning.GlobalMagnitudeCap);

            _writtenThisPass.Clear();

            for (int i = 0; i < aggregates.Count; i++)
            {
                ModifierAggregate aggregate = aggregates[i];

                ModifierBinding binding;
                if (!ModifierRegistry.TryResolve(aggregate.Scope, aggregate.Modifier, out binding))
                    continue; // reported at load by AgoraEffects.LogCoverage; never substituted here

                // Third and final cap. Core clamped, the ledger clamped, and this clamps at the sink
                // itself — the layer that actually touches the city (non-negotiable #5).
                ModifierDelta desired = binding.ToDelta(aggregate.Magnitude).Clamped(globalCap);

                if (aggregate.Scope == EffectScope.District)
                    WriteDistrict(aggregate.DistrictId, binding, desired, aggregate.IsCarriedOver);
                else
                    WriteCity(binding, desired, aggregate.IsCarriedOver);
            }

            ClearStaleSlots();
        }

        private bool IsEnabled()
        {
            var settings = AgoraMod.Settings;
            if (settings == null || !settings.Enabled) return false;
            if (!AgoraEffects.IsInitialised) return false;
            if (!AgoraEffects.EffectsEnabled) return false;
            return AgoraEffects.Palette.Enabled;
        }

        // --- Targets -----------------------------------------------------------------------------

        private void RefreshDistrictIndex()
        {
            IDistrictEntityResolver resolver = AgoraEffects.DistrictResolver;

            // The sensor resolver keeps its own index; only the fallback is ours to rebuild.
            if (resolver != null && !ReferenceEquals(resolver, _fallbackResolver)) return;

            // Null means Shutdown cleared it. Adopt the fallback rather than resolving nothing until
            // the next Attach happens to install a replacement.
            if (resolver == null) AgoraEffects.DistrictResolver = _fallbackResolver;

            _districtEntities.Clear();
            NativeArray<Entity> entities = _districtQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++) _districtEntities.Add(entities[i]);
            }
            finally
            {
                entities.Dispose();
            }

            _fallbackResolver.Rebuild(_districtEntities);
        }

        private bool TryResolveDistrict(string districtId, out Entity district)
        {
            district = Entity.Null;

            IDistrictEntityResolver resolver = AgoraEffects.DistrictResolver;
            if (resolver == null) return false;
            if (resolver.TryResolve(districtId, out district)) return true;

            if (_loggedMissingDistricts.Add(districtId ?? ""))
            {
                AgoraMod.Log.Warn("effects: no district entity for id '" + districtId
                    + "'. District-scoped effects for it will not be applied. Known ids: "
                    + Describe(resolver.KnownDistrictIds));
            }
            return false;
        }

        private static string Describe(IReadOnlyList<string> ids)
        {
            if (ids == null || ids.Count == 0) return "(none)";

            var builder = new System.Text.StringBuilder();
            int shown = ids.Count < 12 ? ids.Count : 12;
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) builder.Append(", ");
                builder.Append(ids[i]);
            }
            if (shown < ids.Count) builder.Append(", ... (").Append(ids.Count).Append(" total)");
            return builder.ToString();
        }

        // --- Writing -----------------------------------------------------------------------------

        private void WriteDistrict(string districtId, ModifierBinding binding, ModifierDelta desired, bool carriedOver)
        {
            Entity district;
            if (!TryResolveDistrict(districtId, out district)) return;
            if (!EntityManager.Exists(district) || !EntityManager.HasBuffer<DistrictModifier>(district)) return;

            DynamicBuffer<DistrictModifier> buffer = EntityManager.GetBuffer<DistrictModifier>(district, false);
            int index = binding.TypeIndex;
            while (buffer.Length <= index) buffer.Add(default(DistrictModifier));

            var key = new SlotKey(district, index);
            DistrictModifier slot = buffer[index];
            slot.m_Delta = NextValue(key, EffectScope.District, slot.m_Delta, desired, carriedOver);
            buffer[index] = slot;
        }

        private void WriteCity(ModifierBinding binding, ModifierDelta desired, bool carriedOver)
        {
            Entity city = _citySystem.City;
            if (city == Entity.Null) return;
            if (!EntityManager.Exists(city) || !EntityManager.HasBuffer<CityModifier>(city)) return;

            DynamicBuffer<CityModifier> buffer = EntityManager.GetBuffer<CityModifier>(city, false);
            int index = binding.TypeIndex;
            while (buffer.Length <= index) buffer.Add(default(CityModifier));

            var key = new SlotKey(city, index);
            CityModifier slot = buffer[index];
            slot.m_Delta = NextValue(key, EffectScope.City, slot.m_Delta, desired, carriedOver);
            buffer[index] = slot;
        }

        /// <summary>
        /// Works out the value to store, and remembers the baseline underneath it so the next pass can
        /// replace our contribution rather than pile another one on top.
        /// </summary>
        /// <param name="carriedOver">
        /// True when the ledger says Agora was already driving this slot in an earlier month. On an
        /// untracked slot that means the value came back from a save still containing our
        /// contribution, so it must be divided out rather than treated as a clean baseline.
        /// </param>
        private float2 NextValue(SlotKey key, EffectScope scope, float2 current, ModifierDelta desired, bool carriedOver)
        {
            SlotState state;
            bool tracked = _slots.TryGetValue(key, out state);

            // Bit-exact comparison against our own last write. Anything else — a policy toggle, the
            // game's 256-tick rebuild, another mod — means the slot was reset and `current` is already
            // the clean baseline.
            bool ours = tracked && current.x == state.WrittenX && current.y == state.WrittenY;

            var currentDelta = new ModifierDelta(current.x, current.y);
            ModifierDelta remembered = tracked ? state.Baseline : ModifierDelta.Zero;
            ModifierDelta previous = tracked ? state.Contribution : desired;

            ModifierDelta baseline = ModifierReconciler.BaselineFor(
                currentDelta, ours, remembered, !tracked && carriedOver, previous);

            ModifierDelta composed = ModifierDelta.Compose(baseline, desired).ToSinglePrecision();
            var value = new float2((float)composed.Absolute, (float)composed.Relative);

            if (desired.IsZero)
            {
                if (tracked) _slots.Remove(key);
                return value;
            }

            if (!tracked)
            {
                state = new SlotState();
                _slots.Add(key, state);
            }

            state.Scope = scope;
            state.Baseline = baseline;
            state.WrittenX = value.x;
            state.WrittenY = value.y;
            state.Contribution = desired;

            _writtenThisPass.Add(key);
            return value;
        }

        /// <summary>Zeroes every slot that had a contribution last pass but none this pass.</summary>
        private void ClearStaleSlots()
        {
            CollectStale(_writtenThisPass);
            for (int i = 0; i < _stale.Count; i++) Zero(_stale[i]);
        }

        private void CollectStale(List<SlotKey> keep)
        {
            _stale.Clear();
            foreach (KeyValuePair<SlotKey, SlotState> pair in _slots)
            {
                if (keep != null && Contains(keep, pair.Key)) continue;
                _stale.Add(pair.Key);
            }

            // The dictionary's enumeration order is not stable, and although writes to distinct slots
            // are independent, an ordered pass is one less thing to reason about when a log is read.
            _stale.Sort(CompareSlots);
        }

        private static bool Contains(List<SlotKey> keys, SlotKey key)
        {
            for (int i = 0; i < keys.Count; i++)
                if (keys[i].Equals(key)) return true;
            return false;
        }

        private static int CompareSlots(SlotKey a, SlotKey b)
        {
            int byIndex = a.Target.Index.CompareTo(b.Target.Index);
            if (byIndex != 0) return byIndex;

            int byVersion = a.Target.Version.CompareTo(b.Target.Version);
            if (byVersion != 0) return byVersion;

            return a.TypeIndex.CompareTo(b.TypeIndex);
        }

        private void Zero(SlotKey key)
        {
            SlotState state;
            if (!_slots.TryGetValue(key, out state)) return;

            if (!EntityManager.Exists(key.Target))
            {
                _slots.Remove(key); // district bulldozed: nothing left to hand back
                return;
            }

            if (state.Scope == EffectScope.District)
            {
                if (!EntityManager.HasBuffer<DistrictModifier>(key.Target)) { _slots.Remove(key); return; }

                DynamicBuffer<DistrictModifier> buffer = EntityManager.GetBuffer<DistrictModifier>(key.Target, false);
                if (key.TypeIndex >= buffer.Length) { _slots.Remove(key); return; }

                DistrictModifier slot = buffer[key.TypeIndex];
                slot.m_Delta = NextValue(key, EffectScope.District, slot.m_Delta, ModifierDelta.Zero, false);
                buffer[key.TypeIndex] = slot;
                return;
            }

            if (!EntityManager.HasBuffer<CityModifier>(key.Target)) { _slots.Remove(key); return; }

            DynamicBuffer<CityModifier> cityBuffer = EntityManager.GetBuffer<CityModifier>(key.Target, false);
            if (key.TypeIndex >= cityBuffer.Length) { _slots.Remove(key); return; }

            CityModifier citySlot = cityBuffer[key.TypeIndex];
            citySlot.m_Delta = NextValue(key, EffectScope.City, citySlot.m_Delta, ModifierDelta.Zero, false);
            cityBuffer[key.TypeIndex] = citySlot;
        }

        /// <summary>
        /// Removes every Agora contribution from the world. Used when the mod is switched off mid-save
        /// and on dispose: disabling Agora must leave a stock city, not a city with our last numbers
        /// baked into it.
        /// </summary>
        public void TryRevertAll()
        {
            if (_slots.Count == 0) return;

            try
            {
                CollectStale(null);
                for (int i = 0; i < _stale.Count; i++) Zero(_stale[i]);
                _slots.Clear();
                _writtenThisPass.Clear();
                AgoraMod.Log.Info("effects: reverted every modifier Agora was holding.");
            }
            catch (Exception ex)
            {
                // Teardown runs during world disposal, where the entity manager may already be gone.
                // Losing the revert is bad; throwing out of OnDestroy is worse.
                AgoraMod.Log.Warn("effects: could not revert modifiers cleanly: " + ex.Message);
                _slots.Clear();
            }
        }

        // --- Slot bookkeeping --------------------------------------------------------------------

        private readonly struct SlotKey : IEquatable<SlotKey>
        {
            public readonly Entity Target;
            public readonly int TypeIndex;

            public SlotKey(Entity target, int typeIndex)
            {
                Target = target;
                TypeIndex = typeIndex;
            }

            public bool Equals(SlotKey other)
            {
                return Target.Equals(other.Target) && TypeIndex == other.TypeIndex;
            }

            public override bool Equals(object obj)
            {
                return obj is SlotKey && Equals((SlotKey)obj);
            }

            public override int GetHashCode()
            {
                return (Target.GetHashCode() * 397) ^ TypeIndex;
            }
        }

        private sealed class SlotState
        {
            public EffectScope Scope;

            /// <summary>The exact <c>float</c> lanes we last stored, for the bit-exact ownership test.</summary>
            public float WrittenX;
            public float WrittenY;

            /// <summary>
            /// What the slot read underneath our contribution. Remembered rather than re-derived: the
            /// inverse of <see cref="ModifierDelta.Compose"/> is exact in real arithmetic but not in
            /// <c>float</c>, and this system runs hundreds of times a year.
            /// </summary>
            public ModifierDelta Baseline;

            /// <summary>What of the stored value was ours.</summary>
            public ModifierDelta Contribution;
        }
    }
}
