using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 将固定晴天光照应用到地图自有的程序化天空材质，并向全局光照提供同一条日月轨道。
    /// 不推进时间、不保存状态、不直接控制方向光；禁用时释放材质和表现接管权。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapProceduralSkyView : MonoBehaviour
    {
        [Header("天空资源")]
        [Tooltip("项目内的模板；运行时只修改独立实例，不修改共享材质。")]
        [SerializeField] private Material skyTemplate;
        [SerializeField, Range(0f, 1f)] private float reflectionIntensity = 0.5f;

        [Header("日月轨道与外观")]
        [Tooltip("轨道绕世界 Y 轴旋转的角度；0 度时太阳从 +X 升起、向 -X 落下。")]
        [SerializeField] private float orbitYaw;
        [SerializeField, Range(10f, 85f)] private float noonElevation = 72f;
        [Tooltip("天体在单位天空球上的直径参数，不是世界米。")]
        [SerializeField, Range(0.005f, 0.15f)] private float sunSize = 0.035f;
        [SerializeField, Range(0.005f, 0.15f)] private float moonSize = 0.025f;
        [SerializeField, Range(0f, 1f)] private float sunGlow = 0.25f;
        [SerializeField, Range(0f, 1f)] private float moonGlow = 0.15f;

        [Header("天空渐变")]
        [SerializeField] private Color daySky = new Color(0.22f, 0.53f, 0.78f);
        [SerializeField] private Color twilightSky = new Color(0.22f, 0.17f, 0.30f);
        [SerializeField] private Color nightSky = new Color(0.015f, 0.025f, 0.065f);
        [SerializeField] private Color dayHorizon = new Color(0.72f, 0.82f, 0.89f);
        [SerializeField] private Color twilightHorizon = new Color(0.95f, 0.40f, 0.15f);
        [SerializeField] private Color nightHorizon = new Color(0.045f, 0.065f, 0.12f);

        [Header("流云与星空")]
        [SerializeField, Range(0f, 1f)] private float cloudOpacity = 0.55f;
        [Tooltip("Jupiter 噪声流动倍率，越大移动越快；仅用于视觉动画。")]
        [SerializeField, Range(0f, 50f)] private float cloudSpeed = 8f;
        [SerializeField] private Vector2 cloudFlow = new Vector2(1f, 0.3f);
        [SerializeField, Range(0.2f, 10f)] private float cloudScale = 2f;
        [SerializeField, Range(0.1f, 2f)] private float starDensity = 0.6f;
        [SerializeField, Range(0f, 1f)] private float starOpacity = 0.85f;

        // 同一启停周期独占此实例和接管前快照；空值表示尚未接管或已经释放。
        private Material runtimeSky;
        private Material baselineSky;
        private float baselineReflection;
        private DayNightView lighting;
        private float lastDayProgress = .5f;

        /// <summary>最近一次初始化的失败原因；成功重试后清空，仅用于现场诊断。</summary>
        internal string InitializationError { get; private set; }

        /// <summary>地图事务激活内容根时接管天空；候选场景须保持内容根隐藏，避免与旧地图竞争。</summary>
        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (!Initialize(FindObjectOfType<DayNightView>()))
            {
                Debug.LogError($"[MapProceduralSkyView] 场景 {gameObject.scene.name}：{InitializationError}，保留原天空。", this);
                return;
            }
            Apply(.5f);
            // 首次启用不依赖事件订阅顺序，立即对齐刚恢复或加载的时刻。
            lighting.Apply(.5f);
            DynamicGI.UpdateEnvironment();
        }

        private void OnDisable() => Release();
        private void OnDestroy() => Release();

        /// <summary>进入地图时先验证依赖再接管；重复调用幂等，失败不改变环境或光照。</summary>
        internal bool Initialize(DayNightView targetLighting)
        {
            InitializationError = null;
            if (runtimeSky != null) return true;
            if (skyTemplate == null)
                InitializationError = "未配置天空模板";
            else if (targetLighting == null)
                InitializationError = "未找到启用的 DayNightView";
            else if (skyTemplate.shader == null)
                InitializationError = $"天空模板 {skyTemplate.name} 缺少 Shader";
            else if (!skyTemplate.shader.isSupported)
                InitializationError = $"天空 Shader {skyTemplate.shader.name} 不支持当前图形设备 {SystemInfo.graphicsDeviceType}";
            else if (!skyTemplate.HasProperty("_SunDirection") || !skyTemplate.HasProperty("_CloudTex"))
                InitializationError = $"天空模板 {skyTemplate.name} 缺少必需参数 _SunDirection 或 _CloudTex";
            if (InitializationError != null) return false;

            var candidate = new Material(skyTemplate) { name = skyTemplate.name + " (Runtime)" };
            if (!targetLighting.BindOrbit(this))
            {
                InitializationError = $"昼夜轨道绑定被 {targetLighting.gameObject.scene.name}/{targetLighting.name} 拒绝，已有其他地图持有绑定";
                DestroyMaterial(candidate);
                return false;
            }
            baselineSky = RenderSettings.skybox;
            baselineReflection = RenderSettings.reflectionIntensity;
            lighting = targetLighting;
            runtimeSky = candidate;
            RenderSettings.skybox = runtimeSky;
            RenderSettings.reflectionIntensity = reflectionIntensity;
            return true;
        }

        /// <summary>消费既有时刻，更新天空参数；无实例时不接管任何全局状态。</summary>
        internal void Apply(float dayProgress)
        {
            lastDayProgress = dayProgress;
            if (runtimeSky == null) return;
            var sun = GetSunDirection(dayProgress);
            var solarHeight = Mathf.Sin((dayProgress - 0.25f) * Mathf.PI * 2f);
            var daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0.18f, solarHeight));
            var twilight = DayNightView.CalculateTwilightAmount(solarHeight);
            var skyColor = Color.Lerp(Color.Lerp(nightSky, twilightSky, twilight), daySky, daylight);
            var horizonColor = Color.Lerp(Color.Lerp(nightHorizon, twilightHorizon, twilight), dayHorizon, daylight);
            var grey = new Color(.32f,.38f,.45f) * Mathf.Lerp(.12f,1f,daylight);
            skyColor = Color.Lerp(skyColor, grey, 0f * .85f);
            horizonColor = Color.Lerp(horizonColor, grey, 0f * .8f);
            runtimeSky.SetColor("_SkyColor", skyColor);
            runtimeSky.SetColor("_HorizonColor", horizonColor);
            runtimeSky.SetColor("_GroundColor", Color.Lerp(nightHorizon, dayHorizon * 0.5f, daylight));
            // Jupiter 使用入射光方向；天体在天空中的方向取负，月亮与太阳相隔半圈。
            runtimeSky.SetVector("_SunDirection", -sun);
            runtimeSky.SetVector("_MoonDirection", sun);
            var sunColor = Color.Lerp(new Color(1f, 0.40f, 0.12f), new Color(1f, 0.95f, 0.8f), daylight);
            sunColor.a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.03f, 0.04f, sun.y));
            sunColor.a *= Mathf.Lerp(1f,.15f,0f);
            var moonColor = new Color(0.8f, 0.87f, 1f, 1f - daylight);
            runtimeSky.SetColor("_SunColor", sunColor);
            runtimeSky.SetColor("_MoonColor", moonColor);
            runtimeSky.SetFloat("_SunSize", sunSize);
            runtimeSky.SetFloat("_MoonSize", moonSize);
            runtimeSky.SetFloat("_SunGlow", sunGlow);
            runtimeSky.SetFloat("_MoonGlow", moonGlow);
            runtimeSky.SetFloat("_StarsDensity0", starDensity);
            var stars = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.25f, 0.02f, solarHeight));
            runtimeSky.SetFloat("_StarsOpacity", stars * starOpacity);
            var cloudColor = Color.Lerp(Color.Lerp(new Color(0.06f, 0.08f, 0.14f),
                new Color(0.65f, 0.30f, 0.22f), twilight), new Color(0.92f, 0.94f, 0.96f), daylight);
            cloudColor = Color.Lerp(cloudColor, grey * .8f, 0f * .8f);
            cloudColor.a = Mathf.Lerp(cloudOpacity, 1f, 0f);
            runtimeSky.SetColor("_OverheadCloudColor", cloudColor);
            runtimeSky.SetFloat("_OverheadCloudAnimationSpeed", cloudSpeed);
            runtimeSky.SetFloat("_OverheadCloudFlowDirectionX", cloudFlow.x);
            runtimeSky.SetFloat("_OverheadCloudFlowDirectionZ", cloudFlow.y);
            runtimeSky.SetFloat("_OverheadCloudSize", cloudScale);
        }

        /// <summary>由一天内进度计算世界天空方向；日出 +X、日落 -X，正午最高点受倾角限制。</summary>
        internal Vector3 GetSunDirection(float progress)
        {
            var angle = (progress - 0.25f) * Mathf.PI * 2f;
            var elevation = noonElevation * Mathf.Deg2Rad;
            var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle) * Mathf.Sin(elevation),
                Mathf.Sin(angle) * Mathf.Cos(elevation));
            return Quaternion.Euler(0f, orbitYaw, 0f) * direction;
        }

        /// <summary>地图禁用或销毁时幂等释放；仅恢复仍由自己持有的天空，避免覆盖后继地图。</summary>
        internal void Release()
        {
            if (runtimeSky == null) return;
            if (RenderSettings.skybox == runtimeSky)
            {
                RenderSettings.skybox = baselineSky;
                RenderSettings.reflectionIntensity = baselineReflection;
                DynamicGI.UpdateEnvironment();
            }
            if (lighting != null) lighting.UnbindOrbit(this);
            lighting = null;
            DestroyMaterial(runtimeSky);
            runtimeSky = null;
            baselineSky = null;
        }

        private static void DestroyMaterial(Material material)
        {
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }
    }
}
