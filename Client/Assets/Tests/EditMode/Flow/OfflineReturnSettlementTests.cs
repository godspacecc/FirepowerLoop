using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;

namespace Game.Tests
{
    public sealed class OfflineReturnSettlementTests
    {
        private string runtimeRoot;

        [SetUp]
        public void SetUp()
        {
            runtimeRoot = Path.Combine(Path.GetTempPath(), "AnotherDaySettlementRuntime", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            TemporaryBuildingBehaviorTestSupport.Reset();
            if (Directory.Exists(runtimeRoot)) Directory.Delete(runtimeRoot, true);
        }

        [Test]
        public void SettledExpeditionIdSurvivesBaseWriteAndExpeditionCleanup()
        {
            var root = Path.Combine(Path.GetTempPath(), "AnotherDaySettlement", Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SaveFileStore(root);
                var id = "abcdef0123456789abcdef0123456789";
                var baseData = new BaseSaveData { UpdatedAtUtc = "now" };
                baseData.SettledExpeditionIds.Add(id);
                Assert.That(store.SaveBase(baseData, out var error), Is.True, error);
                Assert.That(store.SaveExpedition(CreateEmptyTaskSave(id, MapIds.SurvivalSample), out error), Is.True, error);
                Assert.That(store.DeleteExpeditionFiles(out error), Is.True, error);
                Assert.That(store.InspectBase().Data.SettledExpeditionIds, Does.Contain(id));
                Assert.That(store.InspectExpedition().Status, Is.EqualTo(SaveFileStatus.Missing));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void SettlePendingWithoutLoadingAsync_UsesFormalSettlementAndDeletesOnlyAfterBaseWrite()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            ResourceLoader.SetLoaderForTests(new LocalizationAssetResourceLoader());
            TemporaryBuildingBehaviorTestSupport.InstallArchitecture(new TestRuntimeConfigProvider(), new FakeTemporaryBuildingViewPort(), new FakeProjectilePort());
            var architecture = (Game)Game.Interface;
            architecture.RegisterSystem(new ExpeditionFlowSystem());
            var save = Game.Interface.GetSystem<SaveSystem>();
            save.ConfigureForTests(runtimeRoot);
            Assert.That(save.CreateBase(out var baseData, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(baseData, out error), Is.True, error);
            var expedition = save.CaptureExpedition(Guid.NewGuid().ToString("N"), MapIds.SurvivalSample, DateTime.UtcNow, out error);
            Assert.That(expedition, Is.Not.Null, error);
            AddEmptyTaskSection(expedition);
            expedition.Sections["another-day.player-bag"] = new JObject
            {
                ["capacity"] = InventoryDefaults.PlayerBagCapacity,
                ["riskState"] = PlayerBagRiskState.InExpedition.ToString(),
                ["slots"] = new JArray()
            };
            Assert.That(save.SaveExpedition(expedition, out error), Is.True, error);

            var result = Game.Interface.GetSystem<ExpeditionFlowSystem>().SettlePendingWithoutLoadingAsync().GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(save.InspectExpedition().Status, Is.EqualTo(SaveFileStatus.Missing));
            Assert.That(save.PrepareBase().Data.SettledExpeditionIds, Does.Contain(expedition.ExpeditionId));
            Assert.That(save.InspectBase().Data.Sections["another-day.player-bag"]["riskState"].Value<string>(),
                Is.EqualTo(PlayerBagRiskState.BaseSafe.ToString()));
        }

        [Test]
        public void SettlePendingWithoutLoadingAsync_BaseWriteFailureKeepsPendingExpeditionForRetry()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            ResourceLoader.SetLoaderForTests(new LocalizationAssetResourceLoader());
            TemporaryBuildingBehaviorTestSupport.InstallArchitecture(new TestRuntimeConfigProvider(), new FakeTemporaryBuildingViewPort(), new FakeProjectilePort());
            ((Game)Game.Interface).RegisterSystem(new ExpeditionFlowSystem());
            var save = Game.Interface.GetSystem<SaveSystem>();
            save.ConfigureForTests(runtimeRoot);
            Assert.That(save.CreateBase(out var baseData, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(baseData, out error), Is.True, error);
            var expedition = save.CaptureExpedition(Guid.NewGuid().ToString("N"), MapIds.SurvivalSample, DateTime.UtcNow, out error);
            Assert.That(expedition, Is.Not.Null, error);
            AddEmptyTaskSection(expedition);
            Assert.That(save.SaveExpedition(expedition, out error), Is.True, error);

            var paths = new SaveFileStore(runtimeRoot);
            using (new FileStream(paths.BasePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var result = Game.Interface.GetSystem<ExpeditionFlowSystem>().SettlePendingWithoutLoadingAsync().GetAwaiter().GetResult();
                Assert.That(result.Success, Is.False);
            }

            Assert.That(save.InspectExpedition().IsValid, Is.True, "结算失败不得开放基地或删除待处理副本");
        }

        [Test]
        public void SettlePendingWithoutLoadingAsync_MissingBaseCreatesCandidateSettlesOnceAndDeletesExpedition()
        {
            var save = InstallSettlementFlow();
            Assert.That(save.CreateBase(out var defaultBase, out var error), Is.True, error);
            var defaultWarehouse = defaultBase.Sections["another-day.warehouse"].DeepClone();
            var paths = new SaveFileStore(runtimeRoot);
            DeleteBaseFiles(paths);
            var expedition = CreatePendingExpedition(save, 2, out error);
            Assert.That(save.SaveExpedition(expedition, out error), Is.True, error);

            var result = Game.Interface.GetSystem<ExpeditionFlowSystem>().SettlePendingWithoutLoadingAsync().GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True, result.Error);
            var settled = save.InspectBase();
            Assert.That(settled.IsValid, Is.True, settled.Error);
            Assert.That(settled.Data.Sections["another-day.player-bag"]["slots"].Single()["amount"].Value<int>(), Is.EqualTo(2));
            Assert.That(JToken.DeepEquals(settled.Data.Sections["another-day.warehouse"], defaultWarehouse), Is.True);
            Assert.That(settled.Data.SettledExpeditionIds, Is.EquivalentTo(new[] { expedition.ExpeditionId }));
            AssertExpeditionFilesMissing(paths);
        }

        [Test]
        public void SettlePendingWithoutLoadingAsync_MissingBaseWriteFailureKeepsExpeditionAndNoBase()
        {
            var save = InstallSettlementFlow();
            var paths = new SaveFileStore(runtimeRoot);
            var expedition = CreatePendingExpedition(save, 2, out var error);
            Assert.That(save.SaveExpedition(expedition, out error), Is.True, error);
            Directory.CreateDirectory(paths.BaseTempPath);

            var failed = Game.Interface.GetSystem<ExpeditionFlowSystem>().SettlePendingWithoutLoadingAsync().GetAwaiter().GetResult();

            Assert.That(failed.Success, Is.False);
            Assert.That(save.InspectExpedition().IsValid, Is.True);
            Assert.That(File.Exists(paths.BasePath), Is.False);
            Directory.Delete(paths.BaseTempPath);

            var retried = Game.Interface.GetSystem<ExpeditionFlowSystem>().SettlePendingWithoutLoadingAsync().GetAwaiter().GetResult();
            Assert.That(retried.Success, Is.True, retried.Error);
            var settled = save.InspectBase().Data;
            Assert.That(settled.SettledExpeditionIds, Is.EquivalentTo(new[] { expedition.ExpeditionId }));
            Assert.That(settled.Sections["another-day.player-bag"]["slots"].Single()["amount"].Value<int>(), Is.EqualTo(2));
            AssertExpeditionFilesMissing(paths);
        }

        [Test]
        public void SettlePendingWithoutLoadingAsync_DeleteFailureRetriesWithoutDuplicateSettlement()
        {
            var save = InstallSettlementFlow();
            var paths = new SaveFileStore(runtimeRoot);
            var expedition = CreatePendingExpedition(save, 3, out var error);
            Assert.That(save.SaveExpedition(expedition, out error), Is.True, error);
            save.FailNextDeleteExpeditionForTests();

            var failed = Game.Interface.GetSystem<ExpeditionFlowSystem>().SettlePendingWithoutLoadingAsync().GetAwaiter().GetResult();

            Assert.That(failed.Success, Is.False);
            var afterFailure = save.InspectBase();
            Assert.That(afterFailure.IsValid, Is.True, afterFailure.Error);
            Assert.That(afterFailure.Data.SettledExpeditionIds, Is.EquivalentTo(new[] { expedition.ExpeditionId }));
            Assert.That(afterFailure.Data.Sections["another-day.player-bag"]["slots"].Single()["amount"].Value<int>(), Is.EqualTo(3));
            Assert.That(save.InspectExpedition().IsValid, Is.True);

            var retried = Game.Interface.GetSystem<ExpeditionFlowSystem>().SettlePendingWithoutLoadingAsync().GetAwaiter().GetResult();
            Assert.That(retried.Success, Is.True, retried.Error);
            var settled = save.InspectBase().Data;
            Assert.That(settled.SettledExpeditionIds, Is.EquivalentTo(new[] { expedition.ExpeditionId }));
            Assert.That(settled.Sections["another-day.player-bag"]["slots"].Single()["amount"].Value<int>(), Is.EqualTo(3));
            AssertExpeditionFilesMissing(paths);
        }

        [Test]
        public void SettlePendingWithoutLoadingAsync_CorruptedBasePreservesBaseAndExpedition()
        {
            var save = InstallSettlementFlow();
            Assert.That(save.CreateBase(out var runtimeBase, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(runtimeBase, out error), Is.True, error);
            var expedition = CreatePendingExpedition(save, 2, out error);
            Assert.That(save.SaveExpedition(expedition, out error), Is.True, error);
            var paths = new SaveFileStore(runtimeRoot);
            var corruptedText = "{ broken base";
            File.WriteAllText(paths.BasePath, corruptedText);
            var expeditionTexts = new[] { paths.ExpeditionPath, paths.ExpeditionTempPath, paths.ExpeditionBackupPath }
                .ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllText(path) : null);

            var result = Game.Interface.GetSystem<ExpeditionFlowSystem>().SettlePendingWithoutLoadingAsync().GetAwaiter().GetResult();

            Assert.That(result.Success, Is.False);
            Assert.That(File.ReadAllText(paths.BasePath), Is.EqualTo(corruptedText));
            foreach (var pair in expeditionTexts)
            {
                Assert.That(File.Exists(pair.Key), Is.EqualTo(pair.Value != null));
                if (pair.Value != null) Assert.That(File.ReadAllText(pair.Key), Is.EqualTo(pair.Value));
            }
            Assert.That(Game.Interface.GetModel<SaveModel>().CurrentBase.UpdatedAtUtc, Is.EqualTo(runtimeBase.UpdatedAtUtc));
            Assert.That(Game.Interface.GetModel<SaveModel>().CurrentBase.SettledExpeditionIds, Is.Empty);
        }

        [Test]
        public void EnterNewExpeditionAsync_FinalRiskCommitFailureCleansItsNewCandidateFiles()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            ResourceLoader.SetLoaderForTests(new LocalizationAssetResourceLoader());
            var testConfig = new TestRuntimeConfigProvider(includeMapPlayerSpawn: true);
            TemporaryBuildingBehaviorTestSupport.InstallArchitecture(testConfig, new FakeTemporaryBuildingViewPort(), new FakeProjectilePort());
            var architecture = (Game)Game.Interface;
            architecture.RegisterUtility<IRuntimeConfigProvider>(new TaskAwareConfigProvider(testConfig));
            architecture.RegisterModel(new ExpeditionTaskModel());
            TemporaryBuildingBehaviorTestSupport.RegisterMapSystem(new FakeMapContentLoader());
            architecture.RegisterSystem(new ExpeditionTaskSystem());
            architecture.RegisterSystem(new ExpeditionSystem());
            architecture.RegisterSystem(new ExpeditionFlowSystem());
            new UnityEngine.GameObject("Main Camera", typeof(UnityEngine.Camera)).tag = "MainCamera";
            var save = Game.Interface.GetSystem<SaveSystem>();
            save.ConfigureForTests(runtimeRoot);
            Assert.That(save.CreateBase(out var baseData, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(baseData, out error), Is.True, error);
            var baseLoad = Game.Interface.GetSystem<MapSceneSystem>().LoadMapAsync(MapIds.Base).GetAwaiter().GetResult();
            Assert.That(baseLoad.Success, Is.True, baseLoad.Error);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player", string.Empty, 100, 1, 1f, 1f, 1f, 1f, 5f), false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);

            // The first write creates offline-expedition.json.  A directory at the backup
            // destination makes only the subsequent InExpedition Flush fail, exercising the
            // candidate cleanup path without mocking the save API.
            var paths = new SaveFileStore(runtimeRoot);
            Directory.CreateDirectory(paths.ExpeditionBackupPath);
            var result = Game.Interface.GetSystem<ExpeditionFlowSystem>().EnterSurvivalAsync(MapIds.SurvivalSample).GetAwaiter().GetResult();

            Assert.That(result.Success, Is.False);
            Assert.That(save.InspectExpedition().Status, Is.EqualTo(SaveFileStatus.Missing));
            Assert.That(File.Exists(paths.ExpeditionPath), Is.False);
            Assert.That(File.Exists(paths.ExpeditionTempPath), Is.False);
            Assert.That(Game.Interface.GetModel<SaveModel>().CurrentDomain, Is.EqualTo(OfflineSaveDomain.Base));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().RiskState, Is.EqualTo(PlayerBagRiskState.BaseSafe));
            Assert.That(Game.Interface.GetSystem<MapSceneSystem>().CurrentMapId, Is.EqualTo(MapIds.Base));
        }

        private SaveSystem InstallSettlementFlow()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            ResourceLoader.SetLoaderForTests(new LocalizationAssetResourceLoader());
            TemporaryBuildingBehaviorTestSupport.InstallArchitecture(new TestRuntimeConfigProvider(), new FakeTemporaryBuildingViewPort(), new FakeProjectilePort());
            ((Game)Game.Interface).RegisterSystem(new ExpeditionFlowSystem());
            var save = Game.Interface.GetSystem<SaveSystem>();
            save.ConfigureForTests(runtimeRoot);
            return save;
        }

        private static ExpeditionSaveData CreatePendingExpedition(SaveSystem save, int amount, out string error)
        {
            var expedition = save.CaptureExpedition(Guid.NewGuid().ToString("N"), MapIds.SurvivalSample, DateTime.UtcNow, out error);
            if (expedition == null) return null;
            expedition.Status = ExpeditionStatus.ReturnQualified;
            AddEmptyTaskSection(expedition);
            expedition.Sections["another-day.player-bag"] = new JObject
            {
                ["capacity"] = InventoryDefaults.PlayerBagCapacity,
                ["riskState"] = PlayerBagRiskState.InExpedition.ToString(),
                ["slots"] = new JArray
                {
                    new JObject { ["index"] = 0, ["itemId"] = ItemIds.MetalParts, ["amount"] = amount }
                }
            };
            return expedition;
        }

        private static ExpeditionSaveData CreateEmptyTaskSave(string id, string mapId)
        {
            var data = new ExpeditionSaveData { ExpeditionId = id, MapId = mapId, Status = ExpeditionStatus.ReturnQualified };
            AddEmptyTaskSection(data); return data;
        }

        private static void AddEmptyTaskSection(ExpeditionSaveData data)
        {
            data.Sections[ExpeditionTaskModel.SaveSectionId] = new JObject { ["schemaVersion"] = 1, ["mapId"] = data.MapId, ["expeditionId"] = data.ExpeditionId, ["tasks"] = new JArray() };
        }

        private sealed class TaskAwareConfigProvider : IRuntimeConfigProvider
        {
            private readonly TestRuntimeConfigProvider inner;
            public TaskAwareConfigProvider(TestRuntimeConfigProvider inner) { this.inner = inner; }
            public bool TryGetExpeditionTasks(string mapId, out System.Collections.Generic.IReadOnlyList<ExpeditionTaskDefinition> values, out string error)
            { values = new[] { new ExpeditionTaskDefinition("expedition.survival-sample.survive", mapId, "Survive", "task.expedition.survival_sample.survive.name", TaskObjectiveType.Survive, 1440f, 100) }; error = string.Empty; return true; }
            public bool TryGetMap(string id, out MapAssetDefinition value, out string error) => inner.TryGetMap(id, out value, out error);
            public bool TryGetMapSpawns(string mapId, out System.Collections.Generic.IReadOnlyList<MapSpawnDefinition> values, out string error) => inner.TryGetMapSpawns(mapId, out values, out error);
            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error) => inner.TryGetPlayer(id, out value, out error);
            public bool TryGetProjectile(string id, out ProjectileConfigDefinition value, out string error) => inner.TryGetProjectile(id, out value, out error);
            public bool TryGetCraftRecipes(out System.Collections.Generic.IReadOnlyList<CraftRecipeDefinition> values, out string error) => inner.TryGetCraftRecipes(out values, out error);
            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error) => inner.TryGetEnemy(id, out value, out error);
            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error) => inner.TryGetLoot(id, out value, out error);
            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error) => inner.TryGetExtraction(id, out value, out error);
        }

        private static void DeleteBaseFiles(SaveFileStore paths)
        {
            foreach (var path in new[] { paths.BasePath, paths.BaseTempPath, paths.BaseBackupPath })
                if (File.Exists(path)) File.Delete(path);
        }

        private static void AssertExpeditionFilesMissing(SaveFileStore paths)
        {
            Assert.That(File.Exists(paths.ExpeditionPath), Is.False);
            Assert.That(File.Exists(paths.ExpeditionTempPath), Is.False);
            Assert.That(File.Exists(paths.ExpeditionBackupPath), Is.False);
        }
    }
}
