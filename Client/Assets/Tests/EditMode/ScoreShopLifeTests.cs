using System.IO;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>消费后按当前积分攒命，已处理的同一档位不重复发放。</summary>
    public sealed class ScoreShopLifeTests
    {
        private static ArcadeTreasureRoomParameters Rules()=>ArcadeTreasureRoomParameters.FromConfig(
            new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))))
                .TbArcadeTreasureRoom.Get("arcade.treasure.first"));
        [Test] public void SpentScore_RecrossingClaimedTierDoesNotAwardLife()
        {
            Assert.That(ArcadeTreasureScoreRules.TryAwardKill(new ArcadeTreasureScoreState(199900,1,0,3,1),Rules(),out var next),Is.True);
            Assert.That(next.Lives,Is.EqualTo(3)); Assert.That(next.LifeTier,Is.EqualTo(1));
        }
        [Test] public void CurrentScore_CrossingNextUnclaimedTierAwardsOnce()
        {
            Assert.That(ArcadeTreasureScoreRules.TryAwardKill(new ArcadeTreasureScoreState(399900,1,0,3,1),Rules(),out var next),Is.True);
            Assert.That(next.Lives,Is.EqualTo(4)); Assert.That(next.LifeTier,Is.EqualTo(2));
            var dead=next.ResetAfterDeath(); Assert.That(dead.LifeTier,Is.EqualTo(2));
        }
        [Test] public void FullLives_ProcessesTierWithoutQueuingReward()
        {
            Assert.That(ArcadeTreasureScoreRules.TryAwardKill(new ArcadeTreasureScoreState(199900,1,0,9,0),Rules(),out var next),Is.True);
            Assert.That(next.Lives,Is.EqualTo(9)); Assert.That(next.LifeTier,Is.EqualTo(1));
        }
    }
}
