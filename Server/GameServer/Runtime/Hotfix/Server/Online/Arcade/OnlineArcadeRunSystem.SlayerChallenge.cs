using System;
using System.Linq;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>挑战独立地图和准备状态；ReturnMap始终取原主线，不改记录中的目的地。</summary>
        private static void InitializeSlayerChallenge(OnlineArcadeRunRecord record,OnlineArcadeBattleRuntime battle)
        {
            var c=ServerConfig.Instance.SlayerChallenge;
            if(!battle.IsChallenge||battle.ChallengeId!=c.ChallengeId)return;
            if(record.SlayerMartRulesVersion!=1||!record.IsCasualRoute)throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid,"Slayer需要有效规则1休闲局");
            battle.ArenaConfigNumber=c.ArenaConfigNumber;battle.RoundLayout=ResolveRoundLayout(c.ArenaConfigNumber,c.LayoutRound);
            battle.RoundGeneration=checked(Math.Max(0,record.RoundGeneration)+1);
            battle.SlayerChallenge=ArcadeSlayerChallengeRules.Create(c,record.RunId.ToString(System.Globalization.CultureInfo.InvariantCulture),record.Players.Select(p=>p.PlayerId).ToArray(),record.NextArena,record.ArenaConfigNumber,battle.RoundGeneration);
            battle.ChallengeSeconds=c.PreparationSeconds;battle.SpawnQueue.Clear();battle.SpawnQueueIndex=battle.SpawnedInWave=battle.TotalInWave=0;
            int index=0;foreach(var p in battle.Players.Values.OrderBy(p=>p.PlayerId))
            {p.PositionX=battle.RoundLayout.SpawnX+(index++==0?-1:1);p.PositionZ=battle.RoundLayout.SpawnY;}
        }

        /// <summary>收币阶段连续补充有界批次，不走普通清轮奖励和挑战提前清完成功。</summary>
        private static void BeginSlayerCollectionWave(OnlineArcadeBattleRuntime battle)
        {
            var config=ServerConfig.Instance.SlayerChallenge;var challenge=ServerConfig.Instance.Tables.TbArcadeChallenge.Get(config.ChallengeId);
            var group=ServerConfig.Instance.Tables.TbArcadeEnemyGroup.Get(challenge.EnemyGroupId);int totalWeight=group.Members.Sum(p=>p.Count),previous=0;
            battle.SpawnQueue.Clear();battle.SpawnQueueIndex=battle.SpawnedInWave=0;
            foreach(var member in group.Members)
            {
                int count=ArcadeOpeningRules.AllocateMember(config.CollectionWaveTotal,previous,member.Count,totalWeight);
                for(int i=0;i<count;i++)battle.SpawnQueue.Add(member.EnemyId);previous+=member.Count;
            }
            battle.TotalInWave=battle.SpawnQueue.Count;battle.MaxAlive=Math.Min(ServerConfig.Instance.ArcadeRule.MaxAliveEnemies,group.MaxAlive*battle.DifficultyPlayerCount);
            battle.NextSpawnAt=TimeInfo.Instance.ServerNow();battle.WaveClearAwarded=false;
        }

        /// <summary>只在宿主已经确认地图和连接的帧推进；收币到期先清理旧攻击再进入独立Boss身份。</summary>
        private static bool TickSlayerChallenge(OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime battle,float seconds)
        {
            var old=battle.SlayerChallenge;if(old==null)return false;
            if(!RoundPlayersReady(battle)||actor.DisconnectedAtByPlayer.Count>0||battle.RoundTransitionPending)return false;
            var next=ArcadeSlayerChallengeRules.Advance(old,ServerConfig.Instance.SlayerChallenge,seconds,true);battle.SlayerChallenge=next;
            battle.ChallengeSeconds=(float)next.RemainingSeconds;
            if(old.Phase!=next.Phase&&next.Phase==ArcadeSlayerChallengePhase.Boss)
            {
                battle.Enemies.Clear();battle.EnemyIterationIds.Clear();battle.SpawnQueue.Clear();battle.SpawnQueueIndex=battle.TotalInWave=battle.SpawnedInWave=0;
                foreach(long id in battle.Projectiles.Where(p=>p.Value.OwnerPlayerId<=0).Select(p=>p.Key).ToArray())battle.Projectiles.Remove(id);
                battle.DeathZones.Clear();battle.ContentAreas.Clear();
            }
            if(next.Phase==ArcadeSlayerChallengePhase.Returning)
            {battle.ChallengeCompletionReady=true;battle.ChallengeSucceeded=next.Outcome==ArcadeSlayerChallengeOutcome.Won;}
            return seconds>0||old.Phase!=next.Phase;
        }

        /// <summary>前三次独立倒地短复起，第四次只结束挑战，不扣主线命数或判主线团灭。</summary>
        private static void DownSlayerPlayer(OnlineArcadeBattleRuntime battle,OnlineArcadeBattlePlayerRuntime player)
        {
            var c=ServerConfig.Instance.SlayerChallenge;battle.SlayerChallenge=ArcadeSlayerChallengeRules.RecordDown(battle.SlayerChallenge,c);
            bool failed=battle.SlayerChallenge.Phase==ArcadeSlayerChallengePhase.Returning;
            player.RespawnSeconds=failed?0:c.RespawnSeconds;player.DownedSeconds=0;
            if(failed){battle.ChallengeCompletionReady=true;battle.ChallengeSucceeded=false;}
        }
    }
}
