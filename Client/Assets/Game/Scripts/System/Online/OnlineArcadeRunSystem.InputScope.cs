using System.Linq;
using QFramework;

namespace Game
{
    public sealed partial class OnlineArcadeRunSystem
    {
        /// <summary>只约束本地意图，资源就绪确认与网络轮询不因个人菜单停止。</summary>
        private bool InputContextBlocked(OnlineArcadeBattlePlayerSnapshot player)=>lastSnapshot.BattlePaused||lastSnapshot.Battle.RoundLoading||
            !player.Alive||player.RespawnSeconds>0||UIHelper.HasUIOpen||ArcadeShopUICtrl.IsShownForInput;
        /// <summary>对局、竞技场或地图代次变化即丢弃旧脉冲；同图广播保留尚未确认的输入。</summary>
        private void ResetInputOnBoundary(OnlineArcadeRunClientSnapshot next)
        {
            var old=lastSnapshot.Battle;var battle=next.Battle;
            bool boundary=lastSnapshot.HasRun!=next.HasRun||lastSnapshot.RunId!=next.RunId||
                (old==null)!=(battle==null)||old!=null&&battle!=null&&(old.ArenaNumber!=battle.ArenaNumber||old.RoundGeneration!=battle.RoundGeneration);
            if(!boundary)return;
            pendingInputSequence=0;pendingInput=default;lastSampledInput=default;confirmedExitGeneration=confirmedExitTarget=0;
            input?.Disable(true);inputCancellation?.Cancel();
        }
        /// <summary>场景未就绪也继续发送就绪查询，但不采集可跨加载积压的物理操作。</summary>
        private bool CanSamplePhysicalInput()
        {
            var battle=lastSnapshot.Battle;if(battle==null)return false;
            long self=this.GetModel<OnlineAccountModel>().Snapshot.Value.PlayerId;
            var player=battle.Players.FirstOrDefault(p=>p.PlayerId==self);
            if(InputContextBlocked(player)||combatView?.IsRoundLayoutReady!=true||!combatView.IsIslandRoundReady)return false;
            var shop=battle.SlayerMart;
            return shop?.Active!=true||shop.Ready&&!shop.Closing&&!shop.Members.Any(m=>m.PlayerId==self&&m.MenuOpen);
        }
    }
}
