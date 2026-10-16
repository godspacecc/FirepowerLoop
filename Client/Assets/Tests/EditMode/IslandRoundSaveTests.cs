using System;
using System.IO;
using AnotherDay.Arcade;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>海岛轮级文件事务；每例使用独立临时目录与正式配置，不读取玩家存档。</summary>
    public sealed class IslandRoundSaveTests
    {
        private Game architecture;
        private ArcadeRunModel model;
        private ArcadeRunSystem system;
        private ArcadeRunFileStore store;
        private string root;

        [SetUp] public void Setup()
        {
            root = Path.Combine(Path.GetTempPath(), "FirepowerLoop-IslandRoundSave-" + Guid.NewGuid().ToString("N"));
            architecture = new Game();
            model = new ArcadeRunModel();
            architecture.RegisterModel(model);
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() =>
                new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes"))))));
            system = new ArcadeRunSystem();
            architecture.RegisterSystem(system);
            system.ConfigureForTests(root);
            store = new ArcadeRunFileStore(root);
            Assert.That(system.TryStartConfiguredRun(out _, out var error), Is.True, error);
            Assert.That(system.TryContinue(out _, out error), Is.True, error);
        }

        [TearDown] public void Cleanup()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test] public void SavePreservesWholeWorkingRunWithoutLeavingArena()
        {
            model.SetTransientTreasureScore(new ArcadeTreasureScoreState(1200, 2, 4, 2));
            var events = 0;
            architecture.RegisterEvent<ArcadeRunChangedEvent>(_ => events++);
            Assert.That(system.TrySaveIslandRound(Node(1), out var error), Is.True, error);
            Assert.That(model.Phase, Is.EqualTo(ArcadeRunPhase.Arena));
            Assert.That(events, Is.Zero);
            var saved = store.Inspect().Data;
            Assert.That(saved.Score, Is.EqualTo(1200));
            Assert.That(saved.ScoreMultiplier, Is.EqualTo(2));
            Assert.That(saved.MultiplierProgress, Is.EqualTo(4));
            Assert.That(saved.RemainingLives, Is.EqualTo(2));
            Assert.That(saved.RunGold, Is.Zero);
            Assert.That(saved.IslandRoundCheckpoint.ArenaGold, Is.EqualTo(17));
            Assert.That(saved.EmergencyResources[ArcadeRunDefaults.BombResourceId], Is.EqualTo(2));
        }

        [Test] public void FailedWriteKeepsNodeAndRetryCommitsOnce()
        {
            Assert.That(system.TrySaveIslandRound(Node(1), out var error), Is.True, error);
            model.SetTransientTreasureScore(new ArcadeTreasureScoreState(300, 1, 1, 2));
            using (var locked = new FileStream(store.RunPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.That(system.TrySaveIslandRound(Node(2), out _), Is.False);
                Assert.That(model.Current.IslandRoundCheckpoint.RoundNumber, Is.EqualTo(1));
                Assert.That(model.Current.Score, Is.EqualTo(300));
                Assert.That(model.Phase, Is.EqualTo(ArcadeRunPhase.Arena));
            }
            Assert.That(store.Inspect().Data.Score, Is.Zero);
            Assert.That(system.TrySaveIslandRound(Node(2), out error), Is.True, error);
            Assert.That(store.Inspect().Data.Score, Is.EqualTo(300));
            Assert.That(system.TrySaveIslandRound(Node(2), out error), Is.True, error);
            Assert.That(store.Inspect().Data.IslandRoundCheckpoint.RoundNumber, Is.EqualTo(2));
        }

        [Test] public void SkippedOrMutatedSameRoundCannotReplaceNode()
        {
            Assert.That(system.TrySaveIslandRound(Node(2), out _), Is.False);
            Assert.That(system.TrySaveIslandRound(Node(1), out var error), Is.True, error);
            var changed = Node(1); changed.ArenaGold = 999;
            Assert.That(system.TrySaveIslandRound(changed, out _), Is.False);
            Assert.That(system.TrySaveIslandRound(Node(3), out _), Is.False);
            Assert.That(store.Inspect().Data.IslandRoundCheckpoint.ArenaGold, Is.EqualTo(17));
        }

        [Test] public void TreasureInterruptionRestoresRoundThreeWholeState()
        {
            for (var round = 1; round <= 3; round++) Assert.That(system.TrySaveIslandRound(Node(round), out _), Is.True);
            model.SetTransientTreasureScore(new ArcadeTreasureScoreState(9000, 3, 7, 1));
            Assert.That(system.TryReturnToSafeNode(out var saved, out var error), Is.True, error);
            Assert.That(saved.IslandRoundCheckpoint.RoundNumber, Is.EqualTo(3));
            Assert.That(saved.Score, Is.Zero);
            Assert.That(saved.RemainingLives, Is.EqualTo(3));
        }

        [Test] public void RoundFourPersistsTreasureStateAndCompletionClearsCheckpoint()
        {
            for (var round = 1; round <= 3; round++) Assert.That(system.TrySaveIslandRound(Node(round), out _), Is.True);
            model.SetTransientTreasureScore(new ArcadeTreasureScoreState(9000, 3, 7, 2));
            Assert.That(system.TrySaveIslandRound(Node(4), out var error), Is.True, error);
            Assert.That(system.TryReturnToSafeNode(out var saved, out error), Is.True, error);
            Assert.That(saved.Score, Is.EqualTo(9000));
            Assert.That(saved.IslandRoundCheckpoint.RoundNumber, Is.EqualTo(4));
            Assert.That(system.TryContinue(out _, out error), Is.True, error);
            Assert.That(system.TryCompleteArena(new ArcadeArenaCompletion
            {
                ArenaNumber = 1, NextArenaConfigNumber = 2, NextShop = new ArcadeShopSaveData { ArenaNumber = 2 },
                Score = saved.Score, ScoreMultiplier = saved.ScoreMultiplier, MultiplierProgress = saved.MultiplierProgress,
                EarnedGold = 17, EmergencyResources = saved.EmergencyResources, EquipmentBySlot = saved.EquipmentBySlot
            }, out saved, out error), Is.True, error);
            Assert.That(saved.IslandRoundCheckpoint, Is.Null);
            Assert.That(saved.RunGold, Is.EqualTo(17));
        }

        [Test] public void LegacyNodeResumesWithoutInventingCheckpoint()
        {
            Assert.That(system.TryReturnToSafeNode(out var saved, out var error), Is.True, error);
            Assert.That(saved.IslandRoundCheckpoint, Is.Null);
            Assert.That(system.TrySaveIslandRound(Node(1), out _), Is.False);
            Assert.That(system.TryContinue(out _, out error), Is.True, error);
            Assert.That(system.TrySaveIslandRound(Node(1), out error), Is.True, error);
        }

        [Test] public void AbandonAndFailureRemainValidResultFiles()
        {
            Assert.That(system.TrySaveIslandRound(Node(1), out var error), Is.True, error);
            Assert.That(system.TryAbandon(out error), Is.True, error);
            Assert.That(store.Inspect().Data.IslandRoundCheckpoint, Is.Null);
            Assert.That(store.Inspect().Data.Outcome, Is.EqualTo("abandoned"));
            Assert.That(store.Delete(out error), Is.True, error);
            model.Clear();
            Assert.That(system.TryStartConfiguredRun(out _, out error), Is.True, error);
            Assert.That(system.TryContinue(out _, out error), Is.True, error);
            Assert.That(system.TrySaveIslandRound(Node(1), out error), Is.True, error);
            model.SetTransientLives(1);
            Assert.That(system.TryHandleDeath(out _, out error), Is.EqualTo(ArcadeDeathResult.RunFailed), error);
            Assert.That(store.Inspect().Data.IslandRoundCheckpoint, Is.Null);
            Assert.That(store.Inspect().Data.Outcome, Is.EqualTo("failed"));
        }

        private static ArcadeIslandRoundCheckpoint Node(int round) => new ArcadeIslandRoundCheckpoint
        {
            RoundNumber = round, MapId = "island.round." + round, PlayerHealth = 80, PlayerMaxHealth = 100,
            Bombs = 2, Dashes = 3, ArenaGold = 17, TemporaryWeaponId = "arcade.weapon.basic",
            TemporaryWeaponRemaining = 1.5f, FirstPersonRemaining = 2f, FireCooldown = .2f,
            InvulnerabilityRemaining = .1f, DashRemaining = .05f, AimDirectionY = 1f
        };
    }
}
