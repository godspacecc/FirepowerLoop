using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FirepowerLoop.Arcade
{
    /// <summary>宝石及下轮加命的同源候选交付；成功前不修改现金、积分或已消费事件。</summary>
    public static class ArcadeSlayerMartBenefitRules
    {
        /// <summary>按每颗正式Gem顺序计分，下一普通轮才消费加命；全部成功后返回完整候选。</summary>
        public static bool TryDeliver(ArcadeSlayerMartState state,ArcadeSlayerMartConfiguration config,ArcadeTreasureRoomParameters treasure,
            IReadOnlyDictionary<long,ArcadeTreasureScoreState> scores,bool nextOrdinaryRound,out ArcadeSlayerMartState candidate,
            out Dictionary<long,ArcadeTreasureScoreState> candidateScores,out string error)
        {
            candidate=null;candidateScores=null;error="benefit_delivery_invalid";
            if(state?.Players==null||state.PendingGrants==null||config==null||treasure==null||scores==null||
                nextOrdinaryRound&&(!state.Ready||!state.Closing)||state.Players.Any(p=>p==null||p.AppliedBenefitGrants==null||!scores.ContainsKey(p.PlayerId))||
                state.Players.Select(p=>p.PlayerId).Distinct().Count()!=state.Players.Count||
                state.PendingGrants.Any(p=>p==null)||state.PendingGrants.Select(p=>p.EventId).Distinct(StringComparer.Ordinal).Count()!=state.PendingGrants.Count)return false;
            var next=state.Clone();var result=scores.ToDictionary(p=>p.Key,p=>p.Value);
            var gem=treasure.Rewards.FirstOrDefault(p=>p.Kind==ArcadeTreasureRewardKind.Gem);
            foreach(var grant in next.PendingGrants)
            {
                var owner=next.Players.SingleOrDefault(p=>p.PlayerId==grant.PlayerId);
                string prefix=next.RegionInstanceId+":";
                if(owner==null||grant.EventId==null||!grant.EventId.StartsWith(prefix,StringComparison.Ordinal)||
                    !long.TryParse(grant.EventId.Substring(prefix.Length),NumberStyles.None,CultureInfo.InvariantCulture,out long sequence)||sequence<=0||sequence>next.GrantSequence)return false;
                if(grant.Effect!=SlayerMartEffect.Gems&&grant.Effect!=SlayerMartEffect.NextRoundLife)continue;
                if(grant.Duration!=0||grant.Effect==SlayerMartEffect.Gems&&(grant.Amount<config.Catalog.GemMinimum||grant.Amount>config.Catalog.GemMaximum)||
                    grant.Effect==SlayerMartEffect.NextRoundLife&&grant.Amount!=1)return false;
                if(owner.AppliedBenefitGrants.Contains(grant.EventId)||grant.Effect==SlayerMartEffect.NextRoundLife&&!nextOrdinaryRound)continue;
                var score=result[owner.PlayerId];
                if(grant.Effect==SlayerMartEffect.Gems)
                {
                    if(gem.Kind!=ArcadeTreasureRewardKind.Gem||gem.Points<=0||gem.Progress<=0)return false;
                    for(int index=0;index<grant.Amount;index++)
                    {if(!ArcadeTreasureScoreRules.TryAward(score,gem,treasure,out var awarded))return false;score=awarded;}
                }
                else
                {
                    if(score.Lives<0||score.Lives>config.Catalog.MaxLives)return false;
                    score=new ArcadeTreasureScoreState(score.Score,score.Multiplier,score.Progress,Math.Min(config.Catalog.MaxLives,score.Lives+1),score.LifeTier);
                }
                result[owner.PlayerId]=score;owner.Lives=score.Lives;owner.AppliedBenefitGrants.Add(grant.EventId);
            }
            candidate=next;candidateScores=result;error=null;return true;
        }
    }
}
