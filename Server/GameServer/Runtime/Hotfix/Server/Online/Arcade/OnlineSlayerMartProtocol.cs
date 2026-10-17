using FirepowerLoop.Arcade;

namespace ET.Server
{
    /// <summary>三跳商店消息只复制意图，玩家身份由各层已认证宿主提供，不接受客户端价格或效果值。</summary>
    public static class OnlineSlayerMartProtocol
    {
        public static G2P_OperateOnlineSlayerMart ToPlayer(C2G_OperateOnlineSlayerMart source,long playerId,long epoch)
        {
            var target=G2P_OperateOnlineSlayerMart.Create();target.PlayerId=playerId;target.SessionEpoch=epoch;
            target.RegionInstanceId=source.RegionInstanceId;target.Generation=source.Generation;target.ExpectedRevision=source.ExpectedRevision;
            target.OperationId=source.OperationId;target.ActionKind=source.ActionKind;target.ItemId=source.ItemId;target.BoxId=source.BoxId;
            target.ConfirmedAmount=source.ConfirmedAmount;target.Value=source.Value;return target;
        }
        public static P2R_OperateOnlineSlayerMart ToRoom(G2P_OperateOnlineSlayerMart source,long playerId)
        {
            var target=P2R_OperateOnlineSlayerMart.Create();target.PlayerId=playerId;target.RegionInstanceId=source.RegionInstanceId;
            target.Generation=source.Generation;target.ExpectedRevision=source.ExpectedRevision;target.OperationId=source.OperationId;
            target.ActionKind=source.ActionKind;target.ItemId=source.ItemId;target.BoxId=source.BoxId;target.ConfirmedAmount=source.ConfirmedAmount;target.Value=source.Value;return target;
        }
        public static ArcadeSlayerMartRequest ToDomain(P2R_OperateOnlineSlayerMart source,long playerId)
            =>new(){PlayerId=playerId,RegionInstanceId=source.RegionInstanceId,Generation=source.Generation,ExpectedRevision=source.ExpectedRevision,
                OperationId=source.OperationId,Action=(SlayerMartAction)source.ActionKind,ItemId=source.ItemId,BoxId=source.BoxId,ConfirmedAmount=source.ConfirmedAmount,Value=source.Value};
    }
}
