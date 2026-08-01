using System;
using System.Collections.Generic;
using Agora.Mod.Effects;
using Agora.Mod.Sensors;
using Unity.Entities;

namespace Agora.Mod.Core
{
    /// <summary>
    /// Closes the district-identity seam between the sensor layer and the effect layer.
    ///
    /// <para>
    /// <b>This is not cosmetic.</b> <see cref="DistrictIdentityMap"/> names districts
    /// <c>d00000042</c>; <see cref="EntityIndexDistrictResolver"/>, the effect layer's documented
    /// last-resort fallback, names them <c>district-42</c>. Those two schemes never intersect, so
    /// with the fallback in place <i>every</i> district-scoped effect resolves to nothing and lands
    /// nowhere — silently, because a missing district is a warn-once, not an error. The effect
    /// packet flagged exactly this and asked the integrator to register a real resolver; this is it.
    /// </para>
    ///
    /// <para>
    /// The sensor layer owns district identity because it is what writes <c>DistrictSnapshot.Id</c>,
    /// so this resolver has no naming opinion of its own: it reads
    /// <see cref="AgoraDistrictSensorSystem.Districts"/> and indexes exactly the ids that went into
    /// the snapshot. Rebuilding on demand rather than caching a copy means a district drawn or
    /// deleted mid-session is picked up on the sensor's next daily sample without any invalidation
    /// protocol between the two layers.
    /// </para>
    /// </summary>
    public sealed class SensorDistrictResolver : IDistrictEntityResolver
    {
        private readonly AgoraDistrictSensorSystem _districts;

        private readonly Dictionary<string, Entity> _byId = new Dictionary<string, Entity>(StringComparer.Ordinal);
        private readonly List<string> _ids = new List<string>();

        /// <summary>Sensor list revision this index was built from — the entry count and the first id.</summary>
        private int _indexedCount = -1;
        private string _indexedFirstId;

        public SensorDistrictResolver(AgoraDistrictSensorSystem districts)
        {
            if (districts == null) throw new ArgumentNullException("districts");
            _districts = districts;
        }

        public IReadOnlyList<string> KnownDistrictIds
        {
            get
            {
                Refresh();
                return _ids;
            }
        }

        public bool TryResolve(string districtId, out Entity district)
        {
            district = Entity.Null;
            if (string.IsNullOrEmpty(districtId)) return false;

            Refresh();
            return _byId.TryGetValue(districtId, out district);
        }

        /// <summary>
        /// Rebuilds the index when the sensor's district list has visibly changed.
        /// </summary>
        /// <remarks>
        /// The change test is (count, first id). The sensor sorts its list by id, so those two move
        /// together for any add, remove or rename — and the cost of being wrong is one stale entry
        /// for one day, not a wrong district: <see cref="TryResolve"/> only ever returns an entity
        /// the sensor itself published under that id.
        /// </remarks>
        private void Refresh()
        {
            IReadOnlyList<DistrictEntry> entries = _districts.Districts;
            int count = entries != null ? entries.Count : 0;
            string firstId = count > 0 ? entries[0].Id : null;

            if (count == _indexedCount && string.Equals(firstId, _indexedFirstId, StringComparison.Ordinal))
            {
                return;
            }

            _byId.Clear();
            _ids.Clear();

            for (int i = 0; i < count; i++)
            {
                DistrictEntry entry = entries[i];
                if (string.IsNullOrEmpty(entry.Id)) continue;
                if (_byId.ContainsKey(entry.Id)) continue;

                _byId.Add(entry.Id, entry.Entity);
                _ids.Add(entry.Id);
            }

            // The sensor already sorts by id, but sorting again costs nothing and means this list
            // stays ordered even if that ever stops being true. KnownDistrictIds is documented as
            // sorted and is read into diagnostics.
            _ids.Sort(StringComparer.Ordinal);

            _indexedCount = count;
            _indexedFirstId = firstId;
        }
    }
}
