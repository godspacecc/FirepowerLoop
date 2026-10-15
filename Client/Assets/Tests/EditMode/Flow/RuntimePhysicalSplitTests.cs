using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ET;
using Game.Network;
using MemoryPack;
using NUnit.Framework;
using QFramework;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class RuntimePhysicalSplitTests
    {
        [TearDown]
        public void TearDown()
        {
            LifeCycle.ClearForTests();
        }

        [Test]
        public void GeneratedMessagesKeepMemoryPackAndStableOpcodes()
        {
            string generatedDir = Path.Combine(ApplicationRoot(), "Assets/Game/Scripts/Network/Generated");
            string[] files = Directory.GetFiles(generatedDir, "*.cs").Select(Path.GetFileName).OrderBy(name => name).ToArray();

            CollectionAssert.AreEqual(
                new[]
                {
                    "ClientMessage_C_1000.cs",
                    "LockStepOuter_C_11001.cs",
                    "OnlineOuter_C_12001.cs",
                    "OuterMessage_C_10001.cs"
                },
                files);

            Assert.AreEqual(12002, OnlineOuter.C2G_Register);
            Assert.AreEqual(12003, OnlineOuter.G2C_Register);
            Assert.AreEqual(12004, OnlineOuter.C2G_Login);
            Assert.AreEqual(12005, OnlineOuter.G2C_Login);
            Assert.AreEqual(12008, OnlineOuter.G2C_SessionReplaced);
            AssertMessageContract<C2G_Login, ISessionRequest>(OnlineOuter.C2G_Login, "G2C_Login", "RpcId", 0);
            AssertMessageContract<G2C_Login, ISessionResponse>(OnlineOuter.G2C_Login, null, "SessionEpoch", 5);
        }

        [Test]
        public void MemoryPackRoundTripKeepsLoginFields()
        {
            C2G_Login login = C2G_Login.Create();
            G2C_Login response = G2C_Login.Create();
            try
            {
                login.RpcId = 17;
                login.UserName = "user";
                login.Password = "pass";

                response.RpcId = 17;
                response.Error = 3;
                response.Message = "ok";
                response.AccountId = 101;
                response.PlayerId = 202;
                response.SessionEpoch = 303;
                response.IsPlayerActorReused = true;

                C2G_Login loginCopy = MemoryPackSerializer.Deserialize<C2G_Login>(MemoryPackSerializer.Serialize(login));
                G2C_Login responseCopy = MemoryPackSerializer.Deserialize<G2C_Login>(MemoryPackSerializer.Serialize(response));

                Assert.AreEqual(login.RpcId, loginCopy.RpcId);
                Assert.AreEqual(login.UserName, loginCopy.UserName);
                Assert.AreEqual(login.Password, loginCopy.Password);
                Assert.AreEqual(response.RpcId, responseCopy.RpcId);
                Assert.AreEqual(response.Error, responseCopy.Error);
                Assert.AreEqual(response.Message, responseCopy.Message);
                Assert.AreEqual(response.AccountId, responseCopy.AccountId);
                Assert.AreEqual(response.PlayerId, responseCopy.PlayerId);
                Assert.AreEqual(response.SessionEpoch, responseCopy.SessionEpoch);
                Assert.AreEqual(response.IsPlayerActorReused, responseCopy.IsPlayerActorReused);
            }
            finally
            {
                login.Dispose();
                response.Dispose();
            }
        }

        [Test]
        public void OnlineLogin_IsNewAccount_RoundTrips()
        {
            G2C_Login response = G2C_Login.Create();
            try
            {
                Assert.IsFalse(response.IsNewAccount);

                response.IsNewAccount = true;
                response.AccountId = 101;
                response.PlayerId = 202;
                G2C_Login copy = MemoryPackSerializer.Deserialize<G2C_Login>(MemoryPackSerializer.Serialize(response));
                Assert.IsTrue(copy.IsNewAccount);
                Assert.AreEqual(101, copy.AccountId);
                Assert.AreEqual(202, copy.PlayerId);

                AssertMessageContract<G2C_Login, ISessionResponse>(OnlineOuter.G2C_Login, null, "IsNewAccount", 7);
            }
            finally
            {
                response.Dispose();
            }
        }
        [Test]
        public void NetworkRuntimeLifecycleIsIdempotentAndDoesNotConnect()
        {
            var runtime = new NetworkRuntime();
            var heartbeat = new RecordingHeartbeatLifecycle();
            var appLifecycle = new RecordingApplicationLifecycleSource();
            var lifecycle = new OnlineAccountLifecycleController(appLifecycle);

            try
            {
                Assert.IsFalse(typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(typeof(OnlineAccountLifecycleController)));
                Assert.IsFalse(runtime.IsInitialized);
                runtime.Update();
                runtime.Initialize();
                runtime.Initialize();
                Assert.IsTrue(runtime.IsInitialized);
                Assert.IsFalse(runtime.IsTransportConnected);

                lifecycle.Initialize(runtime, heartbeat);
                Assert.AreEqual(1, appLifecycle.FocusSubscriberCount);
                Assert.AreEqual(1, appLifecycle.PauseSubscriberCount);

                lifecycle.Initialize(runtime, heartbeat);
                Assert.AreEqual(1, heartbeat.StopCount);
                Assert.AreEqual(1, appLifecycle.FocusSubscriberCount);
                Assert.AreEqual(1, appLifecycle.PauseSubscriberCount);

                LifeCycle.Update();
                Assert.GreaterOrEqual(runtime.ElapsedMilliseconds, 0);
                Assert.AreEqual(1, heartbeat.UpdateCount);

                appLifecycle.RaiseFocus(false);
                appLifecycle.RaiseFocus(true);
                Assert.AreEqual(1, heartbeat.ResumeCount);
                appLifecycle.RaisePause(PauseState.Paused);
                appLifecycle.RaisePause(PauseState.Unpaused);
                Assert.AreEqual(2, heartbeat.ResumeCount);

                lifecycle.Dispose();
                Assert.AreEqual(2, heartbeat.StopCount);
                Assert.AreEqual(0, appLifecycle.FocusSubscriberCount);
                Assert.AreEqual(0, appLifecycle.PauseSubscriberCount);
                runtime.Dispose();
                runtime.Dispose();
                LifeCycle.Update();
                Assert.IsFalse(runtime.IsInitialized);
                Assert.AreEqual(0, runtime.PendingRpcCount);
                Assert.IsFalse(runtime.IsTransportConnected);
                Assert.AreEqual(1, heartbeat.UpdateCount);

                var productionRuntime = new NetworkRuntime();
                var productionHeartbeat = new RecordingHeartbeatLifecycle();
                var productionLifecycle = new OnlineAccountLifecycleController();
                productionRuntime.Initialize();
                productionLifecycle.Initialize(productionRuntime, productionHeartbeat);
                LifeCycle.ApplicationPause(true);
                LifeCycle.ApplicationPause(false);
                Assert.AreEqual(1, productionHeartbeat.ResumeCount);
                productionLifecycle.Dispose();
                LifeCycle.ApplicationPause(true);
                LifeCycle.ApplicationPause(false);
                Assert.AreEqual(1, productionHeartbeat.ResumeCount);
                productionRuntime.Dispose();
            }
            finally
            {
                lifecycle.Dispose();
                runtime.Dispose();
                LifeCycle.ClearForTests();
            }
        }

        [Test]
        public void NetworkRuntimeInitializationFailureRollsBack()
        {
            var transport = new ThrowingDisposeTransport();
            InvalidOperationException expected = new InvalidOperationException("init failed");
            var runtime = new NetworkRuntime(
                () => new ManualClock(),
                () => transport,
                () => new MemoryPackMessageCodec(),
                codec => throw expected);

            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex(@"\[NetworkRuntime\] 初始化失败后的清理异常：exception=System\.InvalidOperationException: dispose failed"));
            InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() => runtime.Initialize());
            Assert.AreSame(expected, actual);
            Assert.IsFalse(runtime.IsInitialized);
            Assert.IsTrue(transport.DisposeCalled);
            runtime.Update();
        }

        [Test]
        public void ProductionRuntimeCreatesNetworkDependenciesWithoutConnecting()
        {
            var runtime = new NetworkRuntime();

            runtime.Initialize();
            try
            {
                Assert.IsTrue(runtime.IsInitialized);
                Assert.IsFalse(runtime.IsTransportConnected);
                Assert.AreEqual(0, runtime.PendingRpcCount);
                Assert.AreEqual(0, runtime.SentPacketCount);
                string runtimeSource = File.ReadAllText(Path.Combine(ApplicationRoot(), "Assets/Game/Scripts/Utility/Network/Adapter/NetworkRuntime.cs"));
                StringAssert.Contains("new TcpClientTransport()", runtimeSource);
                StringAssert.DoesNotContain("new KcpClientTransport()", runtimeSource);
                var transport = new KcpClientTransport();
                try
                {
                    Assert.IsTrue(transport.HasUdpSocket);
                    Assert.IsFalse(transport.HasKcp);
                    Assert.IsFalse(transport.IsConnected);
                    Assert.AreEqual(KcpClientConnectionState.NotConnected, transport.State);
                }
                finally
                {
                    transport.Dispose();
                }
            }
            finally
            {
                runtime.Dispose();
            }
        }

        [Test]
        public void EtKcpHandshakeUsesServerCompatibleSynAndAck()
        {
            var socket = new RecordingDatagramSocket();
            var transport = new KcpClientTransport(socket, 0x11223344);
            var remote = new IPEndPoint(IPAddress.Loopback, 30001);

            transport.Connect(remote, 0);

            Assert.IsFalse(transport.IsConnected);
            Assert.AreEqual(KcpClientConnectionState.Connecting, transport.State);
            byte[] syn = socket.SentPackets.Single();
            Assert.AreEqual(9, syn.Length);
            Assert.AreEqual(KcpClientTransport.SYN, syn[0]);
            Assert.AreEqual(0x11223344u, ReadUInt32(syn, 1));
            Assert.AreEqual(0u, ReadUInt32(syn, 5));

            socket.EnqueueReceived(CreateAck(0x55667788, 0x88776655));
            transport.Pump(1);
            Assert.IsFalse(transport.IsConnected);
            Assert.IsFalse(transport.HasKcp);

            socket.EnqueueReceived(CreateAck(0x55667788, 0x11223344));
            transport.Pump(2);

            Assert.IsTrue(transport.IsConnected);
            Assert.AreEqual(KcpClientConnectionState.Connected, transport.State);
            Assert.AreEqual(0x55667788u, transport.RemoteConn);
            Assert.AreEqual(0x55667788u, transport.KcpConversationId);
        }

        [Test]
        public void EtKcpMessageHeaderUsesLocalConnAndKcpConvOffset()
        {
            var socket = new RecordingDatagramSocket();
            var transport = new KcpClientTransport(socket, 0x01020304);
            var remote = new IPEndPoint(IPAddress.Loopback, 30001);

            transport.Connect(remote, 0);
            socket.EnqueueReceived(CreateAck(0xA1A2A3A4, 0x01020304));
            transport.Pump(1);

            transport.Send(new byte[] { 7, 8, 9 });
            transport.Pump(20);

            byte[] msg = socket.SentPackets.Last();
            Assert.GreaterOrEqual(msg.Length, 9);
            Assert.AreEqual(KcpClientTransport.MSG, msg[0]);
            Assert.AreEqual(0x01020304u, ReadUInt32(msg, 1));
            Assert.AreEqual(0xA1A2A3A4u, ReadUInt32(msg, 5));
        }

        [Test]
        public void EtKcpRejectsMismatchedMessageConnBeforeKcpInput()
        {
            var socket = new RecordingDatagramSocket();
            var transport = new KcpClientTransport(socket, 0x11111111);
            var remote = new IPEndPoint(IPAddress.Loopback, 30001);

            transport.Connect(remote, 0);
            socket.EnqueueReceived(CreateAck(0x22222222, 0x11111111));
            transport.Pump(1);

            socket.EnqueueReceived(CreateMsg(0x33333333, 0x11111111));
            socket.EnqueueReceived(CreateMsg(0x22222222, 0x44444444));
            transport.Pump(2);

            Assert.AreEqual(0, transport.AcceptedKcpInputCount);
        }

        [Test]
        public void EtKcpDisposeReleasesSocketKcpAndConnectionState()
        {
            var socket = new RecordingDatagramSocket();
            var transport = new KcpClientTransport(socket, 0x01010101);
            var remote = new IPEndPoint(IPAddress.Loopback, 30001);

            transport.Connect(remote, 0);
            socket.EnqueueReceived(CreateAck(0x02020202, 0x01010101));
            transport.Pump(1);
            Assert.IsTrue(transport.HasKcp);

            transport.Dispose();

            Assert.IsTrue(socket.IsDisposed);
            Assert.IsFalse(transport.IsConnected);
            Assert.IsFalse(transport.HasUdpSocket);
            Assert.IsFalse(transport.HasKcp);
            Assert.AreEqual(KcpClientConnectionState.Closed, transport.State);
            Assert.AreEqual(3, socket.SentPackets.Count(packet => packet[0] == KcpClientTransport.FIN));
        }

        [Test]
        public void MessagePacketMatchesServerOuterFormat()
        {
            var codec = new MemoryPackMessageCodec();
            C2G_Login request = C2G_Login.Create();
            try
            {
                request.RpcId = 42;
                request.UserName = "format-user";
                request.Password = "format-pass";

                byte[] packet = codec.Encode(request);
                byte[] body = MemoryPackSerializer.Serialize(request);
                C2G_Login decodedBody = MemoryPackSerializer.Deserialize<C2G_Login>(packet.AsSpan(2));

                Assert.AreEqual(OnlineOuter.C2G_Login, packet[0] | (packet[1] << 8));
                Assert.AreEqual(2 + body.Length, packet.Length);
                Assert.AreEqual(request.RpcId, decodedBody.RpcId);
                Assert.AreEqual(request.UserName, decodedBody.UserName);
                Assert.AreEqual(request.Password, decodedBody.Password);
            }
            finally
            {
                request.Dispose();
            }
        }

        [Test]
        public void RequestPacketResponseCorrelationCompletesPendingRpc()
        {
            var transport = new RecordingTransport();
            var runtime = CreateTestRuntime(transport);
            runtime.Initialize();
            try
            {
                C2G_Login request = C2G_Login.Create();
                request.UserName = "user";
                request.Password = "pass";
                RpcRequest rpcRequest = runtime.SendRequestForTest(request, 1000);

                Assert.AreEqual(1, runtime.PendingRpcCount);
                Assert.AreEqual(1, runtime.SentPacketCount);
                DecodedMessage outbound = runtime.DecodeForTest(transport.DequeueSentPacket());
                Assert.AreEqual(OnlineOuter.C2G_Login, outbound.Opcode);
                Assert.AreEqual(rpcRequest.RpcId, outbound.RpcId);
                Assert.AreEqual(rpcRequest.RpcId, ((C2G_Login)outbound.Message).RpcId);

                G2C_Login response = G2C_Login.Create();
                response.RpcId = rpcRequest.RpcId;
                response.AccountId = 101;
                response.PlayerId = 202;
                response.SessionEpoch = 303;
                transport.EnqueueReceivedPacket(new MemoryPackMessageCodec().Encode(response));
                runtime.Update();

                Assert.IsTrue(rpcRequest.ResponseTask.IsCompleted);
                var result = (G2C_Login)rpcRequest.ResponseTask.Result;
                Assert.AreEqual(response.RpcId, result.RpcId);
                Assert.AreEqual(response.AccountId, result.AccountId);
                Assert.AreEqual(response.PlayerId, result.PlayerId);
                Assert.AreEqual(response.SessionEpoch, result.SessionEpoch);
                Assert.AreEqual(0, runtime.PendingRpcCount);
            }
            finally
            {
                runtime.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator NetworkRuntimeObservesPingResponseBeforeAsyncContinuationAndHeartbeatDeadlineCheck()
        {
            yield return RuntimeDeadlineResponseScenario(resume: false);
            yield return RuntimeDeadlineResponseScenario(resume: true);
        }

        private static IEnumerator RuntimeDeadlineResponseScenario(bool resume)
        {
            ResetRuntimeHeartbeatArchitecture();
            LifeCycle.ClearForTests();
            var runtimeClock = new ManualClock();
            var heartbeatClock = new ManualHeartbeatClock();
            var transport = new ScriptedClientTransport();
            var runtime = new NetworkRuntime(
                () => runtimeClock,
                () => transport,
                () => new MemoryPackMessageCodec(),
                codec => new RpcMessageDispatcher(codec));
            runtime.Initialize();
            OnlineAccountLifecycleController controller = null;
            RecordingApplicationLifecycleSource appSource = null;
            try
            {
                RuntimeHeartbeatArchitecture.Network = runtime;
                var account = RuntimeHeartbeatArchitecture.Interface.GetSystem<OnlineAccountSystem>();
                account.SetHeartbeatClockForTests(heartbeatClock);
                appSource = new RecordingApplicationLifecycleSource();
                controller = new OnlineAccountLifecycleController(appSource);
                controller.Initialize(runtime, account);

                Task<OnlineAccountOperationResult> login = account.LoginAsync("review_user", "Pass_123");
                runtimeClock.Advance(1);
                LifeCycle.Update();
                yield return WaitUntil(() => transport.SentPackets.Count == 1, "login request sent");
                DecodedMessage loginRequest = runtime.DecodeForTest(transport.SentPackets[0]);
                var loginResponse = G2C_Login.Create();
                loginResponse.RpcId = loginRequest.RpcId;
                loginResponse.Error = 0;
                loginResponse.AccountId = 17;
                loginResponse.PlayerId = 23;
                loginResponse.SessionEpoch = 29;
                transport.EnqueueReceivedPacket(runtime.ConnectionGeneration, new MemoryPackMessageCodec().Encode(loginResponse));
                runtimeClock.Advance(1);
                LifeCycle.Update();
                yield return WaitUntilCompleted(login);
                Assert.AreEqual(OnlineConnectionState.Authenticated, account.Snapshot.ConnectionState);

                int observed = 0;
                bool observedBeforeRpcCompletion = false;
                int observedRpcId = 0;
                ((INetworkResponseObserver)runtime).RegisterResponseHandler<G2C_Ping>(_ =>
                {
                    observed++;
                    throw new InvalidOperationException("observer boom");
                });
                ((INetworkResponseObserver)runtime).RegisterResponseHandler<G2C_Ping>(response =>
                {
                    observed++;
                    observedRpcId = response.RpcId;
                    observedBeforeRpcCompletion = runtime.PendingRpcCount == 1;
                });

                if (resume)
                {
                    appSource.RaisePause(PauseState.Paused);
                    appSource.RaisePause(PauseState.Unpaused);
                }
                else
                {
                    heartbeatClock.Advance(2500);
                }

                runtimeClock.Advance(1);
                LifeCycle.Update();
                Assert.AreEqual(2, transport.SentPackets.Count);
                DecodedMessage pingRequest = runtime.DecodeForTest(transport.SentPackets[1]);
                Assert.AreEqual(OuterMessage.C2G_Ping, pingRequest.Opcode);

                heartbeatClock.Advance(resume ? 3000 : 7500);
                var pingResponse = G2C_Ping.Create();
                pingResponse.RpcId = pingRequest.RpcId;
                pingResponse.Error = 0;
                transport.EnqueueReceivedPacket(runtime.ConnectionGeneration, new MemoryPackMessageCodec().Encode(pingResponse));
                runtimeClock.Advance(resume ? 3000 : 7500);
                LifeCycle.Update();

                Assert.AreEqual(2, observed);
                Assert.AreEqual(pingRequest.RpcId, observedRpcId);
                Assert.IsTrue(observedBeforeRpcCompletion);
                Assert.AreEqual(NetworkConnectionState.Connected, runtime.ConnectionState);
                Assert.AreEqual(OnlineConnectionState.Authenticated, account.Snapshot.ConnectionState);
                Assert.AreEqual(0, runtime.PendingRpcCount);

                heartbeatClock.Advance(2499);
                runtimeClock.Advance(2499);
                LifeCycle.Update();
                Assert.AreEqual(2, transport.SentPackets.Count);

                heartbeatClock.Advance(1);
                runtimeClock.Advance(1);
                LifeCycle.Update();
                Assert.AreEqual(3, transport.SentPackets.Count);
            }
            finally
            {
                controller?.Dispose();
                ResetRuntimeHeartbeatArchitecture();
                runtime.Dispose();
                LifeCycle.ClearForTests();
            }
        }

        [Test]
        public void FrameUpdateDrivesClockAndRpcTimeout()
        {
            var clock = new ManualClock();
            var transport = new RecordingTransport();
            var runtime = new NetworkRuntime(
                () => clock,
                () => transport,
                () => new MemoryPackMessageCodec(),
                codec => new RpcMessageDispatcher(codec));

            runtime.Initialize();
            try
            {
                RpcRequest rpcRequest = runtime.SendRequestForTest(C2G_Login.Create(), 50);
                clock.Advance(49);
                runtime.Update();
                Assert.IsFalse(rpcRequest.ResponseTask.IsCompleted);
                Assert.AreEqual(49, runtime.ElapsedMilliseconds);

                clock.Advance(1);
                runtime.Update();
                Assert.IsTrue(rpcRequest.ResponseTask.IsFaulted);
                Assert.IsInstanceOf<TimeoutException>(rpcRequest.ResponseTask.Exception?.GetBaseException());
                Assert.AreEqual(0, runtime.PendingRpcCount);
            }
            finally
            {
                runtime.Dispose();
            }
        }

        [Test]
        public void RpcCancellationAndDisposeLeaveNoPendingWaits()
        {
            var transport = new RecordingTransport();
            var runtime = CreateTestRuntime(transport);
            CancellationTokenSource cancellation = null;
            runtime.Initialize();
            try
            {
                cancellation = new CancellationTokenSource();
                RpcRequest canceled = runtime.SendRequestForTest(C2G_Login.Create(), 1000, cancellation.Token);
                cancellation.Cancel();

                Assert.IsTrue(canceled.ResponseTask.IsCanceled);
                Assert.AreEqual(0, runtime.PendingRpcCount);

                RpcRequest disposed = runtime.SendRequestForTest(C2G_Login.Create(), 1000);
                runtime.Dispose();
                Assert.IsTrue(disposed.ResponseTask.IsFaulted);
                Assert.IsInstanceOf<ObjectDisposedException>(disposed.ResponseTask.Exception?.GetBaseException());
                Assert.AreEqual(0, runtime.PendingRpcCount);
                Assert.DoesNotThrow(() => runtime.Dispose());
            }
            finally
            {
                cancellation?.Dispose();
                runtime.Dispose();
            }
        }

        [Test]
        public void PreCanceledRequestDoesNotCreatePendingRpc()
        {
            var runtime = CreateTestRuntime(new RecordingTransport());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            runtime.Initialize();
            try
            {
                RpcRequest canceled = runtime.SendRequestForTest(C2G_Login.Create(), 1000, cancellation.Token);

                Assert.AreEqual(0, runtime.PendingRpcCount);
                Assert.AreEqual(0, runtime.SentPacketCount);
                Assert.IsTrue(canceled.ResponseTask.IsCanceled);
            }
            finally
            {
                runtime.Dispose();
            }
        }

        [Test]
        public void EncodingAndSendFailuresLeaveNoPendingRpcAndPreserveException()
        {
            var sendFailure = new InvalidOperationException("send failed");
            var throwingTransport = new RecordingTransport { SendException = sendFailure };
            var sendRuntime = CreateTestRuntime(throwingTransport);
            sendRuntime.Initialize();
            try
            {
                InvalidOperationException actual = Assert.Throws<InvalidOperationException>(
                    () => sendRuntime.SendRequestForTest(C2G_Login.Create(), 1000));
                Assert.AreSame(sendFailure, actual);
                Assert.AreEqual(0, sendRuntime.PendingRpcCount);
            }
            finally
            {
                sendRuntime.Dispose();
            }

            var encodeFailure = new InvalidOperationException("encode failed");
            var encodeRuntime = new NetworkRuntime(
                () => new ManualClock(),
                () => new RecordingTransport(),
                () => new ThrowingCodec(encodeFailure),
                codec => new RpcMessageDispatcher(codec));
            encodeRuntime.Initialize();
            try
            {
                InvalidOperationException actual = Assert.Throws<InvalidOperationException>(
                    () => encodeRuntime.SendRequestForTest(C2G_Login.Create(), 1000));
                Assert.AreSame(encodeFailure, actual);
                Assert.AreEqual(0, encodeRuntime.PendingRpcCount);
            }
            finally
            {
                encodeRuntime.Dispose();
            }
        }

        [Test]
        public void RpcCancellationRaceAfterPendingAddLeavesNoWaitOrRegistration()
        {
            var codec = new MemoryPackMessageCodec();
            var dispatcher = new RpcMessageDispatcher(codec);
            using var cancellation = new CancellationTokenSource();
            dispatcher.BeforeCancellationRegistrationForTest = cancellation.Cancel;

            RpcRequest request = dispatcher.SendRequest(C2G_Login.Create(), 0, 1000, cancellation.Token);

            Assert.IsTrue(request.ResponseTask.IsCanceled);
            Assert.AreEqual(0, dispatcher.PendingCount);
            Assert.AreEqual(0, dispatcher.ActiveCancellationRegistrationCount);
            Assert.DoesNotThrow(() => dispatcher.Cancel(request.RpcId));
            dispatcher.Dispose();
            Assert.AreEqual(0, dispatcher.PendingCount);
            Assert.AreEqual(0, dispatcher.ActiveCancellationRegistrationCount);
        }

        [Test]
        public void ClientAssemblyAndSourceBoundaryIsPhysical()
        {
            string root = ApplicationRoot();
            string networkDir = Path.Combine(root, "Assets/Game/Scripts/Network");
            string utilityNetworkDir = Path.Combine(root, "Assets/Game/Scripts/Utility/Network");
            string gameSource = File.ReadAllText(Path.Combine(root, "Assets/Game/Scripts/Game.cs"));
            string asmdef = File.ReadAllText(Path.Combine(root, "Assets/Game/Game.asmdef"));

            StringAssert.Contains("\"name\": \"Game\"", asmdef);
            StringAssert.DoesNotContain("Google.Protobuf", asmdef);
            StringAssert.Contains("MemoryPack", asmdef);
            StringAssert.Contains("Unity.Mathematics", asmdef);

            Assert.IsTrue(Directory.Exists(Path.Combine(networkDir, "Generated")));
            Assert.IsTrue(File.Exists(Path.Combine(utilityNetworkDir, "Adapter/NetworkRuntime.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "Assets/Game/Scripts/ThirdParty/Kcp/Kcp.cs")));
            Assert.IsFalse(Directory.Exists(Path.Combine(networkDir, "Adapter")));
            Assert.IsFalse(Directory.Exists(Path.Combine(networkDir, "Diagnostics")));
            Assert.IsFalse(Directory.Exists(Path.Combine(networkDir, "Runtime")));
            Assert.IsFalse(File.Exists(Path.Combine(networkDir, "Protocol/ProtoDemo.cs")));
            foreach (string file in Directory.GetFiles(networkDir, "*.cs", SearchOption.AllDirectories)
                         .Concat(Directory.GetFiles(utilityNetworkDir, "*.cs", SearchOption.AllDirectories)))
            {
                string text = File.ReadAllText(file);
                StringAssert.DoesNotContain("Server/GameServer", text);
                StringAssert.DoesNotContain("Unity.Loader", text);
                StringAssert.DoesNotContain("MonoBehaviour", text);
                StringAssert.DoesNotContain("Scene : Entity", text);
            }

            string[] publicMemberNames = typeof(INetworkRuntime)
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Select(member => member.Name)
                .ToArray();
            CollectionAssert.IsSubsetOf(
                new[]
                {
                    "get_IsInitialized",
                    "get_ConnectionState",
                    "get_ConnectionGeneration",
                    "Initialize",
                    "Update",
                    "ConnectAsync",
                    "CallAsync",
                    "RegisterPushHandler",
                    "RegisterDisconnectedHandler",
                    "Disconnect",
                    "Dispose",
                    "IsInitialized",
                    "ConnectionState",
                    "ConnectionGeneration"
                },
                publicMemberNames);
            CollectionAssert.DoesNotContain(publicMemberNames, "Connect");
            CollectionAssert.DoesNotContain(publicMemberNames, "Register");
            CollectionAssert.DoesNotContain(publicMemberNames, "Login");
            CollectionAssert.DoesNotContain(publicMemberNames, "Logout");

            StringAssert.Contains("new NetworkRuntime", gameSource);
            StringAssert.Contains("RegisterUtility<INetworkRuntime>", gameSource);
            StringAssert.Contains("OnlineAccountLifecycleController", gameSource);
            StringAssert.DoesNotContain("new GameObject(\"OnlineAccountLifecycleController\")", gameSource);
            StringAssert.DoesNotContain("networkUpdate", gameSource);
        }

        private static void AssertMessageContract<TMessage, TInterface>(ushort opcode, string responseType, string orderedProperty, int order)
        {
            Type type = typeof(TMessage);
            Assert.IsTrue(typeof(TInterface).IsAssignableFrom(type));
            Assert.IsNotNull(type.GetCustomAttribute<MemoryPackableAttribute>());
            Assert.AreEqual(opcode, type.GetCustomAttribute<MessageAttribute>()?.Opcode);
            if (responseType != null)
            {
                Assert.AreEqual(responseType, type.GetCustomAttribute<ResponseTypeAttribute>()?.Type);
            }

            PropertyInfo property = type.GetProperty(orderedProperty);
            Assert.IsNotNull(property);
            Assert.AreEqual(order, property.GetCustomAttribute<MemoryPackOrderAttribute>()?.Order);
        }

        private static string ApplicationRoot()
        {
            return Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ?? throw new InvalidOperationException("Cannot resolve project root.");
        }

        private static NetworkRuntime CreateTestRuntime(IKcpTransport transport)
        {
            return new NetworkRuntime(
                () => new ManualClock(),
                () => transport,
                () => new MemoryPackMessageCodec(),
                codec => new RpcMessageDispatcher(codec));
        }

        private static IEnumerator WaitUntil(Func<bool> condition, string label, int maxFrames = 120)
        {
            int frames = 0;
            while (!condition() && frames++ < maxFrames)
            {
                LifeCycle.Update();
                yield return null;
            }

            Assert.IsTrue(condition(), $"Condition did not become true within the expected frame budget: {label}.");
        }

        private static IEnumerator WaitUntilCompleted(Task task, int maxFrames = 120)
        {
            int frames = 0;
            while (!task.IsCompleted && frames++ < maxFrames)
            {
                LifeCycle.Update();
                yield return null;
            }

            Assert.IsTrue(task.IsCompleted, "Task did not complete within the expected frame budget.");
            if (task.IsFaulted)
            {
                throw task.Exception.GetBaseException();
            }
        }

        private static void ResetRuntimeHeartbeatArchitecture()
        {
            FieldInfo field = typeof(Architecture<RuntimeHeartbeatArchitecture>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
            if (field?.GetValue(null) is IArchitecture architecture)
            {
                architecture.Deinit();
            }
            else
            {
                field?.SetValue(null, null);
            }

            RuntimeHeartbeatArchitecture.Network = null;
        }

        private sealed class RuntimeHeartbeatArchitecture : Architecture<RuntimeHeartbeatArchitecture>
        {
            public static INetworkRuntime Network;

            protected override void Init()
            {
                RegisterModel(new OnlineAccountModel());
                RegisterUtility<INetworkRuntime>(Network);
                RegisterUtility<INetworkConfigProvider>(new RuntimeHeartbeatConfigProvider());
                RegisterUtility<IOnlineAccountPreferenceStore>(new RuntimeHeartbeatPreferenceStore());
                RegisterSystem(new OnlineAccountSystem());
            }
        }

        private sealed class RuntimeHeartbeatConfigProvider : INetworkConfigProvider
        {
            public Task<NetworkConfigResult> LoadAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(NetworkConfigResult.Ok(new NetworkEndpoint("127.0.0.1", 31001)));
            }
        }

        private sealed class RuntimeHeartbeatPreferenceStore : IOnlineAccountPreferenceStore
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

        private static byte[] CreateAck(uint remoteConn, uint localConn)
        {
            byte[] packet = new byte[9];
            packet[0] = KcpClientTransport.ACK;
            WriteUInt32(packet, 1, remoteConn);
            WriteUInt32(packet, 5, localConn);
            return packet;
        }

        private static byte[] CreateMsg(uint remoteConn, uint localConn)
        {
            byte[] packet = new byte[9];
            packet[0] = KcpClientTransport.MSG;
            WriteUInt32(packet, 1, remoteConn);
            WriteUInt32(packet, 5, localConn);
            return packet;
        }

        private static uint ReadUInt32(byte[] packet, int offset)
        {
            return BitConverter.ToUInt32(packet, offset);
        }

        private static void WriteUInt32(byte[] packet, int offset, uint value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, packet, offset, bytes.Length);
        }

        private sealed class ManualClock : INetworkClock
        {
            private long current;

            public long ElapsedMilliseconds { get; private set; }

            public long DeltaMilliseconds { get; private set; }

            public void Advance(long milliseconds)
            {
                current += milliseconds;
            }

            public void Update()
            {
                DeltaMilliseconds = current - ElapsedMilliseconds;
                ElapsedMilliseconds = current;
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

        private sealed class ScriptedClientTransport : IClientTransport
        {
            private readonly Queue<ClientTransportEvent> events = new Queue<ClientTransportEvent>();

            public bool IsConnected { get; private set; }
            public int ConnectionGeneration { get; private set; }
            public List<byte[]> SentPackets { get; } = new List<byte[]>();

            public Task ConnectAsync(int generation, NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ConnectionGeneration = generation;
                IsConnected = true;
                events.Enqueue(ClientTransportEvent.Connected(generation));
                return Task.CompletedTask;
            }

            public void Send(int generation, byte[] body)
            {
                Assert.AreEqual(ConnectionGeneration, generation);
                SentPackets.Add(body);
            }

            public bool TryDequeue(out ClientTransportEvent transportEvent)
            {
                if (events.Count == 0)
                {
                    transportEvent = default;
                    return false;
                }

                transportEvent = events.Dequeue();
                return true;
            }

            public void EnqueueReceivedPacket(int generation, byte[] packet)
            {
                events.Enqueue(ClientTransportEvent.FrameReceived(generation, packet));
            }

            public void Disconnect(NetworkDisconnectReason reason)
            {
                IsConnected = false;
                events.Enqueue(ClientTransportEvent.Disconnected(ConnectionGeneration, reason, reason.ToString()));
            }

            public void Pump(long nowMilliseconds)
            {
            }

            public void Dispose()
            {
                IsConnected = false;
                SentPackets.Clear();
                events.Clear();
            }
        }

        private sealed class RecordingHeartbeatLifecycle : IOnlineHeartbeatLifecycle
        {
            public int UpdateCount { get; private set; }
            public int ResumeCount { get; private set; }
            public int StopCount { get; private set; }

            public void UpdateHeartbeat()
            {
                UpdateCount++;
            }

            public void NotifyApplicationResumed()
            {
                ResumeCount++;
            }

            public void StopHeartbeatForNetworkLifecycle()
            {
                StopCount++;
            }
        }

        private sealed class RecordingApplicationLifecycleSource : IOnlineApplicationLifecycleSource
        {
            public event Action<bool> FocusChanged;
            public event Action<PauseState> PauseStateChanged;

            public int FocusSubscriberCount => FocusChanged?.GetInvocationList().Length ?? 0;
            public int PauseSubscriberCount => PauseStateChanged?.GetInvocationList().Length ?? 0;
            public bool Disposed { get; private set; }

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
                Disposed = true;
            }
        }

        private sealed class ThrowingDisposeTransport : IKcpTransport
        {
            public bool DisposeCalled { get; private set; }

            public bool IsConnected => false;

            public int SentPacketCount => 0;

            public int Pump(long nowMilliseconds)
            {
                return 0;
            }

            public void Send(byte[] packet)
            {
            }

            public bool TryReceive(out byte[] packet)
            {
                packet = null;
                return false;
            }

            public void Close()
            {
            }

            public void Dispose()
            {
                DisposeCalled = true;
                throw new InvalidOperationException("dispose failed");
            }
        }

        private sealed class RecordingTransport : IKcpTransport
        {
            private readonly Queue<byte[]> sentPackets = new Queue<byte[]>();
            private readonly Queue<byte[]> receivedPackets = new Queue<byte[]>();

            public Exception SendException { get; set; }

            public bool IsConnected => false;

            public int SentPacketCount { get; private set; }

            public int Pump(long nowMilliseconds)
            {
                return receivedPackets.Count;
            }

            public void Send(byte[] packet)
            {
                if (SendException != null)
                {
                    throw SendException;
                }

                sentPackets.Enqueue(packet);
                SentPacketCount++;
            }

            public bool TryReceive(out byte[] packet)
            {
                if (receivedPackets.Count == 0)
                {
                    packet = null;
                    return false;
                }

                packet = receivedPackets.Dequeue();
                return true;
            }

            public void EnqueueReceivedPacket(byte[] packet)
            {
                receivedPackets.Enqueue(packet);
            }

            public byte[] DequeueSentPacket()
            {
                return sentPackets.Dequeue();
            }

            public void Close()
            {
                receivedPackets.Clear();
            }

            public void Dispose()
            {
                sentPackets.Clear();
                receivedPackets.Clear();
            }
        }

        private sealed class RecordingDatagramSocket : IUdpDatagramSocket
        {
            private readonly Queue<byte[]> receivedPackets = new Queue<byte[]>();

            public bool IsDisposed { get; private set; }

            public int Available => receivedPackets.Count;

            public List<byte[]> SentPackets { get; } = new List<byte[]>();

            public void Bind(EndPoint localEndPoint)
            {
            }

            public int ReceiveFrom(byte[] buffer, int offset, int length, SocketFlags socketFlags, ref EndPoint remoteEndPoint)
            {
                byte[] packet = receivedPackets.Dequeue();
                Buffer.BlockCopy(packet, 0, buffer, offset, packet.Length);
                remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 30001);
                return packet.Length;
            }

            public int SendTo(byte[] buffer, int offset, int length, SocketFlags socketFlags, EndPoint remoteEndPoint)
            {
                byte[] packet = new byte[length];
                Buffer.BlockCopy(buffer, offset, packet, 0, length);
                SentPackets.Add(packet);
                return length;
            }

            public void EnqueueReceived(byte[] packet)
            {
                receivedPackets.Enqueue(packet);
            }

            public void Dispose()
            {
                IsDisposed = true;
                receivedPackets.Clear();
            }
        }

        private sealed class ThrowingCodec : IMessageCodec
        {
            private readonly Exception encodeException;
            private readonly MemoryPackMessageCodec inner = new MemoryPackMessageCodec();

            public ThrowingCodec(Exception encodeException)
            {
                this.encodeException = encodeException;
            }

            public byte[] Encode(IMessage message)
            {
                throw encodeException;
            }

            public DecodedMessage Decode(byte[] packet)
            {
                return inner.Decode(packet);
            }

            public Type GetResponseType(Type requestType)
            {
                return inner.GetResponseType(requestType);
            }
        }
    }
}
