using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace ET.Server
{
    /// <summary>单一手持的服务器定向契约检查；候选迁移不接触正式账号文件。</summary>
    public static partial class OnlineTestComponentSystem
    {
        private static async ETTask<int> RunHeldItemContracts(this OnlineTestComponent self)
        {
            int failed = 0;
            failed += await self.Case("WORLD-HELD-01 InPlaceHolsterReloadAndReferenceMove", async () =>
            {
                using var session = self.CreateSession(); using var login = await self.RegisterAndLogin(session, UniqueUser("held01"));
                using var entered = await session.Call(EnterWorld(Op(7101))) as G2C_EnterWorld;
                Ensure(entered.Error == 0, "enter failed");
                var initial = entered.World.SelfInventory;
                var player = entered.World.Players.First(x => x.UnitId == entered.World.SelfUnitId);
                var bow = initial.Slots.Single(x => x.ItemId == 6);
                int originalCount = initial.Slots.Count;
                using var hold = await session.Call(HeldOperation(Op(7102), initial.Revision, 9, bow.SlotIndex, 6, player.LifecycleRevision)) as G2C_OperateWorldInventory;
                Ensure(hold.Accepted && hold.SelfInventory.Slots.Count == originalCount && hold.SelfInventory.Equipment.ItemId == 6, "take out moved or lost item");
                using var repeated = await session.Call(HeldOperation(Op(7103), hold.SelfInventory.Revision, 9, bow.SlotIndex, 6, player.LifecycleRevision)) as G2C_OperateWorldInventory;
                Ensure(repeated.Accepted && repeated.SelfInventory.Revision == hold.SelfInventory.Revision, "same reference was not idempotent");
                using var reload = await session.Call(HeldOperation(Op(7104), repeated.SelfInventory.Revision, 11, bow.SlotIndex, 6, player.LifecycleRevision)) as G2C_OperateWorldInventory;
                Ensure(reload.Accepted && reload.SelfInventory.Slots.Single(x => x.ItemId == 6).LoadedAmmo == 1, "reload did not update weapon instance");
                using var holster = await session.Call(HeldOperation(Op(7105), reload.SelfInventory.Revision, 10, -1, 0, player.LifecycleRevision)) as G2C_OperateWorldInventory;
                Ensure(holster.Accepted && holster.SelfInventory.Equipment.ItemId == 0 && holster.SelfInventory.Slots.Single(x => x.ItemId == 6).LoadedAmmo == 1, "holster lost loaded ammo");
                using var holdAgain = await session.Call(HeldOperation(Op(7106), holster.SelfInventory.Revision, 9, bow.SlotIndex, 6, player.LifecycleRevision)) as G2C_OperateWorldInventory;
                using var move = await session.Call(InventoryOperation(Op(7107), holdAgain.SelfInventory.Revision, 0, 0, 1, 1, bow.SlotIndex, 1, 11, player.LifecycleRevision)) as G2C_OperateWorldInventory;
                Ensure(move.Accepted && move.SelfInventory.Equipment.SourceSlotIndex == 11 && move.SelfInventory.Slots.Single(x => x.SlotIndex == 11).LoadedAmmo == 1, "held reference did not follow move");
            });
            failed += await self.Case("WORLD-HELD-02 LegacySchemaAndWrongItemAreRejected", async () =>
            {
                using var session = self.CreateSession(); using var login = await self.RegisterAndLogin(session, UniqueUser("held02"));
                using var entered = await session.Call(EnterWorld(Op(7111))) as G2C_EnterWorld;
                var player = entered.World.Players.First(x => x.UnitId == entered.World.SelfUnitId);
                using var wrong = await session.Call(HeldOperation(Op(7112), entered.World.SelfInventory.Revision, 9, 0, 6, player.LifecycleRevision)) as G2C_OperateWorldInventory;
                Ensure(!wrong.Accepted && wrong.SelfInventory.Revision == entered.World.SelfInventory.Revision, "wrong instance changed inventory");
                var legacy = HeldOperation(Op(7113), entered.World.SelfInventory.Revision, 2, 0, 5, player.LifecycleRevision);
                using var rejected = await session.Call(legacy) as G2C_OperateWorldInventory;
                Ensure(!rejected.Accepted && rejected.Error != 0, "legacy equip action accepted");
                var stale = HeldOperation(Op(7114), entered.World.SelfInventory.Revision, 9, 0, 5, player.LifecycleRevision);stale.InventorySchemaVersion=0;
                using var schema = await session.Call(stale) as G2C_OperateWorldInventory;
                Ensure(!schema.Accepted && schema.Error != 0, "legacy protocol accepted");
            });
            failed += await self.Case("WORLD-HELD-03 FullLegacyMigrationPreservesCountsAndIsMinimal", async () =>
            {
                var inventory = OnlineInventoryRecordHelper.Empty(ServerConfig.Instance.OnlineWorldRule.BagCapacity);
                for(int i=0;i<inventory.Capacity;i++)inventory.Slots.Add(new OnlineItemSlotRecord{SlotIndex=i,ItemId=5,Amount=1});
                using var legacy = JsonDocument.Parse("{\"MeleeWeaponItemId\":5,\"RangedWeaponItemId\":6,\"ActiveSlot\":2,\"RangedLoadedAmmo\":1}");
                typeof(OnlineAccountComponentSystem).GetMethod("MigrateHeldInventory",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{inventory,legacy.RootElement});
                OnlineAccountComponentSystem.ValidateInventory(inventory,"held migration test");
                Ensure(inventory.MigrationExtraCapacity==3 && inventory.Capacity==15 && inventory.Slots.Count(x=>x.ItemId==5)==13 && inventory.Slots.Single(x=>x.ItemId==6).SlotIndex==inventory.Equipment.SourceSlotIndex,"legacy counts/reference mismatch");
                var roundtrip=OnlineInventoryRecordHelper.FromMessage(OnlineInventoryRecordHelper.ToMessage(inventory));
                OnlineAccountComponentSystem.ValidateInventory(roundtrip,"held roundtrip test");
                Ensure(OnlineInventoryRecordHelper.Fingerprint(roundtrip,false,0,"")==OnlineInventoryRecordHelper.Fingerprint(inventory,false,0,""),"migration fields lost during transfer");
                await self.Root().GetComponent<TimerComponent>().WaitAsync(1);
            });
            failed += await self.Case("WORLD-HELD-04 SourceValidationAndCloneLoadedAmmo", async () =>
            {
                var inventory=OnlineInventoryRecordHelper.Empty(12);inventory.Slots.Add(new OnlineItemSlotRecord{SlotIndex=3,ItemId=6,Amount=1,LoadedAmmo=1});inventory.Equipment.ItemId=6;inventory.Equipment.SourceSlotIndex=3;
                var clone=OnlineInventoryRecordHelper.Clone(inventory);OnlineAccountComponentSystem.ValidateInventory(clone,"held clone");
                Ensure(clone.Slots[0].LoadedAmmo==1 && OnlineInventoryRecordHelper.HeldStack(clone)!=OnlineInventoryRecordHelper.HeldStack(inventory),"clone ownership/ammo mismatch");
                clone.Equipment.SourceSlotIndex=4;bool rejected=false;try{OnlineAccountComponentSystem.ValidateInventory(clone,"invalid held source");}catch(InvalidOperationException){rejected=true;}Ensure(rejected,"missing source accepted");
                await self.Root().GetComponent<TimerComponent>().WaitAsync(1);
            });
            return failed;
        }
        private static C2G_OperateWorldInventory HeldOperation(string operation,long revision,int action,int source,int item,long lifecycle)
        {
            var q=C2G_OperateWorldInventory.Create();q.InventorySchemaVersion=1;q.OperationId=operation;q.ExpectedInventoryRevision=revision;q.ActionKind=action;q.SourceContainerKind=1;q.SourceSlotIndex=source;q.ExpectedItemId=item;q.LifecycleRevision=lifecycle;return q;
        }
    }
}
