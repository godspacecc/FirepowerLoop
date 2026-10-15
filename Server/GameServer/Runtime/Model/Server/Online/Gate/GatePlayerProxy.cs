namespace ET.Server
{
    [ChildOf(typeof(Scene))]
    public class GatePlayerProxy: Entity, IAwake, IDestroy
    {
        public long AccountId;
        public long PlayerId;
        public ActorId PlayerActorId;
        public long SessionEpoch;
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
