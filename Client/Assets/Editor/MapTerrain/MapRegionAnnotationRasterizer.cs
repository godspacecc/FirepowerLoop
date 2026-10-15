using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 将可编辑笔触重放为派生遮罩；遮罩可随时重建，不承担标注数据权威。
    /// </summary>
    public static class MapRegionAnnotationRasterizer
    {
        public static byte[] BuildCoverage(MapRegionAnnotationAsset annotation, MapRegionLayerKind kind)
        {
            if (annotation == null) throw new ArgumentNullException(nameof(annotation));
            if (!annotation.Validate(out var error)) throw new InvalidOperationException(error);
            var layer = annotation.GetLayer(kind) ?? throw new InvalidOperationException("标注图层不存在：" + kind);
            var coverage = new byte[annotation.ImageWidth * annotation.ImageHeight];
            foreach (var stroke in layer.Strokes)
            {
                var value = stroke.Mode == MapRegionStrokeMode.Paint ? (byte)255 : (byte)0;
                RasterizeStroke(coverage, annotation.ImageWidth, annotation.ImageHeight, stroke, value);
            }
            return coverage;
        }

        public static Texture2D CreateOverlay(MapRegionAnnotationAsset annotation, MapRegionLayerKind kind,
            float opacity)
        {
            var coverage = BuildCoverage(annotation, kind);
            return CreateOverlayTexture(annotation, kind, opacity, coverage);
        }

        /// <summary>用正式遮罩的同一套盖章规则预览正在绘制的笔触，包含橡皮对旧笔触的影响。</summary>
        public static Texture2D CreateOverlayWithStroke(MapRegionAnnotationAsset annotation, MapRegionLayerKind kind,
            float opacity, MapRegionStrokeMode mode, float radiusPixels, IReadOnlyList<Vector2> points)
        {
            return CreateOverlayTexture(annotation, kind, opacity,
                BuildCoverageWithStroke(annotation, kind, mode, radiusPixels, points));
        }

        public static byte[] BuildCoverageWithStroke(MapRegionAnnotationAsset annotation, MapRegionLayerKind kind,
            MapRegionStrokeMode mode, float radiusPixels, IReadOnlyList<Vector2> points)
        {
            var coverage = BuildCoverage(annotation, kind);
            if (points != null && points.Count > 0)
            {
                var stroke = new MapRegionAnnotationStroke(mode, radiusPixels, new List<Vector2>(points));
                RasterizeStroke(coverage, annotation.ImageWidth, annotation.ImageHeight, stroke,
                    mode == MapRegionStrokeMode.Paint ? (byte)255 : (byte)0);
            }
            return coverage;
        }

        private static Texture2D CreateOverlayTexture(MapRegionAnnotationAsset annotation, MapRegionLayerKind kind,
            float opacity, byte[] coverage)
        {
            var layer = annotation.GetLayer(kind);
            var texture = new Texture2D(annotation.ImageWidth, annotation.ImageHeight, TextureFormat.RGBA32, false, true)
            {
                name = "Region annotation overlay - " + kind,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[coverage.Length];
            var color = (Color32)layer.Color;
            var alphaScale = Mathf.Clamp01(opacity);
            for (var topIndex = 0; topIndex < coverage.Length; topIndex++)
            {
                var x = topIndex % annotation.ImageWidth;
                var yFromTop = topIndex / annotation.ImageWidth;
                var textureIndex = (annotation.ImageHeight - 1 - yFromTop) * annotation.ImageWidth + x;
                pixels[textureIndex] = new Color32(color.r, color.g, color.b,
                    (byte)Mathf.RoundToInt(coverage[topIndex] * alphaScale));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>导出不透明黑白 PNG；白色为当前图层最终覆盖区，黑色为未覆盖区。</summary>
        public static byte[] EncodeBinaryMaskPng(MapRegionAnnotationAsset annotation, MapRegionLayerKind kind)
        {
            var coverage = BuildCoverage(annotation, kind);
            var texture = new Texture2D(annotation.ImageWidth, annotation.ImageHeight, TextureFormat.RGB24, false, true);
            try
            {
                var pixels = new Color32[coverage.Length];
                for (var topIndex = 0; topIndex < coverage.Length; topIndex++)
                {
                    var x = topIndex % annotation.ImageWidth;
                    var yFromTop = topIndex / annotation.ImageWidth;
                    var textureIndex = (annotation.ImageHeight - 1 - yFromTop) * annotation.ImageWidth + x;
                    var value = coverage[topIndex];
                    pixels[textureIndex] = new Color32(value, value, value, 255);
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                return texture.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void RasterizeStroke(byte[] coverage, int width, int height,
            MapRegionAnnotationStroke stroke, byte value)
        {
            var points = stroke.Points;
            if (points.Count == 1)
            {
                Stamp(coverage, width, height, points[0], stroke.RadiusPixels, value);
                return;
            }

            for (var pointIndex = 1; pointIndex < points.Count; pointIndex++)
            {
                var from = points[pointIndex - 1];
                var to = points[pointIndex];
                var distance = Vector2.Distance(from, to);
                var stepLength = Mathf.Max(1f, stroke.RadiusPixels * 0.35f);
                var steps = Mathf.Max(1, Mathf.CeilToInt(distance / stepLength));
                for (var step = 0; step <= steps; step++)
                    Stamp(coverage, width, height, Vector2.Lerp(from, to, step / (float)steps),
                        stroke.RadiusPixels, value);
            }
        }

        private static void Stamp(byte[] coverage, int width, int height, Vector2 center, float radius, byte value)
        {
            var minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
            var maxX = Mathf.Min(width - 1, Mathf.CeilToInt(center.x + radius));
            var minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
            var maxY = Mathf.Min(height - 1, Mathf.CeilToInt(center.y + radius));
            var radiusSquared = radius * radius;
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var deltaX = x - center.x;
                var deltaY = y - center.y;
                if (deltaX * deltaX + deltaY * deltaY <= radiusSquared) coverage[y * width + x] = value;
            }
        }
    }
}
