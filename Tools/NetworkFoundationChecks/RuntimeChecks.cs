using Game.Network;
using ET;

internal static class RuntimeChecks
{
    internal static async Task Run(Action<bool,string> check)
    {
        var transport = new QueuedTransport();
        var runtime = new NetworkRuntime(() => new NetworkClock(), () => transport,
            () => new MemoryPackMessageCodec(), codec => new RpcMessageDispatcher(codec));
        runtime.Initialize();
        var endpoint = new NetworkEndpoint("localhost", 31001);
        var first = runtime.ConnectAsync(endpoint, 1000, default); runtime.Update(); await first;
        var pending = runtime.CallAsync<G2C_Login>(new C2G_Login { UserName = "fixture", Password = "test-only" }, 1000, default);
        var replacement = runtime.ConnectAsync(endpoint, 1000, default); runtime.Update(); await replacement;
        try { await pending; throw new Exception("旧RPC未失败"); } catch (IOException) { }
        check(runtime.PendingRpcCount == 0, "替换连接立即结束旧RPC，无自动重放");
        runtime.RegisterPushHandler<G2C_SessionReplaced>(_ => runtime.Dispose());
        transport.Events.Enqueue(ClientTransportEvent.FrameReceived(runtime.ConnectionGeneration,
            new MemoryPackMessageCodec().Encode(new G2C_SessionReplaced())));
        runtime.Update();
        check(!runtime.IsInitialized, "推送回调销毁运行时后本轮分发安全退出");

        var canceledTransport = new QueuedTransport();
        var canceledRuntime = new NetworkRuntime(() => new NetworkClock(), () => canceledTransport,
            () => new MemoryPackMessageCodec(), codec => new RpcMessageDispatcher(codec));
        canceledRuntime.Initialize();
        using var cancellation = new CancellationTokenSource();
        var canceledConnect = canceledRuntime.ConnectAsync(endpoint, 1000, cancellation.Token);
        cancellation.Cancel(); canceledRuntime.Update();
        try { await canceledConnect; throw new Exception("取消建连仍成功"); } catch (OperationCanceledException) { }
        check(canceledRuntime.ConnectionState == NetworkConnectionState.Disconnected && canceledTransport.Disconnected,
            "排队Connected不能复活取消建连，传输收到关闭");
        Boot.ClientReleasePolicy.OfflineSinglePlayer = true;
        try
        {
            try { await canceledRuntime.ConnectAsync(endpoint, 1000, default); throw new Exception("离线包意外联网"); }
            catch (InvalidOperationException) { }
            check(canceledRuntime.ConnectionState == NetworkConnectionState.Disconnected, "目标纯离线策略保留，连接未建立");
        }
        finally { Boot.ClientReleasePolicy.OfflineSinglePlayer = false; canceledRuntime.Dispose(); }
        var messageCodec = new MemoryPackMessageCodec();
        var bytes = Convert.FromBase64String("5S4JACUAAAAAAAAA/////3sAAAAAAAAAyAEAAAAAAAAHAAAAAAAAAAAB");
        var login = (G2C_Login)messageCodec.Decode(bytes).Message;
        check(login.RpcId == 37 && login.AccountId == 123 && login.PlayerId == 456 && login.SessionEpoch == 7 &&
            bytes.SequenceEqual(messageCodec.Encode(login)), "基础版固定服务端登录字节与目标真实生成协议双向兼容");
    }

    private sealed class QueuedTransport : IClientTransport
    {
        internal readonly Queue<ClientTransportEvent> Events = new();
        internal bool Disconnected;
        public bool IsConnected => !Disconnected;
        public int ConnectionGeneration { get; private set; }
        public Task ConnectAsync(int generation, NetworkEndpoint endpoint, int timeout, CancellationToken token)
        { ConnectionGeneration = generation; Disconnected = false; Events.Enqueue(ClientTransportEvent.Connected(generation)); return Task.CompletedTask; }
        public bool TryDequeue(out ClientTransportEvent value) => Events.TryDequeue(out value);
        public void Send(int generation, byte[] bytes) { }
        public void Pump(long now) { }
        public void Disconnect(NetworkDisconnectReason reason) => Disconnected = true;
        public void Dispose() { }
    }
}
