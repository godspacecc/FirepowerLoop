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
    /// <summary>紫火箭实际发射、目标变化及完整路径命中，不以预览轨迹代替权威结果。</summary>
    public sealed class ArcadePurpleRocketTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;
        [SetUp] public void Setup()
        {
            var architecture=new Game();var run=new ArcadeRunModel();architecture.RegisterModel(run);architecture.RegisterModel(new ArcadeCombatModel());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);
            run.CommitSafeNode(new ArcadeRunSaveData{RunId="upgrade-test",RemainingLives=3,IsCasualRoute=true});run.EnterArena();
            runtime=new ArcadeCombatRuntime{ArenaNumber=1,PlayerHealth=100,PlayerMaxHealth=100,AimDirection=Vector2.up};architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);
            Set("catalog",catalog);Set("arena",catalog.Arenas.First(a=>a.ArenaNumber==1));Set("view",new ArcadeCombatViewController());Set("random",new System.Random(5));
        }
        private void Set(string name,object value)=>typeof(ArcadeCombatSystem).GetField(name,Private).SetValue(system,value);
        private object Call(string name,params object[] args)=>typeof(ArcadeCombatSystem).GetMethod(name,Private).Invoke(system,args);
        private void Pick(string suffix)=>Assert.That(Call("ApplyDrop",runtime,catalog.Drops.First(d=>d.Id=="arcade.drop."+suffix)),Is.True);
        private int Level=>runtime.Powerups.TryGetValue(ArcadePowerupBehavior.WeaponSkull,out var s)&&s.RemainingSeconds>0?Mathf.RoundToInt(s.Definition.Magnitude)-1:0;

        private void Ready(){Pick("weapon.rocket");Pick("skull.large");}
        private ArcadeEnemyRuntime Enemy(int id,float x,float z){var e=new ArcadeEnemyRuntime{Id=id,Position=new Vector2(x,z),Health=1000,MaxHealth=1000};runtime.Enemies[id]=e;return e;}
        [Test] public void PurpleVolleyHasThreeRocketsAndConservesTotalDamage()
        {
            Ready();Enemy(30,-2,10);Enemy(20,0,9);Enemy(10,2,10);Call("TryFire",runtime);
            var shots=runtime.Projectiles.Values.ToArray();Assert.That(shots.Length,Is.EqualTo(3));
            Assert.That(shots.Sum(p=>p.Damage),Is.EqualTo(catalog.Weapons.First(w=>w.Id=="arcade.weapon.rocket").Damage*3));
            Assert.That(shots.All(p=>p.ProjectileVisualResourceName.EndsWith("#Purple")),Is.True);
            Assert.That(shots.Select(p=>p.Velocity.x).Distinct().Count(),Is.EqualTo(3));
        }
        [Test] public void RedRocketKeepsSingleShotAndPurpleGrenadeKeepsContinuousSingleShape()
        {
            Pick("weapon.rocket");Pick("skull.small");Call("TryFire",runtime);Assert.That(runtime.Projectiles.Count,Is.EqualTo(1));
            runtime.Projectiles.Clear();runtime.FireCooldown=0;Pick("weapon.grenade");Pick("skull.large");Call("TryFire",runtime);
            Assert.That(runtime.Projectiles.Count,Is.EqualTo(1));Assert.That(runtime.FireCooldown,Is.EqualTo(catalog.Weapons.First(w=>w.Id=="arcade.weapon.grenade").FireIntervalSeconds/3).Within(.0001));
        }
        [Test] public void FullBudgetDoesNotCreatePartialVolleyOrConsumeFireSequence()
        {
            Ready();for(int i=0;i<catalog.Rule.MaxProjectiles-2;i++)runtime.Projectiles[10000+i]=new ArcadeProjectileRuntime{Id=10000+i,RemainingSeconds=1};
            int before=runtime.Projectiles.Count;Call("TryFire",runtime);Assert.That(runtime.Projectiles.Count,Is.EqualTo(before));Assert.That(runtime.PlayerFireSequence,Is.Zero);
        }
        [Test] public void InFlightRocketsTrackMovingEnemyAfterWeaponChange()
        {
            Ready();var enemy=Enemy(9,0,10);Call("TryFire",runtime);var shots=runtime.Projectiles.Values.ToArray();
            var before=shots.Select(p=>p.Velocity.x).ToArray();Pick("weapon.rapid");enemy.Position=new Vector2(5,10);Call("TickProjectiles",runtime,.1f);
            Assert.That(shots.All(p=>p.Velocity.x>before[Array.IndexOf(shots,p)]),Is.True);Assert.That(shots.All(p=>p.ProjectileVisualResourceName.EndsWith("#Purple")),Is.True);
        }
        [Test] public void PauseAndHugeStepRespectLifetimeWithoutDistantBlast()
        {
            Ready();var enemy=Enemy(9,0,50);Call("TryFire",runtime);var shots=runtime.Projectiles.Values.ToArray();var positions=shots.Select(p=>p.Position).ToArray();
            Call("TickProjectiles",runtime,0f);Assert.That(shots.Select((p,i)=>p.Position==positions[i]).All(v=>v),Is.True);
            Call("TickProjectiles",runtime,10f);Assert.That(runtime.Projectiles,Is.Empty);Assert.That(enemy.Health,Is.EqualTo(1000));
        }
        [Test] public void NearerWallStopsSweepBeforeEnemyBehindIt()
        {
            Ready();var walls=(System.Collections.Generic.IReadOnlyList<ArcadeObstacleDefinition>)Call("ObstaclesFor",runtime);Assert.That(walls.Count,Is.GreaterThan(0));
            var wall=walls[0];var axis=wall.HalfExtents.x<wall.HalfExtents.y?Vector2.right:Vector2.up;float half=wall.HalfExtents.x<wall.HalfExtents.y?wall.HalfExtents.x:wall.HalfExtents.y;
            var center=wall.Center;runtime.PlayerPosition=center+axis*(half+2);runtime.AimDirection=-axis;
            var at=center-axis*(half+7);var enemy=Enemy(9,at.x,at.y);Call("TryFire",runtime);Call("TickProjectiles",runtime,1f);Assert.That(enemy.Health,Is.EqualTo(1000));Assert.That(runtime.Projectiles,Is.Empty);
        }
        [Test] public void NearestCrateKeepsExistingDirectAndAreaDamageBudget()
        {
            Ready();Enemy(9,0,10);
            runtime.Crates[1]=new FirepowerLoop.Arcade.ArcadeCrateState(new FirepowerLoop.Arcade.ArcadeBabylonParameters.Crate(1,0,3,.4f,1000));
            runtime.Crates[2]=new FirepowerLoop.Arcade.ArcadeCrateState(new FirepowerLoop.Arcade.ArcadeBabylonParameters.Crate(2,2,3,.4f,1000));
            Call("TryFire",runtime);Call("TickProjectiles",runtime,.6f);int damage=catalog.Weapons.First(w=>w.Id=="arcade.weapon.rocket").Damage*3;
            Assert.That(runtime.Crates[1].Health,Is.EqualTo(1000-damage*2));Assert.That(runtime.Crates[2].Health,Is.EqualTo(1000-damage));Assert.That(runtime.Projectiles,Is.Empty);
        }
        [Test] public void FastCrossingHitsEnemyOncePerRocketWithoutTunnelling()
        {
            Ready();var enemy=Enemy(9,0,5);Call("TryFire",runtime);Call("TickProjectiles",runtime,.7f);
            Assert.That(enemy.Health,Is.EqualTo(1000-catalog.Weapons.First(w=>w.Id=="arcade.weapon.rocket").Damage*3));Assert.That(runtime.Projectiles,Is.Empty);
            Call("TickProjectiles",runtime,.7f);Assert.That(enemy.Health,Is.EqualTo(1000-catalog.Weapons.First(w=>w.Id=="arcade.weapon.rocket").Damage*3));
        }
    }
}
