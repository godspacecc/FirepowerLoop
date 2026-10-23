using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class OnlineElectricHitFeedbackTests
    {
        [Test] public void WholeDelayedSnapshotAndFrozenHazardClockDoNotReplayOldHit()
        {
            var type=typeof(OnlineArcadePresentationSystem).Assembly.GetType("Game.OnlineElectricHitPresentationState");
            var state=Activator.CreateInstance(type,true);var method=type.GetMethod("Observe");int count=0;Action<int,Vector2> play=(id,p)=>count++;
            void Observe(OnlineArcadeElectricHitSnapshot[] hits,long server,double received)=>method.Invoke(state,new object[]{hits,false,1,true,6f,play,server,received});
            Observe(Array.Empty<OnlineArcadeElectricHitSnapshot>(),100000,1);
            Observe(new[]{new OnlineArcadeElectricHitSnapshot(1,7,0,0,1,6,100100)},100100,2.1);
            Assert.That(count,Is.Zero,"整份快照额外排队一秒时，不应补播命中");
            Observe(new[]{new OnlineArcadeElectricHitSnapshot(2,7,0,0,1,6,100200)},105000,6);
            Assert.That(count,Is.Zero,"机关时钟冻结也不能使旧记录永久新鲜");
            Observe(new[]{new OnlineArcadeElectricHitSnapshot(3,7,0,0,1,6,105100)},105100,6.1);
            Assert.That(count,Is.EqualTo(1),"恢复正常新鲜快照后仍反馈实际命中");
        }
        [Test] public void HistoricalDuplicateHiddenAndOldRoundHitsDoNotReplay()
        {
            var type=typeof(OnlineArcadePresentationSystem).Assembly.GetType("Game.OnlineElectricHitPresentationState");
            Assert.That(type,Is.Not.Null,"需要消费真实命中事实，不能从靠近电塔或掉血推断");
            var state=Activator.CreateInstance(type,true);var method=type.GetMethod("Observe");int count=0;
            Action<int,Vector2> play=(id,p)=>{Assert.That(id,Is.EqualTo(7));Assert.That(p,Is.EqualTo(new Vector2(2,3)));count++;};
            OnlineArcadeElectricHitSnapshot H(long id,int round=1,float time=6)=>new OnlineArcadeElectricHitSnapshot(id,7,2,3,round,time);
            void Observe(OnlineArcadeElectricHitSnapshot[] hits,bool baseline=false,int generation=1,bool visible=true,float elapsed=6.1f)=>method.Invoke(state,new object[]{hits,baseline,generation,visible,elapsed,play,0L,0d});
            Observe(new[]{H(1)},true);Assert.That(count,Is.Zero);
            Observe(new[]{H(1),H(2)});Observe(new[]{H(1),H(2)});Assert.That(count,Is.EqualTo(1));
            Observe(new[]{H(3)},visible:false);Observe(new[]{H(3)});Assert.That(count,Is.EqualTo(1));
            Observe(new[]{H(4,time:2)});Assert.That(count,Is.EqualTo(1),"网络晚到的旧接触不能延迟补闪");
            Observe(new[]{H(1,2)},generation:2,baseline:true);Observe(new[]{H(5)},generation:1);Assert.That(count,Is.EqualTo(1));
            Observe(new[]{H(2,2)},generation:2);Assert.That(count,Is.EqualTo(2));
        }
    }
}
