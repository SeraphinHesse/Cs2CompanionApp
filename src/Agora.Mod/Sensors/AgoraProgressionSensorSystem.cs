using System;
using Agora.Core.Contracts;
using Colossal.Entities;
using Game.City;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// The progression sensor: milestone level, experience, milestone progress, unlocked features and
    /// the per-resource tax rates.
    ///
    /// <para>
    /// City-only in its entirety, and not merely for want of a district query — a district has no
    /// milestone, and <c>TaxSystem</c> exposes no per-district, per-resource overload
    /// (<c>docs/scout/0004-city-statistics.md</c> §6, §8). This system therefore has no
    /// <c>Districts</c> property at all, which is a more honest statement than an always-empty one.
    /// </para>
    ///
    /// <para>
    /// <b>Read-only, and deliberately so.</b> Every system this file resolves has a matching writer
    /// sitting next to the reader it calls — <c>TaxSystem.Set…</c> and
    /// <c>MilestoneSystem.UnlockAllMilestones</c>. Calling one would be "targeting the player's
    /// authority" under <c>politicsmodplan.md</c> §7's FORBIDDEN list. A sensor reads.
    /// </para>
    /// </summary>
    public sealed partial class AgoraProgressionSensorSystem : AgoraSensorSystemBase
    {
        private EntityQuery _milestoneLevelQuery;
        private EntityQuery _featureQuery;

        private MilestoneSystem _milestones;
        private CitySystem _citySystem;
        private PrefabSystem _prefabs;
        private TaxSystem _taxes;
        private ResourceSystem _resources;

        private readonly CityReading _city = new CityReading();

        /// <summary>City progression and tax detail from the most recent sample.</summary>
        public CityReading City => _city;

        protected override void CreateQueries()
        {
            _milestones = World.GetOrCreateSystemManaged<MilestoneSystem>();
            _citySystem = World.GetOrCreateSystemManaged<CitySystem>();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _taxes = World.GetOrCreateSystemManaged<TaxSystem>();

            // Game.Prefabs.ResourceSystem, not a Game.Simulation type: it owns the resource-prefab
            // table that turns a Resource flag into the prefab entity carrying TaxableResourceData.
            _resources = World.GetOrCreateSystemManaged<ResourceSystem>();

            _milestoneLevelQuery = GetEntityQuery(ComponentType.ReadOnly<MilestoneLevel>());

            // Features only, without Locked in the query. Locked is enableable, so naming it here
            // would match exactly the entities that are *still locked* — which is how the game's own
            // FeatureUISystem.cs:30 builds its "lockedFeatures" binding. The unlocked set is the
            // complement, and it is taken per entity below.
            _featureQuery = GetEntityQuery(
                ComponentType.ReadOnly<FeatureData>(),
                ComponentType.ReadOnly<PrefabData>());
        }

        public override void Invalidate()
        {
            base.Invalidate();

            // The world underneath these readings has been replaced. Clearing them here is what stops
            // one city's unlocks and tax sliders being served for the next.
            _city.Progression = null;
            _city.UnlockedFeatureIds.Clear();
            _city.IndustryTaxRates.Clear();
        }

        protected override void Sample(SimDate date)
        {
            SampleProgression();
            SampleUnlockedFeatures();
            SampleIndustryTaxRates();
        }

        private void SampleProgression()
        {
            // The game does not assume the MilestoneLevel singleton exists — MilestoneUISystem.cs:376
            // checks the query first, and an unguarded GetSingleton throws, which the base class would
            // swallow into "sampling failed" and leave this whole family blind.
            if (_milestoneLevelQuery.IsEmptyIgnoreFilter)
            {
                _city.Progression = null;
                return;
            }

            int milestone = _milestoneLevelQuery.GetSingleton<MilestoneLevel>().m_AchievedMilestone;

            // "City level" and "milestone" are one number in CS2; there is no second counter to read.
            //
            // Experience is the lifetime total, CitySystem.XP — deliberately not
            // MilestoneSystem.currentXP, which is XP *since the last milestone*
            // (MilestoneSystem.cs:90 computes it as XP minus the last milestone's requirement) and so
            // falls back toward zero every time the city achieves one. This scalar is recorded into
            // MetricHistory, and a counter that resets would hand wave 3 a large negative delta at the
            // precise moment the city succeeded. The lifetime accumulator is monotonic and cannot.
            //
            // MilestoneProgress keeps the within-milestone position, which is where that information
            // honestly belongs.
            _city.Progression = new ProgressionState(
                milestone,
                _citySystem.XP,
                Fraction(_milestones.progress));
        }

        /// <summary>
        /// Sanitises <c>MilestoneSystem.progress</c>, which is <c>currentXP / requiredXP</c> with no
        /// guard of its own: <c>requiredXP</c> reaches zero at the top of the milestone track, and the
        /// division then yields infinity or NaN rather than a share. A non-finite value here would
        /// serialise into <c>snapshot.json</c> as invalid JSON, taking the LLM path down with it.
        /// </summary>
        /// <remarks>
        /// The three non-finite branches are three different facts and must not be collapsed back
        /// into one.
        /// <para>
        /// <b>+∞ means the track is complete, and that is the non-obvious half.</b>
        /// <c>MilestoneSystem.OnUpdate</c> refreshes <c>m_LastRequired</c> from the achieved milestone
        /// every tick, but assigns <c>m_NextRequired</c> only inside
        /// <c>if (TryGetMilestone(achieved + 1, …))</c> (<c>MilestoneSystem.cs:79-84</c>). Once the
        /// final milestone is achieved that lookup fails, <c>m_NextRequired</c> is left stale at the
        /// final milestone's own requirement, and <c>requiredXP</c> (<c>:43</c>) becomes exactly zero
        /// — the game's way of saying there is no next milestone. Meanwhile <c>m_Progress</c>
        /// (<c>:90</c>) stays positive and keeps growing, so the division is positive-over-zero and
        /// the result is <c>+∞</c> permanently, from that day on. Reporting <c>0.0</c> for it would
        /// claim a city that has finished the entire track has made no progress at all, and would
        /// hand <c>MetricHistory</c> a one-day fall of ~1.0 — the largest negative delta a
        /// <c>[0,1]</c> metric can carry — at the instant of the city's biggest success. Complete is
        /// <c>1.0</c>.
        /// </para>
        /// <para>
        /// <b>NaN genuinely is unknown.</b> Before the tutorial flag clears, <c>OnUpdate</c> returns
        /// early and both operands are still zero, so the division is 0/0. Nothing has been measured
        /// yet and <c>0.0</c> is the honest answer.
        /// </para>
        /// </remarks>
        private static double Fraction(float progress)
        {
            if (float.IsNaN(progress)) return 0.0;              // 0/0: nothing measured yet
            if (float.IsPositiveInfinity(progress)) return 1.0; // requiredXP == 0: track complete
            if (float.IsNegativeInfinity(progress)) return 0.0;
            if (progress < 0f) return 0.0;
            if (progress > 1f) return 1.0;
            return progress;
        }

        private void SampleUnlockedFeatures()
        {
            _city.UnlockedFeatureIds.Clear();

            NativeArray<Entity> features = _featureQuery.ToEntityArray(Allocator.TempJob);
            try
            {
                for (int i = 0; i < features.Length; i++)
                {
                    Entity feature = features[i];

                    // Unlocking disables the marker, it does not remove it — UnlockSystem.cs:208 calls
                    // SetComponentEnabled<Locked>(entity, false). HasComponent would therefore answer
                    // "was this ever lockable", and report the whole feature list as permanently
                    // locked. HasEnabledComponent is the form StatisticsUISystem.cs:397 uses.
                    if (EntityManager.HasEnabledComponent<Locked>(feature)) continue;

                    // FeatureData is an empty tag, so a feature carries no id of its own. The identity
                    // is the prefab, and the only portable id is its name (scout 0004 §6.3).
                    string name = _prefabs.GetPrefabName(feature);
                    if (!string.IsNullOrEmpty(name))
                    {
                        _city.UnlockedFeatureIds.Add(name);
                    }
                }
            }
            finally
            {
                features.Dispose();
            }

            // Chunk iteration order is not stable across runs. Assembly sorts too, but handing over
            // collection order would be relying on a sort this file cannot see.
            _city.UnlockedFeatureIds.Sort(StringComparer.Ordinal);
        }

        private void SampleIndustryTaxRates()
        {
            _city.IndustryTaxRates.Clear();

            // The three areas TaxSystem exposes a per-resource reader for. Residential is keyed by job
            // level rather than by resource and has no place in this list.
            CollectAreaTaxRates(TaxAreaType.Commercial, TaxArea.Commercial);
            CollectAreaTaxRates(TaxAreaType.Industrial, TaxArea.Industrial);
            CollectAreaTaxRates(TaxAreaType.Office, TaxArea.Office);

            _city.IndustryTaxRates.Sort(CompareTaxRates);
        }

        private void CollectAreaTaxRates(TaxAreaType areaType, TaxArea area)
        {
            // The game's own enumeration of what is taxable where — TaxSystem.cs:620-635. Industrial
            // and office share one slot in the internal rate array precisely because their taxable
            // resource sets are disjoint, and Contains is what separates them.
            ResourcePrefabs prefabs = _resources.GetPrefabs();
            ResourceIterator iterator = ResourceIterator.GetIterator();
            while (iterator.Next())
            {
                Entity prefab = prefabs[iterator.resource];

                TaxableResourceData taxable;
                if (!EntityManager.TryGetComponent(prefab, out taxable)) continue;
                if (!taxable.Contains(areaType)) continue;

                // Whole percentage points from the game, fractions in the contract — the same
                // convention AgoraEconomySensorSystem.cs:129-136 already applies to the area rates.
                double rate = ReadRate(areaType, iterator.resource) / 100.0;

                _city.IndustryTaxRates.Add(new ResourceTaxRate(
                    area,
                    EconomyUtils.GetResourceIndex(iterator.resource),
                    EconomyUtils.GetName(iterator.resource),
                    rate));
            }
        }

        private int ReadRate(TaxAreaType areaType, Resource resource)
        {
            switch (areaType)
            {
                case TaxAreaType.Commercial: return _taxes.GetCommercialTaxRate(resource);
                case TaxAreaType.Industrial: return _taxes.GetIndustrialTaxRate(resource);
                case TaxAreaType.Office: return _taxes.GetOfficeTaxRate(resource);
                default: return 0;
            }
        }

        /// <summary>
        /// Orders by <c>(Area, ResourceIndex)</c>. The resource index is the game's own key — a small
        /// dense integer — where the <c>Resource</c> flag is a bitfield running to <c>1 &lt;&lt; 40</c>
        /// and sorts meaninglessly.
        /// </summary>
        private static int CompareTaxRates(ResourceTaxRate left, ResourceTaxRate right)
        {
            int byArea = ((int)left.Area).CompareTo((int)right.Area);
            return byArea != 0 ? byArea : left.ResourceIndex.CompareTo(right.ResourceIndex);
        }
    }
}
