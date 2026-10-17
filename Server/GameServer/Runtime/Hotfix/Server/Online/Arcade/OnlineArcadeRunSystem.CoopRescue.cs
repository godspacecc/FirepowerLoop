using System;
using System.Linq;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>零命恢复者凭显式轮节点身份参与，旧档默认 Alive 不构成免费活动资格。</summary>
        internal static bool CanContinueCoop(OnlineArcadePlayerRecord p)
            => p.Lives>0 || p.CoopRescueEligible && p.RoundAlive && p.RoundHealth>0;
        /// <summary>倒地使用独立时钟；不能占用会冻结整场机关的付命短复活字段。</summary>
        private static bool TickCoopDowned(OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime battle,
            OnlineArcadeBattlePlayerRuntime player,float seconds,long now)
        {
            if(player.DownedSeconds<=0 || !battle.IsCasualRoute || battle.IsChallenge || battle.AllPlayersDefeated ||
                battle.RoundTransitionPending || battle.SlayerMartReturnWave>0 || !RoundPlayersReady(battle) ||
                actor.DisconnectedAtByPlayer.Count>0 || player.InTreasureRoom ||
                battle.Players.Values.All(p=>p.InTreasureRoom))return false;
            player.DownedSeconds=Math.Max(0,player.DownedSeconds-seconds);
            if(player.DownedSeconds==0)RestoreCoopPlayer(battle,player,now);
            return seconds>0;
        }

        /// <summary>恢复只补生命及一次复起保护，命数和死亡清理过的支援、队列均不返还。</summary>
        private static void RestoreCoopPlayer(OnlineArcadeBattleRuntime battle,OnlineArcadeBattlePlayerRuntime player,long now)
        {
            player.DownedSeconds=0;player.RespawnSeconds=0;player.CurrentHealth=player.MaxHealth;player.Alive=true;
            player.MoveX=player.MoveZ=0;player.Firing=false;player.LastInputAt=now;
            player.InvulnerabilitySeconds=FirepowerLoop.Arcade.ArcadeSlayerMartAbilityRules.RespawnProtection(
                ServerConfig.Instance.ArcadeRule.ContactInvulnerabilitySeconds,SlayerMartAbility(battle,player.PlayerId),ServerConfig.Instance.SlayerMart.Parameter);
        }
    }
}
