using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using ET;
using Game.Network;
using Luban;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class MapSelectionUiTests
    {
        private static readonly string[] RequiredLocalizationKeys =
        {
            "npc.role.map_guide", "ui.map_selection.title", "ui.map_selection.type.survival",
            "ui.map_selection.type.task", "ui.map_selection.target.survive_minutes",
            "ui.map_selection.target.kills", "ui.map_selection.state.unlocked",
            "ui.map_selection.state.unlock_cost", "ui.map_selection.action.enter",
            "ui.map_selection.action.unlock", "ui.map_selection.action.processing",
            "ui.map_selection.error.open", "ui.map_selection.error.unknown_map",
            "ui.map_selection.error.not_expedition", "ui.map_selection.error.cost",
            "ui.map_selection.error.enter"
            ,"task.expedition.survival_sample.survive.name", "task.expedition.mission_sample.kill.name",
            "ui.map_selection.objective.survive", "ui.map_selection.objective.kill",
            "ui.map_selection.task_config_unavailable", "ui.map_selection.state.unavailable",
            "ui.map_selection.action.unavailable", "ui.map_selection.error.task_config_unavailable",
            "ui.map_selection.recovery.title", "ui.map_selection.recovery.content"
        };

        private ControlledMapContentLoader mapLoader;
        private MapSelectionNetwork network;
        private SpawnlessConfigProxy configProxy;
        private string tempSaveRoot;
        private ReadyGameplayResources resources;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            UIHelper.ResetForTests();
            mapLoader = new ControlledMapContentLoader();
            network = new MapSelectionNetwork();
            resources = new ReadyGameplayResources();
            ResourceLoader.SetLoaderForTests(new AssetDatabaseResourceLoader());
            var config = CreateSpawnlessConfigProvider(out configProxy);
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(config);
                architecture.RegisterSystem<OptionalGameplayResourceSystem>(resources);
                architecture.RegisterUtility<IMapContentLoader>(mapLoader);
                architecture.RegisterUtility<INetworkRuntime>(network);
                architecture.RegisterUtility<INetworkConfigProvider>(new MapSelectionNetworkConfig());
                architecture.RegisterUtility<IOnlineAccountPreferenceStore>(new MapSelectionPreferenceStore());
            };
            CreateUIRoot();
            UIHelper.SetPrefabLoaderForTests(LoadUiPrefab);
            tempSaveRoot = Path.Combine(Path.GetTempPath(), "AnotherDayMapSelectionUiTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempSaveRoot);
            var architecture = Game.Interface;
            architecture.GetSystem<SaveSystem>().ConfigureForTests(tempSaveRoot);
            var localization = architecture.GetUtility<LocalizationUtility>();
            localization.LoadFromJson("zh-CN", File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
            localization.LoadFromJson("en-US", File.ReadAllText("Assets/Game/Config/Localization/en-US.json"));
            architecture.GetModel<LocalizationModel>().SetLanguageCode("zh-CN");
            PrepareStableOfflineBase();
        }

        [TearDown]
        public void TearDown()
        {
            UIHelper.ResetForTests();
            ResourceLoader.ResetForTests();
            mapLoader?.Cleanup();
            TestReset.ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = _ => { };
            DestroyTransientSceneObjects();
            if (!string.IsNullOrEmpty(tempSaveRoot) && Directory.Exists(tempSaveRoot))
                Directory.Delete(tempSaveRoot, true);
            tempSaveRoot = null;
            mapLoader = null;
        }

        [Test]
        public void MapRowsUseStableCatalogOrder()
        {
            var maps = MapCatalog.GetExpeditionMaps();
            Assert.That(maps.Select(item => item.MapId), Is.EqualTo(new[]
            {
                MapIds.SurvivalSample, MapIds.MissionSample, MapIds.HeJiaVillage, MapIds.CivilisationValley
            }));
            Assert.That(maps.Select(item => item.DisplayOrder), Is.EqualTo(new[] { 1, 2, 3, 4 }));
        }

        [UnityTest]
        public IEnumerator CardSelectionDoesNotStartMapLoad()
        {
            mapLoader.Mode = ControlledLoadMode.Hold;
            var ui = OpenDialog();
            GetRows(ui)[0].BtnSelect.onClick.Invoke();
            yield return null;
            Assert.That(mapLoader.LoadRequests, Is.Empty, "卡片只切换详情，主按钮才提交出发意图");
        }

        [UnityTest]
        public IEnumerator DownloadCompletesWithoutEnteringAndFooterSwitchesToExplore()
        {
            resources.NeedsDownload = true;
            var ui = OpenDialog();
            GetRows(ui)[0].BtnSelect.onClick.Invoke();
            var entry = ui.GetComponent<ReferenceCollector>().data.SingleOrDefault(item => item.key == "BtnAction");
            Assert.That(entry, Is.Not.Null, "统一主按钮缺失");
            ((Button)entry.gameObject).onClick.Invoke();
            yield return null;
            Assert.That(mapLoader.LoadRequests, Is.Empty);
            Assert.That(UIHelper.GetUICtrl<MapSelectionUICtrl>().IsShow(), Is.True);
            Assert.That(ui.GetComponentsInChildren<TextMeshProUGUI>().Any(text => text.text == Loc.T("ui.map_selection.action.explore")), Is.True);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().RiskState, Is.EqualTo(PlayerBagRiskState.BaseSafe));
        }

        [UnityTest]
        public IEnumerator FailedDownloadKeepsRetryAndDoesNotEnter()
        {
            resources.NeedsDownload = true; resources.FailDownload = true;
            var ui = OpenDialog();
            GetRows(ui)[0].BtnSelect.onClick.Invoke();
            ui.BtnAction.onClick.Invoke();
            yield return null;
            Assert.That(mapLoader.LoadRequests, Is.Empty);
            Assert.That(ui.BtnAction.interactable, Is.True);
            Assert.That(ui.ActionText.text, Is.EqualTo(Loc.T("ui.map_selection.action.retry_download")));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().RiskState, Is.EqualTo(PlayerBagRiskState.BaseSafe));
            resources.FailDownload = false;
            ui.BtnAction.onClick.Invoke();
            yield return null;
            Assert.That(ui.ActionText.text, Is.EqualTo(Loc.T("ui.map_selection.action.explore")));
            Assert.That(mapLoader.LoadRequests, Is.Empty);
        }

        [UnityTest]
        public IEnumerator LateDownloadDoesNotMutateReopenedDialog()
        {
            resources.NeedsDownload = true; resources.HoldDownload = true;
            var ui = OpenDialog();
            GetRows(ui)[0].BtnSelect.onClick.Invoke();
            ui.BtnAction.onClick.Invoke();
            Assert.That(ui.BtnAction.interactable, Is.False);
            UIHelper.HideUI<MapSelectionUICtrl>();
            ui = OpenDialog();
            Assert.That(ui.SelectedName.text, Is.EqualTo(Loc.T("map.civilisation_valley.name")));
            resources.Completion.TrySetResult(new OptionalGameplayDownloadResult(true, string.Empty));
            yield return null;
            Assert.That(ui.SelectedName.text, Is.EqualTo(Loc.T("map.civilisation_valley.name")));
            Assert.That(ui.BtnAction.interactable, Is.True);
            Assert.That(ui.ActionText.text, Is.EqualTo(Loc.T("ui.map_selection.action.download")));
            Assert.That(mapLoader.LoadRequests, Is.Empty);
        }

        [UnityTest]
        public IEnumerator OnlyOneUpcomingChapterAndOtherMapsAreNotHistoricalChapters()
        {
            var ui = OpenDialog();
            var rows = ui.MapList.GetDraggableItems().Select(t => t.GetComponent<MapSelectionOptionBoxUI>()).ToList();
            Assert.That(rows[0].TextName.text, Is.EqualTo(Loc.T("map.civilisation_valley.name")));
            var next = rows.Single(row => row.TextDetail.text == Loc.T("ui.map_selection.next_chapter"));
            Assert.That(rows.Count(row => row.TextDetail.text == Loc.T("ui.map_selection.other_expedition")), Is.EqualTo(3));
            next.BtnSelect.onClick.Invoke();
            ui.BtnAction.onClick.Invoke();
            yield return null;
            Assert.That(ui.BtnAction.interactable, Is.False);
            Assert.That(mapLoader.LoadRequests, Is.Empty);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().RiskState, Is.EqualTo(PlayerBagRiskState.BaseSafe));
        }

        [Test]
        public void TryShowOfflineRejectsUnsafeMapStates()
        {
            Game.Interface.GetModel<MapSceneModel>().Clear();
            Assert.That(MapSelectionUICtrl.TryShowOffline(out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(UIHelper.GetUICtrl<MapSelectionUICtrl>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator OpeningBuildsFourRealRowsWithLocalizedStateAndTargets()
        {
            var ui = OpenDialog();
            yield return null;
            var rows = GetRows(ui);
            var maps = MapCatalog.GetExpeditionMaps();
            Assert.That(rows, Has.Count.EqualTo(4), Diagnostics("open rows"));
            Assert.That(rows.Select(row => row.TextName.text), Is.EqualTo(new[]
            {
                Loc.T("map.survival_sample.name"), Loc.T("map.mission_sample.name"), Loc.T("map.hejia_village.name"), Loc.T("map.civilisation_valley.name")
            }));
            GetRows(ui)[0].BtnSelect.onClick.Invoke();
            Assert.That(ui.StoryText.text, Is.EqualTo(Loc.T("ui.map_selection.objective.survive", Loc.T("task.expedition.survival_sample.survive.name"), Loc.T("ui.expedition.duration", 24, 0))));
            rows[1].BtnSelect.onClick.Invoke();
            Assert.That(ui.StoryText.text, Is.EqualTo(Loc.T("ui.map_selection.objective.kill", Loc.T("task.expedition.mission_sample.kill.name"), 2)));
            Assert.That(rows[2].TextDetail.text, Does.Contain(Loc.T("ui.map_selection.other_expedition")));
            Assert.That(rows[2].TextDetail.text, Does.Not.Contain(Loc.T("ui.map_selection.target.kills", 0)));
            Assert.That(rows[0].TextState.text, Is.EqualTo(Loc.T("ui.map_selection.state.unlocked")));
            Assert.That(rows[1].TextState.text, Does.Contain("1"));
            Assert.That(ui.ActionText.text, Is.EqualTo(Loc.T("ui.map_selection.action.unlock")));
            Assert.That(rows[2].TextState.text, Is.EqualTo(Loc.T("ui.map_selection.state.unlocked")));
        }

        [UnityTest]
        public IEnumerator LockedRowClickSpendsOnceUnlocksAndRefreshesEveryRealRow()
        {
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            Assert.That(inventory.TryAddWarehouseItem(ItemIds.MetalParts, 1, out var accepted), Is.True);
            Assert.That(accepted, Is.EqualTo(1));
            var ui = OpenDialog();
            SelectAndAct(GetRows(ui)[1]);
            yield return null;
            var rows = GetRows(ui);
            Assert.That(UIHelper.GetUICtrl<MapSelectionUICtrl>().IsShow(), Is.True, Diagnostics("unlock result"));
            Assert.That(Game.Interface.GetModel<WarehouseModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.Zero,
                "一次解锁事务应且仅应扣除一份材料");
            Assert.That(Game.Interface.GetSystem<BaseMapSelectionSystem>().IsMapUnlocked(MapIds.MissionSample), Is.True);
            Assert.That(mapLoader.LoadRequests, Is.Empty, "解锁事务不得提前进入地图加载");
            Assert.That(rows, Has.Count.EqualTo(4));
            Assert.That(rows.All(RowHasRenderedState), Is.True, "解锁后全部真实行都应重新渲染");
            Assert.That(rows[1].TextState.text, Is.EqualTo(Loc.T("ui.map_selection.state.unlocked")));
            Assert.That(ui.ActionText.text, Is.EqualTo(Loc.T("ui.map_selection.action.explore")));
            Assert.That(ui.BtnClose.interactable, Is.True);
            Assert.That(rows.All(row => row.BtnSelect.interactable), Is.True);
        }

        [UnityTest]
        public IEnumerator UnlockedRowClickUsesRealMapTransactionClosesDialogAndShowsHudOnSuccess()
        {
            mapLoader.Mode = ControlledLoadMode.SucceedImmediately;
            var ui = OpenDialog();
            SelectAndAct(GetRows(ui)[0]);
            yield return WaitUntilBounded(() => Game.Interface.GetModel<ExpeditionModel>().IsActive,
                "successful expedition activation");
            Assert.That(mapLoader.LoadRequests, Is.EqualTo(new[] { MapIds.SurvivalSample }), Diagnostics("success count"));
            Assert.That(UIHelper.GetUICtrl<MapSelectionUICtrl>().IsShow(), Is.False, Diagnostics("dialog closed"));
            var hud = UIHelper.GetUICtrl<ExpeditionHudUICtrl>();
            Assert.That(hud, Is.Not.Null, Diagnostics("HUD created"));
            Assert.That(hud.IsShow(), Is.True, Diagnostics("HUD visible"));
        }

        [UnityTest]
        public IEnumerator InsufficientCostKeepsDialogRestoresButtonsAndShowsLocalizedTip()
        {
            var ui = OpenDialog();
            SelectAndAct(GetRows(ui)[1]);
            yield return null;
            var rows = GetRows(ui);
            Assert.That(UIHelper.GetUICtrl<MapSelectionUICtrl>().IsShow(), Is.True, Diagnostics("cost failure"));
            Assert.That(Game.Interface.GetSystem<BaseMapSelectionSystem>().IsMapUnlocked(MapIds.MissionSample), Is.False);
            Assert.That(mapLoader.LoadRequests, Is.Empty);
            Assert.That(ui.BtnClose.interactable, Is.True);
            Assert.That(rows.All(row => row.BtnSelect.interactable), Is.True);
            AssertVisibleTip(Loc.T("ui.map_selection.error.cost"));
        }

        [UnityTest]
        public IEnumerator EnterFailureKeepsDialogRestoresButtonsAndShowsLocalizedTip()
        {
            mapLoader.Mode = ControlledLoadMode.FailImmediately;
            var ui = OpenDialog();
            SelectAndAct(GetRows(ui)[0]);
            yield return null;
            var rows = GetRows(ui);
            Assert.That(mapLoader.LoadRequests, Is.EqualTo(new[] { MapIds.SurvivalSample }), Diagnostics("enter failure count"));
            Assert.That(UIHelper.GetUICtrl<MapSelectionUICtrl>().IsShow(), Is.True, Diagnostics("enter failure dialog"));
            Assert.That(ui.BtnClose.interactable, Is.True);
            Assert.That(rows.All(row => row.BtnSelect.interactable), Is.True);
            AssertVisibleTip(Loc.T("ui.map_selection.error.enter"));
        }

        [UnityTest]
        public IEnumerator RecoveryDialogKeepsExitActionsAndIgnoresHiddenOwnerCallbacks()
        {
            var ui = OpenDialog();
            var controller = UIHelper.GetUICtrl<MapSelectionUICtrl>();
            var flow = Game.Interface.GetSystem<ExpeditionFlowSystem>();
            typeof(ExpeditionFlowSystem).GetField("pendingEntryRecovery", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(flow, Game.Interface.GetSystem<SaveSystem>().CaptureRuntimeSnapshot());
            typeof(ExpeditionFlowSystem).GetField("entryFailure", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(flow, "original entry failure");
            var generation = (int)typeof(MapSelectionUICtrl).GetField("operationGeneration", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(controller);
            typeof(MapSelectionUICtrl).GetMethod("ShowEntryRecovery", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, new object[] { generation, ui, "original entry failure" });
            yield return null;
            var message = UIHelper.GetUICtrl<MessageUICtrl>();
            Assert.That(message.IsShow(), Is.True);
            var config = (MessageConfig)typeof(MessageUICtrl).GetField("currentConfig", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(message);
            Assert.That(config.CloseOnBackground, Is.False);
            Assert.That(config.Content, Does.Contain("original entry failure"));
            Assert.That(config.Buttons.Select(button => button.ButtonText), Is.EqualTo(new[]
                { Loc.T("ui.expedition.retry_return"), Loc.T("ui.pause.main_menu") }));
            Assert.That(ui.BtnClose.interactable, Is.False);
            UIHelper.HideUI<MapSelectionUICtrl>();
            config.Buttons[0].OnClick();
            config.Buttons[1].OnClick();
            yield return null;
            Assert.That(flow.HasPendingEntryRecovery, Is.True, "隐藏界面的旧按钮不得恢复或释放当前事务");
            Assert.That(mapLoader.LoadRequests, Is.Empty);
        }

        [UnityTest]
        public IEnumerator PendingRequestRejectsRepeatedRowsAndHideInvalidatesLateSuccessAndOldListeners()
        {
            mapLoader.Mode = ControlledLoadMode.Hold;
            var ui = OpenDialog();
            var controller = UIHelper.GetUICtrl<MapSelectionUICtrl>();
            var oldRows = GetRows(ui);
            SelectAndAct(oldRows[0]);
            SelectAndAct(oldRows[0]);
            SelectAndAct(oldRows[2]);
            yield return null;
            Assert.That(mapLoader.LoadRequests, Is.EqualTo(new[] { MapIds.SurvivalSample }), Diagnostics("pending de-duplication"));
            Assert.That(ui.BtnClose.interactable, Is.False);
            Assert.That(oldRows.All(row => !row.BtnSelect.interactable), Is.True);
            UIHelper.HideUI<MapSelectionUICtrl>();
            Assert.That(controller.IsShow(), Is.False);
            SelectAndAct(oldRows[0]);
            SelectAndAct(oldRows[2]);
            Assert.That(mapLoader.LoadRequests, Has.Count.EqualTo(1), Diagnostics("old listeners after hide"));
            mapLoader.CompleteHeldSuccess();
            yield return WaitUntilBounded(
                () => Game.Interface.GetModel<MapSceneModel>().Status.Value != MapLoadStatus.Loading,
                "late map completion after hide");
            yield return null;
            Assert.That(controller.IsShow(), Is.False, Diagnostics("late result dialog"));
            Assert.That(UIHelper.GetUICtrl<ExpeditionHudUICtrl>(), Is.Null, Diagnostics("late result HUD"));
            Assert.That(mapLoader.LoadRequests, Has.Count.EqualTo(1), Diagnostics("late result count"));
            SelectAndAct(oldRows[0]);
            Assert.That(mapLoader.LoadRequests, Has.Count.EqualTo(1), "隐藏后的旧行不得再次提交");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OnlineModeUsesRealRowsSendsSelectedMapOnceAndRestoresAfterFailure()
        {
            OnlineAccountSystem account = Game.Interface.GetSystem<OnlineAccountSystem>();
            _ = Game.Interface.GetSystem<OnlineWorldSystem>();
            Task<OnlineAccountOperationResult> login = account.LoginAsync("map_user", "Pass_123");
            yield return WaitUntilBounded(() => network.PendingLoginCount == 1, "online login request");
            network.CompleteLogin();
            yield return WaitUntilBounded(() => login.IsCompleted, "online login completion");
            yield return WaitUntilBounded(() => network.PendingCurrentCount == 1, "online restore request");
            network.CompleteCurrentEmpty();
            yield return WaitUntilBounded(() => Game.Interface.GetSystem<OnlineWorldSystem>().Snapshot.OperationState == OnlineWorldOperationState.Idle,
                "online restore completion");

            Assert.That(MapSelectionUICtrl.TryShowOnline(out string error), Is.True, error);
            MapSelectionUI ui = Object.FindObjectsOfType<MapSelectionUI>(true)
                .Single(item => item.gameObject.scene.IsValid() && string.IsNullOrEmpty(item.gameObject.scene.path));
            var rows = GetRows(ui);
            Assert.That(rows, Has.Count.EqualTo(4));
            Assert.That(rows.All(row => string.IsNullOrEmpty(row.TextState.text)), Is.True,
                "online mode must not expose offline unlock state or cost");
            Assert.That(ui.ActionText.text, Is.EqualTo(Loc.T("ui.map_selection.action.explore")));

            SelectAndAct(rows[1]);
            SelectAndAct(rows[0]);
            yield return WaitUntilBounded(() => network.PendingEnterCount == 1, "online enter request");
            Assert.That(network.EnterRequests.Single().MapId, Is.EqualTo(MapIds.MissionSample));
            Assert.That(ui.BtnClose.interactable, Is.False);
            Assert.That(GetRows(ui).All(row => !row.BtnSelect.interactable), Is.True);
            Assert.That(MapSelectionUICtrl.TryShowOffline(out error), Is.False, "pending mode switch must be rejected");

            network.CompleteEnterError(200123);
            yield return WaitUntilBounded(() => network.PendingEnterCount == 0 && ui.BtnClose.interactable,
                "online failure restore");
            Assert.That(UIHelper.GetUICtrl<MapSelectionUICtrl>().IsShow(), Is.True);
            Assert.That(GetRows(ui).All(row => row.BtnSelect.interactable), Is.True);
            AssertVisibleTip(OnlineWorldText.Result(OnlineWorldResultCode.ServiceUnavailable));
        }

        [Test]
        public void PrefabsExposeRequiredRealButtonAndListBindings()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Expedition/MapSelection.prefab");
            var boxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Expedition/Box/MapSelectionOptionBox.prefab");
            var ui = prefab.GetComponent<MapSelectionUI>();
            var box = boxPrefab.GetComponent<MapSelectionOptionBoxUI>();
            var viewport = ui.MapScroll.viewport;
            Assert.That(viewport, Is.Not.Null);
            Assert.That(viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(viewport.GetComponent<Mask>(), Is.Null);
            Assert.That(viewport.GetComponent<Image>(), Is.Null);
            var scrollBackground = ui.MapScroll.GetComponent<Image>();
            Assert.That(scrollBackground, Is.Not.Null);
            Assert.That(scrollBackground.raycastTarget, Is.True);
            Assert.That(ui.MapScroll.content, Is.SameAs(ui.MapList.transform));
            Assert.That(ui.MapList.itemPrefab, Is.SameAs(boxPrefab));
            Assert.That(box.BtnSelect.onClick.GetPersistentEventCount(), Is.Zero);
            Assert.That(box.BtnSelect.gameObject, Is.SameAs(boxPrefab));
            Assert.That(ui.BtnAction, Is.Not.Null);
        }

        [Test]
        public void LocalizationKeysExistInBothLanguages()
        {
            var zh = JObject.Parse(File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
            var en = JObject.Parse(File.ReadAllText("Assets/Game/Config/Localization/en-US.json"));
            CollectionAssert.AreEquivalent(zh.Properties().Select(item => item.Name), en.Properties().Select(item => item.Name));
            foreach (var key in RequiredLocalizationKeys)
            {
                Assert.That(zh[key]?.Value<string>(), Is.Not.Empty, key);
                Assert.That(en[key]?.Value<string>(), Is.Not.Empty, key);
            }

            Assert.That(Texts.TaskExpeditionSurvivalSampleSurviveName, Is.EqualTo(Loc.T("task.expedition.survival_sample.survive.name")));
            Assert.That(Texts.TaskExpeditionMissionSampleKillName, Is.EqualTo(Loc.T("task.expedition.mission_sample.kill.name")));
            Assert.That(Texts.MapSelectionObjectiveSurvive("task", "time"), Is.EqualTo(Loc.T("ui.map_selection.objective.survive", "task", "time")));
            Assert.That(Texts.MapSelectionObjectiveKill("task", 2), Is.EqualTo(Loc.T("ui.map_selection.objective.kill", "task", 2)));
            Assert.That(Texts.MapSelectionTaskConfigUnavailable, Is.EqualTo(Loc.T("ui.map_selection.task_config_unavailable")));
            Assert.That(Texts.MapSelectionStateUnavailable, Is.EqualTo(Loc.T("ui.map_selection.state.unavailable")));
            Assert.That(Texts.MapSelectionActionUnavailable, Is.EqualTo(Loc.T("ui.map_selection.action.unavailable")));
            Assert.That(Texts.MapSelectionErrorTaskConfigUnavailable, Is.EqualTo(Loc.T("ui.map_selection.error.task_config_unavailable")));
        }

        [UnityTest]
        public IEnumerator EmptyTaskConfigKeepsOfflineRowEnterable()
        {
            mapLoader.Mode = ControlledLoadMode.SucceedImmediately;
            var ui = OpenDialog();
            yield return null;
            var row = GetRows(ui)[2];
            Assert.That(row.TextName.text, Is.EqualTo(Loc.T("map.hejia_village.name")));
            Assert.That(row.TextDetail.text, Does.Contain(Loc.T("ui.map_selection.other_expedition")));
            Assert.That(row.TextDetail.text, Does.Not.Contain(Loc.T("ui.map_selection.target.kills", 0)));
            Assert.That(row.BtnSelect.interactable, Is.True);

            SelectAndAct(row);
            yield return WaitUntilBounded(() => Game.Interface.GetModel<ExpeditionModel>().IsActive,
                "empty-task expedition activation");

            Assert.That(mapLoader.LoadRequests, Has.Count.EqualTo(1));
            Assert.That(mapLoader.LoadRequests.All(id => id == MapIds.HeJiaVillage), Is.True);
            Assert.That(Game.Interface.GetModel<ExpeditionTaskModel>().Tasks, Is.Empty);
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().CanReturn, Is.True);
            Assert.That(UIHelper.GetUICtrl<MapSelectionUICtrl>().IsShow(), Is.False);
        }

        [UnityTest]
        public IEnumerator HeJiaVillageRowEntersLocalSceneAndCanReturn()
        {
            mapLoader.Mode = ControlledLoadMode.SucceedImmediately;
            var ui = OpenDialog();
            yield return null;
            var row = GetRows(ui)[2];
            Assert.That(row.TextName.text, Is.EqualTo(Loc.T("map.hejia_village.name")));
            Assert.That(row.BtnSelect.interactable, Is.True);

            SelectAndAct(row);
            yield return WaitUntilBounded(() => Game.Interface.GetModel<ExpeditionModel>().IsActive,
                "local map expedition activation");

            Assert.That(mapLoader.LoadRequests, Has.Count.EqualTo(1));
            Assert.That(mapLoader.LoadRequests.All(id => id == MapIds.HeJiaVillage), Is.True);
            Assert.That(Game.Interface.GetModel<ExpeditionTaskModel>().Tasks, Is.Empty);
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().CanReturn, Is.True);
            Assert.That(UIHelper.GetUICtrl<MapSelectionUICtrl>().IsShow(), Is.False);
        }
        [UnityTest]
        public IEnumerator InvalidTaskConfigDisablesOfflineRowAndSurvivesRefresh()
        {
            configProxy.FailedTaskMapId = MapIds.SurvivalSample;
            var ui = OpenDialog();
            yield return null;
            var row = GetRows(ui)[0];
            AssertTaskConfigUnavailable(row);

            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });
            yield return null;
            row = GetRows(ui)[0];
            AssertTaskConfigUnavailable(row);

            SelectAndAct(row);
            yield return null;
            Assert.That(mapLoader.LoadRequests, Is.Empty);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().RiskState, Is.EqualTo(PlayerBagRiskState.BaseSafe));
            Assert.That(UIHelper.GetUICtrl<MapSelectionUICtrl>().IsShow(), Is.True);
            AssertVisibleTip(Loc.T("ui.map_selection.error.task_config_unavailable"));
        }

        private static MapSelectionUI OpenDialog()
        {
            Assert.That(MapSelectionUICtrl.TryShowOffline(out var error), Is.True, error);
            var ctrl = UIHelper.GetUICtrl<MapSelectionUICtrl>();
            Assert.That(ctrl, Is.Not.Null);
            Assert.That(ctrl.IsShow(), Is.True);
            var ui = Object.FindObjectsOfType<MapSelectionUI>(true)
                .Single(item => item.gameObject.scene.IsValid() && string.IsNullOrEmpty(item.gameObject.scene.path));
            Assert.That(GetRows(ui), Has.Count.EqualTo(4));
            return ui;
        }

        // 旧远征回归按稳定目录顺序访问真实卡片；下一章预告没有地图 ID，不参与领域事务测试。
        private static List<MapSelectionOptionBoxUI> GetRows(MapSelectionUI ui) => MapCatalog.GetExpeditionMaps()
            .Select(map => ui.MapList.GetDraggableItems().Select(item => item.GetComponent<MapSelectionOptionBoxUI>())
                .Single(row => row.TextName.text == Loc.T(map.DisplayNameKey))).ToList();

        private static void SelectAndAct(MapSelectionOptionBoxUI row)
        {
            row.BtnSelect.onClick.Invoke();
            var ctrl = UIHelper.GetUICtrl<MapSelectionUICtrl>();
            if (ctrl != null && ctrl.IsShow()) row.GetComponentInParent<MapSelectionUI>().BtnAction.onClick.Invoke();
        }

        private static bool RowHasRenderedState(MapSelectionOptionBoxUI row) => row != null &&
            !string.IsNullOrWhiteSpace(row.TextName.text) && !string.IsNullOrWhiteSpace(row.TextDetail.text) &&
            !string.IsNullOrWhiteSpace(row.TextState.text);

        private static void AssertTaskConfigUnavailable(MapSelectionOptionBoxUI row)
        {
            Assert.That(row.TextDetail.text, Is.EqualTo(Loc.T("ui.map_selection.task_config_unavailable")));
            Assert.That(row.TextState.text, Is.EqualTo(Loc.T("ui.map_selection.state.unavailable")));
            Assert.That(row.BtnSelect.interactable, Is.False);
        }

        private void PrepareStableOfflineBase()
        {
            var architecture = Game.Interface;
            var context = CreateMapContext(MapIds.Base);
            architecture.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            architecture.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, new TestMapHandle(MapIds.Base, context));
            architecture.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.BaseSafe);
            var save = architecture.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var created, out var saveError), Is.True, saveError);
            Assert.That(save.CommitBaseRuntime(created, out saveError), Is.True, saveError);
            architecture.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player.graybox.default", "Prefabs/Characters/GrayboxPlayer", 100, 10, 1f, 1f, 1f, 1f, 5f), false);
            architecture.GetModel<CombatModel>().SetPlayerHP(100);
        }

        private IEnumerator WaitUntilBounded(Func<bool> completed, string stage, int maxFrames = 120)
        {
            for (var frame = 0; frame < maxFrames && !completed(); frame++) yield return null;
            Assert.That(completed(), Is.True, Diagnostics($"timeout: {stage}, maxFrames={maxFrames}"));
        }

        private string Diagnostics(string stage)
        {
            var map = Game.Interface.GetModel<MapSceneModel>();
            var dialog = UIHelper.GetUICtrl<MapSelectionUICtrl>();
            var hud = UIHelper.GetUICtrl<ExpeditionHudUICtrl>();
            return $"stage={stage}; requests=[{string.Join(",", mapLoader?.LoadRequests ?? new List<string>())}]; " +
                   $"requestCount={mapLoader?.LoadRequests.Count ?? 0}; mapStatus={map.Status.Value}; " +
                   $"currentMap={map.CurrentMapId ?? "<null>"}; dialogShown={dialog?.IsShow() ?? false}; hudShown={hud?.IsShow() ?? false}";
        }

        private static void AssertVisibleTip(string expected)
        {
            var ctrl = UIHelper.GetUICtrl<FloatUICtrl>();
            Assert.That(ctrl, Is.Not.Null, "提示控制器未创建");
            Assert.That(ctrl.IsShow(), Is.True, "提示控制器未显示");
            var texts = Object.FindObjectsOfType<FloatUI>(true)
                .Where(item => item.gameObject.scene.IsValid() && string.IsNullOrEmpty(item.gameObject.scene.path))
                .SelectMany(item => item.TipsRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
                .Where(text => text.gameObject.activeInHierarchy).Select(text => text.text).ToArray();
            Assert.That(texts, Does.Contain(expected));
        }

        private static GameObject LoadUiPrefab(string path)
        {
            switch (path)
            {
                case "Prefabs/UI/Expedition/MapSelection":
                    return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Expedition/MapSelection.prefab");
                case "Prefabs/UI/Expedition/ExpeditionHud":
                    return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Expedition/ExpeditionHud.prefab");
                case "Prefabs/UI/Notify/Float": return CreateFloatPrefab();
                case "Prefabs/UI/Notify/Message":
                    return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Notify/Message.prefab");
                default: return null;
            }
        }

        private static GameObject CreateFloatPrefab()
        {
            var root = new GameObject("Float", typeof(RectTransform), typeof(FloatUI));
            var ui = root.GetComponent<FloatUI>();
            var tipsRoot = new GameObject("TipsRoot", typeof(RectTransform));
            tipsRoot.transform.SetParent(root.transform, false);
            ui.TipsRoot = tipsRoot.GetComponent<RectTransform>();
            var tip = new GameObject("TipTemplate", typeof(RectTransform), typeof(FloatTextBoxUI));
            tip.transform.SetParent(ui.TipsRoot, false);
            var text = new GameObject("TextTips", typeof(RectTransform), typeof(TextMeshProUGUI));
            text.transform.SetParent(tip.transform, false);
            tip.SetActive(false);
            ui.TipTemplate = tip.GetComponent<FloatTextBoxUI>();
            ui.TipTemplate.TextTips = text.GetComponent<TextMeshProUGUI>();
            return root;
        }

        private static void CreateUIRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(UIRoot));
            foreach (var layerName in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var layer = new GameObject(layerName, typeof(RectTransform));
                layer.transform.SetParent(root.transform, false);
            }
            var eventSystem = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
            var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            Assert.That(inputModuleType, Is.Not.Null);
            eventSystem.AddComponent(inputModuleType);
            eventSystem.transform.SetParent(root.transform, false);
            new GameObject("PoolRoot");
            var camera = new GameObject("Main Camera", typeof(Camera));
            camera.tag = "MainCamera";
        }

        private static MapContext CreateMapContext(string mapId)
        {
            var root = new GameObject("Map-" + mapId);
            var presentation = new GameObject("Presentation");
            presentation.transform.SetParent(root.transform, false);
            var dynamicRoot = new GameObject("DynamicObjectRoot");
            dynamicRoot.transform.SetParent(presentation.transform, false);
            var context = root.AddComponent<MapContext>();
            var serialized = new SerializedObject(context);
            serialized.FindProperty("mapContentRoot").objectReferenceValue = presentation;
            serialized.FindProperty("dynamicObjectRoot").objectReferenceValue = dynamicRoot.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return context;
        }

        private static void DestroyTransientSceneObjects()
        {
            foreach (var obj in Object.FindObjectsOfType<GameObject>(true))
                if (obj.scene.IsValid() && obj.scene.isLoaded && string.IsNullOrEmpty(obj.scene.path)) Object.DestroyImmediate(obj);
        }

        private static LubanRuntimeConfigProvider CreateRealConfigProvider() => new LubanRuntimeConfigProvider(() =>
            new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", $"{name}.bytes")))));

        private static IRuntimeConfigProvider CreateSpawnlessConfigProvider(out SpawnlessConfigProxy proxy)
        {
            var provider = DispatchProxy.Create<IRuntimeConfigProvider, SpawnlessConfigProxy>();
            proxy = (SpawnlessConfigProxy)(object)provider;
            proxy.Inner = CreateRealConfigProvider();
            return provider;
        }

        public class SpawnlessConfigProxy : DispatchProxy
        {
            public IRuntimeConfigProvider Inner { get; set; }
            public string FailedTaskMapId { get; set; }

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod.Name == nameof(IRuntimeConfigProvider.TryGetMapSpawns))
                {
                    var mapId = (string)args[0];
                    args[1] = new[]
                    {
                        new MapSpawnDefinition("player." + mapId, mapId, MapSpawnObjectType.Player,
                            "player.graybox.default", 0f, 0f, 0f, 0f)
                    };
                    args[2] = null;
                    return true;
                }

                if (targetMethod.Name == nameof(IRuntimeConfigProvider.TryGetExpeditionTasks) &&
                    string.Equals((string)args[0], FailedTaskMapId, StringComparison.Ordinal))
                {
                    args[1] = Array.Empty<ExpeditionTaskDefinition>();
                    args[2] = "controlled task configuration failure";
                    return false;
                }

                try
                {
                    return targetMethod.Invoke(Inner, args);
                }
                catch (TargetInvocationException exception)
                {
                    throw exception.InnerException ?? exception;
                }
            }
        }

        private enum ControlledLoadMode { SucceedImmediately, FailImmediately, Hold }

        private sealed class ControlledMapContentLoader : IMapContentLoader
        {
            private UniTaskCompletionSource<MapContentLoadResult> pending;
            private string pendingMapId;
            public ControlledLoadMode Mode { get; set; } = ControlledLoadMode.SucceedImmediately;
            public List<string> LoadRequests { get; } = new List<string>();

            public UniTask<MapContentLoadResult> LoadAsync(string mapId, string address, Action<float> onProgress,
                CancellationToken cancellationToken = default)
            {
                LoadRequests.Add(mapId);
                onProgress?.Invoke(0.5f);
                if (Mode == ControlledLoadMode.FailImmediately)
                    return UniTask.FromResult(MapContentLoadResult.Fail("controlled map load failure"));
                if (Mode == ControlledLoadMode.Hold)
                {
                    Assert.That(pending, Is.Null, "同一时间只允许一份受控地图请求");
                    pendingMapId = mapId;
                    pending = new UniTaskCompletionSource<MapContentLoadResult>();
                    return pending.Task;
                }
                onProgress?.Invoke(1f);
                return UniTask.FromResult(Success(mapId, LoadRequests.Count(item => item == mapId)));
            }

            public UniTask<MapContentUnloadResult> UnloadAsync(IMapContentHandle handle,
                CancellationToken cancellationToken = default)
            {
                if (handle is TestMapHandle testHandle) testHandle.Valid = false;
                return UniTask.FromResult(MapContentUnloadResult.Ok());
            }

            public void CompleteHeldSuccess()
            {
                Assert.That(pending, Is.Not.Null, "没有待完成的地图加载请求");
                var completion = pending;
                var mapId = pendingMapId;
                pending = null;
                pendingMapId = null;
                completion.TrySetResult(Success(mapId, LoadRequests.Count(item => item == mapId)));
            }

            private readonly List<TerrainData> terrainData = new List<TerrainData>();

            private MapContentLoadResult Success(string mapId, int mapLoadIndex)
            {
                var context = CreateMapContext(mapId);
                if (mapId == MapIds.HeJiaVillage)
                {
                    var ordinal = Math.Max(0, mapLoadIndex - 1);
                    var tileObject = new GameObject($"GeneratedTerrainTile-{ordinal}", typeof(Terrain), typeof(GeneratedTerrainTile));
                    tileObject.transform.SetParent(context.MapContentRoot.transform, false);
                    var data = new TerrainData { heightmapResolution = 33, size = new Vector3(32f, 8f, 32f) };
                    terrainData.Add(data);
                    var terrain = tileObject.GetComponent<Terrain>();
                    terrain.terrainData = data;
                    var tile = tileObject.GetComponent<GeneratedTerrainTile>();
                    SetTileField(tile, "mapName", "HeJiaVillage");
                    SetTileField(tile, "row", ordinal / 2 + 1);
                    SetTileField(tile, "column", ordinal % 2 + 1);
                    SetTileField(tile, "totalRows", 2);
                    SetTileField(tile, "totalColumns", 2);
                    SetTileField(tile, "tileWorldSize", 32f);
                    SetTileField(tile, "heightmapResolution", 33);
                    SetTileField(tile, "terrain", terrain);
                }

                return MapContentLoadResult.Ok(new TestMapHandle(mapId, context));
            }

            public void Cleanup()
            {
                foreach (var data in terrainData)
                    if (data != null) Object.DestroyImmediate(data);
                terrainData.Clear();
            }

            private static void SetTileField<T>(GeneratedTerrainTile tile, string name, T value) =>
                typeof(GeneratedTerrainTile).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(tile, value);
        }

        private sealed class TestMapHandle : IMapContentHandle
        {
            public TestMapHandle(string mapId, MapContext context) { MapId = mapId; Context = context; }
            public string MapId { get; }
            public MapContext Context { get; }
            public bool Valid { get; set; } = true;
            public bool IsValid => Valid;
        }

        private sealed class MapSelectionNetwork : INetworkRuntime, INetworkResponseObserver
        {
            private readonly List<(IRequest Request, TaskCompletionSource<IResponse> Completion)> pending = new();
            public List<C2G_EnterWorld> EnterRequests { get; } = new();
            public int PendingEnterCount => pending.Count(item => item.Request is C2G_EnterWorld);
            public int PendingLoginCount => pending.Count(item => item.Request is C2G_Login);
            public int PendingCurrentCount => pending.Count(item => item.Request is C2G_GetCurrentWorld);
            public bool IsInitialized => true;
            public NetworkConnectionState ConnectionState => NetworkConnectionState.Connected;
            public int ConnectionGeneration => 1;
            public void Initialize() { }
            public void Update() { }
            public Task ConnectAsync(NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<TResponse> CallAsync<TResponse>(IRequest request, int timeoutMilliseconds, CancellationToken cancellationToken) where TResponse : class, IResponse
            {
                var completion = new TaskCompletionSource<IResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                pending.Add((request, completion));
                if (request is C2G_EnterWorld enter) EnterRequests.Add(enter);
                return completion.Task.ContinueWith(task => (TResponse)task.GetAwaiter().GetResult(), CancellationToken.None);
            }
            public IDisposable RegisterPushHandler<T>(Action<T> handler) where T : class, IMessage => new TestSubscription();
            public IDisposable RegisterDisconnectedHandler(Action<NetworkDisconnected> handler) => new TestSubscription();
            public IDisposable RegisterResponseHandler<T>(Action<T> handler) where T : class, IResponse => new TestSubscription();
            public void Disconnect(NetworkDisconnectReason reason) { }
            public void Dispose() { }
            public void CompleteEnterError(int error)
            {
                int index = pending.FindIndex(item => item.Request is C2G_EnterWorld);
                Assert.That(index, Is.GreaterThanOrEqualTo(0));
                var completion = pending[index].Completion;
                pending.RemoveAt(index);
                G2C_EnterWorld response = G2C_EnterWorld.Create();
                response.Error = error;
                completion.SetResult(response);
            }
            public void CompleteLogin()
            {
                var completion = Take<C2G_Login>();
                G2C_Login response = G2C_Login.Create(); response.Error = 0; response.AccountId = 1; response.PlayerId = 2; response.SessionEpoch = 3;
                completion.SetResult(response);
            }
            public void CompleteCurrentEmpty()
            {
                var completion = Take<C2G_GetCurrentWorld>();
                G2C_GetCurrentWorld response = G2C_GetCurrentWorld.Create(); response.Error = 0; response.HasWorld = false;
                completion.SetResult(response);
            }
            private TaskCompletionSource<IResponse> Take<T>() where T : class, IRequest
            {
                int index = pending.FindIndex(item => item.Request is T);
                Assert.That(index, Is.GreaterThanOrEqualTo(0));
                var completion = pending[index].Completion;
                pending.RemoveAt(index);
                return completion;
            }
        }

        /// <summary>本夹具提供本地资源；资源失败及下载事务由门禁测试覆盖。</summary>
        private sealed class ReadyGameplayResources : OptionalGameplayResourceSystem
        {
            public bool NeedsDownload;
            public bool FailDownload;
            public bool HoldDownload;
            public readonly UniTaskCompletionSource<OptionalGameplayDownloadResult> Completion = new();
            public override OptionalGameplayResourceQuery QueryMap(string mapId) =>
                new OptionalGameplayResourceQuery(NeedsDownload ? OptionalGameplayResourceState.NeedsDownload : OptionalGameplayResourceState.Ready,
                    "test", NeedsDownload ? 1 : 0, NeedsDownload ? 1048576 : 0, string.Empty);
            public override UniTask<OptionalGameplayResourceQuery> CheckMapAsync(string mapId) => UniTask.FromResult(QueryMap(mapId));
            public override UniTask<OptionalGameplayDownloadResult> DownloadMapAsync(string mapId, string version,
                Action<OptionalGameplayDownloadProgress> progress, CancellationToken cancellationToken = default)
            {
                if (HoldDownload) return Completion.Task;
                if (FailDownload) return UniTask.FromResult(new OptionalGameplayDownloadResult(false, "test network failure"));
                progress?.Invoke(new OptionalGameplayDownloadProgress(1048576, 1048576, 1, 1));
                NeedsDownload = false;
                return UniTask.FromResult(new OptionalGameplayDownloadResult(true, string.Empty));
            }
        }

        private sealed class MapSelectionNetworkConfig : INetworkConfigProvider
        {
            public Task<NetworkConfigResult> LoadAsync(CancellationToken cancellationToken) =>
                Task.FromResult(NetworkConfigResult.Ok(new NetworkEndpoint("127.0.0.1", 31001)));
        }

        private sealed class MapSelectionPreferenceStore : IOnlineAccountPreferenceStore
        {
            public string LoadLastUserName() => string.Empty;
            public bool SaveLastUserName(string userName, out string error) { error = null; return true; }
        }

        private sealed class TestSubscription : IDisposable { public void Dispose() { } }

        private sealed class AssetDatabaseResourceLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object =>
                AssetDatabase.LoadAssetAtPath<T>($"Assets/Game/{path}.prefab");
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
            {
                onLoaded?.Invoke(LoadAsset<T>(path));
                return new ResourceLoadHandle(path);
            }
            public void Release(ResourceLoadHandle handle) { }
        }
    }
}
