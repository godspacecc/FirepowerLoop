using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    /// <summary>以正式共享底图和控制器覆盖轮级显隐交接，防止每帧释放旧表现时重新露出测试围墙。</summary>
    public sealed class ArcadeArenaBoundaryTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly string[] WallNames = { "NorthBoundary", "SouthBoundary", "EastBoundary", "WestBoundary" };
        private Scene scene, priorScene;
        private MapContext map;
        private ArcadeCombatViewController view;
        private ArcadeConfigCatalogDefinition catalog;
        private IResourceLoader previousLoader;
        private FormalLoader loader;
        private GameObject root;
        private Renderer[] baseRenderers;

        [SetUp]
        public void Setup()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(n => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", n + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog, out var error), Is.True, error);
            previousLoader = ResourceLoader.Current; loader = new FormalLoader(); ResourceLoader.Use(loader);
            priorScene = SceneManager.GetActiveScene();
            scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/TaskSampleMap.unity", OpenSceneMode.Additive);
            map = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MapContext>(true)).Single();
            map.SetMapContentActive(true);
            baseRenderers = map.GetComponentsInChildren<Renderer>(true).Where(r => WallNames.Contains(r.name) || r.name == "Ground").ToArray();
            Assert.That(baseRenderers.Length, Is.EqualTo(5), "正式共享底图应含四面旧墙及底板");
            view = new ArcadeCombatViewController();
        }

        [TearDown]
        public void Cleanup()
        {
            view?.End(); ResourceLoader.Use(previousLoader);
            SceneManager.SetActiveScene(priorScene);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }

        private void Set(string name, object value) => typeof(ArcadeCombatViewController).GetField(name, Hidden).SetValue(view, value);
        private void Invoke(string name, params object[] args) => typeof(ArcadeCombatViewController).GetMethod(name, Hidden).Invoke(view, args);
        private void Start(ArcadeRoundLayoutParameters layout)
        {
            view.End(); root = new GameObject("BoundaryRegressionRuntime"); root.transform.SetParent(map.DynamicObjectRoot, false);
            Set("root", root); Set("islandBaseArenaRoot", map.transform); Set("contentArena", layout.ArenaConfigNumber);
            var stage = catalog.EarlyStages.FirstOrDefault(p => p.ArenaConfigNumber == layout.ArenaConfigNumber);
            Set("earlyStage", stage);
            view.ConfigureRoundBounds(stage?.HalfWidth ?? catalog.Rule.ArenaHalfWidth, stage?.HalfHeight ?? catalog.Rule.ArenaHalfHeight, catalog.Rule.PlayerRadius);
            view.SetIslandRound(layout.ArenaConfigNumber == 1 ? layout.RoundNumber : 0);
            view.SetRoundLayout(layout);
        }
        private void Start(int arena) => Start(catalog.RoundLayouts.First(p => p.ArenaConfigNumber == arena));
        private void AssertBaseHidden() => Assert.That(baseRenderers.Where(r => r.enabled).Select(r => r.name), Is.Empty, "已准备好的地图不能露出共享底图");

        [Test]
        public void EveryConfiguredRoundKeepsBaseHiddenAcrossRepeatedSnapshots()
        {
            var exposed = new List<string>();
            Assert.That(catalog.RoundLayouts, Is.Not.Empty);
            foreach (var layout in catalog.RoundLayouts)
            {
                Start(layout); Assert.That(view.IsRoundLayoutReady, Is.True, layout.Id + " " + view.RoundLayoutLoadError);
                var opening = catalog.Arenas.Single(a => a.ArenaNumber == layout.ArenaConfigNumber).Opening;
                for (int i = 0; i < 3; i++) view.SetOpening(opening, false);
                if (baseRenderers.Any(r => r.enabled)) exposed.Add(layout.Id);
            }
            Assert.That(exposed, Is.Empty, "全部正式轮布局都必须保持覆盖：" + string.Join(",", exposed));
        }

        [TestCase(21)]
        [TestCase(22)]
        public void ConfirmedEnvironmentTakesOverOnlyAfterAllResourcesAreReady(int arena)
        {
            loader.DelayEnvironment = true; Start(arena);
            Assert.That(view.IsRoundLayoutReady, Is.False);
            view.SetOpening(default, false);
            Assert.That(baseRenderers.All(r => r.enabled), Is.True, "资源准备前保留旧底图兜底");
            loader.CompleteEnvironment(); Assert.That(view.IsRoundLayoutReady, Is.True, view.RoundLayoutLoadError);
            view.SetOpening(default, false); AssertBaseHidden();
        }

        [Test]
        public void ShopRegionKeepsBaseHiddenWhenOpeningIsDisabled()
        {
            Start(5);
            view.SetSlayerMartRegion(new ArcadeSlayerMartRegionLayout(1, 8, 6, 0, 0, 0, -4, 1, new[] { new ArcadeSlayerMartPoint(2, 2) }), new[] { 1 });
            view.SetOpening(default, false); AssertBaseHidden();
            Assert.That(root.GetComponentsInChildren<Renderer>(true).Any(r => r.name == "NorthWall" && r.enabled), Is.True, "保留商店本身的正式墙体");
        }

        [Test]
        public void ContentLoadFailureCannotExposeAnAlreadyReadyEnvironment()
        {
            Start(21); AssertBaseHidden(); loader.FailContent = true;
            LogAssert.Expect(LogType.Error, new Regex(".*前两场内容加载失败.*"));
            Invoke("LoadContentAsync", 21);
            Assert.That(view.IsRoundLayoutReady, Is.True, "公共效果加载失败不能撤销已准备好的码头环境");
            AssertBaseHidden();
        }

        [Test]
        public void RoomRoundTripKeepsBaseHiddenAndFinalReleaseRestoresOriginalStates()
        {
            baseRenderers[0].enabled = false;
            var original = baseRenderers.Select(r => r.enabled).ToArray();
            Start(21);
            Set("contentTemplates", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content"));
            view.SetTreasureRoom(null, true, false, false, Array.Empty<int>());
            view.SetOpening(default, false); AssertBaseHidden();
            view.SetTreasureRoom(null, false, false, false, Array.Empty<int>());
            view.SetOpening(default, false); AssertBaseHidden();
            Assert.That(root.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled), Is.True, "本轮正式环境仍然可见");
            view.End(); view.End();
            Assert.That(baseRenderers.Select(r => r.enabled), Is.EqualTo(original), "重复释放逐项恢复原始状态");
        }
        /// <summary>只替换资源调度边界；场景、Prefab和全部显隐逻辑均使用正式实现。</summary>
        private sealed class FormalLoader : IResourceLoader
        {
            public bool DelayEnvironment, FailContent;
            private readonly List<Action> pending = new();
            public T LoadAsset<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>("Assets/Game/" + path + ".prefab");
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> complete) where T : UnityEngine.Object
            {
                bool environment = path.EndsWith("ArcadeDocksVisual", StringComparison.Ordinal) || path.EndsWith("ArcadeFarmVisual", StringComparison.Ordinal) || path.EndsWith("ArcadeHerdVisual", StringComparison.Ordinal);
                if (DelayEnvironment && environment) pending.Add(() => complete(LoadAsset<T>(path)));
                else complete(FailContent && path.EndsWith("ArcadeObstacleVisualSet", StringComparison.Ordinal) ? null : LoadAsset<T>(path));
                return ResourceLoadHandle.Empty;
            }
            public void CompleteEnvironment() { foreach (var callback in pending.ToArray()) callback(); pending.Clear(); }
            public void Release(ResourceLoadHandle handle) { }
        }
    }
}
