namespace ET.Server
{
    [MessageHandler(SceneType.Player)]
    public class G2P_PlayerDisconnectedHandler: MessageHandler<OnlinePlayerActor, G2P_PlayerDisconnected, P2G_PlayerDisconnected>
    {
        protected override async ETTask Run(OnlinePlayerActor actor, G2P_PlayerDisconnected request, P2G_PlayerDisconnected response)
        {
            await actor.HandleDisconnected(request.SessionEpoch);
            response.Error = ErrorCode.ERR_Success;
        }
    }
}
