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
        /// <summary>场景之外的实际入场步骤进度与提示；只接受当前入口令牌。</summary>
        private float entryProgress;
        private string entryStageKey;
        /// <summary>入口准备期固定幸存者身份，不能借用残留闯关或联机存档选图。</summary>
        private bool survivorEntryArtwork;
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
        protected override bool CanHide() => !downloadActive && !entryPreparing && !mapLoadingActive;
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
            UI.BtnBack.onClick.AddListener(Hide);
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
        }

        protected override void OnShow()
        {
            RefreshPresentation();
            UI.BtnBack.interactable = CanHide();
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
            survivorEntryArtwork=false;
        }

        private void OnStageChanged(MapLoadingStageEvent evt)
        {
            if(!mapLoadingActive||evt.MapId!=loadingMapId)return;
            mapStage=evt.Stage;
            if(displayMode==DisplayMode.Map)RenderMapStage();
        }

        private void RenderMapStage()
        {
            if (entryPreparing && !mapLoadingActive && !string.IsNullOrEmpty(entryStageKey))
            { UI.MessageText.text = Loc.T(entryStageKey); return; }
            if(!mapStage.HasValue){UI.MessageText.text=Loc.T("ui.loading.scene_status");return;}
            UI.MessageText.text=Loc.T("ui.loading.stage."+mapStage.Value);
        }

        private void OnLoadingStarted(MapLoadingStartedEvent evt)
        {
            if(!entryPreparing||evt.MapId!=MapIds.Arcade)survivorEntryArtwork=false;
            loadingMapId=evt.MapId;mapStage=null;mapFailureReason=null;
            mapLoadingStartedAt=DateTime.Now;
            failureLogsCopied=false;
            mapLoadingActive = true;
            baseMapLoading = false;
            mapProgress = 0f;
            entryProgress = 0f;
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
            =>BeginEntryPreparationCore(mapId,false);

        /// <summary>幸存者从资源准备起使用独立背景；新请求接管后旧令牌不能清理新背景。</summary>
        public int BeginSurvivorEntryPreparation()=>BeginEntryPreparationCore(MapIds.Arcade,true);

        private int BeginEntryPreparationCore(string mapId,bool survivor)
        {
            survivorEntryArtwork=survivor;
            entryPreparing = true;
            entryProgress = 0f;
            entryStageKey = "ui.optional.expedition.preparing";
            int requestId = ++entryRequestId;
            OnLoadingStarted(new MapLoadingStartedEvent(mapId));
            mapLoadingActive = false;
            return requestId;
        }

        /// <summary>真实步骤完成后推进入口进度；旧令牌和无效数值不得覆盖新请求。</summary>
        public void ReportEntryPreparation(int requestId, float progress, string stageKey)
        {
            if (!entryPreparing || requestId != entryRequestId || float.IsNaN(progress) || float.IsInfinity(progress)) return;
            entryProgress = Mathf.Max(entryProgress, Mathf.Clamp(progress, 0f, .99f));
            entryStageKey = stageKey;
            if (UI != null) RefreshPresentation();
        }

        /// <summary>仅释放对应入口的遮罩；地图失败详情保留，玩家可返回原菜单重试。</summary>
        public void EndEntryPreparation(int requestId, bool succeeded = true)
        {
            if (!entryPreparing || requestId != entryRequestId) return;
            entryPreparing = false;
            // 失败清理不冒充成功：可以返回菜单，但不能补出100%。
            if (!succeeded) mapProgress = Mathf.Min(mapProgress, .99f);
            if(!mapLoadingActive&&string.IsNullOrEmpty(mapError))survivorEntryArtwork=false;
            if (UI == null) return;
            RefreshPresentation();
            if (!mapLoadingActive && string.IsNullOrEmpty(mapError)) Hide();
        }

        private void OnLoadingCompleted(MapLoadingCompletedEvent evt)
        {
            if (evt.MapId != loadingMapId) return;
            if(mapStage.HasValue)RenderMapStage();
            mapLoadingActive = false;
            mapProgress = 1f;
            if (entryPreparing)
            {
                entryProgress = Mathf.Max(entryProgress, .8f);
                entryStageKey = "ui.arcade.island.loading";
            }
            mapError = null;
            if(!entryPreparing&&string.IsNullOrEmpty(mapError))survivorEntryArtwork=false;
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
            // 地图占前80%，后续场地和入口持有者全部释放才允许100%；不按时间伪推进。
            if (!CanHide() || !string.IsNullOrEmpty(mapError))
                progress = Mathf.Min(.99f, Mathf.Max(progress * .8f, entryProgress));
            UI.ProgressSlider.gameObject.SetActive(true);
            UI.PercentText.gameObject.SetActive(true);
            UI.ProgressSlider.value = progress;
            // 消除阶段常量的浮点截断误差，未完成仍严格小于100%。
            int percent = progress >= 1f ? 100 : Mathf.Min(99, Mathf.FloorToInt(progress * 100f + .0001f));
            UI.PercentText.text = $"{percent}%";
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
            UI.BtnBack.interactable = CanHide();
            UI.BtnRetry.gameObject.SetActive(false);
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
            // 资源下载覆盖及地图进度事件不能把幸存者插画换成旧存档的关卡图。
            if(survivorEntryArtwork&&loadingMapId==MapIds.Arcade){UI.LoadingArtwork.ShowSurvivor();return;}
            UI.LoadingArtwork.ShowArena(0);
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
            if (!success) Notify.ShowFloatTips(Loc.T("ui.optional.content.failed"));
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
