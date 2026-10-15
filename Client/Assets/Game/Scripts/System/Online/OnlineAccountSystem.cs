using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ET;
using Game.Network;
using QFramework;

namespace Game
{
    internal interface IOnlineHeartbeatClock
    {
        long ElapsedMilliseconds { get; }
    }

    public sealed class OnlineAccountSystem : AbstractSystem, IOnlineHeartbeatLifecycle
    {
        private const int ConnectTimeoutMilliseconds = 8000;
        private const int RpcTimeoutMilliseconds = 10000;
        private const int HeartbeatIntervalMilliseconds = 2500;
        private const int HeartbeatTimeoutMilliseconds = 10000;
        private const int HeartbeatResumeTimeoutMilliseconds = 3000;
        private INetworkRuntime network;
        private IOnlineAccountPreferenceStore preferences;
        private INetworkResponseObserver responseObserver;
        private IDisposable disconnectedSubscription;
        private IDisposable sessionReplacedSubscription;
        private IDisposable heartbeatResponseSubscription;
        private CancellationTokenSource operationCancellation;
        private CancellationTokenSource heartbeatCancellation;
        private IOnlineHeartbeatClock heartbeatClock = new StopwatchHeartbeatClock();
        private int operationGeneration;
        private int heartbeatGeneration;
        private int heartbeatConnectionGeneration;
        private long heartbeatSessionEpoch;
        private int heartbeatRequestGeneration;
        private int activeHeartbeatRpcId;
        private long lastHeartbeatResponseAtMilliseconds;
        private long nextHeartbeatSendAtMilliseconds;
        private long heartbeatResumeDeadlineMilliseconds;
        private bool heartbeatEnabled;
        private bool heartbeatInFlight;
        private bool heartbeatResumeInProgress;
        private OnlineAccountSnapshot lastSnapshot = OnlineAccountSnapshot.Empty();

        public OnlineAccountSnapshot Snapshot => lastSnapshot;

        protected override void OnInit()
        {
            network = this.GetUtility<INetworkRuntime>();
            preferences = this.GetUtility<IOnlineAccountPreferenceStore>();
            string lastUserName = preferences?.LoadLastUserName() ?? string.Empty;
            lastSnapshot = OnlineAccountSnapshot.Empty(lastUserName);
            Publish(OnlineAccountSnapshot.Empty(lastUserName));
            if (network != null)
            {
                disconnectedSubscription = network.RegisterDisconnectedHandler(OnNetworkDisconnected);
                sessionReplacedSubscription = network.RegisterPushHandler<G2C_SessionReplaced>(OnSessionReplaced);
            }
        }

        protected override void OnDeinit()
        {
            StopHeartbeatForNetworkLifecycle();
            CancelOperation();
            sessionReplacedSubscription?.Dispose();
            disconnectedSubscription?.Dispose();
            sessionReplacedSubscription = null;
            disconnectedSubscription = null;
        }

        public Task<OnlineAccountOperationResult> LoginAsync(string userName, string password)
        {
            return RunExclusive(OnlineOperationState.LoggingIn, userName, password, async (generation, trimmed, rawPassword, token) =>
            {
                var connected = await EnsureAnonymousConnected(generation, token);
                if (connected != OnlineAccountResultCode.Success)
                {
                    return connected;
                }

                return await LoginOnCurrentConnection(generation, trimmed, rawPassword, token);
            });
        }

