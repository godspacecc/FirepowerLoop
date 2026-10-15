using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;

namespace Game.Tests
{
    public sealed class ItemInventoryFoundationTests
    {
        private string tempRoot;

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(CreateRealConfigProvider());
            };

            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayItemInventoryFoundation", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot);
            _ = Game.Interface.GetSystem<InventorySystem>();
        }

        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = _ => { };
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void FormalItemConfigLoadsAndValidates()
        {
            var provider = CreateRealConfigProvider();

            Assert.That(provider.TryGetItem(ItemIds.MetalParts, out var metal, out var error), Is.True, error);
            Assert.That(metal.Id, Is.EqualTo(1));
            Assert.That(metal.ItemType, Is.EqualTo(ItemType.Material));
            Assert.That(metal.MaxStackAmount, Is.EqualTo(20));
            Assert.That(metal.UseEffect, Is.EqualTo(ItemUseEffect.None));
            Assert.That(metal.UseValue, Is.EqualTo(0));

            Assert.That(provider.TryGetItem(ItemIds.Cloth, out var cloth, out error), Is.True, error);
            Assert.That(cloth.Id, Is.EqualTo(2));
            Assert.That(cloth.ItemType, Is.EqualTo(ItemType.Material));
            Assert.That(cloth.MaxStackAmount, Is.EqualTo(20));

            Assert.That(provider.TryGetItem(ItemIds.Medkit, out var medkit, out error), Is.True, error);
            Assert.That(medkit.Id, Is.EqualTo(3));
            Assert.That(medkit.ItemType, Is.EqualTo(ItemType.Consumable));
            Assert.That(medkit.MaxStackAmount, Is.EqualTo(5));
            Assert.That(medkit.UseEffect, Is.EqualTo(ItemUseEffect.RestoreHealth));
            Assert.That(medkit.UseValue, Is.EqualTo(30));
        }

        [Test]
        public void FixedItemIdsMatchGeneratedItemTable()
        {
            Assert.That(ItemIds.MetalParts, Is.EqualTo(1));
            Assert.That(ItemIds.Cloth, Is.EqualTo(2));
            Assert.That(ItemIds.Medkit, Is.EqualTo(3));
            Assert.That(ItemIds.Ration, Is.EqualTo(4));
            Assert.That(ItemIds.ShortSword, Is.EqualTo(5));
            Assert.That(ItemIds.Bow, Is.EqualTo(6));
            Assert.That(ItemIds.Arrow, Is.EqualTo(7));

            var provider = CreateRealConfigProvider();
            var ids = new[]
            {
                ItemIds.MetalParts,
                ItemIds.Cloth,
                ItemIds.Medkit,
                ItemIds.Ration,
                ItemIds.ShortSword,
                ItemIds.Bow,
                ItemIds.Arrow
            };

            foreach (var id in ids)
            {
                Assert.That(provider.TryGetItem(id, out var item, out var error), Is.True, error);
                Assert.That(item.Id, Is.EqualTo(id));
            }
        }

        [Test]
        public void PlayerBagIgnoresRetiredQuickSlotsAndStopsWritingThem()
        {
            RestoreBag(PlayerBagRiskState.BaseSafe, Slot(0, ItemIds.Medkit, 2));
            var bag = Game.Interface.GetModel<PlayerBagModel>();
            var legacy = (JObject)bag.Capture();
            legacy["quickSlots"] = new JArray(ItemIds.Medkit, 0, 0, 0, 0, 0);
            Assert.That(bag.Validate(legacy, out var error), Is.True, error);
            bag.Restore(legacy);
            Assert.That(bag.Container.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(2));
            Assert.That(bag.Capture()["quickSlots"], Is.Null, "新保存不再写入废弃快捷槽字段");
            legacy["quickSlots"] = "retired field is ignored";
            Assert.That(bag.Validate(legacy, out error), Is.True, error);
            bag.Restore(legacy);
            Assert.That(bag.Container.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(2));
        }

        [Test]
        public void ContainerRulesAreDeterministicAndAtomic()
        {
            var container = new ItemContainer(3);
            Assert.That(container.TryAdd(ItemIds.MetalParts, 25, 20, out var accepted, out var error), Is.True, error);
            Assert.That(accepted, Is.EqualTo(25));
            Assert.That(container.Slots[0].Amount, Is.EqualTo(20));
            Assert.That(container.Slots[1].Amount, Is.EqualTo(5));
            Assert.That(container.TryAdd(ItemIds.MetalParts, 50, 20, out accepted, out error), Is.True, error);
            Assert.That(accepted, Is.EqualTo(35));
            Assert.That(container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(60));

            var move = new ItemContainer(3);
            Assert.That(move.TryMoveWithin(0, 0, 20, out error), Is.False);
            Assert.That(error, Is.EqualTo("源槽位为空"));
            Assert.That(move.TryAdd(ItemIds.MetalParts, 15, 20, out _, out error), Is.True, error);
            Assert.That(move.TryMoveWithin(0, 2, 20, out error), Is.True, error);
            Assert.That(move.Slots[2].Amount, Is.EqualTo(15));
            Assert.That(move.TryAdd(ItemIds.MetalParts, 8, 20, out _, out error), Is.True, error);
            Assert.That(move.TryMoveWithin(2, 0, 20, out error), Is.True, error);
            Assert.That(move.Slots[0].Amount, Is.EqualTo(20));
            Assert.That(move.Slots[2].Amount, Is.EqualTo(3));
            var selfSnapshot = SnapshotText(move);
            Assert.That(move.TryMoveWithin(0, 0, 20, out error), Is.True, error);
            Assert.That(SnapshotText(move), Is.EqualTo(selfSnapshot));

            var swap = new ItemContainer(3);
            Assert.That(swap.TryAdd(ItemIds.MetalParts, 5, 20, out _, out error), Is.True, error);
            Assert.That(swap.TryAdd(ItemIds.Cloth, 5, 20, out _, out error), Is.True, error);
            Assert.That(swap.TryMoveWithin(0, 1, 20, out error), Is.True, error);
            Assert.That(swap.Slots[0].ItemId, Is.EqualTo(ItemIds.Cloth));
            Assert.That(swap.Slots[1].ItemId, Is.EqualTo(ItemIds.MetalParts));
            Assert.That(swap.TrySplitHalf(1, 2, out error), Is.True, error);
            Assert.That(swap.Slots[1].Amount, Is.EqualTo(3));
            Assert.That(swap.Slots[2].Amount, Is.EqualTo(2));
            Assert.That(swap.TryRemove(2, 1, out _, out error), Is.True, error);
            Assert.That(swap.TrySplitHalf(2, 0, out error), Is.False);
            Assert.That(swap.TryExpandCapacity(5, out error), Is.True, error);
            Assert.That(swap.Slots[1].Amount, Is.EqualTo(3));
        }

        [Test]
        public void CrossContainerTransferRollsBackBothSides()
        {
            var source = new ItemContainer(2);
            var target = new ItemContainer(2);
            Assert.That(source.TryAdd(ItemIds.MetalParts, 10, 20, out _, out var error), Is.True, error);
            Assert.That(target.TryAdd(ItemIds.MetalParts, 15, 20, out _, out error), Is.True, error);
            Assert.That(ItemContainer.TryTransfer(source, 0, target, 0, ResolveTestMaxStack, out error), Is.True, error);
            Assert.That(source.Slots[0].Amount, Is.EqualTo(5));
            Assert.That(target.Slots[0].Amount, Is.EqualTo(20));

            Assert.That(source.TryAdd(ItemIds.Cloth, 4, 20, out _, out error), Is.True, error);
            Assert.That(ItemContainer.TryTransfer(source, 1, target, 1, ResolveTestMaxStack, out error), Is.True, error);
            Assert.That(target.Slots[1].ItemId, Is.EqualTo(ItemIds.Cloth));

            var exchangeSource = new ItemContainer(2);
            var exchangeTarget = new ItemContainer(2);
            Assert.That(exchangeSource.TryAdd(ItemIds.MetalParts, 7, 20, out _, out error), Is.True, error);
            Assert.That(exchangeTarget.TryAdd(ItemIds.Cloth, 9, 20, out _, out error), Is.True, error);
            Assert.That(ItemContainer.TryTransfer(exchangeSource, 0, exchangeTarget, 0, ResolveTestMaxStack, out error), Is.True, error);
            Assert.That(exchangeSource.Slots[0].ItemId, Is.EqualTo(ItemIds.Cloth));
            Assert.That(exchangeSource.Slots[0].Amount, Is.EqualTo(9));
            Assert.That(exchangeTarget.Slots[0].ItemId, Is.EqualTo(ItemIds.MetalParts));
            Assert.That(exchangeTarget.Slots[0].Amount, Is.EqualTo(7));

            var beforeSource = SnapshotText(source);
            var beforeTarget = SnapshotText(target);
            Assert.That(ItemContainer.TryTransfer(source, 9, target, 0, ResolveTestMaxStack, out error), Is.False);
            Assert.That(SnapshotText(source), Is.EqualTo(beforeSource));
            Assert.That(SnapshotText(target), Is.EqualTo(beforeTarget));
            Assert.That(ItemContainer.TryTransfer(source, 0, target, 1, _ => 0, out error), Is.False);
            Assert.That(SnapshotText(source), Is.EqualTo(beforeSource));
            Assert.That(SnapshotText(target), Is.EqualTo(beforeTarget));
            Assert.That(ItemContainer.TryTransfer(source, 0, target, 1, _ => throw new InvalidOperationException("resolver exploded"), out error), Is.False);
            Assert.That(SnapshotText(source), Is.EqualTo(beforeSource));
            Assert.That(SnapshotText(target), Is.EqualTo(beforeTarget));
        }

        [Test]
        public void BagAndWarehouseUseSeparateValidatedSections()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>();
            var warehouse = Game.Interface.GetModel<WarehouseModel>();
            Assert.That(bag.SectionId, Is.EqualTo("another-day.player-bag"));
            Assert.That(warehouse.SectionId, Is.EqualTo("another-day.warehouse"));
            Assert.That(bag.Container.Capacity, Is.EqualTo(12));
            Assert.That(warehouse.Container.Capacity, Is.EqualTo(40));

            var bagData = new JObject
            {
                ["capacity"] = 12,
                ["riskState"] = "BaseSafe",
                ["slots"] = new JArray(new JObject { ["index"] = 0, ["itemId"] = ItemIds.Medkit, ["amount"] = 2 })
            };
            Assert.That(bag.Validate(bagData, out var error), Is.True, error);
            bag.Restore(bagData);
            Assert.That(bag.Container.Slots[0].Amount, Is.EqualTo(2));
            var capturedBagSlot = ((JArray)bag.Capture()["slots"])[0];
            Assert.That(capturedBagSlot["itemId"].Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(capturedBagSlot["itemId"].Value<int>(), Is.EqualTo(ItemIds.Medkit));

            Assert.That(bag.Validate(MutateSlot(bagData, "index", 12), out _), Is.False);
            Assert.That(bag.Validate(new JObject
            {
                ["capacity"] = 12,
                ["riskState"] = "BaseSafe",
                ["slots"] = new JArray(
                    new JObject { ["index"] = 0, ["itemId"] = ItemIds.Medkit, ["amount"] = 1 },
                    new JObject { ["index"] = 0, ["itemId"] = ItemIds.Medkit, ["amount"] = 1 })
            }, out _), Is.False);
            Assert.That(bag.Validate(MutateSlot(bagData, "amount", 6), out _), Is.False);
            Assert.That(bag.Validate(MutateSlot(bagData, "itemId", "missing.item"), out _), Is.False);
            Assert.That(bag.Validate(MutateSlot(bagData, "itemId", 0), out _), Is.False);
            Assert.That(bag.Validate(MutateSlot(bagData, "itemId", -1), out _), Is.False);
            var badRisk = (JObject)bagData.DeepClone();
            badRisk["riskState"] = "Nope";
            Assert.That(bag.Validate(badRisk, out _), Is.False);
            badRisk["riskState"] = "4";
            Assert.That(bag.Validate(badRisk, out _), Is.False);
            badRisk["riskState"] = "-1";
            Assert.That(bag.Validate(badRisk, out _), Is.False);
            badRisk["riskState"] = 1;
            Assert.That(bag.Validate(badRisk, out _), Is.False);

            var warehouseData = new JObject
            {
                ["capacity"] = 40,
                ["slots"] = new JArray(new JObject { ["index"] = 0, ["itemId"] = ItemIds.MetalParts, ["amount"] = 20 })
            };
            Assert.That(warehouse.Validate(warehouseData, out error), Is.True, error);
            warehouse.Restore(warehouseData);
            Assert.That(warehouse.Container.Slots[0].Amount, Is.EqualTo(20));
            var capturedWarehouseSlot = ((JArray)warehouse.Capture()["slots"])[0];
            Assert.That(capturedWarehouseSlot["itemId"].Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(capturedWarehouseSlot["itemId"].Value<int>(), Is.EqualTo(ItemIds.MetalParts));
        }

        [Test]
        public void ItemAndLootConfigRejectNonPositiveIds()
        {
            var tables = CreateTables();
            var item = tables.TbItem.Get(ItemIds.MetalParts);
            typeof(global::Item).GetField("Id")?.SetValue(item, 0);
            var error = InvokeProviderValidate(tables);
            Assert.That(error, Does.Contain("TbItem"));
            Assert.That(error, Does.Contain("Id"));

            tables = CreateTables();
            var loot = tables.TbLoot.Get("loot.metal-parts");
            typeof(global::LootConfig).GetField("ItemId")?.SetValue(loot, 0);
            error = InvokeProviderValidate(tables);
            Assert.That(error, Does.Contain("TbLoot"));
            Assert.That(error, Does.Contain("ItemId"));
        }

        [Test]
        public void WarehouseSpendingRemainsAtomic()
        {
            var changed = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => changed++);
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            Assert.That(inventory.TryAddWarehouseItem(ItemIds.MetalParts, 3, out var accepted), Is.True);
            Assert.That(accepted, Is.EqualTo(3));
            Assert.That(inventory.TrySpendWarehouseItem(ItemIds.MetalParts, 1), Is.True);
            Assert.That(Game.Interface.GetModel<WarehouseModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(2));
            Assert.That(changed, Is.GreaterThanOrEqualTo(2));

            var warehouse = Game.Interface.GetModel<WarehouseModel>();
            warehouse.Reset();
            Assert.That(warehouse.Container.TryAdd(ItemIds.Cloth, 800, 20, out _, out var error), Is.True, error);
            var failedSnapshot = SnapshotText(warehouse.Container);
            var beforeFailedChanged = changed;
            Assert.That(inventory.TrySpendWarehouseItem(ItemIds.MetalParts, 1), Is.False);
            Assert.That(SnapshotText(warehouse.Container), Is.EqualTo(failedSnapshot));
            Assert.That(changed, Is.EqualTo(beforeFailedChanged));
            Assert.That(warehouse.Container.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(800));
        }

        [Test]
        public void WarehouseExpansionQuoteCombinesContainersAndReportsLimits()
        {
            EnterBaseSafeState();
            var changed = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => changed++);
            var warehouse = Game.Interface.GetModel<WarehouseModel>();
            var bag = Game.Interface.GetModel<PlayerBagModel>();
            RestoreWarehouse(InventoryDefaults.WarehouseCapacity, Slot(0, ItemIds.MetalParts, 2), Slot(3, ItemIds.Cloth, 1));
            RestoreBag(PlayerBagRiskState.BaseSafe, Slot(1, ItemIds.MetalParts, 3), Slot(2, ItemIds.Cloth, 2));
            var warehouseBefore = SnapshotText(warehouse.Container);
            var bagBefore = SnapshotText(bag.Container);

            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryGetWarehouseExpansionQuote(out var quote, out var error), Is.True, error);

            Assert.That(quote.CurrentCapacity, Is.EqualTo(InventoryDefaults.WarehouseCapacity));
            Assert.That(quote.MaximumCapacity, Is.EqualTo(InventoryDefaults.WarehouseMaximumCapacity));
            Assert.That(quote.MetalPartsOwned, Is.EqualTo(5));
            Assert.That(quote.MetalPartsRequired, Is.EqualTo(InventoryDefaults.WarehouseExpansionMetalPartsCost));
            Assert.That(quote.ClothOwned, Is.EqualTo(3));
            Assert.That(quote.ClothRequired, Is.EqualTo(InventoryDefaults.WarehouseExpansionClothCost));
            Assert.That(quote.StateAllowed, Is.True);
            Assert.That(quote.IsAtMaximum, Is.False);
            Assert.That(quote.HasRequiredMaterials, Is.True);
            Assert.That(quote.CanExpand, Is.True);
            Assert.That(warehouse.Container.Capacity, Is.EqualTo(InventoryDefaults.WarehouseCapacity));
            Assert.That(SnapshotText(warehouse.Container), Is.EqualTo(warehouseBefore));
            Assert.That(SnapshotText(bag.Container), Is.EqualTo(bagBefore));
            Assert.That(changed, Is.EqualTo(0));
        }

        [Test]
        public void WarehouseExpansionConsumesWarehouseBeforeBagInSlotOrderAndPersistsCapacity()
        {
            EnterBaseSafeState();
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var created, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(created, out error), Is.True, error);

            RestoreWarehouse(
                InventoryDefaults.WarehouseCapacity,
                Slot(0, ItemIds.MetalParts, 2),
                Slot(5, ItemIds.MetalParts, 1),
                Slot(6, ItemIds.Cloth, 1));
            RestoreBag(
                PlayerBagRiskState.BaseSafe,
                Slot(0, ItemIds.MetalParts, 3),
                Slot(2, ItemIds.MetalParts, 5),
                Slot(3, ItemIds.Cloth, 2),
                Slot(4, ItemIds.Cloth, 5));
            var changed = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => changed++);

            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryExpandWarehouse(out error), Is.True, error);

            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(warehouse.Capacity, Is.EqualTo(InventoryDefaults.WarehouseCapacity + 1));
            Assert.That(warehouse.Slots.ContainsKey(0), Is.False);
            Assert.That(warehouse.Slots.ContainsKey(5), Is.False);
            Assert.That(warehouse.Slots.ContainsKey(6), Is.False);
            AssertSlot(bag, 0, ItemIds.MetalParts, 2);
            AssertSlot(bag, 2, ItemIds.MetalParts, 5);
            Assert.That(bag.Slots.ContainsKey(3), Is.False);
            AssertSlot(bag, 4, ItemIds.Cloth, 5);
            Assert.That(changed, Is.EqualTo(1));

            // 此夹具不伪造地图 Handle；直接走基地 DTO 捕获与固定文件写入入口验证持久化。
            Assert.That(save.CaptureBaseSections(created, out error), Is.True, error);
            Assert.That(save.SaveBase(created, out error), Is.True, error);
            var prepared = save.PrepareBase();
            Assert.That(prepared.IsValid, Is.True, prepared.Error);
            var savedWarehouse = (JObject)prepared.Data.Sections["another-day.warehouse"];
            Assert.That(savedWarehouse["capacity"].Value<int>(), Is.EqualTo(InventoryDefaults.WarehouseCapacity + 1));
            Assert.That(SnapshotText(warehouse), Is.EqualTo(SnapshotTextFromSlots((JArray)savedWarehouse["slots"])));
        }

        [Test]
        public void WarehouseExpansionRejectsMissingMaterialsAndInvalidStatesWithoutMutation()
        {
            EnterBaseSafeState();
            RestoreWarehouse(InventoryDefaults.WarehouseCapacity, Slot(0, ItemIds.MetalParts, InventoryDefaults.WarehouseExpansionMetalPartsCost - 1));
            RestoreBag(PlayerBagRiskState.BaseSafe, Slot(0, ItemIds.Cloth, InventoryDefaults.WarehouseExpansionClothCost));
            AssertExpansionRejectedWithoutMutation("仓库扩容材料不足", quote =>
            {
                Assert.That(quote.StateAllowed, Is.True);
                Assert.That(quote.HasRequiredMaterials, Is.False);
                Assert.That(quote.CanExpand, Is.False);
            });

            EnterBaseSafeState();
            RestoreWarehouse(InventoryDefaults.WarehouseCapacity, Slot(0, ItemIds.MetalParts, InventoryDefaults.WarehouseExpansionMetalPartsCost));
            RestoreBag(PlayerBagRiskState.BaseSafe, Slot(0, ItemIds.Cloth, InventoryDefaults.WarehouseExpansionClothCost - 1));
            AssertExpansionRejectedWithoutMutation("仓库扩容材料不足", quote =>
            {
                Assert.That(quote.StateAllowed, Is.True);
                Assert.That(quote.HasRequiredMaterials, Is.False);
                Assert.That(quote.CanExpand, Is.False);
            });

            EnterBaseSafeState();
            RestoreWarehouse(InventoryDefaults.WarehouseCapacity, Slot(0, ItemIds.MetalParts, InventoryDefaults.WarehouseExpansionMetalPartsCost));
            RestoreBag(PlayerBagRiskState.BaseSafe, Slot(0, ItemIds.Cloth, InventoryDefaults.WarehouseExpansionClothCost));
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
            AssertExpansionRejectedWithoutMutation("仓库操作需要处于游戏中状态", quote =>
            {
                Assert.That(quote.StateAllowed, Is.False);
                Assert.That(quote.CanExpand, Is.False);
            });

            EnterBaseSafeState();
            RestoreWarehouse(InventoryDefaults.WarehouseCapacity, Slot(0, ItemIds.MetalParts, InventoryDefaults.WarehouseExpansionMetalPartsCost));
            RestoreBag(PlayerBagRiskState.BaseSafe, Slot(0, ItemIds.Cloth, InventoryDefaults.WarehouseExpansionClothCost));
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            AssertExpansionRejectedWithoutMutation("仓库操作需要位于基地地图", quote =>
            {
                Assert.That(quote.StateAllowed, Is.False);
                Assert.That(quote.CanExpand, Is.False);
            });

            EnterBaseSafeState();
            RestoreWarehouse(InventoryDefaults.WarehouseCapacity, Slot(0, ItemIds.MetalParts, InventoryDefaults.WarehouseExpansionMetalPartsCost));
            RestoreBag(PlayerBagRiskState.EnteringExpedition, Slot(0, ItemIds.Cloth, InventoryDefaults.WarehouseExpansionClothCost));
            AssertExpansionRejectedWithoutMutation("仓库操作需要玩家背包处于安全状态", quote =>
            {
                Assert.That(quote.StateAllowed, Is.False);
                Assert.That(quote.CanExpand, Is.False);
            });
        }

        [Test]
        public void WarehouseExpansionStopsAtMaximumAndPreservesOversizedHistory()
        {
            EnterBaseSafeState();
            RestoreWarehouse(
                InventoryDefaults.WarehouseMaximumCapacity - 1,
                Slot(0, ItemIds.MetalParts, InventoryDefaults.WarehouseExpansionMetalPartsCost),
                Slot(1, ItemIds.Cloth, InventoryDefaults.WarehouseExpansionClothCost));
            RestoreBag(PlayerBagRiskState.BaseSafe);
            var changed = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => changed++);

            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryExpandWarehouse(out var error), Is.True, error);
            Assert.That(Game.Interface.GetModel<WarehouseModel>().Container.Capacity, Is.EqualTo(InventoryDefaults.WarehouseMaximumCapacity));
            Assert.That(changed, Is.EqualTo(1));
            var atMaxSnapshot = SnapshotText(Game.Interface.GetModel<WarehouseModel>().Container);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryExpandWarehouse(out error), Is.False);
            Assert.That(error, Is.EqualTo("仓库已达到最大容量"));
            Assert.That(Game.Interface.GetModel<WarehouseModel>().Container.Capacity, Is.EqualTo(InventoryDefaults.WarehouseMaximumCapacity));
            Assert.That(SnapshotText(Game.Interface.GetModel<WarehouseModel>().Container), Is.EqualTo(atMaxSnapshot));
            Assert.That(changed, Is.EqualTo(1));

            RestoreWarehouse(InventoryDefaults.WarehouseMaximumCapacity + 1);
            RestoreBag(PlayerBagRiskState.BaseSafe);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryGetWarehouseExpansionQuote(out var quote, out error), Is.True, error);
            Assert.That(quote.CurrentCapacity, Is.EqualTo(InventoryDefaults.WarehouseMaximumCapacity + 1));
            Assert.That(quote.IsAtMaximum, Is.True);
            Assert.That(quote.CanExpand, Is.False);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryExpandWarehouse(out error), Is.False);
            Assert.That(error, Is.EqualTo("仓库已达到最大容量"));
            Assert.That(Game.Interface.GetModel<WarehouseModel>().Container.Capacity, Is.EqualTo(InventoryDefaults.WarehouseMaximumCapacity + 1));
        }

        [Test]
        public void MapUnlockConsumesWarehouseMetalOnly()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>();
            Assert.That(bag.Container.TryAdd(ItemIds.MetalParts, 1, 20, out _, out var error), Is.True, error);

            var selection = Game.Interface.GetSystem<BaseMapSelectionSystem>();
            var bagOnly = selection.InteractWithMapAsync(MapIds.MissionSample).GetAwaiter().GetResult();
            Assert.That(bagOnly.Success, Is.False);
            Assert.That(selection.IsMapUnlocked(MapIds.MissionSample), Is.False);

            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryAddWarehouseItem(ItemIds.MetalParts, 1, out _), Is.True);
            var warehouse = Game.Interface.GetModel<WarehouseModel>();
            var unlocked = selection.InteractWithMapAsync(MapIds.MissionSample).GetAwaiter().GetResult();
            Assert.That(unlocked.Success, Is.True, unlocked.Error);
            Assert.That(unlocked.UnlockedNow, Is.True);
            Assert.That(warehouse.Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(0));
            Assert.That(bag.Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(1));
        }

        [Test]
        public void GeneratedItemSchemaUsesIntIdsAndFixedFields()
        {
            Assert.That(typeof(global::Item).GetField("Id")?.FieldType, Is.EqualTo(typeof(int)));
            Assert.That(typeof(global::WeaponConfig).GetField("ItemId")?.FieldType, Is.EqualTo(typeof(int)));
            Assert.That(typeof(global::WeaponConfig).GetField("AmmoItemId")?.FieldType, Is.EqualTo(typeof(int)));
            Assert.That(typeof(global::LootConfig).GetField("ItemId")?.FieldType, Is.EqualTo(typeof(int)));
            Assert.That(typeof(global::TbItem).GetMethod("Get", new[] { typeof(int) }), Is.Not.Null);
            Assert.That(typeof(global::TbWeapon).GetMethod("Get", new[] { typeof(int) }), Is.Not.Null);
            Assert.That(typeof(global::Item).GetField("IconName"), Is.Not.Null);
            Assert.That(typeof(global::Item).GetField("ItemType"), Is.Not.Null);
            Assert.That(typeof(global::Item).GetField("MaxStackAmount"), Is.Not.Null);
            Assert.That(typeof(global::Item).GetField("UseEffect"), Is.Not.Null);
            Assert.That(typeof(global::Item).GetField("UseValue"), Is.Not.Null);
        }

        private static LubanRuntimeConfigProvider CreateRealConfigProvider()
        {
            return new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", $"{name}.bytes")))));
        }

        private static Tables CreateTables()
        {
            return new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", $"{name}.bytes"))));
        }

        private static string InvokeProviderValidate(Tables tables)
        {
            return (string)typeof(LubanRuntimeConfigProvider)
                .GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, new object[] { tables });
        }

        private static int ResolveTestMaxStack(int itemId)
        {
            return itemId == ItemIds.Medkit ? 5 : 20;
        }

        private static void EnterBaseSafeState()
        {
            var app = Game.Interface.GetSystem<AppStateSystem>();
            if (Game.Interface.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing)
            {
                app.TryTransition(AppState.Loading);
                app.TryTransition(AppState.Playing);
            }

            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Assert.That(
                Game.Interface.GetModel<PlayerBagModel>().RiskState,
                Is.EqualTo(PlayerBagRiskState.BaseSafe));
        }

        private static string SnapshotText(ItemContainer container)
        {
            return JsonConvert.SerializeObject(container.CaptureSnapshot().Slots
                .OrderBy(pair => pair.Key)
                .Select(pair => new { pair.Key, pair.Value.ItemId, pair.Value.Amount }));
        }

        private static string SnapshotTextFromSlots(JArray slots)
        {
            return JsonConvert.SerializeObject(slots
                .OrderBy(item => item["index"].Value<int>())
                .Select(item => new
                {
                    Key = item["index"].Value<int>(),
                    ItemId = item["itemId"].Value<int>(),
                    Amount = item["amount"].Value<int>()
                }));
        }

        private static JObject Slot(int index, int itemId, int amount)
        {
            return new JObject
            {
                ["index"] = index,
                ["itemId"] = itemId,
                ["amount"] = amount
            };
        }

        private static void RestoreWarehouse(int capacity, params JObject[] slots)
        {
            Game.Interface.GetModel<WarehouseModel>().Restore(new JObject
            {
                ["capacity"] = capacity,
                ["slots"] = new JArray(slots)
            });
        }

        private static void RestoreBag(PlayerBagRiskState riskState, params JObject[] slots)
        {
            Game.Interface.GetModel<PlayerBagModel>().Restore(new JObject
            {
                ["capacity"] = InventoryDefaults.PlayerBagCapacity,
                ["riskState"] = riskState.ToString(),
                ["slots"] = new JArray(slots)
            });
        }

        private static void AssertSlot(ItemContainer container, int index, int itemId, int amount)
        {
            Assert.That(container.TryGet(index, out var stack), Is.True);
            Assert.That(stack.ItemId, Is.EqualTo(itemId));
            Assert.That(stack.Amount, Is.EqualTo(amount));
        }

        private static void AssertExpansionRejectedWithoutMutation(
            string expectedError,
            Action<WarehouseExpansionQuote> assertQuote)
        {
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var warehouseCapacity = warehouse.Capacity;
            var warehouseBefore = SnapshotText(warehouse);
            var bagBefore = SnapshotText(bag);
            var changed = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => changed++);

            Assert.That(inventory.TryGetWarehouseExpansionQuote(out var quote, out var error), Is.True, error);
            assertQuote(quote);
            Assert.That(inventory.TryExpandWarehouse(out error), Is.False);
            Assert.That(error, Is.EqualTo(expectedError));
            Assert.That(warehouse.Capacity, Is.EqualTo(warehouseCapacity));
            Assert.That(SnapshotText(warehouse), Is.EqualTo(warehouseBefore));
            Assert.That(SnapshotText(bag), Is.EqualTo(bagBefore));
            Assert.That(changed, Is.EqualTo(0));
        }

        private static JObject MutateSlot(JObject source, string key, object value)
        {
            var clone = (JObject)source.DeepClone();
            clone["slots"][0][key] = JToken.FromObject(value);
            return clone;
        }

        private string WriteSave(int version, JObject sections)
        {
            return WriteRawSave(version, sections);
        }

        private string ReadSaveText(string id)
        {
            return File.ReadAllText(Path.Combine(tempRoot, "Saves", $"{id}.json"));
        }

        private string WriteRawSave(int version, JToken sections)
        {
            var id = Guid.NewGuid().ToString("N");
            var saves = Path.Combine(tempRoot, "Saves");
            Directory.CreateDirectory(saves);
            var raw = new JObject
            {
                ["Version"] = version,
                ["SaveId"] = id,
                ["SaveName"] = "Test",
                ["CreatedAtUtc"] = "2026-07-31T00:00:00.0000000Z",
                ["UpdatedAtUtc"] = "2026-07-31T00:00:00.0000000Z",
                ["MapContext"] = new JObject { ["MapId"] = MapIds.Base },
                ["Sections"] = sections
            };
            File.WriteAllText(Path.Combine(saves, $"{id}.json"), raw.ToString(Formatting.Indented));
            return id;
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }
    }
}
