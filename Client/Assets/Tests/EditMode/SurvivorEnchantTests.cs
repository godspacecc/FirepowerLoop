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
    public sealed class SurvivorEnchantTests
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
            Set("layout",config.Layout);Set("obstacles",Array.Empty<ArcadeObstacleDefinition>());
            model.State=new SurvivorRunState(10,5);model.Active=true;model.Health=model.MaximumHealth=100;
        }
        [TearDown] public void Cleanup(){system.Stop();input.Dispose();}
        private void Prepare(){Assert.That(Call("PrepareEnemyRoster",new object[]{null}),Is.True);}
        private ArcadeEnemyRuntime Add(int id,string kind="shambler",float x=0,int hp=100,float speed=2)
        { var e=new ArcadeEnemyRuntime{Id=id,ConfigId="enemy.arcade.zombie."+kind,Health=hp,MaxHealth=hp,Speed=speed,Position=new Vector2(x,0)};Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(id,e);return e; }
        private void Tick(float dt)=>Call("AdvanceEnchantments",dt);
        [Test] public void FirstPulseWaitsFourSecondsAndExcludesSelf()
        {Prepare();var source=Add(1,"enchant.health");var target=Add(2);Tick(3.99f);Assert.That(target.MaxHealth,Is.EqualTo(100));Tick(.02f);Assert.That(target.MaxHealth,Is.EqualTo(120));Assert.That(source.MaxHealth,Is.EqualTo(100));}
        [Test] public void InjuredHealthAddsOnlyIncrementAndNeverHealsAtCap()
        {Prepare();Add(1,"enchant.health");var e=Add(2);e.Health=10;for(int i=0;i<5;i++)Tick(4);Assert.That(e.MaxHealth,Is.EqualTo(200));Assert.That(e.Health,Is.EqualTo(110));e.Health=1;Tick(4);Assert.That(e.Health,Is.EqualTo(1));}
        [Test] public void AllThreeKindsCoexistAndSpeedIsLinear()
        {Prepare();Add(1,"enchant.health");Add(2,"enchant.speed");Add(3,"enchant.shield");var e=Add(4);for(int i=0;i<6;i++)Tick(4);Assert.That(e.MaxHealth,Is.EqualTo(200));Assert.That(e.Speed,Is.EqualTo(3).Within(.001));Call("ApplyModifierDamage",e,1000,null);Assert.That(e.Health,Is.EqualTo(200));}
        [Test] public void MultipleSourcesCapInSameFrame()
        {Prepare();for(int i=1;i<=8;i++)Add(i,"enchant.health");var e=Add(20);Tick(4);Assert.That(e.MaxHealth,Is.EqualTo(200));}
        [Test] public void RadiusBoundaryGroundEntryAndOtherSupporters()
        {Prepare();Add(1,"enchant.health");var edge=Add(2,"enchant.speed",6);var outside=Add(3,x:6.01f);var ground=Add(4);ground.GroundEntry=new SurvivorGroundEntryState{Started=true,ForwardZ=1};Tick(4);Assert.That(edge.MaxHealth,Is.EqualTo(120));Assert.That(outside.MaxHealth,Is.EqualTo(100));Assert.That(ground.MaxHealth,Is.EqualTo(100));}
        [Test] public void DeadSourceStopsAndGrantedEffectSurvivesDistance()
        {Prepare();var source=Add(1,"enchant.health");var e=Add(2);Tick(4);source.Health=0;e.Position=Vector2.one*50;Tick(20);Assert.That(e.MaxHealth,Is.EqualTo(120));}
        [Test] public void EnchantedShieldRefillsWithoutRestoringBrokenNativeShield()
        {Prepare();Add(1,"enchant.shield");var e=Add(2,"shield",hp:200);Tick(4);Call("ApplyModifierDamage",e,int.MaxValue,null);Assert.That(e.Health,Is.EqualTo(200));Call("ApplyModifierDamage",e,int.MaxValue,null);Assert.That(e.Health,Is.EqualTo(200));Tick(4);Call("ProjectSpecialEnemy",e);Call("ApplyModifierDamage",e,999,null);Assert.That(e.Health,Is.EqualTo(200));Call("ApplyModifierDamage",e,10,null);Assert.That(e.Health,Is.EqualTo(190));}
        [Test] public void PauseFreezesPulseAndStopClearsForReusedIdentity()
        {Prepare();var app=new AppStateModel();app.CurrentState.Value=AppState.Playing;((IBelongToArchitecture)system).GetArchitecture().RegisterModel(app);Add(1,"enchant.health");var e=Add(2);Tick(3);model.Paused=true;Call("TickFrame",10f);Assert.That(e.MaxHealth,Is.EqualTo(100));Tick(1);Assert.That(e.MaxHealth,Is.EqualTo(120));system.Stop();Prepare();Add(1,"enchant.health");e=Add(2);Tick(3);Assert.That(e.MaxHealth,Is.EqualTo(100));Tick(1);Assert.That(e.MaxHealth,Is.EqualTo(120));}
        [Test] public void DeadTargetStateIsRemovedAndDoesNotLeakToReusedIdentity()
        {Prepare();Add(1,"enchant.health");var e=Add(2);Tick(4);Call("ApplyModifierDamage",e,10000,null);e=Add(2);Tick(4);Assert.That(e.MaxHealth,Is.EqualTo(120));}
        [Test] public void ExtremeHealthSaturatesWithoutOverflow()
        {Prepare();Add(1,"enchant.health");var e=Add(2,hp:int.MaxValue-10);Tick(4);Assert.That(e.MaxHealth,Is.EqualTo(int.MaxValue));Assert.That(e.Health,Is.EqualTo(int.MaxValue));}
        [Test] public void SpeedKeepsFrostTimeReduction()
        {Prepare();Add(1,"enchant.speed");var e=Add(2);Tick(4);var t=typeof(SurvivorSystem).GetNestedType("ModifierShot",BindingFlags.NonPublic);var shot=Activator.CreateInstance(t);t.GetField("FrostSeconds").SetValue(shot,3f);t.GetField("FrostSlowFraction").SetValue(shot,.5f);Call("ApplyElementHit",e,shot);Assert.That((float)Call("ElementMovementSeconds",e,1f),Is.LessThan(1));Assert.That(e.Speed,Is.EqualTo(2.2f).Within(.001));}
        [Test] public void EnchantedShieldCapsAndCanBeRefilledRepeatedly()
        {Prepare();Add(1,"enchant.shield");var e=Add(2);for(int i=0;i<6;i++)Tick(4);Assert.That(system.TryGetEnchantSnapshot(e.Id,out var snapshot),Is.True);Assert.That(snapshot.ShieldRemaining,Is.EqualTo(100));for(int i=0;i<9;i++){Call("ApplyModifierDamage",e,200,null);Tick(4);Assert.That(system.TryGetEnchantSnapshot(e.Id,out snapshot),Is.True);Assert.That(snapshot.ShieldRemaining,Is.EqualTo(20));}Assert.That(e.Health,Is.EqualTo(100));}
        [Test] public void GroundSourceDoesNotAccumulatePulseTime()
        {Prepare();var source=Add(1,"enchant.health");source.GroundEntry=new SurvivorGroundEntryState{Started=true,ForwardZ=1};var e=Add(2);Tick(10);source.GroundEntry=default;Tick(3);Assert.That(e.MaxHealth,Is.EqualTo(100));Tick(1);Assert.That(e.MaxHealth,Is.EqualTo(120));}
        [Test] public void BirthHealthForGoldRemainsUnchanged()
        {Prepare();Add(1,"enchant.health");var e=Add(2);Tick(20);Assert.That((int)Call("EnchantBirthHealth",e),Is.EqualTo(100));Assert.That(e.MaxHealth,Is.EqualTo(200));}
        [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)] [TestCase(-1f)] [TestCase(0f)]
        public void BadPulseConfigurationIsRejected(float interval)
        {Assert.Throws<ArgumentException>(()=>new SurvivorEnemyDefinition("a","b",0,120,new[]{100},1,1,1,Vector3.one,1,specialKind:"EnchantHealth",enchantInterval:interval,enchantRadius:6,enchantHealth:.2f,enchantSpeed:.1f,enchantStacks:5,enchantShieldGain:20,enchantShieldCap:100));}
        private sealed class View:IArcadeCombatViewPort
        {
            public bool Hidden=true,Visible=true;
            public bool ShieldIntact;
            public void SetSurvivorEnemyShield(int id,bool intact){ShieldIntact=intact;}
            public float SpawnRadius;public Vector3 Scale;
            public bool GroundStarted;
            public void SetEnemyGroundEntry(int id,SurvivorGroundEntryState state){GroundStarted=state.Started;}
            public bool IsArenaSpawnHidden(Vector2 p,float r){SpawnRadius=r;return Hidden;}
            public void SetEnemyScale(int id,Vector3 scale){Scale=scale;}
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