        public async Task<OnlineAccountOperationResult> LogoutAsync()
        {
            if (Snapshot.OperationState != OnlineOperationState.Idle)
            {
                return new OnlineAccountOperationResult(OnlineAccountResultCode.OperationInProgress, Snapshot);
            }

            int generation = StartOperation();
            CancellationToken token = operationCancellation.Token;
            Publish(WithOperation(Snapshot, OnlineOperationState.LoggingOut, OnlineAccountResultCode.None));
            OnlineAccountResultCode result = OnlineAccountResultCode.Success;
            try
            {
                if (Snapshot.IsAuthenticated && network.ConnectionState == NetworkConnectionState.Connected)
                {
                    StopHeartbeatForNetworkLifecycle();
                    var logout = C2G_Logout.Create();
                    NetworkDiagnostics.Log("联机登出发送");
                    var response = await network.CallAsync<G2C_Logout>(logout, RpcTimeoutMilliseconds, token);
                    if (!IsCurrentOperation(generation))
                    {
                        return new OnlineAccountOperationResult(OnlineAccountResultCode.Disconnected, Snapshot);
                    }
                    result = MapServerError(response.Error);
                }
            }
            catch (OperationCanceledException) when (!IsCurrentOperation(generation))
            {
                return new OnlineAccountOperationResult(OnlineAccountResultCode.Disconnected, Snapshot);
            }
            catch (OperationCanceledException)
            {
                result = OnlineAccountResultCode.RequestTimeout;
            }
            catch (TimeoutException)
            {
                result = OnlineAccountResultCode.RequestTimeout;
            }
            catch
            {
                result = OnlineAccountResultCode.Disconnected;
            }
            finally
            {
                EndOperation(generation);
            }

            if (!IsCurrentOperation(generation))
            {
                return new OnlineAccountOperationResult(OnlineAccountResultCode.Disconnected, Snapshot);
            }

            if (result == OnlineAccountResultCode.Success)
            {
                NetworkDiagnostics.Log("联机登出断开连接");
                network.Disconnect(NetworkDisconnectReason.LocalRequest);
                Publish(Cleared(Snapshot.LastUserName, OnlineAccountResultCode.Success));
            }
            else
            {
                Publish(WithResult(Snapshot, result));
            }

            return new OnlineAccountOperationResult(result, Snapshot);
        }

        public void CloseAnonymousSession()
        {
            if (Snapshot.IsAuthenticated)
            {
                return;
            }

            CancelOperation();
            network?.Disconnect(NetworkDisconnectReason.LocalRequest);
            if (Snapshot.ConnectionState != OnlineConnectionState.Disconnected)
            {
                Publish(Cleared(Snapshot.LastUserName, OnlineAccountResultCode.None));
            }
        }

        private Task<OnlineAccountOperationResult> RunExclusive(
            OnlineOperationState operation,
            string userName,
            string password,
            Func<int, string, string, CancellationToken, Task<OnlineAccountResultCode>> action)
        {
            if (Snapshot.OperationState != OnlineOperationState.Idle)
            {
                return Task.FromResult(new OnlineAccountOperationResult(OnlineAccountResultCode.OperationInProgress, Snapshot));
            }

            string trimmed = (userName ?? string.Empty).Trim();
            string rawPassword = password ?? string.Empty;
            var validation = ValidateInput(trimmed, rawPassword);
            if (validation != OnlineAccountResultCode.Success)
            {
                Publish(WithResult(Snapshot, validation));
                return Task.FromResult(new OnlineAccountOperationResult(validation, Snapshot));
            }

            int generation = StartOperation();
            CancellationToken token = operationCancellation.Token;
            Publish(WithOperation(Snapshot, operation, OnlineAccountResultCode.None));
            return RunExclusiveCore(generation, trimmed, rawPassword, token, action);
        }

        private async Task<OnlineAccountOperationResult> RunExclusiveCore(
            int generation,
            string trimmed,
            string rawPassword,
            CancellationToken token,
            Func<int, string, string, CancellationToken, Task<OnlineAccountResultCode>> action)
        {
            OnlineAccountResultCode result;
            try
            {
                result = await action(generation, trimmed, rawPassword, token);
            }
            catch (OperationCanceledException) when (!IsCurrentOperation(generation))
            {
                return new OnlineAccountOperationResult(OnlineAccountResultCode.Disconnected, Snapshot);
            }
            catch (OperationCanceledException)
            {
                result = OnlineAccountResultCode.RequestTimeout;
            }
            catch (TimeoutException)
            {
                result = OnlineAccountResultCode.RequestTimeout;
            }
            catch
            {
                result = OnlineAccountResultCode.ConnectFailed;
            }
            finally
            {
                EndOperation(generation);
            }

            if (!IsCurrentOperation(generation))
            {
                return new OnlineAccountOperationResult(OnlineAccountResultCode.Disconnected, Snapshot);
            }

            Publish(WithOperation(Snapshot, OnlineOperationState.Idle, result));
            return new OnlineAccountOperationResult(result, Snapshot);
        }

