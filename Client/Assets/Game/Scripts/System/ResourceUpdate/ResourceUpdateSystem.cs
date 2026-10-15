using System;
using System.Threading;
using System.Threading.Tasks;
using Boot;
using Cysharp.Threading.Tasks;
using QFramework;

namespace Game
{
    /// <summary>
    /// 协调资源版本、公告及主动 APK 检查；资源快照独立于安装包候选，不下载、不替换或安装。
    /// </summary>
    public sealed class ResourceUpdateSystem : AbstractSystem
    {
        private const int DefaultTimeoutSeconds = 5;
        private Task<ResourceUpdateSnapshot> currentCheck;
        /// <summary>主动综合检查的在途事务；重复点击复用请求，完成后允许再次提示同一 APK。</summary>
        private Task<ResourceUpdateCheckWithNoticeResult> currentManualCheck;
        private CancellationTokenSource cancellation;
        private int generation;

        public ResourceUpdateSnapshot Snapshot => this.GetModel<ResourceUpdateModel>().Snapshot.Value;

        protected override void OnInit()
        {
            cancellation = new CancellationTokenSource();
        }

        protected override void OnDeinit()
        {
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
        }

        public void InitializeStartupInfo(BootResourceStartupInfo info)
        {
            Publish(new ResourceUpdateSnapshot(
                ResourceUpdateStatus.NotChecked,
                info.ActiveVersion,
                info.HostVersion,
                0,
                0,
                string.Empty,
                info.Source,
                info.ResourceServer,
                false,
                false));
        }

        /// <summary>
        /// 检查远端资源更新并发布快照；并发或过期请求只保留最新一次结果。
        /// </summary>
        public UniTask<ResourceUpdateSnapshot> CheckAsync(bool userInitiated)
        {
            if (currentCheck != null && !currentCheck.IsCompleted)
            {
                return currentCheck.AsUniTask();
            }

            currentCheck = CheckInternal(userInitiated, ++generation);
            return currentCheck.AsUniTask();
        }

        /// <summary>主动检查同时返回 APK 候选；静默检查维持资源语义，下载与安装仍由 Boot 负责。</summary>
        public UniTask<ResourceUpdateCheckWithNoticeResult> CheckWithNoticeAsync(bool userInitiated)
        {
            if (!userInitiated) return CheckWithNoticeInternal(false).AsUniTask();
            if (currentManualCheck == null || currentManualCheck.IsCompleted)
                currentManualCheck = CheckWithNoticeInternal(true);
            return currentManualCheck.AsUniTask();
        }

        private async Task<ResourceUpdateCheckWithNoticeResult> CheckWithNoticeInternal(bool userInitiated)
        {
            var token = cancellation.Token;
            var snapshot = await CheckAsync(userInitiated);
            var notice = await FetchNoticeAfterSuccessfulManualCheck(userInitiated, snapshot);
            ApkUpdateManifest applicationUpdate = null;
            var checker = this.GetUtility<IApplicationUpdateChecker>();
            if (userInitiated && checker != null && !token.IsCancellationRequested &&
                !string.IsNullOrWhiteSpace(snapshot.ResourceServer))
            {
                try { applicationUpdate = await checker.CheckAsync(snapshot.ResourceServer, token); }
                catch (OperationCanceledException) { }
                catch (Exception exception) { Log.Warning($"[ResourceUpdateSystem] 安装包检查不可用：{exception.GetType().Name}"); }
                // 架构已释放或服务器已切换时，晚到的候选不能进入新界面。
                if (token.IsCancellationRequested || snapshot.ResourceServer != Snapshot.ResourceServer)
                    applicationUpdate = null;
            }
            WindowsUpdateManifest windowsUpdate = null;
            var windowsChecker = this.GetUtility<IWindowsApplicationUpdateChecker>();
            if (userInitiated && windowsChecker != null && !token.IsCancellationRequested &&
                !string.IsNullOrWhiteSpace(snapshot.ResourceServer))
            {
                try { windowsUpdate = await windowsChecker.CheckAsync(snapshot.ResourceServer, token); }
                catch (OperationCanceledException) { }
                catch (Exception e) { Log.Warning($"[ResourceUpdateSystem] PC 安装包检查不可用：{e.GetType().Name}"); }
                if (token.IsCancellationRequested || snapshot.ResourceServer != Snapshot.ResourceServer)
                    windowsUpdate = null;
            }
            return new ResourceUpdateCheckWithNoticeResult(snapshot, notice, applicationUpdate, windowsUpdate);
        }

