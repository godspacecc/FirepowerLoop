namespace ET.Server
{
    [MessageSessionHandler(SceneType.GameGate)]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public sealed class C2G_OperateOnlineSlayerMartHandler:MessageSessionHandler<C2G_OperateOnlineSlayerMart,G2C_OperateOnlineSlayerMart>
    {
        /// <summary>网关只确认当前会话及协议能力，实际报价和扣款交由房间权威。</summary>
        protected override async ETTask Run(Session session,C2G_OperateOnlineSlayerMart request,G2C_OperateOnlineSlayerMart response)
        {
            GatePlayerProxy proxy=session.GetComponent<OnlineSessionComponent>()?.Proxy;
            if(proxy==null||!proxy.IsAuthenticated){response.Error=ErrorCode.ERR_OnlineSessionNotLoggedIn;return;}
            if(!OnlineArcadeProtocolGuard.Matches(request.ProtocolFingerprint))
            {response.Error=ErrorCode.ERR_OnlineRoomVersionMismatch;response.Message="街机协议版本不匹配，请更新客户端";return;}
            proxy.ArcadeProtocolEpoch=proxy.SessionEpoch;
            var inner=OnlineSlayerMartProtocol.ToPlayer(request,proxy.PlayerId,proxy.SessionEpoch);
            using var result=await session.Root().GetComponent<MessageSender>().Call(proxy.PlayerActorId,inner,false) as P2G_OperateOnlineSlayerMart;
            response.Error=result.Error;response.Message=result.Message;response.Accepted=result.Accepted;response.Replayed=result.Replayed;
            response.Run=OnlineArcadeRunActorSystem.CloneSnapshot(result.Run);
        }
    }
}
