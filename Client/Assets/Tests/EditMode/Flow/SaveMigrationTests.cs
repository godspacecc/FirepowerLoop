using System;
using System.IO;
using NUnit.Framework;
using QFramework;

namespace Game.Tests
{
    /// <summary>测试阶段不迁移旧格式；只验证拒绝、保留和新格式准备入口。</summary>
    public sealed class SaveMigrationTests
    {
        private string root;
        [SetUp] public void SetUp() => root=Path.Combine(Path.GetTempPath(),"AnotherDayUnsupportedSave",Guid.NewGuid().ToString("N"));
        [TearDown] public void TearDown() { TestReset.ResetArchitecture();if(Directory.Exists(root))Directory.Delete(root,true); }
        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(999999)]
        public void OldBaseVersionsAreUnsupportedWithoutChangingSource(int version)
        {
            var path=Path.Combine(root,"Saves","base.json");Directory.CreateDirectory(Path.GetDirectoryName(path));
            var source="{\"Version\":"+version+"}";File.WriteAllText(path,source);
            var store=new SaveFileStore(root);Assert.That(store.InspectBase().Status,Is.EqualTo(SaveFileStatus.Unsupported));
            Assert.That(File.ReadAllText(path),Is.EqualTo(source));Assert.That(File.Exists(store.BasePath),Is.False);
        }
        [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void OldExpeditionVersionsAreUnsupportedWithoutChangingSource(int version)
        {
            var path=Path.Combine(root,"Saves","offline-expedition.json");Directory.CreateDirectory(Path.GetDirectoryName(path));
            var source="{\"Version\":"+version+"}";File.WriteAllText(path,source);
            var store=new SaveFileStore(root);Assert.That(store.InspectExpedition().Status,Is.EqualTo(SaveFileStatus.Unsupported));
            Assert.That(File.ReadAllText(path),Is.EqualTo(source));Assert.That(File.Exists(store.ExpeditionPath),Is.False);
        }
        [Test] public void StoragePreparationPreservesLegacyUnknownNestedAndSettingsFiles()
        {
            var saves=Path.Combine(root,"Saves");Directory.CreateDirectory(saves);
            var paths=new[]{Path.Combine(saves,"base.json"),Path.Combine(saves,"offline-expedition.json"),
                Path.Combine(saves,Guid.NewGuid().ToString("N")+".json"),Path.Combine(saves,"unknown.json"),
                Path.Combine(saves,"Nested","kept.json"),Path.Combine(root,"Settings","settings.json")};
            foreach(var path in paths){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"{ broken");}
            var save=Game.Interface.GetSystem<SaveSystem>();save.ConfigureForTests(root);
            Assert.That(save.PrepareOfflineStorage(out var error),Is.True,error);
            foreach(var path in paths)Assert.That(File.ReadAllText(path),Is.EqualTo("{ broken"));
        }
        [Test] public void StorageGateFailureBlocksReadAndWriteThenRetries()
        {
            var save=Game.Interface.GetSystem<SaveSystem>();save.ConfigureForTests(root);save.FailNextOfflineStorageCleanupForTests("read gate");
            Assert.That(save.InspectBase().Status,Is.EqualTo(SaveFileStatus.Corrupted));
            save.ConfigureForTests(root);save.FailNextOfflineStorageCleanupForTests("write gate");
            Assert.That(save.SaveBase(new BaseSaveData(),out _),Is.False);
            Assert.That(save.PrepareOfflineStorage(out var error),Is.True,error);
            Assert.That(save.SaveBase(new BaseSaveData(),out error),Is.True,error);Assert.That(save.InspectBase().IsValid,Is.True);
        }
        [Test] public void NewFormatRoundTripsWithoutMigrationRegistry()
        {
            var store=new SaveFileStore(root);Assert.That(store.SaveBase(new BaseSaveData(),out var error),Is.True,error);
            Assert.That(store.InspectBase().IsValid,Is.True);Assert.That(store.CleanupLegacyFiles(out error),Is.True,error);
            Assert.That(store.InspectBase().IsValid,Is.True);
        }
    }
}
