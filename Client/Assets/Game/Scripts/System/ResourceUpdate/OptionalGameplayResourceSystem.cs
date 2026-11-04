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
        NeedsDownload
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
        // 非地图入口使用独立范围，避免巨人城或幸存者请求闯关专用敌人。
        public const string SurvivorScope = "survivor";
        public const string CraterScope = "crater";
        internal static string[] DownloadTags(string scope)
        {
            if (scope == MapIds.Arcade) return new[] { "arcade", "game-player", "campaign" };
            if (scope == SurvivorScope) return new[] { "arcade", "game-player", "survivor" };
            if (scope == CraterScope) return new[] { "crater", "game-player" };
            return Array.Empty<string>();
        }

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

                var tags = DownloadTags(mapId);
                if (tags.Length == 0) return Unavailable("未知的玩法资源范围");
                var addresses = new HashSet<string>(StringComparer.Ordinal);
                foreach (var tag in tags)
                {
                    var assets = package.GetAssetInfos(tag);
                    if (assets.Length == 0) return Unavailable("当前清单缺少玩法标签：" + tag);
                    foreach (var asset in assets)
                        if (!asset.IsInvalid) addresses.Add(asset.Address);
                }
                if (!addresses.Contains("Prefabs/Characters/GrayboxPlayer"))
                    return Unavailable("玩法资源缺少共用角色");
                if (mapId == CraterScope)
                {
                    if (!addresses.Contains(ChallengeWorldCtrl.MapAddress) || !addresses.Contains("Prefabs/UI/Challenge/ChallengeHud"))
                        return Unavailable("挑战地图或界面未收集");
                }
                else
                {
                    var configs = this.GetUtility<IRuntimeConfigProvider>();
                    if (configs == null || !configs.TryGetMap(MapIds.Arcade, out var definition, out _) ||
                        definition.SceneAddresses == null || definition.SceneAddresses.Count == 0)
                        return Unavailable("地图配置不可用");
                    foreach (var address in definition.SceneAddresses)
                        if (!addresses.Contains(address)) return Unavailable("地图不在下载范围：" + address);
                }

                var downloader = package.CreateResourceDownloader(DownloadTags(mapId), 10, 3);
                if (downloader == null)
                    return Unavailable("玩法下载器创建失败");
                if (downloader.TotalDownloadCount == 0)
                    return new OptionalGameplayResourceQuery(OptionalGameplayResourceState.Ready, version, 0, 0, string.Empty);

                var source = this.GetSystem<ResourceUpdateSystem>()?.Snapshot.ResourceSource ?? BootResourceSource.EditorSimulate;
                if (source == BootResourceSource.BuiltinFallback || source == BootResourceSource.EditorSimulate)
                    return Unavailable("当前资源来源不能下载缺失的玩法资源");

                return new OptionalGameplayResourceQuery(OptionalGameplayResourceState.NeedsDownload, version,
                    downloader.TotalDownloadCount, downloader.TotalDownloadBytes, string.Empty);
            }
            catch (Exception exception)
            {
                return Unavailable("玩法资源检查异常：" + exception.Message);
            }
        }

        /// <summary>确认后仅启动一次下载；版本变化、失败或释放均返回失败并允许后续重新查询缓存。</summary>
        public virtual async UniTask<OptionalGameplayDownloadResult> DownloadMapAsync(
            string mapId, string expectedVersion, Action<OptionalGameplayDownloadProgress> onProgress,
            CancellationToken cancellationToken = default, Func<bool> isCurrent = null)
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
                var downloader = package.CreateResourceDownloader(DownloadTags(mapId), 10, 3);
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
                while (!downloader.IsDone)
                {
                    if (released || cancellationToken.IsCancellationRequested || isCurrent != null && !isCurrent())
                    {
                        downloader.CancelDownload();
                        await downloader.WaitAsync();
                        return new OptionalGameplayDownloadResult(false, "资源下载已取消");
                    }
                    await UniTask.Yield();
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
    }
}
