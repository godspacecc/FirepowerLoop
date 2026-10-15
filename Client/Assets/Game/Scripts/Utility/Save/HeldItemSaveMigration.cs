using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Game
{
    /// <summary>协调旧双槽与随身库存的候选迁移；不直接写文件或修改运行模型。</summary>
    internal static class HeldItemSaveMigration
    {
        internal static bool TryMigrate(JObject sections, IRuntimeConfigProvider configs, out JObject migrated, out bool changed, out string error)
        {
            migrated = null; changed = false; error = null;
            try
            {
                var next = (JObject)sections.DeepClone();
                var equipment = next[WeaponEquipmentModel.Section] as JObject;
                if (equipment == null) { migrated = next; return true; }
                var bag = next["another-day.player-bag"] as JObject;
                if (bag?["capacity"]?.Type != JTokenType.Integer || bag["capacity"].Value<int>() < InventoryDefaults.PlayerBagCapacity ||
                    !InventorySaveContracts.ValidateSlots(bag["slots"],bag["capacity"].Value<int>(),configs,out var slots,out error)) return false;
                int capacity=bag["capacity"].Value<int>();
                var container=InventorySaveContracts.RestoreContainer(capacity,slots);
                if (equipment.Property("schemaVersion") == null)
                {
                    if (!WeaponEquipmentModel.ValidateLegacy(equipment,configs,out error)) return false;
                    int melee=equipment["meleeWeaponItemId"].Value<int>(),ranged=equipment["rangedWeaponItemId"].Value<int>();
                    string active=equipment["activeSlot"].Value<string>();
                    int selectedId=active=="Ranged"?ranged:active=="Melee"?melee:0,selectedIndex=-1;
                    foreach (int id in new[]{melee,ranged})
                    {
                        if(id==0)continue;
                        AddPreserving(container,id,1,configs);
                        // 武器不堆叠，取新回填格，不能误认已有同类库存实例。
                        int index=container.Slots.Keys.Except(slots.Select(s=>s.Index)).OrderByDescending(i=>i).First();
                        slots.Add(new ItemSlotSaveData{Index=index,ItemId=id,Amount=1});
                        if(id==selectedId)selectedIndex=index;
                    }
                    int loaded=equipment["rangedLoadedAmmo"]?.Value<int>()??0;
                    if(loaded>0)
                    {
                        if(!configs.TryGetWeapon(ranged,out var weapon,out error))return false;
                        AddPreserving(container,weapon.AmmoItemId,loaded,configs);
                    }
                    bool supported=selectedIndex>=0&&WeaponEquipmentModel.ValidateHeldWeapon(selectedId,configs,out _);
                    next[WeaponEquipmentModel.Section]=new JObject{["schemaVersion"]=1,["sourceContainerKind"]=supported?(int)InventoryContainerKind.PlayerBag:-1,["sourceSlotIndex"]=supported?selectedIndex:-1,["itemId"]=supported?selectedId:0};
                    bag["capacity"]=container.Capacity;bag["slots"]=InventorySaveContracts.CaptureSlots(container);changed=true;
                }
                if(!WeaponEquipmentModel.TryReadHeld(next[WeaponEquipmentModel.Section],out var source,out var itemId,out error) || itemId>0&&!WeaponEquipmentModel.ValidateHeldWeapon(itemId,configs,out error))return false;
                if(source.HasValue)
                {
                    var token=source.Value.ContainerKind==InventoryContainerKind.Pocket?bag["pocketSlots"]:bag["slots"];
                    int limit=source.Value.ContainerKind==InventoryContainerKind.Pocket?InventoryDefaults.PlayerPocketCapacity:bag["capacity"].Value<int>();
                    if(!InventorySaveContracts.ValidateSlots(token??new JArray(),limit,configs,out var sourceSlots,out error)||!sourceSlots.Any(s=>s.Index==source.Value.SlotIndex&&s.ItemId==itemId&&s.Amount==1)) {error="手持引用与随身库存不一致";return false;}
                }
                migrated=next;return true;
            }
            catch(Exception e){error="手持迁移失败："+e.Message;return false;}
        }
        /// <summary>先利用合并与空格，补偿只覆盖本次旧装备所需缺口；所有操作发生在迁移候选中。</summary>
        private static void AddPreserving(ItemContainer container,int id,int amount,IRuntimeConfigProvider configs)
        {
            if(!configs.TryGetItem(id,out var item,out var error))throw new InvalidOperationException(error);
            int acceptable=container.CalculateAcceptableAmount(id,amount,item.MaxStackAmount);
            if(acceptable<amount)
            {
                int extra=(amount-acceptable+item.MaxStackAmount-1)/item.MaxStackAmount;
                container.RestoreSnapshot(new ItemContainerSnapshot(checked(container.Capacity+extra),container.Slots.ToDictionary(p=>p.Key,p=>p.Value.Clone())));
            }
            if(!container.TryAdd(id,amount,item.MaxStackAmount,out var accepted,out error)||accepted!=amount)throw new InvalidOperationException(error??"旧物品无法完整回填");
        }
    }
}
