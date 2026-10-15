namespace ET.Server
{
    [MessageSessionHandler(SceneType.GameGate)]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public class C2G_GetPublicRoomListHandler: MessageSessionHandler<C2G_GetPublicRoomList, G2C_GetPublicRoomList>
    {
        protected override async ETTask Run(Session session, C2G_GetPublicRoomList request, G2C_GetPublicRoomList response)
        {
            GatePlayerProxy proxy = session.GetComponent<OnlineSessionComponent>()?.Proxy;
            if (proxy == null || !proxy.IsAuthenticated)
            {
                response.Error = ErrorCode.ERR_OnlineSessionNotLoggedIn;
                response.Message = string.Empty;
                return;
            }

            try
            {
                ActorId roomScene = ServerConfig.Instance.GetSceneByName(session.Zone(), "OnlineRoom").ActorId;
                G2R_GetPublicRoomList inner = G2R_GetPublicRoomList.Create();
                inner.CursorRoomId = request.CursorRoomId;
                using R2G_GetPublicRoomList result = await session.Root().GetComponent<MessageSender>().Call(roomScene, inner, false) as R2G_GetPublicRoomList;
                response.Error = result.Error;
                response.Message = string.Empty;
                response.NextCursorRoomId = result.NextCursorRoomId;
                response.HasMore = result.HasMore;
                foreach (PublicRoomSummary source in result.Rooms)
                {
                    PublicRoomSummary room = PublicRoomSummary.Create();
                    room.RoomCode = source.RoomCode ?? string.Empty;
                    room.OwnerDisplayName = source.OwnerDisplayName ?? string.Empty;
                    room.MemberCount = source.MemberCount;
                    room.Capacity = source.Capacity;
                    response.Rooms.Add(room);
                }
            }
            catch (System.Exception e)
            {
                Log.Error($"online room public list failed stage=GateForward exception={e}");
                response.Error = ErrorCode.ERR_OnlineRoomServiceUnavailable;
                response.Message = string.Empty;
            }
        }
    }

    [MessageSessionHandler(SceneType.GameGate)]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public class C2G_CreateRoomHandler: MessageSessionHandler<C2G_CreateRoom, G2C_CreateRoom>
    {
        protected override async ETTask Run(Session session, C2G_CreateRoom request, G2C_CreateRoom response)
        {
            GatePlayerProxy proxy = session.GetComponent<OnlineSessionComponent>()?.Proxy;
            if (proxy == null || !proxy.IsAuthenticated)
            {
                response.Error = ErrorCode.ERR_OnlineSessionNotLoggedIn;
                response.Message = string.Empty;
                return;
            }

            G2P_CreateRoom inner = G2P_CreateRoom.Create();
            inner.PlayerId = proxy.PlayerId;
            inner.SessionEpoch = proxy.SessionEpoch;
            inner.OperationId = request.OperationId;
            inner.Purpose = request.Purpose;
            inner.Visibility = request.Visibility;
            inner.ClientVersion = request.ClientVersion ?? string.Empty;
            inner.ArcadeConfigFingerprint = request.ArcadeConfigFingerprint ?? string.Empty;
            using P2G_CreateRoom result = await session.Root().GetComponent<MessageSender>().Call(proxy.PlayerActorId, inner, false) as P2G_CreateRoom;
            response.Error = result.Error;
            response.Message = string.Empty;
            response.HasRoom = result.HasRoom;
            response.Room = OnlineRoomSnapshotHelper.CloneSnapshot(result.Room);
            if (result.Error != ErrorCode.ERR_Success)
            {
                Log.Warning($"online room create rejected playerId={proxy.PlayerId} error={result.Error}");
            }
        }
    }

    [MessageSessionHandler(SceneType.GameGate)]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public class C2G_MatchArcadeRoomHandler: MessageSessionHandler<C2G_MatchArcadeRoom, G2C_MatchArcadeRoom>
    {
        protected override async ETTask Run(Session session, C2G_MatchArcadeRoom request,
            G2C_MatchArcadeRoom response)
        {
            GatePlayerProxy proxy = session.GetComponent<OnlineSessionComponent>()?.Proxy;
            if (proxy == null || !proxy.IsAuthenticated)
            {
                response.Error = ErrorCode.ERR_OnlineSessionNotLoggedIn;
                response.Message = string.Empty;
                return;
            }

            G2P_MatchArcadeRoom inner = G2P_MatchArcadeRoom.Create();
            inner.PlayerId = proxy.PlayerId;
            inner.SessionEpoch = proxy.SessionEpoch;
            inner.OperationId = request.OperationId;
            inner.ClientVersion = request.ClientVersion ?? string.Empty;
            inner.ArcadeConfigFingerprint = request.ArcadeConfigFingerprint ?? string.Empty;
            using P2G_MatchArcadeRoom result = await session.Root().GetComponent<MessageSender>()
                .Call(proxy.PlayerActorId, inner, false) as P2G_MatchArcadeRoom;
            response.Error = result.Error;
            response.Message = string.Empty;
            response.HasRoom = result.HasRoom;
            response.Room = OnlineRoomSnapshotHelper.CloneSnapshot(result.Room);
            if (result.Error != ErrorCode.ERR_Success &&
                result.Error != ErrorCode.ERR_OnlineChatMembershipPending)
            {
                Log.Warning($"online arcade match rejected playerId={proxy.PlayerId} error={result.Error}");
            }
        }
    }

    [MessageSessionHandler(SceneType.GameGate)]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public class C2G_JoinRoomHandler: MessageSessionHandler<C2G_JoinRoom, G2C_JoinRoom>
    {
        protected override async ETTask Run(Session session, C2G_JoinRoom request, G2C_JoinRoom response)
        {
            GatePlayerProxy proxy = session.GetComponent<OnlineSessionComponent>()?.Proxy;
            if (proxy == null || !proxy.IsAuthenticated)
            {
                response.Error = ErrorCode.ERR_OnlineSessionNotLoggedIn;
                response.Message = string.Empty;
                return;
            }

            G2P_JoinRoom inner = G2P_JoinRoom.Create();
            inner.PlayerId = proxy.PlayerId;
            inner.SessionEpoch = proxy.SessionEpoch;
            inner.OperationId = request.OperationId;
            inner.RoomCode = request.RoomCode;
            inner.SourceAddress = session.RemoteAddress?.Address?.ToString() ?? string.Empty;
            inner.ExpectedPurpose = request.ExpectedPurpose;
            inner.ClientVersion = request.ClientVersion ?? string.Empty;
            inner.ArcadeConfigFingerprint = request.ArcadeConfigFingerprint ?? string.Empty;
            using P2G_JoinRoom result = await session.Root().GetComponent<MessageSender>().Call(proxy.PlayerActorId, inner, false) as P2G_JoinRoom;
            response.Error = result.Error;
            response.Message = string.Empty;
            response.HasRoom = result.HasRoom;
            response.Room = OnlineRoomSnapshotHelper.CloneSnapshot(result.Room);
            if (result.Error != ErrorCode.ERR_Success)
            {
                Log.Warning($"online room join rejected playerId={proxy.PlayerId} error={result.Error}");
            }
        }
    }

    [MessageSessionHandler(SceneType.GameGate)]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public class C2G_LeaveRoomHandler: MessageSessionHandler<C2G_LeaveRoom, G2C_LeaveRoom>
    {
        protected override async ETTask Run(Session session, C2G_LeaveRoom request, G2C_LeaveRoom response)
        {
            GatePlayerProxy proxy = session.GetComponent<OnlineSessionComponent>()?.Proxy;
            if (proxy == null || !proxy.IsAuthenticated)
            {
                response.Error = ErrorCode.ERR_OnlineSessionNotLoggedIn;
                response.Message = string.Empty;
                return;
            }

            G2P_LeaveRoom inner = G2P_LeaveRoom.Create();
            inner.PlayerId = proxy.PlayerId;
            inner.SessionEpoch = proxy.SessionEpoch;
            inner.OperationId = request.OperationId;
            using P2G_LeaveRoom result = await session.Root().GetComponent<MessageSender>().Call(proxy.PlayerActorId, inner, false) as P2G_LeaveRoom;
            response.Error = result.Error;
            response.Message = string.Empty;
            if (result.Error != ErrorCode.ERR_Success)
            {
                Log.Warning($"online room leave rejected playerId={proxy.PlayerId} error={result.Error}");
            }
        }
    }

    [MessageSessionHandler(SceneType.GameGate)]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public class C2G_GetCurrentRoomHandler: MessageSessionHandler<C2G_GetCurrentRoom, G2C_GetCurrentRoom>
    {
        protected override async ETTask Run(Session session, C2G_GetCurrentRoom request, G2C_GetCurrentRoom response)
        {
            GatePlayerProxy proxy = session.GetComponent<OnlineSessionComponent>()?.Proxy;
            if (proxy == null || !proxy.IsAuthenticated)
            {
                response.Error = ErrorCode.ERR_OnlineSessionNotLoggedIn;
                response.Message = string.Empty;
                return;
            }

            G2P_GetCurrentRoom inner = G2P_GetCurrentRoom.Create();
            inner.PlayerId = proxy.PlayerId;
            inner.SessionEpoch = proxy.SessionEpoch;
            using P2G_GetCurrentRoom result = await session.Root().GetComponent<MessageSender>().Call(proxy.PlayerActorId, inner, false) as P2G_GetCurrentRoom;
            response.Error = result.Error;
            response.Message = string.Empty;
            response.HasRoom = result.HasRoom;
            response.Room = OnlineRoomSnapshotHelper.CloneSnapshot(result.Room);
            if (result.Error != ErrorCode.ERR_Success)
            {
                Log.Warning($"online room snapshot rejected playerId={proxy.PlayerId} error={result.Error}");
            }
        }
    }
}
