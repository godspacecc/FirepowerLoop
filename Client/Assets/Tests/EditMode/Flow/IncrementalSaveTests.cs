using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>验证增量文件协议的真实磁盘结果与恢复，不访问玩家的正式存档目录。</summary>
    public sealed class IncrementalSaveTests
    {
        private string root;
        private SaveFileStore store;
        [SetUp] public void SetUp()
        { root = Path.Combine(Path.GetTempPath(), "AnotherDayIncremental", Guid.NewGuid().ToString("N")); store = new SaveFileStore(root); }
        [TearDown] public void TearDown() { if (Directory.Exists(root)) Directory.Delete(root, true); }
        private static BaseSaveData Data() => new BaseSaveData
        {
            UpdatedAtUtc = "initial",
            Sections = { ["condition"] = new JObject { ["water"] = 80, ["tili"] = 100 },
                ["bag"] = new JObject { ["slots"] = new JArray(new JObject { ["index"] = 0, ["itemId"] = 9, ["amount"] = 4 }) },
                ["exploration"] = new JObject { ["visited"] = new JArray("tree.1") } }
        };
        private JObject Manifest()
        { var m = JObject.Parse(File.ReadAllText(store.BasePath)); Assert.That((int?)m["Format"], Is.EqualTo(1)); return m; }
        private string FileOf(string name) => Path.Combine(Path.GetDirectoryName(store.BasePath), (string)Manifest()[name]);
        private void Save(BaseSaveData d) => Assert.That(store.SaveBase(d, out var error), Is.True, error);

        [Test] public void ScalarChangeLeavesInitialSnapshotUntouchedAndWritesOnlyThatField()
        {
            var d = Data(); Save(d); var snapshot = File.ReadAllBytes(FileOf("Snapshot"));
            d.Sections["condition"]["water"] = 79; d.UpdatedAtUtc = "next"; Save(d);
            CollectionAssert.AreEqual(snapshot, File.ReadAllBytes(FileOf("Snapshot")));
            var journal = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(FileOf("Journal")));
            Assert.That(journal, Does.Contain("water")); Assert.That(journal, Does.Not.Contain("tili"));
            Assert.That(journal, Does.Not.Contain("slots"));
            Assert.That(new SaveFileStore(root).InspectBase().Data.Sections["condition"]["water"].Value<int>(), Is.EqualTo(79));
        }
        [Test] public void NoActualChangeDoesNotAppendEvenWhenTimestampChanges()
        {
            var d = Data(); Save(d); var journal = FileOf("Journal"); var length = new FileInfo(journal).Length;
            d.UpdatedAtUtc = "unneeded"; Save(d);
            Assert.That(new FileInfo(journal).Length, Is.EqualTo(length));
            Assert.That(new SaveFileStore(root).InspectBase().Data.UpdatedAtUtc, Is.EqualTo("initial"));
        }
        [Test] public void SlotRemovalAndHarvestAdditionCommitTogether()
        {
            var d = Data(); Save(d); ((JArray)d.Sections["bag"]["slots"]).Clear();
            ((JArray)d.Sections["exploration"]["visited"]).Add("tree.2"); Save(d);
            var restored = new SaveFileStore(root).InspectBase(); Assert.That(restored.IsValid, Is.True, restored.Error);
            Assert.That(restored.Data.Sections["bag"]["slots"].Count(), Is.Zero);
            CollectionAssert.AreEquivalent(new[] { "tree.1", "tree.2" }, restored.Data.Sections["exploration"]["visited"].Values<string>());
        }
        [Test] public void EntityDeletionAndFieldUpdateUseStableIdentity()
        {
            var d = Data(); d.World.Loot.Add(new ExpeditionLootSaveData { RuntimeId = 1, ConfigId = "wood", RemainingAmount = 4 });
            d.World.Loot.Add(new ExpeditionLootSaveData { RuntimeId = 2, ConfigId = "water", RemainingAmount = 3 }); Save(d);
            d.World.Loot.RemoveAt(0); d.World.Loot[0].RemainingAmount = 2; Save(d);
            var restored = new SaveFileStore(root).InspectBase(); Assert.That(restored.IsValid, Is.True, restored.Error);
            Assert.That(restored.Data.World.Loot.Single().RuntimeId, Is.EqualTo(2));
            Assert.That(restored.Data.World.Loot.Single().RemainingAmount, Is.EqualTo(2));
        }
        [Test] public void IncompleteTailRecoversPreviousCompleteTransactionAndCanRetry()
        {
            var d = Data(); Save(d); d.Sections["condition"]["water"] = 79; Save(d);
            var journal = FileOf("Journal"); var committed = new FileInfo(journal).Length;
            using (var stream = new FileStream(journal, FileMode.Append)) stream.Write(new byte[] { 1, 2, 3 }, 0, 3);
            var fresh = new SaveFileStore(root); var read = fresh.InspectBase();
            Assert.That(read.IsValid, Is.True, read.Error); Assert.That(read.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(read.Data.Sections["condition"]["water"].Value<int>(), Is.EqualTo(79));
            d.Sections["condition"]["water"] = 78;
            Assert.That(fresh.SaveBase(d, out var error), Is.True, error);
            Assert.That(new FileInfo(journal).Length, Is.GreaterThan(committed));
            Assert.That(new SaveFileStore(root).InspectBase().Data.Sections["condition"]["water"].Value<int>(), Is.EqualTo(78));
        }
        [Test] public void CompletedRecordCorruptionIsRejected()
        {
            var d = Data(); Save(d); d.Sections["condition"]["water"] = 79; Save(d);
            var path = FileOf("Journal"); var bytes = File.ReadAllBytes(path); bytes[bytes.Length / 2] ^= 1; File.WriteAllBytes(path, bytes);
            Assert.That(new SaveFileStore(root).InspectBase().Status, Is.EqualTo(SaveFileStatus.Corrupted));
        }
        [Test] public void CompletedLengthHeaderCorruptionIsRejectedInsteadOfDropped()
        {
            var d=Data();Save(d);d.Sections["condition"]["water"]=79;Save(d);
            var path=FileOf("Journal");var bytes=File.ReadAllBytes(path);var length=BitConverter.ToInt32(bytes,4);
            Array.Copy(BitConverter.GetBytes(length+1000),0,bytes,4,4);File.WriteAllBytes(path,bytes);
            Assert.That(new SaveFileStore(root).InspectBase().Status,Is.EqualTo(SaveFileStatus.Corrupted));
        }
        [Test] public void ReorderedIdentityCollectionsDoNotGenerateChanges()
        {
            var d = Data(); ((JArray)d.Sections["exploration"]["visited"]).Add("tree.2"); Save(d);
            d.Sections["exploration"]["visited"] = new JArray("tree.2", "tree.1"); Save(d);
            Assert.That(new FileInfo(FileOf("Journal")).Length, Is.Zero);
        }
        [Test] public void OldFixedFilesArePreservedAndNotMigrated()
        {
            var legacy = Path.Combine(root, "Saves", "base.json"); Directory.CreateDirectory(Path.GetDirectoryName(legacy));
            File.WriteAllText(legacy, "{\"Version\":4}");
            Assert.That(store.InspectBase().Status.ToString(), Is.EqualTo("Unsupported"));
            Save(Data()); Assert.That(File.ReadAllText(legacy), Is.EqualTo("{\"Version\":4}"));
            Assert.That(new SaveFileStore(root).InspectBase().IsValid, Is.True);
        }
        [Test] public void LongJournalReplaysExactLatestValues()
        {
            var d = Data(); Save(d);
            for (var i = 0; i < 150; i++) { d.Sections["condition"]["water"] = i; Save(d); }
            var read = new SaveFileStore(root).InspectBase(); Assert.That(read.IsValid, Is.True, read.Error);
            Assert.That(read.Data.Sections["condition"]["water"].Value<int>(), Is.EqualTo(149));
        }
        [Test] public void NullAndPropertyDeletionRemainDistinct()
        {
            var d = Data(); Save(d); d.Sections["condition"]["water"] = JValue.CreateNull();
            ((JObject)d.Sections["condition"]).Remove("tili"); Save(d);
            var read = new SaveFileStore(root).InspectBase().Data.Sections["condition"];
            Assert.That(read["water"].Type, Is.EqualTo(JTokenType.Null)); Assert.That(read["tili"], Is.Null);
        }
        private void FailAppend(string stage)
        {
            var adapter = typeof(SaveFileStore).GetField("incremental", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(store);
            adapter.GetType().GetField("FailNextAppendStageForTests", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(adapter, stage);
        }
        [Test] public void PartialWriteFailureLeavesWholeOperationUncommittedAndRetryable()
        {
            var d = Data(); Save(d); d.Sections["condition"]["water"] = 79;
            ((JArray)d.Sections["exploration"]["visited"]).Add("tree.2"); FailAppend("payload");
            Assert.That(store.SaveBase(d, out _), Is.False);
            var prior = new SaveFileStore(root).InspectBase().Data;
            Assert.That(prior.Sections["condition"]["water"].Value<int>(), Is.EqualTo(80));
            Assert.That(prior.Sections["exploration"]["visited"].Count(), Is.EqualTo(1));
            Save(d); Assert.That(new SaveFileStore(root).InspectBase().Data.Sections["exploration"]["visited"].Count(), Is.EqualTo(2));
        }
        [Test] public void UncertainCompletedWriteIsAcknowledgedInsteadOfReportedAsFailure()
        {
            var d = Data(); Save(d); d.Sections["condition"]["water"] = 79; FailAppend("committed");
            Assert.That(store.SaveBase(d, out var error), Is.True, error);
            var length = new FileInfo(FileOf("Journal")).Length; Save(d);
            Assert.That(new FileInfo(FileOf("Journal")).Length, Is.EqualTo(length));
            Assert.That(new SaveFileStore(root).InspectBase().Data.Sections["condition"]["water"].Value<int>(), Is.EqualTo(79));
        }
        [Test] public void ExactDuplicateTransactionReplaysOnce()
        {
            var d = Data(); Save(d); ((JArray)d.Sections["exploration"]["visited"]).Add("tree.2"); Save(d);
            var path = FileOf("Journal"); var bytes = File.ReadAllBytes(path);
            using (var stream = new FileStream(path, FileMode.Append)) stream.Write(bytes, 0, bytes.Length);
            var read = new SaveFileStore(root).InspectBase(); Assert.That(read.IsValid, Is.True, read.Error);
            Assert.That(read.Data.Sections["exploration"]["visited"].Count(), Is.EqualTo(2));
        }
        [Test] public void ForeignGenerationRecordIsRejected()
        {
            var d = Data(); Save(d); d.Sections["condition"]["water"] = 79; Save(d); var old = File.ReadAllBytes(FileOf("Journal"));
            typeof(SaveFileStore).GetMethod("CreateBase", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(store, new object[] { Data(), null });
            File.WriteAllBytes(FileOf("Journal"), old);
            Assert.That(new SaveFileStore(root).InspectBase().Status, Is.EqualTo(SaveFileStatus.Corrupted));
        }
        [Test] public void SnapshotCorruptionDoesNotSilentlyRollbackGameplay()
        {
            Save(Data()); File.AppendAllText(FileOf("Snapshot"), " ");
            Assert.That(new SaveFileStore(root).InspectBase().Status, Is.EqualTo(SaveFileStatus.Corrupted));
        }
        [Test] public void MissingSequenceIsRejected()
        {
            var d = Data(); Save(d); d.Sections["condition"]["water"] = 79; Save(d); var path = FileOf("Journal");
            var first = new FileInfo(path).Length; d.Sections["condition"]["water"] = 78; Save(d);
            var bytes = File.ReadAllBytes(path); File.WriteAllBytes(path, bytes.Skip((int)first).ToArray());
            Assert.That(new SaveFileStore(root).InspectBase().Status, Is.EqualTo(SaveFileStatus.Corrupted));
        }
        private static ExpeditionSaveData Expedition(string map = MapIds.SurvivalSample)
        {
            var id = Guid.NewGuid().ToString("N");
            return new ExpeditionSaveData { ExpeditionId = id, MapId = map, Sections =
                { [ExpeditionTaskModel.SaveSectionId] = new JObject { ["schemaVersion"] = 1, ["mapId"] = map, ["expeditionId"] = id, ["tasks"] = new JArray() } } };
        }
        [Test] public void NewBaseInvalidatesPreviousExpeditionButAllowsCreatingNextOne()
        {
            Save(Data()); Assert.That(store.SaveExpedition(Expedition(), out var error), Is.True, error);
            typeof(SaveFileStore).GetMethod("CreateBase", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(store, new object[] { Data(), null });
            Assert.That(store.InspectExpedition().Status, Is.EqualTo(SaveFileStatus.Missing));
            var next = Expedition(); Assert.That(store.SaveExpedition(next, out error), Is.True, error);
            Assert.That(new SaveFileStore(root).InspectExpedition().Data.ExpeditionId, Is.EqualTo(next.ExpeditionId));
        }
        [Test] public void MissingBaseManifestCanRecoverOwnedExpeditionThroughBackup()
        {
            Save(Data()); var expedition = Expedition();
            Assert.That(store.SaveExpedition(expedition, out var error), Is.True, error);
            File.Copy(store.BasePath, store.BaseBackupPath, true); File.Delete(store.BasePath);
            var fresh = new SaveFileStore(root);
            Assert.That(fresh.InspectBase().IsValid, Is.True);
            var restored = fresh.InspectExpedition();
            Assert.That(restored.IsValid, Is.True, restored.Error);
            Assert.That(restored.Data.ExpeditionId, Is.EqualTo(expedition.ExpeditionId));
        }
        private static ExpeditionSaveData Valley()
        {
            var config = new LubanRuntimeConfigProvider(() => new Tables(n => new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/" + n + ".bytes").bytes)));
            Assert.That(config.TryGetCivilisationCatalog(out var templates, out var error), Is.True, error);
            Assert.That(config.TryGetValleyGenerationProfile(MapIds.CivilisationValley, out var profile, out error), Is.True, error);
            Assert.That(ValleyLayoutGenerator.TryGenerate(37, 0, profile, templates, out var layout, out error), Is.True, error);
            var data = Expedition(MapIds.CivilisationValley); data.ValleyLayout = layout;
            data.Sections[CivilisationExpeditionModel.Section] = CivilisationExpeditionModel.Empty(); return data;
        }
        [Test] public void ChangedInitialTerrainCannotBeSilentlyIgnored()
        {
            var d = Valley(); Assert.That(store.SaveExpedition(d, out var error), Is.True, error);
            d.ValleyLayout.Heights[0] += 1;
            Assert.That(store.SaveExpedition(d, out error), Is.False); Assert.That(error, Does.Contain("布局"));
        }
        [Test] public void MutableSaveValidatesHarvestIdentityAgainstInitialLayout()
        {
            var d = Valley(); Assert.That(store.SaveExpedition(d, out var error), Is.True, error);
            var mutable = (ExpeditionSaveData)typeof(ExpeditionSaveData).GetMethod("CloneMutable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(d, null);
            ((JArray)mutable.Sections[CivilisationExpeditionModel.Section]["visited"]).Add("foreign.tree");
            var args = new object[] { mutable, null };
            Assert.That((bool)typeof(SaveFileStore).GetMethod("SaveExpeditionMutable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(store, args), Is.False);
            Assert.That(args[1], Is.Not.Null.And.Not.Empty);
        }
        [Test] public void MutableValleyWriteNeverIncludesTerrainOrUnchangedResourceDefinitions()
        {
            var d = Valley(); Assert.That(store.SaveExpedition(d, out var error), Is.True, error);
            var manifest = JObject.Parse(File.ReadAllText(store.ExpeditionPath)); var folder = Path.GetDirectoryName(store.ExpeditionPath);
            var initial = File.ReadAllBytes(Path.Combine(folder, (string)manifest["Snapshot"]));
            var mutable = (ExpeditionSaveData)typeof(ExpeditionSaveData).GetMethod("CloneMutable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(d, null);
            mutable.ElapsedGameMinutes = 1; var args = new object[] { mutable, null };
            Assert.That((bool)typeof(SaveFileStore).GetMethod("SaveExpeditionMutable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(store, args), Is.True, args[1]?.ToString());
            CollectionAssert.AreEqual(initial, File.ReadAllBytes(Path.Combine(folder, (string)manifest["Snapshot"])));
            var bytes = File.ReadAllBytes(Path.Combine(folder, (string)manifest["Journal"]));
            Assert.That(bytes.Length, Is.LessThan(1024));
            Assert.That(System.Text.Encoding.UTF8.GetString(bytes), Does.Not.Contain("Heights").And.Not.Contain("Points"));
            var restored = new SaveFileStore(root).InspectExpedition(); Assert.That(restored.IsValid, Is.True, restored.Error);
            Assert.That(restored.Data.ValleyLayout.Heights.Length, Is.EqualTo(66049)); Assert.That(restored.Data.ElapsedGameMinutes, Is.EqualTo(1));
        }
        [Test] public void SystemPartialWriteFailureRetainsDirtyStateAndDoesNotCommitBag()
        {
            var support=new CivilisationHarvestTests();support.SetUp();
            try
            {
                var save=Game.Interface.GetSystem<SaveSystem>();var before=save.InspectExpedition().Data;
                var bag=Game.Interface.GetModel<PlayerBagModel>().Container;
                Assert.That(bag.TryAdd(9,1,10,out _,out var error),Is.True,error);save.MarkDirty("test partial system save");
                var native=(SaveFileStore)typeof(SaveSystem).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(save);
                var adapter=typeof(SaveFileStore).GetField("incremental",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(native);
                adapter.GetType().GetField("FailNextAppendStageForTests",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(adapter,"payload");
                Assert.That(save.Flush(out error),Is.False);
                Assert.That((bool)typeof(SaveSystem).GetField("dirty",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(save),Is.True);
                Assert.That(JToken.DeepEquals(before.Sections["another-day.player-bag"],save.InspectExpedition().Data.Sections["another-day.player-bag"]),Is.True);
                Assert.That(save.Flush(out error),Is.True,error);
                Assert.That(JToken.DeepEquals(Game.Interface.GetModel<PlayerBagModel>().Capture(),save.InspectExpedition().Data.Sections["another-day.player-bag"]),Is.True);
                Assert.That((bool)typeof(SaveSystem).GetField("dirty",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(save),Is.False);
            }
            finally{support.TearDown();}
        }
        [Test] public void SystemCannotImplicitlyCreateNewFormatFromExistingRuntime()
        {
            var support=new CivilisationHarvestTests();support.SetUp();
            try
            {
                var save=Game.Interface.GetSystem<SaveSystem>();
                var native=(SaveFileStore)typeof(SaveSystem).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(save);
                Assert.That(native.DeleteExpeditionFiles(out _),Is.True);
                var old=Path.Combine(native.SavesDirectory,"offline-expedition.json");File.WriteAllText(old,"kept old file");
                save.MarkDirty("test stale runtime");Assert.That(save.Flush(out var error),Is.False);Assert.That(error,Does.Contain("新建"));
                Assert.That(File.ReadAllText(old),Is.EqualTo("kept old file"));Assert.That(File.Exists(native.ExpeditionPath),Is.False);
            }
            finally{support.TearDown();}
        }
    }
}
