using System;
using System.Linq;

namespace ET.Server
{
    [ComponentOf(typeof(Scene))]
    public class OnlineAccountComponent: Entity, IAwake
    {
        public OnlineIdentityStoreSnapshot Snapshot = new();
        public string StorePath;
        public int RemainingFailSaveCount;
        public readonly System.Collections.Generic.Dictionary<long, System.Collections.Generic.Dictionary<string, OnlineProfileSaveOperationRecord>> SaveOperations = new();
    }

    [EnableClass]
    public class OnlineIdentityStoreSnapshot
    {
        public int SchemaVersion { get; set; } = 3;
        public long NextAccountId { get; set; } = 1000001;
        public long NextPlayerId { get; set; } = 2000001;
        public System.Collections.Generic.List<OnlineAccountRecord> Accounts { get; set; } = new();
        public System.Collections.Generic.List<OnlinePlayerProfileRecord> Players { get; set; } = new();
    }

    [EnableClass]
    public class OnlineAccountRecord
    {
        public long AccountId { get; set; }
        public string NormalizedUserName { get; set; }
        public string DisplayUserName { get; set; }
        public int AlgorithmVersion { get; set; }
        public int Iterations { get; set; }
        public string SaltBase64 { get; set; }
        public string HashBase64 { get; set; }
        public long PlayerId { get; set; }
        public string CreatedAtUtc { get; set; }
    }

    [EnableClass]
    public class OnlinePlayerProfileRecord
    {
        public long PlayerId { get; set; }
        public long AccountId { get; set; }
        public string DisplayName { get; set; }
        public long Revision { get; set; }
        public string CreatedAtUtc { get; set; }
        public string UpdatedAtUtc { get; set; }
        public bool HasReceivedOnlineStarterSupplies { get; set; }
        public OnlineInventoryRecord OnlineInventory { get; set; } = OnlineInventoryRecordHelper.Empty(12);
    }

    [EnableClass]
    public sealed class OnlineItemSlotRecord
    {
        public int SlotIndex { get; set; }
        public int ItemId { get; set; }
        public int Amount { get; set; }
        /// <summary>此库存武器已装弹药，随该实例移动和持久化。</summary>
        public int LoadedAmmo { get; set; }
    }

    [EnableClass]
    public sealed class OnlineEquipmentRecord
    {
        /// <summary>当前手持仅引用随身库存；空手 ItemId=0 且来源=-1。</summary>
        public int SchemaVersion { get; set; } = 1;
        public int SourceSlotIndex { get; set; } = -1;
        public int ItemId { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public int MeleeWeaponItemId => ItemId > 0 && ServerConfig.Instance.GetWeapon(ItemId).WeaponKind == WeaponKind.Melee ? ItemId : 0;
        [System.Text.Json.Serialization.JsonIgnore] public int RangedWeaponItemId => ItemId > 0 && ServerConfig.Instance.GetWeapon(ItemId).WeaponKind == WeaponKind.Projectile ? ItemId : 0;
        [System.Text.Json.Serialization.JsonIgnore] public int ActiveSlot => RangedWeaponItemId > 0 ? 2 : 1;
    }

    [EnableClass]
    public sealed class OnlineInventoryRecord
    {
        /// <summary>独立穿戴物品，零为空槽；旧账号缺省为零。</summary>
        public int TorsoItemId { get; set; }
        public int LegsItemId { get; set; }
        public int HeadItemId { get; set; }
        public int HandsItemId { get; set; }
        public int FeetItemId { get; set; }
        public int BackItemId { get; set; }
        public int ArmorItemId { get; set; }
        public int Capacity { get; set; }
        /// <summary>旧双槽回填产生的一次性补偿格数；普通角色为零。</summary>
        public int MigrationExtraCapacity { get; set; }
        public System.Collections.Generic.List<OnlineItemSlotRecord> Slots { get; set; } = new();
        public OnlineEquipmentRecord Equipment { get; set; } = new();
        public long Revision { get; set; }
    }

    [EnableClass]
    public sealed class OnlineProfileSaveOperationRecord
    {
        public string Fingerprint;
        public int Error;
        public string Message;
        public long Revision;
        public long CompletedAt;
    }

