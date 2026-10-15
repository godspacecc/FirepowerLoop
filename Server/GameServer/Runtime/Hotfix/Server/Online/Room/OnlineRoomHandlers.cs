using System;

namespace ET.Server
{
    [MessageHandler(SceneType.RoomDirectory)]
    public class P2D_CreateRoomHandler: MessageHandler<Scene, P2D_CreateRoom, D2P_CreateRoom>
    {
        protected override async ETTask Run(Scene scene, P2D_CreateRoom request, D2P_CreateRoom response)
        {
            var result = await scene.GetComponent<OnlineRoomDirectoryComponent>().CreateRoom(request.PlayerId,
                request.DisplayName, request.SessionEpoch, request.Purpose, request.Visibility,
                request.ClientVersion, request.ArcadeConfigFingerprint);
            response.Error = result.Error;
            response.Message = result.Message;
            response.RoomId = result.RoomId;
            response.Room = result.Room;
        }
    }

    [MessageHandler(SceneType.RoomDirectory)]
    public class P2D_MatchArcadeRoomHandler: MessageHandler<Scene, P2D_MatchArcadeRoom, D2P_MatchArcadeRoom>
    {
        protected override async ETTask Run(Scene scene, P2D_MatchArcadeRoom request, D2P_MatchArcadeRoom response)
        {
            var result = await scene.GetComponent<OnlineRoomDirectoryComponent>().MatchArcadeRoom(
                request.PlayerId, request.DisplayName, request.SessionEpoch,
                request.ClientVersion, request.ArcadeConfigFingerprint);
            response.Error = result.Error;
            response.Message = result.Message;
            response.RoomId = result.RoomId;
            response.Created = result.Created;
            response.Room = result.Room;
        }
    }

    [MessageHandler(SceneType.RoomDirectory)]
    public class P2D_ResolveRoomHandler: MessageHandler<Scene, P2D_ResolveRoom, D2P_ResolveRoom>
    {
        protected override async ETTask Run(Scene scene, P2D_ResolveRoom request, D2P_ResolveRoom response)
        {
            var result = scene.GetComponent<OnlineRoomDirectoryComponent>().ResolveRoom(request.PlayerId, request.RoomCode, request.SourceAddress);
            response.Error = result.Error;
            response.Message = result.Message;
            response.RoomId = result.RoomId;
            await ETTask.CompletedTask;
        }
    }

    [MessageHandler(SceneType.RoomDirectory)]
    public class R2D_CloseRoomHandler: MessageHandler<Scene, R2D_CloseRoom, D2R_CloseRoom>
    {
        protected override async ETTask Run(Scene scene, R2D_CloseRoom request, D2R_CloseRoom response)
        {
            scene.GetComponent<OnlineRoomDirectoryComponent>().CloseRoom(request.RoomId, request.RoomCode);
            response.Error = ErrorCode.ERR_Success;
            await ETTask.CompletedTask;
        }
    }

    [MessageHandler(SceneType.OnlineRoom)]
    public class D2R_CreateRoomHandler: MessageHandler<Scene, D2R_CreateRoom, R2D_CreateRoom>
    {
        protected override async ETTask Run(Scene scene, D2R_CreateRoom request, R2D_CreateRoom response)
        {
            OnlineRoomManagerComponent manager = scene.GetComponent<OnlineRoomManagerComponent>();
            if (!OnlineRoomSnapshotHelper.TryValidateCreation(request.Purpose, request.Visibility,
                    request.ClientVersion, request.ArcadeConfigFingerprint, out OnlineRoomPurpose purpose,
                    out OnlineRoomVisibility visibility, out int capacity, out string validationError))
            {
                response.Error = ErrorCode.ERR_OnlineRoomOperationInvalid;
                response.Message = validationError;
                return;
            }
            OnlineRoomActor room = manager.AddChildWithId<OnlineRoomActor>(request.RoomId);
            room.Init(request.RoomId, request.RoomCode, request.PlayerId, request.DisplayName, request.SessionEpoch,
                purpose, visibility, capacity, request.ClientVersion, request.ArcadeConfigFingerprint);
            room.AddComponent<MailBoxComponent, MailBoxType>(MailBoxType.OrderedMessage);
            manager.Add(room);

            try
            {
                await scene.GetComponent<LocationProxyComponent>().Add(LocationType.OnlineRoom, request.RoomId, room.GetActorId());
                response.Room = OnlineRoomSnapshotHelper.BuildSnapshot(room);
                bool chatReady = await room.SyncChatMembers(true);
                response.Error = chatReady ? ErrorCode.ERR_Success : ErrorCode.ERR_OnlineChatMembershipPending;
                response.Message = string.Empty;
            }
            catch (Exception e)
            {
                manager.Remove(request.RoomId);
                room.Dispose();
                Log.Error($"online room location add failed roomId={request.RoomId} error={e.GetType().Name}");
                response.Error = ErrorCode.ERR_OnlineRoomServiceUnavailable;
                response.Message = string.Empty;
            }
        }
    }

    [MessageHandler(SceneType.OnlineRoom)]
    public class G2R_GetPublicRoomListHandler: MessageHandler<Scene, G2R_GetPublicRoomList, R2G_GetPublicRoomList>
    {
        protected override async ETTask Run(Scene scene, G2R_GetPublicRoomList request, R2G_GetPublicRoomList response)
        {
            try
            {
                OnlineRoomManagerComponent manager = scene.GetComponent<OnlineRoomManagerComponent>();
                long nextCursorRoomId;
                bool hasMore;
                foreach (PublicRoomSummary room in manager.GetPublicRoomList(
                             request.CursorRoomId,
                             out nextCursorRoomId,
                             out hasMore))
                {
                    response.Rooms.Add(room);
                }

                response.NextCursorRoomId = nextCursorRoomId;
                response.HasMore = hasMore;
                response.Error = ErrorCode.ERR_Success;
                response.Message = string.Empty;
            }
            catch (Exception e)
            {
                Log.Error($"online room public list failed stage=ReadManager exception={e}");
                response.Error = ErrorCode.ERR_OnlineRoomServiceUnavailable;
                response.Message = string.Empty;
            }

            await ETTask.CompletedTask;
        }
    }

