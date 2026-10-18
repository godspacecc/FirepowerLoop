using System;
using System.Collections.Generic;
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
    public sealed class SimpleBoxUiGenerationMigrationTests
    {
        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            UIHelper.ResetForTests();
            ResourceLoader.SetLoaderForTests(new MinimalLocalizationLoader());
            LocalizationLanguages.ResetForTests();
        }

        [TearDown]
        public void TearDown()
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
        }

        [Test]
        public void SimpleBoxPrefabs_HaveCollectorGeneratedViewAndStableBindings()
        {
            AssertPrefab<RuntimeLogItemBoxUI>(RuntimeLogItemBoxPath, RuntimeLogItemFields);
            AssertPrefab<HotKeyItemBoxUI>(HotKeyItemBoxPath, HotKeyItemFields);
        }

        [Test]
        public void HostPrefabs_KeepItemTemplateReferences()
        {
            var runtimeLog = LoadPrefab<RuntimeLogUI>("Assets/Game/Prefabs/UI/Diagnostics/RuntimeLog.prefab");
            var setting = LoadPrefab<SettingUI>("Assets/Game/Prefabs/UI/Home/Setting.prefab");
            var controls = setting.SettingControlsRoot.GetComponent<SettingControlsPageUI>();

            Assert.That(AssetDatabase.GetAssetPath(runtimeLog.LogList.prefabSource.prefab), Is.EqualTo(RuntimeLogItemBoxPath));
            Assert.That(AssetDatabase.GetAssetPath(controls.HotKeyList.itemPrefab), Is.EqualTo(HotKeyItemBoxPath));
        }

        [Test]
        public void FloatPrefab_UsesEmbeddedTemplateAndGeneratedBindings()
        {
            const string floatPath = "Assets/Game/Prefabs/UI/Notify/Float.prefab";
            var host = LoadPrefab<FloatUI>(floatPath);
            var collector = host.GetComponent<ReferenceCollector>();
            var components = host.GetComponentsInChildren<Component>(true);

            Assert.That(host.TipsRoot, Is.Not.Null);
            Assert.That(host.TipTemplate, Is.Not.Null);
            Assert.That(host.TipTemplate.TextTips, Is.Not.Null);
            Assert.That(host.TipTemplate.transform.parent, Is.EqualTo(host.TipsRoot));
            Assert.That(host.TipTemplate.gameObject.activeSelf, Is.False);
            Assert.That(AssetDatabase.GetAssetPath(host.TipTemplate.gameObject), Is.EqualTo(floatPath));
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(host.TipTemplate.gameObject), Is.Null);
            Assert.That(collector, Is.Not.Null);
            Assert.That(collector.data.Select(item => $"{item.key}:{item.TypeName}"), Is.EqualTo(new[]
            {
                "TipsRoot:UnityEngine.RectTransform",
                "TipTemplate:Game.FloatTextBoxUI"
            }));
            Assert.That(components, Has.None.Null);
            Assert.That(components.Select(component => component.GetType().Name), Does.Not.Contain("FloatTipList"));
        }

        [Test]
        public void SimpleBoxPrefabs_KeepFixedLayoutValues()
        {
            AssertLayout(RuntimeLogItemBoxPath, false, new Vector2(812f, 36f), new Vector2(812f, 36f));
            AssertLayout(HotKeyItemBoxPath, true, new Vector2(864f, 82f), new Vector2(-1f, 82f));
        }

        [Test]
        public void RuntimeLogRefresh_RepaintsAllTextsOnReusedRow()
        {
            var ctrl = new RuntimeLogUICtrl();
            var row = CreateRuntimeLogRow();
            var first = new RuntimeLogEntry(DateTime.Parse("2026-08-21T01:02:03.004Z").ToUniversalTime(), LogType.Warning, "A message", "A stack");
            var second = new RuntimeLogEntry(DateTime.Parse("2026-08-21T05:06:07.008Z").ToUniversalTime(), LogType.Error, "B message", "B stack");

            SetField(ctrl, "visibleEntries", new[] { first });
            Invoke(ctrl, "OnLogItemRefresh", row.transform, 0);
            SetField(ctrl, "visibleEntries", new[] { second });
            Invoke(ctrl, "OnLogItemRefresh", row.transform, 0);

            Assert.That(row.TextTime.text, Is.EqualTo(second.Timestamp.ToString("HH:mm:ss.fff")));
            Assert.That(row.TextType.text, Is.EqualTo(LogType.Error.ToString()));
            Assert.That(row.TextMessage.text, Is.EqualTo("B message"));
            Object.DestroyImmediate(row.gameObject);
        }

        [Test]
        public void HotKeyRefresh_ClickSubmitsOnlyCurrentRebindActionOnReusedRow()
        {
            CreateTestUIRoot();
            UIHelper.SetPrefabLoaderForTests(path => path == "Prefabs/UI/Notify/Float" ? CreateFloatPrefab() : null);
            var ctrl = new SettingControlsPageCtrl();
            var row = CreateHotKeyRow();
            var first = new HotKeyBinding
            {
                ActionId = HotKeyActionIds.TogglePause,
                Context = HotKeyContext.UI,
                BindingPath = "<Keyboard>/escape"
            };
            var second = new HotKeyBinding
            {
                ActionId = HotKeyActionIds.ToggleRuntimeLog,
                Context = HotKeyContext.Global,
                BindingPath = "<Keyboard>/f1"
            };

            SetField(ctrl, "bindings", new[] { first });
            Invoke(ctrl, "OnRefreshItem", row.transform, 0);

            SetField(ctrl, "bindings", new[] { second });
            Invoke(ctrl, "OnRefreshItem", row.transform, 0);

            Assert.That(row.ActionNameText.text, Is.EqualTo(InvokeStatic<string>(typeof(SettingControlsPageCtrl), "FormatAction", second.ActionId)));
            Assert.That(row.BindingText.text, Is.EqualTo(HotKeyDisplayFormatter.FormatPath(second.BindingPath)));
            Assert.That(row.RebindButton.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo(Texts.SettingsRebind));

            row.RebindButton.onClick.Invoke();

            var hotKeySystem = Game.Interface.GetSystem<HotKeySystem>();
            Assert.That(hotKeySystem.IsCapturingRebind, Is.True);
            Assert.That(hotKeySystem.CapturingActionId, Is.EqualTo(second.ActionId));
            Assert.That(hotKeySystem.CapturingActionId, Is.Not.EqualTo(first.ActionId));
            Object.DestroyImmediate(row.gameObject);
        }

        private static void AssertPrefab<T>(string path, FieldExpectation[] expectedFields) where T : BaseBoxUI
        {
            var prefab = LoadPrefab<T>(path).gameObject;
            var collectors = prefab.GetComponents<ReferenceCollector>();
            Assert.That(collectors.Length, Is.EqualTo(1), path);
            var collector = collectors[0];
            var ui = prefab.GetComponent<T>();
            Assert.That(ui, Is.Not.Null, path);

            var fields = typeof(T).GetFields(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance);
            Assert.That(fields.Select(item => item.Name), Is.EqualTo(expectedFields.Select(item => item.Name)), path);
            Assert.That(fields.Select(item => item.FieldType), Is.EqualTo(expectedFields.Select(item => item.DeclaredType)), path);
            Assert.That(collector.data.Select(item => item.key), Is.EqualTo(expectedFields.Select(item => item.Name)), path);
            Assert.That(collector.data.Select(item => item.TypeName), Is.EqualTo(expectedFields.Select(item => item.ActualType.FullName)), path);
            Assert.That(typeof(T).GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static), Is.Empty);
            Assert.That(typeof(T).GetInterfaces(), Is.Empty);

            foreach (var expected in expectedFields)
            {
                var field = fields.Single(item => item.Name == expected.Name);
                var viewValue = field.GetValue(ui) as Object;
                var collected = collector.data.Single(item => item.key == expected.Name);
                var collectedValue = ResolveReferenceObject(collected.gameObject, field.FieldType);

                Assert.That(viewValue, Is.Not.Null, expected.Name);
                Assert.That(collected.gameObject, Is.Not.Null, expected.Name);
                Assert.That(collectedValue, Is.Not.Null, expected.Name);
                Assert.That(field.FieldType.IsInstanceOfType(viewValue), Is.True, expected.Name);
                Assert.That(viewValue.GetType(), Is.EqualTo(expected.ActualType), expected.Name);
                Assert.That(PathOf(prefab.transform, viewValue), Is.EqualTo(expected.Path), expected.Name);
                Assert.That(ObjectName(viewValue), Is.EqualTo(expected.ObjectName), expected.Name);
                Assert.That(ReferenceEquals(viewValue, collectedValue), Is.True, expected.Name);
            }
        }

        private static void AssertLayout(string path, bool active, Vector2 size, Vector2? preferred)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            Assert.That(prefab.activeSelf, Is.EqualTo(active), path);
            Assert.That(((RectTransform)prefab.transform).sizeDelta, Is.EqualTo(size), path);
            var layout = prefab.GetComponent<LayoutElement>();
            if (preferred == null)
            {
                Assert.That(layout, Is.Null, path);
                return;
            }

            Assert.That(layout, Is.Not.Null, path);
            Assert.That(layout.preferredWidth, Is.EqualTo(preferred.Value.x), path);
            Assert.That(layout.preferredHeight, Is.EqualTo(preferred.Value.y), path);
        }

        private static RuntimeLogItemBoxUI CreateRuntimeLogRow()
        {
            var root = new GameObject("RuntimeLogItemBox", typeof(RectTransform));
            var row = root.AddComponent<RuntimeLogItemBoxUI>();
            row.TextType = CreateText(root.transform, "TextType");
            row.TextMessage = CreateText(root.transform, "TextMessage");
            row.TextTime = CreateText(root.transform, "TextTime");
            return row;
        }

        private static HotKeyItemBoxUI CreateHotKeyRow()
        {
            var root = new GameObject("HotKeyItemBox", typeof(RectTransform));
            var row = root.AddComponent<HotKeyItemBoxUI>();
            row.ActionNameText = CreateText(root.transform, "ActionNameText");
            row.BindingText = CreateText(root.transform, "BindingText");
            row.RebindButton = CreateButton(root.transform, "RebindButton", out _);
            return row;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name)
        {
            var text = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(parent, false);
            return text;
        }

        private static Button CreateButton(Transform parent, string name, out TMP_Text label)
        {
            var button = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            button.transform.SetParent(parent, false);
            label = CreateText(button.transform, "Text");
            return button;
        }

        private static void AssertButtonClickBinding(Button button, object expectedTarget, string expectedMethodName, params object[] expectedArgs)
        {
            var eventCtrl = button.GetComponent<ButtonClickEventCtrl>();
            Assert.That(eventCtrl, Is.Not.Null, button.name);
            Assert.That(eventCtrl.Args, Is.EqualTo(expectedArgs), button.name);

            var callbacks = GetEasyEventCallbacks(eventCtrl.OnClickEvent);
            var productionCallbacks = callbacks
                .Where(item => ReferenceEquals(item.Target, expectedTarget))
                .ToArray();
            Assert.That(productionCallbacks.Length, Is.EqualTo(1), button.name);
            Assert.That(productionCallbacks[0].Method.Name, Is.EqualTo(expectedMethodName), button.name);
        }

        private static Delegate[] GetEasyEventCallbacks<T>(EasyEvent<T> easyEvent)
        {
            var handler = typeof(EasyEvent<T>)
                .GetField("mOnEvent", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(easyEvent) as Delegate;
            Assert.That(handler, Is.Not.Null);
            return handler.GetInvocationList();
        }

        private static T LoadPrefab<T>(string path) where T : Component
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            var component = prefab.GetComponent<T>();
            Assert.That(component, Is.Not.Null, path);
            return component;
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

        private static string PathOf(Transform root, Object value)
        {
            var transform = value switch
            {
                GameObject gameObject => gameObject.transform,
                Component component => component.transform,
                _ => null
            };
            Assert.That(transform, Is.Not.Null);
            var parts = new List<string>();
            while (transform != null)
            {
                parts.Add(transform.name);
                if (transform == root)
                {
                    break;
                }

                transform = transform.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string ObjectName(Object value)
        {
            return value is Component component ? component.name : value.name;
        }

        private static void Invoke(object target, string method, params object[] args)
        {
            target.GetType()
                .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(target, args);
        }

        private static T InvokeStatic<T>(Type type, string method, params object[] args)
        {
            return (T)type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, args);
        }

        private static void SetField(object target, string field, object value)
        {
            target.GetType()
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }

        private static FieldExpectation F(string name, Type declaredType, Type actualType, string path, string objectName, string globalObjectId)
        {
            return new FieldExpectation(name, declaredType, actualType, path, objectName, globalObjectId);
        }

        private readonly struct FieldExpectation
        {
            public FieldExpectation(string name, Type declaredType, Type actualType, string path, string objectName, string globalObjectId)
            {
                Name = name;
                DeclaredType = declaredType;
                ActualType = actualType;
                Path = path;
                ObjectName = objectName;
                GlobalObjectId = globalObjectId;
            }

            public string Name { get; }
            public Type DeclaredType { get; }
            public Type ActualType { get; }
            public string Path { get; }
            public string ObjectName { get; }
            public string GlobalObjectId { get; }
        }

        private static void CreateTestUIRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.AddComponent<UIRoot>();
            CreateRect(root.transform, "HUD");
            CreateRect(root.transform, "Dialog");
            CreateRect(root.transform, "Popup");
            CreateRect(root.transform, "Tips");
            CreateRect(root.transform, "Top");

            var eventSystem = new GameObject("EventSystem", typeof(RectTransform), typeof(EventSystem));
            eventSystem.transform.SetParent(root.transform, false);
            var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            Assert.That(inputModuleType, Is.Not.Null);
            eventSystem.AddComponent(inputModuleType);
            new GameObject(SceneRoot.PoolRootName);
        }

        private static GameObject CreateFloatPrefab()
        {
            var root = CreateRoot("Float", out var ui);
            var floatUi = root.AddComponent<FloatUI>();
            floatUi.TipsRoot = CreateRect(root.transform, "Tips");
            var template = new GameObject("TipTemplate", typeof(RectTransform)).AddComponent<FloatTextBoxUI>();
            template.transform.SetParent(floatUi.TipsRoot, false);
            template.TextTips = (TextMeshProUGUI)CreateText(template.transform, "Text");
            template.gameObject.SetActive(false);
            floatUi.TipTemplate = template;
            Object.DestroyImmediate(ui);
            return root;
        }

        private static GameObject CreateRoot(string name, out BaseUI ui)
        {
            var root = new GameObject(name, typeof(RectTransform));
            ui = root.AddComponent<BaseUI>();
            return root;
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
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

        private sealed class MinimalLocalizationLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object
            {
                switch (path)
                {
                    case LocalizationLanguages.LanguagesAddress:
                        return new TextAsset("{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"Chinese\"}]}") as T;
                    case "zh-CN.json":
                        return new TextAsset("{}") as T;
                    default:
                        return null;
                }
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

        private const string RuntimeLogItemBoxPath = "Assets/Game/Prefabs/UI/Diagnostics/Box/RuntimeLogItemBox.prefab";
        private const string HotKeyItemBoxPath = "Assets/Game/Prefabs/UI/Home/Box/HotKeyItemBox.prefab";

        private static readonly FieldExpectation[] RuntimeLogItemFields =
        {
            F("TextType", typeof(TMP_Text), typeof(TextMeshProUGUI), "RuntimeLogItemBox/TextType", "TextType", "GlobalObjectId_V1-1-a04661945b1ca7840b03bce84fa808ad-2560526705896775976-0"),
            F("TextMessage", typeof(TMP_Text), typeof(TextMeshProUGUI), "RuntimeLogItemBox/TextMessage", "TextMessage", "GlobalObjectId_V1-1-a04661945b1ca7840b03bce84fa808ad-5793312241504373580-0"),
            F("TextTime", typeof(TMP_Text), typeof(TextMeshProUGUI), "RuntimeLogItemBox/TextTime", "TextTime", "GlobalObjectId_V1-1-a04661945b1ca7840b03bce84fa808ad-1976265977297672779-0")
        };

        private static readonly FieldExpectation[] HotKeyItemFields =
        {
            F("ActionNameText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "HotKeyItemBox/ActionNameText", "ActionNameText", "GlobalObjectId_V1-1-33cac0bbdb1427947ac764abdfaca0dc-6472032488254085280-0"),
            F("BindingText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "HotKeyItemBox/BindingText", "BindingText", "GlobalObjectId_V1-1-33cac0bbdb1427947ac764abdfaca0dc-5700933094101811989-0"),
            F("RebindButton", typeof(Button), typeof(Button), "HotKeyItemBox/RebindButton", "RebindButton", "GlobalObjectId_V1-1-33cac0bbdb1427947ac764abdfaca0dc-7226248874544816665-0")
        };

    }
}
