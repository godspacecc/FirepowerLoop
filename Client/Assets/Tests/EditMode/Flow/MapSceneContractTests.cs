using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class MapSceneContractTests
    {
        private static readonly string[] HeJiaAddresses =
        {
            "Scenes/GeneratedMaps/HeJiaVillage/Scenes/HeJiaVillage_1_1",
            "Scenes/GeneratedMaps/HeJiaVillage/Scenes/HeJiaVillage_1_2",
            "Scenes/GeneratedMaps/HeJiaVillage/Scenes/HeJiaVillage_2_1",
            "Scenes/GeneratedMaps/HeJiaVillage/Scenes/HeJiaVillage_2_2"
        };
        private FakeTemporaryBuildingViewPort viewPort;
        private GameObject cameraObject;

        [SetUp]
        public void SetUp()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            ResourceLoader.SetLoaderForTests(new LocalizationAssetResourceLoader());
            LocalizationLanguages.ResetForTests();
            viewPort = new FakeTemporaryBuildingViewPort();
            TemporaryBuildingBehaviorTestSupport.InstallArchitecture(new TestRuntimeConfigProvider(true), viewPort, new FakeProjectilePort());
            TemporaryBuildingBehaviorTestSupport.EnterExpedition();
            cameraObject = new GameObject("MapContractCamera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
        }

        [TearDown]
        public void TearDown()
        {
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            TemporaryBuildingBehaviorTestSupport.Reset();
        }

        [Test]
        public void CatalogDoesNotOwnRuntimeSampleAddress()
        {
            Assert.IsTrue(MapCatalog.TryGet("map.another-day.base", out var definition));
            Assert.That(definition.MapId, Is.EqualTo("map.another-day.base"));
            Assert.That(definition.Kind, Is.EqualTo(MapKind.Base));
        }

        [UnityTest]
        public IEnumerator SuccessfulReplacementPublishesOneReleaseForCapturedOldMap()
        {
            var oldHandle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.Base, 11);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, oldHandle);
            var unitSystem = Game.Interface.GetSystem<UnitSystem>();
            var loader = new FakeMapContentLoader
            {
                OnUnloadSuccess = () => unitSystem.Remove(PlayerModel.LocalPlayerUnitId, UnitRemovedReason.Explicit)
            };
            var mapSystem = TemporaryBuildingBehaviorTestSupport.RegisterMapSystem(loader);
            Assert.That(mapSystem.Initialized, Is.True, "MapSceneSystem 必须在候选地图加载前完成初始化。");
            var events = new List<MapCurrentReleasedEvent>();
            var loadingStages = new List<string>();
            var playerReady = false;
            var unregister = Game.Interface.RegisterEvent<MapCurrentReleasedEvent>(events.Add);
            var unregisterStarted = Game.Interface.RegisterEvent<MapLoadingStartedEvent>(_ => loadingStages.Add("started"));
            var unregisterCompleted = Game.Interface.RegisterEvent<MapLoadingCompletedEvent>(_ => loadingStages.Add("completed"));
            var unregisterFailed = Game.Interface.RegisterEvent<MapLoadingFailedEvent>(failed => loadingStages.Add("failed:" + failed.Error));
            var unregisterPlayerReady = Game.Interface.RegisterEvent<PlayerReadyEvent>(_ => playerReady = true);
            try
            {
                var task = mapSystem.LoadMapAsync(MapIds.SurvivalSample).AsTask();
                yield return Await(task);

                Assert.That(task.Result.Success, Is.True,
                    $"{task.Result.Error}; stages={string.Join(",", loadingStages)}; candidateCount={loader.LoadedHandles.Count}; " +
                    $"unloadCount={loader.UnloadCount}; unloadedOld={ReferenceEquals(loader.LastUnloaded, oldHandle)}; " +
                    $"releaseCount={events.Count}; playerReady={playerReady}");
                Assert.That(events, Has.Count.EqualTo(1));
                Assert.That(events[0].MapId, Is.EqualTo(MapIds.Base));
                Assert.That(events[0].MapInstanceId, Is.EqualTo(TemporaryBuildingBehaviorTestSupport.MapInstanceId));
                Assert.That(loader.UnloadCount, Is.EqualTo(1));
                Assert.That(loader.LastUnloaded, Is.SameAs(oldHandle));
                Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.EqualTo(MapIds.SurvivalSample));
                Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.Not.Null);
            }
            finally
            {
                unregister.UnRegister();
                unregisterStarted.UnRegister();
                unregisterCompleted.UnRegister();
                unregisterFailed.UnRegister();
                unregisterPlayerReady.UnRegister();
            }
        }

        [UnityTest]
        public IEnumerator RecoveryFailureCleansBothMapsAndPublishesCapturedOldReleaseOnce()
        {
            var oldHandle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.Base, 12);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, oldHandle);
            var loader = new FakeMapContentLoader { FailNextUnload = true, InvalidateBeforeUnloadFailure = true };
            var mapSystem = TemporaryBuildingBehaviorTestSupport.RegisterMapSystem(loader);
            var events = new List<MapCurrentReleasedEvent>();
            var unregister = Game.Interface.RegisterEvent<MapCurrentReleasedEvent>(events.Add);
            try
            {
                var task = mapSystem.LoadMapAsync(MapIds.SurvivalSample).AsTask();
                yield return Await(task);

                Assert.That(task.Result.Success, Is.False);
                Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.Null);
                Assert.That(events, Has.Count.EqualTo(1));
                Assert.That(events[0].MapId, Is.EqualTo(MapIds.Base));
                Assert.That(events[0].MapInstanceId, Is.EqualTo(TemporaryBuildingBehaviorTestSupport.MapInstanceId));
                Assert.That(oldHandle.IsValid, Is.False);
                Assert.That(loader.LoadedHandles, Has.Count.EqualTo(1));
                Assert.That(loader.LoadedHandles[0].IsValid, Is.False);
            }
            finally
            {
                unregister.UnRegister();
            }
        }

        [UnityTest]
        public IEnumerator CandidateFailureRestoresOldMapWithoutReleaseEvent()
        {
            var oldHandle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.Base, 13);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, oldHandle);
            var loader = new FakeMapContentLoader { FailLoad = true };
            var mapSystem = TemporaryBuildingBehaviorTestSupport.RegisterMapSystem(loader);
            var events = new List<MapCurrentReleasedEvent>();
            var unregister = Game.Interface.RegisterEvent<MapCurrentReleasedEvent>(events.Add);
            try
            {
                var task = mapSystem.LoadMapAsync(MapIds.SurvivalSample).AsTask();
                yield return Await(task);

                Assert.That(task.Result.Success, Is.False);
                Assert.That(events, Is.Empty);
                Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.EqualTo(MapIds.Base));
                Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.SameAs(oldHandle));
                Assert.That(oldHandle.IsValid, Is.True);
            }
            finally
            {
                unregister.UnRegister();
            }
        }

        [UnityTest]
        public IEnumerator GroupLoadPositionsActivatesAndUnloadsAllPartsInReverse()
        {
            Game.Interface.GetSystem<UnitSystem>().Remove(PlayerModel.LocalPlayerUnitId, UnitRemovedReason.Explicit);
            ((Game)Game.Interface).RegisterUtility<IRuntimeConfigProvider>(
                new TestRuntimeConfigProvider(true, null, HeJiaAddresses));
            var loader = new FakeMapContentLoader();
            var mapSystem = TemporaryBuildingBehaviorTestSupport.RegisterMapSystem(loader);
            var progress = new List<float>();
            var unregister = Game.Interface.RegisterEvent<MapLoadingProgressEvent>(evt =>
            {
                if (evt.MapId == MapIds.HeJiaVillage) progress.Add(evt.Progress);
            });
            try
            {
                var load = mapSystem.LoadMapAsync(MapIds.HeJiaVillage).AsTask();
                yield return Await(load);
                Assert.That(load.Result.Success, Is.True, load.Result.Error);
                Assert.That(loader.LoadedAddresses, Is.EqualTo(HeJiaAddresses));
                Assert.That(progress.Last(), Is.EqualTo(1f));
                Assert.That(progress.Take(progress.Count - 1), Has.All.LessThan(1f));
                var group = Game.Interface.GetModel<MapSceneModel>().CurrentHandle as MapContentGroupHandle;
                Assert.That(group, Is.Not.Null);
                Assert.That(group.Children.Select(child => child.Context.MapContentRoot.transform.position), Is.EqualTo(new[]
                {
                    Vector3.zero, new Vector3(1024f, 0f, 0f), new Vector3(0f, 0f, 1024f), new Vector3(1024f, 0f, 1024f)
                }));
                Assert.That(group.Children.All(child => child.Context.MapContentRoot.activeSelf), Is.True);
                Assert.That(group.Children.Skip(1).All(child => child.Context.DynamicObjectRoot.childCount == 0), Is.True);

                var children = group.Children.ToArray();
                var unload = mapSystem.UnloadCurrentMapAsync().AsTask();
                yield return Await(unload);
                Assert.That(unload.Result.Success, Is.True, unload.Result.Error);
                Assert.That(loader.UnloadedHandles, Is.EqualTo(children.Reverse()));
                Assert.That(loader.UnloadedHandles.Distinct().Count(), Is.EqualTo(4));
            }
            finally
            {
                unregister.UnRegister();
            }
        }

        [UnityTest]
        public IEnumerator GroupMetadataFailureCleansLoadedPartsAndKeepsOldMap()
        {
            Game.Interface.GetSystem<UnitSystem>().Remove(PlayerModel.LocalPlayerUnitId, UnitRemovedReason.Explicit);
            ((Game)Game.Interface).RegisterUtility<IRuntimeConfigProvider>(
                new TestRuntimeConfigProvider(true, null, HeJiaAddresses));
            var oldHandle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.Base, 30);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, oldHandle);
            var loader = new FakeMapContentLoader { DuplicateTerrainCoordinateAtIndex = 1 };
            var mapSystem = TemporaryBuildingBehaviorTestSupport.RegisterMapSystem(loader);
            var releases = new List<MapCurrentReleasedEvent>();
            var unregister = Game.Interface.RegisterEvent<MapCurrentReleasedEvent>(releases.Add);
            try
            {
                var load = mapSystem.LoadMapAsync(MapIds.HeJiaVillage).AsTask();
                yield return Await(load);
                Assert.That(load.Result.Success, Is.False);
                Assert.That(load.Result.Error, Does.Contain("重复"));
                Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.SameAs(oldHandle));
                Assert.That(oldHandle.IsValid, Is.True);
                Assert.That(releases, Is.Empty);
                Assert.That(loader.UnloadedHandles, Is.EqualTo(loader.LoadedHandles.AsEnumerable().Reverse()));
                Assert.That(loader.UnloadedHandles.Distinct().Count(), Is.EqualTo(4));
            }
            finally
            {
                unregister.UnRegister();
            }
        }

        [UnityTest]
        public IEnumerator GroupCandidateCleanupFailureRetainsOwnershipUntilRetry()
        {
            Game.Interface.GetSystem<UnitSystem>().Remove(PlayerModel.LocalPlayerUnitId, UnitRemovedReason.Explicit);
            ((Game)Game.Interface).RegisterUtility<IRuntimeConfigProvider>(
                new TestRuntimeConfigProvider(true, null, HeJiaAddresses));
            var loader = new FakeMapContentLoader
            {
                FailLoadAtIndex = 2,
                PersistentlyFailLoadedHandleIndex = 1
            };
            var mapSystem = TemporaryBuildingBehaviorTestSupport.RegisterMapSystem(loader);
            var releases = new List<MapCurrentReleasedEvent>();
            var unregister = Game.Interface.RegisterEvent<MapCurrentReleasedEvent>(releases.Add);
            try
            {
                // 第三块加载失败，第二块的两轮清理持续失败：候选残留保持隐藏且不影响旧基地所有权。
                var oldBase = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.Base, 40);
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, oldBase);
                var partial = mapSystem.LoadMapAsync(MapIds.HeJiaVillage).AsTask();
                yield return Await(partial);
                Assert.That(partial.Result.Success, Is.False);
                Assert.That(loader.LoadedHandles, Has.Count.EqualTo(2));
                var partialChildren = loader.LoadedHandles.Cast<IMapContentHandle>().ToArray();
                var partialResidual = partialChildren[1];
                Assert.That(loader.UnloadedHandles, Is.EqualTo(new[]
                {
                    partialChildren[1], partialChildren[0], partialChildren[1]
                }));
                Assert.That(partialChildren[0].IsValid, Is.False);
                Assert.That(partialResidual.IsValid, Is.True);
                Assert.That(partialResidual.Context.MapContentRoot.activeSelf, Is.False);
                Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.EqualTo(MapIds.Base));
                Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.SameAs(oldBase));
                Assert.That(oldBase.IsValid, Is.True);
                Assert.That(releases, Is.Empty);

                var loadCallsBeforeBlock = loader.LoadedAddresses.Count;
                var blocked = mapSystem.LoadMapAsync(MapIds.SurvivalSample).AsTask();
                yield return Await(blocked);
                Assert.That(blocked.Result.Success, Is.False);
                Assert.That(blocked.Result.Error, Does.Contain("待清理"));
                Assert.That(loader.LoadedAddresses, Has.Count.EqualTo(loadCallsBeforeBlock));

                loader.PersistentlyFailLoadedHandleIndex = -1;
                var retryCleanup = mapSystem.UnloadCurrentMapAsync().AsTask();
                yield return Await(retryCleanup);
                Assert.That(retryCleanup.Result.Success, Is.True, retryCleanup.Result.Error);
                Assert.That(loader.UnloadedHandles.Last(), Is.SameAs(partialResidual));
                Assert.That(partialChildren.All(child => !child.IsValid), Is.True);
                Assert.That(partialChildren.All(child => ((FakeMapHandle)child).Root == null), Is.True);
                Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.EqualTo(MapIds.Base));
                Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.SameAs(oldBase));
                Assert.That(oldBase.IsValid, Is.True);
                Assert.That(releases, Is.Empty, "清理未提交候选不得发布基地或候选释放事件。");

                loader.ResetObservations();
                var retryLoad = mapSystem.LoadMapAsync(MapIds.HeJiaVillage).AsTask();
                yield return Await(retryLoad);
                Assert.That(retryLoad.Result.Success, Is.True, retryLoad.Result.Error);
                Assert.That(releases, Has.Count.EqualTo(1));
                Assert.That(releases[0].MapId, Is.EqualTo(MapIds.Base));

                var reset = mapSystem.UnloadCurrentMapAsync().AsTask();
                yield return Await(reset);
                Assert.That(reset.Result.Success, Is.True, reset.Result.Error);
                releases.Clear();
                loader.ResetObservations();

                // 四块加载完成后由真实 TemporaryBuildingCtrl 对无效恢复快照准备失败；候选组残留遵守同一所有权规则。
                var oldBaseAfterPrepareFailure = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.Base, 41);
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, oldBaseAfterPrepareFailure);
                loader.PersistentlyFailLoadedHandleIndex = 1;
                var invalidSnapshot = new ExpeditionSaveData
                {
                    MapId = MapIds.HeJiaVillage,
                    World = new ExpeditionWorldSaveData
                    {
                        TemporaryBuildings = new List<ExpeditionTemporaryBuildingSaveData>
                        {
                            new ExpeditionTemporaryBuildingSaveData { RuntimeId = 0, ConfigId = string.Empty }
                        }
                    }
                };
                var prepareFailure = mapSystem.LoadMapAsync(MapIds.HeJiaVillage, invalidSnapshot).AsTask();
                yield return Await(prepareFailure);
                Assert.That(prepareFailure.Result.Success, Is.False);
                Assert.That(prepareFailure.Result.Error, Does.Contain("临时建筑准备失败"));
                Assert.That(loader.LoadedHandles, Has.Count.EqualTo(4));
                var preparedChildren = loader.LoadedHandles.Cast<IMapContentHandle>().ToArray();
                var preparedResidual = preparedChildren[1];
                Assert.That(loader.UnloadedHandles, Is.EqualTo(new[]
                {
                    preparedChildren[3], preparedChildren[2], preparedChildren[1], preparedChildren[0], preparedChildren[1]
                }));
                Assert.That(preparedResidual.IsValid, Is.True);
                Assert.That(preparedResidual.Context.MapContentRoot.activeSelf, Is.False);
                Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.EqualTo(MapIds.Base));
                Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.SameAs(oldBaseAfterPrepareFailure));
                Assert.That(releases, Is.Empty);

                loadCallsBeforeBlock = loader.LoadedAddresses.Count;
                blocked = mapSystem.LoadMapAsync(MapIds.SurvivalSample).AsTask();
                yield return Await(blocked);
                Assert.That(blocked.Result.Success, Is.False);
                Assert.That(loader.LoadedAddresses, Has.Count.EqualTo(loadCallsBeforeBlock));

                loader.PersistentlyFailLoadedHandleIndex = -1;
                retryCleanup = mapSystem.UnloadCurrentMapAsync().AsTask();
                yield return Await(retryCleanup);
                Assert.That(retryCleanup.Result.Success, Is.True, retryCleanup.Result.Error);
                Assert.That(preparedChildren.All(child => !child.IsValid), Is.True);
                Assert.That(preparedChildren.All(child => ((FakeMapHandle)child).Root == null), Is.True);
                Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.EqualTo(MapIds.Base));
                Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.SameAs(oldBaseAfterPrepareFailure));
                Assert.That(oldBaseAfterPrepareFailure.IsValid, Is.True);
                Assert.That(releases, Is.Empty);

                loader.ResetObservations();
                retryLoad = mapSystem.LoadMapAsync(MapIds.HeJiaVillage).AsTask();
                yield return Await(retryLoad);
                Assert.That(retryLoad.Result.Success, Is.True, retryLoad.Result.Error);
                Assert.That(releases, Has.Count.EqualTo(1));
                Assert.That(releases[0].MapId, Is.EqualTo(MapIds.Base));
            }
            finally
            {
                unregister.UnRegister();
            }
        }

        [UnityTest]
        public IEnumerator GroupPartialLoadAndUnloadFailuresReleaseEveryOwnedPart()
        {
            Game.Interface.GetSystem<UnitSystem>().Remove(PlayerModel.LocalPlayerUnitId, UnitRemovedReason.Explicit);
            ((Game)Game.Interface).RegisterUtility<IRuntimeConfigProvider>(
                new TestRuntimeConfigProvider(true, null, HeJiaAddresses));
            var loader = new FakeMapContentLoader { FailLoadAtIndex = 2 };
            var mapSystem = TemporaryBuildingBehaviorTestSupport.RegisterMapSystem(loader);

            // 部分加载失败：只清理已取得所有权的两块，并保持反向顺序。
            var partial = mapSystem.LoadMapAsync(MapIds.HeJiaVillage).AsTask();
            yield return Await(partial);
            Assert.That(partial.Result.Success, Is.False);
            Assert.That(loader.UnloadedHandles, Is.EqualTo(loader.LoadedHandles.AsEnumerable().Reverse()));
            Assert.That(loader.UnloadedHandles, Has.Count.EqualTo(2));
            Assert.That(loader.LoadedHandles.All(handle => !handle.IsValid && handle.Root == null), Is.True);
            Assert.That(loader.ReleasedHandles, Is.EquivalentTo(loader.LoadedHandles));

            // 首次单块失败、不可取消最终清理成功：整体按最终物理结果成功。
            loader.ResetObservations();
            var load = mapSystem.LoadMapAsync(MapIds.HeJiaVillage).AsTask();
            yield return Await(load);
            Assert.That(load.Result.Success, Is.True, load.Result.Error);
            var children = ((MapContentGroupHandle)Game.Interface.GetModel<MapSceneModel>().CurrentHandle).Children.ToArray();
            var releases = new List<MapCurrentReleasedEvent>();
            var releasedAfterPhysicalCleanup = true;
            var unregister = Game.Interface.RegisterEvent<MapCurrentReleasedEvent>(evt =>
            {
                releasedAfterPhysicalCleanup &= children.All(child => !child.IsValid);
                releases.Add(evt);
            });
            loader.FailUnloadAtCall = 2;
            var unload = mapSystem.UnloadCurrentMapAsync().AsTask();
            yield return Await(unload);
            Assert.That(unload.Result.Success, Is.True, unload.Result.Error);
            Assert.That(loader.UnloadedHandles, Is.EqualTo(new[]
            {
                children[3], children[2], children[1], children[0], children[2]
            }));
            Assert.That(loader.UnloadedHandles.Distinct().Count(), Is.EqualTo(4));
            Assert.That(loader.ReleasedHandles.Distinct(), Is.EquivalentTo(children));
            Assert.That(children.All(child => !child.IsValid), Is.True);
            Assert.That(children.All(child => ((FakeMapHandle)child).Root == null), Is.True);
            Assert.That(releasedAfterPhysicalCleanup, Is.True);
            Assert.That(releases, Has.Count.EqualTo(1));
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.Null);

            // 外部取消发生在组卸载期间：所有底层卸载仍使用不可取消令牌并完成。
            releases.Clear();
            loader.ResetObservations();
            load = mapSystem.LoadMapAsync(MapIds.HeJiaVillage).AsTask();
            yield return Await(load);
            Assert.That(load.Result.Success, Is.True, load.Result.Error);
            children = ((MapContentGroupHandle)Game.Interface.GetModel<MapSceneModel>().CurrentHandle).Children.ToArray();
            using (var cts = new System.Threading.CancellationTokenSource())
            {
                loader.OnUnloadCall = (call, _, __) =>
                {
                    if (call == 1) cts.Cancel();
                };
                unload = mapSystem.UnloadCurrentMapAsync(cts.Token).AsTask();
                yield return Await(unload);
                Assert.That(unload.Result.Success, Is.True, unload.Result.Error);
            }
            Assert.That(loader.UnloadedHandles, Is.EqualTo(children.Reverse()));
            Assert.That(loader.UnloadTokensCanBeCanceled, Has.All.False);
            Assert.That(loader.ReleasedHandles.Distinct(), Is.EquivalentTo(children));
            Assert.That(children.All(child => !child.IsValid), Is.True);
            Assert.That(releases, Has.Count.EqualTo(1));
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.Null);

            // 最终清理持续失败：保留私有清理责任，不发布事件，不允许并存新地图。
            releases.Clear();
            loader.ResetObservations();
            load = mapSystem.LoadMapAsync(MapIds.HeJiaVillage).AsTask();
            yield return Await(load);
            Assert.That(load.Result.Success, Is.True, load.Result.Error);
            var group = (MapContentGroupHandle)Game.Interface.GetModel<MapSceneModel>().CurrentHandle;
            children = group.Children.ToArray();
            var persistent = children[1];
            loader.PersistentlyFailUnloadHandle = persistent;
            unload = mapSystem.UnloadCurrentMapAsync().AsTask();
            yield return Await(unload);
            Assert.That(unload.Result.Success, Is.False);
            Assert.That(unload.Result.Error, Does.Contain("最终清理失败"));
            Assert.That(loader.UnloadedHandles, Is.EqualTo(new[]
            {
                children[3], children[2], children[1], children[0], children[1]
            }));
            Assert.That(loader.ReleasedHandles.Distinct(), Is.EquivalentTo(new[]
            {
                children[3], children[2], children[0]
            }));
            Assert.That(persistent.IsValid, Is.True);
            Assert.That(((FakeMapHandle)persistent).Root, Is.Not.Null);
            Assert.That(children.Where(child => !ReferenceEquals(child, persistent)).All(child => !child.IsValid), Is.True);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.SameAs(group));
            Assert.That(Game.Interface.GetModel<MapSceneModel>().Status.Value, Is.EqualTo(MapLoadStatus.Failed));
            Assert.That(releases, Is.Empty);

            var loadCallCount = loader.LoadedAddresses.Count;
            var blocked = mapSystem.LoadMapAsync(MapIds.SurvivalSample).AsTask();
            yield return Await(blocked);
            Assert.That(blocked.Result.Success, Is.False);
            Assert.That(blocked.Result.Error, Does.Contain("待清理"));
            Assert.That(loader.LoadedAddresses, Has.Count.EqualTo(loadCallCount));
            Assert.That(releases, Is.Empty);

            // 解除故障后仍从公开卸载入口接管残留；成功后才释放事件并允许后续加载。
            loader.PersistentlyFailUnloadHandle = null;
            var retry = mapSystem.UnloadCurrentMapAsync().AsTask();
            yield return Await(retry);
            Assert.That(retry.Result.Success, Is.True, retry.Result.Error);
            Assert.That(persistent.IsValid, Is.False);
            Assert.That(((FakeMapHandle)persistent).Root, Is.Null);
            Assert.That(loader.ReleasedHandles.Distinct(), Is.EquivalentTo(children));
            Assert.That(releases, Has.Count.EqualTo(1));
            Assert.That(releasedAfterPhysicalCleanup, Is.True);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.Null);

            var next = mapSystem.LoadMapAsync(MapIds.SurvivalSample).AsTask();
            yield return Await(next);
            Assert.That(next.Result.Success, Is.True, next.Result.Error);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.EqualTo(MapIds.SurvivalSample));
            unregister.UnRegister();
        }

        private static IEnumerator Await(System.Threading.Tasks.Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                throw task.Exception;
            }
        }
    }
}
