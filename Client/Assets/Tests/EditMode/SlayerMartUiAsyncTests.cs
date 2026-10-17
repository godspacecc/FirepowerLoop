using System.Reflection;
using System.Collections;
using UnityEngine.TestTools;
using System.Threading.Tasks;
using ET;
using FirepowerLoop.Arcade;
using NUnit.Framework;
using QFramework;

namespace Game.Tests
{
    public sealed class SlayerMartUiAsyncTests
    {
        private SlayerMartUiFlowTests h;
        [SetUp]public void Setup(){h=new SlayerMartUiFlowTests();h.Setup();}
        [TearDown]public void Cleanup()=>h.Cleanup();
        [UnityTest]public IEnumerator NewRegionImmediatelyReplacesOldMenu()=>Check(false);
        [UnityTest]public IEnumerator NewRegionWithMissingQuoteReplacesOldMenu()=>Check(true);
        private IEnumerator Check(bool missingQuote)
        {
            var completion=new TaskCompletionSource<IResponse>();h.Network.OverrideAsyncResponse=_=>completion.Task;h.Show();
            var pending=(Task<bool>)typeof(ArcadeShopUICtrl).GetMethod("SendAsync",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(h.Controller,new object[]{SlayerMartAction.Buy,false,"slayermart.item.01",2,null});
            if(missingQuote){h.Configure(r=>{r.Battle.SlayerMartGeneration=r.Battle.RoundGeneration=5;r.Battle.BattleRevision=8;r.SelfSlayerMart=null;});h.Network.PushConfigured();}
            h.Configure(r=>{r.Battle.SlayerMartGeneration=r.Battle.RoundGeneration=r.SelfSlayerMart.Generation=5;r.Battle.BattleRevision=9;r.Battle.SlayerMartRevision=r.SelfSlayerMart.Revision=8;});h.Network.PushConfigured();h.Show();
            bool canBuyBeforeOldResponse=h.Ui.BtnBuy.interactable;
            completion.SetResult(new G2C_OperateOnlineSlayerMart{Accepted=true});
            for(int frame=0;frame<20&&!pending.IsCompleted;frame++)yield return null;
            Assert.That(pending.IsCompleted,Is.True);pending.GetAwaiter().GetResult();
            h.Architecture.SendEvent(new LanguageChangedEvent("zh-CN","zh-CN"));
            Assert.That(canBuyBeforeOldResponse,Is.True,"新区域不能继承旧显示的请求锁");
            Assert.That(h.Ui.TextMessage.text,Does.Not.Contain("暂未确认"),"旧回调不能写入新区域的错误提示");
        }
    }
}
