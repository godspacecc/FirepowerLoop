using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ET;
using Game;
using Game.Network;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;

namespace FirepowerLoop.Tests.EditMode.Flow
{
    public sealed class OnlineRoomChatTests
    {
        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
        }

        [TestCase(" hello ", "hello", true)]
        [TestCase("\r\n", "", false)]
        [TestCase("hello\nworld", "hello\nworld", false)]
        public void TextContract_NormalizesAndRejectsControlCharacters(string input, string expected, bool valid)
        {
            Assert.That(OnlineRoomChatSystem.TryNormalizeContent(input, out string normalized), Is.EqualTo(valid));
            Assert.That(normalized, Is.EqualTo(expected));
        }

        [Test]
        public void TextContract_UsesVisibleElementsAndUtf8Limit()
        {
            Assert.That(OnlineRoomChatSystem.TryNormalizeContent(new string('a', 200), out _), Is.True);
            Assert.That(OnlineRoomChatSystem.TryNormalizeContent(new string('a', 201), out _), Is.False);
            Assert.That(OnlineRoomChatSystem.TryNormalizeContent(new string('界', 200), out _), Is.True);
            Assert.That(OnlineRoomChatSystem.TryNormalizeContent(string.Concat(Enumerable.Repeat("😀", 200)), out _), Is.True);
            Assert.That(OnlineRoomChatSystem.TryNormalizeContent(string.Concat(Enumerable.Repeat("😀", 201)), out _), Is.False);
        }

        [Test]
        public void OrdinaryDisconnectPreservesRoomAndMessages_ButSessionReplacementClearsThem()
        {
            ChatHarness harness = CreateHarness();
            harness.PublishAuthenticatedRoom(500);
            harness.Network.RaiseMessage(500, Message("m1", 1, "one"));

            harness.Account.Publish(new OnlineAccountSnapshot(OnlineConnectionState.Disconnected,
                OnlineOperationState.Idle, OnlineAccountResultCode.Disconnected, string.Empty,
                0, 0, 0, false, "owner"));

            Assert.That(harness.System.Snapshot.RoomId, Is.EqualTo(500));
            Assert.That(harness.System.Snapshot.Messages.Count, Is.EqualTo(1));
            Assert.That(harness.System.Snapshot.ConnectionLost, Is.True);

            harness.Account.Publish(new OnlineAccountSnapshot(OnlineConnectionState.Disconnected,
                OnlineOperationState.Idle, OnlineAccountResultCode.SessionReplaced, string.Empty,
                0, 0, 0, false, "owner"));

            Assert.That(harness.System.Snapshot.RoomId, Is.Zero);
            Assert.That(harness.System.Snapshot.Messages, Is.Empty);
        }

        [UnityTest]
        public IEnumerator HistoryAndPush_AreMergedByStableIdAndSequence()
        {
            ChatHarness harness = CreateHarness();
            harness.PublishAuthenticatedRoom(500);
            PendingCall history = harness.Network.TakeNext<C2G_GetRoomChatHistory>();

            harness.Network.RaiseMessage(500, Message("m2", 2, "two"));
            var response = G2C_GetRoomChatHistory.Create();
            response.Error = 0;
            response.RoomId = 500;
            response.MaxChatSequence = 2;
            response.Messages.Add(Message("m1", 1, "one"));
            response.Messages.Add(Message("m2", 2, "two"));
            history.Complete(response);
            yield return WaitUntil(() => harness.System.Snapshot.HistoryState == OnlineRoomChatHistoryState.Ready);

            Assert.That(harness.System.Snapshot.HistoryState, Is.EqualTo(OnlineRoomChatHistoryState.Ready));
            Assert.That(harness.System.Snapshot.Messages.Count, Is.EqualTo(2));
            Assert.That(harness.System.Snapshot.Messages[0].ChatMessageId, Is.EqualTo("m1"));
            Assert.That(harness.System.Snapshot.Messages[1].ChatMessageId, Is.EqualTo("m2"));
        }

