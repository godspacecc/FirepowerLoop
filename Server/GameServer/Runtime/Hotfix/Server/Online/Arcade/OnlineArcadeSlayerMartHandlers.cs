using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunManagerSystem
    {
        /// <summary>同步发布完整候选后才广播，广播只带公共状态，定向响应保留本人报价。</summary>
        public static async ETTask<(bool Accepted,bool Replayed,string Error,OnlineArcadeRunSnapshot Snapshot)> OperateSlayerMart(
            this OnlineArcadeRunManagerComponent manager,long playerId,ArcadeSlayerMartRequest request)
        {
            var actor=manager.GetActorForPlayer(playerId);
            if(actor==null||actor.IsDisposed||actor.Battle==null)throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid,"当前没有可操作的竞技场现场");
            var result=actor.OperateSlayerMart(playerId,request);var snapshot=actor.BuildSnapshotForPlayer(playerId);
            if(result.Success&&!result.Replayed)await manager.BroadcastRunChanged(actor);
            return(result.Success,result.Replayed,result.Error,snapshot);
        }
    }
    [MessageHandler(SceneType.OnlineRoom)]
    public sealed class P2R_OperateOnlineSlayerMartHandler:MessageHandler<Scene,P2R_OperateOnlineSlayerMart,R2P_OperateOnlineSlayerMart>
    {
        /// <summary>房间消息串行进入唯一Actor交易入口，不在转发层发钱或发能力。</summary>
        protected override async ETTask Run(Scene scene,P2R_OperateOnlineSlayerMart request,R2P_OperateOnlineSlayerMart response)
        {
            try
            {
                var result=await scene.GetComponent<OnlineArcadeRunManagerComponent>().OperateSlayerMart(request.PlayerId,
                    OnlineSlayerMartProtocol.ToDomain(request,request.PlayerId));
                response.Error=result.Accepted?ErrorCode.ERR_Success:ErrorCode.ERR_OnlineArcadeStateInvalid;response.Message=result.Error;
                response.Accepted=result.Accepted;response.Replayed=result.Replayed;response.Run=result.Snapshot;
            }
            catch(RpcException exception){response.Error=exception.Error;response.Message=exception.Message;}
        }
    }
}
