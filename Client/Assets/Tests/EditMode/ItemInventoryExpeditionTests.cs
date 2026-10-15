using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class ItemInventoryExpeditionTests
    {
        private string tempRoot;
        private TestConfigProvider configs;
        private TestMapContentLoader mapLoader;
        private TestResourceLoader resourceLoader;
        private readonly List<GameObject> objects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayItemInventoryExpeditionTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            configs = new TestConfigProvider();
            mapLoader = new TestMapContentLoader(objects);
            resourceLoader = new TestResourceLoader(CreatePlayerPrefab(), CreateProjectilePrefab(), CreateExtractionPrefab());
            ResourceLoader.SetLoaderForTests(resourceLoader);
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(configs);
                architecture.RegisterUtility<IMapContentLoader>(mapLoader);
            };
            CreateMainCamera();

            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot);
            Assert.That(Game.Interface.GetSystem<SaveSystem>().CreateBase(out var created, out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<SaveSystem>().CommitBaseRuntime(created, out error), Is.True, error);
            CreateCraftUiTestRoot();
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(new PlayerConfigDefinition("player", string.Empty, 100, 10, 1f, 1f, 1f, 1f, 5f), false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
        }

        [TearDown]
        public void TearDown()
        {
            TestReset.ResetArchitecture();
            ResourceLoader.ResetForTests();
            Architecture<Game>.OnRegisterPatch = _ => { };
            for (var i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(objects[i]);
                }
            }

            objects.Clear();
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void FormalLootConfigUsesItemIdsAndProvidesAllSources()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new Luban.ByteBuf(File.ReadAllBytes($"Assets/Game/Config/{name}.bytes"))));

            Assert.That(provider.TryGetLoot("loot.metal-parts", out var metal, out var error), Is.True, error);
            Assert.That(provider.TryGetLoot("loot.cloth", out var cloth, out error), Is.True, error);
            Assert.That(provider.TryGetLoot("loot.medkit", out var medkit, out error), Is.True, error);
            Assert.That(metal.ItemId, Is.EqualTo(ItemIds.MetalParts));
            Assert.That(cloth.ItemId, Is.EqualTo(ItemIds.Cloth));
            Assert.That(medkit.ItemId, Is.EqualTo(ItemIds.Medkit));
            Assert.That(provider.TryGetItem(ItemIds.Medkit, out var medkitItem, out error), Is.True, error);
            Assert.That(medkitItem.UseDurationSeconds, Is.EqualTo(1f));
            Assert.That(provider.TryGetItem(ItemIds.MetalParts, out var materialItem, out error), Is.True, error);
            Assert.That(materialItem.UseDurationSeconds, Is.Zero);
            Assert.That(provider.TryGetMapSpawns(MapIds.SurvivalSample, out var spawns, out error), Is.True, error);
            Assert.That(spawns.Count(item => item.ObjectType == MapSpawnObjectType.Loot && item.ConfigId == "loot.metal-parts"), Is.GreaterThanOrEqualTo(1));
            Assert.That(spawns.Count(item => item.ObjectType == MapSpawnObjectType.Loot && item.ConfigId == "loot.cloth"), Is.GreaterThanOrEqualTo(1));
            Assert.That(spawns.Count(item => item.ObjectType == MapSpawnObjectType.Loot && item.ConfigId == "loot.medkit"), Is.GreaterThanOrEqualTo(1));
            Assert.That(provider.TryGetItems(out var items, out error), Is.True, error);
            foreach (var item in items)
            {
                Assert.That(provider.TryGetLootByItemId(item.Id, out var loot, out error), Is.True, $"ItemId={item.Id}: {error}");
                Assert.That(loot.ItemId, Is.EqualTo(item.Id));
            }

            Assert.That(provider.TryGetLootByItemId(10, out var baseMap, out error), Is.True, error);
            Assert.That(provider.TryGetLootByItemId(11, out var survivalMap, out error), Is.True, error);
            Assert.That(provider.TryGetLootByItemId(12, out var missionMap, out error), Is.True, error);
            Assert.That(provider.TryGetLootByItemId(13, out var hejiaMap, out error), Is.True, error);
            Assert.That(baseMap.Id, Is.EqualTo("loot.map.base"));
            Assert.That(survivalMap.Id, Is.EqualTo("loot.map.survival-sample"));
            Assert.That(missionMap.Id, Is.EqualTo("loot.map.mission-sample"));
            Assert.That(hejiaMap.Id, Is.EqualTo("loot.map.hejia-village"));
        }

        [Test]
        public void PlayerBagDropCommitsWholeStackOnceAndCannotRepeatOldSlot()
        {
            EnterInteractiveBase();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 7, 20, out _, out var error), Is.True, error);
            var port = new TestPlayerInventoryDropPort();
            var events = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => events++);

            Assert.That(Game.Interface.GetSystem<InventorySystem>()
                .TryDropPlayerBagStack(0, port, out error), Is.True, error);

            Assert.That(bag.TryGet(0, out _), Is.False);
            Assert.That(port.CreateCalls, Is.EqualTo(1));
            Assert.That(port.ItemId, Is.EqualTo(ItemIds.MetalParts));
            Assert.That(port.Amount, Is.EqualTo(7));
            Assert.That(port.RollbackCalls, Is.Zero);
            Assert.That(events, Is.EqualTo(1));
            Assert.That(IsSaveDirty(), Is.True);

            Assert.That(Game.Interface.GetSystem<InventorySystem>()
                .TryDropPlayerBagStack(0, port, out _), Is.False);
            Assert.That(port.CreateCalls, Is.EqualTo(1));
            Assert.That(events, Is.EqualTo(1));
        }

        [Test]
        public void PlayerBagDropPortFailureRestoresSnapshotWithoutSideEffects()
        {
            EnterInteractiveBase();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Cloth, 5, 20, out _, out var error), Is.True, error);
            var before = SnapshotText(bag);
            var beforeDirty = IsSaveDirty();
            var port = new TestPlayerInventoryDropPort
            {
                ReturnSuccess = false,
                Receipt = new PlayerInventoryDropReceipt(new[] { 7001 })
            };
            var events = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => events++);

            Assert.That(Game.Interface.GetSystem<InventorySystem>()
                .TryDropPlayerBagStack(0, port, out error), Is.False);

            Assert.That(error, Does.Contain("test drop failure"));
            Assert.That(SnapshotText(bag), Is.EqualTo(before));
            Assert.That(port.CreateCalls, Is.EqualTo(1));
            Assert.That(port.RollbackCalls, Is.EqualTo(1));
            Assert.That(events, Is.Zero);
            Assert.That(IsSaveDirty(), Is.EqualTo(beforeDirty));
        }

        [Test]
        public void PlayerBagDropPortExceptionRestoresSnapshotWithoutPublishing()
        {
            EnterInteractiveBase();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Medkit, 3, 5, out _, out var error), Is.True, error);
            var before = SnapshotText(bag);
            var port = new TestPlayerInventoryDropPort { ThrowOnCreate = true };
            var events = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => events++);

            Assert.That(Game.Interface.GetSystem<InventorySystem>()
                .TryDropPlayerBagStack(0, port, out error), Is.False);

            Assert.That(error, Does.Contain("test create exception"));
            Assert.That(SnapshotText(bag), Is.EqualTo(before));
            Assert.That(port.CreateCalls, Is.EqualTo(1));
            Assert.That(port.RollbackCalls, Is.EqualTo(1));
            Assert.That(events, Is.Zero);
        }

        [Test]
        public void PlayerBagDropRollbackExceptionStillRestoresSnapshotAndKeepsPrimaryError()
        {
            EnterInteractiveBase();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 4, 20, out _, out var error), Is.True, error);
            var before = SnapshotText(bag);
            var port = new TestPlayerInventoryDropPort
            {
                ReturnSuccess = false,
                ThrowOnRollback = true,
                Receipt = new PlayerInventoryDropReceipt(new[] { 7002 })
            };

            LogAssert.Expect(LogType.Error, new Regex("玩家背包丢弃世界回滚失败.*test rollback exception"));
            Assert.That(Game.Interface.GetSystem<InventorySystem>()
                .TryDropPlayerBagStack(0, port, out error), Is.False);

            Assert.That(error, Is.EqualTo("test drop failure"));
            Assert.That(SnapshotText(bag), Is.EqualTo(before));
            Assert.That(port.RollbackCalls, Is.EqualTo(1));
        }

        [TestCase(-1)]
        [TestCase(99)]
        public void PlayerBagDropInvalidSlotDoesNotCallPortOrChangeInventory(int slotIndex)
        {
            EnterInteractiveBase();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 2, 20, out _, out var error), Is.True, error);
            var before = SnapshotText(bag);
            var port = new TestPlayerInventoryDropPort();

            Assert.That(Game.Interface.GetSystem<InventorySystem>()
                .TryDropPlayerBagStack(slotIndex, port, out _), Is.False);
            Assert.That(SnapshotText(bag), Is.EqualTo(before));
            Assert.That(port.CreateCalls, Is.Zero);
        }

        [Test]
        public void PlayerBagDropBlockedStateDoesNotCallPortOrChangeInventory()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 2, 20, out _, out var error), Is.True, error);
            var before = SnapshotText(bag);
            var port = new TestPlayerInventoryDropPort();

            Assert.That(Game.Interface.GetSystem<InventorySystem>()
                .TryDropPlayerBagStack(0, port, out _), Is.False);
            Assert.That(SnapshotText(bag), Is.EqualTo(before));
            Assert.That(port.CreateCalls, Is.Zero);
        }

        [Test]
        public void PickupSupportsFullPartialZeroAndRepeatRequests()
        {
            EnterInteractiveExpedition();
            var full = RegisterLoot(100, "loot.medkit", ItemIds.Medkit, 1);
            Assert.That(Game.Interface.GetSystem<LootSystem>().TryCollect(full, PlayerModel.LocalPlayerUnitId), Is.True);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(1));
            Assert.That(Game.Interface.GetSystem<LootSystem>().TryCollect(full, PlayerModel.LocalPlayerUnitId), Is.False);

            FillBagExceptOnePartialMetalSlot();
            var partial = RegisterLoot(101, "loot.metal-parts", ItemIds.MetalParts, 4);
            Assert.That(Game.Interface.GetSystem<LootSystem>().TryCollect(partial, PlayerModel.LocalPlayerUnitId), Is.True);
            Assert.That(Game.Interface.GetModel<LootModel>().TryGet(partial, out var remaining), Is.True);
            Assert.That(remaining.Amount, Is.EqualTo(2));
            var zero = RegisterLoot(102, "loot.cloth", ItemIds.Cloth, 3);
            Assert.That(Game.Interface.GetSystem<LootSystem>().TryCollect(zero, PlayerModel.LocalPlayerUnitId), Is.False);
            Assert.That(Game.Interface.GetModel<LootModel>().TryGet(zero, out var zeroSnapshot), Is.True);
            Assert.That(zeroSnapshot.Amount, Is.EqualTo(3));
        }

        [Test]
        public void PickupFailureRollsBackBagLootUnitAndEvents()
        {
            EnterInteractiveExpedition();
            configs.MissingItemId = ItemIds.Cloth;
            var unitId = RegisterLoot(110, "loot.cloth", ItemIds.Cloth, 3);
            var events = 0;
            Game.Interface.RegisterEvent<LootCollectedEvent>(_ => events++);
            var before = SnapshotText(Game.Interface.GetModel<PlayerBagModel>().Container);

            Assert.That(Game.Interface.GetSystem<LootSystem>().TryCollect(unitId, PlayerModel.LocalPlayerUnitId), Is.False);

            Assert.That(SnapshotText(Game.Interface.GetModel<PlayerBagModel>().Container), Is.EqualTo(before));
            Assert.That(Game.Interface.GetModel<LootModel>().TryGet(unitId, out var loot), Is.True);
            Assert.That(loot.Amount, Is.EqualTo(3));
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(unitId, out _), Is.True);
            Assert.That(events, Is.EqualTo(0));

            configs.MissingItemId = null;
            var lateUnit = RegisterLoot(111, "loot.medkit", ItemIds.Medkit, 1);
            var inventoryEvents = 0;
            var lootEvents = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => inventoryEvents++);
            Game.Interface.RegisterEvent<LootCollectedEvent>(_ => lootEvents++);
            Game.Interface.RegisterEvent<UnitRemovedEvent>(_ => throw new InvalidOperationException("remove listener failed"));
            before = SnapshotText(Game.Interface.GetModel<PlayerBagModel>().Container);

            Assert.That(Game.Interface.GetSystem<LootSystem>().TryCollect(lateUnit, PlayerModel.LocalPlayerUnitId), Is.False);

            Assert.That(SnapshotText(Game.Interface.GetModel<PlayerBagModel>().Container), Is.EqualTo(before));
            Assert.That(Game.Interface.GetModel<LootModel>().TryGet(lateUnit, out var restoredLoot), Is.True);
            Assert.That(restoredLoot.Collected, Is.False);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(lateUnit, out _), Is.True);
            Assert.That(inventoryEvents, Is.EqualTo(0));
            Assert.That(lootEvents, Is.EqualTo(0));

            TestReset.ResetArchitecture();
            SetUp();
            EnterInteractiveExpedition();
            var committedUnit = RegisterLoot(112, "loot.medkit", ItemIds.Medkit, 1);
            Game.Interface.RegisterEvent<LootCollectedEvent>(_ => throw new InvalidOperationException("presentation failed"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[LootSystem\].*LootCollectedEvent.*presentation failed"));

            Assert.That(Game.Interface.GetSystem<LootSystem>().TryCollect(committedUnit, PlayerModel.LocalPlayerUnitId), Is.True);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<LootModel>().TryGet(committedUnit, out var collectedLoot), Is.True);
            Assert.That(collectedLoot.Collected, Is.True);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(committedUnit, out _), Is.False);
        }

        [Test]
        public void WorldLootDisplayUsesConfiguredItemNameAndRemainingAmount()
        {
            var viewObject = new GameObject("loot-view", typeof(BoxCollider), typeof(ResourcePickupView));
            objects.Add(viewObject);
            var labelObject = new GameObject("label", typeof(TextMeshPro));
            objects.Add(labelObject);
            var root = new GameObject("name-root");
            objects.Add(root);
            labelObject.transform.SetParent(root.transform, false);
            var view = viewObject.GetComponent<ResourcePickupView>();
            SetPrivateField(view, "nameRoot", root);
            SetPrivateField(view, "nameLabel", labelObject.GetComponent<TextMeshPro>());

            viewObject.SetActive(false);
            view.RefreshDisplay("Metal Parts", 4);
            viewObject.SetActive(true);
            Assert.That(labelObject.GetComponent<TextMeshPro>().text, Is.EqualTo("Metal Partsx4"));

            view.RefreshDisplay("Metal Parts", 2);
            viewObject.SetActive(false);
            viewObject.SetActive(true);
            Assert.That(labelObject.GetComponent<TextMeshPro>().text, Is.EqualTo("Metal Partsx2"));
        }

        [Test]
        public void MedkitHealsCombatModelAndConsumesExactlyOne()
        {
            EnterInteractiveExpedition();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Medkit, 2, 5, out _, out var error), Is.True, error);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(80);

            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryBeginUsePlayerBagItem(0, out error), Is.True, error);
            Assert.That(bag.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(2));
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(1f);

            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(100));
            Assert.That(bag.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(1));
        }

        [Test]
        public void MedkitRejectsFullDeadInvalidMaterialAndUnsafeStates()
        {
            EnterInteractiveExpedition();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Medkit, 1, 5, out _, out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryBeginUsePlayerBagItem(0, out error), Is.True, error);
            Game.Interface.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.UserCanceled);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(0);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryBeginUsePlayerBagItem(0, out _), Is.False);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(80);
            Game.Interface.GetModel<ExpeditionModel>().MarkDefeated();
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryBeginUsePlayerBagItem(0, out _), Is.False);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(1));

            var materialBag = new ItemContainer(12);
            Assert.That(materialBag.TryAdd(ItemIds.MetalParts, 1, 20, out _, out error), Is.True, error);
        }

        [Test]
        public void EnterExpeditionPersistsRiskAndRollsBackLoadFailure()
        {
            var flow = Game.Interface.GetSystem<ExpeditionFlowSystem>();
            var success = flow.EnterSurvivalAsync(MapIds.SurvivalSample).GetAwaiter().GetResult();
            Assert.That(success.Success, Is.True, success.Error);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().RiskState, Is.EqualTo(PlayerBagRiskState.InExpedition));
            Assert.That(mapLoader.LoadedMapIds, Does.Contain(MapIds.SurvivalSample));

            TestReset.ResetArchitecture();
            SetUp();
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.TryAdd(ItemIds.Medkit, 1, 5, out _, out var error), Is.True, error);
            var before = SnapshotText(Game.Interface.GetModel<PlayerBagModel>().Container);
            mapLoader.FailNextLoad = true;
            var failed = Game.Interface.GetSystem<ExpeditionFlowSystem>().EnterSurvivalAsync(MapIds.SurvivalSample).GetAwaiter().GetResult();

            Assert.That(failed.Success, Is.False);
            Assert.That(mapLoader.LoadedMapIds.Count, Is.EqualTo(1));
            Assert.That(SnapshotText(Game.Interface.GetModel<PlayerBagModel>().Container), Is.EqualTo(before));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().RiskState, Is.EqualTo(PlayerBagRiskState.BaseSafe));
        }

        [Test]
        public void EntryCommitFailureAfterBaseUnloadRestoresBaseWithoutStartingExpedition()
        {
            var maps = Game.Interface.GetSystem<MapSceneSystem>();
            var initial = maps.LoadBaseMapAsync(Game.Interface.GetModel<SaveModel>().CurrentBase).GetAwaiter().GetResult();
            Assert.That(initial.Success, Is.True, initial.Error);
            var oldHandle = Game.Interface.GetModel<MapSceneModel>().CurrentHandle;
            var bag = SnapshotText(Game.Interface.GetModel<PlayerBagModel>().Container);
            Game.Interface.RegisterEvent<MapCurrentReleasedEvent>(evt =>
            {
                if (evt.MapId == MapIds.Base) RegisterPlayer(99, Vector3.zero);
            });
            mapLoader.OnUnloadSuccess = handle =>
            {
                if (handle.MapId != MapIds.Base) Game.Interface.GetSystem<UnitSystem>().RemoveMapInstance(99, default);
            };

            var result = Game.Interface.GetSystem<ExpeditionFlowSystem>()
                .EnterSurvivalAsync(MapIds.SurvivalSample).GetAwaiter().GetResult();

            Assert.That(result.Success, Is.False, "恢复基地不应把进入远征报告为成功");
            Assert.That(result.Error, Is.Not.Empty);
            Assert.That(oldHandle.IsValid, Is.False);
            Assert.That(maps.HasRecoverableCurrentWorld(), Is.True, result.Error);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.EqualTo(MapIds.Base));
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().IsActive, Is.False);
            Assert.That(Game.Interface.GetSystem<SaveSystem>().InspectExpedition().Status, Is.EqualTo(SaveFileStatus.Missing));
            Assert.That(SnapshotText(Game.Interface.GetModel<PlayerBagModel>().Container), Is.EqualTo(bag));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().RiskState, Is.EqualTo(PlayerBagRiskState.BaseSafe));
        }

        [Test]
        public void EntryPreparationFailureKeepsExistingBaseWithoutReloadingIt()
        {
            var maps = Game.Interface.GetSystem<MapSceneSystem>();
            var initial = maps.LoadBaseMapAsync(Game.Interface.GetModel<SaveModel>().CurrentBase).GetAwaiter().GetResult();
            Assert.That(initial.Success, Is.True, initial.Error);
            var handle = Game.Interface.GetModel<MapSceneModel>().CurrentHandle;
            mapLoader.FailNextLoad = true;
            var flow = Game.Interface.GetSystem<ExpeditionFlowSystem>();
            var result = flow.EnterSurvivalAsync(MapIds.SurvivalSample).GetAwaiter().GetResult();
            Assert.That(result.Success, Is.False);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.SameAs(handle));
            Assert.That(mapLoader.LoadedMapIds.Count, Is.EqualTo(2));
            Assert.That(flow.HasPendingEntryRecovery, Is.False);
        }

        [Test]
        public void FailedEntryBaseRecoveryCanRetryWithoutDeletingSave() => VerifyFailedEntryRecovery(false);

        [Test]
        public void FailedEntryBaseRecoveryCanLeaveWithoutDeletingSave() => VerifyFailedEntryRecovery(true);

        private void VerifyFailedEntryRecovery(bool leave)
        {
            var maps = Game.Interface.GetSystem<MapSceneSystem>();
            var save = Game.Interface.GetSystem<SaveSystem>();
            var initial = maps.LoadBaseMapAsync(Game.Interface.GetModel<SaveModel>().CurrentBase).GetAwaiter().GetResult();
            Assert.That(initial.Success, Is.True, initial.Error);
            Game.Interface.RegisterEvent<MapCurrentReleasedEvent>(evt =>
            {
                if (evt.MapId == MapIds.Base) RegisterPlayer(99, Vector3.zero);
            });
            mapLoader.OnUnloadSuccess = handle =>
            {
                if (handle.MapId != MapIds.Base)
                {
                    Game.Interface.GetSystem<UnitSystem>().RemoveMapInstance(99, default);
                    mapLoader.FailBaseLoad = true;
                }
            };
            var flow = Game.Interface.GetSystem<ExpeditionFlowSystem>();
            var failed = flow.EnterSurvivalAsync(MapIds.SurvivalSample).GetAwaiter().GetResult();
            Assert.That(failed.Success, Is.False);
            Assert.That(failed.Error, Does.Contain("基地恢复失败"));
            Assert.That(flow.HasPendingEntryRecovery, Is.True);
            Assert.That(maps.HasRecoverableCurrentWorld(), Is.False);
            Assert.That(flow.EnterSurvivalAsync(MapIds.SurvivalSample).GetAwaiter().GetResult().Success, Is.False);
            var failedAgain = flow.RetryEntryRecoveryAsync().GetAwaiter().GetResult();
            Assert.That(failedAgain.Error, Is.EqualTo(failed.Error), "重试应保留首次进入错误");
            mapLoader.FailBaseLoad = false;
            var recovered = (leave ? flow.LeaveFailedEntryAsync() : flow.RetryEntryRecoveryAsync()).GetAwaiter().GetResult();
            Assert.That(recovered.Success, Is.True, recovered.Error);
            Assert.That(flow.HasPendingEntryRecovery, Is.False);
            Assert.That(maps.HasRecoverableCurrentWorld(), Is.EqualTo(!leave));
            Assert.That(save.InspectBase().IsValid, Is.True);
            Assert.That(save.InspectExpedition().Status, Is.EqualTo(SaveFileStatus.Missing));
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().IsActive, Is.False);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().RiskState, Is.EqualTo(PlayerBagRiskState.BaseSafe));
        }

        [Test]
        public void ConfiguredEntryCreatesTaskSectionBeforeCandidateSave()
        {
            var entered = Game.Interface.GetSystem<ExpeditionFlowSystem>()
                .EnterSurvivalAsync(MapIds.SurvivalSample)
                .GetAwaiter()
                .GetResult();

            Assert.That(entered.Success, Is.True, entered.Error);
            var expedition = Game.Interface.GetModel<ExpeditionModel>();
            var tasks = Game.Interface.GetModel<ExpeditionTaskModel>().Tasks;
            Assert.That(expedition.IsTaskManaged, Is.True);
            Assert.That(tasks, Has.Count.EqualTo(1));
            Assert.That(tasks[0].MapId, Is.EqualTo(expedition.CurrentMapId));
            Assert.That(tasks[0].ExpeditionId, Is.EqualTo(expedition.CurrentExpeditionId));

            var pending = Game.Interface.GetSystem<SaveSystem>().InspectExpedition();
            Assert.That(pending.IsValid, Is.True, pending.Error);
            Assert.That(pending.Data.TaskSchemaVersion, Is.EqualTo(1));
            Assert.That(pending.Data.LegacySourceVersion, Is.Zero);
            Assert.That(pending.Data.Sections.ContainsKey(ExpeditionTaskModel.SaveSectionId), Is.True);
            Assert.That(pending.Data.Sections[ExpeditionTaskModel.SaveSectionId]["expeditionId"]?.Value<string>(),
                Is.EqualTo(expedition.CurrentExpeditionId));
            Assert.That((JArray)pending.Data.Sections[ExpeditionTaskModel.SaveSectionId]["tasks"], Has.Count.EqualTo(1));
        }

        [Test]
        public void EmptyTaskEntryWritesBoundEmptySectionAndRefreshesExtractionAvailable()
        {
            var entered = Game.Interface.GetSystem<ExpeditionFlowSystem>()
                .EnterExpeditionAsync(MapIds.HeJiaVillage)
                .GetAwaiter()
                .GetResult();

            Assert.That(entered.Success, Is.True, entered.Error);
            var expedition = Game.Interface.GetModel<ExpeditionModel>();
            Assert.That(expedition.CurrentMapId, Is.EqualTo(MapIds.HeJiaVillage));
            Assert.That(expedition.IsTaskManaged, Is.False);
            Assert.That(expedition.ElapsedGameMinutes, Is.Zero);
            Assert.That(expedition.CanReturn, Is.True);
            Assert.That(Game.Interface.GetModel<ExpeditionTaskModel>().Tasks, Is.Empty);

            var pending = Game.Interface.GetSystem<SaveSystem>().InspectExpedition();
            Assert.That(pending.IsValid, Is.True, pending.Error);
            var section = pending.Data.Sections[ExpeditionTaskModel.SaveSectionId];
            Assert.That(section["mapId"]?.Value<string>(), Is.EqualTo(MapIds.HeJiaVillage));
            Assert.That(section["expeditionId"]?.Value<string>(), Is.EqualTo(expedition.CurrentExpeditionId));
            Assert.That((JArray)section["tasks"], Is.Empty);

            var extractions = Game.Interface.GetModel<ExtractionModel>().Extractions;
            Assert.That(extractions, Has.Count.EqualTo(1));
            Assert.That(extractions.Values.Single().Available, Is.True);
        }

        [Test]
        public void TaskInitializationFailureRestoresBaseStateWithoutCandidateFile()
        {
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.TryAdd(
                ItemIds.Medkit, 1, 5, out _, out var error), Is.True, error);
            var bagBefore = SnapshotText(Game.Interface.GetModel<PlayerBagModel>().Container);
            configs.ReturnMultipleTasks = true;

            var entered = Game.Interface.GetSystem<ExpeditionFlowSystem>()
                .EnterSurvivalAsync(MapIds.SurvivalSample)
                .GetAwaiter()
                .GetResult();

            Assert.That(entered.Success, Is.False);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().RiskState, Is.EqualTo(PlayerBagRiskState.BaseSafe));
            Assert.That(SnapshotText(Game.Interface.GetModel<PlayerBagModel>().Container), Is.EqualTo(bagBefore));
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().Status, Is.EqualTo(ExpeditionStatus.None));
            Assert.That(Game.Interface.GetModel<ExpeditionTaskModel>().Capture(), Is.Null);
            Assert.That(Game.Interface.GetSystem<SaveSystem>().InspectExpedition().Status, Is.EqualTo(SaveFileStatus.Missing));
        }

        [Test]
        public void TaskConfigFailurePrecedesRecoveryAndLeavesBagFilesAndMapUntouched()
        {
            var save = Game.Interface.GetSystem<SaveSystem>();
            var paths = new SaveFileStore(tempRoot);
            var baseBefore = File.ReadAllText(paths.BasePath);
            Game.Interface.GetSystem<InventorySystem>().MarkRecoveryRequired(PlayerBagRecoveryKind.EnterRollback, "test pending recovery");
            configs.FailTaskQuery = true;

            var entered = Game.Interface.GetSystem<ExpeditionFlowSystem>()
                .EnterSurvivalAsync(MapIds.SurvivalSample)
                .GetAwaiter()
                .GetResult();

            Assert.That(entered.Success, Is.False);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().RiskState, Is.EqualTo(PlayerBagRiskState.RecoveryRequired));
            Assert.That(File.ReadAllText(paths.BasePath), Is.EqualTo(baseBefore));
            Assert.That(save.InspectExpedition().Status, Is.EqualTo(SaveFileStatus.Missing));
            Assert.That(mapLoader.LoadedMapIds, Is.Empty);
        }

        [Test]
        public void FlushedInProgressExpeditionRestartsThroughFormalResumeWithConsistentHudAndTask()
        {
            AssertFlushedExpeditionRestartsThroughFormalResumeWithConsistentHudAndTask(1439.999f, false, 1, "23:59");
        }

        [Test]
        public void FlushedCompletedExpeditionRestartsThroughFormalResumeWithConsistentHudAndTask()
        {
            AssertFlushedExpeditionRestartsThroughFormalResumeWithConsistentHudAndTask(1440f, true, 2, "00:00");
        }

        private void AssertFlushedExpeditionRestartsThroughFormalResumeWithConsistentHudAndTask(
            float elapsedMinutes,
            bool completed,
            int expectedDay,
            string expectedTime)
        {
            var flow = Game.Interface.GetSystem<ExpeditionFlowSystem>();
            var entered = flow.EnterSurvivalAsync(MapIds.SurvivalSample).GetAwaiter().GetResult();
            Assert.That(entered.Success, Is.True, entered.Error);
            var expeditionId = Game.Interface.GetModel<ExpeditionModel>().CurrentExpeditionId;
            Game.Interface.GetSystem<ExpeditionSystem>().AdvanceGameMinutes(elapsedMinutes);
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.Flush(out var error), Is.True, error);

            var written = save.InspectExpedition();
            Assert.That(written.IsValid, Is.True, written.Error);
            Assert.That(written.Data.ElapsedGameMinutes, Is.EqualTo(elapsedMinutes).Within(.001f));
            Assert.That(written.Data.Status, Is.EqualTo(completed ? ExpeditionStatus.ReturnQualified : ExpeditionStatus.Playing));
            Assert.That(written.Data.Sections[ExpeditionTaskModel.SaveSectionId]["tasks"]?[0]?["status"]?.Value<string>(),
                Is.EqualTo(completed ? ExpeditionTaskStatus.Completed.ToString() : ExpeditionTaskStatus.InProgress.ToString()));

            RebuildArchitectureForPendingExpedition();
            save = Game.Interface.GetSystem<SaveSystem>();
            flow = Game.Interface.GetSystem<ExpeditionFlowSystem>();
            var resumed = flow.ResumePendingExpeditionAsync().GetAwaiter().GetResult();
            Assert.That(resumed.Success, Is.True, resumed.Error);
            var expedition = Game.Interface.GetModel<ExpeditionModel>();
            var task = Game.Interface.GetModel<ExpeditionTaskModel>().Tasks.Single();
            Assert.That(expedition.ElapsedGameMinutes, Is.EqualTo(elapsedMinutes).Within(.001f));
            Assert.That(task.CurrentAmount, Is.EqualTo(elapsedMinutes).Within(.001f));
            Assert.That(task.Status, Is.EqualTo(completed ? ExpeditionTaskStatus.Completed : ExpeditionTaskStatus.InProgress));
            Assert.That(expedition.CanReturn, Is.EqualTo(completed));
            Assert.That(save.InspectExpedition().Data.ExpeditionId, Is.EqualTo(expeditionId));

            CreateCraftUiTestRoot();
            UIHelper.SetPrefabLoaderForTests(path => path == "Prefabs/UI/Expedition/ExpeditionHud"
                ? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Expedition/ExpeditionHud.prefab")
                : null);
            var hud = UIHelper.ShowUI<ExpeditionHudUICtrl>();
            var ui = (ExpeditionHudUI)typeof(BaseUICtrl)
                .GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(hud);
            Assert.That(ui, Is.Not.Null);
            Assert.That(ui.TimerText.text, Is.EqualTo(Texts.ExpeditionSurvivalClock(
                Texts.ExpeditionDayClock(expectedDay, expectedTime),
                Texts.ExpeditionDayClock(2, "00:00"))));
        }

        [Test]
        public void ReturnFailureRestoresTaskState()
        {
            var flow = Game.Interface.GetSystem<ExpeditionFlowSystem>();
            var entered = flow.EnterSurvivalAsync(MapIds.SurvivalSample).GetAwaiter().GetResult();
            Assert.That(entered.Success, Is.True, entered.Error);
            Game.Interface.GetSystem<ExpeditionSystem>().AdvanceGameMinutes(PlayerConditionRules.GameMinutesPerDay);
            var before = Game.Interface.GetModel<ExpeditionTaskModel>().Tasks.Single();
            var expeditionId = Game.Interface.GetModel<ExpeditionModel>().CurrentExpeditionId;
            var blocked = Path.Combine(tempRoot, "return-blocked-root");
            File.WriteAllText(blocked, "not a directory");
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(blocked);

            var returned = flow.ReturnToBaseAsync().GetAwaiter().GetResult();

            Assert.That(returned.Success, Is.False);
            var restored = Game.Interface.GetModel<ExpeditionTaskModel>().Tasks.Single();
            Assert.That(restored.InstanceId, Is.EqualTo(before.InstanceId));
            Assert.That(restored.CurrentAmount, Is.EqualTo(before.CurrentAmount));
            Assert.That(restored.Status, Is.EqualTo(ExpeditionTaskStatus.Completed));
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().CurrentExpeditionId, Is.EqualTo(expeditionId));
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().CanReturn, Is.True);
        }

        [Test]
        public void SuccessfulReturnKeepsBagAndRecoveryRetryOnlySaves()
        {
            var flow = Game.Interface.GetSystem<ExpeditionFlowSystem>();
            EnterInteractiveExpedition();
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.TryAdd(ItemIds.MetalParts, 2, 20, out _, out var error), Is.True, error);
            Game.Interface.GetSystem<ExpeditionSystem>().AdvanceGameMinutes(PlayerConditionRules.GameMinutesPerDay);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().Remove(PlayerModel.LocalPlayerUnitId, UnitRemovedReason.MapInstanceReleased), Is.True);

            var returned = flow.ReturnToBaseAsync().GetAwaiter().GetResult();
            Assert.That(returned.Success, Is.True, returned.Error);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().RiskState, Is.EqualTo(PlayerBagRiskState.BaseSafe));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(2));
            Assert.That(Game.Interface.GetModel<WarehouseModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().Status, Is.EqualTo(ExpeditionStatus.None));
            Assert.That(Game.Interface.GetSystem<SaveSystem>().InspectBase().Data.Sections["another-day.player-bag"]["riskState"].Value<string>(),
                Is.EqualTo(PlayerBagRiskState.BaseSafe.ToString()));

            TestReset.ResetArchitecture();
            SetUp();
            flow = Game.Interface.GetSystem<ExpeditionFlowSystem>();
            var entered = flow.EnterSurvivalAsync(MapIds.SurvivalSample).GetAwaiter().GetResult();
            Assert.That(entered.Success, Is.True, entered.Error);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.TryAdd(ItemIds.MetalParts, 2, 20, out _, out error), Is.True, error);
            Game.Interface.GetSystem<ExpeditionSystem>().AdvanceGameMinutes(PlayerConditionRules.GameMinutesPerDay);
            var blocked = Path.Combine(tempRoot, "blocked-save-root");
            File.WriteAllText(blocked, "not a directory");
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(blocked);
            var failed = flow.ReturnToBaseAsync().GetAwaiter().GetResult();
            var loadCountAfterFailure = mapLoader.LoadedMapIds.Count;
            Assert.That(failed.Success, Is.False);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().RiskState, Is.EqualTo(PlayerBagRiskState.InExpedition));

            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot);
            Assert.That(mapLoader.LoadedMapIds.Count, Is.EqualTo(loadCountAfterFailure));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(2));
        }

        [Test]
        public void DeathReturnClearsBagOnceAndNeverChangesWarehouse()
        {
            var flow = Game.Interface.GetSystem<ExpeditionFlowSystem>();
            EnterInteractiveExpedition();
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.TryAdd(ItemIds.MetalParts, 2, 20, out _, out var error), Is.True, error);
            Assert.That(Game.Interface.GetModel<WarehouseModel>().Container.TryAdd(ItemIds.Cloth, 3, 20, out _, out error), Is.True, error);
            var warehouseBefore = SnapshotText(Game.Interface.GetModel<WarehouseModel>().Container);
            Game.Interface.GetModel<ExpeditionModel>().MarkDefeated();
            Assert.That(Game.Interface.GetSystem<UnitSystem>().Remove(PlayerModel.LocalPlayerUnitId, UnitRemovedReason.MapInstanceReleased), Is.True);
            mapLoader.FailNextLoad = true;
            LogAssert.Expect(LogType.Error, "[BaseUICtrl] UI Prefab 未找到：Prefabs/UI/Home/Start");
            var failed = flow.FailExpeditionAndReturnToBaseAsync().GetAwaiter().GetResult();
            Assert.That(failed.Success, Is.False);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.Slots.Count, Is.EqualTo(0));
            Assert.That(SnapshotText(Game.Interface.GetModel<WarehouseModel>().Container), Is.EqualTo(warehouseBefore));
        }

        [Test]
        public void ResumedExpeditionReturnRestoresBaseSectionsAndCraftingAccess()
        {
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(Game.Interface.GetModel<WarehouseModel>().Container.TryAdd(ItemIds.Cloth, 3, 20, out _, out var error), Is.True, error);
            var baseData = save.InspectBase().Data;
            Assert.That(save.CaptureBaseSections(baseData, out error), Is.True, error);
            Assert.That(save.SaveBase(baseData, out error), Is.True, error);

            EnterInteractiveExpedition();
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.TryAdd(ItemIds.MetalParts, 2, 20, out _, out error), Is.True, error);
            var pendingModel = Game.Interface.GetModel<ExpeditionModel>();
            var pending = pendingModel.CaptureSave(pendingModel.CurrentExpeditionId, DateTime.UtcNow);
            Assert.That(save.CaptureExpeditionSections(pending, out error), Is.True, error);
            Assert.That(save.SaveExpedition(pending, out error), Is.True, error);

            RebuildArchitectureForPendingExpedition();
            save = Game.Interface.GetSystem<SaveSystem>();
            var flow = Game.Interface.GetSystem<ExpeditionFlowSystem>();
            var resumed = flow.ResumePendingExpeditionAsync().GetAwaiter().GetResult();
            Assert.That(resumed.Success, Is.True, resumed.Error);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().RiskState, Is.EqualTo(PlayerBagRiskState.InExpedition));
            Assert.That(Game.Interface.GetModel<WarehouseModel>().Container.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(0),
                "重启恢复副本不得提前恢复 BaseOnly 仓库");

            Game.Interface.GetSystem<ExpeditionSystem>().AdvanceGameMinutes(PlayerConditionRules.GameMinutesPerDay);
            var returned = flow.ReturnToBaseAsync().GetAwaiter().GetResult();
            Assert.That(returned.Success, Is.True, returned.Error);
            Assert.That(save.InspectExpedition().Status, Is.EqualTo(SaveFileStatus.Missing));
            Assert.That(Game.Interface.GetModel<SaveModel>().CurrentDomain, Is.EqualTo(OfflineSaveDomain.Base));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().RiskState, Is.EqualTo(PlayerBagRiskState.BaseSafe));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(2));
            Assert.That(Game.Interface.GetModel<WarehouseModel>().Container.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(3));
            Assert.That(save.InspectBase().Data.Sections["another-day.player-bag"]["riskState"].Value<string>(),
                Is.EqualTo(PlayerBagRiskState.BaseSafe.ToString()));

            CreateCraftUiTestRoot();
            Assert.That(CraftUICtrl.TryShow(out error), Is.True, error);
        }

        [Test]
        public void BaseWriteFailurePersistsReturnQualifiedExpeditionForRestartRetry()
        {
            var save = Game.Interface.GetSystem<SaveSystem>();
            var flow = Game.Interface.GetSystem<ExpeditionFlowSystem>();
            var entered = flow.EnterSurvivalAsync(MapIds.SurvivalSample).GetAwaiter().GetResult();
            Assert.That(entered.Success, Is.True, entered.Error);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.TryAdd(ItemIds.MetalParts, 2, 20, out _, out var error), Is.True, error);
            Game.Interface.GetSystem<ExpeditionSystem>().AdvanceGameMinutes(PlayerConditionRules.GameMinutesPerDay);

            var paths = new SaveFileStore(tempRoot);
            // 普通保存现在追加变化日志；锁定真实写入目标而不是不再改写的入口备份。
            var baseManifest=JObject.Parse(File.ReadAllText(paths.BasePath));
            var baseJournal=Path.Combine(Path.GetDirectoryName(paths.BasePath),(string)baseManifest["Journal"]);
            using (new FileStream(baseJournal, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var failed = flow.ReturnToBaseAsync().GetAwaiter().GetResult();
                Assert.That(failed.Success, Is.False);
            }

            var pending = save.InspectExpedition();
            Assert.That(pending.IsValid, Is.True, pending.Error);
            Assert.That(pending.Data.Status, Is.EqualTo(ExpeditionStatus.ReturnQualified));

            RebuildArchitectureForPendingExpedition();
            save = Game.Interface.GetSystem<SaveSystem>();
            flow = Game.Interface.GetSystem<ExpeditionFlowSystem>();
            var resumed = flow.ResumePendingExpeditionAsync().GetAwaiter().GetResult();
            Assert.That(resumed.Success, Is.True, resumed.Error);
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().CanReturn, Is.True);
            var retried = flow.ReturnToBaseAsync().GetAwaiter().GetResult();
            Assert.That(retried.Success, Is.True, retried.Error);
            Assert.That(save.InspectExpedition().Status, Is.EqualTo(SaveFileStatus.Missing));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().RiskState, Is.EqualTo(PlayerBagRiskState.BaseSafe));
        }

        [Test]
        public void DeleteFailureRestoresExpeditionRuntimeAndRetrySettlesOnlyOnce()
        {
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(Game.Interface.GetModel<WarehouseModel>().Container.TryAdd(ItemIds.Cloth, 3, 20, out _, out var error), Is.True, error);
            var baseData = save.InspectBase().Data;
            Assert.That(save.CaptureBaseSections(baseData, out error), Is.True, error);
            Assert.That(save.SaveBase(baseData, out error), Is.True, error);

            var flow = Game.Interface.GetSystem<ExpeditionFlowSystem>();
            var entered = flow.EnterSurvivalAsync(MapIds.SurvivalSample).GetAwaiter().GetResult();
            Assert.That(entered.Success, Is.True, entered.Error);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.TryAdd(ItemIds.MetalParts, 2, 20, out _, out error), Is.True, error);
            Assert.That(Game.Interface.GetModel<WarehouseModel>().Container.TryAdd(ItemIds.Cloth, 2, 20, out _, out error), Is.True, error);
            Game.Interface.GetSystem<ExpeditionSystem>().AdvanceGameMinutes(PlayerConditionRules.GameMinutesPerDay);
            var expeditionId = Game.Interface.GetModel<ExpeditionModel>().CurrentExpeditionId;

            save.FailNextDeleteExpeditionForTests();
            var failed = flow.ReturnToBaseAsync().GetAwaiter().GetResult();

            Assert.That(failed.Success, Is.False);
            Assert.That(Game.Interface.GetModel<SaveModel>().CurrentDomain, Is.EqualTo(OfflineSaveDomain.Expedition));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().RiskState, Is.EqualTo(PlayerBagRiskState.InExpedition));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(2));
            Assert.That(Game.Interface.GetModel<WarehouseModel>().Container.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(5),
                "删除失败必须在基地提交后恢复副本快照中的 BaseOnly 数据");
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().CanReturn, Is.True);
            var pending = save.InspectExpedition();
            Assert.That(pending.IsValid, Is.True, pending.Error);
            Assert.That(pending.Data.Status, Is.EqualTo(ExpeditionStatus.ReturnQualified));

            var retried = flow.ReturnToBaseAsync().GetAwaiter().GetResult();
            Assert.That(retried.Success, Is.True, retried.Error);
            var settledIds = save.InspectBase().Data.SettledExpeditionIds;
            Assert.That(settledIds.Count(id => id == expeditionId), Is.EqualTo(1));
            Assert.That(save.InspectExpedition().Status, Is.EqualTo(SaveFileStatus.Missing));
            Assert.That(Game.Interface.GetModel<WarehouseModel>().Container.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(3));
        }

        [Test]
        public void UnsafeLoadedSaveReturnsToBaseClearsBagAndRecovers()
        {
            var save = Game.Interface.GetSystem<SaveSystem>();
            var expedition = save.CaptureExpedition(Guid.NewGuid().ToString("N"), MapIds.SurvivalSample, DateTime.UtcNow, out var error);
            Assert.That(expedition, Is.Not.Null, error);
            expedition.Sections[ExpeditionTaskModel.SaveSectionId]=new JObject { ["schemaVersion"]=1,["mapId"]=expedition.MapId,
                ["expeditionId"]=expedition.ExpeditionId,["tasks"]=new JArray() };
            Assert.That(save.SaveExpedition(expedition, out error), Is.True, error);
            Assert.That(save.PrepareExpedition().IsValid, Is.True);
        }

        [Test]
        public void LegacyTemporaryResourceChainIsRemoved()
        {
            var productFiles = Directory.GetFiles("Assets/Game/Scripts", "*.cs", SearchOption.AllDirectories);
            var productText = string.Join("\n", productFiles.Select(File.ReadAllText));
            Assert.That(productText, Does.Not.Contain("CarriedTestResourceCount"));
            Assert.That(productText, Does.Not.Contain("CollectExpeditionTestResource"));
            Assert.That(productText, Does.Not.Contain("TryCollectExpeditionTestResource"));
            Assert.That(productText, Does.Not.Contain("CanCommitExpeditionResources"));
            Assert.That(productText, Does.Not.Contain("CommitExpeditionResources"));
            Assert.That(productText, Does.Not.Contain("test.resource"));
            Assert.That(productText, Does.Not.Contain("loot.test-resource"));
            Assert.That(typeof(UnitModel).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Any(field => field.Name.Contains("Health")), Is.False);
        }

        private void EnterInteractiveExpedition()
        {
            RegisterPlayer(1, Vector3.zero);
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            var expeditionId = Guid.NewGuid().ToString("N");
            Assert.That(Game.Interface.GetSystem<ExpeditionSystem>().TryBeginConfiguredExpedition(
                MapIds.SurvivalSample,
                expeditionId,
                new[] { new ExpeditionTaskDefinition("expedition.survival-sample.survive", MapIds.SurvivalSample, "存活一天", "task.expedition.survival_sample.survive.name", TaskObjectiveType.Survive, 60f, 100) },
                out var taskError), Is.True, taskError);
            Game.Interface.GetModel<SaveModel>().SetExpedition(
                new ExpeditionSaveData { ExpeditionId = expeditionId, MapId = MapIds.SurvivalSample });
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(PlayerBagRiskState.InExpedition, "test", false, out var error), Is.True, error);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(new PlayerConfigDefinition("player", string.Empty, 100, 10, 1f, 1f, 1f, 1f, 5f), false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
        }

        private static void EnterInteractiveBase()
        {
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
        }

        private static bool IsSaveDirty()
        {
            return (bool)typeof(SaveSystem)
                .GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(Game.Interface.GetSystem<SaveSystem>());
        }

        private sealed class TestPlayerInventoryDropPort : IPlayerInventoryDropPort
        {
            public bool ReturnSuccess { get; set; } = true;
            public bool ThrowOnCreate { get; set; }
            public bool ThrowOnRollback { get; set; }
            public PlayerInventoryDropReceipt Receipt { get; set; } =
                new PlayerInventoryDropReceipt(new[] { 7000 });
            public int CreateCalls { get; private set; }
            public int RollbackCalls { get; private set; }
            public int ItemId { get; private set; }
            public int Amount { get; private set; }

            public bool TryCreatePlayerInventoryDrop(
                int itemId,
                int amount,
                out PlayerInventoryDropReceipt receipt,
                out string error)
            {
                CreateCalls++;
                ItemId = itemId;
                Amount = amount;
                receipt = Receipt;
                if (ThrowOnCreate)
                {
                    throw new InvalidOperationException("test create exception");
                }

                error = ReturnSuccess ? null : "test drop failure";
                return ReturnSuccess;
            }

            public void RollbackPlayerInventoryDrop(PlayerInventoryDropReceipt receipt)
            {
                RollbackCalls++;
                if (ThrowOnRollback)
                {
                    throw new InvalidOperationException("test rollback exception");
                }
            }
        }

        private void RebuildArchitectureForPendingExpedition()
        {
            TestReset.ResetArchitecture();
            ResourceLoader.ResetForTests();
            for (var i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(objects[i]);
                }
            }

            objects.Clear();
            configs = new TestConfigProvider();
            mapLoader = new TestMapContentLoader(objects);
            resourceLoader = new TestResourceLoader(CreatePlayerPrefab(), CreateProjectilePrefab(), CreateExtractionPrefab());
            ResourceLoader.SetLoaderForTests(resourceLoader);
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(configs);
                architecture.RegisterUtility<IMapContentLoader>(mapLoader);
            };
            CreateMainCamera();
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(new PlayerConfigDefinition("player", string.Empty, 100, 10, 1f, 1f, 1f, 1f, 5f), false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
        }

        private void CreateCraftUiTestRoot()
        {
            UIHelper.SetPrefabLoaderForTests(path => path switch
            {
                "Prefabs/UI/Craft/Craft" => CreateCraftPrefab(),
                "Prefabs/UI/Notify/Float" => CreateFloatPrefab(),
                _ => null
            });

            if (UnityEngine.Object.FindObjectOfType<UIRoot>() != null)
            {
                return;
            }

            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            objects.Add(root);
            root.AddComponent<UIRoot>();
            foreach (var layer in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var layerRoot = new GameObject(layer, typeof(RectTransform));
                layerRoot.transform.SetParent(root.transform, false);
                objects.Add(layerRoot);
            }

            var eventSystem = new GameObject("EventSystem", typeof(RectTransform), typeof(UnityEngine.EventSystems.EventSystem));
            eventSystem.transform.SetParent(root.transform, false);
            objects.Add(eventSystem);
            var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputModuleType != null) eventSystem.AddComponent(inputModuleType);
        }

        private static GameObject CreateFloatPrefab()
        {
            return new GameObject("Float", typeof(RectTransform), typeof(FloatUI));
        }

        private static GameObject CreateCraftPrefab()
        {
            var root = new GameObject("Craft", typeof(RectTransform));
            var baseUi = root.AddComponent<BaseUI>();
            var ui = root.AddComponent<CraftUI>();
            ui.BtnClose = CreateCraftButton(root.transform, "BtnClose");
            ui.BtnBatchMinus = CreateCraftButton(root.transform, "BtnBatchMinus");
            ui.BtnBatchPlus = CreateCraftButton(root.transform, "BtnBatchPlus");
            ui.BtnCraft = CreateCraftButton(root.transform, "BtnCraft");
            ui.DetailIcon = new GameObject("DetailIcon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            ui.DetailIcon.transform.SetParent(root.transform, false);
            ui.ReasonRoot = new GameObject("ReasonRoot");
            ui.ReasonRoot.transform.SetParent(root.transform, false);
            ui.TextDetailName = CreateCraftText(root.transform, "TextDetailName");
            ui.TextDetailDesc = CreateCraftText(root.transform, "TextDetailDesc");
            ui.TextSingleOutput = CreateCraftText(root.transform, "TextSingleOutput");
            ui.TextBatchCount = CreateCraftText(root.transform, "TextBatchCount");
            ui.TextOutputTotal = CreateCraftText(root.transform, "TextOutputTotal");
            ui.TextReason = CreateCraftText(root.transform, "TextReason");
            UnityEngine.Object.DestroyImmediate(baseUi);
            return root;
        }

        private static Button CreateCraftButton(Transform parent, string name)
        {
            var button = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            button.transform.SetParent(parent, false);
            return button;
        }

        private static TextMeshProUGUI CreateCraftText(Transform parent, string name)
        {
            var text = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(parent, false);
            return text;
        }

        private int RegisterLoot(int unitId, string configId, int itemId, int amount)
        {
            configs.Loots[configId] = new LootConfigDefinition(configId, "loot", itemId, amount);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                unitId, 1, UnitType.Loot, UnitSource.MapPlaced, configId, Vector3.zero, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<LootSystem>().TryRegisterLoot(unitId, configs.Loots[configId], out error), Is.True, error);
            return unitId;
        }

        private void RegisterPlayer(long mapInstanceId, Vector3 position)
        {
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                PlayerModel.LocalPlayerUnitId, mapInstanceId, UnitType.Player, UnitSource.Player, "player", position, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out var error), Is.True, error);
        }

        private void FillBagExceptOnePartialMetalSlot()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            bag.RestoreSnapshot(new ItemContainer(12).CaptureSnapshot());
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 18, 20, out _, out var error), Is.True, error);
            for (var i = 1; i < 12; i++)
            {
                Assert.That(bag.TryAdd(ItemIds.Cloth, 20, 20, out _, out error), Is.True, error);
            }
        }

        private static string SnapshotText(ItemContainer container)
        {
            return string.Join("|", container.Slots.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value.ItemId}:{pair.Value.Amount}"));
        }

        private void CreateMainCamera()
        {
            var camera = new GameObject("Main Camera", typeof(Camera));
            camera.tag = "MainCamera";
            objects.Add(camera);
        }

        private GameObject CreatePlayerPrefab()
        {
            var player = new GameObject("player-prefab", typeof(CharacterController), typeof(PlayerView));
            objects.Add(player);
            var tiliRoot = new GameObject("tili-root");
            objects.Add(tiliRoot);
            tiliRoot.transform.SetParent(player.transform, false);
            var tiliFillObject = new GameObject("tili-fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            objects.Add(tiliFillObject);
            tiliFillObject.transform.SetParent(tiliRoot.transform, false);
            var meleeAttachRoot = new GameObject("melee-attach").transform;
            objects.Add(meleeAttachRoot.gameObject);
            meleeAttachRoot.SetParent(player.transform, false);
            var rangedAttachRoot = new GameObject("ranged-attach").transform;
            objects.Add(rangedAttachRoot.gameObject);
            rangedAttachRoot.SetParent(player.transform, false);
            var view = player.GetComponent<PlayerView>();
            var flashlight = new GameObject("flashlight", typeof(Light));
            flashlight.transform.SetParent(player.transform, false);
            flashlight.GetComponent<Light>().type = LightType.Spot;
            SetPrivateField(view, "flashlight", flashlight.GetComponent<Light>());
            var emitter = player.AddComponent<DamageNumberEmitter>();
            SetPrivateField(emitter, "numberPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/WorldHud/DamageNumberBox.prefab").GetComponent<DamageNumberView>());
            SetPrivateField(view, "damageNumberEmitter", emitter);
            SetPrivateField(view, "characterController", player.GetComponent<CharacterController>());
            SetPrivateField(view, "cameraProfile", ScriptableObject.CreateInstance<TopDownCameraProfile>());
            SetPrivateField(view, "shoulderCameraProfile", ScriptableObject.CreateInstance<ShoulderCameraProfile>());
            SetPrivateField(view, "tiliRingCanvas", tiliRoot);
            SetPrivateField(view, "tiliRingFill", tiliFillObject.GetComponent<Image>());
            SetPrivateField(view, "meleeWeaponAttachRoot", meleeAttachRoot);
            SetPrivateField(view, "weaponSwingRoot", meleeAttachRoot);
            SetPrivateField(view, "rangedWeaponAttachRoot", rangedAttachRoot);
            return player;
        }

        private GameObject CreateProjectilePrefab()
        {
            var projectile = new GameObject("projectile-prefab", typeof(BoxCollider), typeof(Rigidbody), typeof(ProjectileView));
            objects.Add(projectile);
            projectile.GetComponent<BoxCollider>().isTrigger = true;
            projectile.GetComponent<Rigidbody>().isKinematic = true;
            return projectile;
        }

        private GameObject CreateExtractionPrefab()
        {
            var prefab = new GameObject("extraction-prefab", typeof(BoxCollider), typeof(TaskExitView));
            objects.Add(prefab);
            prefab.GetComponent<BoxCollider>().isTrigger = true;
            var visual = new GameObject("visual-root");
            objects.Add(visual);
            visual.transform.SetParent(prefab.transform, false);
            SetPrivateField(prefab.GetComponent<TaskExitView>(), "visualRoot", visual);
            // 正式撤离点已要求提示引用；夹具也提供同样的最小 View 契约。
            var prompt=new GameObject("PromptRoot",typeof(RectTransform),typeof(Canvas));prompt.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            prompt.transform.SetParent(prefab.transform,false);objects.Add(prompt);
            var label=new GameObject("PromptText",typeof(RectTransform),typeof(TextMeshProUGUI));label.transform.SetParent(prompt.transform,false);objects.Add(label);
            SetPrivateField(prefab.GetComponent<TaskExitView>(),"promptRoot",prompt);
            SetPrivateField(prefab.GetComponent<TaskExitView>(),"promptText",label.GetComponent<TextMeshProUGUI>());
            return prefab;
        }

        private static MapContext CreateMapContext(string mapId)
        {
            var root = new GameObject("map-root-" + mapId);
            var runtime = new GameObject("runtime-root").transform;
            runtime.SetParent(root.transform, false);
            var contextObject = new GameObject("map-context-" + mapId);
            var context = contextObject.AddComponent<MapContext>();
            SetPrivateField(context, "mapContentRoot", root);
            SetPrivateField(context, "dynamicObjectRoot", runtime);
            return context;
        }

        private static void SetPrivateField<TTarget, TValue>(TTarget target, string fieldName, TValue value)
        {
            typeof(TTarget)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }

        private sealed class TestMapContentLoader : IMapContentLoader
        {
            private readonly List<GameObject> objects;
            private readonly List<TestMapHandle> handles = new List<TestMapHandle>();

            public TestMapContentLoader(List<GameObject> objects)
            {
                this.objects = objects;
            }

            public bool FailNextLoad { get; set; }
            public bool FailBaseLoad { get; set; }
            public Action<IMapContentHandle> OnUnloadSuccess { get; set; }
            public List<string> LoadedMapIds { get; } = new List<string>();

            public UniTask<MapContentLoadResult> LoadAsync(
                string mapId,
                string address,
                Action<float> onProgress,
                CancellationToken cancellationToken = default)
            {
                LoadedMapIds.Add(mapId);
                if (FailNextLoad || FailBaseLoad && mapId == MapIds.Base)
                {
                    FailNextLoad = false;
                    return UniTask.FromResult(MapContentLoadResult.Fail("load failed"));
                }

                var context = CreateMapContext(mapId);
                objects.Add(context.gameObject);
                objects.Add(context.MapContentRoot);
                objects.Add(context.DynamicObjectRoot.gameObject);
                var handle = new TestMapHandle(mapId, context);
                handles.Add(handle);
                return UniTask.FromResult(MapContentLoadResult.Ok(handle));
            }

            public UniTask<MapContentUnloadResult> UnloadAsync(
                IMapContentHandle handle,
                CancellationToken cancellationToken = default)
            {
                if (handle is TestMapHandle testHandle)
                {
                    testHandle.Valid = false;
                    OnUnloadSuccess?.Invoke(handle);
                }

                return UniTask.FromResult(MapContentUnloadResult.Ok());
            }
        }

        private sealed class TestMapHandle : IMapContentHandle
        {
            public TestMapHandle(string mapId, MapContext context)
            {
                MapId = mapId;
                Context = context;
            }

            public string MapId { get; }
            public MapContext Context { get; }
            public bool Valid { get; set; } = true;
            public bool IsValid => Valid;
        }

        private sealed class TestResourceLoader : IResourceLoader
        {
            private readonly GameObject playerPrefab;
            private readonly GameObject projectilePrefab;
            private readonly GameObject extractionPrefab;

            public TestResourceLoader(GameObject playerPrefab, GameObject projectilePrefab, GameObject extractionPrefab)
            {
                this.playerPrefab = playerPrefab;
                this.projectilePrefab = projectilePrefab;
                this.extractionPrefab = extractionPrefab;
            }

            public T LoadAsset<T>(string path) where T : UnityEngine.Object
            {
                return Select(path) as T;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
            {
                onLoaded?.Invoke(Select(path) as T);
                return new ResourceLoadHandle(new object());
            }

            public void Release(ResourceLoadHandle handle)
            {
            }

            private GameObject Select(string path)
            {
                if (path == "Prefabs/Combat/GrayboxArrow") return projectilePrefab;
                if (path == "Prefabs/SceneObjects/TaskExit") return extractionPrefab;
                return playerPrefab;
            }
        }

        private sealed class TestConfigProvider : IRuntimeConfigProvider
        {
            public readonly Dictionary<string, LootConfigDefinition> Loots = new Dictionary<string, LootConfigDefinition>();

            public int? MissingItemId { get; set; }
            public bool ReturnMultipleTasks { get; set; }
            public bool FailTaskQuery { get; set; }

            public bool TryGetItem(int id, out ItemDefinition value, out string error)
            {
                if (MissingItemId.HasValue && id == MissingItemId.Value)
                {
                    value = default;
                    error = "missing item";
                    return false;
                }

                if (id == ItemIds.MetalParts)
                {
                    value = new ItemDefinition(id, "Metal Parts", string.Empty, string.Empty, string.Empty, string.Empty, global::ItemType.Material, 20, global::ItemUseEffect.None, 0);
                    error = null;
                    return true;
                }

                if (id == ItemIds.Cloth)
                {
                    value = new ItemDefinition(id, "Cloth", string.Empty, string.Empty, string.Empty, string.Empty, global::ItemType.Material, 20, global::ItemUseEffect.None, 0);
                    error = null;
                    return true;
                }

                if (id == ItemIds.Medkit)
                {
                    value = new ItemDefinition(id, "Medkit", string.Empty, string.Empty, string.Empty, string.Empty, global::ItemType.Consumable, 5, global::ItemUseEffect.RestoreHealth, 30, 1f);
                    error = null;
                    return true;
                }

                value = default;
                error = "missing item";
                return false;
            }

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = new MapAssetDefinition(id, id);
                error = null;
                return true;
            }

            public bool TryGetExpeditionTasks(string mapId, out IReadOnlyList<ExpeditionTaskDefinition> values, out string error)
            {
                if (FailTaskQuery)
                {
                    values = Array.Empty<ExpeditionTaskDefinition>();
                    error = "task config unavailable";
                    return false;
                }
                if (ReturnMultipleTasks && mapId == MapIds.SurvivalSample)
                {
                    values = new[]
                    {
                        new ExpeditionTaskDefinition("expedition.survival-sample.survive", mapId, "存活一天", "task.expedition.survival_sample.survive.name", TaskObjectiveType.Survive, PlayerConditionRules.GameMinutesPerDay, 100),
                        new ExpeditionTaskDefinition("expedition.survival-sample.invalid-extra", mapId, "额外任务", "task.expedition.survival_sample.survive.name", TaskObjectiveType.Survive, 1f, 200)
                    };
                    error = string.Empty;
                    return true;
                }

                values = mapId == MapIds.SurvivalSample
                    ? new[] { new ExpeditionTaskDefinition("expedition.survival-sample.survive", mapId, "存活一天", "task.expedition.survival_sample.survive.name", TaskObjectiveType.Survive, PlayerConditionRules.GameMinutesPerDay, 100) }
                    : mapId == MapIds.MissionSample
                        ? new[] { new ExpeditionTaskDefinition("expedition.mission-sample.kill", mapId, "清除威胁", "task.expedition.mission_sample.kill.name", TaskObjectiveType.Kill, 2f, 100) }
                        : Array.Empty<ExpeditionTaskDefinition>();
                error = string.Empty;
                return true;
            }

            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                var spawns = new List<MapSpawnDefinition>
                {
                    new MapSpawnDefinition("player." + mapId, mapId, MapSpawnObjectType.Player, "player", 0f, 0f, 0f, 0f)
                };
                if (mapId == MapIds.HeJiaVillage)
                {
                    spawns.Add(new MapSpawnDefinition("extraction." + mapId, mapId, MapSpawnObjectType.Extraction,
                        "extraction.task.main", 2f, 0f, 2f, 0f));
                }

                values = spawns;
                error = null;
                return true;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                value = new PlayerConfigDefinition(id, "player.prefab", 100, 10, 1f, 1f, 1f, 1f, 5f);
                error = null;
                return true;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetWeapon(int itemId, out WeaponConfigDefinition value, out string error)
            {
                if (itemId == ItemIds.ShortSword)
                {
                    value = new WeaponConfigDefinition(itemId, WeaponKind.Melee, 25, 1.2f, 1.4f, 90f, string.Empty, 0);
                    error = null;
                    return true;
                }

                if (itemId == ItemIds.Bow)
                {
                    value = new WeaponConfigDefinition(itemId, WeaponKind.Projectile, 18, 0.8f, 6f, 0f, "projectile.arrow.basic", ItemIds.Arrow);
                    error = null;
                    return true;
                }

                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetProjectile(string id, out ProjectileConfigDefinition value, out string error)
            {
                if (id == "projectile.arrow.basic")
                {
                    value = new ProjectileConfigDefinition(id, "Prefabs/Combat/GrayboxArrow", 14f, 14f);
                    error = null;
                    return true;
                }

                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                if (Loots.TryGetValue(id, out value))
                {
                    error = null;
                    return true;
                }

                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
            {
                if (id == "extraction.task.main")
                {
                    value = new ExtractionConfigDefinition(id, "Prefabs/SceneObjects/TaskExit");
                    error = null;
                    return true;
                }

                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetCraftRecipes(out IReadOnlyList<CraftRecipeDefinition> values, out string error)
            {
                values = Array.Empty<CraftRecipeDefinition>();
                error = null;
                return true;
            }
        }
    }
}
