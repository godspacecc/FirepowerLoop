using MemoryPack;
using System.Collections.Generic;

namespace ET
{
    [MemoryPackable]
    [Message(OnlineOuter.C2G_Register)]
    [ResponseType(nameof(G2C_Register))]
    public partial class C2G_Register : MessageObject, ISessionRequest
    {
        public static C2G_Register Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2G_Register), isFromPool) as C2G_Register;
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
    [Message(OnlineOuter.G2C_Register)]
    public partial class G2C_Register : MessageObject, ISessionResponse
    {
        public static G2C_Register Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_Register), isFromPool) as G2C_Register;
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
    [Message(OnlineOuter.C2G_Login)]
    [ResponseType(nameof(G2C_Login))]
    public partial class C2G_Login : MessageObject, ISessionRequest
    {
        public static C2G_Login Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2G_Login), isFromPool) as C2G_Login;
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
    [Message(OnlineOuter.G2C_Login)]
    public partial class G2C_Login : MessageObject, ISessionResponse
    {
        public static G2C_Login Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_Login), isFromPool) as G2C_Login;
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
        public long SessionEpoch { get; set; }

        [MemoryPackOrder(6)]
        public bool IsPlayerActorReused { get; set; }

        [MemoryPackOrder(7)]
        public bool IsNewAccount { get; set; }

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
            this.SessionEpoch = default;
            this.IsPlayerActorReused = default;
            this.IsNewAccount = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.C2G_Logout)]
    [ResponseType(nameof(G2C_Logout))]
    public partial class C2G_Logout : MessageObject, ISessionRequest
    {
        public static C2G_Logout Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2G_Logout), isFromPool) as C2G_Logout;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.G2C_Logout)]
    public partial class G2C_Logout : MessageObject, ISessionResponse
    {
        public static G2C_Logout Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_Logout), isFromPool) as G2C_Logout;
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
    [Message(OnlineOuter.G2C_SessionReplaced)]
    public partial class G2C_SessionReplaced : MessageObject, ISessionMessage
    {
        public static G2C_SessionReplaced Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_SessionReplaced), isFromPool) as G2C_SessionReplaced;
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
    [Message(OnlineOuter.RoomMemberInfo)]
    public partial class RoomMemberInfo : MessageObject
    {
        public static RoomMemberInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(RoomMemberInfo), isFromPool) as RoomMemberInfo;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public string DisplayName { get; set; }

        [MemoryPackOrder(2)]
        public bool IsOwner { get; set; }

        [MemoryPackOrder(3)]
        public int ConnectionState { get; set; }

        [MemoryPackOrder(4)]
        public long JoinSequence { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.DisplayName = default;
            this.IsOwner = default;
            this.ConnectionState = default;
            this.JoinSequence = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineRoomSnapshot)]
    public partial class OnlineRoomSnapshot : MessageObject
    {
        public static OnlineRoomSnapshot Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineRoomSnapshot), isFromPool) as OnlineRoomSnapshot;
        }

        [MemoryPackOrder(0)]
        public long RoomId { get; set; }

        [MemoryPackOrder(1)]
        public string RoomCode { get; set; }

        [MemoryPackOrder(2)]
        public long OwnerPlayerId { get; set; }

        [MemoryPackOrder(3)]
        public long MembershipRevision { get; set; }

        [MemoryPackOrder(4)]
        public List<RoomMemberInfo> Members { get; set; } = new();

        [MemoryPackOrder(5)]
        public int Purpose { get; set; }

        [MemoryPackOrder(6)]
        public int Visibility { get; set; }

        [MemoryPackOrder(7)]
        public int Capacity { get; set; }

        [MemoryPackOrder(8)]
        public bool MembershipLocked { get; set; }

        [MemoryPackOrder(9)]
        public string ClientVersion { get; set; }

        [MemoryPackOrder(10)]
        public string ArcadeConfigFingerprint { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RoomId = default;
            this.RoomCode = default;
            this.OwnerPlayerId = default;
            this.MembershipRevision = default;
            this.Members.Clear();
            this.Purpose = default;
            this.Visibility = default;
            this.Capacity = default;
            this.MembershipLocked = default;
            this.ClientVersion = default;
            this.ArcadeConfigFingerprint = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.C2G_CreateRoom)]
    [ResponseType(nameof(G2C_CreateRoom))]
    public partial class C2G_CreateRoom : MessageObject, ISessionRequest
    {
        public static C2G_CreateRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2G_CreateRoom), isFromPool) as C2G_CreateRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public string OperationId { get; set; }

        [MemoryPackOrder(2)]
        public int Purpose { get; set; }

        [MemoryPackOrder(3)]
        public int Visibility { get; set; }

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
            this.OperationId = default;
            this.Purpose = default;
            this.Visibility = default;
            this.ClientVersion = default;
            this.ArcadeConfigFingerprint = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.G2C_CreateRoom)]
    public partial class G2C_CreateRoom : MessageObject, ISessionResponse
    {
        public static G2C_CreateRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_CreateRoom), isFromPool) as G2C_CreateRoom;
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
    [Message(OnlineOuter.C2G_MatchArcadeRoom)]
    [ResponseType(nameof(G2C_MatchArcadeRoom))]
    public partial class C2G_MatchArcadeRoom : MessageObject, ISessionRequest
    {
        public static C2G_MatchArcadeRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2G_MatchArcadeRoom), isFromPool) as C2G_MatchArcadeRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public string OperationId { get; set; }

        [MemoryPackOrder(2)]
        public string ClientVersion { get; set; }

        [MemoryPackOrder(3)]
        public string ArcadeConfigFingerprint { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.OperationId = default;
            this.ClientVersion = default;
            this.ArcadeConfigFingerprint = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.G2C_MatchArcadeRoom)]
    public partial class G2C_MatchArcadeRoom : MessageObject, ISessionResponse
    {
        public static G2C_MatchArcadeRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_MatchArcadeRoom), isFromPool) as G2C_MatchArcadeRoom;
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
    [Message(OnlineOuter.C2G_JoinRoom)]
    [ResponseType(nameof(G2C_JoinRoom))]
    public partial class C2G_JoinRoom : MessageObject, ISessionRequest
    {
        public static C2G_JoinRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2G_JoinRoom), isFromPool) as C2G_JoinRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public string OperationId { get; set; }

        [MemoryPackOrder(2)]
        public string RoomCode { get; set; }

        [MemoryPackOrder(3)]
        public int ExpectedPurpose { get; set; }

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
            this.OperationId = default;
            this.RoomCode = default;
            this.ExpectedPurpose = default;
            this.ClientVersion = default;
            this.ArcadeConfigFingerprint = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.G2C_JoinRoom)]
    public partial class G2C_JoinRoom : MessageObject, ISessionResponse
    {
        public static G2C_JoinRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_JoinRoom), isFromPool) as G2C_JoinRoom;
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
    [Message(OnlineOuter.C2G_LeaveRoom)]
    [ResponseType(nameof(G2C_LeaveRoom))]
    public partial class C2G_LeaveRoom : MessageObject, ISessionRequest
    {
        public static C2G_LeaveRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2G_LeaveRoom), isFromPool) as C2G_LeaveRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public string OperationId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.OperationId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.G2C_LeaveRoom)]
    public partial class G2C_LeaveRoom : MessageObject, ISessionResponse
    {
        public static G2C_LeaveRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_LeaveRoom), isFromPool) as G2C_LeaveRoom;
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
    [Message(OnlineOuter.C2G_GetCurrentRoom)]
    [ResponseType(nameof(G2C_GetCurrentRoom))]
    public partial class C2G_GetCurrentRoom : MessageObject, ISessionRequest
    {
        public static C2G_GetCurrentRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2G_GetCurrentRoom), isFromPool) as C2G_GetCurrentRoom;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.G2C_GetCurrentRoom)]
    public partial class G2C_GetCurrentRoom : MessageObject, ISessionResponse
    {
        public static G2C_GetCurrentRoom Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_GetCurrentRoom), isFromPool) as G2C_GetCurrentRoom;
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
    [Message(OnlineOuter.G2C_RoomChanged)]
    public partial class G2C_RoomChanged : MessageObject, ISessionMessage
    {
        public static G2C_RoomChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_RoomChanged), isFromPool) as G2C_RoomChanged;
        }

        [MemoryPackOrder(0)]
        public bool HasRoom { get; set; }

        [MemoryPackOrder(1)]
        public OnlineRoomSnapshot Room { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.HasRoom = default;
            this.Room = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.RoomChatMessageInfo)]
    public partial class RoomChatMessageInfo : MessageObject
    {
        public static RoomChatMessageInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(RoomChatMessageInfo), isFromPool) as RoomChatMessageInfo;
        }

        [MemoryPackOrder(0)]
        public string ChatMessageId { get; set; }

        [MemoryPackOrder(1)]
        public long ChatSequence { get; set; }

        [MemoryPackOrder(2)]
        public long SenderPlayerId { get; set; }

        [MemoryPackOrder(3)]
        public string SenderDisplayName { get; set; }

        [MemoryPackOrder(4)]
        public long ServerTime { get; set; }

        [MemoryPackOrder(5)]
        public string Content { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.ChatMessageId = default;
            this.ChatSequence = default;
            this.SenderPlayerId = default;
            this.SenderDisplayName = default;
            this.ServerTime = default;
            this.Content = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.C2G_SendRoomChat)]
    [ResponseType(nameof(G2C_SendRoomChat))]
    public partial class C2G_SendRoomChat : MessageObject, ISessionRequest
    {
        public static C2G_SendRoomChat Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2G_SendRoomChat), isFromPool) as C2G_SendRoomChat;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public string ClientMessageId { get; set; }

        [MemoryPackOrder(2)]
        public string Content { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.ClientMessageId = default;
            this.Content = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.G2C_SendRoomChat)]
    public partial class G2C_SendRoomChat : MessageObject, ISessionResponse
    {
        public static G2C_SendRoomChat Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_SendRoomChat), isFromPool) as G2C_SendRoomChat;
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
    [Message(OnlineOuter.C2G_GetRoomChatHistory)]
    [ResponseType(nameof(G2C_GetRoomChatHistory))]
    public partial class C2G_GetRoomChatHistory : MessageObject, ISessionRequest
    {
        public static C2G_GetRoomChatHistory Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2G_GetRoomChatHistory), isFromPool) as C2G_GetRoomChatHistory;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.G2C_GetRoomChatHistory)]
    public partial class G2C_GetRoomChatHistory : MessageObject, ISessionResponse
    {
        public static G2C_GetRoomChatHistory Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_GetRoomChatHistory), isFromPool) as G2C_GetRoomChatHistory;
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
    [Message(OnlineOuter.G2C_RoomChatMessage)]
    public partial class G2C_RoomChatMessage : MessageObject, ISessionMessage
    {
        public static G2C_RoomChatMessage Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_RoomChatMessage), isFromPool) as G2C_RoomChatMessage;
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
    [Message(OnlineOuter.ServerTestCmdCategoryInfo)]
    public partial class ServerTestCmdCategoryInfo : MessageObject
    {
        public static ServerTestCmdCategoryInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(ServerTestCmdCategoryInfo), isFromPool) as ServerTestCmdCategoryInfo;
        }

        [MemoryPackOrder(0)]
        public string Id { get; set; }

        [MemoryPackOrder(1)]
        public int Order { get; set; }

        [MemoryPackOrder(2)]
        public string LabelText { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.Id = default;
            this.Order = default;
            this.LabelText = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.ServerTestCmdEntryInfo)]
    public partial class ServerTestCmdEntryInfo : MessageObject
    {
        public static ServerTestCmdEntryInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(ServerTestCmdEntryInfo), isFromPool) as ServerTestCmdEntryInfo;
        }

        [MemoryPackOrder(0)]
        public string Id { get; set; }

        [MemoryPackOrder(1)]
        public string CategoryId { get; set; }

        [MemoryPackOrder(2)]
        public int Order { get; set; }

        [MemoryPackOrder(3)]
        public string LabelText { get; set; }

        [MemoryPackOrder(4)]
        public string CommandTemplate { get; set; }

        [MemoryPackOrder(5)]
        public string DescriptionText { get; set; }

        [MemoryPackOrder(6)]
        public string UsageText { get; set; }

        [MemoryPackOrder(7)]
        public string ExampleText { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.Id = default;
            this.CategoryId = default;
            this.Order = default;
            this.LabelText = default;
            this.CommandTemplate = default;
            this.DescriptionText = default;
            this.UsageText = default;
            this.ExampleText = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.C2G_GetServerTestCmdCatalog)]
    [ResponseType(nameof(G2C_GetServerTestCmdCatalog))]
    public partial class C2G_GetServerTestCmdCatalog : MessageObject, ISessionRequest
    {
        public static C2G_GetServerTestCmdCatalog Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2G_GetServerTestCmdCatalog), isFromPool) as C2G_GetServerTestCmdCatalog;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.G2C_GetServerTestCmdCatalog)]
    public partial class G2C_GetServerTestCmdCatalog : MessageObject, ISessionResponse
    {
        public static G2C_GetServerTestCmdCatalog Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_GetServerTestCmdCatalog), isFromPool) as G2C_GetServerTestCmdCatalog;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public List<ServerTestCmdCategoryInfo> Categories { get; set; } = new();

        [MemoryPackOrder(4)]
        public List<ServerTestCmdEntryInfo> Entries { get; set; } = new();

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.Error = default;
            this.Message = default;
            this.Categories.Clear();
            this.Entries.Clear();

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.C2G_ExecuteServerTestCmd)]
    [ResponseType(nameof(G2C_ExecuteServerTestCmd))]
    public partial class C2G_ExecuteServerTestCmd : MessageObject, ISessionRequest
    {
        public static C2G_ExecuteServerTestCmd Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2G_ExecuteServerTestCmd), isFromPool) as C2G_ExecuteServerTestCmd;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public string CommandText { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.CommandText = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.G2C_ExecuteServerTestCmd)]
    public partial class G2C_ExecuteServerTestCmd : MessageObject, ISessionResponse
    {
        public static G2C_ExecuteServerTestCmd Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_ExecuteServerTestCmd), isFromPool) as G2C_ExecuteServerTestCmd;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int Error { get; set; }

        [MemoryPackOrder(2)]
        public string Message { get; set; }

        [MemoryPackOrder(3)]
        public bool Recognized { get; set; }

        [MemoryPackOrder(4)]
        public bool Success { get; set; }

        [MemoryPackOrder(5)]
        public int ResultCode { get; set; }

        [MemoryPackOrder(6)]
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
            this.Recognized = default;
            this.Success = default;
            this.ResultCode = default;
            this.ServerTime = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.PublicRoomSummary)]
    public partial class PublicRoomSummary : MessageObject
    {
        public static PublicRoomSummary Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(PublicRoomSummary), isFromPool) as PublicRoomSummary;
        }

        [MemoryPackOrder(0)]
        public string RoomCode { get; set; }

        [MemoryPackOrder(1)]
        public string OwnerDisplayName { get; set; }

        [MemoryPackOrder(2)]
        public int MemberCount { get; set; }

        [MemoryPackOrder(3)]
        public int Capacity { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RoomCode = default;
            this.OwnerDisplayName = default;
            this.MemberCount = default;
            this.Capacity = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.C2G_GetPublicRoomList)]
    [ResponseType(nameof(G2C_GetPublicRoomList))]
    public partial class C2G_GetPublicRoomList : MessageObject, ISessionRequest
    {
        public static C2G_GetPublicRoomList Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2G_GetPublicRoomList), isFromPool) as C2G_GetPublicRoomList;
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
    [Message(OnlineOuter.G2C_GetPublicRoomList)]
    public partial class G2C_GetPublicRoomList : MessageObject, ISessionResponse
    {
        public static G2C_GetPublicRoomList Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_GetPublicRoomList), isFromPool) as G2C_GetPublicRoomList;
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

    public static class OnlineOuter
    {
        public const ushort C2G_Register = 12002;
        public const ushort G2C_Register = 12003;
        public const ushort C2G_Login = 12004;
        public const ushort G2C_Login = 12005;
        public const ushort C2G_Logout = 12006;
        public const ushort G2C_Logout = 12007;
        public const ushort G2C_SessionReplaced = 12008;
        public const ushort RoomMemberInfo = 12009;
        public const ushort OnlineRoomSnapshot = 12010;
        public const ushort C2G_CreateRoom = 12011;
        public const ushort G2C_CreateRoom = 12012;
        public const ushort C2G_MatchArcadeRoom = 12013;
        public const ushort G2C_MatchArcadeRoom = 12014;
        public const ushort C2G_JoinRoom = 12015;
        public const ushort G2C_JoinRoom = 12016;
        public const ushort C2G_LeaveRoom = 12017;
        public const ushort G2C_LeaveRoom = 12018;
        public const ushort C2G_GetCurrentRoom = 12019;
        public const ushort G2C_GetCurrentRoom = 12020;
        public const ushort G2C_RoomChanged = 12021;
        public const ushort RoomChatMessageInfo = 12022;
        public const ushort C2G_SendRoomChat = 12023;
        public const ushort G2C_SendRoomChat = 12024;
        public const ushort C2G_GetRoomChatHistory = 12025;
        public const ushort G2C_GetRoomChatHistory = 12026;
        public const ushort G2C_RoomChatMessage = 12027;
        public const ushort ServerTestCmdCategoryInfo = 12067;
        public const ushort ServerTestCmdEntryInfo = 12068;
        public const ushort C2G_GetServerTestCmdCatalog = 12069;
        public const ushort G2C_GetServerTestCmdCatalog = 12070;
        public const ushort C2G_ExecuteServerTestCmd = 12071;
        public const ushort G2C_ExecuteServerTestCmd = 12072;
        public const ushort PublicRoomSummary = 12073;
        public const ushort C2G_GetPublicRoomList = 12074;
        public const ushort G2C_GetPublicRoomList = 12075;
    }
}