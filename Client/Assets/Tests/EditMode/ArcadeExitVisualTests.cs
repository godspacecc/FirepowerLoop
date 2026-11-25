using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>出口正式资源、权威落点及显示/隐藏回归；不改产品资源或玩家档。</summary>
    public sealed class ArcadeExitVisualTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly FieldInfo singleton = typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
        private object previous;
        private IResourceLoader previousLoader;
        private object previousLanguages;
        private readonly FieldInfo languageCache = typeof(LocalizationLanguages).GetField("cachedConfig", BindingFlags.Static | BindingFlags.NonPublic);
        private SurvivorCombatViewController view;
        private GameObject root;

        [SetUp] public void Setup()
        {
            previous = singleton.GetValue(null);
            previousLoader = ResourceLoader.Current;
            previousLanguages = languageCache.GetValue(null);
            ResourceLoader.Use(new TestLoader());
            LocalizationLanguages.ResetForTests();
            var architecture = new Game();
            singleton.SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", Hidden).SetValue(architecture, true);
            architecture.RegisterModel(new LocalizationModel());
            var localization = new LocalizationUtility(false);
            localization.LoadFromJson("zh-CN", File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Localization/zh-CN.json")));
            architecture.RegisterUtility(localization);
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() => new Tables(name =>
                new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes"))))));
            root = new GameObject("ExitVisualTest");
            view = new SurvivorCombatViewController();
            Set("root", root);
            Set("contentArena", 1);
            Set("contentTemplates", Bank().transform.Find("Content"));
        }

        [TearDown] public void Cleanup()
        {
            view?.End();
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            singleton.SetValue(null, previous);
            ResourceLoader.Use(previousLoader);
            languageCache.SetValue(null, previousLanguages);
        }

        private void Set(string name, object value) => typeof(SurvivorCombatViewController).GetField(name, Hidden).SetValue(view, value);
        private static GameObject Bank() => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab");

        private sealed class TestLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : UnityEngine.Object =>
                AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Config/Localization/" + path);
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> callback) where T : UnityEngine.Object
            { callback(LoadAsset<T>(path)); return ResourceLoadHandle.Empty; }
            public void Release(ResourceLoadHandle handle) { }
        }

        [Test] public void FormalGateHasVisibleDirectionAndTriggerRingWithoutBlockingMovement()
        {
            var gate = Bank().transform.Find("Content/RouteGate");
            Assert.That(gate.Find("ExitCue/Arrow"), Is.Not.Null, "清场出口必须有方向箭头");
            var ring = gate.Find("ExitCue/GroundRing")?.GetComponent<LineRenderer>();
            Assert.That(ring, Is.Not.Null, "出口触发区域必须有高对比地面光圈");
            Assert.That(ring.loop, Is.True);
            Assert.That(ring.sharedMaterial, Is.Not.Null);
            Assert.That(gate.GetComponentsInChildren<Collider>(true), Is.Empty);
        }

        [Test] public void OrdinaryExitKeepsArrowsWithoutGateSceneEffects()
        {
            view.SetExits(new[] { -2 }, 19.5f, false);
            var marker = root.transform.Find("ArcadeRouteExit_-2");
            Assert.That(marker, Is.Not.Null, "出口仍需要指路箭头");
            var position = new Vector2(0, 16.5f);
            Assert.That(marker.localPosition, Is.EqualTo(new Vector3(position.x, 0, position.y)));
            Assert.That(marker.GetComponentsInChildren<MeshRenderer>(true), Is.Empty, "不能重新带回门框和牌子");
            Assert.That(marker.GetComponentsInChildren<TextMeshPro>(true), Is.Empty);
            Assert.That(marker.GetComponentsInChildren<ParticleSystem>(true), Is.Empty);
            Assert.That(marker.Find("ExitCue/GroundRing"), Is.Null);
            var arrow = marker.Find("ExitCue/Arrow");
            var approach = marker.Find("ExitCue/ApproachArrow");
            Assert.That(arrow, Is.Not.Null);
            Assert.That(approach, Is.Not.Null);
            var before = arrow.localPosition;
            view.TickPlayerFeedback(.25f);
            Assert.That(arrow.localPosition, Is.Not.EqualTo(before));
            view.SetPlayer(new Vector2(-3, -8), Vector2.up, true, false);
            Assert.That(approach.gameObject.activeSelf, Is.True);
            Assert.That(Vector3.Distance(approach.position, new Vector3(-3, .28f, -8)), Is.EqualTo(2.3f).Within(.01f));
            Assert.That(Vector3.Dot(approach.forward, new Vector3(position.x + 3, 0, position.y + 8).normalized), Is.GreaterThan(.99f));
            view.SetPlayer(position, Vector2.up, true, false);
            Assert.That(approach.gameObject.activeSelf, Is.False);
            view.SetExits(new[] { -2 }, 19.5f, false);
            Assert.That(root.transform.Find("ArcadeRouteExit_-2"), Is.SameAs(marker));
            view.SetExits(Array.Empty<int>(), 19.5f, false);
            Assert.That(root.transform.childCount, Is.Zero, "下一轮清理箭头");
            Assert.DoesNotThrow(() => view.TickPlayerFeedback(.25f));
        }
        [Test] public void TreasureEntranceGuidesPlayerAcrossExitSyncAndCleansUpOnEntry()
        {
            Assert.That(Game.Interface.GetUtility<IRuntimeConfigProvider>().TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            var room = catalog.TreasureRooms.First();
            view.SetTreasureRoom(room, false, false, false, Array.Empty<int>());
            Assert.That(root.transform.Find("RouteGate(Clone)"), Is.Null, "清场前未发现的入口保持隐藏");
            view.SetTreasureRoom(room, false, true, true, Array.Empty<int>());
            var gate = root.transform.Find("RouteGate(Clone)");
            Assert.That(gate, Is.Not.Null);
            var gatePosition = gate.localPosition;
            var arrow = gate.Find("ExitCue/Arrow");
            var before = arrow.localPosition;
            // 普通出口同步不能抹掉奖励房指路，重复奖励房快照也不能复制箭头。
            view.SetExits(Array.Empty<int>(), 19.5f, false);
            view.SetTreasureRoom(room, false, true, true, Array.Empty<int>());
            view.SetPlayer(Vector2.zero, Vector2.up, true, false);
            var guide = gate.Find("ExitCue/ApproachArrow");
            Assert.That(guide.gameObject.activeSelf, Is.True);
            Assert.That(Vector3.Distance(guide.position, new Vector3(0, .28f, 0)), Is.EqualTo(2.3f).Within(.01f));
            Assert.That(Vector3.Dot(guide.forward, new Vector3(gatePosition.x, 0, gatePosition.z).normalized), Is.GreaterThan(.99f));
            view.TickPlayerFeedback(.25f);
            Assert.That(arrow.localPosition, Is.Not.EqualTo(before));
            Assert.That(gate.localPosition, Is.EqualTo(gatePosition), "引导动画不能移动权威入口落点");
            view.SetPlayer(new Vector2(gatePosition.x, gatePosition.z), Vector2.up, true, false);
            Assert.That(guide.gameObject.activeSelf, Is.False, "到达入口后收起近身箭头");
            view.SetPlayer(Vector2.zero, Vector2.up, false, false);
            Assert.That(guide.gameObject.activeSelf, Is.False, "玩家隐藏时不残留箭头");
            Assert.That(root.GetComponentsInChildren<Transform>().Count(t => t.name == "RouteGate(Clone)"), Is.EqualTo(1));
            view.SetTreasureRoom(room, true, true, true, Array.Empty<int>());
            Assert.That(gate == null, Is.True, "入房时必须销毁外部入口");
            Assert.DoesNotThrow(() => { view.SetPlayer(Vector2.zero, Vector2.up, true, false); view.TickPlayerFeedback(.25f); });
            view.SetTreasureRoom(room, false, false, false, Array.Empty<int>());
            Assert.That(root.transform.Find("RouteGate(Clone)"), Is.Null, "第四轮不能残留旧宝藏入口");
            Assert.DoesNotThrow(() => view.TickPlayerFeedback(.25f));
        }

        [Test] public void EveryThemeKeepsOnlyOrdinaryExitArrows()
        {
            for (int arena = 1; arena <= 20; arena++)
            {
                Set("contentArena", arena);
                view.SetExits(new[] { arena + 1 }, 19.5f, false);
                Assert.That(root.transform.childCount, Is.EqualTo(1), "各主题都需要保留指路：" + arena);
                Assert.That(root.GetComponentsInChildren<MeshRenderer>(true), Is.Empty);
                Assert.That(root.GetComponentsInChildren<ParticleSystem>(true), Is.Empty);
                view.SetExits(Array.Empty<int>(), 19.5f, false);
            }
        }
    }
}
