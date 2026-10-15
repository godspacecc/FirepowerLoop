using System;
using System.Reflection;
using NUnit.Framework;
using QFramework;

namespace Game.Tests
{
    /// <summary>检查随身物品单一手持及库存守恒；复用真实架构测试环境，不写正式资源。</summary>
    public sealed class HeldItemDomainTests
    {
        private WeaponEquipmentTests environment;
        private WeaponEquipmentSystem System => Game.Interface.GetSystem<WeaponEquipmentSystem>();
        private PlayerBagModel Bag => Game.Interface.GetModel<PlayerBagModel>();
        [SetUp] public void SetUp() { environment = new WeaponEquipmentTests(); environment.SetUp(); }
        [TearDown] public void TearDown() => environment.TearDown();
        private void Add(int id) => Assert.That(Bag.Container.TryAdd(id, 1, 1, out _, out var error), Is.True, error);
        private void Hold(int index) => Assert.That(System.TryEquipFromPlayerBag(index, out var error), Is.True, error);
        private void Holster()
        {
            var method = typeof(WeaponEquipmentSystem).GetMethod("TryHolster");
            Assert.That(method, Is.Not.Null, "需要正式收起入口");
            var args = new object[] { null }; Assert.That(method.Invoke(System, args), Is.EqualTo(true), args[0]?.ToString());
        }
        [Test] public void TakeOutPreservesInventory()
        { Add(ItemIds.ShortSword); Hold(0); Assert.That(Bag.Container.GetTotalAmount(ItemIds.ShortSword), Is.EqualTo(1)); Assert.That(Bag.Container.TryGet(0, out _), Is.True); }
        [Test] public void ReplaceAndRepeatHoldAreIdempotent()
        { Add(ItemIds.ShortSword); Add(ItemIds.Bow); Hold(0); Hold(1); Hold(1); Assert.That(Bag.Container.Slots.Count, Is.EqualTo(2)); var m=Game.Interface.GetModel<WeaponEquipmentModel>(); Assert.That(m.MeleeWeaponItemId, Is.Zero); Assert.That(m.RangedWeaponItemId, Is.EqualTo(ItemIds.Bow)); }
        [Test] public void FullBagHolsterSucceeds()
        { Add(ItemIds.ShortSword); for(int i=1;i<Bag.Container.Capacity;i++) Add(ItemIds.Bow); Hold(0); Holster(); Assert.That(Bag.Container.Slots.Count, Is.EqualTo(Bag.Container.Capacity)); Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().MeleeWeaponItemId, Is.Zero); }
        [Test] public void PocketHoldAndMoveFollowSource()
        { Assert.That(Bag.Pocket.TryAdd(ItemIds.ShortSword,1,1,out _,out _),Is.True); Assert.That(System.TryEquipFromSlot(new InventorySlotRef(InventoryContainerKind.Pocket,0),WeaponSlot.Melee,out var e),Is.True,e); Assert.That(Bag.Pocket.GetTotalAmount(ItemIds.ShortSword),Is.EqualTo(1)); Assert.That(Game.Interface.GetSystem<InventorySystem>().TryTransfer(new InventorySlotRef(InventoryContainerKind.Pocket,0),new InventorySlotRef(InventoryContainerKind.PlayerBag,3),out e),Is.True,e); Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().MeleeWeaponItemId,Is.EqualTo(ItemIds.ShortSword)); }
        [Test] public void TransferOutClearsHeld()
        { Add(ItemIds.ShortSword); Hold(0); Assert.That(Game.Interface.GetSystem<InventorySystem>().TryTransfer(new InventorySlotRef(InventoryContainerKind.PlayerBag,0),new InventorySlotRef(InventoryContainerKind.Warehouse,0),out var e),Is.True,e); Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().MeleeWeaponItemId,Is.Zero); }
        [Test] public void FailedTransferRetainsHeldAndInventory()
        { Add(ItemIds.ShortSword); Hold(0); Assert.That(Game.Interface.GetSystem<InventorySystem>().TryTransfer(new InventorySlotRef(InventoryContainerKind.PlayerBag,0),new InventorySlotRef(InventoryContainerKind.Warehouse,-1),out _),Is.False); Assert.That(Bag.Container.GetTotalAmount(ItemIds.ShortSword),Is.EqualTo(1)); Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().MeleeWeaponItemId,Is.EqualTo(ItemIds.ShortSword)); }
        [Test] public void WarehouseCannotBeTakenOutDirectly()
        { var w=Game.Interface.GetModel<WarehouseModel>().Container; w.TryAdd(ItemIds.ShortSword,1,1,out _,out _); Assert.That(System.TryEquipFromSlot(new InventorySlotRef(InventoryContainerKind.Warehouse,0),WeaponSlot.Melee,out _),Is.False); }
        [Test] public void UnavailablePlayerRejectsHold()
        { Add(ItemIds.ShortSword); Game.Interface.GetModel<CombatModel>().SetPlayerHP(0); Assert.That(System.TryEquipFromPlayerBag(0,out _),Is.False); Assert.That(Bag.Container.GetTotalAmount(ItemIds.ShortSword),Is.EqualTo(1)); }
        [Test] public void LoadedAmmoSurvivesHolsterAndPocketMove()
        {
            Add(ItemIds.Bow);Hold(0);var model=Game.Interface.GetModel<WeaponEquipmentModel>();Assert.That(model.TrySetRangedLoadedAmmo(1,out _),Is.True);
            Holster();Hold(0);Assert.That(model.RangedLoadedAmmo,Is.EqualTo(1));
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryMoveCarriedStack(new InventorySlotRef(InventoryContainerKind.PlayerBag,0),InventoryContainerKind.Pocket,1,out var error),Is.True,error);
            Assert.That(model.HeldSource.Value.ContainerKind,Is.EqualTo(InventoryContainerKind.Pocket));Assert.That(model.RangedLoadedAmmo,Is.EqualTo(1));
        }
        [Test] public void SameTypeReplacementCannotInheritHeldReference()
        {
            Add(ItemIds.ShortSword);Hold(0);Bag.Container.TryRemove(0,1,out _,out _);Add(ItemIds.ShortSword);
            System.ValidateSourceAfterInventoryMutation();Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().HeldItemId,Is.Zero);
        }
        [Test] public void TargetSideSwapFollowsHeldInstance()
        {
            Add(ItemIds.ShortSword);Add(ItemIds.Bow);Hold(0);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryTransfer(new InventorySlotRef(InventoryContainerKind.PlayerBag,1),new InventorySlotRef(InventoryContainerKind.PlayerBag,0),out var error),Is.True,error);
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().HeldSource.Value.SlotIndex,Is.EqualTo(1));Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().HeldItemId,Is.EqualTo(ItemIds.ShortSword));
        }
        [Test] public void FailedInventoryTransactionRebindsLoadedInstance()
        {
            Add(ItemIds.Bow);Hold(0);var model=Game.Interface.GetModel<WeaponEquipmentModel>();model.TrySetRangedLoadedAmmo(1,out _);
            Bag.Container.RestoreSnapshot(Bag.Container.CaptureSnapshot());System.ValidateSourceAfterInventoryMutation();model.TrySetRangedLoadedAmmo(0,out _);
            Assert.That(Bag.Container.Slots[0].LoadedAmmo,Is.Zero);
        }
    }
}
