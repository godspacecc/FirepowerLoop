using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class OfflineSaveDomainTests
    {
        private string root;

        [SetUp]
        public void SetUp() => root = Path.Combine(Path.GetTempPath(), "AnotherDayOfflineDomains", Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            TemporaryBuildingBehaviorTestSupport.Reset();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void FixedDomainsUseSeparateFilesAndIgnoreLegacySlots()
        {
            var store = new SaveFileStore(root);
            Directory.CreateDirectory(store.SavesDirectory);
            File.WriteAllText(Path.Combine(store.SavesDirectory, "0123456789abcdef0123456789abcdef.json"), "{}");
            Assert.That(store.InspectBase().Status, Is.EqualTo(SaveFileStatus.Missing));
            Assert.That(store.InspectExpedition().Status, Is.EqualTo(SaveFileStatus.Missing));

            Assert.IsTrue(store.SaveBase(new BaseSaveData { UpdatedAtUtc = "now" }, out var error), error);
            Assert.IsTrue(store.SaveExpedition(CreateEmptyTaskSave("abcdef0123456789abcdef0123456789", MapIds.Base), out error), error);
            Assert.IsTrue(File.Exists(store.BasePath));
            Assert.IsTrue(File.Exists(store.ExpeditionPath));
        }

        [Test]
        public void CorruptedPrimaryUsesValidBackupAndReportsOrigin()
        {
            var store = new SaveFileStore(root);
            Assert.IsTrue(store.SaveBase(new BaseSaveData { UpdatedAtUtc = "first" }, out var error), error);
            // 入口备份用于新 generation 发布；普通差异保存不会替换入口。
            Assert.IsTrue((bool)typeof(SaveFileStore).GetMethod("CreateBase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(store, new object[] { new BaseSaveData { UpdatedAtUtc = "second" }, null }));
            File.WriteAllText(store.BasePath, "not-json");

            var result = store.InspectBase();
            Assert.That(result.Status, Is.EqualTo(SaveFileStatus.Valid));
            Assert.IsTrue(result.FromBackup);
            Assert.That(result.Data.UpdatedAtUtc, Is.EqualTo("first"));
        }

        [Test]
        public void CorruptedPrimaryAndBackupAreNotMissing()
        {
            var store = new SaveFileStore(root);
            Directory.CreateDirectory(Path.GetDirectoryName(store.BasePath));
            File.WriteAllText(store.BasePath, "bad");
            File.WriteAllText(store.BaseBackupPath, "also-bad");

            var result = store.InspectBase();
            Assert.That(result.Status, Is.EqualTo(SaveFileStatus.Corrupted));
            Assert.That(result.Data, Is.Null);
            Assert.IsTrue(File.Exists(store.BasePath));
            Assert.IsTrue(File.Exists(store.BaseBackupPath));
        }

        [Test]
        public void ExpeditionSnapshotRoundTripsGameplayState()
        {
            var source = new ExpeditionModel();
            source.BeginTask(MapIds.SurvivalSample, 3);
            source.SetExpeditionId("abcdef0123456789abcdef0123456789");
            source.AddTaskKill();
            var snapshot = source.CaptureSave(source.CurrentExpeditionId, DateTime.UtcNow);
            snapshot.Sections[ExpeditionTaskModel.SaveSectionId] = TaskSection(snapshot.MapId, snapshot.ExpeditionId, new JArray());
            snapshot.World.Enemies.Add(new ExpeditionEnemySaveData { RuntimeId = 1, ConfigId = "enemy-1", Pose = new ExpeditionPoseData(), CurrentHealth = 10f, MaxHealth = 10f });
            var cloned = snapshot.Clone();
            Assert.That(cloned.World.Enemies.Count, Is.EqualTo(1));
            Assert.AreNotSame(snapshot.World.Enemies[0], cloned.World.Enemies[0]);

            var restored = new ExpeditionModel();
            Assert.IsTrue(restored.TryRestoreSave(snapshot, out var error), error);
            Assert.That(restored.CurrentExpeditionId, Is.EqualTo(source.CurrentExpeditionId));
            Assert.That(restored.CurrentMapId, Is.EqualTo(source.CurrentMapId));
            Assert.That(restored.TaskTargetKills, Is.EqualTo(3));
            Assert.That(restored.TaskKillCount, Is.EqualTo(1));
            Assert.That(restored.Status, Is.EqualTo(ExpeditionStatus.Playing));
        }

        [Test]
        public void RegisteredUnknownSectionsDoNotCrossDomainsThroughFormalSettlement()
        {
            InstallRuntime();
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var baseData, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(baseData, out error), Is.True, error);
            var expedition = save.CaptureExpedition("abcdef0123456789abcdef0123456789", MapIds.SurvivalSample, DateTime.UtcNow, out error);
            Assert.That(expedition, Is.Not.Null, error);
            expedition.Sections["another-day.unregistered-test"] = new JObject { ["value"] = 1 };

            Assert.That(save.ApplyExpeditionSettlement(baseData, expedition, false, out _, out error), Is.True, error);
            Assert.That(baseData.Sections.ContainsKey("another-day.unregistered-test"), Is.False);
        }

        [Test]
        public void FlushWritesOnlyTheActiveFixedDomain()
        {
            InstallRuntime();
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var baseData, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(baseData, out error), Is.True, error);
            var baseBefore = save.InspectBase().Data.UpdatedAtUtc;
            var expedition = save.CaptureExpedition("abcdef0123456789abcdef0123456789", MapIds.SurvivalSample, DateTime.UtcNow, out error);
            Assert.That(expedition, Is.Not.Null, error);
            Game.Interface.GetModel<ExpeditionModel>().BeginConfiguredExpedition(expedition.MapId, expedition.ExpeditionId, false, 0f, 0);
            Game.Interface.GetModel<ExpeditionModel>().QualifyReturn();
            Game.Interface.GetModel<ExpeditionTaskModel>().Restore(TaskSection(expedition.MapId, expedition.ExpeditionId, new JArray()));
            expedition.Status = ExpeditionStatus.ReturnQualified;
            Assert.That(save.CaptureExpeditionSections(expedition, out error), Is.True, error);
            Assert.That(save.SaveExpedition(expedition, out error), Is.True, error);
            Assert.That(save.CommitExpeditionRuntime(expedition, out error), Is.True, error);

            var handle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.SurvivalSample, 901);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, handle);
            save.MarkDirty("test active expedition flush");
            Assert.That(save.Flush(out error), Is.True, error);

            Assert.That(save.InspectBase().Data.UpdatedAtUtc, Is.EqualTo(baseBefore));
            Assert.That(save.InspectExpedition().IsValid, Is.True);
        }

        [Test]
        public void RegisteredCurrencyRejectsMissingAndMalformedCurrentVersionSections()
        {
            InstallRuntime();
            Game.Interface.RegisterModel(new CurrencyModel());
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.RegisterSection(Game.Interface.GetModel<CurrencyModel>(), out var error), Is.True, error);
            Assert.That(save.CreateBase(out var baseline, out error), Is.True, error);
            var store = new SaveFileStore(root);

            var invalidSections = new JToken[]
            {
                null,
                new JObject { ["schemaVersion"] = 1, ["gold"] = -1 },
                new JObject { ["schemaVersion"] = 1, ["gold"] = "1" },
                new JObject { ["schemaVersion"] = 2, ["gold"] = 0 }
            };
            foreach (var invalid in invalidSections)
            {
                var candidate = baseline.Clone();
                if (invalid == null) candidate.Sections.Remove(CurrencyModel.SaveSectionId);
                else candidate.Sections[CurrencyModel.SaveSectionId] = invalid;
                Assert.That(store.SaveBase(candidate, out error), Is.True, error);
                save.ConfigureForTests(root);
                var read = save.PrepareBase();
                Assert.That(read.Status, Is.EqualTo(SaveFileStatus.Corrupted));
            }
        }

        [Test]
        public void OldFixedExpeditionIsUnsupportedAndNeverMigrated()
        {
            var store = new SaveFileStore(root); Directory.CreateDirectory(store.SavesDirectory);
            var legacy = JObject.FromObject(CreateEmptyTaskSave("abcdef0123456789abcdef0123456789", MapIds.SurvivalSample));
            legacy["Version"] = 2; legacy.Remove("TaskSchemaVersion"); legacy.Remove("LegacySourceVersion"); ((JObject)legacy["Sections"]).Remove(ExpeditionTaskModel.SaveSectionId);
            var oldPath=Path.Combine(store.SavesDirectory,"offline-expedition.json");var source=legacy.ToString();
            File.WriteAllText(oldPath, source);
            var read=store.InspectExpedition();Assert.That(read.Status,Is.EqualTo(SaveFileStatus.Unsupported));
            Assert.That(File.ReadAllText(oldPath),Is.EqualTo(source));
        }

        [Test]
        public void VersionTwoWithTaskSectionIsRejectedWithoutChangingSourceFile()
        {
            var store = new SaveFileStore(root);
            Directory.CreateDirectory(Path.GetDirectoryName(store.ExpeditionPath));
            var legacy = JObject.FromObject(CreateEmptyTaskSave("abcdef0123456789abcdef0123456789", MapIds.SurvivalSample));
            legacy["Version"] = 2;
            legacy.Remove("TaskSchemaVersion");
            legacy.Remove("LegacySourceVersion");
            var source = legacy.ToString();
            File.WriteAllText(store.ExpeditionPath, source);

            var inspected = store.InspectExpedition();

            Assert.That(inspected.Status, Is.EqualTo(SaveFileStatus.Corrupted));
            Assert.That(File.ReadAllText(store.ExpeditionPath), Is.EqualTo(source));
        }

        [Test]
        public void TaskSectionCaptureFailurePreservesLegacyMarkersAndOriginalSections()
        {
            InstallRuntime();
            var save = Game.Interface.GetSystem<SaveSystem>();
            var data = new ExpeditionSaveData
            {
                ExpeditionId = "abcdef0123456789abcdef0123456789",
                MapId = MapIds.SurvivalSample,
                TaskSchemaVersion = 0,
                LegacySourceVersion = 2,
                Sections = new System.Collections.Generic.Dictionary<string, JToken>(StringComparer.Ordinal)
                {
                    ["sentinel"] = new JObject { ["value"] = 7 }
                }
            };

            Assert.That(save.CaptureExpeditionSections(data, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(data.TaskSchemaVersion, Is.Zero);
            Assert.That(data.LegacySourceVersion, Is.EqualTo(2));
            Assert.That(data.Sections.Keys, Is.EquivalentTo(new[] { "sentinel" }));
            Assert.That(data.Sections["sentinel"]?["value"]?.Value<int>(), Is.EqualTo(7));
        }

        [Test]
        public void ExpeditionFlushSynchronizesTopLevelTimeAndStatusWithTaskSection()
        {
            InstallRuntime();
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var baseData, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(baseData, out error), Is.True, error);
            var id = "abcdef0123456789abcdef0123456789";
            var definition = new ExpeditionTaskDefinition("survive", MapIds.SurvivalSample, "Task", "task", TaskObjectiveType.Survive, 2f, 1);
            Assert.That(Game.Interface.GetSystem<ExpeditionSystem>().TryBeginConfiguredExpedition(MapIds.SurvivalSample, id, new[] { definition }, out error), Is.True, error);
            var initial = Game.Interface.GetModel<ExpeditionModel>().CaptureSave(id, DateTime.UtcNow);
            Assert.That(save.CaptureExpeditionSections(initial, out error), Is.True, error);
            Assert.That(save.SaveExpedition(initial, out error), Is.True, error);
            Assert.That(save.CommitExpeditionRuntime(initial, out error), Is.True, error);

            Game.Interface.GetSystem<ExpeditionSystem>().AdvanceGameMinutes(1.25f);
            Assert.That(save.Flush(out error), Is.True, error);
            var inProgress = save.InspectExpedition();
            Assert.That(inProgress.IsValid, Is.True, inProgress.Error);
            Assert.That(inProgress.Data.ElapsedGameMinutes, Is.EqualTo(1.25f));
            Assert.That(inProgress.Data.Status, Is.EqualTo(ExpeditionStatus.Playing));
            Assert.That(inProgress.Data.Sections[ExpeditionTaskModel.SaveSectionId]["tasks"]?[0]?["status"]?.Value<string>(), Is.EqualTo("InProgress"));

            Game.Interface.GetSystem<ExpeditionSystem>().AdvanceGameMinutes(.75f);
            Assert.That(save.Flush(out error), Is.True, error);
            var completed = save.InspectExpedition();
            Assert.That(completed.IsValid, Is.True, completed.Error);
            Assert.That(completed.Data.ElapsedGameMinutes, Is.EqualTo(2f));
            Assert.That(completed.Data.Status, Is.EqualTo(ExpeditionStatus.ReturnQualified));
            Assert.That(completed.Data.Sections[ExpeditionTaskModel.SaveSectionId]["tasks"]?[0]?["status"]?.Value<string>(), Is.EqualTo("Completed"));
        }

        [Test]
        public void TaskSectionFlushPromotesSchemaVersionAfterSuccessfulCapture()
        {
            InstallRuntime(); var save = Game.Interface.GetSystem<SaveSystem>(); Assert.That(save.CreateBase(out var baseData, out var error), Is.True, error); Assert.That(save.CommitBaseRuntime(baseData, out error), Is.True, error);
            var id = "abcdef0123456789abcdef0123456789"; Game.Interface.GetModel<ExpeditionModel>().BeginConfiguredExpedition(MapIds.HeJiaVillage, id, false, 0f, 0); Game.Interface.GetModel<ExpeditionModel>().QualifyReturn(); Game.Interface.GetModel<ExpeditionTaskModel>().Restore(TaskSection(MapIds.HeJiaVillage, id, new JArray()));
            var data = Game.Interface.GetModel<ExpeditionModel>().CaptureSave(id, DateTime.UtcNow); data.TaskSchemaVersion = 0; data.LegacySourceVersion = 2;
            Assert.That(save.CaptureExpeditionSections(data, out error), Is.True, error); Assert.That(data.TaskSchemaVersion, Is.EqualTo(1)); Assert.That(data.LegacySourceVersion, Is.Zero);
        }

        private static ExpeditionSaveData CreateEmptyTaskSave(string id, string mapId)
        {
            var data = new ExpeditionSaveData { ExpeditionId = id, MapId = mapId, Status = ExpeditionStatus.ReturnQualified };
            data.Sections[ExpeditionTaskModel.SaveSectionId] = TaskSection(mapId, id, new JArray()); return data;
        }

        private static JObject TaskSection(string mapId, string id, JArray tasks) => new JObject { ["schemaVersion"] = 1, ["mapId"] = mapId, ["expeditionId"] = id, ["tasks"] = tasks };

        private void InstallRuntime()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            ResourceLoader.SetLoaderForTests(new LocalizationAssetResourceLoader());
            TemporaryBuildingBehaviorTestSupport.InstallArchitecture(
                new TestRuntimeConfigProvider(), new FakeTemporaryBuildingViewPort(), new FakeProjectilePort());
            Game.Interface.RegisterModel(new ExpeditionTaskModel());
            Game.Interface.RegisterSystem(new ExpeditionTaskSystem());
            Game.Interface.RegisterSystem(new ExpeditionSystem());
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(root);
        }
    }
}
