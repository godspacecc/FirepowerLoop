using System;
using System.Linq;
using System.Reflection;
using ET;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>真实HUD入口与窗口交接；临时场景根只提供既有五层UI端口，不是产品布局。</summary>
    public sealed class SlayerMartHudEntryTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        internal SlayerMartUiFlowTests h;internal ArcadeHudUICtrl controller;private GameObject root,fixtureRoot;internal ArcadeHudUI ui;
        [SetUp]public void Setup()
        {
            UIHelper.ResetForTests();h=new SlayerMartUiFlowTests();h.Setup();
            h.Root.SetActive(false);typeof(BaseUICtrl).GetField("_isActive",Private).SetValue(h.Controller,false);
            Configure();
            if(Object.FindObjectOfType<UIRoot>()==null)
            {
                fixtureRoot=new GameObject("SlayerMartUIFixture",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
                foreach(string name in new[]{"HUD","Dialog","Popup","Tips","Top"}){var layer=new GameObject(name,typeof(RectTransform));layer.transform.SetParent(fixtureRoot.transform,false);}
                var events=new GameObject("EventSystem");events.SetActive(false);events.transform.SetParent(fixtureRoot.transform,false);events.AddComponent<EventSystem>();events.AddComponent<InputSystemUIInputModule>();fixtureRoot.AddComponent<UIRoot>();
            }
            UIHelper.SetPrefabLoaderForTests(address=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/"+address+(address.EndsWith(".prefab",StringComparison.Ordinal)?"":".prefab")));
            root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"));ui=root.GetComponent<ArcadeHudUI>();controller=new ArcadeHudUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI",Private).SetValue(controller,ui);typeof(BaseUICtrl).GetField("_isActive",Private).SetValue(controller,true);typeof(ArcadeHudUICtrl).GetField("onlineMode",Private).SetValue(controller,true);
            typeof(ArcadeHudUICtrl).GetMethod("OnInit",Private).Invoke(controller,null);
        }
        internal void Configure(Action<ET.OnlineArcadeRunSnapshot> customize=null)
        {h.Configure(r=>{var player=OnlineArcadeBattlePlayerInfo.Create();player.PlayerId=10;player.Alive=true;player.CurrentHealth=player.MaxHealth=100;r.Battle.Players.Add(player);r.Battle.SlayerMartMembers[0].MenuOpen=false;customize?.Invoke(r);});h.Network.PushConfigured();}
        private T Field<T>(string name) where T:Object
        {var field=typeof(ArcadeHudUI).GetField(name);Assert.That(field,Is.Not.Null,"HUD缺少正式序列化入口："+name);var value=field.GetValue(ui);Assert.That(value,Is.Not.Null);return (T)value;}
        internal void Sync(){var method=typeof(ArcadeHudUICtrl).GetMethod("SyncSlayerMartUi",Private);Assert.That(method,Is.Not.Null,"HUD尚未接商店区域状态");method.Invoke(controller,null);}
        private ArcadeShopUI Window()
        {var shop=UIHelper.GetUICtrl<ArcadeShopUICtrl>();Assert.That(shop,Is.Not.Null);return (ArcadeShopUI)typeof(BaseUICtrl).GetField("BaseUI",Private).GetValue(shop);}
        [TearDown]public void Cleanup()
        {
            var shop=UIHelper.GetUICtrl<ArcadeShopUICtrl>();if(shop!=null){var view=(BaseUI)typeof(BaseUICtrl).GetField("BaseUI",Private).GetValue(shop);if(view!=null)Object.DestroyImmediate(view.gameObject);}
            if(root!=null)Object.DestroyImmediate(root);UIHelper.ResetForTests();if(fixtureRoot!=null)Object.DestroyImmediate(fixtureRoot);h.Cleanup();
        }
        [Test]public void NewHudControlsHaveBoundReferencesAndHiddenDefaults()
        {var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab").GetComponent<ArcadeHudUI>();foreach(var name in new[]{"BtnSlayerMart","BtnFieldUpgrade","TextStormCharge","TextArmor"}){var field=typeof(ArcadeHudUI).GetField(name);Assert.That(field,Is.Not.Null,name);var component=(Component)field.GetValue(prefab);Assert.That(component,Is.Not.Null);Assert.That(component.gameObject.activeSelf,Is.False);}}
        [Test]public void ShopEntryIsLargeEnoughForTouch()
        {var button=Field<Button>("BtnSlayerMart");Assert.That(((RectTransform)button.transform).rect.height,Is.GreaterThanOrEqualTo(96));}
        [Test]public void OrdinaryCombatNeverShowsShoppingEntry()
        {Configure(r=>{r.Battle.SlayerMartActive=false;r.SelfSlayerMart=null;});Sync();Assert.That(Field<Button>("BtnSlayerMart").gameObject.activeSelf,Is.False);Assert.That(UIHelper.GetUICtrl<ArcadeShopUICtrl>(),Is.Null);}
        [Test]public void ChoosingAutomaticallyOpensEntryDialog()
        {Configure(r=>{r.Battle.SlayerMartReady=r.SelfSlayerMart.Ready=false;r.Battle.SlayerMartMembers[0].Choice=0;});Sync();Assert.That(Window().ChoiceRoot.activeSelf,Is.True);}
        [Test]public void LoadingAutomaticallyOpensWaitingDialog()
        {Configure(r=>r.Battle.SlayerMartReady=r.SelfSlayerMart.Ready=false);Sync();Assert.That(Window().LoadingRoot.activeSelf,Is.True);}
        [Test]public void ReadyRegionOffersButtonWithoutForcingMenuOpen()
        {Sync();Assert.That(Field<Button>("BtnSlayerMart").gameObject.activeSelf,Is.True);Assert.That(UIHelper.GetUICtrl<ArcadeShopUICtrl>(),Is.Null);}
        [Test]public void HudClickOpensMenuAndSendsOnlyMenuIntent()
        {Sync();Field<Button>("BtnSlayerMart").onClick.Invoke();Assert.That(Window().ShoppingRoot.activeSelf,Is.True);var request=h.Network.LastRequest as C2G_OperateOnlineSlayerMart;Assert.That(request,Is.Not.Null);Assert.That(request.ActionKind,Is.EqualTo(4));Assert.That(request.Value,Is.True);}
        [Test]public void EnteredRegionClosesLoadingAndOffersManualShopping()
        {Configure(r=>r.Battle.SlayerMartReady=r.SelfSlayerMart.Ready=false);Sync();Configure();Sync();Assert.That(UIHelper.GetUICtrl<ArcadeShopUICtrl>().IsShow(),Is.False);Assert.That(Field<Button>("BtnSlayerMart").gameObject.activeSelf,Is.True);}
    }
}
