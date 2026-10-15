namespace ET.Server
{
    [ServerTestCmdHandler("server.ping", "ping", "server.general", 0, "服务器", 0, "Ping", "$ping",
        "检查服务器测试指令通道", "$ping", "$ping")]
    public sealed class ServerPingTestCmdHandler: IServerTestCmdHandler
    {
        public async ETTask<ServerTestCmdRunResult> Run(Scene root, Session session, string argumentText)
        {
            await ETTask.CompletedTask;
            if (!string.IsNullOrWhiteSpace(argumentText))
                return new ServerTestCmdRunResult(true, false, ServerTestCmdResultCode.InvalidInput, "用法：$ping");
            return new ServerTestCmdRunResult(true, true, ServerTestCmdResultCode.Success, "pong", TimeInfo.Instance.ServerNow());
        }
    }
}
