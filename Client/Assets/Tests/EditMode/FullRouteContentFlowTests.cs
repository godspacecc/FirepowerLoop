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
    /// <summary>单人特殊内容的真实运行态定向检查，只使用隔离本局文件。</summary>
    public sealed class FullRouteContentFlowTests
    {
        private Game architecture;
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;
        private string directory;
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        [SetUp] public void SetUp()
        {
            directory=Path.Combine(Path.GetTempPath(),"FirepowerContent-"+Guid.NewGuid().ToString("N"));
            architecture=new Game();architecture.RegisterModel(new ArcadeRunModel());architecture.RegisterModel(new ArcadeCombatModel());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(name=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",name+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            var run=new ArcadeRunSaveData {RunId=Guid.NewGuid().ToString("N"),IsCasualRoute=true,ArenaConfigNumber=15,CompletedArenaCount=1,NextArenaNumber=2,UpdatedAtUtc=DateTime.UtcNow.ToString("O")};
            run.Checkpoint.ArenaNumber=2;run.EquipmentBySlot["weapon"]=ArcadeRunDefaults.BasicWeaponId;
            Assert.That(new ArcadeRunFileStore(directory).Save(run,out error),Is.True,error);architecture.GetModel<ArcadeRunModel>().CommitSafeNode(run);architecture.GetModel<ArcadeRunModel>().EnterArena();
            runtime=new ArcadeCombatRuntime {ArenaNumber=2,PlayerHealth=100,PlayerMaxHealth=100};architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);
            Set("catalog",catalog);Set("arena",catalog.Arenas.First(a=>a.ArenaNumber==15));Set("random",new System.Random(17));
        }
        [TearDown] public void TearDown(){architecture.GetModel<ArcadeCombatModel>().Clear();if(Directory.Exists(directory))Directory.Delete(directory,true);}
        private void Set(string name,object value)=>typeof(ArcadeCombatSystem).GetField(name,Flags).SetValue(system,value);
        private object Call(string name,params object[] values){var method=typeof(ArcadeCombatSystem).GetMethod(name,Flags);Assert.That(method,Is.Not.Null,"特殊内容规则尚未接入："+name);return method.Invoke(system,values);}
        private static void Field(object owner,string name,object value){var field=owner.GetType().GetField(name);Assert.That(field,Is.Not.Null,"特殊运行态字段缺失："+name);field.SetValue(owner,value);}
        private ArcadeEnemyRuntime Enemy(int kind){var e=new ArcadeEnemyRuntime{Id=1,Health=100,MaxHealth=100,Speed=2,Damage=10,Position=Vector2.zero};Field(e,"ContentSpecialKind",kind);runtime.Enemies[e.Id]=e;return e;}
        [Test] public void MannequinNeedsThreeSeparatedHitsAndFreezes()
        {
            Call("InitializeRoundContent",runtime);var enemy=Enemy(3);Call("DamageEnemy",enemy,999);Assert.That(enemy.Health,Is.GreaterThan(0));
            var before=enemy.Position;Call("TickRoundEnemyContent",runtime,enemy,Vector2.up*10,false,.1f);Assert.That(enemy.Position,Is.EqualTo(before));
            Call("TickRoundContent",runtime,3f);Call("DamageEnemy",enemy,999);Assert.That(enemy.Health,Is.GreaterThan(0));
            Call("TickRoundContent",runtime,3f);Call("DamageEnemy",enemy,999);Assert.That(enemy.Health,Is.LessThanOrEqualTo(0));
        }
        [Test] public void BurrowWarningRejectsDamageThenExposes()
        {
            Call("InitializeRoundContent",runtime);var enemy=Enemy(4);Call("DamageEnemy",enemy,10);Assert.That(enemy.Health,Is.EqualTo(100));
            Call("TickRoundContent",runtime,2f);Call("DamageEnemy",enemy,10);Assert.That(enemy.Health,Is.LessThan(100));
        }
        [Test] public void ParasiteHitReducesMovementForRemainingDuration()
        {
            Call("InitializeRoundContent",runtime);var projectile=new ArcadeProjectileRuntime();Field(projectile,"ContentSlowSeconds",2f);
            Call("OnRoundProjectileHit",runtime,projectile);var delta=(Vector2)Call("RoundMovementDelta",runtime,Vector2.right,5f,.1f);Assert.That(delta.x,Is.LessThan(.5f));
        }
        [Test] public void ContentClockDoesNotAdvanceAfterClear()
        {
            Call("InitializeRoundContent",runtime);runtime.AwaitingExit=true;Call("TickRoundContent",runtime,100f);
            var clock=runtime.GetType().GetField("ContentClock");Assert.That(clock,Is.Not.Null);dynamic value=clock.GetValue(runtime);Assert.That((double)value.Elapsed,Is.EqualTo(0));
        }
        [Test] public void SpecialSpawnSelectionDoesNotAddEnemyBudget()
        {
            Call("InitializeRoundContent",runtime);runtime.SpawnQueue.Enqueue("budget");int selected=0;
            for(int i=1;i<=20;i++){var enemy=new ArcadeEnemyRuntime{Id=i,Health=10,MaxHealth=10,Speed=2};Call("AssignRoundEnemyContent",runtime,enemy);var field=enemy.GetType().GetField("ContentSpecialKind");Assert.That(field,Is.Not.Null);if((int)field.GetValue(enemy)!=0)selected++;}
            Assert.That(selected,Is.GreaterThan(0).And.LessThan(20));Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(1));
        }
        [Test] public void ToxicDeathCreatesOwnedAreaWithoutResurrectingEnemy()
        {
            Call("InitializeRoundContent",runtime);var enemy=Enemy(15);runtime.Enemies.Remove(enemy.Id);Call("OnRoundEnemyKilled",runtime,enemy,false);
            var areas=runtime.GetType().GetField("ContentAreas");Assert.That(areas,Is.Not.Null);Assert.That(((System.Collections.ICollection)areas.GetValue(runtime)).Count,Is.EqualTo(1));Assert.That(runtime.Enemies.Count,Is.Zero);
        }
        private FirepowerLoop.Arcade.ArcadeRoundLayoutParameters EventTheme(int kind)
        {
            var layout=catalog.RoundLayouts.First(p=>p.EventKind==kind);
            var run=architecture.GetModel<ArcadeRunModel>().Current;run.ArenaConfigNumber=layout.ArenaConfigNumber;
            architecture.GetModel<ArcadeRunModel>().CommitSafeNode(run);architecture.GetModel<ArcadeRunModel>().EnterArena();
            runtime.WaveIndex=layout.RoundNumber-1;Set("arena",catalog.Arenas.First(a=>a.ArenaNumber==layout.ArenaConfigNumber));Call("InitializeRoundContent",runtime);return layout;
        }
        [Test] public void IceMomentumCannotCrossThinWallInOneFrame()
        {
            var layout=EventTheme(5);runtime.PlayerPosition=new Vector2(-2,0);runtime.ContentVelocity=Vector2.right*100;
            Set("contentObstacleIdentity",layout.Id);Set("contentRoundObstacles",new[]{new ArcadeObstacleDefinition(0,0,.1f,5)});
            var delta=(Vector2)Call("RoundMovementDelta",runtime,Vector2.zero,5f,.2f);
            Assert.That(runtime.PlayerPosition.x+delta.x,Is.LessThanOrEqualTo(-catalog.Rule.PlayerRadius-.09f),"冰面不能跨越端点以外的薄墙");
            Assert.That(runtime.ContentVelocity.x,Is.Zero);
        }
        [Test] public void BumperMovesToDifferentLegalLandingAndKeepsCooldown()
        {
            var layout=EventTheme(9);runtime.PlayerPosition=runtime.ContentEventAnchor;var before=runtime.PlayerPosition;
            Call("TickRoundContent",runtime,layout.EventWarning+.1f);
            Assert.That(Vector2.Distance(before,runtime.PlayerPosition),Is.GreaterThan(layout.EventRadius));
            Assert.That(layout.IsWalkable(runtime.PlayerPosition.x,runtime.PlayerPosition.y,catalog.Rule.PlayerRadius,catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight),Is.True);
            Assert.That(runtime.ContentBumperCooldown,Is.GreaterThan(0));
        }
        [Test] public void LaserWarningIsSafeAndActiveCapsuleDamages()
        {
            var layout=EventTheme(4);runtime.PlayerPosition=Vector2.zero;
            Call("TickRoundContent",runtime,layout.EventWarning*.5f);Assert.That(runtime.PlayerHealth,Is.EqualTo(100));
            Assert.That(runtime.ContentAreaSnapshots.Any(p=>p.Kind==4 && p.Warning),Is.True,"预警线必须真实投影");
            Call("TickRoundContent",runtime,layout.EventWarning*.5f+.1f);Assert.That(runtime.PlayerHealth,Is.LessThan(100));
        }
        [Test] public void HomingProjectileTurnsAtSpeedAndPoolResetDropsContent()
        {
            Call("InitializeRoundContent",runtime);runtime.PlayerPosition=Vector2.up*10;
            var projectile=new ArcadeProjectileRuntime{Hostile=true,Velocity=Vector2.right*5,ContentTurnRate=2,ContentSlowSeconds=3};
            Call("SteerRoundProjectile",runtime,projectile,.1f);
            Assert.That(projectile.Velocity.y,Is.GreaterThan(0));Assert.That(projectile.Velocity.magnitude,Is.EqualTo(5).Within(.001f));
            projectile.Reset();Assert.That(projectile.ContentTurnRate,Is.Zero);Assert.That(projectile.ContentSlowSeconds,Is.Zero);
        }
        [Test] public void MegaReinforcementsUseOrdinaryGroupWhenBossGroupCapIsOne()
        {
            var selected=catalog.Arenas.First(a=>a.ArenaNumber==16);
            var run=architecture.GetModel<ArcadeRunModel>().Current;run.ArenaConfigNumber=16;
            architecture.GetModel<ArcadeRunModel>().CommitSafeNode(run);architecture.GetModel<ArcadeRunModel>().EnterArena();
            runtime.WaveIndex=3;runtime.ThemeBossStarted=true;runtime.ContentBossEnemyId=1;runtime.NextEntityId=2;
            Set("arena",selected);Set("group",catalog.EnemyGroups.First(g=>g.Id==selected.WaveGroupIds[3]));
            Assert.That(catalog.EnemyGroups.First(g=>g.Id==selected.WaveGroupIds[3]).MaxAlive,Is.EqualTo(1),"夹具必须实际命中单体Boss组");
            Call("InitializeRoundContent",runtime);
            var boss=new ArcadeEnemyRuntime{Id=1,Health=100,MaxHealth=100,ContentBossKind=3};runtime.Enemies.Add(1,boss);
            runtime.SpawnQueue.Enqueue("unchanged-budget");Call("SpawnThemeReinforcements",runtime,2);
            Assert.That(runtime.Enemies.Count,Is.EqualTo(3),"独立Boss加两只真实增援");
            Assert.That(runtime.Enemies.Values.Count(e=>e.Id!=1 && e.ContentBossKind==0),Is.EqualTo(2));
            var ordinary=catalog.EnemyGroups.First(g=>g.Id==selected.WaveGroupIds[0]);
            Assert.That(runtime.Enemies.Count,Is.LessThanOrEqualTo(Math.Min(ordinary.MaxAlive,catalog.Rule.MaxAliveEnemies)));
            Assert.That(runtime.Enemies.Values.Where(e=>e.Id!=1).All(e=>ordinary.Members.Any(m=>m.EnemyId==e.ConfigId)),Is.True);
            Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(1),"独立增援不改普通轮预算");
        }
    }
}
