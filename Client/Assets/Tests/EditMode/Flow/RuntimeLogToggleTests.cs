using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class RuntimeLogToggleTests
    {
        private GameObject runtimeLogPrefab;

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            UIHelper.ResetForTests();
            ResourceLoader.SetLoaderForTests(new MinimalLocalizationLoader());
            InstallMinimalArchitecture();
            CreateUIRoot();
            runtimeLogPrefab = CreateRuntimeLogPrefab();
            UIHelper.SetPrefabLoaderForTests(path => path.Contains("RuntimeLog") ? runtimeLogPrefab : null);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(runtimeLogPrefab);
            UIHelper.ResetForTests();
            ResetArchitecture();
            ResourceLoader.ResetForTests();
            foreach (var ui in EditModeSceneIsolation.FindObjectsInActiveTestScene<RuntimeLogUI>())
            {
                UnityEngine.Object.DestroyImmediate(ui.gameObject);
            }
            EditModeSceneIsolation.DestroyNamedObjectsInActiveTestScene("UIRoot", "EventSystem");
        }

        [Test]
        public void RuntimeLogHotKeyTogglesVisibility()
        {
            var hotKeySource = File.ReadAllText(Path.Combine(ProjectRoot(), "Assets/Game/Scripts/System/Input/HotKeySystem.cs"));
            Assert.That(hotKeySource, Does.Contain("HotKeyActionIds.ToggleRuntimeLog"));
            Assert.That(hotKeySource, Does.Contain("UIHelper.ShowUI<RuntimeLogUICtrl>()"));
            Assert.That(hotKeySource, Does.Contain("UIHelper.HideUI<RuntimeLogUICtrl>()"));
            Assert.That(HotKeyDefaults.F1, Is.EqualTo("<Keyboard>/f1"));
        }

        [Test]
        public void RuntimeLogActionIsUnavailableWhenNotRegistered()
        {
            var registry = new HotKeyActionRegistry();

            Assert.IsFalse(registry.TryExecute(new[] { HotKeyActionIds.ToggleRuntimeLog }));
        }

        [Test]
        public void RuntimeLogPrefabUsesConfirmedHeaderFilterSummaryAndDetailLayout()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Diagnostics/RuntimeLog.prefab");
            Assert.That(prefab, Is.Not.Null);
            var rect = (RectTransform)prefab.transform;
            var ui = prefab.GetComponent<RuntimeLogUI>();
            var titleText = FindText(prefab.transform, "TitleText");

            Assert.That(rect.sizeDelta, Is.EqualTo(new Vector2(900f, 640f)));
            AssertButton(ui.BtnCopy, 96f, 36f);
            AssertButton(ui.BtnExport, 96f, 36f);
            AssertButton(ui.BtnClear, 96f, 36f);
            AssertButton(ui.BtnClose, 96f, 36f);
            AssertFilterButton(ui.BtnAll);
            AssertFilterButton(ui.BtnNormal);
            AssertFilterButton(ui.BtnWarning);
            AssertFilterButton(ui.BtnError);
            Assert.That(ui.LogList.viewport.rect.size, Is.EqualTo(new Vector2(812f, 318f)));
            Assert.That(ui.LogList.viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(ui.LogList.prefabSource.poolSize, Is.EqualTo(4));
            Assert.That(ui.LogList.content.GetComponent<VerticalLayoutGroup>().spacing, Is.EqualTo(6f));
            Assert.That(ui.DetailScrollRect.viewport.rect.size, Is.EqualTo(new Vector2(812f, 142f)));
            Assert.That(ui.DetailScrollRect.viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(ui.DetailText, Is.Not.Null);

            var keys = prefab.GetComponentsInChildren<LocalizedText>(true).Select(item => item.Key).ToArray();
            Assert.That(keys.Any(item => item.StartsWith("ui.runtime_log.", StringComparison.Ordinal)), Is.False);
            Assert.That(titleText.text, Is.EqualTo("运行日志"));
            Assert.That(ui.BtnAll.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("全部"));
            Assert.That(ui.BtnNormal.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("普通"));
            Assert.That(ui.BtnWarning.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("警告"));
            Assert.That(ui.BtnError.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("错误"));
            Assert.That(ui.BtnCopy.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("复制"));
            Assert.That(ui.BtnExport.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("导出"));
            Assert.That(ui.BtnClear.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("清空"));
        }

        [Test]
        public void FilterButtonsAreMutuallyExclusiveAndErrorIncludesAssertAndException()
        {
            var ctrl = ShowRuntimeLog();
            var ui = GetBaseUI<RuntimeLogUI>(ctrl);
            var model = Game.Interface.GetModel<RuntimeLogModel>();
            model.AddRange(new[]
            {
                Entry(LogType.Log, "normal"),
                Entry(LogType.Warning, "warning"),
                Entry(LogType.Error, "error"),
                Entry(LogType.Assert, "assert"),
                Entry(LogType.Exception, "exception")
            });

            Assert.That(ui.LogList.totalCount, Is.EqualTo(5));
            AssertSelected(ui, ui.BtnAll);

            ui.BtnWarning.onClick.Invoke();
            Assert.That(ui.LogList.totalCount, Is.EqualTo(1));
            Assert.That(ui.DetailText.text, Does.Contain("warning"));
            AssertSelected(ui, ui.BtnWarning);

            ui.BtnError.onClick.Invoke();
            Assert.That(ui.LogList.totalCount, Is.EqualTo(3));
            Assert.That(ui.DetailText.text, Does.Contain("exception"));
            AssertSelected(ui, ui.BtnError);

            ctrl.Hide();
            ctrl.Show();
            ui = GetBaseUI<RuntimeLogUI>(ctrl);
            Assert.That(ui.LogList.totalCount, Is.EqualTo(3));
            Assert.That(GetCurrentFilter(ctrl).ToString(), Is.EqualTo("Error"));
        }

        [Test]
        public void DestroyedViewReloadDefaultsToAll()
        {
            var ctrl = ShowRuntimeLog();
            var ui = GetBaseUI<RuntimeLogUI>(ctrl);
            Game.Interface.GetModel<RuntimeLogModel>().AddRange(new[]
            {
                Entry(LogType.Log, "normal"),
                Entry(LogType.Warning, "warning"),
                Entry(LogType.Error, "error")
            });
            ui.BtnError.onClick.Invoke();
            Assert.That(ui.LogList.totalCount, Is.EqualTo(1));

            UnityEngine.Object.DestroyImmediate(ui.gameObject);
            ctrl.Show();
            ui = GetBaseUI<RuntimeLogUI>(ctrl);
            Assert.That(ui.LogList.totalCount, Is.EqualTo(3));
            Assert.That(GetCurrentFilter(ctrl).ToString(), Is.EqualTo("All"));
            AssertSelected(ui, ui.BtnAll);
        }

        [Test]
        public void SummaryOmitsStackAndDetailShowsFullLatestEntry()
        {
            var ctrl = ShowRuntimeLog();
            var ui = GetBaseUI<RuntimeLogUI>(ctrl);
            var entry = Entry(LogType.Error, "message only", "full stack");
            SetPrivate(ctrl, "visibleEntries", new[] { entry });
            InvokePrivate(ctrl, "ShowAuthoritativeDetail");

            var itemGo = CreateItem();
            InvokePrivate(ctrl, "OnLogItemRefresh", itemGo.transform, 0);
            var item = itemGo.GetComponent<RuntimeLogItemBoxUI>();

            Assert.That(item.TextTime.text, Is.EqualTo(entry.Timestamp.ToString("HH:mm:ss.fff")));
            Assert.That(item.TextType.text, Is.EqualTo(LogType.Error.ToString()));
            Assert.That(item.TextMessage.text, Is.EqualTo("message only"));
            Assert.That(item.TextMessage.text, Does.Not.Contain("full stack"));
            Assert.That(ui.DetailText.text, Does.Contain("message only"));
            Assert.That(ui.DetailText.text, Does.Contain("full stack"));
            UnityEngine.Object.DestroyImmediate(itemGo);
        }

        [Test]
        public void HiddenChangesStayDirtyAndReopenRefreshesCurrentFilter()
        {
            var ctrl = ShowRuntimeLog();
            var ui = GetBaseUI<RuntimeLogUI>(ctrl);
            ui.BtnWarning.onClick.Invoke();
            ctrl.Hide();

            Game.Interface.GetModel<RuntimeLogModel>()
                .Add(new RuntimeLogEntry(DateTime.UnixEpoch, LogType.Warning, "hidden warning", "hidden stack"));

            Assert.That(GetPrivate<bool>(ctrl, "listDirty"), Is.True);
            Assert.That(ui.LogList.totalCount, Is.Zero);

            ctrl.Show();
            ui = GetBaseUI<RuntimeLogUI>(ctrl);
            Assert.That(ui.LogList.totalCount, Is.EqualTo(1));
            Assert.That(ui.DetailText.text, Does.Contain("hidden warning"));
            Assert.That(ui.DetailText.text, Does.Contain("hidden stack"));
            Assert.That(GetCurrentFilter(ctrl).ToString(), Is.EqualTo("Warning"));
        }

        [Test]
        public void RefreshFollowsBottomButPreservesAwayPositionWhileDetailAdvances()
        {
            var ctrl = ShowRuntimeLog();
            var ui = GetBaseUI<RuntimeLogUI>(ctrl);
            var model = Game.Interface.GetModel<RuntimeLogModel>();
            model.AddRange(Enumerable.Range(0, 24)
                .Select(i => Entry(LogType.Log, $"seed {i:00}", $"seed stack {i:00}"))
                .ToArray());
            Assert.That(ui.LogList.totalCount, Is.EqualTo(24));
            Assert.That(ui.DetailText.text, Does.Contain("seed 23"));

            ui.LogList.verticalNormalizedPosition = 0f;
            var bottomBefore = ui.LogList.content.anchoredPosition;
            model.Add(Entry(LogType.Log, "bottom latest", "bottom stack"));
            Assert.That(ui.LogList.totalCount, Is.EqualTo(25));
            Assert.That(ui.LogList.content.anchoredPosition, Is.EqualTo(bottomBefore));
            Assert.That(ui.DetailText.text, Does.Contain("bottom latest"));

            ui.LogList.verticalNormalizedPosition = 1f;
            var before = ui.LogList.content.anchoredPosition;
            model.Add(Entry(LogType.Log, "away latest", "away stack"));
            Assert.That(ui.LogList.content.anchoredPosition, Is.EqualTo(before));
            Assert.That(ui.DetailText.text, Does.Contain("away latest"));

            ui.BtnCopy.onClick.Invoke();
            Assert.That(ui.DetailText.text, Does.Contain("已复制当前筛选日志。"));
            Assert.That(ui.DetailText.text, Does.Contain("away latest"));

            UIHelper.HideUI<RuntimeLogUICtrl>();
            model.Add(Entry(LogType.Log, "hidden latest", "hidden stack"));
            Assert.That(GetPrivate<bool>(ctrl, "listDirty"), Is.True);

            UIHelper.ShowUI<RuntimeLogUICtrl>();
            ui = GetBaseUI<RuntimeLogUI>(ctrl);
            Assert.That(ui.LogList.content.anchoredPosition, Is.EqualTo(before));
            Assert.That(ui.DetailText.text, Does.Not.Contain("已复制当前筛选日志。"));
            Assert.That(ui.DetailText.text, Does.Contain("hidden latest"));
            Assert.That(ui.DetailText.text, Does.Contain("hidden stack"));
        }

        [Test]
        public void DetailContentGrowsAndScrollsToTop()
        {
            var ctrl = ShowRuntimeLog();
            var ui = GetBaseUI<RuntimeLogUI>(ctrl);
            var longStack = string.Join("\n", Enumerable.Range(0, 60).Select(i => $"stack {i}"));
            SetPrivate(ctrl, "visibleEntries", new[] { Entry(LogType.Exception, "long detail", longStack) });
            InvokePrivate(ctrl, "ShowAuthoritativeDetail");

            var content = ui.DetailScrollRect.content;
            var detailRect = (RectTransform)ui.DetailText.transform;
            Assert.That(content.rect.height, Is.GreaterThanOrEqualTo(ui.DetailScrollRect.viewport.rect.height));
            Assert.That(detailRect.rect.height, Is.GreaterThan(0f));
            Assert.That(detailRect.rect.height, Is.EqualTo(content.rect.height - 20f).Within(0.5f));
            Assert.That(ui.DetailScrollRect.verticalNormalizedPosition, Is.EqualTo(1f));
        }

        [Test]
        public void CopyShowsReadableSuccessOrEmptyFeedback()
        {
            var ctrl = ShowRuntimeLog();
            var ui = GetBaseUI<RuntimeLogUI>(ctrl);

            ui.BtnCopy.onClick.Invoke();
            Assert.That(ui.DetailText.text, Does.Contain("当前筛选没有日志，已复制空日志报告。"));
            Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo(RuntimeLogTextFormatter.Format(
                Array.Empty<RuntimeLogEntry>(),
                AppState.Paused,
                "map.test.runtime-log")));

            Game.Interface.GetModel<RuntimeLogModel>().Add(Entry(LogType.Log, "copy me"));
            ui.BtnCopy.onClick.Invoke();
            Assert.That(ui.DetailText.text, Does.Contain("已复制当前筛选日志。"));
            Assert.That(ui.DetailText.text, Does.Contain("copy me"));
            Assert.That(GUIUtility.systemCopyBuffer, Does.Contain("copy me"));
        }

        [Test]
        public void ExportShowsPathAndCatchesFailure()
        {
            var ctrl = ShowRuntimeLog();
            var ui = GetBaseUI<RuntimeLogUI>(ctrl);
            var expectedPath = Path.Combine(Path.GetTempPath(), "RuntimeLogTest.txt");
            SetPrivate(ctrl, "exportLogs", new Func<IEnumerable<RuntimeLogEntry>, AppState, string, DateTime?, string, string>(
                (entries, state, deviceId, since, mapId) => expectedPath));
            Game.Interface.GetModel<RuntimeLogModel>().Add(Entry(LogType.Warning, "export me"));
            ui.BtnExport.onClick.Invoke();
            Assert.That(ui.DetailText.text, Does.Contain(expectedPath));
            Assert.That(ui.DetailText.text, Does.Contain("export me"));
            Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo(expectedPath));

            SetPrivate(ctrl, "exportLogs", new Func<IEnumerable<RuntimeLogEntry>, AppState, string, DateTime?, string, string>(
                (entries, state, deviceId, since, mapId) => throw new IOException("export failed for test")));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("System\\.IO\\.IOException: export failed for test"));
            ui.BtnExport.onClick.Invoke();
            Assert.That(ui.DetailText.text, Does.Contain("导出失败，请查看内部日志。"));
        }

        [Test]
        public void HideShowAfterCopyOrExportFeedbackRestoresAuthoritativeDetail()
        {
            var ctrl = ShowRuntimeLog();
            var ui = GetBaseUI<RuntimeLogUI>(ctrl);
            var expectedPath = Path.Combine(Path.GetTempPath(), "RuntimeLogTest.txt");
            SetPrivate(ctrl, "exportLogs", new Func<IEnumerable<RuntimeLogEntry>, AppState, string, DateTime?, string, string>(
                (entries, state, deviceId, since, mapId) => expectedPath));
            Game.Interface.GetModel<RuntimeLogModel>().AddRange(new[]
            {
                Entry(LogType.Log, "normal detail", "normal stack"),
                Entry(LogType.Warning, "authoritative warning", "authoritative stack")
            });
            ui.BtnWarning.onClick.Invoke();

            ui.BtnCopy.onClick.Invoke();
            Assert.That(ui.DetailText.text, Does.Contain("已复制当前筛选日志。"));
            ctrl.Hide();
            ctrl.Show();
            ui = GetBaseUI<RuntimeLogUI>(ctrl);
            Assert.That(ui.DetailText.text, Does.Not.Contain("已复制当前筛选日志。"));
            Assert.That(ui.DetailText.text, Does.Contain("authoritative warning"));
            Assert.That(ui.DetailText.text, Does.Contain("authoritative stack"));
            AssertSelected(ui, ui.BtnWarning);

            ui.BtnExport.onClick.Invoke();
            Assert.That(ui.DetailText.text, Does.Contain(expectedPath));
            ctrl.Hide();
            ctrl.Show();
            ui = GetBaseUI<RuntimeLogUI>(ctrl);
            Assert.That(ui.DetailText.text, Does.Not.Contain(expectedPath));
            Assert.That(ui.DetailText.text, Does.Contain("authoritative warning"));
            Assert.That(ui.DetailText.text, Does.Contain("authoritative stack"));
            Assert.That(ui.LogList.totalCount, Is.EqualTo(1));
        }

        [Test]
        public void ClearKeepsPendingOwnershipAndRefreshesAfterDrain()
        {
            var ctrl = ShowRuntimeLog();
            var ui = GetBaseUI<RuntimeLogUI>(ctrl);
            var system = Game.Interface.GetSystem<RuntimeLogSystem>();
            var model = Game.Interface.GetModel<RuntimeLogModel>();
            model.Add(Entry(LogType.Log, "visible"));
            system.EnqueueForTests(Entry(LogType.Error, "pending"));
            Assert.That(ui.LogList.totalCount, Is.EqualTo(1));

            ui.BtnClear.onClick.Invoke();
            Assert.That(model.Snapshot(), Is.Empty);
            Assert.That(ui.LogList.totalCount, Is.Zero);

            Assert.That(system.DrainForTests(10), Is.EqualTo(1));
            Assert.That(model.Snapshot().Select(item => item.Message), Is.EqualTo(new[] { "pending" }));
            Assert.That(ui.DetailText.text, Does.Contain("pending"));
        }

        [Test]
        public void RuntimeLogLocalizationKeysAreNotRequired()
        {
            var zh = File.ReadAllText(Path.Combine(ProjectRoot(), "Assets/Game/Config/Localization/zh-CN.json"));
            var en = File.ReadAllText(Path.Combine(ProjectRoot(), "Assets/Game/Config/Localization/en-US.json"));
            Assert.That(zh, Does.Not.Contain("\"ui.runtime_log."));
            Assert.That(en, Does.Not.Contain("\"ui.runtime_log."));

            var generated = File.ReadAllText(Path.Combine(ProjectRoot(), "Assets/Game/Scripts/Utility/Localization/TextsGenerated.cs"));
            Assert.That(generated, Does.Not.Contain("ui.runtime_log."));
            Assert.That(generated, Does.Not.Contain("RuntimeLogCopy"));
            Assert.That(generated, Does.Not.Contain("RuntimeLogExport"));
            Assert.That(generated, Does.Not.Contain("RuntimeLogFilter"));
            Assert.That(generated, Does.Not.Contain("RuntimeLogTitle"));
            Assert.That(generated, Does.Contain("HotkeyToggleRuntimeLog"));
        }

        private RuntimeLogUICtrl ShowRuntimeLog()
        {
            return UIHelper.ShowUI<RuntimeLogUICtrl>();
        }

        private static RuntimeLogEntry Entry(LogType type, string message, string stack = "")
        {
            return new RuntimeLogEntry(DateTime.UnixEpoch.AddSeconds(message.Length), type, message, stack);
        }

        private static GameObject CreateRuntimeLogPrefab()
        {
            var root = new GameObject("RuntimeLog", typeof(RectTransform));
            ((RectTransform)root.transform).sizeDelta = new Vector2(900f, 640f);
            var ui = root.AddComponent<RuntimeLogUI>();
            AddText(root.transform, "TitleText", "运行日志");
            ui.BtnAll = AddFilterButton(root.transform, "All", "全部");
            ui.BtnNormal = AddFilterButton(root.transform, "Normal", "普通");
            ui.BtnWarning = AddFilterButton(root.transform, "Warning", "警告");
            ui.BtnError = AddFilterButton(root.transform, "Error", "错误");

            var itemPrefab = CreateItem();
            itemPrefab.transform.SetParent(root.transform, false);
            itemPrefab.SetActive(false);
            ui.LogList = AddLoopList(root.transform, "LogList", itemPrefab, 812f, 318f, 4);
            ui.DetailScrollRect = AddDetail(root.transform, out var detailText);
            ui.DetailText = detailText;
            ui.BtnCopy = AddButton(root.transform, "Copy", 96f, 36f, "复制");
            ui.BtnExport = AddButton(root.transform, "Export", 96f, 36f, "导出");
            ui.BtnClear = AddButton(root.transform, "Clear", 96f, 36f, "清空");
            ui.BtnClose = AddButton(root.transform, "Close", 96f, 36f, "关闭");
            return root;
        }

        private static GameObject CreateItem()
        {
            var itemPrefab = new GameObject("RuntimeLogItem", typeof(RectTransform));
            var item = itemPrefab.AddComponent<RuntimeLogItemBoxUI>();
            item.TextTime = AddText(itemPrefab.transform, "Time");
            item.TextType = AddText(itemPrefab.transform, "Type");
            item.TextMessage = AddText(itemPrefab.transform, "Message");
            item.TextMessage.enableWordWrapping = false;
            item.TextMessage.overflowMode = TextOverflowModes.Ellipsis;
            return itemPrefab;
        }

        private static LoopVerticalScrollRect AddLoopList(Transform parent, string name, GameObject itemPrefab, float width, float height, int poolSize)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, height);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(go.transform, false);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup));
            content.transform.SetParent(viewport.transform, false);
            content.GetComponent<VerticalLayoutGroup>().spacing = 6f;

            var list = go.AddComponent<LoopVerticalScrollRect>();
            list.viewport = viewportRect;
            list.content = (RectTransform)content.transform;
            list.horizontal = false;
            list.vertical = true;
            list.prefabSource.prefab = itemPrefab;
            list.prefabSource.poolSize = poolSize;
            return list;
        }

        private static ScrollRect AddDetail(Transform parent, out TMP_Text detailText)
        {
            var scrollGo = new GameObject("DetailScroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(parent, false);
            ((RectTransform)scrollGo.transform).sizeDelta = new Vector2(812f, 142f);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(scrollGo.transform, false);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = (RectTransform)content.transform;
            contentRect.sizeDelta = new Vector2(812f, 142f);
            detailText = AddText(content.transform, "DetailText");
            var detailRect = (RectTransform)detailText.transform;
            detailRect.anchorMin = new Vector2(0f, 0f);
            detailRect.anchorMax = new Vector2(1f, 1f);
            detailRect.offsetMin = new Vector2(10f, 10f);
            detailRect.offsetMax = new Vector2(-10f, -10f);
            detailText.alignment = TextAlignmentOptions.TopLeft;
            detailText.enableWordWrapping = true;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            return scroll;
        }

        private static TMP_Text AddText(Transform parent, string name, string text = "")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            return label;
        }

        private static Button AddButton(Transform parent, string name, float width = 96f, float height = 36f, string textValue = "")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, height);
            var text = AddText(go.transform, "Text", textValue);
            text.alignment = TextAlignmentOptions.Center;
            return go.GetComponent<Button>();
        }

        private static Button AddFilterButton(Transform parent, string name, string textValue)
        {
            var button = AddButton(parent, name, 96f, 34f, textValue);
            var outline = button.gameObject.AddComponent<Outline>();
            outline.enabled = false;
            return button;
        }

        private static TMP_Text FindText(Transform root, string path)
        {
            var child = root.Find(path);
            Assert.That(child, Is.Not.Null, path);
            var text = child.GetComponent<TMP_Text>();
            Assert.That(text, Is.Not.Null, path);
            return text;
        }

        private static T GetBaseUI<T>(BaseUICtrl ctrl) where T : BaseUI
        {
            return (T)typeof(BaseUICtrl)
                .GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(ctrl);
        }

        private static T GetPrivate<T>(object target, string field)
        {
            return (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static object GetCurrentFilter(object target)
        {
            return target.GetType().GetField("currentFilter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static void SetPrivate(object target, string field, object value)
        {
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static void InvokePrivate(object target, string method, params object[] args)
        {
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        }

        private static void AssertSelected(RuntimeLogUI ui, Button selected)
        {
            foreach (var button in new[] { ui.BtnAll, ui.BtnNormal, ui.BtnWarning, ui.BtnError })
            {
                Assert.That(button.GetComponent<Outline>().enabled, Is.EqualTo(button == selected), button.name);
            }
        }

        private static void AssertButton(Button button, float width, float height)
        {
            Assert.That(button, Is.Not.Null);
            Assert.That(((RectTransform)button.transform).sizeDelta, Is.EqualTo(new Vector2(width, height)));
        }

        private static void AssertFilterButton(Button button)
        {
            AssertButton(button, 96f, 34f);
            Assert.That(button.GetComponent<Outline>(), Is.Not.Null);
        }

        private static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static void CreateUIRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(UIRoot));
            foreach (var layerName in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var layer = new GameObject(layerName, typeof(RectTransform));
                layer.transform.SetParent(root.transform, false);
            }

            var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            Assert.NotNull(inputModuleType);
            new GameObject("EventSystem", typeof(EventSystem), inputModuleType).transform.SetParent(root.transform, false);
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private static void InstallMinimalArchitecture()
        {
            var architecture = new Game();
            var localization = new LocalizationUtility(loadConfiguredTables: false);
            localization.LoadFromJson("zh-CN",
                "{}");
            var mapSceneModel = new MapSceneModel();
            mapSceneModel.SetLoaded("map.test.runtime-log", null);

            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterModel(new RuntimeLogModel());
            architecture.RegisterModel(new AppStateModel());
            architecture.RegisterModel(mapSceneModel);
            architecture.RegisterUtility(localization);
            architecture.RegisterSystem(new RuntimeLogSystem());
            architecture.RegisterSystem(new MapSceneSystem(null, null, null, null));

            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, architecture);
        }

        private sealed class MinimalLocalizationLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : UnityEngine.Object
            {
                return path == LocalizationLanguages.LanguagesAddress
                    ? new TextAsset("{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"Chinese\"},{\"code\":\"en-US\",\"label\":\"English\"}]}") as T
                    : null;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
            {
                var asset = LoadAsset<T>(path);
                onLoaded?.Invoke(asset);
                return asset != null ? new ResourceLoadHandle(asset) : ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }
    }
}

