using QFramework;

namespace Game
{
    /// <summary>
    /// 监听版本查询领域变更，从资源与当前平台安装包的已知结果推导开始页红点。
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
            return model != null && model.HasKnownUpdate;
        }

        protected override int OnGetCount()
        {
            return OnCheckActive() ? 1 : 0;
        }
    }
}
