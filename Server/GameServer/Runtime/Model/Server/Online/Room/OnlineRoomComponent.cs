using System.Collections.Generic;

namespace ET.Server
{
    public enum OnlineRoomMemberConnectionState
    {
        Online = 1,
        DisconnectedGrace = 2,
    }

    /// <summary>房间用途由服务端创建时锁定；普通联机与街机联机不能混用同一房间。</summary>
    public enum OnlineRoomPurpose
    {
        General = 0,
        Arcade = 1,
    }

    public enum OnlineRoomVisibility
    {
        Public = 0,
        Private = 1,
    }

    [EnableClass]
    public sealed class OnlineRoomMember
    {
        public long PlayerId;
        public string DisplayName;
        public OnlineRoomMemberConnectionState ConnectionState;
        public long SessionEpoch;
        public long JoinSequence;
    }

    [EnableClass]
    public sealed class OnlineRoomCloseRecord
    {
        public long RoomId;
        public long TombstoneUntil;
    }

    [EnableClass]
    public sealed class OnlineRoomRateBucket
    {
        public long WindowStart;
        public int Count;
    }

    [ComponentOf(typeof(Scene))]
    public class OnlineRoomDirectoryComponent: Entity, IAwake
    {
        public readonly Dictionary<string, long> ActiveRooms = new();
        public readonly HashSet<string> ReservedCodes = new();
        public readonly Dictionary<string, OnlineRoomCloseRecord> Tombstones = new();
        /// <summary>按客户端版本和街机配置指纹隔离的单人等待房；房间被领取或关闭后立即移除。</summary>
        public readonly Dictionary<string, long> ArcadeMatchRooms = new();
        /// <summary>等待房反向索引，用于房间关闭时幂等清理匹配入口。</summary>
        public readonly Dictionary<long, string> ArcadeMatchKeysByRoomId = new();
        public readonly Dictionary<long, OnlineRoomRateBucket> PlayerJoinAttempts = new();
        public readonly Dictionary<string, OnlineRoomRateBucket> SourceJoinAttempts = new();
    }

    [ComponentOf(typeof(Scene))]
    public class OnlineRoomManagerComponent: Entity, IAwake
    {
        public readonly Dictionary<long, EntityRef<OnlineRoomActor>> Rooms = new();
        public int FailedCloseCount;
        public int FailedLeaveCount;
        public int FailedConnectionSyncCount;
        public int FailedGetCurrentCount;
    }

    [ChildOf(typeof(OnlineRoomManagerComponent))]
    public class OnlineRoomActor: Entity, IAwake, IDestroy
    {
        public long RoomId;
        public string RoomCode;
        public long OwnerPlayerId;
        public long MembershipRevision;
        public long NextJoinSequence;
        public OnlineRoomPurpose Purpose;
        public OnlineRoomVisibility Visibility;
        public int Capacity;
        public bool MembershipLocked;
        public string ClientVersion;
        public string ArcadeConfigFingerprint;
        public bool Closing;
        public bool CloseRetryPending;
        public long ChatConfirmedRevision;
        public long ChatSyncGeneration;
        public bool ChatSyncRetryPending;
        public readonly Dictionary<long, OnlineRoomMember> Members = new();
    }
}
