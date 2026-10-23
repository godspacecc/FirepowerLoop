using System;
using System.Linq;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunManagerSystem
    {
        /// <summary>整关结算候选中为每人创建一次独立商店，终局清空不产生无用途商品。</summary>
        private static void OpenScoreShops(OnlineArcadeRunRecord record)
        {
            foreach (var player in record.Players)
                player.ScoreShop = record.Phase == OnlineArcadeRunPhase.Victory || !record.IsCasualRoute || record.ScoreShopRulesVersion != 1 ? null :
                    ArcadeScoreShopRules.Open(ServerConfig.Instance.ScoreShop, record.NextArena,
                        unchecked((int)(record.RunId * 397 ^ player.PlayerId * 31 ^ record.NextArena)));
        }

        /// <summary>只写入待保存候选；消费不改变倍率及已处理生命档位。</summary>
        private static void ApplyScoreShopIntent(OnlineArcadeRunRecord record, OnlineArcadePlayerRecord player,
            int kind, string dropId)
        {
            var shop=player.ScoreShop;
            if(!record.IsCasualRoute || record.ScoreShopRulesVersion != 1 || record.Phase!=OnlineArcadeRunPhase.SafeCheckpoint || shop==null || shop.NextArena!=record.NextArena ||
                !ArcadeScoreShopRules.TryOperate(ServerConfig.Instance.ScoreShop,shop,player.Score,shop.Revision,
                    (ScoreShopAction)(kind-9),dropId,out var next,out long nextScore,out _))
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid,"积分不足或商店商品已失效，请刷新状态重试");
            // 旧档尚无高水位时，必须在第一次消费前按原分数记录已经跨过的档位。
            if(player.ScoreLifeTier<0)
            {
                var rules=ArcadeTreasureRoomParameters.FromConfig(ServerConfig.Instance.Tables.TbArcadeTreasureRoom.DataList.First());
                player.ScoreLifeTier=rules.LifeThreshold>0?player.Score/rules.LifeThreshold:0;
            }
            player.Score=nextScore;player.ScoreShop=next;
            player.Ready=false;player.ChallengeReady=false;
        }

        /// <summary>恢复和保存均拒绝伪造、跨关或提前交付的账本；缺字段旧档保持原样。</summary>
        private static void ValidateScoreShops(OnlineArcadeRunRecord record)
        {
            if(record.ScoreShopRulesVersion is < 0 or > 1 || !record.IsCasualRoute && record.ScoreShopRulesVersion!=0)
                throw new InvalidOperationException("OnlineArcadeStore contains invalid score shop rules version.");
            foreach(var player in record.Players)
            {
                var shop=player.ScoreShop;
                if(player.ScoreLifeTier < -1 || shop!=null &&
                    (record.ScoreShopRulesVersion != 1 || !ArcadeScoreShopRules.IsValid(ServerConfig.Instance.ScoreShop,shop) ||
                    shop.NextArena!=record.NextArena || record.Phase==OnlineArcadeRunPhase.Victory ||
                    shop.Delivered && string.IsNullOrEmpty(record.RoundLayoutId)))
                    throw new InvalidOperationException("OnlineArcadeStore contains invalid score shop checkpoint.");
            }
        }
    }

    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>双方分别完成购买或跳过后才开放普通关、挑战与自动切关。</summary>
        internal static bool HasPendingScoreShops(OnlineArcadeRunRecord record)
            => record.Players.Any(p => p.ScoreShop != null && !p.ScoreShop.Completed);


        /// <summary>仅在下一普通场候选准备完成后，沿普通空投领取入口发放；随轮前快照一起落盘。</summary>
        private static void DeliverScoreShopDrops(OnlineArcadeRunRecord record,OnlineArcadeBattleRuntime battle)
        {
            if(battle.IsChallenge || battle.SlayerChallenge!=null)return;
            // 临时包装不发布候选；领取逻辑需要同一个候选记录与战斗来应用既有同枪/无人机规则。
            var candidateActor=new OnlineArcadeRunActor{Record=record,Battle=battle};
            foreach(var source in record.Players)
            {
                var shop=source.ScoreShop;
                if(shop==null||!shop.Completed||shop.Delivered||shop.PurchasedDropId==null)continue;
                var drop=ServerConfig.Instance.Tables.TbArcadeDrop.GetOrDefault(shop.PurchasedDropId);
                if(shop.NextArena!=record.NextArena || drop==null ||
                    !candidateActor.ApplyDrop(battle.Players[source.PlayerId],drop))
                    throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid,"已购空投暂时无法交付，保留原安全节点");
                shop.Delivered=true;
            }
        }
    }
}
