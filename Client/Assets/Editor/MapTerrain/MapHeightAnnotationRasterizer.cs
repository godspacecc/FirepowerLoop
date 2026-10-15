using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>从 DEM 基线和人工相对高差生成局部预览；不写 TerrainData 或原始高度图。</summary>
    public static class MapHeightAnnotationRasterizer
    {
        public const float DemBaseElevationMeters = 120f;
        public const float DemHeightRangeMeters = 330f;
        public const float DemWorldSizeMeters = 2048f;

        public static float[] BuildDeltaMeters(MapHeightAnnotationAsset annotation)
        {
            return BuildDeltaMeters(annotation, null);
        }

        public static float[] BuildDeltaMetersWithStroke(MapHeightAnnotationAsset annotation,
            MapHeightStrokeMode mode, float deltaMeters, float radiusPixels, IReadOnlyList<Vector2> points)
        {
            var activeStroke = points != null && points.Count > 0
                ? new MapHeightAnnotationStroke(mode, deltaMeters, radiusPixels, new List<Vector2>(points))
                : null;
            return BuildDeltaMeters(annotation, activeStroke);
        }

        private static float[] BuildDeltaMeters(MapHeightAnnotationAsset annotation,
            MapHeightAnnotationStroke activeStroke)
        {
            if (annotation == null) throw new ArgumentNullException(nameof(annotation));
            if (!annotation.Validate(out var error)) throw new InvalidOperationException(error);
            var values = new float[annotation.ImageWidth * annotation.ImageHeight];
            var touched = new bool[values.Length];
            foreach (var stroke in annotation.Strokes)
            {
                ApplyStroke(values, touched, annotation.ImageWidth, annotation.ImageHeight, stroke);
            }
            if (activeStroke != null)
                ApplyStroke(values, touched, annotation.ImageWidth, annotation.ImageHeight, activeStroke);
            return values;
        }

        private static void ApplyStroke(float[] values, bool[] touched, int width, int height,
            MapHeightAnnotationStroke stroke)
        {
            Array.Clear(touched, 0, touched.Length);
            RasterizeStroke(touched, width, height, stroke);
            for (var index = 0; index < values.Length; index++)
            {
                if (!touched[index]) continue;
                values[index] = stroke.Mode == MapHeightStrokeMode.ResetToDem
                    ? 0f
                    : values[index] + stroke.DeltaMeters;
            }
        }

        public static float SampleDemMeters(MapHeightAnnotationAsset annotation, Texture2D dem, Vector2 imagePoint)
        {
            if (dem == null) throw new ArgumentNullException(nameof(dem));
            var unity = annotation.ImagePointToUnity(imagePoint);
            var u = Mathf.Clamp01(unity.x / DemWorldSizeMeters);
            var v = Mathf.Clamp01(unity.y / DemWorldSizeMeters);
            return DemBaseElevationMeters + dem.GetPixelBilinear(u * (dem.width - 1f) / dem.width,
                v * (dem.height - 1f) / dem.height).grayscale * DemHeightRangeMeters;
        }

        public static Texture2D CreatePreview(MapHeightAnnotationAsset annotation, Texture2D dem, float opacity)
        {
            return CreatePreview(annotation, dem, opacity, null);
        }

        public static Texture2D CreatePreview(MapHeightAnnotationAsset annotation, Texture2D dem, float opacity,
            Color[] demBaseColors)
        {
            return CreatePreviewTexture(annotation, dem, opacity, BuildDeltaMeters(annotation), demBaseColors, true);
        }

        /// <summary>复用窗口已算出的米制高差，保证预览色块和光标读数使用同一状态。</summary>
        public static Texture2D CreatePreviewFromDelta(MapHeightAnnotationAsset annotation, Texture2D dem,
            float opacity, float[] delta, Color[] demBaseColors)
        {
            return CreatePreviewFromDeltaWithMode(annotation, dem, opacity, delta, demBaseColors, true);
        }

        /// <summary>色带关闭时仍显示人工高差及其边界，卫星底图可以独立阅读。</summary>
        public static Texture2D CreatePreviewFromDeltaWithMode(MapHeightAnnotationAsset annotation, Texture2D dem,
            float opacity, float[] delta, Color[] demBaseColors, bool showDemTint)
        {
            if (delta == null || delta.Length != annotation.ImageWidth * annotation.ImageHeight)
                throw new ArgumentException("高差尺寸与标注不一致。", nameof(delta));
            return CreatePreviewTexture(annotation, dem, opacity, delta, demBaseColors, showDemTint);
        }

        /// <summary>把当前未提交笔触按正式高差规则合成到预览，确保松开前后宽度和颜色一致。</summary>
        public static Texture2D CreatePreviewWithStroke(MapHeightAnnotationAsset annotation, Texture2D dem,
            float opacity, MapHeightStrokeMode mode, float deltaMeters, float radiusPixels,
            IReadOnlyList<Vector2> points)
        {
            return CreatePreviewWithStroke(annotation, dem, opacity, mode, deltaMeters, radiusPixels, points, null);
        }

        public static Texture2D CreatePreviewWithStroke(MapHeightAnnotationAsset annotation, Texture2D dem,
            float opacity, MapHeightStrokeMode mode, float deltaMeters, float radiusPixels,
            IReadOnlyList<Vector2> points, Color[] demBaseColors)
        {
            return CreatePreviewTexture(annotation, dem, opacity,
                BuildDeltaMetersWithStroke(annotation, mode, deltaMeters, radiusPixels, points), demBaseColors, true);
        }

        /// <summary>DEM 不随画笔变化；窗口缓存一次基础色，拖动时只重新计算人工高差与合成像素。</summary>
        public static Color[] BuildDemBaseColors(MapHeightAnnotationAsset annotation, Texture2D dem)
        {
            var colors = new Color[annotation.ImageWidth * annotation.ImageHeight];
            for (var y = 0; y < annotation.ImageHeight; y++)
            for (var x = 0; x < annotation.ImageWidth; x++)
            {
                var index = y * annotation.ImageWidth + x;
                var height = SampleDemMeters(annotation, dem, new Vector2(x, y));
                colors[index] = HeightColor(Mathf.InverseLerp(130f, 443f, height));
            }
            return colors;
        }

        /// <summary>从 DEM 米制高度生成 10 米等高线；每 50 米加粗。仅作规划阅读辅助，不改变高度数据。</summary>
        public static Texture2D CreateContourOverlay(MapHeightAnnotationAsset annotation, Texture2D dem)
        {
            var width = annotation.ImageWidth;
            var height = annotation.ImageHeight;
            var samples = new float[width * height];
            var pixels = new Color32[samples.Length];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                samples[y * width + x] = SampleDemMeters(annotation, dem, new Vector2(x, y));

            for (var y = 0; y < height - 1; y++)
            for (var x = 0; x < width - 1; x++)
            {
                var index = y * width + x;
                var level = Mathf.FloorToInt(samples[index] / 10f);
                var right = Mathf.FloorToInt(samples[index + 1] / 10f);
                var below = Mathf.FloorToInt(samples[index + width] / 10f);
                if (level == right && level == below) continue;
                var major = Mathf.FloorToInt(samples[index] / 50f) !=
                            Mathf.FloorToInt(samples[index + 1] / 50f) ||
                            Mathf.FloorToInt(samples[index] / 50f) !=
                            Mathf.FloorToInt(samples[index + width] / 50f);
                var color = major ? new Color32(255, 255, 255, 245) : new Color32(24, 25, 20, 205);
                // 图像像素从左上开始，Unity 纹理像素从左下开始。
                pixels[(height - 1 - y) * width + x] = color;
                if (major && x + 1 < width)
                    pixels[(height - 1 - y) * width + x + 1] = color;
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = "HeJiaVillage DEM contours",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D CreatePreviewTexture(MapHeightAnnotationAsset annotation, Texture2D dem,
            float opacity, float[] delta, Color[] demBaseColors, bool showDemTint)
        {
            if (demBaseColors != null && demBaseColors.Length != delta.Length)
                throw new ArgumentException("DEM 底色与标注尺寸不一致。", nameof(demBaseColors));
            var pixels = new Color32[delta.Length];
            for (var y = 0; y < annotation.ImageHeight; y++)
            for (var x = 0; x < annotation.ImageWidth; x++)
            {
                var index = y * annotation.ImageWidth + x;
                var baseColor = demBaseColors != null ? demBaseColors[index] :
                    HeightColor(Mathf.InverseLerp(130f, 443f,
                        SampleDemMeters(annotation, dem, new Vector2(x, y))));
                if (!showDemTint) baseColor = Color.clear;
                if (Mathf.Abs(delta[index]) > 0.001f)
                {
                    var editColor = delta[index] > 0f ? new Color(1f, 0.16f, 0.08f) : new Color(0.08f, 0.4f, 1f);
                    baseColor = showDemTint
                        ? Color.Lerp(baseColor, editColor, Mathf.Clamp01(Mathf.Abs(delta[index]) / 10f) * 0.8f)
                        : editColor;
                }
                // 笔触边界必须在小幅高差和低 DEM 透明度下仍可辨认。
                var edge = Mathf.Abs(delta[index]) > 0.001f &&
                           (x == 0 || y == 0 || x == annotation.ImageWidth - 1 ||
                            y == annotation.ImageHeight - 1 ||
                            Mathf.Abs(delta[index - 1]) <= 0.001f ||
                            Mathf.Abs(delta[index + 1]) <= 0.001f ||
                            Mathf.Abs(delta[index - annotation.ImageWidth]) <= 0.001f ||
                            Mathf.Abs(delta[index + annotation.ImageWidth]) <= 0.001f);
                if (edge)
                    baseColor = delta[index] > 0f ? new Color(1f, 0.2f, 0.08f, 1f) :
                        new Color(0.08f, 0.55f, 1f, 1f);
                else baseColor.a = showDemTint ? Mathf.Clamp01(opacity) :
                    Mathf.Abs(delta[index]) > 0.001f ? Mathf.Clamp01(0.22f + Mathf.Abs(delta[index]) / 20f) : 0f;
                var textureIndex = (annotation.ImageHeight - 1 - y) * annotation.ImageWidth + x;
                pixels[textureIndex] = baseColor;
            }
            var texture = new Texture2D(annotation.ImageWidth, annotation.ImageHeight, TextureFormat.RGBA32, false, true)
            {
                name = "HeJiaVillage local DEM preview",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>导出 RFloat EXR；R 通道为相对 DEM 的米制高差，可保留负值。</summary>
        public static byte[] EncodeDeltaExr(MapHeightAnnotationAsset annotation)
        {
            var delta = BuildDeltaMeters(annotation);
            var texture = new Texture2D(annotation.ImageWidth, annotation.ImageHeight, TextureFormat.RFloat, false, true);
            try
            {
                var pixels = new Color[delta.Length];
                for (var y = 0; y < annotation.ImageHeight; y++)
                for (var x = 0; x < annotation.ImageWidth; x++)
                {
                    var sourceIndex = y * annotation.ImageWidth + x;
                    var textureIndex = (annotation.ImageHeight - 1 - y) * annotation.ImageWidth + x;
                    pixels[textureIndex] = new Color(delta[sourceIndex], 0f, 0f, 1f);
                }
                texture.SetPixels(pixels);
                texture.Apply(false, false);
                return texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static Color HeightColor(float value)
        {
            if (value < 0.5f) return Color.Lerp(new Color(0.12f, 0.35f, 0.18f),
                new Color(0.72f, 0.68f, 0.24f), value * 2f);
            return Color.Lerp(new Color(0.72f, 0.68f, 0.24f), new Color(0.92f, 0.92f, 0.9f),
                (value - 0.5f) * 2f);
        }

        private static void RasterizeStroke(bool[] touched, int width, int height, MapHeightAnnotationStroke stroke)
        {
            if (stroke.Points.Count == 1)
            {
                Stamp(touched, width, height, stroke.Points[0], stroke.RadiusPixels);
                return;
            }
            for (var index = 1; index < stroke.Points.Count; index++)
            {
                var from = stroke.Points[index - 1];
                var to = stroke.Points[index];
                var distance = Vector2.Distance(from, to);
                var steps = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(1f, stroke.RadiusPixels * 0.35f)));
                for (var step = 0; step <= steps; step++)
                    Stamp(touched, width, height, Vector2.Lerp(from, to, step / (float)steps), stroke.RadiusPixels);
            }
        }

        private static void Stamp(bool[] touched, int width, int height, Vector2 center, float radius)
        {
            var minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
            var maxX = Mathf.Min(width - 1, Mathf.CeilToInt(center.x + radius));
            var minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
            var maxY = Mathf.Min(height - 1, Mathf.CeilToInt(center.y + radius));
            var radiusSquared = radius * radius;
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var dx = x - center.x;
                var dy = y - center.y;
                if (dx * dx + dy * dy <= radiusSquared) touched[y * width + x] = true;
            }
        }
    }
}
