using System;
using System.Linq;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>退出投票只留当前挑战有界回执；取消可重试，不产生中途存档或重复投票。</summary>
        internal static bool OperateSlayerExit(OnlineArcadeRunActor actor,long playerId,string operationId,long revision,bool value)
        {
            var battle=actor.Battle;var state=battle?.SlayerChallenge;
            if(state==null||string.IsNullOrWhiteSpace(operationId)||operationId.Length>64||!state.Members.Contains(playerId))
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid,"Slayer退出身份无效");
            string key=playerId+":"+operationId,fingerprint=revision+":"+value;
            if(battle.SlayerExitRequests.TryGetValue(key,out string previous))
            {if(previous!=fingerprint)throw new RpcException(ErrorCode.ERR_OnlineArcadeOperationConflict,"Slayer退出参数冲突");return true;}
            if(actor.Record.Revision!=revision||actor.DisconnectedAtByPlayer.Count>0||battle.SlayerExitRequests.Count>=128||
                state.Phase!=ArcadeSlayerChallengePhase.Collecting&&state.Phase!=ArcadeSlayerChallengePhase.Boss)
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid,"Slayer退出阶段或版本无效");
            var next=ArcadeSlayerChallengeRules.SetExitVote(state,playerId,value);
            battle.SlayerExitRequests.Add(key,fingerprint);battle.SlayerChallenge=next;
            if(next.Phase==ArcadeSlayerChallengePhase.Returning){battle.ChallengeCompletionReady=true;battle.ChallengeSucceeded=false;}
            battle.BattleRevision++;return true;
        }
    }
}
