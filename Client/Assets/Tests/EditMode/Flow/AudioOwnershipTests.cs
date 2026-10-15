using System.Collections.Generic;
using System.Linq;
using System.Collections;
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
    public sealed class AudioOwnershipTests
    {
        private ControlledAudioLoader loader;
        private GameObject audioRoot;

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            loader = new ControlledAudioLoader();
            ResourceLoader.SetLoaderForTests(loader);
            audioRoot = CreateAudioRootBindings();
        }

        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
            ResourceLoader.ResetForTests();
            Object.DestroyImmediate(audioRoot);
        }

        [Test]
        public void SameCommittedMusicAddressIsDeduplicated()
        {
            var audio = Game.Interface.GetSystem<AudioSystem>();

            audio.PlayMusic("a", 0f);
            loader.Complete("Audio/Music/a");
            audio.PlayMusic("a", 0f);

            Assert.That(loader.Requests.Count(item => item.Address == "Audio/Music/a"), Is.EqualTo(1));
        }

        [Test]
        public void OlderPendingMusicCompletionReleasesOnlyItsOwnHandle()
        {
            var audio = Game.Interface.GetSystem<AudioSystem>();

            audio.PlayMusic("a", 0f);
            audio.PlayMusic("b", 0f);
            loader.Complete("Audio/Music/a");
            loader.Complete("Audio/Music/b");

            CollectionAssert.Contains(loader.ReleasedRawHandles, "Audio/Music/a#1");
            CollectionAssert.DoesNotContain(loader.ReleasedRawHandles, "Audio/Music/b#2");
        }

        [Test]
        public void FailedReplacementKeepsActiveMusicAndCanRetryAddress()
        {
            var audio = Game.Interface.GetSystem<AudioSystem>();
            audio.PlayMusic("a", 0f);
            loader.Complete("Audio/Music/a");

            audio.PlayMusic("b", 0f);
            loader.CompleteNull("Audio/Music/b");
            audio.PlayMusic("b", 0f);

            Assert.That(loader.Requests.Count(item => item.Address == "Audio/Music/b"), Is.EqualTo(2));
            CollectionAssert.Contains(loader.ReleasedRawHandles, "Audio/Music/b#2");
        }

        [Test]
        public void StopMusicCancelsPendingAndReleasesActiveHandlesOnce()
        {
            var audio = Game.Interface.GetSystem<AudioSystem>();
            audio.PlayMusic("a", 0f);
            loader.Complete("Audio/Music/a");
            audio.PlayMusic("b", 0f);

            audio.StopMusic(0f);
            loader.Complete("Audio/Music/b");

            var released = string.Join(", ", loader.ReleasedRawHandles.Select(item => item?.ToString()));
            Assert.That(loader.ReleasedRawHandles.Count(item => Equals(item, "Audio/Music/a#1")), Is.EqualTo(1), released);
            Assert.That(loader.ReleasedRawHandles.Count(item => Equals(item, "Audio/Music/b#2")), Is.EqualTo(1), released);
        }

        [Test]
        public void InvalidMusicNameKeepsCommittedMusicAndDoesNotRequestAnotherLoad()
        {
            var audio = Game.Interface.GetSystem<AudioSystem>();
            audio.PlayMusic("a", 0f);
            loader.Complete("Audio/Music/a");
            var requestsBefore = loader.Requests.Count;
            var releasesBefore = loader.ReleasedRawHandles.Count;

            LogAssert.Expect(LogType.Warning, "[AudioSystem] 音乐播放请求已忽略：原值 Music/Bad；文件名不能包含路径分隔符");
            audio.PlayMusic("Music/Bad", 0f);

            Assert.That(loader.Requests.Count, Is.EqualTo(requestsBefore));
            Assert.That(loader.ReleasedRawHandles.Count, Is.EqualTo(releasesBefore));
            Assert.That(GetCurrentMusicAddress(audio), Is.EqualTo("Audio/Music/a"));
        }

        [Test]
        public void PendingWorldSoundCompletedDuringPauseDoesNotStart()
        {
            var audio = Game.Interface.GetSystem<AudioSystem>();
            audio.PlaySoundAt("a", Vector3.zero);
            Game.Interface.SendEvent(new AppStateChangedEvent(AppState.Playing, AppState.Paused));
            loader.Complete("Audio/Sound/a");
            Assert.That(audio.ActiveSoundCount, Is.Zero);
            Assert.That(loader.ReleasedRawHandles.Count(x => Equals(x, "Audio/Sound/a#1")), Is.EqualTo(1));
        }

        [Test]
        public void SpatialGainSurvivesVolumeRefresh()
        {
            var audio = Game.Interface.GetSystem<AudioSystem>();
            audio.PlaySoundAt("a", Vector3.zero, 35f, -10, new SoundPlaybackOptions(0.4f, 16f, 0f));
            loader.Complete("Audio/Sound/a");
            var source = audioRoot.GetComponentsInChildren<AudioSource>().Single(x => x.clip != null);
            audio.ApplyVolumes();
            Assert.That(source.volume, Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(source.minDistance, Is.EqualTo(16f));
        }

        [UnityTest]
        public IEnumerator ExpiredSpatialRequestDoesNotPlayLate()
        {
            var audio = Game.Interface.GetSystem<AudioSystem>();
            audio.PlaySoundAt("a", Vector3.zero, 35f, -10, new SoundPlaybackOptions(0.6f, 16f, 0.01f));
            yield return new WaitForSecondsRealtime(0.03f);
            loader.Complete("Audio/Sound/a");
            Assert.That(audio.ActiveSoundCount, Is.Zero);
            Assert.That(loader.ReleasedRawHandles.Count(x => Equals(x, "Audio/Sound/a#1")), Is.EqualTo(1));
        }

        [Test]
        public void MapUnloadCancelsPendingSpatialHandleExactlyOnce()
        {
            var audio = Game.Interface.GetSystem<AudioSystem>();
            audio.PlaySoundAt("a", Vector3.zero, 35f, -10, new SoundPlaybackOptions(0.6f, 16f, 0.15f));
            audio.StopMapOwnedSounds();
            loader.Complete("Audio/Sound/a");
            Assert.That(audio.ActiveSoundCount, Is.Zero);
            Assert.That(loader.ReleasedRawHandles.Count(x => Equals(x, "Audio/Sound/a#1")), Is.EqualTo(1));
        }

        private static string GetCurrentMusicAddress(AudioSystem audio)
        {
            return (string)typeof(AudioSystem).GetField("currentMusicAddress", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(audio);
        }

        private static GameObject CreateAudioRootBindings()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Game/Audio/AnotherDayAudioMixer.mixer");
            Assert.NotNull(mixer, "Audio ownership tests require the project AudioMixer asset.");
            var musicGroup = mixer.FindMatchingGroups("Music").FirstOrDefault();
            var soundGroup = mixer.FindMatchingGroups("Sound").FirstOrDefault();
            Assert.NotNull(musicGroup, "Audio ownership tests require the Music mixer group.");
            Assert.NotNull(soundGroup, "Audio ownership tests require the Sound mixer group.");

            var root = new GameObject("AudioRoot");
            var pool = new GameObject("SoundPoolRoot").transform;
            var playing = new GameObject("PlayingSoundRoot").transform;
            pool.SetParent(root.transform, false);
            playing.SetParent(root.transform, false);
            var sourceA = new GameObject("MusicSourceA", typeof(AudioSource)).GetComponent<AudioSource>();
            var sourceB = new GameObject("MusicSourceB", typeof(AudioSource)).GetComponent<AudioSource>();
            sourceA.transform.SetParent(root.transform, false);
            sourceB.transform.SetParent(root.transform, false);
            var bindings = root.AddComponent<AudioRootBindings>();
            bindings.Mixer = mixer;
            bindings.MusicGroup = musicGroup;
            bindings.SoundGroup = soundGroup;
            bindings.MusicSourceA = sourceA;
            bindings.MusicSourceB = sourceB;
            bindings.SoundPoolRoot = pool;
            bindings.PlayingSoundRoot = playing;
            return root;
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private sealed class ControlledAudioLoader : IResourceLoader
        {
            private int nextId;
            public List<Request> Requests { get; } = new List<Request>();
            public List<object> ReleasedRawHandles { get; } = new List<object>();

            public T LoadAsset<T>(string path) where T : Object
            {
                return typeof(T) == typeof(AudioClip) ? AudioClip.Create(path, 64, 1, 44100, false) as T : null;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, System.Action<T> onLoaded) where T : Object
            {
                var request = new Request(path, $"{path}#{++nextId}", asset => onLoaded?.Invoke(asset as T));
                Requests.Add(request);
                return new ResourceLoadHandle(request.RawHandle);
            }

            public void Release(ResourceLoadHandle handle)
            {
                if (handle.IsValid)
                {
                    ReleasedRawHandles.Add(handle.RawHandle);
                }
            }

            public void Complete(string address, AudioClip clip = null)
            {
                var request = Requests.Last(item => item.Address == address && !item.Completed);
                request.Completed = true;
                request.Callback(clip ?? AudioClip.Create(address, 64, 1, 44100, false));
            }

            public void CompleteNull(string address)
            {
                var request = Requests.Last(item => item.Address == address && !item.Completed);
                request.Completed = true;
                request.Callback(null);
            }

            public sealed class Request
            {
                public Request(string address, object rawHandle, System.Action<Object> callback)
                {
                    Address = address;
                    RawHandle = rawHandle;
                    Callback = callback;
                }

                public string Address { get; }
                public object RawHandle { get; }
                public System.Action<Object> Callback { get; }
                public bool Completed { get; set; }
            }
        }
    }
}
