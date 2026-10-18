using System;
using System.Reflection;
using ET;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>验证正式联机快照映射与冲刺预测，客户端后续输入不能覆盖服务器方向。</summary>
    public sealed class ArcadeDashPredictionTests
    {
        private OnlineArcadeBattlePlayerSnapshot Copy(float seconds, float dashX, float dashZ)
        {
            var source = OnlineArcadeBattleInfo.Create();
            var player = OnlineArcadeBattlePlayerInfo.Create();
            player.PlayerId = 1; player.Alive = true;
            player.AimZ = 1; player.DashSeconds = seconds;
            player.DashDirectionX = dashX; player.DashDirectionZ = dashZ;
            source.Players.Add(player);
            var snapshot = (OnlineArcadeBattleSnapshot)typeof(OnlineArcadeRunSystem)
                .GetMethod("CopyBattle", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { source });
            var result = snapshot.Players[0];
            // 模拟消息对象池归还后复用，预测只能读取已经复制的快照。
            player.DashDirectionX = -1; player.DashDirectionZ = 0;
            source.Dispose();
            return result;
        }

        [TestCase(0f, 0f)]
        [TestCase(-1f, 0f)]
        [TestCase(0f, -1f)]
        public void ActiveDashPredictionUsesCopiedAuthority(float moveX, float moveY)
        {
            var player = Copy(.2f, .6f, .8f);
            Assert.That(player.DashDirectionX, Is.EqualTo(.6f));
            Assert.That(player.DashDirectionZ, Is.EqualTo(.8f));
            var move = OnlineArcadePresentationSystem.PredictedMovementDirection(new Vector2(moveX, moveY), player);
            var position = OnlineArcadePresentationSystem.PredictLocalPosition(Vector2.zero, move, 10, .02f,
                .35f, 20, 20, Array.Empty<ArcadeObstacleDefinition>());
            Assert.That(Vector2.Distance(position, new Vector2(.12f, .16f)), Is.LessThan(.001f));
        }

        [Test]
        public void ExpiredDashPredictionResumesCurrentInput()
        {
            var move = OnlineArcadePresentationSystem.PredictedMovementDirection(Vector2.left, Copy(0, 1, 0));
            Assert.That(move, Is.EqualTo(Vector2.left));
        }

        [Test]
        public void LegacySnapshotWithoutDirectionFallsBackToAuthoritativeFacing()
        {
            var move = OnlineArcadePresentationSystem.PredictedMovementDirection(Vector2.left, Copy(.2f, 0, 0));
            Assert.That(move, Is.EqualTo(Vector2.up));
        }
    }
}
