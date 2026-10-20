using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>真实 HUD 财宝飞入、到达计分、连续拾取和回收回归；只操作临时界面及内存局。</summary>
    public sealed class ArcadeScoreFlightTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly FieldInfo singleton = typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
        private object previous;
        private IResourceLoader previousLoader;
        private Game architecture;
        private GameObject canvasRoot, instance;
        private ArcadeHudUICtrl controller;
        private ArcadeHudUI hud;

        [SetUp] public void SetUp()
        {
            previous = singleton.GetValue(null); previousLoader = ResourceLoader.Current;
            architecture = new Game(); singleton.SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", Hidden).SetValue(architecture, true);
            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterModel(new ArcadeRunModel());
            architecture.RegisterSystem(new MobileInputSystem());
            ResourceLoader.SetLoaderForTests(new LocalizationAssetResourceLoader());
            architecture.RegisterUtility(new LocalizationUtility());
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() => new Tables(name =>
                new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes"))))));
            canvasRoot = new GameObject("ScoreFlightTestCanvas", typeof(RectTransform), typeof(Canvas));
            canvasRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"), canvasRoot.transform);
            hud = instance.GetComponent<ArcadeHudUI>(); controller = new ArcadeHudUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI", Hidden).SetValue(controller, hud);
            typeof(BaseUICtrl).GetField("_isActive", Hidden).SetValue(controller, true);
            Invoke("OnInit");
            InstallScore(1000);
            Canvas.ForceUpdateCanvases();
        }

        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(canvasRoot);
            architecture?.Deinit(); singleton.SetValue(null, previous);
            ResourceLoader.SetLoaderForTests(previousLoader); LifeCycle.ClearForTests();
        }

        [Test] public void FormalHudReferencesReusableNonBlockingFlightBoxWithoutPopupText()
        {
            var list = FlightList();
            Assert.That(AssetDatabase.GetAssetPath(list.itemPrefab), Is.EqualTo("Assets/Game/Prefabs/UI/Arcade/Box/ArcadeScoreFlightBox.prefab"));
            Assert.That(list.itemPrefab.GetComponent<BaseBoxUI>(), Is.Not.Null);
            Assert.That(list.itemPrefab.GetComponent<ReferenceCollector>(), Is.Not.Null);
            Assert.That(list.itemPrefab.GetComponentsInChildren<TMP_Text>(true), Is.Empty, "用户不需要额外的 +分数文字");
            Assert.That(list.itemPrefab.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget), Is.True);
        }

        [TestCase(false)] [TestCase(true)]
        public void PickupFliesToOwnScoreThenIncreasesAndPulsesWithoutChangingRules(bool mobile)
        {
            typeof(MobileInputSystem).GetField("editorSimulationEnabled", Hidden).SetValue(architecture.GetSystem<MobileInputSystem>(), mobile);
            Invoke("ApplyInputLayout", mobile, false);
            InstallScore(1250);
            Assert.That(Queue(250), Is.True);
            var box = SelfBox(mobile); var score = mobile ? box.MobileScore : box.TextScore;
            var baseline = score.transform.localScale;
            AssertScore(1000, mobile);
            var flight = FlightList().GetDraggableItems().Single(); var start = flight.localPosition;
            Invoke("AdvanceScorePickups", .25f);
            Assert.That(flight.localPosition, Is.Not.EqualTo(start));
            var destination = flight.parent.InverseTransformPoint(score.rectTransform.TransformPoint(score.rectTransform.rect.center));
            Assert.That(Vector3.Distance(flight.localPosition, destination), Is.LessThan(Vector3.Distance(start, destination)));
            AssertScore(1000, mobile);
            Invoke("AdvanceScorePickups", .35f);
            Assert.That(FlightList().GetDraggableItems(), Is.Empty);
            AssertScore(1250, mobile);
            Assert.That(score.transform.localScale.x, Is.GreaterThan(baseline.x));
            Invoke("AdvanceScorePickups", .5f);
            Assert.That(score.transform.localScale, Is.EqualTo(baseline));
            Assert.That(architecture.GetModel<ArcadeRunModel>().Current.Score, Is.EqualTo(1250), "HUD 不能重复发放积分");
        }

        [Test] public void BurstPickupsBoundVisualCountAndPreserveEveryScoreIncrease()
        {
            InstallScore(4200);
            for (int i = 0; i < 32; i++) Assert.That(Queue(100), Is.True);
            Assert.That(FlightList().GetDraggableItems().Count, Is.LessThanOrEqualTo(16));
            AssertScore(1000, false);
            Invoke("AdvanceScorePickups", .6f);
            AssertScore(4200, false);
            Assert.That(architecture.GetModel<ArcadeRunModel>().Current.Score, Is.EqualTo(4200));
        }

        [Test] public void HideOrMapReleaseCancelsFlightRestoresScaleAndNeverReplaysOldScore()
        {
            InstallScore(1100); Queue(100); Invoke("AdvanceScorePickups", .6f);
            var score = SelfBox(false).TextScore;
            Assert.That(score.transform.localScale.x, Is.GreaterThan(1));
            InstallScore(1200); Queue(100);
            Invoke("ClearScorePickupFeedback"); Invoke("ClearScorePickupFeedback");
            Assert.That(FlightList().GetDraggableItems(), Is.Empty);
            Assert.That(score.transform.localScale, Is.EqualTo(Vector3.one)); AssertScore(1200, false);
            InstallScore(1300); Queue(100); Invoke("OnHide");
            Assert.That(FlightList().GetDraggableItems(), Is.Empty);
            typeof(BaseUICtrl).GetField("_isActive", Hidden).SetValue(controller, false);
            Assert.That(Queue(100), Is.False);
            typeof(BaseUICtrl).GetField("_isActive", Hidden).SetValue(controller, true);
            InstallScore(9000); Invoke("AdvanceScorePickups", 1f); AssertScore(9000, false);
        }

        [Test] public void ZeroOrInvalidPickupDoesNotDelayTheScoreOrCreateAnEffect()
        {
            Assert.That(Queue(0), Is.False); Assert.That(Queue(-1), Is.False);
            Assert.That((bool)Invoke("TryPlayScorePickup", 50L, new Vector2(float.NaN, 0)), Is.False);
            Assert.That(FlightList().GetDraggableItems(), Is.Empty); AssertScore(1000, false);
        }

        private bool Queue(long amount) => (bool)Invoke("TryPlayScorePickup", amount, new Vector2(Screen.width * .55f, Screen.height * .4f));
        private object Invoke(string name, params object[] args)
        {
            var method = typeof(ArcadeHudUICtrl).GetMethod(name, Hidden | BindingFlags.Public);
            Assert.That(method, Is.Not.Null, "缺少财宝飞入行为：" + name);
            return method.Invoke(controller, args);
        }
        private CustomListView FlightList()
        {
            var field = typeof(ArcadeHudUI).GetField("ScoreFlightList");
            Assert.That(field, Is.Not.Null, "正式 HUD 必须通过生成器引用飞行集合");
            return (CustomListView)field.GetValue(hud);
        }
        private ArcadePlayerStatusBoxUI SelfBox(bool mobile) => (mobile ? hud.MobilePlayerStatusList : hud.PlayerStatusList).GetDraggableItems()[0].GetComponent<ArcadePlayerStatusBoxUI>();
        private void AssertScore(long value, bool mobile) => Assert.That(mobile ? SelfBox(true).MobileScore.text : SelfBox(false).TextScore.text,
            Is.EqualTo(mobile ? value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) : Loc.T("ui.arcade.hud.score", value)));
        private void InstallScore(long score)
        {
            var model = architecture.GetModel<ArcadeRunModel>();
            if (!model.HasRun) { model.CommitSafeNode(new ArcadeRunSaveData { RemainingLives = 3 }); model.EnterArena(); }
            model.SetTransientTreasureScore(new ArcadeTreasureScoreState(score, 1, 0, 3));
            architecture.SendEvent(new ArcadeCombatHudChangedEvent(new ArcadeCombatHudSnapshot { PlayerHealth = 100, PlayerMaxHealth = 100 }));
        }
    }
}
