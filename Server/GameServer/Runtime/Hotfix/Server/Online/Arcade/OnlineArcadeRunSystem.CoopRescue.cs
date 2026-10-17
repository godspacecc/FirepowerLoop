using System;
using System.Linq;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>零命恢复者凭显式轮节点身份参与，旧档默认 Alive 不构成免费活动资格。</summary>
        internal static bool CanContinueCoop(OnlineArcadePlayerRecord p)
            => p.Lives>0 || p.CoopRescueEligible && p.RoundAlive && p.RoundHealth>0;

        /// <summary>延迟弹丸等正常积分加命，把零命倒地转回付命短复活，禁止保存矛盾身份。</summary>
        private static void NormalizeCoopLifeGain(OnlineArcadeRunActor actor,OnlineArcadePlayerRecord record)
        {
            if(record.Lives<=0||actor.Battle==null||!actor.Battle.Players.TryGetValue(record.PlayerId,out var player)||
                player.Alive||player.DownedSeconds<=0)return;
            player.DownedSeconds=0;player.RespawnSeconds=ServerConfig.Instance.ArcadeRule.RespawnDelaySeconds;
        }

        /// <summary>仅快照携带非伤害救援圈与倒地点；不加入实际机关或伤害集合。</summary>
        private static void AppendCoopRescueMarkers(OnlineArcadeBattleRuntime source,OnlineArcadeBattleInfo snapshot)
        {
            if(!source.IsCasualRoute||source.IsChallenge||source.SlayerMartReturnWave>0)return;
            int index=0;
            foreach(var player in source.Players.Values.OrderBy(p=>p.PlayerId))
            {
                int id=-20000-index++;
                if(player.Alive||player.DownedSeconds<=0||player.InTreasureRoom)continue;
                var marker=OnlineArcadeContentAreaInfo.Create();marker.Id=id;marker.Kind=12;
                marker.X=player.PositionX;marker.Y=player.PositionZ;marker.Radius=ServerConfig.Instance.ArcadeRule.CoopRescueRadius;
                marker.Warning=true;snapshot.ContentAreas.Add(marker);
            }
        }
        /// <summary>倒地使用独立时钟；不能占用会冻结整场机关的付命短复活字段。</summary>
        private static bool TickCoopDowned(OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime battle,
            OnlineArcadeBattlePlayerRuntime player,float seconds,long now)
        {
            if(player.DownedSeconds<=0 || !battle.IsCasualRoute || battle.IsChallenge || battle.AllPlayersDefeated ||
                battle.RoundTransitionPending || battle.SlayerMartReturnWave>0 || !RoundPlayersReady(battle) ||
                actor.DisconnectedAtByPlayer.Count>0 || player.InTreasureRoom ||
                battle.Players.Values.All(p=>p.InTreasureRoom))return false;
            player.DownedSeconds=Math.Max(0,player.DownedSeconds-seconds*CoopRescueRate(battle,player));
            if(player.DownedSeconds==0)RestoreCoopPlayer(battle,player,now);
            return seconds>0;
        }

        /// <summary>同区域存活队友靠近且当前障碍不遮挡时加速；离开后保留已完成进度。</summary>
        private static float CoopRescueRate(OnlineArcadeBattleRuntime battle,OnlineArcadeBattlePlayerRuntime downed)
        {
            var rule=ServerConfig.Instance.ArcadeRule;
            bool helping=battle.Players.Values.Any(helper=>helper.PlayerId!=downed.PlayerId && helper.Alive &&
                !helper.InTreasureRoom && helper.RespawnSeconds<=0 &&
                DistanceSquared(helper.PositionX,helper.PositionZ,downed.PositionX,downed.PositionZ)<=rule.CoopRescueRadius*rule.CoopRescueRadius &&
                !SegmentCrossesObstacle(helper.PositionX,helper.PositionZ,downed.PositionX,downed.PositionZ,0,EffectiveObstacles(battle)));
            return helping?rule.CoopRescueSpeed:1;
        }

        /// <summary>恢复只补生命及一次复起保护，命数和死亡清理过的支援、队列均不返还。</summary>
        private static void RestoreCoopPlayer(OnlineArcadeBattleRuntime battle,OnlineArcadeBattlePlayerRuntime player,long now,float protection=-1)
        {
            if(protection<0)protection=FirepowerLoop.Arcade.ArcadeSlayerMartAbilityRules.RespawnProtection(
                ServerConfig.Instance.ArcadeRule.ContactInvulnerabilitySeconds,SlayerMartAbility(battle,player.PlayerId),ServerConfig.Instance.SlayerMart.Parameter);
            player.DownedSeconds=0;player.RespawnSeconds=0;player.CurrentHealth=player.MaxHealth;player.Alive=true;
            player.MoveX=player.MoveZ=0;player.Firing=false;player.LastInputAt=now;
            player.InvulnerabilitySeconds=protection;
        }

        /// <summary>Actor 串行裁决捐命；候选与回执先准备，随后扣命和救起一次发布，不另建中途存档。</summary>
        public static bool DonateCoopLife(this OnlineArcadeRunActor actor,long donorId,long runId,int generation,long targetId,long operation)
        {
            var battle=actor.Battle;
            if(battle==null || actor.Record.RunId!=runId || generation!=battle.RoundGeneration || operation<=0 ||
                !battle.Players.TryGetValue(donorId,out var donor))
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid,"捐命请求身份或地图代次已过期");
            if(donor.DonationReceipts.TryGetValue(operation,out var old))
            {
                if(old.TargetPlayerId!=targetId)throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid,"捐命操作不能更换目标");
                return old.Success;
            }
            if(operation<=donor.LastDonationOperation)return false;
            var source=actor.Record.Players.First(p=>p.PlayerId==donorId);
            var targetRecord=actor.Record.Players.FirstOrDefault(p=>p.PlayerId==targetId);
            battle.Players.TryGetValue(targetId,out var target);
            bool eligible=battle.IsCasualRoute && !battle.IsChallenge && !battle.AllPlayersDefeated &&
                !battle.ArenaCompletionReady && !battle.ChallengeCompletionReady && !battle.RoundTransitionPending &&
                battle.SlayerMartReturnWave<=0 && RoundPlayersReady(battle) && actor.DisconnectedAtByPlayer.Count==0 &&
                donor.Alive && !donor.InTreasureRoom && donor.RespawnSeconds<=0 && source.Lives>=ServerConfig.Instance.ArcadeRule.CoopDonationMinLives &&
                targetId!=donorId && targetRecord?.Lives==0 && targetRecord.CoopRescueEligible &&
                target!=null && !target.Alive && !target.InTreasureRoom && target.DownedSeconds>0;
            float protection=eligible?FirepowerLoop.Arcade.ArcadeSlayerMartAbilityRules.RespawnProtection(
                ServerConfig.Instance.ArcadeRule.ContactInvulnerabilitySeconds,SlayerMartAbility(battle,targetId),ServerConfig.Instance.SlayerMart.Parameter):0;
            // 分配回执与计算保护等可能失败的工作必须在扣命之前完成。
            var receipt=new OnlineArcadeDonationReceipt{TargetPlayerId=targetId,Success=eligible};
            donor.DonationReceipts.Add(operation,receipt);donor.LastDonationOperation=operation;
            if(donor.DonationReceipts.Count>32)donor.DonationReceipts.Remove(donor.DonationReceipts.Keys.First());
            if(!eligible)return false;
            source.Lives--;
            RestoreCoopPlayer(battle,target,TimeInfo.Instance.ServerNow(),protection);
            battle.InputsDirty=true;
            return true;
        }
    }
}
