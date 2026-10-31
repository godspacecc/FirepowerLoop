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
            Set("layout",config.Layout);Set("obstacles",Array.Empty<ArcadeObstacleDefinition>());
            model.State=new SurvivorRunState(10,5);model.Active=true;model.Health=model.MaximumHealth=100;
        }
        [TearDown] public void Cleanup(){system.Stop();input.Dispose();}
        private void Prepare(){Assert.That(Call("PrepareEnemyRoster",new object[]{null}),Is.True);}
        /// <summary>同一敌种在4分钟与60分钟出生时，生命、伤害和移速均不增长。</summary>
        [TestCase(0d,0d)] [TestCase(.7d,.35d)] [TestCase(.999d,.999d)]
        public void SpawnStatsStayFixedOverSurvivalTime(double sample,double lateSample)
        {
            Prepare();Set("enemyRandom",new SampleRandom(sample));model.State.Advance(240,false);Call("Spawn");
            var enemies=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");var early=enemies.Values.Single();
            // 后期抽取比例改变，使用同一敌种在对应概率区间中的样本比较属性。
            model.State.Advance(3360,false);Set("enemyRandom",new SampleRandom(lateSample));Call("Spawn");var late=enemies.Values.Single(e=>e.Id!=early.Id);
            Assert.That(late.ConfigId,Is.EqualTo(early.ConfigId));
            Assert.That(late.MaxHealth,Is.EqualTo(early.MaxHealth));
            Assert.That(late.Damage,Is.EqualTo(early.Damage));
            Assert.That(late.Speed,Is.EqualTo(early.Speed).Within(.00001f));
        }
        /// <summary>固定抽样使各个时间点选择同一敌种。</summary>
        private sealed class SampleRandom:System.Random
        {
            private readonly double sample;
            public SampleRandom(double sample){this.sample=sample;}
            public override double NextDouble()=>sample;
        }
        /// <summary>同一随机位置后期可选到进阶敌人，但不能改写已存活的普通丧尸。</summary>
        [Test] public void NewPhaseOnlyChangesNewSpawns()
        {
            Prepare();Set("enemyRandom",new SampleRandom(.35d));Call("Spawn");
            var enemies=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");var old=enemies.Values.Single();
            var before=(old.ConfigId,old.MaxHealth,old.Damage,old.Speed);
            Assert.That(old.ConfigId,Is.EqualTo("enemy.arcade.zombie.shambler"));
            model.State.Advance(600,false);Call("Spawn");
            var added=enemies.Values.Single(e=>e.Id!=old.Id);
            Assert.That(added.ConfigId,Is.EqualTo("enemy.arcade.zombie.gaunt"));
            Assert.That((old.ConfigId,old.MaxHealth,old.Damage,old.Speed),Is.EqualTo(before));
        }
        [Test] public void SpawnUsesUnlockedRosterAndPerModeStats()
        {
            Prepare();Set("enemyRandom",new FixedRandom());model.State.Advance(120,false);Call("Spawn");
            var e=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Values.Single();
            Assert.That(e.ConfigId,Is.EqualTo("enemy.arcade.zombie.gaunt"));Assert.That(e.MaxHealth,Is.EqualTo(90));
            Assert.That(e.Speed,Is.EqualTo(1.8f*1.7f).Within(.001));Assert.That(e.Damage,Is.EqualTo(12));
        }
        /// <summary>爬出不依赖隐藏出生点；已有敌人达到旧上限时仍可生成。</summary>
        [Test] public void GroundSpawnIgnoresHiddenLaneAndFormerAliveLimit()
        {
            Prepare();Set("view",new View{Hidden=false});Call("Spawn");
            var enemies=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");Assert.That(enemies.Count,Is.EqualTo(1));enemies.Clear();
            Set("view",new View());for(int i=0;i<config.MaxAliveEnemies;i++)enemies.Add(1000+i,new ArcadeEnemyRuntime());
            Call("Spawn");Assert.That(enemies.Count,Is.EqualTo(config.MaxAliveEnemies+1));
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
        /// <summary>三类丧尸在本模式均为1秒冷却，不修改闯关瘦削原有的2秒配置。</summary>
        [TestCase("enemy.arcade.zombie.shambler")]
        [TestCase("enemy.arcade.zombie.gaunt")]
        [TestCase("enemy.arcade.zombie.bloated")]
        public void ApprovedModeCooldownIsOneSecond(string id)
        {
            Prepare();var e=new ArcadeEnemyRuntime{Id=11,ConfigId=id,Position=new Vector2(.7f,0),Damage=10};
            Assert.That(provider.TryGetEnemy(id,out var shared,out var error),Is.True,error);
            Assert.That(shared.AttackCooldownSeconds,Is.EqualTo(id=="enemy.arcade.zombie.gaunt"?2f:1f),"闯关原值保持");
            Call("TickMeleeEnemy",e,.01f);Call("TickMeleeEnemy",e,.5f);
            Assert.That(e.AttackCooldown,Is.EqualTo(1f));
        }
        [Test] public void HeavySpawnUsesHighHealthSlowSpeedAndLargeBody()
        {
            Prepare();Set("enemyRandom",new FixedRandom());model.State.Advance(240,false);Call("Spawn");
            var e=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Values.Single();
            Assert.That(e.ConfigId,Is.EqualTo("enemy.arcade.zombie.bloated"));
            Assert.That(e.Damage,Is.EqualTo(15));Assert.That(e.MaxHealth,Is.EqualTo(300));Assert.That(e.Speed,Is.EqualTo(1.98f).Within(.001));
            Assert.That(Call("EnemyRadius",e),Is.EqualTo(Get<ArcadeConfigCatalogDefinition>("catalog").Rule.EnemyContactRadius*1.5f));
        }
        [Test] public void RealFrameProjectsHeavyScaleAndChecksFullSpawnBody()
        {
            Prepare();var app=new AppStateModel();app.CurrentState.Value=AppState.Playing;
            ((IBelongToArchitecture)system).GetArchitecture().RegisterModel(app);
            var catalog=Get<ArcadeConfigCatalogDefinition>("catalog");Set("weapon",catalog.Weapons.Single(x=>x.Id==config.WeaponId));
            Set("navigation",new SurvivorNavigation(40,40,catalog.Rule.EnemyContactRadius,Array.Empty<ArcadeObstacleDefinition>()));
            model.Loadout=new SurvivorLoadout(config.WeaponSlots,"Rifle");model.State.Advance(240,false);Set("enemyRandom",new FixedRandom());
            var port=new View{Visible=false};Set("view",port);Call("TickFrame",.01f);
            Assert.That(port.Scale,Is.EqualTo(new Vector3(1.5f,1.2f,1.4f)),"正式帧必须投影采用体型");
            Assert.That(port.GroundStarted,Is.True,"正式帧必须提交爬出状态");
            Assert.That(Call("EnemyRadius",Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Values.Single()),Is.EqualTo(catalog.Rule.EnemyContactRadius*1.5f));
        }
        [Test] public void ProjectileHitsEnlargedBodyBeyondOrdinaryRadius()
        {
            Prepare();float radius=Get<ArcadeConfigCatalogDefinition>("catalog").Rule.EnemyContactRadius;
            var enemy=new ArcadeEnemyRuntime{Id=100,ConfigId="enemy.arcade.zombie.bloated",Position=new Vector2(1,0),Health=100,MaxHealth=100};
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(enemy.Id,enemy);
            Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets").Add(101,new ArcadeProjectileRuntime{Id=101,Position=new Vector2(0,radius*1.2f),Velocity=new Vector2(40,0),Damage=10,RemainingSeconds=1});
            Call("AdvanceBullets",.05f);Assert.That(enemy.Health,Is.EqualTo(90));
        }
        [Test] public void LargeBodyCannotUseNarrowGapThatSmallBodyCanTraverse()
        {
            Prepare();var obstacles=new[]{new ArcadeObstacleDefinition(0,2.5f,1.5f,1.5f,true),new ArcadeObstacleDefinition(0,-2.5f,1.5f,1.5f,true)};
            float radius=Get<ArcadeConfigCatalogDefinition>("catalog").Rule.EnemyContactRadius;
            var small=new SurvivorNavigation(8,4,radius*.9f,obstacles);var large=new SurvivorNavigation(8,4,radius*1.5f,obstacles);
            var target=new Vector2(5,0);small.SetTarget(target);large.SetTarget(target);var a=new Vector2(-5,0);var b=a;
            for(int i=0;i<200;i++){a=small.Move(a,target,.1f);b=large.Move(b,target,.1f);}
            Assert.That(a.x,Is.GreaterThan(4));Assert.That(b.x,Is.LessThan(-1.5f));
        }
        private sealed class View:IArcadeCombatViewPort
        {
            public bool Hidden=true,Visible=true;
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
