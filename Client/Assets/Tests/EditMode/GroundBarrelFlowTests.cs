using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>地面桶真实运行态命中与轮前恢复，全部文件位于隔离目录。</summary>
    public sealed class GroundBarrelFlowTests
    {
        private Game architecture;
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;
        private string directory;
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        [SetUp] public void SetUp()
        {
            directory=Path.Combine(Path.GetTempPath(),"FirepowerGroundBarrel-"+Guid.NewGuid().ToString("N"));
            architecture=new Game();architecture.RegisterModel(new ArcadeRunModel());architecture.RegisterModel(new ArcadeCombatModel());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(name=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",name+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            var run=new ArcadeRunSaveData{RunId=Guid.NewGuid().ToString("N"),IsCasualRoute=true,ArenaConfigNumber=3,CompletedArenaCount=1,NextArenaNumber=2,UpdatedAtUtc=DateTime.UtcNow.ToString("O")};
            run.Checkpoint.ArenaNumber=2;run.EquipmentBySlot["weapon"]=ArcadeRunDefaults.BasicWeaponId;
            Assert.That(new ArcadeRunFileStore(directory).Save(run,out error),Is.True,error);architecture.GetModel<ArcadeRunModel>().CommitSafeNode(run);architecture.GetModel<ArcadeRunModel>().EnterArena();
            runtime=new ArcadeCombatRuntime{ArenaNumber=2,PlayerHealth=100,PlayerMaxHealth=100,PlayerPosition=new Vector2(0,-15)};architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);Set("catalog",catalog);Set("arena",catalog.Arenas.First(a=>a.ArenaNumber==3));Set("random",new System.Random(17));
            Set("contentObstacleIdentity",Layout.Id);Set("contentRoundObstacles",Array.Empty<ArcadeObstacleDefinition>());
        }
        [TearDown] public void TearDown(){architecture.GetModel<ArcadeCombatModel>().Clear();if(Directory.Exists(directory))Directory.Delete(directory,true);}
        private ArcadeRoundLayoutParameters Layout=>catalog.RoundLayouts.First(p=>p.ArenaConfigNumber==3 && p.RoundNumber==runtime.WaveIndex+1);
        private void Set(string name,object value)=>typeof(ArcadeCombatSystem).GetField(name,Flags).SetValue(system,value);
        private object Call(string name,params object[] values){var method=typeof(ArcadeCombatSystem).GetMethod(name,Flags);Assert.That(method,Is.Not.Null,"地面桶规则尚未接入："+name);return method.Invoke(system,values);}
        private int Count=>((ICollection)runtime.GetType().GetField("GroundBarrels").GetValue(runtime)).Count;
        private Vector2 Point(int index)=>new Vector2(Layout.Barrels[index].X,Layout.Barrels[index].Y);
        private static ArcadeProjectileRuntime Shot()=>new ArcadeProjectileRuntime{Hostile=false,FromPlayerWeapon=true,Damage=100};
        [Test] public void SubmittedResultStopsBarrelAndCarrierDamage()
        {
            Call("InitializeGroundBarrels",runtime);var point=Point(0);
            var enemy=new ArcadeEnemyRuntime{Id=7,Position=point+Vector2.up*2.5f,Health=100,MaxHealth=100};runtime.Enemies.Add(7,enemy);
            runtime.CompletionSubmitted=true;
            var projectile=Shot();projectile.Damage=200;projectile.Behavior=global::ArcadeWeaponBehavior.Explosive;projectile.ExplosionRadius=4;
            Assert.That((bool)Call("TryHitGroundBarrel",runtime,projectile,point-Vector2.right,point+Vector2.right*.1f),Is.False);
            Assert.That(enemy.Health,Is.EqualTo(100));Assert.That(Count,Is.EqualTo(2));
            Assert.That(architecture.GetModel<ArcadeRunModel>().Current.Score,Is.Zero);
        }
        [Test] public void PlayerShotDetonatesNearestBarrelOnceWithoutChangingBudget()
        {
            Call("InitializeGroundBarrels",runtime);var first=Point(0);var second=Point(1);var direction=(second-first).normalized;
            runtime.SpawnQueue.Enqueue("preserved-budget");
            Assert.That((bool)Call("TryHitGroundBarrel",runtime,Shot(),first-direction*3,second+direction*3),Is.True);
            Assert.That(Count,Is.EqualTo(1));Assert.That((bool)Call("TryHitGroundBarrel",runtime,Shot(),first-direction,first+direction*.1f),Is.False);
            Assert.That(Count,Is.EqualTo(1));Assert.That(runtime.ContentAreas.Count,Is.EqualTo(1));Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(1));Assert.That(runtime.Enemies.Count,Is.Zero);
        }
        [Test] public void WallOrCloserEnemyBlocksShotToFarBarrel()
        {
            Call("InitializeGroundBarrels",runtime);var point=Point(0);var from=point-Vector2.right*4;var to=point+Vector2.right;
            Set("contentRoundObstacles",new[]{new ArcadeObstacleDefinition(point.x-2,point.y,.2f,2)});
            Assert.That((bool)Call("TryHitGroundBarrel",runtime,Shot(),from,to),Is.False);Assert.That(Count,Is.EqualTo(2));
            Set("contentRoundObstacles",Array.Empty<ArcadeObstacleDefinition>());
            runtime.Enemies.Add(7,new ArcadeEnemyRuntime{Id=7,Position=point-Vector2.right*2,Health=100,MaxHealth=100});
            Assert.That((bool)Call("TryHitGroundBarrel",runtime,Shot(),from,to),Is.False);Assert.That(Count,Is.EqualTo(2));
        }
        [Test] public void SameMapBeginKeepsDestroyedStateAndNewMapInitializesItsOwnBarrels()
        {
            Call("InitializeGroundBarrels",runtime);Call("DetonateGroundBarrel",runtime,0);Call("InitializeGroundBarrels",runtime);
            Assert.That(Count,Is.EqualTo(1));runtime.WaveIndex=1;Call("InitializeGroundBarrels",runtime);Assert.That(Count,Is.EqualTo(2));
            var checkpoint=JObject.FromObject(Call("CaptureRoundCheckpoint",runtime));Assert.That((JArray)checkpoint["DestroyedBarrelIds"],Is.Empty);
        }
        [Test] public void UnknownDestroyedBarrelIsRejectedBeforeCurrentEnemiesAreCleared()
        {
            var raw=JObject.FromObject(Call("CaptureRoundCheckpoint",runtime));raw["DestroyedBarrelIds"]=new JArray(99);
            var checkpoint=raw.ToObject<ArcadeRoundCheckpoint>();runtime.Enemies.Add(7,new ArcadeEnemyRuntime{Id=7,Health=10,MaxHealth=10});
            var error=Assert.Throws<TargetInvocationException>(()=>Call("StartContentRound",runtime,0,checkpoint));
            Assert.That(error.InnerException,Is.TypeOf<InvalidOperationException>());Assert.That(runtime.Enemies.ContainsKey(7),Is.True,"未知桶身份不能先销毁当前现场");
        }
        [Test] public void ExplosionDamagesActorsOnceAndKeepsCarrierDeathExplosion()
        {
            Call("InitializeGroundBarrels",runtime);var point=Point(0);runtime.PlayerPosition=point+Vector2.up*.5f;
            var enemy=new ArcadeEnemyRuntime{Id=7,Position=point+Vector2.right*.5f,Health=100,MaxHealth=100};runtime.Enemies.Add(7,enemy);
            Call("DetonateGroundBarrel",runtime,0);Assert.That(runtime.PlayerHealth,Is.EqualTo(100),"桶爆炸不能伤害玩家");Assert.That(enemy.Health,Is.LessThan(100));
            int playerHealth=runtime.PlayerHealth,enemyHealth=enemy.Health;runtime.PlayerInvulnerability=0;
            Call("TickRoundContent",runtime,.1f);Assert.That(runtime.PlayerHealth,Is.EqualTo(playerHealth));Assert.That(enemy.Health,Is.EqualTo(enemyHealth),"同次爆炸不在危险区重复伤害");
            Call("OnRoundEnemyKilled",runtime,new ArcadeEnemyRuntime{Id=8,Position=point,ContentSpecialKind=1},false);
            Assert.That(runtime.ContentAreas.Any(a=>a.Kind==14),Is.True,"载体原死亡爆炸仍保留");
            Call("TickRoundContent",runtime,.1f);Assert.That(runtime.PlayerHealth,Is.EqualTo(100),"载体爆炸区域也不能延迟伤害玩家");
        }
        [Test] public void ExplosiveWeaponKeepsItsOriginalBlastWhenItDetonatesBarrel()
        {
            Call("InitializeGroundBarrels",runtime);var point=Point(0);
            var enemy=new ArcadeEnemyRuntime{Id=7,Position=point+Vector2.up*2.5f,Health=100,MaxHealth=100};runtime.Enemies.Add(7,enemy);
            var projectile=Shot();projectile.Damage=100;projectile.Behavior=global::ArcadeWeaponBehavior.Explosive;projectile.ExplosionRadius=4;
            Assert.That((bool)Call("TryHitGroundBarrel",runtime,projectile,point-Vector2.right,point+Vector2.right*.1f),Is.True);
            Assert.That(runtime.Enemies.ContainsKey(7),Is.False,"桶爆炸和原武器爆炸都必须结算");
            Assert.That(Count,Is.EqualTo(1));
        }
        [Test] public void ExplosiveWeaponBarrelHitPreservesNearbyPlayerLife()
        {
            var runs=new ArcadeRunSystem();architecture.RegisterSystem(runs);runs.ConfigureForTests(directory);
            Call("InitializeGroundBarrels",runtime);var point=Point(0);runtime.PlayerPosition=point+Vector2.up*.5f;runtime.PlayerHealth=10;
            var enemy=new ArcadeEnemyRuntime{Id=7,Position=point+Vector2.up*2.5f,Health=100,MaxHealth=100};runtime.Enemies.Add(7,enemy);
            var projectile=Shot();projectile.Damage=100;projectile.Behavior=global::ArcadeWeaponBehavior.Explosive;projectile.ExplosionRadius=4;
            Assert.That((bool)Call("TryHitGroundBarrel",runtime,projectile,point-Vector2.right,point+Vector2.right*.1f),Is.True);
            Assert.That(runtime.RespawnRemaining,Is.Zero,"桶爆炸不能触发玩家复活等待");
            Assert.That(architecture.GetModel<ArcadeCombatModel>().Runtime,Is.SameAs(runtime));
            Assert.That(architecture.GetModel<ArcadeRunModel>().Current.RemainingLives,Is.EqualTo(3));
            Assert.That(runtime.Enemies.ContainsKey(7),Is.False);
        }

        [Test] public void CarrierKeepsBarrelUntilDeathWithoutCreatingGroundBarrel()
        {
            Call("InitializeGroundBarrels",runtime);
            var carrier=new ArcadeEnemyRuntime{Id=8,Position=Vector2.zero,Health=100,MaxHealth=100,ContentSpecialKind=1};runtime.Enemies.Add(8,carrier);
            int initial=Count;
            runtime.ContentClock.Advance(120,false);Call("TickVortexBarrels",runtime,.1f);
            Assert.That(Count,Is.EqualTo(initial));Assert.That(carrier.ContentSpecialKind,Is.EqualTo(1));
            int areas=runtime.ContentAreas.Count;Call("OnRoundEnemyKilled",runtime,carrier,false);
            Assert.That(runtime.ContentAreas.Count,Is.EqualTo(areas+1),"一直背桶的载体死亡仍结算既有爆炸");
            Assert.That(Count,Is.EqualTo(initial));
        }

        [Test] public void PurpleExplosionStillProjectsWhenBarrelEndsTheWave()
        {
            Call("InitializeRoundContent",runtime);Call("TickRoundContent",runtime,.01f);
            Call("DetonateGroundBarrel",runtime,0);runtime.WaveClearAwarded=true;runtime.AwaitingExit=true;
            Call("SyncRoundContent",runtime);
            Assert.That(runtime.ContentPresentationSnapshots.Any(a=>a.Kind==14),Is.True,"清场帧也必须收到已结算的桶爆炸");
        }
        [Test] public void SameMapContentRestartNeverReusesBarrelExplosionIdentity()
        {
            Call("InitializeRoundContent",runtime);Call("DetonateGroundBarrel",runtime,0);int first=runtime.ContentAreas.Single(a=>a.Kind==14).Id;
            Call("InitializeRoundContent",runtime);Call("DetonateGroundBarrel",runtime,1);
            Assert.That(runtime.ContentAreas.Where(a=>a.Kind==14).Max(a=>a.Id),Is.GreaterThan(first),"同地图Boss准备不能复用旧爆炸身份");
        }

        [TestCase(1)] [TestCase(4)] public void LastExplosionSurvivesActualBossEntry(int bossKind)
        {
            var layout=catalog.RoundLayouts.First(p=>p.RoundNumber==4&&p.BossKind==bossKind);
            architecture.GetModel<ArcadeRunModel>().Current.ArenaConfigNumber=layout.ArenaConfigNumber;runtime.WaveIndex=3;
            Set("arena",catalog.Arenas.First(a=>a.ArenaNumber==layout.ArenaConfigNumber));
            Call("InitializeRoundContent",runtime);runtime.ContentClock.Advance(10,false);
            Call("OnRoundEnemyKilled",runtime,new ArcadeEnemyRuntime{Id=300,Position=Vector2.zero,ContentSpecialKind=1},false);
            Call("TickWaveTransition",runtime,.01f);Call("SyncRoundContent",runtime);
            Assert.That(runtime.IsFinalBoss||runtime.ThemeBossStarted,Is.True);
            Assert.That(runtime.ContentPresentationSnapshots.Count(a=>a.Kind==14),Is.EqualTo(1),"实际Boss入场不能清掉最后一击表现");
        }

        [Test] public void BarrelKillingThemeBossKeepsAlreadySettledExplosion()
        {
            Call("InitializeRoundContent",runtime);var point=Point(0);
            runtime.ThemeBossStarted=true;runtime.ContentBossEnemyId=300;
            runtime.Enemies.Add(300,new ArcadeEnemyRuntime{Id=300,Position=point,Health=1,MaxHealth=100,ContentBossKind=1});
            Call("DetonateGroundBarrel",runtime,0);Call("SyncRoundContent",runtime);
            Assert.That(runtime.ThemeBossDefeated,Is.True);
            Assert.That(runtime.ContentPresentationSnapshots.Count(a=>a.Kind==14),Is.EqualTo(1),"Boss死亡清理只能停危险，不能吞掉已结算爆炸");
        }

        [Test] public void ManyCarriersNeverReleaseExtraGroundBarrels()
        {
            Call("InitializeGroundBarrels",runtime);
            for(int id=1;id<=30;id++)runtime.Enemies.Add(id,new ArcadeEnemyRuntime{Id=id,Health=100,MaxHealth=100,ContentSpecialKind=1});
            runtime.ContentClock.Advance(20,false);Call("TickVortexBarrels",runtime,.1f);
            Assert.That(Count,Is.EqualTo(Layout.Barrels.Count));
            Assert.That(runtime.Enemies.Values.All(e=>e.ContentSpecialKind==1),Is.True,"所有载体均持续背桶");
        }

        [Test] public void VortexPullsDropsAndConsumesWithoutAwardOrRefund()
        {
            Call("InitializeGroundBarrels",runtime);var point=Point(0);var drop=new ArcadeDropRuntime{Id=90,Position=point+Vector2.up*2,Definition=catalog.Drops[0]};runtime.Drops.Add(90,drop);
            long score=architecture.GetModel<ArcadeRunModel>().Current.Score;
            Call("TickVortexBarrels",runtime,.1f);Assert.That(drop.Position.y,Is.LessThan(point.y+2));Assert.That(runtime.Drops.ContainsKey(90),Is.True);
            Call("TickVortexBarrels",runtime,20f);Assert.That(runtime.Drops.ContainsKey(90),Is.False);Assert.That(architecture.GetModel<ArcadeRunModel>().Current.Score,Is.EqualTo(score));
            Call("DetonateGroundBarrel",runtime,0);Assert.That(runtime.Drops,Is.Empty,"炸桶不返还吞掉的物品");
        }

        [Test] public void VortexFreezesInTreasureRoomAndIgnoresUndeliveredWeapons()
        {
            Call("InitializeGroundBarrels",runtime);var point=Point(0);var drop=new ArcadeDropRuntime{Id=90,Position=point+Vector2.up,DeliveryRemainingSeconds=1};runtime.Drops.Add(90,drop);
            Call("TickVortexBarrels",runtime,1f);Assert.That(drop.Position,Is.EqualTo(point+Vector2.up));
            drop.DeliveryRemainingSeconds=0;runtime.InTreasureRoom=true;Call("TickVortexBarrels",runtime,1f);Assert.That(drop.Position,Is.EqualTo(point+Vector2.up));
            runtime.InTreasureRoom=false;runtime.WaveClearAwarded=true;Call("TickVortexBarrels",runtime,1f);Assert.That(drop.Position,Is.EqualTo(point+Vector2.up));
        }

        [Test] public void BarrelSurvivesWeakShotUntilAccumulatedDamageReachesHealth()
        {
            Call("InitializeGroundBarrels",runtime);var point=Point(0);var weak=Shot();weak.Damage=1;
            Assert.That((bool)Call("TryHitGroundBarrel",runtime,weak,point-Vector2.right,point),Is.True);
            Assert.That(Count,Is.EqualTo(2),"落地桶有独立耐久");
            Assert.That((bool)Call("TryHitGroundBarrel",runtime,Shot(),point-Vector2.right,point),Is.True);Assert.That(Count,Is.EqualTo(1));
        }
    }
}
