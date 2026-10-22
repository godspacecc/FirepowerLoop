using FirepowerLoop.Arcade;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>无人机独立规则契约；不创建正式视觉或修改正式配置。</summary>
    public sealed class ArcadeDroneRulesTests
    {
        private readonly ArcadeDroneTuning tuning=new ArcadeDroneTuning(25,10,.85f,.35f,12,.45f,19,1.5f,1.2f,16,60,2.5f,.7f);
        private ArcadeDroneState State(float remaining=25)=>new ArcadeDroneState{RemainingSeconds=remaining,X=.85f,Z=.35f};
        private ArcadeDroneStep Step(ArcadeDroneState s,float dt=.1f,int tier=1,bool fire=true,params ArcadeDroneTarget[] enemies)
            =>ArcadeDroneRules.Step(s,tuning,dt,0,0,tier,true,fire,enemies);
        [Test] public void FollowUsesOwnerOffsetAndConsumesOnlyActiveTime()
        {
            var s=State();ArcadeDroneRules.Step(s,tuning,.5f,3,4,1,true,true,new ArcadeDroneTarget[0]);
            Assert.That(s.X,Is.EqualTo(3.85f).Within(.001f));Assert.That(s.Z,Is.EqualTo(4.35f).Within(.001f));Assert.That(s.RemainingSeconds,Is.EqualTo(24.5f));
        }
        [Test] public void NearestTargetAndStableIdentityTieDetermineShot()
        {
            var s=State();var shot=Step(s,enemies:new[]{new ArcadeDroneTarget(3,2.85f,.35f),new ArcadeDroneTarget(2,2.85f,.35f),new ArcadeDroneTarget(1,8,.35f)});
            Assert.That(shot.Shot,Is.True);Assert.That(shot.TargetId,Is.EqualTo(2));Assert.That(shot.ShotDamage,Is.EqualTo(12));
        }
        [Test] public void CurrentPlayerTierControlsDamageAndRateThenReverts()
        {
            var s=State();var enemies=new[]{new ArcadeDroneTarget(1,3,0)};
            foreach(int tier in new[]{1,2,3,1})
            {s.FireCooldownSeconds=0;var shot=Step(s,tier:tier,enemies:enemies);Assert.That(shot.ShotDamage,Is.EqualTo(12*tier));Assert.That(s.FireCooldownSeconds,Is.EqualTo(.45f/tier).Within(.0001f));}
        }
        [Test] public void FullProjectileBudgetDoesNotFakeShotOrSpendCooldown()
        {
            var s=State();var enemies=new[]{new ArcadeDroneTarget(1,3,0)};
            Assert.That(Step(s,fire:false,enemies:enemies).Shot,Is.False);Assert.That(s.FireCooldownSeconds,Is.Zero);
            Assert.That(Step(s,enemies:enemies).Shot,Is.True);
        }
        [Test] public void NoEnemyExpiresQuietly()
        {var s=State(.05f);Assert.That(Step(s).Exploded,Is.False);Assert.That(s.Phase,Is.EqualTo(ArcadeDronePhase.Expired));}
        [Test] public void OutsideRangeCannotTriggerRamOrExplosion()
        {var s=State(.05f);var result=Step(s,enemies:new[]{new ArcadeDroneTarget(1,20,0)});Assert.That(result.Exploded,Is.False);Assert.That(s.Phase,Is.EqualTo(ArcadeDronePhase.Expired));}
        [Test] public void InRangeRamProducesOneStrengthenedExplosion()
        {
            var s=State(1.25f);var targets=new[]{new ArcadeDroneTarget(7,2.85f,.35f)};
            var first=Step(s,.1f,3,enemies:targets);Assert.That(s.Phase,Is.EqualTo(ArcadeDronePhase.Ramming));Assert.That(first.Exploded,Is.False);
            var impact=Step(s,.1f,3,enemies:targets);Assert.That(impact.Exploded,Is.True);Assert.That(impact.ExplosionDamage,Is.EqualTo(180));
            Assert.That(s.Phase,Is.EqualTo(ArcadeDronePhase.Exploding));Assert.That(Step(s,.1f,3,enemies:targets).Exploded,Is.False);
        }
        [Test] public void LostRamTargetExpiresWithoutEmptyExplosion()
        {var s=State(1.25f);Step(s,enemies:new[]{new ArcadeDroneTarget(7,8,0)});Assert.That(s.Phase,Is.EqualTo(ArcadeDronePhase.Ramming));Assert.That(Step(s).Exploded,Is.False);Assert.That(s.Phase,Is.EqualTo(ArcadeDronePhase.Expired));}
        [Test] public void ZeroAndInvalidStepsCannotAdvanceOrFire()
        {
            var s=State();foreach(float dt in new[]{0f,-1f,float.NaN,float.PositiveInfinity})Assert.That(Step(s,dt,enemies:new[]{new ArcadeDroneTarget(1,3,0)}).Shot,Is.False);
            Assert.That(s.RemainingSeconds,Is.EqualTo(25));Assert.That(s.Phase,Is.EqualTo(ArcadeDronePhase.Following));
        }
        [Test] public void LargeStepCannotLeaveAnExpiredDroneAlive()
        {var s=State();Assert.That(Step(s,60).Exploded,Is.False);Assert.That(s.Phase,Is.EqualTo(ArcadeDronePhase.Expired));Assert.That(Step(s,60).Exploded,Is.False);}
        [Test] public void MovingTargetCannotKeepRammingPastLifetime()
        {
            var s=State(.3f);var targets=new[]{new ArcadeDroneTarget(1,10,0)};
            Step(s,.1f,enemies:targets);var result=Step(s,.3f,enemies:new[]{new ArcadeDroneTarget(1,30,0)});
            Assert.That(result.Exploded,Is.False);Assert.That(s.Phase,Is.EqualTo(ArcadeDronePhase.Expired));
        }
        [Test] public void ExplosionVisualFinishesWithoutRepeatingDamage()
        {
            var s=State(.4f);var targets=new[]{new ArcadeDroneTarget(1,1,.35f)};Assert.That(Step(s,enemies:targets).Exploded,Is.True);
            Assert.That(Step(s,1,enemies:targets).Exploded,Is.False);Assert.That(s.Phase,Is.EqualTo(ArcadeDronePhase.Expired));
        }
    }
}
