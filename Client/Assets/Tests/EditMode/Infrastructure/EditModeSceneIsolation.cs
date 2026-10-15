using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    [SetUpFixture]
    public sealed class EditModeSceneIsolationFixture
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            EditModeSceneIsolation.Enter();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            EditModeSceneIsolation.Exit();
        }
    }
}

namespace AnotherDay.Tests
{
    [SetUpFixture]
    public sealed class EditModeSceneIsolationFixture
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            EditModeSceneIsolation.Enter();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            EditModeSceneIsolation.Exit();
        }
    }
}

internal static class EditModeSceneIsolation
{
    private const string TempSceneDirectory = "Assets/Tests/TempSceneIsolation";

    private static SceneSetup[] previousSetup;
    private static string previousActivePath;
    private static Scene ownIsolationScene;
    private static bool createdOwnIsolationScene;
    private static bool entered;

    public static void Enter()
    {
        if (entered)
        {
            return;
        }

        ThrowIfDirtyProductionScenes();

        var active = SceneManager.GetActiveScene();
        if (IsUnsavedLoadedScene(active))
        {
            ownIsolationScene = active;
            createdOwnIsolationScene = false;
            entered = true;
            return;
        }

        previousSetup = EditorSceneManager.GetSceneManagerSetup();
        previousActivePath = active.IsValid() ? active.path : null;
        ownIsolationScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SceneManager.SetActiveScene(ownIsolationScene);
        createdOwnIsolationScene = true;
        entered = true;
    }

    public static void Exit()
    {
        if (!entered)
        {
            return;
        }

        var dirtyProductionScenes = LoadedScenes()
            .Where(IsProductionScene)
            .Where(scene => scene.isDirty)
            .Select(scene => scene.path)
            .ToArray();
        var cleanupErrors = new List<Exception>();

        try
        {
            EnsureSafeActiveUnsavedScene();
            foreach (var scene in LoadedScenes().Where(scene => IsTempScene(scene)).ToArray())
            {
                CloseScene(scene, cleanupErrors);
                DeleteTempSceneAsset(scene.path, cleanupErrors);
            }

            foreach (var scene in LoadedScenes()
                         .Where(IsProductionScene)
                         .Where(scene => scene.isDirty)
                         .ToArray())
            {
                CloseScene(scene, cleanupErrors);
            }
        }
        finally
        {
            if (createdOwnIsolationScene)
            {
                try
                {
                    RestorePreviousSetup();
                }
                catch (Exception exception)
                {
                    cleanupErrors.Add(exception);
                }
            }

            previousSetup = null;
            previousActivePath = null;
            ownIsolationScene = default;
            createdOwnIsolationScene = false;
            entered = false;
        }

        if (dirtyProductionScenes.Length > 0 || cleanupErrors.Count > 0)
        {
            var parts = new List<string>();
            if (dirtyProductionScenes.Length > 0)
            {
                parts.Add("EditMode tests modified production scenes: " + string.Join(", ", dirtyProductionScenes));
            }

            parts.AddRange(cleanupErrors.Select(error => error.Message));
            throw new AssertionException(string.Join(Environment.NewLine, parts));
        }
    }

    public static Scene RequireActiveTestScene()
    {
        var active = SceneManager.GetActiveScene();
        if (!IsUnsavedLoadedScene(active))
        {
            throw new InvalidOperationException(
                $"EditMode test objects must be created in an unsaved active scene. Active scene: '{active.path}'");
        }

        return active;
    }

