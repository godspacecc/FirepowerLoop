using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ET;
using FirepowerLoop.Arcade;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>仅验证真实客户端状态所有者；网络替身只提供权威响应及超时。</summary>
    public sealed class SlayerMartOnlineFlowTests
    {
        [TearDown] public void TearDown()=>OnlineArcadeRunTests.ResetArchitecture();
        private static OnlineArcadeRunTests.Harness Open()
        {
            var h=OnlineArcadeRunTests.CreateHarness();h.Network.ConfigureRun=r=>SetShop(r);h.System.StartAsync().GetAwaiter().GetResult();return h;
        }
        internal static void SetShop(ET.OnlineArcadeRunSnapshot r,int generation=4,long revision=7,long owner=10)
        {
            r.Phase=2;r.Battle=OnlineArcadeBattleInfo.Create();r.Battle.ArenaNumber=3;r.Battle.RoundGeneration=generation;
            r.Battle.BattleRevision=revision;r.Battle.SlayerMartRulesVersion=1;r.Battle.SlayerMartActive=true;
            r.Battle.SlayerMartVisit=1;r.Battle.SlayerMartGeneration=generation;r.Battle.SlayerMartRevision=revision;
            r.Battle.SlayerMartReady=true;r.Battle.SlayerMartSeconds=290;
            var member=OnlineSlayerMartMemberInfo.Create();member.PlayerId=10;member.Choice=1;member.Lives=3;r.Battle.SlayerMartMembers.Add(member);
            var q=OnlineSlayerMartSnapshotInfo.Create();q.RegionInstanceId="100:slayermart:1";q.Generation=generation;q.Revision=revision;
            q.Visit=1;q.PlayerId=owner;q.Cash=22000;q.Tokens=3;q.Bombs=2;q.Dashes=4;q.Lives=3;q.VestSlots=1;q.Plates=1;
            q.Ready=true;q.RemainingSeconds=290;q.Members.Add(member);
            var offer=OnlineSlayerMartOfferInfo.Create();offer.ItemId="slayermart.item.01";offer.Error="server_restriction";q.Offers.Add(offer);r.SelfSlayerMart=q;
        }
        private static ArcadeSlayerMartSnapshot Read(OnlineArcadeRunSystem s)
        {
            var m=s.GetType().GetMethod("GetSlayerMartSnapshot");Assert.That(m,Is.Not.Null,"客户端尚未接入个人商店快照");
            return (ArcadeSlayerMartSnapshot)m.Invoke(s,null);
        }
        private static Task Invoke(OnlineArcadeRunSystem s,string method,params object[] args)
        {var m=s.GetType().GetMethod(method);Assert.That(m,Is.Not.Null,"缺少正式商店交易入口");return (Task)m.Invoke(s,args);}
        private static ArcadeSlayerMartRequest Buy()=>new(){PlayerId=10,RegionInstanceId="100:slayermart:1",Generation=4,ExpectedRevision=7,OperationId="stable-buy",Action=SlayerMartAction.Buy,ItemId="slayermart.item.01",ConfirmedAmount=1};
        [Test] public void DirectedQuoteUsesOwnBalancesAndServerRejection()
        {var h=Open();var q=Read(h.System);Assert.That(q.Cash,Is.EqualTo(22000));Assert.That(q.Tokens,Is.EqualTo(3));Assert.That(q.Offers.Single().Error,Is.EqualTo("server_restriction"));}
        [Test] public void PublicBroadcastRetainsPersonalQuote()
        {var h=Open();h.Network.ConfigureRun=r=>{SetShop(r,revision:8);r.SelfSlayerMart=null;};h.Network.PushConfigured();Assert.That(Read(h.System).Cash,Is.EqualTo(22000));}
        [Test] public void QuoteIsIndependentOfMutableProtocolLists()
        {OnlineSlayerMartSnapshotInfo original=null;var h=OnlineArcadeRunTests.CreateHarness();h.Network.ConfigureRun=r=>{SetShop(r);original=r.SelfSlayerMart;};h.System.StartAsync().GetAwaiter().GetResult();original.Cash=1;original.Offers.Clear();Assert.That(Read(h.System).Cash,Is.EqualTo(22000));Assert.That(Read(h.System).Offers.Count,Is.EqualTo(1));}
        [Test] public void ForeignPrivateQuoteIsIgnored()
        {var h=Open();h.Network.ConfigureRun=r=>{SetShop(r,revision:8,owner:20);r.SelfSlayerMart.Cash=1;};h.Network.PushConfigured();Assert.That(Read(h.System).Cash,Is.EqualTo(22000));}
        [Test] public void OldPrivateRevisionCannotReplaceLatestQuote()
        {var h=Open();h.Network.ConfigureRun=r=>{SetShop(r,revision:9);r.SelfSlayerMart.Revision=6;r.SelfSlayerMart.Cash=1;};h.Network.PushConfigured();Assert.That(Read(h.System).Cash,Is.EqualTo(22000));}
        [Test] public void NewGenerationClearsOldQuoteUntilDirectedReply()
        {var h=Open();h.Network.ConfigureRun=r=>{SetShop(r,5,8);r.SelfSlayerMart=null;};h.Network.PushConfigured();Assert.That(Read(h.System),Is.Null);}
        [Test] public void StaleGenerationCannotReplaceCurrentShop()
        {var h=Open();h.Network.ConfigureRun=r=>SetShop(r,3,9);h.Network.PushConfigured();Assert.That(Read(h.System).Generation,Is.EqualTo(4));}
        [Test] public void LeavingShopClearsPrivateQuote()
        {var h=Open();h.Network.ConfigureRun=r=>{SetShop(r,revision:8);r.Battle.SlayerMartActive=false;r.SelfSlayerMart=null;};h.Network.PushConfigured();Assert.That(Read(h.System),Is.Null);}
        [Test] public void UnknownOutcomeRetriesExactIntentWithoutLocalDebit()
        {
            var h=Open();var request=Buy();h.Network.OverrideResponse=_=>throw new TimeoutException();Invoke(h.System,"OperateSlayerMartAsync",request).GetAwaiter().GetResult();
            var first=(C2G_OperateOnlineSlayerMart)h.Network.LastRequest;request.ItemId="mutated";request.OperationId="mutated";request.ConfirmedAmount=99;
            Assert.That(Read(h.System).Cash,Is.EqualTo(22000));Invoke(h.System,"RetryPendingSlayerMartAsync").GetAwaiter().GetResult();
            var retry=(C2G_OperateOnlineSlayerMart)h.Network.LastRequest;Assert.That(retry.OperationId,Is.EqualTo("stable-buy"));Assert.That(retry.ItemId,Is.EqualTo(first.ItemId));Assert.That(retry.ConfirmedAmount,Is.EqualTo(1));Assert.That(retry.ProtocolFingerprint,Does.EndWith("|slayermart.v2|coop-rescue.v1"));
        }
        [Test] public void PendingUnknownOutcomeDoesNotSendDifferentPurchase()
        {var h=Open();int calls=0;h.Network.OverrideResponse=_=>{calls++;throw new TimeoutException();};Invoke(h.System,"OperateSlayerMartAsync",Buy()).GetAwaiter().GetResult();var next=Buy();next.OperationId="new";Invoke(h.System,"OperateSlayerMartAsync",next).GetAwaiter().GetResult();Assert.That(calls,Is.EqualTo(1));}
        [Test] public void WrongOwnerIntentNeverReachesNetwork()
        {var h=Open();int calls=0;h.Network.OverrideResponse=_=>{calls++;throw new TimeoutException();};var request=Buy();request.PlayerId=20;Invoke(h.System,"OperateSlayerMartAsync",request).GetAwaiter().GetResult();Assert.That(calls,Is.Zero);}
        [Test] public void EndingRunClearsQuoteAndPendingTransaction()
        {var h=Open();h.Network.PushRunEnded();Assert.That(Read(h.System),Is.Null);}
        [Test] public void QuoteCarriesInventoryForTruncationAndLifePreview()
        {var q=Read(Open().System);Assert.That(q.Bombs,Is.EqualTo(2));Assert.That(q.Dashes,Is.EqualTo(4));Assert.That(q.VestSlots,Is.EqualTo(1));Assert.That(q.Plates,Is.EqualTo(1));Assert.That(q.Members.Single().Lives,Is.EqualTo(3));}
        [Test] public void PublicCountdownAndMenuStateAdvanceWithoutPrivateQuote()
        {var h=Open();h.Network.ConfigureRun=r=>{SetShop(r,revision:8);r.Battle.SlayerMartSeconds=273;r.Battle.SlayerMartMembers[0].MenuOpen=true;r.SelfSlayerMart=null;};h.Network.PushConfigured();var q=Read(h.System);Assert.That(q.RemainingSeconds,Is.EqualTo(273));Assert.That(q.Members.Single().MenuOpen,Is.True);}
        [Test] public void ExplicitRejectionClearsPendingAndKeepsServerReason()
        {var h=Open();h.Network.OverrideResponse=_=>new G2C_OperateOnlineSlayerMart{Error=200135,Message="sold_out"};var task=(Task<OnlineSlayerMartOperationResult>)Invoke(h.System,"OperateSlayerMartAsync",Buy());var result=task.GetAwaiter().GetResult();Assert.That(result.Error,Is.EqualTo("sold_out"));Assert.That(h.System.HasPendingSlayerMartOperation,Is.False);Assert.That(Read(h.System).Cash,Is.EqualTo(22000));}
        [Test] public void ConfirmedReplayAllowsNextTransaction()
        {var h=Open();int calls=0;h.Network.OverrideResponse=_=>{calls++;return new G2C_OperateOnlineSlayerMart{Accepted=true,Replayed=true};};Invoke(h.System,"OperateSlayerMartAsync",Buy()).GetAwaiter().GetResult();Assert.That(h.System.HasPendingSlayerMartOperation,Is.False);var next=Buy();next.OperationId="next";Invoke(h.System,"OperateSlayerMartAsync",next).GetAwaiter().GetResult();Assert.That(calls,Is.EqualTo(2));}
        [Test] public void LateTransactionReplyAfterRunEndedCannotRestoreQuote()
        {var h=Open();h.Network.OverrideResponse=_=>{h.Network.PushRunEnded();return new G2C_OperateOnlineSlayerMart{Accepted=true};};Invoke(h.System,"OperateSlayerMartAsync",Buy()).GetAwaiter().GetResult();Assert.That(Read(h.System),Is.Null);Assert.That(h.System.HasPendingSlayerMartOperation,Is.False);}
        [Test] public void OldRestoreResponseCannotRollBackNewerPush()
        {
            var h=Open();var old=ET.OnlineArcadeRunSnapshot.Create();old.RunId=100;old.RoomId=200;old.Revision=1;old.ConfigFingerprint=h.Network.ConfigFingerprint;SetShop(old);
            h.Network.OverrideResponse=_=>{h.Network.ConfigureRun=r=>SetShop(r,5,9);h.Network.PushConfigured();return new G2C_GetCurrentOnlineArcadeRun{HasRun=true,Run=old};};
            h.System.RestoreAsync().GetAwaiter().GetResult();Assert.That(Read(h.System).Generation,Is.EqualTo(5));Assert.That(h.System.Snapshot.Battle.RoundGeneration,Is.EqualTo(5));
        }
        [Test] public void ForeignQuoteOnFirstResponseDoesNotExposeOtherPlayersCash()
        {var h=OnlineArcadeRunTests.CreateHarness();h.Network.ConfigureRun=r=>SetShop(r,owner:20);h.System.StartAsync().GetAwaiter().GetResult();Assert.That(Read(h.System),Is.Null);}
        [Test] public void WrongRegionIntentNeverReachesNetwork()
        {var h=Open();int calls=0;h.Network.OverrideResponse=_=>{calls++;return new G2C_OperateOnlineSlayerMart();};var request=Buy();request.RegionInstanceId="other";Invoke(h.System,"OperateSlayerMartAsync",request).GetAwaiter().GetResult();Assert.That(calls,Is.Zero);}
    }
}
