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

    internal sealed class TcpClientTransport : IClientTransport
    {
        private readonly ConcurrentQueue<ClientTransportEvent> events = new ConcurrentQueue<ClientTransportEvent>();
        private readonly object sendGate = new object();
        private readonly object stateGate = new object();
        private TcpClient client;
        private NetworkStream stream;
        private CancellationTokenSource connectionCancellation;
        private bool disconnectPublished;
        private bool disposed;

        public bool IsConnected { get; private set; }
        public int ConnectionGeneration { get; private set; }

        public async Task ConnectAsync(int generation, NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            if (timeoutMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            }

            ReleaseCurrentConnection();
            ThrowIfDisposed();
            var candidateCancellation = new CancellationTokenSource();
            CancellationTokenSource connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCancellation.CancelAfter(timeoutMilliseconds);
            lock (stateGate)
            {
                ConnectionGeneration = generation;
                connectionCancellation?.Dispose();
                connectionCancellation = candidateCancellation;
                disconnectPublished = false;
            }

            var newClient = new TcpClient();
            try
            {
                Task connectTask = newClient.ConnectAsync(endpoint.Host, endpoint.Port);
                Task completed = await Task.WhenAny(connectTask, Task.Delay(Timeout.Infinite, connectCancellation.Token));
                if (completed != connectTask)
                {
                    throw new OperationCanceledException(connectCancellation.Token);
                }

                await connectTask;
                NetworkStream newStream = newClient.GetStream();
                lock (stateGate)
                {
                    if (disposed || generation != ConnectionGeneration || !ReferenceEquals(candidateCancellation, connectionCancellation))
                    {
                        newStream.Dispose();
                        newClient.Dispose();
                        return;
                    }

                    stream = newStream;
                    client = newClient;
                    IsConnected = true;
                    disconnectPublished = false;
                }
                events.Enqueue(ClientTransportEvent.Connected(generation));
                _ = Task.Run(() => ReadLoop(generation, newStream, candidateCancellation.Token));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                newClient.Dispose();
                ReleaseIfCurrent(generation, candidateCancellation);
                throw new TimeoutException("网络连接超时。");
            }
            catch
            {
                newClient.Dispose();
                ReleaseIfCurrent(generation, candidateCancellation);
                throw;
            }
            finally
            {
                connectCancellation.Dispose();
            }
        }

        public void Send(int generation, byte[] body)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }
            if (generation != ConnectionGeneration || !IsConnected || stream == null)
            {
                throw new IOException("TCP 连接未激活。");
            }

            byte[] frame = TcpFrameCodec.WriteFrame(body);
            try
            {
                lock (sendGate)
                {
                    stream.Write(frame, 0, frame.Length);
                }
            }
            catch (Exception e)
            {
                PublishDisconnectOnce(generation, NetworkDisconnectReason.TransportError, e.GetType().Name);
                ReleaseCurrentConnection();
                throw;
            }
        }

        public bool TryDequeue(out ClientTransportEvent transportEvent)
        {
            return events.TryDequeue(out transportEvent);
        }

        public void Pump(long nowMilliseconds)
        {
        }

        public void Disconnect(NetworkDisconnectReason reason)
        {
            int generation = ConnectionGeneration;
            ReleaseCurrentConnection();
            PublishDisconnectOnce(generation, reason, reason.ToString());
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Disconnect(NetworkDisconnectReason.Disposed);
            connectionCancellation?.Dispose();
            connectionCancellation = null;
        }

        private async Task ReadLoop(int generation, NetworkStream readStream, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            var assembler = new TcpFrameAssembler();
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int count = await readStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (count == 0)
                    {
                        ReleaseIfCurrent(generation, readStream);
                        PublishDisconnectOnce(generation, NetworkDisconnectReason.RemoteClosed, "eof");
                        return;
                    }

                    foreach (byte[] frame in assembler.Append(buffer, count))
                    {
                        events.Enqueue(ClientTransportEvent.FrameReceived(generation, frame));
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (InvalidDataException e)
            {
                ReleaseIfCurrent(generation, readStream);
                PublishDisconnectOnce(generation, NetworkDisconnectReason.ProtocolError, e.GetType().Name);
            }
            catch (Exception e)
            {
                ReleaseIfCurrent(generation, readStream);
                PublishDisconnectOnce(generation, NetworkDisconnectReason.TransportError, e.GetType().Name);
            }
        }

        private void ReleaseCurrentConnection()
        {
            NetworkStream oldStream = null;
            TcpClient oldClient = null;
            CancellationTokenSource oldCancellation = null;
            lock (stateGate)
            {
                IsConnected = false;
                oldStream = stream;
                oldClient = client;
                oldCancellation = connectionCancellation;
                stream = null;
                client = null;
                connectionCancellation = null;
            }

            try
            {
                oldCancellation?.Cancel();
            }
            catch (ObjectDisposedException exception)
            {
                Log.Warning($"[TcpClientTransport] 取消连接清理时令牌已释放：generation={ConnectionGeneration} exception={exception}");
            }
            catch (Exception exception)
            {
                Log.Error($"[TcpClientTransport] 取消连接清理异常：generation={ConnectionGeneration} exception={exception}");
            }
            oldStream?.Dispose();
            oldClient?.Dispose();
            oldCancellation?.Dispose();
        }

        private void ReleaseIfCurrent(int generation, NetworkStream readStream)
        {
            lock (stateGate)
            {
                if (generation != ConnectionGeneration || !ReferenceEquals(stream, readStream))
                {
                    return;
                }
            }

            ReleaseCurrentConnection();
        }

        private void ReleaseIfCurrent(int generation, CancellationTokenSource cancellation)
        {
            lock (stateGate)
            {
                if (generation != ConnectionGeneration || !ReferenceEquals(connectionCancellation, cancellation))
                {
                    return;
                }
            }

            ReleaseCurrentConnection();
        }

        private void PublishDisconnectOnce(int generation, NetworkDisconnectReason reason, string diagnostic)
        {
            lock (stateGate)
            {
                if (generation != ConnectionGeneration || disconnectPublished)
                {
                    return;
                }
                disconnectPublished = true;
                IsConnected = false;
            }

            events.Enqueue(ClientTransportEvent.Disconnected(generation, reason, diagnostic));
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(TcpClientTransport));
            }
        }
    }

    internal sealed class TcpFrameAssembler
    {
        private readonly List<byte> pending = new List<byte>();

        public IEnumerable<byte[]> Append(byte[] buffer, int count)
        {
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
                    yield break;
                }

                byte[] frame = pending.GetRange(2, length).ToArray();
                pending.RemoveRange(0, length + 2);
                yield return frame;
            }
        }
    }

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
