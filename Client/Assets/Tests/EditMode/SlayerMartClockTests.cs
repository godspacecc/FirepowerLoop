using System;
using System.Reflection;
using FirepowerLoop.Arcade;
using NUnit.Framework;
using QFramework;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>通过实际OnUpdate采用的帧适配器验证真实期限，不用战斗小步长计时。</summary>
    public sealed class SlayerMartClockTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private SlayerMartRegionFlowTests h;private double now;
        private ArcadeSlayerMartPlayer Player=>h.runtime.SlayerMart.Players[0];
        [SetUp]public void Setup()
        {
            now=0;h=new SlayerMartRegionFlowTests();h.SetUp();h.architecture.RegisterModel(new AppStateModel());h.architecture.GetModel<AppStateModel>().CurrentState.Value=AppState.Playing;h.Shopping();
            var clock=typeof(ArcadeCombatSystem).GetField("slayerMartRealtime",Private);Assert.That(clock,Is.Not.Null,"缺少独立实际时钟");clock.SetValue(h.combat,new Func<double>(()=>now));Frame(0,0);
        }
        [TearDown]public void Cleanup(){h?.TearDown();}
        private void Frame(float simulated,double elapsed)
        {now+=elapsed;var method=typeof(ArcadeCombatSystem).GetMethod("TickFrame",Private);Assert.That(method,Is.Not.Null,"实际Update缺少双时钟帧适配");method.Invoke(h.combat,new object[]{h.runtime,simulated});}
        [Test]public void FifteenFpsStillConsumesTenRealSeconds(){for(int i=0;i<150;i++)Frame(1f/15,1d/15);Assert.That(h.runtime.SlayerMart.RemainingSeconds,Is.EqualTo(290).Within(.001));}
        [Test]public void LongFrameConsumesFullElapsedTime(){Frame(3,3);Assert.That(h.runtime.SlayerMart.RemainingSeconds,Is.EqualTo(297).Within(.001));}
        [Test]public void PersonalMenuNeverPausesDeadline(){Player.MenuOpen=true;Frame(10,10);Assert.That(h.runtime.SlayerMart.RemainingSeconds,Is.EqualTo(290).Within(.001));}
        [Test]public void PauseDoesNotAccumulateCatchup(){h.architecture.GetModel<AppStateModel>().CurrentState.Value=AppState.Paused;Frame(0,20);h.architecture.GetModel<AppStateModel>().CurrentState.Value=AppState.Playing;Frame(0,20);Assert.That(h.runtime.SlayerMart.RemainingSeconds,Is.EqualTo(300));Frame(.1f,1);Assert.That(h.runtime.SlayerMart.RemainingSeconds,Is.EqualTo(299));}
        [Test]public void LoadingReadinessDoesNotConsumeDeadline(){h.view.Ready=false;Frame(.1f,20);Assert.That(h.combat.TryOperateSlayerMart(Buy("loading"),out _),Is.False);h.view.Ready=true;Frame(.1f,20);Assert.That(h.runtime.SlayerMart.RemainingSeconds,Is.EqualTo(300));Frame(.1f,1);Assert.That(h.runtime.SlayerMart.RemainingSeconds,Is.EqualTo(299));}
        private ArcadeSlayerMartRequest Buy(string id)
        {Player.Cash=100000;const string item="slayermart.item.01";if(!Player.Offers.Contains(item)){Player.Offers.RemoveAt(0);Player.Offers.Add(item);}return new ArcadeSlayerMartRequest{Action=SlayerMartAction.Buy,PlayerId=1,RegionInstanceId=h.runtime.SlayerMart.RegionInstanceId,Generation=h.runtime.SlayerMart.Generation,ExpectedRevision=h.runtime.SlayerMart.Revision,OperationId=id,ItemId=item,ConfirmedAmount=2};}
        [Test]public void PurchaseAfterDeadlineBeforeNextFrameIsRejected(){var request=Buy("too-late");now=301;Assert.That(h.combat.TryOperateSlayerMart(request,out _),Is.False);Assert.That(Player.Cash,Is.EqualTo(100000));Assert.That(h.runtime.SlayerMart.Closing,Is.True);}
        [Test]public void AcceptedReceiptCanReplayAfterDeadlineWithoutSecondDebit(){var request=Buy("before-deadline");Assert.That(h.combat.TryOperateSlayerMart(request,out var error),Is.True,error);long cash=Player.Cash;now=301;Assert.That(h.combat.TryOperateSlayerMart(request,out error),Is.True,error);Assert.That(Player.Cash,Is.EqualTo(cash));Assert.That(h.runtime.SlayerMart.Closing,Is.True);}
        [Test]public void LongFrameDoesNotAccelerateShopMovement()
        {
            using var input=new PlayerInputCtrl();typeof(ArcadeCombatSystem).GetField("input",Private).SetValue(h.combat,input);
            // 本用例只验证帧适配的位移上限，连续设备采集不在编辑模式时钟测试中混测。
            typeof(PlayerInputCtrl).GetProperty("MoveInput").SetValue(input,Vector2.right);
            Assert.That(UIHelper.HasUIOpen,Is.False);Assert.That(Player.MenuOpen,Is.False);
            h.runtime.PlayerPosition=Vector2.zero;Frame(.5f,.5);
            Assert.That(h.runtime.PlayerPosition.x,Is.GreaterThan(0));
            Assert.That(h.runtime.PlayerPosition.magnitude,Is.LessThanOrEqualTo(h.catalog.Rule.PlayerMoveSpeed*.05f+.001f));
            Assert.That(h.runtime.SlayerMart.RemainingSeconds,Is.EqualTo(299.5).Within(.001));
        }
    }
}
