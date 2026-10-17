using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ET;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>正式Prefab和Controller的实际按钮测试；网络替身仅返回权威报价或超时。</summary>
    public sealed class SlayerMartUiFlowTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static FieldInfo ArchitectureField=>typeof(Architecture<Game>).GetField("mArchitecture",BindingFlags.Static|BindingFlags.NonPublic);
        private IResourceLoader previousLoader;private object previousLanguageCache;
        private object previousArchitecture;private Game architecture;private GameObject root;private ArcadeShopUI ui;private ArcadeShopUICtrl controller;
        private OnlineArcadeRunTests.FakeNetwork network;private OnlineArcadeRunSystem online;
        internal ArcadeShopUI Ui=>ui;internal GameObject Root=>root;internal ArcadeShopUICtrl Controller=>controller;internal Game Architecture=>architecture;internal OnlineArcadeRunTests.FakeNetwork Network=>network;internal OnlineArcadeRunSystem Online=>online;
        internal ET.OnlineArcadeRunSnapshot Wire {get;private set;}
        internal void Configure(Action<ET.OnlineArcadeRunSnapshot> customize=null)=>network.ConfigureRun=r=>{Shop(r);customize?.Invoke(r);Wire=r;};
        internal G2C_OperateOnlineSlayerMart Acknowledge(C2G_OperateOnlineSlayerMart r){var q=Wire.SelfSlayerMart;if(r.ActionKind==4)q.Members[0].MenuOpen=r.Value;if(r.ActionKind==5)q.Members[0].Finished=r.Value;q.Revision++;Wire.Battle.SlayerMartRevision=q.Revision;Wire.Battle.BattleRevision++;return new G2C_OperateOnlineSlayerMart{Accepted=true,Run=Wire};}
        private int calls;private C2G_OperateOnlineSlayerMart last;
        [SetUp] public void Setup()
        {
            calls=0;last=null;
            previousLoader=ResourceLoader.Current;previousLanguageCache=typeof(LocalizationLanguages).GetField("cachedConfig",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);ResourceLoader.Use(new LocalizationAssetResourceLoader());LocalizationLanguages.ResetForTests();
            previousArchitecture=ArchitectureField.GetValue(null);architecture=new Game();ArchitectureField.SetValue(null,architecture);
            typeof(Architecture<Game>).GetField("mInited",Private).SetValue(architecture,true);
            architecture.RegisterModel(new OnlineAccountModel());architecture.RegisterModel(new OnlineArcadeRunModel());architecture.RegisterModel(new LocalizationModel());
            architecture.GetModel<OnlineAccountModel>().Publish(new OnlineAccountSnapshot(OnlineConnectionState.Authenticated,OnlineOperationState.Idle,OnlineAccountResultCode.Success,"one",1,10,1,false,"one"));
            var config=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes("Assets/Game/Config/"+n+".bytes"))));architecture.RegisterUtility<IRuntimeConfigProvider>(config);
            Assert.That(config.TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);
            var loc=new LocalizationUtility(false);loc.LoadFromJson("zh-CN",File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));architecture.RegisterUtility(loc);
            network=new OnlineArcadeRunTests.FakeNetwork{ConfigFingerprint=catalog.Rule.ConfigFingerprint};architecture.RegisterUtility<global::Game.Network.INetworkRuntime>(network);
            Configure();online=new OnlineArcadeRunSystem();architecture.RegisterSystem(online);online.StartAsync().GetAwaiter().GetResult();
            network.OverrideResponse=request=>{calls++;last=(C2G_OperateOnlineSlayerMart)request;return new G2C_OperateOnlineSlayerMart{Accepted=true};};
            root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeShop.prefab"));ui=root.GetComponent<ArcadeShopUI>();controller=new ArcadeShopUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI",Private).SetValue(controller,ui);typeof(BaseUICtrl).GetField("_isActive",Private).SetValue(controller,true);Invoke("OnInit");
        }
        private static void Shop(ET.OnlineArcadeRunSnapshot r)
        {SlayerMartOnlineFlowTests.SetShop(r);r.SelfSlayerMart.Cash=100000;r.SelfSlayerMart.Bombs=0;r.SelfSlayerMart.Offers.Clear();r.SelfSlayerMart.Members[0].MenuOpen=true;
            var teammate=OnlineSlayerMartMemberInfo.Create();teammate.PlayerId=20;teammate.Choice=1;teammate.Lives=3;r.Battle.SlayerMartMembers.Add(teammate);r.SelfSlayerMart.Members.Add(teammate);
            for(int i=1;i<=16;i++){var offer=OnlineSlayerMartOfferInfo.Create();offer.ItemId="slayermart.item."+i.ToString("00");r.SelfSlayerMart.Offers.Add(offer);}}
        private void Invoke(string method)=>typeof(ArcadeShopUICtrl).GetMethod(method,Private).Invoke(controller,null);
        internal void Show(){root.SetActive(true);typeof(BaseUICtrl).GetField("_isActive",Private).SetValue(controller,true);Invoke("OnShow");}
        [TearDown] public void Cleanup()
        {if(root!=null){Invoke("OnHide");Object.DestroyImmediate(root);}architecture?.Deinit();ArchitectureField.SetValue(null,previousArchitecture);ResourceLoader.Use(previousLoader);typeof(LocalizationLanguages).GetField("cachedConfig",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,previousLanguageCache);}
        [Test] public void ShoppingDoesNotPauseDomainClock()
        {Assert.That(controller.BlocksGame,Is.False);Assert.That(controller.Layer(),Is.EqualTo(UILayer.Dialog));}
        [Test] public void ControllerLoadsPreservedRelativeResourcePath()
        {Assert.That(typeof(ArcadeShopUICtrl).GetMethod("Path",Private).Invoke(controller,null),Is.EqualTo("Arcade/ArcadeShop"));}
        [Test] public void ShoppingRendersSixteenAuthoritativeCardsAndOwnCash()
        {Show();Assert.That(ui.ShoppingRoot.activeSelf,Is.True);Assert.That(ui.ChoiceRoot.activeSelf,Is.False);Assert.That(ui.OfferList.GetComponentsInChildren<ArcadeShopOfferBoxUI>().Length,Is.EqualTo(16));Assert.That(ui.TextCash.text,Does.Contain("100,000"));Assert.That(calls,Is.Zero);}
        [Test] public void ChoosingShowsExplicitEnterSkipWithoutShopping()
        {network.OverrideResponse=null;network.ConfigureRun=r=>{Shop(r);r.Battle.SlayerMartReady=r.SelfSlayerMart.Ready=false;r.Battle.SlayerMartMembers[0].Choice=0;};network.PushConfigured();Show();Assert.That(ui.ChoiceRoot.activeSelf,Is.True);Assert.That(ui.ShoppingRoot.activeSelf,Is.False);}
        [Test] public void LoadingShowsWaitingRootWithoutStartingCountdownLocally()
        {network.OverrideResponse=null;network.ConfigureRun=r=>{Shop(r);r.Battle.SlayerMartReady=r.SelfSlayerMart.Ready=false;};network.PushConfigured();Show();Assert.That(ui.LoadingRoot.activeSelf,Is.True);Assert.That(ui.ShoppingRoot.activeSelf,Is.False);}
        [Test] public void SelectingCardUpdatesDetailWithoutSendingPurchase()
        {Show();var cells=ui.OfferList.GetComponentsInChildren<ArcadeShopOfferBoxUI>();cells[1].BtnSelect.onClick.Invoke();Assert.That(ui.TextDetailName.text,Does.Contain("冲刺"));Assert.That(calls,Is.Zero);}
        [Test] public void BuySubmitsOneIntentAndDoesNotDebitCashLocally()
        {Show();ui.BtnBuy.onClick.Invoke();Assert.That(calls,Is.EqualTo(1));Assert.That(last.ItemId,Is.EqualTo("slayermart.item.01"));Assert.That(last.Generation,Is.EqualTo(4));Assert.That(last.ActionKind,Is.EqualTo(1));Assert.That(last.OperationId,Has.Length.EqualTo(32));Assert.That(online.GetSlayerMartSnapshot().Cash,Is.EqualTo(100000));}
        [Test] public void TimeoutDisablesNewPurchaseAndRetryUsesSameId()
        {network.OverrideResponse=request=>{calls++;last=(C2G_OperateOnlineSlayerMart)request;throw new TimeoutException();};Show();ui.BtnBuy.onClick.Invoke();string id=last?.OperationId;Assert.That(calls,Is.EqualTo(1));Assert.That(ui.BtnBuy.interactable,Is.False);ui.BtnBuy.onClick.Invoke();Assert.That(calls,Is.EqualTo(1));ui.BtnRetry.onClick.Invoke();Assert.That(calls,Is.EqualTo(2));Assert.That(last.OperationId,Is.EqualTo(id));}
        [Test] public void RepeatedShowDoesNotAddDuplicateBuyListener()
        {Show();Show();ui.BtnBuy.onClick.Invoke();Assert.That(calls,Is.EqualTo(1));}
        [Test] public void RerollSubmitsIndependentActionWithoutLocalRedraw()
        {Show();var names=ui.OfferList.GetComponentsInChildren<ArcadeShopOfferBoxUI>().Select(c=>c.TextName.text).ToArray();ui.BtnReroll.onClick.Invoke();Assert.That(calls,Is.EqualTo(1));Assert.That(last.ActionKind,Is.EqualTo(2));Assert.That(ui.OfferList.GetComponentsInChildren<ArcadeShopOfferBoxUI>().Select(c=>c.TextName.text),Is.EqualTo(names));}
        [Test] public void ClosingAuthorityHidesMenuEvenWithPreviousSelection()
        {network.OverrideResponse=null;network.ConfigureRun=r=>{Shop(r);r.Battle.SlayerMartClosing=r.SelfSlayerMart.Closing=true;};network.PushConfigured();Show();Assert.That(root.activeSelf,Is.False);}
        [Test] public void FormalLocalizationCoversAllTwentyFourNamesAndEffects()
        {foreach(var language in new[]{"zh-CN","en-US"}){var json=File.ReadAllText("Assets/Game/Config/Localization/"+language+".json");for(int i=1;i<=24;i++){Assert.That(json.Contains("ui.arcade.slayermart.item."+i.ToString("00")+".name"),Is.True,language+" item name "+i);Assert.That(json.Contains("ui.arcade.slayermart.item."+i.ToString("00")+".desc"),Is.True,language+" item desc "+i);}}}
    }
}
