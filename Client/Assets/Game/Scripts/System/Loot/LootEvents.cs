using UnityEngine;
using System.Collections.Generic;

namespace Game
{
    public readonly struct LootCreatedEvent
    {
        public LootCreatedEvent(int lootUnitId, long mapInstanceId, string configId, int itemId, int amount)
        {
            LootUnitId = lootUnitId;
            MapInstanceId = mapInstanceId;
            ConfigId = configId;
            ItemId = itemId;
            Amount = amount;
        }

        public int LootUnitId { get; }
        public long MapInstanceId { get; }
        public string ConfigId { get; }
        public int ItemId { get; }
        public int Amount { get; }
    }

    /// <summary>
    /// 请求在指定世界会话中创建掉落物，供敌人掉落规则通知表现控制器。
    /// </summary>
    public readonly struct LootDropRequestedEvent
    {
        public LootDropRequestedEvent(long mapInstanceId, string lootConfigId, Vector3 position, Quaternion rotation)
        {
            MapInstanceId = mapInstanceId;
            LootConfigId = lootConfigId;
            Position = position;
            Rotation = rotation;
        }

        public long MapInstanceId { get; }
        public string LootConfigId { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
    }

    public readonly struct LootDropBatchEntry
    {
        public LootDropBatchEntry(string lootConfigId, int amountOverride, Vector3 offset)
        {
            LootConfigId = lootConfigId;
            AmountOverride = amountOverride;
            Offset = offset;
        }

        public string LootConfigId { get; }
        public int AmountOverride { get; }
        public Vector3 Offset { get; }
    }

    public readonly struct LootDropBatchRequestedEvent
    {
        public LootDropBatchRequestedEvent(
            long mapInstanceId,
            int sourceEnemyUnitId,
            Vector3 position,
            Quaternion rotation,
            IReadOnlyList<LootDropBatchEntry> entries)
        {
            MapInstanceId = mapInstanceId;
            SourceEnemyUnitId = sourceEnemyUnitId;
            Position = position;
            Rotation = rotation;
            Entries = entries;
        }

        public long MapInstanceId { get; }
        public int SourceEnemyUnitId { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public IReadOnlyList<LootDropBatchEntry> Entries { get; }
    }

    /// <summary>
    /// 掉落物被收集后发布的领域事件。
    /// </summary>
    public readonly struct LootCollectedEvent
    {
        public LootCollectedEvent(int lootUnitId, int collectorUnitId, string configId, int itemId, int acceptedAmount, int remainingAmount)
        {
            LootUnitId = lootUnitId;
            CollectorUnitId = collectorUnitId;
            ConfigId = configId;
            ItemId = itemId;
            AcceptedAmount = acceptedAmount;
            RemainingAmount = remainingAmount;
        }

        public int LootUnitId { get; }
        public int CollectorUnitId { get; }
        public string ConfigId { get; }
        public int ItemId { get; }
        public int AcceptedAmount { get; }
        public int RemainingAmount { get; }
    }
}
