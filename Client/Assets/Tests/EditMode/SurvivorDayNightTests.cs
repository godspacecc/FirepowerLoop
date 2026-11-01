using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>昼夜投影必须循环、冻结并归还原场景灯光，不能污染其它模式。</summary>
    public sealed class SurvivorDayNightTests
    {
        private ArcadeCombatViewController view;
        private GameObject root, originalObject;
        private Light original, oldSun;
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            oldSun = RenderSettings.sun;
            originalObject = new GameObject("ExistingDayLight", typeof(Light));
            original = originalObject.GetComponent<Light>(); original.type = LightType.Directional;
            original.intensity = .77f; RenderSettings.sun = original;
            root = new GameObject("DayNightTestRoot"); view = new ArcadeCombatViewController();
            typeof(ArcadeCombatViewController).GetField("root", Hidden).SetValue(view, root);
        }
        [TearDown] public void Cleanup()
        {
            view.End(); Object.DestroyImmediate(originalObject); RenderSettings.sun = oldSun;
        }
        private void Sample(double seconds)
        {
            var method = typeof(ArcadeCombatViewController).GetMethod("SetSurvivorTime");
            Assert.That(method, Is.Not.Null, "缺少本局时间到昼夜表现的入口");
            method.Invoke(view, new object[] {seconds});
            var visibility=typeof(ArcadeCombatViewController).GetMethod("SetSurvivorVisibility");
            Assert.That(visibility,Is.Not.Null,"缺少亮区投影入口");
            float night=(float)(.5-.5*System.Math.Cos(seconds/360*System.Math.PI*2));
            visibility.Invoke(view,new object[]{night,24-12*night,new Vector2(3,4)});
        }
        [Test] public void NightRemainsVisibleAndNewCycleReturnsToDay()
        {
            view.BeginSurvivorDayNight(); Sample(0);
            var sun = RenderSettings.sun; float day = sun.intensity; var color = sun.color;
            Sample(180); Assert.That(sun.intensity, Is.LessThan(day)); Assert.That(sun.intensity, Is.LessThan(.05f));
            Assert.That(sun.color.b, Is.GreaterThan(sun.color.r));
            Sample(360); Assert.That(sun.intensity, Is.EqualTo(day).Within(.0001)); Assert.That(sun.color, Is.EqualTo(color));
        }
        [Test] public void RepeatedAuthorityTimeFreezesLightAndPhaseBoundaryIsContinuous()
        {
            view.BeginSurvivorDayNight(); Sample(179.999);
            var sun = RenderSettings.sun; var color = sun.color; float brightness = sun.intensity;
            Sample(179.999); Assert.That(sun.intensity, Is.EqualTo(brightness)); Assert.That(sun.color, Is.EqualTo(color));
            Sample(180.001); Assert.That(Mathf.Abs(sun.intensity-brightness), Is.LessThan(.001f));
        }
        [Test] public void EndRestoresExistingLightAndDoesNotEnablePreviouslyDisabledLight()
        {
            original.enabled = false; view.BeginSurvivorDayNight(); Sample(180);
            Assert.That(RenderSettings.sun, Is.Not.EqualTo(original));
            view.End(); view.End(); Assert.That(RenderSettings.sun, Is.EqualTo(original));
            Assert.That(original.enabled, Is.False); Assert.That(original.intensity, Is.EqualTo(.77f));
        }
        [Test] public void LocalLightFollowsPlayerAndUsesSameGroundRadius()
        {
            view.BeginSurvivorDayNight();Sample(180);
            var lamp=root.transform.Find("SurvivorPlayerLight").GetComponent<Light>();
            Assert.That(lamp.type,Is.EqualTo(LightType.Spot));
            Assert.That(lamp.intensity,Is.GreaterThan(0));
            Assert.That(lamp.transform.position.x,Is.EqualTo(3).Within(.001));
            Assert.That(lamp.transform.position.z,Is.EqualTo(4).Within(.001));
            float radius=Mathf.Tan(lamp.spotAngle*Mathf.Deg2Rad/2)*12;
            Assert.That(radius,Is.EqualTo(12).Within(.001));
            Sample(0);Assert.That(lamp.intensity,Is.Zero);
        }
        [Test] public void EndRestoresAmbientAndReflectionAfterNight()
        {
            var mode=RenderSettings.ambientMode;var ambient=RenderSettings.ambientLight;
            float intensity=RenderSettings.ambientIntensity,reflection=RenderSettings.reflectionIntensity;
            view.BeginSurvivorDayNight();Sample(180);
            Assert.That(RenderSettings.ambientIntensity,Is.Zero);Assert.That(RenderSettings.reflectionIntensity,Is.Zero);
            view.End();Assert.That(RenderSettings.ambientMode,Is.EqualTo(mode));Assert.That(RenderSettings.ambientLight,Is.EqualTo(ambient));
            Assert.That(RenderSettings.ambientIntensity,Is.EqualTo(intensity));Assert.That(RenderSettings.reflectionIntensity,Is.EqualTo(reflection));
        }
        [Test] public void IncompleteStartStillRestoresTemporaryPipeline()
        {
            var previous=QualitySettings.renderPipeline;
            var source=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>("Assets/Settings/URP-Performant.asset");
            try
            {
                QualitySettings.renderPipeline=source;
                typeof(ArcadeCombatViewController).GetMethod("PrepareSurvivorLightingPipeline",Hidden).Invoke(view,null);
                Assert.That(QualitySettings.renderPipeline,Is.Not.SameAs(source));
                view.End();Assert.That(QualitySettings.renderPipeline,Is.SameAs(source));
            }
            finally{view.End();QualitySettings.renderPipeline=previous;}
        }
        [TestCase("Performant")][TestCase("Balanced")]
        public void LowerQualityUsesTemporaryPixelLightingAndRestoresPipeline(string quality)
        {
            var previous=QualitySettings.renderPipeline;
            var source=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>("Assets/Settings/URP-"+quality+".asset");
            string original=JsonUtility.ToJson(source);
            try
            {
                QualitySettings.renderPipeline=source;view.BeginSurvivorDayNight();Sample(180);
                Assert.That(QualitySettings.renderPipeline,Is.Not.SameAs(source));
                StringAssert.Contains("\"m_AdditionalLightsRenderingMode\":1",JsonUtility.ToJson(QualitySettings.renderPipeline));
                Assert.That(JsonUtility.ToJson(source),Is.EqualTo(original));
                view.End();Assert.That(QualitySettings.renderPipeline,Is.SameAs(source));
            }
            finally{view.End();QualitySettings.renderPipeline=previous;}
        }
        [Test] public void OtherModesIgnoreTimeAndRepeatedSetupDoesNotLeakLights()
        {
            Sample(180); Assert.That(RenderSettings.sun, Is.EqualTo(original)); Assert.That(original.enabled, Is.True);
            view.BeginSurvivorDayNight(); Sample(180);
            view.BeginSurvivorDayNight(); Sample(0);
            Assert.That(root.GetComponentsInChildren<Light>().Length, Is.EqualTo(3));
            Assert.That(original.enabled, Is.False); view.End(); Assert.That(original.enabled, Is.True);
        }
    }
}
