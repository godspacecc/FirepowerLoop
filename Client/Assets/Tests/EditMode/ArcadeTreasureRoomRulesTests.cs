using System;
using System.IO;
using System.Linq;
using AnotherDay.Arcade;
using Luban;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>正式宝藏配置与共用规则验收，不启动整局或网络服务。</summary>
    public sealed class ArcadeTreasureRoomRulesTests
    {
        [Test] public void GroundRewards_ExpireThirtySecondsAfterFirstEntryWithoutClosingRoom()
        {
            var room = Entered();
            room.Advance(29.5f);
            Assert.That(room.RemainingRewards, Is.Not.Empty);
            room.Advance(.5f);
            Assert.That(room.RemainingRewards, Is.Empty);
            Assert.That(room.Expired, Is.False, "物资过期不能提前关闭房间或出口");
            Assert.That(room.TryCollect(room.InstanceId, 1, 1, _ => true, out _), Is.False);
        }
        private static ArcadeTreasureRoomParameters Rules() => ArcadeTreasureRoomParameters.FromConfig(
            new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",name+".bytes"))))
                .TbArcadeTreasureRoom.Get("arcade.treasure.first"));
        private static ArcadeTreasureRoomState Entered()
        {
            var room = new ArcadeTreasureRoomState("run:1:3:first", Rules());
            Assert.That(room.TryEnter(room.InstanceId, 1, true, true, out _), Is.True);
            return room;
        }
        [Test] public void FormalRewards_AreOneFixedSharedSet()
        {
            var p = Rules();
            Assert.That(p.ArenaConfigNumber, Is.EqualTo(1)); Assert.That(p.WaveNumber, Is.EqualTo(3));
            Assert.That(p.Duration, Is.EqualTo(60)); Assert.That(p.Rewards.Count, Is.EqualTo(18));
            Assert.That(p.Rewards.Count(r=>r.Kind==AnotherDay.Arcade.ArcadeTreasureRewardKind.GoldBar), Is.EqualTo(12));
            Assert.That(p.Rewards.Count(r=>r.Kind==AnotherDay.Arcade.ArcadeTreasureRewardKind.Gem), Is.EqualTo(4));
            Assert.That(p.Rewards.Select(r=>r.Id).Distinct().Count(), Is.EqualTo(18));
        }
        [Test] public void RoomCoordinatesMustFitArenaAndPlayerRadius()
        {
            var method = typeof(ArcadeTreasureRoomParameters).GetMethod("FitsArena");
            Assert.That(method, Is.Not.Null);
            Assert.That(method.Invoke(Rules(), new object[] { 20f, 20f, .5f }), Is.True);
            Assert.That(method.Invoke(Rules(), new object[] { 10f, 10f, .5f }), Is.False);
            Assert.That(method.Invoke(Rules(), new object[] { 20f, 20f, 9f }), Is.False);
            Assert.That(method.Invoke(Rules(), new object[] { float.NaN, 20f, .5f }), Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void LateEntryAndReentry_DoNotResetSharedClock(bool leaveAll)
        {
            var r=Entered(); r.Advance(20);
            if(leaveAll) Assert.That(r.TryLeave(r.InstanceId,1,out _),Is.True);
            r.Advance(10); Assert.That(r.TryEnter(r.InstanceId,2,true,true,out _),Is.True);
            Assert.That(r.TryEnter(r.InstanceId,1,true,true,out _),Is.True);
            Assert.That(r.RemainingSeconds,Is.EqualTo(30));
            Assert.That(r.RemainingRewards,Is.Empty, "晚进入或重新进入不能重新生成已到期的地面奖励");
            Assert.That(r.RewardRemainingSeconds,Is.Zero);
        }
        [Test] public void Timeout_ReturnsOnlyRemainingMembersOnce()
        {
            var r=Entered();r.TryEnter(r.InstanceId,2,true,true,out _);r.TryLeave(r.InstanceId,1,out _);
            Assert.That(r.Advance(60),Is.EqualTo(new long[]{2}));Assert.That(r.Advance(1),Is.Empty);
            Assert.That(r.TryEnter(r.InstanceId,1,true,true,out _),Is.False);
            Assert.That(r.TryCollect(r.InstanceId,2,1,_=>true,out _),Is.False);
        }
        [Test] public void LeavingOneMember_DoesNotMoveOthers()
        {
            var r=Entered();r.TryEnter(r.InstanceId,2,true,true,out _);r.TryLeave(r.InstanceId,1,out _);
            Assert.That(r.Members,Is.EqualTo(new long[]{2}));Assert.That(r.Contains(2),Is.True);
        }
        [Test] public void SharedClaim_AppliesExactlyOnceAcrossPlayers()
        {
            var r=Entered();r.TryEnter(r.InstanceId,2,true,true,out _);int applied=0;
            Assert.That(r.TryCollect(r.InstanceId,1,1,_=>{applied++;return true;},out _),Is.True);
            Assert.That(r.TryCollect(r.InstanceId,2,1,_=>{applied++;return true;},out _),Is.False);
            Assert.That(applied,Is.EqualTo(1));Assert.That(r.RemainingRewards.Length,Is.EqualTo(17));
        }
        [Test] public void FailedApply_LeavesSharedRewardAvailable()
        {
            var r=Entered();Assert.That(r.TryCollect(r.InstanceId,1,1,_=>false,out _),Is.False);
            Assert.That(r.TryCollect(r.InstanceId,1,1,_=>true,out _),Is.True);
        }
        [Test] public void ThrowingApply_LeavesRewardAndUnlocksGuard()
        {
            var r=Entered();Assert.Throws<InvalidOperationException>(()=>r.TryCollect(r.InstanceId,1,1,_=>throw new InvalidOperationException(),out _));
            Assert.That(r.TryCollect(r.InstanceId,1,1,_=>true,out _),Is.True);
        }
        [Test] public void ReentrantClaim_CannotApplySameRewardAgain()
        {
            var r=Entered();Assert.That(r.TryCollect(r.InstanceId,1,1,reward=>{
                Assert.That(r.TryCollect(r.InstanceId,1,1,__=>true,out _),Is.False);return true;},out _),Is.True);
        }
        [TestCase("old",1,1)] [TestCase("run:1:3:first",2,1)] [TestCase("run:1:3:first",1,99)]
        public void InvalidClaim_DoesNotInvokeApply(string instance,long player,int item)
        {
            var r=Entered();Assert.That(r.TryCollect(instance,player,item,_=>throw new Exception("不能调用"),out _),Is.False);
            Assert.That(r.RemainingRewards.Length,Is.EqualTo(18));
        }
        [TestCase(false,true)] [TestCase(true,false)] [TestCase(false,false)]
        public void Entry_RequiresDiscoveryAndClear(bool found,bool cleared)
        {var r=new ArcadeTreasureRoomState("x",Rules());Assert.That(r.TryEnter("x",1,found,cleared,out _),Is.False);Assert.That(r.Started,Is.False);}
        [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void InvalidDelta_DoesNotChangeClock(float delta)
        {var r=Entered();Assert.Throws<ArgumentOutOfRangeException>(()=>r.Advance(delta));Assert.That(r.RemainingSeconds,Is.EqualTo(60));}
        [Test] public void BeforeFirstEntry_ClockDoesNotStart()
        {var r=new ArcadeTreasureRoomState("x",Rules());r.Advance(1000);Assert.That(r.RemainingSeconds,Is.EqualTo(60));Assert.That(r.Expired,Is.False);}
        [Test] public void Gold_UsesOldMultiplierBeforeLevelGrowth()
        {
            Assert.That(ArcadeTreasureScoreRules.TryAward(new ArcadeTreasureScoreState(100,2,900,3),Rules().Rewards[0],Rules(),out var n),Is.True);
            Assert.That(n.Score,Is.EqualTo(300));Assert.That(n.Multiplier,Is.EqualTo(3));Assert.That(n.Progress,Is.Zero);
        }
        [Test] public void Gem_Uses250PointsAndCarriesProgress()
        {
            Assert.That(ArcadeTreasureScoreRules.TryAward(new ArcadeTreasureScoreState(0,1,900,3),Rules().Rewards[12],Rules(),out var n),Is.True);
            Assert.That(n.Score,Is.EqualTo(250));Assert.That(n.Multiplier,Is.EqualTo(2));Assert.That(n.Progress,Is.EqualTo(150));
        }
        [Test] public void ScoreTier_AwardsOnlyCrossedLifeThreshold()
        {
            var p=Rules();ArcadeTreasureScoreRules.TryAward(new ArcadeTreasureScoreState(199900,1,0,3),p.Rewards[0],p,out var n);
            Assert.That(n.Score,Is.EqualTo(200000));Assert.That(n.Lives,Is.EqualTo(4));
            ArcadeTreasureScoreRules.TryAward(n,p.Rewards[0],p,out var again);Assert.That(again.Lives,Is.EqualTo(4));
        }
        [Test] public void FullLives_DoesNotQueueOrCompensate()
        {
            var p=Rules();ArcadeTreasureScoreRules.TryAward(new ArcadeTreasureScoreState(199900,1,0,9),p.Rewards[0],p,out var n);
            Assert.That(n.Lives,Is.EqualTo(9));ArcadeTreasureScoreRules.TryAward(new ArcadeTreasureScoreState(n.Score,1,0,8),p.Rewards[0],p,out var later);
            Assert.That(later.Lives,Is.EqualTo(8));
        }
        [Test] public void OldExcessLives_AreNotReduced()
        {ArcadeTreasureScoreRules.TryAward(new ArcadeTreasureScoreState(199900,1,0,12),Rules().Rewards[0],Rules(),out var n);Assert.That(n.Lives,Is.EqualTo(12));}
        [Test] public void ScoreOverflow_RejectsWholeCandidate()
        {
            var s=new ArcadeTreasureScoreState(long.MaxValue,1,0,3);
            Assert.That(ArcadeTreasureScoreRules.TryAward(s,Rules().Rewards[0],Rules(),out var n),Is.False);Assert.That(n.Score,Is.EqualTo(s.Score));Assert.That(n.Lives,Is.EqualTo(3));
        }
        [Test] public void Death_ResetsMultiplierButKeepsCumulativeScore()
        {var n=new ArcadeTreasureScoreState(230000,4,900,2).ResetAfterDeath();Assert.That(n.Score,Is.EqualTo(230000));Assert.That(n.Multiplier,Is.EqualTo(1));Assert.That(n.Progress,Is.Zero);}
        [Test] public void MultiplierCap_DiscardsFurtherProgress()
        {ArcadeTreasureScoreRules.TryAward(new ArcadeTreasureScoreState(0,9,0,3),Rules().Rewards[12],Rules(),out var n);Assert.That(n.Score,Is.EqualTo(2250));Assert.That(n.Multiplier,Is.EqualTo(9));Assert.That(n.Progress,Is.Zero);}
        [Test] public void NewInstance_AfterRollbackHasIndependentLedger()
        {var r=Entered();r.TryCollect(r.InstanceId,1,1,_=>true,out _);var fresh=Entered();Assert.That(fresh.RemainingRewards.Length,Is.EqualTo(18));Assert.That(fresh.RemainingSeconds,Is.EqualTo(60));}

        [Test]
        public void SharedRoomAuthority_Exists()
        {
            Assert.That(typeof(ArcadeCombatSystem).Assembly.GetType("AnotherDay.Arcade.ArcadeTreasureRoomState"),
                Is.Not.Null, "尚未实现共享区域权威状态");
        }

        [Test]
        public void FormalCatalog_ContainsFirstRoom()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            Assert.That(catalog.GetType().GetProperty("TreasureRooms"), Is.Not.Null,
                "正式目录尚未提供宝藏房配置");
        }
    }
}
