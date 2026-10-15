using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class PauseUiFlowTests
    {
        private GameObject closablePrefab;
        private GameObject poolRoot;

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            LocalizationLanguages.ResetForTests();
            UIHelper.ResetForTests();
            ResourceLoader.SetLoaderForTests(new PauseLocalizationLoader());
            InstallPauseTestArchitecture();
            CreateUIRoot();
            closablePrefab = CreateClosablePrefab();
            UIHelper.SetPrefabLoaderForTests(path =>
            {
                if (path.Contains("TestClosable")) return closablePrefab;
                if (path == "Prefabs/UI/Home/Pause") return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Pause.prefab");
                if (path == "Prefabs/UI/Home/Setting") return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Setting.prefab");
                if (path == "Prefabs/UI/Notify/Float") return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Notify/Float.prefab");
                if (path == "Prefabs/UI/Notify/Message") return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Notify/Message.prefab");
                return path == "Prefabs/UI/Home/Start" ? CreateStartPrefab() : null;
            });
        }

        [TearDown]
        public void TearDown()
        {
            QuitGameCommand.SetQuitHandlerForTests(null);
            Object.DestroyImmediate(closablePrefab);
            UIHelper.ResetForTests();
            GameObjectPoolHelper.ClearForTests();
            if (poolRoot != null) Object.DestroyImmediate(poolRoot);
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            ResetArchitecture();
        }

        [Test]
        public void ArcadePause_ReopensAsOnlineInBaseAndRefreshesLanguage()
        {
            var map = Game.Interface.GetModel<MapSceneModel>();
            map.SetLoaded(MapIds.Arcade, null, MapPreparationMode.ArcadePlayerOnly);
            var ctrl = UIHelper.ShowUI<PauseUICtrl>();
            var text = GetBaseUI<PauseUI>(ctrl).BtnOnline.GetComponentInChildren<TMP_Text>();
            Assert.That(text.text, Is.EqualTo("离开街机"));
            Game.Interface.GetModel<LocalizationModel>().CurrentLanguageCode.Value = "en-US";
            Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN", "en-US"));
            Assert.That(text.text, Is.EqualTo("Leave Arcade"));
            ctrl.Hide();
            map.SetLoaded(MapIds.Arcade, null);
            ctrl.Show();
            Assert.That(text.text, Is.EqualTo("Online Game"));
        }

        [Test]
        public void ArcadeLeave_WithoutBaseSnapshotKeepsPauseAndDoesNotOpenLogin()
        {
            poolRoot = new GameObject(SceneRoot.PoolRootName);
            Game.Interface.RegisterSystem(new ArcadeSceneFlowSystem());
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Arcade, null, MapPreparationMode.ArcadePlayerOnly);
            var ctrl = UIHelper.ShowUI<PauseUICtrl>();
            GetBaseUI<PauseUI>(ctrl).BtnOnline.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.EqualTo(MapIds.Arcade));
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(UIHelper.GetUICtrl<OnlineLoginUICtrl>()?.IsShow(), Is.Not.True);
            Assert.That(GetBaseUI<PauseUI>(ctrl).BtnOnline.interactable, Is.True);
        }

        [Test]
        public void EscapeFallsBackToPausedStateWhenNoRecoverableWorldExists()
        {
            EnterPlaying();
            var hotKeys = Game.Interface.GetSystem<HotKeySystem>();

            Assert.IsTrue(hotKeys.ExecuteBinding(HotKeyDefaults.Escape));
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
            Assert.IsTrue(hotKeys.ExecuteBinding(HotKeyDefaults.Escape));
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
        }

        [Test]
        public void TogglePauseWithoutRecoverableWorldKeepsPausedState()
        {
            var hotKeys = Game.Interface.GetSystem<HotKeySystem>();

            Assert.IsTrue(hotKeys.ExecuteBinding(HotKeyDefaults.Escape));
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
        }

        [Test]
        public void DirectPauseAction_UsesSamePauseMenuRouteAsKeyboard()
        {
            EnterPlaying();
            var hotKeys = Game.Interface.GetSystem<HotKeySystem>();

            Assert.That(hotKeys.TryExecuteAction(HotKeyActionIds.TogglePause), Is.True);
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
            Assert.That(Object.FindObjectsOfType<BaseUI>().Any(ui => ui.GetType().Name == "PauseUI" && ui.gameObject.activeInHierarchy), Is.True);
            Assert.That(UIHelper.GetUICtrl<StartUICtrl>()?.IsShow(), Is.Not.True);
        }

        [Test]
        public void PauseLayout_ShrinksForArcadeAndRestoresForBaseWithoutButtonGaps()
        {
            var map = Game.Interface.GetModel<MapSceneModel>();
            map.SetLoaded(MapIds.Arcade, null);
            var ctrl = UIHelper.ShowUI<PauseUICtrl>();
            var view = GetBaseUI<PauseUI>(ctrl);
            var panel = (RectTransform)view.transform.Find("ContentRoot/Panel");
            var group = (RectTransform)panel.Find("ButtonGroup");
            var baseHeight = panel.rect.height;
            var baseGroupHeight = group.rect.height;
            var baseBottomMargin = ButtonBottomMargin(panel, view.BtnExit);
            Assert.That(view.BtnMainMenu.gameObject.activeSelf, Is.True);
            Assert.That(baseBottomMargin, Is.EqualTo(36f).Within(0.1f), "最后一个按钮应保持固定底部留白");
            Assert.That(baseHeight, Is.GreaterThan(baseGroupHeight));

            ctrl.Hide();
            map.SetLoaded(MapIds.Arcade, null, MapPreparationMode.ArcadePlayerOnly);
            ctrl.Show();
            Assert.That(view.BtnMainMenu.gameObject.activeSelf, Is.False);
            Assert.That(baseHeight - panel.rect.height, Is.EqualTo(84f).Within(0.1f), "隐藏按钮应同时移除 68 高度和 16 间距");
            Assert.That(baseGroupHeight - group.rect.height, Is.EqualTo(84f).Within(0.1f));
            Assert.That(ButtonBottomMargin(panel, view.BtnExit), Is.EqualTo(baseBottomMargin).Within(0.1f));
            Assert.That(panel.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(panel.rect.width, Is.EqualTo(560f).Within(0.1f));

            // 进一步隐藏任意按钮，验证由内容驱动，而不是按地图硬编码四/五按钮尺寸。
            view.BtnSetting.gameObject.SetActive(false);
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            Assert.That(baseHeight - panel.rect.height, Is.EqualTo(168f).Within(0.1f));
            Assert.That(ButtonBottomMargin(panel, view.BtnExit), Is.EqualTo(baseBottomMargin).Within(0.1f));
            view.BtnSetting.gameObject.SetActive(true);

            ctrl.Hide();
            map.SetLoaded(MapIds.Arcade, null);
            ctrl.Show();
            Assert.That(panel.rect.height, Is.EqualTo(baseHeight).Within(0.1f));
            Assert.That(group.rect.height, Is.EqualTo(baseGroupHeight).Within(0.1f));
        }

        private static float ButtonBottomMargin(RectTransform panel, Button button)
        {
            var corners = new Vector3[4];
            ((RectTransform)button.transform).GetWorldCorners(corners);
            return panel.InverseTransformPoint(corners[0]).y - panel.rect.yMin;
        }

        [Test]
        public void PausePrefab_HasAdaptivePanelReturnButtonAndNoStartArtwork()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Pause.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<ReferenceCollector>(), Is.Not.Null);
            var panel = prefab.transform.Find("ContentRoot/Panel") as RectTransform;
            Assert.That(panel, Is.Not.Null);
            Assert.That(panel.anchorMin, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(panel.anchorMax, Is.EqualTo(panel.anchorMin));
            Assert.That(panel.sizeDelta.x, Is.LessThanOrEqualTo(600f));
            Assert.That(panel.GetComponent<ContentSizeFitter>().verticalFit, Is.EqualTo(ContentSizeFitter.FitMode.PreferredSize));
            Assert.That(panel.GetComponent<VerticalLayoutGroup>(), Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<Button>(true).Select(b => b.name),
                Is.EquivalentTo(new[] { "BtnContinue", "BtnOnline", "BtnSetting", "BtnMainMenu", "BtnReturnCamp", "BtnExit" }));
            Assert.That(prefab.GetComponentsInChildren<RawImage>(true), Is.Empty);
            var view = prefab.GetComponent<BaseUI>();
            Assert.That(view, Is.Not.Null);
            var returnCamp = prefab.GetComponent<PauseUI>().BtnReturnCamp;
            var returnText = returnCamp.GetComponentInChildren<TMP_Text>(true);
            Assert.That(returnText.text, Is.EqualTo("返回营地"));
            Assert.That(returnText.GetComponent<LocalizedText>().Key, Is.EqualTo("ui.pause.return_camp"));
            foreach (var item in prefab.GetComponent<ReferenceCollector>().data)
            {
                Assert.That(item.gameObject, Is.Not.Null, item.key);
                var field = view.GetType().GetField(item.key);
                Assert.That(field, Is.Not.Null, item.key);
                Assert.That(field.GetValue(view), Is.SameAs(item.gameObject), item.key);
            }
        }

        [Test]
        public void PauseSettings_CloseBackToPauseAndMapLoadingClearsMenu()
        {
            EnterPlaying();
            var hotKeys = Game.Interface.GetSystem<HotKeySystem>();
            hotKeys.TryExecuteAction(HotKeyActionIds.TogglePause);
            var ctrl = UIHelper.GetUICtrl<PauseUICtrl>();
            var view = GetBaseUI<PauseUI>(ctrl);
            view.BtnSetting.onClick.Invoke();
            Assert.That(UIHelper.GetUICtrl<SettingUICtrl>().IsShow(), Is.True);
            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(hotKeys.ExecuteBinding(HotKeyDefaults.Escape), Is.True);
            Assert.That(UIHelper.GetUICtrl<SettingUICtrl>().IsShow(), Is.False);
            Assert.That(UIHelper.GetTopmostBlockingUI(), Is.SameAs(ctrl));
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
            // 即使通过非交互调用触发禁用按钮，缺少完整世界也不能恢复。
            view.BtnContinue.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
            Game.Interface.SendEvent(new MapLoadingStartedEvent(MapIds.Arcade));
            Assert.That(ctrl.IsShow(), Is.False);
        }

        [Test]
        public void PauseExit_RepeatedShowInvokesQuitOnlyOnce()
        {
            var calls = 0;
            QuitGameCommand.SetQuitHandlerForTests(() => calls++);
            var ctrl = UIHelper.ShowUI<PauseUICtrl>();
            ctrl.Hide();
            ctrl.Show();
            GetBaseUI<PauseUI>(ctrl).BtnExit.onClick.Invoke();
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void TopmostBlockingUiPrefersLayerThenShowOrder()
        {
            var dialog = RegisterShown(new TestDialogUICtrl(), CreateBaseUI("Dialog"));
            var top = RegisterShown(new TestTopUICtrl(), CreateBaseUI("Top"));
            var tips = RegisterShown(new TestTipsUICtrl(), CreateBaseUI("Tips"));

            Assert.That(UIHelper.GetTopmostBlockingUI(), Is.SameAs(top));

            top.Hide();
            Assert.That(UIHelper.GetTopmostBlockingUI(), Is.SameAs(tips));

            var laterDialog = RegisterShown(new SecondTestDialogUICtrl(), CreateBaseUI("Dialog2"));
            Assert.That(UIHelper.GetTopmostBlockingUI(), Is.SameAs(tips));

            tips.Hide();
            Assert.That(UIHelper.GetTopmostBlockingUI(), Is.SameAs(laterDialog));

            laterDialog.Hide();
            Assert.That(UIHelper.GetTopmostBlockingUI(), Is.SameAs(dialog));

            RegisterShown(new NonBlockingTopUICtrl(), CreateBaseUI("Hud"));
            Assert.That(UIHelper.GetTopmostBlockingUI(), Is.SameAs(dialog));
        }

        [Test]
        public void EscapeClosesEachWhitelistedControllerWithoutChangingAppState()
        {
            var hotKeys = Game.Interface.GetSystem<HotKeySystem>();
            var stateBefore = Game.Interface.GetModel<AppStateModel>().CurrentState.Value;
            var cases = new Func<BaseUICtrl>[]
            {
                () => RegisterShown(new RuntimeLogUICtrl(), CreateBaseUI<RuntimeLogUI>("RuntimeLog")),
                () => RegisterShown(new TestCmdUICtrl(), CreateBaseUI<TestCmdUI>("TestCmd")),
            };

            foreach (var create in cases)
            {
                var ctrl = create();
                Assert.IsTrue(ctrl.IsShow());
                Assert.IsTrue(hotKeys.ExecuteBinding(HotKeyDefaults.Escape));
                Assert.IsFalse(ctrl.IsShow());
                Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(stateBefore));
            }
        }

        [Test]
        public void EscapeClosesOnlyTopmostWhitelistedController()
        {
            var save = RegisterShown(new TestCmdUICtrl(), CreateBaseUI<TestCmdUI>("TestCmd"));
            var log = RegisterShown(new RuntimeLogUICtrl(), CreateBaseUI<RuntimeLogUI>("RuntimeLog"));
            var hotKeys = Game.Interface.GetSystem<HotKeySystem>();

            Assert.IsTrue(hotKeys.ExecuteBinding(HotKeyDefaults.Escape));
            Assert.IsTrue(save.IsShow());
            Assert.IsFalse(log.IsShow());

            Assert.IsTrue(hotKeys.ExecuteBinding(HotKeyDefaults.Escape));
            Assert.IsFalse(save.IsShow());
        }

        [Test]
        public void NonWhitelistedBlockingUiShieldsUnderlyingWindowAndPause()
        {
            EnterPlaying();
            var save = RegisterShown(new TestCmdUICtrl(), CreateBaseUI<TestCmdUI>("TestCmd"));
            var sameLayerShield = RegisterShown(new ShieldDialogUICtrl(), CreateBaseUI("ShieldDialog"));
            var hotKeys = Game.Interface.GetSystem<HotKeySystem>();

            Assert.IsFalse(hotKeys.ExecuteBinding(HotKeyDefaults.Escape));
            Assert.IsTrue(save.IsShow());
            Assert.IsTrue(sameLayerShield.IsShow());
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Playing));

            sameLayerShield.Hide();
            var topLayerShield = RegisterShown(new ShieldTopUICtrl(), CreateBaseUI("ShieldTop"));
            Assert.IsFalse(hotKeys.ExecuteBinding(HotKeyDefaults.Escape));
            Assert.IsTrue(save.IsShow());
            Assert.IsTrue(topLayerShield.IsShow());
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Playing));
        }

        [Test]
        public void NonBlockingUiDoesNotShieldWhitelistedWindow()
        {
            var save = RegisterShown(new TestCmdUICtrl(), CreateBaseUI<TestCmdUI>("TestCmd"));
            var hud = RegisterShown(new NonBlockingTopUICtrl(), CreateBaseUI("Hud"));
            var hotKeys = Game.Interface.GetSystem<HotKeySystem>();

            Assert.IsTrue(hotKeys.ExecuteBinding(HotKeyDefaults.Escape));
            Assert.IsFalse(save.IsShow());
            Assert.IsTrue(hud.IsShow());
        }

        [Test]
        public void EscapeDuringRebindOnlyCancelsCapture()
        {
            EnterPlaying();
            var hotKeys = Game.Interface.GetSystem<HotKeySystem>();

            Assert.IsTrue(hotKeys.BeginRebind("inventory.toggle").Success);
            var result = hotKeys.ReceiveBindingPath(HotKeyDefaults.Escape);

            Assert.IsFalse(result.Success);
            Assert.IsFalse(hotKeys.IsCapturingRebind);
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Playing));
            Assert.IsNull(UIHelper.GetUICtrl<StartUICtrl>());
        }

        [Test]
        public void DedicatedWindowHotKeysRemainRegistered()
        {
            var bindings = Game.Interface.GetModel<HotKeyModel>().Bindings;

            Assert.That(bindings.Count(item => item.BindingPath == HotKeyDefaults.Escape), Is.EqualTo(1));
            Assert.That(bindings.Single(item => item.BindingPath == HotKeyDefaults.Escape).ActionId,
                Is.EqualTo(HotKeyActionIds.TogglePause));
            Assert.That(bindings.Single(item => item.BindingPath == "<Keyboard>/tab").ActionId,
                Is.EqualTo("inventory.toggle"));
            Assert.That(bindings.Single(item => item.BindingPath == HotKeyDefaults.F1).ActionId,
                Is.EqualTo(HotKeyActionIds.ToggleRuntimeLog));
            Assert.That(bindings.Any(item => item.BindingPath == "<Keyboard>/c"), Is.False);
            Assert.That(bindings.Any(item => item.ActionId == "test-command.toggle"), Is.False);
        }

        [Test]
        public void StartExitUsesQuitCommand()
        {
            var source = System.IO.File.ReadAllText(System.IO.Path.Combine(
                UnityEngine.Application.dataPath, "Game/Scripts/Controller/UICtrl/Home/StartUICtrl.cs"));
            Assert.That(source, Does.Contain("this.SendCommand<QuitGameCommand>()"));
        }

        private static void EnterPlaying()
        {
            var appState = Game.Interface.GetSystem<AppStateSystem>();
            Assert.IsTrue(appState.TryTransition(AppState.Loading));
            Assert.IsTrue(appState.TryTransition(AppState.Playing));
        }

        private static GameObject CreateClosablePrefab()
        {
            return new GameObject("TestClosable", typeof(RectTransform), typeof(TestClosableUI));
        }

        private static GameObject CreateStartPrefab()
        {
            var root = new GameObject("Start", typeof(RectTransform), typeof(StartUI));
            var ui = root.GetComponent<StartUI>();
            ui.BtnNewGame = AddButton(root.transform, "BtnNewGame");
            ui.BtnContinue = AddButton(root.transform, "BtnContinue");
            ui.BtnOnline = AddButton(root.transform, "BtnOnline");
            ui.BtnCheckUpdate = AddButton(root.transform, "BtnCheckUpdate");
            ui.BtnSetting = AddButton(root.transform, "BtnSetting");
            ui.BtnExit = AddButton(root.transform, "BtnExit");
            ui.VersionText = AddText(root.transform, "Version").GetComponent<TextMeshProUGUI>();
            ui.UpdateStatusText = AddText(root.transform, "UpdateStatus").GetComponent<TextMeshProUGUI>();
            return root;
        }

        private static T RegisterShown<T>(T ctrl, BaseUI ui) where T : BaseUICtrl
        {
            var dict = (Dictionary<string, BaseUICtrl>)typeof(UIHelper)
                .GetField("UICtrlDict", BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null);
            Assert.NotNull(dict);
            dict[ctrl.GetType().Name] = ctrl;

            typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(ctrl, ui);
            typeof(BaseUICtrl).GetField("_initialized", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(ctrl, true);
            typeof(BaseUICtrl).GetField("_isActive", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(ctrl, true);

            ui.gameObject.SetActive(true);
            typeof(UIHelper).GetMethod("NotifyShown", BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, new object[] { ctrl });
            return ctrl;
        }

        private static BaseUI CreateBaseUI(string name)
        {
            return CreateBaseUI<BaseUI>(name);
        }

        private static T CreateBaseUI<T>(string name) where T : BaseUI
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(T));
            return go.GetComponent<T>();
        }

        /// <summary>关闭路由使用正式背包 View 契约，避免手工空 View 遗漏数量弹窗等必需引用。</summary>
        private static BagUI CreateBagUI()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Bag.prefab");
            Assert.That(prefab, Is.Not.Null);
            return Object.Instantiate(prefab).GetComponent<BagUI>();
        }

        private static CraftUI CreateCraftUI()
        {
            var ui = CreateBaseUI<CraftUI>("Craft");
            ui.BtnClose = AddButton(ui.transform, "Close");
            ui.RecipeList = new GameObject("RecipeList", typeof(RectTransform), typeof(CustomListView))
                .GetComponent<CustomListView>();
            ui.RecipeList.transform.SetParent(ui.transform, false);
            return ui;
        }

        private static ShopUI CreateShopUI()
        {
            var ui = CreateBaseUI<ShopUI>("Shop");
            ui.ItemList = new GameObject("ItemList", typeof(RectTransform), typeof(CustomListView))
                .GetComponent<CustomListView>();
            ui.ItemList.transform.SetParent(ui.transform, false);
            return ui;
        }

        private static EquipmentSlotBoxUI AddEquipmentSlot(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(EquipmentSlotBoxUI));
            go.transform.SetParent(parent, false);
            return go.GetComponent<EquipmentSlotBoxUI>();
        }

        private static Button AddButton(Transform parent, string name)
        {
            return AddButton(parent, name, null);
        }

        private static Button AddButton(Transform parent, string name, string localizationKey)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Button));
            go.transform.SetParent(parent, false);
            var text = AddText(go.transform, "Text");
            if (!string.IsNullOrWhiteSpace(localizationKey))
            {
                text.gameObject.AddComponent<LocalizedText>().Key = localizationKey;
            }

            return go.GetComponent<Button>();
        }

        private static TMP_Text AddText(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            return go.GetComponent<TMP_Text>();
        }

        private static TMP_Text AddLocalizedText(Transform parent, string name, string localizationKey)
        {
            var text = AddText(parent, name);
            text.gameObject.AddComponent<LocalizedText>().Key = localizationKey;
            return text;
        }

        private static string GetButtonText(Button button)
        {
            return button.GetComponentInChildren<TMP_Text>(true).text;
        }

        private static string GetText(Transform root, string path)
        {
            return root.Find(path).GetComponent<TMP_Text>().text;
        }

        private static void CreateUIRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(UIRoot));
            foreach (var layerName in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var layer = new GameObject(layerName, typeof(RectTransform));
                layer.transform.SetParent(root.transform, false);
            }

            var inputModuleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            Assert.NotNull(inputModuleType);
            new GameObject("EventSystem", typeof(EventSystem), inputModuleType).transform.SetParent(root.transform, false);
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private static void InstallPauseTestArchitecture()
        {
            var architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(architecture, true);

            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterModel(new DisplaySettingsModel());
            architecture.RegisterModel(new AudioSettingsModel());
            architecture.RegisterModel(new HotKeyModel());
            architecture.RegisterModel(new AppStateModel());
            architecture.RegisterModel(new RuntimeLogModel());
            architecture.RegisterModel(new MapSceneModel());
            architecture.RegisterModel(new ResourceUpdateModel());

            architecture.RegisterUtility(new LocalizationUtility(loadConfiguredTables: false));
            architecture.RegisterUtility(new HotKeyActionRegistry());

            architecture.RegisterSystem(new SettingsSystem());
            architecture.RegisterSystem(new AppStateSystem());
            architecture.RegisterSystem(new MobileInputSystem());
            architecture.RegisterSystem(new HotKeySystem());
            architecture.RegisterSystem(new MapSceneSystem(null, null, null, null));
            architecture.RegisterSystem(new ResourceUpdateSystem());
        }

        private static T GetBaseUI<T>(BaseUICtrl ctrl) where T : BaseUI
        {
            return (T)typeof(BaseUICtrl)
                .GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(ctrl);
        }

        private sealed class TestClosableUI : BaseUI
        {
        }

        private sealed class TestClosableUICtrl : BaseUICtrl
        {
            protected override string Path()
            {
                return "TestClosable";
            }

            protected override void OnInit()
            {
            }

            protected override void OnShow()
            {
            }

            protected override void OnHide()
            {
            }

            public override UILayer Layer()
            {
                return UILayer.Dialog;
            }
        }

        private abstract class TestRouteUICtrl : BaseUICtrl
        {
            protected override string Path()
            {
                return "TestRoute";
            }

            protected override void OnInit()
            {
            }

            protected override void OnShow()
            {
            }

            protected override void OnHide()
            {
            }
        }

        private sealed class TestDialogUICtrl : TestRouteUICtrl
        {
            public override UILayer Layer()
            {
                return UILayer.Dialog;
            }
        }

        private sealed class SecondTestDialogUICtrl : TestRouteUICtrl
        {
            public override UILayer Layer()
            {
                return UILayer.Dialog;
            }
        }

        private sealed class TestTipsUICtrl : TestRouteUICtrl
        {
            public override UILayer Layer()
            {
                return UILayer.Tips;
            }
        }

        private sealed class TestTopUICtrl : TestRouteUICtrl
        {
            public override UILayer Layer()
            {
                return UILayer.Top;
            }
        }

        private sealed class ShieldDialogUICtrl : TestRouteUICtrl
        {
            public override UILayer Layer()
            {
                return UILayer.Dialog;
            }
        }

        private sealed class ShieldTopUICtrl : TestRouteUICtrl
        {
            public override UILayer Layer()
            {
                return UILayer.Top;
            }
        }

        private sealed class NonBlockingTopUICtrl : TestRouteUICtrl
        {
            public override bool BlocksGame => false;

            public override UILayer Layer()
            {
                return UILayer.Top;
            }
        }

        private sealed class PauseLocalizationLoader : IResourceLoader
        {
            private readonly Dictionary<string, TextAsset> assets = new Dictionary<string, TextAsset>
            {
                [LocalizationLanguages.LanguagesAddress] = new TextAsset(
                    "{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"简体中文\"},{\"code\":\"en-US\",\"label\":\"English\"}]}"),
                ["zh-CN.json"] = new TextAsset(
                    "{\"ui.pause.abandon_confirm\":\"放弃将丢失背包和装备\",\"ui.pause.return_camp\":\"返回营地\",\"ui.pause.title\":\"暂停\",\"ui.pause.leave_arcade\":\"离开街机\",\"ui.start.online\":\"联机游戏\",\"ui.pause.resume\":\"继续\",\"ui.pause.main_menu\":\"主界面\",\"ui.start.settings\":\"设置\",\"ui.start.exit\":\"退出\"}"),
                ["en-US.json"] = new TextAsset(
                    "{\"ui.pause.title\":\"Paused\",\"ui.pause.leave_arcade\":\"Leave Arcade\",\"ui.start.online\":\"Online Game\",\"ui.pause.resume\":\"Resume\",\"ui.pause.main_menu\":\"Main Menu\",\"ui.start.settings\":\"Settings\",\"ui.start.exit\":\"Exit\"}")
            };

            public T LoadAsset<T>(string path) where T : Object
            {
                return assets.TryGetValue(path, out var asset) ? asset as T : null;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, System.Action<T> onLoaded) where T : Object
            {
                var asset = LoadAsset<T>(path);
                onLoaded?.Invoke(asset);
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }
    }
}
