using System.Collections.Generic;
using System.Reflection;
using FirepowerLoop.Arcade;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>完整五牛接触验证玩家实际离开通路，不能靠无敌时间掩盖连续击飞。</summary>
    public sealed class ArcadeHerdSideImpactTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private ArcadeHerdIntegrationTests fixture;
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        [SetUp] public void Setup()
        {
            fixture=new ArcadeHerdIntegrationTests();fixture.Setup();
            system=(ArcadeCombatSystem)typeof(ArcadeHerdIntegrationTests).GetField("system",Hidden).GetValue(fixture);
            runtime=(ArcadeCombatRuntime)typeof(ArcadeHerdIntegrationTests).GetField("runtime",Hidden).GetValue(fixture);
            Call("TickRoundContent",runtime,8f);
        }
        [TearDown] public void Cleanup()=>fixture.Cleanup();
        private object Call(string method,params object[] args)=>typeof(ArcadeCombatSystem).GetMethod(method,Hidden).Invoke(system,args);
        private List<ArcadeHerdState> Cows=>runtime.HerdCows;
        private void Arrange(int direction,float lane,float offset,float x=0)
        {
            runtime.PlayerPosition=new Vector2(x,lane+offset);
            for(int i=0;i<Cows.Count;i++){Cows[i].X=x+(-7-3*i)*direction;Cows[i].Z=lane;Cows[i].DirectionX=direction;}
        }
        private void PassEntireQueue(float lane)
        {
            bool hit=false;
            for(int i=0;i<120;i++)
            {
                // 主动撤去接触保护，证明后续四牛落空来自侧向位移，而非额外免伤。
                runtime.PlayerInvulnerability=0;Call("TickRoundContent",runtime,.05f);
                hit|=runtime.HerdImpact.Active;
                Assert.That(runtime.PlayerHealth,Is.GreaterThanOrEqualTo(75),"后续牛仍连续命中玩家");
            }
            Assert.That(hit,Is.True);Assert.That(runtime.PlayerHealth,Is.EqualTo(75));
            Assert.That(Mathf.Abs(runtime.PlayerPosition.y-lane),Is.GreaterThan(2.7f),"落点仍在牛队通路");
            Assert.That(runtime.HerdImpact.Active,Is.False);
            var before=runtime.PlayerPosition;runtime.MoveDirection=Vector2.right;Call("TickPlayer",runtime,.1f);
            Assert.That(runtime.PlayerPosition.x,Is.GreaterThan(before.x),"落地后操作未恢复");
        }
        [TestCase(1,0f)] [TestCase(-1,0f)] [TestCase(1,.6f)] [TestCase(1,-.6f)]
        public void FullQueuePassesAfterSideLaunch(int direction,float offset)
        {Arrange(direction,-14,offset);PassEntireQueue(-14);}

        [TestCase(1)] [TestCase(-1)] public void EdgeContactChoosesTheInsideLanding(int edge)
        {
            float half=(float)typeof(ArcadeCombatSystem).GetProperty("ArenaHalfHeight",Hidden).GetValue(system);
            // x=0在农场上下沿的正式隔墙内；使用实际通道x=2.2，避免从非法墙内起点检查移动。
            float lane=edge*(half-.6f);Arrange(1,lane,0,2.2f);PassEntireQueue(lane);
            Assert.That(runtime.PlayerPosition.y*edge,Is.LessThan(lane*edge));
            Assert.That(Mathf.Abs(runtime.PlayerPosition.y),Is.LessThan(half));
        }
        [Test] public void BlockedSideChoosesOpenSideWithoutCrossingTheWall()
        {
            Arrange(1,-14,0);Call("ContentObstacles",runtime);
            typeof(ArcadeCombatSystem).GetField("contentRoundObstacles",Hidden).SetValue(system,
                new[]{new ArcadeObstacleDefinition(0,-12.2f,4,.5f)});
            PassEntireQueue(-14);Assert.That(runtime.PlayerPosition.y,Is.LessThan(-16.7f));
        }
    }
}
