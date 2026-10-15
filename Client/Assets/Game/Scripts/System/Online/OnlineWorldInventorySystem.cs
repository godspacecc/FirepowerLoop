using System;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using QFramework;

namespace Game
{
    public sealed class OnlineWorldInventorySystem : AbstractSystem
    {
        private const int MoveStackAction = 1;
        private const int HoldAction = 9, HolsterAction = 10, ReloadRangedAction = 11;
        private long pendingCorpseId;
        private long pendingSearchActionId;
        private long pendingReloadActionId;
        private bool operationInFlight;

        public bool IsSearching => pendingCorpseId != 0;

        protected override void OnInit()
        {
            this.RegisterEvent<PlayerActionEndedEvent>(OnPlayerActionEnded);
            this.RegisterEvent<OnlineWorldSelfDamagedEvent>(_ => CancelAndClose(PlayerActionCancelReason.Damaged));
            this.RegisterEvent<MapLoadingStartedEvent>(_ => CancelAndClose(PlayerActionCancelReason.PlayerUnavailable));
            this.RegisterEvent<AppStateChangedEvent>(evt => { if(evt.Current!=AppState.Playing)CancelAndClose(PlayerActionCancelReason.PlayerUnavailable); });
            this.RegisterEvent<OnlineWorldChangedEvent>(evt => { if(evt.Snapshot==null||!evt.Snapshot.HasWorld||evt.Snapshot.ConnectionLost||evt.Snapshot.SelfUnitId==0)CancelAndClose(PlayerActionCancelReason.PlayerUnavailable); });
        }

        protected override void OnDeinit()=>CancelAndClose(PlayerActionCancelReason.PlayerUnavailable);

        public bool TryBeginCorpseSearch(long corpseId,out string error)
        {
            if(UIHelper.HasUIOpen){error="已打开的界面阻止搜索";return false;}
            OnlineWorldSnapshot world=this.GetSystem<OnlineWorldSystem>()?.Snapshot;
            OnlineWorldCorpseSnapshot corpse=world?.Corpses.FirstOrDefault(x=>x.CorpseId==corpseId)??default;
            OnlineWorldPlayerSnapshot player=world?.Players.FirstOrDefault(x=>x.UnitId==world.SelfUnitId)??default;
            if(world==null||!world.HasWorld||world.ConnectionLost||player.UnitId==0||player.IsDead||corpse.CorpseId==0||!corpse.HasContents||(player.LocalPosition-corpse.LocalPosition).sqrMagnitude>2.25f){error="搜索条件未满足";return false;}
            CancelSearch();CloseOpenCorpse();pendingCorpseId=corpseId;
            if(!this.GetSystem<PlayerActionSystem>().TryBegin(new PlayerActionRequest(PlayerActionKind.Search,1f,string.Empty),out pendingSearchActionId,out error)){pendingCorpseId=0;return false;}
            return true;
        }

        public void CancelSearch(){if(pendingSearchActionId!=0)this.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.OwnerCanceled);pendingSearchActionId=0;pendingCorpseId=0;}
        public void CloseOpenCorpse(){this.GetModel<OnlineWorldModel>()?.PublishOpenCorpse(null);}
        public bool TryGetOpenCorpse(out OnlineWorldCorpseContentsSnapshot snapshot){OnlineWorldCorpseContentsSnapshot? value=this.GetModel<OnlineWorldModel>()?.OpenCorpse.Value;snapshot=value??default;return value.HasValue;}

