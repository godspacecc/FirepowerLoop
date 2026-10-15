using System;
using System.IO;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class StartPauseUnifiedFlowTests
    {
        private ActiveWorldPlayerPortProvider playerPorts;
        private FakeMapContentLoader mapLoader;
        private string runtimeRoot;

        [SetUp]
        public void SetUp()
        {
            runtimeRoot = Path.Combine(Path.GetTempPath(), "AnotherDayStartPause", Guid.NewGuid().ToString("N"));
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            ResourceLoader.SetLoaderForTests(new LocalizationAssetResourceLoader());
            TemporaryBuildingBehaviorTestSupport.InstallArchitecture(
                new TestRuntimeConfigProvider(includeMapPlayerSpawn: true),
                new FakeTemporaryBuildingViewPort(), new FakeProjectilePort());
            var architecture = (Game)Game.Interface;
            architecture.RegisterModel(new ExpeditionTaskModel());
            architecture.RegisterSystem(new ExpeditionTaskSystem());
            architecture.RegisterSystem(new ExpeditionSystem());
            // 正式返回事务需要文明结算展示模型与装备保存/风险清空职责。
            architecture.RegisterModel(new CivilisationTechnologyModel());
            architecture.RegisterSystem(new WeaponEquipmentSystem());
            architecture.RegisterSystem(new ExpeditionFlowSystem());
            playerPorts = new ActiveWorldPlayerPortProvider();
            ((Game)Game.Interface).RegisterUtility<IPlayerRuntimePortProvider>(playerPorts);
            mapLoader = new FakeMapContentLoader();
            TemporaryBuildingBehaviorTestSupport.RegisterMapSystem(mapLoader);
            new GameObject("Main Camera", typeof(Camera)).tag = "MainCamera";
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(runtimeRoot);
        }

        [TearDown]
        public void TearDown()
        {
            TemporaryBuildingBehaviorTestSupport.Reset();
            if (Directory.Exists(runtimeRoot)) Directory.Delete(runtimeRoot, true);
        }

        [Test]
        public void ApplicationStartsPausedWithoutMainMenuState()
        {
            var model = new AppStateModel();
            Assert.That(model.CurrentState.Value, Is.EqualTo(AppState.Paused));
            Assert.That(System.Enum.GetNames(typeof(AppState)), Does.Not.Contain("MainMenu"));
        }

        [Test]
        public void StartContinueRoute_ResumesOnlyACompleteCurrentWorld()
        {
            var map = Game.Interface.GetSystem<MapSceneSystem>();
            var loaded = map.LoadMapAsync(MapIds.Base).GetAwaiter().GetResult();
            Assert.That(loaded.Success, Is.True, loaded.Error);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var unit), Is.True);
            playerPorts.Current = new PlayerMapLocalPoseSnapshot(unit.UnitId, unit.MapInstanceId,
                Vector3.zero, 0f, true);

            var appState = Game.Interface.GetSystem<AppStateSystem>();
            Assert.That(appState.TryPause(), Is.True);
            Assert.That(StartUICtrl.TryResumeCurrentWorld(map, appState), Is.True);
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Playing));

            Assert.That(appState.TryPause(), Is.True);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Assert.That(StartUICtrl.TryResumeCurrentWorld(map, appState), Is.False, "只有 MapId 不是可恢复世界");
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
        }

        [Test]
        public void IndependentPauseResume_RestoresActiveWorldAndClosesOnlyPauseMenu()
        {
            var map = Game.Interface.GetSystem<MapSceneSystem>();
            var loaded = map.LoadMapAsync(MapIds.Base).GetAwaiter().GetResult();
            Assert.That(loaded.Success, Is.True, loaded.Error);
            Game.Interface.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var unit);
            playerPorts.Current = new PlayerMapLocalPoseSnapshot(unit.UnitId, unit.MapInstanceId, Vector3.zero, 0f, true);
            var root = new GameObject("PauseTestRoot", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            try
            {
                foreach (var layer in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
                    new GameObject(layer, typeof(RectTransform)).transform.SetParent(root.transform, false);
                new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule))
                    .transform.SetParent(root.transform, false);
                root.AddComponent<UIRoot>();
                UIHelper.SetPrefabLoaderForTests(path => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
                var app = Game.Interface.GetSystem<AppStateSystem>();
                Assert.That(app.TryPause(), Is.True);
                var ctrl = UIHelper.ShowUI<PauseUICtrl>();
                var view = UnityEngine.Object.FindObjectOfType<PauseUI>();
                Assert.That(view.BtnContinue.interactable, Is.True);
                view.BtnContinue.onClick.Invoke();
                Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Playing));
                Assert.That(ctrl.IsShow(), Is.False);
                Assert.That(map.CurrentMapId, Is.EqualTo(MapIds.Base));
                Assert.That(UIHelper.GetUICtrl<StartUICtrl>(), Is.Null);
            }
            finally
            {
                UIHelper.ResetForTests();
                UIRoot.ClearForTests();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BasePauseReturnMainMenu_PreservesWorldAndContinueResumesIt()
        {
            var architecture = (Game)Game.Interface;
            architecture.RegisterModel(new ResourceUpdateModel());
            architecture.RegisterSystem(new ResourceUpdateSystem());
            architecture.GetSystem<ResourceUpdateSystem>().InitializeStartupInfo(
                new Boot.BootResourceStartupInfo(Boot.BootResourceSource.BuiltinFallback, "", "", ""));
            var map = architecture.GetSystem<MapSceneSystem>();
            var loaded = map.LoadMapAsync(MapIds.Base).GetAwaiter().GetResult();
            Assert.That(loaded.Success, Is.True, loaded.Error);
            architecture.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var unit);
            playerPorts.Current = new PlayerMapLocalPoseSnapshot(unit.UnitId, unit.MapInstanceId, Vector3.zero, 0f, true);
            var root = new GameObject("ReturnMenuTestRoot", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            try
            {
                foreach (var layer in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
                    new GameObject(layer, typeof(RectTransform)).transform.SetParent(root.transform, false);
                new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule)).transform.SetParent(root.transform, false);
                root.AddComponent<UIRoot>();
                UIHelper.SetPrefabLoaderForTests(path => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
                var app = architecture.GetSystem<AppStateSystem>();
                Assert.That(app.TryPause(), Is.True);
                var pause = UIHelper.ShowUI<PauseUICtrl>();
                var view = UnityEngine.Object.FindObjectOfType<PauseUI>();
                var field = typeof(PauseUI).GetField("BtnMainMenu");
                Assert.That(field, Is.Not.Null, "暂停 View 应提供返回主菜单按钮");
                var button = (UnityEngine.UI.Button)field.GetValue(view);
                Assert.That(button, Is.Not.Null, "正式 Prefab 必须绑定按钮");
                Assert.That(button.gameObject.activeSelf, Is.True);
                button.onClick.Invoke();
                Assert.That(pause.IsShow(), Is.False);
                var start = UIHelper.GetUICtrl<StartUICtrl>();
                Assert.That(start.IsShow(), Is.True);
                Assert.That(architecture.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
                Assert.That(map.CurrentMapId, Is.EqualTo(MapIds.Base));
                Assert.That(map.HasRecoverableCurrentWorld(), Is.True);
                UnityEngine.Object.FindObjectOfType<StartUI>().BtnContinue.onClick.Invoke();
                Assert.That(start.IsShow(), Is.False);
                Assert.That(architecture.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Playing));
                architecture.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var resumed);
                Assert.That(resumed.UnitId, Is.EqualTo(unit.UnitId));
                Assert.That(resumed.MapInstanceId, Is.EqualTo(unit.MapInstanceId), "继续游戏不得重建基地实例");

                Assert.That(app.TryPause(), Is.True);
                architecture.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
                pause.Show();
                Assert.That(button.gameObject.activeSelf, Is.False, "远征不提供此入口");
                button.onClick.Invoke();
                Assert.That(pause.IsShow(), Is.True, "隐藏按钮的旧回调也不得打开主菜单");
                Assert.That(start.IsShow(), Is.False);
            }
            finally
            {
                UIHelper.ResetForTests();
                UIRoot.ClearForTests();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void StartupReturnDecision_SettlementFailureRequestsOnlySettlementRetryAndKeepsExpedition()
        {
            ((Game)Game.Interface).RegisterSystem(new ExpeditionFlowSystem());
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var baseData, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(baseData, out error), Is.True, error);
            var expedition = CreatePendingReturn(save, 0, out error);
            Assert.That(expedition, Is.Not.Null, error);
            Assert.That(save.SaveExpedition(expedition, out error), Is.True, error);

            var paths = new SaveFileStore(runtimeRoot);
            var baseEntryRequests = 0;
            var baseOpenedRequests = 0;
            var errorRetryRequests = 0;
            using (new FileStream(paths.BasePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                LogAssert.Expect(LogType.Error, new Regex(@"\[GameStart\] 结算待恢复副本失败：.*"));
                var result = GameStart.ExecutePendingReturnDecisionAsync(
                    _ => { baseEntryRequests++; return UniTask.FromResult(true); },
                    () => baseOpenedRequests++,
                    () => errorRetryRequests++).GetAwaiter().GetResult();
                Assert.That(result, Is.False);
            }

            Assert.That(save.InspectExpedition().IsValid, Is.True);
            Assert.That(Game.Interface.GetSystem<MapSceneSystem>().CurrentMapId, Is.Null);
            Assert.That(baseEntryRequests, Is.EqualTo(0));
            Assert.That(baseOpenedRequests, Is.EqualTo(0));
            Assert.That(errorRetryRequests, Is.EqualTo(1));
        }

        [Test]
        public void StartupReturnDecision_SuccessEntersBaseAndOpensRuntimeHuds()
        {
            RegisterStartupReturnSystems();
            var save = Game.Interface.GetSystem<SaveSystem>();
            var expedition = CreatePendingReturn(save, 2, out var error);
            Assert.That(save.SaveExpedition(expedition, out error), Is.True, error);
            var baseEntryRequests = 0;
            var baseOpenedRequests = 0;
            var errorRetryRequests = 0;

            var result = GameStart.ExecutePendingReturnDecisionAsync(
                async data =>
                {
                    baseEntryRequests++;
                    return (await StartUICtrl.EnterBaseRuntimeAsync(data)).Success;
                },
                () => baseOpenedRequests++,
                () => errorRetryRequests++).GetAwaiter().GetResult();

            Assert.That(result, Is.True);
            Assert.That(Game.Interface.GetSystem<MapSceneSystem>().CurrentMapId, Is.EqualTo(MapIds.Base));
            Assert.That(save.InspectExpedition().Status, Is.EqualTo(SaveFileStatus.Missing));
            Assert.That(baseEntryRequests, Is.EqualTo(1));
            Assert.That(baseOpenedRequests, Is.EqualTo(1));
            Assert.That(errorRetryRequests, Is.EqualTo(0));
        }

        [Test]
        public void StartupReturnDecision_BaseEntryFailureKeepsSettledBaseAndDoesNotRetrySettlement()
        {
            RegisterStartupReturnSystems();
            var save = Game.Interface.GetSystem<SaveSystem>();
            var expedition = CreatePendingReturn(save, 3, out var error);
            Assert.That(save.SaveExpedition(expedition, out error), Is.True, error);
            mapLoader.FailNextLoad = true;
            var baseEntryRequests = 0;
            var baseOpenedRequests = 0;
            var errorRetryRequests = 0;
            LogAssert.Expect(LogType.Error, "[GameStart] 结算后进入基地失败：受控基地进入未完成");

            var result = GameStart.ExecutePendingReturnDecisionAsync(
                async data =>
                {
                    baseEntryRequests++;
                    return (await StartUICtrl.EnterBaseRuntimeAsync(data)).Success;
                },
                () => baseOpenedRequests++,
                () => errorRetryRequests++).GetAwaiter().GetResult();

            Assert.That(result, Is.False);
            var settled = save.InspectBase();
            Assert.That(settled.IsValid, Is.True, settled.Error);
            Assert.That(settled.Data.SettledExpeditionIds, Is.EquivalentTo(new[] { expedition.ExpeditionId }));
            Assert.That(settled.Data.Sections["another-day.player-bag"]["slots"][0]["amount"].Value<int>(), Is.EqualTo(3));
            Assert.That(save.InspectExpedition().Status, Is.EqualTo(SaveFileStatus.Missing));
            Assert.That(baseEntryRequests, Is.EqualTo(1));
            Assert.That(baseOpenedRequests, Is.EqualTo(0));
            Assert.That(errorRetryRequests, Is.EqualTo(0));

            var retryBase = save.PrepareBase();
            Assert.That(retryBase.IsValid, Is.True, retryBase.Error);
            var retryResult = StartUICtrl.EnterBaseRuntimeAsync(retryBase.Data).GetAwaiter().GetResult();
            Assert.That(retryResult.Success, Is.True, retryResult.Error);
            Assert.That(Game.Interface.GetSystem<MapSceneSystem>().CurrentMapId, Is.EqualTo(MapIds.Base));
            var afterRetry = save.InspectBase().Data;
            Assert.That(afterRetry.SettledExpeditionIds, Is.EquivalentTo(new[] { expedition.ExpeditionId }));
            Assert.That(afterRetry.Sections["another-day.player-bag"]["slots"][0]["amount"].Value<int>(), Is.EqualTo(3));
        }

        [Test]
        public void CorruptedExpeditionBaseDecision_OnlyOpensBaseAfterControlledEntrySucceeds()
        {
            ((Game)Game.Interface).RegisterSystem(new GameSessionFlowSystem());
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out _, out var error), Is.True, error);
            var paths = new SaveFileStore(runtimeRoot);
            File.WriteAllText(paths.ExpeditionPath, "corrupt expedition");
            var baseOpenedRequests = 0;
            var errorRetryRequests = 0;
            LogAssert.Expect(LogType.Error, "[GameStart] 损坏副本进入基地失败：受控基地进入未完成");
            var failedResult = GameStart.ExecuteCorruptedExpeditionBaseDecisionAsync(
                _ => UniTask.FromResult(false),
                () => baseOpenedRequests++,
                () => errorRetryRequests++).GetAwaiter().GetResult();

            Assert.That(failedResult, Is.False);
            Assert.That(baseOpenedRequests, Is.EqualTo(0));
            Assert.That(errorRetryRequests, Is.EqualTo(1));
            Assert.That(File.ReadAllText(paths.ExpeditionPath), Is.EqualTo("corrupt expedition"));

            var successResult = GameStart.ExecuteCorruptedExpeditionBaseDecisionAsync(
                EnterBaseForTestAsync,
                () => baseOpenedRequests++,
                () => errorRetryRequests++).GetAwaiter().GetResult();

            Assert.That(successResult, Is.True);
            Assert.That(baseOpenedRequests, Is.EqualTo(1));
            Assert.That(errorRetryRequests, Is.EqualTo(1));
            Assert.That(File.ReadAllText(paths.ExpeditionPath), Is.EqualTo("corrupt expedition"));
            Assert.That(Game.Interface.GetSystem<MapSceneSystem>().CurrentMapId, Is.EqualTo(MapIds.Base));
        }

        [TestCase(MapIds.SurvivalSample, false)]
        [TestCase(MapIds.SurvivalSample, true)]
        [TestCase(MapIds.HeJiaVillage, false)]
        [TestCase(MapIds.HeJiaVillage, true)]
        public void PauseReturn_SettlesQualifiedOrDiscardsUnqualifiedBag(string mapId, bool qualified)
        {
            RegisterStartupReturnSystems();
            var save = Game.Interface.GetSystem<SaveSystem>();
            var warehouse = Game.Interface.GetModel<WarehouseModel>();
            warehouse.Restore(new JObject
            {
                ["capacity"] = InventoryDefaults.WarehouseCapacity,
                ["slots"] = new JArray { new JObject { ["index"] = 0, ["itemId"] = ItemIds.MetalParts, ["amount"] = 7 } }
            });
            var baseData = save.InspectBase().Data;
            Assert.That(save.CaptureBaseSections(baseData, out var baseError), Is.True, baseError);
            Assert.That(save.SaveBase(baseData, out baseError), Is.True, baseError);
            Assert.That(Game.Interface.GetSystem<MapSceneSystem>().LoadMapAsync(mapId).GetAwaiter().GetResult().Success, Is.True);
            var id = Guid.NewGuid().ToString("N");
            var task = new ExpeditionTaskDefinition("pause.survive", mapId, "存活", "task", TaskObjectiveType.Survive, 2f, 100);
            Assert.That(Game.Interface.GetSystem<ExpeditionSystem>().TryBeginConfiguredExpedition(mapId, id, new[] { task }, out var error), Is.True, error);
            if (qualified) Game.Interface.GetSystem<ExpeditionSystem>().AdvanceGameMinutes(2f);
            var bag = Game.Interface.GetModel<PlayerBagModel>();
            bag.Restore(new JObject
            {
                ["capacity"] = InventoryDefaults.PlayerBagCapacity,
                ["riskState"] = PlayerBagRiskState.InExpedition.ToString(),
                ["slots"] = new JArray { new JObject { ["index"] = 0, ["itemId"] = ItemIds.MetalParts, ["amount"] = 3 } }
            });
            Assert.That(Game.Interface.GetSystem<AppStateSystem>().TryPause(), Is.True);
            var method = typeof(ExpeditionFlowSystem).GetMethod("ReturnFromPauseAsync");
            Assert.That(method, Is.Not.Null);
            var result = ((UniTask<ExpeditionFlowResult>)method.Invoke(Game.Interface.GetSystem<ExpeditionFlowSystem>(), new object[] { mapId, id, qualified })).GetAwaiter().GetResult();
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.EqualTo(MapIds.Base));
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Playing));
            Assert.That(bag.Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(qualified ? 3 : 0));
            Assert.That(warehouse.Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(7), "返程或放弃不改变基地仓库");
            Assert.That(save.InspectExpedition().Status, Is.EqualTo(SaveFileStatus.Missing));
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().IsActive, Is.False);
            var repeated = ((UniTask<ExpeditionFlowResult>)method.Invoke(Game.Interface.GetSystem<ExpeditionFlowSystem>(), new object[] { mapId, id, qualified })).GetAwaiter().GetResult();
            Assert.That(repeated.Success, Is.False, "重复确认不得重复结算");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PauseReturn_RejectsStaleConfirmationWithoutChangingExpedition(bool changedQualification)
        {
            RegisterStartupReturnSystems();
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            var expedition = Game.Interface.GetModel<ExpeditionModel>();
            expedition.BeginSurvival(MapIds.SurvivalSample, 2f);
            expedition.SetExpeditionId("abcdef0123456789abcdef0123456789");
            var method = typeof(ExpeditionFlowSystem).GetMethod("ReturnFromPauseAsync");
            Assert.That(method, Is.Not.Null);
            if (changedQualification) expedition.QualifyReturn();
            var expectedStatus = expedition.Status;
            var id = changedQualification ? expedition.CurrentExpeditionId : "0123456789abcdef0123456789abcdef";
            var result = ((UniTask<ExpeditionFlowResult>)method.Invoke(Game.Interface.GetSystem<ExpeditionFlowSystem>(), new object[] { MapIds.SurvivalSample, id, false })).GetAwaiter().GetResult();
            Assert.That(result.Success, Is.False);
            Assert.That(expedition.Status, Is.EqualTo(expectedStatus));
        }

        private static async UniTask<bool> EnterBaseForTestAsync(BaseSaveData data)
        {
            return (await StartUICtrl.EnterBaseRuntimeAsync(data)).Success;
        }

        private void RegisterStartupReturnSystems()
        {
            var architecture = (Game)Game.Interface;
            architecture.RegisterSystem(new GameSessionFlowSystem());
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var baseData, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(baseData, out error), Is.True, error);
        }

        private static ExpeditionSaveData CreatePendingReturn(SaveSystem save, int amount, out string error)
        {
            var expeditionId = Guid.NewGuid().ToString("N");
            var definition = new ExpeditionTaskDefinition(
                "expedition.survival-sample.survive",
                MapIds.SurvivalSample,
                "存活一天",
                "task.expedition.survival_sample.survive.name",
                TaskObjectiveType.Survive,
                1f,
                100);
            if (!Game.Interface.GetSystem<ExpeditionSystem>().TryBeginConfiguredExpedition(
                    MapIds.SurvivalSample, expeditionId, new[] { definition }, out error)) return null;
            Game.Interface.GetSystem<ExpeditionSystem>().AdvanceGameMinutes(1f);
            var expedition = Game.Interface.GetModel<ExpeditionModel>().CaptureSave(expeditionId, DateTime.UtcNow);
            if (!save.CaptureExpeditionSections(expedition, out error)) return null;
            expedition.Sections["another-day.player-bag"] = new JObject
            {
                ["capacity"] = InventoryDefaults.PlayerBagCapacity,
                ["riskState"] = PlayerBagRiskState.InExpedition.ToString(),
                ["slots"] = amount > 0
                    ? new JArray { new JObject { ["index"] = 0, ["itemId"] = ItemIds.MetalParts, ["amount"] = amount } }
                    : new JArray()
            };
            return expedition;
        }

        private sealed class ActiveWorldPlayerPortProvider : IPlayerRuntimePortProvider
        {
            public PlayerMapLocalPoseSnapshot Current;
            public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort port) { port = null; return false; }
            public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort port) { port = null; return false; }
            public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider) => false;
            public bool TryGetCurrentPlayerMapLocalPose(out PlayerMapLocalPoseSnapshot snapshot, out string error)
            {
                snapshot = Current;
                error = snapshot.IsActive ? null : "test player inactive";
                return snapshot.IsActive;
            }
        }
    }
}
