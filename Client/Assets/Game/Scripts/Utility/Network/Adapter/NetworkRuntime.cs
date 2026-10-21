using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ET;

[assembly: InternalsVisibleTo("FirepowerLoop.EditMode.Tests")]

namespace Game.Network
{
    public sealed class NetworkRuntime : INetworkRuntime, INetworkResponseObserver
    {
        private readonly Func<INetworkClock> clockFactory;
        private readonly Func<IClientTransport> transportFactory;
        private readonly Func<IMessageCodec> codecFactory;
        private readonly Func<IMessageCodec, RpcMessageDispatcher> rpcFactory;
        private INetworkClock clock;
        private IClientTransport transport;
        private IMessageCodec codec;
        private RpcMessageDispatcher rpc;
        private readonly Dictionary<Type, List<Delegate>> pushHandlers = new Dictionary<Type, List<Delegate>>();
        private readonly Dictionary<Type, List<Delegate>> responseHandlers = new Dictionary<Type, List<Delegate>>();
        private readonly List<Action<NetworkDisconnected>> disconnectedHandlers = new List<Action<NetworkDisconnected>>();
        private PendingConnect pendingConnect;
        private bool disposed = true;
        private int connectionGeneration;

        public NetworkRuntime()
            : this(
                () => new NetworkClock(),
                () => new TcpClientTransport(),
                () => new MemoryPackMessageCodec(),
                codec => new RpcMessageDispatcher(codec))
        {
        }

        internal NetworkRuntime(
            Func<INetworkClock> clockFactory,
            Func<IClientTransport> transportFactory,
            Func<IMessageCodec> codecFactory,
            Func<IMessageCodec, RpcMessageDispatcher> rpcFactory)
        {
            this.clockFactory = clockFactory ?? throw new ArgumentNullException(nameof(clockFactory));
            this.transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            this.codecFactory = codecFactory ?? throw new ArgumentNullException(nameof(codecFactory));
            this.rpcFactory = rpcFactory ?? throw new ArgumentNullException(nameof(rpcFactory));
        }

        public bool IsInitialized { get; private set; }
        public NetworkConnectionState ConnectionState { get; private set; } = NetworkConnectionState.Uninitialized;
        public int ConnectionGeneration => connectionGeneration;

        internal long ElapsedMilliseconds => clock?.ElapsedMilliseconds ?? 0;

        internal int PendingRpcCount => rpc?.PendingCount ?? 0;

        internal bool IsTransportConnected => transport?.IsConnected ?? false;

        internal int SentPacketCount => (transport as KcpTransportAdapter)?.SentPacketCount ?? 0;

        internal NetworkRuntime(
            Func<INetworkClock> clockFactory,
            Func<IKcpTransport> transportFactory,
            Func<IMessageCodec> codecFactory,
            Func<IMessageCodec, RpcMessageDispatcher> rpcFactory)
            : this(
                clockFactory,
                () => new KcpTransportAdapter(transportFactory()),
                codecFactory,
                rpcFactory)
        {
        }

        public void Initialize()
        {
            if (IsInitialized)
            {
                return;
            }

            disposed = false;
            try
            {
                clock = clockFactory();
                codec = codecFactory();
                transport = transportFactory();
                rpc = rpcFactory(codec);

                if (clock == null || codec == null || transport == null || rpc == null)
                {
                    throw new InvalidOperationException("网络运行时依赖工厂返回空值。");
                }

                IsInitialized = true;
                ConnectionState = NetworkConnectionState.Disconnected;
            }
            catch
            {
                DisposeSuppressing();
                throw;
            }
        }

        public void Update()
        {
            if (!IsInitialized || disposed)
            {
                return;
            }

            clock.Update();
            transport.Pump(clock.ElapsedMilliseconds);
            int processed = 0;
            while (processed++ < 64 && transport.TryDequeue(out ClientTransportEvent evt))
            {
                ProcessTransportEvent(evt);
            }

            rpc.TickTimeouts(clock.ElapsedMilliseconds);
        }

