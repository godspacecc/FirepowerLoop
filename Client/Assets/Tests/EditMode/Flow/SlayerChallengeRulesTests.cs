using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>首批完整 Slayer 的同源阶段、容错、退出与现金规则，仅此明确类范围。</summary>
    public sealed class SlayerChallengeRulesTests
    {
        private static Type Type(string name){var t=typeof(ArcadeRunModel).Assembly.GetType("FirepowerLoop.Arcade.ArcadeSlayerChallenge"+name);Assert.That(t,Is.Not.Null,"完整Slayer同源规则尚未实现："+name);return t;}
        private static object Config()=>Type("Configuration").GetMethod("FromTables").Invoke(null,new object[]{new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))))});
        private static object Call(string method,params object[] args)=>Type("Rules").GetMethod(method).Invoke(null,args);
        private static object Get(object state,string key)=>state.GetType().GetProperty(key).GetValue(state);
        private static int Phase(object state)=>Convert.ToInt32(Get(state,"Phase"));
        private static object Create(object c)=>Call("Create",c,"test-run",new long[]{1,2},4,5,1);
        private static object Enter(object c){var s=Create(c);s=Call("SetEntryChoice",s,c,1L,true);return Call("SetEntryChoice",s,c,2L,true);}
        private static object Boss(object c)=>Call("Advance",Enter(c),c,90d,true);
        [Test] public void FormalConfigurationHasConfirmedAndProjectFirstValues(){var c=Config();Assert.That(Get(c,"PreparationSeconds"),Is.EqualTo(1200f));Assert.That(Get(c,"CollectionSeconds"),Is.EqualTo(90f));Assert.That(Get(c,"DownLimit"),Is.EqualTo(4));Assert.That(Get(c,"CoinAmount"),Is.EqualTo(100));}
        [Test] public void EntryRetainsMainReturnIdentity(){var s=Create(Config());Assert.That(Phase(s),Is.EqualTo(1));Assert.That(Get(s,"ReturnArena"),Is.EqualTo(4));Assert.That(Get(s,"ReturnMap"),Is.EqualTo(5));}
        [Test] public void BothMembersMustChooseEntry(){var c=Config();var s=Call("SetEntryChoice",Create(c),c,1L,true);Assert.That(Phase(s),Is.EqualTo(1));s=Call("SetEntryChoice",s,c,2L,true);Assert.That(Phase(s),Is.EqualTo(2));}
        [Test] public void UnanimousSkipReturnsWithoutWinning(){var c=Config();var s=Call("SetEntryChoice",Create(c),c,1L,false);s=Call("SetEntryChoice",s,c,2L,false);Assert.That(Phase(s),Is.EqualTo(5));Assert.That(Get(s,"Outcome").ToString(),Is.EqualTo("Skipped"));}
        [Test] public void PreparationTimeoutSkipsChallenge(){var c=Config();Assert.That(Phase(Call("Advance",Create(c),c,1200d,true)),Is.EqualTo(5));}
        [Test] public void PausedStageNeverConsumesTime(){var c=Config();var s=Enter(c);var next=Call("Advance",s,c,1000d,false);Assert.That(Get(next,"RemainingSeconds"),Is.EqualTo(90d));}
        [Test] public void CollectionTimeoutStartsBossWithoutVictory(){var c=Config();var s=Boss(c);Assert.That(Phase(s),Is.EqualTo(3));Assert.That(Get(s,"Outcome").ToString(),Is.EqualTo("None"));}
        [Test] public void FirstThreeDownsKeepChallengeAlive(){var c=Config();var s=Enter(c);for(int i=0;i<3;i++)s=Call("RecordDown",s,c);Assert.That(Phase(s),Is.EqualTo(2));Assert.That(Get(s,"TeamDowns"),Is.EqualTo(3));}
        [Test] public void FourthDownFailsWholeChallenge(){var c=Config();var s=Enter(c);for(int i=0;i<4;i++)s=Call("RecordDown",s,c);Assert.That(Phase(s),Is.EqualTo(5));Assert.That(Get(s,"Outcome").ToString(),Is.EqualTo("Failed"));}
        [Test] public void FinishedChallengeIgnoresFurtherDowns(){var c=Config();var s=Enter(c);for(int i=0;i<8;i++)s=Call("RecordDown",s,c);Assert.That(Get(s,"TeamDowns"),Is.EqualTo(4));}
        [Test] public void ExitNeedsBothOriginalMembers(){var c=Config();var s=Call("SetExitVote",Enter(c),1L,true);Assert.That(Phase(s),Is.EqualTo(2));s=Call("SetExitVote",s,2L,true);Assert.That(Phase(s),Is.EqualTo(5));Assert.That(Get(s,"Outcome").ToString(),Is.EqualTo("Abandoned"));}
        [Test] public void ExitVoteCanBeCancelled(){var c=Config();var s=Call("SetExitVote",Enter(c),1L,true);s=Call("SetExitVote",s,1L,false);s=Call("SetExitVote",s,2L,true);Assert.That(Phase(s),Is.EqualTo(2));}
        [Test] public void CollectionCannotPretendBossVictory(){var c=Config();Assert.Throws<TargetInvocationException>(()=>Call("BossDefeated",Enter(c),c));}
        [Test] public void BossVictoryOpensTimedShop(){var c=Config();var s=Call("BossDefeated",Boss(c),c);Assert.That(Phase(s),Is.EqualTo(4));Assert.That(Get(s,"RemainingSeconds"),Is.EqualTo(300d));Assert.That(Get(s,"Outcome").ToString(),Is.EqualTo("Won"));}
        [Test] public void ShopTimeoutReturnsWonMainline(){var c=Config();var s=Call("BossDefeated",Boss(c),c);s=Call("Advance",s,c,300d,true);Assert.That(Phase(s),Is.EqualTo(5));Assert.That(Get(s,"Outcome").ToString(),Is.EqualTo("Won"));}
        [Test] public void CoinIdentityCannotBeCollectedTwice(){var c=Config();var s=Call("CollectCoin",Enter(c),c,10L);s=Call("CollectCoin",s,c,10L);Assert.That(Get(s,"CashCollected"),Is.EqualTo(100L));}
        [Test] public void CollectionCashHasFiniteConfiguredCap(){var c=Config();var s=Enter(c);for(long i=1;i<=250;i++)s=Call("CollectCoin",s,c,i);Assert.That(Get(s,"CashCollected"),Is.EqualTo(20000L));}
        [Test] public void CashSurvivesAbandonWithoutBossBonus(){var c=Config();var s=Call("CollectCoin",Enter(c),c,1L);s=Call("SetExitVote",s,1L,true);s=Call("SetExitVote",s,2L,true);Assert.That(Call("UnclaimedCash",s,c),Is.EqualTo(100L));}
        [Test] public void WonCashGrantIsClaimedOnce(){var c=Config();var s=Call("BossDefeated",Boss(c),c);Assert.That(Call("UnclaimedCash",s,c),Is.EqualTo(5000L));s=Call("ClaimCash",s);Assert.That(Call("UnclaimedCash",s,c),Is.EqualTo(0L));}
        [Test] public void InvalidClockDeltaRejectsWithoutMutatingSource(){var c=Config();var s=Enter(c);Assert.Throws<TargetInvocationException>(()=>Call("Advance",s,c,double.NaN,true));Assert.That(Get(s,"RemainingSeconds"),Is.EqualTo(90d));}
    }
}
