using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ET;

namespace Game.Network
{
    internal sealed class RpcMessageDispatcher : IDisposable
    {
        private readonly IMessageCodec codec;
        private readonly object gate = new object();
        private readonly Dictionary<int, PendingRpc> pendingRpcs = new Dictionary<int, PendingRpc>();
        private int nextRpcId;
        private int activeCancellationRegistrationCount;
        private bool disposed;

        public RpcMessageDispatcher(IMessageCodec codec)
        {
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
        }

        public int PendingCount
        {
            get
            {
                lock (gate)
                {
                    return pendingRpcs.Count;
                }
            }
        }

        internal Action BeforeCancellationRegistrationForTest { get; set; }

        internal int ActiveCancellationRegistrationCount
        {
            get
            {
                lock (gate)
                {
                    return activeCancellationRegistrationCount;
                }
            }
        }

        public RpcRequest SendRequest(IRequest request, long nowMilliseconds, long timeoutMilliseconds, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (timeoutMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            }
            Type responseType = codec.GetResponseType(request.GetType());
            PendingRpc pending;
            lock (gate)
            {
                ThrowIfDisposed();
                if (cancellationToken.IsCancellationRequested)
                {
                    var canceled = new TaskCompletionSource<IResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                    canceled.TrySetCanceled(cancellationToken);
                    return new RpcRequest(0, canceled.Task);
                }

                int rpcId = ++nextRpcId;
                request.RpcId = rpcId;
                var completion = new TaskCompletionSource<IResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                pending = new PendingRpc(rpcId, responseType, nowMilliseconds + timeoutMilliseconds, completion);
                pendingRpcs.Add(rpcId, pending);
            }
            NetworkDiagnostics.Log($"RPC 等待响应 type={request.GetType().Name} rpcId={pending.RpcId} response={responseType.Name}");

            if (cancellationToken.CanBeCanceled)
            {
                BeforeCancellationRegistrationForTest?.Invoke();
                CancellationTokenRegistration registration = cancellationToken.Register(state =>
                {
                    var callbackState = (CancelCallbackState)state;
                    callbackState.Dispatcher.Cancel(callbackState.RpcId, callbackState.CancellationToken);
                }, new CancelCallbackState(this, pending.RpcId, cancellationToken));

                bool disposeRegistration = false;
                lock (gate)
                {
                    if (pendingRpcs.TryGetValue(pending.RpcId, out PendingRpc active) && ReferenceEquals(active, pending))
                    {
                        pending.SetCancellation(registration, ReleaseCancellationRegistration);
                        activeCancellationRegistrationCount++;
                    }
                    else
                    {
                        disposeRegistration = true;
                    }
                }

                if (disposeRegistration)
                {
                    registration.Dispose();
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    Cancel(pending.RpcId, cancellationToken);
                }
            }

            return new RpcRequest(pending.RpcId, pending.Completion.Task);
        }

        public bool TryComplete(IResponse response)
        {
            PendingRpc pending;
            lock (gate)
            {
                ThrowIfDisposed();
                if (response == null || !pendingRpcs.TryGetValue(response.RpcId, out pending))
                {
                    return false;
                }

                if (!pending.ResponseType.IsInstanceOfType(response))
                {
                    throw new InvalidOperationException($"响应类型不匹配 RpcId={response.RpcId}: {response.GetType().FullName}");
                }

                pendingRpcs.Remove(response.RpcId);
            }

            pending.Dispose();
            NetworkDiagnostics.Log($"RPC 完成 type={response.GetType().Name} rpcId={response.RpcId}");
            pending.Completion.TrySetResult(response);
            return true;
        }

        public void TickTimeouts(long nowMilliseconds)
        {
            List<int> timedOut = null;
            lock (gate)
            {
                ThrowIfDisposed();
                if (pendingRpcs.Count == 0)
                {
                    return;
                }

                foreach (PendingRpc pending in pendingRpcs.Values)
                {
                    if (nowMilliseconds >= pending.TimeoutAtMilliseconds)
                    {
                        timedOut ??= new List<int>();
                        timedOut.Add(pending.RpcId);
                    }
                }
            }

            if (timedOut == null)
            {
                return;
            }

            foreach (int rpcId in timedOut)
            {
                Timeout(rpcId);
            }
        }

