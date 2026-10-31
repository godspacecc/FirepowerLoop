using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using FirepowerLoop.Arcade;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Game.Tests
{
    /// <summary>场内爬出规则及真实三体型动作；限定检查不启动自然对局。</summary>
    public sealed class SurvivorGroundEntryTests
    {
        private Game architecture;private SurvivorSystem system;private SurvivorModel model;private SurvivorConfiguration config;
        private PlayerInputCtrl input;private View view;private ArcadeConfigCatalogDefinition catalog;
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private void Set(string key,object value)=>typeof(SurvivorSystem).GetField(key,Flags).SetValue(system,value);
        private T Get<T>(string key)=>(T)typeof(SurvivorSystem).GetField(key,Flags).GetValue(system);
        private object Call(string key,params object[] args)=>typeof(SurvivorSystem).GetMethod(key,Flags).Invoke(system,args);
        private Dictionary<int,ArcadeEnemyRuntime> Enemies=>Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");
        private object Ground(ArcadeEnemyRuntime e)
        {var f=typeof(ArcadeEnemyRuntime).GetField("GroundEntry");Assert.That(f,Is.Not.Null,"缺少场内爬出状态");return f.GetValue(e);}
        private float Age(ArcadeEnemyRuntime e)=>(float)Ground(e).GetType().GetField("Age").GetValue(Ground(e));
        private void SetAge(ArcadeEnemyRuntime e,float age)
        {var g=Ground(e);g.GetType().GetField("Age").SetValue(g,age);typeof(ArcadeEnemyRuntime).GetField("GroundEntry").SetValue(e,g);}
        [SetUp] public void Setup()
        {
            UIHelper.ResetForTests();architecture=new Game();model=new SurvivorModel();architecture.RegisterModel(model);architecture.RegisterModel(new MapSceneModel());
            var app=new AppStateModel();architecture.RegisterModel(app);app.CurrentState.Value=AppState.Playing;
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);Assert.That(provider.TryGetSurvivor(out config,out var error),Is.True,error);
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out error),Is.True,error);
            system=new SurvivorSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);input=new PlayerInputCtrl();view=new View();
            var layout=config.Layout;
            Set("config",config);Set("catalog",catalog);Set("layout",layout);Set("view",view);Set("input",input);
            Set("weapon",catalog.Weapons.Single(x=>x.Id==config.WeaponId));
            Set("obstacles",layout.Obstacles.Select(x=>new ArcadeObstacleDefinition(x.X,x.Y,x.HalfWidth,x.HalfHeight,x.BlocksProjectiles)).ToArray());
            Set("player",new Vector2(layout.SpawnX,layout.SpawnY));
            model.State=new SurvivorRunState(10,5);model.Loadout=new SurvivorLoadout(config.WeaponSlots,"Rifle");model.Active=true;model.Health=model.MaximumHealth=100;
        }
        [TearDown] public void Cleanup(){system.Stop();input.Dispose();}
        [Test] public void VisibleSpawnUsesReachableFarmFloorAndSafeDistance()
        {
            for(int i=0;i<80;i++)Call("Spawn");Assert.That(Enemies.Count,Is.EqualTo(80));
            foreach(var e in Enemies.Values){Assert.That(e.OutsideEntry.Active,Is.False);Assert.That(Age(e),Is.Zero);Assert.That(Vector2.Distance(e.Position,Get<Vector2>("player")),Is.GreaterThanOrEqualTo(config.MinimumSpawnDistance));Assert.That(Mathf.Abs(e.Position.x),Is.LessThan(30.5f));Assert.That(Mathf.Abs(e.Position.y),Is.LessThan(30.5f));}
            Assert.That(view.HiddenQueries,Is.Zero,"钻地不能再依赖场外隐藏通道");
        }
        [Test] public void PausedFrameFreezesCrawlAgeAndPosition()
        {Call("Spawn");var e=Enemies.Values.Single();var p=e.Position;system.SetPaused(true);Call("TickFrame",.05f);Assert.That(Age(e),Is.Zero);Assert.That(e.Position,Is.EqualTo(p));}
        [Test] public void CrawlPreventsMeleeUntilStandingAndProjectsAuthorityAge()
        {
            Call("Spawn");var e=Enemies.Values.Single();Set("player",e.Position);Set("spawnClock",100f);Set("fireClock",100f);
            Call("TickFrame",.05f);Assert.That(model.Health,Is.EqualTo(100));Assert.That(e.MeleeWindupActive,Is.False);Assert.That(Age(e),Is.EqualTo(.05f).Within(.0001));Assert.That(view.GroundWrites,Is.GreaterThan(0));
        }
        [Test] public void UndergroundBodyDoesNotBlockBulletOrGiveEarlyExperience()
        {
            Call("Spawn");var e=Enemies.Values.Single();var start=e.Position;
            Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets").Add(1000,new ArcadeProjectileRuntime{Id=1000,Position=start-Vector2.right,Velocity=Vector2.right*40,Damage=10000,RemainingSeconds=2});
            Call("AdvanceBullets",.05f);Assert.That(Enemies.Count,Is.EqualTo(1));Assert.That(Get<Dictionary<int,(Vector2 Position,long Experience)>>("drops"),Is.Empty);
        }
        [Test] public void StandingKillLeavesExperienceAtReachableFloor()
        {
            Call("Spawn");var e=Enemies.Values.Single();SetAge(e,SurvivorGroundEntryRules.TotalSeconds);var p=e.Position;
            Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets").Add(1000,new ArcadeProjectileRuntime{Id=1000,Position=p-Vector2.right,Velocity=Vector2.right*40,Damage=10000,RemainingSeconds=2});
            Call("AdvanceBullets",.05f);Assert.That(Enemies,Is.Empty);Assert.That(model.Kills,Is.EqualTo(1));Assert.That(Get<Dictionary<int,(Vector2 Position,long Experience)>>("drops").Values.Single().Position,Is.EqualTo(p));
        }
        /// <summary>登场不是计时免伤；真实露出身体可在站稳之前被射中。</summary>
        [Test] public void ExposedClimbingBodyCanBeHitBeforeStanding()
        {
            Call("Spawn");var e=Enemies.Values.Single();SetAge(e,2.5f);var p=e.Position;
            var forward=new Vector2(e.GroundEntry.ForwardX,e.GroundEntry.ForwardZ);
            bool hit=(bool)Call("TryGroundBodySweep",e,p-forward*3,p+forward*3,.75f,0f);
            Assert.That(hit,Is.True,"露出身体时不能因为登场阶段而免伤");
        }
        /// <summary>爬出前后通路必须属于玩家同一连通区。</summary>
        [Test] public void NavigationRejectsWalkableButDisconnectedSpawnFloor()
        {
            var nav=new SurvivorNavigation(8,8,.5f,new[]{new ArcadeObstacleDefinition(0,0,.5f,9,true)});nav.SetTarget(new Vector2(-4,0));
            Assert.That(nav.IsReachable(new Vector2(4,0)),Is.False);Assert.That(nav.IsReachable(new Vector2(-4,0)),Is.True);
        }
        [Test] public void DeathKeepsPoseAndMapSoilOutlivesActor()
        {
            var map=new GameObject("GroundTestMap");var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"),map.transform);
            var effects=new EnemyGroundEntryPresentation(map.transform);
            try {
                var v=actor.GetComponent<EnemyView>();v.BeginPresentationUse();v.SetNavigationEnabled(false);v.Animator.Rebind();v.Animator.Update(0);
                var state=new SurvivorGroundEntryState{Started=true,Age=1.85f,ForwardZ=1};v.SetGroundEntry(state,Vector3.forward,Vector3.up);v.LateTickCombatFeedback();effects.Sync(1,v,state,Vector3.forward,Vector3.up);
                var soil=map.transform.Find("GroundEntrySoil");Assert.That(soil,Is.Not.Null);Assert.That(soil.IsChildOf(actor.transform),Is.False);
                var visual=v.Animator.transform.parent;var position=visual.localPosition;var rotation=visual.localRotation;
                Assert.That(v.TryPlayPhysicalDeath(Vector3.left,false,out _),Is.True);effects.Interrupt(1);
                Assert.That(Vector3.Distance(position,visual.localPosition),Is.LessThan(.001f));Assert.That(Quaternion.Angle(rotation,visual.localRotation),Is.LessThan(.01f));
                var clod=soil.GetChild(2);var before=clod.position;effects.Tick(.1f,true);Assert.That(clod.position,Is.EqualTo(before));
                v.ReleasePresentationResources();Object.DestroyImmediate(actor);Assert.That(soil!=null&&soil.gameObject.activeSelf,Is.True);
                effects.Tick(.1f,false);Assert.That(clod.position,Is.Not.EqualTo(before));effects.Tick(8,false);Assert.That(soil==null,Is.True);
            } finally {effects.Dispose();Object.DestroyImmediate(map);}
        }
        [Test] public void MapCleanupReleasesSoilAndDuplicateProjectionDoesNotReplay()
        {
            var map=new GameObject("GroundTestMap");var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"),map.transform);
            var effects=new EnemyGroundEntryPresentation(map.transform);
            try {var view=actor.GetComponent<EnemyView>();var state=new SurvivorGroundEntryState{Started=true,Age=2.2f,ForwardZ=1};
                effects.Sync(1,view,state,Vector3.forward,Vector3.up);var soil=map.transform.Find("GroundEntrySoil");var origin=soil.position;
                actor.transform.position=Vector3.one*5;effects.Sync(1,view,state,Vector3.forward,Vector3.up);Assert.That(soil.position,Is.EqualTo(origin));Assert.That(map.transform.childCount,Is.EqualTo(2));
                var mesh=soil.GetComponentInChildren<MeshFilter>().sharedMesh;var material=soil.GetComponentInChildren<Renderer>().sharedMaterial;
                effects.Dispose();Assert.That(soil==null&&mesh==null&&material==null,Is.True);
            } finally {effects.Dispose();Object.DestroyImmediate(map);}
        }
        [TestCase("Shambler")][TestCase("Gaunt")][TestCase("Bloated")]
        public void FormalActorSamplesChangingArmsAndKneesAndResets(string kind)
        {
            var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombie"+kind+".prefab"));
            try{
                var v=actor.GetComponent<EnemyView>();v.BeginPresentationUse();v.SetNavigationEnabled(false);v.Animator.Rebind();v.Animator.Update(0);
                var method=typeof(EnemyView).GetMethod("SetGroundEntry");Assert.That(method,Is.Not.Null,"正式View尚未接入爬出动作");
                var type=method.GetParameters()[0].ParameterType;object State(float age){var value=Activator.CreateInstance(type);type.GetField("Started").SetValue(value,true);type.GetField("Age").SetValue(value,age);type.GetField("ForwardZ").SetValue(value,1f);return value;}
                var visual=v.Animator.transform.parent;var rest=visual.localPosition;var arm=actor.GetComponentsInChildren<Transform>().Single(t=>t.name=="UpperArm.L");var leg=actor.GetComponentsInChildren<Transform>().Single(t=>t.name=="LowerLeg.L");
                method.Invoke(v,new[]{State(1.25f),Vector3.forward,Vector3.up});v.LateTickCombatFeedback();var a=arm.localRotation;var b=leg.localRotation;
                method.Invoke(v,new[]{State(2.45f),Vector3.forward,Vector3.up});v.LateTickCombatFeedback();Assert.That(Quaternion.Angle(a,arm.localRotation),Is.GreaterThan(8));Assert.That(Quaternion.Angle(b,leg.localRotation),Is.GreaterThan(8));Assert.That(v.Animator.enabled,Is.False);
                method.Invoke(v,new[]{State(SurvivorGroundEntryRules.TotalSeconds),Vector3.forward,Vector3.up});v.LateTickCombatFeedback();Assert.That(v.Animator.enabled,Is.True);Assert.That(Vector3.Distance(rest,visual.localPosition),Is.LessThan(.001f));
                v.ResetPhysicalFeedback();v.BeginPresentationUse();method.Invoke(v,new[]{State(1.25f),Vector3.forward,Vector3.up});v.LateTickCombatFeedback();Assert.That(v.Animator.enabled,Is.False,"回池后应允许下一只重新爬出");
            }finally{Object.DestroyImmediate(actor);}
        }
        /// <summary>后来挂入的普通网格不属于身体，受击不应染色。</summary>
        [Test] public void HitFeedbackDoesNotTintAttachedScenery()
        {
            var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"));
            try { var prop=GameObject.CreatePrimitive(PrimitiveType.Cube);prop.transform.SetParent(actor.transform,false);
                var renderer=prop.GetComponent<Renderer>();var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",Color.green);renderer.SetPropertyBlock(block);
                actor.GetComponent<EnemyView>().PlayHitFeedback();renderer.GetPropertyBlock(block);
                Assert.That(block.GetColor("_BaseColor"),Is.EqualTo(Color.green));
            } finally {Object.DestroyImmediate(actor);}
        }
        /// <summary>正式时间轴与已确认的五点二秒分段爬出保持一致。</summary>
        [Test] public void HeavyCrawlUsesApprovedTiming()
        {Assert.That(SurvivorGroundEntryRules.TotalSeconds,Is.EqualTo(5.2f));Assert.That(SurvivorGroundEntryRules.ClipAge(1.6f),Is.EqualTo(2.9f).Within(.001));}
        private sealed class View:IArcadeCombatViewPort
        {
            public int HiddenQueries,GroundWrites;
            public bool IsArenaSpawnHidden(Vector2 p,float radius){HiddenQueries++;return false;}
            public void SetEnemyGroundEntry(int id,SurvivorGroundEntryState state){GroundWrites++;}
            public bool IsActive=>true;public bool IsRoundLayoutReady=>true;
            public void Begin(int n,float w,float h,IReadOnlyList<ArcadeObstacleDefinition> o){}public void End(){}
            public bool TryScreenToArena(Vector2 s,out Vector2 p){p=default;return false;}
            public void SetPlayer(Vector2 p,Vector2 f,bool v,bool i){}public void SetPlayerHealth(int c,int m){}
            public void SetEnemy(int id,string c,Vector2 p,int hp,int max,bool t=false,int seq=0,bool bar=false){}
            public void RemoveEnemy(int id,bool death=false){}public void SetProjectile(int id,bool h,Vector2 p,Vector2 d){}public void RemoveProjectile(int id){}
            public void SetDrop(int id,ArcadeDropDefinition d,Vector2 p){}public void RemoveDrop(int id){}public void SetRemotePlayer(long id,Vector2 p,Vector2 f,bool v,bool i,int c,int m){}public void RemoveRemotePlayer(long id){}public void TickPlayerFeedback(float d){}
        }
    }
}
