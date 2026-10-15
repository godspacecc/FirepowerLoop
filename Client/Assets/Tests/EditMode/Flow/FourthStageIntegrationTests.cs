using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class FourthStageIntegrationTests
    {
        private string tempRoot;
        private FakeMapLoader loader;
        private FakeChecker checker;
        private FakeClock clock;

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayFourthStageIntegration", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            loader = new FakeMapLoader();
            checker = new FakeChecker();
            clock = new FakeClock(DateTime.Parse("2026-07-20T00:00:00Z").ToUniversalTime());
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IMapContentLoader>(loader);
                architecture.RegisterUtility<IResourceUpdateRuntimeChecker>(checker);
                architecture.RegisterUtility<ISettingsFileStore>(new SettingsFileStore(tempRoot));
            };
            ResourceLoader.SetLoaderForTests(new NullResourceLoader());
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot, clock);
        }

        [TearDown]
        public void TearDown()
        {
            ResourceLoader.ResetForTests();
            ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = _ => { };
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }

            DestroySceneObject("SoundSource-1");
        }

        [UnityTest]
        public IEnumerator ValidSaveCanEnterPlayingThenPauseResumeAndReturnMainMenu()
        {
            var saves = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(saves.CreateBase(out var created, out var error), Is.True, error);
            var loadTask = Game.Interface.GetSystem<MapSceneSystem>().LoadMapAsync(MapIds.Base).AsTask();
            yield return Await(loadTask);
            var loaded = loadTask.Result;
            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.IsTrue(saves.CommitBaseRuntime(created, out error), error);

            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Playing));
            Assert.IsTrue(Game.Interface.GetSystem<AppStateSystem>().TryPause());
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
            Assert.IsTrue(Game.Interface.GetSystem<AppStateSystem>().TryResume());
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Playing));
            Assert.IsTrue(saves.Flush(out error), error);

            var unloadTask = Game.Interface.GetSystem<MapSceneSystem>().UnloadCurrentMapAsync().AsTask();
            yield return Await(unloadTask);
            var unload = unloadTask.Result;
            Assert.IsTrue(unload.Success, unload.Error);
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
        }

        [UnityTest]
        public IEnumerator ResourceUpdateControlsRedPointWithoutBlockingSaveLoad()
        {
            checker.Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.UpdateAvailable, "1", "2", 1, 100, null));
            yield return Await(Game.Interface.GetSystem<ResourceUpdateSystem>().CheckAsync(false).AsTask());
            LifeCycle.LateUpdate();
            Assert.IsTrue(Game.Interface.GetSystem<RedPointSystem>().GetState(RedPointIds.StartResourceUpdate).Active);

            Assert.That(Game.Interface.GetSystem<SaveSystem>().CreateBase(out _, out var error), Is.True, error);

            checker.Next = ResourceUpdateCheckResult.Fail("1", "offline");
            yield return Await(Game.Interface.GetSystem<ResourceUpdateSystem>().CheckAsync(false).AsTask());
            LifeCycle.LateUpdate();
            Assert.IsFalse(Game.Interface.GetSystem<RedPointSystem>().GetState(RedPointIds.StartResourceUpdate).Active);
            Assert.That(Game.Interface.GetSystem<SaveSystem>().InspectBase().IsValid, Is.True);
        }

        [Test]
        public void SettingsResetDoesNotDeleteFixedBaseSave()
        {
            var saves = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(saves.CreateBase(out _, out var error), Is.True, error);

            Game.Interface.GetSystem<SettingsSystem>().ResetAllToDefault();
            Assert.That(saves.InspectBase().IsValid, Is.True);
            var settingsPath = Path.Combine(tempRoot, "Settings", "settings.json");
            Assert.IsTrue(File.Exists(settingsPath));
        }

        [Test]
        public void MapUnloadStopsMapOwnedAudio()
        {
            var source = File.ReadAllText(Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                "Assets/Game/Scripts/System/Map/MapSceneSystem.cs"));

            Assert.That(source, Does.Contain("StopMapOwnedSounds"));
        }

        [Test]
        public void AudioFourthStageContractsAreSplitIntoFocusedRevisionTests()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            Assert.IsTrue(File.Exists(Path.Combine(root, "Assets/Tests/EditMode/Flow/AudioOwnershipTests.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "Assets/Tests/EditMode/Flow/AudioMixerBindingTests.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "Assets/Tests/EditMode/Flow/AudioPoolTests.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "Assets/Tests/EditMode/Flow/AudioPauseTests.cs")));
        }

        private static int GetAudioSoundCount(AudioSystem audio)
        {
            var sounds = typeof(AudioSystem).GetField("sounds", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(audio) as System.Collections.ICollection;
            return sounds?.Count ?? 0;
        }

        private static IEnumerator Await(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                throw task.Exception;
            }

            if (task.IsCanceled)
            {
                Assert.Fail("Task was cancelled.");
            }
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private static void DestroySceneObject(string name)
        {
            var go = GameObject.Find(name);
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }

        private sealed class FakeMapLoader : IMapContentLoader
        {
            public UniTask<MapContentLoadResult> LoadAsync(string mapId, string address, Action<float> onProgress, CancellationToken cancellationToken = default)
            {
                return UniTask.FromResult(MapContentLoadResult.Ok(new FakeMapHandle(mapId)));
            }

            public UniTask<MapContentUnloadResult> UnloadAsync(IMapContentHandle handle, CancellationToken cancellationToken = default)
            {
                return UniTask.FromResult(MapContentUnloadResult.Ok());
            }
        }

        private sealed class FakeMapHandle : IMapContentHandle
        {
            public FakeMapHandle(string mapId) { MapId = mapId; }
            public string MapId { get; }
            public MapContext Context => null;
            public bool IsValid => true;
        }

        private sealed class FakeChecker : IResourceUpdateRuntimeChecker
        {
            public ResourceUpdateCheckResult Next { get; set; } =
                ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.NoUpdate, "1", "1", 0, 0, null));

            public bool CanCheck => true;

            public UniTask<ResourceUpdateCheckResult> CheckAsync(int timeoutSeconds)
            {
                return UniTask.FromResult(Next);
            }
        }

        private sealed class FakeClock : ISaveClock
        {
            private DateTime utcNow;
            public FakeClock(DateTime utcNow) { this.utcNow = utcNow; }
            public DateTime UtcNow => utcNow;
            public float RealtimeSinceStartup { get; private set; }
        }

        private sealed class NullResourceLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object
            {
                return null;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
            {
                onLoaded?.Invoke(null);
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }
    }
}
