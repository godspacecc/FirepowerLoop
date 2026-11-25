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

        [SetUp] public void Setup()
        {
            var field = typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
            previousArchitecture = field.GetValue(null);
            architecture = new Game(); field.SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", Hidden).SetValue(architecture, true);
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

        private void AssertImage(string file) => Assert.That(AssetDatabase.GetAssetPath(view.MapArtwork.GetComponent<Image>().sprite), Is.EqualTo(Art + file + ".png"));

        private int BeginSurvivor()
        {
            var method=typeof(LoadingUICtrl).GetMethod("BeginSurvivorEntryPreparation");
            Assert.That(method,Is.Not.Null,"缺少幸存者独立加载背景入口");
            return (int)method.Invoke(controller,null);
        }
        [Test] public void SurvivorMapCompletionCannotClosePendingBattlePreparation()
        {
            int menu=BeginSurvivor(),battle=BeginSurvivor();
            architecture.SendEvent(new MapLoadingStartedEvent(MapIds.Arcade));
            architecture.SendEvent(new MapLoadingCompletedEvent(MapIds.Arcade));
            controller.EndEntryPreparation(menu);
            Assert.That(controller.IsShow(),Is.True,"基础地图完成及旧菜单释放均不能关闭战场准备遮罩");
            controller.EndEntryPreparation(battle);
            Assert.That(controller.IsShow(),Is.False);
        }
        [Test] public void SurvivorArtworkStaysThroughoutPreparation()
        {
            BeginSurvivor();AssertImage("survivor-loading");
            architecture.SendEvent(new MapLoadingStartedEvent(MapIds.Arcade));AssertImage("survivor-loading");
            controller.BeginResourceDownload(7);AssertImage("survivor-loading");
        }
        [Test] public void SurvivorStaleReleaseCannotClearNewRequestAndGenericEntryResetsArt()
        {
            int old=BeginSurvivor();int current=BeginSurvivor();controller.EndEntryPreparation(old);AssertImage("survivor-loading");
            controller.EndEntryPreparation(current);controller.BeginEntryPreparation(MapIds.Arcade);AssertImage("01-island");
        }
        [Test] public void UnrelatedMapDoesNotRetainSurvivorArtwork()
        {
            BeginSurvivor();architecture.SendEvent(new MapLoadingStartedEvent("unrelated-map"));AssertImage("01-island");
        }

        [Test] public void FormalMenuAndLobbyReferenceAdoptedImages()
        {
            var menu = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab");
            var lobby = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Online/OnlineRoom.prefab");
            Assert.That(AssetDatabase.GetAssetPath(menu.transform.Find("Background/Artwork").GetComponent<RawImage>().texture), Is.EqualTo(Art + "main-menu.png"));
            Assert.That(AssetDatabase.GetAssetPath(lobby.transform.Find("LandscapeBackground").GetComponent<RawImage>().texture), Is.EqualTo(Art + "online-lobby.png"));
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
