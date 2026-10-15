using System.Collections.Generic;

namespace ET.Server
{
    [EnableClass]
    public sealed class OnlineWorldCorpse
    {
        public long CorpseId, SourceUnitId, SourcePlayerId, WorldId, ExpiresAtServerTime, StateRevision;
        public int SourceKind, Capacity;
        public string SourceConfigId, MapId;
        public float PositionX, PositionY, PositionZ, YawDegrees;
        public readonly List<OnlineItemSlotRecord> Slots = new();
    }

    [EnableClass]
    public sealed class OnlineWorldPendingRespawn
    {
        public long PlayerId, PreviousUnitId, RespawnAtServerTime;
        public string DisplayName, PlayerConfigId;
        public ActorId PlayerActorId;
        public OnlineWorldConnectionState ConnectionState;
    }

    [EnableClass]
    public sealed class OnlineWorldLeaveRecord
    {
        public long PlayerId, UnitId;
        public string OperationId;
        public bool Prepared, Completed, Aborted;
        public OnlineInventoryRecord Inventory;
    }

    [EnableClass]
    public sealed class OnlineWorldInventoryOperationRecord
    {
        public string Fingerprint;
        public int Error;
        public bool Accepted, HasCorpse;
        public long CompletedAt, CorpseId;
    }
}
