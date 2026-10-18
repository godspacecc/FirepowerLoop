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

