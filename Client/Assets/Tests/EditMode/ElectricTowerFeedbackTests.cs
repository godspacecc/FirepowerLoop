using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using FirepowerLoop.Arcade;
using Object = UnityEngine.Object;
namespace Game.Tests
{
    /// <summary>验证真实电塔音源、距离变化和晚加载释放，不模拟伤害裁决。</summary>
    public sealed class ElectricTowerFeedbackTests
    {
        private ControlledAudioLoader loader;
        private GameObject audioRoot, sceneRoot;
        private ArcadeCombatViewController view;
        private AudioSystem audio;
        private static ArcadeHazardParameters Tower => new ArcadeHazardParameters(1,1,0,0,0,1.2f,0,0,4,1.5f,3,0,1,1,.05f);
        [SetUp] public void SetUp()
        {
            ResetArchitecture(); loader=new ControlledAudioLoader();ResourceLoader.SetLoaderForTests(loader);
            audioRoot=CreateAudioRootBindings();audio=Game.Interface.GetSystem<AudioSystem>();
            sceneRoot=new GameObject("ElectricFeedbackMap");view=new ArcadeCombatViewController();
            Set("root",sceneRoot);Set("mapRoot",sceneRoot.transform);Set("contentArena",21);
            view.SetPlayer(Vector2.zero,Vector2.up,true,false);
        }
        [TearDown] public void TearDown()
        {
            view.End();ResetArchitecture();ResourceLoader.ResetForTests();
            if(sceneRoot!=null)Object.DestroyImmediate(sceneRoot);Object.DestroyImmediate(audioRoot);
        }
        private void Set(string name,object value)=>typeof(ArcadeCombatViewController).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,value);
        private void Sync(double time=6)=>view.SetHazards(new[]{Tower},time);
        private AudioSource Loop => audioRoot.GetComponentsInChildren<AudioSource>().Single(s=>s.clip!=null&&s.loop);
        [Test] public void NearTowerIsQuietAndMovingAwayChangesSameLiveSource()
        {
            Sync();loader.Complete("Audio/Sound/ArcadeElectricDischarge");var source=Loop;
            Assert.That(source.spatialBlend,Is.EqualTo(1));Assert.That(source.volume,Is.InRange(.1f,.3f));float near=source.volume;
            view.SetPlayer(new Vector2(10,0),Vector2.up,true,false);
            Assert.That(Loop,Is.SameAs(source));Assert.That(source.volume,Is.LessThan(near*.2f));
            Assert.That(loader.Requests.Count(r=>r.Address.EndsWith("Discharge")),Is.EqualTo(1));
            view.SetPlayer(new Vector2(15,0),Vector2.up,true,false);Assert.That(audio.ActiveSoundCount,Is.Zero);
        }
        [Test] public void PendingLoopUsesLatestPlayerDistance()
        {
            Sync();view.SetPlayer(new Vector2(10,0),Vector2.up,true,false);
            loader.Complete("Audio/Sound/ArcadeElectricDischarge");Assert.That(Loop.volume,Is.InRange(.01f,.06f));
        }
        [TestCase("end")] [TestCase("hidden")] [TestCase("empty")] [TestCase("pause")]
        public void OwnershipStopCancelsPendingAndReleasesOnce(string reason)
        {
            Sync();
            if(reason=="end")view.End();else if(reason=="hidden")view.SetPlayer(Vector2.zero,Vector2.up,false,false);
            else if(reason=="empty")view.SetHazards(Array.Empty<ArcadeHazardParameters>(),6);else view.StopPlayerFireAudio();
            foreach(var request in loader.Requests.ToArray())loader.Complete(request.Address);
            Assert.That(audio.ActiveSoundCount,Is.Zero);Assert.That(loader.ReleasedRawHandles.Count,Is.EqualTo(loader.Requests.Count));
            Assert.That(loader.ReleasedRawHandles.Distinct().Count(),Is.EqualTo(loader.Requests.Count));
        }
        [Test] public void AuthorityPhasesReplaceLoopsWithoutSnapshotStacking()
        {
            Sync(4.3);Sync(4.3);loader.Complete("Audio/Sound/ArcadeElectricCharge");Assert.That(Loop.clip.name,Does.EndWith("Charge"));
            Sync(6);Sync(6);loader.Complete("Audio/Sound/ArcadeElectricDischarge");
            Assert.That(audioRoot.GetComponentsInChildren<AudioSource>().Count(s=>s.clip!=null&&s.loop),Is.EqualTo(1));
            Sync(8.5);Assert.That(audio.ActiveSoundCount,Is.Zero);
        }
        [Test] public void HitCreatesApprovedLinesAndExpiresWithoutColliders()
        {
            var template=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/DocksElectricTower.prefab");
            Assert.That(template,Is.Not.Null);var templates=new GameObject("Templates");templates.transform.SetParent(sceneRoot.transform);
            var tower=Object.Instantiate(template,templates.transform);tower.name="DocksElectricTower";tower.SetActive(false);Set("contentTemplates",templates.transform);
            Sync();var method=typeof(ArcadeCombatViewController).GetMethod("PlayElectricTowerHit");Assert.That(method,Is.Not.Null);
            method.Invoke(view,new object[]{1,new Vector2(1,0)});
            Assert.That(sceneRoot.GetComponentsInChildren<LineRenderer>().Count(l=>l.name.StartsWith("电击命中")),Is.EqualTo(16));
            Assert.That(sceneRoot.GetComponentsInChildren<Collider>(),Is.Empty);
            view.TickPlayerFeedback(.4f);
            Assert.That(sceneRoot.GetComponentsInChildren<LineRenderer>().Count(l=>l.name.StartsWith("电击命中")),Is.Zero);
        }
        [TestCase(false)] [TestCase(true)] public void LastEnemyHitFinishesWhenWaveClears(bool snapshotFirst)
        {
            Sync();
            if(snapshotFirst)view.SetHazards(Array.Empty<ArcadeHazardParameters>(),6);
            view.PlayElectricTowerHit(1,Vector2.zero);
            if(!snapshotFirst)view.SetHazards(Array.Empty<ArcadeHazardParameters>(),6);
            Assert.That(loader.Requests.Any(r=>r.Address=="Audio/Sound/ArcadeElectricHit"),Is.True,"清波快照不能吞掉最后一击");
            loader.Complete("Audio/Sound/ArcadeElectricHit");Assert.That(audio.ActiveSoundCount,Is.EqualTo(1));
        }
        [Test] public void FatalHitFinishesAtLastListenerPositionAfterPlayerHides()
        {
            Sync();view.PlayElectricTowerHit(1,new Vector2(1,0));view.SetPlayer(new Vector2(40,40),Vector2.up,false,false);
            loader.Complete("Audio/Sound/ArcadeElectricHit");
            var hit=audioRoot.GetComponentsInChildren<AudioSource>().Single(x=>x.clip!=null);
            Assert.That(hit.loop,Is.False);Assert.That(hit.volume,Is.InRange(.1f,.25f));Assert.That(hit.transform.position,Is.EqualTo(new Vector3(1,.9f,0)));
            Assert.That(audio.ActiveSoundCount,Is.EqualTo(1),"倒地停止环境循环，短促命中声自然完成");
        }
        [Test] public void DistanceGainSurvivesSettingsRefresh()
        {
            Sync();loader.Complete("Audio/Sound/ArcadeElectricDischarge");view.SetPlayer(new Vector2(10,0),Vector2.up,true,false);
            float gain=Loop.volume;audio.ApplyVolumes();Assert.That(Loop.volume,Is.EqualTo(gain).Within(.0001f));
        }
        [Test] public void FailedPendingLoopRecoversOnNextAuthoritativeSync()
        {
            Sync();loader.CompleteNull("Audio/Sound/ArcadeElectricDischarge");Sync();
            Assert.That(loader.Requests.Count(r=>r.Address.EndsWith("Discharge")),Is.EqualTo(2));
            loader.Complete("Audio/Sound/ArcadeElectricDischarge");Assert.That(Loop.volume,Is.InRange(.1f,.3f));
        }
        [Test] public void ApprovedSoundsAreImportedAndHaveActualCollectorAddresses()
        {
            object Read(object o,string name)=>o.GetType().GetProperty(name)?.GetValue(o)??o.GetType().GetField(name)?.GetValue(o);
            var setting=AssetDatabase.LoadAssetAtPath<Object>("Assets/AssetBundleCollectorSetting.asset");
            var result=setting.GetType().GetMethod("BeginCollect").Invoke(setting,new object[]{"DefaultPackage",true,false});
            var addresses=((System.Collections.IEnumerable)Read(result,"CollectAssets")).Cast<object>().Select(a=>(string)Read(a,"Address")).ToHashSet();
            foreach(string name in new[]{"ArcadeElectricCharge","ArcadeElectricDischarge","ArcadeElectricPowerOn","ArcadeElectricHit"})
            {
                var clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/Sound/"+name+".wav");
                Assert.That(clip,Is.Not.Null);Assert.That(clip.channels,Is.EqualTo(1));Assert.That(clip.length,Is.GreaterThan(.15f));
                Assert.That(addresses.Contains("Audio/Sound/"+name),Is.True,"正式地址不可只在编辑器可用");
            }
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
                return typeof(T) == typeof(AudioClip) ? AudioClip.Create(path, 132300, 1, 44100, false) as T : null;
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
                request.Callback(clip ?? AudioClip.Create(address, 132300, 1, 44100, false));
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
