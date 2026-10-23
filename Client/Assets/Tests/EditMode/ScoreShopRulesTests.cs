using System.IO;
using System.Linq;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>用正式商品目录验收交易结果；不启动整局或网络。</summary>
    public sealed class ScoreShopRulesTests
    {
        private static ArcadeScoreShopConfiguration Config() => ArcadeScoreShopConfiguration.FromTables(
            new Tables(n => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));

        [Test] public void StageTwo_OnlyOffersThreeDistinctUnlockedAirdrops()
        {
            var config=Config(); var state=ArcadeScoreShopRules.Open(config,2,123);
            Assert.That(state.Offers.Count,Is.EqualTo(3));
            Assert.That(state.Offers.Distinct().Count(),Is.EqualTo(3));
            Assert.That(state.Offers.All(id=>new[]{"arcade.drop.weapon.rapid","arcade.drop.weapon.spread","arcade.drop.weapon.flamethrower","arcade.drop.support.drone"}.Contains(id)),Is.True);
        }
        [Test] public void Refresh_ChargesFiveHundredAndChangesAtLeastOneOffer()
        {
            var config=Config(); var before=ArcadeScoreShopRules.Open(config,2,123);
            Assert.That(ArcadeScoreShopRules.TryOperate(config,before,12500,1,ScoreShopAction.Refresh,null,out var next,out var score,out _),Is.True);
            Assert.That(score,Is.EqualTo(12000)); Assert.That(next.Offers.Except(before.Offers),Is.Not.Empty);
            Assert.That(before.Revision,Is.EqualTo(1)); Assert.That(next.Revision,Is.EqualTo(2));
        }
        [Test] public void Purchase_SpendsQuotedPriceAndEndsVisitWithoutChangingInput()
        {
            var config=Config(); var before=ArcadeScoreShopRules.Open(config,2,123); var id=before.Offers[0];
            Assert.That(ArcadeScoreShopRules.TryOperate(config,before,10000,1,ScoreShopAction.Buy,id,out var next,out var score,out _),Is.True);
            Assert.That(score,Is.EqualTo(10000-config.Items.Single(i=>i.DropId==id).Price));
            Assert.That(next.Completed,Is.True); Assert.That(next.PurchasedDropId,Is.EqualTo(id)); Assert.That(before.Completed,Is.False);
            Assert.That(ArcadeScoreShopRules.TryOperate(config,next,score,next.Revision,ScoreShopAction.Refresh,null,out _,out _,out _),Is.False);
        }
        [Test] public void Skip_PreservesScoreAndEndsVisit()
        {
            var config=Config(); var state=ArcadeScoreShopRules.Open(config,2,1);
            Assert.That(ArcadeScoreShopRules.TryOperate(config,state,400,1,ScoreShopAction.Skip,null,out var next,out var score,out _),Is.True);
            Assert.That(score,Is.EqualTo(400)); Assert.That(next.Completed,Is.True); Assert.That(next.PurchasedDropId,Is.Null);
        }
        [TestCase(ScoreShopAction.Buy)] [TestCase(ScoreShopAction.Refresh)]
        public void InsufficientScore_DoesNotMutateOffersOrRevision(ScoreShopAction action)
        {
            var config=Config(); var state=ArcadeScoreShopRules.Open(config,2,1); var ids=state.Offers.ToArray();
            Assert.That(ArcadeScoreShopRules.TryOperate(config,state,400,1,action,state.Offers[0],out _,out var score,out _),Is.False);
            Assert.That(score,Is.EqualTo(400)); Assert.That(state.Offers,Is.EqualTo(ids)); Assert.That(state.Revision,Is.EqualTo(1));
        }
        [Test] public void StaleRequestAndForeignProduct_CannotSpendScore()
        {
            var config=Config(); var state=ArcadeScoreShopRules.Open(config,2,1);
            Assert.That(ArcadeScoreShopRules.TryOperate(config,state,10000,0,ScoreShopAction.Buy,state.Offers[0],out _,out _,out _),Is.False);
            Assert.That(ArcadeScoreShopRules.TryOperate(config,state,10000,1,ScoreShopAction.Buy,"arcade.drop.bomb",out _,out _,out _),Is.False);
        }
    }
}
