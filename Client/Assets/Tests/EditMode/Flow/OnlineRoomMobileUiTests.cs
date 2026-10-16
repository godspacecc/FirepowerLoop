using System;
using System.Reflection;
using Game;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirepowerLoop.Tests.EditMode.Flow
{
    public sealed class OnlineRoomMobileUiTests
    {
        private GameObject instance;
        private OnlineRoomUI ui;
        private OnlineRoomUICtrl ctrl;

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Online/OnlineRoom.prefab");
            instance = UnityEngine.Object.Instantiate(prefab);
            ui = instance.GetComponent<OnlineRoomUI>();
            ctrl = new OnlineRoomUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ctrl, ui);
            Set("isVisible", true);
            Set("latest", Room(500));
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(instance);

        [Test]
        public void Prefab_BindsChatControlsAndKeepsMobileColoursAfterAwake()
        {
            UITheme.Apply(instance);
            var collector = ui.GetComponent<ReferenceCollector>();
            foreach (string name in new[] { "ChatPanelRoot", "ChatPreviewRoot", "ChatPreviewText", "ChatOpenButton", "ChatCloseButton", "ChatUnreadText" })
            {
                UnityEngine.Object value = Field<UnityEngine.Object>(name);
                Assert.That(collector.GetObject(name), Is.EqualTo(value), name);
            }
            Assert.That(Field<GameObject>("ChatPanelRoot").activeSelf, Is.False);
            Assert.That(ui.WorldButton.targetGraphic.color.g, Is.GreaterThan(0.65f));
            Assert.That(Field<Button>("ChatOpenButton").GetComponent<RectTransform>().rect.height, Is.GreaterThanOrEqualTo(120));
            Assert.That(ui.ChatInput.GetComponent<RectTransform>().rect.height, Is.GreaterThanOrEqualTo(120));
            Assert.That(ui.ChatInput.textComponent.fontSize, Is.GreaterThanOrEqualTo(42));
        }

        [Test]
        public void CollapsedPreview_ShowsOnlyLastTwoMessagesAndEscapesRichText()
        {
            Render(Message(1, "first"), Message(2, "<size=999>hello</size>"), Message(3, "last"));
            var preview = Field<TMP_Text>("ChatPreviewText");
            Assert.That(preview.text, Does.Not.Contain("first"));
            Assert.That(preview.text, Does.Contain("hello").And.Contain("last"));
            Assert.That(preview.richText, Is.False);
        }

        [Test]
        public void HistoryAndRepeatedPush_DoNotInflateUnreadCount()
        {
            Render(Message(1, "history"));
            Assert.That(Field<TMP_Text>("ChatUnreadText").gameObject.activeSelf, Is.False);
            Render(Message(1, "history"), Message(2, "new"));
            Render(Message(1, "history"), Message(2, "new"));
            Assert.That(Field<TMP_Text>("ChatUnreadText").text, Is.EqualTo("1"));
            Invoke("SetChatExpanded", true);
            Assert.That(Field<TMP_Text>("ChatUnreadText").gameObject.activeSelf, Is.False);
            Assert.That(Field<GameObject>("ChatPanelRoot").activeSelf, Is.True);
            Assert.That(Field<GameObject>("ChatPreviewRoot").activeSelf, Is.False);
        }

        [Test]
        public void SwitchingRoom_DiscardsUnreadAndOldPreview()
        {
            Render(Message(1, "old"));
            Render(Message(1, "old"), Message(2, "new"));
            Invoke("ClearChatList");
            Set("latest", Room(600));
            Invoke("RenderChat", Chat(600, Message(1, "other room")));
            Assert.That(Field<TMP_Text>("ChatUnreadText").gameObject.activeSelf, Is.False);
            Assert.That(Field<TMP_Text>("ChatPreviewText").text, Does.Not.Contain("old").And.Contain("other room"));
        }

        [Test]
        public void ClosingChat_ClearsOnlyItsFocusAndPreservesDraft()
        {
            var previous = EventSystem.current;
            var eventRoot = new GameObject("LobbyTestEvents", typeof(EventSystem));
            var events = eventRoot.GetComponent<EventSystem>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(EventSystem).GetMethod("OnEnable", flags).Invoke(events, null);
            try
            {
                EventSystem.current = events;
                Invoke("SetChatExpanded", true);
                ui.ChatInput.SetTextWithoutNotify("draft");
                EventSystem.current.SetSelectedGameObject(ui.ChatInput.gameObject);
                Invoke("SetChatExpanded", false);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.Null);
                Assert.That(ui.ChatInput.text, Is.EqualTo("draft"));
                Assert.That(Field<GameObject>("ChatPanelRoot").activeSelf, Is.False);
                Assert.That(Field<GameObject>("ChatPreviewRoot").activeSelf, Is.True);
            }
            finally
            {
                typeof(EventSystem).GetMethod("OnDisable", flags).Invoke(events, null);
                UnityEngine.Object.DestroyImmediate(eventRoot);
                if (previous != null) EventSystem.current = previous;
            }
        }

        [TestCase(1920, 1080)]
        [TestCase(2400, 1080)]
        public void ExpandedChat_DoesNotCoverPrimaryAction(int width, int height)
        {
            var canvas = new GameObject("MobileLobbyCanvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                ((RectTransform)canvas.transform).sizeDelta = new Vector2(width, height);
                instance.transform.SetParent(canvas.transform, false);
                Invoke("SetChatExpanded", true);
                Canvas.ForceUpdateCanvases();
                var chat = Bounds(Field<GameObject>("ChatPanelRoot").GetComponent<RectTransform>());
                var action = Bounds(ui.WorldButton.GetComponent<RectTransform>());
                Assert.That(chat.Overlaps(action), Is.False);
                Assert.That(chat.width, Is.GreaterThan(width * 0.4f));
            }
            finally { instance.transform.SetParent(null); UnityEngine.Object.DestroyImmediate(canvas); }
        }

        private T Field<T>(string name) where T : UnityEngine.Object
        {
            var field = typeof(OnlineRoomUI).GetField(name);
            Assert.That(field, Is.Not.Null, "Missing generated UI reference: " + name);
            var value = field.GetValue(ui) as T;
            Assert.That(value, Is.Not.Null, "Unresolved UI reference: " + name);
            return value;
        }

        private void Set(string name, object value) => typeof(OnlineRoomUICtrl).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ctrl, value);
        private void Invoke(string name, params object[] args)
        {
            var method = typeof(OnlineRoomUICtrl).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Missing behavior: " + name);
            method.Invoke(ctrl, args);
        }
        private void Render(params OnlineRoomChatMessageSnapshot[] messages) => Invoke("RenderChat", Chat(500, messages));
        private static OnlineRoomChatSnapshot Chat(long roomId, params OnlineRoomChatMessageSnapshot[] messages) =>
            new OnlineRoomChatSnapshot(roomId, messages, messages.Length, OnlineRoomChatHistoryState.Ready, OnlineRoomChatSendState.Idle, OnlineRoomChatResultCode.None, false, string.Empty);
        private static OnlineRoomChatMessageSnapshot Message(long seq, string text) => new OnlineRoomChatMessageSnapshot("m" + seq, seq, 20, "friend", 1000, text);
        private static OnlineRoomSnapshot Room(long id) => new OnlineRoomSnapshot(true, id, "ABC234", 10, 1, Array.Empty<OnlineRoomMemberSnapshot>(), false, OnlineRoomOperationState.Idle, OnlineRoomResultCode.None);
        private static Rect Bounds(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }
    }
}
