namespace ET.Server
{
    [ChildOf(typeof(Scene))]
    public class GatePlayerProxy: Entity, IAwake, IDestroy
    {
        public long AccountId;
        public long PlayerId;
        public ActorId PlayerActorId;
        public long SessionEpoch;
        /// <summary>当前登录代次已确认新版街机协议，防止旧客户端收到无法解析的新战斗广播。</summary>
        public long ArcadeProtocolEpoch;
        public EntityRef<Session> Session;
        public bool IsAuthenticated;
        public bool SuppressDisconnect;
    }

    [ComponentOf(typeof(Session))]
    public class OnlineSessionComponent: Entity, IAwake<GatePlayerProxy>, IDestroy
    {
        public EntityRef<GatePlayerProxy> Proxy;
    }
}
