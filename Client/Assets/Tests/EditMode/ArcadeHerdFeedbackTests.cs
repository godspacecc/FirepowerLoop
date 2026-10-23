using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;
namespace Game.Tests
{
    /// <summary>通过真实牛群Controller与AudioSystem，仅替换异步磁盘边界，检查去重及地图归属。</summary>
    public sealed class ArcadeHerdFeedbackTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private ArcadeCombatViewController view; private AudioSystem audio;
        private GameObject root, audioRoot; private Loader loader; private IResourceLoader previous;
        private ArcadeConfigCatalogDefinition catalog;
        [SetUp] public void Setup()
        {
            Reset(); previous = ResourceLoader.Current; loader = new Loader(); ResourceLoader.Use(loader);
            audioRoot = BindAudio(); audio = Game.Interface.GetSystem<AudioSystem>();
            root = new GameObject("HerdFeedbackMap"); view = new ArcadeCombatViewController();
            Set("root", root); Set("mapRoot", root.transform);
            var round = new GameObject("Round"); round.transform.SetParent(root.transform, false); Set("roundContentRoot", round);
            Set("earlyHerdTemplate", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeHerdVisual.prefab"));
            var provider = new LubanRuntimeConfigProvider(() => new Tables(n => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", n + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog, out var error), Is.True, error);
            Set("earlyStage", catalog.EarlyStages.Single(s => s.ArenaConfigNumber == 22)); Set("contentArena", 22);
            view.SetPlayer(Vector2.zero, Vector2.up, true, false);
        }
        [TearDown] public void Cleanup()
        {
            view?.End(); Reset(); ResourceLoader.Use(previous);
            if (root != null) Object.DestroyImmediate(root); if (audioRoot != null) Object.DestroyImmediate(audioRoot);
            foreach (var clip in loader.Clips) Object.DestroyImmediate(clip);
        }
        private void Set(string name, object value) => typeof(ArcadeCombatViewController).GetField(name, Hidden).SetValue(view, value);
        private void Sync(double phase, ArcadeHerdPhase kind = ArcadeHerdPhase.Charging) => view.SetHerds(7, new[] { new ArcadeHerdSnapshot(7, 1, kind, 0, 0, 1, 0, phase) });
        private void Step() { Sync(.08); Sync(.13); view.TickPlayerFeedback(.02f); }
        private Mesh Dust => root.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name == "HerdDustFeedback")?.sharedMesh;
        [Test] public void LandingEmitsDustAndOneHoofSoundWithoutRepeatedSnapshotStacking()
        {
            Step(); Assert.That(loader.Requests.Count(r => r.Address.EndsWith("ArcadeHerdHoof")), Is.EqualTo(1));
            Assert.That(Dust, Is.Not.Null); Assert.That(Dust.vertexCount, Is.GreaterThan(0));
            int count = loader.Requests.Count; Sync(.13); Sync(.13); Assert.That(loader.Requests.Count, Is.EqualTo(count));
        }
        [Test] public void JoiningMidChargeAndLargeSnapshotGapsNeverReplayOldSteps()
        {
            Sync(5); Sync(5.01); Sync(8); Assert.That(loader.Requests, Is.Empty);
            Assert.That(Dust == null || Dust.vertexCount == 0, Is.True);
        }
        [Test] public void PauseCancelsLateAudioAndResumesWithFreshBaseline()
        {
            Step(); Assert.That(loader.Requests, Is.Not.Empty); view.SetHerdActivityPaused(true);
            loader.CompleteAll(); Assert.That(audio.ActiveSoundCount, Is.Zero);
            Assert.That(loader.Released.Distinct().Count(), Is.EqualTo(loader.Requests.Count));
            int count = loader.Requests.Count; Sync(.5); view.SetHerdActivityPaused(false); Sync(1);
            Assert.That(loader.Requests.Count, Is.EqualTo(count)); Assert.That(Dust.vertexCount, Is.Zero);
        }
        [Test] public void HiddenMapStopsPendingFeedbackAndDoesNotCatchUpOnReturn()
        {
            Step(); Assert.That(loader.Requests, Is.Not.Empty); root.SetActive(false); view.TickPlayerFeedback(.1f); Sync(.6); loader.CompleteAll();
            Assert.That(audio.ActiveSoundCount, Is.Zero); int count = loader.Requests.Count;
            root.SetActive(true); Sync(1); Assert.That(loader.Requests.Count, Is.EqualTo(count));
        }
        [Test] public void RemovedCowsLeaveOnlyShortDustTailAndEndReleasesExactlyOnce()
        {
            Step(); Assert.That(Dust, Is.Not.Null);
            view.SetHerds(7, Array.Empty<ArcadeHerdSnapshot>()); view.TickPlayerFeedback(.8f);
            Assert.That(Dust.vertexCount, Is.Zero); view.End(); view.End(); loader.CompleteAll();
            Assert.That(audio.ActiveSoundCount, Is.Zero); Assert.That(loader.Released.Distinct().Count(), Is.EqualTo(loader.Requests.Count));
            Assert.That(loader.Released.Count, Is.EqualTo(loader.Requests.Count));
        }
        [Test] public void AuthoritativeHerdDeathsPlayOneImpactAndOtherDeathsStaySilent()
        {
            Sync(.01); view.SetEnemy(20, "unused", new Vector2(1, 0), 1, 1);
            view.RemoveEnemy(20, true, Vector2.right, false, false, ArcadeDeathImpactRules.Herd);
            view.RemoveEnemy(20, true, Vector2.right, false, false, ArcadeDeathImpactRules.Herd);
            view.SetEnemy(21, "unused", Vector2.one, 1, 1); view.RemoveEnemy(21, true, Vector2.right, false, false, 0);
            Assert.That(loader.Requests.Count(r => r.Address.EndsWith("ArcadeHerdImpact")), Is.EqualTo(1));
        }
        [Test] public void PlayerPoseSnapshotsStaySilentAndExplicitContactsPlayOnce()
        {
            Sync(.01); var entryType = typeof(ArcadeCombatViewController).GetNestedType("RemotePlayerEntry", BindingFlags.NonPublic);
            var entry = Activator.CreateInstance(entryType, true); entryType.GetField("PlayerId").SetValue(entry, 42L);
            entryType.GetField("Position").SetValue(entry, new Vector2(2, 0));
            ((IDictionary)typeof(ArcadeCombatViewController).GetField("remotePlayers", Hidden).GetValue(view)).Add(42L, entry);
            var hit = new ArcadeHerdImpactState { Remaining = 1.7f, DirectionX = 1 };
            view.SetPlayerHerdImpact(0,default); view.SetPlayerHerdImpact(42,default);
            view.SetPlayerHerdImpact(0, hit); view.SetPlayerHerdImpact(0, hit);
            Assert.That(loader.Requests,Is.Empty,"受击姿态不能反推实际命中");view.PlayHerdContact(Vector2.zero);
            view.TickPlayerFeedback(.08f);
            view.SetPlayerHerdImpact(42, hit); view.SetPlayerHerdImpact(42, hit);
            view.PlayHerdContact(new Vector2(2,0));
            Assert.That(loader.Requests.Count(r => r.Address.EndsWith("ArcadeHerdImpact")), Is.EqualTo(2));
        }
        [Test] public void ResumeFirstImpactStateCannotReplayHiddenContact()
        {
            var messages = new List<string>();
            using (var feedback = new ArcadeHerdFeedback(root.transform, null, (name, p, gain) => messages.Add(name), () => { }))
            {
                feedback.ObserveImpact(99, default, Vector2.zero); feedback.SetEnabled(false); feedback.SetEnabled(true);
                feedback.ObserveImpact(99, new ArcadeHerdImpactState { Remaining = 1.6f }, Vector2.zero);
                Assert.That(messages, Is.Empty, "恢复的首帧只能建立受击基线");
            }
        }
        [Test] public void FatalPlayerContactSurvivesProtocolCopyAndRepeatedPacketsStaySilent()
        {
            Sync(.01); var presentation = new OnlineArcadePresentationSystem();
            ((ICanSetArchitecture)presentation).SetArchitecture(Game.Interface);
            var method = typeof(OnlineArcadePresentationSystem).GetMethod("SyncHerdContacts", Hidden);
            Assert.That(method, Is.Not.Null, "致死玩家接触必须有独立权威反馈，不能依赖存活击飞");
            typeof(OnlineArcadePresentationSystem).GetField("view", Hidden).SetValue(presentation, view);
            OnlineArcadeBattleSnapshot Copy(long sequence)
            {
                using var info = ET.OnlineArcadeBattleInfo.Create(); info.RoundGeneration = 7; info.HazardElapsedSeconds = 10;
                var player = ET.OnlineArcadeBattlePlayerInfo.Create(); player.PlayerId = 42; player.Alive = false;
                foreach (var value in new[] { ("HerdContactSequence", (object)sequence), ("HerdContactX", 2f), ("HerdContactZ", 0f), ("HerdContactElapsedSeconds", 10d) })
                {
                    var property = player.GetType().GetProperty(value.Item1); Assert.That(property, Is.Not.Null); property.SetValue(player, value.Item2);
                }
                info.Players.Add(player);
                var convert = typeof(OnlineArcadeRunSystem).GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single(m => m.ReturnType == typeof(OnlineArcadeBattleSnapshot) && m.GetParameters().Length == 1);
                return (OnlineArcadeBattleSnapshot)convert.Invoke(null, new object[] { info });
            }
            method.Invoke(presentation, new object[] { Copy(0), true });
            var hit = Copy(1); method.Invoke(presentation, new object[] { hit, true }); method.Invoke(presentation, new object[] { hit, true });
            Assert.That(loader.Requests.Count(r => r.Address.EndsWith("ArcadeHerdImpact")), Is.EqualTo(1));
            method.Invoke(presentation, new object[] { Copy(2), false }); method.Invoke(presentation, new object[] { Copy(2), true });
            Assert.That(loader.Requests.Count(r => r.Address.EndsWith("ArcadeHerdImpact")), Is.EqualTo(1), "暂停期间仅推进消费基线");
            view.TickPlayerFeedback(.08f); method.Invoke(presentation,new object[]{Copy(3),true});
            Assert.That(loader.Requests.Count(r=>r.Address.EndsWith("ArcadeHerdImpact")),Is.EqualTo(2),"解除暂停的首个新接触不能被旧View暂停状态吞掉");
            typeof(OnlineArcadePresentationSystem).GetMethod("EndPresentation",Hidden).Invoke(presentation,null);
            Assert.That(((IDictionary)typeof(OnlineArcadePresentationSystem).GetField("herdContactSequences",Hidden).GetValue(presentation)).Count,Is.Zero,"同run/round重连前必须清基线");
        }
        [Test] public void BlockingUiWhileAppPlayingCancelsLateHoofThroughActualSystemUpdate()
        {
            Step(); Assert.That(loader.Requests, Is.Not.Empty);
            var architecture = new Game(); var model = new ArcadeCombatModel(); architecture.RegisterModel(model);
            var app = new AppStateModel(); architecture.RegisterModel(app); app.CurrentState.Value = AppState.Playing;
            var run = new ArcadeRunModel(); architecture.RegisterModel(run); run.CommitSafeNode(new ArcadeRunSaveData { IsCasualRoute = true });
            var runtime = new ArcadeCombatRuntime(); model.Begin(runtime);
            var system = new ArcadeCombatSystem(); ((ICanSetArchitecture)system).SetArchitecture(architecture);
            foreach (var pair in new[] { ("view", (object)view), ("catalog", catalog), ("arena", catalog.Arenas.Single(a => a.ArenaNumber == 22)) })
                typeof(ArcadeCombatSystem).GetField(pair.Item1, Hidden).SetValue(system, pair.Item2);
            Assert.That((bool)typeof(ArcadeCombatSystem).GetMethod("RoundContentActive", Hidden).Invoke(system, new object[] { runtime }), Is.True);
            var blocker = new GameObject("HerdBlockingUi", typeof(BaseUI));
            var ctrl = new Blocker(blocker.GetComponent<BaseUI>());
            var dict = (IDictionary)typeof(UIHelper).GetField("UICtrlDict", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            dict.Add("HerdFeedbackTestBlocker", ctrl);
            try
            {
                Assert.That(UIHelper.HasUIOpen, Is.True);
                typeof(ArcadeCombatSystem).GetMethod("OnUpdate", Hidden).Invoke(system, null);
                Assert.That(audio.ActiveSoundCount,Is.Zero,"阻塞UI必须先取消待加载的旧蹄声");loader.CompleteAll();
                Assert.That(audio.ActiveSoundCount, Is.Zero);
            }
            finally { dict.Remove("HerdFeedbackTestBlocker"); Object.DestroyImmediate(blocker); }
        }
        private sealed class Blocker : BaseUICtrl
        {
            public Blocker(BaseUI ui) { BaseUI = ui; typeof(BaseUICtrl).GetField("_isActive", Hidden).SetValue(this, true); }
            public override UILayer Layer() => UILayer.Dialog;
            protected override string Path() => "unused";
            protected override void OnInit() { } protected override void OnShow() { } protected override void OnHide() { }
        }
        [Test] public void ApprovedDustAndFourAudioAssetsArePersistentAndCollectable()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeHerdVisual.prefab");
            var property = new SerializedObject(prefab.GetComponent<ArcadeHerdVisual>()).FindProperty("dustMaterial");
            Assert.That(property, Is.Not.Null, "正式牛模板应引用已采用尘土材质");
            var material = property.objectReferenceValue as Material; Assert.That(material, Is.Not.Null);
            Assert.That(EditorUtility.IsPersistent(material.GetTexture("_BaseMap")), Is.True);
            object Read(object obj, string name) => obj.GetType().GetProperty(name)?.GetValue(obj) ?? obj.GetType().GetField(name)?.GetValue(obj);
            var setting = AssetDatabase.LoadAssetAtPath<Object>("Assets/AssetBundleCollectorSetting.asset");
            var result = setting.GetType().GetMethod("BeginCollect").Invoke(setting, new object[] { "DefaultPackage", true, false });
            var addresses = ((IEnumerable)Read(result, "CollectAssets")).Cast<object>().Select(a => (string)Read(a, "Address")).ToHashSet();
            foreach (string name in new[] { "Hoof", "Hoof2", "Hoof3", "Hoof4", "Scrape", "Snort", "Impact" })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/Sound/ArcadeHerd" + name + ".wav");
                Assert.That(clip, Is.Not.Null); Assert.That(clip.length, Is.GreaterThan(.15f)); Assert.That(clip.channels, Is.EqualTo(1));
                Assert.That(addresses.Contains("Audio/Sound/ArcadeHerd" + name), Is.True);
            }
        }
        private static void Reset()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }
        private static GameObject BindAudio()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Game/Audio/FirepowerLoopAudioMixer.mixer");
            var go = new GameObject("AudioRoot"); var bindings = go.AddComponent<AudioRootBindings>();
            bindings.Mixer = mixer; bindings.MusicGroup = mixer.FindMatchingGroups("Music").First(); bindings.SoundGroup = mixer.FindMatchingGroups("Sound").First();
            var pool = new GameObject("SoundPoolRoot").transform; pool.SetParent(go.transform); bindings.SoundPoolRoot = pool;
            var playing = new GameObject("PlayingSoundRoot").transform; playing.SetParent(go.transform); bindings.PlayingSoundRoot = playing;
            var a = new GameObject("MusicA", typeof(AudioSource)); a.transform.SetParent(go.transform); bindings.MusicSourceA = a.GetComponent<AudioSource>();
            var b = new GameObject("MusicB", typeof(AudioSource)); b.transform.SetParent(go.transform); bindings.MusicSourceB = b.GetComponent<AudioSource>();
            return go;
        }
        private sealed class Loader : IResourceLoader
        {
            public readonly List<Request> Requests = new List<Request>(); public readonly List<object> Released = new List<object>();
            public readonly List<AudioClip> Clips = new List<AudioClip>();
            public T LoadAsset<T>(string path) where T : Object => null;
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> done) where T : Object
            { var request = new Request { Address = path, Identity = new object(), Complete = clip => done(clip as T) }; Requests.Add(request); return new ResourceLoadHandle(request.Identity); }
            public void Release(ResourceLoadHandle handle) { if (handle.IsValid) Released.Add(handle.RawHandle); }
            public void CompleteAll()
            {
                foreach (var request in Requests.Where(r => !r.Done).ToArray())
                { request.Done = true; var clip = AudioClip.Create(request.Address, 13230, 1, 44100, false); Clips.Add(clip); request.Complete(clip); }
            }
            public sealed class Request { public string Address; public object Identity; public bool Done; public Action<AudioClip> Complete; }
        }
    }
}
