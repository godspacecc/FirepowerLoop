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
    public sealed class CombatSoundTests
    {
        private GameObject root;
        private Loader loader;
        private AudioSystem audio;
        private object sound;
        [SetUp] public void SetUp()
        {
            TestReset.ResetArchitecture(); loader = new Loader(); ResourceLoader.SetLoaderForTests(loader);
            root = new GameObject("CombatSoundAudio");
            var b = root.AddComponent<AudioRootBindings>();
            b.Mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Game/Audio/AnotherDayAudioMixer.mixer");
            b.MusicGroup = b.Mixer.FindMatchingGroups("Music").First(); b.SoundGroup = b.Mixer.FindMatchingGroups("Sound").First();
            b.SoundPoolRoot = Child("Pool").transform; b.PlayingSoundRoot = Child("Playing").transform;
            b.MusicSourceA = Child("MusicA").AddComponent<AudioSource>(); b.MusicSourceB = Child("MusicB").AddComponent<AudioSource>();
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            audio = Game.Interface.GetSystem<AudioSystem>();
            var type = typeof(AudioSystem).Assembly.GetType("Game.CombatSoundSystem");
            Assert.That(type, Is.Not.Null, "战斗音效表现尚未实现");
            sound = typeof(CombatSoundTests).GetMethod(nameof(Get), BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(type).Invoke(null, null);
        }
        private static T Get<T>() where T : class, ISystem => Game.Interface.GetSystem<T>();
        private GameObject Child(string name) { var o = new GameObject(name); o.transform.SetParent(root.transform); return o; }
        [TearDown] public void TearDown()
        { TestReset.ResetArchitecture(); ResourceLoader.ResetForTests(); Object.DestroyImmediate(root); }
        private void Cue(string cue, int weapon = 19, long sequence = 0)
        {
            var assembly = typeof(AudioSystem).Assembly;
            var kind = assembly.GetType("Game.CombatSoundCueKind");
            var evt = assembly.GetType("Game.CombatSoundCueEvent");
            var value = Activator.CreateInstance(evt, Enum.Parse(kind, cue), weapon, Vector3.zero, sequence);
            typeof(CombatSoundTests).GetMethod(nameof(Send), BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(evt).Invoke(null, new[] { value });
        }
        private static void Send<T>(T e) => Game.Interface.SendEvent(e);
        [Test] public void BowNeverUsesGunMagazineOrDryFire()
        { Cue("ReloadStart", 6); Cue("ReloadFinish", 6); Cue("DryFire", 6); Assert.That(loader.Names, Is.Empty); }
        [Test] public void MagazineStagesUseDifferentOperations()
        { Cue("ReloadStart"); Cue("ReloadFinish"); Assert.That(loader.Names, Is.EqualTo(new[] { "Audio/Sound/WeaponMagazineOut", "Audio/Sound/WeaponMagazineIn" })); }
        [Test] public void ShotgunReloadUsesShellAndCocking()
        { Cue("ReloadStart", 24); Cue("ReloadFinish", 24); Assert.That(loader.Names, Is.EqualTo(new[] { "Audio/Sound/WeaponShotgunLoad", "Audio/Sound/WeaponShotgunCock" })); }
        [Test] public void DuplicateAuthoritySequenceDoesNotRepeatSwing()
        { Cue("Swing", 5, 18); Cue("Swing", 5, 18); Cue("Swing", 5, 17); Assert.That(loader.Names.Count, Is.EqualTo(1)); }
        [Test] public void OneShotMayPlaySwingAndImpactUnderSameSequence()
        { Cue("Swing", 5, 18); Cue("MeleeBody", 5, 18); Assert.That(loader.Names.Count, Is.EqualTo(2)); }
        [Test] public void ImpactMaterialSelectsBodyMetalOrIce()
        { Cue("BulletBody"); Cue("BulletMetal"); Cue("BulletIce"); Assert.That(loader.Names[0], Does.Contain("CombatBulletBody")); Assert.That(loader.Names[1], Does.Contain("CombatBulletMetal")); Assert.That(loader.Names[2], Does.EndWith("CombatBulletIce")); }
        [Test] public void RepeatedDryTriggerIsRateLimited()
        { Cue("DryFire"); Cue("DryFire"); Assert.That(loader.Names.Count, Is.EqualTo(1)); }
        [Test] public void PauseStopsPendingOperations()
        { Cue("ReloadStart"); Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Paused; Game.Interface.SendEvent(new AppStateChangedEvent(AppState.Playing, AppState.Paused)); Assert.That(audio.ActiveSoundCount, Is.Zero); Assert.That(loader.Releases, Is.EqualTo(1)); }
        [Test] public void CancelReloadStopsOwnedOperation()
        { Cue("ReloadStart"); Cue("ReloadCancel"); Assert.That(audio.ActiveSoundCount, Is.Zero); Assert.That(loader.Releases, Is.EqualTo(1)); }
        [Test] public void AuthorityPresentationCopiesLethalTargetValues()
        {
            var ids = new List<long> { 7 };
            var enemies = new List<OnlineWorldEnemySnapshot>
            {
                new OnlineWorldEnemySnapshot(7, 1, "map", "enemy", Vector3.zero, 0, Vector3.right, 0, 0, 0, 10, true, 0, 1, 1)
            };
            var e = new OnlineWorldCombatPresentationEvent(9, OnlineWorldCombatActionKind.PlayerProjectile, 1, 19, ids, enemies);
            ids.Clear(); enemies.Clear();
            Assert.That(e.TargetUnitIds, Is.EqualTo(new long[] { 7 }));
            Assert.That(e.TargetEnemies.Single().IsDead, Is.True);
            Assert.That(e.TargetEnemies.Single().LocalPosition, Is.EqualTo(Vector3.right));
        }
        private sealed class Loader : IResourceLoader
        {
            public readonly List<string> Names = new List<string>(); public int Releases;
            public T LoadAsset<T>(string path) where T : Object => null;
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> loaded) where T : Object
            { Names.Add(path); return new ResourceLoadHandle(path + Names.Count); }
            public void Release(ResourceLoadHandle handle) { Releases++; }
        }
    }
}
