using QFramework;

namespace Game
{
    /// <summary>
    /// 监听资源更新领域变更，并以当前资源更新快照推导开始界面的更新红点。
    /// </summary>
    public sealed class ResourceUpdateRedPointMod : RedPointMod
    {
        private ResourceUpdateModel model;

        public ResourceUpdateRedPointMod() : base(RedPointIds.StartResourceUpdate)
        {
        }

        protected override void OnAttach()
        {
            model = this.GetModel<ResourceUpdateModel>();
            Track(this.RegisterEvent<ResourceUpdateChangedEvent>(_ => NotifyChange()));
        }

        protected override void OnDetach()
        {
            model = null;
        }

        protected override bool OnCheckActive()
        {
            return model != null && model.Snapshot.Value.Status == ResourceUpdateStatus.UpdateAvailable;
        }

        protected override int OnGetCount()
        {
            return OnCheckActive() ? 1 : 0;
        }
    }
}
