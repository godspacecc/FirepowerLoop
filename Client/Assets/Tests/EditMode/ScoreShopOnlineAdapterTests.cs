using System.Reflection;
using FirepowerLoop.Arcade;
using Newtonsoft.Json;
using NUnit.Framework;
using QFramework;
namespace Game.Tests
{
    /// <summary>本人账本推送必须结束已落定的未知交易，不能把旧关购买意图带到下一次访问。</summary>
    public sealed class ScoreShopOnlineAdapterTests
    {
        private Game architecture;private OnlineArcadeRunSystem system;
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            TestReset.ResetArchitecture();architecture=new Game();
            architecture.RegisterModel(new OnlineAccountModel());architecture.RegisterModel(new OnlineArcadeRunModel());
            architecture.GetModel<OnlineAccountModel>().Publish(new OnlineAccountSnapshot(OnlineConnectionState.Authenticated,OnlineOperationState.Idle,OnlineAccountResultCode.Success,"one",1,10,1,false,"one"));
            system=new OnlineArcadeRunSystem();architecture.RegisterSystem(system);
            Observe(new ArcadeScoreShopState{NextArena=2});
            Set("pendingScoreShopOperation","original-buy");Set("pendingScoreShopRun",100L);Set("pendingScoreShopPlayer",10L);
            Set("pendingScoreShopAction",ScoreShopAction.Buy);Set("pendingScoreShopDrop","arcade.drop.weapon.rapid");
            typeof(OnlineArcadeRunSystem).GetField("pendingScoreShopArena",Hidden)?.SetValue(system,2);
        }
        [TearDown] public void Cleanup(){architecture.Deinit();TestReset.ResetArchitecture();}
        private void Set(string name,object value)=>typeof(OnlineArcadeRunSystem).GetField(name,Hidden).SetValue(system,value);
        private void Observe(ArcadeScoreShopState shop)
        {
            var source=ET.OnlineArcadeRunSnapshot.Create();source.RunId=100;
            var player=ET.OnlineArcadePlayerInfo.Create();player.PlayerId=10;player.ScoreShopJson=JsonConvert.SerializeObject(shop);source.Players.Add(player);
            typeof(OnlineArcadeRunSystem).GetMethod("ObserveScoreShop",Hidden).Invoke(system,new object[]{source});
        }
        [Test] public void CompletedPurchasePush_ClearsUnknownIntent()
        {Observe(new ArcadeScoreShopState{NextArena=2,Completed=true,PurchasedDropId="arcade.drop.weapon.rapid"});Assert.That(system.HasPendingScoreShopOperation,Is.False);}
        [Test] public void NewVisitAfterReconnect_DoesNotLockNewOffersToOldPurchase()
        {
            typeof(OnlineArcadeRunSystem).GetMethod("ClearScoreShop",Hidden).Invoke(system,new object[]{false});
            Assert.That(system.HasPendingScoreShopOperation,Is.True,"断线本身不能丢掉未知交易");
            Observe(new ArcadeScoreShopState{NextArena=3});Assert.That(system.HasPendingScoreShopOperation,Is.False);
            Assert.That(system.GetScoreShopState().NextArena,Is.EqualTo(3));
        }
    }
}
