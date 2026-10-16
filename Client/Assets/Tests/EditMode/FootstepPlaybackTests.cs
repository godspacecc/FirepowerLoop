using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class FootstepPlaybackTests
    {
        private GameObject actor, floor, audioRoot;
        private FootstepProfile profile;
        private FootstepPresenter presenter;
        private DeferredLoader loader;
        private AudioSystem audio;
        [SetUp] public void SetUp()
        {
            TestReset.ResetArchitecture();
            loader = new DeferredLoader(); ResourceLoader.SetLoaderForTests(loader);
            audioRoot = new GameObject("AudioRoot");
            var binding = audioRoot.AddComponent<AudioRootBindings>();
            binding.Mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Game/Audio/FirepowerLoopAudioMixer.mixer");
            binding.MusicGroup = binding.Mixer.FindMatchingGroups("Music").First();
            binding.SoundGroup = binding.Mixer.FindMatchingGroups("Sound").First();
            binding.SoundPoolRoot = Child("Pool").transform; binding.PlayingSoundRoot = Child("Playing").transform;
            binding.MusicSourceA = Child("MusicA").AddComponent<AudioSource>(); binding.MusicSourceB = Child("MusicB").AddComponent<AudioSource>();
            actor = new GameObject("Actor", typeof(CharacterController), typeof(PlayerView));
            actor.transform.position = new Vector3(5000f, 0f, 5000f);
            var controller = actor.GetComponent<CharacterController>(); controller.height = 2f; controller.center = Vector3.up; controller.enabled = false;
            profile = ScriptableObject.CreateInstance<FootstepProfile>();
            // 本组只验证选音与句柄归属，固定测试节奏，避免听感调参改变每次 Step 的前提。
            profile.WalkStride = 0.65f; profile.RunStride = 0.9f;
            profile.MinimumInterval = 0.12f; profile.FastSpeed = 4.5f;
            profile.Dirt = new[] { "FootstepDirt01", "FootstepDirt02" };
            var view = actor.GetComponent<PlayerView>();
            var serialized = new SerializedObject(view); serialized.FindProperty("footstepProfile").objectReferenceValue = profile; serialized.ApplyModifiedPropertiesWithoutUndo();
            floor = new GameObject("Floor", typeof(BoxCollider), typeof(FootstepSurface));
            floor.transform.position = actor.transform.position + Vector3.down * 0.5f; floor.GetComponent<BoxCollider>().size = new Vector3(10f, 1f, 10f);
            Physics.SyncTransforms();
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            audio = Game.Interface.GetSystem<AudioSystem>();
            presenter = new FootstepPresenter(view, Game.Interface);
        }
        private GameObject Child(string name) { var child = new GameObject(name); child.transform.SetParent(audioRoot.transform); return child; }
        private void Step() => presenter.Tick(Vector3.right * 0.7f, 0.2f, true, false);
        [TearDown] public void TearDown()
        {
            presenter?.Dispose(); TestReset.ResetArchitecture(); ResourceLoader.ResetForTests();
            Object.DestroyImmediate(actor); Object.DestroyImmediate(floor); Object.DestroyImmediate(audioRoot); Object.DestroyImmediate(profile);
        }
        [Test] public void ConsecutiveStepsChooseDifferentVariants()
        { Step(); Step(); Assert.That(loader.Addresses.Count, Is.EqualTo(2)); Assert.That(loader.Addresses[0], Is.Not.EqualTo(loader.Addresses[1])); }
        [Test] public void AtMostTwoRequestsRemainOwnedByActor()
        { Step(); Step(); Step(); Assert.That(audio.ActiveSoundCount, Is.EqualTo(2)); Assert.That(loader.ReleaseCount, Is.EqualTo(1)); }
        [Test] public void StopCancelsPendingStepAndLateCompletion()
        { Step(); presenter.Tick(Vector3.zero, 0.2f, true, false); loader.Complete(0); Assert.That(audio.ActiveSoundCount, Is.Zero); Assert.That(loader.ReleaseCount, Is.EqualTo(1)); }
        [Test] public void PauseCancelsPendingStep()
        { Step(); Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Paused; Game.Interface.SendEvent(new AppStateChangedEvent(AppState.Playing, AppState.Paused)); loader.Complete(0); Assert.That(audio.ActiveSoundCount, Is.Zero); }
        [Test] public void DeathSuppressesFurtherRequests()
        { presenter.SetAlive(false); Step(); Assert.That(loader.Addresses, Is.Empty); }
        [Test] public void RepeatedDisposeReleasesPendingHandleOnce()
        { Step(); presenter.Dispose(); presenter.Dispose(); loader.Complete(0); Assert.That(audio.ActiveSoundCount, Is.Zero); Assert.That(loader.ReleaseCount, Is.EqualTo(1)); }

        [Test] public void FastDirtStepIsStrongerAndReturningToWalkRestoresGain()
        {
            // 直接检查正式 AudioSource，覆盖同音色土路的力度区分与跑转走恢复。
            profile.MaximumStartDelay = 0f;
            Step(); loader.Complete(0);
            float walkVolume = audioRoot.transform.Find("Playing").GetComponentInChildren<AudioSource>().volume;
            presenter.Reset();
            presenter.Tick(Vector3.right * 1.5f, 0.2f, true, false);
            loader.Complete(1);
            float runVolume = audioRoot.transform.Find("Playing").GetComponentInChildren<AudioSource>().volume;
            Assert.That(runVolume, Is.GreaterThanOrEqualTo(walkVolume * 1.2f));
            presenter.Reset();
            Step(); loader.Complete(2);
            Assert.That(audioRoot.transform.Find("Playing").GetComponentInChildren<AudioSource>().volume,
                Is.EqualTo(walkVolume).Within(0.0001f));
        }

        private sealed class DeferredLoader : IResourceLoader
        {
            public readonly List<string> Addresses = new List<string>();
            private readonly List<Action<Object>> callbacks = new List<Action<Object>>();
            public int ReleaseCount;
            public T LoadAsset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>("Assets/Game/" + path + ".wav");
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> callback) where T : Object
            { Addresses.Add(path); callbacks.Add(o => callback(o as T)); return new ResourceLoadHandle(path + "#" + callbacks.Count); }
            public void Complete(int index) => callbacks[index](LoadAsset<AudioClip>(Addresses[index]));
            public void Release(ResourceLoadHandle handle) { ReleaseCount++; }
        }
    }
}
