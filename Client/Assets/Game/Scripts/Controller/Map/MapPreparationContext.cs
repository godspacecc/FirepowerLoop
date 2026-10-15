using System;
using System.Collections.Generic;

namespace Game
{
    public enum MapPreparationMode
    {
        ArcadePlayerOnly
    }

    /// <summary>隐藏街机候选地图及其出生点契约，不承载存档或业务权威状态。</summary>
    public sealed class MapPreparationContext
    {
        private MapPreparationContext(string mapId, long mapInstanceId, MapContext scene, IReadOnlyList<MapSpawnDefinition> spawns)
        {
            if (string.IsNullOrWhiteSpace(mapId))
                throw new ArgumentException("MapId 不能为空", nameof(mapId));
            if (mapInstanceId <= 0)
                throw new ArgumentOutOfRangeException(nameof(mapInstanceId));
            MapId = mapId;
            MapInstanceId = mapInstanceId;
            Scene = scene ?? throw new ArgumentNullException(nameof(scene));
            Spawns = spawns ?? throw new ArgumentNullException(nameof(spawns));
        }

        /// <summary>为正式街机地图创建资源事务上下文。</summary>
        public static MapPreparationContext CreateArcade(string mapId, long mapInstanceId, MapContext scene, IReadOnlyList<MapSpawnDefinition> spawns) => new MapPreparationContext(mapId, mapInstanceId, scene, spawns);
        public string MapId { get; }
        public long MapInstanceId { get; }
        public MapContext Scene { get; }
        public IReadOnlyList<MapSpawnDefinition> Spawns { get; }
        public MapPreparationMode Mode => MapPreparationMode.ArcadePlayerOnly;
    }
}
