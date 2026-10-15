using System;
using System.Collections.Generic;

namespace Game
{
    public enum MapPreparationMode { OfflineFull, OnlinePlayersOnly, ArcadePlayerOnly }

    public sealed class MapPreparationContext
    {
        public MapPreparationContext(
            string mapId,
            long mapInstanceId,
            MapContext scene,
            IReadOnlyList<MapSpawnDefinition> spawns)
            : this(mapId, mapInstanceId, scene, spawns, null, null)
        {
        }

        public MapPreparationContext(
            string mapId,
            long mapInstanceId,
            MapContext scene,
            IReadOnlyList<MapSpawnDefinition> spawns,
            ExpeditionSaveData expeditionSnapshot)
            : this(mapId, mapInstanceId, scene, spawns, expeditionSnapshot, null)
        {
        }

        public MapPreparationContext(
            string mapId,
            long mapInstanceId,
            MapContext scene,
            IReadOnlyList<MapSpawnDefinition> spawns,
            BaseSaveData baseSnapshot)
            : this(mapId, mapInstanceId, scene, spawns, null, baseSnapshot)
        {
        }

        private MapPreparationContext(
            string mapId,
            long mapInstanceId,
            MapContext scene,
            IReadOnlyList<MapSpawnDefinition> spawns,
            ExpeditionSaveData expeditionSnapshot,
            BaseSaveData baseSnapshot)
        {
            if (string.IsNullOrWhiteSpace(mapId))
            {
                throw new ArgumentException("MapId 不能为空", nameof(mapId));
            }

            if (mapInstanceId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(mapInstanceId), "MapInstanceId 必须大于 0");
            }

            MapId = mapId;
            MapInstanceId = mapInstanceId;
            Scene = scene ?? throw new ArgumentNullException(nameof(scene));
            Spawns = spawns ?? throw new ArgumentNullException(nameof(spawns));
            ExpeditionSnapshot = expeditionSnapshot?.Clone();
            BaseSnapshot = baseSnapshot?.Clone();
            Mode = MapPreparationMode.OfflineFull;
        }

        /// <summary>创建街机地图上下文；正式控制器只准备表现资源，敌人、掉落和结算规则仍由街机运行态负责。</summary>
        public static MapPreparationContext CreateArcade(
            string mapId,
            long mapInstanceId,
            MapContext scene,
            IReadOnlyList<MapSpawnDefinition> spawns)
        {
            var context = new MapPreparationContext(mapId, mapInstanceId, scene, spawns);
            context.Mode = MapPreparationMode.ArcadePlayerOnly;
            return context;
        }

        public MapPreparationContext(string mapId,long mapInstanceId,MapContext scene,IReadOnlyList<MapSpawnDefinition> spawns,OnlineWorldSnapshot onlineWorld)
        {
            if(string.IsNullOrWhiteSpace(mapId))throw new ArgumentException("MapId 不能为空",nameof(mapId));
            if(mapInstanceId<=0)throw new ArgumentOutOfRangeException(nameof(mapInstanceId));
            MapId=mapId;MapInstanceId=mapInstanceId;Scene=scene??throw new ArgumentNullException(nameof(scene));Spawns=spawns??throw new ArgumentNullException(nameof(spawns));OnlineWorld=onlineWorld??throw new ArgumentNullException(nameof(onlineWorld));Mode=MapPreparationMode.OnlinePlayersOnly;
        }

        public string MapId { get; }
        public long MapInstanceId { get; }
        public MapContext Scene { get; }
        public IReadOnlyList<MapSpawnDefinition> Spawns { get; }
        public ExpeditionSaveData ExpeditionSnapshot { get; }
        public BaseSaveData BaseSnapshot { get; }
        public MapPreparationMode Mode { get; private set; }
        /// <summary>只属于隐藏候选的清单与点目录，活动目录随成功句柄发布。</summary>
        public ValleyLayoutSaveData ValleyLayout { get; internal set; }
        public IReadOnlyList<CivilisationSearchPointDefinition> CivilisationPoints { get; internal set; }
        public OnlineWorldSnapshot OnlineWorld { get; }

        // 搜索容器在候选阶段只保存在这里；所有控制器均准备成功后才写入活动 Model。
        internal List<SearchContainerRecord> SearchContainerCandidates { get; } = new List<SearchContainerRecord>();
    }
}
