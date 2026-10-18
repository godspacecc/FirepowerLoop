using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>炸弹表现只消费新权威记录，重复、首次加入和跨轮记录不能误播。</summary>
    public class ArcadeBombPresentationTests
    {
        private object state;
        private Type receiptType;
        private readonly List<Vector2> played = new();
        [SetUp] public void SetUp()
        {
            var assembly=typeof(OnlineArcadePresentationSystem).Assembly;
            var type=assembly.GetType("Game.OnlineBombPresentationState");
            Assert.NotNull(type,"缺少炸弹表现去重状态");
            state=Activator.CreateInstance(type);receiptType=assembly.GetType("Game.OnlineArcadeBombBlastSnapshot");played.Clear();
        }
        private void Observe(bool baseline,int generation,bool visible,params long[] sequences)
        {
            var items=Array.CreateInstance(receiptType,sequences.Length);
            for(int i=0;i<sequences.Length;i++)items.SetValue(Activator.CreateInstance(receiptType,sequences[i],1L,3f,-4f,generation),i);
            state.GetType().GetMethod("Observe").Invoke(state,new object[]{items,baseline,generation,visible,(Action<Vector2>)played.Add});
        }
        [Test] public void BaselineAndRepeatedSnapshotsDoNotReplay()
        {
            Observe(true,1,true,1);Observe(false,1,true,1,2);Observe(false,1,true,1,2);
            Assert.AreEqual(1,played.Count);Assert.AreEqual(new Vector2(3,-4),played[0]);
        }
        [Test] public void RapidUsesRemainDistinct()
        {
            Observe(true,1,true);Observe(false,1,true,1,2,3);Assert.AreEqual(3,played.Count);
        }
        [Test] public void HiddenRegionDoesNotReplayWhenReturning()
        {
            Observe(true,1,true);Observe(false,1,false,1);Observe(false,1,true,1);Assert.IsEmpty(played);
        }
        [Test] public void NewRoundBaselineDoesNotReplayHistory()
        {
            Observe(true,1,true,1,2);Observe(true,2,true);Observe(false,2,true,1);Assert.AreEqual(1,played.Count);
        }
        [Test] public void OldRoundCannotRaiseNewRoundSequence()
        {
            Observe(true,2,true);Observe(false,1,true,99);Observe(false,2,true,1);Assert.AreEqual(1,played.Count);
        }
        [Test] public void ProtocolProjectionKeepsOriginIndependentFromMutableMessage()
        {
            var source=new ET.OnlineArcadeBattleInfo();
            source.BombBlasts.Add(new ET.OnlineArcadeBombBlastInfo{Sequence=4,PlayerId=2,PositionX=3,PositionZ=-4,RoundGeneration=7});
            var copy=(OnlineArcadeBattleSnapshot)typeof(OnlineArcadeRunSystem).GetMethod("CopyBattle",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{source});
            source.BombBlasts[0].PositionX=99;
            Assert.AreEqual(1,copy.BombBlasts.Count);Assert.AreEqual(3,copy.BombBlasts[0].PositionX);
            Assert.AreEqual(2,copy.BombBlasts[0].PlayerId);Assert.AreEqual(7,copy.BombBlasts[0].RoundGeneration);
        }
    }
}
