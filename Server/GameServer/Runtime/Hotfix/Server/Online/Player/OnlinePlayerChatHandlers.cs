namespace ET.Server
{
    [MessageHandler(SceneType.Player)]
    public class G2P_SendRoomChatHandler: MessageHandler<OnlinePlayerActor, G2P_SendRoomChat, P2G_SendRoomChat>
    {
        protected override async ETTask Run(OnlinePlayerActor actor, G2P_SendRoomChat request, P2G_SendRoomChat response)
        {
            var result = await actor.SendRoomChat(request.SessionEpoch, request.ClientMessageId, request.Content);
            response.Error = result.Error;
            response.Message = result.Message;
            response.RoomId = result.RoomId;
            response.Accepted = result.Accepted;
            response.ChatMessage = result.ChatMessage;
        }
    }

    [MessageHandler(SceneType.Player)]
    public class G2P_GetRoomChatHistoryHandler: MessageHandler<OnlinePlayerActor, G2P_GetRoomChatHistory, P2G_GetRoomChatHistory>
    {
        protected override async ETTask Run(OnlinePlayerActor actor, G2P_GetRoomChatHistory request, P2G_GetRoomChatHistory response)
        {
            var result = await actor.GetRoomChatHistory(request.SessionEpoch);
            response.Error = result.Error;
            response.Message = result.Message;
            response.RoomId = result.RoomId;
            response.MaxChatSequence = result.MaxSequence;
            response.Messages.AddRange(result.Messages);
        }
    }

    [MessageHandler(SceneType.Player)]
    public class C2P_RoomChatMessageHandler: MessageHandler<OnlinePlayerActor, C2P_RoomChatMessage>
    {
        protected override async ETTask Run(OnlinePlayerActor actor, C2P_RoomChatMessage message)
        {
            await actor.ReceiveRoomChatMessage(message);
        }
    }
}
