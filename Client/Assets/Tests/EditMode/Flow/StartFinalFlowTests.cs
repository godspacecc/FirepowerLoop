using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Boot;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class StartFinalFlowTests
    {
        private string tempRoot;
        private FakeChecker checker;
        private FakeRemoteVersionProbe probe;
        private FakeMapLoader loader;
        private FakeRestart restart;
        private FakeClock clock;
        private FakeNoticeClient noticeClient;
        private int hudPrefabLoadCount;

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            UIHelper.ResetForTests();
            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayStartFinalTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            checker = new FakeChecker();
            probe = new FakeRemoteVersionProbe();
            loader = new FakeMapLoader();
            restart = new FakeRestart();
            clock = new FakeClock(DateTime.Parse("2026-07-20T00:00:00Z").ToUniversalTime());
            noticeClient = new FakeNoticeClient();
            hudPrefabLoadCount = 0;
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IResourceUpdateRuntimeChecker>(checker);
                architecture.RegisterUtility<IRemoteResourceVersionProbe>(probe);
                architecture.RegisterUtility<IMapContentLoader>(loader);
                architecture.RegisterUtility<IApplicationRestartUtility>(restart);
                architecture.RegisterUtility<IResourceUpdateNoticeClient>(noticeClient);
                architecture.RegisterUtility<IRuntimeConfigProvider>(new StartRuntimeConfigProvider());
            };
            ResourceLoader.SetLoaderForTests(new LocalizationLoader());
            LocalizationLanguages.ResetForTests();
            UIHelper.SetPrefabLoaderForTests(CreatePrefab);
            CreateTestUIRoot();
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot, clock);
            ResetSilentCheckStarted();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            UIHelper.ResetForTests();
            ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = _ => { };
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }

            DestroySceneObject("UIRoot");
            DestroySceneObject("EventSystem");
            DestroySceneObject("Main Camera");
            DestroySceneObject(SceneRoot.PoolRootName);
            ResetSilentCheckStarted();
        }

        [Test]
        public void StartPrefabHasFinalButtonsVersionTextAndUpdateRedPoint()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab");
            var ui = prefab.GetComponent<StartUI>();

            Assert.NotNull(ui.BtnNewGame);
            Assert.NotNull(ui.BtnContinue);
            Assert.NotNull(ui.BtnSetting);
            Assert.NotNull(ui.BtnCheckUpdate);
            Assert.NotNull(ui.BtnExit);
            Assert.NotNull(ui.VersionText);
            Assert.NotNull(ui.UpdateStatusText);
            Assert.IsNull(prefab.transform.Find("Panel/HintText"));
            Assert.IsFalse(ui.VersionText.gameObject.activeSelf);
            Assert.IsFalse(ui.UpdateStatusText.gameObject.activeSelf);
            Assert.NotNull(ui.UpdateRedPoint);
            Assert.NotNull(ui.ButtonGroup);
            Assert.IsTrue(prefab.transform.Find("Panel/ButtonGroup/BtnSave") == null);
            Assert.IsNull(typeof(StartUI).GetField("BtnSave"));
            Assert.IsFalse(prefab.GetComponent<ReferenceCollector>().data.Any(item => item.key == "BtnSave"));
            Assert.That(ui.UpdateRedPoint.NodeId, Is.EqualTo(RedPointIds.StartResourceUpdate));

            var observer = ui.UpdateRedPoint.transform as RectTransform;
            var mark = typeof(RedPointView)
                .GetField("markRoot", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(ui.UpdateRedPoint) as GameObject;
            Assert.NotNull(mark);
            Assert.IsTrue(mark.transform.IsChildOf(observer));
            Assert.That(mark.transform.parent.name, Is.EqualTo("UpdateRedPoint"));
            Assert.That(mark.name, Is.EqualTo("Mark"));
            Assert.IsTrue(observer.gameObject.activeSelf);
            Assert.IsFalse(mark.activeSelf);
            Assert.IsNull(observer.GetComponent<Image>());
            var markRect = mark.transform as RectTransform;
            Assert.That(markRect.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(markRect.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(markRect.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(markRect.sizeDelta, Is.EqualTo(Vector2.zero));
            var markImage = mark.GetComponent<Image>();
            Assert.NotNull(markImage);
            Assert.That(markImage.color, Is.EqualTo(new Color(0.95f, 0.18f, 0.14f, 1f)));
            Assert.That(markImage.type, Is.EqualTo(Image.Type.Simple));
            Assert.IsTrue(markImage.raycastTarget);
            Assert.IsTrue(markImage.maskable);

            Assert.That(GetStartButtonLocalizationKey(ui.BtnContinue), Is.EqualTo("ui.start.continue"));
            Assert.That(GetStartButtonLocalizationKey(ui.BtnCheckUpdate), Is.EqualTo("ui.start.check_update"));
            Assert.That(GetStartButtonLocalizationKey(ui.BtnSetting), Is.EqualTo("ui.start.settings"));
        }

        [Test]
        public void StartPrefabUsesRightAlignedReadableMenuLayout()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab");
            var panel = prefab.transform.Find("Panel") as RectTransform;
            var background = prefab.transform.Find("Background") as RectTransform;
            var ui = prefab.GetComponent<StartUI>();

            Assert.NotNull(background);
            Assert.NotNull(panel);
            Assert.That(panel.anchorMin, Is.EqualTo(Vector2.one));
            Assert.That(panel.sizeDelta, Is.EqualTo(new Vector2(430f, 600f)));
            AssertStartButtonGroupContract(ui, panel);
            var titleText = prefab.transform.Find("Panel/TitleText").GetComponent<TMP_Text>();
            var checkUpdateText = ui.BtnCheckUpdate.GetComponentInChildren<TMP_Text>(true);
            Assert.That(titleText.fontSize, Is.EqualTo(76f));
            Assert.IsTrue(titleText.enableAutoSizing);
            Assert.IsTrue(checkUpdateText.enableAutoSizing);
        }

        [Test]
        public void NoSaveButtonsCollapseWithoutGap()
        {
            var start = UIHelper.ShowUI<StartUICtrl>();
            var ui = GetBaseUI<StartUI>(start);

            AssertVisibleButtonLayout(ui, new[]
            {
                "BtnNewGame",
                "BtnCheckUpdate",
                "BtnSetting",
                "BtnExit"
            });
        }

        [Test]
        public void ValidBaseButtonsCollapseWithoutGap()
        {
            Assert.That(Game.Interface.GetSystem<SaveSystem>().CreateBase(out _, out var error), Is.True, error);
            var start = UIHelper.ShowUI<StartUICtrl>();
            var ui = GetBaseUI<StartUI>(start);

            AssertVisibleButtonLayout(ui, new[]
            {
                "BtnContinue",
                "BtnCheckUpdate",
                "BtnSetting",
                "BtnExit"
            });
        }

        [Test]
        public void MenuPrefabsUseChineseDefaultVisibleText()
        {
            var combined =
                File.ReadAllText("Assets/Game/Prefabs/UI/Home/Start.prefab");

            Assert.That(combined, Does.Not.Contain("m_text: New Game"));
            Assert.That(combined, Does.Not.Contain("m_text: Continue"));
            Assert.That(combined, Does.Not.Contain("m_text: Check Update"));
            Assert.That(combined, Does.Not.Contain("m_text: Settings"));
            Assert.That(combined, Does.Not.Contain("m_text: Exit"));
        }

        [Test]
        public void ContinueLoadsFixedBaseThenCommitsBaseDomain()
        {
            var saves = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(saves.CreateBase(out var created, out var error), Is.True, error);

            var start = UIHelper.ShowUI<StartUICtrl>();
            var ui = GetBaseUI<StartUI>(start);
            Assert.IsTrue(ui.BtnContinue.interactable);
            Assert.IsNull(UIHelper.GetUICtrl<PlayerConditionHudUICtrl>());

            ui.BtnContinue.onClick.Invoke();

            Assert.That(loader.LoadedMapId, Is.EqualTo(MapIds.Base));
            Assert.That(Game.Interface.GetModel<SaveModel>().CurrentDomain, Is.EqualTo(OfflineSaveDomain.Base));
            Assert.That(Game.Interface.GetModel<SaveModel>().CurrentBase, Is.Not.Null);
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Playing));
            Assert.IsFalse(start.IsShow());
            Assert.IsFalse(UIHelper.GetUICtrl<LoadingUICtrl>()?.IsShow() ?? false);
            Assert.That(UIHelper.GetUICtrl<PlayerConditionHudUICtrl>()?.IsShow(), Is.True);
        }

        [Test]
        public void ResumePendingExpeditionSuccessShowsBothRuntimeHuds()
        {
            ((Game)Game.Interface).RegisterSystem(new ExpeditionFlowSystem());
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var baseData, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(baseData, out error), Is.True, error);
            var expedition = CreatePendingPlaying(save, out error);
            Assert.That(save.SaveExpedition(expedition, out error), Is.True, error);

            var start = UIHelper.ShowUI<StartUICtrl>();
            start.ResumePendingExpeditionAsync().GetAwaiter().GetResult();

            Assert.That(loader.LoadedMapId, Is.EqualTo(MapIds.SurvivalSample));
            Assert.IsFalse(start.IsShow());
            Assert.IsFalse(UIHelper.GetUICtrl<LoadingUICtrl>()?.IsShow() ?? false);
            Assert.That(UIHelper.GetUICtrl<PlayerConditionHudUICtrl>()?.IsShow(), Is.True);
            Assert.That(UIHelper.GetUICtrl<ExpeditionHudUICtrl>()?.IsShow(), Is.True);
        }

        [Test]
        public void ResumePendingExpeditionFailureKeepsStartAndDoesNotShowExpeditionHud()
        {
            ((Game)Game.Interface).RegisterSystem(new ExpeditionFlowSystem());
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var baseData, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(baseData, out error), Is.True, error);
            var expedition = CreatePendingPlaying(save, out error);
            Assert.That(save.SaveExpedition(expedition, out error), Is.True, error);
            loader.NextLoadError = "test map load failure";

            var start = UIHelper.ShowUI<StartUICtrl>();
            LogAssert.Expect(LogType.Error, new Regex(@"\[LoadingUICtrl\] 地图加载失败：test map load failure"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[StartUICtrl\] 恢复远征失败：test map load failure"));
            start.ResumePendingExpeditionAsync().GetAwaiter().GetResult();

            Assert.IsTrue(start.IsShow());
            Assert.That(UIHelper.GetUICtrl<LoadingUICtrl>()?.IsShow(), Is.True);
            Assert.That(UIHelper.GetUICtrl<ExpeditionHudUICtrl>(), Is.Null);
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
            Assert.That(save.InspectExpedition().IsValid, Is.True);
        }

        [Test]
        public void ResumeLegacyExpeditionBindsEquivalentTaskBeforeShowingHud()
        {
            ((Game)Game.Interface).RegisterSystem(new ExpeditionFlowSystem());
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var baseData, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(baseData, out error), Is.True, error);
            var expedition = CreatePendingPlaying(save, out error);
            Assert.That(expedition, Is.Not.Null, error);
            expedition.ElapsedGameMinutes = 321.5f;
            expedition.TargetGameMinutes = 1440f;
            var raw = JObject.FromObject(expedition);
            raw["Version"] = 2;
            raw.Remove("TaskSchemaVersion");
            raw.Remove("LegacySourceVersion");
            ((JObject)raw["Sections"]).Remove(ExpeditionTaskModel.SaveSectionId);
            var store = new SaveFileStore(tempRoot);
            Directory.CreateDirectory(store.SavesDirectory);
            var sourceText = raw.ToString();
            File.WriteAllText(store.ExpeditionPath, sourceText);
            Game.Interface.GetSystem<ExpeditionTaskSystem>().Clear();
            Game.Interface.GetModel<ExpeditionModel>().Clear();

            var start = UIHelper.ShowUI<StartUICtrl>();
            start.ResumePendingExpeditionAsync().GetAwaiter().GetResult();

            Assert.That(loader.LoadedMapId, Is.EqualTo(MapIds.SurvivalSample));
            var task = Game.Interface.GetModel<ExpeditionTaskModel>().Tasks.Single();
            Assert.That(task.TaskId, Is.EqualTo("expedition.survival-sample.survive"));
            Assert.That(task.InstanceId, Is.EqualTo($"{expedition.ExpeditionId}:expedition.survival-sample.survive"));
            Assert.That(task.TargetAmount, Is.EqualTo(1440f));
            Assert.That(task.CurrentAmount, Is.EqualTo(321.5f));
            Assert.That(task.Status, Is.EqualTo(ExpeditionTaskStatus.InProgress));
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().CanReturn, Is.False);
            Assert.That(UIHelper.GetUICtrl<ExpeditionHudUICtrl>()?.IsShow(), Is.True);
            Assert.That(File.ReadAllText(store.ExpeditionPath), Is.EqualTo(sourceText));
        }

        [Test]
        public void ResumeVersionThreeRejectsTaskAndStatusMismatchBeforeShowingHud()
        {
            ((Game)Game.Interface).RegisterSystem(new ExpeditionFlowSystem());
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var baseData, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(baseData, out error), Is.True, error);
            var valid = CreatePendingPlaying(save, out error);
            Assert.That(valid, Is.Not.Null, error);
            var validRaw = JObject.FromObject(valid);
            Game.Interface.GetSystem<ExpeditionTaskSystem>().Clear();
            Game.Interface.GetModel<ExpeditionModel>().Clear();
            var store = new SaveFileStore(tempRoot);
            var start = UIHelper.ShowUI<StartUICtrl>();
            LogAssert.ignoreFailingMessages = true;

            var cases = new[]
            {
                CreateStatusMismatch(validRaw, ExpeditionStatus.ReturnQualified, completeTask: false, emptyTasks: false),
                CreateStatusMismatch(validRaw, ExpeditionStatus.Playing, completeTask: true, emptyTasks: false),
                CreateStatusMismatch(validRaw, ExpeditionStatus.Playing, completeTask: false, emptyTasks: true)
            };

            foreach (var raw in cases)
            {
                if (File.Exists(store.ExpeditionBackupPath)) File.Delete(store.ExpeditionBackupPath);
                var sourceText = raw.ToString();
                File.WriteAllText(store.ExpeditionPath, sourceText);

                start.ResumePendingExpeditionAsync().GetAwaiter().GetResult();

                Assert.That(loader.LoadedMapId, Is.Null);
                Assert.That(Game.Interface.GetModel<ExpeditionModel>().CanReturn, Is.False);
                Assert.That(Game.Interface.GetModel<ExpeditionModel>().Status, Is.EqualTo(ExpeditionStatus.None));
                Assert.That(Game.Interface.GetModel<ExpeditionTaskModel>().Capture(), Is.Null);
                Assert.That(UIHelper.GetUICtrl<ExpeditionHudUICtrl>(), Is.Null);
                Assert.That(start.IsShow(), Is.True);
                Assert.That(File.ReadAllText(store.ExpeditionPath), Is.EqualTo(sourceText));
            }
        }

        [Test]
        public void StartShowsOnlyNewGameWithoutSaveAndOnlyContinueWithSave()
        {
            var start = UIHelper.ShowUI<StartUICtrl>();
            var ui = GetBaseUI<StartUI>(start);

            Assert.IsTrue(ui.BtnNewGame.gameObject.activeSelf);
            Assert.IsFalse(ui.BtnContinue.gameObject.activeSelf);

            UIHelper.HideUI<StartUICtrl>();
            Assert.That(Game.Interface.GetSystem<SaveSystem>().CreateBase(out _, out var error), Is.True, error);

            start = UIHelper.ShowUI<StartUICtrl>();
            ui = GetBaseUI<StartUI>(start);

            Assert.IsFalse(ui.BtnNewGame.gameObject.activeSelf);
            Assert.IsTrue(ui.BtnContinue.gameObject.activeSelf);
            Assert.IsTrue(ui.BtnContinue.interactable);
        }

        [Test]
        public void NewGameCreatesBaseAndEntersBaseMap()
        {
            Assert.IsNull(UIHelper.GetUICtrl<PlayerConditionHudUICtrl>());
            var start = UIHelper.ShowUI<StartUICtrl>();
            var startUi = GetBaseUI<StartUI>(start);
            startUi.BtnNewGame.onClick.Invoke();
            Assert.That(loader.LoadedMapId, Is.EqualTo(MapIds.Base));
            Assert.That(Game.Interface.GetModel<SaveModel>().CurrentDomain, Is.EqualTo(OfflineSaveDomain.Base));
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Playing));
            Assert.IsFalse(start.IsShow());
            Assert.IsFalse(UIHelper.GetUICtrl<LoadingUICtrl>()?.IsShow() ?? false);
            Assert.That(UIHelper.GetUICtrl<PlayerConditionHudUICtrl>()?.IsShow(), Is.True);
        }

        [Test]
        public void UnsupportedOldSaveAllowsNewGameAndPreservesOriginalFile()
        {
            var oldPath=Path.Combine(tempRoot,"Saves","base.json");Directory.CreateDirectory(Path.GetDirectoryName(oldPath));
            File.WriteAllText(oldPath,"{\"Version\":4}");
            var start=UIHelper.ShowUI<StartUICtrl>();var ui=GetBaseUI<StartUI>(start);
            Assert.That(ui.BtnNewGame.gameObject.activeSelf,Is.True);Assert.That(ui.BtnContinue.gameObject.activeSelf,Is.False);
            Assert.That(Game.Interface.GetSystem<SaveSystem>().CreateBase(out _,out var createError),Is.True,createError);
            Assert.That(Game.Interface.GetSystem<SaveSystem>().InspectBase().IsValid,Is.True);
            Assert.That(File.ReadAllText(oldPath),Is.EqualTo("{\"Version\":4}"));
        }

        [UnityTest]
        public IEnumerator ManualUpdateAvailableShowsRestartAndLaterOnly()
        {
            checker.Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.UpdateAvailable, "1", "2", 2, 2048, null));
            restart.Result = false;
            restart.Error = "cannot restart";
            var start = UIHelper.ShowUI<StartUICtrl>();
            var ui = GetBaseUI<StartUI>(start);

            ui.BtnCheckUpdate.onClick.Invoke();

            yield return WaitUntilUi<ResourceUpdateMessageUICtrl>();
            var message = GetBaseUI<ResourceUpdateMessageUI>(UIHelper.GetUICtrl<ResourceUpdateMessageUICtrl>());
            var buttons = message.GetComponentsInChildren<MessageButtonBoxUI>(false);
            Assert.That(((RectTransform)message.transform.Find("Panel")).sizeDelta, Is.EqualTo(new Vector2(600, 280)));
            Assert.That(message.GetComponent<Image>().color.a, Is.LessThanOrEqualTo(0.25f));
            Assert.That(message.BtnRestart, Is.Not.Null);
            Assert.That(message.BtnLater, Is.Not.Null);
            Assert.That(message.TitleText.text, Is.EqualTo(Texts.StartUpdateAvailable));
            Assert.That(buttons.Length, Is.EqualTo(2));
            Assert.That(buttons[0].ButtonText.text, Is.EqualTo(Texts.StartUpdateRestartNow));
            Assert.That(buttons[1].ButtonText.text, Is.EqualTo(Texts.StartUpdateLater));

            var errors = new List<string>();
            using (CaptureErrors(errors))
            {
                message.BtnRestart.onClick.Invoke();
            }

            var failed = GetBaseUI<ResourceUpdateMessageUI>(UIHelper.GetUICtrl<ResourceUpdateMessageUICtrl>());
            Assert.That(failed.TitleText.text, Is.EqualTo(Texts.StartUpdateRestartFailed));
            Assert.That(failed.ContentText.text, Is.EqualTo(Loc.T("ui.start.update.restart_manual")));
            Assert.That(UIHelper.GetUICtrl<ResourceUpdateMessageUICtrl>().IsShow(), Is.True);
            message.BtnLater.onClick.Invoke();
            Assert.That(UIHelper.GetUICtrl<ResourceUpdateMessageUICtrl>().IsShow(), Is.False);
            AssertHasError(errors, "cannot restart");
            Assert.IsTrue(restart.Called);
        }

        [UnityTest]
        public IEnumerator ManualUpdateAvailableWithNoticeUsesNoticeContentAndButtons()
        {
            InitializeHostLatest();
            checker.Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(
                ResourceUpdateStatus.UpdateAvailable,
                "1",
                "2",
                2,
                2048,
                null,
                BootResourceSource.HostLatest,
                "http://127.0.0.1:8000"));
            noticeClient.Next = ResourceUpdateNoticeResult.Ok(new ResourceUpdateNoticeContent(
                "2",
                "公告标题",
                "公告摘要",
                new[] { "\u7b2c\u4e00\u9879", "\u7b2c\u4e8c\u9879" }));
            var start = UIHelper.ShowUI<StartUICtrl>();
            var ui = GetBaseUI<StartUI>(start);

            ui.BtnCheckUpdate.onClick.Invoke();

            yield return WaitUntilUi<ResourceUpdateMessageUICtrl>();
            var message = GetBaseUI<ResourceUpdateMessageUI>(UIHelper.GetUICtrl<ResourceUpdateMessageUICtrl>());
            var buttons = message.GetComponentsInChildren<MessageButtonBoxUI>(false);
            Assert.That(message.TitleText.text, Is.EqualTo("公告标题"));
            Assert.That(message.ContentText.text, Is.EqualTo("\u516c\u544a\u6458\u8981\n1. \u7b2c\u4e00\u9879\n2. \u7b2c\u4e8c\u9879"));
            Assert.That(buttons.Length, Is.EqualTo(2));
            Assert.That(buttons[0].ButtonText.text, Is.EqualTo(Texts.StartUpdateRestartNow));
            Assert.That(buttons[1].ButtonText.text, Is.EqualTo(Texts.StartUpdateLater));
        }

        [UnityTest]
        public IEnumerator ManualUpdateAvailableWithEmptyNoticeTitleUsesStandardTitle()
        {
            InitializeHostLatest();
            checker.Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(
                ResourceUpdateStatus.UpdateAvailable,
                "1",
                "2",
                0,
                0,
                null,
                BootResourceSource.HostLatest,
                "http://127.0.0.1:8000"));
            noticeClient.Next = ResourceUpdateNoticeResult.Ok(new ResourceUpdateNoticeContent(
                "2",
                "",
                "公告摘要",
                Array.Empty<string>()));
            var start = UIHelper.ShowUI<StartUICtrl>();
            var ui = GetBaseUI<StartUI>(start);

            ui.BtnCheckUpdate.onClick.Invoke();

            yield return WaitUntilUi<ResourceUpdateMessageUICtrl>();
            var message = GetBaseUI<ResourceUpdateMessageUI>(UIHelper.GetUICtrl<ResourceUpdateMessageUICtrl>());
            Assert.That(message.TitleText.text, Is.EqualTo(Texts.StartUpdateAvailable));
            Assert.That(message.ContentText.text, Is.EqualTo("公告摘要"));
        }

        [UnityTest]
        public IEnumerator ManualNoUpdateWithNoticeIncludesNoticeTitleInBodyAndCloseOnly()
        {
            InitializeHostLatest();
            checker.Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(
                ResourceUpdateStatus.NoUpdate,
                "2",
                "2",
                0,
                0,
                null,
                BootResourceSource.HostLatest,
                "http://127.0.0.1:8000"));
            noticeClient.Next = ResourceUpdateNoticeResult.Ok(new ResourceUpdateNoticeContent(
                "2",
                "公告标题",
                "公告摘要",
                new[] { "\u7b2c\u4e00\u9879" }));
            var start = UIHelper.ShowUI<StartUICtrl>();
            var ui = GetBaseUI<StartUI>(start);

            ui.BtnCheckUpdate.onClick.Invoke();

            yield return WaitUntilUi<MessageUICtrl>();
            var message = GetBaseUI<MessageUI>(UIHelper.GetUICtrl<MessageUICtrl>());
            var buttons = message.ButtonRoot.GetComponentsInChildren<MessageButtonBoxUI>(false);
            Assert.That(message.TitleText.text, Is.EqualTo(Texts.StartUpdateCurrentNoticeTitle));
            Assert.That(message.ContentText.text, Is.EqualTo("\u516c\u544a\u6807\u9898\n\u516c\u544a\u6458\u8981\n1. \u7b2c\u4e00\u9879"));
            Assert.That(buttons.Length, Is.EqualTo(1));
            Assert.That(buttons[0].ButtonText.text, Is.EqualTo(Texts.CommonClose));
        }

        [UnityTest]
        public IEnumerator SilentCheckDoesNotCallNoticeClient()
        {
            InitializeHostLatest();
            var start = UIHelper.ShowUI<StartUICtrl>();
            yield return null;
            Assert.That(noticeClient.CallCount, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator NoUpdateAndFailedChecksDoNotBlockContinue()
        {
            Assert.That(Game.Interface.GetSystem<SaveSystem>().CreateBase(out _, out var error), Is.True, error);
            var start = UIHelper.ShowUI<StartUICtrl>();
            var ui = GetBaseUI<StartUI>(start);

            checker.Next = ResourceUpdateCheckResult.Fail("1", "offline");
            var errors = new List<string>();
            using (CaptureErrors(errors))
            {
                ui.BtnCheckUpdate.onClick.Invoke();
                for (var i = 0; i < 5; i++)
                {
                    yield return null;
                }
            }

            AssertHasError(errors, "offline");
            Assert.IsTrue(ui.BtnContinue.interactable);
            Assert.IsFalse(Game.Interface.GetSystem<RedPointSystem>().GetState(RedPointIds.StartResourceUpdate).Active);
        }

        [Test]
        public void BuiltinFallbackOnShowDoesNotAutomaticallyProbe()
        {
            InitializeBuiltinFallback();

            var start = UIHelper.ShowUI<StartUICtrl>();
            var ui = GetBaseUI<StartUI>(start);

            Assert.That(probe.CallCount, Is.EqualTo(0));
            Assert.IsFalse(ui.UpdateStatusText.gameObject.activeSelf);
            Assert.IsTrue(ui.VersionText.gameObject.activeSelf);
            Assert.That(ui.VersionText.text, Is.EqualTo("vbuiltin-1"));
            Assert.IsFalse(Game.Interface.GetSystem<RedPointSystem>().GetState(RedPointIds.StartResourceUpdate).Active);
        }

        [Test]
        public void UnknownVersionAndIdleUpdateStatusStayHidden()
        {
            Game.Interface.GetSystem<ResourceUpdateSystem>()
                .InitializeStartupInfo(new BootResourceStartupInfo(
                    BootResourceSource.BuiltinFallback,
                    "http://127.0.0.1:8000",
                    string.Empty,
                    string.Empty));

            var start = UIHelper.ShowUI<StartUICtrl>();
            var ui = GetBaseUI<StartUI>(start);

            Assert.IsFalse(ui.VersionText.gameObject.activeSelf);
            Assert.IsFalse(ui.UpdateStatusText.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator BuiltinFallbackManualFailureShowsServerUnavailable()
        {
            InitializeBuiltinFallback();
            probe.Next = RemoteResourceVersionProbeResult.Fail("offline");
            var start = UIHelper.ShowUI<StartUICtrl>();
            var ui = GetBaseUI<StartUI>(start);

            var errors = new List<string>();
            using (CaptureErrors(errors))
            {
                ui.BtnCheckUpdate.onClick.Invoke();
                yield return null;
            }

            AssertHasError(errors, "offline");
            Assert.That(probe.CallCount, Is.EqualTo(1));
            Assert.IsTrue(ui.UpdateStatusText.gameObject.activeSelf);
            Assert.That(ui.UpdateStatusText.text, Is.EqualTo(Texts.StartUpdateServerUnavailable));
            Assert.IsFalse(Game.Interface.GetSystem<RedPointSystem>().GetState(RedPointIds.StartResourceUpdate).Active);
        }

        [UnityTest]
        public IEnumerator BuiltinFallbackSameVersionShowsNoRemoteUpdate()
        {
            InitializeBuiltinFallback();
            probe.Next = RemoteResourceVersionProbeResult.Ok("builtin-1");
            var start = UIHelper.ShowUI<StartUICtrl>();
            var ui = GetBaseUI<StartUI>(start);

            ui.BtnCheckUpdate.onClick.Invoke();
            yield return null;

            Assert.That(probe.CallCount, Is.EqualTo(1));
            Assert.IsFalse(ui.UpdateStatusText.gameObject.activeSelf);
            Assert.IsFalse(Game.Interface.GetSystem<RedPointSystem>().GetState(RedPointIds.StartResourceUpdate).Active);
        }

        [UnityTest]
        public IEnumerator BuiltinFallbackDifferentVersionShowsRestartPromptWithoutFakeSize()
        {
            InitializeBuiltinFallback();
            probe.Next = RemoteResourceVersionProbeResult.Ok("host-2");
            var start = UIHelper.ShowUI<StartUICtrl>();
            var ui = GetBaseUI<StartUI>(start);

            ui.BtnCheckUpdate.onClick.Invoke();

            yield return WaitUntilUi<ResourceUpdateMessageUICtrl>();
            var message = GetBaseUI<ResourceUpdateMessageUI>(UIHelper.GetUICtrl<ResourceUpdateMessageUICtrl>());
            var buttons = message.GetComponentsInChildren<MessageButtonBoxUI>(false);

            Assert.That(probe.CallCount, Is.EqualTo(1));
            Assert.That(message.TitleText.text, Is.EqualTo(Texts.StartUpdateRestartRequired));
            Assert.That(message.ContentText.text, Does.Contain("host-2"));
            Assert.That(message.ContentText.text, Does.Not.Contain("0 B"));
            Assert.That(message.ContentText.text, Does.Not.Contain("0 \u4e2a\u6587\u4ef6"));
            Assert.That(buttons.Length, Is.EqualTo(2));
            Assert.That(buttons[0].ButtonText.text, Is.EqualTo(Texts.StartUpdateRestartNow));
            Assert.That(buttons[1].ButtonText.text, Is.EqualTo(Texts.StartUpdateLater));
            LifeCycle.LateUpdate();
            Assert.IsTrue(Game.Interface.GetSystem<RedPointSystem>().GetState(RedPointIds.StartResourceUpdate).Active);
        }

        [Test]
        public void StartControllerOnlyShowsWhilePaused()
        {
            var source = File.ReadAllText(Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                "Assets/Game/Scripts/Controller/UICtrl/Home/StartUICtrl.cs"));

            Assert.That(source, Does.Not.Contain("EnsureMainMenuState"));
            Assert.That(source, Does.Contain("appState != AppState.Paused"));
            Assert.That(source, Does.Not.Contain("TryTransition(AppState.MainMenu)"));
            Assert.That(source, Does.Not.Contain("anchoredPosition"));
            Assert.That(source, Does.Not.Contain("SetSiblingIndex"));
            Assert.That(source, Does.Not.Contain("LayoutElement"));
        }

        private static void AssertStartButtonGroupContract(StartUI ui, RectTransform panel)
        {
            Assert.NotNull(ui.ButtonGroup);
            Assert.That(ui.ButtonGroup.parent, Is.EqualTo(panel));
            Assert.That(ui.ButtonGroup.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(ui.ButtonGroup.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(ui.ButtonGroup.pivot, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(ui.ButtonGroup.anchoredPosition, Is.EqualTo(new Vector2(0f, -270f)));
            Assert.That(ui.ButtonGroup.sizeDelta, Is.EqualTo(new Vector2(430f, 316f)));

            var layout = ui.ButtonGroup.GetComponent<VerticalLayoutGroup>();
            Assert.NotNull(layout);
            Assert.That(layout.childAlignment, Is.EqualTo(TextAnchor.UpperLeft));
            Assert.That(layout.spacing, Is.EqualTo(20f));
            Assert.That(layout.padding.left, Is.EqualTo(0));
            Assert.That(layout.padding.right, Is.EqualTo(0));
            Assert.That(layout.padding.top, Is.EqualTo(0));
            Assert.That(layout.padding.bottom, Is.EqualTo(0));
            Assert.IsFalse(layout.childControlWidth);
            Assert.IsFalse(layout.childControlHeight);
            Assert.IsFalse(layout.childForceExpandWidth);
            Assert.IsFalse(layout.childForceExpandHeight);

            var buttons = ui.ButtonGroup.GetComponentsInChildren<Button>(true)
                .Where(button => button.transform.parent == ui.ButtonGroup)
                .ToArray();
            var names = new[] { "BtnNewGame", "BtnContinue", "BtnOnline", "BtnSetting" };
            Assert.That(buttons.Length, Is.EqualTo(names.Length));
            for (var i = 0; i < buttons.Length; i++)
            {
                var rect = buttons[i].GetComponent<RectTransform>();
                Assert.That(buttons[i].name, Is.EqualTo(names[i]));
                Assert.That(buttons[i].transform.parent, Is.EqualTo(ui.ButtonGroup));
                Assert.That(buttons[i].transform.GetSiblingIndex(), Is.GreaterThanOrEqualTo(i));
                Assert.That(rect.sizeDelta, Is.EqualTo(new Vector2(430f, 64f)));
            }
        }

        private static void AssertVisibleButtonLayout(StartUI ui, IReadOnlyList<string> expectedNames)
        {
            Assert.NotNull(ui.ButtonGroup);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(ui.ButtonGroup);
            Canvas.ForceUpdateCanvases();

            var panel = (RectTransform)ui.ButtonGroup.parent;
            var visible = StartButtons(ui)
                .Where(button => button.gameObject.activeSelf)
                .ToArray();
            Assert.That(visible.Select(button => button.name), Is.EqualTo(expectedNames));

            var centers = visible
                .Select(button => CenterYRelativeToPanel(panel, button.GetComponent<RectTransform>()))
                .ToArray();
            Assert.That(centers[0], Is.EqualTo(70f).Within(0.01f));
            for (var i = 1; i < centers.Length; i++)
            {
                Assert.That(centers[i - 1] - centers[i], Is.EqualTo(52f).Within(0.01f));
            }
        }

        private static IEnumerable<Button> StartButtons(StartUI ui)
        {
            yield return ui.BtnNewGame;
            yield return ui.BtnContinue;
            yield return ui.BtnCheckUpdate;
            yield return ui.BtnSetting;
            yield return ui.BtnExit;
        }

        private static float CenterYRelativeToPanel(RectTransform panel, RectTransform child)
        {
            var worldCenter = child.TransformPoint(child.rect.center);
            return panel.InverseTransformPoint(worldCenter).y;
        }

        private static T GetBaseUI<T>(BaseUICtrl ctrl) where T : BaseUI
        {
            return (T)typeof(BaseUICtrl)
                .GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(ctrl);
        }

        private static IEnumerator WaitUntilUi<T>() where T : BaseUICtrl
        {
            for (var i = 0; i < 60 && UIHelper.GetUICtrl<T>() == null; i++)
            {
                yield return null;
            }

            Assert.NotNull(UIHelper.GetUICtrl<T>());
        }

        private GameObject CreatePrefab(string path)
        {
            switch (path)
            {
                case "Prefabs/UI/Home/Start":
                    return CreateStartPrefab();
                case "Prefabs/UI/Common/Loading":
                    return CreateLoadingPrefab();
                case "Prefabs/UI/Common/ResourceUpdateMessage":
                    return Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Common/ResourceUpdateMessage.prefab"));
                case "Prefabs/UI/Notify/Message":
                    return CreateMessagePrefab();
                case "Prefabs/UI/Notify/Float":
                    return CreateFloatPrefab();
                case "Prefabs/UI/Player/PlayerConditionHud":
                    hudPrefabLoadCount++;
                    return CreatePlayerConditionHudPrefab();
                case "Prefabs/UI/Player/GameplayGuideHud":
                    return CreateGameplayGuideHudPrefab();
                case "Prefabs/UI/Expedition/ExpeditionHud":
                    return CreateExpeditionHudPrefab();
                default:
                    return null;
            }
        }

        private static GameObject CreateStartPrefab()
        {
            var root = CreateRoot("Start", out var baseUi);
            var ui = root.AddComponent<StartUI>();
            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(root.transform, false);
            ((RectTransform)panel.transform).sizeDelta = new Vector2(560f, 620f);
            var group = new GameObject("ButtonGroup", typeof(RectTransform), typeof(VerticalLayoutGroup));
            group.transform.SetParent(panel.transform, false);
            ui.ButtonGroup = (RectTransform)group.transform;
            ui.ButtonGroup.anchorMin = new Vector2(0.5f, 0.5f);
            ui.ButtonGroup.anchorMax = new Vector2(0.5f, 0.5f);
            ui.ButtonGroup.pivot = new Vector2(0.5f, 1f);
            ui.ButtonGroup.anchoredPosition = new Vector2(0f, 92f);
            ui.ButtonGroup.sizeDelta = new Vector2(340f, 252f);
            var layout = group.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 8f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            ui.VersionText = (TextMeshProUGUI)CreateText(root.transform, "Version");
            ui.UpdateStatusText = (TextMeshProUGUI)CreateText(root.transform, "UpdateStatus");
            ui.BtnNewGame = CreateStartButton(ui.ButtonGroup, "BtnNewGame", out _);
            ui.BtnContinue = CreateStartButton(ui.ButtonGroup, "BtnContinue", out _);
            ui.BtnCheckUpdate = CreateStartButton(ui.ButtonGroup, "BtnCheckUpdate", out _);
            ui.BtnSetting = CreateStartButton(ui.ButtonGroup, "BtnSetting", out _);
            ui.BtnExit = CreateStartButton(ui.ButtonGroup, "BtnExit", out _);
            ui.UpdateRedPoint = new GameObject("UpdateRedPoint").AddComponent<RedPointView>();
            ui.UpdateRedPoint.NodeId = RedPointIds.StartResourceUpdate;
            ui.UpdateRedPoint.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(baseUi);
            return root;
        }

        private static GameObject CreateMessagePrefab()
        {
            var root = CreateRoot("Message", out var baseUi);
            var ui = root.AddComponent<MessageUI>();
            ui.TitleText = CreateText(root.transform, "Title");
            ui.ContentText = CreateText(root.transform, "Content");
            ui.InputField = new GameObject("Input", typeof(RectTransform)).AddComponent<TMP_InputField>();
            ui.InputField.transform.SetParent(root.transform, false);
            ui.InputErrorText = CreateText(root.transform, "InputError");
            var template = CreateButton<MessageButtonBoxUI>(root.transform, "Template", out var buttonText);
            template.BtnButton = template.GetComponent<Button>();
            template.ButtonText = (TextMeshProUGUI)buttonText;
            template.gameObject.SetActive(false);
            ui.ButtonRoot = CreateList(root.transform, "Buttons", template.gameObject);
            Object.DestroyImmediate(baseUi);
            return root;
        }

        private static GameObject CreateLoadingPrefab()
        {
            var root = CreateRoot("Loading", out var baseUi);
            var ui = root.AddComponent<LoadingUI>();
            ui.BaseArtwork = CreateRect(root.transform, "BaseArtwork").gameObject;
            ui.MapArtwork = CreateRect(root.transform, "MapArtwork").gameObject;
            ui.BaseTitle = CreateRect(root.transform, "BaseTitle").gameObject;
            ui.MapTitle = CreateRect(root.transform, "MapTitle").gameObject;
            ui.MessageText = (TextMeshProUGUI)CreateText(root.transform, "Message");
            ui.ProgressSlider = new GameObject("Progress", typeof(RectTransform), typeof(Slider)).GetComponent<Slider>();
            ui.ProgressSlider.transform.SetParent(root.transform, false);
            ui.PercentText = (TextMeshProUGUI)CreateText(root.transform, "Percent");
            ui.ErrorRoot = new GameObject("Error", typeof(RectTransform));
            ui.ErrorRoot.transform.SetParent(root.transform, false);
            ui.ErrorText = (TextMeshProUGUI)CreateText(ui.ErrorRoot.transform, "ErrorText");
            ui.BtnBack = CreateButton(ui.ErrorRoot.transform, "Back", out _);
            Object.DestroyImmediate(baseUi);
            return root;
        }

        private static GameObject CreateFloatPrefab()
        {
            var root = CreateRoot("Float", out var baseUi);
            var ui = root.AddComponent<FloatUI>();
            ui.TipsRoot = CreateRect(root.transform, "Tips");
            var tip = new GameObject("TipTemplate", typeof(RectTransform)).AddComponent<FloatTextBoxUI>();
            tip.transform.SetParent(ui.TipsRoot, false);
            tip.TextTips = (TextMeshProUGUI)CreateText(tip.transform, "Text");
            tip.gameObject.SetActive(false);
            ui.TipTemplate = tip;
            Object.DestroyImmediate(baseUi);
            return root;
        }

        private static GameObject CreatePlayerConditionHudPrefab()
        {
            // 使用正式 Prefab，避免流程测试的简化桩漏掉手机状态区引用。
            return Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Player/PlayerConditionHud.prefab"));
        }

        private static GameObject CreateGameplayGuideHudPrefab()
        {
            return Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Player/GameplayGuideHud.prefab"));
        }

        private static GameObject CreateExpeditionHudPrefab()
        {
            return Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Expedition/ExpeditionHud.prefab"));
        }

        private static void CreateHudRing(
            Transform parent,
            string prefix,
            out GameObject root,
            out Image fill,
            out TextMeshProUGUI icon,
            out TextMeshProUGUI value)
        {
            root = new GameObject(prefix + "Root", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            fill = new GameObject(prefix + "Fill", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            fill.transform.SetParent(root.transform, false);
            icon = (TextMeshProUGUI)CreateText(root.transform, prefix + "IconText");
            value = (TextMeshProUGUI)CreateText(root.transform, prefix + "ValueText");
        }

        private static ExpeditionSaveData CreatePendingPlaying(SaveSystem save, out string error)
        {
            var id = Guid.NewGuid().ToString("N");
            var definition = new ExpeditionTaskDefinition("expedition.survival-sample.survive", MapIds.SurvivalSample, "Survive", "task.expedition.survival_sample.survive.name", TaskObjectiveType.Survive, 1440f, 100);
            if (!Game.Interface.GetSystem<ExpeditionSystem>().TryBeginConfiguredExpedition(MapIds.SurvivalSample, id, new[] { definition }, out error)) return null;
            var data = Game.Interface.GetModel<ExpeditionModel>().CaptureSave(id, DateTime.UtcNow);
            return save.CaptureExpeditionSections(data, out error) ? data : null;
        }

        private static JObject CreateStatusMismatch(
            JObject source,
            ExpeditionStatus topLevelStatus,
            bool completeTask,
            bool emptyTasks)
        {
            var raw = (JObject)source.DeepClone();
            raw["Status"] = (int)topLevelStatus;
            var taskSection = (JObject)raw["Sections"]?[ExpeditionTaskModel.SaveSectionId];
            var tasks = (JArray)taskSection?["tasks"];
            if (emptyTasks)
            {
                tasks?.Clear();
            }
            else if (completeTask && tasks?[0] is JObject task)
            {
                task["currentAmount"] = task["targetAmount"]?.Value<float>() ?? 1440f;
                task["status"] = ExpeditionTaskStatus.Completed.ToString();
            }

            return raw;
        }

        private static GameObject CreateRoot(string name, out BaseUI ui)
        {
            var root = new GameObject(name, typeof(RectTransform));
            ui = root.AddComponent<BaseUI>();
            return root;
        }

        private static Button CreateButton(Transform parent, string name, out TMP_Text text)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            text = CreateText(go.transform, "Text");
            return go.GetComponent<Button>();
        }

        private static Button CreateStartButton(Transform parent, string name, out TMP_Text text)
        {
            var button = CreateButton(parent, name, out text);
            button.GetComponent<RectTransform>().sizeDelta = new Vector2(340f, 44f);
            return button;
        }

        private static T CreateButton<T>(Transform parent, string name, out TMP_Text text) where T : Component
        {
            var button = CreateButton(parent, name, out text);
            return button.gameObject.AddComponent<T>();
        }

        private static TMP_Text CreateText(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<TextMeshProUGUI>();
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static CustomListView CreateList(Transform parent, string name, GameObject itemPrefab)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var list = go.AddComponent<CustomListView>();
            list.itemPrefab = itemPrefab;
            return list;
        }

        private static LoopVerticalScrollRect CreateLoopList(
            Transform parent,
            string name,
            GameObject itemPrefab,
            float width,
            float height,
            int poolSize)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            ((RectTransform)root.transform).sizeDelta = new Vector2(width, height);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup));
            content.transform.SetParent(viewport.transform, false);

            var list = root.AddComponent<LoopVerticalScrollRect>();
            list.viewport = viewportRect;
            list.content = (RectTransform)content.transform;
            list.horizontal = false;
            list.vertical = true;
            list.prefabSource.prefab = itemPrefab;
            list.prefabSource.poolSize = poolSize;
            return list;
        }

        private static void CreateTestUIRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.AddComponent<UIRoot>();
            CreateRect(root.transform, "HUD");
            CreateRect(root.transform, "Dialog");
            CreateRect(root.transform, "Popup");
            CreateRect(root.transform, "Tips");
            CreateRect(root.transform, "Top");
            var eventSystem = new GameObject("EventSystem", typeof(RectTransform));
            eventSystem.transform.SetParent(root.transform, false);
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputModuleType != null)
            {
                eventSystem.AddComponent(inputModuleType);
            }

            new GameObject("Main Camera", typeof(Camera)).tag = "MainCamera";
            new GameObject(SceneRoot.PoolRootName).transform.SetParent(null);
        }

        private static void DestroySceneObject(string name)
        {
            var go = GameObject.Find(name);
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private static void ResetSilentCheckStarted()
        {
            typeof(StartUICtrl)
                .GetField("silentCheckStarted", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, false);
        }

        private static void InitializeBuiltinFallback()
        {
            Game.Interface.GetSystem<ResourceUpdateSystem>()
                .InitializeStartupInfo(new BootResourceStartupInfo(
                    BootResourceSource.BuiltinFallback,
                    "http://127.0.0.1:8000",
                    "builtin-1",
                    string.Empty));
        }

        private static void InitializeHostLatest()
        {
            Game.Interface.GetSystem<ResourceUpdateSystem>()
                .InitializeStartupInfo(new BootResourceStartupInfo(
                    BootResourceSource.HostLatest,
                    "http://127.0.0.1:8000",
                    "1",
                    "2"));
        }

        private static IDisposable CaptureErrors(List<string> errors)
        {
            return Log.CaptureForTests((type, message) =>
            {
                if (type == LogType.Error)
                {
                    errors.Add(message);
                }
            });
        }

        private static void AssertHasError(List<string> errors, string pattern)
        {
            Assert.IsTrue(errors.Exists(message => Regex.IsMatch(message, pattern)), $"Expected log matching: {pattern}");
        }

        private sealed class FakeChecker : IResourceUpdateRuntimeChecker
        {
            public ResourceUpdateCheckResult Next { get; set; } =
                ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.NoUpdate, "1", "1", 0, 0, null));

            public bool CanCheck => true;

            public UniTask<ResourceUpdateCheckResult> CheckAsync(int timeoutSeconds)
            {
                return UniTask.FromResult(Next);
            }
        }

        private sealed class FakeRemoteVersionProbe : IRemoteResourceVersionProbe
        {
            public RemoteResourceVersionProbeResult Next { get; set; } =
                RemoteResourceVersionProbeResult.Ok("builtin-1");

            public int CallCount { get; private set; }

            public UniTask<RemoteResourceVersionProbeResult> ProbeAsync(
                string resourceServer,
                int timeoutSeconds)
            {
                CallCount++;
                return UniTask.FromResult(Next);
            }
        }

        private sealed class FakeNoticeClient : IResourceUpdateNoticeClient
        {
            public ResourceUpdateNoticeResult Next { get; set; } = ResourceUpdateNoticeResult.NoNotice;
            public int CallCount { get; private set; }

            public UniTask<ResourceUpdateNoticeResult> FetchAsync(ResourceUpdateNoticeRequest request)
            {
                CallCount++;
                return UniTask.FromResult(Next);
            }
        }

        private sealed class FakeMapLoader : IMapContentLoader
        {
            public string LoadedMapId { get; private set; }
            public string NextLoadError { get; set; }

            public UniTask<MapContentLoadResult> LoadAsync(string mapId, string address, Action<float> onProgress, CancellationToken cancellationToken = default)
            {
                LoadedMapId = mapId;
                if (!string.IsNullOrWhiteSpace(NextLoadError))
                {
                    var error = NextLoadError;
                    NextLoadError = null;
                    return UniTask.FromResult(MapContentLoadResult.Fail(error));
                }

                return UniTask.FromResult(MapContentLoadResult.Ok(new FakeMapHandle(mapId)));
            }

            public UniTask<MapContentUnloadResult> UnloadAsync(IMapContentHandle handle, CancellationToken cancellationToken = default)
            {
                if (handle is FakeMapHandle fake)
                {
                    fake.Destroy();
                }

                return UniTask.FromResult(MapContentUnloadResult.Ok());
            }
        }

        private static string GetStartButtonLocalizationKey(Button button)
        {
            return button.GetComponentInChildren<LocalizedText>(true)?.Key;
        }

        private sealed class FakeMapHandle : IMapContentHandle
        {
            private readonly GameObject root;

            public FakeMapHandle(string mapId)
            {
                MapId = mapId;
                root = new GameObject($"FakeMap_{mapId}");
                var presentation = new GameObject("Presentation");
                presentation.transform.SetParent(root.transform, false);
                var runtime = new GameObject("RuntimeUnits");
                runtime.transform.SetParent(presentation.transform, false);
                var context = root.AddComponent<MapContext>();
                typeof(MapContext)
                    .GetField("mapContentRoot", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(context, presentation);
                typeof(MapContext)
                    .GetField("dynamicObjectRoot", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(context, runtime.transform);
                Context = context;
            }

            public string MapId { get; }
            public MapContext Context { get; }
            public bool IsValid => root != null && Context != null;

            public void Destroy()
            {
                if (root != null)
                {
                    Object.DestroyImmediate(root);
                }
            }
        }

        private sealed class FakeRestart : IApplicationRestartUtility
        {
            public bool Result { get; set; } = true;
            public string Error { get; set; }
            public bool Called { get; private set; }

            public bool TryRestart(out string error)
            {
                Called = true;
                error = Error;
                return Result;
            }
        }

        private sealed class FakeClock : ISaveClock
        {
            private DateTime utcNow;
            public FakeClock(DateTime utcNow) { this.utcNow = utcNow; }
            public DateTime UtcNow => utcNow;
            public float RealtimeSinceStartup { get; private set; }

            public void Advance(float seconds)
            {
                RealtimeSinceStartup += seconds;
                utcNow = utcNow.AddSeconds(seconds);
            }
        }

        private sealed class StartRuntimeConfigProvider : IRuntimeConfigProvider
        {
            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = new MapAssetDefinition(id, $"Scenes/{id}");
                error = null;
                return true;
            }

            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = new[]
                {
                    new MapSpawnDefinition(
                        "spawn.player",
                        mapId,
                        MapSpawnObjectType.Player,
                        "player.fake",
                        0f,
                        0f,
                        0f,
                        0f)
                };
                error = null;
                return true;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                value = new PlayerConfigDefinition(id, "player.fake", 100, 10, 1f, 1f, 1f, 1f, 1f);
                error = null;
                return true;
            }

            public bool TryGetProjectile(string id, out ProjectileConfigDefinition value, out string error)
            {
                value = new ProjectileConfigDefinition(id, "projectile.fake", 10f, 10f);
                error = null;
                return true;
            }

            public bool TryGetExpeditionTasks(string mapId, out IReadOnlyList<ExpeditionTaskDefinition> values, out string error)
            {
                values = mapId == MapIds.SurvivalSample
                    ? new[] { new ExpeditionTaskDefinition("expedition.survival-sample.survive", mapId, "Survive", "task.expedition.survival_sample.survive.name", TaskObjectiveType.Survive, 1440f, 100) }
                    : Array.Empty<ExpeditionTaskDefinition>();
                error = string.Empty; return true;
            }

            public bool TryGetCraftRecipes(out IReadOnlyList<CraftRecipeDefinition> values, out string error)
            {
                values = System.Array.Empty<CraftRecipeDefinition>();
                error = null;
                return true;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                value = default;
                error = "not used";
                return false;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                value = default;
                error = "not used";
                return false;
            }

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
            {
                value = default;
                error = "not used";
                return false;
            }
        }

        private sealed class LocalizationLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object
            {
                if (path == "projectile.fake" && typeof(T) == typeof(GameObject))
                {
                    var prefab = new GameObject("ProjectileFake", typeof(BoxCollider), typeof(Rigidbody), typeof(ProjectileView));
                    prefab.GetComponent<BoxCollider>().isTrigger = true;
                    prefab.GetComponent<Rigidbody>().isKinematic = true;
                    return prefab as T;
                }

                if (path == "player.fake" && typeof(T) == typeof(GameObject))
                {
                    return CreatePlayerPrefab() as T;
                }

                switch (path)
                {
                    case LocalizationLanguages.LanguagesAddress:
                        return new TextAsset("{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"\u7b80\u4f53\u4e2d\u6587\"}]}") as T;
                    case "zh-CN.json":
                        return AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/Localization/zh-CN.json") as T;
                    default:
                        return null;
                }
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
            {
                var asset = LoadAsset<T>(path);
                onLoaded?.Invoke(asset);
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }

            private static GameObject CreatePlayerPrefab()
            {
                var prefab = new GameObject("PlayerFake", typeof(CharacterController), typeof(PlayerView));
                var view = prefab.GetComponent<PlayerView>();
                var tiliCanvas = new GameObject("TiliCanvas", typeof(RectTransform));
                tiliCanvas.transform.SetParent(prefab.transform, false);
                var tiliFill = new GameObject("TiliFill", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                tiliFill.transform.SetParent(tiliCanvas.transform, false);
                var meleeAttachRoot = new GameObject("MeleeWeaponAttachRoot").transform;
                meleeAttachRoot.SetParent(prefab.transform, false);
                var rangedAttachRoot = new GameObject("RangedWeaponAttachRoot").transform;
                rangedAttachRoot.SetParent(prefab.transform, false);
                typeof(PlayerView).GetField("characterController", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(view, prefab.GetComponent<CharacterController>());
                typeof(PlayerView).GetField("cameraProfile", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(view, ScriptableObject.CreateInstance<TopDownCameraProfile>());
                typeof(PlayerView).GetField("tiliRingCanvas", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(view, tiliCanvas);
                typeof(PlayerView).GetField("tiliRingFill", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(view, tiliFill);
                typeof(PlayerView).GetField("meleeWeaponAttachRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(view, meleeAttachRoot);
                typeof(PlayerView).GetField("rangedWeaponAttachRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(view, rangedAttachRoot);
                return prefab;
            }
        }
    }
}
