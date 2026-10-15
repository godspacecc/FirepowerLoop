using System.IO;
using System.Reflection;
using Boot;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class GameplayGuideHudTests
    {
        private GameObject uiRoot;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            var architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(architecture, true);
            architecture.RegisterModel(new SaveModel());
            architecture.RegisterModel(new MapSceneModel());
            architecture.RegisterModel(new ExpeditionModel());
            architecture.RegisterModel(new AppStateModel());
            architecture.RegisterModel(new PlayerCameraModeModel());
            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterModel(new DisplaySettingsModel());
            architecture.RegisterModel(new AudioSettingsModel());
            architecture.RegisterModel(new HotKeyModel());
            architecture.RegisterModel(new ResourceUpdateModel());
            architecture.RegisterModel(new TestToolSettingsModel());
            architecture.RegisterModel(new MobileControlLayoutModel());
            ResourceLoader.SetLoaderForTests(new HudLocalizationResourceLoader());
            architecture.RegisterUtility(new LocalizationUtility());
            architecture.RegisterUtility<ISettingsFileStore>(new HudSettingsFileStore());
            architecture.RegisterUtility<IDisplayResolutionApplier>(new HudDisplayResolutionApplier());
            architecture.RegisterUtility(new HotKeyActionRegistry());
            architecture.RegisterSystem(new HotKeySystem());
            architecture.RegisterSystem(new SettingsSystem());
            architecture.RegisterSystem(new AppStateSystem());
            architecture.RegisterSystem(new ExpeditionFlowSystem());
            architecture.RegisterSystem(new MobileInputSystem());
            architecture.RegisterSystem(new PlayerInteractionSystem());
            architecture.RegisterSystem(new ResourceUpdateSystem());
            architecture.GetSystem<ResourceUpdateSystem>().InitializeStartupInfo(
                new BootResourceStartupInfo(BootResourceSource.BuiltinFallback, string.Empty, "guide-test-v1", string.Empty));
            uiRoot = CreateUiRoot();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
        }

        [TearDown]
        public void TearDown()
        {
            UIHelper.ResetForTests();
            UIRoot.ClearForTests();
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            if (uiRoot != null) Object.DestroyImmediate(uiRoot);
            TestReset.ResetArchitecture();
        }
        [Test]
        public void DesktopPause_UsesHotKeyWithoutHudButton()
        {
            Game.Interface.RegisterSystem(new MapSceneSystem(null, null, null, null, null, null));
            EnterBasePlaying();
            UIHelper.ShowUI<GameplayGuideHudUICtrl>();
            var view = Object.FindObjectOfType<GameplayGuideHudUI>();
            Assert.That(view.BtnPause.gameObject.activeInHierarchy, Is.False);
            Assert.That(view.MobileBtnPause.gameObject.activeInHierarchy, Is.False);
            Assert.That(Game.Interface.GetSystem<HotKeySystem>().TryExecuteAction(HotKeyActionIds.TogglePause), Is.True);
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
            Assert.That(UIHelper.GetUICtrl<PauseUICtrl>().IsShow(), Is.True);
            Assert.That(view.BtnPause.gameObject.activeInHierarchy, Is.False);
        }

        [Test]
        public void PrefabAndController_ExposeThePersistentNonBlockingGuideContract()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/GameplayGuideHud.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponents<ReferenceCollector>(), Has.Length.EqualTo(1));
            var ui = prefab.GetComponent<GameplayGuideHudUI>();
            Assert.That(ui, Is.Not.Null);
            Assert.That(ui.GuideRoot, Is.Not.Null);
            Assert.That(ui.GuideText, Is.Not.Null);
            Assert.That(ui.VersionText, Is.Not.Null);
            Assert.That(ui.GuideText.raycastTarget, Is.False);
            Assert.That(ui.VersionText.raycastTarget, Is.False);
            Assert.That(((RectTransform)ui.GuideRoot.transform).anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(((RectTransform)ui.GuideRoot.transform).anchoredPosition, Is.EqualTo(new Vector2(-32f, -32f)));
            Assert.That(ui.VersionText.rectTransform.anchorMin, Is.EqualTo(new Vector2(1f, 0f)));

            var ctrl = new GameplayGuideHudUICtrl();
            Assert.That(ctrl.Layer(), Is.EqualTo(UILayer.HUD));
            Assert.That(ctrl.BlocksGame, Is.False);
            var path = typeof(BaseUICtrl).GetMethod("Path", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(path.Invoke(ctrl, null), Is.EqualTo("Player/GameplayGuideHud"));
        }

        [Test]
        public void StandardHudControllers_SuppressOnlyArcadeMap()
        {
            foreach (var controllerType in new[]
                     {
                         typeof(PlayerConditionHudUICtrl),
                         typeof(GameplayGuideHudUICtrl)
                     })
            {
                var method = controllerType.GetMethod(
                    "ShouldSuppressForMap",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null, controllerType.Name);
                Assert.That(method.Invoke(null, new object[] { MapIds.Arcade }), Is.EqualTo(true), controllerType.Name);
                Assert.That(method.Invoke(null, new object[] { MapIds.Base }), Is.EqualTo(false), controllerType.Name);
                Assert.That(method.Invoke(null, new object[] { "expedition-test" }), Is.EqualTo(false), controllerType.Name);
            }
        }

        [Test]
        public void LocalizedGuideStrings_IncludeDynamicVisibilityShortcut()
        {
            foreach (var path in new[]
                     {
                         "Assets/Game/Config/Localization/zh-CN.json",
                         "Assets/Game/Config/Localization/en-US.json"
                     })
            {
                var text = (string)JObject.Parse(File.ReadAllText(path))["ui.gameplay_guide.lines"];
                Assert.That(text.Split('\n'), Has.Length.EqualTo(11), path);
                Assert.That(text, Does.Contain("{0}"), path);
                Assert.That(text, Does.Contain("{1}"), path);
                Assert.That(text, Does.Contain("{2}"), path);
                Assert.That(text, Does.Not.Contain("[E]"), path);
                Assert.That(text, Does.Not.Contain("F1"), path);
            }
        }

        [Test]
        public void ShownHud_HidesWholeViewInArcadeAndRecoversFromMapEventWithoutSecondShow()
        {
            var app = Game.Interface.GetSystem<AppStateSystem>();
            app.TryTransition(AppState.Loading);
            app.TryTransition(AppState.Playing);
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            var ctrl = UIHelper.ShowUI<GameplayGuideHudUICtrl>();
            var view = Object.FindObjectOfType<GameplayGuideHudUI>();
            Assert.That(view.GuideRoot.activeSelf, Is.True);
            Assert.That(view.GuideText.text.Split('\n'), Has.Length.EqualTo(11));
            Assert.That(view.VersionText.text, Is.EqualTo("测试版本 vguide-test-v1"));

            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Arcade, null, MapPreparationMode.ArcadePlayerOnly);
            Game.Interface.SendEvent(new MapLoadingCompletedEvent(MapIds.Arcade));
            Assert.That(UIHelper.GetUICtrl<GameplayGuideHudUICtrl>(), Is.SameAs(ctrl));
            Assert.That(ctrl.IsShow(), Is.False);
            Assert.That(view.gameObject.activeSelf, Is.False);
            Assert.That(view.GuideRoot.activeInHierarchy, Is.False);
            Assert.That(view.VersionText.gameObject.activeInHierarchy, Is.False);

            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.SendEvent(new MapLoadingCompletedEvent(MapIds.Base));
            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(view.gameObject.activeSelf, Is.True);
            Assert.That(view.GuideRoot.activeSelf, Is.True);
            Assert.That(view.VersionText.gameObject.activeSelf, Is.True);

            Game.Interface.GetModel<MapSceneModel>().SetLoaded(string.Empty, null);
            Game.Interface.SendEvent(new MapCurrentReleasedEvent(MapIds.Base, 1));
            Assert.That(UIHelper.GetUICtrl<GameplayGuideHudUICtrl>(), Is.SameAs(ctrl));
            Assert.That(ctrl.IsShow(), Is.False);
            Assert.That(view.gameObject.activeSelf, Is.False);

            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.SendEvent(new MapLoadingCompletedEvent(MapIds.Base));
            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(view.gameObject.activeSelf, Is.True);
            Assert.That(view.GuideRoot.activeSelf, Is.True);
            Assert.That(view.VersionText.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void ShownHud_RefreshesThroughSettingsLanguageResourceAndPauseBlockingProducers()
        {
            EnterBasePlaying();
            UIHelper.ShowUI<GameplayGuideHudUICtrl>();
            var view = Object.FindObjectOfType<GameplayGuideHudUI>();
            var before = view.GuideText.text;

            var settings = SettingsData.CreateDefault();
            settings.HotKeys = new System.Collections.Generic.List<HotKeyBinding>
            {
                new HotKeyBinding { ActionId = HotKeyActionIds.ToggleInventory, Context = HotKeyContext.Gameplay, BindingPath = "<Keyboard>/q" },
                new HotKeyBinding { ActionId = HotKeyActionIds.TogglePause, Context = HotKeyContext.Gameplay, BindingPath = "<Keyboard>/f" }
            };
            Game.Interface.GetSystem<SettingsSystem>().Apply(settings);
            Assert.That(view.GuideText.text, Does.Contain("Q"));
            Assert.That(view.GuideText.text, Does.Contain("F"));

            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });
            Assert.That(view.GuideText.text, Is.Not.EqualTo(before));
            Game.Interface.GetSystem<ResourceUpdateSystem>().InitializeStartupInfo(
                new BootResourceStartupInfo(BootResourceSource.BuiltinFallback, string.Empty, "guide-test-v2", string.Empty));
            Assert.That(view.VersionText.text, Does.Contain("guide-test-v2"));

            var app = Game.Interface.GetSystem<AppStateSystem>();
            Assert.That(app.TryTransition(AppState.Paused), Is.True);
            Assert.That(view.GuideRoot.activeSelf, Is.False);
            Assert.That(view.VersionText.gameObject.activeSelf, Is.True);
            Assert.That(app.TryTransition(AppState.Playing), Is.True);
            Assert.That(view.GuideRoot.activeSelf, Is.True);

            UIHelper.ShowUI<BlockingTestUICtrl>();
            Assert.That(view.GuideRoot.activeSelf, Is.True, "窗口通过层级遮挡 HUD，不改变其显示状态");
            Assert.That(view.VersionText.gameObject.activeSelf, Is.True);
            UIHelper.HideUI<BlockingTestUICtrl>();
            Assert.That(view.GuideRoot.activeSelf, Is.True);
            Assert.That(view.VersionText.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void DestroyedHudView_RecreatesAndReceivesSinglePublicEventStream()
        {
            EnterBasePlaying();
            UIHelper.ShowUI<GameplayGuideHudUICtrl>();
            var oldView = Object.FindObjectOfType<GameplayGuideHudUI>();
            Object.DestroyImmediate(oldView.gameObject);

            UIHelper.ShowUI<GameplayGuideHudUICtrl>();
            var replacement = Object.FindObjectOfType<GameplayGuideHudUI>();
            Assert.That(replacement, Is.Not.Null);
            Assert.That(replacement, Is.Not.SameAs(oldView));
            UIHelper.ShowUI<BlockingTestUICtrl>();
            UIHelper.HideUI<BlockingTestUICtrl>();
            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.SendEvent(new MapLoadingCompletedEvent(MapIds.Base));
            Assert.That(replacement.GuideRoot.activeSelf, Is.True);
            Assert.That(replacement.VersionText.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void MobileMode_RefreshesGuideLanguageAndRestoresDesktopHints()
        {
            EnterBasePlaying();
            UIHelper.ShowUI<GameplayGuideHudUICtrl>();
            var view = Object.FindObjectOfType<GameplayGuideHudUI>();
            var desktop = view.GuideText.text;
            var mobile = Game.Interface.GetSystem<MobileInputSystem>();
            mobile.TrySetEditorSimulation(true);
            Assert.That(view.GuideRoot.activeSelf, Is.False, "手机端移除常驻操作说明");
            Assert.That(view.MobileBtnPause.gameObject.activeSelf, Is.True, "隐藏说明不能丢失暂停入口");
            UIHelper.ShowUI<BlockingTestUICtrl>();
            Assert.That(view.MobileBtnPause.gameObject.activeSelf, Is.True, "暂停入口保持显示，由窗口层遮挡");
            view.MobileBtnPause.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Playing), "阻断窗口下拒绝过期点击");
            UIHelper.HideUI<BlockingTestUICtrl>();
            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });
            Assert.That(view.GuideRoot.activeSelf, Is.False);
            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "zh-CN" });
            mobile.TrySetEditorSimulation(false);
            Assert.That(view.GuideText.text, Is.EqualTo(desktop));
            Assert.That(view.GuideRoot.activeSelf, Is.True);
        }

        [Test]
        public void MobilePause_RemainsAvailableOutsideBaseAndUsesExistingPauseRoute()
        {
            Game.Interface.RegisterSystem(new MapSceneSystem(null, null, null, null, null, null));
            EnterBasePlaying();
            Game.Interface.GetModel<MapSceneModel>().SetLoaded("ExpeditionTest", null);
            var input = Game.Interface.GetSystem<MobileInputSystem>();
            input.TrySetEditorSimulation(true);
            UIHelper.ShowUI<GameplayGuideHudUICtrl>();
            var view = Object.FindObjectOfType<GameplayGuideHudUI>();
            Assert.That(view.GuideRoot.activeSelf, Is.False);
            Assert.That(view.MobileBtnPause.gameObject.activeSelf, Is.True);
            view.MobileBtnPause.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
            Assert.That(view.MobileBtnPause.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void MobileSettings_SwitchesLayoutAndHotKeysWithoutChangingBindings()
        {
            var ctrl = UIHelper.ShowUI<SettingUICtrl>();
            var view = Object.FindObjectOfType<SettingUI>();
            view.ControlsTabButton.onClick.Invoke();
            var binding = Game.Interface.GetModel<HotKeyModel>().Bindings[0].BindingPath;
            var mobile = Game.Interface.GetSystem<MobileInputSystem>();
            mobile.TrySetEditorSimulation(true);
            Assert.That(view.ControlsTabButton.gameObject.activeSelf, Is.True);
            Assert.That(view.SettingControlsRoot.activeSelf, Is.False);
            Assert.That(view.SettingMobileRoot.activeSelf, Is.True);
            var sidebar = (RectTransform)view.GeneralTabButton.transform.parent;
            LayoutRebuilder.ForceRebuildLayoutImmediate(sidebar);
            var audioY = view.AudioTabButton.GetComponent<RectTransform>().anchoredPosition.y;
            var networkY = view.NetworkTabButton.GetComponent<RectTransform>().anchoredPosition.y;
            Assert.That(audioY - networkY, Is.EqualTo(144f).Within(0.1f), "手机端保留操作分类供布局编辑");
            ctrl.Hide();
            ctrl.Show();
            Assert.That(view.SettingMobileRoot.activeSelf, Is.True);
            mobile.TrySetEditorSimulation(false);
            Assert.That(view.ControlsTabButton.gameObject.activeSelf, Is.True);
            view.ControlsTabButton.onClick.Invoke();
            Assert.That(view.SettingControlsRoot.activeSelf, Is.True);
            Assert.That(view.SettingMobileRoot.activeSelf, Is.False);
            Assert.That(Game.Interface.GetModel<HotKeyModel>().Bindings[0].BindingPath, Is.EqualTo(binding));
            ctrl.Hide();
        }

        [Test]
        public void DesktopGuideShortcut_PreservesHiddenChoiceAcrossRefreshAndMapReload()
        {
            EnterBasePlaying();
            var ctrl = UIHelper.ShowUI<GameplayGuideHudUICtrl>();
            var view = Object.FindObjectOfType<GameplayGuideHudUI>();
            var keys = Game.Interface.GetSystem<HotKeySystem>();
            Assert.That(view.GuideText.text, Does.Contain("[F5] 隐藏/显示提示"));
            Assert.That(view.GuideText.GetPreferredValues().y, Is.LessThanOrEqualTo(view.GuideText.rectTransform.rect.height),
                "新增快捷键说明必须完整容纳在提示区域中");
            Assert.That(keys.ExecuteBinding("<Keyboard>/f5"), Is.True);
            Assert.That(view.GuideRoot.activeSelf, Is.False);
            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(view.VersionText.gameObject.activeInHierarchy, Is.True);

            var app = Game.Interface.GetSystem<AppStateSystem>();
            app.TryTransition(AppState.Paused);
            app.TryTransition(AppState.Playing);
            UIHelper.ShowUI<BlockingTestUICtrl>();
            UIHelper.HideUI<BlockingTestUICtrl>();
            Game.Interface.SendEvent(new MapLoadingStartedEvent(MapIds.Base));
            Game.Interface.SendEvent(new MapLoadingCompletedEvent(MapIds.Base));
            Assert.That(view.GuideRoot.activeSelf, Is.False);
            Assert.That(keys.ExecuteBinding("<Keyboard>/f5"), Is.True);
            Assert.That(view.GuideRoot.activeSelf, Is.True);
        }

        [Test]
        public void DesktopGuideShortcut_RebindAndLanguageRefreshUseCurrentBinding()
        {
            EnterBasePlaying();
            UIHelper.ShowUI<GameplayGuideHudUICtrl>();
            var view = Object.FindObjectOfType<GameplayGuideHudUI>();
            var keys = Game.Interface.GetSystem<HotKeySystem>();
            Assert.That(keys.BeginRebind("gameplay-guide.toggle").Success, Is.True);
            Assert.That(keys.ReceiveBindingPath("<Keyboard>/h").Success, Is.True);
            Assert.That(view.GuideText.text, Does.Contain("[H] 隐藏/显示提示"));
            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });
            Assert.That(view.GuideText.text, Does.Contain("[H] Hide / Show Hints"));
            Assert.That(view.GuideText.GetPreferredValues().y, Is.LessThanOrEqualTo(view.GuideText.rectTransform.rect.height));
            Assert.That(keys.ExecuteBinding("<Keyboard>/f5"), Is.False);
            Assert.That(keys.ExecuteBinding("<Keyboard>/h"), Is.True);
            Assert.That(view.GuideRoot.activeSelf, Is.False);
            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "zh-CN" });
            Assert.That(view.GuideRoot.activeSelf, Is.False);
            Assert.That(keys.ExecuteBinding("<Keyboard>/h"), Is.True);
            Assert.That(view.GuideText.text, Does.Contain("[H] 隐藏/显示提示"));
        }

        [Test]
        public void DesktopGuideShortcut_IgnoresMobilePausedBlockingAndArcadeContexts()
        {
            EnterBasePlaying();
            UIHelper.ShowUI<GameplayGuideHudUICtrl>();
            var keys = Game.Interface.GetSystem<HotKeySystem>();
            var view = Object.FindObjectOfType<GameplayGuideHudUI>();
            var mobile = Game.Interface.GetSystem<MobileInputSystem>();
            mobile.TrySetEditorSimulation(true);
            Assert.That(keys.TryExecuteAction("gameplay-guide.toggle"), Is.False);
            Assert.That(view.MobileBtnPause.gameObject.activeSelf, Is.True);
            mobile.TrySetEditorSimulation(false);
            var app = Game.Interface.GetSystem<AppStateSystem>();
            app.TryTransition(AppState.Paused);
            Assert.That(keys.TryExecuteAction("gameplay-guide.toggle"), Is.False);
            app.TryTransition(AppState.Playing);
            UIHelper.ShowUI<BlockingTestUICtrl>();
            Assert.That(keys.TryExecuteAction("gameplay-guide.toggle"), Is.False);
            UIHelper.HideUI<BlockingTestUICtrl>();
            Assert.That(view.GuideRoot.activeSelf, Is.True);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Arcade, null, MapPreparationMode.ArcadePlayerOnly);
            Game.Interface.SendEvent(new MapLoadingCompletedEvent(MapIds.Arcade));
            Assert.That(keys.TryExecuteAction("gameplay-guide.toggle"), Is.False);
        }

        private static void EnterBasePlaying()
        {
            var app = Game.Interface.GetSystem<AppStateSystem>();
            app.TryTransition(AppState.Loading);
            app.TryTransition(AppState.Playing);
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
        }

        private static GameObject CreateUiRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            foreach (var layer in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var child = new GameObject(layer, typeof(RectTransform));
                child.transform.SetParent(root.transform, false);
            }
            var eventSystem = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
            eventSystem.transform.SetParent(root.transform, false);
            var inputModule = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputModule != null) eventSystem.AddComponent(inputModule);
            root.AddComponent<UIRoot>();
            return root;
        }

        private sealed class HudLocalizationResourceLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object
            {
                if (typeof(T) != typeof(TextAsset)) return null;
                var assetPath = path == "languages.json"
                    ? "Assets/Game/Config/Localization/languages.json"
                    : "Assets/Game/Config/Localization/" + path;
                return AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath) as T;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, System.Action<T> onLoaded) where T : Object
            {
                onLoaded?.Invoke(LoadAsset<T>(path));
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle) { }
        }

        private sealed class BlockingTestUICtrl : BaseUICtrl
        {
            public override UILayer Layer() => UILayer.Dialog;
            public override bool BlocksGame => true;
            protected override string Path() => "Player/GameplayGuideHud";
            protected override void OnInit() { }
            protected override void OnShow() { }
            protected override void OnHide() { }
        }

        private sealed class HudSettingsFileStore : ISettingsFileStore
        {
            public SettingsData LoadOrDefault(out SettingsLoadResult result) { result = SettingsLoadResult.CreatedDefault; return SettingsData.CreateDefault(); }
            public bool Save(SettingsData data, out string error) { error = null; return true; }
        }

        private sealed class HudDisplayResolutionApplier : IDisplayResolutionApplier
        {
            public void Apply(DisplayResolutionPreset preset, FullScreenMode windowMode) { }
        }
    }
}
