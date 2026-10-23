namespace ET.Server
{
    /// <summary>房间连接状态的异步桥接，避免房间与街机系统形成静态调用环。</summary>
    [MessageHandler(SceneType.OnlineRoom)]
    public sealed class R2R_OnlineArcadeConnectionChangedHandler : MessageHandler<Scene, R2R_OnlineArcadeConnectionChanged>
    {
        protected override async ETTask Run(Scene scene, R2R_OnlineArcadeConnectionChanged message)
        {
            OnlineArcadeRunManagerComponent manager = scene.GetComponent<OnlineArcadeRunManagerComponent>();
            if (manager != null)
                await manager.SetPlayerConnection(message.RoomId, message.PlayerId, message.Connected);
        }
    }

    [MessageHandler(SceneType.OnlineRoom)]
    public sealed class P2R_StartOnlineArcadeRunHandler : MessageHandler<OnlineRoomActor, P2R_StartOnlineArcadeRun, R2P_StartOnlineArcadeRun>
    {
        protected override async ETTask Run(OnlineRoomActor room, P2R_StartOnlineArcadeRun request, R2P_StartOnlineArcadeRun response)
        {
            try
            {
                OnlineArcadeRunManagerComponent manager = room.Root().GetComponent<OnlineArcadeRunManagerComponent>();
                OnlineArcadeRunRecord record = await manager.CreateForRoom(room, request.PlayerId, request.OperationId);
                response.Error = ErrorCode.ERR_Success;
                response.HasRun = true;
                response.Run = OnlineArcadeRunActorSystem.BuildSnapshot(record);
            }
            catch (RpcException exception)
            {
                response.Error = exception.Error;
                response.Message = exception.Message;
            }
        }
    }

    [MessageHandler(SceneType.OnlineRoom)]
    public sealed class P2R_GetCurrentOnlineArcadeRunHandler : MessageHandler<Scene, P2R_GetCurrentOnlineArcadeRun, R2P_GetCurrentOnlineArcadeRun>
    {
        protected override async ETTask Run(Scene scene, P2R_GetCurrentOnlineArcadeRun request, R2P_GetCurrentOnlineArcadeRun response)
        {
            OnlineArcadeRunActor actor = scene.GetComponent<OnlineArcadeRunManagerComponent>().GetActorForPlayer(request.PlayerId);
            response.Error = ErrorCode.ERR_Success;
            response.HasRun = actor != null && !actor.IsDisposed;
            response.Run = response.HasRun ? actor.BuildSnapshotForPlayer(request.PlayerId) : null;
            await ETTask.CompletedTask;
        }
    }

    [MessageHandler(SceneType.OnlineRoom)]
    public sealed class P2R_OperateOnlineArcadeSafeCheckpointHandler : MessageHandler<Scene, P2R_OperateOnlineArcadeSafeCheckpoint, R2P_OperateOnlineArcadeSafeCheckpoint>
    {
        protected override async ETTask Run(Scene scene, P2R_OperateOnlineArcadeSafeCheckpoint request,
            R2P_OperateOnlineArcadeSafeCheckpoint response)
        {
            try
            {
                var result = await scene.GetComponent<OnlineArcadeRunManagerComponent>().OperateSafeCheckpoint(
                    request.PlayerId, request.OperationId, request.ExpectedRevision, request.ActionKind,
                    request.Ready, request.ScoreShopDropId);
                response.Error = ErrorCode.ERR_Success;
                response.Accepted = result.Accepted;
                response.Run = result.Snapshot;
            }
            catch (RpcException exception)
            {
                response.Error = exception.Error;
                response.Message = exception.Message;
                response.Run = scene.GetComponent<OnlineArcadeRunManagerComponent>().GetActorForPlayer(request.PlayerId)?.BuildSnapshotForPlayer(request.PlayerId);
            }
        }
    }

    [MessageHandler(SceneType.OnlineRoom)]
    public sealed class P2R_SubmitOnlineArcadeInputHandler : MessageHandler<Scene, P2R_SubmitOnlineArcadeInput, R2P_SubmitOnlineArcadeInput>
    {
        protected override async ETTask Run(Scene scene, P2R_SubmitOnlineArcadeInput request,
            R2P_SubmitOnlineArcadeInput response)
        {
            try
            {
                var result = scene.GetComponent<OnlineArcadeRunManagerComponent>().SubmitBattleInput(
                    request.PlayerId, request.ArenaNumber, request.InputSequence, request.MoveX, request.MoveZ,
                    request.AimX, request.AimZ, request.Firing, request.UseBomb, request.UseDash, request.RoundGeneration, request.RoundReady, request.ExitTarget, request.ConfirmExit,request.UseFieldUpgrade,request.DonationRunId,request.DonateLifeTargetId);
                response.Error = ErrorCode.ERR_Success;
                response.Accepted = result.Accepted;
                response.LastAcceptedInputSequence = result.LastAcceptedInputSequence;
                response.Run = result.Snapshot;
            }
            catch (RpcException exception)
            {
                response.Error = exception.Error;
                response.Message = exception.Message;
            }
            await ETTask.CompletedTask;
        }
    }

    /// <summary>在联机街机权威场景执行房主测试指令；网关只负责转发，不直接接触战斗状态。</summary>
    [MessageHandler(SceneType.OnlineRoom)]
    public sealed class G2R_ExecuteOnlineArcadeTestCmdHandler :
        MessageHandler<Scene, G2R_ExecuteOnlineArcadeTestCmd, R2G_ExecuteOnlineArcadeTestCmd>
    {
        protected override async ETTask Run(Scene scene, G2R_ExecuteOnlineArcadeTestCmd request,
            R2G_ExecuteOnlineArcadeTestCmd response)
        {
            try
            {
                ServerTestCmdRunResult result = await scene.GetComponent<OnlineArcadeRunManagerComponent>()
                    .ExecuteTestCommand(request.PlayerId, request.ArgumentText);
                response.Error = ErrorCode.ERR_Success;
                response.Success = result.Success;
                response.ResultCode = (int)result.Code;
                response.Message = result.Message;
                response.ServerTime = result.ServerTime;
            }
            catch (RpcException exception)
            {
                response.Error = exception.Error;
                response.ResultCode = (int)ServerTestCmdResultCode.ExecutionFailed;
                response.Message = exception.Message;
                response.ServerTime = TimeInfo.Instance.ServerNow();
            }
        }
    }
}
