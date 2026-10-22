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
    /// <summary>真实紫级射线的墙面余程、连接上限和一次命中契约。</summary>
    public sealed class ArcadePurpleRayTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private ArcadeCombatSystem system;private ArcadeCombatRuntime runtime;private ArcadeConfigCatalogDefinition catalog;
        [SetUp] public void Setup()
        {
            var architecture=new Game();var run=new ArcadeRunModel();architecture.RegisterModel(run);architecture.RegisterModel(new ArcadeCombatModel());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);
            run.CommitSafeNode(new ArcadeRunSaveData{RunId="ray-test",RemainingLives=3,IsCasualRoute=false});run.EnterArena();
            runtime=new ArcadeCombatRuntime{ArenaNumber=2,PlayerHealth=100,PlayerMaxHealth=100,AimDirection=Vector2.up};architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);
            Set("catalog",catalog);Arena();Set("view",new ArcadeCombatViewController());Set("random",new System.Random(6));
        }
        private void Set(string name,object value)=>typeof(ArcadeCombatSystem).GetField(name,Private).SetValue(system,value);
        private object Call(string name,params object[] args)=>typeof(ArcadeCombatSystem).GetMethod(name,Private).Invoke(system,args);
        private void Arena(params ArcadeObstacleDefinition[] walls)=>Set("arena",new ArcadeArenaDefinition(99,new[]{"arcade.group.basic"},walls,false,null,0,1));
        private void Pick(string id)=>Assert.That(Call("ApplyDrop",runtime,catalog.Drops.First(d=>d.Id=="arcade.drop."+id)),Is.True);
        private ArcadeProjectileRuntime Shot(bool purple=true)
        {Pick("weapon.raygun");Pick(purple?"skull.large":"skull.small");Call("TryFire",runtime);return runtime.Projectiles.Values.Single();}
        private ArcadeEnemyRuntime Enemy(int id,float x,float z){var e=new ArcadeEnemyRuntime{Id=id,Position=new Vector2(x,z),Health=1000,MaxHealth=1000};runtime.Enemies[id]=e;return e;}
        [Test] public void TwoReflectionsKeepUnusedDistanceAndThirdWallConsumesShot()
        {
            Arena(new ArcadeObstacleDefinition(-3,0,.1f,12),new ArcadeObstacleDefinition(3,0,.1f,12));
            var p=Shot();p.Position=Vector2.zero;p.Velocity=new Vector2(22,0);Call("TickProjectiles",runtime,.2f);
            Assert.That(runtime.Projectiles.ContainsKey(p.Id),Is.True);Assert.That(p.Velocity.x,Is.LessThan(0));Assert.That(p.Position.x,Is.EqualTo(1.4f).Within(.01));
            Call("TickProjectiles",runtime,.3f);Assert.That(p.Velocity.x,Is.GreaterThan(0));Assert.That(runtime.Projectiles.Count,Is.EqualTo(1));
            Call("TickProjectiles",runtime,.3f);Assert.That(runtime.Projectiles,Is.Empty);
        }
        [Test] public void CornerReflectsBothComponentsWithinOneStep()
        {
            Arena(new ArcadeObstacleDefinition(3,3,1,1));
            var p=Shot();p.Position=Vector2.zero;p.Velocity=new Vector2(10,10);Call("TickProjectiles",runtime,.3f);
            Assert.That(runtime.Projectiles.Count,Is.EqualTo(1));Assert.That(p.Velocity.x,Is.LessThan(0));Assert.That(p.Velocity.y,Is.LessThan(0));
            Assert.That(p.Position.x,Is.EqualTo(1).Within(.01));Assert.That(p.Position.y,Is.EqualTo(1).Within(.01));
        }
        [Test] public void FirstEnemyAndTwoOtherEnemiesAreDamagedOnceIncludingCloseNeighbours()
        {
            var first=Enemy(1,0,4);var a=Enemy(2,.5f,4);var b=Enemy(3,-.5f,4);var extra=Enemy(4,2,4);var p=Shot();int damage=p.Damage;
            Call("TickProjectiles",runtime,.5f);Assert.That(first.Health,Is.EqualTo(1000-damage));Assert.That(a.Health,Is.EqualTo(1000-damage));Assert.That(b.Health,Is.EqualTo(1000-damage));Assert.That(extra.Health,Is.EqualTo(1000));Assert.That(runtime.Projectiles,Is.Empty);
            Call("TickProjectiles",runtime,.5f);Assert.That(first.Health,Is.EqualTo(1000-damage));Assert.That(a.Health,Is.EqualTo(1000-damage));
        }
        [Test] public void ChainIncludesFourMetreBoundaryAndExcludesFurtherTarget()
        {
            Enemy(1,0,4);var edge=Enemy(2,4,4);var outside=Enemy(3,-4.01f,4);var p=Shot();int damage=p.Damage;
            Call("TickProjectiles",runtime,.5f);Assert.That(edge.Health,Is.EqualTo(1000-damage));Assert.That(outside.Health,Is.EqualTo(1000));
        }
        [Test] public void LifetimeAndPauseDoNotHitDistantEnemyOrChangeInFlightTier()
        {
            var enemy=Enemy(1,0,10);var p=Shot();p.RemainingSeconds=.1f;var before=p.Position;Pick("weapon.rapid");
            Call("TickProjectiles",runtime,0f);Assert.That(p.Position,Is.EqualTo(before));Assert.That(p.RemainingSeconds,Is.EqualTo(.1f));Assert.That(p.WeaponPowerLevel,Is.EqualTo(2));
            Call("TickProjectiles",runtime,10f);Assert.That(enemy.Health,Is.EqualTo(1000));Assert.That(runtime.Projectiles,Is.Empty);
        }
        [Test] public void RedRayStillStopsAtWall()
        {
            Arena(new ArcadeObstacleDefinition(0,3,5,.5f));
            Shot(false);Call("TickProjectiles",runtime,.12f);Assert.That(runtime.Projectiles,Is.Empty);
        }
        [Test] public void ArcsFreezeExpireAndCannotSurviveNewMapInitialization()
        {
            Enemy(1,0,4);Enemy(2,2,4);Shot();Call("TickProjectiles",runtime,.3f);Assert.That(runtime.PurpleRayArcs.Count,Is.EqualTo(1));
            float before=runtime.PurpleRayArcs[0].RemainingSeconds;Call("TickProjectiles",runtime,0f);Assert.That(runtime.PurpleRayArcs[0].RemainingSeconds,Is.EqualTo(before));
            Call("TickProjectiles",runtime,.2f);Assert.That(runtime.PurpleRayArcs,Is.Empty);
            runtime.FireCooldown=0;Call("TryFire",runtime);Call("TickProjectiles",runtime,.3f);Assert.That(runtime.PurpleRayArcs.Count,Is.EqualTo(1));
            Call("InitializeRoundContent",runtime);Assert.That(runtime.PurpleRayArcs,Is.Empty);
        }
    }
}
