using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ET;
using Game.Network;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class OnlineRoomRowGenerationMigrationTests
    {
        private const string MemberPrefabPath = "Assets/Game/Prefabs/UI/Online/Box/OnlineRoomMember.prefab";
        private const string ChatPrefabPath = "Assets/Game/Prefabs/UI/Online/Box/OnlineRoomChatMessage.prefab";
        private const string RoomPrefabPath = "Assets/Game/Prefabs/UI/Online/OnlineRoom.prefab";

        [SetUp]
        public void SetUp()
        {
            UIHelper.ResetForTests();
            ResetGameArchitecture();

            var localizationLoader = new LocalizationTestLoader();
            ResourceLoader.SetLoaderForTests(localizationLoader);
            Assert.AreSame(localizationLoader, ResourceLoader.Current);
            LocalizationLanguages.ResetForTests();

            EnsureUiRootForTest();
            InstallMinimalGameArchitectureForUiTest();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab"));
        }

        [TearDown]
        public void TearDown()
        {
            UIHelper.ResetForTests();
            DestroyTestSceneObjects();
            ResetGameArchitecture();
            LocalizationLanguages.ResetForTests();
            ResourceLoader.ResetForTests();
        }

        [Test]
        public void RowPrefabsUseBoxPathsCollectorsAndGeneratedViews()
        {
            var roomPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoomPrefabPath);
            var memberPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MemberPrefabPath);
            var chatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChatPrefabPath);
            Assert.NotNull(roomPrefab);
            Assert.NotNull(memberPrefab);
            Assert.NotNull(chatPrefab);

            Assert.That(AssetDatabase.AssetPathToGUID(MemberPrefabPath), Is.EqualTo("1e1e1e5ea8801f64eaf63ce803382c26"));
            Assert.That(AssetDatabase.AssetPathToGUID(ChatPrefabPath), Is.EqualTo("b20de60da2b5f1e41a182119ad9cf8ec"));

            var room = roomPrefab.GetComponent<OnlineRoomUI>();
            Assert.NotNull(room);
            Assert.AreSame(memberPrefab, room.MemberList.itemPrefab);
            Assert.AreSame(chatPrefab, room.ChatMessageList.prefabSource.prefab);
            Assert.That(AssetDatabase.GetAssetPath(room.MemberList.itemPrefab), Is.EqualTo(MemberPrefabPath));
            Assert.That(AssetDatabase.GetAssetPath(room.ChatMessageList.prefabSource.prefab), Is.EqualTo(ChatPrefabPath));

            AssertMemberPrefab(memberPrefab);
            AssertChatPrefab(chatPrefab);
        }

        [Test]
        public void ControllerRefreshesMemberRowsFromSnapshotsAcrossReuse()
        {
            var ctrl = UIHelper.ShowUI<OnlineRoomUICtrl>();
            var ui = GetUI(ctrl);

            PublishRoom(Member("Alice", 101, true, OnlineRoomMemberConnectionState.Online));
            var row = GetOnlyActiveMemberRow(ui);
            AssertMember(row, "Alice", Loc.T("ui.online.room.member_player", 101),
                Loc.T("ui.online.room.owner"), Loc.T("ui.online.room.member_online"));
            Assert.That(row.CharacterImage.color, Is.EqualTo(Color.white));

            PublishRoom(Member("Bob", 202, false, OnlineRoomMemberConnectionState.DisconnectedGrace));
            Assert.AreSame(row.gameObject, GetOnlyActiveMemberRow(ui).gameObject);
            AssertMember(row, "Bob", Loc.T("ui.online.room.member_player", 202),
                string.Empty, Loc.T("ui.online.room.member_disconnected"));
            Assert.That(row.CharacterImage.color.a, Is.LessThan(1f));

            PublishRoom(Member("Alice", 101, true, OnlineRoomMemberConnectionState.Online));
            Assert.AreSame(row.gameObject, GetOnlyActiveMemberRow(ui).gameObject);
            AssertMember(row, "Alice", Loc.T("ui.online.room.member_player", 101),
                Loc.T("ui.online.room.owner"), Loc.T("ui.online.room.member_online"));
            Assert.That(row.CharacterImage.color, Is.EqualTo(Color.white), "复用成员行必须恢复在线模型颜色");
        }

        [Test]
        public void ChatRowBinding_RestoresEveryMutableBoxFieldAcrossReuse()
        {
            var ctrl = UIHelper.ShowUI<OnlineRoomUICtrl>();
            PublishRoom(Member("Alice", 101, true, OnlineRoomMemberConnectionState.Online));
            PublishChat(Message("m1", 1, "Alice", 0, "hello"));
            var design = AssetDatabase.LoadAssetAtPath<GameObject>(ChatPrefabPath)
                .GetComponent<OnlineRoomChatMessageBoxUI>();
            var row = UnityEngine.Object.Instantiate(design.gameObject).GetComponent<OnlineRoomChatMessageBoxUI>();
            try
            {
                PolluteChatRow(row);
                InvokeChatRowRefresh(ctrl, row.transform, 0);
                AssertChat(row, design, "Alice", "hello", FormatTime(0), timeActive: true);

                PublishChat(Message("m2", 2, "Bob", long.MaxValue, "bad time"));
                PolluteChatRow(row);
                InvokeChatRowRefresh(ctrl, row.transform, 0);
                AssertChat(row, design, "Bob", "bad time", string.Empty, timeActive: false);

                row.gameObject.SetActive(true);
                LogAssert.Expect(LogType.Error,
                    "[OnlineRoomUICtrl] 聊天消息行跳过刷新：index=1 超出快照 count=1，行已隐藏。");
                InvokeChatRowRefresh(ctrl, row.transform, 1);
                Assert.That(row.gameObject.activeSelf, Is.False);

                var missingBoxRow = new GameObject("MissingChatMessageBox");
                try
                {
                    LogAssert.Expect(LogType.Error,
                        "[OnlineRoomUICtrl] 聊天消息行 Prefab 契约损坏：缺少 OnlineRoomChatMessageBoxUI，行已隐藏。");
                    InvokeChatRowRefresh(ctrl, missingBoxRow.transform, 0);
                    Assert.That(missingBoxRow.activeSelf, Is.False);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(missingBoxRow);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(row.gameObject);
            }
        }

        [Test]
        public void ChatList_UsesPublishedSnapshotsForFollowTrimRoomReplacementAndCleanup()
        {
            var ctrl = UIHelper.ShowUI<OnlineRoomUICtrl>();
            var ui = GetUI(ctrl);
            PublishRoom(500);

            PublishChat(500, Array.Empty<OnlineRoomChatMessageSnapshot>());
            // 手机大厅默认收起聊天；滚动跟随契约在玩家展开面板后验证。
            ui.ChatOpenButton.onClick.Invoke();
            AssertChatList(ctrl, ui, 500, 0);

            PublishChat(500, new[] { Message("m1", 1, "Alice", 0, "first") });
            AssertChatList(ctrl, ui, 500, 1, "m1");

            var firstTwoHundred = Enumerable.Range(1, 200)
                .Select(sequence => Message($"m{sequence}", sequence, "Alice", sequence * 1000L, $"message {sequence}"))
                .ToArray();
            PublishChat(500, firstTwoHundred);
            AssertChatList(ctrl, ui, 500, 200, "m1", "m200");

            ui.ChatMessageList.verticalNormalizedPosition = 1f;
            var trimmed = Enumerable.Range(2, 200)
                .Select(sequence => Message($"m{sequence}", sequence, "Bob", sequence * 1000L, $"message {sequence}"))
                .ToArray();
            PublishChat(500, trimmed);
            AssertChatList(ctrl, ui, 500, 200, "m2", "m201");
            Assert.That(ui.ChatMessageList.verticalNormalizedPosition, Is.GreaterThan(ChatBottomFollowToleranceForTest));

            PublishRoom(600);
            AssertChatList(ctrl, ui, 0, 0);
            PublishChat(500, new[] { Message("old", 202, "Old", 0, "must not render") });
            AssertChatList(ctrl, ui, 0, 0);
            PublishChat(600, new[] { Message("new", 1, "New", 0, "new room") });
            AssertChatList(ctrl, ui, 600, 1, "new");

            PublishEmptyRoom();
            AssertChatList(ctrl, ui, 0, 0);
            PublishEmptyRoom();
            AssertChatList(ctrl, ui, 0, 0);

            ctrl.Hide();
            PublishChat(600, new[] { Message("hidden", 2, "Hidden", 0, "must stay clear") });
            AssertChatList(ctrl, ui, 0, 0);
        }

        [Test]
        public void ChatList_MissingPrefabSourceKeepsThePreviousCommittedSnapshot()
        {
            var ctrl = UIHelper.ShowUI<OnlineRoomUICtrl>();
            var ui = GetUI(ctrl);
            PublishRoom(500);
            PublishChat(500, new[] { Message("old", 1, "Alice", 0, "old message") });
            AssertChatList(ctrl, ui, 500, 1, "old");

            var source = ui.ChatMessageList.prefabSource.prefab;
            try
            {
                ui.ChatMessageList.prefabSource.prefab = null;
                LogAssert.Expect(LogType.Error,
                    "[OnlineRoomUICtrl] 聊天列表 Prefab 契约损坏：LoopVerticalScrollRect.prefabSource.prefab 必须引用 OnlineRoomChatMessage.prefab，跳过刷新。");
                PublishChat(500, new[]
                {
                    Message("new-1", 2, "Bob", 0, "new one"),
                    Message("new-2", 3, "Bob", 0, "new two")
                });

                AssertChatList(ctrl, ui, 500, 1, "old");
            }
            finally
            {
                ui.ChatMessageList.prefabSource.prefab = source;
            }
        }

        private static void AssertMemberPrefab(GameObject prefab)
        {
            var collector = prefab.GetComponent<ReferenceCollector>();
            var view = prefab.GetComponent<OnlineRoomMemberBoxUI>();
            Assert.NotNull(collector);
            Assert.NotNull(view);
            Assert.That(view, Is.InstanceOf<BaseBoxUI>());
            AssertGeneratedView<OnlineRoomMemberBoxUI>(
                new Dictionary<string, Type>
                {
                    ["CharacterImage"] = typeof(RawImage),
                    ["ConnectionText"] = typeof(TMPro.TextMeshProUGUI),
                    ["DisplayNameText"] = typeof(TMPro.TextMeshProUGUI),
                    ["OwnerText"] = typeof(TMPro.TextMeshProUGUI),
                    ["PlayerIdText"] = typeof(TMPro.TextMeshProUGUI)
                });
            AssertField(collector, view.ConnectionText, "ConnectionText", "OnlineRoomMember/ConnectionText", "ConnectionText", typeof(TMPro.TextMeshProUGUI));
            AssertField(collector, view.DisplayNameText, "DisplayNameText", "OnlineRoomMember/DisplayNameText", "DisplayNameText", typeof(TMPro.TextMeshProUGUI));
            AssertField(collector, view.OwnerText, "OwnerText", "OnlineRoomMember/OwnerText", "OwnerText", typeof(TMPro.TextMeshProUGUI));
            AssertField(collector, view.PlayerIdText, "PlayerIdText", "OnlineRoomMember/PlayerIdText", "PlayerIdText", typeof(TMPro.TextMeshProUGUI));
            AssertField(collector, view.CharacterImage, "CharacterImage", "OnlineRoomMember/CharacterImage", "CharacterImage", typeof(RawImage));
        }

        private static void AssertChatPrefab(GameObject prefab)
        {
            var collector = prefab.GetComponent<ReferenceCollector>();
            var view = prefab.GetComponent<OnlineRoomChatMessageBoxUI>();
            Assert.NotNull(collector);
            Assert.NotNull(view);
            Assert.That(view, Is.InstanceOf<BaseBoxUI>());
            AssertGeneratedView<OnlineRoomChatMessageBoxUI>(
                new Dictionary<string, Type>
                {
                    ["Background"] = typeof(Image),
                    ["ContentText"] = typeof(TMPro.TextMeshProUGUI),
                    ["SenderText"] = typeof(TMPro.TextMeshProUGUI),
                    ["TimeText"] = typeof(TMPro.TextMeshProUGUI)
                });
            AssertField(collector, view.Background, "Background", "OnlineRoomChatMessage", "OnlineRoomChatMessage", typeof(Image));
            AssertField(collector, view.ContentText, "ContentText", "OnlineRoomChatMessage/ContentText", "ContentText", typeof(TMPro.TextMeshProUGUI));
            AssertField(collector, view.SenderText, "SenderText", "OnlineRoomChatMessage/Header/SenderText", "SenderText", typeof(TMPro.TextMeshProUGUI));
            AssertField(collector, view.TimeText, "TimeText", "OnlineRoomChatMessage/Header/TimeText", "TimeText", typeof(TMPro.TextMeshProUGUI));
        }

        private static void AssertGeneratedView<T>(IReadOnlyDictionary<string, Type> expected)
        {
            var type = typeof(T);
            Assert.That(type.GetMethod("Render", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), Is.Null);
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            Assert.That(fields.Select(field => field.Name).OrderBy(name => name).ToArray(),
                Is.EqualTo(expected.Keys.OrderBy(name => name).ToArray()));
            foreach (var field in fields)
            {
                Assert.That(field.FieldType, Is.EqualTo(expected[field.Name]), field.Name);
                Assert.That(field.FieldType.FullName, Does.Not.Contain("OnlineRoomMemberSnapshot"));
                Assert.That(field.FieldType.FullName, Does.Not.Contain("OnlineRoomChatMessageSnapshot"));
            }
        }

        private static void AssertField(
            ReferenceCollector collector,
            UnityEngine.Object fieldValue,
            string key,
            string path,
            string objectName,
            Type componentType)
        {
            var item = collector.data.Single(data => data.key == key);
            Assert.AreSame(item.gameObject, fieldValue, key);
            var go = ResolveGameObject(fieldValue);
            Assert.That(GetHierarchyPath(go.transform), Is.EqualTo(path), key);
            Assert.That(go.name, Is.EqualTo(objectName), key);
            Assert.That(fieldValue.GetType(), Is.EqualTo(componentType), key);
            Assert.That(item.TypeName, Is.EqualTo(componentType.FullName), key);
        }

        private static OnlineRoomUI GetUI(OnlineRoomUICtrl ctrl)
        {
            var field = typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic);
            return (OnlineRoomUI)field.GetValue(ctrl);
        }

        private static OnlineRoomMemberBoxUI GetOnlyActiveMemberRow(OnlineRoomUI ui)
        {
            return GetOnlyActiveRow(ui.MemberList).GetComponent<OnlineRoomMemberBoxUI>();
        }

        private static Transform GetOnlyActiveRow(CustomListView list)
        {
            var active = list.GetContentTransform().Cast<Transform>().Where(child => child.gameObject.activeSelf).ToArray();
            Assert.That(active.Length, Is.EqualTo(1));
            return active[0];
        }

        private static void InvokeChatRowRefresh(OnlineRoomUICtrl ctrl, Transform row, int index)
        {
            var method = typeof(OnlineRoomUICtrl).GetMethod("RenderChatMessage", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(ctrl, new object[] { row, index });
        }

        private static void AssertMember(OnlineRoomMemberBoxUI row, string display, string player, string owner, string connection)
        {
            Assert.That(row.DisplayNameText.text, Is.EqualTo(display));
            Assert.That(row.PlayerIdText.text, Is.EqualTo(player));
            Assert.That(row.OwnerText.text, Is.EqualTo(owner));
            Assert.That(row.ConnectionText.text, Is.EqualTo(connection));
        }

        private static void AssertChat(
            OnlineRoomChatMessageBoxUI row,
            OnlineRoomChatMessageBoxUI design,
            string sender,
            string content,
            string time,
            bool timeActive)
        {
            Assert.That(row.SenderText.text, Is.EqualTo(sender));
            Assert.That(row.ContentText.text, Is.EqualTo(content));
            Assert.That(row.TimeText.text, Is.EqualTo(time));
            Assert.That(row.SenderText.gameObject.activeSelf, Is.EqualTo(design.SenderText.gameObject.activeSelf));
            Assert.That(row.ContentText.gameObject.activeSelf, Is.EqualTo(design.ContentText.gameObject.activeSelf));
            Assert.That(row.TimeText.gameObject.activeSelf, Is.EqualTo(timeActive && design.TimeText.gameObject.activeSelf));
            AssertTextDesign(row.SenderText, design.SenderText);
            AssertTextDesign(row.ContentText, design.ContentText);
            AssertTextDesign(row.TimeText, design.TimeText);
            Assert.That(row.Background.gameObject.activeSelf, Is.EqualTo(design.Background.gameObject.activeSelf));
            Assert.That(row.Background.color, Is.EqualTo(design.Background.color));
            Assert.That(row.Background.raycastTarget, Is.False);
        }

        private static void AssertTextDesign(TMPro.TMP_Text target, TMPro.TMP_Text design)
        {
            Assert.That(target.color, Is.EqualTo(design.color));
            Assert.That(target.alignment, Is.EqualTo(design.alignment));
            Assert.That(target.raycastTarget, Is.EqualTo(design.raycastTarget));
        }

        private static void PolluteChatRow(OnlineRoomChatMessageBoxUI row)
        {
            row.SenderText.text = "stale sender";
            row.ContentText.text = "stale content";
            row.TimeText.text = "stale time";
            row.SenderText.gameObject.SetActive(false);
            row.ContentText.gameObject.SetActive(false);
            row.TimeText.gameObject.SetActive(false);
            row.Background.gameObject.SetActive(false);
            foreach (var text in new[] { row.SenderText, row.ContentText, row.TimeText })
            {
                text.color = Color.magenta;
                text.alignment = TMPro.TextAlignmentOptions.BottomRight;
                text.raycastTarget = !text.raycastTarget;
            }
            row.Background.color = Color.green;
            row.Background.raycastTarget = true;
        }

        private static void AssertChatList(
            OnlineRoomUICtrl ctrl,
            OnlineRoomUI ui,
            long expectedRoomId,
            int expectedCount,
            params string[] expectedMessageIds)
        {
            Assert.That(ui.ChatMessageList.totalCount, Is.EqualTo(expectedCount));
            Assert.That(GetPrivate<long>(ctrl, "displayedChatRoomId"), Is.EqualTo(expectedRoomId));
            var messages = GetPrivate<List<OnlineRoomChatMessageSnapshot>>(ctrl, "chatMessages");
            Assert.That(messages, Has.Count.EqualTo(expectedCount));
            if (expectedMessageIds.Length > 0)
            {
                Assert.That(messages[0].ChatMessageId, Is.EqualTo(expectedMessageIds[0]));
                Assert.That(messages[messages.Count - 1].ChatMessageId,
                    Is.EqualTo(expectedMessageIds[expectedMessageIds.Length - 1]));
            }
        }

        private static T GetPrivate<T>(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            return (T)field.GetValue(instance);
        }

        private static void PublishRoom(OnlineRoomMemberSnapshot member)
        {
            PublishRoom(500, member);
        }

        private static void PublishRoom(long roomId)
        {
            PublishRoom(roomId, Member("Owner", 1, true, OnlineRoomMemberConnectionState.Online));
        }

        private static void PublishRoom(long roomId, OnlineRoomMemberSnapshot member)
        {
            Game.Interface.GetModel<OnlineRoomModel>().Publish(new OnlineRoomSnapshot(
                true,
                roomId,
                "ABC234",
                member.PlayerId,
                member.JoinSequence,
                new[] { member },
                false,
                OnlineRoomOperationState.Idle,
                OnlineRoomResultCode.None));
        }

        private static void PublishChat(OnlineRoomChatMessageSnapshot message)
        {
            PublishChat(500, new[] { message });
        }

        private static void PublishChat(long roomId, IReadOnlyList<OnlineRoomChatMessageSnapshot> messages)
        {
            long maxSequence = messages.Count == 0 ? 0 : messages[messages.Count - 1].ChatSequence;
            Game.Interface.GetModel<OnlineRoomChatModel>().Publish(new OnlineRoomChatSnapshot(
                roomId,
                messages,
                maxSequence,
                OnlineRoomChatHistoryState.Ready,
                OnlineRoomChatSendState.Idle,
                OnlineRoomChatResultCode.Success,
                false,
                string.Empty));
        }

        private static void PublishEmptyRoom()
        {
            Game.Interface.GetModel<OnlineRoomModel>().Publish(new OnlineRoomSnapshot(
                false,
                0,
                string.Empty,
                0,
                0,
                Array.Empty<OnlineRoomMemberSnapshot>(),
                false,
                OnlineRoomOperationState.Idle,
                OnlineRoomResultCode.None));
        }

        private const float ChatBottomFollowToleranceForTest = 0.02f;

        private static OnlineRoomMemberSnapshot Member(
            string displayName,
            long playerId,
            bool owner,
            OnlineRoomMemberConnectionState state)
        {
            return new OnlineRoomMemberSnapshot(playerId, displayName, owner, state, playerId);
        }

        private static OnlineRoomChatMessageSnapshot Message(
            string id,
            long sequence,
            string sender,
            long serverTime,
            string content)
        {
            return new OnlineRoomChatMessageSnapshot(id, sequence, sequence, sender, serverTime, content);
        }

        private static string FormatTime(long serverTime)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(serverTime).ToLocalTime().ToString("HH:mm");
        }

        private static GameObject ResolveGameObject(UnityEngine.Object obj)
        {
            return obj is GameObject go ? go : ((Component)obj).gameObject;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }

        private static void InstallMinimalGameArchitectureForUiTest()
        {
            var game = new Game();
            var accountModel = new OnlineAccountModel();
            var roomModel = new OnlineRoomModel();
            var roomChatModel = new OnlineRoomChatModel();
            var localizationModel = new LocalizationModel();
            var network = new FakeNetworkRuntime();
            var accountSystem = new OnlineAccountSystem();
            var roomSystem = new OnlineRoomSystem();
            var roomChatSystem = new OnlineRoomChatSystem();

            game.RegisterModel(localizationModel);
            game.RegisterModel(accountModel);
            game.RegisterModel(roomModel);
            game.RegisterModel(roomChatModel);
            game.RegisterUtility<INetworkRuntime>(network);
            game.RegisterUtility<IOnlineAccountPreferenceStore>(new FakePreferenceStore());
            game.RegisterUtility(new LocalizationUtility(loadConfiguredTables: false));
            game.RegisterSystem(accountSystem);
            game.RegisterSystem(roomSystem);
            game.RegisterSystem(roomChatSystem);
            // 大厅已复用街机入口；普通房间测试也必须提供其空快照依赖。
            game.RegisterSystem(new OnlineArcadeRunSystem());
            Init(localizationModel);
            Init(accountModel);
            Init(roomModel);
            Init(roomChatModel);
            Init(accountSystem);
            Init(roomSystem);
            Init(roomChatSystem);

            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, game);
        }

        private static void Init(ICanInit target)
        {
            target.Init();
            target.Initialized = true;
        }

        private static void ResetGameArchitecture()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private static void EnsureUiRootForTest()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(UIRoot));
            new GameObject("HUD", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("Dialog", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("Popup", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("Tips", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("Top", typeof(RectTransform)).transform.SetParent(root.transform, false);
            if (GameObject.Find(SceneRoot.PoolRootName) == null)
            {
                new GameObject(SceneRoot.PoolRootName);
            }
            var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            Assert.NotNull(inputModuleType, "Unity Input System UI module type must be available.");
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), inputModuleType);
            eventSystem.transform.SetParent(root.transform, false);
        }

        private static void DestroyTestSceneObjects()
        {
            foreach (var rootName in new[] { "UIRoot", "EventSystem", SceneRoot.PoolRootName })
            {
                var obj = GameObject.Find(rootName);
                if (obj != null)
                {
                    UnityEngine.Object.DestroyImmediate(obj);
                }
            }
        }

        private sealed class FakeNetworkRuntime : INetworkRuntime
        {
            public bool IsInitialized { get; private set; } = true;
            public NetworkConnectionState ConnectionState { get; private set; } = NetworkConnectionState.Connected;
            public int ConnectionGeneration => 1;
            public void Initialize() => IsInitialized = true;
            public void Update() { }
            public Task ConnectAsync(NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<TResponse> CallAsync<TResponse>(IRequest request, int timeoutMilliseconds, CancellationToken cancellationToken)
                where TResponse : class, IResponse
            {
                if (request is C2G_GetPublicRoomList)
                {
                    G2C_GetPublicRoomList response = G2C_GetPublicRoomList.Create();
                    response.Error = 0;
                    response.HasMore = false;
                    response.NextCursorRoomId = 0;
                    return Task.FromResult(response as TResponse);
                }

                return Task.FromException<TResponse>(new InvalidOperationException("network calls are outside this migration test"));
            }
            public IDisposable RegisterPushHandler<TMessage>(Action<TMessage> handler) where TMessage : class, IMessage => new Subscription();
            public IDisposable RegisterDisconnectedHandler(Action<NetworkDisconnected> handler) => new Subscription();
            public void Disconnect(NetworkDisconnectReason reason) => ConnectionState = NetworkConnectionState.Disconnected;
            public void Dispose() => ConnectionState = NetworkConnectionState.Disposed;
        }

        private sealed class Subscription : IDisposable
        {
            public void Dispose() { }
        }

        private sealed class FakePreferenceStore : IOnlineAccountPreferenceStore
        {
            public string LoadLastUserName() => string.Empty;
            public bool SaveLastUserName(string userName, out string error)
            {
                error = string.Empty;
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
                        return new TextAsset(File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Localization", path))) as T;
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
    }
}