        private async Task<OnlineAccountResultCode> EnsureAnonymousConnected(int generation, CancellationToken token)
        {
            if (network.ConnectionState == NetworkConnectionState.Connected)
            {
                if (Snapshot.ConnectionState != OnlineConnectionState.Authenticated)
                {
                    Publish(new OnlineAccountSnapshot(OnlineConnectionState.AnonymousConnected, Snapshot.OperationState, Snapshot.LastResultCode, string.Empty, 0, 0, 0, false, Snapshot.LastUserName));
                }
                return OnlineAccountResultCode.Success;
            }

            var provider = this.GetUtility<INetworkConfigProvider>();
            var config = provider == null
                ? NetworkConfigResult.Ok(StreamingAssetsNetworkConfigProvider.DefaultEndpoint)
                : await provider.LoadAsync(token);
            if (!IsCurrentOperation(generation))
            {
                return OnlineAccountResultCode.Disconnected;
            }
            if (!config.Success)
            {
                Publish(WithResult(Snapshot, OnlineAccountResultCode.InvalidNetworkConfig));
                return OnlineAccountResultCode.InvalidNetworkConfig;
            }

            Publish(new OnlineAccountSnapshot(OnlineConnectionState.Connecting, Snapshot.OperationState, OnlineAccountResultCode.None, string.Empty, 0, 0, 0, false, Snapshot.LastUserName));
            try
            {
                NetworkDiagnostics.Log("联机连接中");
                await network.ConnectAsync(config.Endpoint, ConnectTimeoutMilliseconds, token);
                if (!IsCurrentOperation(generation))
                {
                    return OnlineAccountResultCode.Disconnected;
                }
                return OnlineAccountResultCode.Success;
            }
            catch (TimeoutException)
            {
                return OnlineAccountResultCode.ConnectTimeout;
            }
            catch
            {
                return OnlineAccountResultCode.ConnectFailed;
            }
        }

        private async Task<OnlineAccountResultCode> LoginOnCurrentConnection(int generation, string userName, string password, CancellationToken token)
        {
            var login = C2G_Login.Create();
            login.UserName = userName;
            login.Password = password;
            var response = await network.CallAsync<G2C_Login>(login, RpcTimeoutMilliseconds, token);
            if (!IsCurrentOperation(generation))
            {
                return OnlineAccountResultCode.Disconnected;
            }

            var code = MapServerError(response.Error);
            if (code != OnlineAccountResultCode.Success)
            {
                return code;
            }

            string last = Snapshot.LastUserName;
            bool preferenceSaved = preferences == null || preferences.SaveLastUserName(userName, out _);
            if (preferenceSaved)
            {
                last = userName;
            }

            OnlineAccountResultCode resultCode;
            if (response.IsNewAccount)
            {
                resultCode = preferenceSaved ? OnlineAccountResultCode.AccountCreated : OnlineAccountResultCode.PreferenceSaveFailed;
            }
            else
            {
                resultCode = preferenceSaved ? OnlineAccountResultCode.Success : OnlineAccountResultCode.PreferenceSaveFailed;
            }

            StopHeartbeatForNetworkLifecycle();
            var observer = network as INetworkResponseObserver;
            if (observer == null)
            {
                NetworkDiagnostics.Log("心跳不可用 observer=missing");
                network.Disconnect(NetworkDisconnectReason.ProtocolError);
                return OnlineAccountResultCode.UnknownFailure;
            }
            responseObserver = observer;

            var authenticated = new OnlineAccountSnapshot(
                OnlineConnectionState.Authenticated,
                OnlineOperationState.Idle,
                resultCode,
                userName,
                response.AccountId,
                response.PlayerId,
                response.SessionEpoch,
                response.IsPlayerActorReused,
                last,
                response.IsNewAccount);
            Publish(authenticated);
            StartHeartbeat(network.ConnectionGeneration, response.SessionEpoch);
            return resultCode;
        }

        private void OnNetworkDisconnected(NetworkDisconnected disconnected)
        {
            StopHeartbeatForNetworkLifecycle();
            CancelOperation();
            if (Snapshot.IsAuthenticated)
            {
                Publish(Cleared(Snapshot.LastUserName, OnlineAccountResultCode.Disconnected));
                this.SendEvent(new OnlineAccountAttentionEvent(OnlineAccountResultCode.Disconnected));
            }
            else if (Snapshot.ConnectionState != OnlineConnectionState.Disconnected)
            {
                Publish(Cleared(Snapshot.LastUserName, OnlineAccountResultCode.Disconnected));
            }
        }

        private void OnSessionReplaced(G2C_SessionReplaced message)
        {
            StopHeartbeatForNetworkLifecycle();
            CancelOperation();
            network.Disconnect(NetworkDisconnectReason.ProtocolError);
            Publish(Cleared(Snapshot.LastUserName, OnlineAccountResultCode.SessionReplaced));
            this.SendEvent(new OnlineAccountAttentionEvent(OnlineAccountResultCode.SessionReplaced));
        }

