using System;
using UnityEngine;

namespace Game
{
    /// <summary>保存本次地图实例的俯视画面记忆，只接收玩家附近像素；不拥有探索、建筑或存档状态。</summary>
    public sealed class MapImageMemory : IDisposable
    {
        private readonly MapDisplayDefinition display;
        private readonly Color32[] pixels;
        /// <summary>独立于资源句柄的可读纹理；由所属地图 Controller 在卸载时销毁。</summary>
        public Texture2D Texture { get; private set; }

        public MapImageMemory(MapDisplayDefinition display, int size, Color32[] initialPixels)
        {
            if (size <= 0 || initialPixels == null || initialPixels.Length != size * size)
                throw new ArgumentException("地图像素尺寸不匹配");
            this.display = display;
            pixels = (Color32[])initialPixels.Clone();
            Texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { name = "MapObservedImage", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Texture.SetPixels32(pixels); Texture.Apply(false);
        }

        /// <summary>提交局部拍摄结果，圆形观察范围外保持旧像素；非法区域拒绝且不部分写入。</summary>
        public void ApplyNearbyPatch(RectInt region, Color32[] patch, Vector3 player, float radius)
        {
            if (Texture == null) throw new ObjectDisposedException(nameof(MapImageMemory));
            var size = Texture.width;
            if (region.width <= 0 || region.height <= 0 || region.xMin < 0 || region.yMin < 0 ||
                region.xMax > size || region.yMax > size || patch == null || patch.Length != region.width * region.height ||
                float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0 ||
                !MapDisplayProjection.TryWorldToNormalized(display, player, out _))
                throw new ArgumentException("地图刷新区域或玩家位置无效");
            var committed = new Color32[patch.Length];
            for (var y = 0; y < region.height; y++)
            for (var x = 0; x < region.width; x++)
            {
                var worldX = display.MinX + (region.x + x + .5f) * display.Width / size;
                var worldZ = display.MinZ + (region.y + y + .5f) * display.Height / size;
                var dx = worldX - player.x; var dz = worldZ - player.z;
                var index = (region.y + y) * size + region.x + x;
                var patchIndex = y * region.width + x;
                if (dx * dx + dz * dz <= radius * radius) pixels[index] = patch[patchIndex];
                committed[patchIndex] = pixels[index];
            }
            Texture.SetPixels32(region.x, region.y, region.width, region.height, committed);
            Texture.Apply(false);
        }

        public void Dispose()
        {
            if (Texture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(Texture);
            else UnityEngine.Object.DestroyImmediate(Texture);
            Texture = null;
        }
    }
}