        public Task<bool> MoveWholeStackAsync(InventorySlotRef source,InventorySlotRef target)=>Operate(MoveStackAction,source,target);
        public Task<bool> EquipWeaponAsync(InventorySlotRef source,WeaponSlot slot)=>Operate(HoldAction,source,new InventorySlotRef(InventoryContainerKind.PlayerBag,-1));
        public Task<bool> UnequipWeaponAsync(WeaponSlot slot,int targetBagSlotIndex)=>Operate(HolsterAction,new InventorySlotRef(InventoryContainerKind.PlayerBag,-1),new InventorySlotRef(InventoryContainerKind.PlayerBag,targetBagSlotIndex));
        /// <summary>穿戴基于选择时的权威版本；不在本机预先移除物品。</summary>
        public Task<bool> EquipClothingAsync(InventorySlotRef source,long revision)=>Operate(12,source,default,revision);
        public Task<bool> UnequipClothingAsync(global::ClothingSlot slot,long revision)=>Operate(13,default,new InventorySlotRef(InventoryContainerKind.PlayerBag,(int)slot),revision);
        public Task<bool> HolsterAsync()=>Operate(HolsterAction,default,default);
        public Task<bool> SetActiveSlotAsync(WeaponSlot slot)
        {
            var inventory=this.GetSystem<OnlineWorldSystem>().Snapshot.SelfInventory;
            var config=this.GetUtility<IRuntimeConfigProvider>();var kind=slot==WeaponSlot.Ranged?global::WeaponKind.Projectile:global::WeaponKind.Melee;
            var candidates=inventory.Slots.Where(x=>config.TryGetWeapon(x.ItemId,out var weapon,out _)&&weapon.WeaponKind==kind).OrderBy(x=>x.SlotIndex).ToArray();
            if(candidates.Length==0)return Task.FromResult(false);
            int index=Array.FindIndex(candidates,x=>x.SlotIndex==inventory.Equipment.SourceSlotIndex);
            return EquipWeaponAsync(new InventorySlotRef(InventoryContainerKind.PlayerBag,candidates[(index+1)%candidates.Length].SlotIndex),slot);
        }

