using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using UnityEditor;

namespace Game.Tests
{
    /// <summary>验证河谷斧头采集、材料事务与建筑模式互斥，不进入运行模式。</summary>
    public sealed class CivilisationHarvestTests
    {
        private CivilisationSearchTests support;
        private GameObject ground;
        internal HarvestTestDropPort Drops { get; private set; }
        private const string Tree = "tree.valley.5";
        private static readonly Vector3 TreePosition = new Vector3(16.96f, 0, 15.61f);

        [SetUp] public void SetUp()
        {
            support = new CivilisationSearchTests(); support.SetUp();
            var architecture = (Game)Game.Interface;
            architecture.RegisterModel(new LootModel());
            architecture.RegisterSystem(new LootSystem());
            Drops = new HarvestTestDropPort();
            architecture.RegisterUtility<ICivilisationHarvestDropPort>(Drops);
            architecture.RegisterModel(new TemporaryBuildingModel());
            architecture.RegisterModel(new WeaponEquipmentModel());
            architecture.RegisterModel(new CombatModel());
            architecture.RegisterSystem(new MobileInputSystem());
            architecture.RegisterSystem(new WeaponEquipmentSystem());
            architecture.GetModel<CombatModel>().PlayerHP.Value = 100;
            architecture.RegisterSystem(new TemporaryBuildingSystem());
            Assert.That(architecture.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                1, 1, UnitType.Player, UnitSource.MapPlaced, "player.graybox.default", TreePosition,
                Quaternion.identity, UnitAuthorityMode.Local, 0, true), out var error), Is.True, error);
        }
        [TearDown] public void TearDown()
        {
            if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
            support?.TearDown();
        }
        internal static void Equip(int weapon = 17, string slot = "Melee")
        {
            if (slot == "Ranged") weapon = 6;
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            if (weapon > 0 && !bag.Slots.Any(p => p.Value.ItemId == weapon))
                Assert.That(bag.TryAdd(weapon, 1, 1, out _, out _), Is.True);
            var index = weapon == 0 ? -1 : bag.Slots.First(p => p.Value.ItemId == weapon).Key;
            Game.Interface.GetModel<WeaponEquipmentModel>().Restore(new JObject
            {
                ["schemaVersion"] = 1, ["sourceContainerKind"] = weapon == 0 ? -1 : (int)InventoryContainerKind.PlayerBag,
                ["sourceSlotIndex"] = index, ["itemId"] = weapon
            });
        }
        private static bool Begin(out string error) => Game.Interface.GetSystem<CivilisationSearchSystem>().TryBeginSearch(1, Tree, out error);
        private static void Complete() => Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(3.1f);
        private static int Wood => Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(9);

        [Test] public void EquippedAxeGrantsWoodOnceAndPersistsHarvest()
        {
            Equip(); Assert.That(Begin(out var error), Is.True, error); Complete();
            Assert.That(Wood, Is.Zero, "采集产物应落地，不能直接进入背包");
            Assert.That(Drops.Amount(9), Is.EqualTo(4));
            Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited(Tree), Is.True);
            Assert.That(Game.Interface.GetSystem<SaveSystem>().InspectExpedition().Data.Sections[
                CivilisationExpeditionModel.Section]["visited"].Values<string>(), Does.Contain(Tree));
            Assert.That(Begin(out _), Is.False); Complete(); Assert.That(Drops.Amount(9), Is.EqualTo(4));
        }
        [Test] public void AxeInBagOrInactiveSlotDoesNotPermitChopping()
        {
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.TryAdd(17, 1, 1, out _, out _), Is.True);
            Assert.That(Begin(out _), Is.False);
            Equip(17, "Ranged"); Assert.That(Begin(out _), Is.False);
            Equip(5); Assert.That(Begin(out _), Is.False); Assert.That(Wood, Is.Zero);
        }
        [Test] public void UnequippingAxeBeforeCompletionDoesNotHarvest()
        {
            Equip(); Assert.That(Begin(out var error), Is.True, error); Equip(0); Complete();
            Assert.That(Wood, Is.Zero); Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited(Tree), Is.False);
        }
        [Test] public void InterruptedChopLeavesTreeAvailable()
        {
            Equip(); Assert.That(Begin(out var error), Is.True, error);
            Game.Interface.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.Damaged); Complete();
            Assert.That(Wood, Is.Zero); Assert.That(Begin(out error), Is.True, error); Complete(); Assert.That(Drops.Amount(9), Is.EqualTo(4));
        }
        [Test] public void MovingAwayBeforeCompletionDoesNotHarvest()
        {
            Equip(); Assert.That(Begin(out var error), Is.True, error);
            Game.Interface.GetSystem<UnitSystem>().TryUpdateTransform(1, Vector3.zero, Quaternion.identity); Complete();
            Assert.That(Wood, Is.Zero); Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited(Tree), Is.False);
        }
        [Test] public void FullBagStillHarvestsTreeAndLeavesRewardOnGround()
        {
            Equip(); var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(5, bag.Capacity - 1, 1, out _, out _), Is.True);
            Assert.That(Begin(out var error), Is.True, error); Complete();
            Assert.That(Wood, Is.Zero); Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited(Tree), Is.True);
            Assert.That(Drops.Amount(9), Is.EqualTo(4));
            Assert.That(Game.Interface.GetSystem<LootSystem>().TryCollect(Drops.LastUnitId, 1), Is.False);
            Assert.That(Drops.Amount(9), Is.EqualTo(4));
        }
        [Test] public void EnteringDestroyModeCancelsChopAndBlocksNewChop()
        {
            Equip(); Assert.That(Begin(out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<TemporaryBuildingSystem>().BeginDestroyMode(out error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<PlayerActionSystem>().TryGetSnapshot(out _), Is.False);
            Assert.That(Begin(out _), Is.False); Complete(); Assert.That(Wood, Is.Zero);
            Game.Interface.GetSystem<TemporaryBuildingSystem>().CancelCurrentMode();
            Assert.That(Begin(out error), Is.True, error);
        }
        [Test] public void OnlyWoodShedIsAllowedInOfflineValley()
        {
            var config = Game.Interface.GetUtility<IRuntimeConfigProvider>();
            Assert.That(config.TryGetTempBuilding("camp_shed_wood_01", out var shed, out var error), Is.True, error);
            Assert.That(config.TryGetTempBuilding("camp_awning_standing_01a", out var awning, out error), Is.True, error);
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            Assert.That(system.CanBuildDefinition(shed), Is.True);
            Assert.That(system.CanBuildDefinition(awning), Is.False);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            Assert.That(system.CanBuildDefinition(shed), Is.False);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.CivilisationValley, null, MapPreparationMode.OnlinePlayersOnly);
            Assert.That(system.CanBuildDefinition(shed), Is.False);
        }
        [Test] public void SaveFailureRestoresWoodAndLeavesTreeAvailable()
        {
            Equip(); var root = Path.Combine(Path.GetTempPath(), "AnotherDayHarvestFailure", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); var blocked = Path.Combine(root, "blocked"); File.WriteAllText(blocked, "block");
            try
            {
                Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(Path.Combine(blocked, "child"));
                Assert.That(Begin(out var error), Is.True, error); Complete();
                Assert.That(Wood, Is.Zero); Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited(Tree), Is.False);
                Assert.That(Game.Interface.GetModel<LootModel>().Loot, Is.Empty);
                Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(Drops.LastUnitId, out _), Is.False);
            }
            finally { Directory.Delete(root, true); }
        }
        [Test] public void EnteringBuildPreviewCancelsChop()
        {
            Equip(); Assert.That(Begin(out var error), Is.True, error);
            ((Game)Game.Interface).RegisterUtility<ITemporaryBuildingViewPort>(new FakeTemporaryBuildingViewPort());
            Assert.That(Game.Interface.GetSystem<TemporaryBuildingSystem>().BeginBuildPreview("camp_shed_wood_01", out error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<PlayerActionSystem>().TryGetSnapshot(out _), Is.False);
            Assert.That(Begin(out _), Is.False); Complete(); Assert.That(Wood, Is.Zero);
        }
        [Test] public void CollectedMaterialsBuildShedAndDestroyReturnsExactCostsOnce()
        {
            var architecture = (Game)Game.Interface;
            var search = architecture.GetSystem<CivilisationSearchSystem>();
            void Gather(string id, Vector3 position)
            {
                architecture.GetSystem<UnitSystem>().TryUpdateTransform(1, position, Quaternion.identity);
                Assert.That(search.TryBeginSearch(1, id, out var error), Is.True, error); Complete();
                if (id.StartsWith("tree.")) Assert.That(architecture.GetSystem<LootSystem>().TryCollect(Drops.LastUnitId, 1), Is.True);
            }
            Gather("camp.axe", new Vector3(6, 0, 6));
            var axeSlot = architecture.GetModel<PlayerBagModel>().Container.Slots.Single(p => p.Value.ItemId == 17).Key;
            Assert.That(architecture.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(axeSlot, out var equipError), Is.True, equipError);
            Gather(Tree, TreePosition); Gather("tree.valley.6", new Vector3(18.58f, 0, 12.14f));
            Gather("tree.valley.34", new Vector3(-12.36f, 0, 15.88f)); Gather("camp.cloth", new Vector3(1, 0, 6));
            Assert.That(Wood, Is.EqualTo(12)); Assert.That(architecture.GetModel<PlayerBagModel>().Container.GetTotalAmount(2), Is.EqualTo(2));
            var port = new FakeTemporaryBuildingViewPort(); var refunds = new FakeBuildingRefundDropPort();
            architecture.RegisterUtility<ITemporaryBuildingViewPort>(port); architecture.RegisterUtility<IBuildingRefundDropPort>(refunds);
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "Ground";
            // 工具可能保留正式场景的碰撞体；把测试地面放到独立坐标，避免河床污染领域事务测试。
            ground.transform.position = new Vector3(1000, -.5f, 1000); ground.transform.localScale = new Vector3(40, 1, 40); Physics.SyncTransforms();
            architecture.GetSystem<UnitSystem>().TryUpdateTransform(1, new Vector3(1000, 0, 1000), Quaternion.identity);
            var buildings = architecture.GetSystem<TemporaryBuildingSystem>();
            Assert.That(buildings.BeginBuildPreview("camp_shed_wood_01", out var error), Is.True, error);
            Assert.That(buildings.UpdateBuildPreview(new Vector3(1002, 0, 1000), Quaternion.identity), Is.True);
            Assert.That(buildings.ConfirmBuild(out error), Is.True, error); Complete();
            var model = architecture.GetModel<TemporaryBuildingModel>();
            Assert.That(model.Buildings.Count, Is.EqualTo(1)); Assert.That(Wood, Is.Zero);
            Assert.That(architecture.GetModel<PlayerBagModel>().Container.GetTotalAmount(2), Is.Zero);
            port.PickedRuntimeId = model.Buildings.Keys.Single();
            Assert.That(buildings.BeginDestroyMode(out error), Is.True, error);
            buildings.RefreshDestroyTarget(new Ray(Vector3.up, Vector3.forward));
            Assert.That(buildings.ConfirmDestroy(out error), Is.True, error);
            Assert.That(model.Buildings, Is.Empty);
            Assert.That(refunds.LastDrops.Single(d => d.ItemId == 9).Amount, Is.EqualTo(12));
            Assert.That(refunds.LastDrops.Single(d => d.ItemId == 2).Amount, Is.EqualTo(2));
            Assert.That(buildings.ConfirmDestroy(out _), Is.False);
            Assert.That(refunds.CreateCount, Is.EqualTo(1));
        }
        [Test] public void SavedShedCanResumeSameValleyJourneyButNotOtherMaps()
        {
            var architecture = (Game)Game.Interface;
            architecture.RegisterUtility<IViewInstanceProvider>(new UnityViewInstanceProvider());
            ResourceLoader.SetLoaderForTests(new BuildingAssetLoader());
            var handle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.CivilisationValley, 1);
            var controller = new TemporaryBuildingCtrl(architecture);
            var journey = new ExpeditionSaveData { MapId = MapIds.CivilisationValley };
            journey.World.TemporaryBuildings.Add(new ExpeditionTemporaryBuildingSaveData
            { RuntimeId = 42, ConfigId = "camp_shed_wood_01", Pose = new ExpeditionPoseData { X = 0, Y = 0, Z = 15 }, UniformScale = 1 });
            var method = typeof(TemporaryBuildingCtrl).GetMethod("PrepareMap", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                var context = new MapPreparationContext(MapIds.CivilisationValley, 1, handle.Context, Array.Empty<MapSpawnDefinition>(), journey);
                var result = (MapControllerResult)method.Invoke(controller, new object[] { context });
                Assert.That(result.Success, Is.True, result.Error);
                var other = new MapPreparationContext(MapIds.SurvivalSample, 2, handle.Context, Array.Empty<MapSpawnDefinition>(), journey);
                result = (MapControllerResult)method.Invoke(controller, new object[] { other });
                Assert.That(result.Success, Is.False);
            }
            finally { controller.ReleaseAll(); UnityEngine.Object.DestroyImmediate(handle.Root); }
        }
        private sealed class BuildingAssetLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>("Assets/Game/" + path + ".prefab");
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
            { onLoaded?.Invoke(LoadAsset<T>(path)); return ResourceLoadHandle.Empty; }
            public void Release(ResourceLoadHandle handle) { }
        }

        [Test] public void DropFailureLeavesResourceAvailableAndRetryCreatesOnce()
        {
            Equip(); Drops.Fail = true;
            Assert.That(Begin(out _), Is.True); Complete();
            Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited(Tree), Is.False);
            Assert.That(Game.Interface.GetModel<LootModel>().Loot, Is.Empty);
            Drops.Fail = false;
            Assert.That(Begin(out _), Is.True); Complete();
            Assert.That(Drops.Amount(9), Is.EqualTo(4));
            Assert.That(Begin(out _), Is.False);
        }

        [Test] public void HarvestRewardCanBePickedUpPartiallyAndRemainingLootSurvives()
        {
            Equip(); var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(Game.Interface.GetUtility<IRuntimeConfigProvider>().TryGetItem(9, out var wood, out _), Is.True);
            Assert.That(bag.TryAdd(9, wood.MaxStackAmount - 2, wood.MaxStackAmount, out _, out _), Is.True);
            Assert.That(bag.TryAdd(5, bag.Capacity - 2, 1, out _, out _), Is.True);
            Assert.That(Begin(out _), Is.True); Complete();
            Assert.That(Game.Interface.GetSystem<LootSystem>().TryCollect(Drops.LastUnitId, 1), Is.True);
            Assert.That(Wood, Is.EqualTo(wood.MaxStackAmount));
            Assert.That(Drops.Amount(9), Is.EqualTo(2));
            Assert.That(Game.Interface.GetSystem<LootSystem>().TryCollect(Drops.LastUnitId, 1), Is.False);
            Assert.That(Drops.Amount(9), Is.EqualTo(2));
        }
    }

    /// <summary>隔离 Unity 表现的测试端口；产物仍使用真实 Unit/Loot 注册和拾取规则。</summary>
    internal sealed class HarvestTestDropPort : ICivilisationHarvestDropPort
    {
        public bool Fail;
        public int LastUnitId;
        public int Amount(int item) => Game.Interface.GetModel<LootModel>().Loot.Values.Where(x => x.ItemId == item && !x.Collected).Sum(x => x.Amount);
        public bool TryCreateHarvestDrop(long map, Vector3 position, int item, int amount,
            out IReadOnlyList<int> ids, out string error)
        {
            ids = Array.Empty<int>(); error = "测试掉落失败";
            if (Fail) return false;
            var arch = Game.Interface;
            if (!arch.GetUtility<IRuntimeConfigProvider>().TryGetLootByItemId(item, out var config, out error)) return false;
            LastUnitId = arch.GetSystem<UnitSystem>().AllocateUnitId();
            if (!arch.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(LastUnitId, map, UnitType.Loot,
                UnitSource.RuntimeSpawned, config.Id, position, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out error)) return false;
            if (!arch.GetSystem<LootSystem>().TryRegisterLoot(LastUnitId,
                new LootConfigDefinition(config.Id, config.PrefabAddress, item, amount), out error))
            { arch.GetSystem<UnitSystem>().Remove(LastUnitId, UnitRemovedReason.Explicit); return false; }
            ids = new[] { LastUnitId }; return true;
        }
        public void RollbackHarvestDrop(IReadOnlyList<int> ids)
        {
            foreach (var id in ids ?? Array.Empty<int>())
            {
                Game.Interface.GetSystem<LootSystem>().TryRemoveLoot(id);
                Game.Interface.GetSystem<UnitSystem>().Remove(id, UnitRemovedReason.Explicit);
            }
        }
    }
}
