using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Network;
using QFramework;
using Boot;

namespace Game
{
    /// <summary>
    /// 管理本机服务器地址，呈现发送器状态并提交手动上传/清理意图；不负责网络确认与队列扣减。
    /// </summary>
    public sealed class SettingNetworkPageCtrl : BasePageCtrl
    {
        private CancellationTokenSource loadCancellation;
        private int lastPending = -1;
        /// <summary>仅页面可见期间跟踪发送器版本，页面关闭不会取消后台上传。</summary>
        private int lastDiagnosticRevision = -1;
        private SettingNetworkPageUI UI => BaseUI as SettingNetworkPageUI;

        protected override void OnInit()
        {
            UI.GameServerApplyButton.onClick.AddListener(ApplyGameServer);
            UI.GameServerResetButton.onClick.AddListener(ResetGameServer);
            UI.ResourceServerApplyButton.onClick.AddListener(ApplyResourceServer);
            UI.ResourceServerResetButton.onClick.AddListener(ResetResourceServer);
            UI.DiagnosticClearButton.onClick.AddListener(ClearDiagnostic);
            UI.DiagnosticUploadButton.onClick.AddListener(UploadDiagnostic);
            this.RegisterEvent<LanguageChangedEvent>(_ => RefreshDiagnostic()).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            RestartLoad();
            RefreshDiagnostic();
            LifeCycle.RegisterUpdate(RefreshPendingIfChanged);
        }

        protected override void OnHide()
        {
            LifeCycle.UnregisterUpdate(RefreshPendingIfChanged);
            loadCancellation?.Cancel();
            loadCancellation?.Dispose();
            loadCancellation = null;
        }

        private async UniTaskVoid LoadAsync(CancellationToken token)
        {
            try
            {
                var game = await this.GetUtility<INetworkConfigProvider>().LoadAsync(token);
                var resource = await this.GetUtility<IResourceServerConfigProvider>().LoadAsync(token);
                if (token.IsCancellationRequested || UI == null) return;
                UI.GameServerInput.text = game.Success ? game.Endpoint.ToString() : string.Empty;
                UI.GameServerFeedback.text = game.Success ? "下次联机时使用此地址。" : "读取联机服务器地址失败。";
                UI.ResourceServerInput.text = resource.Success ? resource.ResourceServer : string.Empty;
                UI.ResourceServerFeedback.text = resource.Success ? "下次启动时用于资源更新。" : "读取资源服务器地址失败。";
            }
            catch (OperationCanceledException)
            {
                // 页面关闭或重新加载时，旧请求安静退出，不覆盖新页面状态。
            }
        }

        private void ApplyGameServer()
        {
            var provider = this.GetUtility<INetworkConfigProvider>() as IEditableNetworkConfigProvider;
            if (provider != null && provider.TrySaveEndpoint(UI.GameServerInput.text, out var endpoint, out _))
            {
                UI.GameServerInput.text = endpoint.ToString();
                UI.GameServerFeedback.text = "已保存，下次联机生效。";
                return;
            }
            UI.GameServerFeedback.text = "地址格式无效，请输入 IP 或域名:端口。";
        }

        private void ResetGameServer()
        {
            var provider = this.GetUtility<INetworkConfigProvider>() as IResettableNetworkConfigProvider;
            if (provider == null || !provider.TryResetEndpoint(out _)) { UI.GameServerFeedback.text = "恢复默认失败。"; return; }
            RestartLoad();
        }

        private void ApplyResourceServer()
        {
            if (this.GetUtility<IResourceServerConfigProvider>().TrySave(UI.ResourceServerInput.text, out var server, out _))
            {
                UI.ResourceServerInput.text = server;
                UI.ResourceServerFeedback.text = "已保存，下次启动时生效。";
                return;
            }
            UI.ResourceServerFeedback.text = "地址格式无效，请输入完整 HTTP/HTTPS 地址。";
        }

        private void ResetResourceServer()
        {
            if (!this.GetUtility<IResourceServerConfigProvider>().TryReset(out _)) { UI.ResourceServerFeedback.text = "恢复默认失败。"; return; }
            RestartLoad();
        }

