using System.Collections.Generic;

namespace ET.Server
{
    [EnableClass]
    public sealed class OnlineChatMember
    {
        public long PlayerId;
        public string DisplayName;
        public int ConnectionState;
    }

    [EnableClass]
    public sealed class OnlineChatStoredMessage
    {
        public string ChatMessageId;
        public long ChatSequence;
        public long SenderPlayerId;
        public string SenderDisplayName;
        public long ServerTime;
        public string Content;
    }

    [EnableClass]
    public sealed class OnlineChatSendRecord
    {
        public long CreatedAt;
        public string Content;
        public OnlineChatStoredMessage Message;
    }

    [EnableClass]
    public sealed class OnlineChatRateState
    {
        public int Tokens = 5;
        public long LastRefillAt;
    }

    [EnableClass]
    public sealed class OnlineChatChannel
    {
        public long RoomId;
        public long MembershipRevision;
        public long NextChatSequence = 1;
        public readonly Dictionary<long, OnlineChatMember> Members = new();
        public readonly Queue<OnlineChatStoredMessage> History = new();
        public readonly Dictionary<string, OnlineChatSendRecord> SendRecords = new();
        public readonly Dictionary<long, OnlineChatRateState> RateStates = new();
    }

    [ComponentOf(typeof(Scene))]
    public class OnlineChatComponent: Entity, IAwake
    {
        public readonly Dictionary<long, OnlineChatChannel> Channels = new();
        public int FailedSyncCount;
        public int FailedCloseCount;
    }
}
