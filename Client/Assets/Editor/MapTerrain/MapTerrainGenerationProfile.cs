using System;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 地表层在现实数据控制图中的业务语义；仅负责决定混合权重，不改变 TerrainLayer 美术内容。
    /// </summary>
    public enum MapTerrainSurfaceRole
    {
        HeightBased = 0,
        Sand = 1,
        Vegetation = 2,
        Dirt = 3,
        Rock = 4
    }

    [Serializable]
    public sealed class MapTerrainSurfaceLayerRule
    {
        [SerializeField] private TerrainLayer layer;
        [SerializeField] private MapTerrainSurfaceRole role;
        [SerializeField, Range(0f, 1f)] private float centerHeight = 0.5f;
        [SerializeField, Range(0.01f, 1f)] private float blendWidth = 0.5f;

        public TerrainLayer Layer => layer;
        public MapTerrainSurfaceRole Role => role;
        public float CenterHeight => centerHeight;
        public float BlendWidth => blendWidth;
    }

    [CreateAssetMenu(fileName = "地图地形生成配置", menuName = "Game/地图地形生成配置")]
    public sealed class MapTerrainGenerationProfile : ScriptableObject
    {
        [SerializeField] private string mapName;
        [SerializeField] private Texture2D heightmap;
        [SerializeField] private int rows = 1;
        [SerializeField] private int columns = 1;
        [SerializeField] private float tileWorldSize = 1000f;
        [SerializeField] private float maxTerrainHeight = 300f;
        [SerializeField] private int heightmapResolution = 129;
        [SerializeField] private int detailSeed;
        [SerializeField] private float detailFrequency = 4f;
        [SerializeField] private float detailAmplitude;
        [SerializeField] private int detailOctaves = 3;
        [SerializeField] private int alphamapResolution = 256;
        [SerializeField] private Texture2D surfaceControlMap;
        [SerializeField, Min(1f)] private float surfaceSlopeSampleMeters = 30f;
        [SerializeField, Range(0f, 89f)] private float rockSlopeStartDegrees = 20f;
        [SerializeField, Range(1f, 90f)] private float rockSlopeFullDegrees = 37f;
        [SerializeField] private MapTerrainSurfaceLayerRule[] surfaceLayers = Array.Empty<MapTerrainSurfaceLayerRule>();

        public string MapName => mapName;
        public Texture2D Heightmap => heightmap;
        public int Rows => rows;
        public int Columns => columns;
        public float TileWorldSize => tileWorldSize;
        public float MaxTerrainHeight => maxTerrainHeight;
        public int HeightmapResolution => heightmapResolution;
        public int DetailSeed => detailSeed;
        public float DetailFrequency => detailFrequency;
        public float DetailAmplitude => detailAmplitude;
        public int DetailOctaves => detailOctaves;
        public int AlphamapResolution => alphamapResolution;
        /// <summary>
        /// 全图控制图：R 为 WorldCover 分类值及人工核实修正，G 为 NDVI P50 原始值，
        /// A 为 OSM 道路与人工核实道路遮罩。
        /// 空值表示继续使用原有的纯高度混合规则。
        /// </summary>
        public Texture2D SurfaceControlMap => surfaceControlMap;
        /// <summary>计算地表坡度时的半径，单位为米；应与权威高程源的名义采样尺度一致。</summary>
        public float SurfaceSlopeSampleMeters => surfaceSlopeSampleMeters;
        /// <summary>开始混入陡坡材质的真实坡度，单位为度。</summary>
        public float RockSlopeStartDegrees => rockSlopeStartDegrees;
        /// <summary>陡坡材质达到完整权重的真实坡度，单位为度。</summary>
        public float RockSlopeFullDegrees => rockSlopeFullDegrees;
        public MapTerrainSurfaceLayerRule[] SurfaceLayers => surfaceLayers;
    }
}
