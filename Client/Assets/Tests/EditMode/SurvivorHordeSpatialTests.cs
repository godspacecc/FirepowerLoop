using System;
using System.Collections.Generic;
using NUnit.Framework;
namespace Game.Tests
{
    /// <summary>查询真实生产空间索引；反射允许缺少实现时先观察失败。</summary>
    public sealed class SurvivorHordeSpatialTests
    {
        private object New(){var t=typeof(SurvivorHordeState).Assembly.GetType("Game.SurvivorEnemySpatialIndex");Assert.IsNotNull(t,"缺少生产空间索引");return Activator.CreateInstance(t,new object[]{4f});}
        private static void Call(object s,string name,params object[] args)=>s.GetType().GetMethod(name).Invoke(s,args);
        private static void Add(object s,int id,float x,float y)=>Call(s,"Add",id,x,y);
        [Test] public void LongSegmentFindsIntermediateCells(){var s=New();Add(s,2,9,0);Add(s,1,-9,0);var r=new List<int>();Call(s,"QuerySegment",-12f,0f,12f,0f,.5f,r);CollectionAssert.AreEqual(new[]{1,2},r);}
        [Test] public void PaddingIncludesLargeBodiesAcrossBoundary(){var s=New();Add(s,1,4.1f,1.5f);var r=new List<int>();Call(s,"QuerySegment",0f,0f,8f,0f,2f,r);CollectionAssert.AreEqual(new[]{1},r);}
        [Test] public void StableIdOrderDoesNotDependOnInsertion(){var s=New();Add(s,8,1,0);Add(s,2,-1,0);var r=new List<int>();Call(s,"QueryCircle",0f,0f,2f,r);CollectionAssert.AreEqual(new[]{2,8},r);}
        [Test] public void RebuildClearRemovesDeadEnemy(){var s=New();Add(s,1,0,0);Call(s,"Clear");var r=new List<int>();Call(s,"QueryCircle",0f,0f,2f,r);Assert.IsEmpty(r);}
        [Test] public void QueryRespectsRange(){var s=New();Add(s,1,0,0);Add(s,2,10,0);var r=new List<int>();Call(s,"QueryCircle",0f,0f,3f,r);CollectionAssert.AreEqual(new[]{1},r);}
        [Test] public void EmptyIndexClearsReusedOutput(){var s=New();var r=new List<int>{9};Call(s,"QueryCircle",0f,0f,3f,r);Assert.IsEmpty(r);}
        [Test] public void DuplicateIdsNeverDuplicateCandidates(){var s=New();Add(s,1,0,0);Add(s,1,1,0);var r=new List<int>();Call(s,"QueryCircle",0f,0f,3f,r);CollectionAssert.AreEqual(new[]{1},r);}
        [Test] public void CircleMatchesBruteForceAcrossNegativeCells(){var s=New();var expected=new List<int>();int id=0;for(int x=-12;x<=12;x++)for(int y=-12;y<=12;y++){Add(s,++id,x,y);if(x*x+y*y<=49)expected.Add(id);}var r=new List<int>();Call(s,"QueryCircle",0f,0f,7f,r);CollectionAssert.AreEqual(expected,r);}
    }
}
