using System;
using System.Collections.Generic;
using System.Linq;
using FirepowerLoop.Arcade;

namespace Game
{
    /// <summary>商店选择、资源准备、购物与返回的只读阶段。</summary>
    public enum ArcadeSlayerMartStage { Choosing, Loading, Shopping, Closing }
    /// <summary>单张个人报价的只读投影，禁用原因来自相同购买前置规则。</summary>
    public sealed class ArcadeSlayerMartOfferSnapshot
    {
        public ArcadeSlayerMartItem Item { get; }
        public string Error { get; }
        public ArcadeSlayerMartOfferSnapshot(ArcadeSlayerMartItem item,string error){Item=item;Error=error;}
    }
    /// <summary>只公开队友状态，不携带其现金、券、个人卡牌或重抽序列。</summary>
    public readonly struct ArcadeSlayerMartMemberSnapshot
    {
        public long PlayerId { get; }
        public int Choice { get; }
        public bool MenuOpen { get; }
        public bool Finished { get; }
        public ArcadeSlayerMartMemberSnapshot(ArcadeSlayerMartPlayer player)
        {PlayerId=player.PlayerId;Choice=player.Choice;MenuOpen=player.MenuOpen;Finished=player.Finished;}
    }
    /// <summary>界面只读的本人商店快照；不能通过此对象修改运行账本。</summary>
    public sealed class ArcadeSlayerMartSnapshot
    {
        public string RegionInstanceId { get; }
        public int Generation { get; }
        public long Revision { get; }
        /// <summary>当前店次和本人身份，不能用队友身份发出个人交易。</summary>
        public int Visit { get; }
        public long PlayerId { get; }
        public float RemainingSeconds { get; }
        /// <summary>个人现金和券，不包含队友的经济信息。</summary>
        public long Cash { get; }
        public int Tokens { get; }
        public ArcadeSlayerMartStage Stage { get; }
        /// <summary>资源准备错误只供显示及显式重试，不会重置库存。</summary>
        public string Error { get; }
        public IReadOnlyList<ArcadeSlayerMartOfferSnapshot> Offers { get; }
        public IReadOnlyList<ArcadeSlayerMartMemberSnapshot> Members { get; }
        public ArcadeSlayerMartSnapshot(ArcadeSlayerMartState state,long playerId,ArcadeSlayerMartCatalog catalog,string loadError=null)
        {
            var player=state.Players.Single(p=>p.PlayerId==playerId);
            RegionInstanceId=state.RegionInstanceId;Generation=state.Generation;Revision=state.Revision;Visit=state.Visit;PlayerId=playerId;
            RemainingSeconds=state.RemainingSeconds;Cash=player.Cash;Tokens=player.Tokens;
            Error=loadError;
            Stage=state.Closing?ArcadeSlayerMartStage.Closing:state.Ready?ArcadeSlayerMartStage.Shopping:
                state.Players.All(p=>p.Choice==1)?ArcadeSlayerMartStage.Loading:ArcadeSlayerMartStage.Choosing;
            Offers=Array.AsReadOnly(player.Offers.Select(id=>
            {
                var item=catalog.Items.Single(p=>p.Id==id);string error=ArcadeSlayerMartRules.PurchaseError(state,player,item,catalog);
                return new ArcadeSlayerMartOfferSnapshot(item,error??(player.Cash<item.Price?"insufficient_cash":null));
            }).ToArray());
            Members=Array.AsReadOnly(state.Players.Select(p=>new ArcadeSlayerMartMemberSnapshot(p)).ToArray());
        }
    }
}