        [UnityTest]
        public IEnumerator TimedOutSend_RetriesSameClientMessageId_AndClearsOnlyAfterAcceptance()
        {
            ChatHarness harness = CreateHarness();
            harness.PublishAuthenticatedRoom(500);
            PendingCall history = harness.Network.TakeNext<C2G_GetRoomChatHistory>();
            var historyResponse = G2C_GetRoomChatHistory.Create();
            historyResponse.Error = 0;
            historyResponse.RoomId = 500;
            history.Complete(historyResponse);
            yield return WaitUntil(() => harness.System.Snapshot.HistoryState == OnlineRoomChatHistoryState.Ready);

            harness.Network.ImmediateException = new TimeoutException();
            Task<OnlineRoomChatSendResult> timeoutTask = harness.System.SendAsync("retry me");
            yield return WaitUntil(() => timeoutTask.IsCompleted);
            OnlineRoomChatSendResult timeout = timeoutTask.GetAwaiter().GetResult();
            string firstId = ((C2G_SendRoomChat)harness.Network.LastImmediateRequest).ClientMessageId;
            Assert.That(timeout.Code, Is.EqualTo(OnlineRoomChatResultCode.Timeout));
            Assert.That(timeout.ShouldClearInput, Is.False);
            Assert.That(harness.System.Snapshot.PendingContent, Is.EqualTo("retry me"));

            Task<OnlineRoomChatSendResult> retryTask = harness.System.SendAsync("retry me");
            PendingCall retry = harness.Network.TakeNext<C2G_SendRoomChat>();
            Assert.That(((C2G_SendRoomChat)retry.Request).ClientMessageId, Is.EqualTo(firstId));
            var accepted = G2C_SendRoomChat.Create();
            accepted.Error = 0;
            accepted.Accepted = true;
            accepted.RoomId = 500;
            accepted.ChatMessage = Message("server-1", 1, "retry me");
            retry.Complete(accepted);
            yield return WaitUntil(() => retryTask.IsCompleted);
            OnlineRoomChatSendResult result = retryTask.GetAwaiter().GetResult();

            Assert.That(result.Code, Is.EqualTo(OnlineRoomChatResultCode.Success));
            Assert.That(result.ShouldClearInput, Is.True);
            Assert.That(harness.System.Snapshot.PendingContent, Is.Empty);
            Assert.That(harness.System.Snapshot.Messages.Count, Is.EqualTo(1));
        }

