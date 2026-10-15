using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using Boot;
using YooAsset;

namespace Game.Tests
{
    [TestFixture]
    public class FoundationBootSmokeTests
    {
        private const float TIMEOUT_SECONDS = 30f;
        private const string BOOT_SCENE_PATH = "Assets/Game/Scenes/Boot.unity";
        private const string MAIN_SCENE_NAME = "Main";

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return CleanupRuntimeObjects();

            var loadOperation = SceneManager.LoadSceneAsync(BOOT_SCENE_PATH, LoadSceneMode.Single);
            Assert.That(loadOperation, Is.Not.Null, "Boot scene load operation should not be null.");

            float startTime = Time.realtimeSinceStartup;
            while (!loadOperation.isDone)
            {
                if (Time.realtimeSinceStartup - startTime > TIMEOUT_SECONDS)
                {
                    Assert.Fail($"Loading Boot scene timed out after {TIMEOUT_SECONDS} seconds.");
                    yield break;
                }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator BootEntersMainWithinTimeout()
        {
            float startTime = Time.realtimeSinceStartup;
            string lastError = null;

            while (Time.realtimeSinceStartup - startTime < TIMEOUT_SECONDS)
            {
                var activeScene = SceneManager.GetActiveScene();
                if (activeScene.name == MAIN_SCENE_NAME)
                {
                    Assert.Pass($"Entered Main scene in {Time.realtimeSinceStartup - startTime:F1} seconds.");
                    yield break;
                }

                var resourceBoot = UnityEngine.Object.FindObjectOfType<ResourceBoot>();
                if (resourceBoot != null && !string.IsNullOrEmpty(resourceBoot.LastError))
                    lastError = resourceBoot.LastError;

                yield return new WaitForSeconds(0.5f);
            }

            string message = $"Boot startup timed out after {TIMEOUT_SECONDS} seconds; Main scene was not loaded.";
            if (!string.IsNullOrEmpty(lastError))
                message += $"\nResourceBoot last error: {lastError}";
            Assert.Fail(message);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return CleanupRuntimeObjects();
        }

        private static IEnumerator CleanupRuntimeObjects()
        {
            ResourceSceneLoader.ReleaseCurrent();

            ResourcePackage package = null;
            try
            {
                package = YooAssets.TryGetPackage("DefaultPackage");
            }
            catch (System.Exception)
            {
                // YooAssets throws if it has not been initialized yet; cleanup should stay idempotent.
            }

            if (package != null)
            {
                var destroyOperation = package.DestroyAsync();
                while (!destroyOperation.IsDone)
                    yield return null;
                YooAssets.RemovePackage(package);
            }

            var launchers = UnityEngine.Object.FindObjectsOfType<Launcher>();
            foreach (var launcher in launchers)
                UnityEngine.Object.DestroyImmediate(launcher.gameObject);

            yield return null;
        }
    }
}
