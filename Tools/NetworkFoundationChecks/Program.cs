using System.Net;
using System.Net.Sockets;
using Game.Network;

if (args.Length >= 2 && args[0] == "--native") { await NativeAccountChecks.Run(args[1], args.Length > 2 ? args[2] : null, args.Length > 3 && args[3] == "--resume"); return; }

int passed = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); passed++; }
Check(!NetworkEndpoint.TryParse("user:password@localhost:31001", out _) &&
    !NetworkEndpoint.TryParse("https://localhost/path:31001", out _) && NetworkEndpoint.TryParse("localhost:31001", out _), "端点拒绝凭据和URL，接受主机及端口");
_ = default(NetworkEndpoint).GetHashCode();
var parser = new TcpFrameAssembler();
parser.Append(new byte[] { 2 }, 1);
try { parser.Append(new byte[] { 0 }, 2); throw new Exception("未拒绝长度越界"); } catch (ArgumentOutOfRangeException) { }
byte[] tail = { 0, 3, 4 };
var snapshot = parser.Append(tail, 3); tail[1] = 99;
Check(snapshot.Single().SequenceEqual(new byte[] { 3, 4 }) && snapshot.Single()[0] == 3, "分帧立即复制且重复枚举不改变状态");
byte[] body = Enumerable.Repeat((byte)0xA5, ushort.MaxValue).ToArray();
var frame = TcpFrameCodec.WriteFrame(body);
Check(new TcpFrameAssembler().Append(frame, frame.Length).Single().SequenceEqual(body), "最大65535字节帧保持字节一致");
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
using var transport = new TcpClientTransport();
var endpoint = new NetworkEndpoint("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
var accept = listener.AcceptTcpClientAsync(deadline.Token);
await transport.ConnectAsync(1, endpoint, 2000, deadline.Token);
using var old = await accept;
transport.Send(1, new byte[] { 1, 2, 3 });
byte[] wire = new byte[5]; await old.GetStream().ReadExactlyAsync(wire, deadline.Token);
Check(wire.SequenceEqual(new byte[] { 3, 0, 1, 2, 3 }), "真实TCP异步发送的帧字节一致");
accept = listener.AcceptTcpClientAsync(deadline.Token);
await transport.ConnectAsync(2, endpoint, 2000, deadline.Token);
using var next = await accept; old.Dispose();
await next.GetStream().WriteAsync(new byte[] { 2, 0, 9, 8 }, deadline.Token);
bool received = false;
while (!received)
{
    deadline.Token.ThrowIfCancellationRequested();
    while (transport.TryDequeue(out var item))
        if (item.Generation == 2 && item.Kind == ClientTransportEventKind.Frame) received = item.Frame.SequenceEqual(new byte[] { 9, 8 });
    if (!received) await Task.Delay(5, deadline.Token);
}
Check(transport.IsConnected, "旧Socket关闭不影响替换连接收包");
try { transport.Send(1, new byte[] { 1, 2 }); throw new Exception("未拒绝旧代次发送"); } catch (IOException) { }
transport.Disconnect(NetworkDisconnectReason.LocalRequest);
Check(!transport.IsConnected, "主动断开清理物理连接");
using var canceled = new CancellationTokenSource(); canceled.Cancel();
try { await transport.ConnectAsync(3, endpoint, 2000, canceled.Token); throw new Exception("未拒绝取消的建连"); } catch (OperationCanceledException) { }
Check(!transport.IsConnected, "已取消请求不创建连接");
await RuntimeChecks.Run(Check);
Console.WriteLine($"网络独立检查通过：{passed}项；不代替Unity账号/玩法验收。");
