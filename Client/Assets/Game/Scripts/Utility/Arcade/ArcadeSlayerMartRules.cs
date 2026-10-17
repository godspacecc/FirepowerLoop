using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FirepowerLoop.Arcade
{
    /// <summary>区域消费纯规则：只计算候选，不修改宿主工作态。</summary>
    public static class ArcadeSlayerMartRules
    {
        public static ArcadeSlayerMartState Create(long runId,int visit,IReadOnlyList<long> members,ulong seed,ArcadeSlayerMartCatalog catalog)
            =>runId>0?Create(runId.ToString(CultureInfo.InvariantCulture),visit,members,seed,catalog):throw new ArgumentException("非法本局身份");

        /// <summary>单人使用完整GUID，双人使用服务器ID；不截断或Hash本局身份。</summary>
        public static ArcadeSlayerMartState Create(string runId,int visit,IReadOnlyList<long> members,ulong seed,ArcadeSlayerMartCatalog catalog)
        {
            if(string.IsNullOrWhiteSpace(runId)||runId.Length>128||runId.Contains(":" )||catalog==null||members==null||members.Count<1||members.Count>2||members.Any(p=>p<=0)||members.Distinct().Count()!=members.Count)
                throw new ArgumentException("非法本局或原始成员");
            var region=catalog.Regions.SingleOrDefault(p=>p.Visit==visit)??throw new ArgumentException("商店区域不存在");
            var s=new ArcadeSlayerMartState {RegionInstanceId=runId+":slayermart:"+visit,
                Visit=visit,Generation=visit,Revision=1,Seed=seed,RemainingSeconds=region.Duration};
            foreach(long id in members.OrderBy(p=>p))
            {
                var player=new ArcadeSlayerMartPlayer{PlayerId=id};
                player.Offers=Draw(s,player,catalog);s.Players.Add(player);
            }
            return s;
        }

        /// <summary>身份、去重、前置、完整候选按此顺序验证；拒绝不改变输入或记录成功回执。</summary>
        public static ArcadeSlayerMartResult Evaluate(ArcadeSlayerMartState current,ArcadeSlayerMartRequest request,ArcadeSlayerMartCatalog catalog)
        {
            try
            {
                if(!Valid(current,catalog)||request==null||string.IsNullOrWhiteSpace(request.OperationId)||request.OperationId.Length>128||
                    request.RegionInstanceId!=current.RegionInstanceId||request.Generation!=current.Generation||!Enum.IsDefined(typeof(SlayerMartAction),request.Action))return Fail("invalid_identity");
                var source=current.Players.SingleOrDefault(p=>p.PlayerId==request.PlayerId);
                if(source==null)return Fail("invalid_player");
                string fingerprint=Fingerprint(request);
                var receipt=current.Receipts.FirstOrDefault(p=>p.PlayerId==request.PlayerId&&p.OperationId==request.OperationId);
                if(receipt!=null)return receipt.Fingerprint==fingerprint
                    ? new ArcadeSlayerMartResult{Success=true,Replayed=true,Candidate=current.Clone()}:Fail("operation_payload_changed");
                if(request.ExpectedRevision!=current.Revision)return Fail("stale_revision");
                if(current.Closing||current.RemainingSeconds<=0)return Fail("closed");
                bool choice=request.Action==SlayerMartAction.SelectEnter||request.Action==SlayerMartAction.SelectSkip;
                if(!choice&&!current.Ready)return Fail("not_ready");
                var next=current.Clone();var player=next.Players.Single(p=>p.PlayerId==request.PlayerId);
                var grants=new List<ArcadeSlayerMartGrant>();string error=null;
                switch(request.Action)
                {
                    case SlayerMartAction.Buy:
                        error=Buy(next,player,request,catalog,grants);break;
                    case SlayerMartAction.Reroll:
                        error=Reroll(next,player,catalog);break;
                    case SlayerMartAction.CollectCash:
                        var region=catalog.Regions.Single(p=>p.Visit==next.Visit);
                        if(request.BoxId<1||request.BoxId>region.CashBoxes||next.CollectedBoxes.Contains(request.BoxId))return Fail("cash_unavailable");
                        foreach(var member in next.Players)member.Cash=checked(member.Cash+region.CashPerMember);
                        next.CollectedBoxes.Add(request.BoxId);break;
                    case SlayerMartAction.SetMenuOpen:
                        player.MenuOpen=request.Value;break;
                    case SlayerMartAction.SetFinished:
                        if(request.Value&&player.MenuOpen)return Fail("menu_still_open");
                        player.Finished=request.Value;break;
                    case SlayerMartAction.SelectEnter: player.Choice=1;break;
                    case SlayerMartAction.SelectSkip: player.Choice=2;break;
                }
                if(error!=null)return Fail(error);
                next.Revision=checked(next.Revision+1);
                next.PendingGrants.AddRange(grants.Select(p=>p.Clone()));
                next.Receipts.Add(new ArcadeSlayerMartReceipt{PlayerId=request.PlayerId,OperationId=request.OperationId,Fingerprint=fingerprint,Grants=grants});
                return new ArcadeSlayerMartResult{Success=true,Candidate=next};
            }
            catch(OverflowException){return Fail("numeric_overflow");}
        }

        /// <summary>购买只按配置计算，本局唯一、当次限购、前置和全部效果容量先于扣款。</summary>
        private static string Buy(ArcadeSlayerMartState state,ArcadeSlayerMartPlayer player,ArcadeSlayerMartRequest request,
            ArcadeSlayerMartCatalog catalog,List<ArcadeSlayerMartGrant> grants)
        {
            var item=catalog.Items.FirstOrDefault(p=>p.Id==request.ItemId);
            if(item==null||!player.Offers.Contains(item.Id))return "not_offered";
            string error=PurchaseError(state,player,item,catalog);if(error!=null)return error;
            if(player.Cash<item.Price)return "insufficient_cash";
            int amount=item.Amount;
            if(item.Effect==SlayerMartEffect.Bomb||item.Effect==SlayerMartEffect.Dash)
            {
                int owned=item.Effect==SlayerMartEffect.Bomb?player.Bombs:player.Dashes;
                int cap=item.Effect==SlayerMartEffect.Bomb?catalog.MaxBombs:catalog.MaxDashes;
                amount=Math.Min(amount,cap-owned);
                if(request.ConfirmedAmount!=amount)return "confirm_actual_amount";
                if(item.Effect==SlayerMartEffect.Bomb)player.Bombs+=amount;else player.Dashes+=amount;
            }
            else if(item.Effect==SlayerMartEffect.Tokens)player.Tokens=checked(player.Tokens+amount);
            else if(item.Effect==SlayerMartEffect.Vest)player.VestSlots=1;
            else if(item.Effect==SlayerMartEffect.TacticalVest)player.VestSlots=2;
            else if(item.Effect==SlayerMartEffect.Plate)player.Plates++;
            else if(item.Effect==SlayerMartEffect.Chicken||item.Effect==SlayerMartEffect.ChickenPack)player.Chickens+=amount;
            else if(item.Effect==SlayerMartEffect.Gems)
            {
                ulong random=Mix(state.Seed^(ulong)state.Visit^(ulong)player.PlayerId^0x47454D53UL);
                amount=catalog.GemMinimum+(int)Next(ref random,(uint)(catalog.GemMaximum-catalog.GemMinimum+1));
            }
            player.Cash-=item.Price;
            if(item.OncePerRun)player.Owned.Add(item.Id);
            if(item.Effect!=SlayerMartEffect.Plate)player.Bought.Add(item.Id);
            var targets=item.Effect==SlayerMartEffect.NextRoundLife?state.Players:new List<ArcadeSlayerMartPlayer>{player};
            if(item.Effect==SlayerMartEffect.NextRoundLife)state.TeamLifeBought=true;
            foreach(var target in targets)
            {
                state.GrantSequence=checked(state.GrantSequence+1);
                grants.Add(new ArcadeSlayerMartGrant{EventId=state.RegionInstanceId+":"+state.GrantSequence.ToString(CultureInfo.InvariantCulture),
                    PlayerId=target.PlayerId,Effect=item.Effect,Amount=amount,Duration=item.Duration});
            }
            return null;
        }

        /// <summary>此错误码也供只读卡片禁用说明使用；不因余额改抽取权重。</summary>
        public static string PurchaseError(ArcadeSlayerMartState state,ArcadeSlayerMartPlayer player,ArcadeSlayerMartItem item,ArcadeSlayerMartCatalog catalog)
        {
            if(item.OncePerRun&&player.Owned.Contains(item.Id))return "already_owned";
            if(item.Effect!=SlayerMartEffect.Plate&&player.Bought.Contains(item.Id))return "visit_limit";
            if(player.Lives<=0&&item.Effect!=SlayerMartEffect.Tokens&&item.Effect!=SlayerMartEffect.NextRoundLife)return "player_dead";
            if(item.Effect==SlayerMartEffect.Bomb&&player.Bombs>=catalog.MaxBombs||item.Effect==SlayerMartEffect.Dash&&player.Dashes>=catalog.MaxDashes)return "supply_full";
            if(item.Effect==SlayerMartEffect.Vest&&player.VestSlots>=1||item.Effect==SlayerMartEffect.TacticalVest&&player.VestSlots>=2)return "vest_owned";
            if(item.Effect==SlayerMartEffect.Plate&&(player.VestSlots==0||player.Plates>=player.VestSlots))return "plate_capacity";
            if((item.Effect==SlayerMartEffect.Chicken||item.Effect==SlayerMartEffect.ChickenPack)&&item.Amount>catalog.MaxChickens-player.Chickens)return "chicken_capacity";
            if(item.Effect==SlayerMartEffect.NextRoundLife&&(state.TeamLifeBought||state.Players.All(p=>p.Lives<=0)||state.Players.All(p=>p.Lives>=catalog.MaxLives)))return "team_life_unavailable";
            return null;
        }

        /// <summary>用未拥有候选优先抽取，禁用卡只在不足16种时补足，不随刷新重置限购。</summary>
        private static List<string> Draw(ArcadeSlayerMartState state,ArcadeSlayerMartPlayer player,ArcadeSlayerMartCatalog catalog)
        {
            ulong seed=Mix(state.Seed^(ulong)player.PlayerId^((ulong)state.Visit<<32)^(ulong)player.RerollSequence);
            var active=catalog.Items.Where(p=>!player.Owned.Contains(p.Id)&&!player.Bought.Contains(p.Id)).Select(p=>p.Id).OrderBy(p=>p,StringComparer.Ordinal).ToList();
            var disabled=catalog.Items.Where(p=>player.Owned.Contains(p.Id)||player.Bought.Contains(p.Id)).Select(p=>p.Id).OrderBy(p=>p,StringComparer.Ordinal).ToList();
            Shuffle(active,ref seed);Shuffle(disabled,ref seed);
            return active.Concat(disabled).Take(catalog.Cards).ToList();
        }

        private static string Reroll(ArcadeSlayerMartState state,ArcadeSlayerMartPlayer player,ArcadeSlayerMartCatalog catalog)
        {
            if(player.Tokens<=0)return "no_token";
            var active=catalog.Items.Where(p=>!player.Owned.Contains(p.Id)&&!player.Bought.Contains(p.Id)).Select(p=>p.Id).ToList();
            if(!active.Except(player.Offers).Any())return "no_inventory_change";
            var before=new HashSet<string>(player.Offers,StringComparer.Ordinal);
            player.RerollSequence=checked(player.RerollSequence+1);
            var after=Draw(state,player,catalog);
            if(before.SetEquals(after))
            {
                string replacement=active.Where(p=>!before.Contains(p)).OrderBy(p=>p,StringComparer.Ordinal).First();
                after[after.Count-1]=replacement;
            }
            player.Offers=after;player.Tokens--;return null;
        }

        /// <summary>跨进程固定算法；不使用运行时随机Hash或操作ID，避免重启和改请求刷库存。</summary>
        private static ulong Mix(ulong value)
        { unchecked{value+=0x9E3779B97F4A7C15UL;value=(value^(value>>30))*0xBF58476D1CE4E5B9UL;value=(value^(value>>27))*0x94D049BB133111EBUL;return value^(value>>31);} }
        private static uint Next(ref ulong state,uint bound)
        { state=Mix(state);return (uint)(state%bound); }
        private static void Shuffle(List<string> values,ref ulong seed)
        { for(int i=values.Count-1;i>0;i--){int j=(int)Next(ref seed,(uint)(i+1));string v=values[i];values[i]=values[j];values[j]=v;} }
        private static string Fingerprint(ArcadeSlayerMartRequest q)
        { string item=q.ItemId??string.Empty;return string.Join("|",((int)q.Action).ToString(CultureInfo.InvariantCulture),item.Length.ToString(CultureInfo.InvariantCulture),item,q.BoxId.ToString(CultureInfo.InvariantCulture),q.ConfirmedAmount.ToString(CultureInfo.InvariantCulture),q.Value?"1":"0"); }
        private static ArcadeSlayerMartResult Fail(string error)=>new ArcadeSlayerMartResult{Error=error};

        /// <summary>恢复或调用边界拒绝不完整、重复身份或越界库存，避免坏状态部分消费。</summary>
        private static bool Valid(ArcadeSlayerMartState state,ArcadeSlayerMartCatalog catalog)
            =>state!=null&&catalog!=null&&state.RulesVersion==1&&!string.IsNullOrEmpty(state.RegionInstanceId)&&state.Revision>0&&state.Generation>0&&
                !float.IsNaN(state.RemainingSeconds)&&!float.IsInfinity(state.RemainingSeconds)&&state.RemainingSeconds>=0&&
                catalog.Regions.Any(p=>p.Visit==state.Visit)&&state.Players!=null&&state.Players.Count>=1&&state.Players.Count<=2&&
                state.Players.All(p=>p!=null&&p.PlayerId>0&&p.Cash>=0&&p.Tokens>=0&&p.RerollSequence>=0&&p.Bombs>=0&&p.Bombs<=catalog.MaxBombs&&
                    p.Dashes>=0&&p.Dashes<=catalog.MaxDashes&&p.Lives>=0&&p.Lives<=catalog.MaxLives&&p.VestSlots>=0&&p.VestSlots<=2&&p.Plates>=0&&p.Plates<=p.VestSlots&&
                    p.Chickens>=0&&p.Chickens<=catalog.MaxChickens&&p.Offers!=null&&p.Offers.Count==catalog.Cards&&p.Offers.Distinct(StringComparer.Ordinal).Count()==catalog.Cards&&
                    p.Offers.All(id=>catalog.Items.Any(item=>item.Id==id))&&p.Owned!=null&&p.Bought!=null)&&state.Players.Select(p=>p.PlayerId).Distinct().Count()==state.Players.Count&&
                state.Receipts!=null&&state.PendingGrants!=null&&state.CollectedBoxes!=null;
    }
}