        public async Task ConnectAsync(NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            if (Boot.ClientReleasePolicy.OfflineSinglePlayer)
                throw new InvalidOperationException("纯离线发布模式不允许建立联机连接。");
            EnsureInitialized();
            int generation = ++connectionGeneration;
            var pending = new PendingConnect(generation, cancellationToken);
            PendingConnect replaced = pendingConnect;
            replaced?.Cancel(new IOException("网络连接已被新的连接请求替换。"));
            pendingConnect = pending;
            ConnectionState = NetworkConnectionState.Connecting;
            NetworkDiagnostics.Log($"连接开始 endpoint={endpoint} timeout={timeoutMilliseconds}");
            try
            {
                await transport.ConnectAsync(generation, endpoint, timeoutMilliseconds, pending.Token);
                await pending.Task;
                NetworkDiagnostics.Log($"连接成功 endpoint={endpoint}");
            }
            catch (TimeoutException)
            {
                NetworkDiagnostics.Log($"连接失败 endpoint={endpoint} error=TimeoutException");
                if (IsCurrentPending(pending))
                {
                    ConnectionState = NetworkConnectionState.Disconnected;
                    pending.Fail(new TimeoutException("网络连接超时。"));
                    pendingConnect = null;
                }
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                NetworkDiagnostics.Log($"连接失败 endpoint={endpoint} error=OperationCanceledException");
                if (IsCurrentPending(pending))
                {
                    ConnectionState = NetworkConnectionState.Disconnected;
                    pending.Cancel(cancellationToken);
                    pendingConnect = null;
                }
                throw;
            }
            catch (Exception exception)
            {
                NetworkDiagnostics.Log($"连接失败 endpoint={endpoint} error={exception.GetType().Name}");
                if (IsCurrentPending(pending))
                {
                    ConnectionState = NetworkConnectionState.Disconnected;
                    pending.Fail(new IOException("网络连接失败。"));
                    pendingConnect = null;
                }
                else
                {
                    await pending.Task;
                }
                throw;
            }
            finally
            {
                pending.Dispose();
            }
        }

        public async Task<TResponse> CallAsync<TResponse>(IRequest request, int timeoutMilliseconds, CancellationToken cancellationToken)
            where TResponse : class, IResponse
        {
            EnsureInitialized();
            if (ConnectionState != NetworkConnectionState.Connected)
            {
                throw new InvalidOperationException("网络未连接。");
            }

            RpcRequest rpcRequest = rpc.SendRequest(request, clock.ElapsedMilliseconds, timeoutMilliseconds, cancellationToken);
            if (rpcRequest.ResponseTask.IsCanceled)
            {
                return await CastResponse<TResponse>(rpcRequest.ResponseTask);
            }

            try
            {
                byte[] packet = codec.Encode(request);
                NetworkDiagnostics.LogSend(request);
                transport.Send(connectionGeneration, packet);
            }
            catch
            {
                rpc.Fail(rpcRequest.RpcId, new IOException("网络发送失败。"));
                Disconnect(NetworkDisconnectReason.TransportError);
                throw;
            }

            return await CastResponse<TResponse>(rpcRequest.ResponseTask);
        }

        public IDisposable RegisterPushHandler<TMessage>(Action<TMessage> handler)
            where TMessage : class, IMessage
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Type type = typeof(TMessage);
            if (!pushHandlers.TryGetValue(type, out var handlers))
            {
                handlers = new List<Delegate>();
                pushHandlers.Add(type, handlers);
            }

            handlers.Add(handler);
            return new DelegateSubscription(() => handlers.Remove(handler));
        }

        public IDisposable RegisterResponseHandler<TResponse>(Action<TResponse> handler)
            where TResponse : class, IResponse
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Type type = typeof(TResponse);
            if (!responseHandlers.TryGetValue(type, out var handlers))
            {
                handlers = new List<Delegate>();
                responseHandlers.Add(type, handlers);
            }

