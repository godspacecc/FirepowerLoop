namespace ET.Server
{
    [MessageHandler(SceneType.GameGate)]
    [FriendOf(typeof(GatePlayerProxy))]
    public class P2G_RoomChatMessageHandler: MessageHandler<GatePlayerProxy, P2G_RoomChatMessage>
    {
        protected override async ETTask Run(GatePlayerProxy proxy, P2G_RoomChatMessage message)
        {
            if (!proxy.IsAuthenticated || proxy.PlayerId != message.PlayerId || proxy.SessionEpoch != message.SessionEpoch)
            {
                return;
            }

            Session session = proxy.Session;
            if (session == null || session.IsDisposed)
            {
                return;
            }

            G2C_RoomChatMessage push = G2C_RoomChatMessage.Create();
            push.RoomId = message.RoomId;
            push.ChatMessage = OnlineChatComponentSystem.CloneProtocol(message.ChatMessage);
            session.Send(push);
            await ETTask.CompletedTask;
        }
    }
}
