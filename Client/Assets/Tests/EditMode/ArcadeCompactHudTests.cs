using System.Reflection;
using System.IO;
using Luban;
using QFramework;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>验证实际 HUD 的输入模式切换、效果复用和移动端按钮避让。</summary>
    public sealed class ArcadeCompactHudTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private object previousArchitecture;
        private IResourceLoader previousLoader;
        private object previousLanguageCache;
        private static FieldInfo ArchitectureField => typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
        private static FieldInfo LanguageCache => typeof(LocalizationLanguages).GetField("cachedConfig", BindingFlags.Static | BindingFlags.NonPublic);
        [SetUp]
        public void Setup()
        {
            previousArchitecture = ArchitectureField.GetValue(null); previousLoader = ResourceLoader.Current;
            previousLanguageCache = LanguageCache.GetValue(null);
            ResourceLoader.Use(new LocalizationAssetResourceLoader()); LocalizationLanguages.ResetForTests();
            var architecture = new Game(); ArchitectureField.SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", Private).SetValue(architecture, true);
            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterModel(new MobileControlLayoutModel());
            var loc = new LocalizationUtility(false); loc.LoadFromJson("zh-CN", File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
            architecture.RegisterUtility(loc);
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(File.ReadAllBytes("Assets/Game/Config/" + name + ".bytes")))));
        }
        [TearDown]
        public void Cleanup()
        { ArchitectureField.SetValue(null, previousArchitecture); ResourceLoader.Use(previousLoader); LanguageCache.SetValue(null, previousLanguageCache); }

        /// <summary>同一权威玩家行切换手机/桌面展示；分数、生命与倍率不因布局切换丢失或重复累计。</summary>
        [Test]
        public void ConfirmedMobileInformationProjectsStateAndRestoresDesktop()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"));
            try
            {
                var ui = root.GetComponent<ArcadeHudUI>();
                var mobileField = typeof(ArcadeHudUI).GetField("MobileInformationRoot");
                Assert.That(mobileField, Is.Not.Null, "确认图中的手机信息区尚未接入正式 HUD");
                var mobileRoot = (GameObject)mobileField.GetValue(ui);
                var list = (CustomListView)typeof(ArcadeHudUI).GetField("MobilePlayerStatusList").GetValue(ui);
                var ctrl = new ArcadeHudUICtrl();
                typeof(BaseUICtrl).GetField("BaseUI", Private).SetValue(ctrl, ui);
                typeof(ArcadeHudUICtrl).GetMethod("OnInit", Private).Invoke(ctrl, null);
                typeof(ArcadeHudUICtrl).GetField("combat", Private).SetValue(ctrl,
                    new ArcadeCombatHudSnapshot { PlayerHealth = 60, PlayerMaxHealth = 100, Bombs = 1, Dashes = 2 });
                typeof(ArcadeHudUICtrl).GetField("hasCombatSnapshot", Private).SetValue(ctrl, true);
                var mode = typeof(ArcadeHudUICtrl).GetMethod("ApplyInputLayout", Private);
                mode.Invoke(ctrl, new object[] { true, false });
                typeof(ArcadeHudUICtrl).GetMethod("RefreshLocalPlayerStatuses", Private).Invoke(ctrl,
                    new object[] { new ArcadeRunSaveData { RemainingLives = 3, Score = 42350, ScoreMultiplier = 3, MultiplierProgress = 500 } });
                Assert.That(mobileRoot.activeSelf, Is.True);
                Assert.That(ui.PlayerStatusList.gameObject.activeSelf, Is.False);
                var box = list.GetDraggableItems()[0].GetComponent<ArcadePlayerStatusBoxUI>();
                T Field<T>(string name) where T : Object => (T)typeof(ArcadePlayerStatusBoxUI).GetField(name).GetValue(box);
                Assert.That(Field<TMPro.TextMeshProUGUI>("MobileScore").text, Is.EqualTo("42,350"));
                Assert.That(Field<TMPro.TextMeshProUGUI>("MobileLives").text, Is.EqualTo("3"));
                Assert.That(Field<TMPro.TextMeshProUGUI>("MobileBombs").text, Is.EqualTo("1"));
                Assert.That(Field<TMPro.TextMeshProUGUI>("MobileDashes").text, Is.EqualTo("2"));
                Assert.That(Field<Image>("MobileHealthFill").fillAmount, Is.EqualTo(.6f).Within(.001f));
                Assert.That(Field<Image>("MobileMultiplierFill").fillAmount, Is.EqualTo(.5f).Within(.001f));
                var track = (RectTransform)Field<Image>("MobileMultiplierFill").transform.parent;
                Assert.That(track.rect.height, Is.GreaterThan(track.rect.width * 3));
                mode.Invoke(ctrl, new object[] { false, false });
                Assert.That(mobileRoot.activeSelf, Is.False);
                Assert.That(ui.PlayerStatusList.gameObject.activeSelf, Is.True);
                Assert.That(ui.PlayerStatusList.GetDraggableItems()[0].GetComponent<ArcadePlayerStatusBoxUI>().TextScore.text, Does.Contain("42350"));
                mode.Invoke(ctrl, new object[] { true, false });
                Assert.That(Field<TMPro.TextMeshProUGUI>("MobileScore").text, Is.EqualTo("42,350"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>能力沿右侧技能列排列；只迁移旧默认值，用户已经编辑的位置不被覆盖。</summary>
        [Test]
        public void MobileStormJoinsSkillColumnWithoutOverwritingCustomLayout()
        {
            var data = new MobileControlLayoutData();
            Assert.That(data.FieldUpgrade.X, Is.EqualTo(data.Dash.X));
            Assert.That(data.FieldUpgrade.Y, Is.LessThan(data.Dash.Y));
            data.FieldUpgrade.X = .6875f;
            data.FieldUpgrade.Y = 1000f / 1080f;
            data.Sanitize();
            Assert.That(data.FieldUpgrade.X, Is.EqualTo(data.Dash.X));
            Assert.That(data.FieldUpgrade.Y, Is.LessThan(data.Dash.Y));
            data.FieldUpgrade.X = .6f;
            data.FieldUpgrade.Y = .8f;
            data.Sanitize();
            Assert.That(data.FieldUpgrade.X, Is.EqualTo(.6f));
            Assert.That(data.FieldUpgrade.Y, Is.EqualTo(.8f));
        }

        /// <summary>手机能力显示权威充能和持续时间，未获得时隐藏入口，满充能才能提交技能意图。</summary>
        [Test]
        public void MobileStormProjectsChargeDurationAndOwnership()
        {
            Game.Interface.RegisterModel(new HotKeyModel());
            Game.Interface.RegisterModel(new AppStateModel());
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            Game.Interface.RegisterSystem(new MobileInputSystem());
            Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(true);
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"));
            try
            {
                var ui = root.GetComponent<ArcadeHudUI>(); var ctrl = new ArcadeHudUICtrl();
                typeof(BaseUICtrl).GetField("BaseUI", Private).SetValue(ctrl, ui);
                typeof(ArcadeHudUICtrl).GetMethod("OnInit", Private).Invoke(ctrl, null);
                typeof(ArcadeHudUICtrl).GetMethod("ApplyInputLayout", Private).Invoke(ctrl, new object[] { true, false });
                var combat = new ArcadeCombatHudSnapshot { SlayerMartRulesVersion = 1, HasStorm = true, StormCharge = 42, PlayerHealth = 100 };
                typeof(ArcadeHudUICtrl).GetField("combat", Private).SetValue(ctrl, combat);
                var sync = typeof(ArcadeHudUICtrl).GetMethod("SyncFieldUpgradeUi", Private);
                sync.Invoke(ctrl, null);
                Assert.That(ui.TextStormCharge.gameObject.activeSelf, Is.False);
                Assert.That(ui.MobileStormCharge.text, Is.EqualTo("42%"));
                Assert.That(ui.MobileStormFill.fillAmount, Is.EqualTo(.42f).Within(.001f));
                Assert.That(ui.BtnFieldUpgrade.interactable, Is.False);
                combat.StormCharge = 100; sync.Invoke(ctrl, null);
                Assert.That(ui.MobileStormCharge.text, Is.EqualTo("100%"));
                Assert.That(ui.BtnFieldUpgrade.interactable, Is.True);
                int intents = 0;
                Game.Interface.RegisterEvent<ArcadeHudActionRequestedEvent>(evt => { if (evt.Action == ArcadeHudActionKind.FieldUpgrade) intents++; });
                ui.BtnFieldUpgrade.onClick.Invoke();
                Assert.That(intents, Is.EqualTo(1));
                Assert.That(combat.StormCharge, Is.EqualTo(100), "HUD 不应自行扣除权威充能");
                combat.StormSeconds = 2.2f; sync.Invoke(ctrl, null);
                Assert.That(ui.MobileStormCharge.text, Is.EqualTo("3"));
                Assert.That(ui.BtnFieldUpgrade.interactable, Is.False);
                combat.HasStorm = false; sync.Invoke(ctrl, null);
                Assert.That(ui.BtnFieldUpgrade.gameObject.activeSelf, Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>真实双人玩家行和效果在横屏安全区内可读，信息区不侵入默认触控区域。</summary>
        [TestCase(1920, 1080)]
        [TestCase(2340, 1080)]
        [TestCase(1440, 1080)]
        public void MobileCoopInformationFitsTextAndAvoidsTouchAreas(int width, int height)
        {
            Game.Interface.RegisterSystem(new MobileInputSystem());
            Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(true);
            var canvas = new GameObject("MobileInformationTest", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(width, height);
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"), canvas.transform, false);
            try
            {
                var ui = root.GetComponent<ArcadeHudUI>();
                var ctrl = new ArcadeHudUICtrl();
                typeof(BaseUICtrl).GetField("BaseUI", Private).SetValue(ctrl, ui);
                typeof(ArcadeHudUICtrl).GetMethod("OnInit", Private).Invoke(ctrl, null);
                typeof(ArcadeHudUICtrl).GetMethod("ApplyInputLayout", Private).Invoke(ctrl, new object[] { true, false });
                var players = new[] {
                    new OnlineArcadePlayerSnapshot(1, "Self", 3, 0, 1, 2, "arcade.weapon.basic", false, null, score: 42350, scoreMultiplier: 3, multiplierProgress: 600),
                    new OnlineArcadePlayerSnapshot(2, "Player 2", 0, 0, 0, 1, "arcade.weapon.basic", false, null, score: 16800, scoreMultiplier: 2, multiplierProgress: 300) };
                var battle = new OnlineArcadeBattleSnapshot(1, 4, 4, 1, 1, 0, 1, 1000,
                    new[] { new OnlineArcadeBattlePlayerSnapshot(1, 0, 0, 0, 1, 75, 100, true, false, 0),
                        new OnlineArcadeBattlePlayerSnapshot(2, 0, 0, 0, 1, 0, 100, false, false, 0) },
                    System.Array.Empty<OnlineArcadeEnemySnapshot>(), System.Array.Empty<OnlineArcadeProjectileSnapshot>(), System.Array.Empty<OnlineArcadeDropSnapshot>());
                var snapshot = new OnlineArcadeRunClientSnapshot(true, 1, 2, 1, OnlineArcadeRunPhase.Arena,
                    0, 1, "layout", players, OnlineArcadeOperationState.Idle, OnlineArcadeResultCode.Success, battle);
                typeof(ArcadeHudUICtrl).GetMethod("RefreshOnlinePlayerStatuses", Private).Invoke(ctrl, new object[] { snapshot, 1L });
                typeof(ArcadeHudUICtrl).GetMethod("RenderTimedStatus", Private).Invoke(ctrl, new object[] {
                    "ui.arcade.product.arcade.weapon.rapid.name", 12f,
                    new[] { new ArcadePowerupHudEntry("ui.arcade.product.arcade.powerup.skull.small.name", 8f, 0),
                        new ArcadePowerupHudEntry("ui.arcade.product.arcade.powerup.skull.large.name", 6f, 0) } });
                var safe = root.AddComponent<SafeAreaFitter>(); safe.enabled = false;
                typeof(SafeAreaFitter).GetMethod("ApplyAnchors", Private).Invoke(safe,
                    new object[] { new Vector2(.03f, .02f), new Vector2(.97f, .98f) });
                typeof(ArcadeHudUICtrl).GetMethod("ApplyJoystickLayout", Private).Invoke(ctrl, null);
                for (int pass = 0; pass < 3; pass++)
                { LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)root.transform); Canvas.ForceUpdateCanvases(); }
                Assert.That(ui.MobilePlayerStatusList.GetDraggableItems().Count, Is.EqualTo(2));
                foreach (var text in ui.MobilePlayerStatusList.GetComponentsInChildren<TMPro.TextMeshProUGUI>())
                {
                    text.ForceMeshUpdate();
                    Assert.That(text.isTextOverflowing, Is.False, text.name + " 超出正式文本框：" + text.text);
                }
                var information = MobileControlLayoutGeometry.Bounds(ui.JoystickLayoutArea, (RectTransform)ui.MobileInformationRoot.transform);
                foreach (var touch in new[] { ui.MoveJoystickRoot.transform, ui.AimJoystickRoot.transform,
                    ui.BtnBomb.transform, ui.BtnDash.transform, ui.BtnPause.transform, ui.BtnFieldUpgrade.transform })
                    Assert.That(information.Overlaps(MobileControlLayoutGeometry.Bounds(ui.JoystickLayoutArea, (RectTransform)touch)), Is.False, touch.name);
                Assert.That(ui.MobileEffectList.GetDraggableItems().Count, Is.EqualTo(2));
                Assert.That(ui.MobileWeaponEffectList.GetDraggableItems().Count, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(canvas); }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void InterruptedStick_ReopensAtCenter(bool hideHud, bool aim)
        {
            UIHelper.ResetForTests();
            ResourceLoader.Use(new LocalizationAssetResourceLoader());
            LocalizationLanguages.ResetForTests();
            Game.Interface.RegisterSystem(new MobileInputSystem());
            Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(true);
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
            var canvas = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            foreach (var layer in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
                new GameObject(layer, typeof(RectTransform)).transform.SetParent(canvas.transform, false);
            new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule)).transform.SetParent(canvas.transform, false);
            canvas.AddComponent<UIRoot>();
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"));
            var ui = root.GetComponent<ArcadeHudUI>();
            var ctrl = new ArcadeHudUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI", Private).SetValue(ctrl, ui);
            var refresh = typeof(ArcadeHudUICtrl).GetMethod("RefreshJoystickVisibility", Private);
            var stick = aim ? ui.AimJoystick : ui.MoveJoystick;
            try
            {
                refresh.Invoke(ctrl, null);
                // EditMode 不执行运行时 Start；沿用真实组件初始化和拖动入口，不伪造偏移。
                stick.GetType().GetMethod("OnEnable", Private).Invoke(stick, null);
                stick.GetType().GetMethod("Start", Private).Invoke(stick, null);
                var rect = (RectTransform)stick.transform;
                var center = rect.anchoredPosition;
                var pointer = new UnityEngine.EventSystems.PointerEventData(null)
                { position = RectTransformUtility.WorldToScreenPoint(null, rect.position) };
                stick.OnPointerDown(pointer);
                pointer.position += new Vector2(45, 30);
                stick.OnDrag(pointer);
                Assert.That(Vector2.Distance(rect.anchoredPosition, center), Is.GreaterThan(1));

                if (hideHud)
                    typeof(ArcadeHudUICtrl).GetMethod("OnHide", Private).Invoke(ctrl, null);
                else
                {
                    UIHelper.ShowUI<MessageUICtrl>();
                    Assert.That(UIHelper.HasUIOpen, Is.True);
                    refresh.Invoke(ctrl, null);
                    UIHelper.HideUI<MessageUICtrl>();
                }
                refresh.Invoke(ctrl, null);
                Assert.That(rect.anchoredPosition, Is.EqualTo(center), "未松手时被界面中断，重开必须回到原中心");
                // 新拖动结束后仍回原点，不能把中断位置当成下一次原点。
                stick.OnPointerDown(pointer);
                pointer.position += Vector2.right * 20;
                stick.OnDrag(pointer);
                stick.OnPointerUp(pointer);
                Assert.That(rect.anchoredPosition, Is.EqualTo(center));
            }
            finally
            {
                stick.GetType().GetMethod("OnDisable", Private).Invoke(stick, null);
                Object.DestroyImmediate(root);
                UIHelper.ResetForTests();
                if (canvas != null) Object.DestroyImmediate(canvas);
            }
        }

        [Test]
        public void FirstPersonTouchControls_ReleaseFireOnPauseAndRestoreAimOnExit()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"));
            try
            {
                var ui = root.GetComponent<ArcadeHudUI>();
                var ctrl = new ArcadeHudUICtrl();
                typeof(BaseUICtrl).GetField("BaseUI", Private).SetValue(ctrl, ui);
                var snapshot = new ArcadeCombatHudSnapshot { FirstPersonSeconds = 10, PlayerHealth = 100 };
                typeof(ArcadeHudUICtrl).GetField("combat", Private).SetValue(ctrl, snapshot);
                typeof(ArcadeHudUICtrl).GetField("hasCombatSnapshot", Private).SetValue(ctrl, true);
                var refresh = typeof(ArcadeHudUICtrl).GetMethod("RefreshFirstPersonMobileControls", Private);
                refresh.Invoke(ctrl, new object[] { true, true });
                Assert.That(ui.AimJoystickRoot.activeSelf, Is.False);
                Assert.That(ui.FirstPersonFirePress.gameObject.activeSelf, Is.True);
                int releases = 0;
                ui.FirstPersonFirePress.Released += () => releases++;
                var pointer = new UnityEngine.EventSystems.PointerEventData(null) { pointerId = 41 };
                ui.FirstPersonFirePress.OnPointerDown(pointer);
                refresh.Invoke(ctrl, new object[] { true, false });
                Assert.That(ui.FirstPersonFirePress.gameObject.activeSelf, Is.False);
                Assert.That(releases, Is.EqualTo(1), "暂停必须释放原触点");
                refresh.Invoke(ctrl, new object[] { true, true });
                ui.FirstPersonFirePress.OnPointerUp(pointer);
                Assert.That(releases, Is.EqualTo(1), "重开后旧触点的释放不能成为新请求");
                snapshot.FirstPersonSeconds = 0;
                refresh.Invoke(ctrl, new object[] { true, true });
                Assert.That(ui.AimJoystickRoot.activeSelf, Is.True);
                Assert.That(ui.FirstPersonFirePress.gameObject.activeSelf, Is.False);
                snapshot.FirstPersonSeconds = 10;
                refresh.Invoke(ctrl, new object[] { false, false });
                Assert.That(ui.FirstPersonFirePress.gameObject.activeSelf, Is.False, "桌面不显示触屏开火控件");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void InputLayout_KeepsOnlineExitAndSeparatesDesktopFromTouch(bool mobile, bool online)
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"));
            try
            {
                var ui = root.GetComponent<ArcadeHudUI>();
                var ctrl = new ArcadeHudUICtrl();
                typeof(BaseUICtrl).GetField("BaseUI", Private).SetValue(ctrl, ui);
                var method = typeof(ArcadeHudUICtrl).GetMethod("ApplyInputLayout", Private);
                Assert.That(method, Is.Not.Null, "HUD 尚未按输入模式切换能力区与暂停入口");
                method.Invoke(ctrl, new object[] { mobile, online });
                Assert.That(ui.BtnPause.gameObject.activeSelf, Is.EqualTo(mobile || online));
                Assert.That(ui.BtnBomb.gameObject.activeInHierarchy, Is.EqualTo(mobile));
                Assert.That(ui.BtnDash.gameObject.activeInHierarchy, Is.EqualTo(mobile));
                Assert.That(root.transform.Find("JoystickLayoutArea/DesktopAbilityRoot").gameObject.activeSelf, Is.EqualTo(!mobile));
                // 单机图标不能被旧“暂停”文字覆盖；联机沿用明确的断线退出语义。
                var labels = ui.BtnPause.GetComponentsInChildren<TMPro.TextMeshProUGUI>(false);
                if (online) Assert.That(labels, Has.Some.Matches<TMPro.TextMeshProUGUI>(text => text.text == Loc.T("ui.arcade.hud.online_exit")));
                else Assert.That(labels, Is.Empty, "单机暂停入口只显示双竖线图标");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void MobileButtons_AreSeparatedAboveAimStickAndInsideSafeArea()
        {
            var ui = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab").GetComponent<ArcadeHudUI>();
            var pause = (RectTransform)ui.BtnPause.transform;
            var bomb = (RectTransform)ui.BtnBomb.transform;
            var dash = (RectTransform)ui.BtnDash.transform;
            Assert.That(pause.parent, Is.EqualTo(bomb.parent));
            Assert.That(bomb.parent, Is.EqualTo(dash.parent));
            Assert.That(pause.anchoredPosition.y - bomb.anchoredPosition.y, Is.GreaterThan((pause.rect.height + bomb.rect.height) * .5f));
            Assert.That(bomb.anchoredPosition.y - dash.anchoredPosition.y, Is.GreaterThan((bomb.rect.height + dash.rect.height) * .5f));
            Assert.That(bomb.rect.width, Is.GreaterThanOrEqualTo(96));
            Assert.That(dash.rect.height, Is.GreaterThanOrEqualTo(96));
            Assert.That(pause.parent, Is.EqualTo(ui.JoystickLayoutArea));
            Assert.That(ui.JoystickLayoutArea.parent, Is.EqualTo(ui.transform), "BaseUICtrl 安全区适配须覆盖按钮父容器");
            Assert.That(pause.anchoredPosition.x + pause.rect.width * .5f, Is.LessThan(0));
            Assert.That(pause.anchoredPosition.y + pause.rect.height * .5f, Is.LessThan(0));
        }

        [TestCase("small","SkullRedIcon")]
        [TestCase("large","SkullPurpleIcon")]
        public void SkullStatusUsesColoredIconWithoutTextFrameOrProgress(string tier,string iconField)
        {
            var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"));
            try
            {
                var ui=root.GetComponent<ArcadeHudUI>();var ctrl=new ArcadeHudUICtrl();
                typeof(BaseUICtrl).GetField("BaseUI",Private).SetValue(ctrl,ui);
                typeof(ArcadeHudUICtrl).GetMethod("OnInit",Private).Invoke(ctrl,null);
                typeof(ArcadeHudUICtrl).GetMethod("RenderTimedStatus",Private).Invoke(ctrl,new object[]{null,0f,new[]{new ArcadePowerupHudEntry("ui.arcade.product.arcade.powerup.skull."+tier+".name",10,0)}});
                var box=ui.EffectList.GetDraggableItems()[0].GetComponent<ArcadeEffectBoxUI>();
                Assert.That(box.Icon.gameObject.activeSelf,Is.True,"骷髅强化需要正式图标");
                Assert.That(box.Icon.sprite,Is.SameAs(typeof(ArcadeHudUI).GetField(iconField).GetValue(ui)));
                Assert.That(box.TextFallback.gameObject.activeSelf,Is.False);Assert.That(box.TextCharges.gameObject.activeSelf,Is.False);
                Assert.That(box.ProgressFill.transform.parent.gameObject.activeSelf,Is.False);Assert.That(box.GetComponent<Image>().enabled,Is.False);
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void SkullOnlyPulsesInLastThreeSecondsAndReuseRestoresNormalEffect()
        {
            var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"));
            try
            {
                var ui=root.GetComponent<ArcadeHudUI>();var ctrl=new ArcadeHudUICtrl();
                typeof(BaseUICtrl).GetField("BaseUI",Private).SetValue(ctrl,ui);typeof(ArcadeHudUICtrl).GetMethod("OnInit",Private).Invoke(ctrl,null);
                var render=typeof(ArcadeHudUICtrl).GetMethod("RenderTimedStatus",Private);
                var pulse=typeof(ArcadeHudUICtrl).GetMethod("UpdateSkullWarning",Private);Assert.That(pulse,Is.Not.Null);
                void Render(string id,float seconds)=>render.Invoke(ctrl,new object[]{null,0f,new[]{new ArcadePowerupHudEntry("ui.arcade.product.arcade.powerup."+id+".name",seconds,0)}});
                Render("skull.small",3);var box=ui.EffectList.GetDraggableItems()[0].GetComponent<ArcadeEffectBoxUI>();
                pulse.Invoke(ctrl,new object[]{0d});Assert.That(box.Icon.color.a,Is.EqualTo(1).Within(.01f));
                pulse.Invoke(ctrl,new object[]{.25d});Assert.That(box.Icon.color.a,Is.InRange(.5f,.65f));
                Render("skull.large",3.1f);pulse.Invoke(ctrl,new object[]{.25d});Assert.That(box.Icon.color,Is.EqualTo(Color.white));
                Render("boots",8);pulse.Invoke(ctrl,new object[]{.25d});Assert.That(box.Icon.color,Is.EqualTo(Color.white));
                Assert.That(box.ProgressFill.transform.parent.gameObject.activeSelf,Is.True);Assert.That(box.GetComponent<Image>().enabled,Is.True);
                Render("skull.small",0);Assert.That(ui.EffectList.GetDraggableItems(),Is.Empty);
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void TimedEffects_UseExternalReusableBoxAndKeepAllActiveEffects()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab");
            var field = typeof(ArcadeHudUI).GetField("EffectList");
            Assert.That(field, Is.Not.Null, "缺少紧凑效果列表");
            var list = (CustomListView)field.GetValue(prefab.GetComponent<ArcadeHudUI>());
            Assert.That(list.itemPrefab, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(list.itemPrefab), Does.EndWith("Box/ArcadeEffectBox.prefab"));
            Assert.That(list.GetComponent<GridLayoutGroup>(), Is.Not.Null, "效果并存时应换行，不能遮住能力区");
        }

        [Test]
        public void TimedEffects_RefreshReplaceExpireWithoutOwningAClock()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"));
            try
            {
                var ui = root.GetComponent<ArcadeHudUI>(); var ctrl = new ArcadeHudUICtrl();
                typeof(BaseUICtrl).GetField("BaseUI", Private).SetValue(ctrl, ui);
                typeof(ArcadeHudUICtrl).GetMethod("OnInit", Private).Invoke(ctrl, null);
                var render = typeof(ArcadeHudUICtrl).GetMethod("RenderTimedStatus", Private);
                var list = (CustomListView)typeof(ArcadeHudUI).GetField("EffectList").GetValue(ui);
                render.Invoke(ctrl, new object[] { "ui.arcade.product.arcade.weapon.rapid.name", 12.5f, new[] {
                    new ArcadePowerupHudEntry("ui.arcade.product.arcade.powerup.shield.name", 2f, 1),
                    new ArcadePowerupHudEntry("ui.arcade.product.arcade.powerup.boots.name", 9f, 0),
                    new ArcadePowerupHudEntry("ui.arcade.product.arcade.powerup.magnet.name", 10f, 0) } });
                var cells = list.GetDraggableItems(); Assert.That(cells.Count, Is.EqualTo(3));
                var weapon = ui.WeaponEffectList.GetDraggableItems()[0].GetComponent<ArcadeEffectBoxUI>();
                Assert.That(weapon.ProgressFill.fillAmount, Is.EqualTo(.5f).Within(.001f));
                Assert.That(weapon.Icon.sprite, Is.EqualTo(ui.RapidIcon));
                var shield = cells[0].GetComponent<ArcadeEffectBoxUI>();
                Assert.That(shield.TextFallback.gameObject.activeSelf, Is.True);
                Assert.That(shield.TextFallback.text, Is.EqualTo("盾"));
                Assert.That(shield.ProgressFill.color.g, Is.LessThan(.5f));
                render.Invoke(ctrl, new object[] { "ui.arcade.product.arcade.weapon.spread.name", 20f, new ArcadePowerupHudEntry[0] });
                Assert.That(list.GetDraggableItems(), Is.Empty);
                Assert.That(ui.WeaponEffectList.GetDraggableItems().Count, Is.EqualTo(1));
                Assert.That(weapon.Icon.sprite, Is.EqualTo(ui.SpreadIcon));
                Assert.That(weapon.ProgressFill.fillAmount, Is.EqualTo(1f));
                render.Invoke(ctrl, new object[] { null, 0f, new ArcadePowerupHudEntry[0] });
                Assert.That(list.GetDraggableItems(), Is.Empty);
                Assert.That(ui.SupportEffectRoot.activeSelf, Is.False);
                Assert.That(ui.WeaponEffectRoot.activeSelf, Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(1920, 1080)]
        [TestCase(2340, 1080)]
        [TestCase(1440, 1080)]
        public void CustomSticks_AvoidRealButtonsWithoutRewritingSavedSettings(int width, int height)
        {
            var canvas = new GameObject("LayoutTest", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(width, height);
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"), canvas.transform, false);
            try
            {
                var ui = root.GetComponent<ArcadeHudUI>(); var ctrl = new ArcadeHudUICtrl();
                typeof(BaseUICtrl).GetField("BaseUI", Private).SetValue(ctrl, ui);
                var model = Game.Interface.GetModel<MobileControlLayoutModel>();
                var data = model.ToData(); data.Aim.X = .93f; data.Aim.Y = .45f; data.Aim.Scale = 1.5f; data.Aim.Opacity = .5f;
                model.Apply(data);
                typeof(ArcadeHudUICtrl).GetMethod("ApplyJoystickLayout", Private).Invoke(ctrl, null);
                var touch = MobileControlLayoutGeometry.Bounds(ui.JoystickLayoutArea, (RectTransform)ui.AimJoystickRoot.transform);
                foreach (var button in new[] { ui.BtnPause, ui.BtnBomb, ui.BtnDash })
                    Assert.That(touch.Overlaps(MobileControlLayoutGeometry.Bounds(ui.JoystickLayoutArea, (RectTransform)button.transform)), Is.False);
                Assert.That(model.ToData().Aim.Y, Is.EqualTo(.45f));
                Assert.That(model.ToData().Aim.Scale, Is.EqualTo(1.5f));
                Assert.That(ui.AimOpacity.alpha, Is.EqualTo(.5f));
                var preview = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/MobileControlLayout.prefab").GetComponent<MobileControlLayoutUI>();
                Assert.That(((RectTransform)preview.BombPreview.transform).rect.size, Is.EqualTo(((RectTransform)ui.BtnBomb.transform).rect.size));
                Assert.That(((RectTransform)preview.DashPreview.transform).rect.size, Is.EqualTo(((RectTransform)ui.BtnDash.transform).rect.size));
                Assert.That(((RectTransform)preview.PausePreview.transform).rect.size, Is.EqualTo(((RectTransform)ui.BtnPause.transform).rect.size));
            }
            finally { Object.DestroyImmediate(canvas); }
        }

        [Test]
        public void FirstPersonCountdown_DoesNotCoverConcurrentEffectsOnTablet()
        {
            var canvas = new GameObject("Tablet", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1440, 1080);
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"), canvas.transform, false);
            try
            {
                var ui = root.GetComponent<ArcadeHudUI>();
                var timer = MobileControlLayoutGeometry.Bounds(ui.JoystickLayoutArea, ui.TextFirstPerson.rectTransform);
                var supports = MobileControlLayoutGeometry.Bounds(ui.JoystickLayoutArea, (RectTransform)ui.SupportEffectRoot.transform);
                Assert.That(timer.Overlaps(supports), Is.False, "第一人称计时与有效辅助效果须同时可读");
                Assert.That(timer.Overlaps(MobileControlLayoutGeometry.Bounds(ui.JoystickLayoutArea, (RectTransform)ui.TextWave.transform.parent)), Is.False);
            }
            finally { Object.DestroyImmediate(canvas); }
        }
    }
}

