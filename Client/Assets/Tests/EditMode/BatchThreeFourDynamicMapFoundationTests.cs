using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class BatchThreeFourDynamicMapFoundationTests
    {
        private FakeMapLoader loader;
        private AssetDatabaseResourceLoader resourceLoader;
        private TestRuntimeConfigProvider configProvider;
        private string tempSaveRoot;
        private readonly List<GameObject> cleanupObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            loader = new FakeMapLoader();
            resourceLoader = new AssetDatabaseResourceLoader();
            configProvider = new TestRuntimeConfigProvider();
            tempSaveRoot = Path.Combine(Path.GetTempPath(), "AnotherDayBatchThreeFour", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempSaveRoot);
            ResourceLoader.SetLoaderForTests(resourceLoader);
            var cameraObject = new GameObject("Test Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cleanupObjects.Add(cameraObject);
            CreateGameArchitectureForTests();

            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempSaveRoot);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var item in cleanupObjects)
            {
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }

            cleanupObjects.Clear();
            loader?.DestroyAll();
            if (!string.IsNullOrWhiteSpace(tempSaveRoot) && Directory.Exists(tempSaveRoot))
            {
                Directory.Delete(tempSaveRoot, true);
            }

            ResourceLoader.ResetForTests();
            ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = _ => { };
        }

        [Test]
        public void GeneratedTablesContainRequiredFieldsAndCurrentRows()
        {
            var tables = LoadGeneratedTables();
            Assert.That(tables.TbMap.DataList.Count, Is.EqualTo(4));
            Assert.That(tables.TbMapScenePart.DataList.Count, Is.EqualTo(3));
            Assert.That(tables.TbMapSpawn.DataList.Count, Is.EqualTo(23));
            Assert.That(tables.TbNpc.GetOrDefault("npc.base.map-guide").InteractionParam, Is.Empty);
            Assert.That(tables.TbNpc.GetOrDefault("npc.base.survival-guide"), Is.Null);
            Assert.That(tables.TbNpc.GetOrDefault("npc.base.mission-guide"), Is.Null);
            Assert.That(tables.TbNpc.GetOrDefault("npc.base.hejia-village-guide"), Is.Null);
            Assert.That(tables.TbMapSpawn.DataList.Count(row => row.MapId == MapIds.HeJiaVillage && row.ObjectType == MapSpawnObjectType.Player), Is.EqualTo(1));
            Assert.That(tables.TbMapSpawn.DataList.Count(row => row.MapId == MapIds.HeJiaVillage && row.ObjectType != MapSpawnObjectType.Player), Is.EqualTo(0));
            Assert.That(tables.TbNpc.GetOrDefault("npc.base.warehouse-keeper").InteractionKind, Is.EqualTo(global::NpcInteractionKind.Warehouse));
            Assert.That(tables.TbPlayer.GetOrDefault("player.graybox.default").PrefabName, Is.EqualTo("GrayboxPlayer"));
            Assert.That(tables.TbEnemy.GetOrDefault("enemy.graybox.basic").LootConfigId, Is.EqualTo("loot.metal-parts"));
            Assert.That(tables.TbLoot.GetOrDefault("loot.metal-parts").PrefabName, Is.EqualTo("TestResourcePickup"));
            Assert.That(tables.TbExtraction.GetOrDefault("extraction.task.main").PrefabName, Is.EqualTo("TaskExit"));
        }

        [Test]
        public void RuntimeConfigProviderRejectsDuplicateOrBrokenReferences()
        {
            var provider = new LubanRuntimeConfigProvider(LoadGeneratedTables);
            Assert.IsTrue(provider.TryGetMap(MapIds.Base, out var map, out var error), error);
            Assert.That(map.SceneAddress, Is.EqualTo("Scenes/BaseMap"));

            provider = new LubanRuntimeConfigProvider(() => BuildTables(
                new[]
                {
                    SpawnRow("duplicate.spawn", MapIds.Base, MapSpawnObjectType.Player, "player.graybox.default"),
                    SpawnRow("duplicate.spawn", MapIds.Base, MapSpawnObjectType.Player, "player.graybox.default")
                }));
            Assert.IsFalse(provider.TryGetMap(MapIds.Base, out _, out error));
            Assert.That(error, Does.Contain("Tables"));
            Assert.That(error, Does.Contain("duplicate"));

            provider = new LubanRuntimeConfigProvider(() => BuildTables(
                new[]
                {
                    SpawnRow("bad.map", "missing.map", MapSpawnObjectType.Player, "player.graybox.default")
                }));
            Assert.IsFalse(provider.TryGetMapSpawns(MapIds.Base, out _, out error));
            Assert.That(error, Does.Contain("TbMapSpawn"));
            Assert.That(error, Does.Contain("MapId"));

            provider = new LubanRuntimeConfigProvider(() => throw new InvalidOperationException("bytes missing"));
            Assert.IsFalse(provider.TryGetMap(MapIds.Base, out _, out error));
            Assert.That(error, Does.Contain("Tables"));
            Assert.That(error, Does.Contain("bytes missing"));
        }

        [Test]
        public void RuntimeConfigProviderBuildsOrderedMapSceneGroupAndRejectsBrokenParts()
        {
            var provider = new LubanRuntimeConfigProvider(LoadGeneratedTables);
            Assert.That(provider.TryGetMap(MapIds.HeJiaVillage, out var map, out var error), Is.True, error);
            Assert.That(map.SceneAddresses, Is.EqualTo(new[]
            {
                "Scenes/GeneratedMaps/HeJiaVillageLocalPreview/HeJiaVillageLocalPreview"
            }));
            Assert.That(map.SceneAddress, Is.EqualTo(map.SceneAddresses[0]));

            provider = new LubanRuntimeConfigProvider(() => LoadGeneratedTablesWithParts(new[]
            {
                new PartRow("broken", "missing.map", "GeneratedMaps/HeJiaVillage/Scenes/HeJiaVillage_1_2", 1)
            }));
            Assert.That(provider.TryGetMap(MapIds.Base, out _, out error), Is.False);
            Assert.That(error, Does.Contain("TbMapScenePart[broken].MapId"));

            provider = new LubanRuntimeConfigProvider(() => LoadGeneratedTablesWithParts(new[]
            {
                new PartRow("one", MapIds.HeJiaVillage, "GeneratedMaps/HeJiaVillage/Scenes/HeJiaVillage_1_2", 1),
                new PartRow("two", MapIds.HeJiaVillage, "GeneratedMaps/HeJiaVillage/Scenes/HeJiaVillage_2_1", 1)
            }));
            Assert.That(provider.TryGetMap(MapIds.Base, out _, out error), Is.False);
            Assert.That(error, Does.Contain("Order"));
        }

        [Test]
        public void MapCatalogNoLongerOwnsSceneAddresses()
        {
            Assert.IsTrue(MapCatalog.TryGet(MapIds.Base, out var definition));
            Assert.That(typeof(MapDefinition).GetProperty("Address"), Is.Null);
            Assert.IsFalse(MapCatalog.TryGet("map.runtime-sample", out _));
            Assert.That(definition.Kind, Is.EqualTo(MapKind.Base));
        }

        [Test]
        public void FourRuntimePrefabsMatchConfiguredAddressesAndComponents()
        {
            AssertPrefab<PlayerView>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            var enemy = AssertPrefab<EnemyView>("Assets/Game/Prefabs/Characters/GrayboxEnemy.prefab");
            Assert.That(enemy.GetComponent<Collider>(), Is.Not.Null);
            Assert.That(enemy.GetComponent<NavMeshAgent>(), Is.Not.Null);
            var loot = AssertPrefab<ResourcePickupView>("Assets/Game/Prefabs/SceneObjects/TestResourcePickup.prefab");
            Assert.That(loot.GetComponent<Collider>().isTrigger, Is.True);
            var exit = AssertPrefab<TaskExitView>("Assets/Game/Prefabs/SceneObjects/TaskExit.prefab");
            Assert.That(exit.GetComponent<Collider>().isTrigger, Is.True);
        }

        [Test]
        public void MovedPrefabsPreserveOriginalGuids()
        {
            Assert.That(AssetDatabase.AssetPathToGUID("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"), Is.EqualTo("2e96af74810a335438a1d61070bb3a0f"));
            Assert.That(AssetDatabase.AssetPathToGUID("Assets/Game/Prefabs/SceneObjects/TestResourcePickup.prefab"), Is.EqualTo("3b12d4b689c7f4e42883195658f54886"));
        }

        [Test]
        public void MapContextHasOnlyPresentationAndRuntimeRoots()
        {
            var fields = typeof(MapContext).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(fields.Any(item => item.Name == "mapContentRoot"), Is.True);
            Assert.That(fields.Any(item => item.Name == "dynamicObjectRoot"), Is.True);
            Assert.That(fields.Any(item => item.Name == "enemySpawns"), Is.False);
            Assert.That(fields.Any(item => item.Name == "lootSpawns"), Is.False);
            Assert.That(fields.Any(item => item.Name == "extractionPoints"), Is.False);
            Assert.That(fields.Any(item => item.Name == "playerSpawn"), Is.False);
            Assert.That(typeof(MapContext).GetProperty("PlayerSpawn"), Is.Null);
            Assert.That(typeof(MapContext).GetProperty("ExtractionPoints"), Is.Null);
        }

        [Test]
        public void MapInstanceIdReplacesRuntimeSessionIdentity()
        {
            var scripts = ReadAllGameScripts();
            Assert.That(scripts, Does.Contain("MapInstanceId"));
            Assert.That(scripts, Does.Not.Contain("WorldSession"));
            Assert.That(scripts, Does.Not.Contain("MapRuntime"));
            Assert.That(scripts, Does.Not.Contain("SessionId"));
        }

        [Test]
        public void MapSceneSystemDirectlyOwnsFourControllers()
        {
            var source = ReadGameScript("System/Map/MapSceneSystem.cs");
            Assert.That(source, Does.Contain("PlayerCtrl playerCtrl"));
            Assert.That(source, Does.Contain("EnemyCtrl enemyCtrl"));
            Assert.That(source, Does.Contain("LootCtrl lootCtrl"));
            Assert.That(source, Does.Contain("ExtractionCtrl extractionCtrl"));
            Assert.That(source, Does.Contain("PrepareControllers"));
        }

        [UnityTest]
        public IEnumerator PrepareFailureRollsBackCandidateInReverseAndKeepsOldMap()
        {
            var system = Game.Interface.GetSystem<MapSceneSystem>();
            yield return Await(system.LoadMapAsync(MapIds.Base).AsTask());
            var oldHandle = Game.Interface.GetModel<MapSceneModel>().CurrentHandle;
            loader.FailNextLoad = true;
            var task = system.LoadMapAsync(MapIds.SurvivalSample).AsTask();
            yield return Await(task);
            Assert.IsFalse(task.Result.Success);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.SameAs(oldHandle));
        }

        [UnityTest]
        public IEnumerator OldMapUnloadFailureRestoresOldMapAndRollsBackCandidate()
        {
            var system = Game.Interface.GetSystem<MapSceneSystem>();
            yield return Await(system.LoadMapAsync(MapIds.Base).AsTask());
            var oldHandle = Game.Interface.GetModel<MapSceneModel>().CurrentHandle;
            loader.FailNextUnload = true;
            var task = system.LoadMapAsync(MapIds.SurvivalSample).AsTask();
            yield return Await(task);
            Assert.IsFalse(task.Result.Success);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.SameAs(oldHandle));
        }

        [UnityTest]
        public IEnumerator RestoreExceptionClearsOldAndCandidateAndReturnsMainMenu()
        {
            var system = Game.Interface.GetSystem<MapSceneSystem>();
            yield return Await(system.LoadMapAsync(MapIds.Base).AsTask());
            var oldHandle = loader.LoadedHandles.Last();
            loader.FailNextUnload = true;
            loader.InvalidateBeforeUnloadFailure = true;
            var task = system.LoadMapAsync(MapIds.SurvivalSample).AsTask();
            yield return Await(task);
            Assert.IsFalse(task.Result.Success);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.Null);
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
            Assert.IsFalse(oldHandle.IsValid);
        }

        [UnityTest]
        public IEnumerator CommitFailureAfterOldUnloadCleansCandidateAndReturnsMainMenu()
        {
            var system = Game.Interface.GetSystem<MapSceneSystem>();
            yield return Await(system.LoadMapAsync(MapIds.Base).AsTask());
            var oldHandle = loader.LoadedHandles.Last();
            loader.OnUnloadSuccess = () =>
            {
                var duplicate = new UnitSnapshot(
                    PlayerModel.LocalPlayerUnitId,
                    99,
                    UnitType.Player,
                    UnitSource.Player,
                    TestRuntimeConfigProvider.Player.Id,
                    Vector3.zero,
                    Quaternion.identity,
                    UnitAuthorityMode.Local,
                    0,
                    true);
                Assert.IsTrue(Game.Interface.GetSystem<UnitSystem>().TryRegister(duplicate, out var error), error);
            };

            var task = system.LoadMapAsync(MapIds.SurvivalSample).AsTask();
            yield return Await(task);
            var candidateHandle = loader.LoadedHandles.Last();
            Assert.IsFalse(task.Result.Success);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.Null);
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
            Assert.IsFalse(oldHandle.IsValid);
            Assert.IsFalse(candidateHandle.IsValid);
        }

        [UnityTest]
        public IEnumerator ExplicitUnloadFailureRestoresOldMap()
        {
            var system = Game.Interface.GetSystem<MapSceneSystem>();
            var initialLoad = system.LoadMapAsync(MapIds.Base).AsTask();
            yield return Await(initialLoad);
            var oldHandle = Game.Interface.GetModel<MapSceneModel>().CurrentHandle;
            Assert.That(initialLoad.Result.Success, Is.True,
                $"首次地图加载失败：{initialLoad.Result.Error}；当前句柄为空：{oldHandle == null}");
            loader.FailNextUnload = true;
            var task = system.UnloadCurrentMapAsync().AsTask();
            yield return Await(task);
            Assert.That(task.Result.Success, Is.False,
                $"首次加载成功：{initialLoad.Result.Success}；卸载前句柄为空：{oldHandle == null}；当前句柄仍为旧句柄：{ReferenceEquals(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, oldHandle)}；FakeMapLoader.UnloadAsync 调用次数：{loader.UnloadCallCount}");
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.SameAs(oldHandle));
        }

        [UnityTest]
        public IEnumerator ExplicitUnloadFromPausedTransitionsThroughLoadingToMainMenu()
        {
            var system = Game.Interface.GetSystem<MapSceneSystem>();
            yield return Await(system.LoadMapAsync(MapIds.Base).AsTask());

            var timeScale = new FakeTimeScaleController { TimeScale = 0.35f };
            var appState = Game.Interface.GetSystem<AppStateSystem>();
            appState.SetTimeScaleControllerForTests(timeScale);
            Assert.IsTrue(appState.TryPause());

            var events = new List<AppStateChangedEvent>();
            var unregister = Game.Interface.RegisterEvent<AppStateChangedEvent>(events.Add);
            try
            {
                var task = system.UnloadCurrentMapAsync().AsTask();
                yield return Await(task);

                Assert.IsTrue(task.Result.Success, task.Result.Error);
                Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.Null);
                Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
                Assert.That(timeScale.TimeScale, Is.EqualTo(0.35f));
                Assert.That(events.Select(item => item.Previous).ToArray(), Is.EqualTo(new[] { AppState.Paused, AppState.Loading }));
                Assert.That(events.Select(item => item.Current).ToArray(), Is.EqualTo(new[] { AppState.Loading, AppState.Paused }));
            }
            finally
            {
                unregister.UnRegister();
                appState.SetTimeScaleControllerForTests(null);
            }
        }

        [UnityTest]
        public IEnumerator ExplicitUnloadFailureFromPausedRestoresPausedAndTimeScale()
        {
            var system = Game.Interface.GetSystem<MapSceneSystem>();
            yield return Await(system.LoadMapAsync(MapIds.Base).AsTask());
            var oldHandle = Game.Interface.GetModel<MapSceneModel>().CurrentHandle;

            var timeScale = new FakeTimeScaleController { TimeScale = 0.35f };
            var appState = Game.Interface.GetSystem<AppStateSystem>();
            appState.SetTimeScaleControllerForTests(timeScale);
            Assert.IsTrue(appState.TryPause());

            loader.FailNextUnload = true;
            try
            {
                var task = system.UnloadCurrentMapAsync().AsTask();
                yield return Await(task);

                Assert.IsFalse(task.Result.Success);
                Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.SameAs(oldHandle));
                Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
                Assert.That(timeScale.TimeScale, Is.EqualTo(0f));
            }
            finally
            {
                appState.SetTimeScaleControllerForTests(null);
            }
        }

        [UnityTest]
        public IEnumerator CancellationBeforeOldUnloadKeepsOldMap()
        {
            var system = Game.Interface.GetSystem<MapSceneSystem>();
            yield return Await(system.LoadMapAsync(MapIds.Base).AsTask());
            var oldHandle = Game.Interface.GetModel<MapSceneModel>().CurrentHandle;
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var task = system.LoadMapAsync(MapIds.SurvivalSample, cts.Token).AsTask();
            yield return Await(task);
            Assert.IsFalse(task.Result.Success);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.SameAs(oldHandle));
        }

        [UnityTest]
        public IEnumerator MapContentRootsNeverOverlapAtStableState()
        {
            var system = Game.Interface.GetSystem<MapSceneSystem>();
            var firstLoad = system.LoadMapAsync(MapIds.Base).AsTask();
            yield return Await(firstLoad);
            Assert.IsTrue(firstLoad.Result.Success, firstLoad.Result.Error);
            var firstHandle = loader.LoadedHandles.Last();
            var secondLoad = system.LoadMapAsync(MapIds.SurvivalSample).AsTask();
            yield return Await(secondLoad);
            Assert.IsTrue(secondLoad.Result.Success, secondLoad.Result.Error);
            var secondHandle = loader.LoadedHandles.Last();
            Assert.IsTrue(secondHandle.Context.MapContentRoot.activeSelf);
            Assert.IsFalse(firstHandle.IsValid);
        }

        [UnityTest]
        public IEnumerator PlayerLoadsConfiguredPrefabAndSpawnsAtConfiguredLocalPose()
        {
            var system = Game.Interface.GetSystem<MapSceneSystem>();
            var load = system.LoadMapAsync(MapIds.SurvivalSample).AsTask();
            yield return Await(load);
            Assert.That(load.Result.Success, Is.True, load.Result.Error);
            var player = Object.FindObjectsOfType<PlayerView>(true).Single();
            Assert.That(player.transform.localPosition.x, Is.EqualTo(0f).Within(0.01f));
            Assert.That(player.transform.localPosition.y, Is.EqualTo(0.1f).Within(0.01f));
            Assert.That(player.transform.localPosition.z, Is.EqualTo(-14f).Within(0.01f));
            Assert.That(resourceLoader.LoadedAddresses, Does.Contain("Prefabs/Characters/GrayboxPlayer"));
        }

        [UnityTest]
        public IEnumerator PlayerPrefabSuccessfulHandleReleasesExactlyOnce()
        {
            var system = Game.Interface.GetSystem<MapSceneSystem>();
            yield return Await(system.LoadMapAsync(MapIds.Base).AsTask());
            Game.Interface.Deinit();
            Assert.That(resourceLoader.PlayerReleaseCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CancelledLatePlayerLoadCannotBecomeStagedContent()
        {
            resourceLoader.DeferAsyncLoad = true;
            var system = Game.Interface.GetSystem<MapSceneSystem>();
            using var cts = new CancellationTokenSource();
            var task = system.LoadMapAsync(MapIds.Base, cts.Token).AsTask();
            cts.Cancel();
            yield return AwaitWithinFrames(task, 20);
            Assert.IsTrue(task.IsCompleted);
            Assert.IsFalse(task.Result.Success);
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(resourceLoader.PlayerPendingCount, Is.EqualTo(1));
            Assert.That(resourceLoader.PlayerReleaseCount, Is.EqualTo(0));

            var secondTask = system.LoadMapAsync(MapIds.Base).AsTask();
            yield return Await(secondTask);
            Assert.IsFalse(secondTask.Result.Success);
            Assert.That(resourceLoader.PlayerLoadCount, Is.EqualTo(1));

            resourceLoader.FlushDeferred();
            yield return null;
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(resourceLoader.PlayerReleaseCount, Is.EqualTo(1));

            resourceLoader.DeferAsyncLoad = false;
            var thirdTask = system.LoadMapAsync(MapIds.Base).AsTask();
            yield return Await(thirdTask);
            Assert.IsTrue(thirdTask.Result.Success, thirdTask.Result.Error);
            Assert.That(resourceLoader.PlayerLoadCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator PlayerLoadCallbackThenCancellationClearsLoadingState()
        {
            var system = Game.Interface.GetSystem<MapSceneSystem>();
            using var cts = new CancellationTokenSource();
            resourceLoader.AfterLoadedCallback = cts.Cancel;

            var task = system.LoadMapAsync(MapIds.Base, cts.Token).AsTask();
            yield return Await(task);
            Assert.IsFalse(task.Result.Success);
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(resourceLoader.PlayerReleaseCount, Is.EqualTo(1));

            resourceLoader.AfterLoadedCallback = null;
            var secondTask = system.LoadMapAsync(MapIds.Base).AsTask();
            yield return Await(secondTask);
            Assert.IsTrue(secondTask.Result.Success, secondTask.Result.Error);
            Assert.That(resourceLoader.PlayerLoadCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator MissingMainCameraRejectsCandidateWithoutFallback()
        {
            DestroyMainCamera();
            var system = Game.Interface.GetSystem<MapSceneSystem>();
            var task = system.LoadMapAsync(MapIds.Base).AsTask();
            yield return Await(task);
            Assert.IsFalse(task.Result.Success);
            Assert.That(task.Result.Error, Does.Contain("Main Camera"));
        }

        [UnityTest]
        public IEnumerator PlayerMovementAndCombatUseTbPlayerValues()
        {
            var system = Game.Interface.GetSystem<MapSceneSystem>();
            yield return Await(system.LoadMapAsync(MapIds.Base).AsTask());
            var combat = Game.Interface.GetModel<CombatModel>();
            Assert.That(combat.PlayerMaxHP, Is.EqualTo(123));
            Assert.That(combat.PlayerAttackDamage, Is.EqualTo(17));
            Assert.That(configProvider.PlayerReadCount, Is.GreaterThanOrEqualTo(1));
            var moveSource = ReadGameScript("System/Player/PlayerMoveSystem.cs");
            Assert.That(moveSource, Does.Not.Contain("= 5f"));
            Assert.That(moveSource, Does.Not.Contain("= 720f"));
            Assert.That(moveSource, Does.Contain("config.MoveSpeed"));
        }

        [Test]
        public void PlayerHealthSaveWaitsForConfigAndKeepsExistingFormat()
        {
            var combat = Game.Interface.GetModel<CombatModel>();
            Assert.That(combat.PlayerMaxHP, Is.EqualTo(0));
            Assert.IsFalse(combat.TryGetCombatant(PlayerModel.LocalPlayerUnitId, out _));

            combat.Restore(new Newtonsoft.Json.Linq.JObject());
            combat.ApplyPlayerConfig(TestRuntimeConfigProvider.Player, true);
            Assert.That(combat.PlayerHP.Value, Is.EqualTo(123));

            combat.Restore(Newtonsoft.Json.Linq.JObject.FromObject(new { hp = 37 }));
            Assert.That(combat.PlayerHP.Value, Is.EqualTo(37));
            combat.ApplyPlayerConfig(TestRuntimeConfigProvider.Player, true);
            Assert.IsTrue(combat.TryGetCombatant(PlayerModel.LocalPlayerUnitId, out var player));
            Assert.That(player.HP, Is.EqualTo(37));
            Assert.That(player.MaxHP, Is.EqualTo(123));
            combat.Reset();
            Assert.That(combat.PlayerHP.Value, Is.EqualTo(123));
            var capture = combat.Capture().ToString();
            Assert.That(capture, Does.Contain("hp"));
            Assert.That(capture, Does.Not.Contain("maxHP"));
        }

        [UnityTest]
        public IEnumerator NewSaveHealthWaitsForPlayerConfigButExplicitZeroRemainsZero()
        {
            Game.Interface.GetSystem<CombatSystem>();
            var saveSystem = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(saveSystem.CreateBase(out var created, out var error), Is.True, error);
            Assert.That(created.Sections[Game.Interface.GetModel<CombatModel>().SectionId]["hp"], Is.Null);

            var system = Game.Interface.GetSystem<MapSceneSystem>();
            yield return Await(system.LoadMapAsync(MapIds.Base).AsTask());
            Assert.IsTrue(saveSystem.CommitBaseRuntime(created, out error), error);
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(123));

            var zero = created.Clone();
            zero.Sections[Game.Interface.GetModel<CombatModel>().SectionId] =
                Newtonsoft.Json.Linq.JObject.FromObject(new { hp = 0 });
            yield return Await(system.LoadMapAsync(MapIds.SurvivalSample).AsTask());
            Assert.IsTrue(saveSystem.CommitBaseRuntime(zero, out error), error);
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(0));
        }

        [Test]
        public void ScenesContainNoPreplacedGameplayObjects()
        {
            AssertSceneCounts("Assets/Game/Scenes/BaseMap.unity");
            AssertSceneCounts("Assets/Game/Scenes/SurvivalSampleMap.unity");
            AssertSceneCounts("Assets/Game/Scenes/TaskSampleMap.unity");
        }

        [Test]
        public void LegacyWorldSessionMapRuntimeAndAuthoringTypesAreAbsent()
        {
            var scripts = ReadAllGameScripts();
            Assert.That(scripts, Does.Not.Contain("WorldSession"));
            Assert.That(scripts, Does.Not.Contain("WorldRootBindings"));
            Assert.That(scripts, Does.Not.Contain("MapSceneContext"));
            Assert.That(scripts, Does.Not.Contain("MapRuntime"));
            Assert.That(scripts, Does.Not.Contain("Authoring"));
        }

        private static Tables LoadGeneratedTables()
        {
            return new Tables(name =>
            {
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/Game/Config/{name}.bytes");
                Assert.That(asset, Is.Not.Null, name);
                return new ByteBuf(asset.bytes);
            });
        }

        private static Tables BuildTables(IReadOnlyList<SpawnRowData> spawns)
        {
            return new Tables(name =>
            {
                switch (name)
                {
                    case "tbmap":
                        return ToReadableBuffer(buf =>
                        {
                            buf.WriteSize(3);
                            buf.WriteString(MapIds.Base);
                            buf.WriteString("BaseMap");
                            buf.WriteString(MapIds.SurvivalSample);
                            buf.WriteString("SurvivalSampleMap");
                            buf.WriteString(MapIds.MissionSample);
                            buf.WriteString("TaskSampleMap");
                        });
                    case "tbmapspawn":
                        return ToReadableBuffer(buf =>
                        {
                            buf.WriteSize(spawns.Count);
                            foreach (var spawn in spawns)
                            {
                                buf.WriteString(spawn.Id);
                                buf.WriteString(spawn.MapId);
                                buf.WriteInt((int)spawn.ObjectType);
                                buf.WriteString(spawn.ConfigId);
                                buf.WriteFloat(0f);
                                buf.WriteFloat(0f);
                                buf.WriteFloat(0f);
                                buf.WriteFloat(0f);
                            }
                        });
                    case "tbmapscenepart":
                        return ToReadableBuffer(buf => buf.WriteSize(0));
                    default:
                        return LoadConfigBuffer(name);
                }
            });
        }

        private static Tables LoadGeneratedTablesWithParts(IReadOnlyList<PartRow> parts)
        {
            return new Tables(name => name == "tbmapscenepart"
                ? ToReadableBuffer(buf =>
                {
                    buf.WriteSize(parts.Count);
                    foreach (var part in parts)
                    {
                        buf.WriteString(part.Id);
                        buf.WriteString(part.MapId);
                        buf.WriteString(part.ScenePath);
                        buf.WriteInt(part.Order);
                    }
                })
                : LoadConfigBuffer(name));
        }

        private readonly struct PartRow
        {
            public PartRow(string id, string mapId, string scenePath, int order)
            {
                Id = id; MapId = mapId; ScenePath = scenePath; Order = order;
            }
            public string Id { get; }
            public string MapId { get; }
            public string ScenePath { get; }
            public int Order { get; }
        }

        private static ByteBuf LoadConfigBuffer(string name)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/Game/Config/{name}.bytes");
            Assert.That(asset, Is.Not.Null, name);
            return new ByteBuf(asset.bytes);
        }

        private static ByteBuf ToReadableBuffer(Action<ByteBuf> write)
        {
            var buffer = new ByteBuf();
            write(buffer);
            return new ByteBuf(buffer.CopyData());
        }

        private static SpawnRowData SpawnRow(string id, string mapId, MapSpawnObjectType objectType, string configId)
        {
            return new SpawnRowData(id, mapId, objectType, configId);
        }

        private static T AssertPrefab<T>(string path) where T : Component
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            var component = prefab.GetComponent<T>();
            Assert.That(component, Is.Not.Null, path);
            return component;
        }

        private static string ReadGameScript(string relative)
        {
            return File.ReadAllText(Path.Combine(Application.dataPath, "Game/Scripts", relative.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string ReadAllGameScripts()
        {
            return string.Join("\n", Directory.GetFiles(Path.Combine(Application.dataPath, "Game/Scripts"), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Replace('\\', '/').Contains("/Model/Config/"))
                .Select(File.ReadAllText));
        }

        private static void AssertSceneCounts(string path)
        {
            var previous = EditorSceneManager.GetActiveScene().path;
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            try
            {
                Assert.That(FindByName(scene, "PlayerSpawn"), Is.EqualTo(0), path);
                Assert.That(FindInScene<PlayerView>(scene).Count, Is.EqualTo(0), path);
                Assert.That(FindInScene<EnemyView>(scene).Count, Is.EqualTo(0), path);
                Assert.That(FindInScene<ResourcePickupView>(scene).Count, Is.EqualTo(0), path);
                Assert.That(FindInScene<TaskExitView>(scene).Count, Is.EqualTo(0), path);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(previous))
                {
                    EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                }
            }
        }

        private static int FindByName(Scene scene, string name)
        {
            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                count += root.GetComponentsInChildren<Transform>(true).Count(item => item.name == name);
            }

            return count;
        }

        private static List<T> FindInScene<T>(Scene scene) where T : Component
        {
            var result = new List<T>();
            foreach (var root in scene.GetRootGameObjects())
            {
                result.AddRange(root.GetComponentsInChildren<T>(true));
            }

            return result;
        }

        private static IEnumerator Await(Task task)
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

        private static IEnumerator AwaitWithinFrames(Task task, int maxFrames)
        {
            var frames = 0;
            while (!task.IsCompleted && frames < maxFrames)
            {
                frames++;
                yield return null;
            }

            if (task.IsFaulted)
            {
                throw task.Exception;
            }
        }

        private void DestroyMainCamera()
        {
            foreach (var camera in Object.FindObjectsOfType<Camera>(true))
            {
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private void CreateGameArchitectureForTests()
        {
            var architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(architecture, true);

            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterModel(new DisplaySettingsModel());
            architecture.RegisterModel(new AudioSettingsModel());
            architecture.RegisterModel(new HotKeyModel());
            architecture.RegisterModel(new AppStateModel());
            architecture.RegisterModel(new RuntimeLogModel());
            architecture.RegisterModel(new SaveModel());
            architecture.RegisterModel(new MapSceneModel());
            architecture.RegisterModel(new UnitModel());
            architecture.RegisterModel(new PlayerModel());
            architecture.RegisterModel(new PlayerBasePoseModel());
            architecture.RegisterModel(new PlayerConditionModel());
            architecture.RegisterModel(new PlayerAttributeModel());
            architecture.RegisterModel(new CombatModel());
            architecture.RegisterModel(new LootModel());
            architecture.RegisterModel(new ExtractionModel());
            architecture.RegisterModel(new ExpeditionModel());
            architecture.RegisterModel(new PlayerBagModel());
            architecture.RegisterModel(new WarehouseModel());
            architecture.RegisterModel(new SearchContainerModel());
            architecture.RegisterModel(new WeaponEquipmentModel());
            architecture.RegisterModel(new ProjectileModel());
            architecture.RegisterModel(new TestCmdModel());
            architecture.RegisterModel(new BaseMapProgressModel());
            architecture.RegisterModel(new ResourceUpdateModel());

            architecture.RegisterUtility<ISettingsFileStore>(new SettingsFileStore());
            architecture.RegisterUtility<IDisplayResolutionApplier>(new UnityDisplayResolutionApplier());
            architecture.RegisterUtility(new LocalizationUtility(loadConfiguredTables: false));
            architecture.RegisterUtility(new HotKeyActionRegistry());
            architecture.RegisterUtility<IMapContentLoader>(loader);
            architecture.RegisterUtility<IRuntimeConfigProvider>(configProvider);
            architecture.RegisterUtility<IViewInstanceProvider>(new UnityViewInstanceProvider());
            architecture.RegisterUtility(new ViewCtrlRegistry());
            architecture.RegisterUtility<IResourceUpdateRuntimeChecker>(new YooAssetResourceUpdateRuntimeChecker());
            architecture.RegisterUtility<IRemoteResourceVersionProbe>(new UnityWebRequestResourceVersionProbe());
            architecture.RegisterUtility<IResourceUpdateNoticeClient>(new UnityWebRequestResourceUpdateNoticeClient());
            architecture.RegisterUtility<IApplicationRestartUtility>(new ApplicationRestartUtility());

            var playerCtrl = new PlayerCtrl(architecture);
            var projectileCtrl = new ProjectileCtrl(architecture);
            var enemyCtrl = new EnemyCtrl(architecture);
            var lootCtrl = new LootCtrl(architecture);
            var extractionCtrl = new ExtractionCtrl(architecture);
            var npcCtrl = new NpcCtrl(architecture);
            AssignControllerOwner(architecture, "playerCtrl", playerCtrl);
            AssignControllerOwner(architecture, "projectileCtrl", projectileCtrl);
            AssignControllerOwner(architecture, "enemyCtrl", enemyCtrl);
            AssignControllerOwner(architecture, "lootCtrl", lootCtrl);
            AssignControllerOwner(architecture, "extractionCtrl", extractionCtrl);
            AssignControllerOwner(architecture, "npcCtrl", npcCtrl);

            architecture.RegisterUtility<IPlayerRuntimePortProvider>(playerCtrl);
            architecture.RegisterUtility<IProjectileRuntimePortProvider>(projectileCtrl);
            architecture.RegisterUtility<IEnemyColliderResolver>(enemyCtrl);

            architecture.RegisterSystem(new SettingsSystem());
            architecture.RegisterSystem(new SaveSystem());
            architecture.RegisterSystem(new AppStateSystem());
            architecture.RegisterSystem(new UnitSystem());
            architecture.RegisterSystem(new PlayerAttributeSystem());
            architecture.RegisterSystem(new PlayerInputSystem());
            architecture.RegisterSystem(new PlayerMoveSystem());
            architecture.RegisterSystem(new PlayerCameraSystem());
            architecture.RegisterSystem(new RuntimeLogSystem());
            architecture.RegisterSystem(new AudioSystem());
            architecture.RegisterSystem(new HotKeySystem());
            architecture.RegisterSystem(new MapSceneSystem(projectileCtrl, playerCtrl, npcCtrl, enemyCtrl, lootCtrl, extractionCtrl));
            architecture.RegisterSystem(new ExpeditionSystem());
            architecture.RegisterSystem(new PlayerConditionSystem());
            architecture.RegisterSystem(new ExpeditionFlowSystem());
            architecture.RegisterSystem(new InventorySystem());
            architecture.RegisterSystem(new SearchContainerSystem());
            architecture.RegisterSystem(new WeaponEquipmentSystem());
            architecture.RegisterSystem(new TestCmdSystem());
            architecture.RegisterSystem(new BaseMapSelectionSystem());
            architecture.RegisterSystem(new PlayerInteractionSystem());
            architecture.RegisterSystem(new WeaponSystem());
            architecture.RegisterSystem(new ProjectileSystem());
            architecture.RegisterSystem(new CombatSystem());
            architecture.RegisterSystem(new EnemySystem());
            architecture.RegisterSystem(new EnemyDropSystem());
            architecture.RegisterSystem(new LootSystem());
            architecture.RegisterSystem(new ExtractionSystem());
            architecture.RegisterSystem(new CombatFlowSystem());
            architecture.RegisterSystem(new GameSessionFlowSystem());
            architecture.RegisterSystem(new RedPointSystem());
            architecture.RegisterSystem(new ResourceUpdateSystem());
        }

        private static void AssignControllerOwner(Game architecture, string fieldName, object controller)
        {
            var field = typeof(Game).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Game.{fieldName} was not found.");
            field.SetValue(architecture, controller);
        }

        private sealed class TestRuntimeConfigProvider : IRuntimeConfigProvider
        {
            public static readonly PlayerConfigDefinition Player = new PlayerConfigDefinition(
                "player.graybox.default",
                "Prefabs/Characters/GrayboxPlayer",
                123,
                17,
                3.5f,
                0.8f,
                6f,
                540f,
                7f);

            public int PlayerReadCount { get; private set; }

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                error = null;
                value = new MapAssetDefinition(id, id == MapIds.SurvivalSample ? "Scenes/SurvivalSampleMap" : id == MapIds.MissionSample ? "Scenes/TaskSampleMap" : "Scenes/BaseMap");
                return true;
            }

            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                error = null;
                values = new[]
                {
                    new MapSpawnDefinition("player.main", mapId, MapSpawnObjectType.Player, Player.Id, 0f, mapId == MapIds.SurvivalSample ? 0.1f : 0f, mapId == MapIds.SurvivalSample ? -14f : 0f, 0f)
                };
                return true;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                PlayerReadCount++;
                error = null;
                value = Player;
                return id == Player.Id;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                error = null;
                value = default;
                return false;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                error = null;
                value = default;
                return false;
            }

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
            {
                error = null;
                value = default;
                return false;
            }

            public bool TryGetProjectile(string id, out ProjectileConfigDefinition value, out string error)
            {
                error = null;
                value = new ProjectileConfigDefinition(id, "Prefabs/Combat/GrayboxArrow", 14f, 14f);
                return id == "projectile.arrow.basic";
            }

            public bool TryGetCraftRecipes(out IReadOnlyList<CraftRecipeDefinition> values, out string error)
            {
                values = Array.Empty<CraftRecipeDefinition>();
                error = null;
                return true;
            }
        }

        private sealed class FakeMapLoader : IMapContentLoader
        {
            public bool FailNextLoad { get; set; }
            public bool FailNextUnload { get; set; }
            public bool InvalidateBeforeUnloadFailure { get; set; }
            public Action OnUnloadSuccess { get; set; }
            public int UnloadCallCount { get; private set; }
            public List<FakeMapHandle> LoadedHandles { get; } = new List<FakeMapHandle>();

            public UniTask<MapContentLoadResult> LoadAsync(string mapId, string address, Action<float> onProgress, CancellationToken cancellationToken = default)
            {
                onProgress?.Invoke(1f);
                if (FailNextLoad)
                {
                    FailNextLoad = false;
                    return UniTask.FromResult(MapContentLoadResult.Fail("load failed"));
                }

                var handle = new FakeMapHandle(mapId, CreateContext(mapId));
                LoadedHandles.Add(handle);
                return UniTask.FromResult(MapContentLoadResult.Ok(handle));
            }

            public UniTask<MapContentUnloadResult> UnloadAsync(IMapContentHandle handle, CancellationToken cancellationToken = default)
            {
                UnloadCallCount++;
                if (FailNextUnload)
                {
                    FailNextUnload = false;
                    if (InvalidateBeforeUnloadFailure && handle is FakeMapHandle invalid)
                    {
                        invalid.Valid = false;
                        if (invalid.Root != null)
                        {
                            Object.DestroyImmediate(invalid.Root);
                        }
                    }

                    return UniTask.FromResult(MapContentUnloadResult.Fail("unload failed"));
                }

                if (handle is FakeMapHandle fake)
                {
                    fake.Valid = false;
                    Object.DestroyImmediate(fake.Root);
                }

                OnUnloadSuccess?.Invoke();
                OnUnloadSuccess = null;

                return UniTask.FromResult(MapContentUnloadResult.Ok());
            }

            private static MapContext CreateContext(string mapId)
            {
                var root = new GameObject("FakeMap-" + mapId);
                var mapContentRoot = new GameObject("MapContentRoot");
                mapContentRoot.transform.SetParent(root.transform);
                var runtimeRoot = new GameObject("DynamicObjectRoot");
                runtimeRoot.transform.SetParent(mapContentRoot.transform);
                mapContentRoot.SetActive(false);
                var context = root.AddComponent<MapContext>();
                SetField(context, "mapContentRoot", mapContentRoot);
                SetField(context, "dynamicObjectRoot", runtimeRoot.transform);
                return context;
            }

            private static void SetField(object target, string name, object value)
            {
                target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);
            }

            public void DestroyAll()
            {
                foreach (var handle in LoadedHandles)
                {
                    if (handle.Root != null)
                    {
                        Object.DestroyImmediate(handle.Root);
                    }
                }
            }
        }

        private sealed class FakeTimeScaleController : ITimeScaleController
        {
            public float TimeScale { get; set; }
        }

        private sealed class FakeMapHandle : IMapContentHandle
        {
            public FakeMapHandle(string mapId, MapContext context)
            {
                MapId = mapId;
                Context = context;
            }

            public string MapId { get; }
            public MapContext Context { get; }
            public GameObject Root => Context != null ? Context.gameObject : null;
            public bool Valid { get; set; } = true;
            public bool IsValid => Valid && Context != null;
        }

        private sealed class AssetDatabaseResourceLoader : IResourceLoader
        {
            private readonly Queue<Action> deferred = new Queue<Action>();
            private readonly Dictionary<object, string> addressByHandle = new Dictionary<object, string>();
            public List<string> LoadedAddresses { get; } = new List<string>();
            public int ReleaseCount { get; private set; }
            public int PendingCount => deferred.Count;
            public int PlayerLoadCount { get; private set; }
            public int PlayerReleaseCount { get; private set; }
            public int PlayerPendingCount => deferred.Count;
            public bool DeferAsyncLoad { get; set; }
            public Action AfterLoadedCallback { get; set; }

            public T LoadAsset<T>(string path) where T : Object
            {
                return AssetDatabase.LoadAssetAtPath<T>($"Assets/Game/{path}.prefab");
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
            {
                LoadedAddresses.Add(path);
                bool isPlayerPrefab = string.Equals(path, TestRuntimeConfigProvider.Player.PrefabAddress, StringComparison.Ordinal);
                if (isPlayerPrefab)
                {
                    PlayerLoadCount++;
                }

                object rawHandle = new object();
                addressByHandle[rawHandle] = path;
                var handle = new ResourceLoadHandle(rawHandle);
                void Complete()
                {
                    onLoaded?.Invoke(LoadAsset<T>(path));
                    if (isPlayerPrefab)
                    {
                        AfterLoadedCallback?.Invoke();
                    }
                }

                if (DeferAsyncLoad && isPlayerPrefab)
                {
                    deferred.Enqueue(Complete);
                }
                else
                {
                    Complete();
                }

                return handle;
            }

            public void Release(ResourceLoadHandle handle)
            {
                if (handle.IsValid)
                {
                    ReleaseCount++;
                    if (addressByHandle.TryGetValue(handle.RawHandle, out string path) &&
                        string.Equals(path, TestRuntimeConfigProvider.Player.PrefabAddress, StringComparison.Ordinal))
                    {
                        PlayerReleaseCount++;
                    }
                }
            }

            public void FlushDeferred()
            {
                while (deferred.Count > 0)
                {
                    deferred.Dequeue().Invoke();
                }
            }
        }

        private readonly struct SpawnRowData
        {
            public SpawnRowData(string id, string mapId, MapSpawnObjectType objectType, string configId)
            {
                Id = id;
                MapId = mapId;
                ObjectType = objectType;
                ConfigId = configId;
            }

            public string Id { get; }
            public string MapId { get; }
            public MapSpawnObjectType ObjectType { get; }
            public string ConfigId { get; }
        }
    }
}
