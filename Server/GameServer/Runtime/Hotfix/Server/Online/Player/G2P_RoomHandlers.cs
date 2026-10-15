namespace ET.Server
{
    [MessageHandler(SceneType.Player)]
    public class G2P_CreateRoomHandler: MessageHandler<OnlinePlayerActor, G2P_CreateRoom, P2G_CreateRoom>
    {
        protected override async ETTask Run(OnlinePlayerActor actor, G2P_CreateRoom request, P2G_CreateRoom response)
        {
            var result = await actor.CreateRoom(request.SessionEpoch, request.OperationId, request.Purpose,
                request.Visibility, request.ClientVersion, request.ArcadeConfigFingerprint);
            response.Error = result.Error;
            response.Message = result.Message;
            response.HasRoom = result.HasRoom;
            response.Room = result.Room;
        }
    }

    [MessageHandler(SceneType.Player)]
    public class G2P_MatchArcadeRoomHandler: MessageHandler<OnlinePlayerActor, G2P_MatchArcadeRoom, P2G_MatchArcadeRoom>
    {
        protected override async ETTask Run(OnlinePlayerActor actor, G2P_MatchArcadeRoom request,
            P2G_MatchArcadeRoom response)
        {
            var result = await actor.MatchArcadeRoom(request.SessionEpoch, request.OperationId,
                request.ClientVersion, request.ArcadeConfigFingerprint);
            response.Error = result.Error;
            response.Message = result.Message;
            response.HasRoom = result.HasRoom;
            response.Room = result.Room;
        }
    }

    [MessageHandler(SceneType.Player)]
    public class G2P_JoinRoomHandler: MessageHandler<OnlinePlayerActor, G2P_JoinRoom, P2G_JoinRoom>
    {
        protected override async ETTask Run(OnlinePlayerActor actor, G2P_JoinRoom request, P2G_JoinRoom response)
        {
            var result = await actor.JoinRoom(request.SessionEpoch, request.OperationId, request.RoomCode,
                request.SourceAddress, request.ExpectedPurpose, request.ClientVersion, request.ArcadeConfigFingerprint);
            response.Error = result.Error;
            response.Message = result.Message;
            response.HasRoom = result.HasRoom;
            response.Room = result.Room;
        }
    }

    [MessageHandler(SceneType.Player)]
    public class G2P_LeaveRoomHandler: MessageHandler<OnlinePlayerActor, G2P_LeaveRoom, P2G_LeaveRoom>
    {
        protected override async ETTask Run(OnlinePlayerActor actor, G2P_LeaveRoom request, P2G_LeaveRoom response)
        {
            var result = await actor.LeaveRoom(request.SessionEpoch, request.OperationId);
            response.Error = result.Error;
            response.Message = result.Message;
        }
    }

    [MessageHandler(SceneType.Player)]
    public class G2P_GetCurrentRoomHandler: MessageHandler<OnlinePlayerActor, G2P_GetCurrentRoom, P2G_GetCurrentRoom>
    {
        protected override async ETTask Run(OnlinePlayerActor actor, G2P_GetCurrentRoom request, P2G_GetCurrentRoom response)
        {
            var result = await actor.GetCurrentRoom(request.SessionEpoch);
            response.Error = result.Error;
            response.Message = result.Message;
            response.HasRoom = result.HasRoom;
            response.Room = result.Room;
        }
    }

    [MessageHandler(SceneType.Player)]
    public class R2P_RoomChangedHandler: MessageHandler<OnlinePlayerActor, R2P_RoomChanged>
    {
        protected override async ETTask Run(OnlinePlayerActor actor, R2P_RoomChanged message)
        {
            await actor.ReceiveRoomChanged(message);
        }
    }
}
