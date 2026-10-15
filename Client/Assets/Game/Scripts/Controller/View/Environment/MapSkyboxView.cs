using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 为当前地图提供场景专属天空盒，并跟随既有昼夜快照切换日夜表现。
    /// 该组件只负责 RenderSettings 表现，不保存时间、不决定地图状态，也不修改昼夜规则。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapSkyboxView : MonoBehaviour
    {
        [Header("地图天空")]
        [SerializeField] private Material daySkybox;
        [SerializeField] private Material nightSkybox;
        [SerializeField, Range(-1f, 1f)] private float nightSolarHeightThreshold = -0.08f;
        [Tooltip("地图天空盒接管期间使用的环境反射强度；禁用组件时恢复接管前数值。")]
        [SerializeField, Range(0f, 1f)] private float reflectionIntensity = 0.5f;

        private IUnRegister visualChangedUnregister;
        private Material baselineSkybox;
        private float baselineReflectionIntensity;
        private Material appliedSkybox;
        private bool baselineCaptured;

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            CaptureBaseline();
            visualChangedUnregister = Game.Interface.RegisterEvent<DayNightVisualChangedEvent>(OnVisualChanged);
            var system = Game.Interface.GetSystem<DayNightVisualSystem>();
            if (system != null)
            {
                Apply(system.GetSnapshot());
            }
        }

        private void OnDisable()
        {
            visualChangedUnregister?.UnRegister();
            visualChangedUnregister = null;

            if (Application.isPlaying)
            {
                RestoreBaseline();
            }
        }

        private void OnVisualChanged(DayNightVisualChangedEvent evt)
        {
            Apply(evt.Snapshot);
        }

        /// <summary>
        /// 昼夜事件到达时幂等切换天空并维持地图反射强度；缺少对应材质时保留当前天空，
        /// 但仍应用反射强度，避免地形被默认天空反射洗白。
        /// </summary>
        internal void Apply(DayNightVisualSnapshot snapshot)
        {
            var solarHeight = Mathf.Sin((snapshot.DayProgress - 0.25f) * Mathf.PI * 2f);
            var target = solarHeight < nightSolarHeightThreshold ? nightSkybox : daySkybox;
            var changed = false;
            if (!Mathf.Approximately(RenderSettings.reflectionIntensity, reflectionIntensity))
            {
                RenderSettings.reflectionIntensity = reflectionIntensity;
                changed = true;
            }

            if (target != null && target != appliedSkybox)
            {
                appliedSkybox = target;
                RenderSettings.skybox = target;
                changed = true;
            }

            if (changed)
            {
                DynamicGI.UpdateEnvironment();
            }
        }

        private void CaptureBaseline()
        {
            if (baselineCaptured)
            {
                return;
            }

            baselineCaptured = true;
            baselineSkybox = RenderSettings.skybox;
            baselineReflectionIntensity = RenderSettings.reflectionIntensity;
            appliedSkybox = null;
        }

        private void RestoreBaseline()
        {
            if (!baselineCaptured)
            {
                return;
            }

            RenderSettings.skybox = baselineSkybox;
            RenderSettings.reflectionIntensity = baselineReflectionIntensity;
            DynamicGI.UpdateEnvironment();
            baselineSkybox = null;
            baselineReflectionIntensity = 0f;
            appliedSkybox = null;
            baselineCaptured = false;
        }
    }
}
