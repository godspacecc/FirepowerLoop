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
        private ArcadeCombatViewController view;
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
            view = new ArcadeCombatViewController();
            Set("root", root);
            Set("contentArena", 1);
            Set("contentTemplates", Bank().transform.Find("Content"));
            Set("islandRound", 1);
        }

        [TearDown] public void Cleanup()
        {
            view?.End();
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            singleton.SetValue(null, previous);
            ResourceLoader.Use(previousLoader);
            languageCache.SetValue(null, previousLanguages);
        }

        private void Set(string name, object value) => typeof(ArcadeCombatViewController).GetField(name, Hidden).SetValue(view, value);
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

        [Test] public void RoundExitKeepsAuthorityPositionWhileCueAnimatesAndRepeatedSyncDoesNotDuplicate()
        {
            view.SetExits(new[] { -2 }, 19.5f, false);
            var marker = root.transform.Find("ArcadeRouteExit_-2");
            var position = ArcadeIslandLayout.GetExitPosition(1, 0, 1, 19.5f, 19.5f, .55f);
            Assert.That(Vector2.Distance(new Vector2(marker.localPosition.x, marker.localPosition.z), position), Is.LessThan(.01f));
            Assert.That(marker.GetComponentInChildren<TextMeshPro>(true).text, Is.EqualTo("进入下一轮"));
            var arrow = marker.Find("ExitCue/Arrow");
            Assert.That(arrow, Is.Not.Null);
            var before = arrow.localPosition;
            var gatePosition = marker.localPosition;
            view.TickPlayerFeedback(.25f);
            Assert.That(arrow.localPosition, Is.Not.EqualTo(before));
            Assert.That(marker.localPosition, Is.EqualTo(gatePosition), "动画不得移动实际入口位置");
            view.SetPlayer(new Vector2(-3, -8), Vector2.up, true, false);
            var guide = marker.Find("ExitCue/ApproachArrow");
            Assert.That(guide.gameObject.activeSelf, Is.True, "远离出口时角色附近仍须有指路箭头");
            Assert.That(Vector3.Distance(guide.position, new Vector3(-3, .28f, -8)), Is.EqualTo(2.3f).Within(.01f));
            view.SetPlayer(position, Vector2.up, true, false);
            Assert.That(guide.gameObject.activeSelf, Is.False, "到达门口后不遮挡地面光圈");
            view.SetExits(new[] { -2 }, 19.5f, false);
            Assert.That(root.GetComponentsInChildren<Transform>().Count(t => t.name == "ArcadeRouteExit_-2"), Is.EqualTo(1));
            view.SetExits(Array.Empty<int>(), 19.5f, false);
            Assert.That(root.transform.childCount, Is.Zero, "下一轮必须清除旧出口及特效");
            Assert.DoesNotThrow(() => view.TickPlayerFeedback(.25f));
        }

        [Test] public void FourthRoundStillPresentsBothRouteChoices()
        {
            Set("islandRound", 4);
            view.SetExits(new[] { 2, 3 }, 19.5f, false);
            Assert.That(root.transform.childCount, Is.EqualTo(2));
            foreach (int destination in new[] { 2, 3 })
            {
                var gate = root.transform.Find("ArcadeRouteExit_" + destination);
                Assert.That(gate.Find("ExitCue/Arrow"), Is.Not.Null);
                Assert.That(gate.GetComponentInChildren<TextMeshPro>(true).text, Is.Not.Empty);
            }
        }

        [Test] public void NextRoundPlaqueUsesGenericTextAndFitsBothLanguages()
        {
            view.SetExits(new[] { -2 }, 19.5f, false);
            var label = root.transform.Find("ArcadeRouteExit_-2").GetComponentInChildren<TextMeshPro>(true);
            foreach (var language in new[] { "zh-CN", "en-US" })
            {
                Assert.That(RuntimeJson.TryParseStringTable(File.ReadAllText(Path.Combine(Application.dataPath,
                    "Game/Config/Localization/" + language + ".json")), out var table), Is.True);
                label.text = table["ui.arcade.exit.enter_round"];
                Assert.That(label.text, Is.EqualTo(language == "zh-CN" ? "进入下一轮" : "Next Round"));
                label.ForceMeshUpdate(true, true);
                Assert.That(label.isTextOverflowing, Is.False, "缩小后的文字不能溢出底板：" + language);
                Assert.That(label.preferredWidth, Is.LessThanOrEqualTo(label.rectTransform.rect.width));
                Assert.That(label.preferredHeight, Is.LessThanOrEqualTo(label.rectTransform.rect.height));
            }
        }

        [Test] public void FormalExitUsesOnlyLoadedTemplateMaterials()
        {
            view.SetExits(new[] { -2 }, 19.5f, false);
            Assert.That(root.transform.Find("ArcadeRouteExit_-2"), Is.Not.Null);
            Assert.That(typeof(ArcadeCombatViewController).GetField("exitMaterial", Hidden).GetValue(view),
                Is.Null, "正式门不需要备用圆柱材质，不能因按名称查找 Shader 失败而阻断创建");
        }

        [Test] public void EntryWarningWaitsForLoadedMaterialsAndResumesOnNextSnapshot()
        {
            Set("contentTemplates", null);
            Assert.DoesNotThrow(() => view.SetIslandEntryWarning(new Vector2(2, 4), true));
            Assert.That(root.transform.Find("IslandEntryWarning"), Is.Null,
                "模板未就绪时延迟表现，不能回退到按名称查找 Shader");
            Set("contentTemplates", Bank().transform.Find("Content"));
            view.SetIslandEntryWarning(new Vector2(2, 4), true);
            Assert.That(root.transform.Find("IslandEntryWarning").gameObject.activeSelf, Is.True);
        }

        [Test] public void EntryWarningPreservesLoadedShaderAndRenderSettingsWithoutMutatingTemplate()
        {
            var templates = UnityEngine.Object.Instantiate(Bank().transform.Find("Content").gameObject);
            var source = new Material(Bank().transform.Find("Content/RouteGate/Threshold").GetComponent<Renderer>().sharedMaterial);
            try
            {
                source.SetFloat("_Cull", 0);
                var originalColor = source.GetColor("_BaseColor");
                templates.transform.Find("RouteGate/ExitCue/GroundRing").GetComponent<LineRenderer>().sharedMaterial = source;
                Set("contentTemplates", templates.transform);
                view.SetIslandEntryWarning(new Vector2(2, 4), true);
                var warning = root.transform.Find("IslandEntryWarning");
                var material = warning.GetComponent<Renderer>().sharedMaterial;
                Assert.That(material, Is.Not.SameAs(source), "预告颜色只改实例材质");
                Assert.That(material.shader, Is.SameAs(source.shader), "依赖随资源加载的 Shader 引用");
                Assert.That(material.GetFloat("_Cull"), Is.Zero, "保留模板渲染设置");
                var color = material.GetColor("_BaseColor");
                Assert.That(color.r, Is.EqualTo(1).Within(.001f));
                Assert.That(color.g, Is.EqualTo(.72f).Within(.001f));
                Assert.That(color.b, Is.EqualTo(.08f).Within(.001f));
                Assert.That(color.a, Is.EqualTo(1).Within(.001f));
                Assert.That(source.GetColor("_BaseColor"), Is.EqualTo(originalColor));
                view.SetIslandEntryWarning(Vector2.zero, false);
                Assert.That(warning.gameObject.activeSelf, Is.False);
                view.SetIslandEntryWarning(new Vector2(-2, 5), true);
                Assert.That(warning.localPosition, Is.EqualTo(new Vector3(-2, .08f, 5)));
                Assert.That(warning.GetComponent<Renderer>().sharedMaterial, Is.SameAs(material));
                view.End();
                Assert.That(material == null, Is.True, "离场释放独占材质");
                Assert.That(source != null, Is.True, "离场不能释放模板材质");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(templates);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }
    }
}
