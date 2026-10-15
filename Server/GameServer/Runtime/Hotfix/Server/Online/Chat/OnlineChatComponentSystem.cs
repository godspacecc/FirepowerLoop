using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ET.Server
{
    [EntitySystemOf(typeof(OnlineChatComponent))]
    [FriendOf(typeof(OnlineChatComponent))]
    public static partial class OnlineChatComponentSystem
    {
        private const int HistoryLimit = 50;
        private const int SendRecordLimit = 256;
        private const long SendRecordLifetimeMilliseconds = 10 * 60 * 1000;

        [EntitySystem]
        private static void Awake(this OnlineChatComponent self)
        {
        }

        public static long SyncMembers(this OnlineChatComponent self, R2C_SyncRoomChatMembers request)
        {
            if (request.RoomId == 0 || request.MembershipRevision <= 0)
            {
                throw new RpcException(ErrorCode.ERR_OnlineChatMembershipPending, "成员同步处理中");
            }

            if (!self.Channels.TryGetValue(request.RoomId, out OnlineChatChannel channel))
            {
                channel = new OnlineChatChannel { RoomId = request.RoomId };
                self.Channels.Add(request.RoomId, channel);
            }

            if (request.MembershipRevision <= channel.MembershipRevision)
            {
                return channel.MembershipRevision;
            }

            channel.Members.Clear();
            foreach (RoomChatMemberInfo member in request.Members)
            {
                if (member.PlayerId == 0 || channel.Members.ContainsKey(member.PlayerId))
                {
                    continue;
                }

                channel.Members.Add(member.PlayerId, new OnlineChatMember
                {
                    PlayerId = member.PlayerId,
                    DisplayName = member.DisplayName ?? string.Empty,
                    ConnectionState = member.ConnectionState,
                });
            }

            channel.MembershipRevision = request.MembershipRevision;
            return channel.MembershipRevision;
        }

        public static bool CloseChannel(this OnlineChatComponent self, long roomId)
        {
            if (roomId == 0)
            {
                return false;
            }

            return self.Channels.Remove(roomId);
        }

        public static async ETTask<(int Error, string Message, OnlineChatStoredMessage ChatMessage)> Send(
            this OnlineChatComponent self,
            long roomId,
            long playerId,
            string clientMessageId,
            string content)
        {
            if (!self.Channels.TryGetValue(roomId, out OnlineChatChannel channel) || !channel.Members.TryGetValue(playerId, out OnlineChatMember member))
            {
                return (ErrorCode.ERR_OnlineNotInRoom, "当前不在房间中", null);
            }

            if (!IsValidClientMessageId(clientMessageId))
            {
                return (ErrorCode.ERR_OnlineChatOperationConflict, "消息标识无效或冲突", null);
            }

            if (!TryNormalizeContent(content, out string normalized))
            {
                return (ErrorCode.ERR_OnlineChatTextInvalid, "消息内容不合法", null);
            }

            long now = TimeInfo.Instance.ServerNow();
            channel.CleanupSendRecords(now);
            string recordKey = MakeRecordKey(playerId, clientMessageId);
            if (channel.SendRecords.TryGetValue(recordKey, out OnlineChatSendRecord existing))
            {
                return existing.Content == normalized
                    ? (ErrorCode.ERR_Success, string.Empty, existing.Message)
                    : (ErrorCode.ERR_OnlineChatOperationConflict, "消息标识无效或冲突", null);
            }

            if (!channel.TryConsumeSendToken(playerId, now))
            {
                return (ErrorCode.ERR_OnlineChatRateLimited, "发送过于频繁，请稍后重试", null);
            }

            long sequence = channel.NextChatSequence++;
            OnlineChatStoredMessage accepted = new()
            {
                ChatMessageId = $"{(ulong)roomId:x16}{(ulong)sequence:x16}",
                ChatSequence = sequence,
                SenderPlayerId = playerId,
                SenderDisplayName = member.DisplayName ?? string.Empty,
                ServerTime = now,
                Content = normalized,
            };
            channel.History.Enqueue(accepted);
            while (channel.History.Count > HistoryLimit)
            {
                channel.History.Dequeue();
            }

            channel.SendRecords.Add(recordKey, new OnlineChatSendRecord
            {
                CreatedAt = now,
                Content = normalized,
                Message = accepted,
            });

            await self.Broadcast(channel, accepted);
            return (ErrorCode.ERR_Success, string.Empty, accepted);
        }

        public static (int Error, string Message, long MaxSequence, OnlineChatStoredMessage[] Messages) GetHistory(
            this OnlineChatComponent self,
            long roomId,
            long playerId)
        {
            if (!self.Channels.TryGetValue(roomId, out OnlineChatChannel channel) || !channel.Members.ContainsKey(playerId))
            {
                return (ErrorCode.ERR_OnlineNotInRoom, "当前不在房间中", 0, Array.Empty<OnlineChatStoredMessage>());
            }

            return (ErrorCode.ERR_Success, string.Empty, channel.NextChatSequence - 1, channel.History.ToArray());
        }

        public static RoomChatMessageInfo ToProtocol(OnlineChatStoredMessage source)
        {
            if (source == null)
            {
                return null;
            }

            RoomChatMessageInfo message = RoomChatMessageInfo.Create();
            message.ChatMessageId = source.ChatMessageId;
            message.ChatSequence = source.ChatSequence;
            message.SenderPlayerId = source.SenderPlayerId;
            message.SenderDisplayName = source.SenderDisplayName;
            message.ServerTime = source.ServerTime;
            message.Content = source.Content;
            return message;
        }

        public static RoomChatMessageInfo CloneProtocol(RoomChatMessageInfo source)
        {
            if (source == null)
            {
                return null;
            }

            RoomChatMessageInfo clone = RoomChatMessageInfo.Create();
            clone.ChatMessageId = source.ChatMessageId;
            clone.ChatSequence = source.ChatSequence;
            clone.SenderPlayerId = source.SenderPlayerId;
            clone.SenderDisplayName = source.SenderDisplayName;
            clone.ServerTime = source.ServerTime;
            clone.Content = source.Content;
            return clone;
        }

        public static bool TryNormalizeContent(string content, out string normalized)
        {
            normalized = (content ?? string.Empty).Trim();
            if (normalized.Length == 0 || Encoding.UTF8.GetByteCount(normalized) > 800)
            {
                return false;
            }

            TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(normalized);
            int visibleCount = 0;
            while (elements.MoveNext())
            {
                if (++visibleCount > 200)
                {
                    return false;
                }
            }

            return !normalized.Any(char.IsControl);
        }

        private static bool IsValidClientMessageId(string clientMessageId)
        {
            return clientMessageId != null && clientMessageId.Length == 32 && clientMessageId.All(Uri.IsHexDigit);
        }

        private static string MakeRecordKey(long playerId, string clientMessageId)
        {
            return $"{playerId}:{clientMessageId.ToLowerInvariant()}";
        }

        private static bool TryConsumeSendToken(this OnlineChatChannel channel, long playerId, long now)
        {
            if (!channel.RateStates.TryGetValue(playerId, out OnlineChatRateState rate))
            {
                rate = new OnlineChatRateState { LastRefillAt = now };
                channel.RateStates.Add(playerId, rate);
            }

            long elapsed = Math.Max(0, now - rate.LastRefillAt);
            int recovered = (int)(elapsed / 1000);
            if (recovered > 0)
            {
                rate.Tokens = Math.Min(5, rate.Tokens + recovered);
                rate.LastRefillAt += recovered * 1000L;
            }

            if (rate.Tokens <= 0)
            {
                return false;
            }

            rate.Tokens--;
            return true;
        }

        private static void CleanupSendRecords(this OnlineChatChannel channel, long now)
        {
            foreach (string key in channel.SendRecords
                         .Where(pair => now - pair.Value.CreatedAt > SendRecordLifetimeMilliseconds)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                channel.SendRecords.Remove(key);
            }

            if (channel.SendRecords.Count <= SendRecordLimit)
            {
                return;
            }

            foreach (string key in channel.SendRecords.OrderBy(pair => pair.Value.CreatedAt)
                         .Take(channel.SendRecords.Count - SendRecordLimit)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                channel.SendRecords.Remove(key);
            }
        }

        private static async ETTask Broadcast(this OnlineChatComponent self, OnlineChatChannel channel, OnlineChatStoredMessage message)
        {
            foreach (long playerId in channel.Members.Keys.ToArray())
            {
                try
                {
                    ActorId playerActor = await self.Root().GetComponent<LocationProxyComponent>().Get(LocationType.OnlinePlayer, playerId);
                    C2P_RoomChatMessage push = C2P_RoomChatMessage.Create();
                    push.RoomId = channel.RoomId;
                    push.ChatMessage = ToProtocol(message);
                    self.Root().GetComponent<MessageSender>().Send(playerActor, push);
                }
                catch (Exception e)
                {
                    Log.Warning($"online chat push skipped stage=RouteToPlayer playerId={playerId} roomId={channel.RoomId} error={e.GetType().Name}");
                }
            }
        }
    }
}
