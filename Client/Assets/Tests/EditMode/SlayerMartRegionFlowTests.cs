using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using FirepowerLoop.Arcade;

namespace Game.Tests
{
    /// <summary>商店真实出口与轮前候选的定向回归，仅使用临时存档和内存表现端口。</summary>
    public sealed class SlayerMartRegionFlowTests
    {
        internal Game architecture;
        internal ArcadeCombatSystem combat;
        internal ArcadeCombatRuntime runtime;
        internal ArcadeConfigCatalogDefinition catalog;
        private string directory;
        private ArcadeSlayerMartConfiguration shopConfig;
        private int operation;
        internal View view;
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp] public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "FirepowerFullRoute-" + Guid.NewGuid().ToString("N"));
            architecture = new Game();
            var run = new ArcadeRunModel(); architecture.RegisterModel(run);
            architecture.RegisterModel(new ArcadeCombatModel());
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(
                File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog, out var error), Is.True, error);
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            var runs = new ArcadeRunSystem(); architecture.RegisterSystem(runs); runs.ConfigureForTests(directory);
            var data = new ArcadeRunSaveData { RunId = Guid.NewGuid().ToString("N"), IsCasualRoute = true,
                ArenaConfigNumber = 3, SlayerMartRulesVersion=1, CompletedArenaCount = 2, NextArenaNumber = 3, UpdatedAtUtc = DateTime.UtcNow.ToString("O") };
            data.Checkpoint.ArenaNumber = 3;
            data.EquipmentBySlot[ArcadeRunDefaults.WeaponSlotId] = ArcadeRunDefaults.BasicWeaponId;
            Assert.That(new ArcadeRunFileStore(directory).Save(data, out error), Is.True, error);
            run.CommitSafeNode(data); run.EnterArena();
            combat = new ArcadeCombatSystem(); ((ICanSetArchitecture)combat).SetArchitecture(architecture);
            runtime = new ArcadeCombatRuntime { SlayerMartRulesVersion=1, ArenaNumber = 3, PlayerHealth = 73, PlayerMaxHealth = 100 };
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            Set("catalog", catalog); Set("arena", System.Linq.Enumerable.First(catalog.Arenas, a => a.ArenaNumber == 3)); Set("random", new System.Random(37));
            Assert.That(provider.TryGetSlayerMart(out shopConfig,out error),Is.True,error);Set("slayerMartConfig",shopConfig);
            view = new View(); Set("view", view);
        }
        [TearDown] public void TearDown()
        {
            architecture.GetModel<ArcadeCombatModel>().Clear();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        private void Set(string name, object value) => typeof(ArcadeCombatSystem).GetField(name, Flags).SetValue(combat, value);
        private object Call(string name, params object[] values) => typeof(ArcadeCombatSystem).GetMethod(name, Flags).Invoke(combat, values);

        [Test] public void EleventhRoundExitPreparesShopBeforeNextOrdinaryRound()
        {AtStore();Assert.That(runtime.WaveIndex,Is.EqualTo(2));Assert.That(Store().Inspect().Data.RoundCheckpoint.RoundNumber,Is.EqualTo(3));}
        [Test] public void OtherOrdinaryRoundContinuesWithoutShop()
        {ExitRound(0);Assert.That(runtime.SlayerMart,Is.Null);Assert.That(runtime.WaveIndex,Is.EqualTo(1));}
        [Test] public void OldRulesKeepExistingExitFlow()
        {runtime.SlayerMartRulesVersion=0;ExitRound(2);Assert.That(runtime.SlayerMart,Is.Null);Assert.That(runtime.WaveIndex,Is.EqualTo(3));}
        [Test] public void ChallengeCannotPrepareNewShop()
        {runtime.IsChallenge=true;var method=typeof(ArcadeCombatSystem).GetMethod("TryBeginSlayerMart",Flags);Assert.That(method,Is.Not.Null);Assert.That(method.Invoke(combat,new object[]{runtime,3}),Is.False);}
        [Test] public void SkipReturnsWithoutCashOrIntermediateSave()
        {AtStore();Operate(SlayerMartAction.SelectSkip);Step(0);Assert.That(runtime.WaveIndex,Is.EqualTo(3));Assert.That(runtime.SlayerMart.Closing,Is.True);Assert.That(runtime.SlayerMart.Players[0].Cash,Is.Zero);Assert.That(Store().Inspect().Data.RoundCheckpoint.RoundNumber,Is.EqualTo(4));}
        [Test] public void SlowMapPreparationKeepsEntireDeadline()
        {AtStore();view.Ready=false;Operate(SlayerMartAction.SelectEnter);Step(100);Assert.That(runtime.SlayerMart.Ready,Is.False);Assert.That(runtime.SlayerMart.RemainingSeconds,Is.EqualTo(300));view.Ready=true;Step(100);Assert.That(runtime.SlayerMart.Ready,Is.True);Assert.That(runtime.SlayerMart.RemainingSeconds,Is.EqualTo(300));}
        [Test] public void ShoppingMenuDoesNotPauseRegionDeadline()
        {Shopping();Operate(SlayerMartAction.SetMenuOpen,value:true);Step(10);Assert.That(runtime.SlayerMart.RemainingSeconds,Is.EqualTo(290));}
        [Test] public void DeadlineReturnsEvenWithOpenMenu()
        {Shopping();Operate(SlayerMartAction.SetMenuOpen,value:true);Step(301);Assert.That(runtime.WaveIndex,Is.EqualTo(3));Assert.That(runtime.SlayerMart.Closing,Is.True);Assert.That(runtime.SlayerMart.Players[0].MenuOpen,Is.False);}
        [Test] public void PhysicalCashBoxesGrantNinePersonalSharesOnce()
        {Shopping();foreach(var box in shopConfig.Regions[0].Boxes){runtime.PlayerPosition=new(box.X,box.Y);Step(0);}Assert.That(runtime.SlayerMart.Players[0].Cash,Is.EqualTo(18000));Step(0);Assert.That(runtime.SlayerMart.Players[0].Cash,Is.EqualTo(18000));}
        [Test] public void RemoteCashRequestCannotCollectAcrossRoom()
        {Shopping();runtime.PlayerPosition=new(0,-4);Operate(SlayerMartAction.CollectCash,box:1,success:false);Assert.That(runtime.SlayerMart.Players[0].Cash,Is.Zero);}
        [Test] public void ReplayedPurchaseDoesNotRefillBombs()
        {Shopping();Offer(1);runtime.Bombs=0;Operate(SlayerMartAction.Buy,item:1,amount:2,operationId:"bomb");runtime.Bombs=1;Operate(SlayerMartAction.Buy,item:1,amount:2,operationId:"bomb");Assert.That(runtime.Bombs,Is.EqualTo(1));Assert.That(runtime.SlayerMart.Players[0].Cash,Is.EqualTo(97000));}
        [Test] public void GemOverflowRejectsCashAndGrantTogether()
        {Shopping();Offer(17);architecture.GetModel<ArcadeRunModel>().SetTransientTreasureScore(new(long.MaxValue-1,1,0,3));Operate(SlayerMartAction.Buy,item:17,success:false);Assert.That(runtime.SlayerMart.Players[0].Cash,Is.EqualTo(100000));Assert.That(runtime.SlayerMart.PendingGrants,Is.Empty);}
        [Test] public void NextRoundLifeIsConsumedOnceAfterReturnResourcesReady()
        {Shopping();Offer(24);Operate(SlayerMartAction.Buy,item:24);Assert.That(architecture.GetModel<ArcadeRunModel>().Current.RemainingLives,Is.EqualTo(3));runtime.Bombs=runtime.Dashes=0;view.Ready=false;Operate(SlayerMartAction.SetFinished,value:true);Step(0);Assert.That(runtime.IslandTransitionPending,Is.True);Assert.That(runtime.Bombs,Is.Zero);Assert.That(architecture.GetModel<ArcadeRunModel>().Current.RemainingLives,Is.EqualTo(3));view.Ready=true;combat.Tick(runtime,0);Assert.That(runtime.Bombs,Is.EqualTo(1));Assert.That(architecture.GetModel<ArcadeRunModel>().Current.RemainingLives,Is.EqualTo(4));Step(0);Assert.That(architecture.GetModel<ArcadeRunModel>().Current.RemainingLives,Is.EqualTo(4));}
        [Test] public void FailedReturnSaveRetainsOriginalFileAndDoesNotRepeatLife()
        {
            Shopping();Offer(24);Operate(SlayerMartAction.Buy,item:24);Operate(SlayerMartAction.SetFinished,value:true);
            using(var locked=new FileStream(Store().RunPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){Step(0);Assert.That(runtime.CompletionAwaitingRetry,Is.True);}
            Assert.That(Store().Inspect().Data.RoundCheckpoint.RoundNumber,Is.EqualTo(3));Assert.That(Store().Inspect().Data.RemainingLives,Is.EqualTo(3));
            Call("RetryIslandTransition",runtime);Assert.That(runtime.CompletionAwaitingRetry,Is.False);Assert.That(Store().Inspect().Data.RemainingLives,Is.EqualTo(4));
        }
        [Test] public void ChoicePhaseRejectsCombatInputsWithoutConsumingInventory()
        {AtStore();runtime.Bombs=runtime.Dashes=2;runtime.AimDirection=Vector2.up;runtime.SlayerMart.Players[0].Owned.Add("slayermart.item.16");runtime.SlayerMart.Players[0].StormCharge=100;Call("UseBomb",runtime);Call("UseDash",runtime);Call("TryFire",runtime);Assert.That(Call("UseSlayerMartUpgrade",runtime),Is.False);Assert.That(runtime.Bombs,Is.EqualTo(2));Assert.That(runtime.Dashes,Is.EqualTo(2));Assert.That(runtime.Projectiles,Is.Empty);}
        [Test] public void SnapshotOffersAreReadOnlyPersonalCopies()
        {Shopping();var snapshot=combat.GetSlayerMartSnapshot();Assert.That(snapshot.Offers.Count,Is.EqualTo(16));Assert.Throws<NotSupportedException>(()=>((IList<ArcadeSlayerMartOfferSnapshot>)snapshot.Offers).Clear());Assert.That(runtime.SlayerMart.Players[0].Offers.Count,Is.EqualTo(16));}
        [Test] public void ComboAtomicallyHandsOffSpentOrdinaryBarrel()
        {Shopping();var drop=System.Linq.Enumerable.First(catalog.Drops,p=>p.ProductId=="arcade.powerup.barrel");Assert.That(Call("ApplyDrop",runtime,drop),Is.True);var old=runtime.Powerups[ArcadePowerupBehavior.Barrel];old.Charges=1;old.CooldownSeconds=.2f;runtime.SlayerMart.Players[0].OrdinaryBarrelPhase=.7;Offer(6);Operate(SlayerMartAction.Buy,item:6);Assert.That(runtime.Powerups.ContainsKey(ArcadePowerupBehavior.Barrel),Is.False);var barrel=System.Linq.Enumerable.Single(runtime.SlayerMart.Players[0].Supports,p=>p.Kind==SlayerMartSupportKind.Barrel);Assert.That(barrel.Charges,Is.EqualTo(1));Assert.That(barrel.CooldownSeconds,Is.EqualTo(.2).Within(.0001));Assert.That(barrel.Phase,Is.EqualTo(.7));}
        [Test] public void RegionResourceFailureRetriesSameInventoryAndDeadline()
        {AtStore();Operate(SlayerMartAction.SelectEnter);view.RegionError="test resource unavailable";Step(100);Assert.That(runtime.CompletionAwaitingRetry,Is.True);Assert.That(runtime.SlayerMart.RemainingSeconds,Is.EqualTo(300));var method=typeof(ArcadeCombatSystem).GetMethod("TryRetrySlayerMartPreparation");Assert.That(method,Is.Not.Null);view.RegionError=null;Assert.That(method.Invoke(combat,null),Is.True);Step(0);Assert.That(runtime.SlayerMart.Ready,Is.True);Assert.That(runtime.SlayerMart.RemainingSeconds,Is.EqualTo(300));}
        [Test] public void ThirtyFirstActualRoundOpensSecondVisitWithElevenBoxes()
        {
            var model=architecture.GetModel<ArcadeRunModel>();var node=model.Current.Clone();var old=ArcadeSlayerMartRules.BeginVisit(node.RunId,11,false,new long[]{1},7,shopConfig,null);old.Ready=old.Closing=true;old.Players[0].Lives=3;old.Players[0].Cash=500;
            node.NextArenaNumber=8;node.CompletedArenaCount=7;node.Checkpoint.ArenaNumber=8;node.SlayerMart=old;node.RoundCheckpoint=null;Assert.That(Store().Save(node,out var error),Is.True,error);model.CommitSafeNode(node);model.EnterArena();runtime.ArenaNumber=8;runtime.SlayerMart=old.Clone();
            ExitRound(2);Assert.That(runtime.SlayerMart.Visit,Is.EqualTo(2));Assert.That(runtime.SlayerMart.Players[0].Cash,Is.EqualTo(500));Assert.That(shopConfig.Regions[1].Boxes.Count,Is.EqualTo(11));
        }
        [Test] public void GemsAndConsumedMarkerSaveTogetherAfterReturn()
        {Shopping();Offer(17);Operate(SlayerMartAction.Buy,item:17,operationId:"gems");long score=architecture.GetModel<ArcadeRunModel>().Current.Score;Assert.That(score,Is.GreaterThan(0));Operate(SlayerMartAction.Buy,item:17,operationId:"gems");Assert.That(architecture.GetModel<ArcadeRunModel>().Current.Score,Is.EqualTo(score));Operate(SlayerMartAction.SetFinished,value:true);Step(0);var saved=Store().Inspect();Assert.That(saved.IsValid,Is.True,saved.Error);Assert.That(saved.Data.Score,Is.EqualTo(score));Assert.That(saved.Data.SlayerMart.Players[0].AppliedBenefitGrants.Count,Is.EqualTo(1));}
        private ArcadeRunFileStore Store()=>new(directory);
        private void ExitRound(int wave)
        {
            Call("StartContentRound",runtime,0,null);
            for(int current=0;current<=wave;current++)
            {
                Assert.That(runtime.IslandTransitionPending,Is.False,"基础地图准备尚未完成");runtime.SpawnQueue.Clear();runtime.TreasureCompleted=true;Call("TickWaveTransition",runtime,0f);
                Assert.That(runtime.WaveClearAwarded,Is.True,"基础清场未完成");Assert.That(runtime.IslandRoundAwaitingExit,Is.True,"基础出口未建立");
                runtime.IslandExitPrompt=true;Call("ConfirmContentExit",runtime,0,current);
            }
        }
        private void AtStore(){ExitRound(2);Assert.That(runtime.SlayerMart,Is.Not.Null,"正式第11轮出口尚未挂接商店");}
        internal void Shopping(){AtStore();Operate(SlayerMartAction.SelectEnter);Step(0);Assert.That(runtime.SlayerMart.Ready,Is.True);}
        private void Step(float seconds){var method=typeof(ArcadeCombatSystem).GetMethod("TickSlayerMartRegion",Flags);Assert.That(method,Is.Not.Null,"缺少正式区域活动帧");method.Invoke(combat,new object[]{runtime,seconds});}
        private void Offer(int number)
        {var p=runtime.SlayerMart.Players[0];p.Cash=100000;string id="slayermart.item."+number.ToString("00");if(!p.Offers.Contains(id)){p.Offers.RemoveAt(0);p.Offers.Add(id);}}
        private void Operate(SlayerMartAction action,int item=0,int amount=0,bool value=false,int box=0,bool success=true,string operationId=null)
        {
            var s=runtime.SlayerMart;var request=new ArcadeSlayerMartRequest{Action=action,PlayerId=1,RegionInstanceId=s.RegionInstanceId,Generation=s.Generation,ExpectedRevision=s.Revision,OperationId=operationId??"test:"+(++operation),ItemId=item==0?null:"slayermart.item."+item.ToString("00"),ConfirmedAmount=amount,Value=value,BoxId=box};
            var method=typeof(ArcadeCombatSystem).GetMethod("TryOperateSlayerMart");Assert.That(method,Is.Not.Null,"缺少正式交易入口");object[] args={request,null};Assert.That(method.Invoke(combat,args),Is.EqualTo(success),args[1] as string);
        }
        internal sealed class View : IArcadeCombatViewPort
        {
            public bool Ready = true;
            public bool IsSlayerMartRegionReady=>Ready;
            public string RegionError;
            public string SlayerMartRegionLoadError=>RegionError;
            public void SetSlayerMartRegion(ArcadeSlayerMartRegionLayout layout,IReadOnlyList<int> remaining){}
            public bool FirstPerson;
            public int Round;
            public bool TreasureEntranceShown;
            public bool IsIslandRoundReady => Ready;
            public bool IsIslandTreasureReady => Ready;
            public bool IsFirstPersonActive => FirstPerson;
            public bool TryBeginFirstPerson(out string error) { FirstPerson = true; error = null; return true; }
            public bool TryReadFirstPersonAim(out Vector2 aim) { aim = Vector2.up; return FirstPerson; }
            public void EndFirstPerson() { FirstPerson = false; }
            public void SetIslandRound(int number) { Round = number; }
            public void SetTreasureRoom(ArcadeTreasureRoomParameters p, bool inRoom, bool discovered, bool open, IReadOnlyList<int> remainingIds)
                { TreasureEntranceShown = !inRoom && discovered; }
            public bool IsActive => true;
            public void Begin(int n, float w, float h, IReadOnlyList<ArcadeObstacleDefinition> o) { }
            public void End() { }
            public bool TryScreenToArena(Vector2 s, out Vector2 p) { p = default; return false; }
            public void SetPlayer(Vector2 p, Vector2 f, bool v, bool i) { }
            public void SetPlayerHealth(int c, int m) { }
            public void SetEnemy(int id, string config, Vector2 p, int c, int m, bool telegraph = false, int attackSequence = 0, bool persistentHealthBar = false) { }
            public void RemoveEnemy(int id, bool playDeath = false) { }
            public void SetProjectile(int id, bool hostile, Vector2 p, Vector2 d) { }
            public void RemoveProjectile(int id) { }
            public void SetDrop(int id, ArcadeDropDefinition d, Vector2 p) { }
            public void RemoveDrop(int id) { }
            public void SetRemotePlayer(long id, Vector2 p, Vector2 f, bool v, bool i, int c, int m) { }
            public void RemoveRemotePlayer(long id) { }
            public void TickPlayerFeedback(float s) { }
        }
    }
}
