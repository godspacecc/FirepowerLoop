using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ET;
using Game.Network;
using Luban;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class TestCmdUiTests
    {
        private string tempRoot;
        private GameObject floatPrefab;
        private CountingSettingsFileStore settingsStore;
        private FakeSettingsClock settingsClock;
        private ControllableNetwork network;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            UIHelper.ResetForTests();
            ResourceLoader.SetLoaderForTests(new LocalizationTestLoader());
            LocalizationLanguages.ResetForTests();
            settingsStore = new CountingSettingsFileStore();
            settingsClock = new FakeSettingsClock();
            network = new ControllableNetwork();
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(new TestConfigProvider());
                architecture.RegisterUtility<ISettingsFileStore>(settingsStore);
                architecture.RegisterUtility<INetworkRuntime>(network);
            };
            CreateUIRoot();
            floatPrefab = CreateFloatPrefab();
            UIHelper.SetPrefabLoaderForTests(path =>
                path == "Prefabs/UI/Notify/Float"
                    ? floatPrefab
                    : AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab"));
            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayTestCmdUiTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            Game.Interface.GetSystem<SettingsSystem>().SetClockForTests(settingsClock);
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot);
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
            EnterBase();
        }

        [UnityTest]
        public IEnumerator ServerCatalogMergesWithFullTemplateAndReopenStartsWithoutStaleEntries()
        {
            network.State = NetworkConnectionState.Connected;
            PublishAuthenticated();
            TestCmdUICtrl ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            TestCmdUI ui = GetBaseUI<TestCmdUI>(ctrl);
            Assert.That(GetPrivateEnumerableCount(ctrl, "categories"), Is.EqualTo(5), "local catalog and Recent render first");
            PendingNetworkCall firstCatalog = network.SinglePending<C2G_GetServerTestCmdCatalog>();
            Assert.That(firstCatalog.Complete(ValidServerCatalog()), Is.True);
            yield return null;

            Assert.That(GetPrivateEnumerableCount(ctrl, "categories"), Is.EqualTo(6));
            TestCmdCategoryBoxUI serverCategory = CreateRefreshedCategory(ctrl, 5);
            Assert.That(serverCategory.TextLabel.text, Is.EqualTo("服务器"));
            serverCategory.BtnSelect.onClick.Invoke();
            TestCmdEntryBoxUI ping = CreateRefreshedEntry(ctrl, 0);
            ping.BtnSelect.onClick.Invoke();
            Assert.That(ui.InputCommand.text, Is.EqualTo("$ping"));

            UIHelper.HideUI<TestCmdUICtrl>();
            ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            Assert.That(GetPrivateEnumerableCount(ctrl, "categories"), Is.EqualTo(5),
                "new display generation must not render the previous server snapshot");
            PendingNetworkCall secondCatalog = network.Pending<C2G_GetServerTestCmdCatalog>().Last();
            Assert.That(secondCatalog, Is.Not.SameAs(firstCatalog));
            Assert.That(secondCatalog.Complete(ValidServerCatalog()), Is.True);
            yield return null;
            Assert.That(GetPrivateEnumerableCount(ctrl, "categories"), Is.EqualTo(6));
        }

        [UnityTest]
        public IEnumerator CatalogFailureKeepsLocalCatalogAndHiddenLateResponseIsIgnored()
        {
            network.State = NetworkConnectionState.Connected;
            PublishAuthenticated();
            TestCmdUICtrl ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            PendingNetworkCall failed = network.SinglePending<C2G_GetServerTestCmdCatalog>();
            failed.Fail(new InvalidOperationException("catalog failed"));
            yield return null;
            Assert.That(GetPrivate<string>(ctrl, "selectedCategoryId"), Is.EqualTo(TestCmdModel.RecentCategoryId));
            Assert.That(GetPrivateEnumerableCount(ctrl, "categories"), Is.EqualTo(6));
            Assert.That(CreateRefreshedCategory(ctrl, 0).TextLabel.text, Is.EqualTo("最近"));

            UIHelper.HideUI<TestCmdUICtrl>();
            ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            PendingNetworkCall current = network.Pending<C2G_GetServerTestCmdCatalog>().Last();
            UIHelper.HideUI<TestCmdUICtrl>();
            Assert.That(current.IsCanceled, Is.True);
            Assert.That(current.Complete(ValidServerCatalog()), Is.False, "hidden request must reject late response");
            ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            Assert.That(GetPrivateEnumerableCount(ctrl, "categories"), Is.EqualTo(5));
            yield return null;
            Assert.That(GetPrivateEnumerableCount(ctrl, "categories"), Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator ButtonAndSubmitAreDeduplicatedAndHideUnloadCancelExecution()
        {
            PublishAuthenticated();
            TestCmdUICtrl ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            TestCmdUI ui = GetBaseUI<TestCmdUI>(ctrl);
            network.State = NetworkConnectionState.Connected;
            PublishAuthenticated();
            ui.InputCommand.text = "$ping";
            ui.BtnExecute.onClick.Invoke();
            ui.BtnExecute.onClick.Invoke();
            ui.InputCommand.onSubmit.Invoke(ui.InputCommand.text);
            Assert.That(network.Pending<C2G_ExecuteServerTestCmd>().Count, Is.EqualTo(1));
            PendingNetworkCall hiddenExecution = network.SinglePending<C2G_ExecuteServerTestCmd>();
            UIHelper.HideUI<TestCmdUICtrl>();
            Assert.That(hiddenExecution.IsCanceled, Is.True);
            Assert.That(hiddenExecution.Complete(PingResponse()), Is.False);

            ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            ui = GetBaseUI<TestCmdUI>(ctrl);
            ui.InputCommand.text = "$ping";
            ui.InputCommand.onSubmit.Invoke(ui.InputCommand.text);
            yield return null;
            PendingNetworkCall unloadExecution = network.Pending<C2G_ExecuteServerTestCmd>().Last();
            InvokePrivate(ctrl, "OnUnload");
            Assert.That(unloadExecution.IsCanceled, Is.True);
            Assert.That(unloadExecution.Complete(PingResponse()), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LogCommandClosesTestWindowAndOpensRuntimeLogWindow()
        {
            var cmdCtrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var cmdUi = GetBaseUI<TestCmdUI>(cmdCtrl);
            cmdUi.InputCommand.text = "#log";
            cmdUi.BtnExecute.onClick.Invoke();
            yield return null;

            Assert.That(cmdCtrl.IsShow(), Is.False);
            var logCtrl = UIHelper.GetUICtrl<RuntimeLogUICtrl>();
            Assert.That(logCtrl, Is.Not.Null);
            Assert.That(logCtrl.IsShow(), Is.True);
        }

        [UnityTest]
        public IEnumerator SoftKeyboardLogSubmitDefersAndUsesSubmittedText()
        {
            var ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var ui = GetBaseUI<TestCmdUI>(ctrl);
            ui.InputCommand.text = "#log";
            ui.InputCommand.onSubmit.Invoke(ui.InputCommand.text);
            Assert.That(ctrl.IsShow(), Is.True, "TMP 提交回调返回前不能隐藏输入框");
            Assert.That(UIHelper.GetUICtrl<RuntimeLogUICtrl>(), Is.Null);

            // 等待期间的新文本、重复提交和按钮点击不得替换原提交或重复执行。
            ui.InputCommand.text = "#clone 1 1";
            ui.InputCommand.onSubmit.Invoke(ui.InputCommand.text);
            ui.BtnExecute.onClick.Invoke();
            yield return null;

            Assert.That(ctrl.IsShow(), Is.False);
            Assert.That(UIHelper.GetUICtrl<RuntimeLogUICtrl>().IsShow(), Is.True);
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands, Is.EqualTo(new[] { "#log" }));
            Assert.That(SnapshotText(Game.Interface.GetModel<PlayerBagModel>().Container), Is.Empty);
        }

        [Test]
        public void MobileExecuteButtonKeepsKeyboardClosedAndExecutesOnce()
        {
            var ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var ui = GetBaseUI<TestCmdUI>(ctrl);
            ActivateTestCmdInputForTests(ui);
            SetPrivate(ctrl, "softKeyboardSupported", new Func<bool>(() => true));
            ui.InputCommand.text = "#clone 1 1";
            ui.BtnExecute.onClick.Invoke();

            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.Slots.Values.Sum(slot => slot.Amount), Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands.Count, Is.EqualTo(1));
            Assert.That(ui.InputCommand.isFocused, Is.False);
            if (EventSystem.current != null)
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.Not.EqualTo(ui.InputCommand.gameObject));
            Assert.That(typeof(TMP_InputField).GetField("m_ShouldActivateNextUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(ui.InputCommand), Is.False, "执行结束不能预约下一帧再次弹出软键盘");

            CreateRefreshedCategory(ctrl, 1).BtnSelect.onClick.Invoke();
            CreateRefreshedEntry(ctrl, 0).BtnSelect.onClick.Invoke();
            Assert.That(ui.InputCommand.text, Is.EqualTo("#clone "));
            Assert.That(ui.InputCommand.isFocused, Is.False);
            Assert.That(typeof(TMP_InputField).GetField("m_ShouldActivateNextUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(ui.InputCommand), Is.False, "选择模板只填充文本，不能主动弹出软键盘");
        }

        [UnityTest]
        public IEnumerator MobileKeyboardConfirmExecutesOnceWithoutReopeningKeyboard()
        {
            var ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var ui = GetBaseUI<TestCmdUI>(ctrl);
            ActivateTestCmdInputForTests(ui);
            SetPrivate(ctrl, "softKeyboardSupported", new Func<bool>(() => true));
            ui.InputCommand.text = "#clone 1 2";
            ui.InputCommand.onSubmit.Invoke(ui.InputCommand.text);
            ui.InputCommand.onSubmit.Invoke(ui.InputCommand.text);
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands, Is.Empty);
            yield return null;

            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.Slots.Values.Sum(slot => slot.Amount), Is.EqualTo(2));
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands.Count, Is.EqualTo(1));
            Assert.That(ui.InputCommand.isFocused, Is.False);
            if (EventSystem.current != null)
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.Not.EqualTo(ui.InputCommand.gameObject));
            Assert.That(typeof(TMP_InputField).GetField("m_ShouldActivateNextUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(ui.InputCommand), Is.False);
        }

        [UnityTest]
        public IEnumerator DeferredKeyboardSubmitIsCanceledByHideReopenAndDestroyedView()
        {
            var ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var ui = GetBaseUI<TestCmdUI>(ctrl);
            ui.InputCommand.text = "#clone 1 1";
            ui.InputCommand.onSubmit.Invoke(ui.InputCommand.text);
            UIHelper.HideUI<TestCmdUICtrl>();
            ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            yield return null;
            Assert.That(SnapshotText(Game.Interface.GetModel<PlayerBagModel>().Container), Is.Empty);
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands, Is.Empty);
            Assert.That(ctrl.IsShow(), Is.True);

            ui = GetBaseUI<TestCmdUI>(ctrl);
            ui.InputCommand.text = "#clone 1 1";
            ui.InputCommand.onSubmit.Invoke(ui.InputCommand.text);
            UnityEngine.Object.DestroyImmediate(ui.gameObject);
            ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            ui = GetBaseUI<TestCmdUI>(ctrl);
            ui.InputCommand.text = "#clone 1 2";
            ui.BtnExecute.onClick.Invoke();
            yield return null;
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands, Is.EqualTo(new[] { "#clone 1 2" }));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.Slots.Values.Sum(slot => slot.Amount), Is.EqualTo(2));
        }

        [TearDown]
        public void TearDown()
        {
            DestroyTemporaryRows();
            UIHelper.ResetForTests();
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            Architecture<Game>.OnRegisterPatch = _ => { };
            TestReset.ResetArchitecture();
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }

            UnityEngine.Object.DestroyImmediate(floatPrefab);
            EditModeSceneIsolation.DestroyNamedObjectsInActiveTestScene(SceneRoot.PoolRootName);
        }

        [Test]
        public void TestCmdPrefabsHaveCompleteSerializedContract()
        {
            var rootPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Diagnostics/TestCmd.prefab");
            var buttonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Diagnostics/TestCmdButton.prefab");
            var categoryPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Diagnostics/Box/TestCmdCategoryBox.prefab");
            var entryPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Diagnostics/Box/TestCmdEntryBox.prefab");
            Assert.NotNull(rootPrefab);
            Assert.NotNull(buttonPrefab);
            Assert.NotNull(categoryPrefab);
            Assert.NotNull(entryPrefab);

            var ui = rootPrefab.GetComponent<TestCmdUI>();
            Assert.NotNull(ui);
            Assert.NotNull(ui.BtnClose);
            Assert.NotNull(ui.BtnExecute);
            Assert.NotNull(ui.InputCommand);
            Assert.NotNull(ui.TextResult);
            Assert.NotNull(ui.ResultScrollRect);
            Assert.NotNull(ui.ResultViewportRect);
            Assert.NotNull(ui.ResultContentRect);
            Assert.NotNull(ui.CategoryList);
            Assert.NotNull(ui.EntryList);
            Assert.NotNull(ui.TextDetails);
            Assert.NotNull(ui.TextUsage);
            Assert.NotNull(ui.TextExample);
            var titleText = FindText(rootPrefab.transform, "TextTitle");
            var categoryTitleText = FindText(rootPrefab.transform, "TextCategoryTitle");
            var entryTitleText = FindText(rootPrefab.transform, "TextEntryTitle");
            var detailsTitleText = FindText(rootPrefab.transform, "TextDetailsTitle");
            var resultTitleText = FindText(rootPrefab.transform, "DetailsPanel/TextResultTitle");
            Assert.That(ui.CategoryList.prefabSource.prefab, Is.SameAs(categoryPrefab));
            Assert.That(ui.EntryList.prefabSource.prefab, Is.SameAs(entryPrefab));
            Assert.NotNull(rootPrefab.GetComponent<ReferenceCollector>());
            var rootRect = GetRect(rootPrefab);
            Assert.That(rootRect.sizeDelta, Is.EqualTo(new Vector2(1120f, 620f)));
            var categoryRect = GetRect(ui.CategoryList.gameObject);
            var entryRect = GetRect(ui.EntryList.gameObject);
            var detailsRect = GetRect(ui.TextDetails.transform.parent.gameObject);
            var categoryTitleRect = GetRect(categoryTitleText.gameObject);
            var entryTitleRect = GetRect(entryTitleText.gameObject);
            var detailsTitleRect = GetRect(detailsTitleText.gameObject);
            var titleRect = GetRect(titleText.gameObject);
            var closeRect = GetRect(ui.BtnClose.gameObject);
            var inputRect = GetRect(ui.InputCommand.gameObject);
            var executeRect = GetRect(ui.BtnExecute.gameObject);
            var resultTitleRect = GetRect(resultTitleText.gameObject);
            var resultScrollRect = GetRect(ui.ResultScrollRect.gameObject);
            Assert.That(categoryRect.sizeDelta, Is.EqualTo(new Vector2(200f, 368f)));
            Assert.That(entryRect.sizeDelta, Is.EqualTo(new Vector2(360f, 368f)));
            Assert.That(detailsRect.sizeDelta, Is.EqualTo(new Vector2(500f, 368f)));
            Assert.That(categoryRect.anchoredPosition, Is.EqualTo(new Vector2(-442f, 6f)));
            Assert.That(entryRect.anchoredPosition, Is.EqualTo(new Vector2(-150f, 6f)));
            Assert.That(detailsRect.anchoredPosition, Is.EqualTo(new Vector2(292f, 6f)));
            Assert.That(LeftEdge(entryRect) - RightEdge(categoryRect), Is.EqualTo(12f).Within(0.01f));
            Assert.That(LeftEdge(detailsRect) - RightEdge(entryRect), Is.EqualTo(12f).Within(0.01f));
            Assert.That(titleRect.anchoredPosition, Is.EqualTo(new Vector2(0f, 270f)));
            Assert.That(titleRect.sizeDelta, Is.EqualTo(new Vector2(420f, 36f)));
            Assert.That(closeRect.anchorMin, Is.EqualTo(Vector2.one));
            Assert.That(closeRect.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(closeRect.pivot, Is.EqualTo(Vector2.one));
            Assert.That(closeRect.anchoredPosition, Is.EqualTo(new Vector2(-16f, -16f)));
            Assert.That(closeRect.sizeDelta, Is.EqualTo(new Vector2(40f, 40f)));
            Assert.That(categoryTitleRect.anchoredPosition, Is.EqualTo(new Vector2(-442f, 204f)));
            Assert.That(entryTitleRect.anchoredPosition, Is.EqualTo(new Vector2(-150f, 204f)));
            Assert.That(detailsTitleRect.anchoredPosition, Is.EqualTo(new Vector2(292f, 204f)));
            Assert.That(inputRect.anchoredPosition, Is.EqualTo(new Vector2(-76f, -222f)));
            Assert.That(inputRect.sizeDelta, Is.EqualTo(new Vector2(880f, 52f)));
            Assert.That(executeRect.anchoredPosition, Is.EqualTo(new Vector2(446f, -222f)));
            Assert.That(executeRect.sizeDelta, Is.EqualTo(new Vector2(140f, 52f)));
            Assert.That(LeftEdge(executeRect) - RightEdge(inputRect), Is.EqualTo(12f).Within(0.01f));
            Assert.That(resultTitleRect.anchoredPosition, Is.EqualTo(new Vector2(0f, -74f)));
            Assert.That(resultTitleRect.sizeDelta, Is.EqualTo(new Vector2(476f, 24f)));
            Assert.That(resultScrollRect.anchoredPosition, Is.EqualTo(new Vector2(0f, -136f)));
            Assert.That(resultScrollRect.sizeDelta, Is.EqualTo(new Vector2(476f, 100f)));
            Assert.That(ui.ResultScrollRect.viewport, Is.SameAs(ui.ResultViewportRect));
            Assert.That(ui.ResultScrollRect.content, Is.SameAs(ui.ResultContentRect));
            Assert.That(ui.ResultViewportRect, Is.SameAs(ui.ResultScrollRect.transform));
            Assert.That(ui.ResultViewportRect.GetComponent<Image>().raycastTarget, Is.True);
            Assert.NotNull(ui.ResultViewportRect.GetComponent<RectMask2D>());
            Assert.That(ui.TextResult.transform.parent, Is.SameAs(ui.ResultContentRect));
            Assert.That(ui.TextResult.fontSize, Is.EqualTo(20f));
            Assert.That(ui.TextResult.enableWordWrapping, Is.True);
            Assert.That(ui.TextResult.overflowMode, Is.EqualTo(TextOverflowModes.Overflow));
            Assert.That(ui.TextResult.alignment, Is.EqualTo(TextAlignmentOptions.TopLeft));
            Assert.That(IntersectsWorld(titleRect, closeRect), Is.False);
            AssertNodeInside(rootRect, titleRect);
            AssertNodeInsideWorld(rootRect, closeRect);
            foreach (var middle in new[] { categoryTitleRect, entryTitleRect, detailsTitleRect, categoryRect, entryRect, detailsRect })
            {
                Assert.That(Intersects(titleRect, middle), Is.False);
                Assert.That(IntersectsWorld(closeRect, middle), Is.False);
                AssertNodeInside(rootRect, middle);
            }

            foreach (var bottom in new[] { inputRect, executeRect })
            {
                foreach (var middle in new[] { categoryTitleRect, entryTitleRect, detailsTitleRect, categoryRect, entryRect, detailsRect })
                {
                    Assert.That(Intersects(bottom, middle), Is.False);
                }

                AssertNodeInside(rootRect, bottom);
            }

            Assert.That(Intersects(inputRect, executeRect), Is.False);
            Assert.That(ui.InputCommand.pointSize, Is.EqualTo(22f));
            Assert.That(ui.InputCommand.lineType, Is.EqualTo(TMP_InputField.LineType.SingleLine));
            AssertInputTextContract(ui.InputCommand.textComponent);
            AssertInputTextContract(ui.InputCommand.placeholder as TMP_Text);
            var executeText = ui.BtnExecute.GetComponentInChildren<TMP_Text>(true);
            Assert.NotNull(executeText);
            Assert.That(GetRect(executeText.gameObject).sizeDelta, Is.EqualTo(new Vector2(120f, 40f)));
            Assert.That(executeText.fontSize, Is.EqualTo(22f));
            Assert.That(executeText.enableWordWrapping, Is.False);
            Assert.That(executeText.enableAutoSizing, Is.False);
            Assert.NotNull(ui.TextDetails.transform.parent.GetComponent<RectMask2D>());
            Assert.That(ui.TextDetails.alignment, Is.EqualTo(TextAlignmentOptions.TopLeft));
            Assert.That(ui.TextDetails.enableWordWrapping, Is.True);
            Assert.That(ui.TextDetails.raycastTarget, Is.False);
            Assert.That(ui.TextUsage.alignment, Is.EqualTo(TextAlignmentOptions.TopLeft));
            Assert.That(ui.TextUsage.enableWordWrapping, Is.True);
            Assert.That(ui.TextUsage.raycastTarget, Is.False);
            Assert.That(ui.TextExample.alignment, Is.EqualTo(TextAlignmentOptions.TopLeft));
            Assert.That(ui.TextExample.enableWordWrapping, Is.True);
            Assert.That(ui.TextExample.raycastTarget, Is.False);

            var category = categoryPrefab.GetComponent<TestCmdCategoryBoxUI>();
            Assert.NotNull(category);
            Assert.NotNull(category.BtnSelect);
            Assert.That(category.BtnSelect.GetComponent<ButtonClickEventCtrl>(), Is.Null);
            Assert.NotNull(category.TextLabel);
            Assert.NotNull(category.SelectedRoot);
            Assert.That(category.SelectedRoot.activeSelf, Is.False);
            Assert.That(GetRect(categoryPrefab).sizeDelta, Is.EqualTo(new Vector2(180f, 38f)));
            Assert.That(GetRect(category.TextLabel.gameObject).sizeDelta.x, Is.LessThanOrEqualTo(160f));
            var categoryLayout = categoryPrefab.GetComponent<LayoutElement>();
            Assert.NotNull(categoryLayout);
            Assert.That(categoryLayout.preferredWidth, Is.EqualTo(180f));
            Assert.That(categoryLayout.preferredHeight, Is.EqualTo(38f));
            Assert.NotNull(categoryPrefab.GetComponent<ReferenceCollector>());

            var entry = entryPrefab.GetComponent<TestCmdEntryBoxUI>();
            Assert.NotNull(entry);
            Assert.NotNull(entry.BtnSelect);
            Assert.That(entry.BtnSelect.GetComponent<ButtonClickEventCtrl>(), Is.Null);
            Assert.NotNull(entry.TextCommand);
            Assert.NotNull(entry.SelectedRoot);
            Assert.That(entry.SelectedRoot.activeSelf, Is.False);
            Assert.That(GetRect(entryPrefab).sizeDelta, Is.EqualTo(new Vector2(340f, 38f)));
            Assert.That(GetRect(entry.TextCommand.gameObject).sizeDelta.x, Is.LessThanOrEqualTo(320f));
            var entryLayout = entryPrefab.GetComponent<LayoutElement>();
            Assert.NotNull(entryLayout);
            Assert.That(entryLayout.preferredWidth, Is.EqualTo(340f));
            Assert.That(entryLayout.preferredHeight, Is.EqualTo(38f));
            Assert.NotNull(entryPrefab.GetComponent<ReferenceCollector>());

            var buttonUi = buttonPrefab.GetComponent<TestCmdButtonUI>();
            Assert.NotNull(buttonUi);
            Assert.NotNull(buttonUi.Button);
            Assert.NotNull(buttonUi.TextTitle);
            var buttonRect = GetRect(buttonPrefab);
            Assert.That(buttonRect.sizeDelta, Is.EqualTo(new Vector2(160f, 48f)));
            Assert.That(buttonRect.anchorMin, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(buttonRect.anchorMax, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(buttonRect.pivot, Is.EqualTo(new Vector2(0.5f, 0.5f)));
        }

        [Test]
        public void TestCmdResultAreaIsPrebuiltAndExecutesThroughTheRealEntryPoint()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Diagnostics/TestCmd.prefab");
            var prefabUi = prefab.GetComponent<TestCmdUI>();
            Assert.That(prefabUi.ResultScrollRect, Is.Not.Null);
            Assert.That(prefabUi.ResultViewportRect, Is.Not.Null);
            Assert.That(prefabUi.ResultContentRect, Is.Not.Null);
            Assert.That(prefabUi.TextResult.transform.parent, Is.SameAs(prefabUi.ResultContentRect));

            var ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var ui = GetBaseUI<TestCmdUI>(ctrl);
            Assert.That(ui.ResultScrollRect.viewport, Is.SameAs(ui.ResultViewportRect));
            Assert.That(ui.ResultScrollRect.content, Is.SameAs(ui.ResultContentRect));

            ui.InputCommand.text = "#clone 金属";
            ui.BtnExecute.onClick.Invoke();

            Assert.That(ui.TextResult.text, Does.Contain("获得金属零件x1。"));
        }

        [UnityTest]
        public IEnumerator CloneSubmitShowsOneLocalizedGainTipForActualAcceptedAmountOnly()
        {
            var ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var ui = GetBaseUI<TestCmdUI>(ctrl);

            ui.InputCommand.text = "#clone 1 2";
            ui.BtnExecute.onClick.Invoke();
            Assert.That(ActiveFloatTipTexts(), Is.EqualTo(new[] { Texts.TipItemGained("金属零件", 2) }));
            Assert.That(ui.TextResult.text, Is.EqualTo("获得金属零件x2。"));
            ClearFloatTips();

            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            for (var i = 0; i < 11; i++)
            {
                Assert.That(bag.TryAdd(ItemIds.Cloth, 5, 5, out _, out var error), Is.True, error);
            }

            ui.InputCommand.text = "#clone 1 10";
            ui.BtnExecute.onClick.Invoke();
            Assert.That(ActiveFloatTipTexts(), Is.EqualTo(new[] { Texts.TipItemGained("金属零件", 3) }));
            Assert.That(ui.TextResult.text, Is.EqualTo("获得金属零件x3，未接收7。"));
            ClearFloatTips();

            ui.InputCommand.text = "#clone item";
            ui.BtnExecute.onClick.Invoke();
            Assert.That(ActiveFloatTipTexts(), Is.Empty);

            ui.InputCommand.text = "#clone 1 1";
            ui.BtnExecute.onClick.Invoke();
            Assert.That(ActiveFloatTipTexts(), Is.Empty);

            ui.InputCommand.text = "#clear bag";
            ui.BtnExecute.onClick.Invoke();
            Assert.That(ActiveFloatTipTexts(), Is.Empty);

            network.State = NetworkConnectionState.Connected;
            PublishAuthenticated();
            ui.InputCommand.text = "$ping";
            ui.BtnExecute.onClick.Invoke();
            Assert.That(network.SinglePending<C2G_ExecuteServerTestCmd>().Complete(PingResponse()), Is.True);
            yield return null;
            Assert.That(ActiveFloatTipTexts(), Is.Empty);
        }

        [Test]
        public void TestCmdRowAdaptersReplaceReuseMappingsAndClearWhenHidden()
        {
            var ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var ui = GetBaseUI<TestCmdUI>(ctrl);
            var category = CreateRefreshedCategory(ctrl, 2);
            InvokePrivate(ctrl, "OnCategoryRefresh", category.transform, 1);
            var categoryAdapter = category.BtnSelect.GetComponent<ButtonClickEventCtrl>();
            Assert.That(categoryAdapter, Is.Not.Null);
            category.BtnSelect.onClick.Invoke();

            Assert.That(GetPrivate<string>(ctrl, "selectedCategoryId"), Is.EqualTo("inventory-item"));
            Assert.That(GetAdapterCallbackCount(categoryAdapter, ctrl, "OnCategoryRowClick"), Is.EqualTo(0),
                "the category refresh clears the stale temporary row binding before rebuilding visible rows");

            var entry = CreateRefreshedEntry(ctrl, 1);
            InvokePrivate(ctrl, "OnEntryRefresh", entry.transform, 0);
            InvokePrivate(ctrl, "OnEntryRefresh", entry.transform, 0);
            var entryAdapter = entry.BtnSelect.GetComponent<ButtonClickEventCtrl>();
            Assert.That(entryAdapter, Is.Not.Null);
            Assert.That(GetAdapterCallbackCount(entryAdapter, ctrl, "OnEntryRowClick"), Is.EqualTo(1));
            entry.BtnSelect.onClick.Invoke();
            Assert.That(ui.InputCommand.text, Is.EqualTo("#clone "));

            var selectedCategoryBeforeHide = GetPrivate<string>(ctrl, "selectedCategoryId");
            var selectedEntryBeforeHide = GetPrivate<string>(ctrl, "selectedEntryId");
            UIHelper.HideUI<TestCmdUICtrl>();
            category.BtnSelect.onClick.Invoke();
            entry.BtnSelect.onClick.Invoke();

            Assert.That(GetPrivate<string>(ctrl, "selectedCategoryId"), Is.EqualTo(selectedCategoryBeforeHide));
            Assert.That(GetPrivate<string>(ctrl, "selectedEntryId"), Is.EqualTo(selectedEntryBeforeHide));
            Assert.That(GetAdapterCallbackCount(categoryAdapter, ctrl, "OnCategoryRowClick"), Is.Zero);
            Assert.That(GetAdapterCallbackCount(entryAdapter, ctrl, "OnEntryRowClick"), Is.Zero);
        }

        [Test]
        public void InvalidIndices_ClearStaleRowMappingsAndRuntimeBindings()
        {
            var ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var ui = GetBaseUI<TestCmdUI>(ctrl);
            CreateRefreshedCategory(ctrl, 1).BtnSelect.onClick.Invoke();

            var categoryRow = CreateRefreshedCategory(ctrl, 2);
            var categoryAdapter = categoryRow.BtnSelect.GetComponent<ButtonClickEventCtrl>();
            Assert.That(IsPrivateDictionaryMapped(ctrl, "categoryRows", categoryRow), Is.True);
            Assert.That(categoryAdapter, Is.Not.Null);
            Assert.That(GetAdapterCallbackCount(categoryAdapter, ctrl, "OnCategoryRowClick"), Is.EqualTo(1));
            var categoryBeforeInvalidRefresh = GetPrivate<string>(ctrl, "selectedCategoryId");

            LogAssert.Expect(LogType.Error, "[TestCmdUICtrl] 分类行索引无效，已解除旧行绑定。");
            InvokePrivate(ctrl, "OnCategoryRefresh", categoryRow.transform, -1);

            Assert.That(IsPrivateDictionaryMapped(ctrl, "categoryRows", categoryRow), Is.False);
            Assert.That(GetAdapterCallbackCount(categoryAdapter, ctrl, "OnCategoryRowClick"), Is.Zero);
            categoryRow.BtnSelect.onClick.Invoke();
            Assert.That(GetPrivate<string>(ctrl, "selectedCategoryId"), Is.EqualTo(categoryBeforeInvalidRefresh));

            var entryRow = CreateRefreshedEntry(ctrl, 1);
            var entryAdapter = entryRow.BtnSelect.GetComponent<ButtonClickEventCtrl>();
            Assert.That(IsPrivateDictionaryMapped(ctrl, "entryRows", entryRow), Is.True);
            Assert.That(entryAdapter, Is.Not.Null);
            Assert.That(GetAdapterCallbackCount(entryAdapter, ctrl, "OnEntryRowClick"), Is.EqualTo(1));
            var inputBeforeInvalidRefresh = ui.InputCommand.text;

            LogAssert.Expect(LogType.Error, "[TestCmdUICtrl] 指令行索引无效，已解除旧行绑定。");
            InvokePrivate(ctrl, "OnEntryRefresh", entryRow.transform, -1);

            Assert.That(IsPrivateDictionaryMapped(ctrl, "entryRows", entryRow), Is.False);
            Assert.That(GetAdapterCallbackCount(entryAdapter, ctrl, "OnEntryRowClick"), Is.Zero);
            entryRow.BtnSelect.onClick.Invoke();
            Assert.That(ui.InputCommand.text, Is.EqualTo(inputBeforeInvalidRefresh));
        }

        [Test]
        public void DestroyedView_ClearsRowMappingsAndReloadsWithoutDuplicateBindings()
        {
            var ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var oldUi = GetBaseUI<TestCmdUI>(ctrl);
            CreateRefreshedCategory(ctrl, 1).BtnSelect.onClick.Invoke();
            var oldCategoryRow = CreateRefreshedCategory(ctrl, 2);
            var oldEntryRow = CreateRefreshedEntry(ctrl, 0);
            Assert.That(oldCategoryRow.BtnSelect.GetComponent<ButtonClickEventCtrl>(), Is.Not.Null);
            Assert.That(oldEntryRow.BtnSelect.GetComponent<ButtonClickEventCtrl>(), Is.Not.Null);
            Assert.That(GetPrivateEnumerableCount(ctrl, "categoryRows"), Is.GreaterThan(0));
            Assert.That(GetPrivateEnumerableCount(ctrl, "entryRows"), Is.GreaterThan(0));

            oldUi.OnTriggerDestroyEvent.Trigger();
            Assert.That(ctrl.IsShow(), Is.False);
            Assert.That(GetPrivateEnumerableCount(ctrl, "categoryRows"), Is.Zero);
            Assert.That(GetPrivateEnumerableCount(ctrl, "entryRows"), Is.Zero);

            UnityEngine.Object.DestroyImmediate(oldUi.gameObject);
            Assert.That(UnityObjectState.IsMissing(oldUi), Is.True);

            var nextCtrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var nextUi = GetBaseUI<TestCmdUI>(nextCtrl);
            Assert.That(ReferenceEquals(oldUi, nextUi), Is.False);
            var currentCategoryRow = CreateRefreshedCategory(nextCtrl, 2);
            InvokePrivate(nextCtrl, "OnCategoryRefresh", currentCategoryRow.transform, 2);
            var currentCategoryAdapter = currentCategoryRow.BtnSelect.GetComponent<ButtonClickEventCtrl>();
            Assert.That(currentCategoryRow.BtnSelect.GetComponents<ButtonClickEventCtrl>(), Has.Length.EqualTo(1));
            Assert.That(GetAdapterCallbackCount(currentCategoryAdapter, nextCtrl, "OnCategoryRowClick"), Is.EqualTo(1));
            currentCategoryRow.BtnSelect.onClick.Invoke();
            Assert.That(GetPrivate<string>(nextCtrl, "selectedCategoryId"), Is.EqualTo("player-condition"));

            var currentEntryRow = CreateRefreshedEntry(nextCtrl, 0);
            InvokePrivate(nextCtrl, "OnEntryRefresh", currentEntryRow.transform, 0);
            var currentEntryAdapter = currentEntryRow.BtnSelect.GetComponent<ButtonClickEventCtrl>();
            Assert.That(currentEntryRow.BtnSelect.GetComponents<ButtonClickEventCtrl>(), Has.Length.EqualTo(1));
            Assert.That(GetAdapterCallbackCount(currentEntryAdapter, nextCtrl, "OnEntryRowClick"), Is.EqualTo(1));
            currentEntryRow.BtnSelect.onClick.Invoke();
            Assert.That(nextUi.InputCommand.text, Is.EqualTo("#advance time "));
        }

        [Test]
        public void RealTestCmdCatalogIsSortedValidAndComplete()
        {
            var provider = new LubanRuntimeConfigProvider(LoadGeneratedTables);
            Assert.That(provider.TryGetTestCmdCatalog(out var categories, out var entries, out var error), Is.True, error);
            Assert.That(categories.Select(item => item.Order), Is.Ordered);
            Assert.That(categories.Select(item => item.Id), Is.Unique);
            Assert.That(entries.Select(item => item.Id), Is.Unique);

            // 验证实际生成目录覆盖全部客户端指令，不锁定中文文案以免正常说明更新导致误报。
            var requiredTemplates = new[]
            {
                "#clone ", "#clear bag", "#advance time ", "#unitbar on", "#unitbar off",
                "#set hp ", "#set power ", "#set tili ", "#set maxhp ", "#set movespeed ",
                "#set water ", "#set maxwater ", "#set temp ", "#log", "#terrain",
                "#terrain instancing off", "#terrain instancing restore", "#netlog on", "#netlog off",
                "#camera topdown", "#camera shoulder", "#camera firstperson",
                "#mobileinput on", "#mobileinput off",
                "#weather rain", "#weather clear", "#weather dry", "#weather demo", "#weather status",
                "#arcade status", "#arcade heal", "#arcade damage ", "#arcade gold ",
                "#arcade bombs ", "#arcade dashes ", "#arcade drop ", "#arcade clear wave", "#arcade clear arena",
                "#arcade drop arcade.drop.first_person",
                "#gallery", "#appearance list", "#appearance ", "#appearance reset"
            };
            Assert.That(entries.Select(item => item.CommandTemplate), Is.SupersetOf(requiredTemplates));
            var categoryIds = new HashSet<string>(categories.Select(item => item.Id));
            foreach (var entry in entries)
            {
                Assert.That(categoryIds.Contains(entry.CategoryId), Is.True, entry.Id);
                Assert.That(string.IsNullOrWhiteSpace(entry.LabelText), Is.False, entry.Id);
                Assert.That(string.IsNullOrWhiteSpace(entry.DescriptionText), Is.False, entry.Id);
                Assert.That(string.IsNullOrWhiteSpace(entry.UsageText), Is.False, entry.Id);
                Assert.That(string.IsNullOrWhiteSpace(entry.ExampleText), Is.False, entry.Id);
            }
            foreach (var group in entries.GroupBy(item => item.CategoryId))
                Assert.That(group.Select(item => item.Order), Is.Ordered, group.Key);
        }
        [Test]
        public void ReservedRecentAndInvalidCatalogRowsAreRejected()
        {
            AssertDuplicateCatalogIdRejected(
                new[]
                {
                    new TestCmdCategoryDefinition("inventory-item", 100, "背包物品"),
                    new TestCmdCategoryDefinition("inventory-item", 200, "玩家状态")
                },
                TestCatalogEntries,
                "inventory-item");
            AssertInvalidCatalog(
                new[] { new TestCmdCategoryDefinition(" ", 100, "背包物品") },
                TestCatalogEntries,
                "TbTestCmdCategory.Id");
            AssertInvalidCatalog(
                new[] { new TestCmdCategoryDefinition(TestCmdModel.RecentCategoryId, 100, "最近") },
                TestCatalogEntries,
                "TbTestCmdCategory[recent].Id");
            AssertInvalidCatalog(
                new[]
                {
                    new TestCmdCategoryDefinition("inventory-item", 100, "背包物品"),
                    new TestCmdCategoryDefinition("player-condition", 100, "玩家状态")
                },
                TestCatalogEntries,
                "TbTestCmdCategory[player-condition].Order");
            AssertInvalidCatalog(
                new[] { new TestCmdCategoryDefinition("inventory-item", 100, string.Empty) },
                TestCatalogEntries,
                "TbTestCmdCategory[inventory-item].LabelText");
            AssertInvalidCatalog(
                TestCatalogCategories,
                new[] { Entry(" ", "inventory-item", 100, "bad ", "向当前玩家背包加入物品。") },
                "TbTestCmdEntry.Id");
            AssertInvalidCatalog(
                TestCatalogCategories,
                new[] { Entry(TestCmdModel.RecentCategoryId, "inventory-item", 100, "bad ", "向当前玩家背包加入物品。") },
                "TbTestCmdEntry[recent].Id");
            AssertDuplicateCatalogIdRejected(
                TestCatalogCategories,
                new[]
                {
                    Entry("inventory.clone", "inventory-item", 100, "#clone ", "向当前玩家背包加入物品。"),
                    Entry("inventory.clone", "inventory-item", 200, "#clear bag", "清空当前玩家背包。")
                },
                "inventory.clone");
            AssertInvalidCatalog(
                TestCatalogCategories,
                new[] { Entry("bad.empty-category", string.Empty, 100, "bad ", "向当前玩家背包加入物品。") },
                "TbTestCmdEntry[bad.empty-category].CategoryId");
            AssertInvalidCatalog(
                TestCatalogCategories,
                new[] { Entry("bad.missing-category", "missing", 100, "bad ", "向当前玩家背包加入物品。") },
                "TbTestCmdEntry[bad.missing-category].CategoryId");
            AssertInvalidCatalog(
                TestCatalogCategories,
                new[]
                {
                    Entry("bad.first-order", "inventory-item", 100, "bad ", "向当前玩家背包加入物品。"),
                    Entry("bad.second-order", "inventory-item", 100, "bad2 ", "清空当前玩家背包。")
                },
                "TbTestCmdEntry[bad.second-order].Order");
            AssertInvalidCatalog(
                TestCatalogCategories,
                new[] { Entry("bad.empty-label", "inventory-item", 100, "bad ", "向当前玩家背包加入物品。", labelText: string.Empty) },
                "TbTestCmdEntry[bad.empty-label].LabelText");
            AssertInvalidCatalog(
                TestCatalogCategories,
                new[] { Entry("bad.empty-template", "inventory-item", 100, string.Empty, "向当前玩家背包加入物品。") },
                "TbTestCmdEntry[bad.empty-template].CommandTemplate");
            AssertInvalidCatalog(
                TestCatalogCategories,
                new[] { Entry("bad.empty-description", "inventory-item", 100, "bad ", string.Empty) },
                "TbTestCmdEntry[bad.empty-description].DescriptionText");
            AssertInvalidCatalog(
                TestCatalogCategories,
                new[] { Entry("bad.empty-usage", "inventory-item", 100, "bad ", "向当前玩家背包加入物品。", usageText: string.Empty) },
                "TbTestCmdEntry[bad.empty-usage].UsageText");
            AssertInvalidCatalog(
                TestCatalogCategories,
                new[] { Entry("bad.empty-example", "inventory-item", 100, "bad ", "向当前玩家背包加入物品。", exampleText: string.Empty) },
                "TbTestCmdEntry[bad.empty-example].ExampleText");
        }

        [Test]
        public void RecentLeadsAndIsDefaultWithoutOverwritingInput()
        {
            var ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var ui = GetBaseUI<TestCmdUI>(ctrl);
            Assert.That(GetPrivate<string>(ctrl, "selectedCategoryId"), Is.EqualTo(TestCmdModel.RecentCategoryId));
            Assert.That(GetPrivate<string>(ctrl, "selectedEntryId"), Is.Null);
            Assert.That(ui.InputCommand.text, Is.Empty);
            Assert.That(CreateRefreshedCategory(ctrl, 0).TextLabel.text, Is.EqualTo("最近"));
            Assert.That(CreateRefreshedCategory(ctrl, 1).TextLabel.text, Is.EqualTo("背包物品"));
            Assert.That(CreateRefreshedCategory(ctrl, 2).TextLabel.text, Is.EqualTo("玩家状态"));
            Assert.That(CreateRefreshedCategory(ctrl, 3).TextLabel.text, Is.EqualTo("玩家属性"));
            Assert.That(CreateRefreshedCategory(ctrl, 4).TextLabel.text, Is.EqualTo("网络诊断"));
            var recent = CreateRefreshedCategory(ctrl, 0);
            Assert.That(recent.SelectedRoot.activeSelf, Is.True);
            Assert.That(GetPrivateEnumerableCount(ctrl, "entries"), Is.EqualTo(0));
            Assert.That(ui.TextDetails.text, Is.EqualTo("暂无最近指令"));
            Assert.That(ui.TextUsage.text, Is.Empty);
            Assert.That(ui.TextExample.text, Is.Empty);

            ui.InputCommand.text = "#clone 1 1";
            ui.BtnExecute.onClick.Invoke();
            UIHelper.HideUI<TestCmdUICtrl>();
            ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            ui = GetBaseUI<TestCmdUI>(ctrl);
            Assert.That(GetPrivate<string>(ctrl, "selectedCategoryId"), Is.EqualTo(TestCmdModel.RecentCategoryId));
            Assert.That(GetPrivate<string>(ctrl, "selectedEntryId"), Is.EqualTo("#clone 1 1"));
            Assert.That(ui.InputCommand.text, Is.EqualTo("#clone 1 1"));
        }

        [UnityTest]
        public IEnumerator AccountEventsStartOneCatalogRequestThenRestoreAndClearServerContent()
        {
            Game.Interface.GetModel<TestToolSettingsModel>().RecordSubmittedCommand("$ping");
            Game.Interface.GetModel<TestToolSettingsModel>().RecordSubmittedCommand("#clone 1 1");

            var ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var ui = GetBaseUI<TestCmdUI>(ctrl);
            Assert.That(network.Pending<C2G_GetServerTestCmdCatalog>(), Is.Empty);
            Assert.That(GetPrivateEnumerableCount(ctrl, "categories"), Is.EqualTo(5));
            Assert.That(CreateRefreshedCategory(ctrl, 0).TextLabel.text, Is.EqualTo("最近"));
            Assert.That(GetPrivateEnumerableCount(ctrl, "entries"), Is.EqualTo(1));
            Assert.That(GetPrivate<string>(ctrl, "selectedEntryId"), Is.EqualTo("#clone 1 1"));
            Assert.That(ui.InputCommand.text, Is.Empty);
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands, Is.EqualTo(new[] { "#clone 1 1", "$ping" }));

            network.State = NetworkConnectionState.Connected;
            PublishAuthenticated();
            PendingNetworkCall firstRequest = network.SinglePending<C2G_GetServerTestCmdCatalog>();
            Assert.That(network.Pending<C2G_GetServerTestCmdCatalog>(), Is.EqualTo(new[] { firstRequest }));
            Assert.That(GetPrivateEnumerableCount(ctrl, "entries"), Is.EqualTo(2));
            Assert.That(CreateRefreshedEntry(ctrl, 0).TextCommand.text, Is.EqualTo("#clone 1 1"));
            var pendingPingHistory = CreateRefreshedEntry(ctrl, 1);
            Assert.That(pendingPingHistory.TextCommand.text, Is.EqualTo("$ping"));
            pendingPingHistory.BtnSelect.onClick.Invoke();
            Assert.That(ui.TextDetails.text, Is.EqualTo("该历史指令没有可用的配置说明。"));
            Assert.That(ui.TextUsage.text, Is.Empty);
            Assert.That(ui.TextExample.text, Is.Empty);
            PublishAuthenticated(OnlineOperationState.LoggingOut);
            Assert.That(firstRequest.IsCanceled, Is.False);
            Assert.That(network.Pending<C2G_GetServerTestCmdCatalog>(), Is.EqualTo(new[] { firstRequest }));

            Assert.That(firstRequest.Complete(ValidServerCatalog()), Is.True);
            yield return null;
            Assert.That(GetPrivate<string>(ctrl, "selectedEntryId"), Is.EqualTo("$ping"));
            Assert.That(ui.TextDetails.text, Is.EqualTo("检查服务器响应。"));
            Assert.That(ui.TextUsage.text, Is.EqualTo("用法：$ping"));
            Assert.That(ui.TextExample.text, Is.EqualTo("示例：$ping"));
            Assert.That(ui.InputCommand.text, Is.EqualTo("$ping"));
            Assert.That(GetPrivateEnumerableCount(ctrl, "categories"), Is.EqualTo(6));
            Assert.That(CreateRefreshedCategory(ctrl, 5).TextLabel.text, Is.EqualTo("服务器"));
            Assert.That(GetPrivateEnumerableCount(ctrl, "entries"), Is.EqualTo(2));
            Assert.That(CreateRefreshedEntry(ctrl, 1).TextCommand.text, Is.EqualTo("$ping"));

            CreateRefreshedCategory(ctrl, 5).BtnSelect.onClick.Invoke();
            Assert.That(GetPrivate<string>(ctrl, "selectedCategoryId"), Is.EqualTo("server.general"));
            Game.Interface.GetModel<OnlineAccountModel>().Publish(OnlineAccountSnapshot.Empty());
            Assert.That(GetPrivate<string>(ctrl, "selectedCategoryId"), Is.EqualTo(TestCmdModel.RecentCategoryId));
            Assert.That(GetPrivateEnumerableCount(ctrl, "categories"), Is.EqualTo(5));
            Assert.That(GetPrivateEnumerableCount(ctrl, "entries"), Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands, Is.EqualTo(new[] { "#clone 1 1", "$ping" }));

            PublishAuthenticated();
            PendingNetworkCall secondRequest = network.SinglePending<C2G_GetServerTestCmdCatalog>();
            Assert.That(secondRequest, Is.Not.SameAs(firstRequest));
            Game.Interface.GetModel<OnlineAccountModel>().Publish(OnlineAccountSnapshot.Empty());
            Assert.That(secondRequest.IsCanceled, Is.True);
            Assert.That(secondRequest.Complete(ValidServerCatalog()), Is.False);
            yield return null;
            Assert.That(GetPrivateEnumerableCount(ctrl, "categories"), Is.EqualTo(5));
            Assert.That(GetPrivateEnumerableCount(ctrl, "entries"), Is.EqualTo(1));
        }

        [Test]
        public void CategorySelectionChoosesFirstConfiguredEntryAndFillsInput()
        {
            var ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var ui = GetBaseUI<TestCmdUI>(ctrl);
            CreateRefreshedCategory(ctrl, 1).BtnSelect.onClick.Invoke();
            Assert.That(GetPrivate<string>(ctrl, "selectedCategoryId"), Is.EqualTo("inventory-item"));
            Assert.That(GetPrivate<string>(ctrl, "selectedEntryId"), Is.EqualTo("inventory.clone"));
            Assert.That(CreateRefreshedEntry(ctrl, 0).TextCommand.text, Is.EqualTo("添加物品"));
            Assert.That(ui.InputCommand.text, Is.EqualTo("#clone "));
            Assert.That(ui.InputCommand.stringPosition, Is.EqualTo(ui.InputCommand.text.Length));
            Assert.That(ui.TextDetails.text, Is.EqualTo("向当前玩家背包加入物品。"));
            Assert.That(ui.TextUsage.text, Is.EqualTo("语法：#clone <ItemId> <Amount>"));
            Assert.That(ui.TextExample.text, Is.EqualTo("示例：#clone 3 5"));

            CreateRefreshedCategory(ctrl, 3).BtnSelect.onClick.Invoke();
            Assert.That(GetPrivate<string>(ctrl, "selectedCategoryId"), Is.EqualTo("player-attribute"));
            Assert.That(GetPrivate<string>(ctrl, "selectedEntryId"), Is.EqualTo("attribute.set-hp"));
            Assert.That(ui.InputCommand.text, Is.EqualTo("#set hp "));
            Assert.That(ui.InputCommand.stringPosition, Is.EqualTo(ui.InputCommand.text.Length));
            Assert.That(ui.TextDetails.text, Is.EqualTo("设置当前生命：set hp <整数>。"));
            Assert.That(ui.TextUsage.text, Is.EqualTo("语法：#set hp <整数>"));
            Assert.That(ui.TextExample.text, Is.EqualTo("示例：#set hp 80"));
        }

        [Test]
        public void HistoryDescriptionUsesLongestConfiguredTemplateWithoutHardcoding()
        {
            var ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var ui = GetBaseUI<TestCmdUI>(ctrl);
            Game.Interface.GetModel<TestToolSettingsModel>().RecordSubmittedCommand("#set hp 7");
            CreateRefreshedCategory(ctrl, 0).BtnSelect.onClick.Invoke();
            SetPrivate(ctrl, "catalogEntries", new[]
            {
                Entry("short.set", "player-attribute", 1, "set ", "设置当前能量：set power <数值>。"),
                Entry("long.set-hp", "player-attribute", 2, "#set hp ", "设置当前生命：set hp <整数>。")
            });
            InvokePrivate(ctrl, "RefreshEntries", true);
            CreateRefreshedEntry(ctrl, 0).BtnSelect.onClick.Invoke();
            Assert.That(ui.TextDetails.text, Is.EqualTo("设置当前生命：set hp <整数>。"));
            Assert.That(ui.TextUsage.text, Is.Not.Empty);
            Assert.That(ui.TextExample.text, Is.Not.Empty);

            Game.Interface.GetModel<TestToolSettingsModel>().RecordSubmittedCommand("#unmatched command");
            InvokePrivate(ctrl, "RefreshEntries", true);
            CreateRefreshedEntry(ctrl, 0).BtnSelect.onClick.Invoke();
            Assert.That(ui.TextDetails.text, Is.EqualTo("该历史指令没有可用的配置说明。"));
            Assert.That(ui.TextUsage.text, Is.Empty);
            Assert.That(ui.TextExample.text, Is.Empty);
        }

        [Test]
        public void ExecutingRefreshesRecentWithoutChangingConfiguredSelection()
        {
            var ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var ui = GetBaseUI<TestCmdUI>(ctrl);
            CreateRefreshedCategory(ctrl, 0).BtnSelect.onClick.Invoke();
            ui.InputCommand.text = "#clone 1 1";
            ui.BtnExecute.onClick.Invoke();
            Assert.That(GetPrivate<string>(ctrl, "selectedCategoryId"), Is.EqualTo(TestCmdModel.RecentCategoryId));
            Assert.That(GetPrivate<string>(ctrl, "selectedEntryId"), Is.EqualTo("#clone 1 1"));
            Assert.That(CreateRefreshedEntry(ctrl, 0).SelectedRoot.activeSelf, Is.True);

            CreateRefreshedCategory(ctrl, 1).BtnSelect.onClick.Invoke();
            CreateRefreshedEntry(ctrl, 1).BtnSelect.onClick.Invoke();
            Assert.That(ui.InputCommand.text, Is.EqualTo("#clear bag"));
            ui.BtnExecute.onClick.Invoke();
            Assert.That(GetPrivate<string>(ctrl, "selectedCategoryId"), Is.EqualTo("inventory-item"));
            Assert.That(GetPrivate<string>(ctrl, "selectedEntryId"), Is.EqualTo("inventory.clear-bag"));
            Assert.That(ui.InputCommand.text, Is.EqualTo("#clear bag"));
            Assert.That(ui.TextDetails.text, Is.EqualTo("清空当前玩家背包。"));
        }

        [Test]
        public void ReusedRowsClearSelectionStateWhenRefreshedUnselected()
        {
            var ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var category = CreateRefreshedCategory(ctrl, 0);
            Assert.That(category.SelectedRoot.activeSelf, Is.True);
            InvokePrivate(ctrl, "OnCategoryRefresh", category.transform, 1);
            Assert.That(category.SelectedRoot.activeSelf, Is.False);

            CreateRefreshedCategory(ctrl, 1).BtnSelect.onClick.Invoke();
            var entry = CreateRefreshedEntry(ctrl, 0);
            Assert.That(entry.SelectedRoot.activeSelf, Is.True);
            InvokePrivate(ctrl, "OnEntryRefresh", entry.transform, 1);
            Assert.That(entry.SelectedRoot.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator CategoryTemplateHistoryAndSubmitRefreshUi()
        {
            var ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var ui = GetBaseUI<TestCmdUI>(ctrl);
            Assert.That(FindText(ui.transform, "TextTitle").text, Is.EqualTo("测试指令"));
            Assert.That(FindText(ui.transform, "TextCategoryTitle").text, Is.EqualTo("分类"));
            Assert.That(FindText(ui.transform, "TextEntryTitle").text, Is.EqualTo("指令"));
            Assert.That(FindText(ui.transform, "TextDetailsTitle").text, Is.EqualTo("指令说明"));
            Assert.That(ui.TextDetails.text, Is.EqualTo("暂无最近指令"));
            Assert.That(ui.TextUsage.text, Is.Empty);
            Assert.That(ui.TextExample.text, Is.Empty);
            Assert.That(ui.TextResult.text, Is.EqualTo("暂无结果。"));
            Assert.That(SnapshotText(Game.Interface.GetModel<PlayerBagModel>().Container), Is.Empty);

            var inventoryCategory = CreateRefreshedCategory(ctrl, 1);
            inventoryCategory.BtnSelect.onClick.Invoke();
            var cloneTemplate = CreateRefreshedEntry(ctrl, 0);
            var clearTemplate = CreateRefreshedEntry(ctrl, 1);
            Assert.That(cloneTemplate.TextCommand.text, Is.EqualTo("添加物品"));
            cloneTemplate.BtnSelect.onClick.Invoke();
            Assert.That(ui.InputCommand.text, Is.EqualTo("#clone "));
            Assert.That(ui.InputCommand.stringPosition, Is.EqualTo(ui.InputCommand.text.Length));
            Assert.That(ui.TextDetails.text, Is.EqualTo("向当前玩家背包加入物品。"));
            Assert.That(ui.TextUsage.text, Is.EqualTo("语法：#clone <ItemId> <Amount>"));
            Assert.That(ui.TextExample.text, Is.EqualTo("示例：#clone 3 5"));
            Assert.That(SnapshotText(Game.Interface.GetModel<PlayerBagModel>().Container), Is.Empty);

            clearTemplate.BtnSelect.onClick.Invoke();
            Assert.That(ui.InputCommand.text, Is.EqualTo("#clear bag"));
            Assert.That(ui.TextDetails.text, Is.EqualTo("清空当前玩家背包。"));
            Assert.That(ui.TextUsage.text, Is.EqualTo("语法：#clear bag"));
            Assert.That(ui.TextExample.text, Is.EqualTo("示例：#clear bag"));
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands, Is.Empty);

            ui.InputCommand.text = "#clone 1 1";
            ui.BtnExecute.onClick.Invoke();

            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands[0], Is.EqualTo("#clone 1 1"));
            Assert.That(ui.TextResult.text, Does.Contain("1"));
            if (EventSystem.current != null)
            {
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(ui.InputCommand.gameObject));
            }

            ui.InputCommand.text = "#clear bag";
            ui.InputCommand.onSubmit.Invoke(ui.InputCommand.text);
            yield return null;
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands[0], Is.EqualTo("#clear bag"));
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands[1], Is.EqualTo("#clone 1 1"));

            var recentCategory = CreateRefreshedCategory(ctrl, 0);
            recentCategory.BtnSelect.onClick.Invoke();
            Assert.That(ui.TextDetails.text, Is.EqualTo("清空当前玩家背包。"));

            var clearHistory = CreateRefreshedEntry(ctrl, 0);
            var cloneHistory = CreateRefreshedEntry(ctrl, 1);
            clearHistory.BtnSelect.onClick.Invoke();
            Assert.That(ui.InputCommand.text, Is.EqualTo("#clear bag"));
            Assert.That(ui.TextDetails.text, Is.EqualTo("清空当前玩家背包。"));
            Assert.That(ui.TextUsage.text, Is.EqualTo("语法：#clear bag"));
            Assert.That(ui.TextExample.text, Is.EqualTo("示例：#clear bag"));

            cloneHistory.BtnSelect.onClick.Invoke();
            Assert.That(ui.InputCommand.text, Is.EqualTo("#clone 1 1"));
            Assert.That(ui.TextDetails.text, Is.EqualTo("向当前玩家背包加入物品。"));
            Assert.That(ui.TextUsage.text, Is.EqualTo("语法：#clone <ItemId> <Amount>"));
            Assert.That(ui.TextExample.text, Is.EqualTo("示例：#clone 3 5"));

            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });
            Assert.That(ui.TextDetails.text, Is.EqualTo("向当前玩家背包加入物品。"));

            inventoryCategory = CreateRefreshedCategory(ctrl, 1);
            inventoryCategory.BtnSelect.onClick.Invoke();
            Assert.That(ui.TextDetails.text, Is.EqualTo("向当前玩家背包加入物品。"));
        }

        [Test]
        public void TestCmdButton_ClickOpensAndDragDoesNotOpen()
        {
            var buttonCtrl = UIHelper.ShowUI<TestCmdButtonUICtrl>();
            var buttonUi = GetBaseUI<TestCmdButtonUI>(buttonCtrl);
            Assert.That(buttonCtrl.Layer(), Is.EqualTo(UILayer.Top));
            Assert.That(buttonCtrl.BlocksGame, Is.False);

            ExecutePointerClick(buttonUi.gameObject, new Vector2(180f, 120f));
            var cmdCtrl = UIHelper.GetUICtrl<TestCmdUICtrl>();
            Assert.That(cmdCtrl.IsShow(), Is.True);
            Assert.That(cmdCtrl.BlocksGame, Is.True);
            UIHelper.HideUI<TestCmdUICtrl>();

            ExecuteDrag(buttonUi.gameObject, new Vector2(180f, 120f), new Vector2(360f, 240f));
            Assert.That(UIHelper.GetUICtrl<TestCmdUICtrl>().IsShow(), Is.False);
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().TestCmdButtonNormalizedPosition.Value.x, Is.GreaterThan(0.09f));

            ExecutePointerClick(buttonUi.gameObject, new Vector2(360f, 240f));
            Assert.That(UIHelper.GetUICtrl<TestCmdUICtrl>().IsShow(), Is.True);
        }

        [Test]
        public void TestCmdButton_DragEndPersistsOnce()
        {
            var ctrl = UIHelper.ShowUI<TestCmdButtonUICtrl>();
            var ui = GetBaseUI<TestCmdButtonUI>(ctrl);
            var model = Game.Interface.GetModel<TestToolSettingsModel>();
            var settings = Game.Interface.GetSystem<SettingsSystem>();
            model.SetTestCmdButtonPosition(new Vector2(0.72f, 0.83f));
            settingsStore.ResetSaves();

            var beforeDragPosition = model.TestCmdButtonNormalizedPosition.Value;
            var eventData = CreatePointerEvent(new Vector2(300f, 200f));
            ExecuteEvents.Execute(ui.gameObject, eventData, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(ui.gameObject, eventData, ExecuteEvents.beginDragHandler);
            eventData.position = new Vector2(320f, 220f);
            ExecuteEvents.Execute(ui.gameObject, eventData, ExecuteEvents.dragHandler);
            eventData.position = new Vector2(340f, 240f);
            ExecuteEvents.Execute(ui.gameObject, eventData, ExecuteEvents.dragHandler);

            Assert.That(model.TestCmdButtonNormalizedPosition.Value, Is.EqualTo(beforeDragPosition));
            Assert.That(settings.Flush(), Is.True);
            settingsClock.Now += 1f;
            LifeCycle.Update();
            Assert.That(settingsStore.SaveCount, Is.EqualTo(0));

            ExecuteEvents.Execute(ui.gameObject, eventData, ExecuteEvents.endDragHandler);
            var afterDragPosition = model.TestCmdButtonNormalizedPosition.Value;
            Assert.That(afterDragPosition, Is.Not.EqualTo(beforeDragPosition));
            Assert.That(settingsStore.SaveCount, Is.EqualTo(0));

            settingsClock.Now += 0.51f;
            LifeCycle.Update();
            Assert.That(settingsStore.SaveCount, Is.EqualTo(1));
            Assert.That(settingsStore.LastSaved.TestTools.TestCmdButtonPositionX, Is.EqualTo(afterDragPosition.x).Within(0.0001f));
            Assert.That(settingsStore.LastSaved.TestTools.TestCmdButtonPositionY, Is.EqualTo(afterDragPosition.y).Within(0.0001f));
        }

        [Test]
        public void TestCmdButton_DefaultClampAndModelResetRefreshesVisiblePosition()
        {
            var ctrl = UIHelper.ShowUI<TestCmdButtonUICtrl>();
            var ui = GetBaseUI<TestCmdButtonUI>(ctrl);
            var rect = ui.transform as RectTransform;

            Assert.That(rect.anchoredPosition.x, Is.EqualTo(-960f + 1920f * 0.09f).Within(0.01f));
            Assert.That(rect.anchoredPosition.y, Is.EqualTo(-540f + 1080f * 0.11f).Within(0.01f));

            Game.Interface.GetModel<TestToolSettingsModel>().SetTestCmdButtonPosition(new Vector2(2f, -1f));
            Assert.That(rect.anchoredPosition.x, Is.EqualTo(960f - 80f).Within(0.01f));
            Assert.That(rect.anchoredPosition.y, Is.EqualTo(-540f + 24f).Within(0.01f));

            Game.Interface.GetSystem<SettingsSystem>().Apply(SettingsData.CreateDefault());
            Assert.That(rect.anchoredPosition.x, Is.EqualTo(-960f + 1920f * 0.09f).Within(0.01f));
            Assert.That(rect.anchoredPosition.y, Is.EqualTo(-540f + 1080f * 0.11f).Within(0.01f));
        }

        [Test]
        public void TestCmdButton_RemainsAboveTopWindowsAndCommandBackground()
        {
            var buttonCtrl = UIHelper.ShowUI<TestCmdButtonUICtrl>();
            var cmdCtrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var topLayer = UIRoot.Instance.GetLayerObj(UILayer.Top);
            var buttonUi = GetBaseUI<TestCmdButtonUI>(buttonCtrl);
            var cmdUi = GetBaseUI<TestCmdUI>(cmdCtrl);

            Assert.That(buttonUi.gameObject.activeSelf, Is.True);
            Assert.That(cmdCtrl.Layer(), Is.EqualTo(UILayer.Top));
            Assert.That(buttonUi.transform.parent, Is.SameAs(topLayer));
            Assert.That(topLayer.GetChild(0).name, Does.Contain("Background"));
            Assert.That(topLayer.GetChild(1), Is.EqualTo(cmdUi.transform));
            Assert.That(topLayer.GetChild(topLayer.childCount - 1), Is.SameAs(buttonUi.transform));

            var message = UIHelper.ShowUI<MessageUICtrl>();
            Assert.That(topLayer.GetChild(topLayer.childCount - 1), Is.SameAs(buttonUi.transform));
            message.Hide();

            ExecutePointerClick(topLayer.GetChild(0).gameObject, new Vector2(180f, 120f));
            Assert.That(cmdCtrl.IsShow(), Is.True);

            UIHelper.HideUI<TestCmdUICtrl>();
            ExecutePointerClick(buttonUi.gameObject, new Vector2(180f, 120f));
            Assert.That(cmdCtrl.IsShow(), Is.True);
        }

        [Test]
        public void LegacyTestCmdHotKey_NoLongerOpensWindow()
        {
            var sanitized = SettingsData.SanitizeHotKeys(new[]
            {
                new HotKeyBinding { ActionId = "test-command.toggle", Context = HotKeyContext.Gameplay, BindingPath = "<Keyboard>/c" }
            });
            Assert.That(sanitized.Any(item => item.ActionId == "test-command.toggle"), Is.False);
            Assert.That(sanitized.Any(item => item.BindingPath == "<Keyboard>/c"), Is.False);

            var hotKeys = Game.Interface.GetSystem<HotKeySystem>();
            Assert.That(hotKeys.ExecuteBinding("<Keyboard>/c"), Is.False);
            Assert.That(UIHelper.GetUICtrl<TestCmdUICtrl>(), Is.Null);

            Game.Interface.GetModel<HotKeyModel>().Apply(new[]
            {
                new HotKeyBinding { ActionId = "test-command.toggle", Context = HotKeyContext.Gameplay, BindingPath = "<Keyboard>/f2" }
            });
            hotKeys.RebuildActions();
            Assert.That(hotKeys.ExecuteBinding("<Keyboard>/f2"), Is.False);
            Assert.That(UIHelper.GetUICtrl<TestCmdUICtrl>(), Is.Null);
        }

        [Test]
        public void OpenTestCmdBlocksGameplayInputWithoutPausingWorld()
        {
            var beforeScale = Time.timeScale;
            var input = Game.Interface.GetSystem<PlayerInputSystem>();
            var ctrl = UIHelper.ShowUI<TestCmdUICtrl>();

            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Playing));
            Assert.That(Time.timeScale, Is.EqualTo(beforeScale));
            Assert.That(UIHelper.HasUIOpen, Is.True);
            Assert.That(input.MoveInput, Is.EqualTo(Vector2.zero));

            var attacks = 0;
            var interactions = 0;
            Game.Interface.RegisterEvent<PlayerAttackRequestedEvent>(_ => attacks++);
            Game.Interface.RegisterEvent<PlayerInteractRequestedEvent>(_ => interactions++);
            LifeCycle.Update();

            Assert.That(attacks, Is.EqualTo(0));
            Assert.That(interactions, Is.EqualTo(0));
            UIHelper.HideUI<TestCmdUICtrl>();
            Assert.That(UIHelper.HasUIOpen, Is.False);
        }

        [Test]
        public void TestCmdUsesReadableLabelsResultsAndIgnoresLanguageSwitch()
        {
            var zhJson = File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Localization/zh-CN.json"));
            var enJson = File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Localization/en-US.json"));
            var report = LocalizationValidator.ValidateJsonTables(new System.Collections.Generic.Dictionary<string, string>
            {
                ["zh-CN"] = zhJson,
                ["en-US"] = enJson
            });
            Assert.That(report.IsValid, Is.True, string.Join("\n", report.Issues.Select(item => item.ToString())));
            Assert.That(zhJson, Does.Not.Contain("\"ui.test_cmd."));
            Assert.That(enJson, Does.Not.Contain("\"ui.test_cmd."));
            Assert.That(File.ReadAllText(Path.Combine(Application.dataPath, "Game/Scripts/Utility/Localization/TextsGenerated.cs")),
                Does.Not.Contain("TestCmd"));
            AssertRequiredChineseFontCoverage();

            var ctrl = UIHelper.ShowUI<TestCmdUICtrl>();
            var ui = GetBaseUI<TestCmdUI>(ctrl);
            Assert.That(FindText(ui.transform, "TextTitle").text, Is.EqualTo("测试指令"));
            Assert.That(ui.BtnExecute.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("执行"));

            ui.InputCommand.text = "#clone 1 1";
            ui.BtnExecute.onClick.Invoke();
            Assert.That(ui.TextResult.text, Does.Contain("获得金属零件x1"));
            CreateRefreshedCategory(ctrl, 1).BtnSelect.onClick.Invoke();
            CreateRefreshedEntry(ctrl, 1).BtnSelect.onClick.Invoke();
            var categoryId = GetPrivate<string>(ctrl, "selectedCategoryId");
            var entryId = GetPrivate<string>(ctrl, "selectedEntryId");
            var inputText = ui.InputCommand.text;
            var history = Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands.ToArray();

            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });
            Assert.That(GetPrivate<string>(ctrl, "selectedCategoryId"), Is.EqualTo(categoryId));
            Assert.That(GetPrivate<string>(ctrl, "selectedEntryId"), Is.EqualTo(entryId));
            Assert.That(ui.InputCommand.text, Is.EqualTo(inputText));
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands, Is.EqualTo(history));
            Assert.That(FindText(ui.transform, "TextTitle").text, Is.EqualTo("测试指令"));
            Assert.That(ui.BtnExecute.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("执行"));
            ui.InputCommand.text = "#clear bag";
            ui.BtnExecute.onClick.Invoke();
            Assert.That(ui.TextResult.text, Does.Contain("已清空玩家背包"));
        }

        private static void EnterBase()
        {
            var app = Game.Interface.GetSystem<AppStateSystem>();
            app.TryTransition(AppState.Loading);
            app.TryTransition(AppState.Playing);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(PlayerBagRiskState.BaseSafe, "test", false, out _);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player", string.Empty, 100, 10, 1f, 1f, 1f, 1f, 5f),
                false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
        }

        private static T GetBaseUI<T>(BaseUICtrl ctrl) where T : BaseUI
        {
            return (T)typeof(BaseUICtrl)
                .GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(ctrl);
        }

        private static string[] ActiveFloatTipTexts()
        {
            var floatCtrl = UIHelper.GetUICtrl<FloatUICtrl>();
            if (floatCtrl == null)
            {
                return Array.Empty<string>();
            }

            return GetBaseUI<FloatUI>(floatCtrl).TipsRoot.transform
                .Cast<Transform>()
                .Where(child => child.gameObject.activeSelf)
                .Select(child => child.GetComponent<FloatTextBoxUI>().TextTips.text)
                .ToArray();
        }

        private static void ClearFloatTips()
        {
            var floatCtrl = UIHelper.GetUICtrl<FloatUICtrl>();
            if (floatCtrl != null)
            {
                floatCtrl.Hide();
            }
        }

        private static GameObject CreateFloatPrefab()
        {
            var root = new GameObject("Float", typeof(RectTransform), typeof(FloatUI));
            var ui = root.GetComponent<FloatUI>();
            var tipsRoot = new GameObject("TipsRoot", typeof(RectTransform));
            tipsRoot.transform.SetParent(root.transform, false);
            ui.TipsRoot = tipsRoot.GetComponent<RectTransform>();
            var tip = new GameObject("TipTemplate", typeof(RectTransform), typeof(FloatTextBoxUI));
            tip.transform.SetParent(ui.TipsRoot, false);
            var text = new GameObject("TextTips", typeof(RectTransform), typeof(TextMeshProUGUI));
            text.transform.SetParent(tip.transform, false);
            tip.SetActive(false);
            ui.TipTemplate = tip.GetComponent<FloatTextBoxUI>();
            ui.TipTemplate.TextTips = text.GetComponent<TextMeshProUGUI>();
            return root;
        }

        private static void ActivateTestCmdInputForTests(TestCmdUI ui)
        {
            // EditMode 无原生软键盘；准备 TMP 已聚焦且完成延迟激活的前置状态，不修改产品 Prefab。
            typeof(TMP_InputField).GetField("m_AllowInput", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(ui.InputCommand, true);
            typeof(TMP_InputField).GetField("m_ShouldActivateNextUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(ui.InputCommand, false);
            Assert.That(ui.InputCommand.isFocused, Is.True);
        }

        private static void CreateUIRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(UIRoot));
            ((RectTransform)root.transform).sizeDelta = new Vector2(1920f, 1080f);
            foreach (var layerName in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var layer = new GameObject(layerName, typeof(RectTransform));
                layer.transform.SetParent(root.transform, false);
                var rect = (RectTransform)layer.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), inputModuleType);
            eventSystem.transform.SetParent(root.transform, false);
            eventSystem.GetComponent<EventSystem>().UpdateModules();
            new GameObject(SceneRoot.PoolRootName);
        }

        private static void ExecutePointerClick(GameObject target, Vector2 position)
        {
            var eventData = CreatePointerEvent(position);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.pointerClickHandler);
        }

        private static void ExecuteDrag(GameObject target, Vector2 start, Vector2 end)
        {
            var eventData = CreatePointerEvent(start);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.beginDragHandler);
            eventData.position = end;
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(target, eventData, ExecuteEvents.endDragHandler);
        }

        private static PointerEventData CreatePointerEvent(Vector2 position)
        {
            return new PointerEventData(EventSystem.current)
            {
                position = position,
                pressPosition = position,
                button = PointerEventData.InputButton.Left
            };
        }

        private static void DestroyTemporaryRows()
        {
            foreach (var row in UnityEngine.Object.FindObjectsOfType<TestCmdEntryBoxUI>(true))
            {
                UnityEngine.Object.DestroyImmediate(row.gameObject);
            }

            foreach (var row in UnityEngine.Object.FindObjectsOfType<TestCmdCategoryBoxUI>(true))
            {
                UnityEngine.Object.DestroyImmediate(row.gameObject);
            }
        }

        private sealed class CountingSettingsFileStore : ISettingsFileStore
        {
            public int SaveCount { get; private set; }
            public SettingsData LastSaved { get; private set; }
            public SettingsData DataToLoad { get; set; } = SettingsData.CreateDefault();

            public SettingsData LoadOrDefault(out SettingsLoadResult result)
            {
                result = SettingsLoadResult.CreatedDefault;
                return DataToLoad.Clone();
            }

            public bool Save(SettingsData data, out string error)
            {
                SaveCount++;
                LastSaved = data.Clone();
                error = null;
                return true;
            }

            public void ResetSaves()
            {
                SaveCount = 0;
                LastSaved = null;
            }
        }

        private sealed class FakeSettingsClock : ISettingsClock
        {
            public float Now { get; set; }
        }

        private static TestCmdEntryBoxUI CreateRefreshedEntry(TestCmdUICtrl ctrl, int index)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Diagnostics/Box/TestCmdEntryBox.prefab");
            Assert.NotNull(prefab);
            var instance = UnityEngine.Object.Instantiate(prefab);
            var row = instance.GetComponent<TestCmdEntryBoxUI>();
            Assert.NotNull(row);
            InvokePrivate(ctrl, "OnEntryRefresh", instance.transform, index);
            return row;
        }

        private static int GetAdapterCallbackCount(ButtonClickEventCtrl adapter, object target, string methodName)
        {
            var callback = typeof(EasyEvent<object[]>)
                .GetField("mOnEvent", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(adapter.OnClickEvent) as Delegate;
            if (callback == null)
            {
                return 0;
            }

            return callback.GetInvocationList().Count(item =>
                ReferenceEquals(item.Target, target) && item.Method.Name == methodName);
        }

        private static TestCmdCategoryBoxUI CreateRefreshedCategory(TestCmdUICtrl ctrl, int index)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Diagnostics/Box/TestCmdCategoryBox.prefab");
            Assert.NotNull(prefab);
            var instance = UnityEngine.Object.Instantiate(prefab);
            var row = instance.GetComponent<TestCmdCategoryBoxUI>();
            Assert.NotNull(row);
            InvokePrivate(ctrl, "OnCategoryRefresh", instance.transform, index);
            return row;
        }

        private static void InvokePrivate(TestCmdUICtrl ctrl, string methodName, params object[] args)
        {
            typeof(TestCmdUICtrl)
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(ctrl, args);
        }

        private static T GetPrivate<T>(TestCmdUICtrl ctrl, string fieldName)
        {
            return (T)typeof(TestCmdUICtrl)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(ctrl);
        }

        private static void SetPrivate<T>(TestCmdUICtrl ctrl, string fieldName, T value)
        {
            typeof(TestCmdUICtrl)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(ctrl, value);
        }

        private static int GetPrivateEnumerableCount(TestCmdUICtrl ctrl, string fieldName)
        {
            var value = typeof(TestCmdUICtrl)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(ctrl) as System.Collections.IEnumerable;
            Assert.NotNull(value);
            var count = 0;
            foreach (var _ in value)
            {
                count++;
            }

            return count;
        }

        private static bool IsPrivateDictionaryMapped(TestCmdUICtrl ctrl, string fieldName, object key)
        {
            var dictionary = typeof(TestCmdUICtrl)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(ctrl);
            Assert.NotNull(dictionary);
            var containsKey = dictionary.GetType().GetMethod("ContainsKey", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(containsKey);
            return (bool)containsKey.Invoke(dictionary, new[] { key });
        }

        private static RectTransform GetRect(GameObject gameObject)
        {
            var rect = gameObject.GetComponent<RectTransform>();
            Assert.NotNull(rect, gameObject.name);
            return rect;
        }

        private static TMP_Text FindText(Transform root, string path)
        {
            var child = root.Find(path);
            Assert.NotNull(child, path);
            var text = child.GetComponent<TMP_Text>();
            Assert.NotNull(text, path);
            return text;
        }

        private static float LeftEdge(RectTransform rect)
        {
            return rect.anchoredPosition.x - rect.sizeDelta.x * rect.pivot.x;
        }

        private static float RightEdge(RectTransform rect)
        {
            return rect.anchoredPosition.x + rect.sizeDelta.x * (1f - rect.pivot.x);
        }

        private static bool Intersects(RectTransform a, RectTransform b)
        {
            return LeftEdge(a) < RightEdge(b) &&
                   RightEdge(a) > LeftEdge(b) &&
                   BottomEdge(a) < TopEdge(b) &&
                   TopEdge(a) > BottomEdge(b);
        }

        private static bool IntersectsWorld(RectTransform a, RectTransform b)
        {
            return WorldRect(a).Overlaps(WorldRect(b));
        }

        private static void AssertNodeInside(RectTransform root, RectTransform node)
        {
            var rootLeft = -root.sizeDelta.x * root.pivot.x;
            var rootRight = root.sizeDelta.x * (1f - root.pivot.x);
            var rootBottom = -root.sizeDelta.y * root.pivot.y;
            var rootTop = root.sizeDelta.y * (1f - root.pivot.y);
            Assert.That(LeftEdge(node) + 0.01f, Is.GreaterThanOrEqualTo(rootLeft));
            Assert.That(RightEdge(node) - 0.01f, Is.LessThanOrEqualTo(rootRight));
            Assert.That(BottomEdge(node) + 0.01f, Is.GreaterThanOrEqualTo(rootBottom));
            Assert.That(TopEdge(node) - 0.01f, Is.LessThanOrEqualTo(rootTop));
        }

        private static void AssertNodeInsideWorld(RectTransform root, RectTransform node)
        {
            var rootRect = WorldRect(root);
            var nodeRect = WorldRect(node);
            Assert.That(nodeRect.xMin + 0.01f, Is.GreaterThanOrEqualTo(rootRect.xMin));
            Assert.That(nodeRect.xMax - 0.01f, Is.LessThanOrEqualTo(rootRect.xMax));
            Assert.That(nodeRect.yMin + 0.01f, Is.GreaterThanOrEqualTo(rootRect.yMin));
            Assert.That(nodeRect.yMax - 0.01f, Is.LessThanOrEqualTo(rootRect.yMax));
        }

        private static void AssertInputTextContract(TMP_Text text)
        {
            Assert.NotNull(text);
            var rect = GetRect(text.gameObject);
            Assert.That(rect.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(rect.sizeDelta, Is.EqualTo(new Vector2(840f, 40f)));
            Assert.That(text.fontSize, Is.EqualTo(22f));
            Assert.That(text.enableAutoSizing, Is.False);
            Assert.That(text.enableWordWrapping, Is.False);
            Assert.That(text.alignment, Is.EqualTo(TextAlignmentOptions.MidlineLeft));
        }

        private static float TopEdge(RectTransform rect)
        {
            return rect.anchoredPosition.y + rect.sizeDelta.y * (1f - rect.pivot.y);
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static float BottomEdge(RectTransform rect)
        {
            return rect.anchoredPosition.y - rect.sizeDelta.y * rect.pivot.y;
        }

        private static string SnapshotText(ItemContainer container)
        {
            return string.Join("|", container.Slots
                .OrderBy(pair => pair.Key)
                .Select(pair => $"{pair.Key}:{pair.Value.ItemId}:{pair.Value.Amount}"));
        }

        private static global::Tables LoadGeneratedTables()
        {
            return new global::Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", $"{name}.bytes"))));
        }

        private static void AssertInvalidCatalog(
            IReadOnlyList<TestCmdCategoryDefinition> categories,
            IReadOnlyList<TestCmdEntryDefinition> entries,
            string expectedError)
        {
            var provider = new LubanRuntimeConfigProvider(() => new global::Tables(name =>
            {
                if (name == "tbtestcmdcategory")
                {
                    return BuildCategoryBytes(categories);
                }

                if (name == "tbtestcmdentry")
                {
                    return BuildEntryBytes(entries);
                }

                return new ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", $"{name}.bytes")));
            }));

            Assert.That(provider.TryGetTestCmdCatalog(out _, out _, out var error), Is.False);
            Assert.That(error, Does.Contain(expectedError));
        }

        private static void AssertDuplicateCatalogIdRejected(
            IReadOnlyList<TestCmdCategoryDefinition> categories,
            IReadOnlyList<TestCmdEntryDefinition> entries,
            string expectedId)
        {
            var provider = new LubanRuntimeConfigProvider(() => new global::Tables(name =>
            {
                if (name == "tbtestcmdcategory")
                {
                    return BuildCategoryBytes(categories);
                }

                if (name == "tbtestcmdentry")
                {
                    return BuildEntryBytes(entries);
                }

                return new ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", $"{name}.bytes")));
            }));

            Assert.That(provider.TryGetTestCmdCatalog(out _, out _, out var error), Is.False);
            Assert.That(error, Does.Contain(expectedId));
        }

        private static ByteBuf BuildCategoryBytes(IReadOnlyList<TestCmdCategoryDefinition> categories)
        {
            var buffer = new ByteBuf();
            buffer.WriteSize(categories.Count);
            foreach (var row in categories)
            {
                buffer.WriteString(row.Id);
                buffer.WriteInt(row.Order);
                buffer.WriteString(row.LabelText);
            }

            return new ByteBuf(buffer.CopyData());
        }

        private static ByteBuf BuildEntryBytes(IReadOnlyList<TestCmdEntryDefinition> entries)
        {
            var buffer = new ByteBuf();
            buffer.WriteSize(entries.Count);
            foreach (var row in entries)
            {
                buffer.WriteString(row.Id);
                buffer.WriteString(row.CategoryId);
                buffer.WriteInt(row.Order);
                buffer.WriteString(row.LabelText);
                buffer.WriteString(row.CommandTemplate);
                buffer.WriteString(row.DescriptionText);
                buffer.WriteString(row.UsageText);
                buffer.WriteString(row.ExampleText);
            }

            return new ByteBuf(buffer.CopyData());
        }

        private static void AssertRequiredChineseFontCoverage()
        {
            var required = new[] { 20196, 21313, 28385 };
            var charset = File.ReadAllText(Path.Combine(Application.dataPath, "Game/Fonts/3500hz.txt"));
            var charsetCodePoints = EnumerateCodePoints(charset).ToHashSet();
            foreach (var codePoint in required)
            {
                Assert.That(charsetCodePoints, Does.Contain(codePoint), $"3500hz.txt missing U+{codePoint:X4}");
            }

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/Game/Fonts/Chinese/SourceHanSansCN-Normal SDF.asset");
            Assert.NotNull(font);
            var fontCodePoints = font.characterTable.Select(item => (int)item.unicode).ToHashSet();
            foreach (var codePoint in required)
            {
                Assert.That(fontCodePoints, Does.Contain(codePoint), $"TMP font missing U+{codePoint:X4}");
            }
        }

        private static System.Collections.Generic.IEnumerable<int> EnumerateCodePoints(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                yield break;
            }

            for (var index = 0; index < source.Length; index++)
            {
                var current = source[index];
                if (char.IsHighSurrogate(current) &&
                    index < source.Length - 1 &&
                    char.IsLowSurrogate(source[index + 1]))
                {
                    yield return char.ConvertToUtf32(current, source[index + 1]);
                    index++;
                    continue;
                }

                if (!char.IsSurrogate(current))
                {
                    yield return current;
                }
            }
        }

        private static G2C_GetServerTestCmdCatalog ValidServerCatalog()
        {
            G2C_GetServerTestCmdCatalog response = G2C_GetServerTestCmdCatalog.Create();
            response.Categories.Add(new ServerTestCmdCategoryInfo
            {
                Id = "server.general",
                Order = 500,
                LabelText = "服务器"
            });
            response.Entries.Add(new ServerTestCmdEntryInfo
            {
                Id = "server.ping",
                CategoryId = "server.general",
                Order = 100,
                LabelText = "Ping",
                CommandTemplate = "$ping",
                DescriptionText = "检查服务器响应。",
                UsageText = "用法：$ping",
                ExampleText = "示例：$ping"
            });
            return response;
        }

        private static void PublishAuthenticated(OnlineOperationState operationState = OnlineOperationState.Idle)
        {
            Game.Interface.GetModel<OnlineAccountModel>().Publish(new OnlineAccountSnapshot(
                OnlineConnectionState.Authenticated,
                operationState,
                OnlineAccountResultCode.Success,
                "test-user",
                1,
                1,
                1,
                false,
                "test-user"));
        }

        private static G2C_ExecuteServerTestCmd PingResponse()
        {
            return new G2C_ExecuteServerTestCmd
            {
                Recognized = true,
                Success = true,
                ResultCode = (int)TestCmdResultCode.ServerCommandComplete,
                Message = "pong",
                ServerTime = 123
            };
        }

        private sealed class ControllableNetwork : INetworkRuntime
        {
            private readonly List<PendingNetworkCall> calls = new List<PendingNetworkCall>();

            public NetworkConnectionState State = NetworkConnectionState.Disconnected;
            public bool IsInitialized => true;
            public NetworkConnectionState ConnectionState => State;
            public int ConnectionGeneration => 1;

            public void Initialize() { }
            public void Update() { }
            public Task ConnectAsync(NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public async Task<TResponse> CallAsync<TResponse>(
                IRequest request,
                int timeoutMilliseconds,
                CancellationToken cancellationToken)
                where TResponse : class, IResponse
            {
                var call = new PendingNetworkCall(request, cancellationToken);
                calls.Add(call);
                return (TResponse)await call.Task;
            }

            public IReadOnlyList<PendingNetworkCall> Pending<TRequest>() where TRequest : class, IRequest
            {
                return calls.Where(call => !call.IsCompleted && call.Request is TRequest).ToArray();
            }

            public PendingNetworkCall SinglePending<TRequest>() where TRequest : class, IRequest
            {
                return Pending<TRequest>().Single();
            }

            public IDisposable RegisterPushHandler<TMessage>(Action<TMessage> handler)
                where TMessage : class, IMessage => NoopDisposable.Instance;

            public IDisposable RegisterDisconnectedHandler(Action<NetworkDisconnected> handler) =>
                NoopDisposable.Instance;

            public void Disconnect(NetworkDisconnectReason reason) => State = NetworkConnectionState.Disconnected;
            public void Dispose() => State = NetworkConnectionState.Disposed;
        }

        private sealed class PendingNetworkCall
        {
            private readonly TaskCompletionSource<IResponse> completion = new TaskCompletionSource<IResponse>();
            private readonly CancellationTokenRegistration registration;

            public PendingNetworkCall(IRequest request, CancellationToken cancellationToken)
            {
                Request = request;
                registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            }

            public IRequest Request { get; }
            public Task<IResponse> Task => completion.Task;
            public bool IsCompleted => completion.Task.IsCompleted;
            public bool IsCanceled => completion.Task.IsCanceled;

            public bool Complete(IResponse response)
            {
                registration.Dispose();
                return completion.TrySetResult(response);
            }

            public bool Fail(Exception exception)
            {
                registration.Dispose();
                return completion.TrySetException(exception);
            }
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new NoopDisposable();
            public void Dispose() { }
        }

        private sealed class TestConfigProvider : IRuntimeConfigProvider
        {
            public bool TryGetItem(int id, out ItemDefinition value, out string error)
            {
                switch (id)
                {
                    case ItemIds.MetalParts:
                        value = new ItemDefinition(id, "Metal Parts", "Metal Parts", "item.metal_parts.name", "item.metal_parts.desc", string.Empty, global::ItemType.Material, 5, global::ItemUseEffect.None, 0);
                        error = null;
                        return true;
                    case ItemIds.Cloth:
                        value = new ItemDefinition(id, "Cloth", "Cloth", "item.cloth.name", "item.cloth.desc", string.Empty, global::ItemType.Material, 5, global::ItemUseEffect.None, 0);
                        error = null;
                        return true;
                    default:
                        value = default;
                        error = "missing item";
                        return false;
                }
            }

            public bool TryGetItems(out System.Collections.Generic.IReadOnlyList<ItemDefinition> values, out string error)
            {
                values = new[]
                {
                    new ItemDefinition(ItemIds.MetalParts, "Metal Parts", "Metal Parts", "item.metal_parts.name", "item.metal_parts.desc", string.Empty, global::ItemType.Material, 5, global::ItemUseEffect.None, 0),
                    new ItemDefinition(ItemIds.Cloth, "Cloth", "Cloth", "item.cloth.name", "item.cloth.desc", string.Empty, global::ItemType.Material, 5, global::ItemUseEffect.None, 0)
                };
                error = null;
                return true;
            }

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = new MapAssetDefinition(id, id);
                error = null;
                return true;
            }

            public bool TryGetMapSpawns(string mapId, out System.Collections.Generic.IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = Array.Empty<MapSpawnDefinition>();
                error = null;
                return true;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                value = default;
                error = "missing player";
                return false;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                value = default;
                error = "missing enemy";
                return false;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                value = default;
                error = "missing loot";
                return false;
            }

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
            {
                value = default;
                error = "missing extraction";
                return false;
            }

            public bool TryGetTestCmdCatalog(
                out System.Collections.Generic.IReadOnlyList<TestCmdCategoryDefinition> categories,
                out System.Collections.Generic.IReadOnlyList<TestCmdEntryDefinition> entries,
                out string error)
            {
                categories = TestCatalogCategories;
                entries = TestCatalogEntries;
                error = null;
                return true;
            }
        }

        private static readonly TestCmdCategoryDefinition[] TestCatalogCategories =
        {
            new TestCmdCategoryDefinition("inventory-item", 100, "背包物品"),
            new TestCmdCategoryDefinition("player-condition", 200, "玩家状态"),
            new TestCmdCategoryDefinition("player-attribute", 300, "玩家属性"),
            new TestCmdCategoryDefinition("network-diagnostics", 400, "网络诊断")
        };

        private static readonly TestCmdEntryDefinition[] TestCatalogEntries =
        {
            Entry("inventory.clone", "inventory-item", 100, "#clone ", "向当前玩家背包加入物品。", "添加物品", "语法：#clone <ItemId> <Amount>", "示例：#clone 3 5"),
            Entry("inventory.clear-bag", "inventory-item", 200, "#clear bag", "清空当前玩家背包。", "清空背包", "语法：#clear bag", "示例：#clear bag"),
            Entry("condition.advance-time", "player-condition", 100, "#advance time ", "推进游戏分钟，用于结算能量、远征时间与能量耗尽伤害。", "推进时间", "语法：#advance time <游戏分钟>", "示例：#advance time 60"),
            Entry("condition.unitbar", "player-condition", 200, "#unitbar ", "开启或关闭存活敌人的头顶生命条。", "敌人血条", "语法：#unitbar on / #unitbar off", "示例：#unitbar on"),
            Entry("attribute.set-hp", "player-attribute", 100, "#set hp ", "设置当前生命：set hp <整数>。", "设置当前生命", "语法：#set hp <整数>", "示例：#set hp 80"),
            Entry("attribute.set-power", "player-attribute", 200, "#set power ", "设置当前能量：set power <数值>。", "设置当前能量", "语法：#set power <数值>", "示例：#set power 80"),
            Entry("attribute.set-tili", "player-attribute", 300, "#set tili ", "设置当前体力：set tili <数值>。", "设置当前体力", "语法：#set tili <数值>", "示例：#set tili 80"),
            Entry("attribute.set-maxhp", "player-attribute", 400, "#set maxhp ", "设置最大生命：set maxhp <正整数>。", "设置最大生命", "语法：#set maxhp <正整数>", "示例：#set maxhp 120"),
            Entry("attribute.set-movespeed", "player-attribute", 500, "#set movespeed ", "设置移动速度：set movespeed <正数>。", "设置移动速度", "语法：#set movespeed <正数>", "示例：#set movespeed 6"),
            Entry("network.netlog-on", "network-diagnostics", 100, "#netlog on", "开启客户端网络诊断日志，Unity Console 和运行日志界面会出现 [Network] 连接、发送、接收、RPC 完成、超时和断开信息。", "开启网络日志", "用法：#netlog on", "示例：#netlog on"),
            Entry("network.netlog-off", "network-diagnostics", 200, "#netlog off", "关闭客户端网络诊断日志。", "关闭网络日志", "用法：#netlog off", "示例：#netlog off")
        };

        private static TestCmdEntryDefinition Entry(
            string id,
            string categoryId,
            int order,
            string commandTemplate,
            string descriptionText,
            string labelText = null,
            string usageText = null,
            string exampleText = null)
        {
            return new TestCmdEntryDefinition(
                id,
                categoryId,
                order,
                labelText ?? id ?? "未命名指令",
                commandTemplate,
                descriptionText,
                usageText ?? "用法：未配置",
                exampleText ?? "示例：未配置");
        }

        private sealed class LocalizationTestLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : UnityEngine.Object
            {
                switch (path)
                {
                    case LocalizationLanguages.LanguagesAddress:
                        return new TextAsset("{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"简体中文\"},{\"code\":\"en-US\",\"label\":\"English\"}]}") as T;
                    case "zh-CN.json":
                    case "en-US.json":
                        return new TextAsset(File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Localization", path))) as T;
                    default:
                        return null;
                }
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
            {
                var asset = LoadAsset<T>(path);
                onLoaded?.Invoke(asset);
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }
    }
}




