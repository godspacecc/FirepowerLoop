using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>通过实际单人刷怪入口验证通道交叠；不创建正式场景或访问玩家存档。</summary>
    public sealed class PassageOverlapTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private Game app;
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;
        [SetUp] public void Setup()
        {
            app=new Game();app.RegisterModel(new ArcadeRunModel());app.RegisterModel(new ArcadeCombatModel());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);app.RegisterUtility<IRuntimeConfigProvider>(provider);
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(app);Set("catalog",catalog);
            Stage(1,0);
        }
        private void Set(string name,object value)=>typeof(ArcadeCombatSystem).GetField(name,Flags).SetValue(system,value);
        private object Call(string name,params object[] args)=>typeof(ArcadeCombatSystem).GetMethod(name,Flags).Invoke(system,args);
        private void Stage(int map,int wave)
        {
            var run=app.GetModel<ArcadeRunModel>();run.CommitSafeNode(new ArcadeRunSaveData{RunId="passage-overlap",RemainingLives=3,
                IsCasualRoute=true,ArenaConfigNumber=map,NextArenaNumber=1,Checkpoint=new ArcadeCheckpointSaveData{ArenaNumber=1}});run.EnterArena();
            runtime=new ArcadeCombatRuntime{ArenaNumber=1,PlayerHealth=100,PlayerMaxHealth=100};app.GetModel<ArcadeCombatModel>().Begin(runtime);
            Set("arena",catalog.Arenas.Single(a=>a.ArenaNumber==map));Set("random",new System.Random(37));Call("BeginWave",runtime,wave);
            Call("InitializeGroundBarrels",runtime);
            runtime.IslandWaveBudget=runtime.SpawnQueue.Count;
        }
        private int[] Step()
        {
            runtime.Enemies.Clear();Call("TickSpawning",runtime,1f);
            var layout=(FirepowerLoop.Arcade.ArcadeRoundLayoutParameters)Call("RoundLayout",runtime);
            return runtime.Enemies.Values.Select(e=>Enumerable.Range(0,layout.Entries.Count).OrderBy(i=>layout.ArenaConfigNumber==1
                ? -Vector2.Dot(e.Position,new Vector2(layout.Entries[i].X,layout.Entries[i].Y).normalized)
                : (e.Position-new Vector2(layout.Entries[i].X,layout.Entries[i].Y)).sqrMagnitude).First()).ToArray();
        }
        [Test] public void DocksEachPassageAddsOneStationaryBarrelPerRound()
        {
            for(int wave=0;wave<4;wave++)
            {
                Stage(21,wave);var seen=new HashSet<int>();var positions=new Dictionary<int,Vector2>();
                int budget=runtime.SpawnQueue.Count;
                for(int tick=0;tick<budget+3;tick++)
                {
                    seen.UnionWith(Step());
                    Assert.That(runtime.GroundBarrels.Count,Is.EqualTo(1+seen.Count),"每个通道本批首只出敌才补一桶");
                    foreach(var pair in positions)Assert.That(runtime.GroundBarrels[pair.Key],Is.EqualTo(pair.Value),"桶生成后必须静止");
                    positions=new Dictionary<int,Vector2>(runtime.GroundBarrels);
                }
                Assert.That(seen.Count,Is.EqualTo(3));
                foreach(var pair in positions)
                    foreach(var other in positions.Where(p=>p.Key!=pair.Key))
                        Assert.That(Vector2.Distance(pair.Value,other.Value),Is.GreaterThan(1f),"补桶不能重叠");
            }
        }
        [Test] public void DocksWaitingDoesNotAddBarrelAndDestroyedBarrelDoesNotRespawn()
        {
            Stage(21,0);runtime.InTreasureRoom=true;Call("TickSpawning",runtime,1f);
            Assert.That(runtime.GroundBarrels.Count,Is.EqualTo(1));Assert.That(runtime.Enemies,Is.Empty);
            runtime.InTreasureRoom=false;Step();Assert.That(runtime.GroundBarrels.Count,Is.EqualTo(2));
            int id=runtime.GroundBarrels.Keys.Max();runtime.GroundBarrels.Remove(id);
            Step();Assert.That(runtime.GroundBarrels.ContainsKey(id),Is.False,"后续出怪不能复活已打掉的桶");
        }
        [Test] public void OtherMapsDoNotGainDocksPassageBarrels()
        {
            foreach(int map in new[]{1,22,2,20})
            {
                Stage(map,0);int initial=runtime.GroundBarrels.Count;Step();
                Assert.That(runtime.GroundBarrels.Count,Is.EqualTo(initial));
            }
        }
        [Test] public void HalfBatchStartsNextPassageAndOldPassageContinues()
        {
            int half=((110+2)/3+1)/2;var seen=new HashSet<int>();int first=-1;
            for(int i=0;i<half-1;i++){var born=Step();first=born[0];seen.UnionWith(born);Assert.That(seen.Count,Is.EqualTo(1));}
            seen.UnionWith(Step());Assert.That(seen.Count,Is.EqualTo(2),"前通道生成一半时下一通道必须开始");
            var next=Step();Assert.That(next,Does.Contain(first));Assert.That(next.Distinct().Count(),Is.GreaterThanOrEqualTo(2),"旧通道必须继续和新通道同时刷");
        }
        [Test] public void EveryConfiguredMapAndRoundUsesOverlappingPassages()
        {
            foreach(var layout in catalog.RoundLayouts)
            {
                Stage(layout.ArenaConfigNumber,layout.RoundNumber-1);int total=runtime.SpawnQueue.Count;
                int half=((total+layout.Entries.Count-1)/layout.Entries.Count+1)/2;var seen=new HashSet<int>();
                for(int i=0;i<half;i++){Step();seen.Add(runtime.IslandPassages.Current);}
                Assert.That(seen.Count,Is.GreaterThanOrEqualTo(2),layout.Id);
            }
        }
        [Test] public void SharedAliveLimitKeepsQueueAndResumesWithoutBurst()
        {
            for(int i=0;i<100;i++)Call("TickSpawning",runtime,1f);
            Assert.That(runtime.Enemies.Count,Is.EqualTo(24));Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(86));
            int first=runtime.Enemies.Keys.First();runtime.Enemies.Remove(first);Call("TickSpawning",runtime,1f);
            Assert.That(runtime.Enemies.Count,Is.EqualTo(24));Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(85));
        }
        [Test] public void CompletedScheduleCannotLoseOrDuplicateRoundBudget()
        {
            int born=0;for(int i=0;i<115;i++)born+=Step().Length;
            Assert.That(born,Is.EqualTo(110));Assert.That(runtime.SpawnQueue,Is.Empty);
        }
    }
}
