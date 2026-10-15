using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class BaseBuildingPersistenceAndRefundTests
    {
        [Test]
        public void BaseWorldClone_IsIndependentAndPersistsOnlyLootAndBuildings()
        {
            var baseData = new BaseSaveData
            {
                World = new BaseWorldSaveData
                {
                    Loot = new List<ExpeditionLootSaveData>
                    {
                        new ExpeditionLootSaveData { RuntimeId = 10, ConfigId = "loot.wood", Pose = new ExpeditionPoseData { X = 1 }, RemainingAmount = 3 }
                    },
                    TemporaryBuildings = new List<ExpeditionTemporaryBuildingSaveData>
                    {
                        new ExpeditionTemporaryBuildingSaveData { RuntimeId = 11, ConfigId = "building.arrow-tower", Pose = new ExpeditionPoseData { RotationY = 90 }, LoadedAmmo = 4 }
                    }
                }
            };
            var clone = baseData.Clone();
            clone.World.Loot[0].RemainingAmount = 1;
            clone.World.TemporaryBuildings[0].LoadedAmmo = 0;
            Assert.That(baseData.World.Loot[0].RemainingAmount, Is.EqualTo(3));
            Assert.That(baseData.World.TemporaryBuildings[0].LoadedAmmo, Is.EqualTo(4));
        }

        [Test]
        public void RefundContract_KeepsGroundDropAmountsAndDeterministicOffsets()
        {
            var drop = new BuildingRefundDrop(ItemIds.Arrow, 7, new Vector3(.35f, 0f, .15f));
            var receipt = new BuildingRefundDropReceipt(new[] { 12, 13 });
            Assert.That(drop.ItemId, Is.EqualTo(ItemIds.Arrow));
            Assert.That(drop.Amount, Is.EqualTo(7));
            Assert.That(drop.Offset, Is.EqualTo(new Vector3(.35f, 0f, .15f)));
            Assert.That(receipt.LootUnitIds, Is.EqualTo(new[] { 12, 13 }));
        }

        [Test]
        public void BaseBuildAndDestroy_UsesPublicSystemFlowAndReturnsConfiguredMaterials()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            GameObject ground = null;
            try
            {
                var config = new TestRuntimeConfigProvider();
                var view = new FakeTemporaryBuildingViewPort();
                TemporaryBuildingBehaviorTestSupport.InstallArchitecture(config, view, new FakeProjectilePort());
                ground = TemporaryBuildingBehaviorTestSupport.CreateGround();
                TemporaryBuildingBehaviorTestSupport.EnterBase();
                var refund = Game.Interface.GetUtility<IBuildingRefundDropPort>() as FakeBuildingRefundDropPort;

                foreach (var item in new[]
                         {
                             ("temp_arrow_tower", new Vector3(2f, 0f, 0f), 2, 3),
                             ("temp_roadblock_wood", new Vector3(-2f, 0f, 0f), 0, 3),
                             ("temp_campfire", new Vector3(0f, 0f, 2f), 0, 2)
                         })
                {
                    if (item.Item3 > 0) TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.MetalParts, item.Item3);
                    TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, item.Item4);
                    var id = TemporaryBuildingBehaviorTestSupport.Build(item.Item1, item.Item2);
                    Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings.ContainsKey(id), Is.True, item.Item1);
                    Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(0), "材料必须经实际建造扣除");
                    Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Wood), Is.EqualTo(0), "材料必须经实际建造扣除");
                    Assert.That(config.TryGetTempBuilding(item.Item1, out var definition, out var definitionError), Is.True, definitionError);
                    if (item.Item1 == "temp_arrow_tower")
                    {
                        Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().TrySetManualAttack(id,
                            new TemporaryManualAttackRuntimeState(0f, 4)), Is.True);
                    }
                    TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Arrow, 240);
                    Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.Slots.Count,
                        Is.EqualTo(Game.Interface.GetModel<PlayerBagModel>().Container.Capacity));

                    Assert.That(Game.Interface.GetSystem<TemporaryBuildingSystem>().BeginDestroyMode(out var error), Is.True, error);
                    view.PickedRuntimeId = id;
                    Game.Interface.GetSystem<TemporaryBuildingSystem>().RefreshDestroyTarget(new Ray(Vector3.up * 4f, Vector3.down));
                    Assert.That(Game.Interface.GetSystem<TemporaryBuildingSystem>().ConfirmDestroy(out error), Is.True, error);
                    Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings.ContainsKey(id), Is.False);
                    Assert.That(refund.LastMapInstanceId, Is.EqualTo(TemporaryBuildingBehaviorTestSupport.MapInstanceId));
                    Assert.That(refund.LastDrops.Select(drop => (drop.ItemId, drop.Amount)),
                        Is.EquivalentTo(definition.Materials.Select(material => (material.ItemId, material.Amount))
                            .Concat(item.Item1 == "temp_arrow_tower" ? new[] { (ItemIds.Arrow, 4) } : Array.Empty<(int, int)>())));
                    Assert.That(refund.LastDrops.Select(drop => drop.Offset), Is.EqualTo(new[]
                    {
                        new Vector3(0.35f, 0f, 0.15f),
                        new Vector3(-0.3f, 0f, 0.22f),
                        new Vector3(0f, 0f, -0.35f)
                    }.Take(refund.LastDrops.Count)));
                    Assert.That(refund.LastPosition, Is.EqualTo(item.Item2));
                    Assert.That(refund.LastRotation, Is.EqualTo(Quaternion.identity));
                    Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Mode, Is.EqualTo(TemporaryBuildingMode.DestroyMode));
                    Game.Interface.GetSystem<TemporaryBuildingSystem>().CancelCurrentMode();
                    Game.Interface.GetSystem<InventorySystem>().ClearPlayerBag();
                }
            }
            finally
            {
                if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
                TemporaryBuildingBehaviorTestSupport.Reset();
            }
        }

        [Test]
        public void DestroyRefundFailure_KeepsBuildingAndTargetWithoutRollbackSideEffects()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            GameObject ground = null;
            try
            {
                var config = new TestRuntimeConfigProvider();
                var view = new FakeTemporaryBuildingViewPort();
                TemporaryBuildingBehaviorTestSupport.InstallArchitecture(config, view, new FakeProjectilePort());
                ground = TemporaryBuildingBehaviorTestSupport.CreateGround();
                TemporaryBuildingBehaviorTestSupport.EnterBase();
                TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
                var id = TemporaryBuildingBehaviorTestSupport.Build("temp_roadblock_wood", Vector3.right * 2f);
                var refund = Game.Interface.GetUtility<IBuildingRefundDropPort>() as FakeBuildingRefundDropPort;
                refund.CreateSucceeds = false;

                var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
                Assert.That(system.BeginDestroyMode(out var error), Is.True, error);
                view.PickedRuntimeId = id;
                system.RefreshDestroyTarget(new Ray(Vector3.up * 4f, Vector3.down));
                Assert.That(system.ConfirmDestroy(out error), Is.False);
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings.ContainsKey(id), Is.True);
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().DestroyTargetBuildingId, Is.EqualTo(id));
                Assert.That(view.BuildingReleaseCount, Is.Zero);
                Assert.That(refund.CreateCount, Is.EqualTo(1));
                Assert.That(refund.RollbackCount, Is.Zero);
            }
            finally
            {
                if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
                TemporaryBuildingBehaviorTestSupport.Reset();
            }
        }

        [Test]
        public void DestroyWithoutRefundPortOrWithExpiredMap_KeepsTheBuildingAndTarget()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            GameObject ground = null;
            try
            {
                var config = new TestRuntimeConfigProvider();
                var view = new FakeTemporaryBuildingViewPort();
                TemporaryBuildingBehaviorTestSupport.InstallArchitecture(config, view, new FakeProjectilePort(), registerRefundPort: false);
                ground = TemporaryBuildingBehaviorTestSupport.CreateGround();
                TemporaryBuildingBehaviorTestSupport.EnterBase();
                TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
                var id = TemporaryBuildingBehaviorTestSupport.Build("temp_roadblock_wood", Vector3.right);
                var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
                Assert.That(system.BeginDestroyMode(out var error), Is.True, error);
                view.PickedRuntimeId = id;
                system.RefreshDestroyTarget(new Ray(Vector3.up * 4f, Vector3.down));
                Assert.That(system.ConfirmDestroy(out error), Is.False);
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings.ContainsKey(id), Is.True);
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().DestroyTargetBuildingId, Is.EqualTo(id));
            }
            finally
            {
                if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
                TemporaryBuildingBehaviorTestSupport.Reset();
            }
        }

        [Test]
        public void DestroyAfterRefundCreationAndModelRemovalFailure_RollsBackOnceWithoutClearingViewOrMode()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            GameObject ground = null;
            try
            {
                var config = new TestRuntimeConfigProvider();
                var view = new FakeTemporaryBuildingViewPort();
                var refund = new FakeBuildingRefundDropPort();
                TemporaryBuildingBehaviorTestSupport.InstallArchitecture(config, view, new FakeProjectilePort(), refundPort: refund);
                ground = TemporaryBuildingBehaviorTestSupport.CreateGround();
                TemporaryBuildingBehaviorTestSupport.EnterBase();
                TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
                var id = TemporaryBuildingBehaviorTestSupport.Build("temp_roadblock_wood", Vector3.right);
                refund.AfterSuccessfulCreate = () => Game.Interface.GetModel<TemporaryBuildingModel>().RemoveBuilding(id);
                var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
                Assert.That(system.BeginDestroyMode(out var error), Is.True, error);
                view.PickedRuntimeId = id;
                system.RefreshDestroyTarget(new Ray(Vector3.up * 4f, Vector3.down));
                Assert.That(system.ConfirmDestroy(out error), Is.False);
                Assert.That(error, Does.Contain("移除失败"));
                Assert.That(refund.CreateCount, Is.EqualTo(1));
                Assert.That(refund.RollbackCount, Is.EqualTo(1));
                Assert.That(view.BuildingReleaseCount, Is.Zero);
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().DestroyTargetBuildingId, Is.EqualTo(id));
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Mode, Is.EqualTo(TemporaryBuildingMode.DestroyMode));
                Assert.That(system.ConfirmDestroy(out _), Is.False);
                Assert.That(refund.CreateCount, Is.EqualTo(1));
            }
            finally
            {
                if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
                TemporaryBuildingBehaviorTestSupport.Reset();
            }
        }

        [Test]
        public void BaseRefund_UsesRealLootCtrlForGroundPickupAndCapture()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            GameObject ground = null;
            FakeMapHandle mapHandle = null;
            try
            {
                var config = new TestRuntimeConfigProvider();
                var view = new FakeTemporaryBuildingViewPort();
                TemporaryBuildingBehaviorTestSupport.InstallArchitecture(config, view, new FakeProjectilePort(), useRealLoot: true);
                ground = TemporaryBuildingBehaviorTestSupport.CreateGround();
                TemporaryBuildingBehaviorTestSupport.EnterBase();
                mapHandle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.Base, TemporaryBuildingBehaviorTestSupport.MapInstanceId);
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, mapHandle);
                var loot = (LootCtrl)Game.Interface.GetUtility<IBuildingRefundDropPort>();
                var prepared = loot.PrepareMapAsync(new MapPreparationContext(
                    MapIds.Base, TemporaryBuildingBehaviorTestSupport.MapInstanceId, mapHandle.Context,
                    Array.Empty<MapSpawnDefinition>()), CancellationToken.None).GetAwaiter().GetResult();
                Assert.That(prepared.Success, Is.True, prepared.Error);
                Assert.That(loot.CommitPreparedMap().Success, Is.True);
                Game.Interface.RegisterSystem(new MapSceneSystem(null, null, null, null, loot, null));

                TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
                var id = TemporaryBuildingBehaviorTestSupport.Build("temp_roadblock_wood", Vector3.right);
                var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
                Assert.That(system.BeginDestroyMode(out var error), Is.True, error);
                view.PickedRuntimeId = id;
                system.RefreshDestroyTarget(new Ray(Vector3.up * 4f, Vector3.down));
                Assert.That(system.ConfirmDestroy(out error), Is.True, error);
                Assert.That(Game.Interface.GetModel<LootModel>().Loot.Count, Is.EqualTo(1));
                var lootId = Game.Interface.GetModel<LootModel>().Loot.Keys.Single();
                Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(lootId, out var lootUnit), Is.True);
                var lootPosition = lootUnit.Position;
                var lootRotation = lootUnit.Rotation;
                TemporaryBuildingBehaviorTestSupport.MovePlayer(lootUnit.Position);
                Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var movedPlayer), Is.True);
                Assert.That(movedPlayer.MapInstanceId, Is.EqualTo(lootUnit.MapInstanceId));
                Assert.That((movedPlayer.Position - lootUnit.Position).sqrMagnitude,
                    Is.LessThanOrEqualTo(LootSystem.CollectRange * LootSystem.CollectRange));
                Assert.That(Game.Interface.GetSystem<LootSystem>().TryCollect(lootId, PlayerModel.LocalPlayerUnitId), Is.True);
                Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Wood), Is.EqualTo(3));
                Assert.That(Game.Interface.GetModel<LootModel>().Loot.Count, Is.Zero);

                var save = Game.Interface.GetSystem<SaveSystem>();
                var captured = new BaseSaveData();
                Assert.That(save.CaptureBaseSections(captured, out error), Is.True, error);
                Assert.That(captured.World.Loot, Has.Count.EqualTo(1));
                var capturedLoot = captured.World.Loot.Single();
                Assert.That(capturedLoot.RuntimeId, Is.EqualTo(lootId));
                Assert.That(capturedLoot.ConfigId, Is.EqualTo("loot.wood"));
                Assert.That(capturedLoot.Collected, Is.True);
                Assert.That(capturedLoot.RemainingAmount, Is.Zero);
                Assert.That(capturedLoot.Pose.X, Is.EqualTo(lootPosition.x).Within(0.0001f));
                Assert.That(capturedLoot.Pose.Y, Is.EqualTo(lootPosition.y).Within(0.0001f));
                Assert.That(capturedLoot.Pose.Z, Is.EqualTo(lootPosition.z).Within(0.0001f));
                Assert.That(capturedLoot.Pose.RotationY, Is.EqualTo(lootRotation.eulerAngles.y).Within(0.0001f));
                Assert.That(captured.World.TemporaryBuildings, Is.Empty);
            }
            finally
            {
                if (mapHandle != null && mapHandle.Root != null) UnityEngine.Object.DestroyImmediate(mapHandle.Root);
                if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
                TemporaryBuildingBehaviorTestSupport.Reset();
            }
        }

        [Test]
        public void BaseRefund_PartialRealLootPickupCapturesRemainingAmountAndPose()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            GameObject ground = null;
            FakeMapHandle mapHandle = null;
            try
            {
                var config = new TestRuntimeConfigProvider();
                var view = new FakeTemporaryBuildingViewPort();
                TemporaryBuildingBehaviorTestSupport.InstallArchitecture(config, view, new FakeProjectilePort(), useRealLoot: true);
                ground = TemporaryBuildingBehaviorTestSupport.CreateGround();
                TemporaryBuildingBehaviorTestSupport.EnterBase();
                mapHandle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.Base, TemporaryBuildingBehaviorTestSupport.MapInstanceId);
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, mapHandle);
                var loot = (LootCtrl)Game.Interface.GetUtility<IBuildingRefundDropPort>();
                var prepared = loot.PrepareMapAsync(new MapPreparationContext(MapIds.Base,
                    TemporaryBuildingBehaviorTestSupport.MapInstanceId, mapHandle.Context,
                    Array.Empty<MapSpawnDefinition>()), CancellationToken.None).GetAwaiter().GetResult();
                Assert.That(prepared.Success, Is.True, prepared.Error);
                Assert.That(loot.CommitPreparedMap().Success, Is.True);
                Game.Interface.RegisterSystem(new MapSceneSystem(null, null, null, null, loot, null));

                TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.MetalParts, 2);
                TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
                var id = TemporaryBuildingBehaviorTestSupport.Build("temp_arrow_tower", Vector3.right);
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().TrySetManualAttack(id,
                    new TemporaryManualAttackRuntimeState(0f, 4)), Is.True);
                TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Arrow, 19);
                Assert.That(config.TryGetItem(ItemIds.Arrow, out var arrowDefinition, out var arrowError), Is.True, arrowError);
                Assert.That(arrowDefinition.MaxStackAmount, Is.EqualTo(20));
                TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, arrowDefinition.MaxStackAmount * 11);
                var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
                Assert.That(bag.Slots.Count, Is.EqualTo(bag.Capacity));
                Assert.That(bag.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(19));
                var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
                Assert.That(system.BeginDestroyMode(out var error), Is.True, error);
                view.PickedRuntimeId = id;
                system.RefreshDestroyTarget(new Ray(Vector3.up * 4f, Vector3.down));
                Assert.That(system.ConfirmDestroy(out error), Is.True, error);

                var arrowLoot = Game.Interface.GetModel<LootModel>().Loot.Values.Single(item => item.ItemId == ItemIds.Arrow);
                var arrowLootId = arrowLoot.UnitId;
                Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(arrowLootId, out var arrowUnit), Is.True);
                var arrowPosition = arrowUnit.Position;
                var arrowRotation = arrowUnit.Rotation;
                TemporaryBuildingBehaviorTestSupport.MovePlayer(arrowPosition);
                Assert.That(Game.Interface.GetSystem<LootSystem>().TryCollect(arrowLootId, PlayerModel.LocalPlayerUnitId), Is.True);
                Assert.That(Game.Interface.GetModel<LootModel>().TryGet(arrowLootId, out var remaining), Is.True);
                Assert.That(remaining.Amount, Is.GreaterThan(0));
                Assert.That(remaining.Amount, Is.LessThan(4));

                var captured = new BaseSaveData();
                Assert.That(Game.Interface.GetSystem<SaveSystem>().CaptureBaseSections(captured, out error), Is.True, error);
                var capturedLoot = captured.World.Loot.Single(item => item.RuntimeId == arrowLootId);
                Assert.That(capturedLoot.ConfigId, Is.EqualTo("loot.arrow"));
                Assert.That(capturedLoot.Collected, Is.False);
                Assert.That(capturedLoot.RemainingAmount, Is.EqualTo(remaining.Amount));
                Assert.That(capturedLoot.Pose.X, Is.EqualTo(arrowPosition.x).Within(0.0001f));
                Assert.That(capturedLoot.Pose.Y, Is.EqualTo(arrowPosition.y).Within(0.0001f));
                Assert.That(capturedLoot.Pose.Z, Is.EqualTo(arrowPosition.z).Within(0.0001f));
                Assert.That(capturedLoot.Pose.RotationY, Is.EqualTo(arrowRotation.eulerAngles.y).Within(0.0001f));
            }
            finally
            {
                if (mapHandle != null && mapHandle.Root != null) UnityEngine.Object.DestroyImmediate(mapHandle.Root);
                if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
                TemporaryBuildingBehaviorTestSupport.Reset();
            }
        }

        [Test]
        public void BaseSnapshot_LoadsThroughRealMapControllersAndRestoresLootBuildingsAndAmmo()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            GameObject ground = null;
            GameObject cameraObject = null;
            FakeMapContentLoader loader = null;
            try
            {
                var config = new TestRuntimeConfigProvider(includeMapPlayerSpawn: true);
                var fakeView = new FakeTemporaryBuildingViewPort();
                TemporaryBuildingBehaviorTestSupport.InstallArchitecture(config, fakeView, new FakeProjectilePort(), useRealLoot: true, useRealBuilding: true);
                ground = TemporaryBuildingBehaviorTestSupport.CreateGround();
                cameraObject = new GameObject("Main Camera", typeof(Camera));
                cameraObject.tag = "MainCamera";
                Assert.That(config.TryGetMapSpawns(MapIds.Base, out var spawns, out var spawnError), Is.True, spawnError);
                Assert.That(spawns.Count(item => item.ObjectType == MapSpawnObjectType.Player), Is.EqualTo(1));
                Assert.That(spawns.Any(item => item.ObjectType == MapSpawnObjectType.Npc ||
                                               item.ObjectType == MapSpawnObjectType.Enemy ||
                                               item.ObjectType == MapSpawnObjectType.Extraction), Is.False);
                var loot = (LootCtrl)Game.Interface.GetUtility<IBuildingRefundDropPort>();
                loader = new FakeMapContentLoader();
                var map = TemporaryBuildingBehaviorTestSupport.RegisterMapSystem(loader, loot);
                var snapshot = new BaseSaveData
                {
                    World = new BaseWorldSaveData
                    {
                        Loot = new List<ExpeditionLootSaveData>
                        {
                            new ExpeditionLootSaveData
                            {
                                RuntimeId = 51, ConfigId = "loot.wood", RemainingAmount = 3,
                                Pose = new ExpeditionPoseData { X = 1f, Y = 0f, Z = 1f, RotationY = 15f }
                            }
                        },
                        TemporaryBuildings = new List<ExpeditionTemporaryBuildingSaveData>
                        {
                            new ExpeditionTemporaryBuildingSaveData
                            {
                                RuntimeId = 41, ConfigId = "temp_roadblock_wood",
                                Pose = new ExpeditionPoseData { X = -1f, Y = 0f, Z = 1f, RotationY = 20f }
                            },
                            new ExpeditionTemporaryBuildingSaveData
                            {
                                RuntimeId = 42, ConfigId = "temp_arrow_tower", LoadedAmmo = 4,
                                Pose = new ExpeditionPoseData { X = 2f, Y = 0f, Z = 1f, RotationY = 35f }
                            }
                        }
                    }
                };
                var result = map.LoadBaseMapAsync(snapshot).GetAwaiter().GetResult();
                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(Game.Interface.GetModel<LootModel>().TryGet(51, out var lootState), Is.True);
                Assert.That(lootState.Amount, Is.EqualTo(3));
                Assert.That(lootState.MapInstanceId, Is.EqualTo(map.CurrentMapInstanceId));
                var buildings = Game.Interface.GetModel<TemporaryBuildingModel>().Buildings;
                Assert.That(buildings.Keys, Is.EquivalentTo(new[] { 41, 42 }));
                Assert.That(buildings[41].ConfigId, Is.EqualTo("temp_roadblock_wood"));
                Assert.That(buildings[42].Position, Is.EqualTo(new Vector3(2f, 0f, 1f)));
                Assert.That(buildings[42].Rotation.eulerAngles.y, Is.EqualTo(35f).Within(0.001f));
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().ManualAttacks[42].LoadedAmmo, Is.EqualTo(4));
                Assert.That(Game.Interface.GetUtility<ITemporaryBuildingViewPort>(), Is.TypeOf<TemporaryBuildingCtrl>());
            }
            finally
            {
                // Test Runner 复用未保存场景；显式释放本用例加载的根，避免玩家胶囊污染后续放置检查。
                if (loader != null)
                    foreach (var handle in loader.LoadedHandles)
                        if (handle.Root != null) UnityEngine.Object.DestroyImmediate(handle.Root);
                if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
                if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
                TemporaryBuildingBehaviorTestSupport.Reset();
            }
        }

        [Test]
        public void BaseSnapshot_InvalidBuildingRollsBackAllPreparedRealControllers()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            GameObject ground = null;
            GameObject cameraObject = null;
            FakeMapContentLoader loader = null;
            try
            {
                var config = new TestRuntimeConfigProvider(includeMapPlayerSpawn: true);
                var fakeView = new FakeTemporaryBuildingViewPort();
                TemporaryBuildingBehaviorTestSupport.InstallArchitecture(config, fakeView, new FakeProjectilePort(), useRealLoot: true, useRealBuilding: true);
                ground = TemporaryBuildingBehaviorTestSupport.CreateGround();
                cameraObject = new GameObject("Main Camera", typeof(Camera));
                cameraObject.tag = "MainCamera";
                loader = new FakeMapContentLoader();
                var loot = (LootCtrl)Game.Interface.GetUtility<IBuildingRefundDropPort>();
                var map = TemporaryBuildingBehaviorTestSupport.RegisterMapSystem(loader, loot);
                var snapshot = new BaseSaveData
                {
                    World = new BaseWorldSaveData
                    {
                        Loot = new List<ExpeditionLootSaveData>
                        {
                            new ExpeditionLootSaveData { RuntimeId = 61, ConfigId = "loot.wood", RemainingAmount = 3,
                                Pose = new ExpeditionPoseData { X = 1f, Z = 1f } }
                        },
                        TemporaryBuildings = new List<ExpeditionTemporaryBuildingSaveData>
                        {
                            new ExpeditionTemporaryBuildingSaveData { RuntimeId = 71, ConfigId = "temp_roadblock_wood",
                                Pose = new ExpeditionPoseData { X = -1f, Z = 1f } },
                            new ExpeditionTemporaryBuildingSaveData { RuntimeId = 72, ConfigId = "temp_missing_building",
                                Pose = new ExpeditionPoseData { X = 2f, Z = 1f } }
                        }
                    }
                };
                var result = map.LoadBaseMapAsync(snapshot).GetAwaiter().GetResult();
                Assert.That(result.Success, Is.False);
                Assert.That(Game.Interface.GetModel<LootModel>().Loot, Is.Empty);
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings, Is.Empty);
                Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.Zero);
                Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.Null);
                Assert.That(loader.UnloadCount, Is.GreaterThanOrEqualTo(1));
            }
            finally
            {
                if (loader != null)
                    foreach (var handle in loader.LoadedHandles)
                        if (handle.Root != null) UnityEngine.Object.DestroyImmediate(handle.Root);
                if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
                if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
                TemporaryBuildingBehaviorTestSupport.Reset();
            }
        }

        [Test]
        public void BuildingQualificationMatrix_AcceptsValidBaseAndExpeditionAndRejectsInvalidContexts()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            GameObject ground = null;
            try
            {
                var config = new TestRuntimeConfigProvider();
                var view = new FakeTemporaryBuildingViewPort();
                TemporaryBuildingBehaviorTestSupport.InstallArchitecture(config, view, new FakeProjectilePort());
                ground = TemporaryBuildingBehaviorTestSupport.CreateGround();
                TemporaryBuildingBehaviorTestSupport.EnterBase();
                TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
                var baseId = TemporaryBuildingBehaviorTestSupport.Build("temp_roadblock_wood", Vector3.right);
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings.ContainsKey(baseId), Is.True);

                Assert.That(Game.Interface.GetSystem<UnitSystem>().Remove(PlayerModel.LocalPlayerUnitId, UnitRemovedReason.Explicit), Is.True);
                TemporaryBuildingBehaviorTestSupport.EnterExpedition();
                TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 2);
                var expeditionId = TemporaryBuildingBehaviorTestSupport.Build("temp_campfire", Vector3.forward);
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings.ContainsKey(expeditionId), Is.True);

                var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
                var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
                Game.Interface.GetModel<ExpeditionModel>().QualifyReturn();
                Assert.That(Game.Interface.GetModel<ExpeditionModel>().Status, Is.EqualTo(ExpeditionStatus.ReturnQualified));
                Assert.That(system.BeginBuildPreview("temp_campfire", out var returnError), Is.True, returnError);
                system.CancelCurrentMode();

                void AssertRejected(string label, Func<bool> attempt)
                {
                    var bagBefore = TemporaryBuildingBehaviorTestSupport.BagSnapshot();
                    var idsBefore = Game.Interface.GetModel<TemporaryBuildingModel>().Buildings.Keys.OrderBy(id => id).ToArray();
                    var previewCountBefore = view.PreviewCreateCount;
                    var buildingCountBefore = view.BuildingCreateCount;
                    Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Mode, Is.EqualTo(TemporaryBuildingMode.Normal), label);
                    Assert.That(attempt(), Is.False, label);
                    var buildingModel = Game.Interface.GetModel<TemporaryBuildingModel>();
                    Assert.That(TemporaryBuildingBehaviorTestSupport.BagSnapshot(), Is.EqualTo(bagBefore), label);
                    Assert.That(buildingModel.Buildings.Keys.OrderBy(id => id), Is.EqualTo(idsBefore), label);
                    Assert.That(buildingModel.Mode, Is.EqualTo(TemporaryBuildingMode.Normal), label);
                    Assert.That(buildingModel.PreviewConfigId, Is.Null, label);
                    Assert.That(buildingModel.PreviewMapInstanceId, Is.Zero, label);
                    Assert.That(view.PreviewCreateCount, Is.EqualTo(previewCountBefore), label);
                    Assert.That(view.BuildingCreateCount, Is.EqualTo(buildingCountBefore), label);
                }

                Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), false);
                Assert.That(Game.Interface.GetModel<SaveModel>().CurrentDomain, Is.EqualTo(OfflineSaveDomain.None));
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
                Game.Interface.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.BaseSafe);
                AssertRejected("空存档域", () => system.BeginBuildPreview("temp_campfire", out _));

                Game.Interface.GetModel<ExpeditionModel>().BeginSurvival(MapIds.SurvivalSample, 60f);
                Game.Interface.GetModel<SaveModel>().SetExpedition(new ExpeditionSaveData { MapId = MapIds.SurvivalSample, Status = ExpeditionStatus.Playing });
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.MissionSample, null);
                Game.Interface.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.InExpedition);
                Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.Not.EqualTo(Game.Interface.GetModel<ExpeditionModel>().CurrentMapId));
                AssertRejected("副本地图错配", () => system.BeginBuildPreview("temp_campfire", out _));

                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
                Game.Interface.GetModel<ExpeditionModel>().MarkDefeated();
                Assert.That(Game.Interface.GetModel<ExpeditionModel>().Status, Is.EqualTo(ExpeditionStatus.Defeated));
                AssertRejected("副本失败状态", () => system.BeginBuildPreview("temp_campfire", out _));

                Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
                Game.Interface.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.BaseSafe);
                Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Loading;
                AssertRejected("非 Playing 状态", () => system.BeginBuildPreview("temp_campfire", out _));
            }
            finally
            {
                if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
                TemporaryBuildingBehaviorTestSupport.Reset();
            }
        }

        [Test]
        public void DestroyExpiredMapAndRepeatedSuccessKeepRefundIdempotent()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            GameObject ground = null;
            try
            {
                var config = new TestRuntimeConfigProvider();
                var view = new FakeTemporaryBuildingViewPort();
                TemporaryBuildingBehaviorTestSupport.InstallArchitecture(config, view, new FakeProjectilePort());
                ground = TemporaryBuildingBehaviorTestSupport.CreateGround();
                TemporaryBuildingBehaviorTestSupport.EnterBase();
                TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
                var id = TemporaryBuildingBehaviorTestSupport.Build("temp_roadblock_wood", Vector3.right);
                var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
                var refund = (FakeBuildingRefundDropPort)Game.Interface.GetUtility<IBuildingRefundDropPort>();
                Assert.That(system.BeginDestroyMode(out var error), Is.True, error);
                view.PickedRuntimeId = id;
                system.RefreshDestroyTarget(new Ray(Vector3.up * 4f, Vector3.down));
                TemporaryBuildingBehaviorTestSupport.MovePlayer(Vector3.zero, TemporaryBuildingBehaviorTestSupport.MapInstanceId + 1);
                Assert.That(system.ConfirmDestroy(out error), Is.False);
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings.ContainsKey(id), Is.True);
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().DestroyTargetBuildingId, Is.EqualTo(id));
                Assert.That(refund.CreateCount, Is.Zero);

                TemporaryBuildingBehaviorTestSupport.MovePlayer(Vector3.zero);
                Assert.That(system.ConfirmDestroy(out error), Is.True, error);
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings.ContainsKey(id), Is.False);
                Assert.That(refund.CreateCount, Is.EqualTo(1));
                Assert.That(system.ConfirmDestroy(out _), Is.False);
                Assert.That(refund.CreateCount, Is.EqualTo(1));
            }
            finally
            {
                if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
                TemporaryBuildingBehaviorTestSupport.Reset();
            }
        }
    }
}