        private async Task<ResourceUpdateSnapshot> CheckInternal(bool userInitiated, int requestGeneration)
        {
            if (Snapshot.ResourceSource == BootResourceSource.BuiltinFallback)
            {
                return await CheckBuiltinFallback(userInitiated, requestGeneration);
            }

            var checker = this.GetUtility<IResourceUpdateRuntimeChecker>();
            if (checker == null || !checker.CanCheck)
            {
                var failed = new ResourceUpdateSnapshot(ResourceUpdateStatus.CheckFailed,
                    Snapshot.CurrentVersion,
                    string.Empty,
                    0,
                    0,
                    "资源更新检查器不可用",
                    Snapshot.ResourceSource,
                    Snapshot.ResourceServer);
                Publish(failed);
                return failed;
            }

            Publish(new ResourceUpdateSnapshot(ResourceUpdateStatus.Checking,
                Snapshot.CurrentVersion,
                Snapshot.TargetVersion,
                Snapshot.TotalDownloadCount,
                Snapshot.TotalDownloadBytes,
                string.Empty,
                Snapshot.ResourceSource,
                Snapshot.ResourceServer));

            try
            {
                var result = await checker.CheckAsync(DefaultTimeoutSeconds);
                if (cancellation.IsCancellationRequested || requestGeneration != generation)
                {
                    return new ResourceUpdateSnapshot(ResourceUpdateStatus.CheckFailed,
                        Snapshot.CurrentVersion,
                        Snapshot.TargetVersion,
                        Snapshot.TotalDownloadCount,
                        Snapshot.TotalDownloadBytes,
                        "资源更新检查已取消或请求已过期",
                        Snapshot.ResourceSource,
                        Snapshot.ResourceServer);
                }

                var snapshot = result.Success
                    ? PreserveRuntimeContext(result.Snapshot, string.Empty)
                    : PreserveRuntimeContext(
                        new ResourceUpdateSnapshot(ResourceUpdateStatus.CheckFailed,
                            result.Snapshot.CurrentVersion,
                            result.Snapshot.TargetVersion,
                            result.Snapshot.TotalDownloadCount,
                            result.Snapshot.TotalDownloadBytes,
                            string.IsNullOrWhiteSpace(result.Error)
                                ? $"资源更新检查失败：{result.Snapshot.Error}"
                                : $"资源更新检查失败：{result.Error}"),
                        string.Empty);

                Publish(snapshot);
                return snapshot;
            }
            catch (Exception e)
            {
                var failed = new ResourceUpdateSnapshot(ResourceUpdateStatus.CheckFailed,
                    Snapshot.CurrentVersion,
                    string.Empty,
                    0,
                    0,
                    $"资源更新检查异常：{e.Message}",
                    Snapshot.ResourceSource,
                    Snapshot.ResourceServer);
                Publish(failed);
                return failed;
            }
        }

        private async Task<ResourceUpdateSnapshot> CheckBuiltinFallback(bool userInitiated, int requestGeneration)
        {
            if (!userInitiated)
            {
                return Snapshot;
            }

            Publish(new ResourceUpdateSnapshot(
                ResourceUpdateStatus.Checking,
                Snapshot.CurrentVersion,
                Snapshot.TargetVersion,
                0,
                0,
                string.Empty,
                Snapshot.ResourceSource,
                Snapshot.ResourceServer,
                false,
                false));

            var probe = this.GetUtility<IRemoteResourceVersionProbe>();
            if (probe == null)
            {
                var failedProbe = BuiltinSnapshot(ResourceUpdateStatus.CheckFailed, string.Empty, "资源服务器版本探测器不可用", false);
                Publish(failedProbe);
                return failedProbe;
            }

            try
            {
                var result = await probe.ProbeAsync(Snapshot.ResourceServer, DefaultTimeoutSeconds);
                if (cancellation.IsCancellationRequested || requestGeneration != generation)
                {
                    return BuiltinSnapshot(ResourceUpdateStatus.CheckFailed, Snapshot.TargetVersion, "资源服务器版本探测已取消或请求已过期", false);
                }

                ResourceUpdateSnapshot snapshot;
                if (!result.Success)
                {
                    snapshot = BuiltinSnapshot(ResourceUpdateStatus.CheckFailed, string.Empty, result.Error, false);
                }
                else if (string.Equals(result.Version, Snapshot.CurrentVersion, StringComparison.Ordinal))
                {
                    snapshot = BuiltinSnapshot(ResourceUpdateStatus.NoUpdate, result.Version, string.Empty, false);
                }
                else
                {
                    snapshot = BuiltinSnapshot(ResourceUpdateStatus.UpdateAvailable, result.Version, string.Empty, true);
                }

                Publish(snapshot);
                return snapshot;
            }
            catch (Exception e)
            {
                var failed = BuiltinSnapshot(ResourceUpdateStatus.CheckFailed, string.Empty, $"资源服务器版本探测异常：{e.Message}", false);
                Publish(failed);
                return failed;
            }
        }

