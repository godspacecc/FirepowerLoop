using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>验证测试跳关必须通过真实客户端版本门禁并重新等待地图，而不是复用旧握手。</summary>
    public sealed class OnlineArcadeCommandGateTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private static void Set(object target,string name,object value)=>target.GetType().GetField(name,Flags).SetValue(target,value);
        private static OnlineArcadeBattleSnapshot Battle(int generation,long revision)=>new(2,3,4,0,1,2,revision,0,
            Array.Empty<OnlineArcadeBattlePlayerSnapshot>(),Array.Empty<OnlineArcadeEnemySnapshot>(),roundGeneration:generation);
        private static OnlineArcadeRunClientSnapshot Snapshot(long revision,OnlineArcadeBattleSnapshot battle)=>new(true,1,2,revision,
            OnlineArcadeRunPhase.Arena,1,2,"test",Array.Empty<OnlineArcadePlayerSnapshot>(),OnlineArcadeOperationState.Idle,OnlineArcadeResultCode.Success,battle);

        [Test]
        public void RepeatedGotoPassesPresentationRevisionGateAndWaitsForNewMap()
        {
            var system=new OnlineArcadePresentationSystem();var view=new WaitingView();
            Set(system,"active",true);Set(system,"latest",Snapshot(10,Battle(5,999)));
            Set(system,"activeRoundGeneration",5);Set(system,"lastAppliedBattleRevision",999L);Set(system,"activeBattleArenaNumber",2);Set(system,"view",view);
            var next=Snapshot(11,Battle(6,1));
            typeof(OnlineArcadePresentationSystem).GetMethod("OnRunChanged",Flags).Invoke(system,new object[]{new OnlineArcadeRunChangedEvent(next)});
            Assert.That(view.TransitionCount,Is.EqualTo(1),"新代次必须重新进入地图准备");
            Assert.That(system.IsRoundPresented(6),Is.False,"资源未准备前不能提交新地图ready");
            var accepted=(OnlineArcadeRunClientSnapshot)typeof(OnlineArcadePresentationSystem).GetField("latest",Flags).GetValue(system);
            Assert.That(accepted.Revision,Is.EqualTo(next.Revision));
            Assert.That(accepted.Battle.RoundGeneration,Is.EqualTo(6));
        }

        [Test]
        public void RestoredNormalContentWithNewRevisionPassesRealRunGate()
        {
            var system=new OnlineArcadeRunSystem();Set(system,"lastSnapshot",Snapshot(500,Battle(6,999)));
            var gate=typeof(OnlineArcadeRunSystem).GetMethod("IsStaleRunSnapshot",Flags);
            var old=new ET.OnlineArcadeRunSnapshot{RunId=1,Revision=1};
            var restored=new ET.OnlineArcadeRunSnapshot{RunId=1,Revision=501};
            Assert.That((bool)gate.Invoke(system,new object[]{old}),Is.True);
            Assert.That((bool)gate.Invoke(system,new object[]{restored}),Is.False);
        }

        private sealed class WaitingView:IArcadeCombatViewPort
        {
            public int TransitionCount;
            public bool IsActive=>true;
            public bool IsRoundTransitionCovered=>false;
            public void BeginRoundTransition()=>TransitionCount++;
            public void Begin(int n,float w,float h,System.Collections.Generic.IReadOnlyList<ArcadeObstacleDefinition> o){}
            public void End(){}
            public bool TryScreenToArena(Vector2 s,out Vector2 p){p=default;return false;}
            public void SetPlayer(Vector2 p,Vector2 f,bool v,bool i){}
            public void SetPlayerHealth(int c,int m){}
            public void SetEnemy(int id,string c,Vector2 p,int hp,int max,bool t=false,int seq=0,bool bar=false){}
            public void RemoveEnemy(int id,bool death=false){}
            public void SetProjectile(int id,bool h,Vector2 p,Vector2 d){}
            public void RemoveProjectile(int id){}
            public void SetDrop(int id,ArcadeDropDefinition d,Vector2 p){}
            public void RemoveDrop(int id){}
            public void SetRemotePlayer(long id,Vector2 p,Vector2 f,bool v,bool i,int c,int m){}
            public void RemoveRemotePlayer(long id){}
            public void TickPlayerFeedback(float d){}
        }
    }
}
