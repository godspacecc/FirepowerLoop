using System.Collections.Generic;
using QFramework;

namespace Game
{
    public enum OnlineRoomMemberConnectionState
    {
        Online = 1,
        DisconnectedGrace = 2
    }

    public enum OnlineRoomPurpose
    {
        General = 0,
        Arcade = 1
    }

    public enum OnlineRoomVisibility
    {
        Public = 0,
        Private = 1
    }

    public enum OnlineRoomOperationState
    {
        Idle,
        Creating,
        Matching,
        Joining,
        Leaving,
        Restoring
    }

    public enum OnlineRoomResultCode
    {
        None,
        Success,
        ClientInputInvalid,
        RoomCodeInvalid,
        RoomNotFound,
        RoomFull,
        AlreadyInRoom,
        NotInRoom,
        OperationInvalid,
        RateLimited,
        RoomClosing,
        MembershipPending,
        ServiceUnavailable,
        NotLoggedIn,
        Disconnected,
        Timeout,
        OperationInProgress,
        PurposeMismatch,
        VersionMismatch,
        UnknownFailure
    }

    public enum OnlinePublicRoomListState
    {
        Idle,
        Refreshing,
        LoadingMore
    }

    public enum OnlinePublicRoomListOperation
    {
        None,
        Refresh,
        LoadMore
    }

    public readonly struct OnlinePublicRoomItem
    {
        public OnlinePublicRoomItem(string roomCode, string ownerDisplayName, int memberCount, int capacity)
        {
            RoomCode = roomCode ?? string.Empty;
            OwnerDisplayName = ownerDisplayName ?? string.Empty;
            MemberCount = memberCount;
            Capacity = capacity;
        }

        public string RoomCode { get; }
        public string OwnerDisplayName { get; }
        public int MemberCount { get; }
        public int Capacity { get; }
    }

    public sealed class OnlinePublicRoomListSnapshot
    {
        public OnlinePublicRoomListSnapshot(
            IReadOnlyList<OnlinePublicRoomItem> items,
            long nextCursorRoomId,
            bool hasMore,
            OnlinePublicRoomListState state,
            OnlinePublicRoomListOperation lastOperation,
            OnlineRoomResultCode lastResultCode)
        {
            Items = CopyItems(items);
            NextCursorRoomId = nextCursorRoomId;
            HasMore = hasMore;
            State = state;
            LastOperation = lastOperation;
            LastResultCode = lastResultCode;
        }

        public IReadOnlyList<OnlinePublicRoomItem> Items { get; }
        public long NextCursorRoomId { get; }
        public bool HasMore { get; }
        public OnlinePublicRoomListState State { get; }
        public OnlinePublicRoomListOperation LastOperation { get; }
        public OnlineRoomResultCode LastResultCode { get; }

        public static OnlinePublicRoomListSnapshot Empty()
        {
            return new OnlinePublicRoomListSnapshot(
                System.Array.Empty<OnlinePublicRoomItem>(), 0, false,
                OnlinePublicRoomListState.Idle, OnlinePublicRoomListOperation.None, OnlineRoomResultCode.None);
        }

        private static IReadOnlyList<OnlinePublicRoomItem> CopyItems(IReadOnlyList<OnlinePublicRoomItem> items)
        {
            if (items == null || items.Count == 0)
            {
                return System.Array.Empty<OnlinePublicRoomItem>();
            }

            var copy = new OnlinePublicRoomItem[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                copy[i] = items[i];
            }

            return System.Array.AsReadOnly(copy);
        }
    }

    public readonly struct OnlinePublicRoomListResult
    {
        public OnlinePublicRoomListResult(OnlineRoomResultCode code, OnlinePublicRoomListSnapshot snapshot)
        {
            Code = code;
            Snapshot = snapshot;
        }

        public OnlineRoomResultCode Code { get; }
        public OnlinePublicRoomListSnapshot Snapshot { get; }
        public bool Success => Code == OnlineRoomResultCode.Success;
    }

