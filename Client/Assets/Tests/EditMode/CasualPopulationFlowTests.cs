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
    /// <summary>实际普通轮准备的总量、组选择与特殊阶段隔离，使用临时本局。</summary>
    public sealed class CasualPopulationFlowTests
    {
        private Game architecture;private ArcadeCombatSystem system;private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;private string root;
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            root=Path.Combine(Path.GetTempPath(),"FirepowerPopulation-"+Guid.NewGuid().ToString("N"));
            architecture=new Game();architecture.RegisterModel(new ArcadeRunModel());architecture.RegisterModel(new ArcadeCombatModel());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);Set("catalog",catalog);Set("random",new System.Random(17));
        }
        [TearDown] public void Cleanup(){architecture.GetModel<ArcadeCombatModel>().Clear();if(Directory.Exists(root))Directory.Delete(root,true);}
        private void Set(string name,object value)=>typeof(ArcadeCombatSystem).GetField(name,Flags).SetValue(system,value);
        private ArcadeEnemyGroupDefinition Group=>(ArcadeEnemyGroupDefinition)typeof(ArcadeCombatSystem).GetField("group",Flags).GetValue(system);
        private void Prepare(int map,int ordinal,int wave,bool casual=true,bool challenge=false,bool final=false)
        {
            var run=new ArcadeRunSaveData{RunId=Guid.NewGuid().ToString("N"),IsCasualRoute=casual,ArenaConfigNumber=map,CompletedArenaCount=ordinal-1,NextArenaNumber=ordinal,UpdatedAtUtc=DateTime.UtcNow.ToString("O")};
            run.Checkpoint.ArenaNumber=ordinal;run.EquipmentBySlot["weapon"]=ArcadeRunDefaults.BasicWeaponId;
            Assert.That(new ArcadeRunFileStore(root).Save(run,out var error),Is.True,error);architecture.GetModel<ArcadeRunModel>().CommitSafeNode(run);architecture.GetModel<ArcadeRunModel>().EnterArena();
            runtime=new ArcadeCombatRuntime{ArenaNumber=ordinal,PlayerHealth=100,PlayerMaxHealth=100,IsChallenge=challenge,IsFinalBoss=final};architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            Set("arena",catalog.Arenas.First(a=>a.ArenaNumber==map));if(challenge)Set("challenge",catalog.Challenges.First());
            typeof(ArcadeCombatSystem).GetMethod("BeginWave",Flags).Invoke(system,new object[]{runtime,wave});
        }
        [Test] public void LaterOrdinaryRoundUsesCumulativeRoundNine(){Prepare(15,3,0);Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(190));}
        [Test] public void DifferentThemesAtSameOrdinalUseSamePopulation(){Prepare(4,3,0);int first=runtime.SpawnQueue.Count;Prepare(15,3,0);Assert.That(first,Is.EqualTo(190));Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(first));}
        [Test] public void RoundThirtyReachesFourHundredCap(){Prepare(19,8,1);Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(400));}
        [Test] public void RoundFortyDoesNotExceedPopulationCap(){Prepare(20,10,3);Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(400));}
        [Test] public void OldBossPlaceholderUsesNearestEarlierOrdinaryGroup(){Prepare(4,3,3);Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(220));Assert.That(Group.Id,Is.EqualTo("arcade.group.ranged.1"));Assert.That(Group.MaxAlive,Is.EqualTo(9));Assert.That(runtime.SpawnQueue.Any(id=>id=="enemy.arcade.brute"),Is.False);}
        [Test] public void MixedCompositionRetainsExactIntegerAllocation(){Prepare(12,6,0);Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(310));Assert.That(runtime.SpawnQueue.Count(id=>id=="enemy.arcade.basic"),Is.EqualTo(232));Assert.That(runtime.SpawnQueue.Count(id=>id=="enemy.arcade.archer"),Is.EqualTo(78));}
        [Test] public void OrdinaryGroupSubstitutionPreservesOriginalWaveGold(){Prepare(8,6,3);runtime.SpawnQueue.Clear();typeof(ArcadeCombatSystem).GetMethod("TickWaveTransition",Flags).Invoke(system,new object[]{runtime,.01f});Assert.That(runtime.ArenaGold,Is.EqualTo(ArcadeCombatSystem.ScaleGold(40,6,catalog.Rule.GoldGrowthPerArena)));}
        [Test] public void LegacySequentialModeRetainsConfiguredPopulation(){Prepare(15,3,0,false);Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(16));}
        [Test] public void TimedChallengeRetainsItsOwnPopulation(){Prepare(15,3,0,true,true);Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(6));}
        [Test] public void FinalBossStageNeverExpandsOrdinaryPopulation(){Prepare(20,10,3);typeof(ArcadeCombatSystem).GetMethod("BeginFinalBoss",Flags).Invoke(system,new object[]{runtime});Assert.That(runtime.IsFinalBoss,Is.True);Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(1));}
    }
}
