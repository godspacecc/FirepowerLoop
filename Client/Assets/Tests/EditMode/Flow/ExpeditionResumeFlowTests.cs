using System;
using System.IO;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using QFramework;

namespace Game.Tests
{
    public sealed class ExpeditionResumeFlowTests
    {
        private string runtimeRoot;
        private FakeMapContentLoader mapLoader;

        [SetUp]
        public void SetUp()
        {
            runtimeRoot = Path.Combine(Path.GetTempPath(), "AnotherDayResumeRuntime", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            TemporaryBuildingBehaviorTestSupport.Reset();
            if (Directory.Exists(runtimeRoot)) Directory.Delete(runtimeRoot, true);
        }

        [Test]
        public void DuplicateRuntimeIdsMakeExpeditionSnapshotCorrupted()
        {
            var root = Path.Combine(Path.GetTempPath(), "AnotherDayResume", Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SaveFileStore(root);
                var data = new ExpeditionSaveData { ExpeditionId = "abcdef0123456789abcdef0123456789", MapId = MapIds.SurvivalSample };
                data.World.Enemies.Add(new ExpeditionEnemySaveData { RuntimeId = 1, ConfigId = "enemy", Pose = new ExpeditionPoseData(), CurrentHealth = 10f, MaxHealth = 10f });
                data.World.Enemies.Add(new ExpeditionEnemySaveData { RuntimeId = 1, ConfigId = "enemy", Pose = new ExpeditionPoseData { X = 1f, Y = 1f, Z = 1f }, CurrentHealth = 10f, MaxHealth = 10f });
                Assert.That(store.SaveExpedition(data, out _), Is.False);
                Assert.That(store.InspectExpedition().Status, Is.EqualTo(SaveFileStatus.Missing));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void ResumePendingExpeditionAsync_CommitsTemporaryBuildingCandidateWithNewMapInstance()
        {
            InstallResumeArchitecture();
            var save = Game.Interface.GetSystem<SaveSystem>();
            var snapshot = NewSnapshot(save, "temp_arrow_tower", 420, 3);
            Assert.That(save.SaveExpedition(snapshot, out var error), Is.True, error);

            var result = Game.Interface.GetSystem<ExpeditionFlowSystem>().ResumePendingExpeditionAsync().GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True, result.Error);
            var building = Game.Interface.GetModel<TemporaryBuildingModel>();
            Assert.That(building.Buildings.TryGetValue(420, out var state), Is.True);
            Assert.That(state.MapInstanceId, Is.GreaterThan(0));
            Assert.That(state.MapInstanceId, Is.EqualTo(Game.Interface.GetSystem<MapSceneSystem>().CurrentMapInstanceId));
            Assert.That(building.ManualAttacks[420].LoadedAmmo, Is.EqualTo(3));
            Assert.That(building.Mode, Is.EqualTo(TemporaryBuildingMode.Normal));
            Assert.That(save.InspectExpedition().IsValid, Is.True, "候选成功后的副本文件必须保留");
        }

        [Test]
        public void ResumePendingExpeditionAsync_TemporaryBuildingPrepareFailureKeepsFileAndLeavesNoCommittedBuilding()
        {
            InstallResumeArchitecture();
            var save = Game.Interface.GetSystem<SaveSystem>();
            var snapshot = NewSnapshot(save, "missing-building", 421, 2);
            Assert.That(save.SaveExpedition(snapshot, out var error), Is.True, error);

            var result = Game.Interface.GetSystem<ExpeditionFlowSystem>().ResumePendingExpeditionAsync().GetAwaiter().GetResult();

            Assert.That(result.Success, Is.False);
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings, Is.Empty);
            Assert.That(save.InspectExpedition().IsValid, Is.True, "候选失败不得删除待恢复副本文件");
        }

        [Test]
        public void ResumePendingExpeditionAsync_RollsBackPreparedTemporaryViewsAndPreservesOldWorld()
        {
            InstallResumeArchitecture();
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var baseData, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(baseData, out error), Is.True, error);
            var map = Game.Interface.GetSystem<MapSceneSystem>();
            Assert.That(map.LoadMapAsync(MapIds.Base).GetAwaiter().GetResult().Success, Is.True);
            var oldHandle = Game.Interface.GetModel<MapSceneModel>().CurrentHandle;

            var snapshot = NewSnapshot(save, "temp_arrow_tower", 422, 1);
            snapshot.World.TemporaryBuildings.Add(new ExpeditionTemporaryBuildingSaveData
            {
                RuntimeId = 423,
                ConfigId = "missing-building",
                Pose = new ExpeditionPoseData { X = 4f, Y = 0f, Z = 3f, RotationY = 0f },
                LoadedAmmo = 0
            });
            Assert.That(save.SaveExpedition(snapshot, out error), Is.True, error);

            var result = Game.Interface.GetSystem<ExpeditionFlowSystem>().ResumePendingExpeditionAsync().GetAwaiter().GetResult();

            Assert.That(result.Success, Is.False);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.SameAs(oldHandle));
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.EqualTo(MapIds.Base));
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings, Is.Empty);
            Assert.That(mapLoader.UnloadCount, Is.EqualTo(1), "失败候选必须逆序释放其地图与临时建筑视图");
            Assert.That(mapLoader.LastUnloaded, Is.Not.SameAs(oldHandle));
            Assert.That(save.InspectExpedition().IsValid, Is.True);
        }

        [Test]
        public void ResumePendingExpeditionAsync_AdvancesRuntimeIdBeforeFormalNewBuildingCommit()
        {
            InstallResumeArchitecture();
            var save = Game.Interface.GetSystem<SaveSystem>();
            var restored = NewSnapshot(save, "temp_arrow_tower", 1, 3);
            Assert.That(save.SaveExpedition(restored, out var error), Is.True, error);
            var resume = Game.Interface.GetSystem<ExpeditionFlowSystem>().ResumePendingExpeditionAsync().GetAwaiter().GetResult();
            Assert.That(resume.Success, Is.True, resume.Error);

            Assert.That(Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(
                PlayerBagRiskState.InExpedition, "test resumed building", false, out error), Is.True, error);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player", string.Empty, 100, 1, 1f, 1f, 1f, 1f, 5f), false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
            var map = Game.Interface.GetSystem<MapSceneSystem>();
            TemporaryBuildingBehaviorTestSupport.MovePlayer(UnityEngine.Vector3.zero, map.CurrentMapInstanceId);
            TemporaryBuildingBehaviorTestSupport.CreateGround();
            var previousQueriesHitTriggers = UnityEngine.Physics.queriesHitTriggers;
            UnityEngine.Physics.queriesHitTriggers = false;
            int newRuntimeId;
            try
            {
                newRuntimeId = TemporaryBuildingBehaviorTestSupport.Build("temp_roadblock_wood", new UnityEngine.Vector3(1f, 0f, 1f));
            }
            finally
            {
                UnityEngine.Physics.queriesHitTriggers = previousQueriesHitTriggers;
            }
            var model = Game.Interface.GetModel<TemporaryBuildingModel>();

            Assert.That(newRuntimeId, Is.GreaterThan(1));
            Assert.That(model.Buildings.ContainsKey(1), Is.True, "恢复建筑不得被正式建造覆盖");
            Assert.That(model.Buildings.ContainsKey(newRuntimeId), Is.True);
            Assert.That(model.Buildings.Count, Is.EqualTo(2));

            Assert.That(map.LoadMapAsync(MapIds.Base).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(model.Buildings, Is.Empty, "地图释放必须同时释放恢复和新建建筑记录");
        }

        private void InstallResumeArchitecture()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            ResourceLoader.SetLoaderForTests(new LocalizationAssetResourceLoader());
            var fakePort = new FakeTemporaryBuildingViewPort();
            var config = new TestRuntimeConfigProvider(includeMapPlayerSpawn: true);
            TemporaryBuildingBehaviorTestSupport.InstallArchitecture(config, fakePort, new FakeProjectilePort());
            var architecture = (Game)Game.Interface;
            architecture.RegisterUtility<IRuntimeConfigProvider>(new TaskAwareConfigProvider(config));
            architecture.RegisterModel(new ExpeditionTaskModel());
            architecture.RegisterUtility<ITemporaryBuildingViewPort>(new TemporaryBuildingCtrl(architecture));
            mapLoader = new FakeMapContentLoader();
            TemporaryBuildingBehaviorTestSupport.RegisterMapSystem(mapLoader);
            architecture.RegisterSystem(new ExpeditionTaskSystem());
            architecture.RegisterSystem(new ExpeditionSystem());
            architecture.RegisterSystem(new ExpeditionFlowSystem());
            new UnityEngine.GameObject("Main Camera", typeof(UnityEngine.Camera)).tag = "MainCamera";
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(runtimeRoot);
        }

        private static ExpeditionSaveData NewSnapshot(SaveSystem save, string configId, int runtimeId, int ammo)
        {
            var snapshot = save.CaptureExpedition(Guid.NewGuid().ToString("N"), MapIds.SurvivalSample, DateTime.UtcNow, out var error);
            Assert.That(snapshot, Is.Not.Null, error);
            snapshot.Status = ExpeditionStatus.Playing;
            snapshot.Sections[ExpeditionTaskModel.SaveSectionId] = new JObject
            {
                ["schemaVersion"] = 1, ["mapId"] = snapshot.MapId, ["expeditionId"] = snapshot.ExpeditionId,
                ["tasks"] = new JArray(new JObject
                {
                    ["instanceId"] = snapshot.ExpeditionId + ":expedition.survival-sample.survive", ["taskId"] = "expedition.survival-sample.survive",
                    ["mapId"] = snapshot.MapId, ["expeditionId"] = snapshot.ExpeditionId, ["objectiveType"] = "Survive",
                    ["targetAmount"] = 1440f, ["currentAmount"] = 10f, ["order"] = 100, ["status"] = "InProgress"
                })
            };
            snapshot.World.TemporaryBuildings.Add(new ExpeditionTemporaryBuildingSaveData
            {
                RuntimeId = runtimeId,
                ConfigId = configId,
                Pose = new ExpeditionPoseData { X = 2f, Y = 0f, Z = 3f, RotationY = 45f },
                LoadedAmmo = ammo
            });
            return snapshot;
        }

        private sealed class TaskAwareConfigProvider : IRuntimeConfigProvider
        {
            private readonly TestRuntimeConfigProvider inner;
            public TaskAwareConfigProvider(TestRuntimeConfigProvider inner) { this.inner = inner; }
            public bool TryGetExpeditionTasks(string mapId, out System.Collections.Generic.IReadOnlyList<ExpeditionTaskDefinition> values, out string error) { values = new[] { new ExpeditionTaskDefinition("expedition.survival-sample.survive", mapId, "Survive", "task.expedition.survival_sample.survive.name", TaskObjectiveType.Survive, 1440f, 100) }; error = string.Empty; return true; }
            public bool TryGetMap(string id, out MapAssetDefinition value, out string error) => inner.TryGetMap(id, out value, out error);
            public bool TryGetMapSpawns(string mapId, out System.Collections.Generic.IReadOnlyList<MapSpawnDefinition> values, out string error) => inner.TryGetMapSpawns(mapId, out values, out error);
            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error) => inner.TryGetPlayer(id, out value, out error);
            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error) => inner.TryGetEnemy(id, out value, out error);
            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error) => inner.TryGetLoot(id, out value, out error);
            public bool TryGetLootByItemId(int id, out LootConfigDefinition value, out string error) => inner.TryGetLootByItemId(id, out value, out error);
            public bool TryGetItem(int id, out ItemDefinition value, out string error) => inner.TryGetItem(id, out value, out error);
            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error) => inner.TryGetExtraction(id, out value, out error);
            public bool TryGetProjectile(string id, out ProjectileConfigDefinition value, out string error) => inner.TryGetProjectile(id, out value, out error);
            public bool TryGetCraftRecipes(out System.Collections.Generic.IReadOnlyList<CraftRecipeDefinition> values, out string error) => inner.TryGetCraftRecipes(out values, out error);
            public bool TryGetTempBuilding(string id, out TempBuildingDefinition value, out string error) => inner.TryGetTempBuilding(id, out value, out error);
            public bool TryGetTempBuildings(out System.Collections.Generic.IReadOnlyList<TempBuildingDefinition> values, out string error) => inner.TryGetTempBuildings(out values, out error);
            public bool TryGetTempBuildingManualAttack(string id, out TempBuildingManualAttackDefinition value) => inner.TryGetTempBuildingManualAttack(id, out value);
        }
    }
}