    public readonly struct OnlinePublicRoomListChangedEvent
    {
        public OnlinePublicRoomListChangedEvent(OnlinePublicRoomListSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public OnlinePublicRoomListSnapshot Snapshot { get; }
    }

    public readonly struct OnlineRoomMemberSnapshot
    {
        public OnlineRoomMemberSnapshot(long playerId, string displayName, bool isOwner, OnlineRoomMemberConnectionState connectionState, long joinSequence)
        {
            PlayerId = playerId;
            DisplayName = displayName ?? string.Empty;
            IsOwner = isOwner;
            ConnectionState = connectionState;
            JoinSequence = joinSequence;
        }

        public long PlayerId { get; }
        public string DisplayName { get; }
        public bool IsOwner { get; }
        public OnlineRoomMemberConnectionState ConnectionState { get; }
        public long JoinSequence { get; }
    }

    public sealed class OnlineRoomSnapshot
    {
        public OnlineRoomSnapshot(
            bool hasRoom,
            long roomId,
            string roomCode,
            long ownerPlayerId,
            long membershipRevision,
            IReadOnlyList<OnlineRoomMemberSnapshot> members,
            bool connectionLost,
            OnlineRoomOperationState operationState,
            OnlineRoomResultCode lastResultCode,
            OnlineRoomPurpose purpose = OnlineRoomPurpose.General,
            OnlineRoomVisibility visibility = OnlineRoomVisibility.Public,
            int capacity = 4,
            bool membershipLocked = false,
            string clientVersion = "",
            string arcadeConfigFingerprint = "")
        {
            HasRoom = hasRoom;
            RoomId = roomId;
            RoomCode = roomCode ?? string.Empty;
            OwnerPlayerId = ownerPlayerId;
            MembershipRevision = membershipRevision;
            Members = CopyMembers(members);
            ConnectionLost = connectionLost;
            OperationState = operationState;
            LastResultCode = lastResultCode;
            Purpose = purpose;
            Visibility = visibility;
            Capacity = capacity;
            MembershipLocked = membershipLocked;
            ClientVersion = clientVersion ?? string.Empty;
            ArcadeConfigFingerprint = arcadeConfigFingerprint ?? string.Empty;
        }

        public bool HasRoom { get; }
        public long RoomId { get; }
        public string RoomCode { get; }
        public long OwnerPlayerId { get; }
        public long MembershipRevision { get; }
        public IReadOnlyList<OnlineRoomMemberSnapshot> Members { get; }
        public bool ConnectionLost { get; }
        public OnlineRoomOperationState OperationState { get; }
        public OnlineRoomResultCode LastResultCode { get; }
        public OnlineRoomPurpose Purpose { get; }
        public OnlineRoomVisibility Visibility { get; }
        public int Capacity { get; }
        public bool MembershipLocked { get; }
        public string ClientVersion { get; }
        public string ArcadeConfigFingerprint { get; }

        public static OnlineRoomSnapshot Empty(OnlineRoomResultCode result = OnlineRoomResultCode.None)
        {
            return new OnlineRoomSnapshot(false, 0, string.Empty, 0, 0,
                System.Array.Empty<OnlineRoomMemberSnapshot>(), false, OnlineRoomOperationState.Idle, result,
                capacity: 0);
        }

        public OnlineRoomSnapshot WithOperation(OnlineRoomOperationState operation, OnlineRoomResultCode result)
        {
            return new OnlineRoomSnapshot(HasRoom, RoomId, RoomCode, OwnerPlayerId, MembershipRevision, Members,
                ConnectionLost, operation, result, Purpose, Visibility, Capacity, MembershipLocked, ClientVersion,
                ArcadeConfigFingerprint);
        }

        public OnlineRoomSnapshot WithConnectionLost(bool lost)
        {
            return new OnlineRoomSnapshot(HasRoom, RoomId, RoomCode, OwnerPlayerId, MembershipRevision, Members,
                lost, OperationState, LastResultCode, Purpose, Visibility, Capacity, MembershipLocked, ClientVersion,
                ArcadeConfigFingerprint);
        }

        private static IReadOnlyList<OnlineRoomMemberSnapshot> CopyMembers(
            IReadOnlyList<OnlineRoomMemberSnapshot> members)
        {
            if (members == null || members.Count == 0)
            {
                return System.Array.Empty<OnlineRoomMemberSnapshot>();
            }

            var copy = new OnlineRoomMemberSnapshot[members.Count];
            for (var i = 0; i < members.Count; i++)
            {
                copy[i] = members[i];
            }

            return System.Array.AsReadOnly(copy);
        }
    }

    public readonly struct OnlineRoomOperationResult
    {
        public OnlineRoomOperationResult(OnlineRoomResultCode code, OnlineRoomSnapshot snapshot)
        {
            Code = code;
            Snapshot = snapshot;
        }

        public OnlineRoomResultCode Code { get; }
        public OnlineRoomSnapshot Snapshot { get; }
        public bool Success => Code == OnlineRoomResultCode.Success;
    }

    public readonly struct OnlineRoomChangedEvent
    {
        public OnlineRoomChangedEvent(OnlineRoomSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public OnlineRoomSnapshot Snapshot { get; }
    }

    public sealed class OnlineRoomModel : AbstractModel
    {
        public BindableProperty<OnlineRoomSnapshot> Snapshot { get; } =
            new BindableProperty<OnlineRoomSnapshot>(OnlineRoomSnapshot.Empty());
        public BindableProperty<OnlinePublicRoomListSnapshot> PublicRooms { get; } =
            new BindableProperty<OnlinePublicRoomListSnapshot>(OnlinePublicRoomListSnapshot.Empty());

        protected override void OnInit()
        {
        }

        public void Publish(OnlineRoomSnapshot snapshot)
        {
            Snapshot.Value = snapshot;
            this.SendEvent(new OnlineRoomChangedEvent(snapshot));
        }

        // 同一不可变快照先进入 Model 再发事件，订阅者在事件处理中读取到的状态始终一致。
        public void PublishPublicRooms(OnlinePublicRoomListSnapshot snapshot)
        {
            PublicRooms.Value = snapshot;
            this.SendEvent(new OnlinePublicRoomListChangedEvent(snapshot));
        }
    }
}
