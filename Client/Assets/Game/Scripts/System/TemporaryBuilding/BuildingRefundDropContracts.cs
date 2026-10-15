using System;
using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    public readonly struct BuildingRefundDrop
    {
        public BuildingRefundDrop(int itemId, int amount, Vector3 offset)
        {
            ItemId = itemId;
            Amount = amount;
            Offset = offset;
        }

        public int ItemId { get; }
        public int Amount { get; }
        public Vector3 Offset { get; }
    }

    public readonly struct BuildingRefundDropReceipt
    {
        public BuildingRefundDropReceipt(IReadOnlyList<int> lootUnitIds)
        {
            LootUnitIds = lootUnitIds ?? Array.Empty<int>();
        }

        public IReadOnlyList<int> LootUnitIds { get; }
        public static BuildingRefundDropReceipt Empty => new BuildingRefundDropReceipt(Array.Empty<int>());
    }

    /// <summary>建筑拆除使用的原子地面掉落端口；端口本身从不标脏。</summary>
    public interface IBuildingRefundDropPort : IUtility
    {
        bool TryCreateBuildingRefundDrops(long mapInstanceId, Vector3 position, Quaternion rotation,
            IReadOnlyList<BuildingRefundDrop> drops, out BuildingRefundDropReceipt receipt, out string error);
        void RollbackBuildingRefundDrops(BuildingRefundDropReceipt receipt);
    }
}
