using System;
using System.IO;


using NUnit.Framework;

namespace Game.Tests
{
    public class SurvivorResultFileStoreTests
    {
        private string root;
        private Type dataType;
        private object store;
        private string PathName => Path.Combine(root, "Saves", "survivor-result-7.json");
        [SetUp] public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "SurvivorResultTests-" + Guid.NewGuid().ToString("N"));
            dataType = typeof(Game).Assembly.GetType("Game.SurvivorResultData");
            var type = typeof(Game).Assembly.GetType("Game.SurvivorResultFileStore");
            Assert.That(type, Is.Not.Null, "独立幸存者结果存储尚未实现");
            store = Activator.CreateInstance(type, 7L, root);
        }
        [TearDown] public void TearDown() { if (Directory.Exists(root)) Directory.Delete(root, true); }
        private object Data(long id, long score = 10)
        {
            var data = Activator.CreateInstance(dataType);
            foreach (var pair in new System.Collections.Generic.Dictionary<string, object> {
                {"RunId",id},{"Score",score},{"SurvivalSeconds",12.5d},{"Kills",3},{"Level",2},{"Outcome","failed"}})
                dataType.GetField(pair.Key).SetValue(data, pair.Value);
            return data;
        }
        private bool Save(object data) => (bool)store.GetType().GetMethod("TrySave").Invoke(store, new[] { data, null });
        private bool Read(out object data)
        {
            var args = new object[] { null, null };
            var ok = (bool)store.GetType().GetMethod("TryRead").Invoke(store,args);
            data = args[0]; return ok;
        }
        [Test] public void MissingFileReturnsEmpty() { Assert.That(Read(out var data), Is.True); Assert.That(data, Is.Null); }
        [Test] public void RoundTripPreservesResultAndArcadeFile()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName));
            var arcade = Path.Combine(root,"Saves","arcade-run-7.json"); File.WriteAllText(arcade,"sentinel");
            Assert.That(Save(Data(1)), Is.True); Assert.That(Read(out var data),Is.True);
            Assert.That(dataType.GetField("SurvivalSeconds").GetValue(data),Is.EqualTo(12.5d));
            Assert.That(File.ReadAllText(arcade),Is.EqualTo("sentinel"));
        }
        [Test] public void RetryOldRunDoesNotReplaceLatest()
        {
            Assert.That(Save(Data(1)),Is.True); Assert.That(Save(Data(2,20)),Is.True);
            Assert.That(Save(Data(1)),Is.True); Assert.That(Read(out var data),Is.True);
            Assert.That(dataType.GetField("RunId").GetValue(data),Is.EqualTo(2L));
            Assert.That(Save(Data(1,99)),Is.False);
        }
        [Test] public void InvalidResultPreservesOldFile()
        {
            Assert.That(Save(Data(1)),Is.True); var old=File.ReadAllText(PathName);
            Assert.That(Save(Data(2,-1)),Is.False); Assert.That(File.ReadAllText(PathName),Is.EqualTo(old));
            var invalid=Data(2); dataType.GetField("SurvivalSeconds").SetValue(invalid,double.NaN);
            Assert.That(Save(invalid),Is.False);
        }
        [Test] public void CorruptFileIsRejectedWithoutOverwrite()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)); File.WriteAllText(PathName,"{}");
            Assert.That(Read(out var data),Is.False); Assert.That(data,Is.Null);
            Assert.That(Save(Data(1)),Is.False); Assert.That(File.ReadAllText(PathName),Is.EqualTo("{}"));
        }
        [Test] public void FailedWriteRetainsPreviousResultAndAllowsRetry()
        {
            Assert.That(Save(Data(1)),Is.True); var old=File.ReadAllText(PathName);
            using(var stream=new FileStream(PathName,FileMode.Open,FileAccess.Read,FileShare.None))
                Assert.That(Save(Data(2)),Is.False);
            Assert.That(File.ReadAllText(PathName),Is.EqualTo(old)); Assert.That(Save(Data(2)),Is.True);
        }
    }
}
