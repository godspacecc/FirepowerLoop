using System;
using System.IO;
using System.Reflection;
using System.Linq;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>真实单人安全节点及文件事务，不以模拟钱包替代存档。</summary>
    public sealed class ScoreShopRunTests
    {
        private string root;
        private Game architecture;
        private ArcadeRunSystem system;
        [SetUp] public void SetUp(){root=Path.Combine(Path.GetTempPath(),"ScoreShopTests",Guid.NewGuid().ToString("N"));Reload();}
        private void Reload()
        {
            TestReset.ResetArchitecture();architecture=new Game();
            typeof(Architecture<Game>).GetField("mArchitecture",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,architecture);
            typeof(Architecture<Game>).GetField("mInited",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(architecture,true);
            architecture.RegisterModel(new ArcadeRunModel());
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))))));
            architecture.RegisterSystem(new ArcadeRunSystem());system=architecture.GetSystem<ArcadeRunSystem>();system.ConfigureForTests(root);
        }
        [TearDown] public void TearDown(){TestReset.ResetArchitecture();if(Directory.Exists(root))Directory.Delete(root,true);}
        private ArcadeRunSaveData CompleteFirst()
        {
            Assert.That(system.TryStartConfiguredRun(out var run,out var error),Is.True,error);
            Assert.That(run.ScoreShop,Is.Null);Assert.That(system.TryContinue(out _,out error),Is.True,error);
            architecture.GetModel<ArcadeRunModel>().SetTransientTreasureScore(new ArcadeTreasureScoreState(210000,2,400,4,1));
            Assert.That(system.TryCompleteArena(new ArcadeArenaCompletion {ArenaNumber=1,Score=210000,ScoreMultiplier=2,MultiplierProgress=400,
                EmergencyResources=run.EmergencyResources,EquipmentBySlot=run.EquipmentBySlot,NextArenaConfigNumber=21,
                NextCheckpoint=new ArcadeCheckpointSaveData{ArenaNumber=2,GenerationSeed=123}},out var next,out error),Is.True,error);
            return next;
        }
        [Test] public void WholeStage_OpensOnceAndBlocksContinueUntilSkip()
        {
            var run=CompleteFirst();Assert.That(run.ScoreShop,Is.Not.Null);
            Assert.That(system.TryContinue(out _,out _),Is.False);
            Assert.That(system.TryOperateScoreShop(ScoreShopAction.Skip,null,run.ScoreShop.Revision,out run,out var error),Is.True,error);
            Assert.That(run.Score,Is.EqualTo(210000));Assert.That(system.TryContinue(out _,out error),Is.True,error);
        }
        [Test] public void Refresh_ReloadPreservesPaidOffersScoreAndLifeTier()
        {
            var run=CompleteFirst();Assert.That(run.ScoreShop,Is.Not.Null);
            Assert.That(system.TryOperateScoreShop(ScoreShopAction.Refresh,null,1,out run,out var error),Is.True,error);
            var offers=run.ScoreShop.Offers.ToArray();Reload();Assert.That(system.TryResume(out run,out error),Is.True,error);
            Assert.That(run.Score,Is.EqualTo(209500));Assert.That(run.ScoreShop.Offers,Is.EqualTo(offers));Assert.That(run.ScoreLifeTier,Is.EqualTo(1));
            Assert.That(run.ScoreMultiplier,Is.EqualTo(2));Assert.That(run.MultiplierProgress,Is.EqualTo(400));
        }
        [Test] public void Purchase_RetryCannotChargeTwiceAndPendingDeliverySurvivesReload()
        {
            var run=CompleteFirst();Assert.That(run.ScoreShop,Is.Not.Null);var id=run.ScoreShop.Offers[0];
            Assert.That(system.TryOperateScoreShop(ScoreShopAction.Buy,id,1,out run,out var error),Is.True,error);long score=run.Score;
            Assert.That(system.TryOperateScoreShop(ScoreShopAction.Buy,id,1,out _,out _),Is.False);
            Reload();Assert.That(system.TryResume(out run,out error),Is.True,error);
            Assert.That(run.Score,Is.EqualTo(score));Assert.That(run.ScoreShop.PurchasedDropId,Is.EqualTo(id));Assert.That(run.ScoreShop.Delivered,Is.False);
        }
        [Test] public void SaveFailure_KeepsOriginalScoreAndOfferRevision()
        {
            var run=CompleteFirst();Assert.That(run.ScoreShop,Is.Not.Null);
            string blocked=Path.Combine(root,"blocked");File.WriteAllText(blocked,"not a directory");system.ConfigureForTests(blocked);
            Assert.That(system.TryOperateScoreShop(ScoreShopAction.Refresh,null,1,out _,out _),Is.False);
            var current=architecture.GetModel<ArcadeRunModel>().Current;
            Assert.That(current.Score,Is.EqualTo(210000));Assert.That(current.ScoreShop.Revision,Is.EqualTo(1));
            Assert.That(current.ScoreShop.Offers,Is.EqualTo(run.ScoreShop.Offers));
        }
        [Test] public void AbandonFromOpenShop_PersistsResultWithoutStalePurchaseState()
        {
            CompleteFirst();Assert.That(system.TryAbandon(out var error),Is.True,error);
            Reload();Assert.That(system.TryResume(out var run,out error),Is.True,error);
            Assert.That(run.Outcome,Is.EqualTo("abandoned"));Assert.That(run.ScoreShop,Is.Null);
        }
        [Test] public void PurchasedEffectAndDeliveredFlag_SaveTogetherAndSurviveReload()
        {
            var run=CompleteFirst();string id=run.ScoreShop.Offers[0];
            Assert.That(system.TryOperateScoreShop(ScoreShopAction.Buy,id,1,out run,out var error),Is.True,error);
            Assert.That(system.TryContinue(out _,out error),Is.True,error);
            var round=FirstRound();
            Assert.That(system.TrySaveRound(round,out _),Is.False,"没有领取效果的首轮不能吞掉商品");
            round.ScoreShopDelivered=true;round.PlayerState.TemporaryWeaponId="arcade.weapon.rapid";round.PlayerState.TemporaryWeaponRemaining=25;
            Assert.That(system.TrySaveRound(round,out error),Is.True,error);
            Reload();Assert.That(system.TryResume(out run,out error),Is.True,error);
            Assert.That(run.ScoreShop.Delivered,Is.True);Assert.That(run.RoundCheckpoint.PlayerState.TemporaryWeaponRemaining,Is.EqualTo(25));
        }
        [Test] public void FileValidation_RejectsRoundThatWouldSkipPendingPurchase()
        {
            var run=CompleteFirst();Assert.That(system.TryOperateScoreShop(ScoreShopAction.Buy,run.ScoreShop.Offers[0],1,out run,out var error),Is.True,error);
            run.RoundCheckpoint=FirstRound();
            Assert.That(ArcadeRunFileStore.TryValidate(run,out _),Is.False);
        }
        [Test] public void FinalVictory_HasNoShopAndLegacyRulesStayDisabled()
        {
            var run=CompleteFirst();Assert.That(architecture.GetUtility<IRuntimeConfigProvider>().TryGetArcadeCatalog(out var catalog,out var configError),Is.True,configError);
            int stageCount=1;var terminal=catalog.Arenas.First(a=>a.ArenaNumber==1);
            while(terminal.NextArenaNumbers.Count>0){stageCount++;terminal=catalog.Arenas.First(a=>a.ArenaNumber==terminal.NextArenaNumbers[0]);}
            run.ScoreShop=null;run.NextArenaNumber=stageCount;run.CompletedArenaCount=stageCount-1;run.ArenaConfigNumber=terminal.ArenaNumber;run.Checkpoint.ArenaNumber=stageCount;
            architecture.GetModel<ArcadeRunModel>().CommitSafeNode(run);Assert.That(system.TryContinue(out _,out var error),Is.True,error);
            Assert.That(system.TryCompleteArena(new ArcadeArenaCompletion{ArenaNumber=stageCount,Victory=true,Score=run.Score,
                EmergencyResources=run.EmergencyResources,EquipmentBySlot=run.EquipmentBySlot},out run,out error),Is.True,error);
            Assert.That(run.ScoreShop,Is.Null);Assert.That(run.HasResult,Is.True);
            // 旧版本字段为零，之后正常通关也不凭空添加新商店。
            Reload();Assert.That(system.TryResume(out run,out error),Is.True,error);run.Victory=false;run.Outcome=null;
            run.NextArenaNumber=1;run.CompletedArenaCount=0;run.ArenaConfigNumber=1;run.Checkpoint.ArenaNumber=1;run.ScoreShopRulesVersion=0;
            architecture.GetModel<ArcadeRunModel>().CommitSafeNode(run);Assert.That(system.TryContinue(out _,out error),Is.True,error);
            Assert.That(system.TryCompleteArena(new ArcadeArenaCompletion{ArenaNumber=1,Score=run.Score,NextArenaConfigNumber=21,
                EmergencyResources=run.EmergencyResources,EquipmentBySlot=run.EquipmentBySlot,NextCheckpoint=new ArcadeCheckpointSaveData{ArenaNumber=2}},out run,out error),Is.True,error);
            Assert.That(run.ScoreShop,Is.Null);
        }
        private static ArcadeRoundCheckpoint FirstRound()=>new ArcadeRoundCheckpoint{ArenaNumber=2,ArenaConfigNumber=21,RoundNumber=1,LayoutVersion=1,MapId="arena.21.round.1",
            PlayerState=new ArcadeIslandRoundCheckpoint{MapId="arena.21.round.1",RoundNumber=1,PlayerHealth=100,PlayerMaxHealth=100,Bombs=1,Dashes=2}};
    }
}
