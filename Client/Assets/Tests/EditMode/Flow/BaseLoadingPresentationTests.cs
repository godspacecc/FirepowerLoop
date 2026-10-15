using System;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class BaseLoadingPresentationTests
    {
        private GameObject instance;
        private LoadingUICtrl ctrl;
        private LoadingUI ui;
        private GameObject poolRoot;

        [SetUp]
        public void SetUp()
        {
            UIHelper.ResetForTests();
            ResourceLoader.SetLoaderForTests(new TableLoader());
            LocalizationLanguages.ResetForTests();
            var architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(architecture, true);
            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterModel(new RuntimeLogModel());
            architecture.RegisterModel(new AppStateModel());
            architecture.RegisterUtility(new LocalizationUtility());
            var uiRoot = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            foreach (var layer in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
                new GameObject(layer, typeof(RectTransform)).transform.SetParent(uiRoot.transform, false);
            var eventSystem = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            eventSystem.transform.SetParent(uiRoot.transform, false);
            poolRoot = new GameObject("PoolRoot");
            uiRoot.AddComponent<UIRoot>();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
            instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Common/Loading.prefab"));
            ui = instance.GetComponent<LoadingUI>();
            ctrl = new LoadingUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ctrl, ui);
        }

        [TearDown]
        public void TearDown()
        {
            ctrl.Hide();
            var currentView = (BaseUI)typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ctrl);
            if (currentView != null) Object.DestroyImmediate(currentView.gameObject);
            Object.DestroyImmediate(instance);
            UIHelper.ResetForTests();
            Object.DestroyImmediate(poolRoot);
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
        }

        [Test]
        public void BaseMapUsesCampAndNumericPercentage()
        {
            Invoke("OnLoadingStarted", new MapLoadingStartedEvent(MapIds.Base));
            Invoke("OnProgress", new MapLoadingProgressEvent(MapIds.Base, .67f));
            Assert.That(ui.PercentText.text, Is.EqualTo("67%"));
            Assert.That(ui.MessageText.text, Is.EqualTo("正在加载场景…"));
            AssertActive("BaseArtwork", true);
            AssertActive("BackgroundArtwork", false);
            AssertActive("BaseTitle", true);
            AssertActive("Title", false);
        }

        [Test]
        public void StageShowsActualWorkWithoutElapsedAndKeepsPercentage()
        {
            Invoke("OnLoadingStarted", new MapLoadingStartedEvent(MapIds.CivilisationValley));
            Invoke("OnProgress", new MapLoadingProgressEvent(MapIds.CivilisationValley,.67f));
            Invoke("OnStageChanged", StageEvent(MapIds.CivilisationValley,"Points"));
            Assert.That(ui.MessageText.text,Does.Contain("树木与采集点"));
            Assert.That(ui.MessageText.text,Does.Not.Contain("秒"));
            Assert.That(ui.PercentText.text,Is.EqualTo("67%"));
            Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US");
            typeof(LoadingUICtrl).GetMethod("RefreshPresentation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ctrl,null);
            Assert.That(ui.MessageText.text,Does.Contain("trees"));
            Assert.That(ui.MessageText.text,Does.Not.Contain("elapsed"));
        }

        [Test]
        public void StageChangeRejectsAnotherMapWithoutElapsed()
        {
            Invoke("OnLoadingStarted",new MapLoadingStartedEvent(MapIds.CivilisationValley));
            Invoke("OnStageChanged",StageEvent(MapIds.CivilisationValley,"Navigation"));
            Invoke("OnStageChanged",StageEvent(MapIds.Base,"Points"));
            Assert.That(ui.MessageText.text,Does.Contain("导航"));
            Assert.That(ui.MessageText.text,Does.Not.Contain("秒"));
            Invoke("OnStageChanged",StageEvent(MapIds.CivilisationValley,"GroundCover"));
            Assert.That(ui.MessageText.text,Does.Contain("草灌"));
            Assert.That(ui.MessageText.text,Does.Not.Contain("秒"));
        }

        [Test]
        public void MapFailureShowsActualReasonAndRetainsItAfterLanguageChange()
        {
            const string reason="地图加载异常：MethodNotFind UnityEngine.CombineInstance::set_mesh";
            Invoke("OnLoadingStarted",new MapLoadingStartedEvent(MapIds.CivilisationValley));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error,"[LoadingUICtrl] 地图加载失败："+reason);
            Invoke("OnFailed",new MapLoadingFailedEvent(MapIds.CivilisationValley,reason));
            Assert.That(ui.ErrorRoot.activeSelf,Is.True);
            Assert.That(ui.ErrorText.text,Does.Contain(reason));
            Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US");
            typeof(LoadingUICtrl).GetMethod("RefreshPresentation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ctrl,null);
            Assert.That(ui.ErrorText.text,Does.Contain("Failed to load"));
            Assert.That(ui.ErrorText.text,Does.Contain(reason));
        }

        [Test]
        public void MapFailureWithoutReasonKeepsReadableFallback()
        {
            Invoke("OnLoadingStarted",new MapLoadingStartedEvent(MapIds.CivilisationValley));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error,"[LoadingUICtrl] 地图加载失败：");
            Invoke("OnFailed",new MapLoadingFailedEvent(MapIds.CivilisationValley,null));
            Assert.That(ui.ErrorText.text,Is.EqualTo("地图加载失败"));
            Assert.That(ui.ErrorRoot.activeSelf,Is.True);
        }

        [Test]
        public void CopyFailureIncludesStageReasonAndLogStackWithoutReplacingError()
        {
            Invoke("OnLoadingStarted",new MapLoadingStartedEvent(MapIds.CivilisationValley));
            Invoke("OnStageChanged",StageEvent(MapIds.CivilisationValley,"GroundCover"));
            Game.Interface.GetModel<RuntimeLogModel>().Add(new RuntimeLogEntry(DateTime.Now,LogType.Exception,"mesh failed","ValleyVegetationBuilder.Create()"));
            Fail("MethodNotFind UnityEngine.CombineInstance::set_mesh");
            var error=ui.ErrorText.text;
            var previous=GUIUtility.systemCopyBuffer;
            try
            {
                CopyButton().onClick.Invoke();
                Assert.That(GUIUtility.systemCopyBuffer,Does.Contain(MapIds.CivilisationValley));
                Assert.That(GUIUtility.systemCopyBuffer,Does.Contain("GroundCover"));
                Assert.That(GUIUtility.systemCopyBuffer,Does.Contain("MethodNotFind UnityEngine.CombineInstance::set_mesh"));
                Assert.That(GUIUtility.systemCopyBuffer,Does.Contain("ValleyVegetationBuilder.Create()"));
                Assert.That(GUIUtility.systemCopyBuffer,Does.Contain("Application.version"));
                Assert.That(ui.ErrorText.text,Is.EqualTo(error));
                Assert.That(CopyStatus().text,Is.EqualTo("已复制日志"));
                Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US");
                Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN","en-US"));
                Assert.That(CopyStatus().text,Is.EqualTo("Logs copied"));
                Assert.That(ui.ErrorText.text,Does.Contain("MethodNotFind"));
            }
            finally { GUIUtility.systemCopyBuffer=previous; }
        }

        [Test]
        public void CopyFailureWithoutBufferedLogsStillIncludesRawReason()
        {
            Invoke("OnLoadingStarted",new MapLoadingStartedEvent(MapIds.CivilisationValley));
            Fail("no buffered logs yet");
            var previous=GUIUtility.systemCopyBuffer;
            try
            {
                CopyButton().onClick.Invoke();
                Assert.That(GUIUtility.systemCopyBuffer,Does.Contain("no buffered logs yet"));
                Assert.That(CopyButton().gameObject.activeSelf,Is.True);
                Assert.That(CopyStatus().text,Is.EqualTo("已复制日志"));
            }
            finally { GUIUtility.systemCopyBuffer=previous; }
        }

        [Test]
        public void NewMapRequestClearsCopyFeedbackAndRejectsOldFailureCopy()
        {
            Invoke("OnLoadingStarted",new MapLoadingStartedEvent(MapIds.CivilisationValley));
            Fail("old failure");
            var previous=GUIUtility.systemCopyBuffer;
            try
            {
                CopyButton().onClick.Invoke();
                Invoke("OnLoadingStarted",new MapLoadingStartedEvent(MapIds.Base));
                Assert.That(CopyStatus().text,Is.Empty);
                Assert.That(CopyButton().gameObject.activeSelf,Is.False);
                GUIUtility.systemCopyBuffer="unchanged";
                CopyButton().onClick.Invoke();
                Assert.That(GUIUtility.systemCopyBuffer,Is.EqualTo("unchanged"));
            }
            finally { GUIUtility.systemCopyBuffer=previous; }
        }

        private void Fail(string reason)
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error,"[LoadingUICtrl] 地图加载失败："+reason);
            Invoke("OnFailed",new MapLoadingFailedEvent(MapIds.CivilisationValley,reason));
        }

        private UnityEngine.UI.Button CopyButton()
        {
            var field=typeof(LoadingUI).GetField("BtnCopyLog");
            Assert.That(field,Is.Not.Null,"加载失败面板尚未提供复制日志按钮");
            var button=field.GetValue(ui) as UnityEngine.UI.Button;
            Assert.That(button,Is.Not.Null,"复制日志按钮引用未绑定");
            return button;
        }

        private TMPro.TextMeshProUGUI CopyStatus()
        {
            var field=typeof(LoadingUI).GetField("CopyStatusText");
            Assert.That(field,Is.Not.Null);
            return (TMPro.TextMeshProUGUI)field.GetValue(ui);
        }

        private static object StageEvent(string mapId,string stage)
        {
            var assembly=typeof(MapSceneSystem).Assembly;
            var eventType=assembly.GetType("Game.MapLoadingStageEvent");
            Assert.That(eventType,Is.Not.Null,"地图加载尚未提供真实阶段事件");
            return Activator.CreateInstance(eventType,mapId,Enum.Parse(assembly.GetType("Game.MapLoadingStage"),stage));
        }

        [Test]
        public void UnknownDownloadSizeDoesNotClaimDownloadingZeroBytes()
        {
            ctrl.BeginResourceDownload(10);
            Assert.That(ui.DownloadMessageText.text, Does.Not.Contain("0 B/0 B"));
            Assert.That(ui.DownloadSlider.gameObject.activeSelf, Is.False);
            Assert.That(ui.DownloadPercentText.gameObject.activeSelf, Is.False);
            Assert.That(UIHelper.GetUICtrl<ResourceDownloadHudUICtrl>()?.IsShow() ?? false, Is.False);
        }

        [Test]
        public void FailedDownloadClosesProgressPopupAndRing()
        {
            ctrl.BeginResourceDownload(10);
            ctrl.SetResourceDownloadProgress(10, new OptionalGameplayDownloadProgress(0, 1024, 0, 2));
            ctrl.EndResourceDownload(10, false);
            Assert.That(ctrl.IsShow(), Is.False);
            Assert.That(UIHelper.GetUICtrl<ResourceDownloadHudUICtrl>()?.IsShow() ?? false, Is.False);
        }

        [Test]
        public void KnownDownloadSizeAppearsBeforeFirstNetworkProgress()
        {
            ctrl.BeginResourceDownload(10, new OptionalGameplayDownloadProgress(0, 1024, 0, 2));
            Assert.That(ui.DownloadMessageText.text, Does.Contain("0 B/1.0 KB"));
            Assert.That(ui.DownloadSlider.gameObject.activeSelf, Is.True);
            Assert.That(UIHelper.GetUICtrl<ResourceDownloadHudUICtrl>()?.IsShow(), Is.True);
            ctrl.SetResourceDownloadProgress(10, default);
            Assert.That(ui.DownloadMessageText.text, Does.Contain("0 B/1.0 KB"), "未知进度不能覆盖有效总大小");
        }

        [Test]
        public void DownloadPopupUsesCenteredPanelAndAllowsClosing()
        {
            ctrl.BeginResourceDownload(10);
            var panel = instance.transform.Find("DownloadRoot/Panel") as RectTransform;
            Assert.That(panel, Is.Not.Null);
            Assert.That(panel.anchorMin, Is.EqualTo(new Vector2(.5f, .5f)));
            Assert.That(panel.anchorMax, Is.EqualTo(new Vector2(.5f, .5f)));
            Assert.That(panel.sizeDelta.x, Is.LessThanOrEqualTo(640));
            Assert.That(panel.sizeDelta.y, Is.LessThanOrEqualTo(360));
            Assert.That(ui.MapArtwork.activeInHierarchy, Is.False);
            Assert.That(instance.GetComponent<UnityEngine.UI.Image>().color.a, Is.Zero,
                "下载弹窗不能被旧的不透明全屏根背景遮住基地画面");
            Assert.That(ui.MapRoot.GetComponent<UnityEngine.UI.Image>().color.a, Is.EqualTo(1),
                "场景加载仍需保留不透明全屏覆盖");
            ctrl.Hide();
            Assert.That(ctrl.IsShow(), Is.False);
        }

        [Test]
        public void DownloadRestoresLatestBaseProgressAndRejectsStaleRequest()
        {
            ctrl.BeginResourceDownload(10);
            Invoke("OnLoadingStarted", new MapLoadingStartedEvent(MapIds.Base));
            Invoke("OnProgress", new MapLoadingProgressEvent(MapIds.Base, .67f));
            ctrl.SetResourceDownloadProgress(10, new OptionalGameplayDownloadProgress(512, 1024, 1, 2));
            ctrl.SetResourceDownloadProgress(9, new OptionalGameplayDownloadProgress(1024, 1024, 2, 2));
            Assert.That(ui.PercentText.text, Is.EqualTo("67%"), "地图加载优先保持全屏进度");
            Assert.That(ui.MapRoot.activeSelf, Is.True);
            ctrl.EndResourceDownload(9, true);
            Assert.That(ui.BtnBack.interactable, Is.True);
            ctrl.EndResourceDownload(10, true);
            Assert.That(ui.PercentText.text, Is.EqualTo("67%"));
            AssertActive("BaseArtwork", true);
            Assert.That(ui.BtnBack.interactable, Is.True);
        }

        [Test]
        public void HiddenDownloadUpdatesRingAndReopensLatestSnapshot()
        {
            ctrl.BeginResourceDownload(10);
            ctrl.Hide();
            ctrl.SetResourceDownloadProgress(10, new OptionalGameplayDownloadProgress(512, 1024, 1, 2));
            ctrl.SetResourceDownloadProgress(9, new OptionalGameplayDownloadProgress(1024, 1024, 2, 2));
            var hudCtrl = UIHelper.GetUICtrl<ResourceDownloadHudUICtrl>();
            var hud = (ResourceDownloadHudUI)typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hudCtrl);
            Assert.That(ctrl.IsShow(), Is.False);
            Assert.That(hudCtrl.BlocksGame, Is.False);
            Assert.That(hudCtrl.IsShow(), Is.True);
            Assert.That(hud.ProgressRing.fillAmount, Is.EqualTo(.5f));
            Assert.That(hud.PercentText.text, Is.EqualTo("50%"));
            hud.BtnDetails.onClick.Invoke();
            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(ui.DownloadPercentText.text, Is.EqualTo("50%"));
            Assert.That(ui.DownloadMessageText.text, Does.Contain("512 B/1.0 KB"));
            Assert.That(ctrl.ShouldResumeDownloadEntry, Is.False);
            ctrl.EndResourceDownload(9, true);
            Assert.That(hudCtrl.IsShow(), Is.True);
            ctrl.EndResourceDownload(10, true);
            Assert.That(hudCtrl.IsShow(), Is.False);
            Assert.That(ctrl.IsShow(), Is.False);
        }

        [Test]
        public void DestroyedPopupReopensFromRingWithoutLosingProgress()
        {
            ctrl.BeginResourceDownload(10);
            ctrl.Hide();
            Object.DestroyImmediate(instance);
            ctrl.SetResourceDownloadProgress(10, new OptionalGameplayDownloadProgress(512, 1024, 1, 2));
            var hudCtrl = UIHelper.GetUICtrl<ResourceDownloadHudUICtrl>();
            var hud = (ResourceDownloadHudUI)typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hudCtrl);
            hud.BtnDetails.onClick.Invoke();
            var recreated = (LoadingUI)typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ctrl);
            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(recreated.DownloadPercentText.text, Is.EqualTo("50%"));
            ctrl.EndResourceDownload(10, true);
        }

        [Test]
        public void RepeatedShowSwitchesFromBaseToOtherMapAndKeepsErrorReturn()
        {
            Invoke("OnLoadingStarted", new MapLoadingStartedEvent(MapIds.Base));
            Invoke("OnLoadingStarted", new MapLoadingStartedEvent(MapIds.HeJiaVillage));
            AssertActive("BaseArtwork", false);
            AssertActive("BackgroundArtwork", true);
            AssertActive("Title", true);
            ctrl.ShowError("测试错误");
            Assert.That(ui.ErrorRoot.activeSelf, Is.True);
            Assert.That(ui.ErrorText.text, Is.EqualTo("测试错误"));
            ctrl.Hide();
            Assert.That(ctrl.IsShow(), Is.False);
        }

        [TestCase(0L, "0 B")]
        [TestCase(1L, "1 B")]
        [TestCase(1023L, "1023 B")]
        [TestCase(1024L, "1.0 KB")]
        [TestCase(52429L, "51.2 KB")]
        [TestCase(1048576L, "1.0 MB")]
        public void DownloadSizeRemainsReadableInBothLanguages(long bytes, string expected)
        {
            ctrl.BeginResourceDownload(10);
            foreach (var language in new[] { "zh-CN", "en-US" })
            {
                Game.Interface.GetModel<LocalizationModel>().SetLanguageCode(language);
                Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN", language));
                ctrl.SetResourceDownloadProgress(10, new OptionalGameplayDownloadProgress(bytes, bytes, 1, 1));
                if (bytes == 0)
                    Assert.That(ui.DownloadMessageText.text, Is.EqualTo(Loc.T("ui.optional.expedition.preparing")), language);
                else
                    Assert.That(ui.DownloadMessageText.text, Does.Contain(expected + "/" + expected), language);
                Assert.That(ui.DownloadMessageText.text, Does.Not.Contain("ui.loading.download_status"));
            }
        }

        [Test]
        public void LanguageChangeRefreshesStatusWithoutResettingProgress()
        {
            Invoke("OnLoadingStarted", new MapLoadingStartedEvent(MapIds.Base));
            Invoke("OnProgress", new MapLoadingProgressEvent(MapIds.Base, .67f));
            Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US");
            Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN", "en-US"));
            Assert.That(ui.PercentText.text, Is.EqualTo("67%"));
            Assert.That(ui.MessageText.text, Is.EqualTo("Loading scene…"));
            Assert.That(ui.BaseTitle.GetComponent<TMPro.TextMeshProUGUI>().text, Is.EqualTo("Entering Base"));
        }

        [Test]
        public void FormalPrefabGeneratedReferencesResolveToCollectorObjects()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Common/Loading.prefab");
            var view = prefab.GetComponent<LoadingUI>();
            var collector = prefab.GetComponent<ReferenceCollector>();
            Assert.That(collector.data.Count, Is.EqualTo(typeof(LoadingUI).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Length));
            foreach (var entry in collector.data)
            {
                var field = typeof(LoadingUI).GetField(entry.key);
                Assert.That(field, Is.Not.Null, entry.key);
                Assert.That(field.GetValue(view), Is.SameAs(entry.gameObject), entry.key);
            }
            Assert.That(AssetDatabase.GetAssetPath(view.BaseArtwork.GetComponent<UnityEngine.UI.Image>().sprite), Is.EqualTo("Assets/Game/Art/UI/Loading/BaseLoadingBackground.png"));
            Assert.That(AssetDatabase.GetAssetPath(view.MapArtwork.GetComponent<UnityEngine.UI.Image>().sprite), Is.EqualTo("Assets/Game/Art/UI/Loading/LoadingBackground.png"));
            Assert.That(view.BaseTitle.transform.GetSiblingIndex(), Is.LessThan(view.ErrorRoot.transform.GetSiblingIndex()), "错误遮罩必须覆盖基地标题");
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Common/ResourceDownloadHud.prefab");
            var hudView = hud.GetComponent<ResourceDownloadHudUI>();
            foreach (var entry in hud.GetComponent<ReferenceCollector>().data)
            {
                var field = typeof(ResourceDownloadHudUI).GetField(entry.key);
                Assert.That(field, Is.Not.Null, entry.key);
                Assert.That(field.GetValue(hudView), Is.SameAs(entry.gameObject), entry.key);
            }
            Assert.That(hudView.ProgressRing.type, Is.EqualTo(UnityEngine.UI.Image.Type.Filled));
            Assert.That(hudView.ProgressRing.fillMethod, Is.EqualTo(UnityEngine.UI.Image.FillMethod.Radial360));
            Assert.That(hudView.ProgressRing.raycastTarget, Is.False);
        }

        private void AssertActive(string path, bool active)
        {
            var node = instance.GetComponentsInChildren<Transform>(true).Single(item => item.name == path);
            Assert.That(node, Is.Not.Null, path);
            Assert.That(node.gameObject.activeSelf, Is.EqualTo(active), path);
        }

        private void Invoke(string method, object arg) => typeof(LoadingUICtrl)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ctrl, new[] { arg });

        private sealed class TableLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(
                "Assets/Game/Config/Localization/" + (path == LocalizationLanguages.LanguagesAddress ? "languages.json" : path));
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> callback) where T : Object
            {
                callback?.Invoke(LoadAsset<T>(path));
                return ResourceLoadHandle.Empty;
            }
            public void Release(ResourceLoadHandle handle) { }
        }
    }
}
