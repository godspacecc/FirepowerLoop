using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 和家村卫星底图上的人工区域标注权威数据；只保存用户笔触，不负责推断地物或修改 Terrain/场景。
    /// </summary>
    public sealed class MapRegionAnnotationAsset : ScriptableObject
    {
        [SerializeField] private string baseImageProjectPath;
        [SerializeField] private string baseImageSha256;
        [SerializeField] private int imageWidth;
        [SerializeField] private int imageHeight;
        [SerializeField] private List<MapRegionAnnotationLayer> layers = new List<MapRegionAnnotationLayer>();

        public string BaseImageProjectPath => baseImageProjectPath;
        public string BaseImageSha256 => baseImageSha256;
        public int ImageWidth => imageWidth;
        public int ImageHeight => imageHeight;
        public IReadOnlyList<MapRegionAnnotationLayer> Layers => layers;

        /// <summary>首次建立标注时固定底图身份和像素坐标系；不会产生任何区域边界。</summary>
        public void Initialize(string projectPath, string sha256, int width, int height)
        {
            if (string.IsNullOrWhiteSpace(projectPath)) throw new ArgumentException("底图路径不能为空。", nameof(projectPath));
            if (string.IsNullOrWhiteSpace(sha256)) throw new ArgumentException("底图版本指纹不能为空。", nameof(sha256));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "底图尺寸必须为正数。");

            baseImageProjectPath = projectPath.Replace('\\', '/');
            baseImageSha256 = sha256;
            imageWidth = width;
            imageHeight = height;
            layers = new List<MapRegionAnnotationLayer>
            {
                new MapRegionAnnotationLayer(MapRegionLayerKind.Water, "水域", new Color(0.1f, 0.55f, 1f, 1f)),
                new MapRegionAnnotationLayer(MapRegionLayerKind.Road, "道路", new Color(1f, 0.45f, 0.08f, 1f)),
                new MapRegionAnnotationLayer(MapRegionLayerKind.Vegetation, "植被", new Color(0.15f, 0.78f, 0.24f, 1f)),
                new MapRegionAnnotationLayer(MapRegionLayerKind.Farmland, "田地/开阔地", new Color(1f, 0.82f, 0.15f, 1f)),
                new MapRegionAnnotationLayer(MapRegionLayerKind.Uncertain, "待确认/不处理", new Color(0.85f, 0.2f, 0.85f, 1f))
            };
        }

        public MapRegionAnnotationLayer GetLayer(MapRegionLayerKind kind)
        {
            return layers.Find(value => value.Kind == kind);
        }

        /// <summary>
        /// 追加一条完整笔触。调用方应在鼠标抬起时一次提交，并在调用前登记 Unity Undo。
        /// </summary>
        public void AddStroke(MapRegionLayerKind kind, MapRegionStrokeMode mode, float radiusPixels,
            IReadOnlyList<Vector2> points)
        {
            var layer = GetLayer(kind) ?? throw new InvalidOperationException("标注图层不存在：" + kind);
            if (points == null || points.Count == 0) throw new ArgumentException("笔触至少需要一个点。", nameof(points));
            if (radiusPixels <= 0f) throw new ArgumentOutOfRangeException(nameof(radiusPixels), "画笔半径必须为正数。");

            var copied = new List<Vector2>(points.Count);
            for (var index = 0; index < points.Count; index++)
            {
                var point = points[index];
                copied.Add(new Vector2(Mathf.Clamp(point.x, 0f, imageWidth - 1f),
                    Mathf.Clamp(point.y, 0f, imageHeight - 1f)));
            }
            layer.AddStroke(new MapRegionAnnotationStroke(mode, radiusPixels, copied));
        }

        /// <summary>清空一个语义图层；其它图层与底图身份保持不变。</summary>
        public void ClearLayer(MapRegionLayerKind kind)
        {
            var layer = GetLayer(kind) ?? throw new InvalidOperationException("标注图层不存在：" + kind);
            layer.Clear();
        }

        public bool Validate(out string error)
        {
            if (string.IsNullOrWhiteSpace(baseImageProjectPath))
            {
                error = "未记录底图路径。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(baseImageSha256))
            {
                error = "未记录底图版本指纹。";
                return false;
            }
            if (imageWidth <= 0 || imageHeight <= 0)
            {
                error = "底图尺寸无效。";
                return false;
            }
            foreach (MapRegionLayerKind kind in Enum.GetValues(typeof(MapRegionLayerKind)))
            {
                if (GetLayer(kind) != null) continue;
                error = "缺少标注图层：" + kind;
                return false;
            }
            error = string.Empty;
            return true;
        }
    }

    public enum MapRegionLayerKind
    {
        Water = 0,
        Road = 1,
        Vegetation = 2,
        Farmland = 3,
        Uncertain = 4
    }

    public enum MapRegionStrokeMode
    {
        Paint = 0,
        Erase = 1
    }

    [Serializable]
    public sealed class MapRegionAnnotationLayer
    {
        [SerializeField] private MapRegionLayerKind kind;
        [SerializeField] private string displayName;
        [SerializeField] private Color color;
        [SerializeField] private List<MapRegionAnnotationStroke> strokes = new List<MapRegionAnnotationStroke>();

        public MapRegionLayerKind Kind => kind;
        public string DisplayName => displayName;
        public Color Color => color;
        public IReadOnlyList<MapRegionAnnotationStroke> Strokes => strokes;

        public MapRegionAnnotationLayer(MapRegionLayerKind kind, string displayName, Color color)
        {
            this.kind = kind;
            this.displayName = displayName;
            this.color = color;
        }

        internal void AddStroke(MapRegionAnnotationStroke stroke)
        {
            strokes.Add(stroke);
        }

        internal void Clear()
        {
            strokes.Clear();
        }
    }

    [Serializable]
    public sealed class MapRegionAnnotationStroke
    {
        [SerializeField] private MapRegionStrokeMode mode;
        [SerializeField] private float radiusPixels;
        [SerializeField] private List<Vector2> points;

        public MapRegionStrokeMode Mode => mode;
        public float RadiusPixels => radiusPixels;
        public IReadOnlyList<Vector2> Points => points;

        public MapRegionAnnotationStroke(MapRegionStrokeMode mode, float radiusPixels, List<Vector2> points)
        {
            this.mode = mode;
            this.radiusPixels = radiusPixels;
            this.points = points;
        }
    }
}
