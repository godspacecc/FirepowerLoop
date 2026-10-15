namespace ET.Server
{
    [MessageSessionHandler(SceneType.GameGate)]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public sealed class C2G_StartOnlineArcadeRunHandler : MessageSessionHandler<C2G_StartOnlineArcadeRun, G2C_StartOnlineArcadeRun>
    {
        protected override async ETTask Run(Session session, C2G_StartOnlineArcadeRun request, G2C_StartOnlineArcadeRun response)
        {
            GatePlayerProxy proxy = session.GetComponent<OnlineSessionComponent>()?.Proxy;
            if (proxy == null || !proxy.IsAuthenticated)
            {
                response.Error = ErrorCode.ERR_OnlineSessionNotLoggedIn;
                return;
            }
            G2P_StartOnlineArcadeRun inner = G2P_StartOnlineArcadeRun.Create();
            inner.PlayerId = proxy.PlayerId;
            inner.SessionEpoch = proxy.SessionEpoch;
            inner.OperationId = request.OperationId ?? string.Empty;
            using P2G_StartOnlineArcadeRun result = await session.Root().GetComponent<MessageSender>().Call(proxy.PlayerActorId, inner, false) as P2G_StartOnlineArcadeRun;
            response.Error = result.Error;
            response.Message = string.Empty;
            response.HasRun = result.HasRun;
            response.Run = OnlineArcadeRunActorSystem.CloneSnapshot(result.Run);
        }
    }

    [MessageSessionHandler(SceneType.GameGate)]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public sealed class C2G_GetCurrentOnlineArcadeRunHandler : MessageSessionHandler<C2G_GetCurrentOnlineArcadeRun, G2C_GetCurrentOnlineArcadeRun>
    {
        protected override async ETTask Run(Session session, C2G_GetCurrentOnlineArcadeRun request, G2C_GetCurrentOnlineArcadeRun response)
        {
            GatePlayerProxy proxy = session.GetComponent<OnlineSessionComponent>()?.Proxy;
            if (proxy == null || !proxy.IsAuthenticated)
            {
                response.Error = ErrorCode.ERR_OnlineSessionNotLoggedIn;
                return;
            }
            G2P_GetCurrentOnlineArcadeRun inner = G2P_GetCurrentOnlineArcadeRun.Create();
            inner.PlayerId = proxy.PlayerId;
            inner.SessionEpoch = proxy.SessionEpoch;
            using P2G_GetCurrentOnlineArcadeRun result = await session.Root().GetComponent<MessageSender>().Call(proxy.PlayerActorId, inner, false) as P2G_GetCurrentOnlineArcadeRun;
            response.Error = result.Error;
            response.Message = string.Empty;
            response.HasRun = result.HasRun;
            response.Run = OnlineArcadeRunActorSystem.CloneSnapshot(result.Run);
        }
    }

    [MessageSessionHandler(SceneType.GameGate)]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public sealed class C2G_OperateOnlineArcadeSafeShopHandler : MessageSessionHandler<C2G_OperateOnlineArcadeSafeShop, G2C_OperateOnlineArcadeSafeShop>
    {
        protected override async ETTask Run(Session session, C2G_OperateOnlineArcadeSafeShop request,
            G2C_OperateOnlineArcadeSafeShop response)
        {
            GatePlayerProxy proxy = session.GetComponent<OnlineSessionComponent>()?.Proxy;
            if (proxy == null || !proxy.IsAuthenticated)
            {
                response.Error = ErrorCode.ERR_OnlineSessionNotLoggedIn;
                return;
            }
            G2P_OperateOnlineArcadeSafeShop inner = G2P_OperateOnlineArcadeSafeShop.Create();
            inner.PlayerId = proxy.PlayerId;
            inner.SessionEpoch = proxy.SessionEpoch;
            inner.OperationId = request.OperationId ?? string.Empty;
            inner.ExpectedRevision = request.ExpectedRevision;
            inner.ActionKind = request.ActionKind;
            inner.OfferId = request.OfferId ?? string.Empty;
            inner.GoldAmount = request.GoldAmount;
            inner.Ready = request.Ready;
            using P2G_OperateOnlineArcadeSafeShop result = await session.Root().GetComponent<MessageSender>()
                .Call(proxy.PlayerActorId, inner, false) as P2G_OperateOnlineArcadeSafeShop;
            response.Error = result.Error;
            response.Message = result.Message;
            response.Accepted = result.Accepted;
            response.Run = OnlineArcadeRunActorSystem.CloneSnapshot(result.Run);
        }
    }

    [MessageSessionHandler(SceneType.GameGate)]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public sealed class C2G_SubmitOnlineArcadeInputHandler : MessageSessionHandler<C2G_SubmitOnlineArcadeInput, G2C_SubmitOnlineArcadeInput>
    {
        protected override async ETTask Run(Session session, C2G_SubmitOnlineArcadeInput request,
            G2C_SubmitOnlineArcadeInput response)
        {
            GatePlayerProxy proxy = session.GetComponent<OnlineSessionComponent>()?.Proxy;
            if (proxy == null || !proxy.IsAuthenticated)
            {
                response.Error = ErrorCode.ERR_OnlineSessionNotLoggedIn;
                return;
            }
            G2P_SubmitOnlineArcadeInput inner = G2P_SubmitOnlineArcadeInput.Create();
            inner.PlayerId = proxy.PlayerId;
            inner.SessionEpoch = proxy.SessionEpoch;
            inner.ArenaNumber = request.ArenaNumber;
            inner.InputSequence = request.InputSequence;
            inner.MoveX = request.MoveX;
            inner.MoveZ = request.MoveZ;
            inner.AimX = request.AimX;
            inner.AimZ = request.AimZ;
            inner.Firing = request.Firing;
            inner.UseBomb = request.UseBomb;
            inner.UseDash = request.UseDash;
            using P2G_SubmitOnlineArcadeInput result = await session.Root().GetComponent<MessageSender>()
                .Call(proxy.PlayerActorId, inner, false) as P2G_SubmitOnlineArcadeInput;
            response.Error = result.Error;
            response.Message = result.Message;
            response.Accepted = result.Accepted;
            response.LastAcceptedInputSequence = result.LastAcceptedInputSequence;
            response.Run = OnlineArcadeRunActorSystem.CloneSnapshot(result.Run);
        }
    }
}