        private static IEnumerator WaitUntil(Func<bool> predicate)
        {
            float deadline = Time.realtimeSinceStartup + 1f;
            while (!predicate() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.That(predicate(), Is.True);
        }

        [Test]
        public void Prefabs_ExposeCompleteChatContract()
        {
            GameObject roomPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Online/OnlineRoom.prefab");
            GameObject messagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Online/Box/OnlineRoomChatMessage.prefab");
            Assert.That(roomPrefab, Is.Not.Null);
            Assert.That(messagePrefab, Is.Not.Null);

            OnlineRoomUI ui = roomPrefab.GetComponent<OnlineRoomUI>();
            Assert.That(ui.ChatStateText, Is.Not.Null);
            Assert.That(ui.ChatMessageList, Is.Not.Null);
            Assert.That(ui.ChatMessageList.prefabSource.prefab, Is.EqualTo(messagePrefab));
            Assert.That(ui.ChatMessageList.GetComponent<ScrollRect>(), Is.Null);
            Assert.That(ui.ChatMessageList.content.GetComponent<CustomListView>(), Is.Null);
            Assert.That(ui.ChatInput, Is.Not.Null);
            Assert.That(ui.ChatInput.lineType, Is.EqualTo(TMPro.TMP_InputField.LineType.SingleLine));
            Assert.That(ui.ChatSendButton, Is.Not.Null);

            OnlineRoomChatMessageBoxUI row = messagePrefab.GetComponent<OnlineRoomChatMessageBoxUI>();
            Assert.That(row, Is.Not.Null);
            Assert.That(row.SenderText, Is.Not.Null);
            Assert.That(row.ContentText, Is.Not.Null);
            Assert.That(row.TimeText, Is.Not.Null);
        }

        private static ChatHarness CreateHarness()
        {
            ResetArchitecture();
            TestChatArchitecture.Network = new FakeChatNetwork();
            IArchitecture architecture = TestChatArchitecture.Interface;
            return new ChatHarness(architecture.GetSystem<OnlineRoomChatSystem>(),
                architecture.GetModel<OnlineAccountModel>(), architecture.GetModel<OnlineRoomModel>(), TestChatArchitecture.Network);
        }

        private static void ResetArchitecture()
        {
            FieldInfo field = typeof(Architecture<TestChatArchitecture>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
            if (field?.GetValue(null) is IArchitecture architecture)
            {
                architecture.Deinit();
            }
            field?.SetValue(null, null);
            TestChatArchitecture.Network = null;
        }

        private static RoomChatMessageInfo Message(string id, long sequence, string content)
        {
            RoomChatMessageInfo message = RoomChatMessageInfo.Create();
            message.ChatMessageId = id;
            message.ChatSequence = sequence;
            message.SenderPlayerId = 10;
            message.SenderDisplayName = "owner";
            message.ServerTime = 1000 + sequence;
            message.Content = content;
            return message;
        }

        private sealed class TestChatArchitecture : Architecture<TestChatArchitecture>
        {
            public static FakeChatNetwork Network;

            protected override void Init()
            {
                RegisterModel(new OnlineAccountModel());
                RegisterModel(new OnlineRoomModel());
                RegisterModel(new OnlineRoomChatModel());
                RegisterUtility<INetworkRuntime>(Network);
                RegisterSystem(new OnlineRoomChatSystem());
            }
        }

        private sealed class ChatHarness
        {
            public ChatHarness(OnlineRoomChatSystem system, OnlineAccountModel account, OnlineRoomModel room, FakeChatNetwork network)
            {
                System = system;
                Account = account;
                Room = room;
                Network = network;
            }

            public OnlineRoomChatSystem System { get; }
            public OnlineAccountModel Account { get; }
            public OnlineRoomModel Room { get; }
            public FakeChatNetwork Network { get; }

            public void PublishAuthenticatedRoom(long roomId)
            {
                Account.Publish(new OnlineAccountSnapshot(OnlineConnectionState.Authenticated, OnlineOperationState.Idle,
                    OnlineAccountResultCode.Success, "owner", 1, 10, 1, false, "owner"));
                Room.Publish(new Game.OnlineRoomSnapshot(true, roomId, "ABC234", 10, 1,
                    new[] { new OnlineRoomMemberSnapshot(10, "owner", true, OnlineRoomMemberConnectionState.Online, 1) },
                    false, OnlineRoomOperationState.Idle, OnlineRoomResultCode.Success));
            }
        }

        private sealed class FakeChatNetwork : INetworkRuntime
        {
            private readonly List<PendingCall> calls = new List<PendingCall>();
            private readonly List<Action<G2C_RoomChatMessage>> messageHandlers = new List<Action<G2C_RoomChatMessage>>();
            private readonly List<Action<NetworkDisconnected>> disconnectHandlers = new List<Action<NetworkDisconnected>>();

            public bool IsInitialized { get; private set; } = true;
            public NetworkConnectionState ConnectionState { get; private set; } = NetworkConnectionState.Connected;
            public int ConnectionGeneration => 1;
            public Exception ImmediateException { get; set; }
            public IRequest LastImmediateRequest { get; private set; }

            public void Initialize() => IsInitialized = true;
            public void Update() { }
            public Task ConnectAsync(NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<TResponse> CallAsync<TResponse>(IRequest request, int timeoutMilliseconds, CancellationToken cancellationToken)
                where TResponse : class, IResponse
            {
                if (ImmediateException != null)
                {
                    Exception exception = ImmediateException;
                    ImmediateException = null;
                    LastImmediateRequest = request;
                    return Task.FromException<TResponse>(exception);
                }

                PendingCall call = new PendingCall(request);
                calls.Add(call);
                return call.Task.ContinueWith(task => (TResponse)task.GetAwaiter().GetResult(), CancellationToken.None);
            }

            public IDisposable RegisterPushHandler<TMessage>(Action<TMessage> handler) where TMessage : class, IMessage
            {
                if (typeof(TMessage) == typeof(G2C_RoomChatMessage))
                {
                    Action<G2C_RoomChatMessage> typed = message => handler((TMessage)(object)message);
                    messageHandlers.Add(typed);
                    return new Subscription(() => messageHandlers.Remove(typed));
                }
                return new Subscription(() => { });
            }

            public IDisposable RegisterDisconnectedHandler(Action<NetworkDisconnected> handler)
            {
                disconnectHandlers.Add(handler);
                return new Subscription(() => disconnectHandlers.Remove(handler));
            }

            public void Disconnect(NetworkDisconnectReason reason) => ConnectionState = NetworkConnectionState.Disconnected;
            public void Dispose() => ConnectionState = NetworkConnectionState.Disposed;

            public PendingCall TakeNext<TRequest>() where TRequest : class, IRequest
            {
                int index = calls.FindIndex(call => call.Request is TRequest);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), $"No pending {typeof(TRequest).Name} call.");
                PendingCall call = calls[index];
                calls.RemoveAt(index);
                return call;
            }

            public void RaiseMessage(long roomId, RoomChatMessageInfo message)
            {
                G2C_RoomChatMessage push = G2C_RoomChatMessage.Create();
                push.RoomId = roomId;
                push.ChatMessage = message;
                foreach (Action<G2C_RoomChatMessage> handler in messageHandlers.ToArray())
                {
                    handler(push);
                }
            }
        }

        private sealed class PendingCall
        {
            private readonly TaskCompletionSource<IResponse> source = new TaskCompletionSource<IResponse>();
            public PendingCall(IRequest request) => Request = request;
            public IRequest Request { get; }
            public Task<IResponse> Task => source.Task;
            public void Complete(IResponse response) => source.TrySetResult(response);
        }

        private sealed class Subscription : IDisposable
        {
            private Action dispose;
            public Subscription(Action dispose) => this.dispose = dispose;
            public void Dispose()
            {
                dispose?.Invoke();
                dispose = null;
            }
        }
    }
}
