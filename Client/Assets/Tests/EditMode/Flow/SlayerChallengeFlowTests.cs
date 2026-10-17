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
            run.SlayerMart=ArcadeSlayerMartRules.Create(run.RunId,1,new long[]{1},17,ArcadeSlayerMartConfiguration.FromTables(tables).Catalog);run.SlayerMart.Ready=run.SlayerMart.Closing=true;
            run.Checkpoint.ChallengeId=config.ChallengeId;run.EquipmentBySlot["weapon"]=ArcadeRunDefaults.BasicWeaponId;architecture.GetModel<ArcadeRunModel>().CommitSafeNode(run);
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);
            typeof(ArcadeCombatSystem).GetField("catalog",Flags).SetValue(system,catalog);typeof(ArcadeCombatSystem).GetField("view",Flags).SetValue(system,new View());
            typeof(ArcadeCombatSystem).GetField("random",Flags).SetValue(system,new System.Random(17));
            runtime=new ArcadeCombatRuntime{ArenaNumber=4,IsChallenge=true,ChallengeId=config.ChallengeId,PlayerHealth=100,PlayerMaxHealth=100,SlayerMart=run.SlayerMart.Clone(),SlayerMartRulesVersion=1};architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
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
        private ArcadeEnemyRuntime Guardian(){Start();Call("TickSlayerChallenge",runtime,90f);Assert.That(runtime.Enemies.Count,Is.EqualTo(1),"缺少实际Guardian");var e=runtime.Enemies.Values.Single();e.Position=Vector2.zero;runtime.PlayerPosition=Vector2.up;return e;}
        [Test] public void GuardianHasConfiguredHealth(){var e=Guardian();Assert.That(e.Health,Is.EqualTo(1500));Assert.That(e.IsBossPresentation,Is.True);}
        [Test] public void GuardianHammerUsesIndependentDown(){var e=Guardian();Call("TickSlayerGuardian",runtime,e,.1f);Assert.That(runtime.PlayerHealth,Is.EqualTo(100));Call("TickSlayerGuardian",runtime,e,1f);Assert.That(State.TeamDowns,Is.EqualTo(1));Assert.That(architecture.GetModel<ArcadeRunModel>().Current.RemainingLives,Is.EqualTo(3));}
        [Test] public void GuardianHammerRespectsProtection(){var e=Guardian();runtime.PlayerInvulnerability=10;Call("TickSlayerGuardian",runtime,e,.1f);Call("TickSlayerGuardian",runtime,e,1f);Assert.That(runtime.PlayerHealth,Is.EqualTo(100));}
        [Test] public void GuardianDeathOpensShoppingWithoutMainExit(){var e=Guardian();Call("KillEnemy",runtime,e,false);Assert.That(State.Phase,Is.EqualTo(ArcadeSlayerChallengePhase.Shopping));Assert.That(runtime.AwaitingExit,Is.False);Assert.That(runtime.CompletionSubmitted,Is.False);}
        [Test] public void GuardianBombCannotClearBoss(){var e=Guardian();runtime.Bombs=1;Call("UseBomb",runtime);Assert.That(runtime.Enemies.ContainsKey(e.Id),Is.True);}
        [Test] public void GuardianVictoryCreditsIndependentShop(){Guardian();Call("KillEnemy",runtime,runtime.Enemies.Values.Single(),false);Assert.That(runtime.SlayerMartReturnWave,Is.GreaterThan(0));Assert.That(runtime.SlayerMart.Players[0].Cash,Is.EqualTo(5000));Assert.That(runtime.SlayerMart.Generation,Is.EqualTo(2));Assert.That(runtime.SlayerMart.CollectedBoxes.Count,Is.EqualTo(9));}
        [Test] public void CollectionKillGuaranteesPhysicalCoin(){Start();var e=new ArcadeEnemyRuntime{Id=71,Position=runtime.PlayerPosition};runtime.Enemies.Add(e.Id,e);Call("KillEnemy",runtime,e,false);Assert.That(runtime.Drops.Count,Is.EqualTo(1));Assert.That(State.CashCollected,Is.Zero);}
        [Test] public void PhysicalCoinCreditsChallengeCashInsteadOfRunGold(){Start();var e=new ArcadeEnemyRuntime{Id=71,Position=runtime.PlayerPosition};runtime.Enemies.Add(e.Id,e);Call("KillEnemy",runtime,e,false);Call("TickDrops",runtime);Assert.That(State.CashCollected,Is.EqualTo(100));Assert.That(runtime.ArenaGold,Is.Zero);Assert.That(architecture.GetModel<ArcadeRunModel>().Current.RunGold,Is.Zero);}
        [Test] public void LongFrameDoesNotExtendSlayerDeadline(){Start();double now=0;typeof(ArcadeCombatSystem).GetField("slayerMartRealtime",Flags).SetValue(system,new Func<double>(()=>now));system.TickFrame(runtime,.001f);now=3;system.TickFrame(runtime,.05f);Assert.That(State.RemainingSeconds,Is.EqualTo(87).Within(.0001));}
        [Test] public void ChallengeAttributedKillStillChargesOwnedStorm(){Start();runtime.SlayerMart.Players[0].Owned.Add("slayermart.item.16");var e=new ArcadeEnemyRuntime{Id=71,Position=runtime.PlayerPosition};runtime.Enemies.Add(e.Id,e);Call("KillEnemy",runtime,e,false);Assert.That(runtime.SlayerMart.Players[0].StormCharge,Is.EqualTo(1));}
        [Test] public void ReturningTickStopsAfterSynchronousCombatRelease()
        {
            string root=Path.Combine(Path.GetTempPath(),"FirepowerSlayerTick-"+Guid.NewGuid().ToString("N"));
            var runSystem=new ArcadeRunSystem();architecture.RegisterSystem(runSystem);runSystem.ConfigureForTests(root);
            run.Checkpoint.ArenaNumber=4;run.Checkpoint.ChallengeAttempted=true;run.UpdatedAtUtc=DateTime.UtcNow.ToString("O");
            Assert.That(new ArcadeRunFileStore(root).Save(run,out var error),Is.True,error);architecture.GetModel<ArcadeRunModel>().CommitChallenge(run);
            Start();runtime.SlayerChallenge=ArcadeSlayerChallengeRules.SetExitVote(State,1,true);
            var registration=architecture.RegisterEvent<ArcadeRunChangedEvent>(evt=>Call("OnRunChanged",evt));
            try{Assert.DoesNotThrow(()=>system.Tick(runtime,.1f));Assert.That(architecture.GetModel<ArcadeCombatModel>().Runtime,Is.Null);}
            finally{registration.UnRegister();Assert.That(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase),Is.True);if(Directory.Exists(root))Directory.Delete(root,true);}
        }
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
