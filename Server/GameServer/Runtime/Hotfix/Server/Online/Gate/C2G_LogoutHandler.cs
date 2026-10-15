namespace ET.Server
{
    [MessageSessionHandler(SceneType.GameGate)]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public class C2G_LogoutHandler: MessageSessionHandler<C2G_Logout, G2C_Logout>
    {
        protected override async ETTask Run(Session session, C2G_Logout request, G2C_Logout response)
        {
            OnlineSessionComponent sessionComponent = session.GetComponent<OnlineSessionComponent>();
            GatePlayerProxy proxy = sessionComponent?.Proxy;
            if (proxy == null || !proxy.IsAuthenticated)
            {
                response.Error = ErrorCode.ERR_OnlineSessionNotLoggedIn;
                response.Message = string.Empty;
                return;
            }

            proxy.SuppressDisconnect = true;
            G2P_PlayerLogout logout = G2P_PlayerLogout.Create();
            logout.PlayerId = proxy.PlayerId;
            logout.SessionEpoch = proxy.SessionEpoch;
            using P2G_PlayerLogout result = await session.Root().GetComponent<MessageSender>().Call(proxy.PlayerActorId, logout, false) as P2G_PlayerLogout;
            response.Error = result.Error;
            response.Message = string.Empty;
            if (result.Error != ErrorCode.ERR_Success)
            {
                Log.Warning($"online logout rejected playerId={proxy.PlayerId} error={result.Error}");
            }
            proxy.IsAuthenticated = false;
            session.RemoveComponent<OnlineSessionComponent>();
            proxy.Dispose();
        }
    }
}
