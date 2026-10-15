using UnityEngine;
using UnityEngine.Rendering;

namespace Game
{
    /// <summary>
    /// 仅在 Android Player 启动时降低 3D 内部渲染比例；Canvas 仍按设备屏幕分辨率渲染。
    /// </summary>
    public static class MobileRenderScaleRuntime
    {
        public const float AndroidInitialRenderScale = 0.85f;

        public static void ApplyForCurrentPlatform()
        {
            if (!ShouldApply(Application.platform))
            {
                return;
            }

            var asset = GraphicsSettings.currentRenderPipeline ?? QualitySettings.renderPipeline;
            var property = asset?.GetType().GetProperty("renderScale");
            if (property != null && property.CanWrite && property.PropertyType == typeof(float))
            {
                property.SetValue(asset, AndroidInitialRenderScale, null);
                return;
            }

            Log.Error("[MobileRenderScaleRuntime] Android 当前渲染管线不支持 renderScale，未能应用 0.85。");
        }

        internal static bool ShouldApply(RuntimePlatform platform)
        {
            return platform == RuntimePlatform.Android;
        }
    }
}
