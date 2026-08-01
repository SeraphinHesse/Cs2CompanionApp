using System.Collections.Generic;
using Agora.Core.Contracts;
using Game.Areas;
using Game.Common;
using Game.Tools;
using Game.UI;
using Unity.Collections;
using Unity.Entities;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// One district the sensors know about: its ECS entity, the stable id the engine uses, and the
    /// player's name for it.
    /// </summary>
    public readonly struct DistrictEntry
    {
        public Entity Entity { get; }

        /// <summary>Stable id from <see cref="DistrictIdentityMap"/>. Also the seed-stream entity id.</summary>
        public string Id { get; }

        /// <summary>Player-assigned name, or the id when the district has never been renamed.</summary>
        public string Name { get; }

        public DistrictEntry(Entity entity, string id, string name)
        {
            Entity = entity;
            Id = id;
            Name = name;
        }
    }

    /// <summary>
    /// Geography sensor: enumerates the city's districts and gives each a stable identity.
    ///
    /// <para>
    /// Every other sensor keys its per-district results off this list, so it runs first and owns the
    /// ordering. The list is sorted by id before publication — ECS chunk order is not stable across
    /// loads, and letting it decide district order would make engine output depend on memory layout.
    /// </para>
    ///
    /// <para>
    /// Districts are real ECS entities carrying <c>Game.Areas.District</c> (Scout 0001 §2). Entities
    /// mid-edit (<c>Game.Tools.Temp</c>) or already removed (<c>Game.Common.Deleted</c>) are excluded:
    /// a district being dragged out under the player's cursor is not yet a place anyone lives.
    /// </para>
    /// </summary>
    public sealed partial class AgoraDistrictSensorSystem : AgoraSensorSystemBase
    {
        private EntityQuery _districtQuery;
        private NameSystem _nameSystem;

        private readonly DistrictIdentityMap _identity = new DistrictIdentityMap();
        private readonly List<DistrictEntry> _districts = new List<DistrictEntry>();

        /// <summary>The identity map, exposed so load reconciliation can pin ids from the sidecar.</summary>
        public DistrictIdentityMap Identity => _identity;

        /// <summary>
        /// The city's districts, ordered by <see cref="DistrictEntry.Id"/>. Empty before the first
        /// sample and on a map with no districts drawn — which is the normal state of a new city, and
        /// the reason nothing downstream may assume at least one district exists.
        /// </summary>
        public IReadOnlyList<DistrictEntry> Districts => _districts;

        protected override void CreateQueries()
        {
            _nameSystem = World.GetOrCreateSystemManaged<NameSystem>();

            _districtQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<District>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
        }

        public override void Invalidate()
        {
            base.Invalidate();
            _districts.Clear();
            _identity.Clear();
        }

        protected override void Sample(SimDate date)
        {
            _districts.Clear();

            NativeArray<Entity> entities = _districtQuery.ToEntityArray(Allocator.TempJob);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];
                    string id = _identity.Resolve(entity.Index);
                    _districts.Add(new DistrictEntry(entity, id, ReadName(entity, id)));
                }
            }
            finally
            {
                entities.Dispose();
            }

            _districts.Sort(CompareById);
        }

        private static int CompareById(DistrictEntry a, DistrictEntry b) =>
            string.CompareOrdinal(a.Id, b.Id);

        private string ReadName(Entity entity, string fallback)
        {
            // Only the custom name is used. NameSystem's other accessors return localisation keys or
            // rendered labels, both of which change with the player's language — and a district's
            // display name must not change what the engine sees, only what the dashboard shows.
            string custom;
            if (_nameSystem != null && _nameSystem.TryGetCustomName(entity, out custom) && !string.IsNullOrEmpty(custom))
            {
                return custom;
            }

            return fallback;
        }
    }
}
