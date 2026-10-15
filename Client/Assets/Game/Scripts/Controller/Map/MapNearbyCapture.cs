using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>把当前地图附近地表渲染为像素；不激活对象，不改变采集、碰撞或探索状态。</summary>
    public static class MapNearbyCapture
    {
        private static readonly Type UrpCameraData = Type.GetType(
            "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
        /// <summary>按底图像素边界对齐相机，返回圆形观察范围的外接矩形；地图边缘裁切到合法区域。</summary>
        public static RectInt GetRegion(MapDisplayDefinition display, Vector3 player, float radius, int size)
        {
            if (size <= 0 || radius <= 0 || float.IsNaN(radius) || float.IsInfinity(radius) ||
                !MapDisplayProjection.TryWorldToNormalized(display, player, out var uv)) return default;
            var minX = Mathf.Clamp(Mathf.FloorToInt((uv.x - radius / display.Width) * size), 0, size);
            var minY = Mathf.Clamp(Mathf.FloorToInt((uv.y - radius / display.Height) * size), 0, size);
            var maxX = Mathf.Clamp(Mathf.CeilToInt((uv.x + radius / display.Width) * size), 0, size);
            var maxY = Mathf.Clamp(Mathf.CeilToInt((uv.y + radius / display.Height) * size), 0, size);
            return new RectInt(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>同步拍摄后恢复临时渲染屏蔽及 RenderTexture；失败不提交地图记忆。</summary>
        public static Color32[] Capture(MapContext context, MapDisplayDefinition display, RectInt region, int size, float playerY)
        {
            if (size <= 0 || region.width <= 0 || region.height <= 0 || region.xMin < 0 || region.yMin < 0 ||
                region.xMax > size || region.yMax > size)
                throw new ArgumentException("地图拍摄区域无效");
            if (context == null || !context.Validate(out _) || !context.MapContentRoot.activeInHierarchy)
                throw new InvalidOperationException("地图内容不可拍摄");
            var hidden = new List<Renderer>();
            var cameraObject = new GameObject("__NearbyMapCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            // 热更新程序集不额外引用 URP DLL，按已安装管线的相机接口关闭昂贵的附加渲染。
            if (UrpCameraData != null)
            {
                var data = cameraObject.AddComponent(UrpCameraData);
                foreach (var property in new[] { "renderShadows", "renderPostProcessing", "requiresColorTexture", "requiresDepthTexture" })
                    UrpCameraData.GetProperty(property)?.SetValue(data, false);
            }
            var target = RenderTexture.GetTemporary(region.width, region.height, 24, RenderTextureFormat.ARGB32);
            Texture2D image = null;
            var previous = RenderTexture.active;
            try
            {
                // 动态根只保留正式建筑；角色、掉落、预览和特效不会留下地图残影。
                foreach (var renderer in context.DynamicObjectRoot.GetComponentsInChildren<Renderer>(false))
                {
                    var building = renderer.GetComponentInParent<TemporaryBuildingView>();
                    if (building != null && building.IsCommitted && !(renderer is ParticleSystemRenderer) && !(renderer is TrailRenderer)) continue;
                    if (!renderer.forceRenderingOff) { hidden.Add(renderer); renderer.forceRenderingOff = true; }
                }
                camera.orthographic = true;
                camera.orthographicSize = region.height * display.Height / size * .5f;
                camera.aspect = region.width * display.Width / (region.height * display.Height);
                var x = display.MinX + (region.x + region.width * .5f) * display.Width / size;
                var z = display.MinZ + (region.y + region.height * .5f) * display.Height / size;
                camera.transform.SetPositionAndRotation(new Vector3(x, playerY + 200f, z), Quaternion.Euler(90, 0, 0));
                camera.nearClipPlane = .1f; camera.farClipPlane = 400f;
                camera.allowHDR = false; camera.allowMSAA = false;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(32, 37, 43, 255);
                var uiLayer = LayerMask.NameToLayer("UI"); if (uiLayer >= 0) camera.cullingMask &= ~(1 << uiLayer);
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image = new Texture2D(region.width, region.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, region.width, region.height), 0, 0); image.Apply(false);
                return image.GetPixels32();
            }
            finally
            {
                foreach (var renderer in hidden) if (renderer != null) renderer.forceRenderingOff = false;
                RenderTexture.active = previous; camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(target);
                Destroy(image); Destroy(cameraObject);
            }
        }

        /// <summary>资源纹理可为不可读导入资产，通过 GPU 复制创建独立的 512 像素地图缓存。</summary>
        public static MapImageMemory CreateMemory(MapDisplayDefinition display, Texture source)
        {
            const int size = 512;
            var target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Texture2D readable = null;
            try
            {
                Graphics.Blit(source, target); RenderTexture.active = target;
                readable = new Texture2D(size, size, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, size, size), 0, 0); readable.Apply(false);
                return new MapImageMemory(display, size, readable.GetPixels32());
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Destroy(readable); }
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
