using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>真实配置与真实幸存者战斗规则；替换显示端，不运行场景或访问正式存档。</summary>
    public sealed class SurvivorEnemyCombatTests
    {
        private SurvivorSystem system;
        private SurvivorModel model;
        private SurvivorConfiguration config;
        private IRuntimeConfigProvider provider;
        private PlayerInputCtrl input;
        private readonly BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private void Set(string n,object v)=>typeof(SurvivorSystem).GetField(n,flags).SetValue(system,v);
        private T Get<T>(string n)=>(T)typeof(SurvivorSystem).GetField(n,flags).GetValue(system);
        private object Call(string n,params object[] v)
        {var method=typeof(SurvivorSystem).GetMethod(n,flags);Assert.That(method,Is.Not.Null,n);return method.Invoke(system,v);}
        [SetUp] public void Setup()
        {
            var architecture=new Game();model=new SurvivorModel();architecture.RegisterModel(model);
            provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            Assert.That(provider.TryGetSurvivor(out config,out var error),Is.True,error);
            Assert.That(provider.TryGetArcadeCatalog(out var catalog,out error),Is.True,error);
            system=new SurvivorSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);input=new PlayerInputCtrl();
            Set("input",input);Set("view",new View());Set("config",config);Set("catalog",catalog);
            Set("layout",catalog.RoundLayouts.Single(x=>x.Id==config.LayoutId));Set("obstacles",Array.Empty<ArcadeObstacleDefinition>());
            model.State=new SurvivorRunState(10,5);model.Active=true;model.Health=model.MaximumHealth=100;
        }
        [TearDown] public void Cleanup(){system.Stop();input.Dispose();}
        private void Prepare(){Assert.That(Call("PrepareEnemyRoster",new object[]{null}),Is.True);}
        [Test] public void SpawnUsesUnlockedRosterAndPerModeStats()
        {
            Prepare();Set("enemyRandom",new FixedRandom());model.State.Advance(120,false);Call("Spawn");
            var e=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Values.Single();
            Assert.That(e.ConfigId,Is.EqualTo("enemy.arcade.zombie.gaunt"));Assert.That(e.MaxHealth,Is.EqualTo(74));
            Assert.That(e.Speed,Is.EqualTo(1.8f*1.7f*1.04f).Within(.001));
        }
        [Test] public void HiddenSpawnFailureAndAliveBudgetDoNotCreateEnemies()
        {
            Prepare();Set("view",new View{Hidden=false});Call("Spawn");
            var enemies=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");Assert.That(enemies,Is.Empty);
            Set("view",new View());for(int i=0;i<config.MaxAliveEnemies;i++)enemies.Add(1000+i,new ArcadeEnemyRuntime());
            Call("Spawn");Assert.That(enemies.Count,Is.EqualTo(config.MaxAliveEnemies));
        }
        [Test] public void ZombieGrabDamagesAfterWindupAndCancelsOutOfRange()
        {
            Prepare();var e=new ArcadeEnemyRuntime{Id=11,ConfigId="enemy.arcade.zombie.shambler",Position=new Vector2(.7f,0),Damage=10};
            Call("TickMeleeEnemy",e,.1f);Assert.That(model.Health,Is.EqualTo(100));Assert.That(e.MeleeWindupRemaining,Is.GreaterThan(0));
            e.Position=new Vector2(3,0);Call("TickMeleeEnemy",e,1f);Assert.That(model.Health,Is.EqualTo(100));Assert.That(e.MeleeWindupActive,Is.False);
            e.Position=new Vector2(.7f,0);Call("TickMeleeEnemy",e,.1f);Call("TickMeleeEnemy",e,.5f);Assert.That(model.Health,Is.EqualTo(90));
            Assert.That(e.MeleeRecoveryRemaining,Is.GreaterThan(0));
        }
        [Test] public void ApproachingExactMeleeBoundaryStillStartsAndHits()
        {
            Prepare();Set("player",new Vector2(4,0));
            var navigation=new SurvivorNavigation(40,40,.3f,Array.Empty<ArcadeObstacleDefinition>());navigation.SetTarget(new Vector2(4,0));Set("navigation",navigation);
            var e=new ArcadeEnemyRuntime{Id=11,ConfigId="enemy.arcade.zombie.shambler",Position=new Vector2(4.9f,0),Damage=10,Speed=2.2f};
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(e.Id,e);
            Call("TickEnemies",.05f);Assert.That(e.MeleeWindupActive,Is.True,"浮点边界不能让敌人永久站住");
            for(int i=0;i<10;i++)Call("TickEnemies",.05f);
            Assert.That(model.Health,Is.EqualTo(90));
        }
        private sealed class FixedRandom:System.Random {public override double NextDouble()=>.999;}
        private sealed class View:IArcadeCombatViewPort
        {
            public bool Hidden=true,Visible=true;
            public bool IsArenaSpawnHidden(Vector2 p,float r)=>Hidden;
            public bool IsArenaTargetVisible(Vector2 p)=>Visible;
            public bool IsActive=>true;
            public bool IsRoundLayoutReady=>true;
            public void Begin(int n,float w,float h,IReadOnlyList<ArcadeObstacleDefinition> o){}
            public void End(){}
            public bool TryScreenToArena(Vector2 s,out Vector2 p){p=default;return false;}
            public void SetPlayer(Vector2 p,Vector2 f,bool v,bool i){}
            public void SetPlayerHealth(int c,int m){}
            public void SetEnemy(int id,string c,Vector2 p,int hp,int max,bool t=false,int seq=0,bool bar=false){}
            public void RemoveEnemy(int id,bool death=false){}
            public void SetProjectile(int id,bool h,Vector2 p,Vector2 d){}
            public void RemoveProjectile(int id){}
            public void SetDrop(int id,ArcadeDropDefinition d,Vector2 p){}
            public void RemoveDrop(int id){}
            public void SetRemotePlayer(long id,Vector2 p,Vector2 f,bool v,bool i,int c,int m){}
            public void RemoveRemotePlayer(long id){}
            public void TickPlayerFeedback(float d){}
        }
    }
}
