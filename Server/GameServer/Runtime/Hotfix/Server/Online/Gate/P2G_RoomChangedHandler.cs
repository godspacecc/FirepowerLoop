namespace ET.Server
{
    [MessageHandler(SceneType.GameGate)]
    [FriendOf(typeof(GatePlayerProxy))]
    public class P2G_RoomChangedHandler: MessageHandler<GatePlayerProxy, P2G_RoomChanged>
    {
        protected override async ETTask Run(GatePlayerProxy proxy, P2G_RoomChanged message)
        {
            if (!proxy.IsAuthenticated ||
                proxy.PlayerId != message.PlayerId ||
                proxy.SessionEpoch != message.SessionEpoch)
            {
                return;
            }

            Session session = proxy.Session;
            if (session == null || session.IsDisposed)
            {
                return;
            }

            G2C_RoomChanged push = G2C_RoomChanged.Create();
            push.HasRoom = message.HasRoom;
            push.Room = OnlineRoomSnapshotHelper.CloneSnapshot(message.Room);
            session.Send(push);
            await ETTask.CompletedTask;
        }
    }
}
