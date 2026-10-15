using System;
using System.Linq;
using System.Text.Json;

namespace ET.Server
{
    /// <summary>衣裤定向契约：使用独立内存 World，不连接或修改正式账号。</summary>
    [FriendOf(typeof(OnlineWorldComponent))]
    public static partial class OnlineTestComponentSystem
    {
        private static async ETTask<int> RunClothingContracts(this OnlineTestComponent self)
        {
            int failed=0;
            using var world=self.Root().AddComponent<OnlineWorldComponent>();
            world.MapStates.Clear();world.PlayerMapIds.Clear();
            var state=new OnlineWorldMapState{MapId="clothing-test"};world.MapStates.Add(state.MapId,state);
            var unit=new OnlineWorldUnit{PlayerId=91,UnitId=92,WorldId=world.WorldId,MapId=state.MapId,LifecycleRevision=1,CurrentHP=100,MaxHP=100};
            state.Units.Add(92,unit);state.PlayerUnits.Add(91,92);world.PlayerMapIds.Add(91,state.MapId);
            var inventory=OnlineInventoryRecordHelper.Empty(ServerConfig.Instance.OnlineWorldRule.BagCapacity);
            inventory.Slots.Add(new OnlineItemSlotRecord{SlotIndex=0,ItemId=38,Amount=1});
            inventory.Slots.Add(new OnlineItemSlotRecord{SlotIndex=1,ItemId=39,Amount=1});
            state.PlayerInventories.Add(91,inventory);
            P2W_OperateWorldInventory Request(string id,long revision,int action,int source,int item,int slot=0)
            {
                var q=P2W_OperateWorldInventory.Create();q.PlayerId=91;q.UnitId=92;q.WorldId=world.WorldId;q.MapId=state.MapId;q.LifecycleRevision=1;q.InventorySchemaVersion=1;
                q.OperationId=id;q.ExpectedInventoryRevision=revision;q.ActionKind=action;q.SourceContainerKind=1;q.SourceSlotIndex=source;q.ExpectedItemId=item;q.TargetSlotIndex=slot;return q;
            }
            failed+=await self.Case("CLOTHING-01 EquipReplayAndStaleRevision",async()=>
            {
                var q=Request("wear",1,12,0,38);var result=world.Operate(q);
                Ensure(result.Accepted&&result.Inventory.TorsoItemId==38&&!result.Inventory.Slots.Any(x=>x.ItemId==38),"equip did not transfer item");
                Ensure(world.Operate(q).Inventory.Revision==result.Inventory.Revision,"replay advanced revision");
                Ensure(!world.Operate(Request("stale",1,12,1,39)).Accepted,"stale revision accepted");
                Ensure(unit.TorsoItemId==38,"remote unit appearance missing");await ETTask.CompletedTask;
            });
            failed+=await self.Case("CLOTHING-02 FullBagSwapAndUnequipReject",async()=>
            {
                var current=state.PlayerInventories[91];
                for(int i=0;i<current.Capacity;i++)if(!current.Slots.Any(x=>x.SlotIndex==i))current.Slots.Add(new OnlineItemSlotRecord{SlotIndex=i,ItemId=5,Amount=1});
                var swapped=world.Operate(Request("swap",current.Revision,12,1,39));
                Ensure(swapped.Accepted&&swapped.Inventory.TorsoItemId==39&&swapped.Inventory.Slots.Single(x=>x.SlotIndex==1).ItemId==38,"full bag swap lost item");
                var denied=world.Operate(Request("full",swapped.Inventory.Revision,13,-1,0,1));
                Ensure(!denied.Accepted&&denied.Inventory.Revision==swapped.Inventory.Revision&&denied.Inventory.TorsoItemId==39,"full bag remove mutated state");await ETTask.CompletedTask;
            });
            failed+=await self.Case("CLOTHING-03 AccountWireAndFirstSightRoundtrip",async()=>
            {
                var current=state.PlayerInventories[91];
                var persisted=JsonSerializer.Deserialize<OnlineInventoryRecord>(JsonSerializer.Serialize(current));
                var wire=OnlineWorldSnapshotHelper.Clone(OnlineInventoryRecordHelper.ToMessage(persisted));
                var roundtrip=OnlineInventoryRecordHelper.FromMessage(wire);OnlineAccountComponentSystem.ValidateInventory(roundtrip,"clothing roundtrip");
                Ensure(roundtrip.TorsoItemId==39&&OnlineInventoryRecordHelper.Fingerprint(roundtrip,false,0,"")==OnlineInventoryRecordHelper.Fingerprint(current,false,0,""),"clothing lost across account or wire");
                var visible=OnlineWorldSnapshotHelper.Clone(OnlineWorldComponentSystem.Clone(unit));Ensure(visible.TorsoItemId==39,"first sight/reconnect player snapshot lost appearance");
                var legacy=JsonSerializer.Deserialize<OnlineInventoryRecord>("{\"Capacity\":12,\"Revision\":1}");Ensure(legacy.TorsoItemId==0&&legacy.LegsItemId==0,"legacy account received clothing");await ETTask.CompletedTask;
            });
            failed+=await self.Case("CLOTHING-04 WrongSlotAccountRejected",async()=>
            {
                var invalid=OnlineInventoryRecordHelper.Clone(state.PlayerInventories[91]);invalid.LegsItemId=38;
                bool rejected=false;try{OnlineAccountComponentSystem.ValidateInventory(invalid,"wrong clothing slot");}catch(InvalidOperationException){rejected=true;}Ensure(rejected,"wrong slot accepted");await ETTask.CompletedTask;
            });
            failed+=await self.Case("CLOTHING-06 SevenPartsAuthorityAccountAndRemoteRoundtrip",async()=>
            {
                for(int slot=2;slot<=7;slot++)
                {
                    var current=state.PlayerInventories[91];int id=slot==2?40:slot+39;
                    var source=current.Slots.FirstOrDefault(x=>x.SlotIndex==0);
                    if(source==null){source=new OnlineItemSlotRecord{SlotIndex=0};current.Slots.Add(source);}
                    source.ItemId=id;source.Amount=1;source.LoadedAmmo=0;
                    var worn=world.Operate(Request("seven:"+slot,current.Revision,12,0,id));Ensure(worn.Accepted,"new part wear rejected: "+slot);
                    Ensure(OnlineInventoryRecordHelper.GetClothing(state.PlayerInventories[91],(ClothingSlot)slot)==id,"new part missing");
                }
                var value=state.PlayerInventories[91];
                var persisted=JsonSerializer.Deserialize<OnlineInventoryRecord>(JsonSerializer.Serialize(value));
                var clone=OnlineWorldSnapshotHelper.Clone(OnlineInventoryRecordHelper.ToMessage(persisted));
                var roundtrip=OnlineInventoryRecordHelper.FromMessage(clone);OnlineAccountComponentSystem.ValidateInventory(roundtrip,"seven-part roundtrip");
                Ensure(OnlineInventoryRecordHelper.ClothingItems(value).SequenceEqual(OnlineInventoryRecordHelper.ClothingItems(roundtrip)),"new parts lost in account/wire clone");
                var visible=OnlineWorldSnapshotHelper.Clone(OnlineWorldComponentSystem.Clone(unit));
                Ensure(visible.HeadItemId==42&&visible.HandsItemId==43&&visible.FeetItemId==44&&visible.BackItemId==45&&visible.ArmorItemId==46,"remote appearance missing");
                var invalid=OnlineInventoryRecordHelper.Clone(value);invalid.HeadItemId=43;
                bool rejected=false;try{OnlineAccountComponentSystem.ValidateInventory(invalid,"wrong new slot");}catch(InvalidOperationException){rejected=true;}Ensure(rejected,"wrong new slot accepted");
                await ETTask.CompletedTask;
            });
            failed+=await self.Case("CLOTHING-05 DeathMovesWornItemsIntoCorpse",async()=>
            {
                int before=state.PlayerInventories[91].Slots.Sum(x=>x.Amount)+OnlineInventoryRecordHelper.ClothingItems(state.PlayerInventories[91]).Count(x=>x.Item1>0);
                world.TransitionPlayerDeath(state,unit,TimeInfo.Instance.ServerNow(),true);
                Ensure(state.PlayerInventories[91].TorsoItemId==0&&state.PlayerInventories[91].Slots.Count==0,"death retained inventory");
                var corpse=state.Corpses.Values.Single();Ensure(corpse.Slots.Sum(x=>x.Amount)==before&&corpse.Slots.Single(x=>x.ItemId==39).Amount==1,"death lost or duplicated clothes");await ETTask.CompletedTask;
                foreach(int id in new[]{40,42,43,44,45,46})Ensure(corpse.Slots.Single(x=>x.ItemId==id).Amount==1,"death lost new part "+id);
                Ensure(OnlineInventoryRecordHelper.ClothingItems(state.PlayerInventories[91]).All(x=>x.Item1==0),"death retained new part");
            });
            return failed;
        }
    }
}
