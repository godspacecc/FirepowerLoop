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
    /// <summary>通过真实战斗入口验证墓穴出生及一次暗影重生，不以视觉动画代替规则。</summary>
    public sealed class NextStageEnemyTests
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        Game app;ArcadeCombatSystem system;ArcadeCombatRuntime runtime;ArcadeConfigCatalogDefinition catalog;
        [SetUp] public void Setup()
        {
            app=new Game();var run=new ArcadeRunModel();app.RegisterModel(run);app.RegisterModel(new ArcadeCombatModel());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);app.RegisterUtility<IRuntimeConfigProvider>(provider);
            run.CommitSafeNode(new ArcadeRunSaveData{RunId="next-enemy",RemainingLives=3,IsCasualRoute=true,CasualRouteVersion=1,ArenaConfigNumber=23,NextArenaNumber=4,CompletedArenaCount=3});run.EnterArena();
            runtime=new ArcadeCombatRuntime{ArenaNumber=4,PlayerHealth=100,PlayerMaxHealth=100,PlayerPosition=new Vector2(8,-8)};app.GetModel<ArcadeCombatModel>().Begin(runtime);
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(app);Set("catalog",catalog);Set("view",new ArcadeCombatViewController());Set("random",new System.Random(7));Stage(23);
        }
        void Set(string n,object v)=>typeof(ArcadeCombatSystem).GetField(n,Private).SetValue(system,v);
        object Call(string n,params object[] v)=>typeof(ArcadeCombatSystem).GetMethod(n,Private).Invoke(system,v);
        void Stage(int id){Set("arena",catalog.Arenas.Single(a=>a.ArenaNumber==id));Set("contentObstacleIdentity",null);runtime.ContentSpawnSequence=id==23?1:4;}
        ArcadeEnemyRuntime Spawn(int id=1)
        {
            var e=new ArcadeEnemyRuntime{Id=id,ConfigId="enemy.arcade.zombie.shambler",Health=100,MaxHealth=100,Damage=10,Speed=1,Position=new Vector2(0,24)};
            runtime.Enemies.Add(id,e);Call("AssignRoundEnemyContent",runtime,e);return e;
        }
        object State(ArcadeEnemyRuntime e)
        {
            var field=typeof(ArcadeEnemyRuntime).GetField("StageEnemy");Assert.That(field,Is.Not.Null,"缺少地图特色敌人权威阶段");return field.GetValue(e);
        }
        int Phase(ArcadeEnemyRuntime e)=>(int)State(e).GetType().GetField("Phase").GetValue(State(e));
        [Test] public void GraveEnemyUsesFixedTombOutsidePlayerFeet()
        {
            var e=Spawn();Assert.That(e.ContentSpecialKind,Is.EqualTo(18));
            Assert.That(new[]{-13f,-8f,8f,13f}.Any(x=>Math.Abs(e.Position.x-x)<.001f),Is.True,"仍从普通通道/空地出生");
            Assert.That(new[]{-13f,-8f,8f,13f}.Any(z=>Math.Abs(e.Position.y-(z-FirepowerLoop.Arcade.ArcadeNextStageEnemyRules.GraveLandingOffset))<.001f),Is.True);
            Assert.That(Vector2.Distance(runtime.PlayerPosition,e.Position),Is.GreaterThanOrEqualTo(4));Assert.That(Phase(e),Is.EqualTo(1));
        }
        [Test] public void EmergenceRejectsDamageAndMovementUntilOverGraveRim()
        {
            var e=Spawn();Assert.That(Call("RoundIncomingDamage",e,20),Is.EqualTo(0));var at=e.Position;
            Call("TickRoundEnemyContent",runtime,e,runtime.PlayerPosition,false,1f);Assert.That(e.Position,Is.EqualTo(at));Assert.That(runtime.PlayerHealth,Is.EqualTo(100));
            Call("TickRoundEnemyContent",runtime,e,runtime.PlayerPosition,false,1.2f);Assert.That(Phase(e),Is.Zero);Assert.That(Call("RoundIncomingDamage",e,20),Is.EqualTo(20));
        }
        [Test] public void ShadowCrowArrivalIsSafeBeforeChasing()
        {
            Stage(24);var e=Spawn();Assert.That(e.ContentSpecialKind,Is.EqualTo(19));Assert.That(Call("RoundIncomingDamage",e,20),Is.EqualTo(0));
            Call("TickRoundEnemyContent",runtime,e,runtime.PlayerPosition,false,1.3f);Assert.That(Phase(e),Is.Zero);Assert.That(Call("RoundIncomingDamage",e,20),Is.EqualTo(20));
        }
        ArcadeEnemyRuntime ReadyShadow()
        {Stage(24);var e=Spawn();Call("TickRoundEnemyContent",runtime,e,runtime.PlayerPosition,false,1.3f);return e;}
        [Test] public void FirstShadowDeathRetainsIdentityAndPaysNothing()
        {
            var e=ReadyShadow();var run=app.GetModel<ArcadeRunModel>();long before=run.Current.Score;e.Health=0;Call("KillEnemy",runtime,e,false);
            Assert.That(runtime.Enemies.ContainsKey(e.Id),Is.True,"第一次打倒被错误结算成死亡");Assert.That(e.Health,Is.EqualTo(e.MaxHealth));Assert.That(Phase(e),Is.EqualTo(3));
            Assert.That(run.Current.Score,Is.EqualTo(before));Assert.That(runtime.Drops,Is.Empty);Assert.That(runtime.AwaitingExit,Is.False);
        }
        [Test] public void WarpCannotBeKilledAgainBeforeSecondBodyAppears()
        {
            var e=ReadyShadow();e.Health=0;Call("KillEnemy",runtime,e,false);Assert.That(runtime.Enemies.ContainsKey(e.Id),Is.True);
            Assert.That(Call("RoundIncomingDamage",e,1000),Is.Zero);Call("KillEnemy",runtime,e,false);Assert.That(runtime.Enemies.ContainsKey(e.Id),Is.True);
            Call("TickRoundEnemyContent",runtime,e,runtime.PlayerPosition,false,1.4f);e.Health=0;Call("KillEnemy",runtime,e,false);Assert.That(runtime.Enemies,Is.Empty);
        }
        [Test] public void PausedRoundFreezesStageAndPoolResetClearsIt()
        {
            var e=Spawn();int phase=Phase(e);runtime.IslandTransitionPending=true;
            Call("TickRoundEnemyContent",runtime,e,runtime.PlayerPosition,false,20f);Assert.That(Phase(e),Is.EqualTo(phase));
            e.Reset();Assert.That(Phase(e),Is.Zero);Assert.That((int)State(e).GetType().GetField("RevivesRemaining").GetValue(State(e)),Is.Zero);
        }
        [Test] public void CrowdingCannotMoveHiddenStageEnemies()
        {
            var hidden=Spawn();var at=hidden.Position;
            runtime.Enemies.Add(2,new ArcadeEnemyRuntime{Id=2,Position=at,Health=10});
            Call("ResolveEnemyCrowding",runtime);Assert.That(hidden.Position,Is.EqualTo(at),"墓穴起身被追击敌人挤离墓位");
            runtime.Enemies.Clear();var shadow=ReadyShadow();shadow.Health=0;Call("KillEnemy",runtime,shadow,false);at=shadow.Position;
            runtime.Enemies.Add(2,new ArcadeEnemyRuntime{Id=2,Position=at,Health=10});
            Call("ResolveEnemyCrowding",runtime);Assert.That(shadow.Position,Is.EqualTo(at),"蓝影落点被拥挤分离移动");
        }
        [Test] public void PurpleShotgunCannotPushHiddenStageEnemies()
        {
            foreach(int phase in new[]{1,2,3})
            {
                runtime.Enemies.Clear();Stage(phase==1?23:24);
                var enemy=Spawn();
                if(phase==3){Call("TickRoundEnemyContent",runtime,enemy,runtime.PlayerPosition,false,1.3f);enemy.Health=0;Call("KillEnemy",runtime,enemy,false);}
                Assert.That(Phase(enemy),Is.EqualTo(phase));enemy.Position=new Vector2(0,-6);var at=enemy.Position;
                var shot=new ArcadeProjectileRuntime{FromPlayerWeapon=true,SourceWeaponId="arcade.weapon.spread",WeaponPowerLevel=2,
                    SourcePosition=new Vector2(-3,-6),Velocity=Vector2.right,EvolutionPushedEnemyIds=new System.Collections.Generic.HashSet<int>()};
                Call("PushPurpleShotgun",runtime,shot,at);
                Assert.That(enemy.Position,Is.EqualTo(at),"紫霰弹移动了隐藏阶段"+phase);
                Assert.That(shot.EvolutionPushedEnemyIds.Contains(enemy.Id),Is.False,"隐藏态不能消费本轮齐射推力身份");
                enemy.StageEnemy.Phase=0;Call("PushPurpleShotgun",runtime,shot,at);
                Assert.That(enemy.Position.x,Is.GreaterThan(at.x),"已活动敌人应保留既有紫霰弹击退");
            }
        }
    }
}
