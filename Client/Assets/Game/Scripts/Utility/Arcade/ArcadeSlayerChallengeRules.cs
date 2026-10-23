using System;
using System.Linq;

namespace FirepowerLoop.Arcade
{
    /// <summary>单人与服务端共用的Slayer候选规则，调用方成功发布前不会修改输入状态。</summary>
    public static class ArcadeSlayerChallengeRules
    {
        /// <summary>返回身份接纳当前十三关路线；旧局仍由已保存的目的地返回，不重新计算路线。</summary>
        public static ArcadeSlayerChallengeState Create(ArcadeSlayerChallengeConfiguration config,string runId,long[] members,int returnArena,int returnMap,int generation)
        {
            if(config==null||!config.Enabled||string.IsNullOrWhiteSpace(runId)||runId.Length>128||runId.Contains(":")||members==null||
                members.Length<1||members.Length>2||members.Any(p=>p<=0)||members.Distinct().Count()!=members.Length||
                returnArena<1||returnArena>ArcadeRouteRules.ArenaCount(ArcadeRouteRules.CurrentVersion)||returnMap<1||returnMap>25||generation<=0)throw new ArgumentException("Slayer进入身份无效");
            var state=new ArcadeSlayerChallengeState{RunId=runId,Generation=generation,ReturnArena=returnArena,ReturnMap=returnMap,
                Phase=ArcadeSlayerChallengePhase.Preparing,RemainingSeconds=config.PreparationSeconds,Members=members.OrderBy(p=>p).ToList()};
            foreach(long member in state.Members)state.EntryChoices[member]=0;return state;
        }
        private static void Member(ArcadeSlayerChallengeState state,long id)
        {if(state==null||!state.Members.Contains(id))throw new ArgumentException("Slayer不是原始成员");}
        /// <summary>选择必须全员一致；混合选择保持准备，超时按主线跳过。</summary>
        public static ArcadeSlayerChallengeState SetEntryChoice(ArcadeSlayerChallengeState source,ArcadeSlayerChallengeConfiguration config,long member,bool enter)
        {
            Member(source,member);var next=source.Clone();if(next.Phase!=ArcadeSlayerChallengePhase.Preparing)return next;
            next.EntryChoices[member]=enter?1:2;
            if(next.EntryChoices.Values.All(p=>p==1)){next.Phase=ArcadeSlayerChallengePhase.Collecting;next.RemainingSeconds=config.CollectionSeconds;}
            else if(next.EntryChoices.Values.All(p=>p==2))Return(next,ArcadeSlayerChallengeOutcome.Skipped);return next;
        }
        /// <summary>只推进当前阶段；跨阶段超出的delta不偷耗新阶段，Boss胜利由实际击杀单独裁决。</summary>
        public static ArcadeSlayerChallengeState Advance(ArcadeSlayerChallengeState source,ArcadeSlayerChallengeConfiguration config,double seconds,bool active)
        {
            if(source==null||config==null||double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<0)throw new ArgumentException("Slayer时钟无效");
            var next=source.Clone();if(!active||seconds==0)return next;
            if(next.Phase!=ArcadeSlayerChallengePhase.Preparing&&next.Phase!=ArcadeSlayerChallengePhase.Collecting&&next.Phase!=ArcadeSlayerChallengePhase.Shopping)return next;
            next.RemainingSeconds=Math.Max(0,next.RemainingSeconds-seconds);if(next.RemainingSeconds>0)return next;
            if(next.Phase==ArcadeSlayerChallengePhase.Preparing)Return(next,ArcadeSlayerChallengeOutcome.Skipped);
            else if(next.Phase==ArcadeSlayerChallengePhase.Collecting)next.Phase=ArcadeSlayerChallengePhase.Boss;
            else Return(next,ArcadeSlayerChallengeOutcome.Won);return next;
        }
        public static ArcadeSlayerChallengeState RecordDown(ArcadeSlayerChallengeState source,ArcadeSlayerChallengeConfiguration config)
        {
            var next=source.Clone();if(!Combat(next))return next;
            next.TeamDowns=checked(next.TeamDowns+1);if(next.TeamDowns>=config.DownLimit)Return(next,ArcadeSlayerChallengeOutcome.Failed);return next;
        }
        public static ArcadeSlayerChallengeState SetExitVote(ArcadeSlayerChallengeState source,long member,bool value)
        {
            Member(source,member);var next=source.Clone();if(!Combat(next))return next;
            next.ExitVotes.Remove(member);if(value)next.ExitVotes.Add(member);
            if(next.Members.All(p=>next.ExitVotes.Contains(p)))Return(next,ArcadeSlayerChallengeOutcome.Abandoned);return next;
        }
        public static ArcadeSlayerChallengeState BossDefeated(ArcadeSlayerChallengeState source,ArcadeSlayerChallengeConfiguration config)
        {
            var next=source.Clone();if(next.Phase==ArcadeSlayerChallengePhase.Shopping&&next.Outcome==ArcadeSlayerChallengeOutcome.Won)return next;
            if(next.Phase!=ArcadeSlayerChallengePhase.Boss)throw new InvalidOperationException("Slayer尚未击败实际Boss");
            next.Phase=ArcadeSlayerChallengePhase.Shopping;next.Outcome=ArcadeSlayerChallengeOutcome.Won;next.RemainingSeconds=config.ShopSeconds;next.ExitVotes.Clear();return next;
        }
        public static ArcadeSlayerChallengeState CollectCoin(ArcadeSlayerChallengeState source,ArcadeSlayerChallengeConfiguration config,long coin)
        {
            if(coin<=0)throw new ArgumentException("Slayer金币身份无效");var next=source.Clone();
            if(!Combat(next)||next.CashGranted||next.CashCollected>=config.MaxCollectionCash||next.CollectedCoinIds.Contains(coin))return next;
            next.CashCollected=checked(next.CashCollected+Math.Min(config.CoinAmount,config.MaxCollectionCash-next.CashCollected));next.CollectedCoinIds.Add(coin);return next;
        }
        public static long UnclaimedCash(ArcadeSlayerChallengeState state,ArcadeSlayerChallengeConfiguration config)
            =>state.CashGranted||state.Phase!=ArcadeSlayerChallengePhase.Shopping&&state.Phase!=ArcadeSlayerChallengePhase.Returning?0:
                checked(state.CashCollected+(state.Outcome==ArcadeSlayerChallengeOutcome.Won?config.BossBonusCash:0));
        public static ArcadeSlayerChallengeState ClaimCash(ArcadeSlayerChallengeState source)
        {
            if(source.Phase!=ArcadeSlayerChallengePhase.Shopping&&source.Phase!=ArcadeSlayerChallengePhase.Returning)throw new InvalidOperationException("Slayer现金尚未结算");
            var next=source.Clone();next.CashGranted=true;return next;
        }
        private static bool Combat(ArcadeSlayerChallengeState state)=>state.Phase==ArcadeSlayerChallengePhase.Collecting||state.Phase==ArcadeSlayerChallengePhase.Boss;
        private static void Return(ArcadeSlayerChallengeState state,ArcadeSlayerChallengeOutcome outcome)
        {state.Phase=ArcadeSlayerChallengePhase.Returning;state.Outcome=outcome;state.RemainingSeconds=0;}
    }
}
