using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>验证统一主菜单直接开局的资源门禁；使用临时存档，不加载地图或连接网络。</summary>
    public sealed class UnifiedMainMenuTests
    {
        private Game architecture;
        private GameObject instance;
        private StartUICtrl controller;
        private DeferredPreparation preparation;
        private string saveRoot;
        private GameObject uiRoot;

        [SetUp]
        public void SetUp()
        {
            UIHelper.ResetForTests();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
            uiRoot = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            foreach (var layer in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
                new GameObject(layer, typeof(RectTransform)).transform.SetParent(uiRoot.transform, false);
            var events = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            events.transform.SetParent(uiRoot.transform, false);
            uiRoot.AddComponent<UIRoot>();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(architecture, true);
            ResourceLoader.SetLoaderForTests(new LocalizationLoader());
            LocalizationLanguages.ResetForTests();
            architecture.RegisterModel(new MapSceneModel());
            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterUtility(new LocalizationUtility());
            saveRoot = System.IO.Path.Combine(Application.temporaryCachePath, "UnifiedMenu-" + Guid.NewGuid().ToString("N"));
            preparation = new DeferredPreparation();
            architecture.RegisterUtility<IGameplayResourcePreparation>(preparation);
            instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab"));
            controller = new StartUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, instance.GetComponent<StartUI>());
            typeof(BaseUICtrl).GetField("_isActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, true);
        }

        [TearDown]
        public void TearDown()
        {
            controller.Hide();
            preparation.CompleteAll();
            UIHelper.ResetForTests();
            if (uiRoot != null) Object.DestroyImmediate(uiRoot);
            Object.DestroyImmediate(instance);
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, true);
        }

        [Test]
        public void StartOpensSurvivorSelectionWithoutModePageOrCampaignDownload()
        {
            architecture.RegisterModel(new SurvivorModel());
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() =>
                new Tables(file => new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", file + ".bytes"))))));
            var survivor = new SurvivorSystem();
            architecture.RegisterSystem(survivor);
            typeof(SurvivorSystem).GetField("goldStore", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(survivor, new SurvivorGoldFileStore(1, saveRoot));
            try
            {
                Invoke("OnInit");
                var view = instance.GetComponent<StartUI>();
                var sound = (UnityEngine.Events.UnityAction)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction), controller,
                    typeof(StartUICtrl).GetMethod("PlayMenuClick", BindingFlags.Instance | BindingFlags.NonPublic));
                view.BtnNewGame.onClick.RemoveListener(sound);
                view.BtnNewGame.onClick.Invoke();
                Assert.That(view.ArcadeModePanel.activeSelf, Is.False);
                Assert.That(UIHelper.GetUICtrl<SurvivorEntryUICtrl>()?.IsShow(), Is.True);
                Assert.That(preparation.Requests, Is.Empty);
                UIHelper.HideUI<SurvivorEntryUICtrl>();
                view.BtnNewGame.onClick.Invoke();
                Assert.That(UIHelper.GetUICtrl<SurvivorEntryUICtrl>()?.IsShow(), Is.True);
                Assert.That(view.ArcadeModePanel.activeSelf, Is.False);
            }
            finally { LifeCycle.ClearForTests(); }
        }

        private void Invoke(string method) => typeof(StartUICtrl).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);

        private sealed class DeferredPreparation : IGameplayResourcePreparation
        {
            public readonly List<UniTaskCompletionSource<bool>> Requests = new List<UniTaskCompletionSource<bool>>();
            public readonly List<Func<bool>> Owners = new List<Func<bool>>();
            public readonly List<string> MapIds = new List<string>();
            public UniTask<bool> PrepareAsync(string mapId, Func<bool> isCurrent = null, CancellationToken cancellationToken = default)
            {
                var request = new UniTaskCompletionSource<bool>();
                Requests.Add(request); Owners.Add(isCurrent); MapIds.Add(mapId);
                return request.Task;
            }
            public void CompleteAll() { foreach (var request in Requests) request.TrySetResult(false); }
        }

        private sealed class LocalizationLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Config/Localization/" + path);
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> loaded) where T : Object
            { loaded?.Invoke(LoadAsset<T>(path)); return ResourceLoadHandle.Empty; }
            public void Release(ResourceLoadHandle handle) { }
        }
    }
}
