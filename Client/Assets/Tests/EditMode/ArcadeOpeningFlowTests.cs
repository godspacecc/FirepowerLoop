using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>首场真实单人规则的定向回归；使用内存领域现场，不修改正式存档或地图。</summary>
    public sealed class ArcadeOpeningFlowTests
    {
        private Game architecture;
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeRunModel run;
        private ArcadeConfigCatalogDefinition catalog;
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            architecture = new Game();
            run = new ArcadeRunModel(); architecture.RegisterModel(run);
            architecture.RegisterModel(new ArcadeCombatModel());
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",name+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            var data = new ArcadeRunSaveData { RunId="opening-test", RemainingLives=3, IsCasualRoute=true,
                ArenaConfigNumber=1, NextArenaNumber=1, Checkpoint=new ArcadeCheckpointSaveData{ArenaNumber=1} };
            run.CommitSafeNode(data); run.EnterArena();
            system = new ArcadeCombatSystem(); ((ICanSetArchitecture)system).SetArchitecture(architecture);
            runtime = new ArcadeCombatRuntime{ArenaNumber=1,PlayerMaxHealth=100,PlayerHealth=100};
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            typeof(ArcadeCombatSystem).GetField("catalog",Flags).SetValue(system,catalog);
            typeof(ArcadeCombatSystem).GetField("arena",Flags).SetValue(system,catalog.Arenas[0]);
            typeof(ArcadeCombatSystem).GetField("random",Flags).SetValue(system,new System.Random(37));
        }
        private object Call(string method,params object[] args) => typeof(ArcadeCombatSystem).GetMethod(method,Flags).Invoke(system,args);

        [Test] public void IslandEnemyChasesCurrentPlayerFromFirstStepAndStaysInsideShore()
        {
            for (int wave = 0; wave < 4; wave++) CheckIslandEnemyChase(wave, true);
            CheckIslandEnemyChase(0, false);
        }

        private void CheckIslandEnemyChase(int wave, bool casual)
        {
            runtime.Enemies.Clear();
            var data = run.Current;
            data.IsCasualRoute = casual;
            run.CommitSafeNode(data); run.EnterArena();
            Call("BeginWave", runtime, wave);
            runtime.IslandWaveBudget = runtime.SpawnQueue.Count;
            runtime.IslandEntryWarningRemaining = 0;
            Call("TickSpawning", runtime, 5f);
            var enemy = runtime.Enemies.Values.Single();
            var region = new ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, casual ? wave + 1 : 0);
            var tangent = new Vector2(-enemy.Position.y, enemy.Position.x).normalized;
            runtime.PlayerInvulnerability = 1000;
            if (wave == 0) Assert.That(region.IsShallow(enemy.Position.x, enemy.Position.y), Is.True);
            foreach (var side in new[] { 1f, -1f })
            {
                var target = region.Constrain(enemy.Position.x + tangent.x * side * 4,
                    enemy.Position.y + tangent.y * side * 4, catalog.Rule.PlayerRadius);
                runtime.PlayerPosition = new Vector2(target.X, target.Y);
                var before = enemy.Position;
                var direction = (runtime.PlayerPosition - before).normalized;
                Call("TickEnemies", runtime, .05f);
                var motion = enemy.Position - before;
                Assert.That(Vector2.Dot(motion.normalized, direction), Is.GreaterThan(.9f),
                    "出生首帧及玩家换向后均须立即追踪当前玩家，不能先走固定登陆点");
                Assert.That(motion.magnitude, Is.LessThanOrEqualTo(enemy.Speed * .05f + .001f));
            }
            // 沿岸追击多帧：保持圆形敌人半径留量，不进入外缘深水或跳到内陆。
            var edge = region.Constrain(enemy.Position.x + tangent.x * 30, enemy.Position.y + tangent.y * 30, catalog.Rule.PlayerRadius);
            runtime.PlayerPosition = new Vector2(edge.X, edge.Y);
            for (int i = 0; i < 100; i++)
            {
                var before = enemy.Position;
                Call("TickEnemies", runtime, .05f);
                Assert.That(region.Contains(enemy.Position.x, enemy.Position.y), Is.True);
                var legal = region.Constrain(enemy.Position.x, enemy.Position.y, catalog.Rule.EnemyContactRadius);
                Assert.That(Vector2.Distance(enemy.Position, new Vector2(legal.X, legal.Y)), Is.LessThan(.01f));
                Assert.That(Vector2.Distance(before, enemy.Position), Is.LessThanOrEqualTo(enemy.Speed * .05f + .01f));
            }
        }

        [Test]
        public void IslandEntry_FourMapsUseFixedBatchesAndMoveContinuouslyWithoutBorderSnap()
        {
            typeof(ArcadeCombatSystem).GetField("random",Flags).SetValue(system,new System.Random(37));
            runtime.PlayerInvulnerability=1000f;
            for(int wave=0;wave<4;wave++)
            {
                runtime.Enemies.Clear();Call("BeginWave",runtime,wave);
                int total=runtime.SpawnQueue.Count;
                Assert.That(total,Is.EqualTo(110+10*wave));
                runtime.IslandWaveBudget=total;runtime.IslandEntryWarningRemaining=0;
                Call("TickSpawning",runtime,5f);runtime.SpawnCooldown=0;Call("TickSpawning",runtime,5f);
                var region=new ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight,wave+1);
                var water=runtime.Enemies.Values.First();
                var shore=runtime.Enemies.Values.Last();
                Assert.That(region.Contains(water.Position.x,water.Position.y),Is.True);
                Assert.That(region.Contains(shore.Position.x,shore.Position.y),Is.True);
                Assert.That(runtime.Enemies.Count+runtime.SpawnQueue.Count,Is.EqualTo(total));
                Vector2 before=water.Position;Call("TickEnemies",runtime,.05f);
                Assert.That(Vector2.Distance(before,water.Position),Is.LessThanOrEqualTo(water.Speed*.05f+.001f));
                runtime.PlayerPosition=water.Position+Vector2.up*.25f;
                int health=runtime.PlayerHealth;runtime.PlayerInvulnerability=0;water.AttackCooldown=0;
                Call("TickEnemies",runtime,.05f);
                Assert.That(runtime.PlayerHealth,Is.LessThan(health),"水侧敌人立即按原接触规则攻击附近玩家");
                runtime.PlayerInvulnerability=1000f;runtime.PlayerPosition=Vector2.zero;
                for(int i=0;i<500;i++)Call("TickEnemies",runtime,.05f);
                Assert.That(region.IsShallow(water.Position.x,water.Position.y),Is.False);
                Assert.That(Mathf.Abs(shore.Position.y),Is.LessThan(catalog.Rule.ArenaHalfHeight));
                int spawned=runtime.Enemies.Count;
                while(runtime.SpawnQueue.Count>0)
                {
                    runtime.Enemies.Clear();runtime.SpawnCooldown=0;Call("TickSpawning",runtime,5f);
                    Assert.That(runtime.Enemies.Count,Is.EqualTo(1));spawned++;
                    // 弹丸等共享实体序号不能改变固定批次方向或既定总预算。
                    runtime.NextEntityId++;
                    var e=runtime.Enemies.Values.Single();
                    Assert.That(region.Contains(e.Position.x,e.Position.y),Is.True);
                }
                Assert.That(spawned,Is.EqualTo(total));
                runtime.Enemies.Clear();
                if(wave==2)
                {
                    runtime.PlayerPosition=new Vector2(-12,-12); // 清场后主动走到已验证可达的入口。
                    Call("TickTreasureRoom",runtime,0f);Assert.That(runtime.InTreasureRoom,Is.True);
                    Call("ReturnFromTreasure",runtime);Assert.That(runtime.WaveIndex,Is.EqualTo(3));
                }
                else {Call("TickWaveTransition",runtime,0f);Assert.That(runtime.WaveClearAwarded,Is.True);}
            }
        }
        [Test] public void NewOrdinaryWave_ReplenishesOnlyEmptyInventoryAndQueuesFullWave()
        {
            runtime.Bombs=0;runtime.Dashes=4;Call("BeginWave",runtime,1);
            Assert.That(runtime.Bombs,Is.EqualTo(1));Assert.That(runtime.Dashes,Is.EqualTo(4));
            Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(120));
        }
        [TestCase("weapon.short-sword")]
        [TestCase("weapon.greatsword")]
        [TestCase("weapon.knife")]
        [TestCase("weapon.axe")]
        [TestCase("weapon.pickaxe")]
        public void LegacyMeleeEquipment_UsesBaselineGun(string id)
        {
            var data = run.Current;
            data.EquipmentBySlot[ArcadeRunDefaults.WeaponSlotId] = id;
            run.CommitSafeNode(data);
            var args = new object[] { runtime, default(ArcadeWeaponDefinition) };
            Assert.That((bool)Call("TryGetWeapon", args), Is.True);
            Assert.That(((ArcadeWeaponDefinition)args[1]).Id, Is.EqualTo(catalog.Rule.BaseWeaponId));
            runtime.TemporaryWeaponId = id; runtime.TemporaryWeaponRemaining = 10f;
            Assert.That((bool)Call("TryGetWeapon", args), Is.True);
            Assert.That(((ArcadeWeaponDefinition)args[1]).Id, Is.EqualTo(catalog.Rule.BaseWeaponId));
        }

        [Test] public void ShoppingApiCannotBeUsedByLegacyNodes()
        {
            Assert.That(typeof(ArcadeRunSystem).GetMethod("TryPurchase"), Is.Null);
            Assert.That(typeof(ArcadeCheckpointSaveData).GetProperty("Offers"), Is.Null);
        }
        [Test] public void PendingQueue_PreventsFalseClearEvenWhenNoEnemyIsAlive()
        {
            Call("BeginWave",runtime,0);Call("TickWaveTransition",runtime,200f);
            Assert.That(runtime.WaveClearAwarded,Is.False);Assert.That(runtime.WaveIndex,Is.Zero);
        }
        [Test] public void ClearedIslandRound_RequiresExitConfirmationRegardlessOfFireOrTimeout()
        {
            Call("BeginWave",runtime,0);runtime.SpawnQueue.Clear();runtime.Firing=true;
            Call("TickWaveTransition",runtime,0f);Call("TickWaveTransition",runtime,2f);
            Assert.That(runtime.WaveIndex,Is.Zero);
            runtime.Firing=false;Call("TickWaveTransition",runtime,.1f);
            runtime.Firing=true;Call("TickWaveTransition",runtime,.1f);
            Assert.That(runtime.WaveIndex,Is.Zero);Assert.That(runtime.SpawnQueue,Is.Empty);
            Call("TickWaveTransition",runtime,600f);Assert.That(runtime.WaveIndex,Is.Zero);
            Assert.That(runtime.IslandRoundAwaitingExit,Is.True);
        }
        [Test] public void FinalClear_ShowsTwoExitsWithoutStartingAnotherWave()
        {
            Call("BeginWave",runtime,3);runtime.SpawnQueue.Clear();Call("TickWaveTransition",runtime,0f);
            Assert.That(runtime.AwaitingExit,Is.True);Assert.That(runtime.WaveTransitionRemaining,Is.Zero);
            Assert.That(catalog.Arenas[0].NextArenaNumbers,Is.EquivalentTo(new[]{2,3}));
        }
        [Test] public void StageNoticesFollowAuthorityWithoutChangingRestOrExit()
        {
            var notices = architecture.GetModel<ArcadeCombatModel>().StageNotices;
            Call("BeginWave", runtime, 0);
            Call("PublishHud", runtime);
            Assert.That(notices.TryConsume(Time.unscaledTimeAsDouble, out var notice), Is.True);
            Assert.That(notice.Kind, Is.EqualTo(ArcadeStageNoticeKind.WaveStarted));
            runtime.SpawnQueue.Clear();
            Call("TickWaveTransition", runtime, 0f);
            float rest = runtime.WaveTransitionRemaining;
            Call("PublishHud", runtime);
            Assert.That(notices.TryConsume(Time.unscaledTimeAsDouble, out notice), Is.True);
            Assert.That(notice.Kind, Is.EqualTo(ArcadeStageNoticeKind.WaveCompleted));
            Assert.That(runtime.WaveTransitionRemaining, Is.EqualTo(rest));
            Assert.That(rest, Is.Zero);
            Assert.That(runtime.IslandRoundAwaitingExit, Is.True);
            Assert.That(runtime.AwaitingExit, Is.False);
            Call("BeginWave", runtime, 3);
            runtime.SpawnQueue.Clear();
            Call("TickWaveTransition", runtime, 0f);
            Call("PublishHud", runtime);
            Assert.That(notices.TryConsume(Time.unscaledTimeAsDouble, out notice), Is.True);
            Assert.That(notice.Kind, Is.EqualTo(ArcadeStageNoticeKind.ArenaCompleted));
            Assert.That(runtime.AwaitingExit, Is.True);
            Assert.That(runtime.WaveTransitionRemaining, Is.Zero);
            Call("PublishHud", runtime);
            Assert.That(notices.TryConsume(Time.unscaledTimeAsDouble, out _), Is.False);
        }
        [Test] public void WaterMovement_UsesSameConfiguredZoneAsPresentation()
        {
            var region=new ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight,1);
            int south=ArcadeIslandRegion.Samples*3/4;
            runtime.PlayerPosition=new Vector2(0,(region.Outer[south].Y+region.Sand[south].Y)*.5f);
            runtime.MoveDirection=Vector2.right;
            Call("TickPlayer",runtime,.1f);
            Assert.That(runtime.PlayerPosition.x,Is.EqualTo(catalog.Rule.PlayerMoveSpeed*.55f*.1f).Within(.001f));
            runtime.PlayerPosition=Vector2.zero;Call("TickPlayer",runtime,.1f);
            Assert.That(runtime.PlayerPosition.x,Is.EqualTo(catalog.Rule.PlayerMoveSpeed*.1f).Within(.001f));
        }
        [Test] public void IslandKillCountsOnceUsesMultiplierAndDoesNotAdvanceTreasureProgress()
        {
            run.SetTransientTreasureScore(new ArcadeTreasureScoreState(199950,2,4,3));
            for(int i=0;i<catalog.Rule.MaxDrops;i++)runtime.Drops.Add(i,new ArcadeDropRuntime());
            var enemy=new ArcadeEnemyRuntime{Id=1,Position=Vector2.zero};runtime.Enemies.Add(1,enemy);
            Call("KillEnemy",runtime,enemy,false);
            Assert.That(run.Current.Score,Is.EqualTo(200150));Assert.That(run.Current.RemainingLives,Is.EqualTo(4));
            Assert.That(run.Current.MultiplierProgress,Is.EqualTo(4));Assert.That(run.Current.ScoreMultiplier,Is.EqualTo(2));
            Call("KillEnemy",runtime,enemy,false);Assert.That(run.Current.Score,Is.EqualTo(200150));
            var hazard=new ArcadeEnemyRuntime{Id=2};runtime.Enemies.Add(2,hazard);
            Call("KillEnemy",runtime,hazard,true);Assert.That(run.Current.Score,Is.EqualTo(200150));
        }
        [Test] public void BombProtectsForThreeSecondsWithoutShorteningLongProtectionOrRewardingInvalidUse()
        {
            runtime.Bombs=1;runtime.PlayerInvulnerability=5;Call("UseBomb",runtime);
            Assert.That(runtime.PlayerInvulnerability,Is.EqualTo(5));Assert.That(runtime.Bombs,Is.Zero);
            runtime.Bombs=1;runtime.PlayerInvulnerability=0;Call("UseBomb",runtime);
            Assert.That(runtime.PlayerInvulnerability,Is.EqualTo(3));Call("DamagePlayer",runtime,20);
            Assert.That(runtime.PlayerHealth,Is.EqualTo(100));
            runtime.PlayerInvulnerability=0;runtime.InTreasureRoom=true;runtime.Bombs=1;Call("UseBomb",runtime);
            Assert.That(runtime.PlayerInvulnerability,Is.Zero);Assert.That(runtime.Bombs,Is.EqualTo(1));
            runtime.InTreasureRoom=false;runtime.Bombs=0;Call("UseBomb",runtime);Assert.That(runtime.PlayerInvulnerability,Is.Zero);
        }
        [Test] public void DashKillsSweptOrdinaryTargetButNotOffPathBossOrDeepSea()
        {
            runtime.Dashes=1;runtime.MoveDirection=Vector2.right;Call("UseDash",runtime);
            runtime.Enemies.Add(1,new ArcadeEnemyRuntime{Id=1,Position=new Vector2(2,0)});
            runtime.Enemies.Add(2,new ArcadeEnemyRuntime{Id=2,Position=new Vector2(2,4)});
            runtime.Enemies.Add(3,new ArcadeEnemyRuntime{Id=3,Position=new Vector2(2,0),BossId="boss"});
            Call("TickPlayer",runtime,.35f);
            Assert.That(runtime.Enemies.ContainsKey(1),Is.False);Assert.That(runtime.Enemies.ContainsKey(2),Is.True);
            Assert.That(runtime.Enemies.ContainsKey(3),Is.True);Assert.That(run.Current.Score,Is.EqualTo(100));
            var region=new ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight);
            var edge=region.Constrain(30,0,catalog.Rule.PlayerRadius);runtime.PlayerPosition=new Vector2(edge.X,edge.Y);
            var outer=region.Constrain(30,0,0);
            runtime.Enemies.Add(4,new ArcadeEnemyRuntime{Id=4,Position=new Vector2(outer.X+.1f,outer.Y)});
            Call("TickPlayer",runtime,.35f);Assert.That(runtime.Enemies.ContainsKey(4),Is.True);
            Assert.That(region.Contains(runtime.PlayerPosition.x,runtime.PlayerPosition.y),Is.True);
        }
        [Test] public void ThirdClearWaitsForEntranceAndCannotRaceRestTimer()
        {
            Call("BeginWave",runtime,2);runtime.SpawnQueue.Clear();runtime.PlayerPosition=Vector2.zero;
            Call("PublishHud",runtime);
            var notices = architecture.GetModel<ArcadeCombatModel>().StageNotices;
            notices.TryConsume(Time.unscaledTimeAsDouble, out _);
            Call("TickTreasureRoom",runtime,0f);
            Assert.That(runtime.InTreasureRoom,Is.False);Assert.That(runtime.TreasureDiscovered,Is.False);
            Call("TickWaveTransition",runtime,500f);Assert.That(runtime.WaveIndex,Is.EqualTo(2));
            runtime.PlayerPosition=new Vector2(-12,-12);Call("TickTreasureRoom",runtime,0f);
            Assert.That(runtime.InTreasureRoom,Is.True);Assert.That(runtime.TreasureDiscovered,Is.True);
            Call("PublishHud", runtime);
            Assert.That(notices.TryConsume(Time.unscaledTimeAsDouble, out var thirdClear), Is.True);
            Assert.That(thirdClear.Kind, Is.EqualTo(ArcadeStageNoticeKind.WaveCompleted));
            Call("TickWaveTransition",runtime,500f);Assert.That(runtime.WaveIndex,Is.EqualTo(2));
            Call("TickTreasureRoom",runtime,60f);
            Assert.That(runtime.InTreasureRoom,Is.False);Assert.That(runtime.WaveIndex,Is.EqualTo(3));
            Call("PublishHud", runtime);
            Assert.That(notices.TryConsume(Time.unscaledTimeAsDouble, out var fourthWave), Is.True);
            Assert.That(fourthWave.Kind, Is.EqualTo(ArcadeStageNoticeKind.WaveStarted));
            Assert.That(fourthWave.WaveNumber, Is.EqualTo(4));
            Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(140));
            Call("TickTreasureRoom",runtime,0f);Assert.That(runtime.InTreasureRoom,Is.False);
        }
        [Test] public void Campfire_OnlyBurnsFourthRoundAndRespectsInvulnerability()
        {
            var p=catalog.Arenas[0].Opening;runtime.PlayerPosition=new Vector2(p.CampfireX,p.CampfireY);
            runtime.WaveIndex=2;Call("TickOpeningFire",runtime,1f);Assert.That(runtime.PlayerHealth,Is.EqualTo(100));
            runtime.WaveIndex=3;Call("TickOpeningFire",runtime,1f);Assert.That(runtime.PlayerHealth,Is.EqualTo(88));
            Call("TickOpeningFire",runtime,1f);Assert.That(runtime.PlayerHealth,Is.EqualTo(88));
            runtime.PlayerInvulnerability=0;runtime.InTreasureRoom=true;Call("TickOpeningFire",runtime,1f);
            Assert.That(runtime.PlayerHealth,Is.EqualTo(88));
        }
        [Test] public void OrdinaryTreasure_AwardsPersonalMultiplierScoreWithoutCash()
        {
            var drop=catalog.Drops.Single(d=>d.Id=="arcade.drop.gold.small");
            for(int i=0;i<11;i++)Call("ApplyDrop",runtime,drop);
            Assert.That(run.Current.Score,Is.EqualTo(1200));Assert.That(run.Current.ScoreMultiplier,Is.EqualTo(2));
            Assert.That(runtime.ArenaGold,Is.Zero);Assert.That(run.Current.RunGold,Is.Zero);
        }
        [Test] public void DualPlayerTotals_AddFiftyInsteadOfMultiplyingSolo()
        {
            for(int i=1;i<=4;i++)Assert.That(ArcadeOpeningRules.OrdinaryRoundTotal(i,2),Is.EqualTo(150+10*i));
            Assert.That(ArcadeOpeningRules.OrdinaryRoundTotal(40,1),Is.EqualTo(400));
        }
    }
}
