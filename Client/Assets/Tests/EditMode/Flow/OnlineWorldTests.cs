using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using ET;
using Game.Network;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed partial class OnlineWorldTests
    {
        private const string MapId = "map.another-day.survival-sample";
        private WorldNetwork network;
        private WorldMapLoader mapLoader;
        private WorldResourceLoader resources;
        private CountingViewProvider views;
        private MutablePlayerPorts playerPorts;
        private TestInputReader input;
        private bool architectureDeinitialized;
        private string saveRoot;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            LifeCycle.ClearForTests();
            EditModeSceneIsolation.Enter();
            EditModeSceneIsolation.RequireActiveTestScene();
            network = new WorldNetwork(); mapLoader = new WorldMapLoader(); resources = new WorldResourceLoader();
            views = new CountingViewProvider(); playerPorts = new MutablePlayerPorts();
            input = new TestInputReader { MoveInput = Vector2.up, SprintHeld = true };
            ResourceLoader.SetLoaderForTests(resources);
            PlayerInputSystem.SetInputReaderFactoryForTests(() => input);
            Game.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<INetworkRuntime>(network);
                architecture.RegisterUtility<IMapContentLoader>(mapLoader);
                architecture.RegisterUtility<IRuntimeConfigProvider>(new TestRuntimeConfigProvider(includeMapPlayerSpawn: true));
                architecture.RegisterUtility<IViewInstanceProvider>(views);
                architecture.RegisterUtility<IPlayerRuntimePortProvider>(playerPorts);
                architecture.RegisterUtility<INetworkConfigProvider>(new WorldNetworkConfig());
                architecture.RegisterUtility<IOnlineAccountPreferenceStore>(new WorldPreferenceStore());
            };
            var camera = new GameObject("OnlineWorldTestCamera", typeof(Camera), typeof(AudioListener)); camera.tag = "MainCamera";
            _ = Game.Interface.GetSystem<OnlineWorldSystem>();
            saveRoot = Path.Combine(Path.GetTempPath(), "AnotherDayWorldBaseEntry", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(saveRoot);
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(saveRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (!architectureDeinitialized && Game.Interface is IArchitecture architecture) architecture.Deinit();
            Game.OnRegisterPatch = _ => { };
            PlayerInputSystem.SetInputReaderFactoryForTests(null);
            ResourceLoader.ResetForTests(); LifeCycle.ClearForTests(); EditModeSceneIsolation.Exit();
            if (!string.IsNullOrWhiteSpace(saveRoot) && Directory.Exists(saveRoot)) Directory.Delete(saveRoot, true);
        }

        [Test]
        public void ProtocolSnapshotMapsAllFields()
        {
            WorldPlayerInfo player = WirePlayer(7, 11, new Vector3(2, .1f, -14), 90, 5, 2);
            var wire = ET.OnlineWorldSnapshot.Create(); wire.WorldId = 1; wire.MapId = player.MapId; wire.WorldRevision = 9; wire.SelfUnitId = 11; wire.Players.Add(player);
            OnlineWorldSnapshot actual = OnlineWorldSystem.Copy(wire, false, OnlineWorldOperationState.Idle, OnlineWorldResultCode.Success);
            Assert.That(actual.WorldId, Is.EqualTo(1)); Assert.That(actual.SelfUnitId, Is.EqualTo(11)); Assert.That(actual.WorldRevision, Is.EqualTo(9)); Assert.That(actual.Players, Has.Count.EqualTo(1));
            Assert.That(actual.Players[0].LocalPosition, Is.EqualTo(new Vector3(2, .1f, -14))); Assert.That(actual.Players[0].YawDegrees, Is.EqualTo(90)); Assert.That(actual.Players[0].ConnectionState, Is.EqualTo(OnlineWorldConnectionState.DisconnectedGrace));
        }

        [TestCase(0, OnlineWorldResultCode.Success)] [TestCase(200105, OnlineWorldResultCode.NotLoggedIn)]
        [TestCase(200124, OnlineWorldResultCode.NotInWorld)] [TestCase(200125, OnlineWorldResultCode.OperationInvalid)]
        [TestCase(200126, OnlineWorldResultCode.StateConflict)] [TestCase(200123, OnlineWorldResultCode.ServiceUnavailable)]
        public void ServerErrorsMapToWorldResults(int error, OnlineWorldResultCode expected) => Assert.That(OnlineWorldSystem.MapError(error), Is.EqualTo(expected));

        [Test]
        public void SnapshotCopiesPlayersAndPreservesLocalUnitIsolation()
        {
            var players = new[] { Player(1, 500, Vector3.zero, 0, 1) }; var snapshot = Snapshot(500, 1, players); players[0] = default;
            Assert.That(snapshot.SelfUnitId, Is.EqualTo(500)); Assert.That(snapshot.Players[0].UnitId, Is.EqualTo(500)); Assert.That(PlayerModel.LocalPlayerUnitId, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator EnterGateRejectsOfflineMapAndOnlineModeCreatesPlayersOnly()
        {
            var worldSystem = Game.Interface.GetSystem<OnlineWorldSystem>(); var mapModel = Game.Interface.GetModel<MapSceneModel>();
            Task<OnlineWorldOperationResult> invalid = worldSystem.EnterWorldAsync(string.Empty); yield return Await(invalid);
            Assert.That(invalid.Result.Code, Is.EqualTo(OnlineWorldResultCode.OperationInvalid));
            Task<OnlineWorldOperationResult> unknown = worldSystem.EnterWorldAsync("map.unknown"); yield return Await(unknown);
            Assert.That(unknown.Result.Code, Is.EqualTo(OnlineWorldResultCode.OperationInvalid));
            Task<OnlineWorldOperationResult> baseMap = worldSystem.EnterWorldAsync(MapIds.Base); yield return Await(baseMap);
            Assert.That(baseMap.Result.Code, Is.EqualTo(OnlineWorldResultCode.OperationInvalid));
            Assert.That(network.PendingCount<C2G_EnterWorld>(), Is.Zero, "invalid map ids must not send Enter");
            var oldHandle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.Base, 41); mapModel.SetLoaded(MapIds.Base, oldHandle);
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            Task<OnlineWorldOperationResult> rejected = worldSystem.EnterWorldAsync(MapIds.SurvivalSample); yield return Await(rejected);
            Assert.That(rejected.Result.Code, Is.EqualTo(OnlineWorldResultCode.OperationInvalid)); Assert.That(network.PendingCount<C2G_EnterWorld>(), Is.Zero);
            Assert.That(mapModel.CurrentHandle, Is.SameAs(oldHandle)); Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Playing));
            Object.DestroyImmediate(oldHandle.Root); mapModel.Clear();

            Task<OnlineWorldOperationResult> entering = worldSystem.EnterWorldAsync(MapIds.SurvivalSample); yield return WaitUntil(() => network.PendingCount<C2G_EnterWorld>() == 1, "enter request");
            Assert.That(network.PendingRequest<C2G_EnterWorld>().MapId, Is.EqualTo(MapIds.SurvivalSample));
            ET.OnlineWorldSnapshot initial = WireSnapshot(501, 1, WirePlayer(11, 501, new Vector3(0, .1f, -14), 0, 1), WirePlayer(12, 502, new Vector3(2, .1f, -14), 45, 1));
            network.Complete<C2G_EnterWorld>(EnterResponse(initial)); yield return WaitUntil(() => network.PendingCount<C2G_GetCurrentWorld>() == 1, "final convergence request");
            network.Complete<C2G_GetCurrentWorld>(CurrentResponse(initial)); yield return Await(entering);
            Assert.That(entering.Result.Success, Is.True, entering.Result.Code.ToString()); Assert.That(mapModel.CurrentMode, Is.EqualTo(MapPreparationMode.OnlinePlayersOnly));
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Playing));
            Assert.That(Game.Interface.GetSystem<PlayerInputSystem>().SprintHeld, Is.True, "online world must preserve the shared Shift sprint intent");
            Assert.That(Game.Interface.GetModel<UnitModel>().Units.Keys, Is.EquivalentTo(new[] { PlayerModel.LocalPlayerUnitId }), "remote players must not enter UnitSystem");
            Assert.That(RemoteTransforms().Count, Is.EqualTo(1)); Assert.That(mapModel.CurrentHandle.Context.DynamicObjectRoot.childCount, Is.EqualTo(2));
            var offlineContext = new MapPreparationContext(MapId, 99, mapModel.CurrentHandle.Context, new[] { new MapSpawnDefinition("player.spawn", MapId, MapSpawnObjectType.Player, "player", 0, .1f, -14, 0) });
            Assert.That(offlineContext.Mode, Is.EqualTo(MapPreparationMode.OfflineFull));
        }

        [UnityTest]
        public IEnumerator EnterGateAllowsRecoverableStableBaseAfterFlushAndRejectsUnsafeOrMissingSaveStates()
        {
            OnlineWorldSystem system = Game.Interface.GetSystem<OnlineWorldSystem>();
            MapSceneModel map = Game.Interface.GetModel<MapSceneModel>();
            SaveModel save = Game.Interface.GetModel<SaveModel>();
            SaveSystem saveSystem = Game.Interface.GetSystem<SaveSystem>();
            saveSystem.FailNextOfflineStorageCleanupForTests();

            IMapContentHandle handle = PrepareStableBase(141);
            save.SetBase(new BaseSaveData(), false);
            yield return AssertEntryRejectedWithoutMutation(system, map, handle, "missing Base domain");

            handle = PrepareStableBase(142);
            save.ClearCurrent();
            yield return AssertEntryRejectedWithoutMutation(system, map, handle, "missing CurrentBase");

            foreach (MapLoadStatus status in new[] { MapLoadStatus.Loading, MapLoadStatus.Unloading, MapLoadStatus.Failed })
            {
                handle = PrepareStableBase(150 + (int)status);
                map.SetStatus(status);
                yield return AssertEntryRejectedWithoutMutation(system, map, handle, status.ToString());
            }

            FakeMapHandle invalidHandle = (FakeMapHandle)PrepareStableBase(160);
            invalidHandle.Invalidate();
            yield return AssertEntryRejectedWithoutMutation(system, map, invalidHandle, "invalid handle");

            if (map.CurrentHandle is FakeMapHandle invalidRoot && invalidRoot.Root != null) Object.DestroyImmediate(invalidRoot.Root);
            var contextlessHandle = new ContextlessMapHandle(MapIds.Base);
            save.SetBase(new BaseSaveData(), true);
            map.SetLoaded(MapIds.Base, contextlessHandle, MapPreparationMode.OfflineFull);
            yield return AssertEntryRejectedWithoutMutation(system, map, contextlessHandle, "missing context");

            handle = PrepareStableBase(170);
            map.SetLoaded(MapId, handle, MapPreparationMode.OfflineFull);
            yield return AssertEntryRejectedWithoutMutation(system, map, handle, "map id contradiction");

            handle = PrepareStableBase(171);
            map.SetLoaded(MapIds.Base, handle, MapPreparationMode.OnlinePlayersOnly);
            yield return AssertEntryRejectedWithoutMutation(system, map, handle, "preparation mode contradiction");

            IMapContentHandle baseHandle = PrepareStableBase(172);
            Task<OnlineWorldOperationResult> flushFailure = system.EnterWorldAsync(MapIds.SurvivalSample);
            yield return Await(flushFailure);
            Assert.That(flushFailure.Result.Code, Is.EqualTo(OnlineWorldResultCode.BaseSaveFailed),
                "invalid gates must not consume the injected Flush failure");
            Assert.That(network.PendingCount<C2G_EnterWorld>(), Is.Zero);
            Assert.That(map.CurrentHandle, Is.SameAs(baseHandle));

            saveSystem.MarkDirty("world base entry retry after gate matrix");

            Task<OnlineWorldOperationResult> entering = system.EnterWorldAsync(MapIds.SurvivalSample);
            yield return WaitUntil(() => network.PendingCount<C2G_EnterWorld>() == 1, "base enter request");
            ET.OnlineWorldSnapshot world = WireSnapshot(1501, 1, WirePlayer(111, 1501, Vector3.zero, 0, 1));
            network.Complete<C2G_EnterWorld>(EnterResponse(world));
            yield return WaitUntil(() => network.PendingCount<C2G_GetCurrentWorld>() == 1, "base convergence request");
            network.Complete<C2G_GetCurrentWorld>(CurrentResponse(world));
            yield return Await(entering);
            Assert.That(entering.Result.Success, Is.True, entering.Result.Code.ToString());
            Assert.That(map.CurrentMode, Is.EqualTo(MapPreparationMode.OnlinePlayersOnly));

            Task<OnlineWorldOperationResult> leaving = system.LeaveWorldAsync();
            yield return WaitUntil(() => network.PendingCount<C2G_LeaveWorld>() == 1, "base leave request");
            network.Complete<C2G_LeaveWorld>(LeaveResponse(0));
            yield return Await(leaving);
            Assert.That(leaving.Result.Success, Is.True, leaving.Result.Code.ToString());
            Assert.That(system.Snapshot.HasWorld, Is.False);
            Assert.That(map.CurrentMapId, Is.EqualTo(MapIds.Base));
            Assert.That(map.CurrentMode, Is.EqualTo(MapPreparationMode.OfflineFull));
            Assert.That(map.CurrentHandle, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator BaseSaveFailureKeepsMapAndDoesNotSendEnter()
        {
            OnlineWorldSystem system = Game.Interface.GetSystem<OnlineWorldSystem>();
            MapSceneModel map = Game.Interface.GetModel<MapSceneModel>();
            IMapContentHandle baseHandle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.Base, 143);
            map.SetLoaded(MapIds.Base, baseHandle, MapPreparationMode.OfflineFull);
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
            SaveSystem save = Game.Interface.GetSystem<SaveSystem>();
            save.MarkDirty("world base save failure test");
            save.FailNextOfflineStorageCleanupForTests();

            Task<OnlineWorldOperationResult> result = system.EnterWorldAsync(MapIds.SurvivalSample);
            yield return Await(result);

            Assert.That(result.Result.Code, Is.EqualTo(OnlineWorldResultCode.BaseSaveFailed));
            Assert.That(network.PendingCount<C2G_EnterWorld>(), Is.Zero);
            Assert.That(map.CurrentHandle, Is.SameAs(baseHandle));
            Assert.That(map.CurrentMapId, Is.EqualTo(MapIds.Base));
        }

        [UnityTest]
        public IEnumerator BaseOriginMapLoadFailurePreservesBaseAndCleansServerWorld()
        {
            IMapContentHandle baseHandle = PrepareStableBase(201);
            mapLoader.FailNextLoad = true;
            OnlineWorldSystem system = Game.Interface.GetSystem<OnlineWorldSystem>();
            Task<OnlineWorldOperationResult> entering = system.EnterWorldAsync(MapIds.SurvivalSample);
            yield return WaitUntil(() => network.PendingCount<C2G_EnterWorld>() == 1, "base candidate enter");
            network.Complete<C2G_EnterWorld>(EnterResponse(WireSnapshot(2101, 1, WirePlayer(201, 2101, Vector3.zero, 0, 1))));
            yield return WaitUntil(() => network.PendingCount<C2G_LeaveWorld>() == 1, "base candidate cleanup");
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.SameAs(baseHandle));
            network.Complete<C2G_LeaveWorld>(LeaveResponse(0));
            yield return Await(entering);
            Assert.That(entering.Result.Code, Is.EqualTo(OnlineWorldResultCode.MapLoadFailed));
            Assert.That(system.Snapshot.HasWorld, Is.False);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.SameAs(baseHandle));
            yield return EmptyOriginCycleNeverLoadsBase();
        }

        [UnityTest]
        public IEnumerator BaseOriginCleanupFailureKeepsIdentityAndRetryDoesNotUnloadBase()
        {
            IMapContentHandle baseHandle = PrepareStableBase(202);
            mapLoader.FailNextLoad = true;
            OnlineWorldSystem system = Game.Interface.GetSystem<OnlineWorldSystem>();
            Task<OnlineWorldOperationResult> entering = system.EnterWorldAsync(MapIds.SurvivalSample);
            yield return WaitUntil(() => network.PendingCount<C2G_EnterWorld>() == 1, "cleanup failure enter");
            network.Complete<C2G_EnterWorld>(EnterResponse(WireSnapshot(2201, 1, WirePlayer(202, 2201, Vector3.zero, 0, 1))));
            yield return WaitUntil(() => network.PendingCount<C2G_LeaveWorld>() == 1, "cleanup failure leave");
            network.Complete<C2G_LeaveWorld>(LeaveResponse(200123));
            yield return Await(entering);
            Assert.That(system.Snapshot.HasWorld, Is.True);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.SameAs(baseHandle));
            int unloads = mapLoader.UnloadCount;

            Task<OnlineWorldOperationResult> retry = system.LeaveWorldAsync();
            yield return WaitUntil(() => network.PendingCount<C2G_LeaveWorld>() == 1, "cleanup retry");
            network.Complete<C2G_LeaveWorld>(LeaveResponse(0));
            yield return Await(retry);
            Assert.That(retry.Result.Success, Is.True);
            Assert.That(system.Snapshot.HasWorld, Is.False);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.SameAs(baseHandle));
            Assert.That(mapLoader.UnloadCount, Is.EqualTo(unloads));
            yield return EmptyOriginCycleNeverLoadsBase();
        }

        [UnityTest]
        public IEnumerator BaseOriginLeaveBaseRestoreFailureReturnsMapLoadFailedWithNoWorld()
        {
            PrepareStableBase(203);
            ET.OnlineWorldSnapshot world = WireSnapshot(2301, 1, WirePlayer(203, 2301, Vector3.zero, 0, 1));
            yield return EnterWorld(world);
            mapLoader.FailNextLoad = true;
            OnlineWorldSystem system = Game.Interface.GetSystem<OnlineWorldSystem>();
            Task<OnlineWorldOperationResult> leaving = system.LeaveWorldAsync();
            yield return WaitUntil(() => network.PendingCount<C2G_LeaveWorld>() == 1, "restore failure leave");
            network.Complete<C2G_LeaveWorld>(LeaveResponse(0));
            yield return Await(leaving);
            Assert.That(leaving.Result.Code, Is.EqualTo(OnlineWorldResultCode.MapLoadFailed));
            Assert.That(system.Snapshot.HasWorld, Is.False);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.Null);
            yield return EmptyOriginCycleNeverLoadsBase();
        }

        [UnityTest]
        public IEnumerator UncertainBaseEntryThenConfirmedAbsentClearsReturnIntent()
        {
            PrepareStableBase(204);
            OnlineWorldSystem system = Game.Interface.GetSystem<OnlineWorldSystem>();
            Task<OnlineWorldOperationResult> entering = system.EnterWorldAsync(MapIds.SurvivalSample);
            yield return WaitUntil(() => network.PendingCount<C2G_EnterWorld>() == 1, "uncertain base enter");
            network.Fail<C2G_EnterWorld>(new TimeoutException("injected timeout"));
            yield return Await(entering);
            Assert.That(entering.Result.Code, Is.EqualTo(OnlineWorldResultCode.Timeout));

            Task<OnlineWorldOperationResult> restore = system.RestoreCurrentWorldAsync();
            yield return WaitUntil(() => network.PendingCount<C2G_GetCurrentWorld>() == 1, "confirmed absent query");
            network.Complete<C2G_GetCurrentWorld>(CurrentEmptyResponse());
            yield return Await(restore);
            Assert.That(restore.Result.Success, Is.True);
            yield return EmptyOriginCycleNeverLoadsBase();
        }

        [UnityTest]
        public IEnumerator FinalIdentityMismatchCleanupClearsIntentAfterBaseRestoreAttempt()
        {
            PrepareStableBase(205);
            OnlineWorldSystem system = Game.Interface.GetSystem<OnlineWorldSystem>();
            Task<OnlineWorldOperationResult> entering = system.EnterWorldAsync(MapIds.SurvivalSample);
            yield return WaitUntil(() => network.PendingCount<C2G_EnterWorld>() == 1, "mismatch enter");
            network.Complete<C2G_EnterWorld>(EnterResponse(WireSnapshot(2501, 1, WirePlayer(205, 2501, Vector3.zero, 0, 1))));
            yield return WaitUntil(() => network.PendingCount<C2G_GetCurrentWorld>() == 1, "mismatch convergence");
            network.Complete<C2G_GetCurrentWorld>(CurrentEmptyResponse());
            yield return Await(entering);
            Assert.That(entering.Result.Code, Is.EqualTo(OnlineWorldResultCode.StateConflict));
            Assert.That(system.Snapshot.HasWorld, Is.False);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.EqualTo(MapIds.Base));
            yield return EmptyOriginCycleNeverLoadsBase();
        }

        [UnityTest]
        public IEnumerator ConfirmedAccountCleanupClearsBaseReturnIntentAndReloginDoesNotRestore()
        {
            PrepareStableBase(206);
            yield return EnterWorld(WireSnapshot(2601, 1, WirePlayer(206, 2601, Vector3.zero, 0, 1)));
            Game.Interface.SendEvent(new OnlineAccountChangedEvent(new OnlineAccountSnapshot(
                OnlineConnectionState.Disconnected, OnlineOperationState.Idle, OnlineAccountResultCode.Success,
                string.Empty, 0, 0, 0, false, string.Empty)));
            yield return WaitUntil(() => !Game.Interface.GetSystem<OnlineWorldSystem>().Snapshot.HasWorld, "account cleanup");
            int baseLoads = mapLoader.BaseLoadCount;
            Game.Interface.SendEvent(new OnlineAccountChangedEvent(new OnlineAccountSnapshot(
                OnlineConnectionState.Authenticated, OnlineOperationState.Idle, OnlineAccountResultCode.Success,
                "relogin", 1, 2, 88, true, "relogin")));
            yield return WaitUntil(() => network.PendingCount<C2G_GetCurrentWorld>() == 1, "account relogin restore");
            network.Complete<C2G_GetCurrentWorld>(CurrentEmptyResponse());
            yield return WaitUntil(() => Game.Interface.GetSystem<OnlineWorldSystem>().Snapshot.OperationState == OnlineWorldOperationState.Idle, "account relogin complete");
            Assert.That(mapLoader.BaseLoadCount, Is.EqualTo(baseLoads));
            yield return EmptyOriginCycleNeverLoadsBase();
        }

        [UnityTest]
        public IEnumerator EmptyOriginLeaveNeverLoadsBase()
        {
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), false);
            yield return EmptyOriginCycleNeverLoadsBase();
        }

        [UnityTest]
        public IEnumerator DeinitThenReinitializeDoesNotRestoreBaseForEmptyOrigin()
        {
            PrepareStableBase(207);
            Task<OnlineWorldOperationResult> entering = Game.Interface.GetSystem<OnlineWorldSystem>().EnterWorldAsync(MapIds.SurvivalSample);
            yield return WaitUntil(() => network.PendingCount<C2G_EnterWorld>() == 1, "deinit intent enter");
            network.Fail<C2G_EnterWorld>(new TimeoutException("deinit uncertainty"));
            yield return Await(entering);

            if (Game.Interface is IArchitecture oldArchitecture) oldArchitecture.Deinit();
            architectureDeinitialized = true;
            TestReset.ResetArchitecture();
            architectureDeinitialized = false;
            _ = Game.Interface.GetSystem<OnlineWorldSystem>();
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(saveRoot);
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), false);
            yield return EmptyOriginCycleNeverLoadsBase();
        }

        private static readonly BaseIntentTerminalPath[] ConfirmedTerminalPaths =
        {
            BaseIntentTerminalPath.EnterResponseRejected,
            BaseIntentTerminalPath.RestoreConfirmedAbsent,
            BaseIntentTerminalPath.FinalIdentityMismatch,
            BaseIntentTerminalPath.ExplicitLeave,
            BaseIntentTerminalPath.AccountCleanup
        };

        [UnityTest]
        public IEnumerator BaseReturnIntentClearsAcrossAllConfirmedTerminalPaths()
        {
            foreach (BaseIntentTerminalPath terminalPath in ConfirmedTerminalPaths)
            {
                IEnumerator scenario = ExerciseConfirmedTerminalPath(terminalPath);
                while (scenario.MoveNext())
                {
                    yield return scenario.Current;
                }
            }
        }

        private IEnumerator ExerciseConfirmedTerminalPath(BaseIntentTerminalPath terminalPath)
        {
            PrepareStableBase(300 + (int)terminalPath);
            OnlineWorldSystem system = Game.Interface.GetSystem<OnlineWorldSystem>();

            if (terminalPath == BaseIntentTerminalPath.EnterResponseRejected)
            {
            Task<OnlineWorldOperationResult> entering = system.EnterWorldAsync(MapIds.SurvivalSample);
                yield return WaitUntil(() => network.PendingCount<C2G_EnterWorld>() == 1, "terminal rejected enter");
                var rejected = G2C_EnterWorld.Create();
                rejected.Error = 200125;
                network.Complete<C2G_EnterWorld>(rejected);
                yield return Await(entering);
                Assert.That(entering.Result.Code, Is.EqualTo(OnlineWorldResultCode.OperationInvalid));
            }
            else if (terminalPath == BaseIntentTerminalPath.RestoreConfirmedAbsent)
            {
            Task<OnlineWorldOperationResult> entering = system.EnterWorldAsync(MapIds.SurvivalSample);
                yield return WaitUntil(() => network.PendingCount<C2G_EnterWorld>() == 1, "terminal uncertain enter");
                network.Fail<C2G_EnterWorld>(new TimeoutException("terminal uncertainty"));
                yield return Await(entering);
                Task<OnlineWorldOperationResult> restore = system.RestoreCurrentWorldAsync();
                yield return WaitUntil(() => network.PendingCount<C2G_GetCurrentWorld>() == 1, "terminal absent restore");
                network.Complete<C2G_GetCurrentWorld>(CurrentEmptyResponse());
                yield return Await(restore);
            }
            else
            {
                ET.OnlineWorldSnapshot world = WireSnapshot(9300 + (int)terminalPath, 1,
                    WirePlayer(930 + (int)terminalPath, 9300 + (int)terminalPath, Vector3.zero, 0, 1));
                if (terminalPath == BaseIntentTerminalPath.FinalIdentityMismatch)
                {
            Task<OnlineWorldOperationResult> entering = system.EnterWorldAsync(MapIds.SurvivalSample);
                    yield return WaitUntil(() => network.PendingCount<C2G_EnterWorld>() == 1, "terminal mismatch enter");
                    network.Complete<C2G_EnterWorld>(EnterResponse(world));
                    yield return WaitUntil(() => network.PendingCount<C2G_GetCurrentWorld>() == 1, "terminal mismatch convergence");
                    network.Complete<C2G_GetCurrentWorld>(CurrentEmptyResponse());
                    yield return Await(entering);
                    Assert.That(entering.Result.Code, Is.EqualTo(OnlineWorldResultCode.StateConflict));
                }
                else
                {
                    yield return EnterWorld(world);
                    if (terminalPath == BaseIntentTerminalPath.ExplicitLeave)
                    {
                        Task<OnlineWorldOperationResult> leaving = system.LeaveWorldAsync();
                        yield return WaitUntil(() => network.PendingCount<C2G_LeaveWorld>() == 1, "terminal explicit leave");
                        network.Complete<C2G_LeaveWorld>(LeaveResponse(0));
                        yield return Await(leaving);
                        Assert.That(leaving.Result.Success, Is.True);
                    }
                    else
                    {
                        Game.Interface.SendEvent(new OnlineAccountChangedEvent(new OnlineAccountSnapshot(
                            OnlineConnectionState.Disconnected, OnlineOperationState.Idle,
                            OnlineAccountResultCode.Success, string.Empty, 0, 0, 0, false, string.Empty)));
                        yield return WaitUntil(() => !system.Snapshot.HasWorld, "terminal account cleanup");
                    }
                }
            }

            Assert.That(system.Snapshot.HasWorld, Is.False);
            yield return EmptyOriginCycleNeverLoadsBase();
        }

        [UnityTest]
        public IEnumerator LoadingPushesBufferFinalSnapshotConvergesAndStaleRevisionsAreIgnored()
        {
            mapLoader.DelayNextLoad = true; OnlineWorldSystem system = Game.Interface.GetSystem<OnlineWorldSystem>();
            Task<OnlineWorldOperationResult> entering = system.EnterWorldAsync(MapIds.SurvivalSample); yield return WaitUntil(() => network.PendingCount<C2G_EnterWorld>() == 1, "enter request");
            ET.OnlineWorldSnapshot candidate = WireSnapshot(601, 1, WirePlayer(21, 601, Vector3.zero, 0, 1), WirePlayer(22, 602, Vector3.right, 0, 1));
            network.Complete<C2G_EnterWorld>(EnterResponse(candidate)); yield return WaitUntil(() => mapLoader.HasDelayedLoad, "delayed map load");
            network.Emit(EnteredPush(2, WirePlayer(23, 603, Vector3.right * 3, 10, 1))); network.Emit(TransformPush(3, WirePlayer(23, 603, Vector3.right * 4, 20, 2))); network.Emit(LeftPush(4, 602));
            Assert.That(system.Snapshot.Players.Select(x => x.UnitId), Is.EquivalentTo(new long[] { 601, 603 })); Assert.That(RemoteTransforms(), Is.Empty);
            mapLoader.CompleteDelayedLoad(); yield return WaitUntil(() => network.PendingCount<C2G_GetCurrentWorld>() == 1, "final convergence request");
            ET.OnlineWorldSnapshot final = WireSnapshot(601, 5, WirePlayer(21, 601, Vector3.zero, 0, 1), WirePlayer(24, 604, Vector3.forward * 5, 30, 1));
            network.Complete<C2G_GetCurrentWorld>(CurrentResponse(final)); yield return Await(entering);
            Assert.That(entering.Result.Success, Is.True); Assert.That(system.Snapshot.Players.Select(x => x.UnitId), Is.EquivalentTo(new long[] { 601, 604 }));
            Assert.That(RemoteTransforms().Count, Is.EqualTo(1));
            network.Emit(LeftPush(4, 604)); network.Emit(TransformPush(6, WirePlayer(24, 604, Vector3.one * 99, 180, 0)));
            Assert.That(system.Snapshot.Players.Single(x => x.UnitId == 604).LocalPosition, Is.EqualTo(Vector3.forward * 5));
            network.Emit(TransformPush(6, WirePlayer(24, 604, Vector3.one * 7, 75, 2))); Assert.That(system.Snapshot.Players.Single(x => x.UnitId == 604).LocalPosition, Is.EqualTo(Vector3.one * 7));
            network.Emit(TransformPush(5, WirePlayer(24, 604, Vector3.one * 8, 80, 3))); Assert.That(system.Snapshot.Players.Single(x => x.UnitId == 604).LocalPosition, Is.EqualTo(Vector3.one * 7));
        }

        [UnityTest]
        public IEnumerator DisconnectCallbacksPreserveWorldAndReloginRestoresSameUnit()
        {
            yield return LoginAndRestoreEmpty(29);
            ET.OnlineWorldSnapshot world = WireSnapshot(701, 1, WirePlayer(31, 701, Vector3.zero, 0, 1), WirePlayer(32, 702, Vector3.right * 2, 0, 1));
            yield return EnterWorld(world);
            OnlineWorldSystem system = Game.Interface.GetSystem<OnlineWorldSystem>();
            MapSceneModel map = Game.Interface.GetModel<MapSceneModel>();
            IMapContentHandle originalHandle = map.CurrentHandle;
            Transform oldRoot = originalHandle.Context.DynamicObjectRoot;
            OnlineRemotePlayerViewCtrl oldRemoteView = RemoteView(702);
            GameObject oldRemoteInstance = oldRemoteView.Instance;
            object oldRemoteHandle = RemoteRawHandle(702);
            long mapInstanceId = Game.Interface.GetSystem<MapSceneSystem>().CurrentMapInstanceId;
            Assert.That(ActiveContext<Transform>("activeRoot"), Is.SameAs(oldRoot));
            Assert.That(ActiveContext<long>("activeMapInstanceId"), Is.EqualTo(mapInstanceId));
            Assert.That(ActiveContext<long>("activeWorldId"), Is.EqualTo(1));
            Assert.That(ActiveContext<string>("activeMapId"), Is.EqualTo(MapId));
            Assert.That(ActiveContext<int>("activeGeneration"), Is.GreaterThan(0));
            playerPorts.Pose = new PlayerMapLocalPoseSnapshot(PlayerModel.LocalPlayerUnitId, mapInstanceId, new Vector3(1, 0, 2), 15, true);
            LifeCycle.Update();
            yield return WaitUntil(() => network.PendingCount<C2G_ReportWorldTransform>() == 1, "first report");
            network.Complete<C2G_ReportWorldTransform>(ReportResponse(701, 1));
            yield return null;
            int reportsBeforeDisconnect = network.ReportRequests.Count;
            int movesBeforeDisconnect = playerPorts.Motor.MoveCount;

            network.RaiseDisconnected(NetworkDisconnectReason.RemoteClosed);
            yield return null;
            Assert.That(system.Snapshot.HasWorld, Is.True);
            Assert.That(system.Snapshot.SelfUnitId, Is.EqualTo(701));
            Assert.That(system.Snapshot.MapId, Is.EqualTo(MapId));
            Assert.That(system.Snapshot.Players.Select(player => player.UnitId), Is.EquivalentTo(new long[] { 701, 702 }));
            Assert.That(system.Snapshot.ConnectionLost, Is.True);
            Assert.That(system.Snapshot.OperationState, Is.EqualTo(OnlineWorldOperationState.Idle));
            Assert.That(map.CurrentHandle, Is.SameAs(originalHandle));
            LifeCycle.Update();
            Assert.That(playerPorts.Motor.MoveCount, Is.EqualTo(movesBeforeDisconnect));
            Assert.That(network.ReportRequests, Has.Count.EqualTo(reportsBeforeDisconnect));

            Task<OnlineAccountOperationResult> relogin = Game.Interface.GetSystem<OnlineAccountSystem>().LoginAsync("world_test", "Pass_123");
            yield return WaitUntil(() => network.PendingCount<C2G_Login>() == 1, "relogin request");
            network.Complete<C2G_Login>(LoginResponse(30));
            yield return Await(relogin);
            yield return WaitUntil(() => network.PendingCount<C2G_GetCurrentWorld>() == 1, "restore current world");
            Vector3 restoredPosition = new Vector3(5, 0.1f, -8);
            ET.OnlineWorldSnapshot restored = WireSnapshot(701, 2, WirePlayer(31, 701, restoredPosition, 35, 2), WirePlayer(32, 702, Vector3.right * 3, 15, 2));
            network.Complete<C2G_GetCurrentWorld>(CurrentResponse(restored));
            yield return WaitUntil(() => network.PendingCount<C2G_GetCurrentWorld>() == 1, "restore convergence");
            network.Complete<C2G_GetCurrentWorld>(CurrentResponse(restored));
            yield return WaitUntil(() => system.Snapshot.OperationState == OnlineWorldOperationState.Idle, "restore completion");
            Assert.That(system.Snapshot.HasWorld, Is.True);
            Assert.That(system.Snapshot.SelfUnitId, Is.EqualTo(701));
            Assert.That(system.Snapshot.ConnectionLost, Is.False);
            Assert.That((LocalPlayerTransform().localPosition - restoredPosition).sqrMagnitude, Is.LessThan(0.000001f));
            IMapContentHandle replacementHandle = map.CurrentHandle;
            Transform replacementRoot = replacementHandle.Context.DynamicObjectRoot;
            Assert.That(replacementHandle, Is.Not.SameAs(originalHandle));
            Assert.That(replacementRoot, Is.Not.SameAs(oldRoot));
            Assert.That(oldRemoteInstance == null, Is.True, "old active remote instance must be released during map replacement");
            Assert.That(resources.ReleaseCount(oldRemoteHandle), Is.EqualTo(1));

            OnlineRemotePlayerViewCtrl replacementRemoteView = RemoteView(702);
            GameObject replacementRemoteInstance = replacementRemoteView.Instance;
            object replacementRemoteHandle = RemoteRawHandle(702);
            Assert.That(replacementRemoteInstance.transform.parent, Is.SameAs(replacementRoot));
            Assert.That(ActiveContext<Transform>("activeRoot"), Is.SameAs(replacementRoot));
            Assert.That(ActiveContext<long>("activeMapInstanceId"), Is.EqualTo(Game.Interface.GetSystem<MapSceneSystem>().CurrentMapInstanceId));
            Assert.That(ActiveContext<long>("activeWorldId"), Is.EqualTo(1));
            Assert.That(ActiveContext<string>("activeMapId"), Is.EqualTo(MapId));
            Assert.That(ActiveContext<int>("activeGeneration"), Is.GreaterThan(0));

            Vector3 pushedTarget = new Vector3(11, 0.1f, 6);
            network.Emit(TransformPush(3, WirePlayer(32, 702, pushedTarget, 85, 3)));
            Vector3 pushStart = replacementRemoteInstance.transform.localPosition;
            Assert.That((pushStart - pushedTarget).sqrMagnitude, Is.GreaterThan(0.000001f));
            replacementRemoteView.Tick(0.05f);
            Assert.That((replacementRemoteInstance.transform.localPosition - pushStart).sqrMagnitude, Is.GreaterThan(0.000001f));
            Assert.That((replacementRemoteInstance.transform.localPosition - pushedTarget).sqrMagnitude, Is.GreaterThan(0.000001f));
            replacementRemoteView.Tick(0.05f);
            Assert.That((replacementRemoteInstance.transform.localPosition - pushedTarget).sqrMagnitude, Is.LessThan(0.000001f));

            network.Emit(LeftPush(4, 702));
            Assert.That(replacementRemoteInstance == null, Is.True);
            Assert.That(resources.ReleaseCount(replacementRemoteHandle), Is.EqualTo(1));
            Assert.That(HasActiveRemote(702), Is.False);

            resources.DelayNextGameObjectLoad = true;
            network.Emit(EnteredPush(5, WirePlayer(33, 703, Vector3.right * 4, 0, 1)));
            yield return WaitUntil(() => resources.HasDelayedLoad, "late active remote load");
            object lateHandle = resources.DelayedRawHandle;
            network.Emit(LeftPush(6, 703));
            resources.CompleteDelayedLoad();
            yield return null;
            yield return null;
            Assert.That(resources.ReleaseCount(lateHandle), Is.EqualTo(1));
            Assert.That(HasActiveRemote(703), Is.False);

            playerPorts.Pose = new PlayerMapLocalPoseSnapshot(PlayerModel.LocalPlayerUnitId, Game.Interface.GetSystem<MapSceneSystem>().CurrentMapInstanceId, new Vector3(8, 0, 9), 95, true);
            yield return WaitUntil(() => { LifeCycle.Update(); return network.ReportRequests.Count > reportsBeforeDisconnect; }, "report resumed");
        }

        [UnityTest]
        public IEnumerator MapLoadFailureAndServerCleanupFailureFreezeIdentityUntilLeaveRetry()
        {
            yield return LoginAndRestoreEmpty(29);
            resources.FailGameObjectLoadAt = 2;
            ET.OnlineWorldSnapshot world = WireSnapshot(801, 1, WirePlayer(41, 801, Vector3.zero, 0, 1), WirePlayer(42, 802, Vector3.right * 2, 0, 1));
            OnlineWorldSystem system = Game.Interface.GetSystem<OnlineWorldSystem>();
            Task<OnlineWorldOperationResult> entering = system.EnterWorldAsync(MapIds.SurvivalSample);
            yield return WaitUntil(() => network.PendingCount<C2G_EnterWorld>() == 1, "enter request");
            network.Complete<C2G_EnterWorld>(EnterResponse(world));
            yield return WaitUntil(() => network.PendingCount<C2G_LeaveWorld>() == 1, "cleanup leave request");
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.Null);
            Assert.That(RemoteTransforms(), Is.Empty);
            Assert.That(resources.ReleaseCalls, Is.GreaterThanOrEqualTo(1));
            Assert.That(views.LiveInstances, Is.Zero, "failed public map transaction leaked a view instance");
            Assert.That(ActiveEntries(Game.Interface.GetSystem<MapSceneSystem>().OnlineWorldPlayers).Count, Is.Zero);
            network.Complete<C2G_LeaveWorld>(LeaveResponse(200123));
            yield return Await(entering);
            Assert.That(entering.Result.Code, Is.EqualTo(OnlineWorldResultCode.ServiceUnavailable));
            Assert.That(system.Snapshot.HasWorld, Is.True);
            Assert.That(system.Snapshot.SelfUnitId, Is.EqualTo(801));
            Assert.That(system.Snapshot.ConnectionLost, Is.True);
            LifeCycle.Update();
            Assert.That(network.PendingCount<C2G_ReportWorldTransform>(), Is.Zero);

            Task<OnlineWorldOperationResult> retry = system.LeaveWorldAsync();
            yield return WaitUntil(() => network.PendingCount<C2G_LeaveWorld>() == 1, "leave retry");
            network.Complete<C2G_LeaveWorld>(LeaveResponse(0));
            yield return Await(retry);
            Assert.That(retry.Result.Success, Is.True);
            Assert.That(system.Snapshot.HasWorld, Is.False);
        }

        [UnityTest]
        public IEnumerator ServerLeaveThenLocalUnloadFailureRemainsFrozenUntilNotInWorldRetry()
        {
            yield return LoginAndRestoreEmpty(29);
            ET.OnlineWorldSnapshot world = WireSnapshot(901, 1, WirePlayer(51, 901, Vector3.zero, 0, 1), WirePlayer(52, 902, Vector3.right * 2, 0, 1));
            yield return EnterWorld(world);
            OnlineWorldSystem system = Game.Interface.GetSystem<OnlineWorldSystem>();
            MapSceneModel map = Game.Interface.GetModel<MapSceneModel>();
            IMapContentHandle committedHandle = map.CurrentHandle;
            mapLoader.FailNextUnload = true;
            Task<OnlineWorldOperationResult> first = system.LeaveWorldAsync();
            yield return WaitUntil(() => network.PendingCount<C2G_LeaveWorld>() == 1, "first leave");
            network.Complete<C2G_LeaveWorld>(LeaveResponse(0));
            yield return Await(first);
            Assert.That(first.Result.Code, Is.EqualTo(OnlineWorldResultCode.MapLoadFailed));
            Assert.That(system.Snapshot.HasWorld, Is.True);
            Assert.That(system.Snapshot.ConnectionLost, Is.True);
            Assert.That(map.CurrentHandle, Is.SameAs(committedHandle));

            Task<OnlineWorldOperationResult> second = system.LeaveWorldAsync();
            yield return WaitUntil(() => network.PendingCount<C2G_LeaveWorld>() == 1, "second leave");
            network.Complete<C2G_LeaveWorld>(LeaveResponse(200124));
            yield return Await(second);
            Assert.That(second.Result.Success, Is.True);
            Assert.That(system.Snapshot.HasWorld, Is.False);
            Assert.That(map.CurrentHandle, Is.Null);
        }

        [UnityTest]
        public IEnumerator RemoteTransformPushInterpolatesActiveInstanceToTarget()
        {
            yield return LoginAndRestoreEmpty(29);
            Vector3 oldPosition = Vector3.right * 2;
            Vector3 target = new Vector3(10, 0, 4);
            ET.OnlineWorldSnapshot world = WireSnapshot(1001, 1, WirePlayer(61, 1001, Vector3.zero, 0, 1), WirePlayer(62, 1002, oldPosition, 0, 1));
            yield return EnterWorld(world);
            OnlineRemotePlayerViewCtrl remoteView = RemoteView(1002);
            Transform remote = remoteView.Instance.transform;
            Assert.That(remote.localPosition, Is.EqualTo(oldPosition));
            network.Emit(TransformPush(2, WirePlayer(62, 1002, target, 90, 2)));
            Assert.That(remote.localPosition, Is.EqualTo(oldPosition), "push must feed the production interpolator instead of snapping");
            remoteView.Tick(0.05f);
            Assert.That((remote.localPosition - oldPosition).sqrMagnitude, Is.GreaterThan(0.000001f));
            Assert.That((remote.localPosition - target).sqrMagnitude, Is.GreaterThan(0.000001f));
            remoteView.Tick(0.05f);
            Assert.That((remote.localPosition - target).sqrMagnitude, Is.LessThan(0.000001f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RestoredAttackSequenceContinuesFromAuthorityAndNeverMovesBackward()
        {
            WorldPlayerInfo self = WirePlayer(71, 1101, Vector3.zero, 0, 1);
            self.CurrentHP = self.MaxHP = 100;
            self.LifecycleRevision = 3;
            self.LastAcceptedAttackSequence = 41;
            yield return EnterWorld(WireSnapshot(1101, 1, self));
            OnlineWorldSystem system = Game.Interface.GetSystem<OnlineWorldSystem>();

            Assert.That(system.TryRequestMeleeAttack(0, Vector3.forward), Is.True);
            yield return WaitUntil(() => network.PendingCount<C2G_WorldMeleeAttack>() == 1, "restored attack request");
            Assert.That(network.AttackRequests.Last().AttackSequence, Is.EqualTo(42));
            network.Complete<C2G_WorldMeleeAttack>(MeleeResponse(42, 3));
            yield return WaitUntil(() => !PrivateBool(system, "attackInFlight"), "first restored attack completion");

            WorldPlayerInfo stale = WirePlayer(71, 1101, Vector3.zero, 0, 2);
            stale.CurrentHP = stale.MaxHP = 100;
            stale.LifecycleRevision = 3;
            stale.LastAcceptedAttackSequence = 10;
            network.Emit(CombatPush(2, 0, 0, stale));
            Assert.That(system.TryRequestMeleeAttack(0, Vector3.forward), Is.True);
            yield return WaitUntil(() => network.PendingCount<C2G_WorldMeleeAttack>() == 1, "second attack request");
            Assert.That(network.AttackRequests.Last().AttackSequence, Is.EqualTo(43));
            network.Complete<C2G_WorldMeleeAttack>(MeleeResponse(43, 3));
        }

        [UnityTest]
        public IEnumerator UnknownTransformSchedulesOnlyOneCurrentWorldConvergence()
        {
            yield return EnterWorld(WireSnapshot(1201, 1, WirePlayer(81, 1201, Vector3.zero, 0, 1)));
            network.Emit(TransformPush(2, WirePlayer(82, 1202, Vector3.one, 0, 2)));
            network.Emit(TransformPush(3, WirePlayer(83, 1203, Vector3.right, 0, 3)));
            yield return WaitUntil(() => network.PendingCount<C2G_GetCurrentWorld>() == 1, "single convergence request");
            Assert.That(network.PendingCount<C2G_GetCurrentWorld>(), Is.EqualTo(1));
            network.Complete<C2G_GetCurrentWorld>(CurrentResponse(
                WireSnapshot(1201, 4, WirePlayer(81, 1201, Vector3.zero, 0, 4), WirePlayer(82, 1202, Vector3.one, 0, 4))));
            OnlineWorldSystem system = Game.Interface.GetSystem<OnlineWorldSystem>();
            yield return WaitUntil(() => system.Snapshot.Players.Any(x => x.UnitId == 1202), "convergence snapshot applied");
            Assert.That(system.Snapshot.Players.Select(x => x.UnitId),
                Is.EquivalentTo(new long[] { 1201, 1202 }));
        }

        [UnityTest]
        public IEnumerator RemotePlayerHurtFeedbackPlaysOnceForAuthoritativeHealthDrop()
        {
            WorldPlayerInfo local = WirePlayer(91, 1301, Vector3.zero, 0, 1);
            local.CurrentHP = local.MaxHP = 100; local.LifecycleRevision = 1;
            WorldPlayerInfo remote = WirePlayer(92, 1302, Vector3.right, 0, 1);
            remote.CurrentHP = remote.MaxHP = 100; remote.LifecycleRevision = 1;
            yield return EnterWorld(WireSnapshot(1301, 1, local, remote));
            OnlineRemotePlayerViewCtrl remoteView = RemoteView(1302);
            PlayerView playerView = remoteView.Instance.GetComponent<PlayerView>();
            Assert.That(PrivateFloat(playerView, "colorFeedbackRemainingSeconds"), Is.Zero, "initial snapshot must stay silent");

            WorldPlayerInfo damaged = WirePlayer(92, 1302, Vector3.right, 0, 2);
            damaged.CurrentHP = 90; damaged.MaxHP = 100; damaged.LifecycleRevision = 1;
            G2C_WorldCombatChanged push = CombatPush(2, 2, 999, damaged);
            network.Emit(push);
            Assert.That(PrivateFloat(playerView, "colorFeedbackRemainingSeconds"), Is.GreaterThan(0f));

            remoteView.Tick(1f);
            Assert.That(PrivateFloat(playerView, "colorFeedbackRemainingSeconds"), Is.LessThanOrEqualTo(0f));
            network.Emit(push);
            Assert.That(PrivateFloat(playerView, "colorFeedbackRemainingSeconds"), Is.LessThanOrEqualTo(0f), "duplicate state replayed hurt feedback");
            WorldPlayerInfo older = WirePlayer(92, 1302, Vector3.right, 0, 1);
            older.CurrentHP = 80; older.MaxHP = 100; older.LifecycleRevision = 1;
            network.Emit(CombatPush(3, 2, 999, older));
            Assert.That(PrivateFloat(playerView, "colorFeedbackRemainingSeconds"), Is.LessThanOrEqualTo(0f), "older state replayed hurt feedback");
        }

        [UnityTest]
        public IEnumerator NavigationExecutorTakeoverBatchesChangesSendsEmptyHeartbeatAndKeepsSingleRequestInFlight()
        {
            WorldPlayerInfo self = WirePlayer(101, 1401, Vector3.zero, 0, 1);
            self.CurrentHP = self.MaxHP = 100; self.LifecycleRevision = 1;
            ET.OnlineWorldSnapshot wire = WireSnapshot(1401, 1, self);
            wire.NavigationExecutorUnitId = 1401; wire.NavigationLeaseRevision = 7;
            yield return EnterWorld(wire);
            OnlineWorldSystem worldSystem = Game.Interface.GetSystem<OnlineWorldSystem>();
            OnlineWorldSnapshot snapshot = worldSystem.Snapshot;
            var controller = new OnlineWorldEnemyCtrl(Game.Interface);
            var first = new OnlineWorldEnemyNavigationSample(501, 1, Vector3.zero, 0f);
            var changed = new OnlineWorldEnemyNavigationSample(501, 1, Vector3.right, 10f);
            try
            {
                controller.ObserveNavigationLeaseForTests(snapshot);
                Assert.That(controller.SampleAndReportNavigation(snapshot, 100, new[] { first }), Is.True);
                Assert.That(network.NavigationRequests, Has.Count.EqualTo(1));
                Assert.That(network.NavigationRequests[0].Reports, Has.Count.EqualTo(1));
                Assert.That(controller.SampleAndReportNavigation(snapshot, 200, new[] { changed }), Is.False, "second navigation RPC entered flight");
                Assert.That(network.NavigationRequests, Has.Count.EqualTo(1));
                network.Complete<C2G_ReportWorldEnemyNavigation>(NavigationResponse(1));
                yield return WaitUntil(() => !PrivateBool(worldSystem, "navigationInFlight"), "first navigation request completion");

                Assert.That(controller.SampleAndReportNavigation(snapshot, 201, new[] { first }), Is.False, "unchanged pose was reported");
                Assert.That(network.NavigationRequests, Has.Count.EqualTo(1));
                Assert.That(controller.SampleAndReportNavigation(snapshot, 500, new[] { first }), Is.True, "500ms empty heartbeat was not sent");
                Assert.That(network.NavigationRequests.Last().Reports, Is.Empty);
                network.Complete<C2G_ReportWorldEnemyNavigation>(NavigationResponse(2));
                yield return WaitUntil(() => !PrivateBool(worldSystem, "navigationInFlight"), "empty heartbeat completion");

                network.Emit(LeasePush(2, 999, 8));
                OnlineWorldSnapshot follower = worldSystem.Snapshot;
                controller.ObserveNavigationLeaseForTests(follower);
                network.Emit(LeasePush(3, snapshot.SelfUnitId, 9));
                OnlineWorldSnapshot takeover = worldSystem.Snapshot;
                controller.ObserveNavigationLeaseForTests(takeover);
                Assert.That(controller.SampleAndReportNavigation(takeover, 100, new[] { first }), Is.True, "takeover did not reset the confirmed-pose report baseline");
                Assert.That(network.NavigationRequests.Last().NavigationLeaseRevision, Is.EqualTo(9));
                Assert.That(network.NavigationRequests.Last().Reports, Has.Count.EqualTo(1));
                network.Complete<C2G_ReportWorldEnemyNavigation>(NavigationResponse(1));
            }
            finally
            {
                controller.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator LocalAuthoritativeDeathStopsInputRespawnSnapsAndDoesNotPolluteOfflineHealthOrSave()
        {
            CombatModel combat = Game.Interface.GetModel<CombatModel>();
            combat.ApplyPlayerMaxHP(100); combat.SetPlayerHP(77);
            SaveModel save = Game.Interface.GetModel<SaveModel>();
            save.SetBase(new BaseSaveData(), false);
            BaseSaveData preservedBase = save.CurrentBase;
            WorldPlayerInfo alive = WirePlayer(111, 1501, Vector3.zero, 0, 1);
            alive.CurrentHP = alive.MaxHP = 100; alive.LifecycleRevision = 1;
            yield return EnterWorld(WireSnapshot(1501, 1, alive));
            long mapInstanceId = Game.Interface.GetSystem<MapSceneSystem>().CurrentMapInstanceId;
            playerPorts.Pose = new PlayerMapLocalPoseSnapshot(PlayerModel.LocalPlayerUnitId, mapInstanceId, Vector3.zero, 0, true);
            LifeCycle.Update();
            CountingMotor motor = playerPorts.Motor;
            int movesBeforeDeath = motor.MoveCount;
            int attacksBeforeDeath = network.AttackRequests.Count;

            WorldPlayerInfo dead = WirePlayer(111, 1501, new Vector3(2, .1f, 3), 25, 2);
            dead.CurrentHP = 0; dead.MaxHP = 100; dead.IsDead = true; dead.RespawnAtServerTime = 999; dead.LifecycleRevision = 2;
            network.Emit(CombatPush(2, 2, 9001, dead));
            input.AttackPressedThisFrame = true;
            LifeCycle.Update();
            input.AttackPressedThisFrame = false;
            Assert.That(motor.MoveCount, Is.EqualTo(movesBeforeDeath), "authoritative death did not stop movement input");
            Assert.That(network.AttackRequests, Has.Count.EqualTo(attacksBeforeDeath), "authoritative death did not stop attack input");
            Assert.That(Game.Interface.GetSystem<OnlineWorldSystem>().TryRequestMeleeAttack(0, Vector3.forward), Is.False);
            Assert.That(combat.PlayerHP.Value, Is.EqualTo(77), "online death polluted offline CombatModel health");
            Assert.That(save.CurrentBase, Is.SameAs(preservedBase), "online death replaced the offline save root");
            Assert.That(save.CurrentDomain, Is.EqualTo(OfflineSaveDomain.None));

            Vector3 respawnPosition = new Vector3(7, .1f, -6);
            WorldPlayerInfo respawned = WirePlayer(111, 1501, respawnPosition, 80, 3);
            respawned.CurrentHP = respawned.MaxHP = 100; respawned.LifecycleRevision = 3;
            network.Emit(CombatPush(3, 0, 0, respawned));
            Assert.That((LocalPlayerTransform().localPosition - respawnPosition).sqrMagnitude, Is.LessThan(0.000001f), "authoritative respawn did not snap local player");
            input.AttackPressedThisFrame = true;
            LifeCycle.Update();
            input.AttackPressedThisFrame = false;
            yield return WaitUntil(() => network.PendingCount<C2G_WorldMeleeAttack>() == 1, "attack input after respawn");
            Assert.That(network.AttackRequests.Last().LifecycleRevision, Is.EqualTo(3));
            network.Complete<C2G_WorldMeleeAttack>(MeleeResponse(network.AttackRequests.Last().AttackSequence, 3));
            yield return WaitUntil(() => !PrivateBool(Game.Interface.GetSystem<OnlineWorldSystem>(), "attackInFlight"), "respawn attack completion");
            Assert.That(combat.PlayerHP.Value, Is.EqualTo(77));
            Assert.That(save.CurrentBase, Is.SameAs(preservedBase));
        }

        [UnityTest]
        public IEnumerator MobileUnarmedAttackIgnoresOfflineZeroHealthAndPlaysFeedback()
        {
            WorldPlayerInfo alive = WirePlayer(111, 1501, Vector3.zero, 0, 1);
            alive.CurrentHP = alive.MaxHP = 100;
            alive.LifecycleRevision = 1;
            yield return EnterWorld(WireSnapshot(1501, 1, alive));
            var combat = Game.Interface.GetModel<CombatModel>();
            combat.SetPlayerHP(0);
            input.MoveInput = Vector2.zero;
            input.MeleeSlotPressedThisFrame = input.RangedSlotPressedThisFrame = input.DestroyPressedThisFrame = false;
            var mobile = Game.Interface.GetSystem<MobileInputSystem>();
            mobile.TrySetEditorSimulation(true);
            Game.Interface.GetModel<PlayerCameraModeModel>().Mode = PlayerCameraMode.Shoulder;
            var motor = playerPorts.Motor;
            Assert.That(mobile.RequestShoulderAttack(), Is.True, "服务器角色存活时应接受手机按钮点击");
            LifeCycle.Update();
            Assert.That(network.AttackRequests, Has.Count.EqualTo(1), "离线零血量不得拦截联机空手攻击请求");
            Assert.That(network.AttackRequests[0].WeaponItemId, Is.Zero);
            Assert.That(motor.AttackFeedbackCount, Is.EqualTo(1), "请求发出后应播放空手动作");
            Assert.That(combat.PlayerHP.Value, Is.Zero, "联机攻击不得修改离线生命");
            network.Complete<C2G_WorldMeleeAttack>(MeleeResponse(network.AttackRequests[0].AttackSequence, 1));
            yield return WaitUntil(() => !PrivateBool(Game.Interface.GetSystem<OnlineWorldSystem>(), "attackInFlight"), "mobile attack completion");
        }

        [UnityTest]
        public IEnumerator OnlineZeroHealthRejectsAttackEvenWithPositiveOfflineHealth()
        {
            WorldPlayerInfo alive = WirePlayer(111, 1501, Vector3.zero, 0, 1);
            alive.CurrentHP = alive.MaxHP = 100;
            alive.LifecycleRevision = 1;
            yield return EnterWorld(WireSnapshot(1501, 1, alive));
            var combat = Game.Interface.GetModel<CombatModel>();
            combat.SetPlayerHP(77);
            WorldPlayerInfo zeroHealth = WirePlayer(111, 1501, Vector3.zero, 0, 2);
            zeroHealth.CurrentHP = 0;
            zeroHealth.MaxHP = 100;
            zeroHealth.LifecycleRevision = 1;
            // 即使死亡标记尚未设置，零生命的权威快照也不能被离线正血量放行。
            network.Emit(CombatPush(2, 0, 0, zeroHealth));
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Assert.That(network.AttackRequests, Is.Empty);
            Assert.That(playerPorts.Motor.AttackFeedbackCount, Is.Zero);
            Assert.That(combat.PlayerHP.Value, Is.EqualTo(77));
        }

        private IReadOnlyList<Transform> RemoteTransforms()
        {
            Transform root = Game.Interface.GetModel<MapSceneModel>().CurrentHandle?.Context?.DynamicObjectRoot;
            if (root == null) return Array.Empty<Transform>();
            return root.Cast<Transform>().Where(child => child.gameObject.activeSelf && child.GetComponent<PlayerView>() != null &&
                child.GetComponent<PlayerView>().CharacterController != null && !child.GetComponent<PlayerView>().CharacterController.enabled).ToArray();
        }

        private static OnlineRemotePlayerViewCtrl RemoteView(long unitId)
        {
            object controller = RemoteController();
            var active = ActiveEntries(controller);
            object entry = active?[unitId];
            var view = entry?.GetType().GetField("View", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(entry) as OnlineRemotePlayerViewCtrl;
            Assert.That(view, Is.Not.Null, $"active remote view {unitId}");
            return view;
        }

        private static object RemoteRawHandle(long unitId)
        {
            object entry = ActiveEntries(RemoteController())?[unitId];
            object boxed = entry?.GetType().GetField("Handle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(entry);
            Assert.That(boxed, Is.Not.Null, $"active remote handle {unitId}");
            return ((ResourceLoadHandle)boxed).RawHandle;
        }

        private static bool HasActiveRemote(long unitId) => ActiveEntries(RemoteController())?.Contains(unitId) == true;

        private static T ActiveContext<T>(string fieldName)
        {
            object value = RemoteController().GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(RemoteController());
            Assert.That(value, Is.Not.Null, fieldName);
            return (T)value;
        }

        private static object RemoteController() => Game.Interface.GetSystem<MapSceneSystem>().OnlineWorldPlayers;

        private static IDictionary ActiveEntries(object controller) =>
            (IDictionary)controller.GetType().GetField("active", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(controller);

        private Transform LocalPlayerTransform()
        {
            Transform root = Game.Interface.GetModel<MapSceneModel>().CurrentHandle?.Context?.DynamicObjectRoot;
            Assert.That(root, Is.Not.Null);
            Transform local = root.Cast<Transform>().Single(child => child.gameObject.activeSelf && child.GetComponent<PlayerView>()?.CharacterController?.enabled == true);
            return local;
        }

        private IEnumerator LoginAndRestoreEmpty(long sessionEpoch)
        {
            Task<OnlineAccountOperationResult> login = Game.Interface.GetSystem<OnlineAccountSystem>().LoginAsync("world_test", "Pass_123");
            yield return WaitUntil(() => network.PendingCount<C2G_Login>() == 1, "login request");
            network.Complete<C2G_Login>(LoginResponse(sessionEpoch));
            yield return Await(login);
            Assert.That(login.Result.Success, Is.True);
            yield return WaitUntil(() => network.PendingCount<C2G_GetCurrentWorld>() == 1, "automatic empty restore");
            network.Complete<C2G_GetCurrentWorld>(CurrentEmptyResponse());
            yield return WaitUntil(() => Game.Interface.GetSystem<OnlineWorldSystem>().Snapshot.OperationState == OnlineWorldOperationState.Idle, "empty restore completion");
        }

        private IEnumerator EnterWorld(ET.OnlineWorldSnapshot world)
        {
            Task<OnlineWorldOperationResult> entering = Game.Interface.GetSystem<OnlineWorldSystem>().EnterWorldAsync(MapIds.SurvivalSample);
            yield return WaitUntil(() => network.PendingCount<C2G_EnterWorld>() == 1, "enter request");
            network.Complete<C2G_EnterWorld>(EnterResponse(world));
            yield return WaitUntil(() => network.PendingCount<C2G_GetCurrentWorld>() == 1, "enter convergence");
            network.Complete<C2G_GetCurrentWorld>(CurrentResponse(world));
            yield return Await(entering);
            Assert.That(entering.Result.Success, Is.True, entering.Result.Code.ToString());
        }

        private IMapContentHandle PrepareStableBase(long instanceId)
        {
            MapSceneModel map = Game.Interface.GetModel<MapSceneModel>();
            if (map.CurrentHandle is FakeMapHandle old && old.Root != null) Object.DestroyImmediate(old.Root);
            map.Clear();
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
            Game.Interface.GetSystem<SaveSystem>().MarkDirty("world base origin test");
            IMapContentHandle handle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.Base, instanceId);
            map.SetLoaded(MapIds.Base, handle, MapPreparationMode.OfflineFull);
            return handle;
        }

        private IEnumerator EmptyOriginCycleNeverLoadsBase()
        {
            MapSceneModel map = Game.Interface.GetModel<MapSceneModel>();
            Task<MapSceneResult> unloading = Game.Interface.GetSystem<MapSceneSystem>()
                .UnloadCurrentMapAsync().AsTask();
            yield return Await(unloading);
            Assert.That(unloading.Result.Success, Is.True, unloading.Result.Error);
            Assert.That(map.CurrentHandle, Is.Null);
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), false);
            int baseLoads = mapLoader.BaseLoadCount;
            ET.OnlineWorldSnapshot world = WireSnapshot(9901, 1, WirePlayer(999, 9901, Vector3.zero, 0, 1));
            yield return EnterWorld(world);
            Task<OnlineWorldOperationResult> leaving = Game.Interface.GetSystem<OnlineWorldSystem>().LeaveWorldAsync();
            yield return WaitUntil(() => network.PendingCount<C2G_LeaveWorld>() == 1, "empty origin leave");
            network.Complete<C2G_LeaveWorld>(LeaveResponse(0));
            yield return Await(leaving);
            Assert.That(leaving.Result.Success, Is.True);
            Assert.That(mapLoader.BaseLoadCount, Is.EqualTo(baseLoads));
            Assert.That(map.CurrentHandle, Is.Null);
        }

        private IEnumerator AssertEntryRejectedWithoutMutation(
            OnlineWorldSystem system,
            MapSceneModel map,
            IMapContentHandle expectedHandle,
            string label)
        {
            string expectedMapId = map.CurrentMapId;
            MapPreparationMode expectedMode = map.CurrentMode;
            MapLoadStatus expectedStatus = map.Status.Value;
            Task<OnlineWorldOperationResult> rejected = system.EnterWorldAsync(MapIds.SurvivalSample);
            yield return Await(rejected);
            Assert.That(rejected.Result.Code, Is.EqualTo(OnlineWorldResultCode.OperationInvalid), label);
            Assert.That(network.PendingCount<C2G_EnterWorld>(), Is.Zero, label);
            Assert.That(map.CurrentHandle, Is.SameAs(expectedHandle), label);
            Assert.That(map.CurrentMapId, Is.EqualTo(expectedMapId), label);
            Assert.That(map.CurrentMode, Is.EqualTo(expectedMode), label);
            Assert.That(map.Status.Value, Is.EqualTo(expectedStatus), label);
        }

        public enum BaseIntentTerminalPath
        {
            EnterResponseRejected,
            RestoreConfirmedAbsent,
            FinalIdentityMismatch,
            ExplicitLeave,
            AccountCleanup
        }

        private static OnlineWorldPlayerSnapshot Player(long playerId, long unitId, Vector3 position, float yaw, long revision) => new(playerId, unitId, "P" + playerId, 1, MapId, "player.graybox.default", position, yaw, OnlineWorldConnectionState.Online, revision);
        private static OnlineWorldSnapshot Snapshot(long selfUnitId, long revision, IReadOnlyList<OnlineWorldPlayerSnapshot> players) => new(true, 1, MapId, selfUnitId, revision, players, false, OnlineWorldOperationState.Idle, OnlineWorldResultCode.Success);
        private static WorldPlayerInfo WirePlayer(long playerId, long unitId, Vector3 position, float yaw, long revision, int connection = 1) { WorldPlayerInfo value = WorldPlayerInfo.Create(); value.PlayerId = playerId; value.UnitId = unitId; value.DisplayName = "P" + playerId; value.WorldId = 1; value.MapId = MapId; value.PlayerConfigId = "player.graybox.default"; value.PositionX = position.x; value.PositionY = position.y; value.PositionZ = position.z; value.YawDegrees = yaw; value.ConnectionState = connection; value.StateRevision = revision; return value; }
        private static ET.OnlineWorldSnapshot WireSnapshot(long selfUnitId, long revision, params WorldPlayerInfo[] players) { var value = ET.OnlineWorldSnapshot.Create(); value.WorldId = 1; value.MapId = MapId; value.WorldRevision = revision; value.SelfUnitId = selfUnitId; value.SelfInventory=OnlineInventoryInfo.Create(); value.SelfInventory.Capacity=12; value.SelfInventory.Equipment=OnlineEquipmentInfo.Create();value.SelfInventory.Equipment.SchemaVersion=1;value.SelfInventory.Equipment.SourceSlotIndex=-1; value.SelfInventory.Equipment.ActiveSlot=(int)WeaponSlot.Melee; foreach (WorldPlayerInfo player in players) value.Players.Add(player); return value; }
        private static G2C_EnterWorld EnterResponse(ET.OnlineWorldSnapshot snapshot) { var value = G2C_EnterWorld.Create(); value.Error = 0; value.HasWorld = true; value.World = snapshot; return value; }
        private static G2C_GetCurrentWorld CurrentResponse(ET.OnlineWorldSnapshot snapshot) { var value = G2C_GetCurrentWorld.Create(); value.Error = 0; value.HasWorld = true; value.World = snapshot; return value; }
        private static G2C_GetCurrentWorld CurrentEmptyResponse() { var value = G2C_GetCurrentWorld.Create(); value.Error = 0; value.HasWorld = false; return value; }
        private static G2C_Login LoginResponse(long sessionEpoch) { var value = G2C_Login.Create(); value.Error = 0; value.AccountId = 1; value.PlayerId = 2; value.SessionEpoch = sessionEpoch; value.IsPlayerActorReused = true; return value; }
        private static G2C_ReportWorldTransform ReportResponse(long unitId, long revision) { var value = G2C_ReportWorldTransform.Create(); value.Error = 0; value.WorldId = 1; value.MapId = MapId; value.UnitId = unitId; value.StateRevision = revision; return value; }
        private static G2C_LeaveWorld LeaveResponse(int error) { var value = G2C_LeaveWorld.Create(); value.Error = error; return value; }
        private static G2C_WorldPlayerEntered EnteredPush(long revision, WorldPlayerInfo player) { var value = G2C_WorldPlayerEntered.Create(); value.WorldRevision = revision; value.Player = player; return value; }
        private static G2C_WorldPlayerTransformChanged TransformPush(long revision, WorldPlayerInfo player) { var value = G2C_WorldPlayerTransformChanged.Create(); value.WorldId = 1; value.MapId = MapId; value.WorldRevision = revision; value.Player = player; return value; }
        private static G2C_WorldPlayerLeft LeftPush(long revision, long unitId) { var value = G2C_WorldPlayerLeft.Create(); value.WorldId = 1; value.MapId = MapId; value.WorldRevision = revision; value.UnitId = unitId; return value; }
        private static G2C_WorldCombatChanged CombatPush(long revision, int kind, long actor, params WorldPlayerInfo[] players) { var value = G2C_WorldCombatChanged.Create(); value.WorldId = 1; value.MapId = MapId; value.WorldRevision = revision; value.ActionKind = kind; value.ActorUnitId = actor; foreach (WorldPlayerInfo player in players) value.Players.Add(player); return value; }
        private static G2C_WorldNavigationLeaseChanged LeasePush(long revision, long executor, long lease) { var value = G2C_WorldNavigationLeaseChanged.Create(); value.WorldId = 1; value.MapId = MapId; value.WorldRevision = revision; value.NavigationExecutorUnitId = executor; value.NavigationLeaseRevision = lease; return value; }
        private static G2C_WorldMeleeAttack MeleeResponse(long sequence, long lifecycle) { var value = G2C_WorldMeleeAttack.Create(); value.Error = 0; value.Accepted = true; value.AttackSequence = sequence; value.LifecycleRevision = lifecycle; return value; }
        private static G2C_ReportWorldEnemyNavigation NavigationResponse(long sequence) { var value = G2C_ReportWorldEnemyNavigation.Create(); value.Error = 0; value.Accepted = true; value.LastAcceptedReportSequence = sequence; return value; }
        private static float PrivateFloat(object target, string fieldName) => (float)target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static bool PrivateBool(object target, string fieldName) => (bool)target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static IEnumerator Await(Task task) { while (!task.IsCompleted) yield return null; if (task.IsFaulted) throw task.Exception; }
        private static IEnumerator WaitUntil(Func<bool> condition, string label) { float end = Time.realtimeSinceStartup + 5f; while (!condition()) { Assert.That(Time.realtimeSinceStartup, Is.LessThan(end), label); yield return null; } }

        private sealed class WorldNetwork : INetworkRuntime, INetworkResponseObserver
        {
            private readonly List<Pending> pending = new(); private readonly Dictionary<Type, List<Delegate>> pushes = new(); private readonly List<Action<NetworkDisconnected>> disconnected = new();
            public readonly List<C2G_ReportWorldTransform> ReportRequests = new(); public readonly List<C2G_WorldMeleeAttack> AttackRequests = new(); public readonly List<C2G_ReportWorldEnemyNavigation> NavigationRequests = new(); public bool IsInitialized => true; public NetworkConnectionState ConnectionState { get; private set; } = NetworkConnectionState.Connected; public int ConnectionGeneration { get; private set; } = 1;
            public int MaxPendingReports { get; private set; } public int ActiveSubscriptions => pushes.Values.Sum(x => x.Count) + disconnected.Count;
            public void Initialize() { } public void Update() { }
            public Task ConnectAsync(NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken) { ConnectionState = NetworkConnectionState.Connected; ConnectionGeneration++; return Task.CompletedTask; }
            public async Task<TResponse> CallAsync<TResponse>(IRequest request, int timeoutMilliseconds, CancellationToken cancellationToken) where TResponse : class, IResponse { var call = new Pending(request, cancellationToken); pending.Add(call); if (request is C2G_ReportWorldTransform report) { ReportRequests.Add(report); MaxPendingReports = Math.Max(MaxPendingReports, PendingCount<C2G_ReportWorldTransform>()); } if(request is C2G_WorldMeleeAttack attack)AttackRequests.Add(attack);if(request is C2G_ReportWorldEnemyNavigation navigation)NavigationRequests.Add(navigation); return (TResponse)await call.Task; }
            public int PendingCount<T>() where T : IRequest => pending.Count(x => x.Request is T && !x.Task.IsCompleted);
            public T PendingRequest<T>() where T : class, IRequest => pending.Where(x => x.Request is T && !x.Task.IsCompleted).Select(x => x.Request as T).Single();
            public void Complete<T>(IResponse response) where T : IRequest { int index = pending.FindIndex(x => x.Request is T && !x.Task.IsCompleted); Assert.That(index, Is.GreaterThanOrEqualTo(0), typeof(T).Name); Pending call = pending[index]; pending.RemoveAt(index); call.Complete(response); }
            public void Fail<T>(Exception exception) where T : IRequest { int index = pending.FindIndex(x => x.Request is T && !x.Task.IsCompleted); Assert.That(index, Is.GreaterThanOrEqualTo(0), typeof(T).Name); Pending call = pending[index]; pending.RemoveAt(index); call.Fail(exception); }
            public IDisposable RegisterPushHandler<T>(Action<T> handler) where T : class, IMessage { if (!pushes.TryGetValue(typeof(T), out List<Delegate> handlers)) pushes[typeof(T)] = handlers = new List<Delegate>(); handlers.Add(handler); return new Subscription(() => handlers.Remove(handler)); }
            public IDisposable RegisterDisconnectedHandler(Action<NetworkDisconnected> handler) { disconnected.Add(handler); return new Subscription(() => disconnected.Remove(handler)); }
            public IDisposable RegisterResponseHandler<T>(Action<T> handler) where T : class, IResponse => new Subscription(() => { });
            public void Emit<T>(T message) where T : class, IMessage { if (pushes.TryGetValue(typeof(T), out List<Delegate> handlers)) foreach (Delegate handler in handlers.ToArray()) ((Action<T>)handler)(message); }
            public void RaiseDisconnected(NetworkDisconnectReason reason = NetworkDisconnectReason.RemoteClosed) { ConnectionState = NetworkConnectionState.Disconnected; var message = new NetworkDisconnected(ConnectionGeneration, reason, "test"); foreach (Action<NetworkDisconnected> handler in disconnected.ToArray()) handler(message); }
            public void Disconnect(NetworkDisconnectReason reason) => ConnectionState = NetworkConnectionState.Disconnected; public void Dispose() => ConnectionState = NetworkConnectionState.Disposed;
            private sealed class Pending { private readonly TaskCompletionSource<IResponse> source = new(TaskCreationOptions.RunContinuationsAsynchronously); private readonly CancellationTokenRegistration registration; public Pending(IRequest request, CancellationToken token) { Request = request; if (token.CanBeCanceled) registration = token.Register(() => source.TrySetCanceled(token)); } public IRequest Request { get; } public Task<IResponse> Task => source.Task; public void Complete(IResponse response) { registration.Dispose(); source.TrySetResult(response); } public void Fail(Exception exception) { registration.Dispose(); source.TrySetException(exception); } }
        }

        private sealed class WorldMapLoader : IMapContentLoader
        {
            private TaskCompletionSource<MapContentLoadResult> delayed; public bool DelayNextLoad { get; set; } public bool FailNextLoad { get; set; } public bool FailNextUnload { get; set; } public bool HasDelayedLoad => delayed != null; public int BaseLoadCount { get; private set; } public int UnloadCount { get; private set; }
            public UniTask<MapContentLoadResult> LoadAsync(string mapId, string address, Action<float> onProgress, CancellationToken cancellationToken = default) { onProgress?.Invoke(.5f); if (mapId == MapIds.Base) BaseLoadCount++; if (FailNextLoad) { FailNextLoad = false; return UniTask.FromResult(MapContentLoadResult.Fail("injected load failure")); } if (DelayNextLoad) { DelayNextLoad = false; delayed = new TaskCompletionSource<MapContentLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously); return delayed.Task.AsUniTask(); } return UniTask.FromResult(MapContentLoadResult.Ok(TemporaryBuildingBehaviorTestSupport.CreateMapHandle(mapId, 51))); }
            public void CompleteDelayedLoad() { TaskCompletionSource<MapContentLoadResult> source = delayed; delayed = null; source.SetResult(MapContentLoadResult.Ok(TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapId, 52))); }
            public UniTask<MapContentUnloadResult> UnloadAsync(IMapContentHandle handle, CancellationToken cancellationToken = default) { UnloadCount++; if (FailNextUnload) { FailNextUnload = false; return UniTask.FromResult(MapContentUnloadResult.Fail("injected unload failure")); } if (handle is FakeMapHandle fake && fake.Root != null) Object.DestroyImmediate(fake.Root); return UniTask.FromResult(MapContentUnloadResult.Ok()); }
        }

        private sealed class ContextlessMapHandle : IMapContentHandle
        {
            public ContextlessMapHandle(string mapId) { MapId = mapId; }
            public string MapId { get; }
            public MapContext Context => null;
            public bool IsValid => true;
        }

        private sealed class WorldResourceLoader : IResourceLoader
        {
            private readonly Dictionary<object, int> releases = new(); private Action<GameObject> delayed; private int gameObjectLoadCount; public string FailAssetPath { get; set; } public bool DelayNextGameObjectLoad { get; set; } public int FailGameObjectLoadAt { get; set; } public object DelayedRawHandle { get; private set; } public bool HasDelayedLoad => delayed != null; public int ReleaseCalls => releases.Values.Sum();
            public T LoadAsset<T>(string path) where T : Object => Load(path) as T;
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object { object raw = new object(); bool gameObject = typeof(T) == typeof(GameObject); if (gameObject) gameObjectLoadCount++; if(path==FailAssetPath){onLoaded?.Invoke(null);} else if (gameObject && FailGameObjectLoadAt == gameObjectLoadCount) { FailGameObjectLoadAt = 0; onLoaded?.Invoke(null); } else if (DelayNextGameObjectLoad && gameObject) { DelayNextGameObjectLoad = false; DelayedRawHandle = raw; delayed = asset => onLoaded(asset as T); } else onLoaded?.Invoke(Load(path) as T); return new ResourceLoadHandle(raw); }
            public void CompleteDelayedLoad() { Action<GameObject> callback = delayed; delayed = null; callback?.Invoke(Load("Prefabs/Characters/GrayboxPlayer") as GameObject); }
            public void Release(ResourceLoadHandle handle) { if (handle.IsValid) releases[handle.RawHandle] = ReleaseCount(handle.RawHandle) + 1; }
            public int ReleaseCount(object raw) => raw != null && releases.TryGetValue(raw, out int count) ? count : 0;
            private static Object Load(string path) { if (path.StartsWith("Prefabs/", StringComparison.Ordinal)) return AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab"); if(path.StartsWith("map.",StringComparison.Ordinal)&&path.EndsWith(".json",StringComparison.Ordinal))return AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/WorldCollision/"+path); string candidate = path == "languages.json" ? "Assets/Game/Config/Localization/languages.json" : $"Assets/Game/Config/Localization/{path}"; return AssetDatabase.LoadAssetAtPath<TextAsset>(candidate); }
        }

        private sealed class CountingViewProvider : IViewInstanceProvider
        {
            private readonly HashSet<GameObject> live = new(); public int LiveInstances => live.Count(x => x != null);
            public T Instantiate<T>(T prefab, Transform parent, Vector3 position, Quaternion rotation) where T : Component { T value = Object.Instantiate(prefab, position, rotation, parent); live.Add(value.gameObject); return value; }
            public void Release(GameObject instance) { if (instance == null) return; live.Remove(instance); Object.DestroyImmediate(instance); }
        }

        private sealed class MutablePlayerPorts : IPlayerRuntimePortProvider
        {
            private readonly TestCameraPort camera = new();
            public PlayerMapLocalPoseSnapshot Pose; public CountingMotor Motor;
            public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort port)
            {
                if (Motor == null || Motor.UnitId != unitId || Motor.MapInstanceId != mapInstanceId) Motor = new CountingMotor(unitId, mapInstanceId);
                port = Motor;
                return true;
            }
            public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort port) { port = camera; return true; } public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider) => false;
            public bool TryGetCurrentPlayerPose(out PlayerPoseSnapshot snapshot, out string error) { snapshot = new PlayerPoseSnapshot(Pose.UnitId, Pose.MapInstanceId, Pose.LocalPosition, Quaternion.Euler(0, Pose.LocalYawDegrees, 0), Pose.IsActive); error = Pose.IsActive ? null : "inactive"; return Pose.IsActive; }
            public bool TryGetCurrentPlayerMapLocalPose(out PlayerMapLocalPoseSnapshot snapshot, out string error) { snapshot = Pose; error = Pose.IsActive ? null : "inactive"; return Pose.IsActive; }
        }

        private sealed class TestCameraPort : IPlayerCameraPort
        {
            public bool IsValid => true;
            public void ApplyProfile() { }
            public void LateUpdate() { }
            public bool TryGetCameraPose(out Vector3 position, out Vector3 up) { position = Vector3.zero; up = Vector3.up; return true; }
        }

        private sealed class CountingMotor : ICharacterMotorPort
        {
            public CountingMotor(int unitId, long mapInstanceId) { UnitId = unitId; MapInstanceId = mapInstanceId; } public int UnitId { get; } public long MapInstanceId { get; } public bool IsValid => true; public bool IsEnabled => true; public Vector3 Position { get; private set; } public Quaternion Rotation { get; private set; } = Quaternion.identity; public float MovementYawDegrees => 0; public bool IsGrounded => true; public int MoveCount { get; private set; }
            public int AttackFeedbackCount { get; private set; }
            public void Move(Vector3 motion) { MoveCount++; Position += motion; } public void RotateTowards(Quaternion target, float maxDegreesDelta) => Rotation = target; public void FaceDirection(Vector3 direction) { } public void SetTiliRing(float current, float maximum, bool isSprinting, bool exhausted) { } public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin) { origin = Position; return true; } public void FaceWorldHud(Vector3 cameraPosition, Vector3 cameraUp) { } public void PlayAttackFeedback() { AttackFeedbackCount++; } public void PlayWeaponSwingFeedback() { } public void PlayHurtFeedback() { } public void TickCombatFeedback(float deltaTime) { }
        }

        private sealed class TestInputReader : PlayerInputSystem.IPlayerInputReader
        {
            public Vector2 MoveInput { get; set; } public bool IsEnabled { get; private set; } public bool AttackPressedThisFrame { get; set; } public bool HolsterPressedThisFrame { get; set; }
            public bool MeleeSlotPressedThisFrame { get; set; } = true; public bool RangedSlotPressedThisFrame { get; set; } = true; public bool InteractPressedThisFrame { get; set; } public bool SprintHeld { get; set; } public bool BuildPressedThisFrame { get; set; } public bool DestroyPressedThisFrame { get; set; } = true; public bool CancelPressedThisFrame { get; set; } public bool ReloadPressedThisFrame { get; set; }
            public void Enable() => IsEnabled = true; public void Disable() => IsEnabled = false; public void Dispose() { } public void ClearPressed() { }
        }

        private sealed class WorldNetworkConfig : INetworkConfigProvider { public Task<NetworkConfigResult> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(NetworkConfigResult.Ok(new NetworkEndpoint("127.0.0.1", 31001))); }
        private sealed class WorldPreferenceStore : IOnlineAccountPreferenceStore { public string LoadLastUserName() => string.Empty; public bool SaveLastUserName(string userName, out string error) { error = null; return true; } }
        private sealed class Subscription : IDisposable { private Action dispose; public Subscription(Action dispose) { this.dispose = dispose; } public void Dispose() { dispose?.Invoke(); dispose = null; } }
    }
}
