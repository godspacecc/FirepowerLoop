using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class CraftSystemTests
    {
        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            InstallArchitecture();
        }

        [TearDown]
        public void TearDown()
        {
            TestReset.ResetArchitecture();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CraftIncludesPocketMaterialsAndRestoresThemOnFailure(bool fail)
        {
            SetBaseState(AppState.Playing, MapIds.Base, PlayerBagRiskState.BaseSafe);
            InstallCurrentSave();
            var model = Game.Interface.GetModel<PlayerBagModel>();
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            Assert.That(model.Container.TryAdd(ItemIds.Cloth, 1, 20, out _, out var error), Is.True, error);
            Assert.That(model.Pocket.TryAdd(ItemIds.Cloth, 2, 20, out _, out error), Is.True, error);
            Assert.That(warehouse.TryAdd(ItemIds.Cloth, 4, 20, out _, out error), Is.True, error);
            var craft = Game.Interface.GetSystem<CraftSystem>();
            Assert.That(craft.TryGetQuotes(out var quotes, out error), Is.True, error);
            var quote = quotes.Single(q => q.Recipe.Id == "craft.medkit.basic");
            Assert.That(quote.Inventory.Materials.Single().BagOwnedAmount, Is.EqualTo(3));
            var events = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => events++);
            if (fail)
                typeof(InventorySystem).GetField("craftAfterMaterialsRemovedTestHook", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(Game.Interface.GetSystem<InventorySystem>(), new Func<string>(() => "blocked"));
            Assert.That(craft.TryCraft("craft.medkit.basic", out _, out error), Is.EqualTo(!fail), error);
            Assert.That(model.Container.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(fail ? 1 : 0));
            Assert.That(model.Pocket.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(fail ? 2 : 1));
            Assert.That(warehouse.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(4));
            Assert.That(model.Container.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(fail ? 0 : 1));
            Assert.That(events, Is.EqualTo(fail ? 0 : 1));
        }

        [Test]
        public void GeneratedCraftRecipesMatchPlan()
        {
            var provider = new LubanRuntimeConfigProvider(LoadGeneratedTables);

            Assert.That(provider.TryGetCraftRecipes(out var recipes, out var error), Is.True, error);
            Assert.That(recipes.Select(item => item.Id), Is.EqualTo(new[] { "craft.medkit.basic", "craft.arrow.basic" }));

            var medkit = recipes[0];
            Assert.That(medkit.OutputItemId, Is.EqualTo(ItemIds.Medkit));
            Assert.That(medkit.OutputAmount, Is.EqualTo(1));
            Assert.That(medkit.Materials.Single().ItemId, Is.EqualTo(ItemIds.Cloth));
            Assert.That(medkit.Materials.Single().Amount, Is.EqualTo(2));
            Assert.That(medkit.Order, Is.EqualTo(100));

            var arrow = recipes[1];
            Assert.That(arrow.OutputItemId, Is.EqualTo(ItemIds.Arrow));
            Assert.That(arrow.OutputAmount, Is.EqualTo(5));
            Assert.That(arrow.Materials.Select(item => item.ItemId), Is.EqualTo(new[] { ItemIds.MetalParts, ItemIds.Cloth }));
            Assert.That(arrow.Materials.Select(item => item.Amount), Is.EqualTo(new[] { 1, 1 }));
            Assert.That(arrow.Order, Is.EqualTo(200));
        }

        [Test]
        public void ProviderRejectsInvalidCraftRowsAndKeepsRecipeErrorsActionable()
        {
            var cases = new[]
            {
                (Row: new CraftRow(" ", ItemIds.Medkit, 1, new[] { new CraftMaterialDefinition(ItemIds.Cloth, 1) }, 1), Fragment: "TbCraftRecipe.Id"),
                (Row: new CraftRow("craft.bad.output", 0, 1, new[] { new CraftMaterialDefinition(ItemIds.Cloth, 1) }, 1), Fragment: "OutputItemId"),
                (Row: new CraftRow("craft.bad.output.ref", 9999, 1, new[] { new CraftMaterialDefinition(ItemIds.Cloth, 1) }, 1), Fragment: "OutputItemId"),
                (Row: new CraftRow("craft.bad.amount", ItemIds.Medkit, 0, new[] { new CraftMaterialDefinition(ItemIds.Cloth, 1) }, 1), Fragment: "OutputAmount"),
                (Row: new CraftRow("craft.bad.materials", ItemIds.Medkit, 1, Array.Empty<CraftMaterialDefinition>(), 1), Fragment: "Materials"),
                (Row: new CraftRow("craft.bad.material.id", ItemIds.Medkit, 1, new[] { new CraftMaterialDefinition(0, 1) }, 1), Fragment: "Materials.ItemId"),
                (Row: new CraftRow("craft.bad.material.amount", ItemIds.Medkit, 1, new[] { new CraftMaterialDefinition(ItemIds.Cloth, 0) }, 1), Fragment: "Materials.Amount"),
                (Row: new CraftRow("craft.bad.material.dup", ItemIds.Medkit, 1, new[] { new CraftMaterialDefinition(ItemIds.Cloth, 1), new CraftMaterialDefinition(ItemIds.Cloth, 1) }, 1), Fragment: "Materials.ItemId"),
                (Row: new CraftRow("craft.bad.material.ref", ItemIds.Medkit, 1, new[] { new CraftMaterialDefinition(9999, 1) }, 1), Fragment: "Materials.ItemId")
            };

            foreach (var testCase in cases)
            {
                var provider = new LubanRuntimeConfigProvider(() => BuildTablesWithCraftRows(testCase.Row));

                Assert.That(provider.TryGetCraftRecipes(out _, out var error), Is.False, testCase.Row.Id);
                Assert.That(error, Does.Contain(testCase.Fragment), testCase.Row.Id);
            }
        }

        [Test]
        public void ProviderSortsCraftRecipesAndReturnsImmutableSnapshots()
        {
            var provider = new LubanRuntimeConfigProvider(() => BuildTablesWithCraftRows(
                new CraftRow("craft.z", ItemIds.Medkit, 1, new[] { new CraftMaterialDefinition(ItemIds.Cloth, 1) }, 10),
                new CraftRow("craft.a", ItemIds.Arrow, 5, new[] { new CraftMaterialDefinition(ItemIds.Cloth, 1), new CraftMaterialDefinition(ItemIds.MetalParts, 1) }, 10)));

            Assert.That(provider.TryGetCraftRecipes(out var recipes, out var error), Is.True, error);
            Assert.That(recipes.Select(recipe => recipe.Id), Is.EqualTo(new[] { "craft.a", "craft.z" }));
            Assert.That(recipes[0].Materials.Select(material => material.ItemId), Is.EqualTo(new[] { ItemIds.MetalParts, ItemIds.Cloth }));
            Assert.Throws<NotSupportedException>(() => ((IList<CraftRecipeDefinition>)recipes).Add(recipes[0]));
            Assert.Throws<NotSupportedException>(() => ((IList<CraftMaterialDefinition>)recipes[0].Materials).Add(new CraftMaterialDefinition(ItemIds.Cloth, 1)));
        }

        [Test]
        public void CraftRequiresPlayingSafeBaseState()
        {
            var craft = Game.Interface.GetSystem<CraftSystem>();
            SetBaseState(AppState.Paused, MapIds.Base, PlayerBagRiskState.BaseSafe);

            Assert.That(craft.TryCraft("craft.medkit.basic", out var reason, out _), Is.False);
            Assert.That(reason, Is.EqualTo(CraftFailureReason.StateNotAllowed));

            SetBaseState(AppState.Playing, MapIds.SurvivalSample, PlayerBagRiskState.BaseSafe);
            Assert.That(craft.TryCraft("craft.medkit.basic", out reason, out _), Is.False);
            Assert.That(reason, Is.EqualTo(CraftFailureReason.StateNotAllowed));
        }

        [Test]
        public void UnsafeBaseStateQuoteAndCommitKeepInventoryEventsAndDirtyUnchanged()
        {
            SetBaseState(AppState.Playing, MapIds.Base, PlayerBagRiskState.InExpedition);
            InstallCurrentSave();
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Cloth, 2, 20, out _, out var error), Is.True, error);
            var recipe = new CraftRecipeDefinition(
                "test.unsafe",
                ItemIds.Medkit,
                1,
                new[] { new CraftMaterialDefinition(ItemIds.Cloth, 2) },
                1);
            var beforeBag = SnapshotText(bag);
            var beforeWarehouse = SnapshotText(warehouse);
            var dirtyBefore = IsSaveDirty();
            var dirtyAtBefore = GetSaveDirtyAt();
            var inventoryEvents = 0;
            var completedEvents = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => inventoryEvents++);
            Game.Interface.RegisterEvent<CraftCompletedEvent>(_ => completedEvents++);

            Assert.That(inventory.TryGetCraftInventoryQuote(recipe, out var quote, out error), Is.True, error);
            Assert.That(quote.StateAllowed, Is.False);
            Assert.That(quote.CanCraft, Is.False);
            Assert.That(quote.FailureReason, Is.EqualTo(CraftFailureReason.StateNotAllowed));
            Assert.That(inventory.TryCommitCraftInventory(recipe, out var reason, out error), Is.False);
            Assert.That(reason, Is.EqualTo(CraftFailureReason.StateNotAllowed));
            Assert.That(SnapshotText(bag), Is.EqualTo(beforeBag));
            Assert.That(SnapshotText(warehouse), Is.EqualTo(beforeWarehouse));
            Assert.That(inventoryEvents, Is.EqualTo(0));
            Assert.That(completedEvents, Is.EqualTo(0));
            Assert.That(IsSaveDirty(), Is.EqualTo(dirtyBefore));
            Assert.That(GetSaveDirtyAt(), Is.EqualTo(dirtyAtBefore));
        }

        [Test]
        public void CraftCommitsBagFirstWithLowSlotMaterialOrderAndSingleSideEffects()
        {
            SetBaseState(AppState.Playing, MapIds.Base, PlayerBagRiskState.BaseSafe);
            InstallCurrentSave();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Cloth, 1, 20, out _, out var error), Is.True, error);
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 1, 20, out _, out error), Is.True, error);
            Assert.That(warehouse.TryAdd(ItemIds.Cloth, 1, 20, out _, out error), Is.True, error);

            var inventoryEvents = 0;
            var completedEvents = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => inventoryEvents++);
            Game.Interface.RegisterEvent<CraftCompletedEvent>(_ => completedEvents++);

            Assert.That(Game.Interface.GetSystem<CraftSystem>().TryCraft("craft.arrow.basic", out var reason, out error), Is.True, error);
            Assert.That(reason, Is.EqualTo(CraftFailureReason.None));
            Assert.That(bag.Slots[0].ItemId, Is.EqualTo(ItemIds.Arrow));
            Assert.That(bag.Slots[0].Amount, Is.EqualTo(5));
            Assert.That(bag.Slots.ContainsKey(1), Is.False);
            Assert.That(bag.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(0));
            Assert.That(warehouse.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(1));
            Assert.That(bag.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(0));
            Assert.That(inventoryEvents, Is.EqualTo(1));
            Assert.That(completedEvents, Is.EqualTo(1));
            Assert.That(IsSaveDirty(), Is.True);
            Assert.That(GetSaveDirtyAt(), Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void MultiMaterialCommitConsumesBagLowSlotsThenWarehouseLowSlotsAndPlacesOutput()
        {
            SetBaseState(AppState.Playing, MapIds.Base, PlayerBagRiskState.BaseSafe);
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 1, 20, out _, out var error), Is.True, error);
            Assert.That(bag.TryAdd(ItemIds.Cloth, 1, 20, out _, out error), Is.True, error);
            Assert.That(warehouse.TryAdd(ItemIds.MetalParts, 2, 20, out _, out error), Is.True, error);
            var recipe = new CraftRecipeDefinition(
                "test.multi.partial",
                ItemIds.Medkit,
                1,
                new[]
                {
                    new CraftMaterialDefinition(ItemIds.Cloth, 1),
                    new CraftMaterialDefinition(ItemIds.MetalParts, 3)
                },
                1);

            Assert.That(inventory.TryCommitCraftInventory(recipe, out var reason, out error), Is.True, error);
            Assert.That(reason, Is.EqualTo(CraftFailureReason.None));
            Assert.That(bag.Slots[0].ItemId, Is.EqualTo(ItemIds.Medkit));
            Assert.That(bag.Slots[0].Amount, Is.EqualTo(1));
            Assert.That(bag.Slots.ContainsKey(1), Is.False);
            Assert.That(warehouse.Slots.ContainsKey(0), Is.False);
            Assert.That(bag.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(0));
            Assert.That(bag.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(0));
            Assert.That(warehouse.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(0));
        }

        [Test]
        public void CraftQuoteAndInvalidPublicEntryKeepContainersEventsAndDirtyUnchanged()
        {
            SetBaseState(AppState.Playing, MapIds.Base, PlayerBagRiskState.BaseSafe);
            InstallCurrentSave();
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Cloth, 2, 20, out _, out var error), Is.True, error);
            var beforeBag = SnapshotText(bag);
            var beforeWarehouse = SnapshotText(warehouse);
            var dirtyBefore = IsSaveDirty();
            var dirtyAtBefore = GetSaveDirtyAt();
            var events = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => events++);

            var quoteRecipe = new CraftRecipeDefinition(
                "test.quote",
                ItemIds.Medkit,
                1,
                new[] { new CraftMaterialDefinition(ItemIds.Cloth, 2) },
                1);
            Assert.That(inventory.TryGetCraftInventoryQuote(quoteRecipe, out var quote, out error), Is.True, error);
            Assert.That(quote.CanCraft, Is.True);
            Assert.That(SnapshotText(bag), Is.EqualTo(beforeBag));
            Assert.That(SnapshotText(warehouse), Is.EqualTo(beforeWarehouse));

            var invalidRecipes = new[]
            {
                new CraftRecipeDefinition(string.Empty, ItemIds.Medkit, 1, new[] { new CraftMaterialDefinition(ItemIds.Cloth, 1) }, 1),
                new CraftRecipeDefinition("test.no.output", 9999, 1, new[] { new CraftMaterialDefinition(ItemIds.Cloth, 1) }, 1),
                new CraftRecipeDefinition("test.zero.output.amount", ItemIds.Medkit, 0, new[] { new CraftMaterialDefinition(ItemIds.Cloth, 1) }, 1),
                new CraftRecipeDefinition("test.no.material", ItemIds.Medkit, 1, Array.Empty<CraftMaterialDefinition>(), 1),
                new CraftRecipeDefinition("test.dup.material", ItemIds.Medkit, 1, new[] { new CraftMaterialDefinition(ItemIds.Cloth, 1), new CraftMaterialDefinition(ItemIds.Cloth, 1) }, 1),
                new CraftRecipeDefinition("test.bad.material.id", ItemIds.Medkit, 1, new[] { new CraftMaterialDefinition(0, 1) }, 1),
                new CraftRecipeDefinition("test.unknown.material", ItemIds.Medkit, 1, new[] { new CraftMaterialDefinition(9999, 1) }, 1),
                new CraftRecipeDefinition("test.bad.amount", ItemIds.Medkit, 1, new[] { new CraftMaterialDefinition(ItemIds.Cloth, 0) }, 1)
            };

            foreach (var invalidRecipe in invalidRecipes)
            {
                Assert.That(inventory.TryGetCraftInventoryQuote(invalidRecipe, out quote, out error), Is.True, invalidRecipe.Id);
                Assert.That(quote.FailureReason, Is.EqualTo(CraftFailureReason.ConfigUnavailable), invalidRecipe.Id);
                Assert.That(quote.Materials, Is.Empty, invalidRecipe.Id);
                Assert.That(inventory.TryCommitCraftInventory(invalidRecipe, out var reason, out error), Is.False, invalidRecipe.Id);
                Assert.That(reason, Is.EqualTo(CraftFailureReason.ConfigUnavailable), invalidRecipe.Id);
            }

            Assert.That(SnapshotText(bag), Is.EqualTo(beforeBag));
            Assert.That(SnapshotText(warehouse), Is.EqualTo(beforeWarehouse));
            Assert.That(events, Is.EqualTo(0));
            Assert.That(IsSaveDirty(), Is.EqualTo(dirtyBefore));
            Assert.That(GetSaveDirtyAt(), Is.EqualTo(dirtyAtBefore));
        }

        [Test]
        public void MaterialsInsufficientThroughCraftSystemKeepsFullSnapshotAndZeroSideEffects()
        {
            SetBaseState(AppState.Playing, MapIds.Base, PlayerBagRiskState.BaseSafe);
            InstallCurrentSave();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Cloth, 1, 20, out _, out var error), Is.True, error);
            var beforeBag = SnapshotText(bag);
            var beforeWarehouse = SnapshotText(warehouse);
            var dirtyBefore = IsSaveDirty();
            var dirtyAtBefore = GetSaveDirtyAt();
            var inventoryEvents = 0;
            var completedEvents = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => inventoryEvents++);
            Game.Interface.RegisterEvent<CraftCompletedEvent>(_ => completedEvents++);

            Assert.That(Game.Interface.GetSystem<CraftSystem>().TryCraft("craft.medkit.basic", out var reason, out error), Is.False);
            Assert.That(reason, Is.EqualTo(CraftFailureReason.MaterialsInsufficient));
            Assert.That(SnapshotText(bag), Is.EqualTo(beforeBag));
            Assert.That(SnapshotText(warehouse), Is.EqualTo(beforeWarehouse));
            Assert.That(inventoryEvents, Is.EqualTo(0));
            Assert.That(completedEvents, Is.EqualTo(0));
            Assert.That(IsSaveDirty(), Is.EqualTo(dirtyBefore));
            Assert.That(GetSaveDirtyAt(), Is.EqualTo(dirtyAtBefore));
        }

        [Test]
        public void OutputOverflowIsQuotedForDropInsteadOfCapacityFailure()
        {
            SetBaseState(AppState.Playing, MapIds.Base, PlayerBagRiskState.BaseSafe);
            InstallCurrentSave();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Cloth, 2, 20, out _, out var error), Is.True, error);
            FillExcept(warehouse, ItemIds.MetalParts);
            var recipe = new CraftRecipeDefinition(
                "test.capacity",
                ItemIds.Medkit,
                500,
                new[] { new CraftMaterialDefinition(ItemIds.Cloth, 2) },
                1);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryGetCraftInventoryQuote(recipe, out var quote, out var quoteError), Is.True, quoteError);
            Assert.That(quote.CanCraft, Is.True);
            Assert.That(quote.HasOutputCapacity, Is.False);
            Assert.That(quote.StorableInBagAmount, Is.EqualTo(55));
            Assert.That(quote.StorableInWarehouseAmount, Is.EqualTo(0));
            Assert.That(quote.DroppedAmount, Is.EqualTo(445));
        }

        [Test]
        public void CraftOverflowUsesPortOnceWithRemainingOutputAndDoesNotUseItWithoutOverflow()
        {
            var overflowRecipe = InstallOverflowRecipe(outputAmount: 500);
            var port = new TestOverflowDropPort();
            Game.Interface.RegisterUtility<ICraftOverflowDropPort>(port);
            SetBaseState(AppState.Playing, MapIds.Base, PlayerBagRiskState.BaseSafe);
            InstallCurrentSave();
            FillContainersForOverflow();

            Assert.That(Game.Interface.GetSystem<CraftSystem>().TryCraft(overflowRecipe.Id, 1, out var result, out var reason, out var error), Is.True, error);
            Assert.That(reason, Is.EqualTo(CraftFailureReason.None));
            Assert.That(result.DroppedAmount, Is.GreaterThan(0));
            Assert.That(port.CreateCalls, Is.EqualTo(1));
            Assert.That(port.LastItemId, Is.EqualTo(ItemIds.Medkit));
            Assert.That(port.LastAmount, Is.EqualTo(result.DroppedAmount));
            Assert.That(port.RollbackCalls, Is.EqualTo(0));

            TestReset.ResetArchitecture();
            InstallArchitecture();
            var noOverflowRecipe = InstallOverflowRecipe(outputAmount: 1);
            port = new TestOverflowDropPort();
            Game.Interface.RegisterUtility<ICraftOverflowDropPort>(port);
            SetBaseState(AppState.Playing, MapIds.Base, PlayerBagRiskState.BaseSafe);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.TryAdd(ItemIds.Cloth, 2, 20, out _, out error), Is.True, error);

            Assert.That(Game.Interface.GetSystem<CraftSystem>().TryCraft(noOverflowRecipe.Id, out reason, out error), Is.True, error);
            Assert.That(reason, Is.EqualTo(CraftFailureReason.None));
            Assert.That(port.CreateCalls, Is.EqualTo(0));
            Assert.That(port.RollbackCalls, Is.EqualTo(0));
        }

        [Test]
        public void CraftOverflowPortFailureRollsBackReceiptContainersAndAllSideEffects()
        {
            var overflowRecipe = InstallOverflowRecipe(outputAmount: 500);
            var port = new TestOverflowDropPort
            {
                CreateSucceeds = false,
                ReceiptOnFailure = new CraftOverflowDropReceipt(new[] { 7101 })
            };
            Game.Interface.RegisterUtility<ICraftOverflowDropPort>(port);
            SetBaseState(AppState.Playing, MapIds.Base, PlayerBagRiskState.BaseSafe);
            InstallCurrentSave();
            FillContainersForOverflow();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            var beforeBag = SnapshotText(bag);
            var beforeWarehouse = SnapshotText(warehouse);
            var dirtyBefore = IsSaveDirty();
            var dirtyAtBefore = GetSaveDirtyAt();
            var inventoryEvents = 0;
            var completedEvents = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => inventoryEvents++);
            Game.Interface.RegisterEvent<CraftCompletedEvent>(_ => completedEvents++);

            Assert.That(Game.Interface.GetSystem<CraftSystem>().TryCraft(overflowRecipe.Id, out var reason, out _), Is.False);
            Assert.That(reason, Is.EqualTo(CraftFailureReason.DropCreationFailed));
            Assert.That(port.CreateCalls, Is.EqualTo(1));
            Assert.That(port.RollbackCalls, Is.EqualTo(1));
            Assert.That(port.LastRollbackReceipt.LootUnitIds, Is.EquivalentTo(new[] { 7101 }));
            Assert.That(SnapshotText(bag), Is.EqualTo(beforeBag));
            Assert.That(SnapshotText(warehouse), Is.EqualTo(beforeWarehouse));
            Assert.That(inventoryEvents, Is.EqualTo(0));
            Assert.That(completedEvents, Is.EqualTo(0));
            Assert.That(IsSaveDirty(), Is.EqualTo(dirtyBefore));
            Assert.That(GetSaveDirtyAt(), Is.EqualTo(dirtyAtBefore));
        }

        [Test]
        public void CraftBatchQuotesAndCommitCoverOneNinetyNineAndInvalidCounts()
        {
            SetBaseState(AppState.Playing, MapIds.Base, PlayerBagRiskState.BaseSafe);
            InstallCurrentSave();
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            var craft = Game.Interface.GetSystem<CraftSystem>();
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            for (var i = 0; i < 9; i++)
            {
                Assert.That(warehouse.TryAdd(ItemIds.Cloth, 20, 20, out _, out var stackError), Is.True, stackError);
            }

            Assert.That(warehouse.TryAdd(ItemIds.Cloth, 18, 20, out _, out var addError), Is.True, addError);
            Assert.That(craft.TryGetQuotes(1, out var oneBatchQuotes, out var oneBatchError), Is.True, oneBatchError);
            var oneBatch = oneBatchQuotes.Single(quote => quote.Recipe.Id == "craft.medkit.basic");
            Assert.That(oneBatch.Inventory.Materials.Single().RequiredAmount, Is.EqualTo(2));
            Assert.That(oneBatch.Inventory.OutputAmount, Is.EqualTo(1));

            Assert.That(craft.TryGetQuotes(99, out var maxBatchQuotes, out var maxBatchError), Is.True, maxBatchError);
            var maxBatch = maxBatchQuotes.Single(quote => quote.Recipe.Id == "craft.medkit.basic");
            Assert.That(maxBatch.Inventory.Materials.Single().RequiredAmount, Is.EqualTo(198));
            Assert.That(maxBatch.Inventory.OutputAmount, Is.EqualTo(99));
            Assert.That(maxBatch.Inventory.CanCraft, Is.True);

            Assert.That(craft.TryGetQuotes(0, out var zeroBatchQuotes, out var zeroBatchError), Is.True, zeroBatchError);
            Assert.That(zeroBatchQuotes.All(quote => quote.Inventory.FailureReason == CraftFailureReason.InvalidBatchCount), Is.True);
            Assert.That(craft.TryGetQuotes(100, out var overBatchQuotes, out var overBatchError), Is.True, overBatchError);
            Assert.That(overBatchQuotes.All(quote => quote.Inventory.FailureReason == CraftFailureReason.InvalidBatchCount), Is.True);

            Assert.That(craft.TryCraft("craft.medkit.basic", 0, out _, out var zeroReason, out _), Is.False);
            Assert.That(zeroReason, Is.EqualTo(CraftFailureReason.InvalidBatchCount));
            Assert.That(craft.TryCraft("craft.medkit.basic", 100, out _, out var overReason, out _), Is.False);
            Assert.That(overReason, Is.EqualTo(CraftFailureReason.InvalidBatchCount));

            var inventoryEvents = 0;
            var completedEvents = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => inventoryEvents++);
            Game.Interface.RegisterEvent<CraftCompletedEvent>(_ => completedEvents++);
            Assert.That(craft.TryCraft("craft.medkit.basic", 99, out var result, out var reason, out var craftError), Is.True, craftError);
            Assert.That(reason, Is.EqualTo(CraftFailureReason.None));
            Assert.That(result.OutputAmount, Is.EqualTo(99));
            Assert.That(result.StoredInBagAmount, Is.EqualTo(60));
            Assert.That(result.StoredInWarehouseAmount, Is.EqualTo(39));
            Assert.That(result.DroppedAmount, Is.EqualTo(0));
            Assert.That(warehouse.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(60));
            Assert.That(warehouse.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(39));
            Assert.That(inventoryEvents, Is.EqualTo(1));
            Assert.That(completedEvents, Is.EqualTo(1));
            Assert.That(IsSaveDirty(), Is.True);
        }

        [Test]
        public void PostMaterialHookFailureRollsBackBothContainers()
        {
            SetBaseState(AppState.Playing, MapIds.Base, PlayerBagRiskState.BaseSafe);
            InstallCurrentSave();
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 1, 20, out _, out var error), Is.True, error);
            Assert.That(warehouse.TryAdd(ItemIds.Cloth, 1, 20, out _, out error), Is.True, error);
            var beforeBag = SnapshotText(bag);
            var beforeWarehouse = SnapshotText(warehouse);
            var dirtyAtBefore = GetSaveDirtyAt();
            var inventoryEvents = 0;
            var completedEvents = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => inventoryEvents++);
            Game.Interface.RegisterEvent<CraftCompletedEvent>(_ => completedEvents++);
            typeof(InventorySystem)
                .GetField("craftAfterMaterialsRemovedTestHook", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(inventory, new Func<string>(() => "hook failure"));

            Assert.That(Game.Interface.GetSystem<CraftSystem>().TryCraft("craft.arrow.basic", out var reason, out _), Is.False);
            Assert.That(reason, Is.EqualTo(CraftFailureReason.InventoryOperationFailed));
            Assert.That(SnapshotText(bag), Is.EqualTo(beforeBag));
            Assert.That(SnapshotText(warehouse), Is.EqualTo(beforeWarehouse));
            Assert.That(IsSaveDirty(), Is.False);
            Assert.That(GetSaveDirtyAt(), Is.EqualTo(dirtyAtBefore));
            Assert.That(inventoryEvents, Is.EqualTo(0));
            Assert.That(completedEvents, Is.EqualTo(0));
        }

        [Test]
        public void CraftOutputSpillsToWarehouseAfterBagPartialAccept()
        {
            SetBaseState(AppState.Playing, MapIds.Base, PlayerBagRiskState.BaseSafe);
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Arrow, 18, 20, out _, out var error), Is.True, error);
            for (var i = 1; i < bag.Capacity; i++)
            {
                Assert.That(bag.TryAdd(ItemIds.Ration, 1, 1, out _, out error), Is.True, error);
            }

            Assert.That(warehouse.TryAdd(ItemIds.MetalParts, 1, 20, out _, out error), Is.True, error);
            Assert.That(warehouse.TryAdd(ItemIds.Cloth, 1, 20, out _, out error), Is.True, error);

            Assert.That(Game.Interface.GetSystem<CraftSystem>().TryCraft("craft.arrow.basic", out var reason, out error), Is.True, error);
            Assert.That(reason, Is.EqualTo(CraftFailureReason.None));
            Assert.That(bag.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(20));
            Assert.That(warehouse.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(3));
        }

        [Test]
        public void CraftCompletedListenerExceptionDoesNotRollbackCommit()
        {
            SetBaseState(AppState.Playing, MapIds.Base, PlayerBagRiskState.BaseSafe);
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Cloth, 2, 20, out _, out var error), Is.True, error);
            Game.Interface.RegisterEvent<CraftCompletedEvent>(_ => throw new InvalidOperationException("listener failed"));
            LogAssert.Expect(UnityEngine.LogType.Error, "[CraftSystem] CraftCompletedEvent 监听器失败：listener failed");

            Assert.That(Game.Interface.GetSystem<CraftSystem>().TryCraft("craft.medkit.basic", out var reason, out error), Is.True, error);
            Assert.That(reason, Is.EqualTo(CraftFailureReason.None));
            Assert.That(bag.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(0));
            Assert.That(bag.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(1));
        }

        [Test]
        public void WarehouseExpansionPublicEntryStillConsumesWarehouseBeforeBag()
        {
            SetBaseState(AppState.Playing, MapIds.Base, PlayerBagRiskState.BaseSafe);
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            Assert.That(warehouse.TryAdd(ItemIds.MetalParts, 4, 20, out _, out var error), Is.True, error);
            Assert.That(warehouse.TryAdd(ItemIds.Cloth, 3, 20, out _, out error), Is.True, error);
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 4, 20, out _, out error), Is.True, error);
            Assert.That(bag.TryAdd(ItemIds.Cloth, 3, 20, out _, out error), Is.True, error);

            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryExpandWarehouse(out error), Is.True, error);
            Assert.That(warehouse.Capacity, Is.EqualTo(InventoryDefaults.WarehouseCapacity + 1));
            Assert.That(warehouse.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(0));
            Assert.That(warehouse.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(0));
            Assert.That(bag.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(4));
            Assert.That(bag.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(3));
        }

        private static void InstallArchitecture()
        {
            var architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(architecture, true);
            architecture.RegisterModel(new SaveModel());
            architecture.RegisterModel(new AppStateModel());
            architecture.RegisterModel(new MapSceneModel());
            architecture.RegisterModel(new PlayerBagModel());
            architecture.RegisterModel(new WarehouseModel());
            architecture.RegisterModel(new WeaponEquipmentModel());
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(LoadGeneratedTables));
            architecture.RegisterSystem(new SaveSystem());
            architecture.RegisterSystem(new InventorySystem());
            architecture.RegisterSystem(new WeaponEquipmentSystem());
            architecture.RegisterSystem(new CraftSystem());
        }

        private static void SetBaseState(AppState state, string mapId, PlayerBagRiskState riskState)
        {
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = state;
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(mapId, null);
            Game.Interface.GetModel<PlayerBagModel>().SetRiskState(riskState);
        }

        private static void InstallCurrentSave()
        {
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
        }

        private static CraftRecipeDefinition InstallOverflowRecipe(int outputAmount)
        {
            var recipe = new CraftRecipeDefinition(
                "craft.overflow.test",
                ItemIds.Medkit,
                outputAmount,
                new[] { new CraftMaterialDefinition(ItemIds.Cloth, 2) },
                1);
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(
                new RecipeOverrideConfigProvider(new LubanRuntimeConfigProvider(LoadGeneratedTables), recipe));
            return recipe;
        }

        private static void FillContainersForOverflow()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Cloth, 2, 20, out _, out var error), Is.True, error);
            for (var i = 1; i < bag.Capacity; i++)
            {
                Assert.That(bag.TryAdd(ItemIds.MetalParts, 1, 1, out _, out error), Is.True, error);
            }

            FillExcept(warehouse, ItemIds.MetalParts);
        }

        private static Tables LoadGeneratedTables()
        {
            return new Tables(name =>
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>($"Assets/Game/Config/{name}.bytes");
                Assert.That(asset, Is.Not.Null, name);
                return new ByteBuf(asset.bytes);
            });
        }

        private static Tables BuildTablesWithCraftRows(params CraftRow[] rows)
        {
            return new Tables(name => name == "tbcraftrecipe"
                ? ToCraftRecipeBuffer(rows)
                : new ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", $"{name}.bytes"))));
        }

        private static ByteBuf ToCraftRecipeBuffer(IReadOnlyList<CraftRow> rows)
        {
            var buffer = new ByteBuf();
            buffer.WriteSize(rows.Count);
            foreach (var row in rows)
            {
                buffer.WriteString(row.Id);
                buffer.WriteInt(row.OutputItemId);
                buffer.WriteInt(row.OutputAmount);
                buffer.WriteSize(row.Materials.Count);
                foreach (var material in row.Materials)
                {
                    buffer.WriteInt(material.ItemId);
                    buffer.WriteInt(material.Amount);
                }

                buffer.WriteInt(row.Order);
            }

            return new ByteBuf(buffer.CopyData());
        }

        private static void FillExcept(ItemContainer container, int itemId)
        {
            for (var i = 0; i < container.Capacity; i++)
            {
                Assert.That(container.TryAdd(itemId, 1, 1, out _, out var error), Is.True, error);
            }
        }

        private static string SnapshotText(ItemContainer container)
        {
            return string.Join(
                "|",
                container.Slots.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value.ItemId}:{pair.Value.Amount}"));
        }

        private static bool IsSaveDirty()
        {
            return (bool)typeof(SaveSystem)
                .GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(Game.Interface.GetSystem<SaveSystem>());
        }

        private static float GetSaveDirtyAt()
        {
            return (float)typeof(SaveSystem)
                .GetField("dirtyAt", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(Game.Interface.GetSystem<SaveSystem>());
        }

        private readonly struct CraftRow
        {
            public CraftRow(
                string id,
                int outputItemId,
                int outputAmount,
                IReadOnlyList<CraftMaterialDefinition> materials,
                int order)
            {
                Id = id;
                OutputItemId = outputItemId;
                OutputAmount = outputAmount;
                Materials = materials;
                Order = order;
            }

            public string Id { get; }
            public int OutputItemId { get; }
            public int OutputAmount { get; }
            public IReadOnlyList<CraftMaterialDefinition> Materials { get; }
            public int Order { get; }
        }

        private sealed class RecipeOverrideConfigProvider : IRuntimeConfigProvider
        {
            private readonly IRuntimeConfigProvider inner;
            private readonly CraftRecipeDefinition recipe;

            public RecipeOverrideConfigProvider(IRuntimeConfigProvider inner, CraftRecipeDefinition recipe)
            {
                this.inner = inner;
                this.recipe = recipe;
            }

            public bool TryGetItem(int id, out ItemDefinition value, out string error) => inner.TryGetItem(id, out value, out error);
            public bool TryGetMap(string id, out MapAssetDefinition value, out string error) => inner.TryGetMap(id, out value, out error);
            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error) => inner.TryGetMapSpawns(mapId, out values, out error);
            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error) => inner.TryGetPlayer(id, out value, out error);
            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error) => inner.TryGetEnemy(id, out value, out error);
            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error) => inner.TryGetLoot(id, out value, out error);
            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error) => inner.TryGetExtraction(id, out value, out error);

            public bool TryGetCraftRecipe(string id, out CraftRecipeDefinition value, out string error)
            {
                value = string.Equals(id, recipe.Id, StringComparison.Ordinal) ? recipe : null;
                error = value == null ? "missing craft recipe" : null;
                return value != null;
            }

            public bool TryGetCraftRecipes(out IReadOnlyList<CraftRecipeDefinition> values, out string error)
            {
                values = new[] { recipe };
                error = null;
                return true;
            }
        }

        private sealed class TestOverflowDropPort : ICraftOverflowDropPort
        {
            public bool CreateSucceeds { get; set; } = true;
            public CraftOverflowDropReceipt ReceiptOnFailure { get; set; } = CraftOverflowDropReceipt.Empty;
            public int CreateCalls { get; private set; }
            public int RollbackCalls { get; private set; }
            public int LastItemId { get; private set; }
            public int LastAmount { get; private set; }
            public CraftOverflowDropReceipt LastRollbackReceipt { get; private set; }

            public bool TryCreateCraftOverflowDrop(int itemId, int amount, out CraftOverflowDropReceipt receipt, out string error)
            {
                CreateCalls++;
                LastItemId = itemId;
                LastAmount = amount;
                receipt = CreateSucceeds ? CraftOverflowDropReceipt.Empty : ReceiptOnFailure;
                error = CreateSucceeds ? null : "test drop creation failed";
                return CreateSucceeds;
            }

            public void RollbackCraftOverflowDrop(CraftOverflowDropReceipt receipt)
            {
                RollbackCalls++;
                LastRollbackReceipt = receipt;
            }
        }
    }
}
