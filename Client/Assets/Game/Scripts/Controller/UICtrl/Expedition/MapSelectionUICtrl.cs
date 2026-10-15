using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>呈现章节选择及单一下载/出发入口；解锁、资源缓存、切图及存档事务由领域系统裁决，不保存第二份进度。</summary>
    public sealed class MapSelectionUICtrl : BaseUICtrl
    {
        private enum SelectionMode
        {
            Offline,
            Online
        }

        private readonly List<MapDefinition> maps = new List<MapDefinition>();
        private readonly List<string> forbiddenItems=new List<string>();
        private readonly Dictionary<MapSelectionOptionBoxUI, string> rowMapIds =
            new Dictionary<MapSelectionOptionBoxUI, string>();
        private int operationGeneration;
        private bool operationPending;
        private SelectionMode mode;
        /// <summary>只保存当前展示选择；null 表示尚未配置的下一章，不是有效地图身份。</summary>
        private string selectedMapId;
        private bool nextChapterSelected;
        private OptionalGameplayResourceQuery selectedResources;
        /// <summary>属于本次可见窗口的下载取消令牌，隐藏或销毁后取消并拒绝过期回调。</summary>
        private CancellationTokenSource downloadCancellation;
        private bool downloading;
        /// <summary>最近失败的资源请求，仅影响重试文案；不代表持久化下载或地图解锁状态。</summary>
        private string failedDownloadMapId;

        private MapSelectionUI UI => BaseUI as MapSelectionUI;

        public override UILayer Layer() => UILayer.Dialog;
        public override bool BlocksGame => true;
        protected override BackgroundConfig GetBackgroundConfig() => new BackgroundConfig
        {
            ColorType = BackgroundColorType.Black, Alpha = .28f, ClickEffect = BackgroundClickEffect.None
        };

        protected override string Path() => "Expedition/MapSelection";

        public static bool TryShowOffline(out string error)
        {
            var architecture = Game.Interface;
            if (architecture == null)
            {
                error = "游戏架构不可用";
                return false;
            }

            try
            {
                var map = architecture.GetModel<MapSceneModel>();
                var save = architecture.GetModel<SaveModel>();
                if (map == null || map.Status.Value != MapLoadStatus.Loaded ||
                    !string.Equals(map.CurrentMapId, MapIds.Base, StringComparison.Ordinal) ||
                    map.CurrentMode != MapPreparationMode.OfflineFull ||
                    map.CurrentHandle == null || !map.CurrentHandle.IsValid || map.CurrentHandle.Context == null ||
                    save == null || save.CurrentDomain != OfflineSaveDomain.Base || save.CurrentBase == null)
                {
                    error = "当前不是稳定的离线基地状态";
                    return false;
                }

                var ctrl = UIHelper.ShowUI<MapSelectionUICtrl>();
                if (ctrl == null || !ctrl.IsShow())
                {
                    error = "地图选择界面加载失败";
                    return false;
                }

                return ctrl.TrySetMode(SelectionMode.Offline, out error);
            }
            catch (Exception exception)
            {
                error = "地图选择界面打开异常：" + exception.Message;
                return false;
            }
        }

        public static bool TryShowOnline(out string error)
        {
            var architecture = Game.Interface;
            if (architecture == null)
            {
                error = "游戏架构不可用";
                return false;
            }

            try
            {
                OnlineAccountSystem account = architecture.GetSystem<OnlineAccountSystem>();
                OnlineWorldSystem world = architecture.GetSystem<OnlineWorldSystem>();
                if (account == null || !account.Snapshot.IsAuthenticated || account.Snapshot.OperationState != OnlineOperationState.Idle)
                {
                    error = "当前联机账号状态不可进入地图";
                    return false;
                }
                if (world == null || world.Snapshot.OperationState != OnlineWorldOperationState.Idle || world.Snapshot.HasWorld)
                {
                    error = "当前联机世界状态不可选择地图";
                    return false;
                }

                MapSelectionUICtrl ctrl = UIHelper.ShowUI<MapSelectionUICtrl>();
                if (ctrl == null || !ctrl.IsShow())
                {
                    error = "地图选择界面加载失败";
                    return false;
                }

                return ctrl.TrySetMode(SelectionMode.Online, out error);
            }
            catch (Exception exception)
            {
                error = "地图选择界面打开异常：" + exception.Message;
                return false;
            }
        }

        protected override void OnInit()
        {
            UI.BtnClose.onClick.AddListener(Hide);
            UI.BtnAction.onClick.AddListener(ActOnSelection);
            UI.BtnAdjustSupplies.onClick.AddListener(()=>{if(IsShow()&&!operationPending)UIHelper.ShowUI<BagUICtrl>();});
            UI.ForbiddenList.AddItemRefreshListener((item,index)=>item.GetComponent<PreparationForbiddenItemBoxUI>().NameText.text=forbiddenItems[index]);
            UI.MapList.AddItemRefreshListener(RefreshRow);
            this.RegisterEvent<InventoryChangedEvent>(_ => RefreshIfVisible())
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ => RefreshIfVisible())
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<BasePreparationChangedEvent>(_=>RefreshIfVisible()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<ClothingEquipmentChangedEvent>(_=>RefreshIfVisible()).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            operationPending = false;
            downloading = false;
            failedDownloadMapId = null;
            downloadCancellation = new CancellationTokenSource();
            selectedMapId = MapIds.CivilisationValley;
            nextChapterSelected = false;
            Refresh();
            if (UI?.MapList != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)UI.MapList.transform);
            }

            UI?.MapScroll?.StopMovement();
            if (UI?.MapScroll != null)
            {
                UI.MapScroll.verticalNormalizedPosition = 1f;
            }
        }

        protected override void OnHide()
        {
            InvalidateAndClear();
        }

        protected override void OnUnload()
        {
            InvalidateAndClear();
        }

        private void RefreshIfVisible()
        {
            if (IsShow() && !operationPending)
            {
                Refresh();
            }
        }

        private void Refresh()
        {
            if (UI == null)
            {
                return;
            }

            ClearRowListeners();
            rowMapIds.Clear();
            maps.Clear();
            var catalog = MapCatalog.GetExpeditionMaps();
            if (mode == SelectionMode.Online) maps.AddRange(catalog);
            else
            {
                // 当前仅河谷是文明章节；null 卡片只预告下一章，其他远征不冒充历史时期。
                if (MapCatalog.TryGet(MapIds.CivilisationValley, out var valley)) maps.Add(valley);
                maps.Add(null);
                bool pendingShown = false;
                foreach (var definition in catalog)
                {
                    if (definition.MapId == MapIds.CivilisationValley) continue;
                    bool unlocked = this.GetModel<BaseMapProgressModel>().IsMapUnlocked(definition.MapId);
                    if (unlocked || !pendingShown) maps.Add(definition);
                    if (!unlocked) pendingShown = true;
                }
            }
            if (!nextChapterSelected && !maps.Exists(map => map != null && map.MapId == selectedMapId))
                selectedMapId = maps.Find(map => map != null)?.MapId;
            if (UI.MapList != null)
            {
                UI.MapList.totalCount = maps.Count;
                UI.MapList.RefreshCells();
            }

            SetInteractable(!operationPending);
            RenderDetail();
        }

        private void RefreshRow(Transform item, int index)
        {
            var box = item.GetComponent<MapSelectionOptionBoxUI>();
            if (box == null || index < 0 || index >= maps.Count)
            {
                return;
            }

            var definition = maps[index];
            box.BtnSelect.onClick.RemoveAllListeners();
            if (definition == null)
            {
                SetText(box.TextName, Loc.T("ui.map_selection.next_name"));
                SetText(box.TextDetail, Loc.T("ui.map_selection.next_chapter"));
                SetText(box.TextState, Loc.T("ui.map_selection.next_state"));
                box.Preview.gameObject.SetActive(false);
                box.SelectionFrame.SetActive(nextChapterSelected);
                box.BtnSelect.interactable = !operationPending;
                box.BtnSelect.onClick.AddListener(() => ChooseChapter(null));
                return;
            }
            rowMapIds[box] = definition.MapId;
            SetText(box.TextName, Loc.T(definition.DisplayNameKey));
            var detail = string.Empty;
            var taskConfigAvailable = mode == SelectionMode.Online || TryFormatTaskDetail(definition, out detail);
            SetText(box.TextDetail, !taskConfigAvailable ? Loc.T("ui.map_selection.task_config_unavailable") :
                definition.MapId == MapIds.CivilisationValley ? Loc.T("ui.map_selection.valley_chapter") : Loc.T("ui.map_selection.other_expedition"));
            box.Preview.gameObject.SetActive(definition.MapId == MapIds.CivilisationValley);
            box.SelectionFrame.SetActive(!nextChapterSelected && selectedMapId == definition.MapId);

            var unlocked = mode == SelectionMode.Online || this.GetModel<BaseMapProgressModel>().IsMapUnlocked(definition.MapId);
            SetText(box.TextState, mode == SelectionMode.Online ? string.Empty : !taskConfigAvailable ? Loc.T("ui.map_selection.state.unavailable") : unlocked ? Loc.T("ui.map_selection.state.unlocked") : FormatUnlockCost(definition));

            if (box.BtnSelect != null)
            {
                box.BtnSelect.onClick.RemoveAllListeners();
                var mapId = definition.MapId;
                box.BtnSelect.onClick.AddListener(() => ChooseChapter(mapId));
                box.BtnSelect.interactable = !operationPending && taskConfigAvailable;
            }
        }

        /// <summary>卡片仅切换当前详情；不扣除解锁物品，不下载，也不创建地图。</summary>
        private void ChooseChapter(string mapId)
        {
            if (operationPending || !IsShow()) return;
            selectedMapId = mapId;
            nextChapterSelected = mapId == null;
            Refresh();
        }

        /// <summary>从永久科技、地图解锁和当前资源清单重画选中项；占用期间保留真实下载进度，不发起领域事务。</summary>
        private void RenderDetail()
        {
            UI.PreparationRoot.SetActive(mode==SelectionMode.Offline&&!nextChapterSelected);
            bool valley = !nextChapterSelected && selectedMapId == MapIds.CivilisationValley;
            UI.Preview.gameObject.SetActive(valley);
            UI.TechRoot.SetActive(valley);
            if (nextChapterSelected || !MapCatalog.TryGet(selectedMapId ?? string.Empty, out var definition))
            {
                UI.SelectedName.text = Loc.T("ui.map_selection.next_name");
                UI.EraText.text = Loc.T("ui.map_selection.next_chapter");
                UI.StoryText.text = Loc.T("ui.map_selection.next_detail");
                UI.ResourceText.text = Loc.T("ui.map_selection.next_state");
                UI.ActionText.text = Loc.T("ui.map_selection.state.locked");
                UI.BtnAction.interactable = false;
                return;
            }
            UI.SelectedName.text = Loc.T(definition.DisplayNameKey);
            UI.EraText.text = Loc.T(valley ? "ui.map_selection.valley_era" : "ui.map_selection.other_expedition");
            bool available = mode == SelectionMode.Online || TryFormatTaskDetail(definition, out _);
            UI.StoryText.text = valley ? Loc.T("ui.map_selection.valley_story") :
                mode == SelectionMode.Online ? FormatLegacyDetail(definition) :
                TryFormatTaskDetail(definition, out var detail) ? detail : Loc.T("ui.map_selection.task_config_unavailable");
            if(mode==SelectionMode.Offline)
            {
                var load=this.GetSystem<CarryLoadSystem>().Query(selectedMapId);
                UI.StoryText.text=Loc.T(valley?"ui.base_prep.rules.traditional":"ui.base_prep.rules.unrestricted");
                UI.CarryWeightText.text=Loc.T("ui.base_prep.weight",load.Grams/1000.0,load.Limit/1000.0);
                forbiddenItems.Clear();forbiddenItems.AddRange(load.Forbidden);UI.ForbiddenList.totalCount=forbiddenItems.Count;UI.ForbiddenList.RefreshCells();
                UI.ForbiddenScroll.gameObject.SetActive(forbiddenItems.Count>0);
                UI.CarryStateText.text=load.Error??(load.Grams>load.Limit?Loc.T("ui.base_prep.overweight",load.Grams/1000.0,load.Limit/1000.0):Loc.T(forbiddenItems.Count>0?"ui.base_prep.blocked":"ui.base_prep.ready"));
            }
            // 章节科技进度统计永久知识，不再把干粮单项当作全章目标或通关条件。
            var technology = this.GetModel<CivilisationTechnologyModel>();
            if (this.GetUtility<IRuntimeConfigProvider>().TryGetCivilisationCatalog(out var catalog, out _))
                UI.TechnologyText.text = Loc.T("ui.map_selection.valley_technology",
                    catalog.Technologies.Count(t => technology.IsUnlocked(t.Id)), catalog.Technologies.Count) + "\n" +
                    Loc.T(technology.IsChapterCompleted("chapter.han-transport") ? "civilisation.workshop.completed" : "civilisation.workshop.ongoing");
            else UI.TechnologyText.text = Loc.T("ui.map_selection.task_config_unavailable");
            if (operationPending)
            {
                if (!downloading) UI.ActionText.text = Loc.T("ui.map_selection.action.processing");
                UI.BtnAction.interactable = false;
                return;
            }
            bool unlocked = mode == SelectionMode.Online || this.GetModel<BaseMapProgressModel>().IsMapUnlocked(selectedMapId);
            if (!available || !unlocked)
            {
                UI.ResourceText.text = available ? FormatUnlockCost(definition) : Loc.T("ui.map_selection.task_config_unavailable");
                UI.ActionText.text = Loc.T(available ? "ui.map_selection.action.unlock" : "ui.map_selection.action.unavailable");
                UI.BtnAction.interactable = available;
                return;
            }
            selectedResources = this.GetSystem<OptionalGameplayResourceSystem>().QueryMap(selectedMapId);
            bool ready = selectedResources.State == OptionalGameplayResourceState.Ready;
            bool needs = selectedResources.State == OptionalGameplayResourceState.NeedsDownload;
            UI.ResourceText.text = ready ? Loc.T(valley ? "ui.map_selection.resources.base_ready" : "ui.map_selection.resources.ready") :
                needs ? Loc.T("ui.map_selection.resources.size", ResourceDownloadSize.Format(selectedResources.TotalBytes)) : Loc.T("ui.optional.content.unavailable");
            UI.ActionText.text = Loc.T(ready ? "ui.map_selection.action.explore" : needs ?
                failedDownloadMapId == selectedMapId ? "ui.map_selection.action.retry_download" : "ui.map_selection.action.download" : "ui.map_selection.action.retry");
            UI.BtnAction.interactable = !ready||mode==SelectionMode.Online||this.GetSystem<CarryLoadSystem>().Query(selectedMapId).CanDepart;
        }

        /// <summary>唯一底部操作入口；未解锁地图沿用解锁事务，已解锁地图按当前展示的资源状态下载或出发。</summary>
        private void ActOnSelection()
        {
            if (operationPending || !IsShow() || nextChapterSelected || string.IsNullOrEmpty(selectedMapId)) return;
            bool unlocked = mode == SelectionMode.Online || this.GetModel<BaseMapProgressModel>().IsMapUnlocked(selectedMapId);
            if (!unlocked || selectedResources.State == OptionalGameplayResourceState.Ready) SelectAsync(selectedMapId).Forget();
            else DownloadSelectedAsync(selectedMapId).Forget();
        }

        /// <summary>显式点击下载后只准备本地图资源；成功后重新查询并留在弹窗，必须再次点击才能出发。</summary>
        private async UniTaskVoid DownloadSelectedAsync(string mapId)
        {
            operationPending = true;
            downloading = true;
            int generation = ++operationGeneration;
            var owner = BaseUI;
            var token = downloadCancellation.Token;
            SetInteractable(false);
            UI.ActionText.text = Loc.T("ui.map_selection.action.checking");
            var resources = this.GetSystem<OptionalGameplayResourceSystem>();
            try
            {
                var query = await resources.CheckMapAsync(mapId);
                if (!CanApplyResult(generation, owner) || token.IsCancellationRequested) return;
                if (query.State == OptionalGameplayResourceState.NeedsDownload)
                {
                    var result = await resources.DownloadMapAsync(mapId, query.Version, progress =>
                    {
                        if (!CanApplyResult(generation, owner) || token.IsCancellationRequested) return;
                        int percent = progress.TotalBytes <= 0 ? 0 : Mathf.Clamp((int)(progress.CurrentBytes * 100d / progress.TotalBytes), 0, 100);
                        UI.ActionText.text = Loc.T("ui.map_selection.action.downloading", percent);
                        UI.ResourceText.text = Loc.T("ui.map_selection.resources.progress",
                            ResourceDownloadSize.Format(progress.CurrentBytes), ResourceDownloadSize.Format(progress.TotalBytes));
                    }, token);
                    if (!CanApplyResult(generation, owner) || token.IsCancellationRequested) return;
                    failedDownloadMapId = result.Success ? null : mapId;
                    if (!result.Success) Notify.ShowFloatTips(Loc.T("ui.optional.content.failed"));
                }
                else if (query.State != OptionalGameplayResourceState.Ready)
                {
                    failedDownloadMapId = mapId;
                    Notify.ShowFloatTips(Loc.T("ui.optional.content.unavailable"));
                }
                else failedDownloadMapId = null;
            }
            catch (Exception exception)
            {
                if (CanApplyResult(generation, owner) && !token.IsCancellationRequested)
                {
                    failedDownloadMapId = mapId;
                    Notify.ShowFloatTips(Loc.T("ui.optional.content.failed"));
                    Log.Warning("[MapSelectionUICtrl] 下载失败：" + exception.Message);
                }
            }
            finally
            {
                // 旧请求不能解除新窗口的忙状态，或更新已经替换的选中地图。
                if (CanApplyResult(generation, owner))
                {
                    operationPending = false; downloading = false; Refresh();
                }
            }
        }

        private async UniTaskVoid SelectAsync(string mapId)
        {
            if (operationPending || !IsShow())
            {
                return;
            }

            if (mode == SelectionMode.Offline && !this.GetUtility<IRuntimeConfigProvider>().TryGetExpeditionTasks(mapId, out _, out _))
            {
                Notify.ShowFloatTips(Loc.T("ui.map_selection.error.task_config_unavailable"));
                Refresh();
                return;
            }

            operationPending = true;
            var generation = ++operationGeneration;
            var uiInstance = BaseUI;
            SetInteractable(false);
            RefreshButtonLabels();

            if (mode == SelectionMode.Online)
            {
                await SelectOnlineAsync(mapId, generation, uiInstance);
                return;
            }

            MapSelectionResult result;
            try
            {
                if (this.GetModel<BaseMapProgressModel>().IsMapUnlocked(mapId))
                {
                    if (this.GetSystem<OptionalGameplayResourceSystem>().QueryMap(mapId).State != OptionalGameplayResourceState.Ready)
                    {
                        if (CanApplyResult(generation, uiInstance)) { operationPending = false; Refresh(); }
                        return;
                    }
                }
                result = await this.GetSystem<BaseMapSelectionSystem>().InteractWithMapAsync(mapId);
            }
            catch (Exception exception)
            {
                if (!CanApplyResult(generation, uiInstance))
                {
                    return;
                }

                operationPending = false;
                SetInteractable(true);
                RefreshButtonLabels();
                Notify.ShowFloatTips(Loc.T("ui.map_selection.error.enter"));
                Log.Error($"[MapSelectionUICtrl] 地图选择异常：map={mapId} exception={exception}");
                return;
            }

            if (!CanApplyResult(generation, uiInstance))
            {
                return;
            }

            if (result.Success && result.EnteredMap)
            {
                Hide();
                UIHelper.ShowUI<ExpeditionHudUICtrl>();
                return;
            }

            operationPending = false;
            if (result.Success && result.UnlockedNow)
            {
                Refresh();
                return;
            }

            SetInteractable(true);
            RefreshButtonLabels();
            Notify.ShowFloatTips(Loc.T(ErrorKey(result.Code)));
            Log.Warning($"[MapSelectionUICtrl] 地图选择失败：map={mapId} code={result.Code} reason={result.Error}");
            if (this.GetSystem<ExpeditionFlowSystem>().HasPendingEntryRecovery)
                ShowEntryRecovery(generation, uiInstance, result.Error);
        }

        /// <summary>基地恢复失败时保留操作入口；按钮只提交恢复或离开意图，不直接修改地图状态。</summary>
        private void ShowEntryRecovery(int generation, BaseUI owner, string error)
        {
            if (!CanApplyResult(generation, owner)) return;
            SetInteractable(false);
            Notify.ShowMessage(Loc.T("ui.map_selection.recovery.title"),
                Loc.T("ui.map_selection.recovery.content") + "\n" + error,
                new List<MessageButtonConfig>
                {
                    new MessageButtonConfig { ButtonText = Loc.T("ui.expedition.retry_return"), Role = MessageButtonRole.Confirm,
                        OnClick = () => RecoverEntryAsync(generation, owner, false).Forget() },
                    new MessageButtonConfig { ButtonText = Loc.T("ui.pause.main_menu"), Role = MessageButtonRole.Cancel,
                        OnClick = () => RecoverEntryAsync(generation, owner, true).Forget() }
                });
        }

        /// <summary>异步完成后只更新原界面；失败重新显示可重试对话框，成功结束错误现场。</summary>
        private async UniTask RecoverEntryAsync(int generation, BaseUI owner, bool leave)
        {
            if (!CanApplyResult(generation, owner) || operationPending) return;
            operationPending = true;
            ExpeditionFlowResult result;
            try
            {
                var flow = this.GetSystem<ExpeditionFlowSystem>();
                result = leave ? await flow.LeaveFailedEntryAsync() : await flow.RetryEntryRecoveryAsync();
            }
            catch (Exception exception) { result = ExpeditionFlowResult.Fail(exception.Message); }
            if (!CanApplyResult(generation, owner)) return;
            operationPending = false;
            if (!result.Success)
            {
                Log.Warning($"[MapSelectionUICtrl] 进入失败现场恢复失败：{result.Error}");
                ShowEntryRecovery(generation, owner, result.Error);
                return;
            }
            Hide();
            if (leave)
            {
                UIHelper.HideUI<ExpeditionHudUICtrl>();
                UIHelper.HideUI<PlayerConditionHudUICtrl>();
                UIHelper.HideUI<GameplayGuideHudUICtrl>();
                UIHelper.ShowUI<StartUICtrl>();
            }
        }

        private async UniTask SelectOnlineAsync(string mapId, int generation, BaseUI uiInstance)
        {
            OnlineWorldOperationResult result;
            try
            {
                var account = this.GetSystem<OnlineAccountSystem>();
                var identity = account?.Snapshot ?? OnlineAccountSnapshot.Empty();
                bool IsCurrentSession() => CanApplyResult(generation, uiInstance) && account != null &&
                    account.Snapshot.IsAuthenticated && account.Snapshot.AccountId == identity.AccountId &&
                    account.Snapshot.SessionEpoch == identity.SessionEpoch;
                if (!IsCurrentSession() || this.GetSystem<OptionalGameplayResourceSystem>().QueryMap(mapId).State != OptionalGameplayResourceState.Ready)
                {
                    if (CanApplyResult(generation, uiInstance)) { operationPending = false; Refresh(); }
                    return;
                }
                result = await this.GetSystem<OnlineWorldSystem>().EnterWorldAsync(mapId);
            }
            catch (Exception exception)
            {
                if (!CanApplyResult(generation, uiInstance)) return;
                operationPending = false;
                SetInteractable(true);
                RefreshButtonLabels();
                Notify.ShowFloatTips(OnlineWorldText.Result(OnlineWorldResultCode.UnknownFailure));
                Log.Error($"[MapSelectionUICtrl] 联机地图选择异常：map={mapId} exception={exception}");
                return;
            }

            if (!CanApplyResult(generation, uiInstance)) return;
            if (result.Success && result.Snapshot.HasWorld)
            {
                Hide();
                UIHelper.HideUI<OnlineRoomUICtrl>();
                UIHelper.HideUI<OnlineLoginUICtrl>();
                UIHelper.HideUI<StartUICtrl>();
                return;
            }

            operationPending = false;
            SetInteractable(true);
            RefreshButtonLabels();
            Notify.ShowFloatTips(OnlineWorldText.Result(result.Code));
        }

        private bool CanApplyResult(int generation, BaseUI uiInstance)
        {
            return generation == operationGeneration && ReferenceEquals(BaseUI, uiInstance) && IsShow();
        }

        private void SetInteractable(bool interactable)
        {
            UI.BtnAction.interactable = interactable && !nextChapterSelected;
            if (UI?.BtnClose != null)
            {
                UI.BtnClose.interactable = interactable;
            }

            if (UI?.MapList == null)
            {
                return;
            }

            foreach (Transform child in UI.MapList.transform)
            {
                var box = child.GetComponent<MapSelectionOptionBoxUI>();
                var button = box?.BtnSelect;
                if (button != null)
                {
                    var configAvailable = !rowMapIds.TryGetValue(box, out var mapId) || mode == SelectionMode.Online ||
                                          this.GetUtility<IRuntimeConfigProvider>().TryGetExpeditionTasks(mapId, out _, out _);
                    button.interactable = interactable && configAvailable;
                }
            }
        }

        private void RefreshButtonLabels()
        {
            RenderDetail();
        }

        private bool TryFormatTaskDetail(MapDefinition definition, out string detail)
        {
            detail = string.Empty;
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetExpeditionTasks(definition.MapId, out var tasks, out _)) return false;
            // 文明远征出发前明确当次物资边界；规则仍由结算 System 执行。
            if (definition.MapId == MapIds.CivilisationValley)
            {
                detail = Loc.T("civilisation.departure.detail");
                return true;
            }
            var typeKey = definition.Kind == MapKind.Survival ? "ui.map_selection.type.survival" : "ui.map_selection.type.task";
            if (tasks.Count == 0) { detail = Loc.T(typeKey); return true; }
            var task = tasks[0]; var localizedName = Loc.T(task.NameKey);
            var name = string.IsNullOrWhiteSpace(localizedName) || localizedName == task.NameKey ? task.Name : localizedName;
            detail = task.ObjectiveType == global::TaskObjectiveType.Survive
                ? Loc.T("ui.map_selection.objective.survive", name, ExpeditionClockRules.FormatDuration(task.TargetAmount))
                : Loc.T("ui.map_selection.objective.kill", name, UnityEngine.Mathf.FloorToInt(task.TargetAmount));
            return true;
        }

        private static string FormatDayTime(float gameMinutes)
        {
            var total = UnityEngine.Mathf.FloorToInt(gameMinutes); var day = total / (int)PlayerConditionRules.GameMinutesPerDay + 1;
            var minute = total % (int)PlayerConditionRules.GameMinutesPerDay;
            return Texts.ExpeditionDayClock(day, $"{minute / 60:00}:{minute % 60:00}");
        }

        private string FormatLegacyDetail(MapDefinition definition)
        {
            var typeKey = definition.Kind == MapKind.Survival
                ? "ui.map_selection.type.survival"
                : "ui.map_selection.type.task";
            var target = string.Empty;
            if (definition.SurvivalTargetGameMinutes > 0f)
            {
                target = Loc.T("ui.map_selection.target.survive_minutes", definition.SurvivalTargetGameMinutes);
            }
            else if (definition.TaskTargetKills > 0)
            {
                target = Loc.T("ui.map_selection.target.kills", definition.TaskTargetKills);
            }

            return string.IsNullOrEmpty(target) ? Loc.T(typeKey) : Loc.T(typeKey) + "  " + target;
        }

        private string FormatUnlockCost(MapDefinition definition)
        {
            var itemName = definition.UnlockCostItemId.ToString();
            if (this.GetUtility<IRuntimeConfigProvider>().TryGetItem(definition.UnlockCostItemId, out var item, out _))
            {
                var localized = Loc.T(item.NameKey);
                itemName = string.IsNullOrWhiteSpace(localized) || string.Equals(localized, item.NameKey, StringComparison.Ordinal)
                    ? item.Name
                    : localized;
            }

            return Loc.T("ui.map_selection.state.unlock_cost", itemName, definition.UnlockCostAmount);
        }

        private static string ErrorKey(MapSelectionResultCode code)
        {
            switch (code)
            {
                case MapSelectionResultCode.UnknownMap:
                    return "ui.map_selection.error.unknown_map";
                case MapSelectionResultCode.NotExpeditionMap:
                    return "ui.map_selection.error.not_expedition";
                case MapSelectionResultCode.UnlockCostInsufficient:
                    return "ui.map_selection.error.cost";
                default:
                    return "ui.map_selection.error.enter";
            }
        }

        private void InvalidateAndClear()
        {
            downloadCancellation?.Cancel();
            downloadCancellation?.Dispose();
            downloadCancellation = null;
            operationGeneration++;
            operationPending = false;
            ClearRowListeners();
            rowMapIds.Clear();
            maps.Clear();
        }

        private bool TrySetMode(SelectionMode requested, out string error)
        {
            if (operationPending && mode != requested)
            {
                error = "地图选择操作进行中";
                return false;
            }

            mode = requested;
            if (IsShow() && !operationPending) Refresh();
            error = null;
            return true;
        }

        private void ClearRowListeners()
        {
            if (UI?.MapList == null)
            {
                return;
            }

            foreach (Transform child in UI.MapList.transform)
            {
                child.GetComponent<MapSelectionOptionBoxUI>()?.BtnSelect?.onClick.RemoveAllListeners();
            }
        }

        private static void SetText(TextMeshProUGUI target, string value)
        {
            if (target != null)
            {
                target.text = value ?? string.Empty;
            }
        }
    }
}
