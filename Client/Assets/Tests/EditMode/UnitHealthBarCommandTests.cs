using System;
using System.Threading;
using System.Collections.Generic;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Tests
{
    public sealed class UnitHealthBarCommandTests
    {
        private const string PlayerPrefabPath = "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab";
        private const string EnemyPrefabPath = "Assets/Game/Prefabs/Characters/GrayboxEnemy.prefab";
        private const string ArcherPrefabPath = "Assets/Game/Prefabs/Characters/GrayboxArcher.prefab";
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private NavMeshDataInstance navMeshDataInstance;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            ResourceLoader.SetLoaderForTests(new PrefabResourceLoader());
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(new RuntimeConfigProvider());
            Game.Interface.RegisterUtility<IViewInstanceProvider>(new TrackingViewInstanceProvider(objects));
        }

        [TearDown]
        public void TearDown()
        {
            ResourceLoader.ResetForTests();
            if (navMeshDataInstance.valid)
            {
                navMeshDataInstance.Remove();
            }

            for (var i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(objects[i]);
                }
            }

            objects.Clear();
            TestReset.ResetArchitecture();
        }

        [Test]
        public void UnitBarCommandDefaultsOffAndPublishesOnlyOnChange()
        {
            var model = Game.Interface.GetModel<TestCmdModel>();
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            var events = 0;
            var lastVisible = true;
            var unregister = Game.Interface.RegisterEvent<UnitHealthBarVisibilityChangedEvent>(evt =>
            {
                events++;
                lastVisible = evt.Visible;
            });

            Assert.That(model.UnitHealthBarsVisible, Is.False);
            AssertResult(system.ExecuteAsync("#unitbar off", CancellationToken.None).GetAwaiter().GetResult(), TestCmdResultCode.UnitHealthBarsChanged, true);
            Assert.That(events, Is.EqualTo(0));

            AssertResult(system.ExecuteAsync("#unitbar on", CancellationToken.None).GetAwaiter().GetResult(), TestCmdResultCode.UnitHealthBarsChanged, true);
            Assert.That(model.UnitHealthBarsVisible, Is.True);
            Assert.That(events, Is.EqualTo(1));
            Assert.That(lastVisible, Is.True);

            AssertResult(system.ExecuteAsync("#unitbar on", CancellationToken.None).GetAwaiter().GetResult(), TestCmdResultCode.UnitHealthBarsChanged, true);
            Assert.That(events, Is.EqualTo(1));

            AssertResult(system.ExecuteAsync("#unitbar off", CancellationToken.None).GetAwaiter().GetResult(), TestCmdResultCode.UnitHealthBarsChanged, true);
            Assert.That(model.UnitHealthBarsVisible, Is.False);
            Assert.That(events, Is.EqualTo(2));
            Assert.That(lastVisible, Is.False);
            unregister.UnRegister();
        }

        [TestCase("unitbar", TestCmdResultCode.UsageError)]
        [TestCase("unitbar true", TestCmdResultCode.UsageError)]
        [TestCase("unitbar on extra", TestCmdResultCode.UsageError)]
        public void UnitBarCommandRejectsNonStrictGrammar(string command, TestCmdResultCode expected)
        {
            var result = Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#" + command, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Recognized, Is.True);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(expected));
            Assert.That(Game.Interface.GetModel<TestCmdModel>().UnitHealthBarsVisible, Is.False);
        }

        [Test]
        public void EnemyHealthBarIgnoresAlertDamageAndCorpseAlwaysHides()
        {
            var instance = UnityEngine.Object.Instantiate(LoadPrefab(EnemyPrefabPath));
            try
            {
                var view = instance.GetComponent<EnemyView>();
                Assert.NotNull(view);

                view.SetHealthDisplay(50, 100);
                view.SetAlerted(true);
                Assert.That(view.HealthBarRoot.gameObject.activeSelf, Is.False);
                Assert.That(view.HealthBarFill.fillAmount, Is.EqualTo(0.5f).Within(0.001f));

                view.SetHealthBarVisible(true);
                Assert.That(view.HealthBarRoot.gameObject.activeSelf, Is.True);
                view.MarkDead();
                Assert.That(view.HealthBarRoot.gameObject.activeSelf, Is.False);
                view.SetHealthBarVisible(true);
                Assert.That(view.HealthBarRoot.gameObject.activeSelf, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ProductUnitHealthBarRootsDefaultInactive()
        {
            Assert.That(LoadPrefab(PlayerPrefabPath).transform.Find("HealthBarRoot"), Is.Null);
            Assert.That(LoadPrefab(EnemyPrefabPath).transform.Find("EnemyHealthBarRoot").gameObject.activeSelf, Is.False);
            Assert.That(LoadPrefab(ArcherPrefabPath).transform.Find("EnemyHealthBarRoot").gameObject.activeSelf, Is.False);
        }

        [Test]
        public void UnitBarCommandTogglesEnemiesAndKeepsPlayerWithoutWorldHealthBar()
        {
            AddFlatNavMesh();
            var scenario = CreateCommittedScenario();

            Assert.That(scenario.Player.transform.Find("HealthBarRoot"), Is.Null);
            Assert.That(scenario.Enemy.HealthBarRoot.gameObject.activeSelf, Is.False);

            AssertResult(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#unitbar on", CancellationToken.None).GetAwaiter().GetResult(), TestCmdResultCode.UnitHealthBarsChanged, true);
            Assert.That(scenario.Player.transform.Find("HealthBarRoot"), Is.Null);
            Assert.That(scenario.Enemy.HealthBarRoot.gameObject.activeSelf, Is.True);

            AssertResult(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#unitbar off", CancellationToken.None).GetAwaiter().GetResult(), TestCmdResultCode.UnitHealthBarsChanged, true);
            Assert.That(scenario.Player.transform.Find("HealthBarRoot"), Is.Null);
            Assert.That(scenario.Enemy.HealthBarRoot.gameObject.activeSelf, Is.False);

            scenario.ReleaseAll();
        }

        [Test]
        public void HiddenHealthUpdatesStayHiddenAndReopenWithLatestFill()
        {
            AddFlatNavMesh();
            var scenario = CreateCommittedScenario();
            var enemyId = scenario.EnemyUnitId;

            AssertResult(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#unitbar off", CancellationToken.None).GetAwaiter().GetResult(), TestCmdResultCode.UnitHealthBarsChanged, true);
            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryApplyPlayerEnvironmentalDamage(75, out var playerDamage), Is.True);
            Assert.That(playerDamage, Is.EqualTo(75));
            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryDamage(PlayerModel.LocalPlayerUnitId, enemyId, 15), Is.True);

            Assert.That(scenario.Player.transform.Find("HealthBarRoot"), Is.Null);
            Assert.That(scenario.Enemy.HealthBarRoot.gameObject.activeSelf, Is.False);
            Assert.That(scenario.Enemy.HealthBarFill.fillAmount, Is.EqualTo(0.25f).Within(0.001f));

            AssertResult(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#unitbar on", CancellationToken.None).GetAwaiter().GetResult(), TestCmdResultCode.UnitHealthBarsChanged, true);
            Assert.That(scenario.Player.transform.Find("HealthBarRoot"), Is.Null);
            Assert.That(scenario.Enemy.HealthBarRoot.gameObject.activeSelf, Is.True);
            Assert.That(scenario.Enemy.HealthBarFill.fillAmount, Is.EqualTo(0.25f).Within(0.001f));

            scenario.ReleaseAll();
        }

        [Test]
        public void UnitBarOnBeforeSpawnIsInheritedByEnemyWithPlayerWithoutWorldHealthBar()
        {
            AddFlatNavMesh();
            AssertResult(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#unitbar on", CancellationToken.None).GetAwaiter().GetResult(), TestCmdResultCode.UnitHealthBarsChanged, true);

            var scenario = CreateCommittedScenario();

            Assert.That(scenario.Player.transform.Find("HealthBarRoot"), Is.Null);
            Assert.That(scenario.Enemy.HealthBarRoot.gameObject.activeSelf, Is.True);
            scenario.ReleaseAll();
        }

        [Test]
        public void DeactivateReactivateInheritsCurrentUnitBarState()
        {
            AddFlatNavMesh();
            var scenario = CreateCommittedScenario();

            AssertResult(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#unitbar off", CancellationToken.None).GetAwaiter().GetResult(), TestCmdResultCode.UnitHealthBarsChanged, true);
            scenario.PlayerCtrl.SetCurrentMapActive(false);
            scenario.EnemyCtrl.SetCurrentMapActive(false);
            AssertResult(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#unitbar on", CancellationToken.None).GetAwaiter().GetResult(), TestCmdResultCode.UnitHealthBarsChanged, true);

            scenario.PlayerCtrl.SetCurrentMapActive(true);
            scenario.EnemyCtrl.SetCurrentMapActive(true);

            Assert.That(scenario.Player.transform.Find("HealthBarRoot"), Is.Null);
            Assert.That(scenario.Enemy.HealthBarRoot.gameObject.activeSelf, Is.True);

            AssertResult(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#unitbar off", CancellationToken.None).GetAwaiter().GetResult(), TestCmdResultCode.UnitHealthBarsChanged, true);
            scenario.PlayerCtrl.SetCurrentMapActive(false);
            scenario.EnemyCtrl.SetCurrentMapActive(false);
            scenario.PlayerCtrl.SetCurrentMapActive(true);
            scenario.EnemyCtrl.SetCurrentMapActive(true);

            Assert.That(scenario.Player.transform.Find("HealthBarRoot"), Is.Null);
            Assert.That(scenario.Enemy.HealthBarRoot.gameObject.activeSelf, Is.False);

            scenario.ReleaseAll();
        }

        [Test]
        public void DeadEnemyStaysHiddenWhenUnitBarCommandTurnsOn()
        {
            AddFlatNavMesh();
            var scenario = CreateCommittedScenario();
            RegisterPlayerCombatant();

            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryDamage(PlayerModel.LocalPlayerUnitId, scenario.EnemyUnitId), Is.True);
            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryDamage(PlayerModel.LocalPlayerUnitId, scenario.EnemyUnitId), Is.True);
            Assert.That(scenario.Enemy.HealthBarRoot.gameObject.activeSelf, Is.False);

            AssertResult(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#unitbar on", CancellationToken.None).GetAwaiter().GetResult(), TestCmdResultCode.UnitHealthBarsChanged, true);

            Assert.That(scenario.Enemy.HealthBarRoot.gameObject.activeSelf, Is.False);
            scenario.ReleaseAll();
        }

        [Test]
        public void CorpseWorldHud_FacesCameraThroughEnemyLateUpdate()
        {
            AddFlatNavMesh();
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            Game.Interface.GetModel<ExpeditionModel>().BeginSurvival(MapIds.SurvivalSample, 60f);
            Assert.That(Game.Interface.GetSystem<InventorySystem>()
                .TrySetPlayerBagRiskState(PlayerBagRiskState.InExpedition, "test", false, out var riskError), Is.True, riskError);

            var scenario = CreateCommittedScenario(Vector3.zero, new Vector3(1f, 0f, 0f));
            try
            {
                AssertResult(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#unitbar on", CancellationToken.None).GetAwaiter().GetResult(), TestCmdResultCode.UnitHealthBarsChanged, true);
                LifeCycle.LateUpdate();
                Assert.That(scenario.Enemy.HealthBarRoot.gameObject.activeSelf, Is.True);

                Assert.That(Game.Interface.GetSystem<CombatSystem>()
                    .TryDamage(PlayerModel.LocalPlayerUnitId, scenario.EnemyUnitId), Is.True);
                Assert.That(Game.Interface.GetSystem<CombatSystem>()
                    .TryDamage(PlayerModel.LocalPlayerUnitId, scenario.EnemyUnitId), Is.True);
                LifeCycle.Update();

                var containers = Game.Interface.GetModel<SearchContainerModel>();
                Assert.That(containers.TryGet(scenario.EnemyUnitId, out var corpse), Is.True);
                Assert.That(corpse.Container.Slots.Count, Is.GreaterThan(0));
                scenario.Enemy.SetFocused(true);
                var prompt = new SerializedObject(scenario.Enemy)
                    .FindProperty("corpsePromptRoot").objectReferenceValue as GameObject;
                Assert.NotNull(prompt);
                Assert.That(prompt.activeSelf, Is.True);

                var camera = Camera.main;
                Assert.NotNull(camera);
                camera.transform.position = new Vector3(7f, 5f, -4f);
                LifeCycle.LateUpdate();

                var direction = prompt.transform.position - camera.transform.position;
                Assert.That(Quaternion.Angle(prompt.transform.rotation, Quaternion.LookRotation(direction, camera.transform.up)), Is.LessThan(0.1f));
                Assert.That(scenario.Enemy.HealthBarRoot.gameObject.activeSelf, Is.False);
            }
            finally
            {
                scenario.ReleaseAll();
            }
        }

        [Test]
        public void CommandsAfterControllerReleaseDoNotTouchReleasedViews()
        {
            AddFlatNavMesh();
            var scenario = CreateCommittedScenario();
            var playerRoot = scenario.Player.gameObject;
            var enemyRoot = scenario.Enemy.HealthBarRoot.gameObject;

            AssertResult(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#unitbar on", CancellationToken.None).GetAwaiter().GetResult(), TestCmdResultCode.UnitHealthBarsChanged, true);
            scenario.ReleaseAll();

            Assert.DoesNotThrow(() => Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#unitbar off", CancellationToken.None).GetAwaiter().GetResult());
            Assert.DoesNotThrow(() => Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#unitbar on", CancellationToken.None).GetAwaiter().GetResult());
            Assert.That(playerRoot == null, Is.True);
            Assert.That(enemyRoot == null, Is.True);
        }

        private static GameObject LoadPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.NotNull(prefab, path);
            return prefab;
        }

        private static void AssertResult(TestCmdExecutionResult result, TestCmdResultCode code, bool success)
        {
            Assert.That(result.Recognized, Is.True);
            Assert.That(result.Success, Is.EqualTo(success));
            Assert.That(result.Code, Is.EqualTo(code));
            Assert.That(result.Message, Is.Not.Empty);
        }

        private ControllerScenario CreateCommittedScenario(Vector3? playerPosition = null, Vector3? enemyPosition = null)
        {
            var context = CreateContext(playerPosition ?? new Vector3(-1f, 0f, 0f), enemyPosition ?? new Vector3(1f, 0f, 1f));
            RegisterPlayerCombatant();

            var playerCtrl = new PlayerCtrl(Game.Interface);
            var enemyCtrl = new EnemyCtrl(Game.Interface);
            Game.Interface.RegisterUtility<IPlayerRuntimePortProvider>(playerCtrl);

            var playerPrepare = playerCtrl.PrepareMapAsync(context, default).GetAwaiter().GetResult();
            Assert.That(playerPrepare.Success, Is.True, playerPrepare.Error);
            var enemyPrepare = enemyCtrl.PrepareMapAsync(context, default).GetAwaiter().GetResult();
            Assert.That(enemyPrepare.Success, Is.True, enemyPrepare.Error);

            var playerCommit = playerCtrl.CommitPreparedMap();
            Assert.That(playerCommit.Success, Is.True, playerCommit.Error);
            var enemyCommit = enemyCtrl.CommitPreparedMap();
            Assert.That(enemyCommit.Success, Is.True, enemyCommit.Error);

            var player = context.Scene.DynamicObjectRoot.GetComponentInChildren<PlayerView>(true);
            var enemy = context.Scene.DynamicObjectRoot.GetComponentInChildren<EnemyView>(true);
            Assert.NotNull(player);
            Assert.NotNull(enemy);

            var enemyId = 0;
            foreach (var unit in Game.Interface.GetModel<UnitModel>().Units.Values)
            {
                if (unit.UnitType == UnitType.Enemy)
                {
                    enemyId = unit.UnitId;
                    break;
                }
            }

            Assert.That(enemyId, Is.GreaterThan(0));
            return new ControllerScenario(playerCtrl, enemyCtrl, player, enemy, enemyId);
        }

        private MapPreparationContext CreateContext(Vector3 playerPosition, Vector3 enemyPosition)
        {
            var contextObject = new GameObject("UnitBarMapContext", typeof(MapContext));
            objects.Add(contextObject);
            var mapContentRoot = new GameObject("UnitBarMapContentRoot");
            objects.Add(mapContentRoot);
            var runtimeRoot = new GameObject("UnitBarDynamicObjectRoot").transform;
            objects.Add(runtimeRoot.gameObject);
            runtimeRoot.SetParent(mapContentRoot.transform, false);

            var cameraObject = new GameObject("UnitBarMainCamera", typeof(Camera));
            objects.Add(cameraObject);
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 10f, -10f);
            cameraObject.transform.rotation = Quaternion.Euler(45f, 0f, 0f);

            var mapContext = contextObject.GetComponent<MapContext>();
            SetPrivateField(mapContext, "mapContentRoot", mapContentRoot);
            SetPrivateField(mapContext, "dynamicObjectRoot", runtimeRoot);

            return new MapPreparationContext(
                MapIds.SurvivalSample,
                7,
                mapContext,
                new[]
                {
                    new MapSpawnDefinition("player.spawn", MapIds.SurvivalSample, MapSpawnObjectType.Player, RuntimeConfigProvider.PlayerConfigId, playerPosition.x, playerPosition.y, playerPosition.z, 0f),
                    new MapSpawnDefinition("enemy.spawn", MapIds.SurvivalSample, MapSpawnObjectType.Enemy, RuntimeConfigProvider.EnemyConfigId, enemyPosition.x, enemyPosition.y, enemyPosition.z, 0f)
                });
        }

        private void AddFlatNavMesh()
        {
            if (navMeshDataInstance.valid)
            {
                navMeshDataInstance.Remove();
            }

            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one),
                    size = new Vector3(40f, 0.1f, 40f),
                    area = 0
                }
            };
            var data = NavMeshBuilder.BuildNavMeshData(
                NavMesh.GetSettingsByID(0),
                sources,
                new Bounds(Vector3.zero, new Vector3(40f, 4f, 40f)),
                Vector3.zero,
                Quaternion.identity);
            navMeshDataInstance = NavMesh.AddNavMeshData(data);
        }

        private static void RegisterPlayerCombatant()
        {
            var combat = Game.Interface.GetModel<CombatModel>();
            combat.ApplyPlayerConfig(
                new PlayerConfigDefinition(RuntimeConfigProvider.PlayerConfigId, RuntimeConfigProvider.PlayerPrefabAddress, 100, 10, 2f, 0.1f, 4f, 360f, 5f),
                false);
            combat.RegisterOrResetCombatant(PlayerModel.LocalPlayerUnitId, CombatFaction.Player, 100, 10, 0);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            target.GetType()
                .GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }

        private sealed class ControllerScenario
        {
            public ControllerScenario(PlayerCtrl playerCtrl, EnemyCtrl enemyCtrl, PlayerView player, EnemyView enemy, int enemyUnitId)
            {
                PlayerCtrl = playerCtrl;
                EnemyCtrl = enemyCtrl;
                Player = player;
                Enemy = enemy;
                EnemyUnitId = enemyUnitId;
            }

            public PlayerCtrl PlayerCtrl { get; }
            public EnemyCtrl EnemyCtrl { get; }
            public PlayerView Player { get; }
            public EnemyView Enemy { get; }
            public int EnemyUnitId { get; }

            public void ReleaseAll()
            {
                PlayerCtrl.ReleaseAll();
                EnemyCtrl.ReleaseAll();
            }
        }

        private sealed class RuntimeConfigProvider : IRuntimeConfigProvider
        {
            public const string PlayerConfigId = "player.unitbar";
            public const string EnemyConfigId = "enemy.unitbar";
            public const string PlayerPrefabAddress = "Prefabs/Characters/GrayboxPlayer";
            private const string EnemyPrefabAddress = "Prefabs/Characters/GrayboxEnemy";

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = new MapAssetDefinition(id, id);
                error = null;
                return true;
            }

            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = Array.Empty<MapSpawnDefinition>();
                error = null;
                return true;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                value = new PlayerConfigDefinition(PlayerConfigId, PlayerPrefabAddress, 100, 10, 2f, 0.1f, 4f, 360f, 5f);
                error = null;
                return id == PlayerConfigId;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                value = new EnemyConfigDefinition(
                    EnemyConfigId,
                    EnemyPrefabAddress,
                    20,
                    10,
                    8f,
                    1.35f,
                    1f,
                    2.2f,
                    string.Empty,
                    global::EnemyAttackKind.Melee,
                    0f,
                    0f,
                    string.Empty,
                    "loot.weapon.guaranteed",
                    string.Empty,
                    0,
                    0f);
                error = null;
                return id == EnemyConfigId;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                value = new LootConfigDefinition(id, "loot", ItemIds.MetalParts, 1);
                error = null;
                return id == "loot.weapon.guaranteed";
            }

            public bool TryGetItem(int id, out ItemDefinition value, out string error)
            {
                value = new ItemDefinition(ItemIds.MetalParts, "Metal Parts", "Metal Parts", "item.metal_parts.name", "item.metal_parts.desc", string.Empty, global::ItemType.Material, 5, global::ItemUseEffect.None, 0);
                error = id == ItemIds.MetalParts ? null : "missing item";
                return id == ItemIds.MetalParts;
            }

            public bool TryGetItems(out IReadOnlyList<ItemDefinition> values, out string error)
            {
                values = new[] { new ItemDefinition(ItemIds.MetalParts, "Metal Parts", "Metal Parts", "item.metal_parts.name", "item.metal_parts.desc", string.Empty, global::ItemType.Material, 5, global::ItemUseEffect.None, 0) };
                error = null;
                return true;
            }

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
            {
                value = default;
                error = "missing extraction";
                return false;
            }
        }

        private sealed class PrefabResourceLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : UnityEngine.Object
            {
                return AssetDatabase.LoadAssetAtPath<T>(ToAssetPath(path));
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
            {
                onLoaded?.Invoke(LoadAsset<T>(path));
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle) { }

            private static string ToAssetPath(string path)
            {
                switch (path)
                {
                    case RuntimeConfigProvider.PlayerPrefabAddress:
                        return PlayerPrefabPath;
                    case "Prefabs/Characters/GrayboxEnemy":
                        return EnemyPrefabPath;
                    default:
                        return path;
                }
            }
        }

        private sealed class TrackingViewInstanceProvider : IViewInstanceProvider
        {
            private readonly List<UnityEngine.Object> objects;

            public TrackingViewInstanceProvider(List<UnityEngine.Object> objects)
            {
                this.objects = objects;
            }

            public T Instantiate<T>(T prefab, Transform parent, Vector3 position, Quaternion rotation) where T : Component
            {
                var instance = UnityEngine.Object.Instantiate(prefab, position, rotation, parent);
                objects.Add(instance.gameObject);
                return instance;
            }

            public void Release(GameObject instance)
            {
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }
    }
}
