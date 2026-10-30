namespace ET.Server
{
    [MessageSessionHandler(SceneType.GameGate)]
    [FriendOf(typeof(GatePlayerProxy))]
    public class C2G_LoginHandler: MessageSessionHandler<C2G_Login, G2C_Login>
    {
        protected override async ETTask Run(Session session, C2G_Login request, G2C_Login response)
        {
            if (!OnlineAccountAdmission.Allow(session)) { response.Error = 200201; return; }
            long instanceId = session.InstanceId;
            using var accountLock = await session.Root().GetComponent<CoroutineLockComponent>().Wait(CoroutineLockType.OnlineAccount, instanceId);
            if (session.IsDisposed || session.InstanceId != instanceId) return;
            if (session.GetComponent<OnlineSessionComponent>() != null)
            {
                response.Error = ErrorCode.ERR_OnlineSessionStateConflict;
                response.Message = string.Empty;
                Log.Warning($"online login rejected error={response.Error}");
                return;
            }

            Scene root = session.Root();
            ActorId accountActorId = root.GetOnlineSceneActorId("Account");
            G2A_Login login = G2A_Login.Create();
            login.UserName = request.UserName;
            login.Password = request.Password;
            using A2G_Login account = await root.GetComponent<MessageSender>().Call(accountActorId, login, false) as A2G_Login;
            request.Password = login.Password = string.Empty;
            if (session.IsDisposed || session.InstanceId != instanceId) return;
            if (account.Error != ErrorCode.ERR_Success)
            {
                response.Error = account.Error;
                response.Message = string.Empty;
                Log.Warning($"online login account rejected error={account.Error}");
                return;
            }

            ActorId playerSceneActorId = root.GetOnlineSceneActorId("Player");
            GatePlayerProxy proxy = root.AddChild<GatePlayerProxy>();
            bool bound = false;
            try
            {
                proxy.Init(account.AccountId, account.PlayerId, default, 0, session);
                proxy.AddComponent<MailBoxComponent, MailBoxType>(MailBoxType.OrderedMessage);

                G2P_GetOrCreatePlayer getOrCreate = G2P_GetOrCreatePlayer.Create();
                getOrCreate.AccountId = account.AccountId;
                getOrCreate.PlayerId = account.PlayerId;
                getOrCreate.DisplayName = account.DisplayName;
                getOrCreate.GateProxyActorId = proxy.GetActorId();
                getOrCreate.ProfileRevision = account.Revision;
                getOrCreate.SessionEpoch = account.SessionEpoch;

                using P2G_GetOrCreatePlayer player = await root.GetComponent<MessageSender>().Call(playerSceneActorId, getOrCreate, false) as P2G_GetOrCreatePlayer;
                if (proxy.IsDisposed) return;
                if (player.Error != ErrorCode.ERR_Success)
                {
                    proxy.SuppressDisconnect = true;
                    proxy.Dispose();
                    response.Error = player.Error;
                    response.Message = string.Empty;
                    Log.Warning($"online login player rejected playerId={account.PlayerId} error={player.Error}");
                    return;
                }

                proxy.PlayerActorId = player.PlayerActorId;
                proxy.SessionEpoch = player.SessionEpoch;
                proxy.IsAuthenticated = true;
                if (session.IsDisposed || session.InstanceId != instanceId || proxy.IsDisposed || proxy.SuppressDisconnect)
                    return;
                session.RemoveComponent<SessionAcceptTimeoutComponent>();
                session.AddComponent<OnlineSessionComponent, GatePlayerProxy>(proxy);
                bound = true;

                response.AccountId = account.AccountId;
                response.PlayerId = account.PlayerId;
                response.SessionEpoch = player.SessionEpoch;
                response.IsPlayerActorReused = player.IsPlayerActorReused;
                response.IsNewAccount = account.IsNewAccount;

                string successLog = $"player login success playerId={account.PlayerId} sessionEpoch={player.SessionEpoch} actorReused={player.IsPlayerActorReused} isNewAccount={account.IsNewAccount}";
                Log.Info(successLog);
            }
            finally
            {
                if (!bound && !proxy.IsDisposed) { proxy.MarkDisconnected(); proxy.Dispose(); }
            }
        }
    }
}
