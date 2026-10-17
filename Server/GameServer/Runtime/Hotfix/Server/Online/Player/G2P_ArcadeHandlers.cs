namespace ET.Server
{
    [MessageHandler(SceneType.Player)]
    public sealed class G2P_StartOnlineArcadeRunHandler : MessageHandler<OnlinePlayerActor, G2P_StartOnlineArcadeRun, P2G_StartOnlineArcadeRun>
    {
        protected override async ETTask Run(OnlinePlayerActor actor, G2P_StartOnlineArcadeRun request, P2G_StartOnlineArcadeRun response)
        {
            var result = await actor.StartOnlineArcadeRun(request.SessionEpoch, request.OperationId);
            response.Error = result.Error;
            response.Message = result.Message;
            response.HasRun = result.HasRun;
            response.Run = result.Run;
        }
    }

    [MessageHandler(SceneType.Player)]
    public sealed class G2P_GetCurrentOnlineArcadeRunHandler : MessageHandler<OnlinePlayerActor, G2P_GetCurrentOnlineArcadeRun, P2G_GetCurrentOnlineArcadeRun>
    {
        protected override async ETTask Run(OnlinePlayerActor actor, G2P_GetCurrentOnlineArcadeRun request, P2G_GetCurrentOnlineArcadeRun response)
        {
            var result = await actor.GetCurrentOnlineArcadeRun(request.SessionEpoch);
            response.Error = result.Error;
            response.Message = result.Message;
            response.HasRun = result.HasRun;
            response.Run = result.Run;
        }
    }

    [MessageHandler(SceneType.Player)]
    public sealed class G2P_OperateOnlineArcadeSafeCheckpointHandler : MessageHandler<OnlinePlayerActor, G2P_OperateOnlineArcadeSafeCheckpoint, P2G_OperateOnlineArcadeSafeCheckpoint>
    {
        protected override async ETTask Run(OnlinePlayerActor actor, G2P_OperateOnlineArcadeSafeCheckpoint request,
            P2G_OperateOnlineArcadeSafeCheckpoint response)
        {
            var result = await actor.OperateOnlineArcadeSafeCheckpoint(request.SessionEpoch, request.OperationId,
                request.ExpectedRevision, request.ActionKind, request.Ready);
            response.Error = result.Error;
            response.Message = result.Message;
            response.Accepted = result.Accepted;
            response.Run = result.Run;
        }
    }

    [MessageHandler(SceneType.Player)]
    public sealed class R2P_OnlineArcadeRunChangedHandler : MessageHandler<OnlinePlayerActor, R2P_OnlineArcadeRunChanged>
    {
        protected override async ETTask Run(OnlinePlayerActor actor, R2P_OnlineArcadeRunChanged message)
        {
            await actor.ReceiveOnlineArcadeRunChanged(message);
        }
    }

    [MessageHandler(SceneType.Player)]
    public sealed class G2P_SubmitOnlineArcadeInputHandler : MessageHandler<OnlinePlayerActor, G2P_SubmitOnlineArcadeInput, P2G_SubmitOnlineArcadeInput>
    {
        protected override async ETTask Run(OnlinePlayerActor actor, G2P_SubmitOnlineArcadeInput request,
            P2G_SubmitOnlineArcadeInput response)
        {
            var result = await actor.SubmitOnlineArcadeInput(request.SessionEpoch, request.ArenaNumber,
                request.InputSequence, request.MoveX, request.MoveZ, request.AimX, request.AimZ, request.Firing,
                request.UseBomb, request.UseDash, request.RoundGeneration, request.RoundReady, request.ExitTarget, request.ConfirmExit,request.UseFieldUpgrade);
            response.Error = result.Error;
            response.Message = result.Message;
            response.Accepted = result.Accepted;
            response.LastAcceptedInputSequence = result.LastAcceptedInputSequence;
            response.Run = result.Run;
        }
    }
}
