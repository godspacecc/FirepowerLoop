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
    public sealed class SurvivorSpecialEnemyTests
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
        [Test] public void SpecialRosterContainsSixIndependentEnemies()
        {
            Prepare();Assert.That(Get<SurvivorEnemyRoster>("enemyRoster").Entries.Count,Is.EqualTo(6));
        }
        [Test] public void ExploderWarnsThenSelfDestructsWithoutRewards()
        {
            Prepare();var e=new ArcadeEnemyRuntime{Id=11,ConfigId="enemy.arcade.zombie.exploder",Position=new Vector2(1,0),Health=60,Damage=25};
            var all=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");all.Add(e.Id,e);
            Call("TickSpecialEnemy",e,.01f);Assert.That(model.Health,Is.EqualTo(100));
            Call("TickSpecialEnemy",e,.5f);Assert.That(all.ContainsKey(11),Is.True);
            Call("TickSpecialEnemy",e,.51f);Call("FlushSpecialDeaths");Assert.That(model.Health,Is.EqualTo(75));Assert.That(all.Count,Is.Zero);Assert.That(model.Kills,Is.Zero);
        }
        [Test] public void KilledWarningEnemyCannotExplode()
        {
            Prepare();var e=new ArcadeEnemyRuntime{Id=11,ConfigId="enemy.arcade.zombie.exploder",Position=new Vector2(1,0),Health=60,Damage=25};
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(e.Id,e);Call("TickSpecialEnemy",e,.01f);
            e.Health=0;Call("TickSpecialEnemy",e,2f);Assert.That(model.Health,Is.EqualTo(100));
        }
        [TestCase(0f,1f,0)] [TestCase(1f,0f,0)] [TestCase(0f,-1f,0)]
        public void ShieldUsesIncomingSource(float x,float y,int damage)
        {
            Prepare();var e=new ArcadeEnemyRuntime{Id=11,ConfigId="enemy.arcade.zombie.shield",Position=Vector2.zero,Health=200};
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(e.Id,e);Call("ApplyModifierDamage",e,100,(Vector2?)new Vector2(x,y));Assert.That(e.Health,Is.EqualTo(200-damage));
        }
        [Test] public void ShieldAbsorbsDirectionlessBurn()
        {
            Prepare();var e=new ArcadeEnemyRuntime{Id=11,ConfigId="enemy.arcade.zombie.shield",Position=Vector2.zero,Health=200};
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(e.Id,e);Call("ApplyModifierDamage",e,100,null);Assert.That(e.Health,Is.EqualTo(200));
        }
        [Test] public void CasterWindupFiresAtCapturedPosition()
        {
            Prepare();Set("player",new Vector2(0,5));var e=new ArcadeEnemyRuntime{Id=11,ConfigId="enemy.arcade.zombie.caster",Health=80};
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(e.Id,e);Call("TickSpecialEnemy",e,.01f);
            Set("player",new Vector2(5,0));Call("TickSpecialEnemy",e,.81f);
            var shots=Get<Dictionary<int,ArcadeProjectileRuntime>>("hostileProjectiles");Assert.That(shots.Count,Is.EqualTo(1));Assert.That(shots.Values.Single().Velocity.x,Is.EqualTo(0).Within(.001));
        }
        [TestCase(false,85)] [TestCase(true,100)]
        public void HostileSweepCannotTunnelThroughPlayerOrWall(bool wall,int health)
        {
            Prepare();if(wall)Set("obstacles",new[]{new ArcadeObstacleDefinition(0,1,.5f,.1f,true)});
            Get<Dictionary<int,ArcadeProjectileRuntime>>("hostileProjectiles").Add(25,new ArcadeProjectileRuntime{Id=25,Position=new Vector2(0,3),Velocity=new Vector2(0,-100),Damage=15,RemainingSeconds=1});
            Call("AdvanceHostileProjectiles",.05f);Assert.That(model.Health,Is.EqualTo(health));Assert.That(Get<Dictionary<int,ArcadeProjectileRuntime>>("hostileProjectiles").Count,Is.Zero);
        }
        [TestCase("Rifle")] [TestCase("Drone")]
        public void RealProjectilePathRespectsShield(string owner)
        {
            Prepare();var e=new ArcadeEnemyRuntime{Id=900,ConfigId="enemy.arcade.zombie.shield",Health=1000,Position=Vector2.zero};
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(e.Id,e);
            var level=config.WeaponLevel(owner,1);Call("Shoot",new Vector2(0,1),Vector2.down,level,owner);Call("AdvanceBullets",.2f);
            Assert.That(e.Health,Is.EqualTo(1000));
        }
        [Test] public void BlastUsesExplosionOriginForShieldDirection()
        {
            Prepare();var e=new ArcadeEnemyRuntime{Id=900,ConfigId="enemy.arcade.zombie.shield",Health=1000,Position=Vector2.zero};
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(e.Id,e);Call("RebuildHordeSpatial");
            var type=typeof(SurvivorSystem).GetNestedType("ModifierShot",BindingFlags.NonPublic);var shot=Activator.CreateInstance(type);
            type.GetField("BlastRadius").SetValue(shot,3f);type.GetField("BlastDamage").SetValue(shot,100);
            Call("TriggerModifierBlast",Vector2.up,0,shot);Assert.That(e.Health,Is.EqualTo(1000));
        }
        [Test] public void BreakingShieldAbsorbsWholeHitThenHealthTakesNextHit()
        {
            Prepare();var e=new ArcadeEnemyRuntime{Id=11,ConfigId="enemy.arcade.zombie.shield",Health=200};
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(e.Id,e);
            Call("ApplyModifierDamage",e,1,(Vector2?)Vector2.up);Assert.That(e.Health,Is.EqualTo(200));
            Call("ApplyModifierDamage",e,150,(Vector2?)Vector2.down);Assert.That(e.Health,Is.EqualTo(200));
            Call("ApplyModifierDamage",e,25,null);Assert.That(e.Health,Is.EqualTo(175));
        }
        [Test] public void PausedFrameAndStopFreezeThenClearSpecialState()
        {
            Prepare();var app=new AppStateModel();app.CurrentState.Value=AppState.Playing;((IBelongToArchitecture)system).GetArchitecture().RegisterModel(app);
            var e=new ArcadeEnemyRuntime{Id=11,ConfigId="enemy.arcade.zombie.exploder",Health=60,Position=Vector2.up};
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(e.Id,e);Call("TickSpecialEnemy",e,.01f);
            var shots=Get<Dictionary<int,ArcadeProjectileRuntime>>("hostileProjectiles");shots.Add(100,new ArcadeProjectileRuntime{Id=100,Position=Vector2.up*2,Velocity=Vector2.down,RemainingSeconds=2,Damage=15});
            model.Paused=true;Call("TickFrame",5f);Assert.That(shots[100].RemainingSeconds,Is.EqualTo(2));Assert.That(model.Health,Is.EqualTo(100));
            system.Stop();Assert.That(shots,Is.Empty);Assert.That(((System.Collections.IDictionary)typeof(SurvivorSystem).GetField("specialEnemies",flags).GetValue(system)).Count,Is.Zero);
        }
        [Test] public void GroundEntryCannotStartSpecialAttack()
        {
            Prepare();Set("navigation",new SurvivorNavigation(40,40,.3f,Array.Empty<ArcadeObstacleDefinition>()));
            var e=new ArcadeEnemyRuntime{Id=11,ConfigId="enemy.arcade.zombie.exploder",Health=60,GroundEntry=new SurvivorGroundEntryState{Started=true,ForwardZ=1}};
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(e.Id,e);Call("TickEnemies",.05f);
            Assert.That(((System.Collections.IDictionary)typeof(SurvivorSystem).GetField("specialEnemies",flags).GetValue(system)).Count,Is.Zero);
        }
        [Test] public void AllSixPhaseWeightsMatchApprovedMix()
        {
            Prepare();var roster=Get<SurvivorEnemyRoster>("enemyRoster");
            int[][] expected={new[]{100,0,0,0,0,0},new[]{60,40,0,0,0,0},new[]{45,30,15,5,0,5},new[]{30,30,20,7,6,7},new[]{20,30,25,8,8,9},new[]{10,30,30,10,10,10}};
            for(int phase=0;phase<6;phase++)
            {
                var counts=new int[6];for(int i=0;i<1000;i++){var choice=roster.Select(phase*120,(i+.5f)/1000);counts[roster.Entries.ToList().IndexOf(choice)]++;}
                Assert.That(counts,Is.EqualTo(expected[phase].Select(x=>x*10).ToArray()));
            }
        }
        /// <summary>完整投影不补盾；退出后新实体从配置恢复耐久。</summary>
        [Test] public void ShieldProjectionDoesNotRestoreDamageAndStopResetsShield()
        {
            Prepare();var e=new ArcadeEnemyRuntime{Id=901,ConfigId="enemy.arcade.zombie.shield",Health=200};
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(e.Id,e);
            Call("ApplyModifierDamage",e,100,null);Call("ProjectSpecialEnemy",e);Call("ProjectSpecialEnemy",e);
            Assert.That(((View)Get<IArcadeCombatViewPort>("view")).ShieldIntact,Is.False);
            system.Stop();Prepare();Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(e.Id,e);Call("ProjectSpecialEnemy",e);
            Assert.That(((View)Get<IArcadeCombatViewPort>("view")).ShieldIntact,Is.True);
            Call("ApplyModifierDamage",e,99,null);Call("ApplyModifierDamage",e,1,null);Assert.That(e.Health,Is.EqualTo(200));
            Call("ApplyModifierDamage",e,10,null);Assert.That(e.Health,Is.EqualTo(190));
        }
        /// <summary>盾牌生命保留150，实际配置行走速度与普通一致。</summary>
        [Test] public void ShieldKeepsHealthAndMatchesOrdinaryMovement()
        {
            Prepare();var roster=Get<SurvivorEnemyRoster>("enemyRoster");var shield=roster.Entries.Single(e=>e.SpecialKind=="Shield");
            var normal=roster.Entries.Single(e=>e.Id=="shambler");var definitions=Get<Dictionary<string,EnemyConfigDefinition>>("enemyConfigs");
            Assert.That(definitions[shield.EnemyId].MaxHP*shield.HealthMultiplier,Is.EqualTo(150));
            Assert.That(definitions[shield.EnemyId].MoveSpeed*shield.SpeedMultiplier,Is.EqualTo(definitions[normal.EnemyId].MoveSpeed*normal.SpeedMultiplier));
        }
        /// <summary>真实灼烧与火区都必须先扣盾，不能走生命旁路。</summary>
        [TestCase(false)] [TestCase(true)]
        public void ShieldAbsorbsActualContinuousDamage(bool molotov)
        {
            Prepare();var e=new ArcadeEnemyRuntime{Id=901,ConfigId="enemy.arcade.zombie.shield",Health=200};
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(e.Id,e);Call("RebuildHordeSpatial");
            if(molotov)
            {
                var t=typeof(SurvivorSystem).GetNestedType("MolotovZone",BindingFlags.NonPublic);var zone=Activator.CreateInstance(t);
                t.GetField("Level").SetValue(zone,config.WeaponLevel("Molotov",1));Call("AdvanceMolotovZone",zone,.25f);
            }
            else
            {
                var t=typeof(SurvivorSystem).GetNestedType("ModifierShot",BindingFlags.NonPublic);var shot=Activator.CreateInstance(t);
                t.GetField("FireSeconds").SetValue(shot,3f);t.GetField("FireDamagePerSecond").SetValue(shot,7d);
                Call("ApplyElementHit",e,shot);Call("AdvanceElementDamage",1f);
            }
            Assert.That(e.Health,Is.EqualTo(200));var state=Call("SpecialState",e);
            Assert.That((int)state.GetType().GetField("ShieldRemaining").GetValue(state),Is.LessThan(100).And.GreaterThan(0));
        }
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
