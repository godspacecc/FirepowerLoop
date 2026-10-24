using System;
using System.Collections;
using UnityEngine.TestTools;
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
    /// <summary>验证喷火循环的去重、停火和地图释放，使用可控异步加载覆盖迟到回调。</summary>
    public sealed class ArcadeFlamethrowerFeedbackTests
    {
        private ControlledAudioLoader loader;
        private GameObject audioRoot;
        private ArcadeCombatViewController view;
        private AudioSystem audio;
        private ArcadeWeaponDefinition flameWeapon;
        [SetUp] public void SetUp()
        {
            ResetArchitecture(); loader = new ControlledAudioLoader(); ResourceLoader.SetLoaderForTests(loader);
            audioRoot = CreateAudioRootBindings(); audio = Game.Interface.GetSystem<AudioSystem>();
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new Luban.ByteBuf(System.IO.File.ReadAllBytes("Assets/Game/Config/"+n+".bytes"))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);
            flameWeapon=catalog.Weapons.Single(w=>w.Id=="arcade.weapon.flamethrower");
            view = new ArcadeCombatViewController(); view.SetPlayerWeapon(flameWeapon);
        }
        [TearDown] public void TearDown()
        {
            view.End(); ResetArchitecture(); ResourceLoader.ResetForTests(); Object.DestroyImmediate(audioRoot);
        }
        private void Pulse(Vector3 position) => Call("PlayFlamethrowerSound", audio, position);
        private void Call(string name, params object[] args)
        {
            var method = typeof(ArcadeCombatViewController).GetMethod(name, BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name + " 需要归属当前战场表现"); method.Invoke(view, args);
        }
        [Test] public void HeldFireKeepsOneLoopAndOneIgnition()
        {
            for(int i=0;i<8;i++) { Pulse(new Vector3(i,0,0)); Call("TickFlamethrowerAudio", flameWeapon.FireIntervalSeconds); }
            Assert.That(loader.Requests.Count(r=>r.Address=="Audio/Sound/ArcadeFireFlameLoop"), Is.EqualTo(1));
            Assert.That(loader.Requests.Count(r=>r.Address=="Audio/Sound/ArcadeFireFlameStart"), Is.EqualTo(1));
            loader.Complete("Audio/Sound/ArcadeFireFlameLoop");
            var loop=audioRoot.GetComponentsInChildren<AudioSource>().Single(x=>x.clip!=null);
            Assert.That(loop.loop, Is.True); Assert.That(loop.transform.position, Is.EqualTo(new Vector3(7,0,0)));
        }
        [Test] public void QuietUiGainSurvivesPendingLoadAndPauseWithoutChangingOtherClicks()
        {
            Game.Interface.SendEvent(new AppStateChangedEvent(AppState.Playing,AppState.Paused));
            var quiet=audio.PlayUiSound("ArcadeUiClick");audio.SetSoundGain(quiet,.25f);
            loader.Complete("Audio/Sound/ArcadeUiClick");
            var quietSource=audioRoot.GetComponentsInChildren<AudioSource>().Single(x=>x.clip!=null);
            Assert.That(quietSource.volume,Is.EqualTo(.25f).Within(.0001f));
            Assert.That(quietSource.spatialBlend,Is.Zero);
            audio.PlayUiSound("FoundationClick");loader.Complete("Audio/Sound/FoundationClick");
            var normalSource=audioRoot.GetComponentsInChildren<AudioSource>().Single(x=>x.clip!=null&&x!=quietSource);
            Assert.That(normalSource.volume,Is.EqualTo(1f).Within(.0001f));
        }
        [TestCase("timeout")] [TestCase("weapon")] [TestCase("end")] [TestCase("hidden")]
        public void StopsBothHandlesAndReleasesLateLoads(string reason)
        {
            Pulse(Vector3.zero);
            if(reason=="timeout") Call("TickFlamethrowerAudio", flameWeapon.FireIntervalSeconds+.1f);
            else if(reason=="weapon") view.SetPlayerWeapon("arcade.weapon.basic");
            else if(reason=="hidden") view.SetPlayer(Vector2.zero,Vector2.up,false,false);
            else view.End();
            loader.Complete("Audio/Sound/ArcadeFireFlameStart"); loader.Complete("Audio/Sound/ArcadeFireFlameLoop");
            Assert.That(audio.ActiveSoundCount, Is.Zero);
            Assert.That(loader.ReleasedRawHandles.Distinct().Count(), Is.EqualTo(2));
            Assert.That(loader.ReleasedRawHandles.Count, Is.EqualTo(2));
        }
        [Test] public void ZeroDeltaDoesNotExpirePausedFlame()
        {
            Pulse(Vector3.zero); for(int i=0;i<20;i++) Call("TickFlamethrowerAudio", 0f);
            Pulse(Vector3.one); Assert.That(loader.Requests.Count, Is.EqualTo(2));
            Call("TickFlamethrowerAudio", flameWeapon.FireIntervalSeconds+.1f); Pulse(Vector3.one); Assert.That(loader.Requests.Count, Is.EqualTo(4));
        }
        [Test] public void ClipsAreImportedAndCollectedWithoutBackupDependencies()
        {
            foreach(var name in new[]{"ArcadeFireFlameStart","ArcadeFireFlameLoop"})
            {
                string path="Assets/Game/Audio/Sound/"+name+".wav";
                var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(path); Assert.That(clip,Is.Not.Null,path);
                Assert.That(clip.channels,Is.EqualTo(1));
                Assert.That(System.IO.File.ReadAllText("Assets/AssetBundleCollectorSetting.asset"),Does.Contain("CollectPath: "+path));
                Assert.That(clip.length, name.EndsWith("Start") ? Is.InRange(.12f,.3f) : Is.GreaterThan(3f));
            }
        }
        [Test] public void FarFlameStaysSoftWithoutOversizedClouds()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxArrow.prefab");
            var ps=prefab.transform.Find("ArcadeFlameBurst").GetComponent<ParticleSystem>();
            Assert.That(ps.main.startSize.constantMax,Is.LessThanOrEqualTo(1.3f),"连续火流不能靠巨型火团覆盖");
            Assert.That(ps.GetComponent<ParticleSystemRenderer>().renderMode,Is.EqualTo(ParticleSystemRenderMode.Billboard));
        }
        [Test] public void AuthoritativeSequenceRoutesFlameAndStopsLiveSources()
        {
            var root=new GameObject("flame-audio-owner");
            var motor=new RecordingMotor { Muzzle=new Vector3(2,1,3) };
            void Set(string field,object value) => typeof(ArcadeCombatViewController).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,value);
            Set("root",root); Set("mapRoot",root.transform); Set("playerMotor",motor);
            try
            {
                view.SetPlayerFireSequence(1); view.SetPlayerFireSequence(1);
                Assert.That(loader.Requests.Count,Is.EqualTo(2),"同一权威序列只点火一次");
                loader.Complete("Audio/Sound/ArcadeFireFlameStart"); loader.Complete("Audio/Sound/ArcadeFireFlameLoop");
                Assert.That(audioRoot.GetComponentsInChildren<AudioSource>().Count(x=>x.clip!=null),Is.EqualTo(2));
                view.TickPlayerFeedback(flameWeapon.FireIntervalSeconds+.1f); Assert.That(audio.ActiveSoundCount,Is.Zero);
                Assert.That(audioRoot.GetComponentsInChildren<AudioSource>().All(x=>x.clip==null),Is.True);
            }
            finally { view.End(); if(root!=null) Object.DestroyImmediate(root); }
        }
        [TestCase(false)] [TestCase(true)]
        public void FailedOrPausedPendingLoopCanRestartOnNextShot(bool paused)
        {
            Pulse(Vector3.zero);
            if(paused)
            {
                Game.Interface.SendEvent(new AppStateChangedEvent(AppState.Playing,AppState.Paused));
                loader.Complete("Audio/Sound/ArcadeFireFlameLoop");
                Game.Interface.SendEvent(new AppStateChangedEvent(AppState.Paused,AppState.Playing));
            }
            else loader.CompleteNull("Audio/Sound/ArcadeFireFlameLoop");
            Pulse(Vector3.one);
            Assert.That(loader.Requests.Count(r=>r.Address=="Audio/Sound/ArcadeFireFlameLoop"),Is.EqualTo(2));
            loader.Complete("Audio/Sound/ArcadeFireFlameStart"); loader.Complete("Audio/Sound/ArcadeFireFlameLoop");
            Assert.That(audio.ActiveSoundCount,Is.EqualTo(2));
        }
        [UnityTest] public IEnumerator ColdLoopLoadRemainsValidWhileStillFiring()
        {
            Pulse(Vector3.zero);
            yield return new WaitForSecondsRealtime(.22f);
            Pulse(Vector3.one);
            loader.Complete("Audio/Sound/ArcadeFireFlameLoop");
            Assert.That(audioRoot.GetComponentsInChildren<AudioSource>().Any(x=>x.clip!=null&&x.loop),Is.True);
        }
        [TestCase(true)] [TestCase(false)] public void OnlinePauseOnlyStopsItsOwnActivePresentation(bool onlineActive)
        {
            Pulse(Vector3.zero); loader.Complete("Audio/Sound/ArcadeFireFlameLoop");
            var system=new OnlineArcadePresentationSystem();
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new Luban.ByteBuf(System.IO.File.ReadAllBytes("Assets/Game/Config/"+n+".bytes"))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);
            var snapshot=new OnlineArcadeRunClientSnapshot(true,1,1,1,OnlineArcadeRunPhase.Arena,0,1,"",null,default,default,
                new OnlineArcadeBattleSnapshot(1,1,4,0,0,2,1,0,null,null),battlePaused:true);
            void Set(string name,object value)=>typeof(OnlineArcadePresentationSystem).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(system,value);
            Set("active",onlineActive); Set("view",view); Set("latest",snapshot); Set("catalog",catalog);
            typeof(OnlineArcadePresentationSystem).GetMethod("OnUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(system,null);
            Assert.That(audio.ActiveSoundCount,Is.EqualTo(onlineActive?0:2),"仅联机现场自己的断线等待停止循环，非活动联机系统不能打断单人循环");
        }
        private static GameObject CreateAudioRootBindings()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Game/Audio/FirepowerLoopAudioMixer.mixer");
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
        private sealed class RecordingMotor : ICharacterMotorPort
        {
            public Vector3 Muzzle { get; set; }
            public int UnitId => 1;
            public long MapInstanceId => 1;
            public bool IsValid => true;
            public bool IsEnabled => true;
            public Vector3 Position => Vector3.zero;
            public Quaternion Rotation => Quaternion.identity;
            public float MovementYawDegrees => 0f;
            public bool IsGrounded => true;
            public void Move(Vector3 motion) { }
            public void RotateTowards(Quaternion rotation, float maxDegreesDelta) { }
            public void FaceDirection(Vector3 direction) { }
            public void SetArcadeWeaponPresentation(string visualResourceName, bool muzzleFlash,
                Vector3 projectileFirePointLocalPosition) { }
            public void SetTiliRing(float current, float maximum, bool isSprinting, bool exhausted) { }
            public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin)
            {
                origin = Muzzle;
                return true;
            }
            public void FaceWorldHud(Vector3 cameraPosition, Vector3 cameraUp) { }
            public void PlayAttackFeedback() { }
            public void PlayWeaponSwingFeedback() { }
            public void PlayHurtFeedback() { }
            public void TickCombatFeedback(float deltaTime) { }
        }
    }
}
