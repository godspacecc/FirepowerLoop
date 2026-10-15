using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class FoleyAmbientTests
    {
        private GameObject root, actor;
        private AudioSystem audio;
        private DeferredLoader loader;
        private IDisposable presenter;
        [SetUp] public void SetUp()
        {
            TestReset.ResetArchitecture();
            loader = new DeferredLoader(); ResourceLoader.SetLoaderForTests(loader);
            root = new GameObject("FoleyTestAudio"); actor = new GameObject("Actor");
            var bindings = root.AddComponent<AudioRootBindings>();
            bindings.Mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Game/Audio/AnotherDayAudioMixer.mixer");
            bindings.MusicGroup = bindings.Mixer.FindMatchingGroups("Music").First();
            bindings.SoundGroup = bindings.Mixer.FindMatchingGroups("Sound").First();
            bindings.SoundPoolRoot = Child("Pool").transform; bindings.PlayingSoundRoot = Child("Playing").transform;
            bindings.MusicSourceA = Child("MusicA").AddComponent<AudioSource>(); bindings.MusicSourceB = Child("MusicB").AddComponent<AudioSource>();
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            audio = Game.Interface.GetSystem<AudioSystem>();
            // 本组投递提交事件验证声音，不创建通知 UI；场景交互控制器由独立测试覆盖。
            (typeof(Game).GetField("civilisationSceneCtrl", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(Game.Interface) as IDisposable)?.Dispose();
        }
        private GameObject Child(string name) { var o = new GameObject(name); o.transform.SetParent(root.transform); return o; }
        [TearDown] public void TearDown()
        {
            presenter?.Dispose(); presenter = null; TestReset.ResetArchitecture(); ResourceLoader.ResetForTests();
            Object.DestroyImmediate(root); Object.DestroyImmediate(actor); loader.Dispose();
        }
        private MethodInfo Method(string name) { var m = typeof(AudioSystem).GetMethod(name); Assert.That(m, Is.Not.Null, name + " 尚未实现"); return m; }
        private AudioPlaybackHandle Loop() => (AudioPlaybackHandle)Method("PlayLoopAt").Invoke(audio,
            new object[] { "TestLoop", Vector3.zero, 35f, -20, new SoundPlaybackOptions(0.25f, 16f, 0f) });
        private AudioSource Source => root.GetComponentsInChildren<AudioSource>().Single(s => s.clip != null);
        [Test] public void LoopUsesSpatialSourceAndGain()
        { Loop(); loader.Complete(0); Assert.That(Source.loop, Is.True); Assert.That(Source.spatialBlend, Is.EqualTo(1)); Assert.That(Source.volume, Is.EqualTo(0.25f)); }
        [Test] public void MovingPendingLoopUsesLatestPosition()
        { var h = Loop(); Method("SetSoundPosition").Invoke(audio, new object[] { h, Vector3.right * 7 }); loader.Complete(0); Assert.That(Source.transform.position, Is.EqualTo(Vector3.right * 7)); }
        [Test] public void ReleasedLoopRejectsLateCompletionExactlyOnce()
        { Loop(); audio.StopMapOwnedSounds(); audio.StopMapOwnedSounds(); loader.Complete(0); Assert.That(audio.ActiveSoundCount, Is.Zero); Assert.That(loader.Releases, Is.EqualTo(1)); }

        private void Ambient()
        {
            var type = typeof(AudioSystem).Assembly.GetType("Game.MapLoopSoundPresenter");
            Assert.That(type, Is.Not.Null, "环境循环表现尚未实现");
            presenter = (IDisposable)Activator.CreateInstance(type, actor.transform, Game.Interface,
                "AmbienceFire", "CampfireIgnite", new SoundPlaybackOptions(0.2f, 16f, 0f), 35f);
        }
        private void State(AppState previous, AppState next)
        {
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = next;
            Game.Interface.SendEvent(new AppStateChangedEvent(previous, next));
        }
        [Test] public void AmbientPauseResumeDoesNotRepeatIgnition()
        { Ambient(); State(AppState.Playing, AppState.Paused); Assert.That(audio.ActiveSoundCount, Is.Zero); State(AppState.Paused, AppState.Playing); Assert.That(audio.ActiveSoundCount, Is.EqualTo(1)); Assert.That(loader.Addresses.Count(x => x.EndsWith("CampfireIgnite")), Is.EqualTo(1)); }
        [Test] public void LoadingAmbientWaitsForPlayingAndSkipsIgnition()
        { State(AppState.Playing, AppState.Loading); Ambient(); Assert.That(loader.Addresses, Is.Empty); State(AppState.Loading, AppState.Playing); Assert.That(audio.ActiveSoundCount, Is.EqualTo(1)); Assert.That(loader.Addresses.Any(x => x.EndsWith("CampfireIgnite")), Is.False); }
        [Test] public void DisposedAmbientNeverRestartsOrAcceptsLateLoad()
        { Ambient(); presenter.Dispose(); presenter.Dispose(); loader.Complete(0); loader.Complete(1); State(AppState.Playing, AppState.Paused); State(AppState.Paused, AppState.Playing); Assert.That(audio.ActiveSoundCount, Is.Zero); Assert.That(loader.Releases, Is.EqualTo(2)); }
        /// <summary>模拟架构先释放、场景对象后停用；迟到加载和重复清理不能报错或重复释放资源。</summary>
        [Test] public void AmbientDisposeAfterArchitectureDeinitIsSafe()
        {
            Ambient();
            var architecture = Game.Interface;
            architecture.Deinit();
            Assert.That(architecture.GetSystem<AudioSystem>(), Is.Null);
            Assert.DoesNotThrow(() => presenter.Dispose());
            Assert.DoesNotThrow(() => presenter.Dispose());
            loader.Complete(0); loader.Complete(1);
            Assert.That(audio.ActiveSoundCount, Is.Zero);
            Assert.That(loader.Releases, Is.EqualTo(2));
        }
        private static void Send<T>(T value) => Game.Interface.SendEvent(value);
        [Test] public void RiverClosestPointUsesTransformedSegments()
        {
            var type = typeof(AudioSystem).Assembly.GetType("Game.MapAmbientSoundView");
            Assert.That(type, Is.Not.Null);
            actor.transform.position = new Vector3(10, 2, 0); actor.transform.rotation = Quaternion.Euler(0, 90, 0);
            var point = (Vector3)type.GetMethod("ClosestPoint").Invoke(null, new object[] { actor.transform,
                new[] { Vector3.zero, Vector3.right * 10, Vector3.right * 10 }, new Vector3(13, 7, -5) });
            Assert.That(Vector3.Distance(point, new Vector3(10, 2, -5)), Is.LessThan(0.001f));
        }

        private sealed class DeferredLoader : IResourceLoader, IDisposable
        {
            public readonly List<string> Addresses = new List<string>();
            private readonly List<Action<Object>> callbacks = new List<Action<Object>>();
            private readonly AudioClip clip = AudioClip.Create("Test", 44100, 1, 44100, false);
            public int Releases;
            public T LoadAsset<T>(string path) where T : Object => clip as T;
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> callback) where T : Object
            { Addresses.Add(path); callbacks.Add(o => callback(o as T)); return new ResourceLoadHandle(path + "#" + callbacks.Count); }
            public void Complete(int i) => callbacks[i](clip);
            public void Release(ResourceLoadHandle handle) { Releases++; }
            public void Dispose() => Object.DestroyImmediate(clip);
        }
    }
}
