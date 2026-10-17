using System;
using System.IO;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>单人Slayer尝试标记与真实临时文件失败重试，不进入正式玩家存档目录。</summary>
    public sealed class SlayerChallengeEntryTests
    {
        private string root;private Game architecture;private ArcadeRunSystem system;private ArcadeRunFileStore store;
        private ArcadeSlayerChallengeConfiguration config;private ArcadeRunSaveData initial;
        [SetUp] public void Setup()
        {
            root=Path.Combine(Path.GetTempPath(),"FirepowerSlayerEntry-"+Guid.NewGuid().ToString("N"));architecture=new Game();architecture.RegisterModel(new ArcadeRunModel());
            var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
            var provider=new LubanRuntimeConfigProvider(()=>tables);architecture.RegisterUtility<IRuntimeConfigProvider>(provider);config=ArcadeSlayerChallengeConfiguration.FromTables(tables);
            system=new ArcadeRunSystem();architecture.RegisterSystem(system);system.ConfigureForTests(root);store=new ArcadeRunFileStore(root);
            initial=new ArcadeRunSaveData{RunId=Guid.NewGuid().ToString("N"),IsCasualRoute=true,SlayerMartRulesVersion=1,CompletedArenaCount=3,NextArenaNumber=4,ArenaConfigNumber=5,
                RemainingLives=3,UpdatedAtUtc=DateTime.UtcNow.ToString("O"),Checkpoint=new ArcadeCheckpointSaveData{ArenaNumber=4,ChallengeId=config.ChallengeId}};
            initial.SlayerMart=ArcadeSlayerMartRules.Create(initial.RunId,1,new long[]{1},17,ArcadeSlayerMartConfiguration.FromTables(tables).Catalog);initial.SlayerMart.Ready=initial.SlayerMart.Closing=true;
            initial.EquipmentBySlot["weapon"]=ArcadeRunDefaults.BasicWeaponId;Assert.That(store.Save(initial,out var error),Is.True,error);architecture.GetModel<ArcadeRunModel>().CommitSafeNode(initial);
        }
        [TearDown] public void Cleanup(){if(root!=null&&Directory.Exists(root)){Assert.That(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase),Is.True);Directory.Delete(root,true);}}
        private ArcadeSlayerChallengeState State()=>ArcadeSlayerChallengeRules.Create(config,initial.RunId,new long[]{1},4,5,1);
        private bool Enter(ArcadeSlayerChallengeState state)
        {var method=typeof(ArcadeRunSystem).GetMethod("TryCommitSlayerEntry");Assert.That(method,Is.Not.Null,"缺少单人挑战尝试事务");return (bool)method.Invoke(system,new object[]{state,null});}
        private ArcadeSlayerChallengeState ReturnState(){var s=ArcadeSlayerChallengeRules.SetEntryChoice(State(),config,1,true);s=ArcadeSlayerChallengeRules.Advance(s,config,90,true);s=ArcadeSlayerChallengeRules.BossDefeated(s,config);return ArcadeSlayerChallengeRules.Advance(s,config,300,true);}
        private bool Complete(ArcadeSlayerChallengeState state){var method=typeof(ArcadeRunSystem).GetMethod("TryCompleteSlayerChallenge");Assert.That(method,Is.Not.Null,"缺少Slayer返回事务");return (bool)method.Invoke(system,new object[]{state,initial.SlayerMart,new ArcadeIslandRoundCheckpoint{Bombs=0,Dashes=1},null});}
        [Test] public void SlayerReturnPreservesMainNodeAndSavesActualSuppliesAndCash(){Prepare();Assert.That(Enter(State()),Is.True);Assert.That(Complete(ReturnState()),Is.True);var saved=store.Inspect().Data;Assert.That(saved.NextArenaNumber,Is.EqualTo(4));Assert.That(saved.ArenaConfigNumber,Is.EqualTo(5));Assert.That(saved.RemainingLives,Is.EqualTo(3));Assert.That(saved.EmergencyResources[ArcadeRunDefaults.BombResourceId],Is.Zero);Assert.That(saved.SlayerMart.Players[0].Cash,Is.EqualTo(5000));Assert.That(saved.Checkpoint.ChallengeId,Is.Null.Or.Empty);}
        [Test] public void SlayerReturnSaveFailureCanRetryWithoutDuplicateCash(){Prepare();Assert.That(Enter(State()),Is.True);var state=ReturnState();string before=File.ReadAllText(store.RunPath);using(var blocker=new FileStream(store.TempPath,FileMode.Create,FileAccess.Write,FileShare.None)){Assert.That(Complete(state),Is.False);Assert.That(File.ReadAllText(store.RunPath),Is.EqualTo(before));}Assert.That(Complete(state),Is.True);Assert.That(store.Inspect().Data.SlayerMart.Players[0].Cash,Is.EqualTo(5000));}
        [Test] public void SkippedPreparationCanReturnWithoutConsumingBeforehand(){Prepare();var state=ArcadeSlayerChallengeRules.SetEntryChoice(State(),config,1,false);Assert.That(Complete(state),Is.True);Assert.That(store.Inspect().Data.Checkpoint.ChallengeAttempted,Is.True);Assert.That(store.Inspect().Data.SlayerMart.Players[0].Cash,Is.Zero);}
        [Test] public void OldIdentityCannotCommitReturn(){Prepare();Assert.That(Enter(State()),Is.True);var state=ReturnState();state.RunId="old-run";Assert.That(Complete(state),Is.False);Assert.That(architecture.GetModel<ArcadeRunModel>().Phase,Is.EqualTo(ArcadeRunPhase.Challenge));}
        [Test] public void PreparationRestoreRetainsUnconsumedOpportunity(){Prepare();Assert.That(system.TryResume(out var saved,out var error),Is.True,error);Assert.That(saved.Checkpoint.ChallengeId,Is.EqualTo(config.ChallengeId));Assert.That(saved.Checkpoint.ChallengeAttempted,Is.False);}
        [Test] public void ActualAttemptRestoreCannotRestartChallenge(){Prepare();Assert.That(Enter(State()),Is.True);Assert.That(system.TryResume(out var saved,out var error),Is.True,error);Assert.That(saved.Checkpoint.ChallengeAttempted,Is.True);Assert.That(system.TryStartChallenge(out _,out _),Is.False);}
        private void Prepare(){Assert.That(system.TryStartChallenge(out _,out var error),Is.True,error);}
        [Test] public void PreparationDoesNotConsumeAttempt(){Prepare();Assert.That(store.Inspect().Data.Checkpoint.ChallengeAttempted,Is.False);Assert.That(architecture.GetModel<ArcadeRunModel>().Phase,Is.EqualTo(ArcadeRunPhase.Challenge));}
        [Test] public void ActualEntrySavesAttemptAndKeepsChallengePhase(){Prepare();Assert.That(Enter(State()),Is.True);Assert.That(store.Inspect().Data.Checkpoint.ChallengeAttempted,Is.True);Assert.That(architecture.GetModel<ArcadeRunModel>().Phase,Is.EqualTo(ArcadeRunPhase.Challenge));}
        [Test] public void FailedEntrySaveLeavesNodeAndCanRetry(){Prepare();string before=File.ReadAllText(store.RunPath);using(var blocker=new FileStream(store.TempPath,FileMode.Create,FileAccess.Write,FileShare.None)){Assert.That(Enter(State()),Is.False);Assert.That(File.ReadAllText(store.RunPath),Is.EqualTo(before));Assert.That(architecture.GetModel<ArcadeRunModel>().Current.Checkpoint.ChallengeAttempted,Is.False);}Assert.That(Enter(State()),Is.True);}
        [Test] public void OldRunIdentityCannotConsumeAttempt(){Prepare();var state=State();state.RunId="old-run";Assert.That(Enter(state),Is.False);Assert.That(store.Inspect().Data.Checkpoint.ChallengeAttempted,Is.False);}
        [Test] public void MainlineOpportunityOnlyAppearsAtConfiguredRule1Node(){var method=typeof(ArcadeRunSystem).GetMethod("ConfigureSlayerOpportunity",BindingFlags.Instance|BindingFlags.NonPublic);Assert.That(method,Is.Not.Null);foreach(int completed in new[]{2,3,4}){var candidate=initial.Clone();candidate.CompletedArenaCount=completed;candidate.NextArenaNumber=completed+1;candidate.Checkpoint.ChallengeId=null;method.Invoke(system,new object[]{candidate});Assert.That(candidate.Checkpoint.ChallengeId==config.ChallengeId,Is.EqualTo(completed==3));}var old=initial.Clone();old.SlayerMartRulesVersion=0;old.Checkpoint.ChallengeId=null;method.Invoke(system,new object[]{old});Assert.That(old.Checkpoint.ChallengeId,Is.Null.Or.Empty);}
    }
}