    public static class OnlineInventoryRecordHelper
    {
        /// <summary>按稳定槽位顺序读取穿戴；空记录返回全零，旧账户兼容。</summary>
        public static (int, ClothingSlot)[] ClothingItems(OnlineInventoryRecord value)=>new[]{
            (value?.TorsoItemId ?? 0,ClothingSlot.Torso),            (value?.LegsItemId ?? 0,ClothingSlot.Legs),            (value?.HeadItemId ?? 0,ClothingSlot.Head),            (value?.HandsItemId ?? 0,ClothingSlot.Hands),            (value?.FeetItemId ?? 0,ClothingSlot.Feet),            (value?.BackItemId ?? 0,ClothingSlot.Back),            (value?.ArmorItemId ?? 0,ClothingSlot.Armor)};
        public static int GetClothing(OnlineInventoryRecord value,ClothingSlot slot)=>ClothingItems(value).First(x=>x.Item2==slot).Item1;
        /// <summary>仅修改已验证操作的候选，发布由 World 库存事务负责。</summary>
        public static void SetClothing(OnlineInventoryRecord value,ClothingSlot slot,int id)
        {
            switch(slot){case ClothingSlot.Torso:value.TorsoItemId=id;break;case ClothingSlot.Legs:value.LegsItemId=id;break;case ClothingSlot.Head:value.HeadItemId=id;break;case ClothingSlot.Hands:value.HandsItemId=id;break;case ClothingSlot.Feet:value.FeetItemId=id;break;case ClothingSlot.Back:value.BackItemId=id;break;case ClothingSlot.Armor:value.ArmorItemId=id;break;default:throw new ArgumentOutOfRangeException(nameof(slot));}
        }
        /// <summary>从权威库存解析当前实例；不存在或替换后返回 null。</summary>
        public static OnlineItemSlotRecord HeldStack(OnlineInventoryRecord value)
            => value?.Equipment?.ItemId > 0 ? value.Slots?.Find(x => x.SlotIndex == value.Equipment.SourceSlotIndex && x.ItemId == value.Equipment.ItemId && x.Amount == 1) : null;

        public static OnlineInventoryRecord Empty(int capacity) => new()
        {
            Capacity = capacity,
            Revision = 1,
            Equipment = new OnlineEquipmentRecord(),
            Slots = new System.Collections.Generic.List<OnlineItemSlotRecord>()
        };

        public static OnlineInventoryRecord Clone(OnlineInventoryRecord value)
        {
            if (value == null) return null;
            OnlineInventoryRecord copy = new()
            {
                Capacity = value.Capacity,
                TorsoItemId=value.TorsoItemId, LegsItemId=value.LegsItemId,HeadItemId=value.HeadItemId,HandsItemId=value.HandsItemId,FeetItemId=value.FeetItemId,BackItemId=value.BackItemId,ArmorItemId=value.ArmorItemId,
                MigrationExtraCapacity = value.MigrationExtraCapacity,
                Revision = value.Revision,
                Equipment = new OnlineEquipmentRecord
                {
                    SchemaVersion = value.Equipment?.SchemaVersion ?? 0,
                    SourceSlotIndex = value.Equipment?.SourceSlotIndex ?? -1,
                    ItemId = value.Equipment?.ItemId ?? 0,
                }
            };
            if (value.Slots != null)
                foreach (OnlineItemSlotRecord slot in value.Slots)
                    copy.Slots.Add(new OnlineItemSlotRecord { SlotIndex = slot.SlotIndex, ItemId = slot.ItemId, Amount = slot.Amount, LoadedAmmo = slot.LoadedAmmo });
            return copy;
        }

