using System.Collections.Generic;
using System.Linq;

namespace Game
{
    public enum MapKind
    {
        Base,
        Survival,
        Task,
        Arcade
    }

    public sealed class MapDefinition
    {
        public MapDefinition(
            string mapId,
            MapKind kind = MapKind.Base,
            string displayNameKey = null,
            float survivalTargetGameMinutes = 0f,
            int taskTargetKills = 0,
            int unlockCostItemId = 0,
            int unlockCostAmount = 0,
            bool unlockedByDefault = true,
            int displayOrder = 0)
        {
            MapId = mapId;
            Kind = kind;
            DisplayNameKey = displayNameKey;
            SurvivalTargetGameMinutes = survivalTargetGameMinutes;
            TaskTargetKills = taskTargetKills;
            UnlockCostItemId = unlockCostItemId;
            UnlockCostAmount = unlockCostAmount;
            UnlockedByDefault = unlockedByDefault;
            DisplayOrder = displayOrder;
        }

        public string MapId { get; }
        public MapKind Kind { get; }
        public string DisplayNameKey { get; }
        public float SurvivalTargetGameMinutes { get; }
        public int TaskTargetKills { get; }
        public int UnlockCostItemId { get; }
        public int UnlockCostAmount { get; }
        public bool UnlockedByDefault { get; }
        public int DisplayOrder { get; }
    }

    /// <summary>定义地图选择与规则元数据；场景地址由运行时配置提供，不在此维护。</summary>
    public static class MapCatalog
    {
        private static readonly Dictionary<string, MapDefinition> Definitions =
            new Dictionary<string, MapDefinition>
            {
                [MapIds.CivilisationValley] = new MapDefinition(MapIds.CivilisationValley, MapKind.Task, "map.civilisation_valley.name", displayOrder: 4),
                [MapIds.Base] =
                    new MapDefinition(MapIds.Base, MapKind.Base, "map.base.name"),
                [MapIds.SurvivalSample] =
                    new MapDefinition(
                        MapIds.SurvivalSample,
                        MapKind.Survival,
                        "map.survival_sample.name",
                        PlayerConditionRules.GameMinutesPerDay,
                        displayOrder: 1),
                [MapIds.MissionSample] =
                    new MapDefinition(
                        MapIds.MissionSample,
                        MapKind.Task,
                        "map.mission_sample.name",
                        taskTargetKills: 2,
                        unlockCostItemId: ItemIds.MetalParts,
                        unlockCostAmount: 1,
                        unlockedByDefault: false,
                        displayOrder: 2),
                [MapIds.Arcade] =
                    new MapDefinition(MapIds.Arcade, MapKind.Arcade, "map.arcade.name"),
                // 保存和道具继续使用稳定地图 ID，场景地址由 Luban 指向 768 米局部地图。
                [MapIds.HeJiaVillage] =
                    new MapDefinition(
                        MapIds.HeJiaVillage,
                        MapKind.Task,
                        "map.hejia_village.name",
                        taskTargetKills: 0,
                        unlockCostItemId: 0,
                        unlockCostAmount: 0,
                        unlockedByDefault: true,
                        displayOrder: 3),
            };

        public static bool TryGet(string mapId, out MapDefinition definition)
        {
            return Definitions.TryGetValue(mapId, out definition);
        }

        public static IReadOnlyList<MapDefinition> GetExpeditionMaps()
        {
            return Definitions.Values
                .Where(item => item.Kind == MapKind.Survival || item.Kind == MapKind.Task)
                .OrderBy(item => item.DisplayOrder)
                .ThenBy(item => item.MapId, System.StringComparer.Ordinal)
                .ToList();
        }
    }
}
