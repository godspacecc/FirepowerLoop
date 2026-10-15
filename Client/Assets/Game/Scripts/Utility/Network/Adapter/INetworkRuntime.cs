using System;
using System.Threading;
using System.Threading.Tasks;
using ET;
using QFramework;

namespace Game.Network
{
    public interface INetworkRuntime : IUtility
    {
        bool IsInitialized { get; }
        NetworkConnectionState ConnectionState { get; }
        int ConnectionGeneration { get; }
        void Initialize();
        void Update();
        Task ConnectAsync(NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken);
        Task<TResponse> CallAsync<TResponse>(IRequest request, int timeoutMilliseconds, CancellationToken cancellationToken)
            where TResponse : class, IResponse;
        IDisposable RegisterPushHandler<TMessage>(Action<TMessage> handler)
            where TMessage : class, IMessage;
        IDisposable RegisterDisconnectedHandler(Action<NetworkDisconnected> handler);
        void Disconnect(NetworkDisconnectReason reason);
        void Dispose();
    }

    public enum NetworkConnectionState
    {
        Uninitialized,
        Disconnected,
        Connecting,
        Connected,
        Disposed
    }

    public enum NetworkDisconnectReason
    {
        LocalRequest,
        ConnectFailed,
        ConnectTimeout,
        RemoteClosed,
        TransportError,
        ProtocolError,
        HeartbeatTimeout,
        Disposed
    }

    internal interface INetworkResponseObserver
    {
        IDisposable RegisterResponseHandler<TResponse>(Action<TResponse> handler)
            where TResponse : class, IResponse;
    }

    public readonly struct NetworkDisconnected
    {
        public NetworkDisconnected(int generation, NetworkDisconnectReason reason, string diagnostic)
        {
            Generation = generation;
            Reason = reason;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public int Generation { get; }
        public NetworkDisconnectReason Reason { get; }
        public string Diagnostic { get; }
    }
}
