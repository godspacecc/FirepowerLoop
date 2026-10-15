namespace ET.Server
{
    [MessageHandler(SceneType.Player)]
    public class G2P_PlayerLogoutHandler: MessageHandler<OnlinePlayerActor, G2P_PlayerLogout, P2G_PlayerLogout>
    {
        protected override async ETTask Run(OnlinePlayerActor actor, G2P_PlayerLogout request, P2G_PlayerLogout response)
        {
            actor.HandleLogout(request.SessionEpoch).Coroutine();
            response.Error = ErrorCode.ERR_Success;
            await ETTask.CompletedTask;
        }
    }
}
