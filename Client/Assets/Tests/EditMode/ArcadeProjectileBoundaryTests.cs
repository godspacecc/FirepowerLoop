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
    /// <summary>真实战斗循环验证全地图弹道越界、水面放行、实体命中和寿命回收。</summary>
    public sealed class ArcadeProjectileBoundaryTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;
        private ArcadeRunModel run;
        [SetUp] public void Setup()
        {
            var architecture=new Game();run=new ArcadeRunModel();architecture.RegisterModel(run);architecture.RegisterModel(new ArcadeCombatModel());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);
            run.CommitSafeNode(new ArcadeRunSaveData{RunId="all-map-shot",RemainingLives=3,IsCasualRoute=true});run.EnterArena();
            runtime=new ArcadeCombatRuntime{ArenaNumber=2,PlayerHealth=100,PlayerMaxHealth=100,PlayerPosition=Vector2.zero};architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);
            Set("catalog",catalog);Set("view",new ArcadeCombatViewController());Set("random",new System.Random(8));
        }
        private void Set(string name,object value)=>typeof(ArcadeCombatSystem).GetField(name,Private).SetValue(system,value);
        private object Call(string name,params object[] args)=>typeof(ArcadeCombatSystem).GetMethod(name,Private).Invoke(system,args);
        private void Arena(int number,int round=1)
        {Set("arena",catalog.Arenas.First(a=>a.ArenaNumber==number));runtime.WaveIndex=round-1;runtime.Projectiles.Clear();runtime.Enemies.Clear();runtime.GroundBarrels.Clear();runtime.Crates.Clear();}
        private ArcadeProjectileRuntime Shot(int kind,Vector2 from,Vector2 velocity,float lifetime=2)
        {
            var p=new ArcadeProjectileRuntime{Id=901,Position=from,Velocity=velocity,RemainingSeconds=lifetime,Damage=1,RemainingHits=1,
                Hostile=kind==1,FromPlayerWeapon=kind!=1,SourceWeaponId=kind==2?"arcade.weapon.rocket":kind==3?"arcade.weapon.raygun":"arcade.weapon.basic",
                WeaponPowerLevel=kind>=2?2:0,RemainingReflections=2};runtime.Projectiles[p.Id]=p;return p;
        }
        [Test] public void EveryRoundLayoutAndProjectileKindCanTravelOutsideAllFourEdges()
        {
            foreach(var layout in catalog.RoundLayouts)foreach(int kind in Enumerable.Range(0,4))foreach(var direction in new[]{Vector2.right,Vector2.left,Vector2.up,Vector2.down})
            {
                Arena(layout.ArenaConfigNumber,layout.RoundNumber);
                float half=layout.ArenaConfigNumber==22?30.5f:layout.ArenaConfigNumber==21?(direction.x!=0?20.5f:19.5f):19.5f;
                var p=Shot(kind,direction*(half-.1f),direction*10);Call("TickProjectiles",runtime,.1f);
                Assert.That(runtime.Projectiles.ContainsKey(901),Is.True,$"{layout.Id} kind={kind} direction={direction}");
                Call("TickProjectiles",runtime,.1f);Assert.That(runtime.Projectiles.ContainsKey(901),Is.True);
                Assert.That(p.Velocity,Is.EqualTo(direction*10));Assert.That(Vector2.Dot(p.Position,direction),Is.EqualTo(half+1.9f).Within(.002f));
            }
        }
        [Test] public void LegacyArenasAndChallengesAlsoAllowOutsideFlight()
        {
            run.Current.IsCasualRoute=false;
            foreach(var arena in catalog.Arenas)foreach(bool challenge in new[]{false,true})foreach(int kind in Enumerable.Range(0,4))
            {Arena(arena.ArenaNumber);runtime.IsChallenge=challenge;Shot(kind,new Vector2(100,0),new Vector2(10,0));Call("TickProjectiles",runtime,.1f);Assert.That(runtime.Projectiles.ContainsKey(901),Is.True,$"arena={arena.ArenaNumber}, challenge={challenge}, kind={kind}");}
        }
        [Test] public void DockWaterBlocksWalkingButAllowsEveryProjectileKind()
        {
            for(int round=1;round<=4;round++)foreach(int kind in Enumerable.Range(0,4))
            {
                Arena(21,round);var water=new Vector2(15,7);
                Assert.That(ArcadeCombatSystem.IsInsideObstacle(water,0,(System.Collections.Generic.IReadOnlyList<ArcadeObstacleDefinition>)Call("ObstaclesFor",runtime)),Is.True);
                var p=Shot(kind,new Vector2(10,7),new Vector2(10,0));Call("TickProjectiles",runtime,.6f);
                Assert.That(runtime.Projectiles.ContainsKey(901),Is.True,$"dock round {round}, kind {kind}");Assert.That(p.Position.x,Is.EqualTo(16).Within(.001));
            }
        }
        [Test] public void SolidWallConsumesPurpleRayWithoutReflectionEvenWithOldAllowance()
        {
            run.Current.IsCasualRoute=false;Set("arena",new ArcadeArenaDefinition(99,new[]{"arcade.group.basic"},new[]{new ArcadeObstacleDefinition(3,0,.1f,10)},false,null,0,1));
            Shot(3,Vector2.zero,new Vector2(22,0));Call("TickProjectiles",runtime,.2f);Assert.That(runtime.Projectiles,Is.Empty);
        }
        [Test] public void OutsideShotsStillExpireForEveryProjectileKind()
        {
            foreach(int kind in Enumerable.Range(0,4)){Arena(21);Shot(kind,new Vector2(25,0),Vector2.right*10,.15f);Call("TickProjectiles",runtime,.1f);Assert.That(runtime.Projectiles.ContainsKey(901),Is.True);Call("TickProjectiles",runtime,.1f);Assert.That(runtime.Projectiles,Is.Empty);}
        }
        [Test] public void AllShotKindsStillStopAtDockCargo()
        {
            foreach(int kind in Enumerable.Range(0,4))
            {Arena(21);Shot(kind,new Vector2(-6.75f,7),Vector2.up*10);Call("TickProjectiles",runtime,.3f);Assert.That(runtime.Projectiles,Is.Empty,$"cargo kind={kind}");}
        }
    }
}
