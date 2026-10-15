using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class UiCloseAndRootGenerationTests
    {
        private const string OnlineRoomPath = "Assets/Game/Prefabs/UI/Online/OnlineRoom.prefab";
        private const string RuntimeLogPath = "Assets/Game/Prefabs/UI/Diagnostics/RuntimeLog.prefab";
        private const string TestCmdPath = "Assets/Game/Prefabs/UI/Diagnostics/TestCmd.prefab";

        private string tempRoot;

        [TearDown]
        public void TearDown()
        {
            CleanupControllerHarness();
        }

        [Test]
        public void Prefabs_HaveCloseButtonsInsideRootPanelWithoutPlannedOverlaps()
        {
            AssertCloseLayout<OnlineRoomUI>(OnlineRoomPath, "CloseButton", new Vector2(40f, 40f),
                "CurrentRoomCodeText");
            AssertCloseLayout<RuntimeLogUI>(RuntimeLogPath, "BtnClose", new Vector2(40f, 40f),
                "TitleText", "BtnAll", "BtnNormal", "BtnWarning", "BtnError");
            AssertCloseLayout<TestCmdUI>(TestCmdPath, "BtnClose", new Vector2(40f, 40f),
                "TextTitle", "TextCategoryTitle", "TextEntryTitle", "TextDetailsTitle");
        }

        [Test]
        public void Prefabs_HaveCollectorGeneratedViewAndStableFieldBindings()
        {
            AssertGeneratedBindings<OnlineRoomUI>(OnlineRoomPath, OnlineRoomFields);
            AssertGeneratedBindings<RuntimeLogUI>(RuntimeLogPath, RuntimeLogFields);
            AssertGeneratedBindings<TestCmdUI>(TestCmdPath, TestCmdFields);
        }

        [Test]
        public void OnlineRoomWorldButtonHasGeneratedBindingAndSafeLayout()
        {
            var prefab = LoadPrefab(OnlineRoomPath);
            var ui = prefab.GetComponent<OnlineRoomUI>();
            RectTransform world = ui.WorldButton.GetComponent<RectTransform>();
            Assert.That(Intersects(world, ui.LeaveButton.GetComponent<RectTransform>()), Is.False);
            Assert.That(Intersects(world, ui.MemberList.GetComponent<RectTransform>()), Is.False);
            Assert.That(Intersects(world, ui.ChatInput.GetComponent<RectTransform>()), Is.False);
            Assert.That(Intersects(world, ui.ChatSendButton.GetComponent<RectTransform>()), Is.False);
        }

        [Test]
        public void TestCmdPrefab_HasCompleteStaticResultBindings()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestCmdPath);
            var ui = prefab.GetComponent<TestCmdUI>();

            Assert.That(ui.ResultScrollRect, Is.Not.Null);
            Assert.That(ui.ResultViewportRect, Is.SameAs(ui.ResultScrollRect.transform));
            Assert.That(ui.ResultContentRect, Is.Not.Null);
            Assert.That(ui.ResultScrollRect.viewport, Is.SameAs(ui.ResultViewportRect));
            Assert.That(ui.ResultScrollRect.content, Is.SameAs(ui.ResultContentRect));
            Assert.That(ui.ResultViewportRect.GetComponent<Image>().raycastTarget, Is.True);
            Assert.That(ui.ResultViewportRect.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(ui.TextResult.transform.parent, Is.SameAs(ui.ResultContentRect));
        }

        [Test]
        public void OnlineRoomCloseButton_UsesRealControllerHideAndClearsSelection()
        {
            SetupControllerHarness();
            var ctrl = UIHelper.ShowUI<OnlineRoomUICtrl>();
            var ui = GetBaseUI<OnlineRoomUI>(ctrl);

            var eventSystem = EnsureCurrentEventSystem();
            eventSystem.SetSelectedGameObject(ui.RoomCodeInput.gameObject);
            ui.CloseButton.onClick.Invoke();

            Assert.That(ctrl.IsShow(), Is.False);
            Assert.That(eventSystem.currentSelectedGameObject, Is.Null);
        }

        [Test]
        public void RuntimeLogCloseButton_UsesRealControllerHide()
        {
            SetupControllerHarness();
            var ctrl = UIHelper.ShowUI<RuntimeLogUICtrl>();
            var ui = GetBaseUI<RuntimeLogUI>(ctrl);

            ui.BtnClose.onClick.Invoke();

            Assert.That(ctrl.IsShow(), Is.False);
        }

        [Test]
        public void TestCmdCloseButton_UsesRealControllerHideAndClearsSelection()
        {
            SetupControllerHarness();
            var ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var ui = GetBaseUI<TestCmdUI>(ctrl);

            var eventSystem = EnsureCurrentEventSystem();
            eventSystem.SetSelectedGameObject(ui.InputCommand.gameObject);
            ui.BtnClose.onClick.Invoke();

            Assert.That(ctrl.IsShow(), Is.False);
            Assert.That(eventSystem.currentSelectedGameObject, Is.Null);
        }

        private static void AssertCloseLayout<T>(string prefabPath, string closeField, Vector2 expectedSize, params string[] nonOverlappingFields)
            where T : BaseUI
        {
            var prefab = LoadPrefab(prefabPath);
            var ui = prefab.GetComponent<T>();
            Assert.That(ui, Is.Not.Null);
            var close = (Button)typeof(T).GetField(closeField).GetValue(ui);
            var closeRect = (RectTransform)close.transform;
            var rootRect = (RectTransform)prefab.transform;

            Assert.That(closeRect.anchorMin, Is.EqualTo(Vector2.one), closeField);
            Assert.That(closeRect.anchorMax, Is.EqualTo(Vector2.one), closeField);
            Assert.That(closeRect.pivot, Is.EqualTo(Vector2.one), closeField);
            Assert.That(closeRect.anchoredPosition, Is.EqualTo(new Vector2(-16f, -16f)), closeField);
            Assert.That(closeRect.sizeDelta, Is.EqualTo(expectedSize), closeField);
            Assert.That(Contains(rootRect, closeRect), Is.True, closeField);

            foreach (var fieldName in nonOverlappingFields)
            {
                var other = RectOf(ResolveLayoutObject(typeof(T), ui, prefab.transform, fieldName));
                Assert.That(Intersects(closeRect, other), Is.False, fieldName);
            }
        }

        private static void AssertGeneratedBindings<T>(string prefabPath, FieldExpectation[] expectedFields)
            where T : BaseUI
        {
            var prefab = LoadPrefab(prefabPath);
            var collector = prefab.GetComponent<ReferenceCollector>();
            var ui = prefab.GetComponent<T>();
            Assert.That(collector, Is.Not.Null);
            Assert.That(prefab.GetComponents<ReferenceCollector>().Length, Is.EqualTo(1));
            Assert.That(ui, Is.Not.Null);

            var fields = typeof(T)
                .GetFields(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance);
            var fieldsByName = fields.ToDictionary(field => field.Name);
            Assert.That(fieldsByName.Keys, Is.EquivalentTo(expectedFields.Select(item => item.Name)));
            Assert.That(collector.data.Select(item => item.key), Is.EquivalentTo(expectedFields.Select(item => item.Name)));
            Assert.That(collector.data.Select(item => item.key).Distinct().Count(), Is.EqualTo(collector.data.Count));

            foreach (var expected in expectedFields)
            {
                var field = fieldsByName[expected.Name];
                var viewValue = field.GetValue(ui) as Object;
                var collected = collector.data.Single(item => item.key == expected.Name);
                var collectedValue = ResolveReferenceObject(collected.gameObject, field.FieldType);

                Assert.That(field.FieldType,
                    Is.EqualTo(expected.Type).Or.EqualTo(expected.ActualType), expected.Name);
                Assert.That(collected.TypeName,
                    Is.EqualTo(expected.Type.FullName).Or.EqualTo(expected.ActualType.FullName), expected.Name);
                Assert.That(viewValue, Is.Not.Null, expected.Name);
                Assert.That(collectedValue, Is.Not.Null, expected.Name);
                Assert.That(PathOf(prefab.transform, viewValue), Is.EqualTo(expected.Path), expected.Name);
                Assert.That(PathOf(prefab.transform, collectedValue), Is.EqualTo(expected.Path), expected.Name);
                Assert.That(ObjectName(viewValue), Is.EqualTo(expected.ObjectName), expected.Name);
                Assert.That(viewValue.GetType(), Is.EqualTo(expected.ActualType), expected.Name);
                Assert.That(ReferenceEquals(viewValue, collectedValue), Is.True, expected.Name);
            }
        }

        private void SetupControllerHarness()
        {
            CleanupControllerHarness();
            ResetArchitecture();
            UIHelper.ResetForTests();
            ResourceLoader.SetLoaderForTests(new MinimalLocalizationLoader());
            LocalizationLanguages.ResetForTests();
            Game.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(new MinimalRuntimeConfigProvider());
            };
            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayUiCloseTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            new GameObject(SceneRoot.PoolRootName);
            CreateUiRoot();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab"));
        }

        private void CleanupControllerHarness()
        {
            UIHelper.ResetForTests();
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            Game.OnRegisterPatch = _ => { };
            ResetArchitecture();
            EditModeSceneIsolation.DestroyNamedObjectsInActiveTestScene(
                "UIRoot",
                "EventSystem",
                SceneRoot.PoolRootName);
            if (!string.IsNullOrEmpty(tempRoot) && Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }

            tempRoot = null;
        }

        private static GameObject LoadPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            return prefab;
        }

        private static T GetBaseUI<T>(BaseUICtrl ctrl) where T : BaseUI
        {
            Assert.That(ctrl, Is.Not.Null);
            Assert.That(ctrl.IsShow(), Is.True);
            var ui = typeof(BaseUICtrl)
                .GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(ctrl) as T;
            Assert.That(ui, Is.Not.Null);
            return ui;
        }

        private static Object ResolveReferenceObject(Object source, Type expectedType)
        {
            if (source == null || expectedType == null)
            {
                return null;
            }

            if (expectedType.IsInstanceOfType(source))
            {
                return source;
            }

            if (source is GameObject gameObject)
            {
                return expectedType == typeof(GameObject)
                    ? gameObject
                    : gameObject.GetComponent(expectedType);
            }

            if (source is Component component)
            {
                return expectedType == typeof(GameObject)
                    ? component.gameObject
                    : component.GetComponent(expectedType);
            }

            return null;
        }

        private static RectTransform RectOf(Object value)
        {
            return value switch
            {
                GameObject gameObject => gameObject.transform as RectTransform,
                Component component => component.transform as RectTransform,
                _ => null
            };
        }

        private static Object ResolveLayoutObject(Type uiType, BaseUI ui, Transform prefabRoot, string fieldOrPath)
        {
            var field = uiType.GetField(fieldOrPath);
            if (field != null)
            {
                return field.GetValue(ui) as Object;
            }

            var child = prefabRoot.Find(fieldOrPath);
            Assert.That(child, Is.Not.Null, fieldOrPath);
            return child.GetComponent<RectTransform>();
        }

        private static string PathOf(Transform root, Object value)
        {
            var rect = RectOf(value);
            Assert.That(rect, Is.Not.Null);
            var parts = new List<string>();
            var current = rect.transform;
            while (current != null)
            {
                parts.Add(current.name);
                if (current == root)
                {
                    break;
                }

                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string ObjectName(Object value)
        {
            return value is Component component ? component.name : value.name;
        }

        private static bool Contains(RectTransform root, RectTransform rect)
        {
            return Left(rect) >= Left(root) - 0.01f &&
                   Right(rect) <= Right(root) + 0.01f &&
                   Bottom(rect) >= Bottom(root) - 0.01f &&
                   Top(rect) <= Top(root) + 0.01f;
        }

        private static bool Intersects(RectTransform left, RectTransform right)
        {
            return Left(left) < Right(right) &&
                   Right(left) > Left(right) &&
                   Bottom(left) < Top(right) &&
                   Top(left) > Bottom(right);
        }

        private static float Left(RectTransform rect) => WorldRect(rect).xMin;
        private static float Right(RectTransform rect) => WorldRect(rect).xMax;
        private static float Bottom(RectTransform rect) => WorldRect(rect).yMin;
        private static float Top(RectTransform rect) => WorldRect(rect).yMax;

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static void CreateUiRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(UIRoot));
            foreach (var layerName in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var layer = new GameObject(layerName, typeof(RectTransform));
                layer.transform.SetParent(root.transform, false);
            }

            var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            Assert.That(inputModuleType, Is.Not.Null);
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), inputModuleType);
            eventSystem.transform.SetParent(root.transform, false);
        }

        private static EventSystem EnsureCurrentEventSystem()
        {
            var eventSystem = EditModeSceneIsolation.FindObjectsInActiveTestScene<EventSystem>().FirstOrDefault();
            Assert.That(eventSystem, Is.Not.Null);
            RegisterKnownEventSystem(eventSystem);
            eventSystem.UpdateModules();
            EventSystem.current = eventSystem;
            Assert.That(EventSystem.current, Is.SameAs(eventSystem));
            return eventSystem;
        }

        private static void RegisterKnownEventSystem(EventSystem eventSystem)
        {
            var listField = typeof(EventSystem)
                .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
                .FirstOrDefault(field =>
                    typeof(System.Collections.IList).IsAssignableFrom(field.FieldType) &&
                    field.FieldType.IsGenericType &&
                    field.FieldType.GetGenericArguments()[0] == typeof(EventSystem));
            var list = listField?.GetValue(null) as System.Collections.IList;
            if (list != null && !list.Contains(eventSystem))
            {
                list.Add(eventSystem);
            }
        }

        private static void ResetArchitecture()
        {
            if (typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                    ?.GetValue(null) is IArchitecture architecture)
            {
                architecture.Deinit();
            }

            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private static readonly FieldExpectation[] OnlineRoomFields =
        {
            Field("RootPanel", typeof(RectTransform), typeof(RectTransform), "OnlineRoom", "OnlineRoom"),
            Field("EmptyRoot", typeof(GameObject), typeof(GameObject), "OnlineRoom/EmptyRoot", "EmptyRoot"),
            Field("JoinedRoot", typeof(GameObject), typeof(GameObject), "OnlineRoom/JoinedRoot", "JoinedRoot"),
            Field("CurrentRoomCodeText", typeof(TMP_Text), typeof(TextMeshProUGUI), "OnlineRoom/JoinedRoot/CurrentRoomCodeText", "CurrentRoomCodeText"),
            Field("DisconnectHintRoot", typeof(GameObject), typeof(GameObject), "OnlineRoom/JoinedRoot/DisconnectHintText", "DisconnectHintText"),
            Field("ResultText", typeof(TMP_Text), typeof(TextMeshProUGUI), "OnlineRoom/ResultText", "ResultText"),
            Field("RoomCodeInput", typeof(TMP_InputField), typeof(TMP_InputField), "OnlineRoom/EmptyRoot/RoomCodeInput", "RoomCodeInput"),
            Field("CreateButton", typeof(Button), typeof(Button), "OnlineRoom/EmptyRoot/CreateButton", "CreateButton"),
            Field("JoinButton", typeof(Button), typeof(Button), "OnlineRoom/EmptyRoot/JoinButton", "JoinButton"),
            Field("LeaveButton", typeof(Button), typeof(Button), "OnlineRoom/JoinedRoot/LeaveButton", "LeaveButton"),
            Field("WorldButton", typeof(Button), typeof(Button), "OnlineRoom/JoinedRoot/WorldButton", "WorldButton"),
            Field("CloseButton", typeof(Button), typeof(Button), "OnlineRoom/CloseButton", "CloseButton"),
            Field("MemberList", typeof(CustomListView), typeof(CustomListView), "OnlineRoom/JoinedRoot/MemberViewport/MemberContent", "MemberContent"),
            Field("ChatStateText", typeof(TMP_Text), typeof(TextMeshProUGUI), "OnlineRoom/JoinedRoot/ChatPanel/ChatStateText", "ChatStateText"),
            Field("ChatMessageList", typeof(LoopVerticalScrollRect), typeof(LoopVerticalScrollRect), "OnlineRoom/JoinedRoot/ChatPanel/ChatViewport", "ChatViewport"),
            Field("ChatInput", typeof(TMP_InputField), typeof(TMP_InputField), "OnlineRoom/JoinedRoot/ChatPanel/ChatInput", "ChatInput"),
            Field("ChatSendButton", typeof(Button), typeof(Button), "OnlineRoom/JoinedRoot/ChatPanel/ChatSendButton", "ChatSendButton")
        };

        private static readonly FieldExpectation[] RuntimeLogFields =
        {
            Field("BtnAll", typeof(Button), typeof(Button), "RuntimeLog/BtnAll", "BtnAll"),
            Field("BtnNormal", typeof(Button), typeof(Button), "RuntimeLog/BtnNormal", "BtnNormal"),
            Field("BtnWarning", typeof(Button), typeof(Button), "RuntimeLog/BtnWarning", "BtnWarning"),
            Field("BtnError", typeof(Button), typeof(Button), "RuntimeLog/BtnError", "BtnError"),
            Field("LogList", typeof(LoopVerticalScrollRect), typeof(LoopVerticalScrollRect), "RuntimeLog/LogList", "LogList"),
            Field("DetailScrollRect", typeof(ScrollRect), typeof(ScrollRect), "RuntimeLog/DetailScrollRect", "DetailScrollRect"),
            Field("DetailText", typeof(TMP_Text), typeof(TextMeshProUGUI), "RuntimeLog/DetailScrollRect/Viewport/Content/DetailText", "DetailText"),
            Field("BtnClose", typeof(Button), typeof(Button), "RuntimeLog/BtnClose", "BtnClose"),
            Field("BtnClear", typeof(Button), typeof(Button), "RuntimeLog/BtnClear", "BtnClear"),
            Field("BtnCopy", typeof(Button), typeof(Button), "RuntimeLog/BtnCopy", "BtnCopy"),
            Field("BtnExport", typeof(Button), typeof(Button), "RuntimeLog/BtnExport", "BtnExport")
        };

        private static readonly FieldExpectation[] TestCmdFields =
        {
            Field("BtnClose", typeof(Button), typeof(Button), "TestCmd/BtnClose", "BtnClose"),
            Field("BtnExecute", typeof(Button), typeof(Button), "TestCmd/BtnExecute", "BtnExecute"),
            Field("InputCommand", typeof(TMP_InputField), typeof(TMP_InputField), "TestCmd/InputCommand", "InputCommand"),
            Field("CategoryList", typeof(LoopVerticalScrollRect), typeof(LoopVerticalScrollRect), "TestCmd/CategoryList", "CategoryList"),
            Field("EntryList", typeof(LoopVerticalScrollRect), typeof(LoopVerticalScrollRect), "TestCmd/EntryList", "EntryList"),
            Field("ResultScrollRect", typeof(ScrollRect), typeof(ScrollRect), "TestCmd/DetailsPanel/ResultScroll", "ResultScroll"),
            Field("ResultViewportRect", typeof(RectTransform), typeof(RectTransform), "TestCmd/DetailsPanel/ResultScroll", "ResultScroll"),
            Field("ResultContentRect", typeof(RectTransform), typeof(RectTransform), "TestCmd/DetailsPanel/ResultScroll/ResultContent", "ResultContent"),
            Field("TextResult", typeof(TMP_Text), typeof(TextMeshProUGUI), "TestCmd/DetailsPanel/ResultScroll/ResultContent/TextResult", "TextResult"),
            Field("TextDetails", typeof(TMP_Text), typeof(TextMeshProUGUI), "TestCmd/DetailsPanel/TextDetails", "TextDetails"),
            Field("TextUsage", typeof(TMP_Text), typeof(TextMeshProUGUI), "TestCmd/DetailsPanel/TextUsage", "TextUsage"),
            Field("TextExample", typeof(TMP_Text), typeof(TextMeshProUGUI), "TestCmd/DetailsPanel/TextExample", "TextExample")
        };

        private static FieldExpectation Field(string name, Type type, Type actualType, string path, string objectName)
        {
            return new FieldExpectation(name, type, actualType, path, objectName);
        }

        private readonly struct FieldExpectation
        {
            public FieldExpectation(string name, Type type, Type actualType, string path, string objectName)
            {
                Name = name;
                Type = type;
                ActualType = actualType;
                Path = path;
                ObjectName = objectName;
            }

            public string Name { get; }
            public Type Type { get; }
            public Type ActualType { get; }
            public string Path { get; }
            public string ObjectName { get; }
        }

        private sealed class MinimalRuntimeConfigProvider : IRuntimeConfigProvider
        {
            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = new MapAssetDefinition(id, id);
                error = null;
                return true;
            }

            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = Array.Empty<MapSpawnDefinition>();
                error = null;
                return true;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                value = default;
                error = "missing player";
                return false;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                value = default;
                error = "missing enemy";
                return false;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                value = default;
                error = "missing loot";
                return false;
            }

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
            {
                value = default;
                error = "missing extraction";
                return false;
            }

            public bool TryGetTestCmdCatalog(
                out IReadOnlyList<TestCmdCategoryDefinition> categories,
                out IReadOnlyList<TestCmdEntryDefinition> entries,
                out string error)
            {
                categories = Array.Empty<TestCmdCategoryDefinition>();
                entries = Array.Empty<TestCmdEntryDefinition>();
                error = null;
                return true;
            }
        }

        private sealed class MinimalLocalizationLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object
            {
                return path == LocalizationLanguages.LanguagesAddress
                    ? new TextAsset("{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"Chinese\"}]}") as T
                    : null;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
            {
                var asset = LoadAsset<T>(path);
                onLoaded?.Invoke(asset);
                return asset != null ? new ResourceLoadHandle(asset) : ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }

    }
}
