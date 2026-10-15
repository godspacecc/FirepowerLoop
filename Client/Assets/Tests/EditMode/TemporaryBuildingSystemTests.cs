using NUnit.Framework;
using QFramework;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class TemporaryBuildingSystemTests
    {
        private FakeTemporaryBuildingViewPort viewPort;
        private FakeProjectilePort projectile;
        private GameObject ground;

        [SetUp]
        public void SetUp()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            ResourceLoader.SetLoaderForTests(new LocalizationAssetResourceLoader());
            LocalizationLanguages.ResetForTests();
            viewPort = new FakeTemporaryBuildingViewPort();
            projectile = new FakeProjectilePort();
            TemporaryBuildingBehaviorTestSupport.InstallArchitecture(new TestRuntimeConfigProvider(), viewPort, projectile);
            TemporaryBuildingBehaviorTestSupport.EnterExpedition();
            ground = TemporaryBuildingBehaviorTestSupport.CreateGround();
        }

        [TearDown]
        public void TearDown()
        {
            if (ground != null) Object.DestroyImmediate(ground);
            TemporaryBuildingBehaviorTestSupport.Reset();
        }

        [TestCase("temp_roadblock_wood", 3.8f, true)]
        [TestCase("temp_roadblock_wood", 3.9f, true)]
        [TestCase("temp_roadblock_wood", 3.91f, false)]
        [TestCase("temp_roadblock_wood", 4f, false)]
        [TestCase("temp_campfire", 3.5f, true)]
        [TestCase("temp_campfire", 3.7f, false)]
        [TestCase("temp_arrow_tower", 3.7f, true)]
        [TestCase("temp_arrow_tower", 3.8f, false)]
        public void BuildAndDestroyUseSamePlayerReachAtFootprintEdge(string configId, float distance, bool reachable)
        {
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            var model = Game.Interface.GetModel<TemporaryBuildingModel>();
            Assert.That(system.BeginBuildPreview(configId, out var error), Is.True, error);
            system.UpdateBuildPreview(Vector3.right * distance, Quaternion.identity);
            Assert.That(model.PreviewValid, Is.EqualTo(reachable), model.PreviewReason);
            system.CancelCurrentMode();
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 20);
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.MetalParts, 10);
            var id = TemporaryBuildingBehaviorTestSupport.Build(configId, Vector3.zero);
            Assert.That(system.BeginDestroyMode(out error), Is.True, error);
            TemporaryBuildingBehaviorTestSupport.MovePlayer(Vector3.right * distance);
            viewPort.PickedRuntimeId = id;
            system.RefreshDestroyTarget(new Ray(Vector3.up * 10, Vector3.down));
            Assert.That(model.DestroyTargetBuildingId, Is.EqualTo(reachable ? id : 0));
            Assert.That(model.DestroyTargetTooFar, Is.EqualTo(!reachable));
        }

        [TestCase(2.9f, true)]
        [TestCase(3.1f, false)]
        public void BuildingFootprintDoesNotExtendVerticalPlayerReach(float height, bool reachable)
        {
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
            var id = TemporaryBuildingBehaviorTestSupport.Build("temp_roadblock_wood", Vector3.zero);
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            Assert.That(system.BeginDestroyMode(out var error), Is.True, error);
            TemporaryBuildingBehaviorTestSupport.MovePlayer(Vector3.up * height);
            viewPort.PickedRuntimeId = id;
            system.RefreshDestroyTarget(new Ray(Vector3.up * 10, Vector3.down));
            Assert.That(system.GetHudSnapshot().DestroyTargetBuildingId, Is.EqualTo(reachable ? id : 0));
        }

        [Test]
        public void PreviewRangeRefreshesWhenPlayerMovesWithoutChangingPreviewPose()
        {
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            Assert.That(system.BeginBuildPreview("temp_roadblock_wood", out var error), Is.True, error);
            system.UpdateBuildPreview(Vector3.forward * 2f, Quaternion.Euler(0f, 15f, 0f));
            TemporaryBuildingBehaviorTestSupport.MovePlayer(Vector3.back * 4f);
            typeof(TemporaryBuildingSystem).GetMethod("OnUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(system, null);
            var model = Game.Interface.GetModel<TemporaryBuildingModel>();
            Assert.That(model.PreviewValid, Is.False);
            Assert.That(model.PreviewPosition, Is.EqualTo(Vector3.forward * 2f));
            Assert.That(model.PreviewRotation.eulerAngles.y, Is.EqualTo(15f).Within(.01f));
            TemporaryBuildingBehaviorTestSupport.MovePlayer(Vector3.zero);
            typeof(TemporaryBuildingSystem).GetMethod("OnUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(system, null);
            Assert.That(model.PreviewValid, Is.True, model.PreviewReason);
        }

        [Test]
        public void ThreeBuildingKindsSpendMaterialsAndFailedCommitRollsBack()
        {
            var created = 0;
            string createdConfig = null;
            var unregister = Game.Interface.RegisterEvent<TemporaryBuildingCreatedEvent>(evt => { created++; createdConfig = evt.ConfigId; });
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.MetalParts, 2);
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;

            Assert.That(system.BeginBuildPreview("temp_arrow_tower", out var error), Is.True, error);
            Assert.That(system.UpdateBuildPreview(new Vector3(1f, 0f, 0f), Quaternion.identity), Is.True);
            Assert.That(system.ConfirmBuild(out error), Is.True, error);
            system.TickForTests(1.1f);
            Assert.That(created, Is.EqualTo(1));
            Assert.That(createdConfig, Is.EqualTo("temp_arrow_tower"));
            Assert.That(bag.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(0));
            Assert.That(bag.GetTotalAmount(ItemIds.Wood), Is.EqualTo(0));

            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
            viewPort.CreateBuildingSucceeds = false;
            Assert.That(system.BeginBuildPreview("temp_roadblock_wood", out error), Is.True, error);
            Assert.That(system.UpdateBuildPreview(new Vector3(3f, 0f, 0f), Quaternion.identity), Is.True);
            Assert.That(system.ConfirmBuild(out error), Is.True, error);
            LogAssert.Expect(LogType.Error, "[TemporaryBuildingSystem] 建造表现创建失败：create blocked");
            system.TickForTests(1.1f);
            Assert.That(bag.GetTotalAmount(ItemIds.Wood), Is.EqualTo(3));
            Assert.That(created, Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Mode, Is.EqualTo(TemporaryBuildingMode.BuildPreview));
            unregister.UnRegister();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BuildingUsesPocketAndRollsBackBothContainers(bool fail)
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>();
            var inventoryEvents = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => inventoryEvents++);
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 1);
            Assert.That(bag.Pocket.TryAdd(ItemIds.Wood, 2, 20, out _, out var error), Is.True, error);
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            Assert.That(system.BeginBuildPreview("temp_roadblock_wood", out error), Is.True, error);
            Assert.That(system.UpdateBuildPreview(Vector3.right, Quaternion.identity), Is.True);
            Assert.That(system.ConfirmBuild(out error), Is.True, error);
            viewPort.CreateBuildingSucceeds = !fail;
            if (fail) LogAssert.Expect(LogType.Error, "[TemporaryBuildingSystem] 建造表现创建失败：create blocked");
            system.TickForTests(1.1f);
            Assert.That(bag.Container.GetTotalAmount(ItemIds.Wood), Is.EqualTo(fail ? 1 : 0));
            Assert.That(bag.Pocket.GetTotalAmount(ItemIds.Wood), Is.EqualTo(fail ? 2 : 0));
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings.Count, Is.EqualTo(fail ? 0 : 1));
            Assert.That(inventoryEvents, Is.EqualTo(fail ? 0 : 1));
        }

        [Test]
        public void ManualBuildingReloadConsumesPocketAmmo()
        {
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.MetalParts, 2);
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
            var id = TemporaryBuildingBehaviorTestSupport.Build("temp_arrow_tower", Vector3.right);
            var pocket = Game.Interface.GetModel<PlayerBagModel>().Pocket;
            Assert.That(pocket.TryAdd(ItemIds.Arrow, 2, 20, out _, out var error), Is.True, error);
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            Assert.That(system.TryEnterManualAttackOperation(id, out error), Is.True, error);
            Assert.That(system.RequestManualReload(out error), Is.True, error);
            system.TickForTests(2.1f);
            Assert.That(pocket.GetTotalAmount(ItemIds.Arrow), Is.Zero);
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().ManualAttacks[id].LoadedAmmo, Is.EqualTo(2));
        }

        [Test]
        public void CampfireCommitsWithoutNavigationBlocking()
        {
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 2);
            var runtimeId = TemporaryBuildingBehaviorTestSupport.Build("temp_campfire", new Vector3(1f, 0f, 0f));
            Assert.That(viewPort.Definitions[runtimeId].BlocksNavigation, Is.False);
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings.ContainsKey(runtimeId), Is.True);
        }

        [Test]
        public void AllConfiguredKindsCanBeginPreviewById()
        {
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            foreach (var configId in new[] { "temp_arrow_tower", "temp_roadblock_wood", "temp_campfire" })
            {
                Assert.That(system.BeginBuildPreview(configId, out var error), Is.True, error);
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().PreviewConfigId, Is.EqualTo(configId));
                system.CancelCurrentMode();
            }

            Assert.That(viewPort.PreviewCreateCount, Is.EqualTo(3));
        }

        [Test]
        public void BuildPublishesCommonCreatedEventForEveryConfiguredKind()
        {
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.MetalParts, 2);
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 8);
            var created = new List<TemporaryBuildingCreatedEvent>();
            var unregister = Game.Interface.RegisterEvent<TemporaryBuildingCreatedEvent>(created.Add);
            try
            {
                var arrow = TemporaryBuildingBehaviorTestSupport.Build("temp_arrow_tower", new Vector3(1f, 0f, 0f));
                var roadblock = TemporaryBuildingBehaviorTestSupport.Build("temp_roadblock_wood", new Vector3(3f, 0f, 0f));
                // 此测试验证创建事件而非远距离放置；靠近第三个位置以满足统一操作距离。
                TemporaryBuildingBehaviorTestSupport.MovePlayer(Vector3.right * 4f);
                var campfire = TemporaryBuildingBehaviorTestSupport.Build("temp_campfire", new Vector3(4.8f, 0f, 0f));

                Assert.That(created.Select(value => value.ConfigId), Is.EqualTo(new[]
                    { "temp_arrow_tower", "temp_roadblock_wood", "temp_campfire" }));
                Assert.That(created.Select(value => value.RuntimeId), Is.EqualTo(new[] { arrow, roadblock, campfire }));
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings, Has.Count.EqualTo(3));
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().ManualAttacks.Keys, Is.EqualTo(new[] { arrow }));
            }
            finally
            {
                unregister.UnRegister();
            }
        }

        [Test]
        public void NonBlockingCampfireSkipsNarrowPassageRejection()
        {
            var left = new GameObject("NarrowLeft");
            var right = new GameObject("NarrowRight");
            left.transform.position = new Vector3(-1f, 0f, 0f);
            right.transform.position = new Vector3(1f, 0f, 0f);
            left.AddComponent<BoxCollider>().size = new Vector3(.3f, 2f, 2f);
            right.AddComponent<BoxCollider>().size = new Vector3(.3f, 2f, 2f);
            try
            {
                TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 2);
                var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
                Assert.That(system.BeginBuildPreview("temp_campfire", out var error), Is.True, error);
                Assert.That(system.UpdateBuildPreview(Vector3.zero, Quaternion.identity), Is.True);
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().PreviewValid, Is.True,
                    Game.Interface.GetModel<TemporaryBuildingModel>().PreviewReason);
            }
            finally
            {
                Object.DestroyImmediate(left);
                Object.DestroyImmediate(right);
            }
        }

        [Test]
        public void ManualAttackConsumesAmmoHonorsCooldownAndRejectsOtherKinds()
        {
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.MetalParts, 2);
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
            var arrowTower = TemporaryBuildingBehaviorTestSupport.Build("temp_arrow_tower", new Vector3(1f, 0f, 0f));
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Arrow, 2);
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            Assert.That(system.TryEnterManualAttackOperation(arrowTower, out var error), Is.True, error);
            Assert.That(system.RequestManualReload(out error), Is.True, error);
            system.TickForTests(2.1f);
            Assert.That(system.RequestManualAttack(out var projectileId, out error), Is.True, error);
            Assert.That(projectileId, Is.GreaterThan(0));
            Assert.That(projectile.CreateCount, Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.Zero);
            Assert.That(system.RequestManualAttack(out _, out error), Is.False);
            Assert.That(system.GetHudSnapshot().CooldownRemaining, Is.GreaterThan(0f));

            system.CancelCurrentMode();
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
            var roadblock = TemporaryBuildingBehaviorTestSupport.Build("temp_roadblock_wood", new Vector3(3f, 0f, 0f));
            Assert.That(system.TryEnterManualAttackOperation(roadblock, out error), Is.False);
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().OperatingManualAttackId, Is.Zero);

            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 2);
            TemporaryBuildingBehaviorTestSupport.MovePlayer(Vector3.right * 4f);
            var campfire = TemporaryBuildingBehaviorTestSupport.Build("temp_campfire", new Vector3(4.8f, 0f, 0f));
            Assert.That(system.TryEnterManualAttackOperation(campfire, out error), Is.False);
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().OperatingManualAttackId, Is.Zero);
        }
    }
}
