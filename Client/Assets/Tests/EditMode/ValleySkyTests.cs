using System;
using System.Reflection;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Tests.EditMode
{
    public sealed class ValleySkyTests
    {
        private const string MaterialPath = "Assets/Game/Art/Environment/CivilisationExpedition/Sky/ValleySky.mat";
        private GameObject skyObject;
        private GameObject lightObject;
        private Component sky;
        private DayNightView lighting;
        private Material template;
        private Material originalSky;
        private float originalReflection;

        [SetUp]
        public void SetUp()
        {
            originalSky = RenderSettings.skybox;
            originalReflection = RenderSettings.reflectionIntensity;
            var type = typeof(DayNightView).Assembly.GetType("Game.MapProceduralSkyView");
            Assert.That(type, Is.Not.Null, "程序化天空表现组件必须可用");
            skyObject = new GameObject("ValleySkyTest");
            sky = skyObject.AddComponent(type);
            lightObject = new GameObject("ValleyLightTest");
            lightObject.AddComponent<Light>().type = LightType.Directional;
            lighting = lightObject.AddComponent<DayNightView>();
            typeof(DayNightView).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(lighting, null);
            template = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Assert.That(template, Is.Not.Null, "正式天空资源必须随项目保存");
            type.GetField("skyTemplate", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(sky, template);
        }

        [TearDown]
        public void TearDown()
        {
            if (sky != null) Invoke("Release");
            if (skyObject != null) Object.DestroyImmediate(skyObject);
            if (lightObject != null) Object.DestroyImmediate(lightObject);
            RenderSettings.skybox = originalSky;
            RenderSettings.reflectionIntensity = originalReflection;
        }

        [TestCase(0.25f, 1f)]
        [TestCase(0.75f, -1f)]
        public void SunriseAndSunset_AreOppositeHorizons(float progress, float expectedX)
        {
            Initialize();
            Apply(progress);
            var direction = -(Vector3)RenderSettings.skybox.GetVector("_SunDirection");
            Assert.That(direction.x, Is.EqualTo(expectedX).Within(0.0001f));
            Assert.That(direction.y, Is.Zero.Within(0.0001f));
        }

        [TestCase(0.35f)]
        [TestCase(0.65f)]
        [TestCase(0f)]
        public void VisibleCelestialBody_AlignsWithLightingRegardlessOfEventOrder(float progress)
        {
            Initialize();
            lighting.Apply(progress);
            Apply(progress);
            var property = progress == 0f ? "_MoonDirection" : "_SunDirection";
            var incomingRay = ((Vector3)RenderSettings.skybox.GetVector(property)).normalized;
            Assert.That(Vector3.Dot(incomingRay, lightObject.transform.forward), Is.GreaterThan(0.9999f));
            lighting.Apply(progress);
            Assert.That(Vector3.Dot(incomingRay, lightObject.transform.forward), Is.GreaterThan(0.9999f));
        }

        [Test]
        public void NightStars_FadeOutAtDawnAndRemainHiddenAtNoon()
        {
            Initialize();
            Apply(0f);
            var night = RenderSettings.skybox.GetFloat("_StarsOpacity");
            Apply(0.24f);
            var dawn = RenderSettings.skybox.GetFloat("_StarsOpacity");
            Apply(0.5f);
            Assert.That(night, Is.GreaterThan(0.5f));
            Assert.That(dawn, Is.LessThan(night));
            Assert.That(RenderSettings.skybox.GetFloat("_StarsOpacity"), Is.Zero.Within(0.0001f));
        }

        [Test]
        public void Release_IsIdempotentRestoresEnvironmentAndLeavesTemplateUnchanged()
        {
            var templateColor = template.GetColor("_SkyColor");
            RenderSettings.reflectionIntensity = 0.83f;
            lighting.Apply(0.35f);
            var originalRotation = lightObject.transform.rotation;
            Initialize();
            Apply(0.35f);
            var instance = RenderSettings.skybox;
            Assert.That(instance, Is.Not.SameAs(template));
            Assert.That(RenderSettings.reflectionIntensity, Is.EqualTo(0.5f).Within(0.0001f));
            Invoke("Release");
            Invoke("Release");
            Assert.That(RenderSettings.skybox, Is.SameAs(originalSky));
            Assert.That(RenderSettings.reflectionIntensity, Is.EqualTo(0.83f).Within(0.0001f));
            Assert.That(Quaternion.Angle(originalRotation, lightObject.transform.rotation), Is.LessThan(0.001f));
            Assert.That(instance == null, Is.True, "运行时材质必须释放");
            Assert.That(template.GetColor("_SkyColor"), Is.EqualTo(templateColor));
            Initialize();
            Apply(0f);
            Assert.That(RenderSettings.skybox, Is.Not.SameAs(instance));
        }

        [Test]
        public void MissingMaterial_DoesNotTakeEnvironmentOrLightOwnership()
        {
            sky.GetType().GetField("skyTemplate", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(sky, null);
            Assert.That(Invoke("Initialize", lighting), Is.EqualTo(false));
            Assert.That(RenderSettings.skybox, Is.SameAs(originalSky));
            Assert.That(RenderSettings.reflectionIntensity, Is.EqualTo(originalReflection));
        }

        [TestCase(true, "模板")]
        [TestCase(false, "DayNightView")]
        public void MissingDependency_ReportsSpecificCauseWithoutChangingEnvironment(bool missingTemplate, string cause)
        {
            if (missingTemplate)
                sky.GetType().GetField("skyTemplate", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(sky, null);
            Assert.That(Invoke("Initialize", missingTemplate ? lighting : null), Is.EqualTo(false));
            Assert.That(ReadInitializationError(sky), Does.Contain(cause));
            Assert.That(RenderSettings.skybox, Is.SameAs(originalSky));
            Assert.That(RenderSettings.reflectionIntensity, Is.EqualTo(originalReflection));
        }

        [Test]
        public void CompetingSky_PreservesOldOwnershipAndCanBindAfterRelease()
        {
            Initialize();
            var oldSky = RenderSettings.skybox;
            var candidateObject = new GameObject("CandidateSkyTest");
            var candidate = candidateObject.AddComponent(sky.GetType());
            candidate.GetType().GetField("skyTemplate", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(candidate, template);
            var initialize = candidate.GetType().GetMethod("Initialize", BindingFlags.NonPublic | BindingFlags.Instance);
            var release = candidate.GetType().GetMethod("Release", BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                Assert.That(initialize.Invoke(candidate, new object[] { lighting }), Is.EqualTo(false));
                Assert.That(ReadInitializationError(candidate), Does.Contain("绑定"));
                Assert.That(RenderSettings.skybox, Is.SameAs(oldSky));
                Invoke("Release");
                Assert.That(initialize.Invoke(candidate, new object[] { lighting }), Is.EqualTo(true));
                Assert.That(ReadInitializationError(candidate), Is.Null.Or.Empty);
                release.Invoke(candidate, null);
                Assert.That(RenderSettings.skybox, Is.SameAs(originalSky));
            }
            finally
            {
                release.Invoke(candidate, null);
                Object.DestroyImmediate(candidateObject);
            }
        }

        private static string ReadInitializationError(Component target)
        {
            var property = target.GetType().GetProperty("InitializationError", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(property, Is.Not.Null, "失败原因必须可供天空启用日志读取");
            return (string)property.GetValue(target);
        }

        private void Initialize() => Assert.That(Invoke("Initialize", lighting), Is.EqualTo(true));
        private void Apply(float progress) => Invoke("Apply", progress);
        private object Invoke(string name, params object[] args) => sky.GetType()
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(sky, args);
    }
}
