using System.Linq;
using QFramework;

namespace Game
{
    public sealed partial class OnlineArcadeRunSystem
    {
        /// <summary>客户端只检查当前可见资格以过滤意图；扣命与救起始终由服务器裁决。</summary>
        public bool TryGetDonationTarget(out long targetId)
        {
            targetId=0;var battle=lastSnapshot.Battle;
            if(battle==null || lastSnapshot.Phase!=OnlineArcadeRunPhase.Arena || battle.IsChallenge ||
                battle.SlayerMart?.Active==true || this.GetModel<AppStateModel>()?.CurrentState.Value!=AppState.Playing)return false;
            long self=this.GetModel<OnlineAccountModel>().Snapshot.Value.PlayerId;
            var player=battle.Players.FirstOrDefault(p=>p.PlayerId==self);
            if(InputContextBlocked(player)||player.InTreasureRoom)return false;
            if(!this.GetUtility<IRuntimeConfigProvider>().TryGetArcadeCatalog(out var catalog,out _))return false;
            var member=lastSnapshot.Players.FirstOrDefault(p=>p.PlayerId==self);
            if(member.Lives<catalog.Rule.CoopDonationMinLives || catalog.Rule.CoopDonationMinLives<2)return false;
            var target=battle.Players.FirstOrDefault(p=>p.PlayerId!=self&&!p.Alive&&!p.InTreasureRoom&&p.DownedSeconds>0);
            targetId=target.PlayerId;return targetId>0;
        }
    }
}
