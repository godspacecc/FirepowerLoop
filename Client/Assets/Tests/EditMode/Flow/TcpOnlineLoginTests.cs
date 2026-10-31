using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ET;
using Game.Network;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TMPro;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Game.Tests
{
    public sealed class TcpOnlineLoginTests
    {
        [TearDown]
        public void TearDown()
        {
            UIHelper.ResetForTests();
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            TestReset.ResetArchitecture();
            ResetTestOnlineArchitecture();
            Architecture<Game>.OnRegisterPatch = _ => { };
        }

        [Test]
        public void NetworkConfigMissingUsesDefaultAndInvalidFileDoesNotFallback()
        {
            var missing = new StreamingAssetsNetworkConfigProvider(_ => throw new FileNotFoundException());
            var missingResult = missing.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(missingResult.Success, Is.True);
            Assert.That(missingResult.Endpoint.ToString(), Is.EqualTo("127.0.0.1:31011"));

            var invalid = new StreamingAssetsNetworkConfigProvider(_ => Task.FromResult("{\"gameGateEndpoint\":\" :70000\"}"));
            var invalidResult = invalid.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(invalidResult.Success, Is.False);
            Assert.That(invalidResult.Error, Is.EqualTo(NetworkConfigError.Invalid));
        }

        [Test]
        public void PlayerSavedNetworkEndpointOverridesEmbeddedConfigAndRejectsInvalidReplacement()
        {
            string root = Path.Combine(Path.GetTempPath(), "FirepowerLoopNetworkEndpoint", Guid.NewGuid().ToString("N"));
            try
            {
                var provider = new StreamingAssetsNetworkConfigProvider(
                    _ => Task.FromResult("{\"gameGateEndpoint\":\"127.0.0.1:31001\"}"),
                    root);
                Assert.That(provider.TrySaveEndpoint(" 192.168.31.94:31001 ", out NetworkEndpoint saved, out string error), Is.True, error);
                Assert.That(saved.ToString(), Is.EqualTo("192.168.31.94:31001"));

                var reloaded = new StreamingAssetsNetworkConfigProvider(
                    _ => Task.FromResult("{\"gameGateEndpoint\":\"127.0.0.1:31001\"}"),
                    root);
                NetworkConfigResult result = reloaded.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
                Assert.That(result.Success, Is.True);
                Assert.That(result.Endpoint.ToString(), Is.EqualTo("192.168.31.94:31001"));

                Assert.That(reloaded.TrySaveEndpoint("missing-port", out _, out _), Is.False);
                NetworkConfigResult unchanged = reloaded.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
                Assert.That(unchanged.Endpoint.ToString(), Is.EqualTo("192.168.31.94:31001"));

                Assert.That(reloaded.TryResetEndpoint(out error), Is.True, error);
                NetworkConfigResult reset = reloaded.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
                Assert.That(reset.Endpoint.ToString(), Is.EqualTo("127.0.0.1:31001"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void TcpFrameWriterMatchesEtOuterFrameContract()
        {
            byte[] body = { 0xE4, 0x2E, 0x11, 0x22 };
            byte[] frame = TcpFrameCodec.WriteFrame(body);
            Assert.That(frame[0], Is.EqualTo(4));
            Assert.That(frame[1], Is.EqualTo(0));
            CollectionAssert.AreEqual(body, frame.Skip(2).ToArray());
        }

        [Test]
        public void TcpFrameReaderHandlesPartialStickyAndMultipleFramesAndRejectsInvalidLength()
        {
            var assembler = new TcpFrameAssembler();
            byte[] first = TcpFrameCodec.WriteFrame(new byte[] { 1, 2 });
            byte[] second = TcpFrameCodec.WriteFrame(new byte[] { 3, 4, 5 });
            byte[] sticky = first.Concat(second).ToArray();
            Assert.That(assembler.Append(sticky.Take(3).ToArray(), 3).ToArray(), Is.Empty);
            var frames = assembler.Append(sticky.Skip(3).ToArray(), sticky.Length - 3).ToArray();
            Assert.That(frames.Length, Is.EqualTo(2));
            CollectionAssert.AreEqual(new byte[] { 1, 2 }, frames[0]);
            CollectionAssert.AreEqual(new byte[] { 3, 4, 5 }, frames[1]);
            Assert.Throws<InvalidDataException>(() => new TcpFrameAssembler().Append(new byte[] { 1, 0 }, 2).ToArray());
        }

        [UnityTest]
        public IEnumerator NetworkRuntimeInitializeDoesNotConnectAndProcessesEventsOnUpdate()
        {
            var transport = new FakeTransport();
            var runtime = CreateRuntime(transport);
            runtime.Initialize();
            Assert.That(runtime.ConnectionState, Is.EqualTo(NetworkConnectionState.Disconnected));
            Assert.That(transport.ConnectCalls, Is.EqualTo(0));

            Task connect = runtime.ConnectAsync(new NetworkEndpoint("127.0.0.1", 31001), 1000, CancellationToken.None);
            Assert.That(runtime.ConnectionState, Is.EqualTo(NetworkConnectionState.Connecting));
            Assert.That(connect.IsCompleted, Is.False);
            runtime.Update();
            yield return WaitUntilCompleted(connect);
            AssertTaskSucceeded(connect);
            Assert.That(runtime.ConnectionState, Is.EqualTo(NetworkConnectionState.Connected));
        }

        [UnityTest]
        public IEnumerator DisconnectFailsPendingRpcAndOldGenerationCannotAffectNewConnection()
        {
            var transport = new FakeTransport();
            var runtime = CreateRuntime(transport);
            runtime.Initialize();
            Task connect = runtime.ConnectAsync(new NetworkEndpoint("127.0.0.1", 31001), 1000, CancellationToken.None);
            runtime.Update();
            yield return WaitUntilCompleted(connect);
            AssertTaskSucceeded(connect);
            var call = runtime.CallAsync<G2C_Login>(C2G_Login.Create(), 10000, CancellationToken.None);
            Assert.That(runtime.PendingRpcCount, Is.EqualTo(1));
            transport.Enqueue(ClientTransportEvent.Disconnected(runtime.ConnectionGeneration - 1, NetworkDisconnectReason.RemoteClosed, "old"));
            runtime.Update();
            Assert.That(runtime.PendingRpcCount, Is.EqualTo(1));
            runtime.Disconnect(NetworkDisconnectReason.RemoteClosed);
            Assert.That(runtime.PendingRpcCount, Is.EqualTo(0));
            yield return WaitUntilCompleted(call);
            Assert.That(call.IsFaulted, Is.True);
            Assert.That(call.Exception.Flatten().InnerExceptions.Any(ex => ex is IOException), Is.True);
        }

        [UnityTest]
        public IEnumerator NetworkRuntimeProtocolDiagnosticsLogRedactedJsonRespectSwitchAndDoNotBreakTransport()
        {
            var logs = new List<CapturedLog>();
            Application.LogCallback logCallback = (condition, stackTrace, type) => logs.Add(new CapturedLog(condition, type));
            NetworkRuntime runtime = null;
            NetworkRuntime throwingRuntime = null;
            var request = C2G_Login.Create();
            var response = G2C_Login.Create();
            var disabledRequest = C2G_Login.Create();
            var disabledResponse = G2C_Login.Create();
            Application.logMessageReceived += logCallback;
            try
            {
                NetworkDiagnostics.SetEnabled(true);
                var transport = new FakeTransport();
                var codec = new MemoryPackMessageCodec();
                runtime = CreateRuntime(transport, codec);
                runtime.Initialize();
                Task connect = runtime.ConnectAsync(new NetworkEndpoint("127.0.0.1", 31001), 1000, CancellationToken.None);
                runtime.Update();
                yield return WaitUntilCompleted(connect);
                AssertTaskSucceeded(connect);
                logs.Clear();

                request.UserName = "protocol-log-user";
                request.Password = "protocol-log-password";
                Task<G2C_Login> call = runtime.CallAsync<G2C_Login>(request, 10000, CancellationToken.None);
                Assert.That(transport.SentPackets.Count, Is.EqualTo(1));
                CapturedLog sendLog = logs.Single(log => log.Condition.StartsWith("[Network] Send C2G_Login="));
                JObject sendJson = JObject.Parse(sendLog.Condition.Substring(sendLog.Condition.IndexOf('=') + 1));
                Assert.That((int)sendJson[nameof(C2G_Login.RpcId)], Is.GreaterThan(0));
                Assert.That((string)sendJson[nameof(C2G_Login.UserName)], Is.EqualTo(request.UserName));
                Assert.That((string)sendJson[nameof(C2G_Login.Password)], Is.EqualTo("***"));
                Assert.That(sendLog.Condition, Does.Not.Contain(request.Password));
                var sentRequest = (C2G_Login)codec.Decode(transport.SentPackets[0]).Message;
                Assert.That(sentRequest.Password, Is.EqualTo("protocol-log-password"));
                Assert.That(request.Password, Is.EqualTo("protocol-log-password"));

                response.RpcId = request.RpcId;
                response.Error = 37;
                response.Message = "protocol-log-response";
                response.AccountId = 101;
                response.PlayerId = 202;
                response.SessionEpoch = 303;
                transport.Enqueue(ClientTransportEvent.FrameReceived(runtime.ConnectionGeneration, codec.Encode(response)));
                runtime.Update();
                yield return WaitUntilCompleted(call);
                AssertTaskSucceeded(call);
                Assert.That(call.Result.RpcId, Is.EqualTo(response.RpcId));

                CapturedLog receiveLog = logs.Single(log => log.Condition.StartsWith("[Network] Receive G2C_Login="));
                JObject receiveJson = JObject.Parse(receiveLog.Condition.Substring(receiveLog.Condition.IndexOf('=') + 1));
                Assert.That((int)receiveJson[nameof(G2C_Login.RpcId)], Is.EqualTo(response.RpcId));
                Assert.That((int)receiveJson[nameof(G2C_Login.Error)], Is.EqualTo(response.Error));
                Assert.That((string)receiveJson[nameof(G2C_Login.Message)], Is.EqualTo(response.Message));
                Assert.That((long)receiveJson[nameof(G2C_Login.AccountId)], Is.EqualTo(response.AccountId));
                Assert.That((long)receiveJson[nameof(G2C_Login.PlayerId)], Is.EqualTo(response.PlayerId));
                Assert.That((long)receiveJson[nameof(G2C_Login.SessionEpoch)], Is.EqualTo(response.SessionEpoch));
                Assert.That(new[] { sendLog.Condition, receiveLog.Condition }, Has.None.Contains("运行时操作失败："));
                Assert.That(logs.Any(log => log.Condition.Contains("发送请求 type=") ||
                    log.Condition.Contains("收到响应 type=") || log.Condition.Contains("收到消息 type=")), Is.False);
                call.Result.Dispose();

                NetworkDiagnostics.SetEnabled(false);
                logs.Clear();
                int sentBeforeDisabledCall = transport.SentPackets.Count;
                disabledRequest.UserName = "diagnostics-disabled-user";
                disabledRequest.Password = "diagnostics-disabled-password";
                Task<G2C_Login> disabledCall = runtime.CallAsync<G2C_Login>(disabledRequest, 10000, CancellationToken.None);
                Assert.That(transport.SentPackets.Count, Is.EqualTo(sentBeforeDisabledCall + 1));
                disabledResponse.RpcId = disabledRequest.RpcId;
                transport.Enqueue(ClientTransportEvent.FrameReceived(runtime.ConnectionGeneration, codec.Encode(disabledResponse)));
                runtime.Update();
                yield return WaitUntilCompleted(disabledCall);
                AssertTaskSucceeded(disabledCall);
                Assert.That(logs.Any(log => log.Condition.StartsWith("[Network] Send ") ||
                    log.Condition.StartsWith("[Network] Receive ")), Is.False);
                disabledCall.Result.Dispose();

                runtime.Dispose();
                runtime = null;
                NetworkDiagnostics.SetEnabled(true);
                logs.Clear();

                var throwingTransport = new FakeTransport();
                var throwingCodec = new ThrowingJsonCodec();
                throwingRuntime = CreateRuntime(throwingTransport, throwingCodec);
                throwingRuntime.Initialize();
                Task throwingConnect = throwingRuntime.ConnectAsync(new NetworkEndpoint("127.0.0.1", 31001), 1000, CancellationToken.None);
                throwingRuntime.Update();
                yield return WaitUntilCompleted(throwingConnect);
                AssertTaskSucceeded(throwingConnect);
                logs.Clear();

                LogAssert.Expect(LogType.Warning, new Regex(@"^\[Network\] Send ThrowingJsonRequest JSON序列化失败 error=\w+$"));
                var throwingRequest = new ThrowingJsonRequest();
                Task<ThrowingJsonResponse> throwingCall = throwingRuntime.CallAsync<ThrowingJsonResponse>(throwingRequest, 10000, CancellationToken.None);
                Assert.That(throwingTransport.SentPackets.Count, Is.EqualTo(1));

                LogAssert.Expect(LogType.Warning, new Regex(@"^\[Network\] Receive ThrowingJsonResponse JSON序列化失败 error=\w+$"));
                throwingCodec.Response.RpcId = throwingRequest.RpcId;
                throwingTransport.Enqueue(ClientTransportEvent.FrameReceived(throwingRuntime.ConnectionGeneration, new byte[] { 2 }));
                throwingRuntime.Update();
                yield return WaitUntilCompleted(throwingCall);
                AssertTaskSucceeded(throwingCall);
                Assert.That(throwingCall.Result, Is.SameAs(throwingCodec.Response));
                Assert.That(throwingRuntime.ConnectionState, Is.EqualTo(NetworkConnectionState.Connected));
                Assert.That(logs.Count(log => log.Type == LogType.Warning && log.Condition.Contains("JSON序列化失败")), Is.EqualTo(2));
            }
            finally
            {
                Application.logMessageReceived -= logCallback;
                runtime?.Dispose();
                throwingRuntime?.Dispose();
                request.Dispose();
                response.Dispose();
                disabledRequest.Dispose();
                disabledResponse.Dispose();
                NetworkDiagnostics.SetEnabled(true);
            }
        }

        [UnityTest]
        public IEnumerator TcpTransportLoopbackConnectsWithBoundedWaitAndQueuesConnectedEvent()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            Task<TcpClient> accept = listener.AcceptTcpClientAsync();
            var transport = new TcpClientTransport();
            try
            {
                Task connect = transport.ConnectAsync(5, new NetworkEndpoint("127.0.0.1", endpoint.Port), 1000, CancellationToken.None);
                yield return WaitUntilCompleted(connect);
                yield return WaitUntilCompleted(accept);

                AssertTaskSucceeded(connect);
                AssertTaskSucceeded(accept);
                Assert.That(transport.IsConnected, Is.True);
                Assert.That(transport.ConnectionGeneration, Is.EqualTo(5));
                Assert.That(transport.TryDequeue(out ClientTransportEvent evt), Is.True);
                Assert.That(evt.Kind, Is.EqualTo(ClientTransportEventKind.Connected));
                Assert.That(evt.Generation, Is.EqualTo(5));

                accept.Result.Dispose();
            }
            finally
            {
                transport.Dispose();
                listener.Stop();
            }
        }

        [UnityTest]
        public IEnumerator TcpTransportKeepsReceivingAfterConnectTimeoutWindow()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            Task<TcpClient> accept = listener.AcceptTcpClientAsync();
            var transport = new TcpClientTransport();
            TcpClient accepted = null;
            try
            {
                Task connect = transport.ConnectAsync(7, new NetworkEndpoint("127.0.0.1", endpoint.Port), 1000, CancellationToken.None);
                yield return WaitUntilCompleted(connect);
                yield return WaitUntilCompleted(accept);

                AssertTaskSucceeded(connect);
                AssertTaskSucceeded(accept);
                accepted = accept.Result;
                Assert.That(transport.TryDequeue(out ClientTransportEvent connected), Is.True);
                Assert.That(connected.Kind, Is.EqualTo(ClientTransportEventKind.Connected));
                Assert.That(connected.Generation, Is.EqualTo(7));

                yield return WaitForMilliseconds(1200);

                byte[] body = { 0x21, 0x22 };
                byte[] frame = TcpFrameCodec.WriteFrame(body);
                NetworkStream serverStream = accepted.GetStream();
                Task write = serverStream.WriteAsync(frame, 0, frame.Length);
                yield return WaitUntilCompleted(write);
                AssertTaskSucceeded(write);

                ClientTransportEvent received = default;
                bool gotFrame = false;
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(1000);
                while (DateTime.UtcNow < deadline && !gotFrame)
                {
                    while (transport.TryDequeue(out ClientTransportEvent evt))
                    {
                        if (evt.Kind == ClientTransportEventKind.Frame)
                        {
                            received = evt;
                            gotFrame = true;
                            break;
                        }
                    }

                    if (!gotFrame)
                    {
                        yield return null;
                    }
                }

                Assert.That(gotFrame, Is.True);
                Assert.That(received.Generation, Is.EqualTo(7));
                CollectionAssert.AreEqual(body, received.Frame);
                Assert.That(transport.IsConnected, Is.True);
            }
            finally
            {
                accepted?.Dispose();
                transport.Dispose();
                listener.Stop();
            }
        }

        [UnityTest]
        public IEnumerator ConnectingCancelKeepsCanceledResultAndClosesTransport()
        {
            var transport = new FakeTransport { DelayConnect = true };
            var runtime = CreateRuntime(transport);
            var cancellation = new CancellationTokenSource();
            runtime.Initialize();
            try
            {
                Task connect = runtime.ConnectAsync(new NetworkEndpoint("127.0.0.1", 31001), 1000, cancellation.Token);
                Assert.That(runtime.ConnectionState, Is.EqualTo(NetworkConnectionState.Connecting));
                cancellation.Cancel();
                yield return WaitUntilCompleted(connect);

                Assert.That(connect.IsCanceled, Is.True);
                Assert.That(runtime.ConnectionState, Is.EqualTo(NetworkConnectionState.Disconnected));
                runtime.Update();
                Assert.That(connect.IsCanceled, Is.True);
                Assert.That(transport.IsConnected, Is.False);
            }
            finally
            {
                cancellation.Dispose();
                runtime.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator OverlappingRuntimeConnectsCompleteIndependentlyAndIgnoreOldEvents()
        {
            var transport = new ManualConnectTransport();
            var runtime = CreateRuntime(transport);
            runtime.Initialize();
            try
            {
                Task oldConnect = runtime.ConnectAsync(new NetworkEndpoint("127.0.0.1", 31001), 1000, CancellationToken.None);
                Task newConnect = runtime.ConnectAsync(new NetworkEndpoint("127.0.0.1", 31002), 1000, CancellationToken.None);

                yield return WaitUntilCompleted(oldConnect);
                Assert.That(oldConnect.IsFaulted, Is.True);
                Assert.That(runtime.ConnectionGeneration, Is.EqualTo(2));

                transport.Enqueue(ClientTransportEvent.Connected(1));
                runtime.Update();
                Assert.That(newConnect.IsCompleted, Is.False);

                transport.Enqueue(ClientTransportEvent.Connected(2));
                runtime.Update();
                yield return WaitUntilCompleted(newConnect);
                AssertTaskSucceeded(newConnect);
                Assert.That(runtime.ConnectionState, Is.EqualTo(NetworkConnectionState.Connected));
            }
            finally
            {
                runtime.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator OldConnectTokenCancellationDoesNotCancelNewRuntimeConnect()
        {
            var transport = new ManualConnectTransport();
            var runtime = CreateRuntime(transport);
            var oldCancellation = new CancellationTokenSource();
            runtime.Initialize();
            try
            {
                Task oldConnect = runtime.ConnectAsync(new NetworkEndpoint("127.0.0.1", 31001), 1000, oldCancellation.Token);
                Task newConnect = runtime.ConnectAsync(new NetworkEndpoint("127.0.0.1", 31002), 1000, CancellationToken.None);
                oldCancellation.Cancel();

                yield return WaitUntilCompleted(oldConnect);
                Assert.That(oldConnect.IsFaulted, Is.True);

                transport.Enqueue(ClientTransportEvent.Connected(2));
                runtime.Update();
                yield return WaitUntilCompleted(newConnect);
                AssertTaskSucceeded(newConnect);
                Assert.That(runtime.ConnectionState, Is.EqualTo(NetworkConnectionState.Connected));
            }
            finally
            {
                oldCancellation.Dispose();
                runtime.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator OnlineRemoteDisconnectInvalidatesPendingLoginResult()
        {
            var harness = CreateOnlineHarness();
            Task<OnlineAccountOperationResult> login = harness.System.LoginAsync("review_user", "Pass_123");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);

            harness.Network.RaiseDisconnected(NetworkDisconnectReason.RemoteClosed);
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Disconnected));
            Assert.That(harness.System.Snapshot.LastResultCode, Is.EqualTo(OnlineAccountResultCode.Disconnected));

            harness.Network.CompleteNextLoginSuccess("review_user");
            yield return WaitUntilCompleted(login);
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Disconnected));
            Assert.That(harness.System.Snapshot.LastResultCode, Is.EqualTo(OnlineAccountResultCode.Disconnected));
        }

        [UnityTest]
        public IEnumerator OnlineSessionReplacedInvalidatesPendingLoginResult()
        {
            var harness = CreateOnlineHarness();
            Task<OnlineAccountOperationResult> login = harness.System.LoginAsync("review_user", "Pass_123");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);

            harness.Network.RaiseSessionReplaced();
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Disconnected));
            Assert.That(harness.System.Snapshot.LastResultCode, Is.EqualTo(OnlineAccountResultCode.SessionReplaced));

            harness.Network.CompleteNextLoginSuccess("review_user");
            yield return WaitUntilCompleted(login);
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Disconnected));
            Assert.That(harness.System.Snapshot.LastResultCode, Is.EqualTo(OnlineAccountResultCode.SessionReplaced));
        }

        [UnityTest]
        public IEnumerator OnlineCloseAnonymousSessionInvalidatesPendingLoginResult()
        {
            var harness = CreateOnlineHarness();
            Task<OnlineAccountOperationResult> login = harness.System.LoginAsync("review_user", "Pass_123");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Connecting));

            harness.System.CloseAnonymousSession();
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Disconnected));

            harness.Network.CompleteNextLoginSuccess("review_user");
            yield return WaitUntilCompleted(login);
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Disconnected));
            Assert.That(harness.System.Snapshot.UserName, Is.Empty);
        }

        [UnityTest]
        public IEnumerator OnlineDeinitInvalidatesPendingLoginResult()
        {
            var harness = CreateOnlineHarness();
            Task<OnlineAccountOperationResult> login = harness.System.LoginAsync("review_user", "Pass_123");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);

            TestOnlineArchitecture.Interface.Deinit();
            harness.Network.CompleteNextLoginSuccess("review_user");
            yield return WaitUntilCompleted(login);
            Assert.That(harness.System.Snapshot.ConnectionState, Is.Not.EqualTo(OnlineConnectionState.Authenticated));
        }

        [UnityTest]
        public IEnumerator OnlineDeinitDuringLogoutIgnoresLateFaultWithoutPublishing()
        {
            var logs = new List<string>();
            using IDisposable capture = Log.CaptureForTests((type, message) =>
            {
                if (type == LogType.Error || type == LogType.Exception)
                {
                    logs.Add(message);
                }
            });

            var harness = CreateOnlineHarness();
            Task<OnlineAccountOperationResult> login = harness.System.LoginAsync("review_user", "Pass_123");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            harness.Network.CompleteNextLoginSuccess("review_user");
            yield return WaitUntilCompleted(login);
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Authenticated));

            Task<OnlineAccountOperationResult> logout = harness.System.LogoutAsync();
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            int disconnectsBeforeDeinit = harness.Network.DisconnectCount;

            TestOnlineArchitecture.Interface.Deinit();
            harness.Network.FailNextLogout(new ObjectDisposedException(nameof(FakeOnlineNetwork)));
            yield return WaitUntilCompleted(logout);

            Assert.That(logout.IsFaulted, Is.False);
            Assert.That(logout.IsCanceled, Is.False);
            Assert.That(logout.Result.Code, Is.EqualTo(OnlineAccountResultCode.Disconnected));
            Assert.That(harness.Network.DisconnectCount, Is.EqualTo(disconnectsBeforeDeinit));
            Assert.That(logs.Any(log => log.Contains("NullReferenceException")), Is.False);
        }

        [UnityTest]
        public IEnumerator OnlineDisconnectDuringLogoutInvalidatesPendingLogoutResult()
        {
            var harness = CreateOnlineHarness();
            Task<OnlineAccountOperationResult> login = harness.System.LoginAsync("review_user", "Pass_123");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            harness.Network.CompleteNextLoginSuccess("review_user");
            yield return WaitUntilCompleted(login);
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Authenticated));

            Task<OnlineAccountOperationResult> logout = harness.System.LogoutAsync();
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            harness.Network.RaiseDisconnected(NetworkDisconnectReason.RemoteClosed);
            harness.Network.CompleteNextLogoutSuccess();
            yield return WaitUntilCompleted(logout);
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Disconnected));
            Assert.That(harness.System.Snapshot.LastResultCode, Is.EqualTo(OnlineAccountResultCode.Disconnected));
        }

        [UnityTest]
        public IEnumerator LoginNewAccountPreferenceFailureKeepsAuthenticatedState()
        {
            var harness = CreateOnlineHarness();
            harness.Preferences.SaveResult = false;
            Task<OnlineAccountOperationResult> login = harness.System.LoginAsync("review_user", "Pass_123");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            harness.Network.CompleteNextLoginNewAccount("review_user");
            yield return WaitUntilCompleted(login);

            Assert.That(login.Result.Code, Is.EqualTo(OnlineAccountResultCode.PreferenceSaveFailed));
            Assert.That(login.Result.Success, Is.True);
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Authenticated));
            Assert.That(harness.System.Snapshot.LastResultCode, Is.EqualTo(OnlineAccountResultCode.PreferenceSaveFailed));
            Assert.That(harness.System.Snapshot.IsNewAccount, Is.True);
        }

        [UnityTest]
        public IEnumerator LoginNewAccountCompletesOnSingleConnection()
        {
            var harness = CreateOnlineHarness();
            Task<OnlineAccountOperationResult> login = harness.System.LoginAsync("review_user", "Pass_123");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            harness.Network.CompleteNextLoginNewAccount("review_user");
            yield return WaitUntilCompleted(login);

            Assert.That(login.Result.Code, Is.EqualTo(OnlineAccountResultCode.AccountCreated));
            Assert.That(login.Result.Success, Is.True);
            Assert.That(harness.Network.ConnectCount, Is.EqualTo(1));
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Authenticated));
            Assert.That(harness.System.Snapshot.UserName, Is.EqualTo("review_user"));
            Assert.That(harness.System.Snapshot.IsNewAccount, Is.True);
        }

        [UnityTest]
        public IEnumerator ExistingAccountWrongPassword_RemainsAnonymous()
        {
            var harness = CreateOnlineHarness();
            Task<OnlineAccountOperationResult> login = harness.System.LoginAsync("review_user", "Wrong_99");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            Assert.That(harness.Network.PendingRequestTypes.Single(), Is.EqualTo(nameof(C2G_Login)));
            harness.Network.CompleteNextLoginError(200104);
            yield return WaitUntilCompleted(login);

            Assert.That(login.Result.Code, Is.EqualTo(OnlineAccountResultCode.PasswordWrong));
            Assert.That(login.Result.Success, Is.False);
            Assert.That(harness.System.Snapshot.ConnectionState, Is.Not.EqualTo(OnlineConnectionState.Authenticated));
            Assert.That(harness.System.Snapshot.LastResultCode, Is.EqualTo(OnlineAccountResultCode.PasswordWrong));
        }

        [UnityTest]
        public IEnumerator SessionReplacedDuringLogoutDoesNotLetOldLogoutOverrideNewLogin()
        {
            var harness = CreateOnlineHarness();
            Task<OnlineAccountOperationResult> firstLogin = harness.System.LoginAsync("first_user", "Pass_123");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            harness.Network.CompleteNextLoginSuccess("first_user");
            yield return WaitUntilCompleted(firstLogin);
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Authenticated));

            Task<OnlineAccountOperationResult> oldLogout = harness.System.LogoutAsync();
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            harness.Network.RaiseSessionReplaced();
            int disconnectsAfterReplacement = harness.Network.DisconnectCount;

            Task<OnlineAccountOperationResult> newLogin = harness.System.LoginAsync("second_user", "Pass_123");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 2);
            harness.Network.CompleteNextLoginSuccess("second_user");
            yield return WaitUntilCompleted(newLogin);
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Authenticated));
            Assert.That(harness.System.Snapshot.UserName, Is.EqualTo("second_user"));

            harness.Network.CompleteNextLogoutSuccess();
            yield return WaitUntilCompleted(oldLogout);
            Assert.That(harness.Network.DisconnectCount, Is.EqualTo(disconnectsAfterReplacement));
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Authenticated));
            Assert.That(harness.System.Snapshot.UserName, Is.EqualTo("second_user"));
        }

        [UnityTest]
        public IEnumerator CloseAnonymousDuringLoginDoesNotLetOldLoginOverrideNewLogin()
        {
            var harness = CreateOnlineHarness();
            Task<OnlineAccountOperationResult> oldLogin = harness.System.LoginAsync("first_user", "Pass_123");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Connecting));

            harness.System.CloseAnonymousSession();
            Task<OnlineAccountOperationResult> newLogin = harness.System.LoginAsync("second_user", "Pass_123");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 2);
            harness.Network.CompleteLoginForUser("second_user");
            yield return WaitUntilCompleted(newLogin);
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Authenticated));
            Assert.That(harness.System.Snapshot.UserName, Is.EqualTo("second_user"));

            harness.Network.CompleteLoginForUser("first_user");
            yield return WaitUntilCompleted(oldLogin);
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Authenticated));
            Assert.That(harness.System.Snapshot.UserName, Is.EqualTo("second_user"));
        }

        [UnityTest]
        public IEnumerator RegisterSuccessAutoLogsInOnSameConnection()
        {
            var harness = CreateOnlineHarness();
            var registration = harness.System.RegisterAsync("registered_user", "Password-123");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            harness.Network.CompleteNextRegisterSuccess();
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            harness.Network.CompleteNextLoginSuccess("registered_user");
            yield return WaitUntilCompleted(registration);
            Assert.That(registration.Result.Code, Is.EqualTo(OnlineAccountResultCode.AccountCreated));
            Assert.That(harness.System.Snapshot.IsAuthenticated, Is.True);
            Assert.That(harness.System.Snapshot.PlayerId, Is.EqualTo(23));
        }

        [UnityTest]
        public IEnumerator RegisterRejectsLegacyShortPasswordWithoutNetworkRequest()
        {
            var harness = CreateOnlineHarness();
            var registration = harness.System.RegisterAsync("registered_user", "old123");
            yield return WaitUntilCompleted(registration);
            Assert.That(registration.Result.Code, Is.EqualTo(OnlineAccountResultCode.InvalidPassword));
            Assert.That(harness.Network.PendingCallCount, Is.Zero);
            Assert.That(harness.System.Snapshot.IsAuthenticated, Is.False);
        }

        [Test]
        public void LoginSuccessPublishesSnapshotAndPersistsOnlyUserName()
        {
            string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var store = new OnlineAccountPreferenceStore(dir);
            Assert.That(store.SaveLastUserName("tester", out _), Is.True);
            string json = File.ReadAllText(Path.Combine(dir, "Online", "online-account-preferences.json"));
            Assert.That(json, Does.Contain("lastUserName"));
            Assert.That(json, Does.Not.Contain("password").IgnoreCase);
            Assert.That(json, Does.Not.Contain("PlayerId"));
        }

        [Test]
        public void FailuresNeverPersistUserNameOrPassword()
        {
            string source = Read("Client/Assets/Game/Scripts/Model/Online/OnlineAccountContracts.cs");
            Assert.That(source, Does.Not.Contain("PasswordInput"));
            Assert.That(source, Does.Not.Contain("password"));
        }

        [Test]
        public void OnlineInputValidationAcceptsConfirmedBoundaries()
        {
            foreach (var sample in new[]
            {
                ("abc", ""),
                ("abc", "A"),
                ("abc", "Abc_1234"),
                ("  Abc_123  ", "A"),
                ("ABCDEFGHIJKLMNOPQRST", "A")
            })
            {
                Assert.That(
                    OnlineAccountSystem.ValidateInput(sample.Item1, sample.Item2),
                    Is.EqualTo(OnlineAccountResultCode.Success),
                    $"{sample.Item1}/{sample.Item2}");
            }
        }

        [Test]
        public void OnlineInputValidationRejectsInvalidUserNames()
        {
            foreach (string userName in new[] { "ab", new string('A', 33), "abc-def", "abc def", "中文名" })
            {
                Assert.That(
                    OnlineAccountSystem.ValidateInput(userName, "A"),
                    Is.EqualTo(OnlineAccountResultCode.InvalidUserName),
                    userName);
            }
        }

        [Test]
        public void OnlineInputValidationRejectsInvalidPasswords()
        {
            foreach (string password in new[] { "123456789", "abc def", "密码", "abc-def", "abc!", "\n", "A\n", "\r\n" })
            {
                Assert.That(
                    OnlineAccountSystem.ValidateInput("valid_user", password),
                    Is.EqualTo(OnlineAccountResultCode.InvalidPassword),
                    password);
            }
        }

        [Test]
        public void LogoutAndRemoteDisconnectClearAuthenticationWithoutReconnect()
        {
            string source = Read("Client/Assets/Game/Scripts/System/Online/OnlineAccountSystem.cs");
            Assert.That(source, Does.Contain("LogoutAsync"));
            Assert.That(source, Does.Contain("Cleared"));
            Assert.That(source, Does.Not.Contain("Reconnect"));
        }

        [Test]
        public void SessionReplacedClearsAuthenticationAndPublishesAttention()
        {
            Assert.That(OnlineAccountSystem.MapServerError(200106), Is.EqualTo(OnlineAccountResultCode.SessionReplaced));
            string source = Read("Client/Assets/Game/Scripts/System/Online/OnlineAccountSystem.cs");
            Assert.That(source, Does.Contain("G2C_SessionReplaced"));
            Assert.That(source, Does.Contain("OnlineAccountAttentionEvent"));
        }

        [Test]
        public void CloseAnonymousDisconnectsButAuthenticatedCloseKeepsConnection()
        {
            string source = Read("Client/Assets/Game/Scripts/System/Online/OnlineAccountSystem.cs");
            Assert.That(source, Does.Contain("CloseAnonymousSession"));
            Assert.That(source, Does.Contain("AnonymousConnected"));
            Assert.That(source, Does.Contain("Authenticated"));
        }

        [Test]
        public void GameRegistersSingleTcpRuntimeLifecycleWithoutEnablingKcp()
        {
            string game = Read("Client/Assets/Game/Scripts/Game.cs");
            Assert.That(game, Does.Contain("RegisterUtility<INetworkRuntime>(networkRuntime)"));
            Assert.That(game, Does.Contain("OnlineAccountLifecycleController"));
            Assert.That(Regex.Matches(game, @"new\s+NetworkRuntime\s*\(").Count, Is.EqualTo(1));
            Assert.That(Regex.Matches(game, @"new\s+OnlineAccountSystem\s*\(").Count, Is.EqualTo(1));
            Assert.That(game, Does.Contain("onlineAccountSystem = new OnlineAccountSystem();"));
            Assert.That(game, Does.Contain("RegisterSystem(onlineAccountSystem);"));
            Assert.That(game, Does.Contain("networkLifecycle.Initialize(networkRuntime, onlineAccountSystem);"));
            Assert.That(Regex.Matches(game, @"new\s+OnlineAccountLifecycleController\s*\(").Count, Is.EqualTo(1));
            string runtime = Read("Client/Assets/Game/Scripts/Utility/Network/Adapter/NetworkRuntime.cs");
            Assert.That(runtime, Does.Contain("new TcpClientTransport()"));
            Assert.That(runtime, Does.Not.Contain("new KcpClientTransport()"));
        }

        [Test]
        public void StartOnlineEntryAndLoginPrefabMatchConfirmedContract()
        {
            string startUi = Read("Client/Assets/Game/Scripts/Controller/UI/Home/StartUI.cs");
            string startCtrl = Read("Client/Assets/Game/Scripts/Controller/UICtrl/Home/StartUICtrl.cs");
            string onlinePrefab = Read("Client/Assets/Game/Prefabs/UI/Online/OnlineLogin.prefab");
            Assert.That(startUi, Does.Contain("BtnOnline"));
            Assert.That(startCtrl, Does.Contain("OnlineLoginUICtrl.ShowForArcadeLobby()"));
            Assert.That(onlinePrefab, Does.Contain("OnlineLogin"));
            Assert.That(onlinePrefab, Does.Contain("560"));
            Assert.That(onlinePrefab, Does.Contain("480"));

            GameObject startPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab");
            Assert.That(startPrefab, Is.Not.Null);
            Assert.That(startPrefab.name, Is.EqualTo("Start"));
            Assert.That(startPrefab.transform.parent, Is.Null);

            StartUI ui = startPrefab.GetComponent<StartUI>();
            Assert.That(ui, Is.Not.Null);
            Assert.That(ui.ButtonGroup, Is.Not.Null);
            Assert.That(ui.BtnOnline, Is.Not.Null);
            var onlineText = ui.BtnOnline.transform.Find("BtnOnlineText");
            Assert.That(onlineText, Is.Not.Null);
            Assert.That(ui.BtnOnline.GetComponent<Button>(), Is.Not.Null);
            Assert.That(onlineText.GetComponent<TMP_Text>(), Is.Not.Null);
            // 首页可独立添加其它功能；此处只核对既有联机导航仍可用。

            Assert.That(ui.BtnOnline.transform.IsChildOf(ui.ArcadeModePanel.transform), Is.True);
            string[] expected = { "BtnNewGame", "BtnContinue", "BtnSetting" };
            string[] actual = Enumerable.Range(0, ui.ButtonGroup.childCount)
                .Select(index => ui.ButtonGroup.GetChild(index).name)
                .Where(name => name.StartsWith("Btn"))
                .ToArray();
            CollectionAssert.IsSubsetOf(expected, actual);
            foreach (string buttonName in expected)
            {
                var rect = ui.ButtonGroup.Find(buttonName) as RectTransform;
                Assert.That(rect, Is.Not.Null);
                Assert.That(rect.GetComponent<Button>(), Is.Not.Null);
                Assert.That(rect.sizeDelta.y, Is.GreaterThanOrEqualTo(44f));
            }
        }

        [Test]
        public void OnlineLoginPasswordInputMatchesTestStageContract()
        {
            GameObject onlinePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Online/OnlineLogin.prefab");
            Assert.That(onlinePrefab, Is.Not.Null);
            Assert.That(onlinePrefab.name, Is.EqualTo("OnlineLogin"));
            var onlineUi = onlinePrefab.GetComponent<OnlineLoginUI>();
            Assert.That(onlineUi, Is.Not.Null);
            Assert.That(onlineUi.PasswordInput, Is.Not.Null);
            Assert.That(onlineUi.PasswordInput.characterLimit, Is.EqualTo(128));
            Assert.That(onlineUi.PasswordInput.contentType, Is.EqualTo(TMP_InputField.ContentType.Password));
        }

        [Test]
        public void OnlineControllerUsesAccountSystemAndViewLifetimeSubscriptions()
        {
            string source = Read("Client/Assets/Game/Scripts/Controller/UICtrl/Online/OnlineLoginUICtrl.cs");
            Assert.That(source, Does.Contain("GetSystem<OnlineAccountSystem>"));
            Assert.That(source, Does.Contain("UnRegisterWhenGameObjectDestroyed"));
            Assert.That(source, Does.Not.Contain("NetworkRuntime"));
            Assert.That(source, Does.Not.Contain("PasswordInput.text = password"));
        }

        [Test]
        public void OnlineLocalizationKeysResolveInBothLanguagesAndGeneratedTexts()
        {
            string zh = Read("Client/Assets/Game/Config/Localization/zh-CN.json");
            string en = Read("Client/Assets/Game/Config/Localization/en-US.json");
            foreach (string key in new[]
            {
                "ui.start.online",
                "ui.online.error.username",
                "ui.online.error.password",
                "ui.online.error.config",
                "ui.online.error.connect_failed",
                "ui.online.error.connect_timeout",
                "ui.online.error.request_timeout",
                "ui.online.error.username_exists",
                "ui.online.error.account_or_password",
                "ui.online.error.service_unavailable",
                "ui.online.error.register_login_failed",
                "ui.online.error.disconnected",
                "ui.online.error.session_replaced",
                "ui.online.error.preference_save",
                "ui.online.error.password_wrong",
                "ui.online.account_created"
            })
            {
                Assert.That(zh, Does.Contain(key));
                Assert.That(en, Does.Contain(key));
            }

            string generated = Read("Client/Assets/Game/Scripts/Utility/Localization/TextsGenerated.cs");
            Assert.That(generated, Does.Contain("StartOnline"));
            Assert.That(generated, Does.Contain("OnlineErrorUsername"));
            Assert.That(generated, Does.Contain("OnlineErrorPassword"));
            Assert.That(generated, Does.Contain("OnlineAccountCreated"));
            Assert.That(generated, Does.Contain("OnlineErrorPasswordWrong"));
            // 通过运行时 LocalizationUtility 切换语言并核对实际文本
            ResourceLoader.SetLoaderForTests(new LocalizationTestLoader());
            LocalizationLanguages.ResetForTests();
            var utility = new LocalizationUtility();

            // 中文验证
            string zhCreated = utility.Translate("zh-CN", "ui.online.account_created");
            string zhWrong = utility.Translate("zh-CN", "ui.online.error.password_wrong");
            Assert.That(zhCreated, Is.EqualTo("新账号已创建并登录"));
            Assert.That(zhWrong, Is.EqualTo("密码错误"));
            bool zhCreatedHasChinese = zhCreated.Any(c => c >= '\u4e00' && c <= '\u9fff');
            Assert.That(zhCreatedHasChinese, Is.True, "AccountCreated zh should contain Chinese");
            bool zhWrongHasChinese = zhWrong.Any(c => c >= '\u4e00' && c <= '\u9fff');
            Assert.That(zhWrongHasChinese, Is.True, "PasswordWrong zh should contain Chinese");

            // 英文验证
            string enCreated = utility.Translate("en-US", "ui.online.account_created");
            string enWrong = utility.Translate("en-US", "ui.online.error.password_wrong");
            Assert.That(enCreated, Is.EqualTo("New account created and signed in"));
            Assert.That(enWrong, Is.EqualTo("Password is incorrect"));
            bool enCreatedHasEnglish = enCreated.Any(c => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z');
            Assert.That(enCreatedHasEnglish, Is.True, "AccountCreated en should contain English");
            bool enWrongHasEnglish = enWrong.Any(c => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z');
            Assert.That(enWrongHasEnglish, Is.True, "PasswordWrong en should contain English");

            // 中英文值不应相同
            Assert.That(zhCreated, Is.Not.EqualTo(enCreated), "AccountCreated zh vs en should differ");
            Assert.That(zhWrong, Is.Not.EqualTo(enWrong), "PasswordWrong zh vs en should differ");
        }

        [UnityTest]
        public IEnumerator OnlineLoginButton_UsesSingleLoginEntry()
        {
            ResourceLoader.SetLoaderForTests(new LocalizationTestLoader());
            LocalizationLanguages.ResetForTests();
            string prefabPath = "Assets/Game/Prefabs/UI/Online/OnlineLogin.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"Prefab not found at {prefabPath}");
            var observedInstance = UnityEngine.Object.Instantiate(prefab);
            Assert.That(observedInstance.GetComponent<OnlineLoginUI>(), Is.Not.Null, "Prefab instance should expose OnlineLoginUI");
            UnityEngine.Object.DestroyImmediate(observedInstance);

            EnsureUiRootForTest();
            TestReset.ResetArchitecture();
            var network = new FakeOnlineNetwork();
            var config = new FakeNetworkConfigProvider();
            var preferences = new FakeOnlineAccountPreferenceStore();

            var game = new global::Game.Game();
            var accountModel = new OnlineAccountModel();
            var localizationModel = new LocalizationModel();
            var accountSystem = new OnlineAccountSystem();
            game.RegisterModel(localizationModel);
            game.RegisterModel(accountModel);
            game.RegisterUtility<INetworkRuntime>(network);
            game.RegisterUtility<INetworkConfigProvider>(config);
            game.RegisterUtility<IOnlineAccountPreferenceStore>(preferences);
            game.RegisterUtility(CreateTestLocalizationUtility());
            game.RegisterSystem(accountSystem);
            var field = typeof(Architecture<global::Game.Game>).GetField("mArchitecture", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            field?.SetValue(null, game);
            ((ICanInit)localizationModel).Init();
            ((ICanInit)localizationModel).Initialized = true;
            ((ICanInit)accountModel).Init();
            ((ICanInit)accountModel).Initialized = true;
            ((ICanInit)accountSystem).Init();
            ((ICanInit)accountSystem).Initialized = true;

            UIHelper.SetPrefabLoaderForTests(path =>
                path == "Prefabs/UI/Online/OnlineLogin"
                    ? prefab
                    : AssetDatabase.LoadAssetAtPath<GameObject>(string.Format("Assets/Game/{0}.prefab", path)));
            try
            {
                using (Log.CaptureForTests((_, _) => { }))
                {
                    var loginCtrl = UIHelper.ShowUI<OnlineLoginUICtrl>();
                    Assert.That(loginCtrl, Is.Not.Null, "OnlineLoginUICtrl should be created through UIHelper");
                    var loginUi = UnityEngine.Object.FindObjectOfType<OnlineLoginUI>();
                    Assert.That(loginUi, Is.Not.Null, "OnlineLoginUI not created");
                    Assert.That(loginUi.LoginButton, Is.Not.Null, "LoginButton not found");

                    loginUi.UserNameInput.text = "review_user";
                    loginUi.PasswordInput.text = "Password-123";

                    loginUi.LoginButton.onClick.Invoke();
                    yield return WaitUntil(() => network.PendingCallCount == 1);

                    Assert.That(network.PendingCallCount, Is.EqualTo(1), "onClick should trigger one request");
                    Assert.That(network.PendingRequestTypes.Single(), Is.EqualTo(nameof(C2G_Login)));
                    network.CompleteNextLoginError(200104);
                    yield return WaitUntil(() => UnityEngine.Object.FindObjectOfType<MessageUI>() != null);
                    Assert.That(loginUi.PasswordInput.text, Is.Empty);
                    Assert.That(network.PendingCallCount, Is.Zero, "未确认前不能发送注册");
                    var message = UnityEngine.Object.FindObjectOfType<MessageUI>();
                    var confirm = message.GetComponentsInChildren<MessageButtonBoxUI>(true)
                        .Single(button => button.gameObject.activeInHierarchy && button.ButtonText.text == Loc.T("ui.online.register"));
                    confirm.BtnButton.onClick.Invoke();
                    yield return WaitUntil(() => network.PendingCallCount == 1);
                    Assert.That(network.PendingRequestTypes.Single(), Is.EqualTo(nameof(C2G_Register)));
                    network.CompleteNextRegisterSuccess();
                    yield return WaitUntil(() => network.PendingCallCount == 1);
                    network.CompleteNextLoginSuccess("review_user");
                    yield return WaitUntil(() => accountSystem.Snapshot.IsAuthenticated);
                    Assert.That(network.ConnectCount, Is.EqualTo(1), "显式注册后同连接登录");
                    Assert.That(loginUi.PasswordInput.text, Is.Empty);

                }
            }
            finally
            {
                UIHelper.HideUI<OnlineLoginUICtrl>();
                UIHelper.ResetForTests();
                TestReset.ResetArchitecture();
                ResetTestOnlineArchitecture();
                Architecture<Game>.OnRegisterPatch = _ => { };
            }
        }


        [Test]
        public void OnlineLoginPrefab_HasSingleSubmitEntry()
        {
            string prefabPath = "Assets/Game/Prefabs/UI/Online/OnlineLogin.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"Prefab not found at {prefabPath}");

            // 登录按钮存在且无注册按钮
            var loginBtn = RecursiveFind(prefab.transform, "LoginButton");
            Assert.That(loginBtn, Is.Not.Null, "LoginButton not found in Prefab");
            var registerBtn = RecursiveFind(prefab.transform, "RegisterButton");
            Assert.That(registerBtn, Is.Null, "RegisterButton should not exist in Prefab");

            // 按钮布局：居中且宽度覆盖原双按钮区域
            var loginRect = loginBtn.GetComponent<RectTransform>();
            Assert.That(loginRect, Is.Not.Null);
            Assert.That(loginRect.anchoredPosition.x, Is.EqualTo(0f).Within(0.01f), "LoginButton should be centered");
            Assert.That(loginRect.anchorMin.x, Is.EqualTo(0f));
            Assert.That(loginRect.anchorMax.x, Is.EqualTo(1f));
            Assert.That(loginRect.sizeDelta.x, Is.EqualTo(0f), "登录按钮沿用已确认的父容器横向拉伸");
            Assert.That(loginRect.sizeDelta.y, Is.EqualTo(64f).Within(0.01f));

            // 按钮组件存在
            var button = loginBtn.GetComponent<Button>();
            Assert.That(button, Is.Not.Null, "LoginButton should have Button component");


            // 文本子对象存在
            var textChild = loginBtn.Find("Text");
            Assert.That(textChild, Is.Not.Null, "LoginButton should have a Text child");
            var tmpText = textChild.GetComponent<TMP_Text>();
            Assert.That(tmpText, Is.Not.Null, "Text child should have TMP_Text component");
            Assert.That(tmpText.text, Is.EqualTo("登录"), "Button text should keep the current zh-CN authoring value");

            var loginUi = prefab.GetComponent<OnlineLoginUI>();
            Assert.That(loginUi, Is.Not.Null, "Prefab should bind OnlineLoginUI");
            Assert.That(loginUi.UserNameInput, Is.Not.Null, "UserNameInput reference should be valid");
            Assert.That(loginUi.PasswordInput, Is.Not.Null, "PasswordInput reference should be valid");
            Assert.That(loginUi.ResultText, Is.Not.Null, "ResultText reference should be valid");
            Assert.That(loginUi.LoginButton, Is.SameAs(button), "LoginButton reference should target the serialized button");
            var localizedText = tmpText.GetComponent<LocalizedText>();
            Assert.That(localizedText, Is.Not.Null, "LoginButton text should own LocalizedText");
            Assert.That(localizedText.Key, Is.EqualTo("ui.online.login"), "LoginButton text should use login localization key");
            Assert.That(localizedText.Texts, Is.Empty, "LoginButton text should use key mode");
        }

        [UnityTest]
        public IEnumerator LoginResultPresentation_CoversNewExistingAndPreferenceFailure()
        {
            var harness = CreateOnlineHarness();

            // New account login
            Task<OnlineAccountOperationResult> newLogin = harness.System.LoginAsync("new_user", "pass123");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            Assert.That(harness.Network.PendingRequestTypes.Single(), Is.EqualTo(nameof(C2G_Login)));
            harness.Network.CompleteNextLoginNewAccount("new_user");
            yield return WaitUntilCompleted(newLogin);
            Assert.That(newLogin.Result.Code, Is.EqualTo(OnlineAccountResultCode.AccountCreated));
            Assert.That(newLogin.Result.Success, Is.True);
            Assert.That(harness.System.Snapshot.IsNewAccount, Is.True);

            // Logout for next test
            Task<OnlineAccountOperationResult> logout = harness.System.LogoutAsync();
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            harness.Network.CompleteNextLogoutSuccess();
            yield return WaitUntilCompleted(logout);

            // Existing account login
            Task<OnlineAccountOperationResult> existLogin = harness.System.LoginAsync("exist_user", "pass123");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            Assert.That(harness.Network.PendingRequestTypes.Single(), Is.EqualTo(nameof(C2G_Login)));
            harness.Network.CompleteNextLoginSuccess("exist_user");
            yield return WaitUntilCompleted(existLogin);
            Assert.That(existLogin.Result.Code, Is.EqualTo(OnlineAccountResultCode.Success));
            Assert.That(harness.System.Snapshot.IsNewAccount, Is.False);

            // Logout for preference test
            logout = harness.System.LogoutAsync();
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            harness.Network.CompleteNextLogoutSuccess();
            yield return WaitUntilCompleted(logout);

            // Preference save failure
            harness.Preferences.SaveResult = false;
            Task<OnlineAccountOperationResult> prefLogin = harness.System.LoginAsync("pref_user", "pass123");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            Assert.That(harness.Network.PendingRequestTypes.Single(), Is.EqualTo(nameof(C2G_Login)));
            harness.Network.CompleteNextLoginNewAccount("pref_user");
            yield return WaitUntilCompleted(prefLogin);
            Assert.That(prefLogin.Result.Code, Is.EqualTo(OnlineAccountResultCode.PreferenceSaveFailed));
            Assert.That(prefLogin.Result.Success, Is.True);
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Authenticated));
        }

        [Test]
        public void LoginValidation_PreservesCurrentRules()
        {
            Assert.That(OnlineAccountSystem.ValidateInput("abc", ""), Is.EqualTo(OnlineAccountResultCode.Success));
            Assert.That(OnlineAccountSystem.ValidateInput("abc", "A"), Is.EqualTo(OnlineAccountResultCode.Success));
            Assert.That(OnlineAccountSystem.ValidateInput("abc", "Abc_1234"), Is.EqualTo(OnlineAccountResultCode.Success));
            Assert.That(OnlineAccountSystem.ValidateInput("  Abc_123  ", "A"), Is.EqualTo(OnlineAccountResultCode.Success));
            Assert.That(OnlineAccountSystem.ValidateInput("ABCDEFGHIJKLMNOPQRST", "A"), Is.EqualTo(OnlineAccountResultCode.Success));

            Assert.That(OnlineAccountSystem.ValidateInput("ab", "A"), Is.EqualTo(OnlineAccountResultCode.InvalidUserName));
            Assert.That(OnlineAccountSystem.ValidateInput(new string('A', 33), "A"), Is.EqualTo(OnlineAccountResultCode.InvalidUserName));
            Assert.That(OnlineAccountSystem.ValidateInput("abc-def", "A"), Is.EqualTo(OnlineAccountResultCode.InvalidUserName));
            Assert.That(OnlineAccountSystem.ValidateInput("中文名", "A"), Is.EqualTo(OnlineAccountResultCode.InvalidUserName));

            Assert.That(OnlineAccountSystem.ValidateInput("valid_user", "123456789"), Is.EqualTo(OnlineAccountResultCode.InvalidPassword));
            Assert.That(OnlineAccountSystem.ValidateInput("valid_user", "abc def"), Is.EqualTo(OnlineAccountResultCode.InvalidPassword));
            Assert.That(OnlineAccountSystem.ValidateInput("valid_user", "密码"), Is.EqualTo(OnlineAccountResultCode.InvalidPassword));
            Assert.That(OnlineAccountSystem.ValidateInput("valid_user", "abc-def"), Is.EqualTo(OnlineAccountResultCode.InvalidPassword));

            // Server-side error mapping preserves existing codes
            Assert.That(OnlineAccountSystem.MapServerError(200101), Is.EqualTo(OnlineAccountResultCode.InvalidUserName));
            Assert.That(OnlineAccountSystem.MapServerError(200102), Is.EqualTo(OnlineAccountResultCode.InvalidPassword));
            Assert.That(OnlineAccountSystem.MapServerError(200103), Is.EqualTo(OnlineAccountResultCode.UserNameExists));
            Assert.That(OnlineAccountSystem.MapServerError(200104), Is.EqualTo(OnlineAccountResultCode.PasswordWrong));
            Assert.That(OnlineAccountSystem.MapServerError(200105), Is.EqualTo(OnlineAccountResultCode.NotLoggedIn));
            Assert.That(OnlineAccountSystem.MapServerError(200106), Is.EqualTo(OnlineAccountResultCode.SessionReplaced));
            Assert.That(OnlineAccountSystem.MapServerError(200107), Is.EqualTo(OnlineAccountResultCode.ServiceUnavailable));
            Assert.That(OnlineAccountSystem.MapServerError(200108), Is.EqualTo(OnlineAccountResultCode.SessionConflict));
        }

        [UnityTest]
        public IEnumerator LoginLifecycleRegression_PreservesExistingFailuresAndStaleCompletion()
        {
            var harness = CreateOnlineHarness();

            // Session replaced preserved
            Task<OnlineAccountOperationResult> login = harness.System.LoginAsync("review_user", "pass");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            Assert.That(harness.Network.PendingRequestTypes.Single(), Is.EqualTo(nameof(C2G_Login)));
            harness.Network.CompleteNextLoginSuccess("review_user");
            yield return WaitUntilCompleted(login);
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Authenticated));

            harness.Network.RaiseSessionReplaced();
            Assert.That(harness.System.Snapshot.ConnectionState, Is.EqualTo(OnlineConnectionState.Disconnected));
            Assert.That(harness.System.Snapshot.LastResultCode, Is.EqualTo(OnlineAccountResultCode.SessionReplaced));

            // Stale login completion doesn't override new session
            Task<OnlineAccountOperationResult> oldLogin = harness.System.LoginAsync("old_user", "pass");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            Assert.That(harness.Network.PendingRequestTypes.Single(), Is.EqualTo(nameof(C2G_Login)));
            harness.System.CloseAnonymousSession();
            Task<OnlineAccountOperationResult> newLogin = harness.System.LoginAsync("new_user", "pass");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 2);
            Assert.That(harness.Network.PendingRequestTypes, Is.All.EqualTo(nameof(C2G_Login)));
            harness.Network.CompleteLoginForUser("new_user");
            yield return WaitUntilCompleted(newLogin);
            Assert.That(harness.System.Snapshot.UserName, Is.EqualTo("new_user"));

            harness.Network.CompleteLoginForUser("old_user");
            yield return WaitUntilCompleted(oldLogin);
            Assert.That(harness.System.Snapshot.UserName, Is.EqualTo("new_user"));

            // Wrong password preserved as PasswordWrong
            var logoutPass = harness.System.LogoutAsync();
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            harness.Network.CompleteNextLogoutSuccess();
            yield return WaitUntilCompleted(logoutPass);

            Task<OnlineAccountOperationResult> wrongPass = harness.System.LoginAsync("review_user", "wrong");
            yield return WaitUntil(() => harness.Network.PendingCallCount == 1);
            Assert.That(harness.Network.PendingRequestTypes.Single(), Is.EqualTo(nameof(C2G_Login)));
            harness.Network.CompleteNextLoginError(200104);
            yield return WaitUntilCompleted(wrongPass);
            Assert.That(wrongPass.Result.Code, Is.EqualTo(OnlineAccountResultCode.PasswordWrong));
            Assert.That(wrongPass.Result.Success, Is.False);
        }

        [Test]
        public void OnlineResultText_ResolvesSpecificPlayerFacingErrorsInBothLanguages()
        {
            TestReset.ResetArchitecture();
            var game = new global::Game.Game();
            var localizationModel = new LocalizationModel();
            game.RegisterModel(localizationModel);
            game.RegisterModel(new DisplaySettingsModel());
            game.RegisterModel(new AudioSettingsModel());
            game.RegisterModel(new HotKeyModel());
            game.RegisterUtility(CreateTestLocalizationUtility());
            game.RegisterUtility<ISettingsFileStore>(new FakeSettingsFileStore());
            game.RegisterSystem(new SettingsSystem());
            var field = typeof(Architecture<global::Game.Game>).GetField("mArchitecture", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            field?.SetValue(null, game);
            ((ICanInit)localizationModel).Init();
            ((ICanInit)localizationModel).Initialized = true;

            string originalLanguage = localizationModel.CurrentLanguageCode.Value;
            using (Log.CaptureForTests((_, _) => { }))
            {
                try
                {
                    Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "zh-CN" });
                    string zhCreated = OnlineAccountText.Result(OnlineAccountResultCode.AccountCreated);
                    string zhWrong = OnlineAccountText.Result(OnlineAccountResultCode.PasswordWrong);
                    Assert.That(zhCreated, Is.EqualTo("新账号已创建并登录"));
                    Assert.That(zhWrong, Is.EqualTo("密码错误"));
                    Assert.That(OnlineRoomText.Result(OnlineRoomResultCode.PurposeMismatch),
                        Is.EqualTo("房间类型不匹配，请从相同玩法入口加入"));
                    Assert.That(OnlineRoomText.Result(OnlineRoomResultCode.VersionMismatch),
                        Is.EqualTo("客户端版本或玩法配置不一致"));

                    Assert.That(ArcadeUiFormatter.OnlineResult(OnlineArcadeResultCode.SaveFailed),
                        Is.EqualTo("街机进度保存失败，请重试"));
                    Assert.That(ArcadeUiFormatter.OnlineResult(OnlineArcadeResultCode.OperationConflict),
                        Is.EqualTo("街机状态已变化，请重试"));

                    Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });
                    string enCreated = OnlineAccountText.Result(OnlineAccountResultCode.AccountCreated);
                    string enWrong = OnlineAccountText.Result(OnlineAccountResultCode.PasswordWrong);
                    Assert.That(enCreated, Is.EqualTo("New account created and signed in"));
                    Assert.That(enWrong, Is.EqualTo("Password is incorrect"));
                    Assert.That(OnlineRoomText.Result(OnlineRoomResultCode.PurposeMismatch),
                        Is.EqualTo("Room type mismatch. Join from the same game mode entry"));
                    Assert.That(OnlineRoomText.Result(OnlineRoomResultCode.VersionMismatch),
                        Is.EqualTo("Client version or gameplay configuration does not match"));

                    Assert.That(ArcadeUiFormatter.OnlineResult(OnlineArcadeResultCode.SaveFailed),
                        Is.EqualTo("Failed to save arcade progress. Please retry"));
                    Assert.That(ArcadeUiFormatter.OnlineResult(OnlineArcadeResultCode.OperationConflict),
                        Is.EqualTo("Arcade state changed. Please retry"));
                }
                finally
                {
                    try { Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = originalLanguage }); } catch { }
                    TestReset.ResetArchitecture();
                }
            }
        }


        [Test]
        public void OnlineLocalBindsWildcardAndLauncherContractStaysUnchanged()
        {
            string csv = Read("Config/DataCsv/server_start_machine.csv");
            Assert.That(csv, Does.Contain("OnlineLocal:1,OnlineLocal,1,0.0.0.0,127.0.0.1"));
            string launcher = Read("Server/GameServer/Tools/start-local-online-server.ps1");
            Assert.That(launcher, Does.Contain("--Console=1"));
            Assert.That(launcher, Does.Contain("127.0.0.1:${Port}"));
            Assert.That(launcher, Does.Contain("按 R 重启"));
        }

        [Test]
        public void ServerLoginSuccessLogIsSanitizedAndVisible()
        {
            string source = Read("Server/GameServer/Runtime/Hotfix/Server/Online/Gate/C2G_LoginHandler.cs");
            Assert.That(source, Does.Contain("player login success playerId="));
            Assert.That(source, Does.Contain("Log.Info(successLog)"));
            Assert.That(source, Does.Not.Contain("System.Console.WriteLine(successLog)"));
            string successLine = source.Split('\n').First(line => line.Contains("player login success"));
            Assert.That(successLine, Does.Not.Contain("UserName"));
            Assert.That(successLine, Does.Not.Contain("Password"));
            Assert.That(successLine, Does.Not.Contain("AccountId"));
        }

        private static NetworkRuntime CreateRuntime(IClientTransport transport, IMessageCodec codec = null)
        {
            return new NetworkRuntime(
                () => new ManualClock(),
                () => transport,
                () => codec ?? new MemoryPackMessageCodec(),
                codec => new RpcMessageDispatcher(codec));
        }

        private static IEnumerator WaitUntilCompleted(Task task, int maxFrames = 120)
        {
            for (int frame = 0; frame < maxFrames && !task.IsCompleted; frame++)
            {
                yield return null;
            }

            Assert.That(task.IsCompleted, Is.True, "Task did not complete within the bounded EditMode wait.");
        }

        private static IEnumerator WaitUntil(Func<bool> condition, int maxFrames = 120)
        {
            for (int frame = 0; frame < maxFrames && !condition(); frame++)
            {
                yield return null;
            }

            Assert.That(condition(), Is.True, "Condition did not become true within the bounded EditMode wait.");
        }

        private static IEnumerator WaitForMilliseconds(int milliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < deadline)
            {
                yield return null;
            }
        }

        private static void AssertTaskSucceeded(Task task)
        {
            if (task.IsFaulted)
            {
                throw task.Exception.GetBaseException();
            }

            Assert.That(task.IsCanceled, Is.False);
        }

        private static string Read(string relative)
        {
            return File.ReadAllText(Path.Combine(WorkspaceRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string WorkspaceRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        }

        private static LocalizationUtility CreateTestLocalizationUtility()
        {
            ResourceLoader.SetLoaderForTests(new LocalizationTestLoader());
            LocalizationLanguages.ResetForTests();
            return new LocalizationUtility();
        }

        private static Transform RecursiveFind(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var result = RecursiveFind(parent.GetChild(i), name);
                if (result != null) return result;
            }
            return null;
        }
        private static OnlineHarness CreateOnlineHarness()
        {
            var network = new FakeOnlineNetwork();
            var config = new FakeNetworkConfigProvider();
            var preferences = new FakeOnlineAccountPreferenceStore();
            ResetTestOnlineArchitecture();
            TestOnlineArchitecture.Network = network;
            TestOnlineArchitecture.Config = config;
            TestOnlineArchitecture.Preferences = preferences;

            return new OnlineHarness(
                TestOnlineArchitecture.Interface.GetSystem<OnlineAccountSystem>(),
                network,
                config,
                preferences);
        }

        private static void ResetTestOnlineArchitecture()
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

            TestOnlineArchitecture.Network = null;
            TestOnlineArchitecture.Config = null;
            TestOnlineArchitecture.Preferences = null;
        }

        private sealed class TestOnlineArchitecture : Architecture<TestOnlineArchitecture>
        {
            public static FakeOnlineNetwork Network;
            public static FakeNetworkConfigProvider Config;
            public static FakeOnlineAccountPreferenceStore Preferences;

            protected override void Init()
            {
                RegisterModel(new OnlineAccountModel());
                RegisterUtility<INetworkRuntime>(Network);
                RegisterUtility<INetworkConfigProvider>(Config);
                RegisterUtility<IOnlineAccountPreferenceStore>(Preferences);
                RegisterSystem(new OnlineAccountSystem());
            }
        }

        private sealed class OnlineHarness
        {
            public OnlineHarness(
                OnlineAccountSystem system,
                FakeOnlineNetwork network,
                FakeNetworkConfigProvider config,
                FakeOnlineAccountPreferenceStore preferences)
            {
                System = system;
                Network = network;
                Config = config;
                Preferences = preferences;
            }

            public OnlineAccountSystem System { get; }
            public FakeOnlineNetwork Network { get; }
            public FakeNetworkConfigProvider Config { get; }
            public FakeOnlineAccountPreferenceStore Preferences { get; }
        }

        private sealed class ManualClock : INetworkClock
        {
            public long ElapsedMilliseconds { get; private set; }
            public long DeltaMilliseconds { get; private set; }
            public void Update()
            {
                DeltaMilliseconds = 16;
                ElapsedMilliseconds += 16;
            }
        }

        private sealed class FakeTransport : IClientTransport
        {
            private readonly Queue<ClientTransportEvent> events = new Queue<ClientTransportEvent>();
            public List<byte[]> SentPackets { get; } = new List<byte[]>();
            public bool IsConnected { get; private set; }
            public int ConnectionGeneration { get; private set; }
            public int ConnectCalls { get; private set; }
            public bool DelayConnect { get; set; }

            public Task ConnectAsync(int generation, NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken)
            {
                ConnectCalls++;
                ConnectionGeneration = generation;
                if (DelayConnect)
                {
                    var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
                    return completion.Task;
                }

                IsConnected = true;
                events.Enqueue(ClientTransportEvent.Connected(generation));
                return Task.CompletedTask;
            }

            public void Send(int generation, byte[] body)
            {
                SentPackets.Add(body);
            }

            public bool TryDequeue(out ClientTransportEvent transportEvent)
            {
                if (events.Count > 0)
                {
                    transportEvent = events.Dequeue();
                    return true;
                }

                transportEvent = default;
                return false;
            }

            public void Enqueue(ClientTransportEvent evt)
            {
                events.Enqueue(evt);
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
                events.Clear();
            }
        }

        private sealed class ThrowingJsonRequest : IRequest
        {
            public int RpcId { get; set; }
            public string ThrowOnRead => throw new InvalidOperationException("request-json-failure");
        }

        private sealed class ThrowingJsonResponse : IResponse
        {
            public int Error { get; set; }
            public string Message { get; set; }
            public int RpcId { get; set; }
            public string ThrowOnRead => throw new InvalidOperationException("response-json-failure");
        }

        private sealed class ThrowingJsonCodec : IMessageCodec
        {
            public ThrowingJsonResponse Response { get; } = new ThrowingJsonResponse();

            public byte[] Encode(IMessage message)
            {
                Assert.That(message, Is.TypeOf<ThrowingJsonRequest>());
                return new byte[] { 1 };
            }

            public DecodedMessage Decode(byte[] packet)
            {
                return new DecodedMessage(2, Response.RpcId, Response);
            }

            public Type GetResponseType(Type requestType)
            {
                Assert.That(requestType, Is.EqualTo(typeof(ThrowingJsonRequest)));
                return typeof(ThrowingJsonResponse);
            }
        }

        private readonly struct CapturedLog
        {
            public CapturedLog(string condition, LogType type)
            {
                Condition = condition;
                Type = type;
            }

            public string Condition { get; }
            public LogType Type { get; }
        }

        private sealed class ManualConnectTransport : IClientTransport
        {
            private readonly Queue<ClientTransportEvent> events = new Queue<ClientTransportEvent>();

            public bool IsConnected { get; private set; }
            public int ConnectionGeneration { get; private set; }

            public Task ConnectAsync(int generation, NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken)
            {
                ConnectionGeneration = generation;
                return Task.CompletedTask;
            }

            public void Send(int generation, byte[] body)
            {
            }

            public bool TryDequeue(out ClientTransportEvent transportEvent)
            {
                if (events.Count > 0)
                {
                    transportEvent = events.Dequeue();
                    return true;
                }

                transportEvent = default;
                return false;
            }

            public void Enqueue(ClientTransportEvent evt)
            {
                if (evt.Kind == ClientTransportEventKind.Connected)
                {
                    IsConnected = true;
                }
                events.Enqueue(evt);
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
                events.Clear();
            }
        }

        private sealed class FakeNetworkConfigProvider : INetworkConfigProvider
        {
            public Task<NetworkConfigResult> LoadAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(NetworkConfigResult.Ok(new NetworkEndpoint("127.0.0.1", 31001)));
            }
        }

        private sealed class FakeOnlineAccountPreferenceStore : IOnlineAccountPreferenceStore
        {
            public bool SaveResult { get; set; } = true;
            public string LastSavedUserName { get; private set; }

            public string LoadLastUserName()
            {
                return string.Empty;
            }

            public bool SaveLastUserName(string userName, out string error)
            {
                LastSavedUserName = userName;
                error = SaveResult ? null : "fake-save-failed";
                return SaveResult;
            }
        }

        private sealed class FakeSettingsFileStore : ISettingsFileStore
        {
            public SettingsData SavedData { get; private set; }

            public SettingsData LoadOrDefault(out SettingsLoadResult result)
            {
                result = SettingsLoadResult.CreatedDefault;
                return SettingsData.CreateDefault();
            }

            public bool Save(SettingsData data, out string error)
            {
                SavedData = data;
                error = null;
                return true;
            }
        }

        private sealed class LocalizationTestLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : UnityEngine.Object
            {
                switch (path)
                {
                    case LocalizationLanguages.LanguagesAddress:
                        return new TextAsset("{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"简体中文\"},{\"code\":\"en-US\",\"label\":\"English\"}]}") as T;
                    case "zh-CN.json":
                    case "en-US.json":
                        return new TextAsset(Read($"Client/Assets/Game/Config/Localization/{path}")) as T;
                    default:
                        return null;
                }
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
            {
                var asset = LoadAsset<T>(path);
                onLoaded?.Invoke(asset);
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }

        private sealed class FakeOnlineNetwork : INetworkRuntime, INetworkResponseObserver
        {
            private readonly List<PendingCall> pendingCalls = new List<PendingCall>();
            private readonly List<Action<NetworkDisconnected>> disconnectedHandlers = new List<Action<NetworkDisconnected>>();
            private readonly List<Action<G2C_SessionReplaced>> sessionReplacedHandlers = new List<Action<G2C_SessionReplaced>>();
            private readonly Dictionary<Type, List<Delegate>> responseHandlers = new Dictionary<Type, List<Delegate>>();
            private int generation;
            private int nextRpcId;

            public bool IsInitialized { get; private set; } = true;
            public NetworkConnectionState ConnectionState { get; private set; } = NetworkConnectionState.Disconnected;
            public int ConnectionGeneration => generation;
            public int PendingCallCount => pendingCalls.Count;
            public IReadOnlyList<string> PendingRequestTypes => pendingCalls.Select(call => call.Request.GetType().Name).ToArray();
            public int ConnectCount { get; private set; }
            public int DisconnectCount { get; private set; }

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
                ConnectCount++;
                ConnectionState = NetworkConnectionState.Connected;
                return Task.CompletedTask;
            }

            public Task<TResponse> CallAsync<TResponse>(IRequest request, int timeoutMilliseconds, CancellationToken cancellationToken)
                where TResponse : class, IResponse
            {
                request.RpcId = ++nextRpcId;
                var call = new PendingCall(request, cancellationToken);
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
                ConnectionState = NetworkConnectionState.Disconnected;
            }

            public void Dispose()
            {
                IsInitialized = false;
                ConnectionState = NetworkConnectionState.Disposed;
                foreach (var call in pendingCalls.ToArray())
                {
                    call.TrySetException(new ObjectDisposedException(nameof(FakeOnlineNetwork)));
                }
                pendingCalls.Clear();
            }

            public void RaiseDisconnected(NetworkDisconnectReason reason)
            {
                ConnectionState = NetworkConnectionState.Disconnected;
                var disconnected = new NetworkDisconnected(generation, reason, reason.ToString());
                foreach (var handler in disconnectedHandlers.ToArray())
                {
                    handler(disconnected);
                }
            }

            public void RaiseSessionReplaced()
            {
                var message = G2C_SessionReplaced.Create();
                message.Error = 200106;
                message.Reason = "review";
                foreach (var handler in sessionReplacedHandlers.ToArray())
                {
                    handler(message);
                }
            }

            public void CompleteNextRegisterSuccess()
            {
                var call = TakeNext<C2G_Register>();
                Assert.That(call.Request, Is.TypeOf<C2G_Register>());
                var response = G2C_Register.Create();
                response.Error = 0;
                response.AccountId = 17;
                call.TrySetResult(response);
            }

            public void CompleteNextLoginSuccess(string userName)
            {
                var call = TakeNext<C2G_Login>();
                Assert.That(call.Request, Is.TypeOf<C2G_Login>());
                var response = G2C_Login.Create();
                response.Error = 0;
                response.AccountId = 17;
                response.PlayerId = 23;
                response.SessionEpoch = 29;
                response.IsPlayerActorReused = true;
                response.RpcId = call.Request.RpcId;
                NotifyResponse(response);
                call.TrySetResult(response);
            }

            public void CompleteLoginForUser(string userName)
            {
                var call = TakeLoginForUser(userName);
                var response = G2C_Login.Create();
                response.Error = 0;
                response.AccountId = 17;
                response.PlayerId = 23;
                response.SessionEpoch = 29;
                response.IsPlayerActorReused = true;
                response.RpcId = call.Request.RpcId;
                NotifyResponse(response);
                call.TrySetResult(response);
            }

            public void CompleteNextLoginNewAccount(string userName)
            {
                var call = TakeNext<C2G_Login>();
                Assert.That(call.Request, Is.TypeOf<C2G_Login>());
                var response = G2C_Login.Create();
                response.Error = 0;
                response.AccountId = 17;
                response.PlayerId = 23;
                response.SessionEpoch = 29;
                response.IsPlayerActorReused = false;
                response.IsNewAccount = true;
                response.RpcId = call.Request.RpcId;
                NotifyResponse(response);
                call.TrySetResult(response);
            }

            public void CompleteNextLoginError(int error)
            {
                var call = TakeNext<C2G_Login>();
                var response = G2C_Login.Create();
                response.Error = error;
                response.RpcId = call.Request.RpcId;
                NotifyResponse(response);
                call.TrySetResult(response);
            }

            public void CompleteNextLogoutSuccess()
            {
                var call = TakeNext<C2G_Logout>();
                Assert.That(call.Request, Is.TypeOf<C2G_Logout>());
                var response = G2C_Logout.Create();
                response.Error = 0;
                response.RpcId = call.Request.RpcId;
                NotifyResponse(response);
                call.TrySetResult(response);
            }

            public void FailNextLogout(Exception exception)
            {
                var call = TakeNext<C2G_Logout>();
                Assert.That(call.Request, Is.TypeOf<C2G_Logout>());
                call.TrySetException(exception);
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

            private PendingCall TakeNext<TRequest>()
                where TRequest : class, IRequest
            {
                int index = pendingCalls.FindIndex(call => call.Request is TRequest);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), $"No pending {typeof(TRequest).Name} call.");
                PendingCall call = pendingCalls[index];
                pendingCalls.RemoveAt(index);
                return call;
            }

            private PendingCall TakeLoginForUser(string userName)
            {
                int index = pendingCalls.FindIndex(call => call.Request is C2G_Login login && login.UserName == userName);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), $"No pending login call for {userName}.");
                PendingCall call = pendingCalls[index];
                pendingCalls.RemoveAt(index);
                return call;
            }

            private sealed class PendingCall
            {
                private readonly TaskCompletionSource<IResponse> completion =
                    new TaskCompletionSource<IResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

                public PendingCall(IRequest request, CancellationToken cancellationToken)
                {
                    Request = request;
                }

                public IRequest Request { get; }
                public Task<IResponse> Task => completion.Task;

                public void TrySetResult(IResponse response)
                {
                    completion.TrySetResult(response);
                }

                public void TrySetException(Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }
        }

        private static void EnsureUiRootForTest()
        {
            GameObject root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(UIRoot));
            new GameObject("HUD", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("Dialog", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("Popup", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("Tips", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("Top", typeof(RectTransform)).transform.SetParent(root.transform, false);
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            eventSystem.transform.SetParent(root.transform, false);
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
