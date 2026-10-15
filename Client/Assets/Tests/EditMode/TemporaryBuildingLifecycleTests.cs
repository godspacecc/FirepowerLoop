using NUnit.Framework;
using QFramework;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class TemporaryBuildingLifecycleTests
    {
        private FakeTemporaryBuildingViewPort viewPort;
        private GameObject ground;

        [SetUp]
        public void SetUp()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            ResourceLoader.SetLoaderForTests(new LocalizationAssetResourceLoader());
            LocalizationLanguages.ResetForTests();
            viewPort = new FakeTemporaryBuildingViewPort();
            TemporaryBuildingBehaviorTestSupport.InstallArchitecture(new TestRuntimeConfigProvider(), viewPort, new FakeProjectilePort());
            TemporaryBuildingBehaviorTestSupport.EnterExpedition();
            ground = TemporaryBuildingBehaviorTestSupport.CreateGround();
        }

        [TearDown]
        public void TearDown()
        {
            if (ground != null) Object.DestroyImmediate(ground);
            TemporaryBuildingBehaviorTestSupport.Reset();
        }

        [Test]
        public void ClearForMapOnlyReleasesMatchingInstancesAndRepeatedReleaseIsIdempotent()
        {
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.MetalParts, 2);
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 5);
            var first = TemporaryBuildingBehaviorTestSupport.Build("temp_arrow_tower", new Vector3(1f, 0f, 0f));

            var unitSystem = Game.Interface.GetSystem<UnitSystem>();
            Assert.That(unitSystem.Remove(PlayerModel.LocalPlayerUnitId, UnitRemovedReason.Explicit), Is.True);
            Assert.That(unitSystem.TryRegister(new UnitSnapshot(
                PlayerModel.LocalPlayerUnitId, 902, UnitType.Player, UnitSource.Player, "player", new Vector3(4f, 0f, 0f),
                Quaternion.identity, UnitAuthorityMode.Local, 0, true), out var error), Is.True, error);
            var second = TemporaryBuildingBehaviorTestSupport.Build("temp_campfire", new Vector3(4f, 0f, 0f));
            var model = Game.Interface.GetModel<TemporaryBuildingModel>();
            Assert.That(model.Buildings.ContainsKey(first), Is.True);
            Assert.That(model.Buildings.ContainsKey(second), Is.True);

            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            system.ClearForMap(TemporaryBuildingBehaviorTestSupport.MapInstanceId);
            Assert.That(model.Buildings.ContainsKey(first), Is.False);
            Assert.That(model.Buildings.ContainsKey(second), Is.True);
            Assert.That(viewPort.ReleasedMaps.ContainsKey(TemporaryBuildingBehaviorTestSupport.MapInstanceId), Is.True);

            system.ClearForMap(TemporaryBuildingBehaviorTestSupport.MapInstanceId);
            Assert.That(model.Buildings.ContainsKey(second), Is.True);
            Assert.That(viewPort.Buildings.ContainsKey(second), Is.True);
        }

        [UnityTest]
        public IEnumerator FailedCandidateLoadPreservesOldBuildingInstance()
        {
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.MetalParts, 2);
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
            var oldRuntimeId = TemporaryBuildingBehaviorTestSupport.Build("temp_arrow_tower", new Vector3(1f, 0f, 0f));
            var oldHandle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.Base, 11);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, oldHandle);
            var loader = new FakeMapContentLoader { FailLoad = true };
            var mapSystem = TemporaryBuildingBehaviorTestSupport.RegisterMapSystem(loader);

            var task = mapSystem.LoadMapAsync(MapIds.Base).AsTask();
            yield return Await(task);

            Assert.That(task.Result.Success, Is.False);
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings.ContainsKey(oldRuntimeId), Is.True);
            Assert.That(viewPort.Buildings.ContainsKey(oldRuntimeId), Is.True);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.EqualTo(oldHandle));
        }

        [UnityTest]
        public IEnumerator UnloadCurrentMapReleasesOnlyCurrentMapAndPublishesRelease()
        {
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.MetalParts, 2);
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
            var runtimeId = TemporaryBuildingBehaviorTestSupport.Build("temp_arrow_tower", new Vector3(1f, 0f, 0f));
            var handle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.Base, 12);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, handle);
            var releaseEvents = 0;
            var unregister = Game.Interface.RegisterEvent<MapCurrentReleasedEvent>(evt => releaseEvents++);
            var mapSystem = TemporaryBuildingBehaviorTestSupport.RegisterMapSystem(new FakeMapContentLoader());

            var task = mapSystem.UnloadCurrentMapAsync().AsTask();
            yield return Await(task);
            unregister.UnRegister();

            Assert.That(task.Result.Success, Is.True, task.Result.Error);
            Assert.That(releaseEvents, Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings.ContainsKey(runtimeId), Is.False);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.Null);
        }

        [Test]
        public void DestroyModeRemovesAnyBuildingWithoutRefund()
        {
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.MetalParts, 2);
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 8);
            var arrow = TemporaryBuildingBehaviorTestSupport.Build("temp_arrow_tower", new Vector3(1f, 0f, 0f));
            var roadblock = TemporaryBuildingBehaviorTestSupport.Build("temp_roadblock_wood", new Vector3(3f, 0f, 0f));
            var campfire = TemporaryBuildingBehaviorTestSupport.Build("temp_campfire", new Vector3(4.8f, 0f, 0f));
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var snapshot = TemporaryBuildingBehaviorTestSupport.BagSnapshot();
            var destroyed = new List<TemporaryBuildingDestroyedEvent>();
            var unregister = Game.Interface.RegisterEvent<TemporaryBuildingDestroyedEvent>(destroyed.Add);
            try
            {
                var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
                foreach (var pair in new[]
                {
                    (arrow, new Vector3(0f, 0f, 0f)),
                    (roadblock, new Vector3(3f, 0f, 0f)),
                    (campfire, new Vector3(4.8f, 0f, 0f))
                })
                {
                    TemporaryBuildingBehaviorTestSupport.MovePlayer(pair.Item2);
                    Assert.That(system.BeginDestroyMode(out var error), Is.True, error);
                    viewPort.PickedRuntimeId = pair.Item1;
                    system.RefreshDestroyTarget(new Ray(Vector3.up, Vector3.down));
                    Assert.That(system.ConfirmDestroy(out error), Is.True, error);
                    system.CancelCurrentMode();
                }

                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings, Is.Empty);
                Assert.That(viewPort.Buildings, Is.Empty);
                Assert.That(destroyed.Select(value => value.RuntimeId), Is.EqualTo(new[] { arrow, roadblock, campfire }));
                Assert.That(TemporaryBuildingBehaviorTestSupport.BagSnapshot(), Is.EqualTo(snapshot));
                Assert.That(bag.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(0));
                Assert.That(bag.GetTotalAmount(ItemIds.Wood), Is.EqualTo(0));
            }
            finally
            {
                unregister.UnRegister();
            }
        }

        [Test]
        public void CreatedViewBelongsToCurrentMapDynamicObjectRoot()
        {
            var handle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.Base, 21);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, handle);
            var provider = Game.Interface.GetUtility<IRuntimeConfigProvider>();
            Assert.That(provider.TryGetTempBuilding("temp_roadblock_wood", out var definition, out var error), Is.True, error);
            var controller = new TemporaryBuildingCtrl((Game)Game.Interface);
            var state = new TemporaryBuildingRuntimeState(901, definition.Id, TemporaryBuildingBehaviorTestSupport.MapInstanceId,
                Vector3.zero, Quaternion.identity);
            try
            {
                Assert.That(controller.TryCreateBuilding(state, definition, false, out error), Is.True, error);
                var child = handle.Context.DynamicObjectRoot.GetComponentsInChildren<TemporaryBuildingView>(true).Single();
                Assert.That(child.transform.parent, Is.EqualTo(handle.Context.DynamicObjectRoot));
                Assert.That(child.transform.IsChildOf(handle.Context.DynamicObjectRoot), Is.True);
            }
            finally
            {
                controller.ReleaseAll();
                if (handle.Root != null) Object.DestroyImmediate(handle.Root);
            }
        }

        private static IEnumerator Await(System.Threading.Tasks.Task task)
        {
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception;
        }
    }
}
