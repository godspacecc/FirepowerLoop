using Game.Network;
using ET;
using System.Net.Sockets;

/// <summary>真实目标Gate→Account→Player链路；只使用隔离夹具账号，不写玩家本地偏好。</summary>
internal static class NativeAccountChecks
{
    internal static async Task Run(string endpointText, string markerPath = null, bool resume = false)
    {
        if (!NetworkEndpoint.TryParse(endpointText, out var endpoint) || endpoint.Host != "127.0.0.1") throw new Exception("仅允许回环测试夹具");
        string user = "fixture_" + Guid.NewGuid().ToString("N")[..12];
        using var first = new Peer(); using var second = new Peer();
        await first.Connect(endpoint); await second.Connect(endpoint);
        if (resume)
        {
            var marker = System.Text.Json.JsonSerializer.Deserialize<IdentityMarker>(await File.ReadAllTextAsync(markerPath));
            if (!marker.User.StartsWith("fixture_", StringComparison.Ordinal)) throw new Exception("不是测试账号标记");
            var restored = await first.Call<G2C_Login>(new C2G_Login { UserName = marker.User, Password = "Password-123" });
            Check(restored.Error == 0 && restored.AccountId == marker.AccountId && restored.PlayerId == marker.PlayerId && restored.SessionEpoch > marker.Epoch,
                "真实服务重启后保留账号、玩家编号且会话世代继续增长");
            Check((await first.Call<G2C_Ping>(new C2G_Ping())).Error == 0, "重启后已认证心跳正常");
            return;
        }
        var missing = await first.Call<G2C_Login>(new C2G_Login { UserName = user, Password = "Password-123" });
        Check(missing.Error == 200104, "未知账号登录不自动注册");
        var weak = await first.Call<G2C_Register>(new C2G_Register { UserName = user, Password = "old123" });
        Check(weak.Error == 200102, "真实服务拒绝新账号短密码");
        var created = await first.Call<G2C_Register>(new C2G_Register { UserName = user, Password = "Password-123" });
        Check(created.Error == 0 && created.AccountId > 0, "显式注册写入账号库");
        var login = await first.Call<G2C_Login>(new C2G_Login { UserName = user, Password = "Password-123" });
        Check(login.Error == 0 && login.AccountId == created.AccountId && login.PlayerId > 0 && login.SessionEpoch > 0, "同连接登录绑定持久身份与玩家Actor");
        Check((await first.Call<G2C_Ping>(new C2G_Ping())).Error == 0, "已认证心跳通过持久世代校验");
        bool replaced = false; first.Runtime.RegisterPushHandler<G2C_SessionReplaced>(_ => replaced = true);
        var next = await second.Call<G2C_Login>(new C2G_Login { UserName = user, Password = "Password-123" });
        Check(next.Error == 0 && next.PlayerId == login.PlayerId && next.SessionEpoch > login.SessionEpoch, "第二连接复用玩家并递增持久世代");
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!replaced && DateTime.UtcNow < until) { first.Runtime.Update(); await Task.Delay(5); }
        Check(replaced, "旧连接收到顶号通知");
        Check((await second.Call<G2C_Ping>(new C2G_Ping())).Error == 0, "新连接顶号后心跳正常");
        if (markerPath != null) await File.WriteAllTextAsync(markerPath,
            System.Text.Json.JsonSerializer.Serialize(new IdentityMarker(user, next.AccountId, next.PlayerId, next.SessionEpoch)));
        Console.WriteLine("真实目标ET账号链路：8项通过；隔离数据库由启动夹具清理。");
    }
    private sealed record IdentityMarker(string User, long AccountId, long PlayerId, long Epoch);
    private static void Check(bool value, string text) { if (!value) throw new Exception(text); Console.WriteLine("PASS " + text); }
    private sealed class Peer : IDisposable
    {
        internal NetworkRuntime Runtime { get; } = new(() => new NetworkClock(), () => new TcpClientTransport(),
            () => new MemoryPackMessageCodec(), codec => new RpcMessageDispatcher(codec));
        internal Peer() { Runtime.Initialize(); }
        internal async Task Connect(NetworkEndpoint endpoint) => await Pump(Runtime.ConnectAsync(endpoint, 8000, default));
        internal async Task<T> Call<T>(IRequest request) where T : class, IResponse
        { var task = Runtime.CallAsync<T>(request, 10000, default); await Pump(task); return await task; }
        private async Task Pump(Task task)
        { var until = DateTime.UtcNow.AddSeconds(15); while (!task.IsCompleted && DateTime.UtcNow < until) { Runtime.Update(); await Task.Delay(5); } if (!task.IsCompleted) throw new TimeoutException(); await task; }
        public void Dispose() => Runtime.Dispose();
    }
}
