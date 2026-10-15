using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace Game.Tests
{
    public sealed class AppearanceGalleryLayoutTests
    {
        private Scene scene;
        private AppearanceGallerySceneView view;
        private Camera camera;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/Maps/AppearanceGallery.unity", OpenSceneMode.Additive);
            view = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<AppearanceGallerySceneView>(true)).Single();
            camera = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true)).Single(x => x.name == "PreviewCamera");
        }

        [TearDown]
        public void TearDown()
        {
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
        }

        [Test]
        public void Background_CoversUnsafeScreenEdgesWithoutRenderingBaseObjects()
        {
            var background = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true))
                .SingleOrDefault(x => x.name == "GalleryBackgroundCamera");
            Assert.That(background, Is.Not.Null, "安全区外也必须遮住基地背景");
            Assert.That(background.rect, Is.EqualTo(new Rect(0, 0, 1, 1)));
            Assert.That(background.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
            Assert.That(background.backgroundColor.a, Is.EqualTo(1f));
            Assert.That(background.cullingMask, Is.Zero, "背景相机只清屏，不渲染基地或预览对象");
            Assert.That(background.depth, Is.LessThan(camera.depth));
            Assert.That(background.enabled, Is.True);
        }

        [Test]
        public void Controls_BackgroundIsOpaqueAndControlsKeepSafeAreaFitting()
        {
            var controls = view.GetComponentInChildren<Canvas>(true).transform.Find("Controls");
            Assert.That(controls.GetComponent<UnityEngine.UI.Image>().color.a, Is.EqualTo(1f));
            Assert.That(controls.GetComponent<SafeAreaFitter>(), Is.Not.Null);
            Assert.That(view.GetComponentInChildren<Canvas>(true).transform.Find("PreviewControls")
                .GetComponent<SafeAreaFitter>(), Is.Not.Null);
        }

        [TestCase(1)]
        [TestCase(3)]
        public void BrowseVariants_SingleNameAndMultipleChoiceRestoreTheirPresentation(int count)
        {
            var fields = new UnityEditor.SerializedObject(view);
            var dropdown = (UnityEngine.UI.Dropdown)fields.FindProperty("variantDropdown").objectReferenceValue;
            var refresh = typeof(AppearanceGallerySceneView).GetMethod("RefreshVariantSelectionPresentation", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(refresh, Is.Not.Null);
            foreach (var optionCount in new[] { count == 1 ? 3 : 1, count })
            {
                dropdown.ClearOptions();
                dropdown.AddOptions(Enumerable.Range(1, optionCount).Select(i => "外观 " + i).ToList());
                refresh.Invoke(view, null);
            }
            Assert.That(dropdown.captionText.text, Is.EqualTo("外观 1"));
            Assert.That(dropdown.captionText.enabled, Is.True);
            Assert.That(dropdown.interactable, Is.EqualTo(count > 1));
            Assert.That(dropdown.targetGraphic.enabled, Is.EqualTo(count > 1));
            Assert.That(dropdown.transform.Find("Arrow").gameObject.activeSelf, Is.EqualTo(count > 1));
            Assert.That(((RectTransform)dropdown.transform).anchorMin.y, Is.GreaterThanOrEqualTo(.68f), "浏览内容应紧接标签排列");
        }

        [Test]
        public void Scene_PutsControlsOnRightAndPreviewOnLeft()
        {
            var canvas = view.GetComponentInChildren<Canvas>(true);
            var controls = (RectTransform)canvas.transform.Find("Controls");
            Assert.That(controls.anchorMin.x, Is.GreaterThan(.5f));
            Assert.That(controls.anchorMax.x, Is.EqualTo(1f));
            Assert.That(camera.rect.xMin, Is.Zero);
            Assert.That(camera.rect.xMax, Is.EqualTo(controls.anchorMin.x).Within(.001f));
            Assert.That(controls.GetComponent<SafeAreaFitter>(), Is.Not.Null, "选择和微调控件需避开手机挖孔安全区");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Tabs_CurrentPageIsHighlightedAndOtherPageCanBeOpened(bool tuning)
        {
            var panel = view.GetComponentInChildren<AppearanceGalleryPoseTuningPanel>(true);
            var fields = new UnityEditor.SerializedObject(panel);
            var browseButton = (UnityEngine.UI.Button)fields.FindProperty("browseButton").objectReferenceValue;
            var tuningButton = (UnityEngine.UI.Button)fields.FindProperty("tuningButton").objectReferenceValue;
            var browsePage = (GameObject)fields.FindProperty("browsePage").objectReferenceValue;
            var tuningPage = (GameObject)fields.FindProperty("tuningPage").objectReferenceValue;
            var switchPage = typeof(AppearanceGalleryPoseTuningPanel).GetMethod("ShowTuning", BindingFlags.Instance | BindingFlags.NonPublic);
            // 连续切换后检查实际页面与强调色同步，防止残留上一页的选中外观。
            switchPage.Invoke(panel, new object[] { !tuning });
            switchPage.Invoke(panel, new object[] { tuning });
            Assert.That(browsePage.activeSelf, Is.EqualTo(!tuning));
            Assert.That(tuningPage.activeSelf, Is.EqualTo(tuning));
            var current = tuning ? tuningButton : browseButton;
            var other = tuning ? browseButton : tuningButton;
            Assert.That(current.interactable, Is.False, "当前页无需重复打开");
            Assert.That(other.interactable, Is.True);
            Assert.That(current.colors.disabledColor.a, Is.EqualTo(1f), "选中标签不应呈半透明禁用外观");
            var currentColor = current.targetGraphic.color * current.colors.disabledColor;
            var otherColor = other.targetGraphic.color * other.colors.normalColor;
            Assert.That(currentColor.grayscale, Is.GreaterThan(otherColor.grayscale + .1f), "当前页必须比未选中页更亮");
            Assert.That(other.colors.highlightedColor.grayscale, Is.LessThan(current.colors.disabledColor.grayscale), "悬停未选中标签也不能冒充当前页");
        }

        [Test]
        public void PreviewTools_LeaveMiddleClearAndKeepSeparatedTouchTargets()
        {
            var root = view.GetComponentInChildren<Canvas>(true).transform.Find("PreviewControls");
            var topNames = new[] { "CharacterDropdown", "EquipmentDropdown", "CameraModeButton", "ResetCameraButton", "ExitButton" };
            var bottomNames = new[] { "AnimationDropdown", "PlayPauseButton", "Timeline" };
            var rects = topNames.Concat(bottomNames).Select(name => (RectTransform)root.Find(name)).ToArray();
            foreach (var rect in rects)
            {
                Assert.That((rect.anchorMax.y - rect.anchorMin.y) * 1080f + rect.sizeDelta.y,
                    Is.GreaterThanOrEqualTo(64f), rect.name + " 的参考触摸高度不足");
                if (topNames.Contains(rect.name)) Assert.That(rect.anchorMin.y, Is.GreaterThanOrEqualTo(.82f));
                else Assert.That(rect.anchorMax.y, Is.LessThanOrEqualTo(.18f));
            }
            for (var i = 0; i < rects.Length; i++)
            for (var j = i + 1; j < rects.Length; j++)
                Assert.That(Rect.MinMaxRect(rects[i].anchorMin.x, rects[i].anchorMin.y, rects[i].anchorMax.x, rects[i].anchorMax.y)
                    .Overlaps(Rect.MinMaxRect(rects[j].anchorMin.x, rects[j].anchorMin.y, rects[j].anchorMax.x, rects[j].anchorMax.y)),
                    Is.False, rects[i].name + " 与 " + rects[j].name + " 的触摸区重叠");
        }

        [Test]
        public void Timeline_ThinTrackKeepsFullTouchAreaAndVisibleProgress()
        {
            var timeline = view.GetComponentInChildren<Canvas>(true).transform.Find("PreviewControls/Timeline");
            var touchArea = timeline.GetComponent<UnityEngine.UI.Image>();
            Assert.That(touchArea, Is.Not.Null, "细轨道外也要能触摸拖动");
            Assert.That(touchArea.raycastTarget, Is.True);
            Assert.That(touchArea.color.a, Is.Zero);
            var background = timeline.Find("Background").GetComponent<UnityEngine.UI.Image>();
            Assert.That(background.rectTransform.anchorMin.y, Is.EqualTo(.5f));
            Assert.That(background.rectTransform.anchorMax.y, Is.EqualTo(.5f));
            Assert.That(background.rectTransform.sizeDelta.y, Is.InRange(6f, 12f));
            var slider = timeline.GetComponent<UnityEngine.UI.Slider>();
            var handle = slider.handleRect.GetComponent<UnityEngine.UI.Image>();
            var fill = slider.fillRect.GetComponent<UnityEngine.UI.Image>();
            var handleArea = (RectTransform)slider.handleRect.parent;
            Assert.That(handleArea.anchorMin.y, Is.EqualTo(.5f));
            Assert.That(handleArea.anchorMax.y, Is.EqualTo(.5f));
            Assert.That(handleArea.sizeDelta.y, Is.GreaterThan(background.rectTransform.sizeDelta.y * 2f));
            Assert.That(handle.rectTransform.sizeDelta.y, Is.Zero, "柄在固定高度的容器内随 Slider 刷新位置");
            Assert.That(fill.color.g, Is.GreaterThan(fill.color.r), "进度使用青色");
            Assert.That(handle.color.grayscale, Is.GreaterThan(background.color.grayscale + .3f));
            slider.SetValueWithoutNotify(.5f);
            Assert.That(slider.fillRect.anchorMax.x, Is.EqualTo(.5f).Within(.001f));
            Assert.That(slider.handleRect.anchorMin.x, Is.EqualTo(.5f).Within(.001f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Hints_FollowCurrentPageAndHoldHintStaysBesideAdjustments(bool tuning)
        {
            var panel = view.GetComponentInChildren<AppearanceGalleryPoseTuningPanel>(true);
            var fields = new UnityEditor.SerializedObject(panel);
            var values = (GameObject)fields.FindProperty("valuesPage").objectReferenceValue;
            var canvas = view.GetComponentInChildren<Canvas>(true);
            var hold = canvas.GetComponentsInChildren<UnityEngine.UI.Text>(true).Single(t => t.name == "HoldHint");
            var gesture = canvas.transform.Find("PreviewControls/DragHint");
            typeof(AppearanceGalleryPoseTuningPanel).GetMethod("ShowTuning", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(panel, new object[] { tuning });
            Assert.That(gesture.gameObject.activeSelf, Is.EqualTo(!tuning));
            Assert.That(hold.transform.IsChildOf(values.transform), Is.True, "长按提示应随微调数值页显示，而不是留在预览区");
            Assert.That(hold.gameObject.activeInHierarchy, Is.EqualTo(tuning));
            Assert.That(hold.raycastTarget, Is.False, "提示不能挡住加减按钮");
            var minus = (UnityEngine.UI.Button)fields.FindProperty("zMinus").objectReferenceValue;
            var step = (UnityEngine.UI.Button)fields.FindProperty("fineButton").objectReferenceValue;
            Assert.That(hold.rectTransform.anchorMax.y, Is.LessThanOrEqualTo(((RectTransform)minus.transform).anchorMin.y));
            Assert.That(hold.rectTransform.anchorMin.y, Is.GreaterThanOrEqualTo(((RectTransform)step.transform).anchorMax.y));
        }

        [TestCase("GetCharacterDisplayName", "GrayboxPlayer", "玩家")]
        [TestCase("GetEquipmentDisplayName", "ArcadePistol（仅试装）", "手枪（仅试装）")]
        [TestCase("GetAnimationDisplayName", "RifleFire", "步枪开火")]
        public void OptionNames_KeepChineseMeaningWithoutResourceIdentifiers(string method, string input, string expected)
        {
            var display = typeof(AppearanceGallerySceneView).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(display.Invoke(null, new object[] { input }), Is.EqualTo(expected));
        }

        [Test]
        public void SharedAnimationNames_HideDefinitionIdsAndKeepChineseQualifiers()
        {
            var display = typeof(AppearanceGallerySceneView).GetMethod("GetAnimationDefinitionDisplayName", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(display, Is.Not.Null);
            var guids = UnityEditor.AssetDatabase.FindAssets("t:CharacterAnimationDefinition", new[] { "Assets/Game/Art/Characters/Presentation/Definitions" });
            Assert.That(guids, Is.Not.Empty);
            foreach (var guid in guids)
            {
                var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterAnimationDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                var text = (string)display.Invoke(null, new object[] { definition });
                Assert.That(text, Does.Not.Contain(definition.name));
                Assert.That(System.Text.RegularExpressions.Regex.IsMatch(text, "[\\u4e00-\\u9fff]"), Is.True);
            }
            var candidate = ScriptableObject.CreateInstance<CharacterAnimationDefinition>();
            try
            {
                var fields = new UnityEditor.SerializedObject(candidate);
                fields.FindProperty("displayName").stringValue = "搭箭（仅预览）（StandingDrawArrow）";
                fields.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(display.Invoke(null, new object[] { candidate }), Is.EqualTo("搭箭（仅预览）"));
            }
            finally { Object.DestroyImmediate(candidate); }
        }

        [Test]
        public void SelectionLabels_AreVisibleAndDoNotOverlapTheirDropdowns()
        {
            var root = view.GetComponentInChildren<Canvas>(true).transform.Find("PreviewControls");
            foreach (var item in new[] { new[] { "CharacterDropdownTitle", "CharacterDropdown", "角色" },
                new[] { "EquipmentDropdownTitle", "EquipmentDropdown", "武器" }, new[] { "AnimationDropdownTitle", "AnimationDropdown", "动作" } })
            {
                var label = root.Find(item[0]).GetComponent<UnityEngine.UI.Text>();
                var dropdown = (RectTransform)root.Find(item[1]);
                Assert.That(label.gameObject.activeSelf, Is.True);
                Assert.That(label.text, Is.EqualTo(item[2]));
                Assert.That(label.raycastTarget, Is.False);
                Assert.That(label.rectTransform.anchorMax.x, Is.LessThanOrEqualTo(dropdown.anchorMin.x));
                Assert.That(label.rectTransform.anchorMin.y, Is.EqualTo(dropdown.anchorMin.y));
            }
        }

        [TestCase(100f, 0f, 2200f, 1080f)]
        [TestCase(0f, 0f, 2200f, 1080f)]
        [TestCase(100f, 30f, 2200f, 1020f)]
        public void SafeArea_KeepsControlAndCameraBoundaryAligned(float x, float y, float width, float height)
        {
            var method = typeof(AppearanceGallerySceneView).GetMethod("MapPreviewViewportToSafeArea", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var safeArea = new Rect(x, y, width, height);
            var viewport = (Rect)method.Invoke(null, new object[] { camera.rect, safeArea, new Vector2(2400f, 1080f) });
            var controls = (RectTransform)view.GetComponentInChildren<Canvas>(true).transform.Find("Controls");
            var fitter = controls.GetComponent<SafeAreaFitter>();
            Assert.That(fitter, Is.Not.Null);
            fitter.ApplyAnchors(new Vector2(x / 2400f, y / 1080f), new Vector2((x + width) / 2400f, (y + height) / 1080f));
            Assert.That(viewport.xMin, Is.EqualTo(x / 2400f).Within(.001f));
            Assert.That(viewport.yMin, Is.EqualTo(y / 1080f).Within(.001f));
            Assert.That(viewport.xMax, Is.EqualTo(controls.anchorMin.x).Within(.001f));
            Assert.That(viewport.yMax, Is.EqualTo(controls.anchorMax.y).Within(.001f));
            Assert.That(controls.anchorMax.x, Is.EqualTo((x + width) / 2400f).Within(.001f));
        }

        [TestCase(.2f, true)]
        [TestCase(.8f, false)]
        public void TouchOrbit_OnlyChangesYawInsideLeftPreview(float horizontalFraction, bool shouldOrbit)
        {
            var device = InputSystem.AddDevice<Touchscreen>();
            try
            {
                var start = new Vector2(Screen.width * horizontalFraction, Screen.height * .5f);
                var before = GetYaw();
                SendTouch(device, UnityEngine.InputSystem.TouchPhase.Began, start);
                InvokeInput();
                SendTouch(device, UnityEngine.InputSystem.TouchPhase.Moved, start + new Vector2(20f, 0f));
                InvokeInput();
                Assert.That(GetYaw() - before, Is.EqualTo(shouldOrbit ? 5f : 0f).Within(.001f));
            }
            finally { InputSystem.RemoveDevice(device); }
        }

        [TestCase(.4f, true)]
        [TestCase(.8f, false)]
        public void PinchZoom_RequiresBothFingersInsidePreview(float secondHorizontalFraction, bool shouldZoom)
        {
            var device = InputSystem.AddDevice<Touchscreen>();
            try
            {
                var first = new Vector2(Screen.width * .2f, Screen.height * .5f);
                var second = new Vector2(Screen.width * secondHorizontalFraction, Screen.height * .5f);
                var field = typeof(AppearanceGallerySceneView).GetField("cameraDistance", BindingFlags.Instance | BindingFlags.NonPublic);
                var before = (float)field.GetValue(view);
                SendTouch(device, UnityEngine.InputSystem.TouchPhase.Began, first);
                InputSystem.QueueStateEvent(device, new TouchState { touchId = 2, phase = UnityEngine.InputSystem.TouchPhase.Began, position = second });
                InputSystem.Update();
                InvokeInput();
                InputSystem.QueueStateEvent(device, new TouchState { touchId = 2, phase = UnityEngine.InputSystem.TouchPhase.Moved, position = second + new Vector2(20f, 0f) });
                InputSystem.Update();
                InvokeInput();
                Assert.That((float)field.GetValue(view) - before, Is.EqualTo(shouldZoom ? -.2f : 0f).Within(.001f));
            }
            finally { InputSystem.RemoveDevice(device); }
        }

        private float GetYaw() => (float)typeof(AppearanceGallerySceneView).GetField("yawDegrees", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);

        [Test]
        public void TouchStartingInControlsDoesNotOrbitAfterEnteringPreview()
        {
            var device = InputSystem.AddDevice<Touchscreen>();
            try
            {
                var before = GetYaw();
                SendTouch(device, UnityEngine.InputSystem.TouchPhase.Began, new Vector2(Screen.width * .8f, Screen.height * .5f));
                InvokeInput();
                SendTouch(device, UnityEngine.InputSystem.TouchPhase.Moved, new Vector2(Screen.width * .2f, Screen.height * .5f));
                InvokeInput();
                Assert.That(GetYaw(), Is.EqualTo(before), "手势必须从预览区开始");
            }
            finally { InputSystem.RemoveDevice(device); }
        }
        private void InvokeInput() => typeof(AppearanceGallerySceneView).GetMethod("HandleCameraInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
        private static void SendTouch(Touchscreen device, UnityEngine.InputSystem.TouchPhase phase, Vector2 position)
        {
            InputSystem.QueueStateEvent(device, new TouchState { touchId = 1, phase = phase, position = position });
            InputSystem.Update();
        }
    }
}
