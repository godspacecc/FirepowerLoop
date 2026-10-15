using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ET;
using Game.Network;
using NUnit.Framework;
using QFramework;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class ServerTestCmdTests
    {
        private FakeNetwork network;
        private CountingSettingsFileStore settingsStore;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            network = new FakeNetwork();
            settingsStore = new CountingSettingsFileStore();
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<INetworkRuntime>(network);
                architecture.RegisterUtility<ISettingsFileStore>(settingsStore);
            };
            network.State = NetworkConnectionState.Disconnected;
            PublishAuthenticated();
            network.State = NetworkConnectionState.Connected;
        }

        [TearDown]
        public void TearDown()
        {
            Architecture<Game>.OnRegisterPatch = _ => { };
            TestReset.ResetArchitecture();
        }

        [Test]
        public void CatalogIsDeepCopiedValidatedAndSorted()
        {
            G2C_GetServerTestCmdCatalog response = G2C_GetServerTestCmdCatalog.Create();
            response.Categories.Add(new ServerTestCmdCategoryInfo { Id = "server.general", Order = 2, LabelText = "服务器" });
            response.Entries.Add(new ServerTestCmdEntryInfo { Id = "server.ping", CategoryId = "server.general", Order = 1,
                LabelText = "Ping", CommandTemplate = "$ping", DescriptionText = "说明", UsageText = "$ping", ExampleText = "$ping" });
            network.Response = response;
            ServerTestCmdCatalogSnapshot snapshot = Game.Interface.GetSystem<ServerTestCmdSystem>()
                .RefreshCatalogAsync(CancellationToken.None).GetAwaiter().GetResult();
            response.Categories[0].Id = "mutated"; response.Entries.Clear();
            Assert.That(snapshot.Status, Is.EqualTo(ServerTestCmdCatalogStatus.Available));
            Assert.That(snapshot.Categories[0].Id, Is.EqualTo("server.general"));
            Assert.That(snapshot.Entries[0].CommandTemplate, Is.EqualTo("$ping"));
        }

        [Test]
        public void ExecuteSendsBodyOnceAndMapsStructuredResult()
        {
            network.Response = new G2C_ExecuteServerTestCmd { Recognized = true, Success = true, ResultCode = 1, Message = "pong", ServerTime = 123 };
            TestCmdExecutionResult result = Game.Interface.GetSystem<TestCmdSystem>()
                .ExecuteAsync("$ping", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(network.CallCount, Is.EqualTo(1));
            Assert.That(((C2G_ExecuteServerTestCmd)network.LastRequest).CommandText, Is.EqualTo("ping"));
            Assert.That(result.Code, Is.EqualTo(TestCmdResultCode.ServerCommandComplete));
            Assert.That(result.ServerTime, Is.EqualTo(123));
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands, Is.EqualTo(new[] { "$ping" }));
        }

        [Test]
        public void DisconnectedNeverCallsNetwork()
        {
            network.State = NetworkConnectionState.Disconnected;
            TestCmdExecutionResult result = Game.Interface.GetSystem<TestCmdSystem>()
                .ExecuteAsync("$ping", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(result.Code, Is.EqualTo(TestCmdResultCode.ServerNotConnected));
            Assert.That(network.CallCount, Is.Zero);
        }

        [Test]
        public void UnauthenticatedServerCommandIsRejectedWithoutNetworkHistoryOrDirtySettings()
        {
            Game.Interface.GetModel<OnlineAccountModel>().Publish(OnlineAccountSnapshot.Empty());
            settingsStore.ResetSaves();

            TestCmdExecutionResult result = Game.Interface.GetSystem<TestCmdSystem>()
                .ExecuteAsync("$ping", CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Recognized, Is.False);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(TestCmdResultCode.ServerNotConnected));
            Assert.That(result.Message, Is.EqualTo("请先登录后使用服务器指令。"));
            Assert.That(network.CallCount, Is.Zero);
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands, Is.Empty);
            Assert.That(Game.Interface.GetModel<TestCmdModel>().LastResult.Code, Is.EqualTo(TestCmdResultCode.ServerNotConnected));
            Assert.That(Game.Interface.GetSystem<SettingsSystem>().Flush(), Is.True);
            Assert.That(settingsStore.SaveCount, Is.Zero);
        }

        [Test]
        public void TimeoutAndUnknownResultAreNotSuccess()
        {
            network.Exception = new TimeoutException();
            Assert.That(Game.Interface.GetSystem<ServerTestCmdSystem>().ExecuteAsync("ping", CancellationToken.None).GetAwaiter().GetResult().Code,
                Is.EqualTo(TestCmdResultCode.ServerTimeout));
            network.Response = new G2C_ExecuteServerTestCmd { Recognized = true, Success = true, ResultCode = 999, Message = "bad" };
            TestCmdExecutionResult unknown = Game.Interface.GetSystem<ServerTestCmdSystem>().ExecuteAsync("ping", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(unknown.Success, Is.False);
            Assert.That(unknown.Code, Is.EqualTo(TestCmdResultCode.ServerExecutionFailed));
        }

        [UnityTest]
        public IEnumerator ActiveCancellationAndInFlightDisconnectMapExplicitly()
        {
            network.UsePending = true;
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            Task<TestCmdExecutionResult> canceledTask = Game.Interface.GetSystem<ServerTestCmdSystem>()
                .ExecuteAsync("ping", cancellation.Token);
            Assert.That(network.PendingCalls.Count, Is.EqualTo(1));
            cancellation.Cancel();
            while (!canceledTask.IsCompleted) yield return null;
            Assert.That(canceledTask.GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.Canceled));

            Task<TestCmdExecutionResult> disconnectedTask = Game.Interface.GetSystem<ServerTestCmdSystem>()
                .ExecuteAsync("ping", CancellationToken.None);
            PendingCall call = network.PendingCalls[1];
            network.State = NetworkConnectionState.Disconnected;
            call.Fail(new InvalidOperationException("transport closed"));
            while (!disconnectedTask.IsCompleted) yield return null;
            Assert.That(disconnectedTask.GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.ServerDisconnected));
        }

        [Test]
        public void CatalogRpcFailureAndInvalidProtocolBecomeUnavailable()
        {
            network.Response = new G2C_GetServerTestCmdCatalog { Error = 1, Message = "failed" };
            Assert.That(Game.Interface.GetSystem<ServerTestCmdSystem>().RefreshCatalogAsync(CancellationToken.None)
                .GetAwaiter().GetResult().Status, Is.EqualTo(ServerTestCmdCatalogStatus.Unavailable));

            G2C_GetServerTestCmdCatalog invalid = G2C_GetServerTestCmdCatalog.Create();
            invalid.Categories.Add(new ServerTestCmdCategoryInfo { Id = "client.invalid", Order = 0, LabelText = "bad" });
            network.Response = invalid;
            Assert.That(Game.Interface.GetSystem<ServerTestCmdSystem>().RefreshCatalogAsync(CancellationToken.None)
                .GetAwaiter().GetResult().Status, Is.EqualTo(ServerTestCmdCatalogStatus.Unavailable));
        }

        [UnityTest]
        public IEnumerator NewCatalogGenerationCancelsOldAndOnlyNewResponseIsAccepted()
        {
            network.UsePending = true;
            ServerTestCmdSystem system = Game.Interface.GetSystem<ServerTestCmdSystem>();
            Task<ServerTestCmdCatalogSnapshot> firstTask = system.RefreshCatalogAsync(CancellationToken.None);
            PendingCall first = network.PendingCalls[0];
            Task<ServerTestCmdCatalogSnapshot> secondTask = system.RefreshCatalogAsync(CancellationToken.None);
            PendingCall second = network.PendingCalls[1];

            G2C_GetServerTestCmdCatalog response = ValidCatalog();
            Assert.That(second.Complete(response), Is.True);
            while (!secondTask.IsCompleted || !firstTask.IsCompleted) yield return null;
            Assert.That(secondTask.GetAwaiter().GetResult().Status, Is.EqualTo(ServerTestCmdCatalogStatus.Available));
            Assert.That(firstTask.GetAwaiter().GetResult().Status, Is.EqualTo(ServerTestCmdCatalogStatus.Canceled));
            Assert.That(first.Complete(ValidCatalog()), Is.False, "canceled old request must reject late completion");
        }

        private static G2C_GetServerTestCmdCatalog ValidCatalog()
        {
            G2C_GetServerTestCmdCatalog response = G2C_GetServerTestCmdCatalog.Create();
            response.Categories.Add(new ServerTestCmdCategoryInfo { Id = "server.general", Order = 0, LabelText = "服务器" });
            response.Entries.Add(new ServerTestCmdEntryInfo { Id = "server.ping", CategoryId = "server.general", Order = 0,
                LabelText = "Ping", CommandTemplate = "$ping", DescriptionText = "说明", UsageText = "$ping", ExampleText = "$ping" });
            return response;
        }

        private static void PublishAuthenticated()
        {
            Game.Interface.GetModel<OnlineAccountModel>().Publish(new OnlineAccountSnapshot(
                OnlineConnectionState.Authenticated,
                OnlineOperationState.Idle,
                OnlineAccountResultCode.Success,
                "test-user",
                1,
                1,
                1,
                false,
                "test-user"));
        }

        private sealed class CountingSettingsFileStore : ISettingsFileStore
        {
            public int SaveCount { get; private set; }

            public SettingsData LoadOrDefault(out SettingsLoadResult result)
            {
                result = SettingsLoadResult.CreatedDefault;
                return SettingsData.CreateDefault();
            }

            public bool Save(SettingsData data, out string error)
            {
                SaveCount++;
                error = null;
                return true;
            }

            public void ResetSaves()
            {
                SaveCount = 0;
            }
        }

        private sealed class FakeNetwork: INetworkRuntime
        {
            public object Response;
            public Exception Exception;
            public IRequest LastRequest;
            public int CallCount;
            public bool UsePending;
            public readonly List<PendingCall> PendingCalls = new List<PendingCall>();
            public NetworkConnectionState State = NetworkConnectionState.Connected;
            public bool IsInitialized => true;
            public NetworkConnectionState ConnectionState => State;
            public int ConnectionGeneration => 1;
            public void Initialize() { }
            public void Update() { }
            public Task ConnectAsync(NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken) => Task.CompletedTask;
            public async Task<TResponse> CallAsync<TResponse>(IRequest request, int timeoutMilliseconds, CancellationToken cancellationToken) where TResponse : class, IResponse
            {
                LastRequest = request; CallCount++;
                if (UsePending)
                {
                    PendingCall call = new PendingCall(request, cancellationToken);
                    PendingCalls.Add(call);
                    return (TResponse)await call.Task;
                }
                if (Exception != null) { Exception value = Exception; Exception = null; throw value; }
                object valueResponse = Response; Response = null;
                return (TResponse)valueResponse;
            }
            public IDisposable RegisterPushHandler<TMessage>(Action<TMessage> handler) where TMessage : class, IMessage => EmptyDisposable.Instance;
            public IDisposable RegisterDisconnectedHandler(Action<NetworkDisconnected> handler) => EmptyDisposable.Instance;
            public void Disconnect(NetworkDisconnectReason reason) => State = NetworkConnectionState.Disconnected;
            public void Dispose() => State = NetworkConnectionState.Disposed;
        }

        private sealed class PendingCall
        {
            private readonly TaskCompletionSource<IResponse> completion =
                new TaskCompletionSource<IResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly CancellationTokenRegistration registration;

            public PendingCall(IRequest request, CancellationToken cancellationToken)
            {
                Request = request;
                registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            }

            public IRequest Request { get; }
            public Task<IResponse> Task => completion.Task;
            public bool Complete(IResponse response) { registration.Dispose(); return completion.TrySetResult(response); }
            public bool Fail(Exception exception) { registration.Dispose(); return completion.TrySetException(exception); }
        }

        private sealed class EmptyDisposable: IDisposable
        {
            public static readonly EmptyDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