        public static OnlineInventoryInfo ToMessage(OnlineInventoryRecord value)
        {
            if (value == null) return null;
            OnlineInventoryInfo message = OnlineInventoryInfo.Create();
            message.Capacity = value.Capacity;
            message.TorsoItemId=value.TorsoItemId;message.LegsItemId=value.LegsItemId;message.HeadItemId=value.HeadItemId;message.HandsItemId=value.HandsItemId;message.FeetItemId=value.FeetItemId;message.BackItemId=value.BackItemId;message.ArmorItemId=value.ArmorItemId;
            message.MigrationExtraCapacity = value.MigrationExtraCapacity;
            message.Revision = value.Revision;
            message.Equipment = OnlineEquipmentInfo.Create();
            message.Equipment.MeleeWeaponItemId = value.Equipment?.MeleeWeaponItemId ?? 0;
            message.Equipment.RangedWeaponItemId = value.Equipment?.RangedWeaponItemId ?? 0;
            message.Equipment.ActiveSlot = value.Equipment?.ActiveSlot ?? 0;
            message.Equipment.RangedLoadedAmmo = HeldStack(value)?.LoadedAmmo ?? 0;
            message.Equipment.SchemaVersion = value.Equipment?.SchemaVersion ?? 0;
            message.Equipment.SourceSlotIndex = value.Equipment?.SourceSlotIndex ?? -1;
            message.Equipment.ItemId = value.Equipment?.ItemId ?? 0;
            if (value.Slots != null)
                foreach (OnlineItemSlotRecord valueSlot in value.Slots)
                {
                    OnlineItemSlotInfo slot = OnlineItemSlotInfo.Create();
                    slot.SlotIndex = valueSlot.SlotIndex;
                    slot.ItemId = valueSlot.ItemId;
                    slot.Amount = valueSlot.Amount;
                    slot.LoadedAmmo = valueSlot.LoadedAmmo;
                    message.Slots.Add(slot);
                }
            return message;
        }

        public static OnlineInventoryRecord FromMessage(OnlineInventoryInfo value)
        {
            if (value == null) return null;
            OnlineInventoryRecord record = new()
            {
                Capacity = value.Capacity,
                TorsoItemId=value.TorsoItemId, LegsItemId=value.LegsItemId,HeadItemId=value.HeadItemId,HandsItemId=value.HandsItemId,FeetItemId=value.FeetItemId,BackItemId=value.BackItemId,ArmorItemId=value.ArmorItemId,
                MigrationExtraCapacity = value.MigrationExtraCapacity,
                Revision = value.Revision,
                Equipment = new OnlineEquipmentRecord
                {
                    SchemaVersion = value.Equipment?.SchemaVersion ?? 0,
                    SourceSlotIndex = value.Equipment?.SourceSlotIndex ?? -1,
                    ItemId = value.Equipment?.ItemId ?? 0,
                }
            };
            foreach (OnlineItemSlotInfo slot in value.Slots)
                record.Slots.Add(new OnlineItemSlotRecord { SlotIndex = slot.SlotIndex, ItemId = slot.ItemId, Amount = slot.Amount, LoadedAmmo = slot.LoadedAmmo });
            return record;
        }

        public static string Fingerprint(OnlineInventoryRecord value, bool received, long expectedRevision, string displayName)
        {
            System.Text.StringBuilder b = new();
            foreach(var pair in ClothingItems(value))b.Append(pair.Item1).Append('|');
            b.Append(expectedRevision).Append('|').Append(received).Append('|').Append(displayName).Append('|')
                .Append(value?.MigrationExtraCapacity ?? 0).Append('|').Append(value?.Equipment?.SourceSlotIndex ?? -1).Append('|').Append(value?.Capacity ?? 0).Append('|').Append(value?.Revision ?? 0).Append('|')
                .Append(value?.Equipment?.MeleeWeaponItemId ?? 0).Append('|').Append(value?.Equipment?.RangedWeaponItemId ?? 0)
                .Append('|').Append(value?.Equipment?.ActiveSlot ?? 0).Append('|').Append(HeldStack(value)?.LoadedAmmo ?? 0);
            if (value?.Slots != null)
                foreach (OnlineItemSlotRecord slot in value.Slots) b.Append('|').Append(slot.SlotIndex).Append(':').Append(slot.ItemId).Append(':').Append(slot.Amount).Append(':').Append(slot.LoadedAmmo);
            return b.ToString();
        }
    }
}
