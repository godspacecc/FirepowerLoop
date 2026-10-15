using System;
using System.Collections.Generic;
using QFramework;

namespace Game
{
    /// <summary>
    /// 记录一次玩家背包主动丢弃已创建的世界掉落物，只用于在同一事务失败时撤销本次世界创建。
    /// </summary>
    public readonly struct PlayerInventoryDropReceipt
    {
        public PlayerInventoryDropReceipt(IReadOnlyList<int> lootUnitIds)
        {
            LootUnitIds = lootUnitIds ?? Array.Empty<int>();
        }

        public IReadOnlyList<int> LootUnitIds { get; }

        public static PlayerInventoryDropReceipt Empty =>
            new PlayerInventoryDropReceipt(Array.Empty<int>());
    }

    /// <summary>
    /// 为玩家背包主动丢弃提供同步的世界创建与事务回滚边界，不负责扣除或恢复背包物品。
    /// </summary>
    public interface IPlayerInventoryDropPort : IUtility
    {
        /// <summary>
        /// 在当前玩家附近创建一组与背包整堆数量一致的运行时掉落物。
        /// </summary>
        /// <param name="itemId">要丢弃的物品配置 Id。</param>
        /// <param name="amount">要丢弃的整堆数量。</param>
        /// <param name="receipt">成功创建的世界对象收据，只用于撤销本次创建；失败时为空收据。</param>
        /// <param name="error">失败原因；成功时为空。</param>
        /// <returns>世界对象已完整创建并归属活动地图时返回 true。</returns>
        bool TryCreatePlayerInventoryDrop(
            int itemId,
            int amount,
            out PlayerInventoryDropReceipt receipt,
            out string error);

        /// <summary>
        /// 撤销收据对应的本次玩家背包主动丢弃世界对象；空收据、未知对象和重复回滚均安全返回。
        /// </summary>
        /// <param name="receipt">仅标识本次世界创建的回滚收据。</param>
        void RollbackPlayerInventoryDrop(PlayerInventoryDropReceipt receipt);
    }
}
