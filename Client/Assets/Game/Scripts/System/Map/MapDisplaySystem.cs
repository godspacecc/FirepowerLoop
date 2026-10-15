using System;
using System.Collections.Generic;
using System.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>从随身地图道具解析地图展示请求和标记快照；不拥有地图配置、背包或场景状态。</summary>
    public sealed class MapDisplaySystem : AbstractSystem
    {
        protected override void OnInit() { }

        /// <summary>仅为当前随身携带且配置有效的地图道具建立请求；多个道具可共享同一地图 ID。</summary>
        public bool TryResolveCarriedItem(int itemId, out MapOpenRequest request,
            out MapOpenFailureCode code, out string error)
        {
            request = default;
            if (this.GetSystem<InventorySystem>().GetCarriedItemAmount(itemId) <= 0)
                return Fail(MapOpenFailureCode.NotCarried, "地图道具不在背包或口袋中", out code, out error);

            var provider = this.GetUtility<IRuntimeConfigProvider>();
            if (!provider.TryGetItem(itemId, out var item, out error) || item.ItemType != global::ItemType.Tool ||
                item.ActionKind != global::ItemActionKind.OpenMap || string.IsNullOrWhiteSpace(item.ActionTarget))
                return Fail(MapOpenFailureCode.InvalidItemAction, error ?? $"TbItem[{itemId}].ActionKind 无效", out code, out error);

            if (!provider.TryGetMapDisplay(item.ActionTarget, out _, out error))
                return Fail(MapOpenFailureCode.DisplayUnavailable, error, out code, out error);

            request = new MapOpenRequest(itemId, item.ActionTarget);
            code = MapOpenFailureCode.None;
            error = null;
            return true;
        }

        public bool TryResolveCurrentMap(out MapOpenRequest request, out MapOpenFailureCode code, out string error)
        {
            request = default;
            var map = this.GetModel<MapSceneModel>();
            if (map.Status.Value != MapLoadStatus.Loaded || string.IsNullOrWhiteSpace(map.CurrentMapId))
                return Fail(MapOpenFailureCode.NoCurrentMap, "当前地图不可用", out code, out error);

            var provider = this.GetUtility<IRuntimeConfigProvider>();
            if (!provider.TryGetItems(out var items, out error))
                return Fail(MapOpenFailureCode.DisplayUnavailable, error, out code, out error);

            foreach (var item in items.OrderBy(value => value.Id))
            {
                if (item.ActionKind == global::ItemActionKind.OpenMap &&
                    string.Equals(item.ActionTarget, map.CurrentMapId, StringComparison.Ordinal) &&
                    this.GetSystem<InventorySystem>().GetCarriedItemAmount(item.Id) > 0)
                    return TryResolveCarriedItem(item.Id, out request, out code, out error);
            }
            return Fail(MapOpenFailureCode.NoCurrentMapItem, "背包和口袋没有当前区域地图", out code, out error);
        }

        public bool IsStillCarried(MapOpenRequest request)
        {
            return request.MapItemId > 0 && !string.IsNullOrWhiteSpace(request.MapId) &&
                   this.GetSystem<InventorySystem>().GetCarriedItemAmount(request.MapItemId) > 0 &&
                   this.GetUtility<IRuntimeConfigProvider>().TryGetItem(request.MapItemId, out var item, out _) &&
                   item.ActionKind == global::ItemActionKind.OpenMap &&
                   string.Equals(item.ActionTarget, request.MapId, StringComparison.Ordinal);
        }

        public bool TryBuildSnapshot(MapOpenRequest request, out MapDisplaySnapshot snapshot,
            out MapOpenFailureCode code, out string error)
        {
            snapshot = null;
            if (!IsStillCarried(request))
                return Fail(MapOpenFailureCode.NotCarried, "地图道具不再随身携带", out code, out error);
            var provider = this.GetUtility<IRuntimeConfigProvider>();
            if (!provider.TryGetMapDisplay(request.MapId, out var display, out error))
                return Fail(MapOpenFailureCode.DisplayUnavailable, error, out code, out error);

            var current = this.GetModel<MapSceneModel>();
            var isCurrent = current.Status.Value == MapLoadStatus.Loaded &&
                            string.Equals(current.CurrentMapId, request.MapId, StringComparison.Ordinal);
            var markers = new List<MapExtractionMarkerSnapshot>();
            MapPlayerMarkerSnapshot? player = null;
            if (isCurrent)
            {
                var instanceId = this.GetSystem<MapSceneSystem>()?.CurrentMapInstanceId ?? 0;
                if (instanceId > 0)
                {
                    var units = this.GetModel<UnitModel>().Units.Values.ToArray();
                    var local = units.FirstOrDefault(value => value.UnitId == PlayerModel.LocalPlayerUnitId &&
                        value.MapInstanceId == instanceId && value.UnitType == UnitType.Player && value.IsActive);
                    if (local.UnitId > 0)
                        player = new MapPlayerMarkerSnapshot(local.Position, local.Rotation.eulerAngles.y);
                    var extractions = this.GetModel<ExtractionModel>();
                    foreach (var unit in units.Where(value => value.MapInstanceId == instanceId && value.IsActive &&
                                 value.UnitType == UnitType.ExtractionPoint))
                    {
                        if (extractions.TryGet(unit.UnitId, out var state) && state.MapInstanceId == instanceId && state.Available &&
                            ((current.CurrentHandle as GeneratedValleyContentHandle)?.Layout.GenerationVersion != 3 ||
                             this.GetModel<MapExplorationModel>().IsExplored(display,unit.Position)))
                            markers.Add(new MapExtractionMarkerSnapshot(
                                string.IsNullOrWhiteSpace(unit.ConfigId) ? unit.UnitId.ToString() : unit.ConfigId,
                                unit.Position, true));
                    }
                }
            }
            else
            {
                if (!provider.TryGetMapSpawns(request.MapId, out var spawns, out error))
                    return Fail(MapOpenFailureCode.DisplayUnavailable, error, out code, out error);
                markers.AddRange(spawns.Where(value => value.ObjectType == global::MapSpawnObjectType.Extraction && request.MapId!=MapIds.CivilisationValley)
                    .Select(value => new MapExtractionMarkerSnapshot(value.Id, value.LocalPosition, false)));
            }

            snapshot = new MapDisplaySnapshot(request, display, player,
                markers.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray());
            code = MapOpenFailureCode.None;
            error = null;
            return true;
        }

        private static bool Fail(MapOpenFailureCode value, string message, out MapOpenFailureCode code, out string error)
        { code = value; error = message ?? string.Empty; return false; }
    }
}
