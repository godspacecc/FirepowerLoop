using System;
using System.Linq;
using QFramework;

namespace Game
{
    public readonly struct ClothingEquipmentChangedEvent { }
    /// <summary>本机衣裤穿脱的唯一入口；先生成完整库存候选，再提交并通知，不处理角色资源。</summary>
    public sealed class ClothingEquipmentSystem : AbstractSystem
    {
        protected override void OnInit()
        {if(!this.GetSystem<SaveSystem>().RegisterSection(this.GetModel<ClothingEquipmentModel>(),out var error))Log.Error(error);}
        /// <summary>以物品运行期身份防止旧格号穿上替代物；替换旧衣物返回来源格，满包也保持物品守恒。</summary>
        public bool TryEquip(InventorySlotRef source,int expectedItemId,Guid expectedIdentity,out string error)
        {
            if(!CanModify(out error))return false;
            if(!InventorySystem.IsCarriedContainer(source.ContainerKind) || source.RuntimeContainerId!=0 ||
                !this.GetSystem<InventorySystem>().TryGetContainer(source,out var bag) || !bag.TryGet(source.SlotIndex,out var stack) ||
                stack.ItemId!=expectedItemId || stack.RuntimeIdentity!=expectedIdentity || stack.Amount!=1)
            {error="衣物来源已改变";return false;}
            if(!this.GetUtility<IRuntimeConfigProvider>().TryGetItem(stack.ItemId,out var item,out error) ||
                item.ItemType!=global::ItemType.Clothing || item.MaxStackAmount!=1 || !ClothingEquipmentModel.TryIndex(item.WearSlot,out var index))
            {error="物品不可穿戴";return false;}
            var model=this.GetModel<ClothingEquipmentModel>();
            var sourceSlots=bag.Slots.ToDictionary(p=>p.Key,p=>p.Value.Clone());
            var wearSlots=model.Container.Slots.ToDictionary(p=>p.Key,p=>p.Value.Clone());
            sourceSlots.Remove(source.SlotIndex);
            if(wearSlots.TryGetValue(index,out var old))sourceSlots[source.SlotIndex]=old;
            wearSlots[index]=stack.Clone();
            Commit(bag,new ItemContainerSnapshot(bag.Capacity,sourceSlots),new ItemContainerSnapshot(ClothingEquipmentModel.SlotCount,wearSlots));
            error=null;return true;
        }
        /// <summary>卸下只寻找背包空格；背包满或版本过期不改变任何物品和版本。</summary>
        public bool TryUnequip(global::ClothingSlot slot,long expectedRevision,out string error)
        {
            if(!CanModify(out error))return false;
            var model=this.GetModel<ClothingEquipmentModel>();
            if(model.Revision!=expectedRevision || !ClothingEquipmentModel.TryIndex(slot,out var index) || !model.Container.TryGet(index,out var item))
            {error="装备状态已改变";return false;}
            var bag=this.GetModel<PlayerBagModel>().Container;
            int free=-1;for(int i=0;i<bag.Capacity;i++)if(!bag.TryGet(i,out _)){free=i;break;}
            if(free<0){error="背包已满";return false;}
            var sourceSlots=bag.Slots.ToDictionary(p=>p.Key,p=>p.Value.Clone());
            var wearSlots=model.Container.Slots.ToDictionary(p=>p.Key,p=>p.Value.Clone());
            wearSlots.Remove(index); sourceSlots[free]=item.Clone();
            Commit(bag,new ItemContainerSnapshot(bag.Capacity,sourceSlots),new ItemContainerSnapshot(ClothingEquipmentModel.SlotCount,wearSlots));
            error=null;return true;
        }
        bool CanModify(out string error)
        {
            if(this.GetModel<MapSceneModel>().CurrentMode==MapPreparationMode.OnlinePlayersOnly)
            {error="联机穿戴必须由服务端提交";return false;}
            if(this.GetModel<CombatModel>().PlayerHP.Value<=0){error="穿戴需要存活玩家";return false;}
            return this.GetSystem<InventorySystem>().CanModifyPlayerBag(out error);
        }
        void Commit(ItemContainer bag,ItemContainerSnapshot inventory,ItemContainerSnapshot clothing)
        {
            // 所有候选验证已完成，发布事件前保证两处状态均为新状态。
            var previousBag=bag.CaptureSnapshot();var model=this.GetModel<ClothingEquipmentModel>();
            try { bag.RestoreSnapshot(inventory);model.Commit(clothing); }
            catch { bag.RestoreSnapshot(previousBag);throw; }
            this.GetSystem<InventorySystem>().CommitPlayerBagChanged("clothing equipment changed");
            NotifyChanged();
        }
        internal void NotifyChanged()
        {try{this.SendEvent(new ClothingEquipmentChangedEvent());}catch(Exception e){Log.Error(e.Message);}}
        /// <summary>远征失败清理随身穿戴；由既有结算事务保存或恢复，不独立写盘。</summary>
        public void ClearForRiskLoss()
        {this.GetModel<ClothingEquipmentModel>().Reset();NotifyChanged();}
    }
}
