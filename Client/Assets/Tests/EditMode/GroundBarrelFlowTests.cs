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
        private static ArcadeProjectileRuntime Shot()=>new ArcadeProjectileRuntime{Hostile=false,FromPlayerWeapon=true,Damage=10};
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
            Call("DetonateGroundBarrel",runtime,0);Assert.That(runtime.PlayerHealth,Is.LessThan(100));Assert.That(enemy.Health,Is.LessThan(100));
            int playerHealth=runtime.PlayerHealth,enemyHealth=enemy.Health;runtime.PlayerInvulnerability=0;
            Call("TickRoundContent",runtime,.1f);Assert.That(runtime.PlayerHealth,Is.EqualTo(playerHealth));Assert.That(enemy.Health,Is.EqualTo(enemyHealth),"同次爆炸不在危险区重复伤害");
            Call("OnRoundEnemyKilled",runtime,new ArcadeEnemyRuntime{Id=8,Position=point,ContentSpecialKind=1},false);
            Assert.That(runtime.ContentAreas.Any(a=>a.Kind==3 && a.DamageRatio>0),Is.True,"载体原死亡爆炸仍保留");
        }
        [Test] public void ExplosiveWeaponKeepsItsOriginalBlastWhenItDetonatesBarrel()
        {
            Call("InitializeGroundBarrels",runtime);var point=Point(0);
            var enemy=new ArcadeEnemyRuntime{Id=7,Position=point+Vector2.up*2.5f,Health=100,MaxHealth=100};runtime.Enemies.Add(7,enemy);
            var projectile=Shot();projectile.Damage=20;projectile.Behavior=global::ArcadeWeaponBehavior.Explosive;projectile.ExplosionRadius=4;
            Assert.That((bool)Call("TryHitGroundBarrel",runtime,projectile,point-Vector2.right,point+Vector2.right*.1f),Is.True);
            Assert.That(enemy.Health,Is.EqualTo(68),"当前正式桶比例伤害12点和原武器爆炸20点均需结算");
            Assert.That(Count,Is.EqualTo(1));
        }
        [Test] public void ExplosiveWeaponStillBlastsEnemiesWhenBarrelHitStartsRespawn()
        {
            var runs=new ArcadeRunSystem();architecture.RegisterSystem(runs);runs.ConfigureForTests(directory);
            Call("InitializeGroundBarrels",runtime);var point=Point(0);runtime.PlayerPosition=point+Vector2.up*.5f;runtime.PlayerHealth=10;
            var enemy=new ArcadeEnemyRuntime{Id=7,Position=point+Vector2.up*2.5f,Health=100,MaxHealth=100};runtime.Enemies.Add(7,enemy);
            var projectile=Shot();projectile.Damage=20;projectile.Behavior=global::ArcadeWeaponBehavior.Explosive;projectile.ExplosionRadius=4;
            Assert.That((bool)Call("TryHitGroundBarrel",runtime,projectile,point-Vector2.right,point+Vector2.right*.1f),Is.True);
            Assert.That(runtime.RespawnRemaining,Is.GreaterThan(0),"桶伤害需真实进入复活等待");
            Assert.That(architecture.GetModel<ArcadeCombatModel>().Runtime,Is.SameAs(runtime));
            Assert.That(architecture.GetModel<ArcadeRunModel>().Current.RemainingLives,Is.EqualTo(2));
            Assert.That(enemy.Health,Is.EqualTo(68),"已接受的武器爆炸不因当前玩家复活等待而丢失");
        }
    }
}
