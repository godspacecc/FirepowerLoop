using System;

namespace Game
{
    public sealed class ItemStack
    {
        public ItemStack(int itemId, int amount)
        {
            ItemId = itemId;
            Amount = amount;
        }

        /// <summary>运行期库存实例身份；事务克隆保留，载入或新入格重新分配，不写存档。</summary>
        internal Guid RuntimeIdentity { get; private set; } = Guid.NewGuid();
        /// <summary>此武器实例已装弹药；随库存格保存，收起不产生独立装备储存位置。</summary>
        private int loadedAmmo;
        public int LoadedAmmo { get=>loadedAmmo; internal set { if(value<loadedAmmo)LoadedAcquiredAmount-=Math.Max(0,loadedAmmo-value-(loadedAmmo-LoadedAcquiredAmount));loadedAmmo=value; } }
        /// <summary>每根火把剩余燃烧秒数；null 是从未点燃的新物品，0 是耗尽，不能补满。</summary>
        public float? TorchRemainingSeconds { get; internal set; }
        public int ItemId { get; }
        /// <summary>探索来源的本趟ID；null为纯基地来源，与运行期库存Guid分离。</summary>
        public string OriginExpeditionId { get; internal set; }
        /// <summary>堆叠中仍实际存在的当地取得数量，范围0～Amount；基地数量先被消耗。</summary>
        public int AcquiredAmount { get; internal set; }
        public int LoadedAcquiredAmount { get; internal set; }
        internal void MarkExploration(string id,int count)
        {if(string.IsNullOrEmpty(id)||count<0||count>Amount)throw new ArgumentException("物品来源无效");OriginExpeditionId=id;AcquiredAmount=count;}
        internal void ClearExplorationOrigin(){OriginExpeditionId=null;AcquiredAmount=0;LoadedAcquiredAmount=0;}
        internal int AcquiredSlice(int offset,int amount)=>Math.Max(0,offset+amount-(Amount-AcquiredAmount))-Math.Max(0,offset-(Amount-AcquiredAmount));
        /// <summary>合并来源前调用，避免目标数量增长后改变来源计算的基准。</summary>
        internal void MergeOrigin(ItemStack source,int offset,int amount)
        {
            if(source==null)return;var acquired=source.AcquiredSlice(offset,amount);if(acquired==0)return;
            if(AcquiredAmount+LoadedAcquiredAmount>0&&OriginExpeditionId!=source.OriginExpeditionId)throw new InvalidOperationException("不可合并不同远征来源");
            OriginExpeditionId=source.OriginExpeditionId;AcquiredAmount=checked(AcquiredAmount+acquired);
        }
        public int Amount { get; internal set; }

        public ItemStack Clone()
        {
            return new ItemStack(ItemId, Amount) { RuntimeIdentity = RuntimeIdentity, LoadedAmmo = LoadedAmmo, TorchRemainingSeconds = TorchRemainingSeconds, OriginExpeditionId=OriginExpeditionId,AcquiredAmount=AcquiredAmount,LoadedAcquiredAmount=LoadedAcquiredAmount };
        }
    }
}
