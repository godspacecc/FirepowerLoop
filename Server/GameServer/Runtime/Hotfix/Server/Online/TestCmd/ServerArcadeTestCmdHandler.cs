namespace ET.Server
{
    /// <summary>
    /// 把联机街机测试请求转交给 OnlineRoom 权威场景；不在网关缓存或修改任何街机状态。
    /// </summary>
    [ServerTestCmdHandler("server.arcade", "arcade", "server.arcade", 10, "联机街机", 0,
        "联机街机调试", "$arcade <子指令>",
        "由房主在进行中的双人街机局内测试生命、资源、掉落与关卡推进",
        "$arcade status|heal|damage <数值>|gold <数值>|bombs <数值>|dashes <数值>|drop <DropId>|clear wave|clear arena",
        "$arcade status")]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public sealed class ServerArcadeTestCmdHandler : IServerTestCmdHandler
    {
        public async ETTask<ServerTestCmdRunResult> Run(Scene root, Session session, string argumentText)
        {
            GatePlayerProxy proxy = session?.GetComponent<OnlineSessionComponent>()?.Proxy;
            if (proxy == null || !proxy.IsAuthenticated)
                return Failure("请先登录后再执行联机街机测试指令");

            try
            {
                ActorId roomScene = ServerConfig.Instance.GetSceneByName(session.Zone(), "OnlineRoom").ActorId;
                G2R_ExecuteOnlineArcadeTestCmd request = G2R_ExecuteOnlineArcadeTestCmd.Create();
                request.PlayerId = proxy.PlayerId;
                request.ArgumentText = argumentText ?? string.Empty;
                using R2G_ExecuteOnlineArcadeTestCmd response = await root.GetComponent<MessageSender>()
                    .Call(roomScene, request, false) as R2G_ExecuteOnlineArcadeTestCmd;
                if (response == null)
                    return Failure("联机街机测试服务未返回结果");
                if (response.Error != ErrorCode.ERR_Success)
                    return Failure(string.IsNullOrWhiteSpace(response.Message)
                        ? "联机街机测试指令执行失败"
                        : response.Message);
                return new ServerTestCmdRunResult(true, response.Success,
                    (ServerTestCmdResultCode)response.ResultCode, response.Message, response.ServerTime);
            }
            catch (System.Exception exception)
            {
                Log.Error($"online arcade test command failed playerId={proxy.PlayerId} exception={exception}");
                return Failure("联机街机测试服务暂不可用");
            }
        }

        private static ServerTestCmdRunResult Failure(string message) =>
            new(true, false, ServerTestCmdResultCode.ExecutionFailed, message, TimeInfo.Instance.ServerNow());
    }
}
