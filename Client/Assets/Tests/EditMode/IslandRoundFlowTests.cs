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
    /// <summary>海岛逐轮地图的定向回归，仅使用临时存档和内存表现端口。</summary>
    public sealed class IslandRoundFlowTests
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
            directory = Path.Combine(Path.GetTempPath(), "FirepowerIslandFlow-" + Guid.NewGuid().ToString("N"));
            architecture = new Game();
            var run = new ArcadeRunModel(); architecture.RegisterModel(run);
            architecture.RegisterModel(new ArcadeCombatModel());
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(
                File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog, out var error), Is.True, error);
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            var runs = new ArcadeRunSystem(); architecture.RegisterSystem(runs); runs.ConfigureForTests(directory);
            var data = new ArcadeRunSaveData { RunId = Guid.NewGuid().ToString("N"), IsCasualRoute = true,
                ArenaConfigNumber = 1, UpdatedAtUtc = DateTime.UtcNow.ToString("O") };
            data.EquipmentBySlot[ArcadeRunDefaults.WeaponSlotId] = ArcadeRunDefaults.BasicWeaponId;
            Assert.That(new ArcadeRunFileStore(directory).Save(data, out error), Is.True, error);
            run.CommitSafeNode(data); run.EnterArena();
            combat = new ArcadeCombatSystem(); ((ICanSetArchitecture)combat).SetArchitecture(architecture);
            runtime = new ArcadeCombatRuntime { ArenaNumber = 1, PlayerHealth = 73, PlayerMaxHealth = 100 };
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            Set("catalog", catalog); Set("arena", catalog.Arenas[0]); Set("random", new System.Random(37));
            view = new View(); Set("view", view);
        }
        [TearDown] public void TearDown()
        {
            architecture.GetModel<ArcadeCombatModel>().Clear();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        private void Set(string name, object value) => typeof(ArcadeCombatSystem).GetField(name, Flags).SetValue(combat, value);
        private object Call(string name, params object[] values) => typeof(ArcadeCombatSystem).GetMethod(name, Flags).Invoke(combat, values);

        [Test] public void ClearDoesNotAdvanceOnFireOrTimeout()
        {
            Call("BeginWave", runtime, 0); runtime.SpawnQueue.Clear();
            Call("TickWaveTransition", runtime, 0f);
            runtime.Firing = false; Call("TickWaveTransition", runtime, 1f);
            runtime.Firing = true; Call("TickWaveTransition", runtime, 600f);
            Assert.That(runtime.WaveIndex, Is.Zero, "必须主动确认出口，射击和等待均不能推进");
            Assert.That(runtime.SpawnQueue, Is.Empty);
        }
        [Test] public void IslandPositionRespectsBlockingObstacles()
        {
            var obstacles = new[] { new ArcadeObstacleDefinition(0, 0, 2, 3) };
            var result = ArcadeCombatSystem.ResolvePosition(Vector2.zero, .5f, 20, 17, obstacles,
                new ArcadeIslandRegion(20, 17));
            Assert.That(ArcadeCombatSystem.IsInsideObstacle(result, .5f, obstacles), Is.False);
        }
        [Test] public void TreasureActivityKeepsFirstPersonIdentityAndConsumesActualTime()
        {
            runtime.InTreasureRoom = true; runtime.FirstPersonRemaining = 10;
            Call("TickTimers", runtime, 2f);
            Assert.That(runtime.FirstPersonRemaining, Is.EqualTo(8));
        }
        [Test] public void ClearPickupTimeConsumesRemainingTemporaryWeaponTime()
        {
            runtime.WaveClearAwarded = true; runtime.TemporaryWeaponId = "arcade.weapon.basic";
            runtime.TemporaryWeaponRemaining = 10;
            Call("TickTimers", runtime, 2f);
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(8));
        }

        [Test] public void StartPreparesMapAndSavesWholeRoundBeforeSpawning()
        {
            Call("StartIslandRound", runtime, 0, null);
            Assert.That(runtime.IslandTransitionPending, Is.False);
            Assert.That(runtime.SpawnQueue.Count, Is.EqualTo(110));
            Assert.That(runtime.PlayerHealth, Is.EqualTo(73));
            Assert.That(view.Round, Is.EqualTo(1));
            var saved = new ArcadeRunFileStore(directory).Inspect().Data;
            Assert.That(saved.IslandRoundCheckpoint.MapId, Is.EqualTo("island.round.1"));
            Assert.That(saved.IslandRoundCheckpoint.PlayerHealth, Is.EqualTo(73));
            Assert.That(saved.IslandRoundCheckpoint.Bombs, Is.EqualTo(1));
        }

        [Test] public void ResourceWaitFreezesEffectsAndDoesNotCommitTargetUntilReady()
        {
            Call("StartIslandRound", runtime, 0, null);
            runtime.TemporaryWeaponId = "arcade.weapon.rapid"; runtime.TemporaryWeaponRemaining = 7;
            view.Ready = false;
            Call("StartIslandRound", runtime, 1, null);
            combat.Tick(runtime, 30);
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(7));
            Assert.That(runtime.Enemies, Is.Empty);
            Assert.That(new ArcadeRunFileStore(directory).Inspect().Data.IslandRoundCheckpoint.RoundNumber, Is.EqualTo(1));
            view.Ready = true; combat.Tick(runtime, 30);
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(7));
            Assert.That(new ArcadeRunFileStore(directory).Inspect().Data.IslandRoundCheckpoint.RoundNumber, Is.EqualTo(2));
        }

        [Test] public void ConfirmedExitKeepsEffectsChargesCooldownHealthAndClearsWorld()
        {
            Call("StartIslandRound", runtime, 0, null);
            var shield = System.Linq.Enumerable.First(catalog.Powerups, p => p.Behavior == ArcadePowerupBehavior.Shield);
            runtime.Powerups[shield.Behavior] = new ArcadePowerupRuntime { ProductId = shield.Id, Definition = shield,
                RemainingSeconds = 11, Charges = 1, CooldownSeconds = .3f, Position = runtime.PlayerPosition };
            runtime.TemporaryWeaponId = "arcade.weapon.rapid"; runtime.TemporaryWeaponRemaining = 6.5f;
            runtime.Projectiles.Add(41, new ArcadeProjectileRuntime { Id = 41 });
            runtime.Drops.Add(42, new ArcadeDropRuntime { Id = 42 });
            runtime.SpawnQueue.Clear(); Call("TickWaveTransition", runtime, 0f);
            runtime.IslandExitPrompt = true; Call("ConfirmIslandExit", runtime, 0, 0);
            Assert.That(runtime.WaveIndex, Is.EqualTo(1)); Assert.That(view.Round, Is.EqualTo(2));
            Assert.That(runtime.PlayerHealth, Is.EqualTo(73));
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(6.5f));
            Assert.That(runtime.Powerups[shield.Behavior].Charges, Is.EqualTo(1));
            Assert.That(runtime.Powerups[shield.Behavior].CooldownSeconds, Is.EqualTo(.3f));
            Assert.That(runtime.Projectiles, Is.Empty); Assert.That(runtime.Drops, Is.Empty);
        }

        [Test] public void SaveFailureRetriesSameCandidateWithoutExtraSupplyOrTime()
        {
            Call("StartIslandRound", runtime, 0, null);
            runtime.Bombs = 0; runtime.TemporaryWeaponId = "arcade.weapon.rapid"; runtime.TemporaryWeaponRemaining = 4;
            using (new FileStream(new ArcadeRunFileStore(directory).RunPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Call("StartIslandRound", runtime, 1, null);
                Assert.That(runtime.CompletionAwaitingRetry, Is.True); Assert.That(runtime.IslandTransitionPending, Is.True);
                combat.Tick(runtime, 20); Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(4));
            }
            Call("RetryCompletion", runtime);
            Assert.That(runtime.IslandTransitionPending, Is.False);
            Assert.That(runtime.Bombs, Is.EqualTo(1)); Assert.That(runtime.SpawnQueue.Count, Is.EqualTo(120));
            Assert.That(new ArcadeRunFileStore(directory).Inspect().Data.IslandRoundCheckpoint.TemporaryWeaponRemaining, Is.EqualTo(4));
        }

        [Test] public void EntryChangesWaitForWarningAndPreserveExistingEnemiesAndBudget()
        {
            Call("StartIslandRound", runtime, 0, null); Call("StartIslandRound", runtime, 1, null);
            Call("TickSpawning", runtime, .01f); Assert.That(runtime.Enemies, Is.Empty);
            Call("TickSpawning", runtime, 5f); Assert.That(runtime.Enemies.Count, Is.EqualTo(1));
            var first = System.Linq.Enumerable.First(runtime.Enemies.Values);
            Assert.That(first.Position.x, Is.LessThan(0));
            // 40/120预算已进场后固定转到右侧；旧敌人仍在。
            for (int i = 0; i < 39; i++) runtime.SpawnQueue.Dequeue();
            runtime.SpawnCooldown = 0;
            Call("TickSpawning", runtime, .01f); Assert.That(runtime.Enemies.Count, Is.EqualTo(1));
            Call("TickSpawning", runtime, 5f); Assert.That(runtime.Enemies.Count, Is.EqualTo(2));
            Assert.That(runtime.Enemies.ContainsKey(first.Id), Is.True);
            Assert.That(System.Linq.Enumerable.Any(runtime.Enemies.Values, e => e.Position.x > 0), Is.True);
            Assert.That(runtime.SpawnQueue.Count + runtime.Enemies.Count + 39, Is.EqualTo(120));
        }

        [Test] public void CampfireWarningAndClearDoNotDamagePlayer()
        {
            Call("StartIslandRound", runtime, 0, null); Call("StartIslandRound", runtime, 1, null);
            Call("StartIslandRound", runtime, 2, null); runtime.TreasureCompleted = true;
            Call("StartIslandRound", runtime, 3, null);
            runtime.PlayerPosition = new Vector2(catalog.Arenas[0].Opening.CampfireX, catalog.Arenas[0].Opening.CampfireY);
            Call("TickOpeningFire", runtime, .01f); Assert.That(runtime.PlayerHealth, Is.EqualTo(73));
            Call("TickOpeningFire", runtime, 5f); Assert.That(runtime.PlayerHealth, Is.EqualTo(73));
            Call("TickOpeningFire", runtime, .1f); Assert.That(runtime.PlayerHealth, Is.LessThan(73));
            int hp = runtime.PlayerHealth; runtime.WaveClearAwarded = true;
            Call("TickOpeningFire", runtime, 10f); Assert.That(runtime.PlayerHealth, Is.EqualTo(hp));
        }

        [Test] public void TreasureWaitFreezesThenActivitiesConsumeTimeAndExitCommitsFourthMap()
        {
            Call("StartIslandRound", runtime, 0, null); Call("StartIslandRound", runtime, 1, null);
            Call("StartIslandRound", runtime, 2, null); runtime.SpawnQueue.Clear();
            runtime.FirstPersonRemaining = 10; runtime.TemporaryWeaponRemaining = 12;
            runtime.TemporaryWeaponId = "arcade.weapon.rapid"; view.FirstPerson = true;
            view.Ready = false; Call("TickTreasureRoom", runtime, 0f); Call("SyncView", runtime);
            Assert.That(runtime.InTreasureRoom, Is.True); Assert.That(runtime.FirstPersonRemaining, Is.EqualTo(10));
            combat.Tick(runtime, 20); Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(12));
            view.Ready = true; combat.Tick(runtime, 20);
            Call("TickTimers", runtime, 2f); Assert.That(runtime.FirstPersonRemaining, Is.EqualTo(8));
            Call("ReturnFromTreasure", runtime);
            Assert.That(runtime.WaveIndex, Is.EqualTo(3)); Assert.That(runtime.InTreasureRoom, Is.False);
            Assert.That(runtime.FirstPersonRemaining, Is.EqualTo(8)); Assert.That(view.FirstPerson, Is.True);
            Assert.That(new ArcadeRunFileStore(directory).Inspect().Data.IslandRoundCheckpoint.RoundNumber, Is.EqualTo(4));
        }

        [Test] public void RoundRetryRestoresNodeWithoutReplenishingConsumedValues()
        {
            Call("StartIslandRound", runtime, 0, null);
            var checkpoint = new ArcadeRunFileStore(directory).Inspect().Data.IslandRoundCheckpoint;
            checkpoint.Bombs = 0; checkpoint.Dashes = 0; checkpoint.PlayerHealth = 51;
            checkpoint.TemporaryWeaponRemaining = 0; checkpoint.TemporaryWeaponId = null;
            Call("StartIslandRound", runtime, 0, checkpoint);
            Assert.That(runtime.Bombs, Is.Zero); Assert.That(runtime.Dashes, Is.Zero);
            Assert.That(runtime.PlayerHealth, Is.EqualTo(51)); Assert.That(runtime.TemporaryWeaponId, Is.Null);
            Assert.That(runtime.FirstPersonRemaining, Is.Zero);
        }

        [Test] public void FastProjectileCannotCrossShipBetweenFrames()
        {
            Call("StartIslandRound", runtime, 0, null); Call("StartIslandRound", runtime, 1, null);
            Call("StartIslandRound", runtime, 2, null);
            runtime.Projectiles.Add(50, new ArcadeProjectileRuntime { Id = 50, Position = new Vector2(-2, 3),
                Velocity = new Vector2(30, 0), RemainingSeconds = 10 });
            Call("TickProjectiles", runtime, .5f);
            Assert.That(runtime.Projectiles.ContainsKey(50), Is.False, "弹丸两端都在船外也必须被中间船壳阻挡");
        }

        [Test] public void DashCannotPassThroughShipAndPathRemainsOutsideObstacle()
        {
            Call("StartIslandRound", runtime, 0, null); Call("StartIslandRound", runtime, 1, null);
            Call("StartIslandRound", runtime, 2, null);
            runtime.PlayerPosition = new Vector2(-2, 3);
            var end = (Vector2)Call("MoveOnIsland", runtime, new Vector2(16, 0), .55f);
            Assert.That(end.x, Is.LessThan(2));
        }

        [Test] public void StaleExitConfirmationCannotAdvanceNewRound()
        {
            Call("StartIslandRound", runtime, 0, null); runtime.SpawnQueue.Clear(); Call("TickWaveTransition", runtime, 0f);
            runtime.IslandExitPrompt = true; Call("ConfirmIslandExit", runtime, 0, 0);
            runtime.SpawnQueue.Clear(); Call("TickWaveTransition", runtime, 0f); runtime.IslandExitPrompt = true;
            Call("ConfirmIslandExit", runtime, 0, 0);
            Assert.That(runtime.WaveIndex, Is.EqualTo(1));
        }

        [Test] public void FollowingThemeRestoresEffectsBeforeItsOncePerRoundSupply()
        {
            Set("arena", catalog.Arenas[1]);
            var data = architecture.GetModel<ArcadeRunModel>().Current;
            data.NextArenaNumber = 2; data.CompletedArenaCount = 1; data.ArenaConfigNumber = 2;
            var run = architecture.GetModel<ArcadeRunModel>(); run.CommitSafeNode(data); run.EnterArena();
            runtime.ArenaNumber = 2;
            var saved = new ArcadeIslandRoundCheckpoint { RoundNumber = 1, MapId = "arena.entry",
                PlayerHealth = 51, PlayerMaxHealth = 100, Bombs = 0, Dashes = 0,
                TemporaryWeaponId = "arcade.weapon.rapid", TemporaryWeaponRemaining = 6.5f };
            var entry = typeof(ArcadeCombatSystem).GetMethod("BeginArenaEntry", Flags);
            Assert.That(entry, Is.Not.Null, "需将后续场地恢复及按轮补给纳入同一入场边界");
            entry.Invoke(combat, new object[] { runtime, saved });
            Assert.That(runtime.PlayerHealth, Is.EqualTo(51));
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(6.5f));
            Assert.That(runtime.Bombs, Is.EqualTo(1)); Assert.That(runtime.Dashes, Is.EqualTo(1));
            saved.Bombs = 3; saved.Dashes = 4;
            entry.Invoke(combat, new object[] { runtime, saved });
            Assert.That(runtime.Bombs, Is.EqualTo(3)); Assert.That(runtime.Dashes, Is.EqualTo(4));
        }

        [Test] public void TransitionRejectsHudAttacksInventorySpendingAndCameraExit()
        {
            var app = new AppStateModel(); architecture.RegisterModel(app); app.CurrentState.Value = AppState.Playing;
            runtime.Bombs = 2; runtime.Dashes = 2; runtime.FirstPersonRemaining = 10;
            runtime.IslandTransitionPending = true;
            foreach (var action in new[] { ArcadeHudActionKind.Fire, ArcadeHudActionKind.Bomb,
                ArcadeHudActionKind.Dash, ArcadeHudActionKind.ExitFirstPerson })
                Call("OnHudAction", new ArcadeHudActionRequestedEvent(action));
            Assert.That(runtime.Bombs, Is.EqualTo(2)); Assert.That(runtime.Dashes, Is.EqualTo(2));
            Assert.That(runtime.FirstPersonRemaining, Is.EqualTo(10)); Assert.That(runtime.Projectiles, Is.Empty);
        }

        [Test] public void FollowingThemeResourceRetryKeepsItsOwnMapIdentity()
        {
            Set("arena", catalog.Arenas[1]);
            var run = architecture.GetModel<ArcadeRunModel>(); var data = run.Current;
            data.NextArenaNumber = 2; data.CompletedArenaCount = 1; data.ArenaConfigNumber = 2;
            run.CommitSafeNode(data); run.EnterArena(); runtime.ArenaNumber = 2;
            var saved = new ArcadeIslandRoundCheckpoint { RoundNumber = 1, MapId = "arena.entry",
                PlayerHealth = 51, PlayerMaxHealth = 100, Bombs = 1, Dashes = 1 };
            Call("BeginArenaEntry", runtime, saved); runtime.CompletionAwaitingRetry = true;
            view.Ready = false; Call("RetryCompletion", runtime);
            Assert.That(view.Round, Is.Zero, "巴比伦或林地重试不能被标为海岛第1图");
            Assert.That(runtime.PlayerHealth, Is.EqualTo(51));
        }

        [Test] public void ThirdMapTreasureEntranceCanBeDiscoveredFromReachableLand()
        {
            Call("StartIslandRound", runtime, 0, null); Call("StartIslandRound", runtime, 1, null);
            Call("StartIslandRound", runtime, 2, null);
            runtime.PlayerPosition = new Vector2(-12, -12);
            Assert.That(new ArcadeIslandRegion(19.5f, 19.5f, 3).Contains(-12, -12), Is.True);
            Call("TickTreasureRoom", runtime, 0f);
            Assert.That(runtime.TreasureDiscovered, Is.True);
            Assert.That(runtime.InTreasureRoom, Is.False, "发现入口不能绕过有限敌群清场");
        }

        [Test] public void FourthMapDoesNotShowCompletedTreasureEntrance()
        {
            Call("StartIslandRound", runtime, 0, null); Call("StartIslandRound", runtime, 1, null);
            Call("StartIslandRound", runtime, 2, null); runtime.SpawnQueue.Clear();
            Call("TickTreasureRoom", runtime, 0f); Call("SyncView", runtime);
            Call("ReturnFromTreasure", runtime); Call("SyncView", runtime);
            Assert.That(runtime.WaveIndex, Is.EqualTo(3));
            Assert.That(view.TreasureEntranceShown, Is.False);
        }

        [Test] public void ShotsDuringClearedRoundPickupKeepMovingAndExpire()
        {
            Call("StartIslandRound", runtime, 0, null); runtime.SpawnQueue.Clear();
            Call("TickWaveTransition", runtime, 0f); Call("TryFire", runtime);
            Assert.That(runtime.Projectiles.Count, Is.GreaterThan(0));
            var projectile = System.Linq.Enumerable.First(runtime.Projectiles.Values);
            var before = projectile.Position;
            combat.Tick(runtime, .05f);
            Assert.That(projectile.Position, Is.Not.EqualTo(before), "拾取阶段不能留下冻结弹丸");
            combat.Tick(runtime, 10f);
            Assert.That(runtime.Projectiles, Is.Empty); Assert.That(runtime.WaveIndex, Is.Zero);
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
