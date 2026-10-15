using System;
using System.Linq;

namespace ET.Server
{
    [FriendOf(typeof(OnlineTestComponent))]
    public static partial class OnlineTestComponentSystem
    {
        private static async ETTask<int> RunChat(this OnlineTestComponent self)
        {
            int failed = 0;
            failed += await self.Case("CHAT-01 SameRoomMembersReceiveOneOrderedMessageAndHistory", async () =>
            {
                using Session owner = self.CreateSession();
                using G2C_Login ownerLogin = await self.RegisterAndLogin(owner, UniqueUser("cho"));
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(2001))) as G2C_CreateRoom;
                Ensure(create.Error == ErrorCode.ERR_Success, $"create error={create.Error}");

                using Session joiner = self.CreateSession();
                using G2C_Login joinerLogin = await self.RegisterAndLogin(joiner, UniqueUser("chj"));
                using G2C_JoinRoom join = await joiner.Call(JoinRoom(Op(2002), create.Room.RoomCode)) as G2C_JoinRoom;
                Ensure(join.Error == ErrorCode.ERR_Success, $"join error={join.Error}");

                int before = self.RoomChatMessages.Count;
                using G2C_SendRoomChat sent = await owner.Call(SendChat(Op(2003), "hello room")) as G2C_SendRoomChat;
                Ensure(sent.Error == ErrorCode.ERR_Success && sent.Accepted && sent.ChatMessage.ChatSequence == 1, $"send error={sent.Error}");
                await self.WaitForChatPushCount(before + 2, 2000);
                var pushes = self.RoomChatMessages.Skip(before).Take(2).ToArray();
                Ensure(pushes.All(message => message.ChatMessageId == sent.ChatMessage.ChatMessageId && message.ChatSequence == 1), "members received different message identity");
                Ensure(self.RoomChatSessionIds.Skip(before).Take(2).Contains(owner.Id) && self.RoomChatSessionIds.Skip(before).Take(2).Contains(joiner.Id),
                    $"push recipients invalid owner={ownerLogin.PlayerId} joiner={joinerLogin.PlayerId}");

                using G2C_GetRoomChatHistory history = await joiner.Call(C2G_GetRoomChatHistory.Create()) as G2C_GetRoomChatHistory;
                Ensure(history.Error == ErrorCode.ERR_Success && history.RoomId == create.Room.RoomId && history.Messages.Count == 1,
                    $"history error={history.Error} count={history.Messages.Count}");
                Ensure(history.Messages[0].ChatMessageId == sent.ChatMessage.ChatMessageId && history.MaxChatSequence == 1, "history identity/order mismatch");
            });

            failed += await self.Case("CHAT-02 TextContractIsServerAuthoritative", async () =>
            {
                using Session owner = self.CreateSession();
                using G2C_Login _ = await self.RegisterAndLogin(owner, UniqueUser("cht"));
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(2010))) as G2C_CreateRoom;
                Ensure(create.Error == ErrorCode.ERR_Success, $"create error={create.Error}");

