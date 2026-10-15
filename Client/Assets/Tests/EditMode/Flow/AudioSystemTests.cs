using NUnit.Framework;
using System.IO;
using System.Reflection;
using QFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class AudioSystemTests
    {
        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            ResourceLoader.SetLoaderForTests(new FakeAudioLoader());
        }

        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
            AudioSystem.SetBindingsUnavailableForTests(false);
            ResourceLoader.ResetForTests();
        }

        [Test]
        public void VolumeUtilityConvertsLinearToDb()
        {
            Assert.That(AudioVolumeUtility.LinearToDb(0f), Is.EqualTo(-80f));
            Assert.That(AudioVolumeUtility.LinearToDb(-1f), Is.EqualTo(-80f));
            Assert.That(AudioVolumeUtility.LinearToDb(1f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(AudioVolumeUtility.LinearToDb(0.5f), Is.LessThan(0f));
        }

        [Test]
        public void EmptyPlaybackHandleIsInvalid()
        {
            Assert.IsFalse(AudioPlaybackHandle.Empty.IsValid);
        }

        [Test]
        public void MissingBindingReturnsEmptyHandleAndDoesNotCreateAudioRoot()
        {
            var bindingCountBefore = Object.FindObjectsOfType<AudioRootBindings>().Length;
            AudioSystem.SetBindingsUnavailableForTests(true);
            var audio = Game.Interface.GetSystem<AudioSystem>();

            var handle = audio.PlayUiSound("click");

            Assert.IsFalse(handle.IsValid);
            Assert.That(audio.ActiveSoundCount, Is.EqualTo(0));
            Assert.That(audio.PooledSourceCount, Is.EqualTo(0));
            Assert.That(Object.FindObjectsOfType<AudioRootBindings>().Length, Is.EqualTo(bindingCountBefore));
        }

        [Test]
        public void AudioAssetsAndMainSceneWiringExist()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Assert.IsTrue(File.Exists(Path.Combine(root, "Assets/Game/Audio/AnotherDayAudioMixer.mixer")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "Assets/Game/Audio/Music/FoundationMusic.wav")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "Assets/Game/Audio/Sound/FoundationClick.wav")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "Assets/Game/Audio/Sound/FoundationSpatial.wav")));

            var main = File.ReadAllText(Path.Combine(root, "Assets/Game/Scenes/Main.unity"));
            Assert.That(main, Does.Contain("m_Name: AudioRoot"));
            Assert.That(main, Does.Contain("m_Name: MusicSourceA"));
            Assert.That(main, Does.Contain("m_Name: MusicSourceB"));
            Assert.That(main, Does.Contain("m_Name: SoundPoolRoot"));
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private sealed class FakeAudioLoader : IResourceLoader
        {
            private readonly AudioClip clip = AudioClip.Create("test", 64, 1, 44100, false);

            public T LoadAsset<T>(string path) where T : Object
            {
                return typeof(T) == typeof(AudioClip) ? clip as T : null;
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
