using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>第一处分支真实领域现场回归；不改存档、场景或代替画面验收。</summary>
    public sealed class ArcadeEarlyBranchFlowTests
    {
        private Game architecture;
        private ArcadeRunModel run;
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;
        private string root;
        private string operationError;
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            architecture = new Game();
            run = new ArcadeRunModel(); architecture.RegisterModel(run);
            architecture.RegisterModel(new ArcadeCombatModel());
            architecture.RegisterEvent<ArcadeCombatOperationFailedEvent>(evt => operationError = evt.Error);
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog, out var error), Is.True, error);
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            root = Path.Combine(Path.GetTempPath(), "ArcadeEarlyBranch-" + Guid.NewGuid().ToString("N"));
            var runs = new ArcadeRunSystem(); architecture.RegisterSystem(runs); runs.ConfigureForTests(root);
            system = new ArcadeCombatSystem(); ((ICanSetArchitecture)system).SetArchitecture(architecture);
            typeof(ArcadeCombatSystem).GetField("catalog", Flags).SetValue(system, catalog);
        }
        [TearDown] public void Cleanup()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        private void Begin(int map, bool casual = true, bool challenge = false)
        {
            var data = new ArcadeRunSaveData { RunId = Guid.NewGuid().ToString("N"), RemainingLives = 3,
                IsCasualRoute = casual, ArenaConfigNumber = map, NextArenaNumber = 2, CompletedArenaCount = 1,
                UpdatedAtUtc = DateTime.UtcNow.ToString("O"), Shop = new ArcadeShopSaveData { ArenaNumber = 2 } };
            data.EquipmentBySlot[ArcadeRunDefaults.WeaponSlotId] = catalog.Rule.BaseWeaponId;
            run.CommitSafeNode(data);
            run.EnterArena();
            runtime = new ArcadeCombatRuntime { ArenaNumber = 2, IsChallenge = challenge,
                PlayerHealth = 100, PlayerMaxHealth = 100 };
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            typeof(ArcadeCombatSystem).GetField("arena", Flags).SetValue(system, catalog.Arenas.Single(a => a.ArenaNumber == map));
        }
        private object Call(string name, params object[] args) => typeof(ArcadeCombatSystem).GetMethod(name, Flags).Invoke(system, args);
        [TestCase(2)] [TestCase(3)]
        public void BothBranches_UseCumulativeRoundsAndKeepMixedEnemyWeights(int map)
        {
            Begin(map);
            for (var wave = 0; wave < 4; wave++)
            {
                Call("BeginWave", runtime, wave);
                Assert.That(runtime.SpawnQueue.Count, Is.EqualTo(150 + wave * 10));
                var arena = catalog.Arenas.Single(a => a.ArenaNumber == map);
                var group = catalog.EnemyGroups.Single(g => g.Id == arena.WaveGroupIds[wave]);
                Assert.That(runtime.SpawnQueue.Distinct(), Is.EquivalentTo(group.Members.Select(m => m.EnemyId)));
            }
        }
        [Test] public void LegacyBranch_KeepsConfiguredQueueAndShortTransition()
        {
            Begin(2, false); Call("BeginWave", runtime, 0);
            Assert.That(runtime.SpawnQueue.Count, Is.EqualTo(6));
            runtime.SpawnQueue.Clear(); Call("TickWaveTransition", runtime, 0f);
            Assert.That(runtime.WaveTransitionRemaining, Is.LessThan(2f));
        }
        [TestCase(2)] [TestCase(3)]
        public void BranchRest_UsesReleaseThenFireAndFullTimeout(int map)
        {
            Begin(map); Call("BeginWave", runtime, 0); runtime.SpawnQueue.Clear(); runtime.Firing = true;
            Call("TickWaveTransition", runtime, 0f);
            Assert.That(runtime.WaveTransitionRemaining, Is.EqualTo(120f));
            Call("TickWaveTransition", runtime, 2f); Assert.That(runtime.WaveIndex, Is.Zero);
            runtime.Firing = false; Call("TickWaveTransition", runtime, .1f);
            runtime.Firing = true; Call("TickWaveTransition", runtime, .1f);
            Assert.That(runtime.WaveIndex, Is.EqualTo(1)); Assert.That(runtime.SpawnQueue.Count, Is.EqualTo(160));
            runtime.SpawnQueue.Clear(); runtime.Firing = false; Call("TickWaveTransition", runtime, 0f);
            Call("TickWaveTransition", runtime, 120f);
            Assert.That(runtime.WaveIndex, Is.EqualTo(2)); Assert.That(runtime.SpawnQueue.Count, Is.EqualTo(170));
        }
        [TestCase(2)] [TestCase(3)]
        public void FinalBranchClear_OpensExitsImmediatelyWithoutCash(int map)
        {
            Begin(map); Call("BeginWave", runtime, 3); runtime.SpawnQueue.Clear();
            Call("TickWaveTransition", runtime, 0f);
            Assert.That(runtime.AwaitingExit, Is.True);
            Assert.That(runtime.WaveTransitionRemaining, Is.EqualTo(300));
            Assert.That(runtime.ArenaGold, Is.Zero);
        }
        [Test] public void BranchOrdinaryClear_DoesNotIssueShoppingCash()
        {
            Begin(2); Call("BeginWave", runtime, 1); runtime.SpawnQueue.Clear();
            Call("TickWaveTransition", runtime, 0f); Assert.That(runtime.ArenaGold, Is.Zero);
        }
        [Test] public void Challenge_HasNeitherEarlyTotalNorRest()
        {
            Begin(2, true, true);
            Assert.That((bool)Call("UsesEarlyRounds", runtime), Is.False);
            Assert.That((bool)Call("UsesEarlyRest", runtime), Is.False);
        }
        [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void FullArenaCompletion_KeepsExistingWalletAndAddsNoClearCash(int map)
        {
            Begin(map);
            if (map == 1)
            {
                var data = run.Current; data.NextArenaNumber = 1; data.CompletedArenaCount = 0;
                data.Shop.ArenaNumber = 1; run.CommitSafeNode(data); run.EnterArena(); runtime.ArenaNumber = 1;
            }
            var current = run.Current; current.RunGold = 123; run.CommitSafeNode(current); run.EnterArena();
            runtime.SelectedExitConfigNumber = catalog.Arenas.Single(a => a.ArenaNumber == map).NextArenaNumbers[0];
            Call("CompleteArena", runtime);
            Assert.That(runtime.CompletionAwaitingRetry, Is.False, operationError);
            Assert.That(run.Phase, Is.EqualTo(ArcadeRunPhase.SafeShop));
            Assert.That(run.Current.RunGold, Is.EqualTo(123));
            Assert.That(new ArcadeRunFileStore(root).Inspect().Data.RunGold, Is.EqualTo(123));
        }
    }
}
