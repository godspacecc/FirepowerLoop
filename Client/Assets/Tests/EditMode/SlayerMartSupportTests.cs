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
    /// <summary>购买实例进入正式单人战斗后的发射、接触、容量与死亡；不操作用户存档。</summary>
    public sealed class SlayerMartSupportTests
    {
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeSlayerMartConfiguration config;
        private ArcadeConfigCatalogDefinition catalog;
        private ArcadeSlayerMartPlayer Player=>runtime.SlayerMart.Players[0];
        [SetUp] public void Setup()
        {
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);Assert.That(provider.TryGetSlayerMart(out config,out error),Is.True,error);
            var architecture=new Game();architecture.RegisterModel(new ArcadeCombatModel());var runs=new ArcadeRunModel();architecture.RegisterModel(runs);
            runs.CommitSafeNode(new(){RunId=Guid.NewGuid().ToString("N"),RemainingLives=3});runs.EnterArena();
            var runSystem=new ArcadeRunSystem();runSystem.ConfigureForTests(Path.Combine(Path.GetTempPath(),"SlayerSupport-"+Guid.NewGuid().ToString("N")));architecture.RegisterSystem(runSystem);
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);
            Set("catalog",catalog);Set("arena",catalog.Arenas[1]);Set("random",new System.Random(37));Set("slayerMartConfig",config);
            runtime=new(){SlayerMartRulesVersion=1,SlayerMart=new(){RegionInstanceId="run:slayermart:1",Visit=1,Players=new(){new(){PlayerId=1}}},PlayerHealth=100,PlayerMaxHealth=100};
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
        }
        [Test] public void ThreeChickensEmitSeparateSourceProjectiles()
        {Grant(SlayerMartEffect.ChickenPack,3,300);Enemy(1,5,0);Step(0);Assert.That(runtime.Projectiles.Count,Is.EqualTo(3));Assert.That(runtime.Projectiles.Values.Select(p=>p.SupportInstanceId).Distinct().Count(),Is.EqualTo(3));}
        [Test] public void ChickenCooldownDoesNotMergeIntoOneClock()
        {Grant(SlayerMartEffect.ChickenPack,3,300);Enemy(1,5,0);Step(0);Step(.1f);Assert.That(runtime.Projectiles.Count,Is.EqualTo(3));Assert.That(Player.Supports.All(p=>p.CooldownSeconds>0),Is.True);}
        [Test] public void ProjectileCapDoesNotConsumeUnfiredChickens()
        {Grant(SlayerMartEffect.ChickenPack,3,300);Enemy(1,5,0);for(int i=0;i<catalog.Rule.MaxProjectiles-1;i++)runtime.Projectiles[100000+i]=new(){Id=100000+i};Step(0);Assert.That(runtime.Projectiles.Count,Is.EqualTo(catalog.Rule.MaxProjectiles));Assert.That(Player.Supports.Count(p=>p.CooldownSeconds>0),Is.EqualTo(1));}
        [Test] public void ShortOrdinaryChickenDoesNotReplacePurchasedInstances()
        {Grant(SlayerMartEffect.ChickenPack,3,300);Assert.That(Call("ApplyDrop",runtime,ChickenDrop()),Is.True);Assert.That(Player.Supports.All(p=>p.RemainingSeconds==300),Is.True);Assert.That(Player.Supports.Count,Is.EqualTo(3));}
        [Test] public void SixthOrdinaryChickenIsRefused()
        {for(int k=0;k<5;k++)Grant(SlayerMartEffect.Chicken,1,300);Assert.That(Call("ApplyDrop",runtime,ChickenDrop()),Is.False);Assert.That(runtime.Powerups.ContainsKey(ArcadePowerupBehavior.Chicken),Is.False);}
        [Test] public void TeslaContactsLeaveEmptyRingCenterSafe()
        {Grant(SlayerMartEffect.TeslaPack,3);var near=AtBody(1);var center=Enemy(2,0,0);Step(0);Assert.That(near.Health,Is.EqualTo(1000-config.Supports.Get(SlayerMartSupportKind.Orb).Damage));Assert.That(center.Health,Is.EqualTo(1000));}
        [Test] public void ThreeTeslaBodiesShareActualTargetCooldown()
        {Grant(SlayerMartEffect.TeslaPack,3);foreach(var i in Player.Supports)i.Phase=0;var e=AtBody(1);Step(0);Assert.That(e.Health,Is.EqualTo(1000-config.Supports.Get(SlayerMartSupportKind.Orb).Damage));}
        [Test] public void TeslaCannotPushBoss()
        {Grant(SlayerMartEffect.TeslaPack,3);var e=AtBody(1);e.ContentBossKind=1;e.Speed=3;var position=e.Position;Step(0);Assert.That(e.Health,Is.LessThan(1000));Assert.That(e.Position,Is.EqualTo(position));}
        [Test] public void RelayStartsNextShareWithoutRefillingPrevious()
        {Grant(SlayerMartEffect.BarrelRelay,3);var first=Player.Supports[0];var e=AtBody(1,first);for(int k=0;k<3;k++){float dt=k==0?0:.45f;MoveToUpcoming(e,first,dt);Step(dt);}Step(0);Assert.That(first.Charges,Is.Zero);Assert.That(Player.Supports[0].InstanceId,Is.Not.EqualTo(first.InstanceId));Assert.That(Player.Supports[0].Charges,Is.EqualTo(3));Assert.That(Player.Queues[0].Waiting.Count,Is.EqualTo(1));}
        [Test] public void MapTransitionFreezesPurchasedSupportClock()
        {Grant(SlayerMartEffect.ChickenPack,3,300);runtime.IslandTransitionPending=true;Step(5);Assert.That(Player.Supports.All(p=>p.RemainingSeconds==300),Is.True);}
        [Test] public void RealDeathClearsPurchasedAndOrdinarySupports()
        {Grant(SlayerMartEffect.BarrelRelay,3);Player.VestSlots=2;Player.Owned.Add("slayermart.item.16");Player.StormCharge=99;foreach(var kind in new[]{ArcadePowerupBehavior.Chicken,ArcadePowerupBehavior.Saw,ArcadePowerupBehavior.Barrel})runtime.Powerups[kind]=new(){RemainingSeconds=10};runtime.PlayerHealth=10;Call("DamagePlayerCore",runtime,20,SlayerMartHitKind.Ordinary);Assert.That(Player.Supports,Is.Empty);Assert.That(Player.Queues,Is.Empty);Assert.That(runtime.Powerups,Is.Empty);Assert.That(Player.VestSlots,Is.EqualTo(2));Assert.That(Player.StormCharge,Is.EqualTo(99));}
        [Test] public void ExpiryImmediatelyReleasesChickenCapacity()
        {Grant(SlayerMartEffect.ChickenPack,3,300);Step(301);Assert.That(Player.Supports,Is.Empty);Assert.That(Player.Chickens,Is.Zero);}
        private void Grant(SlayerMartEffect effect,int amount,float duration=0)
        {var s=runtime.SlayerMart;s.PendingGrants.Add(new(){EventId=s.RegionInstanceId+":"+(++s.GrantSequence),PlayerId=1,Effect=effect,Amount=amount,Duration=duration});Assert.That(ArcadeSlayerMartSupportRules.TryApplyGrants(s,config,new Dictionary<long,int>(),out var next,out var error),Is.True,error);runtime.SlayerMart=next;}
        private ArcadeDropDefinition ChickenDrop()=>catalog.Drops.Single(d=>d.ProductId==config.Supports.Get(SlayerMartSupportKind.Chicken).ProductId);
        private ArcadeEnemyRuntime Enemy(int id,float x,float y){var e=new ArcadeEnemyRuntime{Id=id,Position=new Vector2(x,y),Health=1000,MaxHealth=1000};runtime.Enemies.Add(id,e);return e;}
        private ArcadeEnemyRuntime AtBody(int id,ArcadeSlayerMartEffectInstance i=null)
        {i??=Player.Supports[0];var p=ArcadeSlayerMartSupportRules.Bodies(Player,i,0,0,config.Supports,config.Parameter)[0];return Enemy(id,p.X,p.Y);}
        private void MoveToUpcoming(ArcadeEnemyRuntime e,ArcadeSlayerMartEffectInstance i,float seconds)
        {double phase=i.Phase;i.Phase+=config.Parameter("support.orbitSpeed")*seconds;var p=ArcadeSlayerMartSupportRules.Bodies(Player,i,0,0,config.Supports,config.Parameter)[0];i.Phase=phase;e.Position=new Vector2(p.X,p.Y);}
        private void Step(float seconds)=>Call("TickSlayerMartSupports",runtime,seconds);
        private void Set(string field,object value)=>typeof(ArcadeCombatSystem).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(system,value);
        private object Call(string method,params object[] args)
        {var m=typeof(ArcadeCombatSystem).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic);Assert.That(m,Is.Not.Null,"支援战斗接点未实现："+method);return m.Invoke(system,args);}
    }
}
