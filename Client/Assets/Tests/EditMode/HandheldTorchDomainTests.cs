using System.IO;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>使用正式配置验证火把手持、燃烧与暂停，复用隔离的架构布置。</summary>
    public sealed class HandheldTorchDomainTests
    {
        private WeaponEquipmentTests environment;
        private WeaponEquipmentSystem System => Game.Interface.GetSystem<WeaponEquipmentSystem>();
        private WeaponEquipmentModel Model => Game.Interface.GetModel<WeaponEquipmentModel>();
        private ItemContainer Bag => Game.Interface.GetModel<PlayerBagModel>().Container;
        [SetUp] public void SetUp()
        {
            environment=new WeaponEquipmentTests(); environment.SetUp();
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() =>
                new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", name+".bytes"))))));
            Bag.TryAdd(48,1,1,out _,out _);
            Assert.That(System.TryHold(new InventorySlotRef(InventoryContainerKind.PlayerBag,0),48,out var error),Is.True,error);
        }
        [TearDown] public void TearDown() => environment.TearDown();
        [Test] public void TorchHasNoWeaponProjectionAndBurnsOnlyWhileLit()
        {
            Assert.That(Model.ActiveSlot,Is.EqualTo(WeaponSlot.None));
            Assert.That(System.TryToggleTorch(out var error),Is.True,error);
            System.TickTorch(100f);
            Assert.That(Bag.Slots[0].TorchRemainingSeconds,Is.EqualTo(500f));
            System.TryToggleTorch(out _); System.TickTorch(100f);
            Assert.That(Bag.Slots[0].TorchRemainingSeconds,Is.EqualTo(500f));
        }
        [Test] public void PauseFreezesFuelAndExhaustionCannotRefill()
        {
            System.TryToggleTorch(out _);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
            System.TickTorch(200f); Assert.That(Bag.Slots[0].TorchRemainingSeconds,Is.EqualTo(600f));
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            System.TickTorch(1000f); Assert.That(Model.TorchLit,Is.False);
            Assert.That(Bag.Slots[0].TorchRemainingSeconds,Is.Zero);
            Assert.That(System.TryToggleTorch(out _),Is.False);
        }
        [Test] public void HolsterAndWeaponSwitchExtinguishWithoutResettingFuel()
        {
            System.TryToggleTorch(out _); System.TickTorch(25f);
            System.TryHolster(out _); Assert.That(Model.TorchLit,Is.False);
            System.TryHold(new InventorySlotRef(InventoryContainerKind.PlayerBag,0),48,out _);
            Assert.That(Bag.Slots[0].TorchRemainingSeconds,Is.EqualTo(575f));
            System.TryToggleTorch(out _);
            Bag.TryAdd(ItemIds.ShortSword,1,1,out _,out _);
            System.TryHold(new InventorySlotRef(InventoryContainerKind.PlayerBag,1),ItemIds.ShortSword,out _);
            Assert.That(Model.TorchLit,Is.False); Assert.That(Model.HeldItemId,Is.EqualTo(ItemIds.ShortSword));
        }
        [Test] public void TransactionSnapshotRetainsTorchIdentity()
        {
            System.TryToggleTorch(out _);
            var snapshot=System.CaptureSnapshot();
            System.TryHolster(out _); System.RestoreSnapshot(snapshot);
            Assert.That(Model.HeldItemId,Is.EqualTo(48));
            Assert.That(Model.TorchLit,Is.True);
        }
        [Test] public void CarriedTransferKeepsInstanceFuelAndLitState()
        {
            System.TryToggleTorch(out _); System.TickTorch(13f);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryTransfer(
                new InventorySlotRef(InventoryContainerKind.PlayerBag,0),new InventorySlotRef(InventoryContainerKind.Pocket,1),out var error),Is.True,error);
            Assert.That(Model.TorchLit,Is.True);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Pocket.Slots[1].TorchRemainingSeconds,Is.EqualTo(587f));
        }
        [Test] public void InventoryAndHeldSaveRoundTripRetainsBurningTorch()
        {
            System.TryToggleTorch(out _); System.TickTorch(39f);
            var bag=Game.Interface.GetModel<PlayerBagModel>();var inventory=bag.Capture();var held=Model.Capture();
            Assert.That(bag.Validate(inventory,out var error),Is.True,error);
            Assert.That(Model.Validate(held,out error),Is.True,error);
            bag.Restore(inventory);Model.Restore(held);System.TickTorch(1f);
            Assert.That(Model.TorchLit,Is.True); Assert.That(bag.Container.Slots[0].TorchRemainingSeconds,Is.EqualTo(560f));
        }
        [Test] public void InvalidFuelIsRejectedInsteadOfRefilled()
        {
            var bag=Game.Interface.GetModel<PlayerBagModel>();var saved=bag.Capture();
            saved["slots"][0]["torchRemainingSeconds"]=-1f;
            Assert.That(bag.Validate(saved,out _),Is.False);
            saved["slots"][0]["torchRemainingSeconds"]=601f;
            Assert.That(bag.Validate(saved,out _),Is.False);
        }
        [Test] public void CraftingSpendsOneWoodAndOneClothWithoutResettingExistingTorch()
        {
            System.TryToggleTorch(out _);System.TickTorch(100f);System.TryHolster(out _);
            Bag.TryAdd(ItemIds.Wood,1,20,out _,out _);Bag.TryAdd(ItemIds.Cloth,1,20,out _,out _);
            var provider=Game.Interface.GetUtility<IRuntimeConfigProvider>();
            Assert.That(provider.TryGetCraftRecipe("craft.torch.basic",out var recipe,out var error),Is.True,error);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryCommitCraftInventory(recipe,1,null,out _,out _,out error),Is.True,error);
            Assert.That(Bag.GetTotalAmount(ItemIds.Wood),Is.Zero);Assert.That(Bag.GetTotalAmount(ItemIds.Cloth),Is.Zero);
            Assert.That(Bag.GetTotalAmount(48),Is.EqualTo(2));Assert.That(Bag.Slots[0].TorchRemainingSeconds,Is.EqualTo(500f));
        }
        [TestCase(0f)]
        [TestCase(37.5f)]
        public void WorldPickupPreservesFuelIncludingExhaustedTorch(float remaining)
        {
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(),true);
            var units=Game.Interface.GetSystem<UnitSystem>();var loot=Game.Interface.GetSystem<LootSystem>();
            Assert.That(units.TryRegister(new UnitSnapshot(PlayerModel.LocalPlayerUnitId,7,UnitType.Player,UnitSource.Player,"player.graybox.default",Vector3.zero,Quaternion.identity,UnitAuthorityMode.Local,0,true),out var error),Is.True,error);
            Assert.That(units.TryRegister(new UnitSnapshot(2000,7,UnitType.Loot,UnitSource.RuntimeSpawned,"loot.torch",Vector3.zero,Quaternion.identity,UnitAuthorityMode.Local,0,true),out error),Is.True,error);
            Assert.That(Game.Interface.GetUtility<IRuntimeConfigProvider>().TryGetLoot("loot.torch",out var config,out error),Is.True,error);
            Assert.That(loot.TryRegisterLoot(2000,config,out error),Is.True,error);
            Assert.That(loot.TrySetTorchRemaining(2000,remaining),Is.True);
            Assert.That(loot.TryCollect(2000,PlayerModel.LocalPlayerUnitId),Is.True);
            Assert.That(Bag.Slots[1].TorchRemainingSeconds,Is.EqualTo(remaining));
            Assert.That(Bag.Slots[0].TorchRemainingSeconds,Is.Null,"原火把不能继承掉落火把余量");
        }
    }
}
