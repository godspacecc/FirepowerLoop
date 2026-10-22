using FirepowerLoop.Arcade;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>牛群锁向、活动时钟及完整扫掠契约；不加载地图、不写正式资源。</summary>
    public sealed class ArcadeHerdRulesTests
    {
        private ArcadeHerdTuning T=>new ArcadeHerdTuning(.8f,12,36,.65f);
        private ArcadeHerdState Start()
        {Assert.That(ArcadeHerdRules.TryStart(10,1,-18,0,3,0,T,out var s),Is.True);return s;}
        [Test]public void WindupLocksNormalizedDirectionAndDoesNotMoveOrHit()
        {
            var s=Start();Assert.That(s.Phase,Is.EqualTo(ArcadeHerdPhase.Windup));Assert.That(s.DirectionX,Is.EqualTo(1));Assert.That(s.DirectionZ,Is.Zero);
            var sweep=ArcadeHerdRules.Step(s,T,.7f,true);Assert.That(sweep.Active,Is.False);Assert.That(s.X,Is.EqualTo(-18));
            Assert.That(ArcadeHerdRules.TryContact(s,sweep,10,-1,-18,0,.4f,true),Is.False);
        }
        [Test]public void CrossingWindupBoundaryUsesOnlyChargePortionOfFrame()
        {
            var s=Start();var sweep=ArcadeHerdRules.Step(s,T,1,true);
            Assert.That(s.Phase,Is.EqualTo(ArcadeHerdPhase.Charging));Assert.That(s.X,Is.EqualTo(-15.6f).Within(.001f));
            Assert.That(sweep.FromX,Is.EqualTo(-18));Assert.That(sweep.ToX,Is.EqualTo(s.X));
        }
        [Test]public void FastCrossingSweepsMiddleAndBothCapsWithoutEndpointTunnelling()
        {
            var s=Start();var sweep=ArcadeHerdRules.Step(s,T,2.8f,true);
            Assert.That(ArcadeHerdRules.TryContact(s,sweep,10,11,-6,.9f,.3f,true),Is.True);
            Assert.That(ArcadeHerdRules.TryContact(s,sweep,10,12,-18.9f,0,.3f,true),Is.True);
            Assert.That(ArcadeHerdRules.TryContact(s,sweep,10,13,6.9f,0,.3f,true),Is.True);
            Assert.That(ArcadeHerdRules.TryContact(s,sweep,10,14,-6,1.1f,.3f,true),Is.False);
        }
        [Test]public void PlayerAndEnemyIdentitiesAreIndependentAndContactIsOncePerCharge()
        {
            var s=Start();var sweep=ArcadeHerdRules.Step(s,T,2,true);
            Assert.That(ArcadeHerdRules.TryContact(s,sweep,10,-1,-10,0,.4f,true),Is.True);
            Assert.That(ArcadeHerdRules.TryContact(s,sweep,10,1,-10,0,.4f,true),Is.True);
            Assert.That(ArcadeHerdRules.TryContact(s,sweep,10,-1,-10,0,.4f,true),Is.False);
            sweep=ArcadeHerdRules.Step(s,T,.1f,true);Assert.That(ArcadeHerdRules.TryContact(s,sweep,10,1,s.X,0,.4f,true),Is.False);
        }
        [Test]public void PauseInvalidStepsAndDeadTargetsDoNotProduceContactsOrCatchup()
        {
            var s=Start();var old=ArcadeHerdRules.Step(s,T,1,true);var paused=ArcadeHerdRules.Step(s,T,90,false);
            Assert.That(paused.Active,Is.False);Assert.That(s.ElapsedSeconds,Is.EqualTo(1).Within(.0001));
            Assert.That(ArcadeHerdRules.TryContact(s,old,10,1,-17,0,.4f,true),Is.False);
            foreach(var dt in new[]{0,-1,float.NaN,float.PositiveInfinity})Assert.That(ArcadeHerdRules.Step(s,T,dt,true).Active,Is.False);
            Assert.That(s.X,Is.EqualTo(-15.6f).Within(.001f));var step=ArcadeHerdRules.Step(s,T,.1f,true);
            Assert.That(ArcadeHerdRules.TryContact(s,step,10,2,s.X,0,.4f,false),Is.False);
            Assert.That(ArcadeHerdRules.TryContact(s,step,10,2,s.X,0,.4f,true),Is.True);
        }
        [Test]public void MapChangeCancelAndNewerStepRejectStaleSweeps()
        {
            var s=Start();var old=ArcadeHerdRules.Step(s,T,1,true);
            Assert.That(ArcadeHerdRules.TryContact(s,old,11,1,-17,0,.4f,true),Is.False);
            ArcadeHerdRules.Step(s,T,.1f,true);Assert.That(ArcadeHerdRules.TryContact(s,old,10,1,-17,0,.4f,true),Is.False);
            var next=ArcadeHerdRules.Step(s,T,.1f,true);ArcadeHerdRules.Cancel(s);
            Assert.That(s.Phase,Is.EqualTo(ArcadeHerdPhase.Expired));Assert.That(ArcadeHerdRules.TryContact(s,next,10,1,s.X,0,.4f,true),Is.False);
        }
        [Test]public void HugeStepExpiresAtConfiguredDistanceButStillReportsFinalCrossingOnce()
        {
            var s=Start();var sweep=ArcadeHerdRules.Step(s,T,1000,true);
            Assert.That(s.Phase,Is.EqualTo(ArcadeHerdPhase.Expired));Assert.That(s.X,Is.EqualTo(18).Within(.001f));
            Assert.That(ArcadeHerdRules.TryContact(s,sweep,10,1,0,0,.4f,true),Is.True);
            Assert.That(ArcadeHerdRules.Step(s,T,1,true).Active,Is.False);
            Assert.That(ArcadeHerdRules.TryContact(s,sweep,10,2,0,0,.4f,true),Is.False);
        }
        [Test]public void InvalidIdentityDirectionTuningAndTargetCoordinatesAreRejected()
        {
            foreach(var t in new[]{new ArcadeHerdTuning(-1,12,36,.65f),new ArcadeHerdTuning(.8f,0,36,.65f),new ArcadeHerdTuning(.8f,12,float.NaN,.65f),new ArcadeHerdTuning(.8f,12,36,-1)})
                Assert.That(ArcadeHerdRules.TryStart(10,1,0,0,1,0,t,out _),Is.False);
            Assert.That(ArcadeHerdRules.TryStart(0,1,0,0,1,0,T,out _),Is.False);Assert.That(ArcadeHerdRules.TryStart(10,0,0,0,1,0,T,out _),Is.False);
            Assert.That(ArcadeHerdRules.TryStart(10,1,0,0,0,0,T,out _),Is.False);Assert.That(ArcadeHerdRules.TryStart(10,1,float.NaN,0,1,0,T,out _),Is.False);
            var s=Start();var sweep=ArcadeHerdRules.Step(s,T,1,true);
            Assert.That(ArcadeHerdRules.TryContact(s,sweep,10,0,s.X,0,.4f,true),Is.False);Assert.That(ArcadeHerdRules.TryContact(s,sweep,10,1,float.NaN,0,.4f,true),Is.False);
        }
    }
}
