using System;
using System.Threading;
using System.Threading.Tasks;
using Boot;
using Cysharp.Threading.Tasks;
using QFramework;

namespace Game
{
    /// <summary>
    /// 协调资源与当前平台安装包版本查询；公告仅由主动入口读取，不下载、不替换或安装。
    /// </summary>
    public sealed class ResourceUpdateSystem : AbstractSystem
    {
        private const int DefaultTimeoutSeconds = 5;
        /// <summary>资源查询的在途请求，自动和手动入口共享。</summary>
        private Task<ResourceUpdateSnapshot> currentCheck;
        /// <summary>资源与安装包查询的共同事务，不包含公告与 UI 提示。</summary>
        private Task<ResourceUpdateCheckWithNoticeResult> currentVersionCheck;
        /// <summary>主动综合检查的在途事务；重复点击复用请求，完成后允许再次提示同一 APK。</summary>
        private Task<ResourceUpdateCheckWithNoticeResult> currentManualCheck;
        private CancellationTokenSource cancellation;
        private int generation;
        /// <summary>当前已加载资源包初始化时绑定的服务器；检查地址变化不重建或切换运行中的资源包。</summary>
        private string startupResourceServer = string.Empty;
        /// <summary>服务器选择顺序，独立于检查代次；同地址再次应用也会废弃更早的默认地址读取。</summary>
        private int serverSelectionRevision;

        public ResourceUpdateSnapshot Snapshot => this.GetModel<ResourceUpdateModel>().Snapshot.Value;

        protected override void OnInit()
        {
            cancellation = new CancellationTokenSource();
        }

        protected override void OnDeinit()
        {
            generation++;
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
        }

        /// <summary>记录资源包的真实启动地址和版本，并建立本次检查上下文。</summary>
        public void InitializeStartupInfo(BootResourceStartupInfo info)
        {
            serverSelectionRevision++;
            startupResourceServer = info.ResourceServer.TrimEnd('/');
            ResetCheckContext(info);
        }

        /// <summary>设置保存成功后立即更换检查地址；保留已加载来源与版本，丢弃旧服候选及在途结果。</summary>
        public void ApplyResourceServer(string resourceServer)
        {
            if (!ResourceServerPreference.TryNormalize(resourceServer, out string server)) return;
            serverSelectionRevision++;
            if (string.Equals(server, Snapshot.ResourceServer.TrimEnd('/'), StringComparison.Ordinal)) return;

            ResetCheckContext(new BootResourceStartupInfo(
                Snapshot.ResourceSource, server, Snapshot.CurrentVersion, string.Empty));
        }

        /// <summary>恢复默认保存成功后重读检查地址；不依赖设置页面可见性，后续选择会废弃本次读取。</summary>
        public async UniTask ReloadResourceServerAsync()
        {
            int selectionRevision = ++serverSelectionRevision;
            var token = cancellation.Token;
            try
            {
                var result = await this.GetUtility<IResourceServerConfigProvider>().LoadAsync(token);
                if (!token.IsCancellationRequested && selectionRevision == serverSelectionRevision && result.Success)
                    ApplyResourceServer(result.ResourceServer);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                Log.Warning($"[ResourceUpdateSystem] 读取资源服务器地址失败：{exception.GetType().Name}");
            }
        }

        /// <summary>只重置更新检查事务；不发布或篡改 Boot 的真实资源启动记录。</summary>
        private void ResetCheckContext(BootResourceStartupInfo info)
        {
            // 启动上下文变化后旧请求失效，候选和已知结果不得串到另一个资源服务器。
            generation++;
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            currentCheck = null;
            currentVersionCheck = null;
            currentManualCheck = null;
            var model = this.GetModel<ResourceUpdateModel>();
            model.ResourceUpdateAvailable = false;
            model.ApkUpdateAvailable = false;
            model.WindowsUpdateAvailable = false;
            model.PendingKnownUpdate = false;
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
            if (ClientReleasePolicy.OfflineSinglePlayer) return UniTask.FromResult(Snapshot);
            if (currentCheck != null && !currentCheck.IsCompleted)
            {
                return currentCheck.AsUniTask();
            }

            currentCheck = CheckInternal(userInitiated, ++generation);
            return currentCheck.AsUniTask();
        }

