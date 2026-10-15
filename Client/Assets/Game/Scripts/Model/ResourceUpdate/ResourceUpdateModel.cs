using QFramework;

namespace Game
{
    /// <summary>
    /// 保存资源查询快照及各版本源的已知结果，供开始页展示和红点模块读取。
    /// </summary>
    public sealed class ResourceUpdateModel : AbstractModel
    {
        public BindableProperty<ResourceUpdateSnapshot> Snapshot { get; } =
            new BindableProperty<ResourceUpdateSnapshot>(ResourceUpdateSnapshot.NotChecked);

        /// <summary>各版本源最近一次成功查询的结果；检查中或失败不覆盖已知结果。</summary>
        public bool ResourceUpdateAvailable { get; internal set; }
        public bool ApkUpdateAvailable { get; internal set; }
        public bool WindowsUpdateAvailable { get; internal set; }
        /// <summary>综合查询期间或有版本源失败时保留原有红点，直到全部确认无更新。</summary>
        public bool PendingKnownUpdate { get; internal set; }

        /// <summary>由已有版本结果汇总红点；首次未知且没有更新证据时保持熄灭。</summary>
        public bool HasKnownUpdate => ResourceUpdateAvailable || ApkUpdateAvailable || WindowsUpdateAvailable || PendingKnownUpdate;

        protected override void OnInit()
        {
        }
    }
}
