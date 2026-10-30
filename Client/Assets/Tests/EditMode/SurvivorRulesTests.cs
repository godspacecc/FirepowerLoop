using System;
using System.Reflection;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>独立幸存者规则契约；通过真实规则入口验证，不加载场景或写正式存档。</summary>
    public sealed class SurvivorRulesTests
    {
        private object NewRun()
        {
            var type=typeof(ArcadeCombatSystem).Assembly.GetType("Game.SurvivorRunState");
            Assert.That(type,Is.Not.Null,"缺少独立幸存者运行态");
            return Activator.CreateInstance(type,new object[]{10,5});
        }
        private static object Call(object state,string name,params object[] args)=>state.GetType().GetMethod(name).Invoke(state,args);
        private static T Read<T>(object state,string name)=>(T)state.GetType().GetProperty(name).GetValue(state);
        private static object Rule(string name,params object[] args)
        {
            var type=typeof(ArcadeCombatSystem).Assembly.GetType("Game.SurvivorCombatRules");
            Assert.That(type,Is.Not.Null,"缺少自动瞄准及无尽刷怪规则");
            return type.GetMethod(name).Invoke(null,args);
        }
        [Test] public void AimChoosesNearestAttackableAndStableTie()
        {
            Assert.That(Rule("SelectTarget",0f,0f,10f,new long[]{9,3,2,1},new float[]{1,2,-2,3},new float[]{0,0,0,0},new bool[]{false,true,true,true}),Is.EqualTo(2L));
        }
        [Test] public void AimRejectsOutsideRangeAndInvalidPositions()
        {
            Assert.That(Rule("SelectTarget",0f,0f,2f,new long[]{1,2},new float[]{3,float.NaN},new float[]{0,0},new bool[]{true,true}),Is.EqualTo(0L));
        }
        [Test] public void AimIncludesExactRangeBoundary()
        {
            Assert.That(Rule("SelectTarget",0f,0f,5f,new long[]{1},new float[]{3},new float[]{4},new bool[]{true}),Is.EqualTo(1L));
        }
        [Test] public void SpawnIntervalGetsHarderButKeepsConfiguredFloor()
        {
            Assert.That(Rule("SpawnInterval",0d,2f,.2f,.1f),Is.EqualTo(2f));
            Assert.That((float)Rule("SpawnInterval",600d,2f,.2f,.1f),Is.LessThan(2f));
            Assert.That(Rule("SpawnInterval",double.MaxValue,2f,.2f,.1f),Is.EqualTo(.2f));
        }
        [Test] public void SpawnBudgetNeverExceedsAliveCap()
        {
            Assert.That(Rule("SpawnBudget",49,50,8),Is.EqualTo(1));
            Assert.That(Rule("SpawnBudget",50,50,8),Is.Zero);
            Assert.That(Rule("SpawnBudget",51,50,8),Is.Zero);
        }
        [Test] public void ExperienceCarriesAcrossQueuedLevels()
        {
            var s=NewRun(); Call(s,"AddExperience",31L);
            Assert.That(Read<int>(s,"Level"),Is.EqualTo(3));
            Assert.That(Read<long>(s,"Experience"),Is.EqualTo(6));
            Assert.That(Read<int>(s,"PendingChoices"),Is.EqualTo(2));
        }
        [Test] public void UpgradeSelectionIsVersionedAndCannotConsumeTwice()
        {
            var s=NewRun(); Call(s,"AddExperience",25L);
            var offer=(int[])Call(s,"Offer",new int[]{1,2,3,4},17);
            var revision=Read<int>(s,"ChoiceRevision");
            Assert.That(offer.Length,Is.EqualTo(3)); Assert.That(offer,Is.Unique);
            Assert.That(Call(s,"Choose",revision,offer[0]),Is.True);
            Assert.That(Call(s,"Choose",revision,offer[1]),Is.False);
            Assert.That(Read<int>(s,"PendingChoices"),Is.EqualTo(1));
        }
        [Test] public void UnknownChoiceLeavesOfferAndExperienceIntact()
        {
            var s=NewRun(); Call(s,"AddExperience",10L); Call(s,"Offer",new int[]{1,2,3},4);
            Assert.That(Call(s,"Choose",Read<int>(s,"ChoiceRevision"),99),Is.False);
            Assert.That(Read<int>(s,"PendingChoices"),Is.EqualTo(1));
        }
        [Test] public void OfferRequiresThreeDistinctValidIds()
        {
            var s=NewRun(); Call(s,"AddExperience",10L);
            Assert.That((int[])Call(s,"Offer",new int[]{1,1,2,0},4),Is.Empty);
            Assert.That(Read<int>(s,"PendingChoices"),Is.EqualTo(1));
        }
        [Test] public void RepeatedOfferKeepsSameRevisionAndContents()
        {
            var s=NewRun(); Call(s,"AddExperience",10L);
            var a=(int[])Call(s,"Offer",new int[]{1,2,3,4},4); var revision=Read<int>(s,"ChoiceRevision");
            var b=(int[])Call(s,"Offer",new int[]{1,2,3,4},99);
            Assert.That(b,Is.EqualTo(a)); Assert.That(Read<int>(s,"ChoiceRevision"),Is.EqualTo(revision));
            a[0]=999; Assert.That((int[])Call(s,"Offer",new int[]{1,2,3,4},99),Is.EqualTo(b));
        }
        [Test] public void PauseAndUpgradeStopClock()
        {
            var s=NewRun(); Call(s,"Advance",2d,false); Call(s,"Advance",3d,true);
            Call(s,"AddExperience",10L); Call(s,"Advance",4d,false);
            Assert.That(Read<double>(s,"ElapsedSeconds"),Is.EqualTo(2));
        }
        [Test] public void DeathEndsClockExperienceAndSelectionOnce()
        {
            var s=NewRun(); Call(s,"AddExperience",10L); var a=(int[])Call(s,"Offer",new int[]{1,2,3},3);
            Assert.That(Call(s,"Finish"),Is.True); Assert.That(Call(s,"Finish"),Is.False);
            Call(s,"Advance",3d,false); Call(s,"AddExperience",100L);
            Assert.That(Read<double>(s,"ElapsedSeconds"),Is.Zero); Assert.That(Read<int>(s,"Level"),Is.EqualTo(2));
            Assert.That(Call(s,"Choose",Read<int>(s,"ChoiceRevision"),a[0]),Is.False);
        }
        [Test] public void InvalidTimeAndNegativeExperienceCannotMutate()
        {
            var s=NewRun(); Call(s,"Advance",double.NaN,false); Call(s,"Advance",double.PositiveInfinity,false);
            Call(s,"Advance",-1d,false); Call(s,"AddExperience",-9L);
            Assert.That(Read<double>(s,"ElapsedSeconds"),Is.Zero); Assert.That(Read<long>(s,"Experience"),Is.Zero);
        }
    }
}
