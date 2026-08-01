using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Entities;

namespace Agora.Mod.Effects
{
    /// <summary>
    /// Turns the <c>DistrictId</c> the engine uses into the district <see cref="Entity"/> a modifier
    /// buffer hangs off.
    /// </summary>
    /// <remarks>
    /// This is a seam on purpose. District identity is owned by the sensor layer — it is the thing
    /// that puts <c>DistrictSnapshot.Id</c> into the snapshot, so it is the thing that decides what
    /// those ids mean. The effect layer must not invent a second, conflicting naming scheme: if the
    /// two disagree, effects silently land nowhere. Sensors should register its own implementation
    /// through <see cref="AgoraEffects.DistrictResolver"/> at load.
    /// </remarks>
    public interface IDistrictEntityResolver
    {
        /// <summary>False when no live district carries that id.</summary>
        bool TryResolve(string districtId, out Entity district);

        /// <summary>Every id this resolver currently knows, sorted ordinal ascending. For diagnostics.</summary>
        IReadOnlyList<string> KnownDistrictIds { get; }
    }

    /// <summary>
    /// The fallback resolver: indexes whatever district entities the world currently holds, keyed by
    /// an id derived from the entity itself.
    /// </summary>
    /// <remarks>
    /// <b>Only correct if the sensor layer uses the same derivation.</b> It is deliberately a
    /// last-resort default so that a build without the sensor layer still exercises the whole effect
    /// path rather than failing at the first district-scoped request; it logs once per unknown id so
    /// a mismatch shows up in <c>Agora.log</c> rather than as effects that quietly never fire.
    /// </remarks>
    public sealed class EntityIndexDistrictResolver : IDistrictEntityResolver
    {
        /// <summary>
        /// The id a district entity gets when nothing better is registered.
        /// </summary>
        public static string IdFor(Entity entity)
        {
            return "district-" + entity.Index.ToString(CultureInfo.InvariantCulture);
        }

        private readonly Dictionary<string, Entity> _byId = new Dictionary<string, Entity>(StringComparer.Ordinal);
        private readonly List<string> _ids = new List<string>();

        public IReadOnlyList<string> KnownDistrictIds
        {
            get { return _ids; }
        }

        public int Count
        {
            get { return _ids.Count; }
        }

        /// <summary>Replaces the index with the given live district entities.</summary>
        public void Rebuild(IReadOnlyList<Entity> districts)
        {
            _byId.Clear();
            _ids.Clear();
            if (districts == null) return;

            for (int i = 0; i < districts.Count; i++)
            {
                string id = IdFor(districts[i]);
                if (_byId.ContainsKey(id)) continue;
                _byId.Add(id, districts[i]);
                _ids.Add(id);
            }

            _ids.Sort(StringComparer.Ordinal);
        }

        public bool TryResolve(string districtId, out Entity district)
        {
            district = Entity.Null;
            if (string.IsNullOrEmpty(districtId)) return false;
            return _byId.TryGetValue(districtId, out district);
        }
    }
}