    [MessageHandler(SceneType.OnlineRoom)]
    public class P2R_JoinRoomHandler: MessageHandler<OnlineRoomActor, P2R_JoinRoom, R2P_JoinRoom>
    {
        protected override async ETTask Run(OnlineRoomActor room, P2R_JoinRoom request, R2P_JoinRoom response)
        {
            try
            {
                response.Room = await room.Join(request.PlayerId, request.DisplayName, request.SessionEpoch,
                    request.ExpectedPurpose, request.ClientVersion, request.ArcadeConfigFingerprint);
                response.Error = ErrorCode.ERR_Success;
            }
            catch (RpcException e)
            {
                response.Error = e.Error;
                response.Message = string.Empty;
                Log.Warning($"online room join rejected playerId={request.PlayerId} error={e.Error}");
                if (e.Error == ErrorCode.ERR_OnlineChatMembershipPending)
                {
                    response.Room = OnlineRoomSnapshotHelper.BuildSnapshot(room);
                }
            }
        }
    }

    [MessageHandler(SceneType.OnlineRoom)]
    public class P2R_LeaveRoomHandler: MessageHandler<OnlineRoomActor, P2R_LeaveRoom, R2P_LeaveRoom>
    {
        protected override async ETTask Run(OnlineRoomActor room, P2R_LeaveRoom request, R2P_LeaveRoom response)
        {
            try
            {
                await room.Leave(request.PlayerId);
                response.Error = ErrorCode.ERR_Success;
            }
            catch (RpcException e)
            {
                response.Error = e.Error;
                response.Message = string.Empty;
                Log.Warning($"online room leave rejected playerId={request.PlayerId} error={e.Error}");
            }
        }
    }

    [MessageHandler(SceneType.OnlineRoom)]
    public class P2R_GetRoomHandler: MessageHandler<OnlineRoomActor, P2R_GetRoom, R2P_GetRoom>
    {
        protected override async ETTask Run(OnlineRoomActor room, P2R_GetRoom request, R2P_GetRoom response)
        {
            try
            {
                response.Room = room.GetSnapshot(request.PlayerId);
                response.Error = ErrorCode.ERR_Success;
            }
            catch (RpcException e)
            {
                response.Error = e.Error;
                response.Message = string.Empty;
                Log.Warning($"online room snapshot rejected playerId={request.PlayerId} error={e.Error}");
            }

            await ETTask.CompletedTask;
        }
    }

    [MessageHandler(SceneType.OnlineRoom)]
    public class P2R_SetMemberConnectionHandler: MessageHandler<OnlineRoomActor, P2R_SetMemberConnection, R2P_SetMemberConnection>
    {
        protected override async ETTask Run(OnlineRoomActor room, P2R_SetMemberConnection request, R2P_SetMemberConnection response)
        {
            try
            {
                await room.SetConnection(request.PlayerId, request.SessionEpoch, request.ConnectionState);
                response.Error = ErrorCode.ERR_Success;
            }
            catch (RpcException e)
            {
                response.Error = e.Error;
                response.Message = string.Empty;
                Log.Warning($"online room connection update rejected playerId={request.PlayerId} error={e.Error}");
            }
        }
    }

    [MessageHandler(SceneType.OnlineRoom)]
    [FriendOf(typeof(OnlineRoomActor))]
    public class R2R_RetryCloseHandler: MessageHandler<OnlineRoomActor, R2R_RetryClose>
    {
        protected override async ETTask Run(OnlineRoomActor room, R2R_RetryClose message)
        {
            if (room.RoomId == message.RoomId)
            {
                await room.RetryClose();
            }
        }
    }

    [MessageHandler(SceneType.OnlineRoom)]
    public class P2R_EnsureRoomChatReadyHandler: MessageHandler<OnlineRoomActor, P2R_EnsureRoomChatReady, R2P_EnsureRoomChatReady>
    {
        protected override async ETTask Run(OnlineRoomActor room, P2R_EnsureRoomChatReady request, R2P_EnsureRoomChatReady response)
        {
            try
            {
                bool ready = await room.EnsureChatReady(request.PlayerId);
                response.Error = ready ? ErrorCode.ERR_Success : ErrorCode.ERR_OnlineChatMembershipPending;
                response.Message = string.Empty;
                response.Room = OnlineRoomSnapshotHelper.BuildSnapshot(room);
            }
            catch (RpcException e)
            {
                response.Error = e.Error;
                response.Message = string.Empty;
                Log.Warning($"online room chat readiness rejected playerId={request.PlayerId} error={e.Error}");
            }
        }
    }

    [MessageHandler(SceneType.OnlineRoom)]
    [FriendOf(typeof(OnlineRoomActor))]
    public class R2R_RetryChatSyncHandler: MessageHandler<OnlineRoomActor, R2R_RetryChatSync>
    {
        protected override async ETTask Run(OnlineRoomActor room, R2R_RetryChatSync message)
        {
            if (room.RoomId == message.RoomId)
            {
                await room.RetryChatSync(message.SyncGeneration);
            }
        }
    }
}
