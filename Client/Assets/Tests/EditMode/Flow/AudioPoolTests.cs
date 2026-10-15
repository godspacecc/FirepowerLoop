using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class AudioPoolTests
    {
        private ImmediateAudioLoader loader;
        private GameObject audioRoot;

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            loader = new ImmediateAudioLoader();
            ResourceLoader.SetLoaderForTests(loader);
        }

        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
            AudioSystem.SetBindingsUnavailableForTests(false);
            ResourceLoader.ResetForTests();
            Object.DestroyImmediate(audioRoot);
        }

        [Test]
        public void MissingBindingFailsSafelyWithoutCreatingAudioRoot()
        {
            var bindingCountBefore = Object.FindObjectsOfType<AudioRootBindings>().Length;
            AudioSystem.SetBindingsUnavailableForTests(true);
            var audio = Game.Interface.GetSystem<AudioSystem>();

            Assert.IsFalse(audio.PlaySound("sound").IsValid);
            Assert.That(audio.PooledSourceCount, Is.EqualTo(0));
            Assert.That(Object.FindObjectsOfType<AudioRootBindings>().Length, Is.EqualTo(bindingCountBefore));
        }

        [Test]
        public void PoolPrewarmsAndCapsAtThirtyTwoActiveSounds()
        {
            audioRoot = CreateAudioRootBindings();
            var audio = Game.Interface.GetSystem<AudioSystem>();

            Assert.That(audio.PooledSourceCount, Is.EqualTo(8));
            for (var i = 0; i < 32; i++)
            {
                Assert.IsTrue(audio.PlaySound("sound", priority: 1).IsValid);
            }

            Assert.IsFalse(audio.PlaySound("sound", priority: 1).IsValid);
            Assert.IsTrue(audio.PlaySound("sound", priority: 2).IsValid);
            Assert.That(audio.ActiveSoundCount, Is.EqualTo(32));
        }

        [Test]
        public void ManualStopReturnsAndResetsSourceBeforeReuse()
        {
            audioRoot = CreateAudioRootBindings();
            var audio = Game.Interface.GetSystem<AudioSystem>();
            var handle = audio.PlaySoundAt("spatial", Vector3.one, 99f);

            audio.Stop(handle);
            var reused = audio.PlayUiSound("ui");

            Assert.IsTrue(reused.IsValid);
            var active = Object.FindObjectsOfType<AudioSource>().FirstOrDefault(source => source.name.StartsWith("SoundSource-"));
            Assert.NotNull(active);
            Assert.That(active.spatialBlend, Is.EqualTo(0f));
            Assert.AreSame(audioRoot.GetComponent<AudioRootBindings>().SoundGroup, active.outputAudioMixerGroup);
        }

        [Test]
        public void SoundFileNameLoadsThroughTheSoundRoot()
        {
            audioRoot = CreateAudioRootBindings();
            var audio = Game.Interface.GetSystem<AudioSystem>();

            var handle = audio.PlaySound("FoundationClick");

            Assert.That(handle.IsValid, Is.True);
            Assert.That(loader.AsyncAddresses, Is.EqualTo(new[] { "Audio/Sound/FoundationClick" }));
        }

        [Test]
        public void InvalidSoundNameDoesNotLoadOrConsumeAnAudioSource()
        {
            audioRoot = CreateAudioRootBindings();
            var audio = Game.Interface.GetSystem<AudioSystem>();
            var pooledBefore = audio.PooledSourceCount;

            LogAssert.Expect(LogType.Warning, "[AudioSystem] 音效播放请求已忽略：原值 Sound/Bad；文件名不能包含路径分隔符");
            var handle = audio.PlaySound("Sound/Bad");

            Assert.That(handle.IsValid, Is.False);
            Assert.That(loader.AsyncAddresses, Is.Empty);
            Assert.That(audio.ActiveSoundCount, Is.EqualTo(0));
            Assert.That(audio.PooledSourceCount, Is.EqualTo(pooledBefore));
        }

        private static GameObject CreateAudioRootBindings()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Game/Audio/AnotherDayAudioMixer.mixer");
            Assert.NotNull(mixer);
            var root = new GameObject("AudioRoot");
            var pool = new GameObject("SoundPoolRoot").transform;
            var playing = new GameObject("PlayingSoundRoot").transform;
            pool.SetParent(root.transform, false);
            playing.SetParent(root.transform, false);
            var bindings = root.AddComponent<AudioRootBindings>();
            bindings.Mixer = mixer;
            bindings.MusicGroup = mixer.FindMatchingGroups("Music").FirstOrDefault();
            bindings.SoundGroup = mixer.FindMatchingGroups("Sound").FirstOrDefault();
            bindings.MusicSourceA = new GameObject("MusicSourceA", typeof(AudioSource)).GetComponent<AudioSource>();
            bindings.MusicSourceB = new GameObject("MusicSourceB", typeof(AudioSource)).GetComponent<AudioSource>();
            bindings.MusicSourceA.transform.SetParent(root.transform, false);
            bindings.MusicSourceB.transform.SetParent(root.transform, false);
            bindings.SoundPoolRoot = pool;
            bindings.PlayingSoundRoot = playing;
            Assert.IsTrue(bindings.IsValid);
            return root;
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private sealed class ImmediateAudioLoader : IResourceLoader
        {
            public System.Collections.Generic.List<string> AsyncAddresses { get; } = new System.Collections.Generic.List<string>();

            public T LoadAsset<T>(string path) where T : Object
            {
                return typeof(T) == typeof(AudioClip) ? AudioClip.Create(path, 64, 1, 44100, false) as T : null;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, System.Action<T> onLoaded) where T : Object
            {
                AsyncAddresses.Add(path);
                onLoaded?.Invoke(LoadAsset<T>(path));
                return new ResourceLoadHandle(path);
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }
    }
}
