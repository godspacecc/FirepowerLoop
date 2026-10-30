using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Game.Network;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Game.Tests
{
    /// <summary>使用真实回环 TCP 验证 ET 帧、替换连接和取消，不访问账号数据库。</summary>
    public sealed class EtNetworkTransportTests
    {
        [Test]
        public void PushHandlerCanDisposeRuntimeDuringUpdate()
        {
            var transport = new QueuedTransport();
            var runtime = new global::Game.Network.NetworkRuntime(() => new NetworkClock(), () => transport,
                () => new MemoryPackMessageCodec(), codec => new RpcMessageDispatcher(codec));
            try
            {
                runtime.Initialize();
                var connect = runtime.ConnectAsync(new NetworkEndpoint("localhost", 31011), 1000, default);
                runtime.Update();
                using var subscription = runtime.RegisterPushHandler<ET.G2C_SessionReplaced>(_ => runtime.Dispose());
                transport.Events.Enqueue(ClientTransportEvent.FrameReceived(1, new MemoryPackMessageCodec().Encode(new ET.G2C_SessionReplaced())));
                Assert.DoesNotThrow(() => runtime.Update());
                Assert.AreEqual(global::Game.Network.NetworkConnectionState.Disposed, runtime.ConnectionState);
            }
            finally { runtime.Dispose(); }
        }

        private sealed class QueuedTransport : IClientTransport
        {
            internal readonly System.Collections.Generic.Queue<ClientTransportEvent> Events = new System.Collections.Generic.Queue<ClientTransportEvent>();
            public bool IsConnected => true;
            public int ConnectionGeneration => 1;
            public Task ConnectAsync(int generation, NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken)
            { Events.Enqueue(ClientTransportEvent.Connected(generation)); return Task.CompletedTask; }
            public void Send(int generation, byte[] body) { }
            public bool TryDequeue(out ClientTransportEvent evt)
            { if (Events.Count > 0) { evt = Events.Dequeue(); return true; } evt = default; return false; }
            public void Pump(long nowMilliseconds) { }
            public void Disconnect(global::Game.Network.NetworkDisconnectReason reason) { }
            public void Dispose() { }
        }

        [UnityTest]
        public IEnumerator CancelBeforeConnectedEventClosesPhysicalSocket()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var runtime = new global::Game.Network.NetworkRuntime();
            using var cancel = new CancellationTokenSource();
            var end = DateTime.UtcNow.AddSeconds(5);
            try
            {
                runtime.Initialize();
                var connect = runtime.ConnectAsync(new NetworkEndpoint("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port), 2000, cancel.Token);
                while (!runtime.IsTransportConnected && !connect.IsCompleted && DateTime.UtcNow < end) yield return null;
                Assert.IsTrue(runtime.IsTransportConnected);
                cancel.Cancel();
                runtime.Update(); // 不让出主线程，立即分发排队的 Connected，复现取消与成功通知竞争。
                while (!connect.IsCompleted && DateTime.UtcNow < end) yield return null;
                Assert.IsTrue(connect.IsCanceled);
                Assert.IsFalse(runtime.IsTransportConnected);
                runtime.Update();
                Assert.AreEqual(global::Game.Network.NetworkConnectionState.Disconnected, runtime.ConnectionState);
            }
            finally { runtime.Dispose(); listener.Stop(); }
        }

        [Test]
        public void EndpointRejectsCredentialsAndUrls()
        {
            Assert.IsFalse(NetworkEndpoint.TryParse("user:password@localhost:31011", out _));
            Assert.IsFalse(NetworkEndpoint.TryParse("https://localhost/path:31011", out _));
            Assert.IsTrue(NetworkEndpoint.TryParse("localhost:31011", out _));
            Assert.DoesNotThrow(() => default(NetworkEndpoint).GetHashCode());
        }

        [UnityTest]
        public IEnumerator RuntimeLoginRpcUsesRealTcpAndMainThreadPump()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var runtime = new global::Game.Network.NetworkRuntime();
            using var stop = new CancellationTokenSource(5000);
            try
            {
                runtime.Initialize();
                Assert.AreEqual(global::Game.Network.NetworkConnectionState.Disconnected, runtime.ConnectionState);
                var peer = RespondToLogin(listener, stop.Token);
                var connect = runtime.ConnectAsync(new NetworkEndpoint("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port), 2000, stop.Token);
                while (!connect.IsCompleted && !stop.IsCancellationRequested) { runtime.Update(); yield return null; }
                Assert.IsTrue(connect.IsCompleted, "连接等待主线程分发超时");
                connect.GetAwaiter().GetResult();
                var login = runtime.CallAsync<ET.G2C_Login>(new ET.C2G_Login { UserName = "fixture", Password = "test-only" }, 2000, stop.Token);
                while (!login.IsCompleted && !stop.IsCancellationRequested) { runtime.Update(); yield return null; }
                Assert.IsTrue(login.IsCompleted, "登录响应超时");
                Assert.AreEqual(123, login.GetAwaiter().GetResult().AccountId);
                peer.GetAwaiter().GetResult();
                runtime.Disconnect(global::Game.Network.NetworkDisconnectReason.LocalRequest);
                runtime.Update();
                Assert.AreEqual(global::Game.Network.NetworkConnectionState.Disconnected, runtime.ConnectionState);
            }
            finally { runtime.Dispose(); listener.Stop(); }
        }

        private static async Task RespondToLogin(TcpListener listener, CancellationToken token)
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var parser = new TcpFrameAssembler();
            var buffer = new byte[1024];
            while (true)
            {
                int count = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                if (count == 0) throw new IOException("测试客户端提前断开");
                foreach (var frame in parser.Append(buffer, count))
                {
                    var codec = new MemoryPackMessageCodec();
                    var request = (ET.C2G_Login)codec.Decode(frame).Message;
                    Assert.AreEqual("fixture", request.UserName);
                    var response = TcpFrameCodec.WriteFrame(codec.Encode(new ET.G2C_Login { RpcId = request.RpcId, AccountId = 123 }));
                    await stream.WriteAsync(response, 0, response.Length, token);
                    return;
                }
            }
        }

        [UnityTest]
        public IEnumerator FramesRoundTripAndOldSocketCannotCloseReplacement()
        {
            Task task = Task.Run(async () =>
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                try
                {
                    using var transport = new TcpClientTransport();
                    var endpoint = new NetworkEndpoint("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
                    var accept = listener.AcceptTcpClientAsync();
                    await transport.ConnectAsync(1, endpoint, 2000, default);
                    using var old = await accept;
                    transport.Send(1, new byte[] { 1, 2, 3 });
                    var bytes = new byte[5];
                    using var timeout = new CancellationTokenSource(3000);
                    int offset = 0;
                    while (offset < bytes.Length)
                        offset += await old.GetStream().ReadAsync(bytes, offset, bytes.Length - offset, timeout.Token);
                    CollectionAssert.AreEqual(new byte[] { 3, 0, 1, 2, 3 }, bytes);
                    accept = listener.AcceptTcpClientAsync();
                    await transport.ConnectAsync(2, endpoint, 2000, default);
                    using var replacement = await accept;
                    old.Close();
                    await replacement.GetStream().WriteAsync(new byte[] { 2, 0, 9, 8 }, 0, 4);
                    bool seen = false;
                    for (int i = 0; i < 300 && !seen; i++)
                    {
                        while (transport.TryDequeue(out var evt))
                        {
                            if (evt.Generation == 2 && evt.Kind == ClientTransportEventKind.Frame)
                            {
                                CollectionAssert.AreEqual(new byte[] { 9, 8 }, evt.Frame);
                                seen = true;
                            }
                        }
                        if (!seen) await Task.Delay(10);
                    }
                    Assert.IsTrue(seen);
                    Assert.IsTrue(transport.IsConnected);
                    Assert.Throws<IOException>(() => transport.Send(1, new byte[] { 1, 2 }));
                }
                finally { listener.Stop(); }
            });
            var end = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < end) yield return null;
            Assert.IsTrue(task.IsCompleted, "回环测试超时");
            task.GetAwaiter().GetResult();
        }

        [Test]
        public void CanceledConnectDoesNotCreateConnection()
        {
            using var transport = new TcpClientTransport();
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            var task = transport.ConnectAsync(1, new NetworkEndpoint("127.0.0.1", 1), 1000, cancel.Token);
            Assert.IsTrue(task.IsCanceled);
            Assert.IsFalse(transport.IsConnected);
            Assert.IsFalse(transport.TryDequeue(out _));
        }
    }
}
