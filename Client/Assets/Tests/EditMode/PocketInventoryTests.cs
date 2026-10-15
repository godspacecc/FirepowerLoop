using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;

namespace Game.Tests
{
    /// <summary>验证口袋的独立保存与库存事务；不创建或修复正式资产。</summary>
    public sealed class PocketInventoryTests
    {
        private string tempRoot;

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = architecture => architecture.RegisterUtility<IRuntimeConfigProvider>(
                (IRuntimeConfigProvider)typeof(ItemInventoryFoundationTests).GetMethod("CreateRealConfigProvider", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null));
            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayPocket", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot);
            _ = Game.Interface.GetSystem<InventorySystem>();
        }

        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = _ => { };
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }

        [Test]
        public void PocketRoundTripPreservesSeparateSlotIdentity()
        {
            var model = Game.Interface.GetModel<PlayerBagModel>();
            var data = LegacyBag();
            data["pocketSlots"] = new JArray(Slot(0, ItemIds.ShortSword, 1), Slot(3, ItemIds.Ration, 2));
            model.Restore(data);
            var captured = model.Capture();
            // 旧档缺少 loadedAmmo，新格式会补零；验证实际格号与内容而非要求 JSON 字段完全相同。
            model.Restore(captured);
            Assert.That(model.Pocket.Slots.Count, Is.EqualTo(2));
            Assert.That(model.Pocket.TryGet(0, out var sword), Is.True);
            Assert.That((sword.ItemId, sword.Amount, sword.LoadedAmmo), Is.EqualTo((ItemIds.ShortSword, 1, 0)));
            Assert.That(model.Pocket.TryGet(3, out var ration), Is.True);
            Assert.That((ration.ItemId, ration.Amount), Is.EqualTo((ItemIds.Ration, 2)));
            Assert.That(model.Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(3));
            Assert.That(model.Container.GetTotalAmount(ItemIds.ShortSword), Is.Zero);
        }

        [Test]
        public void PocketSurvivesBaseSaveFileAndReload()
        {
            EnterBase();
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var created, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(created, out error), Is.True, error);
            var model = Game.Interface.GetModel<PlayerBagModel>();
            var data = LegacyBag();
            data["pocketSlots"] = new JArray(Slot(3, ItemIds.Ration, 2));
            model.Restore(data);
            Assert.That(save.CaptureBaseSections(created, out error), Is.True, error);
            Assert.That(save.SaveBase(created, out error), Is.True, error);
            model.Reset();
            var prepared = save.PrepareBase();
            Assert.That(prepared.IsValid, Is.True, prepared.Error);
            model.Restore(prepared.Data.Sections[model.SectionId]);
            Assert.That(model.Pocket.TryGet(3, out var stack), Is.True);
            Assert.That(stack.ItemId, Is.EqualTo(ItemIds.Ration));
            Assert.That(stack.Amount, Is.EqualTo(2));
            Assert.That(model.Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(3));
        }

        [Test]
        public void LegacySaveRestoresEmptyPocketWithoutMovingBagItems()
        {
            var model = Game.Interface.GetModel<PlayerBagModel>();
            model.Restore(LegacyBag());
            Assert.That(model.Capture()["pocketSlots"], Is.TypeOf<JArray>());
            Assert.That(((JArray)model.Capture()["pocketSlots"]).Count, Is.Zero);
            Assert.That(model.Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(3));
        }

        [Test]
        public void InvalidPocketIndexRejectsWholeSave()
        {
            var model = Game.Interface.GetModel<PlayerBagModel>();
            var data = LegacyBag();
            data["pocketSlots"] = new JArray(Slot(4, ItemIds.Ration, 1));
            Assert.That(model.Validate(data, out _), Is.False);
        }

        [Test]
        public void InvalidPocketTypeRejectsWholeSave()
        {
            var data = LegacyBag();
            data["pocketSlots"] = "broken";
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Validate(data, out _), Is.False);
        }

        [Test]
        public void TransferPartThenBackKeepsBothContainersIndependent()
        {
            EnterBase();
            var model = Game.Interface.GetModel<PlayerBagModel>();
            model.Restore(LegacyBag());
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            var changed = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => changed++);
            Assert.That(inventory.TryMoveCarriedStack(new InventorySlotRef(InventoryContainerKind.PlayerBag, 0), InventoryContainerKind.Pocket, 2, out var error), Is.True, error);
            Assert.That(model.Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(1));
            Assert.That(model.Pocket.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(2));
            Assert.That(inventory.TryMoveCarriedStack(new InventorySlotRef(InventoryContainerKind.Pocket, 0), InventoryContainerKind.PlayerBag, 1, out error), Is.True, error);
            Assert.That(model.Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(2));
            Assert.That(model.Pocket.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(1));
            Assert.That(changed, Is.EqualTo(2));
        }

        [Test]
        public void FullPocketRejectsTransferWithoutMutationOrEvent()
        {
            EnterBase();
            var model = Game.Interface.GetModel<PlayerBagModel>();
            var data = LegacyBag();
            data["pocketSlots"] = new JArray(Slot(0, ItemIds.ShortSword, 1), Slot(1, ItemIds.ShortSword, 1), Slot(2, ItemIds.ShortSword, 1), Slot(3, ItemIds.ShortSword, 1));
            model.Restore(data);
            var before = model.Capture().ToString();
            var changed = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => changed++);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryMoveCarriedStack(new InventorySlotRef(InventoryContainerKind.PlayerBag, 0), InventoryContainerKind.Pocket, 3, out _), Is.False);
            Assert.That(model.Capture().ToString(), Is.EqualTo(before));
            Assert.That(changed, Is.Zero);
        }

        [Test]
        public void InsufficientMergeSpaceDoesNotPartiallyMove()
        {
            EnterBase();
            var model = Game.Interface.GetModel<PlayerBagModel>();
            var data = LegacyBag();
            data["pocketSlots"] = new JArray(Slot(0, ItemIds.MetalParts, 19), Slot(1, ItemIds.ShortSword, 1), Slot(2, ItemIds.ShortSword, 1), Slot(3, ItemIds.ShortSword, 1));
            model.Restore(data);
            var before = model.Capture().ToString();
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryMoveCarriedStack(new InventorySlotRef(InventoryContainerKind.PlayerBag, 0), InventoryContainerKind.Pocket, 2, out _), Is.False);
            Assert.That(model.Capture().ToString(), Is.EqualTo(before));
        }

        [Test]
        public void PocketSplitUsesPocketEmptySlot()
        {
            EnterBase();
            var model = Game.Interface.GetModel<PlayerBagModel>();
            var data = LegacyBag(); data["pocketSlots"] = new JArray(Slot(0, ItemIds.MetalParts, 6)); model.Restore(data);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TrySplitHalf(InventoryContainerKind.Pocket, 0, 1, out var error), Is.True, error);
            Assert.That(model.Pocket.TryGet(1, out var second), Is.True);
            Assert.That(second.Amount, Is.EqualTo(3));
            Assert.That(model.Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(3));
        }

        [Test]
        public void RiskClearEmptiesPocketAndBag()
        {
            var model = Game.Interface.GetModel<PlayerBagModel>();
            var data = LegacyBag(); data["pocketSlots"] = new JArray(Slot(0, ItemIds.Ration, 2)); model.Restore(data);
            Game.Interface.GetSystem<InventorySystem>().ClearPlayerBag();
            Assert.That(model.Pocket.Slots.Count, Is.Zero);
            Assert.That(model.Container.Slots.Count, Is.Zero);
            Assert.That(model.Pocket.Capacity, Is.EqualTo(4));
        }

        [Test]
        public void PocketDropFailureRestoresSource()
        {
            EnterBase();
            var model = Game.Interface.GetModel<PlayerBagModel>();
            var data = LegacyBag(); data["pocketSlots"] = new JArray(Slot(0, ItemIds.Ration, 2)); model.Restore(data);
            var before = model.Capture().ToString();
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryDropCarriedStack(new InventorySlotRef(InventoryContainerKind.Pocket, 0), new FailedDrop(), out _), Is.False);
            Assert.That(model.Capture().ToString(), Is.EqualTo(before));
        }

        [Test]
        public void CarriedMaterialShortageRejectsAllCostsWithoutMutation()
        {
            EnterBase();
            var model = Game.Interface.GetModel<PlayerBagModel>();
            Assert.That(model.Container.TryAdd(ItemIds.Wood, 1, 20, out _, out var error), Is.True, error);
            Assert.That(model.Pocket.TryAdd(ItemIds.Wood, 2, 20, out _, out error), Is.True, error);
            var before = model.Capture().ToString();
            var events = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => events++);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TrySpendCarriedItems(new[]
                { new ItemCostDefinition(ItemIds.Wood, 3), new ItemCostDefinition(ItemIds.Cloth, 1) }, out _), Is.False);
            Assert.That(model.Capture().ToString(), Is.EqualTo(before));
            Assert.That(events, Is.Zero);
        }

        [Test]
        public void WarehouseExpansionUsesWarehouseThenBagThenPocket()
        {
            EnterBase();
            var model = Game.Interface.GetModel<PlayerBagModel>();
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            Assert.That(warehouse.TryAdd(ItemIds.MetalParts, 1, 20, out _, out var error), Is.True, error);
            Assert.That(model.Container.TryAdd(ItemIds.MetalParts, 1, 20, out _, out error), Is.True, error);
            Assert.That(model.Pocket.TryAdd(ItemIds.MetalParts, 4, 20, out _, out error), Is.True, error);
            Assert.That(model.Pocket.TryAdd(ItemIds.Cloth, 3, 20, out _, out error), Is.True, error);
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            Assert.That(inventory.TryGetWarehouseExpansionQuote(out var quote, out error), Is.True, error);
            Assert.That(quote.CanExpand, Is.True);
            Assert.That(inventory.TryExpandWarehouse(out error), Is.True, error);
            Assert.That(warehouse.Capacity, Is.EqualTo(41));
            Assert.That(warehouse.GetTotalAmount(ItemIds.MetalParts), Is.Zero);
            Assert.That(model.Container.GetTotalAmount(ItemIds.MetalParts), Is.Zero);
            Assert.That(model.Pocket.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(2));
            Assert.That(model.Pocket.GetTotalAmount(ItemIds.Cloth), Is.Zero);
        }

        [Test]
        public void CrossContainerConsumptionSurvivesSaveAndReload()
        {
            EnterBase();
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var created, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(created, out error), Is.True, error);
            var model = Game.Interface.GetModel<PlayerBagModel>();
            Assert.That(model.Container.TryAdd(ItemIds.Cloth, 1, 20, out _, out error), Is.True, error);
            Assert.That(model.Pocket.TryAdd(ItemIds.Cloth, 3, 20, out _, out error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TrySpendCarriedItems(
                new[] { new ItemCostDefinition(ItemIds.Cloth, 2) }, out error), Is.True, error);
            Assert.That(save.CaptureBaseSections(created, out error), Is.True, error);
            Assert.That(save.SaveBase(created, out error), Is.True, error);
            model.Reset();
            var prepared = save.PrepareBase();
            Assert.That(prepared.IsValid, Is.True, prepared.Error);
            model.Restore(prepared.Data.Sections[model.SectionId]);
            Assert.That(model.Container.GetTotalAmount(ItemIds.Cloth), Is.Zero);
            Assert.That(model.Pocket.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(2));
        }

        private sealed class FailedDrop : IPlayerInventoryDropPort
        {
            public bool TryCreatePlayerInventoryDrop(int id, int amount, out PlayerInventoryDropReceipt receipt, out string error)
            { receipt = PlayerInventoryDropReceipt.Empty; error = "test rejection"; return false; }
            public void RollbackPlayerInventoryDrop(PlayerInventoryDropReceipt receipt) { }
        }

        private static void EnterBase()
        {
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
        }

        internal static JObject LegacyBag() => new JObject
        {
            ["capacity"] = InventoryDefaults.PlayerBagCapacity,
            ["riskState"] = "BaseSafe",
            ["slots"] = new JArray(Slot(0, ItemIds.MetalParts, 3))
        };

        internal static JObject Slot(int index, int itemId, int amount) => new JObject { ["index"] = index, ["itemId"] = itemId, ["amount"] = amount };

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }
    }
}
