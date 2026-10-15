using System;
using System.Linq;
using QFramework;

namespace Game
{
    public readonly struct WeaponEquipmentChangedEvent { }
    /// <summary>当前手持及火把燃烧的唯一写入口；引用随身库存并推进燃烧，不维护额外装备库存或模型资源。</summary>
    public sealed partial class WeaponEquipmentSystem : AbstractSystem
    {
        protected override void OnInit()
        { if(!this.GetSystem<SaveSystem>().RegisterSection(this.GetModel<WeaponEquipmentModel>(),out var error))Log.Error(error); LifeCycle.RegisterUpdate(UpdateTorch); }
        protected override void OnDeinit() { LifeCycle.UnregisterUpdate(UpdateTorch); }
        public HeldItemSnapshot CaptureHeldSnapshot()=>this.GetModel<WeaponEquipmentModel>().CaptureHeldSnapshot();
        /// <summary>拿出实际随身格中的武器或火把；重复同一实例幂等，失败不改变数量、状态或脏标记。</summary>
        public bool TryHold(InventorySlotRef source,int expectedItemId,out string error)
        {
            error=null;
            if(!CanModifyEquipment(out error))return false;
            if(!InventorySystem.IsCarriedContainer(source.ContainerKind)||source.RuntimeContainerId!=0||!this.GetSystem<InventorySystem>().TryGetContainer(source,out var container)||!container.TryGet(source.SlotIndex,out var stack)||stack.ItemId!=expectedItemId||stack.Amount!=1||!WeaponEquipmentModel.ValidateHeldWeapon(stack.ItemId,this.GetUtility<IRuntimeConfigProvider>(),out error)) { error="只能拿出随身库存中已配置的武器或火把";return false; }
            var model=this.GetModel<WeaponEquipmentModel>();
            if(model.SourceIsCurrent(source)&&model.HeldItemId==stack.ItemId&&ValidateCurrentSource())return true;
            model.SetHeld(source,stack.ItemId,stack);MarkChanged("held item changed");return true;
        }
        public bool TryEquipFromPlayerBag(int index,out string error)=>TryEquipFromSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag,index),WeaponSlot.None,out error);
        public bool TryEquipFromSlot(InventorySlotRef source,WeaponSlot kind,out string error)
        {
            error=null;
            if(!this.GetSystem<InventorySystem>().TryGetContainer(source,out var c)||!c.TryGet(source.SlotIndex,out var s)) {error="来源物品不存在";return false;}
            if(kind!=WeaponSlot.None&&kind!=(this.GetUtility<IRuntimeConfigProvider>().TryGetWeapon(s.ItemId,out var candidateWeapon,out _)&&candidateWeapon.WeaponKind==global::WeaponKind.Projectile?WeaponSlot.Ranged:WeaponSlot.Melee)){error="候选武器类型不匹配";return false;}
            return TryHold(source,s.ItemId,out error);
        }
        /// <summary>收起不转移物品，因此满库存也可执行；空手重复收起不发事件。</summary>
        public bool TryHolster(out string error)
        { if(!CanModifyEquipment(out error))return false;var m=this.GetModel<WeaponEquipmentModel>();if(m.HeldItemId==0)return true;m.Reset();MarkChanged("held item holstered");return true; }
        public bool TryUnequipToPlayerBag(WeaponSlot slot,out string error)=>TryHolster(out error);
        public bool TrySetActiveSlot(WeaponSlot slot,out string error)
        {
            if (slot != WeaponSlot.None && slot != WeaponSlot.Melee && slot != WeaponSlot.Ranged) { error="无效的手持操作"; return false; }
            return slot==WeaponSlot.None?TryHolster(out error):TryCycleHeldWeapon(slot==WeaponSlot.Ranged?global::WeaponKind.Projectile:global::WeaponKind.Melee,out error);
        }
        public bool TryCycleHeldWeapon(global::WeaponKind kind,out string error)
        {
            error=null;if(!CanModifyEquipment(out error))return false;
            var config=this.GetUtility<IRuntimeConfigProvider>();var bag=this.GetModel<PlayerBagModel>();
            var candidates=bag.Container.Slots.OrderBy(p=>p.Key).Where(p=>config.TryGetWeapon(p.Value.ItemId,out var weapon,out _)&&weapon.WeaponKind==kind).Select(p=>new InventorySlotRef(InventoryContainerKind.PlayerBag,p.Key)).Concat(bag.Pocket.Slots.OrderBy(p=>p.Key).Where(p=>config.TryGetWeapon(p.Value.ItemId,out var weapon,out _)&&weapon.WeaponKind==kind).Select(p=>new InventorySlotRef(InventoryContainerKind.Pocket,p.Key))).ToArray();
            if(candidates.Length==0){error="没有可拿出的对应武器";return false;}
            var m=this.GetModel<WeaponEquipmentModel>();int index=Array.FindIndex(candidates,m.SourceIsCurrent);var source=candidates[(index+1)%candidates.Length];TryGetSource(source,out var candidate);return TryHold(source,candidate.ItemId,out error);
        }
        /// <summary>库存事务成功后跟随来源移动；转出随身库存则收起，不独立提交库存变化。</summary>
        internal void FollowTransfer(InventorySlotRef source,InventorySlotRef target)
        { var m=this.GetModel<WeaponEquipmentModel>();if(!m.SourceIsCurrent(source))return;if(InventorySystem.IsCarriedContainer(target.ContainerKind)&&TryGetSource(target,out var s)&&s.ItemId==m.HeldItemId){ var lit=m.TorchLit; m.SetHeld(target,m.HeldItemId,s); m.TorchLit=lit; }else m.Reset();MarkChanged("held source transferred"); }
        internal void ValidateSourceAfterInventoryMutation()
        {var m=this.GetModel<WeaponEquipmentModel>();if(!ValidateCurrentSource()){m.Reset();MarkChanged("held source invalidated");}}
        private bool TryGetSource(InventorySlotRef source,out ItemStack stack)
        { stack=null;return this.GetSystem<InventorySystem>().TryGetContainer(source,out var container)&&container.TryGet(source.SlotIndex,out stack); }
        private bool ValidateCurrentSource()
        {
            var m=this.GetModel<WeaponEquipmentModel>();
            if(!m.HeldSource.HasValue)return true;
            if(!TryGetSource(m.HeldSource.Value,out var s)||s.ItemId!=m.HeldItemId || s.Amount != 1)return false;
            if(m.HeldIdentity!=Guid.Empty && s.RuntimeIdentity!=m.HeldIdentity)return false;
            // 事务回滚可能替换为保留身份的克隆；装填必须绑定当前容器内的实际实例。
            m.BindSource(s);
            return true;
        }
        public WeaponEquipmentSnapshot CaptureSnapshot()=>this.GetModel<WeaponEquipmentModel>().CaptureSnapshot();
        public void RestoreSnapshot(WeaponEquipmentSnapshot s)=>this.GetModel<WeaponEquipmentModel>().RestoreSnapshot(s);
        /// <summary>装填或发射成功后发布库存实例变化；不转移手持来源。</summary>
        internal void CommitExternalMutation(string reason)
        { MarkChanged(reason); this.SendEvent(new InventoryChangedEvent()); }
        public void ClearForRiskLoss(){var m=this.GetModel<WeaponEquipmentModel>();if(m.HeldItemId==0)return;m.Reset();MarkChanged("held risk loss");}
        private bool CanModifyEquipment(out string error)
        {if(this.GetModel<CombatModel>().PlayerHP.Value<=0){error="手持操作需要存活玩家";return false;}return this.GetSystem<InventorySystem>().CanModifyPlayerBag(out error);}
        private void MarkChanged(string reason)
        {
            this.GetSystem<MobileInputSystem>().ClearCombatInput();
            this.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.ContextInvalid);
            this.GetSystem<SaveSystem>().MarkDirty(reason);
            try{this.SendEvent(new WeaponEquipmentChangedEvent());}catch(Exception e){Log.Error(e.Message);}
        }
    }
}
