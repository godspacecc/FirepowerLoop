using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class PauseUiFlowTests
    {
        private GameObject closablePrefab;
        private GameObject poolRoot;
        private string leaveTestRoot;

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
                if (path == "Prefabs/UI/Common/Loading") return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Common/Loading.prefab");
                if (path == "Prefabs/UI/Arcade/ArcadeResume") return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeResume.prefab");
                return path == "Prefabs/UI/Home/Start"
                    ? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab") : null;
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
            if (leaveTestRoot != null && System.IO.Directory.Exists(leaveTestRoot))
                System.IO.Directory.Delete(leaveTestRoot, true);
            leaveTestRoot = null;
        }

        [Test]
        public void MapPreparationFailure_BackKeepsFailedSceneCoveredUntilSafeNodeIsRestored()
        {
            InstallArcadeLeaveDependencies();
            var runs = Game.Interface.GetSystem<ArcadeRunSystem>();
            Assert.That(runs.TryStartNewRun(null, out var saved, out var error), Is.True, error);
            var runModel = Game.Interface.GetModel<ArcadeRunModel>();
            runModel.EnterArena();
            runModel.SetTransientLives(1);
            var combat = new ArcadeCombatModel();
            Game.Interface.RegisterModel(combat);
            combat.Begin(new ArcadeCombatRuntime { IslandTransitionPending = true, CompletionAwaitingRetry = true });
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Arcade, null, MapPreparationMode.ArcadePlayerOnly);
            var bytes = System.IO.File.ReadAllBytes(new ArcadeRunFileStore(leaveTestRoot).RunPath);
            var loading = UIHelper.ShowUI<LoadingUICtrl>();
            Game.Interface.SendEvent(new ArcadeCombatHudChangedEvent(new ArcadeCombatHudSnapshot
            { IslandTransitioning = true, CanRetryCompletion = true, IslandPreparationError = "resource failure" }));
            var view = GetBaseUI<LoadingUI>(loading);
            var flow = Game.Interface.GetSystem<ArcadeSceneFlowSystem>();
            var busy = typeof(ArcadeSceneFlowSystem).GetField("busy", BindingFlags.Instance | BindingFlags.NonPublic);
            busy.SetValue(flow, true);
            view.BtnBack.onClick.Invoke();
            Assert.That(loading.IsShow(), Is.True, "返回失败不能露出未完成战斗");
            Assert.That(view.ErrorRoot.activeSelf, Is.True);
            Assert.That(view.BtnBack.interactable, Is.True);
            Assert.That(runModel.Phase, Is.EqualTo(ArcadeRunPhase.Arena));
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.EqualTo(MapIds.Arcade));
            busy.SetValue(flow, false);
            view.BtnBack.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.Null);
            Assert.That(runModel.Phase, Is.EqualTo(ArcadeRunPhase.SafeCheckpoint));
            Assert.That(runModel.Current.RunId, Is.EqualTo(saved.RunId));
            Assert.That(runModel.Current.RemainingLives, Is.EqualTo(saved.RemainingLives));
            Assert.That(System.IO.File.ReadAllBytes(new ArcadeRunFileStore(leaveTestRoot).RunPath), Is.EqualTo(bytes));
            Assert.That(loading.IsShow(), Is.False);
            Assert.That(UIHelper.GetUICtrl<StartUICtrl>()?.IsShow(), Is.True);
        }

        [UnityTest]
        public IEnumerator MapPreparationReturn_RetriesSafeNodeRecoveryAfterActualMapRelease()
        {
            InstallArcadeLeaveDependencies();
            var runs = Game.Interface.GetSystem<ArcadeRunSystem>();
            Assert.That(runs.TryStartNewRun(null, out _, out var error), Is.True, error);
            var file = new ArcadeRunFileStore(leaveTestRoot).RunPath;
            var bytes = System.IO.File.ReadAllBytes(file);
            Game.Interface.GetModel<ArcadeRunModel>().EnterArena();
            var combat = new ArcadeCombatModel();
            Game.Interface.RegisterModel(combat);
            combat.Begin(new ArcadeCombatRuntime { IslandTransitionPending = true, CompletionAwaitingRetry = true });
            Game.Interface.RegisterModel(new UnitModel());
            Game.Interface.RegisterSystem(new UnitSystem());
            Game.Interface.RegisterSystem(new AudioSystem());
            var loader = new DeferredMapRelease();
            Game.Interface.RegisterUtility<IMapContentLoader>(loader);
            using (var handle = new PreparationMapHandle())
            {
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Arcade, handle, MapPreparationMode.ArcadePlayerOnly);
                var loading = UIHelper.ShowUI<LoadingUICtrl>();
                Game.Interface.SendEvent(new ArcadeCombatHudChangedEvent(new ArcadeCombatHudSnapshot
                { IslandTransitioning = true, CanRetryCompletion = true, IslandPreparationError = "resource failure" }));
                var view = GetBaseUI<LoadingUI>(loading);
                view.BtnBack.onClick.Invoke();
                Assert.That(view.BtnBack.interactable, Is.False);
                Assert.That(loading.IsShow(), Is.True);
                // 地图已开始释放后模拟安全节点文件损坏，不能用无句柄快速路径掩盖释放事件的顺序。
                System.IO.File.WriteAllText(file, "broken save");
                loader.Completion.TrySetResult(MapContentUnloadResult.Ok());
                var startFrame = Time.frameCount;
                var recoveryDeadline = EditorApplication.timeSinceStartup + 2.0;
                while (!view.BtnBack.interactable && EditorApplication.timeSinceStartup < recoveryDeadline)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    yield return null;
                }
                Assert.That(view.BtnBack.interactable, Is.True,
                    "首次返回事务必须已完成，才可恢复文件并重试；frame=" + startFrame + "→" + Time.frameCount);
                Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.Null);
                Assert.That(Game.Interface.GetModel<ArcadeRunModel>().Phase, Is.EqualTo(ArcadeRunPhase.Arena));
                Assert.That(loading.IsShow(), Is.True);
                Assert.That(view.ErrorRoot.activeSelf, Is.True);
                Assert.That(view.BtnRetry.gameObject.activeInHierarchy, Is.True, "释放后恢复失败仍须保留可重试入口");
                loading.Hide();
                Assert.That(loading.IsShow(), Is.True, "恢复失败不能仅关闭遮罩");
                System.IO.File.WriteAllBytes(file, bytes);
                Assert.That(runs.InspectPending().IsValid, Is.True, "重试前必须已恢复真实有效的安全节点文件");
                view.BtnRetry.onClick.Invoke();
                // 正式返回是异步入口，等待事务结束再判定，保留原安全节点目标。
                for (int frame = 0; frame < 10 && Game.Interface.GetModel<ArcadeRunModel>().Phase != ArcadeRunPhase.SafeCheckpoint; frame++)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    yield return null;
                }
                Assert.That(Game.Interface.GetModel<ArcadeRunModel>().Phase, Is.EqualTo(ArcadeRunPhase.SafeCheckpoint),
                    "恢复有效文件后应回到原安全节点：" + view.ErrorText.text + " / " +
                    typeof(LoadingUICtrl).GetField("mapFailureReason", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(loading) +
                    " / active=" + typeof(LoadingUICtrl).GetField("arcadePreparationActive", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(loading) +
                    " / failed=" + typeof(LoadingUICtrl).GetField("arcadePreparationFailed", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(loading));
                Assert.That(loading.IsShow(), Is.False);
            }
        }

        private sealed class DeferredMapRelease : IMapContentLoader
        {
            public readonly UniTaskCompletionSource<MapContentUnloadResult> Completion = new();
            public UniTask<MapContentLoadResult> LoadAsync(string mapId, string address, Action<float> onProgress,
                CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public UniTask<MapContentUnloadResult> UnloadAsync(IMapContentHandle handle,
                CancellationToken cancellationToken = default) => Completion.Task;
        }

        private sealed class PreparationMapHandle : IMapContentHandle, IDisposable
        {
            private readonly GameObject root = new("PreparationMap");
            public PreparationMapHandle() => Context = root.AddComponent<MapContext>();
            public string MapId => MapIds.Arcade;
            public MapContext Context { get; }
            public bool IsValid => root != null;
            public void Dispose() { if (root != null) Object.DestroyImmediate(root); }
        }

        [Test]
        public void ArcadePause_ReopensWithLeaveActionAndRefreshesLanguage()
        {
            var map = Game.Interface.GetModel<MapSceneModel>();
            map.SetLoaded(MapIds.Arcade, null, MapPreparationMode.ArcadePlayerOnly);
            var ctrl = UIHelper.ShowUI<PauseUICtrl>();
            var text = GetBaseUI<PauseUI>(ctrl).BtnOnline.GetComponentInChildren<TMP_Text>();
            Assert.That(text.text, Is.EqualTo("返回主菜单"));
            Game.Interface.GetModel<LocalizationModel>().CurrentLanguageCode.Value = "en-US";
            Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN", "en-US"));
            Assert.That(text.text, Is.EqualTo("Return to Main Menu"));
            ctrl.Hide();
            map.SetLoaded(MapIds.Arcade, null);
            ctrl.Show();
            Assert.That(text.text, Is.EqualTo("Return to Main Menu"));
            Assert.That(typeof(PauseUI).GetField("BtnMainMenu"), Is.Null);
            Assert.That(GetBaseUI<PauseUI>(ctrl).GetComponentsInChildren<Transform>(true).Any(t => t.name == "BtnReturnCamp"), Is.False);
        }

        [Test]
        public void ArcadeLeave_WithoutActiveHandleReturnsToMainMenuAndClosesPause()
        {
            InstallArcadeLeaveDependencies();
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Arcade, null);
            var ctrl = UIHelper.ShowUI<PauseUICtrl>();
            GetBaseUI<PauseUI>(ctrl).BtnOnline.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.Null);
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
            Assert.That(UIHelper.GetUICtrl<StartUICtrl>()?.IsShow(), Is.True);
            Assert.That(ctrl.IsShow(), Is.False, "成功返回主菜单后不能留下可再次点击的旧暂停界面");
            Assert.That(UIHelper.GetUICtrl<OnlineLoginUICtrl>()?.IsShow(), Is.Not.True);
        }

        [Test]
        public void ArcadeLeave_WhenSceneFlowBusyKeepsPauseAndAllowsRetry()
        {
            InstallArcadeLeaveDependencies();
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Arcade, null);
            var flow = Game.Interface.GetSystem<ArcadeSceneFlowSystem>();
            // 仅在测试中模拟另一个场景请求占用事务；失败和重试仍走正式按钮及领域入口。
            var busy = typeof(ArcadeSceneFlowSystem).GetField("busy", BindingFlags.Instance | BindingFlags.NonPublic);
            busy.SetValue(flow, true);
            var ctrl = UIHelper.ShowUI<PauseUICtrl>();
            var view = GetBaseUI<PauseUI>(ctrl);
            view.BtnOnline.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.EqualTo(MapIds.Arcade));
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(view.BtnOnline.interactable, Is.True);
            Assert.That(UIHelper.GetUICtrl<StartUICtrl>()?.IsShow(), Is.Not.True);
            Assert.That(UIHelper.GetUICtrl<OnlineLoginUICtrl>()?.IsShow(), Is.Not.True);
            busy.SetValue(flow, false);
            view.BtnOnline.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.Null);
            Assert.That(UIHelper.GetUICtrl<StartUICtrl>()?.IsShow(), Is.True);
            Assert.That(ctrl.IsShow(), Is.False);
        }

        /// <summary>离开测试使用真实可回滚控制器和隔离存档，不能用null依赖掩盖事务或读取正式档。</summary>
        private void InstallArcadeLeaveDependencies()
        {
            poolRoot = new GameObject(SceneRoot.PoolRootName);
            leaveTestRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FirepowerLoopPauseTests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(leaveTestRoot);
            Game.Interface.RegisterModel(new ArcadeRunModel());
            var run = new ArcadeRunSystem();
            run.ConfigureForTests(leaveTestRoot);
            Game.Interface.RegisterSystem(run);
            Game.Interface.RegisterSystem(new RedPointSystem());
            Game.Interface.RegisterSystem(new MapSceneSystem(new ProjectileCtrl(Game.Interface),
                new PlayerCtrl(Game.Interface), new EnemyCtrl(Game.Interface), new LootCtrl(Game.Interface)));
            Game.Interface.RegisterSystem(new ArcadeSceneFlowSystem());
        }

        [Test]
        public void IslandRoundLeaveWarnsAboutWholeRoundRollbackAndCancelKeepsRun()
        {
            var localization = Game.Interface.GetUtility<LocalizationUtility>();
            localization.InitializeConfiguredTables();
            localization.LoadFromJson("zh-CN",
                System.IO.File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
            var run = new ArcadeRunModel(); Game.Interface.RegisterModel(run);
            run.CommitSafeNode(new ArcadeRunSaveData { RunId = Guid.NewGuid().ToString("N"), IsCasualRoute = true,
                ArenaConfigNumber = 1, IslandRoundCheckpoint = new ArcadeIslandRoundCheckpoint {
                    RoundNumber = 3, MapId = "island.round.3", PlayerHealth = 73, PlayerMaxHealth = 100 } });
            run.EnterArena();
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Arcade, null, MapPreparationMode.ArcadePlayerOnly);
            var pause = UIHelper.ShowUI<PauseUICtrl>();
            GetBaseUI<PauseUI>(pause).BtnOnline.onClick.Invoke();
            var message = UIHelper.GetUICtrl<MessageUICtrl>();
            Assert.That(message.IsShow(), Is.True);
            string content = GetBaseUI<MessageUI>(message).ContentText.text;
            Assert.That(content, Does.Contain("第3轮").And.Contain("沉船浅滩").And.Contain("整体回滚"));
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.EqualTo(MapIds.Arcade));
            var config = (MessageConfig)typeof(MessageUICtrl).GetField("currentConfig", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(message);
            typeof(MessageUICtrl).GetMethod("OnMessageButtonClick", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(message, new object[] { new object[] { config.Buttons.Single(button => button.Role == MessageButtonRole.Cancel) } });
            Assert.That(message.IsShow(), Is.False); Assert.That(pause.IsShow(), Is.True);
            Assert.That(run.Phase, Is.EqualTo(ArcadeRunPhase.Arena));
            Assert.That(run.Current.IslandRoundCheckpoint.RoundNumber, Is.EqualTo(3));
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
        public void PauseLayout_KeepsArcadeActionsCompactAcrossReopen()
        {
            var map = Game.Interface.GetModel<MapSceneModel>();
            map.SetLoaded(MapIds.Arcade, null);
            var ctrl = UIHelper.ShowUI<PauseUICtrl>();
            var view = GetBaseUI<PauseUI>(ctrl);
            var panel = view.PausePanel;
            var group = (RectTransform)panel.Find("ButtonGroup");
            var height = panel.rect.height;
            var groupHeight = group.rect.height;
            Assert.That(typeof(PauseUI).GetField("BtnMainMenu"), Is.Null);
            Assert.That(view.GetComponentsInChildren<Transform>(true).Any(t => t.name == "BtnReturnCamp"), Is.False);
            Assert.That(group.GetComponentsInChildren<Button>().Select(button => button.name),
                Is.EquivalentTo(new[] { "BtnContinue", "BtnOnline", "BtnSetting", "BtnExit" }));
            Assert.That(ButtonBottomMargin(panel, view.BtnExit), Is.EqualTo(36f).Within(0.1f));
            Assert.That(height, Is.GreaterThan(groupHeight));
            Assert.That(panel.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(panel.rect.width, Is.EqualTo(560f).Within(0.1f));

            // 隐藏一个现役按钮时，布局应移除按钮高度及间距，不留下旧菜单空位。
            view.BtnSetting.gameObject.SetActive(false);
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            Assert.That(height - panel.rect.height, Is.EqualTo(84f).Within(0.1f));
            Assert.That(ButtonBottomMargin(panel, view.BtnExit), Is.EqualTo(36f).Within(0.1f));
            view.BtnSetting.gameObject.SetActive(true);
            ctrl.Hide();
            map.SetLoaded(MapIds.Arcade, null, MapPreparationMode.ArcadePlayerOnly);
            ctrl.Show();
            Assert.That(panel.rect.height, Is.EqualTo(height).Within(0.1f));
            Assert.That(group.rect.height, Is.EqualTo(groupHeight).Within(0.1f));
            Assert.That(typeof(PauseUI).GetField("BtnMainMenu"), Is.Null);
            Assert.That(view.GetComponentsInChildren<Transform>(true).Any(t => t.name == "BtnReturnCamp"), Is.False);
        }

        private static float ButtonBottomMargin(RectTransform panel, Button button)
        {
            var corners = new Vector3[4];
            ((RectTransform)button.transform).GetWorldCorners(corners);
            return panel.InverseTransformPoint(corners[0]).y - panel.rect.yMin;
        }

        [Test]
        public void PausePrefab_HasAdaptivePanelCurrentButtonsAndNoRetiredCampEntry()
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
                Is.EquivalentTo(new[] { "BtnContinue", "BtnOnline", "BtnSetting", "BtnExit" }));
            Assert.That(prefab.GetComponentsInChildren<RawImage>(true), Is.Empty);
            var view = prefab.GetComponent<BaseUI>();
            Assert.That(view, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name == "BtnReturnCamp"), Is.False);
            Assert.That(prefab.GetComponent<ReferenceCollector>().data.Any(item => item.key == "BtnReturnCamp"), Is.False);
            Assert.That(view.GetType().GetField("BtnReturnCamp"), Is.Null);
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
        public void EscapeClosesTopLayerBeforeLaterDialog()
        {
            var save = RegisterShown(new TestCmdUICtrl(), CreateBaseUI<TestCmdUI>("TestCmd"));
            var log = RegisterShown(new RuntimeLogUICtrl(), CreateBaseUI<RuntimeLogUI>("RuntimeLog"));
            var hotKeys = Game.Interface.GetSystem<HotKeySystem>();

            Assert.IsTrue(hotKeys.ExecuteBinding(HotKeyDefaults.Escape));
            // Top 工具窗口先于后显示的 Dialog 关闭；第二次 Escape 才关闭日志。
            Assert.IsFalse(save.IsShow());
            Assert.IsTrue(log.IsShow());

            Assert.IsTrue(hotKeys.ExecuteBinding(HotKeyDefaults.Escape));
            Assert.IsFalse(log.IsShow());
        }

        [Test]
        public void NonWhitelistedBlockingUiShieldsUnderlyingWindowAndPause()
        {
            EnterPlaying();
            var save = RegisterShown(new RuntimeLogUICtrl(), CreateBaseUI<RuntimeLogUI>("RuntimeLog"));
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

            Assert.IsTrue(hotKeys.BeginRebind(HotKeyActionIds.UseFieldUpgrade).Success);
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
            Assert.That(bindings.Any(item => item.BindingPath == "<Keyboard>/tab" || item.ActionId == "inventory.toggle"), Is.False);
            Assert.That(bindings.Single(item => item.BindingPath == "<Keyboard>/f").ActionId,
                Is.EqualTo(HotKeyActionIds.UseFieldUpgrade));
            Assert.That(bindings.Single(item => item.BindingPath == "<Keyboard>/r").ActionId,
                Is.EqualTo(HotKeyActionIds.DonateLife));
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
                    "{\"ui.pause.abandon_confirm\":\"放弃将丢失背包和装备\",\"ui.pause.title\":\"暂停\",\"ui.pause.leave_arcade\":\"离开街机\",\"ui.start.online\":\"联机游戏\",\"ui.pause.resume\":\"继续\",\"ui.pause.main_menu\":\"返回主菜单\",\"ui.start.settings\":\"设置\",\"ui.start.exit\":\"退出\"}"),
                ["en-US.json"] = new TextAsset(
                    "{\"ui.pause.title\":\"Paused\",\"ui.pause.leave_arcade\":\"Leave Arcade\",\"ui.start.online\":\"Online Game\",\"ui.pause.resume\":\"Resume\",\"ui.pause.main_menu\":\"Return to Main Menu\",\"ui.start.settings\":\"Settings\",\"ui.start.exit\":\"Exit\"}")
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
