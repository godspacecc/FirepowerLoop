using System.Collections.Generic;

namespace ET.Server
{
    public enum OnlinePlayerState
    {
        Online = 1,
        DisconnectedGrace = 2,
        Offlining = 3,
    }

    [EnableClass]
    public sealed class OnlineRoomOperationRecord
    {
        public string OperationType;
        public string OperationId;
        public long CreatedAt;
        public int Error;
        public string Message;
        public bool HasRoom;
        public OnlineRoomSnapshot Room;
    }

    [ComponentOf(typeof(Scene))]
    public class OnlinePlayerManagerComponent: Entity, IAwake
    {
        public readonly Dictionary<long, EntityRef<OnlinePlayerActor>> Players = new();
    }

    [ChildOf(typeof(OnlinePlayerManagerComponent))]
    public class OnlinePlayerActor: Entity, IAwake<long, long, string>, IDestroy
    {
        public long AccountId;
        public long PlayerId;
        public string DisplayName;
        public long Revision;
        public OnlinePlayerState State;
        public ActorId GateProxyActorId;
        public long SessionEpoch;
        public long TimerGeneration;
        public bool SaveRetryPending;
        public long CurrentRoomId;
        public bool PendingRoomSync;
        public int PendingRoomConnectionState;
        public long PendingRoomSyncEpoch;
        public long RoomSyncGeneration;
        public bool RoomSyncRetryQueued;
        public bool PendingOfflineRoomLeave;
        public string PendingOfflineRoomLeaveOperationId;
        public string PendingRoomReadyOperationType;
        public string PendingRoomReadyOperationId;
        public long PendingRoomReadyRoomId;
        public readonly Queue<OnlineRoomOperationRecord> RoomOperations = new();
    }
}
