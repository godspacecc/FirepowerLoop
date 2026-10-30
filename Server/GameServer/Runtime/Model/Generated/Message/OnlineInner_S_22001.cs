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
        public long SessionEpoch { get; set; }

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
            this.SessionEpoch = default;

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

        [MemoryPackOrder(7)]
        public long ProfileRevision { get; set; }

        [MemoryPackOrder(8)]
        public long SessionEpoch { get; set; }

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
            this.ProfileRevision = default;
            this.SessionEpoch = default;

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
    [Message(OnlineInner.G2P_OperateOnlineArcadeSafeCheckpoint)]
    [ResponseType(nameof(P2G_OperateOnlineArcadeSafeCheckpoint))]
    public partial class G2P_OperateOnlineArcadeSafeCheckpoint : MessageObject, IRequest
    {
        public static G2P_OperateOnlineArcadeSafeCheckpoint Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_OperateOnlineArcadeSafeCheckpoint), isFromPool) as G2P_OperateOnlineArcadeSafeCheckpoint;
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

        [MemoryPackOrder(9)]
        public string ScoreShopDropId { get; set; }

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
            this.ScoreShopDropId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_OperateOnlineArcadeSafeCheckpoint)]
    public partial class P2G_OperateOnlineArcadeSafeCheckpoint : MessageObject, IResponse
    {
        public static P2G_OperateOnlineArcadeSafeCheckpoint Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_OperateOnlineArcadeSafeCheckpoint), isFromPool) as P2G_OperateOnlineArcadeSafeCheckpoint;
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
    [Message(OnlineInner.P2R_OperateOnlineArcadeSafeCheckpoint)]
    [ResponseType(nameof(R2P_OperateOnlineArcadeSafeCheckpoint))]
    public partial class P2R_OperateOnlineArcadeSafeCheckpoint : MessageObject, IRequest
    {
        public static P2R_OperateOnlineArcadeSafeCheckpoint Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2R_OperateOnlineArcadeSafeCheckpoint), isFromPool) as P2R_OperateOnlineArcadeSafeCheckpoint;
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

        [MemoryPackOrder(8)]
        public string ScoreShopDropId { get; set; }

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
            this.ScoreShopDropId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2P_OperateOnlineArcadeSafeCheckpoint)]
    public partial class R2P_OperateOnlineArcadeSafeCheckpoint : MessageObject, IResponse
    {
        public static R2P_OperateOnlineArcadeSafeCheckpoint Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2P_OperateOnlineArcadeSafeCheckpoint), isFromPool) as R2P_OperateOnlineArcadeSafeCheckpoint;
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

        [MemoryPackOrder(12)]
        public int RoundGeneration { get; set; }

        [MemoryPackOrder(13)]
        public bool RoundReady { get; set; }

        [MemoryPackOrder(14)]
        public int ExitTarget { get; set; }

        [MemoryPackOrder(15)]
        public bool ConfirmExit { get; set; }

        [MemoryPackOrder(16)]
        public bool UseFieldUpgrade { get; set; }

        [MemoryPackOrder(17)]
        public long DonationRunId { get; set; }

        [MemoryPackOrder(18)]
        public long DonateLifeTargetId { get; set; }

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
            this.RoundGeneration = default;
            this.RoundReady = default;
            this.ExitTarget = default;
            this.ConfirmExit = default;
            this.UseFieldUpgrade = default;
            this.DonationRunId = default;
            this.DonateLifeTargetId = default;

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

        [MemoryPackOrder(11)]
        public int RoundGeneration { get; set; }

        [MemoryPackOrder(12)]
        public bool RoundReady { get; set; }

        [MemoryPackOrder(13)]
        public int ExitTarget { get; set; }

        [MemoryPackOrder(14)]
        public bool ConfirmExit { get; set; }

        [MemoryPackOrder(15)]
        public bool UseFieldUpgrade { get; set; }

        [MemoryPackOrder(16)]
        public long DonationRunId { get; set; }

        [MemoryPackOrder(17)]
        public long DonateLifeTargetId { get; set; }

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
            this.RoundGeneration = default;
            this.RoundReady = default;
            this.ExitTarget = default;
            this.ConfirmExit = default;
            this.UseFieldUpgrade = default;
            this.DonationRunId = default;
            this.DonateLifeTargetId = default;

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
    [Message(OnlineInner.G2P_OperateOnlineSlayerMart)]
    [ResponseType(nameof(P2G_OperateOnlineSlayerMart))]
    public partial class G2P_OperateOnlineSlayerMart : MessageObject, IRequest
    {
        public static G2P_OperateOnlineSlayerMart Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2P_OperateOnlineSlayerMart), isFromPool) as G2P_OperateOnlineSlayerMart;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(3)]
        public string RegionInstanceId { get; set; }

        [MemoryPackOrder(4)]
        public int Generation { get; set; }

        [MemoryPackOrder(5)]
        public long ExpectedRevision { get; set; }

        [MemoryPackOrder(6)]
        public string OperationId { get; set; }

        [MemoryPackOrder(7)]
        public int ActionKind { get; set; }

        [MemoryPackOrder(8)]
        public string ItemId { get; set; }

        [MemoryPackOrder(9)]
        public int BoxId { get; set; }

        [MemoryPackOrder(10)]
        public int ConfirmedAmount { get; set; }

        [MemoryPackOrder(11)]
        public bool Value { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.SessionEpoch = default;
            this.RegionInstanceId = default;
            this.Generation = default;
            this.ExpectedRevision = default;
            this.OperationId = default;
            this.ActionKind = default;
            this.ItemId = default;
            this.BoxId = default;
            this.ConfirmedAmount = default;
            this.Value = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2G_OperateOnlineSlayerMart)]
    public partial class P2G_OperateOnlineSlayerMart : MessageObject, IResponse
    {
        public static P2G_OperateOnlineSlayerMart Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2G_OperateOnlineSlayerMart), isFromPool) as P2G_OperateOnlineSlayerMart;
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
        public bool Replayed { get; set; }

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
            this.Replayed = default;
            this.Run = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.P2R_OperateOnlineSlayerMart)]
    [ResponseType(nameof(R2P_OperateOnlineSlayerMart))]
    public partial class P2R_OperateOnlineSlayerMart : MessageObject, IRequest
    {
        public static P2R_OperateOnlineSlayerMart Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(P2R_OperateOnlineSlayerMart), isFromPool) as P2R_OperateOnlineSlayerMart;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public string RegionInstanceId { get; set; }

        [MemoryPackOrder(3)]
        public int Generation { get; set; }

        [MemoryPackOrder(4)]
        public long ExpectedRevision { get; set; }

        [MemoryPackOrder(5)]
        public string OperationId { get; set; }

        [MemoryPackOrder(6)]
        public int ActionKind { get; set; }

        [MemoryPackOrder(7)]
        public string ItemId { get; set; }

        [MemoryPackOrder(8)]
        public int BoxId { get; set; }

        [MemoryPackOrder(9)]
        public int ConfirmedAmount { get; set; }

        [MemoryPackOrder(10)]
        public bool Value { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.PlayerId = default;
            this.RegionInstanceId = default;
            this.Generation = default;
            this.ExpectedRevision = default;
            this.OperationId = default;
            this.ActionKind = default;
            this.ItemId = default;
            this.BoxId = default;
            this.ConfirmedAmount = default;
            this.Value = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.R2P_OperateOnlineSlayerMart)]
    public partial class R2P_OperateOnlineSlayerMart : MessageObject, IResponse
    {
        public static R2P_OperateOnlineSlayerMart Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(R2P_OperateOnlineSlayerMart), isFromPool) as R2P_OperateOnlineSlayerMart;
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
        public bool Replayed { get; set; }

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
            this.Replayed = default;
            this.Run = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.G2A_ValidateAccountSession)]
    [ResponseType(nameof(A2G_ValidateAccountSession))]
    public partial class G2A_ValidateAccountSession : MessageObject, IRequest
    {
        public static G2A_ValidateAccountSession Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2A_ValidateAccountSession), isFromPool) as G2A_ValidateAccountSession;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public long AccountId { get; set; }

        [MemoryPackOrder(2)]
        public long SessionEpoch { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.AccountId = default;
            this.SessionEpoch = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineInner.A2G_ValidateAccountSession)]
    public partial class A2G_ValidateAccountSession : MessageObject, IResponse
    {
        public static A2G_ValidateAccountSession Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(A2G_ValidateAccountSession), isFromPool) as A2G_ValidateAccountSession;
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

    public static class OnlineInner
    {
        public const ushort G2A_Register = 22002;
        public const ushort A2G_Register = 22003;
        public const ushort G2A_Login = 22004;
        public const ushort A2G_Login = 22005;
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
        public const ushort G2P_OperateOnlineArcadeSafeCheckpoint = 22142;
        public const ushort P2G_OperateOnlineArcadeSafeCheckpoint = 22143;
        public const ushort P2R_OperateOnlineArcadeSafeCheckpoint = 22144;
        public const ushort R2P_OperateOnlineArcadeSafeCheckpoint = 22145;
        public const ushort R2P_OnlineArcadeRunChanged = 22146;
        public const ushort R2R_OnlineArcadeConnectionChanged = 22147;
        public const ushort P2G_OnlineArcadeRunChanged = 22148;
        public const ushort G2P_SubmitOnlineArcadeInput = 22149;
        public const ushort P2G_SubmitOnlineArcadeInput = 22150;
        public const ushort P2R_SubmitOnlineArcadeInput = 22151;
        public const ushort R2P_SubmitOnlineArcadeInput = 22152;
        public const ushort G2R_ExecuteOnlineArcadeTestCmd = 22153;
        public const ushort R2G_ExecuteOnlineArcadeTestCmd = 22154;
        public const ushort G2P_OperateOnlineSlayerMart = 22155;
        public const ushort P2G_OperateOnlineSlayerMart = 22156;
        public const ushort P2R_OperateOnlineSlayerMart = 22157;
        public const ushort R2P_OperateOnlineSlayerMart = 22158;
        public const ushort G2A_ValidateAccountSession = 22990;
        public const ushort A2G_ValidateAccountSession = 22991;
    }
}