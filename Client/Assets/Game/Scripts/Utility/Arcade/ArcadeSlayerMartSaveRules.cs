using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace FirepowerLoop.Arcade
{
    /// <summary>商店轮前快照结构校验，不依赖Unity、当前配置或玩家真实存档目录。</summary>
    public static class ArcadeSlayerMartSaveRules
    {
        public static bool TryValidate(ArcadeSlayerMartState state,string runId,IReadOnlyList<long> members,int completedOrdinaryRounds,out string error)
        {
            error="商店安全节点身份或数据无效";
            if(state==null||string.IsNullOrWhiteSpace(runId)||members==null||members.Count<1||members.Count>2||
                members.Any(p=>p<=0)||members.Distinct().Count()!=members.Count||state.RulesVersion!=1||state.Visit<1||state.Visit>2||
                state.RegionInstanceId!=runId+":slayermart:"+state.Visit.ToString(CultureInfo.InvariantCulture)||
                state.Generation<=0||state.Revision<=0||state.GrantSequence<0||!state.Ready||!state.Closing||
                float.IsNaN(state.RemainingSeconds)||float.IsInfinity(state.RemainingSeconds)||state.RemainingSeconds<0||state.RemainingSeconds>300||
                completedOrdinaryRounds<(state.Visit==1?11:31)||state.Players==null||state.Players.Count!=members.Count||
                state.CollectedBoxes==null||state.Receipts==null||state.PendingGrants==null)return false;
            if(state.Players.Any(p=>p==null)||!state.Players.Select(p=>p.PlayerId).OrderBy(p=>p).SequenceEqual(members.OrderBy(p=>p)))return false;
            foreach(var p in state.Players)
                if(p.StormCharge<0||p.StormRemaining<0||double.IsNaN(p.StormRemaining)||double.IsInfinity(p.StormRemaining)||
                    p.StormTickElapsed<0||double.IsNaN(p.StormTickElapsed)||double.IsInfinity(p.StormTickElapsed)||
                    p.Cash<0||p.Tokens<0||p.RerollSequence<0||p.Bombs<0||p.Dashes<0||p.Lives<0||p.VestSlots<0||p.VestSlots>2||p.Plates<0||p.Plates>p.VestSlots||p.Chickens<0||
                    p.Choice<0||p.Choice>2||p.Offers==null||p.Offers.Count!=16||p.Offers.Distinct(StringComparer.Ordinal).Count()!=16||p.Offers.Any(id=>!ItemId(id))||
                    p.Owned==null||p.Bought==null||p.Owned.Any(id=>!ItemId(id))||p.Bought.Any(id=>!ItemId(id)))return false;
            int boxes=state.Visit==1?9:11;
            if(state.CollectedBoxes.Any(id=>id<1||id>boxes))return false;
            var operations=new HashSet<string>(StringComparer.Ordinal);
            foreach(var r in state.Receipts)
            {
                if(r==null||!members.Contains(r.PlayerId)||string.IsNullOrWhiteSpace(r.OperationId)||r.OperationId.Length>128||
                    string.IsNullOrWhiteSpace(r.Fingerprint)||!operations.Add(r.PlayerId.ToString(CultureInfo.InvariantCulture)+":"+r.OperationId)||r.Grants==null||
                    r.Grants.Any(g=>!Grant(g,state,members))||r.Grants.Select(g=>g.EventId).Distinct(StringComparer.Ordinal).Count()!=r.Grants.Count)return false;
            }
            if(state.PendingGrants.Any(g=>!Grant(g,state,members))||state.PendingGrants.Select(g=>g.EventId).Distinct(StringComparer.Ordinal).Count()!=state.PendingGrants.Count)return false;
            error=null;return true;
        }

        private static bool ItemId(string id)
        {
            const string prefix="slayermart.item.";
            return id!=null&&id.StartsWith(prefix,StringComparison.Ordinal)&&
                int.TryParse(id.Substring(prefix.Length),NumberStyles.None,CultureInfo.InvariantCulture,out int n)&&n>=1&&n<=24&&id==prefix+n.ToString("00",CultureInfo.InvariantCulture);
        }
        private static bool Grant(ArcadeSlayerMartGrant grant,ArcadeSlayerMartState state,IReadOnlyList<long> members)
        {
            string prefix=state.RegionInstanceId+":";
            return grant!=null&&members.Contains(grant.PlayerId)&&Enum.IsDefined(typeof(SlayerMartEffect),grant.Effect)&&grant.Amount>0&&
                !float.IsNaN(grant.Duration)&&!float.IsInfinity(grant.Duration)&&grant.Duration>=0&&grant.EventId!=null&&grant.EventId.StartsWith(prefix,StringComparison.Ordinal)&&
                long.TryParse(grant.EventId.Substring(prefix.Length),NumberStyles.None,CultureInfo.InvariantCulture,out long n)&&n>0&&n<=state.GrantSequence;
        }
    }
}