        private ResourceUpdateSnapshot BuiltinSnapshot(
            ResourceUpdateStatus status,
            string targetVersion,
            string error,
            bool requiresRestartToUpdate)
        {
            return new ResourceUpdateSnapshot(
                status,
                Snapshot.CurrentVersion,
                targetVersion,
                0,
                0,
                error,
                Snapshot.ResourceSource,
                Snapshot.ResourceServer,
                requiresRestartToUpdate,
                false);
        }

        /// <summary>
        /// 保留启动来源与实际运行版本；成功检查按版本差异判定更新，缓存齐全仍需重启激活清单。
        /// </summary>
        private ResourceUpdateSnapshot PreserveRuntimeContext(
            ResourceUpdateSnapshot snapshot,
            string error)
        {
            var successful = snapshot.Status == ResourceUpdateStatus.NoUpdate ||
                             snapshot.Status == ResourceUpdateStatus.UpdateAvailable;
            var differentVersion = successful && !string.IsNullOrWhiteSpace(snapshot.TargetVersion) &&
                                   !string.Equals(snapshot.CurrentVersion, snapshot.TargetVersion, StringComparison.Ordinal);
            return new ResourceUpdateSnapshot(
                differentVersion ? ResourceUpdateStatus.UpdateAvailable : snapshot.Status,
                snapshot.CurrentVersion,
                snapshot.TargetVersion,
                snapshot.TotalDownloadCount,
                snapshot.TotalDownloadBytes,
                string.IsNullOrWhiteSpace(error) ? snapshot.Error : error,
                Snapshot.ResourceSource,
                Snapshot.ResourceServer,
                differentVersion,
                true);
        }

        private async UniTask<ResourceUpdateNoticeResult> FetchNoticeAfterSuccessfulManualCheck(
            bool userInitiated,
            ResourceUpdateSnapshot snapshot)
        {
            if (!userInitiated ||
                (snapshot.Status != ResourceUpdateStatus.NoUpdate &&
                 snapshot.Status != ResourceUpdateStatus.UpdateAvailable) ||
                string.IsNullOrWhiteSpace(snapshot.ResourceServer) ||
                string.IsNullOrWhiteSpace(snapshot.TargetVersion))
            {
                return ResourceUpdateNoticeResult.NoNotice;
            }

            var client = this.GetUtility<IResourceUpdateNoticeClient>();
            if (client == null)
            {
                return ResourceUpdateNoticeResult.NoNotice;
            }

            var languageCode = this.GetModel<LocalizationModel>().CurrentLanguageCode.Value;
            var result = await client.FetchAsync(new ResourceUpdateNoticeRequest(
                snapshot.ResourceServer,
                snapshot.TargetVersion,
                languageCode,
                DefaultTimeoutSeconds));
            if (!result.Success)
            {
                Log.Warning($"[ResourceUpdateSystem] 资源更新公告不可用：{result.Error}");
                return ResourceUpdateNoticeResult.NoNotice;
            }

            return result;
        }

        private void Publish(ResourceUpdateSnapshot snapshot)
        {
            this.GetModel<ResourceUpdateModel>().Snapshot.Value = snapshot;
            this.SendEvent(new ResourceUpdateChangedEvent(snapshot));
        }
    }
}
