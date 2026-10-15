using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class PageSpawnerTests
    {
        [Test]
        public void BasePageCtrl_Exists_InRuntimeAssembly()
        {
            Assert.That(Type.GetType("Game.BasePageCtrl, Game"), Is.Not.Null);
        }

        [Test]
        public void PageSpawner_MapsPrefabPath_ToPageUiAndCtrlPaths()
        {
            const string prefabPath = "Assets/Game/Prefabs/UI/Home/Page/Task.prefab";
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;

            var spawnerType = ResolveEditorType("Game.PageCodeSpawner");
            var uiPathMethod = spawnerType.GetMethod("GetPageUiScriptAssetPath", flags);
            var ctrlPathMethod = spawnerType.GetMethod("GetPageCtrlScriptAssetPath", flags);

            Assert.That(uiPathMethod, Is.Not.Null);
            Assert.That(ctrlPathMethod, Is.Not.Null);
            Assert.That((string)uiPathMethod.Invoke(null, new object[] { prefabPath }),
                Is.EqualTo("Assets/Game/Scripts/Controller/UI/Home/Page/TaskPageUI.cs"));
            Assert.That((string)ctrlPathMethod.Invoke(null, new object[] { prefabPath }),
                Is.EqualTo("Assets/Game/Scripts/Controller/UICtrl/Home/Page/TaskPageCtrl.cs"));
        }

        [Test]
        public void PageSpawner_GeneratedController_UsesEmbeddedPageContract()
        {
            const string prefabPath = "Assets/Game/Prefabs/UI/Home/Page/Task.prefab";
            var absolutePath = Path.Combine(Application.dataPath, "Game/Scripts/Controller/UICtrl/Home/Page/TaskPageCtrl.cs");
            var root = new GameObject("Task");

            try
            {
                var spawnerType = ResolveEditorType("Game.PageCodeSpawner");
                var writeMethod = spawnerType.GetMethod("WritePageCtrlScript", BindingFlags.NonPublic | BindingFlags.Static);

                Assert.That(writeMethod, Is.Not.Null);
                writeMethod.Invoke(null, new object[] { root, prefabPath });

                var source = File.ReadAllText(absolutePath);
                Assert.That(source, Does.Contain("class TaskPageCtrl : BasePageCtrl"));
                Assert.That(source, Does.Contain("private TaskPageUI UI => BaseUI as TaskPageUI;"));
                Assert.That(source, Does.Contain("protected override void OnInit()"));
                Assert.That(source, Does.Contain("protected override void OnShow()"));
                Assert.That(source, Does.Contain("protected override void OnHide()"));
                Assert.That(source, Does.Not.Contain("Path()"));
                Assert.That(source, Does.Not.Contain("Prefabs/UI/Home/Page/Task"));
                Assert.That(source, Does.Not.Contain("Layer()"));
            }
            finally
            {
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }

                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PageSpawner_ExposesPendingBindingWorkflow()
        {
            var type = ResolveEditorType("Game.PageCodeSpawner");
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
    }
}
