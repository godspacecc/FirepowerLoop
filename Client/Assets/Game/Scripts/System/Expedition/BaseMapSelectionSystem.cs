using Cysharp.Threading.Tasks;
using QFramework;

namespace Game
{
    public enum MapSelectionResultCode
    {
        Success,
        UnknownMap,
        NotExpeditionMap,
        UnlockCostInsufficient,
        EnterFailed
    }

    public readonly struct MapSelectionResult
    {
        private MapSelectionResult(
            bool success,
            bool unlockedNow,
            bool enteredMap,
            MapSelectionResultCode code,
            string error)
        {
            Success = success;
            UnlockedNow = unlockedNow;
            EnteredMap = enteredMap;
            Code = code;
            Error = error ?? string.Empty;
        }

        public bool Success { get; }
        public bool UnlockedNow { get; }
        public bool EnteredMap { get; }
        public MapSelectionResultCode Code { get; }
        public string Error { get; }

        public static MapSelectionResult UnlockOnly()
        {
            return new MapSelectionResult(true, true, false, MapSelectionResultCode.Success, string.Empty);
        }

        public static MapSelectionResult Entered()
        {
            return new MapSelectionResult(true, false, true, MapSelectionResultCode.Success, string.Empty);
        }

        public static MapSelectionResult Fail(MapSelectionResultCode code, string error)
        {
            return new MapSelectionResult(false, false, false, code, error);
        }
    }

    public sealed class BaseMapSelectionSystem : AbstractSystem
    {
        protected override void OnInit()
        {
            this.GetSystem<SaveSystem>()
                .RegisterSection(this.GetModel<BaseMapProgressModel>(), out _);
        }

        public bool IsMapUnlocked(string mapId)
        {
            return this.GetModel<BaseMapProgressModel>().IsMapUnlocked(mapId);
        }

        public async UniTask<ExpeditionFlowResult> EnterMapAsync(string mapId)
        {
            var result = await InteractWithMapAsync(mapId);
            return result.Success ? ExpeditionFlowResult.Ok() : ExpeditionFlowResult.Fail(result.Error);
        }

        public async UniTask<MapSelectionResult> InteractWithMapAsync(string mapId)
        {
            if (!MapCatalog.TryGet(mapId, out var definition))
            {
                return MapSelectionResult.Fail(MapSelectionResultCode.UnknownMap, $"未知地图：{mapId}");
            }

            if (definition.Kind != MapKind.Survival && definition.Kind != MapKind.Task)
            {
                return MapSelectionResult.Fail(MapSelectionResultCode.NotExpeditionMap, $"地图不是远征地图：{mapId}");
            }

            var progress = this.GetModel<BaseMapProgressModel>();
            if (!progress.IsMapUnlocked(definition.MapId))
            {
                if (!this.GetSystem<InventorySystem>()
                        .TrySpendWarehouseItem(definition.UnlockCostItemId, definition.UnlockCostAmount))
                {
                    return MapSelectionResult.Fail(
                        MapSelectionResultCode.UnlockCostInsufficient,
                        $"地图未解锁，仓库物品不足：{definition.MapId}");
                }

                progress.UnlockMap(definition.MapId);
                this.GetSystem<SaveSystem>().MarkDirty("map unlocked");
                return MapSelectionResult.UnlockOnly();
            }

            var enter = await this.GetSystem<ExpeditionFlowSystem>().EnterExpeditionAsync(mapId);
            return enter.Success
                ? MapSelectionResult.Entered()
                : MapSelectionResult.Fail(MapSelectionResultCode.EnterFailed, enter.Error);
        }

        public void UnlockMapForTests(string mapId)
        {
            this.GetModel<BaseMapProgressModel>().UnlockMap(mapId);
        }

        public void ResetProgressForTests()
        {
            this.GetModel<BaseMapProgressModel>().ResetProgress();
        }
    }
}
