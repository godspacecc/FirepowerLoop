using System;
using System.Linq;
using Game;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>通过正式声音事件和 AudioSource 验证子弹命中声的玩家距离衰减。</summary>
    public sealed class CombatImpactAudioTests
    {
        private GameObject audioRoot;
        private PoseProvider player;
        private AudioSystem audio;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            player = new PoseProvider();
            Architecture<Game>.OnRegisterPatch = architecture =>
                architecture.RegisterUtility<IPlayerRuntimePortProvider>(player);
            ResourceLoader.SetLoaderForTests(new ClipLoader());
            audioRoot = new GameObject("ImpactAudioRoot");
            var binding = audioRoot.AddComponent<AudioRootBindings>();
            binding.Mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Game/Audio/FirepowerLoopAudioMixer.mixer");
            binding.MusicGroup = binding.Mixer.FindMatchingGroups("Music").First();
            binding.SoundGroup = binding.Mixer.FindMatchingGroups("Sound").First();
            binding.SoundPoolRoot = Child("Pool").transform;
            binding.PlayingSoundRoot = Child("Playing").transform;
            binding.MusicSourceA = Child("MusicA").AddComponent<AudioSource>();
            binding.MusicSourceB = Child("MusicB").AddComponent<AudioSource>();
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            audio = Game.Interface.GetSystem<AudioSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            audio?.StopMapOwnedSounds();
            Architecture<Game>.OnRegisterPatch = _ => { };
            TestReset.ResetArchitecture();
            ResourceLoader.ResetForTests();
            Object.DestroyImmediate(audioRoot);
        }

        [Test]
        public void BulletHitsBecomeQuieterWithPlayerDistanceAndFarHitsStaySilent()
        {
            var near = Emit(CombatSoundCueKind.BulletBody, Vector3.right * 2f);
            var middle = Emit(CombatSoundCueKind.BulletBody, Vector3.right * 10f);
            Assert.That(near, Is.Not.Null);
            Assert.That(middle, Is.Not.Null);
            Assert.That(near.clip.name, Does.StartWith("CombatBulletBody"));
            Assert.That(near.volume, Is.GreaterThan(0f).And.LessThan(0.25f), "近距命中反馈也应降低基础音量");
            Assert.That(middle.volume, Is.GreaterThan(0f).And.LessThan(near.volume * 0.4f));
            Assert.That(near.spatialBlend, Is.EqualTo(1f), "保留命中点的空间声像");
            Assert.That(near.minDistance, Is.LessThan(3f), "不能再用 16 米近距范围放大命中声");
            Assert.That(near.transform.position, Is.EqualTo(Vector3.right * 2f));
            Assert.That(Emit(CombatSoundCueKind.BulletBody, Vector3.right * 18f), Is.Null);
            Assert.That(Emit(CombatSoundCueKind.BulletBody, Vector3.right * 30f), Is.Null);
            audio.ApplyVolumes();
            Assert.That(middle.volume, Is.LessThan(near.volume * 0.4f), "设置刷新不能抹掉逐次距离衰减");
        }

        [Test]
        public void MovingPlayerCloserRaisesTheNextHitAtTheSameWorldPoint()
        {
            var hit = new Vector3(10f, 0f, 0f);
            var distant = Emit(CombatSoundCueKind.BulletBody, hit);
            player.Position = new Vector3(9f, 0f, 0f);
            var close = Emit(CombatSoundCueKind.BulletBody, hit);
            Assert.That(close.volume, Is.GreaterThan(distant.volume * 2f), "应读取本机玩家的新位置，不能固定音量或按相机距离计算");
        }

        [Test]
        public void MissingOrInactiveLocalPlayerDoesNotPlayFullVolumeBulletHits()
        {
            player.Available = false;
            Assert.That(Emit(CombatSoundCueKind.BulletBody, Vector3.zero), Is.Null);
            player.Available = true;
            player.Active = false;
            Assert.That(Emit(CombatSoundCueKind.BulletBody, Vector3.zero), Is.Null);
            Assert.That(audio.ActiveSoundCount, Is.Zero);
        }

        [Test]
        public void ExistingMeleeAndSwingFeedbackRemainAvailableWithoutBulletDistanceBinding()
        {
            player.Available = false;
            var swing = Emit(CombatSoundCueKind.Swing, Vector3.zero);
            var melee = Emit(CombatSoundCueKind.MeleeBody, Vector3.zero);
            Assert.That(swing.clip.name, Does.StartWith("CombatSwing"));
            Assert.That(melee.clip.name, Does.StartWith("CombatPunchHit"));
            Assert.That(swing.volume, Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(melee.volume, Is.EqualTo(0.35f).Within(0.001f));
        }

        private GameObject Child(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(audioRoot.transform);
            return child;
        }

        private AudioSource Emit(CombatSoundCueKind kind, Vector3 hit)
        {
            var before = Sources();
            Game.Interface.SendEvent(new CombatSoundCueEvent(kind, 0, hit));
            return Sources().Except(before).SingleOrDefault();
        }

        private AudioSource[] Sources() => audioRoot.GetComponentsInChildren<AudioSource>()
            .Where(source => source.clip != null).ToArray();

        private sealed class ClipLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object =>
                AssetDatabase.LoadAssetAtPath<T>("Assets/Game/" + path + ".wav");
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> callback) where T : Object
            {
                callback(LoadAsset<T>(path));
                return new ResourceLoadHandle(path);
            }
            public void Release(ResourceLoadHandle handle) { }
        }

        private sealed class PoseProvider : IPlayerRuntimePortProvider
        {
            public Vector3 Position;
            public bool Available = true, Active = true;
            public bool TryGetCurrentPlayerPose(out PlayerPoseSnapshot pose, out string error)
            {
                pose = new PlayerPoseSnapshot(1, 9, Position, Quaternion.identity, Active);
                error = Available ? null : "测试：玩家未绑定";
                return Available;
            }
            public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort port)
            { port = null; return false; }
            public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort port)
            { port = null; return false; }
            public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider) => false;
        }
    }
}
