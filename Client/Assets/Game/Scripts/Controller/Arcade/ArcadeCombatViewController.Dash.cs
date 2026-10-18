using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>当前地图独占冲刺表现；本机使用零号，队友使用正式玩家 ID。</summary>
        private ArcadeDashPresentation dashPresentation;
        public void SetPlayerDash(long playerId,Vector2 position,Vector2 direction,float remaining,bool visible)
        {
            // 资源未就绪不积压动作；下一份有效状态会呈现剩余阶段。
            if(playerId>0&&remotePlayers.TryGetValue(playerId,out var entry))
            {
                visible &= entry.Visible&&entry.View?.Instance!=null;
                if(visible){var displayed=entry.View.Instance.transform.localPosition;position=new Vector2(displayed.x,displayed.z);}
            }
            dashPresentation?.Set(playerId,position,direction,remaining,visible);
        }
        /// <summary>远端角色使用插值位置，特效必须在同一帧跟随显示锚点。</summary>
        private void MoveRemoteDashVisual(RemotePlayerEntry entry)
        {
            if(entry.View?.Instance==null)return;
            var displayed=entry.View.Instance.transform.localPosition;
            dashPresentation?.Move(entry.PlayerId,new Vector2(displayed.x,displayed.z),entry.Visible);
        }
        private void BindDashVisual(Transform template)
        {ClearDashVisuals();dashPresentation=new ArcadeDashPresentation(root.transform,template);}
        private void ClearDashVisuals(){dashPresentation?.Dispose();dashPresentation=null;}
    }
}
