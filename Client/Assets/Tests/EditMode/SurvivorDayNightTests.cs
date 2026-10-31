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
        }
        [Test] public void NightRemainsVisibleAndNewCycleReturnsToDay()
        {
            view.BeginSurvivorDayNight(); Sample(0);
            var sun = RenderSettings.sun; float day = sun.intensity; var color = sun.color;
            Sample(180); Assert.That(sun.intensity, Is.LessThan(day)); Assert.That(sun.intensity, Is.GreaterThan(.3f));
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
        [Test] public void OtherModesIgnoreTimeAndRepeatedSetupDoesNotLeakLights()
        {
            Sample(180); Assert.That(RenderSettings.sun, Is.EqualTo(original)); Assert.That(original.enabled, Is.True);
            view.BeginSurvivorDayNight(); Sample(180);
            view.BeginSurvivorDayNight(); Sample(0);
            Assert.That(root.GetComponentsInChildren<Light>().Length, Is.EqualTo(2));
            Assert.That(original.enabled, Is.False); view.End(); Assert.That(original.enabled, Is.True);
        }
    }
}
