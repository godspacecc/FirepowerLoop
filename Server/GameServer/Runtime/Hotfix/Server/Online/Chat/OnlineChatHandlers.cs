namespace ET.Server
{
    [MessageHandler(SceneType.Chat)]
    [FriendOf(typeof(OnlineChatComponent))]
    public class R2C_SyncRoomChatMembersHandler: MessageHandler<Scene, R2C_SyncRoomChatMembers, C2R_SyncRoomChatMembers>
    {
        protected override async ETTask Run(Scene scene, R2C_SyncRoomChatMembers request, C2R_SyncRoomChatMembers response)
        {
            try
            {
                OnlineChatComponent chat = scene.GetComponent<OnlineChatComponent>();
                if (chat.FailedSyncCount < Options.Instance.OnlineChatFailSyncCount)
                {
                    chat.FailedSyncCount++;
                    response.Error = ErrorCode.ERR_OnlineChatServiceUnavailable;
                    response.Message = string.Empty;
                    Log.Warning($"online chat sync injected failure roomId={request.RoomId} revision={request.MembershipRevision}");
                    await ETTask.CompletedTask;
                    return;
                }

                response.ConfirmedRevision = chat.SyncMembers(request);
                response.Error = ErrorCode.ERR_Success;
            }
            catch (RpcException e)
            {
                response.Error = e.Error;
                response.Message = string.Empty;
                Log.Warning($"online chat sync rejected roomId={request.RoomId} revision={request.MembershipRevision} error={e.Error}");
            }

            await ETTask.CompletedTask;
        }
    }

    [MessageHandler(SceneType.Chat)]
    [FriendOf(typeof(OnlineChatComponent))]
    public class R2C_CloseRoomChatHandler: MessageHandler<Scene, R2C_CloseRoomChat, C2R_CloseRoomChat>
    {
        protected override async ETTask Run(Scene scene, R2C_CloseRoomChat request, C2R_CloseRoomChat response)
        {
            OnlineChatComponent chat = scene.GetComponent<OnlineChatComponent>();
            if (chat.FailedCloseCount < Options.Instance.OnlineChatFailCloseCount)
            {
                chat.FailedCloseCount++;
                response.Error = ErrorCode.ERR_OnlineChatServiceUnavailable;
                response.Message = string.Empty;
                Log.Warning($"online chat close injected failure roomId={request.RoomId} revision={request.MembershipRevision}");
                NLog.LogManager.Flush();
                await ETTask.CompletedTask;
                return;
            }

            bool removed = chat.CloseChannel(request.RoomId);
            response.Error = ErrorCode.ERR_Success;
            response.Message = string.Empty;
            Log.Info($"online chat close confirmed roomId={request.RoomId} removed={removed.ToString().ToLowerInvariant()}");
            await ETTask.CompletedTask;
        }
    }

    [MessageHandler(SceneType.Chat)]
    public class P2C_SendRoomChatHandler: MessageHandler<Scene, P2C_SendRoomChat, C2P_SendRoomChat>
    {
        protected override async ETTask Run(Scene scene, P2C_SendRoomChat request, C2P_SendRoomChat response)
        {
            var result = await scene.GetComponent<OnlineChatComponent>().Send(
                request.RoomId, request.PlayerId, request.ClientMessageId, request.Content);
            response.Error = result.Error;
            response.Message = result.Message;
            response.RoomId = request.RoomId;
            response.Accepted = result.Error == ErrorCode.ERR_Success;
            response.ChatMessage = OnlineChatComponentSystem.ToProtocol(result.ChatMessage);
        }
    }

    [MessageHandler(SceneType.Chat)]
    public class P2C_GetRoomChatHistoryHandler: MessageHandler<Scene, P2C_GetRoomChatHistory, C2P_GetRoomChatHistory>
    {
        protected override async ETTask Run(Scene scene, P2C_GetRoomChatHistory request, C2P_GetRoomChatHistory response)
        {
            var result = scene.GetComponent<OnlineChatComponent>().GetHistory(request.RoomId, request.PlayerId);
            response.Error = result.Error;
            response.Message = result.Message;
            response.RoomId = request.RoomId;
            response.MaxChatSequence = result.MaxSequence;
            foreach (OnlineChatStoredMessage message in result.Messages)
            {
                response.Messages.Add(OnlineChatComponentSystem.ToProtocol(message));
            }

            await ETTask.CompletedTask;
        }
    }
}
