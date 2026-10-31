using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>采用版昼夜为六分钟纯视觉循环，不改变刷怪或角色属性。</summary>
        private const double SurvivorDayNightSeconds = 360;
        /// <summary>仅保存被本局临时关闭的方向光，退出时逐一归还。</summary>
        private readonly Dictionary<Light, bool> survivorOriginalLights = new();
        /// <summary>本局根拥有的主光与天空补光；不持有资源加载句柄。</summary>
        private Light survivorSun, survivorSkyFill;
        /// <summary>RenderSettings 的原主光引用，必须先于销毁本局灯光恢复。</summary>
        private Light survivorPreviousSun;
        /// <summary>已采用预览的白天、黄昏、夜晚、黎明及循环终点色调。</summary>
        private static readonly Color[] SurvivorLightColors =
        {
            new(1,.96f,.85f), new(1,.61f,.32f), new(.48f,.62f,1), new(1,.75f,.58f), new(1,.96f,.85f)
        };
        /// <summary>夜晚仍保留主光与补光，避免只靠漆黑增加压力。</summary>
        private static readonly float[] SurvivorLightStrengths = {1.5f,1.05f,.42f,.9f,1.5f};

        /// <summary>幸存者启动时接管当前方向光；重复设置保持同一所有权。</summary>
        public void BeginSurvivorDayNight()
        {
            if (root == null) throw new InvalidOperationException("昼夜必须在战场创建后启动");
            if (survivorSun != null) { SetSurvivorTime(0); return; }
            survivorPreviousSun = RenderSettings.sun;
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional) continue;
                survivorOriginalLights.Add(light, light.enabled);
                light.enabled = false;
            }
            survivorSun = CreateSurvivorLight("SurvivorSun", new Vector3(48,-45,0));
            survivorSun.shadows = LightShadows.Soft;
            survivorSkyFill = CreateSurvivorLight("SurvivorSkyFill", new Vector3(55,135,0));
            survivorSkyFill.color = new Color(.72f,.80f,1);
            RenderSettings.sun = survivorSun;
            SetSurvivorTime(0);
        }

        /// <summary>灯光属于本局根，退出时随根销毁，不修改场景资源。</summary>
        private Light CreateSurvivorLight(string name, Vector3 angles)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            child.transform.rotation = Quaternion.Euler(angles);
            var light = child.AddComponent<Light>(); light.type = LightType.Directional;
            return light;
        }

        /// <summary>只读本局有效时间；暂停无新时间，重复投影保持同一画面。</summary>
        public void SetSurvivorTime(double elapsedSeconds)
        {
            if (survivorSun == null || survivorSkyFill == null || double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds)) return;
            float phase = (float)((Math.Max(0,elapsedSeconds) % SurvivorDayNightSeconds) / SurvivorDayNightSeconds * 4);
            int segment = Mathf.Min(3, Mathf.FloorToInt(phase));
            float blend = Mathf.SmoothStep(0,1,phase-segment);
            survivorSun.color = Color.Lerp(SurvivorLightColors[segment],SurvivorLightColors[segment+1],blend);
            survivorSun.intensity = Mathf.Lerp(SurvivorLightStrengths[segment],SurvivorLightStrengths[segment+1],blend);
            survivorSun.transform.rotation = Quaternion.Euler(Mathf.Lerp(segment==1?20:48,segment+1==1?20:48,blend),-45,0);
            survivorSkyFill.intensity = Mathf.Lerp(segment==2?.25f:.40f,segment+1==2?.25f:.40f,blend);
        }

        /// <summary>先归还全局光照，再由 End 销毁本局根；重复释放不触碰其它模式。</summary>
        private void ReleaseSurvivorDayNight()
        {
            if (survivorOriginalLights.Count == 0 && survivorSun == null) return;
            if (RenderSettings.sun == survivorSun) RenderSettings.sun = survivorPreviousSun;
            foreach (var pair in survivorOriginalLights) if (pair.Key != null) pair.Key.enabled = pair.Value;
            survivorOriginalLights.Clear();
            if (survivorSun != null) { survivorSun.enabled = false; DestroyObject(survivorSun.gameObject); }
            if (survivorSkyFill != null) { survivorSkyFill.enabled = false; DestroyObject(survivorSkyFill.gameObject); }
            survivorSun = survivorSkyFill = survivorPreviousSun = null;
        }
    }
}