        public void CancelAll()
        {
            FailAll(new TaskCanceledException("所有等待中的 RPC 请求已取消。"));
        }

        public void FailAll(Exception exception)
        {
            List<PendingRpc> pendingList;
            lock (gate)
            {
                if (pendingRpcs.Count == 0)
                {
                    return;
                }

                pendingList = new List<PendingRpc>(pendingRpcs.Values);
                pendingRpcs.Clear();
            }

            Exception logException = exception ?? new IOException("网络 RPC 失败。");
            NetworkDiagnostics.Log($"RPC 全部失败 errorType={logException.GetType().Name} message={logException.Message}");
            foreach (PendingRpc pending in pendingList)
            {
                pending.Dispose();
                pending.Completion.TrySetException(logException);
            }
        }

        public void Fail(int rpcId, Exception exception)
        {
            PendingRpc pending;
            lock (gate)
            {
                if (disposed || !pendingRpcs.TryGetValue(rpcId, out pending))
                {
                    return;
                }

                pendingRpcs.Remove(rpcId);
            }

            pending.Dispose();
            pending.Completion.TrySetException(exception ?? new IOException("网络 RPC 失败。"));
        }

        public void Cancel(int rpcId)
        {
            Cancel(rpcId, default);
        }

        public void Cancel(int rpcId, CancellationToken cancellationToken)
        {
            PendingRpc pending;
            lock (gate)
            {
                if (disposed || !pendingRpcs.TryGetValue(rpcId, out pending))
                {
                    return;
                }

                pendingRpcs.Remove(rpcId);
            }

            pending.Dispose();
            if (cancellationToken.CanBeCanceled)
            {
                pending.Completion.TrySetCanceled(cancellationToken);
            }
            else
            {
                pending.Completion.TrySetCanceled();
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
            }

            CancelAll();
        }

        private void Timeout(int rpcId)
        {
            PendingRpc pending;
            lock (gate)
            {
                if (disposed || !pendingRpcs.TryGetValue(rpcId, out pending))
                {
                    return;
                }

                pendingRpcs.Remove(rpcId);
            }

            pending.Dispose();
            NetworkDiagnostics.Log($"RPC 超时 rpcId={rpcId}");
            pending.Completion.TrySetException(new TimeoutException($"RPC 请求超时：rpcId={rpcId}"));
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(RpcMessageDispatcher));
            }
        }

        private sealed class PendingRpc : IDisposable
        {
            public PendingRpc(int rpcId, Type responseType, long timeoutAtMilliseconds, TaskCompletionSource<IResponse> completion)
            {
                RpcId = rpcId;
                ResponseType = responseType;
                TimeoutAtMilliseconds = timeoutAtMilliseconds;
                Completion = completion;
            }

            public int RpcId { get; }
            public Type ResponseType { get; }
            public long TimeoutAtMilliseconds { get; }
            public TaskCompletionSource<IResponse> Completion { get; }
            private CancellationTokenRegistration Cancellation { get; set; }
            private Action releaseCancellationRegistration;

            public void SetCancellation(CancellationTokenRegistration cancellation, Action release)
            {
                Cancellation = cancellation;
                releaseCancellationRegistration = release;
            }

            public void Dispose()
            {
                if (releaseCancellationRegistration != null)
                {
                    releaseCancellationRegistration();
                    releaseCancellationRegistration = null;
                }
                Cancellation.Dispose();
            }
        }

        private void ReleaseCancellationRegistration()
        {
            lock (gate)
            {
                activeCancellationRegistrationCount--;
            }
        }

        private readonly struct CancelCallbackState
        {
            public CancelCallbackState(RpcMessageDispatcher dispatcher, int rpcId, CancellationToken cancellationToken)
            {
                Dispatcher = dispatcher;
                RpcId = rpcId;
                CancellationToken = cancellationToken;
            }

            public RpcMessageDispatcher Dispatcher { get; }
            public int RpcId { get; }
            public CancellationToken CancellationToken { get; }
        }
    }

    internal readonly struct RpcRequest
    {
        public RpcRequest(int rpcId, Task<IResponse> responseTask)
        {
            RpcId = rpcId;
            ResponseTask = responseTask;
        }

        public int RpcId { get; }
        public Task<IResponse> ResponseTask { get; }
    }
}
