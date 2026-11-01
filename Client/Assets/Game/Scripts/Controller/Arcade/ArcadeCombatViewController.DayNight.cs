using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>与战斗权威共用六分钟周期，View不自行推进时间。</summary>
        private const double SurvivorDayNightSeconds = SurvivorDayNightRules.CycleSeconds;
        /// <summary>仅保存被本局临时关闭的方向光，退出时逐一归还。</summary>
        private readonly Dictionary<Light, bool> survivorOriginalLights = new();
        /// <summary>本局根拥有的主光与天空补光；不持有资源加载句柄。</summary>
        private Light survivorSun, survivorSkyFill;
        /// <summary>主角局部照明只读取System投影的半径与位置。</summary>
        private Light survivorPlayerLight;
        /// <summary>全局环境设置的归还快照。</summary>
        private bool survivorOwnsAmbient;
        private UnityEngine.Rendering.AmbientMode survivorAmbientMode;
        private Color survivorAmbientColor;
        private float survivorAmbientIntensity, survivorReflectionIntensity;
        /// <summary>未压暗前的主光/补光强度，重复投影不能累乘变黑。</summary>
        private float survivorBaseSun, survivorBaseSky;
        /// <summary>采用预览的灯高；角度由实际攻击半径推导。</summary>
        private const float SurvivorLampHeight = 12;
        /// <summary>只为本局复制不支持局部逐像素灯光的管线，保留原画质资源。</summary>
        private UnityEngine.Rendering.RenderPipelineAsset survivorPreviousPipeline, survivorLightingPipeline;
        /// <summary>对应URP 14序列化的附加灯设置；Json覆盖只改副本中的这两项。</summary>
        [Serializable] private sealed class SurvivorPipelineLighting
        {
            // URP的0/2分别为关闭/逐顶点；1为逐像素。
            public int m_AdditionalLightsRenderingMode;
            // 每个物体至少容纳局部灯与补光。
            public int m_AdditionalLightsPerObjectLimit;
        }
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
            PrepareSurvivorLightingPipeline();
            survivorPreviousSun = RenderSettings.sun;
            survivorOwnsAmbient = true;
            survivorAmbientMode = RenderSettings.ambientMode;
            survivorAmbientColor = RenderSettings.ambientLight;
            survivorAmbientIntensity = RenderSettings.ambientIntensity;
            survivorReflectionIntensity = RenderSettings.reflectionIntensity;
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
            survivorPlayerLight = CreateSurvivorLight("SurvivorPlayerLight", new Vector3(90,0,0));
            survivorPlayerLight.type = LightType.Spot;
            survivorPlayerLight.color = new Color(.65f,.74f,1);
            survivorPlayerLight.shadows = LightShadows.None;
            survivorPlayerLight.intensity = 0;
            RenderSettings.sun = survivorSun;
            SetSurvivorTime(0);
        }

        /// <summary>低/中画质也使用与预览一致的局部照明，原资源和其它画质参数保持不变。</summary>
        private void PrepareSurvivorLightingPipeline()
        {
            var source = QualitySettings.renderPipeline ?? UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            if (source == null || source.GetType().FullName != "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset") return;
            var settings = JsonUtility.FromJson<SurvivorPipelineLighting>(JsonUtility.ToJson(source));
            if (settings.m_AdditionalLightsRenderingMode == 1 && settings.m_AdditionalLightsPerObjectLimit >= 4) return;
            survivorPreviousPipeline = QualitySettings.renderPipeline;
            survivorLightingPipeline = UnityEngine.Object.Instantiate(source);
            survivorLightingPipeline.name = "SurvivorLightingPipeline";
            survivorLightingPipeline.hideFlags = HideFlags.HideAndDontSave;
            settings.m_AdditionalLightsRenderingMode = 1;
            settings.m_AdditionalLightsPerObjectLimit = Mathf.Max(4,settings.m_AdditionalLightsPerObjectLimit);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(settings),survivorLightingPipeline);
            QualitySettings.renderPipeline = survivorLightingPipeline;
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
            survivorBaseSun = Mathf.Lerp(SurvivorLightStrengths[segment],SurvivorLightStrengths[segment+1],blend);
            survivorSun.intensity = survivorBaseSun;
            survivorSun.transform.rotation = Quaternion.Euler(Mathf.Lerp(segment==1?20:48,segment+1==1?20:48,blend),-45,0);
            survivorBaseSky = Mathf.Lerp(segment==2?.25f:.40f,segment+1==2?.25f:.40f,blend);
            survivorSkyFill.intensity = survivorBaseSky;
        }

        /// <summary>主角周围保持可见，远处随夜色压暗；攻击范围由System唯一决定。</summary>
        public void SetSurvivorVisibility(float night, float radius, Vector2 center)
        {
            if (!survivorOwnsAmbient || survivorSun == null || survivorPlayerLight == null ||
                float.IsNaN(night) || float.IsInfinity(night) || float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0 ||
                float.IsNaN(center.x) || float.IsInfinity(center.x) || float.IsNaN(center.y) || float.IsInfinity(center.y)) return;
            night = Mathf.Clamp01(night);
            survivorSun.intensity = Mathf.Lerp(survivorBaseSun,.015f,night);
            survivorSkyFill.intensity = Mathf.Lerp(survivorBaseSky,.005f,night);
            RenderSettings.ambientMode = night <= 0 ? survivorAmbientMode : UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.Lerp(survivorAmbientColor,Color.black,night);
            RenderSettings.ambientIntensity = Mathf.Lerp(survivorAmbientIntensity,0,night);
            RenderSettings.reflectionIntensity = Mathf.Lerp(survivorReflectionIntensity,0,night);
            var point = new Vector3(center.x,SurvivorLampHeight,center.y);
            survivorPlayerLight.transform.position = mapRoot != null ? mapRoot.TransformPoint(point) : point;
            survivorPlayerLight.transform.rotation = (mapRoot != null ? mapRoot.rotation : Quaternion.identity) * Quaternion.Euler(90,0,0);
            survivorPlayerLight.range = Mathf.Sqrt(radius*radius+SurvivorLampHeight*SurvivorLampHeight)+10;
            survivorPlayerLight.spotAngle = 2*Mathf.Atan(radius/SurvivorLampHeight)*Mathf.Rad2Deg;
            survivorPlayerLight.innerSpotAngle = 2*Mathf.Atan(radius*.7f/SurvivorLampHeight)*Mathf.Rad2Deg;
            survivorPlayerLight.intensity = 147*night;
        }

        /// <summary>先归还全局光照，再由 End 销毁本局根；重复释放不触碰其它模式。</summary>
        private void ReleaseSurvivorDayNight()
        {
            if (survivorLightingPipeline != null)
            {
                if (QualitySettings.renderPipeline == survivorLightingPipeline) QualitySettings.renderPipeline = survivorPreviousPipeline;
                DestroyObject(survivorLightingPipeline);
                survivorLightingPipeline = survivorPreviousPipeline = null;
            }
            if (!survivorOwnsAmbient) return;
            survivorOwnsAmbient = false;
            RenderSettings.ambientMode = survivorAmbientMode;
            RenderSettings.ambientLight = survivorAmbientColor;
            RenderSettings.ambientIntensity = survivorAmbientIntensity;
            RenderSettings.reflectionIntensity = survivorReflectionIntensity;
            if (RenderSettings.sun == survivorSun) RenderSettings.sun = survivorPreviousSun;
            foreach (var pair in survivorOriginalLights) if (pair.Key != null) pair.Key.enabled = pair.Value;
            survivorOriginalLights.Clear();
            if (survivorSun != null) { survivorSun.enabled = false; DestroyObject(survivorSun.gameObject); }
            if (survivorSkyFill != null) { survivorSkyFill.enabled = false; DestroyObject(survivorSkyFill.gameObject); }
            if (survivorPlayerLight != null) { survivorPlayerLight.enabled = false; DestroyObject(survivorPlayerLight.gameObject); }
            survivorSun = survivorSkyFill = survivorPreviousSun = survivorPlayerLight = null;
        }
    }
}
