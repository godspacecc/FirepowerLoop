using QFramework;

namespace Game
{
    /// <summary>
    /// 描述地图运行时加载状态，区分空闲、加载中、已加载和失败，用于进度 UI 与流程判断。
    /// </summary>
    public enum MapLoadStatus
    {
        Idle,
        Loading,
        Loaded,
        Unloading,
        Failed
    }

    /// <summary>
    /// 保存当前地图 ID、加载状态、进度和错误快照；实际场景加载与卸载由 MapSceneSystem 串行执行。
    /// </summary>
    public sealed class MapSceneModel : AbstractModel
    {
        public BindableProperty<MapLoadStatus> Status { get; } = new BindableProperty<MapLoadStatus>(MapLoadStatus.Idle);
        public string CurrentMapId { get; private set; }
        public IMapContentHandle CurrentHandle { get; private set; }
        public MapPreparationMode CurrentMode { get; private set; } = MapPreparationMode.ArcadePlayerOnly;

        protected override void OnInit()
        {
        }

        public void SetLoaded(string mapId, IMapContentHandle handle, MapPreparationMode mode = MapPreparationMode.ArcadePlayerOnly)
        {
            CurrentMapId = mapId;
            CurrentHandle = handle;
            CurrentMode = mode;
            Status.Value = MapLoadStatus.Loaded;
        }

        public void Clear(MapLoadStatus status = MapLoadStatus.Idle)
        {
            CurrentMapId = null;
            CurrentHandle = null;
            CurrentMode = MapPreparationMode.ArcadePlayerOnly;
            Status.Value = status;
        }

        public void SetStatus(MapLoadStatus status)
        {
            Status.Value = status;
        }
    }
}
