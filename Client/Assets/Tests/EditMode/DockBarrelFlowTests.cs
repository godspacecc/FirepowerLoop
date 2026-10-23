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
            Set("catalog",catalog);Set("arena",catalog.Arenas.Single(a=>a.ArenaNumber==21));Set("random",new System.Random(17));
        }
        [TearDown] public void Cleanup()=>app.GetModel<ArcadeCombatModel>().Clear();
        private void Set(string key,object value)=>typeof(ArcadeCombatSystem).GetField(key,Hidden).SetValue(system,value);
        private object Call(string key,params object[] args)=>typeof(ArcadeCombatSystem).GetMethod(key,Hidden).Invoke(system,args);
        private int SpawnRound(int wave)
        {
            runtime.WaveIndex=wave;runtime.Enemies.Clear();runtime.SpawnQueue.Clear();Call("InitializeRoundContent",runtime);
            var arena=catalog.Arenas.Single(a=>a.ArenaNumber==21);var group=catalog.EnemyGroups.Single(g=>g.Id==arena.WaveGroupIds[wave]);Set("group",group);
            foreach(var member in group.Members)for(int i=0;i<member.Count;i++)runtime.SpawnQueue.Enqueue(member.EnemyId);
            int budget=runtime.SpawnQueue.Count;while(runtime.SpawnQueue.Count>0)Call("SpawnQueuedEnemy",runtime);
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
        [Test] public void NaturallySpawnedCarriersReleaseOnceAfterActiveLifetime()
        {
            SpawnRound(1);var carriers=runtime.Enemies.Values.Where(e=>e.ContentSpecialKind==1).ToArray();
            Assert.That(carriers.Length,Is.GreaterThan(0));int initial=runtime.GroundBarrels.Count;
            var layout=catalog.RoundLayouts.Single(p=>p.ArenaConfigNumber==21&&p.RoundNumber==2);
            runtime.ContentClock.Advance(layout.BarrelDropSeconds-.01f,false);Call("TickVortexBarrels",runtime,.01f);
            Assert.That(runtime.GroundBarrels.Count,Is.EqualTo(initial));
            runtime.ContentClock.Advance(.02f,false);Call("TickVortexBarrels",runtime,.02f);Call("TickVortexBarrels",runtime,.02f);
            Assert.That(runtime.GroundBarrels.Count,Is.EqualTo(initial+carriers.Length));Assert.That(carriers.All(e=>e.ContentSpecialKind==0),Is.True);
        }
        [Test] public void EarlyCarrierDeathPublishesPurpleExplosionInsteadOfDroppedBarrel()
        {
            SpawnRound(0);var carrier=runtime.Enemies.Values.FirstOrDefault(e=>e.ContentSpecialKind==1);Assert.That(carrier,Is.Not.Null);
            int count=runtime.GroundBarrels.Count;Call("OnRoundEnemyKilled",runtime,carrier,false);
            Assert.That(runtime.GroundBarrels.Count,Is.EqualTo(count));Assert.That(runtime.ContentAreas.Any(a=>a.Kind==14),Is.True);
            Assert.That(runtime.PlayerHealth,Is.EqualTo(100));
        }
    }
}
