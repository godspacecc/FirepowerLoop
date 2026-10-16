using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
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

        [SetUp]
        public void SetUp()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(architecture, true);
            ResourceLoader.SetLoaderForTests(new LocalizationLoader());
            LocalizationLanguages.ResetForTests();
            architecture.RegisterModel(new ArcadeRunModel());
            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterUtility(new LocalizationUtility());
            var system = new ArcadeRunSystem();
            architecture.RegisterSystem(system);
            saveRoot = System.IO.Path.Combine(Application.temporaryCachePath, "UnifiedMenu-" + Guid.NewGuid().ToString("N"));
            system.ConfigureForTests(saveRoot);
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
            Object.DestroyImmediate(instance);
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, true);
        }

        [Test]
        public void SinglePlayerClick_PreparesArenaDirectlyAndRejectsRepeatedClick()
        {
            Invoke("OnNewGameClick");
            Invoke("OnNewGameClick");
            Assert.That(preparation.Requests.Count, Is.EqualTo(1), "无存档时应直接准备街机资源，而不是再打开模式选择页");
            Assert.That(preparation.MapIds, Is.EqualTo(new[] { global::Game.MapIds.Arcade }));
            Assert.That(instance.GetComponent<StartUI>().BtnNewGame.interactable, Is.False);
            Assert.That(architecture.GetModel<ArcadeRunModel>().HasRun, Is.False, "资源准备完成前不能提交新局");
        }

        [Test]
        public void SinglePlayerPreparationFailure_AllowsRetryWithoutCreatingSave()
        {
            Invoke("OnNewGameClick");
            Assert.That(preparation.Requests.Count, Is.EqualTo(1));
            preparation.Requests[0].TrySetResult(false);
            Assert.That(instance.GetComponent<StartUI>().BtnNewGame.interactable, Is.True);
            Assert.That(architecture.GetSystem<ArcadeRunSystem>().InspectPending().Status, Is.EqualTo(SaveFileStatus.Missing));
            Invoke("OnNewGameClick");
            Assert.That(preparation.Requests.Count, Is.EqualTo(2));
        }

        [Test]
        public void LeavingMenu_InvalidatesPendingStartBeforeItCanWriteSave()
        {
            Invoke("OnNewGameClick");
            Assert.That(preparation.Requests.Count, Is.EqualTo(1));
            controller.Hide();
            Assert.That(preparation.Owners[0](), Is.False);
            preparation.Requests[0].TrySetResult(true);
            Assert.That(architecture.GetSystem<ArcadeRunSystem>().InspectPending().Status, Is.EqualTo(SaveFileStatus.Missing));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SavedRun_ShowsProgressOrResultInsteadOfOfferingToOverwrite(bool hasResult)
        {
            var system = architecture.GetSystem<ArcadeRunSystem>();
            Assert.That(system.TryStartNewRun(null, out var snapshot, out var error), Is.True, error);
            snapshot.Score = 12750;
            snapshot.CompletedArenaCount = 4;
            snapshot.NextArenaNumber = 5;
            snapshot.Checkpoint.ArenaNumber = 5;
            snapshot.RemainingLives = 2;
            snapshot.Outcome = hasResult ? "failed" : null;
            Assert.That(new ArcadeRunFileStore(saveRoot).Save(snapshot, out error), Is.True, error);
            Invoke("RefreshSaveButtons");
            var view = instance.GetComponent<StartUI>();
            Assert.That(view.BtnNewGame.gameObject.activeSelf, Is.False, "已有进度或待处理结果时不能直接覆盖开新局");
            Assert.That(view.BtnContinue.gameObject.activeSelf, Is.True);
            Assert.That(view.BtnContinue.GetComponentInChildren<TMPro.TMP_Text>(true).text,
                Is.EqualTo(hasResult ? "查看本局结果" : "继续本局"));
            Assert.That(view.RunSummaryText.gameObject.activeSelf, Is.True);
            Assert.That(view.RunSummaryText.text, Does.Contain("12750"));
            if (!hasResult)
            {
                Assert.That(view.RunSummaryText.text, Does.Contain("5"));
                Assert.That(view.RunSummaryText.text, Does.Contain("2"));
            }
            Assert.That(system.InspectPending().Data.RunId, Is.EqualTo(snapshot.RunId));
            Assert.That(preparation.Requests, Is.Empty);
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
