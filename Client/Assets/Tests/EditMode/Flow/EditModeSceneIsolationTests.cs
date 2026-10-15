using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Boot;
using Boot.UI;
using Game;

namespace Game.Tests
{
    public sealed class EditModeSceneIsolationTests
    {
        private const string BootScenePath = "Assets/Game/Scenes/Boot.unity";
        private const string TempSceneDirectory = "Assets/Tests/TempSceneIsolation";

        [Test]
        public void ActiveSceneProvidedToTestsIsUnsaved()
        {
            var active = EditModeSceneIsolation.RequireActiveTestScene();

            Assert.That(active.IsValid(), Is.True);
            Assert.That(active.isLoaded, Is.True);
            Assert.That(active.path, Is.Empty);
        }

        [Test]
        public void DirtyProductionScenePreflightFailsWithoutChangingActiveScene()
        {
        var previous = SceneManager.GetActiveScene();
        Scene boot = default;
        try
        {
            boot = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(boot);
            EditorSceneManager.MarkSceneDirty(boot);

            var error = Assert.Throws<System.InvalidOperationException>(
                () => EditModeSceneIsolation.ThrowIfDirtyProductionScenes());
            Assert.That(error.Message, Does.Contain(BootScenePath));
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(boot));
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded)
            {
                SceneManager.SetActiveScene(previous);
            }

