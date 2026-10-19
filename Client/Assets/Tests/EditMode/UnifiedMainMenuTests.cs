using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>验证统一主菜单直接开局的资源门禁；使用临时存档，不加载地图或连接网络。</summary>
    public sealed class UnifiedMainMenuTests
    {
        private Game architecture;
        private GameObject instance;
        private StartUICtrl controller;
        private DeferredPreparation preparation;
        private string saveRoot;
        private GameObject uiRoot;

        [SetUp]
        public void SetUp()
        {
            UIHelper.ResetForTests();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
            uiRoot = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            foreach (var layer in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
                new GameObject(layer, typeof(RectTransform)).transform.SetParent(uiRoot.transform, false);
            var events = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            events.transform.SetParent(uiRoot.transform, false);
            uiRoot.AddComponent<UIRoot>();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(architecture, true);
            ResourceLoader.SetLoaderForTests(new LocalizationLoader());
            LocalizationLanguages.ResetForTests();
            architecture.RegisterModel(new ArcadeRunModel());
            architecture.RegisterModel(new MapSceneModel());
            architecture.RegisterModel(new OnlineArcadeRunModel());
            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterUtility(new LocalizationUtility());
            architecture.RegisterSystem(new OnlineArcadeRunSystem());
            var system = new ArcadeRunSystem();
            architecture.RegisterSystem(system);
            saveRoot = System.IO.Path.Combine(Application.temporaryCachePath, "UnifiedMenu-" + Guid.NewGuid().ToString("N"));
            system.ConfigureForTests(saveRoot);
            preparation = new DeferredPreparation();
            architecture.RegisterUtility<IGameplayResourcePreparation>(preparation);
            instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab"));
            controller = new StartUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, instance.GetComponent<StartUI>());
            typeof(BaseUICtrl).GetField("_isActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, true);
        }

        [TearDown]
        public void TearDown()
        {
            controller.Hide();
            preparation.CompleteAll();
            UIHelper.ResetForTests();
            if (uiRoot != null) Object.DestroyImmediate(uiRoot);
            Object.DestroyImmediate(instance);
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, true);
        }

        [Test]
        public void Loading_IslandPreparationCoversHudUntilResourcesAndCheckpointAreReady()
        {
            var loading = UIHelper.ShowUI<LoadingUICtrl>();
            loading.Hide();
            architecture.SendEvent(new MapLoadingStartedEvent(global::Game.MapIds.Arcade));
            architecture.SendEvent(new ArcadeCombatHudChangedEvent(new ArcadeCombatHudSnapshot { IslandTransitioning = true }));
            architecture.SendEvent(new MapLoadingCompletedEvent(global::Game.MapIds.Arcade));
            Assert.That(loading.IsShow(), Is.True, "基础场景完成不能暴露尚未就绪的海岛");
            loading.Hide();
            Assert.That(loading.IsShow(), Is.True, "准备期间不能手动关闭遮罩");
            architecture.SendEvent(new ArcadeCombatHudChangedEvent(new ArcadeCombatHudSnapshot()));
            Assert.That(loading.IsShow(), Is.False, "资源及安全节点都准备完成才交还战斗");
        }

        [Test]
        public void Loading_LaterIslandTransitionReopensLoadingWithoutAnotherSceneLoad()
        {
            var loading = UIHelper.ShowUI<LoadingUICtrl>();
            loading.Hide();
            architecture.SendEvent(new ArcadeCombatHudChangedEvent(new ArcadeCombatHudSnapshot { IslandTransitioning = true }));
            Assert.That(loading.IsShow(), Is.True, "轮间切图和宝藏房准备也需要加载遮罩");
            architecture.SendEvent(new ArcadeCombatHudChangedEvent(new ArcadeCombatHudSnapshot()));
            Assert.That(loading.IsShow(), Is.False);
        }

        [Test]
        public void Loading_PreparationFailureKeepsDetailsAndRetryOnLoadingScreen()
        {
            var loading = UIHelper.ShowUI<LoadingUICtrl>();
            int entry = loading.BeginEntryPreparation(global::Game.MapIds.Arcade);
            architecture.SendEvent(new ArcadeCombatHudChangedEvent(new ArcadeCombatHudSnapshot
            { IslandTransitioning = true, CanRetryCompletion = true, IslandPreparationError = "shader unavailable" }));
            loading.EndEntryPreparation(entry);
            var view = Object.FindObjectOfType<LoadingUI>();
            Assert.That(loading.IsShow(), Is.True);
            Assert.That(view.ErrorRoot.activeSelf, Is.True);
            Assert.That(view.ErrorText.text, Does.Contain("shader unavailable"));
            Assert.That(view.BtnRetry.gameObject.activeInHierarchy, Is.True);
            Assert.That(view.BtnRetry.GetComponentInChildren<TMPro.TMP_Text>().text, Is.EqualTo("重试"));
            Assert.That(view.BtnBack.interactable, Is.True);
            Assert.That(view.BtnCopyLog.gameObject.activeInHierarchy, Is.True);
            loading.Hide();
            Assert.That(loading.IsShow(), Is.True, "失败现场不能仅关闭加载页");
            architecture.SendEvent(new LanguageChangedEvent("zh-CN", "en-US"));
            Assert.That(view.ErrorText.text, Does.Contain("shader unavailable"));
            architecture.SendEvent(new ArcadeCombatHudChangedEvent(new ArcadeCombatHudSnapshot { IslandTransitioning = true }));
            Assert.That(view.ErrorRoot.activeSelf, Is.False);
            Assert.That(view.BtnRetry.gameObject.activeSelf, Is.False);
            Assert.That(view.BtnBack.interactable, Is.False);
            Assert.That(loading.IsShow(), Is.True, "重试完成前继续遮挡地图");
            architecture.SendEvent(new ArcadeCombatHudChangedEvent(new ArcadeCombatHudSnapshot()));
            Assert.That(loading.IsShow(), Is.False);
        }

        [Test]
        public void Loading_CompletionSaveFailureDoesNotBecomeMapPreparation()
        {
            var loading = UIHelper.ShowUI<LoadingUICtrl>();
            loading.Hide();
            architecture.SendEvent(new ArcadeCombatHudChangedEvent(new ArcadeCombatHudSnapshot { CanRetryCompletion = true }));
            Assert.That(loading.IsShow(), Is.False, "普通结算保存失败仍由原结算界面处理");
        }

        [Test]
        public void Loading_DestroyedViewRebindsPreparationAndReleasesOnlyWhenReady()
        {
            var loading = UIHelper.ShowUI<LoadingUICtrl>();
            architecture.SendEvent(new ArcadeCombatHudChangedEvent(new ArcadeCombatHudSnapshot { IslandTransitioning = true }));
            Object.DestroyImmediate(Object.FindObjectOfType<LoadingUI>().gameObject);
            loading.Show();
            loading.Hide();
            Assert.That(loading.IsShow(), Is.True);
            architecture.SendEvent(new ArcadeCombatHudChangedEvent(new ArcadeCombatHudSnapshot()));
            Assert.That(loading.IsShow(), Is.False);
        }

        [Test]
        public void SinglePlayerClick_ShowsLoadingBeforePreparationAndClosesOnFailure()
        {
            Invoke("OnNewGameClick");
            var loading = UIHelper.GetUICtrl<LoadingUICtrl>();
            Assert.That(loading?.IsShow(), Is.True, "等待资源检查期间必须有可见反馈");
            var view = Object.FindObjectOfType<LoadingUI>();
            Assert.That(view.MapRoot.activeSelf, Is.True);
            Assert.That(view.ErrorRoot.activeSelf, Is.False);
            loading.Hide();
            Assert.That(loading.IsShow(), Is.True, "开局准备中不能关闭加载遮罩");
            preparation.Requests[0].TrySetResult(false);
            Assert.That(loading.IsShow(), Is.False, "资源准备失败应恢复可操作菜单");
            Assert.That(controller.IsShow(), Is.True);
        }

        [Test]
        public void Loading_MapEventsUpdateProgressAndStayUntilEntryFinishes()
        {
            Invoke("OnNewGameClick");
            var loading = UIHelper.GetUICtrl<LoadingUICtrl>();
            Assert.That(loading?.IsShow(), Is.True);
            var view = Object.FindObjectOfType<LoadingUI>();
            architecture.SendEvent(new MapLoadingStartedEvent(global::Game.MapIds.Arcade));
            architecture.SendEvent(new MapLoadingProgressEvent(global::Game.MapIds.Arcade, .5f));
            Assert.That(view.ProgressSlider.value, Is.EqualTo(.5f));
            architecture.SendEvent(new MapLoadingCompletedEvent(global::Game.MapIds.Arcade));
            Assert.That(loading.IsShow(), Is.True, "整个开局事务结束前继续遮挡菜单");
            preparation.Requests[0].TrySetResult(false);
            Assert.That(loading.IsShow(), Is.False);
        }

        [Test]
        public void Loading_MapFailureRemainsVisibleAndCanReturnToMenu()
        {
            Invoke("OnNewGameClick");
            var loading = UIHelper.GetUICtrl<LoadingUICtrl>();
            Assert.That(loading?.IsShow(), Is.True);
            var view = Object.FindObjectOfType<LoadingUI>();
            architecture.SendEvent(new MapLoadingStartedEvent(global::Game.MapIds.Arcade));
            LogAssert.Expect(LogType.Error, "[LoadingUICtrl] 地图加载失败：test map failure");
            architecture.SendEvent(new MapLoadingFailedEvent(global::Game.MapIds.Arcade, "test map failure"));
            preparation.Requests[0].TrySetResult(false);
            Assert.That(loading.IsShow(), Is.True);
            Assert.That(view.ErrorRoot.activeSelf, Is.True);
            Assert.That(view.ErrorText.text, Does.Contain("test map failure"));
            Assert.That(view.BtnBack.interactable, Is.True);
            view.BtnBack.onClick.Invoke();
            Assert.That(loading.IsShow(), Is.False);
            Assert.That(controller.IsShow(), Is.True);
        }

        [Test]
        public void SinglePlayerClick_PreparesArenaDirectlyAndRejectsRepeatedClick()
        {
            Invoke("OnNewGameClick");
            Invoke("OnNewGameClick");
            Assert.That(preparation.Requests.Count, Is.EqualTo(1), "无存档时应直接准备街机资源，而不是再打开模式选择页");
            Assert.That(preparation.MapIds, Is.EqualTo(new[] { global::Game.MapIds.Arcade }));
            Assert.That(instance.GetComponent<StartUI>().BtnNewGame.interactable, Is.False);
            Assert.That(architecture.GetModel<ArcadeRunModel>().HasRun, Is.False, "资源准备完成前不能提交新局");
        }

        [Test]
        public void SinglePlayerPreparationFailure_AllowsRetryWithoutCreatingSave()
        {
            Invoke("OnNewGameClick");
            Assert.That(preparation.Requests.Count, Is.EqualTo(1));
            preparation.Requests[0].TrySetResult(false);
            Assert.That(instance.GetComponent<StartUI>().BtnNewGame.interactable, Is.True);
            Assert.That(architecture.GetSystem<ArcadeRunSystem>().InspectPending().Status, Is.EqualTo(SaveFileStatus.Missing));
            Invoke("OnNewGameClick");
            Assert.That(preparation.Requests.Count, Is.EqualTo(2));
        }

        [Test]
        public void LeavingMenu_InvalidatesPendingStartBeforeItCanWriteSave()
        {
            Invoke("OnNewGameClick");
            Assert.That(preparation.Requests.Count, Is.EqualTo(1));
            controller.Hide();
            Assert.That(UIHelper.GetUICtrl<LoadingUICtrl>()?.IsShow(), Is.Not.True);
            Assert.That(preparation.Owners[0](), Is.False);
            preparation.Requests[0].TrySetResult(true);
            Assert.That(architecture.GetSystem<ArcadeRunSystem>().InspectPending().Status, Is.EqualTo(SaveFileStatus.Missing));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SavedRun_ShowsProgressOrResultInsteadOfOfferingToOverwrite(bool hasResult)
        {
            var system = architecture.GetSystem<ArcadeRunSystem>();
            Assert.That(system.TryStartNewRun(null, out var snapshot, out var error), Is.True, error);
            snapshot.Score = 12750;
            snapshot.CompletedArenaCount = 4;
            snapshot.NextArenaNumber = 5;
            snapshot.Checkpoint.ArenaNumber = 5;
            snapshot.RemainingLives = 2;
            snapshot.Outcome = hasResult ? "failed" : null;
            Assert.That(new ArcadeRunFileStore(saveRoot).Save(snapshot, out error), Is.True, error);
            Invoke("RefreshSaveButtons");
            var view = instance.GetComponent<StartUI>();
            Assert.That(view.BtnNewGame.gameObject.activeSelf, Is.False, "已有进度或待处理结果时不能直接覆盖开新局");
            Assert.That(view.BtnContinue.gameObject.activeSelf, Is.True);
            Assert.That(view.BtnContinue.GetComponentInChildren<TMPro.TMP_Text>(true).text,
                Is.EqualTo(hasResult ? "查看本局结果" : "继续本局"));
            Assert.That(view.RunSummaryText.gameObject.activeSelf, Is.True);
            Assert.That(view.RunSummaryText.text, Does.Contain("12750"));
            if (!hasResult)
            {
                Assert.That(view.RunSummaryText.text, Does.Contain("5"));
                Assert.That(view.RunSummaryText.text, Does.Contain("2"));
            }
            Assert.That(system.InspectPending().Data.RunId, Is.EqualTo(snapshot.RunId));
            Assert.That(preparation.Requests, Is.Empty);
        }

        [Test]
        public void MenuInit_PreparesHiddenResumeContentWithoutChangingRunPhase()
        {
            var system = architecture.GetSystem<ArcadeRunSystem>();
            Assert.That(system.TryStartNewRun(null, out _, out var error), Is.True, error);
            Assert.That(system.TryContinue(out _, out error), Is.True, error);
            Invoke("OnInit");
            var entry = UIHelper.GetUICtrl<ArcadeResumeUICtrl>();
            Assert.That(entry, Is.Not.Null, "开始页初始化时应完成恢复窗口准备");
            Assert.That(entry.IsShow(), Is.False);
            Assert.That(UIHelper.HasUIOpen, Is.False, "预加载不能发布可见或输入阻断状态");
            var view = Object.FindObjectOfType<ArcadeResumeUI>(true);
            Assert.That(view.gameObject.activeSelf, Is.False);
            Assert.That(view.TextSummary.text, Is.Not.Empty);
            Assert.That(view.BtnContinue.gameObject.activeSelf, Is.True);
            Assert.That(architecture.GetModel<ArcadeRunModel>().Phase, Is.EqualTo(ArcadeRunPhase.Arena));
            Assert.That(preparation.Requests, Is.Empty, "预加载菜单不能开始准备战斗地图");
        }

        [Test]
        public void ContinueClick_ReusesPreparedViewAndValidatedSave()
        {
            var system = architecture.GetSystem<ArcadeRunSystem>();
            Assert.That(system.TryStartNewRun(null, out _, out var error), Is.True, error);
            Invoke("OnInit");
            var view = Object.FindObjectOfType<ArcadeResumeUI>(true);
            Assert.That(view, Is.Not.Null);
            UIHelper.SetPrefabLoaderForTests(_ => throw new InvalidOperationException("点击不应再加载 Prefab"));
            var store = new ArcadeRunFileStore(saveRoot);
            // 文件内容已在开始页校验；独占内容句柄可证明点击不再重复读取 JSON。
            using (new FileStream(store.RunPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Invoke("OnContinueClick");
                Assert.That(UIHelper.GetUICtrl<ArcadeResumeUICtrl>().IsShow(), Is.True);
                Assert.That(Object.FindObjectOfType<ArcadeResumeUI>(), Is.SameAs(view));
            }
        }

        [Test]
        public void MenuRefresh_AfterHiddenViewDestroyedPreparesReplacement()
        {
            var system = architecture.GetSystem<ArcadeRunSystem>();
            Assert.That(system.TryStartNewRun(null, out _, out var error), Is.True, error);
            Invoke("OnInit");
            var original = Object.FindObjectOfType<ArcadeResumeUI>(true);
            Assert.That(original, Is.Not.Null);
            Object.DestroyImmediate(original.gameObject);
            Invoke("RefreshSaveButtons");
            var replacement = Object.FindObjectOfType<ArcadeResumeUI>(true);
            Assert.That(replacement, Is.Not.Null);
            Assert.That(replacement, Is.Not.SameAs(original));
            Assert.That(replacement.gameObject.activeSelf, Is.False);
            Invoke("OnContinueClick");
            replacement.BtnClose.onClick.Invoke();
            Assert.That(UIHelper.GetUICtrl<ArcadeResumeUICtrl>().IsShow(), Is.False);
        }

        [Test]
        public void MenuInit_WithoutSaveDoesNotCreateResumeWindow()
        {
            Invoke("OnInit");
            Assert.That(UIHelper.GetUICtrl<ArcadeResumeUICtrl>(), Is.Null);
            Assert.That(architecture.GetModel<ArcadeRunModel>().HasRun, Is.False);
        }

        [Test]
        public void MenuRunEvent_RefreshesPreparedResultWithoutOpeningWindow()
        {
            var system = architecture.GetSystem<ArcadeRunSystem>();
            Assert.That(system.TryStartNewRun(null, out var snapshot, out var error), Is.True, error);
            Invoke("OnInit");
            snapshot.Score = 97531;
            snapshot.Outcome = "failed";
            Assert.That(new ArcadeRunFileStore(saveRoot).Save(snapshot, out error), Is.True, error);
            architecture.SendEvent(new ArcadeRunChangedEvent(ArcadeRunPhase.Result, snapshot));
            var view = Object.FindObjectOfType<ArcadeResumeUI>(true);
            Assert.That(view, Is.Not.Null);
            Assert.That(view.TextRewards.text, Does.Contain("97531"));
            Assert.That(view.BtnSettle.gameObject.activeSelf, Is.True);
            Assert.That(view.BtnContinue.gameObject.activeSelf, Is.False);
            Assert.That(view.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void SaveInspection_UnchangedFileReusesDetachedValidatedSnapshot()
        {
            var system = architecture.GetSystem<ArcadeRunSystem>();
            Assert.That(system.TryStartNewRun(null, out _, out var error), Is.True, error);
            var store = new ArcadeRunFileStore(saveRoot);
            var inspected = store.Inspect();
            Assert.That(inspected.IsValid, Is.True);
            inspected.Data.Score = 99999;
            inspected.Data.EmergencyResources[ArcadeRunDefaults.BombResourceId] = 99;
            using (new FileStream(store.RunPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var cached = store.Inspect();
                Assert.That(cached.IsValid, Is.True, "未变化的存档不应重复打开内容句柄");
                Assert.That(cached.Data.Score, Is.Zero, "调用者不能修改缓存快照");
                Assert.That(cached.Data.EmergencyResources.ContainsKey(ArcadeRunDefaults.BombResourceId), Is.False);
            }
        }

        [Test]
        public void SaveInspection_WriteDeleteAndBackupChangesInvalidateCache()
        {
            var system = architecture.GetSystem<ArcadeRunSystem>();
            Assert.That(system.TryStartNewRun(null, out var snapshot, out var error), Is.True, error);
            var store = new ArcadeRunFileStore(saveRoot);
            Assert.That(store.Inspect().Data.Score, Is.Zero);
            snapshot.Score = 45678;
            Assert.That(store.Save(snapshot, out error), Is.True, error);
            Assert.That(store.Inspect().Data.Score, Is.EqualTo(45678));
            File.WriteAllText(store.RunPath, "broken");
            Assert.That(store.Inspect().FromBackup, Is.True);
            File.WriteAllText(store.BackupPath, "also broken");
            Assert.That(store.Inspect().Status, Is.EqualTo(SaveFileStatus.Corrupted));
            Assert.That(store.Delete(out error), Is.True, error);
            Assert.That(store.Inspect().Status, Is.EqualTo(SaveFileStatus.Missing));
            Assert.That(store.Save(snapshot, out error), Is.True, error);
            Assert.That(store.Inspect().Data.Score, Is.EqualTo(45678));
        }

        [Test]
        public void ContinueClick_AfterExternalSaveChangeShowsLatestResult()
        {
            var system = architecture.GetSystem<ArcadeRunSystem>();
            Assert.That(system.TryStartNewRun(null, out var snapshot, out var error), Is.True, error);
            Invoke("OnInit");
            snapshot.Score = 56789;
            snapshot.Outcome = "failed";
            Assert.That(new ArcadeRunFileStore(saveRoot).Save(snapshot, out error), Is.True, error);
            Invoke("OnContinueClick");
            var view = Object.FindObjectOfType<ArcadeResumeUI>();
            Assert.That(view, Is.Not.Null);
            Assert.That(view.TextRewards.text, Does.Contain("56789"));
            Assert.That(view.BtnSettle.gameObject.activeSelf, Is.True);
            Assert.That(view.BtnContinue.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void SaveInspection_TemporaryPrimaryLockDoesNotKeepOlderBackupCached()
        {
            var system = architecture.GetSystem<ArcadeRunSystem>();
            Assert.That(system.TryStartNewRun(null, out var snapshot, out var error), Is.True, error);
            var store = new ArcadeRunFileStore(saveRoot);
            snapshot.Score = 12345;
            Assert.That(store.Save(snapshot, out error), Is.True, error);
            using (new FileStream(store.RunPath, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.That(store.Inspect().FromBackup, Is.True);
            var restored = store.Inspect();
            Assert.That(restored.FromBackup, Is.False, "主文件恢复可读后不能一直返回较旧备份");
            Assert.That(restored.Data.Score, Is.EqualTo(12345));
        }

        [Test]
        public void MenuInit_PreparesColdCasualConfigurationBeforeContinueClick()
        {
            var system = architecture.GetSystem<ArcadeRunSystem>();
            Assert.That(system.TryStartNewRun(null, out var snapshot, out var error), Is.True, error);
            snapshot.IsCasualRoute = true;
            snapshot.ArenaConfigNumber = 1;
            Assert.That(new ArcadeRunFileStore(saveRoot).Save(snapshot, out error), Is.True, error);
            int tableLoads = 0;
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() =>
            {
                tableLoads++;
                return new global::Tables(name => new Luban.ByteBuf(
                    AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/Game/Config/{name}.bytes").bytes));
            }));
            Invoke("OnInit");
            Assert.That(tableLoads, Is.EqualTo(1), "普通路线的配置校验和只读目录也应在显示菜单前准备");
            Assert.That(architecture.GetModel<ArcadeRunModel>().Phase, Is.EqualTo(ArcadeRunPhase.SafeCheckpoint));
            Invoke("OnContinueClick");
            Assert.That(UIHelper.GetUICtrl<ArcadeResumeUICtrl>().IsShow(), Is.True);
            Assert.That(tableLoads, Is.EqualTo(1), "点击应复用正式提供者的配置快照");
        }

        [Test]
        public void HiddenPreparedView_DestroyCallbacksRemoveArchitectureSubscriptions()
        {
            var system = architecture.GetSystem<ArcadeRunSystem>();
            Assert.That(system.TryStartNewRun(null, out _, out var error), Is.True, error);
            Invoke("OnInit");
            var entry = UIHelper.GetUICtrl<ArcadeResumeUICtrl>();
            var view = Object.FindObjectOfType<ArcadeResumeUI>(true);
            Assert.That(EventCallbackCount<LanguageChangedEvent>(entry), Is.EqualTo(1));
            Assert.That(EventCallbackCount<ArcadeRunChangedEvent>(entry), Is.EqualTo(1));
            // EditMode 不自动执行普通运行时组件的全部生命周期，且隐藏对象不接收 SendMessage。
            // 与基础 UI 生命周期测试一致，显式驱动真实销毁回调，再检查订阅及重建后的数量。
            view.OnTriggerDestroyEvent.Trigger();
            typeof(UnRegisterOnDestroyTrigger).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(view.GetComponent<UnRegisterOnDestroyTrigger>(), null);
            Object.DestroyImmediate(view.gameObject);
            Assert.That(EventCallbackCount<LanguageChangedEvent>(entry), Is.Zero, "未显示就销毁也必须注销架构订阅");
            Assert.That(EventCallbackCount<ArcadeRunChangedEvent>(entry), Is.Zero);
            Invoke("RefreshSaveButtons");
            Assert.That(EventCallbackCount<LanguageChangedEvent>(entry), Is.EqualTo(1));
            Assert.That(EventCallbackCount<ArcadeRunChangedEvent>(entry), Is.EqualTo(1));
        }

        private int EventCallbackCount<T>(object target)
        {
            var eventSystem = typeof(Architecture<Game>).GetField("mTypeEventSystem", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(architecture);
            var events = (EasyEvents)typeof(TypeEventSystem).GetField("mEvents", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(eventSystem);
            var callbacks = (Delegate)typeof(EasyEvent<T>).GetField("mOnEvent", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(events.GetEvent<EasyEvent<T>>());
            int count = 0;
            foreach (var callback in callbacks.GetInvocationList()) if (ReferenceEquals(callback.Target, target)) count++;
            return count;
        }

        private void Invoke(string method) => typeof(StartUICtrl).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);

        private sealed class DeferredPreparation : IGameplayResourcePreparation
        {
            public readonly List<UniTaskCompletionSource<bool>> Requests = new List<UniTaskCompletionSource<bool>>();
            public readonly List<Func<bool>> Owners = new List<Func<bool>>();
            public readonly List<string> MapIds = new List<string>();
            public UniTask<bool> PrepareAsync(string mapId, Func<bool> isCurrent = null, CancellationToken cancellationToken = default)
            {
                var request = new UniTaskCompletionSource<bool>();
                Requests.Add(request); Owners.Add(isCurrent); MapIds.Add(mapId);
                return request.Task;
            }
            public void CompleteAll() { foreach (var request in Requests) request.TrySetResult(false); }
        }

        private sealed class LocalizationLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Config/Localization/" + path);
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> loaded) where T : Object
            { loaded?.Invoke(LoadAsset<T>(path)); return ResourceLoadHandle.Empty; }
            public void Release(ResourceLoadHandle handle) { }
        }
    }
}
