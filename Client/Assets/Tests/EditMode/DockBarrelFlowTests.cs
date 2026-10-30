using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using FirepowerLoop.Arcade;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>读取正式码头配置并走真实出生入口，避免仅手工标记载桶身份掩盖零比例。</summary>
    public sealed class DockBarrelFlowTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private Game app;
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;
        [SetUp] public void Setup()
        {
            app=new Game();app.RegisterModel(new ArcadeRunModel());app.RegisterModel(new ArcadeCombatModel());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);app.RegisterUtility<IRuntimeConfigProvider>(provider);
            app.GetModel<ArcadeRunModel>().CommitSafeNode(new ArcadeRunSaveData{RunId="dock-barrel-test",IsCasualRoute=true,ArenaConfigNumber=21,NextArenaNumber=2,CompletedArenaCount=1});
            app.GetModel<ArcadeRunModel>().EnterArena();
            runtime=new ArcadeCombatRuntime{ArenaNumber=2,PlayerHealth=100,PlayerMaxHealth=100};app.GetModel<ArcadeCombatModel>().Begin(runtime);
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(app);
            Set("view",new ArcadeCombatViewController());Set("catalog",catalog);Set("arena",catalog.Arenas.Single(a=>a.ArenaNumber==21));Set("random",new System.Random(17));
        }
        [TearDown] public void Cleanup()=>app.GetModel<ArcadeCombatModel>().Clear();
        private void Set(string key,object value)=>typeof(ArcadeCombatSystem).GetField(key,Hidden).SetValue(system,value);
        private object Call(string key,params object[] args)=>typeof(ArcadeCombatSystem).GetMethod(key,Hidden).Invoke(system,args);
        private int SpawnRound(int wave)
        {
            runtime.WaveIndex=wave;runtime.Enemies.Clear();runtime.SpawnQueue.Clear();Call("InitializeRoundContent",runtime);
            var arena=catalog.Arenas.Single(a=>a.ArenaNumber==21);var group=catalog.EnemyGroups.Single(g=>g.Id==arena.WaveGroupIds[wave]);Set("group",group);
            foreach(var member in group.Members)for(int i=0;i<member.Count;i++)runtime.SpawnQueue.Enqueue(member.EnemyId);
            int budget=runtime.SpawnQueue.Count;while(runtime.SpawnQueue.Count>0)Call("SpawnQueuedEnemy",runtime,null);
            Assert.That(runtime.Enemies.Count,Is.EqualTo(budget),"载桶只能替换既有出生预算");return budget;
        }
        [Test] public void AllFourDockRoundsActuallySpawnCarriersWithinExistingBudget()
        {
            for(int wave=0;wave<4;wave++)
            {
                int budget=SpawnRound(wave),count=runtime.Enemies.Values.Count(e=>e.ContentSpecialKind==1);
                Assert.That(count*2,Is.EqualTo(budget),"码头第"+(wave+1)+"轮载桶敌人必须占实际出生预算的50%");
            }
        }
        [Test] public void NaturallySpawnedCarriersKeepBarrelsAfterActiveLifetime()
        {
            SpawnRound(1);var carriers=runtime.Enemies.Values.Where(e=>e.ContentSpecialKind==1).ToArray();
            Assert.That(carriers.Length,Is.GreaterThan(0));int initial=runtime.GroundBarrels.Count;
            var layout=catalog.RoundLayouts.Single(p=>p.ArenaConfigNumber==21&&p.RoundNumber==2);
            runtime.ContentClock.Advance(layout.BarrelDropSeconds-.01f,false);Call("TickVortexBarrels",runtime,.01f);
            Assert.That(runtime.GroundBarrels.Count,Is.EqualTo(initial));
            runtime.ContentClock.Advance(.02f,false);Call("TickVortexBarrels",runtime,.02f);Call("TickVortexBarrels",runtime,.02f);
            runtime.ContentClock.Advance(120,false);Call("TickVortexBarrels",runtime,.1f);
            Assert.That(runtime.GroundBarrels.Count,Is.EqualTo(initial));Assert.That(carriers.All(e=>e.ContentSpecialKind==1),Is.True);
        }
        [Test] public void EarlyCarrierDeathPublishesPurpleExplosionInsteadOfDroppedBarrel()
        {
            SpawnRound(0);var carrier=runtime.Enemies.Values.FirstOrDefault(e=>e.ContentSpecialKind==1);Assert.That(carrier,Is.Not.Null);
            int count=runtime.GroundBarrels.Count;Call("OnRoundEnemyKilled",runtime,carrier,false);
            Assert.That(runtime.GroundBarrels.Count,Is.EqualTo(count));Assert.That(runtime.ContentAreas.Any(a=>a.Kind==14),Is.True);
            Assert.That(runtime.PlayerHealth,Is.EqualTo(100));
        }
        /// <summary>真实子弹先扣背桶耐久，不能先扣载体生命。</summary>
        [Test] public void WeaponHitsBarrelBeforeCarrierBody()
        {
            var carrier=PrepareCarrier();FireAtCarrier(1);
            Assert.That(carrier.Health,Is.EqualTo(1000),"桶未破时不能先扣丧尸生命");
            Assert.That(carrier.ContentSpecialKind,Is.EqualTo(1));
            Assert.That(runtime.Projectiles,Is.Empty);
        }
        /// <summary>爆桶只结算一次既有范围伤害，后续子弹才命中本体。</summary>
        [Test] public void BreakingBarrelExplodesOnceThenBodyTakesLaterShots()
        {
            var carrier=PrepareCarrier();FireAtCarrier(10000);
            Assert.That(carrier.ContentSpecialKind,Is.Zero,"桶被打破后必须先移除载桶身份");
            Assert.That(carrier.Health,Is.GreaterThan(0).And.LessThan(1000),"本体只承受桶爆炸，不承受溢出弹伤");
            Assert.That(runtime.ContentAreas.Count(a=>a.Kind==14),Is.EqualTo(1));
            Assert.That(runtime.PlayerHealth,Is.EqualTo(100));
            int before=carrier.Health;FireAtCarrier(1);Assert.That(carrier.Health,Is.EqualTo(before-1));
            Assert.That(runtime.ContentAreas.Count(a=>a.Kind==14),Is.EqualTo(1));
        }
        /// <summary>背桶不再到时卸下，已承受的伤害不会随等待恢复。</summary>
        [Test] public void DamagedCarrierKeepsBarrelAndRemainingHealth()
        {
            var carrier=PrepareCarrier();FireAtCarrier(1);
            runtime.ContentClock.Advance(120,false);Call("TickVortexBarrels",runtime,.1f);
            Assert.That(carrier.ContentSpecialKind,Is.EqualTo(1));
            Assert.That(runtime.GroundBarrels,Is.Empty);
            var layout=catalog.RoundLayouts.Single(a=>a.ArenaConfigNumber==21&&a.RoundNumber==1);
            Assert.That(carrier.CarriedBarrelHealth,Is.EqualTo(layout.BarrelHealth-1));
        }
        /// <summary>海中不可攻击敌人不截停射向后方地面桶的子弹，也不受到伤害。</summary>
        [Test] public void SeaCarrierDoesNotTrapProjectileBeforeGroundBarrel()
        {
            var carrier=PrepareCarrier();
            carrier.DockSeaEntry=new ArcadeDockSeaEntryState{Started=true,Age=5f};
            runtime.GroundBarrels.Add(100,new Vector2(1.2f,0));
            var shot=new ArcadeProjectileRuntime{Id=900,Position=Vector2.zero,Velocity=Vector2.right*2,RemainingSeconds=3,Damage=36,FromPlayerWeapon=true,RemainingHits=1};
            runtime.Projectiles.Add(shot.Id,shot);Call("TickProjectiles",runtime,.5f);
            Assert.That(runtime.Projectiles,Is.Empty,"命中地面桶后必须消费弹丸，不能停在海中敌人上");
            Assert.That(runtime.GroundBarrelHealth[100],Is.EqualTo(4));
            Assert.That(carrier.Health,Is.EqualTo(1000));Assert.That(carrier.CarriedBarrelHealth,Is.Zero);
        }
        /// <summary>已登岸背桶与普通载体一致，基础枪两发打爆40耐久，弹丸立即消费。</summary>
        [Test] public void LandedSeaCarrierBarrelBreaksWithTwoBasicShots()
        {
            var carrier=PrepareCarrier();carrier.DockSeaEntry=new ArcadeDockSeaEntryState{Started=true,Age=ArcadeDockSeaEntryRules.ClimbSeconds};
            FireAtCarrier(36);Assert.That(carrier.CarriedBarrelHealth,Is.EqualTo(4));Assert.That(runtime.Projectiles,Is.Empty);
            FireAtCarrier(36);Assert.That(carrier.ContentSpecialKind,Is.Zero);Assert.That(runtime.Projectiles,Is.Empty);
            Assert.That(runtime.ContentAreas.Count(a=>a.Kind==14),Is.EqualTo(1));
        }
        private ArcadeEnemyRuntime PrepareCarrier()
        {
            SpawnRound(0);runtime.Enemies.Clear();runtime.GroundBarrels.Clear();runtime.GroundBarrelHealth.Clear();runtime.Projectiles.Clear();
            Set("contentObstacleIdentity",catalog.RoundLayouts.Single(a=>a.ArenaConfigNumber==21&&a.RoundNumber==1).Id);
            Set("contentRoundObstacles",Array.Empty<ArcadeObstacleDefinition>());
            var enemy=new ArcadeEnemyRuntime{Id=700,Position=Vector2.zero,Health=1000,MaxHealth=1000,ContentSpecialKind=1};runtime.Enemies.Add(enemy.Id,enemy);return enemy;
        }
        /// <summary>范围武器与紫射线也先命中桶，回池清除耐久镜像。</summary>
        [Test] public void ExplosionAndRayUseBarrelFirstAndResetClearsDurability()
        {
            var carrier=PrepareCarrier();
            Call("DamageEnemiesInExplosionRadius",runtime,Vector2.zero,2f,1);
            Call("HitPurpleRay",runtime,new ArcadeProjectileRuntime{Damage=1},carrier);
            var layout=catalog.RoundLayouts.Single(a=>a.ArenaConfigNumber==21&&a.RoundNumber==1);
            Assert.That(carrier.Health,Is.EqualTo(1000));Assert.That(carrier.CarriedBarrelHealth,Is.EqualTo(layout.BarrelHealth-2));
            carrier.Reset();Assert.That(carrier.CarriedBarrelHealth,Is.Zero);
        }
        private void FireAtCarrier(int damage)
        {
            var shot=new ArcadeProjectileRuntime{Id=900,Position=Vector2.left,Velocity=Vector2.right,RemainingSeconds=3,Damage=damage,FromPlayerWeapon=true,RemainingHits=1};
            runtime.Projectiles.Add(shot.Id,shot);Call("TickProjectiles",runtime,1f);
        }
    }
}
