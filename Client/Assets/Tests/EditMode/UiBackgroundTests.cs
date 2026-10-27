using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>验证正式加载页的场地身份与背景绑定；不启动地图、网络或真实存档。</summary>
    public sealed class UiBackgroundTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Art = "Assets/Game/Art/UI/ArcadeBackgrounds/";
        private Game architecture;
        private object previousArchitecture;
        private IResourceLoader previousLoader;
        private GameObject root;
        private LoadingUI view;
        private LoadingUICtrl controller;
        private readonly int[] ids = { 1, 21, 22, 23, 24, 25, 5, 7, 10, 20 };
        private readonly string[] files = { "01-island", "02-docks", "03-farm", "04-graveyard", "05-temple", "06-market", "07-henge", "08-fort", "09-koi", "10-nest" };

        [SetUp] public void Setup()
        {
            var field = typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
            previousArchitecture = field.GetValue(null);
            architecture = new Game(); field.SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", Hidden).SetValue(architecture, true);
            architecture.RegisterModel(new ArcadeRunModel());
            architecture.RegisterModel(new OnlineArcadeRunModel());
            architecture.RegisterModel(new LocalizationModel());
            previousLoader = ResourceLoader.Current; ResourceLoader.Use(new Loader());
            LocalizationLanguages.ResetForTests();
            architecture.RegisterUtility(new LocalizationUtility());
            root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Common/Loading.prefab"));
            view = root.GetComponent<LoadingUI>(); controller = new LoadingUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI", Hidden).SetValue(controller, view);
            typeof(BaseUICtrl).GetField("_initialized", Hidden).SetValue(controller, true);
            typeof(BaseUICtrl).GetField("_isActive", Hidden).SetValue(controller, true);
            typeof(LoadingUICtrl).GetMethod("OnInit", Hidden).Invoke(controller, null);
        }

        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(root);
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, previousArchitecture);
            ResourceLoader.Use(previousLoader); LocalizationLanguages.ResetForTests();
        }

        private void SetRun(int id, int ordinal = 1)
        {
            var data = new ArcadeRunSaveData { ArenaConfigNumber = id, NextArenaNumber = ordinal, IsCasualRoute = true };
            typeof(ArcadeRunModel).GetMethod("CommitSafeNode", Hidden).Invoke(architecture.GetModel<ArcadeRunModel>(), new object[] { data });
            architecture.SendEvent(new ArcadeRunChangedEvent(ArcadeRunPhase.SafeCheckpoint, data));
        }
        private void AssertImage(string file) => Assert.That(AssetDatabase.GetAssetPath(view.MapArtwork.GetComponent<Image>().sprite), Is.EqualTo(Art + file + ".png"));

        [Test] public void TenStagesUseConfigIdentityInsteadOfOrdinalAndRefreshWhileVisible()
        {
            controller.BeginEntryPreparation(MapIds.Arcade);
            for (int i = 0; i < ids.Length; i++) { SetRun(ids[i], i + 1); AssertImage(files[i]); }
        }

        [Test] public void UnknownStageAndDownloadResetPreviousArtwork()
        {
            SetRun(20, 10); controller.BeginEntryPreparation(MapIds.Arcade); AssertImage("10-nest");
            SetRun(999); AssertImage("01-island");
            SetRun(22, 3); AssertImage("03-farm");
            controller.BeginResourceDownload(7); AssertImage("01-island");
        }

        [Test] public void ActiveOnlineBattleOverridesResidualSinglePlayerSave()
        {
            SetRun(22, 3);
            var battle = new OnlineArcadeBattleSnapshot(10, 1, 4, 0, 0, 2, 1, 0, null, null, arenaConfigNumber: 20);
            var snapshot = new OnlineArcadeRunClientSnapshot(true, 42, 3, 1, OnlineArcadeRunPhase.Arena, 9, 10, "test", null, default, default, battle);
            architecture.GetModel<OnlineArcadeRunModel>().Publish(snapshot);
            controller.BeginEntryPreparation(MapIds.Arcade); AssertImage("10-nest");
        }

        [Test] public void NewEntryAndNonArcadeMapUseDefaultArtwork()
        {
            controller.BeginEntryPreparation(MapIds.Arcade); AssertImage("01-island");
            SetRun(25, 6); AssertImage("06-market");
            controller.BeginEntryPreparation("unrelated-map"); AssertImage("01-island");
        }

        [Test] public void FormalMenuAndLobbyReferenceAdoptedImages()
        {
            var menu = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab");
            var lobby = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Online/OnlineRoom.prefab");
            Assert.That(AssetDatabase.GetAssetPath(menu.transform.Find("Background/Artwork").GetComponent<RawImage>().texture), Is.EqualTo(Art + "main-menu.png"));
            Assert.That(AssetDatabase.GetAssetPath(lobby.transform.Find("LandscapeBackground").GetComponent<RawImage>().texture), Is.EqualTo(Art + "online-lobby.png"));
        }

        [Test] public void LegacyOrdinalWithoutConfigIdentityUsesDefaultInsteadOfNewStageArt()
        {
            var data = new ArcadeRunSaveData { NextArenaNumber = 25, IsCasualRoute = false };
            typeof(ArcadeRunModel).GetMethod("CommitSafeNode", Hidden).Invoke(architecture.GetModel<ArcadeRunModel>(), new object[] { data });
            controller.BeginEntryPreparation(MapIds.Arcade); AssertImage("01-island");
            var battle = new OnlineArcadeBattleSnapshot(22, 1, 4, 0, 0, 2, 1, 0, null, null);
            architecture.GetModel<OnlineArcadeRunModel>().Publish(new OnlineArcadeRunClientSnapshot(true, 42, 3, 1, OnlineArcadeRunPhase.Arena, 21, 22, "test", null, default, default, battle));
            AssertImage("01-island");
        }

        [Test] public void GeneratedArtworkReferenceAndAllRequiredViewFieldsResolve()
        {
            var field = typeof(LoadingUI).GetField("LoadingArtwork");
            Assert.That(field, Is.Not.Null, "必须通过 ReferenceCollector 生成背景组件引用");
            var component = field.GetValue(view) as Component;
            Assert.That(component, Is.Not.Null);
            Assert.That(root.GetComponent<ReferenceCollector>().Get<Component>("LoadingArtwork"), Is.SameAs(component));
            foreach (var f in typeof(LoadingUI).GetFields().Where(f => typeof(Object).IsAssignableFrom(f.FieldType)))
                Assert.That(f.GetValue(view) as Object, Is.Not.Null, f.Name);
            Assert.That(view.MapArtwork.GetComponent<Image>().raycastTarget, Is.False);
        }

        private sealed class Loader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Config/Localization/" + path);
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> loaded) where T : Object { loaded?.Invoke(LoadAsset<T>(path)); return ResourceLoadHandle.Empty; }
            public void Release(ResourceLoadHandle handle) { }
        }
    }
}
