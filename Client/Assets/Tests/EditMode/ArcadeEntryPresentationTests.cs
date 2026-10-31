using System.Linq;
using System.Reflection;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>验证街机入口的生成引用与新局/恢复显示边界；不启动战斗、不写存档。</summary>
    public sealed class ArcadeEntryPresentationTests
    {
        private const string Path = "Assets/Game/Prefabs/UI/Arcade/ArcadeResume.prefab";
        private Game architecture;

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            ResourceLoader.SetLoaderForTests(new EntryLocalizationLoader());
            LocalizationLanguages.ResetForTests();
            // 入口生命周期测试仅注册展示依赖，禁止初始化地图、网络或真实存档系统。
            architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(architecture, true);
            architecture.RegisterModel(new ArcadeRunModel());
            architecture.RegisterModel(new MapSceneModel());
            architecture.RegisterModel(new OnlineArcadeRunModel());
            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterUtility(new LocalizationUtility());
            // 只提供空联机快照供入口判定；未注册网络端口，不发起任何网络或存档操作。
            architecture.RegisterSystem(new OnlineArcadeRunSystem());
        }

        [TearDown]
        public void TearDown()
        {
            architecture.Deinit();
            ResetArchitecture();
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
        }

        private static void ResetArchitecture() => typeof(Architecture<Game>).GetField("mArchitecture",
            BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);

        [Test]
        public void EntryReopen_RestoresDisabledStartButton()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Path));
            try
            {
                var ctrl = BindVisible(instance);
                instance.GetComponent<ArcadeResumeUI>().BtnContinue.interactable = false;
                ctrl.Hide();
                Reopen(ctrl, instance);
                Assert.That(instance.GetComponent<ArcadeResumeUI>().BtnContinue.IsInteractable(), Is.True);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void EntryReopen_InvalidatesPreviousResourceRequest()
        {
            var preparation = new DeferredPreparation();
            architecture.RegisterUtility<IGameplayResourcePreparation>(preparation);
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Path));
            try
            {
                var ctrl = BindVisible(instance);
                Invoke(ctrl, "ContinueRunAsync");
                ctrl.Hide();
                Reopen(ctrl, instance);
                Assert.That(preparation.Requests[0].IsCurrent(), Is.False,
                    "同一 View 关闭后重开，旧资源请求仍必须保持过期");
            }
            finally { preparation.CompleteAll(); Object.DestroyImmediate(instance); }
        }

        [Test]
        public void EntryOldFailure_DoesNotEnableButtonDuringNewRequest()
        {
            var preparation = new DeferredPreparation();
            architecture.RegisterUtility<IGameplayResourcePreparation>(preparation);
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Path));
            try
            {
                var ctrl = BindVisible(instance);
                Invoke(ctrl, "ContinueRunAsync");
                ctrl.Hide();
                Reopen(ctrl, instance);
                Invoke(ctrl, "ContinueRunAsync");
                preparation.Requests[0].Completion.TrySetResult(false);
                Assert.That(instance.GetComponent<ArcadeResumeUI>().BtnContinue.IsInteractable(), Is.False,
                    "旧请求失败不能解除新请求的防重复点击状态");
                preparation.Requests[1].Completion.TrySetResult(false);
                Assert.That(instance.GetComponent<ArcadeResumeUI>().BtnContinue.IsInteractable(), Is.True);
            }
            finally { preparation.CompleteAll(); Object.DestroyImmediate(instance); }
        }

        [Test]
        public void EntryPreparationFailure_AllowsRetryAndBlocksDuplicateSubmission()
        {
            var preparation = new DeferredPreparation();
            architecture.RegisterUtility<IGameplayResourcePreparation>(preparation);
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Path));
            try
            {
                var ctrl = BindVisible(instance);
                Invoke(ctrl, "ContinueRunAsync");
                Invoke(ctrl, "ContinueRunAsync");
                Assert.That(preparation.Requests.Count, Is.EqualTo(1));
                preparation.Requests[0].Completion.TrySetResult(false);
                Assert.That(instance.GetComponent<ArcadeResumeUI>().BtnContinue.IsInteractable(), Is.True);
                Invoke(ctrl, "ContinueRunAsync");
                Assert.That(preparation.Requests.Count, Is.EqualTo(2));
                Assert.That(instance.GetComponent<ArcadeResumeUI>().BtnContinue.IsInteractable(), Is.False);
            }
            finally { preparation.CompleteAll(); Object.DestroyImmediate(instance); }
        }

        [Test]
        public void EntryOldSuccess_DoesNotSubmitAfterReopen()
        {
            var preparation = new DeferredPreparation();
            architecture.RegisterUtility<IGameplayResourcePreparation>(preparation);
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Path));
            var errors = new List<string>();
            try
            {
                var ctrl = BindVisible(instance);
                Invoke(ctrl, "ContinueRunAsync");
                ctrl.Hide();
                Reopen(ctrl, instance);
                Invoke(ctrl, "ContinueRunAsync");
                // 夹具没有启动局的领域服务，旧成功回调若越过门禁，会被记录为非法领域调用。
                using (Log.CaptureForTests((type, message) => { if (type == LogType.Error) errors.Add(message); }))
                    preparation.Requests[0].Completion.TrySetResult(true);
                Assert.That(errors, Is.Empty);
                Assert.That(architecture.GetModel<ArcadeRunModel>().HasRun, Is.False);
                Assert.That(instance.GetComponent<ArcadeResumeUI>().BtnContinue.IsInteractable(), Is.False);
            }
            finally { preparation.CompleteAll(); Object.DestroyImmediate(instance); }
        }

        [Test]
        public void EntryPreparationException_RestoresButtonAndAllowsRetry()
        {
            var preparation = new DeferredPreparation();
            architecture.RegisterUtility<IGameplayResourcePreparation>(preparation);
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Path));
            var errors = new List<string>();
            try
            {
                var ctrl = BindVisible(instance);
                Invoke(ctrl, "ContinueRunAsync");
                using (Log.CaptureForTests((type, message) => { if (type == LogType.Error) errors.Add(message); }))
                    preparation.Requests[0].Completion.TrySetException(new InvalidOperationException("resource probe failure"));
                Assert.That(errors, Has.Count.EqualTo(1));
                Assert.That(errors[0], Does.Contain("resource probe failure"));
                var view = instance.GetComponent<ArcadeResumeUI>();
                Assert.That(view.TextMessage.text, Is.EqualTo(Loc.T("ui.arcade.error.open")));
                Assert.That(view.BtnContinue.IsInteractable(), Is.True);
                Invoke(ctrl, "ContinueRunAsync");
                Assert.That(preparation.Requests.Count, Is.EqualTo(2));
                Assert.That(view.BtnContinue.IsInteractable(), Is.False);
            }
            finally { preparation.CompleteAll(); Object.DestroyImmediate(instance); }
        }

        [Test]
        public void EntryDestroyedView_InvalidatesPendingRequest()
        {
            var preparation = new DeferredPreparation();
            architecture.RegisterUtility<IGameplayResourcePreparation>(preparation);
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Path));
            var errors = new List<string>();
            try
            {
                var ctrl = BindVisible(instance);
                Invoke(ctrl, "ContinueRunAsync");
                Object.DestroyImmediate(instance);
                Assert.That(preparation.Requests[0].IsCurrent(), Is.False);
                using (Log.CaptureForTests((type, message) => { if (type == LogType.Error) errors.Add(message); }))
                    preparation.Requests[0].Completion.TrySetResult(true);
                Assert.That(errors, Is.Empty);
            }
            finally { preparation.CompleteAll(); if (instance != null) Object.DestroyImmediate(instance); }
        }

        [TestCase("entryPreparing")]
        [TestCase("arcadePreparationActive")]
        [TestCase("mapLoadingActive")]
        public void LoadingCompletionWaitsForEveryOwner(string owner)
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Common/Loading.prefab"));
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var ctrl = new LoadingUICtrl();
                var ui = instance.GetComponent<LoadingUI>();
                typeof(BaseUICtrl).GetField("BaseUI", flags).SetValue(ctrl, ui);
                typeof(LoadingUICtrl).GetField(owner, flags).SetValue(ctrl, true);
                typeof(LoadingUICtrl).GetMethod("SetMapProgress", flags).Invoke(ctrl, new object[] { 1f });
                Assert.That(ui.PercentText.text, Is.Not.EqualTo("100%"), "准备所有者未释放时不能显示完成");
                Assert.That(ui.ProgressSlider.gameObject.activeSelf, Is.True, "后续准备仍纳入同一进度条");
                typeof(LoadingUICtrl).GetField(owner, flags).SetValue(ctrl, false);
                typeof(LoadingUICtrl).GetMethod("SetMapProgress", flags).Invoke(ctrl, new object[] { 1f });
                Assert.That(ui.PercentText.text, Is.EqualTo("100%"));
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void LoadingEntryStagesIgnoreOldRequestsAndOnlyCompleteOnSuccess(bool succeeded)
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Common/Loading.prefab"));
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var ctrl = new LoadingUICtrl();
                var ui = instance.GetComponent<LoadingUI>();
                typeof(BaseUICtrl).GetField("BaseUI", flags).SetValue(ctrl, ui);
                typeof(LoadingUICtrl).GetField("entryPreparing", flags).SetValue(ctrl, true);
                typeof(LoadingUICtrl).GetField("entryRequestId", flags).SetValue(ctrl, 2);
                typeof(LoadingUICtrl).GetField("mapProgress", flags).SetValue(ctrl, 1f);
                ctrl.ReportEntryPreparation(2, .85f, "ui.arcade.island.loading");
                Assert.That(ui.PercentText.text, Is.EqualTo("85%"));
                ctrl.ReportEntryPreparation(1, .99f, "ui.loading.stage.Scene");
                ctrl.EndEntryPreparation(1);
                Assert.That(ui.PercentText.text, Is.EqualTo("85%"));
                ctrl.ReportEntryPreparation(2, .95f, "ui.loading.stage.Activate");
                Assert.That(ui.PercentText.text, Is.EqualTo("95%"));
                Assert.That(ui.MessageText.text, Is.EqualTo(Loc.T("ui.loading.stage.Activate")));
                ctrl.EndEntryPreparation(2, succeeded);
                Assert.That(ui.PercentText.text == "100%", Is.EqualTo(succeeded));
            }
            finally { Object.DestroyImmediate(instance); }
        }

        private static ArcadeResumeUICtrl BindVisible(GameObject instance)
        {
            var ctrl = new ArcadeResumeUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(ctrl, instance.GetComponent<ArcadeResumeUI>());
            Reopen(ctrl, instance);
            return ctrl;
        }

        private static void Reopen(ArcadeResumeUICtrl ctrl, GameObject instance)
        {
            instance.SetActive(true);
            typeof(BaseUICtrl).GetField("_isActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ctrl, true);
            Invoke(ctrl, "OnShow");
        }

        private static void Invoke(ArcadeResumeUICtrl ctrl, string method) =>
            typeof(ArcadeResumeUICtrl).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ctrl, null);

        /// <summary>仅替换外部资源准备的等待；完成回调仍执行真实入口 Controller。</summary>
        private sealed class DeferredPreparation : IGameplayResourcePreparation
        {
            public readonly List<Request> Requests = new List<Request>();
            public UniTask<bool> PrepareAsync(string mapId, Func<bool> isCurrent = null,
                CancellationToken cancellationToken = default)
            {
                var request = new Request { IsCurrent = isCurrent };
                Requests.Add(request);
                return request.Completion.Task;
            }
            public void CompleteAll()
            {
                foreach (var request in Requests) request.Completion.TrySetResult(false);
            }
            public sealed class Request
            {
                public Func<bool> IsCurrent;
                public readonly UniTaskCompletionSource<bool> Completion = new UniTaskCompletionSource<bool>();
            }
        }

        // 使用项目正式语言表验证 UI，避免 EditMode 依赖尚未启动的 YooAsset 包。
        private sealed class EntryLocalizationLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object =>
                AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Config/Localization/" + path);
            public ResourceLoadHandle LoadAssetAsync<T>(string path, System.Action<T> onLoaded) where T : Object
            { onLoaded?.Invoke(LoadAsset<T>(path)); return ResourceLoadHandle.Empty; }
            public void Release(ResourceLoadHandle handle) { }
        }

        [Test]
        public void EntryPrefab_HasArtworkSafeAreaAndResolvedGeneratedReferences()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            Assert.That(root.GetComponent<SafeAreaFitter>(), Is.Not.Null);
            Assert.That(root.GetComponentsInChildren<Image>(true).Any(i => i.sprite != null &&
                AssetDatabase.GetAssetPath(i.sprite).Contains("ArcadeEntry")), Is.True);
            var view = root.GetComponent<ArcadeResumeUI>();
            var collector = root.GetComponent<ReferenceCollector>();
            foreach (var field in typeof(ArcadeResumeUI).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Assert.That(field.GetValue(view), Is.Not.Null, field.Name);
                Assert.That(collector.data.Single(r => r.key == field.Name).gameObject, Is.Not.Null, field.Name);
            }
            Assert.That(typeof(ArcadeResumeUI).GetField("ResumeDetails"), Is.Not.Null);
        }

        [Test]
        public void DetailsRender_SeparatesScoreAndAllCheckpointStats()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Path));
            try
            {
                var view = instance.GetComponent<ArcadeResumeUI>();
                var ctrl = new ArcadeResumeUICtrl();
                typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ctrl, view);
                var saved = new ArcadeRunSaveData { Score = 12750, CompletedArenaCount = 4, RemainingLives = 2, RunGold = 280 };
                typeof(ArcadeResumeUICtrl).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ctrl, new object[] { saved });
                var collector = instance.GetComponent<ReferenceCollector>();
                foreach (var expected in new[] { ("TextArenaCount", "04"), ("TextLives", "02"), ("TextGold", "280") })
                {
                    var entry = collector.data.SingleOrDefault(item => item.key == expected.Item1);
                    Assert.That(entry, Is.Not.Null, "详情数据必须独立显示：" + expected.Item1);
                    Assert.That(((TMP_Text)entry.gameObject).text, Is.EqualTo(expected.Item2));
                }
                Assert.That(view.TextRewards.text, Is.EqualTo("12,750"));
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void DetailsPrefab_DecorationsHaveRenderersAndPrimaryActionKeepsItsButton()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            var view = prefab.GetComponent<ArcadeResumeUI>();
            foreach (var graphic in prefab.GetComponentsInChildren<ArcadeResumeDecoration>(true))
            {
                Assert.That(graphic.GetComponent<CanvasRenderer>(), Is.Not.Null, graphic.name);
                Assert.That(graphic.raycastTarget, Is.False, "装饰不能抢占按钮输入：" + graphic.name);
            }
            Assert.That(view.BtnContinue.targetGraphic.transform.IsChildOf(view.BtnContinue.transform), Is.True);
            Assert.That(view.TextRewards.fontSizeMax, Is.GreaterThan(view.TextSummary.fontSizeMax));
            Assert.That(view.BtnAbandon.GetComponent<Image>().color.a, Is.Zero, "放弃入口不能与继续入口同等醒目");
            foreach (var button in new[] { view.BtnClose, view.BtnAbandon })
            {
                Assert.That(button.targetGraphic, Is.InstanceOf<TMP_Text>(), "透明按钮必须使用可见文字提供焦点反馈");
                Assert.That(button.colors.selectedColor, Is.Not.EqualTo(button.colors.normalColor));
            }
        }

        [Test]
        public void DetailsOnlineToSoloResult_ResetsPlayerColumnsAndKeepsAuthoritativeStats()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Path));
            try
            {
                var view = instance.GetComponent<ArcadeResumeUI>();
                var ctrl = new ArcadeResumeUICtrl();
                typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ctrl, view);
                var players = new[] {
                    new OnlineArcadePlayerSnapshot(1, "玩家一\n名称", 2, 280, 0, 0, "", true, settlementConfirmed: true, score: 12750),
                    new OnlineArcadePlayerSnapshot(2, "玩家二", 1, 50, 0, 0, "", false, score: 8340) };
                var online = new OnlineArcadeRunClientSnapshot(true, 1, 1, 1, OnlineArcadeRunPhase.SafeCheckpoint,
                    4, 5, "", players, OnlineArcadeOperationState.Idle, OnlineArcadeResultCode.Success);
                var renderOnline = typeof(ArcadeResumeUICtrl).GetMethod("RenderOnlineDetails", BindingFlags.Instance | BindingFlags.NonPublic);
                renderOnline.Invoke(ctrl, new object[] { online, false });
                Assert.That(view.SoloStats.activeSelf, Is.False);
                Assert.That(view.OnlinePlayers.activeSelf, Is.True);
                Assert.That(view.TextRewards.text, Is.EqualTo("21,090"));
                Assert.That(view.TextPlayerNames.text, Is.EqualTo("玩家一 名称\n玩家二"));
                Assert.That(view.TextPlayerScores.text, Is.EqualTo("12,750\n8,340"));
                Assert.That(view.TextPlayerStates.text, Is.EqualTo(Loc.T("ui.arcade.online.ready") + "\n" + Loc.T("ui.arcade.online.not_ready")));
                view.TextMessage.text = "模拟操作错误";
                renderOnline.Invoke(ctrl, new object[] { online, false });
                Assert.That(view.TextMessage.text, Is.EqualTo("模拟操作错误"), "刷新准备状态不能覆盖操作失败信息");
                renderOnline.Invoke(ctrl, new object[] { online, true });
                Assert.That(view.TextPlayerStates.text, Is.EqualTo(Loc.T("ui.arcade.details.confirmed") + "\n" + Loc.T("ui.arcade.details.pending_confirmation")),
                    "结果页显示结算确认状态，不能继续使用战斗准备状态");
                var saved = new ArcadeRunSaveData { Score = 5000, CompletedArenaCount = 3, RemainingLives = 0, RunGold = 75, Outcome = "failed" };
                typeof(ArcadeResumeUICtrl).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ctrl, new object[] { saved });
                Assert.That(view.SoloStats.activeSelf, Is.True);
                Assert.That(view.OnlinePlayers.activeSelf, Is.False);
                Assert.That(view.TextPlayerNames.text, Is.Empty);
                Assert.That(view.TextRewards.text, Is.EqualTo("5,000"));
                Assert.That(view.TextArenaCount.text, Is.EqualTo("03"));
                Assert.That(view.TextLives.text, Is.EqualTo("00"));
                Assert.That(view.TextGold.text, Is.EqualTo("75"));
                Assert.That(view.TextSummary.text, Is.EqualTo(Loc.T("ui.arcade.result.failed")));
                Assert.That(view.BtnSettle.gameObject.activeSelf, Is.True);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void EntryRender_OnlyShowsSavedProgressAndDestructiveActionsForExistingRun(bool hasRun, bool hasResult)
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Path));
            try
            {
                var view = instance.GetComponent<ArcadeResumeUI>();
                var ctrl = new ArcadeResumeUICtrl();
                typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ctrl, view);
                typeof(ArcadeResumeUICtrl).GetField("selectionMode", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ctrl, !hasRun);
                var snapshot = hasRun ? new ArcadeRunSaveData { CompletedArenaCount = 4, RemainingLives = 2, RunGold = 280, Outcome = hasResult ? "failed" : null } : null;
                typeof(ArcadeResumeUICtrl).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ctrl, new object[] { snapshot });
                var field = typeof(ArcadeResumeUI).GetField("ResumeDetails");
                Assert.That(field, Is.Not.Null, "恢复信息必须拥有明确的生成容器引用");
                Assert.That(((GameObject)field.GetValue(view)).activeSelf, Is.EqualTo(hasRun));
                Assert.That(view.BtnSettle.gameObject.activeInHierarchy, Is.EqualTo(hasResult));
                Assert.That(view.BtnAbandon.gameObject.activeInHierarchy, Is.EqualTo(hasRun && !hasResult));
                Assert.That(view.BtnContinue.gameObject.activeInHierarchy, Is.EqualTo(hasRun && !hasResult));
                Assert.That(view.BtnOnline.gameObject.activeInHierarchy, Is.False,
                    "进度和结果弹窗不能再次提供模式选择");
                Assert.That(view.BtnContinue.GetComponentInChildren<TextMeshProUGUI>().text,
                    Is.EqualTo(Loc.T("ui.arcade.resume")));
                Assert.That(view.BtnContinue.GetComponentInChildren<TextMeshProUGUI>().text, Does.Not.StartWith("ui."));
                Assert.That(view.BtnContinue.GetComponentInChildren<LocalizedText>(true), Is.Null,
                    "开始/继续由 Controller 切换，固定本地化组件不能覆盖动态标签");
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}
