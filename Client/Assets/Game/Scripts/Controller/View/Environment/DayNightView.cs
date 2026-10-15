using QFramework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game
{
    /// <summary>
    /// 将昼夜快照应用到全局方向光和环境光；地图可临时提供天体轨道，但不接管光强与环境状态。
    /// 保留正午晴天光照，不推进玩法时间。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public sealed class DayNightView : MonoBehaviour
    {
        [Header("太阳与月光")]
        [SerializeField] private Light directionalLight;
        [SerializeField] private float lightYaw = -30f;
        [SerializeField, Min(0f)] private float daylightIntensity = 1.05f;
        [SerializeField, Min(0f)] private float moonlightIntensity = 0.12f;
        [SerializeField] private Color daylightColor = new Color(1f, 0.956f, 0.84f, 1f);
        [SerializeField] private Color twilightColor = new Color(1f, 0.52f, 0.28f, 1f);
        [SerializeField] private Color moonlightColor = new Color(0.42f, 0.52f, 0.78f, 1f);

        [Header("环境可见度")]
        [SerializeField] private Color daylightAmbient = new Color(0.58f, 0.62f, 0.68f, 1f);
        [SerializeField] private Color twilightAmbient = new Color(0.20f, 0.15f, 0.18f, 1f);
        [SerializeField] private Color nightAmbient = new Color(0.065f, 0.085f, 0.14f, 1f);
        [SerializeField] private Color daylightFog = new Color(0.62f, 0.70f, 0.78f, 1f);
        [SerializeField] private Color nightFog = new Color(0.035f, 0.055f, 0.10f, 1f);

        private bool baselineCaptured;
        private Light baselineSun;
        private Quaternion baselineRotation;
        private Color baselineLightColor;
        private float baselineIntensity;
        private AmbientMode baselineAmbientMode;
        private Color baselineAmbientLight;
        private Color baselineFogColor;
        // 地图启用程序化天空时临时提供轨道；光强、颜色和环境光仍由本 View 统一应用。
        private MapProceduralSkyView orbitSource;
        private float lastDayProgress = .5f;

        private void Awake()
        {
            if (directionalLight == null)
            {
                directionalLight = GetComponent<Light>();
            }

            CaptureBaseline();
        }

        private void Start()
        {
            Apply(.5f);
        }

        private void OnDestroy()
        {
            RestoreBaseline();
        }

        internal void Apply(float dayProgress)
        {
            lastDayProgress = dayProgress;
            if (directionalLight == null)
            {
                return;
            }

            var progress = dayProgress;
            var solarHeight = Mathf.Sin((progress - 0.25f) * Mathf.PI * 2f);
            var daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0.18f, solarHeight));
            var twilight = CalculateTwilightAmount(solarHeight);

            // 单方向光白天模拟太阳，夜晚转为较弱的月光；在地平线附近光强很低，方向切换不可见。
            var elevation = solarHeight >= 0f
                ? Mathf.Lerp(4f, 72f, solarHeight)
                : Mathf.Lerp(18f, 35f, -solarHeight);
            var yaw = solarHeight >= 0f ? lightYaw : lightYaw + 180f;
            directionalLight.transform.rotation = Quaternion.Euler(elevation, yaw, 0f);
            directionalLight.intensity = Mathf.Lerp(moonlightIntensity, daylightIntensity, daylight);
            if (orbitSource != null)
            {
                var sunDirection = orbitSource.GetSunDirection(progress);
                var incomingRay = solarHeight >= 0f ? -sunDirection : sunDirection;
                directionalLight.transform.rotation = Quaternion.LookRotation(incomingRay);
                // 日月在地平线换班时降低光强，避免相反方向的阴影突然翻转。
                directionalLight.intensity *= Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(0f, 0.12f, Mathf.Abs(solarHeight)));
            }
            directionalLight.color = Color.Lerp(
                Color.Lerp(moonlightColor, twilightColor, twilight),
                daylightColor,
                daylight);

            RenderSettings.sun = directionalLight;
            RenderSettings.ambientMode = AmbientMode.Flat;
            var lowLightAmbient = Color.Lerp(nightAmbient, twilightAmbient, twilight);
            RenderSettings.ambientLight = Color.Lerp(lowLightAmbient, daylightAmbient, daylight);
            var fogTransition = Mathf.Max(daylight, twilight * 0.35f);
            RenderSettings.fogColor = Color.Lerp(nightFog, daylightFog, fogTransition);
            var overcast = 0f;
            directionalLight.intensity *= Mathf.Lerp(1f, .32f, overcast);
            RenderSettings.ambientLight *= Mathf.Lerp(1f, .78f, overcast);
            RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, new Color(.35f,.40f,.46f) * Mathf.Lerp(.22f,1f,daylight), overcast * .65f);
        }

        internal static float CalculateTwilightAmount(float solarHeight)
        {
            var distanceFromHorizon = Mathf.Abs(solarHeight);
            var normalized = Mathf.InverseLerp(0.05f, 0.75f, distanceFromHorizon);
            return 1f - Mathf.SmoothStep(0f, 1f, normalized);
        }

        /// <summary>地图天空准备完成后绑定轨道；拒绝第二个所有者，避免地图重叠时争抢方向光。</summary>
        internal bool BindOrbit(MapProceduralSkyView source)
        {
            if (source == null || (orbitSource != null && orbitSource != source)) return false;
            orbitSource = source;
            Apply(lastDayProgress);
            return true;
        }

        /// <summary>只有绑定者可以解除；立即恢复当前时刻的原有光照方向，不影响其他地图。</summary>
        internal void UnbindOrbit(MapProceduralSkyView source)
        {
            if (orbitSource != source) return;
            orbitSource = null;
            Apply(lastDayProgress);
        }

        private void CaptureBaseline()
        {
            if (baselineCaptured || directionalLight == null)
            {
                return;
            }

            baselineCaptured = true;
            baselineSun = RenderSettings.sun;
            baselineRotation = directionalLight.transform.rotation;
            baselineLightColor = directionalLight.color;
            baselineIntensity = directionalLight.intensity;
            baselineAmbientMode = RenderSettings.ambientMode;
            baselineAmbientLight = RenderSettings.ambientLight;
            baselineFogColor = RenderSettings.fogColor;
        }

        private void RestoreBaseline()
        {
            if (!baselineCaptured || directionalLight == null)
            {
                return;
            }

            directionalLight.transform.rotation = baselineRotation;
            directionalLight.color = baselineLightColor;
            directionalLight.intensity = baselineIntensity;
            RenderSettings.sun = baselineSun;
            RenderSettings.ambientMode = baselineAmbientMode;
            RenderSettings.ambientLight = baselineAmbientLight;
            RenderSettings.fogColor = baselineFogColor;
        }
    }
}