    public static void DestroyNamedObjectsInActiveTestScene(params string[] names)
    {
        var scene = RequireActiveTestScene();
        var nameSet = new HashSet<string>(names ?? Array.Empty<string>(), StringComparer.Ordinal);
        if (nameSet.Count == 0)
        {
            return;
        }

        foreach (var root in scene.GetRootGameObjects().ToArray())
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true)
                         .Where(item => nameSet.Contains(item.name))
                         .ToArray())
            {
                if (transform.gameObject.scene == scene)
                {
                    Object.DestroyImmediate(transform.gameObject);
                }
            }
        }
    }

    public static T[] FindObjectsInActiveTestScene<T>(bool includeInactive = true)
        where T : Component
    {
        var scene = RequireActiveTestScene();
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(includeInactive))
            .Where(component => component != null && component.gameObject.scene == scene)
            .ToArray();
    }

    public static void DestroyIfInActiveTestScene(Object target)
    {
        if (target == null)
        {
            return;
        }

        var scene = RequireActiveTestScene();
        var gameObject = target switch
        {
            GameObject go => go,
            Component component => component.gameObject,
            _ => null
        };

        if (gameObject != null && gameObject.scene == scene)
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    public static Scene OpenBootSceneCopy(string sourceScenePath, out Scene sourceScene, out string tempScenePath)
    {
        ThrowIfDirtyProductionScenes();
        var safeActiveScene = RequireActiveTestScene();
        tempScenePath = $"{TempSceneDirectory}/BootCopy-{Guid.NewGuid():N}.unity";
        sourceScene = default;
        Scene copyScene = default;
        var openedSource = false;
        var createdTempAsset = false;
        var cleanupErrors = new List<Exception>();

        try
        {
            Directory.CreateDirectory(TempSceneDirectory);

            sourceScene = FindLoadedSceneByPath(sourceScenePath);
            openedSource = !sourceScene.IsValid() || !sourceScene.isLoaded;
            if (openedSource)
            {
                sourceScene = EditorSceneManager.OpenScene(sourceScenePath, OpenSceneMode.Additive);
            }

            if (sourceScene.isDirty)
            {
                throw new InvalidOperationException($"Boot source scene is dirty before copy: {sourceScene.path}");
            }

            if (!EditorSceneManager.SaveScene(sourceScene, tempScenePath, true))
            {
                throw new InvalidOperationException($"Failed to save Boot scene copy: {tempScenePath}");
            }

            createdTempAsset = true;
            if (sourceScene.isDirty)
            {
                throw new InvalidOperationException($"Boot source scene became dirty while copying: {sourceScene.path}");
            }

            if (openedSource)
            {
                CloseScene(sourceScene, cleanupErrors);
                if (cleanupErrors.Count > 0)
                {
                    throw new AggregateException("Failed to close source scene after creating Boot copy.", cleanupErrors);
                }

                openedSource = false;
            }

            copyScene = EditorSceneManager.OpenScene(tempScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(copyScene);
            return copyScene;
        }
        catch (Exception exception)
        {
            RollbackBootSceneCopy(
                safeActiveScene,
                copyScene,
                sourceScene,
                openedSource,
                tempScenePath,
                cleanupErrors);
            if (cleanupErrors.Count == 0)
            {
                throw;
            }

            throw new AggregateException(
                "Failed to open Boot scene copy and cleanup reported errors.",
                new[] { exception }.Concat(cleanupErrors));
        }
    }

    public static void CloseAndDeleteSceneCopy(Scene copyScene, string tempScenePath)
    {
        var cleanupErrors = new List<Exception>();
        var testScene = LoadedScenes().FirstOrDefault(IsUnsavedLoadedScene);
        if (testScene.IsValid())
        {
            SceneManager.SetActiveScene(testScene);
        }

        CloseScene(copyScene, cleanupErrors);
        DeleteTempSceneAsset(tempScenePath, cleanupErrors);
        DeleteTempDirectoryIfEmpty(cleanupErrors);

        if (cleanupErrors.Count > 0)
        {
            throw new AggregateException("Failed to close and delete Boot scene copy.", cleanupErrors);
        }
    }

    public static void ThrowIfDirtyProductionScenes()
    {
        var dirtyScenes = LoadedScenes()
            .Where(IsProductionScene)
            .Where(scene => scene.isDirty)
            .Select(scene => scene.path)
            .ToArray();
        if (dirtyScenes.Length > 0)
        {
            throw new InvalidOperationException(
                "Loaded production scenes are dirty: " + string.Join(", ", dirtyScenes));
        }
    }

    public static string[] LoadedProductionSceneStatesForReport()
    {
        return LoadedScenes()
            .Where(scene => !string.IsNullOrEmpty(scene.path))
            .Select(scene => $"{scene.path}:isDirty={scene.isDirty}")
            .ToArray();
    }

    private static void EnsureSafeActiveUnsavedScene()
    {
        var active = SceneManager.GetActiveScene();
        if (IsUnsavedLoadedScene(active))
        {
            return;
        }

        if (IsUnsavedLoadedScene(ownIsolationScene))
        {
            SceneManager.SetActiveScene(ownIsolationScene);
            return;
        }

        var existing = LoadedScenes().FirstOrDefault(IsUnsavedLoadedScene);
        if (existing.IsValid())
        {
            SceneManager.SetActiveScene(existing);
            return;
        }

        ownIsolationScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(ownIsolationScene);
        createdOwnIsolationScene = true;
    }

    private static void RestorePreviousSetup()
    {
        if (previousSetup == null || previousSetup.Length == 0)
        {
            return;
        }

        EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
        if (!string.IsNullOrEmpty(previousActivePath))
        {
            var previousActive = FindLoadedSceneByPath(previousActivePath);
            if (previousActive.IsValid() && previousActive.isLoaded)
            {
                SceneManager.SetActiveScene(previousActive);
            }
        }
    }

    private static void CloseScene(Scene scene, ICollection<Exception> errors)
    {
        try
        {
            if (scene.IsValid() && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
        catch (Exception exception)
        {
            errors?.Add(exception);
        }
    }

    private static void RollbackBootSceneCopy(
        Scene safeActiveScene,
        Scene copyScene,
        Scene sourceScene,
        bool openedSource,
        string tempScenePath,
        ICollection<Exception> errors)
    {
        if (safeActiveScene.IsValid() && safeActiveScene.isLoaded && string.IsNullOrEmpty(safeActiveScene.path))
        {
            SceneManager.SetActiveScene(safeActiveScene);
        }

        CloseScene(copyScene, errors);
        if (openedSource)
        {
            CloseScene(sourceScene, errors);
        }

        DeleteTempSceneAsset(tempScenePath, errors);
        DeleteTempDirectoryIfEmpty(errors);
    }

    private static void DeleteTempSceneAsset(string path, ICollection<Exception> errors)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith(TempSceneDirectory + "/", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var deleted = AssetDatabase.DeleteAsset(path);
            var fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            if (!deleted && File.Exists(fullPath))
            {
                throw new IOException($"Failed to delete temp scene asset: {path}");
            }
        }
        catch (Exception exception)
        {
            errors?.Add(exception);
        }
    }

    private static void DeleteTempDirectoryIfEmpty(ICollection<Exception> errors)
    {
        var fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", TempSceneDirectory));
        if (!Directory.Exists(fullPath) || Directory.GetFileSystemEntries(fullPath).Length != 0)
        {
            return;
        }

        try
        {
            if (AssetDatabase.IsValidFolder(TempSceneDirectory))
            {
                var deleted = AssetDatabase.DeleteAsset(TempSceneDirectory);
                if (!deleted && Directory.Exists(fullPath))
                {
                    throw new IOException($"Failed to delete temp scene directory: {TempSceneDirectory}");
                }
            }
            else
            {
                Directory.Delete(fullPath);
            }

            var metaPath = fullPath + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }
        catch (Exception exception)
        {
            errors?.Add(exception);
        }
    }

    private static Scene FindLoadedSceneByPath(string path)
    {
        return LoadedScenes().FirstOrDefault(scene => string.Equals(scene.path, path, StringComparison.Ordinal));
    }

    private static IEnumerable<Scene> LoadedScenes()
    {
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.IsValid() && scene.isLoaded)
            {
                yield return scene;
            }
        }
    }

    private static bool IsUnsavedLoadedScene(Scene scene)
    {
        return scene.IsValid() && scene.isLoaded && string.IsNullOrEmpty(scene.path);
    }

    private static bool IsProductionScene(Scene scene)
    {
        return scene.IsValid()
            && scene.isLoaded
            && !string.IsNullOrEmpty(scene.path)
            && !IsTempScene(scene);
    }

    private static bool IsTempScene(Scene scene)
    {
        return scene.IsValid()
            && scene.isLoaded
            && !string.IsNullOrEmpty(scene.path)
            && scene.path.StartsWith(TempSceneDirectory + "/", StringComparison.Ordinal);
    }
}
