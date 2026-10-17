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
    /// <summary>单人实际战斗System的Slayer阶段和独立倒地，使用正式配置和隔离架构。</summary>
    public sealed class SlayerChallengeFlowTests
    {
        private Game architecture;private ArcadeCombatSystem system;private ArcadeCombatRuntime runtime;private ArcadeRunSaveData run;
        private ArcadeSlayerChallengeConfiguration config;private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private object Call(string name,params object[] args){var m=typeof(ArcadeCombatSystem).GetMethod(name,Flags);Assert.That(m,Is.Not.Null,"单人Slayer宿主尚未接入："+name);return m.Invoke(system,args);}
        private ArcadeSlayerChallengeState State {get{var f=runtime.GetType().GetField("SlayerChallenge");Assert.That(f,Is.Not.Null);return (ArcadeSlayerChallengeState)f.GetValue(runtime);}}
        [SetUp] public void Setup()
        {
            architecture=new Game();architecture.RegisterModel(new ArcadeRunModel());architecture.RegisterModel(new ArcadeCombatModel());
            var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
            var provider=new LubanRuntimeConfigProvider(()=>tables);architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            Assert.That(provider.TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);config=ArcadeSlayerChallengeConfiguration.FromTables(tables);
            run=new ArcadeRunSaveData{RunId=Guid.NewGuid().ToString("N"),IsCasualRoute=true,ArenaConfigNumber=5,CompletedArenaCount=3,NextArenaNumber=4,RemainingLives=3,SlayerMartRulesVersion=1};
            run.Checkpoint.ChallengeId=config.ChallengeId;run.EquipmentBySlot["weapon"]=ArcadeRunDefaults.BasicWeaponId;architecture.GetModel<ArcadeRunModel>().CommitSafeNode(run);
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);
            typeof(ArcadeCombatSystem).GetField("catalog",Flags).SetValue(system,catalog);typeof(ArcadeCombatSystem).GetField("view",Flags).SetValue(system,new View());
            typeof(ArcadeCombatSystem).GetField("random",Flags).SetValue(system,new System.Random(17));
            runtime=new ArcadeCombatRuntime{ArenaNumber=4,IsChallenge=true,ChallengeId=config.ChallengeId,PlayerHealth=100,PlayerMaxHealth=100,SlayerMartRulesVersion=1};architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            Call("InitializeSlayerChallenge",runtime,run);
        }
        [TearDown] public void Cleanup(){architecture?.GetModel<ArcadeCombatModel>().Clear();}
        private void Start(){runtime.GetType().GetField("SlayerChallenge").SetValue(runtime,ArcadeSlayerChallengeRules.SetEntryChoice(State,config,1,true));Call("BeginSlayerCollectionWave",runtime);}
        private void Kill(){runtime.PlayerHealth=100;runtime.PlayerInvulnerability=runtime.RespawnRemaining=0;Call("DamagePlayerCore",runtime,100000,SlayerMartHitKind.Ordinary);}
        [Test] public void IndependentLayoutKeepsOriginalMainlineReturn(){Assert.That(State.ReturnMap,Is.EqualTo(5));Assert.That(State.ReturnArena,Is.EqualTo(4));Assert.That(run.ArenaConfigNumber,Is.EqualTo(5));Assert.That(State.Phase,Is.EqualTo(ArcadeSlayerChallengePhase.Preparing));}
        [Test] public void PreparationHasNoSpawnQueue(){Assert.That(runtime.SpawnQueue.Count,Is.Zero);Assert.That(runtime.ChallengeSeconds,Is.EqualTo(1200));}
        [Test] public void CollectionStartsFiniteConfiguredBatch(){Start();Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(30));Assert.That(State.RemainingSeconds,Is.EqualTo(90d));}
        [Test] public void ClearedBatchRefillsWithoutCompletingChallenge(){Start();runtime.SpawnQueue.Clear();Call("TickWaveTransition",runtime,.1f);Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(30));Assert.That(State.Phase,Is.EqualTo(ArcadeSlayerChallengePhase.Collecting));Assert.That(runtime.CompletionSubmitted,Is.False);}
        [Test] public void CollectionTimeoutStartsBoss(){Start();Call("TickSlayerChallenge",runtime,90f);Assert.That(State.Phase,Is.EqualTo(ArcadeSlayerChallengePhase.Boss));Assert.That(runtime.CompletionSubmitted,Is.False);}
        [Test] public void FirstDeathDoesNotConsumeMainLives(){Start();Kill();Assert.That(State.TeamDowns,Is.EqualTo(1));Assert.That(runtime.RespawnRemaining,Is.EqualTo(3));Assert.That(architecture.GetModel<ArcadeRunModel>().Current.RemainingLives,Is.EqualTo(3));}
        [Test] public void ThreeDeathsRemainRecoverable(){Start();for(int i=0;i<3;i++)Kill();Assert.That(State.Phase,Is.EqualTo(ArcadeSlayerChallengePhase.Collecting));Assert.That(State.TeamDowns,Is.EqualTo(3));}
        [Test] public void FourthDeathReturnsWithoutMainRunFailure(){Start();for(int i=0;i<4;i++)Kill();Assert.That(State.Outcome,Is.EqualTo(ArcadeSlayerChallengeOutcome.Failed));Assert.That(State.Phase,Is.EqualTo(ArcadeSlayerChallengePhase.Returning));Assert.That(architecture.GetModel<ArcadeRunModel>().Current.RemainingLives,Is.EqualTo(3));}
        [Test] public void PaidChallengeRespawnRestoresOnce(){Start();Kill();Call("TickTimers",runtime,3f);Assert.That(runtime.PlayerHealth,Is.EqualTo(100));Assert.That(runtime.PlayerInvulnerability,Is.GreaterThan(0));}
        [Test] public void PreparationRejectsDamage(){Call("DamagePlayerCore",runtime,100000,SlayerMartHitKind.Ordinary);Assert.That(runtime.PlayerHealth,Is.EqualTo(100));Assert.That(State.TeamDowns,Is.Zero);}
        private sealed class View:IArcadeCombatViewPort
        {
            public bool IsActive=>true;public void Begin(int n,float w,float h,IReadOnlyList<ArcadeObstacleDefinition> o){}public void End(){}
            public bool TryScreenToArena(Vector2 s,out Vector2 p){p=Vector2.zero;return false;}
            public void SetPlayer(Vector2 p,Vector2 f,bool v,bool i){}public void SetPlayerHealth(int c,int m){}
            public void SetEnemy(int id,string config,Vector2 p,int c,int m,bool telegraph=false,int attackSequence=0,bool persistentHealthBar=false){}
            public void RemoveEnemy(int id,bool playDeath=false){}public void SetProjectile(int id,bool hostile,Vector2 p,Vector2 d){}public void RemoveProjectile(int id){}
            public void SetDrop(int id,ArcadeDropDefinition d,Vector2 p){}public void RemoveDrop(int id){}
            public void SetRemotePlayer(long id,Vector2 p,Vector2 f,bool v,bool i,int c,int m){}public void RemoveRemotePlayer(long id){}public void TickPlayerFeedback(float s){}
        }
    }
}