            handlers.Add(handler);
            return new DelegateSubscription(() => handlers.Remove(handler));
        }

        public IDisposable RegisterDisconnectedHandler(Action<NetworkDisconnected> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            disconnectedHandlers.Add(handler);
            return new DelegateSubscription(() => disconnectedHandlers.Remove(handler));
        }

        public void Disconnect(NetworkDisconnectReason reason)
        {
            if (!IsInitialized || disposed)
            {
                return;
            }

            NetworkDiagnostics.Log($"断开连接 reason={reason}");
            transport.Disconnect(reason);
            rpc.FailAll(new IOException($"网络已断开：reason={reason}"));
            ConnectionState = NetworkConnectionState.Disconnected;
            CompleteCurrentPending(new IOException($"网络已断开：reason={reason}"));
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            IsInitialized = false;
            ConnectionState = NetworkConnectionState.Disposed;

            Exception firstException = null;
            DisposeResource(ref firstException, () => rpc?.FailAll(new ObjectDisposedException(nameof(NetworkRuntime))));
            DisposeResource(ref firstException, () => CompleteCurrentPending(new ObjectDisposedException(nameof(NetworkRuntime))));
            DisposeResource(ref firstException, () => rpc?.Dispose());
            DisposeResource(ref firstException, () => transport?.Dispose());
            responseHandlers.Clear();
            pushHandlers.Clear();
            disconnectedHandlers.Clear();
            rpc = null;
            transport = null;
            codec = null;
            clock = null;

            if (firstException != null)
            {
                throw firstException;
            }
        }

        internal RpcRequest SendRequestForTest(IRequest request, long timeoutMilliseconds, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            RpcRequest rpcRequest = rpc.SendRequest(request, clock.ElapsedMilliseconds, timeoutMilliseconds, cancellationToken);
            if (rpcRequest.ResponseTask.IsCanceled)
            {
                return rpcRequest;
            }

            try
            {
                transport.Send(connectionGeneration, codec.Encode(request));
            }
            catch
            {
                rpc.Cancel(rpcRequest.RpcId);
                throw;
            }
            return rpcRequest;
        }

        internal DecodedMessage DecodeForTest(byte[] packet)
        {
            EnsureInitialized();
            return codec.Decode(packet);
        }

        private static void DisposeResource(ref Exception firstException, Action dispose)
        {
            try
            {
                dispose();
            }
            catch (Exception ex)
            {
                firstException ??= ex;
            }
        }

        private void DisposeSuppressing()
        {
            try
            {
                Dispose();
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError($"[NetworkRuntime] 初始化失败后的清理异常：exception={exception}");
            }
        }

        private void EnsureInitialized()
        {
            if (!IsInitialized || disposed)
            {
                throw new InvalidOperationException("网络运行时未初始化。");
            }
        }

        private void ProcessTransportEvent(ClientTransportEvent evt)
        {
            if (evt.Generation != connectionGeneration)
            {
                return;
            }

            switch (evt.Kind)
            {
                case ClientTransportEventKind.Connected:
                    ConnectionState = NetworkConnectionState.Connected;
                    CompletePending(evt.Generation, null);
                    break;
                case ClientTransportEventKind.Frame:
                    ProcessFrame(evt.Frame);
                    break;
                case ClientTransportEventKind.Disconnected:
                    NetworkDiagnostics.Log($"断开连接 reason={evt.Reason}");
                    ConnectionState = NetworkConnectionState.Disconnected;
                    CompletePending(evt.Generation, new IOException($"网络已断开：reason={evt.Reason}"));
                    rpc.FailAll(new IOException($"网络已断开：reason={evt.Reason}"));
                    var disconnected = new NetworkDisconnected(evt.Generation, evt.Reason, evt.Diagnostic);
                    foreach (var handler in disconnectedHandlers.ToArray())
                    {
                        handler(disconnected);
                    }
                    break;
            }
        }

        private bool IsCurrentPending(PendingConnect pending)
        {
            return ReferenceEquals(pendingConnect, pending) && pending.Generation == connectionGeneration;
        }

        private void CompleteCurrentPending(Exception exception)
        {
            PendingConnect pending = pendingConnect;
            if (pending == null)
            {
                return;
            }

            pendingConnect = null;
            if (exception == null)
            {
                pending.Succeed();
            }
            else
            {
                pending.Fail(exception);
            }
        }

        private void CompletePending(int generation, Exception exception)
        {
            PendingConnect pending = pendingConnect;
            if (pending == null || pending.Generation != generation)
            {
                return;
            }

            pendingConnect = null;
            if (exception == null)
            {
                pending.Succeed();
            }
            else
            {
                pending.Fail(exception);
            }
        }

        private void ProcessFrame(byte[] packet)
        {
            try
            {
                DecodedMessage decoded = codec.Decode(packet);
                NetworkDiagnostics.LogReceive(decoded.Message);
                if (decoded.Message is IResponse response)
                {
                    NotifyResponseObservers(response);
                    rpc.TryComplete(response);
                    return;
                }

                Type type = decoded.Message.GetType();
                if (pushHandlers.TryGetValue(type, out var handlers))
                {
                    foreach (var handler in handlers.ToArray())
                    {
                        handler.DynamicInvoke(decoded.Message);
                    }
                }
            }
            catch (Exception e)
            {
                rpc.FailAll(e);
                Disconnect(NetworkDisconnectReason.ProtocolError);
            }
        }

        private void NotifyResponseObservers(IResponse response)
        {
            Type type = response.GetType();
            if (!responseHandlers.TryGetValue(type, out var handlers))
            {
                return;
            }

            foreach (var handler in handlers.ToArray())
            {
                try
                {
                    handler.DynamicInvoke(response);
                }
                catch (Exception exception)
                {
                    Exception actual = exception is System.Reflection.TargetInvocationException target && target.InnerException != null
                        ? target.InnerException
                        : exception;
                    NetworkDiagnostics.Log($"响应观察者异常 type={type.Name} error={actual.GetType().Name}");
                }
            }
        }

        private static async Task<TResponse> CastResponse<TResponse>(Task<IResponse> task)
            where TResponse : class, IResponse
        {
            IResponse response = await task;
            return response as TResponse ?? throw new InvalidCastException(typeof(TResponse).FullName);
        }

        private sealed class DelegateSubscription : IDisposable
        {
            private Action dispose;

            public DelegateSubscription(Action dispose)
            {
                this.dispose = dispose;
            }

            public void Dispose()
            {
                dispose?.Invoke();
                dispose = null;
            }
        }

        private sealed class PendingConnect : IDisposable
        {
            private readonly CancellationTokenSource cancellation;
            private readonly TaskCompletionSource<bool> completion =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly CancellationTokenRegistration registration;

            public PendingConnect(int generation, CancellationToken outerToken)
            {
                Generation = generation;
                cancellation = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
                registration = outerToken.Register(() => completion.TrySetCanceled(outerToken));
            }

            public int Generation { get; }
            public CancellationToken Token => cancellation.Token;
            public Task Task => completion.Task;

            public void Succeed()
            {
                completion.TrySetResult(true);
            }

            public void Fail(Exception exception)
            {
                cancellation.Cancel();
                completion.TrySetException(exception);
            }

            public void Cancel(Exception exception)
            {
                cancellation.Cancel();
                completion.TrySetException(exception);
            }

            public void Cancel(CancellationToken token)
            {
                cancellation.Cancel();
                completion.TrySetCanceled(token);
            }

            public void Dispose()
            {
                registration.Dispose();
                cancellation.Dispose();
            }
        }

        private sealed class KcpTransportAdapter : IClientTransport
        {
            private readonly IKcpTransport inner;
            private int generation;

            public KcpTransportAdapter(IKcpTransport inner)
            {
                this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }

            public bool IsConnected => inner.IsConnected;
            public int ConnectionGeneration => generation;
            public int SentPacketCount => inner.SentPacketCount;

            public Task ConnectAsync(int generation, NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken)
            {
                this.generation = generation;
                return Task.CompletedTask;
            }

            public void Send(int generation, byte[] body)
            {
                inner.Send(body);
            }

            public bool TryDequeue(out ClientTransportEvent transportEvent)
            {
                if (inner.TryReceive(out byte[] packet))
                {
                    transportEvent = ClientTransportEvent.FrameReceived(generation, packet);
                    return true;
                }

                transportEvent = default;
                return false;
            }

            public void Disconnect(NetworkDisconnectReason reason)
            {
                inner.Close();
            }

            public void Pump(long nowMilliseconds)
            {
                inner.Pump(nowMilliseconds);
            }

            public void Dispose()
            {
                inner.Dispose();
            }
        }
    }
}