                using G2C_SendRoomChat empty = await owner.Call(SendChat(Op(2011), "   ")) as G2C_SendRoomChat;
                using G2C_SendRoomChat control = await owner.Call(SendChat(Op(2012), "a\nb")) as G2C_SendRoomChat;
                using G2C_SendRoomChat visible = await owner.Call(SendChat(Op(2013), new string('a', 201))) as G2C_SendRoomChat;
                using G2C_SendRoomChat bytes = await owner.Call(SendChat(Op(2014), new string('界', 201))) as G2C_SendRoomChat;
                Ensure(empty.Error == ErrorCode.ERR_OnlineChatTextInvalid, $"empty error={empty.Error}");
                Ensure(control.Error == ErrorCode.ERR_OnlineChatTextInvalid, $"control error={control.Error}");
                Ensure(visible.Error == ErrorCode.ERR_OnlineChatTextInvalid, $"visible error={visible.Error}");
                Ensure(bytes.Error == ErrorCode.ERR_OnlineChatTextInvalid, $"bytes error={bytes.Error}");
            });

            failed += await self.Case("CHAT-03 IdempotencyAndTokenBucketDoNotDoubleCharge", async () =>
            {
                using Session owner = self.CreateSession();
                using G2C_Login _ = await self.RegisterAndLogin(owner, UniqueUser("chi"));
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(2020))) as G2C_CreateRoom;
                Ensure(create.Error == ErrorCode.ERR_Success, $"create error={create.Error}");

                int before = self.RoomChatMessages.Count;
                using G2C_SendRoomChat first = await owner.Call(SendChat(Op(2021), "one")) as G2C_SendRoomChat;
                using G2C_SendRoomChat duplicate = await owner.Call(SendChat(Op(2021), "one")) as G2C_SendRoomChat;
                using G2C_SendRoomChat conflict = await owner.Call(SendChat(Op(2021), "different")) as G2C_SendRoomChat;
                Ensure(first.Error == 0 && duplicate.Error == 0 && first.ChatMessage.ChatSequence == duplicate.ChatMessage.ChatSequence, "duplicate did not return original result");
                Ensure(conflict.Error == ErrorCode.ERR_OnlineChatOperationConflict, $"conflict error={conflict.Error}");
                await self.WaitForChatPushCount(before + 1, 2000);
                await self.Root().GetComponent<TimerComponent>().WaitAsync(100);
                Ensure(self.RoomChatMessages.Count == before + 1, "duplicate message was broadcast twice");

                for (int i = 0; i < 4; i++)
                {
                    using G2C_SendRoomChat accepted = await owner.Call(SendChat(Op(2022 + i), $"burst-{i}")) as G2C_SendRoomChat;
                    Ensure(accepted.Error == ErrorCode.ERR_Success, $"burst {i} error={accepted.Error}");
                }
                using G2C_SendRoomChat limited = await owner.Call(SendChat(Op(2030), "limited")) as G2C_SendRoomChat;
                Ensure(limited.Error == ErrorCode.ERR_OnlineChatRateLimited, $"limit error={limited.Error}");
                await self.Root().GetComponent<TimerComponent>().WaitAsync(1050);
                using G2C_SendRoomChat recovered = await owner.Call(SendChat(Op(2030), "limited")) as G2C_SendRoomChat;
                Ensure(recovered.Error == ErrorCode.ERR_Success, $"recovered error={recovered.Error}");
            });

            failed += await self.Case("CHAT-04 HistoryRetainsExactlyLatestFifty", async () =>
            {
                Session[] sessions = { self.CreateSession(), self.CreateSession(), self.CreateSession(), self.CreateSession() };
                try
                {
                    using G2C_Login _ = await self.RegisterAndLogin(sessions[0], UniqueUser("chh"));
                    using G2C_CreateRoom create = await sessions[0].Call(CreateRoom(Op(2040))) as G2C_CreateRoom;
                    Ensure(create.Error == ErrorCode.ERR_Success, $"create error={create.Error}");
                    for (int i = 1; i < sessions.Length; i++)
                    {
                        using G2C_Login __ = await self.RegisterAndLogin(sessions[i], UniqueUser("chh"));
                        using G2C_JoinRoom joined = await sessions[i].Call(JoinRoom(Op(2040 + i), create.Room.RoomCode)) as G2C_JoinRoom;
                        Ensure(joined.Error == ErrorCode.ERR_Success, $"join {i} error={joined.Error}");
                    }

                    for (int sequence = 1; sequence <= 51; sequence++)
                    {
                        Session sender = sessions[(sequence - 1) % sessions.Length];
                        string id = Op(2100 + sequence);
                        while (true)
                        {
                            using G2C_SendRoomChat sent = await sender.Call(SendChat(id, $"message-{sequence}")) as G2C_SendRoomChat;
                            if (sent.Error == ErrorCode.ERR_OnlineChatRateLimited)
                            {
                                await self.Root().GetComponent<TimerComponent>().WaitAsync(1050);
                                continue;
                            }
                            Ensure(sent.Error == ErrorCode.ERR_Success && sent.ChatMessage.ChatSequence == sequence,
                                $"sequence {sequence} error={sent.Error} actual={sent.ChatMessage?.ChatSequence ?? 0}");
                            break;
                        }
                    }

                    using G2C_GetRoomChatHistory history = await sessions[3].Call(C2G_GetRoomChatHistory.Create()) as G2C_GetRoomChatHistory;
                    Ensure(history.Error == ErrorCode.ERR_Success && history.Messages.Count == 50, $"history count={history.Messages.Count} error={history.Error}");
                    Ensure(history.Messages[0].ChatSequence == 2 && history.Messages[49].ChatSequence == 51 && history.MaxChatSequence == 51,
                        $"history range={history.Messages[0].ChatSequence}-{history.Messages[49].ChatSequence} max={history.MaxChatSequence}");
                }
                finally
                {
                    foreach (Session session in sessions)
                    {
                        session.Dispose();
                    }
                }
            });

            return failed;
        }

        private static async ETTask<int> RunChatRetry(this OnlineTestComponent self)
        {
            return await self.Case("CHAT-05 PendingCreateConvergesWithSameOperationId", async () =>
            {
                using Session owner = self.CreateSession();
                using G2C_Login _ = await self.RegisterAndLogin(owner, UniqueUser("chr"));
                string operationId = Op(2201);
                using G2C_CreateRoom pending = await owner.Call(CreateRoom(operationId)) as G2C_CreateRoom;
                Ensure(pending.Error == ErrorCode.ERR_OnlineChatMembershipPending && pending.HasRoom && pending.Room != null,
                    $"first create did not expose committed pending room error={pending.Error} hasRoom={pending.HasRoom}");
                long roomId = pending.Room.RoomId;

                using G2C_CreateRoom completed = await owner.Call(CreateRoom(operationId)) as G2C_CreateRoom;
                Ensure(completed.Error == ErrorCode.ERR_Success && completed.HasRoom && completed.Room.RoomId == roomId,
                    $"pending create did not converge error={completed.Error} room={completed.Room?.RoomId ?? 0}/{roomId}");

                using G2C_CreateRoom duplicate = await owner.Call(CreateRoom(operationId)) as G2C_CreateRoom;
                Ensure(duplicate.Error == ErrorCode.ERR_Success && duplicate.Room.RoomId == roomId,
                    $"completed operation was not idempotent error={duplicate.Error} room={duplicate.Room?.RoomId ?? 0}/{roomId}");

                using G2C_SendRoomChat sent = await owner.Call(SendChat(Op(2202), "ready after retry")) as G2C_SendRoomChat;
                Ensure(sent.Error == ErrorCode.ERR_Success && sent.Accepted && sent.RoomId == roomId,
                    $"chat unavailable after convergence error={sent.Error} room={sent.RoomId}/{roomId}");
            });
        }

        private static async ETTask<int> RunChatCloseRetry(this OnlineTestComponent self)
        {
            return await self.Case("CHAT-CLOSE-T01 ChatCloseFailureRetriesAndRemovesChannelState", async () =>
            {
                using Session owner = self.CreateSession();
                using G2C_Login ownerLogin = await self.RegisterAndLogin(owner, UniqueUser("chc"));
                using G2C_CreateRoom create = await owner.Call(CreateRoom(Op(2301))) as G2C_CreateRoom;
                Ensure(create.Error == ErrorCode.ERR_Success && create.HasRoom && create.Room != null,
                    $"create error={create.Error} hasRoom={create.HasRoom}");
                string roomCode = create.Room.RoomCode;

                using G2C_SendRoomChat sent = await owner.Call(SendChat(Op(2302), "close cleanup seed")) as G2C_SendRoomChat;
                Ensure(sent.Error == ErrorCode.ERR_Success && sent.Accepted && sent.RoomId == create.Room.RoomId,
                    $"seed chat error={sent.Error} room={sent.RoomId}/{create.Room.RoomId}");
                using G2C_GetRoomChatHistory beforeLeaveHistory = await owner.Call(C2G_GetRoomChatHistory.Create()) as G2C_GetRoomChatHistory;
                Ensure(beforeLeaveHistory.Error == ErrorCode.ERR_Success && beforeLeaveHistory.Messages.Count == 1,
                    $"seed history error={beforeLeaveHistory.Error} count={beforeLeaveHistory.Messages.Count}");

                using G2C_LeaveRoom leave = await owner.Call(LeaveRoom(Op(2303))) as G2C_LeaveRoom;
                Ensure(leave.Error == ErrorCode.ERR_Success, $"leave error={leave.Error} player={ownerLogin.PlayerId}");
                using G2C_GetCurrentRoom current = await owner.Call(C2G_GetCurrentRoom.Create()) as G2C_GetCurrentRoom;
                Ensure(current.Error == ErrorCode.ERR_Success && !current.HasRoom,
                    $"current after leave error={current.Error} hasRoom={current.HasRoom}");

                using G2C_SendRoomChat afterLeaveSend = await owner.Call(SendChat(Op(2304), "should not send")) as G2C_SendRoomChat;
                Ensure(afterLeaveSend.Error == ErrorCode.ERR_OnlineNotInRoom, $"send after leave error={afterLeaveSend.Error}");
                using G2C_GetRoomChatHistory afterLeaveHistory = await owner.Call(C2G_GetRoomChatHistory.Create()) as G2C_GetRoomChatHistory;
                Ensure(afterLeaveHistory.Error == ErrorCode.ERR_OnlineNotInRoom,
                    $"history after leave error={afterLeaveHistory.Error} count={afterLeaveHistory.Messages.Count}");

                using Session joiner = self.CreateSession();
                using G2C_Login _ = await self.RegisterAndLogin(joiner, UniqueUser("chc"));
                using G2C_JoinRoom firstJoin = await joiner.Call(JoinRoom(Op(2305), roomCode)) as G2C_JoinRoom;
                Ensure(firstJoin.Error == ErrorCode.ERR_OnlineRoomClosing, $"first closing join error={firstJoin.Error}");

                await self.Root().GetComponent<TimerComponent>().WaitAsync(5500);
                using G2C_JoinRoom secondJoin = await joiner.Call(JoinRoom(Op(2306), roomCode)) as G2C_JoinRoom;
                Ensure(secondJoin.Error == ErrorCode.ERR_OnlineRoomClosing, $"second closing join error={secondJoin.Error}");

                await self.Root().GetComponent<TimerComponent>().WaitAsync(5500);
                using G2C_JoinRoom finalJoin = await joiner.Call(JoinRoom(Op(2307), roomCode)) as G2C_JoinRoom;
                Ensure(finalJoin.Error == ErrorCode.ERR_OnlineRoomNotFound, $"final join error={finalJoin.Error}");

                using G2C_SendRoomChat finalSend = await owner.Call(SendChat(Op(2308), "still closed")) as G2C_SendRoomChat;
                Ensure(finalSend.Error == ErrorCode.ERR_OnlineNotInRoom, $"final send error={finalSend.Error}");
                using G2C_GetRoomChatHistory finalHistory = await owner.Call(C2G_GetRoomChatHistory.Create()) as G2C_GetRoomChatHistory;
                Ensure(finalHistory.Error == ErrorCode.ERR_OnlineNotInRoom,
                    $"final history error={finalHistory.Error} count={finalHistory.Messages.Count}");
            });
        }

        private static C2G_SendRoomChat SendChat(string clientMessageId, string content)
        {
            C2G_SendRoomChat request = C2G_SendRoomChat.Create();
            request.ClientMessageId = clientMessageId;
            request.Content = content;
            return request;
        }

        private static async ETTask WaitForChatPushCount(this OnlineTestComponent self, int expectedCount, int timeoutMs)
        {
            long end = TimeInfo.Instance.ClientNow() + timeoutMs;
            while (TimeInfo.Instance.ClientNow() < end)
            {
                if (self.RoomChatMessages.Count >= expectedCount)
                {
                    return;
                }
                await self.Root().GetComponent<TimerComponent>().WaitAsync(25);
            }
            throw new Exception($"chat push count timeout expected={expectedCount} actual={self.RoomChatMessages.Count}");
        }
    }
}
