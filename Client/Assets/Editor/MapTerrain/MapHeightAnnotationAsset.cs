using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 用户确认卫星范围内的人工高度修正草图；DEM 始终是基线，本资产只保存相对高差笔触。
    /// </summary>
    public sealed class MapHeightAnnotationAsset : ScriptableObject
    {
        [SerializeField] private string baseImageProjectPath;
        [SerializeField] private string baseImageSha256;
        [SerializeField] private int imageWidth;
        [SerializeField] private int imageHeight;
        [SerializeField] private string demAssetPath;
        [SerializeField] private string demSha256;
        [SerializeField] private double gcjWest;
        [SerializeField] private double gcjEast;
        [SerializeField] private double gcjSouth;
        [SerializeField] private double gcjNorth;
        [SerializeField] private Vector2 unityNorthWest;
        [SerializeField] private Vector2 unityNorthEast;
        [SerializeField] private Vector2 unitySouthWest;
        [SerializeField] private Vector2 unitySouthEast;
        [SerializeField] private List<MapHeightAnnotationStroke> strokes = new List<MapHeightAnnotationStroke>();

        public string BaseImageProjectPath => baseImageProjectPath;
        public string BaseImageSha256 => baseImageSha256;
        public int ImageWidth => imageWidth;
        public int ImageHeight => imageHeight;
        public string DemAssetPath => demAssetPath;
        public string DemSha256 => demSha256;
        public double GcjWest => gcjWest;
        public double GcjEast => gcjEast;
        public double GcjSouth => gcjSouth;
        public double GcjNorth => gcjNorth;
        public Vector2 UnityNorthWest => unityNorthWest;
        public Vector2 UnityNorthEast => unityNorthEast;
        public Vector2 UnitySouthWest => unitySouthWest;
        public Vector2 UnitySouthEast => unitySouthEast;
        public IReadOnlyList<MapHeightAnnotationStroke> Strokes => strokes;

        /// <summary>
        /// 固定已确认底图、DEM 版本和四角映射。四角来自 GCJ-02 边界经迭代反算 WGS84、再投影到 UTM49N；
        /// 其精度仅适合截图级规划，不作为测绘成果。
        /// </summary>
        public void Initialize(string imagePath, string imageSha256, int width, int height,
            string sourceDemPath, string sourceDemSha256)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || string.IsNullOrWhiteSpace(imageSha256))
                throw new ArgumentException("底图路径和指纹不能为空。");
            if (string.IsNullOrWhiteSpace(sourceDemPath) || string.IsNullOrWhiteSpace(sourceDemSha256))
                throw new ArgumentException("DEM 路径和指纹不能为空。");
            if (width <= 1 || height <= 1) throw new ArgumentOutOfRangeException(nameof(width));

            baseImageProjectPath = imagePath.Replace('\\', '/');
            baseImageSha256 = imageSha256;
            imageWidth = width;
            imageHeight = height;
            demAssetPath = sourceDemPath.Replace('\\', '/');
            demSha256 = sourceDemSha256;
            gcjWest = 112.189234655;
            gcjEast = 112.195221345;
            gcjSouth = 27.631620987;
            gcjNorth = 27.638264912;

            // Unity 原点是全图西南角；x 向东、z 向北，单位为米。
            unityNorthWest = HeJiaVillageAnnotationFrame.NorthWest;
            unityNorthEast = HeJiaVillageAnnotationFrame.NorthEast;
            unitySouthWest = HeJiaVillageAnnotationFrame.SouthWest;
            unitySouthEast = HeJiaVillageAnnotationFrame.SouthEast;
            strokes = new List<MapHeightAnnotationStroke>();
        }

        /// <summary>把底图左上原点像素映射到全图 Unity XZ；使用四角双线性插值保留投影产生的轻微旋转。</summary>
        public Vector2 ImagePointToUnity(Vector2 imagePoint)
        {
            var u = Mathf.Clamp01(imagePoint.x / (imageWidth - 1f));
            var v = Mathf.Clamp01(imagePoint.y / (imageHeight - 1f));
            return Vector2.Lerp(Vector2.Lerp(unityNorthWest, unityNorthEast, u),
                Vector2.Lerp(unitySouthWest, unitySouthEast, u), v);
        }

        public void AddStroke(MapHeightStrokeMode mode, float deltaMeters, float radiusPixels,
            IReadOnlyList<Vector2> points)
        {
            if (points == null || points.Count == 0) throw new ArgumentException("高度笔触至少需要一个点。", nameof(points));
            if (radiusPixels <= 0f) throw new ArgumentOutOfRangeException(nameof(radiusPixels));
            if (mode == MapHeightStrokeMode.AddOffset && Mathf.Approximately(deltaMeters, 0f))
                throw new ArgumentException("相对高差不能为 0。", nameof(deltaMeters));

            var copied = new List<Vector2>(points.Count);
            for (var index = 0; index < points.Count; index++)
            {
                var point = points[index];
                copied.Add(new Vector2(Mathf.Clamp(point.x, 0f, imageWidth - 1f),
                    Mathf.Clamp(point.y, 0f, imageHeight - 1f)));
            }
            strokes.Add(new MapHeightAnnotationStroke(mode, deltaMeters, radiusPixels, copied));
        }

        public void Clear()
        {
            strokes.Clear();
        }

        public bool Validate(out string error)
        {
            if (string.IsNullOrWhiteSpace(baseImageProjectPath) || string.IsNullOrWhiteSpace(baseImageSha256) ||
                string.IsNullOrWhiteSpace(demAssetPath) || string.IsNullOrWhiteSpace(demSha256))
            {
                error = "底图或 DEM 身份未锁定。";
                return false;
            }
            if (imageWidth <= 1 || imageHeight <= 1)
            {
                error = "底图尺寸无效。";
                return false;
            }
            if (gcjWest >= gcjEast || gcjSouth >= gcjNorth)
            {
                error = "底图 GCJ-02 边界无效。";
                return false;
            }
            var corners = new[] { unityNorthWest, unityNorthEast, unitySouthWest, unitySouthEast };
            foreach (var corner in corners)
            {
                if (corner.x < 0f || corner.x > 2048f || corner.y < 0f || corner.y > 2048f)
                {
                    error = "局部底图超出和家村 DEM 范围。";
                    return false;
                }
            }
            error = string.Empty;
            return true;
        }
    }

    public enum MapHeightStrokeMode
    {
        AddOffset = 0,
        ResetToDem = 1
    }

    [Serializable]
    public sealed class MapHeightAnnotationStroke
    {
        [SerializeField] private MapHeightStrokeMode mode;
        [SerializeField] private float deltaMeters;
        [SerializeField] private float radiusPixels;
        [SerializeField] private List<Vector2> points;

        public MapHeightStrokeMode Mode => mode;
        public float DeltaMeters => deltaMeters;
        public float RadiusPixels => radiusPixels;
        public IReadOnlyList<Vector2> Points => points;

        public MapHeightAnnotationStroke(MapHeightStrokeMode mode, float deltaMeters, float radiusPixels,
            List<Vector2> points)
        {
            this.mode = mode;
            this.deltaMeters = deltaMeters;
            this.radiusPixels = radiusPixels;
            this.points = points;
        }
    }
}
