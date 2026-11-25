namespace ET.Server
{
    [EntitySystemOf(typeof(GatePlayerProxy))]
    [FriendOf(typeof(GatePlayerProxy))]
    [FriendOf(typeof(OnlineSessionComponent))]
    public static partial class GatePlayerProxySystem
    {
        [EntitySystem]
        private static void Awake(this GatePlayerProxy self)
        {
        }

        public static void Init(this GatePlayerProxy self, long accountId, long playerId, ActorId playerActorId, long sessionEpoch, Session session)
        {
            self.AccountId = accountId;
            self.PlayerId = playerId;
            self.PlayerActorId = playerActorId;
            self.SessionEpoch = sessionEpoch;
            self.Session = session;
            self.IsAuthenticated = true;
        }

        [EntitySystem]
        private static void Destroy(this GatePlayerProxy self)
        {
        }

        public static void MarkDisconnected(this GatePlayerProxy self)
        {
            if (!self.IsAuthenticated || self.SuppressDisconnect)
            {
                return;
            }

            self.IsAuthenticated = false;
            if (self.PlayerActorId != default)
            {
                G2P_PlayerDisconnected disconnected = G2P_PlayerDisconnected.Create();
                disconnected.PlayerId = self.PlayerId;
                disconnected.SessionEpoch = self.SessionEpoch;
                self.Root().GetComponent<MessageSender>().Call(self.PlayerActorId, disconnected, false).Coroutine();
            }
        }

        public static void ReplaceAndClose(this GatePlayerProxy self, int error, string reason)
        {
            self.IsAuthenticated = false;
            self.SuppressDisconnect = true;
            Session session = self.Session;
            if (session != null && !session.IsDisposed)
            {
                G2C_SessionReplaced replaced = G2C_SessionReplaced.Create();
                replaced.Error = error;
                replaced.Reason = reason;
                session.Send(replaced);
                CloseReplacedSessionLater(self, session).Coroutine();
            }
        }

        private static async ETTask CloseReplacedSessionLater(GatePlayerProxy proxy, Session session)
        {
            EntityRef<GatePlayerProxy> proxyRef = proxy;
            EntityRef<Session> sessionRef = session;
            long proxyId = proxy.Id;
            long sessionId = session.Id;

            await proxy.Root().GetComponent<TimerComponent>().WaitAsync(1000);

            GatePlayerProxy currentProxy = proxyRef;
            Session currentSession = sessionRef;
            if (currentSession == null || currentSession.IsDisposed)
            {
                return;
            }

            if (currentProxy == null || currentProxy.IsDisposed || currentProxy.Id != proxyId)
            {
                currentSession.Dispose();
                return;
            }

            Session proxySession = currentProxy.Session;
            if (proxySession != currentSession || currentSession.Id != sessionId)
            {
                return;
            }

            OnlineSessionComponent sessionComponent = currentSession.GetComponent<OnlineSessionComponent>();
            GatePlayerProxy componentProxy = sessionComponent?.Proxy;
            if (sessionComponent != null && componentProxy != currentProxy)
            {
                return;
            }

            currentProxy.IsAuthenticated = false;
            currentProxy.SuppressDisconnect = true;
            currentSession.Dispose();
            currentProxy.Dispose();
        }
    }

    [EntitySystemOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public static partial class OnlineSessionComponentSystem
    {
        [EntitySystem]
        private static void Awake(this OnlineSessionComponent self, GatePlayerProxy proxy)
        {
            self.Proxy = proxy;
        }

        [EntitySystem]
        private static void Destroy(this OnlineSessionComponent self)
        {
            GatePlayerProxy proxy = self.Proxy;
            if (proxy == null || proxy.IsDisposed)
            {
                return;
            }
            proxy.MarkDisconnected();
            proxy.Dispose();
        }
    }
}
