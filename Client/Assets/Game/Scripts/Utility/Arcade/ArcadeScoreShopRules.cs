using System;
using System.Collections.Generic;
using System.Linq;
#if DOTNET
using ShopTables = ET.Tables;
using ShopDropKind = ET.ArcadeDropKind;
using ShopPowerupBehavior = ET.ArcadePowerupBehavior;
#else
using ShopTables = global::Tables;
using ShopDropKind = global::ArcadeDropKind;
using ShopPowerupBehavior = global::ArcadePowerupBehavior;
#endif

namespace FirepowerLoop.Arcade
{
    /// <summary>积分商店动作，与旧现金商店协议隔离。</summary>
    public enum ScoreShopAction { Buy=1, Refresh=2, Skip=3 }
    /// <summary>只读价格与解锁范围，效果仍由正式Drop负责。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeScoreShopItem
    {
        /// <summary>正式空投身份，购买不复制另一份效果定义。</summary>
        public string DropId { get; }
        /// <summary>本次购买扣除的积分。</summary>
        public int Price { get; }
        /// <summary>最早可用于进入的普通关序号。</summary>
        public int MinArena { get; }
        /// <summary>最后可用关序号，零表示不限。</summary>
        public int MaxArena { get; }
        public ArcadeScoreShopItem(string id,int price,int min,int max)
        { DropId=id;Price=price;MinArena=min;MaxArena=max; }
        public bool Eligible(int arena)=>arena>=MinArena&&(MaxArena==0||arena<=MaxArena);
    }
    /// <summary>双端共用已校验目录，禁止把普通掉落或新效果加入商店。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeScoreShopConfiguration
    {
        /// <summary>每次刷新固定扣除积分，不随次数涨价。</summary>
        public int RefreshCost { get; }
        /// <summary>从现有空投校验得到的只读目录。</summary>
        public IReadOnlyList<ArcadeScoreShopItem> Items { get; }
        private ArcadeScoreShopConfiguration(int refresh,IEnumerable<ArcadeScoreShopItem> items)
        { RefreshCost=refresh;Items=Array.AsReadOnly(items.ToArray()); }
        public static ArcadeScoreShopConfiguration FromTables(ShopTables tables)
        {
            var rows=tables.TbArcadeScoreShop.DataList;
            var refresh=rows.Single(r=>r.Id=="refresh");
            if(refresh.DropId!="-"||refresh.ScoreCost<=0)throw new ArgumentException("积分商店刷新配置无效");
            var items=new List<ArcadeScoreShopItem>();
            foreach(var row in rows.Where(r=>r.Id!="refresh"))
            {
                var drop=tables.TbArcadeDrop.GetOrDefault(row.DropId);
                if(row.ScoreCost<=0||drop==null||drop.Weight<=0||drop.Amount<=0||drop.DurationSeconds<=0||
                    (drop.Kind!=ShopDropKind.TemporaryWeapon&&!(drop.Kind==ShopDropKind.Support&&
                        tables.TbArcadePowerup.GetOrDefault(drop.ProductId)?.Behavior==ShopPowerupBehavior.Drone)))
                    throw new ArgumentException("积分商品必须引用现有有效空投："+row.Id);
                items.Add(new ArcadeScoreShopItem(drop.Id,row.ScoreCost,drop.MinArena,drop.MaxArena));
            }
            if(items.Select(i=>i.DropId).Distinct().Count()!=items.Count||items.Count(i=>i.Eligible(2))<3)
                throw new ArgumentException("积分商店商品重复或首访不足三项");
            return new ArcadeScoreShopConfiguration(refresh.ScoreCost,items);
        }
    }
    /// <summary>个人整关商店账本；候选与随机状态一起持久化，重新打开不会免费刷新。</summary>
    [Serializable]
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeScoreShopState
    {
        /// <summary>本次访问对应的下一普通关序号，不是地图配置编号。</summary>
        public int NextArena { get; set; }
        /// <summary>每次成功操作递增，拒绝旧报价重复扣分。</summary>
        public int Revision { get; set; }=1;
        /// <summary>随交易保存的抽样状态，重开窗口不免费重抽。</summary>
        public uint Seed { get; set; }
        /// <summary>当前三个互不重复的正式空投身份。</summary>
        public List<string> Offers { get; set; }=new List<string>();
        /// <summary>已经购买一件或明确跳过。</summary>
        public bool Completed { get; set; }
        /// <summary>下一普通关待领取的空投；跳过时为空。</summary>
        public string PurchasedDropId { get; set; }
        /// <summary>效果已与下一关首轮节点一起保存，不得重复领取。</summary>
        public bool Delivered { get; set; }
        public ArcadeScoreShopState Clone()
        { var copy=(ArcadeScoreShopState)MemberwiseClone();copy.Offers=Offers?.ToList();return copy; }
    }
    /// <summary>只生成交易候选，不持有玩家、文件或战斗资源。</summary>
    public static class ArcadeScoreShopRules
    {
        public static ArcadeScoreShopState Open(ArcadeScoreShopConfiguration config,int nextArena,int seed)
        {
            if(config==null||nextArena<2)throw new ArgumentException("积分商店只能在整关后开放");
            var state=new ArcadeScoreShopState { NextArena=nextArena,Seed=unchecked((uint)seed) };
            state.Offers=Draw(config,state,Array.Empty<string>());
            if(state.Offers.Count!=3)throw new ArgumentException("当前关卡有效空投不足三项");
            return state;
        }
        /// <summary>验证持久化状态与当前配置，拒绝重复、越级及伪造的商品身份。</summary>
        public static bool IsValid(ArcadeScoreShopConfiguration config,ArcadeScoreShopState state)
            =>config!=null&&state!=null&&state.NextArena>=2&&state.Revision>0&&state.Offers!=null&&
                state.Offers.Count==3&&state.Offers.Distinct().Count()==3&&
                state.Offers.All(id=>config.Items.Any(i=>i.DropId==id&&i.Eligible(state.NextArena)))&&
                (state.PurchasedDropId==null||(state.Completed&&state.Offers.Contains(state.PurchasedDropId)))&&
                (!state.Delivered||state.Completed&&state.PurchasedDropId!=null);
        /// <summary>有限池三选一；优先抽上一组以外物品，池仅四种时也保证至少一项变化。</summary>
        private static List<string> Draw(ArcadeScoreShopConfiguration config,ArcadeScoreShopState state,IEnumerable<string> previous)
        {
            var old=new HashSet<string>(previous);var pool=config.Items.Where(i=>i.Eligible(state.NextArena)).Select(i=>i.DropId).ToList();
            for(int i=pool.Count-1;i>0;i--)
            {state.Seed=unchecked(state.Seed*1664525u+1013904223u);int j=(int)(state.Seed%(uint)(i+1));var value=pool[i];pool[i]=pool[j];pool[j]=value;}
            return pool.OrderBy(id=>old.Contains(id)?1:0).Take(3).ToList();
        }
        /// <summary>所有检查先于候选变化，调用方必须将积分和账本作为同一事务提交。</summary>
        public static bool TryOperate(ArcadeScoreShopConfiguration config,ArcadeScoreShopState current,long score,
            int revision,ScoreShopAction action,string dropId,out ArcadeScoreShopState next,out long nextScore,out string error)
        {
            next=null;nextScore=score;error="商店状态或请求已失效";
            if(!IsValid(config,current)||score<0||revision!=current.Revision||revision==int.MaxValue||current.Completed)return false;
            int cost=0;
            switch(action)
            {
                case ScoreShopAction.Buy:
                    var item=config.Items.FirstOrDefault(i=>i.DropId==dropId&&current.Offers.Contains(i.DropId)&&i.Eligible(current.NextArena));
                    if(item==null)return false;cost=item.Price;break;
                case ScoreShopAction.Refresh:
                    if(!config.Items.Any(i=>i.Eligible(current.NextArena)&&!current.Offers.Contains(i.DropId)))
                    {error="没有可以更换的商品";return false;}
                    cost=config.RefreshCost;break;
                case ScoreShopAction.Skip:break;
                default:return false;
            }
            if(score<cost){error="积分不足";return false;}
            next=current.Clone();next.Revision++;nextScore=score-cost;
            if(action==ScoreShopAction.Refresh)next.Offers=Draw(config,next,current.Offers);
            else {next.Completed=true;next.PurchasedDropId=action==ScoreShopAction.Buy?dropId:null;}
            error=null;return true;
        }
    }
}
