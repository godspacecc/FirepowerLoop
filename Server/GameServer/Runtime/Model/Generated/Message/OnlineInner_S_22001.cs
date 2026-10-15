using MemoryPack;
using System.Collections.Generic;

namespace ET
{
    [MemoryPackable]
    [Message(OnlineInner.G2A_Register)]
    [ResponseType(nameof(A2G_Register))]
    public partial class G2A_Register : MessageObject, IRequest
    {
        public static G2A_Register Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2A_Register), isFromPool) as G2A_Register;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public string UserName { get; set; }

        [MemoryPackOrder(2)]
        public string Password { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.UserName = default;
            this.Password = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.A2G_Register)]
    public partial class A2G_Register : MessageObject, IResponse
    {
        public static A2G_Register Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(A2G_Register), isFromPool) as A2G_Register;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public long AccountId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.AccountId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2A_Login)]
    [ResponseType(nameof(A2G_Login))]
    public partial class G2A_Login : MessageObject, IRequest
    {
        public static G2A_Login Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2A_Login), isFromPool) as G2A_Login;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public string UserName { get; set; }

        [MemoryPackOrder(2)]
        public string Password { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.UserName = default;
            this.Password = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.A2G_Login)]
    public partial class A2G_Login : MessageObject, IResponse
    {
        public static A2G_Login Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(A2G_Login), isFromPool) as A2G_Login;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public long AccountId { get; set; }

        [MemoryPackOrder(4)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(5)]
        public string DisplayName { get; set; }

        [MemoryPackOrder(6)]
        public long Revision { get; set; }

        [MemoryPackOrder(7)]
        public bool IsNewAccount { get; set; }

        [MemoryPackOrder(8)]
        public OnlineInventoryInfo OnlineInventory { get; set; }

        [MemoryPackOrder(9)]
        public bool HasReceivedOnlineStarterSupplies { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.AccountId = default;
            this.PlayerId = default;
            this.DisplayName = default;
            this.Revision = default;
            this.IsNewAccount = default;
            this.OnlineInventory = default;
            this.HasReceivedOnlineStarterSupplies = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2A_SavePlayerProfile)]
    [ResponseType(nameof(A2P_SavePlayerProfile))]
    public partial class P2A_SavePlayerProfile : MessageObject, IRequest
    {
        public static P2A_SavePlayerProfile Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2A_SavePlayerProfile), isFromPool) as P2A_SavePlayerProfile;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long AccountId { get; set; }

        [MemoryPackOrder(2)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(3)]
        public string DisplayName { get; set; }

        [MemoryPackOrder(4)]
        public long Revision { get; set; }

        [MemoryPackOrder(5)]
        public string OperationId { get; set; }

        [MemoryPackOrder(6)]
        public long ExpectedRevision { get; set; }

        [MemoryPackOrder(7)]
        public OnlineInventoryInfo OnlineInventory { get; set; }

        [MemoryPackOrder(8)]
        public bool HasReceivedOnlineStarterSupplies { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.AccountId = default;
            this.PlayerId = default;
            this.DisplayName = default;
            this.Revision = default;
            this.OperationId = default;
            this.ExpectedRevision = default;
            this.OnlineInventory = default;
            this.HasReceivedOnlineStarterSupplies = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.A2P_SavePlayerProfile)]
    public partial class A2P_SavePlayerProfile : MessageObject, IResponse
    {
        public static A2P_SavePlayerProfile Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(A2P_SavePlayerProfile), isFromPool) as A2P_SavePlayerProfile;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public long Revision { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Revision = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_GetOrCreatePlayer)]
    [ResponseType(nameof(P2G_GetOrCreatePlayer))]
    public partial class G2P_GetOrCreatePlayer : MessageObject, IRequest
    {
        public static G2P_GetOrCreatePlayer Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_GetOrCreatePlayer), isFromPool) as G2P_GetOrCreatePlayer;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long AccountId { get; set; }

        [MemoryPackOrder(2)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(3)]
        public string DisplayName { get; set; }

        [MemoryPackOrder(4)]
        public ActorId GateProxyActorId { get; set; }

        [MemoryPackOrder(5)]
        public OnlineInventoryInfo OnlineInventory { get; set; }

        [MemoryPackOrder(6)]
        public bool HasReceivedOnlineStarterSupplies { get; set; }

        [MemoryPackOrder(7)]
        public long ProfileRevision { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.AccountId = default;
            this.PlayerId = default;
            this.DisplayName = default;
            this.GateProxyActorId = default;
            this.OnlineInventory = default;
            this.HasReceivedOnlineStarterSupplies = default;
            this.ProfileRevision = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_GetOrCreatePlayer)]
    public partial class P2G_GetOrCreatePlayer : MessageObject, IResponse
    {
        public static P2G_GetOrCreatePlayer Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_GetOrCreatePlayer), isFromPool) as P2G_GetOrCreatePlayer;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public ActorId PlayerActorId { get; set; }

        [MemoryPackOrder(4)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(5)]
        public bool IsPlayerActorReused { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.PlayerActorId = default;
            this.SessionEpoch = default;
            this.IsPlayerActorReused = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_PlayerDisconnected)]
    [ResponseType(nameof(P2G_PlayerDisconnected))]
    public partial class G2P_PlayerDisconnected : MessageObject, IRequest
    {
        public static G2P_PlayerDisconnected Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_PlayerDisconnected), isFromPool) as G2P_PlayerDisconnected;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_PlayerDisconnected)]
    public partial class P2G_PlayerDisconnected : MessageObject, IResponse
    {
        public static P2G_PlayerDisconnected Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_PlayerDisconnected), isFromPool) as P2G_PlayerDisconnected;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_PlayerLogout)]
    [ResponseType(nameof(P2G_PlayerLogout))]
    public partial class G2P_PlayerLogout : MessageObject, IRequest
    {
        public static G2P_PlayerLogout Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_PlayerLogout), isFromPool) as G2P_PlayerLogout;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_PlayerLogout)]
    public partial class P2G_PlayerLogout : MessageObject, IResponse
    {
        public static P2G_PlayerLogout Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_PlayerLogout), isFromPool) as P2G_PlayerLogout;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_SessionReplaced)]
    public partial class P2G_SessionReplaced : MessageObject, IMessage
    {
        public static P2G_SessionReplaced Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_SessionReplaced), isFromPool) as P2G_SessionReplaced;
        }

        [MemoryPackOrder(0)]
        public int Error { get; set; }

        [MemoryPackOrder(1)]
        public string Reason { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.Error = default;
            this.Reason = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_PlayerGraceTimeout)]
    public partial class G2P_PlayerGraceTimeout : MessageObject, IMessage
    {
        public static G2P_PlayerGraceTimeout Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_PlayerGraceTimeout), isFromPool) as G2P_PlayerGraceTimeout;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(2)]
        public long TimerGeneration { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.SessionEpoch = default;
            this.TimerGeneration = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_PlayerRetrySave)]
    public partial class G2P_PlayerRetrySave : MessageObject, IMessage
    {
        public static G2P_PlayerRetrySave Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_PlayerRetrySave), isFromPool) as G2P_PlayerRetrySave;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long SessionEpoch { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.SessionEpoch = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_CreateRoom)]
    [ResponseType(nameof(P2G_CreateRoom))]
    public partial class G2P_CreateRoom : MessageObject, IRequest
    {
        public static G2P_CreateRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_CreateRoom), isFromPool) as G2P_CreateRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(3)]
        public string OperationId { get; set; }

        [MemoryPackOrder(4)]
        public int Purpose { get; set; }

        [MemoryPackOrder(5)]
        public int Visibility { get; set; }

        [MemoryPackOrder(6)]
        public string ClientVersion { get; set; }

        [MemoryPackOrder(7)]
        public string ArcadeConfigFingerprint { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;
            this.OperationId = default;
            this.Purpose = default;
            this.Visibility = default;
            this.ClientVersion = default;
            this.ArcadeConfigFingerprint = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_CreateRoom)]
    public partial class P2G_CreateRoom : MessageObject, IResponse
    {
        public static P2G_CreateRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_CreateRoom), isFromPool) as P2G_CreateRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool HasRoom { get; set; }

        [MemoryPackOrder(4)]
        public OnlineRoomSnapshot Room { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.HasRoom = default;
            this.Room = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_MatchArcadeRoom)]
    [ResponseType(nameof(P2G_MatchArcadeRoom))]
    public partial class G2P_MatchArcadeRoom : MessageObject, IRequest
    {
        public static G2P_MatchArcadeRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_MatchArcadeRoom), isFromPool) as G2P_MatchArcadeRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(3)]
        public string OperationId { get; set; }

        [MemoryPackOrder(4)]
        public string ClientVersion { get; set; }

        [MemoryPackOrder(5)]
        public string ArcadeConfigFingerprint { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;
            this.OperationId = default;
            this.ClientVersion = default;
            this.ArcadeConfigFingerprint = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_MatchArcadeRoom)]
    public partial class P2G_MatchArcadeRoom : MessageObject, IResponse
    {
        public static P2G_MatchArcadeRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_MatchArcadeRoom), isFromPool) as P2G_MatchArcadeRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool HasRoom { get; set; }

        [MemoryPackOrder(4)]
        public OnlineRoomSnapshot Room { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.HasRoom = default;
            this.Room = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_JoinRoom)]
    [ResponseType(nameof(P2G_JoinRoom))]
    public partial class G2P_JoinRoom : MessageObject, IRequest
    {
        public static G2P_JoinRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_JoinRoom), isFromPool) as G2P_JoinRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(3)]
        public string OperationId { get; set; }

        [MemoryPackOrder(4)]
        public string RoomCode { get; set; }

        [MemoryPackOrder(5)]
        public string SourceAddress { get; set; }

        [MemoryPackOrder(6)]
        public int ExpectedPurpose { get; set; }

        [MemoryPackOrder(7)]
        public string ClientVersion { get; set; }

        [MemoryPackOrder(8)]
        public string ArcadeConfigFingerprint { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;
            this.OperationId = default;
            this.RoomCode = default;
            this.SourceAddress = default;
            this.ExpectedPurpose = default;
            this.ClientVersion = default;
            this.ArcadeConfigFingerprint = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_JoinRoom)]
    public partial class P2G_JoinRoom : MessageObject, IResponse
    {
        public static P2G_JoinRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_JoinRoom), isFromPool) as P2G_JoinRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool HasRoom { get; set; }

        [MemoryPackOrder(4)]
        public OnlineRoomSnapshot Room { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.HasRoom = default;
            this.Room = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_LeaveRoom)]
    [ResponseType(nameof(P2G_LeaveRoom))]
    public partial class G2P_LeaveRoom : MessageObject, IRequest
    {
        public static G2P_LeaveRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_LeaveRoom), isFromPool) as G2P_LeaveRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(3)]
        public string OperationId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;
            this.OperationId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_LeaveRoom)]
    public partial class P2G_LeaveRoom : MessageObject, IResponse
    {
        public static P2G_LeaveRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_LeaveRoom), isFromPool) as P2G_LeaveRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_GetCurrentRoom)]
    [ResponseType(nameof(P2G_GetCurrentRoom))]
    public partial class G2P_GetCurrentRoom : MessageObject, IRequest
    {
        public static G2P_GetCurrentRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_GetCurrentRoom), isFromPool) as G2P_GetCurrentRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_GetCurrentRoom)]
    public partial class P2G_GetCurrentRoom : MessageObject, IResponse
    {
        public static P2G_GetCurrentRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_GetCurrentRoom), isFromPool) as P2G_GetCurrentRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool HasRoom { get; set; }

        [MemoryPackOrder(4)]
        public OnlineRoomSnapshot Room { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.HasRoom = default;
            this.Room = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2D_CreateRoom)]
    [ResponseType(nameof(D2P_CreateRoom))]
    public partial class P2D_CreateRoom : MessageObject, IRequest
    {
        public static P2D_CreateRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2D_CreateRoom), isFromPool) as P2D_CreateRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public string DisplayName { get; set; }

        [MemoryPackOrder(3)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(4)]
        public int Purpose { get; set; }

        [MemoryPackOrder(5)]
        public int Visibility { get; set; }

        [MemoryPackOrder(6)]
        public string ClientVersion { get; set; }

        [MemoryPackOrder(7)]
        public string ArcadeConfigFingerprint { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.DisplayName = default;
            this.SessionEpoch = default;
            this.Purpose = default;
            this.Visibility = default;
            this.ClientVersion = default;
            this.ArcadeConfigFingerprint = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.D2P_CreateRoom)]
    public partial class D2P_CreateRoom : MessageObject, IResponse
    {
        public static D2P_CreateRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(D2P_CreateRoom), isFromPool) as D2P_CreateRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public long RoomId { get; set; }

        [MemoryPackOrder(4)]
        public OnlineRoomSnapshot Room { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.RoomId = default;
            this.Room = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2D_MatchArcadeRoom)]
    [ResponseType(nameof(D2P_MatchArcadeRoom))]
    public partial class P2D_MatchArcadeRoom : MessageObject, IRequest
    {
        public static P2D_MatchArcadeRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2D_MatchArcadeRoom), isFromPool) as P2D_MatchArcadeRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public string DisplayName { get; set; }

        [MemoryPackOrder(3)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(4)]
        public string ClientVersion { get; set; }

        [MemoryPackOrder(5)]
        public string ArcadeConfigFingerprint { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.DisplayName = default;
            this.SessionEpoch = default;
            this.ClientVersion = default;
            this.ArcadeConfigFingerprint = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.D2P_MatchArcadeRoom)]
    public partial class D2P_MatchArcadeRoom : MessageObject, IResponse
    {
        public static D2P_MatchArcadeRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(D2P_MatchArcadeRoom), isFromPool) as D2P_MatchArcadeRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public long RoomId { get; set; }

        [MemoryPackOrder(4)]
        public bool Created { get; set; }

        [MemoryPackOrder(5)]
        public OnlineRoomSnapshot Room { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.RoomId = default;
            this.Created = default;
            this.Room = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2D_ResolveRoom)]
    [ResponseType(nameof(D2P_ResolveRoom))]
    public partial class P2D_ResolveRoom : MessageObject, IRequest
    {
        public static P2D_ResolveRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2D_ResolveRoom), isFromPool) as P2D_ResolveRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public string RoomCode { get; set; }

        [MemoryPackOrder(3)]
        public string SourceAddress { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.RoomCode = default;
            this.SourceAddress = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.D2P_ResolveRoom)]
    public partial class D2P_ResolveRoom : MessageObject, IResponse
    {
        public static D2P_ResolveRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(D2P_ResolveRoom), isFromPool) as D2P_ResolveRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public long RoomId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.RoomId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.D2R_CreateRoom)]
    [ResponseType(nameof(R2D_CreateRoom))]
    public partial class D2R_CreateRoom : MessageObject, IRequest
    {
        public static D2R_CreateRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(D2R_CreateRoom), isFromPool) as D2R_CreateRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long RoomId { get; set; }

        [MemoryPackOrder(2)]
        public string RoomCode { get; set; }

        [MemoryPackOrder(3)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(4)]
        public string DisplayName { get; set; }

        [MemoryPackOrder(5)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(6)]
        public int Purpose { get; set; }

        [MemoryPackOrder(7)]
        public int Visibility { get; set; }

        [MemoryPackOrder(8)]
        public string ClientVersion { get; set; }

        [MemoryPackOrder(9)]
        public string ArcadeConfigFingerprint { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.RoomId = default;
            this.RoomCode = default;
            this.PlayerId = default;
            this.DisplayName = default;
            this.SessionEpoch = default;
            this.Purpose = default;
            this.Visibility = default;
            this.ClientVersion = default;
            this.ArcadeConfigFingerprint = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2D_CreateRoom)]
    public partial class R2D_CreateRoom : MessageObject, IResponse
    {
        public static R2D_CreateRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2D_CreateRoom), isFromPool) as R2D_CreateRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public OnlineRoomSnapshot Room { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Room = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2D_CloseRoom)]
    [ResponseType(nameof(D2R_CloseRoom))]
    public partial class R2D_CloseRoom : MessageObject, IRequest
    {
        public static R2D_CloseRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2D_CloseRoom), isFromPool) as R2D_CloseRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long RoomId { get; set; }

        [MemoryPackOrder(2)]
        public string RoomCode { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.RoomId = default;
            this.RoomCode = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.D2R_CloseRoom)]
    public partial class D2R_CloseRoom : MessageObject, IResponse
    {
        public static D2R_CloseRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(D2R_CloseRoom), isFromPool) as D2R_CloseRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2R_JoinRoom)]
    [ResponseType(nameof(R2P_JoinRoom))]
    public partial class P2R_JoinRoom : MessageObject, IRequest
    {
        public static P2R_JoinRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2R_JoinRoom), isFromPool) as P2R_JoinRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public string DisplayName { get; set; }

        [MemoryPackOrder(3)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(4)]
        public int ExpectedPurpose { get; set; }

        [MemoryPackOrder(5)]
        public string ClientVersion { get; set; }

        [MemoryPackOrder(6)]
        public string ArcadeConfigFingerprint { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.DisplayName = default;
            this.SessionEpoch = default;
            this.ExpectedPurpose = default;
            this.ClientVersion = default;
            this.ArcadeConfigFingerprint = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2P_JoinRoom)]
    public partial class R2P_JoinRoom : MessageObject, IResponse
    {
        public static R2P_JoinRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2P_JoinRoom), isFromPool) as R2P_JoinRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public OnlineRoomSnapshot Room { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Room = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2R_LeaveRoom)]
    [ResponseType(nameof(R2P_LeaveRoom))]
    public partial class P2R_LeaveRoom : MessageObject, IRequest
    {
        public static P2R_LeaveRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2R_LeaveRoom), isFromPool) as P2R_LeaveRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public string OperationId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.OperationId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2P_LeaveRoom)]
    public partial class R2P_LeaveRoom : MessageObject, IResponse
    {
        public static R2P_LeaveRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2P_LeaveRoom), isFromPool) as R2P_LeaveRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2R_GetRoom)]
    [ResponseType(nameof(R2P_GetRoom))]
    public partial class P2R_GetRoom : MessageObject, IRequest
    {
        public static P2R_GetRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2R_GetRoom), isFromPool) as P2R_GetRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2P_GetRoom)]
    public partial class R2P_GetRoom : MessageObject, IResponse
    {
        public static R2P_GetRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2P_GetRoom), isFromPool) as R2P_GetRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public OnlineRoomSnapshot Room { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Room = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2R_SetMemberConnection)]
    [ResponseType(nameof(R2P_SetMemberConnection))]
    public partial class P2R_SetMemberConnection : MessageObject, IRequest
    {
        public static P2R_SetMemberConnection Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2R_SetMemberConnection), isFromPool) as P2R_SetMemberConnection;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(3)]
        public int ConnectionState { get; set; }

        [MemoryPackOrder(4)]
        public string OperationId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;
            this.ConnectionState = default;
            this.OperationId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2P_SetMemberConnection)]
    public partial class R2P_SetMemberConnection : MessageObject, IResponse
    {
        public static R2P_SetMemberConnection Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2P_SetMemberConnection), isFromPool) as R2P_SetMemberConnection;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2P_RoomChanged)]
    public partial class R2P_RoomChanged : MessageObject, IMessage
    {
        public static R2P_RoomChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2P_RoomChanged), isFromPool) as R2P_RoomChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public bool HasRoom { get; set; }

        [MemoryPackOrder(2)]
        public OnlineRoomSnapshot Room { get; set; }

        [MemoryPackOrder(3)]
        public long RoomId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.HasRoom = default;
            this.Room = default;
            this.RoomId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_RoomChanged)]
    public partial class P2G_RoomChanged : MessageObject, IMessage
    {
        public static P2G_RoomChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_RoomChanged), isFromPool) as P2G_RoomChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(2)]
        public bool HasRoom { get; set; }

        [MemoryPackOrder(3)]
        public OnlineRoomSnapshot Room { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.SessionEpoch = default;
            this.HasRoom = default;
            this.Room = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2R_RetryClose)]
    public partial class R2R_RetryClose : MessageObject, IMessage
    {
        public static R2R_RetryClose Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2R_RetryClose), isFromPool) as R2R_RetryClose;
        }

        [MemoryPackOrder(0)]
        public long RoomId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RoomId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2P_RetryRoomSync)]
    public partial class P2P_RetryRoomSync : MessageObject, IMessage
    {
        public static P2P_RetryRoomSync Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2P_RetryRoomSync), isFromPool) as P2P_RetryRoomSync;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(2)]
        public long SyncGeneration { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.SessionEpoch = default;
            this.SyncGeneration = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.RoomChatMemberInfo)]
    public partial class RoomChatMemberInfo : MessageObject
    {
        public static RoomChatMemberInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(RoomChatMemberInfo), isFromPool) as RoomChatMemberInfo;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public string DisplayName { get; set; }

        [MemoryPackOrder(2)]
        public int ConnectionState { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.DisplayName = default;
            this.ConnectionState = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2C_SyncRoomChatMembers)]
    [ResponseType(nameof(C2R_SyncRoomChatMembers))]
    public partial class R2C_SyncRoomChatMembers : MessageObject, IRequest
    {
        public static R2C_SyncRoomChatMembers Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2C_SyncRoomChatMembers), isFromPool) as R2C_SyncRoomChatMembers;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long RoomId { get; set; }

        [MemoryPackOrder(2)]
        public long MembershipRevision { get; set; }

        [MemoryPackOrder(3)]
        public List<RoomChatMemberInfo> Members { get; set; } = new();

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.RoomId = default;
            this.MembershipRevision = default;
            this.Members.Clear();

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.C2R_SyncRoomChatMembers)]
    public partial class C2R_SyncRoomChatMembers : MessageObject, IResponse
    {
        public static C2R_SyncRoomChatMembers Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2R_SyncRoomChatMembers), isFromPool) as C2R_SyncRoomChatMembers;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public long ConfirmedRevision { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.ConfirmedRevision = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2C_CloseRoomChat)]
    [ResponseType(nameof(C2R_CloseRoomChat))]
    public partial class R2C_CloseRoomChat : MessageObject, IRequest
    {
        public static R2C_CloseRoomChat Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2C_CloseRoomChat), isFromPool) as R2C_CloseRoomChat;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long RoomId { get; set; }

        [MemoryPackOrder(2)]
        public long MembershipRevision { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.RoomId = default;
            this.MembershipRevision = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.C2R_CloseRoomChat)]
    public partial class C2R_CloseRoomChat : MessageObject, IResponse
    {
        public static C2R_CloseRoomChat Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2R_CloseRoomChat), isFromPool) as C2R_CloseRoomChat;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2C_SendRoomChat)]
    [ResponseType(nameof(C2P_SendRoomChat))]
    public partial class P2C_SendRoomChat : MessageObject, IRequest
    {
        public static P2C_SendRoomChat Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2C_SendRoomChat), isFromPool) as P2C_SendRoomChat;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long RoomId { get; set; }

        [MemoryPackOrder(2)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(3)]
        public string ClientMessageId { get; set; }

        [MemoryPackOrder(4)]
        public string Content { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.RoomId = default;
            this.PlayerId = default;
            this.ClientMessageId = default;
            this.Content = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.C2P_SendRoomChat)]
    public partial class C2P_SendRoomChat : MessageObject, IResponse
    {
        public static C2P_SendRoomChat Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2P_SendRoomChat), isFromPool) as C2P_SendRoomChat;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public long RoomId { get; set; }

        [MemoryPackOrder(4)]
        public bool Accepted { get; set; }

        [MemoryPackOrder(5)]
        public RoomChatMessageInfo ChatMessage { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.RoomId = default;
            this.Accepted = default;
            this.ChatMessage = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2C_GetRoomChatHistory)]
    [ResponseType(nameof(C2P_GetRoomChatHistory))]
    public partial class P2C_GetRoomChatHistory : MessageObject, IRequest
    {
        public static P2C_GetRoomChatHistory Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2C_GetRoomChatHistory), isFromPool) as P2C_GetRoomChatHistory;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long RoomId { get; set; }

        [MemoryPackOrder(2)]
        public long PlayerId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.RoomId = default;
            this.PlayerId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.C2P_GetRoomChatHistory)]
    public partial class C2P_GetRoomChatHistory : MessageObject, IResponse
    {
        public static C2P_GetRoomChatHistory Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2P_GetRoomChatHistory), isFromPool) as C2P_GetRoomChatHistory;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public long RoomId { get; set; }

        [MemoryPackOrder(4)]
        public long MaxChatSequence { get; set; }

        [MemoryPackOrder(5)]
        public List<RoomChatMessageInfo> Messages { get; set; } = new();

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.RoomId = default;
            this.MaxChatSequence = default;
            this.Messages.Clear();

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.C2P_RoomChatMessage)]
    public partial class C2P_RoomChatMessage : MessageObject, IMessage
    {
        public static C2P_RoomChatMessage Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2P_RoomChatMessage), isFromPool) as C2P_RoomChatMessage;
        }

        [MemoryPackOrder(0)]
        public long RoomId { get; set; }

        [MemoryPackOrder(1)]
        public RoomChatMessageInfo ChatMessage { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RoomId = default;
            this.ChatMessage = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_SendRoomChat)]
    [ResponseType(nameof(P2G_SendRoomChat))]
    public partial class G2P_SendRoomChat : MessageObject, IRequest
    {
        public static G2P_SendRoomChat Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_SendRoomChat), isFromPool) as G2P_SendRoomChat;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(3)]
        public string ClientMessageId { get; set; }

        [MemoryPackOrder(4)]
        public string Content { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;
            this.ClientMessageId = default;
            this.Content = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_SendRoomChat)]
    public partial class P2G_SendRoomChat : MessageObject, IResponse
    {
        public static P2G_SendRoomChat Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_SendRoomChat), isFromPool) as P2G_SendRoomChat;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public long RoomId { get; set; }

        [MemoryPackOrder(4)]
        public bool Accepted { get; set; }

        [MemoryPackOrder(5)]
        public RoomChatMessageInfo ChatMessage { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.RoomId = default;
            this.Accepted = default;
            this.ChatMessage = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_GetRoomChatHistory)]
    [ResponseType(nameof(P2G_GetRoomChatHistory))]
    public partial class G2P_GetRoomChatHistory : MessageObject, IRequest
    {
        public static G2P_GetRoomChatHistory Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_GetRoomChatHistory), isFromPool) as G2P_GetRoomChatHistory;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_GetRoomChatHistory)]
    public partial class P2G_GetRoomChatHistory : MessageObject, IResponse
    {
        public static P2G_GetRoomChatHistory Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_GetRoomChatHistory), isFromPool) as P2G_GetRoomChatHistory;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public long RoomId { get; set; }

        [MemoryPackOrder(4)]
        public long MaxChatSequence { get; set; }

        [MemoryPackOrder(5)]
        public List<RoomChatMessageInfo> Messages { get; set; } = new();

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.RoomId = default;
            this.MaxChatSequence = default;
            this.Messages.Clear();

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_RoomChatMessage)]
    public partial class P2G_RoomChatMessage : MessageObject, IMessage
    {
        public static P2G_RoomChatMessage Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_RoomChatMessage), isFromPool) as P2G_RoomChatMessage;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(2)]
        public long RoomId { get; set; }

        [MemoryPackOrder(3)]
        public RoomChatMessageInfo ChatMessage { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.SessionEpoch = default;
            this.RoomId = default;
            this.ChatMessage = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2R_EnsureRoomChatReady)]
    [ResponseType(nameof(R2P_EnsureRoomChatReady))]
    public partial class P2R_EnsureRoomChatReady : MessageObject, IRequest
    {
        public static P2R_EnsureRoomChatReady Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2R_EnsureRoomChatReady), isFromPool) as P2R_EnsureRoomChatReady;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2P_EnsureRoomChatReady)]
    public partial class R2P_EnsureRoomChatReady : MessageObject, IResponse
    {
        public static R2P_EnsureRoomChatReady Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2P_EnsureRoomChatReady), isFromPool) as R2P_EnsureRoomChatReady;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public OnlineRoomSnapshot Room { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Room = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2R_RetryChatSync)]
    public partial class R2R_RetryChatSync : MessageObject, IMessage
    {
        public static R2R_RetryChatSync Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2R_RetryChatSync), isFromPool) as R2R_RetryChatSync;
        }

        [MemoryPackOrder(0)]
        public long RoomId { get; set; }

        [MemoryPackOrder(1)]
        public long SyncGeneration { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RoomId = default;
            this.SyncGeneration = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_EnterWorld)]
    [ResponseType(nameof(P2G_EnterWorld))]
    public partial class G2P_EnterWorld : MessageObject, IRequest
    {
        public static G2P_EnterWorld Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_EnterWorld), isFromPool) as G2P_EnterWorld;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(3)]
        public string OperationId { get; set; }

        [MemoryPackOrder(4)]
        public string MapId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;
            this.OperationId = default;
            this.MapId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_EnterWorld)]
    public partial class P2G_EnterWorld : MessageObject, IResponse
    {
        public static P2G_EnterWorld Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_EnterWorld), isFromPool) as P2G_EnterWorld;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool HasWorld { get; set; }

        [MemoryPackOrder(4)]
        public OnlineWorldSnapshot World { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.HasWorld = default;
            this.World = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_GetCurrentWorld)]
    [ResponseType(nameof(P2G_GetCurrentWorld))]
    public partial class G2P_GetCurrentWorld : MessageObject, IRequest
    {
        public static G2P_GetCurrentWorld Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_GetCurrentWorld), isFromPool) as G2P_GetCurrentWorld;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_GetCurrentWorld)]
    public partial class P2G_GetCurrentWorld : MessageObject, IResponse
    {
        public static P2G_GetCurrentWorld Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_GetCurrentWorld), isFromPool) as P2G_GetCurrentWorld;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool HasWorld { get; set; }

        [MemoryPackOrder(4)]
        public OnlineWorldSnapshot World { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.HasWorld = default;
            this.World = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_LeaveWorld)]
    [ResponseType(nameof(P2G_LeaveWorld))]
    public partial class G2P_LeaveWorld : MessageObject, IRequest
    {
        public static G2P_LeaveWorld Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_LeaveWorld), isFromPool) as G2P_LeaveWorld;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(3)]
        public string OperationId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;
            this.OperationId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_LeaveWorld)]
    public partial class P2G_LeaveWorld : MessageObject, IResponse
    {
        public static P2G_LeaveWorld Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_LeaveWorld), isFromPool) as P2G_LeaveWorld;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_ReportWorldTransform)]
    [ResponseType(nameof(P2G_ReportWorldTransform))]
    public partial class G2P_ReportWorldTransform : MessageObject, IRequest
    {
        public static G2P_ReportWorldTransform Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_ReportWorldTransform), isFromPool) as G2P_ReportWorldTransform;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(3)]
        public float PositionX { get; set; }

        [MemoryPackOrder(4)]
        public float PositionY { get; set; }

        [MemoryPackOrder(5)]
        public float PositionZ { get; set; }

        [MemoryPackOrder(6)]
        public float YawDegrees { get; set; }

        [MemoryPackOrder(7)]
        public long LifecycleRevision { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;
            this.PositionX = default;
            this.PositionY = default;
            this.PositionZ = default;
            this.YawDegrees = default;
            this.LifecycleRevision = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_ReportWorldTransform)]
    public partial class P2G_ReportWorldTransform : MessageObject, IResponse
    {
        public static P2G_ReportWorldTransform Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_ReportWorldTransform), isFromPool) as P2G_ReportWorldTransform;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public long WorldId { get; set; }

        [MemoryPackOrder(4)]
        public string MapId { get; set; }

        [MemoryPackOrder(5)]
        public long UnitId { get; set; }

        [MemoryPackOrder(6)]
        public long StateRevision { get; set; }

        [MemoryPackOrder(7)]
        public long LifecycleRevision { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.WorldId = default;
            this.MapId = default;
            this.UnitId = default;
            this.StateRevision = default;
            this.LifecycleRevision = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2W_EnterWorld)]
    [ResponseType(nameof(W2P_EnterWorld))]
    public partial class P2W_EnterWorld : MessageObject, IRequest
    {
        public static P2W_EnterWorld Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2W_EnterWorld), isFromPool) as P2W_EnterWorld;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public string DisplayName { get; set; }

        [MemoryPackOrder(3)]
        public ActorId PlayerActorId { get; set; }

        [MemoryPackOrder(4)]
        public string MapId { get; set; }

        [MemoryPackOrder(5)]
        public OnlineInventoryInfo OnlineInventory { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.DisplayName = default;
            this.PlayerActorId = default;
            this.MapId = default;
            this.OnlineInventory = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_EnterWorld)]
    public partial class W2P_EnterWorld : MessageObject, IResponse
    {
        public static W2P_EnterWorld Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_EnterWorld), isFromPool) as W2P_EnterWorld;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public OnlineWorldSnapshot World { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.World = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2W_GetCurrentWorld)]
    [ResponseType(nameof(W2P_GetCurrentWorld))]
    public partial class P2W_GetCurrentWorld : MessageObject, IRequest
    {
        public static P2W_GetCurrentWorld Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2W_GetCurrentWorld), isFromPool) as P2W_GetCurrentWorld;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public long UnitId { get; set; }

        [MemoryPackOrder(4)]
        public string MapId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.WorldId = default;
            this.UnitId = default;
            this.MapId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_GetCurrentWorld)]
    public partial class W2P_GetCurrentWorld : MessageObject, IResponse
    {
        public static W2P_GetCurrentWorld Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_GetCurrentWorld), isFromPool) as W2P_GetCurrentWorld;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool HasWorld { get; set; }

        [MemoryPackOrder(4)]
        public OnlineWorldSnapshot World { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.HasWorld = default;
            this.World = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2W_LeaveWorld)]
    [ResponseType(nameof(W2P_LeaveWorld))]
    public partial class P2W_LeaveWorld : MessageObject, IRequest
    {
        public static P2W_LeaveWorld Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2W_LeaveWorld), isFromPool) as P2W_LeaveWorld;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public long UnitId { get; set; }

        [MemoryPackOrder(4)]
        public string MapId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.WorldId = default;
            this.UnitId = default;
            this.MapId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_LeaveWorld)]
    public partial class W2P_LeaveWorld : MessageObject, IResponse
    {
        public static W2P_LeaveWorld Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_LeaveWorld), isFromPool) as W2P_LeaveWorld;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2W_PrepareWorldLeave)]
    [ResponseType(nameof(W2P_PrepareWorldLeave))]
    public partial class P2W_PrepareWorldLeave : MessageObject, IRequest
    {
        public static P2W_PrepareWorldLeave Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2W_PrepareWorldLeave), isFromPool) as P2W_PrepareWorldLeave;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public string MapId { get; set; }

        [MemoryPackOrder(4)]
        public long UnitId { get; set; }

        [MemoryPackOrder(5)]
        public string OperationId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.WorldId = default;
            this.MapId = default;
            this.UnitId = default;
            this.OperationId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_PrepareWorldLeave)]
    public partial class W2P_PrepareWorldLeave : MessageObject, IResponse
    {
        public static W2P_PrepareWorldLeave Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_PrepareWorldLeave), isFromPool) as W2P_PrepareWorldLeave;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool Prepared { get; set; }

        [MemoryPackOrder(4)]
        public OnlineInventoryInfo OnlineInventory { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Prepared = default;
            this.OnlineInventory = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2W_CommitWorldLeave)]
    [ResponseType(nameof(W2P_CommitWorldLeave))]
    public partial class P2W_CommitWorldLeave : MessageObject, IRequest
    {
        public static P2W_CommitWorldLeave Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2W_CommitWorldLeave), isFromPool) as P2W_CommitWorldLeave;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public string MapId { get; set; }

        [MemoryPackOrder(4)]
        public string OperationId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.WorldId = default;
            this.MapId = default;
            this.OperationId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_CommitWorldLeave)]
    public partial class W2P_CommitWorldLeave : MessageObject, IResponse
    {
        public static W2P_CommitWorldLeave Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_CommitWorldLeave), isFromPool) as W2P_CommitWorldLeave;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool Completed { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Completed = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2W_AbortWorldLeave)]
    [ResponseType(nameof(W2P_AbortWorldLeave))]
    public partial class P2W_AbortWorldLeave : MessageObject, IRequest
    {
        public static P2W_AbortWorldLeave Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2W_AbortWorldLeave), isFromPool) as P2W_AbortWorldLeave;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public string MapId { get; set; }

        [MemoryPackOrder(4)]
        public string OperationId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.WorldId = default;
            this.MapId = default;
            this.OperationId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_AbortWorldLeave)]
    public partial class W2P_AbortWorldLeave : MessageObject, IResponse
    {
        public static W2P_AbortWorldLeave Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_AbortWorldLeave), isFromPool) as W2P_AbortWorldLeave;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool Completed { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Completed = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_OpenWorldCorpse)]
    [ResponseType(nameof(P2G_OpenWorldCorpse))]
    public partial class G2P_OpenWorldCorpse : MessageObject, IRequest
    {
        public static G2P_OpenWorldCorpse Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_OpenWorldCorpse), isFromPool) as G2P_OpenWorldCorpse;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(3)]
        public long CorpseId { get; set; }

        [MemoryPackOrder(4)]
        public long LifecycleRevision { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;
            this.CorpseId = default;
            this.LifecycleRevision = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_OpenWorldCorpse)]
    public partial class P2G_OpenWorldCorpse : MessageObject, IResponse
    {
        public static P2G_OpenWorldCorpse Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_OpenWorldCorpse), isFromPool) as P2G_OpenWorldCorpse;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool HasCorpse { get; set; }

        [MemoryPackOrder(4)]
        public WorldCorpseContentsInfo CorpseContents { get; set; }

        [MemoryPackOrder(5)]
        public OnlineInventoryInfo SelfInventory { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.HasCorpse = default;
            this.CorpseContents = default;
            this.SelfInventory = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2W_OpenWorldCorpse)]
    [ResponseType(nameof(W2P_OpenWorldCorpse))]
    public partial class P2W_OpenWorldCorpse : MessageObject, IRequest
    {
        public static P2W_OpenWorldCorpse Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2W_OpenWorldCorpse), isFromPool) as P2W_OpenWorldCorpse;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public string MapId { get; set; }

        [MemoryPackOrder(4)]
        public long UnitId { get; set; }

        [MemoryPackOrder(5)]
        public long CorpseId { get; set; }

        [MemoryPackOrder(6)]
        public long LifecycleRevision { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.WorldId = default;
            this.MapId = default;
            this.UnitId = default;
            this.CorpseId = default;
            this.LifecycleRevision = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_OpenWorldCorpse)]
    public partial class W2P_OpenWorldCorpse : MessageObject, IResponse
    {
        public static W2P_OpenWorldCorpse Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_OpenWorldCorpse), isFromPool) as W2P_OpenWorldCorpse;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool HasCorpse { get; set; }

        [MemoryPackOrder(4)]
        public WorldCorpseContentsInfo CorpseContents { get; set; }

        [MemoryPackOrder(5)]
        public OnlineInventoryInfo SelfInventory { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.HasCorpse = default;
            this.CorpseContents = default;
            this.SelfInventory = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_OperateWorldInventory)]
    [ResponseType(nameof(P2G_OperateWorldInventory))]
    public partial class G2P_OperateWorldInventory : MessageObject, IRequest
    {
        public static G2P_OperateWorldInventory Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_OperateWorldInventory), isFromPool) as G2P_OperateWorldInventory;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(3)]
        public string OperationId { get; set; }

        [MemoryPackOrder(4)]
        public long ExpectedInventoryRevision { get; set; }

        [MemoryPackOrder(5)]
        public long CorpseId { get; set; }

        [MemoryPackOrder(6)]
        public long ExpectedCorpseRevision { get; set; }

        [MemoryPackOrder(7)]
        public int ActionKind { get; set; }

        [MemoryPackOrder(8)]
        public int SourceContainerKind { get; set; }

        [MemoryPackOrder(9)]
        public int SourceSlotIndex { get; set; }

        [MemoryPackOrder(10)]
        public int TargetContainerKind { get; set; }

        [MemoryPackOrder(11)]
        public int TargetSlotIndex { get; set; }

        [MemoryPackOrder(12)]
        public long LifecycleRevision { get; set; }

        [MemoryPackOrder(13)]
        public int InventorySchemaVersion { get; set; }

        [MemoryPackOrder(14)]
        public int ExpectedItemId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;
            this.OperationId = default;
            this.ExpectedInventoryRevision = default;
            this.CorpseId = default;
            this.ExpectedCorpseRevision = default;
            this.ActionKind = default;
            this.SourceContainerKind = default;
            this.SourceSlotIndex = default;
            this.TargetContainerKind = default;
            this.TargetSlotIndex = default;
            this.LifecycleRevision = default;
            this.InventorySchemaVersion = default;
            this.ExpectedItemId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_OperateWorldInventory)]
    public partial class P2G_OperateWorldInventory : MessageObject, IResponse
    {
        public static P2G_OperateWorldInventory Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_OperateWorldInventory), isFromPool) as P2G_OperateWorldInventory;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool Accepted { get; set; }

        [MemoryPackOrder(4)]
        public OnlineInventoryInfo SelfInventory { get; set; }

        [MemoryPackOrder(5)]
        public bool HasCorpse { get; set; }

        [MemoryPackOrder(6)]
        public WorldCorpseContentsInfo CorpseContents { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Accepted = default;
            this.SelfInventory = default;
            this.HasCorpse = default;
            this.CorpseContents = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_OperateWorldTemporaryBuilding)]
    [ResponseType(nameof(P2G_OperateWorldTemporaryBuilding))]
    public partial class G2P_OperateWorldTemporaryBuilding : MessageObject, IRequest
    {
        public static G2P_OperateWorldTemporaryBuilding Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_OperateWorldTemporaryBuilding), isFromPool) as G2P_OperateWorldTemporaryBuilding;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(3)]
        public string OperationId { get; set; }

        [MemoryPackOrder(4)]
        public int ActionKind { get; set; }

        [MemoryPackOrder(5)]
        public int RuntimeId { get; set; }

        [MemoryPackOrder(6)]
        public string ConfigId { get; set; }

        [MemoryPackOrder(7)]
        public float PositionX { get; set; }

        [MemoryPackOrder(8)]
        public float PositionY { get; set; }

        [MemoryPackOrder(9)]
        public float PositionZ { get; set; }

        [MemoryPackOrder(10)]
        public float YawDegrees { get; set; }

        [MemoryPackOrder(11)]
        public float AimX { get; set; }

        [MemoryPackOrder(12)]
        public float AimZ { get; set; }

        [MemoryPackOrder(13)]
        public long ExpectedInventoryRevision { get; set; }

        [MemoryPackOrder(14)]
        public long ExpectedBuildingRevision { get; set; }

        [MemoryPackOrder(15)]
        public long LifecycleRevision { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;
            this.OperationId = default;
            this.ActionKind = default;
            this.RuntimeId = default;
            this.ConfigId = default;
            this.PositionX = default;
            this.PositionY = default;
            this.PositionZ = default;
            this.YawDegrees = default;
            this.AimX = default;
            this.AimZ = default;
            this.ExpectedInventoryRevision = default;
            this.ExpectedBuildingRevision = default;
            this.LifecycleRevision = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_OperateWorldTemporaryBuilding)]
    public partial class P2G_OperateWorldTemporaryBuilding : MessageObject, IResponse
    {
        public static P2G_OperateWorldTemporaryBuilding Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_OperateWorldTemporaryBuilding), isFromPool) as P2G_OperateWorldTemporaryBuilding;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool Accepted { get; set; }

        [MemoryPackOrder(4)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(5)]
        public bool HasBuilding { get; set; }

        [MemoryPackOrder(6)]
        public WorldTemporaryBuildingInfo Building { get; set; }

        [MemoryPackOrder(7)]
        public int RemovedRuntimeId { get; set; }

        [MemoryPackOrder(8)]
        public OnlineInventoryInfo SelfInventory { get; set; }

        [MemoryPackOrder(9)]
        public long ServerTime { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Accepted = default;
            this.WorldRevision = default;
            this.HasBuilding = default;
            this.Building = default;
            this.RemovedRuntimeId = default;
            this.SelfInventory = default;
            this.ServerTime = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2W_OperateWorldInventory)]
    [ResponseType(nameof(W2P_OperateWorldInventory))]
    public partial class P2W_OperateWorldInventory : MessageObject, IRequest
    {
        public static P2W_OperateWorldInventory Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2W_OperateWorldInventory), isFromPool) as P2W_OperateWorldInventory;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public string MapId { get; set; }

        [MemoryPackOrder(4)]
        public long UnitId { get; set; }

        [MemoryPackOrder(5)]
        public string OperationId { get; set; }

        [MemoryPackOrder(6)]
        public long ExpectedInventoryRevision { get; set; }

        [MemoryPackOrder(7)]
        public long CorpseId { get; set; }

        [MemoryPackOrder(8)]
        public long ExpectedCorpseRevision { get; set; }

        [MemoryPackOrder(9)]
        public int ActionKind { get; set; }

        [MemoryPackOrder(10)]
        public int SourceContainerKind { get; set; }

        [MemoryPackOrder(11)]
        public int SourceSlotIndex { get; set; }

        [MemoryPackOrder(12)]
        public int TargetContainerKind { get; set; }

        [MemoryPackOrder(13)]
        public int TargetSlotIndex { get; set; }

        [MemoryPackOrder(14)]
        public long LifecycleRevision { get; set; }

        [MemoryPackOrder(15)]
        public int InventorySchemaVersion { get; set; }

        [MemoryPackOrder(16)]
        public int ExpectedItemId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.WorldId = default;
            this.MapId = default;
            this.UnitId = default;
            this.OperationId = default;
            this.ExpectedInventoryRevision = default;
            this.CorpseId = default;
            this.ExpectedCorpseRevision = default;
            this.ActionKind = default;
            this.SourceContainerKind = default;
            this.SourceSlotIndex = default;
            this.TargetContainerKind = default;
            this.TargetSlotIndex = default;
            this.LifecycleRevision = default;
            this.InventorySchemaVersion = default;
            this.ExpectedItemId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_OperateWorldInventory)]
    public partial class W2P_OperateWorldInventory : MessageObject, IResponse
    {
        public static W2P_OperateWorldInventory Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_OperateWorldInventory), isFromPool) as W2P_OperateWorldInventory;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool Accepted { get; set; }

        [MemoryPackOrder(4)]
        public OnlineInventoryInfo SelfInventory { get; set; }

        [MemoryPackOrder(5)]
        public bool HasCorpse { get; set; }

        [MemoryPackOrder(6)]
        public WorldCorpseContentsInfo CorpseContents { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Accepted = default;
            this.SelfInventory = default;
            this.HasCorpse = default;
            this.CorpseContents = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2W_OperateWorldTemporaryBuilding)]
    [ResponseType(nameof(W2P_OperateWorldTemporaryBuilding))]
    public partial class P2W_OperateWorldTemporaryBuilding : MessageObject, IRequest
    {
        public static P2W_OperateWorldTemporaryBuilding Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2W_OperateWorldTemporaryBuilding), isFromPool) as P2W_OperateWorldTemporaryBuilding;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public string MapId { get; set; }

        [MemoryPackOrder(4)]
        public long UnitId { get; set; }

        [MemoryPackOrder(5)]
        public string OperationId { get; set; }

        [MemoryPackOrder(6)]
        public int ActionKind { get; set; }

        [MemoryPackOrder(7)]
        public int RuntimeId { get; set; }

        [MemoryPackOrder(8)]
        public string ConfigId { get; set; }

        [MemoryPackOrder(9)]
        public float PositionX { get; set; }

        [MemoryPackOrder(10)]
        public float PositionY { get; set; }

        [MemoryPackOrder(11)]
        public float PositionZ { get; set; }

        [MemoryPackOrder(12)]
        public float YawDegrees { get; set; }

        [MemoryPackOrder(13)]
        public float AimX { get; set; }

        [MemoryPackOrder(14)]
        public float AimZ { get; set; }

        [MemoryPackOrder(15)]
        public long ExpectedInventoryRevision { get; set; }

        [MemoryPackOrder(16)]
        public long ExpectedBuildingRevision { get; set; }

        [MemoryPackOrder(17)]
        public long LifecycleRevision { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.WorldId = default;
            this.MapId = default;
            this.UnitId = default;
            this.OperationId = default;
            this.ActionKind = default;
            this.RuntimeId = default;
            this.ConfigId = default;
            this.PositionX = default;
            this.PositionY = default;
            this.PositionZ = default;
            this.YawDegrees = default;
            this.AimX = default;
            this.AimZ = default;
            this.ExpectedInventoryRevision = default;
            this.ExpectedBuildingRevision = default;
            this.LifecycleRevision = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_OperateWorldTemporaryBuilding)]
    public partial class W2P_OperateWorldTemporaryBuilding : MessageObject, IResponse
    {
        public static W2P_OperateWorldTemporaryBuilding Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_OperateWorldTemporaryBuilding), isFromPool) as W2P_OperateWorldTemporaryBuilding;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool Accepted { get; set; }

        [MemoryPackOrder(4)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(5)]
        public bool HasBuilding { get; set; }

        [MemoryPackOrder(6)]
        public WorldTemporaryBuildingInfo Building { get; set; }

        [MemoryPackOrder(7)]
        public int RemovedRuntimeId { get; set; }

        [MemoryPackOrder(8)]
        public OnlineInventoryInfo SelfInventory { get; set; }

        [MemoryPackOrder(9)]
        public long ServerTime { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Accepted = default;
            this.WorldRevision = default;
            this.HasBuilding = default;
            this.Building = default;
            this.RemovedRuntimeId = default;
            this.SelfInventory = default;
            this.ServerTime = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2W_ReportWorldTransform)]
    [ResponseType(nameof(W2P_ReportWorldTransform))]
    public partial class P2W_ReportWorldTransform : MessageObject, IRequest
    {
        public static P2W_ReportWorldTransform Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2W_ReportWorldTransform), isFromPool) as P2W_ReportWorldTransform;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public long UnitId { get; set; }

        [MemoryPackOrder(4)]
        public float PositionX { get; set; }

        [MemoryPackOrder(5)]
        public float PositionY { get; set; }

        [MemoryPackOrder(6)]
        public float PositionZ { get; set; }

        [MemoryPackOrder(7)]
        public float YawDegrees { get; set; }

        [MemoryPackOrder(8)]
        public long LifecycleRevision { get; set; }

        [MemoryPackOrder(9)]
        public string MapId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.WorldId = default;
            this.UnitId = default;
            this.PositionX = default;
            this.PositionY = default;
            this.PositionZ = default;
            this.YawDegrees = default;
            this.LifecycleRevision = default;
            this.MapId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_ReportWorldTransform)]
    public partial class W2P_ReportWorldTransform : MessageObject, IResponse
    {
        public static W2P_ReportWorldTransform Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_ReportWorldTransform), isFromPool) as W2P_ReportWorldTransform;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public long WorldId { get; set; }

        [MemoryPackOrder(4)]
        public string MapId { get; set; }

        [MemoryPackOrder(5)]
        public long UnitId { get; set; }

        [MemoryPackOrder(6)]
        public long StateRevision { get; set; }

        [MemoryPackOrder(7)]
        public long LifecycleRevision { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.WorldId = default;
            this.MapId = default;
            this.UnitId = default;
            this.StateRevision = default;
            this.LifecycleRevision = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_WorldMeleeAttack)]
    [ResponseType(nameof(P2G_WorldMeleeAttack))]
    public partial class G2P_WorldMeleeAttack : MessageObject, IRequest
    {
        public static G2P_WorldMeleeAttack Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_WorldMeleeAttack), isFromPool) as G2P_WorldMeleeAttack;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(3)]
        public long AttackSequence { get; set; }

        [MemoryPackOrder(4)]
        public int WeaponItemId { get; set; }

        [MemoryPackOrder(5)]
        public float AimX { get; set; }

        [MemoryPackOrder(6)]
        public float AimZ { get; set; }

        [MemoryPackOrder(7)]
        public long LifecycleRevision { get; set; }

        [MemoryPackOrder(8)]
        public float AimY { get; set; }

        [MemoryPackOrder(9)]
        public float OriginX { get; set; }

        [MemoryPackOrder(10)]
        public float OriginY { get; set; }

        [MemoryPackOrder(11)]
        public float OriginZ { get; set; }

        [MemoryPackOrder(12)]
        public bool HasProjectileOrigin { get; set; }

        [MemoryPackOrder(13)]
        public string CollisionContentHash { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;
            this.AttackSequence = default;
            this.WeaponItemId = default;
            this.AimX = default;
            this.AimZ = default;
            this.LifecycleRevision = default;
            this.AimY = default;
            this.OriginX = default;
            this.OriginY = default;
            this.OriginZ = default;
            this.HasProjectileOrigin = default;
            this.CollisionContentHash = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_WorldMeleeAttack)]
    public partial class P2G_WorldMeleeAttack : MessageObject, IResponse
    {
        public static P2G_WorldMeleeAttack Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_WorldMeleeAttack), isFromPool) as P2G_WorldMeleeAttack;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public long WorldId { get; set; }

        [MemoryPackOrder(4)]
        public long UnitId { get; set; }

        [MemoryPackOrder(5)]
        public long AttackSequence { get; set; }

        [MemoryPackOrder(6)]
        public bool Accepted { get; set; }

        [MemoryPackOrder(7)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(8)]
        public long LifecycleRevision { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.WorldId = default;
            this.UnitId = default;
            this.AttackSequence = default;
            this.Accepted = default;
            this.WorldRevision = default;
            this.LifecycleRevision = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2W_WorldMeleeAttack)]
    [ResponseType(nameof(W2P_WorldMeleeAttack))]
    public partial class P2W_WorldMeleeAttack : MessageObject, IRequest
    {
        public static P2W_WorldMeleeAttack Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2W_WorldMeleeAttack), isFromPool) as P2W_WorldMeleeAttack;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public long UnitId { get; set; }

        [MemoryPackOrder(4)]
        public long AttackSequence { get; set; }

        [MemoryPackOrder(5)]
        public int WeaponItemId { get; set; }

        [MemoryPackOrder(6)]
        public float AimX { get; set; }

        [MemoryPackOrder(7)]
        public float AimZ { get; set; }

        [MemoryPackOrder(8)]
        public long LifecycleRevision { get; set; }

        [MemoryPackOrder(9)]
        public string MapId { get; set; }

        [MemoryPackOrder(10)]
        public float AimY { get; set; }

        [MemoryPackOrder(11)]
        public float OriginX { get; set; }

        [MemoryPackOrder(12)]
        public float OriginY { get; set; }

        [MemoryPackOrder(13)]
        public float OriginZ { get; set; }

        [MemoryPackOrder(14)]
        public bool HasProjectileOrigin { get; set; }

        [MemoryPackOrder(15)]
        public string CollisionContentHash { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.WorldId = default;
            this.UnitId = default;
            this.AttackSequence = default;
            this.WeaponItemId = default;
            this.AimX = default;
            this.AimZ = default;
            this.LifecycleRevision = default;
            this.MapId = default;
            this.AimY = default;
            this.OriginX = default;
            this.OriginY = default;
            this.OriginZ = default;
            this.HasProjectileOrigin = default;
            this.CollisionContentHash = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_WorldMeleeAttack)]
    public partial class W2P_WorldMeleeAttack : MessageObject, IResponse
    {
        public static W2P_WorldMeleeAttack Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_WorldMeleeAttack), isFromPool) as W2P_WorldMeleeAttack;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public long WorldId { get; set; }

        [MemoryPackOrder(4)]
        public long UnitId { get; set; }

        [MemoryPackOrder(5)]
        public long AttackSequence { get; set; }

        [MemoryPackOrder(6)]
        public bool Accepted { get; set; }

        [MemoryPackOrder(7)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(8)]
        public long LifecycleRevision { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.WorldId = default;
            this.UnitId = default;
            this.AttackSequence = default;
            this.Accepted = default;
            this.WorldRevision = default;
            this.LifecycleRevision = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_ReportWorldEnemyNavigation)]
    [ResponseType(nameof(P2G_ReportWorldEnemyNavigation))]
    public partial class G2P_ReportWorldEnemyNavigation : MessageObject, IRequest
    {
        public static G2P_ReportWorldEnemyNavigation Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_ReportWorldEnemyNavigation), isFromPool) as G2P_ReportWorldEnemyNavigation;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(3)]
        public long NavigationLeaseRevision { get; set; }

        [MemoryPackOrder(4)]
        public long ReportSequence { get; set; }

        [MemoryPackOrder(5)]
        public List<WorldEnemyNavigationReport> Reports { get; set; } = new();

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;
            this.NavigationLeaseRevision = default;
            this.ReportSequence = default;
            this.Reports.Clear();

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_ReportWorldEnemyNavigation)]
    public partial class P2G_ReportWorldEnemyNavigation : MessageObject, IResponse
    {
        public static P2G_ReportWorldEnemyNavigation Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_ReportWorldEnemyNavigation), isFromPool) as P2G_ReportWorldEnemyNavigation;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool Accepted { get; set; }

        [MemoryPackOrder(4)]
        public long CurrentLeaseRevision { get; set; }

        [MemoryPackOrder(5)]
        public long LastAcceptedReportSequence { get; set; }

        [MemoryPackOrder(6)]
        public long WorldRevision { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Accepted = default;
            this.CurrentLeaseRevision = default;
            this.LastAcceptedReportSequence = default;
            this.WorldRevision = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2W_ReportWorldEnemyNavigation)]
    [ResponseType(nameof(W2P_ReportWorldEnemyNavigation))]
    public partial class P2W_ReportWorldEnemyNavigation : MessageObject, IRequest
    {
        public static P2W_ReportWorldEnemyNavigation Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2W_ReportWorldEnemyNavigation), isFromPool) as P2W_ReportWorldEnemyNavigation;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public long UnitId { get; set; }

        [MemoryPackOrder(4)]
        public long NavigationLeaseRevision { get; set; }

        [MemoryPackOrder(5)]
        public long ReportSequence { get; set; }

        [MemoryPackOrder(6)]
        public List<WorldEnemyNavigationReport> Reports { get; set; } = new();

        [MemoryPackOrder(7)]
        public string MapId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.WorldId = default;
            this.UnitId = default;
            this.NavigationLeaseRevision = default;
            this.ReportSequence = default;
            this.Reports.Clear();
            this.MapId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_ReportWorldEnemyNavigation)]
    public partial class W2P_ReportWorldEnemyNavigation : MessageObject, IResponse
    {
        public static W2P_ReportWorldEnemyNavigation Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_ReportWorldEnemyNavigation), isFromPool) as W2P_ReportWorldEnemyNavigation;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool Accepted { get; set; }

        [MemoryPackOrder(4)]
        public long CurrentLeaseRevision { get; set; }

        [MemoryPackOrder(5)]
        public long LastAcceptedReportSequence { get; set; }

        [MemoryPackOrder(6)]
        public long WorldRevision { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Accepted = default;
            this.CurrentLeaseRevision = default;
            this.LastAcceptedReportSequence = default;
            this.WorldRevision = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2W_SetWorldConnection)]
    [ResponseType(nameof(W2P_SetWorldConnection))]
    public partial class P2W_SetWorldConnection : MessageObject, IRequest
    {
        public static P2W_SetWorldConnection Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2W_SetWorldConnection), isFromPool) as P2W_SetWorldConnection;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public long UnitId { get; set; }

        [MemoryPackOrder(4)]
        public ActorId PlayerActorId { get; set; }

        [MemoryPackOrder(5)]
        public int ConnectionState { get; set; }

        [MemoryPackOrder(6)]
        public string MapId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.WorldId = default;
            this.UnitId = default;
            this.PlayerActorId = default;
            this.ConnectionState = default;
            this.MapId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_SetWorldConnection)]
    public partial class W2P_SetWorldConnection : MessageObject, IResponse
    {
        public static W2P_SetWorldConnection Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_SetWorldConnection), isFromPool) as W2P_SetWorldConnection;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_WorldPlayerEntered)]
    public partial class W2P_WorldPlayerEntered : MessageObject, IMessage
    {
        public static W2P_WorldPlayerEntered Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_WorldPlayerEntered), isFromPool) as W2P_WorldPlayerEntered;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(2)]
        public WorldPlayerInfo Player { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.WorldRevision = default;
            this.Player = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_WorldPlayerTransformChanged)]
    public partial class W2P_WorldPlayerTransformChanged : MessageObject, IMessage
    {
        public static W2P_WorldPlayerTransformChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_WorldPlayerTransformChanged), isFromPool) as W2P_WorldPlayerTransformChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long WorldId { get; set; }

        [MemoryPackOrder(2)]
        public string MapId { get; set; }

        [MemoryPackOrder(3)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(4)]
        public WorldPlayerInfo Player { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.WorldId = default;
            this.MapId = default;
            this.WorldRevision = default;
            this.Player = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_WorldPlayerLeft)]
    public partial class W2P_WorldPlayerLeft : MessageObject, IMessage
    {
        public static W2P_WorldPlayerLeft Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_WorldPlayerLeft), isFromPool) as W2P_WorldPlayerLeft;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long WorldId { get; set; }

        [MemoryPackOrder(2)]
        public string MapId { get; set; }

        [MemoryPackOrder(3)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(4)]
        public long UnitId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.WorldId = default;
            this.MapId = default;
            this.WorldRevision = default;
            this.UnitId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_WorldPlayerEntered)]
    public partial class P2G_WorldPlayerEntered : MessageObject, IMessage
    {
        public static P2G_WorldPlayerEntered Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_WorldPlayerEntered), isFromPool) as P2G_WorldPlayerEntered;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(2)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(3)]
        public WorldPlayerInfo Player { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.SessionEpoch = default;
            this.WorldRevision = default;
            this.Player = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_WorldPlayerTransformChanged)]
    public partial class P2G_WorldPlayerTransformChanged : MessageObject, IMessage
    {
        public static P2G_WorldPlayerTransformChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_WorldPlayerTransformChanged), isFromPool) as P2G_WorldPlayerTransformChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public string MapId { get; set; }

        [MemoryPackOrder(4)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(5)]
        public WorldPlayerInfo Player { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.SessionEpoch = default;
            this.WorldId = default;
            this.MapId = default;
            this.WorldRevision = default;
            this.Player = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_WorldPlayerLeft)]
    public partial class P2G_WorldPlayerLeft : MessageObject, IMessage
    {
        public static P2G_WorldPlayerLeft Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_WorldPlayerLeft), isFromPool) as P2G_WorldPlayerLeft;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public string MapId { get; set; }

        [MemoryPackOrder(4)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(5)]
        public long UnitId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.SessionEpoch = default;
            this.WorldId = default;
            this.MapId = default;
            this.WorldRevision = default;
            this.UnitId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_WorldCombatChanged)]
    public partial class W2P_WorldCombatChanged : MessageObject, IMessage
    {
        public static W2P_WorldCombatChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_WorldCombatChanged), isFromPool) as W2P_WorldCombatChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long WorldId { get; set; }

        [MemoryPackOrder(2)]
        public string MapId { get; set; }

        [MemoryPackOrder(3)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(4)]
        public long ServerTime { get; set; }

        [MemoryPackOrder(5)]
        public long CombatSequence { get; set; }

        [MemoryPackOrder(6)]
        public int ActionKind { get; set; }

        [MemoryPackOrder(7)]
        public long ActorUnitId { get; set; }

        [MemoryPackOrder(8)]
        public int WeaponItemId { get; set; }

        [MemoryPackOrder(9)]
        public List<long> TargetUnitIds { get; set; } = new();

        [MemoryPackOrder(10)]
        public List<WorldPlayerInfo> Players { get; set; } = new();

        [MemoryPackOrder(11)]
        public List<WorldEnemyInfo> Enemies { get; set; } = new();

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.WorldId = default;
            this.MapId = default;
            this.WorldRevision = default;
            this.ServerTime = default;
            this.CombatSequence = default;
            this.ActionKind = default;
            this.ActorUnitId = default;
            this.WeaponItemId = default;
            this.TargetUnitIds.Clear();
            this.Players.Clear();
            this.Enemies.Clear();

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_WorldEnemiesTransformChanged)]
    public partial class W2P_WorldEnemiesTransformChanged : MessageObject, IMessage
    {
        public static W2P_WorldEnemiesTransformChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_WorldEnemiesTransformChanged), isFromPool) as W2P_WorldEnemiesTransformChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long WorldId { get; set; }

        [MemoryPackOrder(2)]
        public string MapId { get; set; }

        [MemoryPackOrder(3)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(4)]
        public List<WorldEnemyTransformInfo> Enemies { get; set; } = new();

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.WorldId = default;
            this.MapId = default;
            this.WorldRevision = default;
            this.Enemies.Clear();

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_WorldNavigationLeaseChanged)]
    public partial class W2P_WorldNavigationLeaseChanged : MessageObject, IMessage
    {
        public static W2P_WorldNavigationLeaseChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_WorldNavigationLeaseChanged), isFromPool) as W2P_WorldNavigationLeaseChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long WorldId { get; set; }

        [MemoryPackOrder(2)]
        public string MapId { get; set; }

        [MemoryPackOrder(3)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(4)]
        public long NavigationExecutorUnitId { get; set; }

        [MemoryPackOrder(5)]
        public long NavigationLeaseRevision { get; set; }

        [MemoryPackOrder(6)]
        public long ServerTime { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.WorldId = default;
            this.MapId = default;
            this.WorldRevision = default;
            this.NavigationExecutorUnitId = default;
            this.NavigationLeaseRevision = default;
            this.ServerTime = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_WorldCombatChanged)]
    public partial class P2G_WorldCombatChanged : MessageObject, IMessage
    {
        public static P2G_WorldCombatChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_WorldCombatChanged), isFromPool) as P2G_WorldCombatChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public string MapId { get; set; }

        [MemoryPackOrder(4)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(5)]
        public long ServerTime { get; set; }

        [MemoryPackOrder(6)]
        public long CombatSequence { get; set; }

        [MemoryPackOrder(7)]
        public int ActionKind { get; set; }

        [MemoryPackOrder(8)]
        public long ActorUnitId { get; set; }

        [MemoryPackOrder(9)]
        public int WeaponItemId { get; set; }

        [MemoryPackOrder(10)]
        public List<long> TargetUnitIds { get; set; } = new();

        [MemoryPackOrder(11)]
        public List<WorldPlayerInfo> Players { get; set; } = new();

        [MemoryPackOrder(12)]
        public List<WorldEnemyInfo> Enemies { get; set; } = new();

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.SessionEpoch = default;
            this.WorldId = default;
            this.MapId = default;
            this.WorldRevision = default;
            this.ServerTime = default;
            this.CombatSequence = default;
            this.ActionKind = default;
            this.ActorUnitId = default;
            this.WeaponItemId = default;
            this.TargetUnitIds.Clear();
            this.Players.Clear();
            this.Enemies.Clear();

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_WorldEnemiesTransformChanged)]
    public partial class P2G_WorldEnemiesTransformChanged : MessageObject, IMessage
    {
        public static P2G_WorldEnemiesTransformChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_WorldEnemiesTransformChanged), isFromPool) as P2G_WorldEnemiesTransformChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public string MapId { get; set; }

        [MemoryPackOrder(4)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(5)]
        public List<WorldEnemyTransformInfo> Enemies { get; set; } = new();

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.SessionEpoch = default;
            this.WorldId = default;
            this.MapId = default;
            this.WorldRevision = default;
            this.Enemies.Clear();

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_WorldNavigationLeaseChanged)]
    public partial class P2G_WorldNavigationLeaseChanged : MessageObject, IMessage
    {
        public static P2G_WorldNavigationLeaseChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_WorldNavigationLeaseChanged), isFromPool) as P2G_WorldNavigationLeaseChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public string MapId { get; set; }

        [MemoryPackOrder(4)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(5)]
        public long NavigationExecutorUnitId { get; set; }

        [MemoryPackOrder(6)]
        public long NavigationLeaseRevision { get; set; }

        [MemoryPackOrder(7)]
        public long ServerTime { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.SessionEpoch = default;
            this.WorldId = default;
            this.MapId = default;
            this.WorldRevision = default;
            this.NavigationExecutorUnitId = default;
            this.NavigationLeaseRevision = default;
            this.ServerTime = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_WorldCorpseChanged)]
    public partial class W2P_WorldCorpseChanged : MessageObject, IMessage
    {
        public static W2P_WorldCorpseChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_WorldCorpseChanged), isFromPool) as W2P_WorldCorpseChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long WorldId { get; set; }

        [MemoryPackOrder(2)]
        public string MapId { get; set; }

        [MemoryPackOrder(3)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(4)]
        public bool HasCorpse { get; set; }

        [MemoryPackOrder(5)]
        public WorldCorpseInfo Corpse { get; set; }

        [MemoryPackOrder(6)]
        public long RemovedCorpseId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.WorldId = default;
            this.MapId = default;
            this.WorldRevision = default;
            this.HasCorpse = default;
            this.Corpse = default;
            this.RemovedCorpseId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_WorldCorpseChanged)]
    public partial class P2G_WorldCorpseChanged : MessageObject, IMessage
    {
        public static P2G_WorldCorpseChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_WorldCorpseChanged), isFromPool) as P2G_WorldCorpseChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public string MapId { get; set; }

        [MemoryPackOrder(4)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(5)]
        public bool HasCorpse { get; set; }

        [MemoryPackOrder(6)]
        public WorldCorpseInfo Corpse { get; set; }

        [MemoryPackOrder(7)]
        public long RemovedCorpseId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.SessionEpoch = default;
            this.WorldId = default;
            this.MapId = default;
            this.WorldRevision = default;
            this.HasCorpse = default;
            this.Corpse = default;
            this.RemovedCorpseId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_WorldInventoryCheckpoint)]
    public partial class W2P_WorldInventoryCheckpoint : MessageObject, IMessage
    {
        public static W2P_WorldInventoryCheckpoint Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_WorldInventoryCheckpoint), isFromPool) as W2P_WorldInventoryCheckpoint;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long WorldId { get; set; }

        [MemoryPackOrder(2)]
        public string MapId { get; set; }

        [MemoryPackOrder(3)]
        public string OperationId { get; set; }

        [MemoryPackOrder(4)]
        public OnlineInventoryInfo OnlineInventory { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.WorldId = default;
            this.MapId = default;
            this.OperationId = default;
            this.OnlineInventory = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_WorldSelfInventoryChanged)]
    public partial class W2P_WorldSelfInventoryChanged : MessageObject, IMessage
    {
        public static W2P_WorldSelfInventoryChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_WorldSelfInventoryChanged), isFromPool) as W2P_WorldSelfInventoryChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long WorldId { get; set; }

        [MemoryPackOrder(2)]
        public string MapId { get; set; }

        [MemoryPackOrder(3)]
        public OnlineInventoryInfo SelfInventory { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.WorldId = default;
            this.MapId = default;
            this.SelfInventory = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_WorldSelfInventoryChanged)]
    public partial class P2G_WorldSelfInventoryChanged : MessageObject, IMessage
    {
        public static P2G_WorldSelfInventoryChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_WorldSelfInventoryChanged), isFromPool) as P2G_WorldSelfInventoryChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public string MapId { get; set; }

        [MemoryPackOrder(4)]
        public OnlineInventoryInfo SelfInventory { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.SessionEpoch = default;
            this.WorldId = default;
            this.MapId = default;
            this.SelfInventory = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_WorldSelfUnitChanged)]
    public partial class W2P_WorldSelfUnitChanged : MessageObject, IMessage
    {
        public static W2P_WorldSelfUnitChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_WorldSelfUnitChanged), isFromPool) as W2P_WorldSelfUnitChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long WorldId { get; set; }

        [MemoryPackOrder(2)]
        public string MapId { get; set; }

        [MemoryPackOrder(3)]
        public long PreviousUnitId { get; set; }

        [MemoryPackOrder(4)]
        public long SelfUnitId { get; set; }

        [MemoryPackOrder(5)]
        public long WorldRevision { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.WorldId = default;
            this.MapId = default;
            this.PreviousUnitId = default;
            this.SelfUnitId = default;
            this.WorldRevision = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_WorldSelfUnitChanged)]
    public partial class P2G_WorldSelfUnitChanged : MessageObject, IMessage
    {
        public static P2G_WorldSelfUnitChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_WorldSelfUnitChanged), isFromPool) as P2G_WorldSelfUnitChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public string MapId { get; set; }

        [MemoryPackOrder(4)]
        public long PreviousUnitId { get; set; }

        [MemoryPackOrder(5)]
        public long SelfUnitId { get; set; }

        [MemoryPackOrder(6)]
        public long WorldRevision { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.SessionEpoch = default;
            this.WorldId = default;
            this.MapId = default;
            this.PreviousUnitId = default;
            this.SelfUnitId = default;
            this.WorldRevision = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_WorldTemporaryBuildingChanged)]
    public partial class W2P_WorldTemporaryBuildingChanged : MessageObject, IMessage
    {
        public static W2P_WorldTemporaryBuildingChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_WorldTemporaryBuildingChanged), isFromPool) as W2P_WorldTemporaryBuildingChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long WorldId { get; set; }

        [MemoryPackOrder(2)]
        public string MapId { get; set; }

        [MemoryPackOrder(3)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(4)]
        public bool HasBuilding { get; set; }

        [MemoryPackOrder(5)]
        public WorldTemporaryBuildingInfo Building { get; set; }

        [MemoryPackOrder(6)]
        public int RemovedRuntimeId { get; set; }

        [MemoryPackOrder(7)]
        public long ServerTime { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.WorldId = default;
            this.MapId = default;
            this.WorldRevision = default;
            this.HasBuilding = default;
            this.Building = default;
            this.RemovedRuntimeId = default;
            this.ServerTime = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_WorldTemporaryBuildingChanged)]
    public partial class P2G_WorldTemporaryBuildingChanged : MessageObject, IMessage
    {
        public static P2G_WorldTemporaryBuildingChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_WorldTemporaryBuildingChanged), isFromPool) as P2G_WorldTemporaryBuildingChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public string MapId { get; set; }

        [MemoryPackOrder(4)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(5)]
        public bool HasBuilding { get; set; }

        [MemoryPackOrder(6)]
        public WorldTemporaryBuildingInfo Building { get; set; }

        [MemoryPackOrder(7)]
        public int RemovedRuntimeId { get; set; }

        [MemoryPackOrder(8)]
        public long ServerTime { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.SessionEpoch = default;
            this.WorldId = default;
            this.MapId = default;
            this.WorldRevision = default;
            this.HasBuilding = default;
            this.Building = default;
            this.RemovedRuntimeId = default;
            this.ServerTime = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2R_GetPublicRoomList)]
    [ResponseType(nameof(R2G_GetPublicRoomList))]
    public partial class G2R_GetPublicRoomList : MessageObject, IRequest
    {
        public static G2R_GetPublicRoomList Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2R_GetPublicRoomList), isFromPool) as G2R_GetPublicRoomList;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long CursorRoomId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.CursorRoomId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2G_GetPublicRoomList)]
    public partial class R2G_GetPublicRoomList : MessageObject, IResponse
    {
        public static R2G_GetPublicRoomList Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2G_GetPublicRoomList), isFromPool) as R2G_GetPublicRoomList;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public List<PublicRoomSummary> Rooms { get; set; } = new();

        [MemoryPackOrder(4)]
        public long NextCursorRoomId { get; set; }

        [MemoryPackOrder(5)]
        public bool HasMore { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Rooms.Clear();
            this.NextCursorRoomId = default;
            this.HasMore = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_StartOnlineArcadeRun)]
    [ResponseType(nameof(P2G_StartOnlineArcadeRun))]
    public partial class G2P_StartOnlineArcadeRun : MessageObject, IRequest
    {
        public static G2P_StartOnlineArcadeRun Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_StartOnlineArcadeRun), isFromPool) as G2P_StartOnlineArcadeRun;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(3)]
        public string OperationId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;
            this.OperationId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_StartOnlineArcadeRun)]
    public partial class P2G_StartOnlineArcadeRun : MessageObject, IResponse
    {
        public static P2G_StartOnlineArcadeRun Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_StartOnlineArcadeRun), isFromPool) as P2G_StartOnlineArcadeRun;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool HasRun { get; set; }

        [MemoryPackOrder(4)]
        public OnlineArcadeRunSnapshot Run { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.HasRun = default;
            this.Run = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2R_StartOnlineArcadeRun)]
    [ResponseType(nameof(R2P_StartOnlineArcadeRun))]
    public partial class P2R_StartOnlineArcadeRun : MessageObject, IRequest
    {
        public static P2R_StartOnlineArcadeRun Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2R_StartOnlineArcadeRun), isFromPool) as P2R_StartOnlineArcadeRun;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public string OperationId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.OperationId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2P_StartOnlineArcadeRun)]
    public partial class R2P_StartOnlineArcadeRun : MessageObject, IResponse
    {
        public static R2P_StartOnlineArcadeRun Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2P_StartOnlineArcadeRun), isFromPool) as R2P_StartOnlineArcadeRun;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool HasRun { get; set; }

        [MemoryPackOrder(4)]
        public OnlineArcadeRunSnapshot Run { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.HasRun = default;
            this.Run = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_GetCurrentOnlineArcadeRun)]
    [ResponseType(nameof(P2G_GetCurrentOnlineArcadeRun))]
    public partial class G2P_GetCurrentOnlineArcadeRun : MessageObject, IRequest
    {
        public static G2P_GetCurrentOnlineArcadeRun Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_GetCurrentOnlineArcadeRun), isFromPool) as G2P_GetCurrentOnlineArcadeRun;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_GetCurrentOnlineArcadeRun)]
    public partial class P2G_GetCurrentOnlineArcadeRun : MessageObject, IResponse
    {
        public static P2G_GetCurrentOnlineArcadeRun Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_GetCurrentOnlineArcadeRun), isFromPool) as P2G_GetCurrentOnlineArcadeRun;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool HasRun { get; set; }

        [MemoryPackOrder(4)]
        public OnlineArcadeRunSnapshot Run { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.HasRun = default;
            this.Run = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2R_GetCurrentOnlineArcadeRun)]
    [ResponseType(nameof(R2P_GetCurrentOnlineArcadeRun))]
    public partial class P2R_GetCurrentOnlineArcadeRun : MessageObject, IRequest
    {
        public static P2R_GetCurrentOnlineArcadeRun Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2R_GetCurrentOnlineArcadeRun), isFromPool) as P2R_GetCurrentOnlineArcadeRun;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2P_GetCurrentOnlineArcadeRun)]
    public partial class R2P_GetCurrentOnlineArcadeRun : MessageObject, IResponse
    {
        public static R2P_GetCurrentOnlineArcadeRun Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2P_GetCurrentOnlineArcadeRun), isFromPool) as R2P_GetCurrentOnlineArcadeRun;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool HasRun { get; set; }

        [MemoryPackOrder(4)]
        public OnlineArcadeRunSnapshot Run { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.HasRun = default;
            this.Run = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_OperateOnlineArcadeSafeShop)]
    [ResponseType(nameof(P2G_OperateOnlineArcadeSafeShop))]
    public partial class G2P_OperateOnlineArcadeSafeShop : MessageObject, IRequest
    {
        public static G2P_OperateOnlineArcadeSafeShop Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_OperateOnlineArcadeSafeShop), isFromPool) as G2P_OperateOnlineArcadeSafeShop;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(3)]
        public string OperationId { get; set; }

        [MemoryPackOrder(4)]
        public long ExpectedRevision { get; set; }

        [MemoryPackOrder(5)]
        public int ActionKind { get; set; }

        [MemoryPackOrder(6)]
        public string OfferId { get; set; }

        [MemoryPackOrder(7)]
        public int GoldAmount { get; set; }

        [MemoryPackOrder(8)]
        public bool Ready { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;
            this.OperationId = default;
            this.ExpectedRevision = default;
            this.ActionKind = default;
            this.OfferId = default;
            this.GoldAmount = default;
            this.Ready = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_OperateOnlineArcadeSafeShop)]
    public partial class P2G_OperateOnlineArcadeSafeShop : MessageObject, IResponse
    {
        public static P2G_OperateOnlineArcadeSafeShop Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_OperateOnlineArcadeSafeShop), isFromPool) as P2G_OperateOnlineArcadeSafeShop;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool Accepted { get; set; }

        [MemoryPackOrder(4)]
        public OnlineArcadeRunSnapshot Run { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Accepted = default;
            this.Run = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2R_OperateOnlineArcadeSafeShop)]
    [ResponseType(nameof(R2P_OperateOnlineArcadeSafeShop))]
    public partial class P2R_OperateOnlineArcadeSafeShop : MessageObject, IRequest
    {
        public static P2R_OperateOnlineArcadeSafeShop Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2R_OperateOnlineArcadeSafeShop), isFromPool) as P2R_OperateOnlineArcadeSafeShop;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public string OperationId { get; set; }

        [MemoryPackOrder(3)]
        public long ExpectedRevision { get; set; }

        [MemoryPackOrder(4)]
        public int ActionKind { get; set; }

        [MemoryPackOrder(5)]
        public string OfferId { get; set; }

        [MemoryPackOrder(6)]
        public int GoldAmount { get; set; }

        [MemoryPackOrder(7)]
        public bool Ready { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.OperationId = default;
            this.ExpectedRevision = default;
            this.ActionKind = default;
            this.OfferId = default;
            this.GoldAmount = default;
            this.Ready = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2P_OperateOnlineArcadeSafeShop)]
    public partial class R2P_OperateOnlineArcadeSafeShop : MessageObject, IResponse
    {
        public static R2P_OperateOnlineArcadeSafeShop Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2P_OperateOnlineArcadeSafeShop), isFromPool) as R2P_OperateOnlineArcadeSafeShop;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool Accepted { get; set; }

        [MemoryPackOrder(4)]
        public OnlineArcadeRunSnapshot Run { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Accepted = default;
            this.Run = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2P_OnlineArcadeRunChanged)]
    public partial class R2P_OnlineArcadeRunChanged : MessageObject, IMessage
    {
        public static R2P_OnlineArcadeRunChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2P_OnlineArcadeRunChanged), isFromPool) as R2P_OnlineArcadeRunChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public OnlineArcadeRunSnapshot Run { get; set; }

        [MemoryPackOrder(2)]
        public bool HasRun { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.Run = default;
            this.HasRun = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2R_OnlineArcadeConnectionChanged)]
    public partial class R2R_OnlineArcadeConnectionChanged : MessageObject, IMessage
    {
        public static R2R_OnlineArcadeConnectionChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2R_OnlineArcadeConnectionChanged), isFromPool) as R2R_OnlineArcadeConnectionChanged;
        }

        [MemoryPackOrder(0)]
        public long RoomId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public bool Connected { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RoomId = default;
            this.PlayerId = default;
            this.Connected = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_OnlineArcadeRunChanged)]
    public partial class P2G_OnlineArcadeRunChanged : MessageObject, IMessage
    {
        public static P2G_OnlineArcadeRunChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_OnlineArcadeRunChanged), isFromPool) as P2G_OnlineArcadeRunChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(2)]
        public OnlineArcadeRunSnapshot Run { get; set; }

        [MemoryPackOrder(3)]
        public bool HasRun { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.SessionEpoch = default;
            this.Run = default;
            this.HasRun = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2P_SubmitOnlineArcadeInput)]
    [ResponseType(nameof(P2G_SubmitOnlineArcadeInput))]
    public partial class G2P_SubmitOnlineArcadeInput : MessageObject, IRequest
    {
        public static G2P_SubmitOnlineArcadeInput Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_SubmitOnlineArcadeInput), isFromPool) as G2P_SubmitOnlineArcadeInput;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(3)]
        public int ArenaNumber { get; set; }

        [MemoryPackOrder(4)]
        public long InputSequence { get; set; }

        [MemoryPackOrder(5)]
        public float MoveX { get; set; }

        [MemoryPackOrder(6)]
        public float MoveZ { get; set; }

        [MemoryPackOrder(7)]
        public float AimX { get; set; }

        [MemoryPackOrder(8)]
        public float AimZ { get; set; }

        [MemoryPackOrder(9)]
        public bool Firing { get; set; }

        [MemoryPackOrder(10)]
        public bool UseBomb { get; set; }

        [MemoryPackOrder(11)]
        public bool UseDash { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;
            this.ArenaNumber = default;
            this.InputSequence = default;
            this.MoveX = default;
            this.MoveZ = default;
            this.AimX = default;
            this.AimZ = default;
            this.Firing = default;
            this.UseBomb = default;
            this.UseDash = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_SubmitOnlineArcadeInput)]
    public partial class P2G_SubmitOnlineArcadeInput : MessageObject, IResponse
    {
        public static P2G_SubmitOnlineArcadeInput Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_SubmitOnlineArcadeInput), isFromPool) as P2G_SubmitOnlineArcadeInput;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool Accepted { get; set; }

        [MemoryPackOrder(4)]
        public long LastAcceptedInputSequence { get; set; }

        [MemoryPackOrder(5)]
        public OnlineArcadeRunSnapshot Run { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Accepted = default;
            this.LastAcceptedInputSequence = default;
            this.Run = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2R_SubmitOnlineArcadeInput)]
    [ResponseType(nameof(R2P_SubmitOnlineArcadeInput))]
    public partial class P2R_SubmitOnlineArcadeInput : MessageObject, IRequest
    {
        public static P2R_SubmitOnlineArcadeInput Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2R_SubmitOnlineArcadeInput), isFromPool) as P2R_SubmitOnlineArcadeInput;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public int ArenaNumber { get; set; }

        [MemoryPackOrder(3)]
        public long InputSequence { get; set; }

        [MemoryPackOrder(4)]
        public float MoveX { get; set; }

        [MemoryPackOrder(5)]
        public float MoveZ { get; set; }

        [MemoryPackOrder(6)]
        public float AimX { get; set; }

        [MemoryPackOrder(7)]
        public float AimZ { get; set; }

        [MemoryPackOrder(8)]
        public bool Firing { get; set; }

        [MemoryPackOrder(9)]
        public bool UseBomb { get; set; }

        [MemoryPackOrder(10)]
        public bool UseDash { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.ArenaNumber = default;
            this.InputSequence = default;
            this.MoveX = default;
            this.MoveZ = default;
            this.AimX = default;
            this.AimZ = default;
            this.Firing = default;
            this.UseBomb = default;
            this.UseDash = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2P_SubmitOnlineArcadeInput)]
    public partial class R2P_SubmitOnlineArcadeInput : MessageObject, IResponse
    {
        public static R2P_SubmitOnlineArcadeInput Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2P_SubmitOnlineArcadeInput), isFromPool) as R2P_SubmitOnlineArcadeInput;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool Accepted { get; set; }

        [MemoryPackOrder(4)]
        public long LastAcceptedInputSequence { get; set; }

        [MemoryPackOrder(5)]
        public OnlineArcadeRunSnapshot Run { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Accepted = default;
            this.LastAcceptedInputSequence = default;
            this.Run = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2R_ExecuteOnlineArcadeTestCmd)]
    [ResponseType(nameof(R2G_ExecuteOnlineArcadeTestCmd))]
    public partial class G2R_ExecuteOnlineArcadeTestCmd : MessageObject, IRequest
    {
        public static G2R_ExecuteOnlineArcadeTestCmd Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2R_ExecuteOnlineArcadeTestCmd), isFromPool) as G2R_ExecuteOnlineArcadeTestCmd;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public string ArgumentText { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.ArgumentText = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2G_ExecuteOnlineArcadeTestCmd)]
    public partial class R2G_ExecuteOnlineArcadeTestCmd : MessageObject, IResponse
    {
        public static R2G_ExecuteOnlineArcadeTestCmd Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2G_ExecuteOnlineArcadeTestCmd), isFromPool) as R2G_ExecuteOnlineArcadeTestCmd;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool Success { get; set; }

        [MemoryPackOrder(4)]
        public int ResultCode { get; set; }

        [MemoryPackOrder(5)]
        public long ServerTime { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Success = default;
            this.ResultCode = default;
            this.ServerTime = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.W2P_WorldProjectilesChanged)]
    public partial class W2P_WorldProjectilesChanged : MessageObject, IMessage
    {
        public static W2P_WorldProjectilesChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(W2P_WorldProjectilesChanged), isFromPool) as W2P_WorldProjectilesChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long WorldId { get; set; }

        [MemoryPackOrder(2)]
        public string MapId { get; set; }

        [MemoryPackOrder(3)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(4)]
        public long ServerTime { get; set; }

        [MemoryPackOrder(5)]
        public List<WorldProjectileInfo> Projectiles { get; set; } = new();

        [MemoryPackOrder(6)]
        public List<WorldProjectileTermination> Terminations { get; set; } = new();

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.WorldId = default;
            this.MapId = default;
            this.WorldRevision = default;
            this.ServerTime = default;
            this.Projectiles.Clear();
            this.Terminations.Clear();

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_WorldProjectilesChanged)]
    public partial class P2G_WorldProjectilesChanged : MessageObject, IMessage
    {
        public static P2G_WorldProjectilesChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_WorldProjectilesChanged), isFromPool) as P2G_WorldProjectilesChanged;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(2)]
        public long WorldId { get; set; }

        [MemoryPackOrder(3)]
        public string MapId { get; set; }

        [MemoryPackOrder(4)]
        public long WorldRevision { get; set; }

        [MemoryPackOrder(5)]
        public long ServerTime { get; set; }

        [MemoryPackOrder(6)]
        public List<WorldProjectileInfo> Projectiles { get; set; } = new();

        [MemoryPackOrder(7)]
        public List<WorldProjectileTermination> Terminations { get; set; } = new();

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.SessionEpoch = default;
            this.WorldId = default;
            this.MapId = default;
            this.WorldRevision = default;
            this.ServerTime = default;
            this.Projectiles.Clear();
            this.Terminations.Clear();

            ObjectPool.Instance.Recycle(this);
        }
    }

    public static class OnlineInner
    {
        public const ushort G2A_Register = 22002;
        public const ushort A2G_Register = 22003;
        public const ushort G2A_Login = 22004;
        public const ushort A2G_Login = 22005;
        public const ushort P2A_SavePlayerProfile = 22006;
        public const ushort A2P_SavePlayerProfile = 22007;
        public const ushort G2P_GetOrCreatePlayer = 22008;
        public const ushort P2G_GetOrCreatePlayer = 22009;
        public const ushort G2P_PlayerDisconnected = 22010;
        public const ushort P2G_PlayerDisconnected = 22011;
        public const ushort G2P_PlayerLogout = 22012;
        public const ushort P2G_PlayerLogout = 22013;
        public const ushort P2G_SessionReplaced = 22014;
        public const ushort G2P_PlayerGraceTimeout = 22015;
        public const ushort G2P_PlayerRetrySave = 22016;
        public const ushort G2P_CreateRoom = 22017;
        public const ushort P2G_CreateRoom = 22018;
        public const ushort G2P_MatchArcadeRoom = 22019;
        public const ushort P2G_MatchArcadeRoom = 22020;
        public const ushort G2P_JoinRoom = 22021;
        public const ushort P2G_JoinRoom = 22022;
        public const ushort G2P_LeaveRoom = 22023;
        public const ushort P2G_LeaveRoom = 22024;
        public const ushort G2P_GetCurrentRoom = 22025;
        public const ushort P2G_GetCurrentRoom = 22026;
        public const ushort P2D_CreateRoom = 22027;
        public const ushort D2P_CreateRoom = 22028;
        public const ushort P2D_MatchArcadeRoom = 22029;
        public const ushort D2P_MatchArcadeRoom = 22030;
        public const ushort P2D_ResolveRoom = 22031;
        public const ushort D2P_ResolveRoom = 22032;
        public const ushort D2R_CreateRoom = 22033;
        public const ushort R2D_CreateRoom = 22034;
        public const ushort R2D_CloseRoom = 22035;
        public const ushort D2R_CloseRoom = 22036;
        public const ushort P2R_JoinRoom = 22037;
        public const ushort R2P_JoinRoom = 22038;
        public const ushort P2R_LeaveRoom = 22039;
        public const ushort R2P_LeaveRoom = 22040;
        public const ushort P2R_GetRoom = 22041;
        public const ushort R2P_GetRoom = 22042;
        public const ushort P2R_SetMemberConnection = 22043;
        public const ushort R2P_SetMemberConnection = 22044;
        public const ushort R2P_RoomChanged = 22045;
        public const ushort P2G_RoomChanged = 22046;
        public const ushort R2R_RetryClose = 22047;
        public const ushort P2P_RetryRoomSync = 22048;
        public const ushort RoomChatMemberInfo = 22049;
        public const ushort R2C_SyncRoomChatMembers = 22050;
        public const ushort C2R_SyncRoomChatMembers = 22051;
        public const ushort R2C_CloseRoomChat = 22052;
        public const ushort C2R_CloseRoomChat = 22053;
        public const ushort P2C_SendRoomChat = 22054;
        public const ushort C2P_SendRoomChat = 22055;
        public const ushort P2C_GetRoomChatHistory = 22056;
        public const ushort C2P_GetRoomChatHistory = 22057;
        public const ushort C2P_RoomChatMessage = 22058;
        public const ushort G2P_SendRoomChat = 22059;
        public const ushort P2G_SendRoomChat = 22060;
        public const ushort G2P_GetRoomChatHistory = 22061;
        public const ushort P2G_GetRoomChatHistory = 22062;
        public const ushort P2G_RoomChatMessage = 22063;
        public const ushort P2R_EnsureRoomChatReady = 22064;
        public const ushort R2P_EnsureRoomChatReady = 22065;
        public const ushort R2R_RetryChatSync = 22066;
        public const ushort G2P_EnterWorld = 22067;
        public const ushort P2G_EnterWorld = 22068;
        public const ushort G2P_GetCurrentWorld = 22069;
        public const ushort P2G_GetCurrentWorld = 22070;
        public const ushort G2P_LeaveWorld = 22071;
        public const ushort P2G_LeaveWorld = 22072;
        public const ushort G2P_ReportWorldTransform = 22073;
        public const ushort P2G_ReportWorldTransform = 22074;
        public const ushort P2W_EnterWorld = 22075;
        public const ushort W2P_EnterWorld = 22076;
        public const ushort P2W_GetCurrentWorld = 22077;
        public const ushort W2P_GetCurrentWorld = 22078;
        public const ushort P2W_LeaveWorld = 22079;
        public const ushort W2P_LeaveWorld = 22080;
        public const ushort P2W_PrepareWorldLeave = 22081;
        public const ushort W2P_PrepareWorldLeave = 22082;
        public const ushort P2W_CommitWorldLeave = 22083;
        public const ushort W2P_CommitWorldLeave = 22084;
        public const ushort P2W_AbortWorldLeave = 22085;
        public const ushort W2P_AbortWorldLeave = 22086;
        public const ushort G2P_OpenWorldCorpse = 22087;
        public const ushort P2G_OpenWorldCorpse = 22088;
        public const ushort P2W_OpenWorldCorpse = 22089;
        public const ushort W2P_OpenWorldCorpse = 22090;
        public const ushort G2P_OperateWorldInventory = 22091;
        public const ushort P2G_OperateWorldInventory = 22092;
        public const ushort G2P_OperateWorldTemporaryBuilding = 22093;
        public const ushort P2G_OperateWorldTemporaryBuilding = 22094;
        public const ushort P2W_OperateWorldInventory = 22095;
        public const ushort W2P_OperateWorldInventory = 22096;
        public const ushort P2W_OperateWorldTemporaryBuilding = 22097;
        public const ushort W2P_OperateWorldTemporaryBuilding = 22098;
        public const ushort P2W_ReportWorldTransform = 22099;
        public const ushort W2P_ReportWorldTransform = 22100;
        public const ushort G2P_WorldMeleeAttack = 22101;
        public const ushort P2G_WorldMeleeAttack = 22102;
        public const ushort P2W_WorldMeleeAttack = 22103;
        public const ushort W2P_WorldMeleeAttack = 22104;
        public const ushort G2P_ReportWorldEnemyNavigation = 22105;
        public const ushort P2G_ReportWorldEnemyNavigation = 22106;
        public const ushort P2W_ReportWorldEnemyNavigation = 22107;
        public const ushort W2P_ReportWorldEnemyNavigation = 22108;
        public const ushort P2W_SetWorldConnection = 22109;
        public const ushort W2P_SetWorldConnection = 22110;
        public const ushort W2P_WorldPlayerEntered = 22111;
        public const ushort W2P_WorldPlayerTransformChanged = 22112;
        public const ushort W2P_WorldPlayerLeft = 22113;
        public const ushort P2G_WorldPlayerEntered = 22114;
        public const ushort P2G_WorldPlayerTransformChanged = 22115;
        public const ushort P2G_WorldPlayerLeft = 22116;
        public const ushort W2P_WorldCombatChanged = 22117;
        public const ushort W2P_WorldEnemiesTransformChanged = 22118;
        public const ushort W2P_WorldNavigationLeaseChanged = 22119;
        public const ushort P2G_WorldCombatChanged = 22120;
        public const ushort P2G_WorldEnemiesTransformChanged = 22121;
        public const ushort P2G_WorldNavigationLeaseChanged = 22122;
        public const ushort W2P_WorldCorpseChanged = 22123;
        public const ushort P2G_WorldCorpseChanged = 22124;
        public const ushort W2P_WorldInventoryCheckpoint = 22125;
        public const ushort W2P_WorldSelfInventoryChanged = 22126;
        public const ushort P2G_WorldSelfInventoryChanged = 22127;
        public const ushort W2P_WorldSelfUnitChanged = 22128;
        public const ushort P2G_WorldSelfUnitChanged = 22129;
        public const ushort W2P_WorldTemporaryBuildingChanged = 22130;
        public const ushort P2G_WorldTemporaryBuildingChanged = 22131;
        public const ushort G2R_GetPublicRoomList = 22132;
        public const ushort R2G_GetPublicRoomList = 22133;
        public const ushort G2P_StartOnlineArcadeRun = 22134;
        public const ushort P2G_StartOnlineArcadeRun = 22135;
        public const ushort P2R_StartOnlineArcadeRun = 22136;
        public const ushort R2P_StartOnlineArcadeRun = 22137;
        public const ushort G2P_GetCurrentOnlineArcadeRun = 22138;
        public const ushort P2G_GetCurrentOnlineArcadeRun = 22139;
        public const ushort P2R_GetCurrentOnlineArcadeRun = 22140;
        public const ushort R2P_GetCurrentOnlineArcadeRun = 22141;
        public const ushort G2P_OperateOnlineArcadeSafeShop = 22142;
        public const ushort P2G_OperateOnlineArcadeSafeShop = 22143;
        public const ushort P2R_OperateOnlineArcadeSafeShop = 22144;
        public const ushort R2P_OperateOnlineArcadeSafeShop = 22145;
        public const ushort R2P_OnlineArcadeRunChanged = 22146;
        public const ushort R2R_OnlineArcadeConnectionChanged = 22147;
        public const ushort P2G_OnlineArcadeRunChanged = 22148;
        public const ushort G2P_SubmitOnlineArcadeInput = 22149;
        public const ushort P2G_SubmitOnlineArcadeInput = 22150;
        public const ushort P2R_SubmitOnlineArcadeInput = 22151;
        public const ushort R2P_SubmitOnlineArcadeInput = 22152;
        public const ushort G2R_ExecuteOnlineArcadeTestCmd = 22153;
        public const ushort R2G_ExecuteOnlineArcadeTestCmd = 22154;
        public const ushort W2P_WorldProjectilesChanged = 22155;
        public const ushort P2G_WorldProjectilesChanged = 22156;
    }
}