        private void RefreshDiagnostic()
        {
            if (UI == null) return;
            UI.DiagnosticNotice.text = Loc.T("ui.setting.diagnostic.notice");
            UI.DiagnosticPending.text = string.Format(Loc.T("ui.setting.diagnostic.pending"), DiagnosticReportSender.PendingGroups);
            lastPending = DiagnosticReportSender.PendingGroups;
            lastDiagnosticRevision = DiagnosticReportSender.StatusRevision;
            UI.DiagnosticUploadButton.interactable = !DiagnosticReportSender.IsUploading;
            UI.DiagnosticStatus.text = BuildDiagnosticStatus();
            var label = UI.DiagnosticClearButton.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (label != null) label.text = Loc.T("ui.setting.diagnostic.clear");
        }

        private void RefreshPendingIfChanged()
        {
            if (UI != null && (DiagnosticReportSender.PendingGroups != lastPending ||
                DiagnosticReportSender.StatusRevision != lastDiagnosticRevision)) RefreshDiagnostic();
        }

        /// <summary>按钮复用 Boot 的单一发送器；重复点击和失败保留由发送器决定。</summary>
        private void UploadDiagnostic()
        {
            DiagnosticReportSender.RequestUpload();
            RefreshDiagnostic();
        }

        /// <summary>从只读状态生成玩家可读结果；UTC 时间转换为手机本地时间，不显示原始异常。</summary>
        private static string BuildDiagnosticStatus()
        {
            string key = DiagnosticReportSender.Status switch
            {
                DiagnosticUploadStatus.Loading => "loading",
                DiagnosticUploadStatus.Sending => "sending",
                DiagnosticUploadStatus.Completed => "completed",
                DiagnosticUploadStatus.Empty => "empty",
                DiagnosticUploadStatus.ConfigurationInvalid => "configuration",
                DiagnosticUploadStatus.ConnectionFailed => "connection",
                DiagnosticUploadStatus.TimedOut => "timeout",
                DiagnosticUploadStatus.HttpFailed => "http",
                DiagnosticUploadStatus.StorageFailed => "storage",
                DiagnosticUploadStatus.Cancelled => "cancelled",
                _ => "ready"
            };
            string state = Loc.T("ui.setting.diagnostic.status." + key);
            if (DiagnosticReportSender.Status == DiagnosticUploadStatus.Sending)
                state = string.Format(state, DiagnosticReportSender.PendingGroups);
            else if (DiagnosticReportSender.Status == DiagnosticUploadStatus.HttpFailed)
                state = string.Format(state, DiagnosticReportSender.LastResponseCode);
            string target = DiagnosticReportSender.ReportUrl ?? Loc.T("ui.setting.diagnostic.target_missing");
            string attempted = DiagnosticReportSender.LastAttemptUtc == DateTime.MinValue
                ? Loc.T("ui.setting.diagnostic.never")
                : DiagnosticReportSender.LastAttemptUtc.ToLocalTime().ToString("MM-dd HH:mm:ss");
            string text = state + "\n" + string.Format(Loc.T("ui.setting.diagnostic.target"), target) +
                "\n" + string.Format(Loc.T("ui.setting.diagnostic.last_attempt"), attempted);
            if (!DiagnosticReportSender.IsUploading && DiagnosticReportSender.PendingGroups > 0 &&
                DiagnosticReportSender.NextAttemptUtc > DateTime.UtcNow)
                text += "\n" + string.Format(Loc.T("ui.setting.diagnostic.retry"),
                    DiagnosticReportSender.NextAttemptUtc.ToLocalTime().ToString("HH:mm:ss"));
            return text;
        }

        /// <summary>清理失败保留队列并给出明确反馈；既有完整本地日志不在此入口处理。</summary>
        private void ClearDiagnostic()
        {
            bool success = DiagnosticReportSender.Clear();
            RefreshDiagnostic();
            Notify.ShowFloatTips(Loc.T(success ? "ui.setting.diagnostic.cleared" : "ui.setting.diagnostic.clear_failed"));
        }

        /// <summary>取消旧读取并启动新一轮，防止恢复默认后的旧结果晚到覆盖。</summary>
        private void RestartLoad()
        {
            loadCancellation?.Cancel();
            loadCancellation?.Dispose();
            loadCancellation = new CancellationTokenSource();
            LoadAsync(loadCancellation.Token).Forget();
        }
    }
}
