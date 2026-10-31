using System;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>远场纯规则在Unity实际程序集中的限定验证。</summary>
    public sealed class SurvivorHordeRulesTests
    {
        [Test] public void TenThousandEnemiesUseOneGroup()
        {var s=new SurvivorHordeState();s.Add(0,"a",0,0,2,10000);Assert.AreEqual(1,s.GroupCount);Assert.AreEqual(10000,s.TotalCount);}
        [Test] public void ReservationWaitsForExactlyOneSuccessfulCommit()
        {var s=new SurvivorHordeState();s.Add(0,"a",0,0,2,100);var a=s.Reserve(-1,-1,1,1,2);Assert.AreEqual(2,a.Length);Assert.AreEqual(100,s.TotalCount);Assert.IsTrue(s.Commit(a[0].Id));Assert.IsFalse(s.Commit(a[0].Id));Assert.AreEqual(99,s.TotalCount);}
        [Test] public void OldRunCannotCommitNewRun()
        {var s=new SurvivorHordeState();s.Add(0,"a",0,0,2,1);var a=s.Reserve(-1,-1,1,1,1);s.Clear();s.Add(0,"a",0,0,2,1);s.Reserve(-1,-1,1,1,1);Assert.IsFalse(s.Commit(a[0].Id));Assert.AreEqual(1,s.TotalCount);}
        [Test] public void PausedGroupsDoNotCallNavigation()
        {var s=new SurvivorHordeState();s.Add(0,"a",0,0,2,100);s.Advance(0,(r,id,x,y,d)=>throw new Exception("暂停时寻路"));Assert.AreEqual(100,s.TotalCount);}
        [Test] public void NavigationWorkScalesWithGroupsAndMovingInvalidatesOldTicket()
        {var s=new SurvivorHordeState();s.Add(0,"a",0,0,2,10000);var a=s.Reserve(-1,-1,1,1,1);int calls=0;s.Advance(.5f,(r,id,x,y,d)=>{calls++;return (x+d,y);});Assert.AreEqual(1,calls);Assert.IsFalse(s.Commit(a[0].Id));Assert.AreEqual(1,s.Reserve(1,0,2,1,1)[0].X);}
        [Test] public void BlockedNavigationPreservesGroupAndPopulation()
        {var s=new SurvivorHordeState();s.Add(0,"a",0,0,2,100);s.Advance(1,(r,id,x,y,d)=>(x,y));Assert.AreEqual(100,s.TotalCount);Assert.AreEqual(0,s.Reserve(-1,-1,1,1,1)[0].X);}
        [Test] public void SeedIsDeterministicAndIndependentOfLaterPlayerMovement()
        {var a=new SurvivorHordeState();var b=new SurvivorHordeState();foreach(var s in new[]{a,b})SurvivorHordeRules.Seed(s,3,3,30,30,0,0,8,4,8,42,(x,y)=>true,r=>("a",2f));var x1=a.Reserve(-30,-30,30,30,100);var x2=b.Reserve(-30,-30,30,30,100);Assert.AreEqual(x1.Length,x2.Length);for(int i=0;i<x1.Length;i++){Assert.AreEqual(x1[i].X,x2[i].X);Assert.AreEqual(x1[i].Y,x2[i].Y);Assert.GreaterOrEqual(x1[i].X*x1[i].X+x1[i].Y*x1[i].Y,64);}}
        [Test] public void NoReachableFloorHasBoundedAttempts()
        {var s=new SurvivorHordeState();int calls=0;Assert.AreEqual(0,SurvivorHordeRules.Seed(s,3,3,30,30,0,0,0,4,8,42,(x,y)=>{calls++;return false;},r=>("a",2f)));Assert.AreEqual(72,calls);Assert.AreEqual(0,s.TotalCount);}
        [Test] public void PeripheralRegionsExcludeMapCenter()
        {Assert.IsFalse(SurvivorHordeRules.IsPeripheralRegion(4,3,3));Assert.IsTrue(SurvivorHordeRules.IsPeripheralRegion(0,3,3));Assert.IsTrue(SurvivorHordeRules.IsPeripheralRegion(7,3,3));}
        [Test] public void InvalidOrOverflowingInputDoesNotMutate()
        {var s=new SurvivorHordeState();Assert.Throws<ArgumentException>(()=>s.Add(0,"a",float.NaN,0,2,1));s.Add(0,"a",0,0,2,long.MaxValue);Assert.Throws<OverflowException>(()=>s.Add(0,"a",0,0,2,1));Assert.AreEqual(long.MaxValue,s.TotalCount);}
        [Test] public void DeferredBadGroupCannotStarveFollowingGroup()
        {var s=new SurvivorHordeState();s.Add(0,"blocked",0,0,2,100);s.Add(1,"safe",1,0,2,100);var a=s.Reserve(-2,-2,2,2,2);s.Defer(a[0].Id);var b=s.Reserve(-2,-2,2,2,2);Assert.AreEqual("safe",b[0].EnemyId);Assert.AreEqual(200,s.TotalCount);Assert.IsFalse(s.Commit(a[0].Id));}
        [Test] public void DeferredCandidateDoesNotInvalidateSuccessfulSibling()
        {var s=new SurvivorHordeState();s.Add(0,"a",0,0,2,10);var a=s.Reserve(-2,-2,2,2,2);s.Defer(a[0].Id);Assert.IsTrue(s.Commit(a[1].Id));Assert.AreEqual(9,s.TotalCount);Assert.IsFalse(s.Commit(a[0].Id));}
    }
}
