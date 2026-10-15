using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using QFramework;
using YooAsset;

namespace Game
{
    public enum OptionalGameplayResourceState
    {
        Unavailable,
        Ready
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

    /// <summary>
    /// 只检查启动阶段统一准备的地图资源；不更新清单、不发起下载、不管理解锁或持久化安装状态。
    /// 入口与底层地图加载共用只读检查，启动资源不完整时拒绝进入。
    /// </summary>
    public class OptionalGameplayResourceSystem : AbstractSystem
    {
        private const string PackageName = "DefaultPackage";

        /// <summary>街机资源归属沿用现有清单标签。</summary>
        public static string GetMapTag(string mapId) => mapId == MapIds.Arcade ? "arcade" : null;
        private bool released;
        /// <summary>架构释放后拒绝资源检查；不是持久化安装状态。</summary>
        public bool IsReleased => released;

        protected override void OnInit()
        {
            released = false;
        }

        protected override void OnDeinit()
        {
            released = true;
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

                return Unavailable("启动资源不完整，请重新启动游戏完成资源更新");
            }
            catch (Exception exception)
            {
                return Unavailable("玩法资源检查异常：" + exception.Message);
            }
        }

        /// <summary>兼容现有资源端口；仅返回本地清单检查，不探测服务器或启动下载。</summary>
        public virtual UniTask<OptionalGameplayResourceQuery> CheckMapAsync(string mapId) => UniTask.FromResult(QueryMap(mapId));

        private static OptionalGameplayResourceQuery Unavailable(string error) =>
            new OptionalGameplayResourceQuery(OptionalGameplayResourceState.Unavailable, string.Empty, 0, 0, error);
    }
}
