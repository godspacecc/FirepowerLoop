using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;

namespace Game
{
    /// <summary>
    /// 地图内容句柄，记录已加载地图标识、场景上下文和当前有效状态。
    /// </summary>
    public interface IMapContentHandle
    {
        string MapId { get; }
        MapContext Context { get; }
        bool IsValid { get; }
    }

    /// <summary>
    /// 地图内容加载工具接口，统一提供异步加载、卸载和进度回调入口。
    /// </summary>
    public interface IMapContentLoader : IUtility
    {
        UniTask<MapContentLoadResult> LoadAsync(string mapId, string address, Action<float> onProgress, CancellationToken cancellationToken = default);
        UniTask<MapContentUnloadResult> UnloadAsync(IMapContentHandle handle, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 地图内容加载结果，携带成功状态、错误信息和可卸载的内容句柄。
    /// </summary>
    public sealed class MapContentLoadResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public IMapContentHandle Handle { get; set; }

        public static MapContentLoadResult Ok(IMapContentHandle handle)
        {
            return new MapContentLoadResult { Success = true, Handle = handle };
        }

        public static MapContentLoadResult Fail(string error)
        {
            return new MapContentLoadResult { Success = false, Error = error };
        }
    }

    /// <summary>
    /// 地图内容卸载结果，表达卸载是否成功及失败原因。
    /// </summary>
    public sealed class MapContentUnloadResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }

        public static MapContentUnloadResult Ok()
        {
            return new MapContentUnloadResult { Success = true };
        }

        public static MapContentUnloadResult Fail(string error)
        {
            return new MapContentUnloadResult { Success = false, Error = error };
        }
    }
}
