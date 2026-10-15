using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 保存当前世界会话中的掉落物状态，只记录领域数据，不持有 Unity 表现对象。
    /// </summary>
    public sealed class LootModel : AbstractModel
    {
        private readonly Dictionary<int, LootSnapshot> loot = new Dictionary<int, LootSnapshot>();

        public IReadOnlyDictionary<int, LootSnapshot> Loot => loot;

        protected override void OnInit()
        {
        }

        internal void Set(LootSnapshot snapshot)
        {
            loot[snapshot.UnitId] = snapshot;
        }

        internal bool TryAdd(LootSnapshot snapshot)
        {
            if (loot.ContainsKey(snapshot.UnitId))
            {
                return false;
            }

            loot.Add(snapshot.UnitId, snapshot);
            return true;
        }

        internal bool Remove(int unitId)
        {
            return loot.Remove(unitId);
        }

        internal void Clear()
        {
            loot.Clear();
        }

        public bool TryGet(int unitId, out LootSnapshot snapshot)
        {
            return loot.TryGetValue(unitId, out snapshot);
        }

        internal LootSnapshot CaptureSnapshot(int unitId)
        {
            return loot[unitId];
        }

        internal void RestoreSnapshot(LootSnapshot snapshot)
        {
            loot[snapshot.UnitId] = snapshot;
        }
    }

    /// <summary>
    /// 表示单个掉落物的资源数量、会话归属、空间位置和收集状态。
    /// </summary>
    public readonly struct LootSnapshot
    {
        public LootSnapshot(
            int unitId,
            long mapInstanceId,
            string configId,
            int itemId,
            int amount,
            Vector3 position,
            bool collected, float? torchRemainingSeconds = null, bool originKnown=false,string originExpeditionId=null,int acquiredAmount=0)
        {
            UnitId = unitId;
            MapInstanceId = mapInstanceId;
            ConfigId = configId;
            ItemId = itemId;
            Amount = amount;
            Position = position;
            Collected = collected;
            TorchRemainingSeconds = torchRemainingSeconds;
            OriginKnown=originKnown;OriginExpeditionId=originExpeditionId;AcquiredAmount=acquiredAmount;
        }

        public int UnitId { get; }
        public long MapInstanceId { get; }
        public string ConfigId { get; }
        public int ItemId { get; }
        public int Amount { get; }
        public Vector3 Position { get; }
        public bool Collected { get; }
        /// <summary>掉落火把的实例余量，null 表示尚未点燃的新道具。</summary>
        public float? TorchRemainingSeconds { get; }
        public bool OriginKnown {get;}
        public string OriginExpeditionId {get;}
        public int AcquiredAmount {get;}
        public LootSnapshot WithOrigin(string id,int acquired)=>new LootSnapshot(UnitId,MapInstanceId,ConfigId,ItemId,Amount,Position,Collected,TorchRemainingSeconds,true,id,acquired);
        public LootSnapshot WithTorchRemaining(float? seconds) => new LootSnapshot(UnitId,MapInstanceId,ConfigId,ItemId,Amount,Position,Collected,seconds,OriginKnown,OriginExpeditionId,AcquiredAmount);

        public LootSnapshot WithCollected()
        {
            return new LootSnapshot(UnitId, MapInstanceId, ConfigId, ItemId, 0, Position, true, TorchRemainingSeconds,OriginKnown,OriginExpeditionId,0);
        }

        public LootSnapshot WithRemainingAmount(int remainingAmount)
        {
            return new LootSnapshot(UnitId, MapInstanceId, ConfigId, ItemId, remainingAmount, Position, false, TorchRemainingSeconds,OriginKnown,OriginExpeditionId,System.Math.Min(remainingAmount,AcquiredAmount));
        }
    }
}
