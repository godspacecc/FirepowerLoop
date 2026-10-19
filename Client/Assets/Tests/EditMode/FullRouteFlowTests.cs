using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using FirepowerLoop.Arcade;

namespace Game.Tests
{
    /// <summary>后续主题逐轮事务的定向回归，仅使用临时存档和内存表现端口。</summary>
    public sealed class FullRouteFlowTests
    {
        private Game architecture;
        private ArcadeCombatSystem combat;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;
        private string directory;
        private View view;
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp] public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "FirepowerFullRoute-" + Guid.NewGuid().ToString("N"));
            architecture = new Game();
            var run = new ArcadeRunModel(); architecture.RegisterModel(run);
            architecture.RegisterModel(new ArcadeCombatModel());
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(
                File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog, out var error), Is.True, error);
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            var runs = new ArcadeRunSystem(); architecture.RegisterSystem(runs); runs.ConfigureForTests(directory);
            var data = new ArcadeRunSaveData { RunId = Guid.NewGuid().ToString("N"), IsCasualRoute = true,
                ArenaConfigNumber = 3, CompletedArenaCount = 1, NextArenaNumber = 2, UpdatedAtUtc = DateTime.UtcNow.ToString("O") };
            data.Checkpoint.ArenaNumber = 2;
            data.EquipmentBySlot[ArcadeRunDefaults.WeaponSlotId] = ArcadeRunDefaults.BasicWeaponId;
            Assert.That(new ArcadeRunFileStore(directory).Save(data, out error), Is.True, error);
            run.CommitSafeNode(data); run.EnterArena();
            combat = new ArcadeCombatSystem(); ((ICanSetArchitecture)combat).SetArchitecture(architecture);
            runtime = new ArcadeCombatRuntime { ArenaNumber = 2, PlayerHealth = 73, PlayerMaxHealth = 100 };
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            Set("catalog", catalog); Set("arena", System.Linq.Enumerable.First(catalog.Arenas, a => a.ArenaNumber == 3)); Set("random", new System.Random(37));
            view = new View(); Set("view", view);
        }
        [TearDown] public void TearDown()
        {
            architecture.GetModel<ArcadeCombatModel>().Clear();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        private void Set(string name, object value) => typeof(ArcadeCombatSystem).GetField(name, Flags).SetValue(combat, value);
        private object Call(string name, params object[] values) => typeof(ArcadeCombatSystem).GetMethod(name, Flags).Invoke(combat, values);

        [Test] public void EveryLaterCasualThemeUsesRoundLayoutTransactions()
        {
            var method = typeof(ArcadeCombatSystem).GetMethod("UsesRoundLayouts", Flags);
            Assert.That(method, Is.Not.Null, "后续主题尚未接入轮级地图事务");
            Assert.That((bool)method.Invoke(combat, new object[] { runtime }), Is.True);
        }
        [Test] public void LaterClearDoesNotAdvanceOnFireOrTimeout()
        {
            Call("BeginWave", runtime, 0); runtime.SpawnQueue.Clear();
            Call("TickWaveTransition", runtime, 0f);
            runtime.Firing = true; Call("TickWaveTransition", runtime, 600f);
            Assert.That(runtime.WaveIndex, Is.Zero, "后续主题也必须确认实体出口");
            Assert.That(runtime.SpawnQueue, Is.Empty);
        }
        [Test] public void LaterRoundsUseDifferentBlockingGeometry()
        {
            runtime.WaveIndex = 0; var first = (IReadOnlyList<ArcadeObstacleDefinition>)Call("ObstaclesFor", runtime);
            runtime.WaveIndex = 1; var second = (IReadOnlyList<ArcadeObstacleDefinition>)Call("ObstaclesFor", runtime);
            Assert.That(second, Is.Not.EqualTo(first), "各轮必须使用实际不同的布局阻挡");
        }

        [Test] public void LaterRoundSavesCompleteNodeBeforeEnemyGeneration()
        {
            Call("StartContentRound", runtime, 0, null);
            var saved = new ArcadeRunFileStore(directory).Inspect().Data.RoundCheckpoint;
            Assert.That(saved.MapId, Is.EqualTo("arena.3.round.1"));
            Assert.That(saved.PlayerState.PlayerHealth, Is.EqualTo(73));
            Assert.That(saved.PlayerState.Bombs, Is.EqualTo(1));
            Assert.That(runtime.IslandTransitionPending, Is.False);
            Assert.That(runtime.Enemies, Is.Empty);
            Assert.That(runtime.SpawnQueue.Count, Is.EqualTo(150));
        }

        [Test] public void LaterMapWaitFreezesEffectsAndDoesNotCommitUntilReady()
        {
            view.Ready = false;
            runtime.TemporaryWeaponId = "arcade.weapon.rapid"; runtime.TemporaryWeaponRemaining = 7;
            Call("StartContentRound", runtime, 0, null); combat.Tick(runtime, 30);
            Assert.That(runtime.IslandTransitionPending, Is.True);
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(7));
            Assert.That(new ArcadeRunFileStore(directory).Inspect().Data.RoundCheckpoint, Is.Null);
            view.Ready = true; combat.Tick(runtime, 30);
            Assert.That(runtime.IslandTransitionPending, Is.False);
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(7));
            Assert.That(new ArcadeRunFileStore(directory).Inspect().Data.RoundCheckpoint.RoundNumber, Is.EqualTo(1));
        }

        [Test] public void LaterExitCarriesRemainingEffectAndSavesOnlyOneNextRound()
        {
            Call("StartContentRound", runtime, 0, null);
            runtime.TemporaryWeaponId = "arcade.weapon.rapid"; runtime.TemporaryWeaponRemaining = 6.5f;
            runtime.SpawnQueue.Clear(); Call("TickWaveTransition", runtime, 0f);
            var layout = System.Linq.Enumerable.First(catalog.RoundLayouts, p => p.ArenaConfigNumber == 3 && p.RoundNumber == 1);
            runtime.PlayerPosition = new Vector2(layout.ExitX, layout.ExitY);
            Call("TickContentExit", runtime);
            Call("TickContentExit", runtime);
            Assert.That(runtime.WaveIndex, Is.EqualTo(1));
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(6.5f));
            Assert.That(new ArcadeRunFileStore(directory).Inspect().Data.RoundCheckpoint.RoundNumber, Is.EqualTo(2));
        }

        [Test] public void DedicatedBossFollowsFourthRoundAndBombCannotSkipIt()
        {
            Set("arena", System.Linq.Enumerable.First(catalog.Arenas, a => a.ArenaNumber == 4));
            Call("BeginWave", runtime, 3); runtime.SpawnQueue.Clear();
            Call("TickWaveTransition", runtime, 0f);
            Assert.That(runtime.ThemeBossStarted, Is.True);
            Assert.That(runtime.WaveIndex, Is.EqualTo(3));
            Assert.That(runtime.AwaitingExit, Is.False);
            Assert.That(runtime.Enemies.Count, Is.EqualTo(1));
            runtime.Bombs = 1; Call("UseBomb", runtime);
            Assert.That(runtime.Enemies.ContainsKey(runtime.ContentBossEnemyId), Is.True);
            var boss = runtime.Enemies[runtime.ContentBossEnemyId];
            Assert.That(boss.ContentBossKind, Is.EqualTo(1));
            Call("KillEnemy", runtime, boss, false);
            Assert.That(runtime.ThemeBossDefeated, Is.True);
            Assert.That(runtime.AwaitingExit, Is.True);
            Assert.That(runtime.FinalBossDefeated, Is.False);
        }

        [Test] public void RoundRetryRestoresSurvivingCrateHealthWithoutRefreshingIt()
        {
            var run = architecture.GetModel<ArcadeRunModel>();
            var node = run.Current.Clone(); node.ArenaConfigNumber = 2;
            run.CommitSafeNode(node); run.EnterArena();
            Set("arena", System.Linq.Enumerable.First(catalog.Arenas, a => a.ArenaNumber == 2));
            Call("StartContentRound", runtime, 0, null);
            runtime.Crates[1].Damage(13, false);
            Call("StartContentRound", runtime, 1, null);
            var saved = new ArcadeRunFileStore(directory).Inspect().Data.RoundCheckpoint;
            Assert.That(saved.CrateHealth[1], Is.EqualTo(17));
            runtime.Crates.Clear(); runtime.CratesInitialized = false;
            Call("StartContentRound", runtime, 1, saved);
            Assert.That(runtime.Crates[1].Health, Is.EqualTo(17));
        }
        private sealed class View : IArcadeCombatViewPort
        {
            public bool Ready = true;
            public bool FirstPerson;
            public int Round;
            public bool TreasureEntranceShown;
            public bool IsIslandRoundReady => Ready;
            public bool IsIslandTreasureReady => Ready;
            public bool IsFirstPersonActive => FirstPerson;
            public bool TryBeginFirstPerson(out string error) { FirstPerson = true; error = null; return true; }
            public bool TryReadFirstPersonAim(out Vector2 aim) { aim = Vector2.up; return FirstPerson; }
            public void EndFirstPerson() { FirstPerson = false; }
            public void SetIslandRound(int number) { Round = number; }
            public void SetTreasureRoom(ArcadeTreasureRoomParameters p, bool inRoom, bool discovered, bool open, IReadOnlyList<int> remainingIds)
                { TreasureEntranceShown = !inRoom && discovered; }
            public bool IsActive => true;
            public void Begin(int n, float w, float h, IReadOnlyList<ArcadeObstacleDefinition> o) { }
            public void End() { }
            public bool TryScreenToArena(Vector2 s, out Vector2 p) { p = default; return false; }
            public void SetPlayer(Vector2 p, Vector2 f, bool v, bool i) { }
            public void SetPlayerHealth(int c, int m) { }
            public void SetEnemy(int id, string config, Vector2 p, int c, int m, bool telegraph = false, int attackSequence = 0, bool persistentHealthBar = false) { }
            public void RemoveEnemy(int id, bool playDeath = false) { }
            public void SetProjectile(int id, bool hostile, Vector2 p, Vector2 d) { }
            public void RemoveProjectile(int id) { }
            public void SetDrop(int id, ArcadeDropDefinition d, Vector2 p) { }
            public void RemoveDrop(int id) { }
            public void SetRemotePlayer(long id, Vector2 p, Vector2 f, bool v, bool i, int c, int m) { }
            public void RemoveRemotePlayer(long id) { }
            public void TickPlayerFeedback(float s) { }
        }
    }
}