        private void Publish(OnlineAccountSnapshot snapshot)
        {
            lastSnapshot = snapshot;
            this.GetModel<OnlineAccountModel>().Publish(snapshot);
        }

        private int StartOperation()
        {
            CancelOperation();
            operationCancellation = new CancellationTokenSource();
            return ++operationGeneration;
        }

        private void EndOperation(int generation)
        {
            if (generation == operationGeneration)
            {
                operationCancellation?.Dispose();
                operationCancellation = null;
            }
        }

        private void CancelOperation()
        {
            operationCancellation?.Cancel();
            operationCancellation?.Dispose();
            operationCancellation = null;
            operationGeneration++;
        }

        private bool IsCurrentOperation(int generation)
        {
            return generation == operationGeneration;
        }

        public void UpdateHeartbeat()
        {
            if (!heartbeatEnabled)
            {
                return;
            }

            long now = heartbeatClock.ElapsedMilliseconds;
            if (!Snapshot.IsAuthenticated ||
                network == null ||
                network.ConnectionState != NetworkConnectionState.Connected ||
                network.ConnectionGeneration != heartbeatConnectionGeneration ||
                Snapshot.SessionEpoch != heartbeatSessionEpoch)
            {
                NetworkDiagnostics.Log("心跳停止 reason=session-mismatch");
                StopHeartbeatForNetworkLifecycle();
                return;
            }

            if (heartbeatResumeInProgress)
            {
                if (now >= heartbeatResumeDeadlineMilliseconds)
                {
                    NetworkDiagnostics.Log("心跳超时 mode=resume");
                    network.Disconnect(NetworkDisconnectReason.HeartbeatTimeout);
                    StopHeartbeatForNetworkLifecycle();
                }
                return;
            }

            if (now - lastHeartbeatResponseAtMilliseconds >= HeartbeatTimeoutMilliseconds)
            {
                NetworkDiagnostics.Log("心跳超时 mode=normal");
                network.Disconnect(NetworkDisconnectReason.HeartbeatTimeout);
                StopHeartbeatForNetworkLifecycle();
                return;
            }

            if (!heartbeatInFlight && now >= nextHeartbeatSendAtMilliseconds)
            {
                SendHeartbeat(false, HeartbeatTimeoutMilliseconds - (now - lastHeartbeatResponseAtMilliseconds));
            }
        }

        public void NotifyApplicationResumed()
        {
            if (!heartbeatEnabled ||
                heartbeatResumeInProgress ||
                !Snapshot.IsAuthenticated ||
                network == null ||
                network.ConnectionState != NetworkConnectionState.Connected ||
                network.ConnectionGeneration != heartbeatConnectionGeneration ||
                Snapshot.SessionEpoch != heartbeatSessionEpoch)
            {
                return;
            }

            heartbeatResumeInProgress = true;
            heartbeatResumeDeadlineMilliseconds = heartbeatClock.ElapsedMilliseconds + HeartbeatResumeTimeoutMilliseconds;
            CancelHeartbeatInFlight();
            NetworkDiagnostics.Log("心跳恢复探测");
            SendHeartbeat(true, HeartbeatResumeTimeoutMilliseconds);
        }

        public void StopHeartbeatForNetworkLifecycle()
        {
            heartbeatGeneration++;
            if (!heartbeatEnabled && heartbeatResponseSubscription == null && heartbeatCancellation == null)
            {
                return;
            }

            heartbeatEnabled = false;
            heartbeatConnectionGeneration = 0;
            heartbeatSessionEpoch = 0;
            lastHeartbeatResponseAtMilliseconds = 0;
            nextHeartbeatSendAtMilliseconds = 0;
            heartbeatResumeDeadlineMilliseconds = 0;
            heartbeatResumeInProgress = false;
            CancelHeartbeatInFlight();
            heartbeatResponseSubscription?.Dispose();
            heartbeatResponseSubscription = null;
            responseObserver = null;
            NetworkDiagnostics.Log("心跳停止");
        }

