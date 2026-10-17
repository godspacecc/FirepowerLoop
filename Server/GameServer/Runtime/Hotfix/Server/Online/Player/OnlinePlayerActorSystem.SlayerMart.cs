namespace ET.Server
{
    public static partial class OnlinePlayerActorSystem
    {
        /// <summary>会话确认后转发同一OperationId，响应只向当前操作者返回其个人快照。</summary>
        public static async ETTask<(int Error,string Message,bool Accepted,bool Replayed,OnlineArcadeRunSnapshot Run)> OperateOnlineSlayerMart(
            this OnlinePlayerActor self,G2P_OperateOnlineSlayerMart intent)
        {
            if(intent.SessionEpoch!=self.SessionEpoch||intent.PlayerId!=self.PlayerId||self.State!=OnlinePlayerState.Online)
                return(ErrorCode.ERR_OnlineSessionNotLoggedIn,"未登录",false,false,null);
            if(self.CurrentArcadeRunId==0)return(ErrorCode.ERR_OnlineArcadeStateInvalid,"当前没有可操作的联机街机局",false,false,null);
            try
            {
                var scene=ServerConfig.Instance.GetSceneByName(self.Zone(),"OnlineRoom").ActorId;
                var request=OnlineSlayerMartProtocol.ToRoom(intent,self.PlayerId);
                using var response=await self.Root().GetComponent<MessageSender>().Call(scene,request,false) as R2P_OperateOnlineSlayerMart;
                return(response.Error,response.Message,response.Accepted,response.Replayed,OnlineArcadeRunActorSystem.CloneSnapshot(response.Run));
            }
            catch(RpcException exception){return(exception.Error,exception.Message,false,false,null);}
        }
    }
    [MessageHandler(SceneType.Player)]
    public sealed class G2P_OperateOnlineSlayerMartHandler:MessageHandler<OnlinePlayerActor,G2P_OperateOnlineSlayerMart,P2G_OperateOnlineSlayerMart>
    {
        protected override async ETTask Run(OnlinePlayerActor actor,G2P_OperateOnlineSlayerMart request,P2G_OperateOnlineSlayerMart response)
        {
            var result=await actor.OperateOnlineSlayerMart(request);response.Error=result.Error;response.Message=result.Message;
            response.Accepted=result.Accepted;response.Replayed=result.Replayed;response.Run=result.Run;
        }
    }
}
