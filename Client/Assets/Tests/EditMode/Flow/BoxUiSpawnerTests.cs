using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class BoxUiSpawnerTests
    {
        private const string TempBoxDirectory = "Assets/Game/Prefabs/UI/Home/Box/__BoxUiSpawnerTests";
        private const string TempScriptDirectory = "Assets/Game/Scripts/Controller/UI/Home/Box/__BoxUiSpawnerTests";
        private const string PendingPrefabPathKey = "FirepowerLoop.EditorTools.PendingBoxUiPrefabPath";
        private const string PendingAssemblyTypeKey = "FirepowerLoop.EditorTools.PendingBoxUiAssemblyType";

        [TearDown]
        public void TearDown()
        {
            EditorPrefs.DeleteKey(PendingPrefabPathKey);
            EditorPrefs.DeleteKey(PendingAssemblyTypeKey);
            if (AssetDatabase.IsValidFolder(TempBoxDirectory))
            {
                AssetDatabase.DeleteAsset(TempBoxDirectory);
            }

            if (AssetDatabase.IsValidFolder(TempScriptDirectory))
            {
                AssetDatabase.DeleteAsset(TempScriptDirectory);
            }

            AssetDatabase.Refresh();
        }

        [Test]
        public void BaseBoxUi_Exists_InRuntimeAssembly()
        {
            Assert.That(Type.GetType("Game.BaseBoxUI, Game"), Is.Not.Null);
        }

        [Test]
        public void BaseBoxUi_IsAViewMarkerWithoutGenericRuntimeState()
        {
            var type = typeof(BaseBoxUI);

            Assert.That(type.GetProperty("IsEventRegistered"), Is.Null);
            Assert.That(type.GetMethod("SetData"), Is.Null);
        }

        [Test]
        public void BoxUiSpawner_MapsPrefabPath_ToBoxUiScriptPath()
        {
            const string prefabPath = "Assets/Game/Prefabs/UI/Home/Box/RewardItem.prefab";
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;

            var spawnerType = ResolveEditorType("Game.BoxUICodeSpawner");
            var pathMethod = GetStaticMethod(spawnerType, "GetBoxUiScriptAssetPath", typeof(string));

            Assert.That(pathMethod, Is.Not.Null);
            Assert.That((string)pathMethod.Invoke(null, new object[] { prefabPath }),
                Is.EqualTo("Assets/Game/Scripts/Controller/UI/Home/Box/RewardItemBoxUI.cs"));
        }

        [Test]
        public void BoxUiSpawner_BoxSuffixedPrefab_UsesUiSuffixOnly()
        {
            const string prefabPath = "Assets/Game/Prefabs/UI/Home/Box/RuntimeLogItemBox.prefab";
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;

            var spawnerType = ResolveEditorType("Game.BoxUICodeSpawner");
            var pathMethod = GetStaticMethod(spawnerType, "GetBoxUiScriptAssetPath", typeof(string));
            var typeMethod = GetStaticMethod(spawnerType, "GetBoxUiAssemblyType", typeof(string));

            Assert.That(pathMethod, Is.Not.Null);
            Assert.That(typeMethod, Is.Not.Null);
            Assert.That((string)pathMethod.Invoke(null, new object[] { prefabPath }),
                Is.EqualTo("Assets/Game/Scripts/Controller/UI/Home/Box/RuntimeLogItemBoxUI.cs"));
            Assert.That((string)typeMethod.Invoke(null, new object[] { "RuntimeLogItemBox" }),
                Is.EqualTo("Game.RuntimeLogItemBoxUI"));
        }

        [Test]
        public void BoxUiSpawner_NonBoxSuffixedPrefab_UsesBoxUiSuffix()
        {
            const string prefabPath = "Assets/Game/Prefabs/UI/Home/Box/OnlineRoomMember.prefab";
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;

            var spawnerType = ResolveEditorType("Game.BoxUICodeSpawner");
            var pathMethod = GetStaticMethod(spawnerType, "GetBoxUiScriptAssetPath", typeof(string));
            var typeMethod = GetStaticMethod(spawnerType, "GetBoxUiAssemblyType", typeof(string));

            Assert.That(pathMethod, Is.Not.Null);
            Assert.That(typeMethod, Is.Not.Null);
            Assert.That((string)pathMethod.Invoke(null, new object[] { prefabPath }),
                Is.EqualTo("Assets/Game/Scripts/Controller/UI/Home/Box/OnlineRoomMemberBoxUI.cs"));
            Assert.That((string)typeMethod.Invoke(null, new object[] { "OnlineRoomMember" }),
                Is.EqualTo("Game.OnlineRoomMemberBoxUI"));
        }

        [Test]
        public void BoxUiSpawner_NameMismatch_StopsWithoutScriptPendingBindingOrComponentChange()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            EditorPrefs.DeleteKey(PendingPrefabPathKey);
            EditorPrefs.DeleteKey(PendingAssemblyTypeKey);
            EnsureTempFolder();

            var root = new GameObject("DifferentRoot");
            try
            {
                root.AddComponent<ReferenceCollector>();
                var prefabPath = $"{TempBoxDirectory}/MismatchPrefab.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Object.DestroyImmediate(root);
                root = null;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                prefab.name = "DifferentRoot";
                var beforeComponents = prefab.GetComponents<Component>().Length;
                var generatedScript = $"{TempScriptDirectory}/MismatchPrefabBoxUI.cs";
                Assert.That(prefab.name, Is.EqualTo("DifferentRoot"));
                Assert.That(File.Exists(ToAbsolute(generatedScript)), Is.False);
                Assert.That(File.Exists(ToAbsolute(generatedScript + ".meta")), Is.False);

                LogAssert.Expect(LogType.Error,
                    "[BoxUICodeSpawner] Prefab 文件名必须与根 GameObject 名完全一致：MismatchPrefab != DifferentRoot");
                ResolveEditorType("Game.BoxUICodeSpawner")
                    .GetMethod("AutoUpdateBoxUI", flags)
                    ?.Invoke(null, new object[] { prefab });

                var reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.That(File.Exists(ToAbsolute(generatedScript)), Is.False);
                Assert.That(File.Exists(ToAbsolute(generatedScript + ".meta")), Is.False);
                Assert.That(EditorPrefs.HasKey(PendingPrefabPathKey), Is.False);
                Assert.That(EditorPrefs.HasKey(PendingAssemblyTypeKey), Is.False);
                Assert.That(reloaded.GetComponents<Component>().Length, Is.EqualTo(beforeComponents));
            }
            finally
            {
                if (root != null)
                {
                    Object.DestroyImmediate(root);
                }
            }
        }

        [Test]
        public void BoxUiSpawner_ExposesPendingBindingWorkflow()
        {
            var type = ResolveEditorType("Game.BoxUICodeSpawner");
            foreach (var methodName in new[] { "SetPendingBinding", "ProcessPendingBindingsWhenReady", "BindGeneratedComponent" })
            {
                Assert.That(type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static), Is.Not.Null, methodName);
            }
        }

        private static Type ResolveEditorType(string typeName)
        {
            var type = Type.GetType($"{typeName}, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null, $"Unable to resolve {typeName} from editor assembly.");
            return type;
        }

        private static MethodInfo GetStaticMethod(Type type, string name, params Type[] parameterTypes)
        {
            return type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static, null, parameterTypes, null);
        }

        private static void EnsureTempFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Game/Prefabs/UI/Home/Box"))
            {
                Assert.Fail("Missing Box prefab folder.");
            }

            if (!AssetDatabase.IsValidFolder(TempBoxDirectory))
            {
                AssetDatabase.CreateFolder("Assets/Game/Prefabs/UI/Home/Box", "__BoxUiSpawnerTests");
            }
        }

        private static string ToAbsolute(string assetPath)
        {
            return assetPath.Replace("Assets", Application.dataPath).Replace("/", Path.DirectorySeparatorChar.ToString());
        }

    }
}
