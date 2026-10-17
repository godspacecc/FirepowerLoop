namespace ET.Server
{
    /// <summary>把服务端已落盘的联机街机安全节点推送到对应客户端会话。</summary>
    [MessageHandler(SceneType.GameGate)]
    [FriendOf(typeof(GatePlayerProxy))]
    public sealed class P2G_OnlineArcadeRunChangedHandler : MessageHandler<GatePlayerProxy, P2G_OnlineArcadeRunChanged>
    {
        protected override async ETTask Run(GatePlayerProxy proxy, P2G_OnlineArcadeRunChanged message)
        {
            if (!OnlineArcadeProtocolGuard.CanSendPush(proxy) || proxy.PlayerId != message.PlayerId ||
                proxy.SessionEpoch != message.SessionEpoch || message.HasRun && message.Run == null)
                return;
            Session session = proxy.Session;
            if (session == null || session.IsDisposed) return;
            G2C_OnlineArcadeRunChanged push = G2C_OnlineArcadeRunChanged.Create();
            push.HasRun = message.HasRun;
            push.Run = message.HasRun ? OnlineArcadeRunActorSystem.CloneSnapshot(message.Run) : null;
            session.Send(push);
            await ETTask.CompletedTask;
        }
    }
}