        public bool TryBeginRangedReload(out string error)
        {
            error = null;
            if (pendingReloadActionId != 0 || operationInFlight) { error = "联机换弹正在进行"; return false; }
            OnlineWorldSnapshot world = this.GetSystem<OnlineWorldSystem>()?.Snapshot;
            OnlineEquipmentSnapshot equipment = world?.SelfInventory.Equipment ?? default;
            if (world == null || !world.HasWorld || world.ConnectionLost || world.SelfUnitId == 0 || equipment.ActiveSlot != (int)WeaponSlot.Ranged || equipment.RangedWeaponItemId <= 0)
            { error = "当前不能换弹"; return false; }
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetWeapon(equipment.RangedWeaponItemId, out WeaponConfigDefinition weapon, out error) ||
                weapon.WeaponKind != global::WeaponKind.Projectile || weapon.ReloadSeconds <= 0f || weapon.MagazineCapacity <= 0 ||
                equipment.RangedLoadedAmmo >= weapon.MagazineCapacity || !world.SelfInventory.Slots.Any(x => x.ItemId == weapon.AmmoItemId && x.Amount > 0))
            { error ??= "当前不能换弹"; return false; }
            if (!this.GetSystem<PlayerActionSystem>().TryBegin(new PlayerActionRequest(PlayerActionKind.Reload, weapon.ReloadSeconds, string.Empty), out pendingReloadActionId, out error))
            { pendingReloadActionId = 0; return false; }
            var self = world.Players.FirstOrDefault(p => p.UnitId == world.SelfUnitId);
            SendReloadCue(CombatSoundCueKind.ReloadStart, equipment.RangedWeaponItemId, self.LocalPosition);
            return true;
        }

        private async Task<bool> Operate(int action,InventorySlotRef source,InventorySlotRef target,long? expectedRevision=null)
        {
            if(operationInFlight)return false;OnlineWorldSnapshot world=this.GetSystem<OnlineWorldSystem>().Snapshot;if(world==null||!world.HasWorld||world.ConnectionLost||world.SelfUnitId==0)return false;
            if(expectedRevision.HasValue&&world.SelfInventory.Revision!=expectedRevision.Value)return false;
            long corpseId=0,corpseRevision=0;if(source.ContainerKind==InventoryContainerKind.Search||target.ContainerKind==InventoryContainerKind.Search){if(!TryGetOpenCorpse(out OnlineWorldCorpseContentsSnapshot corpse))return false;corpseId=corpse.Corpse.CorpseId;corpseRevision=corpse.Corpse.StateRevision;}
            int sourceKind=ContainerKind(source.ContainerKind);
            int targetKind=ContainerKind(target.ContainerKind);operationInFlight=true;
            try
            {
                var accepted = await this.GetSystem<OnlineWorldSystem>().OperateWorldInventoryAsync(
                    Guid.NewGuid().ToString("N"), world.SelfInventory.Revision, corpseId, corpseRevision,
                    action, sourceKind, source.SlotIndex, targetKind, target.SlotIndex);
                // 只在权威响应已应用后反馈收取；拒绝、超时或过期连接不播放成功音。
                if (accepted && action == MoveStackAction && source.ContainerKind == InventoryContainerKind.Search &&
                    target.ContainerKind == InventoryContainerKind.PlayerBag)
                    this.SendEvent(new SearchItemCollectedEvent());
                return accepted;
            }
            finally{operationInFlight=false;}
        }

        private static int ContainerKind(InventoryContainerKind kind)=>kind==InventoryContainerKind.Search?2:kind==InventoryContainerKind.PlayerBag?1:0;
        private void OnPlayerActionEnded(PlayerActionEndedEvent evt)
        {
            if(evt.ActionId==pendingSearchActionId&&evt.Kind==PlayerActionKind.Search){long corpse=pendingCorpseId;pendingSearchActionId=0;pendingCorpseId=0;if(evt.Completed)OpenAfterSearch(corpse).Forget();return;}
            if(evt.ActionId==pendingReloadActionId&&evt.Kind==PlayerActionKind.Reload)
            {
                pendingReloadActionId=0;
                if(evt.Completed)CompleteReload().Forget();
                else this.SendEvent(new CombatSoundCueEvent(CombatSoundCueKind.ReloadCancel, 0, UnityEngine.Vector3.zero));
            }
        }
        private async UniTaskVoid OpenAfterSearch(long corpseId){if(await this.GetSystem<OnlineWorldSystem>().OpenWorldCorpseAsync(corpseId))SearchContainerUICtrl.TryShow(corpseId,out _);}
        /// <summary>服务器接受换弹并应用库存后才发插入／上膛声；取消与失败不能播放完成反馈。</summary>
        private async UniTaskVoid CompleteReload()
        {
            var before = this.GetSystem<OnlineWorldSystem>().Snapshot;
            var weapon = before.SelfInventory.Equipment.RangedWeaponItemId;
            var expectedMap = this.GetModel<MapSceneModel>().CurrentHandle;
            var position = before.Players.FirstOrDefault(p => p.UnitId == before.SelfUnitId).LocalPosition;
            SendReloadCue(CombatSoundCueKind.ReloadCancel, weapon, position);
            if (await Operate(ReloadRangedAction, new InventorySlotRef(InventoryContainerKind.PlayerBag,this.GetSystem<OnlineWorldSystem>().Snapshot.SelfInventory.Equipment.SourceSlotIndex), default) &&
                this.GetSystem<OnlineWorldSystem>().Snapshot.WorldId == before.WorldId &&
                this.GetModel<MapSceneModel>().CurrentHandle == expectedMap)
                SendReloadCue(CombatSoundCueKind.ReloadFinish, weapon, position);
        }
        private void SendReloadCue(CombatSoundCueKind kind, int weapon, UnityEngine.Vector3 localPosition)
        {
            var root = this.GetModel<MapSceneModel>().CurrentHandle?.Context?.DynamicObjectRoot;
            if (root != null) this.SendEvent(new CombatSoundCueEvent(kind, weapon, root.TransformPoint(localPosition)));
        }
        private void CancelAndClose(PlayerActionCancelReason reason){if(pendingSearchActionId!=0||pendingReloadActionId!=0)this.GetSystem<PlayerActionSystem>().CancelActive(reason);pendingSearchActionId=0;pendingReloadActionId=0;pendingCorpseId=0;CloseOpenCorpse();}
    }
}