            if (boot.IsValid() && boot.isLoaded)
            {
                EditorSceneManager.CloseScene(boot, true);
            }
        }
    }

    [Test]
    public void ExitReportsAndClosesDirtyProductionSceneWithoutSaving()
    {
        var before = ReadAllBytes(BootScenePath);
        Scene boot = default;
        try
        {
            var active = EditModeSceneIsolation.RequireActiveTestScene();
            boot = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(boot);
            EditorSceneManager.MarkSceneDirty(boot);

            var error = Assert.Throws<AssertionException>(() => EditModeSceneIsolation.Exit());

            Assert.That(error.Message, Does.Contain(BootScenePath));
            Assert.That(boot.isLoaded, Is.False);
            Assert.That(ReadAllBytes(BootScenePath), Is.EqualTo(before));
            Assert.That(active.IsValid(), Is.True);
        }
        finally
        {
            try
            {
                if (boot.IsValid() && boot.isLoaded)
                {
                    EditorSceneManager.CloseScene(boot, true);
                }

                if (!ReadAllBytes(BootScenePath).SequenceEqual(before))
                {
                    throw new AssertionException("Boot scene bytes changed during dirty-scene exit regression.");
                }
            }
            finally
            {
                EditModeSceneIsolation.Enter();
            }
        }
    }

    [Test]
    public void OpenBootSceneCopyRollsBackWhenSourceSceneIsMissing()
    {
        var active = EditModeSceneIsolation.RequireActiveTestScene();
        Scene source;
        string tempPath;

        var error = Assert.Catch<System.Exception>(() =>
            EditModeSceneIsolation.OpenBootSceneCopy("Assets/Game/Scenes/__MissingBootForIsolation__.unity", out source, out tempPath));

        Assert.That(error.Message, Does.Contain("__MissingBootForIsolation__"));
        Assert.That(active.IsValid(), Is.True);
        Assert.That(active.isLoaded, Is.True);
        Assert.That(SceneManager.GetActiveScene().path, Is.Empty);
        Assert.That(Directory.Exists(FullAssetPath(TempSceneDirectory)), Is.False);
    }

    [Test]
    public void SingleSceneBootOpenCanReturnToUnsavedSceneWithoutChangingBootBytes()
    {
        var before = ReadAllBytes(BootScenePath);
        Scene boot = default;
        try
        {
            EditModeSceneIsolation.RequireActiveTestScene();
            boot = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Single);

            Assert.That(boot.IsValid(), Is.True);
            Assert.That(boot.isLoaded, Is.True);
            Assert.That(boot.path, Is.EqualTo(BootScenePath));
            Assert.That(boot.isDirty, Is.False);
            Assert.That(ReadAllBytes(BootScenePath), Is.EqualTo(before));
        }
        finally
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.That(SceneManager.GetActiveScene().path, Is.Empty);
            Assert.That(ReadAllBytes(BootScenePath), Is.EqualTo(before));
        }
    }

    [Test]
    public void DestroyNamedObjectsOnlyTouchesActiveUnsavedScene()
    {
        var testScene = EditModeSceneIsolation.RequireActiveTestScene();
        Scene boot = default;
        try
        {
            var testEventSystem = new GameObject("EventSystem", typeof(EventSystem));
            boot = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Additive);
            var bootEventSystems = boot.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<EventSystem>(true))
                .ToArray();
            Assert.That(bootEventSystems, Has.Length.EqualTo(1));

            EditModeSceneIsolation.DestroyNamedObjectsInActiveTestScene("EventSystem");

            Assert.That(testEventSystem == null, Is.True);
            Assert.That(bootEventSystems[0] == null, Is.False);
            Assert.That(boot.isDirty, Is.False);
            Assert.That(testScene.path, Is.Empty);
        }
        finally
        {
            if (boot.IsValid() && boot.isLoaded)
            {
                EditorSceneManager.CloseScene(boot, true);
            }
        }
    }

    [Test]
    public void UIHelperResetForTestsKeepsPathBackedTempSceneUIRoot()
    {
        Directory.CreateDirectory(TempSceneDirectory);
        var tempScenePath = $"{TempSceneDirectory}/UIRootProtection-{System.Guid.NewGuid():N}.unity";
        var active = EditModeSceneIsolation.RequireActiveTestScene();
        Scene tempScene = default;
        try
        {
            Assert.That(EditorSceneManager.SaveScene(active, tempScenePath, true), Is.True);
            tempScene = EditorSceneManager.OpenScene(tempScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(tempScene);
            var savedRoot = CreateUiRoot("SavedTempUIRoot");
            Assert.That(EditorSceneManager.SaveScene(tempScene, tempScenePath), Is.True);
            SceneManager.SetActiveScene(active);
            var unsavedRoot = CreateUiRoot("UnsavedUIRoot");

            UIHelper.ResetForTests();

            Assert.That(unsavedRoot == null, Is.True);
            Assert.That(savedRoot == null, Is.False);
            Assert.That(savedRoot.scene.path, Is.EqualTo(tempScenePath));
        }
        finally
        {
            if (active.IsValid() && active.isLoaded)
            {
                SceneManager.SetActiveScene(active);
            }

            if (tempScene.IsValid() && tempScene.isLoaded)
            {
                EditorSceneManager.CloseScene(tempScene, true);
            }

            AssetDatabase.DeleteAsset(tempScenePath);
            if (AssetDatabase.IsValidFolder(TempSceneDirectory))
            {
                var fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", TempSceneDirectory));
                if (Directory.Exists(fullPath) && Directory.GetFileSystemEntries(fullPath).Length == 0)
                {
                    AssetDatabase.DeleteAsset(TempSceneDirectory);
                }
            }
        }
    }

    [Test]
    public void BootCopyPreservesLauncherViewReferenceAndLeavesSourceClean()
    {
        Scene copy = EditModeSceneIsolation.OpenBootSceneCopy(BootScenePath, out _, out var tempScenePath);
        try
        {
            var launcher = copy.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Launcher>(true))
                .Single();
            var view = copy.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<HotUpdateView>(true))
                .Single();
            var serialized = new SerializedObject(launcher);

            Assert.That(serialized.FindProperty("_updateView").objectReferenceValue, Is.SameAs(view));
            Assert.That(copy.path, Is.EqualTo(tempScenePath));
            Assert.That(File.Exists(Path.Combine(Application.dataPath, "..", tempScenePath)), Is.True);
            Assert.That(string.Join("|", EditModeSceneIsolation.LoadedProductionSceneStatesForReport()), Does.Not.Contain("isDirty=True"));
        }
        finally
        {
            EditModeSceneIsolation.CloseAndDeleteSceneCopy(copy, tempScenePath);
        }
    }

        private static GameObject CreateUiRoot(string name)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(UIRoot));
            foreach (var layerName in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var layer = new GameObject(layerName, typeof(RectTransform));
                layer.transform.SetParent(root.transform, false);
            }

            var inputModuleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            Assert.That(inputModuleType, Is.Not.Null);
            new GameObject("EventSystem", typeof(EventSystem), inputModuleType).transform.SetParent(root.transform, false);
            return root;
        }

        private static byte[] ReadAllBytes(string assetPath)
        {
            return File.ReadAllBytes(FullAssetPath(assetPath));
        }

        private static string FullAssetPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }
    }
}

namespace AnotherDay.Tests.EditMode.Flow
{
    public sealed class EditModeSceneIsolationNamespaceCoverageTests
    {
        [Test]
        public void AnotherDayNamespaceUsesActiveUnsavedIsolationScene()
        {
            var active = EditModeSceneIsolation.RequireActiveTestScene();

            Assert.That(active.IsValid(), Is.True);
            Assert.That(active.isLoaded, Is.True);
            Assert.That(active.path, Is.Empty);
        }
    }
}
