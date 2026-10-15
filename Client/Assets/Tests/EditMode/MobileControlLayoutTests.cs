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
        public void Editor_HudRowsIndependentlyDragScaleSaveAndCancel()
        {
            var architecture = CreateEditorArchitecture();
            var root = CreateEditor(out var ctrl, out var ui);
            try
            {
                ctrl.Show();
                var names = new[] { "StatusPreview", "HealthPreview", "WeaponPreview" };
                for (var i = 0; i < names.Length; i++)
                {
                    var name = names[i];
                    var field = typeof(MobileControlLayoutUI).GetField(name);
                    Assert.That(field, Is.Not.Null, "设置页需要独立的信息栏预览：" + name);
                    var handle = (MobileLayoutStickHandle)field.GetValue(ui);
                    handle.Selected();
                    Assert.That(ui.SizeSlider.interactable, Is.True);
                    Assert.That(ui.OpacitySlider.interactable, Is.False);
                    ui.SizeSlider.value = 1.1f + i * .1f;
                    var point = ui.LayoutArea.TransformPoint(ui.LayoutArea.rect.min + ui.LayoutArea.rect.size * (.3f + i * .2f));
                    handle.Dragged(RectTransformUtility.WorldToScreenPoint(null, point));
                }
                ui.SaveButton.onClick.Invoke();
                var saved = JObject.FromObject(architecture.GetModel<MobileControlLayoutModel>().ToData());
                var keys = new[] { "status", "health", "weapon" };
                for (var i = 0; i < keys.Length; i++)
                {
                    Assert.That((float)saved[keys[i]]["x"], Is.EqualTo(.3f + i * .2f).Within(.001f));
                    Assert.That((float)saved[keys[i]]["scale"], Is.EqualTo(1.1f + i * .1f).Within(.001f));
                }
                ctrl.Show(); ui.ResetButton.onClick.Invoke(); ui.CancelButton.onClick.Invoke();
                Assert.That(JObject.FromObject(architecture.GetModel<MobileControlLayoutModel>().ToData()), Is.EqualTo(saved));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ButtonPositions_SurviveSettingsCloneAndLegacyDefaults()
        {
            var input = "{\"mobileControlLayout\":{\"move\":{\"x\":0.3,\"y\":0.2},\"pause\":{\"x\":0.7,\"y\":0.8},\"attack\":{\"x\":0.8,\"y\":0.4},\"reload\":{\"x\":0.6,\"y\":0.3},\"inventory\":{\"x\":0.7,\"y\":0.7},\"build\":{\"x\":0.7,\"y\":0.6}}}";
            var data = JsonConvert.DeserializeObject<SettingsData>(input).Sanitize();
            var saved = JObject.Parse(JsonConvert.SerializeObject(data.Clone()))["mobileControlLayout"];
            foreach (var key in new[] { "pause", "attack", "reload", "inventory", "build" })
                Assert.That(saved[key], Is.Not.Null, "克隆和正式保存不能丢失按钮位置：" + key);
            Assert.That((float)saved["reload"]["x"], Is.EqualTo(0.6f));
            Assert.That((float)saved["move"]["x"], Is.EqualTo(0.3f), "增加按钮设置不能重置旧摇杆位置");
            var legacy = JObject.Parse(JsonConvert.SerializeObject(JsonConvert.DeserializeObject<SettingsData>("{\"version\":1}").Sanitize()))["mobileControlLayout"];
            Assert.That((float)legacy["pause"]["x"], Is.LessThan(.2f));
            Assert.That((float)legacy["build"]["x"], Is.LessThan(.2f));
            Assert.That((float)legacy["inventory"]["x"], Is.GreaterThan(.8f));
        }

        [Test]
        public void OrdinarySticks_HaveMatchingFixedRingsAndMovingKnobsWithoutLabels()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/MobileJoystick.prefab");
            var ui = prefab.GetComponent<MobileJoystickUI>();
            foreach (var wrapper in new[] { ui.MoveJoystickRoot, ui.BaseAimRoot })
            {
                Assert.That(wrapper.GetComponentsInChildren<TMPro.TMP_Text>(true), Is.Empty, "摇杆不应带玩家文字");
                Assert.That(wrapper.transform.Find("Ring"), Is.Not.Null, "底盘必须独立固定，不能跟随移动输入面一起移动");
            }
            var moveKnob = ui.JoystickSurface.transform.Find("Handle").GetComponent<Image>();
            var aimKnob = ui.BaseAimSurface.Handle.GetComponent<Image>();
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
                var previews = new[] { ui.PausePreview, ui.InventoryPreview, ui.BuildPreview, ui.AttackPreview, ui.ReloadPreview };
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
                Assert.That(architecture.GetModel<MobileControlLayoutModel>().ToData().Pause.X, Is.EqualTo(.05f), "草稿不能提前写入权威设置");
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
                Assert.That(architecture.GetModel<MobileControlLayoutModel>().ToData().Pause.X, Is.EqualTo(.05f));
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
                var targets = new[] { ui.PausePreview.transform, ui.InventoryPreview.transform, ui.BuildPreview.transform,
                    ui.AttackPreview.transform, ui.ReloadPreview.transform, ui.MovePreview.transform, ui.AimPreview.transform };
                var bounds = targets.Select(t => MobileControlLayoutGeometry.Bounds(ui.LayoutArea, (RectTransform)t)).ToArray();
                for (var i = 0; i < bounds.Length; i++)
                {
                    Assert.That(bounds[i].xMin, Is.GreaterThanOrEqualTo(-.01f));
                    Assert.That(bounds[i].yMin, Is.GreaterThanOrEqualTo(-.01f));
                    Assert.That(bounds[i].xMax, Is.LessThanOrEqualTo(width + .01f));
                    Assert.That(bounds[i].yMax, Is.LessThanOrEqualTo(height + .01f));
                    for (var j = 0; j < i; j++) Assert.That(bounds[i].Overlaps(bounds[j]), Is.False, targets[i].name + " overlap " + targets[j].name);
                }
                Assert.That(bounds[0].center.x, Is.LessThan(width * .2f));
                Assert.That(bounds[2].center.x, Is.LessThan(width * .2f));
                Assert.That(bounds[1].center.x, Is.GreaterThan(width * .8f));
                Assert.That(bounds[0].center.y, Is.EqualTo(bounds[1].center.y).Within(.01f));
                Assert.That(bounds[0].center.x, Is.EqualTo(bounds[2].center.x).Within(.01f));
                Assert.That(bounds[0].center.y, Is.GreaterThan(bounds[2].center.y));
                Assert.That(bounds[4].center.x, Is.LessThan(bounds[3].center.x));
                Assert.That(bounds[4].center.y, Is.LessThan(bounds[3].center.y));
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
        public void OrdinaryJoystick_InitialHideAndReleasePreserveLayoutCenter()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/MobileJoystick.prefab"));
            try
            {
                var ui = root.GetComponent<MobileJoystickUI>();
                var area = (RectTransform)ui.ContentRoot.transform;
                area.sizeDelta = new Vector2(1920, 1080);
                var before = MobileControlLayoutGeometry.Bounds(area, (RectTransform)ui.JoystickSurface.transform).center;
                ui.SetContentVisible(false);
                typeof(UnityEngine.InputSystem.OnScreen.OnScreenStick)
                    .GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui.JoystickSurface, null);
                ui.SetContentVisible(true);
                ui.JoystickSurface.OnPointerUp(null);
                var after = MobileControlLayoutGeometry.Bounds(area, (RectTransform)ui.JoystickSurface.transform).center;
                Assert.That(after, Is.EqualTo(before), "首次菜单隐藏不能把回弹中心改成屏幕左下角");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void OrdinaryJoystick_UsesIndependentGeneratedLayoutWrapper()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/MobileJoystick.prefab");
            var ui = prefab.GetComponent<MobileJoystickUI>();
            var field = typeof(MobileJoystickUI).GetField("MoveJoystickRoot");
            Assert.That(field, Is.Not.Null, "普通地图必须接入共享布局容器");
            var wrapper = (GameObject)field.GetValue(ui);
            Assert.That(wrapper, Is.Not.Null);
            Assert.That(ui.JoystickSurface.transform.parent, Is.EqualTo(wrapper.transform));
            Assert.That(((RectTransform)ui.JoystickSurface.transform).anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(((RectTransform)ui.JoystickSurface.transform).anchorMin, Is.EqualTo(Vector2.one * 0.5f));
            Assert.That(typeof(MobileJoystickUI).GetField("MoveOpacity").GetValue(ui), Is.EqualTo(wrapper.GetComponent<CanvasGroup>()));
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
            Assert.That(invalid.Move.X, Is.EqualTo(220f / 1920f));
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
                data.MobileControlLayout.Reload.X = .7f;
                data.MobileControlLayout.Status.X = .2f;
                data.MobileControlLayout.Health.Scale = 1.4f;
                data.MobileControlLayout.Weapon.Y = .35f;
                Assert.That(store.Save(data, out var error), Is.True, error);
                var read = new SettingsFileStore(root).LoadOrDefault(out var result);
                Assert.That(result, Is.EqualTo(SettingsLoadResult.LoadedPrimary));
                Assert.That(read.MobileControlLayout.Move.X, Is.EqualTo(0.3f));
                Assert.That(read.MobileControlLayout.Aim.Opacity, Is.EqualTo(0.4f));
                Assert.That(read.MobileControlLayout.Reload.X, Is.EqualTo(.7f));
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
            foreach (var path in new[] { "Home/MobileControlLayout", "Home/Setting", "Home/Page/SettingMobile", "Arcade/ArcadeHud", "Player/MobileJoystick", "Player/GameplayGuideHud" })
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

        [TestCase(1920, 1080)]
        [TestCase(2400, 1080)]
        [TestCase(1800, 900)]
        public void OrdinaryJoystick_SavedLayoutMatchesPreviewAndUpdatesAfterResize(int width, int height)
        {
            var architecture = CreateEditorArchitecture();
            var canvas = new GameObject("SharedLayoutCanvas", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var canvasRect = (RectTransform)canvas.transform;
            canvasRect.sizeDelta = new Vector2(width, height);
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/MobileJoystick.prefab"), canvas.transform, false);
            try
            {
                var ui = root.GetComponent<MobileJoystickUI>();
                var ctrl = new MobileJoystickUICtrl();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(BaseUICtrl).GetField("BaseUI", flags).SetValue(ctrl, ui);
                typeof(MobileJoystickUICtrl).GetMethod("OnInit", flags).Invoke(ctrl, null);
                var refresh = typeof(MobileJoystickUICtrl).GetMethod("RefreshLayout", flags);
                refresh.Invoke(ctrl, null);
                var draft = new MobileControlLayoutData();
                draft.Move.X = 0.3f; draft.Move.Y = 0.32f; draft.Move.Scale = 1.4f; draft.Move.Opacity = 0.45f;
                Assert.That(architecture.GetSystem<SettingsSystem>().SaveMobileControlLayout(draft), Is.True);
                refresh.Invoke(ctrl, null);
                Assert.That(((RectTransform)ui.MoveJoystickRoot.transform).anchoredPosition,
                    Is.EqualTo(MobileControlLayoutGeometry.Fit(ui.JoystickLayoutArea.rect.size, draft.Move, true)));
                Assert.That(ui.MoveJoystickRoot.transform.localScale.x, Is.EqualTo(1.4f));
                Assert.That(ui.MoveOpacity.alpha, Is.EqualTo(0.45f));
                ui.SetContentVisible(false);
                typeof(UnityEngine.InputSystem.OnScreen.OnScreenStick).GetMethod("Start", flags).Invoke(ui.JoystickSurface, null);
                ui.SetContentVisible(true);
                ui.JoystickSurface.OnPointerUp(null);
                canvasRect.sizeDelta = new Vector2(height * 2, height);
                refresh.Invoke(ctrl, null);
                Assert.That(((RectTransform)ui.MoveJoystickRoot.transform).anchoredPosition,
                    Is.EqualTo(MobileControlLayoutGeometry.Fit(ui.JoystickLayoutArea.rect.size, draft.Move, true)));
                var bounds = MobileControlLayoutGeometry.Bounds(ui.JoystickLayoutArea, (RectTransform)ui.JoystickSurface.transform);
                Assert.That(bounds.xMin, Is.GreaterThanOrEqualTo(0));
                Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(0));
                Assert.That(bounds.xMax, Is.LessThanOrEqualTo(ui.JoystickLayoutArea.rect.width));
                Assert.That(bounds.yMax, Is.LessThanOrEqualTo(ui.JoystickLayoutArea.rect.height));
                Assert.That(architecture.GetModel<MobileControlLayoutModel>().ToData().Move.X, Is.EqualTo(0.3f));
            }
            finally { Object.DestroyImmediate(canvas); }
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
                Assert.That(rect.anchoredPosition.x, Is.EqualTo(start.x + 100).Within(0.01f));
                Assert.That(rect.anchoredPosition.y, Is.EqualTo(start.y + 50).Within(0.01f));
                architecture.GetSystem<MobileInputSystem>().TrySetEditorSimulation(false);
                Assert.That(ctrl.IsShow(), Is.False);
                Assert.That(architecture.GetModel<MobileControlLayoutModel>().ToData().Move.X, Is.EqualTo(220f / 1920f));
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
                Assert.That(architecture.GetModel<MobileControlLayoutModel>().ToData().Pause.X, Is.EqualTo(.05f));
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
