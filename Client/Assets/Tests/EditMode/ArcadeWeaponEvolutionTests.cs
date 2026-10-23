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
    /// <summary>通过真实发射与碰撞入口验证已确认紫级进化，不创建正式视觉资源。</summary>
    public sealed class ArcadeWeaponEvolutionTests
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
        private void Ready(string weapon,bool purple=true){if(weapon!="basic")Pick("weapon."+weapon);Pick(purple?"skull.large":"skull.small");}
        private void Fire(){runtime.FireCooldown=0;Call("TryFire",runtime);}
        private ArcadeEnemyRuntime Enemy(int id,float x,float y,int health=1000)
        {
            var enemy=new ArcadeEnemyRuntime{Id=id,Position=new Vector2(x,y),Health=health,MaxHealth=health,Damage=1,ConfigId="enemy.zombie"};
            runtime.Enemies[id]=enemy;return enemy;
        }
        [Test] public void PurpleBasicContinuesOnlyAfterKillAndStopsOnSurvivor()
        {
            Ready("basic");var first=Enemy(501,0,1,1);var behind=Enemy(502,0,3f);
            Fire();Call("TickProjectiles",runtime,.022f);Assert.That(runtime.Enemies.ContainsKey(501),Is.False);
            Assert.That(runtime.Projectiles.Count,Is.EqualTo(1),"击杀后仍应保留子弹");
            Call("TickProjectiles",runtime,.11f);Assert.That(behind.Health,Is.LessThan(1000));Assert.That(runtime.Projectiles,Is.Empty,"未击杀时应停弹");
        }
        [Test] public void PurpleRapidAddsTwoSideShotsEveryThirdSuccessfulVolley()
        {
            Ready("rapid");Fire();Assert.That(runtime.Projectiles.Count,Is.EqualTo(1));Fire();Assert.That(runtime.Projectiles.Count,Is.EqualTo(2));
            Fire();Assert.That(runtime.Projectiles.Count,Is.EqualTo(5));Assert.That(runtime.Projectiles.Values.Select(p=>p.Velocity.x).Distinct().Count(),Is.EqualTo(3));
        }
        [Test] public void RedRapidNeverAddsPurpleSideShots()
        {Ready("rapid",false);for(int i=0;i<6;i++)Fire();Assert.That(runtime.Projectiles.Count,Is.EqualTo(6));}
        [Test] public void PurpleSpreadPushesNearbyLivingEnemiesOncePerVolley()
        {
            Ready("spread");var hit=Enemy(501,0,1);var near=Enemy(502,.9f,1.2f);var before=near.Position;
            Fire();Call("TickProjectiles",runtime,.025f);Assert.That(near.Position.y,Is.GreaterThan(before.y+.1f));
            Assert.That(Vector2.Distance(near.Position,before),Is.LessThanOrEqualTo(1.21f));
        }
        [Test] public void PurpleFlameLeavesDamageAfterFlyingFlamesAreGone()
        {
            Ready("flamethrower");Fire();Call("TickProjectiles",runtime,.1f);runtime.Projectiles.Clear();
            var enemy=Enemy(501,0,1.4f);for(int i=0;i<5;i++)Call("TickProjectiles",runtime,.1f);
            Assert.That(enemy.Health,Is.LessThan(1000),"地面残火应继续造成伤害");
            for(int i=0;i<30;i++)Call("TickProjectiles",runtime,.1f);int after=enemy.Health;Call("TickProjectiles",runtime,.5f);Assert.That(enemy.Health,Is.EqualTo(after),"残火应结束");
        }
        [Test] public void PurplePiercingFourthVolleyHitsWiderLine()
        {
            Ready("piercing");for(int i=0;i<3;i++){Fire();runtime.Projectiles.Clear();}
            var enemy=Enemy(501,1.6f,1.3f);Fire();Call("TickProjectiles",runtime,.035f);Assert.That(enemy.Health,Is.LessThan(1000));
        }
        [Test] public void PurpleGrenadeSplitsOnceAndChildrenDoNotSplitAgain()
        {
            Ready("grenade");Fire();var parent=runtime.Projectiles.Values.Single();var expected=parent.Position+parent.Velocity*parent.RemainingSeconds;
            Call("TickProjectiles",runtime,1f);
            Assert.That(runtime.Projectiles.Count,Is.EqualTo(3),"首爆应产生三枚子榴弹");
            Assert.That(runtime.Projectiles.Values.All(p=>Vector2.Distance(p.Position,expected)<.001f),Is.True,"长帧不得越过主弹引信后才分裂");
            Call("TickProjectiles",runtime,1f);Assert.That(runtime.Projectiles,Is.Empty,"子榴弹不再分裂");
        }
        [Test] public void PurpleDroneAddsOneOriginalAttackAndDowngradeRemovesIt()
        {
            Ready("rocket");Pick("support.drone");Enemy(501,-2,4);Enemy(502,2,4);
            Call("TickDrone",runtime,.01f);Assert.That(runtime.Projectiles.Count,Is.EqualTo(2));
            Assert.That(runtime.Projectiles.Values.All(p=>!p.FromPlayerWeapon&&p.Behavior==ArcadeWeaponBehavior.Direct),Is.True);
            runtime.Projectiles.Clear();runtime.Powerups.Remove(ArcadePowerupBehavior.WeaponSkull);
            Call("TickDrone",runtime,.6f);Assert.That(runtime.Projectiles.Count,Is.EqualTo(1));
        }
        [Test] public void BasicLongStepHitsFrontBeforeRearRegardlessOfInsertionOrder()
        {
            Ready("basic");var back=Enemy(502,0,4);Enemy(501,0,2,1);Fire();Call("TickProjectiles",runtime,.25f);
            Assert.That(runtime.Enemies.ContainsKey(501),Is.False,"先击杀前排");Assert.That(back.Health,Is.LessThan(1000));
        }
        [Test] public void SwarmBudgetFailurePreservesThirdVolley()
        {
            Ready("rapid");Fire();Fire();int cap=catalog.Rule.MaxProjectiles;
            for(int i=runtime.Projectiles.Count;i<cap-2;i++)runtime.Projectiles[10000+i]=new ArcadeProjectileRuntime{Id=10000+i,RemainingSeconds=1};
            Fire();Assert.That(runtime.PlayerFireSequence,Is.EqualTo(2));runtime.Projectiles.Clear();Fire();Assert.That(runtime.Projectiles.Count,Is.EqualTo(3));
        }
        [Test] public void SwitchingAwayAndBackWithoutShootingResetsContinuousCharge()
        {
            Ready("rapid");Fire();Fire();Pick("weapon.spread");Pick("weapon.rapid");runtime.Projectiles.Clear();Fire();Assert.That(runtime.Projectiles.Count,Is.EqualTo(1));
        }
        [Test] public void ZeroTimeDoesNotDealDamageOrExpireGround()
        {
            Ready("flamethrower");Fire();Call("TickProjectiles",runtime,.1f);runtime.Projectiles.Clear();var enemy=Enemy(501,0,1.4f);
            for(int i=0;i<10;i++)Call("TickProjectiles",runtime,0f);Assert.That(enemy.Health,Is.EqualTo(1000));
        }
        [Test] public void NewDroneDuringLastSecondStillAddsOneWithoutRefillingClock()
        {
            Pick("support.drone");var support=runtime.Powerups[ArcadePowerupBehavior.Drone];support.RemainingSeconds=.8f;Pick("skull.large");
            Call("TickDrone",runtime,.01f);Assert.That(support.ExtraDrone,Is.Not.Null);Assert.That(support.RemainingSeconds,Is.LessThan(.8f));
        }
        [Test] public void InterleavedShotgunVolleysCannotPushSameTargetTwice()
        {
            Ready("spread");var enemy=Enemy(501,0,1);Fire();var first=runtime.Projectiles.Values.ToArray();runtime.Projectiles.Clear();Fire();var second=runtime.Projectiles.Values.ToArray();
            Call("PushPurpleShotgun",runtime,first[0],enemy.Position);Call("PushPurpleShotgun",runtime,second[0],enemy.Position);var after=enemy.Position;
            Call("PushPurpleShotgun",runtime,first[1],enemy.Position);Assert.That(enemy.Position,Is.EqualTo(after));
        }
        [Test] public void OverlappingGroundDamageIsIndependentOfTickSize()
        {
            int Damage(float dt)
            {
                runtime.WeaponBurnAreas.Clear();runtime.Enemies.Clear();var enemy=Enemy(501,0,0);runtime.WeaponBurnAreas.Add(new FirepowerLoop.Arcade.ArcadeWeaponBurnArea{Id=700,X=-.45f,Z=0,Damage=4,RemainingSeconds=2,TickRemainingSeconds=.25f});
                runtime.WeaponBurnAreas.Add(new FirepowerLoop.Arcade.ArcadeWeaponBurnArea{Id=701,X=.45f,Z=0,Damage=4,RemainingSeconds=2,TickRemainingSeconds=.125f});
                for(int i=0;i<Mathf.RoundToInt(1/dt);i++)Call("TickProjectiles",runtime,dt);return 1000-enemy.Health;
            }
            Assert.That(Damage(.125f),Is.EqualTo(Damage(.25f)),"重叠残火不能随帧率翻倍");
        }
        [Test] public void PiercingMustHitBarrelAfterPassingFrontEnemy()
        {
            Set("arena",catalog.Arenas.First(a=>a.ArenaNumber==3));runtime.ArenaNumber=2;Ready("piercing");runtime.PlayerPosition=new Vector2(0,-8);
            runtime.GroundBarrels[0]=new Vector2(0,-2);runtime.GroundBarrelMapId=catalog.RoundLayouts.First(a=>a.ArenaConfigNumber==3&&a.RoundNumber==1).Id;
            Enemy(501,0,-5);Fire();Call("TickProjectiles",runtime,.6f);
            Assert.That(runtime.GroundBarrelHealth.ContainsKey(0)||!runtime.GroundBarrels.ContainsKey(0),Is.True,"穿过前敌后必须命中桶");
        }
        [Test] public void ChargedBeamDamagesSideEnemyBeforeBarrel()
        {
            Set("arena",catalog.Arenas.First(a=>a.ArenaNumber==3));runtime.ArenaNumber=2;Ready("piercing");runtime.PlayerPosition=new Vector2(0,-8);
            runtime.GroundBarrels[100]=new Vector2(0,-2);runtime.GroundBarrelMapId=catalog.RoundLayouts.First(a=>a.ArenaConfigNumber==3&&a.RoundNumber==1).Id;
            for(int i=0;i<3;i++){Fire();runtime.Projectiles.Clear();}
            var enemy=Enemy(501,1.6f,-5);Fire();runtime.Projectiles.Values.Single().Damage=1;Call("TickProjectiles",runtime,.6f);
            Assert.That(enemy.Health,Is.EqualTo(999),"宽束应先擦中桶前侧边敌人，不能只击桶");
        }
        [Test] public void NewGroundAfterDryGapStartsFreshContactClock()
        {
            Ready("flamethrower");var enemy=Enemy(501,0,0);
            runtime.WeaponBurnAreas.Add(new FirepowerLoop.Arcade.ArcadeWeaponBurnArea{Id=700,Damage=4,RemainingSeconds=.2f});
            Call("TickProjectiles",runtime,.2f);Call("TickProjectiles",runtime,.5f);
            runtime.WeaponBurnAreas.Add(new FirepowerLoop.Arcade.ArcadeWeaponBurnArea{Id=701,Damage=4,RemainingSeconds=2});
            Call("TickProjectiles",runtime,.05f);Assert.That(enemy.Health,Is.EqualTo(1000),"空窗后新火不能继承旧接触进度");
        }
        [Test] public void FuseFrameStillHitsBarrelBeforeEndpoint()
        {
            Set("arena",catalog.Arenas.First(a=>a.ArenaNumber==3));runtime.ArenaNumber=2;Ready("grenade");Fire();
            var p=runtime.Projectiles.Values.Single();p.Position=Vector2.zero;p.Velocity=Vector2.up*3;p.RemainingSeconds=.35f;p.EvolutionClusterChild=true;
            runtime.GroundBarrels[100]=new Vector2(0,.6f);runtime.GroundBarrelHealth[100]=1000;runtime.GroundBarrelMapId=catalog.RoundLayouts.First(a=>a.ArenaConfigNumber==3&&a.RoundNumber==1).Id;
            Call("TickProjectiles",runtime,1f);Assert.That(runtime.GroundBarrelHealth[100],Is.LessThan(1000),"到期帧有效路程中的桶仍须命中");
            runtime.GroundBarrels.Clear();runtime.GroundBarrelHealth.Clear();Fire();p=runtime.Projectiles.Values.Single();p.Position=Vector2.zero;p.Velocity=Vector2.up*3;p.RemainingSeconds=.35f;p.EvolutionClusterChild=true;
            runtime.Crates[1]=new FirepowerLoop.Arcade.ArcadeCrateState(new FirepowerLoop.Arcade.ArcadeBabylonParameters.Crate(1,0,.6f,.1f,1000));
            Call("TickProjectiles",runtime,1f);Assert.That(runtime.Crates[1].Health,Is.LessThan(1000),"引信内的箱体仍须命中");
        }
        [Test] public void BossExplosionCannotRecreateClearedChildren()
        {
            Ready("grenade");Fire();var parent=runtime.Projectiles.Values.Single();var boss=Enemy(501,parent.Position.x,parent.Position.y,1);boss.ContentBossKind=1;
            runtime.ThemeBossStarted=true;runtime.ContentBossEnemyId=boss.Id;
            runtime.WeaponBurnAreas.Add(new(){Id=700,Damage=4,RemainingSeconds=2});Call("DamageEnemiesInProjectileExplosion",runtime,parent);
            Assert.That(runtime.AwaitingExit,Is.True);Assert.That(runtime.Projectiles,Is.Empty);Assert.That(runtime.WeaponBurnAreas,Is.Empty);Assert.That(runtime.WeaponEvolution.Shots,Is.Zero);
        }
        [Test] public void GroundExpiringInsideLongFrameCannotLendContactToNextFire()
        {
            Ready("flamethrower");var enemy=Enemy(501,0,0);runtime.WeaponBurnAreas.Add(new(){Id=700,Damage=4,RemainingSeconds=.2f});
            Call("TickProjectiles",runtime,.3f);runtime.WeaponBurnAreas.Add(new(){Id=701,Damage=4,RemainingSeconds=2});Call("TickProjectiles",runtime,.05f);
            Assert.That(enemy.Health,Is.EqualTo(1000),"同一长帧末尾的无火空窗也要清除接触蓄积");
        }
        [Test] public void ExpiredSkullAndImmediateRepickResetSwarmPhase()
        {
            Ready("rapid");Fire();Fire();runtime.Projectiles.Clear();
            runtime.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds=.01f;
            Call("TickTimers",runtime,.02f);Pick("skull.large");Fire();
            Assert.That(runtime.Projectiles.Count,Is.EqualTo(1),"到期后重拾必须重新从首发蓄能");
        }
    }
}
