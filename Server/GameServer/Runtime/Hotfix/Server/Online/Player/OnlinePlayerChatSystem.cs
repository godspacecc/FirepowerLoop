namespace ET.Server
{
    [FriendOf(typeof(OnlinePlayerActor))]
    public static class OnlinePlayerChatSystem
    {
        public static async ETTask<(int Error, string Message, long RoomId, bool Accepted, RoomChatMessageInfo ChatMessage)> SendRoomChat(
            this OnlinePlayerActor self,
            long epoch,
            string clientMessageId,
            string content)
        {
            if (epoch != self.SessionEpoch || self.State != OnlinePlayerState.Online || self.GateProxyActorId == default)
            {
                return (ErrorCode.ERR_OnlineSessionNotLoggedIn, "未登录", 0, false, null);
            }

            if (self.CurrentRoomId == 0)
            {
                return (ErrorCode.ERR_OnlineNotInRoom, "当前不在房间中", 0, false, null);
            }

            ActorId chat = ServerConfig.Instance.GetSceneByName(self.Zone(), "Chat").ActorId;
            P2C_SendRoomChat request = P2C_SendRoomChat.Create();
            request.RoomId = self.CurrentRoomId;
            request.PlayerId = self.PlayerId;
            request.ClientMessageId = clientMessageId;
            request.Content = content;
            using C2P_SendRoomChat response = await self.Root().GetComponent<MessageSender>().Call(chat, request, false) as C2P_SendRoomChat;
            return (response.Error, response.Message, response.RoomId, response.Accepted,
                OnlineChatComponentSystem.CloneProtocol(response.ChatMessage));
        }

        public static async ETTask<(int Error, string Message, long RoomId, long MaxSequence, RoomChatMessageInfo[] Messages)> GetRoomChatHistory(
            this OnlinePlayerActor self,
            long epoch)
        {
            if (epoch != self.SessionEpoch || self.State != OnlinePlayerState.Online || self.GateProxyActorId == default)
            {
                return (ErrorCode.ERR_OnlineSessionNotLoggedIn, "未登录", 0, 0, System.Array.Empty<RoomChatMessageInfo>());
            }

            if (self.CurrentRoomId == 0)
            {
                return (ErrorCode.ERR_OnlineNotInRoom, "当前不在房间中", 0, 0, System.Array.Empty<RoomChatMessageInfo>());
            }

            ActorId chat = ServerConfig.Instance.GetSceneByName(self.Zone(), "Chat").ActorId;
            P2C_GetRoomChatHistory request = P2C_GetRoomChatHistory.Create();
            request.RoomId = self.CurrentRoomId;
            request.PlayerId = self.PlayerId;
            using C2P_GetRoomChatHistory response = await self.Root().GetComponent<MessageSender>().Call(chat, request, false) as C2P_GetRoomChatHistory;
            RoomChatMessageInfo[] messages = new RoomChatMessageInfo[response.Messages.Count];
            for (int index = 0; index < response.Messages.Count; index++)
            {
                messages[index] = OnlineChatComponentSystem.CloneProtocol(response.Messages[index]);
            }
            return (response.Error, response.Message, response.RoomId, response.MaxChatSequence, messages);
        }

        public static async ETTask ReceiveRoomChatMessage(this OnlinePlayerActor self, C2P_RoomChatMessage message)
        {
            if (message.RoomId != self.CurrentRoomId || self.State != OnlinePlayerState.Online || self.GateProxyActorId == default)
            {
                return;
            }

            P2G_RoomChatMessage push = P2G_RoomChatMessage.Create();
            push.PlayerId = self.PlayerId;
            push.SessionEpoch = self.SessionEpoch;
            push.RoomId = message.RoomId;
            push.ChatMessage = OnlineChatComponentSystem.CloneProtocol(message.ChatMessage);
            self.Root().GetComponent<MessageSender>().Send(self.GateProxyActorId, push);
            await ETTask.CompletedTask;
        }
    }
}
