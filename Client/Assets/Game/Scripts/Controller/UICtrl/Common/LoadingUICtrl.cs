using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>呈现地图加载或资源下载进度；下载终态前保持可见，不负责下载事务或玩法状态。</summary>
    public sealed class LoadingUICtrl : BaseUICtrl
    {
        private enum DisplayMode { Map, ResourceDownload }

        private DisplayMode displayMode;
        private int downloadRequestId;
        /// <summary>下载事务仍在执行；隐藏或销毁 View 不改变该表现请求的有效期。</summary>
        private bool downloadActive;
        /// <summary>玩家已收起弹窗或转入地图加载；完成后不能强制打开原 NPC 界面。</summary>
        private bool downloadDismissed;
        private bool mapLoadingActive;
        /// <summary>开局资源检查到战斗初始化的表现请求；地图完成后也等待入口释放。</summary>
        private bool entryPreparing;
        /// <summary>隔离旧入口的异步清理，避免关闭后来发起的加载请求。</summary>
        private int entryRequestId;
        /// <summary>基础场景完成后的地图内容、相机与安全节点准备，独立于场景加载进度。</summary>
        private bool arcadePreparationActive;
        /// <summary>当前准备快照允许重试；不把普通结算保存失败当作地图失败。</summary>
        private bool arcadePreparationFailed;
        /// <summary>返回菜单期间继续遮挡未完成现场，阻止重复点击。</summary>
        private bool returningFromPreparation;
        /// <summary>地图准备请求代次；旧返回回调不能覆盖同一 View 上的新请求。</summary>
        private int preparationVersion;
        /// <summary>最近地图请求是否为基地；下载覆盖显示期间仍保留，结束后恢复。</summary>
        private bool baseMapLoading;
        /// <summary>最近地图进度，范围 0～1；下载覆盖期间继续由地图事件更新。</summary>
        private float mapProgress;
        private string loadingMapId;
        private MapLoadingStage? mapStage;
        private string mapError;
        /// <summary>地图事务返回的原始失败原因；语言切换只重绘标题，不丢弃诊断内容。</summary>
        private string mapFailureReason;
        /// <summary>本次地图加载开始的本机时间；复制时仅选取此后已收集的日志。</summary>
        private DateTime mapLoadingStartedAt;
        /// <summary>当前失败已复制的表现状态；新请求或新失败清空，语言切换保留。</summary>
        private bool failureLogsCopied;
        /// <summary>当前请求的实际字节与文件进度；总字节未取得时只显示准备状态。</summary>
        private OptionalGameplayDownloadProgress downloadProgress;
        private LoadingUI UI => BaseUI as LoadingUI;
        /// <summary>开局准备、地图加载或下载执行期间禁止普通关闭入口隐藏遮罩。</summary>
        protected override bool CanHide() => !downloadActive && !entryPreparing && !mapLoadingActive &&
            !arcadePreparationActive && !returningFromPreparation;
        public bool ShouldResumeDownloadEntry => downloadActive && !downloadDismissed;

        public override UILayer Layer()
        {
            return UILayer.Top;
        }

        protected override string Path()
        {
            return "Common/Loading";
        }

        protected override void OnInit()
        {
            UI.BtnBack.onClick.AddListener(OnBackClicked);
            UI.BtnRetry.onClick.AddListener(OnRetryPreparationClicked);
            UI.BtnCopyLog.onClick.AddListener(OnCopyLogClicked);
            UI.BtnDownloadClose.onClick.AddListener(Hide);
            this.RegisterEvent<MapLoadingStartedEvent>(OnLoadingStarted)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapLoadingStageEvent>(OnStageChanged)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapLoadingProgressEvent>(OnProgress)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapLoadingCompletedEvent>(OnLoadingCompleted)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapLoadingFailedEvent>(OnFailed)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ => RefreshPresentation())
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<ArcadeCombatHudChangedEvent>(OnArcadePreparationChanged)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapCurrentReleasedEvent>(evt =>
            {
                // 返回事务在卸载后还要读取安全节点，释放事件不能提前丢失失败恢复入口。
                if (evt.MapId == MapIds.Arcade && !returningFromPreparation) ClearArcadePreparation();
            }).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<ArcadeRunChangedEvent>(evt =>
            {
                RefreshArtwork();
                if (evt.Phase != ArcadeRunPhase.Arena && evt.Phase != ArcadeRunPhase.Challenge)
                    ClearArcadePreparation();
            }).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<OnlineArcadeRunChangedEvent>(_ => RefreshArtwork())
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        /// <summary>资源和安全节点仍在准备时保持顶层阻断；终态快照才释放战斗画面。</summary>
        private void OnArcadePreparationChanged(ArcadeCombatHudChangedEvent evt)
        {
            if (evt.Snapshot == null) return;
            if (!evt.Snapshot.IslandTransitioning)
            {
                ClearArcadePreparation();
                return;
            }
            if (!arcadePreparationActive)
            {
                preparationVersion++;
                returningFromPreparation = false;
                loadingMapId = MapIds.Arcade;
                mapLoadingStartedAt = DateTime.Now;
            }
            arcadePreparationActive = true;
            arcadePreparationFailed = evt.Snapshot.CanRetryCompletion;
            mapFailureReason = evt.Snapshot.IslandPreparationError;
            mapError = arcadePreparationFailed ? FormatMapFailure() : null;
            failureLogsCopied = false;
            displayMode = DisplayMode.Map;
            // 普通局内切关由黑幕接管；首次进局和失败重试仍使用完整加载页。
            if(!arcadePreparationFailed&&!mapLoadingActive&&!entryPreparing)
            {
                // 失败页已显示时，重试仍须清除旧错误；普通切轮继续只使用黑幕。
                if(IsShow())RefreshPresentation();
                return;
            }
            Show();
            RefreshPresentation();
        }

        /// <summary>结束、卸载或准备成功时只清理地图准备所持有的显示状态。</summary>
        private void ClearArcadePreparation()
        {
            if (!arcadePreparationActive) return;
            arcadePreparationActive = arcadePreparationFailed = false;
            mapError = mapFailureReason = null;
            RefreshPresentation();
            if (!mapLoadingActive && !entryPreparing) Hide();
        }

        /// <summary>失败现场必须先完成地图卸载及安全节点恢复，才能露出主菜单。</summary>
        private void OnBackClicked()
        {
            if (arcadePreparationActive)
            {
                if (arcadePreparationFailed && !returningFromPreparation) ReturnFromPreparationAsync().Forget();
                return;
            }
            Hide();
        }

        /// <summary>加载页提交专用重试意图，规则与候选状态仍由战斗系统裁决。</summary>
        private void OnRetryPreparationClicked()
        {
            if (!arcadePreparationActive || !arcadePreparationFailed || returningFromPreparation) return;
            if (this.GetModel<MapSceneModel>().CurrentMapId != MapIds.Arcade)
            {
                // 地图已释放时重试剩余的安全节点恢复，不能向旧现场重建资源。
                ReturnFromPreparationAsync().Forget();
                return;
            }
            this.GetSystem<ArcadeCombatSystem>().TryRetryMapPreparation();
        }

        /// <summary>异步返回只刷新发起请求的 View；返回期间保持顶层输入阻断。</summary>
        private async UniTask ReturnFromPreparationAsync()
        {
            var owner = UI;
            int version = preparationVersion;
            returningFromPreparation = true;
            RefreshPresentation();
            try
            {
                var result = await this.GetSystem<ArcadeSceneFlowSystem>().ReturnFromMapPreparationAsync();
                if (owner == null || UI != owner || version != preparationVersion) return;
                if (!result.Success)
                {
                    mapFailureReason = result.Error;
                    mapError = FormatMapFailure();
                }
            }
            finally
            {
                if (owner != null && UI == owner && version == preparationVersion)
                {
                    returningFromPreparation = false;
                    RefreshPresentation();
                    if (!arcadePreparationActive && string.IsNullOrEmpty(mapError)) Hide();
                }
            }
        }

        protected override void OnShow()
        {
            RefreshPresentation();
            UI.BtnBack.interactable = !returningFromPreparation && (CanHide() || arcadePreparationFailed);
        }

        protected override void OnHide()
        {
            if (displayMode == DisplayMode.ResourceDownload && downloadActive) downloadDismissed = true;
            if (UnityEngine.EventSystems.EventSystem.current != null)
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        }

        /// <summary>销毁后的 View 不再接收旧返回结果；新实例依据最新准备快照重新显示。</summary>
        protected override void OnUnload()
        {
            preparationVersion++;
            returningFromPreparation = false;
        }

        private void OnStageChanged(MapLoadingStageEvent evt)
        {
            if(!mapLoadingActive||evt.MapId!=loadingMapId)return;
            mapStage=evt.Stage;
            if(displayMode==DisplayMode.Map)RenderMapStage();
        }

        private void RenderMapStage()
        {
            if (arcadePreparationActive) { UI.MessageText.text = Loc.T("ui.arcade.island.loading"); return; }
            if(!mapStage.HasValue){UI.MessageText.text=Loc.T("ui.loading.scene_status");return;}
            UI.MessageText.text=Loc.T("ui.loading.stage."+mapStage.Value);
        }

        private void OnLoadingStarted(MapLoadingStartedEvent evt)
        {
            preparationVersion++;
            arcadePreparationActive = arcadePreparationFailed = returningFromPreparation = false;
            loadingMapId=evt.MapId;mapStage=null;mapFailureReason=null;
            mapLoadingStartedAt=DateTime.Now;
            failureLogsCopied=false;
            mapLoadingActive = true;
            baseMapLoading = false;
            mapProgress = 0f;
            mapError = null;
            if (downloadActive) downloadDismissed = true;
            displayMode = DisplayMode.Map;
            Show();
            // 已显示时 Show 不触发 OnShow，必须刷新最新地图身份和进度。
            RefreshPresentation();
            UI.ErrorRoot.SetActive(false);
            UI.BtnBack.interactable = false;
        }

        /// <summary>点击开局立即显示现有地图加载界面；先订阅事件，再交接后续地图进度。</summary>
        public int BeginEntryPreparation(string mapId)
        {
            entryPreparing = true;
            int requestId = ++entryRequestId;
            OnLoadingStarted(new MapLoadingStartedEvent(mapId));
            mapLoadingActive = false;
            return requestId;
        }

        /// <summary>仅释放对应入口的遮罩；地图失败详情保留，玩家可返回原菜单重试。</summary>
        public void EndEntryPreparation(int requestId)
        {
            if (!entryPreparing || requestId != entryRequestId) return;
            entryPreparing = false;
            if (UI == null) return;
            RefreshPresentation();
            if (!mapLoadingActive && string.IsNullOrEmpty(mapError)) Hide();
        }

        private void OnLoadingCompleted(MapLoadingCompletedEvent evt)
        {
            if (evt.MapId != loadingMapId) return;
            if(mapStage.HasValue)RenderMapStage();
            mapLoadingActive = false;
            if (!arcadePreparationActive) mapError = null;
            if (displayMode == DisplayMode.ResourceDownload) return;
            RefreshPresentation();
            Hide();
        }

        private void OnProgress(MapLoadingProgressEvent e)
        {
            if (e.MapId != loadingMapId) return;
            mapProgress = e.Progress;
            if (displayMode == DisplayMode.Map) SetMapProgress(e.Progress);
        }

        private void OnFailed(MapLoadingFailedEvent e)
        {
            if (e.MapId != loadingMapId) return;
            Log.Error($"[LoadingUICtrl] 地图加载失败：{e.Error}");
            if(mapStage.HasValue)RenderMapStage();
            mapLoadingActive = false;
            failureLogsCopied=false;
            mapFailureReason=e.Error;
            mapError=FormatMapFailure();
            if (displayMode == DisplayMode.ResourceDownload) return;
            ShowError(mapError);
            UI.BtnBack.interactable = CanHide();
        }

        /// <summary>空原因保留通用提示，实际原因作为动态文本展示并保留在下载切换后的失败快照中。</summary>
        private string FormatMapFailure() => string.IsNullOrWhiteSpace(mapFailureReason)
            ? Texts.LoadingErrorMapFailed
            : Loc.T("ui.loading.error.map_failed_detail",Texts.LoadingErrorMapFailed,mapFailureReason);

        private void SetMapProgress(float value)
        {
            var progress = UnityEngine.Mathf.Clamp01(value);
            // 场景百分比不能代表后续内容与安全节点进度；此阶段只展示真实等待状态。
            UI.ProgressSlider.gameObject.SetActive(!arcadePreparationActive);
            UI.PercentText.gameObject.SetActive(!arcadePreparationActive);
            UI.ProgressSlider.value = progress;
            UI.PercentText.text = $"{(int)(progress * 100f)}%";
            RenderMapStage();
        }

        /// <summary>按当前显示所有者恢复背景、固定标题和动态进度，支持重复 Show 与语言切换。</summary>
        private void RefreshPresentation()
        {
            if(!string.IsNullOrEmpty(mapError))
            {
                mapError=FormatMapFailure();UI.ErrorText.text=mapError;
            }
            UI.MapRoot.SetActive(displayMode == DisplayMode.Map);
            UI.DownloadRoot.SetActive(displayMode == DisplayMode.ResourceDownload);
            UI.ErrorRoot.SetActive(displayMode == DisplayMode.Map && !string.IsNullOrEmpty(mapError));
            UI.BtnBack.interactable = !returningFromPreparation && (CanHide() || arcadePreparationFailed);
            UI.BtnRetry.gameObject.SetActive(displayMode == DisplayMode.Map && arcadePreparationFailed);
            UI.BtnRetry.interactable = !returningFromPreparation;
            RefreshCopyPresentation();
            var isBase = displayMode == DisplayMode.Map && baseMapLoading;
            UI.BaseArtwork.SetActive(isBase);
            UI.MapArtwork.SetActive(!isBase);
            UI.BaseTitle.SetActive(isBase);
            UI.MapTitle.SetActive(!isBase);
            RefreshArtwork();
            if (displayMode == DisplayMode.Map) SetMapProgress(mapProgress);
            else RenderDownloadProgress();
        }

        /// <summary>读取权威场地身份而非关卡序号；下载、非街机及缺失身份使用默认图。</summary>
        private void RefreshArtwork()
        {
            int arena = 0;
            if (displayMode == DisplayMode.Map && loadingMapId == MapIds.Arcade)
            {
                var online = this.GetModel<OnlineArcadeRunModel>()?.Snapshot.Value ?? OnlineArcadeRunClientSnapshot.Empty();
                if (online.HasRun && online.Phase is OnlineArcadeRunPhase.Arena or OnlineArcadeRunPhase.Challenge && online.Battle != null)
                    arena = online.Battle.ArenaConfigNumber;
                else
                {
                    var run = this.GetModel<ArcadeRunModel>()?.Current;
                    if (run != null && !run.HasResult)
                        // 旧顺序局的关卡序号不等于场地编号；缺失真实身份时由View回退默认图。
                        arena = run.IsCasualRoute ? run.ArenaConfigNumber : 0;
                }
            }
            UI.LoadingArtwork.ShowArena(arena);
        }

        private void RenderDownloadProgress()
        {
            bool sizeKnown = downloadProgress.TotalBytes > 0;
            UI.DownloadSlider.gameObject.SetActive(sizeKnown);
            UI.DownloadPercentText.gameObject.SetActive(sizeKnown);
            var ratio = downloadProgress.TotalBytes <= 0 ? 0f :
                UnityEngine.Mathf.Clamp01((float)downloadProgress.CurrentBytes / downloadProgress.TotalBytes);
            UI.DownloadSlider.value = ratio;
            UI.DownloadPercentText.text = $"{(int)(ratio * 100f)}%";
            UI.DownloadMessageText.text = sizeKnown ? Loc.T("ui.loading.download_status",
                ResourceDownloadSize.Format(downloadProgress.CurrentBytes), ResourceDownloadSize.Format(downloadProgress.TotalBytes))
                : Loc.T("ui.optional.expedition.preparing");
        }

        /// <summary>开始一份表现请求，取得真实总大小后才显示进度圈；关闭弹窗不会取消下载。</summary>
        public void BeginResourceDownload(int requestId, OptionalGameplayDownloadProgress initialProgress = default)
        {
            displayMode = DisplayMode.ResourceDownload;
            downloadRequestId = requestId;
            downloadActive = true;
            downloadDismissed = false;
            downloadProgress = initialProgress;
            Show();
            if (UI == null) return;
            UI.BtnBack.interactable = false;
            UI.ErrorRoot.SetActive(false);
            RefreshPresentation();
            if (downloadProgress.TotalBytes > 0)
                UIHelper.ShowUI<ResourceDownloadHudUICtrl>().SetProgress(requestId, downloadProgress, ReopenResourceDownload);
        }

        public void SetResourceDownloadProgress(int requestId, OptionalGameplayDownloadProgress progress)
        {
            if (!downloadActive || requestId != downloadRequestId || progress.TotalBytes <= 0) return;
            downloadProgress = progress;
            if (UI != null && displayMode == DisplayMode.ResourceDownload) RenderDownloadProgress();
            var hud = UIHelper.GetUICtrl<ResourceDownloadHudUICtrl>() ?? UIHelper.ShowUI<ResourceDownloadHudUICtrl>();
            hud.SetProgress(requestId, progress, ReopenResourceDownload);
        }

        /// <summary>从进度圈恢复当前下载快照；地图正在加载时保留全屏加载显示。</summary>
        private void ReopenResourceDownload()
        {
            if (!downloadActive || mapLoadingActive) return;
            displayMode = DisplayMode.ResourceDownload;
            Show();
            RefreshPresentation();
        }

        /// <summary>只结束匹配的下载显示；地图加载已开始时恢复其最新状态。</summary>
        public void EndResourceDownload(int requestId, bool success)
        {
            if (!downloadActive || requestId != downloadRequestId) return;
            downloadActive = false;
            UIHelper.GetUICtrl<ResourceDownloadHudUICtrl>()?.End(requestId);
            downloadRequestId = 0;
            if (UI != null)
            {
                UI.BtnBack.interactable = true;
                RefreshPresentation();
            }
            if (mapLoadingActive)
            {
                displayMode = DisplayMode.Map;
                Show();
                RefreshPresentation();
                SetMapProgress(mapProgress);
                return;
            }
            if (!string.IsNullOrEmpty(mapError))
            {
                displayMode = DisplayMode.Map;
                ShowError(mapError);
                return;
            }
            Hide();
            if (!success) Notify.ShowFloatTips(Loc.T("ui.optional.expedition.failed"));
        }

        /// <summary>仅复制当前失败现场；原始错误独立附加，避免 Unity 日志批处理延迟造成漏报。</summary>
        private void OnCopyLogClicked()
        {
            if (displayMode != DisplayMode.Map || string.IsNullOrEmpty(mapError)) return;
            var logs=this.GetModel<RuntimeLogModel>().Snapshot()
                .Where(entry=>entry.Timestamp>=mapLoadingStartedAt);
            var report=$"LoadingStartedAt: {mapLoadingStartedAt:O}\nLoadingStage: {mapStage?.ToString() ?? "Scene"}\nFailure: {mapFailureReason ?? mapError}\n\n"+
                RuntimeLogTextFormatter.Format(logs,this.GetModel<AppStateModel>().CurrentState.Value,loadingMapId);
            GUIUtility.systemCopyBuffer=report;
            failureLogsCopied=true;
            RefreshCopyPresentation();
        }

        /// <summary>复制入口仅对地图失败可见；成功反馈单独呈现，不覆盖错误详情。</summary>
        private void RefreshCopyPresentation()
        {
            var available=displayMode==DisplayMode.Map && !string.IsNullOrEmpty(mapError);
            UI.BtnCopyLog.gameObject.SetActive(available);
            UI.CopyStatusText.text=available && failureLogsCopied ? Loc.T("ui.loading.error.logs_copied") : string.Empty;
        }

        public void ShowError(string localizedMessage)
        {
            Show();
            UI.ErrorRoot.SetActive(true);
            UI.ErrorText.text = localizedMessage ?? string.Empty;
            RefreshCopyPresentation();
        }
    }
}
