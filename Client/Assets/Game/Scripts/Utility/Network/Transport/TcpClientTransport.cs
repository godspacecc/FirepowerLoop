using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Network
{
    internal interface IClientTransport : IDisposable
    {
        bool IsConnected { get; }
        int ConnectionGeneration { get; }
        Task ConnectAsync(int generation, NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken);
        void Send(int generation, byte[] body);
        bool TryDequeue(out ClientTransportEvent transportEvent);
        void Disconnect(NetworkDisconnectReason reason);
        void Pump(long nowMilliseconds);
    }

    internal enum ClientTransportEventKind
    {
        Connected,
        Frame,
        Disconnected
    }

    internal readonly struct ClientTransportEvent
    {
        private ClientTransportEvent(ClientTransportEventKind kind, int generation, byte[] frame, NetworkDisconnectReason reason, string diagnostic)
        {
            Kind = kind;
            Generation = generation;
            Frame = frame;
            Reason = reason;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public ClientTransportEventKind Kind { get; }
        public int Generation { get; }
        public byte[] Frame { get; }
        public NetworkDisconnectReason Reason { get; }
        public string Diagnostic { get; }

        public static ClientTransportEvent Connected(int generation)
        {
            return new ClientTransportEvent(ClientTransportEventKind.Connected, generation, null, default, string.Empty);
        }

        public static ClientTransportEvent FrameReceived(int generation, byte[] frame)
        {
            return new ClientTransportEvent(ClientTransportEventKind.Frame, generation, frame, default, string.Empty);
        }

        public static ClientTransportEvent Disconnected(int generation, NetworkDisconnectReason reason, string diagnostic)
        {
            return new ClientTransportEvent(ClientTransportEventKind.Disconnected, generation, null, reason, diagnostic);
        }
    }

    /// <summary>每次连接拥有独立 Socket、发送队列和取消源；后台只产出带代次的事件，由运行时主线程分发。</summary>
    internal sealed class TcpClientTransport : IClientTransport
    {
        private readonly object gate = new object();
        private readonly ConcurrentQueue<ClientTransportEvent> events = new ConcurrentQueue<ClientTransportEvent>();
        private Connection current;
        private bool disposed;
        private int generation;
        private sealed class Connection
        {
            internal int Generation;
            internal readonly TcpClient Client = new TcpClient();
            internal readonly CancellationTokenSource Stop = new CancellationTokenSource();
            internal readonly SemaphoreSlim Writer = new SemaphoreSlim(1, 1);
            internal NetworkStream Stream;
            internal int PendingWrites;
        }
        public bool IsConnected { get { lock (gate) return current?.Stream != null; } }
        public int ConnectionGeneration { get { lock (gate) return generation; } }

        /// <summary>先替换旧连接所有权；取消或失败只清理本次实例，迟到完成不能覆盖新连接。</summary>
        public async Task ConnectAsync(int generation, NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            if (timeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds), "连接超时必须为正数。");
            cancellationToken.ThrowIfCancellationRequested();
            var next = new Connection { Generation = generation };
            Connection old;
            lock (gate)
            {
                if (disposed) { next.Client.Dispose(); next.Stop.Dispose(); throw new ObjectDisposedException(nameof(TcpClientTransport)); }
                old = current;
                current = next;
                this.generation = generation;
            }
            Stop(old);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, next.Stop.Token);
            deadline.CancelAfter(timeoutMilliseconds);
            try
            {
                using (deadline.Token.Register(() => next.Client.Close()))
                {
                    await next.Client.ConnectAsync(endpoint.Host, endpoint.Port).ConfigureAwait(false);
                    deadline.Token.ThrowIfCancellationRequested();
                    lock (gate)
                    {
                        if (!ReferenceEquals(current, next)) throw new OperationCanceledException("连接已被替换。");
                        next.Stream = next.Client.GetStream();
                        events.Enqueue(ClientTransportEvent.Connected(generation));
                    }
                }
                _ = ReadLoop(next);
            }
            catch (Exception)
            {
                bool canceled = cancellationToken.IsCancellationRequested || next.Stop.IsCancellationRequested;
                Close(next, NetworkDisconnectReason.ConnectFailed, "连接失败");
                if (canceled) throw new OperationCanceledException("连接已取消。", cancellationToken);
                if (deadline.IsCancellationRequested) throw new TimeoutException("网络连接超时。");
                throw;
            }
        }

        /// <summary>立即复制并入队，最多64帧；异步串行写入，8秒预算包含等待发送的时间。</summary>
        public void Send(int generation, byte[] body)
        {
            var frame = TcpFrameCodec.WriteFrame(body);
            Connection connection;
            lock (gate)
            {
                connection = current;
                if (connection == null || connection.Generation != generation || connection.Stream == null)
                    throw new IOException("TCP 连接未激活。");
                if (connection.PendingWrites >= 64) throw new IOException("TCP 发送队列已满。");
                connection.PendingWrites++;
            }
            _ = Write(connection, frame);
        }
        private async Task Write(Connection connection, byte[] frame)
        {
            bool acquired = false;
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(connection.Stop.Token);
                deadline.CancelAfter(8000);
                await connection.Writer.WaitAsync(deadline.Token).ConfigureAwait(false);
                acquired = true;
                using (deadline.Token.Register(() => Close(connection, NetworkDisconnectReason.TransportError, "发送超时或连接取消")))
                    await connection.Stream.WriteAsync(frame, 0, frame.Length, deadline.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                Close(connection, NetworkDisconnectReason.TransportError, "发送失败");
            }
            finally
            {
                if (acquired) connection.Writer.Release();
                lock (gate) connection.PendingWrites--;
            }
        }
        private async Task ReadLoop(Connection connection)
        {
            var parser = new TcpFrameAssembler();
            var buffer = new byte[8192];
            try
            {
                while (!connection.Stop.IsCancellationRequested)
                {
                    int count = await connection.Stream.ReadAsync(buffer, 0, buffer.Length, connection.Stop.Token).ConfigureAwait(false);
                    if (count == 0) { Close(connection, NetworkDisconnectReason.RemoteClosed, "对端关闭"); return; }
                    foreach (var frame in parser.Append(buffer, count))
                    {
                        lock (gate)
                        {
                            if (!ReferenceEquals(current, connection)) return;
                            if (events.Count >= 256) throw new IOException("TCP 接收事件队列已满。");
                            events.Enqueue(ClientTransportEvent.FrameReceived(connection.Generation, frame));
                        }
                    }
                }
            }
            catch (InvalidDataException) { Close(connection, NetworkDisconnectReason.ProtocolError, "无效帧"); }
            catch (Exception) { Close(connection, NetworkDisconnectReason.TransportError, "接收失败"); }
        }
        /// <summary>在同一临界区核对所有权并摘除连接；外部回调永远不能关闭替换后的 Socket。</summary>
        private void Close(Connection connection, NetworkDisconnectReason reason, string diagnostic)
        {
            lock (gate)
            {
                if (!ReferenceEquals(current, connection)) return;
                current = null;
                events.Enqueue(ClientTransportEvent.Disconnected(connection.Generation, reason, diagnostic));
            }
            Stop(connection);
        }
        private static void Stop(Connection connection)
        {
            if (connection == null) return;
            connection.Stop.Cancel();
            connection.Client.Close();
            // 取消源和信号量仍由在途异步操作访问；不提前 Dispose，随连接对象回收。
        }
        public bool TryDequeue(out ClientTransportEvent transportEvent) => events.TryDequeue(out transportEvent);
        public void Pump(long nowMilliseconds) { }
        public void Disconnect(NetworkDisconnectReason reason)
        {
            Connection connection;
            lock (gate) connection = current;
            if (connection != null) Close(connection, reason, "主动断开");
        }
        public void Dispose()
        {
            lock (gate) disposed = true;
            Disconnect(NetworkDisconnectReason.Disposed);
        }
    }
    /// <summary>按 FirepowerLoop 外网协议拼接半包与粘包；每条连接独享一个实例，断线后丢弃。</summary>
    internal sealed class TcpFrameAssembler
    {
        private readonly List<byte> pending = new List<byte>();

        /// <summary>立即复制输入并返回完整帧快照；非法参数不改变状态，结果可重复枚举。</summary>
        public IEnumerable<byte[]> Append(byte[] buffer, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer), "网络输入缓冲区为空。");
            if (count < 0 || count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count), "网络输入长度超出缓冲区范围。");
            var frames = new List<byte[]>();
            for (int i = 0; i < count; i++)
            {
                pending.Add(buffer[i]);
            }

            while (pending.Count >= 2)
            {
                int length = pending[0] | (pending[1] << 8);
                if (length < 2)
                {
                    throw new InvalidDataException("TCP 包体长度无效。");
                }
                if (pending.Count < length + 2)
                {
                    break;
                }

                byte[] frame = pending.GetRange(2, length).ToArray();
                pending.RemoveRange(0, length + 2);
                frames.Add(frame);
            }
            return frames;
        }
    }

    /// <summary>编码两字节小端长度前缀，正文包含 Opcode，长度范围为 2 至 65535。</summary>
    internal static class TcpFrameCodec
    {
        public static byte[] WriteFrame(byte[] body)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }
            if (body.Length < 2 || body.Length > ushort.MaxValue)
            {
                throw new InvalidDataException("TCP 包体长度无效。");
            }

            var frame = new byte[body.Length + 2];
            frame[0] = (byte)body.Length;
            frame[1] = (byte)(body.Length >> 8);
            Buffer.BlockCopy(body, 0, frame, 2, body.Length);
            return frame;
        }
    }
}
