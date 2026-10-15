using System;
using System.Collections.Generic;
using System.Linq;

namespace Game
{
    public enum ShopTradeKind
    {
        Buy = 0,
        Sell = 1
    }

    public enum ShopFailureReason
    {
        None = 0,
        SessionInvalid,
        StateNotAllowed,
        ConfigInvalid,
        InvalidQuantity,
        NotForSale,
        NotAccepted,
        InsufficientGold,
        InsufficientBagSpace,
        InsufficientItems,
        Overflow,
        OperationInProgress,
        SaveFailed
    }

    public readonly struct ShopItemQuote
    {
        public ShopItemQuote(string configId, int itemId, int order, int unitPrice, int ownedAmount, int maxQuantity, bool canTrade)
        {
            ConfigId = configId;
            ItemId = itemId;
            Order = order;
            UnitPrice = unitPrice;
            OwnedAmount = ownedAmount;
            MaxQuantity = maxQuantity;
            CanTrade = canTrade;
        }

        public string ConfigId { get; }
        public int ItemId { get; }
        public int Order { get; }
        public int UnitPrice { get; }
        public int OwnedAmount { get; }
        public int MaxQuantity { get; }
        public bool CanTrade { get; }
    }

    /// <summary>不可变商店展示快照；构造时复制商品集合，不暴露配置行或背包容器。</summary>
    public readonly struct ShopSnapshot
    {
        public ShopSnapshot(
            string shopId,
            string shopNameKey,
            string npcConfigId,
            int npcUnitId,
            ShopTradeKind tradeKind,
            int selectedItemId,
            int quantity,
            long gold,
            IReadOnlyList<ShopItemQuote> items,
            ShopFailureReason lastFailure,
            string lastErrorKey)
        {
            ShopId = shopId;
            ShopNameKey = shopNameKey;
            NpcConfigId = npcConfigId;
            NpcUnitId = npcUnitId;
            TradeKind = tradeKind;
            SelectedItemId = selectedItemId;
            Quantity = quantity;
            Gold = gold;
            Items = Array.AsReadOnly((items ?? Array.Empty<ShopItemQuote>()).ToArray());
            LastFailure = lastFailure;
            LastErrorKey = lastErrorKey;
        }

        public string ShopId { get; }
        public string ShopNameKey { get; }
        public string NpcConfigId { get; }
        public int NpcUnitId { get; }
        public ShopTradeKind TradeKind { get; }
        public int SelectedItemId { get; }
        public int Quantity { get; }
        public long Gold { get; }
        public IReadOnlyList<ShopItemQuote> Items { get; }
        public ShopFailureReason LastFailure { get; }
        public string LastErrorKey { get; }

        public static ShopSnapshot Empty()
        {
            return new ShopSnapshot(null, null, null, 0, ShopTradeKind.Buy, 0, 1, 0, Array.Empty<ShopItemQuote>(), ShopFailureReason.None, null);
        }
    }

    public readonly struct ShopTradeResult
    {
        public ShopTradeResult(ShopFailureReason failure, ShopSnapshot snapshot)
        {
            Failure = failure;
            Snapshot = snapshot;
        }

        public ShopFailureReason Failure { get; }
        public ShopSnapshot Snapshot { get; }
        public bool Success => Failure == ShopFailureReason.None;
    }

    public readonly struct ShopChangedEvent
    {
        public ShopChangedEvent(ShopSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public ShopSnapshot Snapshot { get; }
    }
}
