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

    [MemoryPackable]
    [Message(OnlineOuter.OnlineArcadeOwnedAmountInfo)]
    public partial class OnlineArcadeOwnedAmountInfo : MessageObject
    {
        public static OnlineArcadeOwnedAmountInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineArcadeOwnedAmountInfo), isFromPool) as OnlineArcadeOwnedAmountInfo;
        }

        [MemoryPackOrder(0)]
        public string Id { get; set; }

        [MemoryPackOrder(1)]
        public int Amount { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.Id = default;
            this.Amount = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineArcadeEquipmentInfo)]
    public partial class OnlineArcadeEquipmentInfo : MessageObject
    {
        public static OnlineArcadeEquipmentInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineArcadeEquipmentInfo), isFromPool) as OnlineArcadeEquipmentInfo;
        }

        [MemoryPackOrder(0)]
        public string SlotId { get; set; }

        [MemoryPackOrder(1)]
        public string ProductId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.SlotId = default;
            this.ProductId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineArcadePowerupInfo)]
    public partial class OnlineArcadePowerupInfo : MessageObject
    {
        public static OnlineArcadePowerupInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineArcadePowerupInfo), isFromPool) as OnlineArcadePowerupInfo;
        }

        [MemoryPackOrder(0)]
        public string ProductId { get; set; }

        [MemoryPackOrder(1)]
        public float RemainingSeconds { get; set; }

        [MemoryPackOrder(2)]
        public int Charges { get; set; }

        [MemoryPackOrder(3)]
        public float PositionX { get; set; }

        [MemoryPackOrder(4)]
        public float PositionZ { get; set; }

        /// <summary>
        /// 强化最后一次实际发射方向；未开火时为零，仅用于哨塔和战斗鸡表现。
        /// </summary>
        [MemoryPackOrder(5)]
        public float ShotDirectionX { get; set; }

        [MemoryPackOrder(6)]
        public float ShotDirectionZ { get; set; }

        /// <summary>
        /// 无人机权威阶段和当前红紫强度；重连只投影当前阶段。
        /// </summary>
        [MemoryPackOrder(7)]
        public int DronePhase { get; set; }

        [MemoryPackOrder(8)]
        public int DronePowerTier { get; set; }

        /// <summary>
        /// 紫级第二架无人机的权威位置与阶段，未激活时HasExtraDrone=false。
        /// </summary>
        [MemoryPackOrder(9)]
        public bool HasExtraDrone { get; set; }

        [MemoryPackOrder(10)]
        public float ExtraDroneX { get; set; }

        [MemoryPackOrder(11)]
        public float ExtraDroneZ { get; set; }

        [MemoryPackOrder(12)]
        public int ExtraDronePhase { get; set; }

        [MemoryPackOrder(13)]
        public float ExtraDroneDirectionX { get; set; }

        [MemoryPackOrder(14)]
        public float ExtraDroneDirectionZ { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.ProductId = default;
            this.RemainingSeconds = default;
            this.Charges = default;
            this.PositionX = default;
            this.PositionZ = default;
            this.ShotDirectionX = default;
            this.ShotDirectionZ = default;
            this.DronePhase = default;
            this.DronePowerTier = default;
            this.HasExtraDrone = default;
            this.ExtraDroneX = default;
            this.ExtraDroneZ = default;
            this.ExtraDronePhase = default;
            this.ExtraDroneDirectionX = default;
            this.ExtraDroneDirectionZ = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineArcadeBattlePlayerInfo)]
    public partial class OnlineArcadeBattlePlayerInfo : MessageObject
    {
        public static OnlineArcadeBattlePlayerInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineArcadeBattlePlayerInfo), isFromPool) as OnlineArcadeBattlePlayerInfo;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public float PositionX { get; set; }

        [MemoryPackOrder(2)]
        public float PositionZ { get; set; }

        [MemoryPackOrder(3)]
        public float AimX { get; set; }

        [MemoryPackOrder(4)]
        public float AimZ { get; set; }

        [MemoryPackOrder(5)]
        public int CurrentHealth { get; set; }

        [MemoryPackOrder(6)]
        public int MaxHealth { get; set; }

        [MemoryPackOrder(7)]
        public bool Alive { get; set; }

        [MemoryPackOrder(8)]
        public bool Invulnerable { get; set; }

        [MemoryPackOrder(9)]
        public float RespawnSeconds { get; set; }

        [MemoryPackOrder(10)]
        public string TemporaryWeaponId { get; set; }

        [MemoryPackOrder(11)]
        public float TemporaryWeaponSeconds { get; set; }

        [MemoryPackOrder(12)]
        public string SupportId { get; set; }

        [MemoryPackOrder(13)]
        public float SupportSeconds { get; set; }

        [MemoryPackOrder(14)]
        public float DashSeconds { get; set; }

        [MemoryPackOrder(15)]
        public List<OnlineArcadePowerupInfo> Powerups { get; set; } = new();

        /// <summary>
        /// 本场成功攻击序列，客户端仅用于动作去重。
        /// </summary>
        [MemoryPackOrder(16)]
        public int FireSequence { get; set; }

        [MemoryPackOrder(17)]
        public bool InTreasureRoom { get; set; }

        [MemoryPackOrder(18)]
        public int VestSlots { get; set; }

        [MemoryPackOrder(19)]
        public int Plates { get; set; }

        [MemoryPackOrder(20)]
        public bool HasStorm { get; set; }

        [MemoryPackOrder(21)]
        public int StormCharge { get; set; }

        [MemoryPackOrder(22)]
        public float StormSeconds { get; set; }

        [MemoryPackOrder(23)]
        public List<OnlineSlayerMartSupportInfo> SlayerMartSupports { get; set; } = new();

        [MemoryPackOrder(24)]
        public float DownedSeconds { get; set; }

        [MemoryPackOrder(25)]
        public float RescueRate { get; set; }

        /// <summary>
        /// 本次冲刺锁定方向，客户端预测使用同一权威向量。
        /// </summary>
        [MemoryPackOrder(26)]
        public float DashDirectionX { get; set; }

        [MemoryPackOrder(27)]
        public float DashDirectionZ { get; set; }

        /// <summary>
        /// 当前牛撞的权威剩余量；重连直接恢复飞行或落地僵直，不重播。
        /// </summary>
        [MemoryPackOrder(28)]
        public float HerdImpactRemaining { get; set; }

        [MemoryPackOrder(29)]
        public float HerdImpactDirectionX { get; set; }

        [MemoryPackOrder(30)]
        public float HerdImpactDirectionZ { get; set; }

        /// <summary>
        /// 实际掉血的牛撞反馈，包含致死；序列只增，位置在死亡/复活变更前记录。
        /// </summary>
        [MemoryPackOrder(31)]
        public long HerdContactSequence { get; set; }

        [MemoryPackOrder(32)]
        public float HerdContactX { get; set; }

        [MemoryPackOrder(33)]
        public float HerdContactZ { get; set; }

        [MemoryPackOrder(34)]
        public double HerdContactElapsedSeconds { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.PositionX = default;
            this.PositionZ = default;
            this.AimX = default;
            this.AimZ = default;
            this.CurrentHealth = default;
            this.MaxHealth = default;
            this.Alive = default;
            this.Invulnerable = default;
            this.RespawnSeconds = default;
            this.TemporaryWeaponId = default;
            this.TemporaryWeaponSeconds = default;
            this.SupportId = default;
            this.SupportSeconds = default;
            this.DashSeconds = default;
            this.Powerups.Clear();
            this.FireSequence = default;
            this.InTreasureRoom = default;
            this.VestSlots = default;
            this.Plates = default;
            this.HasStorm = default;
            this.StormCharge = default;
            this.StormSeconds = default;
            this.SlayerMartSupports.Clear();
            this.DownedSeconds = default;
            this.RescueRate = default;
            this.DashDirectionX = default;
            this.DashDirectionZ = default;
            this.HerdImpactRemaining = default;
            this.HerdImpactDirectionX = default;
            this.HerdImpactDirectionZ = default;
            this.HerdContactSequence = default;
            this.HerdContactX = default;
            this.HerdContactZ = default;
            this.HerdContactElapsedSeconds = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineArcadeEnemyInfo)]
    public partial class OnlineArcadeEnemyInfo : MessageObject
    {
        public static OnlineArcadeEnemyInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineArcadeEnemyInfo), isFromPool) as OnlineArcadeEnemyInfo;
        }

        [MemoryPackOrder(0)]
        public long RuntimeId { get; set; }

        [MemoryPackOrder(1)]
        public string EnemyConfigId { get; set; }

        [MemoryPackOrder(2)]
        public float PositionX { get; set; }

        [MemoryPackOrder(3)]
        public float PositionZ { get; set; }

        [MemoryPackOrder(4)]
        public int CurrentHealth { get; set; }

        [MemoryPackOrder(5)]
        public int MaxHealth { get; set; }

        [MemoryPackOrder(6)]
        public long TargetPlayerId { get; set; }

        [MemoryPackOrder(7)]
        public int WaveNumber { get; set; }

        [MemoryPackOrder(8)]
        public string BossId { get; set; }

        [MemoryPackOrder(9)]
        public int BossPhase { get; set; }

        [MemoryPackOrder(10)]
        public float BossPhaseSeconds { get; set; }

        /// <summary>
        /// 本轮独立护甲的剩余耐久，不计入生命。
        /// </summary>
        [MemoryPackOrder(11)]
        public int ArmorHealth { get; set; }

        [MemoryPackOrder(12)]
        public int SpecialKind { get; set; }

        [MemoryPackOrder(13)]
        public int ContentBossKind { get; set; }

        [MemoryPackOrder(14)]
        public bool ContentWarning { get; set; }

        /// <summary>
        /// 最近一次有效喷火命中的表现剩余时间，不产生持续伤害。
        /// </summary>
        [MemoryPackOrder(15)]
        public float FlameHitRemainingSeconds { get; set; }

        /// <summary>
        /// 存活敌人被牛撞后的当前飞行／落地状态。
        /// </summary>
        [MemoryPackOrder(16)]
        public float HerdImpactRemaining { get; set; }

        [MemoryPackOrder(17)]
        public float HerdImpactDirectionX { get; set; }

        [MemoryPackOrder(18)]
        public float HerdImpactDirectionZ { get; set; }

        /// <summary>
        /// 墓穴、乌鸦和蓝影只同步当前权威阶段，不在客户端重播计时。
        /// </summary>
        [MemoryPackOrder(19)]
        public int StageEnemyPhase { get; set; }

        [MemoryPackOrder(20)]
        public float StageEnemyAge { get; set; }

        [MemoryPackOrder(21)]
        public float StageEnemyOriginX { get; set; }

        [MemoryPackOrder(22)]
        public float StageEnemyOriginZ { get; set; }

        [MemoryPackOrder(23)]
        public int StageEnemyRevives { get; set; }

        /// <summary>
        /// 已完成的真实近战挥击序列；首快照只建立基线，重连不重播。
        /// </summary>
        [MemoryPackOrder(24)]
        public int AttackSequence { get; set; }

        [MemoryPackOrder(25)]
        public float MeleeWindupSeconds { get; set; }

        [MemoryPackOrder(26)]
        public float MeleeWindupRemaining { get; set; }

        [MemoryPackOrder(27)]
        public float MeleeRecoveryRemaining { get; set; }

        /// <summary>
        /// 码头海中进场：只复制当前权威进度，完成身份保留到实体结束。
        /// </summary>
        [MemoryPackOrder(28)]
        public bool DockSeaEntryStarted { get; set; }

        [MemoryPackOrder(29)]
        public float DockSeaEntryAge { get; set; }

        [MemoryPackOrder(30)]
        public float DockSeaEntryLane { get; set; }

        /// <summary>
        /// 潜地怪与犬只投影当前权威技能年龄和锁向，不由客户端积分。
        /// </summary>
        [MemoryPackOrder(31)]
        public float ContentAge { get; set; }

        [MemoryPackOrder(32)]
        public float ContentFacingX { get; set; }

        [MemoryPackOrder(33)]
        public float ContentFacingZ { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RuntimeId = default;
            this.EnemyConfigId = default;
            this.PositionX = default;
            this.PositionZ = default;
            this.CurrentHealth = default;
            this.MaxHealth = default;
            this.TargetPlayerId = default;
            this.WaveNumber = default;
            this.BossId = default;
            this.BossPhase = default;
            this.BossPhaseSeconds = default;
            this.ArmorHealth = default;
            this.SpecialKind = default;
            this.ContentBossKind = default;
            this.ContentWarning = default;
            this.FlameHitRemainingSeconds = default;
            this.HerdImpactRemaining = default;
            this.HerdImpactDirectionX = default;
            this.HerdImpactDirectionZ = default;
            this.StageEnemyPhase = default;
            this.StageEnemyAge = default;
            this.StageEnemyOriginX = default;
            this.StageEnemyOriginZ = default;
            this.StageEnemyRevives = default;
            this.AttackSequence = default;
            this.MeleeWindupSeconds = default;
            this.MeleeWindupRemaining = default;
            this.MeleeRecoveryRemaining = default;
            this.DockSeaEntryStarted = default;
            this.DockSeaEntryAge = default;
            this.DockSeaEntryLane = default;
            this.ContentAge = default;
            this.ContentFacingX = default;
            this.ContentFacingZ = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineArcadeProjectileInfo)]
    public partial class OnlineArcadeProjectileInfo : MessageObject
    {
        public static OnlineArcadeProjectileInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineArcadeProjectileInfo), isFromPool) as OnlineArcadeProjectileInfo;
        }

        [MemoryPackOrder(0)]
        public long RuntimeId { get; set; }

        [MemoryPackOrder(1)]
        public long OwnerPlayerId { get; set; }

        [MemoryPackOrder(2)]
        public float PositionX { get; set; }

        [MemoryPackOrder(3)]
        public float PositionZ { get; set; }

        [MemoryPackOrder(4)]
        public float VelocityX { get; set; }

        [MemoryPackOrder(5)]
        public float VelocityZ { get; set; }

        /// <summary>
        /// 发射时的玩家武器身份；支援和敌方弹丸为空。
        /// </summary>
        [MemoryPackOrder(6)]
        public string SourceWeaponId { get; set; }

        /// <summary>
        /// 弹丸生成时固定的头骨强化层级，0原色、1红色、2紫色。
        /// </summary>
        [MemoryPackOrder(7)]
        public int WeaponPowerLevel { get; set; }

        /// <summary>
        /// 发射时固定的宽贯穿半径与集束子弹标记，只投影实际生成的能力。
        /// </summary>
        [MemoryPackOrder(8)]
        public float EvolutionHitRadius { get; set; }

        [MemoryPackOrder(9)]
        public bool EvolutionClusterChild { get; set; }

        /// <summary>
        /// 剩余飞行寿命仅用于已采用榴弹弧线的相位，客户端不裁决引爆。
        /// </summary>
        [MemoryPackOrder(10)]
        public float RemainingSeconds { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RuntimeId = default;
            this.OwnerPlayerId = default;
            this.PositionX = default;
            this.PositionZ = default;
            this.VelocityX = default;
            this.VelocityZ = default;
            this.SourceWeaponId = default;
            this.WeaponPowerLevel = default;
            this.EvolutionHitRadius = default;
            this.EvolutionClusterChild = default;
            this.RemainingSeconds = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineArcadeDropInfo)]
    public partial class OnlineArcadeDropInfo : MessageObject
    {
        public static OnlineArcadeDropInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineArcadeDropInfo), isFromPool) as OnlineArcadeDropInfo;
        }

        [MemoryPackOrder(0)]
        public long RuntimeId { get; set; }

        [MemoryPackOrder(1)]
        public string DropConfigId { get; set; }

        [MemoryPackOrder(2)]
        public float PositionX { get; set; }

        [MemoryPackOrder(3)]
        public float PositionZ { get; set; }

        /// <summary>
        /// 只在成功补给记录中使用；普通可见掉落为零。
        /// </summary>
        [MemoryPackOrder(4)]
        public long CollectedByPlayerId { get; set; }

        /// <summary>
        /// 上限截断后的实际补充次数，不是配置奖励量。
        /// </summary>
        [MemoryPackOrder(5)]
        public int AddedAmount { get; set; }

        /// <summary>
        /// 已成功应用的积分与金币变化，彼此独立；零值不产生提示。
        /// </summary>
        [MemoryPackOrder(6)]
        public long ScoreAdded { get; set; }

        [MemoryPackOrder(7)]
        public int GoldAdded { get; set; }

        /// <summary>
        /// 固定点武器空投，普通击杀掉落不使用；剩余时间涵盖下降和自动开箱。
        /// </summary>
        [MemoryPackOrder(9)]
        public bool WeaponAirdrop { get; set; }

        [MemoryPackOrder(10)]
        public float DeliveryRemainingSeconds { get; set; }

        /// <summary>
        /// 普通可见掉落的剩余寿命；成功领取收据不使用此值。
        /// </summary>
        [MemoryPackOrder(8)]
        public float RemainingSeconds { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RuntimeId = default;
            this.DropConfigId = default;
            this.PositionX = default;
            this.PositionZ = default;
            this.CollectedByPlayerId = default;
            this.AddedAmount = default;
            this.ScoreAdded = default;
            this.GoldAdded = default;
            this.WeaponAirdrop = default;
            this.DeliveryRemainingSeconds = default;
            this.RemainingSeconds = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineArcadeBattleInfo)]
    public partial class OnlineArcadeBattleInfo : MessageObject
    {
        public static OnlineArcadeBattleInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineArcadeBattleInfo), isFromPool) as OnlineArcadeBattleInfo;
        }

        [MemoryPackOrder(0)]
        public int ArenaNumber { get; set; }

        [MemoryPackOrder(1)]
        public int WaveNumber { get; set; }

        [MemoryPackOrder(2)]
        public int WaveCount { get; set; }

        [MemoryPackOrder(3)]
        public int SpawnedInWave { get; set; }

        [MemoryPackOrder(4)]
        public int TotalInWave { get; set; }

        [MemoryPackOrder(5)]
        public int DifficultyPlayerCount { get; set; }

        [MemoryPackOrder(6)]
        public long BattleRevision { get; set; }

        [MemoryPackOrder(7)]
        public long ServerTime { get; set; }

        [MemoryPackOrder(8)]
        public List<OnlineArcadeBattlePlayerInfo> Players { get; set; } = new();

        [MemoryPackOrder(9)]
        public List<OnlineArcadeEnemyInfo> Enemies { get; set; } = new();

        [MemoryPackOrder(10)]
        public List<OnlineArcadeProjectileInfo> Projectiles { get; set; } = new();

        [MemoryPackOrder(11)]
        public List<OnlineArcadeDropInfo> Drops { get; set; } = new();

        [MemoryPackOrder(12)]
        public bool IsChallenge { get; set; }

        [MemoryPackOrder(13)]
        public string ChallengeId { get; set; }

        [MemoryPackOrder(14)]
        public float ChallengeSeconds { get; set; }

        /// <summary>
        /// 仅在活动战斗推进的机关累计秒数；客户端显示，不裁决接触伤害。
        /// </summary>
        [MemoryPackOrder(15)]
        public float HazardElapsedSeconds { get; set; }

        /// <summary>
        /// 场地配置身份与主线完成序号分离，路线跳转不改变难度进度。
        /// </summary>
        [MemoryPackOrder(16)]
        public int ArenaConfigNumber { get; set; }

        [MemoryPackOrder(17)]
        public bool IsFinalBoss { get; set; }

        [MemoryPackOrder(18)]
        public bool AwaitingExit { get; set; }

        /// <summary>
        /// 本场共享房间投影；稳定物品 ID 不携带客户端可改的奖励价值。
        /// </summary>
        [MemoryPackOrder(19)]
        public string TreasureRoomId { get; set; }

        [MemoryPackOrder(20)]
        public bool TreasureDiscovered { get; set; }

        [MemoryPackOrder(21)]
        public bool TreasureOpen { get; set; }

        [MemoryPackOrder(22)]
        public bool TreasureExpired { get; set; }

        [MemoryPackOrder(23)]
        public float TreasureSeconds { get; set; }

        [MemoryPackOrder(24)]
        public List<int> TreasureRewardIds { get; set; } = new();

        /// <summary>
        /// 完整剩余箱列表，破坏后移除；客户端不得从此列表发奖。
        /// </summary>
        [MemoryPackOrder(25)]
        public List<int> CrateIds { get; set; } = new();

        /// <summary>
        /// 本场成功补给记录；客户端按 RuntimeId 去重，仅反馈、不发奖。
        /// </summary>
        [MemoryPackOrder(26)]
        public List<OnlineArcadeDropInfo> SupplyPickups { get; set; } = new();

        /// <summary>
        /// 宝藏房预置奖励剩余寿命，与房间出口时钟独立。
        /// </summary>
        [MemoryPackOrder(27)]
        public float TreasureRewardSeconds { get; set; }

        [MemoryPackOrder(28)]
        public string RoundLayoutId { get; set; }

        [MemoryPackOrder(29)]
        public int RoundLayoutVersion { get; set; }

        [MemoryPackOrder(30)]
        public int RoundGeneration { get; set; }

        [MemoryPackOrder(31)]
        public bool RoundLoading { get; set; }

        [MemoryPackOrder(32)]
        public string RoundLoadError { get; set; }

        [MemoryPackOrder(33)]
        public float ContentElapsed { get; set; }

        [MemoryPackOrder(34)]
        public List<OnlineArcadeContentAreaInfo> ContentAreas { get; set; } = new();

        [MemoryPackOrder(35)]
        public bool RoundBossEncounter { get; set; }

        [MemoryPackOrder(36)]
        public bool RoundBossDefeated { get; set; }

        [MemoryPackOrder(37)]
        public int SlayerMartRulesVersion { get; set; }

        [MemoryPackOrder(38)]
        public bool SlayerMartActive { get; set; }

        [MemoryPackOrder(39)]
        public int SlayerMartVisit { get; set; }

        [MemoryPackOrder(40)]
        public int SlayerMartGeneration { get; set; }

        [MemoryPackOrder(41)]
        public bool SlayerMartReady { get; set; }

        [MemoryPackOrder(42)]
        public bool SlayerMartClosing { get; set; }

        [MemoryPackOrder(43)]
        public float SlayerMartSeconds { get; set; }

        [MemoryPackOrder(44)]
        public List<OnlineSlayerMartMemberInfo> SlayerMartMembers { get; set; } = new();

        [MemoryPackOrder(45)]
        public List<int> SlayerMartCashBoxIds { get; set; } = new();

        [MemoryPackOrder(46)]
        public long SlayerMartRevision { get; set; }

        [MemoryPackOrder(47)]
        public bool SlayerMartPrepared { get; set; }

        /// <summary>
        /// 独立Slayer挑战公共权威状态；现金为每位原始成员同额收集额，不公开个人购物余额。
        /// </summary>
        [MemoryPackOrder(48)]
        public int SlayerPhase { get; set; }

        [MemoryPackOrder(49)]
        public int SlayerDowns { get; set; }

        [MemoryPackOrder(50)]
        public int SlayerDownLimit { get; set; }

        [MemoryPackOrder(51)]
        public long SlayerCashCollected { get; set; }

        [MemoryPackOrder(52)]
        public List<long> SlayerExitVotes { get; set; } = new();

        [MemoryPackOrder(53)]
        public List<long> SlayerEnterReady { get; set; } = new();

        [MemoryPackOrder(54)]
        public List<long> SlayerSkipReady { get; set; } = new();

        [MemoryPackOrder(55)]
        public int SlayerOutcome { get; set; }

        /// <summary>
        /// 成功炸弹的有界表现记录；只读，不改变伤害判定。
        /// </summary>
        [MemoryPackOrder(56)]
        public List<OnlineArcadeBombBlastInfo> BombBlasts { get; set; } = new();

        /// <summary>
        /// 只读死亡表现事实；客户端不据此裁决伤害或同步骨骼。
        /// </summary>
        [MemoryPackOrder(57)]
        public List<OnlineArcadeEnemyDeathInfo> EnemyDeaths { get; set; } = new();

        /// <summary>
        /// 每轮金币现场只读投影；领取由服务器裁决。
        /// </summary>
        [MemoryPackOrder(58)]
        public OnlineArcadeRoundRewardInfo RoundReward { get; set; }

        [MemoryPackOrder(59)]
        public float StagePortalSeconds { get; set; }

        [MemoryPackOrder(60)]
        public float StagePortalX { get; set; }

        [MemoryPackOrder(61)]
        public float StagePortalZ { get; set; }

        /// <summary>
        /// 地图独占牛群姿态；仅权威端推进与结算伤害。
        /// </summary>
        [MemoryPackOrder(62)]
        public List<OnlineArcadeHerdInfo> Herds { get; set; } = new();

        /// <summary>
        /// 已裁决电击的有界记录；客户端不能据此再扣血。
        /// </summary>
        [MemoryPackOrder(63)]
        public List<OnlineArcadeElectricHitInfo> ElectricHits { get; set; } = new();

        /// <summary>
        /// 当前轮真实武器碰撞反馈，客户端不从弹丸消失推断命中。
        /// </summary>
        [MemoryPackOrder(64)]
        public List<OnlineArcadeWeaponFeedbackInfo> WeaponFeedback { get; set; } = new();

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.ArenaNumber = default;
            this.WaveNumber = default;
            this.WaveCount = default;
            this.SpawnedInWave = default;
            this.TotalInWave = default;
            this.DifficultyPlayerCount = default;
            this.BattleRevision = default;
            this.ServerTime = default;
            this.Players.Clear();
            this.Enemies.Clear();
            this.Projectiles.Clear();
            this.Drops.Clear();
            this.IsChallenge = default;
            this.ChallengeId = default;
            this.ChallengeSeconds = default;
            this.HazardElapsedSeconds = default;
            this.ArenaConfigNumber = default;
            this.IsFinalBoss = default;
            this.AwaitingExit = default;
            this.TreasureRoomId = default;
            this.TreasureDiscovered = default;
            this.TreasureOpen = default;
            this.TreasureExpired = default;
            this.TreasureSeconds = default;
            this.TreasureRewardIds.Clear();
            this.CrateIds.Clear();
            this.SupplyPickups.Clear();
            this.TreasureRewardSeconds = default;
            this.RoundLayoutId = default;
            this.RoundLayoutVersion = default;
            this.RoundGeneration = default;
            this.RoundLoading = default;
            this.RoundLoadError = default;
            this.ContentElapsed = default;
            this.ContentAreas.Clear();
            this.RoundBossEncounter = default;
            this.RoundBossDefeated = default;
            this.SlayerMartRulesVersion = default;
            this.SlayerMartActive = default;
            this.SlayerMartVisit = default;
            this.SlayerMartGeneration = default;
            this.SlayerMartReady = default;
            this.SlayerMartClosing = default;
            this.SlayerMartSeconds = default;
            this.SlayerMartMembers.Clear();
            this.SlayerMartCashBoxIds.Clear();
            this.SlayerMartRevision = default;
            this.SlayerMartPrepared = default;
            this.SlayerPhase = default;
            this.SlayerDowns = default;
            this.SlayerDownLimit = default;
            this.SlayerCashCollected = default;
            this.SlayerExitVotes.Clear();
            this.SlayerEnterReady.Clear();
            this.SlayerSkipReady.Clear();
            this.SlayerOutcome = default;
            this.BombBlasts.Clear();
            this.EnemyDeaths.Clear();
            this.RoundReward = default;
            this.StagePortalSeconds = default;
            this.StagePortalX = default;
            this.StagePortalZ = default;
            this.Herds.Clear();
            this.ElectricHits.Clear();
            this.WeaponFeedback.Clear();

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineArcadePlayerInfo)]
    public partial class OnlineArcadePlayerInfo : MessageObject
    {
        public static OnlineArcadePlayerInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineArcadePlayerInfo), isFromPool) as OnlineArcadePlayerInfo;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public string DisplayName { get; set; }

        [MemoryPackOrder(2)]
        public int Lives { get; set; }

        [MemoryPackOrder(3)]
        public int RunGold { get; set; }

        [MemoryPackOrder(4)]
        public int Bombs { get; set; }

        [MemoryPackOrder(5)]
        public int Dashes { get; set; }

        [MemoryPackOrder(6)]
        public string WeaponId { get; set; }

        [MemoryPackOrder(7)]
        public bool Ready { get; set; }

        [MemoryPackOrder(9)]
        public List<OnlineArcadeOwnedAmountInfo> UpgradeStacks { get; set; } = new();

        [MemoryPackOrder(10)]
        public List<OnlineArcadeOwnedAmountInfo> Consumables { get; set; } = new();

        [MemoryPackOrder(11)]
        public List<OnlineArcadeEquipmentInfo> Equipment { get; set; } = new();

        [MemoryPackOrder(13)]
        public bool SettlementConfirmed { get; set; }

        [MemoryPackOrder(14)]
        public bool AbandonConfirmed { get; set; }

        [MemoryPackOrder(15)]
        public int SettlementState { get; set; }

        [MemoryPackOrder(16)]
        public bool ChallengeReady { get; set; }

        [MemoryPackOrder(17)]
        public long Score { get; set; }

        [MemoryPackOrder(18)]
        public int ScoreMultiplier { get; set; }

        [MemoryPackOrder(19)]
        public int MultiplierProgress { get; set; }

        [MemoryPackOrder(20)]
        public bool CanReadyForArena { get; set; }

        /// <summary>
        /// 本人的商店只读账本，由权威服务器随个人积分发布。
        /// </summary>
        [MemoryPackOrder(21)]
        public string ScoreShopJson { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.DisplayName = default;
            this.Lives = default;
            this.RunGold = default;
            this.Bombs = default;
            this.Dashes = default;
            this.WeaponId = default;
            this.Ready = default;
            this.UpgradeStacks.Clear();
            this.Consumables.Clear();
            this.Equipment.Clear();
            this.SettlementConfirmed = default;
            this.AbandonConfirmed = default;
            this.SettlementState = default;
            this.ChallengeReady = default;
            this.Score = default;
            this.ScoreMultiplier = default;
            this.MultiplierProgress = default;
            this.CanReadyForArena = default;
            this.ScoreShopJson = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineArcadeConnectionInfo)]
    public partial class OnlineArcadeConnectionInfo : MessageObject
    {
        public static OnlineArcadeConnectionInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineArcadeConnectionInfo), isFromPool) as OnlineArcadeConnectionInfo;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public bool Connected { get; set; }

        [MemoryPackOrder(2)]
        public long DisconnectedAtServerTime { get; set; }

        [MemoryPackOrder(3)]
        public long ArenaReconnectDeadlineServerTime { get; set; }

        [MemoryPackOrder(4)]
        public long SoloEndAvailableAtServerTime { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.Connected = default;
            this.DisconnectedAtServerTime = default;
            this.ArenaReconnectDeadlineServerTime = default;
            this.SoloEndAvailableAtServerTime = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineArcadeRunSnapshot)]
    public partial class OnlineArcadeRunSnapshot : MessageObject
    {
        public static OnlineArcadeRunSnapshot Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineArcadeRunSnapshot), isFromPool) as OnlineArcadeRunSnapshot;
        }

        [MemoryPackOrder(0)]
        public long RunId { get; set; }

        [MemoryPackOrder(1)]
        public long RoomId { get; set; }

        [MemoryPackOrder(2)]
        public long Revision { get; set; }

        [MemoryPackOrder(3)]
        public int Phase { get; set; }

        [MemoryPackOrder(4)]
        public int CompletedArena { get; set; }

        [MemoryPackOrder(5)]
        public int NextArena { get; set; }

        [MemoryPackOrder(6)]
        public string ConfigFingerprint { get; set; }

        [MemoryPackOrder(7)]
        public List<OnlineArcadePlayerInfo> Players { get; set; } = new();

        [MemoryPackOrder(8)]
        public OnlineArcadeBattleInfo Battle { get; set; }

        [MemoryPackOrder(9)]
        public List<OnlineArcadeConnectionInfo> Connections { get; set; } = new();

        [MemoryPackOrder(10)]
        public bool BattlePaused { get; set; }

        [MemoryPackOrder(11)]
        public string ChallengeId { get; set; }

        [MemoryPackOrder(12)]
        public bool ChallengeAttempted { get; set; }

        /// <summary>
        /// 仅定向本人响应填写；公共广播始终为空。
        /// </summary>
        [MemoryPackOrder(13)]
        public OnlineSlayerMartSnapshotInfo SelfSlayerMart { get; set; }

        /// <summary>
        /// 缺失为旧四十轮路线；服务器权威公开本局是否追加三关。
        /// </summary>
        [MemoryPackOrder(14)]
        public int CasualRouteVersion { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RunId = default;
            this.RoomId = default;
            this.Revision = default;
            this.Phase = default;
            this.CompletedArena = default;
            this.NextArena = default;
            this.ConfigFingerprint = default;
            this.Players.Clear();
            this.Battle = default;
            this.Connections.Clear();
            this.BattlePaused = default;
            this.ChallengeId = default;
            this.ChallengeAttempted = default;
            this.SelfSlayerMart = default;
            this.CasualRouteVersion = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.C2G_StartOnlineArcadeRun)]
    [ResponseType(nameof(G2C_StartOnlineArcadeRun))]
    public partial class C2G_StartOnlineArcadeRun : MessageObject, ISessionRequest
    {
        public static C2G_StartOnlineArcadeRun Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2G_StartOnlineArcadeRun), isFromPool) as C2G_StartOnlineArcadeRun;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public string OperationId { get; set; }

        [MemoryPackOrder(2)]
        public string ProtocolFingerprint { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.OperationId = default;
            this.ProtocolFingerprint = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.G2C_StartOnlineArcadeRun)]
    public partial class G2C_StartOnlineArcadeRun : MessageObject, ISessionResponse
    {
        public static G2C_StartOnlineArcadeRun Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_StartOnlineArcadeRun), isFromPool) as G2C_StartOnlineArcadeRun;
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
    [Message(OnlineOuter.C2G_GetCurrentOnlineArcadeRun)]
    [ResponseType(nameof(G2C_GetCurrentOnlineArcadeRun))]
    public partial class C2G_GetCurrentOnlineArcadeRun : MessageObject, ISessionRequest
    {
        public static C2G_GetCurrentOnlineArcadeRun Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2G_GetCurrentOnlineArcadeRun), isFromPool) as C2G_GetCurrentOnlineArcadeRun;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public string ProtocolFingerprint { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.ProtocolFingerprint = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.G2C_GetCurrentOnlineArcadeRun)]
    public partial class G2C_GetCurrentOnlineArcadeRun : MessageObject, ISessionResponse
    {
        public static G2C_GetCurrentOnlineArcadeRun Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_GetCurrentOnlineArcadeRun), isFromPool) as G2C_GetCurrentOnlineArcadeRun;
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
    [Message(OnlineOuter.C2G_OperateOnlineArcadeSafeCheckpoint)]
    [ResponseType(nameof(G2C_OperateOnlineArcadeSafeCheckpoint))]
    public partial class C2G_OperateOnlineArcadeSafeCheckpoint : MessageObject, ISessionRequest
    {
        public static C2G_OperateOnlineArcadeSafeCheckpoint Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2G_OperateOnlineArcadeSafeCheckpoint), isFromPool) as C2G_OperateOnlineArcadeSafeCheckpoint;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public string OperationId { get; set; }

        [MemoryPackOrder(2)]
        public long ExpectedRevision { get; set; }

        [MemoryPackOrder(3)]
        public int ActionKind { get; set; }

        [MemoryPackOrder(6)]
        public bool Ready { get; set; }

        [MemoryPackOrder(7)]
        public string ProtocolFingerprint { get; set; }

        [MemoryPackOrder(8)]
        public string ScoreShopDropId { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.OperationId = default;
            this.ExpectedRevision = default;
            this.ActionKind = default;
            this.Ready = default;
            this.ProtocolFingerprint = default;
            this.ScoreShopDropId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.G2C_OperateOnlineArcadeSafeCheckpoint)]
    public partial class G2C_OperateOnlineArcadeSafeCheckpoint : MessageObject, ISessionResponse
    {
        public static G2C_OperateOnlineArcadeSafeCheckpoint Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_OperateOnlineArcadeSafeCheckpoint), isFromPool) as G2C_OperateOnlineArcadeSafeCheckpoint;
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
    [Message(OnlineOuter.G2C_OnlineArcadeRunChanged)]
    public partial class G2C_OnlineArcadeRunChanged : MessageObject, ISessionMessage
    {
        public static G2C_OnlineArcadeRunChanged Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_OnlineArcadeRunChanged), isFromPool) as G2C_OnlineArcadeRunChanged;
        }

        [MemoryPackOrder(0)]
        public OnlineArcadeRunSnapshot Run { get; set; }

        [MemoryPackOrder(1)]
        public bool HasRun { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.Run = default;
            this.HasRun = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.C2G_SubmitOnlineArcadeInput)]
    [ResponseType(nameof(G2C_SubmitOnlineArcadeInput))]
    public partial class C2G_SubmitOnlineArcadeInput : MessageObject, ISessionRequest
    {
        public static C2G_SubmitOnlineArcadeInput Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2G_SubmitOnlineArcadeInput), isFromPool) as C2G_SubmitOnlineArcadeInput;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public int ArenaNumber { get; set; }

        [MemoryPackOrder(2)]
        public long InputSequence { get; set; }

        [MemoryPackOrder(3)]
        public float MoveX { get; set; }

        [MemoryPackOrder(4)]
        public float MoveZ { get; set; }

        [MemoryPackOrder(5)]
        public float AimX { get; set; }

        [MemoryPackOrder(6)]
        public float AimZ { get; set; }

        [MemoryPackOrder(7)]
        public bool Firing { get; set; }

        [MemoryPackOrder(8)]
        public bool UseBomb { get; set; }

        [MemoryPackOrder(9)]
        public bool UseDash { get; set; }

        [MemoryPackOrder(10)]
        public int RoundGeneration { get; set; }

        [MemoryPackOrder(11)]
        public bool RoundReady { get; set; }

        [MemoryPackOrder(12)]
        public int ExitTarget { get; set; }

        [MemoryPackOrder(13)]
        public bool ConfirmExit { get; set; }

        [MemoryPackOrder(14)]
        public bool UseFieldUpgrade { get; set; }

        [MemoryPackOrder(15)]
        public string ProtocolFingerprint { get; set; }

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
            this.ProtocolFingerprint = default;
            this.DonationRunId = default;
            this.DonateLifeTargetId = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.G2C_SubmitOnlineArcadeInput)]
    public partial class G2C_SubmitOnlineArcadeInput : MessageObject, ISessionResponse
    {
        public static G2C_SubmitOnlineArcadeInput Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_SubmitOnlineArcadeInput), isFromPool) as G2C_SubmitOnlineArcadeInput;
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
    [Message(OnlineOuter.OnlineArcadeContentAreaInfo)]
    public partial class OnlineArcadeContentAreaInfo : MessageObject
    {
        public static OnlineArcadeContentAreaInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineArcadeContentAreaInfo), isFromPool) as OnlineArcadeContentAreaInfo;
        }

        [MemoryPackOrder(0)]
        public int Id { get; set; }

        [MemoryPackOrder(1)]
        public int Kind { get; set; }

        [MemoryPackOrder(2)]
        public float X { get; set; }

        [MemoryPackOrder(3)]
        public float Y { get; set; }

        [MemoryPackOrder(4)]
        public float Radius { get; set; }

        [MemoryPackOrder(5)]
        public bool Warning { get; set; }

        [MemoryPackOrder(6)]
        public float EndX { get; set; }

        [MemoryPackOrder(7)]
        public float EndY { get; set; }

        [MemoryPackOrder(8)]
        public bool Segment { get; set; }

        /// <summary>
        /// 残火显示淡出使用的权威寿命；其它区域保持默认零。
        /// </summary>
        [MemoryPackOrder(9)]
        public float RemainingSeconds { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.Id = default;
            this.Kind = default;
            this.X = default;
            this.Y = default;
            this.Radius = default;
            this.Warning = default;
            this.EndX = default;
            this.EndY = default;
            this.Segment = default;
            this.RemainingSeconds = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineSlayerMartOfferInfo)]
    public partial class OnlineSlayerMartOfferInfo : MessageObject
    {
        public static OnlineSlayerMartOfferInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineSlayerMartOfferInfo), isFromPool) as OnlineSlayerMartOfferInfo;
        }

        [MemoryPackOrder(0)]
        public string ItemId { get; set; }

        [MemoryPackOrder(1)]
        public string Error { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.ItemId = default;
            this.Error = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineSlayerMartMemberInfo)]
    public partial class OnlineSlayerMartMemberInfo : MessageObject
    {
        public static OnlineSlayerMartMemberInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineSlayerMartMemberInfo), isFromPool) as OnlineSlayerMartMemberInfo;
        }

        [MemoryPackOrder(0)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(1)]
        public int Choice { get; set; }

        [MemoryPackOrder(2)]
        public bool MenuOpen { get; set; }

        [MemoryPackOrder(3)]
        public bool Finished { get; set; }

        [MemoryPackOrder(4)]
        public int Lives { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.PlayerId = default;
            this.Choice = default;
            this.MenuOpen = default;
            this.Finished = default;
            this.Lives = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineSlayerMartSnapshotInfo)]
    public partial class OnlineSlayerMartSnapshotInfo : MessageObject
    {
        public static OnlineSlayerMartSnapshotInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineSlayerMartSnapshotInfo), isFromPool) as OnlineSlayerMartSnapshotInfo;
        }

        [MemoryPackOrder(0)]
        public string RegionInstanceId { get; set; }

        [MemoryPackOrder(1)]
        public int Generation { get; set; }

        [MemoryPackOrder(2)]
        public long Revision { get; set; }

        [MemoryPackOrder(3)]
        public int Visit { get; set; }

        [MemoryPackOrder(4)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(5)]
        public long Cash { get; set; }

        [MemoryPackOrder(6)]
        public int Tokens { get; set; }

        [MemoryPackOrder(7)]
        public float RemainingSeconds { get; set; }

        [MemoryPackOrder(8)]
        public bool Ready { get; set; }

        [MemoryPackOrder(9)]
        public bool Closing { get; set; }

        [MemoryPackOrder(10)]
        public List<OnlineSlayerMartOfferInfo> Offers { get; set; } = new();

        [MemoryPackOrder(11)]
        public List<OnlineSlayerMartMemberInfo> Members { get; set; } = new();

        [MemoryPackOrder(12)]
        public int Bombs { get; set; }

        [MemoryPackOrder(13)]
        public int Dashes { get; set; }

        [MemoryPackOrder(14)]
        public int Lives { get; set; }

        [MemoryPackOrder(15)]
        public int VestSlots { get; set; }

        [MemoryPackOrder(16)]
        public int Plates { get; set; }

        [MemoryPackOrder(17)]
        public List<string> Owned { get; set; } = new();

        [MemoryPackOrder(18)]
        public string Error { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RegionInstanceId = default;
            this.Generation = default;
            this.Revision = default;
            this.Visit = default;
            this.PlayerId = default;
            this.Cash = default;
            this.Tokens = default;
            this.RemainingSeconds = default;
            this.Ready = default;
            this.Closing = default;
            this.Offers.Clear();
            this.Members.Clear();
            this.Bombs = default;
            this.Dashes = default;
            this.Lives = default;
            this.VestSlots = default;
            this.Plates = default;
            this.Owned.Clear();
            this.Error = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineSlayerMartSupportInfo)]
    public partial class OnlineSlayerMartSupportInfo : MessageObject
    {
        public static OnlineSlayerMartSupportInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineSlayerMartSupportInfo), isFromPool) as OnlineSlayerMartSupportInfo;
        }

        [MemoryPackOrder(0)]
        public string ProductId { get; set; }

        [MemoryPackOrder(1)]
        public string InstanceId { get; set; }

        [MemoryPackOrder(2)]
        public int Kind { get; set; }

        [MemoryPackOrder(3)]
        public int BodyIndex { get; set; }

        [MemoryPackOrder(4)]
        public float PositionX { get; set; }

        [MemoryPackOrder(5)]
        public float PositionZ { get; set; }

        [MemoryPackOrder(6)]
        public float ShotDirectionX { get; set; }

        [MemoryPackOrder(7)]
        public float ShotDirectionZ { get; set; }

        [MemoryPackOrder(8)]
        public float RemainingSeconds { get; set; }

        [MemoryPackOrder(9)]
        public int Charges { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.ProductId = default;
            this.InstanceId = default;
            this.Kind = default;
            this.BodyIndex = default;
            this.PositionX = default;
            this.PositionZ = default;
            this.ShotDirectionX = default;
            this.ShotDirectionZ = default;
            this.RemainingSeconds = default;
            this.Charges = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.C2G_OperateOnlineSlayerMart)]
    [ResponseType(nameof(G2C_OperateOnlineSlayerMart))]
    public partial class C2G_OperateOnlineSlayerMart : MessageObject, ISessionRequest
    {
        public static C2G_OperateOnlineSlayerMart Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(C2G_OperateOnlineSlayerMart), isFromPool) as C2G_OperateOnlineSlayerMart;
        }

        [MemoryPackOrder(0)]
        public int RpcId { get; set; }

        [MemoryPackOrder(1)]
        public string RegionInstanceId { get; set; }

        [MemoryPackOrder(2)]
        public int Generation { get; set; }

        [MemoryPackOrder(3)]
        public long ExpectedRevision { get; set; }

        [MemoryPackOrder(4)]
        public string OperationId { get; set; }

        [MemoryPackOrder(5)]
        public int ActionKind { get; set; }

        [MemoryPackOrder(6)]
        public string ItemId { get; set; }

        [MemoryPackOrder(7)]
        public int BoxId { get; set; }

        [MemoryPackOrder(8)]
        public int ConfirmedAmount { get; set; }

        [MemoryPackOrder(9)]
        public bool Value { get; set; }

        [MemoryPackOrder(10)]
        public string ProtocolFingerprint { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RpcId = default;
            this.RegionInstanceId = default;
            this.Generation = default;
            this.ExpectedRevision = default;
            this.OperationId = default;
            this.ActionKind = default;
            this.ItemId = default;
            this.BoxId = default;
            this.ConfirmedAmount = default;
            this.Value = default;
            this.ProtocolFingerprint = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.G2C_OperateOnlineSlayerMart)]
    public partial class G2C_OperateOnlineSlayerMart : MessageObject, ISessionResponse
    {
        public static G2C_OperateOnlineSlayerMart Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(G2C_OperateOnlineSlayerMart), isFromPool) as G2C_OperateOnlineSlayerMart;
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
    [Message(OnlineOuter.OnlineArcadeBombBlastInfo)]
    public partial class OnlineArcadeBombBlastInfo : MessageObject
    {
        public static OnlineArcadeBombBlastInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineArcadeBombBlastInfo), isFromPool) as OnlineArcadeBombBlastInfo;
        }

        [MemoryPackOrder(0)]
        public long Sequence { get; set; }

        [MemoryPackOrder(1)]
        public long PlayerId { get; set; }

        [MemoryPackOrder(2)]
        public float PositionX { get; set; }

        [MemoryPackOrder(3)]
        public float PositionZ { get; set; }

        [MemoryPackOrder(4)]
        public int RoundGeneration { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.Sequence = default;
            this.PlayerId = default;
            this.PositionX = default;
            this.PositionZ = default;
            this.RoundGeneration = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineArcadeEnemyDeathInfo)]
    public partial class OnlineArcadeEnemyDeathInfo : MessageObject
    {
        public static OnlineArcadeEnemyDeathInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineArcadeEnemyDeathInfo), isFromPool) as OnlineArcadeEnemyDeathInfo;
        }

        [MemoryPackOrder(0)]
        public long RuntimeId { get; set; }

        [MemoryPackOrder(1)]
        public float DirectionX { get; set; }

        [MemoryPackOrder(2)]
        public float DirectionZ { get; set; }

        [MemoryPackOrder(3)]
        public bool Explosive { get; set; }

        [MemoryPackOrder(4)]
        public int RoundGeneration { get; set; }

        /// <summary>
        /// 本次最终有效伤害来自喷火器。
        /// </summary>
        [MemoryPackOrder(5)]
        public bool FlameKill { get; set; }

        /// <summary>
        /// 0沿用普通/爆炸，1能量爆炸，2近距霰弹；只影响死亡表现。
        /// </summary>
        [MemoryPackOrder(6)]
        public int ImpactKind { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RuntimeId = default;
            this.DirectionX = default;
            this.DirectionZ = default;
            this.Explosive = default;
            this.RoundGeneration = default;
            this.FlameKill = default;
            this.ImpactKind = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineArcadeRoundRewardInfo)]
    public partial class OnlineArcadeRoundRewardInfo : MessageObject
    {
        public static OnlineArcadeRoundRewardInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineArcadeRoundRewardInfo), isFromPool) as OnlineArcadeRoundRewardInfo;
        }

        [MemoryPackOrder(0)]
        public int RoundIdentity { get; set; }

        [MemoryPackOrder(1)]
        public float X { get; set; }

        [MemoryPackOrder(2)]
        public float Z { get; set; }

        [MemoryPackOrder(3)]
        public float ElapsedSeconds { get; set; }

        [MemoryPackOrder(4)]
        public int Phase { get; set; }

        [MemoryPackOrder(5)]
        public List<OnlineArcadeRoundRewardItemInfo> Items { get; set; } = new();

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.RoundIdentity = default;
            this.X = default;
            this.Z = default;
            this.ElapsedSeconds = default;
            this.Phase = default;
            this.Items.Clear();

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineArcadeRoundRewardItemInfo)]
    public partial class OnlineArcadeRoundRewardItemInfo : MessageObject
    {
        public static OnlineArcadeRoundRewardItemInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineArcadeRoundRewardItemInfo), isFromPool) as OnlineArcadeRoundRewardItemInfo;
        }

        [MemoryPackOrder(0)]
        public int Id { get; set; }

        [MemoryPackOrder(1)]
        public float X { get; set; }

        [MemoryPackOrder(2)]
        public float Z { get; set; }

        [MemoryPackOrder(3)]
        public bool GoldBar { get; set; }

        [MemoryPackOrder(4)]
        public bool Collected { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.Id = default;
            this.X = default;
            this.Z = default;
            this.GoldBar = default;
            this.Collected = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineArcadeHerdInfo)]
    public partial class OnlineArcadeHerdInfo : MessageObject
    {
        public static OnlineArcadeHerdInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineArcadeHerdInfo), isFromPool) as OnlineArcadeHerdInfo;
        }

        [MemoryPackOrder(0)]
        public long MapInstanceId { get; set; }

        [MemoryPackOrder(1)]
        public long Id { get; set; }

        [MemoryPackOrder(2)]
        public int Phase { get; set; }

        [MemoryPackOrder(3)]
        public float X { get; set; }

        [MemoryPackOrder(4)]
        public float Z { get; set; }

        [MemoryPackOrder(5)]
        public float DirectionX { get; set; }

        [MemoryPackOrder(6)]
        public float DirectionZ { get; set; }

        [MemoryPackOrder(7)]
        public double PhaseSeconds { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.MapInstanceId = default;
            this.Id = default;
            this.Phase = default;
            this.X = default;
            this.Z = default;
            this.DirectionX = default;
            this.DirectionZ = default;
            this.PhaseSeconds = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineArcadeElectricHitInfo)]
    public partial class OnlineArcadeElectricHitInfo : MessageObject
    {
        public static OnlineArcadeElectricHitInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineArcadeElectricHitInfo), isFromPool) as OnlineArcadeElectricHitInfo;
        }

        [MemoryPackOrder(0)]
        public long Sequence { get; set; }

        [MemoryPackOrder(1)]
        public int HazardId { get; set; }

        [MemoryPackOrder(2)]
        public float X { get; set; }

        [MemoryPackOrder(3)]
        public float Z { get; set; }

        [MemoryPackOrder(4)]
        public int RoundGeneration { get; set; }

        [MemoryPackOrder(5)]
        public float ElapsedSeconds { get; set; }

        [MemoryPackOrder(6)]
        public long ServerTime { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.Sequence = default;
            this.HazardId = default;
            this.X = default;
            this.Z = default;
            this.RoundGeneration = default;
            this.ElapsedSeconds = default;
            this.ServerTime = default;

            ObjectPool.Instance.Recycle(this);
        }
    }

    [MemoryPackable]
    [Message(OnlineOuter.OnlineArcadeWeaponFeedbackInfo)]
    public partial class OnlineArcadeWeaponFeedbackInfo : MessageObject
    {
        public static OnlineArcadeWeaponFeedbackInfo Create(bool isFromPool = false)
        {
            return ObjectPool.Instance.Fetch(typeof(OnlineArcadeWeaponFeedbackInfo), isFromPool) as OnlineArcadeWeaponFeedbackInfo;
        }

        [MemoryPackOrder(0)]
        public long Sequence { get; set; }

        [MemoryPackOrder(1)]
        public int Kind { get; set; }

        [MemoryPackOrder(2)]
        public float PositionX { get; set; }

        [MemoryPackOrder(3)]
        public float PositionZ { get; set; }

        [MemoryPackOrder(4)]
        public float DirectionX { get; set; }

        [MemoryPackOrder(5)]
        public float DirectionZ { get; set; }

        [MemoryPackOrder(6)]
        public float Radius { get; set; }

        [MemoryPackOrder(7)]
        public int RoundGeneration { get; set; }

        [MemoryPackOrder(8)]
        public long ServerTime { get; set; }

        public override void Dispose()
        {
            if (!this.IsFromPool)
            {
                return;
            }

            this.Sequence = default;
            this.Kind = default;
            this.PositionX = default;
            this.PositionZ = default;
            this.DirectionX = default;
            this.DirectionZ = default;
            this.Radius = default;
            this.RoundGeneration = default;
            this.ServerTime = default;

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
        public const ushort OnlineArcadeOwnedAmountInfo = 12077;
        public const ushort OnlineArcadeEquipmentInfo = 12078;
        public const ushort OnlineArcadePowerupInfo = 12079;
        public const ushort OnlineArcadeBattlePlayerInfo = 12080;
        public const ushort OnlineArcadeEnemyInfo = 12081;
        public const ushort OnlineArcadeProjectileInfo = 12082;
        public const ushort OnlineArcadeDropInfo = 12083;
        public const ushort OnlineArcadeBattleInfo = 12084;
        public const ushort OnlineArcadePlayerInfo = 12085;
        public const ushort OnlineArcadeConnectionInfo = 12087;
        public const ushort OnlineArcadeRunSnapshot = 12088;
        public const ushort C2G_StartOnlineArcadeRun = 12089;
        public const ushort G2C_StartOnlineArcadeRun = 12090;
        public const ushort C2G_GetCurrentOnlineArcadeRun = 12091;
        public const ushort G2C_GetCurrentOnlineArcadeRun = 12092;
        public const ushort C2G_OperateOnlineArcadeSafeCheckpoint = 12093;
        public const ushort G2C_OperateOnlineArcadeSafeCheckpoint = 12094;
        public const ushort G2C_OnlineArcadeRunChanged = 12095;
        public const ushort C2G_SubmitOnlineArcadeInput = 12096;
        public const ushort G2C_SubmitOnlineArcadeInput = 12097;
        public const ushort OnlineArcadeContentAreaInfo = 12098;
        public const ushort OnlineSlayerMartOfferInfo = 12099;
        public const ushort OnlineSlayerMartMemberInfo = 12100;
        public const ushort OnlineSlayerMartSnapshotInfo = 12101;
        public const ushort OnlineSlayerMartSupportInfo = 12102;
        public const ushort C2G_OperateOnlineSlayerMart = 12103;
        public const ushort G2C_OperateOnlineSlayerMart = 12104;
        public const ushort OnlineArcadeBombBlastInfo = 12105;
        public const ushort OnlineArcadeEnemyDeathInfo = 12106;
        public const ushort OnlineArcadeRoundRewardInfo = 12107;
        public const ushort OnlineArcadeRoundRewardItemInfo = 12108;
        public const ushort OnlineArcadeHerdInfo = 12109;
        public const ushort OnlineArcadeElectricHitInfo = 12110;
        public const ushort OnlineArcadeWeaponFeedbackInfo = 12111;
    }
}