using System;
using System.Collections.Generic;
using System.Threading;
using Boot;
using Cysharp.Threading.Tasks;
using QFramework;
using YooAsset;

namespace Game
{
    public enum OptionalGameplayResourceState
    {
        Unavailable,
        Ready,
        NeedsDownload,
        ConnectionFailed
    }

    /// <summary>一次当前清单查询的不可变结果；大小单位为字节，不代表持久化安装状态。</summary>
    public readonly struct OptionalGameplayResourceQuery
    {
        public OptionalGameplayResourceQuery(OptionalGameplayResourceState state, string version,
            int fileCount, long totalBytes, string error)
        {
            State = state;
            Version = version ?? string.Empty;
            FileCount = fileCount;
            TotalBytes = totalBytes;
            Error = error ?? string.Empty;
        }

        public OptionalGameplayResourceState State { get; }
        public string Version { get; }
        public int FileCount { get; }
        public long TotalBytes { get; }
        public string Error { get; }
    }

    /// <summary>仅承载数值的下载进度，不向 UI 暴露 YooAsset 操作对象。</summary>
    public readonly struct OptionalGameplayDownloadProgress
    {
        public OptionalGameplayDownloadProgress(long currentBytes, long totalBytes, int currentFiles, int totalFiles)
        {
            CurrentBytes = currentBytes;
            TotalBytes = totalBytes;
            CurrentFiles = currentFiles;
            TotalFiles = totalFiles;
        }

        public long CurrentBytes { get; }
        public long TotalBytes { get; }
        public int CurrentFiles { get; }
        public int TotalFiles { get; }
    }

    public readonly struct OptionalGameplayDownloadResult
    {
        public OptionalGameplayDownloadResult(bool success, string error)
        {
            Success = success;
            Error = error ?? string.Empty;
        }

        public bool Success { get; }
        public string Error { get; }
    }

    /// <summary>
    /// 准备当前 DefaultPackage 清单中指定地图或街机资源；不更新清单、不管理解锁或持久化安装状态。
    /// 下载操作的占用从开始直到 YooAsset 给出终态，失败和释放都会清除占用。
    /// </summary>
    public class OptionalGameplayResourceSystem : AbstractSystem
    {
        private const string PackageName = "DefaultPackage";

        /// <summary>街机资源归属沿用现有清单标签。</summary>
        public static string GetMapTag(string mapId) => mapId == MapIds.Arcade ? "arcade" : null;
        /// <summary>交互前服务器探测超时，单位秒；失败不创建下载事务。</summary>
        private const int ServerCheckTimeoutSeconds = 5;
        private ResourceDownloaderOperation currentDownloader;
        private bool downloading;
        private bool released;
        /// <summary>架构释放后拒绝确认和后续下载；不是持久化安装状态。</summary>
        public bool IsReleased => released;

        protected override void OnInit()
        {
            released = false;
        }

        protected override void OnDeinit()
        {
            released = true;
            currentDownloader?.CancelDownload();
            currentDownloader = null;
            downloading = false;
        }

        /// <summary>每次交互从活动清单重新判断；只有完成标签和必需地址核对后，零文件才表示就绪。</summary>
        public virtual OptionalGameplayResourceQuery QueryMap(string mapId)
        {
            if (released || string.IsNullOrWhiteSpace(mapId)) return Unavailable("地图资源请求已失效");
            try
            {
                var package = YooAssets.TryGetPackage(PackageName);
                if (package == null)
                    return Unavailable("DefaultPackage 未初始化");

                var version = package.GetPackageVersion();
                if (string.IsNullOrWhiteSpace(version))
                    return Unavailable("当前资源清单版本为空");

                var assets = package.GetAssetInfos(GetMapTag(mapId));
                if (assets == null || assets.Length == 0)
                    return Unavailable("当前清单缺少玩法标签资源");

                var addresses = new HashSet<string>(StringComparer.Ordinal);
                foreach (var asset in assets)
                {
                    if (!asset.IsInvalid && !string.IsNullOrWhiteSpace(asset.Address))
                        addresses.Add(asset.Address);
                }

                var configs = this.GetUtility<IRuntimeConfigProvider>();
                if (configs == null || string.IsNullOrWhiteSpace(GetMapTag(mapId)))
                    return Unavailable("地图资源范围或配置不可用");
                if (!configs.TryGetMap(mapId, out var definition, out var error))
                    return Unavailable("地图配置缺失：" + mapId + " " + error);
                if (definition.SceneAddresses == null || definition.SceneAddresses.Count == 0)
                    return Unavailable("地图场景地址为空：" + mapId);
                foreach (var address in definition.SceneAddresses)
                    if (string.IsNullOrWhiteSpace(address) || !addresses.Contains(address))
                        return Unavailable("地图场景未归入下载范围：" + mapId + " " + address);

                var downloader = package.CreateResourceDownloader(GetMapTag(mapId), 10, 3);
                if (downloader == null)
                    return Unavailable("玩法下载器创建失败");
                if (downloader.TotalDownloadCount == 0)
                    return new OptionalGameplayResourceQuery(OptionalGameplayResourceState.Ready, version, 0, 0, string.Empty);

                var source = this.GetSystem<ResourceUpdateSystem>()?.Snapshot.ResourceSource ?? BootResourceSource.EditorSimulate;
                if (source == BootResourceSource.BuiltinFallback || source == BootResourceSource.EditorSimulate)
                    return Unavailable("当前资源来源不能下载缺失的街机资源");

                return new OptionalGameplayResourceQuery(OptionalGameplayResourceState.NeedsDownload, version,
                    downloader.TotalDownloadCount, downloader.TotalDownloadBytes, string.Empty);
            }
            catch (Exception exception)
            {
                return Unavailable("玩法资源检查异常：" + exception.Message);
            }
        }