        internal void SetHeartbeatClockForTests(IOnlineHeartbeatClock clock)
        {
            heartbeatClock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        private void StartHeartbeat(int connectionGeneration, long sessionEpoch)
        {
            heartbeatEnabled = true;
            heartbeatConnectionGeneration = connectionGeneration;
            heartbeatSessionEpoch = sessionEpoch;
            long now = heartbeatClock.ElapsedMilliseconds;
            lastHeartbeatResponseAtMilliseconds = now;
            nextHeartbeatSendAtMilliseconds = now + HeartbeatIntervalMilliseconds;
            heartbeatResponseSubscription = responseObserver.RegisterResponseHandler<G2C_Ping>(OnHeartbeatResponse);
            NetworkDiagnostics.Log($"心跳开始 generation={connectionGeneration} sessionEpoch={sessionEpoch}");
        }

        private void OnHeartbeatResponse(G2C_Ping response)
        {
            if (!heartbeatEnabled ||
                network == null ||
                network.ConnectionGeneration != heartbeatConnectionGeneration ||
                Snapshot.SessionEpoch != heartbeatSessionEpoch ||
                !heartbeatInFlight ||
                response.RpcId != activeHeartbeatRpcId)
            {
                NetworkDiagnostics.Log($"心跳忽略 rpcId={response?.RpcId ?? 0}");
                return;
            }

            if (response.Error != 0)
            {
                NetworkDiagnostics.Log($"心跳响应异常 error={response.Error} rpcId={response.RpcId}");
            }

            long now = heartbeatClock.ElapsedMilliseconds;
            lastHeartbeatResponseAtMilliseconds = now;
            nextHeartbeatSendAtMilliseconds = now + HeartbeatIntervalMilliseconds;
            heartbeatResumeInProgress = false;
            ClearHeartbeatInFlight();
            NetworkDiagnostics.Log($"心跳响应 rpcId={response.RpcId}");
        }

        private void SendHeartbeat(bool resume, long timeoutMilliseconds)
        {
            int generation = heartbeatGeneration;
            int connectionGeneration = heartbeatConnectionGeneration;
            long sessionEpoch = heartbeatSessionEpoch;
            int requestGeneration = ++heartbeatRequestGeneration;
            var request = C2G_Ping.Create();
            var cancellation = new CancellationTokenSource();
            heartbeatCancellation = cancellation;
            heartbeatInFlight = true;
            activeHeartbeatRpcId = 0;
            int timeout = (int)Math.Max(1, timeoutMilliseconds);
            NetworkDiagnostics.Log($"心跳发送 mode={(resume ? "resume" : "normal")} timeout={timeout}");
            _ = SendHeartbeatAsync(request, timeout, generation, connectionGeneration, sessionEpoch, requestGeneration, cancellation);
        }

        private async Task SendHeartbeatAsync(
            C2G_Ping request,
            int timeoutMilliseconds,
            int generation,
            int connectionGeneration,
            long sessionEpoch,
            int requestGeneration,
            CancellationTokenSource cancellation)
        {
            try
            {
                Task<G2C_Ping> task = network.CallAsync<G2C_Ping>(request, timeoutMilliseconds, cancellation.Token);
                if (!IsCurrentHeartbeat(generation, connectionGeneration, sessionEpoch, requestGeneration))
                {
                    return;
                }
                activeHeartbeatRpcId = request.RpcId;
                await task;
            }
            catch (OperationCanceledException) when (!IsCurrentHeartbeat(generation, connectionGeneration, sessionEpoch, requestGeneration))
            {
                NetworkDiagnostics.Log("心跳取消 old=true");
            }
            catch (OperationCanceledException)
            {
                NetworkDiagnostics.Log("心跳取消");
                if (IsCurrentHeartbeat(generation, connectionGeneration, sessionEpoch, requestGeneration) &&
                    network.ConnectionState == NetworkConnectionState.Connected)
                {
                    ScheduleHeartbeatAfterCurrentFailure();
                }
            }
            catch (Exception exception)
            {
                if (!IsCurrentHeartbeat(generation, connectionGeneration, sessionEpoch, requestGeneration))
                {
                    NetworkDiagnostics.Log($"心跳忽略完成异常 error={exception.GetType().Name}");
                    return;
                }

                NetworkDiagnostics.Log($"心跳调用异常 error={exception.GetType().Name}");
                if (network.ConnectionState != NetworkConnectionState.Connected)
                {
                    StopHeartbeatForNetworkLifecycle();
                }
                else
                {
                    ScheduleHeartbeatAfterCurrentFailure();
                }
            }
            finally
            {
                if (IsCurrentHeartbeat(generation, connectionGeneration, sessionEpoch, requestGeneration) &&
                    heartbeatInFlight &&
                    activeHeartbeatRpcId == request.RpcId)
                {
                    ClearHeartbeatInFlight();
                }
            }
        }

        private bool IsCurrentHeartbeat(int generation, int connectionGeneration, long sessionEpoch, int requestGeneration)
        {
            return heartbeatEnabled &&
                   heartbeatGeneration == generation &&
                   heartbeatConnectionGeneration == connectionGeneration &&
                   heartbeatSessionEpoch == sessionEpoch &&
                   heartbeatRequestGeneration == requestGeneration;
        }

        private void CancelHeartbeatInFlight()
        {
            heartbeatRequestGeneration++;
            heartbeatInFlight = false;
            activeHeartbeatRpcId = 0;
            heartbeatCancellation?.Cancel();
            heartbeatCancellation?.Dispose();
            heartbeatCancellation = null;
        }

        private void ClearHeartbeatInFlight()
        {
            heartbeatInFlight = false;
            activeHeartbeatRpcId = 0;
            heartbeatCancellation?.Dispose();
            heartbeatCancellation = null;
        }

        private void ScheduleHeartbeatAfterCurrentFailure()
        {
            if (!heartbeatResumeInProgress)
            {
                nextHeartbeatSendAtMilliseconds = lastHeartbeatResponseAtMilliseconds + HeartbeatTimeoutMilliseconds;
            }
        }

        internal static OnlineAccountResultCode ValidateInput(string userName, string password)
        {
            string trimmed = (userName ?? string.Empty).Trim();
            if (!IsAsciiAccountToken(trimmed, 3, 20))
            {
                return OnlineAccountResultCode.InvalidUserName;
            }

            string rawPassword = password ?? string.Empty;
            if (rawPassword.Length > 0 && !IsAsciiAccountToken(rawPassword, 1, 8))
            {
                return OnlineAccountResultCode.InvalidPassword;
            }

            return OnlineAccountResultCode.Success;
        }

        private static bool IsAsciiAccountToken(string value, int minLength, int maxLength)
        {
            if (value == null || value.Length < minLength || value.Length > maxLength)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool valid = c >= 'A' && c <= 'Z' ||
                             c >= 'a' && c <= 'z' ||
                             c >= '0' && c <= '9' ||
                             c == '_';
                if (!valid)
                {
                    return false;
                }
            }

            return true;
        }

