using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
namespace Game.Tests
{
    /// <summary>整局共享次数与单槽替换回归，不读存档或创建场景对象。</summary>
    public sealed class SurvivorRefreshTests
    {
        private SurvivorRunState State(int budget=3)
        {
            var ctor=typeof(SurvivorRunState).GetConstructor(new[]{typeof(int),typeof(int),typeof(int)});
            Assert.That(ctor,Is.Not.Null,"整局刷新预算尚未接入");
            var s=(SurvivorRunState)ctor.Invoke(new object[]{10,5,budget});s.AddExperience(10);
            s.OfferWeighted(new[]{1,2,3},new[]{1,1,1},1,1);return s;
        }
        private object Call(SurvivorRunState s,string method,params object[] args)
        {var m=typeof(SurvivorRunState).GetMethod(method);Assert.That(m,Is.Not.Null,method);return m.Invoke(s,args);}
        private int Left(SurvivorRunState s)
        {var p=typeof(SurvivorRunState).GetProperty("RemainingRefreshes");Assert.That(p,Is.Not.Null);return (int)p.GetValue(s);}
        private bool Refresh(SurvivorRunState s,int revision,int slot,int[] ids=null,int[] weights=null)
            =>(bool)Call(s,"TryRefresh",revision,slot,ids??new[]{1,2,3,4,5,6},weights??new[]{1,1,1,1,1,1},17);
        [Test] public void RefreshChangesOnlyRequestedSlotAndKeepsPendingExperience()
        {
            var s=State();var before=s.OfferWeighted(new[]{1},new[]{1},3,0);int pending=s.PendingChoices,level=s.Level;long xp=s.Experience;
            Assert.That(Refresh(s,0,1),Is.True);var after=s.OfferWeighted(new[]{1},new[]{1},3,0);
            Assert.That(after[0],Is.EqualTo(before[0]));Assert.That(after[2],Is.EqualTo(before[2]));Assert.That(after[1],Is.Not.EqualTo(before[1]));
            Assert.That(after.Distinct().Count(),Is.EqualTo(3));Assert.That(s.PendingChoices,Is.EqualTo(pending));Assert.That(s.Level,Is.EqualTo(level));Assert.That(s.Experience,Is.EqualTo(xp));
            Assert.That(s.UpgradeLevel(after[1]),Is.Zero);Assert.That(Left(s),Is.EqualTo(2));Assert.That(s.ChoiceRevision,Is.EqualTo(1));s.Advance(2,false);Assert.That(s.ElapsedSeconds,Is.Zero);
        }
        [Test] public void ThreeRefreshesAreSharedAcrossSlotsAndDoNotResetAfterChoosing()
        {
            var s=State();for(int i=0;i<3;i++)Assert.That(Refresh(s,s.ChoiceRevision,i),Is.True);
            Assert.That(Left(s),Is.Zero);Assert.That(Refresh(s,s.ChoiceRevision,0),Is.False);
            int id=s.OfferWeighted(new[]{1},new[]{1},1,0)[0];Assert.That(s.Choose(s.ChoiceRevision,id),Is.True);
            s.AddExperience(s.RequiredExperience);s.OfferWeighted(new[]{1,2,3,4},new[]{1,1,1,1},1,0);Assert.That(Left(s),Is.Zero);Assert.That(Left(State()),Is.EqualTo(3));
        }
        [Test] public void OldRefreshAndOldSelectionAreRejected()
        {var s=State();Assert.That(Refresh(s,0,0),Is.True);Assert.That(Refresh(s,0,1),Is.False);Assert.That(s.Choose(0,2),Is.False);Assert.That(Left(s),Is.EqualTo(2));}
        [Test] public void NoAlternativeDoesNotSpendOrChangeRevision()
        {var s=State();Assert.That(Refresh(s,0,1,new[]{1,2,3},new[]{1,1,1}),Is.False);Assert.That(Left(s),Is.EqualTo(3));Assert.That(s.ChoiceRevision,Is.Zero);}
        [TestCase(-1)] [TestCase(3)] public void InvalidSlotDoesNotSpend(int slot)
        {var s=State();Assert.That(Refresh(s,0,slot),Is.False);Assert.That(Left(s),Is.EqualTo(3));}
        [Test] public void FinishedRunCannotRefresh()
        {var s=State();s.Finish();Assert.That(Refresh(s,0,0),Is.False);Assert.That(Left(s),Is.EqualTo(3));}
        [Test] public void NoPendingChoiceCannotRefresh()
        {var s=State();s.Choose(0,1);Assert.That(Refresh(s,s.ChoiceRevision,0),Is.False);Assert.That(Left(s),Is.EqualTo(3));}
        [Test] public void CandidatesRespectPositiveWeightAndDoNotRepeatOtherCards()
        {var s=State();Assert.That(Refresh(s,0,0,new[]{1,2,3,4,5},new[]{1,1,1,0,1}),Is.True);Assert.That(s.OfferWeighted(new[]{1},new[]{1},1,0)[0],Is.EqualTo(5));}
        [Test] public void UiAvailabilityUsesSameAlternativeRule()
        {var s=State();Assert.That((bool)Call(s,"CanRefresh",0,0,new[]{1,2,3},new[]{1,1,1}),Is.False);Assert.That((bool)Call(s,"CanRefresh",0,0,new[]{1,2,3,4},new[]{1,1,1,1}),Is.True);}
    }
}
