using System.Collections.Generic;
using Agora.Mod.Core;
using Colossal.UI.Binding;

namespace Agora.Mod.UiBindings
{
    /// <summary>
    /// Publishes <c>agora.districts</c>: the district list, the per-district detail and crosstab, and
    /// the city aggregates (<c>docs/contracts/ui_bindings.md</c> §4.4).
    /// </summary>
    /// <remarks>
    /// Detail and crosstab are <see cref="GetterMapBinding{K,V}"/> rather than city-wide arrays: a
    /// fifty-district city would otherwise push fifty full breakdowns across the bridge every month
    /// so the panel could render the one the player has open. The map fetches only subscribed keys.
    /// </remarks>
    public sealed partial class AgoraDistrictsUISystem : AgoraUISystemBase
    {
        private const string Group = "agora.districts";

        private ValueBinding<List<DistrictBriefPayload>> _list;
        private ValueBinding<List<CrosstabCellPayload>> _cityCrosstab;
        private ValueBinding<CityIndicesPayload> _cityIndices;

        private GetterMapBinding<string, DistrictDetailPayload> _detail;
        private GetterMapBinding<string, List<CrosstabCellPayload>> _crosstab;

        protected override void CreateBindings()
        {
            AddBinding(_list = new ValueBinding<List<DistrictBriefPayload>>(
                Group, "list", new List<DistrictBriefPayload>(), ListOf<DistrictBriefPayload>()));

            AddBinding(_cityCrosstab = new ValueBinding<List<CrosstabCellPayload>>(
                Group, "cityCrosstab", new List<CrosstabCellPayload>(), ListOf<CrosstabCellPayload>()));

            AddBinding(_cityIndices = new ValueBinding<CityIndicesPayload>(
                Group, "cityIndices", new CityIndicesPayload()));

            // AddBinding, not AddUpdateBinding: an update binding re-evaluates every subscribed key on
            // every UI tick, and because each payload is a freshly built object the comparer would
            // call it changed every time. Subscribing already fetches the value once; beyond that these
            // are refreshed from Publish, on the engine's cadence.
            AddBinding(_detail = new GetterMapBinding<string, DistrictDetailPayload>(
                Group, "detail", GetDetail));

            // Named argument: keyReader and keyWriter come first in the signature, and the value here
            // is a list, so it needs the same explicit writer a ValueBinding<List<T>> does.
            AddBinding(_crosstab = new GetterMapBinding<string, List<CrosstabCellPayload>>(
                Group, "crosstab", GetCrosstab, valueWriter: ListOf<CrosstabCellPayload>()));
        }

        /// <summary>
        /// One district's detail. An unknown key returns the empty payload rather than throwing — the
        /// player can delete a district while its panel is open, and a map binding that threw would
        /// take the interface down with it.
        /// </summary>
        private static DistrictDetailPayload GetDetail(string districtId) =>
            AgoraUiProjection.BuildDistrictDetail(AgoraRuntime.State, AgoraRuntime.LastSnapshot, districtId);

        private static List<CrosstabCellPayload> GetCrosstab(string districtId) =>
            AgoraUiProjection.BuildCrosstab(AgoraRuntime.State, districtId);

        protected override void Publish()
        {
            var state = AgoraRuntime.State;
            var snapshot = AgoraRuntime.LastSnapshot;

            _list.Update(AgoraUiProjection.BuildDistrictList(state, snapshot));
            _cityCrosstab.Update(AgoraUiProjection.BuildCrosstab(state, null));
            _cityIndices.Update(AgoraUiProjection.BuildCityIndices(state));

            // Refresh whatever detail the panel currently has open. UpdateAll only pushes keys that
            // are actually subscribed, so this costs nothing when no district is selected.
            _detail.UpdateAll();
            _crosstab.UpdateAll();
        }
    }
}