        /// <summary>自动与手动共用版本查询；只有手动入口读取公告，下载与安装仍由 Boot 负责。</summary>
        public UniTask<ResourceUpdateCheckWithNoticeResult> CheckWithNoticeAsync(bool userInitiated)
        {
            if (ClientReleasePolicy.OfflineSinglePlayer) return UniTask.FromResult(new ResourceUpdateCheckWithNoticeResult(Snapshot, ResourceUpdateNoticeResult.NoNotice));
            if (!userInitiated) return CheckVersionsAsync(false).AsUniTask();
            if (currentManualCheck == null || currentManualCheck.IsCompleted)
                currentManualCheck = CheckWithNoticeInternal(true);
            return currentManualCheck.AsUniTask();
        }

        /// <summary>手动展示事务在版本查询完成后单独读取公告，不让自动入口触发公告查询。</summary>
        private async Task<ResourceUpdateCheckWithNoticeResult> CheckWithNoticeInternal(bool userInitiated)
        {
            var token = cancellation.Token;
            var versions = await CheckVersionsAsync(userInitiated);
            if (token.IsCancellationRequested) return new ResourceUpdateCheckWithNoticeResult(Snapshot, ResourceUpdateNoticeResult.NoNotice);
            var notice = await FetchNoticeAfterSuccessfulManualCheck(userInitiated, versions.Snapshot);
            if (token.IsCancellationRequested) return new ResourceUpdateCheckWithNoticeResult(Snapshot, ResourceUpdateNoticeResult.NoNotice);
            return new ResourceUpdateCheckWithNoticeResult(versions.Snapshot, notice, versions.ApplicationUpdate, versions.WindowsUpdate);
        }

        /// <summary>综合查询完成前复用同一 Task；查询结束后下次返回可重新检查。</summary>
        private Task<ResourceUpdateCheckWithNoticeResult> CheckVersionsAsync(bool userInitiated)
        {
            if (currentVersionCheck == null || currentVersionCheck.IsCompleted)
                currentVersionCheck = CheckVersionsInternal(userInitiated);
            return currentVersionCheck;
        }

        /// <summary>保存每个版本源的成功结果；失败保留已知值，检查期间保留原有更新提示。</summary>
        private async Task<ResourceUpdateCheckWithNoticeResult> CheckVersionsInternal(bool userInitiated)
        {
            var token = cancellation.Token;
            var model = this.GetModel<ResourceUpdateModel>();
            model.PendingKnownUpdate = model.HasKnownUpdate;
            var snapshot = await CheckAsync(userInitiated);
            bool allSucceeded = snapshot.Status == ResourceUpdateStatus.NoUpdate || snapshot.Status == ResourceUpdateStatus.UpdateAvailable;
            ApkUpdateManifest applicationUpdate = null;
            var checker = this.GetUtility<IApplicationUpdateChecker>();
            if (checker != null && !token.IsCancellationRequested &&
                !string.IsNullOrWhiteSpace(snapshot.ResourceServer))
            {
                try
                {
                    applicationUpdate = await checker.CheckAsync(snapshot.ResourceServer, token);
                    if (!token.IsCancellationRequested) model.ApkUpdateAvailable = applicationUpdate != null;
                }
                catch (OperationCanceledException) { allSucceeded = false; }
                catch (Exception exception) { allSucceeded = false; Log.Warning($"[ResourceUpdateSystem] 安装包检查不可用：{exception.GetType().Name}"); }
                // 架构已释放或服务器已切换时，晚到的候选不能进入新界面。
                if (token.IsCancellationRequested || snapshot.ResourceServer != Snapshot.ResourceServer)
                    applicationUpdate = null;
            }
            else allSucceeded = false;
            WindowsUpdateManifest windowsUpdate = null;
            var windowsChecker = this.GetUtility<IWindowsApplicationUpdateChecker>();
            if (windowsChecker != null && !token.IsCancellationRequested &&
                !string.IsNullOrWhiteSpace(snapshot.ResourceServer))
            {
                try
                {
                    windowsUpdate = await windowsChecker.CheckAsync(snapshot.ResourceServer, token);
                    if (!token.IsCancellationRequested) model.WindowsUpdateAvailable = windowsUpdate != null;
                }
                catch (OperationCanceledException) { allSucceeded = false; }
                catch (Exception e) { allSucceeded = false; Log.Warning($"[ResourceUpdateSystem] PC 安装包检查不可用：{e.GetType().Name}"); }
                if (token.IsCancellationRequested || snapshot.ResourceServer != Snapshot.ResourceServer)
                    windowsUpdate = null;
            }
            else allSucceeded = false;
            if (!token.IsCancellationRequested)
            {
                if (allSucceeded) model.PendingKnownUpdate = false;
                this.SendEvent(new ResourceUpdateChangedEvent(Snapshot));
            }
            return new ResourceUpdateCheckWithNoticeResult(snapshot, ResourceUpdateNoticeResult.NoNotice, applicationUpdate, windowsUpdate);
        }

