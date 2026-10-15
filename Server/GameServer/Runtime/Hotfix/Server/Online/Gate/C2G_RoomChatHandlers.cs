namespace ET.Server
{
    [MessageSessionHandler(SceneType.GameGate)]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public class C2G_SendRoomChatHandler: MessageSessionHandler<C2G_SendRoomChat, G2C_SendRoomChat>
    {
        protected override async ETTask Run(Session session, C2G_SendRoomChat request, G2C_SendRoomChat response)
        {
            GatePlayerProxy proxy = session.GetComponent<OnlineSessionComponent>()?.Proxy;
            if (proxy == null || !proxy.IsAuthenticated)
            {
                response.Error = ErrorCode.ERR_OnlineSessionNotLoggedIn;
                response.Message = string.Empty;
                return;
            }

            G2P_SendRoomChat inner = G2P_SendRoomChat.Create();
            inner.PlayerId = proxy.PlayerId;
            inner.SessionEpoch = proxy.SessionEpoch;
            inner.ClientMessageId = request.ClientMessageId;
            inner.Content = request.Content;
            using P2G_SendRoomChat result = await session.Root().GetComponent<MessageSender>().Call(proxy.PlayerActorId, inner, false) as P2G_SendRoomChat;
            response.Error = result.Error;
            response.Message = string.Empty;
            response.RoomId = result.RoomId;
            response.Accepted = result.Accepted;
            response.ChatMessage = OnlineChatComponentSystem.CloneProtocol(result.ChatMessage);
            if (result.Error != ErrorCode.ERR_Success)
            {
                Log.Warning($"online room chat send rejected playerId={proxy.PlayerId} error={result.Error}");
            }
        }
    }

    [MessageSessionHandler(SceneType.GameGate)]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public class C2G_GetRoomChatHistoryHandler: MessageSessionHandler<C2G_GetRoomChatHistory, G2C_GetRoomChatHistory>
    {
        protected override async ETTask Run(Session session, C2G_GetRoomChatHistory request, G2C_GetRoomChatHistory response)
        {
            GatePlayerProxy proxy = session.GetComponent<OnlineSessionComponent>()?.Proxy;
            if (proxy == null || !proxy.IsAuthenticated)
            {
                response.Error = ErrorCode.ERR_OnlineSessionNotLoggedIn;
                response.Message = string.Empty;
                return;
            }

            G2P_GetRoomChatHistory inner = G2P_GetRoomChatHistory.Create();
            inner.PlayerId = proxy.PlayerId;
            inner.SessionEpoch = proxy.SessionEpoch;
            using P2G_GetRoomChatHistory result = await session.Root().GetComponent<MessageSender>().Call(proxy.PlayerActorId, inner, false) as P2G_GetRoomChatHistory;
            response.Error = result.Error;
            response.Message = string.Empty;
            response.RoomId = result.RoomId;
            response.MaxChatSequence = result.MaxChatSequence;
            foreach (RoomChatMessageInfo message in result.Messages)
            {
                response.Messages.Add(OnlineChatComponentSystem.CloneProtocol(message));
            }
            if (result.Error != ErrorCode.ERR_Success)
            {
                Log.Warning($"online room chat history rejected playerId={proxy.PlayerId} error={result.Error}");
            }
        }
    }
}
