using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using ET;
using NUnit.Framework;
using QFramework;
namespace Game.Tests
{
    /// <summary>普通槽与购买支援可以并存；只有真实被接触体替代的槽才隐藏。</summary>
    public sealed class SlayerMartOrdinaryVisualTests
    {
        private SlayerMartUiFlowTests h;
        [SetUp]public void Setup(){h=new SlayerMartUiFlowTests();h.Setup();}
        [TearDown]public void Cleanup()=>h.Cleanup();
        [TestCase(ArcadePowerupBehavior.Chicken,1,1)]
        [TestCase(ArcadePowerupBehavior.ElectricOrb,3,1)]
        [TestCase(ArcadePowerupBehavior.Saw,2,0)]
        [TestCase(ArcadePowerupBehavior.Barrel,1,0)]
        public void NormalSlotVisibilityMatchesAuthoritativeBodyReplacement(ArcadePowerupBehavior behavior,int bodies,int ordinaryVisible)
        {
            Assert.That(h.Architecture.GetUtility<IRuntimeConfigProvider>().TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);
            var definition=catalog.Powerups.Single(p=>p.Behavior==behavior);
            var layout=catalog.RoundLayouts.First(p=>p.ArenaConfigNumber==3);
            h.Configure(r=>{r.Battle.SlayerMartActive=false;r.Battle.BattleRevision=8;r.Battle.RoundLayoutId=layout.Id;r.Battle.RoundLayoutVersion=layout.Version;r.SelfSlayerMart=null;var player=OnlineArcadeBattlePlayerInfo.Create();player.PlayerId=10;player.Alive=true;player.CurrentHealth=player.MaxHealth=100;
                var ordinary=OnlineArcadePowerupInfo.Create();ordinary.ProductId=definition.Id;ordinary.RemainingSeconds=20;player.Powerups.Add(ordinary);
                for(int i=0;i<bodies;i++){var body=OnlineSlayerMartSupportInfo.Create();body.ProductId=definition.Id;body.InstanceId="purchased";body.BodyIndex=i;body.RemainingSeconds=100;player.SlayerMartSupports.Add(body);}r.Battle.Players.Add(player);});h.Network.PushConfigured();
            var pair=typeof(SlayerMartOnlineProjectionTests).GetMethod("Present",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{h.Online.Snapshot});
            var view=pair.GetType().GetField("Item2").GetValue(pair);
            Assert.That((int)view.GetType().GetField("LegacyPowerups").GetValue(view),Is.EqualTo(ordinaryVisible));
            Assert.That(((IList)view.GetType().GetField("Bodies").GetValue(view)).Count,Is.EqualTo(bodies));
        }
    }
}
