using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
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
            var map = new MapSceneModel(); architecture.RegisterModel(map);
            map.SetLoaded(MapIds.Arcade, null, MapPreparationMode.ArcadePlayerOnly);
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

        /// <summary>山脚实体阻挡小步和长冲刺，中央场地保持畅通。</summary>
        [TestCase(1, -16f, 10f, -1f, 0f, false)]
        [TestCase(1, -16f, 10f, -1f, 0f, true)]
        [TestCase(3, 16f, 10f, 1f, 0f, false)]
        [TestCase(3, 16f, 10f, 1f, 0f, true)]
        [TestCase(4, 10f, 16f, 0f, 1f, false)]
        [TestCase(4, 10f, 16f, 0f, 1f, true)]
        public void MountainFeetBlockWalkingAndLongDash(int round, float x, float y, float dx, float dy, bool dash)
        {
            Call("StartIslandRound", runtime, round - 1, null);
            var direction = new Vector2(dx, dy); runtime.PlayerPosition = new Vector2(x, y);
            for (int i = 0; i < (dash ? 1 : 100); i++)
                runtime.PlayerPosition = (Vector2)Call("MoveOnIsland", runtime,
                    direction * (dash ? 15f : .15f), catalog.Rule.PlayerRadius);
            Assert.That(Vector2.Dot(runtime.PlayerPosition, direction), Is.EqualTo(17.35f).Within(.02f),
                "山脚近侧必须按角色半径实体阻挡");
        }

        /// <summary>切图更新实体布局，原西侧山脚在第三轮成为海岸。</summary>
        [Test] public void MountainSolidsFollowCurrentRound()
        {
            Call("StartIslandRound", runtime, 0, null);
            var first = (IReadOnlyList<ArcadeObstacleDefinition>)Call("ObstaclesFor", runtime);
            Assert.That(ArcadeCombatSystem.IsInsideObstacle(new Vector2(-18.6f, 10), 0, first), Is.True);
            Call("StartIslandRound", runtime, 2, null);
            var third = (IReadOnlyList<ArcadeObstacleDefinition>)Call("ObstaclesFor", runtime);
            Assert.That(ArcadeCombatSystem.IsInsideObstacle(new Vector2(-18.6f, 10), 0, third), Is.False);
        }

        /// <summary>旧节点的玩家或强化锚点落在码头靠海侧时，岸线夹取不能把它重新压回实体。</summary>
        [TestCase(18f, 5f)]
        [TestCase(17.5f, 5f)]
        [TestCase(17.8f, 5f)]
        [TestCase(18f, 6f)]
        [TestCase(18f, 4f)]
        public void CoastBoundaryProjectionNeverReturnsInsidePier(float x, float y)
        {
            Call("StartIslandRound", runtime, 0, null);
            var obstacles=(IReadOnlyList<ArcadeObstacleDefinition>)Call("ObstaclesFor", runtime);
            var region=new ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight,1);
            var resolved=ArcadeCombatSystem.ResolvePosition(new Vector2(x,y),catalog.Rule.PlayerRadius,
                catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight,obstacles,region);
            Assert.That(ArcadeCombatSystem.IsInsideObstacle(resolved,catalog.Rule.PlayerRadius,obstacles),Is.False,
                "障碍推出与岸线投影应同时满足，不能返回码头内部");
            Assert.That(region.ContainsCircle(resolved.x,resolved.y,catalog.Rule.PlayerRadius),Is.True);
        }

        /// <summary>通过正式旧节点恢复入口验证强化锚点脱离新增码头，且剩余时长不被刷新。</summary>
        [Test] public void RestoredCoastPowerupAnchorCannotRemainInsidePier()
        {
            Call("StartIslandRound", runtime, 0, null);
            var saved=new ArcadeRunFileStore(directory).Inspect().Data.IslandRoundCheckpoint;
            var definition=catalog.Powerups[0];
            saved.Powerups.Add(new ArcadeIslandPowerupCheckpoint { ProductId=definition.Id,
                RemainingSeconds=7, Charges=1, PositionX=17.5f, PositionY=5 });
            Call("StartIslandRound", runtime, 0, saved);
            var restored=runtime.Powerups[definition.Behavior];
            var obstacles=(IReadOnlyList<ArcadeObstacleDefinition>)Call("ObstaclesFor", runtime);
            Assert.That(ArcadeCombatSystem.IsInsideObstacle(restored.Position,catalog.Rule.PlayerRadius,obstacles),Is.False);
            Assert.That(restored.RemainingSeconds,Is.EqualTo(7));
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

        [Test] public void RoundWaitsForBlackBeforeReplacingMapAndRevealsOnlyWhenReady()
        {
            Call("StartIslandRound",runtime,0,null);view.Round=0;view.Reveals=0;
            view.Covered=false; view.Ready=false;
            Call("StartIslandRound",runtime,1,null);
            Assert.That(view.Round,Is.Zero,"淡出完成前保留上一关画面");
            int synchronized=view.PlayerUpdates;Call("SyncView",runtime);
            Assert.That(view.PlayerUpdates,Is.EqualTo(synchronized),"出口帧的公共同步也不能提前移动玩家");
            Assert.That(runtime.IslandTransitionPending,Is.True);
            view.Covered=true;Call("TryFinishIslandTransition",runtime);
            Assert.That(view.Round,Is.EqualTo(2));Assert.That(view.Reveals,Is.Zero);
            view.Ready=true;Call("TryFinishIslandTransition",runtime);
            Assert.That(runtime.IslandTransitionPending,Is.False,runtime.IslandPreparationError);Assert.That(view.Reveals,Is.EqualTo(1));
        }

        [Test] public void ContentThemeWaitsForBlackAndItsOwnResourcesBeforeReveal()
        {
            Set("arena",catalog.Arenas[1]);var run=architecture.GetModel<ArcadeRunModel>();var data=run.Current;
            data.NextArenaNumber=2;data.CompletedArenaCount=1;data.ArenaConfigNumber=2;data.Checkpoint.ArenaNumber=2;
            run.CommitSafeNode(data);run.EnterArena();runtime.ArenaNumber=2;
            view.Covered=false;view.Ready=false;Call("StartContentRound",runtime,0,null);
            Assert.That(view.Begins,Is.Zero);Assert.That(runtime.IslandTransitionPending,Is.True);
            view.Covered=true;Call("TryFinishContentTransition",runtime);Assert.That(view.Begins,Is.EqualTo(1));Assert.That(view.Reveals,Is.Zero);
            view.Ready=true;Call("TryFinishContentTransition",runtime);Assert.That(runtime.IslandTransitionPending,Is.False,runtime.IslandPreparationError);
            Assert.That(view.Reveals,Is.EqualTo(1));
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

        [Test] public void RoundStartDoesNotRequestUnsolicitedSound() => AssertNoUnsolicitedSound(false);

        /// <summary>相同活动时段只使用一个真实通道，敌人从边界外连续入场，不按出生序轮换方向。</summary>
        [Test] public void OneActiveSegmentSpawnsOnlyOutsideItsVisiblePassageWithoutWarnings()
        {
            Call("StartIslandRound", runtime, 1, null);
            Vector2? direction = null;
            for (int i = 0; i < 8; i++)
            {
                runtime.Enemies.Clear(); runtime.SpawnCooldown = 0;
                Call("TickSpawning", runtime, .1f);
                Assert.That(runtime.Enemies.Count, Is.EqualTo(1), "换向不能等待额外预警");
                var position = runtime.Enemies.Values.Single().Position;
                Assert.That(Mathf.Max(Mathf.Abs(position.x), Mathf.Abs(position.y)), Is.GreaterThan(19.5f), "不能在战斗区凭空出现");
                if (direction.HasValue) Assert.That(Vector2.Dot(position.normalized, direction.Value), Is.GreaterThan(.995f));
                direction = position.normalized;
                Assert.That(runtime.IslandEntryWarningRemaining, Is.Zero, "不增加方向预警");
            }
        }

        /// <summary>每段从权威随机源独立抽取，允许重复；等待或过轮不靠击杀预算切换。</summary>
        [Test] public void PassageSegmentsUseRandomDirectionsRatherThanFixedSpawnBudgets()
        {
            Call("StartIslandRound", runtime, 0, null);
            var expected = new System.Random(37); Set("random", new System.Random(37));
            for (int segment = 0; segment < 10; segment++)
            {
                Call("IslandEntryIsReady", runtime, segment == 0 ? 0f : 12f);
                Assert.That(runtime.IslandEntryBatch, Is.EqualTo(expected.Next(3)), "每个时段应独立随机选通道");
                int selected = runtime.IslandEntryBatch;
                Call("IslandEntryIsReady", runtime, .01f);
                Assert.That(runtime.IslandEntryBatch, Is.EqualTo(selected), "同段不应换向");
            }
        }
        [Test] public void EntryBatchDoesNotRequestUnsolicitedSound() => AssertNoUnsolicitedSound(true);

        private void AssertNoUnsolicitedSound(bool entryBatch)
        {
            if (entryBatch)
            {
                Call("StartIslandRound", runtime, 0, null);
                Call("StartIslandRound", runtime, 1, null);
            }
            using (var capture = new SoundCapture(architecture))
            {
                if (entryBatch)
                {
                    Call("TickSpawning", runtime, .01f);
                    runtime.SpawnCooldown = 0;
                    Call("TickSpawning", runtime, 12f);
                    Assert.That(runtime.IslandEntryWarningRemaining, Is.Zero);
                }
                else
                {
                    Call("StartIslandRound", runtime, 0, null);
                    Assert.That(runtime.IslandTransitionPending, Is.False);
                }
                Assert.That(capture.Requests, Is.Empty, "未射击时，开场及入场换向不能播放突兀的全局确认音");
                Assert.That(runtime.PlayerFireSequence, Is.Zero);
            }
        }

        [Test] public void PassageChangeKeepsExistingEnemiesMovingWithoutFiring()
        {
            Call("StartIslandRound", runtime, 0, null);
            Call("TickSpawning", runtime, .01f);
            var enemy = runtime.Enemies.Values.First(); enemy.Position = runtime.PlayerPosition + Vector2.up * 4;
            var before = enemy.Position; runtime.SpawnCooldown = 0;
            Call("IslandEntryIsReady", runtime, 12f);
            combat.Tick(runtime, .01f);
            Assert.That(runtime.IslandEntryWarningRemaining, Is.Zero);
            Assert.That(runtime.Enemies.ContainsKey(enemy.Id), Is.True);
            Assert.That(enemy.Position, Is.Not.EqualTo(before));
            Assert.That(runtime.PlayerFireSequence, Is.Zero);
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

        [Test] public void RoundStartPublishesPreparationWithoutWaitingForAnotherCombatTick()
        {
            ArcadeCombatHudSnapshot snapshot = null;
            architecture.RegisterEvent<ArcadeCombatHudChangedEvent>(evt => snapshot = evt.Snapshot);
            view.Ready = false;
            Call("StartIslandRound", runtime, 0, null);
            Assert.That(snapshot, Is.Not.Null, "出口确认发起异步切图后可能直接进入等待分支，必须当帧通知加载页");
            Assert.That(snapshot.IslandTransitioning, Is.True);
            Assert.That(snapshot.CanRetryCompletion, Is.False);
        }

        [Test] public void ExitContactKeepsEffectsChargesCooldownHealthAndClearsWorld()
        {
            Call("StartIslandRound", runtime, 0, null);
            var shield = System.Linq.Enumerable.First(catalog.Powerups, p => p.Behavior == ArcadePowerupBehavior.Shield);
            runtime.Powerups[shield.Behavior] = new ArcadePowerupRuntime { ProductId = shield.Id, Definition = shield,
                RemainingSeconds = 11, Charges = 1, CooldownSeconds = .3f, Position = runtime.PlayerPosition };
            runtime.TemporaryWeaponId = "arcade.weapon.rapid"; runtime.TemporaryWeaponRemaining = 6.5f;
            runtime.Projectiles.Add(41, new ArcadeProjectileRuntime { Id = 41 });
            runtime.Drops.Add(42, new ArcadeDropRuntime { Id = 42 });
            runtime.SpawnQueue.Clear(); Call("TickWaveTransition", runtime, 0f);
            runtime.PlayerPosition = ArcadeIslandLayout.GetExitPosition(1, 0, 1,
                catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, catalog.Rule.PlayerRadius);
            Call("TickIslandExit", runtime);
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

        [Test] public void LoadingRetryClearsResourceFailureAndRejectsDuplicateOrCompletionRetry()
        {
            ArcadeCombatHudSnapshot snapshot = null;
            architecture.RegisterEvent<ArcadeCombatHudChangedEvent>(evt => snapshot = evt.Snapshot);
            view.Ready = false;
            Call("StartIslandRound", runtime, 0, null);
            view.LoadError = "shader unavailable";
            combat.Tick(runtime, 10);
            Assert.That(snapshot.IslandPreparationError, Is.EqualTo("shader unavailable"));
            Assert.That(snapshot.IslandTransitioning, Is.True);
            Assert.That(snapshot.CanRetryCompletion, Is.True);
            view.LoadError = null;
            Assert.That(combat.TryRetryMapPreparation(), Is.True);
            Assert.That(snapshot.IslandPreparationError, Is.Null);
            Assert.That(snapshot.CanRetryCompletion, Is.False);
            Assert.That(snapshot.IslandTransitioning, Is.True);
            Assert.That(combat.TryRetryMapPreparation(), Is.False);
            view.Ready = true;
            combat.Tick(runtime, 1);
            Assert.That(snapshot.IslandTransitioning, Is.False);
            Assert.That(new ArcadeRunFileStore(directory).Inspect().Data.IslandRoundCheckpoint.RoundNumber, Is.EqualTo(1));
            runtime.CompletionAwaitingRetry = true;
            Assert.That(combat.TryRetryMapPreparation(), Is.False, "加载页不能提交普通结算重试");
        }

        [Test] public void LoadingRetryRejectsMapAlreadyReleased()
        {
            architecture.RegisterModel(new MapSceneModel());
            view.Ready = false;
            Call("StartIslandRound", runtime, 0, null);
            runtime.CompletionAwaitingRetry = true;
            Assert.That(combat.TryRetryMapPreparation(), Is.False, "卸载后的重试必须恢复安全节点，不能装配已释放地图");
        }

        [Test] public void PassageChangesPreserveExistingEnemiesAndBudgetWithoutWarningWait()
        {
            Call("StartIslandRound", runtime, 1, null);
            Call("TickSpawning", runtime, .01f); Assert.That(runtime.Enemies.Count, Is.EqualTo(1));
            var first = runtime.Enemies.Values.First();
            runtime.SpawnCooldown = 0;
            Call("TickSpawning", runtime, 12f);
            Assert.That(runtime.Enemies.Count, Is.EqualTo(2));
            Assert.That(runtime.Enemies.ContainsKey(first.Id), Is.True);
            Assert.That(runtime.SpawnQueue.Count + runtime.Enemies.Count, Is.EqualTo(120));
            Assert.That(runtime.IslandEntryWarningRemaining, Is.Zero);
        }

        [Test] public void RespawnAndClearWaitDoNotConsumePassageTime()
        {
            Call("StartIslandRound", runtime, 0, null);
            Set("random", new System.Random(37));
            Call("IslandEntryIsReady", runtime, 0f);
            runtime.RespawnRemaining = 100; combat.Tick(runtime, 30);
            runtime.RespawnRemaining = 0;
            runtime.SpawnQueue.Clear(); Call("TickWaveTransition", runtime, 0f); combat.Tick(runtime, 30);
            var expected = new System.Random(37); expected.Next(3);
            Call("IslandEntryIsReady", runtime, 12f);
            Assert.That(runtime.IslandEntryBatch, Is.EqualTo(expected.Next(3)));
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
            runtime.PlayerPosition = new Vector2(-12, -12); // 主动接触入口后才进入奖励区域。
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

        [Test] public void FastProjectileCannotCrossMountainFootBetweenFrames()
        {
            Call("StartIslandRound", runtime, 0, null); Call("StartIslandRound", runtime, 1, null);
            Call("StartIslandRound", runtime, 2, null);
            runtime.Projectiles.Add(50, new ArcadeProjectileRuntime { Id = 50, Position = new Vector2(16, 10),
                Velocity = new Vector2(30, 0), RemainingSeconds = 10 });
            Call("TickProjectiles", runtime, .5f);
            Assert.That(runtime.Projectiles.ContainsKey(50), Is.False, "弹丸不能穿过山脚实体");
        }

        [Test]
        public void OrdinaryGunWallContactEmitsOnceOnlyForActualLivePlayerHit()
        {
            foreach (var (source, hostile, life, expected) in new[] {
                ("arcade.weapon.basic", false, 10f, true), ("arcade.weapon.rapid", false, 10f, true),
                ("arcade.weapon.spread", false, 10f, true), ("arcade.weapon.basic", true, 10f, false),
                ("arcade.weapon.basic", false, .1f, false), ("arcade.weapon.raygun", false, 10f, false) })
            {
            view.SurfaceHits = 0;
            Call("StartIslandRound", runtime, 0, null); Call("StartIslandRound", runtime, 1, null);
            Call("StartIslandRound", runtime, 2, null);
            runtime.Projectiles.Add(50, new ArcadeProjectileRuntime { Id = 50, Position = new Vector2(16, 10),
                Velocity = new Vector2(30, 0), RemainingSeconds = life, SourceWeaponId = source, Hostile = hostile });
            var recorder = view;
            Call("TickProjectiles", runtime, .5f); Call("TickProjectiles", runtime, .5f);
            Assert.That(recorder.SurfaceHits, Is.EqualTo(expected ? 1 : 0), source + " hostile=" + hostile + " life=" + life);
            if (expected)
            {
                Assert.That(recorder.SurfacePosition.x, Is.InRange(16f, 19.5f));
                Assert.That(recorder.SurfacePosition.y, Is.EqualTo(10f));
            }
            }
        }

        [Test] public void DashCannotPassThroughMountainFootAndPathRemainsOutsideObstacle()
        {
            Call("StartIslandRound", runtime, 0, null); Call("StartIslandRound", runtime, 1, null);
            Call("StartIslandRound", runtime, 2, null);
            runtime.PlayerPosition = new Vector2(16, 10);
            var end = (Vector2)Call("MoveOnIsland", runtime, new Vector2(16, 0), .55f);
            Assert.That(end.x, Is.EqualTo(17.35f).Within(.02f));
        }

        [Test] public void RepeatedExitContactCannotSkipNewRound()
        {
            Call("StartIslandRound", runtime, 0, null); runtime.SpawnQueue.Clear(); Call("TickWaveTransition", runtime, 0f);
            runtime.PlayerPosition = ArcadeIslandLayout.GetExitPosition(1, 0, 1,
                catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, catalog.Rule.PlayerRadius);
            Call("TickIslandExit", runtime);
            Call("TickIslandExit", runtime);
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
            runtime.PlayerPosition = new Vector2(-12, -12);
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

        /// <summary>保留真实声音系统，仅截获资源加载边界；不触碰正式资源或玩家存档。</summary>
        private sealed class SoundCapture : IResourceLoader, IDisposable
        {
            public readonly List<string> Requests = new();
            private readonly IResourceLoader previous = ResourceLoader.Current;
            private readonly GameObject root;
            private readonly AudioSystem audio;

            public SoundCapture(Game architecture)
            {
                root = new GameObject("IslandRoundAudioTest");
                try
                {
                    var bindings = root.AddComponent<AudioRootBindings>();
                    bindings.Mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Game/Audio/FirepowerLoopAudioMixer.mixer");
                    bindings.MusicGroup = bindings.Mixer.FindMatchingGroups("Music")[0];
                    bindings.SoundGroup = bindings.Mixer.FindMatchingGroups("Sound")[0];
                    bindings.MusicSourceA = Child("MusicA").AddComponent<AudioSource>();
                    bindings.MusicSourceB = Child("MusicB").AddComponent<AudioSource>();
                    bindings.SoundPoolRoot = Child("Pool").transform;
                    bindings.PlayingSoundRoot = Child("Playing").transform;
                    ResourceLoader.Use(this);
                    architecture.RegisterModel(new AudioSettingsModel());
                    audio = new AudioSystem(); architecture.RegisterSystem(audio);
                    ((ICanInit)audio).Init();
                    Assert.That(audio.PooledSourceCount, Is.EqualTo(8), "必须启用真实声音系统才能捕获遗漏调用");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            private GameObject Child(string name)
            {
                var child = new GameObject(name); child.transform.SetParent(root.transform, false); return child;
            }

            public T LoadAsset<T>(string path) where T : UnityEngine.Object => null;
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
            {
                Requests.Add(path); onLoaded?.Invoke(null); return new ResourceLoadHandle(path);
            }
            public void Release(ResourceLoadHandle handle) { }
            public void Dispose()
            {
                try { audio?.Deinit(); }
                finally
                {
                    ResourceLoader.Use(previous);
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private sealed class View : IArcadeCombatViewPort
        {
            public bool Covered=true;
            public int Reveals;
            public int PlayerUpdates;
            public int Begins;
            public bool IsRoundTransitionCovered=>Covered;
            public void BeginRoundTransition() { }
            public void EndRoundTransition()=>Reveals++;
            public bool Ready = true;
            public string LoadError;
            public string IslandRoundLoadError => LoadError;
            public int SurfaceHits;
            public Vector2 SurfacePosition;
            public void PlayProjectileSurfaceImpact(Vector2 position, Vector2 direction) { SurfaceHits++; SurfacePosition = position; }
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
            public void Begin(int n, float w, float h, IReadOnlyList<ArcadeObstacleDefinition> o) { Begins++; }
            public void End() { }
            public bool TryScreenToArena(Vector2 s, out Vector2 p) { p = default; return false; }
            public void SetPlayer(Vector2 p, Vector2 f, bool v, bool i) { PlayerUpdates++; }
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
