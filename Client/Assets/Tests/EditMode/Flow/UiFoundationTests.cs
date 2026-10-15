using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class UiFoundationTests
    {
        [TearDown]
        public void TearDown()
        {
            UIHelper.ResetForTests();
            UIRoot.ClearForTests();
            EditModeSceneIsolation.DestroyNamedObjectsInActiveTestScene(
                "PoolRoot");
        }

        [Test]
        public void GameplayHud_LivesBelowDialogInFormalScene()
        {
            Assert.That(new ArcadeHudUICtrl().Layer(), Is.EqualTo(UILayer.HUD));
            var scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/Main.unity", OpenSceneMode.Additive);
            try
            {
                var root = scene.GetRootGameObjects().Single(go => go.name == "UIRoot").transform;
                var hud = root.Find("HUD");
                Assert.That(hud, Is.Not.Null);
                Assert.That(hud.GetSiblingIndex(), Is.LessThan(root.Find("Dialog").GetSiblingIndex()));
                Assert.That(root.Find("Dialog").GetSiblingIndex(), Is.LessThan(root.Find("Popup").GetSiblingIndex()));
                Assert.That(root.Find("Popup").GetSiblingIndex(), Is.LessThan(root.Find("Tips").GetSiblingIndex()));
                Assert.That(root.Find("Dialog").GetSiblingIndex(), Is.LessThan(root.Find("Tips").GetSiblingIndex()));
                Assert.That(root.Find("Tips").GetSiblingIndex(), Is.LessThan(root.Find("Top").GetSiblingIndex()));
                var rect = (RectTransform)hud;
                Assert.That(rect.anchorMin, Is.EqualTo(Vector2.zero));
                Assert.That(rect.anchorMax, Is.EqualTo(Vector2.one));
                Assert.That(rect.offsetMin, Is.EqualTo(Vector2.zero));
                Assert.That(rect.offsetMax, Is.EqualTo(Vector2.zero));
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void UILayer_ContainsHudDialogTipsAndTop()
        {
            var names = System.Enum.GetNames(typeof(UILayer));

            CollectionAssert.AreEquivalent(new[] { "HUD", "Dialog", "Popup", "Tips", "Top" }, names);
        }

        [Test]
        public void SceneRoot_FindsPoolRoot()
        {
            new GameObject("PoolRoot");

            Assert.That(SceneRoot.PoolRoot.name, Is.EqualTo("PoolRoot"));
        }

        [Test]
        public void MainScene_RemovesObsoleteRootsAndKeepsPoolRoot()
        {
            const string mainScenePath = "Assets/Game/Scenes/Main.unity";
            var activeScene = SceneManager.GetActiveScene();
            var mainScene = EditorSceneManager.OpenScene(mainScenePath, OpenSceneMode.Additive);
            try
            {
                var roots = mainScene.GetRootGameObjects();
                Assert.That(roots.Select(root => root.name), Does.Not.Contain("MapRoot"));
                Assert.That(roots.Select(root => root.name), Does.Not.Contain("UnitRoot"));
                Assert.That(roots.Select(root => root.name), Does.Not.Contain("HudRoot"));
                Assert.That(roots.Select(root => root.name), Does.Contain(SceneRoot.PoolRootName));

                SceneManager.SetActiveScene(mainScene);
                Assert.That(SceneRoot.PoolRoot, Is.Not.Null);
                Assert.That(SceneRoot.PoolRoot.name, Is.EqualTo(SceneRoot.PoolRootName));
            }
            finally
            {
                SceneManager.SetActiveScene(activeScene);
                EditorSceneManager.CloseScene(mainScene, true);
            }
        }

        [Test]
        public void UIRoot_FindsFourLayersAndInputSystemModule()
        {
            CreateUIRoot();

            var root = UIRoot.EnsureInstance();

            Assert.NotNull(root.GetLayerObj(UILayer.HUD));
            Assert.NotNull(root.GetLayerObj(UILayer.Dialog));
            Assert.NotNull(root.GetLayerObj(UILayer.Popup));
            Assert.NotNull(root.GetLayerObj(UILayer.Tips));
            Assert.NotNull(root.GetLayerObj(UILayer.Top));
            Assert.NotNull(FindInputSystemModule(root.gameObject));
        }

        [Test]
        public void BaseUICtrl_ShowInitializesOnceAndHideCallsOnHide()
        {
            CreateUIRoot();
            var prefab = new GameObject("TestPrefab");
            prefab.AddComponent<RectTransform>();
            prefab.AddComponent<BaseUI>();
            UIHelper.SetPrefabLoaderForTests(_ => prefab);

            var ctrl = UIHelper.ShowUI<TestCtrl>();
            UIHelper.ShowUI<TestCtrl>();
            UIHelper.HideUI<TestCtrl>();

            Assert.That(ctrl.InitCount, Is.EqualTo(1));
            Assert.That(ctrl.ShowCount, Is.EqualTo(1));
            Assert.That(ctrl.HideCount, Is.EqualTo(1));
            Assert.IsFalse(ctrl.IsShow());
            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void UIHelper_ReusesControllerByType()
        {
            CreateUIRoot();
            var prefab = new GameObject("TestPrefab");
            prefab.AddComponent<RectTransform>();
            prefab.AddComponent<BaseUI>();
            UIHelper.SetPrefabLoaderForTests(_ => prefab);

            var first = UIHelper.ShowUI<TestCtrl>();
            var second = UIHelper.ShowUI<TestCtrl>();

            Assert.AreSame(first, second);
            Assert.AreSame(first, UIHelper.GetUICtrl<TestCtrl>());
            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void BaseUICtrl_BackgroundIsOptIn()
        {
            var prefab = CreateTestPrefab();
            try
            {
                UIHelper.ShowUI<TestCtrl>();
                var layer = UIRoot.Instance.GetLayerObj(UILayer.Dialog);

                Assert.That(layer.childCount, Is.EqualTo(1));
                Assert.That(layer.GetChild(0).GetComponent<BaseUI>(), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void BackgroundConfig_ResolvesAllColorsAndClampsAlpha()
        {
            Assert.That(new BackgroundConfig { ColorType = BackgroundColorType.Transparent, Alpha = 1f }.GetBackgroundColor(),
                Is.EqualTo(Color.clear));
            Assert.That(new BackgroundConfig { ColorType = BackgroundColorType.Gray, Alpha = 2f }.GetBackgroundColor(),
                Is.EqualTo(new Color(0.5f, 0.5f, 0.5f, 1f)));
            Assert.That(new BackgroundConfig { ColorType = BackgroundColorType.DarkGray, Alpha = -1f }.GetBackgroundColor(),
                Is.EqualTo(new Color(0.3f, 0.3f, 0.3f, 0f)));
            Assert.That(new BackgroundConfig { ColorType = BackgroundColorType.Black, Alpha = 0.25f }.GetBackgroundColor(),
                Is.EqualTo(new Color(0f, 0f, 0f, 0.25f)));
            Assert.That(new BackgroundConfig
            {
                ColorType = BackgroundColorType.Custom,
                CustomColor = new Color(0.1f, 0.2f, 0.3f, 0.9f),
                Alpha = 0.4f
            }.GetBackgroundColor(), Is.EqualTo(new Color(0.1f, 0.2f, 0.3f, 0.4f)));
        }

        [Test]
        public void BaseUICtrl_ConfiguredBackgroundFillsLayerAndPairsWithOwner()
        {
            var prefab = CreateTestPrefab();
            try
            {
                var ctrl = UIHelper.ShowUI<BackgroundTestCtrl>();
                var layer = UIRoot.Instance.GetLayerObj(UILayer.Dialog);
                var background = layer.GetChild(0) as RectTransform;
                var owner = GetBaseUI(ctrl).transform;

                Assert.That(layer.childCount, Is.EqualTo(2));
                Assert.That(background.name, Does.Contain("Background"));
                Assert.That(layer.GetChild(1), Is.EqualTo(owner));
                Assert.That(background.anchorMin, Is.EqualTo(Vector2.zero));
                Assert.That(background.anchorMax, Is.EqualTo(Vector2.one));
                Assert.That(background.offsetMin, Is.EqualTo(Vector2.zero));
                Assert.That(background.offsetMax, Is.EqualTo(Vector2.zero));
                Assert.That(background.GetComponent<Image>().raycastTarget, Is.True);

                ctrl.MoveToTop();
                Assert.That(layer.GetChild(0), Is.EqualTo(background));
                Assert.That(layer.GetChild(1), Is.EqualTo(owner));

                UIHelper.HideUI<BackgroundTestCtrl>();
                Assert.That(background.gameObject.activeSelf, Is.False);
                Assert.That(owner.gameObject.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void BaseUICtrl_BackgroundClickEffectsMatchContract()
        {
            AssertBackgroundClick(BackgroundClickEffect.None, expectOwnerVisible: true, expectThroughCount: 0);
            AssertBackgroundClick(BackgroundClickEffect.CloseOnly, expectOwnerVisible: false, expectThroughCount: 0);
            AssertBackgroundClick(BackgroundClickEffect.ThroughOnly, expectOwnerVisible: true, expectThroughCount: 1);
            AssertBackgroundClick(BackgroundClickEffect.CloseThrough, expectOwnerVisible: false, expectThroughCount: 1);
        }

        [Test]
        public void BaseUICtrl_ClickThroughUsesOriginalPointerAndKeepsOwnerInteractive()
        {
            var prefab = CreateTestPrefab();
            try
            {
                BackgroundTestCtrl.Effect = BackgroundClickEffect.ThroughOnly;
                var target = new GameObject("ClickTarget", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ClickCounter));
                target.transform.SetParent(UIRoot.Instance.GetLayerObj(UILayer.Dialog), false);
                var ctrl = UIHelper.ShowUI<BackgroundTestCtrl>();
                var owner = GetBaseUI(ctrl);
                var ownerTarget = new GameObject("OwnerClickTarget", typeof(RectTransform), typeof(ClickCounter));
                ownerTarget.transform.SetParent(owner.transform, false);
                var background = GetBackground(owner).gameObject;
                var targetCounter = target.GetComponent<ClickCounter>();
                var ownerCounter = ownerTarget.GetComponent<ClickCounter>();
                var eventSystem = FindTestEventSystem();
                var eventData = new PointerEventData(eventSystem)
                {
                    position = new Vector2(100f, 100f),
                    button = PointerEventData.InputButton.Left
                };

                TestRaycaster.Targets = new[] { background, target, target };
                ExecuteEvents.Execute(background, eventData, ExecuteEvents.pointerClickHandler);

                Assert.That(targetCounter.ClickCount, Is.EqualTo(1));
                Assert.That(targetCounter.LastEventData, Is.SameAs(eventData));
                Assert.That(ctrl.IsShow(), Is.True);

                ExecuteEvents.Execute(ownerTarget, eventData, ExecuteEvents.pointerClickHandler);
                Assert.That(ownerCounter.ClickCount, Is.EqualTo(1));

                var noHandler = new GameObject("NoHandler", typeof(RectTransform));
                TestRaycaster.Targets = new[] { background, noHandler };
                Assert.DoesNotThrow(() => ExecuteEvents.Execute(background, eventData, ExecuteEvents.pointerClickHandler));
                Assert.That(targetCounter.ClickCount, Is.EqualTo(1));
                Object.DestroyImmediate(noHandler);
                Object.DestroyImmediate(target);
            }
            finally
            {
                TestRaycaster.Targets = System.Array.Empty<GameObject>();
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void BaseUICtrl_BackgroundLifecycleMovesAndCleansExactOwner()
        {
            var prefab = CreateTestPrefab();
            try
            {
                BackgroundTestCtrl.Effect = BackgroundClickEffect.None;
                var ctrl = UIHelper.ShowUI<BackgroundTestCtrl>();
                var layer = UIRoot.Instance.GetLayerObj(UILayer.Dialog);
                var oldUi = ctrl.CurrentUI;
                var oldBackground = GetBackground(oldUi);
                AssertBackgroundPaired(layer, oldBackground, oldUi.transform);

                UIHelper.HideUI<BackgroundTestCtrl>();
                Assert.That(oldBackground.gameObject.activeSelf, Is.False);
                Assert.That(oldUi.gameObject.activeSelf, Is.False);

                UIHelper.ShowUI<BackgroundTestCtrl>();
                Assert.That(oldBackground.gameObject.activeSelf, Is.True);
                Assert.That(oldUi.gameObject.activeSelf, Is.True);
                AssertBackgroundPaired(layer, oldBackground, oldUi.transform);

                ctrl.MoveToTop();
                AssertBackgroundPaired(layer, oldBackground, oldUi.transform);

                ctrl.ForceForgetLoadedStateForTest();
                UIHelper.ShowUI<BackgroundTestCtrl>();
                var newUi = ctrl.CurrentUI;
                var newBackground = GetBackground(newUi);
                Assert.That(newUi, Is.Not.SameAs(oldUi));
                AssertBackgroundPaired(layer, newBackground, newUi.transform);

                TriggerDestroyEventAndDestroy(oldUi);
                Assert.That(oldBackground == null, Is.True);
                Assert.That(ctrl.CurrentUI, Is.SameAs(newUi));
                Assert.That(ctrl.IsShow(), Is.True);
                AssertBackgroundPaired(layer, newBackground, newUi.transform);

                TriggerDestroyEventAndDestroy(newUi);
                Assert.That(newBackground == null, Is.True);
                Assert.That(ctrl.CurrentUI, Is.Null);
                Assert.That(ctrl.IsShow(), Is.False);
                Assert.That(ctrl.UnloadCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void BaseUICtrl_ApplicationQuitRootTeardownDoesNotDestroyBackgroundTwice()
        {
            LifeCycle.ClearForTests();
            var prefab = CreateTestPrefab();
            var root = UIRoot.Instance;
            BaseUI ui = null;
            Transform background = null;

            try
            {
                var ctrl = UIHelper.ShowUI<QuitBackgroundTestCtrl>();
                ui = ctrl.CurrentUI;
                background = GetBackground(ui);

                LifeCycle.Quit();
                ui.OnTriggerDestroyEvent.Trigger();
                Object.DestroyImmediate(root.gameObject);

                Assert.That(ui == null, Is.True);
                Assert.That(background == null, Is.True);
                Assert.That(ctrl.IsShow(), Is.False);
                Assert.That(ctrl.UnloadCount, Is.EqualTo(1));
                Assert.That(ctrl.HideCount, Is.EqualTo(0));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (root != null)
                {
                    Object.DestroyImmediate(root.gameObject);
                }

                Object.DestroyImmediate(prefab);
                LifeCycle.ClearForTests();
            }
        }

        [Test]
        public void BaseUICtrl_HideCallsOnlyOnHide()
        {
            var prefab = CreateTestPrefab();
            try
            {
                var ctrl = UIHelper.ShowUI<TestCtrl>();
                UIHelper.HideUI<TestCtrl>();

                Assert.That(ctrl.HideCount, Is.EqualTo(1));
                Assert.That(ctrl.UnloadCount, Is.EqualTo(0));
                Assert.That(ctrl.IsShow(), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void BaseUICtrl_ActiveDestroyResetsStateThenCallsUnloadOnce()
        {
            var prefab = CreateTestPrefab();
            try
            {
                var ctrl = UIHelper.ShowUI<TestCtrl>();
                TriggerDestroyEventAndDestroy(ctrl.CurrentUI);

                Assert.That(ctrl.HideCount, Is.EqualTo(0));
                Assert.That(ctrl.UnloadCount, Is.EqualTo(1));
                Assert.That(ctrl.UnloadSawIsShow, Is.False);
                Assert.That(ctrl.UnloadSawBaseUiNull, Is.True);
                Assert.That(ctrl.IsShow(), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void BaseUICtrl_HiddenDestroyCallsUnloadWithoutSecondHide()
        {
            var prefab = CreateTestPrefab();
            try
            {
                var ctrl = UIHelper.ShowUI<TestCtrl>();
                UIHelper.HideUI<TestCtrl>();
                var hideCount = ctrl.HideCount;
                TriggerDestroyEventAndDestroy(ctrl.CurrentUI);

                Assert.That(ctrl.HideCount, Is.EqualTo(hideCount));
                Assert.That(ctrl.UnloadCount, Is.EqualTo(1));
                Assert.That(ctrl.UnloadSawIsShow, Is.False);
                Assert.That(ctrl.UnloadSawBaseUiNull, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void BaseUICtrl_OldViewDestroyDoesNotUnloadReloadedView()
        {
            var prefab = CreateTestPrefab();
            try
            {
                var ctrl = UIHelper.ShowUI<TestCtrl>();
                var old = ctrl.CurrentUI;
                ctrl.ForceForgetLoadedStateForTest();
                UIHelper.ShowUI<TestCtrl>();
                var current = ctrl.CurrentUI;

                TriggerDestroyEventAndDestroy(old);

                Assert.That(ctrl.UnloadCount, Is.EqualTo(0));
                Assert.That(ctrl.CurrentUI, Is.SameAs(current));
                Assert.That(ctrl.IsShow(), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void BaseUICtrl_UnloadExceptionLeavesControllerResetAndUiHelperHidden()
        {
            var prefab = CreateTestPrefab();
            try
            {
                var ctrl = UIHelper.ShowUI<TestCtrl>();
                ctrl.ThrowOnUnload = true;
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[BaseUICtrl\] UI 实例卸载异常：System\.InvalidOperationException: unload failed"));
                TriggerDestroyEventAndDestroy(ctrl.CurrentUI);

                Assert.That(ctrl.UnloadCount, Is.EqualTo(1));
                Assert.That(ctrl.IsShow(), Is.False);
                Assert.That(UIHelper.HasUIOpen, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void TopWindow_ClosesPopupAndRejectsReopenUntilHidden()
        {
            var prefab = CreateTestPrefab();
            try
            {
                var popup = UIHelper.ShowUI<PopupTestCtrl>();
                Assert.That(popup.IsShow(), Is.True);
                var persistent = UIHelper.ShowUI<PersistentPopupTestCtrl>();
                var top = UIHelper.ShowUI<TopTestCtrl>();
                Assert.That(popup.IsShow(), Is.False);
                Assert.That(persistent.IsShow(), Is.True);
                popup.Show();
                Assert.That(popup.IsShow(), Is.False);
                top.Hide();
                popup.Show();
                Assert.That(popup.IsShow(), Is.True);
                Assert.That(UIHelper.GetTopmostBlockingUI(), Is.SameAs(popup));
                top.Show();
                Assert.That(UIHelper.GetTopmostBlockingUI(), Is.SameAs(top));
            }
            finally { Object.DestroyImmediate(prefab); }
        }

        private sealed class PopupTestCtrl : TestCtrl
        {
            public override UILayer Layer() => UILayer.Popup;
        }

        private sealed class TopTestCtrl : TestCtrl
        {
            public override UILayer Layer() => UILayer.Top;
        }

        private sealed class PersistentPopupTestCtrl : TestCtrl
        {
            public override UILayer Layer() => UILayer.Popup;
            public override bool IsTransientPopup => false;
            public override bool BlocksGame => false;
        }

        private static void CreateUIRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(TestRaycaster), typeof(UIRoot));
            ((RectTransform)root.transform).sizeDelta = new Vector2(1920f, 1080f);
            root.GetComponent<TestRaycaster>().RegisterForTests();

            foreach (var layerName in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var layer = new GameObject(layerName, typeof(RectTransform));
                layer.transform.SetParent(root.transform, false);
                var rect = (RectTransform)layer.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            var inputModuleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            Assert.NotNull(inputModuleType, "Unity Input System UI module type must be available.");
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), inputModuleType);
            eventSystem.transform.SetParent(root.transform, false);
            eventSystem.GetComponent<EventSystem>().UpdateModules();
        }

        private static GameObject CreateTestPrefab()
        {
            CreateUIRoot();
            var prefab = new GameObject("TestPrefab");
            prefab.AddComponent<RectTransform>();
            prefab.AddComponent<BaseUI>();
            UIHelper.SetPrefabLoaderForTests(_ => prefab);
            return prefab;
        }

        private static Component FindInputSystemModule(GameObject root)
        {
            return root.GetComponentsInChildren<Component>(true)
                .FirstOrDefault(component => component != null &&
                    component.GetType().FullName == "UnityEngine.InputSystem.UI.InputSystemUIInputModule");
        }

        private static EventSystem FindTestEventSystem()
        {
            return EditModeSceneIsolation.FindObjectsInActiveTestScene<EventSystem>().FirstOrDefault();
        }

        private static void TriggerDestroyEventAndDestroy(BaseUI ui)
        {
            Assert.NotNull(ui);
            ui.OnTriggerDestroyEvent.Trigger();
            Object.DestroyImmediate(ui.gameObject);
        }

        private static BaseUI GetBaseUI(BaseUICtrl ctrl)
        {
            return (BaseUI)typeof(BaseUICtrl)
                .GetField("BaseUI", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(ctrl);
        }

        private static Transform GetBackground(BaseUI owner)
        {
            Assert.NotNull(owner);
            var index = owner.transform.GetSiblingIndex();
            Assert.That(index, Is.GreaterThan(0));
            return owner.transform.parent.GetChild(index - 1);
        }

        private static void AssertBackgroundPaired(Transform layer, Transform background, Transform owner)
        {
            Assert.That(background.parent, Is.EqualTo(layer));
            Assert.That(owner.parent, Is.EqualTo(layer));
            Assert.That(background.GetSiblingIndex(), Is.EqualTo(owner.GetSiblingIndex() - 1));
            Assert.That(background.name, Does.Contain("Background"));
        }

        private static void AssertBackgroundClick(
            BackgroundClickEffect effect,
            bool expectOwnerVisible,
            int expectThroughCount)
        {
            var prefab = CreateTestPrefab();
            try
            {
                BackgroundTestCtrl.Effect = effect;
                var target = new GameObject("ClickTarget", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ClickCounter));
                target.transform.SetParent(UIRoot.Instance.GetLayerObj(UILayer.Dialog), false);
                var rect = (RectTransform)target.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                target.GetComponent<Image>().color = Color.white;

                var ctrl = UIHelper.ShowUI<BackgroundTestCtrl>();
                var background = UIRoot.Instance.GetLayerObj(UILayer.Dialog).GetChild(1).gameObject;
                TestRaycaster.Targets = new[] { background, target };
                Canvas.ForceUpdateCanvases();
                var eventSystem = FindTestEventSystem();
                Assert.NotNull(eventSystem);
                var eventData = new PointerEventData(eventSystem)
                {
                    position = new Vector2(100f, 100f),
                    button = PointerEventData.InputButton.Left
                };

                ExecuteEvents.Execute(background, eventData, ExecuteEvents.pointerClickHandler);

                Assert.That(ctrl.IsShow(), Is.EqualTo(expectOwnerVisible));
                Assert.That(target.GetComponent<ClickCounter>().ClickCount, Is.EqualTo(expectThroughCount));
                Object.DestroyImmediate(target);
            }
            finally
            {
                TestRaycaster.Targets = System.Array.Empty<GameObject>();
                Object.DestroyImmediate(prefab);
                UIHelper.ResetForTests();
            }
        }

        private class TestCtrl : BaseUICtrl
        {
            public int InitCount { get; private set; }
            public int ShowCount { get; private set; }
            public int HideCount { get; private set; }
            public int UnloadCount { get; private set; }
            public bool UnloadSawIsShow { get; private set; }
            public bool UnloadSawBaseUiNull { get; private set; }
            public bool ThrowOnUnload { get; set; }
            public BaseUI CurrentUI => BaseUI;

            protected override string Path()
            {
                return "TestPrefab";
            }

            protected override void OnInit()
            {
                InitCount++;
            }

            protected override void OnShow()
            {
                ShowCount++;
            }

            protected override void OnHide()
            {
                HideCount++;
            }

            protected override void OnUnload()
            {
                UnloadCount++;
                UnloadSawIsShow = IsShow();
                UnloadSawBaseUiNull = BaseUI == null;
                if (ThrowOnUnload)
                {
                    throw new System.InvalidOperationException("unload failed");
                }
            }

            public void ForceForgetLoadedStateForTest()
            {
                var baseType = typeof(BaseUICtrl);
                baseType.GetField("BaseUI", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(this, null);
                baseType.GetField("_initialized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(this, false);
                baseType.GetField("_isActive", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(this, false);
            }

            public override UILayer Layer()
            {
                return UILayer.Dialog;
            }
        }

        private sealed class BackgroundTestCtrl : TestCtrl
        {
            public static BackgroundClickEffect Effect { get; set; } = BackgroundClickEffect.None;

            protected override BackgroundConfig GetBackgroundConfig()
            {
                return new BackgroundConfig
                {
                    ColorType = BackgroundColorType.Gray,
                    Alpha = 0.5f,
                    ClickEffect = Effect
                };
            }
        }

        private sealed class QuitBackgroundTestCtrl : TestCtrl
        {
            protected override BackgroundConfig GetBackgroundConfig()
            {
                return new BackgroundConfig
                {
                    ColorType = BackgroundColorType.Gray,
                    Alpha = 0.5f,
                    ClickEffect = BackgroundClickEffect.None
                };
            }
        }

        private sealed class ClickCounter : MonoBehaviour, IPointerClickHandler
        {
            public int ClickCount { get; private set; }
            public PointerEventData LastEventData { get; private set; }

            public void OnPointerClick(PointerEventData eventData)
            {
                ClickCount++;
                LastEventData = eventData;
            }
        }

        private sealed class TestRaycaster : BaseRaycaster
        {
            public static IReadOnlyList<GameObject> Targets { get; set; } = System.Array.Empty<GameObject>();

            public override Camera eventCamera => null;

            public void RegisterForTests()
            {
                InvokeRaycasterManager("AddRaycaster");
            }

            protected override void OnDisable()
            {
                InvokeRaycasterManager("RemoveRaycasters");
                base.OnDisable();
            }

            public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
            {
                for (var i = 0; i < Targets.Count; i++)
                {
                    if (Targets[i] == null)
                    {
                        continue;
                    }

                    resultAppendList.Add(new RaycastResult
                    {
                        gameObject = Targets[i],
                        module = this,
                        depth = Targets.Count - i,
                        sortingOrder = Targets.Count - i
                    });
                }
            }

            private void InvokeRaycasterManager(string methodName)
            {
                var manager = typeof(BaseRaycaster).Assembly.GetType("UnityEngine.EventSystems.RaycasterManager");
                var method = manager?.GetMethod(
                    methodName,
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                method?.Invoke(null, new object[] { this });
            }
        }

    }
}
