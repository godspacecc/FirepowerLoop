using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Game
{
    public sealed class ItemSlotSaveData
    {
        public int Index { get; set; }
        public int ItemId { get; set; }
        public int Amount { get; set; }
        public int LoadedAmmo { get; set; }
        public float? TorchRemainingSeconds { get; set; }
        public string OriginExpeditionId {get;set;}
        public int AcquiredAmount {get;set;}
        public int LoadedAcquiredAmount {get;set;}
    }

    internal static class InventorySaveContracts
    {
        public const string CapacityKey = "capacity";
        public const string SlotsKey = "slots";
        public const string IndexKey = "index";
        public const string ItemIdKey = "itemId";
        public const string AmountKey = "amount";
        public const string RiskStateKey = "riskState";

        public static JArray CaptureSlots(ItemContainer container)
        {
            var slots = new JArray();
            foreach (var pair in container.Slots.OrderBy(pair => pair.Key))
            {
                slots.Add(new JObject
                {
                    [IndexKey] = pair.Key,
                    [ItemIdKey] = pair.Value.ItemId,
                    [AmountKey] = pair.Value.Amount
                    ,["loadedAmmo"] = pair.Value.LoadedAmmo
                    ,["originExpeditionId"]=pair.Value.OriginExpeditionId
                    ,["acquiredAmount"]=pair.Value.AcquiredAmount
                    ,["loadedAcquiredAmount"]=pair.Value.LoadedAcquiredAmount
                    ,["torchRemainingSeconds"] = pair.Value.TorchRemainingSeconds.HasValue ? new JValue(pair.Value.TorchRemainingSeconds.Value) : JValue.CreateNull()
                });
            }

            return slots;
        }

        public static bool ValidateSlots(
            JToken slotsToken,
            int capacity,
            IRuntimeConfigProvider config,
            out List<ItemSlotSaveData> slots,
            out string error)
        {
            slots = null;
            if (slotsToken == null || slotsToken.Type != JTokenType.Array)
            {
                error = "槽位必须为数组";
                return false;
            }

            var result = new List<ItemSlotSaveData>();
            var indexes = new HashSet<int>();
            foreach (var token in slotsToken.Children())
            {
                if (token.Type != JTokenType.Object)
                {
                    error = "槽位必须为对象";
                    return false;
                }

                var indexToken = token[IndexKey];
                var itemIdToken = token[ItemIdKey];
                var amountToken = token[AmountKey];
                if (indexToken?.Type != JTokenType.Integer ||
                    itemIdToken?.Type != JTokenType.Integer ||
                    amountToken?.Type != JTokenType.Integer)
                {
                    error = "槽位索引、ItemId 或数量无效";
                    return false;
                }

                var index = indexToken.Value<int>();
                var itemId = itemIdToken.Value<int>();
                var amount = amountToken.Value<int>();
                if (index < 0 || index >= capacity || !indexes.Add(index))
                {
                    error = "槽位索引超出范围或重复";
                    return false;
                }

                if (itemId <= 0)
                {
                    error = "槽位 ItemId 必须为正数";
                    return false;
                }

                string itemError = null;
                if (config == null || !config.TryGetItem(itemId, out var item, out itemError))
                {
                    error = string.IsNullOrEmpty(itemError) ? $"未找到物品：{itemId}" : itemError;
                    return false;
                }

                if (amount <= 0 || amount > item.MaxStackAmount)
                {
                    error = "槽位数量无效";
                    return false;
                }

                var loadedToken=token["loadedAmmo"];
                if(loadedToken!=null&&loadedToken.Type!=JTokenType.Integer){error="物品装填弹药格式无效";return false;}
                int loaded=loadedToken?.Value<int>()??0;
                if(loaded<0||loaded>0&&(amount!=1||!config.TryGetWeapon(itemId,out var weapon,out _)||loaded>weapon.MagazineCapacity)) {error="物品装填弹药无效";return false;}
                var fuelToken = token["torchRemainingSeconds"];
                float? fuel = null;
                if (fuelToken != null && fuelToken.Type != JTokenType.Null)
                {
                    if (fuelToken.Type != JTokenType.Float && fuelToken.Type != JTokenType.Integer) { error="火把余量格式无效"; return false; }
                    fuel=fuelToken.Value<float>();
                    if (item.ActionKind != global::ItemActionKind.ToggleTorch || amount != 1 || float.IsNaN(fuel.Value) || float.IsInfinity(fuel.Value) || fuel < 0f || fuel > item.BurnSeconds) { error="火把余量无效"; return false; }
                }
                // 来源字段不接受字符串或浮点数的隐式转换；旧档缺字段仍表示基地来源。
                var originToken=token["originExpeditionId"];
                if(originToken!=null&&originToken.Type!=JTokenType.Null&&originToken.Type!=JTokenType.String){error="库存探索来源身份格式无效";return false;}
                var origin=originToken?.Type==JTokenType.Null?null:originToken?.Value<string>();
                if(!TryReadOriginCount(token["acquiredAmount"],out var acquired)||!TryReadOriginCount(token["loadedAcquiredAmount"],out var loadedAcquired)) {error="库存探索来源数量格式无效";return false;}
                if(acquired<0||acquired>amount||loadedAcquired<0||loadedAcquired>loaded||(acquired>0||loadedAcquired>0)&&(string.IsNullOrEmpty(origin)||origin.Length!=32||!origin.All(Uri.IsHexDigit))){error="库存探索来源数量或身份无效";return false;}
                result.Add(new ItemSlotSaveData { Index = index, ItemId = itemId, Amount = amount, LoadedAmmo=loaded, TorchRemainingSeconds=fuel,OriginExpeditionId=origin,AcquiredAmount=acquired,LoadedAcquiredAmount=loadedAcquired });
            }

            slots = result;
            error = null;
            return true;
        }

        public static ItemContainer RestoreContainer(int capacity, IEnumerable<ItemSlotSaveData> slots)
        {
            var container = new ItemContainer(capacity);
            var snapshotSlots = new Dictionary<int, ItemStack>();
            foreach (var slot in slots.OrderBy(slot => slot.Index))
            {
                snapshotSlots[slot.Index] = new ItemStack(slot.ItemId, slot.Amount) { LoadedAmmo=slot.LoadedAmmo, TorchRemainingSeconds=slot.TorchRemainingSeconds,OriginExpeditionId=slot.OriginExpeditionId,AcquiredAmount=slot.AcquiredAmount,LoadedAcquiredAmount=slot.LoadedAcquiredAmount };
            }

            container.RestoreSnapshot(new ItemContainerSnapshot(capacity, snapshotSlots));
            return container;
        }
        private static bool TryReadOriginCount(JToken token,out int value)
        {
            value=0;if(token==null)return true;
            if(token.Type!=JTokenType.Integer)return false;
            try{value=token.Value<int>();return true;}catch{return false;}
        }
    }
}
