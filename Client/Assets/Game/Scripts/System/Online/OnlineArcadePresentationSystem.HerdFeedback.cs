using System.Collections.Generic;
using UnityEngine;
namespace Game
{
    public sealed partial class OnlineArcadePresentationSystem
    {
        /// <summary>每个地图代次只消费已见玩家递增的接触序列，重连首包只建立基线。</summary>
        private readonly Dictionary<long,long> herdContactSequences=new Dictionary<long,long>();
        private long herdContactRound=long.MinValue,herdContactRun=long.MinValue;
        private void SyncHerdContacts(OnlineArcadeBattleSnapshot battle,bool allowFeedback)
        {
            view?.SetHerdActivityPaused(!allowFeedback);
            bool baseline=herdContactRound!=battle.RoundGeneration||herdContactRun!=latest.RunId;
            if(baseline){herdContactSequences.Clear();herdContactRound=battle.RoundGeneration;herdContactRun=latest.RunId;}
            foreach(var player in battle.Players)
            {
                bool known=herdContactSequences.TryGetValue(player.PlayerId,out long old);
                if(known&&player.HerdContactSequence<old)continue;
                herdContactSequences[player.PlayerId]=player.HerdContactSequence;
                double age=battle.HazardElapsedSeconds-player.HerdContactElapsedSeconds;
                if(!baseline&&known&&allowFeedback&&player.HerdContactSequence>old&&age>=0&&age<=.4)
                    view?.PlayHerdContact(new Vector2(player.HerdContactX,player.HerdContactZ));
            }
        }
        private void ClearHerdContactBaseline()
        {herdContactSequences.Clear();herdContactRound=herdContactRun=long.MinValue;}
    }
}
