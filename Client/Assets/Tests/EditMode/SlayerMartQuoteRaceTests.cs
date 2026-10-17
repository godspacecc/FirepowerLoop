using System;
using System.Linq;
using ET;
using FirepowerLoop.Arcade;
using NUnit.Framework;
namespace Game.Tests
{
    /// <summary>公共战斗和定向报价独立版本，真实RPC回调中重现广播超车。</summary>
    public sealed class SlayerMartQuoteRaceTests
    {
        private SlayerMartUiFlowTests h;
        [SetUp]public void Setup(){h=new SlayerMartUiFlowTests();h.Setup();}
        [TearDown]public void Cleanup()=>h.Cleanup();
        private OnlineArcadeRunSnapshot Quote(int generation=4,long owner=10)
        {var r=OnlineArcadeRunSnapshot.Create();r.RunId=100;r.RoomId=200;r.Revision=1;r.ConfigFingerprint=h.Network.ConfigFingerprint;SlayerMartOnlineFlowTests.SetShop(r,generation,8,owner);r.SelfSlayerMart.Cash=12000;r.SelfSlayerMart.Tokens=2;r.SelfSlayerMart.Offers[0].ItemId="slayermart.item.02";return r;}
        private void Public(int generation=4)
        {h.Configure(r=>{SlayerMartOnlineFlowTests.SetShop(r,generation,9);r.Battle.SlayerMartRevision=8;r.Battle.SlayerMartSeconds=250;r.Battle.SlayerMartMembers[0].Finished=true;r.SelfSlayerMart=null;});h.Network.PushConfigured();}
        private ArcadeSlayerMartRequest Intent(SlayerMartAction action)=>new(){Action=action,PlayerId=10,RegionInstanceId="100:slayermart:1",Generation=4,ExpectedRevision=7,OperationId="race",ItemId=action==SlayerMartAction.Buy?"slayermart.item.01":null,ConfirmedAmount=action==SlayerMartAction.Buy?1:0};
        private void AssertCurrent()
        {var q=h.Online.GetSlayerMartSnapshot();Assert.That(q,Is.Not.Null);Assert.That(q.Cash,Is.EqualTo(12000));Assert.That(q.Tokens,Is.EqualTo(2));Assert.That(q.Revision,Is.EqualTo(8));Assert.That(q.RemainingSeconds,Is.EqualTo(250));Assert.That(q.Members.Single().Finished,Is.True);Assert.That(h.Online.Snapshot.Battle.BattleRevision,Is.EqualTo(9));}
        [Test]public void BuyReceiptMergesNewQuoteAfterPublicOvertakes()
        {var old=Quote();h.Network.OverrideResponse=_=>{Public();return new G2C_OperateOnlineSlayerMart{Accepted=true,Run=old};};Assert.That(h.Online.OperateSlayerMartAsync(Intent(SlayerMartAction.Buy)).GetAwaiter().GetResult().Accepted,Is.True);AssertCurrent();}
        [Test]public void RerollReceiptKeepsNewOffersWithoutRollingBackClock()
        {var old=Quote();h.Network.OverrideResponse=_=>{Public();return new G2C_OperateOnlineSlayerMart{Accepted=true,Run=old};};h.Online.OperateSlayerMartAsync(Intent(SlayerMartAction.Reroll)).GetAwaiter().GetResult();AssertCurrent();Assert.That(h.Online.GetSlayerMartSnapshot().Offers.Single().Item.Id,Is.EqualTo("slayermart.item.02"));}
        [Test]public void InputPollingCanCatchUpPrivateQuote()
        {var old=Quote();Public();h.Network.OverrideResponse=request=>new G2C_SubmitOnlineArcadeInput{Accepted=true,LastAcceptedInputSequence=((C2G_SubmitOnlineArcadeInput)request).InputSequence,Run=old};h.Online.SubmitInputAsync(0,0,0,1,false).GetAwaiter().GetResult();AssertCurrent();}
        [Test]public void RestoreCanSupplyFirstQuoteDespiteFasterPublicFrame()
        {Public(5);Assert.That(h.Online.GetSlayerMartSnapshot(),Is.Null);var old=Quote(5);h.Network.OverrideResponse=_=>new G2C_GetCurrentOnlineArcadeRun{HasRun=true,Run=old};h.Online.RestoreAsync().GetAwaiter().GetResult();AssertCurrent();Assert.That(h.Online.GetSlayerMartSnapshot().Generation,Is.EqualTo(5));}
        [Test]public void OldGenerationNeverMergesEconomy()
        {Public(5);var old=Quote(4);h.Network.OverrideResponse=_=>new G2C_GetCurrentOnlineArcadeRun{HasRun=true,Run=old};h.Online.RestoreAsync().GetAwaiter().GetResult();Assert.That(h.Online.GetSlayerMartSnapshot(),Is.Null);Assert.That(h.Online.Snapshot.Battle.RoundGeneration,Is.EqualTo(5));}
        [Test]public void ForeignOwnerNeverMergesEconomy()
        {Public();var old=Quote(owner:20);h.Network.OverrideResponse=_=>new G2C_GetCurrentOnlineArcadeRun{HasRun=true,Run=old};h.Online.RestoreAsync().GetAwaiter().GetResult();Assert.That(h.Online.GetSlayerMartSnapshot().Cash,Is.EqualTo(100000));}
    }
}
