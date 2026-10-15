using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ET;
using Game.Network;
using NUnit.Framework;
using QFramework;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class OnlineHeartbeatTests
    {
        [TearDown]
        public void TearDown()
        {
            LifeCycle.ClearForTests();
            ResetArchitecture();
        }

        [UnityTest]
        public IEnumerator AuthenticatedSessionSendsFirstPingAfter2500Milliseconds()
        {
            var harness = CreateHarness();
            yield return harness.Login();

            harness.Clock.Advance(2499);
            harness.System.UpdateHeartbeat();
            Assert.AreEqual(0, harness.Network.PendingPingCount);

            harness.Clock.Advance(1);
            harness.System.UpdateHeartbeat();
            Assert.AreEqual(1, harness.Network.PendingPingCount);
            Assert.AreEqual(7500, harness.Network.LastCallTimeoutMilliseconds);
        }

        [UnityTest]
        public IEnumerator PingResponseResetsPeriodAndInFlightRequestPreventsOverlap()
        {
            var harness = CreateHarness();
            yield return harness.Login();

            harness.Clock.Advance(2500);
            harness.System.UpdateHeartbeat();
            int firstRpcId = harness.Network.PendingPingRpcIds.Single();

            harness.Clock.Advance(2500);
            harness.System.UpdateHeartbeat();
            Assert.AreEqual(1, harness.Network.PendingPingCount);

            harness.Clock.Advance(10);
            harness.Network.CompletePing(firstRpcId);

            harness.Clock.Advance(2499);
            harness.System.UpdateHeartbeat();
            Assert.AreEqual(0, harness.Network.PendingPingCount);

            harness.Clock.Advance(1);
            harness.System.UpdateHeartbeat();
            Assert.AreEqual(1, harness.Network.PendingPingCount);
        }

        [UnityTest]
        public IEnumerator NormalHeartbeatTimeoutDisconnectsAt10000MillisecondsFromLastResponse()
        {
            var harness = CreateHarness();
            yield return harness.Login();

            harness.Clock.Advance(2500);
            harness.System.UpdateHeartbeat();
            harness.Clock.Advance(7499);
            harness.System.UpdateHeartbeat();
            Assert.AreEqual(0, harness.Network.DisconnectCount);

            harness.Clock.Advance(1);
            harness.System.UpdateHeartbeat();
            Assert.AreEqual(NetworkDisconnectReason.HeartbeatTimeout, harness.Network.LastDisconnectReason);
        }

        [UnityTest]
        public IEnumerator ResumeSendsImmediatePingAndOldRpcDoesNotSatisfyThreeSecondWindow()
        {
            var harness = CreateHarness(withController: true);
            yield return harness.Login();

            harness.Clock.Advance(2500);
            LifeCycle.Update();
            int oldRpcId = harness.Network.PendingPingRpcIds.Single();

            harness.Application.RaiseFocus(false);
            harness.Application.RaiseFocus(true);
            Assert.AreEqual(2, harness.Network.TotalPingCalls);
            int resumeRpcId = harness.Network.PendingPingRpcIds.Single(id => id != oldRpcId);

            harness.Network.CompletePing(oldRpcId);
            harness.Clock.Advance(2999);
            LifeCycle.Update();
            Assert.AreEqual(0, harness.Network.DisconnectCount);

            harness.Clock.Advance(1);
            LifeCycle.Update();
            Assert.AreEqual(NetworkDisconnectReason.HeartbeatTimeout, harness.Network.LastDisconnectReason);
            Assert.Contains(resumeRpcId, harness.Network.CanceledPingRpcIds);
        }

        [UnityTest]
        public IEnumerator LogoutStopsHeartbeatBeforeLogoutRpc()
        {
            var harness = CreateHarness();
            yield return harness.Login();

            Task<OnlineAccountOperationResult> logout = harness.System.LogoutAsync();
            Assert.AreEqual(1, harness.Network.PendingLogoutCount);

            harness.Clock.Advance(2500);
            harness.System.UpdateHeartbeat();
            Assert.AreEqual(0, harness.Network.PendingPingCount);

            harness.Network.CompleteNextLogoutSuccess();
            yield return WaitUntilCompleted(logout);
            Assert.IsFalse(logout.IsFaulted);
            Assert.IsFalse(logout.IsCanceled);
        }

        [UnityTest]
        public IEnumerator MissingResponseObserverDisconnectsAndDoesNotCommitAuthenticatedSnapshot()
        {
            var network = new FakeNetworkWithoutObserver();
            var clock = new ManualHeartbeatClock();
            ResetArchitecture(network);
            var system = TestOnlineArchitecture.Interface.GetSystem<OnlineAccountSystem>();
            system.SetHeartbeatClockForTests(clock);

            Task<OnlineAccountOperationResult> login = system.LoginAsync("review_user", "Pass_123");
            network.CompleteNextLoginSuccess();
            yield return WaitUntilCompleted(login);
            OnlineAccountOperationResult result = login.Result;

            Assert.AreEqual(OnlineAccountResultCode.UnknownFailure, result.Code);
            Assert.AreNotEqual(OnlineConnectionState.Authenticated, system.Snapshot.ConnectionState);
            Assert.AreEqual(NetworkDisconnectReason.ProtocolError, network.LastDisconnectReason);
        }

        [UnityTest]
        public IEnumerator ConnectedNormalPingFailureDoesNotRetryBefore10000Milliseconds()
        {
            var harness = CreateHarness();
            yield return harness.Login();

            harness.Clock.Advance(2500);
            harness.System.UpdateHeartbeat();
            int rpcId = harness.Network.PendingPingRpcIds.Single();
            harness.Network.FailPing(rpcId, new TimeoutException("fake ping timeout"));
            yield return null;

            harness.Clock.Advance(7499);
            harness.System.UpdateHeartbeat();
            Assert.AreEqual(1, harness.Network.TotalPingCalls);
            Assert.AreEqual(OnlineConnectionState.Authenticated, harness.System.Snapshot.ConnectionState);

            harness.Clock.Advance(1);
            harness.System.UpdateHeartbeat();
            Assert.AreEqual(NetworkDisconnectReason.HeartbeatTimeout, harness.Network.LastDisconnectReason);
            Assert.AreEqual(OnlineConnectionState.Disconnected, harness.System.Snapshot.ConnectionState);
            harness.Clock.Advance(2500);
            harness.System.UpdateHeartbeat();
            Assert.AreEqual(1, harness.Network.TotalPingCalls);
        }

        [UnityTest]
        public IEnumerator PauseResumeSendsSingleRecoveryPingAndSuccessResetsPeriod()
        {
            var harness = CreateHarness(withController: true);
            yield return harness.Login();

            harness.Application.RaisePause(PauseState.Paused);
            harness.Clock.Advance(2500);
            LifeCycle.Update();
            Assert.AreEqual(1, harness.Network.TotalPingCalls);
            int normalRpcId = harness.Network.PendingPingRpcIds.Single();

            harness.Application.RaisePause(PauseState.Unpaused);
            harness.Application.RaiseFocus(true);
            Assert.AreEqual(2, harness.Network.TotalPingCalls);
            int resumeRpcId = harness.Network.PendingPingRpcIds.Single(id => id != normalRpcId);

            harness.Network.CompletePing(resumeRpcId);
            harness.Clock.Advance(2499);
            LifeCycle.Update();
            Assert.AreEqual(2, harness.Network.TotalPingCalls);

            harness.Clock.Advance(1);
            LifeCycle.Update();
            Assert.AreEqual(3, harness.Network.TotalPingCalls);
        }

        [UnityTest]
        public IEnumerator ConnectedRecoveryPingFailureDoesNotRetryBefore3000Milliseconds()
        {
            var harness = CreateHarness(withController: true);
            yield return harness.Login();

            harness.Application.RaiseFocus(false);
            harness.Application.RaiseFocus(true);
            int resumeRpcId = harness.Network.PendingPingRpcIds.Single();
            harness.Network.FailPing(resumeRpcId, new TimeoutException("fake resume timeout"));
            yield return null;

            harness.Clock.Advance(2999);
            LifeCycle.Update();
            Assert.AreEqual(1, harness.Network.TotalPingCalls);
            Assert.AreEqual(0, harness.Network.DisconnectCount);

            harness.Clock.Advance(1);
            LifeCycle.Update();
            Assert.AreEqual(NetworkDisconnectReason.HeartbeatTimeout, harness.Network.LastDisconnectReason);
            Assert.AreEqual(OnlineConnectionState.Disconnected, harness.System.Snapshot.ConnectionState);
        }

        [UnityTest]
        public IEnumerator PassiveDisconnectAndSessionReplacedStopHeartbeat()
        {
            var harness = CreateHarness();
            yield return harness.Login();

            harness.Clock.Advance(2500);
            harness.System.UpdateHeartbeat();
            Assert.AreEqual(1, harness.Network.PendingPingCount);

            harness.Network.RaiseDisconnected(NetworkDisconnectReason.RemoteClosed);
            Assert.AreEqual(OnlineConnectionState.Disconnected, harness.System.Snapshot.ConnectionState);
            harness.Clock.Advance(2500);
            harness.System.UpdateHeartbeat();
            Assert.AreEqual(1, harness.Network.TotalPingCalls);
            Assert.AreEqual(0, harness.Network.ResponseHandlerCount<G2C_Ping>());

            var replaced = CreateHarness();
            yield return replaced.Login();
            replaced.Clock.Advance(2500);
            replaced.System.UpdateHeartbeat();
            replaced.Network.RaiseSessionReplaced();
            Assert.AreEqual(OnlineConnectionState.Disconnected, replaced.System.Snapshot.ConnectionState);
            Assert.AreEqual(OnlineAccountResultCode.SessionReplaced, replaced.System.Snapshot.LastResultCode);
            Assert.AreEqual(0, replaced.Network.ResponseHandlerCount<G2C_Ping>());
        }

        [UnityTest]
        public IEnumerator OldGenerationAndDeinitDisposeStopOldCompletions()
        {
            var harness = CreateHarness(withController: true);
            yield return harness.Login();

            harness.Clock.Advance(2500);
            LifeCycle.Update();
            int oldRpcId = harness.Network.PendingPingRpcIds.Single();

            harness.Network.AdvanceGeneration();
            LifeCycle.Update();
            Assert.AreEqual(0, harness.Network.ResponseHandlerCount<G2C_Ping>());
            harness.Network.CompletePing(oldRpcId);
            yield return null;
            harness.Clock.Advance(2500);
            LifeCycle.Update();
            Assert.AreEqual(1, harness.Network.TotalPingCalls);

            var deinit = CreateHarness(withController: true);
            yield return deinit.Login();
            deinit.Clock.Advance(2500);
            LifeCycle.Update();
            int deinitRpcId = deinit.Network.PendingPingRpcIds.Single();
            ResetArchitecture();
            deinit.Network.CompletePing(deinitRpcId);
            yield return null;
            deinit.Clock.Advance(2500);
            LifeCycle.Update();
            Assert.AreEqual(1, deinit.Network.TotalPingCalls);

            var disposed = CreateHarness(withController: true);
            yield return disposed.Login();
            disposed.Controller.Dispose();
            disposed.Application.RaiseFocus(false);
            disposed.Application.RaiseFocus(true);
            disposed.Application.RaisePause(PauseState.Paused);
            disposed.Application.RaisePause(PauseState.Unpaused);
            disposed.Clock.Advance(2500);
            LifeCycle.Update();
            Assert.AreEqual(0, disposed.Network.TotalPingCalls);
        }

        [UnityTest]
        public IEnumerator NewSessionEpochInvalidatesOldPingCompletion()
        {
            var harness = CreateHarness();
            yield return harness.Login(sessionEpoch: 29);

            harness.Clock.Advance(2500);
            harness.System.UpdateHeartbeat();
            int oldRpcId = harness.Network.PendingPingRpcIds.Single();

            Task<OnlineAccountOperationResult> secondLogin = harness.System.LoginAsync("review_user", "Pass_123");
            harness.Network.CompleteNextLoginSuccess(sessionEpoch: 30);
            yield return WaitUntilCompleted(secondLogin);
            Assert.AreEqual(30, harness.System.Snapshot.SessionEpoch);

            harness.Clock.Advance(2500);
            harness.System.UpdateHeartbeat();
            int newRpcId = harness.Network.PendingPingRpcIds.Single(id => id != oldRpcId);

            harness.Clock.Advance(100);
            harness.Network.CompletePing(oldRpcId);
            yield return null;

            Assert.AreEqual(0, harness.Network.DisconnectCount);
            Assert.AreEqual(OnlineConnectionState.Authenticated, harness.System.Snapshot.ConnectionState);
            Assert.AreEqual(30, harness.System.Snapshot.SessionEpoch);
            CollectionAssert.AreEqual(new[] { newRpcId }, harness.Network.PendingPingRpcIds);

            harness.Clock.Advance(2399);
            harness.System.UpdateHeartbeat();
            Assert.AreEqual(2, harness.Network.TotalPingCalls);
        }

        [UnityTest]
        public IEnumerator ControllerDisposeStopsPendingPingAndIgnoresOldCompletion()
        {
            var harness = CreateHarness(withController: true);
            yield return harness.Login();

            harness.Clock.Advance(2500);
            LifeCycle.Update();
            int rpcId = harness.Network.PendingPingRpcIds.Single();

            harness.Controller.Dispose();
            Assert.Contains(rpcId, harness.Network.CanceledPingRpcIds);
            Assert.AreEqual(0, harness.Network.ResponseHandlerCount<G2C_Ping>());

            harness.Network.CompletePing(rpcId);
            yield return null;
            Assert.AreEqual(0, harness.Network.DisconnectCount);
            Assert.AreEqual(1, harness.Network.TotalPingCalls);

            harness.Clock.Advance(2500);
            LifeCycle.Update();
            harness.Application.RaiseFocus(false);
            harness.Application.RaiseFocus(true);
            harness.Application.RaisePause(PauseState.Paused);
            harness.Application.RaisePause(PauseState.Unpaused);
            harness.Clock.Advance(2500);
            LifeCycle.Update();

            Assert.AreEqual(1, harness.Network.TotalPingCalls);
            Assert.AreEqual(0, harness.Network.DisconnectCount);
        }

        private static HeartbeatHarness CreateHarness(bool withController = false)
        {
            var network = new FakeHeartbeatNetwork();
            var clock = new ManualHeartbeatClock();
            var app = new FakeApplicationLifecycleSource();
            ResetArchitecture(network);
            var system = TestOnlineArchitecture.Interface.GetSystem<OnlineAccountSystem>();
            system.SetHeartbeatClockForTests(clock);
            OnlineAccountLifecycleController controller = null;
            if (withController)
            {
                controller = new OnlineAccountLifecycleController(app);
                controller.Initialize(network, system);
            }

            return new HeartbeatHarness(system, network, clock, app, controller);
        }

        private static void ResetArchitecture(INetworkRuntime network = null)
        {
            var field = typeof(Architecture<TestOnlineArchitecture>).GetField("mArchitecture", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (field?.GetValue(null) is IArchitecture architecture)
            {
                architecture.Deinit();
            }
            else
            {
                field?.SetValue(null, null);
            }

            TestOnlineArchitecture.Network = network;
        }

        private sealed class TestOnlineArchitecture : Architecture<TestOnlineArchitecture>
        {
            public static INetworkRuntime Network;

            protected override void Init()
            {
                RegisterModel(new OnlineAccountModel());
                RegisterUtility<INetworkRuntime>(Network);
                RegisterUtility<INetworkConfigProvider>(new FakeConfigProvider());
                RegisterUtility<IOnlineAccountPreferenceStore>(new FakePreferenceStore());
                RegisterSystem(new OnlineAccountSystem());
            }
        }

        private sealed class HeartbeatHarness
        {
            public HeartbeatHarness(
                OnlineAccountSystem system,
                FakeHeartbeatNetwork network,
                ManualHeartbeatClock clock,
                FakeApplicationLifecycleSource application,
                OnlineAccountLifecycleController controller)
            {
                System = system;
                Network = network;
                Clock = clock;
                Application = application;
                Controller = controller;
            }

            public OnlineAccountSystem System { get; }
            public FakeHeartbeatNetwork Network { get; }
            public ManualHeartbeatClock Clock { get; }
            public FakeApplicationLifecycleSource Application { get; }
            public OnlineAccountLifecycleController Controller { get; }

            public IEnumerator Login(long sessionEpoch = 29)
            {
                Task<OnlineAccountOperationResult> login = System.LoginAsync("review_user", "Pass_123");
                Network.CompleteNextLoginSuccess(sessionEpoch);
                yield return WaitUntilCompleted(login);
                OnlineAccountOperationResult result = login.Result;
                Assert.IsTrue(result.Success);
                Assert.AreEqual(OnlineConnectionState.Authenticated, System.Snapshot.ConnectionState);
            }
        }

        private static IEnumerator WaitUntilCompleted(Task task, int maxFrames = 120)
        {
            int frames = 0;
            while (!task.IsCompleted && frames++ < maxFrames)
            {
                yield return null;
            }

            Assert.IsTrue(task.IsCompleted, "Task did not complete within the expected frame budget.");
            if (task.IsFaulted)
            {
                throw task.Exception.GetBaseException();
            }
        }

        private sealed class ManualHeartbeatClock : IOnlineHeartbeatClock
        {
            public long ElapsedMilliseconds { get; private set; }

            public void Advance(long milliseconds)
            {
                ElapsedMilliseconds += milliseconds;
            }
        }

        private sealed class FakeHeartbeatNetwork : INetworkRuntime, INetworkResponseObserver
        {
            private readonly List<PendingCall> pendingCalls = new List<PendingCall>();
            private readonly Dictionary<Type, List<Delegate>> responseHandlers = new Dictionary<Type, List<Delegate>>();
            private readonly List<Action<NetworkDisconnected>> disconnectedHandlers = new List<Action<NetworkDisconnected>>();
            private readonly List<Action<G2C_SessionReplaced>> sessionReplacedHandlers = new List<Action<G2C_SessionReplaced>>();
            private int generation;
            private int nextRpcId;

            public bool IsInitialized { get; private set; } = true;
            public NetworkConnectionState ConnectionState { get; private set; } = NetworkConnectionState.Disconnected;
            public int ConnectionGeneration => generation;
            public int DisconnectCount { get; private set; }
            public NetworkDisconnectReason LastDisconnectReason { get; private set; }
            public int PendingPingCount => pendingCalls.Count(call => call.Request is C2G_Ping);
            public int PendingLogoutCount => pendingCalls.Count(call => call.Request is C2G_Logout);
            public int TotalPingCalls { get; private set; }
            public int LastCallTimeoutMilliseconds { get; private set; }
            public IReadOnlyList<int> PendingPingRpcIds => pendingCalls.Where(call => call.Request is C2G_Ping).Select(call => call.Request.RpcId).ToArray();
            public List<int> CanceledPingRpcIds { get; } = new List<int>();

            public void Initialize()
            {
                IsInitialized = true;
            }

            public void Update()
            {
            }

            public Task ConnectAsync(NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                generation++;
                ConnectionState = NetworkConnectionState.Connected;
                return Task.CompletedTask;
            }

            public Task<TResponse> CallAsync<TResponse>(IRequest request, int timeoutMilliseconds, CancellationToken cancellationToken)
                where TResponse : class, IResponse
            {
                request.RpcId = ++nextRpcId;
                LastCallTimeoutMilliseconds = timeoutMilliseconds;
                if (request is C2G_Ping)
                {
                    TotalPingCalls++;
                }

                var call = new PendingCall(request, cancellationToken, () =>
                {
                    if (request is C2G_Ping)
                    {
                        CanceledPingRpcIds.Add(request.RpcId);
                    }
                });
                pendingCalls.Add(call);
                return call.Task.ContinueWith(task => (TResponse)task.GetAwaiter().GetResult(), CancellationToken.None);
            }

            public IDisposable RegisterResponseHandler<TResponse>(Action<TResponse> handler)
                where TResponse : class, IResponse
            {
                if (!responseHandlers.TryGetValue(typeof(TResponse), out var handlers))
                {
                    handlers = new List<Delegate>();
                    responseHandlers.Add(typeof(TResponse), handlers);
                }

                handlers.Add(handler);
                return new DelegateSubscription(() => handlers.Remove(handler));
            }

            public IDisposable RegisterPushHandler<TMessage>(Action<TMessage> handler)
                where TMessage : class, IMessage
            {
                if (typeof(TMessage) == typeof(G2C_SessionReplaced))
                {
                    Action<G2C_SessionReplaced> typed = message => handler((TMessage)(object)message);
                    sessionReplacedHandlers.Add(typed);
                    return new DelegateSubscription(() => sessionReplacedHandlers.Remove(typed));
                }

                return new DelegateSubscription(() => { });
            }

            public IDisposable RegisterDisconnectedHandler(Action<NetworkDisconnected> handler)
            {
                disconnectedHandlers.Add(handler);
                return new DelegateSubscription(() => disconnectedHandlers.Remove(handler));
            }

            public void Disconnect(NetworkDisconnectReason reason)
            {
                DisconnectCount++;
                LastDisconnectReason = reason;
                ConnectionState = NetworkConnectionState.Disconnected;
                PublishDisconnected(reason);
            }

            public void Dispose()
            {
                IsInitialized = false;
                ConnectionState = NetworkConnectionState.Disposed;
            }

            public void CompleteNextLoginSuccess(long sessionEpoch = 29)
            {
                var call = TakeNext<C2G_Login>();
                var response = G2C_Login.Create();
                response.RpcId = call.Request.RpcId;
                response.Error = 0;
                response.AccountId = 17;
                response.PlayerId = 23;
                response.SessionEpoch = sessionEpoch;
                call.TrySetResult(response);
            }

            public void CompleteNextLogoutSuccess()
            {
                var call = TakeNext<C2G_Logout>();
                var response = G2C_Logout.Create();
                response.RpcId = call.Request.RpcId;
                response.Error = 0;
                NotifyResponse(response);
                call.TrySetResult(response);
            }

            public void CompletePing(int rpcId)
            {
                int index = pendingCalls.FindIndex(call => call.Request is C2G_Ping && call.Request.RpcId == rpcId);
                Assert.GreaterOrEqual(index, 0, $"No pending ping rpcId={rpcId}");
                PendingCall call = pendingCalls[index];
                pendingCalls.RemoveAt(index);
                var response = G2C_Ping.Create();
                response.RpcId = rpcId;
                response.Error = 0;
                NotifyResponse(response);
                call.TrySetResult(response);
            }

            public void FailPing(int rpcId, Exception exception)
            {
                int index = pendingCalls.FindIndex(call => call.Request is C2G_Ping && call.Request.RpcId == rpcId);
                Assert.GreaterOrEqual(index, 0, $"No pending ping rpcId={rpcId}");
                PendingCall call = pendingCalls[index];
                pendingCalls.RemoveAt(index);
                call.TrySetException(exception);
            }

            public void RaiseDisconnected(NetworkDisconnectReason reason)
            {
                LastDisconnectReason = reason;
                ConnectionState = NetworkConnectionState.Disconnected;
                PublishDisconnected(reason);
            }

            public void RaiseSessionReplaced()
            {
                var message = G2C_SessionReplaced.Create();
                message.Error = 200106;
                message.Reason = "test";
                foreach (var handler in sessionReplacedHandlers.ToArray())
                {
                    handler(message);
                }
            }

            public void AdvanceGeneration()
            {
                generation++;
            }

            public int ResponseHandlerCount<TResponse>()
                where TResponse : class, IResponse
            {
                return responseHandlers.TryGetValue(typeof(TResponse), out var handlers) ? handlers.Count : 0;
            }

            private PendingCall TakeNext<TRequest>()
                where TRequest : class, IRequest
            {
                int index = pendingCalls.FindIndex(call => call.Request is TRequest);
                Assert.GreaterOrEqual(index, 0, $"No pending {typeof(TRequest).Name}");
                PendingCall call = pendingCalls[index];
                pendingCalls.RemoveAt(index);
                return call;
            }

            private void NotifyResponse(IResponse response)
            {
                if (!responseHandlers.TryGetValue(response.GetType(), out var handlers))
                {
                    return;
                }

                foreach (var handler in handlers.ToArray())
                {
                    handler.DynamicInvoke(response);
                }
            }

            private void PublishDisconnected(NetworkDisconnectReason reason)
            {
                var disconnected = new NetworkDisconnected(generation, reason, reason.ToString());
                foreach (var handler in disconnectedHandlers.ToArray())
                {
                    handler(disconnected);
                }
            }
        }

        private sealed class FakeNetworkWithoutObserver : INetworkRuntime
        {
            private readonly List<PendingCall> pendingCalls = new List<PendingCall>();
            private int nextRpcId;

            public bool IsInitialized { get; private set; } = true;
            public NetworkConnectionState ConnectionState { get; private set; } = NetworkConnectionState.Disconnected;
            public int ConnectionGeneration { get; private set; }
            public NetworkDisconnectReason LastDisconnectReason { get; private set; }

            public void Initialize()
            {
            }

            public void Update()
            {
            }

            public Task ConnectAsync(NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken)
            {
                ConnectionGeneration++;
                ConnectionState = NetworkConnectionState.Connected;
                return Task.CompletedTask;
            }

            public Task<TResponse> CallAsync<TResponse>(IRequest request, int timeoutMilliseconds, CancellationToken cancellationToken)
                where TResponse : class, IResponse
            {
                request.RpcId = ++nextRpcId;
                var call = new PendingCall(request, cancellationToken, null);
                pendingCalls.Add(call);
                return call.Task.ContinueWith(task => (TResponse)task.GetAwaiter().GetResult(), CancellationToken.None);
            }

            public IDisposable RegisterPushHandler<TMessage>(Action<TMessage> handler)
                where TMessage : class, IMessage
            {
                return new DelegateSubscription(() => { });
            }

            public IDisposable RegisterDisconnectedHandler(Action<NetworkDisconnected> handler)
            {
                return new DelegateSubscription(() => { });
            }

            public void Disconnect(NetworkDisconnectReason reason)
            {
                LastDisconnectReason = reason;
                ConnectionState = NetworkConnectionState.Disconnected;
            }

            public void Dispose()
            {
            }

            public void CompleteNextLoginSuccess()
            {
                int index = pendingCalls.FindIndex(call => call.Request is C2G_Login);
                Assert.GreaterOrEqual(index, 0);
                PendingCall call = pendingCalls[index];
                pendingCalls.RemoveAt(index);
                var response = G2C_Login.Create();
                response.RpcId = call.Request.RpcId;
                response.Error = 0;
                response.AccountId = 17;
                response.PlayerId = 23;
                response.SessionEpoch = 29;
                call.TrySetResult(response);
            }
        }

        private sealed class PendingCall
        {
            private readonly TaskCompletionSource<IResponse> completion =
                new TaskCompletionSource<IResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly CancellationTokenRegistration registration;
            private readonly Action onCanceled;

            public PendingCall(IRequest request, CancellationToken cancellationToken, Action onCanceled)
            {
                Request = request;
                this.onCanceled = onCanceled;
                if (cancellationToken.CanBeCanceled)
                {
                    registration = cancellationToken.Register(() =>
                    {
                        this.onCanceled?.Invoke();
                        completion.TrySetCanceled(cancellationToken);
                    });
                }
            }

            public IRequest Request { get; }
            public Task<IResponse> Task => completion.Task;

            public void TrySetResult(IResponse response)
            {
                registration.Dispose();
                completion.TrySetResult(response);
            }

            public void TrySetException(Exception exception)
            {
                registration.Dispose();
                completion.TrySetException(exception);
            }
        }

        private sealed class FakeConfigProvider : INetworkConfigProvider
        {
            public Task<NetworkConfigResult> LoadAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(NetworkConfigResult.Ok(new NetworkEndpoint("127.0.0.1", 31001)));
            }
        }

        private sealed class FakePreferenceStore : IOnlineAccountPreferenceStore
        {
            public string LoadLastUserName()
            {
                return string.Empty;
            }

            public bool SaveLastUserName(string userName, out string error)
            {
                error = null;
                return true;
            }
        }

        private sealed class FakeApplicationLifecycleSource : IOnlineApplicationLifecycleSource
        {
            public event Action<bool> FocusChanged;
            public event Action<PauseState> PauseStateChanged;

            public void RaiseFocus(bool focused)
            {
                FocusChanged?.Invoke(focused);
            }

            public void RaisePause(PauseState state)
            {
                PauseStateChanged?.Invoke(state);
            }

            public void Dispose()
            {
            }
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
    }
}
