using NUnit.Framework;
namespace Game.Tests
{
    /// <summary>多阶段任务拒绝越级交互，设施奖励及生成额度均有限。</summary>
    public sealed class MissionPhaseTests
    {
        [Test] public void EntryRejectsSupplyCollection(){Assert.That(new MissionRunState(3,6).TryCollect(0),Is.False);}
        [Test] public void MissionExposesExplicitPhase(){Assert.That(new MissionRunState(3,6).Phase,Is.EqualTo(MissionPhase.Entry));}
        private static void Clear(MissionRunState run){while(run.TryRecordSpawn()){}run.Advance(1,0,false,false);}
        [Test] public void FiniteEncountersRequireClearAndDefendPresenceThenRewardOnce()
        {
            var run=new MissionRunState(2,new[]{1,2,3},2,2,2,2,3);
            Assert.That(run.TryExtract(0),Is.False);Clear(run);
            Assert.That(run.TryCollect(2),Is.True);Assert.That(run.TryCollect(2),Is.False);
            Assert.That(run.TryCollect(0),Is.False);Clear(run);
            Assert.That(run.TryCollect(0),Is.True);Clear(run);
            Assert.That(run.TryCollect(1),Is.True);while(run.TryRecordSpawn()){}
            run.Advance(1,1,false,false);Assert.That(run.Phase,Is.EqualTo(MissionPhase.Search));
            run.Advance(1,0,false,false);Assert.That(run.Phase,Is.EqualTo(MissionPhase.Activate));
            run.Advance(20,0,false,false);Assert.That(run.SecondsRemaining,Is.EqualTo(2));
            run.Advance(2,0,true,false);Assert.That(run.Phase,Is.EqualTo(MissionPhase.Defend));
            Clear(run);Assert.That(run.SecondsRemaining,Is.EqualTo(3));
            run.Advance(3,1,true,false);Assert.That(run.Phase,Is.EqualTo(MissionPhase.Defend));
            run.Advance(1,0,true,false);Assert.That(run.TryTakeFacilityReward(),Is.True);Assert.That(run.TryTakeFacilityReward(),Is.False);
            Assert.That(run.TryRecordSpawn(),Is.False);run.Advance(1,0,false,true);
            Clear(run);Assert.That(run.Phase,Is.EqualTo(MissionPhase.Breakthrough));Clear(run);
            Assert.That(run.Phase,Is.EqualTo(MissionPhase.Extract));Assert.That(run.Spawned,Is.EqualTo(14));
            Assert.That(run.TryExtract(1),Is.False);Assert.That(run.TryExtract(0),Is.True);Assert.That(run.TryExtract(0),Is.False);Assert.That(run.TryRecordSpawn(),Is.False);
        }
        [Test] public void InvalidTimeCannotAdvancePhase()
        {
            var run=new MissionRunState(1,1);run.TryRecordSpawn();
            foreach(float dt in new[]{0,-1,float.NaN,float.PositiveInfinity})run.Advance(dt,0,true,true);
            Assert.That(run.Phase,Is.EqualTo(MissionPhase.Entry));
        }
    }
}