        /// <summary>发布资源查询状态；晚到结果不覆盖已切换的启动上下文。</summary>
        private async Task<ResourceUpdateSnapshot> CheckInternal(bool userInitiated, int requestGeneration)
        {
            var token = cancellation.Token;
            if (Snapshot.ResourceSource == BootResourceSource.BuiltinFallback ||
                !string.Equals(Snapshot.ResourceServer.TrimEnd('/'), startupResourceServer, StringComparison.Ordinal))
            {
                // 已初始化的 YooAsset 包仍指向原服务器；新服仅探测版本，真正切换仍交给重启流程。
                return await CheckRemoteVersion(requestGeneration);
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
                var result = await checker.CheckAsync(DefaultTimeoutSeconds, versionOnly: !userInitiated);
                if (token.IsCancellationRequested || requestGeneration != generation)
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
                if (token.IsCancellationRequested || requestGeneration != generation) return Snapshot;
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

        /// <summary>内置兜底或更换地址后只探测选定服务器版本，不激活清单或下载资源。</summary>
        private async Task<ResourceUpdateSnapshot> CheckRemoteVersion(int requestGeneration)
        {
            var token = cancellation.Token;
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
                var failedProbe = RemoteVersionSnapshot(ResourceUpdateStatus.CheckFailed, string.Empty, "资源服务器版本探测器不可用", false);
                Publish(failedProbe);
                return failedProbe;
            }

            try
            {
                var result = await probe.ProbeAsync(Snapshot.ResourceServer, DefaultTimeoutSeconds);
                if (token.IsCancellationRequested || requestGeneration != generation)
                {
                    return RemoteVersionSnapshot(ResourceUpdateStatus.CheckFailed, Snapshot.TargetVersion, "资源服务器版本探测已取消或请求已过期", false);
                }

                ResourceUpdateSnapshot snapshot;
                if (!result.Success)
                {
                    snapshot = RemoteVersionSnapshot(ResourceUpdateStatus.CheckFailed, string.Empty, result.Error, false);
                }
                else if (string.Equals(result.Version, Snapshot.CurrentVersion, StringComparison.Ordinal))
                {
                    snapshot = RemoteVersionSnapshot(ResourceUpdateStatus.NoUpdate, result.Version, string.Empty, false);
                }
                else
                {
                    snapshot = RemoteVersionSnapshot(ResourceUpdateStatus.UpdateAvailable, result.Version, string.Empty, true);
                }

                Publish(snapshot);
                return snapshot;
            }
            catch (Exception e)
            {
                if (token.IsCancellationRequested || requestGeneration != generation) return Snapshot;
                var failed = RemoteVersionSnapshot(ResourceUpdateStatus.CheckFailed, string.Empty, $"资源服务器版本探测异常：{e.Message}", false);
                Publish(failed);
                return failed;
            }
        }

        /// <summary>保留当前运行版本及真实启动来源，未知下载量不能显示成零下载。</summary>
        private ResourceUpdateSnapshot RemoteVersionSnapshot(
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
                snapshot.HasKnownDownloadSize);
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
            var model = this.GetModel<ResourceUpdateModel>();
            if (snapshot.Status == ResourceUpdateStatus.UpdateAvailable || snapshot.Status == ResourceUpdateStatus.NoUpdate)
                model.ResourceUpdateAvailable = snapshot.Status == ResourceUpdateStatus.UpdateAvailable;
            model.Snapshot.Value = snapshot;
            this.SendEvent(new ResourceUpdateChangedEvent(snapshot));
        }
    }
}