        /// <summary>缺失资源时才探测服务器；本地已就绪无需联网，等待后重新查询以拒绝过期准备结果。</summary>
        public virtual async UniTask<OptionalGameplayResourceQuery> CheckMapAsync(string mapId)
        {
            if (released) return Unavailable("资源系统已释放");
            var query = QueryMap(mapId);
            if (query.State != OptionalGameplayResourceState.NeedsDownload) return query;
            var probe = this.GetUtility<IRemoteResourceVersionProbe>();
            string server = this.GetSystem<ResourceUpdateSystem>()?.Snapshot.ResourceServer;
            try
            {
                if (probe == null || string.IsNullOrWhiteSpace(server))
                    return ConnectionFailed("资源服务器探测器或地址不可用");
                var result = await probe.ProbeAsync(server, ServerCheckTimeoutSeconds);
                if (released) return Unavailable("资源系统已释放");
                if (!result.Success) return ConnectionFailed(result.Error);
                return QueryMap(mapId);
            }
            catch (Exception exception)
            {
                return ConnectionFailed("资源服务器检查失败：" + exception.Message);
            }
        }

        /// <summary>确认后仅启动一次下载；版本变化、失败或释放均返回失败并允许后续重新查询缓存。</summary>
        public virtual async UniTask<OptionalGameplayDownloadResult> DownloadMapAsync(
            string mapId, string expectedVersion, Action<OptionalGameplayDownloadProgress> onProgress,
            CancellationToken cancellationToken = default)
        {
            if (released || downloading)
                return new OptionalGameplayDownloadResult(false, "玩法下载已占用或资源系统已释放");

            downloading = true;
            try
            {
                var query = QueryMap(mapId);
                if (!string.Equals(query.Version, expectedVersion, StringComparison.Ordinal))
                    return new OptionalGameplayDownloadResult(false, "资源清单版本已变化");
                if (query.State == OptionalGameplayResourceState.Ready)
                    return new OptionalGameplayDownloadResult(true, string.Empty);
                if (query.State != OptionalGameplayResourceState.NeedsDownload)
                    return new OptionalGameplayDownloadResult(false, query.Error);

                var package = YooAssets.TryGetPackage(PackageName);
                var downloader = package.CreateResourceDownloader(GetMapTag(mapId), 10, 3);
                if (downloader == null)
                    return new OptionalGameplayDownloadResult(false, "玩法下载器创建失败");
                if (downloader.TotalDownloadCount <= 0 || downloader.TotalDownloadBytes <= 0)
                    return new OptionalGameplayDownloadResult(false, "玩法下载大小无效，请重新检查资源");
                currentDownloader = downloader;
                // 创建下载器后先发布真实总大小，避免等待首次网络回调时 UI 显示未知大小。
                onProgress?.Invoke(new OptionalGameplayDownloadProgress(0, downloader.TotalDownloadBytes,
                    0, downloader.TotalDownloadCount));
                downloader.DownloadUpdateCallback = data =>
                {
                    if (!released)
                        onProgress?.Invoke(new OptionalGameplayDownloadProgress(data.CurrentDownloadBytes,
                            data.TotalDownloadBytes, data.CurrentDownloadCount, data.TotalDownloadCount));
                };
                downloader.BeginDownload();
                try { await downloader.WaitAsync().AttachExternalCancellation(cancellationToken); }
                catch (OperationCanceledException)
                {
                    // 取消后等待下载器终态，不能提前释放占用而启动第二份事务。
                    downloader.CancelDownload();
                    await downloader.WaitAsync();
                    return new OptionalGameplayDownloadResult(false, "资源下载已取消");
                }

                // System 释放可能在 await 期间清空字段；本次结果仍须由原下载器判定。
                if (released || downloader.Status != EOperationStatus.Succeed)
                    return new OptionalGameplayDownloadResult(false, released ? "资源系统已释放" : downloader.Error);
                if (!string.Equals(package.GetPackageVersion(), expectedVersion, StringComparison.Ordinal))
                    return new OptionalGameplayDownloadResult(false, "下载结束时资源清单版本已变化");
                return new OptionalGameplayDownloadResult(true, string.Empty);
            }
            catch (Exception exception)
            {
                return new OptionalGameplayDownloadResult(false, "玩法下载异常：" + exception.Message);
            }
            finally
            {
                if (currentDownloader != null)
                    currentDownloader.DownloadUpdateCallback = null;
                currentDownloader = null;
                downloading = false;
            }
        }

        private static OptionalGameplayResourceQuery Unavailable(string error) =>
            new OptionalGameplayResourceQuery(OptionalGameplayResourceState.Unavailable, string.Empty, 0, 0, error);

        private static OptionalGameplayResourceQuery ConnectionFailed(string error) =>
            new OptionalGameplayResourceQuery(OptionalGameplayResourceState.ConnectionFailed, string.Empty, 0, 0, error);
    }
}
