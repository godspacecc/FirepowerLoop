using QFramework;

namespace Game
{
    /// <summary>
    /// 保存最近一次资源版本检查快照，便于启动 UI 展示状态而不重新触发检查流程。
    /// </summary>
    public sealed class ResourceUpdateModel : AbstractModel
    {
        public BindableProperty<ResourceUpdateSnapshot> Snapshot { get; } =
            new BindableProperty<ResourceUpdateSnapshot>(ResourceUpdateSnapshot.NotChecked);

        protected override void OnInit()
        {
        }
    }
}
