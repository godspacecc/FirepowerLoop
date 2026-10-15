using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class ReferenceCollectorAndUiSpawnerTests
    {
        [Test]
        public void ReferenceCollector_AddGetRemoveAndSort_WorksByKey()
        {
            var root = new GameObject("ReferenceCollectorTest");
            var first = new GameObject("BText");
            var second = new GameObject("AText");
            try
            {
                var collector = root.AddComponent<ReferenceCollector>();

                collector.Add("BText", first);
                collector.Add("AText", second);
                collector.Sort();
                collector.OnAfterDeserialize();

                Assert.AreSame(second, collector.GetObject("AText"));
                Assert.AreSame(first, collector.Get<GameObject>("BText"));
                Assert.That(collector.data[0].key, Is.EqualTo("AText"));

                collector.Remove("AText");
                collector.OnAfterDeserialize();

                Assert.IsNull(collector.GetObject("AText"));
                Assert.AreSame(first, collector.GetObject("BText"));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void ReferenceCollector_AddGameObject_DefaultsToGameObjectTypeName()
        {
            var root = new GameObject("ReferenceCollectorRoot");
            var buttonObject = new GameObject("ButtonObject", typeof(RectTransform), typeof(Button));
            try
            {
                var collector = root.AddComponent<ReferenceCollector>();

                collector.Add("ButtonObject", buttonObject);

                Assert.That(collector.data.Single().TypeName, Is.EqualTo(typeof(GameObject).FullName));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(buttonObject);
            }
        }

        [Test]
        public void ReferenceCollectorEditor_GameObjectTypeCandidates_StartWithGameObjectAndListActualComponents()
        {
            var plain = new GameObject("PlainObject");
            var ui = new GameObject("UiObject", typeof(RectTransform), typeof(Button), typeof(TextMeshProUGUI));
            try
            {
                var editorType = ResolveEditorType("Game.ReferenceCollectorEditor");
                var candidatesMethod = editorType.GetMethod(
                    "GetSelectableTypeNames",
                    BindingFlags.NonPublic | BindingFlags.Static);

                Assert.That(candidatesMethod, Is.Not.Null);

                var plainCandidates = ((string[])candidatesMethod.Invoke(null, new object[] { plain })).ToArray();
                Assert.That(plainCandidates, Is.EqualTo(new[] { typeof(GameObject).FullName, typeof(Transform).FullName }));
                Assert.That(plainCandidates.Distinct().Count(), Is.EqualTo(plainCandidates.Length));

                var uiCandidates = ((string[])candidatesMethod.Invoke(null, new object[] { ui })).ToArray();
                Assert.That(uiCandidates[0], Is.EqualTo(typeof(GameObject).FullName));
                Assert.That(uiCandidates, Does.Contain(typeof(RectTransform).FullName));
                Assert.That(uiCandidates, Does.Contain(typeof(Button).FullName));
                Assert.That(uiCandidates, Does.Contain(typeof(TextMeshProUGUI).FullName));
                Assert.That(uiCandidates, Does.Not.Contain(typeof(Transform).FullName));
                Assert.That(uiCandidates.Distinct().Count(), Is.EqualTo(uiCandidates.Length));
            }
            finally
            {
                Object.DestroyImmediate(plain);
                Object.DestroyImmediate(ui);
            }
        }

        [Test]
        public void ReferenceCollectorEditor_AddGameObject_DefaultsToGameObjectTypeName()
        {
            var host = new GameObject("CollectorHost");
            var child = new GameObject("DraggedObject", typeof(RectTransform), typeof(Button));
            try
            {
                var collector = host.AddComponent<ReferenceCollector>();
                var serializedObject = new SerializedObject(collector);
                var dataProperty = serializedObject.FindProperty("data");
                var editorType = ResolveEditorType("Game.ReferenceCollectorEditor");
                var addMethod = editorType.GetMethod("AddReference", BindingFlags.NonPublic | BindingFlags.Static);

                Assert.That(addMethod, Is.Not.Null);
                addMethod.Invoke(null, new object[] { dataProperty, "DraggedObject", child });
                serializedObject.ApplyModifiedProperties();

                Assert.That(collector.data.Single().TypeName, Is.EqualTo(typeof(GameObject).FullName));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(child);
            }
        }

        [Test]
        public void ReferenceCollectorEditor_DirectObjectTypesUseActualTypeAndClearWhenUnbound()
        {
            var root = new GameObject("ReferenceCollectorDirectType");
            var texture = new Texture2D(2, 2);
            var renderTexture = new RenderTexture(2, 2, 0);
            try
            {
                var collector = root.AddComponent<ReferenceCollector>();
                collector.data.Add(new ReferenceCollectorData { key = "Asset", gameObject = texture, TypeName = "stale" });
                var serialized = new SerializedObject(collector);
                var entry = serialized.FindProperty("data").GetArrayElementAtIndex(0);
                var objectProperty = entry.FindPropertyRelative("gameObject");
                var typeProperty = entry.FindPropertyRelative("TypeName");
                var editorType = ResolveEditorType("Game.ReferenceCollectorEditor");
                var syncMethod = editorType.GetMethod(
                    "SyncNonSelectableTypeName",
                    BindingFlags.NonPublic | BindingFlags.Static);

                Assert.That(syncMethod, Is.Not.Null);
                syncMethod.Invoke(null, new object[] { texture, typeProperty });
                serialized.ApplyModifiedProperties();
                Assert.That(collector.data.Single().TypeName, Is.EqualTo(typeof(Texture2D).FullName));
                Assert.That(collector.data.Single().gameObject, Is.SameAs(texture));

                serialized.Update();
                objectProperty.objectReferenceValue = renderTexture;
                syncMethod.Invoke(null, new object[] { renderTexture, typeProperty });
                serialized.ApplyModifiedProperties();
                Assert.That(collector.data.Single().TypeName, Is.EqualTo(typeof(RenderTexture).FullName));
                Assert.That(collector.data.Single().gameObject, Is.SameAs(renderTexture));

                serialized.Update();
                objectProperty.objectReferenceValue = null;
                syncMethod.Invoke(null, new object[] { null, typeProperty });
                serialized.ApplyModifiedProperties();
                Assert.That(collector.data.Single().TypeName, Is.Empty);
                Assert.That(collector.data.Single().gameObject, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(renderTexture);
            }
        }

        [Test]
        public void ReferenceCollectorEditor_ComponentSourceSelectionSynchronizesSerializedTypeAndSpawnerResolution()
        {
            var root = new GameObject("ReferenceCollectorComponentSource", typeof(Button), typeof(Image));
            var host = new GameObject("ReferenceCollectorHost");
            try
            {
                var button = root.GetComponent<Button>();
                var collector = host.AddComponent<ReferenceCollector>();
                collector.data.Add(new ReferenceCollectorData { key = "Target", gameObject = button, TypeName = "missing.type" });
                var serialized = new SerializedObject(collector);
                var entry = serialized.FindProperty("data").GetArrayElementAtIndex(0);
                var type = entry.FindPropertyRelative("TypeName");
                var editorType = ResolveEditorType("Game.ReferenceCollectorEditor");
                var candidatesMethod = editorType.GetMethod("GetSelectableTypeNames", BindingFlags.NonPublic | BindingFlags.Static);
                var selectMethod = editorType.GetMethod("ResolveSelectableTypeName", BindingFlags.NonPublic | BindingFlags.Static);
                var spawnerType = ResolveEditorType("Game.UICodeSpawner");
                var resolveMethod = spawnerType.GetMethod("ResolveReferenceObject", BindingFlags.NonPublic | BindingFlags.Static);

                var candidates = ((string[])candidatesMethod.Invoke(null, new object[] { root })).ToArray();
                Assert.That(candidates, Does.Contain(typeof(GameObject).FullName));
                Assert.That(candidates, Does.Contain(typeof(Button).FullName));
                Assert.That(candidates, Does.Contain(typeof(Image).FullName));

                type.stringValue = (string)selectMethod.Invoke(null, new object[] { button, type.stringValue });
                serialized.ApplyModifiedProperties();
                Assert.That(collector.data.Single().TypeName, Is.EqualTo(typeof(Button).FullName));

                serialized.Update();
                type.stringValue = typeof(Image).FullName;
                serialized.ApplyModifiedProperties();
                Assert.That(collector.data.Single().TypeName, Is.EqualTo(typeof(Image).FullName));
                Assert.That(resolveMethod.Invoke(null, new object[] { button, typeof(Image) }), Is.SameAs(root.GetComponent<Image>()));

                serialized.Update();
                entry.FindPropertyRelative("gameObject").objectReferenceValue = root;
                type.stringValue = (string)selectMethod.Invoke(null, new object[] { root, "missing.type" });
                serialized.ApplyModifiedProperties();
                Assert.That(collector.data.Single().TypeName, Is.EqualTo(typeof(GameObject).FullName));

                serialized.Update();
                entry.FindPropertyRelative("gameObject").objectReferenceValue = null;
                type.stringValue = (string)selectMethod.Invoke(null, new object[] { null, type.stringValue });
                serialized.ApplyModifiedProperties();
                Assert.That(collector.data.Single().TypeName, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void UiCodeSpawner_MapsUiPrefabPath_ToUiAndCtrlScriptPaths()
        {
            const string prefabPath = "Assets/Game/Prefabs/UI/Home/Start.prefab";
            var spawnerType = ResolveEditorType("Game.UICodeSpawner");

            var uiPathMethod = spawnerType.GetMethod("GetUiScriptAssetPath", BindingFlags.NonPublic | BindingFlags.Static);
            var ctrlPathMethod = spawnerType.GetMethod("GetUiCtrlScriptAssetPath", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(uiPathMethod, Is.Not.Null);
            Assert.That(ctrlPathMethod, Is.Not.Null);
            Assert.That((string)uiPathMethod.Invoke(null, new object[] { prefabPath }),
                Is.EqualTo("Assets/Game/Scripts/Controller/UI/Home/StartUI.cs"));
            Assert.That((string)ctrlPathMethod.Invoke(null, new object[] { prefabPath }),
                Is.EqualTo("Assets/Game/Scripts/Controller/UICtrl/Home/StartUICtrl.cs"));
        }

        [Test]
        public void UiCodeSpawner_GeneratedUiScript_ContainsFieldsAndUsings()
        {
            const string prefabPath = "Assets/Game/Prefabs/UI/Home/GeneratedUi.prefab";
            var absolutePath = Path.Combine(Application.dataPath, "Game/Scripts/Controller/UI/Home/GeneratedUiUI.cs");
            var root = new GameObject("GeneratedUi");
            var child = new GameObject("TitleText", typeof(Text));
            child.transform.SetParent(root.transform, false);

            try
            {
                var collector = root.AddComponent<ReferenceCollector>();
                collector.data.Add(new ReferenceCollectorData
                {
                    key = "TitleText",
                    gameObject = child,
                    TypeName = typeof(Text).FullName
                });
                collector.data.Add(new ReferenceCollectorData
                {
                    key = "MoveStick",
                    gameObject = child,
                    TypeName = typeof(UnityEngine.InputSystem.OnScreen.OnScreenStick).FullName
                });

                var spawnerType = ResolveEditorType("Game.UICodeSpawner");
                var writeUiMethod = spawnerType.GetMethod("WriteUiScript", BindingFlags.NonPublic | BindingFlags.Static);

                Assert.That(writeUiMethod, Is.Not.Null);
                writeUiMethod.Invoke(null, new object[] { root, prefabPath, collector });

                var generatedText = File.ReadAllText(absolutePath);
                Assert.That(generatedText, Does.Contain("using UnityEngine.UI;"));
                Assert.That(generatedText, Does.Contain("using UnityEngine.InputSystem.OnScreen;"));
                Assert.That(generatedText, Does.Contain("public Text TitleText;"));
                Assert.That(generatedText, Does.Contain("public OnScreenStick MoveStick;"));
                Assert.That(generatedText, Does.Not.Contain("partial"));
                Assert.That(generatedText, Does.Not.Contain(".Generated"));
            }
            finally
            {
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }

                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void UiCodeSpawner_GeneratedUiScript_PreservesGameObjectAndComponentFieldTypes()
        {
            const string prefabPath = "Assets/Game/Prefabs/UI/Home/GeneratedUi.prefab";
            var absolutePath = Path.Combine(Application.dataPath, "Game/Scripts/Controller/UI/Home/GeneratedUiUI.cs");
            var root = new GameObject("GeneratedUi");
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Button));
            panel.transform.SetParent(root.transform, false);

            try
            {
                var collector = root.AddComponent<ReferenceCollector>();
                collector.data.Add(new ReferenceCollectorData
                {
                    key = "PanelObject",
                    gameObject = panel,
                    TypeName = typeof(GameObject).FullName
                });
                collector.data.Add(new ReferenceCollectorData
                {
                    key = "PanelButton",
                    gameObject = panel,
                    TypeName = typeof(Button).FullName
                });

                var spawnerType = ResolveEditorType("Game.UICodeSpawner");
                var writeUiMethod = spawnerType.GetMethod("WriteUiScript", BindingFlags.NonPublic | BindingFlags.Static);
                var resolveMethod = spawnerType.GetMethod("ResolveReferenceObject", BindingFlags.NonPublic | BindingFlags.Static);

                Assert.That(writeUiMethod, Is.Not.Null);
                Assert.That(resolveMethod, Is.Not.Null);
                writeUiMethod.Invoke(null, new object[] { root, prefabPath, collector });

                var generatedText = File.ReadAllText(absolutePath);
                Assert.That(generatedText, Does.Contain("public GameObject PanelObject;"));
                Assert.That(generatedText, Does.Contain("public Button PanelButton;"));
                Assert.That(resolveMethod.Invoke(null, new object[] { panel, typeof(GameObject) }), Is.SameAs(panel));
                Assert.That(resolveMethod.Invoke(null, new object[] { panel, typeof(Button) }), Is.SameAs(panel.GetComponent<Button>()));
            }
            finally
            {
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }

                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void UiCodeSpawner_DoesNotOverwriteExistingController()
        {
            const string prefabPath = "Assets/Game/Prefabs/UI/Home/GeneratedUi.prefab";
            var absolutePath = Path.Combine(Application.dataPath, "Game/Scripts/Controller/UICtrl/Home/GeneratedUiUICtrl.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllText(absolutePath, "existing controller");

            try
            {
                var root = new GameObject("GeneratedUi");
                var spawnerType = ResolveEditorType("Game.UICodeSpawner");
                var writeCtrlMethod = spawnerType.GetMethod("WriteUiCtrlScript", BindingFlags.NonPublic | BindingFlags.Static);

                Assert.That(writeCtrlMethod, Is.Not.Null);
                writeCtrlMethod.Invoke(null, new object[] { root, prefabPath });

                Assert.That(File.ReadAllText(absolutePath), Is.EqualTo("existing controller"));
                Object.DestroyImmediate(root);
            }
            finally
            {
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }
            }
        }

        [Test]
        public void UiCodeSpawner_GeneratedController_UsesCollectorAddress()
        {
            const string prefabPath = "Assets/Game/Prefabs/UI/Home/GeneratedUi.prefab";
            var absolutePath = Path.Combine(Application.dataPath, "Game/Scripts/Controller/UICtrl/Home/GeneratedUiUICtrl.cs");
            var root = new GameObject("GeneratedUi");

            try
            {
                var spawnerType = ResolveEditorType("Game.UICodeSpawner");
                var writeMethod = spawnerType.GetMethod("WriteUiCtrlScript", BindingFlags.NonPublic | BindingFlags.Static);

                Assert.That(writeMethod, Is.Not.Null);
                writeMethod.Invoke(null, new object[] { root, prefabPath });

                var source = File.ReadAllText(absolutePath);
                Assert.That(source, Does.Contain("return \"Prefabs/UI/Home/GeneratedUi\";"));
                Assert.That(source, Does.Not.Contain("return \"Assets/Game/"));
            }
            finally
            {
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }

                Object.DestroyImmediate(root);
            }
        }

        private static System.Type ResolveEditorType(string typeName)
        {
            var type = System.Type.GetType($"{typeName}, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null, $"Unable to resolve {typeName} from editor assembly.");
            return type;
        }
    }
}
