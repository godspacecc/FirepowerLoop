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
            initial.EquipmentBySlot["weapon"]=ArcadeRunDefaults.BasicWeaponId;Assert.That(store.Save(initial,out var error),Is.True,error);architecture.GetModel<ArcadeRunModel>().CommitSafeNode(initial);
        }
        [TearDown] public void Cleanup(){if(root!=null&&Directory.Exists(root)){Assert.That(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase),Is.True);Directory.Delete(root,true);}}
        private ArcadeSlayerChallengeState State()=>ArcadeSlayerChallengeRules.Create(config,initial.RunId,new long[]{1},4,5,1);
        private bool Enter(ArcadeSlayerChallengeState state)
        {var method=typeof(ArcadeRunSystem).GetMethod("TryCommitSlayerEntry");Assert.That(method,Is.Not.Null,"缺少单人挑战尝试事务");return (bool)method.Invoke(system,new object[]{state,null});}
        private void Prepare(){Assert.That(system.TryStartChallenge(out _,out var error),Is.True,error);}
        [Test] public void PreparationDoesNotConsumeAttempt(){Prepare();Assert.That(store.Inspect().Data.Checkpoint.ChallengeAttempted,Is.False);Assert.That(architecture.GetModel<ArcadeRunModel>().Phase,Is.EqualTo(ArcadeRunPhase.Challenge));}
        [Test] public void ActualEntrySavesAttemptAndKeepsChallengePhase(){Prepare();Assert.That(Enter(State()),Is.True);Assert.That(store.Inspect().Data.Checkpoint.ChallengeAttempted,Is.True);Assert.That(architecture.GetModel<ArcadeRunModel>().Phase,Is.EqualTo(ArcadeRunPhase.Challenge));}
        [Test] public void FailedEntrySaveLeavesNodeAndCanRetry(){Prepare();string before=File.ReadAllText(store.RunPath);using(var blocker=new FileStream(store.TempPath,FileMode.Create,FileAccess.Write,FileShare.None)){Assert.That(Enter(State()),Is.False);Assert.That(File.ReadAllText(store.RunPath),Is.EqualTo(before));Assert.That(architecture.GetModel<ArcadeRunModel>().Current.Checkpoint.ChallengeAttempted,Is.False);}Assert.That(Enter(State()),Is.True);}
        [Test] public void OldRunIdentityCannotConsumeAttempt(){Prepare();var state=State();state.RunId="old-run";Assert.That(Enter(state),Is.False);Assert.That(store.Inspect().Data.Checkpoint.ChallengeAttempted,Is.False);}
        [Test] public void MainlineOpportunityOnlyAppearsAtConfiguredRule1Node(){var method=typeof(ArcadeRunSystem).GetMethod("ConfigureSlayerOpportunity",BindingFlags.Instance|BindingFlags.NonPublic);Assert.That(method,Is.Not.Null);foreach(int completed in new[]{2,3,4}){var candidate=initial.Clone();candidate.CompletedArenaCount=completed;candidate.NextArenaNumber=completed+1;candidate.Checkpoint.ChallengeId=null;method.Invoke(system,new object[]{candidate});Assert.That(candidate.Checkpoint.ChallengeId==config.ChallengeId,Is.EqualTo(completed==3));}var old=initial.Clone();old.SlayerMartRulesVersion=0;old.Checkpoint.ChallengeId=null;method.Invoke(system,new object[]{old});Assert.That(old.Checkpoint.ChallengeId,Is.Null.Or.Empty);}
    }
}
