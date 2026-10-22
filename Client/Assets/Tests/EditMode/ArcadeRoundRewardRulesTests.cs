using System.Linq;
using FirepowerLoop.Arcade;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>每轮一次奖励、完整特效门禁与离场放弃规则；不依赖未确认资源。</summary>
    public sealed class ArcadeRoundRewardRulesTests
    {
        private ArcadeRoundRewardTuning T=>new ArcadeRoundRewardTuning(1.1f,.4f,1.8f,30,.75f,18,6,10,50,.75f,.32f,5);
        private ArcadeRoundRewardState Start(int round=1){ArcadeRoundRewardState s=null;Assert.That(ArcadeRoundRewardRules.TryStart(ref s,T,round,0,0),Is.True);return s;}
        [Test]public void SameRoundCannotRestartOrDuplicateRewards()
        {
            var s=Start();ArcadeRoundRewardRules.Advance(s,T,1);
            Assert.That(ArcadeRoundRewardRules.TryStart(ref s,T,1,4,5),Is.False);
            Assert.That(s.ElapsedSeconds,Is.EqualTo(1));Assert.That(s.Items.Count,Is.EqualTo(24));
            Assert.That(s.Items.Select(i=>i.Id).Distinct().Count(),Is.EqualTo(24));
        }
        [Test]public void EveryItemWaitsUntilEntirePresentationEnds()
        {
            var s=Start();var item=s.Items[0];ArcadeRoundRewardRules.Advance(s,T,T.PresentationSeconds-.01f);
            Assert.That(ArcadeRoundRewardRules.TryCollect(s,T,item.Id,item.X,item.Z,true,out _),Is.False);
            ArcadeRoundRewardRules.Advance(s,T,.02f);
            Assert.That(ArcadeRoundRewardRules.TryCollect(s,T,item.Id,item.X,item.Z,true,out _),Is.True);
        }
        [Test]public void EachItemCanBeAwardedOnlyOnceAcrossPlayers()
        {
            var s=Start();ArcadeRoundRewardRules.Advance(s,T,4);var item=s.Items[0];
            Assert.That(ArcadeRoundRewardRules.TryCollect(s,T,item.Id,item.X,item.Z,true,out var award),Is.True);
            Assert.That(award.Value,Is.EqualTo(item.Value));
            Assert.That(ArcadeRoundRewardRules.TryCollect(s,T,item.Id,item.X,item.Z,true,out _),Is.False);
        }
        [Test]public void DeadDistantAndInvalidPlayersCannotCollect()
        {
            var s=Start();ArcadeRoundRewardRules.Advance(s,T,4);var item=s.Items[0];
            Assert.That(ArcadeRoundRewardRules.TryCollect(s,T,item.Id,item.X,item.Z,false,out _),Is.False);
            Assert.That(ArcadeRoundRewardRules.TryCollect(s,T,item.Id,item.X+4,item.Z,true,out _),Is.False);
            Assert.That(ArcadeRoundRewardRules.TryCollect(s,T,item.Id,float.NaN,item.Z,true,out _),Is.False);
            Assert.That(s.Collected,Is.Empty);
        }
        [Test]public void PauseAndInvalidStepsDoNotAdvanceOrReleaseItems()
        {
            var s=Start();foreach(float dt in new[]{0,-1,float.NaN,float.PositiveInfinity})ArcadeRoundRewardRules.Advance(s,T,dt);
            Assert.That(s.ElapsedSeconds,Is.Zero);Assert.That(s.Phase,Is.EqualTo(ArcadeRoundRewardPhase.Falling));
        }
        [Test]public void LifetimeStartsAfterPresentationAndLargeStepExpires()
        {
            var s=Start();ArcadeRoundRewardRules.Advance(s,T,2);Assert.That(s.GroundRemainingSeconds,Is.EqualTo(30));
            ArcadeRoundRewardRules.Advance(s,T,3);Assert.That(s.GroundRemainingSeconds,Is.EqualTo(30-(5-T.PresentationSeconds)).Within(.001f));
            ArcadeRoundRewardRules.Advance(s,T,100);Assert.That(s.Phase,Is.EqualTo(ArcadeRoundRewardPhase.Expired));
            var item=s.Items[0];Assert.That(ArcadeRoundRewardRules.TryCollect(s,T,item.Id,item.X,item.Z,true,out _),Is.False);
        }
        [Test]public void LeavingAllowsFreshNextRoundWithoutClaimingLeftovers()
        {
            var s=Start();var previous=s;
            Assert.That(ArcadeRoundRewardRules.TryStart(ref s,T,2,3,4),Is.True);
            Assert.That(s,Is.Not.SameAs(previous));Assert.That(previous.Collected,Is.Empty);
            Assert.That(s.RoundIdentity,Is.EqualTo(2));Assert.That(s.Collected,Is.Empty);
        }
        [Test]public void FixedScatterContainsBothKindsAndConservesConfiguredValue()
        {
            var a=Start();var b=Start();Assert.That(a.Items.Select(i=>(i.Id,i.X,i.Z)),Is.EqualTo(b.Items.Select(i=>(i.Id,i.X,i.Z))));
            Assert.That(a.Items.Count(i=>i.GoldBar),Is.EqualTo(6));Assert.That(a.Items.Sum(i=>i.Value),Is.EqualTo(480));
            Assert.That(a.Items.All(i=>i.X*i.X+i.Z*i.Z<=2.1f*2.1f),Is.True);
        }
    }
}
