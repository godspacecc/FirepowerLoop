using QFramework;

namespace Game
{
    /// <summary>
    /// 保存当前活动域及其最近提交的根快照；无地图时允许保留基地根，但没有活动域。
    /// </summary>
    public sealed class SaveModel : AbstractModel
    {
        public OfflineSaveDomain CurrentDomain { get; private set; } = OfflineSaveDomain.None;
        /// <summary>当前已提交的基地根快照；无地图的开始界面也可以持有它。</summary>
        public BaseSaveData CurrentBase { get; private set; }
        /// <summary>当前已提交的活动副本根快照。它绝不与基地根对象共享实例。</summary>
        public ExpeditionSaveData CurrentExpedition { get; private set; }

        protected override void OnInit()
        {
        }

        public void SetBase(BaseSaveData data, bool active)
        {
            CurrentBase = data?.Clone();
            CurrentExpedition = null;
            CurrentDomain = active && CurrentBase != null ? OfflineSaveDomain.Base : OfflineSaveDomain.None;
        }

        public void SetExpedition(ExpeditionSaveData data)
        {
            CurrentExpedition = data?.Clone();
            CurrentDomain = CurrentExpedition == null ? OfflineSaveDomain.None : OfflineSaveDomain.Expedition;
        }

        /// <summary>磁盘差异事务成功后提交独立的可变快照；不复制不变地形，也不能跨远征提交。</summary>
        internal void SetExpeditionMutable(ExpeditionSaveData data)
        {
            if (CurrentDomain != OfflineSaveDomain.Expedition || CurrentExpedition == null || data == null ||
                data.ExpeditionId != CurrentExpedition.ExpeditionId || data.MapId != CurrentExpedition.MapId)
                throw new System.InvalidOperationException("增量提交远征身份不一致");
            var mutable = data.CloneMutable();
            mutable.ValleyLayout = CurrentExpedition.ValleyLayout;
            CurrentExpedition = mutable;
        }

        public void ClearCurrent()
        {
            CurrentBase = null;
            CurrentExpedition = null;
            CurrentDomain = OfflineSaveDomain.None;
        }
    }
}
