using System.Reflection;
using System;
using QFramework;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>验证真实衣物配置与穿戴事务，不修改正式资产。</summary>
    public sealed class ClothingEquipmentTests
    {
        PocketInventoryTests environment;
        [SetUp] public void SetUp()
        {
            environment = new PocketInventoryTests(); environment.SetUp();
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
        }
        [TearDown] public void TearDown() => environment.TearDown();
        object Owner(string name, string getter)
        {
            var type=typeof(Game).Assembly.GetType("Game."+name);
            Assert.That(type,Is.Not.Null,"穿戴领域尚未实现");
            return typeof(IArchitecture).GetMethod(getter).MakeGenericMethod(type).Invoke(Game.Interface,null);
        }
        object Model => Owner("ClothingEquipmentModel","GetModel");
        object System => Owner("ClothingEquipmentSystem","GetSystem");
        int Equipped(ClothingSlot slot)=>(int)Model.GetType().GetMethod("GetItemId").Invoke(Model,new object[]{slot});
        long Revision=>(long)Model.GetType().GetProperty("Revision").GetValue(Model);
        bool Equip(int index,int id,Guid identity)
        {var s=System;return (bool)s.GetType().GetMethod("TryEquip").Invoke(s,new object[]{new InventorySlotRef(InventoryContainerKind.PlayerBag,index),id,identity,null});}
        bool Unequip(ClothingSlot slot,long revision)
        {var s=System;return (bool)s.GetType().GetMethod("TryUnequip").Invoke(s,new object[]{slot,revision,null});}
        ItemContainer Bag=>Game.Interface.GetModel<PlayerBagModel>().Container;
        (int,Guid) Add(int id)
        {Bag.TryAdd(id,1,1,out var added,out var error);Assert.That(added,Is.EqualTo(1),error);foreach(var p in Bag.Slots)if(p.Value.ItemId==id)return(p.Key,p.Value.RuntimeIdentity);throw new Exception();}
        void Fill(){while(Bag.Slots.Count<Bag.Capacity)Bag.TryAdd(1,20,20,out _,out _);}

        [Test] public void EquipMovesExactlyOneItemOutOfBag()
        {var (i,g)=Add(38);Assert.That(Equip(i,38,g),Is.True);Assert.That(Bag.GetTotalAmount(38),Is.Zero);Assert.That(Equipped(ClothingSlot.Torso),Is.EqualTo(38));}
        [Test] public void FullBagReplacementReturnsOldClothesToSourceSlot()
        {var (i,g)=Add(38);Assert.That(Equip(i,38,g),Is.True);var (j,h)=Add(39);Fill();Assert.That(Equip(j,39,h),Is.True);Assert.That(Bag.TryGet(j,out var old),Is.True);Assert.That(old.ItemId,Is.EqualTo(38));Assert.That(Equipped(ClothingSlot.Torso),Is.EqualTo(39));}
        [Test] public void FullBagUnequipLeavesEquipmentAndRevisionUnchanged()
        {var (i,g)=Add(38);Assert.That(Equip(i,38,g),Is.True);Fill();long rev=Revision;Assert.That(Unequip(ClothingSlot.Torso,rev),Is.False);Assert.That(Revision,Is.EqualTo(rev));Assert.That(Equipped(ClothingSlot.Torso),Is.EqualTo(38));}
        [Test] public void StaleIdentityCannotEquipReplacementInstance()
        {var (i,g)=Add(38);Bag.TryRemove(i,1,out _,out _);Add(38);Assert.That(Equip(i,38,g),Is.False);Assert.That(Bag.GetTotalAmount(38),Is.EqualTo(1));}
        [Test] public void NonClothingCannotEnterEquipment()
        {var (i,g)=Add(1);Assert.That(Equip(i,1,g),Is.False);Assert.That(Equipped(ClothingSlot.Torso),Is.Zero);}
        [Test] public void RepeatedEquipDoesNotDuplicateItem()
        {var (i,g)=Add(38);Assert.That(Equip(i,38,g),Is.True);Assert.That(Equip(i,38,g),Is.False);Assert.That(Bag.GetTotalAmount(38),Is.Zero);Assert.That(Equipped(ClothingSlot.Torso),Is.EqualTo(38));}
        [Test] public void IndependentSlotsAndUnequipPreserveAllItems()
        {var (i,g)=Add(38);Equip(i,38,g);var(j,h)=Add(40);Equip(j,40,h);Assert.That(Unequip(ClothingSlot.Torso,Revision),Is.True);Assert.That(Equipped(ClothingSlot.Legs),Is.EqualTo(40));Assert.That(Bag.GetTotalAmount(38),Is.EqualTo(1));}
        [Test] public void StaleUnequipCannotRemoveNewEquipment()
        {var(i,g)=Add(38);Equip(i,38,g);long rev=Revision;var(j,h)=Add(39);Equip(j,39,h);Assert.That(Unequip(ClothingSlot.Torso,rev),Is.False);Assert.That(Equipped(ClothingSlot.Torso),Is.EqualTo(39));}
        [Test]
        public void AllSevenPartsCanEquipAndUnequipWithoutLosingOtherParts()
        {
            int[] ids={38,40,42,43,44,45,46};
            for(int slot=1;slot<=7;slot++)
            {
                var(i,g)=Add(ids[slot-1]);
                Assert.That(Equip(i,ids[slot-1],g),Is.True,"部位 "+slot);
                Assert.That(Equipped((ClothingSlot)slot),Is.EqualTo(ids[slot-1]));
            }
            for(int slot=7;slot>=1;slot--)
            {
                Assert.That(Unequip((ClothingSlot)slot,Revision),Is.True);
                Assert.That(Bag.GetTotalAmount(ids[slot-1]),Is.EqualTo(1));
                for(int remaining=1;remaining<slot;remaining++)
                    Assert.That(Equipped((ClothingSlot)remaining),Is.EqualTo(ids[remaining-1]));
            }
        }
        [Test]
        public void FourClothesAreConfiguredAsNonStackableWearables()
        {
            var provider = (IRuntimeConfigProvider)typeof(ItemInventoryFoundationTests)
                .GetMethod("CreateRealConfigProvider", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            foreach (int id in new[] {38,39,40,41})
            {
                Assert.That(provider.TryGetItem(id, out var item, out var error), Is.True, error);
                Assert.That(item.MaxStackAmount, Is.EqualTo(1));
                Assert.That(item.ItemType.ToString(), Is.EqualTo("Clothing"));
            }
        }
    }
}
