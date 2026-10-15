using System;
using System.Linq;
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
        /// <summary>下载门禁结束前禁止普通关闭入口隐藏正在执行的准备事务。</summary>
        protected override bool CanHide() => !downloadActive;
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
            UI.BtnBack.interactable = !downloadActive;
        }

        protected override void OnHide()
        {
            if (displayMode == DisplayMode.ResourceDownload && downloadActive) downloadDismissed = true;
            if (UnityEngine.EventSystems.EventSystem.current != null)
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        }

        private void OnStageChanged(MapLoadingStageEvent evt)
        {
            if(!mapLoadingActive||evt.MapId!=loadingMapId)return;
            mapStage=evt.Stage;
            if(displayMode==DisplayMode.Map)RenderMapStage();
        }

        private void RenderMapStage()
        {
            if(!mapStage.HasValue){UI.MessageText.text=Loc.T("ui.loading.scene_status");return;}
            UI.MessageText.text=Loc.T("ui.loading.stage."+mapStage.Value);
        }

        private void OnLoadingStarted(MapLoadingStartedEvent evt)
        {
            loadingMapId=evt.MapId;mapStage=null;mapFailureReason=null;
            mapLoadingStartedAt=DateTime.Now;
            failureLogsCopied=false;
            mapLoadingActive = true;
            baseMapLoading = evt.MapId == MapIds.Base;
            mapProgress = 0f;
            mapError = null;
            if (downloadActive) downloadDismissed = true;
            displayMode = DisplayMode.Map;
            Show();
            // 已显示时 Show 不触发 OnShow，必须刷新最新地图身份和进度。
            RefreshPresentation();
            UI.ErrorRoot.SetActive(false);
        }

        private void OnLoadingCompleted(MapLoadingCompletedEvent evt)
        {
            if(mapStage.HasValue)RenderMapStage();
            mapLoadingActive = false;
            mapError = null;
            if (displayMode == DisplayMode.ResourceDownload) return;
            Hide();
        }

        private void OnProgress(MapLoadingProgressEvent e)
        {
            mapProgress = e.Progress;
            if (displayMode == DisplayMode.Map) SetMapProgress(e.Progress);
        }

        private void OnFailed(MapLoadingFailedEvent e)
        {
            Log.Error($"[LoadingUICtrl] 地图加载失败：{e.Error}");
            if(mapStage.HasValue)RenderMapStage();
            mapLoadingActive = false;
            failureLogsCopied=false;
            mapFailureReason=e.Error;
            mapError=FormatMapFailure();
            if (displayMode == DisplayMode.ResourceDownload) return;
            ShowError(mapError);
        }

        /// <summary>空原因保留通用提示，实际原因作为动态文本展示并保留在下载切换后的失败快照中。</summary>
        private string FormatMapFailure() => string.IsNullOrWhiteSpace(mapFailureReason)
            ? Texts.LoadingErrorMapFailed
            : Loc.T("ui.loading.error.map_failed_detail",Texts.LoadingErrorMapFailed,mapFailureReason);

        private void SetMapProgress(float value)
        {
            var progress = UnityEngine.Mathf.Clamp01(value);
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
            RefreshCopyPresentation();
            var isBase = displayMode == DisplayMode.Map && baseMapLoading;
            UI.BaseArtwork.SetActive(isBase);
            UI.MapArtwork.SetActive(!isBase);
            UI.BaseTitle.SetActive(isBase);
            UI.MapTitle.SetActive(!isBase);
            if (displayMode == DisplayMode.Map) SetMapProgress(mapProgress);
            else RenderDownloadProgress();
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
