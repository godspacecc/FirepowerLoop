using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Reflection;using FirepowerLoop.Arcade;using Luban;using NUnit.Framework;using QFramework;using UnityEngine;
namespace Game.Tests {
/// <summary>验证规则向正式表现端口选择的追击、锁定和盾牌方向。</summary>
public sealed class SurvivorSpecialFacingTests {
SurvivorSystem system;PlayerInputCtrl input;View view;readonly BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
void Set(string n,object v)=>typeof(SurvivorSystem).GetField(n,flags).SetValue(system,v);
T Get<T>(string n)=>(T)typeof(SurvivorSystem).GetField(n,flags).GetValue(system);
object Call(string n,params object[] args)=>typeof(SurvivorSystem).GetMethod(n,flags).Invoke(system,args);
[SetUp]public void Setup(){var a=new Game();a.RegisterModel(new SurvivorModel());var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));a.RegisterUtility<IRuntimeConfigProvider>(provider);system=new SurvivorSystem();((ICanSetArchitecture)system).SetArchitecture(a);input=new PlayerInputCtrl();view=new View();Set("input",input);Set("view",view);Assert.That(Call("PrepareEnemyRoster",new object[]{null}),Is.True);provider.TryGetSurvivor(out var config,out _);Set("config",config);}
[TearDown]public void Cleanup(){system.Stop();input.Dispose();}
ArcadeEnemyRuntime Enemy(string kind){var e=new ArcadeEnemyRuntime{Id=9,ConfigId="enemy.arcade.zombie."+kind,Position=Vector2.zero,Health=150};Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(9,e);return e;}
[TestCase("exploder")][TestCase("caster")]public void LateralChaseProjectsCurrentTarget(string kind){var e=Enemy(kind);Set("player",Vector2.right*20);Call("TickSpecialEnemy",e,.1f);Call("ProjectSpecialEnemy",e);Assert.That(Vector2.Dot(view.Direction,Vector2.right),Is.GreaterThan(.999f));}
[Test]public void CasterLocksWindupThenFacesNewTargetDuringCooldown(){var e=Enemy("caster");e.Position=Vector2.up*3;Set("player",Vector2.zero);Call("TickSpecialEnemy",e,.01f);Set("player",Vector2.right*20);Call("ProjectSpecialEnemy",e);Assert.That(Vector2.Dot(view.Direction,Vector2.down),Is.GreaterThan(.999f));Call("TickSpecialEnemy",e,.81f);Call("ProjectSpecialEnemy",e);Assert.That(Vector2.Dot(view.Direction,(Vector2.right*20-e.Position).normalized),Is.GreaterThan(.999f));Assert.That(Vector2.Dot(Get<Dictionary<int,ArcadeProjectileRuntime>>("hostileProjectiles").Values.Single().Velocity.normalized,Vector2.down),Is.GreaterThan(.999f));}
[Test]public void ShieldProjectionKeepsAuthorityTurnLimit(){var e=Enemy("shield");Set("player",Vector2.right*20);Call("TickSpecialEnemy",e,.1f);Call("ProjectSpecialEnemy",e);Assert.That(Vector2.Angle(Vector2.up,view.Direction),Is.EqualTo(12f).Within(.01f));Assert.That(Vector2.Angle(Vector2.right,view.Direction),Is.GreaterThan(70));}
[Test]public void CoincidentTargetKeepsSafeFacing(){var e=Enemy("caster");Set("player",Vector2.zero);Call("ProjectSpecialEnemy",e);Assert.That(view.Direction,Is.EqualTo(Vector2.up));}
        private sealed class View:IArcadeCombatViewPort
        {
            public Vector2 Direction;
            public void SetSurvivorEnemySpecial(int id,string kind,Vector2 direction,bool warning,float progress,Vector2 target,float radius){Direction=direction;}
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
}}