        internal static OnlineAccountResultCode MapServerError(int error)
        {
            switch (error)
            {
                case 0: return OnlineAccountResultCode.Success;
                case 200101: return OnlineAccountResultCode.InvalidUserName;
                case 200102: return OnlineAccountResultCode.InvalidPassword;
                case 200103: return OnlineAccountResultCode.UserNameExists;
                case 200104: return OnlineAccountResultCode.PasswordWrong;
                case 200105: return OnlineAccountResultCode.NotLoggedIn;
                case 200106: return OnlineAccountResultCode.SessionReplaced;
                case 200107: return OnlineAccountResultCode.ServiceUnavailable;
                case 200108: return OnlineAccountResultCode.SessionConflict;
                default: return OnlineAccountResultCode.UnknownFailure;
            }
        }

        private static OnlineAccountSnapshot WithOperation(OnlineAccountSnapshot source, OnlineOperationState operation, OnlineAccountResultCode result)
        {
            return new OnlineAccountSnapshot(source.ConnectionState, operation, result, source.UserName, source.AccountId, source.PlayerId, source.SessionEpoch, source.IsPlayerActorReused, source.LastUserName, source.IsNewAccount);
        }

        private static OnlineAccountSnapshot WithResult(OnlineAccountSnapshot source, OnlineAccountResultCode result)
        {
            return new OnlineAccountSnapshot(source.ConnectionState, source.OperationState, result, source.UserName, source.AccountId, source.PlayerId, source.SessionEpoch, source.IsPlayerActorReused, source.LastUserName, source.IsNewAccount);
        }

        private static OnlineAccountSnapshot Cleared(string lastUserName, OnlineAccountResultCode result)
        {
            return new OnlineAccountSnapshot(OnlineConnectionState.Disconnected, OnlineOperationState.Idle, result, string.Empty, 0, 0, 0, false, lastUserName);
        }

        private sealed class StopwatchHeartbeatClock : IOnlineHeartbeatClock
        {
            private readonly double ticksToMilliseconds = 1000.0 / Stopwatch.Frequency;
            private readonly long start = Stopwatch.GetTimestamp();

            public long ElapsedMilliseconds => (long)((Stopwatch.GetTimestamp() - start) * ticksToMilliseconds);
        }
    }
}
