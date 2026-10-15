using System.IO;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class AudioPauseTests
    {
        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            ResourceLoader.SetLoaderForTests(new ImmediateAudioLoader());
        }

        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
            ResourceLoader.ResetForTests();
        }

        [Test]
        public void PauseOnlyTargetsNonUiSoundsAndResumeOnlyRestoresSystemPausedSources()
        {
            var source = ReadGameFile("Scripts/System/Audio/AudioSystem.cs");

            Assert.That(source, Does.Contain("e.Current == AppState.Paused"));
            Assert.That(source, Does.Contain("!sound.UiSound"));
            Assert.That(source, Does.Contain("sound.Source.Pause();"));
            Assert.That(source, Does.Contain("sound.PausedBySystem = true;"));
            Assert.That(source, Does.Contain("e.Previous == AppState.Paused && e.Current == AppState.Playing"));
            Assert.That(source, Does.Contain("sound.Source.UnPause();"));
            Assert.That(source, Does.Contain("sound.PausedBySystem = false;"));
        }

        [Test]
        public void DeinitClearsPausedSoundState()
        {
            var source = ReadGameFile("Scripts/System/Audio/AudioSystem.cs");

            Assert.That(source, Does.Contain("foreach (var sound in new List<PlayingSound>(sounds.Values))"));
            Assert.That(source, Does.Contain("Stop(sound.PlaybackHandle);"));
            Assert.That(source, Does.Contain("sounds.Remove(handle.Id)"));
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private static string ReadGameFile(string path)
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return File.ReadAllText(Path.Combine(root, "Assets/Game", path.Replace('/', Path.DirectorySeparatorChar)));
        }

        private sealed class ImmediateAudioLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object
            {
                return typeof(T) == typeof(AudioClip) ? AudioClip.Create(path, 64, 1, 44100, false) as T : null;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, System.Action<T> onLoaded) where T : Object
            {
                onLoaded?.Invoke(LoadAsset<T>(path));
                return new ResourceLoadHandle(path);
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }
    }
}
