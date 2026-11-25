using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>奖励表现不得漂移拾取点、重置渐隐或把快照移除误播成领取成功。</summary>
    public sealed class ArcadeTreasureRewardVisualTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private SurvivorCombatViewController view;
        private GameObject root;
        private ArcadeTreasureRoomParameters room;
        private Transform templates;
        private readonly FieldInfo singleton = typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
        private readonly FieldInfo languages = typeof(LocalizationLanguages).GetField("cachedConfig", BindingFlags.Static | BindingFlags.NonPublic);
        private object previousArchitecture, previousLanguages;
        private IResourceLoader previousLoader;

        [SetUp] public void Setup()
        {
            previousArchitecture = singleton.GetValue(null); previousLanguages = languages.GetValue(null);
            previousLoader = ResourceLoader.Current; ResourceLoader.Use(new LocalizationLoader()); LocalizationLanguages.ResetForTests();
            var architecture = new Game(); singleton.SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", Flags).SetValue(architecture, true);
            architecture.RegisterModel(new LocalizationModel()); architecture.RegisterUtility(new LocalizationUtility(false));
            root = new GameObject("RewardVisualTest");
            templates = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content");
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes"))));
            room = ArcadeTreasureRoomParameters.FromConfig(tables.TbArcadeTreasureRoom.Get("arcade.armory.second"));
            view = new SurvivorCombatViewController();
            Set("root", root); Set("mapRoot", root.transform); Set("contentTemplates", templates);
        }

        [TearDown] public void Cleanup()
        {
            view?.End(); if (root != null) Object.DestroyImmediate(root);
            singleton.SetValue(null, previousArchitecture); languages.SetValue(null, previousLanguages); ResourceLoader.Use(previousLoader);
        }

        [Test] public void FlashIsBoundedAndReleasedOnRoomExit()
        {
            view.SetTreasureRoom(room, true, true, true, Array.Empty<int>());
            for (int i = 0; i < 12; i++) Invoke("EmitTreasurePickupFlash", new Vector2(2, 3));
            var particles = Flashes();
            Assert.That(particles.Length, Is.EqualTo(1), "连拾共用一个特效实例");
            Assert.That(particles[0].particleCount, Is.InRange(1, 32));
            Assert.That(particles[0].isPlaying, Is.True, "运行帧必须自动模拟，不能依赖预览脚本手动 Simulate");
            Assert.That(particles[0].main.startLifetime.constantMax, Is.LessThanOrEqualTo(.3f));
            particles[0].Simulate(.7f, false, false, false);
            Assert.That(particles[0].particleCount, Is.Zero, "短闪光应在自身寿命后结束");
            particles[0].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            Invoke("EmitTreasurePickupFlash", new Vector2(3, 4));
            Assert.That(particles[0].isPlaying, Is.True, "闲置后的再次领取恢复自动模拟");
            Assert.That(particles[0].particleCount, Is.GreaterThan(0));
            view.SetTreasureRoom(room, false, true, true, Array.Empty<int>());
            Assert.That(Flashes(), Is.Empty);
            Invoke("EmitTreasurePickupFlash", Vector2.zero);
            Assert.That(Flashes(), Is.Empty, "房外不能重建奖励闪光");
        }

        [Test] public void ZeroRewardDoesNotEmitFlash()
        {
            view.SetTreasureRoom(room, true, true, true, Array.Empty<int>());
            view.PlayTreasurePickup(0, 0);
            Assert.That(Flashes(), Is.Empty);
        }

        [Test] public void RoomDropSizingKeepsRootAndDoesNotAccumulateAcrossRefreshes()
        {
            var method = typeof(ResourcePickupView).GetMethod("FitArcadeVisual");
            Assert.That(method, Is.Not.Null);
            foreach (string name in new[] { "ArcadeDropBomb", "ArcadeDropDash", "ArcadeRifle", "ArcadePiercingGun", "ArcadePanzerschreck", "ArcadeGrenade" })
            {
                var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/TestResourcePickup.prefab"), root.transform);
                try
                {
                    instance.transform.position = new Vector3(2, .3f, 3);
                    var pickup = instance.GetComponent<ResourcePickupView>();
                    pickup.SetArcadePresentation(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/" + name + ".prefab"), "Prefabs/SceneObjects/" + name);
                    method.Invoke(pickup, new object[] { 1.1f });
                    var visual = instance.transform.Find("ArcadeDropVisual");
                    var scale = visual.localScale; var position = visual.localPosition;
                    var renderers = visual.GetComponentsInChildren<Renderer>(); var bounds = renderers[0].bounds;
                    foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    Assert.That(Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z), Is.EqualTo(1.1f).Within(.01f), name);
                    Assert.That(bounds.min.y, Is.EqualTo(.3f).Within(.01f), name);
                    method.Invoke(pickup, new object[] { 1.1f });
                    Assert.That(Vector3.Distance(visual.localScale, scale), Is.LessThan(.001f), name);
                    Assert.That(Vector3.Distance(visual.localPosition, position), Is.LessThan(.001f), name);
                    Assert.That(instance.transform.position, Is.EqualTo(new Vector3(2, .3f, 3)), "不能移动领取根");
                }
                finally { Object.DestroyImmediate(instance); }
            }
        }

        private ParticleSystem[] Flashes() => root.GetComponentsInChildren<ParticleSystem>().Where(p => p.name == "TreasurePickupFlash").ToArray();
        private void Set(string name, object value) => typeof(SurvivorCombatViewController).GetField(name, Flags).SetValue(view, value);
        private void Invoke(string name, params object[] args)
        {
            var method = typeof(SurvivorCombatViewController).GetMethod(name, Flags);
            Assert.That(method, Is.Not.Null, name); method.Invoke(view, args);
        }

        private sealed class LocalizationLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Config/Localization/" + path);
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> callback) where T : Object
            { callback(LoadAsset<T>(path)); return ResourceLoadHandle.Empty; }
            public void Release(ResourceLoadHandle handle) { }
        }
    }
}
