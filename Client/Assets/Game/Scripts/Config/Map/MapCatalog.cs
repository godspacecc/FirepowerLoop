using System.Collections.Generic;

namespace Game
{
    public enum MapKind
    {
        Arcade
    }

    /// <summary>街机地图描述；场景地址由运行时配置提供。</summary>
    public sealed class MapDefinition
    {
        public MapDefinition(string mapId, MapKind kind = MapKind.Arcade, string displayNameKey = null)
        {
            MapId = mapId;
            Kind = kind;
            DisplayNameKey = displayNameKey;
        }

        public string MapId { get; }
        public MapKind Kind { get; }
        public string DisplayNameKey { get; }
    }

    /// <summary>正式街机地图目录，保持现有稳定地图 ID。</summary>
    public static class MapCatalog
    {
        private static readonly Dictionary<string, MapDefinition> Definitions = new Dictionary<string, MapDefinition>
        {
            [MapIds.Arcade] = new MapDefinition(MapIds.Arcade, MapKind.Arcade, "map.arcade.name")
        };
        public static bool TryGet(string mapId, out MapDefinition definition) => Definitions.TryGetValue(mapId, out definition);
    }
}
