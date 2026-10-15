using System;
using System.Collections.Generic;
using QFramework;

namespace Game
{
    public enum OnlineRoomChatHistoryState
    {
        None,
        Loading,
        Ready,
        Failed
    }

    public enum OnlineRoomChatSendState
    {
        Idle,
        Sending
    }

    public enum OnlineRoomChatResultCode
    {
        None,
        Success,
        ClientInputInvalid,
        NotLoggedIn,
        NotInRoom,
        MembershipPending,
        RateLimited,
        OperationConflict,
        ServiceUnavailable,
        Disconnected,
        Timeout,
        OperationInProgress,
        UnknownFailure
    }

    public readonly struct OnlineRoomChatMessageSnapshot
    {
        public OnlineRoomChatMessageSnapshot(
            string chatMessageId,
            long chatSequence,
            long senderPlayerId,
            string senderDisplayName,
            long serverTime,
            string content)
        {
            ChatMessageId = chatMessageId ?? string.Empty;
            ChatSequence = chatSequence;
            SenderPlayerId = senderPlayerId;
            SenderDisplayName = senderDisplayName ?? string.Empty;
            ServerTime = serverTime;
            Content = content ?? string.Empty;
        }

        public string ChatMessageId { get; }
        public long ChatSequence { get; }
        public long SenderPlayerId { get; }
        public string SenderDisplayName { get; }
        public long ServerTime { get; }
        public string Content { get; }
    }

    public sealed class OnlineRoomChatSnapshot
    {
        public OnlineRoomChatSnapshot(
            long roomId,
            IReadOnlyList<OnlineRoomChatMessageSnapshot> messages,
            long maxConfirmedSequence,
            OnlineRoomChatHistoryState historyState,
            OnlineRoomChatSendState sendState,
            OnlineRoomChatResultCode lastResultCode,
            bool connectionLost,
            string pendingContent)
        {
            RoomId = roomId;
            Messages = CopyMessages(messages);
            MaxConfirmedSequence = maxConfirmedSequence;
            HistoryState = historyState;
            SendState = sendState;
            LastResultCode = lastResultCode;
            ConnectionLost = connectionLost;
            PendingContent = pendingContent ?? string.Empty;
        }

        public long RoomId { get; }
        public IReadOnlyList<OnlineRoomChatMessageSnapshot> Messages { get; }
        public long MaxConfirmedSequence { get; }
        public OnlineRoomChatHistoryState HistoryState { get; }
        public OnlineRoomChatSendState SendState { get; }
        public OnlineRoomChatResultCode LastResultCode { get; }
        public bool ConnectionLost { get; }
        public string PendingContent { get; }

        public static OnlineRoomChatSnapshot Empty(OnlineRoomChatResultCode result = OnlineRoomChatResultCode.None)
        {
            return new OnlineRoomChatSnapshot(0, Array.Empty<OnlineRoomChatMessageSnapshot>(), 0,
                OnlineRoomChatHistoryState.None, OnlineRoomChatSendState.Idle, result, false, string.Empty);
        }

        private static IReadOnlyList<OnlineRoomChatMessageSnapshot> CopyMessages(
            IReadOnlyList<OnlineRoomChatMessageSnapshot> messages)
        {
            if (messages == null || messages.Count == 0)
            {
                return Array.Empty<OnlineRoomChatMessageSnapshot>();
            }

            var copy = new OnlineRoomChatMessageSnapshot[messages.Count];
            for (var i = 0; i < messages.Count; i++)
            {
                copy[i] = messages[i];
            }

            return Array.AsReadOnly(copy);
        }
    }

    public readonly struct OnlineRoomChatSendResult
    {
        public OnlineRoomChatSendResult(OnlineRoomChatResultCode code, OnlineRoomChatSnapshot snapshot, bool shouldClearInput)
        {
            Code = code;
            Snapshot = snapshot;
            ShouldClearInput = shouldClearInput;
        }

        public OnlineRoomChatResultCode Code { get; }
        public OnlineRoomChatSnapshot Snapshot { get; }
        public bool ShouldClearInput { get; }
        public bool Success => Code == OnlineRoomChatResultCode.Success;
    }

    public readonly struct OnlineRoomChatChangedEvent
    {
        public OnlineRoomChatChangedEvent(OnlineRoomChatSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public OnlineRoomChatSnapshot Snapshot { get; }
    }

    public sealed class OnlineRoomChatModel : AbstractModel
    {
        public BindableProperty<OnlineRoomChatSnapshot> Snapshot { get; } =
            new BindableProperty<OnlineRoomChatSnapshot>(OnlineRoomChatSnapshot.Empty());

        protected override void OnInit()
        {
        }

        public void Publish(OnlineRoomChatSnapshot snapshot)
        {
            Snapshot.Value = snapshot;
            this.SendEvent(new OnlineRoomChatChangedEvent(snapshot));
        }
    }
}
