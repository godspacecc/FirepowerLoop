using System;
using System.IO;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;

namespace Game.Tests
{
    /// <summary>独立街机存档事务；夹具不注册基地、背包、仓库或奖励钱包，使用真实配置。</summary>
    public sealed class FirepowerLoopRunTests
    {
        private string root;
        private Game architecture;
        private ArcadeRunSystem run;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "FirepowerLoopTests", Guid.NewGuid().ToString("N"));
            architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(architecture, true);
            architecture.RegisterModel(new ArcadeRunModel());
            var project = Environment.GetEnvironmentVariable("FIREPOWER_LOOP_TEST_ROOT") ?? Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../.."));
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() => new global::Tables(
                name => new ByteBuf(File.ReadAllBytes(Path.Combine(project, "Client/Assets/Game/Config", name + ".bytes"))))));
            architecture.RegisterSystem(new ArcadeRunSystem());
            run = architecture.GetSystem<ArcadeRunSystem>();
            run.ConfigureForTests(root);
        }

        [TearDown]
        public void TearDown()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void NewRunAndRecoveryNeedNoBaseAndKeepIdentity()
        {
            Assert.That(run.TryStartConfiguredRun(out var first, out var error), Is.True, error);
            Assert.That(run.TryResume(out var restored, out error), Is.True, error);
            Assert.That(restored.RunId, Is.EqualTo(first.RunId));
            Assert.That(restored.Shop.GenerationSeed, Is.EqualTo(first.Shop.GenerationSeed));
            Assert.That(File.Exists(Path.Combine(root, "Saves/base.json")), Is.False);
        }

        [Test]
        public void BrokenRecoveryCannotOverwriteExistingFile()
        {
            var store = new ArcadeRunFileStore(root);
            Directory.CreateDirectory(store.SavesDirectory);
            File.WriteAllText(store.RunPath, "broken-player-progress");
            Assert.That(run.TryResume(out _, out _), Is.False);
            Assert.That(run.TryStartConfiguredRun(out _, out _), Is.False);
            Assert.That(File.ReadAllText(store.RunPath), Is.EqualTo("broken-player-progress"));
        }

        [Test]
        public void LastDeathPersistsResultAndCannotResumeBattle()
        {
            Assert.That(run.TryStartConfiguredRun(out _, out var error), Is.True, error);
            Assert.That(run.TryContinue(out _, out error), Is.True, error);
            architecture.GetModel<ArcadeRunModel>().SetTransientLives(1);
            Assert.That(run.TryHandleDeath(out var result, out error), Is.EqualTo(ArcadeDeathResult.RunFailed), error);
            Assert.That(result.Outcome, Is.EqualTo("failed"));
            Assert.That(run.TryResume(out var restored, out error), Is.True, error);
            Assert.That(restored.RemainingLives, Is.Zero);
            Assert.That(architecture.GetModel<ArcadeRunModel>().Phase, Is.EqualTo(ArcadeRunPhase.Result));
            Assert.That(run.TryContinue(out _, out _), Is.False);
            Assert.That(run.TryStartConfiguredRun(out _, out _), Is.False);
        }

        [Test]
        public void ConfirmedAbandonResultDeletesOnlyThisRunWithoutWallet()
        {
            Assert.That(run.TryStartConfiguredRun(out _, out var error), Is.True, error);
            Assert.That(run.TryAbandon(out error), Is.True, error);
            Assert.That(run.InspectPending().Data.Outcome, Is.EqualTo("abandoned"));
            Assert.That(run.TrySettle(out _, out error), Is.True, error);
            Assert.That(run.InspectPending().Status, Is.EqualTo(SaveFileStatus.Missing));
            Assert.That(Directory.GetFiles(root, "*", SearchOption.AllDirectories), Is.Empty);
        }

        [Test]
        public void FailedResultAcknowledgementRemainsRetryable()
        {
            Assert.That(run.TryStartConfiguredRun(out var first, out var error), Is.True, error);
            Assert.That(run.TryAbandon(out error), Is.True, error);
            run.FailNextDeleteForTests();
            Assert.That(run.TrySettle(out _, out _), Is.False);
            Assert.That(run.InspectPending().Data.RunId, Is.EqualTo(first.RunId));
            Assert.That(run.TrySettle(out _, out error), Is.True, error);
        }

        [Test]
        public void FailedAbandonSaveKeepsOriginalNodeAndRuntime()
        {
            Assert.That(run.TryStartConfiguredRun(out var first, out var error), Is.True, error);
            var store = new ArcadeRunFileStore(root);
            using (var locked = new FileStream(store.RunPath, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.That(run.TryAbandon(out _), Is.False);
            Assert.That(architecture.GetModel<ArcadeRunModel>().Current.HasResult, Is.False);
            Assert.That(run.InspectPending().Data.RunId, Is.EqualTo(first.RunId));
            Assert.That(run.InspectPending().Data.HasResult, Is.False);
        }

        [Test]
        public void OnlineResultReceiptIsIsolatedByPlayerAndRequiresAcknowledgement()
        {
            var first = new OnlineArcadeResultFileStore(101, root);
            var second = new OnlineArcadeResultFileStore(102, root);
            var receipt = new OnlineArcadeResultData { RunId = 123, Outcome = "victory",
                Scores = new System.Collections.Generic.Dictionary<string, long> { ["101"] = 12000, ["102"] = 8000 } };
            Assert.That(first.TrySave(receipt, out var error), Is.True, error);
            Assert.That(first.TryRead(out var restored, out error), Is.True, error);
            Assert.That(restored.Scores["101"], Is.EqualTo(12000));
            Assert.That(second.TryRead(out var other, out error), Is.True, error);
            Assert.That(other, Is.Null);
            Assert.That(first.TryAcknowledge(out error), Is.True, error);
            Assert.That(first.TryRead(out var cleared, out error), Is.True, error);
            Assert.That(cleared, Is.Null);
        }

        [Test]
        public void UnfinishedRunCannotBeAcknowledgedAsResult()
        {
            Assert.That(run.TryStartConfiguredRun(out var first, out var error), Is.True, error);
            Assert.That(run.TrySettle(out _, out _), Is.False);
            Assert.That(run.InspectPending().Data.RunId, Is.EqualTo(first.RunId));
            Assert.That(run.InspectPending().Data.HasResult, Is.False);
        }

        [Test]
        public void InvalidOnlineResultCannotOverwriteSavedScores()
        {
            var store = new OnlineArcadeResultFileStore(101, root);
            var valid = new OnlineArcadeResultData { RunId = 123, Outcome = "failed",
                Scores = new System.Collections.Generic.Dictionary<string, long> { ["101"] = 12000 } };
            Assert.That(store.TrySave(valid, out var error), Is.True, error);
            var invalid = new OnlineArcadeResultData { RunId = 124, Outcome = "failed",
                Scores = new System.Collections.Generic.Dictionary<string, long> { ["101"] = -1 } };
            Assert.That(store.TrySave(invalid, out _), Is.False);
            Assert.That(store.TryRead(out var restored, out error), Is.True, error);
            Assert.That(restored.RunId, Is.EqualTo(123));
            Assert.That(restored.Scores["101"], Is.EqualTo(12000));
        }
    }
}
