using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ET;
using NUnit.Framework;
using QFramework;
using TMPro;

namespace Game.Tests
{
    /// <summary>完成购物的分步重试、语言变化及硬截止，不替代服务端领域规则检查。</summary>
    public sealed class SlayerMartUiLifecycleTests
    {
        private SlayerMartUiFlowTests h;
        [SetUp]public void Setup(){h=new SlayerMartUiFlowTests();h.Setup();}
        [TearDown]public void Cleanup()=>h.Cleanup();
        private List<C2G_OperateOnlineSlayerMart> Respond(Func<C2G_OperateOnlineSlayerMart,int,bool> timeout=null)
        {
            var requests=new List<C2G_OperateOnlineSlayerMart>();h.Network.OverrideResponse=raw=>{var request=(C2G_OperateOnlineSlayerMart)raw;requests.Add(request);if(timeout?.Invoke(request,requests.Count)==true)throw new TimeoutException();return h.Acknowledge(request);};return requests;
        }
        [Test]public void FinishClosesMenuBeforeMarkingFinished()
        {var requests=Respond();h.Show();h.Ui.BtnFinish.onClick.Invoke();Assert.That(requests.Select(r=>r.ActionKind),Is.EqualTo(new[]{4,5}));Assert.That(requests[0].Value,Is.False);Assert.That(requests[1].Value,Is.True);Assert.That(h.Root.activeSelf,Is.False);}
        [Test]public void ClosingMenuDoesNotMarkFinished()
        {var requests=Respond();h.Show();h.Ui.BtnCloseMenu.onClick.Invoke();Assert.That(requests.Count,Is.EqualTo(1));Assert.That(requests[0].ActionKind,Is.EqualTo(4));Assert.That(requests[0].Value,Is.False);Assert.That(h.Root.activeSelf,Is.False);}
        [Test]public void TimedOutFinishRetryHidesOnlyAfterConfirmedCompletion()
        {var requests=Respond((r,n)=>n==2);h.Show();h.Ui.BtnFinish.onClick.Invoke();Assert.That(h.Root.activeSelf,Is.True);h.Ui.BtnRetry.onClick.Invoke();Assert.That(requests.Count,Is.EqualTo(3));Assert.That(requests[1].OperationId,Is.EqualTo(requests[2].OperationId));Assert.That(h.Root.activeSelf,Is.False);}
        [Test]public void TimedOutCloseRetryContinuesFinishWithNewIndependentIntent()
        {var requests=Respond((r,n)=>n==1);h.Show();h.Ui.BtnFinish.onClick.Invoke();h.Ui.BtnRetry.onClick.Invoke();Assert.That(requests.Count,Is.EqualTo(3));Assert.That(requests[0].OperationId,Is.EqualTo(requests[1].OperationId));Assert.That(requests[2].ActionKind,Is.EqualTo(5));Assert.That(requests[2].OperationId,Is.Not.EqualTo(requests[1].OperationId));Assert.That(h.Root.activeSelf,Is.False);}
        private void English()
        {h.Architecture.GetUtility<LocalizationUtility>().LoadFromJson("en-US",File.ReadAllText("Assets/Game/Config/Localization/en-US.json"));h.Architecture.GetModel<LocalizationModel>().CurrentLanguageCode.Value="en-US";h.Architecture.SendEvent(new LanguageChangedEvent("zh-CN","en-US"));}
        [Test]public void LanguageSwitchKeepsSelectedCardAndSendsNoReroll()
        {var requests=Respond();h.Show();h.Ui.OfferList.GetComponentsInChildren<ArcadeShopOfferBoxUI>()[1].BtnSelect.onClick.Invoke();var offers=h.Online.GetSlayerMartSnapshot().Offers.Select(o=>o.Item.Id).ToArray();English();Assert.That(h.Ui.TextDetailName.text,Does.Contain("Boost"));Assert.That(requests,Is.Empty);Assert.That(h.Online.GetSlayerMartSnapshot().Offers.Select(o=>o.Item.Id),Is.EqualTo(offers));}
        [Test]public void LanguageSwitchUpdatesStaticEntryAndLoadingLabels()
        {h.Configure(r=>{r.Battle.SlayerMartReady=r.SelfSlayerMart.Ready=false;r.Battle.SlayerMartMembers[0].Choice=0;});h.Network.PushConfigured();h.Show();English();Assert.That(h.Ui.ChoiceRoot.transform.Find("ChoiceTitle").GetComponent<TextMeshProUGUI>().text,Does.Contain("SlayerMart"));Assert.That(h.Ui.LoadingRoot.GetComponentInChildren<TextMeshProUGUI>(true).text,Does.Contain("Preparing"));}
        [Test]public void FinalFiveSecondsOverridePendingPurchaseFeedback()
        {Respond((r,n)=>true);h.Show();h.Ui.BtnBuy.onClick.Invoke();h.Configure(r=>r.Battle.SlayerMartSeconds=r.SelfSlayerMart.RemainingSeconds=4);h.Network.PushConfigured();Assert.That(h.Ui.TextMessage.text,Does.Contain("截止"));}
        [Test]public void HardClosingHidesMenuWithUncertainRequest()
        {Respond((r,n)=>true);h.Show();h.Ui.BtnBuy.onClick.Invoke();h.Configure(r=>r.Battle.SlayerMartClosing=r.SelfSlayerMart.Closing=true);h.Network.PushConfigured();Assert.That(h.Root.activeSelf,Is.False);}
        [Test]public void ServerRejectionDisablesCardAndShowsLocalizedReason()
        {h.Configure(r=>r.SelfSlayerMart.Offers[0].Error="player_dead");h.Network.PushConfigured();h.Show();Assert.That(h.Ui.BtnBuy.interactable,Is.False);Assert.That(h.Ui.TextDetailEffect.text,Does.Contain("零命"));}
        [Test]public void NewGenerationWithoutPersonalQuoteClosesOldMenu()
        {h.Show();h.Configure(r=>{r.Battle.SlayerMartGeneration=5;r.Battle.RoundGeneration=5;r.Battle.BattleRevision=8;r.SelfSlayerMart=null;});h.Network.PushConfigured();Assert.That(h.Root.activeSelf,Is.False);}
        [Test]public void AllTwentyFourDetailsFitFormalPanelInBothLanguages()
        {
            h.Show();long revision=10;
            foreach(var language in new[]{"zh-CN","en-US"})
            {
                if(language=="en-US")English();
                for(int item=1;item<=24;item++)
                {
                    string id="slayermart.item."+item.ToString("00");long current=++revision;
                    h.Configure(r=>{r.Battle.BattleRevision=r.Battle.SlayerMartRevision=r.SelfSlayerMart.Revision=current;r.SelfSlayerMart.Offers.Clear();
                        foreach(var number in new[]{item}.Concat(Enumerable.Range(1,24).Where(i=>i!=item).Take(15))){var offer=OnlineSlayerMartOfferInfo.Create();offer.ItemId="slayermart.item."+number.ToString("00");r.SelfSlayerMart.Offers.Add(offer);}});
                    h.Network.PushConfigured();h.Ui.OfferList.GetComponentsInChildren<ArcadeShopOfferBoxUI>()[0].BtnSelect.onClick.Invoke();
                    var text=h.Ui.TextDetailEffect;float height=text.GetPreferredValues(text.text,text.rectTransform.rect.width,float.PositiveInfinity).y;
                    Assert.That(height,Is.LessThanOrEqualTo(text.rectTransform.rect.height+.1f),language+" "+id+"详情必须完整可见");
                }
            }
        }
        [Test]public void UnavailableTeamLifeShowsNoRemainingPurchase()
        {h.Configure(r=>{r.SelfSlayerMart.Offers[0].ItemId="slayermart.item.24";r.SelfSlayerMart.Offers[0].Error="team_life_unavailable";});h.Network.PushConfigured();h.Show();Assert.That(h.Ui.BtnBuy.interactable,Is.False);Assert.That(h.Ui.TextDetailEffect.text,Does.Contain("剩余限购：0/1"));}
    }
}
