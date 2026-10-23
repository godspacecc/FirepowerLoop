using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>推进真实单人强化循环，证明补特效不更改诱饵、爆桶结算和冷却。</summary>
    public sealed class ArcadeSupportExplosionTests
    {
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;
        private ArcadeSlayerMartConfiguration config;
        private Recorder view;
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);Assert.That(provider.TryGetSlayerMart(out config,out error),Is.True,error);
            var architecture=new Game();architecture.RegisterModel(new ArcadeCombatModel());var runs=new ArcadeRunModel();architecture.RegisterModel(runs);
            runs.CommitSafeNode(new(){RunId="support-explosion-test",RemainingLives=3});runs.EnterArena();
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);
            runtime=new(){ArenaNumber=1,PlayerHealth=100,PlayerMaxHealth=100};architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            view=new Recorder();Set("catalog",catalog);Set("arena",catalog.Arenas[0]);Set("slayerMartConfig",config);Set("view",view);
        }
        private void Set(string field,object value)=>typeof(ArcadeCombatSystem).GetField(field,Flags).SetValue(system,value);
        private object Call(string method,params object[] args)=>typeof(ArcadeCombatSystem).GetMethod(method,Flags).Invoke(system,args);
        private ArcadePowerupRuntime Power(bool decoy)
        {var definition=catalog.Powerups.Single(p=>p.Id==(decoy?"arcade.powerup.decoy":"arcade.powerup.barrel"));var p=new ArcadePowerupRuntime{Definition=definition,RemainingSeconds=decoy?.01f:10,Charges=3,Position=new Vector2(3,2)};runtime.Powerups[definition.Behavior]=p;return p;}
        private ArcadeEnemyRuntime Enemy(Vector2 p){var e=new ArcadeEnemyRuntime{Id=99,Health=10000,MaxHealth=10000,Position=p};runtime.Enemies[99]=e;return e;}
        [Test] public void DecoyExpiresOnceAtItsActualLocation()
        {var p=Power(true);var e=Enemy(p.Position);Call("TickTimers",runtime,.02f);Assert.That(e.Health,Is.EqualTo(10000-p.Definition.Damage));Assert.That(view.Hits.Count,Is.EqualTo(1));Assert.That(view.Hits[0].p,Is.EqualTo(p.Position));Assert.That(view.Hits[0].r,Is.EqualTo(p.Definition.Radius));Call("TickTimers",runtime,.02f);Assert.That(view.Hits.Count,Is.EqualTo(1));}
        [Test] public void DecoyBeforeExpiryHasNoFeedback(){Power(true).RemainingSeconds=1;Call("TickTimers",runtime,.02f);Assert.That(view.Hits,Is.Empty);}
        [Test] public void HiddenDecoyExpiryHasNoFeedback(){Power(true);runtime.InTreasureRoom=true;Call("TickTimers",runtime,.02f);Assert.That(view.Hits,Is.Empty);}
        [Test] public void BarrelConsumesOneChargeAndKeepsCooldown()
        {var p=Power(false);var e=Enemy(runtime.PlayerPosition);Call("TickPowerups",runtime,.02f);Assert.That(p.Charges,Is.EqualTo(2));Assert.That(e.Health,Is.EqualTo(10000-p.Definition.Damage));Assert.That(view.Hits.Count,Is.EqualTo(1));Call("TickPowerups",runtime,.02f);Assert.That(p.Charges,Is.EqualTo(2));Assert.That(view.Hits.Count,Is.EqualTo(1));}
        [Test] public void BarrelWithoutTargetKeepsChargesAndStaysQuiet(){var p=Power(false);Call("TickPowerups",runtime,.02f);Assert.That(view.Hits,Is.Empty);Assert.That(p.Charges,Is.EqualTo(3));}
        [Test] public void EmptyDecoyExpiryStillShowsOneExplosion(){Power(true);Call("TickTimers",runtime,.02f);Assert.That(view.Hits.Count,Is.EqualTo(1));}
        private void Modern()
        {runtime.SlayerMartRulesVersion=1;runtime.SlayerMart=new(){RegionInstanceId="test:slayermart:1",Visit=1,Players=new(){new(){PlayerId=1}}};}
        [Test] public void OrdinaryOrbitUsesActualBarrelBody()
        {
            Modern();var drop=catalog.Drops.Single(p=>p.ProductId==config.Supports.Get(SlayerMartSupportKind.Barrel).ProductId);Assert.That(Call("ApplyDrop",runtime,drop),Is.True);
            var state=runtime.SlayerMart.Players[0];var item=ArcadeSlayerMartSupportRules.OrdinaryBody(state,SlayerMartSupportKind.Barrel,10);
            var body=ArcadeSlayerMartSupportRules.Bodies(state,item,0,0,config.Supports,config.Parameter)[0];Enemy(new Vector2(body.X,body.Y));
            Call("TickPowerups",runtime,0f);Assert.That(view.Hits.Count,Is.EqualTo(1));Assert.That(view.Hits[0].p,Is.EqualTo(new Vector2(body.X,body.Y)));Call("TickPowerups",runtime,0f);Assert.That(view.Hits.Count,Is.EqualTo(1));
        }
        [Test] public void PurchasedOrbitShowsOnlyOneRealTrigger()
        {
            Modern();var state=runtime.SlayerMart;state.PendingGrants.Add(new(){EventId="test:grant:1",PlayerId=1,Effect=SlayerMartEffect.SawBarrel,Amount=1,Duration=300});
            Assert.That(ArcadeSlayerMartSupportRules.TryApplyGrants(state,config,new Dictionary<long,int>(),out var next,out var error),Is.True,error);runtime.SlayerMart=next;
            var owner=next.Players[0];var item=owner.Supports.Single(p=>p.Kind==SlayerMartSupportKind.Barrel);var body=ArcadeSlayerMartSupportRules.Bodies(owner,item,0,0,config.Supports,config.Parameter)[0];Enemy(new Vector2(body.X,body.Y));
            Call("TickSlayerMartSupports",runtime,0f);Assert.That(view.Hits.Count,Is.EqualTo(1));Assert.That(view.Hits[0].p,Is.EqualTo(new Vector2(body.X,body.Y)));Call("TickSlayerMartSupports",runtime,0f);Assert.That(view.Hits.Count,Is.EqualTo(1));
        }
        private sealed class Recorder:IArcadeCombatViewPort
        {
            public readonly List<(Vector2 p,float r)> Hits=new();
            public void PlayProjectileExplosion(Vector2 p,float r)=>Hits.Add((p,r));
            public bool IsActive=>true;
            public void Begin(int n,float w,float h,IReadOnlyList<ArcadeObstacleDefinition> o){}
            public void End(){}
            public bool TryScreenToArena(Vector2 s,out Vector2 p){p=default;return false;}
            public void SetPlayer(Vector2 p,Vector2 f,bool v,bool i){}
            public void SetPlayerHealth(int c,int m){}
            public void SetEnemy(int id,string config,Vector2 p,int c,int m,bool telegraph=false,int attackSequence=0,bool persistentHealthBar=false){}
            public void RemoveEnemy(int id,bool playDeath=false){}
            public void SetProjectile(int id,bool hostile,Vector2 p,Vector2 d){}
            public void RemoveProjectile(int id){}
            public void SetDrop(int id,ArcadeDropDefinition d,Vector2 p){}
            public void RemoveDrop(int id){}
            public void SetRemotePlayer(long id,Vector2 p,Vector2 f,bool v,bool i,int c,int m){}
            public void RemoveRemotePlayer(long id){}
            public void TickPlayerFeedback(float s){}
        }
    }
}
