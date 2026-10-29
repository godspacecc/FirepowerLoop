using NUnit.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using QFramework;
using UnityEngine;
using UnityEditor;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class MobileControlLayoutTests
    {
        [Test]
        public void ArcadeButtons_RoundTripKeepsRealActionsAndIgnoresRetiredActions()
        {
            var data = JsonConvert.DeserializeObject<MobileControlLayoutData>(
                "{\"pause\":{\"x\":0.7,\"y\":0.8},\"bomb\":{\"x\":0.68,\"y\":0.42},\"dash\":{\"x\":0.75,\"y\":0.55},\"fieldUpgrade\":{\"x\":0.6,\"y\":0.7},\"donateLife\":{\"x\":0.65,\"y\":0.65},\"exitFirstPerson\":{\"x\":0.8,\"y\":0.6},\"firstPersonFire\":{\"x\":0.85,\"y\":0.25},\"inventory\":{\"x\":0.3,\"y\":0.3}}");
            var saved = JObject.FromObject(data.Clone());
            Assert.That((float?)saved["bomb"]?["x"], Is.EqualTo(.68f));
            foreach (var key in new[] { "pause", "bomb", "dash", "fieldUpgrade", "donateLife", "exitFirstPerson", "firstPersonFire" })
                Assert.That(saved[key], Is.Not.Null, key);
            foreach (var key in new[] { "inventory", "build", "attack", "reload" }) Assert.That(saved[key], Is.Null, key);
        }

        [Test]
        public void ArcadeEditor_FormalReferencesExposeRealActionsWithoutBaseControls()
        {
            var root = PrefabUtility.LoadPrefabContents("Assets/Game/Prefabs/UI/Home/MobileControlLayout.prefab");
            try
            {
                var collector = root.GetComponent<ReferenceCollector>();
                var ui = root.GetComponent<MobileControlLayoutUI>();
                foreach (var key in new[] { "PausePreview", "BombPreview", "DashPreview", "FieldUpgradePreview", "DonateLifePreview", "ExitFirstPersonPreview", "FirstPersonFirePreview", "PreviewViewport" })
                {
                    Assert.That(collector.data.Any(item => item.key == key), Is.True, key);
                    var value = typeof(MobileControlLayoutUI).GetField(key)?.GetValue(ui) as Object;
                    Assert.That(value, Is.Not.Null, key);
                }
                foreach (var key in new[] { "InventoryPreview", "BuildPreview", "AttackPreview", "ReloadPreview", "StatusPreview", "HealthPreview", "WeaponPreview" })
                    Assert.That(collector.data.Any(item => item.key == key), Is.False, key);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void ArcadeHud_ConsumesSavedButtonPositionInSameSafeAreaAsSticks()
        {
            var architecture = CreateEditorArchitecture();
            var layout = JsonConvert.DeserializeObject<MobileControlLayoutData>("{\"bomb\":{\"x\":0.68,\"y\":0.42}}");
            architecture.GetModel<MobileControlLayoutModel>().Apply(layout);
            var canvas = new GameObject("ArcadeLayoutTest", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1920, 1080);
            try
            {
                var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"), canvas.transform, false);
                var ui = root.GetComponent<ArcadeHudUI>();
                var ctrl = new ArcadeHudUICtrl();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(BaseUICtrl).GetField("BaseUI", flags).SetValue(ctrl, ui);
                Canvas.ForceUpdateCanvases();
                var originalAnchor = ((RectTransform)ui.BtnBomb.transform).anchorMin;
                var originalPosition = ((RectTransform)ui.BtnBomb.transform).anchoredPosition;
                typeof(ArcadeHudUICtrl).GetMethod("ApplyJoystickLayout", flags).Invoke(ctrl, null);
                var bounds = MobileControlLayoutGeometry.Bounds(ui.JoystickLayoutArea, (RectTransform)ui.BtnBomb.transform);
                Assert.That(bounds.center.x / ui.JoystickLayoutArea.rect.width, Is.EqualTo(.68f).Within(.001f));
                Assert.That(bounds.center.y / ui.JoystickLayoutArea.rect.height, Is.EqualTo(.42f).Within(.001f));
                // 刘海换边可能只改变安全区原点而不改变尺寸；更新后仍必须保留保存的比例位置。
                ui.JoystickLayoutArea.anchoredPosition += new Vector2(40, 0);
                typeof(ArcadeHudUICtrl).GetMethod("RefreshJoystickArea", flags).Invoke(ctrl, null);
                bounds = MobileControlLayoutGeometry.Bounds(ui.JoystickLayoutArea, (RectTransform)ui.BtnBomb.transform);
                Assert.That(bounds.center.x / ui.JoystickLayoutArea.rect.width, Is.EqualTo(.68f).Within(.001f));
                var upgrade = MobileControlLayoutGeometry.Bounds(ui.JoystickLayoutArea, (RectTransform)ui.BtnFieldUpgrade.transform);
                var upgradeLayout = layout.FieldUpgrade;
                Assert.That(upgrade.center.x / ui.JoystickLayoutArea.rect.width, Is.EqualTo(upgradeLayout.X).Within(.001f));
                Assert.That(upgrade.center.y / ui.JoystickLayoutArea.rect.height, Is.EqualTo(upgradeLayout.Y).Within(.001f));
                architecture.GetSystem<MobileInputSystem>().TrySetEditorSimulation(false);
                typeof(ArcadeHudUICtrl).GetMethod("RefreshJoystickVisibility", flags).Invoke(ctrl, null);
                Assert.That(((RectTransform)ui.BtnBomb.transform).anchorMin, Is.EqualTo(originalAnchor));
                Assert.That(((RectTransform)ui.BtnBomb.transform).anchoredPosition, Is.EqualTo(originalPosition));
            }
            finally { Object.DestroyImmediate(canvas); }
        }

        [Test]
        public void Editor_FillsActualSafeAreaAtCombatScale()
        {
            CreateEditorArchitecture();
            var root = CreateEditor(out var ctrl, out var ui);
            try
            {
                // 手工实例化的测试夹具补齐BaseUICtrl.Load正式添加的同一安全区适配器。
                var fitter = ui.gameObject.AddComponent<SafeAreaFitter>();
                fitter.ApplyAnchors(new Vector2(.1f, 0), new Vector2(.9f, 1));
                Canvas.ForceUpdateCanvases();
                ctrl.Show();
                Assert.That(ui.LayoutArea.rect.width, Is.EqualTo(1536).Within(.1f));
                Assert.That(ui.LayoutArea.rect.height, Is.EqualTo(1080).Within(.1f));
                Assert.That(ui.LayoutArea.lossyScale, Is.EqualTo(ui.ControlSafeArea.lossyScale));
                var bounds = MobileControlLayoutGeometry.Bounds(ui.ControlSafeArea, ui.LayoutArea);
                Assert.That(bounds.min, Is.EqualTo(Vector2.zero));
                Assert.That(bounds.size, Is.EqualTo(new Vector2(1536, 1080)));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(1920, 1080)]
        [TestCase(2340, 1080)]
        [TestCase(1440, 1080)]
        public void EditorAndHud_ProjectSameControlsForBothModesAndInsetSafeArea(int width, int height)
        {
            var architecture = CreateEditorArchitecture();
            var data = new MobileControlLayoutData(); data.Move.Scale = 1.4f;
            data.Bomb.X = .68f; data.Bomb.Y = .42f;
            architecture.GetModel<MobileControlLayoutModel>().Apply(data);
            var root = CreateEditor(out var ctrl, out var preview);
            ((RectTransform)root.transform).sizeDelta = new Vector2(width, height);
            try
            {
                var hudRoot = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"), root.transform, false);
                var hud = hudRoot.GetComponent<ArcadeHudUI>(); var hudCtrl = new ArcadeHudUICtrl();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(BaseUICtrl).GetField("BaseUI", flags).SetValue(hudCtrl, hud);
                foreach (var go in new[] { preview.gameObject, hudRoot })
                    go.AddComponent<SafeAreaFitter>().ApplyAnchors(new Vector2(.05f, 0), new Vector2(.95f, 1));
                Canvas.ForceUpdateCanvases(); ctrl.Show();
                var previews = new[] { preview.PausePreview.transform, preview.BombPreview.transform, preview.DashPreview.transform,
                    preview.FieldUpgradePreview.transform, preview.DonateLifePreview.transform, preview.ExitFirstPersonPreview.transform, preview.FirstPersonFirePreview.transform };
                var actual = new[] { hud.BtnPause.transform, hud.BtnBomb.transform, hud.BtnDash.transform,
                    hud.BtnFieldUpgrade.transform, hud.BtnDonateLife.transform, hud.BtnExitFirstPerson.transform, hud.FirstPersonFirePress.transform };
                foreach (var firstPerson in new[] { false, true })
                {
                    if (firstPerson) preview.ModeButton.onClick.Invoke();
                    typeof(ArcadeHudUICtrl).GetField("combat", flags).SetValue(hudCtrl,
                        new ArcadeCombatHudSnapshot { FirstPersonSeconds = firstPerson ? 10 : 0, PlayerHealth = 100 });
                    typeof(ArcadeHudUICtrl).GetField("hasCombatSnapshot", flags).SetValue(hudCtrl, true);
                    typeof(ArcadeHudUICtrl).GetMethod("ApplyJoystickLayout", flags).Invoke(hudCtrl, null);
                    Assert.That(preview.LayoutArea.rect.size, Is.EqualTo(hud.JoystickLayoutArea.rect.size));
                    for (var i = 0; i < (firstPerson ? 7 : 5); i++)
                    {
                        // 必须比较最终屏幕几何；只比各自局部坐标会漏掉整块预览被缩小的回归。
                        var expected = MobileControlLayoutGeometry.Bounds((RectTransform)root.transform, (RectTransform)previews[i]);
                        var result = MobileControlLayoutGeometry.Bounds((RectTransform)root.transform, (RectTransform)actual[i]);
                        Assert.That(Vector2.Distance(expected.center, result.center), Is.LessThan(.1f), actual[i].name);
                        Assert.That(Vector2.Distance(expected.size, result.size), Is.LessThan(.1f), actual[i].name);
                    }
                    var move = MobileControlLayoutGeometry.Bounds((RectTransform)root.transform, (RectTransform)preview.MovePreview.transform);
                    var actualMove = MobileControlLayoutGeometry.Bounds((RectTransform)root.transform, (RectTransform)hud.MoveJoystickRoot.transform);
                    Assert.That(Vector2.Distance(move.center, actualMove.center), Is.LessThan(.1f));
                    Assert.That(Vector2.Distance(move.size, actualMove.size), Is.LessThan(.1f));
                }
                ctrl.Hide();
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Editor_ToolbarCanCollapseWithoutChangingLayoutOrDraftAndReopensExpanded()
        {
            var architecture = CreateEditorArchitecture();
            var root = CreateEditor(out var ctrl, out var ui);
            try
            {
                ctrl.Show();
                var toolbar = typeof(MobileControlLayoutUI).GetField("ToolbarRoot")?.GetValue(ui) as GameObject;
                var collapse = typeof(MobileControlLayoutUI).GetField("CollapseToolbarButton")?.GetValue(ui) as Button;
                var expand = typeof(MobileControlLayoutUI).GetField("ExpandToolbarButton")?.GetValue(ui) as Button;
                Assert.That(toolbar, Is.Not.Null, "工具栏需要生成的正式View引用");
                Assert.That(collapse, Is.Not.Null);
                Assert.That(expand, Is.Not.Null);
                ui.SizeSlider.value = 1.4f;
                var before = MobileControlLayoutGeometry.Bounds((RectTransform)root.transform, (RectTransform)ui.MovePreview.transform);
                collapse.onClick.Invoke();
                Assert.That(toolbar.activeSelf, Is.False);
                Assert.That(expand.gameObject.activeInHierarchy, Is.True);
                Assert.That(ui.MovePreview.gameObject.activeInHierarchy, Is.True);
                Assert.That(MobileControlLayoutGeometry.Bounds((RectTransform)root.transform, (RectTransform)ui.MovePreview.transform), Is.EqualTo(before));
                Assert.That(architecture.GetModel<MobileControlLayoutModel>().ToData().Move.Scale, Is.EqualTo(1));
                expand.onClick.Invoke();
                Assert.That(toolbar.activeSelf, Is.True);
                Assert.That(expand.gameObject.activeSelf, Is.False);
                Assert.That(ui.SizeSlider.value, Is.EqualTo(1.4f));
                ui.TrialButton.onClick.Invoke();
                collapse.onClick.Invoke(); expand.onClick.Invoke();
                Assert.That(ui.MovePreview.Trial, Is.True, "收起工具栏不能退出试用");
                collapse.onClick.Invoke(); ctrl.Hide(); ctrl.Show();
                Assert.That(toolbar.activeSelf, Is.True);
                Assert.That(expand.gameObject.activeSelf, Is.False);
                Assert.That(ui.SizeSlider.value, Is.EqualTo(1));
                ctrl.Hide();
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ArcadeDefaults_MoveBothThumbsInwardAndMatchEditorProjection()
        {
            var layout = new MobileControlLayoutData().Sanitize();
            foreach (var size in new[] { new Vector2(1920, 1080), new Vector2(2400, 1080), new Vector2(1800, 900) })
            {
                var move = MobileControlLayoutGeometry.Fit(size, layout.Move, true);
                var aim = MobileControlLayoutGeometry.Fit(size, layout.Aim, false);
                Assert.That(move.x / size.x, Is.GreaterThanOrEqualTo(.17f));
                Assert.That(aim.x / size.x, Is.LessThanOrEqualTo(.83f));
                Assert.That(move.y / size.y, Is.GreaterThanOrEqualTo(.23f));
                Assert.That(aim.y, Is.EqualTo(move.y).Within(.01f));
                Assert.That(MobileControlLayoutGeometry.Fit(size, layout.OrdinaryStick(true), true), Is.EqualTo(move));
                Assert.That(MobileControlLayoutGeometry.Fit(size, layout.OrdinaryStick(false), false), Is.EqualTo(aim));
            }
        }

        [Test]
        public void LegacyDefaultSticks_MigrateIdempotentlyWithoutOverwritingCustomThumbs()
        {
            var layout = JsonConvert.DeserializeObject<MobileControlLayoutData>(
                "{\"move\":{\"x\":0.114583336,\"y\":0.18981482,\"scale\":1.3,\"opacity\":0.6},\"aim\":{\"x\":0.8802083,\"y\":0.18981482}}").Sanitize();
            Assert.That(layout.Move.X, Is.GreaterThan(.17f));
            Assert.That(layout.Aim.X, Is.LessThan(.83f));
            Assert.That(layout.Move.Scale, Is.EqualTo(1.3f));
            Assert.That(layout.Move.Opacity, Is.EqualTo(.6f));
            var saved = JsonConvert.SerializeObject(layout);
            Assert.That(JsonConvert.SerializeObject(layout.Sanitize()), Is.EqualTo(saved));
            layout.Move.X = .28f; layout.Move.Y = .31f;
            layout.Aim.X = .76f; layout.Aim.Y = .29f;
            var copy = layout.Clone();
            Assert.That(copy.Move.X, Is.EqualTo(.28f));
            Assert.That(copy.Move.Y, Is.EqualTo(.31f));
            Assert.That(copy.Aim.X, Is.EqualTo(.76f));
            Assert.That(copy.Aim.Y, Is.EqualTo(.29f));
        }

        [Test]
        public void Editor_FirstPersonPreviewKeepsFireSeparateAndCancelDiscardsItsDraft()
        {
            var architecture = CreateEditorArchitecture();
            var root = CreateEditor(out var ctrl, out var ui);
            try
            {
                ctrl.Show();
                Assert.That(ui.AimPreview.gameObject.activeSelf, Is.True);
                Assert.That(ui.FirstPersonFirePreview.gameObject.activeSelf, Is.False);
                ui.ModeButton.onClick.Invoke();
                Assert.That(ui.AimPreview.gameObject.activeSelf, Is.False);
                Assert.That(ui.FirstPersonFirePreview.gameObject.activeSelf, Is.True);
                Assert.That(ui.ExitFirstPersonPreview.gameObject.activeSelf, Is.True);
                var point = ui.LayoutArea.TransformPoint(ui.LayoutArea.rect.min + ui.LayoutArea.rect.size * .6f);
                ui.FirstPersonFirePreview.Dragged(RectTransformUtility.WorldToScreenPoint(null, point));
                ui.SaveButton.onClick.Invoke();
                var saved = architecture.GetModel<MobileControlLayoutModel>().ToData();
                Assert.That(saved.FirstPersonFire.X, Is.EqualTo(.6f).Within(.001f));
                ctrl.Show(); ui.ResetButton.onClick.Invoke(); ui.CancelButton.onClick.Invoke();
                Assert.That(architecture.GetModel<MobileControlLayoutModel>().ToData().FirstPersonFire.X, Is.EqualTo(.6f).Within(.001f));
            }
            finally { Object.DestroyImmediate(root); }
        }
        [Test]
        public void ButtonPositions_SurviveSettingsCloneAndLegacyDefaults()
        {
            var data = JsonConvert.DeserializeObject<SettingsData>("{\"mobileControlLayout\":{\"move\":{\"x\":0.3,\"y\":0.2},\"pause\":{\"x\":0.7,\"y\":0.8},\"bomb\":{\"x\":0.8,\"y\":0.4}}}").Sanitize();
            var saved = JObject.FromObject(data.Clone())["mobileControlLayout"];
            Assert.That((float)saved["bomb"]["x"], Is.EqualTo(.8f));
            Assert.That((float)saved["pause"]["x"], Is.EqualTo(.7f));
            Assert.That((float)saved["move"]["x"], Is.EqualTo(.3f));
            var legacy = JsonConvert.DeserializeObject<SettingsData>("{\"version\":1}").Sanitize();
            Assert.That(legacy.MobileControlLayout.Bomb, Is.Not.Null);
            Assert.That(legacy.MobileControlLayout.FirstPersonFire, Is.Not.Null);
        }
        [Test]
        public void ArcadeSticks_HaveMatchingFixedRingsAndNonBlockingLabels()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab");
            var ui = prefab.GetComponent<ArcadeHudUI>();
            foreach (var wrapper in new[] { ui.MoveJoystickRoot, ui.AimJoystickRoot })
            {
                foreach (var label in wrapper.GetComponentsInChildren<TMPro.TMP_Text>(true))
                    Assert.That(label.raycastTarget, Is.False, "摇杆现有提示文字不能阻挡触控");
                Assert.That(wrapper.transform.Find("Ring"), Is.Not.Null, "底盘必须独立固定，不能跟随移动输入面一起移动");
            }
            var moveKnob = ui.MoveJoystick.transform.Find("Handle").GetComponent<Image>();
            var aimKnob = ui.AimJoystick.transform.Find("Handle").GetComponent<Image>();
            Assert.That(moveKnob.sprite, Is.EqualTo(aimKnob.sprite));
            Assert.That(moveKnob.rectTransform.sizeDelta, Is.EqualTo(aimKnob.rectTransform.sizeDelta));
            Assert.That(moveKnob.raycastTarget, Is.False);
        }

        [Test]
        public void Editor_ButtonDragsSaveCancelAndResetWithoutExecutingGameplayActions()
        {
            var architecture = CreateEditorArchitecture();
            var root = CreateEditor(out var ctrl, out var ui);
            try
            {
                ctrl.Show();
                var previews = new[] { ui.PausePreview, ui.BombPreview, ui.DashPreview, ui.FieldUpgradePreview, ui.DonateLifePreview, ui.ExitFirstPersonPreview, ui.FirstPersonFirePreview };
                var area = ui.LayoutArea;
                for (var i = 0; i < previews.Length; i++)
                {
                    var point = area.TransformPoint(area.rect.min + new Vector2(area.rect.width * (.6f + i * .06f), area.rect.height * .6f));
                    previews[i].Selected();
                    previews[i].Dragged(RectTransformUtility.WorldToScreenPoint(null, point));
                    Assert.That(ui.SizeSlider.interactable, Is.False, "按钮只调整位置");
                    Assert.That(previews[i].GetComponent<Button>(), Is.Null, "预览不能带真实暂停、背包或攻击回调");
                    Assert.That(previews[i].GetComponent<MobileCombatPressView>(), Is.Null);
                }
                Assert.That(architecture.GetModel<MobileControlLayoutModel>().ToData().Pause.X, Is.EqualTo(1818f / 1920f), "草稿不能提前写入权威设置");
                ui.SaveButton.onClick.Invoke();
                var saved = architecture.GetModel<MobileControlLayoutModel>().ToData();
                for (var i = 0; i < previews.Length; i++)
                {
                    Assert.That(saved.Button((MobileLayoutButton)i).X, Is.EqualTo(.6f + i * .06f).Within(.001f));
                    Assert.That(saved.Button((MobileLayoutButton)i).Y, Is.EqualTo(.6f).Within(.001f));
                }
                ctrl.Show();
                ui.ResetButton.onClick.Invoke(); ui.CancelButton.onClick.Invoke();
                Assert.That(architecture.GetModel<MobileControlLayoutModel>().ToData().Pause.X, Is.EqualTo(.6f).Within(.001f));
                ctrl.Show(); ui.ResetButton.onClick.Invoke(); ui.SaveButton.onClick.Invoke();
                Assert.That(architecture.GetModel<MobileControlLayoutModel>().ToData().Pause.X, Is.EqualTo(1818f / 1920f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Editor_ViewUnloadReleasesDraftAfterBaseClearsViewReference()
        {
            CreateEditorArchitecture();
            var root = CreateEditor(out var ctrl, out var ui);
            try
            {
                ctrl.Show();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(BaseUICtrl).GetMethod("OnBaseUIDestroyed", flags).Invoke(ctrl, new object[] { ui });
                Assert.That(ctrl.IsShow(), Is.False);
                Assert.That(typeof(MobileControlLayoutUICtrl).GetField("draft", flags).GetValue(ctrl), Is.Null);
                LogAssert.NoUnexpectedReceived();
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(1920, 1080)]
        [TestCase(2400, 1080)]
        [TestCase(1800, 900)]
        public void Editor_DefaultButtonsAvoidEachOtherAndBothSticks(int width, int height)
        {
            CreateEditorArchitecture();
            var root = CreateEditor(out var ctrl, out var ui);
            ((RectTransform)root.transform).sizeDelta = new Vector2(width, height);
            try
            {
                ctrl.Show();
                var targets = new[] { ui.PausePreview.transform, ui.BombPreview.transform, ui.DashPreview.transform,
                    ui.FieldUpgradePreview.transform, ui.DonateLifePreview.transform, ui.MovePreview.transform, ui.AimPreview.transform };
                var bounds = targets.Select(t => MobileControlLayoutGeometry.Bounds(ui.LayoutArea, (RectTransform)t)).ToArray();
                for (var i = 0; i < bounds.Length; i++)
                {
                    Assert.That(bounds[i].xMin, Is.GreaterThanOrEqualTo(-.01f));
                    Assert.That(bounds[i].yMin, Is.GreaterThanOrEqualTo(-.01f));
                    Assert.That(bounds[i].xMax, Is.LessThanOrEqualTo(width + .01f));
                    Assert.That(bounds[i].yMax, Is.LessThanOrEqualTo(height + .01f));
                    for (var j = 0; j < i; j++) Assert.That(bounds[i].Overlaps(bounds[j]), Is.False, targets[i].name + " overlap " + targets[j].name);
                }
                ctrl.Hide();
            }
            finally { Object.DestroyImmediate(root); }
        }

        [SetUp]
        public void SetUp()
        {
            ResourceLoader.SetLoaderForTests(new AssetLoader());
            LocalizationLanguages.ResetForTests();
        }
        [TearDown]
        public void TearDown()
        {
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            TestReset.ResetArchitecture();
        }

        [Test]
        public void ArcadeJoystick_UsesIndependentGeneratedLayoutWrapper()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab");
            var ui = prefab.GetComponent<ArcadeHudUI>();
            var field = typeof(ArcadeHudUI).GetField("MoveJoystickRoot");
            Assert.That(field, Is.Not.Null, "街机必须接入共享布局容器");
            var wrapper = (GameObject)field.GetValue(ui);
            Assert.That(wrapper, Is.Not.Null);
            Assert.That(ui.MoveJoystick.transform.parent, Is.EqualTo(wrapper.transform));
            Assert.That(((RectTransform)ui.MoveJoystick.transform).anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(((RectTransform)ui.MoveJoystick.transform).anchorMin, Is.EqualTo(Vector2.one * 0.5f));
            Assert.That(typeof(ArcadeHudUI).GetField("MoveOpacity").GetValue(ui), Is.EqualTo(wrapper.GetComponent<CanvasGroup>()));
        }
        [Test]
        public void SettingsJson_PreservesMobileLayoutThroughClone()
        {
            var data = JsonConvert.DeserializeObject<SettingsData>(
                "{\"mobileControlLayout\":{\"move\":{\"x\":0.2,\"y\":0.25,\"scale\":1.3,\"opacity\":0.6},\"aim\":{\"x\":0.8,\"y\":0.2,\"scale\":0.8,\"opacity\":0.4}}}");
            var json = JObject.Parse(JsonConvert.SerializeObject(data.Clone()));
            Assert.That(json["mobileControlLayout"], Is.Not.Null,
                "玩家布局必须进入正式设置文件，克隆不可丢失布局。");
            Assert.That((float)json["mobileControlLayout"]["move"]["scale"], Is.EqualTo(1.3f));
            Assert.That((float)json["mobileControlLayout"]["aim"]["x"], Is.EqualTo(0.8f));
        }

        [Test]
        public void LegacySettings_DefaultsAndCloneAreIndependent()
        {
            var old = JsonConvert.DeserializeObject<SettingsData>("{\"version\":1}").Sanitize();
            Assert.That(old.MobileControlLayout.Move.Scale, Is.EqualTo(1));
            var copy = old.Clone(); copy.MobileControlLayout.Move.Scale = 1.4f;
            Assert.That(old.MobileControlLayout.Move.Scale, Is.EqualTo(1));
            var invalid = new MobileControlLayoutData
            {
                Move = new MobileStickLayoutData { X = float.NaN, Y = float.PositiveInfinity, Scale = -1, Opacity = 0 },
                Aim = null
            }.Sanitize();
            Assert.That(invalid.Move.X, Is.EqualTo(.18f));
            Assert.That(invalid.Move.Scale, Is.EqualTo(0.7f));
            Assert.That(invalid.Move.Opacity, Is.EqualTo(0.25f));
            Assert.That(invalid.Aim, Is.Not.Null);
        }

        [Test]
        public void SettingsFile_RoundTripKeepsBothSticks()
        {
            var root = Path.Combine(Path.GetTempPath(), "MobileLayout", Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SettingsFileStore(root);
                var data = new SettingsData(); data.MobileControlLayout.Move.X = 0.3f;
                data.MobileControlLayout.Aim.Opacity = 0.4f;
                data.MobileControlLayout.Bomb.X = .7f;
                data.MobileControlLayout.Status.X = .2f;
                data.MobileControlLayout.Health.Scale = 1.4f;
                data.MobileControlLayout.Weapon.Y = .35f;
                Assert.That(store.Save(data, out var error), Is.True, error);
                var read = new SettingsFileStore(root).LoadOrDefault(out var result);
                Assert.That(result, Is.EqualTo(SettingsLoadResult.LoadedPrimary));
                Assert.That(read.MobileControlLayout.Move.X, Is.EqualTo(0.3f));
                Assert.That(read.MobileControlLayout.Aim.Opacity, Is.EqualTo(0.4f));
                Assert.That(read.MobileControlLayout.Bomb.X, Is.EqualTo(.7f));
                Assert.That(read.MobileControlLayout.Status.X, Is.EqualTo(.2f));
                Assert.That(read.MobileControlLayout.Health.Scale, Is.EqualTo(1.4f));
                Assert.That(read.MobileControlLayout.Weapon.Y, Is.EqualTo(.35f));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestCase(1920, 1080, 0.7f)]
        [TestCase(1920, 1080, 1.5f)]
        [TestCase(2146, 966, 1.5f)]
        public void Geometry_AvoidsEdgesButtonsAndOtherStick(int width, int height, float scale)
        {
            var size = new Vector2(width, height);
            var obstacles = new[] { new Rect(80, 355, 140, 70), new Rect(width - 230, 352, 150, 76) };
            var left = new MobileStickLayoutData { X = 0, Y = 0, Scale = scale };
            var right = new MobileStickLayoutData { X = 1, Y = 0, Scale = scale };
            var a = MobileControlLayoutGeometry.Fit(size, left, true, obstacles);
            var b = MobileControlLayoutGeometry.Fit(size, right, false, obstacles);
            var extent = 165 * scale;
            Assert.That(a.x, Is.GreaterThanOrEqualTo(extent));
            Assert.That(a.y, Is.GreaterThanOrEqualTo(extent));
            Assert.That(b.x + extent, Is.LessThanOrEqualTo(width));
            Assert.That(MobileControlLayoutGeometry.IsFree(a, scale, obstacles), Is.True);
            Assert.That(MobileControlLayoutGeometry.IsFree(b, scale, obstacles), Is.True);
            Assert.That(b.x - a.x, Is.GreaterThan(180 * scale));
        }

        [Test]
        public void GeneratedPrefabs_AllReferencesResolveAndPreviewHasNoGamepadControls()
        {
            foreach (var path in new[] { "Home/MobileControlLayout", "Home/Setting", "Home/Page/SettingMobile", "Arcade/ArcadeHud" })
            {
                var root = PrefabUtility.LoadPrefabContents("Assets/Game/Prefabs/UI/" + path + ".prefab");
                try
                {
                    var collector = root.GetComponent<ReferenceCollector>(); var view = root.GetComponent<BaseUI>();
                    Assert.That(collector, Is.Not.Null, path); Assert.That(view, Is.Not.Null, path);
                    foreach (var item in collector.data)
                    {
                        var field = view.GetType().GetField(item.key);
                        Assert.That(field, Is.Not.Null, path + ":" + item.key);
                        Assert.That(field.GetValue(view) as Object, Is.Not.Null, path + ":" + item.key);
                    }
                    if (path == "Home/Setting") Assert.That(root.GetComponent<SettingUI>().SettingMobileRoot.GetComponent<SettingMobilePageUI>(), Is.Not.Null);
                    if (path == "Home/MobileControlLayout")
                    {
                        Assert.That(root.GetComponentsInChildren<UnityEngine.InputSystem.OnScreen.OnScreenControl>(true), Is.Empty);
                        var editor = root.GetComponent<MobileControlLayoutUI>();
                        Assert.That(editor.MovePreview.Knob, Is.Not.Null); Assert.That(editor.AimPreview.Knob, Is.Not.Null);
                        Assert.That(editor.MovePreview.Knob.gameObject.activeSelf, Is.True);
                        Assert.That(editor.AimPreview.Knob.gameObject.activeSelf, Is.True);
                        Assert.That(root.GetComponent<Image>().raycastTarget, Is.True);
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }

        [Test]
        public void Preview_TrialReturnsToCenterWithoutChangingLayout()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/MobileControlLayout.prefab"));
            var es = new GameObject("PreviewEventSystem", typeof(EventSystem));
            try
            {
                var handle = root.GetComponent<MobileControlLayoutUI>().MovePreview;
                handle.Trial = true; handle.Knob.anchoredPosition = new Vector2(60, 20);
                handle.ResetGesture();
                Assert.That(handle.Knob.anchoredPosition, Is.EqualTo(Vector2.zero));
                Assert.That(handle.GetComponentsInChildren<UnityEngine.InputSystem.OnScreen.OnScreenControl>(true), Is.Empty);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(es); }
        }

        [Test]
        public void Editor_CancelDiscardsDraftAndReopenLoadsSavedLayout()
        {
            var architecture = CreateEditorArchitecture();
            var root = CreateEditor(out var ctrl, out var ui);
            try
            {
                ctrl.Show();
                ui.SizeSlider.value = 1.4f;
                ui.OpacitySlider.value = 0.4f;
                Assert.That(architecture.GetModel<MobileControlLayoutModel>().ToData().Move.Scale, Is.EqualTo(1));
                ui.CancelButton.onClick.Invoke();
                ctrl.Show();
                Assert.That(ui.SizeSlider.value, Is.EqualTo(1));
                Assert.That(ui.OpacitySlider.value, Is.EqualTo(1));
                ctrl.Hide(); ctrl.Hide();
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Editor_SaveCommitsBothSticksAndTrialIsIsolated()
        {
            var architecture = CreateEditorArchitecture();
            var root = CreateEditor(out var ctrl, out var ui);
            try
            {
                ctrl.Show();
                ui.SizeSlider.value = 1.4f;
                ui.AimButton.onClick.Invoke(); ui.OpacitySlider.value = 0.4f;
                ui.TrialButton.onClick.Invoke();
                Assert.That(ui.SizeSlider.interactable, Is.False);
                Assert.That(ui.MovePreview.Trial, Is.True);
                Assert.That(ui.TrialText.text, Is.EqualTo("返回编辑"));
                var before = ((RectTransform)ui.MovePreview.transform).anchoredPosition;
                ui.MovePreview.OnPointerDown(new PointerEventData(EventSystem.current) { pointerId = 1, position = Vector2.zero });
                ui.MovePreview.OnDrag(new PointerEventData(EventSystem.current) { pointerId = 1, position = new Vector2(400, 400) });
                Assert.That(ui.MovePreview.Knob.anchoredPosition.magnitude, Is.EqualTo(75).Within(0.001f));
                Assert.That(((RectTransform)ui.MovePreview.transform).anchoredPosition, Is.EqualTo(before));
                ui.MovePreview.OnPointerUp(new PointerEventData(EventSystem.current) { pointerId = 1 });
                Assert.That(ui.MovePreview.Knob.anchoredPosition, Is.EqualTo(Vector2.zero));
                ui.SaveButton.onClick.Invoke();
                var saved = architecture.GetModel<MobileControlLayoutModel>().ToData();
                Assert.That(saved.Move.Scale, Is.EqualTo(1.4f)); Assert.That(saved.Aim.Opacity, Is.EqualTo(0.4f));
                Assert.That(ctrl.IsShow(), Is.False);
                ctrl.Show(); Assert.That(ui.SizeSlider.value, Is.EqualTo(1.4f)); ctrl.Hide();
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static Game CreateEditorArchitecture()
        {
            var architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, architecture);
            architecture.RegisterModel(new LocalizationModel()); architecture.RegisterModel(new DisplaySettingsModel());
            architecture.RegisterModel(new AudioSettingsModel()); architecture.RegisterModel(new HotKeyModel());
            architecture.RegisterModel(new TestToolSettingsModel()); architecture.RegisterModel(new MobileControlLayoutModel());
            architecture.RegisterUtility(new LocalizationUtility());
            architecture.RegisterUtility<ISettingsFileStore>(new SuccessfulStore());
            architecture.RegisterSystem(new SettingsSystem()); architecture.RegisterSystem(new MobileInputSystem());
            architecture.GetSystem<MobileInputSystem>().TrySetEditorSimulation(true);
            return architecture;
        }

        [Test]
        public void Editor_DragPreservesGrabOffsetAndDesktopSwitchClosesDraft()
        {
            var architecture = CreateEditorArchitecture();
            var root = CreateEditor(out var ctrl, out var ui);
            try
            {
                ctrl.Show();
                var handle = ui.MovePreview;
                var rect = (RectTransform)handle.transform;
                var start = rect.anchoredPosition;
                var point = RectTransformUtility.WorldToScreenPoint(null, rect.position) + new Vector2(30, 20);
                handle.OnPointerDown(new PointerEventData(EventSystem.current) { pointerId = 1, position = point });
                handle.OnDrag(new PointerEventData(EventSystem.current) { pointerId = 1, position = point + new Vector2(100, 50) });
                Assert.That(rect.anchoredPosition.x, Is.EqualTo(start.x + 100 / rect.parent.lossyScale.x).Within(0.01f));
                Assert.That(rect.anchoredPosition.y, Is.EqualTo(start.y + 50 / rect.parent.lossyScale.y).Within(0.01f));
                architecture.GetSystem<MobileInputSystem>().TrySetEditorSimulation(false);
                Assert.That(ctrl.IsShow(), Is.False);
                Assert.That(architecture.GetModel<MobileControlLayoutModel>().ToData().Move.X, Is.EqualTo(.18f));
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static GameObject CreateEditor(out MobileControlLayoutUICtrl ctrl, out MobileControlLayoutUI ui)
        {
            var canvas = new GameObject("LayoutTestCanvas", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1920, 1080);
            var es = new GameObject("LayoutEventSystem", typeof(EventSystem)); es.transform.SetParent(canvas.transform);
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/MobileControlLayout.prefab"), canvas.transform, false);
            ui = root.GetComponent<MobileControlLayoutUI>();
            ctrl = new MobileControlLayoutUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ctrl, ui);
            return canvas;
        }
        private sealed class SuccessfulStore : ISettingsFileStore
        {
            public SettingsData LoadOrDefault(out SettingsLoadResult result) { result = SettingsLoadResult.CreatedDefault; return new SettingsData(); }
            public bool Save(SettingsData data, out string error) { error = null; return true; }
        }

        [Test]
        public void SaveFailure_RollsBackAuthoritativeLayoutAndSendsNoEvent()
        {
            var architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, architecture);
            architecture.RegisterModel(new LocalizationModel()); architecture.RegisterModel(new DisplaySettingsModel());
            architecture.RegisterModel(new AudioSettingsModel()); architecture.RegisterModel(new HotKeyModel());
            architecture.RegisterModel(new TestToolSettingsModel()); architecture.RegisterModel(new MobileControlLayoutModel());
            architecture.RegisterUtility<ISettingsFileStore>(new FailedStore()); architecture.RegisterSystem(new SettingsSystem());
            var events = 0; var subscription = architecture.RegisterEvent<MobileControlLayoutChangedEvent>(_ => events++);
            try
            {
                var draft = new MobileControlLayoutData(); draft.Move.Scale = 1.4f;
                draft.Pause.X = .7f;
                LogAssert.Expect(LogType.Error, "[SettingsSystem] 保存设置失败：test failure");
                Assert.That(architecture.GetSystem<SettingsSystem>().SaveMobileControlLayout(draft), Is.False);
                Assert.That(architecture.GetModel<MobileControlLayoutModel>().ToData().Move.Scale, Is.EqualTo(1));
                Assert.That(draft.Move.Scale, Is.EqualTo(1.4f)); Assert.That(events, Is.Zero);
                Assert.That(architecture.GetModel<MobileControlLayoutModel>().ToData().Pause.X, Is.EqualTo(1818f / 1920f));
            }
            finally { subscription.UnRegister(); }
        }

        private sealed class FailedStore : ISettingsFileStore
        {
            public SettingsData LoadOrDefault(out SettingsLoadResult result) { result = SettingsLoadResult.CreatedDefault; return new SettingsData(); }
            public bool Save(SettingsData data, out string error) { error = "test failure"; return false; }
        }
        private sealed class AssetLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Config/Localization/" + path);
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> callback) where T : Object
            { callback?.Invoke(LoadAsset<T>(path)); return ResourceLoadHandle.Empty; }
            public void Release(ResourceLoadHandle handle) { }
        }
    }
}
